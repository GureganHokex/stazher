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

        public static Material ScreenMaterial(Texture2D tex, float emission, Color tint)
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
            foreach (var r in office.GetComponentsInChildren<MeshRenderer>(true))
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
                    else { r.sharedMaterial = ModelLib.ScreenMaterial(ModelLib.CodeTexture, 1.3f, new Color(0.85f, 0.9f, 1f)); go.AddComponent<ScreenScroller>(); }
                    continue;
                }
                if (n == "Desk_Player__Seat") seat = r.bounds.center;
                if (n == "Desk_Player__MonPanel") { go.AddComponent<BoxCollider>(); go.AddComponent<ComputerDesk>(); continue; }
                if (n == "EspBody") { go.AddComponent<BoxCollider>(); go.AddComponent<CoffeeMachine>(); continue; }
                if (n == "LockerBody") { go.AddComponent<BoxCollider>(); go.AddComponent<Wardrobe>(); continue; }

                var size = r.bounds.size;
                bool floor = n == "Floor";
                if (floor || (!Skip(n) && size.y > 0.2f && (size.x > 0.15f || size.z > 0.15f)))
                    go.AddComponent<BoxCollider>();
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
