// Установщик Windows (спринт 9 версии 0.9 «Установщик»): Stazher-Setup.exe на Inno Setup вместо архива.
//  • «Стажёр → Собрать установщик» — из последней сборки в Builds пишет сценарий Temp/Stazher.iss и собирает
//    Builds/Stazher-<версия>-setup/Stazher-Setup.exe компилятором ISCC (Inno Setup 6). Релизная сборка
//    («Собрать релиз для Windows») собирает установщик сама, если Inno Setup есть.
//  • «Стажёр → Поставить Inno Setup (winget)» — один раз на компьютере: winget install JRSoftware.InnoSetup.
//  • «Стажёр → Проверить установщик» — тихая установка в папку пользователя, ярлыки, метка «из интернета»
//    у Stazher.exe, самопроверка установленной игры, обновление поверх запущенной игры, удаление
//    (сохранения остаются). Отчёт — Temp/installer_test.txt.
// Установщик: без прав администратора (%LOCALAPPDATA%\Programs\Stazher), ярлыки в «Пуске» и на рабочем столе,
// русский язык, иконка игры, запуск после установки; удаление — через «Параметры → Приложения»; обновление
// ставится поверх: запущенную игру установщик закрывает, сохранения и скриншоты (AppData\LocalLow) не трогает.
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
    public static class Installer
    {
        // Постоянный идентификатор приложения: по нему новая версия ставится поверх старой, а не рядом
        const string AppId = "{{3C63C3DE-00E5-436A-A147-6FB03A2168A3}";
        const string AppIdPlain = "{3C63C3DE-00E5-436A-A147-6FB03A2168A3}";
        const string SetupName = "Stazher-Setup";
        const string Repo = "https://github.com/GureganHokex/stazher";

        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Out { get { return Path.Combine(Root, "Temp", "devtools.txt"); } }
        static string IconIco { get { return Path.Combine(Root, "Assets", "Intern 3", "Assets", "Intern", "Branding", "icon.ico"); } }

        public static string Iscc()
        {
            var dirs = new List<string>();
            foreach (var env in new[] { "ProgramFiles(x86)", "ProgramFiles", "LOCALAPPDATA" })
            {
                var b = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(b)) continue;
                dirs.Add(Path.Combine(b, "Inno Setup 6"));
                dirs.Add(Path.Combine(b, "Programs", "Inno Setup 6"));
            }
            foreach (var d in dirs) { var p = Path.Combine(d, "ISCC.exe"); if (File.Exists(p)) return p; }
            return null;
        }

        // ---------- Inno Setup на компьютер ----------
        [MenuItem("Стажёр/Поставить Inno Setup (winget)", false, 5)]
        public static void InstallInno()
        {
            var log = new StringBuilder("inno " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            new System.Threading.Thread(() =>
            {
                try
                {
                    var have = Iscc();
                    if (have != null) { log.AppendLine("уже стоит: " + have); return; }
                    Cmd("winget", "--version", log, 30000);
                    int c = Cmd("winget", "install --id JRSoftware.InnoSetup -e --scope user --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", log, 15 * 60000);
                    if (Iscc() == null && c != 0)
                        Cmd("winget", "install --id JRSoftware.InnoSetup -e --silent --accept-source-agreements --accept-package-agreements --disable-interactivity", log, 15 * 60000);
                    log.AppendLine("ISCC: " + (Iscc() ?? "не найден"));
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
                finally { File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false)); }
            }) { IsBackground = true }.Start();
        }

        // ---------- сборка установщика ----------
        [MenuItem("Стажёр/Собрать установщик", false, 4)]
        public static void BuildLatest()
        {
            var dir = Directory.GetDirectories(Path.Combine(Root, "Builds"), "Stazher-*-win64").OrderBy(Directory.GetLastWriteTime).LastOrDefault();
            if (dir == null) { File.WriteAllText(Out, "installer: сборок нет\n", new UTF8Encoding(false)); return; }
            var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(dir), @"^Stazher-(\d+\.\d+\.\d+(?:-dev)?)");
            Build(m.Success ? m.Groups[1].Value : ReleaseBuild.Version, dir, true);
        }

        public static string SetupPath(string version) { return Path.Combine(Root, "Builds", "Stazher-" + version + "-setup", SetupName + ".exe"); }

        /// <summary>Сценарий и компиляция. background — в отдельном потоке (из меню), иначе ждёт (из релизной сборки).</summary>
        public static void Build(string version, string buildDir, bool background)
        {
            var log = new StringBuilder("installer " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            string iscc = Iscc();
            if (iscc == null) { log.AppendLine("нет Inno Setup — «Стажёр → Поставить Inno Setup (winget)»"); Finish(log); return; }
            string outDir = Path.GetDirectoryName(SetupPath(version));
            string iss = Path.Combine(Root, "Temp", "Stazher.iss");
            Directory.CreateDirectory(outDir);
            if (File.Exists(SetupPath(version))) File.Delete(SetupPath(version));
            File.WriteAllText(iss, Script(version, buildDir, outDir), new UTF8Encoding(true));   // с BOM: Inno Setup читает русский текст как UTF-8
            log.AppendLine("сценарий: " + iss);
            Action run = () =>
            {
                try
                {
                    var sw = Stopwatch.StartNew();
                    int c = Cmd(iscc, "/Q " + Q(iss), log, 30 * 60000);
                    var f = new FileInfo(SetupPath(version));
                    log.AppendLine(c == 0 && f.Exists
                        ? "установщик: " + f.FullName + ", " + (f.Length / 1048576.0).ToString("0.0") + " МБ, " + sw.Elapsed.TotalSeconds.ToString("0") + " с"
                        : "установщик не собрался (код " + c + ")");
                }
                catch (Exception e) { log.AppendLine("ошибка: " + e.Message); }
                finally { Finish(log); }
            };
            if (background) new System.Threading.Thread(() => run()) { IsBackground = true }.Start();
            else run();
        }

        static void Finish(StringBuilder log)
        {
            File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false));
            try { File.AppendAllText(Path.Combine(Root, "Temp", "release_build.txt"), log.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
            Debug.Log("[Стажёр] " + log.ToString().TrimEnd().Split('\n').Last());
        }

        static string Script(string version, string buildDir, string outDir)
        {
            var num = System.Text.RegularExpressions.Regex.Match(version, @"^\d+\.\d+\.\d+").Value;
            var s = new StringBuilder();
            s.AppendLine("; Установщик «Стажёра» — пишет Assets/Intern 3/Assets/Intern/Editor/Installer.cs, руками не править");
            s.AppendLine("[Setup]");
            s.AppendLine("AppId=" + AppId);
            s.AppendLine("AppName=Стажёр");
            s.AppendLine("AppVersion=" + version);
            s.AppendLine("AppVerName=Стажёр " + version);
            s.AppendLine("AppPublisher=Codezilla Games");
            s.AppendLine("AppPublisherURL=" + Repo);
            s.AppendLine("AppSupportURL=" + Repo + "/issues");
            s.AppendLine("AppUpdatesURL=" + Repo + "/releases");
            s.AppendLine("VersionInfoVersion=" + num + ".0");
            s.AppendLine("VersionInfoCompany=Codezilla Games");
            s.AppendLine("VersionInfoProductName=Стажёр");
            s.AppendLine("VersionInfoDescription=Установщик игры «Стажёр»");
            s.AppendLine("; без прав администратора: в папку пользователя (%LOCALAPPDATA%\\Programs\\Stazher), ярлыки — свои");
            s.AppendLine("PrivilegesRequired=lowest");
            s.AppendLine("DefaultDirName={autopf}\\Stazher");
            s.AppendLine("DefaultGroupName=Стажёр");
            s.AppendLine("DisableProgramGroupPage=yes");
            s.AppendLine("UsePreviousAppDir=yes");
            s.AppendLine("ArchitecturesAllowed=x64compatible");
            s.AppendLine("ArchitecturesInstallIn64BitMode=x64compatible");
            s.AppendLine("MinVersion=10.0");
            s.AppendLine("WizardStyle=modern");
            s.AppendLine("ShowLanguageDialog=no");
            s.AppendLine("SetupIconFile=" + IconIco);
            s.AppendLine("UninstallDisplayIcon={app}\\Stazher.exe");
            s.AppendLine("UninstallDisplayName=Стажёр");
            s.AppendLine("; обновление поверх: запущенную игру закрывает сам");
            s.AppendLine("CloseApplications=yes");
            s.AppendLine("CloseApplicationsFilter=*.exe,*.dll");
            s.AppendLine("RestartApplications=no");
            s.AppendLine("OutputDir=" + outDir);
            s.AppendLine("OutputBaseFilename=" + SetupName);
            s.AppendLine("Compression=lzma2/ultra64");
            s.AppendLine("SolidCompression=yes");
            s.AppendLine("LZMAUseSeparateProcess=yes");
            s.AppendLine();
            s.AppendLine("[Languages]");
            s.AppendLine("Name: \"russian\"; MessagesFile: \"compiler:Languages\\Russian.isl\"");
            s.AppendLine();
            s.AppendLine("[Tasks]");
            s.AppendLine("Name: \"desktopicon\"; Description: \"{cm:CreateDesktopIcon}\"; GroupDescription: \"{cm:AdditionalIcons}\"");
            s.AppendLine();
            s.AppendLine("[InstallDelete]");
            s.AppendLine("; данные прошлой версии убираем целиком — у новой сборки другой набор файлов");
            s.AppendLine("Type: filesandordirs; Name: \"{app}\\Stazher_Data\"");
            s.AppendLine("Type: filesandordirs; Name: \"{app}\\MonoBleedingEdge\"");
            s.AppendLine("Type: filesandordirs; Name: \"{app}\\D3D12\"");
            s.AppendLine();
            s.AppendLine("[Files]");
            s.AppendLine("Source: \"" + buildDir + "\\*\"; DestDir: \"{app}\"; Excludes: \"*DontShip*,*DoNotShip*\"; Flags: ignoreversion recursesubdirs createallsubdirs");
            s.AppendLine();
            s.AppendLine("[Icons]");
            s.AppendLine("Name: \"{autoprograms}\\Стажёр\"; Filename: \"{app}\\Stazher.exe\"; WorkingDir: \"{app}\"");
            s.AppendLine("Name: \"{autodesktop}\\Стажёр\"; Filename: \"{app}\\Stazher.exe\"; WorkingDir: \"{app}\"; Tasks: desktopicon");
            s.AppendLine();
            s.AppendLine("[Run]");
            s.AppendLine("Filename: \"{app}\\Stazher.exe\"; Description: \"{cm:LaunchProgram,Стажёр}\"; Flags: nowait postinstall skipifsilent");
            s.AppendLine();
            s.Append(CodeSection);
            return s.ToString();
        }

        // Перед установкой и удалением: игру из этой папки просим закрыться (как крестиком окна — она успевает сохраниться),
        // через 12 с — закрываем принудительно; так же ждём её «Unity Crash Handler». Restart Manager сам этого не может:
        // помощник Unity на просьбу закрыться не отвечает, и обновление поверх запущенной игры обрывалось.
        // Другие копии игры (например, сборки в другой папке) не трогаем — только запущенные из {app}.
        const string CodeSection = @"[Code]
function OurProcs(const Exe: String; Mode: Integer): Integer;
var
  Locator, Service, Procs, P: Variant;
  i, rc, Pid: Integer;
  App, Path, Args: String;
begin
  Result := 0;
  App := Lowercase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''' + Exe + '''');
    for i := 0 to Procs.Count - 1 do
    begin
      P := Procs.ItemIndex(i);
      Path := '';
      try
        Path := P.ExecutablePath;
        Path := Lowercase(Path);
      except
      end;
      if Pos(App, Path) = 1 then
      begin
        Result := Result + 1;
        Pid := P.ProcessId;
        Args := '/PID ' + IntToStr(Pid);
        if Mode = 2 then Args := '/F ' + Args;
        if Mode > 0 then Exec(ExpandConstant('{sys}\taskkill.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, rc);
      end;
    end;
  except
    Log('Список процессов недоступен: ' + GetExceptionMessage);
  end;
end;

procedure CloseGame;
var
  i: Integer;
begin
  if OurProcs('Stazher.exe', 1) > 0 then
  begin
    Log('Игра запущена — просим закрыться');
    i := 0;
    while (i < 48) and (OurProcs('Stazher.exe', 0) > 0) do begin Sleep(250); i := i + 1; end;
    if OurProcs('Stazher.exe', 0) > 0 then begin Log('Игра не закрылась — закрываем принудительно'); OurProcs('Stazher.exe', 2); Sleep(500); end;
  end;
  i := 0;
  while (i < 20) and (OurProcs('UnityCrashHandler64.exe', 0) > 0) do begin Sleep(250); i := i + 1; end;
  if OurProcs('UnityCrashHandler64.exe', 0) > 0 then OurProcs('UnityCrashHandler64.exe', 2);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  CloseGame;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then CloseGame;
end;
";

        // ---------- проверка на этом компьютере ----------
        [MenuItem("Стажёр/Проверить установщик", false, 6)]
        public static void Test()
        {
            var log = new StringBuilder("installer test " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            string report = Path.Combine(Root, "Temp", "installer_test.txt");
            File.WriteAllText(Out, "installer test: идёт, отчёт — Temp/installer_test.txt\n", new UTF8Encoding(false));
            new System.Threading.Thread(() =>
            {
                int ok = 0, bad = 0;
                Action<bool, string> check = (cond, what) => { if (cond) ok++; else bad++; log.AppendLine((cond ? "ок   " : "ОШИБ ") + what); };
                try
                {
                    var setup = Directory.GetFiles(Path.Combine(Root, "Builds"), SetupName + ".exe", SearchOption.AllDirectories).OrderBy(File.GetLastWriteTime).LastOrDefault();
                    if (setup == null) { log.AppendLine("установщика нет — сначала «Собрать установщик»"); bad++; return; }
                    log.AppendLine("установщик: " + setup);
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string app = Path.Combine(local, "Programs", "Stazher"), exe = Path.Combine(app, "Stazher.exe");
                    string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "Codezilla Games", "Стажёр");
                    string startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Стажёр.lnk");
                    string deskLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Стажёр.lnk");
                    string key = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppIdPlain + "_is1";

                    // 1. установка
                    var t0 = DateTime.Now;
                    int c = Run(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /LOG=" + Q(Path.Combine(Root, "Temp", "inst_1.log")), log, 10 * 60000);
                    check(c == 0, "установка без вопросов и без прав администратора (код " + c + ", " + (DateTime.Now - t0).TotalSeconds.ToString("0") + " с)");
                    check(File.Exists(exe), "игра в " + app);
                    check(File.Exists(startLnk), "ярлык в «Пуске»: " + startLnk);
                    check(File.Exists(deskLnk), "ярлык на рабочем столе: " + deskLnk);
                    check(Cmd("reg", "query " + Q(key) + " /v DisplayName", log, 20000) == 0, "запись в «Параметры → Приложения» (" + key + ")");
                    check(!Directory.GetDirectories(app, "*DontShip*", SearchOption.TopDirectoryOnly).Any(), "служебные папки Unity не поставились");
                    // метка «скачано из интернета» (из-за неё в zip приходилось «Разблокировать») — у установленных файлов её нет
                    var zl = new StringBuilder();
                    Cmd("powershell", "-NoProfile -Command \"(Get-Item -LiteralPath '" + exe + "' -Stream * | Where-Object Stream -eq 'Zone.Identifier' | Measure-Object).Count\"", zl, 30000);
                    check(zl.ToString().Contains("\n0") || zl.ToString().TrimEnd().EndsWith("0"), "у Stazher.exe нет метки «из интернета» — «Разблокировать» не нужно");

                    // 2. установленная игра запускается и проходит самопроверку
                    string self = Path.Combine(data, "selftest.txt");
                    var st0 = DateTime.Now;
                    c = Run(exe, "-selftest -screen-fullscreen 0 -screen-width 640 -screen-height 360", log, 15 * 60000, app);
                    string selfText = File.Exists(self) && File.GetLastWriteTime(self) >= st0 ? File.ReadAllText(self) : "";
                    var total = selfText.Split('\n').FirstOrDefault(l => l.StartsWith("итог"));
                    check(selfText.Length > 0 && total != null && total.Contains(" 0 ошибок"), "самопроверка установленной игры: " + (total ?? "отчёта нет").Trim());
                    var ver = System.Text.RegularExpressions.Regex.Match(selfText, @"^Стажёр (\S+) ·", System.Text.RegularExpressions.RegexOptions.Multiline);
                    log.AppendLine("     версия установленной игры: " + (ver.Success ? ver.Groups[1].Value : "?"));

                    // 3. обновление поверх запущенной игры: установщик закрывает игру и ставит файлы заново
                    bool saves0 = Directory.Exists(data);
                    var game = Process.Start(new ProcessStartInfo(exe, "-screen-fullscreen 0 -screen-width 640 -screen-height 360") { WorkingDirectory = app, UseShellExecute = false });
                    System.Threading.Thread.Sleep(15000);
                    check(!game.HasExited, "игра запущена перед обновлением (pid " + game.Id + ")");
                    var exeTime = File.GetLastWriteTimeUtc(exe);
                    t0 = DateTime.Now;
                    c = Run(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /CLOSEAPPLICATIONS /LOG=" + Q(Path.Combine(Root, "Temp", "inst_2.log")), log, 10 * 60000);
                    game.WaitForExit(20000);
                    check(c == 0, "обновление поверх (код " + c + ", " + (DateTime.Now - t0).TotalSeconds.ToString("0") + " с)");
                    check(game.HasExited, "установщик закрыл запущенную игру");
                    if (!game.HasExited) { try { game.Kill(); } catch (Exception) { } }
                    check(File.Exists(exe) && File.Exists(Path.Combine(app, "unins000.exe")), "после обновления игра на месте, деинсталлятор один");
                    check(!Directory.Exists(Path.Combine(local, "Programs", "Stazher (2)")) && Directory.GetFiles(app, "unins001.exe").Length == 0, "вторая копия не появилась");
                    check(!saves0 || Directory.Exists(data), "сохранения после обновления на месте (" + data + ")");

                    // 4. удаление: игра и ярлыки уходят, сохранения и скриншоты остаются
                    c = Run(Path.Combine(app, "unins000.exe"), "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART", log, 5 * 60000);
                    for (int i = 0; i < 30 && File.Exists(exe); i++) System.Threading.Thread.Sleep(1000);   // деинсталлятор доделывает в копии из Temp
                    check(c == 0, "удаление (код " + c + ")");
                    check(!File.Exists(exe), "игра удалена");
                    check(!File.Exists(startLnk) && !File.Exists(deskLnk), "ярлыки удалены");
                    check(Cmd("reg", "query " + Q(key), new StringBuilder(), 20000) != 0, "запись в «Параметры → Приложения» удалена");
                    check(!saves0 || Directory.Exists(data), "сохранения и скриншоты остались");
                }
                catch (Exception e) { bad++; log.AppendLine("ошибка: " + e); }
                finally
                {
                    log.AppendLine("итог: " + ok + " ок, " + bad + " ошибок");
                    File.WriteAllText(report, log.ToString(), new UTF8Encoding(false));
                    File.WriteAllText(Out, "installer test: " + ok + " ок, " + bad + " ошибок — Temp/installer_test.txt\n", new UTF8Encoding(false));
                }
            }) { IsBackground = true }.Start();
        }

        // ---------- процессы ----------
        static string Q(string s) { return "\"" + s + "\""; }

        static int Run(string exe, string args, StringBuilder log, int timeoutMs, string wd = null)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = wd ?? Path.GetDirectoryName(exe) };
                using (var p = Process.Start(psi))
                {
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } log.AppendLine("$ " + Path.GetFileName(exe) + " " + args + "  → таймаут"); return -1; }
                    log.AppendLine("$ " + Path.GetFileName(exe) + " " + args + "  → " + p.ExitCode);
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log.AppendLine("$ " + exe + "  → не запустилось: " + ex.Message); return -2; }
        }

        static int Cmd(string exe, string args, StringBuilder log, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } log.AppendLine("$ " + exe + " " + args + "  → таймаут"); return -1; }
                    log.AppendLine("$ " + Path.GetFileName(exe) + " " + args + "  → " + p.ExitCode);
                    if (o.Result.Length > 0) log.AppendLine(o.Result.TrimEnd());
                    if (e.Result.Length > 0) log.AppendLine(e.Result.TrimEnd());
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { log.AppendLine("$ " + exe + " " + args + "  → не запустилось: " + ex.Message); return -2; }
        }
    }
}
