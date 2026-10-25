using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// Scene のスタック。積む・降ろす・入れ替えるは、フレームの終わりにまとめて行う
/// (Update の途中でスタックが変わらないように)。
/// </summary>
public sealed class SceneStack
{
    private readonly List<Scene> _scenes = [];
    private readonly Queue<Action> _pending = new();
    private readonly GameServices _services;

    public SceneStack(GameServices services) => _services = services;

    public int Count => _scenes.Count;

    /// <summary>積む・降ろすたびに増える数(上に何かが積まれて戻ってきたかを知るのに使う)。</summary>
    public int Version { get; private set; }
    public Scene? Top => _scenes.Count > 0 ? _scenes[^1] : null;

    public void Push(Scene scene) => _pending.Enqueue(() =>
    {
        scene.Attach(_services);
        _scenes.Add(scene);
        Version++;
        scene.Enter();
    });

    public void Pop() => _pending.Enqueue(() =>
    {
        if (_scenes.Count == 0) return;
        var top = _scenes[^1];
        _scenes.RemoveAt(_scenes.Count - 1);
        Version++;
        top.Leave();
    });

    public void Replace(Scene scene)
    {
        Pop();
        Push(scene);
    }

    public void Update(GameTime time, InputState input)
    {
        Top?.Update(time, input);
        Flush();
    }

    public void Flush()
    {
        while (_pending.Count > 0) _pending.Dequeue()();
    }

    public void Draw(Draw2D d)
    {
        // 一番上から下へ、重ねない Scene に当たるまでさかのぼり、そこから上へ描く
        int start = _scenes.Count - 1;
        while (start > 0 && _scenes[start].IsOverlay) start--;
        for (int i = Math.Max(start, 0); i < _scenes.Count; i++) _scenes[i].Draw(d);
    }
}
