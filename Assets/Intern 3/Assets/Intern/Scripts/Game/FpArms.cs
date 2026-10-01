// Руки от первого лица (спринт 8 версии 0.9 «Оружие в руках»). Камера от первого лица висит на уровне глаз,
// а плечи у мультяшного стажёра на 40 см ниже — свои руки тела дотягиваются до ствола в кадре только задранными вверх.
// Поэтому, как в шутерах, от первого лица руки — отдельные: копия скелета персонажа, в сетках которой оставлены
// только треугольники рук (по весам костей: плечо, локоть, кисть, пальцы), в той же одежде. Копия висит у камеры,
// плечи — чуть ниже и позади неё, кисти ставит тот же IK. Руки самого тела в это время сжаты в плечо (не мешают в кадре).
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
        CharacterAnim body; Transform bodyUpR, bodyUpL; Vector3 bodyScaleR, bodyScaleL;
        bool shown;
        Transform srcRig;
        public bool Fits(CharacterAnim av) { return root != null && av == body && av != null && av.rig == srcRig; }
        public bool Shown { get { return shown; } }

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
            var bones = smr.bones;
            var arm = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) arm[i] = bones[i] != null && IsArm(bones[i].name);
            var bw = src.boneWeights;
            if (bw == null || bw.Length != src.vertexCount) { cut[src] = null; return null; }
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
            m = Object.Instantiate(src); m.name = src.name + "_fparms";
            int kept = 0;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                var tri = src.GetTriangles(sm);
                var keep = new List<int>(tri.Length / 4);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                    if (aw[tri[i]] >= 0.5f && aw[tri[i + 1]] >= 0.5f && aw[tri[i + 2]] >= 0.5f) { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); }
                m.SetTriangles(keep, sm);
                kept += keep.Count / 3;
            }
            if (kept == 0) { Object.Destroy(m); m = null; }
            cut[src] = m;
            return m;
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
            foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
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
            a.bodyUpR = av.armR; a.bodyUpL = av.armL;
            a.bodyScaleR = av.armR.localScale; a.bodyScaleL = av.armL.localScale;
            copy.SetActive(false);
            return a;
        }

        // Показать руки у камеры (и спрятать руки тела) или вернуть как было
        public void Show(bool on)
        {
            if (root == null || on == shown) return;
            shown = on;
            root.gameObject.SetActive(on);
            if (bodyUpR != null) bodyUpR.localScale = on ? Vector3.one * 0.001f : bodyScaleR;
            if (bodyUpL != null) bodyUpL.localScale = on ? Vector3.one * 0.001f : bodyScaleL;
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

        public void Fingers(bool right, float curl, float index, float thumb, float w = 1f)
        {
            ArmSolver.Fingers(right ? fR : fL, right, curl, index, thumb, w);
        }

        public void Destroy()
        {
            Show(false);
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
