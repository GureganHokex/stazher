// Руки на оружии (спринт 8 версии 0.9 «Оружие в руках»): IK из двух костей для рук (плечо — локоть — кисть),
// пальцы обхватывают рукоять и цевьё, корпус разворачивается в стойку стрелка, голова смотрит на цель.
// Анимация кадра уже записана в кости (UpdateV4) — PlayerCombat после неё ставит оружие от тела
// и вызывает ArmIK для обеих кистей. Только для скелета v4.
using UnityEngine;

namespace Intern.Game
{
    public partial class CharacterAnim
    {
        // середина кулака левой руки в осях кисти — зеркало PalmR (ладонь смотрит внутрь, +X)
        public static readonly Vector3 PalmL = new Vector3(0.036f, -0.088f, 0.008f);
        Transform clavR, clavL, armsFor;
        int[] fingersL;
        float elbowSign = -1f;

        bool ArmsReady()
        {
            if (!v4 || armR == null || armL == null || elbowR == null || elbowL == null || handR == null || handL == null) return false;
            if (armsFor == handR) return true;
            armsFor = handR;
            clavR = F("ClavicleR"); clavL = F("ClavicleL");
            fingersL = new int[15];
            string[] fn = { "Index", "Middle", "Ring", "Pinky", "Thumb" };
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++) fingersL[f * 3 + j] = Idx(fn[f] + (j + 1) + "L");
            elbowSign = ArmSolver.ElbowSign(handR);
            return true;
        }

        // Повороты кисти в осях оружия: f — куда смотрят пальцы (от запястья к костяшкам), n — куда смотрит ладонь
        public static Quaternion HandRot(bool right, Vector3 f, Vector3 n)
        {
            f.Normalize(); n = Vector3.ProjectOnPlane(n, f).normalized;
            return right ? Quaternion.LookRotation(Vector3.Cross(n, f), -f) : Quaternion.LookRotation(Vector3.Cross(f, n), -f);
        }

        // Кисть: точка кулака (PalmR/PalmL) — в target, поворот кисти — handRot; локоть уходит в сторону pole.
        // w — вес (0 — как в анимации), twist — доля закрутки кисти, которую берёт на себя предплечье.
        public void ArmIK(bool right, Vector3 target, Quaternion handRot, Vector3 pole, float w = 1f, float twist = 0.5f)
        {
            if (w <= 0.001f || !ArmsReady()) return;
            Transform up = right ? armR : armL, fo = right ? elbowR : elbowL, ha = right ? handR : handL, cl = right ? clavR : clavL;
            Vector3 palm = right ? PalmR : PalmL;
            if (w < 0.999f)
            {
                target = Vector3.Lerp(ha.TransformPoint(palm), target, w);
                handRot = Quaternion.Slerp(ha.rotation, handRot, w);
            }
            ArmSolver.Solve(cl, up, fo, ha, palm, elbowSign, target, handRot, pole, twist, 24f);
        }

        public Transform[] FingerBones(bool right)
        {
            if (!ArmsReady()) return null;
            int[] f = right ? fingersR : fingersL; if (f == null || vb == null) return null;
            var t = new Transform[15];
            for (int k = 0; k < 15; k++) t[k] = f[k] >= 0 ? vb[f[k]] : null;
            return t;
        }

        // Пальцы: curl — средний, безымянный, мизинец; index — указательный (на спуске меньше); thumb — большой
        public void Fingers(bool right, float curl, float index, float thumb, float w = 1f)
        {
            if (w <= 0.001f || !ArmsReady()) return;
            int[] f = right ? fingersR : fingersL; if (f == null || vb == null) return;
            var t = new Transform[15];
            for (int k = 0; k < 15; k++) t[k] = f[k] >= 0 ? vb[f[k]] : null;
            ArmSolver.Fingers(t, right, curl, index, thumb, w);
        }

        // Стойка стрелка: корпус повёрнут на twist (левое плечо вперёд), голова — на цель.
        // back — отдача корпусом (градусы назад), lean — голова к прикладу (градусы вбок).
        public void AimStance(float twist, Vector3 lookDir, float back, float lean, float w = 1f)
        {
            if (!v4 || w <= 0.001f || iSpine < 0 || iTorso < 0) return;
            Vector3 upW = transform.up;
            Transform s1 = vb[iSpine], s2 = vb[iSpine2], s3 = vb[iTorso];
            Vector3 right = transform.right;
            foreach (var t in new[] { s1, s2, s3 })
            {
                if (t == null) continue;
                t.rotation = Quaternion.AngleAxis(twist * w / 3f, upW) * Quaternion.AngleAxis(-back * w / 3f, right) * t.rotation;
            }
            if (iNeck < 0 || iHead < 0 || vb[iNeck] == null || vb[iHead] == null) return;
            Transform nk = vb[iNeck], hd = vb[iHead];
            Vector3 hf = Vector3.ProjectOnPlane(hd.rotation * Vector3.forward, upW), lf = Vector3.ProjectOnPlane(lookDir, upW);
            if (hf.sqrMagnitude < 1e-6f || lf.sqrMagnitude < 1e-6f) return;
            float yawD = Vector3.SignedAngle(hf, lf, upW) * w;
            nk.rotation = Quaternion.AngleAxis(yawD * 0.45f, upW) * nk.rotation;
            hd.rotation = Quaternion.AngleAxis(yawD * 0.55f, upW) * hd.rotation;
            if (Mathf.Abs(lean) > 0.01f) hd.rotation = Quaternion.AngleAxis(-lean * w, hd.rotation * Vector3.forward) * hd.rotation;
        }
    }

    // IK руки из двух костей (тело и руки от первого лица — FpArms.cs)
    public static class ArmSolver
    {
        // Знак сгиба локтя: в какую сторону поворот вокруг оси X предплечья уводит кисть вперёд
        public static float ElbowSign(Transform hand)
        {
            Vector3 a = hand.localPosition;
            return (Quaternion.AngleAxis(30f, Vector3.right) * a).z > a.z ? 1f : -1f;
        }

        public static void Solve(Transform cl, Transform up, Transform fo, Transform ha, Vector3 palm, float elbowSign,
                                 Vector3 target, Quaternion handRot, Vector3 pole, float twist, float clavMax)
        {
            float s = ha.lossyScale.x;
            Vector3 wrist = target - handRot * (palm * s);
            float la = Vector3.Distance(up.position, fo.position), lb = Vector3.Distance(fo.position, ha.position);
            float reach = la + lb;
            // ключица: тянется к цели, когда рука почти выпрямлена (плечо выходит вперёд)
            if (cl != null && clavMax > 0f)
            {
                float k = Mathf.Clamp01((Vector3.Distance(up.position, wrist) / reach - 0.75f) / 0.25f);
                if (k > 0f)
                {
                    Vector3 from = up.position - cl.position, to = wrist - cl.position;
                    var q = Quaternion.FromToRotation(from, to);
                    float ang; Vector3 ax; q.ToAngleAxis(out ang, out ax);
                    if (ang > 180f) ang -= 360f;
                    cl.rotation = Quaternion.AngleAxis(Mathf.Clamp(ang, -clavMax, clavMax) * k, ax) * cl.rotation;
                }
            }
            Vector3 pa = up.position;
            Vector3 toT = wrist - pa; float d = toT.magnitude;
            float maxL = reach * 0.999f;
            if (d > maxL) { wrist = pa + toT / d * maxL; d = maxL; }
            d = Mathf.Max(d, Mathf.Abs(la - lb) + 0.01f);
            // локоть: сначала сгиб в своей плоскости (ось X плеча), потом точно до нужного угла
            fo.localRotation = Quaternion.AngleAxis(elbowSign * 40f, Vector3.right);
            Vector3 pb = fo.position, pc = ha.position;
            float cur = Vector3.Angle(pa - pb, pc - pb);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(pa - pb, pc - pb);
            if (axis.sqrMagnitude < 1e-8f) axis = up.rotation * Vector3.right;
            axis.Normalize();
            fo.rotation = Quaternion.AngleAxis(want - cur, axis) * fo.rotation;
            // плечо: запястье — в цель, локоть — к pole
            pc = ha.position;
            up.rotation = Quaternion.FromToRotation(pc - pa, wrist - pa) * up.rotation;
            Vector3 ax2 = (wrist - pa).normalized;
            Vector3 el = Vector3.ProjectOnPlane(fo.position - pa, ax2), pl = Vector3.ProjectOnPlane(pole, ax2);
            if (el.sqrMagnitude > 1e-6f && pl.sqrMagnitude > 1e-6f)
                up.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(el, pl, ax2), ax2) * up.rotation;
            // кисть: закрутку делят предплечье и запястье
            Quaternion qh = Quaternion.Inverse(fo.rotation) * handRot;
            Vector3 ba = ha.localPosition.normalized;
            Vector3 v = new Vector3(qh.x, qh.y, qh.z);
            float p = Vector3.Dot(v, ba);
            var tw = new Quaternion(ba.x * p, ba.y * p, ba.z * p, qh.w);
            float m = Mathf.Sqrt(tw.x * tw.x + tw.y * tw.y + tw.z * tw.z + tw.w * tw.w);
            if (m > 1e-5f && twist > 0f)
            {
                tw = new Quaternion(tw.x / m, tw.y / m, tw.z / m, tw.w / m);
                var part = Quaternion.Slerp(Quaternion.identity, tw, twist);
                fo.localRotation = fo.localRotation * part;
                ha.localRotation = Quaternion.Inverse(part) * qh;
            }
            else ha.localRotation = qh;
        }

        // Указательный на спуске: сгиб, при котором подушечка ближе всего к точке спуска (+ дожим при выстреле)
        public static float TriggerIndex(Transform[] f, bool right, Vector3 target, float squeeze)
        {
            if (f == null || f.Length < 3 || f[0] == null || f[1] == null || f[2] == null) return 0f;
            Vector3 ax = right ? Vector3.back : Vector3.forward;
            float best = 0f, bd = float.MaxValue;
            float tip = 0.011f * f[2].lossyScale.y;   // подушечка — около сантиметра за последним суставом
            for (float a = 0f; a <= 95f; a += 2.5f)
            {
                f[0].localRotation = Quaternion.AngleAxis(a * 0.7f, ax); f[1].localRotation = Quaternion.AngleAxis(a, ax); f[2].localRotation = Quaternion.AngleAxis(a * 0.8f, ax);
                Vector3 pad = f[2].position + f[2].rotation * Vector3.down * tip;
                float d = Vector3.Distance(pad, target);
                if (d < bd) { bd = d; best = a; }
            }
            best += squeeze;
            f[0].localRotation = Quaternion.AngleAxis(best * 0.7f, ax); f[1].localRotation = Quaternion.AngleAxis(best, ax); f[2].localRotation = Quaternion.AngleAxis(best * 0.8f, ax);
            return bd;
        }

        // Пальцы (15 костей: указательный, средний, безымянный, мизинец, большой — по три)
        public static void Fingers(Transform[] f, bool right, float curl, float index, float thumb, float w)
        {
            if (f == null) return;
            Vector3 ax = right ? Vector3.back : Vector3.forward;
            Vector3 tax = right ? new Vector3(0.87f, -0.5f, 0f) : new Vector3(0.87f, 0.5f, 0f);
            for (int k = 0; k < 15 && k < f.Length; k++)
            {
                if (f[k] == null) continue;
                bool th = k >= 12;
                float c = th ? thumb * (k == 12 ? 0.6f : 1f) : (k < 3 ? index : curl) * (k % 3 == 0 ? 0.9f : 1f);
                var q = Quaternion.AngleAxis(c, th ? tax : ax);
                f[k].localRotation = w >= 0.999f ? q : Quaternion.Slerp(f[k].localRotation, q, w);
            }
        }
    }
}
