// Пост-обработка для URP: мягкое свечение, сочные цвета, виньетка, сглаживание.
// Сборка Intern.URP компилируется, только если в проекте есть URP.
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

        Camera configured;

        void Start()
        {
            // Качество: дальность и мягкость теней, сглаживание краёв
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null)
            {
                urp.shadowDistance = 28f;
                urp.shadowCascadeCount = 2;
                urp.msaaSampleCount = 4;
            }

            // Отключаем чужие Volume из шаблонной сцены, чтобы эффекты не складывались
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None)) v.enabled = false;

            var vol = gameObject.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();

            var tm = p.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true); bloom.threshold.Override(0.95f); bloom.intensity.Override(0.4f); bloom.scatter.Override(0.65f);
            var ca = p.Add<ColorAdjustments>(true); ca.saturation.Override(4f); ca.contrast.Override(8f); ca.postExposure.Override(0.1f);
            var wb = p.Add<WhiteBalance>(true); wb.temperature.Override(2f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.16f); vg.smoothness.Override(0.5f);
            vol.sharedProfile = p;
        }

        void Update()
        {
            var c = Camera.main;
            if (c == null || c == configured) return;
            var data = c.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            configured = c;
        }
    }
}
