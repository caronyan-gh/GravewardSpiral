using GravewardSpiral.Game.Input;
using GravewardSpiral.Game.Rendering;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>画面1つぶん。Scene スタックの上に積まれる。</summary>
public abstract class Scene
{
    protected GameServices Services { get; private set; } = null!;
    protected SceneStack Stack => Services.Scenes;

    internal void Attach(GameServices services) => Services = services;

    /// <summary>true なら、下の Scene も描く(重ねて出す窓など)。</summary>
    public virtual bool IsOverlay => false;

    public virtual void Enter() { }
    public virtual void Leave() { }

    /// <summary>一番上の Scene だけが呼ばれる。</summary>
    public abstract void Update(GameTime time, InputState input);

    public abstract void Draw(Draw2D d);
}
