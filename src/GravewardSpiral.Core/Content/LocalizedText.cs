using System.Text.Json;
using System.Text.Json.Serialization;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// データの中の名前や説明。JSON では {"en": "...", "ja": "..."} と書く(文字列1つなら英語として読む)。
/// 指定の言語が無ければ英語を、英語も無ければ最初の言語を出す。
/// </summary>
[JsonConverter(typeof(LocalizedTextConverter))]
public sealed class LocalizedText
{
    public const string English = "en";

    private readonly Dictionary<string, string> _texts;

    public LocalizedText(IDictionary<string, string> texts) =>
        _texts = new Dictionary<string, string>(texts, StringComparer.OrdinalIgnoreCase);

    public static LocalizedText Of(string en, string? ja = null)
    {
        var d = new Dictionary<string, string> { [English] = en };
        if (ja != null) d["ja"] = ja;
        return new LocalizedText(d);
    }

    public IReadOnlyDictionary<string, string> Texts => _texts;

    public bool Has(string language) => _texts.ContainsKey(language);

    public string Get(string language)
    {
        if (_texts.TryGetValue(language, out var s)) return s;
        if (_texts.TryGetValue(English, out var en)) return en;
        return _texts.Values.FirstOrDefault() ?? "";
    }

    public override string ToString() => Get(English);
}

public sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return LocalizedText.Of(reader.GetString() ?? "");
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options)
                   ?? throw new JsonException("名前が空です");
        return new LocalizedText(dict);
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value.Texts, options);
}
