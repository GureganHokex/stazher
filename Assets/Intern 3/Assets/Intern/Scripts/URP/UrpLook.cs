// Пост-обработка для URP: мягкое свечение, сочные цвета, виньетка, сглаживание.
// Сборка Intern.URP компилируется, только если в проекте есть URP.
// Настройки графики игры приходят через Configure (игра вызывает его по имени — прямой ссылки между сборками нет).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Intern.Look
{
    public class UrpLook : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<UrpLook>() != null) return;
            new GameObject("InternLook").AddComponent<UrpLook>();
        }

        // --- настройки графики (по умолчанию — «Высокое») ---
        static float cfgScale = 1f, cfgShadow = 28f, cfgExposure = 0.1f;
        static int cfgMsaa = 4, cfgAa = 2, cfgVersion = 1;
        static bool cfgPost = true;

        // aaMode: 0 — нет, 1 — FXAA, 2 — SMAA. msaa: 1, 2, 4, 8
        public static void Configure(float renderScale, int msaa, int aaMode, float shadowDistance, bool post, float exposure)
        {
            cfgScale = Mathf.Clamp(renderScale, 0.25f, 2f); cfgMsaa = msaa; cfgAa = aaMode; cfgShadow = Mathf.Max(0f, shadowDistance);
            cfgPost = post; cfgExposure = exposure; cfgVersion++;
        }

        Camera configured;
        int applied;
        UniversalRenderPipelineAsset urp;
        Volume vol;
        ColorAdjustments ca;
#if UNITY_EDITOR
        float origScale, origShadow; int origMsaa, origCascades; bool haveOrig;
#endif

        void Start()
        {
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
#if UNITY_EDITOR
                origScale = urp.renderScale; origMsaa = urp.msaaSampleCount; origShadow = urp.shadowDistance; origCascades = urp.shadowCascadeCount; haveOrig = true;
#endif
                urp.shadowCascadeCount = 2;
            }

            // Отключаем чужие Volume из шаблонной сцены, чтобы эффекты не складывались
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;

            vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();

            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.65f);
            ca = p.Add<ColorAdjustments>(true); ca.saturation.Override(4f); ca.contrast.Override(8f); ca.postExposure.Override(0.1f);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(2f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.16f); vg.smoothness.Override(0.5f);
            vol.sharedProfile = p;
            applied = 0;
        }

        void Update()
        {
            var c = Camera.main;
            if (applied == cfgVersion && c == configured) return;
            applied = cfgVersion;
            if (urp != null)
            {
                urp.renderScale = cfgScale;
                urp.msaaSampleCount = cfgMsaa;
                urp.shadowDistance = cfgShadow;
            }
            if (vol != null) vol.enabled = cfgPost;
            if (ca != null) ca.postExposure.Override(cfgExposure);
            if (c == null) return;
            var data = c.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = cfgPost;
            data.antialiasing = cfgAa == 0 ? AntialiasingMode.None : cfgAa == 1 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = cfgShadow > 0f;
            configured = c;
        }

#if UNITY_EDITOR
        // В редакторе настройки пишутся прямо в ассет конвейера — после игры возвращаем как было, чтобы не пачкать проект
        void OnDestroy()
        {
            if (!haveOrig || urp == null) return;
            urp.renderScale = origScale; urp.msaaSampleCount = origMsaa; urp.shadowDistance = origShadow; urp.shadowCascadeCount = origCascades;
        }
#endif
    }
}
