// Подсветка Python для редактора, подсказки при наведении и автодополнение.
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Intern.Game
{
    public enum Tok { Text, Control, Keyword, Builtin, Func, Def, Class, Str, Num, Comment, Op, Self, Param }

    public struct Span { public int Start, Len; public Tok Kind; public Span(int s, int l, Tok k) { Start = s; Len = l; Kind = k; } }

    public class Completion { public string Label, Insert, Detail, Doc; public char Icon; public Color IconColor; public int CaretBack; }

    public static class PySyntax
    {
        static readonly HashSet<string> Control = new HashSet<string> { "if", "elif", "else", "while", "for", "in", "return", "break", "continue", "pass",
            "try", "except", "finally", "raise", "import", "from", "as", "with", "yield", "del", "global", "nonlocal", "assert", "lambda" };
        static readonly HashSet<string> Keywords = new HashSet<string> { "def", "class", "and", "or", "not", "is", "None", "True", "False" };

        // Документация на русском: встроенные функции и ключевые слова
        public static readonly Dictionary<string, string[]> Docs = new Dictionary<string, string[]>
        {
            { "print", new[] { "print(*значения, sep=' ', end='\\n')", "Выводит значения в консоль через пробел и переходит на новую строку." } },
            { "input", new[] { "input(подсказка='') -> str", "Ждёт, пока пользователь введёт строку, и возвращает её. Всегда строку — число нужно превратить через int()." } },
            { "int", new[] { "int(x) -> int", "Превращает x в целое число: int('42') → 42, int(3.9) → 3." } },
            { "float", new[] { "float(x) -> float", "Превращает x в дробное число: float('2.5') → 2.5." } },
            { "str", new[] { "str(x) -> str", "Превращает x в строку: str(42) → '42'." } },
            { "bool", new[] { "bool(x) -> bool", "Истинно ли значение: пустые строки, 0 и пустые списки — False." } },
            { "len", new[] { "len(x) -> int", "Длина строки, списка или словаря: len('кот') → 3." } },
            { "range", new[] { "range(стоп) / range(старт, стоп, шаг)", "Последовательность чисел для цикла for. Стоп не входит: range(3) → 0, 1, 2." } },
            { "list", new[] { "list(x) -> list", "Создаёт список. list('abc') → ['a', 'b', 'c']." } },
            { "dict", new[] { "dict() -> dict", "Создаёт словарь: пары «ключ: значение»." } },
            { "set", new[] { "set(x) -> set", "Множество: хранит только уникальные значения." } },
            { "tuple", new[] { "tuple(x) -> tuple", "Кортеж — список, который нельзя менять." } },
            { "sum", new[] { "sum(числа) -> число", "Сумма всех чисел: sum([1, 2, 3]) → 6." } },
            { "min", new[] { "min(a, b, ...) / min(список)", "Самое маленькое значение." } },
            { "max", new[] { "max(a, b, ...) / max(список)", "Самое большое значение." } },
            { "sorted", new[] { "sorted(список, reverse=False) -> list", "Новый отсортированный список, исходный не меняется." } },
            { "abs", new[] { "abs(x)", "Модуль числа: abs(-5) → 5." } },
            { "round", new[] { "round(x, знаков=0)", "Округление: round(2.567, 1) → 2.6." } },
            { "enumerate", new[] { "enumerate(список, start=0)", "Даёт пары (номер, элемент) для цикла for." } },
            { "zip", new[] { "zip(a, b)", "Идёт по двум спискам сразу, парами." } },
            { "type", new[] { "type(x)", "Тип значения: type(5) → int." } },
            { "reversed", new[] { "reversed(список)", "Элементы в обратном порядке." } },
            { "any", new[] { "any(список) -> bool", "True, если хотя бы один элемент истинный." } },
            { "all", new[] { "all(список) -> bool", "True, если все элементы истинные." } },
            { "isinstance", new[] { "isinstance(x, тип) -> bool", "Проверяет, что x — значение этого типа." } },
            { "chr", new[] { "chr(код) -> str", "Символ по его коду: chr(65) → 'A'." } },
            { "ord", new[] { "ord(символ) -> int", "Код символа: ord('A') → 65." } },
            { "map", new[] { "map(функция, список)", "Применяет функцию к каждому элементу." } },
            { "filter", new[] { "filter(функция, список)", "Оставляет элементы, для которых функция вернула True." } },
            { "if", new[] { "if условие:", "Выполняет блок, только если условие истинно." } },
            { "elif", new[] { "elif условие:", "«Иначе если»: проверяется, когда предыдущие условия ложны." } },
            { "else", new[] { "else:", "Выполняется, когда ни одно условие выше не сработало." } },
            { "for", new[] { "for x in последовательность:", "Повторяет блок для каждого элемента." } },
            { "while", new[] { "while условие:", "Повторяет блок, пока условие истинно. Не забудь, что условие должно когда-то стать ложным." } },
            { "def", new[] { "def имя(параметры):", "Объявляет функцию — именованный кусок кода, который можно вызывать." } },
            { "return", new[] { "return значение", "Завершает функцию и отдаёт значение туда, откуда её вызвали." } },
            { "break", new[] { "break", "Немедленно выходит из цикла." } },
            { "continue", new[] { "continue", "Переходит к следующему шагу цикла." } },
            { "pass", new[] { "pass", "Ничего не делает — заглушка там, где нужен блок." } },
            { "in", new[] { "x in коллекция", "Проверяет, есть ли x в строке, списке или словаре." } },
            { "and", new[] { "a and b", "Истинно, только если истинны оба." } },
            { "or", new[] { "a or b", "Истинно, если истинно хотя бы одно." } },
            { "not", new[] { "not x", "Меняет True на False и наоборот." } },
            { "True", new[] { "True", "Логическое «истина»." } },
            { "False", new[] { "False", "Логическое «ложь»." } },
            { "None", new[] { "None", "«Ничего»: так функция возвращает результат, если в ней нет return." } },
            { "is", new[] { "a is b", "Проверяет, что это один и тот же объект (чаще всего: x is None)." } },
        };

        public static bool IsBuiltin(string w) { return Docs.ContainsKey(w) && !Control.Contains(w) && !Keywords.Contains(w); }

        // Разбор одной строки. inStr — открытая тройная кавычка с прошлых строк.
        public static List<Span> Line(string s, ref string inStr)
        {
            var res = new List<Span>();
            int i = 0, n = s.Length;
            string prevWord = null;
            if (inStr != null)
            {
                int end = s.IndexOf(inStr, System.StringComparison.Ordinal);
                if (end < 0) { if (n > 0) res.Add(new Span(0, n, Tok.Str)); return res; }
                res.Add(new Span(0, end + 3, Tok.Str)); i = end + 3; inStr = null;
            }
            while (i < n)
            {
                char c = s[i];
                if (c == ' ') { i++; continue; }
                if (c == '#') { res.Add(new Span(i, n - i, Tok.Comment)); break; }
                // строки (с префиксами f, r, b)
                int q = i; while (q < n && q - i < 2 && "fFrRbBuU".IndexOf(s[q]) >= 0) q++;
                if (q < n && (s[q] == '"' || s[q] == '\''))
                {
                    char qc = s[q];
                    bool triple = q + 2 < n && s[q + 1] == qc && s[q + 2] == qc;
                    if (triple)
                    {
                        string tq = new string(qc, 3);
                        int end = s.IndexOf(tq, q + 3, System.StringComparison.Ordinal);
                        if (end < 0) { res.Add(new Span(i, n - i, Tok.Str)); inStr = tq; return res; }
                        res.Add(new Span(i, end + 3 - i, Tok.Str)); i = end + 3; continue;
                    }
                    int j = q + 1;
                    while (j < n && s[j] != qc) { if (s[j] == '\\') j++; j++; }
                    j = Mathf.Min(n, j + 1);
                    res.Add(new Span(i, j - i, Tok.Str)); i = j; prevWord = null; continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(s[i + 1])))
                {
                    int j = i; while (j < n && (char.IsLetterOrDigit(s[j]) || s[j] == '.' || s[j] == '_')) j++;
                    res.Add(new Span(i, j - i, Tok.Num)); i = j; continue;
                }
                if (IsIdStart(c))
                {
                    int j = i; while (j < n && IsId(s[j])) j++;
                    string w = s.Substring(i, j - i);
                    int k = j; while (k < n && s[k] == ' ') k++;
                    bool call = k < n && s[k] == '(';
                    Tok t;
                    if (prevWord == "def") t = Tok.Def;
                    else if (prevWord == "class") t = Tok.Class;
                    else if (Control.Contains(w)) t = Tok.Control;
                    else if (Keywords.Contains(w)) t = Tok.Keyword;
                    else if (w == "self") t = Tok.Self;
                    else if (call) t = IsBuiltin(w) ? Tok.Builtin : Tok.Func;
                    else if (IsBuiltin(w)) t = Tok.Builtin;
                    else t = Tok.Text;
                    res.Add(new Span(i, j - i, t)); prevWord = w; i = j; continue;
                }
                int o = i; while (o < n && "+-*/%=<>!&|^~@:".IndexOf(s[o]) >= 0) o++;
                if (o > i) { res.Add(new Span(i, o - i, Tok.Op)); i = o; prevWord = null; continue; }
                res.Add(new Span(i, 1, Tok.Text)); i++; prevWord = null;
            }
            return res;
        }

        public static bool IsIdStart(char c) { return char.IsLetter(c) || c == '_'; }
        public static bool IsId(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        public static Color ColorOf(Tok t)
        {
            switch (t)
            {
                case Tok.Control: return Pal.Hex("C586C0");
                case Tok.Keyword: return Pal.Hex("569CD6");
                case Tok.Builtin: return Pal.Hex("DCDCAA");
                case Tok.Func: return Pal.Hex("DCDCAA");
                case Tok.Def: return Pal.Hex("DCDCAA");
                case Tok.Class: return Pal.Hex("4EC9B0");
                case Tok.Str: return Pal.Hex("CE9178");
                case Tok.Num: return Pal.Hex("B5CEA8");
                case Tok.Comment: return Pal.Hex("6A9955");
                case Tok.Op: return Pal.Hex("D4D4D4");
                case Tok.Self: return Pal.Hex("569CD6");
                default: return Pal.Hex("9CDCFE");
            }
        }

        // Строка в rich text для Label (пробелы сохраняются: у метки WhiteSpace.Pre)
        public static string Rich(string s, List<Span> spans)
        {
            var sb = new StringBuilder(s.Length * 2);
            int pos = 0;
            foreach (var sp in spans)
            {
                if (sp.Start > pos) sb.Append(K.Esc(s.Substring(pos, sp.Start - pos)));
                string part = s.Substring(sp.Start, sp.Len);
                if (sp.Kind == Tok.Op || (sp.Kind == Tok.Text && !IsIdStart(part[0])))
                    sb.Append("<color=#D4D4D4>").Append(K.Esc(part)).Append("</color>");
                else
                    sb.Append("<color=").Append(K.Hex(ColorOf(sp.Kind))).Append('>').Append(K.Esc(part)).Append("</color>");
                pos = sp.Start + sp.Len;
            }
            if (pos < s.Length) sb.Append(K.Esc(s.Substring(pos)));
            return sb.ToString();
        }

        // --------- автодополнение ---------
        static readonly string[][] Snippets =
        {
            new[] { "for", "for i in range():", "цикл по числам", "13" },
            new[] { "for", "for x in :", "цикл по коллекции", "1" },
            new[] { "if", "if :", "условие", "1" },
            new[] { "elif", "elif :", "ещё условие", "1" },
            new[] { "else", "else:", "иначе", "0" },
            new[] { "while", "while :", "цикл с условием", "1" },
            new[] { "def", "def ():", "новая функция", "3" },
            new[] { "return", "return ", "вернуть значение", "0" },
        };

        public static List<Completion> Complete(string prefix, string code)
        {
            var res = new List<Completion>();
            if (string.IsNullOrEmpty(prefix)) return res;
            var seen = new HashSet<string>();
            System.Func<string, bool> Match = w => w.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) && w != prefix;
            foreach (var sn in Snippets)
                if (Match(sn[0]) || (sn[0] == prefix && sn[1].Length > prefix.Length + 1))
                    res.Add(new Completion { Label = sn[1].Replace("  ", " "), Insert = sn[1], Detail = sn[2], Icon = '▭', IconColor = Pal.Hex("C586C0"), CaretBack = int.Parse(sn[3]), Doc = Docs.ContainsKey(sn[0]) ? Docs[sn[0]][1] : null });
            // слова из кода: переменные и свои функции
            foreach (Match m in Regex.Matches(code ?? "", @"\b[A-Za-z_][A-Za-z0-9_]*\b"))
            {
                string w = m.Value;
                if (w.Length < 2 || Control.Contains(w) || Keywords.Contains(w) || Docs.ContainsKey(w) || !Match(w) || !seen.Add(w)) continue;
                bool isFunc = Regex.IsMatch(code, @"\bdef\s+" + w + @"\b");
                res.Add(new Completion { Label = w, Insert = isFunc ? w + "()" : w, CaretBack = isFunc ? 1 : 0, Detail = isFunc ? "функция" : "переменная", Icon = isFunc ? 'ƒ' : 'x', IconColor = isFunc ? Pal.Hex("B180D7") : Pal.Hex("75BEFF") });
            }
            foreach (var kv in Docs)
            {
                if (!Match(kv.Key) || !seen.Add(kv.Key)) continue;
                bool fn = IsBuiltin(kv.Key);
                res.Add(new Completion { Label = kv.Key, Insert = fn ? kv.Key + "()" : kv.Key, CaretBack = fn ? 1 : 0, Detail = fn ? kv.Value[0] : "ключевое слово", Doc = kv.Value[1], Icon = fn ? 'ƒ' : 'k', IconColor = fn ? Pal.Hex("B180D7") : Pal.Hex("C586C0") });
            }
            res.Sort((a, b) =>
            {
                int pa = a.Label.StartsWith(prefix, System.StringComparison.Ordinal) ? 0 : 1, pb = b.Label.StartsWith(prefix, System.StringComparison.Ordinal) ? 0 : 1;
                return pa != pb ? pa - pb : a.Label.Length - b.Label.Length;
            });
            if (res.Count > 9) res.RemoveRange(9, res.Count - 9);
            return res;
        }
    }
}
