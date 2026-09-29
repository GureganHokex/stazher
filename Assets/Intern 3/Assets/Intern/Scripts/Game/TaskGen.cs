// Спринт 5 «Без потолка»: генератор задач из эталонных решений.
// «Почини баг» — в эталон вносится одна ошибка (мутация): на единицу, < и <=, забытый return, перепутанные операнды,
// арифметика и логика. Мутант годится, только если он разбирается, не зависает и валит хотя бы один тест,
// а эталон все тесты проходит. «Что вернёт код?» — эталон и вызов из теста; неверные варианты — то, что вернули мутанты.
// Задача задаётся строкой gen:<fix|ask>:<id исходной задачи>:<seed> и собирается заново из неё же (детерминированно):
// в сохранении лежат только эти строки.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Intern.Py;

namespace Intern.Game
{
    public class GenMutant
    {
        public int index;                 // номер кандидата у исходной задачи (для кэша)
        public string code, kind, what, before, after;
        public int line;
        public List<CheckResult> res;     // результаты тестов мутанта
    }

    public static class TaskGen
    {
        public const string Prefix = "gen:";
        public const int PracticeBase = 1000000;      // seed тренировочных задач: PracticeBase + номер

        // ======================= что можно генерировать =======================
        public static bool CanGenerate(TaskData t)
        {
            if (t == null || t.IsChoice || t.generated || string.IsNullOrEmpty(t.solution) || t.testCases == null || t.testCases.Count == 0) return false;
            if (t.type != "write_code" && t.type != "find_bug") return false;
            if (t.language == "python") return true;
            if (t.language == "javascript") return !string.IsNullOrEmpty(t.entry);
            return false;
        }

        public static bool IsSpec(string id) { return id != null && id.StartsWith(Prefix, StringComparison.Ordinal); }

        public static string Spec(string kind, string srcId, int seed) { return Prefix + kind + ":" + srcId + ":" + seed; }

        public static bool ParseSpec(string spec, out string kind, out string src, out int seed)
        {
            kind = src = null; seed = 0;
            if (!IsSpec(spec)) return false;
            var body = spec.Substring(Prefix.Length);
            int a = body.IndexOf(':'), b = body.LastIndexOf(':');
            if (a <= 0 || b <= a) return false;
            kind = body.Substring(0, a); src = body.Substring(a + 1, b - a - 1);
            return int.TryParse(body.Substring(b + 1), out seed) && (kind == "fix" || kind == "ask");
        }

        // Код задачи на доске и в проводнике: D12-3 (тикет дня 12, №3) или TR-5 (тренировка)
        public static string KeyFor(int seed) { return seed >= PracticeBase ? "TR-" + (seed - PracticeBase) : "D" + (seed / 100) + "-" + (seed % 100 + 1); }

        // ======================= сборка задачи =======================
        // level — уровень игрока: от него опыт задачи, подсказки и таймер
        public static TaskData Build(string spec, Func<string, TaskData> find, int level = 0)
        {
            string kind, srcId; int seed;
            if (!ParseSpec(spec, out kind, out srcId, out seed)) return null;
            var s = find != null ? find(srcId) : null;
            if (!CanGenerate(s)) return null;
            try { return kind == "ask" ? BuildAsk(s, seed, spec, level) : BuildFix(s, seed, spec, level); }
            catch (Exception e) { Debug.LogWarning("[Стажёр] Генератор: " + spec + ": " + e.Message); return null; }
        }

        static TaskData Base(TaskData s, string spec, int seed, string kind)
        {
            return new TaskData
            {
                id = spec, key = KeyFor(seed), generated = true, srcId = s.id, genKind = kind,
                track = s.track, topic = s.topic, chapter = s.chapter, grade = s.grade,
                language = s.language, entry = s.entry, testCases = s.testCases, tests = s.tests ?? new TestCase[0],
                theory = s.theory, difficulty = s.difficulty, timeLimit = 0,
                deadline = (3 + 2 * s.difficulty) * 60, legacy = false,
            };
        }

        // Опыт и монеты — от уровня игрока (Levels.TicketXp); с Principal — таймер
        static void Reward(TaskData t, int level, int seed, int minutes)
        {
            t.xp = Levels.TicketXp(level, t.genKind, t.difficulty, seed >= PracticeBase);
            t.reward = t.xp * Levels.TicketCoinsPerXp;
            if (level >= Levels.TimerFrom) { t.timeLimit = minutes; t.deadline = minutes * 60; }
        }

        static readonly string[][] FixStories =
        {
            new[] { "QA Ира", "qa", "После вчерашнего рефакторинга «{0}» снова падает на тестах. Кто-то поменял одну строку и не прогнал pytest. Найди и почини." },
            new[] { "Сеньор Марина", "teamlead", "Смотрю пул-реквест к «{0}»: код выглядит нормально, но CI красный. Там ровно одна ошибка — найдёшь?" },
            new[] { "Продакт Стас", "manager", "Клиент жалуется: «{0}» считает неправильно. Вчера всё работало! Разберись, пожалуйста, до вечера." },
            new[] { "Тимлид Гена", "teamlead", "Стажёр из соседней команды «оптимизировал» «{0}». Тесты упали. Верни как было — там одна правка." },
        };

        static readonly string[][] AskStories =
        {
            new[] { "Сеньор Марина", "teamlead", "Код-ревью «{0}». Без запуска, просто глазами: что получится?" },
            new[] { "Тимлид Гена", "teamlead", "Разминка перед стендапом. Вот рабочий код «{0}» — прокрути его в голове." },
            new[] { "QA Ира", "qa", "Пишу тест-кейс к «{0}» и хочу проверить себя. Что должно получиться?" },
        };

        static TaskData BuildFix(TaskData s, int seed, string spec, int level)
        {
            var m = PickMutants(s, seed, 1).FirstOrDefault();
            if (m == null) return null;
            var t = Base(s, spec, seed, "fix");
            var rng = new Rng(seed * 7 + 3);
            var st = FixStories[rng.Next(FixStories.Length)];
            t.type = "find_bug"; t.isBugHunt = true;
            t.sender = st[0]; t.character = st[1];
            t.title = "Почини: " + s.title;
            t.story = string.Format(st[2], s.title);
            t.goal = "В коде ровно одна ошибка, из-за неё тесты падают. Найди её и исправь — переписывать всё не нужно.";
            t.starter = m.code;
            t.solution = s.solution;
            t.hints = level >= Levels.LineHintUntil ? new[] { KindHint(m.kind, s.language) } : new[] { KindHint(m.kind, s.language), "Ошибка в строке " + m.line + "." };
            t.explanation = "Ошибка была в строке " + m.line + ": " + m.what + ".\nС ошибкой:  " + m.after.Trim() + "\nПравильно:  " + m.before.Trim() +
                            (string.IsNullOrEmpty(s.explanation) ? "" : "\n\nПро исходную задачу: " + s.explanation);
            Reward(t, level, seed, 3 + 2 * s.difficulty);
            return t;
        }

        static TaskData BuildAsk(TaskData s, int seed, string spec, int level)
        {
            var orig = Original(s);
            if (orig == null) return null;
            var muts = PickMutants(s, seed, 8);
            var rng = new Rng(seed * 13 + Fnv(s.id));
            int n = orig.Count, start = rng.Next(n);
            bool func = !string.IsNullOrEmpty(s.entry);
            for (int k = 0; k < n; k++)
            {
                int ti = (start + k) % n;
                string right = Shown(orig[ti]);
                if (string.IsNullOrEmpty(right) || right.Length > 90 || right.Contains("\n") && right.Split('\n').Length > 4) continue;
                var wrong = new List<KeyValuePair<string, GenMutant>>();
                bool haveError = false;
                foreach (var m in muts)
                {
                    if (m.res == null || ti >= m.res.Count) continue;
                    var r = m.res[ti];
                    string a = r.Error != null ? ErrorOption(r.Error.PyType) : Shown(r);
                    if (string.IsNullOrEmpty(a) || a == right || a.Length > 90 || wrong.Any(w => w.Key == a)) continue;
                    bool isErr = r.Error != null || a.StartsWith("Упадёт", StringComparison.Ordinal);
                    if (isErr && haveError) continue;
                    haveError |= isErr;
                    wrong.Add(new KeyValuePair<string, GenMutant>(a, m));
                }
                foreach (var extra in Heuristic(right, s.language))
                    if (wrong.Count < 3 && extra != right && wrong.All(w => w.Key != extra)) wrong.Add(new KeyValuePair<string, GenMutant>(extra, null));
                if (wrong.Count < 2) continue;
                // сначала варианты от мутантов, их не больше трёх
                wrong = wrong.OrderBy(w => w.Value == null ? 1 : 0).Take(3).ToList();
                var opts = new List<string> { right }; opts.AddRange(wrong.Select(w => w.Key));
                var order = Enumerable.Range(0, opts.Count).ToList();
                for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
                var t = Base(s, spec, seed, "ask");
                var st = AskStories[rng.Next(AskStories.Length)];
                t.type = "quiz"; t.sender = st[0]; t.character = st[1];
                t.title = (func ? "Что вернёт: " : "Что выведет: ") + s.title;
                t.story = string.Format(st[2], s.title);
                string call = orig[ti].InputsText ?? "";
                t.goal = func ? "Что вернёт вызов " + call + "?" : "Что выведет программа" + (call == "(без ввода)" || call.Length == 0 ? "?" : " при вводе " + call + "?");
                t.starter = s.solution;
                t.options = order.Select(i => opts[i]).ToArray();
                t.answer = new[] { order.IndexOf(0) };
                t.multi = false;
                t.hints = new[] { "Пройди код по шагам и выпиши значения переменных на каждом шаге.", "Проверь границы: включается ли последний элемент, что будет на первом шаге цикла." };
                var ex = new StringBuilder("Правильный ответ: " + right + ".");
                foreach (var w in wrong)
                    if (w.Value != null) ex.Append("\n• «").Append(w.Key).Append("» — если бы в строке ").Append(w.Value.line).Append(" было ").Append(Code1(w.Value.after)).Append(" вместо ").Append(Code1(w.Value.before)).Append('.');
                if (!string.IsNullOrEmpty(s.explanation)) ex.Append("\n\n").Append(s.explanation);
                t.explanation = ex.ToString();
                Reward(t, level, seed, 2 + s.difficulty);
                return t;
            }
            return null;
        }

        static string Shown(CheckResult r)
        {
            if (r == null || r.Error != null) return null;
            var a = r.Actual ?? "";
            return a.Length == 0 ? "(ничего)" : a;
        }

        static string ErrorOption(string type) { return "Упадёт с ошибкой " + (string.IsNullOrEmpty(type) ? "" : type); }

        // Строка кода для разбора: без отступа, длинная — с многоточием
        static string Code1(string line)
        {
            var t = (line ?? "").Trim();
            if (t.Length == 0) t = "(пусто)";
            if (t.Length > 60) t = t.Substring(0, 57) + "…";
            return "«" + t + "»";
        }

        // Правдоподобные неверные ответы, если мутантов не хватило
        static IEnumerable<string> Heuristic(string right, string lang)
        {
            long n;
            if (long.TryParse(right, out n)) { yield return (n + 1).ToString(); yield return (n - 1).ToString(); yield return (n * 2).ToString(); yield break; }
            if (right == "True") { yield return "False"; yield return "None"; yield break; }
            if (right == "False") { yield return "True"; yield return "None"; yield break; }
            if (right == "true") { yield return "false"; yield return "null"; yield break; }
            if (right == "false") { yield return "true"; yield return "null"; yield break; }
            if (right.StartsWith("[") && right.EndsWith("]"))
            {
                yield return lang == "python" ? "None" : "null";
                yield return "[]";
            }
            yield return lang == "python" ? "None" : "null";
        }

        public static string KindHint(string kind, string lang)
        {
            bool py = lang == "python";
            switch (kind)
            {
                case "cmp": return "Проверь знаки сравнения: < или <=, > или >=, " + (py ? "== или !=" : "=== или !==") + ".";
                case "off": return "Похоже на ошибку на единицу: проверь границы диапазонов, индексы и числа.";
                case "ret": return "Функция что-то вычисляет, но отдаёт ли она результат?";
                case "swap": return "Проверь порядок операндов: a - b и b - a — не одно и то же.";
                case "arith": return "Проверь арифметические операторы: +, -, *, " + (py ? "/ и //" : "/") + ".";
                case "logic": return "Проверь логику: " + (py ? "and/or, not, True/False" : "&&/||, !, true/false") + ", min/max.";
                default: return "Сравни поведение кода с условием задачи на первом падающем тесте.";
            }
        }

        // ======================= проверка кода тестами исходной задачи =======================
        const int MutantSteps = 300000;
        static readonly Dictionary<string, List<CheckResult>> origCache = new Dictionary<string, List<CheckResult>>();
        static readonly Dictionary<string, List<GenMutant>> candCache = new Dictionary<string, List<GenMutant>>();
        static readonly Dictionary<string, int> stepsFor = new Dictionary<string, int>();

        public static void ClearCache() { origCache.Clear(); candCache.Clear(); stepsFor.Clear(); }

        public static List<CheckResult> RunTests(TaskData s, string code, int steps)
        {
            if (s.language == "javascript")
            {
                var lim = new JsLimits { TimeoutSeconds = 1.0, TotalTimeoutSeconds = 4.0, MaxStatements = Math.Max(100000, steps * 3) };
                var jr = JsRun.Check(code, s.entry, s.testCases, lim);
                JsError fe;
                var list = TaskChecks.FromJs(jr, out fe);
                // ошибки JS — в общем виде: тип для варианта «Упадёт с ошибкой …», лимиты — как зависание
                for (int i = 0; i < list.Count && i < jr.Tests.Count; i++)
                {
                    var e = jr.Tests[i].Error ?? (jr.Tests[i].Passed ? null : jr.LoadError);
                    if (e == null) continue;
                    bool hang = jr.Tests[i].Skipped || e.Type == "StepLimit" || e.Type == "Timeout" || e.Type == "Hang";
                    list[i].Error = new PyError(e.Type ?? "Error", e.Text ?? "", e.Line) { Fatal = hang };
                    if (e == jr.LoadError) list[i].Error.Line = 0;
                }
                return list;
            }
            if (!string.IsNullOrEmpty(s.entry))
            {
                var ft = new List<FuncTest>();
                foreach (var x in s.testCases)
                {
                    var d = x as Dictionary<string, object>;
                    if (d == null) continue;
                    object inp, exp; d.TryGetValue("input", out inp); d.TryGetValue("expected", out exp);
                    ft.Add(new FuncTest(inp, exp));
                }
                return PyRun.CheckFunction(code, s.entry, ft, steps);
            }
            return PyRun.Check(code, s.tests, steps);
        }

        // Эталон на тестах: null — эталон не проходит (такую задачу не мутируем)
        public static List<CheckResult> Original(TaskData s)
        {
            List<CheckResult> r;
            if (origCache.TryGetValue(s.id, out r)) return r;
            int steps = MutantSteps;
            r = RunTests(s, s.solution, steps);
            if (r.Count == 0 || !r.All(x => x.Passed)) { steps = 1000000; r = RunTests(s, s.solution, steps); }
            if (r.Count == 0 || !r.All(x => x.Passed)) r = null;
            origCache[s.id] = r; stepsFor[s.id] = steps;
            return r;
        }

        static bool Hung(CheckResult r) { return r.Error != null && r.Error.Fatal; }

        // Годные мутанты в порядке, заданном seed: разбираются, не виснут, валят хотя бы один тест
        public static List<GenMutant> PickMutants(TaskData s, int seed, int want)
        {
            var res = new List<GenMutant>();
            if (Original(s) == null) return res;
            List<GenMutant> cands;
            if (!candCache.TryGetValue(s.id, out cands)) { cands = Mutants(s.solution, s.language); candCache[s.id] = cands; }
            if (cands.Count == 0) return res;
            var rng = new Rng(seed * 31 + Fnv(s.id));
            // сначала выбираем вид ошибки, потом конкретное место — так виды чередуются, а не тонут в числах
            var byKind = cands.GroupBy(c => c.kind).ToDictionary(gr => gr.Key, gr => gr.ToList());
            var kinds = byKind.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            int tries = 0, maxTries = want <= 1 ? 16 : 24;
            while (res.Count < want && tries < maxTries && kinds.Count > 0)
            {
                tries++;
                var kind = kinds[rng.Next(kinds.Count)];
                var list = byKind[kind];
                var m = list[rng.Next(list.Count)];
                list.Remove(m); if (list.Count == 0) kinds.Remove(kind);
                if (!Evaluate(s, m)) continue;
                res.Add(m);
            }
            return res;
        }

        static readonly Dictionary<string, bool> verdicts = new Dictionary<string, bool>();

        static bool Evaluate(TaskData s, GenMutant m)
        {
            string key = s.id + "#" + m.index;
            bool ok;
            if (verdicts.TryGetValue(key, out ok)) return ok;
            ok = false;
            if (Parses(m.code, s.language))
            {
                int steps; stepsFor.TryGetValue(s.id, out steps); if (steps <= 0) steps = MutantSteps;
                m.res = RunTests(s, m.code, steps);
                ok = m.res.Count > 0 && m.res.Any(r => !r.Passed) && !m.res.Any(Hung);
                // упал уже при загрузке (нет функции, синтаксис) — это не «одна строка с ошибкой»
                if (ok && m.res.All(r => r.Error != null && r.Error.Line == 0 && (r.Error.PyType == "NameError" || r.Error.PyType == "NoFunction" || r.Error.PyType == "SyntaxError"))) ok = false;
            }
            verdicts[key] = ok;
            return ok;
        }

        static bool Parses(string code, string lang)
        {
            if (lang == "javascript") { try { return JsRun.SyntaxCheck(code) == null; } catch (Exception) { return false; } }
            try { Parser.ParseProgram(code); return true; }
            catch (Exception) { return false; }
        }

        // ======================= мутации =======================
        struct Edit { public int at, len; public string with, kind, what; }

        public static List<GenMutant> Mutants(string code, string lang)
        {
            code = (code ?? "").Replace("\r\n", "\n");
            bool py = lang != "javascript";
            var mask = CodeMask(code, py);
            var edits = new List<Edit>();
            Comparisons(code, mask, py, edits);
            Ranges(code, mask, py, edits);
            Numbers(code, mask, edits);
            Returns(code, mask, py, edits);
            Swaps(code, mask, edits);
            Arith(code, mask, py, edits);
            Logic(code, mask, py, edits);
            var res = new List<GenMutant>();
            var seen = new HashSet<string>();
            foreach (var e in edits)
            {
                var mc = code.Substring(0, e.at) + e.with + code.Substring(e.at + e.len);
                if (mc == code || !seen.Add(mc)) continue;
                int line = 1; for (int i = 0; i < e.at; i++) if (code[i] == '\n') line++;
                var ol = code.Split('\n'); var nl = mc.Split('\n');
                res.Add(new GenMutant
                {
                    index = res.Count, code = mc, kind = e.kind, what = e.what, line = line,
                    before = line - 1 < ol.Length ? ol[line - 1] : "", after = line - 1 < nl.Length ? nl[line - 1] : "",
                });
            }
            return res;
        }

        static bool AllCode(bool[] mask, int at, int len)
        {
            if (at < 0 || at + len > mask.Length) return false;
            for (int i = at; i < at + len; i++) if (!mask[i]) return false;
            return true;
        }

        static void Add(List<Edit> list, bool[] mask, int at, int len, string with, string kind, string what)
        {
            if (!AllCode(mask, at, len)) return;
            list.Add(new Edit { at = at, len = len, with = with, kind = kind, what = what });
        }

        static string Q(string s) { return "«" + s.Trim() + "»"; }

        // < ↔ <=, > ↔ >=, == ↔ !=
        static readonly Regex CmpRe = new Regex(@"===|!==|==|!=|<=|>=|<|>");
        static void Comparisons(string c, bool[] mask, bool py, List<Edit> list)
        {
            foreach (Match m in CmpRe.Matches(c))
            {
                string op = m.Value; int at = m.Index;
                char prev = at > 0 ? c[at - 1] : ' ', next = at + op.Length < c.Length ? c[at + op.Length] : ' ';
                if (prev == '<' || prev == '>' || prev == '=' || prev == '-' || prev == '!') continue;   // <<, >>, =>, ->
                if (next == '<' || next == '>' || next == '=') continue;
                if (!py && (op == "==" || op == "!=")) continue;   // в JS пишут === и !==
                string to;
                switch (op)
                {
                    case "<": to = "<="; break; case "<=": to = "<"; break;
                    case ">": to = ">="; break; case ">=": to = ">"; break;
                    case "==": to = "!="; break; case "!=": to = "=="; break;
                    case "===": to = "!=="; break; case "!==": to = "==="; break;
                    default: continue;
                }
                Add(list, mask, at, op.Length, to, "cmp", "стоит " + Q(to) + " вместо " + Q(op));
            }
        }

        // range(n) → range(n - 1) / range(1, n); range(a, b) → range(a, b ± 1) / range(a + 1, b); len(x) → len(x) - 1; .length → .length - 1
        static void Ranges(string c, bool[] mask, bool py, List<Edit> list)
        {
            if (py)
            {
                foreach (Match m in Regex.Matches(c, @"\brange\("))
                {
                    int open = m.Index + m.Length - 1, close = Matching(c, mask, open);
                    if (close < 0 || !mask[m.Index]) continue;
                    var args = SplitTop(c.Substring(open + 1, close - open - 1));
                    string inner = c.Substring(open + 1, close - open - 1);
                    if (args.Count == 1)
                    {
                        Add(list, mask, open + 1, inner.Length, Shift(args[0], -1), "off", "в range стоит " + Q(Shift(args[0], -1)) + " вместо " + Q(args[0]));
                        Add(list, mask, open + 1, inner.Length, "1, " + args[0].Trim(), "off", "range начинается с 1, а не с 0");
                    }
                    else if (args.Count == 2)
                    {
                        string a = args[0].Trim(), b = args[1].Trim();
                        Add(list, mask, open + 1, inner.Length, a + ", " + Shift(b, -1), "off", "в range верхняя граница " + Q(Shift(b, -1)) + " вместо " + Q(b));
                        Add(list, mask, open + 1, inner.Length, a + ", " + Shift(b, +1), "off", "в range верхняя граница " + Q(Shift(b, +1)) + " вместо " + Q(b));
                        Add(list, mask, open + 1, inner.Length, Shift(a, +1) + ", " + b, "off", "в range начало " + Q(Shift(a, +1)) + " вместо " + Q(a));
                    }
                }
                foreach (Match m in Regex.Matches(c, @"\blen\("))
                {
                    int open = m.Index + m.Length - 1, close = Matching(c, mask, open);
                    if (close < 0 || !mask[m.Index]) continue;
                    var tail = c.Substring(close + 1, Math.Min(6, c.Length - close - 1));
                    if (Regex.IsMatch(tail, @"^\s*[-+]\s*1\b")) continue;
                    string call = c.Substring(m.Index, close - m.Index + 1);
                    Add(list, mask, m.Index, call.Length, call + " - 1", "off", "стоит " + Q(call + " - 1") + " вместо " + Q(call));
                }
            }
            else
            {
                foreach (Match m in Regex.Matches(c, @"\.length\b"))
                {
                    int end = m.Index + m.Length;
                    var tail = c.Substring(end, Math.Min(6, c.Length - end));
                    if (Regex.IsMatch(tail, @"^\s*([-+]\s*1\b|=[^=])")) continue;
                    Add(list, mask, m.Index, m.Length, ".length - 1", "off", "стоит «.length - 1» вместо «.length»");
                }
            }
        }

        static string Shift(string e, int d)
        {
            e = e.Trim();
            long n;
            if (long.TryParse(e, out n)) return (n + d).ToString();
            var m = Regex.Match(e, @"^(.*\S)\s*([-+])\s*(\d+)$");
            if (m.Success)
            {
                long k = long.Parse(m.Groups[3].Value) * (m.Groups[2].Value == "-" ? -1 : 1) + d;
                if (k == 0) return m.Groups[1].Value;
                return m.Groups[1].Value + (k < 0 ? " - " + (-k) : " + " + k);
            }
            bool simple = Regex.IsMatch(e, @"^[\w.\[\]()]+$");
            return (simple ? e : "(" + e + ")") + (d < 0 ? " - " + (-d) : " + " + d);
        }

        static int Matching(string c, bool[] mask, int open)
        {
            int depth = 0;
            for (int i = open; i < c.Length; i++)
            {
                if (!mask[i]) continue;
                if (c[i] == '(' || c[i] == '[' || c[i] == '{') depth++;
                else if (c[i] == ')' || c[i] == ']' || c[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        static List<string> SplitTop(string s)
        {
            var res = new List<string>(); int depth = 0, from = 0; char q = '\0';
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (q != '\0') { if (ch == '\\') i++; else if (ch == q) q = '\0'; continue; }
                if (ch == '"' || ch == '\'') q = ch;
                else if (ch == '(' || ch == '[' || ch == '{') depth++;
                else if (ch == ')' || ch == ']' || ch == '}') depth--;
                else if (ch == ',' && depth == 0) { res.Add(s.Substring(from, i - from)); from = i + 1; }
            }
            res.Add(s.Substring(from));
            return res;
        }

        // Целые числа в коде: n → n + 1 и n − 1
        static readonly Regex NumRe = new Regex(@"(?<![\w.])\d+(?![\w.])");
        static void Numbers(string c, bool[] mask, List<Edit> list)
        {
            foreach (Match m in NumRe.Matches(c))
            {
                long n; if (!long.TryParse(m.Value, out n) || n > 100000) continue;
                // пропускаем числа в объявлениях по умолчанию и в заголовках вида def f(x=0)? — нет, это тоже поведение
                Add(list, mask, m.Index, m.Length, (n + 1).ToString(), "off", "стоит " + Q((n + 1).ToString()) + " вместо " + Q(m.Value));
                // «n + 1» → «n + 0» выглядит нелепо: такой случай уже даёт мутация range/len
                bool afterSign = Regex.IsMatch(c.Substring(Math.Max(0, m.Index - 3), Math.Min(3, m.Index)), @"[-+]\s*$");
                if (n > 0 && !(n == 1 && afterSign)) Add(list, mask, m.Index, m.Length, (n - 1).ToString(), "off", "стоит " + Q((n - 1).ToString()) + " вместо " + Q(m.Value));
            }
        }

        // return x → x (функция возвращает None / undefined)
        static void Returns(string c, bool[] mask, bool py, List<Edit> list)
        {
            var re = py ? new Regex(@"(?m)^([ \t]*)return[ \t]+(\S[^\n]*)$") : new Regex(@"(?m)^([ \t]*)return[ \t]+([^\s{;][^\n]*)$");
            foreach (Match m in re.Matches(c))
            {
                int at = m.Groups[1].Index + m.Groups[1].Length;
                if (!AllCode(mask, at, 6)) continue;
                string expr = m.Groups[2].Value;
                if (py && expr.TrimStart().StartsWith("#")) continue;
                Add(list, mask, at, m.Groups[2].Index - at, "", "ret", "забыт return: значение вычисляется, но не возвращается");
            }
        }

        // a - b → b - a (и для /, //, %, сравнений)
        const string Operand = @"(?:[A-Za-z_]\w*(?:\.\w+)*(?:\[[^\[\]\n]*\]|\([^()\n]*\))?|\d+(?:\.\d+)?)";
        static readonly Regex SwapRe = new Regex(@"(?<![\w.\])\]])(" + Operand + @")([ \t]*)(//|-|/|%|<=|>=|<|>)([ \t]*)(" + Operand + @")(?![\w.(\[])");
        static void Swaps(string c, bool[] mask, List<Edit> list)
        {
            foreach (Match m in SwapRe.Matches(c))
            {
                string a = m.Groups[1].Value, op = m.Groups[3].Value, b = m.Groups[5].Value;
                if (a == b) continue;
                int opAt = m.Groups[3].Index, end = opAt + op.Length;
                char prev = opAt > 0 ? c[opAt - 1] : ' ', next = end < c.Length ? c[end] : ' ';
                if (next == '=' || next == '>' || prev == '=' || (op == "-" && next == '-') || (op == "/" && next == '/')) continue;
                if (op == "<" || op == ">") { if (prev == '<' || prev == '>' || next == '<') continue; }
                // левый операнд не должен быть частью ключевого слова (return, in, not…)
                if (Regex.IsMatch(a, @"^(return|in|not|and|or|if|elif|while|yield|else|lambda|await|typeof|new|case)$")) continue;
                if (Regex.IsMatch(b, @"^(in|not|and|or|if|else|for)$")) continue;
                string with = b + m.Groups[2].Value + op + m.Groups[4].Value + a;
                Add(list, mask, m.Index, m.Length, with, "swap", "операнды переставлены: " + Q(with) + " вместо " + Q(m.Value));
            }
        }

        // + ↔ -, * → +, // → / (Python), += ↔ -=
        static readonly Regex ArithRe = new Regex(@"(?<=\S)[ \t]+(\+=|-=|\+|-|\*|//)[ \t]+(?=\S)");
        static void Arith(string c, bool[] mask, bool py, List<Edit> list)
        {
            foreach (Match m in ArithRe.Matches(c))
            {
                var g = m.Groups[1]; string op = g.Value, to;
                char prev = g.Index > 0 ? c[g.Index - 1] : ' ';
                switch (op)
                {
                    case "+": to = "-"; break; case "-": to = "+"; break; case "*": to = "+"; break;
                    case "+=": to = "-="; break; case "-=": to = "+="; break;
                    case "//": if (!py) continue; to = "/"; break;
                    default: continue;
                }
                // строки через + не трогаем, если рядом кавычка — это скорее склейка текста
                int ls = Math.Max(0, g.Index - 2), le = Math.Min(c.Length, g.Index + op.Length + 3);
                string around = c.Substring(ls, le - ls);
                if ((op == "+" || op == "+=") && (around.Contains("\"") || around.Contains("'") || around.Contains("`"))) continue;
                Add(list, mask, g.Index, op.Length, to, "arith", "стоит " + Q(to) + " вместо " + Q(op));
            }
        }

        // and ↔ or, not x → x, True ↔ False, min ↔ max, startswith ↔ endswith
        static void Logic(string c, bool[] mask, bool py, List<Edit> list)
        {
            var pairs = py
                ? new[] { new[] { @"\band\b", "and", "or" }, new[] { @"\bor\b", "or", "and" }, new[] { @"\bTrue\b", "True", "False" }, new[] { @"\bFalse\b", "False", "True" },
                          new[] { @"\bmin\(", "min(", "max(" }, new[] { @"\bmax\(", "max(", "min(" }, new[] { @"\.startswith\(", ".startswith(", ".endswith(" }, new[] { @"\.endswith\(", ".endswith(", ".startswith(" },
                          new[] { @"\bnot[ \t]+", "not ", "" } }
                : new[] { new[] { @"&&", "&&", "||" }, new[] { @"\|\|", "||", "&&" }, new[] { @"\btrue\b", "true", "false" }, new[] { @"\bfalse\b", "false", "true" },
                          new[] { @"Math\.min\(", "Math.min(", "Math.max(" }, new[] { @"Math\.max\(", "Math.max(", "Math.min(" }, new[] { @"\.startsWith\(", ".startsWith(", ".endsWith(" }, new[] { @"\.endsWith\(", ".endsWith(", ".startsWith(" },
                          new[] { @"!(?=[\w(])", "!", "" } };
            foreach (var p in pairs)
                foreach (Match m in Regex.Matches(c, p[0]))
                {
                    string what = p[2].Length == 0 ? "пропущено " + Q(p[1]) : "стоит " + Q(p[2]) + " вместо " + Q(p[1]);
                    if (p[1] == "not " && m.Index > 0 && Regex.IsMatch(c.Substring(Math.Max(0, m.Index - 3), Math.Min(3, m.Index)), @"is\s$")) continue;   // is not
                    Add(list, mask, m.Index, m.Length, p[2], "logic", what);
                }
        }

        // Маска «здесь код»: false внутри строк и комментариев
        public static bool[] CodeMask(string s, bool py)
        {
            var m = new bool[s.Length];
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (py && c == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (!py && c == '/' && i + 1 < s.Length && s[i + 1] == '/') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (!py && c == '/' && i + 1 < s.Length && s[i + 1] == '*') { int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal); i = e < 0 ? s.Length : e + 2; continue; }
                if (c == '"' || c == '\'' || (!py && c == '`'))
                {
                    if (py && i + 2 < s.Length && s[i + 1] == c && s[i + 2] == c)
                    {
                        int e = s.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal);
                        i = e < 0 ? s.Length : e + 3; continue;
                    }
                    int j = i + 1;
                    while (j < s.Length && s[j] != c && (c == '`' || s[j] != '\n')) { if (s[j] == '\\') j++; j++; }
                    i = Math.Min(s.Length, j + 1); continue;
                }
                m[i] = true; i++;
            }
            return m;
        }

        // ======================= выбор задач =======================
        // Тикеты дня: 6–8 задач из генератора по пройденному пути (без повторов исходных задач, пока хватает)
        public static List<string> Daily(IList<TaskData> pool, int day, string salt, Func<string, TaskData> find, int count = -1, int level = 0)
        {
            var res = new List<string>();
            var src = pool.Where(CanGenerate).ToList();
            if (src.Count == 0) return res;
            var rng = new Rng(day * 7919 + Fnv(salt ?? ""));
            if (count < 0) count = 6 + rng.Next(3);
            var order = Enumerable.Range(0, src.Count).ToList();
            for (int i = order.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); int tmp = order[i]; order[i] = order[j]; order[j] = tmp; }
            int slot = 0, attempts = 0;
            for (int k = 0; res.Count < count && attempts < count * 4; k++, attempts++)
            {
                var s = src[order[k % order.Count]];
                string kind = rng.Next(100) < 60 ? "fix" : "ask";
                var spec = Spec(kind, s.id, day * 100 + slot);
                var t = Build(spec, find, level);
                if (t == null && kind == "ask") { spec = Spec("fix", s.id, day * 100 + slot); t = Build(spec, find, level); }
                if (t == null) continue;
                res.Add(spec); slot++;
            }
            return res;
        }

        // Тренировка по теме: новая задача из исходных задач темы
        public static string Practice(IList<TaskData> topicTasks, int number, Func<string, TaskData> find, int level = 0)
        {
            var src = topicTasks.Where(CanGenerate).ToList();
            if (src.Count == 0) return null;
            var rng = new Rng(number * 104729 + 17);
            for (int attempt = 0; attempt < src.Count * 2; attempt++)
            {
                var s = src[(rng.Next(src.Count) + attempt) % src.Count];
                string kind = (number + attempt) % 3 == 2 ? "ask" : "fix";
                var spec = Spec(kind, s.id, PracticeBase + number);
                if (Build(spec, find, level) != null) return spec;
                spec = Spec(kind == "ask" ? "fix" : "ask", s.id, PracticeBase + number);
                if (Build(spec, find, level) != null) return spec;
            }
            return null;
        }

        // ======================= помощники =======================
        public static int Fnv(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in s) { h ^= ch; h *= 16777619; }
                return (int)(h & 0x7FFFFFFF);
            }
        }

        // Детерминированный генератор (одинаковый на всех платформах)
        public struct Rng
        {
            uint x;
            public Rng(int seed) { unchecked { x = (uint)seed * 2654435761u + 0x9E3779B9u; if (x == 0) x = 1; } Next(2); }
            public int Next(int n)
            {
                if (n <= 1) { Step(); return 0; }
                return (int)(Step() % (uint)n);
            }
            uint Step() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x; }
        }
    }
}
