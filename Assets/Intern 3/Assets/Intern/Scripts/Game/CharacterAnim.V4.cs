// Спринт 7 версии 0.9 «Живые персонажи»: CharacterAnim на клипах v4 (см. AnimV4.cs).
// Старые модели без позвоночника из трёх костей по-прежнему анимируются кодом (UpdateImported).
using UnityEngine;

namespace Intern.Game
{
    public partial class CharacterAnim
    {
        public static bool UseV4 = true;             // false — старая анимация кодом (ролики «до»)
        public static bool FootIK = true;            // ступни на земле (выключается для замеров)
        public static float IKDistance = 32f;        // дальше камеры — без IK
        public static float LodDistance = 60f;       // дальше — обновление через кадр
        public float throwStart = -9f;               // бросок (нотариус с печатью)
        public bool gripRight;                       // в правой руке предмет (портфель, книга, телефон) — пальцы сжаты
        float hitStart = -9f, plopT = 9f;

        public bool v4;
        int[] fingersR;
        Transform[] vb;
        Quaternion[] pz, lay, lay2, gait;
        Vector4[] acc;
        Vector3 rootOff;
        int iHips = -1, iSpine = -1, iSpine2 = -1, iTorso = -1, iNeck = -1, iHead = -1, iShR = -1, iShL = -1;
        AnimClip[] walk8, run8;          // 8 направлений: 0°, 45°, 90°, 135°, 180°, −135°, −90°, −45°
        readonly float[] w8 = new float[8];
        AnimClip cJog;
        AnimClip cIdle, cWalk, cWalkB, cWalkL, cWalkR, cRun, cRunB, cRunL, cRunR, cSprint, cSit, cSitIdle, cType, cWave, cSwing, cThrow, cHit, cLand, cAim1, cAim2, cHold, cAirUp, cAirDown;
        float gPhase, idleT, typeT, moveW, spdSm, dirSm, airW, landT = 9f, typeW, holdW, aimW, bank, pitchLean, lastYaw, yawRate, lastSpd, vySm, lastY;
        Vector3 velSm, lastPosV4;
        int lodSkip; float lodDt;
        float legLA, legLB;
        readonly float[] contact = new float[2], gs = new float[2];
        // ступня в опоре держит точку касания (пятку, пока носок поднят, иначе подушечку), а не голеностоп:
        // так перекат через стопу остаётся, а сама стопа по земле не едет
        class FootState { public bool planted, stepping; public int piv; public Vector3 pivW, delta, stepFrom; public float lockYaw, lockW, stepT, lastC; }
        static readonly Vector3 HeelPt = new Vector3(0f, -0.1f, -0.072f), BallPt = new Vector3(0f, -0.1f, 0.092f);
        readonly FootState[] feet = { new FootState(), new FootState() };
        public float GaitPhase { get { return gPhase; } }
        public float DevTypeW { get { return typeW; } }
        public Transform spine;
        public Transform handR, handL;               // кисти (скелет v4): оружие и предметы держатся в ладони
        // середина хвата правой руки в осях кисти (в покое оси кисти = оси персонажа: ладонь смотрит внутрь, −X):
        // по замеру кисти (сцена hand) ладонь кончается на x −0,026, пальцы от костяшек (y −0,093) при сгибе ~50°
        // обхватывают круг около (−0,036; −0,088) — сюда встаёт ось рукояти, пальцы ложатся вокруг неё
        public static readonly Vector3 PalmR = new Vector3(-0.036f, -0.088f, 0.008f);
        public System.Collections.Generic.IEnumerable<Transform> V4Bones { get { if (vb != null) foreach (var t in vb) if (t != null) yield return t; } }
        public float DevContact(int i) { return contact[i]; }
        public bool DevPlanted(int i) { return feet[i].planted; }

        void InitV4()
        {
            v4 = false;
            if (!UseV4 || !imported || rig == null || F("Spine") == null || !AnimLib.Ready) return;
            var B = AnimLib.Bones;
            vb = new Transform[B.Length];
            for (int i = 0; i < B.Length; i++) vb[i] = F(B[i]);
            pz = new Quaternion[B.Length]; lay = new Quaternion[B.Length]; lay2 = new Quaternion[B.Length]; gait = new Quaternion[B.Length];
            acc = new Vector4[B.Length];
            iHips = Idx("Hips"); iSpine = Idx("Spine"); iSpine2 = Idx("Spine2"); iTorso = Idx("Torso"); iNeck = Idx("Neck"); iHead = Idx("Head");
            iShR = Idx("ShoulderR"); iShL = Idx("ShoulderL");
            fingersR = new int[15];
            string[] fn = { "Index", "Middle", "Ring", "Pinky", "Thumb" };
            for (int f = 0; f < 5; f++) for (int j = 0; j < 3; j++) fingersR[f * 3 + j] = Idx(fn[f] + (j + 1) + "R");
            spine = F("Spine"); handR = F("HandR"); handL = F("HandL");
            cIdle = AnimLib.Get("idle"); cWalk = AnimLib.Get("walk"); cWalkB = AnimLib.Get("walk_b"); cWalkL = AnimLib.Get("walk_l"); cWalkR = AnimLib.Get("walk_r");
            cRun = AnimLib.Get("run"); cRunB = AnimLib.Get("run_b"); cRunL = AnimLib.Get("run_l"); cRunR = AnimLib.Get("run_r"); cSprint = AnimLib.Get("sprint");
            cSit = AnimLib.Get("sit"); cSitIdle = AnimLib.Get("sit_idle"); cType = AnimLib.Get("type"); cWave = AnimLib.Get("wave"); cSwing = AnimLib.Get("swing");
            cThrow = AnimLib.Get("throw"); cHit = AnimLib.Get("hit"); cLand = AnimLib.Get("land"); cAim1 = AnimLib.Get("aim1"); cAim2 = AnimLib.Get("aim2");
            cHold = AnimLib.Get("hold"); cAirUp = AnimLib.Get("air_up"); cAirDown = AnimLib.Get("air_down");
            cJog = AnimLib.Get("jog");
            walk8 = new[] { cWalk, AnimLib.Get("walk_fr"), cWalkR, AnimLib.Get("walk_br"), cWalkB, AnimLib.Get("walk_bl"), cWalkL, AnimLib.Get("walk_fl") };
            run8 = new[] { cRun, AnimLib.Get("run_fr"), cRunR, AnimLib.Get("run_br"), cRunB, AnimLib.Get("run_bl"), cRunL, AnimLib.Get("run_fl") };
            if (cIdle == null || cWalk == null || cRun == null || iHips < 0 || vb[iHips] == null) return;
            var fl = F("FootL");
            if (legL == null || kneeL == null || fl == null) return;
            legLA = Vector3.Distance(legL.position, kneeL.position); legLB = Vector3.Distance(kneeL.position, fl.position);
            idleT = Random.Range(0f, 8f); gPhase = Random.value; typeT = Random.Range(0f, 5f);
            lastPosV4 = transform.position; lastY = transform.position.y; lastYaw = transform.eulerAngles.y;
            for (int i = 0; i < 2; i++) { feet[i].planted = false; feet[i].stepping = false; feet[i].lockW = 0f; }
            v4 = true;
        }

        static int Idx(string n) { int i; return AnimLib.BoneIndex.TryGetValue(n, out i) ? i : -1; }

        void LateUpdate()
        {
            if (!v4 || hips == null) return;
            UpdateV4();
        }

        // ---------- слои ----------
        // Действие поверх позы: кости по маске клипа. Грудь ставится так, как в клипе относительно персонажа
        // (при боковом ходе таз развёрнут — грудь всё равно смотрит вперёд)
        void Layer(AnimClip c, float t, float w)
        {
            if (c == null || w <= 0.001f) return;
            Vector3 r;
            AnimLib.Sample(c, t, lay, out r);
            var m = c.mask;
            bool spine = m == null || (iSpine >= 0 && m[iSpine]);
            Quaternion chestWant = Quaternion.identity, chestNow = Quaternion.identity;
            if (spine && iSpine >= 0 && iSpine2 >= 0 && iTorso >= 0)
            {
                chestWant = Quaternion.Inverse(pz[iHips]) * (lay[iHips] * lay[iSpine] * lay[iSpine2] * lay[iTorso]);
                chestNow = pz[iSpine] * pz[iSpine2] * pz[iTorso];
            }
            for (int b = 0; b < pz.Length; b++)
            {
                if (m != null && !m[b]) continue;
                if (b == iHips || b == iSpine || b == iSpine2 || b == iTorso) continue;
                pz[b] = Quaternion.Slerp(pz[b], lay[b], w);
            }
            if (spine && iSpine >= 0 && iSpine2 >= 0 && iTorso >= 0)
            {
                var s = Quaternion.Slerp(chestNow, chestWant, w);
                var third = Quaternion.Slerp(Quaternion.identity, s, 1f / 3f);
                pz[iSpine] = third; pz[iSpine2] = third; pz[iTorso] = third;
            }
            if (m == null) rootOff = Vector3.Lerp(rootOff, r, w);
        }

        void Blend(Quaternion[] src, Vector3 srcRoot, float w)
        {
            if (w <= 0.001f) return;
            for (int b = 0; b < pz.Length; b++) pz[b] = Quaternion.Slerp(pz[b], src[b], w);
            rootOff = Vector3.Lerp(rootOff, srcRoot, w);
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        // Окно действия длиной len: плавно входит и выходит
        static float Window(float t, float len, float fin = 0.07f, float fout = 0.15f)
        {
            if (t < 0f || t >= len) return 0f;
            return Mathf.Min(1f, Mathf.Min(Smooth(t / fin), Smooth((len - t) / fout)));
        }

        void UpdateV4()
        {
            float dt = Mathf.Clamp(Time.deltaTime, 0.0001f, 0.1f);
            var cam = Camera.main;
            float camD = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            if (camD > LodDistance)
            {
                lodDt += dt; lodSkip = (lodSkip + 1) % 2;
                if (lodSkip != 0) return;
                dt = Mathf.Min(0.2f, lodDt); lodDt = 0f;
            }
            else lodDt = 0f;
            bool near = camD < IKDistance;
            float now = Time.time;

            // ---- движение корня
            Vector3 pos = transform.position;
            Vector3 vel = (pos - lastPosV4) / dt; lastPosV4 = pos;
            float vy = (pos.y - lastY) / dt; lastY = pos.y;
            vySm = Mathf.Lerp(vySm, vy, 1f - Mathf.Exp(-10f * dt));
            vel.y = 0f;
            if (vel.sqrMagnitude > 400f) vel = Vector3.zero;              // телепорт
            velSm = Vector3.Lerp(velSm, vel, 1f - Mathf.Exp(-12f * dt));
            Vector3 lv = transform.InverseTransformDirection(velSm); lv.y = 0f;
            spdSm = Mathf.Lerp(spdSm, Mathf.Max(0f, moveSpeed), 1f - Mathf.Exp(-10f * dt));
            float spd = spdSm;
            float dirT = lv.magnitude > 0.3f && spd > 0.2f ? Mathf.Atan2(lv.x, lv.z) * Mathf.Rad2Deg : 0f;
            dirSm = Mathf.DeltaAngle(0f, Mathf.LerpAngle(dirSm, dirT, 1f - Mathf.Exp(-8f * dt)));
            float yaw = transform.eulerAngles.y;
            yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, 1f - Mathf.Exp(-6f * dt)); lastYaw = yaw;
            float acc1 = (spd - lastSpd) / dt; lastSpd = spd;

            sit = Mathf.MoveTowards(sit, sitTarget, dt * 1.25f);
            moveW = Mathf.MoveTowards(moveW, spd > 0.08f && sit < 0.01f ? 1f : 0f, dt * 4f);
            airW = Mathf.MoveTowards(airW, grounded || sit > 0.01f ? 0f : 1f, dt * (grounded ? 10f : 5f));
            if (grounded && !wasGrounded) landT = 0f;
            wasGrounded = grounded;
            landT += dt; plopT += dt;

            // ---- стоит: дыхание, перенос веса
            idleT += dt;
            AnimLib.Sample(cIdle, idleT, pz, out rootOff);
            float cL = 1f, cR = 1f;

            // ---- походка: по скорости и направлению, в одной фазе
            if (moveW > 0.001f)
            {
                float vW = cWalk.speed, vR = cRun.speed, vS = cSprint != null ? cSprint.speed : 99f, vJ = cJog != null ? cJog.speed : 0f;
                float amp = 1f, wW = 0f, wJ = 0f, wR = 0f, wS = 0f;
                if (spd <= vW) { wW = 1f; amp = Mathf.Clamp(Mathf.Sqrt(spd / vW), 0.3f, 1f); }
                else if (cJog != null && spd <= vJ) { float t = Smooth((spd - vW) / (vJ - vW)); wW = 1f - t; wJ = t; }
                else if (spd <= vR) { float v0 = cJog != null ? vJ : vW; float t = Smooth((spd - v0) / (vR - v0)); if (cJog != null) wJ = 1f - t; else wW = 1f - t; wR = t; }
                else { float t = Mathf.Clamp01((spd - vR) / (vS - vR)); wR = 1f - t; wS = t; }
                // направление: между двумя соседними из восьми клипов (если диагоналей нет — из четырёх)
                for (int k = 0; k < 8; k++) w8[k] = 0f;
                bool eight = true;
                for (int k = 0; k < 8; k++) if (walk8[k] == null || run8[k] == null) eight = false;
                float a360 = Mathf.Repeat(dirSm, 360f);
                if (eight)
                {
                    int i0 = Mathf.FloorToInt(a360 / 45f) % 8; float f = (a360 - i0 * 45f) / 45f;
                    w8[i0] = 1f - f; w8[(i0 + 1) % 8] += f;
                }
                else
                {
                    int i0 = Mathf.FloorToInt(a360 / 90f) % 4; float f = (a360 - i0 * 90f) / 90f;
                    w8[i0 * 2] = 1f - f; w8[((i0 + 1) % 4) * 2] += f;
                    if (walk8[2] == null || walk8[4] == null || walk8[6] == null) { for (int k = 0; k < 8; k++) w8[k] = 0f; w8[0] = 1f; }
                }
                if (cSprint == null) { wR += wS; wS = 0f; }
                else { wR += wS * (1f - w8[0]); wS *= w8[0]; }
                // трусца есть только вперёд: вбок и назад — пополам шагом и бегом
                if (wJ > 0f) { float side = wJ * (1f - w8[0]); wW += side * 0.5f; wR += side * 0.5f; wJ *= w8[0]; }
                for (int b = 0; b < acc.Length; b++) acc[b] = Vector4.zero;
                Vector3 gr = Vector3.zero; gaitVel = Vector2.zero;
                float T = 0f, vc = 0f, gl = 0f, grc = 0f;
                for (int k = 0; k < 8; k++)
                {
                    if (w8[k] <= 0f) continue;
                    GaitAdd(walk8[k], w8[k] * wW, ref T, ref vc, ref gr, ref gl, ref grc);
                    GaitAdd(run8[k], w8[k] * wR, ref T, ref vc, ref gr, ref gl, ref grc);
                }
                GaitAdd(cSprint, wS, ref T, ref vc, ref gr, ref gl, ref grc);
                GaitAdd(cJog, wJ, ref T, ref vc, ref gr, ref gl, ref grc);
                for (int b = 0; b < acc.Length; b++) gait[b] = AnimLib.Norm(acc[b]);
                vc = gaitVel.magnitude;   // по диагонали два клипа дают меньшую скорость ступни — фаза идёт быстрее
                // фаза: ступня в опоре едет назад ровно со скоростью персонажа
                float vEff = Mathf.Max(0.05f, vc * amp);
                gPhase = Mathf.Repeat(gPhase + dt * (spd / vEff) / Mathf.Max(0.2f, T), 1f);
                Blend(gait, gr, moveW * amp);
                cL = Mathf.Lerp(1f, gl, moveW); cR = Mathf.Lerp(1f, grc, moveW);
            }

            // ---- посадка
            if (sit > 0.001f || sitTarget > 0f)
            {
                Vector3 r;
                if (sit < 0.999f) AnimLib.Sample(cSit, sit * cSit.len, lay2, out r);
                else AnimLib.Sample(cSitIdle, idleT, lay2, out r);
                Blend(lay2, r, Smooth(sit * 6f));
                cL = cR = Mathf.Lerp(cL, 0f, Smooth(sit * 3f));
            }
            // ---- в воздухе и приземление
            if (airW > 0.001f && cAirUp != null && cAirDown != null)
            {
                Vector3 r1, r2;
                AnimLib.Sample(cAirUp, 0f, lay, out r1); AnimLib.Sample(cAirDown, 0f, lay2, out r2);
                float up = Smooth((vySm + 1.5f) / 4.5f);
                for (int b = 0; b < lay.Length; b++) lay2[b] = Quaternion.Slerp(lay2[b], lay[b], up);
                Blend(lay2, Vector3.Lerp(r2, r1, up), Smooth(airW));
                cL = Mathf.Lerp(cL, 0f, airW); cR = Mathf.Lerp(cR, 0f, airW);
            }
            if (cLand != null && landT < cLand.len && sit < 0.01f)
            {
                float w = 1f - Smooth(landT / cLand.len);
                if (moveW > 0.5f) rootOff.y -= 0.07f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(landT / 0.3f));   // на бегу — только пружинит
                else { Vector3 r; AnimLib.Sample(cLand, landT, lay2, out r); Blend(lay2, r, w * 0.9f); }
            }
            if (plopT < 0.4f) rootOff.y -= 0.035f * Mathf.Sin(Mathf.PI * plopT / 0.4f);

            // ---- действия верхом тела
            bool isTyping = typing || now < typingUntil;
            typeW = Mathf.MoveTowards(typeW, sit > 0.5f && (isTyping || handsOnDesk) ? 1f : 0f, dt * 4f);
            if (isTyping) typeT += dt;
            Layer(cType, typeT, typeW * Smooth((sit - 0.5f) * 2f));
            holdW = Mathf.MoveTowards(holdW, holdRight && !aimGun && sit < 0.5f ? 1f : 0f, dt * 6f);
            Layer(GuardClip() ?? cHold, 0f, holdW);
            aimW = Mathf.MoveTowards(aimW, aimGun && sit < 0.5f ? 1f : 0f, dt * 7f);
            Layer(HoldClip() ?? (twoHanded ? cAim2 : cAim1), 0f, aimW);
            if (actC != null)
            {
                float t = (now - actAt) * actSpeed;
                if (t >= actC.len) actC = null;
                else Layer(actC, t, Window(t, actC.len, 0.03f, 0.05f));
            }
            if (cWave != null && now < waveUntil) { float t = cWave.len - (waveUntil - now); Layer(cWave, t, Window(t, cWave.len)); }
            if (cSwing != null) { float t = now - swingStart; Layer(cSwing, t, Window(t, cSwing.len, 0.05f, 0.12f)); }
            if (cThrow != null) { float t = now - throwStart; Layer(cThrow, t, Window(t, cThrow.len, 0.06f, 0.15f)); }
            if (cHit != null) { float t = now - hitStart; Layer(cHit, t, Window(t, cHit.len, 0.03f, 0.2f)); }

            // ---- в руке предмет — пальцы обхватывают ручку
            if (gripRight && fingersR != null)
            {
                float g = 1f - Mathf.Max(holdW, aimW);
                for (int k = 0; k < fingersR.Length; k++)
                    if (fingersR[k] >= 0) pz[fingersR[k]] = Quaternion.Slerp(pz[fingersR[k]], Quaternion.AngleAxis(k < 12 ? 72f : 30f, k < 12 ? Vector3.back : new Vector3(0.87f, -0.5f, 0f)), g);
            }

            // ---- прицел: корпус наклоняется за камерой; отдача
            if (aimW > 0.001f && iSpine >= 0)
            {
                float p = Mathf.Clamp(aimPitch, -60f, 60f) * 0.75f / 3f * aimW;
                var q = Quaternion.AngleAxis(p, Vector3.right);
                pz[iSpine] = pz[iSpine] * q; pz[iSpine2] = pz[iSpine2] * q; pz[iTorso] = pz[iTorso] * q;
                float kick = Mathf.Max(0f, 1f - (now - kickAt) / 0.12f) * 8f;
                if (kick > 0f && iShR >= 0) pz[iShR] = Quaternion.AngleAxis(-kick, Vector3.right) * pz[iShR];
                if (kick > 0f && twoHanded && iShL >= 0) pz[iShL] = Quaternion.AngleAxis(-kick * 0.6f, Vector3.right) * pz[iShL];
            }

            // ---- наклон в поворот и при разгоне
            float bankT = Mathf.Clamp(yawRate * Mathf.Min(spd, 6f) * 0.012f, -11f, 11f) * moveW;
            bank = Mathf.Lerp(bank, bankT, 1f - Mathf.Exp(-6f * dt));
            pitchLean = Mathf.Lerp(pitchLean, Mathf.Clamp(acc1 * 1.1f, -6f, 8f) * moveW, 1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(bank) > 0.01f || Mathf.Abs(pitchLean) > 0.01f)
                pz[iHips] = Quaternion.AngleAxis(-bank, Vector3.forward) * Quaternion.AngleAxis(pitchLean, Vector3.right) * pz[iHips];

            // ---- взгляд
            if (lookAtPlayer && cam != null && iNeck >= 0 && iHead >= 0)
            {
                var to = cam.transform.position - (head != null ? head.position : transform.position); to.y = 0f;
                float want = 0f;
                if (to.magnitude < 5f && to.magnitude > 0.01f)
                    want = Mathf.Clamp(Mathf.DeltaAngle(0f, Quaternion.LookRotation(to).eulerAngles.y - yaw), -60f, 60f);
                lookYaw = Mathf.Lerp(lookYaw, want, 1f - Mathf.Exp(-5f * dt));
            }
            else lookYaw = Mathf.Lerp(lookYaw, 0f, 1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(lookYaw) > 0.05f && iNeck >= 0 && iHead >= 0)
            {
                pz[iNeck] = pz[iNeck] * Quaternion.AngleAxis(lookYaw * 0.4f, Vector3.up);
                pz[iHead] = pz[iHead] * Quaternion.AngleAxis(lookYaw * 0.6f, Vector3.up);
            }

            // ---- в кости
            for (int b = 0; b < vb.Length; b++) if (vb[b] != null) vb[b].localRotation = pz[b];
            hips.localPosition = hipsBase + rootOff;

            // ---- ступни на земле
            contact[0] = cL; contact[1] = cR;
            if (FootIK && near && grounded && sit < 0.05f && airW < 0.5f) FeetIK(dt, pos);
            else { feet[0].planted = feet[1].planted = false; feet[0].stepping = feet[1].stepping = false; feet[0].lockW = feet[1].lockW = 0f; gs[0] = gs[1] = 0f; }

            ReactTick();
            FaceTick(dt);
        }

        float lookYaw;

        // Поза с конкретным оружием (клип hold_<оружие> из Art/grips_v1.py) вместо общего прицела
        public string holdClip;
        string holdName; AnimClip holdC;
        AnimClip HoldClip()
        {
            if (string.IsNullOrEmpty(holdClip)) return null;
            if (holdClip != holdName) { holdName = holdClip; holdC = AnimLib.Get(holdClip); }
            return holdC;
        }

        // Стойка с холодным оружием (клип mguard_<оружие> из Art/melee_v1.py) вместо общего «держит в руке»
        public string guardClip;
        string guardName; AnimClip guardC;
        AnimClip GuardClip()
        {
            if (string.IsNullOrEmpty(guardClip)) return null;
            if (guardClip != guardName) { guardName = guardClip; guardC = AnimLib.Get(guardClip); }
            return guardC;
        }

        // Действие клипом поверх позы (удар холодным оружием m_<оружие>_<n>): за len секунд, начало и конец — стойка
        AnimClip actC; float actAt = -9f, actSpeed = 1f;
        public bool PlayAction(string clip, float len)
        {
            actC = v4 ? AnimLib.Get(clip) : null;
            actAt = Time.time; actSpeed = actC != null && len > 0.01f && actC.len > 0.01f ? actC.len / len : 1f;
            return actC != null;
        }
        public void StopAction() { actC = null; }
        public bool Acting { get { return actC != null; } }

        Vector2 gaitVel;   // скорость земли под опорной ногой в смеси клипов (направление × скорость)
        void GaitAdd(AnimClip c, float w, ref float T, ref float vc, ref Vector3 root, ref float cl, ref float cr)
        {
            if (c == null || w <= 0.001f) return;
            float t = gPhase * c.len;
            AnimLib.Accumulate(c, t, acc, ref root, w);
            float a = c.dir * Mathf.Deg2Rad;
            T += c.len * w; gaitVel += new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * c.speed * w;
            cl += c.Contact(true, t) * w; cr += c.Contact(false, t) * w;
        }

        // ---------- ступни ----------
        static readonly RaycastHit[] hits = new RaycastHit[6];

        float GroundAt(Vector3 p, float rootY)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, rootY + 0.55f, p.z), Vector3.down, hits, 1.0f, Physics.DefaultRaycastLayers & ~(1 << 2), QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.normal.y < 0.6f || h.collider.attachedRigidbody != null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<CharacterAnim>() != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            if (float.IsNegativeInfinity(best)) return 0f;
            return Mathf.Clamp(best - rootY, -0.3f, 0.3f);
        }

        void FeetIK(float dt, Vector3 pos)
        {
            var legs = new[] { legL, legR }; var knees = new[] { kneeL, kneeR };
            var feetT = new Transform[2];
            for (int i = 0; i < 2; i++) feetT[i] = knees[i] != null && knees[i].childCount > 0 ? FootOf(knees[i]) : null;
            if (feetT[0] == null || feetT[1] == null) return;
            bool moving = moveW > 0.25f;
            // высота земли под ступнями и таз ниже, если одна нога ниже
            for (int i = 0; i < 2; i++)
            {
                float g = GroundAt(feetT[i].position, pos.y);
                gs[i] = Mathf.Lerp(gs[i], g, 1f - Mathf.Exp(-14f * dt));
            }
            float drop = Mathf.Min(0f, Mathf.Min(gs[0], gs[1]));
            if (drop < -0.001f) hips.position += Vector3.up * drop;
            for (int i = 0; i < 2; i++)
            {
                var ft = feetT[i]; var fs = feet[i];
                Vector3 fk = ft.position + Vector3.up * (gs[i] - drop);   // таз опустили на drop — ступня встаёт на свою землю
                Quaternion fkRot = ft.rotation;
                float fkYaw = fkRot.eulerAngles.y;
                Vector3 fwd = fkRot * Vector3.forward;
                float pitch = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
                int piv = pitch > 2f ? 0 : 1;
                Vector3 pw = fk + fkRot * (piv == 0 ? HeelPt : BallPt);
                float c = contact[i];
                Vector3 pole = knees[i].position - 0.5f * (legs[i].position + ft.position);
                if (fs.stepping)
                {
                    fs.stepT += dt / 0.3f;
                    float s = Smooth(fs.stepT);
                    Vector3 want = Vector3.Lerp(fs.stepFrom, fk, s) + Vector3.up * (0.07f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(fs.stepT)));
                    float y = Mathf.LerpAngle(fs.lockYaw, fkYaw, s);
                    SolveLeg(legs[i], knees[i], ft, want, pole, Quaternion.AngleAxis(Mathf.DeltaAngle(fkYaw, y), Vector3.up) * fkRot);
                    if (fs.stepT >= 1f) { fs.stepping = false; fs.planted = true; fs.piv = piv; fs.pivW = pw; fs.delta = Vector3.zero; fs.lockYaw = fkYaw; fs.lockW = 1f; }
                    continue;
                }
                // касание: держим сразу (поправка пока нулевая — рывка нет)
                // касание: по клипу (опора > 0.5) или когда подошва уже коснулась земли, а клип вот-вот скажет «опора»
                bool touch = c > 0.5f || (c > 0.15f && pw.y - (pos.y + gs[i]) < 0.012f);
                if (touch && !fs.planted) { fs.planted = true; fs.piv = piv; fs.pivW = pw; fs.delta = Vector3.zero; fs.lockYaw = fkYaw; fs.lockW = 1f; }
                if (c < 0.45f && c <= fs.lastC) fs.planted = false;   // отрыв — когда опора убывает
                fs.lastC = c;
                if (fs.planted)
                {
                    if (fs.piv != piv) { fs.pivW = pw + fs.delta; fs.piv = piv; }     // опора перешла с пятки на подушечку — без рывка
                    Vector3 d = fs.pivW - pw; d.y = 0f;
                    float drift = d.magnitude;
                    float yawOff = Mathf.Abs(Mathf.DeltaAngle(fs.lockYaw, fkYaw));
                    var other = feet[1 - i];
                    if (!moving && (drift > 0.15f || yawOff > 36f) && other.planted && !other.stepping && Time.time > nextStepAt)
                    {
                        // на месте ушли далеко или развернулись — переступить
                        fs.stepping = true; fs.stepT = 0f; fs.stepFrom = fk + d * fs.lockW; fs.planted = false; nextStepAt = Time.time + 0.18f;
                        fs.lockYaw = Mathf.LerpAngle(fkYaw, fs.lockYaw, fs.lockW);
                        continue;
                    }
                    float max = moving ? 0.15f : 0.3f;
                    if (drift > max) { d = d / drift * max; fs.pivW = pw + d; }
                    fs.delta = d;
                }
                fs.lockW = Mathf.MoveTowards(fs.lockW, fs.planted ? 1f : 0f, dt * 7f);
                if (fs.lockW <= 0.001f && Mathf.Abs(gs[i]) < 0.002f && drop > -0.001f) continue;
                Vector3 tgt = fk + fs.delta * fs.lockW;
                Quaternion rot = Quaternion.AngleAxis(Mathf.DeltaAngle(fkYaw, fs.lockYaw) * fs.lockW, Vector3.up) * fkRot;
                SolveLeg(legs[i], knees[i], ft, tgt, pole, rot);
            }
        }
        float nextStepAt;

        static Transform FootOf(Transform knee)
        {
            foreach (Transform c in knee) if (ModelLib.Clean(c.name) == "FootL" || ModelLib.Clean(c.name) == "FootR") return c;
            return null;
        }

        // Нога из двух костей: колено гнётся так, чтобы голеностоп попал в target, в сторону pole (как было в анимации)
        void SolveLeg(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, Quaternion footRot)
        {
            float la = legLA, lb = legLB;
            Vector3 pa = a.position;
            Vector3 toT = target - pa; float d = toT.magnitude;
            float maxL = (la + lb) * 0.999f;
            if (d > maxL) { target = pa + toT / d * maxL; d = maxL; }
            d = Mathf.Max(d, Mathf.Abs(la - lb) + 0.001f);
            Vector3 pb = b.position, pc = c.position;
            float cur = Vector3.Angle(pa - pb, pc - pb);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(pa - pb, pc - pb);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(pa - pb, pole);
            if (axis.sqrMagnitude < 1e-8f) axis = transform.right;
            axis.Normalize();
            b.rotation = Quaternion.AngleAxis(want - cur, axis) * b.rotation;
            pc = c.position;
            a.rotation = Quaternion.FromToRotation(pc - pa, target - pa) * a.rotation;
            Vector3 ax = (target - pa).normalized;
            Vector3 kn = Vector3.ProjectOnPlane(b.position - pa, ax), pl = Vector3.ProjectOnPlane(pole, ax);
            if (kn.sqrMagnitude > 1e-6f && pl.sqrMagnitude > 1e-6f)
                a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(kn, pl, ax), ax) * a.rotation;
            c.rotation = footRot;
        }

        public void Flinch() { hitStart = Time.time; }
        // после ragdoll и выдачи из пула — ступни заново встают на землю
        void ResetV4()
        {
            for (int i = 0; i < 2; i++) { feet[i].planted = false; feet[i].stepping = false; feet[i].lockW = 0f; gs[i] = 0f; }
            lastPosV4 = transform.position; lastY = transform.position.y; velSm = Vector3.zero;
        }
        public void Throw() { throwStart = Time.time; }
    }
}
