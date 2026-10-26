using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace GravewardSpiral.Core.Content;

/// <summary>
/// パックの JSON に書ける項目と、その説明(Core のコードの説明書き GravewardSpiral.Core.xml)。
/// シナリオエディタの「項目の一覧」と、AI に下書きを書かせるときのスキーマに使う。
/// </summary>
public static class FieldDocs
{
    /// <summary>項目1つ: どの型の項目か、JSON の名前、型、説明。</summary>
    public sealed record Field(Type Owner, string Name, Type Type, string Doc);

    /// <summary>型の項目を、中の部品の型もたどって並べる(型ごとにまとまる。根の型が先)。</summary>
    public static List<Field> Walk(Type root, IReadOnlyDictionary<string, string> docs)
    {
        var fields = new List<Field>();
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (!seen.Add(type)) continue;
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.SetMethod is not { IsPublic: true } || p.GetIndexParameters().Length > 0) continue;
                if (p.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
                if (p.GetCustomAttribute<EditorBrowsableAttribute>() is { State: EditorBrowsableState.Never }) continue;
                string name = p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name);
                var inner = Inner(p.PropertyType);
                if (inner != null && inner != type) queue.Enqueue(inner);
                fields.Add(new Field(type, name, p.PropertyType, docs.GetValueOrDefault($"P:{Key(type)}.{p.Name}") ?? ""));
            }
        }
        return fields;
    }

    /// <summary>中の部品の型(Core の class。リストならその中身)。</summary>
    public static Type? Inner(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(List<>)) u = u.GetGenericArguments()[0];
        return u.IsClass && u != typeof(string) && u != typeof(LocalizedText) && u.Namespace?.StartsWith("GravewardSpiral.Core") == true ? u : null;
    }

    /// <summary>説明書きのキーに使う型の名前(入れ子の型は . でつなぐ)。</summary>
    public static string Key(Type type) => type.FullName!.Replace('+', '.');

    /// <summary>Core の説明書き(GravewardSpiral.Core.xml の summary)。キーは "T:型" と "P:型.項目"。無ければ空。</summary>
    public static Dictionary<string, string> Load()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string path = Path.ChangeExtension(typeof(FieldDocs).Assembly.Location, ".xml");
        if (!File.Exists(path)) return result;
        foreach (var member in XDocument.Load(path).Descendants("member"))
        {
            if (member.Attribute("name")?.Value is not { } name || member.Element("summary") is not { } summary) continue;
            foreach (var see in summary.Descendants().Where(d => d.Name == "see" || d.Name == "paramref").ToList())
                see.ReplaceWith((see.Attribute("cref") ?? see.Attribute("name"))?.Value.Split('.').Last() ?? "");
            result[name] = System.Text.RegularExpressions.Regex.Replace(summary.Value.Trim(), @"\s*\n\s*", "");   // 日本語の行の折り返しは、空白なしでつなぐ
        }
        return result;
    }

    /// <summary>
    /// AI に渡すスキーマ: 型ごとに、型の説明と、項目の名前・型・説明。説明は日本語のまま(書き方の決まりもそこにある)。
    /// </summary>
    public static string Schema(Type root, IReadOnlyDictionary<string, string> docs)
    {
        var text = new StringBuilder();
        foreach (var group in Walk(root, docs).GroupBy(f => f.Owner))
        {
            text.AppendLine($"## {group.Key.Name}");
            if (docs.GetValueOrDefault("T:" + Key(group.Key)) is { Length: > 0 } summary) text.AppendLine(summary);
            foreach (var f in group)
                text.AppendLine($"- {f.Name} ({TypeName(f.Type)}){(f.Doc.Length > 0 ? ": " + f.Doc : "")}");
            text.AppendLine();
        }
        return text.ToString();
    }

    /// <summary>JSON での型の書き方(AI 向け。列挙は書ける値)。</summary>
    private static string TypeName(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u.IsEnum) return string.Join(" | ", Enum.GetNames(u).Select(JsonNamingPolicy.SnakeCaseLower.ConvertName));
        if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(List<>)) return "list of " + TypeName(u.GetGenericArguments()[0]);
        if (u.IsGenericType && u.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return "map";
        return u == typeof(string) ? "string" : u == typeof(int) || u == typeof(long) ? "integer" : u == typeof(double) ? "number"
            : u == typeof(bool) ? "bool" : u == typeof(LocalizedText) ? "{\"en\": ..., \"ja\": ...}" : u.Name;
    }
}
