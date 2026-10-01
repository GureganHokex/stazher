// Проверка анимаций (спринт 7 версии 0.9 «Живые персонажи»): команда animtest ставит на проспекте персонажа и гоняет
// его по сценам — шаг, разгон до спринта, поворот на месте, боковой ход, действия, посадка, прыжок, толпа.
// Камера едет рядом, кадры 30 в секунду игрового времени (Time.captureFramerate — ролик ровный, как бы долго ни снимался кадр)
// пишутся в Temp/anim/<метка>_<сцена>/f_####.jpg. Метка и сцена — из Temp/animtag.txt («after walk»); метка before —
// старая анимация кодом (для роликов «до/после»).
// В журнал Temp/devtools.txt: проскальзывание ступней — средняя скорость ступни, стоящей на земле (в идеале 0),
// и время кадра. Только в редакторе: в сборку не попадает.
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
    public class AnimTest : MonoBehaviour
    {
        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static string Out { get { return Path.Combine(Root, "Temp", "devtools.txt"); } }
        static readonly System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;

        public static void Launch()
        {
            if (!Application.isPlaying) { File.WriteAllText(Out, "animtest: нужен режим Play\n", new UTF8Encoding(false)); return; }
            if (FindFirstObjectByType<AnimTest>() != null) return;
            new GameObject("AnimTest").AddComponent<AnimTest>();
        }

        readonly StringBuilder log = new StringBuilder();
        PlayerController pl; GameRoot gr;
        string dir; int frame; bool capture = true; int every = 1;
        // проскальзывание: путь ступней по земле
        float slideSum, slideTime; CharacterAnim watch;
        readonly Vector3[] lastFoot = new Vector3[2], lastPts = new Vector3[4]; bool haveLast;
        readonly List<float> dts = new List<float>();
        StringBuilder trace;
        readonly float[] bandSlide = new float[5], bandTime = new float[5];

        const float Z0 = 318f;   // проспект: середина проезжей части
        // пустая площадка в клетку 1 м далеко от города: видно, стоит ли ступня на месте
        static readonly Vector3 G0 = new Vector3(400f, 0f, -400f);
        static readonly Vector3 SQ = G0 + new Vector3(-13f, 0f, -11f);   // прямая 26 м
        static readonly Vector3 SP = G0 + new Vector3(-8f, 0f, 6f);      // сцены на месте
        static readonly Vector3 FOUNT = G0;                              // бег по кругу r = 10 м
        static readonly Vector3 CITY = new Vector3(-8f, 0f, 369f);       // угол площади в городе (обед)

        static void Ground()
        {
            if (GameObject.Find("AT_Ground") != null) return;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "AT_Ground";
            g.transform.position = G0 + Vector3.down * 0.5f; g.transform.localScale = new Vector3(80f, 1f, 80f);
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(new[] { new Color(0.78f, 0.76f, 0.72f), new Color(0.62f, 0.6f, 0.57f), new Color(0.62f, 0.6f, 0.57f), new Color(0.78f, 0.76f, 0.72f) }); tex.Apply();
            var sh = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
            var m = new Material(sh);
            m.mainTexture = tex; if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTextureScale = new Vector2(40f, 40f); if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(40f, 40f));
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            g.GetComponent<Renderer>().sharedMaterial = m;
            // стойки по краям — ориентиры для глаза
            for (int i = -3; i <= 3; i++)
                foreach (var z in new[] { -20f, 20f })
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube); p.name = "AT_Post";
                    p.transform.position = G0 + new Vector3(i * 6f, 1f, z); p.transform.localScale = new Vector3(0.3f, 2f, 0.3f);
                    p.transform.SetParent(g.transform, true);
                }
        }

        IEnumerator Start()
        {
            string tagFile = Path.Combine(Root, "Temp", "animtag.txt");
            string tag = File.Exists(tagFile) ? File.ReadAllText(tagFile).Trim() : "after";
            string only = null;
            if (tag.Contains(" ")) { var p = tag.Split(' '); tag = p[0]; only = p[1]; }
            log.AppendLine("animtest " + tag + " " + DateTime.Now.ToString("HH:mm:ss") + ", клипов " + AnimLib.Count + (AnimLib.Error != null ? " (" + AnimLib.Error + ")" : ""));
            CharacterAnim.UseV4 = tag != "before";
            JointHelpers.Stretch = !tag.Contains("nostretch");
            gr = FindFirstObjectByType<GameRoot>();
            pl = FindFirstObjectByType<PlayerController>();
            if (gr == null || pl == null) { Finish("нет игры"); yield break; }
            gr.UiResume(); gr.DevCity();
            pl.Teleport(new Vector3(40f, 0.1f, 250f), 0f);          // стажёр в стороне — в кадр не попадает
            pl.cinematic = true;
            GameRoot.DevHideUi = true;
            Ground();
            var scenes = new List<KeyValuePair<string, Func<IEnumerator>>>
            {
                new KeyValuePair<string, Func<IEnumerator>>("walk", () => Walk(1.4f, 6f)),
                new KeyValuePair<string, Func<IEnumerator>>("speeds", Speeds),
                new KeyValuePair<string, Func<IEnumerator>>("turn", Turn),
                new KeyValuePair<string, Func<IEnumerator>>("strafe", Strafe),
                new KeyValuePair<string, Func<IEnumerator>>("tp", ThirdPerson),
                new KeyValuePair<string, Func<IEnumerator>>("acts", Acts),
                new KeyValuePair<string, Func<IEnumerator>>("sit", SitScene),
                new KeyValuePair<string, Func<IEnumerator>>("jump", JumpScene),
                new KeyValuePair<string, Func<IEnumerator>>("crowd", Crowd),
                new KeyValuePair<string, Func<IEnumerator>>("office", Office),
                new KeyValuePair<string, Func<IEnumerator>>("lunch", LunchScene),
                new KeyValuePair<string, Func<IEnumerator>>("player", PlayerScene),
            };
            foreach (var sc in scenes)
            {
                string name = sc.Key; var fn = sc.Value;
                if (only != null && name != only) continue;
                dir = Path.Combine(Root, "Temp", "anim", tag + "_" + name);
                Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
                frame = 0; slideSum = slideTime = 0f; haveLast = false; dts.Clear(); capture = name != "crowd"; every = 1;
                trace = new StringBuilder("pt h v t speed phase cL cR pL pR lx ly lz rx ry rz\n");
                for (int b = 0; b < 5; b++) bandSlide[b] = bandTime[b] = 0f;
                Time.captureFramerate = capture ? 30 : 0;
                float t0 = Time.realtimeSinceStartup;
                yield return fn();
                Time.captureFramerate = 0;
                string slide = slideTime > 0.2f ? string.Format(ci, ", ступня на земле скользит {0:0.00} м/с ({1:0.0} с на земле)", slideSum / slideTime, slideTime) : "";
                string ms = dts.Count > 0 ? string.Format(ci, ", кадр {0:0.0} мс", dts.Average() * 1000f) : "";
                log.AppendLine(string.Format(ci, "{0}: кадров {1}{2}{3}", name, frame, slide, ms));
                string[] bn = { "шаг", "между шагом и бегом", "бег", "спринт", "трусца 2.4" };
                for (int b = 0; b < 5; b++) if (bandTime[b] > 0.2f) log.AppendLine(string.Format(ci, "    {0}: {1:0.00} м/с ({2:0.0} с)", bn[b], bandSlide[b] / bandTime[b], bandTime[b]));
                File.WriteAllText(Path.Combine(Root, "Temp", "anim", tag + "_" + name + ".txt"), trace.ToString());
                foreach (var a in FindObjectsByType<CharacterAnim>(FindObjectsSortMode.None)) if (a.name.StartsWith("AT_")) Destroy(a.gameObject);
                yield return null;
            }
            CharacterAnim.UseV4 = true;
            pl.cinematic = false;
            Finish("готово");
        }

        static CharacterAnim Actor(string model, Vector3 pos, float yaw, int outfit = 0, int skin = 1)
        {
            var ap = new Appearance { outfit = outfit, skin = skin, accessory = 0 };
            var a = CharacterAnim.Spawn(model, null, pos, yaw, model == "Intern" ? ap : null);
            a.name = "AT_" + model;
            return a;
        }

        // камера сбоку/сзади/спереди от персонажа: az — угол вокруг него (0 — сбоку справа), dist, высота, куда смотреть
        void Cam(CharacterAnim a, float az, float dist, float h, float look = 0.95f, float fov = 38f)
        {
            var c = a.transform.position + Vector3.up * look;
            var d = Quaternion.Euler(0f, a.transform.eulerAngles.y + 90f + az, 0f) * Vector3.forward;
            var p = c + d * dist + Vector3.up * (h - look);
            pl.SetCamera(p, Quaternion.LookRotation(c - p), fov);
        }
        void CamFixed(Vector3 p, Vector3 look, float fov = 40f) { pl.SetCamera(p, Quaternion.LookRotation(look - p), fov); }

        // кадр: снимок + учёт проскальзывания
        IEnumerator Tick()
        {
            if (gr != null) { gr.DevKeepAlive(); gr.UiResume(); }
            yield return new WaitForEndOfFrame();
            dts.Add(Time.unscaledDeltaTime);
            if (watch != null) Slide(watch);
            if (capture && frame % every == 0) Capture(Path.Combine(dir, "f_" + (frame / every).ToString("0000") + ".jpg"));
            frame++;
        }

        void Slide(CharacterAnim a)
        {
            var fl = a.hips != null ? Find(a.transform, "FootL") : null; var fr = a.hips != null ? Find(a.transform, "FootR") : null;
            if (fl == null || fr == null) return;
            var ft = new[] { fl, fr };
            float dt = Time.deltaTime;
            float ground = a.transform.position.y;
            // опора — самая низкая точка подошв (пятка или подушечка); если она на земле, она не должна ехать
            var heel = new Vector3(0f, -0.1f, -0.072f); var ball = new Vector3(0f, -0.1f, 0.092f);
            int best = -1; float by = float.MaxValue; var pts = new Vector3[4];
            for (int i = 0; i < 2; i++) { pts[i * 2] = ft[i].position + ft[i].rotation * heel; pts[i * 2 + 1] = ft[i].position + ft[i].rotation * ball; }
            for (int k = 0; k < 4; k++) if (pts[k].y < by) { by = pts[k].y; best = k; }
            // скольжение — точка стоит на земле и в прошлом кадре, и сейчас (подлёт стопы к земле не считаем)
            // и не опускается на неё (кадр касания, когда стопа ещё в воздухе, — не скольжение)
            if (haveLast && dt > 0f && by - ground < 0.01f && lastPts[best].y - ground < 0.01f && Mathf.Abs(by - lastPts[best].y) < 0.006f && a.moveSpeed > 0.05f)
            {
                var d = pts[best] - lastPts[best]; d.y = 0f;
                slideSum += d.magnitude; slideTime += dt;
                int band = a.moveSpeed < 1.5f ? 0 : a.moveSpeed < 3.5f ? 1 : a.moveSpeed < 4.5f ? 2 : 3;
                if (Mathf.Abs(a.moveSpeed - 2.4f) < 0.05f) { bandSlide[4] += d.magnitude; bandTime[4] += dt; }
                bandSlide[band] += d.magnitude; bandTime[band] += dt;
            }
            string slideInfo = string.Format(ci, "{0} {1:0.000} {2:0.00}", best, by - ground, haveLast && dt > 0f ? new Vector3(pts[best].x - lastPts[best].x, 0f, pts[best].z - lastPts[best].z).magnitude / dt : 0f);
            for (int k = 0; k < 4; k++) lastPts[k] = pts[k];
            lastFoot[0] = ft[0].position; lastFoot[1] = ft[1].position;
            haveLast = true;
            if (trace != null)
                trace.AppendLine(slideInfo + " " + string.Format(ci, "{0:0.000} {1:0.00} {2:0.000} {3:0.00} {4:0.00} {5} {6} {7:0.000} {8:0.000} {9:0.000} {10:0.000} {11:0.000} {12:0.000}",
                    Time.time, a.moveSpeed, a.GaitPhase, a.DevContact(0), a.DevContact(1), a.DevPlanted(0) ? 1 : 0, a.DevPlanted(1) ? 1 : 0,
                    ft[0].position.x, ft[0].position.y, ft[0].position.z, ft[1].position.x, ft[1].position.y, ft[1].position.z));
        }

        static Transform Find(Transform t, string n)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>()) if (ModelLib.Clean(c.name) == n) return c;
            return null;
        }

        // ---------- сцены ----------
        IEnumerator Walk(float v, float dur)
        {
            var a = Actor("Intern", SQ + new Vector3(3f, 0f, 0f), 90f); watch = a;
            yield return null;
            float t = 0f;
            while (t < dur)
            {
                float k = Mathf.Clamp01(t / 0.8f);
                a.moveSpeed = v * k;
                a.transform.position += a.transform.forward * a.moveSpeed * Time.deltaTime;
                Cam(a, 180f, 4.2f, 1.1f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        IEnumerator Speeds()
        {
            var a = Actor("Intern", SQ, 90f); watch = a;
            yield return null;
            float t = 0f, v = 0f;
            float[] tv = { 0f, 1.4f, 1.4f, 2.4f, 2.4f, 3.8f, 3.8f, 6.5f, 6.5f, 0f, 0f };
            float[] tt = { 0.6f, 1.2f, 2.6f, 3.0f, 4.4f, 4.9f, 6.3f, 6.9f, 8.3f, 9.2f, 10.5f };
            while (t < 10.5f)
            {
                int i = 0; while (i < tt.Length - 1 && t > tt[i]) i++;
                float tp = i == 0 ? 0f : tt[i - 1], vp = i == 0 ? 0f : tv[i - 1];
                float want = Mathf.Lerp(vp, tv[i], Mathf.Clamp01((t - tp) / Mathf.Max(0.01f, tt[i] - tp)));
                v = want;
                a.moveSpeed = v;
                a.transform.position += a.transform.forward * v * Time.deltaTime;
                Cam(a, 180f, 5.2f, 1.1f, 0.95f, 40f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        IEnumerator Turn()
        {
            var a = Actor("Intern", SP, 0f); watch = null;
            var camP = a.transform.position + new Vector3(2.4f, 1.3f, 2.6f);
            yield return null;
            float t = 0f;
            while (t < 7f)
            {
                // стоит, поворачивается на 120° за 1.2 с, стоит, обратно на 200° за 1.6 с, стоит
                float yaw = t < 1f ? 0f : t < 2.2f ? Mathf.SmoothStep(0f, 120f, (t - 1f) / 1.2f) : t < 3.6f ? 120f : t < 5.2f ? Mathf.SmoothStep(120f, -80f, (t - 3.6f) / 1.6f) : -80f;
                a.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                a.moveSpeed = 0f;
                CamFixed(camP, a.transform.position + Vector3.up * 0.7f, 42f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        // Смотрит вперёд (как с оружием у камеры), идёт в 8 сторон по очереди, потом бежит в 8 сторон; проскальзывание — по каждому направлению
        IEnumerator Strafe()
        {
            var a = Actor("Intern", SP, 0f); watch = a;
            yield return null;
            float[] dirs = { 0f, 45f, 90f, 135f, 180f, -135f, -90f, -45f };
            var seg = new Dictionary<string, float[]>();
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < 8; i++)
                {
                    float v0 = pass == 0 ? 1.3f : 3.4f, dur = pass == 0 ? 2.2f : 1.5f, t = 0f;
                    var w = Quaternion.Euler(0f, dirs[i], 0f) * Vector3.forward;
                    string key = (pass == 0 ? "шаг " : "бег ") + dirs[i].ToString("0");
                    float s0 = slideSum, t0 = slideTime;
                    while (t < dur)
                    {
                        float v = v0 * Mathf.Clamp01(Mathf.Min(t / 0.3f, (dur - t) / 0.3f));
                        a.moveSpeed = v;
                        a.transform.position += w * v * Time.deltaTime;
                        // обратно к центру, чтобы не уйти далеко: между отрезками — мгновенно (камера следует)
                        var c = a.transform.position + Vector3.up * 1.2f;
                        CamFixed(c + new Vector3(1.2f, 0.5f, -3.6f), c + Vector3.forward * 1.5f, 50f);
                        t += Time.deltaTime;
                        yield return Tick();
                    }
                    seg[key] = new[] { slideSum - s0, slideTime - t0 };
                }
            foreach (var kv in seg) if (kv.Value[1] > 0.1f) log.AppendLine(string.Format(ci, "    {0}: {1:0.00} м/с", kv.Key, kv.Value[0] / kv.Value[1]));
        }

        // Как игрок: камера сзади, бег по кругу и зигзагом, остановка
        IEnumerator ThirdPerson()
        {
            var a = Actor("Intern", FOUNT + new Vector3(0f, 0f, -10f), 90f); watch = a;
            yield return null;
            float t = 0f, yaw = 90f; var cp = a.transform.position + new Vector3(-3.4f, 1.9f, 0.5f);
            while (t < 10f)
            {
                // бег по кругу вокруг фонтана (r = 10 м), потом спринт, в конце — остановка и разворот
                float v = t < 0.6f ? 0f : t < 4.5f ? 3.8f : t < 8f ? 6.5f : 0f;
                a.moveSpeed = Mathf.MoveTowards(a.moveSpeed, v, Time.deltaTime * 8f);
                yaw -= a.moveSpeed / 10f * Mathf.Rad2Deg * Time.deltaTime;
                if (t > 8.6f && t < 9.4f) yaw += 150f * Time.deltaTime;
                a.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                a.transform.position += a.transform.forward * a.moveSpeed * Time.deltaTime;
                var behind = a.transform.position - a.transform.forward * 3.4f + a.transform.right * 0.5f + Vector3.up * 1.9f;
                cp = Vector3.Lerp(cp, behind, 1f - Mathf.Exp(-4f * Time.deltaTime));
                CamFixed(cp, a.transform.position + Vector3.up * 1.3f + a.transform.forward * 1.2f, 55f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        IEnumerator Acts()
        {
            var a = Actor("Intern", SP, 0f); watch = null;
            yield return null;
            float t = 0f;
            var camP = a.transform.position + new Vector3(3.0f, 1.45f, 1.5f);
            // 0 машет, 2.2 удар, 3.2 бросок, 4.4 вздрагивает, 5.4 прицел одной рукой (вверх-вниз), 7.4 двумя, 9.4 нож в руке и идёт, 11.5 конец
            bool w = false, s = false, th = false, h = false;
            while (t < 11.5f)
            {
                if (!w && t > 0.2f) { a.Wave(); w = true; }
                if (!s && t > 2.2f) { a.swingStart = Time.time; s = true; }
                if (!th && t > 3.2f) { a.Throw(); th = true; }
                if (!h && t > 4.4f) { a.Flinch(); h = true; }
                a.aimGun = t > 5.4f && t < 9.2f; a.twoHanded = t > 7.4f;
                a.aimPitch = a.aimGun ? Mathf.Sin((t - 5.4f) * 2f) * 25f : 0f;
                a.holdRight = t > 9.4f;
                a.moveSpeed = t > 9.8f ? 1.2f : 0f;
                a.transform.position += a.transform.forward * a.moveSpeed * Time.deltaTime;
                CamFixed(camP, a.transform.position + Vector3.up * 1.0f, 42f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        IEnumerator SitScene()
        {
            var a = Actor("Intern", SP, 180f); watch = null;
            yield return null;
            float t = 0f;
            var camP = a.transform.position + new Vector3(-2.6f, 1.2f, -2.4f);
            while (t < 9f)
            {
                a.sitTarget = t > 0.8f && t < 6.5f ? 1f : 0f;
                a.typing = t > 2.5f && t < 5.5f;
                a.handsOnDesk = t > 2.2f && t < 5.8f;
                CamFixed(camP, a.transform.position + Vector3.up * 0.8f, 42f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        IEnumerator JumpScene()
        {
            var a = Actor("Intern", SQ + new Vector3(4f, 0f, 1f), 90f); watch = null;
            yield return null;
            float t = 0f, y = 0f, vy = 0f; bool air = false;
            var camP = SQ + new Vector3(8f, 1.3f, -4f);
            while (t < 6f)
            {
                // прыжок на месте в 0.8 с, с разбега — в 3.4 с
                a.moveSpeed = t > 2.2f && t < 5f ? 3.8f : 0f;
                if (!air && (Mathf.Abs(t - 0.8f) < 0.02f || Mathf.Abs(t - 3.4f) < 0.02f)) { air = true; vy = 4.6f; }
                if (air) { vy -= 14f * Time.deltaTime; y += vy * Time.deltaTime; if (y <= 0f) { y = 0f; air = false; } }
                a.grounded = !air;
                var p = a.transform.position + a.transform.forward * a.moveSpeed * Time.deltaTime; p.y = y;
                a.transform.position = p;
                CamFixed(camP, new Vector3(a.transform.position.x, 1.0f, a.transform.position.z), 50f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        // 30 человек гуляют по проспекту: время кадра
        IEnumerator Crowd()
        {
            var list = new List<CharacterAnim>();
            string[] models = { "Dev1", "Dev2", "Dev3", "Dev4", "Gena", "Intern" };
            var rnd = new System.Random(7);
            for (int i = 0; i < 30; i++)
            {
                Vector3 p;
                p = G0 + new Vector3(-14f + (float)rnd.NextDouble() * 28f, 0f, -14f + (float)rnd.NextDouble() * 28f);
                var a = Actor(models[i % models.Length], p, (float)rnd.NextDouble() * 360f);
                a.moveSpeed = i % 3 == 0 ? 0f : i % 3 == 1 ? 1.2f : 3.6f;
                list.Add(a);
            }
            yield return null;
            float t = 0f; var camP = G0 + new Vector3(0f, 3.2f, -20f);
            while (t < 6f)
            {
                foreach (var a in list)
                {
                    a.transform.Rotate(0f, 25f * Time.deltaTime * (a.moveSpeed > 2f ? 1f : -0.6f), 0f);
                    a.transform.position += a.transform.forward * a.moveSpeed * Time.deltaTime;
                }
                CamFixed(camP, G0 + new Vector3(0f, 1f, -4f), 60f);
                t += Time.deltaTime;
                yield return Tick();
            }
        }

        // Офис: коллеги печатают за столами, Гена у доски машет
        IEnumerator Office()
        {
            watch = null;
            // где клавиатуры относительно сидящих коллег (для позы печати)
            var kbs = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.name.Contains("Keyboard") || r.name.Contains("Mouse")).ToList();
            foreach (var a in FindObjectsByType<CharacterAnim>(FindObjectsSortMode.None))
            {
                if (a.Sit < 0.9f) continue;
                foreach (var k in kbs.Where(k => (k.bounds.center - a.transform.position).magnitude < 1.4f))
                {
                    var c = a.transform.InverseTransformPoint(k.bounds.center); var e = k.bounds.extents;
                    log.AppendLine(string.Format(ci, "  {0}: {1} центр ({2:0.00} {3:0.00} {4:0.00}) размер ({5:0.00} {6:0.00} {7:0.00})", a.name, ModelLib.Clean(k.name), c.x, c.y, c.z, e.x * 2, e.y * 2, e.z * 2));
                }
                var hl = Find(a.transform, "HandL"); var hr = Find(a.transform, "HandR");
                if (hl != null && hr != null)
                {
                    var l = a.transform.InverseTransformPoint(hl.position); var r = a.transform.InverseTransformPoint(hr.position);
                    log.AppendLine(string.Format(ci, "  {0}: кисти L ({1:0.00} {2:0.00} {3:0.00}) R ({4:0.00} {5:0.00} {6:0.00}), v4 {7}, печать {8}, typing {9}", a.name, l.x, l.y, l.z, r.x, r.y, r.z, a.v4, a.DevTypeW, a.typing));
                }
            }
            var gena = FindObjectsByType<TeamLeadNpc>(FindObjectsSortMode.None).Select(x => x.GetComponent<CharacterAnim>()).FirstOrDefault(x => x != null);
            var shots = new[] {
                new { p = new Vector3(0.95f, 1.35f, 2.75f), l = new Vector3(0f, 0.95f, 1.95f), d = 4f, wave = false },
                new { p = new Vector3(-3.7f, 1.25f, -1.7f), l = new Vector3(-5f, 0.9f, -2.5f), d = 3f, wave = false },
                new { p = gena != null ? gena.transform.position + gena.transform.forward * 2.3f + gena.transform.right * 1.0f + Vector3.up * 1.5f : Vector3.zero,
                      l = gena != null ? gena.transform.position + Vector3.up * 1.1f : Vector3.zero, d = 4f, wave = true } };
            foreach (var sh in shots)
            {
                if (sh.p == Vector3.zero) continue;
                if (sh.wave && gena != null) gena.Wave();
                float t = 0f;
                while (t < sh.d) { CamFixed(sh.p, sh.l, 45f); t += Time.deltaTime; yield return Tick(); }
            }
            // Гена идёт к стажёру через офис (обход столов, повороты), камера следом сверху-сзади
            var lw = gr.DevLeadWalker; var spawn = gr.DevSpawn;
            if (lw != null && spawn != null && gena != null)
            {
                pl.Teleport(spawn.position, spawn.eulerAngles.y);
                yield return null;
                lw.Visit(LeadMood.Praise, "Проверка походки");
                watch = gena;
                float t = 0f;
                var cp = gena.transform.position - gena.transform.forward * 3f + Vector3.up * 2.4f;
                while (t < 10f)
                {
                    var gp = gena.transform.position;
                    var want = gp - gena.transform.forward * 2.8f + gena.transform.right * 1.2f + Vector3.up * 2.3f;
                    cp = Vector3.Lerp(cp, want, 1f - Mathf.Exp(-2.5f * Time.deltaTime));
                    CamFixed(cp, gp + Vector3.up * 0.8f, 50f);
                    t += Time.deltaTime;
                    yield return Tick();
                }
                log.AppendLine("  Гена: " + lw.State + ", путь от доски " + (gena.transform.position - spawn.position).magnitude.ToString("0.0", ci) + " м до стажёра");
                lw.ResetHome();
                pl.Teleport(new Vector3(40f, 0.1f, 250f), 0f);
            }
        }

        // Обед: горожане гуляют, поэт убегает, декан нападает, журналист снимает, нотариус кидает печать
        IEnumerator LunchScene()
        {
            var city = gr.DevCityRefs;
            var run = new LunchRun(city, 600f, () => pl.Position, () => pl.cam.transform, false, false);
            run.Cleanup();
            var me = CITY + new Vector3(4f, 0.1f, 2f);
            pl.Teleport(me, -90f);
            var list = new List<CityNpc>();
            string[] ids = { "lawyer", "critic", "poet", "dean", "journalist", "notary", "philosopher" };
            for (int i = 0; i < ids.Length; i++)
            {
                var n = run.DevSpawn(ids[i], CITY + new Vector3(-3f + (i % 4) * 2.2f, 0f, 6f + (i / 4) * 2.5f), 180f);
                if (n != null) { n.DevUnfreeze(); list.Add(n); }
            }
            yield return null;
            float t = 0f; bool alerted = false;
            while (t < 12f)
            {
                if (!alerted && t > 2.5f) { alerted = true; foreach (var n in list) if (n.Type != "lawyer" && n.Type != "critic") n.Alert(); }
                CamFixed(me + new Vector3(2.5f, 1.7f, -2.2f), me + new Vector3(-2.2f, 1.0f, 3.2f), 50f);
                t += Time.deltaTime;
                yield return Tick();
            }
            run.Cleanup();
            pl.Teleport(new Vector3(40f, 0.1f, 250f), 0f);
        }

        // Настоящий игрок: его камера от третьего лица, управление «клавишами» (подмена ввода InputX.Dev*):
        // нож в руке — бег, спринт, поворот камеры на бегу, остановка, прыжок; пистолет — смотрит за камерой, ходит вбок, назад,
        // по диагонали, прицел вверх-вниз; снова нож — два удара; от первого лица — пробежка
        IEnumerator PlayerScene()
        {
            var cb = gr.Combat; var city = gr.DevCityRefs;
            if (cb == null || city == null || pl.avatar == null) { log.AppendLine("player: нет боя или города"); yield break; }
            var run = new LunchRun(city, 600f, () => pl.Position, () => pl.cam.transform, false, false);
            run.Cleanup();
            var ars = new Arsenal(new SaveData()); ars.Add("pistol");
            cb.Init(pl, ars); cb.Begin(run);
            bool fp0 = pl.firstPerson;
            if (pl.firstPerson) pl.ToggleView();
            pl.Teleport(G0 + new Vector3(-8f, 0.1f, -18f), 0f); pl.SetCamPitch(10f);
            pl.cinematic = false;
            // вторая камера в углу кадра — сбоку от игрока: видно руки, оружие и ноги
            var side = new GameObject("AT_SideCam").AddComponent<Camera>();
            side.rect = new Rect(0.64f, 0.03f, 0.34f, 0.46f); side.depth = pl.cam.depth + 1; side.fieldOfView = 40f;
            side.clearFlags = CameraClearFlags.SolidColor; side.backgroundColor = pl.cam.backgroundColor; side.nearClipPlane = 0.05f;
            InputX.DevDrive = true; InputX.DevMove = Vector2.zero; InputX.DevLook = Vector2.zero; InputX.DevSprint = false;
            watch = pl.avatar;
            yield return null;
            float t = 0f; bool gun = false, knife = false, s1 = false, s2 = false, fp = false, fpOff = false, jumped = false;
            while (t < 22f)
            {
                Vector2 mv = Vector2.zero, look = Vector2.zero; bool sprint = false;
                if (t > 0.8f && t < 6.4f) mv = new Vector2(0f, 1f);                  // вперёд: бег
                sprint = t > 2.6f && t < 5.2f;                                         // спринт
                if (t > 4.2f && t < 5.6f) look.x = 1.6f;                               // поворот камеры на бегу — дуга
                if (!jumped && t > 7.4f) { InputX.DevJump = true; jumped = true; }     // прыжок с места
                if (!gun && t > 8.6f) { cb.Switch(1); gun = true; }                   // пистолет: корпус за камерой
                if (t > 9.2f && t < 10.6f) mv = new Vector2(1f, 0f);                   // вправо
                if (t > 10.8f && t < 12.2f) mv = new Vector2(0f, -1f);                 // назад
                if (t > 12.4f && t < 13.8f) mv = new Vector2(-1f, 0f);                 // влево
                if (t > 14.0f && t < 15.2f) mv = new Vector2(-0.7f, 0.7f);             // вперёд-влево
                if (t > 15.4f && t < 16.9f) look.y = Mathf.Sin((t - 15.4f) * 4.2f) * 0.9f;   // прицел вверх-вниз
                if (!knife && t > 17.0f) { cb.Switch(0); knife = true; }
                if (!s1 && t > 17.6f) { pl.avatar.swingStart = Time.time; s1 = true; }
                if (!s2 && t > 18.4f) { pl.avatar.swingStart = Time.time; s2 = true; }
                if (!fp && t > 19.2f) { pl.ToggleView(); fp = true; }                  // от первого лица
                if (t > 19.6f && t < 21.2f) mv = new Vector2(0.3f, 1f);
                if (!fpOff && t > 21.4f) { pl.ToggleView(); fpOff = true; }
                InputX.DevMove = mv; InputX.DevLook = look; InputX.DevSprint = sprint;
                pl.avatar.aimPitch = pl.CamPitch;
                var pc = pl.Position + Vector3.up * 1.1f;
                var sp = pc + pl.transform.right * 2.9f + pl.transform.forward * 0.9f + Vector3.up * 0.2f;
                side.transform.position = sp; side.transform.rotation = Quaternion.LookRotation(pc - sp);
                t += Time.deltaTime;
                yield return Tick();
            }
            InputX.DevDrive = false; InputX.DevMove = Vector2.zero; InputX.DevLook = Vector2.zero; InputX.DevSprint = false;
            Destroy(side.gameObject);
            cb.End(); if (gr.Arsenal != null) cb.Init(pl, gr.Arsenal);
            if (pl.firstPerson != fp0) pl.ToggleView();
            pl.cinematic = true;
            pl.Teleport(new Vector3(40f, 0.1f, 250f), 0f);
        }

        void Finish(string msg)
        {
            Time.captureFramerate = 0;
            InputX.DevDrive = false;
            JointHelpers.Stretch = true;
            GameRoot.DevHideUi = false;
            CharacterAnim.UseV4 = true;
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
