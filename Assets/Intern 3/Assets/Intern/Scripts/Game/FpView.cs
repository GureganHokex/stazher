// Вид от первого лица без «дыр» со стороны (доводка версии 0.9, D-01). Камера игрока не должна видеть свою голову
// изнутри и руки тела (вместо них — руки у камеры, FpArms). Раньше голова выключалась, а руки тела сжимались в плечо —
// со стороны (окна проверки, тень) был безголовый человек без рук и руки, висящие у камеры в воздухе.
// Теперь видимость решается для каждой камеры:
//   голова и руки тела переходят на слой «не для камеры игрока» (камера игрока его не рисует), а их тень отбрасывает
//   невидимый двойник на прежнем слое — тень целая;
//   руки у камеры рисуются только камерой игрока (переключение перед отрисовкой каждой камеры).
// Слой, а не выключение перед отрисовкой: обычные сетки рисует GPU Resident Drawer, ему такие переключения не видны.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public static class FpView
    {
        public const int OutsideLayer = 30;   // «не для камеры игрока»
        public const int MainOnlyLayer = 29;  // «только для камеры игрока»: руки у камеры и оружие в них
        public static int MainMask { get { return ~(1 << OutsideLayer); } }
        public static int OtherMask { get { return ~(1 << MainOnlyLayer); } }   // для всех прочих камер

        static Camera main;
        public static Camera Main
        {
            get { return main; }
            set { main = value; if (main != null) main.cullingMask &= MainMask; }
        }

        class Hid { public Renderer r; public int layer; public GameObject twin; }
        static readonly Dictionary<string, Hid[]> hidden = new Dictionary<string, Hid[]>();
        static readonly Dictionary<string, Renderer[]> mainOnly = new Dictionary<string, Renderer[]>();
        static bool hooked;

        // Камера игрока этих рендереров не видит; тень от них остаётся
        public static void HideForMain(string key, IList<Renderer> rs, bool on)
        {
            Hid[] old;
            if (hidden.TryGetValue(key, out old))
            {
                foreach (var h in old)
                {
                    if (h.r != null) h.r.gameObject.layer = h.layer;
                    if (h.twin != null) Object.Destroy(h.twin);
                }
                hidden.Remove(key);
            }
            if (!on || rs == null) return;
            var a = new List<Hid>();
            foreach (var r in rs)
            {
                if (r == null || r.gameObject.layer == OutsideLayer) continue;
                var h = new Hid { r = r, layer = r.gameObject.layer };
                h.twin = ShadowTwin(r, h.layer);
                r.gameObject.layer = OutsideLayer;
                a.Add(h);
            }
            hidden[key] = a.ToArray();
        }

        // Невидимый двойник, который только отбрасывает тень (на прежнем слое)
        static GameObject ShadowTwin(Renderer r, int layer)
        {
            if (r.shadowCastingMode == ShadowCastingMode.Off) return null;
            var go = new GameObject(r.name + "_Shadow"); go.layer = layer;
            go.transform.SetParent(r.transform, false);
            var smr = r as SkinnedMeshRenderer;
            Renderer t;
            if (smr != null)
            {
                if (smr.sharedMesh == null) { Object.Destroy(go); return null; }
                var s = go.AddComponent<SkinnedMeshRenderer>();
                s.bones = smr.bones; s.rootBone = smr.rootBone; s.sharedMesh = smr.sharedMesh;
                s.localBounds = smr.localBounds; s.quality = smr.quality; s.updateWhenOffscreen = smr.updateWhenOffscreen;
                t = s;
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) { Object.Destroy(go); return null; }
                go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                t = go.AddComponent<MeshRenderer>();
            }
            t.sharedMaterials = r.sharedMaterials;
            t.shadowCastingMode = ShadowCastingMode.ShadowsOnly; t.receiveShadows = false;
            return go;
        }

        // Оружие у камеры: только для камеры игрока и без тени (тень даёт двойник у тела)
        class Own { public Renderer r; public int layer; public ShadowCastingMode mode; }
        static readonly Dictionary<string, Own[]> own = new Dictionary<string, Own[]>();
        public static void KeepForMain(string key, GameObject root, bool on)
        {
            Own[] old;
            if (own.TryGetValue(key, out old))
            {
                foreach (var o in old) if (o.r != null) { o.r.gameObject.layer = o.layer; o.r.shadowCastingMode = o.mode; }
                own.Remove(key);
            }
            if (!on || root == null) return;
            var a = new List<Own>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.gameObject.layer == MainOnlyLayer) continue;
                a.Add(new Own { r = r, layer = r.gameObject.layer, mode = r.shadowCastingMode });
                r.gameObject.layer = MainOnlyLayer; r.shadowCastingMode = ShadowCastingMode.Off;
            }
            own[key] = a.ToArray();
        }

        // Только для камеры игрока (руки у камеры — это скин-сетки, их можно выключать перед каждой камерой)
        public static void MainOnly(string key, IList<Renderer> rs, bool on)
        {
            if (!hooked)
            {
                hooked = true;
                RenderPipelineManager.beginCameraRendering += (ctx, cam) => Apply(cam != null && cam == main);
                RenderPipelineManager.endCameraRendering += (ctx, cam) => Apply(true);
            }
            Renderer[] old;
            if (mainOnly.TryGetValue(key, out old)) { foreach (var r in old) if (r != null) r.forceRenderingOff = false; mainOnly.Remove(key); }
            if (!on || rs == null) return;
            var a = new List<Renderer>();
            foreach (var r in rs) if (r != null) a.Add(r);
            mainOnly[key] = a.ToArray();
        }

        static void Apply(bool isMain)
        {
            foreach (var set in mainOnly.Values)
                foreach (var r in set) if (r != null) r.forceRenderingOff = !isMain;
        }

        // Для самопроверки: сколько рендереров в наборе на слое «не для камеры игрока» и сколько у них теней-двойников
        public static void DevState(string key, out int hiddenCount, out int twins)
        {
            hiddenCount = twins = 0;
            Hid[] set;
            if (!hidden.TryGetValue(key, out set)) return;
            foreach (var h in set)
            {
                if (h.r != null && h.r.gameObject.layer == OutsideLayer) hiddenCount++;
                if (h.twin != null) twins++;
            }
        }
    }
}
