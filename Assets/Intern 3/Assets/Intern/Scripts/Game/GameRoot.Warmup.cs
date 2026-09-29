// Спринт 9: «Разминка» — задачи новых типов («Что выведет?», «Заполни пропуск», «Собери код», «Кликни по багу»)
// вне пути игрока: Resources/Tasks/tracks/warmup.json. Всегда открыты, грейд не двигают, сдаются как обычные задачи.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intern.Game
{
    public partial class GameRoot
    {
        List<TaskData> warmupTasks; string warmupKey;

        static TrackData WarmupData()
        {
            try { return Tracks.LoadOptional("warmup"); } catch (Exception) { return null; }
        }

        // Языки разминки: основной язык игрока (для TypeScript — ещё и JavaScript), у Fullstack — все готовые
        public List<string> WarmupLangs()
        {
            if (Profession == "fullstack") return new List<string> { "python", "javascript", "typescript" };
            var l = new List<string> { Language };
            if (Language == "typescript") l.Add("javascript");
            return l;
        }

        public List<TaskData> WarmupTasks
        {
            get
            {
                string key = Profession + "/" + Language;
                if (warmupTasks != null && warmupKey == key) return warmupTasks;
                warmupKey = key; warmupTasks = new List<TaskData>();
                var d = WarmupData();
                if (d == null) return warmupTasks;
                var langs = WarmupLangs();
                foreach (var tp in d.topics.Where(x => langs.Contains(x.lang)).OrderBy(x => langs.IndexOf(x.lang)))
                    warmupTasks.AddRange(tp.tasks);
                return warmupTasks;
            }
        }

        public bool WarmupVisible { get { return WarmupTasks.Count > 0 && (Tutorial == null || !Tutorial.Active || Tutorial.Step >= Tut.WorkTillEvening); } }
        public int WarmupDoneCount { get { return WarmupTasks.Count(t => Done.Contains(t.id)); } }

        // Любая задача разминки по id (и другого языка — например, из старого спринта)
        public TaskData WarmupTaskById(string id)
        {
            if (id == null || !id.StartsWith("wu-")) return null;
            var d = WarmupData();
            return d == null ? null : d.topics.SelectMany(tp => tp.tasks).FirstOrDefault(t => t.id == id);
        }
    }
}
