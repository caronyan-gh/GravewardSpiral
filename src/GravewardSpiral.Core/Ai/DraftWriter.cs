using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// シナリオエディタの AI の補助: イベント・品・モンスターの JSON の下書きを書かせる。
/// AI に渡すのは スキーマ(部品の説明)・ベース(選んだ元の JSON)・希望(作者の一言)と、指してよい id などの手がかり。
/// 書いたものはゲームと同じ検査にかけ、問題があれば問題を添えて1回だけ直させる。作者が見て当てるまで、シナリオには入れない。
/// </summary>
public static class DraftWriter
{
    public enum Kind { Event, Item, Monster }

    /// <summary>
    /// 頼むこと。Base は元の JSON(無ければ新しく作る)。Context は頼み事が変わっても同じ手がかり(標準の id の一覧など)、
    /// Specific はこの頼み事だけの手がかり(使う id、元の写し方など)。Cache なら、指示・スキーマ・Context をプロンプトキャッシュにする。
    /// ItemSchema はイベントのとき、一緒に書いてよい新しい品のスキーマ(空なら新しい品は書かせない)。
    /// </summary>
    public sealed record Brief(Kind Kind, string Schema, string? Base, string Wish, string Context, string Specific = "", bool Cache = false, string ItemSchema = "");

    /// <summary>使ったトークン(書かせた全部の回の合計)。Input はキャッシュを使わずに読んだ分。</summary>
    public sealed record Tokens(long Input, long Output, long CacheRead, long CacheWrite)
    {
        public Tokens Add(AiJsonReply r) => new(Input + r.InputTokens, Output + r.OutputTokens, CacheRead + r.CacheRead, CacheWrite + r.CacheWrite);
    }

    /// <summary>
    /// 書かせた結果。Draft は最後の下書き(読めなければ null)、Note は AI が作ったものの短い説明、
    /// Differences は希望どおりにできなかったところ(代わりにしたもの、作れないもの、自信のない項目)、
    /// Problems は直しきれなかった検査の問題、Error は AI を呼べなかった理由。Items はイベントと一緒に書いた新しい品。
    /// </summary>
    public sealed record Result(JsonObject? Draft, List<JsonObject> Items, string? Note, List<string> Differences, List<string> Problems, string? Error, int Tries, Tokens Used);

    /// <summary>直させる回数(最初の1回のほかに)。</summary>
    public const int Retries = 1;

    /// <summary>返事の形: 下書きの JSON(文字列で) と、作者への短い説明。部品の形は検査で見るので、ここでは決めない。</summary>
    private static readonly JsonElement ReplySchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "json": { "type": "string", "description": "The single JSON object (as text)." },
            "items": {
              "type": "array",
              "items": { "type": "string" },
              "description": "New item objects (each as text) that the event needs and the game does not have. Empty unless the instructions allow new items."
            },
            "note": { "type": "string", "description": "In Japanese, concretely what you made as the author asked: for each part of the request, how you made it, with the numbers (2-4 short sentences). Things done differently go in differences, not here." },
            "differences": {
              "type": "array",
              "items": { "type": "string" },
              "description": "In Japanese, one entry per place where the draft is not exactly what was asked: something the game cannot do (and the closest working option), a field whose effect you are unsure of. Empty if it is exactly as asked."
            }
          },
          "required": ["json", "items", "note", "differences"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    private const string Common = """
        You write pack data for GRAVEWARD SPIRAL, a Wizardry-style dungeon RPG built on the D&D 5e SRD 5.1.
        Return exactly one JSON object as text in "json", using only the field names in the schema (snake_case), a Japanese "note" for the author,
        and "differences": every place where the draft is not exactly what was asked (something the game cannot do,
        a field you are unsure of), one short Japanese sentence each. The author reads "differences" before the JSON, so do not leave them only in the note.
        Leave "items" empty unless the instructions for this kind allow new items.
        The author reads the note as "what was done as you asked": for each part of the request, say concretely how you made it, with the numbers
        (e.g. 失敗すると 1d6 の毒のダメージ(耐久力のセーヴ DC 11 で半分)、成功すると金貨 50 枚). 2-4 short sentences. Do not list differences here.
        Write the note and "differences" in plain words as the player would see the game, without JSON field names or values
        (not "again" or once: "game", but 何度でも話しかけられる / 渡したら二度と起きない).
        If a base is given, start from it: keep its id and everything the request does not ask to change. If there is no base, write a new one with the id given.
        Point only at ids listed in the hints (items, monsters, spells, skills, sounds, events). Never invent ids.
        If the request needs something the game cannot do, do not quietly turn it into a different effect. Leave that part out (names and descriptions
        must not claim it either), and in "differences" say what could not be made and which working option comes closest, so the author can decide.
        Player-facing text is {"en": "...", "ja": "..."}. Japanese is the main language: short, plain, natural sentences; Wizardry mood (grim, terse, a little dry humor).
        English matches the meaning. Write original text (do not copy SRD prose). Do not use proper nouns from other works.
        Abilities are written str, dex, con, int, wis, cha (never "dexterity" or "wisdom").
        Money in text is 金貨 in Japanese (e.g. 金貨 50 枚; never ゴールド) and gold in English.
        Before you reply, re-read your JSON. If you notice a mistake, fix it in the JSON; never leave it and only describe it.
        In "differences", mention only real gaps and uncertainties (something left out, a field whose effect you are not sure of, numbers worth a second look).
        """;

    private static string System(Kind kind) => Common + "\n" + kind switch
    {
        Kind.Event => """
            You write one event (events.json). Use only the condition and step types in the schema; read each type's fields carefully.
            Speaking characters: set "speaker" and wrap the Japanese line in 「」 and the English in double quotes. Narration has no speaker.
            Make it interesting: choices with 2-4 options (one way to leave, usually with "again"), skill or ability checks, risk and reward,
            class- or race-gated options via an option's "when", random outcomes. No instant death.
            If a choice option pays gold (a "gold" step with a negative value), give that option "when": [{"type": "gold", "value": <amount>}].
            Flags and counters you add start with the event id (e.g. "<event id>_opened").
            If you scatter it, keep "weight" between 0.5 and 2. Add a "rumor" only when the request asks for one.
            If the event needs an item the game does not have (a thing to bring back, a key, a reward unique to this event), write it in "items"
            (items.json format, see "Schema for new items"): its id starts with the event id, an event-only item has "no_random": true,
            and the event points at it by that id. Use a listed item instead whenever one fits. Mention each new item in the note.
            An item the party must keep for the event to work (a thing to bring back for a request, a key, a token) is "kind": "key"
            (大事な品: it cannot be dropped or sold, so the request can always be finished). Rewards meant to be used or sold keep their normal kind.
            A non-magic item (a thing to bring back, a key, a plain tool) has "rarity": "mundane"; common, uncommon and so on are for magic items only.
            Size rewards and danger for the level band in the hints (low: gold 20-100, damage 1d6-2d6, DC 11-13; mid: gold 100-500, 3d6-5d6, DC 13-15; high: gold 500-3000, 6d6-10d6, DC 15-18).
            """,
        Kind.Item => """
            You write one item (items.json). If the base is a standard item, write "extends": "<base id>" and only the fields that differ.
            If the base already has "extends", keep it and keep writing only differences.
            A field you write replaces the base's whole field (lists such as passive or properties are not merged), so write the full list when you change one.
            Keep numbers (bonus, damage, price, charges) in line with the similar standard items listed in the hints (same rarity, similar price);
            do not make it better than its rarity allows.
            Follow the hints about "no_random" (event-only item or not).
            A non-magic item has "rarity": "mundane"; common, uncommon and so on are for magic items only.
            """,
        Kind.Monster => """
            You write one monster (monsters.json). Write "extends": "<base id>" (the base monster, or the base's own extends) and only the fields that differ.
            A field you write replaces the base's whole field (lists such as actions are not merged), so write the full list when you change one.
            The effects of an action all apply to its target: a "heal" on an attack heals the one who was hit, not the attacker.
            Regeneration is written with "regenerate" (HP at the start of its turn) and "regenerate_stop" (damage types that stop it for a turn), not with a trait.
            There is no self-heal on hit and no effect on death.
            Keep it in line with the 5e rules for its challenge rating: if you make it stronger or weaker (hp, ac, attacks, damage, spells, resistances),
            change "cr" to match, and say the new CR and why in the note.
            """,
        _ => "",
    };

    /// <summary>
    /// 頼む文を作る。previous と problems があれば、前の下書きと検査の問題を渡して直させる。
    /// 毎回同じ前置き(スキーマと Context)を先に、頼み事ごとに変わるものを後ろに置く(キャッシュは前から同じ部分にしか効かない)。
    /// </summary>
    public static AiJsonRequest BuildRequest(Brief brief, string? previous = null, IReadOnlyList<string>? problems = null)
    {
        var stable = new StringBuilder();
        stable.AppendLine("# Schema (field descriptions are in Japanese)");
        stable.AppendLine(brief.Schema.Trim());
        stable.AppendLine();
        if (brief.ItemSchema.Length > 0)
        {
            stable.AppendLine();
            stable.AppendLine("# Schema for new items (items.json; field descriptions are in Japanese)");
            stable.AppendLine(brief.ItemSchema.Trim());
        }
        stable.AppendLine();
        stable.AppendLine("# Hints");
        stable.AppendLine(brief.Context.Trim());
        var user = new StringBuilder();
        if (!brief.Cache)
        {
            user.Append(stable);
            user.AppendLine();
        }
        user.AppendLine("# Hints for this request");
        user.AppendLine(brief.Specific.Trim());
        user.AppendLine();
        user.AppendLine("# Base");
        user.AppendLine(brief.Base is { Length: > 0 } b ? b.Trim() : "(none: write a new one)");
        user.AppendLine();
        user.AppendLine("# Request from the author");
        user.AppendLine(brief.Wish.Trim());
        if (previous != null)
        {
            user.AppendLine();
            user.AppendLine("# Your previous draft");
            user.AppendLine(previous.Trim());
            user.AppendLine();
            user.AppendLine(FixHeading);
            foreach (var p in problems ?? []) user.AppendLine("- " + p);
        }
        return new AiJsonRequest(System(brief.Kind), user.ToString(), ReplySchema, MaxTokens: 16000, Cached: brief.Cache ? stable.ToString() : null);
    }

    /// <summary>直させるときの見出し(API で直させるときと、チャットへの修正依頼で同じ)。</summary>
    private const string FixHeading = "# Problems the game's checker found (fix all of them; keep the rest, but make names, descriptions and the note match what the fixed JSON really does)";

    /// <summary>
    /// チャットに貼るときの、返事の形(API の structured outputs の代わり)。チャットの AI は下書きだけを返しがちなので、
    /// 先頭でも念を押し、最後に例を付ける。
    /// </summary>
    private const string ChatReply = """
        # How to reply (required)
        Reply with exactly one JSON code block and nothing else. The block is a wrapper object, NOT the draft itself:
        - "draft": the one JSON object you made (the event, item or monster)
        - "items": new item objects the event needs (only when the instructions allow new items; otherwise [])
        - "note": in Japanese, concretely what you made as asked, with the numbers (2-4 short sentences)
        - "differences": a list of short Japanese sentences, one per place where the draft is not exactly what was asked
          (anything you added, changed or left out, and anything the game cannot do). Use [] only if it is exactly as asked.
        Example of the shape:
        ```json
        {"draft": {"id": "example_id", "...": "..."}, "items": [], "note": "ローグがいれば罠を判定なしで外せる。それ以外は敏捷力の判定 DC 12 で、成功すると金貨 50 枚、失敗すると毒の状態 10 分。", "differences": ["失敗したときは金も手に入らないようにしました。"]}
        ```
        Do not reply with the draft alone.
        """;

    /// <summary>チャットに貼る文の先頭に置く念押し(返事の形は最後に書く)。</summary>
    private const string ChatFirst = "Read everything below. Your reply must follow \"# How to reply\" at the very end: a wrapper with \"draft\", \"items\", \"note\" and \"differences\", not the draft alone.";

    /// <summary>
    /// API キーが無い人のための、AI のチャットに貼る文。API で頼むときと同じ指示・スキーマ・手がかり・元・希望に、返事の形を足したもの。
    /// 返ってきた文は ParseChat で読む。
    /// </summary>
    public static string ChatText(Brief brief)
    {
        var request = BuildRequest(brief with { Cache = false });
        return ChatFirst + "\n\n" + request.System.Trim() + "\n\n" + request.User.Trim() + "\n\n" + ChatReply.Trim() + "\n";
    }

    /// <summary>同じチャットに貼る修正依頼: 前の下書き(新しい品があればそれも)と、検査が見つけた問題。</summary>
    public static string FixText(string previous, IReadOnlyList<string> problems)
    {
        var text = new StringBuilder();
        text.AppendLine("# Your previous draft");
        text.AppendLine(previous.Trim());
        text.AppendLine();
        text.AppendLine(FixHeading);
        foreach (var p in problems) text.AppendLine("- " + p);
        text.AppendLine();
        text.Append(ChatReply.Trim());
        return text.ToString();
    }

    /// <summary>前の下書きの文(下書きと、新しい品があればその並び)。直させるときに渡す。</summary>
    public static string Previous(string draft, IReadOnlyList<JsonObject> items) =>
        items.Count == 0 ? draft : draft.Trim() + "\n\n# Your previous new items\n" + new JsonArray(items.Select(i => (JsonNode)i.DeepClone()).ToArray()).ToJsonString();

    /// <summary>
    /// チャットから貼った返事を読む。```json のかたまりか、最初の { から最後の } まで。
    /// {"draft": ..., "items": [...], "note": ..., "differences": [...]} の形でなければ、オブジェクトそのものを下書きとみなし、Wrapped を false にする
    /// (メモと希望と違うところが無いので、画面で注意を出す)。
    /// </summary>
    public static (JsonObject? Draft, List<JsonObject> Items, string? Note, List<string> Differences, string? Error, bool Wrapped) ParseChat(string reply)
    {
        string text = reply;
        var block = Regex.Match(reply, "```(?:json)?\\s*\\n(.*?)```", RegexOptions.Singleline);
        if (block.Success) text = block.Groups[1].Value;
        int start = text.IndexOf('{'), end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return (null, [], null, [], "JSON が見つかりません", false);
        try
        {
            if (JsonNode.Parse(text[start..(end + 1)]) is not JsonObject o) return (null, [], null, [], "JSON の形が、1つのオブジェクトではありません", false);
            if (o["draft"] is not JsonObject draft) return (o, [], null, [], null, false);
            var differences = (o["differences"] as JsonArray)?.Select(x => (string?)x).Where(x => x is { Length: > 0 }).Select(x => x!).ToList() ?? [];
            var items = (o["items"] as JsonArray)?.OfType<JsonObject>().Select(i => i.DeepClone().AsObject()).ToList() ?? [];
            return (draft.DeepClone().AsObject(), items, (string?)o["note"], differences, null, true);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, [], null, [], "JSON を読めません: " + ex.Message, false);
        }
    }

    /// <summary>
    /// 書かせて、検査にかける。問題があれば Retries 回まで直させる。check は下書きと新しい品の問題を返す(空なら通った)。
    /// onTry は AI を呼ぶ前に、何回目か(1 なら作る、2 からは直す)を知らせる(画面に「作成中」「修正中」を出す)。
    /// </summary>
    public static async Task<Result> WriteAsync(IAiClient client, Brief brief, Func<JsonObject, IReadOnlyList<JsonObject>, List<string>> check, CancellationToken cancel,
        Action<int>? onTry = null)
    {
        JsonObject? draft = null;
        List<JsonObject> items = [];
        string? note = null, previous = null;
        List<string> differences = [], problems = [];
        var used = new Tokens(0, 0, 0, 0);
        for (int tries = 1; tries <= 1 + Retries; tries++)
        {
            onTry?.Invoke(tries);
            var reply = await client.GenerateJsonAsync(BuildRequest(brief, previous, problems), cancel);
            used = used.Add(reply);
            if (reply.Json is not { } json) return new Result(draft, items, note, differences, problems, reply.Error, tries, used);
            string text = json.TryGetProperty("json", out var j) && j.ValueKind == JsonValueKind.String ? j.GetString() ?? "" : "";
            note = json.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            differences = Strings(json, "differences");
            var itemTexts = Strings(json, "items");
            previous = text;
            try
            {
                draft = JsonNode.Parse(text) as JsonObject;
                items = itemTexts.Select(t => JsonNode.Parse(t) as JsonObject ?? throw new JsonException("新しい品が、1つのオブジェクトではありません")).ToList();
                previous = Previous(text, items);
                problems = draft == null ? ["JSON の形が、1つのオブジェクトではありません"] : check(draft, items);
            }
            catch (JsonException ex)
            {
                draft = null;
                items = [];
                problems = ["JSON を読めません: " + ex.Message];
            }
            if (problems.Count == 0) return new Result(draft, items, note, differences, problems, null, tries, used);
        }
        return new Result(draft, items, note, differences, problems, null, 1 + Retries, used);
    }

    /// <summary>返事の文字列の並び(空の要素は除く)。</summary>
    private static List<string> Strings(JsonElement json, string name) =>
        json.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Trim().Length > 0).ToList()
            : [];
}