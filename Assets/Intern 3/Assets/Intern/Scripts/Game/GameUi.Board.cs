// Интерфейс спринта 4: цель обучения на экране и доска задач спринта как в Jira.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public partial class GameUi
    {
        VisualElement goalCard, boardLayer;
        Label goalText, goalStep;
        string goalShown;

        // ======================= цель обучения =======================
        VisualElement BuildGoal()
        {
            var row = K.Box(true); row.pickingMode = PickingMode.Ignore; row.style.position = Position.Absolute; row.style.left = 0f; row.style.right = 0f; row.style.top = 118f;
            row.style.justifyContent = Justify.Center;
            goalCard = K.Box(true); goalCard.pickingMode = PickingMode.Ignore; goalCard.style.alignItems = Align.Center;
            goalCard.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.94f); K.Radius(goalCard, 16f); Border(goalCard, new Color(Pink.r, Pink.g, Pink.b, 0.7f), 2f);
            K.Pad(goalCard, 10f, 20f, 10f, 14f); goalCard.style.maxWidth = 760f;
            goalCard.Add(new Icon("star", Pink, 22f));
            var col = K.Box(); col.pickingMode = PickingMode.Ignore; col.style.marginLeft = 10f; col.style.flexShrink = 1f;
            goalStep = K.B("", 12f, Pink); goalStep.style.letterSpacing = 1.2f; col.Add(goalStep);
            goalText = K.T("", 17f, Text, false, true, true); col.Add(goalText);
            goalCard.Add(col);
            row.Add(goalCard); row.style.display = DisplayStyle.None;
            return row;
        }

        void UpdateGoal(VisualElement row)
        {
            var goal = g.TutorialGoal;
            row.style.display = goal != null ? DisplayStyle.Flex : DisplayStyle.None;
            row.style.top = GoalRowTop();
            if (goal == null || goal == goalShown) return;
            goalShown = goal;
            goalText.text = K.Esc(goal);
            goalStep.text = "ПЕРВЫЙ ДЕНЬ · ШАГ " + g.Tutorial.Index + " ИЗ " + Onboarding.Count;
            Pop(goalCard);
        }

        // ======================= доска задач =======================
        void BuildBoard()
        {
            boardLayer.Clear();
            var S = g.Sprint; if (S == null) return;
            VisualElement wrap;
            var card = CardBox(1240f, out wrap);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            head.Add(new Icon("task", Sun, 34f));
            var ht = K.B("Доска задач · Спринт " + S.Number, 36f, Sun, true); ht.style.marginLeft = 14f; head.Add(ht);
            head.Add(K.Spacer());
            var days = 5 - (((g.Save.day - 1) % 5 + 5) % 5);
            head.Add(K.B(g.Work.WeekdayFull + " · до ретро " + (days <= 1 ? "сегодня вечером" : days + " дн."), 17f, Muted));
            card.Add(head);

            // прогресс спринта
            float k = S.Goal > 0 ? Mathf.Clamp01((float)S.DoneCount / S.Goal) : 0f;
            var pr = K.Box(true); pr.style.alignItems = Align.Center; pr.style.marginTop = 10f; pr.pickingMode = PickingMode.Ignore;
            pr.Add(K.B("Цель спринта: " + S.DoneCount + " из " + S.Goal + " задач", 18f, k >= 1f ? Mint : Text));
            var bar = K.Box(); bar.pickingMode = PickingMode.Ignore; bar.style.height = 12f; K.Grow(bar); bar.style.marginLeft = 16f; bar.style.backgroundColor = Well; K.Radius(bar, 6f); bar.style.overflow = Overflow.Hidden;
            var fill = K.Box(); fill.style.height = 12f; fill.style.width = Length.Percent(k * 100f); fill.style.backgroundColor = k >= 1f ? Mint : Sun; K.Radius(fill, 6f); bar.Add(fill); pr.Add(bar);
            card.Add(pr);
            bool daily = g.PathComplete && g.Save.daily.Count > 0;
            var note = K.T("Выполнишь цель к вечеру пятницы — бонус " + WeekSprint.BonusFor(S.Goal, Mathf.Min(g.GradeIdx, 3)) + " монет. " +
                (daily ? "Путь пройден: на доске тикеты дня из генератора, каждое утро — новые. Тренировки по темам тоже идут в зачёт спринта."
                       : "Задачи открываются по порядку: следующая — после текущей."), 14f, Muted, false, false, true);
            note.style.marginTop = 6f; note.style.marginBottom = 12f; card.Add(note);

            // колонки: к выполнению, в работе, готово
            var cols = K.Box(true); cols.pickingMode = PickingMode.Ignore; cols.style.height = 520f;
            var todo = BoardColumn("К ВЫПОЛНЕНИЮ", Sky); var doing = BoardColumn("В РАБОТЕ", Sun); var done = BoardColumn("ГОТОВО", Mint);
            cols.Add(todo.Item1); cols.Add(doing.Item1); cols.Add(done.Item1);
            var cur = g.CurrentTaskPublic;
            int nTodo = 0, nDone = 0;
            var ids = new List<string>(daily ? g.Save.daily : S.Tasks);
            if (daily) foreach (var id in S.Tasks) if (id.StartsWith("env-") && !ids.Contains(id)) ids.Add(id);   // карточка окружения идёт и в режиме тикетов дня
            foreach (var id in ids)
            {
                var t = g.TaskById(id); if (t == null) continue;
                bool isDone = g.IsDone(t), isCur = cur != null && cur.id == id;
                var target = isDone ? done.Item2 : isCur ? doing.Item2 : todo.Item2;
                bool locked = t.scenario != null ? !g.EnvOpen(t) : !t.generated && !g.IsOpen(t);
                target.Add(Ticket(t, isDone, isCur, !isDone && !isCur && locked));
                if (isDone) nDone++; else if (!isCur) nTodo++;
            }
            // текущая задача вне спринта (например, дошли дальше плана) — тоже в «В работе»
            if (cur != null && !S.Tasks.Contains(cur.id) && !g.Save.daily.Contains(cur.id) && !g.IsDone(cur)) doing.Item2.Add(Ticket(cur, false, true, false));
            if (nTodo == 0) todo.Item2.Add(EmptyNote("Всё из спринта взято в работу"));
            if (nDone == 0) done.Item2.Add(EmptyNote("Пока пусто — сдай первую задачу"));
            card.Add(cols);
            card.Add(new UiBtn("Закрыть", () => g.CloseBoard(), Ghost, GhostHover, GhostLip, Text, "close", "Esc", 56f, false));
            boardLayer.Add(wrap);
        }

        Tuple<VisualElement, VisualElement> BoardColumn(string title, Color accent)
        {
            var col = K.Box(); K.Grow(col); col.style.flexBasis = 0f; col.style.marginRight = 12f; col.pickingMode = PickingMode.Ignore;
            col.style.backgroundColor = Well; K.Radius(col, 14f); K.Pad(col, 12f, 12f, 12f, 12f);
            var h = K.Box(true); h.style.alignItems = Align.Center; h.pickingMode = PickingMode.Ignore;
            var dot = K.Box(); dot.style.width = 10f; dot.style.height = 10f; K.Radius(dot, 5f); dot.style.backgroundColor = accent; h.Add(dot);
            var l = K.B(title, 13f, Muted); l.style.marginLeft = 8f; l.style.letterSpacing = 1.4f; h.Add(l);
            col.Add(h);
            var scroll = new ScrollBox(); scroll.style.marginTop = 10f;
            scroll.RegisterCallback<WheelEvent>(e => { scroll.Wheel(e.delta.y * 40f); e.StopPropagation(); });
            col.Add(scroll);
            return Tuple.Create(col, scroll.Content);
        }

        VisualElement Ticket(TaskData t, bool isDone, bool isCur, bool locked)
        {
            var c = K.Box(); c.pickingMode = PickingMode.Ignore; K.Pad(c, 10f, 12f, 10f, 12f); K.Radius(c, 10f); c.style.marginBottom = 8f;
            c.style.backgroundColor = isCur ? CardHi : Card; Border(c, isCur ? new Color(Sun.r, Sun.g, Sun.b, 0.8f) : new Color(Line.r, Line.g, Line.b, 0.6f), isCur ? 2f : 1f);
            if (locked) c.style.opacity = 0.6f;
            var top = K.Box(true); top.style.alignItems = Align.Center; top.pickingMode = PickingMode.Ignore;
            var key = K.B(g.TaskCodeOf(t), 12f, isDone ? Mint : Pink); K.Pad(key, 2f, 6f, 2f, 6f); K.Radius(key, 5f); key.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f); top.Add(key);
            top.Add(K.Spacer());
            top.Add(K.B("+" + t.xp + " XP", 12f, Muted));
            c.Add(top);
            var title = K.T(K.Esc(t.title), 15f, isDone ? Muted : Text, false, true, true); title.style.marginTop = 6f; c.Add(title);
            var tp = g.TopicTitleOf(t);
            if (!string.IsNullOrEmpty(tp)) { var tl = K.T(tp + (locked ? " · откроется по порядку" : ""), 12f, Muted, false, false, true); tl.style.marginTop = 4f; c.Add(tl); }
            return c;
        }

        VisualElement EmptyNote(string s) { var l = K.T(s, 14f, Muted, false, false, true); l.style.marginTop = 4f; return l; }
    }
}
