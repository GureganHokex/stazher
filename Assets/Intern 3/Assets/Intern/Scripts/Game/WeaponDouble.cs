// Двойник оружия для вида со стороны при игре от первого лица (доводка версии 0.9, D-01). От первого лица ствол стоит
// у камеры, в руках у камеры, — со стороны это выглядело бы как оружие, висящее перед лицом. Поэтому со стороны и в тени
// тело держит свою копию оружия так же, как от третьего лица, а оригинал у камеры видит только камера игрока.
// Подвижные детали (затвор, магазин, спуск, цевьё) повторяют оригинал каждый кадр.
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public class WeaponDouble
    {
        public WeaponModel Src;
        public Transform Root;
        readonly List<Transform> a = new List<Transform>(), b = new List<Transform>();
        readonly Dictionary<Transform, Transform> map = new Dictionary<Transform, Transform>();
        string key;
        static readonly HashSet<string> Fx = new HashSet<string> { "MuzzleFlash", "TrailBase", "WeaponTrail", "Tracer", "ShellInHand" };

        public static WeaponDouble For(WeaponModel w)
        {
            if (w == null) return null;
            var go = Object.Instantiate(w.gameObject);
            go.name = w.gameObject.name + "_Outside";
            go.transform.localScale = w.transform.lossyScale;
            var d = new WeaponDouble { Src = w, Root = go.transform };
            d.Pair(w.transform, go.transform);
            // только сетки: без скриптов, физики, вспышек и следов
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) if (ps != null) Object.DestroyImmediate(ps.gameObject);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != null && t != go.transform && Fx.Contains(t.name)) Object.DestroyImmediate(t.gameObject);
            for (int i = d.b.Count - 1; i >= 0; i--) if (d.b[i] == null) { d.a.RemoveAt(i); d.b.RemoveAt(i); }
            d.key = "double:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(d);
            var rs = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) rs.Add(r);
            FpView.HideForMain(d.key, rs, true);
            return d;
        }

        void Pair(Transform s, Transform c)
        {
            if (s != Src.transform) { a.Add(s); b.Add(c); }
            map[s] = c;
            int n = Mathf.Min(s.childCount, c.childCount);
            for (int i = 0; i < n; i++) Pair(s.GetChild(i), c.GetChild(i));
        }

        // Точка оригинала → та же точка двойника
        public Transform Of(Transform t) { Transform r; return t != null && map.TryGetValue(t, out r) && r != null ? r : null; }

        public void Sync()
        {
            for (int i = 0; i < a.Count; i++)
            {
                var s = a[i]; var c = b[i];
                if (s == null || c == null) continue;
                c.localPosition = s.localPosition; c.localRotation = s.localRotation; c.localScale = s.localScale;
                if (c.gameObject.activeSelf != s.gameObject.activeSelf) c.gameObject.SetActive(s.gameObject.activeSelf);
            }
        }

        public void Destroy()
        {
            FpView.HideForMain(key, null, false);
            if (Root != null) Object.Destroy(Root.gameObject);
            Root = null;
        }
    }
}
