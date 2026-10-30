namespace GravewardSpiral.Game.Input;

/// <summary>
/// 入力はキーやボタンではなく、この動作に置き換えてから Scene に渡す。
/// 迷宮では Up=前進、Down=後退、Left/Right=回転として使う。
/// </summary>
public enum GameAction
{
    Confirm,
    Cancel,
    Up,
    Down,
    Left,
    Right,
    Menu,
    Map,
    PageUp,
    PageDown,
    /// <summary>迷宮で、向きを変えずに左右へ1歩(横歩き)。</summary>
    StrafeLeft,
    StrafeRight,
    /// <summary>押しながら後ろのキーを押すと、設定と逆の動きにする(下がる ⇔ 振り向く)。</summary>
    Reverse,
    /// <summary>1人を見る画面(持ち物・状態・呪文の準備)で、前の仲間へ。</summary>
    PrevMember,
    /// <summary>1人を見る画面で、次の仲間へ。</summary>
    NextMember,
}
