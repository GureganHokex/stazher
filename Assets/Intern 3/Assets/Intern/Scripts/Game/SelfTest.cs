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
            var fd = FirstDaySim();
            foreach (var line in fd) { fail++; sb.AppendLine("FAIL первый день: " + line); }
            sb.AppendLine("первый день и спринт: " + (fd.Count == 0 ? "сценарии прошли" : fd.Count + " ошибок"));
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
            // 2. Второй день, средняя сложность: первый час простоя — предупреждение, потом штрафы, долг, замечания
            {
                var s = new SaveData { version = 3, difficulty = 1, money = 100 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                var moods = new List<LeadMood>(); w.Visit = (m, t) => moods.Add(m);
                w.Advance(180f, true);
                expect(s.money == 40 && s.dayFines == 60 && s.strikes == 0, "простой 3 часа: деньги " + s.money + ", штрафы " + s.dayFines + ", выговоры " + s.strikes);
                expect(moods.Count == 3 && moods[0] == LeadMood.Warn && moods[1] == LeadMood.Fine, "Гена подходил не так: " + string.Join(",", moods.Select(m => m.ToString()).ToArray()));
                w.Advance(120f, true);                                   // 14:00: 10 монет взял, 20 в долг
                expect(s.money == 0 && s.debt == 20 && s.strikes == 0 && !fired, "штраф без денег: деньги " + s.money + ", долг " + s.debt + ", выговоры " + s.strikes);
                int got = w.Earn(50); s.money += got;
                expect(got == 30 && s.debt == 0 && s.dayDebtPaid == 20, "доход не погасил долг: дошло " + got + ", долг " + s.debt);
                string why; expect(w.CanLunch(out why), "обед в 14:00 недоступен: " + why);
                w.StartLunch();                                          // самоволка в первый раз — замечание
                expect(!fired && s.strikes == 0 && s.warnedAwol, "первая самоволка дала выговор");
                w.Advance(60f, false);
                w.Advance(180f, true);                                   // 16:00 штраф 30, 17:00 долг 30, 18:00 долг 60
                expect(s.money == 0 && s.debt == 60 && s.strikes == 0, "после обеда: деньги " + s.money + ", долг " + s.debt + ", выговоры " + s.strikes);
                var r = w.Finish();                                      // первый прогул — замечание
                expect(!r.truancy && r.remark != null && s.strikes == 0 && s.cleanDays == 0, "первый прогул: выговоры " + s.strikes + ", замечание " + r.remark);
                expect(r.debt == 60, "долг в итогах дня " + r.debt);
                w.NextDay();
                w.Advance(60f, true);                                    // 10:00 предупреждение
                expect(s.debt == 60 && s.strikes == 0, "новый день: предупреждение стоило денег или выговор");
                w.Advance(60f, true);                                    // 11:00 долг 90 → выговор, долг списан
                expect(s.strikes == 1 && s.debt == 0 && !fired, "долг 90: выговоры " + s.strikes + ", долг " + s.debt);
                w.Advance(60f, true);                                    // 12:00 долг 30
                w.StartLunch();                                          // вторая самоволка — выговор, 2 из 2 → уволен
                expect(fired && w.IsFired, "вторая самоволка при 1 из 2 выговоров не уволила");
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
                expect(s.dayIdleHours == 1 && s.money == 500, "119 с правок засчитаны как работа или предупреждение стоило денег");
                w.Activity(WorkKind.Edit, 60f); w.Activity(WorkKind.Edit, 61f); w.Advance(60f, true);
                expect(s.dayWorkHours == 1, "121 с правок не засчитаны");
                w.Advance(60f, true);
                expect(s.money == 400, "второй простой не оштрафован на 100 (Middle): " + s.money);
            }
            // 5. Прогул и снятие выговора за 5 чистых дней
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 1000 }; WorkDay.Reset(s); s.day = 5;
                var w = new WorkDay(s, () => 0);
                s.warnedTruancy = true;                                  // замечание уже было
                w.Activity(WorkKind.Run); w.Advance(540f, true);
                var r = w.Finish();
                expect(r.truancy && s.strikes == 1, "повторный прогул не дал выговор");
                s.cleanDays = 4; w.NextDay(); s.strikeToday = false;
                for (int h = 0; h < 9; h++) { w.Activity(WorkKind.Run); if (h < 3) s.dayTasks++; w.Advance(60f, true); }
                r = w.Finish();
                expect(r.strikeRemoved && s.strikes == 0 && !s.warnedTruancy, "пятый чистый день не снял выговор или замечание");
            }
            // 6. Тяжёлая: час на обеде не считается, выговор только когда долг дорос до трёх штрафов
            {
                var s = new SaveData { version = 3, difficulty = 2, money = 0 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                w.Activity(WorkKind.Run); w.Advance(170f, true);           // 9:00–11:50: работа, предупреждение
                expect(!fired && s.debt == 0, "тяжёлая: предупреждение уволило или стоило денег");
                w.Advance(10f, true);                                     // 12:00: долг 30
                w.StartLunch(); w.Advance(60f, false);                    // час на обеде не считается
                expect(s.dayIdleHours == 2 && s.debt == 30 && !fired, "тяжёлая: простои " + s.dayIdleHours + ", долг " + s.debt);
                w.Advance(60f, true);
                expect(!fired && s.debt == 60, "тяжёлая: долг 60 уже уволил");
                w.Advance(60f, true);
                expect(fired, "тяжёлая: долг 90 не дал выговор");
            }
            // 7. Похвала за три рабочих часа подряд
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 0 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); var moods = new List<LeadMood>(); w.Visit = (m, t) => moods.Add(m);
                for (int h = 0; h < 3; h++) { w.Activity(WorkKind.Solved); w.Advance(60f, true); }
                expect(moods.Count == 1 && moods[0] == LeadMood.Praise, "нет похвалы за три часа работы");
            }
            return bad;
        }

        // Обучение первого дня по шагам и спринт на неделю: план, бонус, неполная неделя
        public static List<string> FirstDaySim()
        {
            var bad = new List<string>();
            Action<bool, string> expect = (c, msg) => { if (!c) bad.Add(msg); };
            {
                var s = new SaveData(); WorkDay.Reset(s);
                expect(s.tutorial == -1, "старое сохранение без обучения: шаг " + s.tutorial);
                var o = new Onboarding(s); var seen = new List<Tut>(); o.Entered = t => seen.Add(t);
                o.Begin();
                expect(o.Active && o.Step == Tut.MeetLead, "обучение не началось");
                o.Event("sit"); o.Event("talk");
                expect(o.Step == Tut.RunCode, "после знакомства и посадки шаг " + o.Step);
                o.Event("run"); expect(o.Step == Tut.SolveFirst, "после запуска шаг " + o.Step);
                s.dayTasks = 1; o.Event("solved"); expect(o.Step == Tut.SolveSecond, "после первой задачи шаг " + o.Step);
                s.minute = 600; s.dayTasks = 2; o.Event("solved"); expect(o.Step == Tut.WaitLunch, "после второй задачи до обеда шаг " + o.Step);
                o.Poll(700, 0, false); expect(o.Step == Tut.WaitLunch, "обед наступил раньше 12:00");
                o.Poll(720, 0, false); expect(o.Step == Tut.GoLunch, "в 12:00 шаг " + o.Step);
                o.Event("lunch"); expect(o.Step == Tut.FirstKills, "в городе шаг " + o.Step);
                o.Poll(730, 2, true); expect(o.Step == Tut.FirstKills, "двое выбитых уже засчитаны за троих");
                o.Poll(731, 3, true); expect(o.Step == Tut.BackToOffice, "трое выбитых: шаг " + o.Step);
                o.Event("lunchEnd"); expect(o.Step == Tut.WorkTillEvening, "после обеда шаг " + o.Step);
                o.Event("dayEnd"); expect(!o.Active && s.tutorial == -1, "обучение не закончилось в конце дня");
                // сел за стол раньше разговора — шаг «Сядь за компьютер» пропущен: 11 шагов с «Готово» минус 1
                expect(seen.Count == Onboarding.Count && !seen.Contains(Tut.SitDown), "шагов показано " + seen.Count);
                // ранний обед и пропуск
                var s2 = new SaveData(); var o2 = new Onboarding(s2); o2.Begin(); o2.Event("talk"); o2.Event("sit"); o2.Event("run"); o2.Event("solved");
                o2.Event("lunch"); expect(o2.Step == Tut.FirstKills, "обед до второй задачи: шаг " + o2.Step);
                var s3 = new SaveData(); var o3 = new Onboarding(s3); o3.Begin(); o3.Skip(); expect(!o3.Active, "пропуск обучения не сработал");
            }
            {
                var s = new SaveData(); WorkDay.Reset(s);
                var sp = new WeekSprint(s); var ids = new List<string>(); for (int i = 0; i < 40; i++) ids.Add("t" + i);
                sp.Plan(1, 0, ids);
                expect(sp.Planned && sp.Number == 1 && sp.Goal == 8 && sp.Tasks.Count == 11, "первый спринт: цель " + sp.Goal + ", задач " + sp.Tasks.Count);
                for (int i = 0; i < 8; i++) sp.TaskDone("t" + i);
                var r = sp.Close(0);
                expect(r.success && r.bonus == WeekSprint.BonusFor(8, 0) && !sp.Planned, "ретро первого спринта: " + r.text);
                sp.Plan(6, 1, ids);
                expect(sp.Number == 2 && sp.Goal == 15, "второй спринт Junior: цель " + sp.Goal);
                r = sp.Close(1); expect(!r.success && r.bonus == 0, "невыполненный спринт дал бонус");
                sp.Plan(8, 2, ids);   // среда: осталось 3 дня из 5
                expect(sp.Goal == Mathf.RoundToInt(12 * 3 / 5f), "спринт со среды: цель " + sp.Goal);
                expect(WeekSprint.Monday(6) && WeekSprint.Friday(5) && !WeekSprint.Friday(6), "дни недели");
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
