// Спринт 9: один подсветчик для Си-подобных языков — Java, C#, Go, C++, Rust, Kotlin, Swift, PHP.
// Общее: // и /* */ комментарии, строки, числа, вызовы, типы с заглавной; у каждого языка — свои ключевые слова,
// встроенные типы и особые строки (Go `raw`, C# @"…" и $"…", Rust r"…", Kotlin/Swift """…""", PHP $переменные).
// state: "*" — внутри блочного комментария, "`" — внутри raw-строки Go, "\"\"\"" — внутри тройной строки.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intern.Game
{
    public static partial class Syntax
    {
        class CLang { public HashSet<string> ctl, kw, types, lit, builtin; }

        static HashSet<string> H(string words) { return new HashSet<string>(words.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); }

        static readonly Dictionary<string, CLang> CLangs = new Dictionary<string, CLang>
        {
            { "java", new CLang { ctl = H("if else for while do switch case default break continue return try catch finally throw yield"),
                kw = H("class interface enum record extends implements new public private protected static final abstract void import package this super var instanceof synchronized throws transient volatile native default sealed permits non-sealed"),
                types = H("int long short byte char boolean float double String Integer Long Double Boolean Object List Map Set ArrayList HashMap HashSet Optional Stream"),
                lit = H("true false null"), builtin = H("System println printf out") } },
            { "csharp", new CLang { ctl = H("if else for foreach while do switch case default break continue return try catch finally throw yield goto when"),
                kw = H("class interface enum struct record namespace using new public private protected internal static readonly const abstract virtual override sealed void this base var in out ref is as async await get set init partial params operator event delegate where typeof nameof"),
                types = H("int long short byte char bool float double decimal string object dynamic String List Dictionary HashSet Task IEnumerable Console Math"),
                lit = H("true false null"), builtin = H("WriteLine Write ReadLine") } },
            { "go", new CLang { ctl = H("if else for range switch case default break continue return goto fallthrough select defer go"),
                kw = H("package import func var const type struct interface map chan"),
                types = H("int int8 int16 int32 int64 uint uint8 uint16 uint32 uint64 float32 float64 string bool byte rune error any complex64 complex128"),
                lit = H("true false nil iota"), builtin = H("make new len cap append copy delete panic recover print println close fmt Println Printf Sprintf errors strings strconv") } },
            { "cpp", new CLang { ctl = H("if else for while do switch case default break continue return try catch throw goto co_return co_await"),
                kw = H("class struct enum union namespace using template typename public private protected virtual override static const constexpr inline new delete this auto operator friend explicit noexcept typedef extern mutable volatile sizeof"),
                types = H("int long short char bool float double void unsigned signed size_t string vector map set unordered_map std cout cin endl"),
                lit = H("true false nullptr NULL"), builtin = H("printf scanf") } },
            { "rust", new CLang { ctl = H("if else for while loop match break continue return in"),
                kw = H("fn let mut struct enum impl trait pub use mod crate self Self super as const static ref move where type dyn unsafe async await"),
                types = H("i8 i16 i32 i64 i128 isize u8 u16 u32 u64 u128 usize f32 f64 bool char str String Vec Option Result Box HashMap"),
                lit = H("true false None Some Ok Err"), builtin = H("println print format vec panic assert assert_eq") } },
            { "kotlin", new CLang { ctl = H("if else for while do when break continue return try catch finally throw in is"),
                kw = H("fun val var class interface object data sealed enum open abstract override private public protected internal companion import package this super as by init constructor lateinit suspend"),
                types = H("Int Long Short Byte Char Boolean Float Double String Unit Any Nothing List MutableList Map Set Array"),
                lit = H("true false null"), builtin = H("println print listOf mutableListOf mapOf setOf") } },
            { "swift", new CLang { ctl = H("if else for while repeat switch case default break continue return guard defer throw try catch do in where"),
                kw = H("func let var class struct enum protocol extension import init deinit self Self super static public private internal fileprivate open override mutating inout as is throws rethrows async await some any"),
                types = H("Int Double Float String Bool Character Array Dictionary Set Optional Void"),
                lit = H("true false nil"), builtin = H("print") } },
            { "php", new CLang { ctl = H("if else elseif for foreach while do switch case default break continue return try catch finally throw match as"),
                kw = H("function class interface trait enum extends implements new public private protected static abstract final const use namespace echo print fn readonly instanceof"),
                types = H("int float string bool array object mixed void null callable iterable self"),
                lit = H("true false null TRUE FALSE NULL"), builtin = H("count strlen array_map array_filter implode explode isset empty var_dump print_r json_encode") } },
        };

        static string[] CLikeKeywords(string lang)
        {
            CLang L; if (!CLangs.TryGetValue(lang, out L)) return new string[0];
            return L.ctl.Concat(L.kw).Concat(L.types).Concat(L.lit).Concat(L.builtin).Distinct().ToArray();
        }

        static void CLike(string lang, string s, List<Span> r, ref string state)
        {
            CLang L; CLangs.TryGetValue(lang, out L);
            int i = 0, n = s.Length;
            string prev = null;
            // продолжение многострочной конструкции
            if (state == "*") { int e = s.IndexOf("*/", StringComparison.Ordinal); if (e < 0) { Add(r, 0, n, Tok.Comment); return; } Add(r, 0, e + 2, Tok.Comment); i = e + 2; state = null; }
            else if (state == "`") { int e = s.IndexOf('`'); if (e < 0) { Add(r, 0, n, Tok.Str); return; } Add(r, 0, e + 1, Tok.Str); i = e + 1; state = null; }
            else if (state == "\"\"\"") { int e = s.IndexOf("\"\"\"", StringComparison.Ordinal); if (e < 0) { Add(r, 0, n, Tok.Str); return; } Add(r, 0, e + 3, Tok.Str); i = e + 3; state = null; }
            while (i < n)
            {
                char c = s[i];
                if (Sp(c)) { i++; continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '/') { Add(r, i, n, Tok.Comment); return; }
                if (c == '#' && lang == "php") { Add(r, i, n, Tok.Comment); return; }
                if (c == '#' && lang == "cpp") { int j = i + 1; while (j < n && IdStart(s[j])) j++; Add(r, i, j, Tok.Keyword); i = j; if (s.Substring(i).TrimStart().StartsWith("<")) { int a = s.IndexOf('<', i), b = s.IndexOf('>', a + 1); if (b > a) { Add(r, a, b + 1, Tok.Str); i = b + 1; } } continue; }
                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Comment); state = "*"; return; }
                    Add(r, i, e + 2, Tok.Comment); i = e + 2; continue;
                }
                if (c == '<' && i + 1 < n && s[i + 1] == '?' && lang == "php") { int j = s.IndexOf("php", i, StringComparison.Ordinal) == i + 2 ? i + 5 : i + 2; Add(r, i, j, Tok.Keyword); i = j; continue; }
                if (c == '"' && i + 2 < n && s[i + 1] == '"' && s[i + 2] == '"' && (lang == "kotlin" || lang == "swift" || lang == "java"))
                {
                    int e = s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    if (e < 0) { Add(r, i, n, Tok.Str); state = "\"\"\""; return; }
                    Add(r, i, e + 3, Tok.Str); i = e + 3; prev = null; continue;
                }
                if (c == '`' && lang == "go") { int e = s.IndexOf('`', i + 1); if (e < 0) { Add(r, i, n, Tok.Str); state = "`"; return; } Add(r, i, e + 1, Tok.Str); i = e + 1; prev = null; continue; }
                if ((c == '@' || c == '$') && lang == "csharp" && i + 1 < n && (s[i + 1] == '"' || (s[i + 1] == '@' || s[i + 1] == '$') && i + 2 < n && s[i + 2] == '"'))
                { int q = s.IndexOf('"', i); int j = StrEnd(s, q, n, '"', c == '$'); Add(r, i, j, Tok.Str); i = j; prev = null; continue; }
                if (c == 'r' && lang == "rust" && i + 1 < n && (s[i + 1] == '"' || s[i + 1] == '#')) { int q = s.IndexOf('"', i); if (q > i && q <= i + 3) { int e = s.IndexOf('"', q + 1); int j = e < 0 ? n : e + 1; while (j < n && s[j] == '#') j++; Add(r, i, j, Tok.Str); i = j; prev = null; continue; } }
                if (c == '"') { int j = StrEnd(s, i, n, '"', true); Add(r, i, j, Tok.Str); i = j; prev = null; continue; }
                if (c == '\'')
                {
                    // символ 'a' / '\n' (в Rust ещё и времена жизни 'a — их не красим строкой)
                    if (lang == "rust" && i + 2 < n && IdStart(s[i + 1]) && s[i + 2] != '\'') { int j = i + 1; while (j < n && Id(s[j])) j++; Add(r, i, j, Tok.Keyword); i = j; continue; }
                    int k = StrEnd(s, i, n, '\'', true); Add(r, i, k, Tok.Str); i = k; prev = null; continue;
                }
                if (c == '$' && lang == "php" && i + 1 < n && IdStart(s[i + 1])) { int j = i + 1; while (j < n && Id(s[j])) j++; Add(r, i, j, s.Substring(i, j - i) == "$this" ? Tok.Self : Tok.Param); i = j; prev = null; continue; }
                if (c == '@' && (lang == "java" || lang == "kotlin" || lang == "swift" || lang == "csharp") && i + 1 < n && IdStart(s[i + 1])) { int j = i + 1; while (j < n && (Id(s[j]) || s[j] == '.')) j++; Add(r, i, j, Tok.Func); i = j; continue; }
                if (Dig(c) || (c == '.' && i + 1 < n && Dig(s[i + 1]))) { int j = NumEnd(s, i, n); Add(r, i, j, Tok.Num); i = j; prev = null; continue; }
                if (IdStart(c))
                {
                    int j = i; while (j < n && Id(s[j])) j++;
                    string w = s.Substring(i, j - i);
                    int k = SkipSp(s, j, n); char nx = k < n ? s[k] : '\0';
                    bool call = nx == '(' || (nx == '!' && lang == "rust" && k + 1 < n && (s[k + 1] == '(' || s[k + 1] == '['));
                    Tok t;
                    if (L == null) t = Tok.Text;
                    else if (prev == "class" || prev == "struct" || prev == "interface" || prev == "enum" || prev == "trait" || prev == "record" || prev == "protocol" || prev == "object" || (prev == "type" && lang == "go") || prev == "impl" || prev == "extends" || prev == "implements" || prev == "new") t = Tok.Class;
                    else if (prev == "func" || prev == "fun" || prev == "fn" || prev == "function" || prev == "def") t = Tok.Def;
                    else if (L.ctl.Contains(w)) t = Tok.Control;
                    else if (L.lit.Contains(w)) t = Tok.Keyword;
                    else if (L.kw.Contains(w)) t = w == "this" || w == "self" || w == "base" || w == "super" ? Tok.Self : Tok.Keyword;
                    else if (call && L.builtin.Contains(w)) t = Tok.Builtin;
                    else if (call) t = Tok.Func;
                    else if (L.types.Contains(w)) t = Tok.Class;
                    else if (L.builtin.Contains(w)) t = Tok.Builtin;
                    else if (char.IsUpper(w[0]) && w.Length > 1 && w.Any(char.IsLower)) t = Tok.Class;
                    else t = Tok.Text;
                    if (call && lang == "rust" && nx == '!') { Add(r, i, k + 1, Tok.Builtin); i = k + 1; prev = w; continue; }
                    Add(r, i, j, t); prev = w; i = j; continue;
                }
                int o = One(s, i, n);
                Add(r, i, o, "+-*/%=<>!&|^~?:".IndexOf(c) >= 0 ? Tok.Op : Tok.Text);
                if (c != '.' && c != ':' ) prev = c == '.' ? prev : null;
                i = o;
            }
        }
    }
}
