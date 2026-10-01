// Руки от первого лица (спринт 8 версии 0.9 «Оружие в руках»). Камера от первого лица висит на уровне глаз,
// а плечи у мультяшного стажёра на 40 см ниже — свои руки тела дотягиваются до ствола в кадре только задранными вверх.
// Поэтому, как в шутерах, от первого лица руки — отдельные: копия скелета персонажа, в сетках которой оставлены
// только треугольники рук (по весам костей: плечо, локоть, кисть, пальцы), в той же одежде. Копия висит у камеры,
// плечи — чуть ниже и позади неё, кисти ставит тот же IK.
// Доводка (D-01): руки тела больше не сжимаются в плечо. Сетка тела делится на «руки» и «остальное»; для камеры игрока
// руки тела — только тень, для остальных камер тело целое и само держит оружие, а руки у камеры не рисуются (FpView).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public class FpArms
    {
        public Transform root;
        Transform clR, clL, upR, upL, foR, foL, haR, haL;
        readonly Transform[] fR = new Transform[15], fL = new Transform[15];
        Transform[] reset;
        float signR, signL;
        CharacterAnim body;
        bool shown;
        string key;
        Renderer[] fpRenderers = new Renderer[0];
        // руки тела: отдельные сетки с треугольниками рук рядом с сетками тела
        class BodySplit { public SkinnedMeshRenderer src, arms; public Mesh full, rest; }
        readonly List<BodySplit> splits = new List<BodySplit>();
        static readonly Dictionary<Mesh, KeyValuePair<Mesh, Mesh>> splitCache = new Dictionary<Mesh, KeyValuePair<Mesh, Mesh>>();
        Transform srcRig;
        public bool Fits(CharacterAnim av) { return root != null && av == body && av != null && av.rig == srcRig; }
        public bool Shown { get { return shown; } }
        public Vector3 ShoulderR { get { return upR.position; } }
        public Transform HandR { get { return haR; } }
        public Transform HandL { get { return haL; } }

        static readonly Dictionary<Mesh, Mesh> cut = new Dictionary<Mesh, Mesh>();
        static readonly HashSet<string> ArmBones = new HashSet<string>();

        static bool IsArm(string n)
        {
            if (ArmBones.Count == 0)
                foreach (var sd in new[] { "L", "R" })
                {
                    foreach (var b in new[] { "Shoulder", "ElbowHelp", "Elbow", "Hand" }) ArmBones.Add(b + sd);
                    foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Pinky" }) for (int i = 1; i <= 3; i++) ArmBones.Add(f + i + sd);
                }
            n = ModelLib.Clean(n);
            if (n.EndsWith("_bind")) n = n.Substring(0, n.Length - 5);
            return ArmBones.Contains(n);
        }

        // Сетка, где остались только треугольники рук (все три вершины — больше чем наполовину на костях рук)
        static Mesh ArmsOnly(SkinnedMeshRenderer smr)
        {
            var src = smr.sharedMesh;
            if (src == null || !src.isReadable) return null;
            Mesh m;
            if (cut.TryGetValue(src, out m)) return m;
            m = Subset(smr, src, true, "_fparms");
            cut[src] = m;
            return m;
        }

        // Часть сетки: arms — только треугольники рук, иначе — всё, кроме них (null, если частей нет)
        static Mesh Subset(SkinnedMeshRenderer smr, Mesh src, bool arms, string suffix)
        {
            if (src == null || !src.isReadable) return null;
            Mesh m;
            var bones = smr.bones;
            var arm = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) arm[i] = bones[i] != null && IsArm(bones[i].name);
            var bw = src.boneWeights;
            if (bw == null || bw.Length != src.vertexCount) return null;
            var aw = new float[bw.Length];
            for (int v = 0; v < bw.Length; v++)
            {
                var w = bw[v]; float a = 0f;
                if (w.boneIndex0 < arm.Length && arm[w.boneIndex0]) a += w.weight0;
                if (w.boneIndex1 < arm.Length && arm[w.boneIndex1]) a += w.weight1;
                if (w.boneIndex2 < arm.Length && arm[w.boneIndex2]) a += w.weight2;
                if (w.boneIndex3 < arm.Length && arm[w.boneIndex3]) a += w.weight3;
                aw[v] = a;
            }
            m = Object.Instantiate(src); m.name = src.name + suffix;
            int kept = 0;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tri = src.GetTriangles(sm);
                var keep = new List<int>(tri.Length / 4);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    bool isArm = aw[tri[i]] >= 0.5f && aw[tri[i + 1]] >= 0.5f && aw[tri[i + 2]] >= 0.5f;
                    if (isArm == arms) { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); }
                }
                m.SetTriangles(keep, sm);
                kept += keep.Count / 3;
            }
            if (kept == 0) { Object.Destroy(m); m = null; }
            return m;
        }

        // Сетка тела → (руки, остальное); один раз на каждую сетку (и на каждую гладкость)
        static bool Split(SkinnedMeshRenderer smr, Mesh full, out Mesh arms, out Mesh rest)
        {
            KeyValuePair<Mesh, Mesh> kv;
            if (!splitCache.TryGetValue(full, out kv))
            {
                var a = Subset(smr, full, true, "_bodyarms");
                var r = a != null ? Subset(smr, full, false, "_noarms") : null;
                kv = new KeyValuePair<Mesh, Mesh>(a, r);
                splitCache[full] = kv;
            }
            arms = kv.Key; rest = kv.Value;
            return arms != null && rest != null;
        }

        public static FpArms Build(CharacterAnim av, Transform parent)
        {
            if (av == null || av.rig == null || !av.v4) return null;
            var copy = Object.Instantiate(av.rig.gameObject, parent, false);
            copy.name = "FpArms";
            // только кости и сетки: без физики, скриптов и всего, что не руки
            foreach (var j in copy.GetComponentsInChildren<Joint>(true)) Object.Destroy(j);
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            // руки тела и тени-двойники (FpView), если успели появиться на скелете, — не копируем
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                if (t != null && t != copy.transform && (t.name.EndsWith("_Arms") || t.name.EndsWith("_Shadow"))) { t.gameObject.SetActive(false); Object.Destroy(t.gameObject); }
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(mb);
            foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
            {
                var smr = r as SkinnedMeshRenderer;
                Mesh arms = smr != null ? ArmsOnly(smr) : null;
                if (arms == null) { r.enabled = false; continue; }
                smr.sharedMesh = arms;
                smr.shadowCastingMode = ShadowCastingMode.Off; smr.receiveShadows = true;
                smr.updateWhenOffscreen = false;
            }
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = FpView.MainOnlyLayer;   // только камера игрока
            var a = new FpArms { root = copy.transform, body = av, srcRig = av.rig };
            System.Func<string, Transform> F = n => ModelLib.Find(copy.transform, n);
            a.clR = F("ClavicleR"); a.clL = F("ClavicleL"); a.upR = F("ShoulderR"); a.upL = F("ShoulderL");
            a.foR = F("ElbowR"); a.foL = F("ElbowL"); a.haR = F("HandR"); a.haL = F("HandL");
            if (a.upR == null || a.upL == null || a.foR == null || a.foL == null || a.haR == null || a.haL == null) { Object.Destroy(copy); return null; }
            string[] fn = { "Index", "Middle", "Ring", "Pinky", "Thumb" };
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++) { a.fR[f * 3 + j] = F(fn[f] + (j + 1) + "R"); a.fL[f * 3 + j] = F(fn[f] + (j + 1) + "L"); }
            // поза покоя: все кости скелета v4 — без поворота
            var list = new List<Transform>();
            foreach (var n in AnimLib.Bones) { var t = F(n); if (t != null) list.Add(t); }
            a.reset = list.ToArray();
            foreach (var t in a.reset) t.localRotation = Quaternion.identity;
            a.signR = ArmSolver.ElbowSign(a.haR); a.signL = ArmSolver.ElbowSign(a.haL);
            JointHelpers.Attach(copy, copy.transform);
            a.key = "fparms:" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a);
            a.fpRenderers = copy.GetComponentsInChildren<Renderer>(true);
            // руки тела — своими сетками рядом с сетками тела (пока не нужны — выключены)
            foreach (var smr in av.rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                Mesh am, rm;
                if (!Split(smr, smr.sharedMesh, out am, out rm)) continue;
                // дочерний объект сетки тела: выключенный костюм выключает и его руки
                var go = new GameObject(smr.name + "_Arms"); go.layer = smr.gameObject.layer;
                go.transform.SetParent(smr.transform, false);
                var ar = go.AddComponent<SkinnedMeshRenderer>();
                ar.bones = smr.bones; ar.rootBone = smr.rootBone; ar.sharedMesh = am; ar.sharedMaterials = smr.sharedMaterials;
                ar.localBounds = smr.localBounds; ar.quality = smr.quality; ar.updateWhenOffscreen = smr.updateWhenOffscreen;
                ar.shadowCastingMode = smr.shadowCastingMode; ar.receiveShadows = smr.receiveShadows;
                ar.enabled = false;
                a.splits.Add(new BodySplit { src = smr, arms = ar, full = smr.sharedMesh, rest = rm });
            }
            copy.SetActive(false);
            return a;
        }

        // Показать руки у камеры (и спрятать руки тела) или вернуть как было
        public void Show(bool on)
        {
            if (root == null) return;
            if (on && !root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (on) KeepSplit();
            if (on == shown) return;
            shown = on;
            root.gameObject.SetActive(on);
            var bodyArms = new List<Renderer>();
            foreach (var sp in splits)
            {
                if (sp.src == null || sp.arms == null) continue;
                if (on) { sp.src.sharedMesh = sp.rest; sp.arms.enabled = true; bodyArms.Add(sp.arms); }
                else { if (sp.src.sharedMesh == sp.rest) sp.src.sharedMesh = sp.full; sp.arms.enabled = false; }
            }
            FpView.HideForMain(key, bodyArms, on);
        }

        // Качество моделей могло смениться (сетка тела заменена гладкой) — делим заново
        void KeepSplit()
        {
            if (!shown) return;
            foreach (var sp in splits)
            {
                if (sp.src == null || sp.arms == null || sp.src.sharedMesh == sp.rest || sp.src.sharedMesh == null) continue;
                Mesh am, rm;
                sp.full = sp.src.sharedMesh;
                if (Split(sp.src, sp.full, out am, out rm)) { sp.rest = rm; sp.arms.sharedMesh = am; sp.src.sharedMesh = rm; }
            }
        }

        // Для самопроверки: сколько сеток тела разделено на руки и остальное
        public int DevSplits { get { int n = 0; foreach (var sp in splits) if (sp.src != null && sp.arms != null && sp.rest != null) n++; return n; } }

        // В оптике: камера игрока не видит ни руки тела, ни свои у камеры (тело со стороны целое)
        public void Conceal()
        {
            Show(true);
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        // Середина плечевых суставов — в точке mid (оси камеры), корпус повёрнут на twist (левое плечо вперёд)
        public void Place(Transform cam, Vector3 mid, float twist)
        {
            if (root == null) return;
            foreach (var t in reset) if (t != null) t.localRotation = Quaternion.identity;
            root.rotation = cam.rotation * Quaternion.Euler(0f, twist, 0f);
            Vector3 now = (upR.position + upL.position) * 0.5f;
            root.position += cam.TransformPoint(mid) - now;
        }

        public void IK(bool right, Vector3 target, Quaternion handRot, Vector3 pole, float twist = 0.5f)
        {
            if (root == null) return;
            if (right) ArmSolver.Solve(clR, upR, foR, haR, CharacterAnim.PalmR, signR, target, handRot, pole, twist, 18f);
            else ArmSolver.Solve(clL, upL, foL, haL, CharacterAnim.PalmL, signL, target, handRot, pole, twist, 18f);
        }

        public Transform[] FingerBones(bool right) { return right ? fR : fL; }

        public void Fingers(bool right, float curl, float index, float thumb, float w = 1f)
        {
            ArmSolver.Fingers(right ? fR : fL, right, curl, index, thumb, w);
        }

        public void Destroy()
        {
            Show(false);
            foreach (var sp in splits) if (sp.arms != null) Object.Destroy(sp.arms.gameObject);
            splits.Clear();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
