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

    public bool AddToParty(Character c)
    {
        if (Party.Count >= MaxParty || InParty(c) || LeftInMaze.ContainsKey(c.Id)) return false;
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
