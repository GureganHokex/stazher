// Инструменты разработки в редакторе (меню «Стажёр»), чтобы работать с проектом без отдельной консоли:
//  • «Git: коммит и пуш в GitLab» — берёт Temp/commit.txt: сначала пути файлов (по одному в строке), потом строка «---»,
//    потом сообщение коммита. Добавляет только эти файлы, коммитит и пушит в origin (GitLab). На GitHub этот пункт не пушит.
//  • «Релиз: проверить GitHub» — только чтение: теги на GitHub, есть ли gh и вошёл ли он в аккаунт.
//  • «Релиз: выложить на GitHub» — по Temp/release.txt (тег, архив, заголовок, «---», текст релиза): тег, пуш main и тега
//    в remote github и релиз с архивом через gh. Запускать только после решения владельца выпустить релиз.
//  • «Проверить ветки языков в Docker» — эталоны задач с запуском (Go и дальше) проходят go test, заготовки — нет; итог в Temp/langcheck.txt.
//  • «Selftest последней сборки» — запускает Builds/Stazher-*-win64/Stazher.exe -selftest в окне и, когда он закончит,
//    копирует отчёт в Temp/selftest_build.txt.
//  • «Графика: замер FPS последней сборки» — Stazher.exe -bench: FPS на каждом пресете и снимки, итог в Temp/bench_build.txt.
//  • Команды без меню: слово в Temp/devcmd.txt — shots, pose, dump, resume, play, stop, refresh, builddev, selftest, bench, commit.
// Итог каждой команды — в Temp/devtools.txt.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Intern.EditorTools
{
    public static class DevTools
    {
        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Out { get { return Path.Combine(Root, "Temp", "devtools.txt"); } }

        static string Git(string args, StringBuilder log)
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                log.AppendLine("$ git " + args + "  → " + p.ExitCode);
                if (o.Length > 0) log.AppendLine(o.TrimEnd());
                if (e.Length > 0) log.AppendLine(e.TrimEnd());
                return p.ExitCode == 0 ? o : null;
            }
        }

        static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }

        [MenuItem("Стажёр/Git: коммит и пуш в GitLab", false, 20)]
        public static void CommitPush()
        {
            var log = new StringBuilder("git " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            try
            {
                string file = Path.Combine(Root, "Temp", "commit.txt");
                if (!File.Exists(file)) { log.AppendLine("нет Temp/commit.txt"); return; }
                var lines = File.ReadAllLines(file, Encoding.UTF8);
                int sep = Array.IndexOf(lines, "---");
                if (sep <= 0) { log.AppendLine("в Temp/commit.txt нет строки «---» после путей"); return; }
                var paths = lines.Take(sep).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                string msgFile = Path.Combine(Root, "Temp", "commit_msg.txt");
                File.WriteAllText(msgFile, string.Join("\n", lines.Skip(sep + 1).ToArray()).Trim() + "\n", new UTF8Encoding(false));
                foreach (var p in paths)
                    if (Git("add -- " + Quote(p), log) == null) { log.AppendLine("не добавился: " + p); return; }
                if (Git("commit -F " + Quote(msgFile), log) == null) return;
                Git("push origin main", log);   // только GitLab: релиз на GitHub — отдельно, в конце бэклога
                Git("log --oneline -1", log);
                File.Delete(file);
            }
            catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
            finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); Debug.Log("[Стажёр] " + log.ToString().Split('\n').Last(l => l.Length > 0)); }
        }

        [MenuItem("Стажёр/Проверить Docker", false, 22)]
        public static void CheckDocker()
        {
            var log = new StringBuilder("docker " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            try
            {
                string detail; var st = Intern.Game.DevEnv.Probe(out detail);
                log.AppendLine("состояние: " + st + " — " + detail);
                var v = Intern.Game.DevEnv.DockerCmd("version", 15000);
                log.AppendLine(v.Text);
                log.AppendLine("папка work: " + Intern.Game.DevEnv.WorkDir);
            }
            catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
            finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
        }

        // Эталоны и заготовки задач с запуском в Docker (ветки lang-*.json): эталон проходит все тесты, заготовка — нет.
        // Работает в фоне, итог — в Temp/langcheck.txt (первый запуск скачает образы компиляторов)
        [MenuItem("Стажёр/Проверить ветки языков в Docker", false, 23)]
        public static void CheckLangBranches()
        {
            string outFile = Path.Combine(Root, "Temp", "langcheck.txt");
            var log = new StringBuilder("ветки языков " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            var tasks = new List<Intern.Game.TaskData>();
            try
            {
                var dir = Path.Combine(Application.dataPath, "Intern 3", "Assets", "Intern", "Resources", "Tasks", "tracks");
                foreach (var f in Directory.GetFiles(dir, "lang-*.json").OrderBy(x => x))
                {
                    var d = Intern.Game.Tracks.Parse(File.ReadAllText(f), Path.GetFileNameWithoutExtension(f));
                    foreach (var tp in d.topics) foreach (var t in tp.tasks) if (t.Mode == "box") tasks.Add(t);
                }
            }
            catch (Exception e) { log.AppendLine("ошибка чтения задач: " + e.Message); File.WriteAllText(outFile, log.ToString(), new UTF8Encoding(false)); return; }
            // Temp/langcheck.only — проверить только задачи с этими префиксами id (через запятую), например «rs-»
            string only = Path.Combine(Root, "Temp", "langcheck.only");
            if (File.Exists(only))
            {
                var pref = File.ReadAllText(only).Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (pref.Length > 0) { tasks = tasks.Where(t => pref.Any(p => t.id.StartsWith(p))).ToList(); log.AppendLine("фильтр: " + string.Join(", ", pref)); }
            }
            log.AppendLine("задач с запуском в Docker: " + tasks.Count);
            File.WriteAllText(Out, "langcheck " + DateTime.Now.ToString("HH:mm:ss") + ": запущено, " + tasks.Count + " задач, итог — в Temp/langcheck.txt\n", new UTF8Encoding(false));
            if (File.Exists(outFile)) File.Delete(outFile);
            if (File.Exists(outFile + ".partial")) File.Delete(outFile + ".partial");
            new System.Threading.Thread(() =>
            {
                int ok = 0, bad = 0; var sw = Stopwatch.StartNew();
                try
                {
                    foreach (var t in tasks)
                    {
                        var spec = Intern.Game.LangBox.For(t.language);
                        var r = Intern.Game.LangBox.Test(spec, "check-" + t.id, t.solution, t.testCode, n => log.AppendLine("  " + n));
                        bool good = r.setupError == null && r.results.Count > 0 && r.results.All(x => x.Passed);
                        var st = Intern.Game.LangBox.Test(spec, "check-" + t.id, t.starter, t.testCode, null);
                        bool starterFails = st.setupError == null && st.results.Any(x => !x.Passed);
                        if (good && starterFails) ok++; else bad++;
                        log.AppendLine((good && starterFails ? "ок   " : "FAIL ") + t.id + ": эталон " + r.results.Count(x => x.Passed) + "/" + r.results.Count +
                                       ", заготовка " + st.results.Count(x => x.Passed) + "/" + st.results.Count + ", " + (r.ms / 1000.0).ToString("0.0") + " с" +
                                       (r.setupError != null ? " — " + r.setupError : "") + (!good && r.results.Count > 0 ? " — " + string.Join(" | ", r.results.Where(x => !x.Passed).Select(x => x.InputsText + ": " + x.Note).ToArray()) : ""));
                        try { File.WriteAllText(outFile + ".partial", log.ToString(), new UTF8Encoding(false)); } catch (Exception) { }   // видно ход проверки
                    }
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e); }
                log.AppendLine("итог: " + ok + " ок, " + bad + " ошибок, " + sw.Elapsed.TotalSeconds.ToString("0") + " с");
                try   // сколько места занимают образы компиляторов — сверить с тем, что обещает игра
                {
                    var imgs = Intern.Game.DevEnv.Run(Intern.Game.DevEnv.Docker, "image ls --format \"{{.Repository}}:{{.Tag}} {{.Size}}\"", 20000);
                    foreach (var spec in Intern.Game.LangBox.All)
                    {
                        var line = (imgs.Out ?? "").Replace("\r", "").Split('\n').FirstOrDefault(l => l.StartsWith(spec.image + " "));
                        log.AppendLine("образ " + spec.image + ": " + (line != null ? line.Substring(spec.image.Length + 1).Trim() : "не скачан") + " (в игре: " + spec.size + ")");
                    }
                }
                catch (Exception e) { log.AppendLine("размеры образов: " + e.Message); }
                try { File.WriteAllText(outFile, log.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // Пакетный прогон в контейнерах языков: Temp/boxbatch.json {"jobs":[{"id","lang","mode":"run|test","cmd","files":[{"name","text"}]}]}
        // → Temp/boxbatch.out.json с кодом выхода, stdout и stderr каждого запуска. Нужен генераторам веток (Tools/sNN), когда компилятора нет под рукой
        [Serializable] public class BoxFile { public string name, text; }
        [Serializable] public class BoxJobIn { public string id, lang, mode, cmd; public BoxFile[] files; }
        [Serializable] public class BoxBatchIn { public BoxJobIn[] jobs; }
        [Serializable] public class BoxJobOut { public string id, stdout, stderr; public int code; public bool timedOut; public double ms; }
        [Serializable] public class BoxBatchOut { public List<BoxJobOut> results = new List<BoxJobOut>(); public string notes = ""; public bool done; }

        [MenuItem("Стажёр/Прогнать пакет запусков в Docker", false, 25)]
        public static void RunBoxBatch()
        {
            string inFile = Path.Combine(Root, "Temp", "boxbatch.json"), outFile = Path.Combine(Root, "Temp", "boxbatch.out.json");
            BoxBatchIn batch;
            try { batch = JsonUtility.FromJson<BoxBatchIn>(File.ReadAllText(inFile)); }
            catch (Exception e) { File.WriteAllText(Out, "boxbatch: не прочитан Temp/boxbatch.json: " + e.Message + "\n", new UTF8Encoding(false)); return; }
            if (File.Exists(outFile)) File.Delete(outFile);
            File.WriteAllText(Out, "boxbatch " + DateTime.Now.ToString("HH:mm:ss") + ": " + batch.jobs.Length + " запусков, итог — в Temp/boxbatch.out.json\n", new UTF8Encoding(false));
            new System.Threading.Thread(() =>
            {
                var res = new BoxBatchOut();
                var notes = new StringBuilder();
                foreach (var j in batch.jobs)
                {
                    var o = new BoxJobOut { id = j.id, stdout = "", stderr = "" };
                    try
                    {
                        var spec = Intern.Game.LangBox.For(j.lang);
                        string err = spec == null ? "нет языка " + j.lang : Intern.Game.LangBox.PrepareBox(spec, n => notes.AppendLine(n));
                        if (err != null) { o.code = -1; o.stderr = err; }
                        else
                        {
                            var files = new Dictionary<string, string>();
                            foreach (var f in j.files ?? new BoxFile[0]) files[f.name] = f.text;
                            string box = "batch-" + j.id;
                            Intern.Game.LangBox.WriteFiles(spec, box, files);
                            string cmd = !string.IsNullOrEmpty(j.cmd) ? j.cmd : j.mode == "test" ? spec.testCmd : spec.runCmd;
                            var r = Intern.Game.LangBox.Exec(spec, box, cmd, spec.RunSec + 30);
                            o.code = r.Code; o.timedOut = r.TimedOut; o.ms = r.Ms; o.stdout = r.Out ?? ""; o.stderr = r.Err ?? "";
                        }
                    }
                    catch (Exception e) { o.code = -2; o.stderr = "исключение: " + e.Message; }
                    res.results.Add(o);
                    res.notes = notes.ToString();
                    try { File.WriteAllText(outFile + ".partial", JsonUtility.ToJson(res), new UTF8Encoding(false)); } catch (Exception) { }
                }
                res.done = true;
                try { File.WriteAllText(outFile, JsonUtility.ToJson(res), new UTF8Encoding(false)); } catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // Пересобрать образ компилятора C++ с нуля (docker rmi + docker build) и записать полный вывод — если сборка в игре не удалась
        [MenuItem("Стажёр/Пересобрать образ C++ (лог в Temp)", false, 24)]
        public static void RebuildCppImage()
        {
            string outFile = Path.Combine(Root, "Temp", "cppimage.txt");
            var spec = Intern.Game.LangBox.Cpp;
            File.WriteAllText(Out, "cppimage " + DateTime.Now.ToString("HH:mm:ss") + ": пересборка " + spec.image + " запущена, итог — в Temp/cppimage.txt\n", new UTF8Encoding(false));
            if (File.Exists(outFile)) File.Delete(outFile);
            new System.Threading.Thread(() =>
            {
                var log = new StringBuilder("пересборка " + spec.image + " " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
                try
                {
                    Intern.Game.DevEnv.DockerCmd("rm -f " + spec.container, 30000);
                    var rm = Intern.Game.DevEnv.DockerCmd("image rm -f " + spec.image, 60000);
                    log.AppendLine("image rm → " + rm.Code + " " + rm.Text.Trim());
                    var sw = Stopwatch.StartNew();
                    var b = Intern.Game.DevEnv.DockerCmd("build --no-cache --progress=plain --label " + Intern.Game.DevEnv.Label + " -t " + spec.image + " -", 20 * 60 * 1000, spec.dockerfile);
                    log.AppendLine("build → код " + b.Code + (b.TimedOut ? " (таймаут)" : "") + ", " + sw.Elapsed.TotalSeconds.ToString("0") + " с");
                    log.AppendLine("--- stdout ---\n" + b.Out + "\n--- stderr ---\n" + b.Err);
                    var sz = Intern.Game.DevEnv.DockerCmd("image ls --format \"{{.Repository}}:{{.Tag}} {{.Size}}\" " + spec.image, 20000);
                    log.AppendLine("размер: " + sz.Out.Trim());
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e); }
                try { File.WriteAllText(outFile, log.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
            }) { IsBackground = true }.Start();
        }

        // Команда без окна и без вопросов: git и gh не должны ждать ввода пароля, иначе редактор повиснет
        static int Cmd(string exe, string args, StringBuilder log, int timeoutMs = 300000)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
                psi.EnvironmentVariables["GCM_INTERACTIVE"] = "never";
                psi.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } log.AppendLine("$ " + exe + " " + args + "  → таймаут"); return -1; }
                    log.AppendLine("$ " + exe + " " + args + "  → " + p.ExitCode);
                    if (o.Result.Length > 0) log.AppendLine(o.Result.TrimEnd());
                    if (e.Result.Length > 0) log.AppendLine(e.Result.TrimEnd());
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log.AppendLine("$ " + exe + " " + args + "  → не запустилось: " + ex.Message); return -2; }
        }

        const string GitHubRepo = "GureganHokex/stazher";

        [MenuItem("Стажёр/Релиз: проверить GitHub", false, 40)]
        public static void CheckGitHub()
        {
            var log = new StringBuilder("github " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            new System.Threading.Thread(() =>
            {
                try
                {
                    Cmd("git", "ls-remote --tags github", log, 60000);
                    Cmd("git", "ls-remote github refs/heads/main", log, 60000);
                    Cmd("git", "rev-parse --short HEAD", log, 10000);
                    if (Cmd("gh", "--version", log, 20000) == 0)
                    {
                        Cmd("gh", "auth status", log, 30000);
                        Cmd("gh", "release list --repo " + GitHubRepo + " --limit 5", log, 30000);
                    }
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
                finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
            }) { IsBackground = true }.Start();
        }

        [MenuItem("Стажёр/Релиз: выложить на GitHub", false, 41)]
        public static void PublishGitHub()
        {
            var log = new StringBuilder("release " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            string file = Path.Combine(Root, "Temp", "release.txt");
            if (!File.Exists(file)) { File.WriteAllText(Out, log + "нет Temp/release.txt\n", new UTF8Encoding(false)); return; }
            var lines = File.ReadAllLines(file, Encoding.UTF8);
            int sep = Array.IndexOf(lines, "---");
            if (sep < 3) { File.WriteAllText(Out, log + "Temp/release.txt: тег, архив, заголовок, потом «---» и текст\n", new UTF8Encoding(false)); return; }
            string tag = lines[0].Trim(), zip = lines[1].Trim(), title = lines[2].Trim();
            string notes = Path.Combine(Root, "Temp", "release_notes.md");
            File.WriteAllText(notes, string.Join("\n", lines.Skip(sep + 1).ToArray()).Trim() + "\n", new UTF8Encoding(false));
            new System.Threading.Thread(() =>
            {
                try
                {
                    string zipPath = Path.Combine(Root, zip);
                    if (!File.Exists(zipPath)) { log.AppendLine("нет архива " + zipPath); return; }
                    log.AppendLine("архив: " + zipPath + ", " + (new FileInfo(zipPath).Length / 1048576.0).ToString("0.0") + " МБ");
                    if (Cmd("git", "rev-parse -q --verify refs/tags/" + tag, log, 10000) != 0 && Cmd("git", "tag -a " + tag + " -m " + Quote(title), log, 30000) != 0) return;
                    if (Cmd("git", "push github main", log) != 0) return;
                    if (Cmd("git", "push github " + tag, log) != 0) return;
                    if (Cmd("gh", "--version", log, 20000) != 0) { log.AppendLine("gh нет — релиз с архивом нужно создать на сайте GitHub, тег уже там"); return; }
                    if (Cmd("gh", "release create " + tag + " " + Quote(zipPath) + " --repo " + GitHubRepo + " --verify-tag --title " + Quote(title) + " --notes-file " + Quote(notes), log, 20 * 60000) != 0) return;
                    Cmd("gh", "release view " + tag + " --repo " + GitHubRepo, log, 30000);
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
                finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
            }) { IsBackground = true }.Start();
        }

        // ---------- команды из файла: Temp/devcmd.txt (одно слово) — чтобы не открывать меню, пока идёт игра ----------
        // shots — снимки ракурса, pose — запомнить ракурс, dump — настройки рендера в журнал
        [InitializeOnLoadMethod]
        static void WatchCommands()
        {
            double nextCheck = 0;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup < nextCheck) return;
                nextCheck = EditorApplication.timeSinceStartup + 0.5;
                string f = Path.Combine(Root, "Temp", "devcmd.txt");
                if (!File.Exists(f)) return;
                string cmd;
                try { cmd = File.ReadAllText(f).Trim(); File.Delete(f); } catch (Exception) { return; }
                switch (cmd)
                {
                    case "shots": Shots(); break;
                    case "pose": SavePose(); break;
                    case "dump": DumpRender(); break;
                    case "resume": Resume(); break;
                    case "play": EditorApplication.isPlaying = true; File.WriteAllText(Out, "play\n", new UTF8Encoding(false)); break;
                    case "stop": EditorApplication.isPlaying = false; File.WriteAllText(Out, "stop\n", new UTF8Encoding(false)); break;
                    case "refresh": File.WriteAllText(Out, "refresh " + DateTime.Now.ToString("HH:mm:ss") + "\n", new UTF8Encoding(false)); AssetDatabase.Refresh(); break;
                    case "builddev":
                        if (EditorApplication.isPlaying) File.WriteAllText(Out, "builddev: сначала выйти из Play\n", new UTF8Encoding(false));
                        else ReleaseBuild.BuildDev();
                        break;
                    case "selftest": SelftestBuild(); break;
                    case "bench": BenchBuild(); break;
                    case "commit": CommitPush(); break;
                    default: File.WriteAllText(Out, "devcmd: неизвестная команда «" + cmd + "»\n", new UTF8Encoding(false)); break;
                }
            };
        }

        // ---------- снимки для проверки графики (только в режиме Play) ----------
        // Temp/pose.txt — ракурс «x y z yaw pitch fp»; Temp/shots.txt — варианты «имя|настройки», настройки — как у UrpLook.Tweak,
        // плюс quality=0..3 (пресет игры). Снимки — Temp/shots/<имя>.png, журнал — Temp/devtools.txt
        static System.Reflection.MethodInfo UrpTweak()
        {
            var t = Type.GetType("Intern.Look.UrpLook, Intern.URP");
            return t != null ? t.GetMethod("Tweak", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
        }

        [MenuItem("Стажёр/Графика: запомнить ракурс", false, 60)]
        public static void SavePose()
        {
            var pl = UnityEngine.Object.FindFirstObjectByType<Intern.Game.PlayerController>();
            if (!EditorApplication.isPlaying || pl == null) { File.WriteAllText(Out, "ракурс: нужен режим Play\n", new UTF8Encoding(false)); return; }
            var p = pl.Position; var ci = System.Globalization.CultureInfo.InvariantCulture;
            string line = string.Format(ci, "{0:0.###} {1:0.###} {2:0.###} {3:0.#} {4:0.#} {5}", p.x, p.y, p.z, pl.CamYaw, pl.CamPitch, pl.firstPerson ? 1 : 0);
            File.WriteAllText(Path.Combine(Root, "Temp", "pose.txt"), line + "\n", new UTF8Encoding(false));
            File.WriteAllText(Out, "ракурс запомнен: " + line + "\n", new UTF8Encoding(false));
        }

        [MenuItem("Стажёр/Графика: снимки ракурса", false, 61)]
        public static void Shots()
        {
            var log = new StringBuilder("снимки " + DateTime.Now.ToString("HH:mm:ss") + "\n");
            var pl = UnityEngine.Object.FindFirstObjectByType<Intern.Game.PlayerController>();
            if (!EditorApplication.isPlaying || pl == null) { File.WriteAllText(Out, log + "нужен режим Play\n", new UTF8Encoding(false)); return; }
            var gr = UnityEngine.Object.FindFirstObjectByType<Intern.Game.GameRoot>();
            if (gr != null) gr.UiResume();   // пауза закрывала бы кадр
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string poseFile = Path.Combine(Root, "Temp", "pose.txt");
            if (File.Exists(poseFile))
            {
                var v = File.ReadAllText(poseFile).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (v.Length >= 5)
                {
                    pl.Teleport(new Vector3(float.Parse(v[0], ci), float.Parse(v[1], ci), float.Parse(v[2], ci)), float.Parse(v[3], ci));
                    pl.SetCamPitch(float.Parse(v[4], ci));
                    if (v.Length >= 6 && (v[5] == "1") != pl.firstPerson) pl.ToggleView();
                    log.AppendLine("ракурс: " + string.Join(" ", v));
                }
            }
            string listFile = Path.Combine(Root, "Temp", "shots.txt");
            var variants = File.Exists(listFile)
                ? File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList()
                : new List<string> { "base|" };
            string dir = Path.Combine(Root, "Temp", "shots");
            Directory.CreateDirectory(dir);
            var tweak = UrpTweak();
            string savedSettings = JsonUtility.ToJson(Intern.Game.GameConfig.S);   // пресеты вариантов меняют настройки — после съёмки вернём
            int idx = 0; double next = EditorApplication.timeSinceStartup + 1.5; int phase = 0;
            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                try
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= step; log.AppendLine("Play остановлен"); File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); return; }
                    if (EditorApplication.timeSinceStartup < next) return;
                    if (idx >= variants.Count)
                    {
                        EditorApplication.update -= step;
                        if (tweak != null) tweak.Invoke(null, new object[] { "reset" });
                        Intern.Game.GameConfig.S = JsonUtility.FromJson<Intern.Game.GameSettings>(savedSettings); Intern.Game.GameConfig.ApplyGraphics();
                        log.AppendLine("готово: " + variants.Count + " снимков в Temp/shots");
                        File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false));
                        return;
                    }
                    var parts = variants[idx].Split(new[] { '|' }, 2);
                    string name = parts[0].Trim(), spec = parts.Length > 1 ? parts[1].Trim() : "";
                    if (phase == 0)
                    {
                        // настройки варианта: сначала вернуть прошлые эксперименты, потом пресет, потом правки
                        if (tweak != null) tweak.Invoke(null, new object[] { "reset" });
                        var rest = new List<string>();
                        foreach (var kv in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (kv.Trim().StartsWith("quality=")) { Intern.Game.GameConfig.SetQuality(int.Parse(kv.Trim().Substring(8))); Intern.Game.GameConfig.ApplyGraphics(); }
                            else rest.Add(kv.Trim());
                        }
                        string res = rest.Count > 0 && tweak != null ? (string)tweak.Invoke(null, new object[] { string.Join(";", rest.ToArray()) }) : "";
                        log.AppendLine(name + ": " + spec + (res.Length > 0 ? " → " + res.Replace("\n", "; ") : ""));
                        phase = 1; next = EditorApplication.timeSinceStartup + 1.2;
                    }
                    else
                    {
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                        phase = 0; idx++; next = EditorApplication.timeSinceStartup + 0.8;
                    }
                }
                catch (Exception e) { EditorApplication.update -= step; log.AppendLine("ошибка: " + e); File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
            };
            EditorApplication.update += step;
            File.WriteAllText(Out, log + "идёт съёмка: " + variants.Count + " вариантов\n", new UTF8Encoding(false));
        }

        // закрыть паузу в режиме Play (команда resume)
        static void Resume()
        {
            var gr = EditorApplication.isPlaying ? UnityEngine.Object.FindFirstObjectByType<Intern.Game.GameRoot>() : null;
            if (gr != null) gr.UiResume();
            File.WriteAllText(Out, "resume: " + (gr != null ? gr.CurMode.ToString() : "нет игры") + "\n", new UTF8Encoding(false));
        }

        [MenuItem("Стажёр/Графика: настройки рендера в журнал", false, 62)]
        public static void DumpRender()
        {
            var tweak = UrpTweak();
            File.WriteAllText(Out, "рендер " + DateTime.Now.ToString("HH:mm:ss") + "\n" + (tweak != null ? (string)tweak.Invoke(null, new object[] { "dump" }) : "нет UrpLook") + "\n", new UTF8Encoding(false));
        }

        [MenuItem("Стажёр/Selftest последней сборки", false, 21)]
        public static void SelftestBuild()
        {
            var log = new StringBuilder("selftest " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            try
            {
                var dir = Directory.GetDirectories(Path.Combine(Root, "Builds"), "Stazher-*-win64").OrderBy(Directory.GetLastWriteTime).LastOrDefault();
                if (dir == null) { log.AppendLine("сборок нет"); return; }
                string exe = Path.Combine(dir, "Stazher.exe");
                string report = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Codezilla Games", "Стажёр", "selftest.txt");
                string copy = Path.Combine(Root, "Temp", "selftest_build.txt");
                if (File.Exists(copy)) File.Delete(copy);
                var p = Process.Start(new ProcessStartInfo(exe, "-selftest -screen-fullscreen 0 -screen-width 640 -screen-height 360") { WorkingDirectory = dir, UseShellExecute = false });
                log.AppendLine("запущено: " + exe + " (pid " + p.Id + "), отчёт появится в Temp/selftest_build.txt");
                var started = DateTime.Now;
                new System.Threading.Thread(() =>
                {
                    try
                    {
                        p.WaitForExit(15 * 60 * 1000);
                        var fresh = File.Exists(report) && File.GetLastWriteTime(report) >= started;
                        string text = fresh ? File.ReadAllText(report) : "отчёт не обновился";
                        // версия внутри сборки должна совпадать с версией в имени папки (Stazher-0.9.0-win64, Stazher-0.9.0-dev-1001-1200-win64)
                        var fm = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(dir), @"^Stazher-(\d+\.\d+\.\d+(?:-dev)?)-");
                        var hm = System.Text.RegularExpressions.Regex.Match(text, @"^Стажёр (\S+) ·", System.Text.RegularExpressions.RegexOptions.Multiline);
                        string ver = fm.Success && hm.Success ? (fm.Groups[1].Value == hm.Groups[1].Value ? "версия сборки: " + hm.Groups[1].Value + " — совпадает с папкой" : "версия сборки: " + hm.Groups[1].Value + " — НЕ совпадает с папкой " + fm.Groups[1].Value) : "версия сборки: не удалось сверить";
                        File.WriteAllText(copy, "exit " + (p.HasExited ? p.ExitCode.ToString() : "timeout") + "\n" + ver + "\n" + text, new UTF8Encoding(false));
                    }
                    catch (Exception e) { try { File.WriteAllText(copy, "ошибка: " + e.Message); } catch { } }
                }) { IsBackground = true }.Start();
            }
            catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
            finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
        }

        // Замер графики последней сборки: Stazher.exe -bench в окне 1920×1080 — FPS на каждом пресете и снимки;
        // отчёт копируется в Temp/bench_build.txt, снимки — в Temp/bench
        [MenuItem("Стажёр/Графика: замер FPS последней сборки", false, 63)]
        public static void BenchBuild()
        {
            var log = new StringBuilder("bench " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            try
            {
                var dir = Directory.GetDirectories(Path.Combine(Root, "Builds"), "Stazher-*-win64").OrderBy(Directory.GetLastWriteTime).LastOrDefault();
                if (dir == null) { log.AppendLine("сборок нет"); return; }
                string exe = Path.Combine(dir, "Stazher.exe");
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Codezilla Games", "Стажёр");
                string report = Path.Combine(data, "bench.txt"), shots = Path.Combine(data, "bench");
                string copy = Path.Combine(Root, "Temp", "bench_build.txt"), copyShots = Path.Combine(Root, "Temp", "bench");
                if (File.Exists(copy)) File.Delete(copy);
                var p = Process.Start(new ProcessStartInfo(exe, "-bench -screen-fullscreen 0 -screen-width 1920 -screen-height 1080") { WorkingDirectory = dir, UseShellExecute = false });
                log.AppendLine("запущено: " + exe + " (pid " + p.Id + "), отчёт появится в Temp/bench_build.txt");
                var started = DateTime.Now;
                new System.Threading.Thread(() =>
                {
                    try
                    {
                        p.WaitForExit(5 * 60 * 1000);
                        var fresh = File.Exists(report) && File.GetLastWriteTime(report) >= started;
                        Directory.CreateDirectory(copyShots);
                        if (Directory.Exists(shots))
                            foreach (var f in Directory.GetFiles(shots, "*.png")) File.Copy(f, Path.Combine(copyShots, Path.GetFileName(f)), true);
                        string plog = Path.Combine(data, "Player.log");
                        if (File.Exists(plog)) File.Copy(plog, Path.Combine(Root, "Temp", "bench_player.log"), true);   // предупреждения рендера — там
                        File.WriteAllText(copy, "exit " + (p.HasExited ? p.ExitCode.ToString() : "timeout") + "\n" + (fresh ? File.ReadAllText(report) : "отчёт не обновился"), new UTF8Encoding(false));
                    }
                    catch (Exception e) { try { File.WriteAllText(copy, "ошибка: " + e.Message); } catch { } }
                }) { IsBackground = true }.Start();
            }
            catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
            finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
        }
    }
}
