// Спринт 7 «Окружение»: терминал IDE. Обычные команды выполняются bash-ем в песочнице (docker exec, скрипт
// stazher-run из образа запоминает папку и код выхода), команды docker — на ПК игрока через DockerGuard.
// Всё медленное идёт в фоновом потоке; вывод и результаты копятся в очереди и забираются Pump() в кадре.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Intern.Game
{
    public class Shell : IEnvRunner
    {
        public const int CommandTimeoutMs = 180000;

        public volatile EnvState State = EnvState.Unknown;
        public volatile string StateText = "Окружение ещё не проверено.";
        public volatile bool Probing, ContainerUp;
        public bool Busy { get { return busy; } }
        public string Cwd = "/work";
        public readonly List<string> History = new List<string>();
        public ShellRecord Last = new ShellRecord();
        public Func<string, bool> OwnsImage = s => false;   // образы, скачанные в игре
        public Action<string> ImagePulled;
        public Action<ShellRecord> Finished;                // команда игрока закончилась (в главном потоке)

        volatile bool busy;
        readonly object qLock = new object();
        readonly Queue<KeyValuePair<int, string>> outQ = new Queue<KeyValuePair<int, string>>();   // 0 — вывод, 1 — ошибка, 2 — служебное
        readonly Queue<Action> mainQ = new Queue<Action>();
        Process proc; string jobId; volatile bool cancelled;
        int jobNo;

        public static readonly Regex Marker = new Regex(@"^__STAZHER__(-?\d+)\|(.*)$");
        static readonly HashSet<string> Interactive = new HashSet<string> { "vim", "vi", "nano", "less", "more", "top", "htop", "man", "ssh", "emacs", "mc", "watch" };

        // ---------- очередь в главный поток ----------
        void Emit(int kind, string text) { lock (qLock) outQ.Enqueue(new KeyValuePair<int, string>(kind, text)); }
        void Main(Action a) { lock (qLock) mainQ.Enqueue(a); }

        // Забрать вывод (kind, текст) и выполнить отложенные действия; вызывать каждый кадр
        public List<KeyValuePair<int, string>> Pump()
        {
            List<KeyValuePair<int, string>> res; List<Action> acts;
            lock (qLock)
            {
                res = new List<KeyValuePair<int, string>>(outQ); outQ.Clear();
                acts = new List<Action>(mainQ); mainQ.Clear();
            }
            foreach (var a in acts) { try { a(); } catch (Exception e) { UnityEngine.Debug.LogException(e); } }
            return res;
        }

        static void Bg(Action a) { new Thread(() => { try { a(); } catch (Exception e) { UnityEngine.Debug.LogWarning("[Стажёр] Терминал: " + e.Message); } }) { IsBackground = true }.Start(); }

        // ---------- окружение ----------
        public void Probe(Action<EnvState> done = null)
        {
            if (Probing) return;
            Probing = true;
            Bg(() =>
            {
                string detail; var st = DevEnv.Probe(out detail);
                bool up = false;
                if (st == EnvState.Ready)
                {
                    var r = DevEnv.DockerCmd("inspect --format \"{{.State.Running}}\" " + DevEnv.Container, 10000);
                    up = r.Ok && r.Out.Trim() == "true";
                }
                State = st; StateText = detail; ContainerUp = up; Probing = false;
                if (done != null) Main(() => done(st));
            });
        }

        public bool CanRunCommands { get { return State == EnvState.Ready && ContainerUp; } }

        // Собрать образ песочницы: вывод docker build — прямо в терминал
        public void BuildImage(string dockerfile, Action<bool> done)
        {
            if (busy) return;
            busy = true;
            Emit(2, "docker build -t " + DevEnv.Image + " — собираю песочницу. Первый раз это 2–5 минут: скачивается Ubuntu, Python и Node.");
            Bg(() =>
            {
                var r = RunStream(DevEnv.Docker, "build --progress=plain --label " + DevEnv.Label + " -t " + DevEnv.Image + " -", 30 * 60 * 1000, dockerfile, false);
                busy = false;
                Emit(r.Ok ? 2 : 1, r.Ok ? "Песочница собрана." : "Сборка не удалась (код " + r.Code + "). Проверь интернет и что Docker Desktop запущен.");
                Probe();
                Main(() => { if (done != null) done(r.Ok); });
            });
        }

        public void StartContainer(Action<bool> done)
        {
            if (busy) return;
            busy = true;
            Emit(2, "Запускаю песочницу " + DevEnv.Container + " (папка " + DevEnv.WorkDir + " → /work)…");
            Bg(() =>
            {
                string err; bool ok = DevEnv.EnsureContainer(out err);
                busy = false; ContainerUp = ok;
                Emit(ok ? 2 : 1, ok ? "Песочница работает. Команды — сюда, в эту строку." : "Не запустилась: " + err);
                Main(() => { if (done != null) done(ok); });
            });
        }

        public static bool StartDockerDesktop()
        {
            foreach (var p in new[] { @"C:\Program Files\Docker\Docker\Docker Desktop.exe", Environment.ExpandEnvironmentVariables(@"%LOCALAPPDATA%\Docker\Docker Desktop.exe") })
                if (File.Exists(p)) { try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); return true; } catch (Exception) { } }
            return false;
        }

        // ---------- команды игрока ----------
        public void Submit(string line)
        {
            line = (line ?? "").Trim();
            if (line.Length == 0 || busy) return;
            if (History.Count == 0 || History[History.Count - 1] != line) History.Add(line);
            if (History.Count > 200) History.RemoveAt(0);
            var first = line.Split(' ')[0];
            if (Interactive.Contains(first) || ((first == "python" || first == "python3" || first == "node" || first == "bash" || first == "sh") && line == first))
            {
                Emit(1, first + " — интерактивная программа, в этом терминале она не откроется. Файлы правь в IDE (они в той же папке /work), а скрипты запускай с именем файла: python3 main.py.");
                Finish(line, "", 1);
                return;
            }
            // цепочка через && || ; — команды docker идут на ПК, остальные — в песочницу, по очереди
            var chain = SplitChain(line);
            bool hasDocker = chain.Any(c => IsDocker(c.Value));
            if (!hasDocker) chain = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", line) };
            foreach (var c in chain)
                if (!IsDocker(c.Value) && Regex.IsMatch(c.Value, @"\|\s*docker\b"))
                { Emit(1, "docker в середине конвейера здесь не работает: docker должен стоять первым (docker logs site | grep GET)."); Finish(line, "", 1); return; }
            if (hasDocker && (State == EnvState.NoDocker || State == EnvState.DockerStopped)) { Emit(1, "Docker не запущен: " + StateText); Finish(line, "", 1); return; }
            if (chain.Any(c => !IsDocker(c.Value) || PipeAt(c.Value) >= 0) && !CanRunCommands)
            { Emit(1, "Песочница не запущена. Пройди миссию «Настрой окружение» — она в проводнике IDE, раздел «ОКРУЖЕНИЕ»."); Finish(line, "", 1); return; }
            RunChain(line, chain);
        }

        void Finish(string cmd, string output, int code)
        {
            var rec = new ShellRecord { cmd = cmd, output = output, code = code };
            Main(() => { Last = rec; if (Finished != null) Finished(rec); });
        }

        static bool IsDocker(string seg) { var t = (seg ?? "").TrimStart(); return t == "docker" || t.StartsWith("docker ") || t.StartsWith("docker\t"); }

        // Строка → команды с оператором перед каждой ("", "&&", "||", ";"); кавычки и \ учитываются, | и & остаются внутри команды
        public static List<KeyValuePair<string, string>> SplitChain(string line)
        {
            var res = new List<KeyValuePair<string, string>>();
            var cur = new StringBuilder(); string op = ""; char q = '\0';
            line = line ?? "";
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q != '\0') { cur.Append(c); if (c == q) q = '\0'; else if (c == '\\' && q == '"' && i + 1 < line.Length) cur.Append(line[++i]); continue; }
                if (c == '\'' || c == '"') { q = c; cur.Append(c); continue; }
                if (c == '\\' && i + 1 < line.Length) { cur.Append(c).Append(line[++i]); continue; }
                string next = null;
                if ((c == '&' || c == '|') && i + 1 < line.Length && line[i + 1] == c) { next = new string(c, 2); i++; }
                else if (c == ';' || c == '\n') next = ";";
                if (next == null) { cur.Append(c); continue; }
                if (cur.ToString().Trim().Length > 0) res.Add(new KeyValuePair<string, string>(op, cur.ToString().Trim()));
                cur.Length = 0; op = next;
            }
            if (cur.ToString().Trim().Length > 0) res.Add(new KeyValuePair<string, string>(op, cur.ToString().Trim()));
            return res;
        }

        // Позиция первого | конвейера (не ||) вне кавычек, −1 — нет
        public static int PipeAt(string seg)
        {
            char q = '\0';
            for (int i = 0; i < seg.Length; i++)
            {
                char c = seg[i];
                if (q != '\0') { if (c == q) q = '\0'; else if (c == '\\' && q == '"') i++; continue; }
                if (c == '\'' || c == '"') { q = c; continue; }
                if (c == '\\') { i++; continue; }
                if (c == '|' && (i + 1 >= seg.Length || seg[i + 1] != '|') && (i == 0 || seg[i - 1] != '|')) return i;
            }
            return -1;
        }

        void RunChain(string line, List<KeyValuePair<string, string>> chain)
        {
            busy = true; cancelled = false;
            string cwd0 = Cwd;
            Bg(() =>
            {
                var sb = new StringBuilder(); int code = 0; string cwd = cwd0;
                foreach (var kv in chain)
                {
                    if (cancelled) { code = 130; break; }
                    if (kv.Key == "&&" && code != 0) continue;
                    if (kv.Key == "||" && code == 0) continue;
                    code = IsDocker(kv.Value) ? DockerSegment(kv.Value, ref cwd, sb) : SandboxSegment(kv.Value, ref cwd, sb);
                }
                if (cancelled) Emit(1, "^C");
                if (cwd != cwd0 && cwd.StartsWith("/")) { var nc = cwd; Main(() => Cwd = nc); }
                busy = false; proc = null; jobId = null;
                Finish(line, sb.ToString(), code);
            });
        }

        // bash в песочнице (вызывается в фоне): stazher-run запоминает папку и код выхода
        int SandboxSegment(string cmd, ref string cwd, StringBuilder sb)
        {
            string id = (++jobNo).ToString() + "_" + Environment.TickCount;
            jobId = id;
            int code = -1; string newCwd = null; bool heldEmpty = false;
            var r = RunStream(DevEnv.Docker, "exec -i " + DevEnv.Container + " stazher-run " + id + " " + DevEnv.ShellQuote(cwd), CommandTimeoutMs, cmd, true,
                l =>
                {
                    var m = Marker.Match(l);
                    if (m.Success) { code = int.Parse(m.Groups[1].Value); newCwd = m.Groups[2].Value; heldEmpty = false; return false; }
                    // пустую строку придерживаем: если за ней маркер — это служебный перевод строки, не показываем
                    if (l.Length == 0 && !heldEmpty) { heldEmpty = true; return false; }
                    if (heldEmpty) { heldEmpty = false; Emit(0, ""); sb.Append('\n'); }
                    sb.Append(l).Append('\n'); return true;
                },
                l => { sb.Append(l).Append('\n'); return true; });
            if (r.TimedOut) Emit(1, "Команда шла дольше " + (CommandTimeoutMs / 1000) + " с и была остановлена.");
            if (newCwd != null && newCwd.StartsWith("/")) cwd = newCwd;
            jobId = null;
            return code >= 0 ? code : (r.Code != 0 ? r.Code : 1);
        }

        // docker на ПК через фильтр (вызывается в фоне); «docker … | grep …» — вывод docker уходит в песочницу
        int DockerSegment(string seg, ref string cwd, StringBuilder sb)
        {
            int pipe = PipeAt(seg);
            string dockerPart = pipe >= 0 ? seg.Substring(0, pipe).Trim() : seg, tail = pipe >= 0 ? seg.Substring(pipe + 1).Trim() : null;
            var plan = DockerGuard.Plan(dockerPart, cwd, DevEnv.WorkDir);
            if (plan.Error != null) { Emit(1, plan.Error); sb.Append(plan.Error).Append('\n'); return 1; }
            // трогать можно только своё: контейнеры с меткой stazher и образы, скачанные или собранные в игре
            foreach (var c in plan.Targets)
            {
                var r = DevEnv.DockerCmd("inspect --format \"{{index .Config.Labels \\\"stazher\\\"}}\" " + c, 10000);
                if (r.Ok && r.Out.Trim() != "1") { var msg = "Контейнер «" + c + "» создан не в игре — его не трогаем."; Emit(1, msg); sb.Append(msg).Append('\n'); return 1; }
            }
            foreach (var im in plan.ImageTargets)
            {
                if (OwnsImage(im)) continue;
                var r = DevEnv.DockerCmd("image inspect --format \"{{index .Config.Labels \\\"stazher\\\"}}\" " + im, 10000);
                if (r.Ok && r.Out.Trim() == "1") continue;
                var msg = r.Ok ? "Образ «" + im + "» скачан не в игре — удалять его отсюда нельзя." : "Образа «" + im + "» нет (docker images покажет, какие есть).";
                Emit(1, msg); sb.Append(msg).Append('\n'); return 1;
            }
            var piped = new StringBuilder();
            var res = RunStream(DevEnv.Docker, plan.ArgLine, CommandTimeoutMs, null, false,
                l => { if (tail != null) { piped.Append(l).Append('\n'); return false; } sb.Append(l).Append('\n'); return true; },
                l => { sb.Append(l).Append('\n'); return true; });
            if (res.TimedOut) Emit(1, "Команда шла дольше " + (CommandTimeoutMs / 1000) + " с и была остановлена. Сервер в контейнере лучше запускать с -d.");
            if (res.Ok && plan.PulledImage != null && ImagePulled != null) { var img = plan.PulledImage; Main(() => ImagePulled(img)); }
            if (tail == null || cancelled) return res.Code == 0 ? 0 : (res.Code > 0 ? res.Code : 1);
            // вывод docker → файл в песочнице → остаток конвейера
            var w = DevEnv.Run(DevEnv.Docker, "exec -i " + DevEnv.Container + " sh -c \"cat > /tmp/stazher-pipe\"", 20000, piped.ToString());
            if (!w.Ok) { var msg = "Не удалось передать вывод docker в песочницу: " + w.Text; Emit(1, msg); sb.Append(msg).Append('\n'); return 1; }
            return SandboxSegment("cat /tmp/stazher-pipe | " + tail, ref cwd, sb);
        }

        public void Interrupt()
        {
            if (!busy) return;
            cancelled = true;
            var id = jobId; var p = proc;
            Bg(() =>
            {
                // фоновые задачи bash игнорируют SIGINT, поэтому TERM, а через секунду — KILL
                if (id != null) DevEnv.DockerCmd("exec " + DevEnv.Container + " bash -c " + DevEnv.ShellQuote("g=$(cat /tmp/stazher-job-" + id + " 2>/dev/null); [ -n \"$g\" ] && { kill -TERM -- -$g; sleep 1; kill -KILL -- -$g; } 2>/dev/null"), 8000);
                Thread.Sleep(800);
                try { if (p != null && !p.HasExited) p.Kill(); } catch (Exception) { }
            });
        }

        // Процесс с построчным выводом в терминал. onOut/onErr возвращают false — строку не показывать
        ProcResult RunStream(string exe, string args, int timeoutMs, string stdin, bool quietMarker, Func<string, bool> onOut = null, Func<string, bool> onErr = null)
        {
            var r = new ProcResult(); var sw = Stopwatch.StartNew();
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = new Process { StartInfo = psi })
                {
                    var outSb = new StringBuilder();
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) return; bool show = onOut == null || onOut(e.Data); if (show) { Emit(0, e.Data); lock (outSb) outSb.Append(e.Data).Append('\n'); } };
                    // stderr показываем как обычный вывод, как в настоящем терминале: git и docker build пишут туда и успехи.
                    // Красным — только сообщения самой игры
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) return; bool show = onErr == null || onErr(e.Data); if (show) Emit(0, e.Data); };
                    p.Start(); proc = p;
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    DevEnv.WriteStdin(p, stdin);
                    if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; try { p.Kill(); } catch (Exception) { } }
                    p.WaitForExit(3000);
                    r.Code = r.TimedOut ? -1 : p.ExitCode;
                    lock (outSb) r.Out = outSb.ToString();
                }
            }
            catch (System.ComponentModel.Win32Exception) { r.NotFound = true; Emit(1, "docker не найден — Docker Desktop не установлен?"); }
            catch (Exception e) { r.Err = e.Message; Emit(1, e.Message); }
            r.Ms = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        // ---------- проверки сценариев (в фоне) ----------
        public void Check(EnvCheckDef c, Action<bool, string> done)
        {
            var last = Last;
            Bg(() =>
            {
                string note; bool ok;
                try { ok = EnvCheck.Evaluate(c, this, last, out note); }
                catch (Exception e) { ok = false; note = "Проверка не удалась: " + e.Message; }
                Main(() => done(ok, note));
            });
        }

        // Любая медленная работа в фоне, результат — в главном потоке
        public void Async<T>(Func<T> work, Action<T> done)
        {
            Bg(() => { T r = work(); Main(() => { if (done != null) done(r); }); });
        }

        public void RunSetup(string script, Action<bool> done)
        {
            Bg(() =>
            {
                var r = DevEnv.Exec(script, 120000);
                if (!r.Ok) Emit(1, "Подготовка задачи не удалась: " + r.Text);
                Main(() => { if (done != null) done(r.Ok); });
            });
        }

        // ---------- IEnvRunner ----------
        public ProcResult Sandbox(string command) { return DevEnv.Exec(command, 60000); }
        public ProcResult Host(string dockerArgs) { return DevEnv.DockerCmd(dockerArgs, 30000); }
        public string HostPath(string sandboxPath) { return DockerGuard.MapPath(sandboxPath, "/work", DevEnv.WorkDir); }
        public string Http(string url, out int status)
        {
            status = 0;
            try
            {
                // localhost на Windows сначала пробует ::1, а порты контейнеров игра открывает только на 127.0.0.1
                url = Regex.Replace(url ?? "", @"^(https?://)localhost\b", "${1}127.0.0.1");
                var req = (HttpWebRequest)WebRequest.Create(url); req.Timeout = 4000; req.ReadWriteTimeout = 4000; req.Proxy = null;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                { status = (int)resp.StatusCode; return rd.ReadToEnd(); }
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp != null) { status = (int)resp.StatusCode; try { using (var rd = new StreamReader(resp.GetResponseStream())) return rd.ReadToEnd(); } catch (Exception) { return ""; } }
                return null;
            }
            catch (Exception) { return null; }
        }
    }
}
