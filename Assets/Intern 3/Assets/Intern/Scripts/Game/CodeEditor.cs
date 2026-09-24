// Редактор кода для игровой IDE: моноширинный текст с подсветкой при наборе, номера строк и точки остановки,
// выделение, отмена, автоотступы и парные скобки, волнистое подчёркивание ошибок, автодополнение,
// подсказки при наведении и миникарта. Ввод приходит из IdeScreen (события OnGUI в координатах панели).
// Язык задаёт Language (python по умолчанию): подсветка и автодополнение — через Syntax; подсказки с документацией,
// выравнивание else/elif и отступы по «:»/return — только для python.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public class CodeEditor : VisualElement
    {
        public float FontSize = 19f, LineH = 28f, CharW = 11f;
        const float Gutter = 70f, PadL = 16f, MinimapW = 96f, MiniLine = 3.2f, MiniChar = 1.5f;

        public readonly List<string> L = new List<string> { "" };
        public int CurL, CurC, AncL, AncC;
        int wantCol = -1;
        public float ScrollY, ScrollX;
        public bool ReadOnly, Active;
        public readonly HashSet<int> Breakpoints = new HashSet<int>();
        public int ErrorLine = -1, DebugLine = -1;
        public string ErrorText;
        public bool DebugIsError;
        public event Action Changed, BreakpointsChanged, CursorMoved;
        public Func<string, string> ExtraHover;   // подсказка для слова от IDE (например, значение переменной в отладке)

        readonly VisualElement gutter, viewport, content, curLine, selLayer, linesBox, caret, squiggles, debugBg, minimap, vthumb, popup, tipBox, gutterMarks;
        readonly List<Label> labels = new List<Label>(), numbers = new List<Label>();
        readonly List<List<Span>> spans = new List<List<Span>>();
        readonly List<string> lineStates = new List<string>();   // состояние токенайзера на входе в строку (для Syntax.Line)
        bool textDirty = true;
        float blink, lastEdit = -10f;

        // отмена
        struct Snap { public string Text; public int L, C; }
        readonly List<Snap> undo = new List<Snap>(), redo = new List<Snap>();
        float lastTypeTime; bool typingGroup;

        // автодополнение
        List<Completion> items = new List<Completion>();
        int itemSel;
        public bool PopupOpen { get { return popup.style.display == DisplayStyle.Flex; } }

        // наведение
        Vector2 hoverPos; float hoverSince; string hoverKey; int hoverGutterLine = -1;

        public CodeEditor()
        {
            style.flexGrow = 1; style.flexShrink = 1; style.flexDirection = FlexDirection.Row; style.overflow = Overflow.Hidden;
            style.backgroundColor = K.EditorBg; pickingMode = PickingMode.Position;
            LineH = Mathf.Round(FontSize * 1.47f);
            CharW = K.MonoAdvance(FontSize);

            gutter = K.Box(); gutter.style.width = Gutter; gutter.style.flexShrink = 0f; gutter.style.overflow = Overflow.Hidden; gutter.pickingMode = PickingMode.Ignore;
            gutterMarks = new VisualElement(); K.Fill(gutterMarks); gutterMarks.pickingMode = PickingMode.Ignore; gutterMarks.generateVisualContent += DrawGutterMarks;
            gutter.Add(gutterMarks);
            Add(gutter);

            viewport = K.Box(); K.Grow(viewport); viewport.style.overflow = Overflow.Hidden; viewport.pickingMode = PickingMode.Ignore;
            Add(viewport);
            content = new VisualElement(); content.style.position = Position.Absolute; content.style.left = 0f; content.style.top = 0f; content.style.width = 4000f; content.pickingMode = PickingMode.Ignore;
            viewport.Add(content);
            curLine = new VisualElement(); curLine.style.position = Position.Absolute; curLine.style.left = 0f; curLine.style.right = 0f; curLine.style.height = LineH;
            K.Line(curLine, K.Border, 1f, 0f, 1f, 0f); curLine.pickingMode = PickingMode.Ignore; content.Add(curLine);
            debugBg = new VisualElement(); debugBg.style.position = Position.Absolute; debugBg.style.left = 0f; debugBg.style.right = 0f; debugBg.style.height = LineH; debugBg.pickingMode = PickingMode.Ignore;
            content.Add(debugBg);
            selLayer = new VisualElement(); K.Fill(selLayer); selLayer.pickingMode = PickingMode.Ignore; content.Add(selLayer);
            linesBox = new VisualElement(); K.Fill(linesBox); linesBox.pickingMode = PickingMode.Ignore; content.Add(linesBox);
            squiggles = new VisualElement(); K.Fill(squiggles); squiggles.pickingMode = PickingMode.Ignore; squiggles.generateVisualContent += DrawSquiggles; content.Add(squiggles);
            caret = new VisualElement(); caret.style.position = Position.Absolute; caret.style.width = 2f; caret.style.height = LineH - 4; caret.style.backgroundColor = Pal.Hex("AEAFAD");
            caret.pickingMode = PickingMode.Ignore; content.Add(caret);

            minimap = new VisualElement(); minimap.style.width = MinimapW; minimap.style.flexShrink = 0f; minimap.pickingMode = PickingMode.Ignore;
            K.Line(minimap, K.Border, 0f, 0f, 0f, 1f); minimap.generateVisualContent += DrawMinimap; Add(minimap);
            vthumb = new VisualElement(); vthumb.style.position = Position.Absolute; vthumb.style.width = 12f; vthumb.style.right = MinimapW; vthumb.style.backgroundColor = new Color(1, 1, 1, 0.1f);
            vthumb.pickingMode = PickingMode.Ignore; Add(vthumb);

            popup = K.Box(); popup.style.position = Position.Absolute; popup.style.display = DisplayStyle.None; popup.style.backgroundColor = K.Widget;
            K.Line(popup, Pal.Hex("454545"), 1f, 1f, 1f, 1f); K.Radius(popup, 3f); popup.pickingMode = PickingMode.Ignore; Add(popup);
            tipBox = K.Box(); tipBox.style.position = Position.Absolute; tipBox.style.display = DisplayStyle.None; tipBox.style.backgroundColor = K.Widget;
            K.Line(tipBox, Pal.Hex("454545"), 1f, 1f, 1f, 1f); K.Radius(tipBox, 3f); K.Pad(tipBox, 8f, 12f, 8f, 12f); tipBox.style.maxWidth = 520f; tipBox.pickingMode = PickingMode.Ignore; Add(tipBox);

            RegisterCallback<GeometryChangedEvent>(e => { ClampScroll(); Place(); });
        }

        // ================= текст =================
        public string Text
        {
            get { return string.Join("\n", L); }
            set
            {
                L.Clear();
                L.AddRange((value ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", new string(' ', IndW)).Split('\n'));
                if (L.Count == 0) L.Add("");
                CurL = CurC = AncL = AncC = 0; ScrollY = ScrollX = 0; undo.Clear(); redo.Clear();
                textDirty = true; HidePopup(); Place();
            }
        }

        // Язык подсветки: python, javascript, typescript, jsx, tsx, sql, yaml, bash, dockerfile, html, css, nginx, hcl, promql, text.
        // Синонимы (js, ts, yml, sh, tf…) приводятся к каноническому имени; null и неизвестные — text (без подсветки).
        string lang = "python";
        public string Language
        {
            get { return lang; }
            set
            {
                string v = Syntax.Norm(value);
                if (v == lang) return;
                lang = v; textDirty = true; HidePopup(); HideTooltip(); hoverKey = null; Place();
            }
        }
        bool Py { get { return lang == "python"; } }
        int IndW { get { return Syntax.IndentWidth(lang); } }
        bool IsWord(char c) { return Syntax.IsWordChar(lang, c); }
        string Pairs { get { return Syntax.IsJs(lang) ? "([{\"'`" : "([{\"'"; } }       // открывающие автопары
        string Closes { get { return Syntax.IsJs(lang) ? ")]}\"'`" : ")]}\"'"; } }      // и парные им

        public void SetCursor(int line, int col, bool extend = false)
        {
            CurL = Mathf.Clamp(line, 0, L.Count - 1); CurC = Mathf.Clamp(col, 0, L[CurL].Length);
            if (!extend) { AncL = CurL; AncC = CurC; }
            blink = 0; Reveal(); Place(); if (CursorMoved != null) CursorMoved();
        }

        public void GoToLine(int line1) { SetCursor(line1 - 1, FirstNonSpace(L[Mathf.Clamp(line1 - 1, 0, L.Count - 1)])); CenterOn(line1 - 1); }
        public void CenterOn(int line) { ScrollY = line * LineH - viewport.layout.height * 0.4f; ClampScroll(); Place(); }
        public void CenterOnIfHidden(int line)
        {
            float vh = viewport.layout.height; if (float.IsNaN(vh)) return;
            float y = line * LineH; if (y < ScrollY || y + LineH > ScrollY + vh) CenterOn(line);
        }

        bool HasSel { get { return CurL != AncL || CurC != AncC; } }
        void SelRange(out int l0, out int c0, out int l1, out int c1)
        {
            if (AncL < CurL || (AncL == CurL && AncC <= CurC)) { l0 = AncL; c0 = AncC; l1 = CurL; c1 = CurC; }
            else { l0 = CurL; c0 = CurC; l1 = AncL; c1 = AncC; }
        }
        string Selected()
        {
            if (!HasSel) return "";
            int l0, c0, l1, c1; SelRange(out l0, out c0, out l1, out c1);
            if (l0 == l1) return L[l0].Substring(c0, c1 - c0);
            var sb = new StringBuilder(L[l0].Substring(c0));
            for (int i = l0 + 1; i < l1; i++) sb.Append('\n').Append(L[i]);
            sb.Append('\n').Append(L[l1].Substring(0, c1));
            return sb.ToString();
        }

        void Push(bool typing)
        {
            float now = Time.unscaledTime;
            if (typing && typingGroup && now - lastTypeTime < 1.0f) { lastTypeTime = now; return; }
            undo.Add(new Snap { Text = Text, L = CurL, C = CurC }); if (undo.Count > 200) undo.RemoveAt(0);
            redo.Clear(); typingGroup = typing; lastTypeTime = now;
        }
        void Restore(List<Snap> from, List<Snap> to)
        {
            if (from.Count == 0) return;
            to.Add(new Snap { Text = Text, L = CurL, C = CurC });
            var s = from[from.Count - 1]; from.RemoveAt(from.Count - 1);
            L.Clear(); L.AddRange(s.Text.Split('\n'));
            SetCursor(s.L, s.C); typingGroup = false; Edited();
        }

        void Edited() { textDirty = true; lastEdit = Time.unscaledTime; ErrorLine = -1; if (Changed != null) Changed(); Reveal(); Place(); }

        void DeleteSel()
        {
            if (!HasSel) return;
            int l0, c0, l1, c1; SelRange(out l0, out c0, out l1, out c1);
            L[l0] = L[l0].Substring(0, c0) + L[l1].Substring(c1);
            if (l1 > l0) L.RemoveRange(l0 + 1, l1 - l0);
            CurL = AncL = l0; CurC = AncC = c0;
        }

        public void Insert(string s)
        {
            DeleteSel();
            s = s.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", new string(' ', IndW));
            var parts = s.Split('\n');
            string line = L[CurL], before = line.Substring(0, CurC), after = line.Substring(CurC);
            if (parts.Length == 1) { L[CurL] = before + s + after; CurC += s.Length; }
            else
            {
                L[CurL] = before + parts[0];
                for (int i = 1; i < parts.Length; i++) L.Insert(CurL + i, parts[i]);
                CurL += parts.Length - 1; CurC = parts[parts.Length - 1].Length; L[CurL] += after;
            }
            AncL = CurL; AncC = CurC; wantCol = -1; blink = 0;
        }

        static int FirstNonSpace(string s) { int i = 0; while (i < s.Length && s[i] == ' ') i++; return i; }

        // ================= клавиатура =================
        public bool KeyDown(Event e)
        {
            bool ctrl = e.control || e.command, shift = e.shift;
            if (PopupOpen && HandlePopupKey(e)) return true;
            HideTooltip();
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: if (HasSel && !shift) { int a, b, c, d; SelRange(out a, out b, out c, out d); SetCursor(a, b); } else Move(ctrl ? WordLeft() : Left(), shift); HidePopup(); return true;
                case KeyCode.RightArrow: if (HasSel && !shift) { int a, b, c, d; SelRange(out a, out b, out c, out d); SetCursor(c, d); } else Move(ctrl ? WordRight() : Right(), shift); HidePopup(); return true;
                case KeyCode.UpArrow: Vert(-1, shift); return true;
                case KeyCode.DownArrow: Vert(1, shift); return true;
                case KeyCode.PageUp: Vert(-Mathf.Max(1, (int)(viewport.layout.height / LineH) - 1), shift); return true;
                case KeyCode.PageDown: Vert(Mathf.Max(1, (int)(viewport.layout.height / LineH) - 1), shift); return true;
                case KeyCode.Home:
                    if (ctrl) { SetCursor(0, 0, shift); return true; }
                    { int f = FirstNonSpace(L[CurL]); SetCursor(CurL, CurC == f ? 0 : f, shift); } HidePopup(); return true;
                case KeyCode.End: if (ctrl) SetCursor(L.Count - 1, L[L.Count - 1].Length, shift); else SetCursor(CurL, L[CurL].Length, shift); HidePopup(); return true;
            }
            if (ctrl && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.A: AncL = 0; AncC = 0; CurL = L.Count - 1; CurC = L[CurL].Length; Place(); return true;
                    case KeyCode.C: Copy(); return true;
                    case KeyCode.X: if (ReadOnly) return true; Copy(); Push(false); if (!HasSel) { L.RemoveAt(CurL); if (L.Count == 0) L.Add(""); SetCursor(Mathf.Min(CurL, L.Count - 1), 0); } else DeleteSel(); Edited(); return true;
                    case KeyCode.V: if (ReadOnly) return true; Push(false); Insert(GUIUtility.systemCopyBuffer ?? ""); Edited(); return true;
                    case KeyCode.Z: if (ReadOnly) return true; if (shift) Restore(redo, undo); else Restore(undo, redo); return true;
                    case KeyCode.Y: if (ReadOnly) return true; Restore(redo, undo); return true;
                    case KeyCode.Slash: if (ReadOnly) return true; ToggleComment(); return true;
                    case KeyCode.Space: if (!ReadOnly) ShowCompletions(true); return true;
                    case KeyCode.D: if (ReadOnly) return true; Push(false); { string ln = L[CurL]; L.Insert(CurL + 1, ln); SetCursor(CurL + 1, CurC); } Edited(); return true;
                }
                if (e.keyCode != KeyCode.None) return false;   // остальные сочетания — хоткеям IDE
            }
            if (ReadOnly) return e.character != 0;
            switch (e.keyCode)
            {
                case KeyCode.Backspace: Push(false); Backspace(ctrl); Edited(); UpdatePopupAfterEdit(); return true;
                case KeyCode.Delete: Push(false); DeleteFwd(); Edited(); return true;
                case KeyCode.Return: case KeyCode.KeypadEnter: Push(false); NewLine(); Edited(); HidePopup(); return true;
                case KeyCode.Tab: Push(false); if (shift) Unindent(); else Tab(); Edited(); return true;
            }
            char ch = e.character;
            if (ch == '\t' || ch == '\n' || ch == '\r') return true;   // эти приходят вторым событием — уже обработаны выше
            if (ch >= ' ' && (!ctrl || e.alt))
            {
                Push(true); TypeChar(ch); Edited();
                if (IsWord(ch)) ShowCompletions(false); else if (ch != '.') HidePopup();
                return true;
            }
            return false;
        }

        int parenGuardL = -1, parenGuardC = -1;   // после автодополнения «print()» набранная «(» не удваивает скобки

        void TypeChar(char ch)
        {
            bool guard = ch == '(' && !HasSel && CurL == parenGuardL && CurC == parenGuardC;
            parenGuardL = -1;
            if (guard) return;
            string line = L[CurL];
            char next = CurC < line.Length ? line[CurC] : '\0';
            string open = Pairs, close = Closes;
            if (!HasSel && close.IndexOf(ch) >= 0 && next == ch && (ch == ')' || ch == ']' || ch == '}' || (CurC > 0 && line[CurC - 1] != '\\')))
            { CurC++; AncC = CurC; return; }                                 // перепрыгиваем через закрывающую
            int oi = open.IndexOf(ch);
            bool quote = ch == '"' || ch == '\'' || ch == '`';
            bool freeAfter = next == '\0' || next == ' ' || close.IndexOf(next) >= 0 || next == ':' || next == ',';
            bool freeBefore = !quote || CurC == 0 || !PySyntax.IsId(line[CurC - 1]);
            if (oi >= 0 && !HasSel && freeAfter && freeBefore && !InStringOrComment(CurL, CurC))
            {
                Insert(ch.ToString() + close[oi]); CurC--; AncC = CurC; return;   // парная скобка/кавычка
            }
            if (oi >= 0 && HasSel && Selected().IndexOf('\n') < 0)
            {
                string s = Selected(); Insert(ch + s + close[oi]); return;         // обернуть выделение
            }
            Insert(ch.ToString());
            // «}» (и «]», «)») в начале строки — выравниваем по строке с парной скобкой (не python)
            if (!Py && (ch == '}' || ch == ']' || ch == ')') && Syntax.UsesBraces(lang)) AlignCloser();
            // «else:», «elif …:», «except:» — выравниваем по ближайшему if/try выше, как VS Code (только python)
            if (ch == ':' && Py)
            {
                string t = L[CurL].Trim();
                bool isElse = t == "else:" || (t.StartsWith("elif ") && t.EndsWith(":"));
                bool isExcept = t == "except:" || t.StartsWith("except ") || t == "finally:";
                if ((isElse || isExcept) && CurL > 0)
                {
                    int ind = FirstNonSpace(L[CurL]), minSeen = ind, target = -1;
                    for (int i = CurL - 1; i >= 0; i--)
                    {
                        string pl = L[i]; if (pl.Trim().Length == 0) continue;
                        int pi = FirstNonSpace(pl); string pt = pl.Trim();
                        bool match = isElse ? (pt.StartsWith("if ") || pt.StartsWith("elif ") || pt.StartsWith("for ") || pt.StartsWith("while "))
                                            : (pt.StartsWith("try") || pt.StartsWith("except"));
                        if (pi <= ind && pi <= minSeen && match) { target = pi; break; }
                        minSeen = Mathf.Min(minSeen, pi);
                        if (minSeen < 0) break;
                    }
                    if (target >= 0 && target < ind)
                    {
                        int d = ind - target; L[CurL] = L[CurL].Substring(d); CurC = Mathf.Max(0, CurC - d); AncC = CurC;
                    }
                }
            }
        }

        bool InStringOrComment(int li, int col)
        {
            string line = L[li];
            if (Py)   // как было: строка разбирается сама по себе
            {
                string st = null; var sp = PySyntax.Line(line, ref st);
                foreach (var s in sp) if ((s.Kind == Tok.Str || s.Kind == Tok.Comment) && col > s.Start && col < s.Start + s.Len) return true;
                return false;
            }
            foreach (var s in LineSpans(li))
            {
                int end = s.Start + s.Len;
                if (col <= s.Start || col > end) continue;
                bool atEol = col == end && end == line.Length;                   // курсор в конце строки сразу за токеном
                if (s.Kind == Tok.Comment && (col < end || (atEol && !line.EndsWith("*/") && !line.EndsWith("-->")))) return true;
                if (s.Kind == Tok.Str && (col < end || (atEol && Unclosed(line, s)))) return true;
            }
            return false;
        }

        // строковый литерал без закрывающей кавычки в конце строки: курсор за ним ещё внутри строки
        static bool Unclosed(string line, Span s)
        {
            char q = line[s.Start];
            if (q != '"' && q != '\'' && q != '`') return false;
            int end = s.Start + s.Len;
            return s.Len == 1 || line[end - 1] != q || (end >= 2 && line[end - 2] == '\\');
        }

        // Токены строки li с правильным входным состоянием (многострочные комментарии, шаблонные строки, JSX…)
        List<Span> LineSpans(int li)
        {
            string st = null;
            if (!textDirty && li < lineStates.Count) st = lineStates[li];
            else for (int i = 0; i < li; i++) Syntax.Line(lang, L[i], ref st);
            return Syntax.Line(lang, L[li], ref st);
        }

        // Текст строки до col без комментария в конце (для решений об отступе)
        string CodeBefore(int li, int col)
        {
            string s = L[li].Substring(0, col);
            foreach (var sp in LineSpans(li)) if (sp.Kind == Tok.Comment && sp.Start < col) { s = s.Substring(0, sp.Start); break; }
            return s.TrimEnd();
        }

        // Закрывающая скобка, перед которой в строке только пробелы, встаёт на отступ строки с парной открывающей
        void AlignCloser()
        {
            int fns = FirstNonSpace(L[CurL]);
            if (CurC - 1 != fns) return;
            int depth = 0;
            for (int li = CurL; li >= 0 && li > CurL - 5000; li--)
            {
                string s = L[li];
                List<Span> sp = li < CurL && !textDirty && li < spans.Count ? spans[li] : null;
                for (int c = li == CurL ? fns - 1 : s.Length - 1; c >= 0; c--)
                {
                    char ch = s[c];
                    bool opener = ch == '{' || ch == '[' || ch == '(', closer = ch == '}' || ch == ']' || ch == ')';
                    if (!opener && !closer) continue;
                    if (sp != null && InStrOrComment(sp, c)) continue;
                    if (closer) { depth++; continue; }
                    if (depth > 0) { depth--; continue; }
                    int target = FirstNonSpace(s);
                    L[CurL] = new string(' ', target) + L[CurL].Substring(fns);
                    CurC = target + 1; AncC = CurC; return;
                }
            }
        }
        static bool InStrOrComment(List<Span> sp, int c)
        {
            foreach (var s in sp) if ((s.Kind == Tok.Str || s.Kind == Tok.Comment) && c >= s.Start && c < s.Start + s.Len) return true;
            return false;
        }

        void Backspace(bool word)
        {
            if (HasSel) { DeleteSel(); return; }
            if (CurC == 0) { if (CurL == 0) return; int c = L[CurL - 1].Length; L[CurL - 1] += L[CurL]; L.RemoveAt(CurL); CurL--; CurC = c; AncL = CurL; AncC = CurC; return; }
            string line = L[CurL];
            int from = CurC - 1;
            if (word) from = WordLeft().y;
            else if (CurC <= FirstNonSpace(line) && CurC % IndW == 0 && CurC >= IndW && line.Substring(CurC - IndW, IndW) == new string(' ', IndW)) from = CurC - IndW;
            else if (CurC < line.Length && Pairs.IndexOf(line[CurC - 1]) >= 0 && Closes[Pairs.IndexOf(line[CurC - 1])] == line[CurC])
            { L[CurL] = line.Remove(CurC - 1, 2); CurC--; AncC = CurC; return; }
            L[CurL] = line.Remove(from, CurC - from); CurC = from; AncC = CurC;
        }

        void DeleteFwd()
        {
            if (HasSel) { DeleteSel(); return; }
            if (CurC < L[CurL].Length) L[CurL] = L[CurL].Remove(CurC, 1);
            else if (CurL < L.Count - 1) { L[CurL] += L[CurL + 1]; L.RemoveAt(CurL + 1); }
        }

        void NewLine()
        {
            if (!Py) { NewLineOther(); return; }
            DeleteSel();
            string line = L[CurL], before = line.Substring(0, CurC);
            int ind = FirstNonSpace(line); if (ind > CurC) ind = CurC;
            string trimmed = before.TrimEnd();
            if (trimmed.EndsWith(":")) ind += 4;
            else if (trimmed.StartsWith(new string(' ', ind) + "return") || trimmed.Trim() == "pass" || trimmed.Trim() == "break" || trimmed.Trim() == "continue") ind = Mathf.Max(0, ind - 4);
            char prev = CurC > 0 ? line[CurC - 1] : '\0', next = CurC < line.Length ? line[CurC] : '\0';
            if ((prev == '(' && next == ')') || (prev == '[' && next == ']') || (prev == '{' && next == '}'))
            {
                int baseInd = FirstNonSpace(line);
                Insert("\n" + new string(' ', baseInd + 4) + "\n" + new string(' ', baseInd));
                CurL--; CurC = baseInd + 4; AncL = CurL; AncC = CurC; return;
            }
            string after = line.Substring(CurC).TrimStart(' ');
            L[CurL] = before; L.Insert(CurL + 1, new string(' ', ind) + after);
            CurL++; CurC = ind; AncL = CurL; AncC = CurC;
        }

        // Enter для не-python: отступ после { [ ( (скобочные языки), после «:» в YAML, после открывающего тега в HTML/JSX,
        // после then/do/else в bash; между парными скобками/тегами — три строки с курсором в середине
        void NewLineOther()
        {
            DeleteSel();
            string line = L[CurL], before = line.Substring(0, CurC), after = line.Substring(CurC).TrimStart(' ');
            int w = IndW, ind = Mathf.Min(FirstNonSpace(line), CurC);
            string code = CodeBefore(CurL, CurC);
            char last = code.Length > 0 ? code[code.Length - 1] : '\0';
            char prev = CurC > 0 ? line[CurC - 1] : '\0', next = CurC < line.Length ? line[CurC] : '\0';
            bool braces = Syntax.UsesBraces(lang), markup = lang == "html" || lang == "jsx" || lang == "tsx" || lang == "javascript";
            char pair = last == '(' ? ')' : last == '[' ? ']' : last == '{' ? '}' : '\0';
            bool split = (prev == '(' && next == ')') || (prev == '[' && next == ']') || (prev == '{' && next == '}')
                || (braces && pair != '\0' && after.Length > 0 && after[0] == pair)
                || (markup && last == '>' && after.StartsWith("</") && OpensTag(code));
            if (split)
            {
                L[CurL] = before.TrimEnd(' ');
                L.Insert(CurL + 1, new string(' ', ind + w));
                L.Insert(CurL + 2, new string(' ', ind) + after);
                CurL++; CurC = ind + w; AncL = CurL; AncC = CurC; return;
            }
            if (braces && pair != '\0') ind += w;
            else if (lang == "yaml") ind = YamlIndent(code, ind, w);
            else if (markup && last == '>' && OpensTag(code)) ind += w;
            else if (lang == "bash" && System.Text.RegularExpressions.Regex.IsMatch(code, @"(^|[;\s])(then|do|else)$")) ind += w;
            L[CurL] = before; L.Insert(CurL + 1, new string(' ', ind) + after);
            CurL++; CurC = ind; AncL = CurL; AncC = CurC;
        }

        // строка кончается открывающим тегом: <div class="x">, <Foo>, <> (но не <br>, <a/>, </b>)
        static readonly System.Text.RegularExpressions.Regex OpenTagRx = new System.Text.RegularExpressions.Regex(@"<([A-Za-z][\w.:-]*)?(\s[^<>]*)?>$");
        static readonly HashSet<string> VoidTags = new HashSet<string> { "br", "hr", "img", "input", "meta", "link", "area", "base", "col", "embed", "source", "track", "wbr", "param" };
        static bool OpensTag(string code)
        {
            var m = OpenTagRx.Match(code);
            if (!m.Success || code.EndsWith("/>") || code.EndsWith("=>")) return false;
            if (!m.Groups[1].Success && m.Groups[2].Success) return false;
            return !VoidTags.Contains(m.Groups[1].Value.ToLowerInvariant());
        }

        // YAML: после «ключ:» и «ключ: |» — глубже ключа; после «- ключ: значение» — на уровень ключа (продолжаем элемент списка)
        int YamlIndent(string code, int ind, int w)
        {
            int kc = FirstNonSpace(code);
            while (kc + 1 < code.Length && code[kc] == '-' && code[kc + 1] == ' ') { kc += 2; while (kc < code.Length && code[kc] == ' ') kc++; }
            if (kc >= code.Length) return code.TrimStart().StartsWith("-") ? kc : ind;   // «- » без содержимого
            bool item = kc > FirstNonSpace(code);
            if (code.EndsWith(":") || System.Text.RegularExpressions.Regex.IsMatch(code, @"(:|^\s*-)\s+[|>][-+0-9]*$")) return kc + w;
            if (item && Syntax.YamlKeyEnd(code, kc, code.Length) >= 0) return kc;
            return ind;
        }

        void Tab()
        {
            if (HasSel && CurL != AncL) { IndentLines(IndW); return; }
            DeleteSel();
            int n = IndW - CurC % IndW; Insert(new string(' ', n));
        }
        void Unindent() { IndentLines(-IndW); }
        void IndentLines(int d)
        {
            int l0 = Mathf.Min(CurL, AncL), l1 = Mathf.Max(CurL, AncL);
            if (HasSel && l1 > l0 && (CurL > AncL ? CurC : AncC) == 0) l1--;
            for (int i = l0; i <= l1; i++)
            {
                if (d > 0) L[i] = new string(' ', d) + L[i];
                else { int k = Mathf.Min(-d, FirstNonSpace(L[i])); L[i] = L[i].Substring(k); }
            }
            CurC = Mathf.Clamp(CurC + d, 0, L[CurL].Length); AncC = Mathf.Clamp(AncC + d, 0, L[AncL].Length);
        }

        // Ctrl+/: строчный комментарий языка (# // --); в HTML и CSS каждая строка оборачивается в <!-- --> или /* */
        void ToggleComment()
        {
            string lc = Syntax.LineComment(lang), bo, bc;
            bool block = Syntax.BlockComment(lang, out bo, out bc);
            if (lc == null && !block) return;
            Push(false);
            int l0 = Mathf.Min(CurL, AncL), l1 = Mathf.Max(CurL, AncL);
            bool all = true; int minInd = int.MaxValue;
            for (int i = l0; i <= l1; i++)
            {
                if (L[i].Trim().Length == 0) continue;
                minInd = Mathf.Min(minInd, FirstNonSpace(L[i]));
                string t = L[i].Trim();
                if (lc != null ? !t.StartsWith(lc) : !(t.StartsWith(bo) && t.EndsWith(bc) && t.Length >= bo.Length + bc.Length)) all = false;
            }
            if (minInd == int.MaxValue) return;
            for (int i = l0; i <= l1; i++)
            {
                if (L[i].Trim().Length == 0) continue;
                if (lc != null)
                {
                    if (all) { int p = L[i].IndexOf(lc, StringComparison.Ordinal); int n = p + lc.Length < L[i].Length && L[i][p + lc.Length] == ' ' ? lc.Length + 1 : lc.Length; L[i] = L[i].Remove(p, n); if (i == CurL) CurC = Mathf.Max(0, CurC - n); }
                    else { L[i] = L[i].Insert(minInd, lc + " "); if (i == CurL) CurC += lc.Length + 1; }
                }
                else if (all)
                {
                    int p = L[i].IndexOf(bo, StringComparison.Ordinal), n = p + bo.Length < L[i].Length && L[i][p + bo.Length] == ' ' ? bo.Length + 1 : bo.Length;
                    L[i] = L[i].Remove(p, n); if (i == CurL && CurC > p) CurC = Mathf.Max(p, CurC - n);
                    string t = L[i].TrimEnd(); int e = t.Length - bc.Length, m = bc.Length;
                    if (e > 0 && t[e - 1] == ' ') { e--; m++; }
                    L[i] = t.Remove(e, m);
                }
                else
                {
                    L[i] = L[i].TrimEnd().Insert(minInd, bo + " ") + " " + bc;
                    if (i == CurL && CurC >= minInd) CurC += bo.Length + 1;
                }
            }
            CurC = Mathf.Clamp(CurC, 0, L[CurL].Length); AncC = Mathf.Clamp(AncC, 0, L[AncL].Length);
            Edited();
        }

        void Copy()
        {
            string s = HasSel ? Selected() : L[CurL] + "\n";
            GUIUtility.systemCopyBuffer = s;
        }

        // ---- перемещение ----
        Vector2Int Left() { return CurC > 0 ? new Vector2Int(CurL, CurC - 1) : CurL > 0 ? new Vector2Int(CurL - 1, L[CurL - 1].Length) : new Vector2Int(0, 0); }
        Vector2Int Right() { return CurC < L[CurL].Length ? new Vector2Int(CurL, CurC + 1) : CurL < L.Count - 1 ? new Vector2Int(CurL + 1, 0) : new Vector2Int(CurL, CurC); }
        Vector2Int WordLeft()
        {
            if (CurC == 0) return Left();
            string s = L[CurL]; int i = CurC;
            while (i > 0 && s[i - 1] == ' ') i--;
            if (i > 0 && IsWord(s[i - 1])) while (i > 0 && IsWord(s[i - 1])) i--;
            else if (i > 0) i--;
            return new Vector2Int(CurL, i);
        }
        Vector2Int WordRight()
        {
            string s = L[CurL]; int i = CurC;
            if (i >= s.Length) return Right();
            if (IsWord(s[i])) while (i < s.Length && IsWord(s[i])) i++;
            else i++;
            while (i < s.Length && s[i] == ' ') i++;
            return new Vector2Int(CurL, i);
        }
        void Move(Vector2Int p, bool extend) { wantCol = -1; SetCursor(p.x, p.y, extend); }
        void Vert(int d, bool extend)
        {
            if (wantCol < 0) wantCol = CurC;
            int nl = Mathf.Clamp(CurL + d, 0, L.Count - 1);
            int keep = wantCol; SetCursor(nl, Mathf.Min(keep, L[nl].Length), extend); wantCol = keep; HidePopup();
        }

        // ================= мышь =================
        bool dragging;
        public void MouseDown(Vector2 panelPos, int clicks, bool shift)
        {
            var lp = this.WorldToLocal(panelPos);
            HideTooltip();
            if (lp.x > layout.width - MinimapW) { MinimapJump(lp.y); return; }
            int line = Mathf.Clamp(Mathf.FloorToInt((lp.y + ScrollY) / LineH), 0, L.Count - 1);
            if (lp.x < Gutter)
            {
                int l1 = Mathf.FloorToInt((lp.y + ScrollY) / LineH) + 1;
                if (l1 >= 1 && l1 <= L.Count) { if (!Breakpoints.Remove(l1)) Breakpoints.Add(l1); gutterMarks.MarkDirtyRepaint(); if (BreakpointsChanged != null) BreakpointsChanged(); }
                return;
            }
            int col = ColAt(line, lp.x);
            HidePopup();
            if (clicks >= 3) { AncL = CurL = line; AncC = 0; CurC = L[line].Length; Place(); return; }
            if (clicks == 2)
            {
                string s = L[line]; int a = Mathf.Min(col, s.Length), b = a;
                while (a > 0 && IsWord(s[a - 1])) a--;
                while (b < s.Length && IsWord(s[b])) b++;
                AncL = CurL = line; AncC = a; CurC = b; Place(); return;
            }
            wantCol = -1; SetCursor(line, col, shift); dragging = true;
        }
        public void MouseDrag(Vector2 panelPos)
        {
            if (!dragging) return;
            var lp = this.WorldToLocal(panelPos);
            int line = Mathf.Clamp(Mathf.FloorToInt((lp.y + ScrollY) / LineH), 0, L.Count - 1);
            SetCursor(line, ColAt(line, lp.x), true);
        }
        public void MouseUp() { dragging = false; }
        int ColAt(int line, float x) { return Mathf.Clamp(Mathf.RoundToInt((x - Gutter - PadL + ScrollX) / CharW), 0, L[line].Length); }

        public void Wheel(float lines) { ScrollY += lines * LineH; ClampScroll(); Place(); HideTooltip(); }
        void MinimapJump(float y)
        {
            float line = y / MiniLine + MiniOffset() / MiniLine;
            ScrollY = line * LineH - viewport.layout.height * 0.5f; ClampScroll(); Place();
        }

        // Наведение: вызывается каждый кадр с позицией мыши на панели
        public void Hover(Vector2 panelPos, bool inside)
        {
            int gl = -1;
            if (PopupOpen) { HideTooltip(); inside = false; }   // открыт список автодополнения — подсказки не мешают
            if (inside && !dragging)
            {
                var lp = this.WorldToLocal(panelPos);
                if (lp.x >= 0 && lp.x < Gutter && lp.y >= 0) { int l1 = Mathf.FloorToInt((lp.y + ScrollY) / LineH) + 1; if (l1 <= L.Count) gl = l1; }
                string key = null; string text = null;
                if (lp.x > Gutter && lp.x < layout.width - MinimapW && lp.y >= 0 && lp.y < layout.height)
                {
                    int line = Mathf.FloorToInt((lp.y + ScrollY) / LineH);
                    if (line >= 0 && line < L.Count)
                    {
                        float cx = (lp.x - Gutter - PadL + ScrollX) / CharW; int col = Mathf.FloorToInt(cx);
                        string s = L[line];
                        if (col >= 0 && col < s.Length)
                        {
                            if (line + 1 == ErrorLine && ErrorText != null && col >= FirstNonSpace(s)) { key = "err" + line; text = ErrorText; }
                            else if (IsWord(s[col]))
                            {
                                int a = col, b = col; while (a > 0 && IsWord(s[a - 1])) a--; while (b < s.Length && IsWord(s[b])) b++;
                                string w = s.Substring(a, b - a); key = line + ":" + a;
                                string[] doc;
                                if (Py && PySyntax.Docs.TryGetValue(w, out doc)) text = "<color=#DCDCAA>" + K.Esc(doc[0]) + "</color>\n" + K.Esc(doc[1]);
                                else if (ExtraHover != null) text = ExtraHover(w);
                            }
                        }
                    }
                }
                if (key != hoverKey) { hoverKey = key; hoverSince = Time.unscaledTime; hoverPos = panelPos; HideTooltip(); }
                else if (key != null && text != null && tipBox.style.display == DisplayStyle.None && Time.unscaledTime - hoverSince > 0.45f) ShowTooltip(text, hoverPos);
            }
            else if (hoverKey != null) { hoverKey = null; HideTooltip(); }
            if (gl != hoverGutterLine) { hoverGutterLine = gl; gutterMarks.MarkDirtyRepaint(); }
        }

        void ShowTooltip(string text, Vector2 panelPos)
        {
            tipBox.Clear();
            var t = K.T(text, 15f, K.Text, false, false, true); t.style.whiteSpace = WhiteSpace.Normal; tipBox.Add(t);
            var lp = this.WorldToLocal(panelPos);
            tipBox.style.left = Mathf.Clamp(lp.x - 20, 4, Mathf.Max(4, layout.width - 540));
            tipBox.style.top = lp.y + LineH * 0.9f > layout.height - 120 ? lp.y - 110 : lp.y + LineH * 0.8f;
            tipBox.style.display = DisplayStyle.Flex;
        }
        public void HideTooltip() { tipBox.style.display = DisplayStyle.None; }

        // ================= автодополнение =================
        string WordBefore()
        {
            string s = L[CurL]; int a = CurC; while (a > 0 && IsWord(s[a - 1])) a--;
            return s.Substring(a, CurC - a);
        }
        void ShowCompletions(bool force)
        {
            if (InStringOrComment(CurL, CurC)) { HidePopup(); return; }
            string w = WordBefore();
            if (!force && w.Length < 1) { HidePopup(); return; }
            items = Syntax.Complete(lang, w, Text);
            if (items.Count == 0) { HidePopup(); return; }
            itemSel = 0; RenderPopup();
        }
        void UpdatePopupAfterEdit() { if (PopupOpen) ShowCompletions(false); }
        public void HidePopup() { popup.style.display = DisplayStyle.None; }

        bool HandlePopupKey(Event e)
        {
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: itemSel = (itemSel + items.Count - 1) % items.Count; RenderPopup(); return true;
                case KeyCode.DownArrow: itemSel = (itemSel + 1) % items.Count; RenderPopup(); return true;
                case KeyCode.Return: case KeyCode.KeypadEnter: case KeyCode.Tab: Accept(items[itemSel]); return true;
                case KeyCode.Escape: HidePopup(); return true;
            }
            if (e.character == '\t' || e.character == '\n') return true;
            return false;
        }

        void Accept(Completion c)
        {
            Push(false);
            AncL = CurL;   // выделение, сделанное при открытом списке (Ctrl+A), не должно ломать вставку
            string w = WordBefore();
            L[CurL] = L[CurL].Remove(CurC - w.Length, w.Length); CurC -= w.Length; AncC = CurC;
            string ins = c.Insert;
            // не удваиваем скобки, если после курсора уже есть «(»
            if (ins.EndsWith("()") && CurC < L[CurL].Length && L[CurL][CurC] == '(') { ins = ins.Substring(0, ins.Length - 2); }
            Insert(ins);
            int back = ins == c.Insert ? c.CaretBack : 0;
            CurC = Mathf.Max(0, CurC - back); AncC = CurC;
            if (ins.EndsWith("()") && back == 1) { parenGuardL = CurL; parenGuardC = CurC; }
            HidePopup(); Edited();
        }

        void RenderPopup()
        {
            popup.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = K.Box(true); row.style.height = 26f; row.style.alignItems = Align.Center; K.Pad(row, 0f, 10f, 0f, 6f);
                if (i == itemSel) row.style.backgroundColor = Pal.Hex("04395E");
                var ic = K.T(it.Icon.ToString(), 14f, it.IconColor, true); ic.style.width = 20f; ic.style.unityTextAlign = TextAnchor.MiddleCenter; row.Add(ic);
                var lb = K.T(K.Esc(it.Label), 16f, i == itemSel ? Color.white : K.Text, true); lb.style.marginLeft = 4f; row.Add(lb);
                row.Add(K.Spacer());
                if (!string.IsNullOrEmpty(it.Detail)) { var dt = K.T(K.Esc(it.Detail), 13f, K.Muted); dt.style.marginLeft = 16f; row.Add(dt); }
                popup.Add(row);
            }
            var sel = items[itemSel];
            if (!string.IsNullOrEmpty(sel.Doc))
            {
                var d = K.T(K.Esc(sel.Doc), 14f, K.Muted, false, false, true); d.style.whiteSpace = WhiteSpace.Normal; d.style.maxWidth = 460f;
                K.Pad(d, 6f, 10f, 8f, 10f); K.Line(d, Pal.Hex("454545"), 1f, 0f, 0f, 0f); popup.Add(d);
            }
            float x = Gutter + PadL + CurC * CharW - ScrollX - 28, y = (CurL + 1) * LineH - ScrollY + 2;
            popup.style.left = Mathf.Max(0, x); popup.style.minWidth = 380f;
            float h = items.Count * 26 + (string.IsNullOrEmpty(sel.Doc) ? 0 : 60);
            popup.style.top = y + h > layout.height - 8 && y - LineH - h > 0 ? y - LineH - h - 4 : y;
            popup.style.display = DisplayStyle.Flex;
        }

        // ================= раскладка и отрисовка =================
        void Reveal()
        {
            float vh = viewport.layout.height, vw = viewport.layout.width;
            if (float.IsNaN(vh) || vh <= 0) return;
            float cy = CurL * LineH;
            if (cy < ScrollY) ScrollY = cy;
            if (cy + LineH > ScrollY + vh) ScrollY = cy + LineH - vh;
            float cx = PadL + CurC * CharW;
            if (cx < ScrollX + PadL) ScrollX = Mathf.Max(0, cx - PadL - 40);
            if (cx > ScrollX + vw - 30) ScrollX = cx - vw + 80;
            ClampScroll();
        }
        void ClampScroll()
        {
            float vh = viewport.layout.height; if (float.IsNaN(vh)) return;
            ScrollY = Mathf.Clamp(ScrollY, 0, Mathf.Max(0, (L.Count - 1) * LineH));
            ScrollX = Mathf.Max(0, ScrollX);
        }

        void Rebuild()
        {
            textDirty = false;
            spans.Clear(); lineStates.Clear();
            string st = null;
            for (int i = 0; i < L.Count; i++) { lineStates.Add(st); spans.Add(Syntax.Line(lang, L[i], ref st)); }
            while (labels.Count < L.Count)
            {
                var lb = K.T("", FontSize, K.Text, true); lb.style.whiteSpace = WhiteSpace.Pre; lb.style.position = Position.Absolute; lb.style.height = LineH;
                lb.style.left = PadL; labels.Add(lb); linesBox.Add(lb);
                var nb = K.T("", FontSize, K.Dim, true); nb.style.position = Position.Absolute; nb.style.width = Gutter - 26; nb.style.left = 0f; nb.style.height = LineH;
                nb.style.unityTextAlign = TextAnchor.MiddleRight; numbers.Add(nb); gutter.Add(nb);
            }
            for (int i = 0; i < labels.Count; i++)
            {
                bool on = i < L.Count;
                labels[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None; numbers[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on) continue;
                labels[i].text = PySyntax.Rich(L[i], spans[i]);
                labels[i].style.top = i * LineH;
                numbers[i].text = (i + 1).ToString();
            }
            content.style.height = L.Count * LineH + 400;
            minimap.MarkDirtyRepaint(); squiggles.MarkDirtyRepaint();
        }

        public void Place()
        {
            if (textDirty) Rebuild();
            content.style.left = -ScrollX; content.style.top = -ScrollY;
            for (int i = 0; i < numbers.Count && i < L.Count; i++)
            {
                numbers[i].style.top = i * LineH - ScrollY;
                numbers[i].style.color = i == CurL && Active ? K.Text : K.Dim;
            }
            curLine.style.top = CurL * LineH; curLine.style.display = Active && !HasSel ? DisplayStyle.Flex : DisplayStyle.None;
            caret.style.left = PadL + CurC * CharW - 1; caret.style.top = CurL * LineH + 2;
            debugBg.style.display = DebugLine > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (DebugLine > 0) { debugBg.style.top = (DebugLine - 1) * LineH; debugBg.style.backgroundColor = DebugIsError ? new Color(0.9f, 0.2f, 0.2f, 0.28f) : K.DebugLine; }
            // выделение
            selLayer.Clear();
            if (HasSel)
            {
                int l0, c0, l1, c1; SelRange(out l0, out c0, out l1, out c1);
                for (int i = l0; i <= l1; i++)
                {
                    int a = i == l0 ? c0 : 0, b = i == l1 ? c1 : L[i].Length + (i < l1 ? 1 : 0);
                    if (b <= a) continue;
                    var r = new VisualElement(); r.style.position = Position.Absolute; r.style.left = PadL + a * CharW; r.style.width = (b - a) * CharW;
                    r.style.top = i * LineH; r.style.height = LineH; r.style.backgroundColor = Active ? K.Selection : Pal.Hex("3A3D41"); r.pickingMode = PickingMode.Ignore;
                    selLayer.Add(r);
                }
            }
            // полоса прокрутки
            float vh = viewport.layout.height, total = (L.Count + 8) * LineH;
            if (!float.IsNaN(vh) && total > vh) { vthumb.style.display = DisplayStyle.Flex; float th = Mathf.Max(30, vh * vh / total); vthumb.style.height = th; vthumb.style.top = (vh - th) * Mathf.Clamp01(ScrollY / Mathf.Max(1, total - vh)); }
            else vthumb.style.display = DisplayStyle.None;
            gutterMarks.MarkDirtyRepaint(); minimap.MarkDirtyRepaint(); squiggles.MarkDirtyRepaint();
        }

        public void Tick(float dt)
        {
            if (textDirty) Place();
            blink += dt;
            bool show = Active && !ReadOnly && (blink % 1.06f) < 0.6f;
            caret.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public float SinceEdit { get { return Time.unscaledTime - lastEdit; } }

        void DrawGutterMarks(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float r = LineH * 0.2f, x = 12f;
            foreach (var b in Breakpoints)
            {
                float y = (b - 1) * LineH - ScrollY + LineH / 2; if (y < -LineH || y > gutter.layout.height + LineH) continue;
                p.fillColor = K.Breakpoint; p.BeginPath(); p.Arc(new Vector2(x, y), r, 0f, 360f); p.Fill();
            }
            if (hoverGutterLine > 0 && !Breakpoints.Contains(hoverGutterLine))
            {
                float y = (hoverGutterLine - 1) * LineH - ScrollY + LineH / 2;
                p.fillColor = new Color(K.Breakpoint.r, K.Breakpoint.g, K.Breakpoint.b, 0.4f); p.BeginPath(); p.Arc(new Vector2(x, y), r, 0f, 360f); p.Fill();
            }
            if (DebugLine > 0)
            {
                float y = (DebugLine - 1) * LineH - ScrollY + LineH / 2, s = LineH * 0.26f, ax = Gutter - 16;
                p.fillColor = DebugIsError ? K.Red : Pal.Hex("FFCC00");
                p.BeginPath(); p.MoveTo(new Vector2(ax - s, y - s * 0.7f)); p.LineTo(new Vector2(ax, y - s * 0.7f)); p.LineTo(new Vector2(ax + s * 0.8f, y));
                p.LineTo(new Vector2(ax, y + s * 0.7f)); p.LineTo(new Vector2(ax - s, y + s * 0.7f)); p.ClosePath(); p.Fill();
            }
        }

        void DrawSquiggles(MeshGenerationContext ctx)
        {
            if (ErrorLine < 1 || ErrorLine > L.Count) return;
            string s = L[ErrorLine - 1]; int a = FirstNonSpace(s); if (a >= s.Length) { a = 0; s = "    "; }
            float x0 = PadL + a * CharW, x1 = PadL + Mathf.Max(s.TrimEnd().Length, a + 2) * CharW, y = ErrorLine * LineH - 4;
            var p = ctx.painter2D; p.strokeColor = K.Red; p.lineWidth = 1.4f; p.BeginPath(); p.MoveTo(new Vector2(x0, y));
            bool up = true; for (float x = x0 + 3; x <= x1; x += 3) { p.LineTo(new Vector2(x, up ? y - 2.5f : y)); up = !up; }
            p.Stroke();
        }

        float MiniOffset()
        {
            float mh = minimap.layout.height; if (float.IsNaN(mh)) return 0;
            float total = L.Count * MiniLine;
            if (total <= mh) return 0;
            float frac = ScrollY / Mathf.Max(1, (L.Count - 1) * LineH);
            return (total - mh) * Mathf.Clamp01(frac);
        }

        void DrawMinimap(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; float off = MiniOffset();
            for (int i = 0; i < L.Count && i < spans.Count; i++)
            {
                float y = i * MiniLine - off + 4; if (y < -MiniLine || y > minimap.layout.height) continue;
                foreach (var sp in spans[i])
                {
                    if (sp.Kind == Tok.Op) continue;
                    var c = PySyntax.ColorOf(sp.Kind); c.a = 0.75f; p.fillColor = c;
                    float x = 8 + sp.Start * MiniChar, w = Mathf.Min(sp.Len * MiniChar, MinimapW - 10 - x);
                    if (w <= 0) continue;
                    p.BeginPath(); p.MoveTo(new Vector2(x, y)); p.LineTo(new Vector2(x + w, y)); p.LineTo(new Vector2(x + w, y + MiniLine * 0.7f)); p.LineTo(new Vector2(x, y + MiniLine * 0.7f)); p.ClosePath(); p.Fill();
                }
            }
            float vh = viewport.layout.height; if (float.IsNaN(vh)) return;
            float top = ScrollY / LineH * MiniLine - off + 4, hgt = vh / LineH * MiniLine;
            p.fillColor = new Color(1, 1, 1, 0.07f);
            p.BeginPath(); p.MoveTo(new Vector2(1, top)); p.LineTo(new Vector2(MinimapW, top)); p.LineTo(new Vector2(MinimapW, top + hgt)); p.LineTo(new Vector2(1, top + hgt)); p.ClosePath(); p.Fill();
            if (ErrorLine > 0)
            {
                float y = (ErrorLine - 1) * MiniLine - off + 4; p.fillColor = K.Red;
                p.BeginPath(); p.MoveTo(new Vector2(MinimapW - 6, y)); p.LineTo(new Vector2(MinimapW, y)); p.LineTo(new Vector2(MinimapW, y + 4)); p.LineTo(new Vector2(MinimapW - 6, y + 4)); p.ClosePath(); p.Fill();
            }
        }

        // Уточнение ширины символа по реальной вёрстке (вызывается, когда метка с образцом получила размер)
        public void CalibrateCharWidth(float measuredWidthOf100)
        {
            if (measuredWidthOf100 > 100 && Mathf.Abs(measuredWidthOf100 / 100f - CharW) > 0.01f) { CharW = measuredWidthOf100 / 100f; textDirty = true; Place(); }
        }
    }
}
