using GravewardSpiral.Core.Events;
using GravewardSpiral.Core.Rng;
using GravewardSpiral.Game.UI;

namespace GravewardSpiral.Game.Scenes.Town;

/// <summary>町の「人を訪ねる」。いま起こせる町のイベント(place が town)の題を並べ、選ぶとイベントの画面を積む。</summary>
public sealed class VisitScene : MenuScene
{
    protected override string Title => Services.Strings["town.visit"];

    private EventContext Context()
    {
        var session = Services.Session!;
        // 町の目安のレベルは、迷宮のまんなかの階のもの(依頼の報酬を、この世界の強さに合わせる)
        return new EventContext { Data = Services.Data, Session = session, Rng = session.Rng[RngStreams.Names.Loot], TargetLevel = Services.Encounters().TargetLevel(0.5) };
    }

    /// <summary>いま起こせる町のイベント(パックの順ではなく id の順)。</summary>
    public static IEnumerable<EventDef> Available(GameServices sv, EventContext ctx) => sv.Data.Events.Values
        .Where(e => e.Place == "town" && EventRunner.Available(e, ctx)).OrderBy(e => e.Id, StringComparer.Ordinal);

    protected override IEnumerable<MenuItem> Items()
    {
        var list = Available(Services, Context()).Select(e => new MenuItem(Services.Name(e.Title), Tag: e)).ToList();
        if (list.Count == 0) Message = Services.Strings["town.visit_none"];
        return list;
    }

    protected override void Choose(MenuItem item, int index)
    {
        var def = (EventDef)item.Tag!;
        // 町のイベントでは戦わない(戦闘の手順は、勝ったことにせず飛ばす)
        Stack.Push(new EventScene(new EventRunner(def, Context()), new EventScene.Callbacks((_, after) => after(false), () => { }, Refresh)));
    }
}
