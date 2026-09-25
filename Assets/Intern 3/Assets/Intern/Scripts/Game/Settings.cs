// Настройки игры: экран, графика, звук, управление, интерфейс. Хранятся в PlayerPrefs отдельно от сохранения.
// Графика конвейера URP применяется через Intern.Look.UrpLook (другая сборка — вызываем по имени).
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Intern.Game
{
    [Serializable]
    public class GameSettings
    {
        // экран
        public int resW, resH;              // 0 — родное разрешение монитора
        public int windowMode = 1;          // 0 полный экран, 1 окно без рамки, 2 в окне
        public bool vsync = true;
        public int fps = 1;                 // индекс в GameConfig.FpsValues
        public float brightness = 1f;       // 0.5 … 1.5
        // графика
        public int quality = 2;             // 0 низкое … 3 ультра, 4 своё
        public float renderScale = 1f;
        public int aa = 3;                  // см. GameConfig.AaNames
        public int shadows = 2;             // 0 выкл, 1 средние, 2 высокие
        public bool post = true;
        public bool screenAnim = true;      // прокрутка кода на мониторах коллег
        // звук
        public float master = 0.8f, music = 0.6f, sfx = 0.8f, uiSound = 0.7f;
        // управление
        public float sensitivity = 1f;
        public bool invertY;
        public float fov = 60f;
        // интерфейс
        public bool keyHints = true;
        public float uiScale = 1f;
        public bool showFps;
        // игра
        public int dayLength = 1;           // 0 короткий, 1 обычный, 2 длинный (DayLength)
        public bool blood = true;           // кровь на обеде
    }

    public static class GameConfig
    {
        const string Key = "intern_settings_v1";
        public static GameSettings S = new GameSettings();
        public static event Action Changed;

        public static readonly string[] WindowModes = { "Полный экран", "Окно без рамки", "В окне" };
        public static readonly int[] FpsValues = { 30, 60, 120, 144, 240, -1 };
        public static readonly string[] FpsNames = { "30", "60", "120", "144", "240", "Без ограничений" };
        public static readonly string[] QualityNames = { "Низкое", "Среднее", "Высокое", "Ультра", "Своё" };
        public static readonly string[] AaNames = { "Выключено", "FXAA — быстрое", "SMAA — чёткое", "MSAA 4× + SMAA", "MSAA 8× + SMAA" };
        public static readonly string[] ShadowNames = { "Выключены", "Средние", "Высокие" };

        public static void Load()
        {
            try
            {
                string j = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(j)) S = JsonUtility.FromJson<GameSettings>(j) ?? new GameSettings();
            }
            catch { S = new GameSettings(); }
            S.fps = Mathf.Clamp(S.fps, 0, FpsValues.Length - 1);
            S.aa = Mathf.Clamp(S.aa, 0, AaNames.Length - 1);
            S.quality = Mathf.Clamp(S.quality, 0, QualityNames.Length - 1);
            S.dayLength = Mathf.Clamp(S.dayLength, 0, 2);
        }

        public static void Save() { PlayerPrefs.SetString(Key, JsonUtility.ToJson(S)); PlayerPrefs.Save(); }

        // Изменили что-то в меню настроек: применить и запомнить
        public static void Commit() { ApplyAll(); Save(); }

        public static void ResetAll() { S = new GameSettings(); Commit(); }

        // Предустановки качества графики: масштаб рендера, сглаживание, тени, пост-обработка
        static readonly float[] PresetScale = { 0.75f, 1f, 1f, 1.25f };
        static readonly int[] PresetAa = { 1, 2, 3, 4 }, PresetShadows = { 0, 1, 2, 2 };
        static readonly bool[] PresetPost = { false, true, true, true };

        public static void SetQuality(int q)
        {
            S.quality = q;
            if (q < 0 || q > 3) return;
            S.renderScale = PresetScale[q]; S.aa = PresetAa[q]; S.shadows = PresetShadows[q]; S.post = PresetPost[q];
        }

        // Поменяли отдельный параметр графики: если совпало с предустановкой — показываем её, иначе «своё»
        public static void GraphicsTouched()
        {
            S.quality = 4;
            for (int q = 0; q < 4; q++)
                if (Mathf.Approximately(S.renderScale, PresetScale[q]) && S.aa == PresetAa[q] && S.shadows == PresetShadows[q] && S.post == PresetPost[q]) { S.quality = q; return; }
        }

        // Разрешения монитора без повторов (частоты обновления не различаем), от большего к меньшему
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v)) list.Add(v);
            }
            var cur = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (!list.Contains(cur)) list.Add(cur);
            list.Sort((a, b) => b.x != a.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));
            return list;
        }

        public static Vector2Int CurrentRes
        {
            get { return S.resW > 0 && S.resH > 0 ? new Vector2Int(S.resW, S.resH) : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height); }
        }

        public static void ApplyAll()
        {
            ApplyDisplay(); ApplyGraphics(); ApplyAudio(); ApplyControls();
            if (Changed != null) Changed();
        }

        public static void ApplyDisplay()
        {
            var mode = S.windowMode == 0 ? FullScreenMode.ExclusiveFullScreen : S.windowMode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var res = CurrentRes;
#if !UNITY_EDITOR
            if (Screen.width != res.x || Screen.height != res.y || Screen.fullScreenMode != mode) Screen.SetResolution(res.x, res.y, mode);
#endif
            QualitySettings.vSyncCount = S.vsync ? 1 : 0;
            Application.targetFrameRate = S.vsync ? -1 : FpsValues[Mathf.Clamp(S.fps, 0, FpsValues.Length - 1)];
        }

        public static void ApplyGraphics()
        {
            float shadowDist = S.shadows == 0 ? 0f : S.shadows == 1 ? 18f : 30f;
            int msaa = S.aa == 3 ? 4 : S.aa == 4 ? 8 : 1;
            int aaMode = S.aa == 0 ? 0 : S.aa == 1 ? 1 : 2;
            float exposure = 0.1f + (S.brightness - 1f) * 1.6f;
            CallUrp("Configure", new object[] { S.renderScale, msaa, aaMode, shadowDist, S.post, exposure });
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.shadows = S.shadows == 0 ? LightShadows.None : S.shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
            ScreenScroller.Animate = S.screenAnim;
        }

        public static void ApplyAudio() { AudioListener.volume = Mathf.Clamp01(S.master); }

        public static void ApplyControls()
        {
            InputX.LookScale = Mathf.Clamp(S.sensitivity, 0.1f, 4f);
            InputX.InvertY = S.invertY;
            PlayerController.FovThird = Mathf.Clamp(S.fov, 45f, 95f);
            PlayerController.FovFirst = PlayerController.FovThird + 12f;
        }

        // Эффективная громкость категории (для будущих звуков)
        public static float Volume(string kind)
        {
            float k = kind == "music" ? S.music : kind == "ui" ? S.uiSound : S.sfx;
            return Mathf.Clamp01(S.master * k);
        }

        static Type urpLook; static bool urpSearched;
        static void CallUrp(string method, object[] args)
        {
            if (!urpSearched)
            {
                urpSearched = true;
                urpLook = Type.GetType("Intern.Look.UrpLook, Intern.URP");
            }
            if (urpLook == null) return;
            try
            {
                var m = urpLook.GetMethod(method, BindingFlags.Public | BindingFlags.Static);
                if (m != null) m.Invoke(null, args);
            }
            catch (Exception e) { Debug.LogWarning("[Стажёр] Настройки графики URP не применились: " + e.Message); }
        }
    }
}
