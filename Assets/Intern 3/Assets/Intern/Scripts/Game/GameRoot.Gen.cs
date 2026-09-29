// GameRoot, спринт 5 «Без потолка»: уровень и титулы после Middle, тикеты дня и тренировки из генератора задач,
// звёзды тем и смена компании. Генератор — TaskGen, уровни — Levels.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Intern.Game
{
    public partial class GameRoot
    {
        public const int StarClean = TopicStars.Clean, StarStreak = TopicStars.Streak;

        readonly Dictionary<string, TaskData> genCache = new Dictionary<string, TaskData>();
        Dictionary<string, TaskData> pathById;

        // Версия 6: списки генератора (в старых сохранениях их нет)
        void MigrateGen()
        {
            if (Save.daily == null) Save.daily = new List<string>();
            if (Save.genDone == null) Save.genDone = new List<string>();
            if (Save.practice == null) Save.practice = new List<string>();
            if (Save.topicStats == null) Save.topicStats = new List<TopicStat>();
            if (Save.version >= 6) return;
            Save.version = 6;
            if (Progress.HasSave()) Progress.Save(Save);
        }

        void ResetGenCache() { pathById = null; genCache.Clear(); }

        // ================== уровень, титул, компания ==================
        public int Level { get { return Levels.Of(Save.xp); } }
        public string CompanyName { get { return Levels.CompanyName(Save.company); } }
        public float TitleBonus { get { return PathComplete ? Levels.TitleBonus(Level) : 0f; } }
        public float CoinMult { get { return 1f + TitleBonus + Levels.CompanyBonus * Save.company; } }
        public bool CanChangeCompany { get { return PathComplete && Level >= Levels.CompanyLevel; } }

        public string NextTitleLine()
        {
            int at; string name = Levels.NextTitle(Level, out at);
            return "До " + name + " — уровень " + at + ".";
        }

        // Награда за задачу: монеты (сложность, срок, решение, титул, компания) и опыт (сытый +10%)
        void Pay(TaskData t, bool usedSolution, bool late, out int xp, out int reward, out int got, out bool sated)
        {
            float mult = 1f;
            var diff = (Difficulty)Save.difficulty;
            if (diff == Difficulty.Medium) mult *= 1.2f;
            if (diff == Difficulty.Hard) mult *= late ? 0.5f : 1.6f;
            else if (late) mult *= 0.75f;   // задача с таймером (инцидент) сдана после срока
            if (usedSolution) mult *= 0.5f;
            mult *= CoinMult;
            reward = Mathf.Max(1, Mathf.RoundToInt(t.reward * mult));
            xp = usedSolution ? t.xp / 2 : t.xp;
            sated = Work != null && Work.Sated;
            if (sated) xp = Mathf.RoundToInt(xp * 1.1f);
            got = Work != null ? Work.Earn(reward) : reward;
            Save.money += got; Save.xp += xp;
        }

        // После начисления опыта: новый уровень, новый титул
        void AfterXp(int oldLevel, string oldRank)
        {
            int lv = Level;
            if (lv > oldLevel) Toast("Новый уровень: " + lv + "!" + (PathComplete ? " " + NextTitleLine() : ""));
            if (PathComplete && RankName != oldRank && Levels.TitleIndex(lv) >= 0)
            {
                int pct = Mathf.RoundToInt(TitleBonus * 100f);
                Toast("НОВЫЙ ТИТУЛ: " + RankName + "! Монеты за задачи +" + pct + "%.");
                if (leadWalker != null && lunch == null && (mode == Mode.Walk || mode == Mode.Ide))
                    LeadVisit(LeadMood.Praise, "Поздравляю, теперь ты " + RankName + "! Премия к задачам — плюс " + pct + "%.");
            }
        }

        // ================== задачи из генератора ==================
        TaskData PathTask(string id)
        {
            if (pathById == null)
            {
                pathById = new Dictionary<string, TaskData>();
                foreach (var t in Path.Tasks) pathById[t.id] = t;
            }
            TaskData r; return id != null && pathById.TryGetValue(id, out r) ? r : null;
        }

        public TaskData GenTask(string spec)
        {
            TaskData t;
            if (genCache.TryGetValue(spec, out t)) return t;
            t = TaskGen.Build(spec, PathTask, Level);
            genCache[spec] = t;   // null тоже запоминаем: исходной задачи нет в пути
            return t;
        }

        public bool GenDone(TaskData t) { return t != null && t.generated && Save.genDone.Contains(t.id); }

        public List<TaskData> DailyTickets
        {
            get
            {
                var l = new List<TaskData>();
                foreach (var s in Save.daily) { var t = GenTask(s); if (t != null) l.Add(t); }
                return l;
            }
        }

        public int DailyDone { get { int n = 0; foreach (var s in Save.daily) if (Save.genDone.Contains(s) && GenTask(s) != null) n++; return n; } }
        public int DailyTotal { get { int n = 0; foreach (var s in Save.daily) if (GenTask(s) != null) n++; return n; } }

        TaskData NextDaily()
        {
            foreach (var s in Save.daily)
                if (!Save.genDone.Contains(s)) { var t = GenTask(s); if (t != null) return t; }
            return null;
        }

        void ForgetCode(string id)
        {
            int i = Save.codeIds.IndexOf(id);
            if (i >= 0) { Save.codeIds.RemoveAt(i); if (i < Save.codeTexts.Count) Save.codeTexts.RemoveAt(i); }
        }

        // Тикеты дня: после конца пути каждое утро — новые 6–8 задач из генератора
        void EnsureDaily(bool force = false)
        {
            if (!PathComplete) return;
            if (!force && Save.dailyDay == Save.day && Save.daily.Count > 0) return;
            var old = Save.daily;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Save.daily = TaskGen.Daily(Path.Tasks, Save.day, Profession + "#" + Save.company, PathTask, -1, Level);
            Save.dailyDay = Save.day;
            Debug.Log("[Стажёр] Тикеты дня " + Save.day + ": " + Save.daily.Count + " шт., " + sw.ElapsedMilliseconds + " мс");
            // вчерашний тикет открыт в IDE — переключаем на сегодняшний (до того, как забудем старый код)
            var open = ideUi != null ? ideUi.Task : null;
            if (open != null && open.generated && !Save.daily.Contains(open.id) && !Save.practice.Contains(open.id))
            {
                var cur = CurrentTask;
                if (cur != null) ideUi.Open(cur);
            }
            foreach (var s in old) if (!Save.daily.Contains(s)) { Save.genDone.Remove(s); ForgetCode(s); genCache.Remove(s); }
            Persist();
        }

        // ================== тренировки и звёзды тем ==================
        public bool TopicTrainable(Topic tp) { return tp != null && tp.tasks.Any(TaskGen.CanGenerate); }

        public List<TaskData> PracticeIn(Topic tp)
        {
            var l = new List<TaskData>();
            if (tp == null) return l;
            foreach (var s in Save.practice) { var t = GenTask(s); if (t != null && t.topic == tp.id) l.Add(t); }
            return l;
        }

        public TaskData NewPractice(Topic tp)
        {
            if (tp == null || !TrackPath.TopicDone(tp, Done) || !TopicTrainable(tp)) return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Save.practiceNo++;
            var spec = TaskGen.Practice(tp.tasks, Save.practiceNo, PathTask, Level);
            if (spec == null) return null;
            Save.practice.Add(spec);
            while (Save.practice.Count > 8)
            {
                var old = Save.practice[0]; Save.practice.RemoveAt(0);
                Save.genDone.Remove(old); ForgetCode(old); genCache.Remove(old);
            }
            Persist();
            Debug.Log("[Стажёр] Тренировка " + spec + ": " + sw.ElapsedMilliseconds + " мс");
            return GenTask(spec);
        }

        public TopicStat StatOf(string topicId, bool create)
        {
            foreach (var st in Save.topicStats) if (st.id == topicId) return st;
            if (!create) return null;
            var n = new TopicStat { id = topicId };
            Save.topicStats.Add(n);
            return n;
        }

        // 0 — тема не сдана; ★ — сдана; ★★ — 5 задач из генератора без подсказок; ★★★ — ещё и 10 подряд вовремя
        public int StarsOf(Topic tp)
        {
            if (tp == null || tp.tasks.Count == 0) return 0;
            return TopicStars.Of(StatOf(tp.id, false), TrackPath.TopicDone(tp, Done));
        }

        Topic TopicOfAny(TaskData t)
        {
            var tp = Path.TopicOf(t);
            if (tp == null && t != null && t.topic != null) Path.TopicById.TryGetValue(t.topic, out tp);
            return tp;
        }

        void CompleteGenerated(TaskData t, bool usedSolution, bool late, bool usedHints, float spent)
        {
            if (Save.genDone.Contains(t.id)) { Toast("Эта задача уже сдана. Повторить — всегда полезно!"); return; }
            int oldLevel = Level; string oldRank = RankName;
            int xp, reward, got; bool sated;
            Pay(t, usedSolution, late, out xp, out reward, out got, out sated);
            Save.genDone.Add(t.id); Save.genSolved++;
            Save.dayTasks++; Save.dayXp += xp; Save.dayMoney += reward;
            // звёзды темы
            var tp = TopicOfAny(t);
            string starNote = null;
            if (tp != null)
            {
                bool onTime = !usedSolution && (spent < 0f || spent <= t.deadline);
                int stars = TopicStars.Record(StatOf(tp.id, true), !usedHints && !usedSolution, onTime);
                if (stars > 0 && TrackPath.TopicDone(tp, Done))
                {
                    int bonus = TopicStars.BonusFor(stars), sxp = Levels.StarXp(stars, Level);
                    Save.money += Work != null ? Work.Earn(bonus) : bonus;
                    Save.xp += sxp; Save.dayXp += sxp;
                    starNote = new string('★', stars) + " Тема «" + tp.title + "»: " + (stars == 2 ? StarClean + " задач без подсказок" : StarStreak + " задач подряд вовремя") + ". Бонус +" + sxp + " XP и +" + bonus + " монет!";
                }
            }
            if (Work != null) Work.Activity(WorkKind.Solved);
            if (Sprint != null) Sprint.TaskDone(t.id);
            Persist(); UpdateBoard();
            Toast((t.genKind == "ask" ? "Верно! " : "Баг исправлен! ") + "+" + xp + " XP" + (sated ? " (сытый +10%)" : "") + ", +" + reward + " монет" +
                  (got < reward ? ", из них " + (reward - got) + " в счёт долга Гене" : ""));
            if (player != null && player.avatar != null) player.avatar.React(2, 3f);
            if (starNote != null) Toast(starNote);
            if (Save.daily.Contains(t.id))
            {
                int left = DailyTotal - DailyDone;
                if (left == 0 && Save.dailyBonusDay != Save.day)
                {
                    // все тикеты дня закрыты — премия опытом и монетами (раз в день)
                    Save.dailyBonusDay = Save.day;
                    int bxp = Levels.DayBonus(oldLevel), bcoins = bxp * Levels.TicketCoinsPerXp / 2;
                    Save.xp += bxp; Save.dayXp += bxp;
                    Save.money += Work != null ? Work.Earn(bcoins) : bcoins; Save.dayMoney += bcoins;
                    Persist();
                    Toast("Все тикеты дня закрыты! Премия: +" + bxp + " XP и +" + bcoins + " монет. Дальше — тренировки по темам в проводнике IDE.");
                    if (leadWalker != null && lunch == null && (mode == Mode.Walk || mode == Mode.Ide)) LeadVisit(LeadMood.Praise, "Все тикеты дня закрыты — отличная работа! Премию уже начислил.");
                }
                else if (left > 0) { var next = NextDaily(); if (next != null) Toast("Следующий тикет: " + next.key + " " + next.title + " (осталось " + left + ")."); }
            }
            AfterXp(oldLevel, oldRank);
        }

#if UNITY_EDITOR
        // Отладка в редакторе, F9 в офисе. Первое нажатие — весь путь сдан (копия сейва — PlayerPrefs intern_save_f9_backup),
        // дальше — опыт до следующего титула без 10 XP (одна задача — и новый титул)
        void DebugEndless()
        {
            if (!PathComplete)
            {
                PlayerPrefs.SetString("intern_save_f9_backup", JsonUtility.ToJson(Save)); PlayerPrefs.Save();
                foreach (var t in Path.Tasks) if (!Save.done.Contains(t.id)) Save.done.Add(t.id);
                Save.xp = Mathf.Max(Save.xp, Levels.TotalFor(29) + 100);
                EnsureDaily(true);
                var cur = CurrentTask; if (ideUi != null && cur != null) ideUi.Open(cur);
                Toast("Отладка F9: путь пройден, уровень " + Level + ", тикетов дня " + Save.daily.Count + ".");
            }
            else
            {
                int at; string name = Levels.NextTitle(Level, out at);
                if (Level < at - 1) { Save.xp = Mathf.Max(Save.xp, Levels.TotalFor(at) - 10); Toast("Отладка F9: уровень " + Level + ", до " + name + " — 10 XP."); }
                else { int ol = Level; string orank = RankName; Save.xp = Levels.TotalFor(at); AfterXp(ol, orank); }   // уже у порога — сразу титул
            }
            Persist(); UpdateBoard();
            Debug.Log("[Стажёр] F9: уровень " + Level + " (" + RankName + "), XP " + Save.xp + ", тикеты дня: " + string.Join(" ", Save.daily.ToArray()));
        }
#endif

        // ================== смена компании ==================
        void CompanyTalk()
        {
            string next = Levels.CompanyName(Save.company + 1);
            OpenDialog("Тимлид Гена",
                "Слышал, тебя зовут в «" + next + "». Жалко отпускать, но понимаю: тут ты уже " + RankName + ".\n\n" +
                "• На новом месте начнёшь с Junior: темы направления придётся пройти заново (общая база для стажёров останется засчитанной).\n" +
                "• Уровень, опыт, монеты, оружие и гардероб остаются с тобой.\n" +
                "• Оклад выше: +" + Mathf.RoundToInt(Levels.CompanyBonus * 100f * (Save.company + 1)) + "% к монетам за задачи навсегда.",
                Btn("Перейти в «" + next + "»", () => { CloseDialog(); ChangeCompany(); }),
                Btn("~Остаюсь", CloseDialog));
        }

        public void ChangeCompany()
        {
            if (!CanChangeCompany) return;
            Levels.ResetForCompany(Save, Path);
            genCache.Clear();
            Persist();
            PlanSprint();
            if (ideUi != null) ideUi.ResetProgress(CurrentTask);
            UpdateBoard();
            Toast("Добро пожаловать в «" + CompanyName + "»! Ты снова " + RankName + ", уровень " + Level + " остался. Монеты за задачи: +" + Mathf.RoundToInt(Levels.CompanyBonus * 100f * Save.company) + "%.");
        }
    }
}
