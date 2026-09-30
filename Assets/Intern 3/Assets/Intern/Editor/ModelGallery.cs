// Проверка моделей персонажей (спринт 3 версии 0.9): в режиме Play ставит каждого персонажа в «студию» вдали от офиса,
// снимает его отдельной камерой с нескольких ракурсов на каждом уровне сглаживания (0 — угловатые, 1, 2) и ищет места,
// где после сглаживания одна сетка вылезла сквозь другую (тело сквозь одежду, голова сквозь рот и брови).
// Запуск: команда «gallery» в Temp/devcmd.txt. Снимки — Temp/gallery/*.png, отчёт — Temp/gallery/report.txt.
// Задания — Temp/gallery.txt (по строке: «имя|модель|костюм|аксессуар|сидит»), без файла — все модели и костюмы.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Intern.Game;

namespace Intern.EditorTools
{
    public static class ModelGallery
    {
        static string Root { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        static readonly Vector3 Studio = new Vector3(500f, 0f, 500f);

        class Job { public string name, model; public int outfit = -1, acc = -1; public bool sit; }
        class View { public string name; public float yaw, dist, height, fov, look = -1f; public int w, h; }

        static readonly View[] Views =
        {
            new View { name = "front", yaw = 0f, dist = 3.7f, height = 1.02f, fov = 34f, w = 520, h = 760 },
            new View { name = "side", yaw = 90f, dist = 3.7f, height = 1.02f, fov = 34f, w = 520, h = 760 },
            new View { name = "back", yaw = 180f, dist = 3.7f, height = 1.02f, fov = 34f, w = 520, h = 760 },
            new View { name = "q34", yaw = 35f, dist = 1.45f, height = 1.3f, fov = 34f, w = 620, h = 620 },
            new View { name = "face", yaw = 20f, dist = 0.85f, height = 1.68f, fov = 30f, w = 520, h = 520 },
            new View { name = "top", yaw = 180f, dist = 1.3f, height = 2.45f, look = 1.55f, fov = 34f, w = 520, h = 520 },   // как камера от третьего лица
            new View { name = "hands", yaw = 0f, dist = 1.05f, height = 0.9f, look = 0.82f, fov = 42f, w = 620, h = 420 },
        };

        static List<Job> Jobs()
        {
            var list = new List<Job>();
            string f = Path.Combine(Root, "Temp", "gallery.txt");
            if (File.Exists(f))
            {
                foreach (var l in File.ReadAllLines(f))
                {
                    var p = l.Trim().Split('|'); if (p.Length < 2 || l.StartsWith("#")) continue;
                    var j = new Job { name = p[0], model = p[1] };
                    if (p.Length > 2 && p[2].Length > 0) j.outfit = int.Parse(p[2]);
                    if (p.Length > 3 && p[3].Length > 0) j.acc = int.Parse(p[3]);
                    if (p.Length > 4) j.sit = p[4] == "1";
                    list.Add(j);
                }
                return list;
            }
            for (int o = 0; o < Catalog.OutfitIds.Length; o++) list.Add(new Job { name = "intern_" + Catalog.OutfitIds[o], model = "Intern", outfit = o, acc = o % 6 });
            foreach (var m in new[] { "Gena", "Dev1", "Dev2", "Dev3", "Dev4" }) list.Add(new Job { name = m.ToLowerInvariant(), model = m });
            list.Add(new Job { name = "dev1_sit", model = "Dev1", sit = true });
            return list;
        }

        public static void Run()
        {
            string outDir = Path.Combine(Root, "Temp", "gallery");
            string log = Path.Combine(Root, "Temp", "devtools.txt");
            if (!EditorApplication.isPlaying) { File.WriteAllText(log, "gallery: нужен режим Play\n", new UTF8Encoding(false)); return; }
            Directory.CreateDirectory(outDir);
            var jobs = Jobs();
            var report = new StringBuilder("проверка моделей " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            report.AppendLine("вылезло: вершины сетки, оказавшиеся снаружи той, что её закрывала (> 1,5 мм); по уровням сглаживания 0 / 1 / 2 / 3 (1 — только нормали, 2 — форма, 3 — форма дважды)");

            // студия: пол, свет, камера с отдельной текстурой
            var studio = new GameObject("GalleryStudio"); studio.transform.position = Studio;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.SetParent(studio.transform, false); floor.transform.localScale = Vector3.one * 2f;
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
            var fm = floor.GetComponent<Renderer>(); fm.sharedMaterial = new Material(fm.sharedMaterial) { color = new Color(0.55f, 0.56f, 0.6f) };
            var key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(studio.transform, false);
            key.type = LightType.Point; key.range = 12f; key.intensity = 6f; key.color = new Color(1f, 0.97f, 0.92f); key.transform.localPosition = new Vector3(1.6f, 2.6f, 2.4f);
            var camGo = new GameObject("GalleryCam"); camGo.transform.SetParent(studio.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.16f, 0.17f, 0.22f); cam.nearClipPlane = 0.05f; cam.farClipPlane = 50f;
            var cad = cam.GetUniversalAdditionalCameraData(); cad.renderPostProcessing = true; cad.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            int idx = 0, phase = 0; CharacterAnim cur = null; double next = EditorApplication.timeSinceStartup + 0.3;
            var spots = new List<Spot>();
            Action Finish = () => { UnityEngine.Object.Destroy(cur.gameObject); cur = null; spots.Clear(); idx++; phase = 0; next = EditorApplication.timeSinceStartup + 0.1; };
            EditorApplication.CallbackFunction step = null;
            step = () =>
            {
                try
                {
                    if (!EditorApplication.isPlaying) { EditorApplication.update -= step; return; }
                    if (EditorApplication.timeSinceStartup < next) return;
                    if (phase == 0)
                    {
                        if (idx >= jobs.Count)
                        {
                            EditorApplication.update -= step;
                            UnityEngine.Object.Destroy(studio);
                            CharacterAnim.SmoothAll(GameConfig.S.models);
                            report.AppendLine("готово: " + jobs.Count + " персонажей, снимки в Temp/gallery");
                            File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString(), new UTF8Encoding(false));
                            File.WriteAllText(log, report.ToString(), new UTF8Encoding(false));
                            return;
                        }
                        var j = jobs[idx];
                        CharacterAnim.SmoothAll(MeshSmooth.MaxLevel);
                        Appearance ap = null;
                        if (j.outfit >= 0) ap = new Appearance { outfit = j.outfit, accessory = Mathf.Max(0, j.acc), skin = 1, emotion = 1 };
                        cur = CharacterAnim.Spawn(j.model, studio.transform, Studio, 180f, ap);
                        cur.lookAtPlayer = false;
                        if (j.sit) cur.SetSitInstant(1f);
                        cur.SetMaxSmooth(0);
                        phase = 1; next = EditorApplication.timeSinceStartup + 0.8;   // поза покоя устанавливается за несколько кадров
                        return;
                    }
                    // фаза 10–12 — крупные планы найденных мест на уровнях 0–2
                    if (phase >= 10)
                    {
                        int sl = phase - 10;
                        for (int k = 0; k < spots.Count; k++) CaptureSpot(cam, cur, spots[k], Path.Combine(outDir, jobs[idx].name + "_spot" + k + "_L" + sl + ".png"));
                        if (sl < MeshSmooth.MaxLevel) { cur.SetMaxSmooth(sl + 1); phase++; next = EditorApplication.timeSinceStartup + 0.2; return; }
                        Finish(); return;
                    }
                    // фаза 1–3 — уровень 0–2: сетку сменили в прошлом шаге, кости уже пересчитали её к этому кадру
                    var job = jobs[idx];
                    int lv = phase - 1;
                    if (lv == 0) report.AppendLine(job.name + ":");
                    foreach (var v in Views)
                    {
                        if (job.sit && v.name == "back") continue;
                        Capture(cam, cur, v, Path.Combine(outDir, job.name + "_" + v.name + "_L" + lv + ".png"));
                    }
                    string where;
                    var found = new List<Spot>();
                    int cnt = Penetration(cur, out where, lv > 0 ? found : null);
                    report.AppendLine("  L" + lv + ": вылезло " + cnt + (where.Length > 0 ? " — " + where : ""));
                    foreach (var sp in found)
                        if (!spots.Any(o => o.pair == sp.pair && (o.local - sp.local).magnitude < 0.05f)) spots.Add(sp);
                    if (lv < MeshSmooth.MaxLevel) { cur.SetMaxSmooth(lv + 1); phase++; next = EditorApplication.timeSinceStartup + 0.2; return; }
                    spots.Sort((x, y) => y.count.CompareTo(x.count));
                    if (spots.Count > 5) spots.RemoveRange(5, spots.Count - 5);
                    for (int k = 0; k < spots.Count; k++) report.AppendLine("  место " + k + ": " + spots[k].pair + " (" + spots[k].count + ") — снимки " + job.name + "_spot" + k + "_L*.png");
                    if (spots.Count == 0) { Finish(); return; }
                    cur.SetMaxSmooth(0); phase = 10; next = EditorApplication.timeSinceStartup + 0.2;
                }
                catch (Exception e) { EditorApplication.update -= step; if (studio != null) UnityEngine.Object.Destroy(studio); File.WriteAllText(log, "gallery ошибка: " + e + "\n", new UTF8Encoding(false)); }
            };
            EditorApplication.update += step;
            File.WriteAllText(log, "gallery: " + jobs.Count + " персонажей…\n", new UTF8Encoding(false));
        }

        static void CaptureSpot(Camera cam, CharacterAnim a, Spot sp, string file)
        {
            var p = a.transform.TransformPoint(sp.local); var n = a.transform.TransformDirection(sp.normal);
            if (n.sqrMagnitude < 0.5f) n = a.transform.forward;
            cam.transform.position = p + (n * 0.8f + Vector3.up * 0.15f).normalized * 0.32f;
            cam.transform.LookAt(p);
            cam.fieldOfView = 34f;
            Render(cam, 480, 480, file);
        }

        static void Render(Camera cam, int w, int h, string file)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        static void Capture(Camera cam, CharacterAnim a, View v, string file)
        {
            var c = a.transform.position;
            var dir = Quaternion.Euler(0f, a.transform.eulerAngles.y + v.yaw, 0f) * Vector3.forward;   // перед лицом персонажа
            cam.transform.position = c + dir * v.dist + Vector3.up * v.height;
            cam.transform.LookAt(c + Vector3.up * (v.look >= 0f ? v.look : v.height - (v.dist > 2f ? 0.02f : 0.05f)));
            cam.fieldOfView = v.fov;
            var rt = RenderTexture.GetTemporary(v.w, v.h, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(v.w, v.h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, v.w, v.h), 0, 0); tex.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        // ---------- поиск «вылезших» сеток ----------
        class Part { public string name; public Vector3[] p, n; public int[] t; public MeshCollider col; public Bounds box; }

        static List<Part> Bake(CharacterAnim a, Transform tmp)
        {
            var parts = new List<Part>();
            foreach (var r in a.GetComponentsInChildren<Renderer>(false))
            {
                Mesh m = null; Matrix4x4 w;
                var smr = r as SkinnedMeshRenderer;
                if (smr != null) { if (smr.sharedMesh == null) continue; m = new Mesh(); smr.BakeMesh(m, true); w = smr.transform.localToWorldMatrix; }
                else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue; m = mf.sharedMesh; w = r.transform.localToWorldMatrix; }
                var vs = m.vertices; var ns = m.normals; var ts = m.triangles;
                if (vs.Length == 0 || ns.Length != vs.Length) continue;
                var part = new Part { name = ModelLib.Clean(r.name), p = new Vector3[vs.Length], n = new Vector3[vs.Length], t = ts };
                for (int i = 0; i < vs.Length; i++) { part.p[i] = w.MultiplyPoint3x4(vs[i]); part.n[i] = w.MultiplyVector(ns[i]).normalized; }
                part.box = new Bounds(part.p[0], Vector3.zero); foreach (var q in part.p) part.box.Encapsulate(q); part.box.Expand(Reach * 2f);
                // двусторонний коллайдер: луч находит поверхность с любой стороны
                var cm = new Mesh(); cm.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                cm.vertices = part.p;
                var both = new int[ts.Length * 2];
                for (int i = 0; i < ts.Length; i += 3) { both[i] = ts[i]; both[i + 1] = ts[i + 1]; both[i + 2] = ts[i + 2]; both[ts.Length + i] = ts[i]; both[ts.Length + i + 1] = ts[i + 2]; both[ts.Length + i + 2] = ts[i + 1]; }
                cm.triangles = both;
                var go = new GameObject("pen_" + part.name); go.transform.SetParent(tmp, false);
                part.col = go.AddComponent<MeshCollider>(); part.col.sharedMesh = cm;
                parts.Add(part);
                if (smr != null) UnityEngine.Object.Destroy(m);
            }
            return parts;
        }

        const float Reach = 0.03f, Poke = 0.0015f;

        // зазор вдоль нормали: >0 — поверхность B снаружи точки, <0 — внутри (точка вылезла); NaN — B здесь нет
        static float Gap(Part b, Vector3 p, Vector3 n)
        {
            RaycastHit h;
            if (b.col.Raycast(new Ray(p + n * Reach, -n), out h, Reach * 2f)) return Reach - h.distance;
            return float.NaN;
        }

        class Spot { public string pair; public int count; public Vector3 local, normal; }

        // spots — места, где вылезло больше всего (в координатах персонажа, чтобы снять их и на других уровнях)
        static int Penetration(CharacterAnim a, out string where, List<Spot> spots = null)
        {
            var tmp = new GameObject("PenetrationTmp");
            int total = 0; var perPair = new Dictionary<string, int>();
            try
            {
                var parts = Bake(a, tmp.transform);
                foreach (var A in parts)
                    foreach (var B in parts)
                    {
                        if (A == B || !A.box.Intersects(B.box)) continue;
                        // B закрывает A, если заметная часть вершин A лежит под B в пределах 3 см
                        int covered = 0, poked = 0, stepN = Mathf.Max(1, A.p.Length / 400);
                        for (int i = 0; i < A.p.Length; i += stepN)
                        {
                            float g = Gap(B, A.p[i], A.n[i]);
                            if (float.IsNaN(g)) continue;
                            if (g >= -0.0005f) covered++; else if (g < -Poke) poked++;
                        }
                        if (covered < 8 || poked * 4 > covered) continue;   // B не одежда поверх A (или они просто рядом)
                        int cnt = 0; Vector3 sumP = Vector3.zero, sumN = Vector3.zero;
                        for (int i = 0; i < A.p.Length; i++) { float g = Gap(B, A.p[i], A.n[i]); if (!float.IsNaN(g) && g < -Poke) { cnt++; sumP += A.p[i]; sumN += A.n[i]; } }
                        if (cnt > 0) { perPair[A.name + " сквозь " + B.name] = cnt; total += cnt; }
                        if (spots != null && cnt >= 6)
                            spots.Add(new Spot { pair = A.name + " сквозь " + B.name, count = cnt, local = a.transform.InverseTransformPoint(sumP / cnt), normal = a.transform.InverseTransformDirection(sumN.normalized) });
                    }
            }
            finally { UnityEngine.Object.Destroy(tmp); }
            where = string.Join(", ", perPair.OrderByDescending(kv => kv.Value).Take(6).Select(kv => kv.Key + " " + kv.Value).ToArray());
            return total;
        }
    }
}
