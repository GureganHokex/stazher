// Перезарядка (спринт 8 версии 0.9 «Оружие в руках», задача 3): у каждого вида своя.
// Магазин: левая рука берёт магазин, вынимает (пустой падает на землю), достаёт новый с пояса, вставляет;
// если стрелял до пустого — передёргивает затвор (пистолет — затворную раму, автомат — рукоять заряжания,
// ПП — рукоять сбоку, винтовка — правой рукой рукоять затвора). С патроном в патроннике перезарядка быстрее.
// Дробовик — по одному патрону через окно снизу, выстрел прерывает перезарядку. Пулемёт — открыть крышку,
// сменить короб, уложить ленту, закрыть, взвести. Руки ставит тот же IK — по точкам самого оружия.
using UnityEngine;

namespace Intern.Game
{
    public partial class PlayerCombat
    {
        bool reloadEmpty, partDropped, boxDropped;
        float pumpAt = -9f, boltAt = -9f, tiltW;
        GameObject shellHand;

        struct Key { public float u; public Vector3 p; public Quaternion r; public Key(float u, Vector3 p, Quaternion r) { this.u = u; this.p = p; this.r = r; } }

        static void Path(Key[] ks, float u, out Vector3 p, out Quaternion r)
        {
            p = ks[0].p; r = ks[0].r;
            if (u <= ks[0].u) return;
            for (int i = 0; i + 1 < ks.Length; i++)
            {
                if (u > ks[i + 1].u) continue;
                float t = S01((u - ks[i].u) / Mathf.Max(1e-4f, ks[i + 1].u - ks[i].u));
                p = Vector3.Lerp(ks[i].p, ks[i + 1].p, t); r = Quaternion.Slerp(ks[i].r, ks[i + 1].r, t);
                return;
            }
            p = ks[ks.Length - 1].p; r = ks[ks.Length - 1].r;
        }

        // Начало перезарядки: вид и «быстрая» (патрон в патроннике)
        void BeginReloadAnim(WeaponDef def)
        {
            reloadEmpty = MagNow <= 0;
            partDropped = false; boxDropped = false;
            if (def.id == "shotgun") reloadDur = Mathf.Max(0.3f, ars.ReloadTime(def) / 4.5f);
            else if (!reloadEmpty) reloadDur *= 0.8f;
        }

        // Дробовик: патрон за патроном; остальные — всё сразу
        bool ReloadStep(WeaponDef def, WeaponSave w)
        {
            if (def.id != "shotgun") return false;
            if (Reserve > 0 && w.mag < MagSize) { ars.AddAmmo(def.ammo, -1); w.mag = Mathf.Max(0, w.mag) + 1; }
            if (w.mag < MagSize && Reserve > 0) { reloadStart = Time.time; return true; }
            reloadStart = -1f;
            if (reloadEmpty) pumpAt = Time.time + 0.08f;
            return true;
        }

        // Наклон ствола на перезарядке (окно магазина — к левой руке)
        void ReloadTilt(GunSpec sp, ref Vector3 pos, ref Quaternion rot, Vector3 gripLocal, float s, float dt)
        {
            bool rack = Reloading && sp.cycle != "pump" && ReloadProgress > 0.74f && reloadEmpty;
            tiltW = Mathf.MoveTowards(tiltW, Reloading && !rack ? 1f : 0f, dt * 4f);
            float w = S01(tiltW);
            if (w <= 0.001f) return;
            Vector3 gw = pos + rot * gripLocal;
            Quaternion r2 = rot * Quaternion.Euler(-8f * w, -6f * w, -20f * w);
            pos = gw - r2 * gripLocal + (rot * new Vector3(-0.04f, -0.05f, -0.07f)) * (w * s);
            rot = r2;
        }

        // Детали и руки на перезарядке. G/GR — левая рука на цевье, RR — правая на рукояти
        void ReloadParts(WeaponModel g, GunSpec sp, bool fp, Transform camT, Vector3 G, Quaternion GR)
        {
            var rot = g.transform.rotation;
            float s = Av != null ? Av.transform.lossyScale.y : 1f;
            Vector3 down = rot * Vector3.down, back = rot * Vector3.back;
            Vector3 P = fp ? camT.TransformPoint(new Vector3(-0.16f, -0.42f, 0.16f) * s)
                           : (Av != null && Av.hips != null ? Av.hips.position + Av.transform.rotation * (new Vector3(-0.17f, 0.03f, 0.13f) * s) : G + Vector3.down * 0.4f);
            Quaternion PR = rot * CharacterAnim.HandRot(false, new Vector3(0.1f, -1f, 0.25f), Vector3.right);
            float u = ReloadProgress;
            Vector3 hp; Quaternion hrt;

            if (sp.cycle == "pump")
            {
                // патрон с пояса — в окно снизу — дослать
                Vector3 K1 = g.shellPort != null ? g.shellPort.position : g.transform.TransformPoint(0f, -0.04f, 0.03f);
                Vector3 K0 = K1 + down * 0.05f - back * 0.02f;
                var KR = rot * CharacterAnim.HandRot(false, new Vector3(0.3f, 0.2f, 0.93f), Vector3.up);
                Path(new[] { new Key(0f, G, GR), new Key(0.32f, P, PR), new Key(0.62f, K0, KR), new Key(0.8f, K1, KR), new Key(1f, G, GR) }, u, out hp, out hrt);
                ShellInHand(u > 0.3f && u < 0.8f, hp, hrt);
                Hand(hp, hrt);
                return;
            }
            if (sp.cycle == "mg") { BeltReload(g, u, G, GR, P, PR); return; }

            // ---- магазин
            if (g.magPart == null) { Path(new[] { new Key(0f, G, GR), new Key(0.4f, P, PR), new Key(1f, G, GR) }, u, out hp, out hrt); Hand(hp, hrt); return; }
            bool pistol = sp.cycle == "slide";
            float depth = pistol ? 0.1f : sp.cycle == "boltaction" ? 0.05f : 0.075f;
            Vector3 M0 = g.PartRestW(g.magPart) + down * depth;
            Vector3 M1 = M0 + down * (pistol ? 0.12f : 0.15f), M2 = M0 + down * 0.11f;
            var MR = pistol ? rot * CharacterAnim.HandRot(false, new Vector3(0.25f, 0.1f, 0.96f), Vector3.up)
                            : rot * CharacterAnim.HandRot(false, new Vector3(0.1f, -0.45f, 0.88f), Vector3.right);
            float rk = reloadEmpty ? 1f : 0f;
            // передёрнуть: точка и направление рывка
            Vector3 R0 = G, R1 = G; Quaternion RRk = GR;
            if (pistol) { R0 = g.transform.TransformPoint(0f, 0.035f, -0.06f); R1 = R0 + back * 0.035f; RRk = rot * CharacterAnim.HandRot(false, new Vector3(1f, 0f, 0.1f), Vector3.down); }
            else if (sp.cycle == "rifle" && g.charge != null) { R0 = g.PartRestW(g.charge) + rot * new Vector3(0f, 0.014f, 0.01f); R1 = R0 + back * 0.065f; RRk = rot * CharacterAnim.HandRot(false, new Vector3(0.85f, -0.1f, -0.5f), Vector3.down); }
            else if (sp.cycle == "smg" && g.charge != null) { R0 = g.PartRestW(g.charge) + rot * new Vector3(-0.012f, 0f, 0f); R1 = R0 + back * 0.08f; RRk = rot * CharacterAnim.HandRot(false, new Vector3(0f, 0.55f, 0.83f), Vector3.right); }
            bool leftRack = reloadEmpty && (pistol || ((sp.cycle == "rifle" || sp.cycle == "smg") && g.charge != null));
            Key[] ks;
            if (pistol)
                ks = leftRack
                    ? new[] { new Key(0f, G, GR), new Key(0.3f, P, PR), new Key(0.5f, M2, MR), new Key(0.62f, M0, MR), new Key(0.76f, R0, RRk), new Key(0.84f, R1, RRk), new Key(0.88f, R1, RRk), new Key(0.98f, G, GR) }
                    : new[] { new Key(0f, G, GR), new Key(0.3f, P, PR), new Key(0.5f, M2, MR), new Key(0.62f, M0, MR), new Key(0.82f, G, GR) };
            else
                ks = leftRack
                    ? new[] { new Key(0f, G, GR), new Key(0.12f, M0, MR), new Key(0.26f, M1, MR), new Key(0.32f, M1 + back * 0.05f, MR), new Key(0.46f, P, PR), new Key(0.62f, M2, MR), new Key(0.72f, M0, MR), new Key(0.78f, R0, RRk), new Key(0.85f, R1, RRk), new Key(0.88f, R1, RRk), new Key(0.98f, G, GR) }
                    : new[] { new Key(0f, G, GR), new Key(0.12f, M0, MR), new Key(0.26f, M1, MR), new Key(0.32f, M1 + back * 0.05f, MR), new Key(0.46f, P, PR), new Key(0.62f, M2, MR), new Key(0.72f, M0, MR), new Key(0.86f, G, GR) };
            Path(ks, u, out hp, out hrt);
            Hand(hp, hrt);

            // магазин: в руке — идёт за ней, между «выбросил» и «достал» — его нет
            float outAt = pistol ? 0.06f : 0.32f, inAt = pistol ? 0.3f : 0.46f, seatAt = pistol ? 0.62f : 0.72f;
            if (u >= outAt && !partDropped) { partDropped = true; Casings.DropPart(g.magPart, player, (pistol ? down * 0.5f : down * 1.2f - back * 0.6f)); }
            bool held = pistol ? (u >= inAt && u < seatAt) : ((u >= 0.12f && u < outAt) || (u >= inAt && u < seatAt));
            g.ShowMag(!(u >= outAt && u < inAt));
            // кисть в точке хвата: магазин сдвигается так, чтобы хват был в ней
            if (held) g.Move(g.magPart, g.transform.InverseTransformVector(hp - M0));

            // затвор
            if (reloadEmpty)
            {
                float k = u < 0.78f ? 0f : u < 0.85f ? S01((u - 0.78f) / 0.07f) : u < 0.88f ? 1f : 0f;
                if (pistol)
                {
                    float z = u < 0.76f ? -0.027f : u < 0.84f ? -0.027f - 0.006f * S01((u - 0.76f) / 0.08f) : u < 0.88f ? -0.033f : 0f;
                    g.Move(g.slide, new Vector3(0f, 0f, z));
                }
                else if (sp.cycle == "rifle") { g.Move(g.charge, new Vector3(0f, 0f, -0.065f * k)); g.Move(g.bolt, new Vector3(0f, 0f, -0.065f * k)); }
                else if (sp.cycle == "smg") g.Move(g.charge, new Vector3(0f, 0.004f * k, -0.075f * k));
                else if (sp.cycle == "boltaction" && u > 0.74f && boltAt < reloadStart) boltAt = Time.time;
            }
        }

        void Hand(Vector3 p, Quaternion r) { leftFree = true; leftW = 1f; leftAt = p; leftRot = r; }

        // Пулемёт: крышка — короб — лента — крышка — взвод
        void BeltReload(WeaponModel g, float u, Vector3 G, Quaternion GR, Vector3 P, Quaternion PR)
        {
            var rot = g.transform.rotation;
            Vector3 down = rot * Vector3.down, left = rot * Vector3.left;
            Vector3 C = g.cover != null ? g.cover.position + rot * new Vector3(0f, 0.02f, -0.1f) : G;
            var CR = rot * CharacterAnim.HandRot(false, new Vector3(0.9f, 0f, 0.4f), Vector3.down);
            Vector3 B0 = g.ammoBox != null ? g.PartRestW(g.ammoBox) + left * 0.03f + down * 0.02f : G;
            var BR = rot * CharacterAnim.HandRot(false, new Vector3(0.1f, -0.3f, 0.95f), Vector3.right);
            Vector3 B1 = B0 + down * 0.14f + left * 0.06f;
            Vector3 L = g.belt != null ? g.belt.position + rot * new Vector3(0f, 0.02f, 0f) : C;
            float open = u < 0.1f ? 0f : u < 0.18f ? S01((u - 0.1f) / 0.08f) : u < 0.78f ? 1f : u < 0.84f ? 1f - S01((u - 0.78f) / 0.06f) : 0f;
            Vector3 hp; Quaternion hr;
            Path(new[] { new Key(0f, G, GR), new Key(0.1f, C, CR), new Key(0.18f, C + rot * new Vector3(0f, 0.09f, 0.03f), CR), new Key(0.24f, B0, BR), new Key(0.34f, B1, BR),
                         new Key(0.4f, B1 + left * 0.05f, BR), new Key(0.52f, P, PR), new Key(0.64f, B0, BR), new Key(0.72f, L, CR), new Key(0.78f, C + rot * new Vector3(0f, 0.09f, 0.03f), CR),
                         new Key(0.84f, C, CR), new Key(0.92f, G, GR) }, u, out hp, out hr);
            Hand(hp, hr);
            if (g.cover != null) g.Move(g.cover, Vector3.zero, Quaternion.Euler(80f * open, 0f, 0f));
            if (u >= 0.4f && !boxDropped && g.ammoBox != null) { boxDropped = true; Casings.DropPart(g.ammoBox, player, down * 1f + left * 0.8f); }
            bool gone = u >= 0.4f && u < 0.52f;
            if (g.ammoBox != null)
            {
                g.ammoBox.gameObject.SetActive(!gone);
                if ((u >= 0.24f && u < 0.4f) || (u >= 0.52f && u < 0.64f)) g.Move(g.ammoBox, g.transform.InverseTransformVector(hp - B0));
            }
            if (g.belt != null) g.belt.gameObject.SetActive(u < 0.26f || u >= 0.68f);
            // взвод правой рукой
            if (g.charge != null && u > 0.86f && u < 0.98f)
            {
                float k = S01((u - 0.86f) / 0.05f) * (1f - S01((u - 0.93f) / 0.03f));
                g.Move(g.charge, new Vector3(0f, 0f, -0.08f * k));
                rightFree = true; rightAt = g.charge.position + rot * new Vector3(0.012f, 0f, 0f);
                rightRot = rot * CharacterAnim.HandRot(true, new Vector3(0f, 0.3f, 0.95f), Vector3.left);
            }
        }

        // Патрон дробовика в левой руке
        void ShellInHand(bool on, Vector3 palm, Quaternion handRot)
        {
            if (shellHand == null)
            {
                if (!on) return;
                shellHand = new GameObject("ShellInHand"); shellHand.layer = 2; shellHand.transform.SetParent(transform, false);
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(shellHand.transform, false); body.transform.localScale = new Vector3(0.02f, 0.0325f, 0.02f); body.layer = 2;
                body.GetComponent<Renderer>().sharedMaterial = Look.Mat(new Color(0.72f, 0.1f, 0.08f));
                var head = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(head.GetComponent<Collider>());
                head.transform.SetParent(shellHand.transform, false); head.transform.localScale = new Vector3(0.021f, 0.008f, 0.021f); head.transform.localPosition = new Vector3(0f, -0.028f, 0f); head.layer = 2;
                head.GetComponent<Renderer>().sharedMaterial = Look.Mat(new Color(0.86f, 0.64f, 0.27f));
            }
            shellHand.SetActive(on);
            if (!on || gunModel == null) return;
            // лежит в пальцах вдоль ствола, донце назад
            shellHand.transform.SetPositionAndRotation(palm + handRot * new Vector3(0.02f, -0.02f, 0.02f), gunModel.transform.rotation * Quaternion.Euler(90f, 0f, 0f));
        }

        void ReloadEnd(WeaponModel g)
        {
            if (shellHand != null && shellHand.activeSelf) shellHand.SetActive(false);
            if (g == null) return;
            g.ShowMag(true);
            if (g.ammoBox != null && !g.ammoBox.gameObject.activeSelf) g.ammoBox.gameObject.SetActive(true);
            if (g.belt != null && !g.belt.gameObject.activeSelf) g.belt.gameObject.SetActive(true);
        }
    }
}
