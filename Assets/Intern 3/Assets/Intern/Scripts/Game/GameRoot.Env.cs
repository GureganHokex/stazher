// GameRoot, спринт 7 «Окружение»: терминал с песочницей Docker, миссия «Настрой окружение» и задачи-сценарии
// (Resources/Tasks/env.json). Терминал один на всю игру: его вывод копится здесь, IDE только показывает.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Intern.Game
{
    public partial class GameRoot
    {
        public Shell Env { get; private set; }
        public readonly StringBuilder EnvTerm = new StringBuilder();   // вывод терминала (разметка для IDE)
        public int EnvTermVersion { get; private set; }
        List<TaskData> envTasks;
        static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[A-Za-z]");

        // Версия 7: списки окружения
        void MigrateEnv()
        {
            if (Save.envDone == null) Save.envDone = new List<string>();
            if (Save.envProgress == null) Save.envProgress = new List<EnvProgress>();
            if (Save.envImages == null) Save.envImages = new List<string>();
            if (Save.version >= 7) return;
            Save.version = 7;
            if (Progress.HasSave()) Progress.Save(Save);
        }

        void SetupEnv()
        {
            Env = new Shell();
            Env.OwnsImage = im => Save.envImages.Contains(DevEnv.NormImage(im));
            Env.ImagePulled = im => { im = DevEnv.NormImage(im); if (!Save.envImages.Contains(im)) { Save.envImages.Add(im); Persist(); } };
            Env.Finished = rec => { if (ideUi != null) ideUi.OnShellFinished(rec); };
            EnvTerm.Append("<color=#9D9D9D>Терминал «Стажёра»: bash в песочнице Docker. Папка /work — это Документы\\Стажёр\\work на твоём ПК. help — что умеет терминал.</color>\n");
        }

        // Каждый кадр: вывод команд и результаты фоновых проверок
        float envBusyWorkAt;

        void EnvTick()
        {
            if (Env == null) return;
            // идёт сборка или команда в терминале: стажёр ждёт результат — это работа, а не простой (и не автопауза)
            if (Env.Busy)
            {
                lastInput = Time.unscaledTime;
                if (Time.unscaledTime > envBusyWorkAt) { envBusyWorkAt = Time.unscaledTime + 10f; ReportWork(WorkKind.Terminal); }
            }
            var outp = Env.Pump();
            if (outp.Count == 0) return;
            foreach (var kv in outp)
            {
                string line = Ansi.Replace(kv.Value ?? "", "").Replace("\r", "");
                string esc = K.Esc(line);
                if (kv.Key == 1) EnvTerm.Append("<color=#F48771>").Append(esc).Append("</color>\n");
                else if (kv.Key == 2) EnvTerm.Append("<color=#9D9D9D>").Append(esc).Append("</color>\n");
                else EnvTerm.Append(esc).Append('\n');
            }
            TrimEnvTerm();
            EnvTermVersion++;
        }

        public void EnvEcho(string markup) { EnvTerm.Append(markup); TrimEnvTerm(); EnvTermVersion++; }
        public void EnvClear() { EnvTerm.Length = 0; EnvTermVersion++; }

        void TrimEnvTerm()
        {
            if (EnvTerm.Length <= 24000) return;
            // режем по границе строки, чтобы не разорвать тег разметки
            int cut = EnvTerm.Length - 16000;
            var s = EnvTerm.ToString();
            int nl = s.IndexOf('\n', cut);
            EnvTerm.Length = 0; EnvTerm.Append("<color=#6E7681>… начало вывода обрезано …</color>\n").Append(nl >= 0 ? s.Substring(nl + 1) : "");
        }

        // ================== задачи-сценарии ==================
        public List<TaskData> EnvTasks
        {
            get
            {
                if (envTasks != null) return envTasks;
                envTasks = new List<TaskData>();
                var ta = Resources.Load<TextAsset>("Tasks/env");
                if (ta == null) { Debug.LogWarning("[Стажёр] Нет Resources/Tasks/env.json — задачи окружения выключены"); return envTasks; }
                var list = EnvContent.Parse(ta.text);
                int n = 1;
                foreach (var s in list.OrderBy(x => x.mission ? 0 : 1)) envTasks.Add(EnvContent.ToTask(s, s.mission ? 0 : n++));
                Debug.Log("[Стажёр] Задачи окружения: " + envTasks.Count);
                return envTasks;
            }
        }

        public TaskData EnvMission { get { return EnvTasks.FirstOrDefault(t => t.scenario.mission); } }
        public bool EnvMissionDone { get { return Save.envDone.Contains(EnvContent.MissionId); } }
        public bool EnvDone(TaskData t) { return t != null && t.scenario != null && Save.envDone.Contains(t.id); }
        public bool EnvOpen(TaskData t) { return t != null && t.scenario != null && (t.scenario.mission || EnvMissionDone); }
        // раздел «Окружение» в проводнике — когда обучение первого дня дошло до «работай до вечера» (или закончилось)
        public bool EnvVisible { get { return EnvTasks.Count > 0 && (Tutorial == null || !Tutorial.Active || Tutorial.Step >= Tut.WorkTillEvening); } }
        public int EnvDoneCount { get { return EnvTasks.Count(EnvDone); } }

        public EnvProgress EnvProg(TaskData t, bool create)
        {
            foreach (var p in Save.envProgress) if (p.id == t.id) return p;
            if (!create) return null;
            var n = new EnvProgress { id = t.id };
            Save.envProgress.Add(n);
            return n;
        }

        // Текущий шаг (после «Заново» пройденную задачу можно пройти ещё раз — уже без награды)
        public int EnvStepOf(TaskData t)
        {
            var p = EnvProg(t, false);
            if (p != null) return Mathf.Clamp(p.step, 0, t.scenario.steps.Count);
            return EnvDone(t) ? t.scenario.steps.Count : 0;
        }

        // Шаг пройден: следующий или вся задача
        public void EnvAdvance(TaskData t, bool usedHints, float spent)
        {
            var p = EnvProg(t, true);
            p.step = Mathf.Min(p.step + 1, t.scenario.steps.Count);
            if (Work != null) Work.Activity(WorkKind.Check);
            Persist();
            if (p.step >= t.scenario.steps.Count) CompleteEnv(t, usedHints, spent);
        }

        // Начать заново: шаги с нуля, подготовка задачи — ещё раз
        public void EnvReset(TaskData t)
        {
            var p = EnvProg(t, true);
            p.step = 0; p.setup = false;
            Persist();
        }

        // Подготовить задачу в песочнице (файлы, репозиторий, лог) — один раз, пока не нажали «Заново»
        public void EnvPrepare(TaskData t, Action<bool> done)
        {
            var s = t.scenario;
            var p = EnvProg(t, true);
            if (string.IsNullOrEmpty(s.setup) || p.setup) { if (done != null) done(true); return; }
            if (!Env.CanRunCommands) { if (done != null) done(false); return; }
            EnvEcho("<color=#9D9D9D>Готовлю задачу «" + K.Esc(s.title) + "» в песочнице…</color>\n");
            Env.RunSetup(s.setup, ok =>
            {
                if (ok) { p.setup = true; Persist(); EnvEcho("<color=#9D9D9D>Готово.</color>\n"); }
                if (done != null) done(ok);
            });
        }

        void CompleteEnv(TaskData t, bool usedHints, float spent)
        {
            if (Save.envDone.Contains(t.id)) return;
            int oldLevel = Level; string oldRank = RankName;
            int xp, reward, got; bool sated;
            Pay(t, false, false, out xp, out reward, out got, out sated);
            Save.envDone.Add(t.id);
            Save.dayTasks++; Save.dayXp += xp; Save.dayMoney += reward;
            if (Work != null) Work.Activity(WorkKind.Solved);
            Persist(); UpdateBoard();
            Toast((t.scenario.mission ? "Окружение настроено! " : "Сценарий пройден! ") + "+" + xp + " XP" + (sated ? " (сытый +10%)" : "") + ", +" + reward + " монет" +
                  (got < reward ? ", из них " + (reward - got) + " в счёт долга Гене" : ""));
            if (player != null && player.avatar != null) player.avatar.React(2, 3f);
            if (t.scenario.mission)
            {
                string line = "Окружение готово — теперь ты можешь по-настоящему работать в терминале. В проводнике IDE, раздел «Окружение», открылись задачи по bash, Git и Docker.";
                if (leadWalker != null && lunch == null && (mode == Mode.Walk || mode == Mode.Ide)) LeadVisit(LeadMood.Praise, line);
                else Toast("Гена: " + line);
            }
            else if (EnvDoneCount == EnvTasks.Count) Toast("Все задачи окружения пройдены. Новые сценарии появятся в следующих обновлениях.");
            AfterXp(oldLevel, oldRank);
        }

        // Гена рассказывает про миссию один раз: первое открытие IDE после обучения
        void EnvAnnounce()
        {
            if (Save.envAnnounced || !EnvVisible || EnvMissionDone || (Tutorial != null && Tutorial.Active)) return;
            Save.envAnnounced = true; Persist();
            if (ideUi != null) ideUi.GameNotice("Гена: пора настроить окружение — терминал, Docker, песочница. Миссия «Настрой окружение» в проводнике IDE, раздел «Окружение».");
        }

        // «Убрать за собой»: контейнеры задач (all — ещё песочница и образы)
        public void EnvCleanup(bool all, Action<string> done)
        {
            var imgs = all ? Save.envImages.ToList() : null;
            EnvEcho("<color=#9D9D9D>Убираю за собой" + (all ? " всё, что создала игра" : " контейнеры задач") + "…</color>\n");
            Env.Async(() => DevEnv.Cleanup(all, imgs), res =>
            {
                if (all)
                {
                    Save.envImages.Clear();
                    foreach (var p in Save.envProgress) p.setup = false;
                    Persist();
                }
                EnvEcho("<color=#9D9D9D>" + K.Esc(res) + "</color>\n");
                Env.Probe();
                if (done != null) done(res);
            });
        }
    }
}
