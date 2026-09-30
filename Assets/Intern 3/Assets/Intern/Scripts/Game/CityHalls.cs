// Спринт 6 версии 0.9 «Заходим в здания»: четыре дома с вывесками, куда можно войти, — бухгалтерия, инженерный
// центр, «Юристы 24/7» и редакция газеты. У каждого своя планировка и свои обитатели.
//  • дверь открывается по E (или сама, когда к ней подходит горожанин); за ней весь первый этаж, без загрузки;
//  • внутри ряды столов с креслами, шкафы и стеллажи вдоль стен, у каждого дома своя задняя зона: кабинет
//    главбуха за стеклом, верстак и стенды с прототипами, два кабинета адвокатов, печатная машина редакции;
//  • у мебели и стен коллайдеры (стажёр, пули, тела) и «препятствия» для горожан — их обходят вдоль борта;
//  • узлы графа внутри дома связаны с улицей через дверь: горожане входят, выходят и убегают через неё.
// Координаты планировки — в «рамке двери»: u — вдоль фасада от середины двери, v — вглубь дома.
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    // Дом, куда можно войти: что внутри и где
    public class CityHall
    {
        public string id, name, kind;                  // kind: acc — бухгалтерия, eng — инженерный центр, law — юристы, news — редакция
        public Rect rect;                               // внутренность, мир (x, z)
        public Vector3 doorIn, doorOut;                 // мир: за дверью внутри и на тротуаре перед ней
        public string street;                           // улица перед дверью
        public EnterableDoor door;
        public readonly List<Vector3> seats = new List<Vector3>();     // кресла у столов (мир)
        public readonly List<float> seatYaw = new List<float>();       // куда сидящий смотрит
        public readonly List<Vector3> hides = new List<Vector3>();     // за шкафами и в углах
        public readonly List<float> hideYaw = new List<float>();
        public readonly List<Vector3> nodePos = new List<Vector3>();   // узлы графа внутри (мир), связи — пары индексов
        public readonly List<Vector2Int> nodeLinks = new List<Vector2Int>();
        public readonly List<int> nodes = new List<int>();             // индексы этих узлов в графе города
        public string[] humanitarians = new string[0], techies = new string[0];
        public readonly List<Renderer> renderers = new List<Renderer>();   // обстановка: прячется, когда камера далеко
        public readonly List<Light> lights = new List<Light>();
        public string Area { get { return "hall:" + id; } }
        public bool Contains(Vector3 w) { return rect.Contains(new Vector2(w.x, w.z)); }
    }

    // Дверь дома: E — открыть или закрыть; горожане открывают её сами, когда подходят
    public class EnterableDoor : Interactable
    {
        public CityHall hall;
        Quaternion closedRot, openRot; float t; bool open;
        public bool IsOpen { get { return open; } }

        public void Setup(CityHall h, Quaternion closed, Quaternion opened) { hall = h; closedRot = closed; openRot = opened; transform.localRotation = closed; }
        public override string Prompt { get { return open ? "[E] Закрыть дверь" : "[E] Войти: " + (hall != null ? hall.name : "дом"); } }
        public override void Interact(GameRoot g) { if (open) Close(); else Open(); }
        public override PlaqueInfo Plaque(GameRoot g) { return g.HallPlaque(hall); }
        public void Open() { open = true; }
        public void ResetClosed() { open = false; t = 0f; transform.localRotation = closedRot; }
        public void Close() { open = false; }

        void Update()
        {
            float target = open ? 1f : 0f;
            if (Mathf.Approximately(t, target)) return;
            t = Mathf.MoveTowards(t, target, Time.deltaTime / 0.35f);
            float k = t * t * (3f - 2f * t);
            transform.localRotation = Quaternion.Slerp(closedRot, openRot, k);
        }
    }

    public static partial class CityBuilder
    {
        // Детали внутри домов — отдельная сетка: в тени потолка им нужен тёплый «комнатный» свет вместо синеватой уличной тени
        static BoxBatch hb;
        // Первый этаж со стороны двери: полоса фасада с проёмом, витрины, рама, ступень, козырёк и вывеска
        static void HallFront(Bld b, Vector2 a, Vector2 dir, Vector2 n, Vector2 sg, float gf, Color band, float faceLen, string sign, Color brand, string street)
        {
            var root = R.root;
            var n3 = new Vector3(n.x, 0, n.y); var d3 = new Vector3(dir.x, 0, dir.y);
            var rot = Quaternion.LookRotation(n3);
            float t = (sg.x + sg.y) / 2f; const float half = 0.85f;   // проём 1,7 м
            System.Func<float, Vector3> at = tt => new Vector3(a.x + dir.x * tt, 0, a.y + dir.y * tt);
            float l0 = sg.x + 0.15f, l1 = t - half, r0 = t + half, r1 = sg.y - 0.15f;
            var plinth = Color.Lerp(band, Dark, 0.5f);
            if (l1 - l0 > 0.05f)
            {
                Look.RBox("Band", root, at((l0 + l1) / 2f) + n3 * 0.05f + Vector3.up * (gf / 2f), Rot(new Vector3(l1 - l0, gf, 0.1f), d3), band, 0.02f, false, 0.5f);
                bb.Box(at((l0 + l1) / 2f) + n3 * 0.13f + Vector3.up * 0.25f, new Vector3(l1 - l0, 0.5f, 0.1f), rot, plinth, 0f, 0.4f);
            }
            if (r1 - r0 > 0.05f)
            {
                Look.RBox("Band", root, at((r0 + r1) / 2f) + n3 * 0.05f + Vector3.up * (gf / 2f), Rot(new Vector3(r1 - r0, gf, 0.1f), d3), band, 0.02f, false, 0.5f);
                bb.Box(at((r0 + r1) / 2f) + n3 * 0.13f + Vector3.up * 0.25f, new Vector3(r1 - r0, 0.5f, 0.1f), rot, plinth, 0f, 0.4f);
            }
            Look.RBox("Band", root, at(t) + n3 * 0.05f + Vector3.up * (2.45f + (gf - 2.45f) / 2f), Rot(new Vector3(2f * half, gf - 2.45f, 0.1f), d3), band, 0.02f, false, 0.5f);
            foreach (int sd in new[] { -1, 1 })
                foreach (float k in new[] { 2.6f, 5.1f })
                {
                    float wt = t + sd * k;
                    if (wt < sg.x + 1.3f || wt > sg.y - 1.3f) continue;
                    Vitrine(at(wt), d3, n3, 2.0f, 1.8f);
                }
            // рама двери, ступень, козырёк, вывеска
            foreach (int sd in new[] { -1, 1 }) bb.Box(at(t) + d3 * (sd * (half + 0.06f)) + n3 * 0.12f + Vector3.up * 1.25f, new Vector3(0.12f, 2.5f, 0.22f), rot, Dark, 0f, 0.5f);
            bb.Box(at(t) + n3 * 0.12f + Vector3.up * 2.52f, new Vector3(2f * half + 0.24f, 0.14f, 0.22f), rot, Dark, 0f, 0.5f);
            bb.Box(at(t) + n3 * 0.35f + Vector3.up * 0.06f, new Vector3(2.1f, 0.12f, 0.6f), rot, Pal.Hex("B8B0A0"), 0f, 0.4f);
            var aw = rot * Quaternion.Euler(22f, 0, 0);
            bb.Box(at(t) + n3 * 0.55f + Vector3.up * 2.78f, new Vector3(2.3f, 0.07f, 1.0f), aw, brand, 0f, 0.5f);
            bb.Box(at(t) + n3 * 1.02f + Vector3.up * 2.56f, new Vector3(2.3f, 0.2f, 0.05f), rot, Color.Lerp(brand, Color.white, 0.25f), 0f, 0.4f);
            if (sign != null) CityKit.SignBox(bb, root, sign, at(t) + n3 * 0.1f + Vector3.up * 3.3f, d3, n3, brand, SignInk, sign.Length > 18 ? 0.03f : 0.036f, Mathf.Min(sg.y - sg.x - 1f, 9f));
            b.hallName = sign; b.hallStreet = street;
            b.hallDoor = at(t); b.hallAlong = d3; b.hallT = t; b.hallLen = faceLen;
        }

        // ---------- внутренность ----------
        class HallTheme
        {
            public Color floor, wall, trim, wood, metal, accent, accent2, seat;
        }

        static HallTheme Theme(string kind)
        {
            switch (kind)
            {
                case "acc": return new HallTheme { floor = Pal.Hex("C9B79C"), wall = Pal.Hex("E9E4D8"), trim = Pal.Hex("7F8AA3"), wood = Pal.Hex("B8906A"), metal = Pal.Hex("8A8FA8"), accent = Pal.Hex("3A7BD5"), accent2 = Pal.Hex("E8782F"), seat = Pal.Hex("2F3B52") };
                case "eng": return new HallTheme { floor = Pal.Hex("A9AEBB"), wall = Pal.Hex("DDE3EA"), trim = Pal.Hex("5B6275"), wood = Pal.Hex("C9CED9"), metal = Pal.Hex("6F7690"), accent = Pal.Hex("E8782F"), accent2 = Pal.Hex("3A6FB5"), seat = Pal.Hex("3E4459") };
                case "law": return new HallTheme { floor = Pal.Hex("7A4E3A"), wall = Pal.Hex("EFE6D2"), trim = Pal.Hex("5A2E2A"), wood = Pal.Hex("6B4226"), metal = Pal.Hex("8C6A4F"), accent = Pal.Hex("7A1F2A"), accent2 = Pal.Hex("1F5E4A"), seat = Pal.Hex("5A2E2A") };
                default: return new HallTheme { floor = Pal.Hex("8E9AA8"), wall = Pal.Hex("F2EEE6"), trim = Pal.Hex("4A4F63"), wood = Pal.Hex("D8C3A0"), metal = Pal.Hex("7F8AA3"), accent = Pal.Hex("C8453A"), accent2 = Pal.Hex("2F3B52"), seat = Pal.Hex("C8453A") };
            }
        }

        // Рамка двери: мир (локально в городе) из (u, v), размеры из (вдоль, высота, вглубь)
        class HallFrame
        {
            public Vector3 O, A, I; public GameObject colGo; public CityHall hall;
            public Vector3 P(float u, float v, float y = 0f) { return O + A * u + I * v + Vector3.up * y; }
            public Vector3 S(float su, float sy, float sv) { return Mathf.Abs(A.x) > 0.5f ? new Vector3(su, sy, sv) : new Vector3(sv, sy, su); }
            public float YawTo(Vector3 dirLocal) { return Mathf.Atan2(dirLocal.x, dirLocal.z) * Mathf.Rad2Deg; }
        }

        // Коробка мебели (bb), по желанию — с коллайдером и «препятствием» для горожан
        static void HBox(HallFrame f, float u, float v, float y, float su, float sy, float sv, Color c, float outline = 0.4f, float emission = 0f, bool solid = false, bool block = false)
        {
            var center = f.P(u, v, y); var size = f.S(su, sy, sv);
            hb.Box(center, size, Quaternion.identity, c, emission, outline);
            if (solid)
            {
                var col = f.colGo.AddComponent<BoxCollider>(); col.center = center; col.size = size;
            }
            if (block)
            {
                var w = R.root.TransformPoint(center);
                R.blockers.Add(new Rect(w.x - size.x / 2f, w.z - size.z / 2f, size.x, size.z));
            }
        }

        static void BuildHall(Bld b, float gf)
        {
            var root = R.root; var th = Theme(b.hallKind);
            hb.group = b.hallKind;
            // нормаль фасада наружу = along × up; внутрь — обратная
            var inward = -Vector3.Cross(b.hallAlong, Vector3.up);
            float depth = Mathf.Abs(inward.x) > 0.5f ? b.x1 - b.x0 : b.z1 - b.z0;
            var colGo = new GameObject("Hall_" + b.hallKind); colGo.transform.SetParent(root, false);
            var h = new CityHall { id = b.hallKind, name = b.hallName, kind = b.hallKind, street = b.hallStreet };
            var f = new HallFrame { O = b.hallDoor, A = b.hallAlong, I = inward, colGo = colGo, hall = h };
            float uMin = -b.hallT + 0.3f, uMax = b.hallLen - b.hallT - 0.3f, vMax = depth - 0.3f, D = depth;
            // прямоугольник внутренности в мире
            var c0 = root.TransformPoint(f.P(uMin, 0.3f)); var c1 = root.TransformPoint(f.P(uMax, vMax));
            h.rect = Rect.MinMaxRect(Mathf.Min(c0.x, c1.x), Mathf.Min(c0.z, c1.z), Mathf.Max(c0.x, c1.x), Mathf.Max(c0.z, c1.z));
            h.doorIn = root.TransformPoint(f.P(0, 1.0f)); h.doorOut = root.TransformPoint(f.P(0, -1.4f));
            float W = uMax - uMin, uc = (uMin + uMax) / 2f, vc = (0.3f + vMax) / 2f;

            // пол, потолок, стены с проёмом, плинтус
            HBox(f, uc, vc, -0.015f, W, 0.04f, vMax - 0.3f, th.floor, 0f);
            HBox(f, uc, vc, gf - 0.06f, W + 0.6f, 0.12f, vMax, Pal.Hex("F4F1EA"), 0f);
            HBox(f, uMin - 0.15f, D / 2f, gf / 2f, 0.3f, gf, D, th.wall, 0.3f, 0f, true, true);
            HBox(f, uMax + 0.15f, D / 2f, gf / 2f, 0.3f, gf, D, th.wall, 0.3f, 0f, true, true);
            HBox(f, uc, vMax + 0.15f, gf / 2f, W + 0.6f, gf, 0.3f, th.wall, 0.3f, 0f, true, true);
            const float half = 0.85f;
            HBox(f, (uMin - 0.3f - half) / 2f, 0.15f, gf / 2f, -half - (uMin - 0.3f), gf, 0.3f, th.wall, 0.3f, 0f, true, true);
            HBox(f, (uMax + 0.3f + half) / 2f, 0.15f, gf / 2f, uMax + 0.3f - half, gf, 0.3f, th.wall, 0.3f, 0f, true, true);
            HBox(f, 0, 0.15f, 2.45f + (gf - 2.45f) / 2f, 2f * half, gf - 2.45f, 0.3f, th.wall, 0.3f, 0f, true);
            foreach (float u in new[] { uMin + 0.03f, uMax - 0.03f }) HBox(f, u, D / 2f, 0.06f, 0.06f, 0.12f, D - 0.6f, th.trim, 0f);
            HBox(f, uc, vMax - 0.03f, 0.06f, W, 0.12f, 0.06f, th.trim, 0f);
            // светильники на потолке и два источника света
            for (float v = 2.5f; v < vMax - 1f; v += 3.5f)
                foreach (float u in new[] { uMin + W * 0.25f, uMin + W * 0.75f }) HBox(f, u, v, gf - 0.14f, 1.2f, 0.04f, 0.6f, Pal.Hex("FFF6DA"), 0f, 1.6f);
            foreach (float k in new[] { 0.3f, 0.72f })
            {
                var lt = new GameObject("HallLight").AddComponent<Light>();
                lt.transform.SetParent(root, false); lt.transform.localPosition = f.P(uc, D * k, gf - 0.7f);
                lt.type = LightType.Point; lt.range = Mathf.Max(9f, W * 0.8f); lt.intensity = 1.5f; lt.color = Pal.Hex("FFEBC8"); lt.shadows = LightShadows.None;
                h.lights.Add(lt);
            }
            // дверь: створка на петлях у левого края проёма, открывается внутрь
            var pivot = new GameObject("HallDoor");
            pivot.transform.SetParent(root, false); pivot.transform.localPosition = f.P(-half + 0.04f, 0.2f);
            var closed = Quaternion.LookRotation(-inward);   // лицом на улицу
            var opened = Quaternion.AngleAxis(Vector3.SignedAngle(f.A, f.I, Vector3.up), Vector3.up) * closed;
            pivot.transform.localRotation = closed;
            // створка: в осях петли вдоль — локальный x (вправо = along, если смотреть с улицы)
            var leaf = new GameObject("Leaf"); leaf.transform.SetParent(pivot.transform, false);
            float sgn = Vector3.Dot(closed * Vector3.right, f.A) > 0f ? 1f : -1f;
            leaf.transform.localPosition = new Vector3(sgn * (half - 0.04f), 1.15f, 0f);
            Look.RBox("LeafWood", leaf.transform, Vector3.zero, new Vector3(2f * half - 0.1f, 2.3f, 0.06f), DoorWood, 0.02f, false, 0.4f);
            Look.RBox("LeafGlass", leaf.transform, new Vector3(0, 0.35f, 0), new Vector3(1.0f, 0.9f, 0.08f), Pal.Hex("9FC6E8"), 0.01f, false, 0.3f, 0.15f);
            foreach (int sd in new[] { -1, 1 }) Look.RBox("Handle", leaf.transform, new Vector3(sgn * (half - 0.25f), -0.1f, sd * 0.07f), new Vector3(0.05f, 0.28f, 0.05f), Pal.Hex("D9A441"), 0.02f, false, 0.3f, 0.3f);
            leaf.AddComponent<BoxCollider>().size = new Vector3(2f * half - 0.1f, 2.3f, 0.1f);
            var lrb = leaf.AddComponent<Rigidbody>(); lrb.isKinematic = true; lrb.useGravity = false;
            var door = pivot.AddComponent<EnterableDoor>(); door.Setup(h, closed, opened);
            h.door = door;

            // ---------- обстановка ----------
            bool big = D >= 18f;
            float backDepth = big ? 5f : 2.9f, pitch = big ? 3.6f : 3.2f;
            float rowEnd = D - backDepth;
            var rows = new List<float>();
            for (float v = 3.4f; v + 2.3f <= rowEnd; v += pitch) rows.Add(v);
            var slots = new List<float>();
            for (float u = 2.1f; u + 0.9f <= uMax - 1.35f; u += 1.8f) { slots.Add(u); slots.Add(-u); }
            float sideU = uMax - 1.05f;   // проход у стен
            foreach (float rv in rows)
                foreach (float su in slots)
                    DeskSlot(f, th, b.hallKind, su, rv);
            // у стен: шкафы, стеллажи, стенды — с просветами
            for (float v = 2.4f; v + 1.2f <= rowEnd - 0.4f; v += 1.6f)
                foreach (int sd in new[] { -1, 1 })
                    WallUnit(f, th, b.hallKind, sd * (uMax - 0.25f), v + 0.6f, sd);
            // у входа: растения и кулер
            foreach (int sd in new[] { -1, 1 }) Plant(f, sd * (uMax - 0.55f), 0.9f);
            HBox(f, -(uMax - 1.4f), 0.75f, 0.55f, 0.35f, 1.1f, 0.35f, Pal.Hex("E8EEF5"), 0.4f, 0f, true, true);   // кулер
            HBox(f, -(uMax - 1.4f), 0.75f, 1.25f, 0.28f, 0.3f, 0.28f, Pal.Hex("9FD0F0"), 0.3f, 0.1f);
            if (b.hallKind == "law") Sofa(f, th, uMax - 2.0f, 0.62f);   // приёмная: диван для клиентов
            BackZone(f, th, b.hallKind, uMin, uMax, rowEnd, vMax, gf);

            // ---------- узлы графа внутри ----------
            var np = new List<Vector2>(); var links = new List<Vector2Int>();
            System.Func<float, float, int> N = (u, v) => { np.Add(new Vector2(u, v)); return np.Count - 1; };
            int nIn = N(0, 1.0f);
            int front = nIn;
            int sL0 = N(-sideU, 1.8f), sR0 = N(sideU, 1.8f);
            links.Add(new Vector2Int(nIn, sL0)); links.Add(new Vector2Int(nIn, sR0));
            int prevC = nIn, prevL = sL0, prevR = sR0;
            foreach (float rv in rows)
            {
                float av = rv + (big ? 2.1f : 1.9f);
                if (av > rowEnd + 0.6f) av = rowEnd + 0.4f;
                int c = N(0, av), l = N(-sideU, av), r = N(sideU, av);
                links.Add(new Vector2Int(prevC, c)); links.Add(new Vector2Int(prevL, l)); links.Add(new Vector2Int(prevR, r));
                links.Add(new Vector2Int(c, l)); links.Add(new Vector2Int(c, r));
                prevC = c; prevL = l; prevR = r;
                // за шкафом у стены, на уровне ряда столов — по очереди слева и справа
                float hs = (rows.IndexOf(rv) % 2 == 0) ? -sideU : sideU;
                h.hides.Add(root.TransformPoint(f.P(hs, rv + 0.3f))); h.hideYaw.Add(f.YawTo(-f.I));
            }
            // задняя зона: узлы по теме (связаны с последним узлом прохода)
            var backIdx = new List<int>();
            foreach (var bn in BackNodes(b.hallKind, uMin, uMax, rowEnd, vMax))
            {
                int k = N(bn.x, bn.y);
                // z ≥ 0 — связь с одним из предыдущих узлов задней зоны (вход в кабинет), иначе с ближним проходом
                int parent = bn.z >= 0f ? backIdx[(int)bn.z] : Mathf.Abs(bn.x) < 1.5f ? prevC : bn.x < 0 ? prevL : prevR;
                links.Add(new Vector2Int(parent, k)); backIdx.Add(k);
                h.hides.Add(root.TransformPoint(f.P(bn.x, bn.y))); h.hideYaw.Add(f.YawTo(-f.I));
            }
            foreach (var p in np) h.nodePos.Add(root.TransformPoint(f.P(p.x, p.y)));
            h.nodeLinks.AddRange(links);
            // кресла: сидящий лицом к двери
            foreach (float rv in rows)
                foreach (float su in slots)
                {
                    h.seats.Add(root.TransformPoint(f.P(su, rv + 0.72f))); h.seatYaw.Add(f.YawTo(-f.I));
                }
            BackSeats(f, h, b.hallKind, uMin, uMax, rowEnd, vMax);
            switch (b.hallKind)
            {
                case "acc": h.humanitarians = new[] { "lawyer", "notary", "philologist" }; h.techies = new[] { "accountant", "accountant" }; break;
                case "eng": h.humanitarians = new[] { "critic", "philosopher", "critic" }; h.techies = new[] { "engineer", "engineer" }; break;
                case "law": h.humanitarians = new[] { "lawyer", "lawyer", "notary", "lawyer" }; break;
                default: h.humanitarians = new[] { "journalist", "journalist", "philologist", "poet" }; h.techies = new[] { "itguy" }; break;
            }
            R.halls.Add(h);
            hb.group = null;
        }

        // После сборки сеток: обстановку каждого дома — в его список (для CityHallCull)
        static void CollectHallRenderers()
        {
            foreach (var h in R.halls)
            {
                h.renderers.Clear();
                foreach (Transform c in R.root) if (c.name == "HallDetails_" + h.id) { var r = c.GetComponent<Renderer>(); if (r != null) h.renderers.Add(r); }
            }
        }

        // Рабочее место: стол, кресло и что на столе — по теме дома
        static void DeskSlot(HallFrame f, HallTheme th, string kind, float u, float v)
        {
            float cv = v + 0.72f;   // кресло
            if (kind == "eng")
            {
                // чертёжный стол: наклонная доска с листом, опора, табурет
                HBox(f, u, v, 0.45f, 0.12f, 0.9f, 0.12f, th.metal, 0.3f);
                HBox(f, u, v, 0.05f, 0.9f, 0.08f, 0.6f, th.metal, 0.3f);
                var tilt = Quaternion.AngleAxis(-25f, f.A);
                var boardC = f.P(u, v, 0.95f);
                hb.Box(boardC, f.S(1.4f, 0.04f, 0.9f), tilt, th.wood, 0f, 0.4f);
                hb.Box(boardC + tilt * Vector3.up * 0.025f, f.S(1.1f, 0.01f, 0.65f), tilt, Pal.Hex("3A6FB5"), 0.15f);
                hb.Box(boardC + tilt * Vector3.up * 0.032f + f.A * 0.1f, f.S(0.5f, 0.005f, 0.02f), tilt, Pal.Hex("E8F0FF"), 0.3f);
                hb.Box(boardC + tilt * Vector3.up * 0.032f - f.I * 0.1f, f.S(0.02f, 0.005f, 0.4f), tilt, Pal.Hex("E8F0FF"), 0.3f);
                var col = f.colGo.AddComponent<BoxCollider>(); col.center = f.P(u, v, 0.55f); col.size = f.S(1.4f, 1.1f, 0.9f);
                var w = R.root.TransformPoint(f.P(u, v)); var sz = f.S(1.4f, 1f, 0.9f);
                R.blockers.Add(new Rect(w.x - sz.x / 2f, w.z - sz.z / 2f, sz.x, sz.z));
                HBox(f, u, cv, 0.35f, 0.08f, 0.7f, 0.08f, th.metal, 0f);
                HBox(f, u, cv, 0.66f, 0.42f, 0.07f, 0.42f, th.seat, 0.3f, 0f, true);
                return;
            }
            var top = kind == "law" ? th.wood : kind == "acc" ? Pal.Hex("E6DCCB") : th.wood;
            HBox(f, u, v, 0.73f, 1.6f, 0.05f, 0.8f, top, 0.4f, 0f, true, true);
            HBox(f, u, v - 0.37f, 0.38f, 1.5f, 0.6f, 0.04f, Color.Lerp(top, Dark, 0.25f), 0.3f);
            foreach (int sd in new[] { -1, 1 }) HBox(f, u + sd * 0.77f, v, 0.36f, 0.04f, 0.72f, 0.76f, Color.Lerp(top, Dark, 0.25f), 0.3f);
            // кресло: сиденье, спинка за сидящим, стойка и крестовина
            HBox(f, u, cv, 0.46f, 0.48f, 0.08f, 0.48f, th.seat, 0.3f, 0f, true);
            HBox(f, u, cv + 0.25f, 0.78f, 0.46f, 0.55f, 0.06f, th.seat, 0.3f);
            HBox(f, u, cv, 0.22f, 0.06f, 0.4f, 0.06f, Dark, 0f);
            HBox(f, u, cv, 0.04f, 0.5f, 0.04f, 0.08f, Dark, 0f); HBox(f, u, cv, 0.04f, 0.08f, 0.04f, 0.5f, Dark, 0f);
            switch (kind)
            {
                case "acc":
                    HBox(f, u - 0.2f, v - 0.15f, 0.97f, 0.55f, 0.34f, 0.04f, Pal.Hex("2B2D42"), 0.3f);          // монитор
                    HBox(f, u - 0.2f, v - 0.13f, 0.97f, 0.49f, 0.28f, 0.02f, Pal.Hex("7FB3E8"), 0f, 0.5f);
                    HBox(f, u - 0.2f, v - 0.15f, 0.8f, 0.06f, 0.12f, 0.06f, Pal.Hex("2B2D42"), 0f);
                    HBox(f, u - 0.2f, v + 0.12f, 0.765f, 0.45f, 0.02f, 0.15f, Pal.Hex("3E4459"), 0f);         // клавиатура
                    HBox(f, u + 0.5f, v + 0.05f, 0.77f, 0.14f, 0.03f, 0.2f, Pal.Hex("4A4A5E"), 0.3f);          // калькулятор
                    HBox(f, u + 0.5f, v - 0.2f, 0.82f, 0.26f, 0.14f, 0.32f, Pal.Hex(((int)(u * 7 + v * 3)) % 2 == 0 ? "E8782F" : "3A7BD5"), 0.3f);   // папки
                    break;
                case "law":
                    HBox(f, u - 0.45f, v - 0.15f, 0.77f, 0.14f, 0.03f, 0.14f, Pal.Hex("D9A441"), 0.3f);        // лампа
                    HBox(f, u - 0.45f, v - 0.15f, 0.95f, 0.03f, 0.34f, 0.03f, Pal.Hex("D9A441"), 0f);
                    HBox(f, u - 0.45f, v - 0.05f, 1.13f, 0.26f, 0.1f, 0.18f, Pal.Hex("1F5E4A"), 0.3f, 0.35f);
                    for (int i = 0; i < 3; i++) HBox(f, u + 0.35f, v - 0.1f, 0.79f + i * 0.06f, 0.36f - i * 0.03f, 0.05f, 0.26f, Pal.Hex(i % 2 == 0 ? "7A1F2A" : "1F5E4A"), 0.3f);   // кодексы
                    HBox(f, u, v + 0.15f, 0.765f, 0.3f, 0.01f, 0.22f, Pal.Hex("F4F1EA"), 0f);
                    break;
                default:   // редакция: ноутбук, стопки бумаги, кружка
                    HBox(f, u - 0.15f, v + 0.05f, 0.765f, 0.4f, 0.02f, 0.28f, Pal.Hex("B9BCD6"), 0.3f);
                    var lid = Quaternion.AngleAxis(-15f, f.A);
                    hb.Box(f.P(u - 0.15f, v - 0.1f, 0.9f), f.S(0.4f, 0.27f, 0.02f), lid, Pal.Hex("B9BCD6"), 0f, 0.3f);
                    hb.Box(f.P(u - 0.15f, v - 0.09f, 0.9f), f.S(0.36f, 0.23f, 0.005f), lid, Pal.Hex("8FC1F0"), 0.5f);
                    for (int i = 0; i < 3; i++) HBox(f, u + 0.45f, v - 0.15f, 0.77f + i * 0.04f, 0.3f, 0.035f, 0.4f, Pal.Hex(i % 2 == 0 ? "F4F1EA" : "E6E0D0"), 0.2f);
                    HBox(f, u + 0.5f, v + 0.2f, 0.8f, 0.08f, 0.1f, 0.08f, Pal.Hex("C8453A"), 0.3f);
                    break;
            }
        }

        // Шкаф, стеллаж или стенд у стены; sd — сторона (−1 — у левой стены)
        static void WallUnit(HallFrame f, HallTheme th, string kind, float u, float v, int sd)
        {
            float depth = 0.45f; float uu = u - sd * 0.0f;
            switch (kind)
            {
                case "acc":
                    HBox(f, uu, v, 1.0f, depth, 2.0f, 1.2f, th.metal, 0.5f, 0f, true, true);
                    for (int shelf = 0; shelf < 4; shelf++)
                        for (int k = 0; k < 5; k++)
                            HBox(f, uu - sd * 0.24f, v - 0.45f + k * 0.22f, 0.3f + shelf * 0.46f, 0.04f, 0.34f, 0.18f,
                                 Pal.Hex(new[] { "3A7BD5", "E8782F", "7FB58A", "D9A441", "C8453A" }[(k + shelf * 2) % 5]), 0f);
                    break;
                case "eng":
                    // стенд: столешница с прототипом и доска с чертежом на стене
                    HBox(f, uu, v, 0.45f, depth, 0.9f, 1.2f, th.metal, 0.5f, 0f, true, true);
                    HBox(f, uu, v, 1.05f, 0.3f, 0.3f, 0.3f, Pal.Hex(((int)(v * 3)) % 2 == 0 ? "E8782F" : "7FB58A"), 0.5f);
                    HBox(f, uu, v + 0.3f, 0.98f, 0.18f, 0.16f, 0.18f, Pal.Hex("B8BCC8"), 0.4f);
                    HBox(f, u + sd * 0.2f, v, 1.9f, 0.04f, 0.8f, 1.1f, Pal.Hex("F4F1EA"), 0.3f);
                    HBox(f, u + sd * 0.17f, v, 1.9f, 0.01f, 0.6f, 0.9f, Pal.Hex("3A6FB5"), 0f, 0.2f);
                    break;
                case "law":
                    HBox(f, uu, v, 1.15f, depth, 2.3f, 1.2f, th.wood, 0.5f, 0f, true, true);
                    for (int shelf = 0; shelf < 5; shelf++)
                        for (int k = 0; k < 6; k++)
                            HBox(f, uu - sd * 0.24f, v - 0.5f + k * 0.2f, 0.28f + shelf * 0.44f, 0.04f, 0.32f, 0.15f,
                                 Pal.Hex(new[] { "7A1F2A", "1F5E4A", "D9A441", "2F3B52", "8C6A4F", "5A2E2A" }[(k * 3 + shelf) % 6]), 0f);
                    break;
                default:
                    // стойка с газетами и пробковая доска с заметками
                    HBox(f, uu, v, 0.55f, depth, 1.1f, 1.2f, th.metal, 0.5f, 0f, true, true);
                    for (int i = 0; i < 6; i++) HBox(f, uu, v - 0.3f + (i % 3) * 0.3f, 1.12f + (i / 3) * 0.05f, 0.34f, 0.04f, 0.26f, Pal.Hex(i % 2 == 0 ? "F4F1EA" : "E6E0D0"), 0.2f);
                    HBox(f, u + sd * 0.2f, v, 1.85f, 0.04f, 0.9f, 1.1f, Pal.Hex("C9A36B"), 0.3f);
                    for (int i = 0; i < 5; i++) HBox(f, u + sd * 0.17f, v - 0.35f + i * 0.17f, 1.75f + (i % 2) * 0.25f, 0.01f, 0.18f, 0.14f, Pal.Hex(i % 3 == 0 ? "FFF3C4" : i % 3 == 1 ? "F4F1EA" : "9FD0F0"), 0f);
                    break;
            }
        }

        static void Sofa(HallFrame f, HallTheme th, float u, float v)
        {
            HBox(f, u, v, 0.22f, 1.8f, 0.44f, 0.6f, th.accent, 0.4f, 0f, true, true);
            HBox(f, u, v - 0.25f, 0.62f, 1.8f, 0.5f, 0.14f, th.accent, 0.4f);
            foreach (int sd in new[] { -1, 1 }) HBox(f, u + sd * 0.85f, v, 0.42f, 0.14f, 0.36f, 0.6f, Color.Lerp(th.accent, Dark, 0.2f), 0.4f);
        }

        static void Plant(HallFrame f, float u, float v)
        {
            HBox(f, u, v, 0.22f, 0.4f, 0.44f, 0.4f, Pal.Hex("C8703F"), 0.4f, 0f, true, true);
            HBox(f, u, v, 0.7f, 0.5f, 0.6f, 0.5f, Pal.Hex("4CAF50"), 0.4f);
            HBox(f, u, v, 1.05f, 0.34f, 0.35f, 0.34f, Pal.Hex("6CC24A"), 0.4f);
        }

        // Задняя зона: у каждого дома своя
        static void BackZone(HallFrame f, HallTheme th, string kind, float uMin, float uMax, float v0, float vMax, float gf)
        {
            float mid = (v0 + vMax) / 2f;
            switch (kind)
            {
                case "acc":
                {
                    // кабинет главбуха за стеклом (слева) и переговорная (справа)
                    float wall = -1.2f;
                    HBox(f, (uMin + wall) / 2f - 0.6f, v0 + 0.05f, 0.5f, (wall - uMin) - 1.4f, 1.0f, 0.1f, th.trim, 0.4f, 0f, true, true);   // низ перегородки
                    HBox(f, (uMin + wall) / 2f - 0.6f, v0 + 0.05f, 2.0f, (wall - uMin) - 1.4f, 2.0f, 0.04f, Pal.Hex("BDE7FF"), 0.2f, 0.12f, true);   // стекло
                    HBox(f, wall, (v0 + vMax) / 2f, 0.5f, 0.1f, 1.0f, vMax - v0, th.trim, 0.4f, 0f, true, true);
                    HBox(f, wall, (v0 + vMax) / 2f, 2.0f, 0.04f, 2.0f, vMax - v0, Pal.Hex("BDE7FF"), 0.2f, 0.12f, true);
                    HBox(f, (uMin + wall) / 2f - 0.6f, v0 + 0.05f, 3.05f, (wall - uMin) - 1.4f, 0.1f, 0.12f, th.trim, 0.3f);
                    // стол главбуха, сейф, кресло
                    float bu = (uMin + wall) / 2f;
                    HBox(f, bu, mid + 0.2f, 0.74f, 2.0f, 0.06f, 0.9f, th.wood, 0.4f, 0f, true, true);
                    HBox(f, bu, mid - 0.2f, 0.38f, 1.9f, 0.7f, 0.05f, Color.Lerp(th.wood, Dark, 0.3f), 0.3f);
                    HBox(f, bu + 0.4f, mid + 0.1f, 0.98f, 0.5f, 0.32f, 0.04f, Pal.Hex("2B2D42"), 0.3f);
                    HBox(f, bu + 0.4f, mid + 0.12f, 0.98f, 0.44f, 0.26f, 0.02f, Pal.Hex("7FB3E8"), 0f, 0.5f);
                    HBox(f, bu, mid + 0.95f, 0.5f, 0.55f, 0.1f, 0.55f, Pal.Hex("5A2E2A"), 0.3f, 0f, true);
                    HBox(f, bu, mid + 1.22f, 0.9f, 0.55f, 0.75f, 0.08f, Pal.Hex("5A2E2A"), 0.3f);
                    HBox(f, uMin + 0.45f, vMax - 0.45f, 0.6f, 0.7f, 1.2f, 0.7f, Pal.Hex("4A4F63"), 0.5f, 0f, true, true);   // сейф
                    HBox(f, uMin + 0.45f, vMax - 0.83f, 0.8f, 0.12f, 0.12f, 0.04f, Pal.Hex("D9A441"), 0.3f, 0.3f);
                    // переговорная: стол и кресла
                    float mu = (wall + uMax) / 2f + 0.3f;
                    HBox(f, mu, mid, 0.74f, 2.6f, 0.06f, 1.2f, th.wood, 0.4f, 0f, true, true);
                    foreach (int sd in new[] { -1, 1 }) HBox(f, mu + sd * 0.8f, mid, 0.36f, 0.08f, 0.72f, 0.08f, Dark, 0f);
                    foreach (int sd in new[] { -1, 1 }) foreach (float du in new[] { -0.7f, 0.7f }) HBox(f, mu + du, mid + sd * 0.95f, 0.46f, 0.46f, 0.08f, 0.46f, th.seat, 0.3f, 0f, true);
                    HBox(f, uMax - 0.6f, vMax - 0.5f, 0.55f, 0.8f, 1.1f, 0.6f, Pal.Hex("E8EEF5"), 0.5f, 0f, true, true);   // принтер
                    HBox(f, uMax - 0.6f, vMax - 0.5f, 1.12f, 0.6f, 0.04f, 0.4f, Pal.Hex("F4F1EA"), 0.2f);
                    break;
                }
                case "eng":
                {
                    // верстак вдоль задней стены, стенды с прототипами посередине, доска со схемой
                    HBox(f, (uMin + uMax) / 2f, vMax - 0.45f, 0.45f, uMax - uMin - 1.2f, 0.9f, 0.8f, th.metal, 0.5f, 0f, true, true);
                    HBox(f, (uMin + uMax) / 2f, vMax - 0.45f, 0.92f, uMax - uMin - 1.1f, 0.05f, 0.85f, Pal.Hex("8C6A4F"), 0.4f);
                    for (int i = 0; i < 6; i++) HBox(f, uMin + 1.2f + i * (uMax - uMin - 2.4f) / 5f, vMax - 0.5f, 1.05f, 0.25f, 0.2f, 0.2f, Pal.Hex(new[] { "E8782F", "3A6FB5", "D9A441" }[i % 3]), 0.4f);
                    HBox(f, (uMin + uMax) / 2f, vMax - 0.05f, 1.9f, 3.2f, 1.2f, 0.05f, Pal.Hex("F4F1EA"), 0.3f);
                    for (int i = 0; i < 5; i++) HBox(f, (uMin + uMax) / 2f - 1.2f + i * 0.6f, vMax - 0.08f, 1.7f + (i % 2) * 0.35f, 0.4f, 0.03f, 0.01f, Pal.Hex("3A6FB5"), 0f);
                    foreach (int sd in new[] { -1, 1 })
                    {
                        float su = sd * (uMax - uMin) * 0.22f;
                        HBox(f, su, mid - 0.6f, 0.4f, 1.0f, 0.8f, 1.0f, Pal.Hex("5B6275"), 0.5f, 0f, true, true);
                        HBox(f, su, mid - 0.6f, 1.0f, 0.5f, 0.4f, 0.5f, Pal.Hex(sd < 0 ? "E8782F" : "7FB58A"), 0.5f, 0.1f);
                        HBox(f, su + 0.2f, mid - 0.4f, 1.3f, 0.12f, 0.35f, 0.12f, Pal.Hex("B8BCC8"), 0.4f);
                    }
                    break;
                }
                case "law":
                {
                    // два кабинета адвокатов за перегородкой с дверными проёмами
                    float pv = v0 + 0.05f;
                    foreach (int sd in new[] { -1, 1 })
                    {
                        float outer = sd < 0 ? uMin : uMax, doorC = sd * (Mathf.Abs(outer) * 0.5f);
                        // перегородка от центра до стены, с проёмом 1 м
                        float a0 = 0f, a1 = doorC - sd * 0.5f, b0 = doorC + sd * 0.5f, b1 = outer;
                        HBox(f, (a0 + a1) / 2f, pv, gf / 2f - 0.05f, Mathf.Abs(a1 - a0), gf - 0.1f, 0.12f, th.wall, 0.3f, 0f, true, true);
                        HBox(f, (b0 + b1) / 2f, pv, gf / 2f - 0.05f, Mathf.Abs(b1 - b0), gf - 0.1f, 0.12f, th.wall, 0.3f, 0f, true, true);
                        HBox(f, doorC, pv, 2.4f + (gf - 2.5f) / 2f, 1.0f, gf - 2.5f, 0.12f, th.wall, 0.3f, 0f, true);
                        HBox(f, doorC, pv - 0.07f, 1.2f, 1.1f, 2.4f, 0.02f, th.trim, 0f);
                        // стол и кресло, книжный шкаф у стены
                        float du = sd * (Mathf.Abs(outer) * 0.55f);
                        HBox(f, du, mid + 0.1f, 0.74f, 1.5f, 0.06f, 0.75f, th.wood, 0.4f, 0f, true, true);
                        HBox(f, du, mid - 0.25f, 0.38f, 1.4f, 0.7f, 0.05f, Color.Lerp(th.wood, Dark, 0.3f), 0.3f);
                        HBox(f, du - sd * 0.35f, mid + 0.05f, 0.95f, 0.03f, 0.34f, 0.03f, Pal.Hex("D9A441"), 0f);
                        HBox(f, du - sd * 0.35f, mid + 0.1f, 1.13f, 0.26f, 0.1f, 0.18f, Pal.Hex("1F5E4A"), 0.3f, 0.35f);
                        HBox(f, du, mid + 0.75f, 0.5f, 0.5f, 0.1f, 0.5f, th.seat, 0.3f, 0f, true);
                        HBox(f, du, mid + 1.0f, 0.9f, 0.5f, 0.75f, 0.08f, th.seat, 0.3f);
                    }
                    HBox(f, 0, (v0 + vMax) / 2f, gf / 2f - 0.05f, 0.12f, gf - 0.1f, vMax - v0, th.wall, 0.3f, 0f, true, true);   // стена между кабинетами
                    // приёмная у входа: стойка
                    HBox(f, uMax - 1.6f, 2.2f, 0.55f, 1.8f, 1.1f, 0.6f, th.wood, 0.5f, 0f, true, true);
                    HBox(f, uMax - 1.6f, 2.2f, 1.12f, 1.9f, 0.05f, 0.7f, Pal.Hex("F4F1EA"), 0.3f);
                    break;
                }
                default:
                {
                    // редакция: печатная машина, пачки газет, доска с полосами номера
                    float pu = uMin + 1.6f;
                    HBox(f, pu, mid, 0.7f, 2.4f, 1.4f, 1.3f, Pal.Hex("4A4F63"), 0.5f, 0f, true, true);
                    foreach (float dv in new[] { -0.35f, 0.05f, 0.45f }) { var c = f.P(pu, mid + dv, 1.45f); hb.Box(c, f.S(2.2f, 0.22f, 0.22f), Quaternion.identity, Pal.Hex("B8BCC8"), 0f, 0.4f); }
                    HBox(f, pu, mid - 0.75f, 0.95f, 2.0f, 0.03f, 0.2f, Pal.Hex("F4F1EA"), 0.2f);
                    for (int i = 0; i < 5; i++) HBox(f, 1.0f + (i % 3) * 0.7f, mid - 0.2f + (i / 3) * 0.7f, 0.2f + (i % 2) * 0.12f, 0.5f, 0.4f + (i % 2) * 0.24f, 0.36f, Pal.Hex(i % 2 == 0 ? "F4F1EA" : "E6E0D0"), 0.3f, 0f, true, true);
                    HBox(f, (uMin + uMax) / 2f, vMax - 0.05f, 1.8f, 3.6f, 1.2f, 0.05f, Pal.Hex("C9A36B"), 0.3f);
                    for (int i = 0; i < 6; i++) HBox(f, (uMin + uMax) / 2f - 1.4f + i * 0.56f, vMax - 0.08f, 1.8f, 0.4f, 0.55f, 0.01f, Pal.Hex(i % 2 == 0 ? "F4F1EA" : "E6E0D0"), 0f);
                    break;
                }
            }
        }

        // Узлы задней зоны (u, v): входы в кабинеты, места у стендов — там же прячутся
        // (u, v, к какому из предыдущих узлов задней зоны привязан; −1 — к ближнему проходу)
        static List<Vector3> BackNodes(string kind, float uMin, float uMax, float v0, float vMax)
        {
            float mid = (v0 + vMax) / 2f; var l = new List<Vector3>();
            switch (kind)
            {
                case "acc": l.Add(new Vector3(-1.8f, v0 - 0.5f, -1)); l.Add(new Vector3(-2.3f, v0 + 1.0f, 0)); l.Add(new Vector3((uMax - 1.2f) / 2f + 0.8f, v0 + 0.35f, -1)); break;
                case "eng": l.Add(new Vector3(0f, mid + 0.4f, -1)); l.Add(new Vector3(-(uMax - uMin) * 0.22f, mid + 0.3f, -1)); l.Add(new Vector3((uMax - uMin) * 0.22f, mid + 0.3f, -1)); break;
                case "law": l.Add(new Vector3(uMin * 0.5f, v0 - 0.5f, -1)); l.Add(new Vector3(uMax * 0.5f, v0 - 0.5f, -1));
                            l.Add(new Vector3(uMin * 0.5f, v0 + 0.8f, 0)); l.Add(new Vector3(uMax * 0.5f, v0 + 0.8f, 1)); break;
                default: l.Add(new Vector3(0.4f, mid + 0.6f, -1)); l.Add(new Vector3(uMax - 1.2f, mid, -1)); break;
            }
            return l;
        }

        // Кресла в задней зоне: главбух, адвокаты в кабинетах, переговорная
        static void BackSeats(HallFrame f, CityHall h, string kind, float uMin, float uMax, float v0, float vMax)
        {
            float mid = (v0 + vMax) / 2f;
            System.Action<float, float, Vector3> seat = (u, v, look) => { h.seats.Add(R.root.TransformPoint(f.P(u, v))); h.seatYaw.Add(f.YawTo(look)); };
            switch (kind)
            {
                case "acc": seat((uMin - 1.2f) / 2f, mid + 0.95f, -f.I); seat((-1.2f + uMax) / 2f + 0.3f - 0.7f, mid + 0.95f, -f.I); break;
                case "law": foreach (int sd in new[] { -1, 1 }) seat(sd * (Mathf.Abs(sd < 0 ? uMin : uMax) * 0.55f), mid + 0.75f, -f.I); break;
            }
        }

        // Узлы домов в графе: внутри — свои, у двери — узел на тротуаре, связанный с ближайшими уличными
        static void HallGraph()
        {
            foreach (var h in R.halls)
            {
                h.nodes.Clear();
                foreach (var p in h.nodePos) { var l = p - R.root.position; h.nodes.Add(Node(l.x, l.z, 0.25f, h.Area)); }
                foreach (var lk in h.nodeLinks) Link(h.nodes[lk.x], h.nodes[lk.y]);
                var o = h.doorOut - R.root.position;
                int outside = Node(o.x, o.z, 0.2f, h.street ?? "street");
                Link(outside, h.nodes[0]);
                // две ближайшие уличные вершины (не из домов)
                var cand = new List<KeyValuePair<float, int>>();
                for (int i = 0; i < R.nodes.Count; i++)
                {
                    if (i == outside || R.nodeStreet[i].StartsWith("hall:")) continue;
                    var d = R.nodes[i] - h.doorOut; d.y = 0; cand.Add(new KeyValuePair<float, int>(d.sqrMagnitude, i));
                }
                cand.Sort((x, y) => x.Key.CompareTo(y.Key));
                for (int k = 0; k < 2 && k < cand.Count; k++) Link(outside, cand[k].Value);
            }
        }
    }

    // Обстановка домов рисуется, только когда камера рядом (30 м от стен) — издалека внутрь всё равно не видно
    public class CityHallCull : MonoBehaviour
    {
        public List<CityHall> halls;
        readonly Dictionary<CityHall, bool> shown = new Dictionary<CityHall, bool>();
        float next;

        void Update()
        {
            if (halls == null || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var cam = Camera.main; if (cam == null) return;
            var p = cam.transform.position;
            foreach (var h in halls)
            {
                float dx = Mathf.Max(0f, Mathf.Max(h.rect.xMin - p.x, p.x - h.rect.xMax));
                float dz = Mathf.Max(0f, Mathf.Max(h.rect.yMin - p.z, p.z - h.rect.yMax));
                bool near = dx * dx + dz * dz < 30f * 30f;
                bool was; if (shown.TryGetValue(h, out was) && was == near) continue;
                shown[h] = near;
                foreach (var r in h.renderers) if (r != null) r.enabled = near;
                foreach (var l in h.lights) if (l != null) l.enabled = near;
            }
        }
    }
}
