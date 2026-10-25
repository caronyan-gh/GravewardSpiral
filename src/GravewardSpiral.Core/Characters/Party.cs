namespace GravewardSpiral.Core.Characters;

/// <summary>名簿(作ったキャラクター全員)とパーティ(最大6人。前の3人が前列、残りが後列)。</summary>
public sealed class Roster
{
    public const int MaxParty = 6;
    public const int FrontRow = 3;

    public List<Character> Characters { get; set; } = [];

    /// <summary>パーティの並び(キャラクターの Id)。</summary>
    public List<string> Party { get; set; } = [];

    public Character? Find(string id) => Characters.FirstOrDefault(c => c.Id == id);

    public IEnumerable<Character> PartyMembers => Party.Select(Find).OfType<Character>();

    public bool InParty(Character c) => Party.Contains(c.Id);

    /// <summary>迷宮に残っている(パーティが全滅して置いてきた)キャラクター。M9 の回収で使う。</summary>
    public Dictionary<string, Maze.PartyPosition> LeftInMaze { get; set; } = [];

    /// <summary>
    /// ほかの世界の迷宮にいる人(キャラの id → その世界)。名簿はクランで1つなので、よその世界で中断したパーティと置いてきた死体は、この世界では使えない。
    /// セーブには書かず、読むときに入れる(SaveStore.MarkAway)。
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Dictionary<string, string> Away { get; } = new(StringComparer.Ordinal);

    /// <summary>この世界で使える(よその世界の迷宮にも、この世界の迷宮にもいない)。</summary>
    public bool Available(Character c) => !LeftInMaze.ContainsKey(c.Id) && !Away.ContainsKey(c.Id);

    public bool AddToParty(Character c)
    {
        if (Party.Count >= MaxParty || InParty(c) || !Available(c)) return false;
        Party.Add(c.Id);
        return true;
    }

    public void RemoveFromParty(Character c) => Party.Remove(c.Id);

    /// <summary>パーティの中で、i 番目と j 番目を入れ替える。</summary>
    public void Swap(int i, int j)
    {
        if (i < 0 || j < 0 || i >= Party.Count || j >= Party.Count) return;
        (Party[i], Party[j]) = (Party[j], Party[i]);
    }

    /// <summary>
    /// 新しい隊列にする(Wizardry の並べ直し)。選んだ順に前から並べ、選ばなかった人は今の順のまま後ろに付ける。
    /// </summary>
    public void Reorder(IEnumerable<Character> first)
    {
        var ids = first.Select(c => c.Id).Where(Party.Contains).Distinct().ToList();
        Party = [.. ids, .. Party.Where(id => !ids.Contains(id))];
    }

    public void Delete(Character c)
    {
        RemoveFromParty(c);
        Characters.Remove(c);
        LeftInMaze.Remove(c.Id);
    }

    public bool IsFrontRow(int index) => index < FrontRow;
}
