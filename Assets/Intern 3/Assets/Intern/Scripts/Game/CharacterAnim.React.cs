// Реакция на удар (спринт 8 версии 0.9 «Оружие в руках», задача 5): горожанин отшатывается туда, куда пришёлся удар
// (спереди — назад, сбоку — вбок, сзади — вперёд), сила — от урона и отбрасывания; в голову — голова запрокидывается
// сильнее. Потом рука хватается за ушибленное место (голова, грудь, живот, бедро) и держит, пока не отпустит боль.
// Скелет v4; старые модели — прежний клип «hit».
using UnityEngine;

namespace Intern.Game
{
    public partial class CharacterAnim
    {
        Vector3 rxDir; float rxT0 = -9f, rxS; bool rxHead;
        Transform woundBone; Vector3 woundLocal; bool woundRight; float woundT0 = -9f, woundLen;
        public bool DevWounded { get { return woundBone != null && Time.time - woundT0 < woundLen; } }

        static float Sm(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public void HitReact(Vector3 worldDir, Vector3 worldPoint, float strength)
        {
            if (!v4) { Flinch(); return; }
            Vector3 d = transform.InverseTransformDirection(worldDir); d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.back;
            rxDir = d.normalized; rxT0 = Time.time; rxS = Mathf.Clamp(strength, 0.3f, 1.7f);
            Vector3 lp = transform.InverseTransformPoint(worldPoint);
            float s = Mathf.Max(0.01f, transform.lossyScale.y), hy = lp.y / s;
            Transform bone = hy > 1.58f ? head : hy > 1.2f ? torso : hy > 0.9f ? (spine != null ? spine : hips) : hy > 0.62f ? (lp.x < 0f ? legL : legR) : null;
            rxHead = bone != null && bone == head;
            if (bone == null) { woundBone = null; return; }
            woundBone = bone; woundLocal = bone.InverseTransformPoint(worldPoint);
            // рука той же стороны; если в правой что-то есть — левая
            woundRight = lp.x > 0.02f && !gripRight && !holdRight && !aimGun;
            woundT0 = Time.time + 0.1f; woundLen = 1.2f + 0.7f * rxS;
        }

        // После позы кадра (UpdateV4): отшатнуться, схватиться за рану
        void ReactTick()
        {
            float t = Time.time - rxT0;
            if (t >= 0f && t < 1.3f && iSpine >= 0 && iTorso >= 0)
            {
                float env = t < 0.06f ? Sm(t / 0.06f) : Mathf.Exp(-(t - 0.06f) / 0.2f) * Mathf.Cos((t - 0.06f) * 6f);
                float ang = 17f * rxS * env;
                Vector3 axis = transform.TransformDirection(Vector3.Cross(Vector3.up, rxDir));
                foreach (int b in new[] { iSpine, iSpine2, iTorso })
                    if (b >= 0 && vb[b] != null) vb[b].rotation = Quaternion.AngleAxis(ang / 3f, axis) * vb[b].rotation;
                if (iNeck >= 0 && vb[iNeck] != null) vb[iNeck].rotation = Quaternion.AngleAxis(ang * (rxHead ? 0.9f : 0.25f), axis) * vb[iNeck].rotation;
                if (iHead >= 0 && vb[iHead] != null) vb[iHead].rotation = Quaternion.AngleAxis(ang * (rxHead ? 1.3f : 0.3f), axis) * vb[iHead].rotation;
                // руки вздрагивают в стороны
                if (armR != null && !aimGun && !holdRight) armR.rotation = Quaternion.AngleAxis(-ang * 0.8f, transform.forward) * armR.rotation;
                if (armL != null) armL.rotation = Quaternion.AngleAxis(ang * 0.8f, transform.forward) * armL.rotation;
            }
            float wt = Time.time - woundT0;
            if (woundBone != null && wt > -0.1f && wt < woundLen)
            {
                float w = Sm((wt + 0.1f) / 0.22f) * (1f - Sm((wt - (woundLen - 0.4f)) / 0.4f));
                Vector3 p = woundBone.TransformPoint(woundLocal);
                Vector3 n = Vector3.ProjectOnPlane(p - woundBone.position, woundBone.up);
                if (n.sqrMagnitude < 1e-5f) n = transform.forward;
                n.Normalize();
                // ладонь к ране, пальцы вниз и к середине тела
                Vector3 f = Vector3.ProjectOnPlane(Vector3.down * 0.6f + (transform.position - p).normalized * 0.4f, n);
                if (rxHead) f = Vector3.ProjectOnPlane(Vector3.up * 0.5f + transform.right * (woundRight ? -0.5f : 0.5f), n);
                if (f.sqrMagnitude < 1e-4f) f = Vector3.down;
                var hr = HandRot(woundRight, f, -n);
                Vector3 pole = transform.right * (woundRight ? 1f : -1f) + Vector3.down * 0.6f + transform.forward * 0.2f;
                ArmIK(woundRight, p + n * 0.022f, hr, pole, w, 0.5f);
                Fingers(woundRight, 20f, 12f, 15f, w);
            }
        }
    }
}
