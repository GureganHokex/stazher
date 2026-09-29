// Спринт 7 «Окружение»: Docker Desktop на ПК игрока и песочница для терминала и задач-сценариев.
// Игра не ставит Docker сама: проверяет, что он есть и запущен, собирает образ песочницы из Dockerfile,
// который пишет сама (SandboxDockerfile), держит один контейнер stazher-sandbox с папкой
// Документы/Стажёр/work, смонтированной как /work, и выполняет в нём команды игрока.
// Всё, что создаёт игра, помечено меткой stazher=1 — «Убрать за собой» удаляет только это.
// Процессы запускаются без окна, с таймаутом; Unity API здесь не используется (можно звать из потока).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Intern.Game
{
    public enum EnvState { Unknown, NoDocker, DockerStopped, NoImage, Ready }

    public class ProcResult
    {
        public int Code = -1;
        public string Out = "", Err = "";
        public bool TimedOut, NotFound;
        public double Ms;
        public bool Ok { get { return Code == 0 && !TimedOut && !NotFound; } }
        public string Text { get { return (Out + (Err.Length > 0 && Out.Length > 0 && !Out.EndsWith("\n") ? "\n" : "") + Err).TrimEnd(); } }
    }

    public static class DevEnv
    {
        public const string Image = "stazher/sandbox:1";
        public const string Container = "stazher-sandbox";
        public const string Label = "stazher=1";
        public const int WebPort = 8080;        // порт для задач с веб-сервером: localhost:8080 на ПК игрока

        public static string Docker = "docker";  // можно подменить полным путём, если docker не в PATH

        // Папка, которую видят и IDE, и песочница (/work)
        public static string WorkDir
        {
            get
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) docs = Path.GetTempPath();
                return Path.Combine(Path.Combine(docs, "Стажёр"), "work");
            }
        }

        // ---------- запуск процесса ----------
        public static ProcResult Run(string exe, string args, int timeoutMs = 20000, string stdin = null, Action<Process> started = null)
        {
            var r = new ProcResult();
            var sw = Stopwatch.StartNew();
            Process p = null;
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                p = Process.Start(psi);
                if (started != null) started(p);
                var outSb = new StringBuilder(); var errSb = new StringBuilder();
                var tOut = new Thread(() => { try { outSb.Append(p.StandardOutput.ReadToEnd()); } catch (Exception) { } }) { IsBackground = true };
                var tErr = new Thread(() => { try { errSb.Append(p.StandardError.ReadToEnd()); } catch (Exception) { } }) { IsBackground = true };
                tOut.Start(); tErr.Start();
                if (stdin != null) WriteStdin(p, stdin);
                if (!p.WaitForExit(timeoutMs)) { r.TimedOut = true; try { p.Kill(); } catch (Exception) { } }
                tOut.Join(2000); tErr.Join(2000);
                r.Out = outSb.ToString(); r.Err = errSb.ToString();
                r.Code = r.TimedOut ? -1 : p.ExitCode;
            }
            catch (System.ComponentModel.Win32Exception) { r.NotFound = true; r.Err = exe + " не найден"; }
            catch (Exception e) { r.Err = e.Message; }
            finally { if (p != null) p.Dispose(); r.Ms = sw.Elapsed.TotalMilliseconds; }
            return r;
        }

        // stdin всегда в UTF-8 байтами: кодировка консоли Windows по умолчанию испортила бы кириллицу
        public static void WriteStdin(Process p, string text)
        {
            try
            {
                var b = new UTF8Encoding(false).GetBytes(text ?? "");
                var st = p.StandardInput.BaseStream;
                st.Write(b, 0, b.Length); st.Flush();
            }
            catch (Exception) { }
            try { p.StandardInput.Close(); } catch (Exception) { }
        }

        // nginx → nginx:latest: «nginx» и «nginx:latest» — один и тот же образ
        public static string NormImage(string im)
        {
            if (string.IsNullOrEmpty(im)) return im;
            int slash = im.LastIndexOf('/');
            return im.IndexOf(':', slash + 1) < 0 && !im.Contains("@") ? im + ":latest" : im;
        }

        public static ProcResult DockerCmd(string args, int timeoutMs = 20000, string stdin = null) { return Run(Docker, args, timeoutMs, stdin); }

        // ---------- состояние окружения ----------
        public static EnvState Probe(out string detail)
        {
            var v = DockerCmd("version --format \"{{.Client.Version}}|{{.Server.Version}}\"", 15000);
            if (v.NotFound) { detail = "Docker не установлен: команда docker не найдена."; return EnvState.NoDocker; }
            var parts = (v.Out ?? "").Trim().Split('|');
            if (!v.Ok || parts.Length < 2 || parts[1].Trim().Length == 0)
            {
                detail = "Docker установлен" + (parts.Length > 0 && parts[0].Length > 0 ? " (клиент " + parts[0].Trim() + ")" : "") + ", но движок не запущен. Запусти Docker Desktop и подожди, пока он загрузится." +
                         (v.Err.Contains("WSL") || v.Err.Contains("virtualiz") ? " Похоже, не хватает WSL 2 или виртуализации в BIOS." : "");
                return EnvState.DockerStopped;
            }
            var img = DockerCmd("image inspect --format \"{{.Id}}\" " + Image, 15000);
            if (!img.Ok) { detail = "Docker " + parts[1].Trim() + " работает. Образ песочницы " + Image + " ещё не собран."; return EnvState.NoImage; }
            detail = "Docker " + parts[1].Trim() + " работает, образ песочницы на месте.";
            return EnvState.Ready;
        }

        // ---------- образ песочницы ----------
        // stazher-run JOB DIR: команда игрока из stdin выполняется в своей группе процессов (для Ctrl+C),
        // в конце печатается __STAZHER__<код>|<папка> — так терминал помнит cd между командами
        public const string RunScript =
            "#!/bin/bash\n" +
            "# stazher-run JOB [DIR] — выполнить команду из stdin в папке DIR (песочница «Стажёра»)\n" +
            "job=\"$1\"; dir=\"${2:-/work}\"\n" +
            "cmd=\"$(cat)\"\n" +
            "script=\"cd $(printf '%q' \"$dir\") 2>/dev/null || cd /work\n$cmd\n__c=\\$?; printf '\\n__STAZHER__%s|%s\\n' \\\"\\$__c\\\" \\\"\\$PWD\\\"\"\n" +
            "setsid bash -c \"$script\" </dev/null &\n" +
            "pid=$!\n" +
            "echo \"$pid\" > \"/tmp/stazher-job-$job\"\n" +
            "wait \"$pid\"; rc=$?\n" +
            "rm -f \"/tmp/stazher-job-$job\"\n" +
            "exit $rc\n";

        public static string SandboxDockerfile()
        {
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(RunScript));
            return string.Join("\n", new[]
            {
                "# Песочница «Стажёра»: сюда игра отправляет команды из терминала IDE. Собирается самой игрой.",
                "FROM ubuntu:24.04",
                "ENV DEBIAN_FRONTEND=noninteractive LANG=C.UTF-8 LC_ALL=C.UTF-8 TZ=Europe/Moscow",
                "RUN apt-get update \\",
                " && apt-get install -y --no-install-recommends bash git curl wget ca-certificates python3 python3-pip python3-venv nodejs npm jq tree less nano procps iproute2 iputils-ping dnsutils unzip zip file tzdata \\",
                " && rm -rf /var/lib/apt/lists/*",
                "RUN git config --system init.defaultBranch main \\",
                " && git config --system user.name \"Стажёр\" \\",
                " && git config --system user.email \"intern@kodzilla.local\" \\",
                " && git config --system --add safe.directory '*' \\",
                " && git config --system core.autocrlf input \\",
                " && git config --system core.fileMode false \\",   // папка work смонтирована с Windows: все файлы выглядят исполняемыми
                " && ln -sf /usr/bin/python3 /usr/local/bin/python",
                "RUN echo '" + b64 + "' | base64 -d > /usr/local/bin/stazher-run && chmod +x /usr/local/bin/stazher-run",
                "WORKDIR /work",
                "LABEL stazher=1",
                "CMD [\"sleep\", \"infinity\"]",
                "",
            });
        }

        // Собрать образ песочницы из Dockerfile (текст передаётся через stdin: docker build -)
        public static ProcResult BuildImage(string dockerfile, int timeoutMs = 20 * 60 * 1000)
        {
            return DockerCmd("build --label " + Label + " -t " + Image + " -", timeoutMs, dockerfile);
        }

        // Контейнер песочницы запущен (если нет — создать и запустить)
        public static bool EnsureContainer(out string error)
        {
            error = null;
            Directory.CreateDirectory(WorkDir);
            var st = DockerCmd("inspect --format \"{{.State.Running}}\" " + Container, 15000);
            if (st.Ok && st.Out.Trim() == "true") return true;
            if (st.Ok) { var s = DockerCmd("start " + Container, 30000); if (s.Ok) return true; }
            var r = DockerCmd("run -d --name " + Container + " --label " + Label + " --hostname stazher" +
                              " -v \"" + WorkDir + ":/work\" -w /work " + Image + " sleep infinity", 60000);
            if (!r.Ok) { error = r.Text; return false; }
            return true;
        }

        // Команда bash внутри песочницы. Для Ctrl+C: started получает процесс docker exec
        public static ProcResult Exec(string command, int timeoutMs = 30000, Action<Process> started = null)
        {
            return Run(Docker, "exec -i -w /work " + Container + " bash -lc " + ShellQuote(command), timeoutMs, null, started);
        }

        // Уборка: контейнеры с меткой stazher=1 (кроме песочницы, если all = false), сети и тома игры;
        // all — ещё и песочница, образы игры и образы, скачанные в игре (extraImages)
        public static string Cleanup(bool all, IEnumerable<string> extraImages = null)
        {
            var sb = new StringBuilder();
            var rows = DockerCmd("ps -a --filter label=" + Label + " --format \"{{.ID}} {{.Names}}\"", 15000).Out.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var ids = new List<string>(); var names = new List<string>();
            foreach (var r in rows)
            {
                var p = r.Trim().Split(' ');
                if (p.Length < 2 || (!all && p[1] == Container)) continue;
                ids.Add(p[0]); names.Add(p[1]);
            }
            if (ids.Count > 0) sb.AppendLine("контейнеры " + string.Join(", ", names.ToArray()) + " — " + (DockerCmd("rm -f " + string.Join(" ", ids.ToArray()), 60000).Ok ? "удалены" : "не удалось удалить"));
            else sb.AppendLine("контейнеров задач нет");
            DockerCmd("network prune -f --filter label=" + Label, 30000);
            DockerCmd("volume prune -f --filter label=" + Label, 30000);
            if (all)
            {
                var imgs = new List<string>();
                if (extraImages != null) foreach (var im in extraImages) if (!string.IsNullOrEmpty(im)) imgs.Add(im);
                foreach (var im in imgs) DockerCmd("rmi " + im, 60000);
                sb.AppendLine("образы игры" + (imgs.Count > 0 ? " и скачанные в ней (" + string.Join(", ", imgs.ToArray()) + ")" : "") + " — " + (DockerCmd("image prune -af --filter label=" + Label, 120000).Ok ? "удалены" : "не удалось удалить"));
            }
            return sb.ToString().TrimEnd();
        }

        // Выход из игры: песочница засыпает (сама запустится при следующей команде). Не ждём — docker отработает сам
        public static void StopSandboxDetached()
        {
            try { Process.Start(new ProcessStartInfo(Docker, "stop -t 1 " + Container) { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception) { }
        }

        // Строка для bash -lc "..." (Windows передаёт аргументы через CommandLineToArgvW)
        public static string ShellQuote(string s)
        {
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes); slashes = 0; sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            sb.Append('"');
            return sb.ToString();
        }
    }
}
