// Проверка падений горожан (спринт 5 версии 0.9): команда ragtest ставит на площади и у стены несколько горожан,
// убивает их с разных сторон, на бегу, битой и рядом друг с другом и снимает кадры 15 раз в секунду
// в Temp/rag/<метка>_<сцена>/f_###.jpg (960×540). Метка — из Temp/ragtag.txt (по умолчанию cur).
// В журнал Temp/devtools.txt — где оказалось тело: нижняя точка (не ушло ли под землю), таз, голова.
// Только в редакторе: в сборку не попадает.
#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Intern.Game
{
    public class RagTest : MonoBehaviour
    {
        class Spawn { public string id; public Vector3 at; public float yaw; public Vector3 run; }
        class Kill { public int who; public float t; public Vector3 dir; public float knock; }
        class Scene { public string name; public string pose; public float dur; public Spawn[] spawns; public Kill[] kills; }

        static readonly Scene[] Scenes =
        {
            new Scene { name = "front", pose = "0 0.1 357.4 0 3 1", dur = 3.5f,
                spawns = new[] { new Spawn { id = "lawyer", at = new Vector3(-1.3f, 0, 361.5f), yaw = 180 }, new Spawn { id = "poet", at = new Vector3(1.5f, 0, 362f), yaw = 180 } },
                kills = new[] { new Kill { who = 0, t = 0.4f, dir = new Vector3(0.1f, -0.05f, 1f) }, new Kill { who = 1, t = 1.2f, dir = new Vector3(-0.2f, -0.05f, 1f), knock = 0.25f } } },
            new Scene { name = "side", pose = "0 0.1 357.4 0 3 1", dur = 3f,
                spawns = new[] { new Spawn { id = "historian", at = new Vector3(0, 0, 361.5f), yaw = 90 } },
                kills = new[] { new Kill { who = 0, t = 0.4f, dir = new Vector3(1f, 0f, 0.15f) } } },
            new Scene { name = "run", pose = "0 0.1 357.4 0 3 1", dur = 3.5f,
                spawns = new[] { new Spawn { id = "philologist", at = new Vector3(-4.5f, 0, 361f), yaw = 90, run = new Vector3(4.5f, 0, 0) } },
                kills = new[] { new Kill { who = 0, t = 0.8f, dir = new Vector3(0f, -0.1f, 1f) } } },
            new Scene { name = "bat", pose = "0 0.1 357.4 0 3 1", dur = 3.5f,
                spawns = new[] { new Spawn { id = "dean", at = new Vector3(0, 0, 361.2f), yaw = 180 } },
                kills = new[] { new Kill { who = 0, t = 0.4f, dir = new Vector3(0f, 0f, 1f), knock = 1.6f } } },
            new Scene { name = "wall", pose = "-1.2 0.1 327.2 -55 12 1", dur = 3f,
                spawns = new[] { new Spawn { id = "journalist", at = new Vector3(-6.1f, 0, 330f), yaw = 90 } },
                kills = new[] { new Kill { who = 0, t = 0.4f, dir = new Vector3(-1f, 0f, 0.1f), knock = 0.3f } } },
            new Scene { name = "pile", pose = "0 0.1 357.4 0 3 1", dur = 4f,
                spawns = new[] { new Spawn { id = "critic", at = new Vector3(-0.45f, 0, 361.2f), yaw = 180 }, new Spawn { id = "critic", at = new Vector3(0.45f, 0, 362f), yaw = 180 } },
                kills = new[] { new Kill { who = 0, t = 0.3f, dir = new Vector3(0.7f, 0f, 1f) }, new Kill { who = 1, t = 1.5f, dir = new Vector3(-0.3f, 0f, 1f) } } },
            Mass(),
        };

        // Нагрузка: 16 горожан сеткой, все падают за секунду — сколько тел двигается разом и какой кадр
        static Scene Mass()
        {
            string[] ids = { "lawyer", "poet", "critic", "historian", "philologist", "journalist", "philosopher", "notary" };
            var sp = new List<Spawn>(); var ks = new List<Kill>();
            for (int i = 0; i < 16; i++)
            {
                sp.Add(new Spawn { id = ids[i % ids.Length], at = new Vector3(-3f + (i % 4) * 2f, 0, 361f + (i / 4) * 1.6f), yaw = 180 });
                ks.Add(new Kill { who = i, t = 0.3f + i * 0.06f, dir = new Vector3(Mathf.Sin(i * 1.7f) * 0.6f, 0f, 1f) });
            }
            return new Scene { name = "mass", pose = "0 0.1 357.4 0 6 1", dur = 4f, spawns = sp.ToArray(), kills = ks.ToArray() };
        }

        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Out { get { return Path.Combine(Root, "Temp", "devtools.txt"); } }

        // Дома, куда можно войти (спринт 6): у каждой двери — снаружи, внутри от входа, из середины, из глубины, после стычки
        public static void LaunchHalls()
        {
            if (!Application.isPlaying) { File.WriteAllText(Out, "halltest: нужен режим Play\n", new UTF8Encoding(false)); return; }
            var go = new GameObject("HallTest"); var t = go.AddComponent<RagTest>(); t.halls = true;
        }
        bool halls;

        IEnumerator HallsRun()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            log.AppendLine("halltest " + DateTime.Now.ToString("HH:mm:ss"));
            var gr = FindFirstObjectByType<GameRoot>(); var pl = FindFirstObjectByType<PlayerController>();
            if (gr == null || pl == null) { Finish("нет игры"); yield break; }
            gr.UiResume(); gr.DevCity();
            var city = gr.DevCityRefs;
            var run = new LunchRun(city, 600f, () => pl.Position, () => pl.cam.transform, false, false);
            run.Cleanup();
            string dir = Path.Combine(Root, "Temp", "rag", "halls"); Directory.CreateDirectory(dir);
            log.AppendLine("домов: " + city.halls.Count);
            var blog = new StringBuilder();
            foreach (var bl in city.blockers) blog.AppendLine(string.Format(ci, "{0:0.000} {1:0.000} {2:0.000} {3:0.000}", bl.xMin, bl.xMax, bl.yMin, bl.yMax));
            File.WriteAllText(Path.Combine(Root, "Temp", "blockers.txt"), blog.ToString());
            foreach (var h in city.halls)
            {
                var outN = h.doorOut - h.doorIn; outN.y = 0; outN.Normalize();
                var inN = -outN; var side = Vector3.Cross(Vector3.up, inN);
                float yawIn = Mathf.Atan2(inN.x, inN.z) * Mathf.Rad2Deg;
                log.AppendLine(string.Format(ci, "{0} «{1}»: внутри {2:0.0}×{3:0.0} м, кресел {4}, укрытий {5}, узлов {6}", h.id, h.name, h.rect.width, h.rect.height, h.seats.Count, h.hides.Count, h.nodes.Count));
                // у самой двери: подсказка «E Войти» и плашка
                Pose(pl, h.doorOut + outN * 0.2f, yawIn, 6f, false);
                yield return new WaitForSeconds(0.6f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_0door.jpg"));
                // снаружи, дверь закрыта
                Pose(pl, h.doorOut + outN * 3.5f, yawIn, 6f, false);
                yield return new WaitForSeconds(0.6f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_1out.jpg"));
                h.door.Open(); run.DevSpawnHall(h.id); run.Paused = true;
                yield return new WaitForSeconds(0.6f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_2open.jpg"));
                Pose(pl, h.doorIn + inN * 0.2f, yawIn, 10f, true);
                yield return new WaitForSeconds(0.5f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_3in.jpg"));
                // FPS внутри (без снимков)
                var dts = new List<float>(); float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 1f) { yield return null; dts.Add(Time.unscaledDeltaTime); }
                log.AppendLine(string.Format(ci, "  кадр внутри {0:0.0} мс", dts.Average() * 1000f));
                var c = new Vector3(h.rect.center.x, 0.1f, h.rect.center.y);
                Pose(pl, c - inN * 1.0f, Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg, 8f, true);
                yield return new WaitForSeconds(0.4f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_4side.jpg"));
                float depth = Mathf.Abs(inN.x) > 0.5f ? h.rect.width : h.rect.height;
                Pose(pl, h.doorIn + inN * (depth - 4.2f) + side * 1.6f, yawIn + 180f, 10f, true);
                yield return new WaitForSeconds(0.4f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_5back.jpg"));
                // стычка: от входа, кто заметит — реагирует; двоих гуманитариев выбиваем
                Pose(pl, h.doorIn, yawIn, 8f, true);
                run.Paused = false;
                yield return new WaitForSeconds(1.2f);
                int killed = 0;
                foreach (var n in FindObjectsByType<CityNpc>(FindObjectsSortMode.None))
                    if (n.hall == h && n.Alive && n.Humanitarian && killed < 2) { var d = n.transform.position - pl.Position; d.y = 0; n.DebugKill(d.normalized, 0f); killed++; }
                yield return new WaitForSeconds(2.5f);
                yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_6fight.jpg"));
                int alive = 0, outside = 0, stuck = 0;
                foreach (var n in FindObjectsByType<CityNpc>(FindObjectsSortMode.None))
                {
                    if (n.hall != h || !n.Alive) continue; alive++;
                    if (!h.Contains(n.transform.position)) outside++;
                    foreach (var b in city.blockers) if (b.Contains(new Vector2(n.transform.position.x, n.transform.position.z))) { stuck++; break; }
                }
                log.AppendLine("  после стычки: живых " + alive + ", вышли на улицу " + outside + ", внутри мебели " + stuck + ", дверь " + (h.door.IsOpen ? "открыта" : "закрыта"));
                if (h.id == "acc" || h.id == "law")
                {
                    // ещё 6 с: убегающие должны выйти через дверь, никто не застревает (след каждые 0,5 с)
                    var trace = new Dictionary<CityNpc, StringBuilder>();
                    for (int k = 0; k < 12; k++)
                    {
                        foreach (var n in FindObjectsByType<CityNpc>(FindObjectsSortMode.None))
                        {
                            if (n.hall != h || !n.Alive || !n.gameObject.activeSelf) continue;
                            StringBuilder tb; if (!trace.TryGetValue(n, out tb)) trace[n] = tb = new StringBuilder();
                            tb.Append(string.Format(ci, " ({0:0.0} {1:0.0})", n.transform.position.x, n.transform.position.z));
                        }
                        yield return new WaitForSeconds(0.5f);
                    }
                    foreach (var kv in trace) if (kv.Key != null && kv.Key.State == CityNpc.St.Flee) log.AppendLine("    след " + kv.Key.Type + ":" + kv.Value);
                    foreach (var kv in trace)
                    {
                        if (kv.Key == null || kv.Key.State != CityNpc.St.Flee) continue;
                        var q = kv.Key.transform.position;
                        foreach (var bl in city.blockers)
                            if (q.x > bl.xMin - 0.6f && q.x < bl.xMax + 0.6f && q.z > bl.yMin - 0.6f && q.z < bl.yMax + 0.6f)
                                log.AppendLine(string.Format(ci, "      рядом препятствие x {0:0.00}..{1:0.00}, z {2:0.00}..{3:0.00}", bl.xMin, bl.xMax, bl.yMin, bl.yMax));
                    }
                    alive = outside = 0; int moving = 0;
                    foreach (var n in FindObjectsByType<CityNpc>(FindObjectsSortMode.None))
                    {
                        if (n.hall != h || !n.Alive || !n.gameObject.activeSelf) continue; alive++;
                        if (!h.Contains(n.transform.position)) outside++;
                        log.AppendLine(string.Format(ci, "    {0}: {1}, {2}, {3}", n.Type, n.State, h.Contains(n.transform.position) ? "внутри" : "снаружи", n.DevPathInfo));
                    }
                    log.AppendLine("  через 6 с: на месте " + alive + " (снаружи " + outside + "), убежали " + run.escaped);
                    // декан снаружи бежит к стажёру, который стоит внутри
                    Pose(pl, h.doorIn + inN * 4f, yawIn + 180f, 8f, true);
                    var dean = run.DevSpawn("dean", h.doorOut + outN * 7f, 0f);
                    dean.DevUnfreeze();
                    yield return new WaitForSeconds(0.3f);
                    dean.Alert();
                    float tt = Time.time; float best = 99f;
                    while (Time.time - tt < 9f && dean != null && dean.Alive)
                    {
                        var d = dean.transform.position - pl.Position; d.y = 0; best = Mathf.Min(best, d.magnitude);
                        if (best < 1.6f) break;
                        yield return null;
                    }
                    log.AppendLine(string.Format(ci, "  декан с улицы: ближе всего {0:0.0} м за {1:0.0} с, {2}", best, Time.time - tt, dean != null && h.Contains(dean.transform.position) ? "вошёл в дом" : "остался снаружи"));
                    yield return new WaitForEndOfFrame(); Capture(Path.Combine(dir, h.id + "_7dean.jpg"));
                }
                run.Cleanup();
                yield return null;
            }
            Finish("готово");
        }

        static void Pose(PlayerController pl, Vector3 at, float yaw, float pitch, bool fp)
        {
            pl.Teleport(new Vector3(at.x, 0.1f, at.z), yaw); pl.SetCamPitch(pitch);
            if (fp != pl.firstPerson) pl.ToggleView();
        }

        public static void Launch()
        {
            if (!Application.isPlaying) { File.WriteAllText(Out, "ragtest: нужен режим Play\n", new UTF8Encoding(false)); return; }
            if (FindFirstObjectByType<RagTest>() != null) return;
            new GameObject("RagTest").AddComponent<RagTest>();
        }

        readonly StringBuilder log = new StringBuilder();

        IEnumerator Start()
        {
            if (halls) { yield return HallsRun(); yield break; }
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            string tagFile = Path.Combine(Root, "Temp", "ragtag.txt");
            string tag = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "cur";
            string only = null;
            if (tag.Contains(" ")) { var parts = tag.Split(' '); tag = parts[0]; only = parts[1]; }
            log.AppendLine("ragtest " + tag + " " + DateTime.Now.ToString("HH:mm:ss"));
            Intern.Game.Ragdoll.Enabled = tag != "before";   // «до» — старое падение доской
            Intern.Game.Ragdoll.DevTrace = true;
            int savedQ = GameConfig.S.quality;
            if (tag == "low") { GameConfig.SetQuality(0); GameConfig.ApplyGraphics(); }   // «Низкое»: упрощённые тела
            var gr = FindFirstObjectByType<GameRoot>();
            var pl = FindFirstObjectByType<PlayerController>();
            if (gr == null || pl == null) { Finish("нет игры"); yield break; }
            gr.UiResume();
            gr.DevCity();
            var city = gr.DevCityRefs;
            var run = new LunchRun(city, 600f, () => pl.Position, () => pl.cam.transform, false, false);
            run.Cleanup();
            foreach (var sc in Scenes)
            {
                if (only != null && sc.name != only) continue;
                var v = sc.pose.Split(' ').Select(x => float.Parse(x, ci)).ToArray();
                pl.Teleport(new Vector3(v[0], v[1], v[2]), v[3]); pl.SetCamPitch(v[4]);
                if ((v[5] > 0.5f) != pl.firstPerson) pl.ToggleView();
                var npcs = new List<CityNpc>();
                foreach (var s in sc.spawns)
                {
                    var n = run.DevSpawn(s.id, s.at, s.yaw);
                    if (n == null) { log.AppendLine(sc.name + ": нет горожанина " + s.id); continue; }
                    npcs.Add(n);
                }
                yield return new WaitForSeconds(0.8f);
                for (int i = 0; i < sc.spawns.Length && i < npcs.Count; i++) npcs[i].DevRunVel = sc.spawns[i].run;
                string dir = Path.Combine(Root, "Temp", "rag", tag + "_" + sc.name);
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
                float t0 = Time.time, next = 0f; int frame = 0; int ki = 0; int maxMoving = 0; var dts = new List<float>();
                var kills = sc.kills.OrderBy(k => k.t).ToArray();
                while (Time.time - t0 < sc.dur)
                {
                    float t = Time.time - t0;
                    while (ki < kills.Length && t >= kills[ki].t)
                    {
                        var k = kills[ki++];
                        if (k.who < npcs.Count && npcs[k.who] != null) { npcs[k.who].DevRunVel = Vector3.zero; npcs[k.who].DebugKill(k.dir.normalized, k.knock); }
                    }
                    yield return new WaitForEndOfFrame();
                    maxMoving = Mathf.Max(maxMoving, Intern.Game.Ragdoll.Moving);
                    bool shot = t >= next && sc.name != "mass";   // в нагрузочной сцене не снимаем — меряем кадр
                    if (!shot) dts.Add(Time.unscaledDeltaTime);
                    if (shot) { Capture(Path.Combine(dir, "f_" + frame.ToString("000") + ".jpg")); frame++; next += 1f / 15f; }
                }
                dts.Sort();
                float avgMs = dts.Count > 0 ? dts.Average() * 1000f : 0f, worstMs = dts.Count > 0 ? dts[dts.Count - 1 - dts.Count / 100] * 1000f : 0f;
                log.AppendLine(sc.name + ": кадров " + frame + string.Format(ci, ", в движении тел до {0}, кадр {1:0.0} мс (худший 1% {2:0.0} мс)", maxMoving, avgMs, worstMs));
                foreach (var n in npcs)
                {
                    if (n == null) continue;
                    float low = float.MaxValue;
                    foreach (var r in n.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy && !r.name.StartsWith("Blob")) low = Mathf.Min(low, r.bounds.min.y);
                    var a = n.Anim;
                    Vector3 hips = a != null && a.hips != null ? a.hips.position : n.transform.position;
                    Vector3 head = a != null && a.head != null ? a.head.position : n.transform.position;
                    log.AppendLine(string.Format(ci, "  {0}: низ {1:0.00}, таз ({2:0.0} {3:0.00} {4:0.0}), голова ({5:0.0} {6:0.00} {7:0.0}), корень ({8:0.0} {9:0.0})",
                        n.Type, low, hips.x, hips.y, hips.z, head.x, head.y, head.z, n.transform.position.x, n.transform.position.z));
                    string extra = sc.name == "mass" ? null : Intern.Game.Ragdoll.DevReport(n.Anim);
                    if (!string.IsNullOrEmpty(extra)) log.AppendLine("    " + extra);
                }
                run.Cleanup();
                yield return null;
            }
            if (tag == "low") { GameConfig.SetQuality(savedQ); GameConfig.ApplyGraphics(); }
            Finish("готово");
        }

        void Finish(string msg)
        {
            Intern.Game.Ragdoll.Enabled = true; Intern.Game.Ragdoll.DevTrace = false;
            log.AppendLine(msg);
            File.WriteAllText(Out, log.ToString(), new UTF8Encoding(false));
            Destroy(gameObject);
        }

        // Кадр экрана, уменьшенный вдвое (среднее 2×2), в JPG
        static void Capture(string path)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int w = tex.width / 2, h = tex.height / 2;
            var src = tex.GetPixels32(); var dst = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = (y * 2) * tex.width + x * 2;
                    Color32 a = src[i], b = src[i + 1], c = src[i + tex.width], d = src[i + tex.width + 1];
                    dst[y * w + x] = new Color32((byte)((a.r + b.r + c.r + d.r) >> 2), (byte)((a.g + b.g + c.g + d.g) >> 2), (byte)((a.b + b.b + c.b + d.b) >> 2), 255);
                }
            var small = new Texture2D(w, h, TextureFormat.RGB24, false);
            small.SetPixels32(dst); small.Apply();
            File.WriteAllBytes(path, small.EncodeToJPG(88));
            Destroy(tex); Destroy(small);
        }
    }
}
#endif
