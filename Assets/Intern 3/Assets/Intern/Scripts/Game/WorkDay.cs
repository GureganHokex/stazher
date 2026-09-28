// Рабочий день: часы 9:00–18:00, обед, учёт работы по часам, штрафы за простой, выговоры и увольнение.
// Чистая логика над SaveData: GameRoot двигает время и показывает то, что здесь решено.
using System;
using UnityEngine;

namespace Intern.Game
{
    // Длина дня из настроек: сколько реальных минут длится игровой час в офисе и сколько длится обед
    public static class DayLength
    {
        public static readonly string[] Names = { "Короткий", "Обычный", "Длинный" };
        public static readonly string[] About = {
            "3 минуты за игровой час, обед 5 минут: день около 29 минут.",
            "5 минут за игровой час, обед 6 минут: день около 46 минут.",
            "7 минут за игровой час, обед 7 минут: день около 63 минут." };
        static readonly float[] OfficeMinPerHour = { 3f, 5f, 7f };
        static readonly float[] LunchMinutes = { 5f, 6f, 7f };

        static int I(int i) { return Mathf.Clamp(i, 0, 2); }
        // Реальных секунд на одну игровую минуту в офисе
        public static float SecondsPerGameMinute(int i) { return OfficeMinPerHour[I(i)]; }
        // Длина обеда в реальных секундах (за это время в игре проходит 60 минут)
        public static float LunchSeconds(int i) { return LunchMinutes[I(i)] * 60f; }
    }

    public enum WorkKind { Edit, Run, Check, Solved, Theory, Hint, Terminal }

    // С каким настроением Гена подходит к столу
    public enum LeadMood { Warn, Fine, Strike, Praise, Info }

    public class DayReport
    {
        public int day, tasks, xp, money, lunchMoney, kills, fines, workHours, idleHours, strikes, limit, debt, debtPaid;
        public bool strikeToday, strikeRemoved, truancy;
        public string weekday, remark;   // remark — замечание вместо выговора (первый прогул)
        public string retro;             // ретро спринта (пятница)
        public bool retroSuccess, tutorialDone;
    }

    public class WorkDay
    {
        public const int Start = 540, LunchOpen = 720, LunchNudge = 780, LunchClose = 960, End = 1080;
        public const float EditSecondsForHour = 120f;   // 2 реальные минуты правок в IDE — рабочий час
        public const int SatedMinutes = 120;            // бонус «Сытый» после обеда

        readonly SaveData s;
        readonly Func<int> gradeIdx;
        public WorkDay(SaveData save, Func<int> grade) { s = save; gradeIdx = grade; }

        // ---- что показать игроку ----
        public Action<string> Lead;          // реплика Гены издалека (уведомление)
        public Action<LeadMood, string> Visit; // Гена подходит к столу и говорит лично
        public Action<string> Notice;        // просто сообщение
        public Action Fired;                 // уволен
        public Action DayOver;               // 18:00

        public static readonly string[] Weekdays = { "Пн", "Вт", "Ср", "Чт", "Пт" };
        public static readonly string[] WeekdaysFull = { "Понедельник", "Вторник", "Среда", "Четверг", "Пятница" };
        public static string WeekdayOf(int day) { return Weekdays[((day - 1) % 5 + 5) % 5]; }
        public static string TimeText(float minute) { int m = Mathf.FloorToInt(minute); return (m / 60) + ":" + (m % 60).ToString("00"); }

        public string Weekday { get { return WeekdayOf(s.day); } }
        public string WeekdayFull { get { return WeekdaysFull[((s.day - 1) % 5 + 5) % 5]; } }
        public string Clock { get { return Weekday + " " + TimeText(s.minute); } }
        public float Minute { get { return s.minute; } }
        public bool FirstDay { get { return s.day <= 1; } }
        public bool Ended { get { return s.minute >= End - 0.001f; } }
        public bool LunchOpenNow { get { return !s.lunchTaken && s.minute >= LunchOpen && s.minute < LunchClose && !Ended; } }
        public bool Sated { get { return s.minute < s.satedUntil; } }
        public int Strikes { get { return s.strikes; } }
        public int StrikeLimit { get { return FireLimit((Difficulty)s.difficulty); } }
        public static int FireLimit(Difficulty d) { return d == Difficulty.Easy ? 3 : d == Difficulty.Medium ? 2 : 1; }
        public int FineAmount { get { int g = gradeIdx(); return g <= 0 ? 30 : g == 1 ? 50 : g == 2 ? 75 : 100; } }
        public int Debt { get { return s.debt; } }
        // Долг, при котором Гена всё-таки даёт выговор: три неоплаченных штрафа
        public int DebtLimit { get { return FineAmount * 3; } }

        bool fired, dayOverSent;
        public bool IsFired { get { return fired; } }

        // ================= время =================
        // Сдвинуть время. office = true — время в офисе (идёт в учёт часа), false — на обеде
        public void Advance(float gameMinutes, bool office)
        {
            if (fired || gameMinutes <= 0f) return;
            float before = s.minute;
            float target = Mathf.Min(s.minute + gameMinutes, End);
            while (s.minute < target && !fired)
            {
                float hourEnd = s.hourStart + 60;
                float step = Mathf.Min(target, hourEnd) - s.minute;
                if (office) s.hourMinutes += step;
                s.minute += step;
                if (s.minute >= hourEnd - 0.0001f) CloseHour();
            }
            Marks(before, s.minute);
            if (Ended && !dayOverSent) { dayOverSent = true; if (DayOver != null) DayOver(); }
        }

        void Marks(float a, float b)
        {
            if (Cross(a, b, LunchOpen) && !s.lunchTaken) Say(Notice, "12:00 — дверь на обед открыта до 16:00.");
            if (Cross(a, b, LunchNudge) && !s.lunchTaken) Say(Lead, "Уже час дня. Сходи поешь, голодный разработчик пишет баги.");
            if (Cross(a, b, LunchClose) && !s.lunchTaken) Say(Notice, "16:00 — обеденное время закончилось. Сегодня без обеда.");
            if (Cross(a, b, End - 30)) Say(Notice, "17:30 — до конца рабочего дня полчаса.");
        }
        static bool Cross(float a, float b, float mark) { return a < mark && b >= mark; }

        // Закрыть учётный час: работал или простаивал
        void CloseHour()
        {
            bool counted = s.hourMinutes >= 30f;   // час, почти целиком прошедший на обеде, не считается
            if (counted)
            {
                if (s.hourWorked)
                {
                    s.dayWorkHours++; s.workStreak++;
                    if (!FirstDay && s.workStreak == 3) Praise();
                }
                else
                {
                    s.dayIdleHours++; s.workStreak = 0;
                    if (!FirstDay) IdleFine();
                }
            }
            s.hourStart += 60; s.hourMinutes = 0f; s.hourEditSec = 0f; s.hourWorked = false;
        }

        static readonly string[] PraiseLines = {
            "Три часа подряд в работе — вот это я понимаю. Так держать!",
            "Смотрю, ты в потоке. Не отвлекаю, продолжай.",
            "Хороший темп. На планёрке скажу, что стажёр тащит.",
        };

        void Praise()
        {
            Tell(LeadMood.Praise, PraiseLines[(s.day + s.hourStart / 60) % PraiseLines.Length]);
        }

        // Час простоя. Первый за день — предупреждение, дальше штраф.
        // Не хватает денег — остаток уходит в долг; выговор только когда долг дорос до трёх штрафов.
        void IdleFine()
        {
            int fine = FineAmount;
            if (!s.idleWarnedToday)
            {
                s.idleWarnedToday = true;
                Tell(LeadMood.Warn, "Эй, целый час без работы. Сегодня первый раз — просто предупреждаю. Следующий час простоя — минус " + fine + " с премии.");
                return;
            }
            int paid = Mathf.Min(s.money, fine), owe = fine - paid;
            s.money -= paid; s.dayFines += fine; s.totalFines += fine;
            if (owe <= 0)
            {
                Tell(LeadMood.Fine, "Опять час без работы. Минус " + fine + " с премии.");
                return;
            }
            s.debt += owe;
            if (s.debt >= DebtLimit)
            {
                s.debt -= DebtLimit;
                AddStrike("долг за простой дорос до " + DebtLimit + " монет. Долг списываю, но выговор пишу");
                return;
            }
            Tell(LeadMood.Fine, "Час без работы — штраф " + fine + ". " + (paid > 0 ? "Взял " + paid + ", остальное " : "Денег нет, ") +
                                "записал в долг. Вернёшь с премии. Долг " + s.debt + " из " + DebtLimit + ", дальше — выговор.");
        }

        // Доход (задачи, обед, баги): сначала гасит долг. Возвращает, сколько дошло до кошелька
        public int Earn(int amount)
        {
            if (amount <= 0 || s.debt <= 0) return amount;
            int pay = Mathf.Min(s.debt, amount);
            s.debt -= pay; s.dayDebtPaid += pay;
            if (s.debt == 0) Say(Notice, "Долг Гене погашен.");
            return amount - pay;
        }

        // ================= работа =================
        public void Activity(WorkKind kind, float amount = 0f)
        {
            if (fired) return;
            if (kind == WorkKind.Edit)
            {
                s.hourEditSec += amount;
                if (s.hourEditSec >= EditSecondsForHour) { s.hourWorked = true; s.workedToday = true; }
                return;
            }
            s.hourWorked = true; s.workedToday = true;
        }

        // ================= обед =================
        // Можно ли идти на обед сейчас. reason — что сказать, если нельзя
        public bool CanLunch(out string reason)
        {
            reason = null;
            if (Ended) reason = "Рабочий день окончен.";
            else if (s.lunchTaken) reason = "Обед сегодня уже был. Работаем до 18:00.";
            else if (s.minute < LunchOpen) reason = "На обед можно с 12:00. Сейчас " + TimeText(s.minute) + ".";
            else if (s.minute >= LunchClose) reason = "Обеденное время прошло: выйти можно было до 16:00.";
            return reason == null;
        }

        public void StartLunch()
        {
            s.lunchTaken = true; s.totalLunches++;
            if (FirstDay || s.workedToday) return;
            if (!s.warnedAwol)
            {
                s.warnedAwol = true; s.dayRemarks++;
                Say(Lead, "Ты куда? С утра ни одной строчки — и сразу на обед? Первый раз — замечание. Второй — выговор.");
            }
            else AddStrike("самоволка: ушёл на обед, не поработав с утра");
        }

        public void EndLunch(int coins, int kills)
        {
            s.dayLunchMoney += coins; s.dayKills += kills; s.totalKills += kills;
            s.satedUntil = s.minute + SatedMinutes;
        }

        // ================= выговоры =================
        public void AddStrike(string reason)
        {
            if (fired) return;
            s.strikes++; s.strikeToday = true; s.cleanDays = 0;
            int limit = StrikeLimit;
            if (s.strikes >= limit) { fired = true; if (Fired != null) Fired(); return; }
            Tell(LeadMood.Strike, "Выговор: " + reason + ". Это " + s.strikes + " из " + limit + "." +
                      (s.strikes == limit - 1 ? " Ещё один — и пишешь заявление." : " Пять чистых дней — и сниму."));
        }

        // ================= конец дня =================
        // Итоги дня: прогул, снятие выговора. Вызывать один раз, до NextDay
        public DayReport Finish()
        {
            var r = new DayReport { day = s.day, weekday = WeekdayFull };
            bool truancy = !FirstDay && (s.dayTasks == 0 || s.dayWorkHours < 2);
            if (truancy)
            {
                string why = s.dayTasks == 0 ? "за день ни одной решённой задачи" : "меньше двух рабочих часов за день";
                if (!s.warnedTruancy) { s.warnedTruancy = true; s.dayRemarks++; r.remark = "Замечание: " + why + ". Первый раз прощаю, в следующий — выговор."; }
                else { r.truancy = true; AddStrike("прогул: " + why); }
            }
            if (!fired)
            {
                if (!s.strikeToday && s.dayRemarks == 0)
                {
                    s.cleanDays++;
                    if (s.cleanDays >= 5)
                    {
                        // пять чистых дней: минус выговор и снова право на замечание
                        if (s.strikes > 0) { s.strikes--; r.strikeRemoved = true; }
                        s.warnedTruancy = s.warnedAwol = false;
                        s.cleanDays = 0;
                    }
                }
                else if (s.dayRemarks > 0) s.cleanDays = 0;
            }
            r.tasks = s.dayTasks; r.xp = s.dayXp; r.money = s.dayMoney; r.lunchMoney = s.dayLunchMoney; r.kills = s.dayKills;
            r.fines = s.dayFines; r.workHours = s.dayWorkHours; r.idleHours = s.dayIdleHours;
            r.strikes = s.strikes; r.limit = StrikeLimit; r.strikeToday = s.strikeToday; r.debt = s.debt; r.debtPaid = s.dayDebtPaid;
            return r;
        }

        public void NextDay()
        {
            s.day++; s.minute = Start; s.lunchTaken = false; s.satedUntil = 0f;
            s.hourStart = Start; s.hourMinutes = 0f; s.hourEditSec = 0f; s.hourWorked = false;
            s.strikeToday = false; s.workedToday = false; s.idleWarnedToday = false; s.workStreak = 0;
            s.dayTasks = s.dayXp = s.dayMoney = s.dayLunchMoney = s.dayKills = s.dayFines = s.dayWorkHours = s.dayIdleHours = 0;
            s.dayDebtPaid = s.dayRemarks = 0;
            dayOverSent = false;
        }

        // Свежий день для старых сохранений (до версии 3)
        public static void Reset(SaveData s)
        {
            s.day = 1; s.minute = Start; s.lunchTaken = false; s.satedUntil = 0f; s.strikes = 0; s.cleanDays = 0;
            s.strikeToday = false; s.workedToday = false; s.hourStart = Start; s.hourMinutes = 0f; s.hourEditSec = 0f; s.hourWorked = false;
            s.dayTasks = s.dayXp = s.dayMoney = s.dayLunchMoney = s.dayKills = s.dayFines = s.dayWorkHours = s.dayIdleHours = 0;
            s.debt = 0; s.idleWarnedToday = false; s.warnedTruancy = s.warnedAwol = false; s.workStreak = 0; s.dayDebtPaid = s.dayRemarks = 0;
        }

        // Гена говорит лично (подходит к столу), если это можно показать, иначе — уведомлением
        void Tell(LeadMood mood, string text)
        {
            if (Visit != null) Visit(mood, text); else Say(Lead, text);
        }

        static void Say(Action<string> a, string text) { if (a != null) a(text); }
    }
}
