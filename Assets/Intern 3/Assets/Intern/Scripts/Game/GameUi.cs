// Интерфейс игры на UI Toolkit поверх экрана: главное меню (фон — бегущий вниз код), пауза, настройки, HUD и тосты.
// Ввод — родной для UI Toolkit (мышь), Esc обрабатывает GameRoot и вызывает Back().
// Диалоги, гардероб и IDE живут отдельно (IMGUI и своя панель в текстуре монитора).
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public class GameUi
    {
        // ---------- палитра игры ----------
        public static readonly Color Deep = Pal.Hex("0D0F2B"), Card = Pal.Hex("1B1F4A"), CardHi = Pal.Hex("252A62"), Line = Pal.Hex("363C88"),
            Well = Pal.Hex("121538"), Sun = Pal.Sun, SunHover = Pal.Hex("FFDF6E"), SunLip = Pal.Hex("C2940E"),
            Indigo = Pal.Hex("3A40A0"), IndigoHover = Pal.Hex("474EB8"), IndigoLip = Pal.Hex("252A6E"),
            Ghost = Pal.Hex("242861"), GhostHover = Pal.Hex("2E3378"), GhostLip = Pal.Hex("15183F"),
            Ink = Pal.Ink, Text = Pal.Text, Muted = Pal.Muted, Pink = Pal.Pink, Mint = Pal.Mint, Sky = Pal.Sky;

        readonly GameRoot g;
        readonly PanelSettings ps;
        readonly GameObject host;
        readonly VisualElement root;

        VisualElement codeBg, dim, hud, menu, menuCard, menuMain, menuNew, pause, pauseCard, settings, settingsPanel;
        VisualElement hudCard, clockChip, strikeRow, satedChip, lunchHud, lunchCoinsRow, summary, summaryCard, fired, firedCard;
        Label clockLabel, strikeLabel, lunchTimer, lunchCoins, lunchSeries, lunchPenalty, lunchKills, lunchHint;
        VisualElement toastRow, toastPill, promptRow, promptKeys, crosshair, cursorHint, keysPanel, bugRow, progressFill;
        Label toastText, promptText, rankLabel, moneyLabel, taskCode, taskTitle, progressLabel, bugLabel, fpsLabel, pauseTask, pauseDiffDesc;
        UiBtn pauseCamera;
        readonly List<VisualElement> pauseDiffPills = new List<VisualElement>();
        readonly List<VisualElement> pauseProfPills = new List<VisualElement>();
        Label pauseProfDesc;
        string newProf = "backend";
        ScrollBox settingsScroll;
        readonly List<VisualElement> tabPills = new List<VisualElement>();

        bool settingsOpen, newGamePage;
        int tab, newDiff;
        readonly Dictionary<VisualElement, float> shownAt = new Dictionary<VisualElement, float>();
        readonly Dictionary<VisualElement, bool> visible = new Dictionary<VisualElement, bool>();

        class Column { public VisualElement holder; public Label a; public float speed, y; }
        readonly List<Column> columns = new List<Column>();

        string lastHud, lastLunch;
        int fpsFrames; float fpsTime;

        public static GameUi TryCreate(GameRoot g)
        {
            try { return new GameUi(g); }
            catch (Exception e) { Debug.LogError("[Стажёр] Новый интерфейс не создался, работает старый: " + e); return null; }
        }

        GameUi(GameRoot g)
        {
            this.g = g;
            ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "GameUiPanel";
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 1f;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.sortingOrder = 10;
            ps.clearColor = false;
            ps.themeStyleSheet = ScriptableObject.CreateInstance<ThemeStyleSheet>();
            var ts = ScriptableObject.CreateInstance<PanelTextSettings>();
            if (K.Sans != null) ts.defaultFontAsset = K.Sans;
            ps.textSettings = ts;
            host = new GameObject("GameUi"); host.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(host);
            var doc = host.AddComponent<UIDocument>(); doc.panelSettings = ps;
            host.SetActive(true);
            root = doc.rootVisualElement;
            K.Fill(root); root.pickingMode = PickingMode.Ignore;
            K.Font(root, false); root.style.color = Text;
            Build();
            GameConfig.Changed += OnSettingsChanged;
            OnSettingsChanged();
        }

        public void Dispose()
        {
            GameConfig.Changed -= OnSettingsChanged;
            if (host != null) UnityEngine.Object.Destroy(host);
        }

        // ======================= сборка =======================
        void Build()
        {
            codeBg = BuildCodeBackground(); root.Add(codeBg);
            dim = Layer(); dim.style.backgroundColor = new Color(0.05f, 0.06f, 0.17f, 0.74f); root.Add(dim);
            hud = BuildHud(); root.Add(hud);
            lunchHud = BuildLunchHud(); root.Add(lunchHud);
            summary = Centered(); root.Add(summary);
            fired = Centered(); root.Add(fired);
            menu = BuildMenu(); root.Add(menu);
            pause = BuildPause(); root.Add(pause);
            settings = BuildSettings(); root.Add(settings);
            toastRow = BuildToast(); root.Add(toastRow);
            fpsLabel = K.B("", 13f, Mint); fpsLabel.style.position = Position.Absolute; fpsLabel.style.top = 6f; fpsLabel.style.left = 0f; fpsLabel.style.right = 0f;
            fpsLabel.style.unityTextAlign = TextAnchor.MiddleCenter; root.Add(fpsLabel);
            foreach (var v in new[] { codeBg, dim, hud, menu, pause, settings, lunchHud, summary, fired }) { v.style.display = DisplayStyle.None; visible[v] = false; }
        }

        static VisualElement Layer() { var v = K.Box(); K.Fill(v); v.pickingMode = PickingMode.Ignore; return v; }

        static VisualElement Centered()
        {
            var v = Layer(); v.style.alignItems = Align.Center; v.style.justifyContent = Justify.Center; return v;
        }

        static void Border(VisualElement v, Color c, float w) { K.Line(v, c, w, w, w, w); }

        // Карточка с мягкой «тенью» (тень — отдельный тёмный прямоугольник позади)
        static VisualElement CardBox(float width, out VisualElement wrap)
        {
            wrap = K.Box(); wrap.pickingMode = PickingMode.Ignore; wrap.style.width = width;
            var shadow = K.Box(); K.Fill(shadow); shadow.style.top = 14f; shadow.style.bottom = -14f; shadow.style.left = 6f; shadow.style.right = 6f;
            shadow.style.backgroundColor = new Color(0.02f, 0.02f, 0.08f, 0.55f); K.Radius(shadow, 26f); shadow.pickingMode = PickingMode.Ignore;
            wrap.Add(shadow);
            var card = K.Box(); card.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.97f); K.Radius(card, 24f); Border(card, Line, 1f);
            K.Pad(card, 30f); card.pickingMode = PickingMode.Position;
            wrap.Add(card);
            return card;
        }

        // ======================= фон главного меню: бегущий код =======================
        VisualElement BuildCodeBackground()
        {
            var bg = Layer(); bg.style.backgroundColor = Deep; bg.style.overflow = Overflow.Hidden;
            var pool = CodePool();
            var rnd = new System.Random(20260924);
            // пять колонок без наложения: по краям ярче и быстрее, в центре (под карточкой) — тусклее
            float[] op = { 0.34f, 0.22f, 0.1f, 0.22f, 0.34f }, sp = { 30f, 19f, 12f, 22f, 27f }, fs = { 17f, 16f, 15f, 16f, 17f };
            for (int i = 0; i < 5; i++) AddColumn(bg, pool, rnd, i * 20f, 20f, fs[i], op[i], sp[i]);
            // затемнения: виньетка, сверху и снизу, мягкое свечение в центре
            var vig = Layer(); vig.style.backgroundImage = new StyleBackground(Radial(128, new Color(Deep.r, Deep.g, Deep.b, 0f), new Color(Deep.r, Deep.g, Deep.b, 0.92f), 1.7f));
            Stretch(vig); bg.Add(vig);
            var top = K.Box(); top.pickingMode = PickingMode.Ignore; top.style.position = Position.Absolute; top.style.left = 0f; top.style.right = 0f; top.style.top = 0f; top.style.height = Length.Percent(22f);
            top.style.backgroundImage = new StyleBackground(Vertical(new Color(Deep.r, Deep.g, Deep.b, 1f), new Color(Deep.r, Deep.g, Deep.b, 0f))); Stretch(top); bg.Add(top);
            var bottom = K.Box(); bottom.pickingMode = PickingMode.Ignore; bottom.style.position = Position.Absolute; bottom.style.left = 0f; bottom.style.right = 0f; bottom.style.bottom = 0f; bottom.style.height = Length.Percent(26f);
            bottom.style.backgroundImage = new StyleBackground(Vertical(new Color(Deep.r, Deep.g, Deep.b, 0f), new Color(Deep.r, Deep.g, Deep.b, 1f))); Stretch(bottom); bg.Add(bottom);
            var glow = K.Box(); glow.pickingMode = PickingMode.Ignore; glow.style.position = Position.Absolute; glow.style.left = Length.Percent(50f); glow.style.top = Length.Percent(50f);
            glow.style.width = 1300f; glow.style.height = 1000f; glow.style.marginLeft = -650f; glow.style.marginTop = -500f;
            glow.style.backgroundImage = new StyleBackground(Radial(128, new Color(0.33f, 0.2f, 0.62f, 0.34f), new Color(0.33f, 0.2f, 0.62f, 0f), 1.2f)); Stretch(glow); bg.Add(glow);
            return bg;
        }

        static void Stretch(VisualElement v) { v.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f)); }

        void AddColumn(VisualElement bg, List<string> pool, System.Random rnd, float leftPct, float widthPct, float size, float opacity, float speed)
        {
            var col = K.Box(); col.pickingMode = PickingMode.Ignore;
            col.style.position = Position.Absolute; col.style.left = Length.Percent(leftPct); col.style.width = Length.Percent(widthPct);
            col.style.top = 0f; col.style.bottom = 0f; col.style.overflow = Overflow.Hidden; col.style.opacity = opacity;
            K.Pad(col, 0f, 0f, 0f, 18f);
            var holder = K.Box(); holder.pickingMode = PickingMode.Ignore; holder.style.position = Position.Absolute; holder.style.left = 18f; holder.style.right = 0f; holder.style.top = 0f;
            string text = ColumnText(pool, rnd, 120, 1 + rnd.Next(400));
            var a = CodeLabel(text, size); var b = CodeLabel(text, size);
            holder.Add(a); holder.Add(b); col.Add(holder); bg.Add(col);
            columns.Add(new Column { holder = holder, a = a, speed = speed, y = (float)rnd.NextDouble() * 800f });
        }

        static Label CodeLabel(string text, float size)
        {
            var l = K.T(text, size, Text, true);
            l.style.whiteSpace = WhiteSpace.Pre; l.style.unityTextAlign = TextAnchor.UpperLeft;
            return l;
        }

        static string ColumnText(List<string> pool, System.Random rnd, int lines, int startNo)
        {
            var sb = new StringBuilder();
            int i = rnd.Next(pool.Count), n = startNo;
            string inStr = null;
            for (int k = 0; k < lines; k++, n++)
            {
                string s = pool[(i + k) % pool.Count];
                sb.Append("<color=#3A4190>").Append(n.ToString().PadLeft(4)).Append("</color>   ");
                sb.Append(GameRich(s, ref inStr)).Append('\n');
            }
            return sb.ToString();
        }

        // Подсветка в цветах игры (солнце, розовый, мята, небо)
        static string GameRich(string s, ref string inStr)
        {
            var spans = PySyntax.Line(s, ref inStr);
            var sb = new StringBuilder(s.Length * 2);
            int pos = 0;
            foreach (var sp in spans)
            {
                if (sp.Start > pos) sb.Append(K.Esc(s.Substring(pos, sp.Start - pos)));
                string part = s.Substring(sp.Start, sp.Len);
                string c;
                switch (sp.Kind)
                {
                    case Tok.Control: c = "#FF7AB8"; break;
                    case Tok.Keyword: case Tok.Self: c = "#9DA8FF"; break;
                    case Tok.Builtin: case Tok.Func: case Tok.Def: c = "#FFD86B"; break;
                    case Tok.Class: c = "#7BE0B5"; break;
                    case Tok.Str: c = "#FFB482"; break;
                    case Tok.Num: c = "#B9F0A2"; break;
                    case Tok.Comment: c = "#6A71B8"; break;
                    case Tok.Op: c = "#C3C7F2"; break;
                    default: c = PySyntax.IsIdStart(part[0]) ? "#A9DCFF" : "#C3C7F2"; break;
                }
                sb.Append("<color=").Append(c).Append('>').Append(K.Esc(part)).Append("</color>");
                pos = sp.Start + sp.Len;
            }
            if (pos < s.Length) sb.Append(K.Esc(s.Substring(pos)));
            return sb.ToString();
        }

        List<string> CodePool()
        {
            var list = new List<string>(Snippet.Split('\n'));
            // плюс настоящий код из задач игры — решения и заготовки
            if (g.Tasks != null && g.Tasks.tasks != null)
                foreach (var t in g.Tasks.tasks)
                {
                    if (t.language != "python" || t.IsChoice) continue;   // подсветка фона — питоновская
                    foreach (var src in new[] { t.solution, t.starter })
                        if (!string.IsNullOrEmpty(src)) { list.Add(""); list.AddRange(src.Replace("\r", "").Split('\n')); }
                }
            return list;
        }

        const string Snippet =
@"# Кодзилла Софт · сервис утреннего кофе
import random
from datetime import date

class Barista:
    def __init__(self, name, cups=0):
        self.name = name
        self.cups = cups

    def brew(self, order):
        if order.size > 400:
            raise ValueError(""кружка не влезает"")
        self.cups += 1
        return f""{self.name} сварил {order.kind}""

def sprint_report(tickets):
    done = [t for t in tickets if t.status == ""done""]
    print(f""Закрыто: {len(done)} из {len(tickets)}"")
    return len(done) / max(1, len(tickets))

bugs = {""KOD-106"": ""падает на пустом вводе"", ""KOD-112"": ""делит на ноль""}
for key, text in bugs.items():
    print(key, ""—"", text)

def fizzbuzz(n):
    for i in range(1, n + 1):
        if i % 15 == 0:
            yield ""FizzBuzz""
        elif i % 3 == 0:
            yield ""Fizz""
        elif i % 5 == 0:
            yield ""Buzz""
        else:
            yield str(i)

# TODO: спросить Гену, зачем тут while True
while True:
    line = input()
    if not line:
        break
    words = line.split()
    print(len(words), ""слов"")

team = [""Гена"", ""Оля"", ""Стажёр"", ""Дима"", ""Ира""]
standup = sorted(team, key=len)
print(""Сегодня первым рассказывает"", standup[0])

def average(prices):
    total = 0
    for p in prices:
        total += p
    return round(total / len(prices), 2)

commits = {""Гена"": 42, ""Оля"": 57, ""Стажёр"": 3}
best = max(commits, key=commits.get)
print(f""Больше всех коммитов у {best}"")

try:
    salary = int(input(""Часы: "")) * 500
except ValueError:
    print(""Нужно число, а не слово"")
else:
    print(""Заработано:"", salary)

def deploy(env=""staging""):
    assert env in (""staging"", ""prod"")
    print(""Выкатываем в"", env, ""…"")
    return True";

        // ======================= главное меню =======================
        VisualElement BuildMenu()
        {
            var layer = Centered();
            var brand = K.Box(true); brand.style.alignItems = Align.Center; brand.pickingMode = PickingMode.Ignore;
            brand.Add(new Icon("kodzilla", Pink, 22f));
            var bl = K.B("КОДЗИЛЛА СОФТ   ·   ПРОГРАММА СТАЖИРОВКИ", 15f, Muted); bl.style.marginLeft = 10f; bl.style.letterSpacing = 3f; brand.Add(bl);
            layer.Add(brand);

            // заголовок с тенью
            var tw = K.Box(); tw.pickingMode = PickingMode.Ignore; tw.style.marginTop = 4f;
            var tsh = K.B("Стажёр", 132f, new Color(0.03f, 0.03f, 0.1f, 0.85f), true); tsh.style.position = Position.Absolute; tsh.style.top = 9f; tsh.style.left = 0f; tw.Add(tsh);
            var title = K.B("Стажёр", 132f, Sun, true); tw.Add(title);
            layer.Add(tw);
            var sub = K.T("Выбери направление — Backend, Frontend или DevOps — и дорасти от стажёра до Middle.", 20f, Muted); sub.style.marginTop = -6f; sub.style.marginBottom = 30f;
            layer.Add(sub);

            VisualElement wrap;
            menuCard = CardBox(540f, out wrap);
            menuMain = K.Box(); menuMain.pickingMode = PickingMode.Ignore; menuCard.Add(menuMain);
            menuNew = K.Box(); menuNew.pickingMode = PickingMode.Ignore; menuCard.Add(menuNew);
            layer.Add(wrap);

            var foot = K.Box(true); foot.style.alignItems = Align.Center; foot.style.marginTop = 30f; foot.pickingMode = PickingMode.Ignore; foot.style.opacity = 0.8f;
            foot.Add(K.T("v0.7", 14f, Muted)); foot.Add(Dot());
            foot.Add(Keycap("Esc", 0.8f)); var fb = K.T("назад", 14f, Muted); foot.Add(fb);
            layer.Add(foot);
            return layer;
        }

        static VisualElement Dot() { var d = K.T("·", 14f, Muted); d.style.marginLeft = 12f; d.style.marginRight = 12f; return d; }

        void RefreshMenu()
        {
            menuMain.Clear(); menuNew.Clear();
            menuMain.style.display = newGamePage ? DisplayStyle.None : DisplayStyle.Flex;
            menuNew.style.display = newGamePage ? DisplayStyle.Flex : DisplayStyle.None;
            if (!newGamePage)
            {
                bool has = g.HasProgress;
                if (has)
                {
                    var c = new UiBtn("Продолжить", () => g.UiContinue(), Sun, SunHover, SunLip, Ink, "play",
                        g.RankFull + "  ·  " + g.DoneCount + " из " + g.TotalCount + " задач  ·  " + Progress.DifficultyName((Difficulty)g.Save.difficulty), 76f);
                    c.style.marginTop = 0f; menuMain.Add(c);
                    menuMain.Add(new UiBtn("Новая игра", () => OpenNewGame(), Indigo, IndigoHover, IndigoLip, Text, "plus"));
                }
                else
                {
                    var c = new UiBtn("Начать стажировку", () => OpenNewGame(), Sun, SunHover, SunLip, Ink, "play", "Первый день в «Кодзилла Софт»", 76f);
                    c.style.marginTop = 0f; menuMain.Add(c);
                }
                menuMain.Add(new UiBtn("Настройки", () => OpenSettings(), Indigo, IndigoHover, IndigoLip, Text, "gear"));
                menuMain.Add(new UiBtn("Выйти из игры", () => g.UiQuit(), Ghost, GhostHover, GhostLip, Muted, "door", null, 56f, false));
                return;
            }

            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore; head.style.marginBottom = 6f;
            head.Add(SmallIconButton("chevL", () => CloseNewGame()));
            var ht = K.B("Новая игра", 28f, Text); ht.style.marginLeft = 12f; head.Add(ht);
            menuNew.Add(head);
            var pl = SectionLabel("НАПРАВЛЕНИЕ"); pl.style.marginTop = 10f; menuNew.Add(pl);
            menuNew.Add(Caption("Сначала у всех общая база: терминал, Git, HTTP, дебаг. Потом — задачи профессии до уровня Middle."));
            var grid = K.Box(true); grid.style.flexWrap = Wrap.Wrap; grid.style.justifyContent = Justify.SpaceBetween; grid.pickingMode = PickingMode.Ignore; grid.style.marginTop = 4f;
            foreach (var pid in Professions.Ids) grid.Add(ProfessionCard(pid));
            menuNew.Add(grid);
            var dl = SectionLabel("СЛОЖНОСТЬ — МОЖНО ПОМЕНЯТЬ В ПАУЗЕ"); dl.style.marginTop = 18f; menuNew.Add(dl);
            var seg = K.Box(true); seg.style.marginTop = 8f; seg.style.backgroundColor = Well; K.Radius(seg, 12f); K.Pad(seg, 4f); Border(seg, Line, 1f);
            string[] mults = { "×1.0", "×1.2", "×1.6" };
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                int id = (int)d; bool sel = newDiff == id;
                var pill = K.Box(true); K.Grow(pill); pill.style.height = 44f; K.Radius(pill, 9f); pill.style.justifyContent = Justify.Center; pill.style.alignItems = Align.Center;
                pill.style.backgroundColor = sel ? Sun : Color.clear;
                pill.Add(K.B(Progress.DifficultyName(d), 17f, sel ? Ink : Text));
                var ml = K.T(mults[id], 13f, sel ? Ink : Muted); ml.style.marginLeft = 8f; pill.Add(ml);
                pill.RegisterCallback<PointerEnterEvent>(e => { if (newDiff != id) pill.style.backgroundColor = Ghost; });
                pill.RegisterCallback<PointerLeaveEvent>(e => { pill.style.backgroundColor = newDiff == id ? Sun : Color.clear; });
                pill.RegisterCallback<ClickEvent>(e => { newDiff = id; RefreshMenu(); });
                seg.Add(pill);
            }
            menuNew.Add(seg);
            string[] ddesc = { "Каждая строка объясняется, подсказки бесплатно, после трёх попыток можно подсмотреть решение.", "Разбор строк — только в отладчике, подсказки за монеты.", "Дедлайны, без теории и подсказок, ошибки без перевода." };
            var dd = K.T(ddesc[Mathf.Clamp(newDiff, 0, 2)], 14f, Muted, false, false, true); dd.style.marginTop = 6f; menuNew.Add(dd);
            if (g.HasProgress)
            {
                var w = K.Box(true); w.style.alignItems = Align.Center; w.style.marginTop = 14f; w.pickingMode = PickingMode.Ignore;
                w.Add(new Icon("warning", Pink, 18f));
                var wl = K.T("Текущий прогресс (" + g.RankFull + ", " + g.DoneCount + " из " + g.TotalCount + ") будет сброшен. Пройденные направления останутся.", 14f, Pink, false, false, true); wl.style.marginLeft = 8f; wl.style.flexShrink = 1f;
                w.Add(wl); menuNew.Add(w);
            }
            var row = K.Box(true); row.pickingMode = PickingMode.Ignore; row.style.marginTop = 8f;
            var back = new UiBtn("Назад", () => CloseNewGame(), Ghost, GhostHover, GhostLip, Muted, null, null, 58f, false, true); back.style.width = 150f; back.style.marginRight = 12f;
            var go = new UiBtn("Начать: " + Professions.Name(newProf), () => g.UiNewGame((Difficulty)newDiff, newProf), Sun, SunHover, SunLip, Ink, "play", null, 58f, true); K.Grow(go);
            row.Add(back); row.Add(go); menuNew.Add(row);
        }

        public void RefreshMenuPublic() { if (g.CurMode == GameRoot.Mode.Menu && !settingsOpen) RefreshMenu(); }

        public static Color ProfColor(string id)
        {
            switch (id) { case "backend": return Mint; case "frontend": return Sky; case "devops": return Sun; default: return Pink; }
        }

        VisualElement ProfessionCard(string id)
        {
            bool locked = !Career.CanPick(id), sel = newProf == id && !locked, done = Career.Done(id);
            var accent = ProfColor(id);
            var c = K.Box(); c.style.width = Length.Percent(49f); c.style.marginTop = 10f; K.Radius(c, 14f); K.Pad(c, 14f, 14f, 14f, 14f);
            c.style.backgroundColor = sel ? CardHi : Well; Border(c, sel ? accent : Line, sel ? 2f : 1f);
            if (locked) c.style.opacity = 0.6f;
            var top = K.Box(true); top.style.alignItems = Align.Center; top.pickingMode = PickingMode.Ignore;
            var ic = K.Box(); ic.pickingMode = PickingMode.Ignore; ic.style.width = 38f; ic.style.height = 38f; K.Radius(ic, 11f); ic.style.justifyContent = Justify.Center; ic.style.alignItems = Align.Center;
            ic.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.18f); ic.Add(new Icon(locked ? "lock" : Professions.Icon(id), accent, 22f)); top.Add(ic);
            var nm = K.B(Professions.Name(id), 20f, Text); nm.style.marginLeft = 12f; top.Add(nm);
            top.Add(K.Spacer());
            if (done) { var b = K.B("ПРОЙДЕНО", 11f, Ink); K.Pad(b, 3f, 7f, 3f, 7f); K.Radius(b, 6f); b.style.backgroundColor = Mint; top.Add(b); }
            c.Add(top);
            string desc = locked ? "Откроется, когда пройдёшь Backend, Frontend и DevOps (" + Career.Count + " из 3)." : Professions.Stack(id);
            var dl = K.T(desc, 13f, Muted, false, false, true); dl.style.marginTop = 8f; c.Add(dl);
            if (!locked)
            {
                c.RegisterCallback<PointerEnterEvent>(e => { if (newProf != id) c.style.backgroundColor = Ghost; });
                c.RegisterCallback<PointerLeaveEvent>(e => { c.style.backgroundColor = newProf == id ? CardHi : Well; });
                c.RegisterCallback<ClickEvent>(e => { newProf = id; RefreshMenu(); });
            }
            return c;
        }

        void OpenNewGame()
        {
            newGamePage = true; newDiff = g.HasProgress ? g.Save.difficulty : 0;
            newProf = g.HasProgress && Career.CanPick(g.Profession) ? g.Profession : "backend";
            menuCard.parent.style.width = 700f;
            RefreshMenu(); Pop(menuCard);
        }
        public void OpenNewGamePublic() { settingsOpen = false; OpenNewGame(); }
        void CloseNewGame() { newGamePage = false; menuCard.parent.style.width = 540f; RefreshMenu(); Pop(menuCard); }

        static Label Caption(string s) { var l = K.T(s, 14f, Muted, false, false, true); l.style.marginTop = 2f; return l; }

        VisualElement SmallIconButton(string icon, Action a)
        {
            var b = K.Box(true); b.style.width = 40f; b.style.height = 40f; K.Radius(b, 10f); b.style.justifyContent = Justify.Center; b.style.alignItems = Align.Center;
            b.style.backgroundColor = Well; Border(b, Line, 1f);
            b.Add(new Icon(icon, Text, 20f));
            b.RegisterCallback<PointerEnterEvent>(e => b.style.backgroundColor = Ghost);
            b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = Well);
            b.RegisterCallback<ClickEvent>(e => a());
            return b;
        }

        // ======================= пауза =======================
        VisualElement BuildPause()
        {
            var layer = Centered();
            VisualElement wrap;
            pauseCard = CardBox(520f, out wrap);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            head.Add(K.B("Пауза", 54f, Sun, true)); head.Add(K.Spacer());
            head.Add(Keycap("Esc", 0.85f)); head.Add(K.T("продолжить", 14f, Muted));
            pauseCard.Add(head);
            pauseTask = K.T("", 15f, Muted, false, false, true); pauseTask.style.marginTop = -2f; pauseTask.style.marginBottom = 10f; pauseCard.Add(pauseTask);
            var cont = new UiBtn("Продолжить", () => g.UiResume(), Sun, SunHover, SunLip, Ink, "play"); pauseCard.Add(cont);
            pauseCard.Add(new UiBtn("Гардероб — сменить внешность", () => g.UiWardrobe(), Indigo, IndigoHover, IndigoLip, Text, "shirt"));
            pauseCamera = new UiBtn("Камера", () => { g.UiToggleView(); RefreshPause(); }, Indigo, IndigoHover, IndigoLip, Text, "camera"); pauseCard.Add(pauseCamera);
            pauseCard.Add(new UiBtn("Настройки", () => OpenSettings(), Indigo, IndigoHover, IndigoLip, Text, "gear"));

            var dl = SectionLabel("СЛОЖНОСТЬ — МОЖНО МЕНЯТЬ В ЛЮБОЙ МОМЕНТ"); dl.style.marginTop = 20f; pauseCard.Add(dl);
            var seg = K.Box(true); seg.style.marginTop = 8f; seg.style.backgroundColor = Well; K.Radius(seg, 12f); K.Pad(seg, 4f); Border(seg, Line, 1f);
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var dd = d;
                var pill = K.Box(true); K.Grow(pill); pill.style.height = 40f; K.Radius(pill, 9f); pill.style.justifyContent = Justify.Center; pill.style.alignItems = Align.Center;
                pill.Add(K.B(Progress.DifficultyName(d), 16f, Text));
                pill.RegisterCallback<ClickEvent>(e => { g.UiSetDifficulty(dd); RefreshPause(); });
                pill.RegisterCallback<PointerEnterEvent>(e => { if (g.Save.difficulty != (int)dd) pill.style.backgroundColor = Ghost; });
                pill.RegisterCallback<PointerLeaveEvent>(e => RefreshPause());
                pauseDiffPills.Add(pill); seg.Add(pill);
            }
            pauseCard.Add(seg);
            pauseDiffDesc = K.T("", 13f, Muted, false, false, true); pauseDiffDesc.style.marginTop = 6f; pauseCard.Add(pauseDiffDesc);

            var pl = SectionLabel("НАПРАВЛЕНИЕ — ПРОГРЕСС СОХРАНЯЕТСЯ"); pl.style.marginTop = 18f; pauseCard.Add(pl);
            var pseg = K.Box(true); pseg.style.marginTop = 8f; pseg.style.backgroundColor = Well; K.Radius(pseg, 12f); K.Pad(pseg, 4f); Border(pseg, Line, 1f);
            foreach (var pid in Professions.Ids)
            {
                var id = pid;
                var pill = K.Box(true); K.Grow(pill); pill.style.height = 40f; K.Radius(pill, 9f); pill.style.justifyContent = Justify.Center; pill.style.alignItems = Align.Center;
                pill.Add(new Icon(Professions.Icon(id), Text, 16f)); var pn = K.B(Professions.Name(id), 15f, Text); pn.style.marginLeft = 6f; pill.Add(pn);
                pill.userData = id;
                pill.RegisterCallback<ClickEvent>(e => { if (!Career.CanPick(id)) { pauseProfDesc.text = "Fullstack откроется, когда пройдёшь Backend, Frontend и DevOps (" + Career.Count + " из 3)."; return; } g.UiSetProfession(id); RefreshPause(); });
                pill.RegisterCallback<PointerEnterEvent>(e => { if (g.Profession != id && Career.CanPick(id)) pill.style.backgroundColor = Ghost; });
                pill.RegisterCallback<PointerLeaveEvent>(e => RefreshPause());
                pauseProfPills.Add(pill); pseg.Add(pill);
            }
            pauseCard.Add(pseg);
            pauseProfDesc = K.T("", 13f, Muted, false, false, true); pauseProfDesc.style.marginTop = 6f; pauseCard.Add(pauseProfDesc);

            var sep = K.Box(); sep.pickingMode = PickingMode.Ignore; sep.style.height = 1f; sep.style.backgroundColor = Line; sep.style.marginTop = 18f; sep.style.marginBottom = 2f; pauseCard.Add(sep);
            var row = K.Box(true); row.pickingMode = PickingMode.Ignore;
            var home = new UiBtn("В главное меню", () => g.UiToMenu(), Ghost, GhostHover, GhostLip, Text, "home", null, 52f, false); K.Grow(home); home.style.marginRight = 10f;
            var quit = new UiBtn("Выйти", () => g.UiQuit(), Ghost, GhostHover, GhostLip, Muted, "door", null, 52f, false); quit.style.width = 150f;
            row.Add(home); row.Add(quit); pauseCard.Add(row);
            layer.Add(wrap);
            return layer;
        }

        static Label SectionLabel(string s) { var l = K.B(s, 12f, Muted); l.style.letterSpacing = 1.5f; return l; }

        void RefreshPause()
        {
            var t = g.CurrentTaskPublic;
            pauseTask.text = g.RankFull + "  ·  " + (t != null && !g.PathComplete ? g.TaskCodeOf(t) + " " + K.Esc(t.title) + "  ·  " : "направление пройдено  ·  ") + g.Save.money + " монет";
            foreach (var pill in pauseProfPills)
            {
                string id = pill.userData as string;
                bool sel = g.Profession == id, can = Career.CanPick(id);
                pill.style.backgroundColor = sel ? ProfColor(id) : Color.clear;
                pill.style.opacity = can ? 1f : 0.45f;
                var l = pill.Q<Label>(); if (l != null) l.style.color = sel ? Ink : Text;
                var ic = pill.Q<Icon>(); if (ic != null) ic.Set(can ? Professions.Icon(id) : "lock", sel ? Ink : Text);
            }
            pauseProfDesc.text = Professions.About(g.Profession) + " Сдано " + g.DoneCount + " из " + g.TotalCount + ".";
            pauseCamera.SetTitle(g.FirstPerson ? "Камера: от первого лица" : "Камера: от третьего лица");
            for (int i = 0; i < pauseDiffPills.Count; i++)
            {
                bool sel = g.Save.difficulty == i;
                pauseDiffPills[i].style.backgroundColor = sel ? Sun : Color.clear;
                var l = pauseDiffPills[i].Q<Label>(); if (l != null) l.style.color = sel ? Ink : Text;
            }
            string[] desc = { "Каждая строка объясняется, подсказки бесплатно, можно подсмотреть решение.", "Разбор строк — в отладчике, подсказки за монеты. Награды ×1.2.", "Дедлайны, без теории и подсказок, ошибки без перевода. Награды ×1.6." };
            pauseDiffDesc.text = desc[Mathf.Clamp(g.Save.difficulty, 0, 2)];
        }

        // ======================= настройки =======================
        static readonly string[] TabNames = { "Игра", "Экран", "Графика", "Звук", "Управление", "Интерфейс" };
        static readonly string[] TabIcons = { "clock", "monitor", "image", "sound", "mouse", "layout" };

        VisualElement BuildSettings()
        {
            var layer = Centered();
            VisualElement wrap;
            settingsPanel = CardBox(1160f, out wrap);
            settingsPanel.style.height = 800f; K.Pad(settingsPanel, 28f, 34f, 24f, 34f);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            head.Add(new Icon("gear", Sun, 34f));
            var ht = K.B("Настройки", 42f, Text, true); ht.style.marginLeft = 14f; head.Add(ht);
            head.Add(K.Spacer()); head.Add(Keycap("Esc", 0.85f)); head.Add(K.T("назад", 14f, Muted));
            settingsPanel.Add(head);

            var tabs = K.Box(true); tabs.style.marginTop = 18f; tabs.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int ti = i;
                var p = K.Box(true); p.style.height = 46f; K.Pad(p, 0f, 20f, 0f, 16f); K.Radius(p, 23f); p.style.alignItems = Align.Center; p.style.marginRight = 10f;
                p.Add(new Icon(TabIcons[i], Text, 20f));
                var l = K.B(TabNames[i], 17f, Text); l.style.marginLeft = 9f; p.Add(l);
                p.RegisterCallback<ClickEvent>(e => { tab = ti; RefreshSettings(); });
                p.RegisterCallback<PointerEnterEvent>(e => { if (tab != ti) p.style.backgroundColor = Ghost; });
                p.RegisterCallback<PointerLeaveEvent>(e => PaintTabs());
                tabPills.Add(p); tabs.Add(p);
            }
            settingsPanel.Add(tabs);
            var sep = K.Box(); sep.pickingMode = PickingMode.Ignore; sep.style.height = 1f; sep.style.backgroundColor = Line; sep.style.marginTop = 16f; settingsPanel.Add(sep);

            settingsScroll = new ScrollBox(); settingsScroll.style.marginTop = 4f;
            settingsScroll.RegisterCallback<WheelEvent>(e => { settingsScroll.Wheel(e.delta.y * 36f); e.StopPropagation(); });
            settingsPanel.Add(settingsScroll);

            var foot = K.Box(true); foot.style.alignItems = Align.Center; foot.style.marginTop = 12f; foot.pickingMode = PickingMode.Ignore;
            var reset = new UiBtn("Сбросить всё", () => { GameConfig.ResetAll(); RefreshSettings(); }, Ghost, GhostHover, GhostLip, Muted, "reset", null, 50f, false); reset.style.width = 220f; reset.style.marginTop = 0f;
            foot.Add(reset); foot.Add(K.Spacer());
            var done = new UiBtn("Готово", () => CloseSettings(), Sun, SunHover, SunLip, Ink, "check", null, 50f, false); done.style.width = 220f; done.style.marginTop = 0f;
            foot.Add(done);
            settingsPanel.Add(foot);
            layer.Add(wrap);
            return layer;
        }

        void PaintTabs()
        {
            for (int i = 0; i < tabPills.Count; i++)
            {
                bool sel = i == tab; var p = tabPills[i];
                p.style.backgroundColor = sel ? Sun : Well; Border(p, sel ? Sun : Line, 1f);
                var ic = p.Q<Icon>(); if (ic != null) ic.Set(ic.Kind, sel ? Ink : Text);
                var l = p.Q<Label>(); if (l != null) l.style.color = sel ? Ink : Text;
            }
        }

        void OpenSettings() { settingsOpen = true; RefreshSettings(); settingsScroll.ToTop(); }
        void CloseSettings() { settingsOpen = false; GameConfig.Save(); if (g.CurMode == GameRoot.Mode.Menu) RefreshMenu(); else RefreshPause(); }

        void RefreshSettings()
        {
            PaintTabs();
            var c = settingsScroll.Content; c.Clear();
            var S = GameConfig.S;
            switch (tab - 1)
            {
                case -1:
                    {
                        c.Add(Row("Длина рабочего дня", DayLength.About[Mathf.Clamp(S.dayLength, 0, 2)],
                            new UiSelect(DayLength.Names, S.dayLength, v => { S.dayLength = v; GameConfig.Commit(); RefreshSettings(); })));
                        c.Add(Row("Кровь на обеде", "Брызги, лужи и пятна в городе.", new UiToggle(S.blood, v => { S.blood = v; GameConfig.Commit(); })));
                        var note = Caption("Длину дня можно менять в любой момент: новая скорость часов включится сразу.");
                        note.style.marginTop = 14f; c.Add(note);
                        break;
                    }
                case 0:
                    {
                        var res = GameConfig.Resolutions(); var cur = GameConfig.CurrentRes;
                        var names = new string[res.Count]; int ri = 0;
                        for (int i = 0; i < res.Count; i++) { names[i] = res[i].x + " × " + res[i].y; if (res[i] == cur) ri = i; }
                        c.Add(Row("Режим окна", "Полный экран даёт максимум кадров, окно без рамки удобнее переключать.",
                            new UiSelect(GameConfig.WindowModes, S.windowMode, v => { S.windowMode = v; GameConfig.Commit(); })));
                        c.Add(Row("Разрешение", Application.isEditor ? "В редакторе Unity не меняется — работает в собранной игре." : "Размер картинки в пикселях.",
                            new UiSelect(names, ri, v => { S.resW = res[v].x; S.resH = res[v].y; GameConfig.Commit(); })));
                        c.Add(Row("Вертикальная синхронизация", "Убирает разрывы кадра. Частоту тогда задаёт монитор.",
                            new UiToggle(S.vsync, v => { S.vsync = v; GameConfig.Commit(); RefreshSettings(); })));
                        var fps = new UiSelect(GameConfig.FpsNames, S.fps, v => { S.fps = v; GameConfig.Commit(); });
                        fps.Enabled = !S.vsync;
                        c.Add(Row("Ограничение кадров", S.vsync ? "Работает, когда синхронизация выключена." : "Меньше кадров — тише вентиляторы и дольше батарея.", fps));
                        c.Add(Row("Яркость", "Общая яркость картинки (нужна включённая пост-обработка).",
                            new UiSlider(0.5f, 1.5f, S.brightness, 0.05f, v => Mathf.RoundToInt(v * 100) + "%", v => { S.brightness = v; GameConfig.ApplyGraphics(); })));
                        break;
                    }
                case 1:
                    {
                        string[] q = S.quality == 4 ? GameConfig.QualityNames : new[] { "Низкое", "Среднее", "Высокое", "Ультра" };
                        c.Add(Row("Качество графики", "Готовый набор параметров ниже. Поменяешь любой — станет «Своё».",
                            new UiSelect(q, S.quality, v => { if (v < 4) { GameConfig.SetQuality(v); GameConfig.Commit(); RefreshSettings(); } })));
                        c.Add(Row("Масштаб рендера", "Меньше 100% — быстрее, но мягче; больше — чётче и тяжелее.",
                            new UiSlider(0.5f, 1.5f, S.renderScale, 0.05f, v => Mathf.RoundToInt(v * 100) + "%", v => { S.renderScale = v; GameConfig.GraphicsTouched(); GameConfig.ApplyGraphics(); }, () => RefreshSettings())));
                        c.Add(Row("Сглаживание", "Убирает «лесенку» на краях предметов.",
                            new UiSelect(GameConfig.AaNames, S.aa, v => { S.aa = v; GameConfig.GraphicsTouched(); GameConfig.Commit(); RefreshSettings(); })));
                        c.Add(Row("Тени", "Тени от солнца из окон и ламп.",
                            new UiSelect(GameConfig.ShadowNames, S.shadows, v => { S.shadows = v; GameConfig.GraphicsTouched(); GameConfig.Commit(); RefreshSettings(); })));
                        c.Add(Row("Пост-обработка", "Мягкое свечение, сочные цвета и виньетка.",
                            new UiToggle(S.post, v => { S.post = v; GameConfig.GraphicsTouched(); GameConfig.Commit(); RefreshSettings(); })));
                        c.Add(Row("Код на мониторах коллег", "Прокрутка кода на чужих экранах в офисе.",
                            new UiToggle(S.screenAnim, v => { S.screenAnim = v; GameConfig.Commit(); })));
                        break;
                    }
                case 2:
                    {
                        Func<float, string> pct = v => Mathf.RoundToInt(v * 100) + "%";
                        c.Add(Row("Общая громкость", "Все звуки игры.", new UiSlider(0f, 1f, S.master, 0.05f, pct, v => { S.master = v; GameConfig.ApplyAudio(); })));
                        c.Add(Row("Музыка", "Фоновая музыка офиса.", new UiSlider(0f, 1f, S.music, 0.05f, pct, v => { S.music = v; })));
                        c.Add(Row("Эффекты", "Клавиатура, кофемашина, шаги, баги.", new UiSlider(0f, 1f, S.sfx, 0.05f, pct, v => { S.sfx = v; })));
                        c.Add(Row("Интерфейс", "Щелчки кнопок и уведомления.", new UiSlider(0f, 1f, S.uiSound, 0.05f, pct, v => { S.uiSound = v; })));
                        var note = Caption("Звуков в игре пока мало — громкость уже запомнится и применится к новым сразу.");
                        note.style.marginTop = 14f; c.Add(note);
                        break;
                    }
                case 3:
                    {
                        c.Add(Row("Чувствительность мыши", "Как быстро поворачивается камера.",
                            new UiSlider(0.2f, 3f, S.sensitivity, 0.05f, v => "×" + v.ToString("0.00"), v => { S.sensitivity = v; GameConfig.ApplyControls(); })));
                        c.Add(Row("Инвертировать ось Y", "Мышь вверх — камера вниз.", new UiToggle(S.invertY, v => { S.invertY = v; GameConfig.Commit(); })));
                        c.Add(Row("Поле зрения", "Угол обзора камеры от третьего лица (от первого — на 12° шире).",
                            new UiSlider(50f, 90f, S.fov, 1f, v => Mathf.RoundToInt(v) + "°", v => { S.fov = v; GameConfig.ApplyControls(); })));
                        c.Add(Row("Вид камеры", "То же, что клавиша V.",
                            new UiSelect(new[] { "От третьего лица", "От первого лица" }, g.FirstPerson ? 1 : 0, v => { if ((v == 1) != g.FirstPerson) g.UiToggleView(); })));
                        var kl = SectionLabel("КЛАВИШИ"); kl.style.marginTop = 24f; c.Add(kl);
                        var grid = K.Box(true); grid.style.flexWrap = Wrap.Wrap; grid.style.marginTop = 6f; grid.pickingMode = PickingMode.Ignore;
                        var colA = K.Box(); colA.style.width = Length.Percent(50f); colA.pickingMode = PickingMode.Ignore;
                        var colB = K.Box(); colB.style.width = Length.Percent(50f); colB.pickingMode = PickingMode.Ignore;
                        colA.Add(Caption("В офисе"));
                        colA.Add(KeyRow(new[] { "W", "A", "S", "D" }, "Ходить"));
                        colA.Add(KeyRow(new[] { "Shift" }, "Бежать"));
                        colA.Add(KeyRow(new[] { "Space" }, "Прыжок"));
                        colA.Add(KeyRow(new[] { "E" }, "Действие: сесть, поговорить"));
                        colA.Add(KeyRow(new[] { "ЛКМ" }, "Поймать баг"));
                        colA.Add(KeyRow(new[] { "V" }, "Камера от 1-го / 3-го лица"));
                        colA.Add(KeyRow(new[] { "Esc" }, "Пауза и назад"));
                        colB.Add(Caption("За компьютером"));
                        colB.Add(KeyRow(new[] { "Ctrl", "F5" }, "Запустить программу"));
                        colB.Add(KeyRow(new[] { "Ctrl", "Enter" }, "Проверить задачу"));
                        colB.Add(KeyRow(new[] { "F5" }, "Отладка / продолжить"));
                        colB.Add(KeyRow(new[] { "F9" }, "Точка останова"));
                        colB.Add(KeyRow(new[] { "F10" }, "Шаг"));
                        colB.Add(KeyRow(new[] { "F11" }, "Шаг с заходом"));
                        colB.Add(KeyRow(new[] { "Ctrl", "Space" }, "Подсказки кода"));
                        colB.Add(KeyRow(new[] { "Ctrl", "/" }, "Закомментировать"));
                        grid.Add(colA); grid.Add(colB); c.Add(grid);
                        break;
                    }
                case 4:
                    {
                        c.Add(Row("Подсказки клавиш на экране", "Панель с управлением в правом верхнем углу.", new UiToggle(S.keyHints, v => { S.keyHints = v; GameConfig.Commit(); })));
                        c.Add(Row("Масштаб интерфейса", "Размер меню, подсказок и карточки задачи.",
                            new UiSlider(0.8f, 1.3f, S.uiScale, 0.05f, v => Mathf.RoundToInt(v * 100) + "%", v => { S.uiScale = v; }, () => { GameConfig.Commit(); })));
                        c.Add(Row("Показывать FPS", "Счётчик кадров вверху экрана.", new UiToggle(S.showFps, v => { S.showFps = v; GameConfig.Commit(); })));
                        break;
                    }
            }
            var pad = K.Box(); pad.style.height = 16f; pad.pickingMode = PickingMode.Ignore; c.Add(pad);
            settingsScroll.Apply();
        }

        VisualElement Row(string title, string desc, VisualElement control)
        {
            var r = K.Box(true); r.style.alignItems = Align.Center; K.Pad(r, 16f, 6f, 16f, 4f); r.pickingMode = PickingMode.Ignore;
            r.style.borderBottomWidth = 1f; r.style.borderBottomColor = new Color(Line.r, Line.g, Line.b, 0.55f);
            var left = K.Box(); K.Grow(left); left.pickingMode = PickingMode.Ignore; left.style.marginRight = 30f;
            left.Add(K.B(title, 19f, Text));
            if (!string.IsNullOrEmpty(desc)) { var d = K.T(desc, 14f, Muted, false, false, true); d.style.marginTop = 3f; left.Add(d); }
            r.Add(left);
            control.style.width = 420f; control.style.flexShrink = 0f;
            r.Add(control);
            return r;
        }

        VisualElement KeyRow(string[] keys, string text)
        {
            var r = K.Box(true); r.style.alignItems = Align.Center; r.style.marginTop = 10f; r.pickingMode = PickingMode.Ignore;
            var kb = K.Box(true); kb.style.width = 170f; kb.style.flexShrink = 0f; kb.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < keys.Length; i++)
            {
                if (i > 0 && (keys[0] == "Ctrl")) { var p = K.T("+", 14f, Muted); p.style.marginRight = 5f; kb.Add(p); }
                kb.Add(Keycap(keys[i], 0.9f));
            }
            r.Add(kb);
            r.Add(K.T(text, 16f, Text));
            return r;
        }

        // ======================= HUD =======================
        VisualElement BuildHud()
        {
            var layer = Layer();
            // карточка задачи
            var card = K.Box(); card.pickingMode = PickingMode.Ignore; card.style.position = Position.Absolute; card.style.left = 24f; card.style.top = 22f; card.style.width = 430f;
            hudCard = card;
            card.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.9f); K.Radius(card, 18f); Border(card, new Color(Line.r, Line.g, Line.b, 0.8f), 1f); K.Pad(card, 16f, 20f, 18f, 20f);
            var r1 = K.Box(true); r1.style.alignItems = Align.Center; r1.pickingMode = PickingMode.Ignore;
            var rank = K.Box(true); rank.pickingMode = PickingMode.Ignore; rank.style.backgroundColor = Mint; K.Radius(rank, 12f); K.Pad(rank, 3f, 12f, 3f, 12f);
            rankLabel = K.B("", 13f, Ink); rankLabel.style.letterSpacing = 1f; rank.Add(rankLabel); r1.Add(rank);
            clockChip = K.Box(true); clockChip.pickingMode = PickingMode.Ignore; clockChip.style.alignItems = Align.Center; clockChip.style.marginLeft = 8f;
            clockChip.style.backgroundColor = Well; K.Radius(clockChip, 12f); K.Pad(clockChip, 3f, 10f, 3f, 8f);
            clockChip.Add(new Icon("clock", Sky, 15f)); clockLabel = K.B("", 14f, Text); clockLabel.style.marginLeft = 5f; clockChip.Add(clockLabel);
            r1.Add(clockChip);
            r1.Add(K.Spacer());
            r1.Add(new Icon("coin", Sun, 22f));
            moneyLabel = K.B("", 22f, Sun); moneyLabel.style.marginLeft = 7f; r1.Add(moneyLabel);
            card.Add(r1);
            var r2 = K.Box(true); r2.style.alignItems = Align.Center; r2.style.marginTop = 14f; r2.pickingMode = PickingMode.Ignore;
            r2.Add(SectionLabel("ТЕКУЩАЯ ЗАДАЧА"));
            var chip = K.Box(true); chip.pickingMode = PickingMode.Ignore; chip.style.backgroundColor = new Color(Pink.r, Pink.g, Pink.b, 0.18f); K.Radius(chip, 6f); K.Pad(chip, 2f, 8f, 2f, 8f); chip.style.marginLeft = 10f;
            taskCode = K.B("", 12f, Pink); chip.Add(taskCode); r2.Add(chip);
            card.Add(r2);
            taskTitle = K.B("", 24f, Text); taskTitle.style.whiteSpace = WhiteSpace.Normal; taskTitle.style.marginTop = 4f; card.Add(taskTitle);
            var r3 = K.Box(true); r3.style.alignItems = Align.Center; r3.style.marginTop = 14f; r3.pickingMode = PickingMode.Ignore;
            var bar = K.Box(); bar.pickingMode = PickingMode.Ignore; K.Grow(bar); bar.style.height = 8f; bar.style.backgroundColor = Well; K.Radius(bar, 4f); bar.style.overflow = Overflow.Hidden;
            progressFill = K.Box(); progressFill.pickingMode = PickingMode.Ignore; progressFill.style.height = 8f; progressFill.style.backgroundColor = Sun; K.Radius(progressFill, 4f);
            bar.Add(progressFill); r3.Add(bar);
            progressLabel = K.T("", 14f, Muted); progressLabel.style.marginLeft = 12f; r3.Add(progressLabel);
            card.Add(r3);
            strikeRow = K.Box(true); strikeRow.style.alignItems = Align.Center; strikeRow.style.marginTop = 12f; strikeRow.pickingMode = PickingMode.Ignore;
            strikeRow.Add(new Icon("warning", Pink, 18f));
            strikeLabel = K.B("", 15f, Pink); strikeLabel.style.marginLeft = 8f; strikeRow.Add(strikeLabel);
            card.Add(strikeRow);
            satedChip = K.Box(true); satedChip.style.alignItems = Align.Center; satedChip.style.marginTop = 10f; satedChip.pickingMode = PickingMode.Ignore;
            satedChip.Add(new Icon("star", Mint, 18f)); var sl = K.B("Сытый: +10% XP за задачи", 15f, Mint); sl.style.marginLeft = 8f; satedChip.Add(sl);
            card.Add(satedChip);
            bugRow = K.Box(true); bugRow.style.alignItems = Align.Center; bugRow.style.marginTop = 12f; bugRow.pickingMode = PickingMode.Ignore;
            bugRow.Add(new Icon("bug", Pink, 20f));
            bugLabel = K.B("", 15f, Pink); bugLabel.style.marginLeft = 8f; bugRow.Add(bugLabel);
            card.Add(bugRow);
            layer.Add(card);

            // клавиши управления
            keysPanel = K.Box(); keysPanel.pickingMode = PickingMode.Ignore; keysPanel.style.position = Position.Absolute; keysPanel.style.right = 24f; keysPanel.style.top = 22f;
            keysPanel.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.78f); K.Radius(keysPanel, 16f); Border(keysPanel, new Color(Line.r, Line.g, Line.b, 0.7f), 1f); K.Pad(keysPanel, 10f, 18f, 14f, 16f);
            keysPanel.Add(HudKeys(new[] { "W", "A", "S", "D" }, "Ходить"));
            keysPanel.Add(HudKeys(new[] { "Shift" }, "Бег"));
            keysPanel.Add(HudKeys(new[] { "Space" }, "Прыжок"));
            keysPanel.Add(HudKeys(new[] { "E" }, "Действие"));
            keysPanel.Add(HudKeys(new[] { "V" }, "Камера"));
            keysPanel.Add(HudKeys(new[] { "Esc" }, "Пауза"));
            layer.Add(keysPanel);

            // подсказка действия
            promptRow = K.Box(true); promptRow.pickingMode = PickingMode.Ignore; promptRow.style.position = Position.Absolute; promptRow.style.left = 0f; promptRow.style.right = 0f;
            promptRow.style.top = Length.Percent(66f); promptRow.style.justifyContent = Justify.Center;
            var pill = K.Box(true); pill.pickingMode = PickingMode.Ignore; pill.style.alignItems = Align.Center; pill.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.93f);
            K.Radius(pill, 30f); Border(pill, new Color(Sun.r, Sun.g, Sun.b, 0.55f), 2f); K.Pad(pill, 9f, 24f, 9f, 10f);
            promptKeys = K.Box(true); promptKeys.pickingMode = PickingMode.Ignore; pill.Add(promptKeys);
            promptText = K.B("", 21f, Text); promptText.style.marginLeft = 8f; pill.Add(promptText);
            promptRow.Add(pill); layer.Add(promptRow);

            // прицел в режиме от первого лица
            crosshair = K.Box(); crosshair.pickingMode = PickingMode.Ignore; crosshair.style.position = Position.Absolute; crosshair.style.left = Length.Percent(50f); crosshair.style.top = Length.Percent(50f);
            layer.Add(crosshair);

            cursorHint = K.Box(true); cursorHint.pickingMode = PickingMode.Ignore; cursorHint.style.position = Position.Absolute; cursorHint.style.left = 0f; cursorHint.style.right = 0f; cursorHint.style.bottom = 36f;
            cursorHint.style.justifyContent = Justify.Center; cursorHint.style.alignItems = Align.Center;
            cursorHint.Add(new Icon("mouse", Muted, 20f)); var ch = K.T("Кликни в окно игры, чтобы управлять мышью", 16f, Muted); ch.style.marginLeft = 8f; cursorHint.Add(ch);
            layer.Add(cursorHint);
            return layer;
        }

        VisualElement HudKeys(string[] keys, string text)
        {
            var r = K.Box(true); r.style.alignItems = Align.Center; r.style.marginTop = 6f; r.pickingMode = PickingMode.Ignore;
            var kb = K.Box(true); kb.style.width = 158f; kb.style.flexShrink = 0f; kb.style.justifyContent = Justify.FlexEnd; kb.pickingMode = PickingMode.Ignore; kb.style.marginRight = 8f;
            foreach (var k in keys) kb.Add(Keycap(k, 0.85f));
            r.Add(kb);
            var l = K.B(text, 16f, Text); l.style.width = 92f; r.Add(l);
            return r;
        }

        // Клавиша как на клавиатуре: светлый колпачок с «толщиной» снизу
        public static VisualElement Keycap(string key, float scale = 1f)
        {
            var v = K.Box(true); v.pickingMode = PickingMode.Ignore;
            v.style.height = 36f * scale; v.style.minWidth = (key == "Space" ? 96f : 36f) * scale;
            K.Pad(v, 0f, 10f * scale, 0f, 10f * scale); K.Radius(v, 8f * scale);
            v.style.backgroundColor = Pal.Hex("F4F2FF");
            v.style.borderBottomWidth = 5f * scale; v.style.borderBottomColor = Pal.Hex("8E94D0");
            v.style.borderTopWidth = 1f; v.style.borderLeftWidth = 1f; v.style.borderRightWidth = 1f;
            v.style.borderTopColor = Color.white; v.style.borderLeftColor = Pal.Hex("D9DBF5"); v.style.borderRightColor = Pal.Hex("D9DBF5");
            v.style.justifyContent = Justify.Center; v.style.alignItems = Align.Center; v.style.marginRight = 6f * scale; v.style.flexShrink = 0f;
            v.Add(K.B(key, (key.Length > 2 ? 13f : 16f) * scale, Pal.Ink));
            return v;
        }

        // ======================= обед =======================
        VisualElement BuildLunchHud()
        {
            var layer = Layer();
            // таймер обеда сверху по центру
            var top = K.Box(true); top.pickingMode = PickingMode.Ignore; top.style.position = Position.Absolute; top.style.left = 0f; top.style.right = 0f; top.style.top = 22f;
            top.style.justifyContent = Justify.Center;
            var pill = K.Box(true); pill.pickingMode = PickingMode.Ignore; pill.style.alignItems = Align.Center; pill.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.92f);
            K.Radius(pill, 26f); Border(pill, new Color(Sun.r, Sun.g, Sun.b, 0.6f), 2f); K.Pad(pill, 8f, 22f, 8f, 16f);
            pill.Add(new Icon("clock", Sun, 26f));
            var lt = K.B("ОБЕД", 14f, Muted); lt.style.marginLeft = 10f; lt.style.letterSpacing = 1.5f; pill.Add(lt);
            lunchTimer = K.B("6:00", 34f, Text); lunchTimer.style.marginLeft = 12f; lunchTimer.style.unityFontStyleAndWeight = FontStyle.Bold; pill.Add(lunchTimer);
            top.Add(pill); layer.Add(top);

            // монеты за этот обед: «+10» рядом со счётчиком складываются в серию
            var card = K.Box(); card.pickingMode = PickingMode.Ignore; card.style.position = Position.Absolute; card.style.left = 24f; card.style.top = 22f; card.style.width = 330f;
            card.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.9f); K.Radius(card, 18f); Border(card, new Color(Line.r, Line.g, Line.b, 0.8f), 1f); K.Pad(card, 14f, 20f, 16f, 20f);
            card.Add(SectionLabel("ЗА ЭТОТ ОБЕД"));
            lunchCoinsRow = K.Box(true); lunchCoinsRow.pickingMode = PickingMode.Ignore; lunchCoinsRow.style.alignItems = Align.Center; lunchCoinsRow.style.marginTop = 6f;
            lunchCoinsRow.Add(new Icon("coin", Sun, 30f));
            lunchCoins = K.B("0", 38f, Sun); lunchCoins.style.marginLeft = 10f; lunchCoinsRow.Add(lunchCoins);
            lunchSeries = K.B("", 26f, Mint); lunchSeries.style.marginLeft = 14f; lunchCoinsRow.Add(lunchSeries);
            lunchPenalty = K.B("", 26f, Pink); lunchPenalty.style.marginLeft = 10f; lunchCoinsRow.Add(lunchPenalty);
            card.Add(lunchCoinsRow);
            lunchKills = K.T("", 16f, Muted); lunchKills.style.marginTop = 6f; card.Add(lunchKills);
            layer.Add(card);

            var hintRow = K.Box(true); hintRow.pickingMode = PickingMode.Ignore; hintRow.style.position = Position.Absolute; hintRow.style.left = 0f; hintRow.style.right = 0f; hintRow.style.bottom = 30f;
            hintRow.style.justifyContent = Justify.Center;
            var hp = K.Box(true); hp.pickingMode = PickingMode.Ignore; hp.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.85f); K.Radius(hp, 14f); K.Pad(hp, 6f, 16f, 6f, 16f);
            lunchHint = K.T("ЛКМ — удар ножом  ·  E — дверь  ·  юрист +10, бухгалтер −20", 15f, Text); hp.Add(lunchHint);
            hintRow.Add(hp); layer.Add(hintRow);
            return layer;
        }

        int shownCoins; float coinsShownAt;
        void UpdateLunchHud()
        {
            var L = g.Lunch; if (L == null) return;
            int sec = Mathf.CeilToInt(L.timeLeft);
            string t = (sec / 60) + ":" + (sec % 60).ToString("00");
            if (lunchTimer.text != t) { lunchTimer.text = t; lunchTimer.style.color = sec <= 30 ? Pink : Text; }
            // счётчик догоняет сумму, когда серия закончилась
            bool inSeries = L.series > 0;
            int target = inSeries ? L.coins - L.series : L.coins;
            if (shownCoins != target && Time.unscaledTime - coinsShownAt > 0.03f)
            {
                shownCoins += Math.Sign(target - shownCoins) * Mathf.Max(1, Mathf.Abs(target - shownCoins) / 6);
                coinsShownAt = Time.unscaledTime;
            }
            string key = shownCoins + "|" + L.series + "|" + L.kills + "|" + L.escaped + "|" + (Time.unscaledTime - L.penaltyAt < 1.6f);
            if (key != lastLunch)
            {
                lastLunch = key;
                lunchCoins.text = shownCoins.ToString();
                lunchSeries.text = inSeries ? "+" + L.series : "";
                lunchPenalty.text = Time.unscaledTime - L.penaltyAt < 1.6f ? "−" + L.penaltyShown : "";
                lunchKills.text = "Выбито " + L.kills + "  ·  убежали " + L.escaped + "  ·  ещё " + Mathf.Max(0, LunchRun.Cap - L.spawned);
            }
            // лёгкий толчок цифры серии при каждом новом «+10»
            float age = Time.unscaledTime - L.seriesAt;
            float bump = inSeries && age < 0.25f ? 1f + (0.25f - age) * 1.2f : 1f;
            lunchSeries.style.scale = new Scale(new Vector3(bump, bump, 1f));
        }

        // ======================= итоги дня =======================
        void BuildSummary()
        {
            summary.Clear();
            var r = g.LastReport; if (r == null) return;
            VisualElement wrap;
            summaryCard = CardBox(640f, out wrap);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            head.Add(new Icon("clock", Sun, 36f));
            var ht = K.B("Итоги дня", 44f, Sun, true); ht.style.marginLeft = 14f; head.Add(ht);
            summaryCard.Add(head);
            var sub = K.T(r.weekday + ", день " + r.day + " · 18:00, рабочий день окончен", 16f, Muted); sub.style.marginTop = 2f; sub.style.marginBottom = 14f; summaryCard.Add(sub);
            summaryCard.Add(StatRow("task", "Решено задач", r.tasks.ToString(), Text));
            summaryCard.Add(StatRow("star", "Опыт", "+" + r.xp + " XP", Mint));
            summaryCard.Add(StatRow("coin", "Монеты за работу", "+" + r.money, Sun));
            summaryCard.Add(StatRow("coin", "Монеты за обед", (r.lunchMoney >= 0 ? "+" : "") + r.lunchMoney + (r.kills > 0 ? "  (выбито " + r.kills + ")" : ""), Sun));
            if (r.fines > 0) summaryCard.Add(StatRow("warning", "Штрафы за простой", "−" + r.fines, Pink));
            summaryCard.Add(StatRow("monitor", "Рабочих часов", r.workHours + " из 8" + (r.idleHours > 0 ? "  (простой " + r.idleHours + ")" : ""), Text));
            summaryCard.Add(StatRow("warning", "Выговоры", r.strikes + " из " + r.limit, r.strikes > 0 ? Pink : Text));
            string note = r.truancy ? "Гена: «Сегодня ты почти ничего не сделал. Это прогул, выговор.»"
                        : r.strikeRemoved ? "Гена: «Пять дней без замечаний, снимаю один выговор.»"
                        : r.tasks >= 5 ? "Гена: «Отличный день. Так держать.»"
                        : "Гена: «Нормально. Завтра можно бодрее.»";
            var nl = K.T(note, 16f, r.truancy ? Pink : Muted, false, false, true); nl.style.marginTop = 14f; summaryCard.Add(nl);
            var row = K.Box(true); row.pickingMode = PickingMode.Ignore; row.style.marginTop = 10f;
            var next = new UiBtn("Следующий день", () => g.UiNextDay(), Sun, SunHover, SunLip, Ink, "play", g.Work.WeekdayFull + ", 9:00", 60f, false); K.Grow(next); next.style.marginRight = 10f;
            var home = new UiBtn("В меню", () => g.UiSummaryToMenu(), Ghost, GhostHover, GhostLip, Text, "home", null, 60f, false); home.style.width = 180f;
            row.Add(next); row.Add(home); summaryCard.Add(row);
            summary.Add(wrap);
        }

        // ======================= уволен =======================
        void BuildFired()
        {
            fired.Clear();
            var r = g.FiredReport; if (r == null) return;
            VisualElement wrap;
            firedCard = CardBox(640f, out wrap);
            var ht = K.B("Уволен", 56f, Pink, true); firedCard.Add(ht);
            var t = K.T("Гена вызвал тебя в переговорку: «Мы расстаёмся. Сдай пропуск и кружку.» Выговоров " + r.strikes + " из " + r.limit + ".", 17f, Text, false, false, true);
            t.style.marginTop = 4f; t.style.marginBottom = 14f; firedCard.Add(t);
            firedCard.Add(StatRow("account", "Кем был", r.weekday, Text));
            firedCard.Add(StatRow("clock", "Дней в компании", r.day.ToString(), Text));
            firedCard.Add(StatRow("task", "Решено задач", r.tasks.ToString(), Text));
            firedCard.Add(StatRow("coin", "Монет на счету", r.money.ToString(), Sun));
            firedCard.Add(StatRow("star", "Обедов и выбитых", r.lunchMoney + " / " + r.kills, Mint));
            if (r.fines > 0) firedCard.Add(StatRow("warning", "Штрафов за простой", r.fines.ToString(), Pink));
            var nl = K.T("Сохранение удалено. Начни заново и работай, а не жди обеда.", 15f, Muted, false, false, true); nl.style.marginTop = 14f; firedCard.Add(nl);
            firedCard.Add(new UiBtn("Новая игра", () => g.UiFiredNewGame(), Sun, SunHover, SunLip, Ink, "reset", null, 60f, false));
            fired.Add(wrap);
        }

        VisualElement StatRow(string icon, string name, string value, Color vc)
        {
            var r = K.Box(true); r.style.alignItems = Align.Center; K.Pad(r, 9f, 4f, 9f, 2f); r.pickingMode = PickingMode.Ignore;
            r.style.borderBottomWidth = 1f; r.style.borderBottomColor = new Color(Line.r, Line.g, Line.b, 0.5f);
            r.Add(new Icon(icon, Muted, 20f));
            var n = K.T(name, 17f, Text); n.style.marginLeft = 12f; r.Add(n);
            r.Add(K.Spacer());
            r.Add(K.B(value, 19f, vc));
            return r;
        }

        // ======================= тосты =======================
        VisualElement BuildToast()
        {
            var row = K.Box(true); row.pickingMode = PickingMode.Ignore; row.style.position = Position.Absolute; row.style.left = 0f; row.style.right = 0f; row.style.bottom = 110f;
            row.style.justifyContent = Justify.Center;
            toastPill = K.Box(true); toastPill.pickingMode = PickingMode.Ignore; toastPill.style.alignItems = Align.Center; toastPill.style.backgroundColor = Sun;
            K.Radius(toastPill, 16f); toastPill.style.borderBottomWidth = 5f; toastPill.style.borderBottomColor = SunLip; K.Pad(toastPill, 12f, 26f, 12f, 20f);
            toastPill.Add(new Icon("bell", Ink, 22f));
            toastText = K.B("", 19f, Ink); toastText.style.marginLeft = 10f; toastPill.Add(toastText);
            row.Add(toastPill); row.style.display = DisplayStyle.None;
            return row;
        }

        // ======================= кадр =======================
        public void Tick(float dt)
        {
            var m = g.CurMode;
            bool inMenu = m == GameRoot.Mode.Menu, inPause = m == GameRoot.Mode.Pause, inWalk = m == GameRoot.Mode.Walk || m == GameRoot.Mode.Dialog;
            bool inLunch = m == GameRoot.Mode.Lunch;
            if (!inMenu && !inPause) settingsOpen = false;
            if (!inMenu) newGamePage = false;

            Show(codeBg, inMenu);
            Show(dim, inPause);
            if (Show(menu, inMenu && !settingsOpen)) { RefreshMenu(); Pop(menuCard); }
            if (Show(pause, inPause && !settingsOpen)) { RefreshPause(); Pop(pauseCard); }
            if (Show(settings, (inMenu || inPause) && settingsOpen)) Pop(settingsPanel);
            Show(hud, inWalk || inLunch);
            Show(lunchHud, inLunch);
            if (Show(summary, m == GameRoot.Mode.DaySummary)) { BuildSummary(); if (summaryCard != null) Pop(summaryCard); }
            if (Show(fired, m == GameRoot.Mode.Fired)) { BuildFired(); if (firedCard != null) Pop(firedCard); }
            hudCard.style.display = inLunch ? DisplayStyle.None : DisplayStyle.Flex;
            keysPanel.style.display = GameConfig.S.keyHints && !inLunch ? DisplayStyle.Flex : DisplayStyle.None;

            if (inMenu) TickCode(dt);
            if (inWalk || inLunch) UpdateHud();
            if (inLunch) UpdateLunchHud();
            UpdateToast(m == GameRoot.Mode.Ide || m == GameRoot.Mode.Transition);
            AnimatePops();
            UpdateFps(dt);
        }

        // Esc: закрыть настройки или страницу новой игры. true — Esc обработан интерфейсом
        public bool Back()
        {
            if (settingsOpen) { CloseSettings(); return true; }
            if (newGamePage) { CloseNewGame(); return true; }
            return false;
        }

        public bool SettingsOpen { get { return settingsOpen; } }

        // Показать/спрятать слой. true — слой только что появился
        bool Show(VisualElement v, bool on)
        {
            bool was; visible.TryGetValue(v, out was);
            if (was == on) return false;
            visible[v] = on;
            v.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            return on;
        }

        // Мягкое появление карточки: снизу вверх и из прозрачности
        void Pop(VisualElement card) { shownAt[card] = Time.unscaledTime; ApplyPop(card, 0f); }
        void ApplyPop(VisualElement card, float k)
        {
            var target = card.parent ?? card;
            target.style.opacity = k;
            target.style.translate = new Translate(0f, (1f - k) * 18f);
        }
        void AnimatePops()
        {
            if (shownAt.Count == 0) return;
            var done = new List<VisualElement>();
            foreach (var kv in shownAt)
            {
                float k = Mathf.Clamp01((Time.unscaledTime - kv.Value) / 0.22f);
                k = 1f - (1f - k) * (1f - k);
                ApplyPop(kv.Key, k);
                if (k >= 1f) done.Add(kv.Key);
            }
            foreach (var d in done) shownAt.Remove(d);
        }

        void TickCode(float dt)
        {
            foreach (var c in columns)
            {
                float h = c.a.layout.height;
                if (float.IsNaN(h) || h < 10f) continue;
                c.y += c.speed * dt;
                if (c.y >= h) c.y -= h;
                c.holder.style.translate = new Translate(0f, c.y - h);
            }
        }

        void UpdateHud()
        {
            var t = g.CurrentTaskPublic;
            bool sprintDone = g.PathComplete;
            var w = g.Work;
            string clock = w != null ? w.Clock : "";
            bool sated = w != null && w.Sated;
            int strikes = w != null ? w.Strikes : 0;
            string key = g.RankFull + "|" + g.Save.money + "|" + (t != null ? t.id : "") + "|" + g.DoneCount + "|" + g.BugCount + "|" + sprintDone + "|" + clock + "|" + sated + "|" + strikes;
            if (key != lastHud)
            {
                lastHud = key;
                rankLabel.text = g.RankFull.ToUpperInvariant();
                moneyLabel.text = g.Save.money.ToString();
                taskCode.text = t != null && !sprintDone ? g.TaskCodeOf(t) : "ГОТОВО";
                taskTitle.text = t != null && !sprintDone ? K.Esc(t.title) : "Направление пройдено!";
                float p = g.TotalCount > 0 ? (float)g.DoneCount / g.TotalCount : 0f;
                progressFill.style.width = Length.Percent(p * 100f);
                progressLabel.text = g.DoneCount + " / " + g.TotalCount;
                bugRow.style.display = g.BugCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                bugLabel.text = "Багов в офисе: " + g.BugCount + " — поймай их!";
                clockLabel.text = clock;
                strikeRow.style.display = strikes > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (w != null) strikeLabel.text = "Выговоры: " + strikes + " из " + w.StrikeLimit + (strikes == w.StrikeLimit - 1 ? " — ещё один, и увольнение" : "");
                satedChip.style.display = sated ? DisplayStyle.Flex : DisplayStyle.None;
            }
            // подсказка действия: «[E] Сесть за компьютер» → клавиша + текст
            string pr = g.FocusPrompt;
            if (pr == null) promptRow.style.display = DisplayStyle.None;
            else
            {
                promptRow.style.display = DisplayStyle.Flex;
                string k = "E", text = pr;
                if (pr.StartsWith("[")) { int e = pr.IndexOf(']'); if (e > 1) { k = pr.Substring(1, e - 1); text = pr.Substring(e + 1).Trim(); } }
                if (promptText.text != text)
                {
                    promptText.text = text; promptKeys.Clear();
                    promptKeys.Add(Keycap(k == "ЛКМ" || k == "Клик" ? "ЛКМ" : k, 1f));
                }
            }
            bool walking = g.CurMode == GameRoot.Mode.Walk || g.CurMode == GameRoot.Mode.Lunch;
            bool fp = g.FirstPerson && walking;
            crosshair.style.display = fp ? DisplayStyle.Flex : DisplayStyle.None;
            if (fp)
            {
                float s = pr != null ? 12f : 7f;
                crosshair.style.width = s; crosshair.style.height = s; crosshair.style.marginLeft = -s / 2f; crosshair.style.marginTop = -s / 2f;
                K.Radius(crosshair, s / 2f); crosshair.style.backgroundColor = pr != null ? Sun : new Color(1f, 1f, 1f, 0.85f);
            }
            cursorHint.style.display = UnityEngine.Cursor.lockState != CursorLockMode.Locked && walking ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void UpdateToast(bool hide)
        {
            string t = hide ? null : g.ToastText;
            if (t == null) { toastRow.style.display = DisplayStyle.None; return; }
            toastRow.style.display = DisplayStyle.Flex;
            if (toastText.text != t)
            {
                toastText.text = K.Esc(t);
                var ic = toastPill.Q<Icon>();
                if (ic != null) ic.Set(t.StartsWith("Задача сдана") ? "coin" : t.StartsWith("Новая задача") ? "task" : t.StartsWith("ПОВЫШЕНИЕ") || t.StartsWith("НАПРАВЛЕНИЕ") || t.StartsWith("ОТКРЫТ") ? "kodzilla" : t.StartsWith("Тема закрыта") ? "check" : "bell", Ink);
            }
            float age = g.ToastAge, left = g.ToastLeft;
            float k = Mathf.Clamp01(age / 0.18f) * Mathf.Clamp01(left / 0.25f);
            toastRow.style.opacity = k;
            toastRow.style.translate = new Translate(0f, (1f - Mathf.Clamp01(age / 0.18f)) * 14f);
        }

        void UpdateFps(float dt)
        {
            if (!GameConfig.S.showFps) return;
            fpsFrames++; fpsTime += Time.unscaledDeltaTime;
            if (fpsTime >= 0.5f) { fpsLabel.text = "FPS " + Mathf.RoundToInt(fpsFrames / fpsTime); fpsFrames = 0; fpsTime = 0f; }
        }

        void OnSettingsChanged()
        {
            var S = GameConfig.S;
            float sc = Mathf.Clamp(S.uiScale, 0.7f, 1.5f);
            ps.referenceResolution = new Vector2Int(Mathf.RoundToInt(1920f / sc), Mathf.RoundToInt(1080f / sc));
            if (keysPanel != null) keysPanel.style.display = S.keyHints ? DisplayStyle.Flex : DisplayStyle.None;
            Gore.Enabled = S.blood;
            if (fpsLabel != null) { fpsLabel.style.display = S.showFps ? DisplayStyle.Flex : DisplayStyle.None; if (!S.showFps) fpsLabel.text = ""; }
        }

        // ======================= текстуры для фона =======================
        static Texture2D Radial(int n, Color inner, Color outer, float power)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / 1.2f);
                    px[y * n + x] = Color.Lerp(inner, outer, Mathf.Pow(d, power));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D Vertical(Color top, Color bottom)
        {
            const int n = 64;
            var t = new Texture2D(1, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++) { float k = (float)y / (n - 1); var c = Color.Lerp(bottom, top, k * k * (3 - 2 * k)); t.SetPixel(0, y, c); }
            t.Apply();
            return t;
        }
    }

    // ======================= элементы управления =======================

    // Крупная кнопка игры: цветная, с «толщиной» снизу, иконкой, подписью и стрелкой
    public class UiBtn : VisualElement
    {
        public Action Click;
        readonly Color bg, hover;
        readonly Label title, sub;
        bool over, down, enabled = true;

        public UiBtn(string text, Action click, Color bg, Color hover, Color lip, Color fg, string icon = null, string subtitle = null, float height = 60f, bool chevron = true, bool center = false)
        {
            Click = click; this.bg = bg; this.hover = hover;
            style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center; style.height = height; style.flexShrink = 0f;
            K.Pad(this, 0f, 20f, 0f, 20f); K.Radius(this, 14f); style.marginTop = 10f;
            style.borderBottomWidth = 5f; style.borderBottomColor = lip;
            if (center) style.justifyContent = Justify.Center;
            if (icon != null) { var ic = new Icon(icon, fg, 22f); ic.style.marginRight = 14f; Add(ic); }
            var col = K.Box(); col.pickingMode = PickingMode.Ignore; if (!center) K.Grow(col);
            title = K.B(text, 20f, fg); col.Add(title);
            if (subtitle != null) { sub = K.T(subtitle, 14f, new Color(fg.r, fg.g, fg.b, 0.72f)); sub.style.marginTop = 2f; col.Add(sub); }
            Add(col);
            if (chevron && !center) Add(new Icon("chevR", new Color(fg.r, fg.g, fg.b, 0.55f), 20f));
            pickingMode = PickingMode.Position;
            RegisterCallback<PointerEnterEvent>(e => { over = true; Paint(); });
            RegisterCallback<PointerLeaveEvent>(e => { over = false; down = false; Paint(); });
            RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) { down = true; Paint(); } });
            RegisterCallback<PointerUpEvent>(e => { down = false; Paint(); });
            RegisterCallback<ClickEvent>(e => { if (enabled && Click != null) Click(); });
            Paint();
        }

        public void SetTitle(string t) { title.text = t; }
        public bool Enabled { get { return enabled; } set { enabled = value; style.opacity = value ? 1f : 0.45f; Paint(); } }

        void Paint()
        {
            style.backgroundColor = over && enabled ? hover : bg;
            style.translate = new Translate(0f, down && enabled ? 3f : 0f);
            style.borderBottomWidth = down && enabled ? 2f : 5f;
        }
    }

    // Выбор из списка: ‹ значение ›, точки показывают позицию
    public class UiSelect : VisualElement
    {
        readonly string[] opts; int idx; readonly Action<int> changed;
        readonly Label val; readonly VisualElement dots;
        bool enabled = true;

        public UiSelect(string[] options, int index, Action<int> onChange)
        {
            opts = options; idx = Mathf.Clamp(index, 0, options.Length - 1); changed = onChange;
            style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center; style.height = 50f;
            style.backgroundColor = GameUi.Well; K.Radius(this, 12f); K.Line(this, GameUi.Line, 1f, 1f, 1f, 1f);
            Add(Arrow(-1));
            var mid = K.Box(); K.Grow(mid); mid.style.alignItems = Align.Center; mid.style.justifyContent = Justify.Center; mid.style.alignSelf = Align.Stretch;
            val = K.B("", 17f, GameUi.Text); mid.Add(val);
            dots = K.Box(true); dots.pickingMode = PickingMode.Ignore; dots.style.marginTop = 4f; mid.Add(dots);
            mid.RegisterCallback<ClickEvent>(e => Step(1));
            Add(mid);
            Add(Arrow(1));
            Paint();
        }

        public bool Enabled { get { return enabled; } set { enabled = value; style.opacity = value ? 1f : 0.4f; } }

        VisualElement Arrow(int dir)
        {
            var b = K.Box(true); b.style.width = 48f; b.style.alignSelf = Align.Stretch; b.style.justifyContent = Justify.Center; b.style.alignItems = Align.Center; K.Radius(b, 11f);
            b.Add(new Icon(dir < 0 ? "chevL" : "chevR", GameUi.Text, 22f));
            b.RegisterCallback<PointerEnterEvent>(e => { if (enabled) b.style.backgroundColor = GameUi.IndigoLip; });
            b.RegisterCallback<PointerLeaveEvent>(e => b.style.backgroundColor = Color.clear);
            b.RegisterCallback<ClickEvent>(e => Step(dir));
            return b;
        }

        void Step(int d)
        {
            if (!enabled || opts.Length == 0) return;
            idx = (idx + d + opts.Length) % opts.Length; Paint();
            if (changed != null) changed(idx);
        }

        void Paint()
        {
            val.text = opts.Length > 0 ? opts[idx] : "";
            dots.Clear();
            if (opts.Length > 1 && opts.Length <= 8)
                for (int i = 0; i < opts.Length; i++)
                {
                    var d = K.Box(); d.pickingMode = PickingMode.Ignore; d.style.width = i == idx ? 14f : 5f; d.style.height = 5f; K.Radius(d, 2.5f); d.style.marginLeft = 2f; d.style.marginRight = 2f;
                    d.style.backgroundColor = i == idx ? GameUi.Sun : new Color(1f, 1f, 1f, 0.22f); dots.Add(d);
                }
        }
    }

    // Ползунок: тянется мышью, значение справа
    public class UiSlider : VisualElement
    {
        readonly float min, max, step; float v;
        readonly Func<float, string> fmt; readonly Action<float> changed; readonly Action released;
        readonly VisualElement area, fill, knob; readonly Label val;
        bool drag;

        public UiSlider(float min, float max, float value, float step, Func<float, string> format, Action<float> onChange, Action onRelease = null)
        {
            this.min = min; this.max = max; this.step = step; v = Mathf.Clamp(value, min, max); fmt = format; changed = onChange; released = onRelease;
            style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center; style.height = 50f;
            area = K.Box(); K.Grow(area); area.style.alignSelf = Align.Stretch; area.style.justifyContent = Justify.Center;
            var track = K.Box(); track.pickingMode = PickingMode.Ignore; track.style.height = 10f; track.style.marginLeft = 13f; track.style.marginRight = 13f;
            track.style.backgroundColor = GameUi.Well; K.Radius(track, 5f); K.Line(track, GameUi.Line, 1f, 1f, 1f, 1f);
            fill = K.Box(); fill.pickingMode = PickingMode.Ignore; fill.style.position = Position.Absolute; fill.style.left = 0f; fill.style.top = 0f; fill.style.bottom = 0f;
            fill.style.backgroundColor = GameUi.Sun; K.Radius(fill, 5f); track.Add(fill);
            knob = K.Box(); knob.pickingMode = PickingMode.Ignore; knob.style.position = Position.Absolute; knob.style.width = 26f; knob.style.height = 26f; knob.style.top = -9f; knob.style.marginLeft = -13f;
            knob.style.backgroundColor = Color.white; K.Radius(knob, 13f); K.Line(knob, GameUi.Sun, 4f, 4f, 4f, 4f); track.Add(knob);
            area.Add(track);
            Add(area);
            val = K.B("", 17f, GameUi.Text); val.style.width = 74f; val.style.unityTextAlign = TextAnchor.MiddleRight; Add(val);
            area.RegisterCallback<PointerDownEvent>(e => { if (e.button != 0) return; drag = true; area.CapturePointer(e.pointerId); SetX(e.localPosition.x); e.StopPropagation(); });
            area.RegisterCallback<PointerMoveEvent>(e => { if (drag && area.HasPointerCapture(e.pointerId)) SetX(e.localPosition.x); });
            area.RegisterCallback<PointerUpEvent>(e => { if (!drag) return; drag = false; area.ReleasePointer(e.pointerId); if (released != null) released(); });
            Paint();
        }

        void SetX(float x)
        {
            float w = area.layout.width - 26f; if (w <= 1f) return;
            float t = Mathf.Clamp01((x - 13f) / w);
            float nv = min + t * (max - min);
            if (step > 0f) nv = Mathf.Round(nv / step) * step;
            nv = Mathf.Clamp(nv, min, max);
            if (Mathf.Approximately(nv, v)) return;
            v = nv; Paint();
            if (changed != null) changed(v);
        }

        void Paint()
        {
            float t = max > min ? (v - min) / (max - min) : 0f;
            fill.style.width = Length.Percent(t * 100f);
            knob.style.left = Length.Percent(t * 100f);
            val.text = fmt != null ? fmt(v) : v.ToString("0.##");
        }
    }

    // Переключатель вкл/выкл
    public class UiToggle : VisualElement
    {
        bool on; readonly Action<bool> changed; readonly VisualElement knob, box; readonly Label state;

        public UiToggle(bool value, Action<bool> onChange)
        {
            on = value; changed = onChange;
            style.flexDirection = FlexDirection.Row; style.alignItems = Align.Center; style.justifyContent = Justify.FlexEnd; style.height = 50f;
            state = K.B("", 16f, GameUi.Muted); state.style.marginRight = 14f; Add(state);
            box = K.Box(); box.style.width = 62f; box.style.height = 34f; K.Radius(box, 17f); K.Line(box, GameUi.Line, 1f, 1f, 1f, 1f);
            knob = K.Box(); knob.pickingMode = PickingMode.Ignore; knob.style.position = Position.Absolute; knob.style.width = 26f; knob.style.height = 26f; knob.style.top = 3f; K.Radius(knob, 13f);
            box.Add(knob); Add(box);
            RegisterCallback<ClickEvent>(e => { on = !on; Paint(); if (changed != null) changed(on); });
            Paint();
        }

        void Paint()
        {
            box.style.backgroundColor = on ? GameUi.Mint : GameUi.Well;
            knob.style.left = on ? 31f : 3f;
            knob.style.backgroundColor = on ? Color.white : GameUi.Muted;
            state.text = on ? "Вкл" : "Выкл";
            state.style.color = on ? GameUi.Mint : GameUi.Muted;
        }
    }
}
