// Пост-обработка и качество рендера для URP: мягкое свечение, сочные цвета, виньетка, сглаживание, тени, затенение углов.
// Сборка Intern.URP компилируется, только если в проекте есть URP.
// Настройки графики игры приходят через Configure и ConfigureQuality (игра вызывает их по имени — прямой ссылки между сборками нет).
using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Intern.Look
{
    public partial class UrpLook : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<UrpLook>() != null) return;
            new GameObject("InternLook").AddComponent<UrpLook>();
        }

        // --- настройки графики (по умолчанию — «Высокое») ---
        static float cfgScale = 1f, cfgShadow = 30f, cfgExposure = 0.1f;
        static int cfgMsaa = 4, cfgAa = 2, cfgVersion = 1, cfgAo = 2, cfgShadowRes = 2048, cfgCascades = 4;
        static bool cfgPost = true;

        // aaMode: 0 — нет, 1 — FXAA, 2 — SMAA. msaa: 1, 2, 4, 8
        public static void Configure(float renderScale, int msaa, int aaMode, float shadowDistance, bool post, float exposure)
        {
            cfgScale = Mathf.Clamp(renderScale, 0.25f, 2f); cfgMsaa = msaa; cfgAa = aaMode; cfgShadow = Mathf.Max(0f, shadowDistance);
            cfgPost = post; cfgExposure = exposure; cfgVersion++;
            ApplyPipeline();
        }

        // ao: 0 — выкл, 1 — среднее, 2 — высокое; карта теней 2048/4096; каскады 2 или 4
        public static void ConfigureQuality(int ao, int shadowRes, int cascades)
        {
            cfgAo = Mathf.Clamp(ao, 0, 2); cfgShadowRes = shadowRes; cfgCascades = Mathf.Clamp(cascades, 1, 4); cfgVersion++;
            ApplyPipeline();
        }

        // Настройки ассета конвейера и фичи затенения углов (SSAO). Затенение — только InterleavedGradient:
        // BlueNoise из шаблона URP давал зернистую «рябь» у стен и тёмные рваные контуры у предметов
        static void ApplyPipeline()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            CaptureOriginals(urp);
            urp.renderScale = cfgScale;
            urp.msaaSampleCount = cfgMsaa;
            urp.shadowDistance = cfgShadow;
            urp.shadowCascadeCount = cfgCascades;
            urp.mainLightShadowmapResolution = cfgShadowRes;
            var ssao = FindFeature("ScreenSpaceAmbientOcclusion");
            if (ssao == null) return;
            ssao.SetActive(cfgAo > 0);
            var st = SsaoSettings(ssao);
            if (st == null || cfgAo == 0) return;
            // Метод и число выборок — ключевые слова шейдера: сборка оставляет только варианты из ассета (InterleavedGradient, High),
            // поэтому здесь их не меняем. «Среднее» отличается половинным разрешением затенения — это параметр, не вариант шейдера
            SetField(st, "AOMethod", 1);                          // InterleavedGradient
            SetField(st, "Samples", 0);                           // High
            SetField(st, "NormalSamples", 2);                     // High
            SetField(st, "BlurQuality", 0);                       // High (bilateral)
            SetField(st, "Downsample", cfgAo < 2);                // «Среднее» — половинное разрешение
            SetField(st, "Intensity", L.aoIntensity);
            SetField(st, "Radius", L.aoRadius);
            SetField(st, "DirectLightingStrength", L.aoDirect);
        }

        Camera configured;
        int applied;
        Volume vol;
        ColorAdjustments ca;
        Tonemapping tm;
        Bloom bloom;
        WhiteBalance wb;
        Vignette vg;

        void Start()
        {
            inst = this;
            ApplyPipeline();

            // Отключаем чужие Volume из шаблонной сцены, чтобы эффекты не складывались
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;

            vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();

            tm = p.Add<Tonemapping>(true);
            bloom = p.Add<Bloom>(true); bloom.scatter.Override(0.65f);
            ca = p.Add<ColorAdjustments>(true);
            wb = p.Add<WhiteBalance>(true);
            vg = p.Add<Vignette>(true); vg.smoothness.Override(0.5f);
            vol.sharedProfile = p;
            ApplyLook();
            applied = 0;
        }

        void Update()
        {
            var c = Camera.main;
            if (applied == cfgVersion && c == configured) return;
            applied = cfgVersion;
            if (vol != null) vol.enabled = cfgPost;
            ApplyLook();
            if (c == null) return;
            var data = c.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = cfgPost;
            data.antialiasing = cfgAa == 0 ? AntialiasingMode.None : cfgAa == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = cfgShadow > 0f;
            configured = c;
        }

        // ---------- доступ к фичам рендера (их настройки у URP внутренние — через reflection) ----------
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        static ScriptableRendererFeature FindFeature(string typeName)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return null;
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(asset) as ScriptableRendererData[] : null;
            if (list == null) return null;
            foreach (var d in list)
                if (d != null)
                    foreach (var feat in d.rendererFeatures)
                        if (feat != null && feat.GetType().Name == typeName) return feat;
            return null;
        }

        static object SsaoSettings(ScriptableRendererFeature ssao)
        {
            if (ssao == null) return null;
            var f = ssao.GetType().GetField("m_Settings", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(ssao) : null;
        }

        static void SetField(object st, string name, object value)
        {
            var f = st.GetType().GetField(name, Any);
            if (f == null) return;
            if (f.FieldType.IsEnum) f.SetValue(st, Enum.ToObject(f.FieldType, Convert.ToInt32(value)));
            else if (f.FieldType == typeof(bool)) f.SetValue(st, Convert.ToBoolean(value));
            else if (f.FieldType == typeof(int)) f.SetValue(st, Convert.ToInt32(value));
            else f.SetValue(st, Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        // ---------- эксперименты для инструментов редактора ----------
        // Tweak("ssao=off"), Tweak("ssao.Intensity=0.3"), Tweak("shadowres=4096"), Tweak("cascades=4"), Tweak("dump"),
        // Tweak("reset") — вернуть настройки игры
        public static string Tweak(string spec)
        {
            var sb = new StringBuilder();
            foreach (var part in (spec ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim(), key = p, val = "";
                int eq = p.IndexOf('=');
                if (eq > 0) { key = p.Substring(0, eq).Trim(); val = p.Substring(eq + 1).Trim(); }
                try { sb.Append(TweakOne(key, val)).Append('\n'); }
                catch (Exception e) { sb.Append(key + ": ошибка " + e.Message).Append('\n'); }
            }
            return sb.ToString().TrimEnd();
        }

        static string TweakOne(string key, string val)
        {
            var ssao = FindFeature("ScreenSpaceAmbientOcclusion");
            var st = SsaoSettings(ssao);
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (key == "reset") { L = Defaults.Clone(); RestoreExperiments(); ApplyPipeline(); if (inst != null) inst.ApplyLook(); return "reset"; }
            string look = TweakLook(key, val);
            if (look != null) return look;
            if (key == "dump")
            {
                var sb = new StringBuilder("ssao: " + (ssao == null ? "нет" : ssao.isActive ? "вкл" : "выкл"));
                if (st != null) foreach (var f in st.GetType().GetFields(Any)) sb.Append(", " + f.Name + "=" + f.GetValue(st));
                if (asset != null) sb.Append("\nurp: scale=" + asset.renderScale + ", msaa=" + asset.msaaSampleCount + ", shadowDist=" + asset.shadowDistance + ", cascades=" + asset.shadowCascadeCount + ", shadowRes=" + asset.mainLightShadowmapResolution + ", softShadows=" + asset.supportsSoftShadows);
                return sb.ToString();
            }
            if (key == "ssao")
            {
                if (ssao == null) return "ssao: нет фичи";
                ssao.SetActive(val == "on" || val == "1" || val == "true");
                return "ssao=" + (ssao.isActive ? "вкл" : "выкл");
            }
            if (key.StartsWith("ssao."))
            {
                if (st == null) return key + ": нет настроек";
                var f = st.GetType().GetField(key.Substring(5), Any);
                if (f == null) return key + ": нет поля";
                SetField(st, f.Name, f.FieldType.IsEnum || f.FieldType == typeof(int) ? (object)int.Parse(val) : f.FieldType == typeof(bool) ? (object)(val == "1" || val == "true" || val == "on") : (object)val);
                return key + "=" + f.GetValue(st);
            }
            if (asset == null) return key + ": нет URP";
            if (key == "shadowres") { asset.mainLightShadowmapResolution = int.Parse(val); return key + "=" + asset.mainLightShadowmapResolution; }
            if (key == "cascades") { asset.shadowCascadeCount = int.Parse(val); return key + "=" + asset.shadowCascadeCount; }
            return key + ": неизвестный ключ";
        }

        // ---------- в редакторе настройки пишутся прямо в ассеты конвейера — после игры возвращаем как было ----------
#if UNITY_EDITOR
        static bool haveOrig;
        static float origScale, origShadow; static int origMsaa, origCascades, origShadowRes; static bool origSsaoOn;
        static object[] origSsao;
        static FieldInfo[] ssaoFields;
#endif
        static void CaptureOriginals(UniversalRenderPipelineAsset urp)
        {
#if UNITY_EDITOR
            if (haveOrig) return;
            haveOrig = true;
            origScale = urp.renderScale; origMsaa = urp.msaaSampleCount; origShadow = urp.shadowDistance; origCascades = urp.shadowCascadeCount; origShadowRes = urp.mainLightShadowmapResolution;
            var ssao = FindFeature("ScreenSpaceAmbientOcclusion"); var st = SsaoSettings(ssao);
            if (ssao != null) origSsaoOn = ssao.isActive;
            if (st != null) { ssaoFields = st.GetType().GetFields(Any); origSsao = new object[ssaoFields.Length]; for (int i = 0; i < ssaoFields.Length; i++) origSsao[i] = ssaoFields[i].GetValue(st); }
#endif
        }

#if UNITY_EDITOR
        void OnDestroy()
        {
            if (!haveOrig) return;
            haveOrig = false;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) { urp.renderScale = origScale; urp.msaaSampleCount = origMsaa; urp.shadowDistance = origShadow; urp.shadowCascadeCount = origCascades; urp.mainLightShadowmapResolution = origShadowRes; }
            var ssao = FindFeature("ScreenSpaceAmbientOcclusion"); var st = SsaoSettings(ssao);
            if (ssao != null) ssao.SetActive(origSsaoOn);
            if (st != null && ssaoFields != null) for (int i = 0; i < ssaoFields.Length; i++) ssaoFields[i].SetValue(st, origSsao[i]);
        }
#endif
    }
}
