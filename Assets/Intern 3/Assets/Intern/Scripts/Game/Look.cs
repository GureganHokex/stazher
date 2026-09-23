// Внешний вид: материалы, скруглённые модели, персонажи с суставами, кастомизация, эффекты.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace Intern.Game
{
    // ================= Внешность персонажа =================
    [Serializable]
    public class Appearance
    {
        public int skin = 1, hair = 0, hairColor = 0, eyes = 0, mouth = 0, top = 0, topColor = 0, pants = 1, shoes = 8, accessory = 0, tie = 2;
        public bool blush = true;
        public Appearance Clone() { return (Appearance)MemberwiseClone(); }
    }

    public static class Catalog
    {
        public static readonly Color[] Skins = Hexes("F2D2B0", "D8B888", "CFA77A", "C49A6C", "A87A52", "7A5236");
        public static readonly Color[] HairColors = Hexes("3A2A20", "6B4226", "A0522D", "F2C14E", "2B2D42", "E86A92", "6C8CFF", "F4F1EA");
        public static readonly Color[] Cloth = Hexes("92A7D2", "383A68", "C8453A", "E8782F", "7FB58A", "D9A441", "7A6FC4", "F2EFE8", "2E2638", "8C6A4F", "E0567F", "4A4A5E");

        public static readonly string[] HairNames = { "Короткая", "Хвостик", "Кудри", "Ёжик", "Каре", "Без волос" };
        public static readonly string[] EyeNames = { "Круглые", "Счастливые", "Сонные", "Блестящие" };
        public static readonly string[] MouthNames = { "Улыбка", "Широкая", "Спокойный", "Котик :3" };
        public static readonly string[] TopNames = { "Футболка", "Худи", "Рубашка и галстук", "Свитер в полоску" };
        public static readonly string[] AccNames = { "Ничего", "Очки", "Наушники", "Кепка", "Шапка", "Корона" };
        static readonly int[] topPrice = { 0, 100, 150, 120 };
        static readonly int[] accPrice = { 0, 0, 120, 80, 60, 0 };

        public static int TopPrice(int i) { return topPrice[Mathf.Clamp(i, 0, topPrice.Length - 1)]; }
        public static int AccPrice(int i) { return accPrice[Mathf.Clamp(i, 0, accPrice.Length - 1)]; }
        public static bool AccNeedsJuniorPlus(int i) { return i == 5; }

        static Color[] Hexes(params string[] h) { var c = new Color[h.Length]; for (int i = 0; i < h.Length; i++) c[i] = Pal.Hex(h[i]); return c; }
        public static Color Pick(Color[] arr, int i) { return arr[((i % arr.Length) + arr.Length) % arr.Length]; }
    }

    public static class Look
    {
        static Shader toon, fx;
        static bool checkedShaders;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();

        static void CheckShaders()
        {
            if (checkedShaders) return;
            checkedShaders = true;
            if (GraphicsSettings.currentRenderPipeline == null) return; // шейдеры написаны под URP
            toon = Resources.Load<Shader>("Shaders/InternToon");
            if (toon != null && !toon.isSupported) toon = null;
            fx = Resources.Load<Shader>("Shaders/InternFx");
            if (fx != null && !fx.isSupported) fx = null;
        }

        public static bool ToonAvailable { get { CheckShaders(); return toon != null; } }
        public static bool FxAvailable { get { CheckShaders(); return fx != null; } }

        public static Material Mat(Color c, float outline = 1f, float emission = 0f)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + "|" + outline + "|" + emission;
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            if (ToonAvailable)
            {
                m = new Material(toon);
                m.SetColor("_BaseColor", c);
                m.SetFloat("_OutlineWidth", 0.0035f * outline);
                m.SetColor("_OutlineColor", Color.Lerp(c, Pal.Ink, 0.78f));
                m.SetColor("_ShadeColor", Color.Lerp(new Color(0.58f, 0.56f, 0.9f), Color.white, 0.12f));
                if (emission > 0) m.SetColor("_EmissionColor", c * emission * 0.6f);
            }
            else
            {
                // В URP шейдер Standard рисуется розовым, поэтому берём стандартный шейдер конвейера
                var sh = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
                m = new Material(sh);
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.1f);
                if (emission > 0 && m.HasProperty("_EmissionColor"))
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", c * emission);
                }
            }
            m.name = "Intern_" + key;
            mats[key] = m;
            return m;
        }

        // Материал эффектов: additive — свечение, иначе обычная прозрачность
        public static Material FxMat(Color c, bool radial, bool additive)
        {
            if (!FxAvailable) return null;
            string key = "fx" + ColorUtility.ToHtmlStringRGBA(c) + radial + additive;
            Material m;
            if (mats.TryGetValue(key, out m) && m != null) return m;
            m = new Material(fx);
            m.SetColor("_Color", c);
            m.SetFloat("_Radial", radial ? 1 : 0);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.renderQueue = 3000;
            mats[key] = m;
            return m;
        }

        public static void Apply(GameObject go, Color c, float outline = 1f, float emission = 0f, bool shadows = true)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            r.sharedMaterial = Mat(c, outline, emission);
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }

        // ---------- Скруглённый параллелепипед ----------
        public static Mesh RoundedBox(Vector3 size, float radius, int seg = 4)
        {
            float minHalf = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.5f;
            radius = Mathf.Clamp(radius, 0f, minHalf * 0.999f);
            string key = size.x.ToString("F3") + "_" + size.y.ToString("F3") + "_" + size.z.ToString("F3") + "_" + radius.ToString("F3") + "_" + seg;
            Mesh cached;
            if (meshes.TryGetValue(key, out cached) && cached != null) return cached;

            Vector3 h = size * 0.5f;
            Vector3 inner = new Vector3(h.x - radius, h.y - radius, h.z - radius);
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();

            // Грани: нормаль d и касательные u, v, где Cross(u, v) = d
            Vector3[] D = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] U = { Vector3.up, Vector3.forward, Vector3.forward, Vector3.right, Vector3.right, Vector3.up };
            Vector3[] V = { Vector3.forward, Vector3.up, Vector3.right, Vector3.forward, Vector3.up, Vector3.right };

            for (int f = 0; f < 6; f++)
            {
                float hu = Vector3.Dot(Abs(U[f]), h), hv = Vector3.Dot(Abs(V[f]), h), hd = Vector3.Dot(Abs(D[f]), h);
                var au = Steps(hu, radius, seg);
                var av = Steps(hv, radius, seg);
                int start = verts.Count;
                for (int j = 0; j < av.Count; j++)
                    for (int i = 0; i < au.Count; i++)
                    {
                        Vector3 p = D[f] * hd + U[f] * au[i] + V[f] * av[j];
                        Vector3 c = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                        Vector3 n = p - c;
                        n = n.sqrMagnitude < 1e-10f ? D[f] : n.normalized;
                        verts.Add(radius > 0 ? c + n * radius : p);
                        norms.Add(radius > 0 ? n : D[f]);
                    }
                int w = au.Count;
                for (int j = 0; j < av.Count - 1; j++)
                    for (int i = 0; i < w - 1; i++)
                    {
                        int a = start + j * w + i, b = a + 1, cI = a + w, d = cI + 1;
                        tris.Add(a); tris.Add(b); tris.Add(cI);
                        tris.Add(b); tris.Add(d); tris.Add(cI);
                    }
            }

            var mesh = new Mesh { name = "RoundedBox_" + key };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            meshes[key] = mesh;
            return mesh;
        }

        static Vector3 Abs(Vector3 v) { return new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z)); }

        static List<float> Steps(float half, float r, int seg)
        {
            var res = new List<float>();
            if (r <= 0) { res.Add(-half); res.Add(half); return res; }
            for (int k = 0; k <= seg; k++) res.Add(-half + r * k / seg);
            for (int k = 0; k <= seg; k++)
            {
                float x = half - r + r * k / seg;
                if (x > res[res.Count - 1] + 1e-5f) res.Add(x);
            }
            return res;
        }

        // Четырёхугольник по 4 точкам (с UV и белым цветом вершин) — для лучей и теней
        public static Mesh Quad(Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr)
        {
            var m = new Mesh { name = "FxQuad" };
            m.vertices = new[] { bl, br, tl, tr };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static GameObject FxObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            if (mat == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // Мягкая тень-пятно под персонажем
        public static void Blob(Transform parent, float radius, float strength = 0.32f)
        {
            var mesh = Quad(new Vector3(-radius, 0, -radius), new Vector3(radius, 0, -radius), new Vector3(-radius, 0, radius), new Vector3(radius, 0, radius));
            var go = FxObject("BlobShadow", parent, mesh, FxMat(new Color(0.12f, 0.08f, 0.25f, strength), true, false));
            if (go != null) go.transform.localPosition = new Vector3(0, 0.015f, 0);
        }

        // Пылинки, парящие в солнечном свете
        public static void Dust(Vector3 center, Vector3 size)
        {
            var mat = FxMat(new Color(1f, 0.95f, 0.8f, 0.9f), true, true);
            if (mat == null) return;
            var go = new GameObject("Dust");
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true;
            main.startLifetime = 10f; main.startSpeed = 0.03f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(1f, 0.96f, 0.85f, 0.8f);
            main.maxParticles = 350;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 30f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = size;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.06f; noise.frequency = 0.25f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
        }

        public static GameObject RBox(string name, Transform parent, Vector3 pos, Vector3 size, Color c,
                                      float radius = 0.06f, bool collider = true, float outline = 1f, float emission = 0f, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = RoundedBox(size, radius);
            go.AddComponent<MeshRenderer>();
            Apply(go, c, outline, emission, shadows);
            if (collider) go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        public static GameObject Prim(string name, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c,
                                      bool collider = false, float outline = 1f, float emission = 0f, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Apply(go, c, outline, emission, shadows);
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        public static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        public static CharacterAnim Bean(string name, Transform parent, Vector3 pos, float yaw, Appearance ap, float scale = 1f)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0, yaw, 0);
            root.localScale = Vector3.one * scale;
            var anim = root.gameObject.AddComponent<CharacterAnim>();
            anim.Build(ap);
            return anim;
        }
    }

    // ================= Персонаж: риг и анимации =================
    public class CharacterAnim : MonoBehaviour
    {
        public Transform rig, hips, torso, head, legL, legR, kneeL, kneeR, armL, armR, neck, elbowL, elbowR;
        public bool imported;
        public string model;
        Vector3 hipsBase, lastPos, lastVel;
        Vector2 lean, leanVel, headLag, headLagVel;
        float armFlopL, armFlopR, armFlopVelL, armFlopVelR;
        int[] eyeAxis = new int[0];
        readonly Dictionary<string, Material> tinted = new Dictionary<string, Material>();
        public Transform[] eyes = new Transform[0];
        public Renderer[] headRenderers = new Renderer[0];
        public Appearance appearance;

        // управление
        public float moveSpeed;        // м/с — задаёт контроллер
        public bool grounded = true;
        public float sitTarget;        // 0 — стоит, 1 — сидит
        public bool typing;            // печатает постоянно (коллеги)
        public float typingUntil;      // печатает, пока время меньше этого (игрок)
        public bool handsOnDesk;       // руки на клавиатуре, даже если не печатает
        public bool lookAtPlayer;
        public float waveUntil;

        float sit, walkPhase, walkAmt, phase, nextBlink, blinkT = -1, land;
        Vector3[] eyeBase = new Vector3[0];
        bool wasGrounded = true;

        public float Sit { get { return sit; } }

        void Awake() { phase = Random.Range(0f, 10f); nextBlink = Time.time + Random.Range(1f, 4f); }

        public void Wave() { waveUntil = Time.time + 1.8f; }

        public void SetSitInstant(float v) { sit = sitTarget = v; }

        public void SetHeadVisible(bool v)
        {
            foreach (var r in headRenderers) if (r != null) r.enabled = v;
        }

        // ---------- Постройка модели ----------
        public void Build(Appearance ap)
        {
            appearance = ap.Clone();
            if (rig != null) { rig.gameObject.SetActive(false); Destroy(rig.gameObject); }
            rig = Look.Node("Rig", transform, Vector3.zero);

            Color skin = Catalog.Pick(Catalog.Skins, ap.skin);
            Color top = Catalog.Pick(Catalog.Cloth, ap.topColor);
            Color pants = Catalog.Pick(Catalog.Cloth, ap.pants);
            Color shoes = Catalog.Pick(Catalog.Cloth, ap.shoes);
            Color hairC = Catalog.Pick(Catalog.HairColors, ap.hairColor);
            Color topDark = Color.Lerp(top, Pal.Ink, 0.25f), topLight = Color.Lerp(top, Color.white, 0.45f);

            hips = Look.Node("Hips", rig, new Vector3(0, 0.48f, 0));
            legL = Leg(hips, -1, pants, shoes, out kneeL);
            legR = Leg(hips, 1, pants, shoes, out kneeR);

            torso = Look.Node("Torso", hips, Vector3.zero);
            Look.RBox("Body", torso, new Vector3(0, 0.3f, 0), new Vector3(0.62f, 0.62f, 0.44f), top, 0.21f, false);
            switch (ap.top)
            {
                case 1: // худи
                    Look.RBox("Hood", torso, new Vector3(0, 0.6f, -0.2f), new Vector3(0.5f, 0.22f, 0.2f), topDark, 0.09f, false);
                    Look.RBox("Pocket", torso, new Vector3(0, 0.17f, 0.21f), new Vector3(0.36f, 0.14f, 0.04f), topDark, 0.02f, false, 0.6f);
                    foreach (var sx in new[] { -1f, 1f })
                        Look.RBox("String", torso, new Vector3(sx * 0.07f, 0.47f, 0.22f), new Vector3(0.025f, 0.14f, 0.025f), Pal.Hex("FFF9F0"), 0.01f, false, 0);
                    break;
                case 2: // рубашка и галстук
                    Look.RBox("Collar", torso, new Vector3(0, 0.58f, 0.04f), new Vector3(0.36f, 0.08f, 0.3f), Pal.Hex("FFF9F0"), 0.03f, false);
                    Look.RBox("TieKnot", torso, new Vector3(0, 0.53f, 0.225f), new Vector3(0.07f, 0.06f, 0.03f), Pal.Hex("E03C84"), 0.015f, false, 0.6f);
                    Look.RBox("Tie", torso, new Vector3(0, 0.36f, 0.225f), new Vector3(0.09f, 0.28f, 0.02f), Pal.Hex("FF4F9A"), 0.02f, false, 0.6f);
                    break;
                case 3: // свитер в полоску
                    Look.RBox("Collar", torso, new Vector3(0, 0.58f, 0.02f), new Vector3(0.32f, 0.08f, 0.28f), topLight, 0.03f, false);
                    foreach (var y in new[] { 0.2f, 0.36f })
                        foreach (var z in new[] { 0.215f, -0.215f })
                            Look.RBox("Stripe", torso, new Vector3(0, y, z), new Vector3(0.48f, 0.06f, 0.02f), topLight, 0.01f, false, 0);
                    break;
                default: // футболка
                    Look.RBox("Collar", torso, new Vector3(0, 0.58f, 0.02f), new Vector3(0.3f, 0.08f, 0.26f), topLight, 0.03f, false);
                    break;
            }
            armL = Arm(torso, -1, top, skin);
            armR = Arm(torso, 1, top, skin);

            head = Look.Node("Head", torso, new Vector3(0, 0.98f, 0));
            Look.Prim("Face", head, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.66f, 0.62f, 0.62f), skin);
            BuildEyes(ap.eyes);
            BuildMouth(ap.mouth);
            if (ap.blush)
                foreach (var sx in new[] { -1f, 1f })
                    Look.Prim("Blush", head, PrimitiveType.Sphere, new Vector3(sx * 0.2f, -0.08f, 0.25f), new Vector3(0.1f, 0.05f, 0.04f), Pal.Hex("FF8FB1"), false, 0f, 0f, false);
            BuildHair(ap.hair, hairC);
            BuildAccessory(ap.accessory, top);

            headRenderers = head.GetComponentsInChildren<Renderer>();
            eyeBase = new Vector3[eyes.Length];
            for (int i = 0; i < eyes.Length; i++) eyeBase[i] = eyes[i].localScale;
            if (transform.Find("BlobShadow") == null) Look.Blob(transform, 0.48f);
        }

        Transform Leg(Transform hipsT, int side, Color pants, Color shoes, out Transform knee)
        {
            var hip = Look.Node(side < 0 ? "HipL" : "HipR", hipsT, new Vector3(side * 0.13f, 0, 0));
            Look.RBox("Thigh", hip, new Vector3(0, -0.11f, 0), new Vector3(0.19f, 0.26f, 0.22f), pants, 0.08f, false);
            knee = Look.Node("Knee", hip, new Vector3(0, -0.22f, 0));
            Look.RBox("Shin", knee, new Vector3(0, -0.1f, 0), new Vector3(0.18f, 0.22f, 0.2f), pants, 0.08f, false);
            Look.RBox("Shoe", knee, new Vector3(0, -0.21f, 0.05f), new Vector3(0.21f, 0.1f, 0.3f), shoes, 0.045f, false);
            return hip;
        }

        Transform Arm(Transform torsoT, int side, Color sleeve, Color skin)
        {
            var sh = Look.Node(side < 0 ? "ShoulderL" : "ShoulderR", torsoT, new Vector3(side * 0.34f, 0.46f, 0));
            Look.RBox("Arm", sh, new Vector3(0, -0.16f, 0), new Vector3(0.15f, 0.34f, 0.15f), sleeve, 0.07f, false);
            Look.Prim("Hand", sh, PrimitiveType.Sphere, new Vector3(0, -0.36f, 0), Vector3.one * 0.15f, skin);
            return sh;
        }

        void BuildEyes(int style)
        {
            var list = new List<Transform>();
            foreach (var sx in new[] { -1f, 1f })
            {
                Transform eye;
                switch (style)
                {
                    case 1: // счастливые ^^
                        eye = Look.Node("Eye", head, new Vector3(sx * 0.12f, 0.04f, 0.295f));
                        var a = Look.RBox("Stroke", eye, new Vector3(-0.022f, 0, 0), new Vector3(0.06f, 0.022f, 0.02f), Pal.Ink, 0.01f, false, 0);
                        a.transform.localRotation = Quaternion.Euler(0, 0, 35);
                        var b = Look.RBox("Stroke", eye, new Vector3(0.022f, 0, 0), new Vector3(0.06f, 0.022f, 0.02f), Pal.Ink, 0.01f, false, 0);
                        b.transform.localRotation = Quaternion.Euler(0, 0, -35);
                        break;
                    case 2: // сонные
                        eye = Look.Prim("Eye", head, PrimitiveType.Sphere, new Vector3(sx * 0.12f, 0.01f, 0.29f), new Vector3(0.11f, 0.05f, 0.05f), Pal.Ink, false, 0).transform;
                        Look.RBox("Lid", head, new Vector3(sx * 0.12f, 0.045f, 0.29f), new Vector3(0.12f, 0.02f, 0.02f), Color.Lerp(Pal.Ink, Color.white, 0.2f), 0.01f, false, 0);
                        break;
                    case 3: // блестящие
                        eye = Look.Prim("Eye", head, PrimitiveType.Sphere, new Vector3(sx * 0.12f, 0.03f, 0.28f), new Vector3(0.13f, 0.17f, 0.07f), Pal.Hex("2B3A8C"), false, 0).transform;
                        Look.Prim("Shine", eye, PrimitiveType.Sphere, new Vector3(0.22f, 0.25f, 0.45f), new Vector3(0.42f, 0.36f, 0.4f), Color.white, false, 0f, 0.4f, false);
                        Look.Prim("Shine2", eye, PrimitiveType.Sphere, new Vector3(-0.2f, -0.25f, 0.45f), new Vector3(0.2f, 0.18f, 0.3f), Color.white, false, 0f, 0.4f, false);
                        break;
                    default: // круглые
                        eye = Look.Prim("Eye", head, PrimitiveType.Sphere, new Vector3(sx * 0.12f, 0.03f, 0.285f), new Vector3(0.1f, 0.14f, 0.06f), Pal.Ink, false, 0).transform;
                        Look.Prim("Shine", eye, PrimitiveType.Sphere, new Vector3(0.22f, 0.25f, 0.45f), new Vector3(0.4f, 0.35f, 0.4f), Color.white, false, 0f, 0.4f, false);
                        break;
                }
                list.Add(eye);
            }
            eyes = list.ToArray();
        }

        void BuildMouth(int style)
        {
            Color lips = Pal.Hex("7A2E3B");
            switch (style)
            {
                case 1:
                    Look.Prim("Mouth", head, PrimitiveType.Sphere, new Vector3(0, -0.12f, 0.285f), new Vector3(0.13f, 0.07f, 0.04f), lips, false, 0, 0, false);
                    Look.RBox("Teeth", head, new Vector3(0, -0.097f, 0.302f), new Vector3(0.08f, 0.018f, 0.01f), Color.white, 0.005f, false, 0, 0, false);
                    break;
                case 2:
                    Look.RBox("Mouth", head, new Vector3(0, -0.11f, 0.3f), new Vector3(0.09f, 0.016f, 0.01f), lips, 0.006f, false, 0, 0, false);
                    break;
                case 3:
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        var m = Look.Prim("Mouth", head, PrimitiveType.Sphere, new Vector3(sx * 0.026f, -0.11f, 0.29f), new Vector3(0.055f, 0.03f, 0.03f), lips, false, 0, 0, false);
                        m.transform.localRotation = Quaternion.Euler(0, 0, sx * 25);
                    }
                    break;
                default:
                    Look.Prim("Mouth", head, PrimitiveType.Sphere, new Vector3(0, -0.11f, 0.29f), new Vector3(0.1f, 0.035f, 0.03f), lips, false, 0, 0, false);
                    break;
            }
        }

        void BuildHair(int style, Color hair)
        {
            switch (style)
            {
                case 0:
                    Look.Prim("Hair", head, PrimitiveType.Sphere, new Vector3(0, 0.12f, -0.03f), new Vector3(0.7f, 0.46f, 0.66f), hair);
                    Look.Prim("Fringe", head, PrimitiveType.Sphere, new Vector3(-0.1f, 0.2f, 0.2f), new Vector3(0.36f, 0.16f, 0.2f), hair);
                    break;
                case 1:
                    Look.Prim("Hair", head, PrimitiveType.Sphere, new Vector3(0, 0.12f, -0.04f), new Vector3(0.7f, 0.48f, 0.66f), hair);
                    Look.Prim("Tail", head, PrimitiveType.Sphere, new Vector3(0, 0.05f, -0.36f), new Vector3(0.22f, 0.3f, 0.22f), hair);
                    Look.Prim("Band", head, PrimitiveType.Sphere, new Vector3(0, 0.16f, -0.31f), new Vector3(0.12f, 0.08f, 0.08f), Pal.Pink);
                    break;
                case 2:
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i * Mathf.PI * 2 / 7;
                        Look.Prim("Curl", head, PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.21f, 0.24f, Mathf.Sin(a) * 0.2f - 0.04f), Vector3.one * 0.26f, hair);
                    }
                    Look.Prim("CurlTop", head, PrimitiveType.Sphere, new Vector3(0, 0.33f, -0.03f), Vector3.one * 0.28f, hair);
                    break;
                case 3:
                    Look.Prim("Hair", head, PrimitiveType.Sphere, new Vector3(0, 0.13f, -0.03f), new Vector3(0.68f, 0.4f, 0.64f), hair);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = -60 + i * 30;
                        var sp = Look.Prim("Spike", head, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.11f, 0.3f, 0.11f), hair);
                        sp.transform.localRotation = Quaternion.Euler(-20, 0, a);
                        sp.transform.localPosition = sp.transform.localRotation * new Vector3(0, 0.34f, 0) + new Vector3(0, 0, -0.04f);
                    }
                    break;
                case 4:
                    Look.Prim("Bob", head, PrimitiveType.Sphere, new Vector3(0, 0.02f, -0.12f), new Vector3(0.78f, 0.7f, 0.66f), hair);
                    Look.Prim("Fringe", head, PrimitiveType.Sphere, new Vector3(0, 0.2f, 0.12f), new Vector3(0.6f, 0.2f, 0.34f), hair);
                    break;
                default:
                    Look.Prim("Shine", head, PrimitiveType.Sphere, new Vector3(0.08f, 0.26f, 0.05f), new Vector3(0.12f, 0.05f, 0.1f), Color.Lerp(Color.white, hair, 0.1f), false, 0, 0, false);
                    break;
            }
        }

        void BuildAccessory(int acc, Color top)
        {
            Color dark = Pal.Hex("2D3052");
            switch (acc)
            {
                case 1: // очки
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        var fr = Look.Prim("Frame", head, PrimitiveType.Cylinder, new Vector3(sx * 0.12f, 0.03f, 0.305f), new Vector3(0.17f, 0.01f, 0.17f), dark, false, 0);
                        fr.transform.localRotation = Quaternion.Euler(90, 0, 0);
                        var lens = Look.Prim("Lens", head, PrimitiveType.Cylinder, new Vector3(sx * 0.12f, 0.03f, 0.316f), new Vector3(0.135f, 0.004f, 0.135f), Pal.Hex("CFEFFF"), false, 0, 0.1f, false);
                        lens.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    }
                    Look.RBox("Bridge", head, new Vector3(0, 0.05f, 0.315f), new Vector3(0.08f, 0.02f, 0.02f), dark, 0.008f, false, 0);
                    break;
                case 2: // наушники
                    Look.RBox("Band", head, new Vector3(0, 0.34f, 0), new Vector3(0.52f, 0.05f, 0.1f), dark, 0.024f, false);
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        Look.RBox("Side", head, new Vector3(sx * 0.31f, 0.18f, 0), new Vector3(0.05f, 0.3f, 0.1f), dark, 0.024f, false);
                        var cup = Look.Prim("Cup", head, PrimitiveType.Cylinder, new Vector3(sx * 0.34f, 0.0f, 0), new Vector3(0.2f, 0.05f, 0.2f), Pal.Pink);
                        cup.transform.localRotation = Quaternion.Euler(0, 0, 90);
                    }
                    break;
                case 3: // кепка
                    Color capC = Catalog.Pick(Catalog.Cloth, appearance.topColor + 5);
                    Look.Prim("CapDome", head, PrimitiveType.Sphere, new Vector3(0, 0.17f, -0.02f), new Vector3(0.7f, 0.42f, 0.68f), capC);
                    Look.RBox("Brim", head, new Vector3(0, 0.21f, 0.3f), new Vector3(0.46f, 0.03f, 0.28f), capC, 0.012f, false);
                    break;
                case 4: // шапка
                    Color bc = Catalog.Pick(Catalog.Cloth, appearance.topColor + 3);
                    Look.Prim("Beanie", head, PrimitiveType.Sphere, new Vector3(0, 0.17f, -0.02f), new Vector3(0.71f, 0.46f, 0.69f), bc);
                    Look.RBox("Cuff", head, new Vector3(0, 0.1f, -0.02f), new Vector3(0.7f, 0.1f, 0.66f), Color.Lerp(bc, Pal.Ink, 0.2f), 0.049f, false);
                    Look.Prim("Pompom", head, PrimitiveType.Sphere, new Vector3(0, 0.42f, -0.02f), Vector3.one * 0.15f, Color.white);
                    break;
                case 5: // корона
                    Color gold = Pal.Hex("FFD23F");
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI * 2 / 8;
                        var p = new Vector3(Mathf.Cos(a) * 0.2f, 0.35f, Mathf.Sin(a) * 0.2f - 0.02f);
                        var seg = Look.RBox("CrownSeg", head, p, new Vector3(0.09f, 0.1f, 0.03f), gold, 0.012f, false, 1, 0.25f);
                        seg.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0);
                        if (i % 2 == 0) Look.Prim("Tip", head, PrimitiveType.Sphere, p + Vector3.up * 0.08f, Vector3.one * 0.05f, gold, false, 1, 0.25f);
                    }
                    Look.Prim("Gem", head, PrimitiveType.Sphere, new Vector3(0, 0.36f, 0.19f), Vector3.one * 0.06f, Pal.Pink, false, 0.5f, 0.6f);
                    break;
            }
        }

        // ---------- Модель из Blender ----------
        public static CharacterAnim Spawn(string model, Transform parent, Vector3 pos, float yaw, Appearance ap)
        {
            var root = new GameObject(model);
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var a = root.AddComponent<CharacterAnim>();
            a.BuildImported(model, ap);
            return a;
        }

        Transform F(string n) { return ModelLib.Find(rig, n); }

        public void BuildImported(string modelName, Appearance ap)
        {
            model = modelName;
            if (rig != null) { rig.gameObject.SetActive(false); Destroy(rig.gameObject); }
            var inst = Instantiate(ModelLib.Prefab("Characters/" + modelName), transform);
            inst.transform.localPosition = Vector3.zero; // поворот префаба не трогаем: в нём может быть поправка осей из FBX
            imported = true;
            ModelLib.ConvertAll(inst);
            // Модель может приехать «лёжа» (оси Blender, Z вверх). Ставим на ноги: голова — вверх, нос — вперёд.
            // если в импорте выключено Preserve Hierarchy, Unity «съедает» узел Root — тогда опора сам префаб
            var rootT = ModelLib.Find(inst.transform, "Root") ?? inst.transform;
            var headT = ModelLib.Find(inst.transform, "Head");
            var noseT = ModelLib.Find(inst.transform, "NoseTip") ?? ModelLib.Find(inst.transform, "Nose");
            if (rootT != null && headT != null && noseT != null)
            {
                Vector3 up = headT.position - rootT.position;
                Vector3 fwd = Vector3.ProjectOnPlane(noseT.position - headT.position, up.normalized);
                if (up.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.00001f)
                {
                    var fix = transform.rotation * Quaternion.Inverse(Quaternion.LookRotation(fwd.normalized, up.normalized));
                    inst.transform.rotation = fix * inst.transform.rotation;
                    inst.transform.position += transform.position - rootT.position;
                }
                Debug.Log("[Стажёр] " + modelName + ": вверх " + (headT.position - rootT.position).normalized + ", вперёд " +
                          Vector3.ProjectOnPlane(noseT.position - headT.position, transform.up).normalized);
            }
            // Кости в FBX могут быть в осях Blender — пересобираем скелет в осях персонажа
            var srcRoot = rootT;
            rig = NormalizeRig(srcRoot, transform);
            rig.name = "Rig";
            inst.SetActive(false); Destroy(inst);
            hips = F("Hips"); torso = F("Torso"); neck = F("Neck"); head = F("Head");
            legL = F("HipL"); legR = F("HipR"); kneeL = F("KneeL"); kneeR = F("KneeR");
            armL = F("ShoulderL"); armR = F("ShoulderR"); elbowL = F("ElbowL"); elbowR = F("ElbowR");
            var el = F("EyeL"); var er = F("EyeR");
            eyes = el != null && er != null ? new[] { el, er } : new Transform[0];
            eyeBase = new Vector3[eyes.Length];
            eyeAxis = new int[eyes.Length];
            for (int i = 0; i < eyes.Length; i++)
            {
                eyeBase[i] = eyes[i].localScale;
                // какая локальная ось глаза смотрит вверх — по ней и «моргаем»
                var ax = new[] { eyes[i].right, eyes[i].up, eyes[i].forward };
                float best = -1;
                for (int k = 0; k < 3; k++) { float d = Mathf.Abs(Vector3.Dot(ax[k], transform.up)); if (d > best) { best = d; eyeAxis[i] = k; } }
            }
            if (hips == null || torso == null || head == null || legL == null || armL == null)
            {
                Debug.LogError("Модель " + modelName + ": не найдены кости (Hips/Torso/Head/HipL/ShoulderL). Проверь экспорт из Blender.");
                imported = false; hips = null; return;
            }
            hipsBase = hips.localPosition;
            headRenderers = head.GetComponentsInChildren<Renderer>(true);
            lastPos = transform.position;
            if (ap != null) ApplyLook(ap); else ShowAccessory(-1);
            if (transform.Find("BlobShadow") == null) Look.Blob(transform, 0.42f);
        }

        static readonly string[] AccNodes = { null, "Acc_Glasses", "Acc_Headphones", "Acc_Cap", "Acc_Beanie", "Acc_Crown" };

        // -1 — оставить аксессуары модели как есть (у коллег свои)
        void ShowAccessory(int index)
        {
            if (index < 0) return;
            for (int i = 1; i < AccNodes.Length; i++)
            {
                var t = F(AccNodes[i]);
                if (t != null) t.gameObject.SetActive(i == index);
            }
        }

        // Перекраска одежды по именам материалов из Blender (lp_skin_, lp_shirt_, ...)
        public void ApplyLook(Appearance ap)
        {
            appearance = ap.Clone();
            if (!imported) { Build(ap); return; }
            Color skin = Catalog.Pick(Catalog.Skins, ap.skin);
            var colors = new Dictionary<string, Color> {
                { "lp_skin_", skin }, { "lp_sock_", Color.Lerp(skin, Color.black, 0.18f) },
                { "lp_shirt_", Catalog.Pick(Catalog.Cloth, ap.topColor) }, { "lp_pants_", Catalog.Pick(Catalog.Cloth, ap.pants) },
                { "lp_boots_", Catalog.Pick(Catalog.Cloth, ap.shoes) }, { "lp_tie_", Catalog.Pick(Catalog.Cloth, ap.tie) },
            };
            foreach (var r in rig.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    foreach (var kv in colors)
                    {
                        if (!mats[i].name.StartsWith(kv.Key)) continue;
                        Material m;
                        if (!tinted.TryGetValue(kv.Key, out m) || m == null) { m = new Material(mats[i]); tinted[kv.Key] = m; }
                        ModelLib.SetColor(m, kv.Value);
                        mats[i] = m; changed = true; break;
                    }
                }
                if (changed) r.sharedMaterials = mats;
            }
            ShowAccessory(ap.accessory);
        }

        static float Spring(ref float x, ref float v, float target, float k, float damp, float dt)
        {
            v += (target - x) * k * dt; v *= Mathf.Exp(-damp * dt); x += v * dt; return x;
        }

        // Пружинная «болтанка» в духе физических игр: наклоны от ускорения, запаздывание головы и рук
        void UpdateImported()
        {
            float dt = Mathf.Max(Time.deltaTime, 0.0001f), t = Time.time + phase;
            sit = Mathf.MoveTowards(sit, sitTarget, dt * 2.4f);
            float s = sit * sit * (3 - 2 * sit);
            float run = Mathf.Clamp01((moveSpeed - 4f) / 2.5f);
            walkAmt = Mathf.MoveTowards(walkAmt, Mathf.Clamp01(moveSpeed / 1.2f), dt * 6f);
            walkPhase += dt * (5f + moveSpeed * 1.2f);
            float stand = (1 - s) * (grounded ? 1 : 0);
            float amp = Mathf.Lerp(30f, 46f, run) * walkAmt * stand;
            float sw = Mathf.Sin(walkPhase);

            if (grounded && !wasGrounded) land = 1f;
            wasGrounded = grounded;
            land = Mathf.MoveTowards(land, 0, dt * 4f);

            // ускорение корня в локальных осях — источник «болтанки»
            Vector3 vel = (transform.position - lastPos) / dt;
            Vector3 acc = (vel - lastVel) / dt;
            lastPos = transform.position; lastVel = vel;
            Vector3 la = transform.InverseTransformDirection(acc);
            la = Vector3.ClampMagnitude(la, 40f);

            float bounce = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.05f * walkAmt * stand;
            float breath = Mathf.Sin(t * 1.8f) * 0.006f;
            hips.localPosition = hipsBase + Vector3.up * (Mathf.Lerp(0f, -0.31f, s) + bounce + breath - land * 0.09f);
            hips.localRotation = Quaternion.Euler(0, sw * 6f * walkAmt * stand, Mathf.Sin(walkPhase) * 3f * walkAmt * stand);

            // корпус: пружина наклона
            Spring(ref lean.x, ref leanVel.x, Mathf.Clamp(-la.z * 0.9f, -18f, 18f) + (6f + run * 10f) * walkAmt * stand + land * 14f, 90f, 9f, dt);
            Spring(ref lean.y, ref leanVel.y, Mathf.Clamp(la.x * 0.9f, -14f, 14f), 90f, 9f, dt);
            torso.localRotation = Quaternion.Euler(lean.x, -sw * 7f * walkAmt * stand, -lean.y);

            // ноги
            float air = grounded ? 0 : 1;
            float kL = Mathf.Max(0, -Mathf.Sin(walkPhase)) * 60f * walkAmt * stand;
            float kR = Mathf.Max(0, Mathf.Sin(walkPhase)) * 60f * walkAmt * stand;
            legL.localRotation = Quaternion.Euler(Mathf.Lerp(sw * amp - air * 30f, -88f, s), 0, 0);
            legR.localRotation = Quaternion.Euler(Mathf.Lerp(-sw * amp - air * 12f, -88f, s), 0, 0);
            if (kneeL != null) kneeL.localRotation = Quaternion.Euler(Mathf.Lerp(kL + air * 55f, 88f, s), 0, 0);
            if (kneeR != null) kneeR.localRotation = Quaternion.Euler(Mathf.Lerp(kR + air * 25f, 88f, s), 0, 0);

            // руки: мотаются на пружинах
            bool isTyping = typing || Time.time < typingUntil;
            float targetL, targetR, zL = -8f, zR = 8f, bendL = 12f, bendR = 12f;
            if (Time.time < waveUntil)
            {
                targetL = Mathf.Sin(t) * 4f; targetR = 0;
                zR = 150f + Mathf.Sin(Time.time * 14f) * 20f; bendR = 30f + Mathf.Sin(Time.time * 14f) * 15f;
            }
            else if (s > 0.5f && (isTyping || handsOnDesk))
            {
                float k = isTyping ? 1 : 0;
                targetL = -48f + Mathf.Max(0, Mathf.Sin(t * 13f)) * 8f * k;
                targetR = -48f + Mathf.Max(0, Mathf.Sin(t * 13f + 1.7f)) * 8f * k;
                bendL = bendR = 50f; zL = -10f; zR = 10f;
            }
            else if (!grounded) { targetL = targetR = -40f; zL = -60f; zR = 60f; bendL = bendR = 35f; }
            else
            {
                float idle = Mathf.Sin(t * 1.3f) * 3f * (1 - walkAmt);
                targetL = -sw * amp * 0.9f + idle - lean.x * 0.5f;
                targetR = sw * amp * 0.9f - idle - lean.x * 0.5f;
                zL = -8f - run * 10f + lean.y * 0.6f; zR = 8f + run * 10f + lean.y * 0.6f;
                bendL = 15f + run * 30f; bendR = 15f + run * 30f;
            }
            Spring(ref armFlopL, ref armFlopVelL, targetL, 140f, 8f, dt);
            Spring(ref armFlopR, ref armFlopVelR, targetR, 140f, 8f, dt);
            armL.localRotation = Quaternion.Euler(armFlopL, 0, zL);
            armR.localRotation = Quaternion.Euler(armFlopR, 0, zR);
            if (elbowL != null) elbowL.localRotation = Quaternion.Euler(-bendL, 0, 0);
            if (elbowR != null) elbowR.localRotation = Quaternion.Euler(-bendR, 0, 0);

            // голова: запаздывает за корпусом и болтается
            Spring(ref headLag.x, ref headLagVel.x, -lean.x * 0.7f + Mathf.Sin(walkPhase * 2f) * 3f * walkAmt * stand, 70f, 6f, dt);
            Spring(ref headLag.y, ref headLagVel.y, lean.y * 0.8f, 70f, 6f, dt);
            float yawHead = Mathf.Sin(t * 0.6f) * 8f * (1 - walkAmt);
            float pitchHead = isTyping ? 10f + Mathf.Sin(t * 0.9f) * 3f : Mathf.Sin(t * 0.8f) * 3f;
            var cam = Camera.main;
            if (lookAtPlayer && cam != null)
            {
                var to = cam.transform.position - head.position; to.y = 0;
                if (to.magnitude < 5f && to.magnitude > 0.01f)
                    yawHead = Mathf.Clamp(Mathf.DeltaAngle(0, Quaternion.LookRotation(to).eulerAngles.y - transform.eulerAngles.y), -60, 60);
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitchHead + headLag.x, yawHead, headLag.y + Mathf.Sin(t * 0.7f) * 3f), dt * 8f);

            // моргание
            if (blinkT < 0 && Time.time > nextBlink) blinkT = 0;
            if (blinkT >= 0)
            {
                blinkT += dt;
                float kb = blinkT < 0.07f ? 1 - blinkT / 0.07f : Mathf.Min(1, (blinkT - 0.07f) / 0.07f);
                for (int i = 0; i < eyes.Length && i < eyeBase.Length; i++)
                    if (eyes[i] != null)
                    {
                        var v = eyeBase[i]; int a = i < eyeAxis.Length ? eyeAxis[i] : 1; float f = Mathf.Max(0.1f, kb);
                        if (a == 0) v.x *= f; else if (a == 1) v.y *= f; else v.z *= f;
                        eyes[i].localScale = v;
                    }
                if (blinkT > 0.14f) { blinkT = -1; nextBlink = Time.time + Random.Range(2f, 5f); }
            }
        }

        // Копия иерархии костей, где у каждой кости оси как у самого персонажа (Y вверх, Z вперёд).
        // Меши переезжают к новым костям, сохраняя своё положение в мире.
        Transform NormalizeRig(Transform src, Transform dstParent)
        {
            var n = new GameObject(ModelLib.Clean(src.name)).transform;
            n.SetParent(dstParent, false);
            n.position = src.position;
            n.rotation = transform.rotation;
            var kids = new List<Transform>();
            foreach (Transform c in src) kids.Add(c);
            foreach (var c in kids)
            {
                if (c.GetComponent<Renderer>() == null) NormalizeRig(c, n);
                else c.SetParent(n, true);
            }
            return n;
        }

        // ---------- Анимация ----------
        void Update()
        {
            if (hips == null) return;
            if (imported) { UpdateImported(); return; }
            float dt = Time.deltaTime, t = Time.time + phase;

            sit = Mathf.MoveTowards(sit, sitTarget, dt * 2.6f);
            float s = sit * sit * (3 - 2 * sit);
            float run = Mathf.Clamp01((moveSpeed - 4f) / 2.5f);
            walkAmt = Mathf.MoveTowards(walkAmt, Mathf.Clamp01(moveSpeed / 1.2f), dt * 6f);
            walkPhase += dt * (5.5f + moveSpeed * 1.35f);
            float stand = (1 - s) * (grounded ? 1 : 0);
            float amp = Mathf.Lerp(32f, 48f, run) * walkAmt * stand;
            float sw = Mathf.Sin(walkPhase);

            if (grounded && !wasGrounded) land = 1f;
            wasGrounded = grounded;
            land = Mathf.MoveTowards(land, 0, dt * 5f);

            // бёдра: подпрыгивание при ходьбе, дыхание, приседание при приземлении
            float bounce = Mathf.Abs(Mathf.Cos(walkPhase)) * 0.045f * walkAmt * stand;
            float breath = Mathf.Sin(t * 2f) * 0.008f;
            hips.localPosition = new Vector3(0, Mathf.Lerp(0.48f, 0.56f, s) + bounce + breath - land * 0.07f, 0);
            torso.localRotation = Quaternion.Euler(Mathf.Lerp(4f + run * 8f, 0, 1 - walkAmt * stand) + land * 10f, Mathf.Sin(walkPhase) * 5f * walkAmt * stand, 0);
            torso.localScale = new Vector3(1 + breath + land * 0.06f, 1 - breath - land * 0.06f, 1);

            // ноги
            float air = grounded ? 0 : 1;
            float kneeL0 = Mathf.Max(0, -Mathf.Sin(walkPhase)) * 55f * walkAmt * stand;
            float kneeR0 = Mathf.Max(0, Mathf.Sin(walkPhase)) * 55f * walkAmt * stand;
            legL.localRotation = Quaternion.Euler(Mathf.Lerp(sw * amp - air * 25f, -90f, s), 0, 0);
            legR.localRotation = Quaternion.Euler(Mathf.Lerp(-sw * amp - air * 10f, -90f, s), 0, 0);
            kneeL.localRotation = Quaternion.Euler(Mathf.Lerp(kneeL0 + air * 50f, 90f, s), 0, 0);
            kneeR.localRotation = Quaternion.Euler(Mathf.Lerp(kneeR0 + air * 20f, 90f, s), 0, 0);

            // руки
            bool isTyping = typing || Time.time < typingUntil;
            if (Time.time < waveUntil)
            {
                armR.localRotation = Quaternion.Euler(0, 0, 150 + Mathf.Sin(Time.time * 14f) * 22f);
                armL.localRotation = Quaternion.Euler(Mathf.Sin(t) * 4f, 0, -6);
            }
            else if (s > 0.5f && (isTyping || handsOnDesk))
            {
                float k = isTyping ? 1 : 0;
                armL.localRotation = Quaternion.Euler(-70 + Mathf.Max(0, Mathf.Sin(t * 13f)) * 12f * k, 0, 8);
                armR.localRotation = Quaternion.Euler(-70 + Mathf.Max(0, Mathf.Sin(t * 13f + 1.7f)) * 12f * k, 0, -8);
            }
            else if (!grounded)
            {
                armL.localRotation = Quaternion.Euler(-30, 0, -55);
                armR.localRotation = Quaternion.Euler(-30, 0, 55);
            }
            else
            {
                float idle = Mathf.Sin(t * 1.3f) * 4f * (1 - walkAmt);
                armL.localRotation = Quaternion.Euler(-sw * amp * 0.85f + idle, 0, -6 - run * 6);
                armR.localRotation = Quaternion.Euler(sw * amp * 0.85f - idle, 0, 6 + run * 6);
            }

            // голова: покачивание и взгляд на игрока
            float yawHead = Mathf.Sin(t * 0.6f) * 8f * (1 - walkAmt);
            float pitchHead = isTyping ? 8f + Mathf.Sin(t * 0.9f) * 3f : Mathf.Sin(t * 0.8f) * 3f;
            var cam = Camera.main;
            if (lookAtPlayer && cam != null)
            {
                var to = cam.transform.position - head.position; to.y = 0;
                if (to.magnitude < 5f && to.magnitude > 0.01f)
                {
                    float want = Mathf.DeltaAngle(0, Quaternion.LookRotation(to).eulerAngles.y - transform.eulerAngles.y);
                    yawHead = Mathf.Clamp(want, -60, 60);
                }
            }
            head.localRotation = Quaternion.Slerp(head.localRotation, Quaternion.Euler(pitchHead, yawHead, Mathf.Sin(t * 0.7f) * 3f), dt * 5f);

            // моргание
            if (blinkT < 0 && Time.time > nextBlink) blinkT = 0;
            if (blinkT >= 0)
            {
                blinkT += dt;
                float k = blinkT < 0.07f ? 1 - blinkT / 0.07f : Mathf.Min(1, (blinkT - 0.07f) / 0.07f);
                for (int i = 0; i < eyes.Length && i < eyeBase.Length; i++)
                    if (eyes[i] != null) eyes[i].localScale = new Vector3(eyeBase[i].x, eyeBase[i].y * Mathf.Max(0.1f, k), eyeBase[i].z);
                if (blinkT > 0.14f) { blinkT = -1; nextBlink = Time.time + Random.Range(2f, 5f); }
            }
        }
    }

    // ================= Мелкие анимации окружения =================
    public class WallClock : MonoBehaviour
    {
        public Transform hour, minute;
        void Update()
        {
            var now = DateTime.Now;
            float m = now.Minute + now.Second / 60f, h = (now.Hour % 12) + m / 60f;
            if (minute != null) minute.localRotation = Quaternion.Euler(0, 0, -m * 6f);
            if (hour != null) hour.localRotation = Quaternion.Euler(0, 0, -h * 30f);
        }
    }

    // Облако плывёт за окном и плавно появляется/исчезает у краёв рамы
    public class Cloud : MonoBehaviour
    {
        public float halfWidth = 1.2f, speed = 0.12f;
        Vector3 baseScale; float x;
        void Start() { baseScale = transform.localScale; x = transform.localPosition.x; }
        void Update()
        {
            x += speed * Time.deltaTime;
            if (x > halfWidth) x = -halfWidth;
            float edge = Mathf.Clamp01((halfWidth - Mathf.Abs(x)) / 0.35f);
            var p = transform.localPosition; p.x = x; transform.localPosition = p;
            transform.localScale = baseScale * edge;
        }
    }

    // Лёгкое покачивание листьев
    public class Sway : MonoBehaviour
    {
        float ph; Quaternion baseRot;
        void Start() { ph = Random.Range(0f, 6f); baseRot = transform.localRotation; }
        void Update() { transform.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(Time.time * 0.9f + ph) * 3f, 0, Mathf.Cos(Time.time * 0.7f + ph) * 3f); }
    }
}
