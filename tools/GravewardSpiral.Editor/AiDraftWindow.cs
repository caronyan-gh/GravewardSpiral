using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using GravewardSpiral.Core.Ai;
using GravewardSpiral.Core.Content;
using static GravewardSpiral.Editor.Loc;

namespace GravewardSpiral.Editor;

/// <summary>
/// AI に下書きを頼む窓(イベント・品・モンスター)。元(ベース)と希望を渡して DraftWriter で書かせ、検査の結果と一緒に見せる。
/// 「この下書きを追加」(か置き換える)を押すまで、シナリオには入れない。下書きの JSON は、追加する前に手で直せる(追加するときにもう一度検査する)。
/// </summary>
public sealed class AiDraftWindow : Window
{
    private static readonly Lazy<Dictionary<string, string>> Docs = new(FieldDocs.Load);

    private readonly EditorContext _context;
    private readonly DraftWriter.Kind _kind;
    private readonly JsonObject? _base;
    private readonly string _id;
    private readonly string? _extend;
    private readonly Func<JsonObject, IReadOnlyList<JsonObject>, List<string>> _check;
    private readonly Action<JsonObject, IReadOnlyList<JsonObject>, bool> _apply;
    private readonly TextBox _wish = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly CheckBox _eventOnly = new() { IsChecked = true, Content = T("イベント品にする(宝の抽選・商店に出さない)", "Event-only item (never in treasure rolls or shops)") };
    private readonly CheckBox _cache = new()
    {
        Content = T("前置きをキャッシュする", "Cache the preamble"), Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        ToolTip = new TextBlock { Text = EditorSettings.CacheExplanation, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 },
    };
    private readonly TextBlock _tokens = new() { Foreground = Ui.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    /// <summary>AI からの報告(作ったもの)と、あなたの希望に沿えなかった内容(黄色の注意)。下書きがあるときだけ出す。</summary>
    private readonly StackPanel _memo = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _problems = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Warn };
    /// <summary>中身の一覧(エディタが JSON から作る。DraftSummary)。</summary>
    private readonly StackPanel _summary = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _applied = new() { TextWrapping = TextWrapping.Wrap, Foreground = Ui.Good, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBox _result = new()
    {
        AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Height = 300,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    /// <summary>イベントと一緒に書いた新しい品(JSON の並び)。イベントのときだけ出す。</summary>
    private readonly TextBox _items = new()
    {
        AcceptsReturn = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Height = 140, Text = "[]",
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    /// <summary>強さを試す(モンスターだけ): パーティのレベル・敵の数・結果。</summary>
    private readonly TextBox _strengthLevel = new() { Width = 50 };
    private readonly TextBox _strengthCount = new() { Width = 50 };
    private readonly StackPanel _strengthResult = new() { Margin = new Thickness(0, 4, 0, 0) };
    private Button? _strengthButton;

    /// <summary>強さを試すときの戦いの数と、パーティ(Sim と同じ6人)。</summary>
    private const int StrengthFights = 200;

    /// <summary>合うレベルを探すとき、1 つのレベルで戦わせる数と、調べる一番上のレベル。</summary>
    private const int ScanFights = 100, ScanTop = 12;
    private static readonly string[] StrengthParty = ["fighter", "fighter", "paladin", "rogue", "ranger", "cleric"];

    private readonly Button _write;
    private readonly Button _replace;
    private readonly Button _add;
    private readonly Button _fixCopy;

    /// <summary>出している AI からの報告(検査の結果が変わったら、注意を付け直す)。</summary>
    private (string? Note, List<string> Differences, bool Unwrapped)? _memoShown;

    /// <summary>今の下書きの検査の問題(修正依頼をコピーするときに使う)。</summary>
    private List<string> _lastProblems = [];

    /// <summary>頼むものの種類(イベント・品・モンスター)。</summary>
    public DraftWriter.Kind Kind => _kind;

    /// <summary>AI に書かせている途中か。</summary>
    public bool Busy { get; private set; }

    /// <summary>AI に書かせた下書きがあって、まだ入れていない(閉じるときに確かめる)。</summary>
    private bool _unused;

    /// <param name="baseJson">元(無ければ新しく作る)。</param>
    /// <param name="baseLabel">元の見出し(窓に出す)。</param>
    /// <param name="id">下書きに付ける id(元のものか、新しく決めたもの)。</param>
    /// <param name="extend">品とモンスターの extends に書く元の id(null なら、全部書き写した形のまま)。</param>
    /// <param name="canReplace">元がこのシナリオのものなら、置き換えられる。</param>
    /// <param name="check">下書きの問題(ゲームと同じ検査)。新しい品(イベントと一緒に書いたもの)も渡す。</param>
    /// <param name="apply">当てる(下書きと、新しい品と、置き換えるか)。</param>
    public AiDraftWindow(EditorContext context, DraftWriter.Kind kind, JsonObject? baseJson, string baseLabel, string id, string? extend, bool canReplace,
        Func<JsonObject, IReadOnlyList<JsonObject>, List<string>> check, Action<JsonObject, IReadOnlyList<JsonObject>, bool> apply)
    {
        _context = context;
        _kind = kind;
        _base = baseJson;
        _id = id;
        _extend = extend;
        _check = check;
        _apply = apply;
        Title = T("AI に頼む: ", "Ask the AI: ") + KindName(kind);
        Width = 760;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Theme.Brush(Theme.Palette.Background);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Ui.Note(T("元と、こうしたいことを書いて頼む。下書きはゲームと同じ検査にかけ、問題があれば1回だけ自動で直させる。追加するまでシナリオには入らない",
            "Describe what you want, starting from the base. The draft goes through the game's checks, and the AI fixes problems once automatically. Nothing enters the scenario until you add it")));
        panel.Children.Add(Ui.Row(T("元", "Base"), new TextBlock { Text = baseLabel, TextWrapping = TextWrapping.Wrap }, 90));
        panel.Children.Add(Ui.Row("id", new TextBlock { Text = id + (extend != null ? $"  (extends {extend})" : "") }, 90));
        if (kind == DraftWriter.Kind.Item) panel.Children.Add(Ui.Row(T("出し方", "Drops"), _eventOnly, 90));
        panel.Children.Add(Ui.Row(T("こうしたい", "Request"), _wish, 90));
        panel.Children.Add(Ui.Row("", Tips(kind), 90));
        var bar = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        _write = Ui.Button(T("下書きを作る", "Make a draft"), Write, T("ご自身の API キーで AI を呼ぶ(1回ごとに料金がかかる)", "Calls the AI with your API key (each call is charged)"));
        bar.Children.Add(_write);
        bar.Children.Add(new TextBlock { Text = T($"モデル: {context.Settings.AiModel}(設定で変える)", $"Model: {context.Settings.AiModel} (change it in Settings)"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
        _cache.IsChecked = context.Settings.AiCache;
        _cache.Click += (_, _) =>
        {
            _context.Settings.AiCache = _cache.IsChecked == true;
            _context.Settings.Save();
        };
        bar.Children.Add(_cache);
        panel.Children.Add(bar);
        // API キーが無い人は、同じ指示を AI のチャットに貼って、返事を貼り戻す
        var chat = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        chat.Children.Add(new TextBlock { Text = T("API キーが無いときは:", "Without an API key:"), Foreground = Ui.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        chat.Children.Add(Ui.Button(T("チャット用にコピー", "Copy for a chat"), CopyForChat,
            T("API で頼むときと同じ指示・項目の説明・元・希望をまとめてコピーする。好きな AI のチャットに貼り、返ってきた答えを「回答を貼り付け」へ", "Copies the same instructions, field descriptions, base and request as the API uses. Paste them into any AI chat, then paste its answer with \"Paste the answer\"")));
        chat.Children.Add(Ui.Button(T("回答を貼り付け", "Paste the answer"), PasteAnswer, T("チャットの答え(JSON)を貼って、同じ検査にかける", "Paste the chat's answer (JSON) and run the same checks")));
        _fixCopy = Ui.Button(T("修正依頼をコピー", "Copy a fix request"), CopyFix, T("今の下書きと検査の問題をまとめてコピーする。同じチャットに貼って直してもらう", "Copies the current draft and the check's problems. Paste them into the same chat to have it fixed"));
        _fixCopy.IsEnabled = false;
        chat.Children.Add(_fixCopy);
        panel.Children.Add(chat);

        // 料金の注意は1行だけ常に出し、目安・キャッシュ・使ったトークンは「詳細」を開いたときだけ
        var cost = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        cost.Children.Add(Caution(T("ご自身の API キーを使用します。生成・自動修正には料金がかかります。", "This uses your own API key. Making and auto-fixing drafts is charged.")));
        var details = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        details.Children.Add(Ui.Note(T("料金の目安(Sonnet。Opus は約 2 倍): イベント約 4 円、品約 1 円、モンスター約 2 円。検査で落ちて自動で直させると、もう1回分。",
            "Rough cost (Sonnet; about twice with Opus): about 4 yen for an event, 1 for an item, 2 for a monster. An automatic fix after a failed check costs one more.")));
        details.Children.Add(Ui.Note(EditorSettings.CacheShort));
        details.Children.Add(_tokens);
        _tokens.Text = T("使ったトークン: (まだ頼んでいない)", "Tokens used: (nothing asked yet)");
        cost.Children.Add(new Expander { Header = T("詳細", "Details"), Content = details, IsExpanded = false, Margin = new Thickness(12, 0, 0, 0), Foreground = Theme.Brush(Theme.Palette.Accent) });
        panel.Children.Add(cost);

        panel.Children.Add(_status);
        panel.Children.Add(_memo);
        panel.Children.Add(_summary);
        if (kind == DraftWriter.Kind.Monster) panel.Children.Add(StrengthPanel());
        panel.Children.Add(Ui.Heading(T("下書き(JSON。追加する前に直してもよい)", "Draft (JSON; you may edit it before adding)")));
        panel.Children.Add(_problems);
        _result.LostFocus += (_, _) => { if (TryParse(_result.Text) is { } edited) ShowSummary(edited, TryItems() ?? []); };   // 手で直したら一覧も作り直す
        panel.Children.Add(_result);
        if (kind == DraftWriter.Kind.Event)
        {
            panel.Children.Add(Ui.Heading(T("このイベントで使う新しい品(JSON の並び。無ければ [])", "New items this event uses (a JSON list; [] if none)")));
            panel.Children.Add(Ui.Note(T("ゲームに無い品(持ち帰る物・鍵・このイベントだけのごほうび)を、イベントと一緒に AI が書く。追加すると、データの品にも入る",
                "Items the game does not have (a thing to bring back, a key, a reward only this event gives), written by the AI with the event. Adding also puts them in the Data tab's items")));
            panel.Children.Add(_items);
            _items.LostFocus += (_, _) => { if (TryParse(_result.Text) is { } edited) ShowSummary(edited, TryItems() ?? []); };
        }
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        _replace = Ui.Button(T("この下書きで元を置き換える", "Replace the base with this draft"), () => Apply(replace: true));
        _add = Ui.Button(T("この下書きを追加", "Add this draft"), () => Apply(replace: false));
        _add.Style = (Style)Application.Current.FindResource("AccentButton");
        _replace.Visibility = canReplace ? Visibility.Visible : Visibility.Collapsed;
        _replace.IsEnabled = _add.IsEnabled = false;
        buttons.Children.Add(_replace);
        buttons.Children.Add(_add);
        var close = Ui.Button(T("閉じる", "Close"), Close);
        close.IsCancel = true;
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        panel.Children.Add(_applied);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 900 };
        Closing += ConfirmClose;
    }

    /// <summary>黄色の注意の1行(⚠ のアイコンと文)。</summary>
    private static StackPanel Caution(string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = "", FontFamily = Theme.Icons, Foreground = Ui.Caution, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(new TextBlock { Text = text, Foreground = Ui.Caution, TextWrapping = TextWrapping.Wrap, MaxWidth = 640, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static string KindName(DraftWriter.Kind kind) => kind switch
    {
        DraftWriter.Kind.Event => T("イベント", "Event"),
        DraftWriter.Kind.Item => T("品", "Item"),
        _ => T("モンスター", "Monster"),
    };

    /// <summary>希望を入れておく(--ai-draft を画面の保存なしで。人がチャット用のコピーなどを試す)。</summary>
    public void SetWish(string wish) => _wish.Text = wish;

    /// <summary>希望を入れ、AI に書かせる代わりに、チャットの答えを貼ったことにする(--ai-paste。確かめ用)。</summary>
    public void PasteNow(string wish, string answer)
    {
        _wish.Text = wish;
        ReadAnswer(answer);
    }

    /// <summary>希望を入れて書かせる(--ai-draft。確かめ用)。</summary>
    public Task WriteNow(string wish)
    {
        _wish.Text = wish;
        return WriteAsync();
    }

    /// <summary>窓の中身を文字で(--ai-draft の確かめで、画面の保存の横に書く)。</summary>
    public string Report() => string.Join("\n", Title, "状態: " + _status.Text,
        string.Join("\n", _memo.Children.OfType<FrameworkElement>().Select(Text)), string.Join("\n", _summary.Children.OfType<FrameworkElement>().Select(Text)),
        string.Join("\n", _strengthResult.Children.OfType<FrameworkElement>().Select(Text)),
        _problems.Text, _tokens.Text, "", _result.Text, _kind == DraftWriter.Kind.Event ? "新しい品: " + _items.Text : "");

    /// <summary>部品の中の文(Report 用)。</summary>
    private static string Text(FrameworkElement e) => e switch
    {
        TextBlock t => t.Text,
        Panel p => string.Concat(p.Children.OfType<FrameworkElement>().Select(Text)),
        Border b when b.Child is FrameworkElement c => Text(c),
        _ => "",
    };

    private async void Write() => await WriteAsync();

    /// <summary>頼むこと(API とチャットで同じ)。</summary>
    private DraftWriter.Brief Brief()
    {
        var root = _kind switch { DraftWriter.Kind.Event => typeof(Core.Events.EventDef), DraftWriter.Kind.Item => typeof(Core.Items.ItemDef), _ => typeof(Core.Monsters.MonsterDef) };
        string baseText = _base?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "";
        string itemSchema = _kind == DraftWriter.Kind.Event ? FieldDocs.Schema(typeof(Core.Items.ItemDef), Docs.Value) : "";   // イベントは新しい品も書ける
        return new DraftWriter.Brief(_kind, FieldDocs.Schema(root, Docs.Value), baseText, _wish.Text, Context(), Specific(), _cache.IsChecked == true, itemSchema);
    }

    /// <summary>「頼み方のコツ」: 何を作るか・どう動いてほしいか・何を変えないかを書く。開くと種類ごとの例。</summary>
    private static Expander Tips(DraftWriter.Kind kind)
    {
        string example = kind switch
        {
            DraftWriter.Kind.Event => T("低レベル向けの宝箱。ローグがいれば罠を外せる。それ以外は器用さの判定。失敗すると軽い毒、成功すると金 50。開けたら二度と使えない。",
                "A treasure chest for low levels. A rogue can disarm the trap; others make a Dexterity check. Failure gives a light poison, success 50 gold. It cannot be used again once opened."),
            DraftWriter.Kind.Item => T("この短剣を、投げると戻ってくる武器にしたい。攻撃力は元のまま。店やランダムな宝には出さない。",
                "Make this dagger return when thrown. Keep its attack as it is. Never in shops or random treasure."),
            _ => T("このゴブリンを、火に強い隊長にしたい。HP を少し増やして、武器は剣のまま。序盤のボス用。",
                "Make this goblin a fire-resistant captain. A little more HP, keep the sword. A boss for the early floors."),
        };
        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(Ui.Note(T("「何を作るか・どう動いてほしいか・何を変えないか」を書く。専門の言葉や JSON は要らない。ゲームで作れない効果は、別の効果に変えずに「あなたの希望に沿えなかった内容」で知らせてくる。",
            "Write what to make, how it should work, and what to keep. No jargon or JSON needed. Effects the game cannot do are not swapped for others; they are reported under \"Where it could not follow the request\".")));
        body.Children.Add(new TextBlock { Text = T("例: ", "Example: ") + example, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return new Expander { Header = "💡 " + T("頼み方のコツ", "How to ask"), Content = body, IsExpanded = false, Foreground = Theme.Brush(Theme.Palette.Accent) };
    }

    /// <summary>AI のチャットに貼る文をコピーする(API で頼むときと同じ指示)。</summary>
    private void CopyForChat()
    {
        if (_wish.Text.Trim().Length == 0)
        {
            Say(T("こうしたいことを書いてください", "Write what you want"), true);
            return;
        }
        Clipboard.SetText(DraftWriter.ChatText(Brief()));
        Say(T("コピーしました。好きな AI のチャットに貼り、返ってきた答えを「回答を貼り付け」へ。", "Copied. Paste it into any AI chat, then paste its answer with \"Paste the answer\"."), false);
    }

    /// <summary>今の下書きと検査の問題を、同じチャットへの修正依頼としてコピーする。</summary>
    private void CopyFix()
    {
        if (_lastProblems.Count == 0) return;
        Clipboard.SetText(DraftWriter.FixText(DraftWriter.Previous(_result.Text, TryItems() ?? []), _lastProblems));
        Say(T("修正依頼をコピーしました。同じチャットに貼り、返ってきた答えを「回答を貼り付け」へ。", "Copied the fix request. Paste it into the same chat, then paste its answer with \"Paste the answer\"."), false);
    }

    /// <summary>チャットの答えを貼って、API のときと同じように検査して見せる。</summary>
    private void PasteAnswer()
    {
        string start = Clipboard.ContainsText() && Clipboard.GetText().Contains('{') ? Clipboard.GetText() : "";
        var box = new TextBox { Text = start, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 320, FontFamily = new System.Windows.Media.FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dialog = new Window
        {
            Title = T("回答を貼り付け", "Paste the answer"), Width = 640, SizeToContent = SizeToContent.Height, Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Theme.Brush(Theme.Palette.Background),
        };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(Ui.Note(T("AI のチャットの答えを、そのまま貼る(JSON のかたまりを探して読む)。", "Paste the chat's answer as is (the JSON block is found and read).")));
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = Ui.Button(T("読み込む", "Read it"), () => dialog.DialogResult = true);
        ok.Style = (Style)Application.Current.FindResource("AccentButton");
        buttons.Children.Add(ok);
        buttons.Children.Add(Ui.Button(T("やめる", "Cancel"), () => dialog.DialogResult = false));
        panel.Children.Add(buttons);
        dialog.Content = panel;
        if (dialog.ShowDialog() != true) return;
        ReadAnswer(box.Text);
    }

    /// <summary>貼った答えを読んで検査し、メモ・問題・下書きを出す(--ai-paste の確かめでも使う)。</summary>
    public void ReadAnswer(string answer)
    {
        var (draft, items, note, differences, error, wrapped) = DraftWriter.ParseChat(answer);
        if (draft == null)
        {
            Say(T("答えを読めませんでした: ", "Could not read the answer: ") + error, true);
            return;
        }
        var problems = Check(draft, items);
        ShowMemo(note, differences, problems.Count > 0, unwrapped: !wrapped);
        _result.Text = draft.ToJsonString(Pretty);
        _items.Text = ItemsText(items);
        ShowSummary(draft, items);
        ShowProblems(problems);
        _applied.Text = "";
        _replace.IsEnabled = _add.IsEnabled = true;
        _unused = true;
        Say(problems.Count == 0 ? T("貼った答えを読みました。", "Read the pasted answer.")
            : T("貼った答えを読みましたが、検査で問題が見つかりました。「修正依頼をコピー」で同じチャットに直してもらえる。", "Read the pasted answer, but the check found problems. Use \"Copy a fix request\" to have the same chat fix it."), problems.Count > 0);
    }

    private async Task WriteAsync()
    {
        if (Busy) return;
        if (_wish.Text.Trim().Length == 0)
        {
            Say(T("こうしたいことを書いてください", "Write what you want"), true);
            return;
        }
        if (_context.Ai(out var noKey) is not { } client)
        {
            Say(noKey!, true);
            return;
        }
        var brief = Brief();
        // 作っているあいだは押せない(続けて押して、二重にお金がかからないように)
        Busy = true;
        _write.IsEnabled = _replace.IsEnabled = _add.IsEnabled = false;
        _applied.Text = "";
        var result = await DraftWriter.WriteAsync(client, brief, Check, CancellationToken.None, tries => Say(tries == 1
            ? T($"下書きを作成中…({client.Model})", $"Making the draft… ({client.Model})")
            : T("検査で見つかった問題を修正中…", "Fixing the problems the check found…"), false));
        Busy = false;
        _write.IsEnabled = true;
        var used = result.Used;
        _tokens.Text = T($"使ったトークン: 入力 {used.Input + used.CacheRead + used.CacheWrite:N0}(うちキャッシュから読んだ {used.CacheRead:N0}・キャッシュに書いた {used.CacheWrite:N0})、出力 {used.Output:N0}",
            $"Tokens used: input {used.Input + used.CacheRead + used.CacheWrite:N0} (read from cache {used.CacheRead:N0}, written to cache {used.CacheWrite:N0}), output {used.Output:N0}");
        if (result.Error != null && result.Draft == null)
        {
            Say(T("作れませんでした: ", "Could not make it: ") + result.Error, true);
            return;
        }
        ShowMemo(result.Note, result.Differences, result.Problems.Count > 0);
        if (result.Draft is { } draft)
        {
            _result.Text = draft.ToJsonString(Pretty);
            _items.Text = ItemsText(result.Items);
            ShowSummary(draft, result.Items);
        }
        ShowProblems(result.Problems);
        Say(result.Problems.Count == 0
            ? T($"{client.Model} が下書きを作りました({result.Tries} 回)。", $"{client.Model} made the draft ({result.Tries} tries).")
            : T($"{client.Model} が下書きを作りましたが、{result.Tries} 回で直しきれない問題があります。JSON を直すか、もう一度頼む", $"{client.Model} made the draft, but problems remain after {result.Tries} tries. Fix the JSON or ask again"), result.Problems.Count > 0);
        _replace.IsEnabled = _add.IsEnabled = result.Draft != null;
        _unused = result.Draft != null;
    }

    /// <summary>AI からの報告: 作ったもの(ふつうの文)と、あなたの希望に沿えなかった内容(1つずつ黄色の注意)。JSON より先に読んでもらう。</summary>
    /// <param name="failedCheck">検査で問題が出ている(「希望通りにできた」の行に注意を添える。AI の申告どおりには動かないかもしれない)。</param>
    /// <param name="unwrapped">チャットの答えが返事の形を守らず、下書きだけだった(メモも違うところも無い)。</param>
    private void ShowMemo(string? note, List<string> differences, bool failedCheck, bool unwrapped = false)
    {
        _memoShown = (note, differences, unwrapped);
        _memo.Children.Clear();
        _memo.Visibility = Visibility.Visible;
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = T("AI からの報告(AI の自己申告。実際の中身は下の一覧で確かめる)", "AI's report (self-reported; check the actual contents in the list below)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        if (unwrapped)
            box.Children.Add(Caution(T("この答えには、AI からの報告(希望通りにできた内容・沿えなかった内容)が付いていません(返事の形を守っていない)。頼んでいないことが入っていないか、JSON を特によく見てください。",
                "This answer has no report from the AI (what it made, where it could not follow the request) (it did not follow the reply shape). Check the JSON carefully for anything you did not ask for.")));
        else
        {
            box.Children.Add(new TextBlock { Text = T("あなたの希望通りにできた内容:", "Done as you asked:"), FontWeight = FontWeights.SemiBold });
            if (failedCheck)
                box.Children.Add(new TextBlock { Text = T("(検査で問題が出ています。直すまで、このとおりには動きません。下の問題を見てください)", "(The check found problems. Until they are fixed it will not work as described; see the problems below)"), Foreground = Ui.Warn, TextWrapping = TextWrapping.Wrap });
            box.Children.Add(new TextBlock { Text = note ?? "", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            ShowDifferences(box, differences);
        }
        _memo.Children.Add(new Border
        {
            Child = box, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            BorderBrush = differences.Count > 0 || unwrapped ? Ui.Caution : Ui.Dim,
        });
    }

    /// <summary>あなたの希望に沿えなかった内容: 無ければ薄く「なし」、あれば1つずつ黄色の注意。</summary>
    private static void ShowDifferences(StackPanel box, List<string> differences)
    {
        if (differences.Count == 0)
            box.Children.Add(new TextBlock { Text = T("あなたの希望に沿えなかった内容: なし", "Not done as you asked: none"), Foreground = Ui.Dim, Margin = new Thickness(0, 4, 0, 0) });
        else
        {
            box.Children.Add(new TextBlock { Text = T("あなたの希望に沿えなかった内容:", "Not done as you asked:"), Foreground = Ui.Caution, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            foreach (var d in differences) box.Children.Add(Caution(d));
        }
    }

    /// <summary>
    /// 中身の一覧を出す。エディタが JSON から作るので、AI からの報告(AI の自己申告)と違って必ず実データどおり。
    /// 食い違いに気づけるように、AI からの報告のすぐ下・JSON の上に置く。
    /// </summary>
    private void ShowSummary(JsonObject draft, IReadOnlyList<JsonObject> items)
    {
        var summary = new DraftSummary(_context) { Extra = [.. items] };
        var lines = _kind switch
        {
            DraftWriter.Kind.Event => summary.Event(draft),
            DraftWriter.Kind.Item => summary.Item(draft),
            _ => summary.Monster(draft),
        };
        _summary.Children.Clear();
        _summary.Visibility = Visibility.Visible;
        var box = new StackPanel();
        box.Children.Add(new TextBlock { Text = T("中身の一覧(エディタが JSON から作る実際の中身。AI の報告と食い違っていないか見る)", "Contents (the actual data, made by the editor from the JSON; compare with the AI's report)"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        foreach (var line in lines) box.Children.Add(new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap });
        foreach (var item in items)
        {
            box.Children.Add(new TextBlock { Text = T("新しい品: ", "New item: ") + ((string?)item["id"] ?? "?"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var line in summary.Item(item)) box.Children.Add(new TextBlock { Text = "  " + line, TextWrapping = TextWrapping.Wrap });
        }
        _summary.Children.Add(new Border { Child = box, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), BorderBrush = Ui.Dim });
    }

    /// <summary>
    /// 強さを試す欄(モンスターだけ)。Sim と同じ6人のパーティ(レベルは選ぶ)と、下書きのモンスターを戦わせ、元のモンスターと並べて出す。
    /// </summary>
    private UIElement StrengthPanel()
    {
        _strengthLevel.Text = Math.Clamp((int)Math.Ceiling(_context.Pack?.LastLevel ?? 5), 1, 20).ToString();
        var row = new WrapPanel();
        row.Children.Add(new TextBlock { Text = T("強さを試す: パーティのレベル", "Test the strength: party level"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(_strengthLevel);
        row.Children.Add(new TextBlock { Text = T("　敵の数(空なら出る数の平均)", "  enemies (blank: average appearing)"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        row.Children.Add(_strengthCount);
        _strengthButton = Ui.Button(T("戦わせる", "Fight"), () => _ = TestStrength(),
            T($"ファイター2・パラディン・ローグ・レンジャー・クレリックの6人と {StrengthFights} 回戦わせる(AI は呼ばない。お金はかからない)", $"Fights {StrengthFights} times against six adventurers (fighter x2, paladin, rogue, ranger, cleric). Does not call the AI"));
        _strengthButton.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(_strengthButton);
        var box = new StackPanel();
        box.Children.Add(row);
        box.Children.Add(Ui.Note(T($"6人のパーティ(ファイター2・パラディン・ローグ・レンジャー・クレリック)と戦わせ、元のモンスターと並べる。レベル 1〜{ScanTop} も順に試して、6人なら何レベルに合うかも出す。AI は呼ばないのでお金はかからない",
            $"Fights a party of six (fighter x2, paladin, rogue, ranger, cleric) and compares with the base monster. Also tries levels 1-{ScanTop} to show which levels it suits. Does not call the AI, so it costs nothing")));
        box.Children.Add(_strengthResult);
        return new Border { Child = box, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), BorderBrush = Ui.Dim, Margin = new Thickness(0, 0, 0, 6) };
    }

    /// <summary>下書きのモンスター(と元のモンスター)を、選んだレベルの6人と戦わせる(--ai-draft の確かめでも呼ぶ)。</summary>
    public async Task TestStrength()
    {
        _strengthResult.Children.Clear();
        void Line(string text, System.Windows.Media.Brush? color = null) => _strengthResult.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = color ?? Ui.Dim });
        if (TryParse(_result.Text) is not { } draft || (string?)draft["id"] is not { Length: > 0 } id)
        {
            Line(T("下書きの JSON を読めません", "Cannot read the draft JSON"), Ui.Warn);
            return;
        }
        int level = int.TryParse(_strengthLevel.Text, out var lv) ? Math.Clamp(lv, 1, 20) : 1;
        int? asked = int.TryParse(_strengthCount.Text, out var n) ? Math.Clamp(n, 1, 12) : null;
        var (data, problem) = _context.DataWith("monsters", draft);
        if (data == null || !data.Monsters.TryGetValue(id, out var mine))
        {
            Line(T("下書きのモンスターを読めません: ", "Cannot load the drafted monster: ") + problem, Ui.Warn);
            return;
        }
        int count = asked ?? AverageAppearing(mine);
        string? baseId = (string?)draft["extends"] ?? _extend;
        var original = baseId != null ? data.Monsters.GetValueOrDefault(baseId) : null;
        if (_strengthButton != null) _strengthButton.IsEnabled = false;
        Line(T("戦わせています…", "Fighting…"));
        var (a, b) = await Task.Run(() =>
        {
            List<Core.Characters.Character> Party(Core.Rng.IRandom _) =>
                StrengthParty.Select((c, i) => Core.Characters.CharacterFactory.QuickCreate(data, $"#{i + 1}", "human", c, level)).ToList();
            var drafted = Core.Combat.CombatSimulator.Run(data, StrengthFights, 7, Party, _ => [(mine, count)]);
            var baseline = original != null ? Core.Combat.CombatSimulator.Run(data, StrengthFights, 7, Party, _ => [(original, count)]) : null;
            return (drafted, baseline);
        });
        var (fitMine, fitBase) = await Task.Run(() => (Fit(data, mine, count), original != null ? Fit(data, original, count) : null));
        if (_strengthButton != null) _strengthButton.IsEnabled = true;
        _strengthResult.Children.Clear();
        Line(T($"レベル {level} の6人と {StrengthFights} 回(同じ乱数):", $"{StrengthFights} fights against six level-{level} adventurers (same dice):"));
        string Row(string label, Core.Combat.SimulationResult r) =>
            T($"{label}×{count}: 勝ち {r.WinRate:P0}　全滅 {(double)r.Defeats / r.Fights:P0}　平均 {r.AverageRounds:F1} ラウンド　死者 {r.AverageDeaths:F2} 人　残り HP {r.AverageHpLeft:P0}",
              $"{label} x{count}: win {r.WinRate:P0}  wipe {(double)r.Defeats / r.Fights:P0}  {r.AverageRounds:F1} rounds  {r.AverageDeaths:F2} dead  HP left {r.AverageHpLeft:P0}");
        _strengthResult.Children.Add(new TextBlock { Text = Row(Loc.Name(mine.Name) ?? id, a) + $"　(CR {mine.Cr:0.###})", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        if (b != null && original != null) Line(Row(T("元の ", "Base: ") + (Loc.Name(original.Name) ?? baseId), b) + $"　(CR {original.Cr:0.###})");
        // 手ごたえの目安(残り HP と全滅): 文は目安だけで、決めるのは作者
        string feel = a.Defeats > a.Fights / 10 ? T("このレベルの6人には手ごわすぎる(全滅が 1 割を超える)", "Too hard for six at this level (wipes over 10%)")
            : a.AverageHpLeft > 0.85 ? T("このレベルの6人には楽(残り HP が 85% を超える)", "Easy for six at this level (over 85% HP left)")
            : T("このレベルの6人に、ほどよい手ごたえ", "A fair fight for six at this level");
        Line(T("目安: ", "Rough feel: ") + feel);
        _strengthResult.Children.Add(new TextBlock { Text = T($"6人なら(×{count}): ", $"For six (x{count}): ") + fitMine, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        if (fitBase != null) Line(T("元は: ", "Base: ") + fitBase);
    }

    /// <summary>出る数の平均(1d6 なら 3。読めなければ 1)。</summary>
    private static int AverageAppearing(Core.Monsters.MonsterDef m)
    {
        try { return Math.Clamp((int)Math.Round(Core.Rules.DiceExpression.Parse(m.Appearing).Average), 1, 12); }
        catch (FormatException) { return 1; }
    }

    /// <summary>
    /// レベル 1〜ScanTop の6人と順に戦わせ、手ごわすぎ(全滅が 1 割を超える)・ほどよい・楽(残り HP が 85% を超える)のレベルの範囲を文にする。
    /// </summary>
    private static string Fit(Core.Characters.GameData data, Core.Monsters.MonsterDef m, int count)
    {
        var kinds = new List<int>();   // 0 手ごわすぎ、1 ほどよい、2 楽
        for (int level = 1; level <= ScanTop; level++)
        {
            int lv = level;
            var r = Core.Combat.CombatSimulator.Run(data, ScanFights, 11, _ => StrengthParty.Select((c, i) => Core.Characters.CharacterFactory.QuickCreate(data, $"#{i + 1}", "human", c, lv)).ToList(), _ => [(m, count)]);
            kinds.Add(r.Defeats > r.Fights / 10 ? 0 : r.AverageHpLeft > 0.85 ? 2 : 1);
        }
        string[] names = [T("手ごわすぎ", "too hard"), T("ほどよい", "fair"), T("楽", "easy")];
        var parts = new List<string>();
        for (int i = 0; i < kinds.Count;)
        {
            int j = i;
            while (j + 1 < kinds.Count && kinds[j + 1] == kinds[i]) j++;
            string range = i == j ? $"{i + 1}" : j == kinds.Count - 1 ? T($"{i + 1} 以上", $"{i + 1}+") : $"{i + 1}〜{j + 1}";
            parts.Add(T($"レベル {range} は{names[kinds[i]]}", $"level {range}: {names[kinds[i]]}"));
            i = j + 1;
        }
        return string.Join(T("、", ", "), parts);
    }

    /// <summary>JSON を1つのオブジェクトとして読む(読めなければ null)。</summary>
    private static JsonObject? TryParse(string text)
    {
        try { return JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return null; }
    }

    /// <summary>下書きを検査する(id と、品の出し方はこちらで決めたものにそろえてから)。</summary>
    private List<string> Check(JsonObject draft, IReadOnlyList<JsonObject> items)
    {
        draft["id"] = _id;
        if (_kind == DraftWriter.Kind.Item)
        {
            draft.Remove("drop_only");
            if (_eventOnly.IsChecked == true) draft["no_random"] = true;
            else draft.Remove("no_random");
        }
        return _check(draft, items);
    }

    /// <summary>
    /// 追加する前にもう一度検査する(手で直したときのため)。問題があっても、確かめてから入れられる。
    /// 入れたあとも窓は開いたままにして、保存するまでファイルに書かないことを知らせる。
    /// </summary>
    private void Apply(bool replace)
    {
        JsonObject draft;
        List<JsonObject> items;
        try
        {
            if (JsonNode.Parse(_result.Text) is not JsonObject o) throw new JsonException(T("1つのオブジェクトにしてください", "Use a single object"));
            draft = o;
            items = Items();
        }
        catch (JsonException ex)
        {
            ShowProblems([T("JSON を読めません: ", "Cannot read the JSON: ") + ex.Message]);
            return;
        }
        var problems = Check(draft, items);
        ShowProblems(problems);
        if (problems.Count > 0 && MessageBox.Show(this, T($"問題が {problems.Count} 件あります。それでも入れますか？(検査のタブでも出る)", $"There are {problems.Count} problem(s). Apply anyway? (They will also show in the Check tab)"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        _apply(draft, items, replace);
        _unused = false;
        _replace.IsEnabled = _add.IsEnabled = false;
        string withItems = items.Count > 0 ? T($"新しい品 {items.Count} つも、データの品に入れました。", $" Also added {items.Count} new item(s) to the Data tab.") : "";
        _applied.Text = (replace
            ? T("元を置き換えました。", "Replaced the base.")
            : T("追加しました。", "Added.")) + withItems + T("保存するとファイルに反映されます。", " It is written to the files when you save.");
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>新しい品の並びを、欄に出す文に。</summary>
    private static string ItemsText(IReadOnlyList<JsonObject> items) => new JsonArray(items.Select(i => (JsonNode)i.DeepClone()).ToArray()).ToJsonString(Pretty);

    /// <summary>新しい品の欄を読む(イベントでなければ空。読めなければ JsonException)。</summary>
    private List<JsonObject> Items()
    {
        if (_kind != DraftWriter.Kind.Event || _items.Text.Trim().Length == 0) return [];
        if (JsonNode.Parse(_items.Text) is not JsonArray list) throw new JsonException(T("新しい品は [ ] の並びにしてください", "Write the new items as a [ ] list"));
        return list.Select(x => x as JsonObject ?? throw new JsonException(T("新しい品の並びに、オブジェクトでないものがあります", "The new item list has something that is not an object"))).Select(x => x.DeepClone().AsObject()).ToList();
    }

    /// <summary>新しい品の欄を読む(読めなければ null)。</summary>
    private List<JsonObject>? TryItems()
    {
        try { return Items(); }
        catch (JsonException) { return null; }
    }

    /// <summary>警告を出さずに閉じる(確かめの起動で、画面を保存したあと)。</summary>
    public void CloseQuietly()
    {
        _unused = false;
        Close();
    }

    /// <summary>AI に書かせた下書きを入れずに閉じる(書かせている途中も)なら、確かめる。一度も書かせていなければ聞かない。</summary>
    private void ConfirmClose(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!Busy && !_unused) return;
        string text = Busy
            ? T("AI が下書きを作っている途中です。閉じると、作ったものは捨てます(料金はかかります)。閉じますか？", "The AI is still making the draft. If you close, it is thrown away (the request is still charged). Close anyway?")
            : T("AI の下書きを、まだ追加していません。閉じると捨てます(もう一度作ると、また料金がかかる)。閉じますか？", "You have not added the AI's draft. Closing throws it away (making it again is charged again). Close anyway?");
        // 既定は「いいえ」(Enter で、うっかり捨てないように)
        if (MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
    }

    /// <summary>検査の結果。通っても、中身が希望どおりかは作者が見る(検査はデータの形と参照だけを見る)。</summary>
    private void ShowProblems(List<string> problems)
    {
        // 検査の結果が変わったら(JSON を手で直したなど)、報告の「希望通りにできた」の注意も合わせる
        if (_memoShown is var (n, d, u) && (_lastProblems.Count > 0) != (problems.Count > 0)) ShowMemo(n, d, problems.Count > 0, u);
        _lastProblems = problems;
        _fixCopy.IsEnabled = problems.Count > 0;
        _problems.Foreground = problems.Count == 0 ? Ui.Good : Ui.Warn;
        _problems.Text = problems.Count == 0
            ? T("○ データ検査OK。効果・数値・説明が希望どおりかは、AI からの報告と中身の一覧で確認してください。", "○ Data check OK. Check in the AI's report and the contents list that the effects, numbers and descriptions are what you wanted.")
            : T($"× 問題 {problems.Count} 件:\n", $"× {problems.Count} problem(s):\n") + string.Join("\n", problems.Select(p => "・" + p));
    }

    private void Say(string text, bool warn)
    {
        _status.Text = text;
        _status.Foreground = warn ? Ui.Warn : Ui.Dim;
    }

    /// <summary>
    /// AI への手がかりのうち、頼み事が変わっても同じもの(標準のパックの id の一覧など)。プロンプトキャッシュに入るので、
    /// 選んだ元やシナリオの中身で変わるものは入れない(Specific へ)。
    /// </summary>
    private string Context()
    {
        var data = _context.BaseData;
        var text = new StringBuilder();
        void List(string label, IEnumerable<string> ids) => text.AppendLine($"{label}: {string.Join(", ", ids)}");
        var monsters = data.Monsters.Values.OrderBy(m => m.Cr).ThenBy(m => m.Id, StringComparer.Ordinal).Select(m => $"{m.Id}:{m.Cr:0.###}");
        var conditions = Enum.GetNames<Core.Effects.Condition>().Select(JsonNamingPolicy.SnakeCaseLower.ConvertName);
        switch (_kind)
        {
            case DraftWriter.Kind.Event:
                List("Standard items", data.Items.Keys.Order(StringComparer.Ordinal));
                List("Standard monsters (id:cr; for combat groups and art. Pick monsters whose CR fits the level band: a group's CRs add up to about the party level, a single boss up to about the party level)", monsters);
                List("Skills", data.Skills.Keys.Order());
                List("Classes", data.Classes.Keys.Order());
                List("Races", data.Races.Keys.Order());
                List("Sounds", Sounds(data));
                List("Conditions", conditions);
                List("Damage types", Core.Effects.DamageTypes.All);
                break;
            case DraftWriter.Kind.Item:
                List("Standard items (for extends, base, looks_like)", data.Items.Keys.Order(StringComparer.Ordinal));
                List("Spells (for use_spell and similar)", data.Spells.Keys.Order(StringComparer.Ordinal));
                List("Damage types", Core.Effects.DamageTypes.All);
                List("Conditions", conditions);
                break;
            default:
                List("Monsters (id:cr; for extends)", monsters);
                List("Traits that work in combat (other traits are only labels and do nothing; use actions, resist, immune or condition_immune instead)", Core.Monsters.MonsterDef.WorkingTraits);
                List("Spells", data.Spells.Keys.Order(StringComparer.Ordinal));
                List("Damage types", Core.Effects.DamageTypes.All);
                List("Conditions", conditions);
                break;
        }
        return text.ToString();
    }

    /// <summary>この頼み事だけの手がかり: 使う id、元の写し方、シナリオのレベル帯、シナリオの中の id、似た標準の品。</summary>
    private string Specific()
    {
        var pack = _context.Pack!;
        var text = new StringBuilder();
        text.AppendLine($"Use this id: {_id}");
        if (_kind != DraftWriter.Kind.Event)
            text.AppendLine("Extend from: " + (_extend ?? "(none: the base is a full copy; keep every field written out)"));
        text.AppendLine($"Scenario: {pack.Id}, {pack.Floors.Count} floors, world level band {pack.FirstLevel:0.#} to {pack.LastLevel:0.#} (party level from the first floor to the last)");
        void List(string label, IEnumerable<string> ids) => text.AppendLine($"{label}: {string.Join(", ", ids)}");
        IEnumerable<string> Own(string collection) =>
            (pack.DataLists().FirstOrDefault(x => x.Collection == collection).List ?? []).Select(o => (string?)o["id"] ?? "").Where(i => i.Length > 0 && i != _id);
        switch (_kind)
        {
            case DraftWriter.Kind.Event:
                List("Events in this scenario (for done / run)", pack.Events.Select(e => e.Id).Where(i => i != _id));
                List("Flags used by this scenario's events", pack.Events.SelectMany(Core.Events.EventDraftCheck.Conditions).Where(c => c.Type == "flag" && c.Id != null).Select(c => c.Id!).Distinct());
                List("This scenario's own items", Own("items"));
                List("This scenario's own monsters", Own("monsters"));
                break;
            case DraftWriter.Kind.Item:
                text.AppendLine($"Event-only item: {(_eventOnly.IsChecked == true ? "yes (no_random: true; only events and drop tables give it)" : "no (it may appear in treasure rolls and shops)")}");
                List("Similar standard items (id: rarity, price, bonus; for balance)", Similar(_context.BaseData));
                break;
        }
        return text.ToString();
    }

    /// <summary>
    /// 元に似た標準の品(元と同じ品か、元を base に持つ品か、元と同じ種類で同じ base を持つ品)。等級と値段の釣り合いの手がかり。
    /// </summary>
    private IEnumerable<string> Similar(Core.Characters.GameData data)
    {
        string? root = _extend ?? (string?)_base?["base"] ?? (string?)_base?["id"];
        if (root == null || !data.Items.TryGetValue(root, out var origin)) return [];
        string family = origin.Base ?? origin.Id;
        return data.Items.Values
            .Where(i => i.Id == family || i.Base == family || (i.Kind == origin.Kind && i.Base != null && i.Base == origin.Base))
            .OrderBy(i => i.Price).Take(15)
            .Select(i => $"{i.Id}: {i.Rarity}, {i.Price:0.##}, +{i.Bonus}");
    }

    /// <summary>標準のイベントで使っている効果音の id。</summary>
    private static IEnumerable<string> Sounds(Core.Characters.GameData data)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        void Walk(IEnumerable<Core.Events.EventStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type == "sound" && s.Id != null) found.Add(s.Id);
                foreach (var child in s.Children()) Walk(child);
            }
        }
        foreach (var e in data.Events.Values) Walk(e.Steps);
        return found;
    }
}
