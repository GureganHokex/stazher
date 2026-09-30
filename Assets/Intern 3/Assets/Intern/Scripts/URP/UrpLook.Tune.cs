// «Картинка» игры: цвет, контраст, свечение, затенение углов — профиль LookProfile. Значения по умолчанию — выбранный вариант.
// Инструменты редактора (DevTools → снимки ракурса) меняют профиль, свет и материалы на лету через Tweak, чтобы сравнить
// варианты на одном кадре; «reset» возвращает всё как было.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Intern.Look
{
    public partial class UrpLook
    {
        class LookProfile
        {
            // вариант «B — ярко и сочно» (спринт 1 версии 0.9): больше контраста и цвета, заметнее затенение углов
            public int tonemap = 1;                  // 0 — нет, 1 — Neutral, 2 — ACES
            public float exposure = 0.1f, contrast = 18f, saturation = 14f, temp = 4f, tint = 0f;
            public float bloom = 0.5f, bloomTh = 0.95f, vignette = 0.16f;
            public float aoIntensity = 0.8f, aoRadius = 0.45f, aoDirect = 0.25f;
            public LookProfile Clone() { return (LookProfile)MemberwiseClone(); }
        }

        static readonly LookProfile Defaults = new LookProfile();
        static LookProfile L = Defaults.Clone();
        static UrpLook inst;

        void ApplyLook()
        {
            if (tm == null) return;
            tm.active = L.tonemap > 0;
            tm.mode.Override(L.tonemap == 2 ? TonemappingMode.ACES : TonemappingMode.Neutral);
            ca.postExposure.Override(cfgExposure + L.exposure);
            ca.contrast.Override(L.contrast);
            ca.saturation.Override(L.saturation);
            wb.temperature.Override(L.temp);
            wb.tint.Override(L.tint);
            bloom.active = L.bloom > 0f;
            bloom.intensity.Override(L.bloom);
            bloom.threshold.Override(L.bloomTh);
            vg.intensity.Override(L.vignette);
        }

        // ---------- эксперименты: свет сцены и материалы (итог переносится в код сцены) ----------
        static bool litCaptured;
        static Color oSky, oEquator, oGround, oSunColor;
        static float oSunIntensity, oShadowStrength;
        static readonly Dictionary<Material, float> oSmooth = new Dictionary<Material, float>();
        static ReflectionProbe probe;

        static void CaptureLight()
        {
            if (litCaptured) return;
            litCaptured = true;
            oSky = RenderSettings.ambientSkyColor; oEquator = RenderSettings.ambientEquatorColor; oGround = RenderSettings.ambientGroundColor;
            var sun = RenderSettings.sun;
            if (sun != null) { oSunIntensity = sun.intensity; oSunColor = sun.color; oShadowStrength = sun.shadowStrength; }
        }

        static void RestoreExperiments()
        {
            if (litCaptured)
            {
                RenderSettings.ambientSkyColor = oSky; RenderSettings.ambientEquatorColor = oEquator; RenderSettings.ambientGroundColor = oGround;
                var sun = RenderSettings.sun;
                if (sun != null) { sun.intensity = oSunIntensity; sun.color = oSunColor; sun.shadowStrength = oShadowStrength; }
                litCaptured = false;
            }
            foreach (var kv in oSmooth) if (kv.Key != null) kv.Key.SetFloat("_Smoothness", kv.Value);
            oSmooth.Clear();
            if (probe != null) { Destroy(probe.gameObject); probe = null; }
        }

        static float F(string v) { return float.Parse(v, CultureInfo.InvariantCulture); }

        static IEnumerable<Material> SceneMaterials()
        {
            var seen = new HashSet<Material>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m != null && seen.Add(m)) yield return m;
        }

        // null — ключ не про «картинку»
        static string TweakLook(string key, string val)
        {
            switch (key)
            {
                case "tonemap": L.tonemap = val == "aces" ? 2 : val == "none" ? 0 : 1; break;
                case "exposure": L.exposure = F(val); break;
                case "contrast": L.contrast = F(val); break;
                case "saturation": L.saturation = F(val); break;
                case "temp": L.temp = F(val); break;
                case "tint": L.tint = F(val); break;
                case "bloom": L.bloom = F(val); break;
                case "bloomth": L.bloomTh = F(val); break;
                case "vignette": L.vignette = F(val); break;
                case "aoint": L.aoIntensity = F(val); ApplyPipeline(); return key + "=" + val;
                case "aorad": L.aoRadius = F(val); ApplyPipeline(); return key + "=" + val;
                case "aodirect": L.aoDirect = F(val); ApplyPipeline(); return key + "=" + val;
                case "ambient":
                {
                    CaptureLight(); float k = F(val);
                    RenderSettings.ambientSkyColor = oSky * k; RenderSettings.ambientEquatorColor = oEquator * k; RenderSettings.ambientGroundColor = oGround * k;
                    return key + "=" + val;
                }
                case "sun": { CaptureLight(); if (RenderSettings.sun != null) RenderSettings.sun.intensity = F(val); return key + "=" + val; }
                case "suncol": { CaptureLight(); Color c; if (RenderSettings.sun != null && ColorUtility.TryParseHtmlString("#" + val, out c)) RenderSettings.sun.color = c; return key + "=" + val; }
                case "shadowstr": { CaptureLight(); if (RenderSettings.sun != null) RenderSettings.sun.shadowStrength = F(val); return key + "=" + val; }
                case "smooth":
                {
                    // smooth=floor:0.4 — всем материалам сцены, в имени которых есть «floor»; smooth=*:0.2 — всем
                    int c = val.LastIndexOf(':'); if (c <= 0) return key + ": нужно имя:значение";
                    string part = val.Substring(0, c).ToLowerInvariant(); float s = F(val.Substring(c + 1)); int n = 0;
                    foreach (var m in SceneMaterials())
                    {
                        if (!m.HasProperty("_Smoothness") || (part != "*" && !m.name.ToLowerInvariant().Contains(part))) continue;
                        if (!oSmooth.ContainsKey(m)) oSmooth[m] = m.GetFloat("_Smoothness");
                        m.SetFloat("_Smoothness", s); n++;
                    }
                    return key + "=" + val + " (" + n + " материалов)";
                }
                case "mats":
                {
                    var names = SceneMaterials().Select(m => m.name + (m.HasProperty("_Smoothness") ? " s=" + m.GetFloat("_Smoothness").ToString("0.##", CultureInfo.InvariantCulture) : "")).OrderBy(x => x).ToList();
                    return "материалы (" + names.Count + "): " + string.Join(", ", names.ToArray());
                }
                case "probe":
                {
                    if (probe != null) { Destroy(probe.gameObject); probe = null; }
                    if (val == "0" || val == "off") return "probe=выкл";
                    // probe=cx,cy,cz,sx,sy,sz — отражения комнаты для глянцевых поверхностей (снимается один раз)
                    var v = val.Split(',').Select(F).ToArray();
                    var go = new GameObject("LookProbe");
                    go.transform.position = v.Length >= 3 ? new Vector3(v[0], v[1], v[2]) : new Vector3(0f, 1.5f, 0.5f);
                    probe = go.AddComponent<ReflectionProbe>();
                    probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                    probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; probe.resolution = 256; probe.boxProjection = true;
                    probe.size = v.Length >= 6 ? new Vector3(v[3], v[4], v[5]) : new Vector3(22f, 3.4f, 16f);
                    probe.RenderProbe();
                    return "probe=" + go.transform.position + " " + probe.size;
                }
                default: return null;
            }
            if (inst != null) inst.ApplyLook();
            return key + "=" + val;
        }
    }
}
