// Игровой Python: значения, ошибки, лексер.
// Этот файл НЕ зависит от Unity — его можно тестировать отдельно.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Intern.Py
{
    // ---------- Ошибки ----------
    public class PyError : Exception
    {
        public string PyType;   // "NameError", "TypeError"...
        public string Ru;       // объяснение на русском
        public int Line;
        public PyError(string type, string ru, int line = 0) : base(type + ": " + ru)
        { PyType = type; Ru = ru; Line = line; }
    }

    public class StopExecution : Exception { }

    // ---------- Значения ----------
    // int -> long, float -> double, str -> string, bool -> bool, None -> null
    public class PyList { public List<object> Items = new List<object>(); public PyList() { } public PyList(IEnumerable<object> e) { Items.AddRange(e); } }
    public class PyTuple { public object[] Items; public PyTuple(object[] i) { Items = i; } }
    public class PyRange { public long Start, Stop, Step; }
    public class PyTypeObj { public string Name; public PyTypeObj(string n) { Name = n; } }

    public class PyDict
    {
        public List<object> Keys = new List<object>();
        public Dictionary<object, object> Map = new Dictionary<object, object>(new PyKeyComparer());
        public void Set(object k, object v) { if (!Map.ContainsKey(k)) Keys.Add(k); Map[k] = v; }
        public bool Remove(object k)
        {
            if (!Map.ContainsKey(k)) return false;
            Map.Remove(k);
            var cmp = new PyKeyComparer();
            for (int i = 0; i < Keys.Count; i++) if (cmp.Equals(Keys[i], k)) { Keys.RemoveAt(i); break; }
            return true;
        }
    }

    public class PyKeyComparer : IEqualityComparer<object>
    {
        public new bool Equals(object a, object b) { return PyOps.Eq(a, b); }
        public int GetHashCode(object o)
        {
            if (o == null) return 0;
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is long) return ((double)(long)o).GetHashCode();
            if (o is double) return ((double)o).GetHashCode();
            if (o is PyTuple) { int h = 17; foreach (var x in ((PyTuple)o).Items) h = h * 31 + GetHashCode(x); return h; }
            if (o is PyList || o is PyDict) throw new PyError("TypeError", "Список или словарь нельзя использовать как ключ словаря.");
            return o.GetHashCode();
        }
    }

    public delegate object BuiltinFn(List<object> args, Dictionary<string, object> kw, int line);

    public class PyBuiltin { public string Name; public BuiltinFn Fn; public PyBuiltin(string n, BuiltinFn f) { Name = n; Fn = f; } }
    public class PyFunction
    {
        public string Name; public List<string> Params; public List<object> Defaults; // значения по умолчанию (уже вычисленные) для последних параметров
        public List<Stmt> Body; public int Line;
    }
    public class PyBoundMethod { public object Self; public string Name; }

    public static class PyOps
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string TypeName(object o)
        {
            if (o == null) return "NoneType";
            if (o is bool) return "bool";
            if (o is long) return "int";
            if (o is double) return "float";
            if (o is string) return "str";
            if (o is PyList) return "list";
            if (o is PyDict) return "dict";
            if (o is PyTuple) return "tuple";
            if (o is PyRange) return "range";
            if (o is PyFunction || o is PyBuiltin || o is PyBoundMethod) return "function";
            if (o is PyTypeObj) return "type";
            return "object";
        }

        public static string TypeNameRu(object o)
        {
            switch (TypeName(o))
            {
                case "int": return "целое число (int)";
                case "float": return "дробное число (float)";
                case "str": return "строка (str)";
                case "bool": return "логическое значение (bool)";
                case "list": return "список (list)";
                case "dict": return "словарь (dict)";
                case "tuple": return "кортеж (tuple)";
                case "NoneType": return "None (пустое значение)";
                case "function": return "функция";
                default: return TypeName(o);
            }
        }

        public static bool Truthy(object o)
        {
            if (o == null) return false;
            if (o is bool) return (bool)o;
            if (o is long) return (long)o != 0;
            if (o is double) return (double)o != 0.0;
            if (o is string) return ((string)o).Length > 0;
            if (o is PyList) return ((PyList)o).Items.Count > 0;
            if (o is PyDict) return ((PyDict)o).Keys.Count > 0;
            if (o is PyTuple) return ((PyTuple)o).Items.Length > 0;
            if (o is PyRange) return RangeLen((PyRange)o) > 0;
            return true;
        }

        public static bool IsNum(object o) { return o is long || o is double || o is bool; }
        public static bool IsInt(object o) { return o is long || o is bool; }
        public static long ToLong(object o) { if (o is bool) return ((bool)o) ? 1 : 0; return (long)o; }
        public static double ToDouble(object o)
        {
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is long) return (long)o;
            return (double)o;
        }

        public static long RangeLen(PyRange r)
        {
            if (r.Step > 0) return r.Stop > r.Start ? (r.Stop - r.Start + r.Step - 1) / r.Step : 0;
            return r.Start > r.Stop ? (r.Start - r.Stop - r.Step - 1) / (-r.Step) : 0;
        }

        public static string FloatStr(double d)
        {
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            if (double.IsNaN(d)) return "nan";
            if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return ((long)d).ToString(Inv) + ".0";
            string s = d.ToString("R", Inv);
            if (s.Contains("E")) s = s.Replace("E", "e").Replace("e+", "e+").Replace("e-0", "e-").Replace("e+0", "e+");
            return s;
        }

        public static string Str(object o)
        {
            if (o is string) return (string)o;
            return Repr(o);
        }

        public static string Repr(object o)
        {
            if (o == null) return "None";
            if (o is bool) return ((bool)o) ? "True" : "False";
            if (o is long) return ((long)o).ToString(Inv);
            if (o is double) return FloatStr((double)o);
            if (o is string)
            {
                var s = (string)o;
                if (s.Contains("'") && !s.Contains("\"")) return "\"" + s.Replace("\\", "\\\\").Replace("\n", "\\n") + "\"";
                return "'" + s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\t", "\\t") + "'";
            }
            if (o is PyList)
            {
                var sb = new StringBuilder("[");
                var l = ((PyList)o).Items;
                for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(l[i] == o ? "[...]" : Repr(l[i])); }
                return sb.Append("]").ToString();
            }
            if (o is PyTuple)
            {
                var t = ((PyTuple)o).Items;
                if (t.Length == 1) return "(" + Repr(t[0]) + ",)";
                var parts = new string[t.Length];
                for (int i = 0; i < t.Length; i++) parts[i] = Repr(t[i]);
                return "(" + string.Join(", ", parts) + ")";
            }
            if (o is PyDict)
            {
                var d = (PyDict)o; var sb = new StringBuilder("{");
                for (int i = 0; i < d.Keys.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Repr(d.Keys[i])).Append(": ").Append(Repr(d.Map[d.Keys[i]])); }
                return sb.Append("}").ToString();
            }
            if (o is PyRange)
            {
                var r = (PyRange)o;
                return r.Step == 1 ? "range(" + r.Start + ", " + r.Stop + ")" : "range(" + r.Start + ", " + r.Stop + ", " + r.Step + ")";
            }
            if (o is PyFunction) return "<function " + ((PyFunction)o).Name + ">";
            if (o is PyBuiltin) return "<built-in function " + ((PyBuiltin)o).Name + ">";
            if (o is PyBoundMethod) return "<method " + ((PyBoundMethod)o).Name + ">";
            if (o is PyTypeObj) return "<class '" + ((PyTypeObj)o).Name + "'>";
            return o.ToString();
        }

        public static bool Eq(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (IsNum(a) && IsNum(b))
            {
                if (IsInt(a) && IsInt(b)) return ToLong(a) == ToLong(b);
                return ToDouble(a) == ToDouble(b);
            }
            if (a is string && b is string) return (string)a == (string)b;
            if (a is PyList && b is PyList)
            {
                var x = ((PyList)a).Items; var y = ((PyList)b).Items;
                if (x.Count != y.Count) return false;
                for (int i = 0; i < x.Count; i++) if (!Eq(x[i], y[i])) return false;
                return true;
            }
            if (a is PyTuple && b is PyTuple)
            {
                var x = ((PyTuple)a).Items; var y = ((PyTuple)b).Items;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++) if (!Eq(x[i], y[i])) return false;
                return true;
            }
            if (a is PyDict && b is PyDict)
            {
                var x = (PyDict)a; var y = (PyDict)b;
                if (x.Keys.Count != y.Keys.Count) return false;
                foreach (var k in x.Keys) { object v; if (!y.Map.TryGetValue(k, out v) || !Eq(x.Map[k], v)) return false; }
                return true;
            }
            return ReferenceEquals(a, b);
        }

        public static int Compare(object a, object b, int line)
        {
            if (IsNum(a) && IsNum(b))
            {
                if (IsInt(a) && IsInt(b)) return ToLong(a).CompareTo(ToLong(b));
                return ToDouble(a).CompareTo(ToDouble(b));
            }
            if (a is string && b is string) return string.CompareOrdinal((string)a, (string)b);
            if ((a is PyList && b is PyList) || (a is PyTuple && b is PyTuple))
            {
                var x = a is PyList ? ((PyList)a).Items.ToArray() : ((PyTuple)a).Items;
                var y = b is PyList ? ((PyList)b).Items.ToArray() : ((PyTuple)b).Items;
                for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
                {
                    if (!Eq(x[i], y[i])) return Compare(x[i], y[i], line);
                }
                return x.Length.CompareTo(y.Length);
            }
            throw new PyError("TypeError",
                "Нельзя сравнивать " + TypeNameRu(a) + " и " + TypeNameRu(b) + " через < или >. " +
                (a is string || b is string ? "Похоже, одно из значений — строка. Число из input() нужно превратить через int(...)." : ""), line);
        }
    }

    // ---------- Лексер ----------
    public enum T { Name, Num, Str, FStr, Op, Newline, Indent, Dedent, EOF }

    public class Token
    {
        public T Type; public string Text; public object Val; public int Line;
        public override string ToString() { return Type + ":" + Text; }
    }

    public static class Lexer
    {
        static readonly string[] Ops3 = { "**=", "//=" };
        static readonly string[] Ops2 = { "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "**", "//", "->" };
        const string Ops1 = "+-*/%<>=()[]{}:,.";

        public static List<Token> Lex(string src)
        {
            var toks = new List<Token>();
            var indents = new Stack<int>(); indents.Push(0);
            src = (src ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\t", "    ");
            int i = 0, line = 1, depth = 0; bool lineStart = true;
            var brackets = new Stack<KeyValuePair<char, int>>();

            while (i < src.Length)
            {
                if (lineStart && depth == 0)
                {
                    int col = 0;
                    while (i < src.Length && src[i] == ' ') { col++; i++; }
                    if (i >= src.Length) break;
                    if (src[i] == '\n') { i++; line++; continue; }
                    if (src[i] == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
                    if (col > indents.Peek()) { indents.Push(col); toks.Add(new Token { Type = T.Indent, Line = line }); }
                    else
                    {
                        while (col < indents.Peek()) { indents.Pop(); toks.Add(new Token { Type = T.Dedent, Line = line }); }
                        if (col != indents.Peek())
                            throw new PyError("IndentationError", "Отступ в этой строке не совпадает ни с одним уровнем выше. Обычно отступ — ровно 4 пробела на каждый уровень.", line);
                    }
                    lineStart = false;
                }
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                if (c == '\n')
                {
                    if (depth == 0) { toks.Add(new Token { Type = T.Newline, Line = line }); lineStart = true; }
                    i++; line++; continue;
                }
                if (c == ' ') { i++; continue; }
                if (c == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
                if (c == '\\' && n == '\n') { i += 2; line++; continue; }

                if (char.IsDigit(c) || (c == '.' && char.IsDigit(n)))
                {
                    int s = i; bool isFloat = false;
                    while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '_')) i++;
                    if (i < src.Length && src[i] == '.' && (i + 1 >= src.Length || src[i + 1] != '.')) { isFloat = true; i++; while (i < src.Length && char.IsDigit(src[i])) i++; }
                    if (i < src.Length && (src[i] == 'e' || src[i] == 'E'))
                    {
                        int save = i; i++;
                        if (i < src.Length && (src[i] == '+' || src[i] == '-')) i++;
                        if (i < src.Length && char.IsDigit(src[i])) { isFloat = true; while (i < src.Length && char.IsDigit(src[i])) i++; }
                        else i = save;
                    }
                    string txt = src.Substring(s, i - s).Replace("_", "");
                    object v;
                    if (isFloat) v = double.Parse(txt, CultureInfo.InvariantCulture);
                    else
                    {
                        long lv;
                        if (!long.TryParse(txt, NumberStyles.None, CultureInfo.InvariantCulture, out lv))
                            throw new PyError("OverflowError", "Слишком большое число для игрового Python.", line);
                        v = lv;
                    }
                    toks.Add(new Token { Type = T.Num, Text = txt, Val = v, Line = line });
                    if (i < src.Length && (char.IsLetter(src[i]) || src[i] == '_'))
                        throw new PyError("SyntaxError", "Имя переменной не может начинаться с цифры: «" + txt + src[i] + "...».", line);
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    string name = src.Substring(s, i - s);
                    if ((name == "f" || name == "F" || name == "r" || name == "R" || name == "rf" || name == "fr") && i < src.Length && (src[i] == '"' || src[i] == '\''))
                    {
                        bool isF = name.ToLower().Contains("f"), isRaw = name.ToLower().Contains("r");
                        int startLine = line;
                        string str = ReadString(src, ref i, ref line, isRaw);
                        toks.Add(new Token { Type = isF ? T.FStr : T.Str, Text = str, Val = str, Line = startLine });
                        continue;
                    }
                    toks.Add(new Token { Type = T.Name, Text = name, Line = line });
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    int startLine = line;
                    string str = ReadString(src, ref i, ref line, false);
                    toks.Add(new Token { Type = T.Str, Text = str, Val = str, Line = startLine });
                    continue;
                }

                string op = null;
                foreach (var o in Ops3) if (string.CompareOrdinal(src, i, o, 0, 3) == 0) { op = o; break; }
                if (op == null) foreach (var o in Ops2) if (string.CompareOrdinal(src, i, o, 0, 2) == 0) { op = o; break; }
                if (op == null && Ops1.IndexOf(c) >= 0) op = c.ToString();
                if (op != null)
                {
                    if (op == "(" || op == "[" || op == "{") { depth++; brackets.Push(new KeyValuePair<char, int>(c, line)); }
                    if (op == ")" || op == "]" || op == "}")
                    {
                        if (depth == 0) throw new PyError("SyntaxError", "Лишняя закрывающая скобка «" + op + "».", line);
                        var open = brackets.Pop(); depth--;
                        char expect = open.Key == '(' ? ')' : open.Key == '[' ? ']' : '}';
                        if (op[0] != expect) throw new PyError("SyntaxError", "Скобка «" + open.Key + "» из строки " + open.Value + " закрыта не той скобкой «" + op + "».", line);
                    }
                    toks.Add(new Token { Type = T.Op, Text = op, Line = line });
                    i += op.Length; continue;
                }
                if (c == '“' || c == '”' || c == '«' || c == '»')
                    throw new PyError("SyntaxError", "Используй обычные кавычки \" или ', а не типографские «» или “”.", line);
                if (c == '!' ) throw new PyError("SyntaxError", "В Python «не равно» пишется как !=, а «не» — словом not.", line);
                if (c == '&' || c == '|') throw new PyError("SyntaxError", "В Python логические «и»/«или» пишутся словами and / or, а не && или ||.", line);
                throw new PyError("SyntaxError", "Непонятный символ «" + c + "».", line);
            }
            if (depth > 0)
            {
                var open = brackets.Peek();
                throw new PyError("SyntaxError", "Скобка «" + open.Key + "» в строке " + open.Value + " не закрыта.", open.Value);
            }
            if (toks.Count > 0 && toks[toks.Count - 1].Type != T.Newline && toks[toks.Count - 1].Type != T.Dedent)
                toks.Add(new Token { Type = T.Newline, Line = line });
            while (indents.Count > 1) { indents.Pop(); toks.Add(new Token { Type = T.Dedent, Line = line }); }
            toks.Add(new Token { Type = T.EOF, Line = line });
            return toks;
        }

        static string ReadString(string src, ref int i, ref int line, bool raw)
        {
            char q = src[i];
            bool triple = i + 2 < src.Length && src[i + 1] == q && src[i + 2] == q;
            int startLine = line;
            i += triple ? 3 : 1;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= src.Length) throw new PyError("SyntaxError", "Строка не закрыта: не хватает закрывающей кавычки " + q + ".", startLine);
                char c = src[i];
                if (triple)
                {
                    if (c == q && i + 2 < src.Length && src[i + 1] == q && src[i + 2] == q) { i += 3; break; }
                }
                else
                {
                    if (c == q) { i++; break; }
                    if (c == '\n') throw new PyError("SyntaxError", "Строка не закрыта: не хватает закрывающей кавычки " + q + " в конце.", startLine);
                }
                if (c == '\n') line++;
                if (c == '\\' && !raw && i + 1 < src.Length)
                {
                    char e = src[i + 1]; i += 2;
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '\n': line++; break;
                        default: sb.Append('\\').Append(e); break;
                    }
                    continue;
                }
                if (c == '\\' && raw && i + 1 < src.Length && (src[i + 1] == q || src[i + 1] == '\\'))
                { sb.Append(c).Append(src[i + 1]); i += 2; continue; }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }
    }
}
