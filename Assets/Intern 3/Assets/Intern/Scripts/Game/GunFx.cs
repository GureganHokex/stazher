// Выстрел (спринт 8 версии 0.9 «Оружие в руках»): вспышка у дульного среза — звезда огня спереди, языки пламени
// вдоль ствола, ореол и короткий свет; дымок, искры, трассер, летящая пуля; стреляные гильзы с физикой.
// Текстуры огня, дыма и искр рисуются кодом при первом выстреле (Intern/Fx, режим текстуры).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public static partial class Fx
    {
        // ---------- текстуры ----------
        static Texture2D tStar, tSide, tSmoke, tSpark, tGlow;
        static readonly Dictionary<int, Texture2D> tRays = new Dictionary<int, Texture2D>();
        static readonly Dictionary<string, Material> texMats = new Dictionary<string, Material>();
        static Mesh qCenter, qSide;

        static float S(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Цвет пламени по яркости: тёмно-оранжевый → оранжевый → жёлтый → белый
        static Color Fire(float i)
        {
            i = Mathf.Clamp01(i);
            Color a = new Color(0.95f, 0.30f, 0.05f), b = new Color(1f, 0.58f, 0.16f), c = new Color(1f, 0.86f, 0.52f), d = new Color(1f, 0.98f, 0.9f);
            Color k = i < 0.35f ? Color.Lerp(a, b, i / 0.35f) : i < 0.7f ? Color.Lerp(b, c, (i - 0.35f) / 0.35f) : Color.Lerp(c, d, (i - 0.7f) / 0.3f);
            k.a = i; return k;
        }

        static Texture2D Make(string name, int w, int h, System.Func<float, float, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        static void Textures()
        {
            if (tStar != null) return;
            var rnd = new System.Random(17);
            // спереди: неровный цветок из 7 лепестков с горячей серединой
            int n = 7; var len = new float[n]; var ang = new float[n]; var wid = new float[n];
            for (int k = 0; k < n; k++) { len[k] = 0.55f + 0.45f * (float)rnd.NextDouble(); ang[k] = (k + 0.35f * (float)rnd.NextDouble()) / n * Mathf.PI * 2f; wid[k] = 0.22f + 0.12f * (float)rnd.NextDouble(); }
            tStar = Make("fx_star", 128, 128, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x);
                float i = Mathf.Exp(-(r / 0.27f) * (r / 0.27f)) + 0.28f * Mathf.Exp(-(r / 0.6f) * (r / 0.6f));
                for (int k = 0; k < n; k++)
                {
                    float d = Mathf.DeltaAngle(th * Mathf.Rad2Deg, ang[k] * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                    float q = 1f - r / len[k]; if (q <= 0f) continue;
                    float ww = wid[k] * (0.25f + q);
                    i = Mathf.Max(i, Mathf.Exp(-(d / ww) * (d / ww)) * Mathf.Pow(q, 0.7f) * 0.95f);
                }
                i *= 0.72f + 0.45f * Mathf.PerlinNoise(u * 9f + 3f, v * 9f + 7f);
                i *= S((1f - r) / 0.12f);
                return Fire(Mathf.Pow(Mathf.Clamp01(i), 0.7f));
            });
            // сбоку: короткая яркая вспышка у среза, «бочка» и факел, расширяющийся и гаснущий к концу
            tSide = Make("fx_side", 128, 64, (u, v) =>
            {
                float x = u, y = (v - 0.5f) * 2f;
                float w = 0.14f + 0.42f * Mathf.Exp(-((x - 0.2f) / 0.11f) * ((x - 0.2f) / 0.11f)) + 0.8f * S(x / 0.4f) * (1f - S((x - 0.55f) / 0.45f));
                float i = Mathf.Exp(-(y / w) * (y / w) * 2.2f);
                i *= 1f - 0.55f * S((x - 0.15f) / 0.85f);
                i *= 0.6f + 0.55f * Mathf.PerlinNoise(x * 7f + 1.3f, y * 2.2f + 5.1f);
                i *= S(x / 0.035f) * S((1f - x) / 0.3f);
                return Fire(Mathf.Pow(Mathf.Clamp01(i), 0.65f));
            });
            tSmoke = Make("fx_smoke", 64, 64, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y);
                float nz = 0.5f * Mathf.PerlinNoise(u * 4f + 11f, v * 4f + 2f) + 0.5f * Mathf.PerlinNoise(u * 9f + 5f, v * 9f + 9f);
                float a = S((1f - r) / 0.7f) * (0.45f + 0.75f * nz);
                return new Color(0.82f, 0.82f, 0.84f, Mathf.Clamp01(a));
            });
            tSpark = Make("fx_spark", 64, 16, (u, v) =>
            {
                float y = (v - 0.5f) * 2f;
                float a = Mathf.Exp(-(y / 0.35f) * (y / 0.35f)) * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.6f) * (0.5f + 0.5f * u);
                return new Color(1f, 0.85f, 0.55f, a);
            });
            tGlow = Make("fx_glow", 64, 64, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Exp(-(r / 0.42f) * (r / 0.42f)) * S((1f - r) / 0.2f);
                return new Color(1f, 0.7f, 0.35f, a);
            });
            // меши: квадрат по центру (плоскость XY) и полоса вдоль оси z от среза (плоскость YZ)
            qCenter = new Mesh { name = "fx_quad" };
            qCenter.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
            qCenter.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            qCenter.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            qCenter.RecalculateBounds();
            qSide = new Mesh { name = "fx_side" };
            qSide.vertices = new[] { new Vector3(0, -0.5f, 0), new Vector3(0, -0.5f, 1), new Vector3(0, 0.5f, 1), new Vector3(0, 0.5f, 0) };
            qSide.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            qSide.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            qSide.RecalculateBounds();
        }

        static Texture2D Rays(int count)
        {
            Texture2D t;
            if (tRays.TryGetValue(count, out t) && t != null) return t;
            var rnd = new System.Random(31 + count);
            var off = new float[count]; var ln = new float[count];
            for (int k = 0; k < count; k++) { off[k] = (k + 0.12f * (float)rnd.NextDouble()) / count * Mathf.PI * 2f; ln[k] = 0.75f + 0.25f * (float)rnd.NextDouble(); }
            t = Make("fx_rays" + count, 128, 128, (u, v) =>
            {
                float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y), th = Mathf.Atan2(y, x);
                float i = 0.9f * Mathf.Exp(-(r / 0.13f) * (r / 0.13f));
                for (int k = 0; k < count; k++)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(th * Mathf.Rad2Deg, off[k] * Mathf.Rad2Deg)) * Mathf.Deg2Rad * r;   // расстояние до луча
                    float q = 1f - r / ln[k]; if (q <= 0f) continue;
                    float ww = 0.012f + 0.05f * q;
                    i = Mathf.Max(i, Mathf.Exp(-(d / ww) * (d / ww)) * Mathf.Pow(q, 1.2f));
                }
                return Fire(i * S((1f - r) / 0.1f));
            });
            tRays[count] = t;
            return t;
        }

        // Материал Intern/Fx с текстурой (режим 3): аддитивный огонь или полупрозрачный дым
        public static Material TexMat(Texture2D tex, bool additive)
        {
            if (tex == null) return null;
            string key = tex.name + additive;
            Material m;
            if (texMats.TryGetValue(key, out m) && m != null) return m;
            var src = Look.FxMat(Color.white, false, additive);
            if (src == null) return null;
            m = new Material(src) { name = "fx_" + key };
            m.SetFloat("_Radial", 3f);
            m.SetTexture("_MainTex", tex);
            texMats[key] = m;
            return m;
        }

        public static GameObject Quad(string name, Transform parent, bool side, Material mat)
        {
            Textures();
            var go = new GameObject(name); go.layer = 2;
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = side ? qSide : qCenter;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // ---------- дым и искры: две общие системы частиц в мире ----------
        static ParticleSystem smoke, sparks;

        static ParticleSystem MakePs(string name, Material mat, bool stretch)
        {
            var go = new GameObject(name); go.layer = 2;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = 1f; main.playOnAwake = false; main.maxParticles = stretch ? 400 : 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startSpeed = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            if (stretch)
            {
                main.gravityModifier = 0.5f;
                gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.92f, 0.7f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            }
            else
            {
                main.gravityModifier = -0.035f;
                gr.SetKeys(new[] { new GradientColorKey(new Color(0.9f, 0.88f, 0.86f), 0f), new GradientColorKey(new Color(0.75f, 0.76f, 0.8f), 1f) },
                           new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.45f, 0.45f), new GradientAlphaKey(0f, 1f) });
                var sz = ps.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 1f)));
                var lv = ps.limitVelocityOverLifetime; lv.enabled = true; lv.limit = 0.25f; lv.dampen = 0.12f;
                var nz = ps.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 0.9f; nz.scrollSpeed = 0.4f;
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            }
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.025f; r.lengthScale = 1.5f; }
            else { r.renderMode = ParticleSystemRenderMode.Billboard; r.sortMode = ParticleSystemSortMode.Distance; }
            ps.Play();
            return ps;
        }

        static bool Particles()
        {
            Textures();
            if (smoke == null) { var m = TexMat(tSmoke, false); if (m == null) return false; smoke = MakePs("FxSmoke", m, false); }
            if (sparks == null) { var m = TexMat(tSpark, true); if (m == null) return false; sparks = MakePs("FxSparks", m, true); }
            return true;
        }

        public static void Smoke(Vector3 at, Vector3 fwd, int count, float size, Vector3 carry)
        {
            if (count <= 0 || !Particles()) return;
            for (int i = 0; i < count; i++)
            {
                var p = new ParticleSystem.EmitParams
                {
                    position = at + fwd * (0.03f + 0.05f * i) + Random.insideUnitSphere * 0.02f,
                    velocity = fwd * Random.Range(0.4f, 1.6f) + Random.insideUnitSphere * 0.25f + Vector3.up * 0.15f + carry * 0.6f,
                    startSize = size * Random.Range(0.8f, 1.3f), startLifetime = Random.Range(0.9f, 1.6f),
                    rotation = Random.Range(0f, 360f), startColor = new Color(1f, 1f, 1f, Random.Range(0.28f, 0.45f)),
                };
                smoke.Emit(p, 1);
            }
        }

        public static void Sparks(Vector3 at, Vector3 fwd, int count, float speed)
        {
            if (count <= 0 || !Particles()) return;
            for (int i = 0; i < count; i++)
            {
                var dir = (fwd + Random.insideUnitSphere * 0.35f).normalized;
                var p = new ParticleSystem.EmitParams
                {
                    position = at + fwd * 0.02f, velocity = dir * speed * Random.Range(0.5f, 1.2f),
                    startSize = Random.Range(0.008f, 0.016f), startLifetime = Random.Range(0.06f, 0.16f), startColor = Color.white,
                };
                sparks.Emit(p, 1);
            }
        }
    }

    // Вспышка на дульном срезе ствола: живёт на стволе (дочерняя к корню оружия, в точке Muzzle), на выстрел — 2–3 кадра.
    // Считается после того, как PlayerCombat поставил ствол в руки (порядок 200).
    [DefaultExecutionOrder(200)]
    public class MuzzleFlash : MonoBehaviour
    {
        Transform star, rays, halo; readonly Transform[] petals = new Transform[3]; readonly float[] rolls = new float[3];
        Renderer[] rs; Light lt; MaterialPropertyBlock mpb;
        float t0 = -9f, life = 0.05f, lightI; int frame0; GunSpec spec; bool silenced;
        float[] baseA;

        // На корне оружия (масштаб 1): у пустышек из FBX свой масштаб, вспышка внутри них сжималась бы в точку
        public static MuzzleFlash On(WeaponModel g)
        {
            var f = g.GetComponentInChildren<MuzzleFlash>(true);
            if (f == null)
            {
                var go = new GameObject("MuzzleFlash"); go.layer = 2;
                go.transform.SetParent(g.transform, false);
                f = go.AddComponent<MuzzleFlash>(); f.Build();
            }
            f.transform.localPosition = g.Local(g.muzzle); f.transform.localRotation = Quaternion.identity; f.transform.localScale = Vector3.one;
            return f;
        }

        void Build()
        {
            var list = new List<Renderer>();
            halo = Fx.Quad("Halo", transform, false, null).transform;
            star = Fx.Quad("Star", transform, false, null).transform;
            rays = Fx.Quad("Rays", transform, false, null).transform;
            for (int i = 0; i < 3; i++) petals[i] = Fx.Quad("Petal" + i, transform, true, null).transform;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) { list.Add(r); r.enabled = false; }
            rs = list.ToArray();
            lt = gameObject.AddComponent<Light>();
            lt.type = LightType.Point; lt.color = new Color(1f, 0.72f, 0.4f); lt.shadows = LightShadows.None; lt.enabled = false;
            lt.renderMode = LightRenderMode.ForcePixel;
            mpb = new MaterialPropertyBlock();
        }

        public void Fire(GunSpec sp, bool quiet)
        {
            spec = sp; silenced = quiet;
            t0 = Time.time; frame0 = Time.frameCount;
            var fwd = transform.forward;
            float sz = sp.flash * (quiet ? 0.25f : 1f) * Random.Range(0.85f, 1.2f);
            halo.GetComponent<Renderer>().sharedMaterial = Fx.TexMat(GlowTex(), true);
            star.GetComponent<Renderer>().sharedMaterial = Fx.TexMat(StarTex(), false);
            rays.GetComponent<Renderer>().sharedMaterial = Fx.TexMat(Fx.RaysTex(sp.spikes), true);
            halo.localScale = Vector3.one * sz * 8f;
            star.localScale = Vector3.one * sz * Random.Range(3.0f, 3.8f);
            rays.localScale = Vector3.one * sz * Random.Range(4.2f, 5.4f);
            star.localPosition = new Vector3(0f, 0f, sz * 0.25f); rays.localPosition = new Vector3(0f, 0f, sz * 0.2f); halo.localPosition = new Vector3(0f, 0f, sz * 0.5f);
            for (int i = 0; i < 3; i++) rolls[i] = Random.Range(0f, 360f);
            float roll0 = Random.Range(0f, 120f);
            for (int i = 0; i < 3; i++)
            {
                petals[i].GetComponent<Renderer>().sharedMaterial = Fx.TexMat(SideTex(), false);
                petals[i].localRotation = Quaternion.Euler(0f, 0f, roll0 + i * 60f);
                petals[i].localScale = new Vector3(1f, sz * Random.Range(2.6f, 3.4f), sz * sp.flashLen * Random.Range(3.0f, 3.9f));
                petals[i].localPosition = Vector3.zero;
            }
            star.gameObject.SetActive(!quiet); rays.gameObject.SetActive(!quiet);
            foreach (var p in petals) p.gameObject.SetActive(!quiet);
            foreach (var r in rs) r.enabled = true;
            lightI = quiet ? 0.6f : sp.light;
            lt.range = 2.5f + sp.flash * 22f; lt.intensity = lightI; lt.enabled = true;
            life = quiet ? 0.035f : 0.05f + sp.flash * 0.12f;
            Apply(1f);
            // дымок и искры
            var pl = GetComponentInParent<PlayerController>();
            var carry = Vector3.zero;
            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) carry = cc.velocity; }
            Fx.Smoke(transform.position, fwd, quiet ? 2 : sp.smoke, 0.05f + sp.flash * 0.8f, carry);
            if (!quiet) Fx.Sparks(transform.position, fwd, sp.sparks, 9f);
        }

        static Texture2D StarTex() { return Fx.StarTex(); }
        static Texture2D SideTex() { return Fx.SideTex(); }
        static Texture2D GlowTex() { return Fx.GlowTex(); }

        void Apply(float a)
        {
            if (rs == null) return;
            foreach (var r in rs)
            {
                if (r == null || !r.enabled) continue;
                // звезда и языки — полупрозрачные (огонь виден и на светлом небе), лучи и ореол — свечение поверх
                bool add = r.transform == halo || r.transform == rays;
                float k = r.transform == halo ? 0.45f : r.transform == rays ? 1.1f : 1f;
                var c = spec != null ? spec.flashTint : new Color(1f, 0.8f, 0.5f);
                Color col = add ? new Color(Mathf.Lerp(1f, c.r, 0.6f) * k, Mathf.Lerp(1f, c.g, 0.6f) * k, Mathf.Lerp(1f, c.b, 0.6f) * k, a)
                                : new Color(Mathf.Lerp(1f, c.r, 0.35f), Mathf.Lerp(1f, c.g, 0.35f), Mathf.Lerp(1f, c.b, 0.35f), Mathf.Min(1f, a * 1.6f));
                mpb.SetColor("_Color", col);
                r.SetPropertyBlock(mpb);
            }
            if (lt != null) lt.intensity = lightI * a;
        }

        void LateUpdate()
        {
            if (rs == null || t0 < 0f) return;
            float age = Time.time - t0;
            if (age > life && Time.frameCount > frame0 + 1)
            {
                foreach (var r in rs) if (r != null) r.enabled = false;
                if (lt != null) lt.enabled = false;
                t0 = -9f; return;
            }
            // звезда и ореол — лицом к камере, лепестки — вдоль ствола, повёрнуты ребром к камере как можно меньше
            var cam = Camera.main;
            if (cam != null)
            {
                var c = cam.transform;
                var bb = new[] { star, rays, halo };
                for (int i = 0; i < 3; i++)
                {
                    bb[i].rotation = Quaternion.LookRotation(bb[i].position - c.position, c.up);
                    bb[i].Rotate(0f, 0f, rolls[i], Space.Self);
                }
            }
            float a = Time.frameCount == frame0 ? 1f : Mathf.Clamp01(1f - age / life) * 0.7f;
            Apply(a);
        }
    }

    public static partial class Fx
    {
        public static Texture2D StarTex() { Textures(); return tStar; }
        public static Texture2D SideTex() { Textures(); return tSide; }
        public static Texture2D GlowTex() { Textures(); return tGlow; }
        public static Texture2D SparkTex() { Textures(); return tSpark; }
        public static Texture2D RaysTex(int n) { Textures(); return Rays(Mathf.Clamp(n, 2, 8)); }
    }

    // Летящая пуля: светящийся отрезок бежит от ствола до попадания
    public class TracerRun : MonoBehaviour
    {
        public Vector3 a, b; public float speed = 380f, length = 3.2f;
        LineRenderer lr; float t, dist;
        void Start() { lr = GetComponent<LineRenderer>(); dist = Vector3.Distance(a, b); Step(); }
        void Update() { t += Time.deltaTime; Step(); }
        void Step()
        {
            if (lr == null) return;
            float head = Mathf.Min(dist, 0.25f + t * speed), tail = Mathf.Max(0f, head - length);
            var dir = dist > 1e-4f ? (b - a) / dist : Vector3.forward;
            lr.SetPosition(0, a + dir * tail); lr.SetPosition(1, a + dir * head);
            if (tail >= dist - 0.01f) Destroy(gameObject);
        }
    }

    // Гильзы: вылетают из окна выброса, кувыркаются, звенят об пол (звук — в спринте 8, задача 5), лежат 4 с
    public static class Casings
    {
        class C { public GameObject go; public Rigidbody rb; public Collider col; public GameObject head; public float until; public Clink snd; }
        static readonly List<C> pool = new List<C>();
        static Material brass, hull;
        static PhysicsMaterial bounce;
        static Mesh cyl;

        static Material Lit(Color c, float smooth, float metal)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            var m = new Material(rp != null && rp.defaultShader != null ? rp.defaultShader : Shader.Find("Standard"));
            ModelLib.SetColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        public static void Eject(WeaponModel g, GunSpec sp, PlayerController pl)
        {
            if (g == null || sp == null) return;
            if (brass == null) { brass = Lit(new Color(0.86f, 0.64f, 0.27f), 0.78f, 0.85f); hull = Lit(new Color(0.72f, 0.1f, 0.08f), 0.45f, 0f); }
            if (bounce == null) bounce = new PhysicsMaterial("Casing") { bounciness = 0.35f, dynamicFriction = 0.45f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            if (cyl == null) { var tmp = GameObject.CreatePrimitive(PrimitiveType.Cylinder); cyl = tmp.GetComponent<MeshFilter>().sharedMesh; Object.Destroy(tmp); }
            C c = null;
            foreach (var x in pool) if (x.go == null || !x.go.activeSelf) { c = x; break; }
            if (c != null && c.go == null) { pool.Remove(c); c = null; }
            if (c == null)
            {
                if (pool.Count >= 48) { c = pool[0]; foreach (var x in pool) if (x.until < c.until) c = x; }
                else
                {
                    c = new C();
                    c.go = new GameObject("Casing"); c.go.layer = 2;
                    c.go.AddComponent<MeshFilter>().sharedMesh = cyl;
                    var mr = c.go.AddComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off;
                    var cap = c.go.AddComponent<CapsuleCollider>(); cap.radius = 0.5f; cap.height = 2f; cap.sharedMaterial = bounce; c.col = cap;
                    c.rb = c.go.AddComponent<Rigidbody>(); c.rb.mass = 0.012f; c.rb.linearDamping = 0.05f; c.rb.angularDamping = 0.4f;
                    c.rb.interpolation = RigidbodyInterpolation.Interpolate; c.rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    c.snd = c.go.AddComponent<Clink>();
                    c.head = new GameObject("Head"); c.head.layer = 2; c.head.transform.SetParent(c.go.transform, false);
                    c.head.AddComponent<MeshFilter>().sharedMesh = cyl;
                    var hr = c.head.AddComponent<MeshRenderer>(); hr.sharedMaterial = brass; hr.shadowCastingMode = ShadowCastingMode.Off;
                    c.head.transform.localPosition = new Vector3(0f, -0.72f, 0f); c.head.transform.localScale = new Vector3(1.06f, 0.3f, 1.06f);
                    pool.Add(c);
                }
            }
            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) Physics.IgnoreCollision(c.col, cc, true); }
            var t = g.transform;
            Vector3 at = g.eject != null ? g.eject.position : (g.muzzle != null ? Vector3.Lerp(g.transform.position, g.muzzle.position, 0.3f) : t.position);
            c.go.transform.SetPositionAndRotation(at, t.rotation * Quaternion.Euler(90f, 0f, 0f));
            c.go.transform.localScale = new Vector3(sp.casingD, sp.casingL * 0.5f, sp.casingD);
            c.go.GetComponent<MeshRenderer>().sharedMaterial = sp.shell ? hull : brass;
            c.head.SetActive(sp.shell);
            c.go.SetActive(true);
            bool down = g.eject != null && g.Local(g.eject).y < -0.02f;   // пулемёт выбрасывает вниз
            Vector3 v = down ? (-t.up * Random.Range(1.2f, 1.8f) + t.right * Random.Range(0.3f, 0.7f))
                             : (t.right * sp.ejectSpeed * Random.Range(0.85f, 1.15f) + t.up * Random.Range(1.1f, 1.9f) - t.forward * Random.Range(0.2f, 0.7f));
            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) v += cc.velocity; }
            c.rb.linearVelocity = v;
            c.rb.angularVelocity = t.up * Random.Range(-28f, 28f) + Random.insideUnitSphere * 10f;
            c.until = Time.time + 4f;
            if (c.snd != null) { c.snd.left = 2; c.snd.id = sp.shell ? "thunk" : "tink"; }
            Runner.Watch();
        }

        // Пустой магазин (короб пулемёта) падает на землю и лежит 6 с
        public static void DropPart(Transform part, PlayerController pl, Vector3 vel)
        {
            if (part == null) return;
            var go = Object.Instantiate(part.gameObject, part.position, part.rotation);
            go.name = "Dropped_" + ModelLib.Clean(part.name);
            go.transform.localScale = part.lossyScale;
            go.SetActive(true);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
            bool any = false; var b = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var l = go.transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                    if (!any) { b = new Bounds(l, Vector3.zero); any = true; } else b.Encapsulate(l);
                }
            }
            if (!any) { Object.Destroy(go); return; }
            var bc = go.AddComponent<BoxCollider>(); bc.center = b.center; bc.size = Vector3.Max(b.size, Vector3.one * 0.01f);
            if (bounce != null) bc.sharedMaterial = bounce;
            var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.3f; rb.angularDamping = 0.3f;
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var carry = Vector3.zero;
            if (pl != null) { var cc = pl.GetComponent<CharacterController>(); if (cc != null) { Physics.IgnoreCollision(bc, cc, true); carry = cc.velocity; } }
            rb.linearVelocity = vel + carry; rb.angularVelocity = Random.insideUnitSphere * 5f;
            var cl = go.AddComponent<Clink>(); cl.left = 1; cl.id = "thunk";
            Object.Destroy(go, 6f);
        }

        // Звон о землю: два первых удара, громкость — от скорости
        public class Clink : MonoBehaviour
        {
            public int left; public string id = "tink"; float last;
            void OnCollisionEnter(Collision c)
            {
                if (left <= 0 || Time.time - last < 0.05f) return;
                float v = c.relativeVelocity.magnitude; if (v < 0.4f) return;
                left--; last = Time.time;
                Sfx.Play(id, transform.position, Mathf.Clamp01(v / 4f) * 0.35f, 0.12f);
            }
        }

        // Убирает упавшие гильзы через 4 с
        class Runner : MonoBehaviour
        {
            static Runner me;
            public static void Watch() { if (me == null) { var go = new GameObject("CasingsRunner"); me = go.AddComponent<Runner>(); } }
            void Update()
            {
                foreach (var c in pool) if (c.go != null && c.go.activeSelf && Time.time > c.until) c.go.SetActive(false);
            }
        }
    }
}
