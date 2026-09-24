// Игровая IDE в стиле VS Code на UI Toolkit. Панель рисуется в RenderTexture, которая стоит материалом
// на экране монитора игрока: IDE видно в офисе даже издалека. Когда стажёр сидит за компьютером,
// GameRoot передаёт сюда события мыши и клавиатуры из OnGUI (координаты уже пересчитаны на экран монитора).
// Логика задач (запуск, проверка, отладка, подсказки, дедлайн) — та же, что была в IdeWindow.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Intern.Py;

namespace Intern.Game
{
    public class IdeScreen
    {
        readonly GameRoot g;
        public TaskData Task;
        public bool Active { get; private set; }
        public RenderTexture Texture { get; private set; }
        readonly int PW, PH;
        readonly GameObject host;
        readonly PanelSettings ps;
        VisualElement root;
        CodeEditor ed;
        readonly Func<Vector2, Vector2?> screenToPanel;   // координаты мыши на экране (как в OnGUI) → точка на панели

        // части интерфейса
        Label commandCenter, crumbs, statusPos, statusErr, statusWarn, statusMode, statusMoney, chipMoney, chipRank, chipDiff, chipDeadline, tabName, sideTitle, rightTitle;
        VisualElement statusBar, tabDot, debugBar, noticeBox, deadlineChip, bottomTabsRow, rightBar;
        Label debugState;
        ScrollBox sideScroll, rightScroll, bottomScroll;
        readonly Dictionary<Side, VisualElement> activityMarks = new Dictionary<Side, VisualElement>();
        readonly Dictionary<Side, Icon> activityIcons = new Dictionary<Side, Icon>();
        readonly Dictionary<Bottom, Label> bottomTabLabels = new Dictionary<Bottom, Label>();
        readonly Dictionary<Bottom, VisualElement> bottomTabMarks = new Dictionary<Bottom, VisualElement>();
        Btn runBtn, debugBtn, checkBtn, resetBtn;
        Label resetLabel;
        Label charProbe;

        enum Side { Task, Files, Debug }
        enum Bottom { Problems, Terminal, DebugConsole, Tests }
        Side side = Side.Task;
        Bottom bottom = Bottom.Terminal;

        // состояние задачи (как в IdeWindow)
        string savedCode = "";
        StringBuilder terminal = new StringBuilder();
        List<CheckResult> lastCheck;
        DebugSession dbg;
        bool solutionShown, confirmReset, theoryOpen = true;
        float confirmResetUntil, noticeUntil;
        List<KeyValuePair<int, string>> explains = new List<KeyValuePair<int, string>>();
        PyError lint;
        string explainedCode;
        readonly Dictionary<string, int> hintsShown = new Dictionary<string, int>();
        readonly Dictionary<string, int> failedChecks = new Dictionary<string, int>();
        readonly Dictionary<string, float> timeSpent = new Dictionary<string, float>();
        readonly HashSet<string> usedSolution = new HashSet<string>();
        readonly HashSet<string> collapsed = new HashSet<string>();
        string dbgSig = "";
        int runtimeErrorLine = -1; string runtimeErrorText;

        // ввод
        Btn pressedBtn, hoverBtn;
        bool pressedEditor;
        ScrollBox pressedScroll;

        Difficulty Diff { get { return (Difficulty)g.Save.difficulty; } }
        bool ExplainPanel { get { return Diff == Difficulty.Easy || g.Save.hasMonitor; } }
        bool RuErrors { get { return Diff != Difficulty.Hard || g.Save.hasDuck; } }
        int HintsShown { get { int v; return Task != null && hintsShown.TryGetValue(Task.id, out v) ? v : 0; } }
        int Fails { get { int v; return Task != null && failedChecks.TryGetValue(Task.id, out v) ? v : 0; } }
        float Spent { get { float v; return Task != null && timeSpent.TryGetValue(Task.id, out v) ? v : 0; } }
        bool Late { get { return Diff == Difficulty.Hard && Task != null && Spent > Task.deadline; } }
        string Code { get { return ed.Text; } }
        string TaskCode(TaskData t) { int i = Array.IndexOf(g.Tasks.tasks, t); return "KOD-" + (101 + Mathf.Max(0, i)); }

        // ======================= создание =======================
        public static IdeScreen TryCreate(GameRoot g, float aspect, Func<Vector2, Vector2?> screenToPanel)
        {
            try { return new IdeScreen(g, aspect, screenToPanel); }
            catch (Exception e) { Debug.LogError("[Стажёр] Новая IDE не запустилась, включаю старую: " + e); return null; }
        }

        IdeScreen(GameRoot g, float aspect, Func<Vector2, Vector2?> screenToPanel)
        {
            this.g = g; this.screenToPanel = screenToPanel;
            PH = 1000; PW = Mathf.RoundToInt(PH * Mathf.Clamp(aspect, 1.2f, 2.4f));
            Texture = new RenderTexture(PW, PH, 24, RenderTextureFormat.ARGB32) { name = "IDE", antiAliasing = 1, useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            Texture.Create();
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "IdePanel";
            ps.targetTexture = Texture;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize; ps.scale = 1f;
            ps.clearColor = true; ps.colorClearValue = K.EditorBg;
            ps.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var ts = ScriptableObject.CreateInstance<PanelTextSettings>();
            if (K.Sans != null) ts.defaultFontAsset = K.Sans;
            ps.textSettings = ts;
            ps.SetScreenToPanelSpaceFunction(p => new Vector2(float.NaN, float.NaN));   // свой ввод — из OnGUI
            host = new GameObject("IdePanel"); host.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(host);
            var doc = host.AddComponent<UIDocument>(); doc.panelSettings = ps;
            host.SetActive(true);
            root = doc.rootVisualElement;
            Build();
            Debug.Log("[Стажёр] IDE на UI Toolkit: " + PW + "×" + PH + ", моноширинный шрифт: " + (K.Mono != null ? K.Mono.name : "нет") + ", обычный: " + (K.Sans != null ? K.Sans.name : "нет"));
        }

        // Отладка (только редактор): снимок текстуры экрана + состояние панели в файл рядом с проектом
        public void DebugDump(string dir)
        {
            var prev = RenderTexture.active; RenderTexture.active = Texture;
            var t = new Texture2D(PW, PH, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, PW, PH), 0, 0); t.Apply(); RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, "ide_dump.png"), t.EncodeToPNG());
            UnityEngine.Object.Destroy(t);
            var doc = host != null ? host.GetComponent<UIDocument>() : null;
            var sb = new StringBuilder();
            sb.AppendLine("host active: " + (host != null && host.activeInHierarchy) + ", doc enabled: " + (doc != null && doc.enabled));
            sb.AppendLine("root panel: " + (root.panel != null) + ", layout: " + root.layout + ", children: " + root.childCount);
            sb.AppendLine("doc root same: " + (doc != null && doc.rootVisualElement == root) + ", texture: " + Texture.width + "x" + Texture.height + " created " + Texture.IsCreated());
            sb.AppendLine("ps target: " + (ps.targetTexture == Texture) + ", clear: " + ps.clearColor + ", sort: " + ps.sortingOrder);
            var r = g != null ? g.ScreenRendererInfo() : "";
            sb.AppendLine(r);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "ide_dump.txt"), sb.ToString());
        }

        public void Dispose()
        {
            if (dbg != null) { dbg.Stop(); dbg = null; }
            if (host != null) UnityEngine.Object.Destroy(host);
            if (Texture != null) Texture.Release();
        }

        // ======================= вёрстка =======================
        void Build()
        {
            root.Clear();
            root.style.width = PW; root.style.height = PH; root.style.backgroundColor = K.EditorBg; root.style.flexDirection = FlexDirection.Column;
            K.Font(root, false); root.style.fontSize = 15f; root.style.color = K.Text;
            root.Add(BuildTitleBar());
            var body = K.Box(true); K.Grow(body); root.Add(body);
            body.Add(BuildActivityBar());
            body.Add(BuildSideBar());
            var center = K.Box(); K.Grow(center); center.style.minWidth = 400f; body.Add(center);
            center.Add(BuildEditorGroup());
            center.Add(BuildBottomPanel());
            rightBar = BuildRightBar(); body.Add(rightBar);
            root.Add(BuildStatusBar());
            noticeBox = BuildNotice(); root.Add(noticeBox);
            // проба ширины символа моноширинного шрифта
            charProbe = K.T(new string('M', 100), ed.FontSize, Color.clear, true); charProbe.style.position = Position.Absolute; charProbe.style.left = -9999f; charProbe.style.whiteSpace = WhiteSpace.Pre;
            charProbe.RegisterCallback<GeometryChangedEvent>(e => ed.CalibrateCharWidth(charProbe.layout.width));
            root.Add(charProbe);
        }

        VisualElement BuildTitleBar()
        {
            var bar = K.Box(true); bar.style.height = 36f; bar.style.backgroundColor = K.TitleBg; bar.style.alignItems = Align.Center; K.Line(bar, K.Border, 0f, 0f, 1f, 0f);
            var logo = new Icon("kodzilla", K.Brand, 20f); logo.style.marginLeft = 14f; logo.style.marginRight = 12f; bar.Add(logo);
            foreach (var m in new[] { "Файл", "Правка", "Выделение", "Вид", "Переход", "Выполнить", "Терминал", "Справка" })
            {
                var b = new Btn(() => Notice("Меню «" + m + "» — для этого есть кнопки и горячие клавиши: F5, F10, F11, Ctrl+Enter.", "md")); K.Pad(b, 4f, 8f, 4f, 8f); K.Radius(b, 4f);
                b.Add(K.T(m, 14f, K.Text)); bar.Add(b);
            }
            bar.Add(K.Spacer());
            var cc = K.Box(true); cc.style.width = 520f; cc.style.height = 24f; cc.style.backgroundColor = Pal.Hex("2A2A2A"); K.Radius(cc, 6f); K.Line(cc, Pal.Hex("3C3C3C"), 1f, 1f, 1f, 1f);
            cc.style.alignItems = Align.Center; cc.style.justifyContent = Justify.Center;
            cc.Add(new Icon("search", K.Muted, 14f)); commandCenter = K.T("kodzilla-soft", 13f, K.Muted); commandCenter.style.marginLeft = 8f; cc.Add(commandCenter);
            bar.Add(cc);
            bar.Add(K.Spacer());
            deadlineChip = Chip(out chipDeadline, "warning", K.Sun); bar.Add(deadlineChip);
            bar.Add(Chip(out chipMoney, "coin", K.Sun));
            bar.Add(Chip(out chipRank, "account", K.Muted));
            bar.Add(Chip(out chipDiff, "gear", K.Muted));
            var exit = new Btn(() => g.CloseIde()); K.Pad(exit, 0f, 14f, 0f, 12f); exit.style.height = 36f; exit.SetColors(Color.clear, Pal.Hex("C42B1C"));
            exit.Add(new Icon("close", K.Text, 16f)); var el = K.T("Выйти  Esc", 13f, K.Text); el.style.marginLeft = 6f; exit.Add(el);
            bar.Add(exit);
            return bar;
        }

        VisualElement Chip(out Label label, string icon, Color iconColor)
        {
            var c = K.Box(true); c.style.alignItems = Align.Center; K.Pad(c, 0f, 10f, 0f, 10f); c.style.height = 24f; K.Mar(c, 0f, 6f, 0f, 0f);
            c.style.backgroundColor = Pal.Hex("2A2A2A"); K.Radius(c, 12f);
            c.Add(new Icon(icon, iconColor, 14f)); label = K.T("", 13f, K.Text); label.style.marginLeft = 6f; c.Add(label);
            return c;
        }

        VisualElement BuildActivityBar()
        {
            var bar = K.Box(); bar.style.width = 52f; bar.style.backgroundColor = K.ActivityBg; bar.style.alignItems = Align.Center; K.Line(bar, K.Border, 0f, 1f, 0f, 0f);
            Action<Side, string, string> Add = (s, icon, tip) =>
            {
                var b = new Btn(() => SetSide(s), false); b.style.width = 52f; b.style.height = 52f; b.style.justifyContent = Justify.Center; b.Tip = tip;
                b.SetColors(Color.clear, Color.clear);
                var mark = new VisualElement(); mark.style.position = Position.Absolute; mark.style.left = 0f; mark.style.top = 8f; mark.style.bottom = 8f; mark.style.width = 2f; mark.style.backgroundColor = K.Accent;
                mark.pickingMode = PickingMode.Ignore; b.Add(mark);
                var ic = new Icon(icon, K.Dim, 26f); b.Add(ic);
                activityMarks[s] = mark; activityIcons[s] = ic; bar.Add(b);
            };
            Add(Side.Task, "task", "Задача");
            Add(Side.Files, "files", "Проводник: все задачи");
            Decor(bar, "search", "Поиск по проекту откроется, когда проект вырастет. Пока всё помещается в один файл!");
            Decor(bar, "branch", "Git подключим на звании Junior+: коммиты, ветки и первый код-ревью.");
            Add(Side.Debug, "debug", "Запуск и отладка");
            Decor(bar, "extensions", "Расширения продаются у кофемашины: резиновая уточка и второй монитор.");
            bar.Add(K.Spacer());
            Decor(bar, "account", "Вы вошли как стажёр «Кодзилла Софт».");
            Decor(bar, "gear", "Настройки: сложность и управление — в меню паузы (Esc в офисе).");
            return bar;
        }

        void Decor(VisualElement bar, string icon, string msg)
        {
            var b = new Btn(() => Notice(msg, icon), false); b.style.width = 52f; b.style.height = 52f; b.style.justifyContent = Justify.Center; b.SetColors(Color.clear, Color.clear);
            b.Add(new Icon(icon, K.Dim, 26f)); bar.Add(b);
        }

        VisualElement BuildSideBar()
        {
            var bar = K.Box(); bar.style.width = 410f; bar.style.flexShrink = 0f; bar.style.backgroundColor = K.SideBg; K.Line(bar, K.Border, 0f, 1f, 0f, 0f);
            var head = K.Box(true); head.style.height = 36f; head.style.alignItems = Align.Center; K.Pad(head, 0f, 12f, 0f, 20f);
            sideTitle = K.T("ЗАДАЧА", 12f, K.Muted); head.Add(sideTitle); head.Add(K.Spacer()); head.Add(new Icon("more", K.Muted, 16f));
            bar.Add(head);
            sideScroll = new ScrollBox(); bar.Add(sideScroll);
            return bar;
        }

        VisualElement BuildEditorGroup()
        {
            var group = K.Box(); K.Grow(group);
            // вкладки и кнопки действий
            var tabs = K.Box(true); tabs.style.height = 40f; tabs.style.backgroundColor = K.TabBg; tabs.style.alignItems = Align.Stretch; K.Line(tabs, K.Border, 0f, 0f, 1f, 0f);
            var tab = K.Box(true); tab.style.alignItems = Align.Center; K.Pad(tab, 0f, 12f, 0f, 14f); tab.style.backgroundColor = K.TabActive; K.Line(tab, K.Border, 0f, 1f, 0f, 0f);
            var top = new VisualElement(); top.style.position = Position.Absolute; top.style.left = 0f; top.style.right = 0f; top.style.top = 0f; top.style.height = 2f; top.style.backgroundColor = K.Accent; tab.Add(top);
            tab.Add(new Icon("py", Color.white, 16f)); tabName = K.T("main.py", 14f, K.TextHi); tabName.style.marginLeft = 8f; tab.Add(tabName);
            tabDot = new Icon("dot", K.Text, 14f); tabDot.style.marginLeft = 10f; tab.Add(tabDot);
            tabs.Add(tab);
            tabs.Add(K.Spacer());
            var acts = K.Box(true); acts.style.alignItems = Align.Center; K.Pad(acts, 0f, 10f, 0f, 0f);
            // панель отладки — на месте кнопок «Запустить/Отладка», как закреплённая панель в VS Code
            debugBar = K.Box(true); debugBar.style.height = 32f; debugBar.style.alignItems = Align.Center; debugBar.style.backgroundColor = Pal.Hex("252526"); K.Radius(debugBar, 5f);
            K.Line(debugBar, Pal.Hex("454545"), 1f, 1f, 1f, 1f); K.Pad(debugBar, 0f, 10f, 0f, 4f); K.Mar(debugBar, 0f, 8f, 0f, 0f); debugBar.style.display = DisplayStyle.None;
            acts.Add(debugBar);
            runBtn = Btn.Text("Запустить", RunOnce, K.Button2, K.Button2Hover, 14f, K.Text, "play", K.Green); runBtn.Tip = "Ctrl+F5"; K.Mar(runBtn, 0f, 6f, 0f, 0f); acts.Add(runBtn);
            debugBtn = Btn.Text("Отладка", () => { if (dbg == null) StartDebug(); }, K.Button2, K.Button2Hover, 14f, K.Text, "debug", K.Orange); debugBtn.Tip = "F5"; K.Mar(debugBtn, 0f, 6f, 0f, 0f); acts.Add(debugBtn);
            checkBtn = Btn.Text("Проверить", CheckTask, K.Brand, K.BrandHover, 14f, Color.white, "check", Color.white); checkBtn.Tip = "Ctrl+Enter"; K.Mar(checkBtn, 0f, 6f, 0f, 0f); acts.Add(checkBtn);
            resetBtn = new Btn(ResetCode); K.Pad(resetBtn, 0f, 10f, 0f, 10f); resetBtn.style.height = 30f; K.Radius(resetBtn, 3f); resetBtn.SetColors(Color.clear, K.Hover);
            resetBtn.Add(new Icon("reset", K.Muted, 16f)); resetLabel = K.T("Сбросить", 13f, K.Muted); resetLabel.style.marginLeft = 6f; resetBtn.Add(resetLabel); acts.Add(resetBtn);
            tabs.Add(acts);
            group.Add(tabs);
            // хлебные крошки
            var cr = K.Box(true); cr.style.height = 26f; cr.style.alignItems = Align.Center; K.Pad(cr, 0f, 0f, 0f, 18f); cr.style.backgroundColor = K.EditorBg;
            crumbs = K.T("", 13f, K.Muted); cr.Add(crumbs); group.Add(cr);
            // редактор
            var edHost = K.Box(); K.Grow(edHost); edHost.style.overflow = Overflow.Hidden;
            ed = new CodeEditor(); edHost.Add(ed);
            ed.Changed += OnCodeChanged;
            ed.BreakpointsChanged += () => { if (dbg != null) dbg.Breakpoints = new HashSet<int>(ed.Breakpoints); if (side == Side.Debug) RefreshSide(); };
            ed.CursorMoved += RefreshStatus;
            ed.ExtraHover = HoverVariable;
            // кнопки отладки
            DebugButton("continue", "Продолжить (F5)", Pal.Hex("75BEFF"), () => { if (dbg != null) dbg.Continue(); });
            DebugButton("stepOver", "Шаг с обходом (F10)", Pal.Hex("75BEFF"), () => { if (dbg != null) dbg.StepOver(); });
            DebugButton("stepInto", "Шаг с заходом (F11)", Pal.Hex("75BEFF"), () => { if (dbg != null) dbg.StepInto(); });
            DebugButton("stop", "Остановить (Shift+F5)", K.Red, StopDebug);
            debugState = K.T("", 13f, K.Muted); debugState.style.marginLeft = 8f; debugBar.Add(debugState);
            group.Add(edHost);
            return group;
        }

        void DebugButton(string icon, string tip, Color col, Action a)
        {
            var b = new Btn(a); b.style.width = 32f; b.style.height = 28f; b.style.justifyContent = Justify.Center; K.Radius(b, 4f); b.Tip = tip; b.SetColors(Color.clear, K.Hover);
            b.Add(new Icon(icon, col, 18f)); debugBar.Add(b);
        }

        VisualElement BuildBottomPanel()
        {
            var panel = K.Box(); panel.style.height = 270f; panel.style.flexShrink = 0f; panel.style.backgroundColor = K.SideBg; K.Line(panel, K.Border, 1f, 0f, 0f, 0f);
            bottomTabsRow = K.Box(true); bottomTabsRow.style.height = 36f; bottomTabsRow.style.alignItems = Align.Stretch; K.Pad(bottomTabsRow, 0f, 12f, 0f, 12f);
            foreach (var t in new[] { Bottom.Problems, Bottom.Terminal, Bottom.DebugConsole, Bottom.Tests })
            {
                var tt = t;
                var b = new Btn(() => { bottom = tt; RefreshBottom(); }); K.Pad(b, 0f, 10f, 0f, 10f); b.SetColors(Color.clear, Color.clear);
                var l = K.T(BottomName(t), 12f, K.Muted); b.Add(l);
                var mark = new VisualElement(); mark.style.position = Position.Absolute; mark.style.left = 10f; mark.style.right = 10f; mark.style.bottom = 0f; mark.style.height = 1f; mark.style.backgroundColor = K.Text;
                mark.pickingMode = PickingMode.Ignore; b.Add(mark);
                bottomTabLabels[t] = l; bottomTabMarks[t] = mark; bottomTabsRow.Add(b);
            }
            bottomTabsRow.Add(K.Spacer());
            var clear = new Btn(() => { if (bottom == Bottom.Terminal) { terminal.Length = 0; RefreshBottom(); } }); clear.Tip = "Очистить терминал"; clear.style.width = 30f; clear.style.justifyContent = Justify.Center;
            clear.Add(new Icon("close", K.Muted, 14f)); bottomTabsRow.Add(clear);
            panel.Add(bottomTabsRow);
            bottomScroll = new ScrollBox(); K.Pad(bottomScroll, 0f, 0f, 0f, 0f); panel.Add(bottomScroll);
            return panel;
        }

        static string BottomName(Bottom b)
        {
            switch (b) { case Bottom.Problems: return "ПРОБЛЕМЫ"; case Bottom.Terminal: return "ТЕРМИНАЛ"; case Bottom.DebugConsole: return "КОНСОЛЬ ОТЛАДКИ"; default: return "ТЕСТЫ"; }
        }

        VisualElement BuildRightBar()
        {
            var bar = K.Box(); bar.style.width = 380f; bar.style.flexShrink = 0f; bar.style.backgroundColor = K.SideBg; K.Line(bar, K.Border, 0f, 0f, 0f, 1f);
            var head = K.Box(true); head.style.height = 36f; head.style.alignItems = Align.Center; K.Pad(head, 0f, 12f, 0f, 16f);
            head.Add(new Icon("kodzilla", K.Brand, 16f)); rightTitle = K.T("РАЗБОР КОДА", 12f, K.Muted); rightTitle.style.marginLeft = 8f; head.Add(rightTitle);
            bar.Add(head);
            rightScroll = new ScrollBox(); bar.Add(rightScroll);
            return bar;
        }

        VisualElement BuildStatusBar()
        {
            statusBar = K.Box(true); statusBar.style.height = 28f; statusBar.style.backgroundColor = K.Status; statusBar.style.alignItems = Align.Center;
            var remote = K.Box(true); remote.style.height = 28f; remote.style.width = 38f; remote.style.justifyContent = Justify.Center; remote.style.alignItems = Align.Center; remote.style.backgroundColor = Pal.Hex("16825D");
            remote.Add(new Icon("kodzilla", Color.white, 14f)); statusBar.Add(remote);
            var br = SItem(); br.Add(new Icon("branch", Color.white, 14f)); br.Add(STxt("main")); statusBar.Add(br);
            var pr = SItem(); pr.Add(new Icon("error", Color.white, 14f)); statusErr = STxt("0"); pr.Add(statusErr);
            pr.Add(new Icon("warning", Color.white, 14f)); statusWarn = STxt("0"); pr.Add(statusWarn); statusBar.Add(pr);
            statusMode = STxt(""); statusMode.style.marginLeft = 14f; statusBar.Add(statusMode);
            statusBar.Add(K.Spacer());
            statusPos = STxt(""); var ps1 = SItem(); ps1.Add(statusPos); statusBar.Add(ps1);
            foreach (var s in new[] { "Пробелы: 4", "UTF-8", "LF" }) { var it = SItem(); it.Add(STxt(s)); statusBar.Add(it); }
            var py = SItem(); py.Add(STxt("{ } Python 3 · кодзилла")); statusBar.Add(py);
            var mo = SItem(); mo.Add(new Icon("coin", K.Sun, 14f)); statusMoney = STxt(""); mo.Add(statusMoney); statusBar.Add(mo);
            var bell = SItem(); bell.Add(new Icon("bell", Color.white, 14f)); statusBar.Add(bell);
            return statusBar;
        }
        static VisualElement SItem() { var v = K.Box(true); v.style.alignItems = Align.Center; K.Pad(v, 0f, 10f, 0f, 10f); v.style.height = 28f; return v; }
        static Label STxt(string s) { var l = K.T(s, 13f, Color.white); l.style.marginLeft = 5f; return l; }

        // Уведомления как в VS Code: стопка карточек над строкой состояния, каждая гаснет сама
        readonly List<KeyValuePair<VisualElement, float>> notices = new List<KeyValuePair<VisualElement, float>>();

        VisualElement BuildNotice()
        {
            var n = K.Box(); n.style.position = Position.Absolute; n.style.right = 400f; n.style.bottom = 44f; n.style.width = 470f;
            n.style.justifyContent = Justify.FlexEnd; n.pickingMode = PickingMode.Ignore;
            return n;
        }

        void Notice(string text, string icon = "md", Color? col = null)
        {
            if (notices.Count > 0 && notices[notices.Count - 1].Key.userData as string == text) { var last = notices[notices.Count - 1]; notices[notices.Count - 1] = new KeyValuePair<VisualElement, float>(last.Key, Time.unscaledTime + 4.5f); return; }
            var n = K.Box(true); n.userData = text;
            n.style.backgroundColor = Pal.Hex("252526"); K.Line(n, Pal.Hex("454545"), 1f, 1f, 1f, 1f); K.Radius(n, 6f); K.Pad(n, 14f, 16f, 14f, 14f); n.style.marginTop = 8f;
            n.style.alignItems = Align.FlexStart; n.pickingMode = PickingMode.Ignore;
            n.Add(new Icon(icon, col ?? K.Blue, 20f));
            var t = K.T(text, 14f, K.Text, false, false, true); t.style.marginLeft = 12f; t.style.flexShrink = 1f; t.style.flexGrow = 1f; n.Add(t);
            noticeBox.Add(n);
            notices.Add(new KeyValuePair<VisualElement, float>(n, Time.unscaledTime + 4.5f + notices.Count * 0.6f));
            while (notices.Count > 3) { notices[0].Key.RemoveFromHierarchy(); notices.RemoveAt(0); }
        }

        // Сообщения игры (награды, повышения, новая задача), пока стажёр за компьютером
        public void GameNotice(string text)
        {
            string icon = "bell"; Color col = K.Sun;
            if (text.StartsWith("Задача сдана")) { icon = "coin"; col = K.Sun; }
            else if (text.StartsWith("ПОВЫШЕНИЕ")) { icon = "kodzilla"; col = K.Brand; }
            else if (text.StartsWith("Новая задача")) { icon = "task"; col = K.Blue; }
            Notice(K.Esc(text), icon, col);
        }

        // ======================= обновление частей =======================
        void RefreshAll() { RefreshTitle(); RefreshSide(); RefreshRight(); RefreshBottom(); RefreshStatus(); RefreshEditorFlags(); }

        void SetSide(Side s) { side = s; RefreshSide(); }

        void RefreshTitle()
        {
            if (Task == null) return;
            commandCenter.text = "kodzilla-soft — " + TaskCode(Task) + " · " + K.Esc(Task.title);
            crumbs.text = "kodzilla-soft  ›  tasks  ›  " + TaskCode(Task).ToLower() + "  ›  <color=#CCCCCC>main.py</color>";
            SetText(chipMoney, g.Save.money.ToString());
            SetText(chipRank, g.RankName);
            SetText(chipDiff, Progress.DifficultyName(Diff));
            bool hard = Diff == Difficulty.Hard;
            deadlineChip.style.display = hard ? DisplayStyle.Flex : DisplayStyle.None;
            if (hard)
            {
                float left = Task.deadline - Spent;
                SetText(chipDeadline, left > 0 ? string.Format("Дедлайн {0}:{1:00}", (int)(left / 60), (int)(left % 60)) : "Дедлайн сорван");
                chipDeadline.style.color = left > 30 ? K.Sun : K.Brand;
            }
        }

        static void SetText(Label l, string s) { if (l.text != s) l.text = s; }

        void RefreshStatus()
        {
            SetText(statusPos, "Стр. " + (ed.CurL + 1) + ", стлб. " + (ed.CurC + 1));
            SetText(statusMoney, g.Save.money.ToString());
            int errs = (lint != null ? 1 : 0) + (runtimeErrorLine > 0 && lint == null ? 1 : 0);
            SetText(statusErr, errs.ToString());
            SetText(statusWarn, "0");
            string mode = dbg == null ? "" : dbg.Finished ? "Отладка завершена" : dbg.Paused ? "Отладка: пауза на строке " + dbg.Line : "Отладка: выполняется…";
            SetText(statusMode, mode);
            var col = dbg != null ? K.StatusDebug : K.Status;
            if (statusBar.style.backgroundColor.value != col) statusBar.style.backgroundColor = col;
            bool dirty = Code != savedCode;
            tabDot.style.display = dirty ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void RefreshEditorFlags()
        {
            ed.ReadOnly = dbg != null;
            if (dbg != null)
            {
                bool stopped = dbg.Paused || dbg.Finished;
                ed.DebugLine = stopped ? dbg.Line : -1; ed.DebugIsError = dbg.Finished && dbg.Error != null;
            }
            else { ed.DebugLine = -1; ed.DebugIsError = false; }
            if (lint != null) { ed.ErrorLine = lint.Line; ed.ErrorText = ErrorPlain(lint); }
            else if (runtimeErrorLine > 0) { ed.ErrorLine = runtimeErrorLine; ed.ErrorText = runtimeErrorText; }
            else { ed.ErrorLine = -1; ed.ErrorText = null; }
            debugBar.style.display = dbg != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (dbg != null) SetText(debugState, dbg.Finished ? (dbg.Error != null ? "упала с ошибкой" : "завершилась") : dbg.Paused ? "пауза · строка " + dbg.Line : "выполняется…");
            bool busy = dbg != null && !dbg.Finished;   // завершившуюся отладку кнопки закрывают сами
            runBtn.Enabled = !busy; checkBtn.Enabled = !busy; resetBtn.Enabled = !busy;
            runBtn.style.display = dbg != null ? DisplayStyle.None : DisplayStyle.Flex;
            debugBtn.style.display = dbg != null ? DisplayStyle.None : DisplayStyle.Flex;
            SetText(resetLabel, confirmReset ? "Точно сбросить?" : "Сбросить");
            resetLabel.style.color = confirmReset ? K.Red : K.Muted;
            ed.Place();
        }

        // ---------- боковая панель ----------
        void RefreshSide()
        {
            foreach (var kv in activityMarks) kv.Value.style.display = kv.Key == side ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var kv in activityIcons) kv.Value.Set(kv.Value.Kind, kv.Key == side ? K.TextHi : K.Dim);
            var c = sideScroll.Content; c.Clear();
            K.Pad(c, 0f, 18f, 24f, 20f);
            if (Task == null) return;
            switch (side)
            {
                case Side.Task: SideTask(c); sideTitle.text = "ЗАДАЧА"; break;
                case Side.Files: SideFiles(c); sideTitle.text = "ПРОВОДНИК"; break;
                case Side.Debug: SideDebug(c); sideTitle.text = "ЗАПУСК И ОТЛАДКА"; break;
            }
            sideScroll.Apply();
        }

        static Label Para(VisualElement c, string text, float size = 15f, Color? col = null, float top = 6f)
        {
            var l = K.T(text, size, col ?? K.Text, false, false, true); l.style.marginTop = top; l.style.whiteSpace = WhiteSpace.Normal; c.Add(l); return l;
        }
        static void Section(VisualElement c, string title, float top = 18f)
        {
            var l = K.T(title, 12f, K.Muted, false, true); l.style.marginTop = top; l.style.marginBottom = 2f; c.Add(l);
        }
        static Label CodeBlock(VisualElement c, string text, Color? col = null)
        {
            var box = K.Box(); box.style.backgroundColor = Pal.Hex("141414"); K.Radius(box, 4f); K.Pad(box, 10f, 12f, 10f, 12f); box.style.marginTop = 6f;
            var l = K.T(K.Esc(text), 14f, col ?? K.Text, true, false, true); l.style.whiteSpace = WhiteSpace.PreWrap; box.Add(l); c.Add(box); return l;
        }

        void SideTask(VisualElement c)
        {
            var t = Task;
            var head = K.Box(true); head.style.alignItems = Align.Center; head.style.marginTop = 4f;
            var code = K.T(TaskCode(t), 12f, Color.white, false, true); K.Pad(code, 2f, 8f, 2f, 8f); code.style.backgroundColor = K.Accent; K.Radius(code, 3f); head.Add(code);
            var ch = K.T(K.Esc(t.chapter), 13f, K.Muted); ch.style.marginLeft = 10f; ch.style.flexShrink = 1f; ch.style.overflow = Overflow.Hidden; head.Add(ch);
            c.Add(head);
            Para(c, "<b>" + K.Esc(t.title) + "</b>", 21f, K.TextHi, 10f);
            if (t.isBugHunt) { var bh = K.T("БАГ-ХАНТ", 11f, Color.white, false, true); K.Pad(bh, 2f, 8f, 2f, 8f); bh.style.backgroundColor = K.Brand; K.Radius(bh, 3f); bh.style.alignSelf = Align.FlexStart; bh.style.marginTop = 6f; c.Add(bh); }
            // отправитель
            var who = K.Box(true); who.style.alignItems = Align.Center; who.style.marginTop = 16f;
            var av = K.Box(); av.style.width = 30f; av.style.height = 30f; K.Radius(av, 15f); av.style.backgroundColor = K.Brand; av.style.justifyContent = Justify.Center; av.style.alignItems = Align.Center;
            string init = string.IsNullOrEmpty(t.sender) ? "?" : t.sender.Substring(0, 1).ToUpper();
            var il = K.T(init, 14f, Color.white, false, true); av.Add(il); who.Add(av);
            var wl = K.T("<b>" + K.Esc(t.sender) + "</b>  <color=#9D9D9D>написал(а) тебе</color>", 14f, K.Text); wl.style.marginLeft = 10f; who.Add(wl);
            c.Add(who);
            var story = Para(c, K.Esc(t.story), 15f, K.Text, 8f);
            story.style.backgroundColor = Pal.Hex("202020"); K.Pad(story, 10f, 12f, 10f, 12f); K.Radius(story, 6f); K.Line(story, Pal.Hex("2B2B2B"), 1f, 1f, 1f, 1f);
            Section(c, "ЦЕЛЬ");
            Para(c, "<b>" + K.Esc(t.goal) + "</b>", 15f, K.TextHi);
            // теория
            if (Diff == Difficulty.Hard) { Section(c, "ТЕОРИЯ"); Para(c, "Теория скрыта на тяжёлой сложности. Вспоминай или гугли — как на настоящей работе.", 14f, K.Muted); }
            else
            {
                var th = new Btn(() => { theoryOpen = !theoryOpen; RefreshSide(); }); th.style.marginTop = 16f; th.style.height = 26f; K.Radius(th, 3f);
                th.Add(new Icon(theoryOpen ? "chevD" : "chevR", K.Muted, 16f)); var tl = K.T("ТЕОРИЯ", 12f, K.Muted, false, true); tl.style.marginLeft = 4f; th.Add(tl); c.Add(th);
                if (theoryOpen) Para(c, K.Esc(t.theory), 15f, K.Text);
            }
            // подсказки
            Section(c, "ПОДСКАЗКИ");
            int shown = HintsShown;
            for (int i = 0; i < shown && i < t.hints.Length; i++)
            {
                var h = Para(c, "<color=#75BEFF>Подсказка " + (i + 1) + ".</color> " + K.Esc(t.hints[i]), 15f, K.Text, 6f);
                K.Line(h, K.Blue, 0f, 0f, 0f, 3f); K.Pad(h, 4f, 0f, 4f, 10f);
            }
            if (shown < t.hints.Length)
            {
                Btn b = null;
                if (Diff == Difficulty.Easy) b = Btn.Text("Подсказка " + (shown + 1) + " из " + t.hints.Length, () => { hintsShown[Task.id] = HintsShown + 1; RefreshSide(); }, K.Button2, K.Button2Hover, 14f, K.Text, "md", K.Blue);
                else if (Diff == Difficulty.Medium)
                {
                    b = Btn.Text("Купить подсказку — 30 монет", () => { if (g.Save.money < 30) return; g.Save.money -= 30; hintsShown[Task.id] = HintsShown + 1; g.Persist(); RefreshSide(); RefreshTitle(); }, K.Button2, K.Button2Hover, 14f, K.Text, "coin", K.Sun);
                    b.Enabled = g.Save.money >= 30;
                }
                else Para(c, "Подсказок на тяжёлой сложности нет.", 14f, K.Muted);
                if (b != null) { b.style.alignSelf = Align.FlexStart; b.style.marginTop = 8f; c.Add(b); }
            }
            else if (t.hints.Length > 0 && shown >= t.hints.Length) Para(c, "Все подсказки открыты.", 13f, K.Muted);
            // решение
            if (Diff == Difficulty.Easy && t.solution != null)
            {
                if (solutionShown) { Section(c, "ЭТАЛОННОЕ РЕШЕНИЕ"); CodeBlock(c, t.solution, Pal.Hex("B5CEA8")); Para(c, "Перепиши его руками, а не копируй: так код лучше запоминается.", 13f, K.Muted); }
                else if (Fails >= 3)
                {
                    var sb = Btn.Text("Показать решение (награда ×0.5)", () => { solutionShown = true; usedSolution.Add(Task.id); RefreshSide(); }, Pal.Hex("5A1D1D"), Pal.Hex("6E2424"), 14f, Pal.Hex("FFB4B4"), "warning", Pal.Hex("FFB4B4"));
                    sb.style.alignSelf = Align.FlexStart; sb.style.marginTop = 14f; c.Add(sb);
                }
            }
            // итог проверки
            if (lastCheck != null)
            {
                Section(c, "ПОСЛЕДНЯЯ ПРОВЕРКА");
                int passed = lastCheck.Count(x => x.Passed);
                bool ok = passed == lastCheck.Count;
                var r = Btn.Text((ok ? "Все тесты пройдены: " : "Пройдено ") + passed + " из " + lastCheck.Count + " — подробнее", () => { bottom = Bottom.Tests; RefreshBottom(); },
                    ok ? Pal.Hex("1E3B2A") : Pal.Hex("3B1E24"), ok ? Pal.Hex("25482F") : Pal.Hex("48252C"), 14f, ok ? K.Green : Pal.Hex("FF8FA3"), ok ? "check" : "error", ok ? K.Green : Pal.Hex("FF8FA3"));
                r.style.alignSelf = Align.FlexStart; r.style.marginTop = 6f; c.Add(r);
                // сдал — сразу предлагаем следующую задачу
                var nx = g.CurrentTaskPublic;
                if (ok && g.Save.done.Contains(Task.id) && nx != null && nx != Task && !g.Save.done.Contains(nx.id))
                {
                    var nb = Btn.Text("Следующая задача: " + K.Esc(nx.title), () => Open(nx), K.Button, K.ButtonHover, 15f, Color.white, "continue", Color.white);
                    nb.style.height = 36f; nb.style.marginTop = 10f; nb.style.justifyContent = Justify.Center; c.Add(nb);
                }
            }
        }

        void SideFiles(VisualElement c)
        {
            var rootRow = K.Box(true); rootRow.style.alignItems = Align.Center; rootRow.style.marginTop = 4f; rootRow.style.marginLeft = -12f;
            rootRow.Add(new Icon("chevD", K.Text, 16f)); rootRow.Add(K.T("KODZILLA-SOFT", 12f, K.Text, false, true)); c.Add(rootRow);
            string chapter = null; VisualElement folder = null;
            for (int i = 0; i < g.Tasks.tasks.Length; i++)
            {
                var t = g.Tasks.tasks[i];
                if (t.chapter != chapter)
                {
                    chapter = t.chapter; string key = chapter;
                    bool open = !collapsed.Contains(key);
                    var fr = new Btn(() => { if (!collapsed.Remove(key)) collapsed.Add(key); RefreshSide(); }); fr.style.height = 28f; K.Pad(fr, 0f, 6f, 0f, 4f); fr.style.marginTop = 2f;
                    fr.Add(new Icon(open ? "chevD" : "chevR", K.Muted, 16f)); fr.Add(new Icon("folder", Pal.Hex("DCB67A"), 16f));
                    var fl = K.T(K.Esc(chapter), 14f, K.Text); fl.style.marginLeft = 6f; fr.Add(fl); c.Add(fr);
                    folder = K.Box(); folder.style.display = open ? DisplayStyle.Flex : DisplayStyle.None; c.Add(folder);
                }
                bool done = g.Save.done.Contains(t.id), unlocked = g.IsUnlocked(i), cur = t == Task;
                var tt = t;
                var row = new Btn(() => { if (unlocked && tt != Task) Open(tt); else if (!unlocked) Notice("Эта задача откроется, когда сдашь предыдущие.", "lock", K.Muted); });
                row.style.height = 28f; K.Pad(row, 0f, 8f, 0f, 30f);
                row.SetColors(cur ? K.Press : Color.clear, cur ? K.Press : K.Hover);
                row.Add(new Icon(unlocked ? "py" : "lock", unlocked ? Color.white : K.Dim, 16f));
                var nl = K.T(TaskCode(t).ToLower().Replace("-", "_") + ".py", 14f, unlocked ? (cur ? K.TextHi : K.Text) : K.Dim, true); nl.style.marginLeft = 8f; row.Add(nl);
                var ttl = K.T(K.Esc(t.title), 13f, K.Muted); ttl.style.marginLeft = 10f; ttl.style.flexShrink = 1f; ttl.style.overflow = Overflow.Hidden; row.Add(ttl);
                row.Add(K.Spacer());
                if (done) row.Add(new Icon("check", K.Green, 16f));
                else if (t.isBugHunt) row.Add(K.T("баг", 11f, K.Brand));
                folder.Add(row);
            }
            Para(c, "Сдано " + g.Save.done.Count + " из " + g.Tasks.tasks.Length + " · " + g.RankName, 13f, K.Muted, 16f);
        }

        void SideDebug(VisualElement c)
        {
            if (dbg == null)
            {
                var b = Btn.Text("Запустить отладку  F5", StartDebug, K.Button, K.ButtonHover, 15f, Color.white, "debug", Color.white);
                b.style.height = 34f; b.style.marginTop = 8f; b.style.justifyContent = Justify.Center; c.Add(b);
                Para(c, "Отладчик выполняет программу по одной строке и показывает, как меняются переменные.", 14f, K.Muted, 12f);
                Para(c, "• Кликни слева от номера строки — появится красная точка остановки: программа остановится перед этой строкой.\n• F10 — шаг, F11 — шаг с заходом в функцию, F5 — продолжить до следующей точки.", 14f, K.Muted, 10f);
                DebugBreakpoints(c);
                return;
            }
            Section(c, "ПЕРЕМЕННЫЕ", 6f);
            if (!dbg.Paused && !dbg.Finished) { Para(c, "Программа выполняется до следующей точки остановки…", 14f, K.Muted); }
            else
            {
                var vars = dbg.Vars.Where(v => v.Type != "function").ToList();
                if (vars.Count == 0) Para(c, "Пока ни одной переменной.", 14f, K.Muted);
                string scope = null;
                foreach (var v in vars)
                {
                    if (v.Scope != scope) { scope = v.Scope; var sl = K.T(K.Esc(scope), 13f, K.Muted); sl.style.marginTop = 8f; c.Add(sl); }
                    var row = K.Box(true); row.style.marginTop = 3f; row.style.flexWrap = Wrap.Wrap; K.Pad(row, 0f, 0f, 0f, 14f);
                    row.Add(K.T("<color=#C586C0>" + K.Esc(v.Name) + "</color><color=#CCCCCC>: </color>", 15f, K.Text, true));
                    var val = K.T(K.Esc(v.Value), 15f, ValueColor(v.Type), true, false, true); val.style.whiteSpace = WhiteSpace.Normal; val.style.flexShrink = 1f; row.Add(val);
                    var ty = K.T(K.Esc(v.Type), 12f, K.Dim); ty.style.marginLeft = 8f; row.Add(ty);
                    c.Add(row);
                }
            }
            var ch = dbg.Changes;
            if (ch.Count > 0)
            {
                Section(c, "ЧТО ИЗМЕНИЛОСЬ НА ПРОШЛОМ ШАГЕ");
                foreach (var x in ch) Para(c, "<color=#89D185>" + K.Esc(x) + "</color>", 14f, K.Text, 3f);
            }
            Section(c, "СТЕК ВЫЗОВОВ");
            var fr = K.Box(true); fr.style.alignItems = Align.Center; fr.style.marginTop = 4f;
            fr.Add(new Icon("play", Pal.Hex("FFCC00"), 12f));
            fr.Add(K.T("  " + K.Esc(dbg.FrameName) + "   <color=#9D9D9D>main.py : " + dbg.Line + "</color>", 14f, K.Text));
            c.Add(fr);
            DebugBreakpoints(c);
            Para(c, "Шагов выполнено: " + dbg.StepCount, 13f, K.Muted, 14f);
        }

        void DebugBreakpoints(VisualElement c)
        {
            Section(c, "ТОЧКИ ОСТАНОВА");
            if (ed.Breakpoints.Count == 0) { Para(c, "Нет. Кликни слева от номера строки.", 14f, K.Muted, 4f); return; }
            foreach (var b in ed.Breakpoints.OrderBy(x => x))
            {
                int line = b;
                var row = new Btn(() => ed.GoToLine(line)); row.style.height = 26f; K.Pad(row, 0f, 6f, 0f, 6f);
                row.Add(new Icon("dot", K.Breakpoint, 14f));
                string text = line <= ed.L.Count ? ed.L[line - 1].Trim() : "";
                var l = K.T("main.py  <color=#9D9D9D>" + line + "</color>   " + K.Esc(text.Length > 32 ? text.Substring(0, 32) + "…" : text), 14f, K.Text, false); l.style.marginLeft = 6f; row.Add(l);
                c.Add(row);
            }
        }

        static Color ValueColor(string type)
        {
            switch (type) { case "str": return Pal.Hex("CE9178"); case "int": case "float": return Pal.Hex("B5CEA8"); case "bool": case "NoneType": return Pal.Hex("569CD6"); default: return K.Text; }
        }

        string HoverVariable(string w)
        {
            if (dbg == null || !(dbg.Paused || dbg.Finished)) return null;
            var v = dbg.Vars.FirstOrDefault(x => x.Name == w);
            return v == null ? null : "<color=#C586C0>" + K.Esc(v.Name) + "</color> = <color=" + K.Hex(ValueColor(v.Type)) + ">" + K.Esc(v.Value) + "</color>   <color=#6E7681>" + K.Esc(v.Type) + "</color>";
        }

        // ---------- правая панель: разбор кода ----------
        void RefreshRight()
        {
            var c = rightScroll.Content; c.Clear(); K.Pad(c, 0f, 16f, 24f, 16f);
            if (Task == null) return;
            if (dbg != null)
            {
                rightTitle.text = "РАЗБОР · ОТЛАДКА";
                if (dbg.Paused)
                {
                    Card(c, "Сейчас выполнится строка " + dbg.Line + (dbg.FrameName != "<модуль>" ? " (внутри функции " + K.Esc(dbg.FrameName) + ")" : ""), K.Sun);
                    if (Diff != Difficulty.Hard || g.Save.hasMonitor)
                    {
                        string why = ExplainLine(dbg.Line);
                        if (why != null) Para(c, K.Esc(why), 15f, K.Text, 8f);
                    }
                }
                else if (dbg.Finished) Card(c, dbg.Error != null ? "Программа упала:\n" + ErrorRich(dbg.Error) : "Программа завершилась. Результат — в консоли отладки.", dbg.Error != null ? K.Red : K.Green);
                else Para(c, "Программа выполняется…", 14f, K.Muted);
                rightScroll.Apply(); return;
            }
            rightTitle.text = "РАЗБОР КОДА";
            if (lint != null) Card(c, "<b>Ошибка в коде</b>\n" + ErrorRich(lint), K.Red);
            if (!ExplainPanel)
            {
                Para(c, "На этой сложности построчный разбор доступен только в отладчике. Купи второй монитор у кофемашины, чтобы видеть его всегда.", 14f, K.Muted, 10f);
                rightScroll.Apply(); return;
            }
            if (explains.Count == 0 && lint == null) Para(c, "Напиши код — и здесь появится объяснение каждой строки.", 14f, K.Muted, 6f);
            foreach (var kv in explains)
            {
                int line = kv.Key;
                var card = new Btn(() => ed.GoToLine(line), false); card.style.alignItems = Align.Stretch; K.Pad(card, 8f, 10f, 8f, 10f); card.style.marginTop = 6f; K.Radius(card, 4f);
                card.SetColors(line == ed.CurL + 1 ? Pal.Hex("24292E") : Color.clear, K.Hover);
                card.Add(K.T("СТРОКА " + line, 11f, Pal.Hex("DCDCAA"), false, true));
                var t = K.T(K.Esc(kv.Value), 15f, K.Text, false, false, true); t.style.marginTop = 3f; t.style.whiteSpace = WhiteSpace.Normal; card.Add(t);
                c.Add(card);
            }
            rightScroll.Apply();
        }

        static void Card(VisualElement c, string text, Color accent)
        {
            var l = K.T(text, 15f, K.Text, false, false, true); l.style.whiteSpace = WhiteSpace.Normal; l.style.marginTop = 6f;
            l.style.backgroundColor = Pal.Hex("202020"); K.Line(l, accent, 0f, 0f, 0f, 3f); K.Pad(l, 10f, 12f, 10f, 12f); K.Radius(l, 4f); c.Add(l);
        }

        // ---------- нижняя панель ----------
        void RefreshBottom()
        {
            foreach (var kv in bottomTabLabels)
            {
                bool on = kv.Key == bottom;
                kv.Value.style.color = on ? K.TextHi : K.Muted;
                bottomTabMarks[kv.Key].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }
            int problems = (lint != null ? 1 : 0) + (lint == null && runtimeErrorLine > 0 ? 1 : 0);
            bottomTabLabels[Bottom.Problems].text = "ПРОБЛЕМЫ" + (problems > 0 ? "  <color=#FFFFFF>" + problems + "</color>" : "");
            if (lastCheck != null) bottomTabLabels[Bottom.Tests].text = "ТЕСТЫ  " + (lastCheck.All(x => x.Passed) ? "<color=#89D185>" : "<color=#F14C4C>") + lastCheck.Count(x => x.Passed) + "/" + lastCheck.Count + "</color>";
            else bottomTabLabels[Bottom.Tests].text = "ТЕСТЫ";
            var c = bottomScroll.Content; c.Clear(); K.Pad(c, 6f, 16f, 12f, 20f);
            bottomScroll.StickToBottom = bottom == Bottom.Terminal || bottom == Bottom.DebugConsole;
            switch (bottom)
            {
                case Bottom.Terminal:
                    {
                        string txt = terminal.ToString() + Prompt() + "<color=#CCCCCC>█</color>";
                        var l = K.T(txt, 15f, K.Text, true, false, true); l.style.whiteSpace = WhiteSpace.PreWrap; c.Add(l);
                        bottomScroll.ToBottom();
                        break;
                    }
                case Bottom.DebugConsole:
                    {
                        string txt = dbg == null ? "<color=#9D9D9D>Здесь появится вывод программы во время отладки (F5).</color>" : K.Esc(dbg.Console);
                        if (dbg != null && dbg.Finished) txt += dbg.Error != null ? "\n" + ErrorRich(dbg.Error) : "\n<color=#89D185>Программа завершилась.</color>";
                        var l = K.T(txt, 15f, K.Text, true, false, true); l.style.whiteSpace = WhiteSpace.PreWrap; c.Add(l);
                        bottomScroll.ToBottom();
                        break;
                    }
                case Bottom.Problems:
                    {
                        if (problems == 0) { Para(c, "В рабочей области проблем не обнаружено.", 14f, K.Muted, 2f); break; }
                        PyError e = lint; int line = lint != null ? lint.Line : runtimeErrorLine;
                        string text = lint != null ? ErrorPlain(lint) : runtimeErrorText;
                        var row = new Btn(() => ed.GoToLine(line)); row.style.alignItems = Align.FlexStart; K.Pad(row, 4f, 8f, 4f, 4f); K.Radius(row, 3f);
                        row.Add(new Icon("error", K.Red, 16f));
                        var tl = K.T(K.Esc(text) + "  <color=#9D9D9D>main.py [стр. " + line + "]</color>", 15f, K.Text, false, false, true); tl.style.marginLeft = 8f; tl.style.whiteSpace = WhiteSpace.Normal; tl.style.flexShrink = 1f; row.Add(tl);
                        c.Add(row);
                        break;
                    }
                case Bottom.Tests:
                    {
                        if (lastCheck == null) { Para(c, "Нажми «Проверить» (Ctrl+Enter) — здесь появятся результаты тестов.", 14f, K.Muted, 2f); break; }
                        bool shownFail = false;
                        for (int i = 0; i < lastCheck.Count; i++)
                        {
                            var r = lastCheck[i];
                            var row = K.Box(true); row.style.alignItems = Align.Center; row.style.marginTop = 4f;
                            row.Add(new Icon(r.Passed ? "check" : "error", r.Passed ? K.Green : K.Red, 16f));
                            var tl = K.T("Тест " + (i + 1) + "   <color=#9D9D9D>ввод: " + K.Esc(string.IsNullOrEmpty(r.InputsText) ? "—" : r.InputsText) + "</color>", 15f, K.Text); tl.style.marginLeft = 8f; row.Add(tl);
                            c.Add(row);
                            if (!r.Passed && !shownFail)
                            {
                                shownFail = true;
                                if (Diff != Difficulty.Hard)
                                {
                                    var grid = K.Box(true); grid.style.marginLeft = 24f; grid.style.marginTop = 4f;
                                    var a = K.Box(); K.Grow(a); a.Add(K.T("ожидалось", 12f, K.Muted)); CodeBlock(a, r.Expected, Pal.Hex("89D185"));
                                    var b = K.Box(); K.Grow(b); b.style.marginLeft = 12f; b.Add(K.T("получилось", 12f, K.Muted)); CodeBlock(b, r.Actual.Length == 0 ? "(пусто)" : r.Actual, Pal.Hex("FF8FA3"));
                                    grid.Add(a); grid.Add(b); c.Add(grid);
                                }
                                if (!string.IsNullOrEmpty(r.Note)) { var n = Para(c, K.Esc(r.Note), 14f, K.Muted, 4f); n.style.marginLeft = 24f; }
                                if (r.Error != null) { var n = Para(c, ErrorRich(r.Error), 14f, K.Text, 4f); n.style.marginLeft = 24f; }
                            }
                        }
                        break;
                    }
            }
            bottomScroll.Apply();
        }

        static string Prompt() { return "<color=#89D185>PS</color> <color=#CCCCCC>C:\\kodzilla-soft></color> "; }

        // ======================= ошибки =======================
        string ErrorRich(PyError e)
        {
            string head = "<color=#F14C4C><b>" + K.Esc(e.PyType) + "</b></color> <color=#9D9D9D>в строке " + e.Line + "</color>";
            if (RuErrors) return head + "\n" + K.Esc(e.Ru);
            return head + "\n<color=#9D9D9D>(Купи резиновую уточку у кофемашины — она объясняет ошибки на русском.)</color>";
        }
        string ErrorPlain(PyError e) { return e.PyType + ": " + (RuErrors ? e.Ru : "ошибка в строке " + e.Line); }

        // ======================= жизненный цикл =======================
        public void Open(TaskData t)
        {
            if (Task != null) SaveCode();
            StopDebugSilently();
            Task = t;
            ed.Text = g.Save.GetCode(t.id) ?? t.starter;
            savedCode = ed.Text;
            ed.Breakpoints.Clear();
            lastCheck = null; runtimeErrorLine = -1; runtimeErrorText = null; explainedCode = null; lint = null; explains.Clear();
            solutionShown = usedSolution.Contains(t.id); confirmReset = false;
            terminal.Length = 0;
            terminal.Append("<color=#9D9D9D>Кодзилла Софт · терминал. Запуск программы: кнопка «Запустить» или Ctrl+F5.</color>\n");
            side = Side.Task; bottom = Bottom.Terminal;
            sideScroll.ToTop(); rightScroll.ToTop();
            RefreshExplanations(true);
            RefreshAll();
        }

        public void Close() { SaveCode(); StopDebugSilently(); SetActive(false); }

        public int PanelWidth { get { return PW; } }
        public int PanelHeight { get { return PH; } }

        // Новая игра: забываем подсказки, провалы и время по задачам
        public void ResetProgress(TaskData t)
        {
            StopDebugSilently();
            hintsShown.Clear(); failedChecks.Clear(); timeSpent.Clear(); usedSolution.Clear();
            Task = null;
            if (t != null) Open(t);
        }

        void SaveCode()
        {
            if (Task == null) return;
            g.Save.SetCode(Task.id, Code); g.Persist(); savedCode = Code;
        }

        public void SetActive(bool a)
        {
            Active = a; ed.Active = a;
            if (!a) { ed.HidePopup(); ed.HideTooltip(); if (hoverBtn != null) { hoverBtn.SetHover(false); hoverBtn = null; } }
            ed.Place(); RefreshStatus();
        }

        int escClosedAt = -10;
        // Esc уже обработан IDE в этом или прошлом кадре (закрыл подсказку или саму IDE) — игре его не трогать
        public bool WantsEsc { get { return ed.PopupOpen || Time.frameCount - escClosedAt <= 1; } }

        public void Tick(float dt)
        {
            if (Task == null) return;
            float t; timeSpent.TryGetValue(Task.id, out t); timeSpent[Task.id] = t + dt;
        }

        // Каждый кадр (и когда стажёр не за компьютером — экран виден в офисе)
        public void Update(float dt)
        {
            if (Task == null) return;
            ed.Tick(dt);
            for (int i = notices.Count - 1; i >= 0; i--)
                if (Time.unscaledTime > notices[i].Value) { notices[i].Key.RemoveFromHierarchy(); notices.RemoveAt(i); }
            if (confirmReset && Time.unscaledTime > confirmResetUntil) { confirmReset = false; RefreshEditorFlags(); }
            if (Diff == Difficulty.Hard || Time.frameCount % 30 == 0) RefreshTitle();
            // разбор кода и проверка синтаксиса — когда пользователь перестал печатать
            if (dbg == null && explainedCode != Code && ed.SinceEdit > 0.4f) { RefreshExplanations(true); RefreshRight(); RefreshBottom(); RefreshStatus(); RefreshEditorFlags(); }
            // отладчик работает в своём потоке: подхватываем его состояние
            if (dbg != null)
            {
                string sig = dbg.Paused + "|" + dbg.Finished + "|" + dbg.Line + "|" + dbg.StepCount + "|" + dbg.Console.Length;
                if (sig != dbgSig)
                {
                    dbgSig = sig;
                    RefreshEditorFlags();
                    if (dbg.Paused || dbg.Finished) { ed.CenterOnIfHidden(dbg.Line - 1); }
                    RefreshSide(); RefreshRight(); RefreshBottom(); RefreshStatus();
                }
            }
            if (Active) PollHover();
        }

        void OnCodeChanged()
        {
            runtimeErrorLine = -1; runtimeErrorText = null; confirmReset = false;
            RefreshStatus();
        }

        void RefreshExplanations(bool force)
        {
            string code = Code;
            if (explainedCode == code) return;
            explains = Explainer.ExplainProgram(code, out lint);
            explainedCode = code;
        }

        string ExplainLine(int line)
        {
            RefreshExplanations(true);
            foreach (var kv in explains) if (kv.Key == line) return kv.Value;
            return null;
        }

        // ======================= действия =======================
        string[] SampleInputs { get { return Task.tests != null && Task.tests.Length > 0 ? Task.tests[0].inputs : new string[0]; } }

        void RunOnce()
        {
            if (dbg != null && dbg.Finished) StopDebug();
            if (Task == null || dbg != null) return;
            SaveCode();
            var inputs = SampleInputs;
            var r = PyRun.Run(Code, inputs);
            var sb = terminal;
            sb.Append(Prompt()).Append("python main.py\n");
            if (inputs.Length > 0) sb.Append("<color=#9D9D9D># ввод из первого теста: " + K.Esc(string.Join(" | ", inputs)) + "</color>\n");
            if (r.Console.Length > 0) sb.Append(K.Esc(r.Console.TrimEnd('\n'))).Append('\n');
            if (r.Error != null)
            {
                sb.Append(ErrorRich(r.Error)).Append('\n');
                runtimeErrorLine = r.Error.Line; runtimeErrorText = ErrorPlain(r.Error);
                Notice("Программа упала: " + r.Error.PyType + " в строке " + r.Error.Line + ". Строка подчёркнута в редакторе.", "error", K.Red);
            }
            else { sb.Append("<color=#89D185>Готово. Шагов: " + r.Steps + ".</color> <color=#9D9D9D>Чтобы сдать задачу — «Проверить» (Ctrl+Enter).</color>\n"); runtimeErrorLine = -1; runtimeErrorText = null; }
            TrimTerminal();
            bottom = Bottom.Terminal;
            RefreshBottom(); RefreshEditorFlags(); RefreshStatus();
        }

        void TrimTerminal() { if (terminal.Length > 12000) terminal.Remove(0, terminal.Length - 9000); }

        void StartDebug()
        {
            if (Task == null || dbg != null) return;
            SaveCode();
            try { Parser.ParseProgram(Code); }
            catch (PyError e)
            {
                runtimeErrorLine = e.Line; runtimeErrorText = ErrorPlain(e);
                terminal.Append(Prompt()).Append("python -m debug main.py\n").Append(ErrorRich(e)).Append('\n');
                bottom = Bottom.Problems; RefreshBottom(); RefreshEditorFlags(); RefreshStatus();
                Notice("Отладка не запустилась: в коде синтаксическая ошибка (строка " + e.Line + ").", "error", K.Red);
                return;
            }
            dbg = new DebugSession(Code, SampleInputs, ed.Breakpoints);
            dbg.Start(true);
            dbgSig = ""; explainedCode = null;
            side = Side.Debug; bottom = Bottom.DebugConsole;
            ed.HidePopup();
            RefreshAll();
        }

        void StopDebug()
        {
            StopDebugSilently();
            side = Side.Task; bottom = Bottom.Terminal;
            RefreshAll();
        }
        void StopDebugSilently() { if (dbg != null) { dbg.Stop(); dbg = null; } }

        void CheckTask()
        {
            if (dbg != null && dbg.Finished) StopDebug();
            if (Task == null || dbg != null) return;
            SaveCode();
            lastCheck = PyRun.Check(Code, Task.tests);
            int passed = lastCheck.Count(c => c.Passed);
            var firstErr = lastCheck.FirstOrDefault(c => c.Error != null);
            runtimeErrorLine = firstErr != null ? firstErr.Error.Line : -1;
            runtimeErrorText = firstErr != null ? ErrorPlain(firstErr.Error) : null;
            terminal.Append(Prompt()).Append("pytest tests/\n");
            if (passed == lastCheck.Count)
            {
                terminal.Append("<color=#89D185><b>Все тесты пройдены: " + passed + " из " + passed + "!</b></color>\n");
                Notice("Все тесты пройдены: " + passed + " из " + passed + "!", "check", K.Green);
                g.CompleteTask(Task, usedSolution.Contains(Task.id), Late);
            }
            else
            {
                failedChecks[Task.id] = Fails + 1;
                terminal.Append("<color=#F14C4C><b>Пройдено " + passed + " из " + lastCheck.Count + ".</b></color> <color=#9D9D9D>Подробности — на вкладке «Тесты».</color>\n");
                if (firstErr != null) terminal.Append(ErrorRich(firstErr.Error)).Append('\n');
                terminal.Append("<color=#9D9D9D>Из-за ошибки в офисе завёлся баг. Выйди и поймай его!</color>\n");
                g.SpawnBug();
                string extra = Diff == Difficulty.Easy && Fails == 3 ? " Слева появилась кнопка «Показать решение»." : "";
                Notice("Пройдено " + passed + " из " + lastCheck.Count + " тестов. В офисе завёлся баг!" + extra, "error", K.Red);
            }
            TrimTerminal();
            bottom = Bottom.Tests;
            RefreshAll();
        }

        void ResetCode()
        {
            if (dbg != null && dbg.Finished) StopDebug();
            if (Task == null || dbg != null) return;
            if (confirmReset) { ed.Text = Task.starter; confirmReset = false; SaveCode(); runtimeErrorLine = -1; Notice("Код сброшен к заготовке задачи.", "reset", K.Muted); }
            else { confirmReset = true; confirmResetUntil = Time.unscaledTime + 3f; }
            RefreshEditorFlags(); RefreshStatus();
        }

        // ======================= ввод =======================
        VisualElement PickAt(Vector2 p)
        {
            if (root.panel == null || float.IsNaN(p.x)) return null;
            return root.panel.Pick(p);
        }
        static T Up<T>(VisualElement v) where T : VisualElement
        {
            for (; v != null; v = v.parent) { var t = v as T; if (t != null) return t; }
            return null;
        }
        bool InEditor(VisualElement v) { for (; v != null; v = v.parent) if (v == ed) return true; return false; }

        // Событие OnGUI (mousePosition — экранные координаты с началом сверху слева, GUI.matrix = identity)
        public void HandleEvent(Event e)
        {
            if (!Active || Task == null) return;
            switch (e.type)
            {
                case EventType.MouseDown:
                    {
                        var pp = screenToPanel(e.mousePosition); if (!pp.HasValue) return;
                        var p = pp.Value; var v = PickAt(p);
                        if (e.button != 0) { e.Use(); return; }
                        var b = Up<Btn>(v);
                        if (b != null && !InEditor(v)) { pressedBtn = b; e.Use(); return; }
                        if (InEditor(v)) { pressedEditor = true; ed.MouseDown(p, e.clickCount, e.shift); RefreshStatus(); if (!ed.PopupOpen) { } e.Use(); return; }
                        pressedScroll = Up<ScrollBox>(v);
                        e.Use(); return;
                    }
                case EventType.MouseDrag:
                    {
                        var pp = screenToPanel(e.mousePosition); if (!pp.HasValue) return;
                        if (pressedEditor) { ed.MouseDrag(pp.Value); RefreshStatus(); }
                        e.Use(); return;
                    }
                case EventType.MouseUp:
                    {
                        var pp = screenToPanel(e.mousePosition);
                        if (pressedBtn != null && pp.HasValue && Up<Btn>(PickAt(pp.Value)) == pressedBtn && pressedBtn.Enabled && pressedBtn.Click != null) pressedBtn.Click();
                        if (pressedEditor) ed.MouseUp();
                        pressedBtn = null; pressedEditor = false; pressedScroll = null;
                        e.Use(); return;
                    }
                case EventType.ScrollWheel:
                    {
                        var pp = screenToPanel(e.mousePosition); if (!pp.HasValue) return;
                        var v = PickAt(pp.Value);
                        float d = e.delta.y;
                        if (InEditor(v)) ed.Wheel(d);
                        else { var sb = Up<ScrollBox>(v); if (sb != null) sb.Wheel(d * 18f); }
                        e.Use(); return;
                    }
                case EventType.KeyDown:
                    if (HotKey(e)) { e.Use(); return; }
                    // программа в отладчике уже завершилась — начал печатать, значит, отладка больше не нужна
                    if (dbg != null && dbg.Finished && !(e.control || e.command) &&
                        ((e.character != 0 && !char.IsControl(e.character)) || e.keyCode == KeyCode.Backspace || e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Return))
                        StopDebug();
                    if (ed.KeyDown(e)) { RefreshStatus(); if (dbg == null && explainedCode != Code) { } e.Use(); }
                    return;
            }
        }

        bool HotKey(Event e)
        {
            bool ctrl = e.control || e.command;
            switch (e.keyCode)
            {
                case KeyCode.F5:
                    if (e.shift) { if (dbg != null) StopDebug(); return true; }
                    if (ctrl) { RunOnce(); return true; }
                    if (dbg == null) StartDebug(); else if (dbg.Paused) dbg.Continue(); else if (dbg.Finished) StopDebug();
                    return true;
                case KeyCode.F9:
                    { int l = ed.CurL + 1; if (!ed.Breakpoints.Remove(l)) ed.Breakpoints.Add(l); if (dbg != null) dbg.Breakpoints = new HashSet<int>(ed.Breakpoints); ed.Place(); if (side == Side.Debug) RefreshSide(); return true; }
                case KeyCode.F10: if (dbg != null && dbg.Paused) dbg.StepOver(); return true;
                case KeyCode.F11: if (dbg != null && dbg.Paused) dbg.StepInto(); return true;
                case KeyCode.Escape:
                    escClosedAt = Time.frameCount;
                    if (ed.PopupOpen) { ed.HidePopup(); return true; }
                    g.CloseIde(); return true;
            }
            if (ctrl)
            {
                switch (e.keyCode)
                {
                    case KeyCode.S: SaveCode(); RefreshStatus(); Notice("Сохранено: main.py", "check", K.Green); return true;
                    case KeyCode.Return: case KeyCode.KeypadEnter: CheckTask(); return true;
                    case KeyCode.B: rightBar.style.display = rightBar.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None; return true;
                }
            }
            return false;
        }

        // Наведение: подсветка кнопок, подсказки к ним и к коду
        float btnHoverSince;
        void PollHover()
        {
            var ms = InputX.MousePosition();
            var pp = screenToPanel(new Vector2(ms.x, Screen.height - ms.y));
            VisualElement v = pp.HasValue ? PickAt(pp.Value) : null;
            var b = v != null && !InEditor(v) ? Up<Btn>(v) : null;
            if (b != hoverBtn)
            {
                if (hoverBtn != null) hoverBtn.SetHover(false);
                hoverBtn = b; btnHoverSince = Time.unscaledTime;
                if (hoverBtn != null) hoverBtn.SetHover(true);
            }
            ed.Hover(pp ?? Vector2.zero, pp.HasValue && InEditor(v));
        }
    }
}
