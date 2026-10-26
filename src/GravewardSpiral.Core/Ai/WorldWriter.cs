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
    public static readonly (int En, int Ja) NameLimit = (32, 14), EnterLimit = (240, 100), DocumentLimit = (520, 220), PrologueLimit = (3000, 1200);   // 序文は、画面でスクロールして読める

    /// <summary>迷宮の主の名前と、出会ったとき・倒したときの文の上限(英語, 日本語)。</summary>
    public static readonly (int En, int Ja) BossNameLimit = (40, 16), BossLineLimit = (300, 120);

    /// <summary>酒場の噂1つの上限(英語, 日本語)と、頼める数の上限。</summary>
    public static readonly (int En, int Ja) RumorLimit = (240, 100);
    public const int MaxRumors = 12;

    /// <summary>自由な迷宮の主として選んだ敵(AI に名前と文を書かせる)。シナリオの決まったボスなら null。</summary>
    public static MonsterDefInfo? AutoBoss(GameData data, Save.GameSession? session)
    {
        if (session == null) return null;
        foreach (var boss in data.Bosses.Values.Where(b => b.Auto).OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            if (session.BossPicks.TryGetValue(boss.Id, out var groups) && MonsterDefInfo.Of(data, groups) is { } info) return info;
        }
        return null;
    }

    /// <summary>主に選んだ敵の手がかり(名前・種類・脅威度・従う者)。</summary>
    public sealed record MonsterDefInfo(LocalizedText Name, string Type, double Cr, List<LocalizedText> Escorts)
    {
        /// <summary>主の組(最初が主、残りが従う者)から作る。主の敵が無ければ null。</summary>
        public static MonsterDefInfo? Of(GameData data, IReadOnlyList<Monsters.BossDef.BossGroup> groups) =>
            groups.Count > 0 && data.Monsters.TryGetValue(groups[0].Monster, out var m)
                ? new MonsterDefInfo(m.Name, m.CreatureType, m.Cr, groups.Skip(1).Select(g => data.Monsters.GetValueOrDefault(g.Monster)?.Name).OfType<LocalizedText>().ToList())
                : null;
    }

    /// <summary>途中の階のボス(シナリオエディタで足したボス)の手がかり: id、置いた階(0 が B1)、敵。</summary>
    public sealed record BossBrief(string Id, int Floor, MonsterDefInfo Monster);

    public static AiPromptDef? Prompt(ContentLibrary content) =>
        content.Find(AiPromptDef.Collection, AiPromptDef.World)?.As<AiPromptDef>();

    /// <summary>頼む文を作る。階ごとに、深さとパックのテーマ(雰囲気の手がかり)を渡す。</summary>
    /// <param name="session">自由な迷宮なら、その由来(Origin)を物語の種として渡し、由来で決まった敵の種類と、部品で作った階の名前を手がかりにする。</param>
    /// <param name="boss">主の手がかりを外から渡す(シナリオエディタで、決まった組の主に名前を付けるとき)。省くと自由な迷宮の主だけ。</param>
    /// <param name="others">途中の階のボス(シナリオエディタで足したボス)。名前と出会い・倒したときの文を "bosses" に書かせる。</param>
    /// <param name="rumors">酒場の噂をいくつ書かせるか(シナリオエディタ。0 なら頼まない)。</param>
    public static AiJsonRequest BuildRequest(GameData data, AiPromptDef prompt, int floorCount, string? clanName, ulong seed = 0,
        Save.GameSession? session = null, MonsterDefInfo? boss = null, IReadOnlyList<BossBrief>? others = null, int rumors = 0)
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
        bool askBoss = false;
        if ((boss ?? AutoBoss(data, session)) is { } m)
        {
            user.AppendLine();
            user.AppendLine($"# The master of the deepest floor is a {m.Name.Get("en")} ({m.Type}, challenge {m.Cr:0.##})"
                + (m.Escorts.Count > 0 ? $", attended by {string.Join(", ", m.Escorts.Select(e => e.Get("en")))}" : "") + ".");
            user.AppendLine("Write \"boss\": its name or title as this world knows it (fit the origin; it may be a title rather than a personal name, " +
                "and must not reveal game numbers), \"intro\" (one or two sentences when the party meets it on the deepest floor) and " +
                "\"victory\" (one or two sentences when it falls; something it guarded is left behind on an altar)" +
                (m.Escorts.Count > 0 ? $", and \"escorts\": a name for each of its attendants in the order given ({m.Escorts.Count}; what this world calls them, e.g. a title or a group name)." : ", and \"escorts\" as an empty list."));
            askBoss = true;
        }
        bool askOthers = others is { Count: > 0 };
        if (askOthers)
        {
            user.AppendLine();
            user.AppendLine("# Other bosses guard floors on the way down. For each, write an entry in \"bosses\" with its \"id\" as given, " +
                "\"name\" (its name or title as this world knows it), \"intro\" (one or two sentences when the party meets it) and " +
                "\"victory\" (one or two sentences when it falls), and \"escorts\": a name for each of its attendants in the order given (empty if none). " +
                "Tie each to its floor and to the world's story; do not reveal game numbers.");
            foreach (var o in others!)
                user.AppendLine($"- id \"{o.Id}\" on B{o.Floor + 1}: a {o.Monster.Name.Get("en")} ({o.Monster.Type}, challenge {o.Monster.Cr:0.##})"
                    + (o.Monster.Escorts.Count > 0 ? $", attended by {string.Join(", ", o.Monster.Escorts.Select(e => e.Get("en")))}" : ""));
        }
        rumors = Math.Clamp(rumors, 0, MaxRumors);
        if (rumors > 0)
        {
            user.AppendLine();
            user.AppendLine($"Write \"rumors\": exactly {rumors} things townsfolk say in the tavern (one or two sentences each, in their own voice). " +
                "Hint at the floors, their dangers, the bosses and the world's story without giving everything away; one or two may be half wrong. " +
                "Do not reveal game numbers, and do not wrap them in quotation marks.");
        }
        if (!string.IsNullOrWhiteSpace(clanName))
        {
            user.AppendLine();
            user.AppendLine($"The adventurers' clan is called \"{clanName}\". You may mention it in the prologue, but do not describe its members.");
        }
        user.AppendLine();
        user.AppendLine("Limits (characters): " +
            $"floor name en {NameLimit.En} / ja {NameLimit.Ja}; enter en {EnterLimit.En} / ja {EnterLimit.Ja}; " +
            $"document en {DocumentLimit.En} / ja {DocumentLimit.Ja}; prologue en {PrologueLimit.En} / ja {PrologueLimit.Ja}" +
            (askBoss || askOthers ? $"; boss name en {BossNameLimit.En} / ja {BossNameLimit.Ja}; boss intro and victory en {BossLineLimit.En} / ja {BossLineLimit.Ja}" : "") +
            (rumors > 0 ? $"; rumor en {RumorLimit.En} / ja {RumorLimit.Ja}." : "."));
        var schema = askBoss ? BossSchema : Schema;
        if (askOthers) schema = WithOtherBosses(schema);
        if (rumors > 0) schema = WithRumors(schema);
        return new AiJsonRequest(prompt.System.Trim(), user.ToString(), schema, MaxTokens: 16000);
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

    /// <summary>自由な迷宮の返事の形(主の名前と文も書かせる)。</summary>
    public static readonly JsonElement BossSchema = WithBoss(Schema);

    private static JsonElement WithBoss(JsonElement schema)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["required"]!.AsArray().Add("boss");
        node["properties"]!.AsObject()["boss"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["name", "intro", "victory", "escorts"],
              "properties": {
                "name": { "$ref": "#/$defs/text" },
                "intro": { "$ref": "#/$defs/text" },
                "victory": { "$ref": "#/$defs/text" },
                "escorts": { "type": "array", "items": { "$ref": "#/$defs/text" } }
              }
            }
            """);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>酒場の噂も書かせる形("rumors": [text])。</summary>
    private static JsonElement WithRumors(JsonElement schema)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["required"]!.AsArray().Add("rumors");
        node["properties"]!.AsObject()["rumors"] = System.Text.Json.Nodes.JsonNode.Parse("""{ "type": "array", "items": { "$ref": "#/$defs/text" } }""");
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>途中の階のボスの名前と文も書かせる形("bosses": [{id, name, intro, victory}])。</summary>
    private static JsonElement WithOtherBosses(JsonElement schema)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(schema.GetRawText())!.AsObject();
        node["required"]!.AsArray().Add("bosses");
        node["properties"]!.AsObject()["bosses"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "name", "intro", "victory", "escorts"],
                "properties": {
                  "id": { "type": "string" },
                  "name": { "$ref": "#/$defs/text" },
                  "intro": { "$ref": "#/$defs/text" },
                  "victory": { "$ref": "#/$defs/text" },
                  "escorts": { "type": "array", "items": { "$ref": "#/$defs/text" } }
                }
              }
            }
            """);
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>
    /// 返事を検査して WorldText にする。足りない階は空の項目で埋め、多すぎる階・書き付けは捨てる。
    /// 文字数を超えた項目、英日のどちらかが空の項目も捨てる(その項目はパックの決まった文になる)。
    /// </summary>
    /// <param name="dropped">捨てた項目と理由を足す(シナリオエディタが知らせる)。省けば数えない。</param>
    public static WorldText Parse(JsonElement json, int floorCount, string? model, List<string>? dropped = null)
    {
        LocalizedText? Take(JsonElement e, (int En, int Ja) limit, string what)
        {
            var t = Text(e, limit, out string? why);
            if (t == null && why != null) dropped?.Add($"{what}: {why}");
            return t;
        }
        // 従う者の名前(読めないものは null のまま並びを保つ)
        List<LocalizedText?> Escorts(JsonElement b, string what) =>
            b.TryGetProperty("escorts", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray().Take(8).Select((e, k) => Take(e, BossNameLimit, $"{what} {k + 1}")).ToList()
                : [];
        int floors = Math.Clamp(floorCount, 1, MaxFloors);
        var world = new WorldText { Model = model };
        if (json.ValueKind != JsonValueKind.Object) return Pad(world, floors);
        if (json.TryGetProperty("prologue", out var p)) world.Prologue = Take(p, PrologueLimit, "序文");
        if (json.TryGetProperty("boss", out var b) && b.ValueKind == JsonValueKind.Object)
        {
            var boss = new WorldBossText
            {
                Name = b.TryGetProperty("name", out var bn) ? Take(bn, BossNameLimit, "主の名前") : null,
                Intro = b.TryGetProperty("intro", out var bi) ? Take(bi, BossLineLimit, "主の出会い") : null,
                Victory = b.TryGetProperty("victory", out var bv) ? Take(bv, BossLineLimit, "主の倒したとき") : null,
                Escorts = Escorts(b, "主の従う者"),
            };
            if (boss.Name != null || boss.Intro != null || boss.Victory != null) world.Boss = boss;
        }
        if (json.TryGetProperty("bosses", out var others) && others.ValueKind == JsonValueKind.Array)
            foreach (var o in others.EnumerateArray())
            {
                if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 } id) continue;
                var boss = new WorldBossText
                {
                    Name = o.TryGetProperty("name", out var on) ? Take(on, BossNameLimit, $"ボス {id} の名前") : null,
                    Intro = o.TryGetProperty("intro", out var oi) ? Take(oi, BossLineLimit, $"ボス {id} の出会い") : null,
                    Victory = o.TryGetProperty("victory", out var ov) ? Take(ov, BossLineLimit, $"ボス {id} の倒したとき") : null,
                    Escorts = Escorts(o, $"ボス {id} の従う者"),
                };
                if (boss.Name != null || boss.Intro != null || boss.Victory != null) world.OtherBosses[id] = boss;
            }
        if (json.TryGetProperty("rumors", out var said) && said.ValueKind == JsonValueKind.Array)
            world.Rumors = said.EnumerateArray().Take(MaxRumors).Select((e, k) => Take(e, RumorLimit, $"噂 {k + 1}")).OfType<LocalizedText>().ToList();
        if (json.TryGetProperty("floors", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var f in list.EnumerateArray().Take(floors))
            {
                string at = $"B{++index}";
                var floor = new WorldFloorText();
                if (f.ValueKind == JsonValueKind.Object)
                {
                    if (f.TryGetProperty("name", out var n)) floor.Name = Take(n, NameLimit, at + " の名前");
                    if (f.TryGetProperty("enter", out var e)) floor.Enter = Take(e, EnterLimit, at + " の入ったとき");
                    if (f.TryGetProperty("documents", out var docs) && docs.ValueKind == JsonValueKind.Array)
                        floor.Documents = docs.EnumerateArray().Select((d, k) => Take(d, DocumentLimit, $"{at} の書き付け {k + 1}")).OfType<LocalizedText>().Take(DocumentsPerFloor).ToList();
                }
                world.Floors.Add(floor);
            }
        }
        return Pad(world, floors);
    }

    /// <summary>頼んで、検査した結果を返す。失敗したら null と理由。</summary>
    public static async Task<(WorldText? World, string? Error)> WriteAsync(IAiClient client, GameData data, AiPromptDef prompt,
        int floorCount, string? clanName, CancellationToken cancel, ulong seed = 0, Save.GameSession? session = null, MonsterDefInfo? boss = null,
        List<string>? dropped = null, IReadOnlyList<BossBrief>? others = null, int rumors = 0)
    {
        var reply = await client.GenerateJsonAsync(BuildRequest(data, prompt, floorCount, clanName, seed, session, boss, others, rumors), cancel);
        if (reply.Json is not { } json) return (null, reply.Error);
        return (Parse(json, floorCount, client.Model, dropped), null);
    }

    private static WorldText Pad(WorldText world, int floors)
    {
        while (world.Floors.Count < floors) world.Floors.Add(new WorldFloorText());
        return world;
    }

    private static LocalizedText? Text(JsonElement e, (int En, int Ja) limit) => Text(e, limit, out _);

    /// <summary>英日の組を読む。読めなければ null と理由(空、長すぎる)。</summary>
    private static LocalizedText? Text(JsonElement e, (int En, int Ja) limit, out string? why)
    {
        why = null;
        if (e.ValueKind != JsonValueKind.Object)
        {
            why = "形が違う";
            return null;
        }
        string en = Clean(e.TryGetProperty("en", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null);
        string ja = Clean(e.TryGetProperty("ja", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null);
        why = en.Length == 0 || ja.Length == 0 ? "英語か日本語が空"
            : ja.Length > limit.Ja ? $"日本語が長すぎる({ja.Length} 字、上限 {limit.Ja})"
            : en.Length > limit.En ? $"英語が長すぎる({en.Length} 字、上限 {limit.En})"
            : null;
        return why == null ? LocalizedText.Of(en, ja) : null;
    }

    /// <summary>前後の空白と、改行以外の制御文字を除く。</summary>
    private static string Clean(string? s) =>
        new string((s ?? "").Where(c => c == '\n' || !char.IsControl(c)).ToArray()).Trim();
}
