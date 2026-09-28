// Первый день (спринт 4): обучение по шагам с Геной и спринты по неделе на доске задач.
// Здесь только логика над SaveData — GameRoot показывает цели, маркеры и реплики.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public enum Tut { MeetLead, SitDown, RunCode, SolveFirst, SolveSecond, WaitLunch, GoLunch, FirstKills, BackToOffice, WorkTillEvening, Done }

    // ======================= обучение первого дня =======================
    public class Onboarding
    {
        readonly SaveData s;
        public Onboarding(SaveData save) { s = save; }

        public Action<Tut> Entered;          // начался шаг — показать реплику Гены
        public bool Active { get { return s.tutorial >= 0 && s.tutorial < (int)Tut.Done; } }
        public Tut Step { get { return Active ? (Tut)s.tutorial : Tut.Done; } }
        public int Index { get { return Active ? s.tutorial + 1 : (int)Tut.Done; } }
        public static int Count { get { return (int)Tut.Done; } }

        public void Begin() { s.tutorial = 0; Enter(Tut.MeetLead); }
        public void Skip() { s.tutorial = -1; }

        void Enter(Tut t)
        {
            s.tutorial = t == Tut.Done ? -1 : (int)t;
            if (Entered != null) Entered(t);
        }

        // Что произошло в игре: talk, sit, run, solved, lunch, lunchEnd, dayEnd
        public void Event(string e)
        {
            if (!Active) return;
            var st = Step;
            switch (e)
            {
                case "talk": if (st == Tut.MeetLead) Enter(Tut.SitDown); break;
                case "sit": if (st <= Tut.SitDown) Enter(Tut.RunCode); break;
                case "run": if (st == Tut.RunCode) Enter(Tut.SolveFirst); break;
                case "solved":
                    if (st <= Tut.SolveFirst) Enter(Tut.SolveSecond);
                    else if (st == Tut.SolveSecond && s.dayTasks >= 2) Enter(s.minute >= WorkDay.LunchOpen ? Tut.GoLunch : Tut.WaitLunch);
                    break;
                case "lunch": if (st < Tut.FirstKills) Enter(Tut.FirstKills); break;
                case "lunchEnd": if (st == Tut.FirstKills || st == Tut.BackToOffice) Enter(Tut.WorkTillEvening); break;
                case "dayEnd": Enter(Tut.Done); break;
            }
        }

        // Проверки по времени и счёту: наступил обед, выбито трое
        public void Poll(float minute, int lunchKills, bool inLunch)
        {
            if (!Active) return;
            var st = Step;
            if (st == Tut.WaitLunch && minute >= WorkDay.LunchOpen) Enter(Tut.GoLunch);
            if (st == Tut.SolveSecond && minute >= WorkDay.LunchOpen + 90 && !s.lunchTaken) Enter(Tut.GoLunch);   // засиделся — обед не пропускаем
            if (st == Tut.FirstKills && inLunch && lunchKills >= 3) Enter(Tut.BackToOffice);
        }

        public string Goal(float minute, int lunchKills)
        {
            switch (Step)
            {
                case Tut.MeetLead: return "Поговори с тимлидом Геной — подойди и нажми E";
                case Tut.SitDown: return "Сядь за свой компьютер — стол с уточкой, E у монитора";
                case Tut.RunCode: return "Прочитай задачу слева. Код — «Запустить» или «Проверить», варианты ответа — «Ответить»";
                case Tut.SolveFirst: return "Сдай первую задачу: «Проверить» или «Ответить» — всё должно быть зелёным";
                case Tut.SolveSecond: return "Реши ещё одну задачу — каждый час должен быть рабочим";
                case Tut.WaitLunch: return "Работай до 12:00, потом обед. Сейчас " + WorkDay.TimeText(minute);
                case Tut.GoLunch: return "Обед! Выйди через дверь «ВЫХОД» у входа в офис";
                case Tut.FirstKills: return "Выбей трёх гуманитариев (ЛКМ — нож): " + Mathf.Min(3, lunchKills) + " из 3. Технарей не трогай";
                case Tut.BackToOffice: return "Вернись в офис: дверь бизнес-центра или конец таймера";
                case Tut.WorkTillEvening: return "Работай до 18:00 — «Сытый» даёт +10% XP";
            }
            return null;
        }

        // Реплика Гены в начале шага (null — без реплики)
        public static string LeadLine(Tut t)
        {
            switch (t)
            {
                case Tut.MeetLead: return "Привет! Я Гена, твой тимлид. Нажми E — расскажу, как тут всё устроено.";
                case Tut.RunCode: return "Слева задача и теория. Если в задаче код — пиши и жми «Запустить», результат будет внизу. Если варианты — выбери ответ внизу и жми «Ответить».";
                case Tut.SolveFirst: return "Теперь сдай задачу: «Проверить» для кода или «Ответить» для вариантов. Всё зелёное — задача сдана.";
                case Tut.SolveSecond: return "Первая задача есть! Смотри на часы: каждый час должен быть рабочим. Сегодня штрафов нет, с завтра — будут.";
                case Tut.WaitLunch: return "Хороший темп. В 12:00 обед, дверь «ВЫХОД» у входа. До тех пор — задачи.";
                case Tut.GoLunch: return "Время обеда! Дверь «ВЫХОД» у входа. В городе гуманитарии дают по 10 монет, технарей не трогай.";
                case Tut.FirstKills: return "Юристы в костюмах с портфелем — гуманитарии. Бухгалтер с калькулятором — технарь, за него штраф. Подписи над головами подскажут.";
                case Tut.BackToOffice: return "Трое есть! На площади оружейная и мастерская — загляни, когда будут деньги. Возвращайся в офис.";
                case Tut.WorkTillEvening: return "С возвращением! «Сытый» даёт +10% XP ещё два часа. Работаем до 18:00.";
            }
            return null;
        }
    }

    // ======================= спринт на неделю =======================
    public class SprintRetro { public int number, goal, done, bonus; public bool success; public string text; }

    public class WeekSprint
    {
        readonly SaveData s;
        public WeekSprint(SaveData save) { s = save; if (s.sprintTasks == null) s.sprintTasks = new List<string>(); }

        public int Number { get { return s.sprintNo; } }
        public int Goal { get { return s.sprintGoal; } }
        public int DoneCount { get { return s.sprintDone; } }
        public bool Planned { get { return s.sprintNo > 0 && s.sprintGoal > 0; } }
        public List<string> Tasks { get { return s.sprintTasks; } }
        public int StartDay { get { return s.sprintStartDay; } }
        public static bool Monday(int day) { return ((day - 1) % 5 + 5) % 5 == 0; }
        public static bool Friday(int day) { return ((day - 1) % 5 + 5) % 5 == 4; }

        // Цель на неделю: сколько задач реально сделать по грейду (первая неделя — с обучением)
        public static int GoalFor(int grade, bool first)
        {
            if (first) return 8;
            return grade <= 1 ? 15 : grade == 2 ? 12 : 10;
        }

        public static int BonusFor(int goal, int grade) { return Mathf.RoundToInt(goal * 20f * (1f + 0.5f * Mathf.Clamp(grade, 0, 3)) / 10f) * 10; }

        // Планирование: следующие задачи по пути попадают в спринт. Неделя началась не с понедельника — цель меньше
        public void Plan(int day, int grade, IList<string> upcoming)
        {
            int left = 5 - (((day - 1) % 5 + 5) % 5);
            int goal = GoalFor(grade, s.sprintNo == 0);
            goal = Mathf.Max(3, Mathf.RoundToInt(goal * left / 5f));
            s.sprintNo++; s.sprintGoal = goal; s.sprintDone = 0; s.sprintStartDay = day;
            s.sprintTasks.Clear();
            for (int i = 0; i < upcoming.Count && s.sprintTasks.Count < goal + 3; i++) s.sprintTasks.Add(upcoming[i]);
        }

        public void TaskDone(string id) { if (Planned) s.sprintDone++; }

        // Ретро в пятницу вечером: выполнили цель — бонус
        public SprintRetro Close(int grade)
        {
            var r = new SprintRetro { number = s.sprintNo, goal = s.sprintGoal, done = s.sprintDone };
            r.success = r.done >= r.goal;
            r.bonus = r.success ? BonusFor(r.goal, grade) : 0;
            r.text = r.success
                ? "Ретро спринта " + r.number + ": сделано " + r.done + " из " + r.goal + ". Цель выполнена — бонус " + r.bonus + " монет."
                : "Ретро спринта " + r.number + ": сделано " + r.done + " из " + r.goal + ". Цель не выполнена, бонуса нет. В понедельник — новый спринт.";
            s.sprintGoal = 0; s.sprintTasks.Clear();
            return r;
        }
    }
}
