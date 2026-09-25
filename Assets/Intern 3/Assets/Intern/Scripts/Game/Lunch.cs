// Обеденный перерыв (прототип v0.8): переулок у бизнес-центра, юристы (+10 монет) и бухгалтеры (−20), нож.
// Город строится кодом один раз, далеко от офиса, и дальше только включается и выключается.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public class CityDoor
    {
        public Vector3 pos;      // точка на тротуаре перед дверью (мировые координаты)
        public string kind;      // lawyer | accountant | any
    }

    public class CityRefs
    {
        public Transform root, spawn;
        public readonly List<CityDoor> doors = new List<CityDoor>();
        public Vector3 min, max;   // где можно ходить (мировые координаты)
    }

    public static class CityBuilder
    {
        public static readonly Vector3 Origin = new Vector3(0f, 0f, 300f);
        const float Half = 5f;          // полширины улицы от стены до стены
        const float Length = 50f;

        static readonly Dictionary<string, Material> facades = new Dictionary<string, Material>();

        public static CityRefs Build()
        {
            var r = new CityRefs();
            var root = new GameObject("City").transform; root.position = Origin;
            r.root = root;
            Color asphalt = Pal.Hex("4B4F66"), walk = Pal.Hex("A9A6B8"), mark = Pal.Hex("F4F1EA");

            // земля, тротуары, разметка
            Look.RBox("Ground", root, new Vector3(0, -0.05f, Length / 2), new Vector3(40f, 0.1f, 90f), asphalt, 0.02f, true, 0.3f);
            Look.RBox("WalkL", root, new Vector3(-Half + 1f, 0.012f, Length / 2), new Vector3(2f, 0.02f, Length), walk, 0.01f, false, 0.3f, 0f, false);
            Look.RBox("WalkR", root, new Vector3(Half - 1f, 0.012f, Length / 2), new Vector3(2f, 0.02f, Length), walk, 0.01f, false, 0.3f, 0f, false);
            for (float z = 3f; z < Length - 2f; z += 5f)
                Look.RBox("Mark", root, new Vector3(0, 0.012f, z), new Vector3(0.18f, 0.02f, 2.2f), mark, 0.01f, false, 0.2f, 0f, false);

            // дома по обе стороны: (z от, z до, высота, цвет, вывеска, кто выходит из двери)
            var left = new[] {
                new Bld(0, 12, 18, "E8B4A0", "Юридическая консультация", "lawyer"),
                new Bld(12, 26, 24, "A8C5E0", "Бухгалтерия", "accountant"),
                new Bld(26, 38, 15, "C9B8E8", "Нотариус", "lawyer"),
                new Bld(38, 50, 21, "F0D38C", "Шаурма", "any"),
            };
            var right = new[] {
                new Bld(0, 14, 21, "9ED9C4", "Адвокатское бюро", "lawyer"),
                new Bld(14, 24, 15, "E8A0B8", "Книги", "any"),
                new Bld(24, 38, 24, "B8C9A0", "Налоги и бухучёт", "accountant"),
                new Bld(38, 50, 18, "D9C4A8", "Юридический факультет", "lawyer"),
            };
            foreach (var b in left) Building(r, b, -1);
            foreach (var b in right) Building(r, b, 1);

            // бизнес-центр в начале улицы (оттуда выходит стажёр) и глухой дом в конце
            Look.RBox("BusinessCenter", root, new Vector3(0, 15f, -5f), new Vector3(26f, 30f, 10f), Pal.Hex("7FA6D6"), 0.1f, true, 0.6f);
            Facade(root, new Vector3(0, 16.5f, -0.02f), new Vector2(26f, 27f), 180f, Pal.Hex("7FA6D6"), 9, 9);
            Look.RBox("BCDoorFrame", root, new Vector3(0, 1.5f, 0.06f), new Vector3(3.2f, 3.0f, 0.12f), Pal.Hex("2B2D42"), 0.04f, false, 0.6f);
            Look.RBox("BCDoor", root, new Vector3(0, 1.4f, 0.14f), new Vector3(2.6f, 2.7f, 0.06f), Pal.Hex("BDE7FF"), 0.02f, false, 0.4f, 0.2f);
            var door = Look.RBox("OfficeDoor", root, new Vector3(0, 1.4f, 0.5f), new Vector3(3.0f, 2.8f, 0.8f), Pal.Hex("BDE7FF"), 0.02f, true, 0f);
            door.GetComponent<MeshRenderer>().enabled = false;
            door.GetComponent<BoxCollider>().isTrigger = true;
            door.AddComponent<CityOfficeDoor>();
            Sign(root, "CODEZILLA", new Vector3(0, 3.6f, 0.2f), 180f, Pal.Hex("FF4F9A"), 0.05f);

            Look.RBox("EndHouse", root, new Vector3(0, 10f, Length + 4f), new Vector3(26f, 20f, 8f), Pal.Hex("E6C9A8"), 0.1f, true, 0.6f);
            Facade(root, new Vector3(0, 11.5f, Length + 0.02f), new Vector2(26f, 17f), 0f, Pal.Hex("E6C9A8"), 9, 6);
            Look.RBox("EndDoor", root, new Vector3(0, 1.1f, Length - 0.04f), new Vector3(1.4f, 2.2f, 0.1f), Pal.Hex("6B4226"), 0.03f, false, 0.6f);
            Sign(root, "Суд", new Vector3(0, 2.8f, Length - 0.1f), 0f, Pal.Hex("2B2D42"), 0.035f);
            r.doors.Add(new CityDoor { pos = root.TransformPoint(new Vector3(0, 0, Length - 0.8f)), kind = "lawyer" });

            // фонари и скамейки
            for (float z = 6f; z < Length; z += 12f)
                foreach (int side in new[] { -1, 1 })
                {
                    var x = side * (Half - 0.35f);
                    Look.Prim("LampPole", root, PrimitiveType.Cylinder, new Vector3(x, 2f, z), new Vector3(0.12f, 2f, 0.12f), Pal.Hex("2B2D42"), true, 0.6f);
                    Look.Prim("LampHead", root, PrimitiveType.Sphere, new Vector3(x - side * 0.3f, 4.05f, z), new Vector3(0.45f, 0.3f, 0.45f), Pal.Hex("FFE7A8"), false, 0.4f, 1.2f);
                }
            Look.RBox("Bench", root, new Vector3(-Half + 0.45f, 0.25f, 20f), new Vector3(0.5f, 0.5f, 1.8f), Pal.Hex("8C6A4F"), 0.05f, true);
            Look.RBox("Bench", root, new Vector3(Half - 0.45f, 0.25f, 31f), new Vector3(0.5f, 0.5f, 1.8f), Pal.Hex("8C6A4F"), 0.05f, true);
            Look.RBox("Bin", root, new Vector3(Half - 0.4f, 0.45f, 9f), new Vector3(0.55f, 0.9f, 0.55f), Pal.Hex("3E7B5A"), 0.08f, true);
            Look.RBox("Bin", root, new Vector3(-Half + 0.4f, 0.45f, 44f), new Vector3(0.55f, 0.9f, 0.55f), Pal.Hex("3E7B5A"), 0.08f, true);

            r.spawn = new GameObject("CitySpawn").transform; r.spawn.SetParent(root, false); r.spawn.localPosition = new Vector3(0, 0.1f, 2.2f);
            r.min = root.TransformPoint(new Vector3(-Half + 0.6f, 0, 1.2f));
            r.max = root.TransformPoint(new Vector3(Half - 0.6f, 0, Length - 1.2f));
            // невидимые стены по краям, чтобы не выйти за город (крыши и щели между домами)
            Wall(root, new Vector3(-Half - 0.3f, 3f, Length / 2), new Vector3(0.6f, 6f, Length + 2f));
            Wall(root, new Vector3(Half + 0.3f, 3f, Length / 2), new Vector3(0.6f, 6f, Length + 2f));
            return r;
        }

        struct Bld
        {
            public float z0, z1, h; public string color, sign, kind;
            public Bld(float z0, float z1, float h, string color, string sign, string kind) { this.z0 = z0; this.z1 = z1; this.h = h; this.color = color; this.sign = sign; this.kind = kind; }
        }

        static void Building(CityRefs r, Bld b, int side)
        {
            var root = r.root;
            float depth = 8f, len = b.z1 - b.z0, zc = (b.z0 + b.z1) / 2f;
            float xc = side * (Half + depth / 2f), face = side * Half;
            var c = Pal.Hex(b.color);
            Look.RBox("House", root, new Vector3(xc, b.h / 2f, zc), new Vector3(depth, b.h, len - 0.2f), c, 0.1f, true, 0.6f);
            float yaw = side < 0 ? -90f : 90f;
            // окна выше первого этажа
            float upper = b.h - 4f;
            Facade(root, new Vector3(face - side * 0.02f, 4f + upper / 2f, zc), new Vector2(len - 0.6f, upper), yaw, c, Mathf.Max(2, Mathf.RoundToInt(len / 3f)), Mathf.Max(1, Mathf.RoundToInt(upper / 3f)));
            // первый этаж: тёмная полоса, витрина, дверь, вывеска
            var band = Color.Lerp(c, Pal.Hex("2B2D42"), 0.55f);
            Look.RBox("Shop", root, new Vector3(face - side * 0.05f, 1.75f, zc), new Vector3(0.1f, 3.5f, len - 0.6f), band, 0.02f, false, 0.5f);
            Look.RBox("Window", root, new Vector3(face - side * 0.11f, 1.6f, zc + len * 0.22f), new Vector3(0.04f, 1.9f, len * 0.34f), Pal.Hex("BDE7FF"), 0.02f, false, 0.3f, 0.15f);
            Look.RBox("Door", root, new Vector3(face - side * 0.11f, 1.1f, zc - len * 0.18f), new Vector3(0.05f, 2.2f, 1.2f), Pal.Hex("6B4226"), 0.02f, false, 0.6f);
            Sign(root, b.sign, new Vector3(face - side * 0.16f, 3.05f, zc), yaw, Pal.Hex("F4F1EA"), 0.028f);
            r.doors.Add(new CityDoor { pos = root.TransformPoint(new Vector3(face - side * 0.8f, 0, zc - len * 0.18f)), kind = b.kind });
        }

        static void Sign(Transform root, string text, Vector3 pos, float yaw, Color c, float size)
        {
            var t = OfficeBuilder.Label(text, pos, size, c, root, yaw);
            t.fontStyle = FontStyle.Bold;
        }

        static void Wall(Transform root, Vector3 pos, Vector3 size)
        {
            var go = new GameObject("CityBounds"); go.transform.SetParent(root, false); go.transform.localPosition = pos;
            go.AddComponent<BoxCollider>().size = size;
        }

        // Стена с окнами: квад с текстурой «окно на фоне стены», повторённой по этажам и пролётам
        static void Facade(Transform root, Vector3 pos, Vector2 size, float yaw, Color wall, int cols, int rows)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Facade"; UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root, false);
            q.transform.localPosition = pos; q.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var m = FacadeMat(wall);
            var inst = new Material(m) { mainTextureScale = new Vector2(cols, rows) };
            var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = inst; mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material FacadeMat(Color wall)
        {
            string key = ColorUtility.ToHtmlStringRGB(wall);
            Material m;
            if (facades.TryGetValue(key, out m) && m != null) return m;
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Facade_" + key };
            var glass = Pal.Hex("5E7FB8"); var frame = Color.Lerp(wall, Color.white, 0.35f); var sill = Color.Lerp(wall, Pal.Hex("2B2D42"), 0.25f);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color col = wall;
                    bool inFrame = x >= 7 && x < 25 && y >= 8 && y < 26;
                    bool inGlass = x >= 9 && x < 23 && y >= 10 && y < 24;
                    if (inFrame) col = frame;
                    if (inGlass) col = Color.Lerp(glass, Color.white, (y - 10) / 40f + ((x + y) % 11 == 0 ? 0.25f : 0f));
                    if (y >= 6 && y < 8 && x >= 5 && x < 27) col = sill;
                    px[y * n + x] = col;
                }
            tex.SetPixels(px); tex.Apply(true);
            var sh = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
            m = new Material(sh) { name = "Facade_" + key, mainTexture = tex, color = Color.white };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.2f);
            facades[key] = m;
            return m;
        }
    }

    // Дверь бизнес-центра в городе: вернуться в офис раньше времени
    public class CityOfficeDoor : Interactable
    {
        public override string Prompt { get { return "[E] Вернуться в офис"; } }
        public override void Interact(GameRoot g) { g.EndLunch(false); }
    }

    // Дверь из офиса на обед
    public class ExitDoor : Interactable
    {
        public GameRoot game;
        public override string Prompt
        {
            get
            {
                if (game == null || game.Work == null) return "[E] Выйти на обед";
                string why;
                return game.Work.CanLunch(out why) ? "[E] Выйти на обед" : "[E] Выход";
            }
        }
        public override void Interact(GameRoot g) { g.TryStartLunch(); }
    }

    // ======================= один обед =======================
    public class LunchRun
    {
        public const int Cap = 60, MaxAlive = 10, Reward = 10, Penalty = 20;
        public const float SpawnEvery = 8f;

        public readonly float duration;
        public float timeLeft;
        public int coins, kills, fines, spawned, escaped;
        public bool Paused;
        // серия для HUD: «+30» рядом со счётчиком, пока бьёшь без пауз
        public int series; public float seriesAt = -99f;
        public int penaltyShown; public float penaltyAt = -99f;
        public Action<string> Say;

        readonly CityRefs city;
        readonly Func<Vector3> playerPos;
        readonly List<CityNpc> npcs = new List<CityNpc>();
        readonly System.Random rnd = new System.Random();
        float spawnT;

        public LunchRun(CityRefs city, float seconds, Func<Vector3> playerPos)
        {
            this.city = city; duration = timeLeft = seconds; this.playerPos = playerPos;
            for (int i = 0; i < 6; i++) Spawn(true);
            spawnT = SpawnEvery;
        }

        public int Alive { get { int n = 0; foreach (var c in npcs) if (c != null && c.Alive) n++; return n; } }
        public bool Over { get { return timeLeft <= 0f; } }
        public Vector3 PlayerPos { get { return playerPos(); } }
        public CityRefs City { get { return city; } }

        public void Tick(float dt)
        {
            if (Paused) return;
            timeLeft = Mathf.Max(0f, timeLeft - dt);
            spawnT -= dt;
            if (spawnT <= 0f)
            {
                spawnT = SpawnEvery;
                if (Alive < MaxAlive && spawned < Cap) Spawn(false);
            }
            if (series > 0 && Time.unscaledTime - seriesAt > 2.2f) series = 0;
        }

        // На улице (в начале обеда) или из двери подальше от игрока
        void Spawn(bool onStreet)
        {
            if (spawned >= Cap) return;
            bool accountant = rnd.NextDouble() < 0.25;
            string want = accountant ? "accountant" : "lawyer";
            Vector3 pos; var p = playerPos();
            if (onStreet)
            {
                pos = Vector3.zero;
                for (int tries = 0; tries < 20; tries++)
                {
                    pos = new Vector3(Mathf.Lerp(city.min.x, city.max.x, (float)rnd.NextDouble()), 0, Mathf.Lerp(city.min.z + 8f, city.max.z, (float)rnd.NextDouble()));
                    if ((pos - p).sqrMagnitude > 100f) break;
                }
            }
            else
            {
                var best = new List<CityDoor>();
                foreach (var d in city.doors) if ((d.kind == want || d.kind == "any") && (d.pos - p).sqrMagnitude > 144f) best.Add(d);
                if (best.Count == 0) foreach (var d in city.doors) if ((d.pos - p).sqrMagnitude > 64f) best.Add(d);
                if (best.Count == 0) return;
                pos = best[rnd.Next(best.Count)].pos;
            }
            pos.y = 0f;
            var npc = CityNpc.Create(this, accountant ? CityNpc.Kind.Accountant : CityNpc.Kind.Lawyer, pos, rnd);
            npcs.Add(npc);
            if (!accountant) spawned++;   // в лимит 60 идут только гуманитарии
        }

        public Vector3 RandomStreetPoint()
        {
            return new Vector3(Mathf.Lerp(city.min.x + 0.6f, city.max.x - 0.6f, (float)rnd.NextDouble()), 0, Mathf.Lerp(city.min.z, city.max.z, (float)rnd.NextDouble()));
        }

        // Ближайшая дверь, которая не за спиной у игрока
        public Vector3 FleeDoor(Vector3 from)
        {
            var p = playerPos(); p.y = 0;
            Vector3 best = from; float bestScore = float.MaxValue;
            foreach (var d in city.doors)
            {
                var toDoor = d.pos - from; toDoor.y = 0;
                var toPlayer = p - from; toPlayer.y = 0;
                float score = toDoor.magnitude;
                if (toPlayer.sqrMagnitude > 0.01f && Vector3.Dot(toDoor.normalized, toPlayer.normalized) > 0.2f) score += 60f;   // бежать мимо игрока — плохая идея
                if (score < bestScore) { bestScore = score; best = d.pos; }
            }
            return best;
        }

        public void OnKill(CityNpc n)
        {
            coins += Reward; kills++;
            series += Reward; seriesAt = Time.unscaledTime;
        }

        public void OnWrongHit(CityNpc n)
        {
            coins -= Penalty; fines += Penalty;
            penaltyShown = Penalty; penaltyAt = Time.unscaledTime;
            if (Say != null) Say("«Я же бухгалтер!» Это технарь: штраф " + Penalty + " монет.");
        }

        public void OnEscaped(CityNpc n) { escaped++; npcs.Remove(n); }

        public void SetPaused(bool p) { Paused = p; }

        // Только для проверки в редакторе: ближайший живой горожанин встаёт перед игроком
        public void DebugPull(Vector3 at, float yaw)
        {
            CityNpc best = null; float bd = float.MaxValue;
            foreach (var n in npcs) if (n != null && n.Alive) { float d = (n.transform.position - at).sqrMagnitude; if (d < bd) { bd = d; best = n; } }
            if (best != null) best.DebugPlace(at, yaw);
        }

        public void Cleanup()
        {
            foreach (var n in npcs) if (n != null) UnityEngine.Object.Destroy(n.gameObject);
            npcs.Clear();
            Gore.Clear();
        }
    }

    // ======================= горожанин =======================
    public class CityNpc : MonoBehaviour
    {
        public enum Kind { Lawyer, Accountant }
        enum St { Walk, Flee, Dead }

        static readonly string[] Models = { "Dev1", "Dev2", "Dev3", "Dev4" };

        public Kind kind;
        public int hp;
        LunchRun run;
        CharacterAnim anim;
        St st;
        Vector3 target;
        float speed, yaw, idleUntil, frozenUntil;
        bool hitOnce;

        public bool Alive { get { return st != St.Dead; } }
        public bool Humanitarian { get { return kind == Kind.Lawyer; } }

        public static CityNpc Create(LunchRun run, Kind kind, Vector3 pos, System.Random rnd)
        {
            string model = Models[rnd.Next(Models.Length)];
            var ap = new Appearance { skin = rnd.Next(6), eyes = rnd.Next(4), mouth = 2, emotion = 0 };
            if (kind == Kind.Lawyer) { ap.topColor = 8; ap.pants = 1; ap.tie = 2; ap.shoes = 8; }
            else { ap.topColor = 7; ap.pants = 9; ap.tie = 4; ap.shoes = 9; }
            float yaw0 = (float)rnd.NextDouble() * 360f;
            CharacterAnim a;
            if (ModelLib.HasCharacter(model)) { a = CharacterAnim.Spawn(model, run.City.root, pos, yaw0, null); a.Tint(ap); a.SetEmotion(0); }
            else a = Look.Bean(model, run.City.root, pos - run.City.root.position, yaw0, ap);
            a.transform.position = pos;
            var n = a.gameObject.AddComponent<CityNpc>();
            n.run = run; n.anim = a; n.kind = kind; n.yaw = yaw0;
            n.hp = kind == Kind.Lawyer ? 60 : 9999;
            var cap = a.gameObject.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.95f, 0); cap.height = 1.9f; cap.radius = 0.32f;
            n.AddProp();
            var tag = OfficeBuilder.Label(kind == Kind.Lawyer ? "Юрист" : "Бухгалтер", new Vector3(0, 2.35f, 0), 0.02f,
                                          kind == Kind.Lawyer ? Pal.Hex("FF4F9A") : Pal.Hex("89D185"), a.transform);
            tag.gameObject.AddComponent<Billboard>();
            n.target = run.RandomStreetPoint();
            n.speed = 1.2f;
            return n;
        }

        // Портфель у юриста, калькулятор у бухгалтера — в правой руке
        void AddProp()
        {
            var hand = anim.elbowR != null ? anim.elbowR : transform;
            if (kind == Kind.Lawyer)
                Look.RBox("Briefcase", hand, new Vector3(0, -0.32f, 0.02f), new Vector3(0.1f, 0.3f, 0.42f), Pal.Hex("6B4226"), 0.03f, false, 0.6f);
            else
                Look.RBox("Calculator", hand, new Vector3(0, -0.3f, 0.08f), new Vector3(0.14f, 0.2f, 0.03f), Pal.Hex("4A4A5E"), 0.02f, false, 0.6f);
        }

        void Update()
        {
            if (st == St.Dead || run == null) return;
            if (run.Paused || Time.time < frozenUntil) { anim.moveSpeed = 0f; return; }
            float dt = Time.deltaTime;
            var p = run.PlayerPos; p.y = 0;
            var me = transform.position; me.y = 0;
            // юрист замечает стажёра с ножом и убегает к ближайшей двери
            if (st == St.Walk && Humanitarian && (p - me).sqrMagnitude < 8f * 8f) Flee();
            if (st == St.Walk && Time.time < idleUntil) { anim.moveSpeed = 0f; return; }

            var to = target - me; to.y = 0;
            float dist = to.magnitude;
            if (dist < 0.4f)
            {
                if (st == St.Flee) { run.OnEscaped(this); Destroy(gameObject); return; }
                target = run.RandomStreetPoint(); idleUntil = Time.time + UnityEngine.Random.Range(0.5f, 2.5f);
                return;
            }
            var dir = to / dist;
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, want, 540f * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            transform.position = me + dir * Mathf.Min(dist, speed * dt);
            anim.moveSpeed = speed;
        }

        void Flee()
        {
            if (st == St.Dead) return;
            st = St.Flee; target = run.FleeDoor(transform.position);
            speed = kind == Kind.Lawyer ? 4.4f : 3.4f;
            anim.React(3, 3f);
            idleUntil = 0f;
        }

        public void DebugPlace(Vector3 at, float yaw)
        {
            at.y = 0f; transform.position = at; transform.rotation = Quaternion.Euler(0, yaw, 0); this.yaw = yaw;
            target = at; idleUntil = Time.time + 3f; frozenUntil = Time.time + 15f; st = St.Walk;
        }

        public void Hit(int damage, Vector3 from)
        {
            if (st == St.Dead) return;
#if UNITY_EDITOR
            Debug.Log("[Стажёр] Удар по " + kind + ": " + hp + " → " + (kind == Kind.Lawyer ? hp - damage : hp));
#endif
            Gore.Hit(transform.position + Vector3.up * 1.2f, (transform.position - from).normalized);
            if (kind == Kind.Accountant)
            {
                if (!hitOnce) { hitOnce = true; run.OnWrongHit(this); }
                anim.React(5, 2f);
                Flee();
                return;
            }
            hp -= damage;
            anim.React(4, 1.5f);
            if (hp <= 0) Die(from);
            else if (st != St.Flee) Flee();
        }

        void Die(Vector3 from)
        {
            st = St.Dead;
            anim.moveSpeed = 0f;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            var tag = GetComponentInChildren<TextMesh>(); if (tag != null) tag.gameObject.SetActive(false);
            run.OnKill(this);
            StartCoroutine(Fall());
        }

        IEnumerator Fall()
        {
            var start = transform.rotation;
            var end = start * Quaternion.Euler(-88f, 0, 0);   // падает на спину
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.4f)
            {
                float k = t * t;
                transform.rotation = Quaternion.Slerp(start, end, k);
                yield return null;
            }
            transform.rotation = end;
            anim.enabled = false;
            Gore.Pool(transform.position + transform.up * -0.4f + Vector3.up * 0.02f);
            yield return new WaitForSeconds(20f);
            Destroy(gameObject);
        }
    }

    // ======================= кровь =======================
    public static class Gore
    {
        public static bool Enabled = true;
        static readonly List<GameObject> pools = new List<GameObject>();
        const int MaxPools = 64;

        public static void Hit(Vector3 pos, Vector3 dir)
        {
            if (!Enabled) return;
            var mat = Look.FxMat(new Color(0.55f, 0.03f, 0.05f, 1f), true, false);
            if (mat == null) return;
            var go = new GameObject("BloodHit"); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.duration = 0.2f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = new Color(0.62f, 0.04f, 0.06f, 1f);
            main.gravityModifier = 1.2f; main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.05f;
            if (dir.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(dir);
            var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = mat; pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            UnityEngine.Object.Destroy(go, 2f);
            // пятно на асфальте под местом удара
            Pool(new Vector3(pos.x, 0.02f, pos.z) + dir * 0.4f, 0.35f);
        }

        // Лужа, которая растекается за пару секунд
        public static void Pool(Vector3 pos, float radius = 0.9f)
        {
            if (!Enabled) return;
            var mat = Look.FxMat(new Color(0.42f, 0.02f, 0.04f, 0.92f), true, false);
            if (mat == null) return;
            var mesh = Look.Quad(new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1));
            var go = Look.FxObject("BloodPool", null, mesh, mat);
            if (go == null) return;
            pos.y = 0.015f + pools.Count * 0.0002f;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
            var grow = go.AddComponent<PoolGrow>(); grow.target = radius * UnityEngine.Random.Range(0.8f, 1.2f);
            pools.Add(go);
            while (pools.Count > MaxPools) { if (pools[0] != null) UnityEngine.Object.Destroy(pools[0]); pools.RemoveAt(0); }
        }

        public static void Clear()
        {
            foreach (var p in pools) if (p != null) UnityEngine.Object.Destroy(p);
            pools.Clear();
        }
    }

    public class PoolGrow : MonoBehaviour
    {
        public float target = 0.9f;
        float t;
        void Update()
        {
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.deltaTime / 2f);
            float k = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.one * Mathf.Max(0.05f, target * k);
        }
    }

    // ======================= нож =======================
    public class Knife
    {
        public const int Damage = 25;
        public const float Cooldown = 0.45f, Reach = 1.1f, Radius = 0.9f;
        GameObject model;
        float nextAt;

        public void Attach(CharacterAnim avatar)
        {
            if (model != null) return;
            var hand = avatar != null && avatar.elbowR != null ? avatar.elbowR : null;
            if (hand == null) return;
            model = new GameObject("Knife");
            model.transform.SetParent(hand, false);
            model.transform.localPosition = new Vector3(0, -0.3f, 0.03f);
            Look.RBox("Handle", model.transform, new Vector3(0, 0, 0.02f), new Vector3(0.035f, 0.035f, 0.12f), Pal.Hex("2B2D42"), 0.01f, false, 0.6f);
            Look.RBox("Blade", model.transform, new Vector3(0, 0.005f, 0.16f), new Vector3(0.012f, 0.045f, 0.17f), Pal.Hex("D8DCE8"), 0.004f, false, 0.6f, 0.15f);
            foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = 2;
        }

        public void Show(bool on) { if (model != null) model.SetActive(on); }

        public bool Ready { get { return Time.time >= nextAt; } }
        public void Used() { nextAt = Time.time + Cooldown; }
    }
}
