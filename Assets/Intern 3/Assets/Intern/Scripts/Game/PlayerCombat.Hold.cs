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
        public Vector3 fL = new Vector3(0.62f, 0.12f, 0.78f), nL = new Vector3(-0.25f, 1f, 0f), offL = new Vector3(0f, 0.012f, 0f);
        public float curlR = 80f, indexR = 32f, thumbR = 45f, curlL = 72f, indexL = 66f, thumbL = 28f;
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
                    fR = new Vector3(0f, -0.37f, 0.93f), fL = new Vector3(0.05f, -0.62f, 0.78f), nL = Vector3.right, offL = new Vector3(-0.014f, -0.01f, 0.01f),
                    curlL = 82f, indexL = 84f, thumbL = 20f,
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
                    fL = new Vector3(0.55f, 0.18f, 0.82f), offL = new Vector3(0f, 0.018f, 0f),
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
                    fL = new Vector3(0.5f, 0.15f, 0.85f), offL = new Vector3(0f, 0.016f, 0f),
                    kickBack = 0.07f, kickPitch = 11f, kickRoll = 2f, body = 3f,
                    flash = 0.18f, flashLen = 2.4f, light = 6f, spikes = 5, sparks = 6, smoke = 4,
                    casingD = 0.012f, casingL = 0.07f, ejectSpeed = 1.6f, cycle = "boltaction",
                };
                all["mg"] = new GunSpec
                {
                    fpHip = new Vector3(0.09f, -0.10f, 0.34f), fpAds = 0.28f,
                    fL = new Vector3(0.6f, 0.05f, 0.8f), offL = new Vector3(0f, -0.012f, 0f),
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

        void LateUpdate()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.05f);
            if (run != null && slot == 0 && Av != null) { MeleePose(dt); return; }
            if (trail != null) trail.Emit(false);
            if (run == null || gunModel == null || slot != 1 || Av == null || Av.elbowR == null) { if (fpArms != null) fpArms.Show(false); return; }
            if (!Av.v4 || Av.handR == null) { OldPlace(); return; }
            var g = gunModel; var sp = GunSpec.Of(g.id);
            if (poseFor != g) { poseFor = g; drawW = 0f; dustOpen = false; ResetSprings(); }
            var camT = player.cam.transform;
            bool fp = player.firstPerson || player.scopeView;
            float s = Av.transform.lossyScale.y;

            adsW = Mathf.MoveTowards(adsW, ads ? 1f : 0f, dt * 5.5f);
            drawW = Mathf.MoveTowards(drawW, 1f, dt / 0.32f);
            lowW = Mathf.MoveTowards(lowW, Av.moveSpeed > 5.2f && !ads && Time.time - lastShotAt > 0.4f ? 1f : 0f, dt * 4f);
            Spring(ref kickBack, ref kickBackV, dt, 380f, 34f);
            Spring(ref kickPitch, ref kickPitchV, dt, 260f, 28f);
            Spring(ref kickRoll, ref kickRollV, dt, 220f, 26f);
            Spring(ref kickYaw, ref kickYawV, dt, 220f, 26f);
            float ready = S01(drawW) * (1f - S01(lowW));

            Vector3 aim = player.AimPoint(80f);
            Vector3 aimDir = (aim - camT.position).normalized;
            // корпус: стойка стрелка (длинный ствол — левое плечо вперёд), голова на цель, отдача корпусом
            float twist = sp.twist;
            holdW = Mathf.MoveTowards(holdW, 1f, dt * 6f);
            Av.AimStance(twist, aimDir, Mathf.Max(0f, kickPitch) * sp.body * 0.2f, sp.longGun && !fp ? 7f * adsW + 3f : 0f, holdW);

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
            // опущен (достаёт, бежит): дуло вниз вокруг рукояти, ствол ближе к телу
            if (ready < 0.999f)
            {
                Vector3 gw = pos + rot * gripLocal;
                var down = Quaternion.AngleAxis(sp.longGun ? 38f : 55f, rot * Vector3.right) * Quaternion.AngleAxis(sp.longGun ? -18f : 0f, Vector3.up);
                Quaternion r2 = Quaternion.Slerp(down * rot, rot, ready);
                Vector3 drop = (Vector3.down * (sp.longGun ? 0.12f : 0.2f) + (rot * Vector3.back) * (sp.longGun ? 0.06f : 0.2f)) * s;
                pos = gw + drop * (1f - ready) - r2 * gripLocal; rot = r2;
            }
            // перезарядка: ствол наклонён к левой руке
            ReloadTilt(sp, ref pos, ref rot, gripLocal, s, dt);
            // отдача: поворот вокруг рукояти и отход назад
            {
                Vector3 gw = pos + rot * gripLocal;
                Quaternion kr = Quaternion.Euler(-kickPitch, kickYaw, kickRoll);
                Quaternion r2 = rot * kr;
                pos = gw - r2 * gripLocal - (r2 * Vector3.forward) * kickBack * s + Vector3.up * (kickBack * 0.25f * s);
                rot = r2;
            }
            g.transform.SetPositionAndRotation(pos, rot);
            bool hide = player.scopeView && Scoped && adsW > 0.85f;
            g.SetVisible(!hide);

            // руки: где они на оружии
            Quaternion hr = rot * CharacterAnim.HandRot(true, sp.fR, sp.nR);
            Quaternion hl = rot * CharacterAnim.HandRot(false, sp.fL, sp.nL);
            Vector3 tR = g.grip.position, tL = g.gripL != null ? g.gripL.position + rot * (sp.offL * s) : tR;

            // подвижные детали: спуск, затвор, цевьё, перезарядка (может забрать руки)
            CycleParts(g, sp, fp, camT, tL, hl);

            Vector3 cr = Av.torso != null ? Av.torso.right : Av.transform.right;
            Vector3 poleR = (cr * (sp.longGun ? 0.9f : 0.45f) + Vector3.down * (sp.longGun ? 0.7f : 1f) + (rot * Vector3.back) * 0.3f);
            Vector3 poleL = (Vector3.down * 1f - cr * (sp.longGun ? 0.25f : 0.45f) + (rot * Vector3.back) * 0.2f);
            if (g.pump != null && sp.cycle == "pump") tL += g.pump.position - g.PartRestW(g.pump);
            if (rightW > 0f) { tR = Vector3.Lerp(tR, rightAt, rightW); hr = Quaternion.Slerp(hr, rightRot, rightW); }
            if (leftW > 0f) { tL = Vector3.Lerp(tL, leftAt, leftW); hl = Quaternion.Slerp(hl, leftRot, leftW); }
            bool onTrigger = Time.time - shotAt < 0.07f;
            float idx = Mathf.Lerp(sp.indexR, sp.indexR + 22f, onTrigger ? 1f : 0f);
            if (fp && FpReady())
            {
                // от первого лица — отдельные руки у камеры
                fpArms.Show(true);
                fpArms.Place(camT, new Vector3(0f, -0.25f, -0.05f) * s, sp.longGun ? 30f : 0f);
                Vector3 cr2 = camT.right, dn = -camT.up;
                fpArms.IK(true, tR, hr, cr2 * (sp.longGun ? 0.8f : 0.5f) + dn * 1f - camT.forward * 0.2f, 0.45f);
                if (g.gripL != null) fpArms.IK(false, tL, hl, dn * 1f - cr2 * 0.35f - camT.forward * 0.1f, 0.5f);
                fpArms.Fingers(true, sp.curlR, idx, sp.thumbR, 1f - rightW * 0.7f);
                if (g.gripL != null) fpArms.Fingers(false, sp.curlL, sp.indexL, sp.thumbL, 1f - leftW * 0.6f);
            }
            else
            {
                if (fpArms != null) fpArms.Show(false);
                Av.ArmIK(true, tR, hr, poleR, 1f, 0.45f);
                Av.ArmIK(false, tL, hl, poleL, g.gripL != null ? 1f : 0f, 0.5f);
                Av.Fingers(true, sp.curlR, idx, sp.thumbR, 1f - rightW * 0.7f);
                if (g.gripL != null) Av.Fingers(false, sp.curlL, sp.indexL, sp.thumbL, 1f - leftW * 0.6f);
            }
            leftW = Mathf.MoveTowards(leftW, leftFree ? 1f : 0f, dt * 8f);
            rightW = Mathf.MoveTowards(rightW, rightFree ? 1f : 0f, dt * 8f);
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
                            rightRot = g.transform.rotation * CharacterAnim.HandRot(true, new Vector3(-0.2f, -0.75f, 0.6f), new Vector3(-0.6f, 0f, -0.5f));
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
