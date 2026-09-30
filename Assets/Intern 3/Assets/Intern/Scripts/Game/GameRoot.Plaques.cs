// Плашки сверху экрана (спринт 2 версии 0.9): подходишь к доске, Гене, своему столу, кофемашине, двери или арсеналу —
// сверху появляется карточка с тем, что раньше висело парящим 3D-текстом, и с тем, что здесь можно сделать.
namespace Intern.Game
{
    public partial class GameRoot
    {
        // null — плашки нет: не рядом с точкой или не режим ходьбы
        public PlaqueInfo FocusPlaque { get { return focus != null && (mode == Mode.Walk || mode == Mode.Lunch) ? focus.Plaque(this) : null; } }

        public PlaqueInfo BoardPlaque()
        {
            var cur = CurrentTask;
            var p = new PlaqueInfo { icon = "task", title = "Доска задач спринта", sub = ProfessionName + " · " + RankName };
            p.Item("Сделано", DoneCount + " / " + TotalCount);
            if (cur != null && !PathComplete)
            {
                var tp = Path.TopicOf(cur);
                if (tp != null) p.Item("Тема", tp.title);
                p.Item("Сейчас", TaskCodeOf(cur) + " " + cur.title);
            }
            else
            {
                p.Item("Направление", "пройдено");
                if (Save.daily.Count > 0) p.Item("Тикеты дня", DailyDone + " / " + DailyTotal);
            }
            if (Sprint != null && Sprint.Planned) p.Item("Спринт " + Sprint.Number, Sprint.DoneCount + " / " + Sprint.Goal);
            p.Item("Багов поймано", Save.bugsCaught.ToString());
            return p;
        }

        public PlaqueInfo LeadPlaque()
        {
            var p = new PlaqueInfo { icon = "account", title = "Тимлид Гена" };
            bool meet = Tutorial != null && Tutorial.Active && Tutorial.Step == Tut.MeetLead;
            if (meet) p.sub = "Ждёт тебя: первый день начинается со знакомства";
            else if (leadWalker != null && leadWalker.HasNews) p.sub = "Хочет тебе что-то сказать";
            else p.sub = "Поговорить: следующая задача, совет, дисциплина" + (CanChangeCompany ? ", смена компании" : "");
            if (Work != null && !meet)
            {
                p.Item("Сегодня задач", Save.dayTasks.ToString());
                p.Item("Выговоры", Save.strikes + " из " + Work.StrikeLimit);
                if (Save.debt > 0) p.Item("Долг", Save.debt + " из " + Work.DebtLimit);
            }
            if (Sprint != null && Sprint.Planned && !meet) p.Item("Спринт " + Sprint.Number, Sprint.DoneCount + " / " + Sprint.Goal);
            return p;
        }

        public PlaqueInfo DeskPlaque()
        {
            var cur = CurrentTask;
            var p = new PlaqueInfo { icon = "monitor", title = "Твоё рабочее место", sub = "Задачи, теория и запуск кода — в IDE за компьютером" };
            if (cur != null) p.Item("Задача", TaskCodeOf(cur) + " " + cur.title);
            p.Item("Сегодня решено", Save.dayTasks.ToString());
            return p;
        }

        public PlaqueInfo ShopPlaque()
        {
            var p = new PlaqueInfo { icon = "coin", title = "Кофе и апгрейды", sub = "Кофе — 20 монет, 30 секунд быстрее ходишь" + (!Save.hasDuck || !Save.hasMonitor ? "; уточка и второй монитор помогают в задачах" : "") };
            p.Item("Монеты", Save.money.ToString());
            return p;
        }

        public PlaqueInfo DoorPlaque()
        {
            var p = new PlaqueInfo { icon = "door", title = "Выход в город" };
            string why = null;
            bool open = Work == null || Work.CanLunch(out why);
            p.sub = open ? "Обед открыт до 16:00: гуманитарии дают монеты, технарей не трогай" : (string.IsNullOrEmpty(why) ? "Обед с 12:00 до 16:00" : why);
            if (Work != null) p.Item("Сейчас", Work.Clock);
            return p;
        }

        public PlaqueInfo ArsenalPlaque()
        {
            var p = new PlaqueInfo { icon = "lock", title = "Арсенал", sub = "Оружие и обвесы на обед — открываются по грейду" };
            p.Item("Монеты", Save.money.ToString());
            return p;
        }
    }
}
