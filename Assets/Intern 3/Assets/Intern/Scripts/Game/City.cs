// Город на обед (v0.8, спринт 2): проспект от бизнес-центра, рыночная площадь и 4 переулка.
// Всё строится кодом один раз, далеко от офиса, и дальше только включается и выключается.
// Улицы — прямоугольники (где можно ходить), дома стоят вокруг и сами понимают, какой стороной смотрят на улицу.
// Горожане ходят по графу улиц: узлы по осевым линиям, у каждого отрезка своя ширина для «полосы».
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
        public readonly List<string> nodeStreet = new List<string>();
        public readonly List<List<int>> links = new List<List<int>>();
        public readonly List<Vector3> alleyEnds = new List<Vector3>();
        public readonly List<KeyValuePair<string, Vector3>> landmarks = new List<KeyValuePair<string, Vector3>>();
        public Vector3 officeDoor, archiveDoor;
        public readonly List<GameObject> shopKeepers = new List<GameObject>();

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
                pts.Add(n + side * lane * nodeLane[chain[i]]);
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

        static readonly Color Dark = Pal.Hex("2B2D42"), Glass = Pal.Hex("BDE7FF"), DoorWood = Pal.Hex("6B4226"), SignInk = Pal.Hex("F4F1EA");

        class Bld
        {
            public float x0, x1, z0, z1, h; public string color;
            public readonly Dictionary<char, string> signs = new Dictionary<char, string>();   // N S E W → вывеска
            public char roomFace; public string roomKind, roomSign;                              // заходить можно
            public Bld(float x0, float x1, float z0, float z1, float h, string color) { this.x0 = x0; this.x1 = x1; this.z0 = z0; this.z1 = z1; this.h = h; this.color = color; }
            public Bld Sign(char f, string s) { signs[f] = s; return this; }
            public Bld Room(char f, string kind, string sign) { roomFace = f; roomKind = kind; roomSign = sign; return this; }
        }

        public static CityRefs Build()
        {
            R = new CityRefs(); rnd = new System.Random(11);
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
                new Bld(-30, 30, -12, 0, 30, "7FA6D6"),
                // проспект, запад
                new Bld(-30, -7, 0, 15, 18, "E8B4A0").Sign('E', "Аптека"),
                new Bld(-30, -7, 15, 30, 24, "A8C5E0").Sign('E', "Бухгалтерия"),
                new Bld(-30, -7, 30, 45, 15, "C9B8E8").Sign('E', "IT-парк"),
                new Bld(-30, -7, 45, 60, 21, "F0D38C").Sign('E', "Почта").Room('N', "food", "Шаурма"),
                // проспект, восток
                new Bld(7, 30, 0, 15, 21, "9ED9C4").Sign('W', "Продукты"),
                new Bld(7, 30, 15, 30, 15, "E8A0B8").Sign('W', "Инженерный центр"),
                new Bld(7, 30, 30, 45, 24, "B8C9A0").Sign('W', "Банк"),
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

            // ---------- ларёк с патронами, фонтан, деревья, фонари ----------
            Kiosk(root, new Vector3(12.5f, 0, 64.5f));
            Fountain(root, new Vector3(0, 0, 76));
            foreach (var p in new[] { new Vector3(-12, 0, 88), new Vector3(12, 0, 88), new Vector3(-12, 0, 64), new Vector3(-5, 0, 3), new Vector3(5, 0, 3) })
                Tree(root, p);
            for (float z = 8f; z < 60f; z += 12f) foreach (int sd in new[] { -1, 1 }) Lamp(root, new Vector3(sd * 6.4f, 0, z), sd);
            foreach (var p in new[] { new Vector3(-36, 0, 73), new Vector3(-48, 0, 79), new Vector3(36, 0, 79), new Vector3(48, 0, 73), new Vector3(-13.5f, 0, 110), new Vector3(13.5f, 0, 100), new Vector3(-7.5f, 0, 124), new Vector3(7.5f, 0, 115) })
                Lamp(root, p, 0);
            foreach (var p in new[] { new Vector3(-6.2f, 0, 25), new Vector3(6.2f, 0, 40), new Vector3(-14.8f, 0, 70), new Vector3(14.8f, 0, 84) }) Bench(root, p, p.x < 0 ? 90f : -90f);
            foreach (var p in new[] { new Vector3(6.3f, 0, 12), new Vector3(-6.3f, 0, 50), new Vector3(-20, 0, 72.6f), new Vector3(24, 0, 79.4f), new Vector3(-13.4f, 0, 98), new Vector3(13.4f, 0, 126) }) Bin(root, p);

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
            // тротуары вдоль проспекта, брусчатка площади и переулков
            Flat(root, "WalkL", -5.5f, 0, 3f, 60, Pal.Hex("A9A6B8"));
            Flat(root, "WalkR", 5.5f, 0, 3f, 60, Pal.Hex("A9A6B8"));
            for (float z = 3f; z < 58f; z += 5f) Flat(root, "Mark", 0, z, 0.18f, 2.2f, Pal.Hex("F4F1EA"), 0.014f);
            for (float x = -3f; x <= 3f; x += 1.2f) Flat(root, "Zebra", x, 57.5f, 0.6f, 3f, Pal.Hex("F4F1EA"), 0.014f);
            Flat(root, "Square", 0, 60, 32, 32, Pal.Hex("C9B99A"));
            for (int i = -3; i <= 3; i++) { Flat(root, "Tile", i * 4.4f, 60, 0.12f, 32, Pal.Hex("B8A88A"), 0.014f); Flat(root, "Tile", 0, 60 + 16 + i * 4.4f - 0.06f, 32, 0.12f, Pal.Hex("B8A88A"), 0.014f); }
            Flat(root, "LawStone", -36, 72, 40, 8, Pal.Hex("8E8A9E"));
            Flat(root, "BookStone", 36, 72, 40, 8, Pal.Hex("8E8A9E"));
            Flat(root, "ArtStone", -10.5f, 92, 7, 40, Pal.Hex("9A8E9E"));
            Flat(root, "UniStone", 10.5f, 92, 7, 40, Pal.Hex("8E9A9E"));
        }

        // плоская полоса: центр по x, начало по z, ширина, длина
        static void Flat(Transform root, string n, float xc, float z0, float w, float len, Color c, float y = 0.01f)
        {
            Look.RBox(n, root, new Vector3(xc, y, z0 + len / 2f), new Vector3(w, 0.02f, len), c, 0.005f, false, 0f, 0f, false);
        }

        // ---------- дом ----------
        static void Building(Bld b)
        {
            var root = R.root;
            var c = Pal.Hex(b.color);
            float w = b.x1 - b.x0, d = b.z1 - b.z0, xc = (b.x0 + b.x1) / 2f, zc = (b.z0 + b.z1) / 2f;
            bool room = b.roomKind != null;
            const float gf = 3.8f;   // высота первого этажа
            if (!room) Look.RBox("House", root, new Vector3(xc, b.h / 2f, zc), new Vector3(w - 0.1f, b.h, d - 0.1f), c, 0.1f, true, 0.6f);
            else
            {
                Look.RBox("House", root, new Vector3(xc, gf + (b.h - gf) / 2f, zc), new Vector3(w - 0.1f, b.h - gf, d - 0.1f), c, 0.1f, true, 0.6f);
                HollowGround(b, c, gf);
            }
            // каждая сторона дома: где она выходит на улицу — фасад с окнами, первый этаж с витринами и дверями
            foreach (var f in new[] { 'N', 'S', 'E', 'W' }) Face(b, f, c, gf);
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
            var band = Color.Lerp(c, Dark, 0.55f);
            string sign; b.signs.TryGetValue(f, out sign);
            bool roomHere = b.roomKind != null && b.roomFace == f;
            foreach (var sg in segs)
            {
                float sl = sg.y - sg.x, mid = (sg.x + sg.y) / 2f;
                var m2 = a + dir * mid;
                var mid3 = new Vector3(m2.x, 0, m2.y); var n3 = new Vector3(n.x, 0, n.y); var d3 = new Vector3(dir.x, 0, dir.y);
                // окна верхних этажей
                float upper = b.h - gf - 0.4f;
                Facade(R.root, mid3 + n3 * 0.02f + Vector3.up * (gf + upper / 2f), new Vector2(sl - 0.4f, upper), yaw, c, Mathf.Max(1, Mathf.RoundToInt(sl / 3f)), Mathf.Max(1, Mathf.RoundToInt(upper / 3f)));
                // первый этаж: тёмная полоса
                if (!roomHere) Look.RBox("Band", R.root, mid3 + n3 * 0.05f + Vector3.up * (gf / 2f), Rot(new Vector3(sl - 0.3f, gf, 0.1f), d3), band, 0.02f, false, 0.5f);
                // витрины и двери вдоль полосы
                string street = StreetOf((mid3 + n3 * 1f).x, (mid3 + n3 * 1f).z);
                if (roomHere) { RoomFront(b, a, dir, n, sg, gf, band, street); continue; }
                int doors = Mathf.Max(1, Mathf.FloorToInt(sl / 9f));
                for (int i = 0; i < doors; i++)
                {
                    float t = sg.x + sl * (i + 0.5f) / doors;
                    var p2 = a + dir * t; var p3 = new Vector3(p2.x, 0, p2.y);
                    Look.RBox("Door", R.root, p3 + n3 * 0.1f + Vector3.up * 1.15f, Rot(new Vector3(1.3f, 2.3f, 0.08f), d3), DoorWood, 0.02f, false, 0.6f);
                    Look.RBox("DoorTop", R.root, p3 + n3 * 0.11f + Vector3.up * 2.45f, Rot(new Vector3(1.5f, 0.15f, 0.1f), d3), Dark, 0.02f, false, 0.4f);
                    // витрины по бокам от двери
                    foreach (int sd in new[] { -1, 1 })
                    {
                        float wt = t + sd * 2.4f;
                        if (wt < sg.x + 1f || wt > sg.y - 1f) continue;
                        var w2 = a + dir * wt;
                        Look.RBox("ShopWindow", R.root, new Vector3(w2.x, 1.6f, w2.y) + n3 * 0.1f, Rot(new Vector3(2.4f, 1.8f, 0.05f), d3), Glass, 0.02f, false, 0.3f, 0.15f);
                    }
                    var door = new CityDoor { pos = R.root.TransformPoint(p3 + n3 * 0.9f), outward = n3, street = street, kind = "door", open = rnd.NextDouble() < 0.45 };
                    R.doors.Add(door);
                    if (i == doors / 2 && sign != null) Sign(R.root, sign, p3 + n3 * 0.16f + Vector3.up * 3.05f, yaw, SignInk, sign.Length > 18 ? 0.032f : 0.04f);
                }
            }
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
                Look.RBox("ShopWindow", root, face0 + d3 * wt + n3 * 0.17f + Vector3.up * 1.6f, Rot(new Vector3(2.8f, 1.9f, 0.05f), d3), Glass, 0.02f, false, 0.3f, 0.35f);
            }
            float yaw = Mathf.Atan2(n.x, n.y) * Mathf.Rad2Deg;
            var entry = face0 + d3 * mid;
            Sign(root, b.roomSign, entry + n3 * 0.3f + Vector3.up * 3.1f, yaw, Pal.Sun, b.roomSign.Length > 16 ? 0.034f : 0.042f);
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
            Sign(root, "CODEZILLA", new Vector3(0, 3.6f, 0.2f), 0f, Pal.Hex("FF4F9A"), 0.05f);
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
            Sign(root, "ПАТРОНЫ", p + new Vector3(-1.3f, 2.25f, 0), -90f, Pal.Sun, 0.024f);
        }

        static void Fountain(Transform root, Vector3 p)
        {
            Look.Prim("FountainRim", root, PrimitiveType.Cylinder, p + Vector3.up * 0.3f, new Vector3(5f, 0.3f, 5f), Pal.Hex("B8B0A0"), true, 0.6f);
            Look.Prim("FountainWater", root, PrimitiveType.Cylinder, p + Vector3.up * 0.5f, new Vector3(4.4f, 0.05f, 4.4f), Pal.Hex("7FC4E8"), false, 0f, 0.4f, false);
            Look.Prim("FountainPost", root, PrimitiveType.Cylinder, p + Vector3.up * 1.1f, new Vector3(0.5f, 0.8f, 0.5f), Pal.Hex("B8B0A0"), true, 0.6f);
            Look.Prim("FountainTop", root, PrimitiveType.Sphere, p + Vector3.up * 2.1f, new Vector3(0.9f, 0.4f, 0.9f), Pal.Hex("7FC4E8"), false, 0.4f, 0.4f);
        }

        static void Tree(Transform root, Vector3 p)
        {
            Look.Prim("Trunk", root, PrimitiveType.Cylinder, p + Vector3.up * 1.2f, new Vector3(0.3f, 1.2f, 0.3f), Pal.Hex("6B4226"), true, 0.6f);
            Look.Prim("Crown", root, PrimitiveType.Sphere, p + Vector3.up * 3.1f, new Vector3(2.6f, 2.2f, 2.6f), Pal.Hex("5FBF4A"), false, 0.6f);
        }

        static void Lamp(Transform root, Vector3 p, int side)
        {
            Look.Prim("LampPole", root, PrimitiveType.Cylinder, p + Vector3.up * 2f, new Vector3(0.12f, 2f, 0.12f), Dark, true, 0.6f);
            Look.Prim("LampHead", root, PrimitiveType.Sphere, p + new Vector3(-side * 0.3f, 4.05f, 0), new Vector3(0.45f, 0.3f, 0.45f), Pal.Hex("FFE7A8"), false, 0.4f, 1.2f);
        }

        static void Bench(Transform root, Vector3 p, float yaw)
        {
            var b = Look.RBox("Bench", root, p + Vector3.up * 0.25f, new Vector3(1.8f, 0.5f, 0.5f), Pal.Hex("8C6A4F"), 0.05f, true);
            b.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        }

        static void Bin(Transform root, Vector3 p) { Look.RBox("Bin", root, p + Vector3.up * 0.45f, new Vector3(0.55f, 0.9f, 0.55f), Pal.Hex("3E7B5A"), 0.08f, true); }

        static void Sign(Transform root, string text, Vector3 pos, float yaw, Color c, float size)
        {
            // вывеска читается с улицы: у TextMesh лицо смотрит в −Z, поэтому разворачиваем на 180°
            var t = OfficeBuilder.Label(text, pos, size, c, root, yaw + 180f);
            t.fontStyle = FontStyle.Bold;
        }

        // ---------- граф улиц ----------
        static int Node(float x, float z, float lane, string street)
        {
            R.nodes.Add(W(x, z)); R.nodeLane.Add(lane); R.nodeStreet.Add(street); R.links.Add(new List<int>());
            return R.nodes.Count - 1;
        }
        static void Link(int a, int b) { if (!R.links[a].Contains(b)) R.links[a].Add(b); if (!R.links[b].Contains(a)) R.links[b].Add(a); }
        static void Chain(params int[] ids) { for (int i = 0; i + 1 < ids.Length; i++) Link(ids[i], ids[i + 1]); }

        static void Graph()
        {
            int a0 = Node(0, 5, 5f, "av"), a1 = Node(0, 20, 5f, "av"), a2 = Node(0, 35, 5f, "av"), a3 = Node(0, 50, 5f, "av");
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

        // ---------- фасад с окнами ----------
        static void Facade(Transform root, Vector3 pos, Vector2 size, float yaw, Color wall, int cols, int rows)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Facade"; UnityEngine.Object.Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(root, false);
            // у квада лицо смотрит в −Z: разворачиваем, чтобы окна смотрели на улицу
            q.transform.localPosition = pos; q.transform.localRotation = Quaternion.Euler(0, yaw + 180f, 0);
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var inst = new Material(FacadeMat(wall)) { mainTextureScale = new Vector2(cols, rows) };
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
