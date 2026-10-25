namespace GravewardSpiral.Game.Audio;

/// <summary>
/// 効果音の id。ファイルは Content/&lt;パック&gt;/audio/se/&lt;id&gt;.wav(か .mp3)。
/// 欲しい素材の一覧(DB の asset_requests と docs/asset_requests.md)と同じ id にする。
/// </summary>
public static class Sounds
{
    public const string Cursor = "cursor";
    public const string Confirm = "confirm";
    public const string Cancel = "cancel";
    public const string Step = "step";
    public const string Bump = "bump";
    public const string Door = "door";
    public const string Search = "search";
    public const string SecretFound = "secret_found";
    public const string Stairs = "stairs";
    public const string MapOpen = "map_open";
    public const string LevelUp = "level_up";
    public const string Encounter = "encounter";
    public const string Hit = "hit";
    public const string Miss = "miss";
    public const string Critical = "critical";
    public const string EnemyDie = "enemy_die";
    public const string PartyHurt = "party_hurt";
    /// <summary>仲間がクリティカルを受けた声。</summary>
    public const string PartyHurtCrit = "party_hurt_crit";
    public const string PartyDown = "party_down";
    public const string PartyWipe = "party_wipe";
    public const string SpellCast = "spell_cast";
    public const string Heal = "heal";
    public const string ChestOpen = "chest_open";
    public const string Trap = "trap";
    public const string Coins = "coins";
    public const string InnRest = "inn_rest";
}
