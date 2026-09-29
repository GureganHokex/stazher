// Проверка ответов на задачи направлений, кроме Python (он в PyCore): выбор вариантов, статические проверки
// (регулярки для YAML, Dockerfile, bash, TS/TSX, HTML/CSS, nginx, Terraform, PromQL), SQL и перевод результатов
// JavaScript в общий вид CheckResult — чтобы вкладка «Тесты» показывала всё одинаково.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Intern.Py;

namespace Intern.Game
{
    public static class TaskChecks
    {
        // ---------- выбор вариантов ----------
        public class ChoiceVerdict
        {
            public bool Correct;
            public int RightPicked, WrongPicked, Missing;
            public HashSet<int> Wrong = new HashSet<int>();
        }

        public static ChoiceVerdict Choice(TaskData t, ICollection<int> picks)
        {
            var v = new ChoiceVerdict();
            var right = new HashSet<int>(t.answer ?? new int[0]);
            foreach (var p in picks)
            {
                if (right.Contains(p)) v.RightPicked++;
                else { v.WrongPicked++; v.Wrong.Add(p); }
            }
            v.Missing = right.Count - v.RightPicked;
            v.Correct = v.WrongPicked == 0 && v.Missing == 0 && picks.Count > 0;
            return v;
        }

        // ---------- статические проверки ----------
        public static List<CheckResult> Static(string code, IList<object> tests)
        {
            var res = new List<CheckResult>();
            if (tests == null) return res;
            code = (code ?? "").Replace("\r\n", "\n");
            foreach (var x in tests)
            {
                var tc = x as Dictionary<string, object>;
                if (tc == null) continue;
                object inp, exp;
                tc.TryGetValue("input", out inp); tc.TryGetValue("expected", out exp);
                string desc = inp as string ?? "";
                var cr = new CheckResult { InputsText = desc, Expected = "", Actual = "" };
                var e = exp as Dictionary<string, object>;
                object pat = null; bool negative = false;
                if (e != null && !e.TryGetValue("regex", out pat)) { negative = e.TryGetValue("not_regex", out pat); }
                if (!(pat is string)) { cr.Note = "Проверка задана неверно — это баг контента, сообщи Гене."; res.Add(cr); continue; }
                object fl; string flags = e.TryGetValue("flags", out fl) ? fl as string ?? "" : "";
                var opt = RegexOptions.CultureInvariant;
                if (flags.IndexOf('i') >= 0) opt |= RegexOptions.IgnoreCase;
                if (flags.IndexOf('m') >= 0) opt |= RegexOptions.Multiline;
                if (flags.IndexOf('s') >= 0) opt |= RegexOptions.Singleline;
                try
                {
                    bool m = Regex.IsMatch(code, (string)pat, opt, TimeSpan.FromSeconds(1));
                    cr.Passed = negative ? !m : m;
                    cr.Actual = cr.Passed ? "выполнено" : "не выполнено";
                }
                catch (RegexMatchTimeoutException) { cr.Note = "Проверка не уложилась во время. Упрости текст и попробуй ещё раз."; }
                catch (ArgumentException ex) { cr.Note = "Ошибка в шаблоне проверки (баг контента): " + ex.Message; }
                if (!cr.Passed && cr.Note == null)
                    cr.Note = negative ? "В решении осталось то, чего быть не должно (" + desc + ")." : "Требование не выполнено — проверь, есть ли это в решении.";
                res.Add(cr);
            }
            return res;
        }


        // ---------- SQL ----------
        public static List<CheckResult> Sql(string code, IList<object> tests, out SqlResult last)
        {
            var res = new List<CheckResult>();
            last = null;
            if (tests == null) return res;
            foreach (var x in tests)
            {
                var tc = x as Dictionary<string, object>;
                if (tc == null) continue;
                object exp; tc.TryGetValue("expected", out exp);
                var sc = SqlRun.Check(code, exp as IList ?? new List<object>());
                last = sc.Result;
                res.Add(new CheckResult
                {
                    Passed = sc.Passed,
                    InputsText = "результат последнего запроса",
                    Expected = sc.Expected ?? "",
                    Actual = sc.Actual ?? "",
                    Note = sc.Note,
                });
            }
            return res;
        }

        // ---------- JavaScript ----------
        public static List<CheckResult> FromJs(JsCheckResult r, out JsError firstError)
        {
            var res = new List<CheckResult>();
            firstError = r != null ? r.LoadError : null;
            if (r == null) return res;
            foreach (var t in r.Tests)
            {
                var notes = new List<string>();
                if (t.Skipped) notes.Add("Тест не запускался: закончилось время на проверку (где-то бесконечный цикл?).");
                if (!string.IsNullOrEmpty(t.Note)) notes.Add(t.Note);
                var err = t.Error ?? (t.Passed ? null : r.LoadError);
                if (err != null) { notes.Add(err.ToString()); if (firstError == null) firstError = err; }
                if (!string.IsNullOrEmpty(t.Log)) notes.Add("console: " + Short(t.Log.TrimEnd('\n'), 400));
                res.Add(new CheckResult
                {
                    Passed = t.Passed,
                    InputsText = t.Call ?? t.InputText ?? "",
                    Expected = t.Expected ?? "",
                    Actual = t.Actual ?? (err != null ? "(ошибка)" : ""),
                    Note = notes.Count > 0 ? string.Join("\n", notes.ToArray()) : null,
                });
            }
            if (res.Count == 0 && r.LoadError != null)
                res.Add(new CheckResult { Passed = false, InputsText = "загрузка кода", Expected = "", Actual = "", Note = r.LoadError.ToString() });
            return res;
        }

        static string Short(string s, int n) { return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

        // ---------- задачи без запуска (спринт 9) ----------
        // Вывод сравнивается строка в строку; пробелы в конце строк и пустые строки в конце не важны
        public static string NormOut(string s)
        {
            var lines = (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines.ToArray());
        }

        public static bool Predict(TaskData t, string answer, out string note)
        {
            note = null;
            string want = NormOut(t.output), got = NormOut(answer);
            if (want == got) return true;
            var wl = want.Split('\n'); var gl = got.Length == 0 ? new string[0] : got.Split('\n');
            if (gl.Length == 0) { note = "Впиши вывод программы."; return false; }
            for (int i = 0; i < Math.Min(wl.Length, gl.Length); i++)
                if (wl[i] != gl[i]) { note = "Строка " + (i + 1) + " не совпадает." + (wl[i].Trim() == gl[i].Trim() ? " Проверь пробелы в начале строки." : ""); return false; }
            note = "Строк в выводе " + wl.Length + ", а у тебя " + gl.Length + ".";
            return false;
        }

        public static bool ClozeOne(ClozeBlank b, string v)
        {
            v = (v ?? "").Trim();
            if (v.Length == 0 || b == null) return false;
            foreach (var a in b.answers) if (a.Trim() == v) return true;
            if (!string.IsNullOrEmpty(b.regex))
                try { return Regex.IsMatch(v, "^(?:" + b.regex + ")$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); } catch (Exception) { }
            return false;
        }

        // Код с подставленными ответами (null — эталонные); пустой пропуск показывается как ‹N›
        public static string ClozeCode(TaskData t, IList<string> vals)
        {
            string code = t.starter ?? "";
            int n = t.blanks != null ? t.blanks.Count : 0;
            for (int i = 0; i < n; i++)
            {
                string v = vals == null ? (t.blanks[i].answers.Length > 0 ? t.blanks[i].answers[0] : "") : i < vals.Count ? vals[i] : "";
                code = code.Replace("[[" + (i + 1) + "]]", string.IsNullOrEmpty(v) ? "‹" + (i + 1) + "›" : v);
            }
            return code;
        }

        public static bool IndentMatters(string lang) { lang = Syntax.Norm(lang); return lang == "python" || lang == "yaml"; }
        public static int IndentOf(string line, int width) { int n = 0; while (n < line.Length && line[n] == ' ') n++; return width > 0 ? n / width : 0; }

        // «Собери код»: строки игрока (текст без отступа и уровень отступа) против эталона и допустимых порядков
        public static bool Parsons(TaskData t, IList<KeyValuePair<string, int>> sol, out string note)
        {
            note = null;
            int w = Syntax.IndentWidth(t.language); bool ind = IndentMatters(t.language);
            var cands = new List<string[]> { t.lines ?? new string[0] };
            if (t.alternatives != null) cands.AddRange(t.alternatives);
            string best = null; int bestGood = -1;
            foreach (var c in cands)
            {
                int good = 0; bool ok = c.Length == sol.Count;
                for (int i = 0; i < Math.Min(c.Length, sol.Count); i++)
                {
                    bool same = c[i].Trim() == sol[i].Key.Trim() && (!ind || IndentOf(c[i], w) == sol[i].Value);
                    if (same && good == i) good++;
                    if (!same) ok = false;
                }
                if (ok) return true;
                if (good > bestGood) { bestGood = good; best = good < c.Length && good < sol.Count ? (c[good].Trim() == sol[good].Key.Trim() ? "отступ" : "строка") : c.Length > sol.Count ? "мало" : "много"; }
            }
            var distr = new HashSet<string>((t.distractors ?? new string[0]).Select(x => x.Trim()));
            if (sol.Any(x => distr.Contains(x.Key.Trim()) && !(t.lines ?? new string[0]).Any(l => l.Trim() == x.Key.Trim()))) { note = "В решении есть лишняя строка — она не нужна в этой программе."; return false; }
            note = best == "мало" ? "Не хватает строк: собраны не все." : best == "много" ? "Строк больше, чем нужно." :
                   best == "отступ" ? "Строка " + (bestGood + 1) + " на месте, но с неверным отступом." :
                   bestGood == 0 ? "Первая строка не та." : "Верно начало (" + bestGood + " стр.), дальше — не то.";
            return false;
        }

        // Ввод первого теста для «Запустить» (JS): первый тест целиком
        public static List<object> FirstTest(TaskData t)
        {
            if (t.testCases == null || t.testCases.Count == 0) return new List<object>();
            return new List<object> { t.testCases[0] };
        }
    }
}
