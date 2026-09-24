// Направления обучения: общая база + Backend / Frontend / DevOps, после всех трёх — Fullstack.
// Контент лежит в Resources/Tasks/tracks/<track>.json (формат: track, roadmap[], tasks[] — см. content/spec/FORMAT.md).
// Путь игрока = темы общей базы и выбранного направления, упорядоченные по грейдам (Стажёр → Junior → Junior+ → Middle)
// и зависимостям (requires). Тема открывается, когда игрок дорос до её грейда и закрыл все темы из requires;
// задачи внутри темы открываются по очереди. Грейд игрока — самый младший грейд, в котором ещё есть незакрытые темы.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using Intern.Py;

namespace Intern.Game
{
    public class Topic
    {
        public string id, title, grade, description, theory, track;
        public string[] requires = new string[0];
        public int gradeIndex, order, level;
        public List<TaskData> tasks = new List<TaskData>();
    }

    public class TrackData
    {
        public string track;
        public List<Topic> topics = new List<Topic>();
    }

    public static class Grades
    {
        public static readonly string[] Ids = { "intern", "junior", "junior_plus", "middle" };
        public static readonly string[] Names = { "Стажёр", "Junior", "Junior+", "Middle" };
        public static int Index(string id) { int i = Array.IndexOf(Ids, id); return i < 0 ? 0 : i; }
        public static string Name(int i) { return Names[Mathf.Clamp(i, 0, Names.Length - 1)]; }
        public static string Name(string id) { return Name(Index(id)); }
    }

    public static class Professions
    {
        public static readonly string[] Ids = { "backend", "frontend", "devops", "fullstack" };

        public static string Name(string id)
        {
            switch (id)
            {
                case "backend": return "Backend";
                case "frontend": return "Frontend";
                case "devops": return "DevOps";
                case "fullstack": return "Fullstack";
                case "common": return "Общая база";
                default: return id ?? "";
            }
        }

        // Короткое имя трека для кода задачи: BASE-12, BE-7, FE-3, OPS-20, FS-1
        public static string Prefix(string track)
        {
            switch (track)
            {
                case "common": return "BASE";
                case "backend": return "BE";
                case "frontend": return "FE";
                case "devops": return "OPS";
                case "fullstack": return "FS";
                default: return "KOD";
            }
        }

        public static string Stack(string id)
        {
            switch (id)
            {
                case "backend": return "Python · FastAPI · SQL · Redis · очереди";
                case "frontend": return "HTML/CSS · JavaScript · React · TypeScript";
                case "devops": return "Linux · Docker · CI/CD · Kubernetes · Terraform";
                case "fullstack": return "Весь стек: контракт API, релизы, инциденты";
                default: return "";
            }
        }

        public static string About(string id)
        {
            switch (id)
            {
                case "backend": return "Сервер «Лампового Маркета»: API, база данных, авторизация, кэш и очереди.";
                case "frontend": return "Всё, что видит покупатель: вёрстка, React-компоненты, работа с API и скорость.";
                case "devops": return "Прод держится на тебе: контейнеры, пайплайны, кластер, мониторинг и дежурства.";
                case "fullstack": return "Сквозные задачи через фронт, бэк и инфраструктуру. Для тех, кто прошёл все три.";
                default: return "";
            }
        }

        public static string Icon(string id)
        {
            switch (id) { case "backend": return "server"; case "frontend": return "browser"; case "devops": return "cloud"; default: return "stack"; }
        }
    }

    // Пройденные направления — общие для всех сохранений: новая игра их не сбрасывает
    public static class Career
    {
        const string Key = "intern_career_v1";
        static HashSet<string> set;

        static HashSet<string> Set
        {
            get
            {
                if (set == null)
                {
                    set = new HashSet<string>();
                    foreach (var p in PlayerPrefs.GetString(Key, "").Split(','))
                        if (p.Length > 0) set.Add(p);
                }
                return set;
            }
        }

        public static bool Done(string track) { return Set.Contains(track); }
        public static int Count { get { return Set.Count; } }
        public static bool FullstackOpen { get { return Done("backend") && Done("frontend") && Done("devops"); } }
        public static bool CanPick(string profession) { return profession != "fullstack" || FullstackOpen; }

        // true — направление отмечено впервые
        public static bool MarkDone(string track)
        {
            if (string.IsNullOrEmpty(track) || !Set.Add(track)) return false;
            Store(); return true;
        }

        public static void Unmark(string track) { if (Set.Remove(track)) Store(); }

        static void Store() { PlayerPrefs.SetString(Key, string.Join(",", Set.ToArray())); PlayerPrefs.Save(); }
    }

    // Путь игрока по выбранной профессии: темы в порядке прохождения и все задачи подряд
    public class TrackPath
    {
        public readonly string Profession;
        public readonly List<Topic> Topics;
        public readonly TaskData[] Tasks;
        public readonly Dictionary<string, Topic> TopicById = new Dictionary<string, Topic>();
        readonly Dictionary<TaskData, Topic> topicOf = new Dictionary<TaskData, Topic>();
        readonly Dictionary<TaskData, int> posInTopic = new Dictionary<TaskData, int>();

        public TrackPath(string profession, IEnumerable<Topic> topics)
        {
            Profession = profession;
            var list = topics.ToList();
            foreach (var tp in list) TopicById[tp.id] = tp;
            // уровень внутри грейда: сколько тем того же грейда нужно пройти до этой
            var level = new Dictionary<string, int>();
            foreach (var tp in list) Level(tp, level, new HashSet<string>());
            Topics = list.OrderBy(tp => tp.gradeIndex)
                         .ThenBy(tp => level[tp.id])
                         .ThenBy(tp => tp.track == "common" ? 1 : 0)   // сначала своё направление, потом общая база
                         .ThenBy(tp => tp.order).ToList();
            var all = new List<TaskData>();
            foreach (var tp in Topics)
                for (int i = 0; i < tp.tasks.Count; i++) { var t = tp.tasks[i]; all.Add(t); topicOf[t] = tp; posInTopic[t] = i; }
            Tasks = all.ToArray();
        }

        int Level(Topic tp, Dictionary<string, int> memo, HashSet<string> visiting)
        {
            int v;
            if (memo.TryGetValue(tp.id, out v)) return v;
            if (!visiting.Add(tp.id)) return 0;   // цикл в requires — не зависаем
            int best = 0;
            foreach (var r in tp.requires)
            {
                Topic rt;
                if (TopicById.TryGetValue(r, out rt) && rt.gradeIndex == tp.gradeIndex) best = Math.Max(best, Level(rt, memo, visiting) + 1);
            }
            visiting.Remove(tp.id);
            memo[tp.id] = best;
            return best;
        }

        public Topic TopicOf(TaskData t) { Topic tp; return t != null && topicOf.TryGetValue(t, out tp) ? tp : null; }
        public int PosInTopic(TaskData t) { int i; return t != null && posInTopic.TryGetValue(t, out i) ? i : 0; }
        public bool Contains(TaskData t) { return t != null && topicOf.ContainsKey(t); }

        public static bool TopicDone(Topic tp, HashSet<string> done)
        {
            foreach (var t in tp.tasks) if (!done.Contains(t.id)) return false;
            return true;
        }

        public int DoneIn(Topic tp, HashSet<string> done) { int n = 0; foreach (var t in tp.tasks) if (done.Contains(t.id)) n++; return n; }

        // 0..3 — текущий грейд; 4 — весь путь пройден
        public int GradeIndex(HashSet<string> done)
        {
            int g = 4;
            foreach (var tp in Topics) if (tp.gradeIndex < g && !TopicDone(tp, done)) g = tp.gradeIndex;
            return g;
        }

        public bool Complete(HashSet<string> done) { return GradeIndex(done) >= 4; }

        // Каких тем не хватает, чтобы открыть эту (пусто — открыта или ждёт только грейда)
        public List<Topic> MissingRequires(Topic tp, HashSet<string> done)
        {
            var res = new List<Topic>();
            foreach (var r in tp.requires)
            {
                Topic rt;
                if (TopicById.TryGetValue(r, out rt) && rt.gradeIndex <= tp.gradeIndex && !TopicDone(rt, done)) res.Add(rt);
            }
            return res;
        }

        public bool TopicOpen(Topic tp, HashSet<string> done, int grade)
        {
            if (tp.gradeIndex > Math.Min(grade, 3)) return false;
            return MissingRequires(tp, done).Count == 0;
        }
        public bool TopicOpen(Topic tp, HashSet<string> done) { return TopicOpen(tp, done, GradeIndex(done)); }

        public bool TaskOpen(TaskData t, HashSet<string> done, int grade)
        {
            if (t == null) return false;
            if (done.Contains(t.id)) return true;
            var tp = TopicOf(t);
            if (tp == null || !TopicOpen(tp, done, grade)) return false;
            int i = PosInTopic(t);
            return i == 0 || done.Contains(tp.tasks[i - 1].id);
        }
        public bool TaskOpen(TaskData t, HashSet<string> done) { return TaskOpen(t, done, GradeIndex(done)); }

        // Первая открытая и не сданная задача по порядку; null — всё сдано
        public TaskData Current(HashSet<string> done)
        {
            int g = GradeIndex(done);
            foreach (var t in Tasks) if (!done.Contains(t.id) && TaskOpen(t, done, g)) return t;
            foreach (var t in Tasks) if (!done.Contains(t.id)) return t;   // на случай битых requires
            return null;
        }

        public int DoneCount(HashSet<string> done) { int n = 0; foreach (var t in Tasks) if (done.Contains(t.id)) n++; return n; }

        public int CountAtGrade(int grade, HashSet<string> done, out int doneN)
        {
            int n = 0; doneN = 0;
            foreach (var tp in Topics) if (tp.gradeIndex == grade) { n += tp.tasks.Count; doneN += DoneIn(tp, done); }
            return n;
        }
    }

    public static class Tracks
    {
        public static readonly string[] All = { "common", "backend", "frontend", "devops", "fullstack" };
        static readonly Dictionary<string, TrackData> cache = new Dictionary<string, TrackData>();

        public static TrackData Load(string track)
        {
            TrackData d;
            if (cache.TryGetValue(track, out d)) return d;
            var ta = Resources.Load<TextAsset>("Tasks/tracks/" + track);
            if (ta == null)
            {
                Debug.LogError("[Стажёр] Не найден файл направления Resources/Tasks/tracks/" + track + ".json");
                d = new TrackData { track = track };
            }
            else
            {
                try { d = Parse(ta.text, track); }
                catch (Exception e) { Debug.LogError("[Стажёр] Ошибка в " + track + ".json: " + e.Message); d = new TrackData { track = track }; }
                Resources.UnloadAsset(ta);
            }
            cache[track] = d;
            return d;
        }

        public static TrackPath BuildPath(string profession) { return BuildPath(profession, Load); }

        public static TrackPath BuildPath(string profession, Func<string, TrackData> load)
        {
            var names = profession == "fullstack" ? new[] { "fullstack" } : new[] { "common", profession };
            var topics = new List<Topic>();
            foreach (var n in names) topics.AddRange(load(n).topics);
            return new TrackPath(profession, topics);
        }

        // py01 → be-py-basics-01 и т. п. (старые задачи перенесены в Backend)
        public static Dictionary<string, string> LegacyMap(Func<string, TrackData> load = null)
        {
            var map = new Dictionary<string, string>();
            foreach (var tp in (load ?? Load)("backend").topics)
                foreach (var t in tp.tasks)
                    if (!string.IsNullOrEmpty(t.legacyId)) map[t.legacyId] = t.id;
            return map;
        }

        // ================= разбор JSON =================
        public static TrackData Parse(string json, string fallbackTrack = null)
        {
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) throw new FormatException("ожидался JSON-объект");
            var d = new TrackData { track = Str(root, "track") ?? fallbackTrack ?? "" };
            var byId = new Dictionary<string, Topic>();
            int order = 0;
            foreach (var o in List(root, "roadmap"))
            {
                var r = o as Dictionary<string, object>;
                if (r == null) continue;
                var tp = new Topic
                {
                    id = Str(r, "topic_id") ?? ("topic" + order), title = Str(r, "title") ?? "", grade = Str(r, "grade") ?? "junior",
                    description = Str(r, "description") ?? "", theory = Str(r, "theory") ?? "", track = d.track, order = order++,
                };
                tp.gradeIndex = Grades.Index(tp.grade);
                tp.requires = List(r, "requires").OfType<string>().ToArray();
                d.topics.Add(tp); byId[tp.id] = tp;
            }
            foreach (var o in List(root, "tasks"))
            {
                var r = o as Dictionary<string, object>;
                if (r == null) continue;
                Topic tp;
                if (!byId.TryGetValue(Str(r, "topic_id") ?? "", out tp)) continue;
                tp.tasks.Add(ToTask(r, tp));
            }
            foreach (var tp in d.topics) tp.tasks.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            // коды задач внутри направления: по грейдам и зависимостям, как в пути игрока
            var local = new TrackPath(d.track, d.topics);
            string prefix = Professions.Prefix(d.track);
            for (int i = 0; i < local.Tasks.Length; i++) local.Tasks[i].key = prefix + "-" + (i + 1);
            return d;
        }

        static TaskData ToTask(Dictionary<string, object> o, Topic tp)
        {
            var c = Dict(o, "content") ?? new Dictionary<string, object>();
            var t = new TaskData();
            t.id = Str(o, "task_id") ?? "";
            t.topic = tp.id; t.track = tp.track; t.chapter = tp.title;
            t.grade = Str(o, "grade") ?? tp.grade;
            t.type = Str(o, "type") ?? "write_code";
            t.difficulty = Mathf.Clamp(Int(o, "difficulty", 1), 1, 5);
            t.xp = Int(o, "xp_reward", 20); t.reward = t.xp;
            t.timeLimit = Int(o, "time_limit_minutes", 0);
            t.character = Str(o, "character") ?? "teamlead";
            t.title = Str(o, "title") ?? t.id;
            t.legacy = Bool(o, "legacy"); t.legacyId = Str(o, "legacy_id");
            string story = Str(o, "story") ?? "", sender = Str(o, "sender");
            if (!string.IsNullOrEmpty(sender)) { t.sender = sender; t.story = story; }
            else { string s, b; SplitStory(story, t.character, out s, out b); t.sender = s; t.story = b; }
            t.theory = Str(o, "theory") ?? tp.theory;   // у перенесённых старых задач — своя короткая теория
            t.goal = Str(c, "question") ?? "";
            t.language = Str(c, "language");
            if (string.IsNullOrEmpty(t.language)) t.language = "text";
            t.starter = Str(c, "code") ?? "";
            t.entry = Str(c, "entry");
            t.explanation = Str(o, "explanation") ?? "";
            t.hints = List(o, "hints").OfType<string>().ToArray();
            var opts = List(c, "options");
            object ca; c.TryGetValue("correct_answer", out ca);
            if (opts.Count > 0)
            {
                t.options = opts.Select(x => x == null ? "" : Convert.ToString(x, CultureInfo.InvariantCulture)).ToArray();
                var arr = ca as List<object>;
                if (arr != null) { t.multi = true; t.answer = arr.Select(ToInt).ToArray(); }
                else t.answer = new[] { ToInt(ca) };
                t.solution = null;
            }
            else t.solution = ca as string;
            object tcs; c.TryGetValue("test_cases", out tcs);
            t.testCases = tcs as List<object>;
            // программа на Python (stdin → stdout): тесты в старом формате — для «Запустить», отладчика и старой IDE
            if (!t.IsChoice && t.language == "python" && string.IsNullOrEmpty(t.entry) && t.testCases != null)
            {
                var list = new List<TestCase>();
                foreach (var x in t.testCases)
                {
                    var tc = x as Dictionary<string, object>;
                    if (tc == null) continue;
                    object inp, exp; tc.TryGetValue("input", out inp); tc.TryGetValue("expected", out exp);
                    list.Add(new TestCase { inputs = PyRun.SplitInput(inp as string), expected = exp as string ?? "" });
                }
                t.tests = list.ToArray();
            }
            else t.tests = new TestCase[0];
            t.deadline = t.timeLimit > 0 ? t.timeLimit * 60 : (3 + 2 * t.difficulty) * 60;
            t.isBugHunt = t.type == "find_bug";
            return t;
        }

        // ================= отправитель и текст тикета =================
        static readonly Dictionary<string, string> People = new Dictionary<string, string>
        {
            { "Гена", "Тимлид Гена" }, { "Марина", "Сеньор Марина" }, { "Стас", "Продакт Стас" }, { "Оля", "HR Оля" },
            { "Ира", "QA Ира" }, { "Дима", "DevOps Дима" },
            { "Нина", "Нина · пекарня «Батон»" }, { "Артур", "Артур · фитнес-клуб «Жми»" }, { "Вера", "Вера · БЦ «Высота»" },
        };

        public static string ByCharacter(string character)
        {
            switch (character)
            {
                case "teamlead": return "Тимлид Гена";
                case "manager": return "Продакт Стас";
                case "qa": return "QA Ира";
                case "devops_colleague": return "DevOps Дима";
                case "client": return "Клиент";
                default: return "Коллега";
            }
        }

        // «Гена: «Текст»» → отправитель «Тимлид Гена» и текст без кавычек
        public static void SplitStory(string story, string character, out string sender, out string body)
        {
            body = (story ?? "").Trim();
            sender = ByCharacter(character);
            int depth = 0, colon = -1;
            for (int i = 0; i < body.Length && i < 140; i++)
            {
                char ch = body[i];
                if (ch == '«') depth++;
                else if (ch == '»') depth--;
                else if (depth == 0 && (ch == '.' || ch == '!' || ch == '?' || ch == '\n')) break;
                else if (depth == 0 && ch == ':') { colon = i; break; }
            }
            if (colon <= 0) return;
            string prefix = body.Substring(0, colon).Trim(), rest = body.Substring(colon + 1).Trim();
            if (prefix.Length == 0 || !char.IsUpper(prefix[0])) return;
            string first = prefix.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string known;
            if (People.TryGetValue(first, out known)) sender = known;
            else if (prefix.Length <= 48) sender = prefix;
            else return;
            body = Unquote(rest);
        }

        // «…» целиком в кавычках (возможно, с точкой после) — снимаем внешние кавычки
        static string Unquote(string s)
        {
            if (s.Length < 2 || s[0] != '«') return s;
            string tail = "";
            string core = s;
            if (core.EndsWith("».")) { core = core.Substring(0, core.Length - 1); tail = "."; }
            if (!core.EndsWith("»")) return s;
            int depth = 0;
            for (int i = 0; i < core.Length; i++)
            {
                if (core[i] == '«') depth++;
                else if (core[i] == '»') { depth--; if (depth == 0 && i != core.Length - 1) return s; }
            }
            string inner = core.Substring(1, core.Length - 2).Trim();
            if (tail.Length > 0 && inner.Length > 0 && ".!?…".IndexOf(inner[inner.Length - 1]) < 0) inner += tail;
            return inner;
        }

        // ================= помощники =================
        static string Str(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return null;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        static int Int(Dictionary<string, object> d, string k, int def)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; }
        }
        static bool Bool(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v; }
        static int ToInt(object v) { try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return -1; } }
        static Dictionary<string, object> Dict(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null; }
        static List<object> List(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v is List<object> ? (List<object>)v : new List<object>();
        }
    }
}
