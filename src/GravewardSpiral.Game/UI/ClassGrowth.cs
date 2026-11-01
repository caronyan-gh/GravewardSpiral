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
    /// <summary>そのクラスを1つ上げると増えるもの。</summary>
    public static List<string> Gains(GameServices services, Character who, ClassDef cls)
    {
        var s = services.Strings;
        var data = services.Data;
        var next = Core.Save.EventAuthoring.Copy(who);
        next.NormalizeClasses();
        if (next.Classes.FirstOrDefault(x => x.ClassId == cls.Id) is { } entry) entry.Level++;
        else next.Classes.Add(new ClassLevel { ClassId = cls.Id, Level = 1 });
        next.Level++;
        var gains = new List<string>();
        var had = ClassFeatures.ActiveFor(data, who).Select(f => f.Id).ToHashSet();
        gains.AddRange(ClassFeatures.ActiveFor(data, next).Where(f => !had.Contains(f.Id)).Select(f => s["feature." + f.Id]));
        var hadPassive = ClassFeatures.Passives(data, who).ToHashSet();
        gains.AddRange(ClassFeatures.Passives(data, next).Where(p => !hadPassive.Contains(p)).Select(p => s["passive." + p]));
        if (next.AbilityPoints(data) > who.AbilityPoints(data)) gains.Add(s.Format("training.gain_asi", next.AbilityPoints(data) - who.AbilityPoints(data)));
        if (Core.Combat.CombatEngine.AttacksPerAction(data, next) > Core.Combat.CombatEngine.AttacksPerAction(data, who)) gains.Add(s.Format("training.gain_attacks", Core.Combat.CombatEngine.AttacksPerAction(data, next)));
        // そのクラスで覚えられる呪文のレベル(マルチクラスの合算のスロットのレベルではない)
        if (cls.Spellcasting != null && Spellcasting.ClassMaxSpellLevel(data, cls, next) is var lv and > 0 && lv > Spellcasting.ClassMaxSpellLevel(data, cls, who))
            gains.Add(s.Format("training.gain_spell_level", lv));
        return gains;
    }

    /// <summary>
    /// クラスの成長の表: レベルごとの得るもの(何も得ないレベルは入れない)。そのクラスだけで L20 まで上げたキャラ(人間)で数える。
    /// クラスはゲームが読み込んだデータから作るので、シナリオが足したクラスでも出る。
    /// </summary>
    public static List<(int Level, string Text)> Table(GameServices services, ClassDef cls)
    {
        var s = services.Strings;
        var data = services.Data;
        string race = data.Races.ContainsKey("human") ? "human" : data.Races.Keys.First();
        var first = CharacterFactory.QuickCreate(data, "-", race, cls.Id);
        var start = ClassFeatures.ActiveFor(data, first).Select(f => s["feature." + f.Id])
            .Concat(ClassFeatures.Passives(data, first).Select(p => s["passive." + p])).ToList();
        if (cls.Spellcasting != null && Spellcasting.ClassMaxSpellLevel(data, cls, first) is var lv and > 0) start.Add(s.Format("training.gain_spell_level", lv));
        var rows = new List<(int, string)>();
        if (start.Count > 0) rows.Add((1, s.List(start)));
        for (int level = 1; level < 20; level++)   // 5版のクラスのレベルは 20 まで
        {
            var gains = Gains(services, CharacterFactory.QuickCreate(data, "-", race, cls.Id, level), cls);
            if (gains.Count > 0) rows.Add((level + 1, s.List(gains)));
        }
        return rows;
    }
}
