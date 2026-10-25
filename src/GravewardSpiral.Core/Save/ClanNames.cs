using GravewardSpiral.Core.Content;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// 名前の無いクランに付ける名前の部品(パックの clan_names.json)。言葉と集まりを1つずつ組み合わせる(Silver Fang / シルバーファング)。
/// セーブを消すときにクランの名前を打たせるので、日本語は画面のキーボードで打てるカタカナだけにする。
/// </summary>
public sealed class ClanNameParts
{
    public const string Collection = "clan_names";

    public string Id { get; set; } = "default";
    public List<LocalizedText> Words { get; set; } = [];
    public List<LocalizedText> Groups { get; set; } = [];

    public static ClanNameParts Load(ContentLibrary content) =>
        content.Find(Collection, "default")?.As<ClanNameParts>() ?? new ClanNameParts();

    /// <summary>key(クランの id)で決まる名前(同じクランならいつも同じ)。部品が無ければ null。</summary>
    public string? Pick(string key, string language)
    {
        if (Words.Count == 0 || Groups.Count == 0) return null;
        ulong h = 1469598103934665603UL;
        foreach (char ch in key) h = (h ^ ch) * 1099511628211UL;
        var word = Words[(int)(h % (ulong)Words.Count)];
        var group = Groups[(int)(h / (ulong)Words.Count % (ulong)Groups.Count)];
        return language == "ja" ? word.Get("ja") + group.Get("ja") : $"{word.Get("en")} {group.Get("en")}";
    }
}
