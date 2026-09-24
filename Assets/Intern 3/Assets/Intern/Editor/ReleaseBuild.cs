// Сборка релиза для Windows: меню «Стажёр → Собрать релиз для Windows».
// Выставляет имя игры, студию, версию и иконку, собирает Builds/Stazher-<версия>-win64/Stazher.exe
// и пишет короткий отчёт в Temp/release_build.txt.
using System;
using System.IO;
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
        public const string Version = "0.7.0";
        const string IconPath = "Assets/Intern 3/Assets/Intern/Branding/icon.png";

        [MenuItem("Стажёр/Собрать релиз для Windows", false, 1)]
        public static void BuildWindows()
        {
            ApplyPlayerSettings();
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds"));
            string dir = Path.Combine(root, "Stazher-" + Version + "-win64");
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
            Debug.Log("[Стажёр] Сборка " + Version + ": " + s.result + ", " + (s.totalSize / 1048576.0).ToString("0.0") + " МБ → " + dir);
        }

        [MenuItem("Стажёр/Применить настройки релиза", false, 2)]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Codezilla Games";
            PlayerSettings.productName = "Стажёр";
            PlayerSettings.bundleVersion = Version;
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
