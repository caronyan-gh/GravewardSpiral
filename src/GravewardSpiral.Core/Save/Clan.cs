using System.Text.Json;
using GravewardSpiral.Core.Characters;

namespace GravewardSpiral.Core.Save;

/// <summary>
/// クランファイル(名簿をまるごと書き出したもの。*.gwclan.json)。パーティと控えのキャラを、持ち物と金ごと持つ。
/// 別のシナリオや新しいゲームを、このクランで始められる(三部作を同じキャラで続けて遊ぶ)。
/// 迷宮に残っている人(全滅して回収していない人)は入れない。
/// </summary>
public sealed class ClanFile
{
    public const string Extension = ".gwclan.json";
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public string Name { get; set; } = "";

    /// <summary>クランの id(GameSession.ClanId)。持ち込んだ世界のセーブも、このクランのものとして並ぶ。</summary>
    public string? Id { get; set; }

    public DateTime SavedAt { get; set; } = DateTime.Now;

    /// <summary>書き出したときに遊んでいたシナリオ(標準なら null)と、クリアしていたか。</summary>
    public string? Scenario { get; set; }
    public bool Cleared { get; set; }

    /// <summary>このクランがクリアした世界の数(周回の数)。</summary>
    public int Clears { get; set; }

    /// <summary>このクランが見たイベント(新しい世界では出にくくする)。</summary>
    public List<string> EventsSeen { get; set; } = [];

    public List<Character> Characters { get; set; } = [];

    /// <summary>パーティの並び(キャラの Id)。</summary>
    public List<string> Party { get; set; } = [];

    /// <summary>クランハウス(倉庫の中身と段階)。</summary>
    public ClanHouse House { get; set; } = new();

    public static ClanFile From(GameSession session, string name, string? scenario)
    {
        var roster = session.Roster;
        var chars = roster.Characters.Where(roster.Available).ToList();
        return new ClanFile
        {
            Name = name,
            Id = session.ClanId,
            Scenario = scenario,
            Cleared = session.Flags.Contains("cleared"),
            Clears = session.Clears,
            EventsSeen = session.EventsSeen.Order(StringComparer.Ordinal).ToList(),
            Characters = chars,
            Party = roster.Party.Where(id => chars.Any(c => c.Id == id)).ToList(),
            House = session.ClanHouse,
        };
    }

    /// <summary>名前をファイル名にして書き出す(同じ名前があれば上書き)。書いたパスを返す。</summary>
    public string Save(string dir)
    {
        Directory.CreateDirectory(dir);
        string safe = string.Concat(Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch)).Trim();
        if (safe.Length == 0) safe = "clan";
        string path = Path.Combine(dir, safe + Extension);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SaveStore.Json));
        return path;
    }

    public static ClanFile Load(string path)
    {
        var clan = JsonSerializer.Deserialize<ClanFile>(File.ReadAllText(path), SaveStore.Json) ?? throw new InvalidDataException("クランファイルが空です");
        if (clan.Version > CurrentVersion) throw new InvalidDataException($"このクランファイル(版 {clan.Version})は、今のゲームより新しいので読めません");
        foreach (var c in clan.Characters) c.NormalizeClasses();
        if (clan.Cleared && clan.Clears == 0) clan.Clears = 1;   // 数を持つ前のファイル
        return clan;
    }

    /// <summary>
    /// セーブにあるクラン(新しい世界をそのクランで始める。名簿は写さずに、そのクランのものを使う)。Path は "clan:&lt;id&gt;"。
    /// 名前が無ければ unnamed(クランの id)。Scenario は最近遊んだ世界のシナリオ。
    /// </summary>
    public static List<(string Path, ClanFile Clan)> FromSaves(SaveStore saves, Func<string, string> unnamed)
    {
        var list = new List<(string, ClanFile)>();
        foreach (var summary in saves.ListClans())
        {
            try
            {
                var data = saves.LoadClan(summary.Id);
                if (data == null) continue;
                list.Add(($"clan:{summary.Id}", new ClanFile
                {
                    Name = data.Name ?? unnamed(data.Id),
                    Id = data.Id,
                    SavedAt = summary.SavedAt,
                    Scenario = summary.Latest?.Scenario,
                    Clears = data.Clears,
                    EventsSeen = data.EventsSeen,
                    Characters = data.Characters,
                    House = data.House,
                }));
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
        }
        return list;
    }

    /// <summary>フォルダのクランファイルを、新しい順に(読めないものは飛ばす)。</summary>
    public static List<(string Path, ClanFile Clan)> List(string dir)
    {
        var list = new List<(string, ClanFile)>();
        if (!Directory.Exists(dir)) return list;
        foreach (var path in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            try { list.Add((path, Load(path))); }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException) { }
        }
        return list.OrderByDescending(x => x.Item2.SavedAt).ToList();
    }

    /// <summary>新しいゲームの名簿にする(クランのキャラを写し、パーティの並びも戻す。迷宮の状態は持ち込まない)。</summary>
    public void ApplyTo(GameSession session)
    {
        // 写してから入れる(同じクランで何度始めても、元のファイルのキャラは変わらない)
        var copy = JsonSerializer.Deserialize<List<Character>>(JsonSerializer.Serialize(Characters, SaveStore.Json), SaveStore.Json) ?? [];
        foreach (var c in copy)
        {
            c.NormalizeClasses();
            c.Lasting.Clear();
            session.Roster.Characters.Add(c);
        }
        foreach (var id in Party)
        {
            if (session.Roster.Find(id) is { } c) session.Roster.AddToParty(c);
        }
        session.ClanHouse = JsonSerializer.Deserialize<ClanHouse>(JsonSerializer.Serialize(House, SaveStore.Json), SaveStore.Json) ?? new();
        session.ClanName ??= Name;
        if (Id != null) session.ClanId = Id;
        session.Clears = Clears;
        session.EventsSeen = EventsSeen.ToHashSet(StringComparer.Ordinal);
    }
}
