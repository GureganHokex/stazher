// MiniJson — маленький JSON-парсер и сериализатор без зависимостей (ни Unity, ни сторонних библиотек).
//
//   object v = MiniJson.Parse(text);            // бросает FormatException с номером строки/столбца
//   bool ok = MiniJson.TryParse(text, out v, out err);
//   string s = MiniJson.Serialize(v);           // компактно
//   string p = MiniJson.Serialize(v, true);     // с отступами (2 пробела)
//
// Типы после Parse:
//   объект -> Dictionary<string, object> (порядок ключей как в тексте; при повторе ключа побеждает последний)
//   массив -> List<object>
//   число  -> long (целое без точки/экспоненты, влезающее в long), иначе double
//   строка -> string, true/false -> bool, null -> null
// Как и json в Python, понимает и пишет NaN, Infinity, -Infinity (в строгом JSON их нет).
// Serialize понимает: null, string, char, bool, все числовые типы, enum (как строку), IDictionary, IEnumerable.
// Прочие объекты пишутся строкой через ToString().
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Intern.Game
{
    public static class MiniJson
    {
        const int MaxDepth = 512;
        static readonly char[] ExpChars = { 'e', 'E' };

        // ======================= разбор =======================

        public static object Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            var p = new Reader(json);
            p.SkipWs();
            object v = p.ReadValue(0);
            p.SkipWs();
            if (!p.End) throw p.Error("лишние символы после JSON-значения");
            return v;
        }

        public static bool TryParse(string json, out object value, out string error)
        {
            try { value = Parse(json); error = null; return true; }
            catch (FormatException e) { value = null; error = e.Message; return false; }
            catch (ArgumentNullException) { value = null; error = "MiniJson: пустая строка (null)"; return false; }
        }

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text)
            {
                s = text;
                i = s.Length > 0 && s[0] == (char)0xFEFF ? 1 : 0;   // BOM
            }

            public bool End { get { return i >= s.Length; } }

            public FormatException Error(string what)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                {
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                }
                return new FormatException("MiniJson: " + what + " (строка " + line + ", столбец " + col + ")");
            }

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            bool Lit(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                int end = i + word.Length;
                if (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] == '_')) return false;
                i = end;
                return true;
            }

            public object ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("слишком глубокая вложенность");
                if (End) throw Error("неожиданный конец текста");
                char c = s[i];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': if (Lit("true")) return true; break;
                    case 'f': if (Lit("false")) return false; break;
                    case 'n': if (Lit("null")) return null; break;
                    case 'N': if (Lit("NaN")) return double.NaN; break;
                    case 'I': if (Lit("Infinity")) return double.PositiveInfinity; break;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        break;
                }
                throw Error("неожиданный символ '" + c + "'");
            }

            Dictionary<string, object> ReadObject(int depth)
            {
                var d = new Dictionary<string, object>();
                i++; // {
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Error("ожидалось имя ключа в кавычках");
                    string key = ReadString();
                    SkipWs();
                    if (End || s[i] != ':') throw Error("ожидалось ':' после ключа \"" + key + "\"");
                    i++;
                    SkipWs();
                    d[key] = ReadValue(depth + 1);
                    SkipWs();
                    if (End) throw Error("объект не закрыт — нет '}'");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("ожидалось ',' или '}'");
                }
            }

            List<object> ReadArray(int depth)
            {
                var list = new List<object>();
                i++; // [
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    SkipWs();
                    list.Add(ReadValue(depth + 1));
                    SkipWs();
                    if (End) throw Error("массив не закрыт — нет ']'");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw Error("ожидалось ',' или ']'");
                }
            }

            string ReadString()
            {
                i++; // "
                StringBuilder sb = null;
                int start = i;
                while (true)
                {
                    if (i >= s.Length) throw Error("строка не закрыта — нет '\"'");
                    char c = s[i];
                    if (c == '"')
                    {
                        string res = sb == null ? s.Substring(start, i - start) : sb.Append(s, start, i - start).ToString();
                        i++;
                        return res;
                    }
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(s, start, i - start);
                        i++;
                        if (i >= s.Length) throw Error("строка оборвалась после '\\'");
                        char e = s[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                {
                                    if (i + 4 > s.Length) throw Error("неполная последовательность \\u");
                                    int code = 0;
                                    for (int k = 0; k < 4; k++)
                                    {
                                        int h = Hex(s[i + k]);
                                        if (h < 0) throw Error("неверная последовательность \\u");
                                        code = code * 16 + h;
                                    }
                                    i += 4;
                                    sb.Append((char)code);
                                    break;
                                }
                            default:
                                i--;
                                throw Error("неизвестная escape-последовательность \\" + e);
                        }
                        start = i;
                        continue;
                    }
                    if (c < ' ') throw Error("управляющий символ внутри строки (перевод строки пишется как \\n)");
                    i++;
                }
            }

            static int Hex(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            object ReadNumber()
            {
                int start = i;
                if (s[i] == '-')
                {
                    i++;
                    if (Lit("Infinity")) return double.NegativeInfinity;
                }
                if (i >= s.Length || s[i] < '0' || s[i] > '9') throw Error("ожидалась цифра");
                if (s[i] == '0') i++;
                else while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                bool isInt = true;
                if (i < s.Length && s[i] == '.')
                {
                    isInt = false;
                    i++;
                    if (i >= s.Length || s[i] < '0' || s[i] > '9') throw Error("после точки нужна цифра");
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    isInt = false;
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    if (i >= s.Length || s[i] < '0' || s[i] > '9') throw Error("в экспоненте нужна цифра");
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                string num = s.Substring(start, i - start);
                if (isInt)
                {
                    long l;
                    if (long.TryParse(num, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                }
                double d;
                if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                // Mono не разбирает переполнение порядка (1e400, 1e-400), а по IEEE это ±Infinity или ±0
                bool neg = num[0] == '-', zero = true;
                int ePos = num.IndexOfAny(ExpChars);
                for (int k = neg ? 1 : 0; k < (ePos < 0 ? num.Length : ePos); k++)
                    if (num[k] >= '1' && num[k] <= '9') { zero = false; break; }
                if (zero || (ePos >= 0 && ePos + 1 < num.Length && num[ePos + 1] == '-')) return neg ? -0.0 : 0.0;
                return neg ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        // ======================= запись =======================

        public static string Serialize(object value)
        {
            return Serialize(value, false);
        }

        public static string Serialize(object value, bool pretty)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty, 0);
            return sb.ToString();
        }

        /// <summary>Строка в кавычках с экранированием, как в JSON.</summary>
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            WriteString(sb, s);
            return sb.ToString();
        }

        /// <summary>Число в JSON-виде: целые без точки, дробные — кратчайшая точная запись («R»), 3.0 остаётся 3.0.</summary>
        public static string FormatNumber(double d)
        {
            if (double.IsNaN(d)) return "NaN";
            if (double.IsPositiveInfinity(d)) return "Infinity";
            if (double.IsNegativeInfinity(d)) return "-Infinity";
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            double back;   // «R» в старых рантаймах иногда теряет последний знак — тогда G17 (всегда точно)
            if (!double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out back) || back != d)
                r = d.ToString("G17", CultureInfo.InvariantCulture);
            if (r.IndexOf('.') < 0 && r.IndexOf('E') < 0 && r.IndexOf('e') < 0) r += ".0";
            return r;
        }

        static void Newline(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void Write(StringBuilder sb, object v, bool pretty, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("MiniJson: слишком глубокая вложенность (нет ли циклической ссылки?)");
            if (v == null) { sb.Append("null"); return; }
            var str = v as string;
            if (str != null) { WriteString(sb, str); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is char) { WriteString(sb, v.ToString()); return; }
            if (v is double) { sb.Append(FormatNumber((double)v)); return; }
            if (v is float) { sb.Append(FormatNumber((double)(float)v)); return; }
            if (v is decimal) { sb.Append(((decimal)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long || v is int || v is short || v is sbyte || v is ulong || v is uint || v is ushort || v is byte)
            {
                sb.Append(((IFormattable)v).ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }

            var dict = v as IDictionary;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Newline(sb, pretty, depth + 1);
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(pretty ? ": " : ":");
                    Write(sb, e.Value, pretty, depth + 1);
                }
                Newline(sb, pretty, depth);
                sb.Append('}');
                return;
            }

            var seq = v as IEnumerable;
            if (seq != null)
            {
                bool first = true;
                sb.Append('[');
                foreach (var item in seq)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Newline(sb, pretty, depth + 1);
                    Write(sb, item, pretty, depth + 1);
                }
                if (!first) Newline(sb, pretty, depth);
                sb.Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ' || c == (char)0x2028 || c == (char)0x2029)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
