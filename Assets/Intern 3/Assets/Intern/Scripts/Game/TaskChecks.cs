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

        // Ввод первого теста для «Запустить» (JS): первый тест целиком
        public static List<object> FirstTest(TaskData t)
        {
            if (t.testCases == null || t.testCases.Count == 0) return new List<object>();
            return new List<object> { t.testCases[0] };
        }
    }
}
