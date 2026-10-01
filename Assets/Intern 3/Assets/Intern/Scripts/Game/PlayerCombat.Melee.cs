// Ближний бой (спринт 8 версии 0.9 «Оружие в руках», задача 2): у ножа, биты и катаны — серии из 3–5 ударов.
// Каждый удар — путь кулака и направление клинка по ключам (от правого плеча, в осях персонажа), поворот корпуса,
// своя дуга, урон, отбрасывание и момент попадания. Клик во время удара — следующий удар серии, пауза — серия сначала.
// Руки ставит тот же IK, что и у огнестрела; двуручные (бита, катана) держит и левая. За клинком — след.
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public class MeleeMove
    {
        public string name;
        public float len, hitAt, dmg = 1f, knock, stun, arc = 60f, reachAdd; public int targets = 1;
        public Vector3 side;                 // куда толкает (оси игрока): справа налево — влево
        public float[] ku; public Vector3[] kp, kb, ke;    // ключи: время, кулак, клинок, сторона лезвия
        public float[] tu, tw, lean;          // поворот корпуса и наклон вперёд
    }

    public partial class PlayerCombat
    {
        static Dictionary<string, MeleeMove[]> moves;
        static MeleeMove M(string name, float len, float hitAt, float dmg, float arc, int targets, Vector3 side, float[] ku, Vector3[] kp, Vector3[] kb, Vector3[] ke, float[] tu, float[] tw, float[] lean = null, float knock = 0f, float stun = 0f, float reachAdd = 0f)
        {
            return new MeleeMove { name = name, len = len, hitAt = hitAt, dmg = dmg, arc = arc, targets = targets, side = side, ku = ku, kp = kp, kb = kb, ke = ke, tu = tu, tw = tw, lean = lean, knock = knock, stun = stun, reachAdd = reachAdd };
        }
        static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }

        // Стойка: кулак, клинок, сторона лезвия (оси персонажа, от правого плеча)
        static Vector3 GuardP(string id) { return id == "bat" ? V(-0.08f, -0.22f, 0.2f) : id == "katana" ? V(-0.2f, -0.34f, 0.32f) : V(-0.07f, -0.2f, 0.3f); }
        static Vector3 GuardB(string id) { return id == "bat" ? V(0.5f, 0.62f, -0.6f) : id == "katana" ? V(0f, 0.5f, 0.87f) : V(0.05f, 0.45f, 0.89f); }
        static Vector3 GuardE(string id) { return id == "bat" ? V(0f, 0f, 1f) : id == "katana" ? V(0f, -0.87f, 0.5f) : V(0f, -0.89f, 0.45f); }
        // от первого лица всё выше и ближе к центру кадра
        static Vector3 FpShift(string id) { return id == "bat" ? V(0f, 0.14f, 0.12f) : id == "katana" ? V(0f, 0.32f, 0.06f) : V(-0.02f, 0.24f, 0.1f); }

        static MeleeMove[] Moves(string id)
        {
            if (moves == null)
            {
                moves = new Dictionary<string, MeleeMove[]>();
                var gK = GuardP("knife"); var bK = GuardB("knife"); var eK = GuardE("knife");
                moves["knife"] = new[]
                {
                    M("справа налево", 0.3f, 0.52f, 1f, 60f, 1, Vector3.left,
                      new[] { 0f, 0.28f, 0.52f, 0.78f, 1f },
                      new[] { gK, V(0.12f, -0.12f, 0.2f), V(-0.12f, -0.18f, 0.5f), V(-0.45f, -0.22f, 0.28f), gK },
                      new[] { bK, V(0.75f, 0.25f, 0.6f), V(0.15f, 0.1f, 1f), V(-0.8f, 0f, 0.55f), bK },
                      new[] { eK, V(-0.6f, 0f, 0.75f), V(-1f, 0f, 0.1f), V(-0.55f, 0f, -0.8f), eK },
                      new[] { 0f, 0.28f, 0.52f, 0.78f, 1f }, new[] { 0f, 25f, -5f, -25f, 0f }),
                    M("слева направо", 0.3f, 0.52f, 1f, 60f, 1, Vector3.right,
                      new[] { 0f, 0.28f, 0.52f, 0.78f, 1f },
                      new[] { gK, V(-0.42f, -0.15f, 0.22f), V(-0.1f, -0.18f, 0.5f), V(0.15f, -0.22f, 0.3f), gK },
                      new[] { bK, V(-0.75f, 0.2f, 0.6f), V(-0.1f, 0.1f, 1f), V(0.8f, 0f, 0.55f), bK },
                      new[] { eK, V(0.6f, 0f, 0.75f), V(1f, 0f, 0.1f), V(0.55f, 0f, -0.8f), eK },
                      new[] { 0f, 0.28f, 0.52f, 0.78f, 1f }, new[] { 0f, -25f, 0f, 20f, 0f }),
                    M("укол", 0.4f, 0.5f, 1.6f, 25f, 1, Vector3.forward,
                      new[] { 0f, 0.3f, 0.5f, 0.72f, 1f },
                      new[] { gK, V(-0.02f, -0.3f, 0.05f), V(-0.12f, -0.2f, 0.6f), V(-0.12f, -0.2f, 0.58f), gK },
                      new[] { bK, V(0f, 0.15f, 1f), V(-0.05f, 0.05f, 1f), V(-0.05f, 0.05f, 1f), bK },
                      new[] { eK, V(0f, -1f, 0.15f), V(0f, -1f, 0f), V(0f, -1f, 0f), eK },
                      new[] { 0f, 0.3f, 0.5f, 0.72f, 1f }, new[] { 0f, 15f, -20f, -15f, 0f }, new[] { 0f, 0f, 8f, 6f, 0f }, 0f, 0f, 0.3f),
                };
                var gB = GuardP("bat"); var bB = GuardB("bat"); var eB = GuardE("bat");
                moves["bat"] = new[]
                {
                    M("наотмашь справа", 0.62f, 0.58f, 1f, 75f, 2, Vector3.left,
                      new[] { 0f, 0.35f, 0.48f, 0.58f, 0.72f, 0.86f, 1f },
                      new[] { gB, V(0.06f, -0.04f, 0.0f), V(0.02f, -0.16f, 0.22f), V(-0.2f, -0.2f, 0.42f), V(-0.42f, -0.18f, 0.3f), V(-0.42f, -0.06f, 0.18f), gB },
                      new[] { bB, V(0.6f, 0.5f, -0.62f), V(1f, 0.12f, 0.15f), V(0.15f, 0.05f, 1f), V(-1f, 0.08f, 0.35f), V(-0.8f, 0.45f, 0.1f), bB },
                      new[] { eB, V(-0.2f, 0f, 1f), V(0f, 0f, 1f), V(-1f, 0f, 0f), V(-0.3f, 0f, -1f), V(0.1f, 0f, -1f), eB },
                      new[] { 0f, 0.35f, 0.58f, 0.72f, 0.86f, 1f }, new[] { 0f, 35f, -5f, -30f, -35f, 0f }, null, 1.6f, 0.8f),
                    M("наотмашь слева", 0.62f, 0.58f, 1f, 75f, 2, Vector3.right,
                      new[] { 0f, 0.35f, 0.48f, 0.58f, 0.72f, 0.86f, 1f },
                      new[] { gB, V(-0.45f, -0.06f, 0.12f), V(-0.4f, -0.16f, 0.28f), V(-0.15f, -0.2f, 0.44f), V(0.04f, -0.18f, 0.32f), V(0.06f, -0.06f, 0.18f), gB },
                      new[] { bB, V(-0.7f, 0.5f, -0.5f), V(-1f, 0.12f, 0.15f), V(0f, 0.05f, 1f), V(1f, 0.08f, 0.35f), V(0.8f, 0.45f, 0.1f), bB },
                      new[] { eB, V(0.2f, 0f, 1f), V(0f, 0f, 1f), V(1f, 0f, 0f), V(0.3f, 0f, -1f), V(-0.1f, 0f, -1f), eB },
                      new[] { 0f, 0.35f, 0.58f, 0.72f, 0.86f, 1f }, new[] { 0f, -35f, 5f, 28f, 32f, 0f }, null, 1.6f, 0.8f),
                    M("сверху", 0.85f, 0.62f, 1.9f, 35f, 1, Vector3.down,
                      new[] { 0f, 0.4f, 0.62f, 0.8f, 1f },
                      new[] { gB, V(-0.15f, 0.32f, 0f), V(-0.2f, -0.22f, 0.45f), V(-0.2f, -0.42f, 0.32f), gB },
                      new[] { bB, V(0f, 0.35f, -0.94f), V(0f, -0.55f, 0.83f), V(0f, -0.95f, 0.3f), bB },
                      new[] { eB, V(0f, 0.94f, 0.35f), V(0f, -1f, 0f), V(0f, -0.3f, -0.95f), eB },
                      new[] { 0f, 0.4f, 0.62f, 1f }, new[] { 0f, 10f, -5f, 0f }, new[] { 0f, -8f, 18f, 0f }, 1.2f, 1.6f),
                };
                var gT = GuardP("katana"); var bT = GuardB("katana"); var eT = GuardE("katana");
                moves["katana"] = new[]
                {
                    M("кэса", 0.45f, 0.52f, 1f, 70f, 3, Vector3.left,
                      new[] { 0f, 0.3f, 0.52f, 0.75f, 1f },
                      new[] { gT, V(0f, 0.22f, 0.05f), V(-0.25f, -0.18f, 0.48f), V(-0.48f, -0.42f, 0.25f), gT },
                      new[] { bT, V(0.4f, 0.7f, -0.55f), V(-0.3f, -0.15f, 0.94f), V(-0.75f, -0.55f, 0.35f), bT },
                      new[] { eT, V(0f, 1f, 0f), V(-0.6f, -0.75f, 0f), V(-0.3f, -0.5f, -0.8f), eT },
                      new[] { 0f, 0.3f, 0.52f, 0.75f, 1f }, new[] { 0f, 25f, -10f, -30f, 0f }),
                    M("снизу вверх", 0.45f, 0.52f, 1f, 70f, 3, Vector3.right,
                      new[] { 0f, 0.3f, 0.52f, 0.75f, 1f },
                      new[] { gT, V(-0.48f, -0.45f, 0.2f), V(-0.2f, -0.15f, 0.48f), V(0.05f, 0.15f, 0.25f), gT },
                      new[] { bT, V(-0.6f, -0.5f, 0.6f), V(0.1f, 0.3f, 0.95f), V(0.6f, 0.7f, 0.35f), bT },
                      new[] { eT, V(0.6f, 0.75f, 0f), V(0.6f, 0.75f, 0f), V(0.3f, 0.6f, -0.7f), eT },
                      new[] { 0f, 0.3f, 0.52f, 0.75f, 1f }, new[] { 0f, -30f, 5f, 25f, 0f }),
                    M("горизонтально", 0.45f, 0.52f, 1.1f, 75f, 3, Vector3.left,
                      new[] { 0f, 0.32f, 0.52f, 0.75f, 1f },
                      new[] { gT, V(0.05f, -0.08f, 0.05f), V(-0.2f, -0.2f, 0.48f), V(-0.48f, -0.2f, 0.25f), gT },
                      new[] { bT, V(0.7f, 0.2f, -0.65f), V(0.1f, 0.05f, 1f), V(-1f, 0.05f, 0.1f), bT },
                      new[] { eT, V(0f, 0f, 1f), V(-1f, 0f, 0f), V(0f, 0f, -1f), eT },
                      new[] { 0f, 0.32f, 0.52f, 0.75f, 1f }, new[] { 0f, 35f, 0f, -35f, 0f }),
                    M("сверху вниз", 0.62f, 0.6f, 1.6f, 35f, 2, Vector3.down,
                      new[] { 0f, 0.38f, 0.6f, 0.8f, 1f },
                      new[] { gT, V(-0.2f, 0.35f, 0.02f), V(-0.2f, -0.15f, 0.5f), V(-0.2f, -0.4f, 0.42f), gT },
                      new[] { bT, V(0f, 0.6f, -0.8f), V(0f, -0.2f, 1f), V(0f, -0.7f, 0.7f), bT },
                      new[] { eT, V(0f, 0.8f, 0.6f), V(0f, -1f, -0.2f), V(0f, -0.7f, -0.7f), eT },
                      new[] { 0f, 0.38f, 0.6f, 1f }, new[] { 0f, 5f, -5f, 0f }, new[] { 0f, -6f, 14f, 0f }, 0.4f, 0.6f),
                    M("укол", 0.52f, 0.5f, 1.4f, 25f, 2, Vector3.forward,
                      new[] { 0f, 0.3f, 0.5f, 0.72f, 1f },
                      new[] { gT, V(-0.12f, -0.32f, 0.02f), V(-0.2f, -0.2f, 0.62f), V(-0.2f, -0.2f, 0.6f), gT },
                      new[] { bT, V(0f, 0.12f, 1f), V(0f, 0.05f, 1f), V(0f, 0.05f, 1f), bT },
                      new[] { eT, V(0f, -1f, 0.12f), V(0f, -1f, 0f), V(0f, -1f, 0f), eT },
                      new[] { 0f, 0.3f, 0.5f, 0.72f, 1f }, new[] { 0f, 15f, -15f, -10f, 0f }, new[] { 0f, 0f, 10f, 8f, 0f }, 0.3f, 0f, 0.5f),
                };
            }
            MeleeMove[] r; return moves.TryGetValue(id ?? "", out r) ? r : moves["knife"];
        }

        // Для самопроверки: ударов в серии и ошибки в ключах
        public static int DevCheckMoves(string id, List<string> bad)
        {
            var ms = Moves(id);
            foreach (var m in ms)
            {
                string w = id + " «" + m.name + "»: ";
                if (m.ku.Length != m.kp.Length || m.ku.Length != m.kb.Length || m.ku.Length != m.ke.Length) bad.Add(w + "ключи разной длины");
                for (int i = 1; i < m.ku.Length; i++) if (m.ku[i] <= m.ku[i - 1]) bad.Add(w + "время ключей не растёт");
                if (m.hitAt <= 0.1f || m.hitAt >= 0.95f) bad.Add(w + "попадание вне удара");
                if (m.tu.Length != m.tw.Length || (m.lean != null && m.lean.Length != m.tu.Length)) bad.Add(w + "ключи корпуса разной длины");
                foreach (var b in m.kb) if (b.sqrMagnitude < 0.01f) bad.Add(w + "нулевое направление клинка");
            }
            return ms.Length;
        }

        // ---------- серия ----------
        int comboIdx = -1; float strikeAt = -9f, strikeEnd = -9f; bool strikeQueued, strikeHit, strikeSwoosh;
        WeaponTrail trail;
        public int DevCombo { get { return comboIdx; } }

        MeleeMove CurMove { get { var ms = Moves(Current.id); return comboIdx >= 0 && comboIdx < ms.Length ? ms[comboIdx] : null; } }
        float StrikeU { get { var m = CurMove; return m != null ? (Time.time - strikeAt) / m.len : 9f; } }
        bool Striking { get { return CurMove != null && StrikeU < 1f; } }

        // Клик: первый удар, следующий в серии или в очередь
        void MeleeClick(WeaponDef def)
        {
            if (KnifeAway) return;
            if (Striking) { if (StrikeU > 0.3f) strikeQueued = true; return; }
            var ms = Moves(def.id);
            int next = Time.time - strikeEnd < 0.4f && comboIdx >= 0 ? (comboIdx + 1) % ms.Length : 0;
            StartStrike(def, next);
        }

        void StartStrike(WeaponDef def, int idx)
        {
            comboIdx = idx; strikeAt = Time.time; strikeQueued = false; strikeHit = false; strikeSwoosh = false;
            player.FaceYaw(player.CamYaw);
            if (Av != null) Av.swingStart = -9f;
        }

        // Ход серии: момент попадания, переход к следующему удару
        void MeleeTick(WeaponDef def)
        {
            var m = CurMove; if (m == null) return;
            float u = StrikeU;
            if (!strikeSwoosh && u >= m.hitAt - 0.2f) { strikeSwoosh = true; Sfx.Play(def.id == "bat" ? "swoosh_heavy" : "swoosh", player.Position + Vector3.up * 1.3f, 0.6f, 0.1f, 0.5f); }
            if (!strikeHit && u >= m.hitAt) { strikeHit = true; MeleeStrikeHit(def, m); }
            if (u >= 1f && strikeEnd < strikeAt)
            {
                strikeEnd = Time.time;
                if (strikeQueued) StartStrike(def, (comboIdx + 1) % Moves(def.id).Length);
            }
        }

        // Попадание по дуге: до m.targets горожан, ближние первыми
        void MeleeStrikeHit(WeaponDef def, MeleeMove m)
        {
            if (run == null) return;
            var rot = Quaternion.Euler(0, player.CamYaw, 0);
            var fwd = rot * Vector3.forward;
            float reach = def.reach + m.reachAdd;
            var c = player.Position + Vector3.up * 1.0f + fwd * (reach * 0.5f);
            var found = new List<KeyValuePair<float, CityNpc>>();
            var seen = new HashSet<CityNpc>();
            foreach (var col in Physics.OverlapSphere(c, reach * 0.5f + 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var n = col.GetComponentInParent<CityNpc>();
                if (n == null || !n.Alive || !seen.Add(n)) continue;
                var to = n.transform.position - player.Position; to.y = 0;
                float d = to.magnitude;
                if (d > reach + 0.45f) continue;
                if (d > 0.5f && Vector3.Angle(fwd, to) > m.arc) continue;
                found.Add(new KeyValuePair<float, CityNpc>(d, n));
            }
            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            var push = (rot * m.side).normalized;
            for (int i = 0; i < found.Count && i < m.targets; i++)
            {
                var n = found[i].Value;
                float knock = Mathf.Max(def.knockback * (m.knock > 0f ? 1f : 0.3f), m.knock);
                float stun = Mathf.Max(m.stun, def.id == "bat" && ars.Perk(def) ? 2f : 0f);
                var dir = (fwd * 0.6f + push * 0.8f).normalized;
                var at = n.transform.position + Vector3.up * (m.side == Vector3.down ? 1.62f : 1.25f) - push * 0.18f;
                n.Hit(Mathf.RoundToInt(ars.Damage(def) * m.dmg), player.Position, at, dir, knock, stun);
                if (i == 0) Sfx.Play(def.id == "bat" ? "hit_bat" : "hit_blade", at, 0.9f);
            }
        }

        // ---------- поза: стойка и удар (IK), след клинка ----------
        void MeleePose(float dt)
        {
            var w = meleeModel; if (w == null || !w.gameObject.activeInHierarchy || Av == null || !Av.v4 || Av.handR == null) { if (fpArms != null) fpArms.Show(false); if (trail != null) trail.Emit(false); return; }
            var def = Current; string id = def.id;
            var camT = player.cam.transform;
            bool fp = player.firstPerson;
            float s = Av.transform.lossyScale.y;
            var m = Striking ? CurMove : null;
            float u = m != null ? Mathf.Clamp01(StrikeU) : 0f;
            Vector3 p = GuardP(id), b = GuardB(id), e = GuardE(id); float twist = 0f, lean = 0f;
            if (m != null)
            {
                Key3(m.ku, m.kp, u, ref p); Key3(m.ku, m.kb, u, ref b); Key3(m.ku, m.ke, u, ref e);
                twist = Key1(m.tu, m.tw, u); if (m.lean != null) lean = Key1(m.tu, m.lean, u);
            }
            holdW = Mathf.MoveTowards(holdW, 1f, dt * 6f);
            Av.AimStance(twist * (fp ? 0.3f : 1f), camT.forward, -lean, 0f, holdW);
            Transform hand; Quaternion frame; Vector3 shoulder;
            if (fp && FpReady())
            {
                fpArms.Show(true);
                fpArms.Place(camT, new Vector3(0f, -0.25f, -0.05f) * s, twist * 0.3f);
                frame = camT.rotation; shoulder = fpArms.ShoulderR; hand = fpArms.HandR;
                p += FpShift(id);
            }
            else
            {
                if (fpArms != null) fpArms.Show(false);
                frame = Av.transform.rotation; shoulder = Av.armR.position; hand = Av.handR;
            }
            // оружие — в кулаке той руки, что сейчас в кадре
            if (w.transform.parent != hand)
            {
                var lp = w.transform.localPosition; var lr = w.transform.localRotation;
                w.transform.SetParent(hand, false); w.transform.localPosition = lp; w.transform.localRotation = lr;
            }
            Vector3 bw = frame * b.normalized;
            Vector3 target = shoulder + frame * (p * s);
            // от третьего лица клинок и кулак не входят в тело: выталкиваем из капсул корпуса, головы и ног
            if (!fp) KeepOutOfBody(w, ref target, ref bw, s);
            Vector3 ew = Vector3.ProjectOnPlane(frame * e, bw);
            if (ew.sqrMagnitude < 1e-4f) ew = frame * Vector3.down;
            Quaternion hr = Quaternion.LookRotation(bw, -ew.normalized);
            Vector3 poleR = frame * new Vector3(0.8f, -1f, -0.3f);
            if (fp) fpArms.IK(true, target, hr, poleR, 0.4f); else Av.ArmIK(true, target, hr, poleR, 1f, 0.4f);
            bool two = w.gripL != null && id != "knife";
            if (two)
            {
                Vector3 poleL = frame * new Vector3(-0.6f, -1f, -0.2f);
                if (fp) fpArms.IK(false, w.gripL.position, hr, poleL, 0.4f); else Av.ArmIK(false, w.gripL.position, hr, poleL, 1f, 0.4f);
            }
            // пальцы обхватывают рукоять (кулак не сжат намертво — рукоять внутри)
            if (fp) { fpArms.Fingers(true, 55f, 55f, 40f); if (two) fpArms.Fingers(false, 55f, 55f, 40f); }
            else { Av.Fingers(true, 55f, 55f, 40f); if (two) Av.Fingers(false, 55f, 55f, 40f); }
            // след: от середины клинка до острия, пока идёт сам удар
            if (trail == null || trail.Weapon != w) { if (trail != null) trail.Destroy(); trail = WeaponTrail.For(w, id); }
            bool cut = m != null && u > m.hitAt - 0.22f && u < m.hitAt + 0.2f;
            if (trail != null) trail.Emit(cut);
        }

        // Капсулы тела: корпус (таз — шея), голова (шар), ноги (таз — колени); запас — толщина оружия
        void KeepOutOfBody(WeaponModel w, ref Vector3 grip, ref Vector3 dir, float s)
        {
            if (Av.hips == null || Av.neck == null || Av.head == null || w.grip == null) return;
            var tip = w.tip ?? w.muzzle; if (tip == null) return;
            float len = Vector3.Distance(w.transform.TransformPoint(w.Local(tip)), w.transform.TransformPoint(w.Local(w.grip)));
            Vector3 up = Av.transform.up;
            Vector3 t0 = Av.hips.position + up * 0.05f * s, t1 = Av.neck.position;
            Vector3 hc = Av.head.position + Av.head.up * 0.13f * s + Av.head.forward * 0.03f * s;
            Vector3 l1 = Av.kneeL != null && Av.kneeR != null ? (Av.kneeL.position + Av.kneeR.position) * 0.5f - up * 0.1f * s : Av.hips.position - up * 0.5f * s;
            float rT = 0.17f * s, rH = 0.18f * s, rL = 0.17f * s, m = 0.04f * s;
            // кулак — не в груди
            {
                Vector3 c = Closest(t0, t1, grip); float d = Vector3.Distance(grip, c), need = rT + 0.05f * s;
                if (d < need && d > 1e-4f) grip = c + (grip - c) / d * need;
            }
            for (int it = 0; it < 6; it++)
            {
                float worst = 0f; Vector3 wp = Vector3.zero, wn = Vector3.zero;
                for (int k = 1; k <= 10; k++)
                {
                    Vector3 pt = grip + dir * (len * k / 10f);
                    Probe(pt, Closest(t0, t1, pt), rT + m, ref worst, ref wp, ref wn);
                    Probe(pt, hc, rH + m, ref worst, ref wp, ref wn);
                    Probe(pt, Closest(Av.hips.position, l1, pt), rL + m, ref worst, ref wp, ref wn);
                }
                if (worst < 0.002f) break;
                Vector3 from = wp - grip, to = from + wn * (worst + 0.01f);
                dir = (Quaternion.FromToRotation(from, to) * dir).normalized;
            }
        }

        static void Probe(Vector3 pt, Vector3 c, float r, ref float worst, ref Vector3 wp, ref Vector3 wn)
        {
            Vector3 d = pt - c; float dist = d.magnitude, pen = r - dist;
            if (pen > worst && dist > 1e-4f) { worst = pen; wp = pt; wn = d / dist; }
        }

        static Vector3 Closest(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        static void Key3(float[] ku, Vector3[] kv, float u, ref Vector3 v)
        {
            if (ku == null || kv == null || kv.Length == 0) return;
            if (u <= ku[0]) { v = kv[0]; return; }
            for (int i = 0; i + 1 < ku.Length && i + 1 < kv.Length; i++)
                if (u <= ku[i + 1]) { float t = S01((u - ku[i]) / Mathf.Max(1e-4f, ku[i + 1] - ku[i])); v = Vector3.Lerp(kv[i], kv[i + 1], t); return; }
            v = kv[Mathf.Min(ku.Length, kv.Length) - 1];
        }

        static float Key1(float[] ku, float[] kv, float u)
        {
            if (ku == null || kv == null || kv.Length == 0) return 0f;
            if (u <= ku[0]) return kv[0];
            for (int i = 0; i + 1 < ku.Length && i + 1 < kv.Length; i++)
                if (u <= ku[i + 1]) return Mathf.Lerp(kv[i], kv[i + 1], S01((u - ku[i]) / Mathf.Max(1e-4f, ku[i + 1] - ku[i])));
            return kv[Mathf.Min(ku.Length, kv.Length) - 1];
        }
    }

    // След клинка: лента между серединой лезвия и остриём за последние ~0,12 с, гаснет к хвосту
    public class WeaponTrail
    {
        public WeaponModel Weapon;
        Transform a, b; GameObject go; Mesh mesh; MeshRenderer mr;
        readonly List<Vector3> pa = new List<Vector3>(), pb = new List<Vector3>(); readonly List<float> pt = new List<float>();
        bool on; Color col; const float Life = 0.13f;

        public static WeaponTrail For(WeaponModel w, string id)
        {
            if (w == null) return null;
            var tip = w.tip ?? w.muzzle; if (tip == null) return null;
            var t = new WeaponTrail { Weapon = w, b = tip };
            var mid = new GameObject("TrailBase").transform; mid.SetParent(w.transform, false);
            Vector3 g = w.grip != null ? w.Local(w.grip) : Vector3.zero, tp = w.Local(tip);
            mid.localPosition = Vector3.Lerp(g, tp, id == "knife" ? 0.45f : 0.4f);
            t.a = mid;
            t.col = id == "bat" ? new Color(1f, 1f, 1f, 0.22f) : new Color(0.85f, 0.92f, 1f, 0.55f);
            t.go = new GameObject("WeaponTrail"); t.go.layer = 2;
            t.mesh = new Mesh { name = "trail" }; t.mesh.MarkDynamic();
            t.go.AddComponent<MeshFilter>().sharedMesh = t.mesh;
            t.mr = t.go.AddComponent<MeshRenderer>();
            t.mr.sharedMaterial = Look.FxMat(Color.white, false, false);
            if (t.mr.sharedMaterial != null) { t.mr.sharedMaterial = new Material(t.mr.sharedMaterial) { name = "trail" }; t.mr.sharedMaterial.SetFloat("_Radial", 2f); }
            t.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; t.mr.receiveShadows = false;
            t.go.AddComponent<TrailTicker>().owner = t;
            return t;
        }

        public void Emit(bool v) { on = v; }

        public void Tick()
        {
            if (a == null || b == null) { Destroy(); return; }
            float now = Time.time;
            if (on) { pa.Add(a.position); pb.Add(b.position); pt.Add(now); }
            while (pt.Count > 0 && now - pt[0] > Life) { pa.RemoveAt(0); pb.RemoveAt(0); pt.RemoveAt(0); }
            mesh.Clear();
            int n = pt.Count;
            if (n < 2) return;
            var v = new Vector3[n * 2]; var c = new Color[n * 2]; var uv = new Vector2[n * 2]; var tr = new int[(n - 1) * 6];
            for (int i = 0; i < n; i++)
            {
                float k = 1f - (now - pt[i]) / Life;           // свежий конец ярче
                float f = Mathf.Clamp01(k) * ((float)i / (n - 1));
                v[i * 2] = pa[i]; v[i * 2 + 1] = pb[i];
                c[i * 2] = new Color(col.r, col.g, col.b, col.a * f * 0.35f); c[i * 2 + 1] = new Color(col.r, col.g, col.b, col.a * f);
                uv[i * 2] = new Vector2((float)i / (n - 1), 0f); uv[i * 2 + 1] = new Vector2((float)i / (n - 1), 1f);
            }
            for (int i = 0; i < n - 1; i++)
            {
                int o = i * 6, q = i * 2;
                tr[o] = q; tr[o + 1] = q + 1; tr[o + 2] = q + 2; tr[o + 3] = q + 1; tr[o + 4] = q + 3; tr[o + 5] = q + 2;
            }
            mesh.vertices = v; mesh.colors = c; mesh.uv = uv; mesh.triangles = tr; mesh.RecalculateBounds();
        }

        public void Destroy()
        {
            if (go != null) Object.Destroy(go);
            if (a != null) Object.Destroy(a.gameObject);
            go = null;
        }

        [DefaultExecutionOrder(300)]
        class TrailTicker : MonoBehaviour { public WeaponTrail owner; void LateUpdate() { if (owner != null) owner.Tick(); } }
    }
}
