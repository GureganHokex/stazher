// Спринт 7, «Что произошло»: короткое объяснение команды терминала после запуска — из каких частей она
// состоит и что делает каждая (конвейеры, перенаправления, флаги частых команд, подкоманды git и docker).
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Intern.Game
{
    public static class ShellExplain
    {
        static readonly Dictionary<string, string> Cmd = new Dictionary<string, string>
        {
            { "pwd", "печатает текущую папку" }, { "ls", "показывает файлы в папке" }, { "cd", "переходит в другую папку" },
            { "cat", "печатает содержимое файла" }, { "echo", "печатает текст" }, { "printf", "печатает текст по шаблону" },
            { "mkdir", "создаёт папку" }, { "rm", "удаляет файлы (без корзины!)" }, { "rmdir", "удаляет пустую папку" }, { "cp", "копирует файлы" }, { "mv", "перемещает или переименовывает" },
            { "touch", "создаёт пустой файл (или обновляет дату)" }, { "head", "первые строки (по умолчанию 10)" }, { "tail", "последние строки (по умолчанию 10)" },
            { "wc", "считает строки, слова и байты" }, { "grep", "ищет строки по образцу" }, { "sort", "сортирует строки" }, { "uniq", "убирает соседние повторы (поэтому перед ним sort)" },
            { "cut", "вырезает колонки из строк" }, { "awk", "обрабатывает строки по колонкам ($1, $2…)" }, { "sed", "заменяет текст в потоке или файле" }, { "tr", "заменяет или удаляет символы" },
            { "find", "ищет файлы по имени и условиям" }, { "tree", "рисует дерево папок" }, { "xargs", "передаёт строки ввода аргументами другой команде" }, { "tee", "пишет вывод и на экран, и в файл" },
            { "chmod", "меняет права доступа" }, { "ln", "создаёт ссылку на файл" }, { "du", "размер файлов и папок" }, { "df", "свободное место на дисках" }, { "stat", "подробности о файле" }, { "file", "определяет тип файла" },
            { "curl", "делает HTTP-запрос" }, { "wget", "скачивает файл по ссылке" }, { "ping", "проверяет, отвечает ли хост" }, { "nslookup", "узнаёт IP по имени сайта" }, { "dig", "узнаёт DNS-записи" }, { "ip", "сетевые интерфейсы и маршруты" },
            { "python3", "запускает Python" }, { "python", "запускает Python" }, { "node", "запускает JavaScript (Node.js)" }, { "npm", "менеджер пакетов Node.js" }, { "pip", "менеджер пакетов Python" }, { "pip3", "менеджер пакетов Python" },
            { "jq", "разбирает и фильтрует JSON" }, { "date", "текущие дата и время" }, { "whoami", "имя пользователя" }, { "env", "переменные окружения" }, { "export", "задаёт переменную окружения" },
            { "ps", "список процессов" }, { "kill", "посылает сигнал процессу (обычно — завершиться)" }, { "sleep", "ждёт N секунд" }, { "seq", "печатает числа по порядку" }, { "which", "где лежит программа" },
            { "diff", "показывает различия двух файлов" }, { "tar", "упаковывает и распаковывает архивы .tar" }, { "zip", "упаковывает в .zip" }, { "unzip", "распаковывает .zip" }, { "basename", "имя файла без папки" }, { "dirname", "папка без имени файла" },
            { "history", "история команд" }, { "clear", "очищает экран терминала" }, { "true", "ничего не делает и завершается успешно" }, { "rev", "переворачивает строки" }, { "nl", "нумерует строки" },
            { "git", "система контроля версий" }, { "docker", "управляет контейнерами (на твоём ПК, через фильтр игры)" },
        };

        static readonly Dictionary<string, string> Sub = new Dictionary<string, string>
        {
            { "git init", "создаёт новый репозиторий в папке" }, { "git status", "что изменено и что готово к коммиту" }, { "git log", "история коммитов" },
            { "git add", "добавляет изменения в следующий коммит (индекс)" }, { "git commit", "сохраняет снимок изменений с сообщением" }, { "git branch", "список веток или новая ветка" },
            { "git checkout", "переключает ветку (или восстанавливает файл)" }, { "git switch", "переключает ветку" }, { "git merge", "вливает другую ветку в текущую" },
            { "git diff", "показывает изменения построчно" }, { "git show", "содержимое коммита или файла в коммите" }, { "git restore", "отменяет изменения в файле" },
            { "git reset", "откатывает индекс или ветку" }, { "git stash", "прячет незакоммиченные изменения" }, { "git remote", "удалённые репозитории" },
            { "git push", "отправляет коммиты на сервер" }, { "git pull", "забирает коммиты с сервера" }, { "git clone", "копирует репозиторий" }, { "git rev-parse", "служебная: узнать хеш или имя ветки" },
            { "docker run", "создаёт и запускает контейнер из образа" }, { "docker ps", "список запущенных контейнеров" }, { "docker images", "список образов" },
            { "docker pull", "скачивает образ из Docker Hub" }, { "docker logs", "вывод (лог) контейнера" }, { "docker exec", "выполняет команду внутри запущенного контейнера" },
            { "docker stop", "останавливает контейнер" }, { "docker start", "запускает остановленный контейнер" }, { "docker restart", "перезапускает контейнер" },
            { "docker rm", "удаляет контейнер" }, { "docker rmi", "удаляет образ" }, { "docker build", "собирает образ по Dockerfile" }, { "docker inspect", "все подробности о контейнере или образе в JSON" },
            { "docker port", "какие порты контейнера открыты на ПК" }, { "docker network", "сети контейнеров" }, { "docker volume", "тома — данные, которые переживают контейнер" },
            { "docker stats", "нагрузка контейнеров" }, { "docker top", "процессы внутри контейнера" }, { "docker kill", "мгновенно убивает контейнер" }, { "docker tag", "даёт образу ещё одно имя" },
        };

        static readonly Dictionary<string, string> Flag = new Dictionary<string, string>
        {
            { "ls -l", "подробный список: права, размер, дата" }, { "ls -a", "со скрытыми файлами (имя с точки)" }, { "ls -h", "размеры в КБ/МБ" },
            { "head -n", "сколько строк взять" }, { "tail -n", "сколько строк взять" }, { "tail -f", "следить за новыми строками" },
            { "sort -n", "как числа, а не как текст" }, { "sort -r", "в обратном порядке" }, { "sort -k", "по какой колонке" }, { "sort -u", "без повторов" },
            { "uniq -c", "с числом повторов перед строкой" }, { "cut -d", "разделитель колонок" }, { "cut -f", "номер колонки" },
            { "wc -l", "только число строк" }, { "wc -w", "только число слов" }, { "grep -i", "без учёта регистра" }, { "grep -v", "строки, где образца НЕТ" },
            { "grep -c", "только число совпадений" }, { "grep -n", "с номерами строк" }, { "grep -r", "во всех файлах папки" }, { "grep -E", "расширенные регулярные выражения" }, { "grep -o", "только совпавшая часть" },
            { "rm -r", "с папками и всем содержимым" }, { "rm -f", "без вопросов и ошибок" }, { "mkdir -p", "вместе с родительскими папками, без ошибки, если есть" },
            { "sed -i", "правит файл на месте" }, { "cp -r", "с папками" }, { "curl -s", "без индикатора загрузки" }, { "curl -I", "только заголовки ответа" }, { "curl -o", "сохранить в файл" },
            { "git commit -m", "сообщение коммита" }, { "git commit -a", "сначала добавить все изменённые файлы" }, { "git checkout -b", "создать ветку и переключиться" }, { "git switch -c", "создать ветку и переключиться" },
            { "git log --oneline", "по строке на коммит" }, { "git log --graph", "рисует ветки" }, { "git log --all", "все ветки, а не только текущую" }, { "git branch -d", "удалить ветку" },
            { "docker run -d", "в фоне: терминал сразу свободен" }, { "docker run -p", "порт ПК:порт контейнера (игра открывает только на 127.0.0.1)" }, { "docker run -v", "папка ПК:папка в контейнере (только из /work)" },
            { "docker run --name", "имя контейнера" }, { "docker run --rm", "удалить контейнер после остановки" }, { "docker run -e", "переменная окружения" },
            { "docker ps -a", "и остановленные тоже" }, { "docker rm -f", "остановить и удалить сразу" }, { "docker logs -f", "следить за логом" }, { "docker build -t", "имя (тег) образа" },
        };

        static readonly HashSet<string> Letters = new HashSet<string> { "ls", "sort", "grep", "rm", "uniq", "wc", "cp", "tail", "head", "curl", "mkdir" };

        // Объяснение строки: список пунктов (уже с разметкой для IDE)
        public static List<string> Explain(string line)
        {
            var res = new List<string>();
            if (string.IsNullOrEmpty(line)) return res;
            var parts = SplitOps(line);
            foreach (var p in parts)
            {
                if (p.Key != null) { res.Add("<color=#C586C0>" + Esc(p.Key) + "</color> — " + OpText(OpOf(p.Key))); continue; }
                var t = DockerGuard.Tokenize(p.Value);
                if (t.Count == 0) continue;
                string c = t[0];
                if (c.Contains("=") && !c.StartsWith("-")) { res.Add("<color=#9CDCFE>" + Esc(c) + "</color> — задаёт переменную"); continue; }
                string desc; Cmd.TryGetValue(c, out desc);
                string head = c; int from = 1;
                if ((c == "git" || c == "docker") && t.Count > 1 && !t[1].StartsWith("-"))
                {
                    string sd; string key = c + " " + t[1];
                    if (c == "docker" && (t[1] == "container" || t[1] == "image") && t.Count > 2) { string alt = "docker " + (t[2] == "ls" ? (t[1] == "image" ? "images" : "ps") : t[2]); if (Sub.ContainsKey(alt)) key = alt; }
                    if (Sub.TryGetValue(key, out sd)) { desc = sd; head = key; from = key.Split(' ').Length; }
                    else head = key;
                }
                res.Add("<color=#DCDCAA>" + Esc(head) + "</color> — " + (desc ?? "команда, которой нет в справочнике игры (попробуй " + Esc(c) + " --help)"));
                // флаги
                string baseKey = head;
                for (int i = from; i < t.Count; i++)
                {
                    string a = t[i];
                    if (!a.StartsWith("-") || a == "-") continue;
                    string f;
                    if (Flag.TryGetValue(baseKey + " " + a, out f)) { res.Add("    <color=#9CDCFE>" + Esc(a) + "</color> — " + f); continue; }
                    if (!a.StartsWith("--") && a.Length > 2 && Letters.Contains(c))
                    {
                        var got = new List<string>();
                        foreach (char ch in a.Substring(1)) if (Flag.TryGetValue(baseKey + " -" + ch, out f)) got.Add("-" + ch + ": " + f);
                        if (got.Count > 0) res.Add("    <color=#9CDCFE>" + Esc(a) + "</color> — " + Esc(string.Join("; ", got.ToArray())));
                    }
                }
            }
            return res;
        }

        static string OpText(string op)
        {
            switch (op)
            {
                case "|": return "конвейер: вывод левой команды идёт на вход правой";
                case ">": return "записать вывод в файл (старое содержимое пропадёт)";
                case ">>": return "дописать вывод в конец файла";
                case "2>": return "ошибки — в файл";
                case "<": return "взять ввод из файла";
                case "&&": return "следующая команда — только если эта прошла успешно";
                case "||": return "следующая команда — только если эта упала";
                case ";": return "команды по очереди, независимо от результата";
                case "&": return "запустить в фоне";
                default: return "";
            }
        }

        // Разбить строку на команды и операторы (с учётом кавычек). Key — оператор, Value — команда
        public static List<KeyValuePair<string, string>> SplitOps(string line)
        {
            var res = new List<KeyValuePair<string, string>>();
            var cur = new StringBuilder(); char q = '\0';
            System.Action flush = () => { var s = cur.ToString().Trim(); if (s.Length > 0) res.Add(new KeyValuePair<string, string>(null, s)); cur.Length = 0; };
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q != '\0') { cur.Append(c); if (c == q) q = '\0'; else if (c == '\\' && q == '"' && i + 1 < line.Length) cur.Append(line[++i]); continue; }
                if (c == '\'' || c == '"') { q = c; cur.Append(c); continue; }
                if (c == '\\' && i + 1 < line.Length) { cur.Append(c).Append(line[++i]); continue; }
                string op = null;
                if (c == '|' || c == '&' || c == '>' || c == ';' || c == '<' || c == '\n')
                {
                    if (i + 1 < line.Length && ((c == '|' && line[i + 1] == '|') || (c == '&' && line[i + 1] == '&') || (c == '>' && line[i + 1] == '>'))) { op = line.Substring(i, 2); i++; }
                    else if (c == '>' && cur.Length > 0 && cur[cur.Length - 1] == '2' && (cur.Length == 1 || cur[cur.Length - 2] == ' ')) { cur.Length--; op = "2>"; }
                    else op = c == '\n' ? ";" : c.ToString();
                }
                if (op == null) { cur.Append(c); continue; }
                flush();
                res.Add(new KeyValuePair<string, string>(op, null));
                // после > и < идёт имя файла — это не команда
                if (op == ">" || op == ">>" || op == "2>" || op == "<")
                {
                    int j = i + 1; while (j < line.Length && line[j] == ' ') j++;
                    int k = j; while (k < line.Length && " |&;<>\n".IndexOf(line[k]) < 0) k++;
                    if (k > j) res[res.Count - 1] = new KeyValuePair<string, string>(op + " " + line.Substring(j, k - j), null);
                    i = k - 1;
                }
            }
            flush();
            return res;
        }

        static string Esc(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>"); }

        public static string OpOf(string key) { return key == null ? null : key.Split(' ')[0]; }
    }
}
