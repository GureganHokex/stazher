// Подсветка синтаксиса и автодополнение для всех языков задач. Python — через PySyntax (поведение то же),
// остальные — лёгкие построчные токенайзеры в духе грамматик VS Code; токены — общий Tok, цвета темы Dark+
// берёт PySyntax.ColorOf/Rich.
// state — непрозрачная строка, переносит многострочные конструкции на следующую строку: блочные комментарии,
// шаблонные строки и разметку JSX, комментарии и <script>/<style> в HTML, heredoc, блочные скаляры YAML.
// Для первой строки документа state = null; строки разбираются по порядку с одной и той же переменной.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Intern.Game
{
    public static class Syntax
    {
        // ================= языки =================
        public static readonly string[] Languages = { "python", "javascript", "typescript", "jsx", "tsx", "sql", "yaml", "bash", "dockerfile", "html", "css", "nginx", "hcl", "promql", "text" };

        // Каноническое имя языка: «js» → javascript, «tf» → hcl…; null, пустое и неизвестное — «text» (без подсветки)
        public static string Norm(string lang)
        {
            if (string.IsNullOrEmpty(lang)) return "text";
            switch (lang)
            {
                case "python": case "javascript": case "typescript": case "jsx": case "tsx": case "sql": case "yaml": case "bash":
                case "dockerfile": case "html": case "css": case "nginx": case "hcl": case "promql": case "text": return lang;
            }
            switch (lang.Trim().ToLowerInvariant())
            {
                case "python": case "py": case "python3": return "python";
                case "javascript": case "js": case "node": case "nodejs": case "mjs": case "cjs": return "javascript";
                case "typescript": case "ts": return "typescript";
                case "jsx": return "jsx";
                case "tsx": return "tsx";
                case "sql": case "postgresql": case "postgres": case "psql": case "mysql": case "sqlite": return "sql";
                case "yaml": case "yml": return "yaml";
                case "bash": case "sh": case "shell": case "zsh": return "bash";
                case "dockerfile": case "docker": return "dockerfile";
                case "html": case "htm": case "xml": return "html";
                case "css": case "scss": case "less": return "css";
                case "nginx": case "nginxconf": return "nginx";
                case "hcl": case "terraform": case "tf": return "hcl";
                case "promql": case "prometheus": return "promql";
                default: return "text";
            }
        }

        public static bool IsJs(string lang) { lang = Norm(lang); return lang == "javascript" || lang == "typescript" || lang == "jsx" || lang == "tsx"; }

        // Ширина отступа: 2 пробела для веба, YAML и Terraform, иначе 4
        public static int IndentWidth(string lang)
        {
            switch (Norm(lang))
            {
                case "yaml": case "html": case "css": case "javascript": case "typescript": case "jsx": case "tsx": case "hcl": return 2;
                default: return 4;
            }
        }

        // Строчный комментарий для Ctrl+/ (null — у языка только блочные или никаких)
        public static string LineComment(string lang)
        {
            switch (Norm(lang))
            {
                case "python": case "yaml": case "bash": case "dockerfile": case "nginx": case "promql": case "hcl": return "#";
                case "javascript": case "typescript": case "jsx": case "tsx": return "//";
                case "sql": return "--";
                default: return null;
            }
        }

        public static bool BlockComment(string lang, out string open, out string close)
        {
            switch (Norm(lang))
            {
                case "html": open = "<!--"; close = "-->"; return true;
                case "css": case "javascript": case "typescript": case "jsx": case "tsx": case "hcl": case "sql": open = "/*"; close = "*/"; return true;
                default: open = close = null; return false;
            }
        }

        // Символ слова (двойной щелчок, Ctrl+стрелки, префикс автодополнения). Для python — ровно PySyntax.IsId.
        public static bool IsWordChar(string lang, char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_') return true;
            switch (Norm(lang))
            {
                case "javascript": case "typescript": case "jsx": case "tsx": return c == '$';
                case "css": case "html": case "yaml": case "hcl": return c == '-';
                default: return false;
            }
        }

        // Языки со скобочными блоками: отступ после { [ ( и выравнивание закрывающей скобки по парной
        // (html — ради <script> и <style>)
        public static bool UsesBraces(string lang)
        {
            switch (Norm(lang))
            {
                case "javascript": case "typescript": case "jsx": case "tsx": case "css": case "hcl": case "nginx": case "bash": case "sql": case "promql": case "html": return true;
                default: return false;
            }
        }

        // ================= разбор строки =================
        public static List<Span> Line(string lang, string s, ref string state)
        {
            s = s ?? "";
            var r = new List<Span>();
            switch (Norm(lang))
            {
                case "python": return PySyntax.Line(s, ref state);
                case "javascript": case "jsx": Js(s, 0, s.Length, r, ref state, false, true); break;
                case "typescript": Js(s, 0, s.Length, r, ref state, true, false); break;
                case "tsx": Js(s, 0, s.Length, r, ref state, true, true); break;
                case "sql": Sql(s, r, ref state); break;
                case "yaml": Yaml(s, r, ref state); break;
                case "bash": Bash(s, 0, r, ref state); break;
                case "dockerfile": Docker(s, r, ref state); break;
                case "html": Html(s, r, ref state); break;
                case "css": Css(s, 0, s.Length, r, ref state); break;
                case "nginx": Nginx(s, r, ref state); break;
                case "hcl": Hcl(s, r, ref state); break;
                case "promql": Prom(s, r); state = null; break;
                default: state = null; break;
            }
            return r;
        }

        // ---------- общие помощники ----------
        static void Add(List<Span> r, int a, int b, Tok k) { if (b > a) r.Add(new Span(a, b - a, k)); }
        static bool Dig(char c) { return c >= '0' && c <= '9'; }
        static bool IdStart(char c) { return char.IsLetter(c) || c == '_'; }
        static bool Id(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
        static bool Sp(char c) { return c == ' ' || c == '\t'; }
        static int SkipSp(string s, int i, int n) { while (i < n && Sp(s[i])) i++; return i; }
        // один «символ»: суррогатная пара (эмодзи) не разрезается между двумя цветами
        static int One(string s, int i, int n) { return char.IsHighSurrogate(s[i]) && i + 1 < n && char.IsLowSurrogate(s[i + 1]) ? i + 2 : i + 1; }
        // позиция после закрывающей кавычки (или конец строки, если её нет)
        static int StrEnd(string s, int i, int n, char q, bool esc)
        {
            int j = i + 1;
            while (j < n && s[j] != q) { if (esc && s[j] == '\\') j++; j++; }
            return Math.Min(n, j + 1);
        }
        // число с суффиксами/единицами: 0x1F, 1_000, 2.5e-3, 10n, 16px
        static int NumEnd(string s, int i, int n)
        {
            bool hex = s[i] == '0' && i + 1 < n && "xXbBoO".IndexOf(s[i + 1]) >= 0;
            int j = i;
            while (j < n)
            {
                char c = s[j];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.') j++;
                else if ((c == '+' || c == '-') && !hex && j > i && (s[j - 1] == 'e' || s[j - 1] == 'E') && j + 1 < n && Dig(s[j + 1])) j++;
                else break;
            }
            return j;
        }
        static bool ContinuesLine(string s) { int e = s.Length - 1; while (e >= 0 && Sp(s[e])) e--; return e >= 0 && s[e] == '\\'; }

        // ================= JavaScript / TypeScript / JSX / TSX =================
        static readonly HashSet<string> JsControl = new HashSet<string> { "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue", "return",
            "throw", "try", "catch", "finally", "import", "export", "from", "await", "yield", "with", "as" };
        static readonly HashSet<string> JsKw = new HashSet<string> { "var", "let", "const", "function", "class", "extends", "new", "delete", "typeof", "instanceof",
            "in", "of", "void", "true", "false", "null", "undefined", "super", "debugger", "async", "static", "get", "set", "NaN", "Infinity" };
        static readonly HashSet<string> JsSoft = new HashSet<string> { "async", "static", "get", "set", "of" };   // ключевые, только если дальше идёт имя
        static readonly HashSet<string> TsKw = new HashSet<string> { "interface", "type", "enum", "namespace", "declare", "abstract", "implements", "private", "public",
            "protected", "readonly", "keyof", "infer", "is", "satisfies", "override" };
        static readonly HashSet<string> TsTypes = new HashSet<string> { "string", "number", "boolean", "any", "unknown", "never", "object", "symbol", "bigint" };
        static readonly HashSet<string> JsClasses = new HashSet<string> { "console", "Math", "JSON", "Object", "Array", "String", "Number", "Boolean", "Promise", "Map", "Set",
            "WeakMap", "WeakSet", "Date", "Error", "TypeError", "RangeError", "RegExp", "Symbol", "BigInt", "Intl", "Reflect", "Proxy", "document", "window", "globalThis",
            "process", "Buffer", "localStorage", "sessionStorage", "navigator", "React" };
        static readonly HashSet<string> JsFuncs = new HashSet<string> { "parseInt", "parseFloat", "isNaN", "isFinite", "setTimeout", "setInterval", "clearTimeout",
            "clearInterval", "fetch", "alert", "require", "structuredClone", "queueMicrotask", "encodeURIComponent", "decodeURIComponent" };
        static readonly HashSet<string> JsExprAfter = new HashSet<string> { "return", "typeof", "case", "do", "else", "in", "of", "new", "delete", "void", "throw",
            "yield", "await", "instanceof", "default", "export" };
        static readonly Regex JsArrow = new Regex(@"\G\s*=\s*(?:async\s*)?(?:\([^()]*\)|[A-Za-z_$][\w$]*)\s*(?::[^=]*)?=>|\G\s*=\s*(?:async\s+)?function\b");

        static bool JsIdStart(char c) { return char.IsLetter(c) || c == '_' || c == '$'; }
        static bool JsId(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$'; }
        static bool JsxName(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '$' || c == '-' || c == '.' || c == ':'; }
        static bool IsPascal(string w)
        {
            if (w.Length < 2 || !char.IsUpper(w[0])) return false;
            foreach (char c in w) if (char.IsLower(c)) return true;
            return false;
        }

        // Разбор s[from..to). Стек режимов (верхний — последний символ state):
        //   T — текст шаблонной строки `…`, E — выражение ${…} в ней, B — вложенные { } внутри E/X,
        //   A — атрибуты открывающего JSX-тега, J — дети JSX-элемента, X — выражение {…} в JSX, * — блочный комментарий.
        static void Js(string s, int from, int to, List<Span> r, ref string state, bool ts, bool jsx)
        {
            var st = new StringBuilder(state ?? "");
            int i = from, n = to;
            bool expr = true, dot = false;   // expr: здесь может начаться выражение (регулярка, JSX); dot: слово после «.»
            string prev = null;              // предыдущее слово: function, class, new…
            while (i < n)
            {
                char top = st.Length > 0 ? st[st.Length - 1] : '\0';
                char c = s[i];
                if (top == '*')
                {
                    int e = s.IndexOf("*/", i, n - i, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); i = n; break; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; st.Length--; continue;
                }
                if (top == 'T') { i = JsTemplate(s, i, i, n, r, st, ref expr); prev = null; dot = false; continue; }
                if (top == 'A')   // <Tag attr="…" attr={…} …>
                {
                    if (Sp(c)) { i++; continue; }
                    if (c == '/' && i + 1 < n && s[i + 1] == '>') { Add(r, i, i + 2, Tok.Op); i += 2; st.Length--; expr = false; prev = null; dot = false; continue; }
                    if (c == '>') { Add(r, i, i + 1, Tok.Op); i++; st[st.Length - 1] = 'J'; continue; }
                    if (c == '{') { Add(r, i, i + 1, Tok.Keyword); i++; st.Append('X'); expr = true; prev = null; dot = false; continue; }
                    if (c == '"' || c == '\'') { int j = StrEnd(s, i, n, c, false); Add(r, i, j, Tok.Str); i = j; continue; }
                    if (c == '/' && i + 1 < n && s[i + 1] == '/') { Add(r, i, n, Tok.Comment); i = n; break; }
                    if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = JsBlock(s, i, n, r, st); continue; }
                    if (JsxName(c)) { int j = i; while (j < n && JsxName(s[j])) j++; Add(r, i, j, Tok.Text); i = j; continue; }
                    int o = One(s, i, n); Add(r, i, o, c == '=' ? Tok.Op : Tok.Text); i = o; continue;
                }
                if (top == 'J')   // дети элемента: текст, {выражения}, вложенные теги
                {
                    if (c == '{') { Add(r, i, i + 1, Tok.Keyword); i++; st.Append('X'); expr = true; prev = null; dot = false; continue; }
                    if (c == '<' && i + 1 < n && s[i + 1] == '/')
                    {
                        int j = SkipSp(s, i + 2, n), k = j; while (k < n && JsxName(s[k])) k++;
                        Add(r, i, i + 2, Tok.Op); Add(r, j, k, TagTok(s, j, k));
                        int m = SkipSp(s, k, n); if (m < n && s[m] == '>') { Add(r, m, m + 1, Tok.Op); m++; }
                        i = m; st.Length--; expr = false; continue;
                    }
                    if (c == '<' && JsxTagAhead(s, i, n)) { i = JsxOpen(s, i, n, r, st); continue; }
                    i++; while (i < n && s[i] != '<' && s[i] != '{') i++;   // текст — цветом по умолчанию
                    continue;
                }
                // ---- код ----
                if (Sp(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { Add(r, i, n, Tok.Comment); i = n; break; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = JsBlock(s, i, n, r, st); continue; }
                if (c == '"' || c == '\'') { int j = StrEnd(s, i, n, c, true); Add(r, i, j, Tok.Str); i = j; expr = false; prev = null; dot = false; continue; }
                if (c == '`') { st.Append('T'); i = JsTemplate(s, i, i + 1, n, r, st, ref expr); prev = null; dot = false; continue; }
                if (Dig(c) || (c == '.' && i + 1 < n && Dig(s[i + 1])))
                {
                    int j = NumEnd(s, i, n); Add(r, i, j, Tok.Num); i = j; expr = false; prev = null; dot = false; continue;
                }
                if (JsIdStart(c))
                {
                    int j = i; while (j < n && JsId(s[j])) j++;
                    string w = s.Substring(i, j - i);
                    int k = SkipSp(s, j, n);
                    char nx = k < n ? s[k] : '\0';
                    bool call = nx == '(' || (ts && GenericCall(s, j, n));
                    bool idNext = k > j && k < n && JsIdStart(nx);
                    Tok t;
                    if (dot) t = call ? Tok.Func : Tok.Text;
                    else if (prev == "function") t = Tok.Def;
                    else if (prev == "class" || prev == "extends" || prev == "implements" || prev == "new" || (ts && (prev == "interface" || prev == "enum" || prev == "type" || prev == "namespace"))) t = Tok.Class;
                    else if (w == "this") t = Tok.Self;
                    else if (JsControl.Contains(w)) t = Tok.Control;
                    else if (JsKw.Contains(w) && (!JsSoft.Contains(w) || idNext || (w == "async" && nx == '('))) t = Tok.Keyword;
                    else if (ts && TsKw.Contains(w) && idNext) t = Tok.Keyword;
                    else if (ts && TsTypes.Contains(w) && !call) t = Tok.Class;
                    else if (call) t = JsFuncs.Contains(w) ? Tok.Builtin : Tok.Func;
                    else if (JsClasses.Contains(w) || IsPascal(w)) t = Tok.Class;
                    else if (nx == '=' && (prev == "const" || prev == "let" || prev == "var") && JsArrow.IsMatch(s, j)) t = Tok.Def;
                    else t = Tok.Text;
                    Add(r, i, j, t);
                    expr = JsExprAfter.Contains(w); dot = false; prev = w; i = j; continue;
                }
                if (c == '{') { if (top == 'E' || top == 'B' || top == 'X') st.Append('B'); Add(r, i, i + 1, Tok.Text); i++; expr = true; prev = null; dot = false; continue; }
                if (c == '}')
                {
                    if (top == 'E' || top == 'X') { Add(r, i, i + 1, Tok.Keyword); st.Length--; i++; expr = false; prev = null; dot = false; continue; }
                    if (top == 'B') st.Length--;
                    Add(r, i, i + 1, Tok.Text); i++; expr = true; prev = null; dot = false; continue;
                }
                if (c == '<' && jsx && expr && JsxTagAhead(s, i, n)) { i = JsxOpen(s, i, n, r, st); prev = null; dot = false; continue; }
                if (c == '/' && expr)
                {
                    int j = RegexEnd(s, i, n);
                    if (j > 0) { Add(r, i, j, Tok.Str); i = j; expr = false; prev = null; dot = false; continue; }
                }
                const string ops = "+-*/%=<>!&|^~?:";
                if (ops.IndexOf(c) >= 0)
                {
                    int o = i;
                    while (o < n && ops.IndexOf(s[o]) >= 0)
                    {
                        if (o > i && s[o] == '/' && o + 1 < n && (s[o + 1] == '/' || s[o + 1] == '*')) break;
                        if (o > i && jsx && s[o] == '<' && JsxTagAhead(s, o, n)) break;
                        o++;
                    }
                    Add(r, i, o, Tok.Op);
                    bool post = o - i == 2 && (s[i] == '+' || s[i] == '-') && s[i + 1] == s[i];   // x++ — дальше не выражение
                    if (!post) expr = true;
                    i = o; prev = null; dot = false; continue;
                }
                int one = One(s, i, n);
                Add(r, i, one, Tok.Text);
                dot = c == '.'; expr = c == '(' || c == '[' || c == ',' || c == ';';
                prev = null; i = one;
            }
            state = st.Length > 0 ? st.ToString() : null;
        }

        static int JsBlock(string s, int i, int n, List<Span> r, StringBuilder st)
        {
            int e = s.IndexOf("*/", i + 2, n - i - 2, StringComparison.Ordinal);
            if (e < 0) { Add(r, i, n, Tok.Comment); st.Append('*'); return n; }
            Add(r, i, e + 2, Tok.Comment); return e + 2;
        }

        // Текст шаблонной строки с позиции i (цвет строки начинается с a). Верх стека — 'T'.
        static int JsTemplate(string s, int a, int i, int n, List<Span> r, StringBuilder st, ref bool expr)
        {
            while (i < n)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '`') { Add(r, a, i + 1, Tok.Str); st.Length--; expr = false; return i + 1; }
                if (c == '$' && i + 1 < n && s[i + 1] == '{') { Add(r, a, i, Tok.Str); Add(r, i, i + 2, Tok.Keyword); st.Append('E'); expr = true; return i + 2; }
                i++;
            }
            if (i > n) i = n;
            Add(r, a, i, Tok.Str);
            return i;
        }

        // «<» открывает JSX-тег: <div …, <Foo>, <a/>, <> ; но не «a < b» и не «<T,>»
        static bool JsxTagAhead(string s, int i, int n)
        {
            if (i + 1 >= n) return false;
            char c = s[i + 1];
            if (c == '>') return true;
            if (!char.IsLetter(c)) return false;
            int j = i + 1; while (j < n && JsxName(s[j])) j++;
            if (j >= n) return true;
            char d = s[j];
            return Sp(d) || d == '>' || (d == '/' && j + 1 < n && s[j + 1] == '>');
        }

        static int JsxOpen(string s, int i, int n, List<Span> r, StringBuilder st)
        {
            if (s[i + 1] == '>') { Add(r, i, i + 2, Tok.Op); st.Append('J'); return i + 2; }
            int j = i + 1; while (j < n && JsxName(s[j])) j++;
            Add(r, i, i + 1, Tok.Op); Add(r, i + 1, j, TagTok(s, i + 1, j)); st.Append('A');
            return j;
        }

        // теги: div — как ключевое слово, компоненты (Foo, Ctx.Provider) — как класс
        static Tok TagTok(string s, int a, int b) { return b > a && (char.IsUpper(s[a]) || s.IndexOf('.', a, b - a) >= 0) ? Tok.Class : Tok.Keyword; }

        // name<Тип, …>( — вызов с явными параметрами типа (TS)
        static bool GenericCall(string s, int j, int n)
        {
            if (j >= n || s[j] != '<') return false;
            int d = 0;
            for (int m = j; m < n && m < j + 160; m++)
            {
                char c = s[m];
                if (c == '<') d++;
                else if (c == '>' && s[m - 1] != '=')
                {
                    d--;
                    if (d == 0) { int q = SkipSp(s, m + 1, n); return q < n && s[q] == '('; }
                }
            }
            return false;
        }

        // /регулярка/флаги — конец или -1, если это не регулярка
        static int RegexEnd(string s, int i, int n)
        {
            int j = i + 1;
            if (j >= n || s[j] == ' ' || s[j] == '*' || s[j] == '/') return -1;
            bool cls = false;
            while (j < n)
            {
                char c = s[j];
                if (c == '\\') { j += 2; continue; }
                if (c == '[') cls = true;
                else if (c == ']') cls = false;
                else if (c == '/' && !cls) break;
                j++;
            }
            if (j >= n) return -1;
            j++; while (j < n && char.IsLetter(s[j])) j++;
            return j;
        }

        // ================= HTML =================
        // state: null — текст; "!" — внутри <!-- -->; "t:тег" — внутри открывающего тега; "s:…"/"c:…" — тело <script>/<style>
        static readonly Regex HtmlEntity = new Regex(@"\G&(?:#\d+|#[xX][0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);");
        static bool HtmlName(char c) { return char.IsLetterOrDigit(c) || c == '-' || c == ':' || c == '_' || c == '.'; }

        static void Html(string s, List<Span> r, ref string state)
        {
            int i = 0, n = s.Length;
            string st = state;
            while (i < n)
            {
                if (st == "!")
                {
                    int e = s.IndexOf("-->", i, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); i = n; break; }
                    Add(r, i, e + 3, Tok.Comment); i = e + 3; st = null; continue;
                }
                if (st != null && (st.StartsWith("s:", StringComparison.Ordinal) || st.StartsWith("c:", StringComparison.Ordinal)))
                {
                    bool script = st[0] == 's';
                    int e = s.IndexOf(script ? "</script" : "</style", i, StringComparison.OrdinalIgnoreCase);
                    int to = e < 0 ? n : e;
                    string inner = st.Length > 2 ? st.Substring(2) : null;
                    if (script) Js(s, i, to, r, ref inner, false, false); else Css(s, i, to, r, ref inner);
                    if (e < 0) { st = st.Substring(0, 2) + (inner ?? ""); i = n; break; }
                    st = null; i = e; continue;
                }
                if (st != null && st.StartsWith("t:", StringComparison.Ordinal)) { i = HtmlAttrs(s, i, n, r, ref st); continue; }
                char c = s[i];
                if (c == '<' && i + 1 < n)
                {
                    char d = s[i + 1];
                    if (d == '!' && string.CompareOrdinal(s, i, "<!--", 0, 4) == 0)
                    {
                        int e = s.IndexOf("-->", i + 4, StringComparison.Ordinal);
                        if (e < 0) { Add(r, i, n, Tok.Comment); st = "!"; i = n; break; }
                        Add(r, i, e + 3, Tok.Comment); i = e + 3; continue;
                    }
                    if (d == '!' || d == '?')   // <!DOCTYPE html>, <?xml …?>
                    {
                        int j = i + 2, k = j; while (k < n && (char.IsLetter(s[k]) || s[k] == '-')) k++;
                        Add(r, i, j, Tok.Op); Add(r, j, k, Tok.Keyword);
                        int e = s.IndexOf('>', k);
                        if (e < 0) { i = n; break; }
                        Add(r, e, e + 1, Tok.Op); i = e + 1; continue;
                    }
                    if (d == '/')
                    {
                        int j = i + 2, k = j; while (k < n && HtmlName(s[k])) k++;
                        Add(r, i, j, Tok.Op); Add(r, j, k, Tok.Keyword);
                        int m = SkipSp(s, k, n); if (m < n && s[m] == '>') { Add(r, m, m + 1, Tok.Op); m++; }
                        i = m; continue;
                    }
                    if (char.IsLetter(d))
                    {
                        int k = i + 1; while (k < n && HtmlName(s[k])) k++;
                        Add(r, i, i + 1, Tok.Op); Add(r, i + 1, k, Tok.Keyword);
                        st = "t:" + s.Substring(i + 1, k - i - 1).ToLowerInvariant(); i = k; continue;
                    }
                }
                if (c == '&')
                {
                    var m = HtmlEntity.Match(s, i);
                    if (m.Success) { Add(r, i, i + m.Length, Tok.Keyword); i += m.Length; continue; }
                }
                i++; while (i < n && s[i] != '<' && s[i] != '&') i++;   // текст — цветом по умолчанию
            }
            state = st;
        }

        static int HtmlAttrs(string s, int i, int n, List<Span> r, ref string st)
        {
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '>') { Add(r, i, i + 2, Tok.Op); st = null; return i + 2; }
                if (c == '>')
                {
                    Add(r, i, i + 1, Tok.Op);
                    string tag = st.Substring(2);
                    st = tag == "script" ? "s:" : tag == "style" ? "c:" : null;
                    return i + 1;
                }
                if (c == '=')
                {
                    Add(r, i, i + 1, Tok.Op);
                    int j = SkipSp(s, i + 1, n);
                    if (j < n && (s[j] == '"' || s[j] == '\'')) { int e = StrEnd(s, j, n, s[j], false); Add(r, j, e, Tok.Str); i = e; }
                    else { int e = j; while (e < n && !char.IsWhiteSpace(s[e]) && s[e] != '>') e++; Add(r, j, e, Tok.Str); i = e; }
                    continue;
                }
                if (c == '"' || c == '\'') { int e = StrEnd(s, i, n, c, false); Add(r, i, e, Tok.Str); i = e; continue; }
                int k = i; while (k < n && !char.IsWhiteSpace(s[k]) && "\"'>=/".IndexOf(s[k]) < 0) k++;
                if (k == i) k = One(s, i, n);
                Add(r, i, k, Tok.Text); i = k;
            }
            return n;
        }

        // ================= CSS =================
        // state: «{» на каждый уровень вложенности, «v» — внутри значения свойства, «*» — внутри /* */
        static readonly HashSet<string> CssPseudo = new HashSet<string> { "hover", "focus", "active", "visited", "link", "first-child", "last-child", "nth-child",
            "nth-of-type", "first-of-type", "last-of-type", "not", "is", "where", "has", "before", "after", "root", "checked", "disabled", "enabled", "focus-visible",
            "focus-within", "placeholder", "empty", "target", "only-child", "selection", "first-line", "first-letter", "marker" };

        static void Css(string s, int from, int to, List<Span> r, ref string state)
        {
            int depth = 0; bool cmt = false, val = false;
            if (state != null) foreach (char ch in state) { if (ch == '{') depth++; else if (ch == 'v') val = true; else if (ch == '*') cmt = true; }
            int i = from, n = to;
            int mode = val ? 2 : -1;   // 0 — селектор, 1 — имя свойства, 2 — значение, 4 — пролог @-правила; -1 — ещё не ясно
            while (i < n)
            {
                if (cmt)
                {
                    int e = s.IndexOf("*/", i, n - i, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); i = n; break; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; cmt = false; continue;
                }
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, n - i - 2, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); cmt = true; i = n; break; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; continue;
                }
                if (c == '{') { Add(r, i, i + 1, Tok.Text); i++; depth++; mode = -1; continue; }
                if (c == '}') { Add(r, i, i + 1, Tok.Text); i++; depth = Math.Max(0, depth - 1); mode = -1; continue; }
                if (c == ';') { Add(r, i, i + 1, Tok.Text); i++; mode = -1; continue; }
                if (c == '"' || c == '\'') { int j = StrEnd(s, i, n, c, true); Add(r, i, j, Tok.Str); i = j; if (mode < 0) mode = 0; continue; }
                if (mode < 0)
                {
                    if (c == '@')
                    {
                        int j = i + 1; while (j < n && (Id(s[j]) || s[j] == '-')) j++;
                        Add(r, i, j, Tok.Control); i = j; mode = 4; continue;
                    }
                    mode = depth == 0 || CssIsSelector(s, i, n) ? 0 : 1;
                }
                if (mode == 0) { i = CssSelectorToken(s, i, n, r); continue; }
                if (mode == 1)
                {
                    if (c == ':') { Add(r, i, i + 1, Tok.Op); i++; mode = 2; continue; }
                    if (Id(c) || c == '-') { int j = i; while (j < n && (Id(s[j]) || s[j] == '-')) j++; Add(r, i, j, Tok.Text); i = j; continue; }
                    int o = One(s, i, n); Add(r, i, o, Tok.Text); i = o; continue;
                }
                i = CssValueToken(s, i, n, r, mode == 4);
            }
            var sb = new StringBuilder();
            sb.Append('{', depth); if (mode == 2) sb.Append('v'); if (cmt) sb.Append('*');
            state = sb.Length > 0 ? sb.ToString() : null;
        }

        // внутри блока: «a:hover {» — селектор, «color: red;» — свойство
        static bool CssIsSelector(string s, int i, int n)
        {
            for (int j = i; j < n; j++)
            {
                char c = s[j];
                if (c == '{') return true;
                if (c == ';' || c == '}') return false;
                if (c == '"' || c == '\'') j = StrEnd(s, j, n, c, true) - 1;
                else if (c == '/' && j + 1 < n && s[j + 1] == '*') break;
            }
            int colon = s.IndexOf(':', i, n - i);
            if (colon < 0) return true;
            if (colon + 1 < n && s[colon + 1] == ':') return true;
            int k = colon + 1; while (k < n && (Id(s[k]) || s[k] == '-')) k++;
            return CssPseudo.Contains(s.Substring(colon + 1, k - colon - 1));
        }

        static int CssSelectorToken(string s, int i, int n, List<Span> r)
        {
            char c = s[i];
            if ((c == '.' || c == '#' || c == ':') && i + 1 < n)
            {
                int j = i + 1; if (c == ':' && s[j] == ':') j++;
                int k = j; while (k < n && (Id(s[k]) || s[k] == '-')) k++;
                if (k > j) { Add(r, i, k, Tok.Func); return k; }
            }
            if (Dig(c)) { int j = NumEnd(s, i, n); if (j < n && s[j] == '%') j++; Add(r, i, j, Tok.Num); return j; }
            if (Id(c) || c == '-') { int j = i; while (j < n && (Id(s[j]) || s[j] == '-')) j++; Add(r, i, j, Tok.Func); return j; }
            if (c == '[')   // [type="text"]
            {
                Add(r, i, i + 1, Tok.Text);
                int j = i + 1, k = j; while (k < n && (Id(s[k]) || s[k] == '-')) k++;
                Add(r, j, k, Tok.Text); j = k;
                while (j < n && "~|^$*=".IndexOf(s[j]) >= 0) j++;
                Add(r, k, j, Tok.Op);
                if (j < n && (s[j] == '"' || s[j] == '\'')) { int e = StrEnd(s, j, n, s[j], true); Add(r, j, e, Tok.Str); j = e; }
                else { int e = j; while (e < n && s[e] != ']') e++; Add(r, j, e, Tok.Str); j = e; }
                if (j < n && s[j] == ']') { Add(r, j, j + 1, Tok.Text); j++; }
                return j;
            }
            int o = One(s, i, n);
            Add(r, i, o, "*>+~,&".IndexOf(c) >= 0 ? Tok.Op : Tok.Text);
            return o;
        }

        // значение свойства (или пролог @media/@import: prelude = true)
        static int CssValueToken(string s, int i, int n, List<Span> r, bool prelude)
        {
            char c = s[i];
            if (c == '#' && i + 1 < n && Id(s[i + 1])) { int j = i + 1; while (j < n && Id(s[j])) j++; Add(r, i, j, Tok.Str); return j; }
            if (Dig(c) || (c == '.' && i + 1 < n && Dig(s[i + 1])) || ((c == '-' || c == '+') && i + 1 < n && (Dig(s[i + 1]) || (s[i + 1] == '.' && i + 2 < n && Dig(s[i + 2])))))
            {
                int j = i + 1; while (j < n && (Dig(s[j]) || s[j] == '.')) j++;
                while (j < n && (char.IsLetter(s[j]) || s[j] == '%')) j++;
                Add(r, i, j, Tok.Num); return j;
            }
            if (c == '!') { int j = i + 1; while (j < n && char.IsLetter(s[j])) j++; Add(r, i, j, j > i + 1 ? Tok.Keyword : Tok.Op); return j; }
            if (Id(c) || (c == '-' && i + 1 < n && (Id(s[i + 1]) || s[i + 1] == '-')))
            {
                int j = i; while (j < n && (Id(s[j]) || s[j] == '-')) j++;
                string w = s.Substring(i, j - i);
                if (j < n && s[j] == '(')
                {
                    Add(r, i, j, Tok.Func);
                    if (w == "url")   // url(картинка.png) — строкой
                    {
                        int e = s.IndexOf(')', j); if (e < 0) e = n;
                        Add(r, j, j + 1, Tok.Text); Add(r, j + 1, e, Tok.Str); if (e < n) Add(r, e, e + 1, Tok.Text);
                        return Math.Min(n, e + 1);
                    }
                    return j;
                }
                Tok t;
                if (w.StartsWith("--", StringComparison.Ordinal)) t = Tok.Text;
                else if (prelude) { int k = SkipSp(s, j, n); t = k < n && s[k] == ':' ? Tok.Text : (w == "and" || w == "not" || w == "only" || w == "or") ? Tok.Keyword : Tok.Str; }
                else t = Tok.Str;
                Add(r, i, j, t); return j;
            }
            int o = One(s, i, n);
            Add(r, i, o, ",/*+-=:>".IndexOf(c) >= 0 ? Tok.Op : Tok.Text);
            return o;
        }

        // ================= SQL =================
        static readonly HashSet<string> SqlKw = new HashSet<string> { "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "ILIKE", "BETWEEN",
            "EXISTS", "JOIN", "INNER", "LEFT", "RIGHT", "FULL", "OUTER", "CROSS", "NATURAL", "ON", "USING", "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "AS",
            "DISTINCT", "ALL", "ANY", "UNION", "INTERSECT", "EXCEPT", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "CREATE", "TABLE", "VIEW", "INDEX", "DROP",
            "ALTER", "ADD", "COLUMN", "RENAME", "TO", "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "UNIQUE", "DEFAULT", "CHECK", "CONSTRAINT", "IF", "ASC", "DESC", "WITH",
            "RECURSIVE", "RETURNING", "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "TRUE", "FALSE", "CASCADE", "TRUNCATE", "GRANT", "REVOKE", "OVER", "PARTITION",
            "WINDOW", "ROWS", "RANGE", "UNBOUNDED", "PRECEDING", "FOLLOWING", "CURRENT", "ROW", "FETCH", "FIRST", "NEXT", "ONLY", "NULLS", "LAST", "EXPLAIN", "ANALYZE",
            "REPLACE", "CONFLICT", "DO", "NOTHING", "AUTOINCREMENT", "TEMP", "TEMPORARY", "SCHEMA", "DATABASE", "TRIGGER", "FOR", "EACH", "OF", "COLLATE",
            "INTEGER", "INT", "BIGINT", "SMALLINT", "TEXT", "VARCHAR", "CHAR", "BOOLEAN", "BOOL", "REAL", "FLOAT", "DOUBLE", "PRECISION", "NUMERIC", "DECIMAL", "DATE",
            "TIME", "TIMESTAMP", "TIMESTAMPTZ", "INTERVAL", "JSON", "JSONB", "UUID", "BLOB", "SERIAL", "BIGSERIAL" };
        static readonly HashSet<string> SqlCtl = new HashSet<string> { "CASE", "WHEN", "THEN", "ELSE", "END" };
        static readonly HashSet<string> SqlFuncs = new HashSet<string> { "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "NULLIF", "ROUND", "ABS", "LENGTH", "LOWER",
            "UPPER", "SUBSTR", "SUBSTRING", "TRIM", "REPLACE", "CONCAT", "NOW", "DATE", "STRFTIME", "DATETIME", "JULIANDAY", "CAST", "EXTRACT", "IFNULL", "ROW_NUMBER",
            "RANK", "DENSE_RANK", "LAG", "LEAD", "STRING_AGG", "GROUP_CONCAT", "DATE_TRUNC", "TO_CHAR", "LEFT", "RIGHT", "IF", "GREATEST", "LEAST", "RANDOM", "INSTR" };
        static readonly HashSet<string> SqlNotFunc = new HashSet<string> { "TABLE", "INTO", "JOIN", "FROM", "UPDATE", "REFERENCES", "VIEW", "INDEX", "EXISTS", "ON" };

        static void Sql(string s, List<Span> r, ref string state)
        {
            int i = 0, n = s.Length;
            string prev = null;
            if (state == "*")
            {
                int e = s.IndexOf("*/", StringComparison.Ordinal);
                if (e < 0) { Add(r, 0, n, Tok.Comment); return; }
                Add(r, 0, e + 2, Tok.Comment); i = e + 2; state = null;
            }
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '-' && i + 1 < n && s[i + 1] == '-') { Add(r, i, n, Tok.Comment); break; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); state = "*"; break; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; continue;
                }
                if (c == '\'')   // 'It''s'
                {
                    int j = i + 1;
                    while (j < n) { if (s[j] == '\'') { if (j + 1 < n && s[j + 1] == '\'') { j += 2; continue; } break; } j++; }
                    j = Math.Min(n, j + 1); Add(r, i, j, Tok.Str); i = j; prev = null; continue;
                }
                if (c == '"' || c == '`') { int j = StrEnd(s, i, n, c, false); Add(r, i, j, Tok.Str); i = j; prev = null; continue; }
                if (Dig(c) || (c == '.' && i + 1 < n && Dig(s[i + 1]))) { int j = NumEnd(s, i, n); Add(r, i, j, Tok.Num); i = j; prev = null; continue; }
                if (IdStart(c))
                {
                    int j = i; while (j < n && Id(s[j])) j++;
                    string u = s.Substring(i, j - i).ToUpperInvariant();
                    int k = SkipSp(s, j, n);
                    bool call = k < n && s[k] == '(';
                    if (call && SqlFuncs.Contains(u) && (k == j || !SqlKw.Contains(u))) Add(r, i, j, Tok.Func);
                    else if (SqlCtl.Contains(u)) Add(r, i, j, Tok.Control);
                    else if (SqlKw.Contains(u)) Add(r, i, j, Tok.Keyword);
                    else if (call && k == j && (prev == null || !SqlNotFunc.Contains(prev))) Add(r, i, j, Tok.Func);
                    // имена таблиц и столбцов — цветом по умолчанию
                    prev = u; i = j; continue;
                }
                int o = i; while (o < n && "+-*/%=<>!|&^~:".IndexOf(s[o]) >= 0 && !(o > i && s[o] == '-' && o + 1 < n && s[o + 1] == '-')) o++;
                if (o > i) { Add(r, i, o, Tok.Op); i = o; prev = null; continue; }
                int one = One(s, i, n); Add(r, i, one, Tok.Text); i = one; prev = null;
            }
        }

        // ================= YAML =================
        // state: "|N" — внутри блочного скаляра (| или >), N — отступ его ключа
        static readonly Regex YamlNum = new Regex(@"^[-+]?(?:\d[\d_]*(?:\.\d*)?(?:[eE][-+]?\d+)?|\.\d+(?:[eE][-+]?\d+)?|0x[0-9a-fA-F]+|0o[0-7]+|\.inf|\.Inf|\.nan|\.NaN)$");
        static readonly HashSet<string> YamlConst = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "true", "false", "yes", "no", "on", "off", "null", "~" };

        static void Yaml(string s, List<Span> r, ref string state)
        {
            int n = s.Length, i = 0;
            while (i < n && s[i] == ' ') i++;
            int parent;
            if (state != null && (state.Length < 2 || state[0] != '|' || !int.TryParse(state.Substring(1), out parent))) state = null;   // чужое состояние
            if (state != null)
            {
                parent = int.Parse(state.Substring(1));
                if (i >= n) return;                                          // пустая строка внутри блока
                if (i > parent) { Add(r, i, n, Tok.Str); return; }
                state = null;
            }
            if (i >= n) return;
            if (s[i] == '#') { Add(r, i, n, Tok.Comment); return; }
            if (i == 0 && n >= 3 && (string.CompareOrdinal(s, 0, "---", 0, 3) == 0 || string.CompareOrdinal(s, 0, "...", 0, 3) == 0) && (n == 3 || Sp(s[3])))
            { Add(r, 0, 3, Tok.Op); i = 3; }
            int keyCol = i;
            while (i + 1 <= n && i < n && s[i] == '-' && (i + 1 == n || Sp(s[i + 1])))   // элементы списка «- »
            { Add(r, i, i + 1, Tok.Op); keyCol = i; i = SkipSp(s, i + 1, n); }
            int ke = YamlKeyEnd(s, i, n);
            if (ke >= 0)
            {
                int kEnd = ke; while (kEnd > i && Sp(s[kEnd - 1])) kEnd--;
                Add(r, i, kEnd, Tok.Keyword); Add(r, ke, ke + 1, Tok.Op); keyCol = i; i = ke + 1;
            }
            YamlValue(s, i, n, r, ref state, keyCol);
        }

        // позиция «:» после ключа (key: …) или -1
        internal static int YamlKeyEnd(string s, int i, int n)
        {
            if (i >= n) return -1;
            char c = s[i];
            if (c == '"' || c == '\'')
            {
                int k = SkipSp(s, StrEnd(s, i, n, c, c == '"'), n);
                return k < n && s[k] == ':' && (k + 1 == n || Sp(s[k + 1])) ? k : -1;
            }
            if ("[{#&*!|>%@`-".IndexOf(c) >= 0 && !(c == '-' && i + 1 < n && !Sp(s[i + 1]))) return -1;
            for (int j = i; j < n; j++)
            {
                char d = s[j];
                if (d == ':' && (j + 1 == n || Sp(s[j + 1]))) return j;
                if (d == '#' && j > i && Sp(s[j - 1])) return -1;
                if (d == '"' || d == '\'') return -1;
            }
            return -1;
        }

        static void YamlValue(string s, int i, int n, List<Span> r, ref string state, int keyCol)
        {
            int flow = 0;
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '#' && (i == 0 || Sp(s[i - 1]))) { Add(r, i, n, Tok.Comment); return; }
                if (c == '"' || c == '\'')
                {
                    int j = StrEnd(s, i, n, c, c == '"'); int k = SkipSp(s, j, n);
                    Add(r, i, j, flow > 0 && k < n && s[k] == ':' ? Tok.Keyword : Tok.Str); i = j; continue;
                }
                if ((c == '|' || c == '>') && flow == 0)   // блочный скаляр: следующие строки с большим отступом — текст
                {
                    int j = i + 1; while (j < n && "+-0123456789".IndexOf(s[j]) >= 0) j++;
                    int k = SkipSp(s, j, n);
                    if (k >= n || s[k] == '#') { Add(r, i, j, Tok.Op); state = "|" + keyCol; i = j; continue; }
                }
                if (c == '&' || c == '*')   // якоря и ссылки
                {
                    int j = i + 1; while (j < n && !char.IsWhiteSpace(s[j]) && ",[]{}".IndexOf(s[j]) < 0) j++;
                    if (j > i + 1) { Add(r, i, j, Tok.Class); i = j; continue; }
                }
                if (c == '!') { int j = i + 1; while (j < n && !char.IsWhiteSpace(s[j])) j++; Add(r, i, j, Tok.Keyword); i = j; continue; }
                if (c == '[' || c == '{') { Add(r, i, i + 1, Tok.Op); flow++; i++; continue; }
                if (flow > 0 && (c == ']' || c == '}')) { Add(r, i, i + 1, Tok.Op); flow--; i++; continue; }
                if (flow > 0 && (c == ',' || c == ':')) { Add(r, i, i + 1, Tok.Op); i++; continue; }
                // простой скаляр — до комментария или конца строки (в потоке — до , ] } и «: »)
                int a = i, e = i, q = i;
                while (q < n)
                {
                    char d = s[q];
                    if (d == '#' && q > a && Sp(s[q - 1])) break;
                    if (flow > 0 && (d == ',' || d == ']' || d == '}' || d == '[' || d == '{')) break;
                    if (flow > 0 && d == ':' && (q + 1 == n || Sp(s[q + 1]) || s[q + 1] == ',')) break;
                    q++;
                    if (!Sp(d)) e = q;
                }
                string v = s.Substring(a, e - a);
                Tok t = flow > 0 && q < n && s[q] == ':' ? Tok.Keyword : YamlNum.IsMatch(v) ? Tok.Num : YamlConst.Contains(v) ? Tok.Keyword : Tok.Str;
                Add(r, a, e, t);
                i = Math.Max(q, a + 1);
            }
        }

        // ================= bash =================
        // state: "c1"/"c0" — строка продолжается после «\» (1 — дальше команда), "\"" / "'" — многострочная строка,
        //        "h:СТОП" / "h-:СТОП" — тело heredoc до строки СТОП
        static readonly HashSet<string> ShCtl = new HashSet<string> { "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac",
            "select", "return", "break", "continue", "exit", "time" };
        static readonly HashSet<string> ShKw = new HashSet<string> { "function", "local", "export", "declare", "readonly", "unset", "typeset", "alias" };
        static readonly HashSet<string> ShCmdAfter = new HashSet<string> { "if", "then", "else", "elif", "while", "until", "do", "time", "!" };
        static readonly HashSet<string> ShPrefix = new HashSet<string> { "sudo", "exec", "nohup", "command", "builtin", "xargs", "env" };

        static void Bash(string s, int from, List<Span> r, ref string state)
        {
            int i = from, n = s.Length;
            string st = state; state = null;
            bool cmd = st != "c0";
            if (st != null && st.StartsWith("h", StringComparison.Ordinal))   // тело heredoc
            {
                string stop = st.Substring(st.IndexOf(':') + 1);
                int a = SkipSp(s, i, n);
                if (s.Trim() == stop) { Add(r, a, a + stop.Length, Tok.Keyword); return; }
                Add(r, a, n, Tok.Str); state = st; return;
            }
            if (st == "\"" || st == "'")
            {
                bool closed;
                i = st == "\"" ? ShDq(s, i, i, n, r, out closed) : ShSq(s, i, i, n, r, out closed);
                if (!closed) { state = st; return; }
                cmd = false;
            }
            string heredoc = null, p1 = null, p2 = null;   // p1, p2 — два предыдущих слова (для «for x in»)
            bool comment = false;
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '#' && (i == from || char.IsWhiteSpace(s[i - 1]) || ";|&(".IndexOf(s[i - 1]) >= 0)) { Add(r, i, n, Tok.Comment); comment = true; break; }
                if (c == '\'')
                {
                    bool closed; i = ShSq(s, i, i + 1, n, r, out closed);
                    if (!closed) { state = "'"; return; }
                    cmd = false; p2 = p1; p1 = "'"; continue;
                }
                if (c == '"')
                {
                    bool closed; i = ShDq(s, i, i + 1, n, r, out closed);
                    if (!closed) { state = "\""; return; }
                    cmd = false; p2 = p1; p1 = "\""; continue;
                }
                if (c == '$')
                {
                    if (i + 1 < n && s[i + 1] == '(')   // $( команда ) и $(( арифметика ))
                    {
                        int j = i + 2; bool arith = j < n && s[j] == '('; if (arith) j++;
                        Add(r, i, j, Tok.Op); i = j; cmd = !arith; p1 = p2 = null; continue;
                    }
                    if (i + 1 < n && s[i + 1] == '\'') { bool closed; i = ShSq(s, i, i + 2, n, r, out closed); if (!closed) { state = "'"; return; } cmd = false; continue; }
                    int e = ShVarEnd(s, i, n);
                    if (e > i + 1) { Add(r, i, e, Tok.Text); i = e; cmd = false; p2 = p1; p1 = "$"; continue; }
                }
                if (c == '`') { Add(r, i, i + 1, Tok.Op); i++; cmd = true; continue; }
                if (c == '<' && i + 1 < n && s[i + 1] == '<' && !(i + 2 < n && s[i + 2] == '<'))   // heredoc
                {
                    int j = i + 2; bool dash = j < n && s[j] == '-'; if (dash) j++;
                    Add(r, i, j, Tok.Op);
                    int k = SkipSp(s, j, n), e = k;
                    if (k < n && (s[k] == '\'' || s[k] == '"')) e = StrEnd(s, k, n, s[k], false);
                    else if (k < n && IdStart(s[k])) while (e < n && (Id(s[e]) || s[e] == '-')) e++;
                    if (e > k) { heredoc = (dash ? "h-:" : "h:") + s.Substring(k, e - k).Trim('\'', '"'); Add(r, k, e, Tok.Keyword); }
                    i = e; cmd = false; continue;
                }
                if (";|&".IndexOf(c) >= 0) { int j = i; while (j < n && ";|&".IndexOf(s[j]) >= 0) j++; Add(r, i, j, Tok.Op); i = j; cmd = true; p1 = p2 = null; continue; }
                if (c == '>' || c == '<' || (Dig(c) && i + 1 < n && s[i + 1] == '>'))   // перенаправления: > >> 2>&1 <
                {
                    int j = i; if (Dig(s[j])) j++;
                    while (j < n && (s[j] == '>' || s[j] == '<' || s[j] == '&')) j++;
                    if (j < n && Dig(s[j]) && s[j - 1] == '&') j++;
                    Add(r, i, j, Tok.Op); i = j; cmd = false; continue;
                }
                if (c == '(' || c == ')' || c == '{' || c == '}')
                {
                    Add(r, i, i + 1, Tok.Op); i++; cmd = c == '(' || c == '{'; continue;
                }
                if (c == '\\' && i == n - 1) { Add(r, i, n, Tok.Op); i = n; break; }
                // слово
                int w0 = i, w1 = i;
                while (w1 < n && !char.IsWhiteSpace(s[w1]) && ";|&<>()'\"`$".IndexOf(s[w1]) < 0) { if (s[w1] == '\\') w1++; w1++; }
                if (w1 > n) w1 = n;
                if (w1 == w0) { w1 = One(s, i, n); Add(r, i, w1, Tok.Text); i = w1; continue; }
                string w = s.Substring(w0, w1 - w0);
                if (cmd)
                {
                    int eq = w.IndexOf('=');
                    if (eq > 0 && IsShName(w, eq)) { Add(r, w0, w0 + eq, Tok.Text); Add(r, w0 + eq, w0 + eq + 1, Tok.Op); i = w0 + eq + 1; cmd = false; continue; }
                    if (ShCtl.Contains(w)) { Add(r, w0, w1, Tok.Control); cmd = ShCmdAfter.Contains(w); }
                    else if (ShKw.Contains(w)) { Add(r, w0, w1, Tok.Keyword); cmd = false; }
                    else if (w == "[" || w == "[[" || w == "!") { Add(r, w0, w1, Tok.Op); cmd = w == "!"; }
                    else
                    {
                        int k = SkipSp(s, w1, n);
                        bool def = k + 1 < n && s[k] == '(' && s[k + 1] == ')';
                        Add(r, w0, w1, def || p1 == "function" ? Tok.Def : Tok.Func);
                        cmd = ShPrefix.Contains(w);
                    }
                }
                else if (p1 == "function") Add(r, w0, w1, Tok.Def);
                else if (p1 != null && ShKw.Contains(p1) && w.IndexOf('=') > 0 && IsShName(w, w.IndexOf('=')))   // export A=1, local x=…
                {
                    int eq = w.IndexOf('='); Add(r, w0, w0 + eq, Tok.Text); Add(r, w0 + eq, w0 + eq + 1, Tok.Op); i = w0 + eq + 1; p2 = p1; p1 = w; continue;
                }
                else if (w == "in" && (p2 == "for" || p2 == "case" || p2 == "select")) Add(r, w0, w1, Tok.Control);
                else if (w == "]" || w == "]]") Add(r, w0, w1, Tok.Op);
                else if (IsAllDigits(w)) Add(r, w0, w1, Tok.Num);
                // аргументы и ключи (-la, --force) — цветом по умолчанию
                p2 = p1; p1 = w; i = w1;
            }
            if (heredoc != null) state = heredoc;
            else if (!comment && ContinuesLine(s)) state = cmd ? "c1" : "c0";
        }

        static bool IsShName(string w, int eq)
        {
            int end = eq > 0 && w[eq - 1] == '+' ? eq - 1 : eq;   // A+=1
            if (end == 0 || !IdStart(w[0])) return false;
            for (int k = 1; k < end; k++) if (!Id(w[k])) return false;
            return true;
        }
        static bool IsAllDigits(string w) { if (w.Length == 0) return false; foreach (char c in w) if (!Dig(c)) return false; return true; }

        // $var, ${…}, $1, $?, $@ — конец подстановки (i указывает на $)
        static int ShVarEnd(string s, int i, int n)
        {
            if (i + 1 >= n) return i + 1;
            char d = s[i + 1];
            if (d == '{') { int e = s.IndexOf('}', i + 2); return e < 0 ? n : e + 1; }
            if (IdStart(d)) { int j = i + 1; while (j < n && Id(s[j])) j++; return j; }
            if (Dig(d) || "?#@*$!-".IndexOf(d) >= 0) return i + 2;
            return i + 1;
        }

        // "…" с подстановками $var внутри; a — начало цвета строки, i — откуда искать
        static int ShDq(string s, int a, int i, int n, List<Span> r, out bool closed)
        {
            while (i < n)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '"') { Add(r, a, i + 1, Tok.Str); closed = true; return i + 1; }
                if (c == '$')
                {
                    int e = ShVarEnd(s, i, n);
                    if (e > i + 1 && !(i + 1 < n && s[i + 1] == '(')) { Add(r, a, i, Tok.Str); Add(r, i, e, Tok.Text); i = e; a = e; continue; }
                }
                i++;
            }
            if (i > n) i = n;
            Add(r, a, i, Tok.Str); closed = false; return i;
        }
        static int ShSq(string s, int a, int i, int n, List<Span> r, out bool closed)
        {
            int e = s.IndexOf('\'', i);
            if (e < 0) { Add(r, a, n, Tok.Str); closed = false; return n; }
            Add(r, a, e + 1, Tok.Str); closed = true; return e + 1;
        }

        // ================= Dockerfile =================
        // state: "r|<состояние bash>" — продолжение RUN/CMD (shell-форма) после «\», "c|ИНСТРУКЦИЯ" — продолжение остальных
        static readonly string[] DockerInstrList = { "FROM", "RUN", "CMD", "LABEL", "MAINTAINER", "EXPOSE", "ENV", "ADD", "COPY", "ENTRYPOINT", "VOLUME", "USER",
            "WORKDIR", "ARG", "ONBUILD", "STOPSIGNAL", "HEALTHCHECK", "SHELL" };
        static readonly HashSet<string> DockerInstr = new HashSet<string>(DockerInstrList);
        static readonly Regex DockerPort = new Regex(@"^\d+(?:-\d+)?(?:/(?:tcp|udp))?$");

        static void Docker(string s, List<Span> r, ref string state)
        {
            int n = s.Length, i = SkipSp(s, 0, n);
            string st = state; state = null;
            if (i < n && s[i] == '#') { Add(r, i, n, Tok.Comment); state = st; return; }   // комментарий не прерывает продолжение
            if (i >= n) { state = st; return; }
            string instr = null, bashState = null;
            bool shell;
            if (st == null)
            {
                int j = i; while (j < n && char.IsLetter(s[j])) j++;
                string w = s.Substring(i, j - i).ToUpperInvariant();
                if (DockerInstr.Contains(w)) { Add(r, i, j, Tok.Keyword); instr = w; i = j; }
                shell = (instr == "RUN" || instr == "CMD" || instr == "ENTRYPOINT") && SkipSp(s, i, n) < n && s[SkipSp(s, i, n)] != '[';
                if (shell && instr == "RUN")   // RUN --mount=… --network=… — ключи до команды
                    for (int k = SkipSp(s, i, n); k + 1 < n && s[k] == '-' && s[k + 1] == '-'; k = SkipSp(s, i, n)) { i = k; while (i < n && !char.IsWhiteSpace(s[i])) i++; }
            }
            else if (st.StartsWith("r|", StringComparison.Ordinal)) { shell = true; bashState = st.Length > 2 ? st.Substring(2) : null; }
            else { shell = false; instr = st.StartsWith("c|", StringComparison.Ordinal) ? st.Substring(2) : null; }
            if (shell)
            {
                Bash(s, i, r, ref bashState);
                if (ContinuesLine(s)) state = "r|" + (bashState ?? "");
                return;
            }
            DockerArgs(s, i, n, r, instr);
            if (ContinuesLine(s)) state = "c|" + instr;
        }

        static void DockerArgs(string s, int i, int n, List<Span> r, string instr)
        {
            bool first = true;
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '"' || c == '\'') { int q = StrEnd(s, i, n, c, c == '"'); Add(r, i, q, Tok.Str); i = q; first = false; continue; }
                if (c == '$') { int e = ShVarEnd(s, i, n); if (e > i + 1) { Add(r, i, e, Tok.Text); i = e; first = false; continue; } }
                if (c == '[' || c == ']' || c == ',') { Add(r, i, i + 1, Tok.Text); i++; continue; }
                if (c == '\\' && ContinuesLine(s) && SkipSp(s, i + 1, n) >= n) { Add(r, i, i + 1, Tok.Op); i++; continue; }
                int j = i; while (j < n && !char.IsWhiteSpace(s[j]) && "\"'$,]".IndexOf(s[j]) < 0) j++;
                if (j == i) { j = One(s, i, n); Add(r, i, j, Tok.Text); i = j; continue; }
                string w = s.Substring(i, j - i);
                if (instr == "FROM" && w.Equals("AS", StringComparison.OrdinalIgnoreCase)) Add(r, i, j, Tok.Keyword);
                else if (instr == "HEALTHCHECK" && (w == "CMD" || w == "NONE"))
                {
                    Add(r, i, j, Tok.Keyword);
                    if (w == "CMD") { string bs = null; Bash(s, j, r, ref bs); return; }
                }
                else if (instr == "EXPOSE" && DockerPort.IsMatch(w)) Add(r, i, j, Tok.Num);
                else if ((instr == "ENV" || instr == "ARG" || instr == "LABEL") && w.IndexOf('=') > 0 && !w.StartsWith("-", StringComparison.Ordinal))
                {
                    int eq = w.IndexOf('='); Add(r, i, i + eq, Tok.Text); Add(r, i + eq, i + eq + 1, Tok.Op);
                }
                else if ((instr == "ENV" || instr == "ARG") && first) Add(r, i, j, Tok.Text);   // ARG NAME, старая форма ENV KEY value
                first = false; i = j;
            }
        }

        // ================= nginx =================
        // state: "a" — директива не закончена (аргументы продолжаются на следующей строке)
        static readonly HashSet<string> NginxCtl = new HashSet<string> { "if", "return", "rewrite", "break" };
        static readonly Regex NginxNum = new Regex(@"^\d+(?:\.\d+)?(?:ms|[kKmMgGsShHdDwWyY])?$");
        static readonly string[] NginxDirectives = { "http", "server", "location", "upstream", "events", "map", "listen", "server_name", "root", "index", "try_files",
            "proxy_pass", "proxy_set_header", "proxy_http_version", "proxy_read_timeout", "proxy_connect_timeout", "proxy_send_timeout", "proxy_buffering",
            "proxy_redirect", "proxy_cache", "add_header", "return", "rewrite", "if", "set", "include", "worker_processes", "worker_connections", "error_page",
            "access_log", "error_log", "log_format", "gzip", "gzip_types", "client_max_body_size", "ssl_certificate", "ssl_certificate_key", "ssl_protocols",
            "ssl_ciphers", "keepalive_timeout", "sendfile", "expires", "alias", "limit_req", "limit_req_zone", "default_type", "types", "deny", "allow", "auth_basic",
            "resolver", "internal", "charset", "default_server", "http2", "fastcgi_pass", "uwsgi_pass", "break" };

        static void Nginx(string s, List<Span> r, ref string state)
        {
            int i = 0, n = s.Length;
            bool start = state == null;   // сейчас начинается директива
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '#') { Add(r, i, n, Tok.Comment); break; }
                if (c == ';' || c == '{' || c == '}') { Add(r, i, i + 1, Tok.Text); i++; start = true; continue; }
                if (c == '"' || c == '\'') { i = NginxStr(s, i, n, r, c); start = false; continue; }
                if (c == '$') { int e = ShVarEnd(s, i, n); if (e > i + 1) { Add(r, i, e, Tok.Text); i = e; start = false; continue; } }
                int j = i + 1; while (j < n && !char.IsWhiteSpace(s[j]) && ";{}\"'".IndexOf(s[j]) < 0 && !(s[j] == '$' && j + 1 < n && (IdStart(s[j + 1]) || s[j + 1] == '{'))) j++;
                string w = s.Substring(i, j - i);
                if (start) { Add(r, i, j, NginxCtl.Contains(w) ? Tok.Control : Tok.Keyword); start = false; }
                else if (NginxNum.IsMatch(w)) Add(r, i, j, Tok.Num);
                else if (w == "on" || w == "off") Add(r, i, j, Tok.Keyword);
                else if (w == "=" || w == "~" || w == "~*" || w == "^~" || w == "!=" || w == "!~" || w == "!~*") Add(r, i, j, Tok.Op);
                // пути, адреса, регулярки — цветом по умолчанию
                i = j;
            }
            state = start ? null : "a";
        }

        static int NginxStr(string s, int i, int n, List<Span> r, char q)
        {
            int a = i; i++;
            while (i < n)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == q) { Add(r, a, i + 1, Tok.Str); return i + 1; }
                if (c == '$') { int e = ShVarEnd(s, i, n); if (e > i + 1 && IdStart(s[i + 1]) || e > i + 1 && s[i + 1] == '{') { Add(r, a, i, Tok.Str); Add(r, i, e, Tok.Text); i = e; a = e; continue; } }
                i++;
            }
            if (i > n) i = n;
            Add(r, a, i, Tok.Str); return i;
        }

        // ================= HCL (Terraform) =================
        // state: "*" — внутри /* */, "h:СТОП" / "h-:СТОП" — тело heredoc
        static readonly HashSet<string> HclRefs = new HashSet<string> { "var", "local", "module", "data", "each", "count", "self", "path", "terraform" };
        static readonly HashSet<string> HclTypes = new HashSet<string> { "string", "number", "bool", "any", "list", "map", "set", "object", "tuple" };
        static readonly HashSet<string> HclCtl = new HashSet<string> { "for", "in", "if", "else", "endif", "endfor" };

        static void Hcl(string s, List<Span> r, ref string state)
        {
            int i = 0, n = s.Length;
            string st = state; state = null;
            if (st != null && st.StartsWith("h", StringComparison.Ordinal))
            {
                string stop = st.Substring(st.IndexOf(':') + 1);
                int a = SkipSp(s, 0, n);
                if (s.Trim() == stop) { Add(r, a, a + stop.Length, Tok.Keyword); return; }
                Add(r, a, n, Tok.Str); state = st; return;
            }
            if (st == "*")
            {
                int e = s.IndexOf("*/", StringComparison.Ordinal);
                if (e < 0) { Add(r, 0, n, Tok.Comment); state = "*"; return; }
                Add(r, 0, e + 2, Tok.Comment); i = e + 2;
            }
            string heredoc = null;
            state = HclRange(s, i, n, r, true, ref heredoc);
            if (state == null && heredoc != null) state = heredoc;
        }

        // Разбор выражений HCL в s[i..n); first — в начале строки (там может быть тип блока или имя атрибута).
        // Возвращает "*", если строка кончилась внутри блочного комментария.
        static string HclRange(string s, int i, int n, List<Span> r, bool first, ref string heredoc)
        {
            bool dot = false;   // имя после «.» — атрибут (var.list, count.index)
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '#' || (c == '/' && i + 1 < n && s[i + 1] == '/')) { Add(r, i, n, Tok.Comment); break; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, n - i - 2, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); return "*"; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; continue;
                }
                if (c == '"') { i = HclString(s, i, n, r); first = false; continue; }
                if (c == '<' && i + 1 < n && s[i + 1] == '<')
                {
                    int j = i + 2; bool dash = j < n && s[j] == '-'; if (dash) j++;
                    int k = j; while (k < n && Id(s[k])) k++;
                    if (k > j) { Add(r, i, j, Tok.Op); Add(r, j, k, Tok.Keyword); heredoc = (dash ? "h-:" : "h:") + s.Substring(j, k - j); i = k; first = false; continue; }
                }
                if (Dig(c)) { int j = NumEnd(s, i, n); Add(r, i, j, Tok.Num); i = j; first = false; continue; }
                if (IdStart(c))
                {
                    int j = i; while (j < n && (Id(s[j]) || s[j] == '-')) j++;
                    string w = s.Substring(i, j - i);
                    int k = SkipSp(s, j, n); char nx = k < n ? s[k] : '\0';
                    Tok t;
                    if (dot) t = nx == '(' ? Tok.Func : Tok.Text;
                    else if (first && (nx == '"' || nx == '{' || (IdStart(nx) && k > j))) t = Tok.Keyword;     // тип блока: resource "…" "…" {
                    else if (first && nx == '=' && !(k + 1 < n && s[k + 1] == '=')) t = Tok.Text;              // имя атрибута
                    else if (w == "true" || w == "false" || w == "null") t = Tok.Keyword;
                    else if (HclCtl.Contains(w)) t = Tok.Control;
                    else if (HclTypes.Contains(w) && (nx != '(' || w == "list" || w == "map" || w == "set" || w == "object" || w == "tuple")) t = Tok.Class;
                    else if (nx == '(') t = Tok.Func;
                    else if (nx == '.' && HclRefs.Contains(w)) t = Tok.Keyword;
                    else t = Tok.Text;
                    Add(r, i, j, t); i = j; first = false; dot = false; continue;
                }
                int o = i; while (o < n && "=!<>&|+-*/%?:".IndexOf(s[o]) >= 0 && !(o > i && (s[o] == '/' || s[o] == '#'))) o++;
                if (o > i) { Add(r, i, o, Tok.Op); i = o; first = false; dot = false; continue; }
                int one = One(s, i, n); Add(r, i, one, Tok.Text); i = one; first = false; dot = c == '.';
            }
            return null;
        }

        // "строка ${выражение} %{ if … }" — интерполяция подсвечивается как код
        static int HclString(string s, int i, int n, List<Span> r)
        {
            int a = i; i++;
            while (i < n)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '"') { Add(r, a, i + 1, Tok.Str); return i + 1; }
                if ((c == '$' || c == '%') && i + 2 < n && s[i + 1] == c && s[i + 2] == '{') { i += 3; continue; }   // $${ и %%{ — экранирование
                if ((c == '$' || c == '%') && i + 1 < n && s[i + 1] == '{')
                {
                    Add(r, a, i, Tok.Str); Add(r, i, i + 2, Tok.Keyword);
                    int e = MatchBrace(s, i + 2, n);
                    string hd = null; HclRange(s, i + 2, e, r, false, ref hd);
                    if (e < n) Add(r, e, e + 1, Tok.Keyword);
                    i = Math.Min(n, e + 1); a = i; continue;
                }
                i++;
            }
            if (i > n) i = n;
            Add(r, a, i, Tok.Str); return i;
        }

        // позиция парной «}» (или n), пропуская строки
        static int MatchBrace(string s, int i, int n)
        {
            int d = 1;
            while (i < n)
            {
                char c = s[i];
                if (c == '"') { i = StrEnd(s, i, n, '"', true); continue; }
                if (c == '{') d++;
                else if (c == '}' && --d == 0) return i;
                i++;
            }
            return n;
        }

        // ================= PromQL =================
        static readonly HashSet<string> PromKw = new HashSet<string> { "by", "without", "on", "ignoring", "group_left", "group_right", "bool", "offset", "and", "or", "unless" };
        static readonly HashSet<string> PromLabels = new HashSet<string> { "by", "without", "on", "ignoring", "group_left", "group_right" };
        static readonly HashSet<string> PromFn = new HashSet<string> { "sum", "avg", "min", "max", "count", "stddev", "stdvar", "topk", "bottomk", "quantile",
            "count_values", "group", "rate", "irate", "increase", "delta", "idelta", "deriv", "predict_linear", "histogram_quantile", "abs", "ceil", "floor", "round",
            "clamp", "clamp_max", "clamp_min", "label_replace", "label_join", "vector", "scalar", "time", "timestamp", "absent", "absent_over_time", "changes",
            "resets", "sort", "sort_desc", "avg_over_time", "min_over_time", "max_over_time", "sum_over_time", "count_over_time", "quantile_over_time",
            "stddev_over_time", "last_over_time", "present_over_time", "exp", "ln", "log2", "log10", "sqrt", "sgn", "day_of_week", "hour", "minute", "month",
            "year", "days_in_month", "holt_winters", "histogram_count", "histogram_sum", "histogram_fraction", "atan2" };

        static void Prom(string s, List<Span> r)
        {
            int i = 0, n = s.Length, depth = 0, labelDepth = -1;
            bool braces = false, labelsNext = false;
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '#') { Add(r, i, n, Tok.Comment); break; }
                if (c == '"' || c == '\'' || c == '`') { int j = StrEnd(s, i, n, c, c != '`'); Add(r, i, j, Tok.Str); i = j; continue; }
                if (c == '{') { braces = true; Add(r, i, i + 1, Tok.Text); i++; continue; }
                if (c == '}') { braces = false; Add(r, i, i + 1, Tok.Text); i++; continue; }
                if (c == '(') { depth++; if (labelsNext) { labelDepth = depth; labelsNext = false; } Add(r, i, i + 1, Tok.Text); i++; continue; }
                if (c == ')') { if (depth == labelDepth) labelDepth = -1; depth = Math.Max(0, depth - 1); Add(r, i, i + 1, Tok.Text); i++; continue; }
                if (Dig(c) || (c == '.' && i + 1 < n && Dig(s[i + 1]))) { int j = NumEnd(s, i, n); Add(r, i, j, Tok.Num); i = j; continue; }
                if (IdStart(c))
                {
                    int j = i; while (j < n && (Id(s[j]) || s[j] == ':')) j++;
                    string w = s.Substring(i, j - i);
                    int k = SkipSp(s, j, n); char nx = k < n ? s[k] : '\0';
                    Tok t;
                    if (braces || labelDepth >= 0) t = Tok.Text;                    // имена лейблов
                    else if (PromKw.Contains(w)) { t = Tok.Keyword; if (PromLabels.Contains(w)) labelsNext = true; }
                    else if (PromFn.Contains(w) && nx != '{' && nx != '[') t = Tok.Builtin;
                    else if (nx == '(') t = Tok.Func;
                    else t = Tok.Class;                                              // имя метрики
                    Add(r, i, j, t); i = j; continue;
                }
                int o = i; while (o < n && "+-*/%^=!<>~".IndexOf(s[o]) >= 0) o++;
                if (o > i) { Add(r, i, o, Tok.Op); i = o; continue; }
                int one = One(s, i, n); Add(r, i, one, c == '@' ? Tok.Op : Tok.Text); i = one;
            }
        }

        // ================= ключевые слова и автодополнение =================
        static readonly string[] PyKwList = { "if", "elif", "else", "while", "for", "in", "return", "break", "continue", "pass", "try", "except", "finally", "raise",
            "import", "from", "as", "with", "yield", "del", "global", "nonlocal", "assert", "lambda", "def", "class", "and", "or", "not", "is", "None", "True", "False" };
        static readonly string[] HtmlTags = { "html", "head", "body", "title", "meta", "link", "script", "style", "div", "span", "p", "a", "img", "ul", "ol", "li",
            "table", "thead", "tbody", "tr", "th", "td", "form", "input", "button", "label", "select", "option", "textarea", "h1", "h2", "h3", "h4", "h5", "h6",
            "header", "footer", "nav", "main", "section", "article", "aside", "br", "hr", "strong", "em", "code", "pre", "blockquote", "iframe", "video", "audio",
            "source", "canvas", "svg", "template", "dialog", "details", "summary", "figure", "figcaption", "small", "time", "fieldset", "legend" };
        static readonly string[] HtmlAttrList = { "class", "id", "href", "src", "alt", "title", "type", "name", "value", "placeholder", "required", "disabled", "checked",
            "readonly", "style", "for", "action", "method", "target", "rel", "lang", "charset", "content", "width", "height", "hidden", "tabindex", "role",
            "aria-label", "onclick", "onchange", "onsubmit", "autocomplete", "autofocus", "minlength", "maxlength", "pattern", "min", "max", "step", "multiple",
            "selected", "loading", "defer", "async" };
        static readonly string[] CssAtRules = { "media", "import", "keyframes", "font-face", "supports", "layer", "container", "charset", "important" };
        static readonly string[] CssProps = { "display", "position", "top", "right", "bottom", "left", "width", "height", "min-width", "max-width", "min-height",
            "max-height", "margin", "margin-top", "margin-right", "margin-bottom", "margin-left", "padding", "padding-top", "padding-right", "padding-bottom",
            "padding-left", "border", "border-radius", "border-color", "border-width", "border-style", "border-top", "border-bottom", "color", "background",
            "background-color", "background-image", "background-size", "font", "font-size", "font-weight", "font-family", "font-style", "line-height", "text-align",
            "text-decoration", "text-transform", "letter-spacing", "white-space", "overflow", "overflow-x", "overflow-y", "opacity", "z-index", "flex",
            "flex-direction", "flex-wrap", "flex-grow", "flex-shrink", "flex-basis", "justify-content", "align-items", "align-self", "align-content", "gap",
            "row-gap", "column-gap", "grid", "grid-template-columns", "grid-template-rows", "grid-template-areas", "grid-column", "grid-row", "grid-area",
            "transition", "transform", "animation", "box-shadow", "box-sizing", "cursor", "content", "visibility", "outline", "object-fit", "vertical-align",
            "list-style", "pointer-events", "user-select", "aspect-ratio", "inset" };
        static readonly string[] CssFuncs = { "rgb", "rgba", "hsl", "hsla", "var", "calc", "url", "linear-gradient", "radial-gradient", "min", "max", "clamp",
            "repeat", "minmax", "translate", "translateX", "translateY", "rotate", "scale" };
        static readonly string[] CssValues = { "flex", "grid", "block", "inline", "inline-block", "inline-flex", "none", "auto", "center", "space-between",
            "space-around", "space-evenly", "flex-start", "flex-end", "stretch", "relative", "absolute", "fixed", "sticky", "static", "solid", "dashed", "bold",
            "normal", "hidden", "visible", "scroll", "pointer", "inherit", "initial", "transparent", "wrap", "nowrap", "column", "row", "uppercase", "lowercase",
            "ease", "ease-in-out", "linear", "cover", "contain", "border-box" };
        static readonly string[] ShCmds = { "echo", "printf", "cd", "ls", "pwd", "cat", "grep", "sed", "awk", "find", "chmod", "chown", "mkdir", "rm", "cp", "mv",
            "touch", "ln", "curl", "wget", "tar", "gzip", "ssh", "scp", "rsync", "docker", "kubectl", "git", "sudo", "systemctl", "journalctl", "ps", "kill", "top",
            "df", "du", "head", "tail", "sort", "uniq", "wc", "xargs", "tee", "sleep", "date", "whoami", "source", "test", "read", "set", "trap", "crontab",
            "useradd", "apt-get", "pip", "npm", "python3", "node" };
        static readonly string[] HclKwList = { "resource", "data", "variable", "output", "locals", "module", "provider", "terraform", "backend",
            "required_providers", "lifecycle", "dynamic", "for_each", "count", "depends_on", "source", "version", "true", "false", "null", "for", "in", "if" };
        static readonly string[] HclFuncs = { "length", "lookup", "merge", "concat", "join", "split", "format", "element", "file", "jsonencode", "jsondecode", "tomap",
            "toset", "tolist", "cidrsubnet", "coalesce", "keys", "values", "upper", "lower", "replace", "contains", "try", "can", "templatefile", "flatten", "distinct" };
        static readonly string[] JsMethods = { "log", "error", "warn", "info", "table", "map", "filter", "reduce", "forEach", "find", "findIndex", "some", "every",
            "includes", "indexOf", "push", "pop", "shift", "unshift", "slice", "splice", "concat", "join", "sort", "reverse", "flat", "flatMap", "keys", "values",
            "entries", "split", "trim", "toLowerCase", "toUpperCase", "startsWith", "endsWith", "replace", "replaceAll", "padStart", "toFixed", "toString",
            "then", "catch", "finally", "json", "stringify", "parse", "floor", "ceil", "round", "random", "max", "min", "abs", "querySelector",
            "querySelectorAll", "getElementById", "addEventListener", "removeEventListener", "preventDefault", "stopPropagation", "setAttribute",
            "appendChild", "toLocaleString", "assign", "freeze", "isArray", "from", "all", "resolve", "reject" };
        static readonly string[] JsProps = { "length", "textContent", "innerHTML", "value", "target", "classList", "dataset", "style", "children", "status", "ok" };
        static readonly string[] ReactHooks = { "useState", "useEffect", "useMemo", "useCallback", "useRef", "useContext", "useReducer", "useLayoutEffect" };
        static readonly string[] YamlKeys = { "apiVersion", "kind", "metadata", "name", "namespace", "labels", "annotations", "spec", "replicas", "selector",
            "matchLabels", "template", "containers", "image", "ports", "containerPort", "env", "value", "valueFrom", "resources", "limits", "requests",
            "services", "volumes", "environment", "depends_on", "build", "command", "restart", "healthcheck", "steps", "jobs", "runs-on", "uses", "with", "run" };

        static string[] Concat(params IEnumerable<string>[] parts) { var l = new List<string>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }
        static string[] jsKwCache, tsKwCache, sqlKwCache, shKwCache;

        // Ключевые слова языка (для SQL — заглавными, для HTML — имена тегов, для nginx — директивы)
        public static string[] Keywords(string lang)
        {
            switch (Norm(lang))
            {
                case "python": return (string[])PyKwList.Clone();
                case "javascript": case "jsx": return (string[])(jsKwCache ?? (jsKwCache = Concat(JsControl, JsKw))).Clone();
                case "typescript": case "tsx": return (string[])(tsKwCache ?? (tsKwCache = Concat(JsControl, JsKw, TsKw, TsTypes))).Clone();
                case "sql": return (string[])(sqlKwCache ?? (sqlKwCache = Concat(SqlKw, SqlCtl))).Clone();
                case "yaml": return new[] { "true", "false", "null" };
                case "bash": return (string[])(shKwCache ?? (shKwCache = Concat(ShCtl, ShKw, new[] { "in" }))).Clone();
                case "dockerfile": return Concat(DockerInstrList, new[] { "AS" });
                case "html": return (string[])HtmlTags.Clone();
                case "css": return (string[])CssAtRules.Clone();
                case "nginx": return (string[])NginxDirectives.Clone();
                case "hcl": return (string[])HclKwList.Clone();
                case "promql": return Concat(PromKw);
                default: return new string[0];
            }
        }

        static readonly Regex WordsJs = new Regex(@"(?<![\w$])[A-Za-z_$][A-Za-z0-9_$]*");
        static readonly Regex WordsDash = new Regex(@"(?<![\w-])-{0,2}[A-Za-z_][A-Za-z0-9_-]*");
        static readonly Regex WordsPlain = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]*\b");

        // Автодополнение: python — PySyntax.Complete; остальные — ключевые слова, встроенное языка и имена из кода
        public static List<Completion> Complete(string lang, string prefix, string code)
        {
            lang = Norm(lang);
            if (lang == "python") return PySyntax.Complete(prefix, code);
            var res = new List<Completion>();
            if (string.IsNullOrEmpty(prefix) || lang == "text") return res;
            bool sql = lang == "sql", upper = sql && char.IsUpper(prefix[0]);
            var seen = new HashSet<string>(sql ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            bool js = IsJs(lang), ts = lang == "typescript" || lang == "tsx";
            Action<IEnumerable<string>, string, char, string, bool> add = (words, detail, icon, color, call) =>
            {
                foreach (var w0 in words)
                {
                    string w = sql ? (upper ? w0.ToUpperInvariant() : w0.ToLowerInvariant()) : w0;
                    if (w.Length <= prefix.Length || !w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !seen.Add(w)) continue;
                    res.Add(new Completion { Label = w, Insert = call ? w + "()" : w, CaretBack = call ? 1 : 0, Detail = detail, Icon = icon, IconColor = Pal.Hex(color) });
                }
            };
            const string kwC = "C586C0", fnC = "B180D7", varC = "75BEFF", clsC = "EE9D28", tagC = "569CD6";
            switch (lang)
            {
                case "javascript": case "typescript": case "jsx": case "tsx":
                    add(Keywords(lang), "ключевое слово", 'k', kwC, false);
                    add(JsClasses, "объект", 'C', clsC, false);
                    add(JsFuncs, "функция", 'ƒ', fnC, true);
                    add(ReactHooks, "хук React", 'ƒ', fnC, true);
                    add(JsMethods, "метод", 'ƒ', fnC, true);
                    add(JsProps, "свойство", 'p', varC, false);
                    break;
                case "sql":
                    add(Keywords(lang), "ключевое слово", 'k', kwC, false);
                    add(SqlFuncs, "функция", 'ƒ', fnC, true);
                    break;
                case "yaml": add(Keywords(lang), "значение", 'k', kwC, false); add(YamlKeys, "ключ", 'p', tagC, false); break;
                case "bash": add(Keywords(lang), "ключевое слово", 'k', kwC, false); add(ShCmds, "команда", 'ƒ', fnC, false); break;
                case "dockerfile": add(Keywords(lang), "инструкция", 'k', kwC, false); break;
                case "html": add(HtmlTags, "тег", 't', tagC, false); add(HtmlAttrList, "атрибут", 'p', varC, false); break;
                case "css":
                    add(CssProps, "свойство", 'p', varC, false); add(CssFuncs, "функция", 'ƒ', fnC, true);
                    add(CssValues, "значение", 'v', "CE9178", false); add(CssAtRules, "@-правило", 'k', kwC, false);
                    break;
                case "nginx": add(NginxDirectives, "директива", 'k', kwC, false); break;
                case "hcl": add(HclKwList, "ключевое слово", 'k', kwC, false); add(HclFuncs, "функция", 'ƒ', fnC, true); add(HclTypes, "тип", 'C', clsC, false); break;
                case "promql": add(PromKw, "ключевое слово", 'k', kwC, false); add(PromFn, "функция", 'ƒ', fnC, true); break;
            }
            // имена из кода: переменные, свои функции, классы CSS, таблицы…
            var kws = new HashSet<string>(Keywords(lang), sql ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var rx = js ? WordsJs : (lang == "css" || lang == "html" || lang == "yaml" || lang == "hcl") ? WordsDash : WordsPlain;
            foreach (Match m in rx.Matches(code ?? ""))
            {
                string w = m.Value;
                if (w.Length < 2 || w.Length <= prefix.Length || kws.Contains(w) || !w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !seen.Add(w)) continue;
                bool fn = false;
                if (js) fn = Regex.IsMatch(code, @"\bfunction\s+" + Regex.Escape(w) + @"\b|(?<![\w$])" + Regex.Escape(w) + @"\s*=\s*(?:async\s*)?(?:\([^()]*\)|[A-Za-z_$][\w$]*)\s*(?::[^=]*)?=>");
                else if (lang == "bash") fn = Regex.IsMatch(code, @"\bfunction\s+" + Regex.Escape(w) + @"\b|(?<![\w-])" + Regex.Escape(w) + @"\s*\(\)");
                res.Add(new Completion { Label = w, Insert = fn && js ? w + "()" : w, CaretBack = fn && js ? 1 : 0, Detail = fn ? "функция" : js || lang == "bash" ? "переменная" : "из кода",
                    Icon = fn ? 'ƒ' : 'x', IconColor = fn ? Pal.Hex(fnC) : Pal.Hex(varC) });
            }
            res.Sort((a, b) =>
            {
                int pa = a.Label.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1, pb = b.Label.StartsWith(prefix, StringComparison.Ordinal) ? 0 : 1;
                return pa != pb ? pa - pb : a.Label.Length - b.Label.Length;
            });
            if (res.Count > 9) res.RemoveRange(9, res.Count - 9);
            return res;
        }
    }
}
