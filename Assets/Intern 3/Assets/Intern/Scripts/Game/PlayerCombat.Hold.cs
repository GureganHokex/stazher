// Оружие в руках (спринт 8 версии 0.9 «Оружие в руках»): ствол ставится от тела, а руки — на ствол (IK), а не наоборот.
// От третьего лица: длинный ствол — затыльником в плечо, корпус в стойке (левое плечо вперёд), пистолет — двумя руками
// перед грудью. От первого лица — ствол в кадре камеры (от бедра справа внизу, в прицеле — по оси взгляда), руки тела
// тянутся к нему. Отдача — пружина (ствол уходит назад и вверх, руки за ним), затвор и цевьё ходят, гильзы летят.
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    // Как держать и как стреляет каждый ствол
    public class GunSpec
    {
        public bool longGun = true;
        public Vector3 pocket = new Vector3(-0.05f, -0.045f, 0.075f);   // затыльник от плечевого сустава (оси груди)
        public Vector3 pistolAt = new Vector3(0.05f, 0.08f, 0.53f);      // рукоять от середины плеч (оси прицела)
        public Vector3 fpHip = new Vector3(0.085f, -0.085f, 0.32f);       // точка Eye в осях камеры: от бедра
        public float fpAds = 0.18f; public float fpAdsUp = 0f;                                       // и в прицеле: на оси взгляда, на таком удалении
        public Vector3 fR = new Vector3(0f, -0.42f, 0.9f), nR = Vector3.left;          // правая: пальцы, ладонь (оси оружия)
        public Vector3 fL = new Vector3(0.62f, 0.12f, 0.78f), nL = new Vector3(-0.25f, 1f, 0f), offL = new Vector3(0f, 0.02f, 0f);
        public float curlR = 50f, indexR = 32f, thumbR = 40f, curlL = 46f, indexL = 44f, thumbL = 25f;
        public float twist = 34f;
        public float kickBack = 0.02f, kickPitch = 3.5f, kickRoll = 1.2f, body = 0.6f;
        public float flash = 0.1f, flashLen = 1.9f, light = 4f; public int spikes = 4, sparks = 4, smoke = 2;
        public Color flashTint = new Color(1f, 0.78f, 0.42f);
        public float casingD = 0.0095f, casingL = 0.045f, ejectSpeed = 2.6f; public bool shell;
        public string cycle = "";

        static Dictionary<string, GunSpec> all;
        public static GunSpec Of(string id)
        {
            if (all == null)
            {
                all = new Dictionary<string, GunSpec>();
                all["pistol"] = new GunSpec
                {
                    longGun = false, fpHip = new Vector3(0.07f, -0.075f, 0.38f), fpAds = 0.40f, twist = 4f,
                    fR = new Vector3(0f, -0.37f, 0.93f), fL = new Vector3(0.05f, -0.62f, 0.78f), nL = Vector3.right, offL = new Vector3(-0.006f, -0.016f, 0.02f),
                    curlL = 58f, indexL = 58f, thumbL = 20f,
                    kickBack = 0.03f, kickPitch = 9f, kickRoll = 2f, body = 0.3f,
                    flash = 0.09f, flashLen = 1.6f, light = 3f, spikes = 4, sparks = 4, smoke = 2,
                    casingD = 0.0095f, casingL = 0.019f, ejectSpeed = 2.4f, cycle = "slide",
                };
                all["smg"] = new GunSpec
                {
                    fpHip = new Vector3(0.085f, -0.085f, 0.32f), fpAds = 0.2f,
                    kickBack = 0.014f, kickPitch = 2.2f, kickRoll = 1f,
                    flash = 0.085f, flashLen = 1.7f, light = 3f, spikes = 3, sparks = 3, smoke = 1,
                    casingD = 0.0095f, casingL = 0.019f, ejectSpeed = 2.6f, cycle = "smg",
                };
                all["shotgun"] = new GunSpec
                {
                    fpHip = new Vector3(0.09f, -0.09f, 0.34f), fpAds = 0.4f, fpAdsUp = 0.025f,
                    fL = new Vector3(0.55f, 0.18f, 0.82f), offL = new Vector3(0f, 0.026f, 0f),
                    kickBack = 0.06f, kickPitch = 12f, kickRoll = 2.5f, body = 2.5f,
                    flash = 0.2f, flashLen = 2.6f, light = 7f, spikes = 6, sparks = 12, smoke = 5, flashTint = new Color(1f, 0.66f, 0.3f),
                    casingD = 0.02f, casingL = 0.065f, ejectSpeed = 2.2f, shell = true, cycle = "pump",
                };
                all["rifle"] = new GunSpec
                {
                    fpHip = new Vector3(0.085f, -0.085f, 0.32f), fpAds = 0.18f,
                    kickBack = 0.016f, kickPitch = 2.6f, kickRoll = 1.1f,
                    flash = 0.12f, flashLen = 1.5f, light = 4f, spikes = 4, sparks = 4, smoke = 2,
                    casingD = 0.0095f, casingL = 0.045f, ejectSpeed = 3f, cycle = "rifle",
                };
                all["sniper"] = new GunSpec
                {
                    fpHip = new Vector3(0.09f, -0.09f, 0.34f), fpAds = 0.3f,
                    fL = new Vector3(0.5f, 0.15f, 0.85f), offL = new Vector3(0f, 0.024f, 0f),
                    kickBack = 0.07f, kickPitch = 11f, kickRoll = 2f, body = 3f,
                    flash = 0.18f, flashLen = 2.4f, light = 6f, spikes = 5, sparks = 6, smoke = 4,
                    casingD = 0.012f, casingL = 0.07f, ejectSpeed = 1.6f, cycle = "boltaction",
                };
                all["mg"] = new GunSpec
                {
                    fpHip = new Vector3(0.09f, -0.10f, 0.34f), fpAds = 0.28f,
                    fL = new Vector3(0.6f, 0.05f, 0.8f), offL = new Vector3(0f, 0f, 0f),
                    kickBack = 0.018f, kickPitch = 2.4f, kickRoll = 1.4f, body = 0.8f,
                    flash = 0.15f, flashLen = 2f, light = 5f, spikes = 5, sparks = 5, smoke = 2,
                    casingD = 0.0095f, casingL = 0.045f, ejectSpeed = 2.2f, cycle = "mg",
                };
            }
            GunSpec s; return id != null && all.TryGetValue(id, out s) ? s : (all["rifle"]);
        }
    }

    public partial class PlayerCombat
    {
        float adsW, drawW = 1f, lowW, holdW;
        bool pumpEjected; float pumpSoundAt = -9f, boltSoundAt = -9f;
        float kickBack, kickBackV, kickPitch, kickPitchV, kickRoll, kickRollV, kickYaw, kickYawV;
        float shotAt = -9f;
        bool dustOpen;
        WeaponModel poseFor;
        MuzzleFlash flashFx;
        // перезарядка и цикл затвора могут забрать руки (PlayerCombat.Reload.cs)
        bool leftFree, rightFree; Vector3 leftAt, rightAt; Quaternion leftRot, rightRot; float leftW, rightW;

        public float DevAdsW { get { return adsW; } }

        static float S01(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Выстрел: толчок пружины отдачи, вспышка, гильза
        void ShotFx(WeaponDef def)
        {
            if (gunModel == null) return;
            var sp = GunSpec.Of(def.id);
            float k = Mathf.Lerp(1f, 0.65f, adsW);
            kickBackV += sp.kickBack * 60f * k;
            kickPitchV += sp.kickPitch * 55f * k * Random.Range(0.85f, 1.15f);
            kickRollV += sp.kickRoll * 40f * Random.Range(-1f, 1f);
            kickYawV += sp.kickRoll * 25f * Random.Range(-1f, 1f);
            shotAt = Time.time; pumpEjected = false;
            if (sp.cycle == "pump") pumpAt = shotAt + 0.2f;
            if (sp.cycle == "boltaction") boltAt = shotAt + 0.28f;
            bool silenced = ars != null && ars.Installed(CurrentSave, "barrel") == "silencer";
            if (gunModel.muzzle != null)
            {
                if (flashFx == null || flashFx.transform.parent != gunModel.transform) flashFx = MuzzleFlash.On(gunModel);
                flashFx.Fire(sp, silenced);
            }
            if (sp.cycle != "pump" && sp.cycle != "boltaction") Casings.Eject(gunModel, sp, player);
            var mz = gunModel.muzzle != null ? gunModel.muzzle.position : gunModel.transform.position;
            Sfx.Play(silenced ? "shot_silenced" : "shot_" + def.id, mz, silenced ? 0.7f : 1f, 0.06f, 0.6f, 4f);
            if (sp.cycle == "rifle") dustOpen = true;
        }

        // Пружина: возврат за ~0,15 с без раскачки
        static void Spring(ref float x, ref float v, float dt, float k, float c)
        {
            v += (-k * x - c * v) * dt; x += v * dt;
        }

        FpArms fpArms;
        public string DevGunFp = "";
        public Vector3? DevGunGrip { get { var g = slot == 1 ? gunModel : meleeModel; return g != null && g.grip != null ? g.grip.position : (Vector3?)null; } }

        void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
            if (run != null && slot == 0 && Av != null) { MeleePose(dt); return; }
            if (Av != null) Av.guardClip = null;
            if (trail != null) trail.Emit(false);
            if (Av != null && slot != 1) Av.holdClip = null;
            if (slot != 1 || run == null) DropGunDouble();
            if (slot != 0 || run == null) DropMeleeDouble();
            if (run == null || gunModel == null || slot != 1 || Av == null || Av.elbowR == null) { if (fpArms != null) fpArms.Show(false); return; }
            if (!Av.v4 || Av.handR == null) { OldPlace(); return; }
            var g = gunModel; var sp = GunSpec.Of(g.id);
            if (poseFor != g) { poseFor = g; drawW = 0f; dustOpen = false; ResetSprings(); }
            var camT = player.cam.transform;
            bool fp = player.firstPerson || player.scopeView;
            float s = Av.transform.lossyScale.y;

            adsW = Mathf.MoveTowards(adsW, ads ? 1f : 0f, dt * 5.5f);
            drawW = Mathf.MoveTowards(drawW, swapTo >= 0 ? 0f : 1f, dt / (swapTo >= 0 ? PutAway : 0.32f));   // убирает перед сменой / достаёт
            lowW = Mathf.MoveTowards(lowW, Av.moveSpeed > 5.2f && !ads && Time.time - lastShotAt > 0.4f ? 1f : 0f, dt * 4f);
            Spring(ref kickBack, ref kickBackV, dt, 380f, 34f);
            Spring(ref kickPitch, ref kickPitchV, dt, 260f, 28f);
            Spring(ref kickRoll, ref kickRollV, dt, 220f, 26f);
            Spring(ref kickYaw, ref kickYawV, dt, 220f, 26f);
            float ready = S01(drawW) * (1f - S01(lowW));

            Vector3 aim = player.AimPoint(80f);
            Vector3 aimDir = (aim - camT.position).normalized;
            // поза из Blender (Art/grips_v1.py): клип hold_<оружие> даёт корпус, голову и пальцы, ствол стоит от груди
            var grip = GripLib.Get(g.id);
            Av.holdClip = grip != null ? "hold_" + g.id : null;
            // корпус: стойка стрелка (длинный ствол — левое плечо вперёд), голова на цель, отдача корпусом
            float twist = grip != null ? 0f : sp.twist;
            holdW = Mathf.MoveTowards(holdW, 1f, dt * 6f);
            Av.AimStance(twist, aimDir, Mathf.Max(0f, kickPitch) * sp.body * 0.2f, grip == null && sp.longGun && !fp ? 7f * adsW + 3f : 0f, holdW);

            Vector3 gripLocal = g.Local(g.grip), eyeLocal = g.eye != null ? g.Local(g.eye) : gripLocal + new Vector3(0f, 0.08f, 0f);
            Vector3 stockLocal = g.stockPt != null ? g.Local(g.stockPt) : gripLocal + new Vector3(0f, 0.04f, -0.25f);
            Vector3 pos; Quaternion rot;
            if (fp)
            {
                Vector3 e = Vector3.Lerp(sp.fpHip, new Vector3(0f, -sp.fpAdsUp, sp.fpAds), S01(adsW));
                e = Vector3.Lerp(e, new Vector3(0.03f, -0.13f, 0.34f), S01(tiltW));   // перезарядка — ствол ближе к середине кадра
                Vector3 eyeW = camT.TransformPoint(e * s);
                Vector3 dir = aim - eyeW; if (dir.sqrMagnitude < 1f) dir = camT.forward;
                // от бедра ствол чуть завален влево и смотрит к середине, в прицеле — ровно по оси взгляда
                var hipRot = Quaternion.LookRotation(dir.normalized, camT.up) * Quaternion.Euler(-1f, -2f, -6f);
                rot = Quaternion.Slerp(hipRot, camT.rotation, S01(adsW));
                pos = eyeW - rot * eyeLocal;
            }
            else TpPose(g, sp, grip, aim, aimDir, gripLocal, stockLocal, s, out pos, out rot);
            Lowered(sp, ready, gripLocal, s, ref pos, ref rot);
            // перезарядка: ствол наклонён к левой руке
            ReloadTilt(sp, ref pos, ref rot, gripLocal, s, dt);
            Kick(gripLocal, s, ref pos, ref rot);
            g.transform.SetPositionAndRotation(pos, rot);
            bool hide = player.scopeView && Scoped && adsW > 0.85f;
            g.SetVisible(!hide);

            // руки: где они на оружии (из позы Blender — запястья и повороты кистей в осях оружия)
            Quaternion hr = rot * CharacterAnim.HandRot(true, sp.fR, sp.nR);
            Quaternion hl = rot * CharacterAnim.HandRot(false, sp.fL, sp.nL);
            Vector3 tR = g.grip.position, tL = g.gripL != null ? g.gripL.position + rot * (sp.offL * s) : tR;
            Vector3 eR = Vector3.zero, eL = Vector3.zero;
            if (grip != null)
            {
                hr = rot * grip.handRot[0]; hl = rot * grip.handRot[1];
                tR = g.transform.TransformPoint(grip.handPos[0]) + hr * (CharacterAnim.PalmR * s);
                tL = g.transform.TransformPoint(grip.handPos[1]) + hl * (CharacterAnim.PalmL * s);
                eR = g.transform.TransformPoint(grip.elbow[0]); eL = g.transform.TransformPoint(grip.elbow[1]);
            }

            // подвижные детали: спуск, затвор, цевьё, перезарядка (может забрать руки)
            CycleParts(g, sp, fp, camT, tL, hl);

            Vector3 cr = Av.torso != null ? Av.torso.right : Av.transform.right;
            Vector3 poleR = (cr * (sp.longGun ? 0.9f : 0.45f) + Vector3.down * (sp.longGun ? 0.7f : 1f) + (rot * Vector3.back) * 0.3f);
            Vector3 poleL = (Vector3.down * 1f - cr * (sp.longGun ? 0.25f : 0.45f) + (rot * Vector3.back) * 0.2f);
            if (grip != null)
            {
                // локти — как в позе: полюс от середины «плечо — кисть» к локтю
                poleR = eR - (Av.armR.position + tR) * 0.5f; poleL = eL - (Av.armL.position + tL) * 0.5f;
            }
            if (g.pump != null && sp.cycle == "pump") tL += g.pump.position - g.PartRestW(g.pump);
            if (rightW > 0f) { tR = Vector3.Lerp(tR, rightAt, rightW); hr = Quaternion.Slerp(hr, rightRot, rightW); }
            if (leftW > 0f) { tL = Vector3.Lerp(tL, leftAt, leftW); hl = Quaternion.Slerp(hl, leftRot, leftW); }
            bool onTrigger = Time.time - shotAt < 0.07f;
            float idx = Mathf.Lerp(sp.indexR, sp.indexR + 22f, onTrigger ? 1f : 0f);
            // спуск: подушечка указательного — на передней грани спускового крючка
            Vector3 trig = g.trigger != null ? g.trigger.position + rot * new Vector3(0f, -0.011f, 0.003f) : tR;
            float squeeze = onTrigger ? 14f : 0f;
            if (hide)
            {
                // в оптике рук не видно: прицел закрывает кадр; со стороны тело держит винтовку
                if (FpReady()) fpArms.Conceal();
                OutsideGun(g, sp, grip, aim, aimDir, gripLocal, stockLocal, ready, s, squeeze, idx);
            }
            else if (fp && FpReady())
            {
                // от первого лица — отдельные руки у камеры
                fpArms.Show(true);
                fpArms.Place(camT, new Vector3(0f, -0.25f, -0.05f) * s, sp.longGun ? 30f : 0f);
                Vector3 cr2 = camT.right, dn = -camT.up;
                fpArms.IK(true, tR, hr, cr2 * (sp.longGun ? 0.8f : 0.5f) + dn * 1f - camT.forward * 0.2f, 0.45f);
                if (g.gripL != null) fpArms.IK(false, tL, hl, dn * 1f - cr2 * 0.35f - camT.forward * 0.1f, 0.5f);
                // со стороны и в тени тело держит двойник ствола, как от третьего лица (камера игрока видит руки у камеры)
                OutsideGun(g, sp, grip, aim, aimDir, gripLocal, stockLocal, ready, s, squeeze, idx);
                if (grip != null)
                {
                    GripLib.SetFingers(fpArms.FingerBones(true), grip.fingers[0], true, squeeze, 1f - rightW * 0.7f);
                    GripLib.SetFingers(fpArms.FingerBones(false), grip.fingers[1], false, 0f, 1f - leftW * 0.6f);
                    var fb = fpArms.FingerBones(true); int nf = 0; foreach (var t in fb) if (t != null) nf++;
                    DevGunFp = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "кисть R {0:0.0} мм {1:0}°, L {2:0.0} мм {3:0}°, костей пальцев {4}, масштаб {5:0.000}/{6:0.000}, палец {7:0}°, ствол от глаза {8}",
                        Vector3.Distance(fpArms.HandR.TransformPoint(CharacterAnim.PalmR), tR) * 1000f, Quaternion.Angle(fpArms.HandR.rotation, hr),
                        Vector3.Distance(fpArms.HandL.TransformPoint(CharacterAnim.PalmL), tL) * 1000f, Quaternion.Angle(fpArms.HandL.rotation, hl),
                        nf, fpArms.HandR.lossyScale.x, s, fb[1] != null ? Quaternion.Angle(fb[1].localRotation, grip.fingers[0][1]) : -1f,
                        (camT.InverseTransformPoint(g.grip.position) / s).ToString("F3"));
                }
                else
                {
                    fpArms.Fingers(true, sp.curlR, idx, sp.thumbR, 1f - rightW * 0.7f);
                    if (g.gripL != null) fpArms.Fingers(false, sp.curlL, sp.indexL, sp.thumbL, 1f - leftW * 0.6f);
                    if (rightW < 0.3f && g.trigger != null) ArmSolver.TriggerIndex(fpArms.FingerBones(true), true, trig, squeeze);
                }
            }
            else
            {
                if (fpArms != null) fpArms.Show(false);
                DropGunDouble();
                Av.ArmIK(true, tR, hr, poleR, 1f, 0.45f);
                Av.ArmIK(false, tL, hl, poleL, g.gripL != null ? 1f : 0f, 0.5f);
                if (grip != null)
                {
                    GripLib.SetFingers(Av.FingerBones(true), grip.fingers[0], true, squeeze, 1f - rightW * 0.7f);
                    GripLib.SetFingers(Av.FingerBones(false), grip.fingers[1], false, 0f, 1f - leftW * 0.6f);
                }
                else
                {
                    Av.Fingers(true, sp.curlR, idx, sp.thumbR, 1f - rightW * 0.7f);
                    if (g.gripL != null) Av.Fingers(false, sp.curlL, sp.indexL, sp.thumbL, 1f - leftW * 0.6f);
                    if (rightW < 0.3f && g.trigger != null) ArmSolver.TriggerIndex(Av.FingerBones(true), true, trig, squeeze);
                }
            }
            leftW = Mathf.MoveTowards(leftW, leftFree ? 1f : 0f, dt * 8f);
            rightW = Mathf.MoveTowards(rightW, rightFree ? 1f : 0f, dt * 8f);
        }

        // Ствол от третьего лица: от груди по позе из Blender (или по старым правилам), довёрнут на цель
        void TpPose(WeaponModel g, GunSpec sp, GripLib.Grip grip, Vector3 aim, Vector3 aimDir, Vector3 gripLocal, Vector3 stockLocal, float s, out Vector3 pos, out Quaternion rot)
        {
            if (grip != null && Av.torso != null)
            {
                // как в позе: от груди, потом довернуть на цель вокруг затыльника (пистолет — вокруг рукояти)
                rot = Av.torso.rotation * grip.chestRot;
                pos = Av.torso.position + Av.torso.rotation * (grip.chestPos * s);
                Vector3 pivL = sp.longGun ? stockLocal : gripLocal;
                Vector3 piv = pos + rot * pivL;
                Vector3 want = aim - piv; if (want.sqrMagnitude < 1f) want = aimDir;
                rot = Quaternion.FromToRotation(rot * Vector3.forward, want.normalized) * rot;
                pos = piv - rot * pivL;
            }
            else if (sp.longGun)
            {
                var chest = Av.torso != null ? Av.torso.rotation : Av.transform.rotation;
                Vector3 pocket = Av.armR.position + chest * (sp.pocket * s) + Vector3.up * (0.025f * adsW * s);
                Vector3 dir = aim - pocket; if (dir.sqrMagnitude < 1f) dir = aimDir;
                rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                pos = pocket - rot * stockLocal;
            }
            else
            {
                Vector3 mid = (Av.armR.position + Av.armL.position) * 0.5f;
                Vector3 d0 = aim - mid; if (d0.sqrMagnitude < 1f) d0 = aimDir;
                Vector3 off = Vector3.Lerp(sp.pistolAt, sp.pistolAt + new Vector3(-0.01f, 0.05f, 0.03f), S01(adsW));
                Vector3 gp = mid + Quaternion.LookRotation(d0.normalized, Vector3.up) * (off * s);
                Vector3 dir = aim - gp; if (dir.sqrMagnitude < 1f) dir = aimDir;
                rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
                pos = gp - rot * gripLocal;
            }
        }

        // Опущен (достаёт, бежит): дуло вниз вокруг рукояти, ствол ближе к телу
        void Lowered(GunSpec sp, float ready, Vector3 gripLocal, float s, ref Vector3 pos, ref Quaternion rot)
        {
            if (ready < 0.999f)
            {
                Vector3 gw = pos + rot * gripLocal;
                var down = Quaternion.AngleAxis(sp.longGun ? 38f : 55f, rot * Vector3.right) * Quaternion.AngleAxis(sp.longGun ? -18f : 0f, Vector3.up);
                Quaternion r2 = Quaternion.Slerp(down * rot, rot, ready);
                Vector3 drop = (Vector3.down * (sp.longGun ? 0.12f : 0.2f) + (rot * Vector3.back) * (sp.longGun ? 0.06f : 0.2f)) * s;
                pos = gw + drop * (1f - ready) - r2 * gripLocal; rot = r2;
            }
        }

        // Отдача: поворот вокруг рукояти и отход назад
        void Kick(Vector3 gripLocal, float s, ref Vector3 pos, ref Quaternion rot)
        {
            {
                Vector3 gw = pos + rot * gripLocal;
                Quaternion kr = Quaternion.Euler(-kickPitch, kickYaw, kickRoll);
                Quaternion r2 = rot * kr;
                pos = gw - r2 * gripLocal - (r2 * Vector3.forward) * kickBack * s + Vector3.up * (kickBack * 0.25f * s);
                rot = r2;
            }
        }

        // ---------- двойник оружия для вида со стороны (от первого лица) ----------
        WeaponDouble gunDouble, meleeDouble;

        void OutsideGun(WeaponModel g, GunSpec sp, GripLib.Grip grip, Vector3 aim, Vector3 aimDir, Vector3 gripLocal, Vector3 stockLocal,
                        float ready, float s, float squeeze, float idx)
        {
            if (gunDouble == null || gunDouble.Src != g || gunDouble.Root == null)
            {
                DropGunDouble();
                gunDouble = WeaponDouble.For(g);
                FpView.KeepForMain("gun", g.gameObject, true);
            }
            if (gunDouble == null) return;
            gunDouble.Sync();
            Vector3 pos; Quaternion rot;
            TpPose(g, sp, grip, aim, aimDir, gripLocal, stockLocal, s, out pos, out rot);
            Lowered(sp, ready, gripLocal, s, ref pos, ref rot);
            Kick(gripLocal, s, ref pos, ref rot);
            var R = gunDouble.Root;
            R.SetPositionAndRotation(pos, rot);
            Quaternion hr, hl; Vector3 tR, tL, poleR, poleL;
            Vector3 cr = Av.torso != null ? Av.torso.right : Av.transform.right;
            if (grip != null)
            {
                hr = rot * grip.handRot[0]; hl = rot * grip.handRot[1];
                tR = R.TransformPoint(grip.handPos[0]) + hr * (CharacterAnim.PalmR * s);
                tL = R.TransformPoint(grip.handPos[1]) + hl * (CharacterAnim.PalmL * s);
                poleR = R.TransformPoint(grip.elbow[0]) - (Av.armR.position + tR) * 0.5f;
                poleL = R.TransformPoint(grip.elbow[1]) - (Av.armL.position + tL) * 0.5f;
            }
            else
            {
                var gR = gunDouble.Of(g.grip); var gL = gunDouble.Of(g.gripL);
                hr = rot * CharacterAnim.HandRot(true, sp.fR, sp.nR); hl = rot * CharacterAnim.HandRot(false, sp.fL, sp.nL);
                tR = gR != null ? gR.position : R.position; tL = gL != null ? gL.position + rot * (sp.offL * s) : tR;
                poleR = cr * 0.6f + Vector3.down; poleL = Vector3.down - cr * 0.4f;
            }
            if (g.pump != null && sp.cycle == "pump") tL += rot * (Quaternion.Inverse(g.transform.rotation) * (g.pump.position - g.PartRestW(g.pump)));
            BodyHoldsGun(g, tR, hr, tL, hl, poleR, poleL, grip, sp, squeeze, idx, tR);
        }

        void DropGunDouble()
        {
            if (gunDouble != null) { gunDouble.Destroy(); gunDouble = null; }
            FpView.KeepForMain("gun", null, false);
        }

        void DropMeleeDouble()
        {
            if (meleeDouble != null) { meleeDouble.Destroy(); meleeDouble = null; }
            FpView.KeepForMain("melee", null, false);
        }

        // Руки тела на стволе (то же, что от третьего лица): IK к точкам хвата и пальцы
        void BodyHoldsGun(WeaponModel g, Vector3 tR, Quaternion hr, Vector3 tL, Quaternion hl, Vector3 poleR, Vector3 poleL,
                          GripLib.Grip grip, GunSpec sp, float squeeze, float idx, Vector3 trig)
        {
            Av.ArmIK(true, tR, hr, poleR, 1f, 0.45f);
            Av.ArmIK(false, tL, hl, poleL, g.gripL != null ? 1f : 0f, 0.5f);
            if (grip != null)
            {
                GripLib.SetFingers(Av.FingerBones(true), grip.fingers[0], true, squeeze, 1f - rightW * 0.7f);
                GripLib.SetFingers(Av.FingerBones(false), grip.fingers[1], false, 0f, 1f - leftW * 0.6f);
            }
            else
            {
                Av.Fingers(true, sp.curlR, idx, sp.thumbR, 1f - rightW * 0.7f);
                if (g.gripL != null) Av.Fingers(false, sp.curlL, sp.indexL, sp.thumbL, 1f - leftW * 0.6f);
            }
        }

        bool FpReady()
        {
            if (fpArms != null && !fpArms.Fits(Av))
            {
                if (meleeModel != null && fpArms.root != null && meleeModel.transform.IsChildOf(fpArms.root) && Av != null && Av.handR != null) meleeModel.transform.SetParent(Av.handR, false);
                fpArms.Destroy(); fpArms = null;
            }
            if (fpArms == null) fpArms = FpArms.Build(Av, transform);
            return fpArms != null;
        }

        public void DropFpArms() { if (fpArms != null) { fpArms.Destroy(); fpArms = null; } }

        void ResetSprings() { kickBack = kickBackV = kickPitch = kickPitchV = kickRoll = kickRollV = kickYaw = kickYawV = 0f; leftW = rightW = 0f; leftFree = rightFree = false; }

        // Затвор после выстрела, спуск, цевьё дробовика, рукоять затвора винтовки
        void CycleParts(WeaponModel g, GunSpec sp, bool fp, Transform camT, Vector3 G, Quaternion GR)
        {
            float t = Time.time - shotAt;
            g.ResetParts();
            if (g.trigger != null && t < 0.09f) g.Move(g.trigger, Vector3.zero, Quaternion.Euler(16f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.09f)), 0f, 0f));
            bool empty = MagNow <= 0 && !Reloading;
            leftFree = false; rightFree = false;
            if (dustOpen) g.Move(g.dustCover, Vector3.zero, Quaternion.Euler(0f, 0f, -105f));
            if (Reloading) ReloadParts(g, sp, fp, camT, G, GR);
            else ReloadEnd(g);
            if (Reloading && sp.cycle != "boltaction") return;
            switch (sp.cycle)
            {
                case "slide":
                {
                    float b = empty ? 1f : t < 0.018f ? t / 0.018f : 1f - S01((t - 0.018f) / 0.06f);
                    if (t >= 0f && (t < 0.08f || empty)) g.Move(g.slide, new Vector3(0f, 0f, -0.027f * Mathf.Clamp01(b)));
                    break;
                }
                case "rifle":
                {
                    float b = t < 0.015f ? t / 0.015f : 1f - S01((t - 0.015f) / 0.05f);
                    if (t < 0.07f) g.Move(g.bolt, new Vector3(0f, 0f, -0.04f * Mathf.Clamp01(b)));
                    break;
                }
                case "mg":
                {
                    if (t < 0.06f) g.Move(g.belt, new Vector3(0.007f * Mathf.Sin(Mathf.PI * t / 0.06f), 0f, 0f));
                    break;
                }
                case "pump":
                {
                    // через 0,2 с после выстрела (или после перезарядки с пустого) — цевьё назад (вылетает гильза) и вперёд
                    float u = (Time.time - pumpAt) / 0.34f;
                    if (u > 0f && u < 1f && !Reloading)
                    {
                        if (pumpSoundAt != pumpAt) { pumpSoundAt = pumpAt; Sfx.Play("pump", g.transform.position, 0.7f, 0.04f, 0.6f); }
                        float b = u < 0.45f ? S01(u / 0.45f) : 1f - S01((u - 0.45f) / 0.55f);
                        g.Move(g.pump, new Vector3(0f, 0f, -0.085f * b));
                        if (u >= 0.42f && !pumpEjected) { pumpEjected = true; Casings.Eject(g, sp, player); }
                    }
                    break;
                }
                case "boltaction":
                {
                    // рукоять вверх — назад (гильза) — вперёд — вниз; правая рука уходит на рукоять
                    float u = (Time.time - boltAt) / 0.62f;
                    if (u > -0.15f && u < 1.15f && (!Reloading || ReloadProgress > 0.72f))
                    {
                        float lift = S01(u / 0.18f) * (1f - S01((u - 0.82f) / 0.18f));
                        float back = S01((u - 0.2f) / 0.25f) * (1f - S01((u - 0.55f) / 0.25f));
                        if (u > 0f && u < 1f)
                        {
                            if (boltSoundAt != boltAt) { boltSoundAt = boltAt; Sfx.Play("bolt", g.transform.position, 0.7f, 0.04f, 0.6f); }
                            g.Move(g.boltHandle, new Vector3(0f, 0f, -0.085f * back), Quaternion.Euler(0f, 0f, 62f * lift));
                            if (back > 0.9f && !pumpEjected) { pumpEjected = true; Casings.Eject(g, sp, player); }
                        }
                        if (g.boltHandle != null)
                        {
                            rightFree = u > -0.1f && u < 1.05f;
                            rightAt = g.KnobW();
                            // рукоять берётся сбоку справа-сзади: ладонь влево и чуть вниз, пальцы вперёд
                            rightRot = g.transform.rotation * CharacterAnim.HandRot(true, new Vector3(-0.15f, -0.35f, 0.92f), new Vector3(-0.85f, -0.35f, 0f));
                        }
                    }
                    break;
                }
            }
        }

        // Старые модели (без скелета v4): ствол в руке, смотрит на прицел
        void OldPlace()
        {
            var hand = Av.elbowR.TransformPoint(new Vector3(0, -0.27f, 0.03f));
            var aim = player.AimPoint(60f);
            var dir = aim - hand; if (dir.sqrMagnitude < 0.25f) dir = player.cam.transform.forward;
            gunModel.transform.position = hand;
            gunModel.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
