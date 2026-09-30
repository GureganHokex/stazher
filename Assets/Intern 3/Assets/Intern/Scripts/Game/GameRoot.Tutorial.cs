// GameRoot: первый день с Геной (цели, маркер над целью, реплики) и спринты по неделе с доской задач.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Intern.Game
{
    public partial class GameRoot
    {
        public Onboarding Tutorial { get; private set; }
        public WeekSprint Sprint { get; private set; }
        bool tutorialPending;          // новая игра: обучение начнётся после гардероба
        Transform tutMarker;
        Mode boardFrom = Mode.Walk;

        void SetupTutorial()
        {
            Tutorial = new Onboarding(Save) { Entered = OnTutStep };
            Sprint = new WeekSprint(Save);
        }

        // Цель для HUD (null — обучения нет)
        public string TutorialGoal
        {
            get
            {
                if (Tutorial == null || !Tutorial.Active) return null;
                if (mode != Mode.Walk && mode != Mode.Lunch && mode != Mode.Dialog && mode != Mode.Shop) return null;
                return Tutorial.Goal(Save.minute, lunch != null ? lunch.kills : 0);
            }
        }

        void OnTutStep(Tut t)
        {
            Persist();
            var line = Onboarding.LeadLine(t);
            if (line == null) return;
            bool officeLive = mode == Mode.Walk || mode == Mode.Dialog || mode == Mode.Wardrobe;
            // на ключевых шагах Гена подходит сам, в остальных — пишет
            if ((t == Tut.MeetLead || t == Tut.GoLunch || t == Tut.WorkTillEvening) && leadWalker != null && officeLive && lunch == null)
                leadWalker.Visit(LeadMood.Info, line, t == Tut.MeetLead ? 90f : 0f);   // знакомство: ждёт, пока стажёр ответит
            else if (mode == Mode.Ide && ideUi != null) ideUi.GameNotice("Гена: " + line);
            else Toast("Гена: " + line);
        }

        void TutEvent(string e) { if (Tutorial != null) Tutorial.Event(e); }

        // Разговор с Геной на первом шаге: знакомство и выбор — учиться или сразу за работу
        bool TutorialTalk()
        {
            if (Tutorial == null || !Tutorial.Active) return false;
            if (Tutorial.Step != Tut.MeetLead) return false;
            if (leadWalker != null) leadWalker.Interrupt();
            string text = "Добро пожаловать в «Кодзиллу»! Я Гена, твой тимлид.\n\n" +
                          "• Рабочий день — с 9:00 до 18:00. Каждый игровой час должен быть рабочим: задачи, запуски, правки кода.\n" +
                          "• Задачи — в IDE за твоим компьютером (стол с уточкой). Слева теория, справа требования.\n" +
                          "• С 12:00 до 16:00 — обед в городе, дверь «ВЫХОД» у входа.\n" +
                          "• Работаем спринтами по неделе: план — на доске задач за мной.\n\n" +
                          "Сегодня первый день: штрафов нет, я буду подсказывать.";
            OpenDialog("Тимлид Гена", text,
                Btn("!Понял, иду за стол", () => { CloseDialog(); TutEvent("talk"); }),
                Btn("~Я уже работал — без обучения", () => { CloseDialog(); Tutorial.Skip(); Persist(); Toast("Обучение пропущено. Доска задач — за Геной, у доски."); }));
            return true;
        }

        // ================== кадр: проверки и маркер ==================
        void TutTick()
        {
            if (Tutorial == null) return;
            if (Tutorial.Active) Tutorial.Poll(Save.minute, lunch != null ? lunch.kills : 0, lunch != null);
            Vector3 at;
            bool show = Tutorial.Active && (mode == Mode.Walk || mode == Mode.Lunch) && TutTarget(out at);
            if (!show) { if (tutMarker != null && tutMarker.gameObject.activeSelf) tutMarker.gameObject.SetActive(false); return; }
            if (tutMarker == null) BuildMarker();
            tutMarker.gameObject.SetActive(true);
            TutTarget(out at);
            tutMarker.position = at;
        }

        bool TutTarget(out Vector3 at)
        {
            at = Vector3.zero;
            switch (Tutorial.Step)
            {
                case Tut.MeetLead: if (refs.lead == null) return false; at = refs.lead.transform.position + Vector3.up * 2.9f; return true;
                case Tut.SitDown: if (refs.screen == null) return false; at = refs.screen.position + Vector3.up * 0.9f; return true;
                case Tut.GoLunch: if (exitSpot == null || lunch != null) return false; at = exitSpot.position + Vector3.up * 2.9f; return true;
                case Tut.FirstKills:
                    if (lunch == null) return false;
                    var n = lunch.NearestTarget(player.Position); if (n == null) return false;
                    at = n.transform.position + Vector3.up * 2.9f; return true;
                case Tut.BackToOffice: if (lunch == null || city == null) return false; at = city.officeDoor + Vector3.up * 4f; return true;
            }
            return false;
        }

        void BuildMarker()
        {
            tutMarker = new GameObject("TutMarker").transform;
            var pin = Look.Node("Pin", tutMarker, Vector3.zero);
            Look.Prim("Head", pin, PrimitiveType.Sphere, new Vector3(0, 0.25f, 0), Vector3.one * 0.32f, Pal.Pink, false, 0.6f, 0.8f, false);
            var tip = Look.RBox("Tip", pin, new Vector3(0, -0.02f, 0), new Vector3(0.2f, 0.2f, 0.06f), Pal.Pink, 0.02f, false, 0.6f, 0.8f, false);
            tip.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            pin.gameObject.AddComponent<Bobber>().amplitude = 0.12f;
            pin.gameObject.AddComponent<Billboard>();
            foreach (var t in tutMarker.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
        }

        // ================== спринт на неделю ==================
        void PlanSprint()
        {
            var upcoming = new List<string>();
            foreach (var t in Path.Tasks) if (!Save.done.Contains(t.id)) upcoming.Add(t.id);
            Sprint.Plan(Save.day, Mathf.Min(GradeIdx, 3), upcoming);
            EnsureSprintEnv();
            Persist(); UpdateBoard();
            Toast("Спринт " + Sprint.Number + ": цель — " + Sprint.Goal + " задач до пятницы. Доска задач — у Гены.");
        }

        // Итоги дня: в пятницу ретро спринта, в понедельник новый план
        void SprintAtDayEnd(DayReport r)
        {
            if (Sprint == null) return;
            if (WeekSprint.Friday(r.day) && Sprint.Planned)
            {
                var retro = Sprint.Close(Mathf.Min(GradeIdx, 3));
                if (retro.bonus > 0) { int got = Work.Earn(retro.bonus); Save.money += got; r.money += retro.bonus; }
                r.retro = retro.text; r.retroSuccess = retro.success;
            }
        }

        void SprintAtDayStart()
        {
            if (Sprint == null) return;
            EnsureDaily();
            if (!Sprint.Planned || (WeekSprint.Monday(Save.day) && Sprint.StartDay != Save.day)) PlanSprint();
        }

        public string TopicTitleOf(TaskData t) { if (t != null && t.scenario != null) return "Окружение · терминал"; var tp = TopicOfAny(t); return tp != null ? tp.title : ""; }
        public TaskData TaskById(string id)
        {
            if (TaskGen.IsSpec(id)) return GenTask(id);
            return PathTask(id) ?? EnvTaskById(id) ?? WarmupTaskById(id);
        }

        // ================== доска задач ==================
        public void OpenBoard()
        {
            if (mode != Mode.Walk) return;
            boardFrom = mode; mode = Mode.Board; SetCursor(false);
            if (player.avatar != null) player.avatar.moveSpeed = 0f;
        }

        public void CloseBoard()
        {
            if (mode != Mode.Board) return;
            mode = Mode.Walk; SetCursor(true); lastInput = Time.unscaledTime;
        }

        void BoardUpdate()
        {
            player.Tick(false);
            if (InputX.Esc()) CloseBoard();
        }

        // Доска-канбан в офисе: E открывает задачи спринта
        void PlaceTaskBoard()
        {
            var go = new GameObject("TaskBoardZone");
            go.transform.position = new Vector3(7.5f, 1.8f, 7.45f);
            var col = go.AddComponent<BoxCollider>(); col.size = new Vector3(4.2f, 2.2f, 0.6f); col.isTrigger = true;
            go.AddComponent<TaskBoard>();
        }
    }

    public class TaskBoard : Interactable
    {
        public override string Prompt { get { return "[E] Доска задач спринта"; } }
        public override void Interact(GameRoot g) { g.OpenBoard(); }
        public override PlaqueInfo Plaque(GameRoot g) { return g.BoardPlaque(); }
    }
}
