// Модели оружия из Blender (спринт 8 версии 0.9 «Оружие в руках»): Art/weapons_v1.py → Resources/Models/Weapons/<id>.fbx.
// Девять видов по настоящим образцам (Glock 17, MP5, Remington 870, M4, Remington 700, M249, KA-BAR, бита, катана) и обвесы.
// Модель выравнивается по пустышкам A_AxisX/Y/Z: ось z — вперёд по стволу, y — вверх, x — вправо, 1 единица = 1 м.
// Точки A_*: GripR — кулак правой руки, GripL — левая рука, Muzzle, Eye (линия прицела), Eject (окно выброса), MagWell,
// Shell (окно заряжания дробовика), Sight/Rail/Barrel (обвесы), Stock (затыльник), Tip/Edge (острие и лезвие).
// Подвижные детали двигаются в осях оружия от положения покоя: Move(деталь, смещение, поворот вокруг своей оси).
// Если модели нет — остаётся прежняя модель из брусков (Weapons.cs).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public partial class WeaponModel
    {
        public bool imported;
        public Transform gripL, eye, eject, shellPort, stockPt, tip, edge, barrelEnd, model;
        public Transform magPart, slide, bolt, charge, trigger, pump, boltHandle, cover, belt, ammoBox, dustCover;
        readonly Dictionary<Transform, Pose> rest = new Dictionary<Transform, Pose>();
        Renderer[] magRenderers = new Renderer[0];

        static readonly Dictionary<string, Material> wmats = new Dictionary<string, Material>();
        static GameObject attachPrefab; static bool attachTried;
        static readonly HashSet<string> logged = new HashSet<string>();

        static WeaponModel BuildImported(WeaponDef def, WeaponSave save, Arsenal ars)
        {
            var pf = ModelLib.Prefab("Weapons/" + def.id);
            if (pf == null) return null;
            var go = new GameObject("Weapon_" + def.id);
            var m = go.AddComponent<WeaponModel>(); m.id = def.id; m.melee = def.Melee; m.imported = true;
            var inst = Instantiate(pf, go.transform, false);
            inst.name = "Model"; m.model = inst.transform;
            string how = Calibrate(go.transform, inst.transform, "W_" + def.id);
            if (logged.Add(def.id)) Debug.Log("[Стажёр] оружие " + def.id + ": модель из Blender, " + how);
            Paint(inst, Golden);
            System.Func<string, Transform> A = n => ModelLib.Find(inst.transform, "A_" + n);
            System.Func<string, Transform> P = n => ModelLib.Find(inst.transform, n);
            m.grip = A("GripR"); m.gripL = A("GripL"); m.muzzle = A("Muzzle") ?? A("Tip"); m.eye = A("Eye"); m.eject = A("Eject");
            m.shellPort = A("Shell"); m.magPoint = A("MagWell"); m.rail = A("Rail"); m.sight = A("Sight"); m.barrelEnd = A("Barrel");
            m.stockPt = A("Stock"); m.tip = A("Tip"); m.edge = A("Edge");
            m.magPart = P("Mag"); m.slide = P("Slide"); m.bolt = P("Bolt"); m.charge = P("Charge"); m.trigger = P("Trigger"); m.pump = P("Pump");
            m.boltHandle = P("BoltHandle"); m.cover = P("Cover"); m.belt = P("Belt"); m.ammoBox = P("AmmoBox"); m.dustCover = P("DustCover");
            foreach (var t in new[] { m.magPart, m.slide, m.bolt, m.charge, m.trigger, m.pump, m.boltHandle, m.cover, m.belt, m.ammoBox, m.dustCover })
                if (t != null) m.rest[t] = new Pose(t.localPosition, t.localRotation);
            if (m.magPart != null) m.magRenderers = m.magPart.GetComponentsInChildren<Renderer>(true);
            m.twoHanded = !def.Melee && def.id != "pistol";
            if (!def.Melee && ars != null && save != null) m.AttachImported(save, ars);
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 2;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = ShadowCastingMode.On;
            return m;
        }

        // Выравнивание импорта: пустышки осей → оси держателя, 10 см → 0,1 единицы, начало координат — в ноль
        static string Calibrate(Transform holder, Transform inst, string rootName)
        {
            var O = ModelLib.Find(inst, rootName) ?? inst;
            var ax = ModelLib.Find(inst, "A_AxisX"); var ay = ModelLib.Find(inst, "A_AxisY"); var az = ModelLib.Find(inst, "A_AxisZ");
            if (ax == null || ay == null || az == null) return "нет пустышек осей";
            Vector3 o = holder.InverseTransformPoint(O.position);
            Vector3 x = holder.InverseTransformPoint(ax.position) - o, y = holder.InverseTransformPoint(ay.position) - o, z = holder.InverseTransformPoint(az.position) - o;
            if (z.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f) return "оси нулевые";
            float s = 0.1f / z.magnitude;
            var q = Quaternion.Inverse(Quaternion.LookRotation(z, y));
            inst.localPosition = q * (inst.localPosition - o) * s;
            inst.localRotation = q * inst.localRotation;
            inst.localScale = inst.localScale * s;
            bool mirror = (q * x).x < 0f;
            if (mirror)
            {
                var mir = new GameObject("Mirror").transform; mir.SetParent(holder, false); mir.localScale = new Vector3(-1f, 1f, 1f);
                inst.SetParent(mir, false);
            }
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "масштаб {0:0.###}{1}", s, mirror ? ", отражение по x" : "");
        }

        // Материалы по именам из Blender: металл с блеском, матовый полимер, дерево, латунь, линзы, светящаяся точка
        struct WMat { public float smooth, metal, emit; public WMat(float s, float m, float e = 0f) { smooth = s; metal = m; emit = e; } }
        static readonly Dictionary<string, WMat> Kinds = new Dictionary<string, WMat>
        {
            { "wpn_metal", new WMat(0.55f, 0.45f) }, { "wpn_steel", new WMat(0.70f, 0.70f) }, { "wpn_dark", new WMat(0.35f, 0.2f) },
            { "wpn_polymer", new WMat(0.22f, 0f) }, { "wpn_wood", new WMat(0.40f, 0f) }, { "wpn_woodd", new WMat(0.40f, 0f) },
            { "wpn_brass", new WMat(0.75f, 0.75f) }, { "wpn_blade", new WMat(0.78f, 0.75f) }, { "wpn_edge", new WMat(0.88f, 0.8f) },
            { "wpn_grip", new WMat(0.10f, 0f) }, { "wpn_wrap", new WMat(0.12f, 0f) }, { "wpn_gold", new WMat(0.75f, 0.8f) },
            { "wpn_olive", new WMat(0.15f, 0f) }, { "wpn_lens", new WMat(0.95f, 0.2f, 0.15f) }, { "wpn_led", new WMat(0.5f, 0f, 3f) },
            { "wpn_rubber", new WMat(0.05f, 0f) }, { "wpn_tape", new WMat(0.08f, 0f) }, { "wpn_fde", new WMat(0.2f, 0f) },
            { "wpn_leather", new WMat(0.32f, 0f) }, { "wpn_leatherd", new WMat(0.32f, 0f) }, { "wpn_white", new WMat(0.3f, 0f, 0.6f) },
            { "wpn_mag", new WMat(0.32f, 0.15f) }, { "wpn_alu", new WMat(0.5f, 0.55f) },
        };
        static readonly HashSet<string> GoldParts = new HashSet<string> { "wpn_metal", "wpn_steel", "wpn_alu", "wpn_mag", "wpn_polymer", "wpn_wood", "wpn_woodd", "wpn_fde" };

        static void Paint(GameObject go, bool gold)
        {
            var rp = GraphicsSettings.currentRenderPipeline;
            var lit = rp != null && rp.defaultShader != null ? rp.defaultShader : Shader.Find("Standard");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var src = ms[i];
                    string n = src != null ? ModelLib.Clean(src.name).ToLowerInvariant() : "wpn_metal";
                    bool g = gold && GoldParts.Contains(n);
                    string key = n + (g ? "#gold" : "");
                    Material m;
                    if (!wmats.TryGetValue(key, out m) || m == null)
                    {
                        Color c = src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src != null && src.HasProperty("_Color") ? src.color : Color.gray;
                        c.a = 1f;
                        WMat k; if (!Kinds.TryGetValue(n, out k)) k = new WMat(0.3f, 0f);
                        if (g) { c = n == "wpn_metal" || n == "wpn_mag" ? Pal.Hex("B8861B") : n == "wpn_steel" ? Pal.Hex("FFD23F") : Pal.Hex("D9A441"); k = new WMat(0.75f, 0.85f); }
                        m = new Material(lit) { name = key };
                        ModelLib.SetColor(m, c);
                        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", k.smooth);
                        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", k.smooth);
                        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", k.metal);
                        if (k.emit > 0f) ModelLib.Emit(m, c * k.emit);
                        wmats[key] = m;
                    }
                    ms[i] = m;
                }
                r.sharedMaterials = ms;
            }
        }

        // ---------- обвесы ----------
        void AttachImported(WeaponSave save, Arsenal ars)
        {
            var sightId = ars.Installed(save, "sight"); var barrelId = ars.Installed(save, "barrel"); var magId = ars.Installed(save, "mag"); var railId = ars.Installed(save, "rail");
            if (!attachTried) { attachTried = true; attachPrefab = ModelLib.Prefab("Weapons/attach"); }
            if (attachPrefab == null) return;
            if (sightId == "reddot") Place("reddot", sight);
            if (sightId == "profiler") Place("profiler", sight);
            if (barrelId == "silencer" && barrelEnd != null)
            {
                Place("silencer", barrelEnd);
                if (muzzle != null) muzzle.position += transform.forward * 0.16f;
            }
            if (railId == "laser") { Place("laser", rail); Place("beam", rail); }
            if (railId == "grip") Place("grip", rail);
            if (magId == "heap" && magPart != null)
            {
                if (Place("drum", magPoint, magPart) != null) foreach (var r in magRenderers) r.enabled = false;
            }
            if (magId == "buffer" && magPart != null && magPoint != null)
            {
                // удлинитель магазина: накладка под горловиной, едет вместе с магазином
                var ext = Look.RBox("LongMag", magPart, Vector3.zero, new Vector3(0.026f, 0.05f, 0.05f), Pal.Hex("34363D"), 0.004f, false, 0.6f, 0f, false);
                ext.transform.position = magPoint.position - transform.up * 0.11f;
                ext.transform.rotation = transform.rotation;
                ext.transform.localScale = Vector3.Scale(ext.transform.localScale, new Vector3(1f / Mathf.Max(1e-4f, magPart.lossyScale.x), 1f / Mathf.Max(1e-4f, magPart.lossyScale.y), 1f / Mathf.Max(1e-4f, magPart.lossyScale.z)));
            }
        }

        Transform Place(string part, Transform at, Transform parent = null)
        {
            if (at == null || attachPrefab == null) return null;
            var tmp = new GameObject("tmp").transform; tmp.SetParent(transform, false);
            tmp.position = at.position; tmp.rotation = transform.rotation;
            var inst = Instantiate(attachPrefab, tmp, false);
            Calibrate(tmp, inst.transform, "W_attach");
            Paint(inst, Golden);
            var p = ModelLib.Find(inst.transform, part);
            if (p != null) p.SetParent(parent ?? transform, true);
            tmp.gameObject.SetActive(false); Destroy(tmp.gameObject);
            return p;
        }

        // ---------- подвижные детали ----------
        // Смещение (метры, оси оружия) и поворот (оси оружия, вокруг оси детали) от положения покоя
        public void Move(Transform t, Vector3 offset, Quaternion rot)
        {
            Pose p;
            if (t == null || !rest.TryGetValue(t, out p)) return;
            var par = t.parent;
            Vector3 offW = transform.TransformVector(offset);
            t.localPosition = p.position + (par != null ? par.InverseTransformVector(offW) : offW);
            Quaternion parRot = par != null ? par.rotation : Quaternion.identity;
            Quaternion qW = transform.rotation * rot * Quaternion.Inverse(transform.rotation);
            t.localRotation = Quaternion.Inverse(parRot) * (qW * (parRot * p.rotation));
        }
        public void Move(Transform t, Vector3 offset) { Move(t, offset, Quaternion.identity); }
        public void ResetParts() { foreach (var kv in rest) if (kv.Key != null) { kv.Key.localPosition = kv.Value.position; kv.Key.localRotation = kv.Value.rotation; } }
        public void ShowMag(bool on) { if (magPart != null) magPart.gameObject.SetActive(on); }

        // Точка в осях оружия (держателя)
        public Vector3 Local(Transform a) { return a != null ? transform.InverseTransformPoint(a.position) : Vector3.zero; }

        // Где деталь была бы в покое (мир) — цевьё ведёт за собой левую руку
        public Vector3 PartRestW(Transform t)
        {
            Pose p;
            if (t == null || !rest.TryGetValue(t, out p)) return t != null ? t.position : transform.position;
            return t.parent != null ? t.parent.TransformPoint(p.position) : p.position;
        }

        // Шарик рукояти затвора (винтовка): крайняя справа точка детали в покое, в её осях
        Vector3 knobLocal; bool knobReady;
        public Vector3 KnobW()
        {
            if (boltHandle == null) return grip != null ? grip.position : transform.position;
            if (!knobReady)
            {
                knobReady = true;
                var keep = new Pose(boltHandle.localPosition, boltHandle.localRotation);
                Pose p; if (rest.TryGetValue(boltHandle, out p)) { boltHandle.localPosition = p.position; boltHandle.localRotation = p.rotation; }
                Vector3 best = Vector3.zero; float bx = float.NegativeInfinity;
                foreach (var mf in boltHandle.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var l = transform.InverseTransformPoint(mf.transform.TransformPoint(c));
                        if (l.x > bx) { bx = l.x; best = l; }
                    }
                }
                // середина шарика: чуть внутрь от края и на высоте оси детали
                Vector3 axisL = transform.InverseTransformPoint(boltHandle.position);
                best = new Vector3(best.x - 0.009f, Mathf.Lerp(best.y, axisL.y, 0.5f), axisL.z);
                knobLocal = boltHandle.InverseTransformPoint(transform.TransformPoint(best));
                boltHandle.localPosition = keep.position; boltHandle.localRotation = keep.rotation;
            }
            return boltHandle.TransformPoint(knobLocal);
        }

        // Спрятать целиком (в оптике) и вернуть как было (у барабанного магазина родной магазин так и остаётся скрытым)
        readonly List<Renderer> hidden = new List<Renderer>(); bool shown = true;
        public void SetVisible(bool on)
        {
            if (on == shown) return;
            shown = on;
            if (!on) { hidden.Clear(); foreach (var r in GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; hidden.Add(r); } }
            else { foreach (var r in hidden) if (r != null) r.enabled = true; hidden.Clear(); }
        }
    }
}
