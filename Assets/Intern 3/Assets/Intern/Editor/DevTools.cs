// Инструменты разработки в редакторе (меню «Стажёр»), чтобы работать с проектом без отдельной консоли:
//  • «Git: коммит и пуш в GitLab» — берёт Temp/commit.txt: сначала пути файлов (по одному в строке), потом строка «---»,
//    потом сообщение коммита. Добавляет только эти файлы, коммитит и пушит в origin (GitLab). На GitHub не пушит никогда.
//  • «Проверить ветки языков в Docker» — эталоны задач с запуском (Go и дальше) проходят go test, заготовки — нет; итог в Temp/langcheck.txt.
//  • «Selftest последней сборки» — запускает Builds/Stazher-*-win64/Stazher.exe -selftest в окне и, когда он закончит,
//    копирует отчёт в Temp/selftest_build.txt.
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
            log.AppendLine("задач с запуском в Docker: " + tasks.Count);
            File.WriteAllText(Out, "langcheck " + DateTime.Now.ToString("HH:mm:ss") + ": запущено, " + tasks.Count + " задач, итог — в Temp/langcheck.txt\n", new UTF8Encoding(false));
            if (File.Exists(outFile)) File.Delete(outFile);
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
