// IDE, спринт 9: задачи без запуска — «Что выведет?», «Заполни пропуск», «Собери код», «Кликни по багу» — и
// TypeScript (типы стираются TsStrip, код идёт в Jint). Ответ набирается на вкладке «ОТВЕТ»: свои поля ввода
// с курсором (IDE рисуется в текстуру, стандартных полей UI Toolkit здесь нет — ввод приходит из OnGUI).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public partial class IdeScreen
    {
        bool IsPredict { get { return TMode == "predict"; } }
        bool IsCloze { get { return TMode == "cloze"; } }
        bool IsParsons { get { return TMode == "parsons"; } }
        bool IsTs { get { return TMode == "ts"; } }
        bool IsClickBug { get { return Task != null && Task.IsClickBug; } }
        bool IsNoRun { get { return IsPredict || IsCloze || IsParsons; } }   // ответ на вкладке «ОТВЕТ», код только для чтения

        // Код для Jint: у TypeScript сначала стираются типы (строки сохраняются — номера в ошибках совпадают)
        string JsCode(string code) { return IsTs ? TsStrip.Strip(code) : code; }

        // ---------- поля ввода ----------
        class Field { public string Text = ""; public int Cur; public bool Multi; public Label View; public VisualElement Box; public Action<string> Changed; }
        readonly List<Field> fields = new List<Field>();
        int fieldFocus = -1, fieldCur = -1;
        bool fieldBlink = true; float fieldBlinkAt;

        // состояние ответов по задачам (переживает переключение задач)
        readonly Dictionary<string, string> predictAns = new Dictionary<string, string>();
        readonly Dictionary<string, string[]> clozeAns = new Dictionary<string, string[]>();
        readonly Dictionary<string, bool[]> clozeMarks = new Dictionary<string, bool[]>();
        readonly Dictionary<string, List<int>> parsonsPool = new Dictionary<string, List<int>>();
        readonly Dictionary<string, List<KeyValuePair<int, int>>> parsonsSol = new Dictionary<string, List<KeyValuePair<int, int>>>();   // (строка, отступ)
        readonly Dictionary<string, int> bugPicks = new Dictionary<string, int>();

        int BugPick { get { int v; return Task != null && bugPicks.TryGetValue(Task.id, out v) ? v : 0; } }

        string[] ClozeVals
        {
            get
            {
                string[] v; int n = Task.blanks != null ? Task.blanks.Count : 0;
                if (!clozeAns.TryGetValue(Task.id, out v) || v.Length != n) clozeAns[Task.id] = v = new string[n];
                return v;
            }
        }

        // все строки «Собери код»: верные + лишние; перемешаны одинаково для одной задачи
        string[] ParsonsItems { get { return (Task.lines ?? new string[0]).Concat(Task.distractors ?? new string[0]).ToArray(); } }
        List<int> Pool
        {
            get
            {
                List<int> v;
                if (!parsonsPool.TryGetValue(Task.id, out v))
                {
                    int n = ParsonsItems.Length; v = Enumerable.Range(0, n).ToList();
                    var rng = new System.Random(TaskGen.Fnv(Task.id));
                    for (int i = n - 1; i > 0; i--) { int j = rng.Next(i + 1); int x = v[i]; v[i] = v[j]; v[j] = x; }
                    // перемешали, но вдруг получилось как в ответе — сдвигаем
                    if (n > 1 && v.Take(Task.lines.Length).SequenceEqual(Enumerable.Range(0, Task.lines.Length))) { v.Add(v[0]); v.RemoveAt(0); }
                    parsonsPool[Task.id] = v;
                }
                return v;
            }
        }
        List<KeyValuePair<int, int>> Sol { get { List<KeyValuePair<int, int>> v; if (!parsonsSol.TryGetValue(Task.id, out v)) parsonsSol[Task.id] = v = new List<KeyValuePair<int, int>>(); return v; } }

        // Текст в редакторе для задач без запуска
        string NoRunEditorText(TaskData t)
        {
            switch (t.Mode)
            {
                case "cloze": return TaskChecks.ClozeCode(t, ClozeValsOf(t));
                case "parsons": return ParsonsCode(t);
                default: return t.starter ?? "";
            }
        }
        string[] ClozeValsOf(TaskData t) { string[] v; return clozeAns.TryGetValue(t.id, out v) ? v : new string[t.blanks != null ? t.blanks.Count : 0]; }

        string ParsonsCode(TaskData t)
        {
            List<KeyValuePair<int, int>> sol; if (!parsonsSol.TryGetValue(t.id, out sol) || sol.Count == 0) return Syntax.LineComment(t.language) != null ? Syntax.LineComment(t.language) + " собери программу из строк на вкладке «ОТВЕТ»" : "";
            var items = (t.lines ?? new string[0]).Concat(t.distractors ?? new string[0]).ToArray();
            int w = Syntax.IndentWidth(t.language);
            return string.Join("\n", sol.Select(x => new string(' ', x.Value * w) + items[x.Key].Trim()).ToArray());
        }

        void RefreshNoRunEditor() { if (Task != null && (IsCloze || IsParsons)) { ed.Text = NoRunEditorText(Task); ed.Place(); } }

        // ---------- вкладка «ОТВЕТ» ----------
        void NoRunTab(VisualElement c)
        {
            var t = Task; bool done = g.IsDone(t), show = done || revealed.Contains(t.id);
            fields.Clear();
            Para(c, "<b>" + K.Esc(t.goal) + "</b>", 16f, K.TextHi, 2f);
            if (IsPredict)
            {
                Para(c, "Впиши, что напечатает программа, строка в строку. Enter — новая строка, Ctrl+Enter — проверить.", 13f, K.Muted, 4f);
                string v; predictAns.TryGetValue(t.id, out v);
                var f = AddField(c, v ?? "", true, s => predictAns[t.id] = s);
                f.Box.style.minHeight = 110f;
                if (show) { Section(c, "ЭТАЛОН — НАСТОЯЩИЙ ВЫВОД ПРОГРАММЫ"); CodeBlock(c, t.output ?? "", Pal.Hex("89D185")); }
            }
            else if (IsCloze)
            {
                Para(c, "Впиши пропуски ‹1›, ‹2›… — код в редакторе обновится сразу. Tab — следующий пропуск, Enter — проверить.", 13f, K.Muted, 4f);
                var vals = ClozeVals; bool[] marks; clozeMarks.TryGetValue(t.id, out marks);
                for (int i = 0; i < vals.Length; i++)
                {
                    int idx = i;
                    var row = K.Box(true); row.style.alignItems = Align.Center; row.style.marginTop = 6f;
                    var lb = K.T("‹" + (i + 1) + "›", 15f, K.Sun, true, true); lb.style.width = 44f; row.Add(lb);
                    var holder = K.Box(); K.Grow(holder); row.Add(holder);
                    AddField(holder, vals[i] ?? "", false, s => { ClozeVals[idx] = s; if (clozeMarks.ContainsKey(t.id)) clozeMarks.Remove(t.id); RefreshNoRunEditor(); });
                    if (marks != null && idx < marks.Length) { var ic = new Icon(marks[idx] ? "check" : "error", marks[idx] ? K.Green : K.Red, 16f); ic.style.marginLeft = 8f; row.Add(ic); }
                    if (show) { var ans = K.T("= " + K.Esc(t.blanks[idx].answers.FirstOrDefault() ?? "…"), 14f, K.Green, true); ans.style.marginLeft = 10f; row.Add(ans); }
                    c.Add(row);
                }
            }
            else if (IsParsons) ParsonsBody(c, show);
            var bar = K.Box(true); bar.style.alignItems = Align.Center; bar.style.marginTop = 12f;
            var sb = Btn.Text(done ? "Сдано" : "Проверить", SubmitNoRun, K.Brand, K.BrandHover, 15f, Color.white, "check", Color.white); sb.style.height = 34f; sb.style.flexShrink = 0f;
            sb.Enabled = !done; bar.Add(sb);
            var hint = K.T("Ctrl+Enter", 13f, K.Dim); hint.style.marginLeft = 10f; bar.Add(hint);
            if (choiceVerdict != null) { var vv = K.T(K.Esc(choiceVerdict), 14f, choiceOk ? K.Green : Pal.Hex("FF8FA3"), false, true, true); vv.style.marginLeft = 14f; vv.style.flexShrink = 1f; bar.Add(vv); }
            c.Add(bar);
            if (show && !string.IsNullOrEmpty(t.explanation)) { Section(c, "РАЗБОР"); Card(c, K.Esc(t.explanation), K.Green); }
            if (fieldFocus >= fields.Count) fieldFocus = -1;
            for (int i = 0; i < fields.Count; i++) FieldRender(i);
        }

        void ParsonsBody(VisualElement c, bool show)
        {
            var t = Task; var items = ParsonsItems; var pool = Pool; var sol = Sol;
            int w = Syntax.IndentWidth(t.language);
            bool ind = TaskChecks.IndentMatters(t.language);
            Para(c, "Кликни строку слева — она встанет в конец программы справа. ↑ ↓ — порядок, ⇤ ⇥ — отступ" + (ind ? " (в " + LangName(t.language) + " он важен)" : "") + ", ✕ — убрать. Лишние строки не нужны.", 13f, K.Muted, 4f);
            var cols = K.Box(true); cols.style.marginTop = 8f; cols.style.alignItems = Align.FlexStart;
            var left = K.Box(); left.style.width = Length.Percent(40f); left.style.marginRight = 12f;
            var right = K.Box(); K.Grow(right);
            left.Add(K.T("СТРОКИ", 11f, K.Muted, false, true));
            right.Add(K.T("ТВОЯ ПРОГРАММА", 11f, K.Muted, false, true));
            foreach (var idx in pool)
            {
                int ii = idx;
                var b = new Btn(() => ParsonsAdd(ii)); K.Pad(b, 5f, 8f, 5f, 8f); K.Radius(b, 4f); b.style.marginTop = 4f; b.SetColors(Pal.Hex("202020"), K.Hover);
                K.Line(b, Pal.Hex("333333"), 1f, 1f, 1f, 1f);
                var l = K.T(K.Esc(items[idx].Trim()), 14f, K.Text, true); l.style.whiteSpace = WhiteSpace.Pre; b.Add(l); left.Add(b);
            }
            if (pool.Count == 0) left.Add(K.T("Все строки в программе.", 13f, K.Dim));
            for (int i = 0; i < sol.Count; i++)
            {
                int k = i;
                var row = K.Box(true); row.style.alignItems = Align.Center; row.style.marginTop = 4f; row.style.backgroundColor = Pal.Hex("202020"); K.Radius(row, 4f); K.Pad(row, 2f, 4f, 2f, 4f);
                row.Add(Mini("↑", () => ParsonsMove(k, -1))); row.Add(Mini("↓", () => ParsonsMove(k, 1)));
                var pad = new VisualElement(); pad.style.width = sol[i].Value * 22f; row.Add(pad);
                var l = K.T(K.Esc(items[sol[i].Key].Trim()), 14f, K.Text, true); l.style.whiteSpace = WhiteSpace.Pre; l.style.flexShrink = 1f; l.style.marginLeft = 6f; row.Add(l);
                row.Add(K.Spacer());
                row.Add(Mini("⇤", () => ParsonsIndent(k, -1))); row.Add(Mini("⇥", () => ParsonsIndent(k, 1))); row.Add(Mini("✕", () => ParsonsRemove(k)));
                right.Add(row);
            }
            if (sol.Count == 0) right.Add(K.T("Пока пусто — кликай строки слева по порядку.", 13f, K.Dim));
            cols.Add(left); cols.Add(right); c.Add(cols);
            if (show) { Section(c, "ЭТАЛОН"); CodeBlock(c, string.Join("\n", t.lines ?? new string[0]), Pal.Hex("89D185")); }
        }

        Btn Mini(string text, Action a)
        {
            var b = new Btn(a); b.style.width = 24f; b.style.height = 24f; b.style.justifyContent = Justify.Center; K.Radius(b, 3f); b.SetColors(Color.clear, K.Hover);
            b.Add(K.T(text, 13f, K.Muted, true)); return b;
        }

        void ParsonsChanged() { if (clozeMarks.ContainsKey(Task.id)) clozeMarks.Remove(Task.id); choiceVerdict = null; RefreshNoRunEditor(); RefreshBottom(); g.ReportWork(WorkKind.Edit); }
        void ParsonsAdd(int item)
        {
            if (g.IsDone(Task)) return;
            var sol = Sol; int indent = 0;
            if (sol.Count > 0)
            {
                string prev = ParsonsItems[sol[sol.Count - 1].Key].TrimEnd();
                indent = sol[sol.Count - 1].Value + (prev.EndsWith(":") || prev.EndsWith("{") ? 1 : 0);
                string cur = ParsonsItems[item].Trim();
                if ((cur.StartsWith("}") || cur.StartsWith("else") || cur.StartsWith("elif") || cur.StartsWith("except") || cur.StartsWith("finally")) && indent > 0) indent--;
            }
            Pool.Remove(item); sol.Add(new KeyValuePair<int, int>(item, indent)); ParsonsChanged();
        }
        void ParsonsRemove(int k) { var sol = Sol; if (k < 0 || k >= sol.Count || g.IsDone(Task)) return; Pool.Add(sol[k].Key); sol.RemoveAt(k); ParsonsChanged(); }
        void ParsonsMove(int k, int d) { var sol = Sol; int j = k + d; if (j < 0 || j >= sol.Count || g.IsDone(Task)) return; var x = sol[k]; sol[k] = sol[j]; sol[j] = x; ParsonsChanged(); }
        void ParsonsIndent(int k, int d) { var sol = Sol; if (k < 0 || k >= sol.Count || g.IsDone(Task)) return; sol[k] = new KeyValuePair<int, int>(sol[k].Key, Mathf.Clamp(sol[k].Value + d, 0, 8)); ParsonsChanged(); }

        // ---------- проверка ----------
        void SubmitNoRun()
        {
            var t = Task; if (t == null || !IsNoRun) return;
            g.ReportWork(WorkKind.Check);
            if (g.IsDone(t)) { Notice("Эта задача уже сдана. Разбор — внизу.", "check", K.Green); return; }
            bool ok; string note = null;
            if (IsPredict) { string v; predictAns.TryGetValue(t.id, out v); ok = TaskChecks.Predict(t, v, out note); }
            else if (IsCloze)
            {
                var vals = ClozeVals;
                var marks = t.blanks.Select((b, i) => TaskChecks.ClozeOne(b, vals[i])).ToArray();
                ok = marks.All(x => x);
                if (Diff != Difficulty.Hard) clozeMarks[t.id] = marks;
                if (!ok) note = "Верно " + marks.Count(x => x) + " из " + marks.Length + ".";
            }
            else ok = TaskChecks.Parsons(t, Sol.Select(x => new KeyValuePair<string, int>(ParsonsItems[x.Key], x.Value)).ToList(), out note);
            if (ok)
            {
                choiceOk = true; choiceVerdict = "Верно!";
                fieldFocus = -1;
                Notice("Верно! Разбор — на вкладке «Ответ».", "check", K.Green);
                g.CompleteTask(t, usedSolution.Contains(t.id), Late, HintsShown > 0, Spent);
            }
            else
            {
                choiceOk = false; failedChecks[t.id] = Fails + 1;
                choiceVerdict = Diff == Difficulty.Hard ? "Неверно." : "Неверно. " + (note ?? "");
                g.SpawnBug();
                string extra = Diff == Difficulty.Easy && Fails == 3 ? " Слева появилась кнопка «Показать ответ»." : "";
                Notice(choiceVerdict + " В офисе завёлся баг!" + extra, "error", K.Red);
            }
            bottom = Bottom.Answer;
            RefreshAll();
        }

        // ---------- поля ввода: отрисовка и клавиши ----------
        Field AddField(VisualElement c, string text, bool multi, Action<string> changed)
        {
            var f = new Field { Text = text ?? "", Multi = multi, Changed = changed };
            f.Cur = f.Text.Length;
            f.Box = K.Box(); f.Box.style.backgroundColor = Pal.Hex("1B1B1B"); K.Radius(f.Box, 4f); K.Pad(f.Box, 6f, 8f, 6f, 8f); f.Box.style.marginTop = 6f;
            f.View = K.T("", 15f, K.Text, true, false, true); f.View.style.whiteSpace = WhiteSpace.PreWrap; f.Box.Add(f.View);
            c.Add(f.Box);
            fields.Add(f);
            if (fields.Count - 1 == fieldFocus && fieldCur >= 0) f.Cur = Mathf.Min(fieldCur, f.Text.Length);
            return f;
        }

        void FieldRender(int i)
        {
            var f = fields[i]; bool foc = i == fieldFocus && Active;
            K.Line(f.Box, foc ? K.Accent : Pal.Hex("3C3C3C"), 1f, 1f, 1f, 1f);
            var sb = new StringBuilder();
            string a = f.Text.Substring(0, f.Cur), b = f.Text.Substring(f.Cur);
            sb.Append(K.Esc(a));
            if (foc)
            {
                bool nl = b.Length == 0 || b[0] == '\n';
                string ch = nl ? " " : b.Substring(0, 1);
                if (fieldBlink) sb.Append("<mark=#AEAFAD99>").Append(K.Esc(ch)).Append("</mark>"); else sb.Append(K.Esc(ch));
                sb.Append(K.Esc(nl ? b : b.Substring(1)));
            }
            else
            {
                sb.Append(K.Esc(b));
                if (f.Text.Length == 0) sb.Append("<color=#6E7681>" + (f.Multi ? "кликни и впиши вывод…" : "кликни и впиши…") + "</color>");
            }
            f.View.text = sb.ToString();
        }

        void FieldFocus(int i)
        {
            fieldFocus = i; fieldBlink = true; fieldBlinkAt = Time.unscaledTime + 0.53f;
            if (i >= 0 && i < fields.Count) fieldCur = fields[i].Cur;
            ed.Active = Active && fieldFocus < 0 && !(IsScenario && termFocus); ed.Place();
            for (int k = 0; k < fields.Count; k++) FieldRender(k);
        }

        bool FieldFocusByClick(VisualElement v)
        {
            for (int i = 0; i < fields.Count; i++)
                if (Inside(v, fields[i].Box)) { if (i != fieldFocus) FieldFocus(i); else { fields[i].Cur = fields[i].Text.Length; fieldCur = fields[i].Cur; FieldRender(i); } return true; }
            if (InEditor(v) && fieldFocus >= 0) FieldFocus(-1);
            return false;
        }

        void FieldEdit(Field f, string text, int cur)
        {
            f.Text = text; f.Cur = Mathf.Clamp(cur, 0, text.Length); fieldCur = f.Cur;
            fieldBlink = true; fieldBlinkAt = Time.unscaledTime + 0.53f;
            if (f.Changed != null) f.Changed(text);
            if (choiceVerdict != null && !g.IsDone(Task)) { choiceVerdict = null; }
            FieldRender(fields.IndexOf(f));
            g.ReportWork(WorkKind.Edit);
        }

        bool FieldKey(Event e)
        {
            if (fieldFocus < 0 || fieldFocus >= fields.Count) return false;
            var f = fields[fieldFocus]; bool ctrl = e.control || e.command;
            if (e.keyCode == KeyCode.Escape) { escClosedAt = Time.frameCount; FieldFocus(-1); return true; }
            if (ctrl && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) return false;   // Ctrl+Enter — проверка (хоткей IDE)
            if (ctrl && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.V:
                        {
                            string p = (GUIUtility.systemCopyBuffer ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
                            if (!f.Multi) p = p.Replace("\n", " ");
                            FieldEdit(f, f.Text.Insert(f.Cur, p), f.Cur + p.Length); return true;
                        }
                    case KeyCode.A: f.Cur = 0; FieldRender(fieldFocus); return true;
                    case KeyCode.E: f.Cur = f.Text.Length; FieldRender(fieldFocus); return true;
                    case KeyCode.U: FieldEdit(f, f.Text.Substring(f.Cur), 0); return true;
                }
                return e.keyCode != KeyCode.None && e.keyCode != KeyCode.S && e.keyCode != KeyCode.B ? true : false;
            }
            switch (e.keyCode)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter:
                    if (f.Multi && !e.shift) { FieldEdit(f, f.Text.Insert(f.Cur, "\n"), f.Cur + 1); return true; }
                    if (fieldFocus + 1 < fields.Count) { FieldFocus(fieldFocus + 1); return true; }
                    SubmitNoRun(); return true;
                case KeyCode.Tab: FieldFocus(e.shift ? Mathf.Max(0, fieldFocus - 1) : (fieldFocus + 1) % fields.Count); return true;
                case KeyCode.Backspace: if (f.Cur > 0) FieldEdit(f, f.Text.Remove(f.Cur - 1, 1), f.Cur - 1); return true;
                case KeyCode.Delete: if (f.Cur < f.Text.Length) FieldEdit(f, f.Text.Remove(f.Cur, 1), f.Cur); return true;
                case KeyCode.LeftArrow: f.Cur = Mathf.Max(0, f.Cur - 1); fieldCur = f.Cur; FieldRender(fieldFocus); return true;
                case KeyCode.RightArrow: f.Cur = Mathf.Min(f.Text.Length, f.Cur + 1); fieldCur = f.Cur; FieldRender(fieldFocus); return true;
                case KeyCode.Home: { int ls = f.Text.LastIndexOf('\n', Mathf.Max(0, f.Cur - 1)); f.Cur = f.Cur > 0 && ls >= 0 && f.Text[f.Cur - 1] != '\n' ? ls + 1 : (f.Cur > 0 && f.Text[f.Cur - 1] == '\n' ? f.Cur : 0); fieldCur = f.Cur; FieldRender(fieldFocus); return true; }
                case KeyCode.End: { int le = f.Text.IndexOf('\n', f.Cur); f.Cur = le < 0 ? f.Text.Length : le; fieldCur = f.Cur; FieldRender(fieldFocus); return true; }
                case KeyCode.UpArrow: case KeyCode.DownArrow:
                    {
                        if (!f.Multi) return true;
                        int ls = f.Text.LastIndexOf('\n', Mathf.Max(0, f.Cur - 1)); if (f.Cur == 0) ls = -1; else if (f.Text[f.Cur - 1] == '\n') ls = f.Cur - 1;
                        int col = f.Cur - (ls + 1);
                        if (e.keyCode == KeyCode.UpArrow) { if (ls < 0) return true; int ps = f.Text.LastIndexOf('\n', Mathf.Max(0, ls - 1)); if (ls == 0) ps = -1; f.Cur = Mathf.Min(ps + 1 + col, ls); }
                        else { int le = f.Text.IndexOf('\n', f.Cur); if (le < 0) return true; int ne = f.Text.IndexOf('\n', le + 1); if (ne < 0) ne = f.Text.Length; f.Cur = Mathf.Min(le + 1 + col, ne); }
                        fieldCur = f.Cur; FieldRender(fieldFocus); return true;
                    }
            }
            if (e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15) return false;
            char ch = e.character;
            if (ch == '\t' || ch == '\n' || ch == '\r') return true;
            if (ch >= ' ') { FieldEdit(f, f.Text.Insert(f.Cur, ch.ToString()), f.Cur + 1); return true; }
            return e.keyCode != KeyCode.None;
        }

        void FieldsUpdate()
        {
            if (fieldFocus >= 0 && Active && Time.unscaledTime > fieldBlinkAt && fieldFocus < fields.Count)
            { fieldBlink = !fieldBlink; fieldBlinkAt = Time.unscaledTime + 0.53f; FieldRender(fieldFocus); }
        }

        // ---------- «Кликни по багу»: выбор строки в редакторе ----------
        void ClickBugPicked()
        {
            if (!IsClickBug || g.IsDone(Task)) return;
            int line = ed.CurL + 1;
            if (line < 1 || line > ed.L.Count) return;
            bugPicks[Task.id] = line;
            if (choiceVerdict != null) choiceVerdict = null;
            bottom = Bottom.Answer;
            RefreshEditorFlags(); RefreshBottom();
        }

        // ======================= проводник: раздел «Разминка» =======================
        static readonly Color WarmColor = new Color(0.773f, 0.525f, 0.753f);   // #C586C0
        void WarmupSection(VisualElement c)
        {
            var list = g.WarmupTasks;
            bool open = Toggle("warmup", list.Contains(Task));
            var h = new Btn(() => Flip("warmup", open)); h.style.height = 28f; h.style.marginTop = 12f; h.style.marginLeft = -6f; K.Pad(h, 0f, 6f, 0f, 2f);
            h.Add(new Icon(open ? "chevD" : "chevR", K.Muted, 16f));
            h.Add(new Icon("star", WarmColor, 14f)); var ht = K.T("РАЗМИНКА", 12f, WarmColor, false, true); ht.style.marginLeft = 6f; h.Add(ht);
            h.Add(K.Spacer()); h.Add(K.T(g.WarmupDoneCount + " / " + list.Count, 12f, K.Muted)); c.Add(h);
            if (!open) return;
            Para(c, "Короткие задачи без написания кода: вывод, пропуски, сборка из строк, поиск бага. Вне пути — открыты всегда.", 12f, K.Muted, 2f);
            foreach (var t in list)
            {
                bool cur = t == Task, done = g.IsDone(t);
                var tt = t;
                var row = new Btn(() => { if (tt != Task) Open(tt); });
                row.style.height = 28f; K.Pad(row, 0f, 8f, 0f, 8f);
                row.SetColors(cur ? K.Press : Color.clear, cur ? K.Press : K.Hover);
                row.Add(FileIcon(t, true));
                var nl = K.T(TaskCode(t).ToLower().Replace("-", "_") + Ext(t), 14f, cur ? K.TextHi : K.Text, true); nl.style.marginLeft = 8f; nl.style.flexShrink = 0f; row.Add(nl);
                var ttl = K.T(K.Esc(t.title), 13f, K.Muted); ttl.style.marginLeft = 10f; ttl.style.flexShrink = 1f; ttl.style.overflow = Overflow.Hidden; row.Add(ttl);
                row.Add(K.Spacer());
                if (done) row.Add(new Icon("check", K.Green, 16f));
                else row.Add(K.T(TypeBadge(t), 11f, K.Dim));
                c.Add(row);
            }
        }

        static string TypeBadge(TaskData t)
        {
            switch (t.type)
            {
                case "predict": return "вывод";
                case "cloze": return "пропуски";
                case "parsons": return "сборка";
                case "clickbug": return "баг";
                default: return "";
            }
        }

        // ======================= компилируемые языки в Docker (спринт 10) =======================
        // «Проверить» — тесты в контейнере языка (go test, javac+MainTest…); без Docker — проверка по требованиям к коду.
        // «Запустить» — сборка и запуск программы. Всё идёт в фоне: строки хода работы попадают в терминал, итог — в BoxPoll.
        BoxJob<BoxReport> boxCheck; BoxJob<BoxRun> boxRun; TaskData boxTask;
        bool boxProbing, boxStaticCheck;
        bool IsBox { get { return TMode == "box"; } }
        bool BoxBusy { get { return boxCheck != null || boxRun != null || boxProbing; } }
        bool DockerUp { get { var sh = g.Env; return sh != null && (sh.State == EnvState.Ready || sh.State == EnvState.NoImage); } }
        LangSpec BoxSpec { get { return Task != null ? LangBox.For(Task.language) : null; } }

        // Состояние Docker ещё неизвестно — сначала проверить его, потом продолжить (then)
        bool BoxNeedProbe(Action then)
        {
            var sh = g.Env;
            if (sh == null || sh.State != EnvState.Unknown) return false;
            boxProbing = true; var t = Task;
            terminal.Append("<color=#9D9D9D>Проверяю Docker…</color>\n");
            sh.Probe(st => { boxProbing = false; if (Task == t) then(); else RefreshStatus(); });
            RefreshBottom(); RefreshStatus();
            return true;
        }

        void CheckBox()
        {
            var spec = BoxSpec;
            if (spec == null) return;
            if (BoxNeedProbe(CheckBox)) return;
            string cmd = spec.testShow ?? spec.testCmd;
            if (!DockerUp) { BoxStaticCheck("Docker не запущен — решение проверено по коду, без запуска. С Docker задачу проверят настоящие тесты (" + cmd + ")."); return; }
            boxStaticCheck = false;
            terminal.Append(Prompt()).Append(cmd).Append('\n');
            string code = Code, tests = Task.testCode, id = Task.id;
            boxTask = Task;
            boxCheck = BoxJob<BoxReport>.Start(j => LangBox.Test(spec, id, code, tests, j.Note));
            bottom = Bottom.Terminal;
            RefreshBottom(); RefreshEditorFlags(); RefreshStatus();
        }

        void BoxStaticCheck(string why)
        {
            boxStaticCheck = true;
            terminal.Append(Prompt()).Append("kodzilla lint ").Append(FileName(Task)).Append('\n');
            terminal.Append("<color=#CCA700>").Append(K.Esc(why)).Append("</color>\n");
            FinishCheck(TaskChecks.Static(Code, Task.requirements ?? new List<object>()), -1, null);
        }

        void RunBox()
        {
            var spec = BoxSpec;
            if (spec == null) return;
            if (BoxNeedProbe(RunBox)) return;
            terminal.Append(Prompt()).Append(spec.runShow ?? spec.runCmd).Append('\n');
            if (!DockerUp)
            {
                terminal.Append("<color=#CCA700>Код на " + spec.name + " компилируется и запускается в Docker, а он сейчас не запущен. Запусти Docker Desktop — или сдавай задачу кнопкой «Проверить»: без Docker решение проверится по коду.</color>\n");
                Notice("Для запуска " + spec.name + " нужен Docker. «Проверить» работает и без него — по коду.", "warning", K.Orange);
                bottom = Bottom.Terminal; RefreshBottom(); return;
            }
            string code = Code, id = Task.id;
            boxTask = Task;
            boxRun = BoxJob<BoxRun>.Start(j => LangBox.Run(spec, id, code, j.Note));
            bottom = Bottom.Terminal;
            RefreshBottom(); RefreshEditorFlags(); RefreshStatus();
        }

        // Каждый кадр: строки хода работы и готовые результаты
        void BoxPoll()
        {
            bool dirty = false;
            if (boxCheck != null)
            {
                foreach (var n in boxCheck.TakeNotes()) { terminal.Append("<color=#9D9D9D>").Append(K.Esc(n)).Append("</color>\n"); dirty = true; }
                if (boxCheck.IsDone) { var r = boxCheck.Result; boxCheck = null; if (boxTask == Task) FinishBox(r); else RefreshStatus(); return; }
            }
            if (boxRun != null)
            {
                foreach (var n in boxRun.TakeNotes()) { terminal.Append("<color=#9D9D9D>").Append(K.Esc(n)).Append("</color>\n"); dirty = true; }
                if (boxRun.IsDone) { var r = boxRun.Result; boxRun = null; if (boxTask == Task) FinishBoxRun(r); else RefreshStatus(); return; }
            }
            if (dirty) RefreshBottom();
        }

        void BoxPulled(bool pulled)
        {
            var spec = BoxSpec;
            if (!pulled || spec == null || g.Env == null || g.Env.ImagePulled == null) return;
            g.Env.ImagePulled(spec.image);   // «Удалить всё, что создала игра» уберёт и образ компилятора
        }

        void FinishBox(BoxReport r)
        {
            if (r == null) { BoxStaticCheck("Тесты не запустились (внутренняя ошибка) — решение проверено по коду."); return; }
            BoxPulled(r.pulled);
            if (r.setupError != null) { terminal.Append("<color=#F14C4C>").Append(K.Esc(r.setupError)).Append("</color>\n"); BoxStaticCheck("Пока проверяю по коду, без запуска."); return; }
            string log = (r.log ?? "").TrimEnd('\n');
            if (log.Length > 6000) log = "…\n" + log.Substring(log.Length - 6000);
            if (log.Length > 0) terminal.Append(K.Esc(log)).Append('\n');
            if (r.ms > 0) terminal.Append("<color=#9D9D9D>Тесты: " + (r.ms / 1000.0).ToString("0.0") + " с</color>\n");
            FinishCheck(r.results, r.errLine, r.errText);
        }

        void FinishBoxRun(BoxRun r)
        {
            if (r == null) return;
            BoxPulled(r.pulled);
            if (r.setupError != null) terminal.Append("<color=#F14C4C>").Append(K.Esc(r.setupError)).Append("</color>\n");
            else
            {
                if (r.output.Length > 0) terminal.Append(K.Esc(r.output.TrimEnd('\n'))).Append('\n');
                if (r.timedOut) terminal.Append("<color=#F14C4C>Программа не завершилась за " + LangBox.RunTimeoutSec + " с и остановлена — похоже на бесконечный цикл.</color>\n");
                else if (r.errLine != 0 && r.errText != null)
                {
                    runtimeErrorLine = r.errLine; runtimeErrorText = r.errText;
                    Notice(r.errText.Length > 90 ? r.errText.Substring(0, 90) + "…" : r.errText, "error", K.Red);
                }
                else if (r.code == 0) { terminal.Append("<color=#89D185>Готово за " + (r.ms / 1000.0).ToString("0.0") + " с.</color> <color=#9D9D9D>Чтобы сдать задачу — «Проверить» (Ctrl+Enter).</color>\n"); runtimeErrorLine = -1; runtimeErrorText = null; }
                else terminal.Append("<color=#F14C4C>Код выхода " + r.code + ".</color>\n");
            }
            TrimTerminal();
            RefreshBottom(); RefreshEditorFlags(); RefreshStatus();
        }

        // Правая панель: как проверяется задача на компилируемом языке
        void BoxHowChecked(VisualElement c, bool hasExpl)
        {
            var spec = BoxSpec;
            if (spec == null) return;
            Section(c, "КАК ПРОВЕРЯЕТСЯ", hasExpl ? 18f : 4f);
            Para(c, "Тесты лежат в " + spec.test + " и запускаются по-настоящему (" + (spec.testShow ?? spec.testCmd) + ") в контейнере " + spec.image + " (Docker). Первый запуск скачает образ — " + spec.size + ".", 14f, K.Text, 6f);
            var st = g.Env != null ? g.Env.State : EnvState.Unknown;
            if (st == EnvState.Unknown) Para(c, "Docker проверится при первом «Запустить» или «Проверить».", 13f, K.Muted, 4f);
            else Para(c, DockerUp ? "Docker работает: «Проверить» запустит тесты, «Запустить» — " + (spec.runShow ?? spec.runCmd) + "." : "Docker сейчас не запущен: «Проверить» сверит решение с требованиями ниже, без запуска.", 13f, DockerUp ? K.Green : K.Orange, 4f);
            if (Diff != Difficulty.Hard && !string.IsNullOrEmpty(Task.testCode)) { Section(c, "ТЕСТЫ — " + spec.test.ToUpperInvariant()); CodeBlock(c, Task.testCode, Pal.Hex("9CDCFE")); }
            if (Task.requirements != null && Task.requirements.Count > 0)
            {
                Section(c, "БЕЗ DOCKER — ПРОВЕРКА ПО КОДУ");
                foreach (var x in Task.requirements) { var d = x as Dictionary<string, object>; object inp = null; if (d != null) d.TryGetValue("input", out inp); Para(c, "• " + K.Esc(inp as string ?? ""), 14f, K.Text, 4f); }
            }
        }
    }
}
