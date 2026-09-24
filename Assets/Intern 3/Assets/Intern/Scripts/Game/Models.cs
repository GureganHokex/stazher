// Загрузка моделей из Blender (Resources/Models): офис и персонажи в стиле low-poly.
// Если моделей нет — игра строит офис из кода, как раньше.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public static class ModelLib
    {
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
        static Material fallback;
        static Texture2D ideTex, codeTex;

        public static GameObject Prefab(string path) { return Resources.Load<GameObject>("Models/" + path); }
        public static bool HasOffice { get { return Prefab("Office") != null; } }
        public static bool HasCharacter(string n) { return Prefab("Characters/" + n) != null; }

        // «Head.001» → «Head», «Leaf (3)» → «Leaf»
        public static string Clean(string n)
        {
            int i = n.IndexOf('.'); if (i >= 0) n = n.Substring(0, i);
            int j = n.IndexOf(" ("); if (j >= 0) n = n.Substring(0, j);
            return n;
        }

        public static Transform Find(Transform root, string name)
        {
            if (Clean(root.name) == name) return root;
            foreach (Transform c in root) { var r = Find(c, name); if (r != null) return r; }
            return null;
        }

        static Shader Lit
        {
            get
            {
                var rp = GraphicsSettings.currentRenderPipeline;
                return rp != null && rp.defaultShader != null ? rp.defaultShader : Shader.Find("Standard");
            }
        }

        public static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        public static void Emit(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
        }

        static void Matte(Material m, bool shiny)
        {
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", shiny ? 0.75f : 0.08f);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", shiny ? 0.75f : 0.08f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        }

        static float EmissionFor(string n)
        {
            if (n.Contains("screen")) return 1.1f;
            if (n.Contains("neon")) return 2.2f;
            if (n.Contains("bulb")) return 2.0f;
            if (n.Contains("sky") || n.Contains("cloud") || n.Contains("hill")) return 0.7f;
            if (n.Contains("led") || n.Contains("btn") || n.Contains("codeb") || n.Contains("codec") || n.Contains("codeink") || n.Contains("dbg")) return 1.0f;
            return 0f;
        }

        // Импортированный материал → матовый материал конвейера того же цвета (один раз на исходник)
        public static Material Convert(Material src)
        {
            if (src == null)
            {
                if (fallback == null) { fallback = new Material(Lit) { name = "lp_fallback" }; SetColor(fallback, new Color(0.45f, 0.3f, 0.2f)); Matte(fallback, false); }
                return fallback;
            }
            Material m;
            if (converted.TryGetValue(src, out m) && m != null) return m;
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : (src.HasProperty("_Color") ? src.color : Color.gray);
            c.a = 1;
            m = new Material(Lit) { name = src.name + "_lp" };
            SetColor(m, c);
            string n = src.name.ToLowerInvariant();
            Matte(m, n.Contains("mirror") || n.Contains("chrome") || n.Contains("water"));
            if (n.Contains("mirror") && m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.9f);
            float e = EmissionFor(n);
            if (e > 0) Emit(m, c * e);
            converted[src] = m;
            return m;
        }

        public static void ConvertAll(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = Convert(mats[i]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        public static Texture2D IdeTexture { get { if (ideTex == null) ideTex = Resources.Load<Texture2D>("Textures/IDE_Screen"); return ideTex; } }
        public static Texture2D CodeTexture { get { if (codeTex == null) codeTex = Resources.Load<Texture2D>("Textures/CodeScroll"); return codeTex; } }

        // Экран монитора как отдельный прямоугольник: смотрит на зрителя, чуть впереди корпуса, с правильной развёрткой.
        // У экранов из Blender нормаль смотрит внутрь монитора (их не видно) и нет нормальной развёртки — поэтому свой.
        // viewer — точка, откуда смотрят (кресло); иначе направление берём «от задней крышки к экрану».
        public static Transform FrontQuad(Renderer src, Vector3? viewer, Vector3? backCenter, Material mat, IList<Renderer> scan)
        {
            var scr = src.transform;
            var mf = scr.GetComponent<MeshFilter>();
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.97f, 0.55f, 0.001f));
            bool wideX = b.size.x >= b.size.z;
            Vector3 c = scr.TransformPoint(b.center);
            float w = scr.TransformVector(wideX ? new Vector3(b.size.x, 0, 0) : new Vector3(0, 0, b.size.z)).magnitude;
            float h = scr.TransformVector(new Vector3(0, b.size.y, 0)).magnitude;
            Vector3 n = viewer.HasValue ? viewer.Value - c : backCenter.HasValue ? c - backCenter.Value : -scr.forward;
            n.y = 0; n = n.sqrMagnitude > 1e-6f ? n.normalized : -scr.forward;
            var rot = Quaternion.LookRotation(-n, Vector3.up);
            Vector3 right = rot * Vector3.right, up = Vector3.up;
            // передняя плоскость корпуса — встаём на 3 мм перед ней
            float front = 0f;
            if (scan != null)
                foreach (var r in scan)
                {
                    if (r == null || r == src) continue;
                    string rn = Clean(r.name);
                    if (!(rn.Contains("Mon") || rn.Contains("Screen") || rn.Contains("Bezel"))) continue;
                    if ((r.bounds.center - c).sqrMagnitude > 0.8f * 0.8f) continue;
                    float d0, d1, x0, x1, y0, y1; Project(r.bounds, c, n, right, up, out d0, out d1, out x0, out x1, out y0, out y1);
                    if (x1 < -w * 0.5f || x0 > w * 0.5f || y1 < -h * 0.5f || y0 > h * 0.5f) continue;
                    front = Mathf.Max(front, Mathf.Min(d1, 0.12f));
                }
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = Clean(src.name) + "_Front";
            var col = go.GetComponent<Collider>(); if (col != null) Object.Destroy(col);
            go.transform.SetPositionAndRotation(c + n * (front + 0.003f), rot);
            go.transform.localScale = new Vector3(w, h, 1f);
            if (scr.parent != null) go.transform.SetParent(scr.parent, true);
            var qr = go.GetComponent<MeshRenderer>();
            qr.sharedMaterial = mat;
            qr.shadowCastingMode = ShadowCastingMode.Off;
            src.enabled = false;
            // стикеры, наклеенные поверх экрана, сдвигаем на рамку — вверх или вниз
            if (scan != null)
                foreach (var r in scan)
                {
                    if (r == null) continue;
                    string rn = Clean(r.name);
                    if (!(rn.Contains("Sticky") || rn.Contains("Note"))) continue;
                    var rb = r.bounds;
                    if ((rb.center - c).sqrMagnitude > 0.8f * 0.8f || rb.size.x > 0.3f || rb.size.y > 0.3f || rb.size.z > 0.3f) continue;
                    float d0, d1, x0, x1, y0, y1; Project(rb, c, n, right, up, out d0, out d1, out x0, out x1, out y0, out y1);
                    if (d1 < -0.01f || d0 > 0.15f) continue;
                    float ox = Mathf.Min(x1, w * 0.5f) - Mathf.Max(x0, -w * 0.5f), oy = Mathf.Min(y1, h * 0.5f) - Mathf.Max(y0, -h * 0.5f);
                    if (ox <= 0 || oy <= 0) continue;
                    float dy = (y0 + y1) > 0 ? (h * 0.5f - y0) + 0.004f : -(y1 + h * 0.5f) - 0.004f;
                    r.transform.position += up * dy + n * Mathf.Max(0f, front + 0.004f - d0);
                }
            return go.transform;
        }

        static void Project(Bounds rb, Vector3 c, Vector3 n, Vector3 right, Vector3 up, out float d0, out float d1, out float x0, out float x1, out float y0, out float y1)
        {
            d0 = x0 = y0 = float.MaxValue; d1 = x1 = y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var v = new Vector3((i & 1) == 0 ? rb.min.x : rb.max.x, (i & 2) == 0 ? rb.min.y : rb.max.y, (i & 4) == 0 ? rb.min.z : rb.max.z) - c;
                float d = Vector3.Dot(v, n), x = Vector3.Dot(v, right), y = Vector3.Dot(v, up);
                d0 = Mathf.Min(d0, d); d1 = Mathf.Max(d1, d); x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
        }

        public static Material ScreenMaterial(Texture tex, float emission, Color tint)
        {
            var m = new Material(Lit) { name = "lp_screen" };
            SetColor(m, tint);
            if (tex != null)
            {
                m.mainTexture = tex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
            }
            Matte(m, false);
            Emit(m, tint * emission);
            return m;
        }
    }

    // Прокрутка кода на экранах коллег — «работают»
    public class ScreenScroller : MonoBehaviour
    {
        Material m; float off, speed;
        void Start()
        {
            m = GetComponent<Renderer>().material;
            m.mainTextureScale = new Vector2(1, 0.28f);
            off = Random.value; speed = Random.Range(0.012f, 0.03f);
        }
        void Update()
        {
            off = Mathf.Repeat(off + speed * Time.deltaTime, 1f);
            m.mainTextureOffset = new Vector2(0, -off);
        }
    }

    // Офис из Blender: ставим, красим, добавляем коллизии и интерактив
    public static class ImportedOffice
    {
        static readonly string[] NoCollide = {
            "Plank", "Rug", "DeskRug", "Runner", "Leaf", "FPLeaf", "Stem", "FPStem", "Key", "Sprinkle", "Code", "Note", "Sticky",
            "Cloud", "Sky", "Hill", "Mull", "Poster", "Sign", "Tick", "Cable", "Cord", "Pendant", "Bulb", "Steam", "Paper", "Pebble",
            "Heart", "Crema", "Coffee", "Spoon", "Book", "Vent", "Knob", "Grille", "Button", "Lace", "Lens", "Cactus", "Flower",
            "Mouse", "Keyboard", "Mug", "Saucer", "Duck", "Magazine", "Laptop", "Hand", "Clock", "Min", "Hour", "Marker", "Col", "Line",
            "Ceiling", "Window", "Win", "Sill", "Rail", "Skirt", "Low", "Band", "Donut", "Icing", "Plate", "Cup", "Hopper", "Gauge", "Needle"
        };

        static readonly string[] ChairNames = { "Seat", "Back", "BackArm", "Pole", "Hub", "StarLeg", "Wheel" };

        static bool Skip(string n)
        {
            foreach (var s in NoCollide) if (n.Contains(s)) return true;
            return false;
        }

        public static OfficeRefs Build()
        {
            var refs = new OfficeRefs();
            var office = Object.Instantiate(ModelLib.Prefab("Office"));
            office.name = "Office";
            // Blender (+Y к окнам) после FBX оказывается повернут на 180° — возвращаем как в игре
            office.transform.rotation = Quaternion.Euler(0, 180, 0);
            ModelLib.ConvertAll(office);

            var playerScreen = (Transform)null; Vector3 seat = new Vector3(-5, 0, 1.55f);
            var chairParts = new List<Transform>();
            var screens = new List<Renderer>();
            var all = office.GetComponentsInChildren<MeshRenderer>(true);
            var byName = new Dictionary<string, Renderer>();
            foreach (var r in all) byName[ModelLib.Clean(r.name)] = r;
            foreach (var r in all)
            {
                string n = ModelLib.Clean(r.name);
                var go = r.gameObject;
                if (n.Contains("Steam")) { r.enabled = false; continue; }
                if (n.StartsWith("Wall") || n.StartsWith("Ceiling") || n.StartsWith("Win") || n.StartsWith("Sky") || n.StartsWith("Mull") ||
                    n.StartsWith("Cloud") || n.StartsWith("Hill") || n.Contains("Pendant") || n.Contains("Bulb") || n.StartsWith("Cord"))
                    r.shadowCastingMode = ShadowCastingMode.Off;

                bool isScreen = n.EndsWith("MonScreen") || n.EndsWith("LaptopScreen");
                if (isScreen)
                {
                    if (n.StartsWith("Desk_Player")) { playerScreen = r.transform; r.sharedMaterial = ModelLib.ScreenMaterial(ModelLib.IdeTexture, 0.9f, Color.white); }
                    else r.sharedMaterial = ModelLib.ScreenMaterial(ModelLib.CodeTexture, 1.3f, new Color(0.85f, 0.9f, 1f));
                    if (n.EndsWith("MonScreen")) screens.Add(r);
                    else if (!n.StartsWith("Desk_Player")) go.AddComponent<ScreenScroller>();
                    continue;
                }
                if (n == "Desk_Player__Seat") seat = r.bounds.center;
                if (n.StartsWith("Desk_Player__") && System.Array.IndexOf(ChairNames, n.Substring(13)) >= 0) { chairParts.Add(r.transform); continue; }
                if (n == "Desk_Player__MonPanel") { go.AddComponent<BoxCollider>(); go.AddComponent<ComputerDesk>(); continue; }
                if (n == "EspBody") { go.AddComponent<BoxCollider>(); go.AddComponent<CoffeeMachine>(); continue; }
                if (n == "LockerBody") { go.AddComponent<BoxCollider>(); go.AddComponent<Wardrobe>(); continue; }

                var size = r.bounds.size;
                bool floor = n == "Floor";
                if (floor || (!Skip(n) && size.y > 0.2f && (size.x > 0.15f || size.z > 0.15f)))
                    go.AddComponent<BoxCollider>();
            }

            // Экраны мониторов: свои прямоугольники перед корпусом (у экранов из модели нормаль смотрит внутрь)
            foreach (var r in screens)
            {
                string n = ModelLib.Clean(r.name);
                string prefix = n.Substring(0, n.Length - "MonScreen".Length);
                Renderer back; byName.TryGetValue(prefix + "MonBack", out back);
                var q = ModelLib.FrontQuad(r, null, back != null ? back.bounds.center : (Vector3?)null, r.sharedMaterial, all);
                if (r.transform == playerScreen) refs.screenQuad = q;
                else q.gameObject.AddComponent<ScreenScroller>();
            }

            // Метки и опорные точки
            refs.screen = playerScreen;
            if (playerScreen != null)
            {
                var mf = playerScreen.GetComponent<MeshFilter>();
                var b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : new Vector3(0.97f, 0.55f, 0);
                refs.screenSize = new Vector2(Mathf.Max(b.x, b.z), b.y);
                // чтобы экран было проще «найти» и с другой стороны — отдельный коллайдер
                playerScreen.gameObject.AddComponent<BoxCollider>();
                playerScreen.gameObject.AddComponent<ComputerDesk>();
            }
            var deskAnchor = new GameObject("PlayerDeskAnchor").transform;
            deskAnchor.position = new Vector3(seat.x, 0, seat.z + 0.95f);
            refs.playerDesk = deskAnchor;
            var chair = new GameObject("PlayerChairAnchor").transform;
            chair.position = new Vector3(seat.x, 0.5f, seat.z);
            refs.playerChair = chair;
            // кресло целиком: точка опоры — центр основания, под сиденьем
            var chairObj = new GameObject("PlayerChair").transform;
            chairObj.position = new Vector3(seat.x, 0, seat.z);
            foreach (var t in chairParts) t.SetParent(chairObj, true);
            var col = chairObj.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.5f, -0.1f); col.size = new Vector3(0.6f, 1.0f, 0.6f);
            refs.chairObj = chairObj;

            refs.spawn = new GameObject("Spawn").transform; refs.spawn.position = new Vector3(-5, 0.1f, -4.8f);
            refs.lockerSpot = new GameObject("LockerSpot").transform; refs.lockerSpot.position = new Vector3(-8f, 0.1f, -6.1f);
            refs.board = OfficeBuilder.Label("", new Vector3(7.5f, 3.2f, 7.78f), 0.013f, Pal.Ink, office.transform.parent);
            refs.board.transform.position = new Vector3(7.5f, 3.2f, 7.78f);
            refs.board.transform.rotation = Quaternion.identity;

            Lighting();
            SpawnPeople(refs);
            Debug.Log("[Стажёр] Офис из Blender загружен: " + office.GetComponentsInChildren<MeshRenderer>(true).Length + " объектов, экран " + refs.screenSize);
            return refs;
        }

        static void Lighting()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = Pal.Hex("FFF3E0"); sun.intensity = 1.6f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.85f;
            sun.transform.rotation = Quaternion.Euler(50, 160, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Pal.Hex("C9DDF2");
            RenderSettings.ambientEquatorColor = Pal.Hex("E9E1D6");
            RenderSettings.ambientGroundColor = Pal.Hex("A89A86");
            RenderSettings.fog = false;
            // тёплый свет от подвесных ламп
            foreach (var x in new[] { -6f, 0f, 6f })
            {
                var pl = new GameObject("PendantLight").AddComponent<Light>();
                pl.type = LightType.Point; pl.range = 7; pl.intensity = 1.2f; pl.color = Pal.Hex("FFE2B0");
                pl.transform.position = new Vector3(x, 2.85f, 0.3f);
            }
        }

        // Коллеги за своими столами и Гена у доски
        static void SpawnPeople(OfficeRefs refs)
        {
            var seats = new[] {
                new { model = "Dev1", pos = new Vector3(0, 0, 1.55f) },
                new { model = "Dev2", pos = new Vector3(5, 0, 1.55f) },
                new { model = "Dev3", pos = new Vector3(-5, 0, -2.95f) },
                new { model = "Dev4", pos = new Vector3(5, 0, -2.95f) },
            };
            foreach (var s in seats)
            {
                if (!ModelLib.HasCharacter(s.model)) continue;
                var a = CharacterAnim.Spawn(s.model, null, s.pos, 0, null);
                a.SetSitInstant(1); a.typing = true; a.lookAtPlayer = true;
            }
            if (ModelLib.HasCharacter("Gena"))
            {
                var lead = CharacterAnim.Spawn("Gena", null, new Vector3(8.2f, 0, 5.6f), 180, null);
                lead.lookAtPlayer = true;
                var cap = lead.gameObject.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.95f, 0); cap.height = 1.9f; cap.radius = 0.35f;
                lead.gameObject.AddComponent<TeamLeadNpc>();
                refs.lead = lead;
                var tag = OfficeBuilder.Label("Тимлид Гена", new Vector3(8.2f, 2.35f, 5.6f), 0.017f, Pal.Ink);
                tag.gameObject.AddComponent<Billboard>();
            }
        }
    }
}
