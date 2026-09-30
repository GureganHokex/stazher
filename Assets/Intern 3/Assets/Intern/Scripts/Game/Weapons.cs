// Оружие обеда: что есть у игрока (Arsenal — поверх сохранения) и как оно работает в руках (PlayerCombat).
// Ближний бой бьёт сферой перед собой, огнестрел — лучом из центра камеры с разбросом.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    // ======================= арсенал в сохранении =======================
    public class Arsenal
    {
        public static readonly string[] SlotIds = { "sight", "barrel", "mag", "rail" };
        public static readonly string[] SlotNames = { "Прицел", "Ствол", "Магазин", "Планка" };

        readonly SaveData s;
        public Arsenal(SaveData save) { s = save; Ensure(); }
        public SaveData Save { get { return s; } }

        void Ensure()
        {
            if (s.arsenal == null) s.arsenal = new List<WeaponSave>();
            if (s.attachments == null) s.attachments = new List<string>();
            if (s.ammoBag == null) s.ammoBag = new List<AmmoSave>();
            if (!Owns("knife")) s.arsenal.Insert(0, new WeaponSave { id = "knife" });
            if (string.IsNullOrEmpty(s.meleeWeapon) || !Owns(s.meleeWeapon)) s.meleeWeapon = "knife";
            if (!string.IsNullOrEmpty(s.gunWeapon) && !Owns(s.gunWeapon)) s.gunWeapon = "";
        }

        public bool Owns(string id) { return Get(id) != null; }
        public WeaponSave Get(string id) { foreach (var w in s.arsenal) if (w.id == id) return w; return null; }
        public int Level(string id) { var w = Get(id); return w != null ? w.level : 0; }
        public WeaponDef Melee { get { return Balance.Weapon(s.meleeWeapon) ?? Balance.Weapon("knife"); } }
        public WeaponDef Gun { get { return string.IsNullOrEmpty(s.gunWeapon) ? null : Balance.Weapon(s.gunWeapon); } }

        public void Add(string id)
        {
            if (Owns(id)) return;
            var def = Balance.Weapon(id); if (def == null) return;
            s.arsenal.Add(new WeaponSave { id = id });
            if (def.Melee) s.meleeWeapon = id; else { s.gunWeapon = id; AddAmmo(def.ammo, def.mag * 2); }
        }

        // Следующее купленное оружие того же вида (ближний бой или огнестрел) по порядку покупки; null — другого нет
        public string NextOwned(bool melee, string current)
        {
            var list = new List<string>();
            foreach (var w in s.arsenal) { var d = Balance.Weapon(w.id); if (d != null && d.Melee == melee) list.Add(w.id); }
            if (list.Count < 2) return null;
            int i = list.IndexOf(current);
            return list[(i + 1) % list.Count];
        }

        public void Equip(string id)
        {
            var def = Balance.Weapon(id); if (def == null || !Owns(id)) return;
            if (def.Melee) s.meleeWeapon = id; else s.gunWeapon = id;
        }

        // ---- патроны ----
        public int AmmoOf(string type) { foreach (var a in s.ammoBag) if (a.id == type) return a.count; return 0; }
        public void AddAmmo(string type, int n)
        {
            if (string.IsNullOrEmpty(type) || n == 0) return;
            foreach (var a in s.ammoBag) if (a.id == type) { a.count = Mathf.Max(0, a.count + n); return; }
            s.ammoBag.Add(new AmmoSave { id = type, count = Mathf.Max(0, n) });
        }

        // ---- обвесы ----
        public bool HasAttach(string id) { return s.attachments.Contains(id); }
        public string Installed(WeaponSave w, string slot)
        {
            if (w == null) return "";
            switch (slot) { case "sight": return w.sight; case "barrel": return w.barrel; case "mag": return w.magSlot; case "rail": return w.rail; }
            return "";
        }
        public void Install(WeaponSave w, string slot, string id)
        {
            if (w == null) return;
            // обвес один — снимаем его с другого ствола
            if (!string.IsNullOrEmpty(id)) foreach (var o in s.arsenal) if (o != w) foreach (var sl in SlotIds) if (Installed(o, sl) == id) Set(o, sl, "");
            Set(w, slot, id ?? "");
            if (slot == "mag") w.mag = Mathf.Min(w.mag, MagSize(Balance.Weapon(w.id)));
        }
        static void Set(WeaponSave w, string slot, string id)
        {
            switch (slot) { case "sight": w.sight = id; break; case "barrel": w.barrel = id; break; case "mag": w.magSlot = id; break; case "rail": w.rail = id; break; }
        }
        public string WhereInstalled(string attachId)
        {
            foreach (var o in s.arsenal) foreach (var sl in SlotIds) if (Installed(o, sl) == attachId) return o.id;
            return null;
        }
        IEnumerable<AttachDef> Attached(WeaponSave w)
        {
            if (w == null) yield break;
            foreach (var sl in SlotIds) { var id = Installed(w, sl); if (!string.IsNullOrEmpty(id)) { var a = Balance.Attach(id); if (a != null) yield return a; } }
        }

        // ---- характеристики с учётом улучшений и обвесов ----
        public bool Perk(WeaponDef w) { return w != null && Level(w.id) >= 5; }
        public int Damage(WeaponDef w) { return Mathf.RoundToInt(w.damage * (1f + Balance.D.upgradeDamage * Level(w.id))); }
        public int MagSize(WeaponDef w)
        {
            if (w == null || w.mag <= 0) return 0;
            float m = 1f; foreach (var a in Attached(Get(w.id))) m *= a.magMult;
            return Mathf.RoundToInt(w.mag * m) + (w.id == "smg" && Perk(w) ? 10 : 0);
        }
        public float ReloadTime(WeaponDef w)
        {
            float t = w.reload * (Level(w.id) >= 3 ? Balance.D.fastReload : 1f);
            foreach (var a in Attached(Get(w.id))) t *= a.reloadMult;
            return t;
        }
        public float Spread(WeaponDef w, bool ads)
        {
            float sp = w.spread * (ads ? 0.45f : 1f);
            if (w.id == "shotgun" && Perk(w)) sp *= 0.6f;
            foreach (var a in Attached(Get(w.id))) sp *= ads ? a.adsSpread : a.hipSpread;
            return sp;
        }
        public float Recoil(WeaponDef w)
        {
            if (w.id == "rifle" && Perk(w)) return 0.15f;
            float r = w.recoil; foreach (var a in Attached(Get(w.id))) r *= a.recoil; return r;
        }
        public float Zoom(WeaponDef w)
        {
            float z = Mathf.Max(1.3f, w.zoom);
            foreach (var a in Attached(Get(w.id))) z = Mathf.Max(z, a.zoom);
            return z;
        }
        public float NoiseRange(WeaponDef w)
        {
            if (w == null || w.Melee) return 0f;
            foreach (var a in Attached(Get(w.id))) if (a.noise > 0f) return a.noise;
            return Balance.D.hearRange;
        }
        public bool Penetrates(WeaponDef w) { return (w.id == "pistol" || w.id == "sniper") && Perk(w); }
        public bool Overheats(WeaponDef w) { return w.id == "mg" && !Perk(w); }
    }

    // ======================= модель оружия =======================
    public class WeaponModel : MonoBehaviour
    {
        public Transform muzzle, sight, rail, magPoint;
        public Transform grip;   // за что держит правая рука (рукоять)
        public string id;
        public bool melee, twoHanded;

        static readonly Color Metal = Pal.Hex("2B2D42"), Steel = Pal.Hex("8A8FA8"), Wood = Pal.Hex("8C6A4F"), Blade = Pal.Hex("D8DCE8");
        static readonly Color GoldDark = Pal.Hex("A87B12"), Gold = Pal.Hex("FFD23F"), GoldWarm = Pal.Hex("D9A441");
        public static bool Golden;   // включает GameRoot перед обедом, когда у игрока титул Architect

        static GameObject Box(Transform p, string n, Vector3 pos, Vector3 size, Color c, float r = 0.008f, float em = 0f)
        { var g = Look.RBox(n, p, pos, size, c, r, false, 0.6f, em, false); return g; }
        static GameObject Cyl(Transform p, string n, Vector3 pos, float radius, float len, Color c)
        {
            var g = Look.Prim(n, p, PrimitiveType.Cylinder, pos, new Vector3(radius * 2f, len / 2f, radius * 2f), c, false, 0.6f, 0f, false);
            g.transform.localRotation = Quaternion.Euler(90f, 0, 0); return g;
        }

        public static WeaponModel Build(WeaponDef def, WeaponSave save, Arsenal ars)
        {
            // Architect: золотое оружие (перк титула)
            Color metal = Golden ? GoldDark : Metal, steel = Golden ? Gold : Steel, wood = Golden ? GoldWarm : Wood;
            var go = new GameObject("Weapon_" + def.id);
            var m = go.AddComponent<WeaponModel>(); m.id = def.id; m.melee = def.Melee;
            var t = go.transform;
            Func<string, Vector3, Transform> pt = (n, p) => { var x = new GameObject(n).transform; x.SetParent(t, false); x.localPosition = p; return x; };
            switch (def.id)
            {
                case "knife":
                    Box(t, "Handle", new Vector3(0, 0, 0.02f), new Vector3(0.035f, 0.035f, 0.12f), metal);
                    Box(t, "Blade", new Vector3(0, 0.005f, 0.16f), new Vector3(0.012f, 0.045f, 0.17f), Blade, 0.004f, 0.15f);
                    m.muzzle = pt("Tip", new Vector3(0, 0, 0.25f)); break;
                case "bat":
                    Cyl(t, "Grip", new Vector3(0, 0, 0.05f), 0.02f, 0.26f, metal);
                    Cyl(t, "Barrel", new Vector3(0, 0, 0.42f), 0.042f, 0.55f, wood);
                    m.muzzle = pt("Tip", new Vector3(0, 0, 0.7f)); break;
                case "katana":
                    Box(t, "Handle", new Vector3(0, 0, 0.03f), new Vector3(0.032f, 0.032f, 0.24f), Pal.Hex("2E2638"));
                    Box(t, "Guard", new Vector3(0, 0, 0.16f), new Vector3(0.08f, 0.03f, 0.02f), Pal.Hex("D9A441"));
                    Box(t, "Blade", new Vector3(0, 0.005f, 0.6f), new Vector3(0.012f, 0.035f, 0.86f), Blade, 0.004f, 0.2f);
                    m.muzzle = pt("Tip", new Vector3(0, 0, 1.02f)); break;
                case "pistol":
                    Box(t, "Grip", new Vector3(0, -0.05f, -0.01f), new Vector3(0.032f, 0.11f, 0.05f), metal).transform.localRotation = Quaternion.Euler(-12f, 0, 0);
                    Box(t, "Slide", new Vector3(0, 0.03f, 0.06f), new Vector3(0.036f, 0.045f, 0.2f), steel);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.03f, 0.17f)); m.sight = pt("Sight", new Vector3(0, 0.06f, 0.05f));
                    m.rail = pt("Rail", new Vector3(0, -0.005f, 0.11f)); m.magPoint = pt("Mag", new Vector3(0, -0.11f, -0.02f)); break;
                case "smg":
                    Box(t, "Body", new Vector3(0, 0.02f, 0.1f), new Vector3(0.05f, 0.08f, 0.3f), metal);
                    Box(t, "Grip", new Vector3(0, -0.05f, 0f), new Vector3(0.03f, 0.1f, 0.045f), metal);
                    Box(t, "Stock", new Vector3(0, 0.02f, -0.12f), new Vector3(0.03f, 0.05f, 0.14f), steel);
                    Cyl(t, "Barrel", new Vector3(0, 0.03f, 0.3f), 0.013f, 0.12f, steel);
                    Box(t, "Mag", new Vector3(0, -0.08f, 0.11f), new Vector3(0.03f, 0.14f, 0.04f), metal);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.03f, 0.37f)); m.sight = pt("Sight", new Vector3(0, 0.07f, 0.08f));
                    m.rail = pt("Rail", new Vector3(0, -0.03f, 0.22f)); m.magPoint = pt("Mag", new Vector3(0, -0.16f, 0.11f)); m.twoHanded = true; break;
                case "shotgun":
                    Box(t, "Receiver", new Vector3(0, 0.01f, 0.04f), new Vector3(0.05f, 0.08f, 0.2f), metal);
                    Box(t, "Grip", new Vector3(0, -0.06f, -0.02f), new Vector3(0.03f, 0.1f, 0.045f), wood);
                    Box(t, "Stock", new Vector3(0, 0f, -0.22f), new Vector3(0.045f, 0.09f, 0.28f), wood);
                    Cyl(t, "Barrel", new Vector3(0, 0.035f, 0.42f), 0.018f, 0.62f, steel);
                    Box(t, "Pump", new Vector3(0, -0.005f, 0.34f), new Vector3(0.05f, 0.045f, 0.18f), wood);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.035f, 0.74f)); m.sight = pt("Sight", new Vector3(0, 0.06f, 0.06f));
                    m.rail = pt("Rail", new Vector3(0, -0.035f, 0.5f)); m.magPoint = pt("Mag", new Vector3(0, -0.04f, 0.14f)); m.twoHanded = true; break;
                case "rifle":
                    Box(t, "Receiver", new Vector3(0, 0.02f, 0.1f), new Vector3(0.05f, 0.09f, 0.36f), metal);
                    Box(t, "Grip", new Vector3(0, -0.06f, 0f), new Vector3(0.03f, 0.1f, 0.045f), metal);
                    Box(t, "Stock", new Vector3(0, 0.01f, -0.2f), new Vector3(0.045f, 0.1f, 0.26f), metal);
                    Cyl(t, "Barrel", new Vector3(0, 0.035f, 0.44f), 0.013f, 0.32f, steel);
                    Box(t, "Mag", new Vector3(0, -0.1f, 0.14f), new Vector3(0.035f, 0.15f, 0.06f), metal).transform.localRotation = Quaternion.Euler(12f, 0, 0);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.035f, 0.61f)); m.sight = pt("Sight", new Vector3(0, 0.08f, 0.08f));
                    m.rail = pt("Rail", new Vector3(0, -0.03f, 0.32f)); m.magPoint = pt("Mag", new Vector3(0, -0.18f, 0.15f)); m.twoHanded = true; break;
                case "sniper":
                    Box(t, "Receiver", new Vector3(0, 0.015f, 0.08f), new Vector3(0.05f, 0.08f, 0.36f), metal);
                    Box(t, "Grip", new Vector3(0, -0.06f, 0f), new Vector3(0.03f, 0.1f, 0.045f), wood);
                    Box(t, "Stock", new Vector3(0, 0f, -0.25f), new Vector3(0.05f, 0.11f, 0.32f), wood);
                    Cyl(t, "Barrel", new Vector3(0, 0.03f, 0.56f), 0.012f, 0.62f, steel);
                    Cyl(t, "Scope", new Vector3(0, 0.085f, 0.1f), 0.022f, 0.28f, metal);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.03f, 0.88f)); m.sight = pt("Sight", new Vector3(0, 0.12f, 0.1f));
                    m.rail = pt("Rail", new Vector3(0, -0.03f, 0.35f)); m.magPoint = pt("Mag", new Vector3(0, -0.07f, 0.1f)); m.twoHanded = true; break;
                default: // mg
                    Box(t, "Body", new Vector3(0, 0.02f, 0.1f), new Vector3(0.08f, 0.12f, 0.46f), metal);
                    Box(t, "Grip", new Vector3(0, -0.07f, -0.02f), new Vector3(0.03f, 0.1f, 0.045f), metal);
                    Box(t, "Stock", new Vector3(0, 0.01f, -0.22f), new Vector3(0.05f, 0.1f, 0.24f), metal);
                    Cyl(t, "Barrel", new Vector3(0, 0.03f, 0.56f), 0.02f, 0.5f, steel);
                    Box(t, "BeltBox", new Vector3(-0.07f, -0.05f, 0.08f), new Vector3(0.07f, 0.1f, 0.13f), Pal.Hex("4A5A3A"));
                    Box(t, "Handle", new Vector3(0, 0.1f, 0.12f), new Vector3(0.025f, 0.04f, 0.12f), metal);
                    m.muzzle = pt("Muzzle", new Vector3(0, 0.03f, 0.82f)); m.sight = pt("Sight", new Vector3(0, 0.1f, 0.0f));
                    m.rail = pt("Rail", new Vector3(0, -0.05f, 0.36f)); m.magPoint = pt("Mag", new Vector3(-0.07f, -0.12f, 0.08f)); m.twoHanded = true; break;
            }
            m.grip = t.Find("Grip") ?? t.Find("Handle");
            if (!def.Melee && ars != null) m.AddAttachments(save, ars);
            if (!def.Melee) go.transform.localScale = Vector3.one * 1.35f;   // стволы чуть крупнее — иначе их не видно из-за плеча
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 2;
            return m;
        }

        // Обвесы видны на стволе
        void AddAttachments(WeaponSave save, Arsenal ars)
        {
            var sight = ars.Installed(save, "sight"); var barrel = ars.Installed(save, "barrel"); var mag = ars.Installed(save, "mag"); var rail = ars.Installed(save, "rail");
            if (sight == "reddot" && this.sight != null) { Box(this.sight, "RedDot", Vector3.zero, new Vector3(0.035f, 0.035f, 0.05f), Metal); Box(this.sight, "Dot", new Vector3(0, 0.005f, 0.026f), new Vector3(0.012f, 0.012f, 0.004f), Pal.Hex("FF3B3B"), 0.002f, 2f); }
            if (sight == "profiler" && this.sight != null) Cyl(this.sight, "Scope", new Vector3(0, 0.01f, 0), 0.022f, 0.24f, Metal);
            if (barrel == "silencer" && muzzle != null) { Cyl(muzzle, "Silencer", new Vector3(0, 0, 0.08f), 0.02f, 0.16f, Pal.Hex("3A3D55")); muzzle.localPosition += new Vector3(0, 0, 0.16f); }
            if (rail == "laser" && this.rail != null) { Box(this.rail, "Laser", Vector3.zero, new Vector3(0.025f, 0.025f, 0.06f), Metal); Box(this.rail, "Beam", new Vector3(0, 0, 0.8f), new Vector3(0.004f, 0.004f, 1.5f), Pal.Hex("FF3B3B"), 0.001f, 3f); }
            if (rail == "grip" && this.rail != null) Box(this.rail, "Foregrip", new Vector3(0, -0.04f, 0), new Vector3(0.026f, 0.08f, 0.035f), Metal);
            if (mag == "buffer" && magPoint != null) Box(magPoint, "LongMag", Vector3.zero, new Vector3(0.032f, 0.1f, 0.045f), Metal);
            if (mag == "heap" && magPoint != null) { var d = Cyl(magPoint, "Drum", Vector3.zero, 0.06f, 0.05f, Metal); d.transform.localRotation = Quaternion.Euler(0, 0, 90f); }
        }
    }

    // ======================= оружие в руках =======================
    [DefaultExecutionOrder(100)]   // после анимации персонажа: ствол ставится в руку уже этого кадра
    public class PlayerCombat : MonoBehaviour
    {
        public Arsenal ars;
        public LunchRun run;
        PlayerController player;
        CharacterAnim Av { get { return player != null ? player.avatar : null; } }

        public int slot;                           // 0 — ближний бой, 1 — огнестрел
        WeaponModel meleeModel, gunModel;
        float nextAt, reloadStart = -1f, reloadDur, heat, overheatUntil, dashReadyAt, throwBackAt = -1f, pendingHitAt = -1f;
        bool ads;
        public float lastShotAt = -9f, spreadKick;

        public WeaponDef Current { get { return slot == 1 && ars.Gun != null ? ars.Gun : ars.Melee; } }
        public WeaponSave CurrentSave { get { return ars.Get(Current.id); } }
        public bool Reloading { get { return reloadStart >= 0f; } }
        public float ReloadProgress { get { return Reloading ? Mathf.Clamp01((Time.time - reloadStart) / reloadDur) : 0f; } }
        public float Heat { get { return heat; } }
        public bool Overheated { get { return Time.time < overheatUntil; } }
        public bool Aiming { get { return ads; } }
        public bool Scoped { get { return ads && !Current.Melee && ars.Zoom(Current) >= 3.9f; } }
        public bool KnifeAway { get { return throwBackAt > 0f; } }
        public float DashReady { get { return Mathf.Clamp01(1f - (dashReadyAt - Time.time) / 3f); } }
        public int MagNow { get { var w = CurrentSave; if (w == null || Current.Melee) return 0; if (w.mag < 0) w.mag = ars.MagSize(Current); return w.mag; } }
        public int MagSize { get { return ars.MagSize(Current); } }
        public int Reserve { get { return Current.Melee ? 0 : ars.AmmoOf(Current.ammo); } }
        public float SpreadNow { get { return Current.Melee ? 0f : ars.Spread(Current, ads) + spreadKick; } }

        public void Init(PlayerController p, Arsenal a) { player = p; ars = a; }

        public string DebugInfo()
        {
            var hand = Av != null && Av.elbowR != null ? Av.elbowR.TransformPoint(new Vector3(0, -0.27f, 0.03f)) : Vector3.zero;
            return "слот " + slot + ", ствол " + (gunModel != null ? gunModel.name + " active=" + gunModel.gameObject.activeInHierarchy + " pos=" + gunModel.transform.position + " рендеров " + gunModel.GetComponentsInChildren<Renderer>().Length : "нет") +
                   ", рука " + hand + ", игрок " + (player != null ? player.Position.ToString() : "-");
        }

        // Начало обеда: оружие в руки
        public void Begin(LunchRun r)
        {
            run = r; slot = 0; reloadStart = -1f; heat = 0f; ads = false; throwBackAt = -1f; pendingHitAt = -1f;
            Rebuild();
        }

        public void End()
        {
            run = null;
            if (meleeModel != null) Destroy(meleeModel.gameObject);
            if (gunModel != null) Destroy(gunModel.gameObject);
            meleeModel = gunModel = null;
            if (player != null) { player.fovScale = 1f; player.faceCamera = false; player.speedMul = 1f; player.scopeView = false; }
            if (Av != null) { Av.holdRight = false; Av.aimGun = false; Av.twoHanded = false; }
        }

        // Модели заново (купили, поставили обвес)
        public void Rebuild()
        {
            if (meleeModel != null) Destroy(meleeModel.gameObject);
            if (gunModel != null) Destroy(gunModel.gameObject);
            var m = ars.Melee;
            meleeModel = WeaponModel.Build(m, ars.Get(m.id), ars);
            if (Av != null && Av.v4 && Av.handR != null)
            {
                // скелет v4: рукоять в кулаке, клинок выходит со стороны большого пальца
                meleeModel.transform.SetParent(Av.handR, false);
                float gz = meleeModel.grip != null ? meleeModel.grip.localPosition.z : 0f;
                meleeModel.transform.localPosition = CharacterAnim.PalmR - new Vector3(0f, 0f, gz);
                meleeModel.transform.localRotation = Quaternion.identity;
            }
            else
            {
                var hand = Av != null && Av.elbowR != null ? Av.elbowR : transform;
                meleeModel.transform.SetParent(hand, false);
                meleeModel.transform.localPosition = new Vector3(0, -0.3f, 0.03f);
            }
            var g = ars.Gun;
            if (g != null) { gunModel = WeaponModel.Build(g, ars.Get(g.id), ars); gunModel.transform.SetParent(transform, false); }
            if (slot == 1 && g == null) slot = 0;
            ApplySlot();
        }

        void ApplySlot()
        {
            if (meleeModel != null) meleeModel.gameObject.SetActive(slot == 0 && !KnifeAway);
            if (gunModel != null) gunModel.gameObject.SetActive(slot == 1);
            if (Av != null) { Av.holdRight = slot == 0; Av.aimGun = slot == 1; Av.twoHanded = slot == 1 && gunModel != null && gunModel.twoHanded; }
            player.faceCamera = slot == 1;
            reloadStart = -1f;
        }

        public void Switch(int to)
        {
            if (to == slot)
            {
                // повторное нажатие 1 или 2 — следующее купленное оружие этого вида (нож ↔ бита ↔ катана)
                var cur = Current; var next = ars.NextOwned(cur.Melee, cur.id);
                if (next == null || Reloading) return;
                ars.Equip(next); ads = false; nextAt = Mathf.Max(nextAt, Time.time + 0.3f);
                Rebuild();
                if (run != null && run.Say != null) run.Say("В руках: " + Current.name);
                return;
            }
            if (to == 1 && ars.Gun == null) { if (run != null && run.Say != null) run.Say("Огнестрела нет — купи в оружейной «Железо» на площади."); return; }
            slot = to; ads = false; nextAt = Mathf.Max(nextAt, Time.time + 0.25f);
            ApplySlot();
        }

        // Ввод и стрельба; active — игрок управляет (не пауза, не магазин)
        public void Tick(float dt, bool active)
        {
            if (run == null || player == null) return;
            spreadKick = Mathf.MoveTowards(spreadKick, 0f, dt * 6f);
            if (!Overheated) heat = Mathf.MoveTowards(heat, 0f, dt * (Time.time - lastShotAt > 0.25f ? 30f : 0f));
            if (throwBackAt > 0f && Time.time >= throwBackAt) { throwBackAt = -1f; ApplySlot(); }
            if (pendingHitAt > 0f && Time.time >= pendingHitAt) { pendingHitAt = -1f; MeleeHit(Current, 1f); }
            if (Reloading && Time.time - reloadStart >= reloadDur) FinishReload();

            var def = Current;
            ads = active && !def.Melee && InputX.AimHeld() && !Reloading;
            player.fovScale = ads ? 1f / ars.Zoom(def) : 1f;
            player.scopeView = Scoped;
            float slow = ads ? 0.6f : 1f;
            player.speedMul = run.PlayerSpeedMul * slow;
            if (Av != null) Av.aimPitch = player.CamPitch;
            if (!active) return;

            if (InputX.Slot1()) Switch(0);
            if (InputX.Slot2()) Switch(1);
            float wheel = InputX.Scroll();
            if (Mathf.Abs(wheel) > 0.01f && !ads) Switch(slot == 0 ? 1 : 0);
            if (InputX.Reload()) StartReload();

            if (def.Melee)
            {
                if (InputX.Attack() && Time.time >= nextAt && !KnifeAway) Swing(def);
                if (InputX.AimPressed()) MeleeSpecial(def);
                return;
            }
            bool trigger = def.auto ? InputX.AttackHeld() : InputX.Attack();
            if (trigger && Time.time >= nextAt) Fire(def);
        }

        // ---------- ближний бой ----------
        void Swing(WeaponDef def)
        {
            nextAt = Time.time + 1f / def.rate;
            player.FaceYaw(player.CamYaw);
            if (Av != null) Av.swingStart = Time.time;
            pendingHitAt = Time.time + (def.id == "knife" ? 0.12f : 0.18f);
        }

        void MeleeHit(WeaponDef def, float mult)
        {
            if (run == null) return;
            var fwd = Quaternion.Euler(0, player.CamYaw, 0) * Vector3.forward;
            var c = player.Position + Vector3.up * 1.0f + fwd * def.reach;
            CityNpc best = null; float bd = 99f;
            foreach (var col in Physics.OverlapSphere(c, 0.9f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var n = col.GetComponentInParent<CityNpc>();
                if (n == null || !n.Alive) continue;
                var to = n.transform.position - player.Position; to.y = 0;
                float d = to.magnitude;
                if (d < bd && (d < 0.5f || Vector3.Dot(to / d, fwd) > 0.1f)) { bd = d; best = n; }
            }
            if (best == null) return;
            float knock = def.knockback, stun = def.id == "bat" && ars.Perk(def) ? 2f : def.knockback > 0f ? 0.8f : 0f;
            best.Hit(Mathf.RoundToInt(ars.Damage(def) * mult), player.Position, best.transform.position + Vector3.up * 1.2f, fwd, knock, stun);
        }

        void MeleeSpecial(WeaponDef def)
        {
            if (def.id == "katana" && Time.time >= dashReadyAt) { dashReadyAt = Time.time + 3f; StartCoroutine(Dash(def, ars.Perk(def) ? 7.5f : 5f)); }
            else if (def.id == "knife" && ars.Perk(def) && !KnifeAway) ThrowKnife(def);
        }

        System.Collections.IEnumerator Dash(WeaponDef def, float dist)
        {
            var fwd = Quaternion.Euler(0, player.CamYaw, 0) * Vector3.forward;
            player.FaceYaw(player.CamYaw);
            if (Av != null) Av.swingStart = Time.time;
            var hit = new HashSet<CityNpc>();
            float dur = 0.2f, done = 0f;
            while (done < dist)
            {
                float step = Mathf.Min(dist - done, dist / dur * Time.deltaTime);
                player.Dash(fwd * step); done += step;
                foreach (var col in Physics.OverlapSphere(player.Position + Vector3.up, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    var n = col.GetComponentInParent<CityNpc>();
                    if (n != null && n.Alive && hit.Add(n)) n.Hit(ars.Damage(def), player.Position, n.transform.position + Vector3.up * 1.2f, fwd, 0.6f, 0.5f);
                }
                yield return null;
            }
        }

        void ThrowKnife(WeaponDef def)
        {
            throwBackAt = Time.time + 2f; ApplySlot();
            var from = player.Position + Vector3.up * 1.4f;
            var dir = (player.AimPoint(25f) - from).normalized;
            var go = WeaponModel.Build(def, null, null).gameObject;
            go.transform.position = from;
            var f = go.AddComponent<ThrownKnife>(); f.dir = dir; f.damage = ars.Damage(def) * 2; f.from = player.Position;
        }

        // ---------- огнестрел ----------
        void Fire(WeaponDef def)
        {
            if (Reloading) return;
            if (Overheated) return;
            var w = CurrentSave;
            if (MagNow <= 0) { StartReload(); nextAt = Time.time + 0.2f; return; }
            nextAt = Time.time + 1f / def.rate;
            w.mag--; lastShotAt = Time.time;
            if (ars.Overheats(def)) { heat += 1f; if (heat >= 60f) { overheatUntil = Time.time + 2.2f; if (run.Say != null) run.Say("Пулемёт перегрелся!"); } }

            var cam = player.cam.transform;
            float spread = SpreadNow;
            var muzzle = gunModel != null && gunModel.muzzle != null ? gunModel.muzzle.position : cam.position + cam.forward * 0.5f;
            int mask = Physics.DefaultRaycastLayers & ~(1 << 2);
            for (int p = 0; p < Mathf.Max(1, def.pellets); p++)
            {
                var dir = Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), cam.up) * Quaternion.AngleAxis(UnityEngine.Random.Range(-spread, spread), cam.right) * cam.forward;
                var hits = Physics.RaycastAll(cam.position, dir, def.range, mask, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                Vector3 end = cam.position + dir * def.range;
                int pierce = ars.Penetrates(def) ? 2 : 1;
                foreach (var h in hits)
                {
                    // не попадаем в то, что между камерой и игроком (за спиной в режиме от третьего лица)
                    if (Vector3.Dot(h.point - player.Position, cam.forward) < -0.2f) continue;
                    var n = h.collider.GetComponentInParent<CityNpc>();
                    if (n != null)
                    {
                        if (!n.Alive) { n.Shove(h.point, dir * (10f + ars.Damage(def) * 0.5f)); continue; }   // лежащее тело толкает, пуля летит дальше
                        n.Hit(ars.Damage(def), player.Position, h.point, dir, def.id == "shotgun" ? 0.25f : 0f, 0f);
                        end = h.point;
                        if (--pierce > 0) continue;
                        break;
                    }
                    end = h.point;
                    Gore.Dust(h.point, h.normal);
                    break;
                }
                Fx.Tracer(muzzle, end);
            }
            Fx.Flash(muzzle, gunModel != null ? gunModel.transform.forward : cam.forward);
            float rec = ars.Recoil(def);
            player.AddRecoil(rec * UnityEngine.Random.Range(0.7f, 1.1f), rec * UnityEngine.Random.Range(-0.35f, 0.35f));
            spreadKick = Mathf.Min(spreadKick + rec * 0.35f, 4f);
            if (Av != null) Av.kickAt = Time.time;
            run.Noise(player.Position, ars.NoiseRange(def));
            if (w.mag <= 0 && Reserve > 0) StartReload();
        }

        public void StartReload()
        {
            var def = Current;
            if (def.Melee || Reloading) return;
            if (MagNow >= MagSize) return;
            if (Reserve <= 0) { if (run != null && run.Say != null) run.Say("Патроны кончились. Ларёк с патронами — на площади."); return; }
            reloadStart = Time.time; reloadDur = ars.ReloadTime(def);
        }

        void FinishReload()
        {
            reloadStart = -1f;
            var def = Current; var w = CurrentSave; if (w == null) return;
            int need = MagSize - Mathf.Max(0, w.mag), take = Mathf.Min(need, Reserve);
            ars.AddAmmo(def.ammo, -take); w.mag = Mathf.Max(0, w.mag) + take;
        }

        // Ствол в руке смотрит туда, куда целится камера
        void LateUpdate()
        {
            if (run == null || gunModel == null || slot != 1 || Av == null || Av.elbowR == null) return;
            bool palm = Av.v4 && Av.handR != null;
            var hand = palm ? Av.handR.TransformPoint(CharacterAnim.PalmR) : Av.elbowR.TransformPoint(new Vector3(0, -0.27f, 0.03f));
            var aim = player.AimPoint(60f);
            var dir = aim - hand; if (dir.sqrMagnitude < 0.25f) dir = player.cam.transform.forward;
            gunModel.transform.position = hand;
            gunModel.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            // скелет v4: рукоять — в кулаке (кисть обхватывает её верх), а не начало ствола в запястье
            if (palm && gunModel.grip != null)
                gunModel.transform.position += hand - gunModel.transform.TransformPoint(gunModel.grip.localPosition + new Vector3(0f, 0.02f, 0f));
        }
    }

    // Метательный нож (перк 5-го уровня)
    public class ThrownKnife : MonoBehaviour
    {
        public Vector3 dir, from; public int damage;
        float travelled;
        void Update()
        {
            float step = 18f * Time.deltaTime;
            RaycastHit h;
            int mask = Physics.DefaultRaycastLayers & ~(1 << 2);
            if (Physics.SphereCast(transform.position, 0.12f, dir, out h, step, mask, QueryTriggerInteraction.Ignore))
            {
                var n = h.collider.GetComponentInParent<CityNpc>();
                if (n != null && n.Alive) n.Hit(damage, from, h.point, dir, 0f, 0f);
                Destroy(gameObject); return;
            }
            transform.position += dir * step; travelled += step;
            transform.Rotate(1080f * Time.deltaTime, 0, 0, Space.Self);
            if (travelled > 25f) Destroy(gameObject);
        }
    }

    // Трассеры и вспышки выстрелов
    public static class Fx
    {
        static Material tracerMat, flashMat;

        public static void Tracer(Vector3 a, Vector3 b)
        {
            if (tracerMat == null) tracerMat = Look.FxMat(new Color(1f, 0.85f, 0.4f, 0.9f), false, true);
            if (tracerMat == null) return;
            var go = new GameObject("Tracer");
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = tracerMat; lr.positionCount = 2; lr.SetPosition(0, a); lr.SetPosition(1, b);
            lr.startWidth = 0.025f; lr.endWidth = 0.01f; lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; lr.receiveShadows = false;
            UnityEngine.Object.Destroy(go, 0.05f);
        }

        public static void Flash(Vector3 at, Vector3 fwd)
        {
            var go = Look.Prim("MuzzleFlash", null, PrimitiveType.Sphere, at + fwd * 0.05f, new Vector3(0.1f, 0.1f, 0.18f), Pal.Hex("FFE08A"), false, 0f, 3f, false);
            go.transform.rotation = Quaternion.LookRotation(fwd); go.layer = 2;
            UnityEngine.Object.Destroy(go, 0.04f);
        }
    }
}
