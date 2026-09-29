// Спринт 9 «Языки: основа»: основной язык игрока. Спринт 10: Go — первый язык с запуском в Docker (LangBox). Путь = общие темы + ветка языка + темы профессии.
// Ветка языка — темы с меткой lang в файлах направлений (be-py-* в backend, fe-js-* в frontend) или отдельный
// файл Resources/Tasks/tracks/lang-<язык>.json. Тема другой ветки выпадает из пути, а её место в зависимостях
// занимает тема выбранной ветки, у которой она указана в replaces.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Intern.Game
{
    public class LangInfo
    {
        public string id, name, badge, runner, soon;
        public string[] professions;
        public bool ready;
    }

    public static class Languages
    {
        public static readonly LangInfo[] All =
        {
            new LangInfo { id = "python", name = "Python", badge = "PY", runner = "py", ready = true, professions = new[] { "backend", "devops" } },
            new LangInfo { id = "javascript", name = "JavaScript", badge = "JS", runner = "js", ready = true, professions = new[] { "frontend" } },
            new LangInfo { id = "typescript", name = "TypeScript", badge = "TS", runner = "ts", ready = true, professions = new[] { "frontend" } },
            new LangInfo { id = "go", name = "Go", badge = "GO", runner = "docker", ready = true, professions = new[] { "backend", "devops" } },
            new LangInfo { id = "java", name = "Java", badge = "JAVA", runner = "docker", ready = true, professions = new[] { "backend" } },
            new LangInfo { id = "csharp", name = "C#", badge = "C#", runner = "docker", soon = "спринт 13", professions = new[] { "backend" } },
            new LangInfo { id = "php", name = "PHP", badge = "PHP", runner = "docker", soon = "позже", professions = new[] { "backend" } },
        };

        public static LangInfo Get(string id) { return All.FirstOrDefault(l => l.id == id); }
        public static string Name(string id) { var l = Get(id); return l != null ? l.name : id ?? ""; }

        // Языки профессии: сначала готовые, потом «скоро». У Fullstack своего выбора нет — язык берётся из пройденных направлений
        public static List<LangInfo> For(string profession)
        {
            return All.Where(l => l.professions.Contains(profession)).OrderBy(l => l.ready ? 0 : 1).ToList();
        }

        public static string Default(string profession) { return profession == "frontend" ? "javascript" : "python"; }

        // Язык, который можно выбрать для профессии (иначе — язык по умолчанию)
        public static string Valid(string profession, string lang)
        {
            if (profession == "fullstack") return Default(profession);
            var l = Get(lang);
            return l != null && l.ready && l.professions.Contains(profession) ? lang : Default(profession);
        }

        public static string About(string id)
        {
            switch (id)
            {
                case "python": return "Код запускается прямо в игре. Читается как английский текст — отличный первый язык.";
                case "javascript": return "Язык браузера: код запускается прямо в игре. На нём держится весь фронтенд.";
                case "typescript": return "JavaScript с типами: ошибки видны до запуска. Код запускается в игре, типы проверяются по коду.";
                case "go": return "Быстрый компилируемый язык серверов и DevOps-инструментов. Код и тесты (go test) запускаются в песочнице Docker — образ golang скачается при первом запуске. Без Docker задачи проверяются по коду.";
                case "java": return "Строгий язык больших корпоративных систем, банков и Android. Код компилируется javac и тестируется в песочнице Docker — образ JDK (около 200 МБ) скачается при первом запуске. Без Docker задачи проверяются по коду.";
                default: return "Ветка языка появится в " + (Get(id) != null ? Get(id).soon : "следующих обновлениях") + ": задачи запускаются в песочнице Docker, без Docker — задачи без запуска.";
            }
        }
    }
}
