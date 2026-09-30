// Сборка для Windows: меню «Стажёр → Собрать релиз для Windows» (Builds/Stazher-<версия>-win64) и
// «Стажёр → Сборка для проверки» (версия 0.9.0-dev, Builds/Stazher-0.9.0-dev-<дата>-win64 — не релиз, никуда не выкладывается).
// Выставляет имя игры, студию, версию и иконку и пишет короткий отчёт в Temp/release_build.txt.
// Релиз сам кладёт в папку «Прочитай.txt» с версией и упаковывает Builds/Stazher-<версия>-win64.zip (без папок DoNotShip/DontShip).
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Intern.EditorTools
{
    public static class ReleaseBuild
    {
        public const string Version = "0.8.0";
        public const string DevVersion = "0.9.0-dev";   // следующий бэклог — между спринтами только сборки для проверки
        const string IconPath = "Assets/Intern 3/Assets/Intern/Branding/icon.png";

        [MenuItem("Стажёр/Собрать релиз для Windows", false, 1)]
        public static void BuildWindows()
        {
            string folder = "Stazher-" + Version + "-win64";
            if (Build(Version, folder)) Package(Version, folder);
        }

        [MenuItem("Стажёр/Сборка для проверки (dev)", false, 3)]
        public static void BuildDev() { Build(DevVersion, "Stazher-" + DevVersion + "-" + DateTime.Now.ToString("MMdd-HHmm") + "-win64"); }

        static bool Build(string version, string folder)
        {
            ApplyPlayerSettings(version);
            EnsureFogVariants();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            string dir = Path.Combine(root, folder);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var opts = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(sc => sc.enabled).Select(sc => sc.path).ToArray(),
                locationPathName = Path.Combine(dir, "Stazher.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            var started = DateTime.Now;
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            var sb = new StringBuilder();
            sb.AppendLine("result: " + s.result);
            sb.AppendLine("path: " + dir);
            sb.AppendLine("size_mb: " + (s.totalSize / 1048576.0).ToString("0.0"));
            sb.AppendLine("errors: " + s.totalErrors + ", warnings: " + s.totalWarnings);
            sb.AppendLine("time_s: " + (DateTime.Now - started).TotalSeconds.ToString("0"));
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception) sb.AppendLine("ERR " + m.content);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "release_build.txt")), sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Стажёр] Сборка " + version + ": " + s.result + ", " + (s.totalSize / 1048576.0).ToString("0.0") + " МБ → " + dir);
            return s.result == BuildResult.Succeeded;
        }

        // Дымка над городом (CityAtmosphere) — линейный туман: без этих настроек Unity выкидывает варианты шейдеров с туманом,
        // потому что в сцене туман выключен, и в сборке город остаётся без дымки
        static void EnsureFogVariants()
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var strip = so.FindProperty("m_FogStripping");
            var lin = so.FindProperty("m_FogKeepLinear");
            if (strip == null || lin == null) return;
            if (strip.intValue == 1 && lin.boolValue) return;
            strip.intValue = 1; lin.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[Стажёр] Туман: варианты шейдеров с линейным туманом включены в сборку");
        }

        // Текст «Прочитай.txt» в архиве релиза; {0} — версия
        const string Readme =
            "Стажёр {0} — симулятор стажёра в IT-компании (Codezilla Games)\r\n\r\n" +
            "Запуск: Stazher.exe. Если Windows покажет «Система Windows защитила ваш компьютер» —\r\n" +
            "«Подробнее» → «Выполнить в любом случае» (игра не подписана сертификатом).\r\n\r\n" +
            "Управление: WASD — ходить, Shift — бег, Space — прыжок, E — действие, V — вид, Esc — пауза, F12 — скриншот.\r\n" +
            "За компьютером: Ctrl+Enter — проверить, Ctrl+F5 — запустить, F5 — отладка (Python), 1–9 — выбрать ответ, Esc — встать.\r\n\r\n" +
            "Языки: основной язык выбирается в паузе. Для терминала и компилируемых языков (Go, Java, C#, C++, Rust,\r\n" +
            "PHP, Kotlin, Swift) нужен Docker Desktop — образ компилятора скачается один раз, при первой задаче.\r\n" +
            "Без Docker задачи на этих языках проверяются по коду.\r\n\r\n" +
            "Сохранения и скриншоты: %USERPROFILE%\\AppData\\LocalLow\\Codezilla Games\\Стажёр\r\n" +
            "Самопроверка: Stazher.exe -selftest (отчёт selftest.txt в той же папке).\r\n\r\n" +
            "Исходники и новости: https://github.com/GureganHokex/stazher\r\n";

        // «Прочитай.txt» и zip-архив релиза: папка с игрой внутри архива, служебные папки Unity не попадают
        static void Package(string version, string folder)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            string dir = Path.Combine(root, folder), zip = Path.Combine(root, folder + ".zip");
            File.WriteAllText(Path.Combine(dir, "Прочитай.txt"), string.Format(Readme, version), new UTF8Encoding(true));
            if (File.Exists(zip)) File.Delete(zip);
            int n = 0;
            using (var za = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
                {
                    string rel = f.Substring(dir.Length + 1).Replace('\\', '/');
                    if (rel.Split('/').Any(part => part.Contains("DoNotShip") || part.Contains("DontShip"))) continue;
                    za.CreateEntryFromFile(f, folder + "/" + rel, System.IO.Compression.CompressionLevel.Optimal);
                    n++;
                }
            string info = "zip: " + zip + ", файлов " + n + ", " + (new FileInfo(zip).Length / 1048576.0).ToString("0.0") + " МБ";
            File.AppendAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "release_build.txt")), info + "\n", new UTF8Encoding(false));
            Debug.Log("[Стажёр] " + info);
        }

        [MenuItem("Стажёр/Применить настройки релиза", false, 2)]
        public static void ApplyPlayerSettings() { ApplyPlayerSettings(Version); }

        static void ApplyPlayerSettings(string version)
        {
            PlayerSettings.companyName = "Codezilla Games";
            PlayerSettings.productName = "Стажёр";
            PlayerSettings.bundleVersion = version;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.forceSingleInstance = true;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon != null)
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
                int n = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Length;
                PlayerSettings.SetIcons(NamedBuildTarget.Standalone, Enumerable.Repeat(icon, n).ToArray(), IconKind.Any);
            }
            else Debug.LogWarning("[Стажёр] Иконка не найдена: " + IconPath);
            AssetDatabase.SaveAssets();
        }
    }
}
