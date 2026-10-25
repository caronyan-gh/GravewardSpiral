using System.Text.Json;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Game.Ai;
using GravewardSpiral.Game.Audio;
using GravewardSpiral.Game.Rendering;
using GravewardSpiral.Game.Scenes.Town;
using GravewardSpiral.Game.UI;
using Microsoft.Xna.Framework;

namespace GravewardSpiral.Game.Scenes;

/// <summary>
/// AI の手伝いの設定(設定の画面から開く)。入・切、API キー(クリップボードから貼る・消す)、モデル、つながるかの確かめ。
/// キーは資格情報マネージャーに置き、画面には先頭と末尾だけを出す。
/// </summary>
public sealed class AiOptionsScene : MenuScene
{
    private Task<AiJsonReply>? _test;

    protected override string Title => Services.Strings["ai.title"];

    private static string? Key => ApiKeyStore.Load(ApiKeyStore.Claude);

    private string Model => Services.Options.AiModel ?? ClaudeClient.DefaultModel;

    protected override IEnumerable<MenuItem> Items()
    {
        var s = Services.Strings;
        string? key = Key;
        yield return new MenuItem(s["ai.enabled"], s[Services.Options.AiEnabled ? "ai.on" : "ai.off"], Tag: "toggle");
        yield return new MenuItem(s["ai.paste_key"], key == null ? s["ai.no_key"] : ApiKeyStore.Mask(key), Tag: "paste");
        yield return new MenuItem(s["ai.delete_key"], Enabled: key != null, Tag: "delete");
        yield return new MenuItem(s["ai.model"], Model, Tag: "model");
        yield return new MenuItem(s["ai.test"], Enabled: key != null && _test == null, Tag: "test");
        yield return new MenuItem(s["common.back"], Tag: "back");
    }

    protected override void Choose(MenuItem item, int index)
    {
        var s = Services.Strings;
        switch (item.Tag as string)
        {
            case "toggle":
                Services.Options.AiEnabled = !Services.Options.AiEnabled;
                Services.SaveOptions();
                Services.Audio.PlaySe(Sounds.Confirm);
                break;
            case "paste":
            {
                string? text = ClipboardText.Read()?.Trim();
                if (text == null || !LooksLikeKey(text))
                {
                    Message = s["ai.paste_bad"];
                    Services.Audio.PlaySe(Sounds.Bump);
                    break;
                }
                Message = ApiKeyStore.Save(ApiKeyStore.Claude, text) ? s["ai.saved"] : s["ai.save_failed"];
                Services.Audio.PlaySe(Sounds.Confirm);
                break;
            }
            case "delete":
                Stack.Push(new ConfirmScene(s["ai.confirm_delete"], () =>
                {
                    ApiKeyStore.Delete(ApiKeyStore.Claude);
                    Message = Key == null ? s["ai.deleted"] : s["ai.deleted_env"];
                    Refresh();
                }));
                break;
            case "model":
            {
                var models = GameOptions.AiModels;
                int i = Array.IndexOf(models, Model);
                Services.Options.AiModel = models[(i + 1) % models.Length];
                Services.SaveOptions();
                Services.Audio.PlaySe(Sounds.Cursor);
                break;
            }
            case "test":
                if (Key is not { } key) break;
                Message = s["ai.testing"];
                var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["reply"],"properties":{"reply":{"type":"string"}}}""").RootElement.Clone();
                var client = new ClaudeClient(key, Model);
                _test = client.GenerateJsonAsync(new AiJsonRequest("Answer in one short word.", "Reply with the word: ready", schema, MaxTokens: 2000), CancellationToken.None);
                break;
            case "back":
                Cancel();
                return;
        }
        Refresh();
    }

    public override void Update(GameTime time, Input.InputState input)
    {
        if (_test is { IsCompleted: true } done)
        {
            var reply = done.Result;
            _test = null;
            Message = reply.Json != null ? Services.Strings.Format("ai.test_ok", Model) : Services.Strings.Format("ai.test_failed", reply.Error ?? "?");
            Services.Audio.PlaySe(reply.Json != null ? Sounds.Confirm : Sounds.Bump);
            Refresh();
        }
        base.Update(time, input);
    }

    /// <summary>Claude の API キーらしいか(sk-ant- で始まり、空白を含まない)。</summary>
    private static bool LooksLikeKey(string s) => s.StartsWith("sk-ant-", StringComparison.Ordinal) && s.Length is >= 30 and <= 400 && !s.Any(char.IsWhiteSpace);

    protected override void DrawInfo(Draw2D d, RectangleF area)
    {
        var s = Services.Strings;
        float y = Line(d, area, area.Y + 20, s["ai.about"], Theme.Text, 19);
        Line(d, area, y + 10, s["ai.about_key"], Theme.Dim, 17);
    }
}
