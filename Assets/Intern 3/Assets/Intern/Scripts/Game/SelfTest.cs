// Самопроверка сборки: Stazher.exe -selftest прогоняет эталонные решения всех задач через проверки игры
// (Python — PyCore, JavaScript — Jint, SQL — SQLite, конфиги — регулярки, выбор — варианты) и пишет отчёт
// в selftest.txt рядом с сохранениями (%USERPROFILE%\AppData\LocalLow\Codezilla Games\Стажёр). Потом выходит.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Intern.Py;

namespace Intern.Game
{
    public class SelfTest : MonoBehaviour
    {
        public static bool Requested { get { return Environment.GetCommandLineArgs().Any(a => a == "-selftest"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            new GameObject("SelfTest").AddComponent<SelfTest>();
        }

        IEnumerator Start()
        {
            yield return null;
            var sb = new StringBuilder();
            int ok = 0, fail = 0;
            var started = DateTime.Now;
            sb.AppendLine("Стажёр " + Application.version + " · самопроверка " + started.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("SQLite: " + (SqlRun.Available ? SqlRun.Library + " " + SqlRun.Version : "НЕТ — " + SqlRun.LoadError));
            var counts = new Dictionary<string, int>();
            foreach (var track in Tracks.All)
            {
                var data = Tracks.Load(track);
                foreach (var tp in data.topics)
                    foreach (var t in tp.tasks)
                    {
                        string mode = t.Mode, err = null;
                        var t0 = DateTime.Now;
                        try { err = Check(t); }
                        catch (Exception e) { err = "исключение: " + e.GetType().Name + ": " + e.Message; }
                        double ms = (DateTime.Now - t0).TotalMilliseconds;
                        if (ms > 3000) sb.AppendLine("медленно " + t.key + " " + t.id + " [" + mode + "]: " + ms.ToString("0") + " мс");
                        Debug.Log("[selftest] " + t.id + " " + mode + " " + ms.ToString("0") + " мс" + (err != null ? " FAIL" : ""));
                        int c; counts.TryGetValue(mode, out c); counts[mode] = c + 1;
                        if (err == null) ok++;
                        else { fail++; sb.AppendLine("FAIL " + t.key + " " + t.id + " [" + mode + "]: " + err); }
                    }
                yield return null;
            }
            foreach (var p in new[] { "backend", "frontend", "devops", "fullstack" })
            {
                var path = Tracks.BuildPath(p);
                var done = new HashSet<string>(); int steps = 0;
                for (var cur = path.Current(done); cur != null && steps < 5000; cur = path.Current(done)) { done.Add(cur.id); steps++; }
                bool pathOk = steps == path.Tasks.Length && path.Complete(done);
                if (!pathOk) fail++;
                sb.AppendLine("путь " + p + ": задач " + path.Tasks.Length + ", пройдено " + steps + (pathOk ? " — ок" : " — ТУПИК"));
            }
            sb.AppendLine("режимы: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value).ToArray()));
            sb.AppendLine("итог: " + ok + " ок, " + fail + " ошибок, " + (DateTime.Now - started).TotalSeconds.ToString("0") + " с");
            string file = Path.Combine(Application.persistentDataPath, "selftest.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Стажёр] Самопроверка: " + ok + " ок, " + fail + " ошибок → " + file);
            Application.Quit(fail == 0 ? 0 : 1);
        }

        static string Check(TaskData t)
        {
            switch (t.Mode)
            {
                case "choice":
                    if (t.answer == null || t.answer.Length == 0 || t.answer.Any(a => a < 0 || a >= t.options.Length)) return "неверные индексы ответа";
                    return TaskChecks.Choice(t, t.answer.ToList()).Correct ? null : "эталонный ответ не принят";
                case "static":
                    {
                        var good = TaskChecks.Static(t.solution, t.testCases);
                        if (good.Count == 0 || good.Any(x => !x.Passed)) return "эталон не прошёл: " + string.Join(" | ", good.Where(x => !x.Passed).Select(x => x.InputsText).ToArray());
                        return TaskChecks.Static(t.starter, t.testCases).All(x => x.Passed) ? "заготовка проходит все требования" : null;
                    }
                case "sql":
                    {
                        SqlResult last;
                        var r = TaskChecks.Sql(t.solution, t.testCases, out last);
                        return r.Count > 0 && r.All(x => x.Passed) ? null : "эталон не прошёл: " + string.Join(" | ", r.Select(x => x.Note).ToArray());
                    }
                case "js":
                    {
                        var r = JsRun.Check(t.solution, t.entry, t.testCases);
                        if (r.AllPassed) return null;
                        JsError e; var cr = TaskChecks.FromJs(r, out e);
                        return "эталон не прошёл: " + string.Join(" | ", cr.Where(x => !x.Passed).Select(x => x.Note).ToArray());
                    }
                default:
                    {
                        List<CheckResult> r;
                        if (string.IsNullOrEmpty(t.entry)) r = PyRun.Check(t.solution, t.tests);
                        else r = PyRun.CheckFunction(t.solution, t.entry, t.testCases.OfType<Dictionary<string, object>>()
                            .Select(d => { object i, x; d.TryGetValue("input", out i); d.TryGetValue("expected", out x); return new FuncTest(i, x); }).ToList());
                        return r.Count > 0 && r.All(x => x.Passed) ? null : "эталон не прошёл: " + string.Join(" | ", r.Where(x => !x.Passed).Select(x => x.Note).ToArray());
                    }
            }
        }
    }
}
