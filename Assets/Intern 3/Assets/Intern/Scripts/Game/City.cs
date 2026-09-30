// Город на обед (v0.8, спринт 2): проспект от бизнес-центра, рыночная площадь и 4 переулка.
// Всё строится кодом один раз, далеко от офиса, и дальше только включается и выключается.
// Улицы — прямоугольники (где можно ходить), дома стоят вокруг и сами понимают, какой стороной смотрят на улицу.
// Горожане ходят по графу улиц: узлы по осевым линиям, у каждого отрезка своя ширина для «полосы».
// Спринт 4 версии 0.9: фасады с объёмом (рамы, подоконники, карнизы, балконы, четыре стиля домов), витрины с товаром,
// вывески-коробы, плитка и брусчатка, бордюры, деревья в клумбах, фонари, остановка, настоящий фонтан и дымка вдали.
// Мелкие детали собираются в BoxBatch (CityKit.cs), вся архитектура — в статические пакеты: объектов мало, FPS держится.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public class CityDoor
    {
        public Vector3 pos;       // точка на тротуаре перед дверью (мир)
        public Vector3 outward;   // от стены на улицу
        public string street;     // av, square, law, book, art, uni
        public string kind;       // door — подъезд/контора, archive — архив, shop — магазин, office — бизнес-центр
        public bool open = true;  // открытый подъезд: сюда убегают (часть дверей заперта)
        public int node;          // ближайший узел графа
    }

    public class CityStreet
    {
        public string id, name; public Rect r;   // r — в локальных координатах города (x, z)
        public float lane;                        // насколько горожанин может отойти от оси
    }

    public class CityRefs
    {
        public Transform root, spawn;
        public readonly List<CityDoor> doors = new List<CityDoor>();
        public readonly List<CityStreet> streets = new List<CityStreet>();
        public readonly List<Vector3> nodes = new List<Vector3>();     // мир
        public readonly List<float> nodeLane = new List<float>();
        public readonly List<float> nodeMin = new List<float>();     // полоса не ближе этого к оси (проспект: только по тротуарам)
        public readonly List<Rect> blockers = new List<Rect>();      // мир (x, z): стоящие машины — горожане их обходят
        public readonly List<string> nodeStreet = new List<string>();
        public readonly List<List<int>> links = new List<List<int>>();
        public readonly List<Vector3> alleyEnds = new List<Vector3>();
        public readonly List<KeyValuePair<string, Vector3>> landmarks = new List<KeyValuePair<string, Vector3>>();
        public Vector3 officeDoor, archiveDoor;
        public readonly List<GameObject> shopKeepers = new List<GameObject>();

        // Пул тел горожан по моделям: живёт вместе с городом, между обедами тоже
        readonly Dictionary<string, Stack<CharacterAnim>> pool = new Dictionary<string, Stack<CharacterAnim>>();
        public int Pooled { get { int n = 0; foreach (var s in pool.Values) n += s.Count; return n; } }

        public CharacterAnim TakePooled(string model)
        {
            Stack<CharacterAnim> st;
            if (pool.TryGetValue(model, out st)) while (st.Count > 0) { var a = st.Pop(); if (a != null) return a; }
            foreach (var kv in pool) while (kv.Value.Count > 0) { var a = kv.Value.Pop(); if (a != null) return a; }
            return null;
        }

        // Заранее собрать несколько тел, пока экран затемнён: первые появления без рывков
        public void Prewarm(string[] models, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string m = models[i % models.Length];
                if (!ModelLib.HasCharacter(m)) continue;
                var a = CharacterAnim.Spawn(m, root, root.position + Vector3.down * 20f, 0f, null);
                a.SetMaxSmooth(1);   // горожан на экране десятки — «очень гладкие» только у офиса
                a.gameObject.SetActive(false);
                Stack<CharacterAnim> st;
                if (!pool.TryGetValue(m, out st)) pool[m] = st = new Stack<CharacterAnim>();
                st.Push(a);
            }
        }

        public void Recycle(CityNpc n)
        {
            if (n == null) return;
            var a = n.Anim;
            n.Strip();
            UnityEngine.Object.DestroyImmediate(n);   // сразу: тело может понадобиться в этом же кадре
            if (a == null) return;
            a.gameObject.SetActive(false);
            string key = string.IsNullOrEmpty(a.model) ? "bean" : a.model;
            Stack<CharacterAnim> st;
            if (!pool.TryGetValue(key, out st)) pool[key] = st = new Stack<CharacterAnim>();
            st.Push(a);
        }

        public Vector3 Local(Vector3 world) { return world - root.position; }

        public CityStreet StreetAt(Vector3 world)
        {
            var l = Local(world); var p = new Vector2(l.x, l.z);
            foreach (var s in streets) if (s.r.Contains(p)) return s;
            return null;
        }
        public bool InStreets(Vector3 world) { return StreetAt(world) != null; }

        public int Nearest(Vector3 world)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++) { var d = nodes[i] - world; d.y = 0; float m = d.sqrMagnitude; if (m < bd) { bd = m; best = i; } }
            return best;
        }

        // Кратчайший путь по графу (узлы — точки; последняя точка — сама цель)
        public List<Vector3> Path(Vector3 from, Vector3 to, float lane)
        {
            int a = Nearest(from), b = Nearest(to);
            var prev = new int[nodes.Count]; var dist = new float[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) { prev[i] = -1; dist[i] = float.MaxValue; }
            var open = new List<int> { a }; dist[a] = 0f;
            while (open.Count > 0)
            {
                int bi = 0; for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
                int u = open[bi]; open.RemoveAt(bi);
                if (u == b) break;
                foreach (var v in links[u])
                {
                    float nd = dist[u] + Vector3.Distance(nodes[u], nodes[v]);
                    if (nd < dist[v]) { if (dist[v] == float.MaxValue) open.Add(v); dist[v] = nd; prev[v] = u; }
                }
            }
            var chain = new List<int>();
            for (int u = b; u >= 0; u = prev[u]) { chain.Add(u); if (u == a) break; }
            chain.Reverse();
            var pts = new List<Vector3>();
            for (int i = 0; i < chain.Count; i++)
            {
                // смещение вбок от оси: у каждого горожанина своя «полоса», на узких улицах меньше
                var n = nodes[chain[i]];
                Vector3 dir = i + 1 < chain.Count ? nodes[chain[i + 1]] - n : i > 0 ? n - nodes[chain[i - 1]] : Vector3.forward;
                dir.y = 0; if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; dir.Normalize();
                var side = new Vector3(dir.z, 0, -dir.x);
                int ni = chain[i];
                pts.Add(n + side * ((lane >= 0f ? 1f : -1f) * nodeMin[ni] + lane * nodeLane[ni]));
            }
            // первая точка пути — не назад к узлу, если цель в другую сторону
            if (pts.Count >= 2)
            {
                var d0 = pts[0] - from; var d1 = pts[1] - from; d0.y = d1.y = 0;
                if (Vector3.Dot(d0, d1) < 0f || d1.magnitude < d0.magnitude) pts.RemoveAt(0);
            }
            to.y = 0; pts.Add(to);
            return pts;
        }

        // Шаг горожанина from → to не заходит в машину: у борта скользит вдоль него, в сторону цели
        public Vector3 Slide(Vector3 from, Vector3 to, Vector3 goal)
        {
            const float R = 0.35f;
            for (int i = 0; i < blockers.Count; i++)
            {
                var r = blockers[i];
                float x0 = r.xMin - R, x1 = r.xMax + R, z0 = r.yMin - R, z1 = r.yMax + R;
                if (to.x <= x0 || to.x >= x1 || to.z <= z0 || to.z >= z1) continue;
                float step = new Vector2(to.x - from.x, to.z - from.z).magnitude;
                bool outX = from.x <= x0 || from.x >= x1, outZ = from.z <= z0 || from.z >= z1;
                if (outX && !outZ)
                {
                    to.x = from.x <= x0 ? x0 : x1;                  // упёрся в длинный борт — идёт вдоль него
                    if (Mathf.Abs(to.z - from.z) < step * 0.5f) to.z = from.z + (goal.z >= r.center.y ? 1f : -1f) * step;
                }
                else if (outZ && !outX)
                {
                    to.z = from.z <= z0 ? z0 : z1;
                    if (Mathf.Abs(to.x - from.x) < step * 0.5f) to.x = from.x + (goal.x >= r.center.x ? 1f : -1f) * step;
                }
                else if (!outX && !outZ)
                {
                    // оказался внутри (появился, отброшен): выйти к ближайшему борту
                    float dl = to.x - x0, dr = x1 - to.x, db = to.z - z0, dt = z1 - to.z, m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
                    if (m == dl) to.x = x0; else if (m == dr) to.x = x1; else if (m == db) to.z = z0; else to.z = z1;
                }
                else to.z = from.z;                                  // на углу: сначала вдоль торца, потом вдоль борта
            }
            return to;
        }

        public Vector3 RandomNode(System.Random rnd, string street = null)
        {
            var list = new List<int>();
            for (int i = 0; i < nodes.Count; i++) if (street == null || nodeStreet[i] == street) list.Add(i);
            if (list.Count == 0) return nodes[rnd.Next(nodes.Count)];
            return nodes[list[rnd.Next(list.Count)]];
        }
    }

    public static class CityBuilder
    {
        public static readonly Vector3 Origin = new Vector3(0f, 0f, 300f);
        static readonly Dictionary<string, Material> facades = new Dictionary<string, Material>();
        static CityRefs R;
        static System.Random rnd;
        static BoxBatch bb;

        // стиль дома: 0 — классика (рамы и подоконники), 1 — кирпич, 2 — стекло и бетон, 3 — старый город (сандрики, балконы)
        static readonly string[] Brands = { "1B1F4A", "7A1F3D", "1F5E4A", "22535E", "4A2E6B", "B8541F", "2B2D42", "274B8A" };

        static readonly Color Dark = Pal.Hex("2B2D42"), Glass = Pal.Hex("BDE7FF"), DoorWood = Pal.Hex("6B4226"), SignInk = Pal.Hex("F4F1EA");

        class Bld
        {
            public float x0, x1, z0, z1, h; public string color;
            public readonly Dictionary<char, string> signs = new Dictionary<char, string>();   // N S E W → вывеска
            public char roomFace; public string roomKind, roomSign;                              // заходить можно
            public int style = -1;
            public Bld(float x0, float x1, float z0, float z1, float h, string color) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; this.h = h; this.color = color; }
            public Bld Sign(char f, string s) { signs[f] = s; return this; }
            public Bld Room(char f, string kind, string sign) { roomFace = f; roomKind = kind; roomSign = sign; return this; }
            public Bld Style(int s) { style = s; return this; }
        }

        public static CityRefs Build()
        {
            R = new CityRefs(); rnd = new System.Random(11); bb = new BoxBatch();
            var root = new GameObject("City").transform; root.position = Origin;
            R.root = root;

            // ---------- улицы ----------
            St("av", "Проспект", -7, 7, 0, 60, 5f);
            St("square", "Рыночная площадь", -16, 16, 60, 92, 4f);
            St("law", "Юридический переулок", -56, -16, 72, 80, 2.8f);
            St("book", "Книжный переулок", 16, 56, 72, 80, 2.8f);
            St("art", "Арт-квартал", -14, -7, 92, 132, 2.3f);
            St("uni", "Университетский переулок", 7, 14, 92, 132, 2.3f);

            Ground(root);

            // ---------- дома ----------
            var blds = new List<Bld>
            {
                // бизнес-центр (отсюда выходит стажёр)
                new Bld(-30, 30, -12, 0, 30, "7FA6D6").Style(2),
                // проспект, запад
                new Bld(-30, -7, 0, 15, 18, "E8B4A0").Sign('E', "Аптека"),
                new Bld(-30, -7, 15, 30, 24, "A8C5E0").Sign('E', "Бухгалтерия"),
                new Bld(-30, -7, 30, 45, 15, "C9B8E8").Sign('E', "IT-парк").Style(2),
                new Bld(-30, -7, 45, 60, 21, "F0D38C").Sign('E', "Почта").Room('N', "food", "Шаурма"),
                // проспект, восток
                new Bld(7, 30, 0, 15, 21, "9ED9C4").Sign('W', "Продукты"),
                new Bld(7, 30, 15, 30, 15, "E8A0B8").Sign('W', "Инженерный центр").Style(2),
                new Bld(7, 30, 30, 45, 24, "B8C9A0").Sign('W', "Банк").Style(2),
                new Bld(7, 30, 45, 60, 18, "D9C4A8").Sign('W', "Кафе «Стек»").Sign('N', "Кафе «Стек»"),
                // Юридический переулок
                new Bld(-56, -43, 60, 72, 18, "D8A48F").Sign('N', "Адвокатское бюро"),
                new Bld(-43, -30, 60, 72, 21, "E6C9A8").Room('N', "notary", "Нотариальная контора"),
                new Bld(-30, -16, 60, 72, 15, "C4B0D8").Sign('N', "Юристы 24/7"),
                new Bld(-56, -43, 80, 92, 24, "A8B8D8").Sign('S', "Юридическая консультация"),
                new Bld(-43, -30, 80, 92, 18, "E8C8A0").Sign('S', "Коллегия адвокатов"),
                new Bld(-30, -16, 80, 92, 21, "B0C8B8").Room('E', "weapons", "Оружейная «Железо»"),
                new Bld(-62, -56, 68, 84, 20, "D9C4A8").Sign('E', "Суд"),
                // Книжный переулок
                new Bld(16, 30, 60, 72, 21, "E0B8C8").Sign('N', "Книжный"),
                new Bld(30, 43, 60, 72, 15, "B8D0E0").Sign('N', "Редакция газеты"),
                new Bld(43, 56, 60, 72, 24, "D8D0A0").Sign('N', "Типография"),
                new Bld(16, 30, 80, 92, 18, "A8D8C0").Room('W', "patch", "Мастерская «Патч»"),
                new Bld(30, 43, 80, 92, 24, "E8B898").Room('S', "library", "Библиотека"),
                new Bld(43, 56, 80, 92, 18, "C8B8E8").Sign('S', "Книги б/у"),
                new Bld(56, 62, 68, 84, 20, "B8C9A0").Sign('W', "Издательство"),
                // Арт-квартал (запад) и центральный квартал
                new Bld(-30, -14, 92, 105, 18, "F0C0A0").Sign('E', "Мастерская художника"),
                new Bld(-30, -14, 105, 118, 24, "C0A8E0").Room('E', "gallery", "Галерея"),
                new Bld(-30, -14, 118, 132, 15, "A0D0D0").Sign('E', "Кассы театра"),
                new Bld(-7, 7, 92, 105, 21, "E8D0A8").Sign('W', "Кофейня «Рифма»").Sign('E', "Кафедра философии").Sign('S', "Дом культуры"),
                new Bld(-7, 7, 105, 118, 18, "B8C8E8").Sign('W', "Театр").Sign('E', "Деканат"),
                new Bld(-7, 7, 118, 132, 24, "D8B8B8").Sign('W', "Студия"),
                // Университетский переулок (восток)
                new Bld(14, 30, 92, 105, 24, "D0D8B0").Sign('W', "Гуманитарный факультет"),
                new Bld(14, 30, 105, 118, 18, "C8C0A8").Room('W', "archive", "Архив"),
                new Bld(14, 30, 118, 132, 21, "A8C0D8").Sign('W', "Общежитие"),
                // концы северных переулков
                new Bld(-16, -5, 132, 138, 18, "E0B0C0").Sign('S', "Арт-пространство"),
                new Bld(5, 16, 132, 138, 18, "B0C8E0").Sign('S', "Университет"),
            };
            foreach (var b in blds) Building(b);
            BusinessCenter(root);

            // ---------- ларёк с патронами, фонтан, деревья, фонари, остановка ----------
            Kiosk(root, new Vector3(12.5f, 0, 64.5f));
            CityKit.Fountain(root, bb, new Vector3(0, 0, 76));
            foreach (var p in new[] { new Vector3(-12, 0, 88), new Vector3(12, 0, 88), new Vector3(-12, 0, 64), new Vector3(-5, 0, 3), new Vector3(5, 0, 3) })
            {
                CityKit.Flowerbed(root, bb, p, 1.35f, rnd);
                CityKit.Tree(root, bb, p + Vector3.up * 0.3f, rnd, 0.95f);
            }
            CityKit.Flowerbed(root, bb, new Vector3(-13f, 0, 81f), 1.1f, rnd);
            for (float z = 8f; z < 60f; z += 12f) foreach (int sd in new[] { -1, 1 }) CityKit.Lamp(bb, new Vector3(sd * 6.4f, 0, z), new Vector3(-sd, 0, 0));
            foreach (var p in new[] { new Vector3(-36, 0, 73), new Vector3(-48, 0, 79), new Vector3(36, 0, 79), new Vector3(48, 0, 73), new Vector3(-13.5f, 0, 110), new Vector3(13.5f, 0, 100), new Vector3(-7.5f, 0, 124), new Vector3(7.5f, 0, 115) })
                CityKit.Lamp(bb, p, ArmToStreet(p));
            foreach (var p in new[] { new Vector3(-6.2f, 0, 25), new Vector3(6.2f, 0, 40), new Vector3(-14.8f, 0, 70), new Vector3(14.8f, 0, 84) }) Bench(root, p, p.x < 0 ? 90f : -90f);
            foreach (var p in new[] { new Vector3(6.3f, 0, 12), new Vector3(-6.3f, 0, 50), new Vector3(-20, 0, 72.6f), new Vector3(24, 0, 79.4f), new Vector3(-13.4f, 0, 98), new Vector3(13.4f, 0, 126) }) Bin(root, p);
            CityKit.BusStop(root, bb, new Vector3(6.3f, 0, 26f), Vector3.forward, Vector3.left);
            // машины у тротуаров проспекта (справа — по ходу движения на север, слева — на юг); у остановки и зебры места нет
            var carRnd = new System.Random(5);
            int ci = 0;
            foreach (var z in new[] { 11f, 17.2f, 36.5f, 47f }) Car(root, new Vector3(3.05f, 0, z + (float)carRnd.NextDouble() * 0.6f), 0f, ci++);
            foreach (var z in new[] { 9.5f, 15.8f, 31f, 43.5f }) Car(root, new Vector3(-3.05f, 0, z + (float)carRnd.NextDouble() * 0.6f), 180f, ci++);

            bb.Flush(root, "CityDetails");
            BatchStatic(root);
            root.gameObject.AddComponent<CityAtmosphere>();

            Graph();
            foreach (var d in R.doors) d.node = R.Nearest(d.pos);

            R.spawn = new GameObject("CitySpawn").transform; R.spawn.SetParent(root, false); R.spawn.localPosition = new Vector3(0, 0.1f, 2.2f);
            R.officeDoor = W(0, 0.5f);
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Офис", R.officeDoor));
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Площадь", W(0, 76)));
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Юридический", W(-40, 76)));
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Книжный", W(40, 76)));
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Арт-квартал", W(-10.5f, 115)));
            R.landmarks.Add(new KeyValuePair<string, Vector3>("Университетский", W(10.5f, 115)));
            foreach (var e in new[] { W(-53, 76), W(53, 76), W(-10.5f, 129), W(10.5f, 129) }) R.alleyEnds.Add(e);
            var built = R; R = null;
            return built;
        }

        static Vector3 W(float x, float z) { return Origin + new Vector3(x, 0, z); }

        // кронштейн фонаря — к оси ближайшей улицы
        static Vector3 ArmToStreet(Vector3 p)
        {
            var q = new Vector2(p.x, p.z);
            CityStreet best = null; float bd = float.MaxValue;
            foreach (var st in R.streets)
            {
                float dx = Mathf.Max(st.r.xMin - q.x, 0f, q.x - st.r.xMax), dz = Mathf.Max(st.r.yMin - q.y, 0f, q.y - st.r.yMax);
                float d = dx * dx + dz * dz; if (d < bd) { bd = d; best = st; }
            }
            if (best == null) return Vector3.forward;
            var c = best.r.center;
            if (best.r.width < best.r.height) return new Vector3(Mathf.Sign(c.x - p.x), 0, 0);
            return new Vector3(0, 0, Mathf.Sign(c.y - p.z));
        }

        // Архитектура не двигается: склеиваем её в статические пакеты (кроме людей, эффектов, воды и надписей)
        static void BatchStatic(Transform root)
        {
            var list = new List<GameObject>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TextMesh>() != null || r.GetComponent<WaterSurface>() != null || r.GetComponentInParent<CharacterAnim>() != null) continue;
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                if (r.sharedMaterial != null && r.sharedMaterial.renderQueue >= 3000) continue;   // прозрачное стекло
                list.Add(r.gameObject);
            }
            if (list.Count > 1) StaticBatchingUtility.Combine(list.ToArray(), root.gameObject);
        }

        static void St(string id, string name, float x0, float x1, float z0, float z1, float lane)
        {
            R.streets.Add(new CityStreet { id = id, name = name, r = Rect.MinMaxRect(x0, z0, x1, z1), lane = lane });
        }

        static bool Walkable(float x, float z)
        {
            var p = new Vector2(x, z);
            foreach (var s in R.streets) if (s.r.Contains(p)) return true;
            return false;
        }
        static string StreetOf(float x, float z)
        {
            var p = new Vector2(x, z);
            foreach (var s in R.streets) if (s.r.Contains(p)) return s.id;
            return "";
        }

        // ---------- земля ----------
        static void Ground(Transform root)
        {
            Look.RBox("Ground", root, new Vector3(0, -0.05f, 62), new Vector3(140f, 0.1f, 170f), Pal.Hex("4B4F66"), 0.02f, true, 0.3f);
            // асфальт проспекта, плитка тротуаров с бордюрами, брусчатка площади и переулков — текстуры рисуются кодом
            CityKit.Ground(root, "Road", new Vector3(0, 0.01f, 30f), 8f, 60f, CityKit.Asphalt(), 4f);
            CityKit.Ground(root, "WalkL", new Vector3(-5.5f, 0.012f, 30f), 3f, 60f, CityKit.Sidewalk(), 1.6f);
            CityKit.Ground(root, "WalkR", new Vector3(5.5f, 0.012f, 30f), 3f, 60f, CityKit.Sidewalk(), 1.6f);
            foreach (int sd in new[] { -1, 1 })
            {
                for (float z = 0.6f; z < 56.5f; z += 2f) bb.Box(new Vector3(sd * 4.1f, 0.07f, z + 0.98f), new Vector3(0.24f, 0.14f, 1.94f), Quaternion.identity, Pal.Hex("D2CFC8"), 0f, 0.3f);
            }
            for (float z = 3f; z < 55f; z += 5f) Flat(root, "Mark", 0, z, 0.16f, 2.4f, Pal.Hex("F4F1EA"), 0.014f);
            for (float x = -3.3f; x <= 3.4f; x += 1.1f) Flat(root, "Zebra", x, 56.9f, 0.55f, 2.8f, Pal.Hex("F4F1EA"), 0.014f);
            Flat(root, "StopLine", -2f, 55.9f, 4f, 0.25f, Pal.Hex("F4F1EA"), 0.014f);
            CityKit.Ground(root, "Square", new Vector3(0, 0.012f, 76f), 32f, 32f, CityKit.Paving(), 2f);
            CityKit.Ground(root, "LawStone", new Vector3(-36f, 0.012f, 76f), 40f, 8f, CityKit.Cobble("8E8A9E"), 1.2f);
            CityKit.Ground(root, "BookStone", new Vector3(36f, 0.012f, 76f), 40f, 8f, CityKit.Cobble("8E8A9E"), 1.2f);
            CityKit.Ground(root, "ArtStone", new Vector3(-10.5f, 0.012f, 112f), 7f, 40f, CityKit.Cobble("9A8E9E"), 1.2f);
            CityKit.Ground(root, "UniStone", new Vector3(10.5f, 0.012f, 112f), 7f, 40f, CityKit.Cobble("8E9A9E"), 1.2f);
        }

        // плоская полоса: центр по x, начало по z, ширина, длина
        static void Flat(Transform root, string n, float xc, float z0, float w, float len, Color c, float y = 0.01f)
        {
            bb.Box(new Vector3(xc, y, z0 + len / 2f), new Vector3(w, 0.02f, len), Quaternion.identity, c);
        }

        // ---------- дом ----------
        static void Building(Bld b)
        {
            var root = R.root;
            var c = Pal.Hex(b.color);
            float w = b.x1 - b.x0, d = b.z1 - b.z0, xc = (b.x0 + b.x1) / 2f, zc = (b.z0 + b.z1) / 2f;
            bool room = b.roomKind != null;
            const float gf = 3.8f;   // высота первого этажа
            if (b.style < 0) b.style = new[] { 0, 1, 3, 0, 3, 1 }[Mathf.Abs((int)(b.x0 * 3f + b.z0 * 7f)) % 6];
            if (!room) Look.RBox("House", root, new Vector3(xc, b.h / 2f, zc), new Vector3(w - 0.1f, b.h, d - 0.1f), c, 0.1f, true, 0.6f);
            else
            {
                Look.RBox("House", root, new Vector3(xc, gf + (b.h - gf) / 2f, zc), new Vector3(w - 0.1f, b.h - gf, d - 0.1f), c, 0.1f, true, 0.6f);
                HollowGround(b, c, gf);
            }
            // каждая сторона дома: где она выходит на улицу — фасад с окнами, первый этаж с витринами и дверями
            foreach (var f in new[] { 'N', 'S', 'E', 'W' }) Face(b, f, c, gf);
            Roof(b, c);
        }

        // Крыша: парапет по периметру и техника (вентиляция, будка лифта) — видно издалека
        static void Roof(Bld b, Color c)
        {
            var edge = b.style == 2 ? Pal.Hex("5B6275") : Color.Lerp(c, Color.white, 0.35f);
            float w = b.x1 - b.x0, d = b.z1 - b.z0, xc = (b.x0 + b.x1) / 2f, zc = (b.z0 + b.z1) / 2f, y = b.h + 0.25f;
            var id = Quaternion.identity;
            bb.Box(new Vector3(xc, y, b.z1 - 0.2f), new Vector3(w - 0.1f, 0.5f, 0.3f), id, edge, 0f, 0.5f);
            bb.Box(new Vector3(xc, y, b.z0 + 0.2f), new Vector3(w - 0.1f, 0.5f, 0.3f), id, edge, 0f, 0.5f);
            bb.Box(new Vector3(b.x1 - 0.2f, y, zc), new Vector3(0.3f, 0.5f, d - 0.1f), id, edge, 0f, 0.5f);
            bb.Box(new Vector3(b.x0 + 0.2f, y, zc), new Vector3(0.3f, 0.5f, d - 0.1f), id, edge, 0f, 0.5f);
            int units = 1 + rnd.Next(3);
            for (int i = 0; i < units; i++)
            {
                var p = new Vector3(Mathf.Lerp(b.x0 + 2f, b.x1 - 2f, (float)rnd.NextDouble()), b.h, Mathf.Lerp(b.z0 + 2f, b.z1 - 2f, (float)rnd.NextDouble()));
                var sz = new Vector3(1.2f + (float)rnd.NextDouble() * 1.5f, 0.8f + (float)rnd.NextDouble() * 1.2f, 1.2f + (float)rnd.NextDouble() * 1.2f);
                bb.Box(p + Vector3.up * sz.y / 2f, sz, id, Pal.Hex(i % 2 == 0 ? "A9ADBE" : "8E93A6"), 0f, 0.5f);
            }
        }

        // Точки стороны дома: начало, конец, нормаль наружу
        static void FaceLine(Bld b, char f, out Vector2 a, out Vector2 e, out Vector2 n)
        {
            switch (f)
            {
                case 'N': a = new Vector2(b.x0, b.z1); e = new Vector2(b.x1, b.z1); n = new Vector2(0, 1); break;
                case 'S': a = new Vector2(b.x1, b.z0); e = new Vector2(b.x0, b.z0); n = new Vector2(0, -1); break;
                case 'E': a = new Vector2(b.x1, b.z1); e = new Vector2(b.x1, b.z0); n = new Vector2(1, 0); break;
                default: a = new Vector2(b.x0, b.z0); e = new Vector2(b.x0, b.z1); n = new Vector2(-1, 0); break;
            }
        }

        static void Face(Bld b, char f, Color c, float gf)
        {
            Vector2 a, e, n; FaceLine(b, f, out a, out e, out n);
            float len = Vector2.Distance(a, e); var dir = (e - a) / len;
            // куски стороны, которые смотрят на улицу
            var segs = new List<Vector2>(); float s0 = -1f;
            for (float t = 0.25f; t <= len; t += 0.5f)
            {
                var p = a + dir * t + n * 0.8f;
                bool open = Walkable(p.x, p.y);
                if (open && s0 < 0f) s0 = t - 0.25f;
                if ((!open || t + 0.5f > len) && s0 >= 0f) { float s1 = open ? len : t - 0.25f; if (s1 - s0 > 1.8f) segs.Add(new Vector2(s0, s1)); s0 = -1f; }
            }
            if (segs.Count == 0) return;
            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;   // куда смотрит фасад
            var band = Color.Lerp(c, Dark, b.style == 2 ? 0.7f : 0.45f);
            string sign; b.signs.TryGetValue(f, out sign);
            bool roomHere = b.roomKind != null && b.roomFace == f;
            var brand = Pal.Hex(Brands[Mathf.Abs((sign ?? b.roomSign ?? b.color).GetHashCode()) % Brands.Length]);
            foreach (var sg in segs)
            {
                float sl = sg.y - sg.x, mid = (sg.x + sg.y) / 2f;
                var m2 = a + dir * mid;
                var mid3 = new Vector3(m2.x, 0, m2.y); var n3 = new Vector3(n.x, 0, n.y); var d3 = new Vector3(dir.x, 0, dir.y);
                // верхние этажи: окна по стилю дома, карниз и межэтажный пояс
                Upper(b, mid3, d3, n3, sl, gf, c, yaw);
                // первый этаж: полоса с цоколем
                if (!roomHere)
                {
                    Look.RBox("Band", R.root, mid3 + n3 * 0.05f + Vector3.up * (gf / 2f), Rot(new Vector3(sl - 0.3f, gf, 0.1f), d3), band, 0.02f, false, 0.5f);
                    bb.Box(mid3 + n3 * 0.13f + Vector3.up * 0.25f, new Vector3(sl - 0.3f, 0.5f, 0.1f), Quaternion.LookRotation(n3), Color.Lerp(band, Dark, 0.5f), 0f, 0.4f);
                }
                // витрины и двери вдоль полосы
                string street = StreetOf((mid3 + n3 * 1f).x, (mid3 + n3 * 1f).z);
                if (roomHere) { RoomFront(b, a, dir, n, sg, gf, band, street); continue; }
                int doors = Mathf.Max(1, Mathf.FloorToInt(sl / 9f));
                for (int i = 0; i < doors; i++)
                {
                    float t = sg.x + sl * (i + 0.5f) / doors;
                    var p2 = a + dir * t; var p3 = new Vector3(p2.x, 0, p2.y);
                    Door(p3, d3, n3, brand);
                    // витрины по бокам от двери
                    foreach (int sd in new[] { -1, 1 })
                    {
                        float wt = t + sd * 2.4f;
                        if (wt < sg.x + 1f || wt > sg.y - 1f) continue;
                        var w2 = a + dir * wt;
                        Vitrine(new Vector3(w2.x, 0, w2.y), d3, n3, 2.3f, 1.8f);
                    }
                    var door = new CityDoor { pos = R.root.TransformPoint(p3 + n3 * 0.9f), outward = n3, street = street, kind = "door", open = rnd.NextDouble() < 0.45 };
                    R.doors.Add(door);
                    if (i == doors / 2 && sign != null)
                        CityKit.SignBox(bb, R.root, sign, p3 + n3 * 0.1f + Vector3.up * 3.25f, d3, n3, brand, SignInk, sign.Length > 18 ? 0.03f : 0.036f, Mathf.Min(sl - 1f, 9f));
                }
            }
        }

        // Верхние этажи. Окно — рама, стекло с переплётом, подоконник; у старого города ещё сандрик и балконы,
        // у «стекла и бетона» — сплошные ленты остекления с импостами
        static void Upper(Bld b, Vector3 mid, Vector3 d3, Vector3 n3, float sl, float gf, Color wall, float yaw)
        {
            var rot = Quaternion.LookRotation(n3);
            float top = b.h - 0.5f, upper = top - gf - 0.3f;
            int rows = Mathf.Max(1, Mathf.FloorToInt(upper / 3.1f));
            float fh = upper / rows;
            var light = Color.Lerp(wall, Color.white, 0.6f);
            // карниз под крышей и пояс над первым этажом
            bb.Box(mid + n3 * 0.22f + Vector3.up * (b.h - 0.35f), new Vector3(sl + 0.2f, 0.4f, 0.44f), rot, b.style == 2 ? Pal.Hex("5B6275") : light, 0f, 0.6f);
            bb.Box(mid + n3 * 0.12f + Vector3.up * (gf + 0.1f), new Vector3(sl, 0.2f, 0.24f), rot, Color.Lerp(wall, Dark, 0.25f), 0f, 0.5f);
            if (b.style == 1) Brick(mid + n3 * 0.015f + Vector3.up * (gf + 0.2f + (top - gf - 0.2f) / 2f), new Vector2(sl - 0.1f, top - gf - 0.2f), yaw, wall);
            if (b.style == 2)
            {
                var glass = Pal.Hex("2E5A73"); var mull = Pal.Hex("39404F");
                for (int r = 0; r < rows; r++)
                {
                    float y = gf + 0.3f + fh * r;
                    bb.Box(mid + n3 * 0.04f + Vector3.up * (y + fh * 0.55f), new Vector3(sl - 0.5f, fh * 0.66f, 0.08f), rot, glass, 0.08f, 0f);
                    bb.Box(mid + n3 * 0.1f + Vector3.up * (y + fh * 0.18f), new Vector3(sl - 0.3f, 0.16f, 0.12f), rot, light, 0f, 0.4f);
                    int m = Mathf.Max(2, Mathf.RoundToInt((sl - 0.5f) / 1.5f));
                    for (int k = 0; k <= m; k++)
                    {
                        float u = -(sl - 0.5f) / 2f + (sl - 0.5f) * k / m;
                        bb.Box(mid + d3 * u + n3 * 0.1f + Vector3.up * (y + fh * 0.55f), new Vector3(0.07f, fh * 0.66f, 0.06f), rot, mull);
                    }
                }
                return;
            }
            int cols = Mathf.Max(1, Mathf.RoundToInt(sl / 2.8f));
            float cw = (sl - 0.6f) / cols;
            float ww = Mathf.Min(1.35f, cw * 0.55f), wh = Mathf.Min(1.8f, fh * 0.58f);
            var frame = b.style == 1 ? Pal.Hex("F4F1EA") : Color.Lerp(wall, Color.white, 0.8f);
            var glassC = Pal.Hex(b.style == 3 ? "2F4466" : "34507A");
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < cols; k++)
                {
                    float u = -(sl - 0.6f) / 2f + cw * (k + 0.5f);
                    float y = gf + 0.3f + fh * r + fh * 0.52f;
                    var c = mid + d3 * u + Vector3.up * y;
                    bb.Box(c + n3 * 0.05f, new Vector3(ww + 0.18f, wh + 0.18f, 0.1f), rot, frame, 0f, 0.35f);
                    var g = (k * 7 + r * 3) % 9 == 0 ? Color.Lerp(glassC, Pal.Hex("FFE7A8"), 0.35f) : glassC;   // где-то внутри горит свет
                    bb.Box(c + n3 * 0.07f, new Vector3(ww, wh, 0.08f), rot, g, 0.06f);
                    bb.Box(c + n3 * 0.12f, new Vector3(0.05f, wh, 0.04f), rot, frame);                               // переплёт
                    bb.Box(c + n3 * 0.12f + Vector3.up * (wh * 0.22f), new Vector3(ww, 0.05f, 0.04f), rot, frame);
                    bb.Box(c + n3 * 0.14f - Vector3.up * (wh / 2f + 0.12f), new Vector3(ww + 0.36f, 0.08f, 0.28f), rot, light, 0f, 0.35f);   // подоконник
                    if (b.style == 3)
                    {
                        bb.Box(c + n3 * 0.12f + Vector3.up * (wh / 2f + 0.2f), new Vector3(ww + 0.4f, 0.14f, 0.18f), rot, light, 0f, 0.35f);   // сандрик
                        bb.Box(c + n3 * 0.16f + Vector3.up * (wh / 2f + 0.2f), new Vector3(0.18f, 0.22f, 0.14f), rot, Color.Lerp(light, Dark, 0.15f));
                        // балкон на среднем окне через этаж
                        if (r % 2 == 1 && k == cols / 2) Balcony(c - Vector3.up * (wh / 2f + 0.2f), d3, n3, ww + 0.9f, Pal.Hex("2B2D42"), light);
                    }
                }
            // пилястры по краям куска фасада — у классики и старого города
            if (b.style != 1)
                foreach (int sd in new[] { -1, 1 })
                    bb.Box(mid + d3 * (sd * (sl / 2f - 0.25f)) + n3 * 0.08f + Vector3.up * (gf + (top - gf) / 2f), new Vector3(0.4f, top - gf, 0.16f), rot, light, 0f, 0.4f);
        }

        static void Balcony(Vector3 floorC, Vector3 d3, Vector3 n3, float w, Color rail, Color slab)
        {
            var rot = Quaternion.LookRotation(n3);
            bb.Box(floorC + n3 * 0.45f, new Vector3(w, 0.14f, 0.9f), rot, slab, 0f, 0.5f);
            bb.Box(floorC + n3 * 0.88f + Vector3.up * 0.95f, new Vector3(w, 0.06f, 0.06f), rot, rail);
            bb.Box(floorC + d3 * (w / 2f - 0.03f) + n3 * 0.45f + Vector3.up * 0.95f, new Vector3(0.06f, 0.06f, 0.9f), rot, rail);
            bb.Box(floorC - d3 * (w / 2f - 0.03f) + n3 * 0.45f + Vector3.up * 0.95f, new Vector3(0.06f, 0.06f, 0.9f), rot, rail);
            int bars = Mathf.RoundToInt(w / 0.16f);
            for (int i = 0; i <= bars; i++)
                bb.Box(floorC + d3 * (-w / 2f + w * i / bars) + n3 * 0.88f + Vector3.up * 0.5f, new Vector3(0.03f, 0.9f, 0.03f), rot, rail);
        }

        // Дверь: рама, полотно со стеклом, ручка, ступень и козырёк цвета вывески
        static void Door(Vector3 p3, Vector3 d3, Vector3 n3, Color brand)
        {
            var rot = Quaternion.LookRotation(n3);
            bb.Box(p3 + n3 * 0.12f + Vector3.up * 1.25f, new Vector3(1.6f, 2.5f, 0.12f), rot, Pal.Hex("2B2D42"), 0f, 0.5f);
            bb.Box(p3 + n3 * 0.17f + Vector3.up * 1.15f, new Vector3(1.3f, 2.25f, 0.06f), rot, DoorWood, 0f, 0.4f);
            bb.Box(p3 + n3 * 0.21f + Vector3.up * 1.55f, new Vector3(0.9f, 0.9f, 0.03f), rot, Pal.Hex("9FC6E8"), 0.15f);
            bb.Box(p3 + d3 * 0.48f + n3 * 0.23f + Vector3.up * 1.1f, new Vector3(0.06f, 0.3f, 0.06f), rot, Pal.Hex("D9A441"), 0.3f);
            bb.Box(p3 + n3 * 0.35f + Vector3.up * 0.06f, new Vector3(1.9f, 0.12f, 0.6f), rot, Pal.Hex("B8B0A0"), 0f, 0.4f);
            var aw = Quaternion.LookRotation(n3) * Quaternion.Euler(22f, 0, 0);
            bb.Box(p3 + n3 * 0.55f + Vector3.up * 2.72f, new Vector3(2.0f, 0.07f, 1.0f), aw, brand, 0f, 0.5f);
            bb.Box(p3 + n3 * 1.02f + Vector3.up * 2.5f, new Vector3(2.0f, 0.2f, 0.05f), rot, Color.Lerp(brand, Color.white, 0.25f), 0f, 0.4f);
        }

        // Витрина: тёмная рама, освещённое нутро с полками и товаром, стекло поверх
        static void Vitrine(Vector3 c, Vector3 d3, Vector3 n3, float w, float h)
        {
            var rot = Quaternion.LookRotation(n3);
            var y = Vector3.up * (0.55f + h / 2f);
            bb.Box(c + y + n3 * 0.13f, new Vector3(w + 0.2f, h + 0.2f, 0.1f), rot, Pal.Hex("2B2D42"), 0f, 0.5f);
            bb.Box(c + y + n3 * 0.15f, new Vector3(w, h, 0.06f), rot, Pal.Hex("F2E3C4"), 0.35f);
            bb.Box(c + y + n3 * 0.19f - Vector3.up * 0.25f, new Vector3(w - 0.1f, 0.05f, 0.08f), rot, Pal.Hex("A0785A"));
            string[] goods = { "E0567F", "3A7BD5", "FFD23F", "7FB58A", "C8453A", "B28DFF", "FF8A5C" };
            int nG = 3 + rnd.Next(3);
            for (int i = 0; i < nG; i++)
            {
                float u = -w / 2f + 0.3f + (w - 0.6f) * (i + 0.5f) / nG;
                float gh = 0.2f + (float)rnd.NextDouble() * 0.35f;
                bb.Box(c + y + d3 * u + n3 * 0.2f + Vector3.up * (-0.22f + gh / 2f), new Vector3(0.22f, gh, 0.08f), rot, Pal.Hex(goods[rnd.Next(goods.Length)]), 0.1f);
                if (i % 2 == 0) bb.Box(c + y + d3 * u + n3 * 0.2f + Vector3.up * 0.42f, new Vector3(0.3f, 0.22f, 0.06f), rot, Pal.Hex(goods[rnd.Next(goods.Length)]), 0.1f);
            }
            bb.Box(c + y + n3 * 0.19f + Vector3.up * 0.3f, new Vector3(w - 0.1f, 0.04f, 0.08f), rot, Pal.Hex("A0785A"));
            bb.Glass(c + y + n3 * 0.235f, w, h, rot);
        }

        // Кирпичная кладка поверх стены (стиль «кирпич»)
        static void Brick(Vector3 pos, Vector2 size, float yaw, Color wall)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Brick"; UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(R.root, false);
            q.transform.localPosition = pos; q.transform.localRotation = Quaternion.Euler(0, yaw + 180f, 0);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var mf = q.GetComponent<MeshFilter>();
            var m = UnityEngine.Object.Instantiate(mf.sharedMesh);
            var uv = m.uv; for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(uv[i].x * size.x, uv[i].y * size.y);
            m.uv = uv; mf.sharedMesh = m;
            var mr = q.GetComponent<MeshRenderer>(); mr.sharedMaterial = BrickMat(wall); mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        static Material BrickMat(Color wall)
        {
            string key = "brick" + ColorUtility.ToHtmlStringRGB(wall);
            Material m;
            if (facades.TryGetValue(key, out m) && m != null) return m;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = key };
            var baseC = Color.Lerp(wall, Pal.Hex("B5553C"), 0.55f); var mortar = Color.Lerp(wall, Color.white, 0.55f);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int row = y / 8, off = (row % 2) * 16, bx = (x + off) / 32;
                    bool seam = y % 8 == 0 || (x + off) % 32 == 0;
                    unchecked { int h = bx * 73856093 ^ row * 19349663; float k = ((h >> 8) & 255) / 255f; px[y * n + x] = seam ? mortar : baseC * (0.88f + k * 0.2f); }
                    px[y * n + x].a = 1f;
                }
            tex.SetPixels(px); tex.Apply(true);
            var sh = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
            m = new Material(sh) { name = key, mainTexture = tex, color = Color.white };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            facades[key] = m;
            return m;
        }

        static Vector3 Rot(Vector3 size, Vector3 along) { return Mathf.Abs(along.x) > 0.5f ? size : new Vector3(size.z, size.y, size.x); }

        // ---------- первый этаж, куда можно зайти ----------
        // Коробка первого этажа: стены по периметру, в стене на улицу — проём. Внутри комната ~6 × 8 м.
        static void HollowGround(Bld b, Color c, float gf)
        {
            var root = R.root; var wall = Color.Lerp(c, Dark, 0.55f);
            const float t = 0.3f;
            // стены по сторонам, кроме той, где вход (её строит RoomFront с проёмом)
            if (b.roomFace != 'N') Look.RBox("GWallN", root, new Vector3((b.x0 + b.x1) / 2f, gf / 2f, b.z1 - t / 2f), new Vector3(b.x1 - b.x0, gf, t), wall, 0.02f, true, 0.5f);
            if (b.roomFace != 'S') Look.RBox("GWallS", root, new Vector3((b.x0 + b.x1) / 2f, gf / 2f, b.z0 + t / 2f), new Vector3(b.x1 - b.x0, gf, t), wall, 0.02f, true, 0.5f);
            if (b.roomFace != 'E') Look.RBox("GWallE", root, new Vector3(b.x1 - t / 2f, gf / 2f, (b.z0 + b.z1) / 2f), new Vector3(t, gf, b.z1 - b.z0), wall, 0.02f, true, 0.5f);
            if (b.roomFace != 'W') Look.RBox("GWallW", root, new Vector3(b.x0 + t / 2f, gf / 2f, (b.z0 + b.z1) / 2f), new Vector3(t, gf, b.z1 - b.z0), wall, 0.02f, true, 0.5f);
        }

        static void RoomFront(Bld b, Vector2 a, Vector2 dir, Vector2 n, Vector2 seg, float gf, Color band, string street)
        {
            var root = R.root;
            var n3 = new Vector3(n.x, 0, n.y); var d3 = new Vector3(dir.x, 0, dir.y);
            float len = b.roomFace == 'N' || b.roomFace == 'S' ? b.x1 - b.x0 : b.z1 - b.z0;
            // проём — посередине той части стены, что выходит на улицу
            float mid = (seg.x + seg.y) / 2f; const float door = 1.8f, t = 0.3f;
            var face0 = new Vector3(a.x, 0, a.y) - n3 * (t / 2f);
            // стена с проёмом: слева, справа и перемычка
            float leftLen = mid - door / 2f, rightLen = len - mid - door / 2f;
            Look.RBox("FrontL", root, face0 + d3 * (leftLen / 2f) + Vector3.up * gf / 2f, Rot(new Vector3(leftLen, gf, t), d3), band, 0.02f, true, 0.5f);
            Look.RBox("FrontR", root, face0 + d3 * (len - rightLen / 2f) + Vector3.up * gf / 2f, Rot(new Vector3(rightLen, gf, t), d3), band, 0.02f, true, 0.5f);
            Look.RBox("Lintel", root, face0 + d3 * mid + Vector3.up * (2.6f + (gf - 2.6f) / 2f), Rot(new Vector3(door, gf - 2.6f, t), d3), band, 0.02f, true, 0.5f);
            // витрины и вывеска
            foreach (int sd in new[] { -1, 1 })
            {
                float wt = mid + sd * 3.2f; if (wt < seg.x + 1.4f || wt > seg.y - 1.4f) continue;
                Vitrine(face0 + d3 * wt + n3 * 0.06f, d3, n3, 2.6f, 1.9f);
            }
            var entry = face0 + d3 * mid;
            var brand = Pal.Hex(b.roomKind == "weapons" ? "7A1F2A" : b.roomKind == "patch" ? "1F4E8A" : b.roomKind == "food" ? "B8541F" : "3B2E6B");
            CityKit.SignBox(bb, root, b.roomSign, entry + n3 * 0.16f + Vector3.up * 3.25f, d3, n3, brand, Pal.Sun, b.roomSign.Length > 16 ? 0.032f : 0.038f, 8f);
            Look.RBox("Awning", root, entry + n3 * 0.7f + Vector3.up * 2.85f, Rot(new Vector3(door + 1.2f, 0.12f, 1.2f), d3), Pal.Hex(b.roomKind == "weapons" ? "C8453A" : b.roomKind == "patch" ? "3A7BD5" : b.roomKind == "food" ? "E8782F" : "7A6FC4"), 0.03f, false, 0.6f);
            Interior(b, entry, -n3, d3, gf);
            string kind = b.roomKind == "archive" ? "archive" : (b.roomKind == "weapons" || b.roomKind == "patch" || b.roomKind == "food") ? "shop" : "door";
            var cd = new CityDoor { pos = root.TransformPoint(entry + n3 * 1.1f), outward = n3, street = street, kind = kind };
            R.doors.Add(cd);
            if (kind == "archive") R.archiveDoor = cd.pos;
        }

        // Комната: пол, стены, потолок, свет и обстановка по типу
        static void Interior(Bld b, Vector3 entry, Vector3 inward, Vector3 along, float gf)
        {
            var root = R.root;
            const float rw = 6.4f, rd = 8f;
            var center = entry + inward * (rd / 2f + 0.2f);
            Color wall = Pal.Hex(b.roomKind == "archive" ? "D8CCB0" : b.roomKind == "gallery" ? "F4F1EA" : b.roomKind == "weapons" ? "8A8FA8" : "E8DCC8");
            Look.RBox("RoomFloor", root, center + Vector3.up * 0.02f, Rot(new Vector3(rw, 0.04f, rd), along), Pal.Hex("A0785A"), 0.01f, false, 0f, 0f, false);
            Look.RBox("RoomBack", root, center + inward * (rd / 2f) + Vector3.up * gf / 2f, Rot(new Vector3(rw, gf, 0.2f), along), wall, 0.02f, true, 0.4f);
            Look.RBox("RoomSideA", root, center + along * (rw / 2f) + Vector3.up * gf / 2f, Rot(new Vector3(0.2f, gf, rd), along), wall, 0.02f, true, 0.4f);
            Look.RBox("RoomSideB", root, center - along * (rw / 2f) + Vector3.up * gf / 2f, Rot(new Vector3(0.2f, gf, rd), along), wall, 0.02f, true, 0.4f);
            Look.RBox("RoomCeil", root, center + Vector3.up * (gf - 0.05f), Rot(new Vector3(rw, 0.1f, rd), along), Pal.Hex("F4F1EA"), 0.01f, false, 0f, 0.3f, false);
            Look.RBox("RoomLamp", root, center + Vector3.up * (gf - 0.14f), Rot(new Vector3(1.2f, 0.06f, 1.2f), along), Pal.Hex("FFF3C4"), 0.02f, false, 0f, 2.2f, false);
            var lt = new GameObject("RoomLight").AddComponent<Light>();
            lt.transform.SetParent(root, false); lt.transform.localPosition = center + Vector3.up * (gf - 0.6f);
            lt.type = LightType.Point; lt.range = 8f; lt.intensity = 1.6f; lt.color = Pal.Hex("FFE8C0"); lt.shadows = LightShadows.None;

            float yawIn = Mathf.Atan2(-inward.x, -inward.z) * Mathf.Rad2Deg;   // лицом к входу
            var counterPos = center + inward * 1.6f;
            switch (b.roomKind)
            {
                case "weapons":
                case "patch":
                case "food":
                    var counter = Look.RBox("Counter", root, counterPos + Vector3.up * 0.55f, Rot(new Vector3(4.2f, 1.1f, 0.8f), along), Pal.Hex(b.roomKind == "food" ? "E8782F" : "6B4226"), 0.04f, true, 0.6f);
                    Look.RBox("CounterTop", root, counterPos + Vector3.up * 1.12f, Rot(new Vector3(4.4f, 0.06f, 0.9f), along), Pal.Hex("F4F1EA"), 0.02f, false, 0.6f);
                    var shop = counter.AddComponent<CityShop>(); shop.kind = b.roomKind; shop.title = b.roomSign;
                    shop.front = R.root.TransformPoint(counterPos - inward * 1.3f);
                    // продавец за прилавком
                    var keeperPos = R.root.TransformPoint(counterPos + inward * 1.1f);
                    string model = b.roomKind == "weapons" ? "Dev4" : b.roomKind == "patch" ? "Dev2" : "Dev1";
                    if (ModelLib.HasCharacter(model))
                    {
                        var k = CharacterAnim.Spawn(model, root, keeperPos, yawIn, null);
                        k.transform.position = keeperPos; k.lookAtPlayer = true;
                        k.Tint(new Appearance { skin = rnd.Next(6), topColor = b.roomKind == "weapons" ? 11 : b.roomKind == "patch" ? 0 : 7, pants = 1, shoes = 8, tie = -1 });
                        R.shopKeepers.Add(k.gameObject);
                    }
                    // товар на задней стене
                    for (int i = 0; i < 5; i++)
                    {
                        var p = center + inward * (rd / 2f - 0.2f) + along * (-2.2f + i * 1.1f) + Vector3.up * (1.6f + (i % 2) * 0.5f);
                        if (b.roomKind == "weapons") Look.RBox("RackGun", root, p, Rot(new Vector3(0.9f, 0.12f, 0.06f), along), Dark, 0.02f, false, 0.6f);
                        else if (b.roomKind == "patch") Look.RBox("Part", root, p, new Vector3(0.3f, 0.2f, 0.3f), Pal.Hex(i % 2 == 0 ? "3A7BD5" : "8A8FA8"), 0.03f, false, 0.6f);
                        else Look.Prim("Doner", root, PrimitiveType.Cylinder, p + Vector3.up * -0.3f, new Vector3(0.35f, 0.5f, 0.35f), Pal.Hex("C98E68"), false, 0.6f);
                    }
                    break;
                case "library":
                case "archive":
                    for (int i = 0; i < 3; i++)
                    {
                        var p = center + along * (-2f + i * 2f) + inward * 1.5f;
                        Look.RBox("Shelf", root, p + Vector3.up * 1.1f, Rot(new Vector3(1.4f, 2.2f, 0.5f), along), Pal.Hex("8C6A4F"), 0.03f, true, 0.6f);
                        for (int k = 0; k < 4; k++) Look.RBox("Books", root, p + Vector3.up * (0.4f + k * 0.5f) - inward * 0.05f, Rot(new Vector3(1.2f, 0.3f, 0.4f), along), Pal.Hex(k % 2 == 0 ? "C8453A" : "3A7BD5"), 0.02f, false, 0.4f);
                    }
                    Look.RBox("Table", root, center - inward * 1.2f + Vector3.up * 0.4f, Rot(new Vector3(2f, 0.8f, 1f), along), Pal.Hex("A0785A"), 0.03f, true, 0.6f);
                    break;
                case "gallery":
                    for (int i = 0; i < 3; i++)
                    {
                        var p = center + inward * (rd / 2f - 0.15f) + along * (-2f + i * 2f) + Vector3.up * 1.7f;
                        Look.RBox("Frame", root, p, Rot(new Vector3(1.2f, 0.9f, 0.06f), along), Pal.Hex("D9A441"), 0.02f, false, 0.6f);
                        Look.RBox("Canvas", root, p - inward * 0.04f, Rot(new Vector3(1f, 0.7f, 0.02f), along), Pal.Hex(i == 0 ? "E0567F" : i == 1 ? "7FB58A" : "7A6FC4"), 0.01f, false, 0.2f, 0.3f);
                    }
                    Look.RBox("Bench", root, center + Vector3.up * 0.25f, Rot(new Vector3(2f, 0.5f, 0.6f), along), Pal.Hex("2E2638"), 0.05f, true, 0.6f);
                    break;
                default: // нотариус
                    Look.RBox("Desk", root, center + inward * 1f + Vector3.up * 0.4f, Rot(new Vector3(2.2f, 0.8f, 1f), along), Pal.Hex("6B4226"), 0.03f, true, 0.6f);
                    Look.RBox("Stamp", root, center + inward * 1f + Vector3.up * 0.86f, new Vector3(0.12f, 0.12f, 0.12f), Pal.Hex("C8453A"), 0.02f, false, 0.6f);
                    Look.RBox("Cabinet", root, center + inward * 3.4f + along * 2f + Vector3.up * 1f, Rot(new Vector3(1f, 2f, 0.6f), along), Pal.Hex("8A8FA8"), 0.03f, true, 0.6f);
                    break;
            }
        }

        // ---------- бизнес-центр и дверь в офис ----------
        static void BusinessCenter(Transform root)
        {
            Look.RBox("BCDoorFrame", root, new Vector3(0, 1.5f, 0.06f), new Vector3(3.2f, 3.0f, 0.12f), Dark, 0.04f, false, 0.6f);
            Look.RBox("BCDoor", root, new Vector3(0, 1.4f, 0.14f), new Vector3(2.6f, 2.7f, 0.06f), Glass, 0.02f, false, 0.4f, 0.2f);
            var door = Look.RBox("OfficeDoor", root, new Vector3(0, 1.4f, 0.5f), new Vector3(3.0f, 2.8f, 0.8f), Glass, 0.02f, true, 0f);
            door.GetComponent<MeshRenderer>().enabled = false;
            door.GetComponent<BoxCollider>().isTrigger = true;
            door.AddComponent<CityOfficeDoor>();
            CityKit.SignBox(bb, root, "CODEZILLA", new Vector3(0, 3.55f, 0.12f), Vector3.right, Vector3.forward, Pal.Hex("1B1F4A"), Pal.Hex("FF4F9A"), 0.05f, 8f);
        }

        // Ларёк с патронами на площади
        static void Kiosk(Transform root, Vector3 p)
        {
            Look.RBox("KioskBody", root, p + Vector3.up * 1.25f, new Vector3(2.4f, 2.5f, 2f), Pal.Hex("3E7B5A"), 0.08f, true, 0.6f);
            Look.RBox("KioskRoof", root, p + Vector3.up * 2.6f, new Vector3(2.8f, 0.2f, 2.4f), Pal.Hex("C8453A"), 0.05f, false, 0.6f);
            Look.RBox("KioskWindow", root, p + new Vector3(-1.21f, 1.5f, 0), new Vector3(0.05f, 0.9f, 1.4f), Glass, 0.02f, false, 0.3f, 0.3f);
            var counter = Look.RBox("KioskCounter", root, p + new Vector3(-1.35f, 1.0f, 0), new Vector3(0.35f, 0.08f, 1.5f), Pal.Hex("F4F1EA"), 0.02f, true, 0.6f);
            var shop = counter.AddComponent<CityShop>(); shop.kind = "ammo"; shop.title = "Ларёк «Патроны»";
            shop.front = R.root.TransformPoint(p + new Vector3(-2.4f, 0, 0));
            CityKit.SignBox(bb, root, "ПАТРОНЫ", p + new Vector3(-1.21f, 2.12f, 0), Vector3.back, Vector3.left, Pal.Hex("7A1F2A"), Pal.Sun, 0.024f, 2.2f);
        }

        // Скамейка: ножки, три доски сиденья, две доски спинки
        static void Bench(Transform root, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0, yaw, 0); var wood = Pal.Hex("A0785A"); var iron = Pal.Hex("2B2D42");
            foreach (int sd in new[] { -1, 1 })
            {
                bb.Box(p + rot * new Vector3(sd * 0.75f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.5f), rot, iron, 0f, 0.4f);
                bb.Box(p + rot * new Vector3(sd * 0.75f, 0.7f, -0.22f), new Vector3(0.08f, 0.55f, 0.08f), rot, iron, 0f, 0.4f);
            }
            for (int i = 0; i < 3; i++) bb.Box(p + rot * new Vector3(0, 0.47f, -0.15f + i * 0.16f), new Vector3(1.8f, 0.05f, 0.13f), rot, wood, 0f, 0.4f);
            for (int i = 0; i < 2; i++) bb.Box(p + rot * new Vector3(0, 0.72f + i * 0.2f, -0.25f), new Vector3(1.8f, 0.12f, 0.04f), rot, wood, 0f, 0.4f);
            var col = new GameObject("BenchCollider"); col.transform.SetParent(root, false); col.transform.localPosition = p + Vector3.up * 0.3f; col.transform.localRotation = rot;
            col.AddComponent<BoxCollider>().size = new Vector3(1.8f, 0.6f, 0.5f);
        }

        // Стоящая машина: кузов, кабина со стёклами, колёса, фары; коробка-столкновение для стажёра и пуль,
        // прямоугольник в blockers — для горожан
        static readonly string[] CarColors = { "E04848", "3A7BD5", "F2C14E", "F4F1EA", "5FA05A", "7E57C2", "2F3B52", "E8782F" };
        static void Car(Transform root, Vector3 p, float yaw, int i)
        {
            var rot = Quaternion.Euler(0, yaw, 0);
            Vector3 F = rot * Vector3.forward, Rt = rot * Vector3.right;
            var paint = Pal.Hex(CarColors[i % CarColors.Length]);
            var roof = Color.Lerp(paint, Color.white, 0.12f);
            var glass = Pal.Hex("2B3550"); var dark = Pal.Hex("2E3040"); var tyre = Pal.Hex("23232B");
            bool van = i % 5 == 3;   // иногда — фургончик
            float len = van ? 4.5f : 4.1f, cabLen = van ? 3.2f : 2.1f, cabZ = van ? -0.45f : -0.25f, cabH = van ? 0.95f : 0.58f;
            bb.Box(p + Vector3.up * 0.6f, new Vector3(1.76f, 0.62f, len), rot, paint, 0f, 0.6f);
            bb.Box(p + Vector3.up * (0.91f + cabH / 2f) + F * cabZ, new Vector3(1.56f, cabH, cabLen), rot, roof, 0f, 0.6f);
            // стёкла: боковые чуть шире кабины, лобовое и заднее
            bb.Box(p + Vector3.up * (0.93f + cabH * 0.55f) + F * (cabZ + (van ? 0.9f : 0f)), new Vector3(1.58f, cabH * 0.62f, van ? 1.1f : cabLen - 0.35f), rot, glass);
            bb.Box(p + Vector3.up * (0.93f + cabH * 0.55f) + F * (cabZ + cabLen / 2f + 0.005f), new Vector3(1.4f, cabH * 0.62f, 0.02f), rot, glass);
            if (!van) bb.Box(p + Vector3.up * (0.93f + cabH * 0.55f) + F * (cabZ - cabLen / 2f - 0.005f), new Vector3(1.4f, cabH * 0.62f, 0.02f), rot, glass);
            // бамперы, фары, номер
            foreach (int s in new[] { -1, 1 })
            {
                bb.Box(p + Vector3.up * 0.36f + F * (s * (len / 2f + 0.03f)), new Vector3(1.8f, 0.2f, 0.1f), rot, dark);
                foreach (int side in new[] { -1, 1 })
                    bb.Box(p + Vector3.up * 0.7f + F * (s * (len / 2f + 0.01f)) + Rt * (side * 0.6f), new Vector3(0.34f, 0.14f, 0.04f), rot,
                        s > 0 ? Pal.Hex("FFF6D8") : Pal.Hex("D83A3A"), s > 0 ? 0.9f : 0.7f);
                bb.Box(p + Vector3.up * 0.5f + F * (s * (len / 2f + 0.09f)), new Vector3(0.5f, 0.12f, 0.02f), rot, Pal.Hex("F4F1EA"));
            }
            // колёса с дисками
            foreach (int fz in new[] { -1, 1 })
                foreach (int side in new[] { -1, 1 })
                {
                    var c = p + Vector3.up * 0.34f + F * (fz * (len / 2f - 0.8f)) + Rt * (side * 0.8f);
                    bb.Cylinder(c, Rt, 0.34f, 0.24f, tyre, 12);
                    bb.Cylinder(c + Rt * (side * 0.03f), Rt, 0.17f, 0.24f, Pal.Hex("B8BCC8"), 8);
                }
            // зеркала
            foreach (int side in new[] { -1, 1 }) bb.Box(p + Vector3.up * 1.0f + F * (cabZ + cabLen / 2f - 0.1f) + Rt * (side * 0.95f), new Vector3(0.14f, 0.1f, 0.06f), rot, paint);

            var col = new GameObject("Car");
            col.transform.SetParent(root, false); col.transform.localPosition = p + Vector3.up * 0.75f; col.transform.localRotation = rot;
            col.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.5f, len + 0.1f);
            // yaw кратен 180°: прямоугольник по осям
            var w = W(p.x, p.z);
            R.blockers.Add(Rect.MinMaxRect(w.x - 0.9f, w.z - len / 2f - 0.05f, w.x + 0.9f, w.z + len / 2f + 0.05f));
        }

        static void Bin(Transform root, Vector3 p) { Look.RBox("Bin", root, p + Vector3.up * 0.45f, new Vector3(0.55f, 0.9f, 0.55f), Pal.Hex("3E7B5A"), 0.08f, true); }

        // ---------- граф улиц ----------
        static int Node(float x, float z, float lane, string street, float min = 0f)
        {
            R.nodes.Add(W(x, z)); R.nodeLane.Add(lane); R.nodeMin.Add(min); R.nodeStreet.Add(street); R.links.Add(new List<int>());
            return R.nodes.Count - 1;
        }
        static void Link(int a, int b) { if (!R.links[a].Contains(b)) R.links[a].Add(b); if (!R.links[b].Contains(a)) R.links[b].Add(a); }
        static void Chain(params int[] ids) { for (int i = 0; i + 1 < ids.Length; i++) Link(ids[i], ids[i + 1]); }

        static void Graph()
        {
            // проспект: горожане идут по тротуарам (4,55–5,55 м от оси), дорогу переходят у зебры и у бизнес-центра
            int a0 = Node(0, 5, 1f, "av", 4.55f), a1 = Node(0, 20, 1f, "av", 4.55f), a2 = Node(0, 35, 1f, "av", 4.55f), a3 = Node(0, 50, 1f, "av", 4.55f);
            Chain(a0, a1, a2, a3);
            // площадь: кольцо вокруг фонтана
            int s00 = Node(-10, 66, 3.5f, "square"), s10 = Node(0, 66, 3.5f, "square"), s20 = Node(10, 66, 3f, "square");
            int s01 = Node(-10, 76, 3.5f, "square"), s21 = Node(10, 76, 3.5f, "square");
            int s02 = Node(-10, 86, 3.5f, "square"), s12 = Node(0, 86, 3.5f, "square"), s22 = Node(10, 86, 3.5f, "square");
            Chain(s00, s10, s20, s21, s22, s12, s02, s01, s00);
            Link(a3, s10);
            int l0 = Node(-22, 76, 2.8f, "law"), l1 = Node(-32, 76, 2.8f, "law"), l2 = Node(-42, 76, 2.8f, "law"), l3 = Node(-52, 76, 2.8f, "law");
            Chain(s01, l0, l1, l2, l3);
            int b0 = Node(22, 76, 2.8f, "book"), b1 = Node(32, 76, 2.8f, "book"), b2 = Node(42, 76, 2.8f, "book"), b3 = Node(52, 76, 2.8f, "book");
            Chain(s21, b0, b1, b2, b3);
            int r0 = Node(-10.5f, 97, 2.3f, "art"), r1 = Node(-10.5f, 107, 2.3f, "art"), r2 = Node(-10.5f, 117, 2.3f, "art"), r3 = Node(-10.5f, 127, 2.3f, "art");
            Chain(s02, r0, r1, r2, r3);
            int u0 = Node(10.5f, 97, 2.3f, "uni"), u1 = Node(10.5f, 107, 2.3f, "uni"), u2 = Node(10.5f, 117, 2.3f, "uni"), u3 = Node(10.5f, 127, 2.3f, "uni");
            Chain(s22, u0, u1, u2, u3);
        }
    }

    // Прилавок в городе: оружейная, мастерская, шаурма, ларёк
    public class CityShop : Interactable
    {
        public string kind, title;
        public Vector3 front;     // где стоит покупатель
        public override string Prompt { get { return "[E] " + title; } }
        public override void Interact(GameRoot g) { g.OpenCityShop(kind, title); }
    }

    // Дверь бизнес-центра в городе: вернуться в офис раньше времени
    public class CityOfficeDoor : Interactable
    {
        public override string Prompt { get { return "[E] Вернуться в офис"; } }
        public override void Interact(GameRoot g) { g.EndLunch(false); }
    }
}
