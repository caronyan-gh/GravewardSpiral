using System.Text.Json;
using Microsoft.Xna.Framework.Input;

namespace GravewardSpiral.Game.Input;

/// <summary>
/// 動作ごとのキーとボタンの割り当て。%LOCALAPPDATA%/GravewardSpiral/input.json から読む。
/// ファイルが無ければ既定の割り当てで作る。書き方は {"Confirm": ["Key:Enter", "Pad:A"], ...}。
/// </summary>
public sealed class InputBindings
{
    public const string FileName = "input.json";

    private readonly Dictionary<GameAction, List<Keys>> _keys = new();
    private readonly Dictionary<GameAction, List<Buttons>> _buttons = new();

    public IReadOnlyList<Keys> KeysFor(GameAction action) => _keys.TryGetValue(action, out var k) ? k : [];
    public IReadOnlyList<Buttons> ButtonsFor(GameAction action) => _buttons.TryGetValue(action, out var b) ? b : [];

    /// <summary>読み込みで飛ばした項目(書き間違いなど)。</summary>
    public List<string> Warnings { get; } = [];

    public static Dictionary<GameAction, string[]> Defaults() => new()
    {
        [GameAction.Confirm] = ["Key:Enter", "Key:Space", "Key:Z", "Pad:A"],
        [GameAction.Cancel] = ["Key:Escape", "Key:Back", "Key:X", "Pad:B"],
        [GameAction.Up] = ["Key:Up", "Key:W", "Pad:DPadUp", "Pad:LeftThumbstickUp"],
        [GameAction.Down] = ["Key:Down", "Key:S", "Pad:DPadDown", "Pad:LeftThumbstickDown"],
        [GameAction.Left] = ["Key:Left", "Key:A", "Pad:DPadLeft", "Pad:LeftThumbstickLeft"],
        [GameAction.Right] = ["Key:Right", "Key:D", "Pad:DPadRight", "Pad:LeftThumbstickRight"],
        [GameAction.Menu] = ["Key:Tab", "Key:C", "Pad:Start", "Pad:Y"],
        [GameAction.Map] = ["Key:M", "Pad:Back", "Pad:X"],
        [GameAction.PageUp] = ["Key:PageUp", "Key:Q", "Pad:LeftShoulder"],
        [GameAction.PageDown] = ["Key:PageDown", "Key:E", "Pad:RightShoulder"],
        // 迷宮の横歩き(一覧の送りと同じキー。迷宮では一覧が無いので重ならない)
        [GameAction.StrafeLeft] = ["Key:Q", "Pad:LeftShoulder"],
        [GameAction.StrafeRight] = ["Key:E", "Pad:RightShoulder"],
        // 押しながら後ろのキーで、設定と逆の動き(R2 は空けておく)
        [GameAction.Reverse] = ["Key:LeftShift", "Key:RightShift", "Pad:LeftTrigger"],
        // 1人を見る画面で仲間を切り替える(< > のキーと L2/R2。L2 の「押しながら」は迷宮だけなので重ならない)
        [GameAction.PrevMember] = ["Key:OemComma", "Pad:LeftTrigger"],
        [GameAction.NextMember] = ["Key:OemPeriod", "Pad:RightTrigger"],
    };

    public static InputBindings LoadOrCreate(string path)
    {
        Dictionary<GameAction, string[]> map = Defaults();
        var warnings = new List<string>();
        if (File.Exists(path))
        {
            try
            {
                var raw = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? [];
                foreach (var (name, entries) in raw)
                {
                    if (Enum.TryParse<GameAction>(name, out var action)) map[action] = entries;
                    else warnings.Add($"{FileName}: 動作 '{name}' はありません");
                }
            }
            catch (JsonException ex)
            {
                warnings.Add($"{FileName} が読めないので既定の割り当てを使います: {ex.Message}");
            }
        }
        else
        {
            var json = JsonSerializer.Serialize(map.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }

        var bindings = FromStrings(map);
        bindings.Warnings.InsertRange(0, warnings);
        return bindings;
    }

    public static InputBindings FromStrings(Dictionary<GameAction, string[]> map)
    {
        var b = new InputBindings();
        foreach (var (action, entries) in map)
        {
            var keys = new List<Keys>();
            var buttons = new List<Buttons>();
            foreach (var entry in entries)
            {
                var parts = entry.Split(':', 2);
                if (parts.Length == 2 && parts[0] == "Key" && Enum.TryParse<Keys>(parts[1], out var key)) keys.Add(key);
                else if (parts.Length == 2 && parts[0] == "Pad" && Enum.TryParse<Buttons>(parts[1], out var button)) buttons.Add(button);
                else b.Warnings.Add($"{FileName}: {action} の '{entry}' が読めません(Key:名前 か Pad:名前)");
            }
            b._keys[action] = keys;
            b._buttons[action] = buttons;
        }
        return b;
    }
}
