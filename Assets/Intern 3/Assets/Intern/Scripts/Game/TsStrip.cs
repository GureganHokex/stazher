// Спринт 9: TypeScript в игре. Типы стираются, остаётся JavaScript, который выполняет Jint (как в esbuild/babel:
// никакой проверки типов, только удаление). Поддержано то, что встречается в задачах: аннотации переменных,
// параметров и результатов, interface/type/declare, дженерики функций, классов и вызовов, as/satisfies, x!,
// модификаторы полей и свойства-параметры конструктора, enum (числовые и строковые), implements, import type.
// Сами типы игра проверяет регулярками из задачи (content.requirements), как статические проверки.
using System;
using System.Collections.Generic;
using System.Text;

namespace Intern.Game
{
    public static class TsStrip
    {
        enum K { Space, Comment, Str, Tpl, Num, Id, Punct, Regex }
        struct T { public K k; public string s; public int line; }

        static readonly HashSet<string> Mods = new HashSet<string> { "public", "private", "protected", "readonly", "abstract", "override", "declare" };
        static readonly HashSet<string> NotMethod = new HashSet<string> { "if", "for", "while", "switch", "catch", "with", "return", "typeof", "function", "await", "new", "delete", "void", "in", "of", "instanceof", "yield", "super", "import" };
        static readonly HashSet<string> ExprKw = new HashSet<string> { "return", "typeof", "instanceof", "in", "of", "new", "delete", "void", "throw", "case", "do", "else", "yield", "await" };

        public static string Strip(string src)
        {
            var toks = Lex(src ?? "");
            var s = new Stripper(toks);
            return s.Run();
        }

        // ======================= лексер =======================
        static List<T> Lex(string s)
        {
            var r = new List<T>(); int i = 0, n = s.Length, line = 1;
            Func<K, int, int, T> mk = (k, a, b) => new T { k = k, s = s.Substring(a, b - a), line = line };
            while (i < n)
            {
                char c = s[i]; int a = i;
                if (char.IsWhiteSpace(c)) { while (i < n && char.IsWhiteSpace(s[i])) { if (s[i] == '\n') line++; i++; } r.Add(mk(K.Space, a, i)); continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { while (i < n && s[i] != '\n') i++; r.Add(mk(K.Comment, a, i)); continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? n : e + 2; var t = mk(K.Comment, a, i); r.Add(t); foreach (var ch in t.s) if (ch == '\n') line++; continue; }
                if (c == '"' || c == '\'') { i++; while (i < n && s[i] != c && s[i] != '\n') { if (s[i] == '\\') i++; i++; } i = Math.Min(n, i + 1); r.Add(mk(K.Str, a, i)); continue; }
                if (c == '`') { i = TplEnd(s, i + 1); var t = mk(K.Tpl, a, i); r.Add(t); foreach (var ch in t.s) if (ch == '\n') line++; continue; }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1]))) { i++; while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '.' || s[i] == '_')) i++; r.Add(mk(K.Num, a, i)); continue; }
                if (char.IsLetter(c) || c == '_' || c == '$' || c == '#') { i++; while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '$')) i++; r.Add(mk(K.Id, a, i)); continue; }
                if (c == '/' && RegexAllowed(r)) { i++; bool cls = false; while (i < n && s[i] != '\n') { if (s[i] == '\\') { i += 2; continue; } if (s[i] == '[') cls = true; else if (s[i] == ']') cls = false; else if (s[i] == '/' && !cls) break; i++; } i = Math.Min(n, i + 1); while (i < n && char.IsLetter(s[i])) i++; r.Add(mk(K.Regex, a, i)); continue; }
                // операторы: длинные сначала
                string[] ops = { ">>>=", "...", "===", "!==", "**=", "<<=", ">>=", "=>", "==", "!=", "<=", ">=", "&&", "||", "??", "?.", "++", "--", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**" };
                string op = null;
                foreach (var o in ops) if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) { op = o; break; }
                if (op == "?." && i + 2 < n && char.IsDigit(s[i + 2])) op = null;   // a ?.5 : b
                int len = op != null ? op.Length : 1;
                r.Add(mk(K.Punct, i, i + len)); i += len;
            }
            return r;
        }

        static int TplEnd(string s, int i)
        {
            int n = s.Length;
            while (i < n)
            {
                if (s[i] == '\\') { i += 2; continue; }
                if (s[i] == '`') return i + 1;
                if (s[i] == '$' && i + 1 < n && s[i + 1] == '{')
                {
                    int depth = 1; i += 2;
                    while (i < n && depth > 0)
                    {
                        if (s[i] == '`') { i = TplEnd(s, i + 1); continue; }
                        if (s[i] == '"' || s[i] == '\'') { char q = s[i++]; while (i < n && s[i] != q) { if (s[i] == '\\') i++; i++; } i++; continue; }
                        if (s[i] == '{') depth++; else if (s[i] == '}') depth--;
                        i++;
                    }
                    continue;
                }
                i++;
            }
            return n;
        }

        static bool RegexAllowed(List<T> r)
        {
            for (int j = r.Count - 1; j >= 0; j--)
            {
                if (r[j].k == K.Space || r[j].k == K.Comment) continue;
                if (r[j].k == K.Num || r[j].k == K.Str || r[j].k == K.Tpl || r[j].k == K.Regex) return false;
                if (r[j].k == K.Id) return ExprKw.Contains(r[j].s);
                return r[j].s != ")" && r[j].s != "]" && r[j].s != "}" && r[j].s != "++" && r[j].s != "--";
            }
            return true;
        }

        // ======================= стирание =======================
        class Stripper
        {
            readonly List<T> t; readonly bool[] del; readonly string[] repl; readonly List<int> sig = new List<int>();   // индексы значимых токенов
            readonly Dictionary<int, string> insertAfter = new Dictionary<int, string>();
            public Stripper(List<T> toks)
            {
                t = toks; del = new bool[t.Count]; repl = new string[t.Count];
                for (int i = 0; i < t.Count; i++) if (t[i].k != K.Space && t[i].k != K.Comment) sig.Add(i);
            }

            string S(int p) { return p >= 0 && p < sig.Count ? t[sig[p]].s : ""; }
            K Kind(int p) { return p >= 0 && p < sig.Count ? t[sig[p]].k : K.Space; }
            bool Is(int p, string v) { return S(p) == v && (Kind(p) == K.Punct || Kind(p) == K.Id); }
            int Line(int p) { return p >= 0 && p < sig.Count ? t[sig[p]].line : int.MaxValue; }
            void Del(int a, int b) { for (int p = a; p < b && p < sig.Count; p++) del[sig[p]] = true; }
            void DelRawRange(int a, int b)   // значимые a..b-1 и всё между ними (пробелы и комментарии внутри типа)
            {
                if (a >= b) return;
                for (int i = sig[a]; i <= sig[Math.Min(b, sig.Count) - 1]; i++) del[i] = true;
            }

            int Match(int p)   // p на открывающей скобке ( [ { — позиция после парной
            {
                string o = S(p); string c = o == "(" ? ")" : o == "[" ? "]" : "}";
                int depth = 0;
                for (int q = p; q < sig.Count; q++)
                {
                    if (Kind(q) != K.Punct) continue;
                    string v = S(q);
                    if (v == "(" || v == "[" || v == "{") depth++;
                    else if (v == ")" || v == "]" || v == "}") { depth--; if (depth == 0) return v == c ? q + 1 : q + 1; }
                }
                return sig.Count;
            }

            int MatchAngle(int p)   // p на «<» — позиция после парной «>» или −1
            {
                int depth = 0;
                for (int q = p; q < sig.Count; q++)
                {
                    string v = S(q);
                    if (Kind(q) == K.Punct)
                    {
                        if (v == "<") depth++;
                        else if (v == ">") { depth--; if (depth == 0) return q + 1; }
                        else if (v == ">>") { depth -= 2; if (depth <= 0) return q + 1; }
                        else if (v == ">=" || v == ";" || v == "{" && depth == 0 || v == ")" && depth == 1 && false) return -1;
                        else if (v == "(" || v == "[" || v == "{") { q = Match(q) - 1; continue; }
                        else if (v == ")" || v == "]" || v == "}" || v == "&&" || v == "||" || v == "=") return -1;
                    }
                }
                return -1;
            }

            // ---------- разбор типа: позиция после типа ----------
            int Type(int p)
            {
                if (Is(p, "|") || Is(p, "&")) p++;
                p = TypeInter(p);
                while (Is(p, "|")) p = TypeInter(p + 1);
                // условный тип: A extends B ? C : D
                if (Is(p, "extends")) { p = Type(p + 1); if (Is(p, "?")) { p = Type(p + 1); if (Is(p, ":")) p = Type(p + 1); } }
                return p;
            }
            int TypeInter(int p) { p = TypePostfix(p); while (Is(p, "&")) p = TypePostfix(p + 1); return p; }
            int TypePostfix(int p)
            {
                p = TypePrimary(p);
                while (true)
                {
                    if (Is(p, "[") ) { p = Match(p); continue; }   // T[] и T[K]
                    break;
                }
                return p;
            }
            int TypePrimary(int p)
            {
                string v = S(p);
                if (Kind(p) == K.Id && (v == "keyof" || v == "typeof" || v == "readonly" || v == "unique" || v == "infer")) return TypePrimary(p + 1);
                if (v == "new" && Is(p + 1, "(")) p++;
                if (Is(p, "("))
                {
                    int q = Match(p);
                    if (Is(q, "=>")) return Type(q + 1);   // функция (a: T) => R
                    return q;
                }
                if (Is(p, "<")) { int q = MatchAngle(p); if (q > 0 && Is(q, "(")) { q = Match(q); if (Is(q, "=>")) return Type(q + 1); } }
                if (Is(p, "{") || Is(p, "[")) return Match(p);
                if (Kind(p) == K.Str || Kind(p) == K.Num || Kind(p) == K.Tpl) return p + 1;
                if (Is(p, "-") && Kind(p + 1) == K.Num) return p + 2;
                if (Kind(p) == K.Id)
                {
                    p++;
                    while (Is(p, ".") && Kind(p + 1) == K.Id) p += 2;
                    if (Is(p, "<")) { int q = MatchAngle(p); if (q > 0) p = q; }
                    // предикат типа: x is string
                    if (Is(p, "is") && Kind(p + 1) == K.Id) return Type(p + 1);
                    return p;
                }
                return p + 1;
            }

            // ---------- основной проход ----------
            readonly Stack<string> ctx = new Stack<string>();   // для каждой открытой { ( [ : "class", "block", "obj", "params", "paren", "brack", "enum"

            public string Run()
            {
                var ctorProps = new List<string>(); bool pendingCtorBody = false;
                string pendingBrace = null;   // что откроет следующая «{»: class
                for (int p = 0; p < sig.Count; p++)
                {
                    if (del[sig[p]]) continue;
                    string v = S(p); K k = Kind(p);
                    string top = ctx.Count > 0 ? ctx.Peek() : "block";
                    bool stmtStart = StmtStart(p);

                    if (k == K.Id)
                    {
                        // interface / type / declare / import type / abstract / enum
                        if (stmtStart || (Is(p - 1, "export") && StmtStart(p - 1)))
                        {
                            int exp = Is(p - 1, "export") ? p - 1 : p;
                            if (v == "interface" && Kind(p + 1) == K.Id)
                            {
                                int q = p + 2; while (q < sig.Count && !Is(q, "{")) q++;
                                q = Match(q); DelRawRange(exp, q); p = q - 1; continue;
                            }
                            if (v == "type" && Kind(p + 1) == K.Id && (Is(p + 2, "=") || Is(p + 2, "<")))
                            {
                                int q = p + 2; if (Is(q, "<")) q = MatchAngle(q); q = Type(q + 1); if (Is(q, ";")) q++;
                                DelRawRange(exp, q); p = q - 1; continue;
                            }
                            if (v == "declare") { int q = StmtEnd(p); DelRawRange(exp, q); p = q - 1; continue; }
                            if (v == "import" && Is(p + 1, "type")) { int q = StmtEnd(p); DelRawRange(p, q); p = q - 1; continue; }
                            if (v == "abstract" && Is(p + 1, "class")) { Del(p, p + 1); continue; }
                            if ((v == "enum" && Kind(p + 1) == K.Id) || (v == "const" && Is(p + 1, "enum") && Kind(p + 2) == K.Id))
                            {
                                int e = v == "const" ? p + 1 : p;
                                p = Enum(v == "const" ? p : p, e) - 1; continue;
                            }
                        }
                        // объявления переменных: let x: T = …, let x!: T
                        if ((v == "let" || v == "const" || v == "var") && !Is(p + 1, "enum"))
                        {
                            int q = p + 1;
                            while (q < sig.Count)
                            {
                                if (Is(q, "{") || Is(q, "[")) q = Match(q);   // деструктуризация
                                else if (Kind(q) == K.Id) q++;
                                else break;
                                if (Is(q, "!") && Is(q + 1, ":")) { Del(q, q + 1); q++; }
                                if (Is(q, ":")) { int e = Type(q + 1); DelRawRange(q, e); q = e; }
                                if (Is(q, "=")) q = ExprEnd(q + 1);   // инициализатор: остальное разберём обычным ходом
                                if (Is(q, ",") ) { q++; continue; }
                                break;
                            }
                            continue;
                        }
                        // class Name<T> extends B<T> implements I { — дженерики и implements
                        if (v == "class")
                        {
                            int q = p + 1;
                            if (Kind(q) == K.Id && !Is(q, "extends") && !Is(q, "implements")) q++;
                            if (Is(q, "<")) { int e = MatchAngle(q); if (e > 0) { DelRawRange(q, e); q = e; } }
                            if (Is(q, "extends")) { q++; while ((Kind(q) == K.Id && !Is(q, "implements")) || Is(q, ".")) q++; if (Is(q, "<")) { int e = MatchAngle(q); if (e > 0) { DelRawRange(q, e); q = e; } } if (Is(q, "(")) q = Match(q); }
                            if (Is(q, "implements")) { int e = q + 1; while (e < sig.Count && !Is(e, "{")) e++; DelRawRange(q, e); q = e; }
                            pendingBrace = "class";
                            continue;
                        }
                        // function name<T>(params): R {
                        if (v == "function")
                        {
                            int q = p + 1; if (Is(q, "*")) q++;
                            if (Kind(q) == K.Id) q++;
                            if (Is(q, "<")) { int e = MatchAngle(q); if (e > 0) { DelRawRange(q, e); q = e; } }
                            if (Is(q, "(")) { q = Params(q, null); ReturnType(q); }
                            continue;
                        }
                        // члены класса: модификаторы, поля, методы
                        if (top == "class" && MemberStart(p))
                        {
                            int q = p;
                            while (Kind(q) == K.Id && (Mods.Contains(S(q)) || S(q) == "static" || S(q) == "async" || S(q) == "get" || S(q) == "set") && (Kind(q + 1) == K.Id || Is(q + 1, "[") || Is(q + 1, "*") || Kind(q + 1) == K.Str || S(q + 1) == "#"))
                            {
                                if (Mods.Contains(S(q))) Del(q, q + 1);
                                q++;
                            }
                            if (Is(q, "*")) q++;
                            // индексная сигнатура [key: string]: T;
                            if (Is(q, "[") && Kind(q + 1) == K.Id && Is(q + 2, ":")) { int e = StmtEnd(q); DelRawRange(p, e); p = e - 1; continue; }
                            int nameEnd = Is(q, "[") ? Match(q) : q + 1;
                            bool isCtor = S(q) == "constructor";
                            int m = nameEnd;
                            if (Is(m, "?") || Is(m, "!")) { Del(m, m + 1); m++; }
                            if (Is(m, "<")) { int e = MatchAngle(m); if (e > 0 && Is(e, "(")) { DelRawRange(m, e); m = e; } }
                            if (Is(m, "("))
                            {
                                var props = isCtor ? new List<string>() : null;
                                int e = Params(m, props);
                                e = ReturnType(e);
                                if (isCtor && props.Count > 0 && Is(e, "{"))
                                {
                                    var sb = new StringBuilder();
                                    foreach (var pr in props) sb.Append(" this.").Append(pr).Append(" = ").Append(pr).Append(";");
                                    // в наследнике this доступен только после super(…) — вставляем после этого вызова
                                    int at = e, bodyEnd = Match(e);
                                    for (int x = e + 1; x < bodyEnd; x++)
                                        if (Is(x, "super") && Is(x + 1, "(")) { at = Match(x + 1) - 1; if (Is(at + 1, ";")) at++; break; }
                                    insertAfter[sig[at]] = sb.ToString();
                                }
                                if (Is(e, ";")) { DelRawRange(p, e + 1); p = e; continue; }   // перегрузка или абстрактный метод без тела
                                p = m - 1;   // дальше обычный ход: «(» откроется в следующей итерации
                                continue;
                            }
                            if (Is(m, ":")) { int e = Type(m + 1); DelRawRange(m, e); }
                            continue;
                        }
                        // x as T, x satisfies T
                        if ((v == "as" || v == "satisfies") && ExprEndTok(p - 1) && !Is(p - 1, ";"))
                        {
                            if (v == "as" && Is(p + 1, "const")) { DelRawRange(p, p + 2); p++; continue; }
                            int e = Type(p + 1); DelRawRange(p, e); p = e - 1; continue;
                        }
                        // вызов с типами: f<T>(…), new Map<K, V>()
                        if (Is(p + 1, "<") && !Is(p - 1, "."))
                        {
                            int e = MatchAngle(p + 1);
                            if (e > 0 && (Is(e, "(") || (Is(p - 1, "new")))) { DelRawRange(p + 1, e); }
                        }
                        // метод объекта / функция-выражение без function: name(a: T): R {
                        bool propStart = Is(p - 1, "{") || Is(p - 1, ",") || Is(p - 1, "async") || Is(p - 1, "get") || Is(p - 1, "set") || Is(p - 1, "*");
                        if (!NotMethod.Contains(v) && Is(p + 1, "(") && (top == "obj" || top == "class") && propStart)
                        {
                            int e = Match(p + 1);
                            if (Is(e, "{") || Is(e, ":")) { Params(p + 1, null); ReturnType(e); }
                        }
                        continue;
                    }

                    if (k == K.Punct)
                    {
                        if (v == "(")
                        {
                            // стрелочная функция: (a: T, b?: U): R => …
                            int e = Match(p);
                            if (Is(e, "=>") || (Is(e, ":") && ArrowAfterType(e)))
                            { Params(p, null); ReturnType(e); }
                            ctx.Push("paren"); continue;
                        }
                        if (v == "<" && ExprStart(p))
                        {
                            // дженерик стрелки: <T,>(x: T) => x
                            int e = MatchAngle(p);
                            if (e > 0 && Is(e, "(")) { int m = Match(e); if (Is(m, "=>") || Is(m, ":")) { DelRawRange(p, e); } }
                            continue;
                        }
                        if (v == "[") { ctx.Push("brack"); continue; }
                        if (v == "{")
                        {
                            if (pendingBrace == "class") { ctx.Push("class"); pendingBrace = null; continue; }
                            ctx.Push(BraceIsObj(p) ? "obj" : "block"); continue;
                        }
                        if (v == ")" || v == "]" || v == "}") { if (ctx.Count > 0) ctx.Pop(); continue; }
                        if (v == "!" && ExprEndTok(p - 1) && !stmtStart)
                        {
                            string nx = S(p + 1);
                            if (nx == "." || nx == ")" || nx == "]" || nx == ";" || nx == "," || nx == "[" || nx == "?." || nx == "=" || nx == ":" || nx == "}" || Line(p + 1) > Line(p)) Del(p, p + 1);
                            continue;
                        }
                    }
                }
                var sb2 = new StringBuilder();
                for (int i = 0; i < t.Count; i++)
                {
                    if (del[i])
                    {
                        // перевод строки внутри удалённого — сохраняем, чтобы номера строк в ошибках совпадали
                        foreach (var ch in t[i].s) if (ch == '\n') sb2.Append('\n');
                        continue;
                    }
                    sb2.Append(repl[i] ?? t[i].s);
                    string ins; if (insertAfter.TryGetValue(i, out ins)) sb2.Append(ins);
                }
                return sb2.ToString();
            }

            bool StmtStart(int p)
            {
                if (p <= 0) return true;
                string v = S(p - 1);
                if (Kind(p - 1) == K.Punct && (v == ";" || v == "{" || v == "}")) return true;
                return Line(p - 1) < Line(p) && !Continues(p - 1);
            }
            bool Continues(int p) { string v = S(p); return Kind(p) == K.Punct && v != ")" && v != "]" && v != "}" && v != "++" && v != "--"; }

            bool MemberStart(int p)
            {
                if (p <= 0) return true;
                string v = S(p - 1);
                return (Kind(p - 1) == K.Punct && (v == ";" || v == "{" || v == "}")) || Line(p - 1) < Line(p);
            }

            bool ExprStart(int p)
            {
                if (p <= 0) return false;
                string v = S(p - 1); K k = Kind(p - 1);
                if (k == K.Id) return ExprKw.Contains(v);
                if (k == K.Punct) return v != ")" && v != "]" && v != "}";
                return false;
            }

            bool BraceIsObj(int p)
            {
                if (p <= 0) return false;
                string v = S(p - 1); K k = Kind(p - 1);
                if (k == K.Id) return v == "return" || v == "yield" || v == "await" || v == "typeof";
                if (k != K.Punct) return false;
                return v == "=" || v == "(" || v == "," || v == "[" || v == "?" || v == ":" || v == "||" || v == "&&" || v == "??" || v == "..." || v == "+" || v == "-";
            }

            bool ExprEndTok(int p)
            {
                K k = Kind(p); string v = S(p);
                if (k == K.Id) return !ExprKw.Contains(v);
                if (k == K.Num || k == K.Str || k == K.Tpl || k == K.Regex) return true;
                return v == ")" || v == "]" || v == "}";
            }

            int StmtEnd(int p)
            {
                int depth = 0;
                for (int q = p; q < sig.Count; q++)
                {
                    string v = S(q);
                    if (Kind(q) == K.Punct)
                    {
                        if (v == "{" || v == "(" || v == "[") depth++;
                        else if (v == "}" || v == ")" || v == "]") { depth--; if (depth == 0 && v == "}" && (Line(q + 1) > Line(q) || Is(q + 1, ";"))) return Is(q + 1, ";") ? q + 2 : q + 1; if (depth < 0) return q; }
                        else if (v == ";" && depth == 0) return q + 1;
                    }
                    if (depth == 0 && Line(q + 1) > Line(q) && !Continues(q) && !(Is(q + 1, "|") || Is(q + 1, "&") || Is(q + 1, "."))) return q + 1;
                }
                return sig.Count;
            }

            int ExprEnd(int p)   // конец инициализатора: «,» или «;» или конец строки на нулевой глубине
            {
                int depth = 0;
                for (int q = p; q < sig.Count; q++)
                {
                    string v = S(q);
                    if (Kind(q) == K.Punct)
                    {
                        if (v == "{" || v == "(" || v == "[") depth++;
                        else if (v == "}" || v == ")" || v == "]") { if (depth == 0) return q; depth--; }
                        else if ((v == "," || v == ";") && depth == 0) return q;
                    }
                    if (depth == 0 && Line(q + 1) > Line(q) && !Continues(q) && !Continues(q + 1)) return q + 1;
                }
                return sig.Count;
            }

            bool ArrowAfterType(int p) { int e = Type(p + 1); return Is(e, "=>"); }

            // Параметры: p на «(»; стирает типы, «?», модификаторы (props — имена свойств-параметров). Возвращает позицию после «)»
            int Params(int p, List<string> props)
            {
                int end = Match(p) - 1;   // позиция «)»
                int q = p + 1;
                while (q < end)
                {
                    // модификаторы: public name: T
                    bool prop = false;
                    while (Kind(q) == K.Id && Mods.Contains(S(q)) && (Kind(q + 1) == K.Id || Is(q + 1, "{") || Is(q + 1, "["))) { Del(q, q + 1); q++; prop = true; }
                    if (Is(q, "...")) q++;
                    int nameStart = q;
                    if (Is(q, "{") || Is(q, "[")) q = Match(q); else if (Kind(q) == K.Id) q++;
                    if (prop && props != null && q == nameStart + 1) props.Add(S(nameStart));
                    if (Is(q, "?")) { Del(q, q + 1); q++; }
                    if (Is(q, ":")) { int e = Type(q + 1); DelRawRange(q, e); q = e; }
                    if (Is(q, "=")) { q = ExprEnd(q + 1); }
                    // до следующей запятой на этом уровне
                    while (q < end && !Is(q, ",")) { if (Is(q, "(") || Is(q, "[") || Is(q, "{")) q = Match(q); else q++; }
                    if (Is(q, ",")) q++;
                }
                return end + 1;
            }

            int ReturnType(int p)   // p после «)»: «: R» перед { или =>
            {
                if (!Is(p, ":")) return p;
                int e = Type(p + 1);
                if (Is(e, "{") || Is(e, "=>") || Is(e, ";")) { DelRawRange(p, e); return e; }
                return p;
            }

            // enum E { A, B = 5, C = "x" } → var E = (function (E) { … return E; })({});
            int Enum(int start, int enumPos)
            {
                string name = S(enumPos + 1);
                int open = enumPos + 2; if (!Is(open, "{")) return enumPos + 1;
                int close = Match(open) - 1;
                var sb = new StringBuilder("var " + name + " = (function (E) {");
                int q = open + 1; string next = "0"; bool numeric = true;
                while (q < close)
                {
                    if (Is(q, ",")) { q++; continue; }
                    string key = S(q); if (Kind(q) == K.Str) key = key.Substring(1, key.Length - 2);
                    q++;
                    string val = null;
                    if (Is(q, "="))
                    {
                        int e = q + 1; var vs = new StringBuilder();
                        while (e < close && !Is(e, ",")) { vs.Append(S(e)).Append(' '); e++; }
                        val = vs.ToString().Trim(); q = e;
                    }
                    if (val == null) val = numeric ? next : "undefined";
                    bool isStr = val.StartsWith("\"") || val.StartsWith("'") || val.StartsWith("`");
                    if (isStr) { sb.Append(" E[\"" + key + "\"] = " + val + ";"); numeric = false; }
                    else { sb.Append(" E[E[\"" + key + "\"] = " + val + "] = \"" + key + "\";"); next = "E[\"" + key + "\"] + 1"; numeric = true; }
                }
                sb.Append(" return E; })({});");
                // первый токен заменяем кодом, остальное удаляем
                int stop = Is(close + 1, ";") ? close + 2 : close + 1;
                DelRawRange(start, stop);
                del[sig[start]] = false; repl[sig[start]] = sb.ToString();
                for (int i = sig[start] + 1; i <= sig[stop - 1]; i++) del[i] = true;
                return stop;
            }
        }
    }
}
