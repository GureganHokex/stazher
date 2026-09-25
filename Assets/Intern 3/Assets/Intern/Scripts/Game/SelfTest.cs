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
            var wd = WorkdaySim();
            foreach (var line in wd) { fail++; sb.AppendLine("FAIL рабочий день: " + line); }
            sb.AppendLine("рабочий день: " + (wd.Count == 0 ? "сценарии прошли" : wd.Count + " ошибок"));
            sb.AppendLine("режимы: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value).ToArray()));
            sb.AppendLine("итог: " + ok + " ок, " + fail + " ошибок, " + (DateTime.Now - started).TotalSeconds.ToString("0") + " с");
            string file = Path.Combine(Application.persistentDataPath, "selftest.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Стажёр] Самопроверка: " + ok + " ок, " + fail + " ошибок → " + file);
            Application.Quit(fail == 0 ? 0 : 1);
        }

        // Ускоренный рабочий день: часы, штрафы за простой, выговоры, самоволка, прогул, увольнение
        public static List<string> WorkdaySim()
        {
            var bad = new List<string>();
            Action<bool, string> expect = (c, msg) => { if (!c) bad.Add(msg); };

            // 1. Первый день без работы: без штрафов и выговоров, 18:00 наступает один раз
            {
                var s = new SaveData { version = 3, difficulty = 1, money = 100 }; WorkDay.Reset(s);
                var w = new WorkDay(s, () => 0); int over = 0; bool fired = false;
                w.DayOver = () => over++; w.Fired = () => fired = true;
                for (int i = 0; i < 600; i++) w.Advance(1f, true);
                var r = w.Finish();
                expect(over == 1, "первый день: 18:00 наступило " + over + " раз");
                expect(s.money == 100 && s.strikes == 0 && !fired && !r.truancy, "первый день: штрафы или выговоры (" + s.money + ", " + s.strikes + ")");
                expect(r.idleHours == 9 && r.workHours == 0, "первый день: часы учёта " + r.workHours + "/" + r.idleHours);
                expect(w.Clock == "Пн 18:00", "первый день: часы показывают " + w.Clock);
            }
            // 2. Второй день, средняя сложность: простой до обеда, штрафы, неоплаченный штраф и самоволка → увольнение
            {
                var s = new SaveData { version = 3, difficulty = 1, money = 100 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                w.Advance(180f, true);
                expect(s.money == 10 && s.dayFines == 90, "простой 3 часа: деньги " + s.money + ", штрафы " + s.dayFines);
                w.Advance(60f, true);
                expect(s.strikes == 1 && !fired, "неоплаченный штраф: выговоров " + s.strikes);
                string why; expect(w.CanLunch(out why), "обед в 13:00 недоступен: " + why);
                w.StartLunch();
                expect(fired && w.IsFired, "самоволка при 1 из 2 выговоров не уволила");
            }
            // 3. Честный день: работа каждый час, обед, задачи → без штрафов, чистый день
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 0 }; WorkDay.Reset(s); s.day = 3;
                var w = new WorkDay(s, () => 1);
                for (int h = 0; h < 3; h++) { w.Activity(WorkKind.Run); w.Advance(60f, true); }
                string why; expect(w.CanLunch(out why), "обед в 12:00: " + why);
                w.StartLunch(); for (int i = 0; i < 60; i++) w.Advance(1f, false); w.EndLunch(120, 12);
                expect(w.Sated, "после обеда нет бонуса «Сытый»");
                expect(!w.CanLunch(out why), "второй обед за день разрешён");
                for (int h = 0; h < 5; h++) { w.Activity(WorkKind.Check); s.dayTasks++; w.Advance(60f, true); }
                var r = w.Finish();
                expect(r.fines == 0 && s.strikes == 0 && !r.truancy, "честный день: штрафы " + r.fines + ", выговоры " + s.strikes);
                expect(r.lunchMoney == 120 && r.kills == 12 && s.cleanDays == 1, "честный день: обед " + r.lunchMoney + "/" + r.kills + ", чистых " + s.cleanDays);
                w.NextDay();
                expect(s.day == 4 && s.minute == WorkDay.Start && !s.lunchTaken && w.Clock == "Чт 9:00", "следующий день: " + w.Clock);
            }
            // 4. Правки в IDE: 2 минуты правок = рабочий час
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 500 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 3);
                w.Activity(WorkKind.Edit, 119f); w.Advance(60f, true);
                expect(s.dayIdleHours == 1 && s.money == 400, "119 с правок засчитаны как работа (штраф Middle 100)");
                w.Activity(WorkKind.Edit, 60f); w.Activity(WorkKind.Edit, 61f); w.Advance(60f, true);
                expect(s.dayWorkHours == 1, "121 с правок не засчитаны");
            }
            // 5. Прогул и снятие выговора за 5 чистых дней
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 1000 }; WorkDay.Reset(s); s.day = 5;
                var w = new WorkDay(s, () => 0);
                w.Activity(WorkKind.Run); w.Advance(540f, true);
                var r = w.Finish();
                expect(r.truancy && s.strikes == 1, "день без задач не посчитан прогулом");
                s.cleanDays = 4; w.NextDay(); s.strikeToday = false;
                for (int h = 0; h < 9; h++) { w.Activity(WorkKind.Run); if (h < 3) s.dayTasks++; w.Advance(60f, true); }
                r = w.Finish();
                expect(r.strikeRemoved && s.strikes == 0, "пятый чистый день не снял выговор");
            }
            // 6. Час, почти целиком прошедший на обеде, не считается
            {
                var s = new SaveData { version = 3, difficulty = 2, money = 0 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                w.Activity(WorkKind.Run); w.Advance(170f, true);           // 9:00–11:50, работа только в первый час
                expect(fired, "тяжёлая: неоплаченный штраф за простой не уволил сразу");
            }
            return bad;
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
