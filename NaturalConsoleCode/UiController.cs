using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace NaturalConsole.NaturalConsoleCode;

/// <summary>
/// A minimal in-game chat box: press the configured hotkey (default F8) to open, type natural
/// language, press Enter to execute. Supports command history (Up/Down), Tab auto-completion of
/// entity names, and a built-in help.
/// </summary>
public partial class UiController : CanvasLayer
{
    private const string ConfigRelPath = "NaturalConsole/config.json";

    private static UiController? _instance;
    private static Key _hotkey;
    private static bool _hotkeyLoaded;

    private PanelContainer _panel = null!;
    private RichTextLabel _output = null!;
    private LineEdit _input = null!;

    private readonly List<string> _history = new();
    private int _historyIndex;
    private string _savedPending = "";

    private List<string> _completions = new();
    private int _completionIndex;
    private string _completionBase = "";
    private int _completionSuffix;
    private string _completedText = "\u0000";

    private List<Choice>? _pendingChoices;
    private ulong _keepFocusUntil;

    public static void TryCreate()
    {
        if (_instance != null)
            return;

        var tree = Engine.GetMainLoop() as SceneTree;
        Node? root = tree?.Root;
        if (root == null)
        {
            var timer = tree?.CreateTimer(0.5);
            if (timer != null)
                timer.Timeout += TryCreate;
            return;
        }

        _instance = new UiController();
        _instance.BuildUi();
        _instance.Hide();

        // Defer adding to the tree: during mod initialization the root is busy setting
        // up its children, so a direct AddChild would fail.
        Callable.From(() => root.AddChild(_instance)).CallDeferred();
        MainFile.Logger.Info("NaturalConsole UI scheduled, hotkey=" + HotkeyName());
    }

    public override void _Ready()
    {
        MainFile.Logger.Info("NaturalConsole UI ready in tree. Press " + HotkeyName() + " to toggle.");
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
            return;

        if (key.Keycode == Hotkey())
        {
            Toggle();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (!Visible || !_input.HasFocus())
            return;

        if (key.Keycode == Key.Up)
        {
            NavigateHistory(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Down)
        {
            NavigateHistory(1);
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Tab)
        {
            CompleteTab();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape)
        {
            Hide();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Toggle()
    {
        Visible = !Visible;
        if (Visible)
        {
            _input.Clear();
            _input.GrabFocus();
        }
    }

    private void NavigateHistory(int direction)
    {
        if (_history.Count == 0)
            return;

        if (_historyIndex == _history.Count)
            _savedPending = _input.Text;

        _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        _input.Text = _historyIndex < _history.Count ? _history[_historyIndex] : _savedPending;
        _input.CaretColumn = _input.Text.Length;
    }

    private void CompleteTab()
    {
        bool repeat = _input.Text == _completedText;
        if (!repeat)
        {
            _completions = EntityIndex.FindCompletions(_input.Text, out int suffix);
            _completionBase = _input.Text;
            _completionSuffix = suffix;
            _completionIndex = -1;
            if (_completions.Count == 0)
                return;
        }
        else if (_completions.Count == 0)
        {
            return;
        }

        _completionIndex = (_completionIndex + 1) % _completions.Count;
        string chosen = _completions[_completionIndex];
        string prefix = _completionBase.Substring(0, _completionBase.Length - _completionSuffix);
        _input.Text = prefix + chosen;
        _input.CaretColumn = _input.Text.Length;
        _completedText = _input.Text;

        if (!repeat && _completions.Count > 1)
        {
            Append("[color=#888888]候选：" + string.Join(" / ", _completions) + "[/color]");
        }
    }

    private void BuildUi()
    {
        Layer = 100;

        _panel = new PanelContainer
        {
            Position = new Vector2(40, 40),
            CustomMinimumSize = new Vector2(760, 460),
        };
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);
        margin.AddChild(vbox);

        var title = new Label { Text = "自然语言控制台（" + HotkeyName() + " 开关 / 回车执行 / ↑↓历史 / Tab补全）" };
        vbox.AddChild(title);

        _output = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddChild(_output);

        _input = new LineEdit { PlaceholderText = "例如：给我999金币 / 加一张打击 / 无敌 / 帮助" };
        _input.TextSubmitted += OnSubmit;
        vbox.AddChild(_input);

        Font font = CreateCjkFont();
        title.AddThemeFontOverride("font", font);
        _output.AddThemeFontOverride("normal_font", font);
        _output.AddThemeFontOverride("bold_font", font);
        _output.AddThemeFontOverride("italics_font", font);
        _output.AddThemeFontOverride("bold_italics_font", font);
        _input.AddThemeFontOverride("font", font);

        Append("[color=#88cc88]输入自然语言即可执行控制台指令，输入“帮助”查看功能。[/color]");
        Append("[color=#888888]示例：给我999金币 / 回满血 / 加一张打击 / 无敌 / 秒杀 / 去第三幕[/color]");
    }

    private void OnSubmit(string text)
    {
        ProcessSubmit(text);
        // Keep the input focused for a short window: the console command queues a game action that
        // can steal focus over the next frames. _Process re-grabs focus every frame in this window.
        _keepFocusUntil = Time.GetTicksMsec() + 700;
        _input.GrabFocus();
    }

    public override void _Process(double delta)
    {
        if (_keepFocusUntil != 0 && Time.GetTicksMsec() < _keepFocusUntil)
        {
            if (Visible && GodotObject.IsInstanceValid(_input) && !_input.HasFocus())
            {
                _input.GrabFocus();
            }
        }
    }

    private void ProcessSubmit(string text)
    {
        string trimmed = text.Trim();
        _input.Clear();

        if (trimmed.Length == 0)
            return;

        _history.Add(trimmed);
        _historyIndex = _history.Count;
        _savedPending = "";

        Append("[b]> " + trimmed + "[/b]");

        if (_pendingChoices != null)
        {
            if (int.TryParse(trimmed, out int idx) && idx >= 1 && idx <= _pendingChoices.Count)
            {
                Choice choice = _pendingChoices[idx - 1];
                _pendingChoices = null;
                Append("[color=#aaccff]→ " + choice.Command + "[/color]");
                Append(ConsoleExecutor.Execute(choice.Command));
            }
            else
            {
                Append("[color=#ff8888]请输入 1-" + _pendingChoices.Count + " 之间的序号选择，或输入新指令。[/color]");
                ShowChoices(_pendingChoices);
            }

            return;
        }

        if (IsHelpRequest(trimmed))
        {
            Append(HelpText());
            return;
        }

        if (IsClearRequest(trimmed))
        {
            _output.Clear();
            return;
        }

        ParseResult parsed = NaturalLanguageParser.Parse(trimmed);
        if (parsed.Choices != null)
        {
            _pendingChoices = parsed.Choices;
            Append("[color=#ffcc88]有多个匹配，请输入序号选择：[/color]");
            ShowChoices(parsed.Choices);
            return;
        }

        if (parsed.Command == null)
        {
            string? llmCommand = LlmTranslator.TryTranslate(trimmed);
            if (llmCommand != null)
            {
                Append("[color=#aaccff]→ " + llmCommand + "[/color]");
                string llmMsg = ConsoleExecutor.Execute(llmCommand);
                Append(llmMsg);
            }
            else
            {
                Append("[color=#ff8888]" + parsed.Error + "[/color]");
            }

            return;
        }

        Append("[color=#aaccff]→ " + parsed.Command + "[/color]");
        string msg = ConsoleExecutor.Execute(parsed.Command);
        Append(msg);
    }

    private void ShowChoices(List<Choice> choices)
    {
        for (int i = 0; i < choices.Count; i++)
        {
            Append("  " + (i + 1) + ". " + choices[i].Label);
        }
    }

    private static bool IsHelpRequest(string t)
    {
        string s = t.Trim().ToLowerInvariant();
        return s is "帮助" or "help" or "怎么用" or "使用说明" or "功能" or "说明" or "指令" or "?" or "？";
    }

    private static bool IsClearRequest(string t)
    {
        string s = t.Trim().ToLowerInvariant();
        return s is "清屏" or "clear" or "清除" or "清空";
    }

    private static string HelpText()
    {
        return
            "[color=#aaccff][b]===== 支持的自然语言功能 =====[/b][/color]\n" +
            "[color=#aaccff]资源[/color]\n" +
            "  给我999金币 / 加100金币  →  金币\n" +
            "  回满血 / 治疗30  →  生命\n" +
            "  加3能量  →  能量\n" +
            "  加20格挡 / 加20护甲  →  格挡\n" +
            "  造成100伤害  →  对敌人伤害\n" +
            "  抽3张牌  →  抽牌\n" +
            "  加10星星  →  辉星\n" +
            "[color=#aaccff]物品（Tab 可补全名字）[/color]\n" +
            "  加一张打击 / 卡牌 打击  →  加1张到手中\n" +
            "  加5张打击 / 给我999张打击  →  加N张（会问放哪个牌堆）\n" +
            "  加5张打击到弃牌堆  →  加N张到指定牌堆（手牌/牌组/抽牌堆/弃牌堆/消耗牌堆）\n" +
            "  加一张升级的打击 / 升级神化 / 神化+  →  加升级版卡牌\n" +
            "  加5张神化+ / 5张升级的神化  →  加N张升级版卡牌\n" +
            "  遗物 灯笼 / 移除遗物 灯笼  →  添加/移除遗物\n" +
            "  药水 熵增药剂 / 药水 荆棘药水  →  加药水（支持<效果>药水俗名）\n" +
            "  状态 荆棘 / 状态 力量 3  →  加状态/能力\n" +
            "  升级第一张牌 / 升级最左边  →  升级手牌\n" +
            "  移除 打击  →  移除卡牌\n" +
            "[color=#aaccff]战斗[/color]\n" +
            "  无敌 / 上帝模式  →  godmode\n" +
            "  秒杀 / 直接获胜  →  win\n" +
            "  杀死全部敌人 / 杀死第一个敌人  →  kill\n" +
            "  自杀 / 死亡  →  die\n" +
            "  加速 / 即时模式  →  instant\n" +
            "[color=#aaccff]跳转与进度[/color]\n" +
            "  去第三幕  →  act 3\n" +
            "  进入战斗 / 触发事件 / 房间  →  fight/event/room\n" +
            "  自由移动 / 旅行  →  travel\n" +
            "  解锁全部  →  unlock all\n" +
            "[color=#aaccff]操作[/color]\n" +
            "  ↑↓ 浏览历史指令，Tab 补全名称，Esc 关闭，输入“帮助”查看本说明。\n" +
            "  名字有歧义时会列出选项，输入序号选择即可。\n" +
            "  输入“清屏”或“clear”可清空输出。";
    }

    private void Append(string text)
    {
        _output.AppendText(text + "\n");
    }

    private static Font CreateCjkFont()
    {
        return new SystemFont
        {
            FontNames = new[] { "Microsoft YaHei", "Microsoft YaHei UI", "SimHei", "Noto Sans CJK SC", "PingFang SC", "sans-serif" },
        };
    }

    private static Key Hotkey()
    {
        if (!_hotkeyLoaded)
        {
            _hotkeyLoaded = true;
            _hotkey = LoadHotkey();
        }

        return _hotkey;
    }

    private static string HotkeyName()
    {
        Key k = Hotkey();
        return k == Key.F8 ? "F8" : k.ToString();
    }

    private static Key LoadHotkey()
    {
        try
        {
            string path = Path.Combine(OS.GetUserDataDir(), ConfigRelPath);
            if (File.Exists(path))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("hotkey", out JsonElement e) && e.ValueKind == JsonValueKind.String)
                {
                    string s = e.GetString()!.Trim();
                    if (s.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(1), out int n) && n >= 1 && n <= 12)
                    {
                        return (Key)((int)Key.F1 + n - 1);
                    }

                    if (Enum.TryParse<Key>(s, true, out Key named) && named != Key.None)
                    {
                        return named;
                    }
                }
            }
        }
        catch
        {
            // fall back to default
        }

        return Key.F8;
    }
}
