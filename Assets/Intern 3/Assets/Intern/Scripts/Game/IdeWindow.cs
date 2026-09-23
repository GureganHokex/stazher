// Игровая IDE: тикет, редактор, отладчик, объяснения, консоль, проверка.
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Intern.Py;

namespace Intern.Game
{
    public class IdeWindow
    {
        readonly GameRoot g;
        UiKit S { get { return g.Ui; } }

        public TaskData Task;
        string code = "";
        Vector2 editorScroll, leftScroll, rightScroll, consoleScroll, listScroll;
        string consoleText = "";
        List<CheckResult> lastCheck;
        DebugSession dbg;
        HashSet<int> bps = new HashSet<int>();
        int errorLine = -1;
        bool showList, showTheory = true, solutionShown, confirmReset;
        float confirmResetUntil;

        // объяснения
        List<KeyValuePair<int, string>> explains = new List<KeyValuePair<int, string>>();
        PyError lint;
        string explainedCode = null;
        float lastEditTime;

        // по задачам
        readonly Dictionary<string, int> hintsShown = new Dictionary<string, int>();
        readonly Dictionary<string, int> failedChecks = new Dictionary<string, int>();
        readonly Dictionary<string, float> timeSpent = new Dictionary<string, float>();
        readonly HashSet<string> usedSolution = new HashSet<string>();

        public IdeWindow(GameRoot g) { this.g = g; }

        Difficulty Diff { get { return (Difficulty)g.Save.difficulty; } }
        bool ExplainPanel { get { return Diff == Difficulty.Easy || g.Save.hasMonitor; } }
        bool RuErrors { get { return Diff != Difficulty.Hard || g.Save.hasDuck; } }

        public void Open(TaskData t)
        {
            if (Task != null) SaveCode();
            StopDebug();
            Task = t;
            code = g.Save.GetCode(t.id) ?? t.starter;
            consoleText = ""; lastCheck = null; errorLine = -1; explainedCode = null; solutionShown = usedSolution.Contains(t.id);
            bps.Clear(); showList = false;
            editorScroll = leftScroll = rightScroll = consoleScroll = Vector2.zero;
        }

        public void Close() { SaveCode(); StopDebug(); }

        void SaveCode() { if (Task != null) { g.Save.SetCode(Task.id, code); g.Persist(); } }

        void StopDebug() { if (dbg != null) { dbg.Stop(); dbg = null; } }

        public void Tick(float dt)
        {
            if (Task == null) return;
            float t; timeSpent.TryGetValue(Task.id, out t); timeSpent[Task.id] = t + dt;
        }

        int HintsShown { get { int v; return hintsShown.TryGetValue(Task.id, out v) ? v : 0; } }
        int Fails { get { int v; return failedChecks.TryGetValue(Task.id, out v) ? v : 0; } }
        float Spent { get { float v; return timeSpent.TryGetValue(Task.id, out v) ? v : 0; } }
        bool Late { get { return Diff == Difficulty.Hard && Spent > Task.deadline; } }

        // ======================= Отрисовка =======================
        public void Draw(float W, float H)
        {
            if (Task == null) return;
            S.Fill(new Rect(0, 0, W, H), Pal.Ink);
            float top = 58, lw = Mathf.Round(W * 0.27f), rw = Mathf.Round(W * 0.26f), cw = W - lw - rw;

            DrawTopBar(new Rect(0, 0, W, top));
            DrawLeft(Inset(new Rect(0, top, lw, H - top)));
            DrawCenter(Inset(new Rect(lw, top, cw, H - top)));
            DrawRight(Inset(new Rect(lw + cw, top, rw, H - top)));
            if (showList) DrawTaskList(W, H);
        }

        static Rect Inset(Rect r) { return new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 10); }

        void DrawTopBar(Rect r)
        {
            S.Fill(r, Pal.Panel);
            GUI.Label(new Rect(16, 8, r.width * 0.5f, 26), Task.chapter, S.small);
            GUI.Label(new Rect(16, 24, r.width * 0.5f, 32), Task.title + (Task.isBugHunt ? "   <color=#FF4F9A>[баг-хант]</color>" : ""), S.h2);

            float x = r.width - 16;
            x -= 130; if (GUI.Button(new Rect(x, 11, 130, 36), "Выйти (Esc)", S.btnAlt)) g.CloseIde();
            x -= 150; if (GUI.Button(new Rect(x, 11, 140, 36), showList ? "Скрыть задачи" : "Все задачи", S.btnAlt)) showList = !showList;
            x -= 10;
            string info = "Монеты: " + g.Save.money + "     Ранг: " + g.RankName + "     Сложность: " + Progress.DifficultyName(Diff);
            if (Diff == Difficulty.Hard)
            {
                float left = Task.deadline - Spent;
                string timer = left > 0 ? string.Format("{0}:{1:00}", (int)(left / 60), (int)(left % 60)) : "сорван";
                info = "<color=" + (left > 30 ? "#FFD23F" : "#FF4F9A") + ">Дедлайн: " + timer + "</color>     " + info;
            }
            var st = new GUIStyle(S.body) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            GUI.Label(new Rect(r.width * 0.35f, 11, x - r.width * 0.35f - 10, 36), info, st);
        }

        void DrawLeft(Rect r)
        {
            GUI.Box(r, GUIContent.none, S.panel);
            var inner = new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24);
            GUILayout.BeginArea(inner);
            leftScroll = GUILayout.BeginScrollView(leftScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            GUILayout.Label("<b>" + UiKit.Esc(Task.sender) + "</b>", S.h3);
            GUILayout.Label(UiKit.Esc(Task.story), S.body);
            GUILayout.Space(10);
            GUILayout.Label("Задача", S.h3);
            GUILayout.Label("<b>" + UiKit.Esc(Task.goal) + "</b>", S.body);
            GUILayout.Space(10);

            // Теория
            if (Diff == Difficulty.Hard)
                GUILayout.Label("Теория скрыта на тяжёлой сложности. Вспоминай или гугли — как на настоящей работе.", S.small);
            else
            {
                if (GUILayout.Button(showTheory ? "Теория (скрыть)" : "Теория (показать)", S.btnGhost, GUILayout.ExpandWidth(false))) showTheory = !showTheory;
                if (showTheory) GUILayout.Label(UiKit.Esc(Task.theory), S.body);
            }
            GUILayout.Space(10);

            // Подсказки
            int shown = HintsShown;
            for (int i = 0; i < shown && i < Task.hints.Length; i++)
                GUILayout.Label("<color=#9FD8FF>Подсказка " + (i + 1) + ":</color> " + UiKit.Esc(Task.hints[i]), S.body);
            if (shown < Task.hints.Length)
            {
                if (Diff == Difficulty.Easy)
                {
                    if (GUILayout.Button("Подсказка (" + (shown + 1) + " из " + Task.hints.Length + ")", S.btnAlt, GUILayout.ExpandWidth(false))) hintsShown[Task.id] = shown + 1;
                }
                else if (Diff == Difficulty.Medium)
                {
                    GUI.enabled = g.Save.money >= 30;
                    if (GUILayout.Button("Купить подсказку — 30 монет", S.btnAlt, GUILayout.ExpandWidth(false)))
                    { g.Save.money -= 30; hintsShown[Task.id] = shown + 1; g.Persist(); }
                    GUI.enabled = true;
                }
                else GUILayout.Label("Подсказок на тяжёлой сложности нет.", S.small);
            }

            // Решение (только лёгкая, после 3 неудачных проверок)
            if (Diff == Difficulty.Easy && Task.solution != null)
            {
                if (solutionShown)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("Эталонное решение", S.h3);
                    GUILayout.Label(UiKit.Esc(Task.solution), new GUIStyle(S.console) { normal = { textColor = Pal.Mint } });
                    GUILayout.Label("Перепиши его руками, а не копируй: так код лучше запоминается.", S.small);
                }
                else if (Fails >= 3)
                {
                    if (GUILayout.Button("Показать решение (награда ×0.5)", S.btnDanger, GUILayout.ExpandWidth(false)))
                    { solutionShown = true; usedSolution.Add(Task.id); }
                }
            }

            // Результаты проверки
            if (lastCheck != null)
            {
                GUILayout.Space(12);
                GUILayout.Label("Результаты проверки", S.h3);
                for (int i = 0; i < lastCheck.Count; i++)
                {
                    var c = lastCheck[i];
                    GUILayout.Label((c.Passed ? "<color=#7BE0B5>[+] Тест " : "<color=#FF4F9A>[x] Тест ") + (i + 1) + "</color>   ввод: " + UiKit.Esc(c.InputsText), S.body);
                    if (!c.Passed)
                    {
                        if (Diff != Difficulty.Hard)
                        {
                            GUILayout.Label("ожидалось:\n" + UiKit.Esc(c.Expected), S.console);
                            GUILayout.Label("получилось:\n" + UiKit.Esc(c.Actual.Length == 0 ? "(пусто)" : c.Actual), S.console);
                        }
                        if (!string.IsNullOrEmpty(c.Note)) GUILayout.Label(UiKit.Esc(c.Note), S.small);
                        break; // достаточно первого упавшего теста
                    }
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawCenter(Rect r)
        {
            float tb = 52, consoleH = Mathf.Min(230, r.height * 0.3f);
            var bar = new Rect(r.x, r.y, r.width, tb);
            GUI.Box(bar, GUIContent.none, S.panelLight);
            DrawToolbar(bar);

            var ed = new Rect(r.x, r.y + tb + 8, r.width, r.height - tb - consoleH - 16);
            if (dbg != null) DrawDebugView(ed); else DrawEditor(ed);

            var con = new Rect(r.x, r.yMax - consoleH, r.width, consoleH);
            DrawConsole(con);
        }

        void DrawToolbar(Rect r)
        {
            float x = r.x + 10, y = r.y + 6, h = 40;
            System.Func<string, GUIStyle, float, bool> B = (label, style, w) =>
            {
                bool res = GUI.Button(new Rect(x, y, w, h), label, style); x += w + 8; return res;
            };
            if (dbg == null)
            {
                if (B("Запустить", S.btnAlt, 110)) RunOnce();
                if (B("Отладка", S.btnAlt, 100)) StartDebug();
                if (B("Проверить", S.btn, 120)) CheckTask();
                if (confirmReset && Time.unscaledTime > confirmResetUntil) confirmReset = false;
                if (B(confirmReset ? "Точно сбросить?" : "Сбросить код", confirmReset ? S.btnDanger : S.btnGhost, 150))
                {
                    if (confirmReset) { code = Task.starter; confirmReset = false; SaveCode(); errorLine = -1; }
                    else { confirmReset = true; confirmResetUntil = Time.unscaledTime + 3; }
                }
                GUI.Label(new Rect(x + 4, y + 8, r.xMax - x - 10, 24), "Tab — отступ.  Клик по номеру строки — точка остановки.", S.small);
            }
            else
            {
                bool paused = dbg.Paused, finished = dbg.Finished;
                GUI.enabled = paused;
                if (B("Шаг", S.btn, 80)) dbg.StepInto();
                if (B("Шаг без захода в функции", S.btnAlt, 230)) dbg.StepOver();
                if (B("Продолжить", S.btnAlt, 120)) dbg.Continue();
                GUI.enabled = true;
                if (B(finished ? "Закрыть отладку" : "Стоп", S.btnDanger, finished ? 160 : 80)) { StopDebug(); }
                string state = finished ? (dbg.Error != null ? "Программа упала с ошибкой" : "Программа завершилась")
                             : paused ? "Пауза перед строкой " + dbg.Line : "Выполняется...";
                GUI.Label(new Rect(x + 4, y + 8, r.xMax - x - 10, 24), state, S.small);
            }
        }

        // ----- Редактор -----
        void DrawEditor(Rect r)
        {
            GUI.Box(r, GUIContent.none, S.editorBg);
            float gutter = 48;
            float lh = S.code.lineHeight > 1 ? S.code.lineHeight : 19f;
            var lines = code.Split('\n');
            float textW = Mathf.Max(r.width - gutter - 20, S.code.CalcSize(new GUIContent(LongestLine(lines))).x + 30);
            float contentH = Mathf.Max(r.height - 4, S.code.padding.vertical + lines.Length * lh + 40);

            editorScroll = GUI.BeginScrollView(r, editorScroll, new Rect(0, 0, gutter + textW, contentH));
            // подсветка строки с ошибкой
            if (errorLine > 0 && errorLine <= lines.Length)
                S.Fill(new Rect(gutter, S.code.padding.top + (errorLine - 1) * lh, textW, lh), new Color(1f, 0.31f, 0.6f, 0.18f));
            // номера строк и точки остановки
            for (int i = 0; i < lines.Length; i++)
            {
                var lr = new Rect(0, S.code.padding.top + i * lh, gutter, lh);
                if (bps.Contains(i + 1)) S.Fill(new Rect(6, lr.y + lh * 0.25f, lh * 0.5f, lh * 0.5f), Pal.Pink);
                if (GUI.Button(lr, (i + 1).ToString(), S.lineNo)) { if (!bps.Remove(i + 1)) bps.Add(i + 1); }
            }
            HandleEditorKeys();
            GUI.SetNextControlName("code");
            string newCode = GUI.TextArea(new Rect(gutter, 0, textW, contentH), code, S.code);
            if (newCode != code) { code = newCode; lastEditTime = Time.unscaledTime; errorLine = -1; }
            GUI.EndScrollView();
        }

        static string LongestLine(string[] lines)
        {
            string best = ""; foreach (var l in lines) if (l.Length > best.Length) best = l; return best;
        }

        void HandleEditorKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || GUI.GetNameOfFocusedControl() != "code") return;
            var te = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
            if (te == null) return;
            if (e.keyCode == KeyCode.Tab)
            {
                te.text = code; te.ReplaceSelection("    "); code = te.text; lastEditTime = Time.unscaledTime; e.Use();
            }
            else if (e.character == '\t') e.Use();
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                te.text = code;
                int cur = Mathf.Clamp(te.cursorIndex, 0, code.Length);
                int ls = code.LastIndexOf('\n', Mathf.Max(0, cur - 1)) + 1;
                if (cur == 0) ls = 0;
                string line = code.Substring(ls, cur - ls);
                string indent = new string(' ', line.Length - line.TrimStart(' ').Length);
                if (line.TrimEnd().EndsWith(":")) indent += "    ";
                te.ReplaceSelection("\n" + indent); code = te.text; lastEditTime = Time.unscaledTime; e.Use();
            }
            else if (e.character == '\n') e.Use();
        }

        // ----- Отладчик -----
        static readonly Regex Kw = new Regex(@"\b(if|elif|else|while|for|in|def|return|break|continue|pass|and|or|not|True|False|None|is)\b");
        static readonly Regex Bi = new Regex(@"\b(print|input|int|float|str|len|range|list|dict|sum|min|max|sorted|abs|round|enumerate|zip)\b(?=\()");

        static string Highlight(string line)
        {
            // простая подсветка: комментарии, строки, числа, ключевые слова
            int hash = -1; bool inStr = false; char q = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inStr) { if (c == q) inStr = false; }
                else if (c == '"' || c == '\'') { inStr = true; q = c; }
                else if (c == '#') { hash = i; break; }
            }
            string codePart = hash >= 0 ? line.Substring(0, hash) : line;
            string comment = hash >= 0 ? line.Substring(hash) : "";
            var sb = new StringBuilder();
            var strRx = new Regex("(\"[^\"]*\"?|'[^']*'?)");
            var parts = strRx.Split(codePart);
            foreach (var p in parts)
            {
                if (p.Length > 0 && (p[0] == '"' || p[0] == '\'')) { sb.Append("<color=#FFB86B>").Append(UiKit.Esc(p)).Append("</color>"); continue; }
                string s = UiKit.Esc(p);
                s = Regex.Replace(s, @"\b(\d+(\.\d+)?)\b", "<color=#BD93F9>$1</color>");
                s = Kw.Replace(s, "<color=#FF79C6>$1</color>");
                s = Bi.Replace(s, "<color=#8BE9FD>$1</color>");
                sb.Append(s);
            }
            if (comment.Length > 0) sb.Append("<color=#6272A4>").Append(UiKit.Esc(comment)).Append("</color>");
            return sb.ToString();
        }

        void DrawDebugView(Rect r)
        {
            GUI.Box(r, GUIContent.none, S.editorBg);
            float gutter = 48, lh = S.code.lineHeight > 1 ? S.code.lineHeight : 19f;
            var lines = code.Split('\n');
            float contentH = Mathf.Max(r.height - 4, 16 + lines.Length * lh + 40);
            float textW = Mathf.Max(r.width - gutter - 20, S.code.CalcSize(new GUIContent(LongestLine(lines))).x + 30);
            int cur = dbg.Paused || dbg.Finished ? dbg.Line : -1;
            editorScroll = GUI.BeginScrollView(r, editorScroll, new Rect(0, 0, gutter + textW, contentH));
            for (int i = 0; i < lines.Length; i++)
            {
                var y = 8 + i * lh;
                if (i + 1 == cur) S.Fill(new Rect(0, y, gutter + textW, lh), dbg.Error != null ? new Color(1f, 0.31f, 0.6f, 0.3f) : new Color(1f, 0.82f, 0.25f, 0.25f));
                if (bps.Contains(i + 1)) S.Fill(new Rect(6, y + lh * 0.25f, lh * 0.5f, lh * 0.5f), Pal.Pink);
                if (GUI.Button(new Rect(0, y, gutter, lh), (i + 1).ToString(), S.lineNo))
                {
                    if (!bps.Remove(i + 1)) bps.Add(i + 1);
                    dbg.Breakpoints = new HashSet<int>(bps);
                }
                GUI.Label(new Rect(gutter + 8, y, textW, lh), Highlight(lines[i]), new GUIStyle(S.codeRich) { padding = new RectOffset(0, 0, 0, 0) });
            }
            GUI.EndScrollView();
        }

        // ----- Консоль -----
        void DrawConsole(Rect r)
        {
            GUI.Box(r, GUIContent.none, S.consoleBg);
            GUI.Label(new Rect(r.x + 10, r.y + 4, 200, 20), "Консоль", S.small);
            string text = consoleText;
            if (dbg != null)
            {
                text = "<color=#9AA0D6>— отладка —</color>\n" + UiKit.Esc(dbg.Console);
                if (dbg.Finished && dbg.Error != null) text += "\n" + FormatError(dbg.Error);
                else if (dbg.Finished) text += "\n<color=#7BE0B5>Программа завершилась.</color>";
            }
            var inner = new Rect(r.x, r.y + 22, r.width, r.height - 22);
            float h = S.console.CalcHeight(new GUIContent(text), inner.width - 20) + 10;
            consoleScroll = GUI.BeginScrollView(inner, consoleScroll, new Rect(0, 0, inner.width - 20, Mathf.Max(h, inner.height)));
            GUI.Label(new Rect(0, 0, inner.width - 20, h), text, S.console);
            GUI.EndScrollView();
        }

        string FormatError(PyError e)
        {
            string head = "<color=#FF4F9A><b>" + e.PyType + "</b> в строке " + e.Line + "</color>";
            if (RuErrors) return head + "\n" + UiKit.Esc(e.Ru);
            return head + "\n<color=#9AA0D6>(Купи резиновую уточку у кофемашины — она объясняет ошибки на русском.)</color>";
        }

        // ----- Правая панель -----
        void DrawRight(Rect r)
        {
            GUI.Box(r, GUIContent.none, S.panel);
            var inner = new Rect(r.x + 14, r.y + 12, r.width - 28, r.height - 24);
            GUILayout.BeginArea(inner);
            rightScroll = GUILayout.BeginScrollView(rightScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);

            if (dbg != null) DrawDebugInfo();
            else DrawExplanations();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawDebugInfo()
        {
            GUILayout.Label("Отладчик", S.h3);
            if (!dbg.Paused && !dbg.Finished) { GUILayout.Label("Программа выполняется до следующей точки остановки...", S.body); return; }

            if (dbg.Paused)
            {
                GUILayout.Label("Сейчас выполнится строка " + dbg.Line + (dbg.FrameName != "<модуль>" ? " (внутри функции " + dbg.FrameName + ")" : ""), S.body);
                if (Diff != Difficulty.Hard || g.Save.hasMonitor)
                {
                    string why = ExplainLine(dbg.Line);
                    if (why != null) GUILayout.Label("<color=#9FD8FF>" + UiKit.Esc(why) + "</color>", S.body);
                }
            }
            var ch = dbg.Changes;
            if (ch.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label("Что изменилось на прошлом шаге", S.h3);
                foreach (var c in ch) GUILayout.Label("<color=#7BE0B5>" + UiKit.Esc(c) + "</color>", S.body);
            }
            GUILayout.Space(8);
            GUILayout.Label("Переменные", S.h3);
            var vars = dbg.Vars.Where(v => v.Type != "function").ToList();
            if (vars.Count == 0) GUILayout.Label("Пока ни одной переменной.", S.small);
            string scope = null;
            foreach (var v in vars)
            {
                if (v.Scope != scope) { scope = v.Scope; GUILayout.Label(scope, S.small); }
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>" + UiKit.Esc(v.Name) + "</b>", S.body, GUILayout.Width(110));
                GUILayout.Label(v.Type, S.tag, GUILayout.ExpandWidth(false));
                GUILayout.EndHorizontal();
                GUILayout.Label(UiKit.Esc(v.Value), S.console);
            }
            GUILayout.Space(8);
            GUILayout.Label("Шагов выполнено: " + dbg.StepCount, S.small);
        }

        string ExplainLine(int line)
        {
            RefreshExplanations(true);
            foreach (var kv in explains) if (kv.Key == line) return kv.Value;
            return null;
        }

        void RefreshExplanations(bool force)
        {
            if (explainedCode == code) return;
            if (!force && Time.unscaledTime - lastEditTime < 0.4f) return;
            explains = Explainer.ExplainProgram(code, out lint);
            explainedCode = code;
        }

        void DrawExplanations()
        {
            RefreshExplanations(false);
            if (lint != null)
            {
                GUILayout.Label("Проверка синтаксиса", S.h3);
                GUILayout.Label(FormatError(lint), S.body);
                GUILayout.Space(8);
            }
            if (!ExplainPanel)
            {
                GUILayout.Label("Разбор кода", S.h3);
                GUILayout.Label("На этой сложности построчный разбор доступен только в отладчике. Купи второй монитор у кофемашины, чтобы видеть его всегда.", S.small);
                return;
            }
            GUILayout.Label("Что делает каждая строка", S.h3);
            if (explains.Count == 0 && lint == null) GUILayout.Label("Напиши код — и здесь появится объяснение каждой строки.", S.small);
            foreach (var kv in explains)
            {
                GUILayout.Label("<color=#FFD23F>Строка " + kv.Key + "</color>", S.small);
                GUILayout.Label(UiKit.Esc(kv.Value), S.body);
                GUILayout.Space(4);
            }
        }

        // ----- Список задач -----
        void DrawTaskList(float W, float H)
        {
            var r = new Rect(W * 0.27f, 70, Mathf.Min(560, W * 0.46f), H - 140);
            GUI.Box(r, GUIContent.none, S.panelLight);
            GUILayout.BeginArea(new Rect(r.x + 16, r.y + 14, r.width - 32, r.height - 28));
            GUILayout.Label("Задачи — " + g.Tasks.language, S.h2);
            listScroll = GUILayout.BeginScrollView(listScroll);
            string chapter = null;
            for (int i = 0; i < g.Tasks.tasks.Length; i++)
            {
                var t = g.Tasks.tasks[i];
                if (t.chapter != chapter) { chapter = t.chapter; GUILayout.Space(6); GUILayout.Label(chapter, S.h3); }
                bool done = g.Save.done.Contains(t.id), open = g.IsUnlocked(i);
                string label = (done ? "[+] " : open ? "[ ] " : "[замок] ") + t.title + (t.isBugHunt ? "  (баг-хант)" : "");
                GUI.enabled = open;
                if (GUILayout.Button(label, t == Task ? S.btn : S.btnGhost)) Open(t);
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ======================= Действия =======================
        string[] SampleInputs { get { return Task.tests != null && Task.tests.Length > 0 ? Task.tests[0].inputs : new string[0]; } }

        void RunOnce()
        {
            SaveCode();
            var inputs = SampleInputs;
            var r = PyRun.Run(code, inputs);
            var sb = new StringBuilder();
            if (inputs.Length > 0) sb.Append("<color=#9AA0D6>Ввод из первого теста: " + UiKit.Esc(string.Join(" | ", inputs)) + "</color>\n");
            sb.Append(UiKit.Esc(r.Console));
            if (r.Error != null) { sb.Append("\n").Append(FormatError(r.Error)); errorLine = r.Error.Line; }
            else { sb.Append("\n<color=#7BE0B5>Готово. Шагов: " + r.Steps + ". Чтобы сдать задачу — нажми «Проверить».</color>"); errorLine = -1; }
            consoleText = sb.ToString();
            consoleScroll = new Vector2(0, 99999);
        }

        void StartDebug()
        {
            SaveCode();
            try { Parser.ParseProgram(code); }
            catch (PyError e) { consoleText = FormatError(e); errorLine = e.Line; return; }
            dbg = new DebugSession(code, SampleInputs, bps);
            dbg.Start(true);
            explainedCode = null;
        }

        void CheckTask()
        {
            SaveCode();
            lastCheck = PyRun.Check(code, Task.tests);
            leftScroll = new Vector2(0, 99999);
            int passed = lastCheck.Count(c => c.Passed);
            var firstErr = lastCheck.FirstOrDefault(c => c.Error != null);
            errorLine = firstErr != null ? firstErr.Error.Line : -1;
            if (passed == lastCheck.Count)
            {
                consoleText = "<color=#7BE0B5><b>Все тесты пройдены: " + passed + " из " + passed + "!</b></color>";
                g.CompleteTask(Task, usedSolution.Contains(Task.id), Late);
            }
            else
            {
                failedChecks[Task.id] = Fails + 1;
                consoleText = "<color=#FF4F9A><b>Пройдено " + passed + " из " + lastCheck.Count + ".</b></color> Подробности — слева." +
                              (firstErr != null ? "\n" + FormatError(firstErr.Error) : "") +
                              "\n<color=#9AA0D6>Из-за ошибки в офисе завёлся баг. Выйди и поймай его!</color>";
                g.SpawnBug();
                if (Diff == Difficulty.Easy && Fails == 3) consoleText += "\n<color=#FFD23F>Если совсем застрял — слева появилась кнопка «Показать решение».</color>";
            }
        }
    }
}
