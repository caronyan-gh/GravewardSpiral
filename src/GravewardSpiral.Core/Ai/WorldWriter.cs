using System.Text;
using System.Text.Json;
using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Combat;
using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Ai;

/// <summary>
/// AI に渡す指示文(パックの ai_prompts.json)。シナリオのパックは同じ id で差し替えられる。
/// system は書き方の決まり、story はそのシナリオの物語の芯(AI はこれに逆らわない味付けだけを書く)。
/// </summary>
public sealed class AiPromptDef
{
    public const string Collection = "ai_prompts";
    public const string World = "world";

    public string Id { get; set; } = "";
    public string System { get; set; } = "";
    public string Story { get; set; } = "";

    /// <summary>着想の言葉(世界ごとに、シードから InspirationCount 個を選んで渡す。由来が毎回似ないように)。省けば渡さない。</summary>
    public List<string> Inspirations { get; set; } = [];

    public const int InspirationCount = 3;
}

/// <summary>
/// 新しい世界の文を AI に書いてもらう(背景の序文、階ごとの名前・入ったときの一文・書き付け)。
/// 数値やルールは渡さず、文だけを書かせる。返事は形と文字数を検査し、通らない項目は捨てる(パックの決まった文を使う)。
/// </summary>
public static class WorldWriter
{
    public const int DocumentsPerFloor = 2;
    public const int MaxFloors = 30;

    /// <summary>項目ごとの文字数の上限(英語, 日本語)。超えたものは途中で切らずに捨てる。</summary>
    public static readonly (int En, int Ja) NameLimit = (32, 14), EnterLimit = (240, 100), DocumentLimit = (520, 220), PrologueLimit = (1300, 520);

    public static AiPromptDef? Prompt(ContentLibrary content) =>
        content.Find(AiPromptDef.Collection, AiPromptDef.World)?.As<AiPromptDef>();

    /// <summary>頼む文を作る。階ごとに、深さとパックのテーマ(雰囲気の手がかり)を渡す。</summary>
    /// <param name="session">自由な迷宮なら、その由来(Origin)を物語の種として渡し、由来で決まった敵の種類と、部品で作った階の名前を手がかりにする。</param>
    public static AiJsonRequest BuildRequest(GameData data, AiPromptDef prompt, int floorCount, string? clanName, ulong seed = 0,
        Save.GameSession? session = null)
    {
        int floors = Math.Clamp(floorCount, 1, MaxFloors);
        var user = new StringBuilder();
        user.AppendLine("# Story core");
        user.AppendLine(prompt.Story.Trim());
        if (session?.Origin is { } origin)
        {
            user.AppendLine();
            user.AppendLine("# This world's origin (build the story on it: keep the place, what went wrong, what lies below and the town's request, and add this world's own details)");
            user.AppendLine(WorldBuilder.Describe(data, origin));
        }
        else if (prompt.Inspirations.Count > 0)
        {
            // 世界のシードで選ぶ(同じ世界なら同じ言葉)
            var rng = new Rng.Pcg32(seed, 41);
            var pool = prompt.Inspirations.ToList();
            var picked = new List<string>();
            while (picked.Count < AiPromptDef.InspirationCount && pool.Count > 0)
            {
                int i = rng.Next(0, pool.Count);
                picked.Add(pool[i]);
                pool.RemoveAt(i);
            }
            user.AppendLine();
            user.AppendLine($"# Inspiration for this world (use them loosely, as seeds for the origin and images): {string.Join(", ", picked)}");
        }
        user.AppendLine();
        user.AppendLine($"# This world has {floors} floors (B1 is the top). Write exactly {floors} entries in \"floors\", in order, each with exactly {DocumentsPerFloor} documents.");
        for (int i = 0; i < floors; i++)
        {
            double depth = floors <= 1 ? 0 : (double)i / (floors - 1);
            var theme = session?.ThemeAt(data, depth) ?? FloorAdvisor.ThemeFor(data, depth);
            var draft = session?.World?.Floor(i);
            var name = draft?.Name ?? theme?.Name;
            var mood = draft?.Enter ?? theme?.Enter;
            user.Append($"- B{i + 1} (depth {depth:0.00})");
            if (name != null) user.Append($", default theme: \"{name.Get("en")}\" / \"{name.Get("ja")}\"");
            if (mood != null) user.Append($", default mood: \"{mood.Get("en")}\"");
            if (theme != null && theme.Types.Count > 0)
                user.Append($", creatures found here: {string.Join(", ", theme.Types.Where(t => t.Key != "*").OrderByDescending(t => t.Value).Take(3).Select(t => t.Key))}");
            user.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(clanName))
        {
            user.AppendLine();
            user.AppendLine($"The adventurers' clan is called \"{clanName}\". You may mention it in the prologue, but do not describe its members.");
        }
        user.AppendLine();
        user.AppendLine("Limits (characters): " +
            $"floor name en {NameLimit.En} / ja {NameLimit.Ja}; enter en {EnterLimit.En} / ja {EnterLimit.Ja}; " +
            $"document en {DocumentLimit.En} / ja {DocumentLimit.Ja}; prologue en {PrologueLimit.En} / ja {PrologueLimit.Ja}.");
        return new AiJsonRequest(prompt.System.Trim(), user.ToString(), Schema, MaxTokens: 16000);
    }

    /// <summary>返事の形(英日の組で書かせる)。</summary>
    public static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["prologue", "floors"],
          "properties": {
            "prologue": { "$ref": "#/$defs/text" },
            "floors": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["name", "enter", "documents"],
                "properties": {
                  "name": { "$ref": "#/$defs/text" },
                  "enter": { "$ref": "#/$defs/text" },
                  "documents": { "type": "array", "items": { "$ref": "#/$defs/text" } }
                }
              }
            }
          },
          "$defs": {
            "text": {
              "type": "object",
              "additionalProperties": false,
              "required": ["en", "ja"],
              "properties": { "en": { "type": "string" }, "ja": { "type": "string" } }
            }
          }
        }
        """).RootElement.Clone();

    /// <summary>
    /// 返事を検査して WorldText にする。足りない階は空の項目で埋め、多すぎる階・書き付けは捨てる。
    /// 文字数を超えた項目、英日のどちらかが空の項目も捨てる(その項目はパックの決まった文になる)。
    /// </summary>
    public static WorldText Parse(JsonElement json, int floorCount, string? model)
    {
        int floors = Math.Clamp(floorCount, 1, MaxFloors);
        var world = new WorldText { Model = model };
        if (json.ValueKind != JsonValueKind.Object) return Pad(world, floors);
        if (json.TryGetProperty("prologue", out var p)) world.Prologue = Text(p, PrologueLimit);
        if (json.TryGetProperty("floors", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in list.EnumerateArray().Take(floors))
            {
                var floor = new WorldFloorText();
                if (f.ValueKind == JsonValueKind.Object)
                {
                    if (f.TryGetProperty("name", out var n)) floor.Name = Text(n, NameLimit);
                    if (f.TryGetProperty("enter", out var e)) floor.Enter = Text(e, EnterLimit);
                    if (f.TryGetProperty("documents", out var docs) && docs.ValueKind == JsonValueKind.Array)
                        floor.Documents = docs.EnumerateArray().Select(d => Text(d, DocumentLimit)).OfType<LocalizedText>().Take(DocumentsPerFloor).ToList();
                }
                world.Floors.Add(floor);
            }
        }
        return Pad(world, floors);
    }

    /// <summary>頼んで、検査した結果を返す。失敗したら null と理由。</summary>
    public static async Task<(WorldText? World, string? Error)> WriteAsync(IAiClient client, GameData data, AiPromptDef prompt,
        int floorCount, string? clanName, CancellationToken cancel, ulong seed = 0, Save.GameSession? session = null)
    {
        var reply = await client.GenerateJsonAsync(BuildRequest(data, prompt, floorCount, clanName, seed, session), cancel);
        if (reply.Json is not { } json) return (null, reply.Error);
        return (Parse(json, floorCount, client.Model), null);
    }

    private static WorldText Pad(WorldText world, int floors)
    {
        while (world.Floors.Count < floors) world.Floors.Add(new WorldFloorText());
        return world;
    }

    private static LocalizedText? Text(JsonElement e, (int En, int Ja) limit)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        string en = Clean(e.TryGetProperty("en", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null);
        string ja = Clean(e.TryGetProperty("ja", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null);
        if (en.Length == 0 || ja.Length == 0 || en.Length > limit.En || ja.Length > limit.Ja) return null;
        return LocalizedText.Of(en, ja);
    }

    /// <summary>前後の空白と、改行以外の制御文字を除く。</summary>
    private static string Clean(string? s) =>
        new string((s ?? "").Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
}
