// Замер графики в сборке: Stazher.exe -bench продолжает сохранённую игру, ставит камеру в ракурсы и на каждом пресете
// («Низкое» … «Ультра») даёт 2 с прогрева и 5 с замера без ограничения кадров, снимает экран и пишет bench.txt
// рядом с сохранениями (%USERPROFILE%\AppData\LocalLow\Codezilla Games\Стажёр; снимки — в папке bench). Потом выходит.
// В этом режиме игра ничего не сохраняет: прогресс и настройки игрока остаются как были.
// Свои ракурсы — файл bench_poses.txt там же: строки «имя|x y z поворот наклон вид» (вид 1 — от первого лица).
// Ракурсы с z > 200 — в городе: он строится и включается без обеда (без горожан и боя).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Intern.Game
{
    public class Bench : MonoBehaviour
    {
        public static bool Requested { get { return Environment.GetCommandLineArgs().Any(a => a == "-bench"); } }
        public static bool Running { get; private set; }
        public static bool NoSave;   // проверки из редактора (команды DevTools): игра идёт, но сохранение не трогаем

        static readonly string[] DefaultPoses = { "office|4.5 0.05 6.8 75 8 0", "hall|-5 0.08 -4.8 0 12 1", "lead|8.2 0.05 4.1 0 6 1",
                                                    "city|0 0.1 302.2 0 8 0", "square|-6 0.1 370 45 4 1", "buh|-10 0.1 322.5 -90 8 1" };
        const float Warmup = 2f, Measure = 5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested || SelfTest.Requested) return;
            Running = true;
            new GameObject("Bench").AddComponent<Bench>();
        }

        IEnumerator Start()
        {
            var sb = new StringBuilder();
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string dir = Path.Combine(Application.persistentDataPath, "bench");
            Directory.CreateDirectory(dir);
            sb.AppendLine("Стажёр " + Application.version + " · замер графики " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("видеокарта: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + "), процессор: " + SystemInfo.processorType);

            // мир собирается в первые кадры; потом — «Продолжить», если есть сохранение с персонажем
            float t0 = Time.realtimeSinceStartup;
            GameRoot root = null;
            while (Time.realtimeSinceStartup - t0 < 20f && (root == null || root.Save == null)) { root = FindFirstObjectByType<GameRoot>(); yield return null; }
            yield return new WaitForSecondsRealtime(2f);
            bool walk = false;
            if (root != null && root.Save != null && root.Save.hasCharacter && Progress.HasSave())
            {
                root.UiContinue();
                yield return new WaitForSecondsRealtime(1.5f);
                walk = root.CurMode == GameRoot.Mode.Walk;
            }
            sb.AppendLine("экран: " + Screen.width + "×" + Screen.height + (walk ? "" : ", сохранения нет — замер в главном меню"));

            var poses = DefaultPoses.ToList();
            string custom = Path.Combine(Application.persistentDataPath, "bench_poses.txt");
            if (File.Exists(custom)) { var l = File.ReadAllLines(custom).Select(x => x.Trim()).Where(x => x.Contains("|") && !x.StartsWith("#")).ToList(); if (l.Count > 0) poses = l; }
            var pl = FindFirstObjectByType<PlayerController>();

            var S = GameConfig.S;
            int savedQuality = S.quality;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;   // замер без ограничения кадров

            foreach (var pose in poses)
            {
                var parts = pose.Split(new[] { '|' }, 2);
                string name = parts[0].Trim();
                var v = parts[1].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (walk && pl != null && v.Length >= 5)
                {
                    if (float.Parse(v[2], ci) > 200f) root.DevCity();
                    pl.Teleport(new Vector3(float.Parse(v[0], ci), float.Parse(v[1], ci), float.Parse(v[2], ci)), float.Parse(v[3], ci));
                    pl.SetCamPitch(float.Parse(v[4], ci));
                    if (v.Length >= 6 && (v[5] == "1") != pl.firstPerson) pl.ToggleView();
                }
                sb.AppendLine();
                sb.AppendLine("ракурс «" + name + "»:");
                for (int q = 0; q < 4; q++)
                {
                    GameConfig.SetQuality(q); GameConfig.ApplyGraphics();
                    QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    yield return new WaitForSecondsRealtime(Warmup);
                    var times = new List<float>();
                    float start = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - start < Measure) { yield return null; times.Add(Time.unscaledDeltaTime); }
                    float avg = times.Count > 0 ? times.Count / times.Sum() : 0f;
                    times.Sort();
                    int n1 = Mathf.Max(1, times.Count / 100);
                    float worst = times.Skip(times.Count - n1).Average();                     // 1% худших кадров
                    string shot = Path.Combine(dir, name + "_" + q + ".png");
                    ScreenCapture.CaptureScreenshot(shot);
                    yield return null; yield return null;
                    sb.AppendLine(string.Format(ci, "  {0,-8} {1,5:0} FPS, 1% худших: {2:0} FPS ({3:0.0} мс)", GameConfig.QualityNames[q], avg, 1f / worst, worst * 1000f));
                    Debug.Log("[bench] " + name + " " + q + " " + avg.ToString("0", ci) + " FPS");
                }
            }
            GameConfig.SetQuality(savedQuality);
            sb.AppendLine();
            sb.AppendLine("снимки: " + dir);
            File.WriteAllText(Path.Combine(Application.persistentDataPath, "bench.txt"), sb.ToString(), new UTF8Encoding(false));
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }
    }
}
