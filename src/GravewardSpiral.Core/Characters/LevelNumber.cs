using System.Text.Json;
using System.Text.Json.Serialization;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Core.Characters;

/// <summary>
/// クラスと種族のデータに書く数(docs/class-data-design.md の「数の書き方」)。決まった 3 つの形のどれかで、計算式は書けない。
/// 数(<c>2</c>)、レベルごと(<c>{ "by_level": { "1": 2, "9": 3 } }</c>。そのレベル以下の一番大きなキーの値)、
/// 決まった組み合わせ(<c>{ "base": 1, "class_level": 0.5, "ability": "cha", "round": "floor", "min": 1 }</c>。
/// 各項を足し、round で整数にし、min・max を当てる)。
/// </summary>
[JsonConverter(typeof(LevelNumberConverter))]
public sealed class LevelNumber
{
    /// <summary>決まった組み合わせに書ける項目。足すのはコアの変更とする。</summary>
    public static readonly string[] ComboKeys = ["base", "class_level", "total_level", "ability", "proficiency", "round", "min", "max"];

    public int? Value { get; init; }
    public SortedDictionary<int, double>? ByLevel { get; init; }
    public double Base { get; init; }
    public double ClassLevel { get; init; }
    public double TotalLevel { get; init; }
    public Ability? Ability { get; init; }
    public double Proficiency { get; init; }

    /// <summary>true なら切り上げ(正の無限大の側)、false なら切り捨て(負の無限大の側)。</summary>
    public bool Ceil { get; init; }
    public int? Min { get; init; }
    public int? Max { get; init; }

    public static LevelNumber Of(int value) => new() { Value = value };

    /// <summary>その人の値。level は数える出どころのレベル(クラスならそのクラスのレベル。by_level と class_level はこれで引く)。</summary>
    public int Eval(Character c, int level) => (int)Math.Floor(EvalValue(c, level));

    /// <summary>mod を渡すと、ability の修正をそれで引く(戦闘の今の能力値。装備と水薬を入れたもの)。省けば素の能力値。</summary>
    public int Eval(Character c, int level, Func<Ability, int>? mod) => (int)Math.Floor(EvalValue(c, level, mod));

    /// <summary>小数のままの値(by_level に 0.5 のような数を書いたとき。アンデッドの破壊の脅威度など)。決まった組み合わせは整数に丸めたもの。</summary>
    public double EvalValue(Character c, int level, Func<Ability, int>? mod = null)
    {
        if (Value is int v) return v;
        if (ByLevel != null)
        {
            double got = 0;
            foreach (var (k, x) in ByLevel)
            {
                if (k > level) break;
                got = x;
            }
            return got;
        }
        double sum = Base + ClassLevel * level + TotalLevel * c.Level + Proficiency * c.ProficiencyBonus
            + (Ability is { } a ? mod?.Invoke(a) ?? c.Scores.Mod(a) : 0);
        int n = (int)(Ceil ? Math.Ceiling(sum) : Math.Floor(sum));
        if (Min is int min) n = Math.Max(min, n);
        if (Max is int max) n = Math.Min(max, n);
        return n;
    }
}

/// <summary>LevelNumber の読み書き。知らない項目、能力値でない ability、floor か ceil でない round、min > max は読めないものとして落とす。</summary>
public sealed class LevelNumberConverter : JsonConverter<LevelNumber>
{
    public override LevelNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return LevelNumber.Of(reader.GetInt32());
        using var doc = JsonDocument.ParseValue(ref reader);
        var o = doc.RootElement;
        if (o.ValueKind != JsonValueKind.Object) throw new JsonException("数は、数か、by_level か、決まった組み合わせのオブジェクトで書きます");
        if (o.TryGetProperty("by_level", out var table))
        {
            if (o.EnumerateObject().Count() != 1) throw new JsonException("by_level と、ほかの項目は一緒に書けません");
            var by = new SortedDictionary<int, double>();
            foreach (var p in table.EnumerateObject())
            {
                if (!int.TryParse(p.Name, out int lv) || lv < 1 || lv > 20) throw new JsonException($"by_level のキー '{p.Name}' は 1〜20 の数です");
                by[lv] = p.Value.GetDouble();
            }
            return new LevelNumber { ByLevel = by };
        }
        foreach (var p in o.EnumerateObject())
        {
            if (!LevelNumber.ComboKeys.Contains(p.Name))
                throw new JsonException($"数の項目 '{p.Name}' はありません({string.Join(" / ", LevelNumber.ComboKeys)}、または by_level)");
        }
        double D(string key) => o.TryGetProperty(key, out var e) ? e.GetDouble() : 0;
        int? I(string key) => o.TryGetProperty(key, out var e) ? e.GetInt32() : null;
        Ability? ability = null;
        if (o.TryGetProperty("ability", out var ab))
        {
            try { ability = Abilities.Parse(ab.GetString() ?? ""); }
            catch (FormatException ex) { throw new JsonException(ex.Message); }
        }
        string round = o.TryGetProperty("round", out var r) ? r.GetString() ?? "" : "floor";
        if (round is not ("floor" or "ceil")) throw new JsonException($"round '{round}' は floor か ceil です");
        var n = new LevelNumber
        {
            Base = D("base"), ClassLevel = D("class_level"), TotalLevel = D("total_level"), Proficiency = D("proficiency"),
            Ability = ability, Ceil = round == "ceil", Min = I("min"), Max = I("max"),
        };
        if (n.Min > n.Max) throw new JsonException($"min({n.Min})が max({n.Max})より大きい");
        return n;
    }

    public override void Write(Utf8JsonWriter writer, LevelNumber value, JsonSerializerOptions options)
    {
        if (value.Value is int v)
        {
            writer.WriteNumberValue(v);
            return;
        }
        writer.WriteStartObject();
        if (value.ByLevel != null)
        {
            writer.WriteStartObject("by_level");
            foreach (var (k, x) in value.ByLevel) writer.WriteNumber(k.ToString(), x);
            writer.WriteEndObject();
        }
        else
        {
            if (value.Base != 0) writer.WriteNumber("base", value.Base);
            if (value.ClassLevel != 0) writer.WriteNumber("class_level", value.ClassLevel);
            if (value.TotalLevel != 0) writer.WriteNumber("total_level", value.TotalLevel);
            if (value.Ability is { } a) writer.WriteString("ability", a.Code());
            if (value.Proficiency != 0) writer.WriteNumber("proficiency", value.Proficiency);
            if (value.Ceil) writer.WriteString("round", "ceil");
            if (value.Min is int min) writer.WriteNumber("min", min);
            if (value.Max is int max) writer.WriteNumber("max", max);
        }
        writer.WriteEndObject();
    }
}
