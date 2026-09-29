// Спринт 7 «Окружение»: задачи-сценарии (шаги с теорией, подсказкой и проверкой по настоящему состоянию)
// и фильтр Docker-команд игрока. Контент — Resources/Tasks/env.json. Проверки не трогают Unity и идут через
// IEnvRunner: в игре это песочница Docker и Docker на ПК, в selftest — подделка.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Intern.Game
{
    // ======================= модель =======================
    public class EnvCheckDef
    {
        public string kind = "sandbox";   // sandbox | host | http | last | file | ips | github
        public string cmd, url, path, expect, notExpect, refCmd, lastCmd;
        public string when;   // сама после команды — только если команда подходит под эту регулярку (github: не тратить лимит запросов)
        public string fail;   // что сказать, если шаг не выполнен

        // Копия с подставленными переменными ({gh} — ник игрока на GitHub)
        public EnvCheckDef Filled()
        {
            return new EnvCheckDef
            {
                kind = kind, cmd = EnvCheck.Fill(cmd), url = EnvCheck.Fill(url), path = EnvCheck.Fill(path), expect = EnvCheck.Fill(expect, true), notExpect = EnvCheck.Fill(notExpect, true),
                refCmd = EnvCheck.Fill(refCmd), lastCmd = EnvCheck.Fill(lastCmd, true), when = EnvCheck.Fill(when, true), fail = EnvCheck.Fill(fail),
            };
        }
    }

    public class EnvStep
    {
        public string title, text, hint, action, explain;
        public string solve;   // команда-решение: selftest и «Показать команду» на лёгкой сложности
        public EnvCheckDef check = new EnvCheckDef();
    }

    public class EnvScenario
    {
        public string id, title, sender, character, story, theory, setup;
        public string file;   // файл из /work, который открывается в редакторе IDE (например, site/index.html)
        public string view;   // что показывать в «Что произошло»: git:<папка> — граф коммитов, docker — контейнеры игры
        public bool mission;
        public int xp = 80, difficulty = 2;
        public List<EnvStep> steps = new List<EnvStep>();
    }

    public static class EnvContent
    {
        public const string MissionId = "env-setup";

        public static List<EnvScenario> Parse(string json)
        {
            var res = new List<EnvScenario>();
            var root = MiniJson.Parse(json) as Dictionary<string, object>;
            if (root == null) return res;
            object tl; if (!root.TryGetValue("tasks", out tl)) return res;
            foreach (var o in (tl as List<object>) ?? new List<object>())
            {
                var d = o as Dictionary<string, object>; if (d == null) continue;
                var s = new EnvScenario
                {
                    id = Str(d, "id"), title = Str(d, "title"), sender = Str(d, "sender") ?? "Тимлид Гена", character = Str(d, "character") ?? "teamlead",
                    story = Str(d, "story") ?? "", theory = Str(d, "theory") ?? "", setup = Str(d, "setup"),
                    file = Str(d, "file"), view = Str(d, "view"),
                    mission = Bool(d, "mission"), xp = Int(d, "xp", 80), difficulty = Int(d, "difficulty", 2),
                };
                object sl; d.TryGetValue("steps", out sl);
                foreach (var so in (sl as List<object>) ?? new List<object>())
                {
                    var sd = so as Dictionary<string, object>; if (sd == null) continue;
                    var st = new EnvStep { title = Str(sd, "title"), text = Str(sd, "text") ?? "", hint = Str(sd, "hint"), action = Str(sd, "action"), explain = Str(sd, "explain"), solve = Str(sd, "solve") };
                    object co; sd.TryGetValue("check", out co);
                    var cd = co as Dictionary<string, object>;
                    if (cd != null)
                        st.check = new EnvCheckDef
                        {
                            kind = Str(cd, "kind") ?? "sandbox", cmd = Str(cd, "cmd"), url = Str(cd, "url"), path = Str(cd, "path"),
                            expect = Str(cd, "expect"), notExpect = Str(cd, "not_expect") ?? Str(cd, "notExpect"),
                            refCmd = Str(cd, "ref_cmd") ?? Str(cd, "refCmd"), lastCmd = Str(cd, "last_cmd") ?? Str(cd, "lastCmd"),
                            when = Str(cd, "when"), fail = Str(cd, "fail"),
                        };
                    s.steps.Add(st);
                }
                if (!string.IsNullOrEmpty(s.id) && s.steps.Count > 0) res.Add(s);
            }
            return res;
        }

        // Задача для IDE: тикет со списком шагов (сами шаги показывает IDE)
        public static TaskData ToTask(EnvScenario s, int index)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(s.title).Append("\n\n");
            foreach (var p in (s.story ?? "").Split('\n')) sb.Append(p).Append('\n');
            sb.Append("\n## Шаги\n\n");
            for (int i = 0; i < s.steps.Count; i++) sb.Append(i + 1).Append(". ").Append(s.steps[i].title).Append('\n');
            sb.Append("\nКоманды — во вкладке «ТЕРМИНАЛ» внизу. Проверка — «Проверить» (Ctrl+Enter) или сама после каждой команды.\n");
            return new TaskData
            {
                id = s.id, key = (s.mission ? "ENV-0" : "ENV-" + index), title = s.title, type = "scenario", language = LangOf(s.file), scenario = s,
                sender = s.sender, character = s.character, story = s.story, theory = s.theory, goal = s.steps[0].title,
                starter = sb.ToString(), chapter = "Окружение", track = "env", topic = "env", grade = "junior",
                xp = s.xp, reward = s.xp, difficulty = s.difficulty, deadline = (6 + 2 * s.difficulty) * 60,
            };
        }

        // Язык редактора по расширению файла сценария
        public static string LangOf(string file)
        {
            if (string.IsNullOrEmpty(file)) return "text";
            var f = file.ToLowerInvariant();
            if (f.EndsWith(".py")) return "python";
            if (f.EndsWith(".js")) return "javascript";
            if (f.EndsWith(".html") || f.EndsWith(".htm")) return "html";
            if (f.EndsWith(".css")) return "css";
            if (f.EndsWith(".sh")) return "bash";
            if (f.EndsWith(".yml") || f.EndsWith(".yaml")) return "yaml";
            if (f.EndsWith(".sql")) return "sql";
            if (f.EndsWith("dockerfile")) return "dockerfile";
            if (f.EndsWith(".conf")) return "nginx";
            return "text";
        }

        static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null; }
        static int Int(Dictionary<string, object> d, string k, int def) { object v; if (!d.TryGetValue(k, out v) || v == null) return def; try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; } }
        static bool Bool(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
    }

    // ======================= проверки шагов =======================
    public interface IEnvRunner
    {
        ProcResult Sandbox(string command);      // bash в песочнице (cwd /work)
        ProcResult Host(string dockerArgs);      // docker на ПК игрока
        string Http(string url, out int status); // GET, тело ответа (null — не ответил)
        string HostPath(string sandboxPath);     // /work/x → путь на ПК
    }

    public class ShellRecord { public string cmd = "", output = ""; public int code; }

    public static class EnvCheck
    {
        static readonly Regex Ip = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b");

        // ---------- переменные сценариев: {gh} — ник на GitHub (спринт 11 «Свой форк») ----------
        public const string Upstream = "GureganHokex/stazher";
        public static readonly Dictionary<string, string> Vars = new Dictionary<string, string>();
        public static readonly Regex GhNick = new Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}$");
        public static string Gh { get { string v; return Vars.TryGetValue("gh", out v) ? v ?? "" : ""; } }

        // {gh} → ник (в регулярке — с экранированием); ника нет — остаётся «{gh}»
        public static string Fill(string s, bool regex = false)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('{') < 0) return s;
            foreach (var kv in Vars)
                if (!string.IsNullOrEmpty(kv.Value)) s = s.Replace("{" + kv.Key + "}", regex ? Regex.Escape(kv.Value) : kv.Value);
            return s;
        }
        // Для игрока: {gh} → ник или «<твой ник>»
        public static string Show(string s) { s = Fill(s); return string.IsNullOrEmpty(s) ? s : s.Replace("{gh}", "<твой ник>"); }
        public static bool NeedsGh(EnvCheckDef c)
        {
            foreach (var f in new[] { c.cmd, c.url, c.path, c.expect, c.notExpect, c.refCmd, c.lastCmd }) if (f != null && f.Contains("{gh}")) return true;
            return false;
        }

        public const string NoNick = "Сначала скажи игре свой ник на GitHub: stazher github <ник>";

        public static bool Evaluate(EnvCheckDef c, IEnvRunner run, ShellRecord last, out string note)
        {
            note = null;
            if (NeedsGh(c) && Gh.Length == 0) { note = NoNick; return false; }
            c = c.Filled();
            string text;
            switch (c.kind)
            {
                case "github":
                    {
                        // публичный API GitHub без входа (60 запросов в час с одного IP), запрос — curl из песочницы
                        var r = run.Sandbox("curl -s -m 15 -H 'Accept: application/vnd.github+json' -H 'User-Agent: stazher-game' " + Quote("https://api.github.com/" + (c.url ?? "").TrimStart('/')));
                        text = r.Out ?? "";
                        if (text.Trim().Length == 0) { note = "GitHub не ответил. Проверь интернет и нажми «Проверить шаг» ещё раз."; return false; }
                        if (text.Contains("API rate limit exceeded")) { note = "GitHub ограничил проверки без входа: 60 запросов в час с одного компьютера. Подожди немного и нажми «Проверить шаг»."; return false; }
                        if (Regex.IsMatch(text, "\"message\"\\s*:\\s*\"Not Found\"")) { note = c.fail ?? "GitHub такого не нашёл."; return false; }
                        break;
                    }
                case "host":
                    {
                        var r = run.Host(c.cmd ?? "");
                        if (r.NotFound) { note = "Docker не найден: команда docker не запускается."; return false; }
                        text = r.Ok ? r.Out : "";
                        break;
                    }
                case "http":
                    {
                        int status; text = run.Http(c.url ?? "http://localhost:8080/", out status);
                        if (text == null) { note = "По адресу " + c.url + " никто не отвечает."; return false; }
                        break;
                    }
                case "file":
                    {
                        text = run.Sandbox("cat " + Quote(c.path ?? "")).Out;
                        break;
                    }
                case "last":
                    {
                        if (last == null || string.IsNullOrEmpty(last.cmd)) { note = "Сначала выполни команду в терминале."; return false; }
                        if (!string.IsNullOrEmpty(c.lastCmd) && !Regex.IsMatch(last.cmd.Trim(), c.lastCmd)) { note = "Последняя команда — не та, что просили в этом шаге."; return false; }
                        text = last.output ?? "";
                        break;
                    }
                case "ips":
                    {
                        // вывод последней команды игрока — те же IP в том же порядке, что у эталонной команды
                        if (last == null || string.IsNullOrEmpty(last.cmd)) { note = "Сначала выполни команду в терминале."; return false; }
                        var want = Ip.Matches(run.Sandbox(c.refCmd ?? "").Out).Cast<Match>().Select(m => m.Value).ToList();
                        var got = Ip.Matches(last.output ?? "").Cast<Match>().Select(m => m.Value).ToList();
                        if (want.Count == 0) { note = "Не удалось посчитать эталон — лог на месте?"; return false; }
                        if (got.Count != want.Count || !got.SequenceEqual(want))
                        {
                            note = got.Count == 0 ? "В выводе последней команды нет IP-адресов." :
                                   got.Count != want.Count ? "Нужно ровно " + want.Count + " IP, а в выводе " + got.Count + "." : "IP не те или не в том порядке: сначала самые частые.";
                            return false;
                        }
                        return true;
                    }
                default:
                    text = run.Sandbox(c.cmd ?? "true").Out;
                    break;
            }
            text = (text ?? "").Replace("\r", "");
            if (!string.IsNullOrEmpty(c.expect) && !Regex.IsMatch(text, c.expect, RegexOptions.Multiline)) { note = note ?? c.fail ?? "Пока не выполнено."; return false; }
            if (!string.IsNullOrEmpty(c.notExpect) && Regex.IsMatch(text, c.notExpect, RegexOptions.Multiline)) { note = note ?? c.fail ?? "Пока не выполнено."; return false; }
            return true;
        }

        public static string Quote(string s) { return "'" + (s ?? "").Replace("'", "'\\''") + "'"; }
    }

    // ======================= фильтр Docker-команд =======================
    // Команды игрока, начинающиеся с docker, идут в настоящий Docker на ПК. Пропускаем только безопасное:
    // белый список подкоманд, никаких --privileged, host-сетей, docker.sock и папок вне work; порты — только
    // на 127.0.0.1; всё создаваемое — с меткой stazher=1, удалять и останавливать можно только своё.
    public class DockerPlan
    {
        public List<string> Args = new List<string>();   // без слова docker
        public string Error;                             // не null — команда запрещена, текст для игрока
        public List<string> Targets = new List<string>(); // контейнеры, которые должны быть нашими (stop, rm, exec…)
        public List<string> ImageTargets = new List<string>();
        public string PulledImage;                      // pull/run — образ, который игрок теперь «владеет»
        public string ArgLine { get { return string.Join(" ", Args.Select(QuoteArg).ToArray()); } }

        static string QuoteArg(string a)
        {
            if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '"', '\t' }) < 0) return a;
            return "\"" + a.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    public static class DockerGuard
    {
        static readonly HashSet<string> Top = new HashSet<string> { "run", "create", "ps", "images", "pull", "stop", "start", "restart", "kill", "rm", "rmi", "logs", "exec", "build", "inspect", "port", "version", "info", "tag", "stats", "top", "network", "volume", "container", "image", "history", "diff" };
        static readonly Dictionary<string, string[]> Sub = new Dictionary<string, string[]>
        {
            { "network", new[] { "ls", "create", "rm", "inspect" } },
            { "volume", new[] { "ls", "create", "rm", "inspect" } },
            { "container", new[] { "ls", "rm", "stop", "start", "restart", "inspect", "logs", "kill", "run", "create", "exec", "port", "top", "diff" } },
            { "image", new[] { "ls", "pull", "rm", "inspect", "build", "history", "tag" } },
        };
        static readonly HashSet<string> TargetsContainers = new HashSet<string> { "stop", "start", "restart", "kill", "rm", "logs", "exec", "port", "top", "diff" };
        static readonly HashSet<string> Shells = new HashSet<string> { "sh", "bash", "ash", "zsh", "/bin/sh", "/bin/bash" };
        static readonly string[] Banned = { "--privileged", "--pid", "--ipc", "--uts", "--userns", "--cap-add", "--security-opt", "--device", "--cgroup-parent", "--volumes-from", "-H", "--host", "--context", "--config", "--add-host", "--runtime", "--gpus" };

        // Разбор строки как в bash: пробелы, '…', "…", \ — без подстановок
        public static List<string> Tokenize(string line)
        {
            var res = new List<string>(); var cur = new StringBuilder(); bool any = false; char q = '\0';
            for (int i = 0; i < (line ?? "").Length; i++)
            {
                char c = line[i];
                if (q == '\'') { if (c == '\'') q = '\0'; else cur.Append(c); continue; }
                if (q == '"') { if (c == '"') q = '\0'; else if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\')) cur.Append(line[++i]); else cur.Append(c); continue; }
                if (c == '\'' || c == '"') { q = c; any = true; continue; }
                if (c == '\\' && i + 1 < line.Length) { cur.Append(line[++i]); any = true; continue; }
                if (char.IsWhiteSpace(c)) { if (any || cur.Length > 0) { res.Add(cur.ToString()); cur.Length = 0; any = false; } continue; }
                cur.Append(c);
            }
            if (any || cur.Length > 0) res.Add(cur.ToString());
            return res;
        }

        // Путь песочницы (/work/…, ./…, $(pwd)/…) → путь на ПК. null — вне work
        public static string MapPath(string p, string cwd, string hostWork)
        {
            if (string.IsNullOrEmpty(p)) return null;
            p = p.Replace("$(pwd)", cwd).Replace("${PWD}", cwd).Replace("$PWD", cwd);
            if (p == "." || p.StartsWith("./") || !p.StartsWith("/")) p = (cwd.TrimEnd('/') + "/" + (p == "." ? "" : p.StartsWith("./") ? p.Substring(2) : p)).TrimEnd('/');
            var parts = new List<string>();
            foreach (var seg in p.Split('/'))
            {
                if (seg.Length == 0 || seg == ".") continue;
                if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(seg);
            }
            if (parts.Count == 0 || parts[0] != "work") return null;
            var rest = string.Join("\\", parts.Skip(1).ToArray());
            return rest.Length == 0 ? hostWork : hostWork.TrimEnd('\\', '/') + "\\" + rest;
        }

        static bool IsNamedVolume(string s) { return Regex.IsMatch(s, @"^[A-Za-z0-9][A-Za-z0-9_.-]*$"); }

        public static DockerPlan Plan(string line, string cwd, string hostWork)
        {
            var plan = new DockerPlan();
            var t = Tokenize(line);
            if (t.Count == 0 || t[0] != "docker") { plan.Error = "Это не docker-команда."; return plan; }
            if (t.Count == 1) { plan.Args.Add("--help"); return plan; }
            string sub = t[1], sub2 = null;
            if (!Top.Contains(sub)) { plan.Error = "Команда «docker " + sub + "» в игре не разрешена. Можно: run, ps, images, pull, logs, exec, stop, rm, build, inspect, network, volume и похожие."; return plan; }
            int argStart = 2;
            if (Sub.ContainsKey(sub))
            {
                if (t.Count < 3) { plan.Error = "Нужна подкоманда: docker " + sub + " " + string.Join("|", Sub[sub]); return plan; }
                sub2 = t[2]; argStart = 3;
                if (Array.IndexOf(Sub[sub], sub2) < 0) { plan.Error = "«docker " + sub + " " + sub2 + "» в игре не разрешена (prune — только кнопкой «Убрать за собой»)."; return plan; }
            }
            // действие в терминах верхнего уровня
            string act = sub;
            if (sub == "container") act = sub2 == "ls" ? "ps" : sub2;
            else if (sub == "image") act = sub2 == "ls" ? "images" : sub2 == "rm" ? "rmi" : sub2;
            else if (sub == "network" || sub == "volume") act = sub + " " + sub2;

            foreach (var a in t.Skip(2))
            {
                if (a.Contains("docker.sock") || a.Contains("docker_engine") || a.Contains("dockerDesktop")) { plan.Error = "Доступ к самому Docker (docker.sock) из контейнера запрещён — это ключ от всего компьютера."; return plan; }
                foreach (var b in Banned)
                    if (a == b || a.StartsWith(b + "=")) { plan.Error = "Флаг " + b + " в игре запрещён: он даёт контейнеру доступ к компьютеру за пределами песочницы."; return plan; }
            }

            plan.Args.Add(sub); if (sub2 != null) plan.Args.Add(sub2);
            bool creates = act == "run" || act == "create" || act == "build" || act == "network create" || act == "volume create";
            if (creates) { plan.Args.Add("--label"); plan.Args.Add(DevEnv.Label); }

            var rest = t.Skip(argStart).ToList();
            bool detached = false, interactive = false; int positional = 0; string image = null;
            bool runLike = act == "run" || act == "create";
            for (int i = 0; i < rest.Count; i++)
            {
                string a = rest[i];
                string next = i + 1 < rest.Count ? rest[i + 1] : null;
                if (runLike && image == null)
                {
                    if (a == "-d" || a == "--detach") { detached = true; plan.Args.Add(a); continue; }
                    if (a == "-it" || a == "-ti" || a == "-t" || a == "--tty" || a == "-dit" || a == "-itd") { interactive = true; if (a.Contains("d")) { detached = true; plan.Args.Add("-d"); } continue; }
                    if (a == "-i" || a == "--interactive") { continue; }
                    if (a == "--network" || a == "--net") { if (next == "host") { plan.Error = "Сеть host в игре запрещена: контейнер видел бы всю сеть компьютера. Используй -p для портов."; return plan; } plan.Args.Add(a); plan.Args.Add(next ?? ""); i++; continue; }
                    if (a.StartsWith("--network=") || a.StartsWith("--net=")) { if (a.EndsWith("=host")) { plan.Error = "Сеть host в игре запрещена."; return plan; } plan.Args.Add(a); continue; }
                    if (a == "-P" || a == "--publish-all") { plan.Error = "-P открывает порты для всей сети. Укажи порт явно: -p 8080:80."; return plan; }
                    if (a == "-p" || a == "--publish" || a.StartsWith("--publish=") || (a.StartsWith("-p") && a.Length > 2))
                    {
                        string v = a == "-p" || a == "--publish" ? next : a.StartsWith("--publish=") ? a.Substring(10) : a.Substring(2);
                        if (v == null) { plan.Error = "После -p нужен порт: -p 8080:80."; return plan; }
                        var pv = LocalPort(v); if (pv == null) { plan.Error = "Не понял порт «" + v + "». Пример: -p 8080:80."; return plan; }
                        plan.Args.Add("-p"); plan.Args.Add(pv);
                        if (a == "-p" || a == "--publish") i++;
                        continue;
                    }
                    if (a == "-v" || a == "--volume" || a.StartsWith("--volume=") || (a.StartsWith("-v") && a.Length > 2 && a[2] != '-'))
                    {
                        string v = a == "-v" || a == "--volume" ? next : a.StartsWith("--volume=") ? a.Substring(9) : a.Substring(2);
                        if (v == null) { plan.Error = "После -v нужен том: -v /work/site:/usr/share/nginx/html:ro."; return plan; }
                        string err; var mv = MapVolume(v, cwd, hostWork, out err); if (mv == null) { plan.Error = err; return plan; }
                        plan.Args.Add("-v"); plan.Args.Add(mv);
                        if (a == "-v" || a == "--volume") i++;
                        continue;
                    }
                    if (a == "--mount" || a.StartsWith("--mount="))
                    {
                        string v = a == "--mount" ? next : a.Substring(8);
                        string err; var mv = MapMount(v, cwd, hostWork, out err); if (mv == null) { plan.Error = err; return plan; }
                        plan.Args.Add("--mount"); plan.Args.Add(mv);
                        if (a == "--mount") i++;
                        continue;
                    }
                    if (a.StartsWith("-"))
                    {
                        plan.Args.Add(a);
                        // флаги со значением: --name x, -e K=V, -w dir, --restart no, -l k=v, --entrypoint x, -u user, -m 256m
                        if (!a.Contains("=") && (a == "--name" || a == "-e" || a == "--env" || a == "-w" || a == "--workdir" || a == "--restart" || a == "-l" || a == "--label" || a == "--entrypoint" || a == "-u" || a == "--user" || a == "-m" || a == "--memory" || a == "--cpus" || a == "--hostname" || a == "-h" || a == "--env-file"))
                        { plan.Args.Add(next ?? ""); i++; }
                        continue;
                    }
                    image = a; plan.Args.Add(a); plan.PulledImage = a; continue;
                }
                if (act == "build")
                {
                    if ((a == "-f" || a == "--file") && next != null)
                    {
                        var mp = MapPath(next, cwd, hostWork); if (mp == null) { plan.Error = "Dockerfile должен лежать в папке /work."; return plan; }
                        plan.Args.Add(a); plan.Args.Add(mp); i++; continue;
                    }
                    if (a == "-t" || a == "--tag") { plan.Args.Add(a); plan.Args.Add(next ?? ""); i++; continue; }
                    if (!a.StartsWith("-"))
                    {
                        var mp = MapPath(a, cwd, hostWork); if (mp == null) { plan.Error = "Собирать образ можно только из папки внутри /work (например, docker build -t site ./site)."; return plan; }
                        plan.Args.Add(mp); positional++; continue;
                    }
                    plan.Args.Add(a); continue;
                }
                if (act == "cp") { plan.Error = "docker cp в игре не нужен: папка /work уже общая."; return plan; }
                if (!a.StartsWith("-"))
                {
                    if (TargetsContainers.Contains(act))
                    {
                        // exec: первый позиционный — контейнер, дальше команда; остальные — список контейнеров
                        if (act == "exec" && positional > 0) { plan.Args.Add(a); positional++; continue; }
                        plan.Targets.Add(a);
                    }
                    else if (act == "rmi") plan.ImageTargets.Add(a);
                    else if (act == "pull") plan.PulledImage = a;
                    positional++;
                }
                else if (act == "exec" && (a == "-it" || a == "-ti" || a == "-t")) { interactive = positional == 0; continue; }
                else if (act == "exec" && a == "-i") continue;
                plan.Args.Add(a);
            }
            if (runLike && image == null) { plan.Error = "Не указан образ. Пример: docker run -d --name site -p 8080:80 nginx:alpine"; return plan; }
            // песочницу самого терминала не останавливаем и не удаляем командами: иначе терминалу негде работать
            if ((act == "stop" || act == "kill" || act == "rm" || act == "restart") && plan.Targets.Contains(DevEnv.Container))
            { plan.Error = DevEnv.Container + " — песочница, в которой работает этот терминал. Её не трогаем: убрать всё — кнопкой «Удалить всё, что создала игра» справа."; return plan; }
            if (act == "rmi" && plan.ImageTargets.Any(x => x == DevEnv.Image || x == DevEnv.Image.Split(':')[0]))
            { plan.Error = "Это образ песочницы терминала — удалить его можно только кнопкой «Удалить всё, что создала игра»."; return plan; }
            if (runLike && interactive && !detached && image != null && rest.IndexOf(image) == rest.Count - 1)
            { plan.Error = "Интерактивный режим (-it) в этом терминале не работает. Запусти с -d или сразу с командой: docker run --rm alpine echo привет"; return plan; }
            if (act == "exec" && positional < 2) { plan.Error = "Нужна команда: docker exec site ls /usr/share/nginx/html. Интерактивный shell (-it sh) здесь не открыть."; return plan; }
            if (act == "exec" && interactive)
            {
                var pos = rest.Where(x => !x.StartsWith("-")).ToList();
                if (pos.Count == 2 && Shells.Contains(pos[1])) { plan.Error = "Интерактивный shell (docker exec -it … " + pos[1] + ") в этом терминале не открыть. Выполни команду сразу: docker exec site ls /usr/share/nginx/html"; return plan; }
            }
            return plan;
        }

        // -p 8080:80 → 127.0.0.1:8080:80 (наружу, в сеть, порт не открываем)
        public static string LocalPort(string v)
        {
            var m = Regex.Match(v, @"^(?:(?<ip>\d{1,3}(?:\.\d{1,3}){3}|localhost):)?(?<h>\d{1,5})(?::(?<c>\d{1,5}))?(?<proto>/(tcp|udp))?$");
            if (!m.Success) return null;
            string c = m.Groups["c"].Success ? m.Groups["c"].Value : null;
            if (c == null) return null;   // -p 80 (случайный порт) — не поддерживаем: игрок должен знать, куда стучаться
            return "127.0.0.1:" + m.Groups["h"].Value + ":" + c + m.Groups["proto"].Value;
        }

        static string MapVolume(string v, string cwd, string hostWork, out string err)
        {
            err = null;
            // src:dst[:mode]
            var parts = v.Split(':');
            if (parts.Length < 2 || parts.Length > 3) { err = "Том пишется так: -v /work/папка:/путь/в/контейнере[:ro]."; return null; }
            string src = parts[0], dst = parts[1];
            if (!dst.StartsWith("/")) { err = "Путь в контейнере должен начинаться с /."; return null; }
            if (IsNamedVolume(src)) return v;
            var mp = MapPath(src, cwd, hostWork);
            if (mp == null) { err = "Смонтировать можно только папку внутри /work (твою рабочую папку) или именованный том."; return null; }
            return mp + ":" + dst + (parts.Length == 3 ? ":" + parts[2] : "");
        }

        static string MapMount(string v, string cwd, string hostWork, out string err)
        {
            err = null;
            if (v == null) { err = "После --mount нужны параметры."; return null; }
            var kv = v.Split(',').Select(p => p.Split(new[] { '=' }, 2)).ToList();
            var type = kv.FirstOrDefault(p => p[0] == "type"); string t = type != null && type.Length > 1 ? type[1] : "volume";
            if (t == "volume" || t == "tmpfs") return v;
            if (t != "bind") { err = "Тип монтирования " + t + " в игре не разрешён."; return null; }
            var outParts = new List<string>();
            foreach (var p in kv)
            {
                if ((p[0] == "source" || p[0] == "src") && p.Length > 1)
                {
                    var mp = MapPath(p[1], cwd, hostWork); if (mp == null) { err = "bind-монтирование — только из папки /work."; return null; }
                    outParts.Add(p[0] + "=" + mp);
                }
                else outParts.Add(string.Join("=", p));
            }
            return string.Join(",", outParts.ToArray());
        }
    }
}
