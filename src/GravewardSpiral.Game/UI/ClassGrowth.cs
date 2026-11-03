using GravewardSpiral.Core.Characters;
using GravewardSpiral.Core.Magic;
using GravewardSpiral.Core.Rules;

namespace GravewardSpiral.Game.UI;

/// <summary>
/// クラスのレベルで得るもの(クラスの技、勝手に効く能力、能力値上昇の点、攻撃の回数、使える呪文のレベル)。
/// 訓練場の「このレベルで得るもの」と、クラスの成長表(ClassGrowthScene。ゲームのデータから作るので、データと食い違わない)。
/// </summary>
public static class ClassGrowth
{
    /// <summary>そのクラスを1つ上げると増えるもの(技と能力は、名前の上に説明の窓を出せる言葉)。</summary>
    public static List<LinkWord> Gains(GameServices services, Character who, ClassDef cls)
    {
        var s = services.Strings;
        var data = services.Data;
        var next = Core.Save.EventAuthoring.Copy(who);
        next.NormalizeClasses();
        if (next.Classes.FirstOrDefault(x => x.ClassId == cls.Id) is { } entry) entry.Level++;
        else next.Classes.Add(new ClassLevel { ClassId = cls.Id, Level = 1 });
        next.Level++;
        var gains = new List<LinkWord>();
        var had = ClassFeatures.ActiveFor(data, who).Select(f => f.Id).ToHashSet();
        gains.AddRange(ClassFeatures.ActiveFor(data, next).Where(f => !had.Contains(f.Id)).Select(f => FeatureText.Link(services, f.Id)));
        var hadPassive = ClassFeatures.Passives(data, who).ToHashSet();
        gains.AddRange(ClassFeatures.Passives(data, next).Where(p => !hadPassive.Contains(p)).Select(p => FeatureText.Link(services, p)));
        if (next.AbilityPoints(data) > who.AbilityPoints(data)) gains.Add(new(s.Format("training.gain_asi", next.AbilityPoints(data) - who.AbilityPoints(data))));
        if (Core.Combat.CombatEngine.AttacksPerAction(data, next) > Core.Combat.CombatEngine.AttacksPerAction(data, who)) gains.Add(new(s.Format("training.gain_attacks", Core.Combat.CombatEngine.AttacksPerAction(data, next))));
        // そのクラスで覚えられる呪文のレベル(マルチクラスの合算のスロットのレベルではない)
        if (cls.Spellcasting != null && Spellcasting.ClassMaxSpellLevel(data, cls, next) is var lv and > 0 && lv > Spellcasting.ClassMaxSpellLevel(data, cls, who))
            gains.Add(new(s.Format("training.gain_spell_level", lv)));
        return gains;
    }

    /// <summary>
    /// クラスの成長の表: レベルごとの得るもの(何も得ないレベルは入れない)。そのクラスだけで L20 まで上げたキャラ(人間)で数える。
    /// クラスはゲームが読み込んだデータから作るので、シナリオが足したクラスでも出る。
    /// </summary>
    public static List<(int Level, List<LinkWord> Words)> Table(GameServices services, ClassDef cls)
    {
        var s = services.Strings;
        var data = services.Data;
        string race = data.Races.ContainsKey("human") ? "human" : data.Races.Keys.First();
        var first = CharacterFactory.QuickCreate(data, "-", race, cls.Id);
        var start = ClassFeatures.ActiveFor(data, first).Select(f => FeatureText.Link(services, f.Id))
            .Concat(ClassFeatures.Passives(data, first).Select(p => FeatureText.Link(services, p))).ToList();
        if (cls.Spellcasting != null && Spellcasting.ClassMaxSpellLevel(data, cls, first) is var lv and > 0) start.Add(new(s.Format("training.gain_spell_level", lv)));
        var rows = new List<(int, List<LinkWord>)>();
        if (start.Count > 0) rows.Add((1, start));
        for (int level = 1; level < 20; level++)   // 5版のクラスのレベルは 20 まで
        {
            var gains = Gains(services, CharacterFactory.QuickCreate(data, "-", race, cls.Id, level), cls);
            if (gains.Count > 0) rows.Add((level + 1, gains));
        }
        return rows;
    }
}
