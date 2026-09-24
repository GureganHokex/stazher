// Мир: стилизованный офис из скруглённых форм, коллеги, игрок, интерактивные объекты, баги.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    // Ссылки на важные точки офиса
    public class OfficeRefs
    {
        public Transform spawn, playerDesk, playerChair, screen, lockerSpot;
        public Transform chairObj; // кресло целиком — его можно отодвигать при посадке
        public Transform screenQuad; // экран монитора игрока (свой прямоугольник перед корпусом), если есть
        public Vector2 screenSize;
        public TextMesh board;
        public CharacterAnim lead;
    }

    public static class OfficeBuilder
    {
        static Transform root;
        static Font font;

        public static Font DefaultFont
        {
            get
            {
                if (font != null) return font;
                try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                if (font == null) try { font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                return font;
            }
        }

        public static TextMesh Label(string text, Vector3 pos, float size, Color c, Transform parent = null, float yaw = 0)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent != null ? parent : root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text; tm.font = DefaultFont; tm.fontSize = 64; tm.characterSize = size;
            tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = c;
            if (tm.font != null) go.GetComponent<MeshRenderer>().material = tm.font.material;
            return tm;
        }

        // Краткие обёртки
        static GameObject B(string n, Vector3 p, Vector3 s, Color c, float r = 0.06f, bool col = true, float outline = 1f, float em = 0f, bool shadows = true, Transform parent = null)
        { return Look.RBox(n, parent != null ? parent : root, p, s, c, r, col, outline, em, shadows); }
        static GameObject P(string n, PrimitiveType t, Vector3 p, Vector3 s, Color c, bool col = false, float outline = 1f, float em = 0f, Transform parent = null)
        { return Look.Prim(n, parent != null ? parent : root, t, p, s, c, col, outline, em); }

        public static OfficeRefs Build()
        {
            var refs = new OfficeRefs();
            root = new GameObject("Office").transform;
            var rnd = new System.Random(7);

            Color floorA = Pal.Hex("EAC48C"), floorB = Pal.Hex("DDB47A"), wall = Pal.Hex("9ADBCD"), wallLow = Pal.Hex("6FC2B3"),
                  side = Pal.Hex("C3B2F5"), sideLow = Pal.Hex("A591EC"), desk = Pal.Hex("F7BE4B"), deskLeg = Pal.Hex("3B3F66"),
                  chair = Pal.Hex("EF6F4E"), dark = Pal.Hex("2D3052"), screen = Pal.Hex("6FE7EC"), mine = Pal.Hex("FFD23F"),
                  pot = Pal.Hex("D9774A"), white = Pal.Hex("FFF9F0");

            // ---------- Пол, стены, потолок ----------
            B("Floor", new Vector3(0, -0.1f, 0), new Vector3(24, 0.2f, 16), floorA, 0, true, 0, 0, false);
            for (int i = 0; i < 20; i += 2)
                B("Plank", new Vector3(0, 0.003f, -7.6f + i * 0.8f + 0.4f), new Vector3(24, 0.004f, 0.8f), floorB, 0, false, 0, 0, false);
            P("Rug", PrimitiveType.Cylinder, new Vector3(7, 0.012f, -3.2f), new Vector3(6.2f, 0.008f, 4.6f), Pal.Hex("F59AB5"), false, 0);
            P("RugInner", PrimitiveType.Cylinder, new Vector3(7, 0.02f, -3.2f), new Vector3(4.6f, 0.008f, 3.2f), Pal.Hex("FFC2D4"), false, 0);

            B("Ceiling", new Vector3(0, 4.1f, 0), new Vector3(24, 0.2f, 16), white, 0, true, 0, 0, false);
            Wall("WallN", new Vector3(0, 0, 8), new Vector3(24, 4, 0.2f), wall, wallLow, Vector3.back);
            Wall("WallS", new Vector3(0, 0, -8), new Vector3(24, 4, 0.2f), wall, wallLow, Vector3.forward);
            Wall("WallE", new Vector3(12, 0, 0), new Vector3(0.2f, 4, 16), side, sideLow, Vector3.left);
            Wall("WallW", new Vector3(-12, 0, 0), new Vector3(0.2f, 4, 16), side, sideLow, Vector3.right);

            // Окна на северной стене: рама, «небо», перекладины, подоконник
            foreach (var x in new[] { -9f, -4f, 1f })
            {
                B("WinFrame", new Vector3(x, 2.25f, 7.86f), new Vector3(3.1f, 2.0f, 0.1f), white, 0.05f, false, 1, 0, false);
                B("SkyTop", new Vector3(x, 2.65f, 7.8f), new Vector3(2.8f, 0.85f, 0.02f), Pal.Hex("A9DCFF"), 0, false, 0, 0.9f, false);
                B("SkyLow", new Vector3(x, 1.8f, 7.8f), new Vector3(2.8f, 0.86f, 0.02f), Pal.Hex("D6F0FF"), 0, false, 0, 0.9f, false);
                B("Hill", new Vector3(x - 0.5f, 1.52f, 7.79f), new Vector3(1.6f, 0.3f, 0.02f), Pal.Hex("8FD694"), 0.14f, false, 0, 0.5f, false);
                B("MullionV", new Vector3(x, 2.25f, 7.77f), new Vector3(0.07f, 1.75f, 0.04f), white, 0.02f, false, 0.5f, 0, false);
                B("MullionH", new Vector3(x, 2.25f, 7.77f), new Vector3(2.8f, 0.07f, 0.04f), white, 0.02f, false, 0.5f, 0, false);
                B("Sill", new Vector3(x, 1.22f, 7.7f), new Vector3(3.3f, 0.08f, 0.3f), white, 0.03f, false, 1, 0, false);
                if (Look.ToonAvailable)
                    for (int k = 0; k < 2; k++)
                    {
                        var win = Look.Node("CloudTrack", root, new Vector3(x, 2.5f + k * 0.28f, 7.795f));
                        var cl = Look.Node("Cloud", win, new Vector3(-1f + k * 1.1f, 0, 0));
                        P("Puff", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.42f, 0.2f, 0.01f), Color.white, false, 0, 0.9f, cl);
                        P("Puff", PrimitiveType.Sphere, new Vector3(0.14f, 0.07f, 0), new Vector3(0.26f, 0.2f, 0.01f), Color.white, false, 0, 0.9f, cl);
                        var cc = cl.gameObject.AddComponent<Cloud>(); cc.halfWidth = 1.15f; cc.speed = 0.06f + k * 0.03f;
                    }
                P("Cactus", PrimitiveType.Capsule, new Vector3(x + 1.1f, 1.38f, 7.7f), new Vector3(0.12f, 0.12f, 0.12f), Pal.Hex("5FBF4A"));
            }

            // ---------- Столы и коллеги ----------
            Transform playerDesk = null;
            var coworkers = new[]
            {
                new { col = 1, row = 0, ap = new Appearance { skin = 1, hair = 1, hairColor = 0, eyes = 0, mouth = 0, top = 1, topColor = 1, pants = 9, shoes = 10, accessory = 2 } },
                new { col = 2, row = 0, ap = new Appearance { skin = 2, hair = 2, hairColor = 3, eyes = 1, mouth = 1, top = 3, topColor = 2, pants = 8, shoes = 9, accessory = 0 } },
                new { col = 0, row = 1, ap = new Appearance { skin = 4, hair = 3, hairColor = 4, eyes = 2, mouth = 2, top = 0, topColor = 6, pants = 9, shoes = 5, accessory = 1 } },
                new { col = 2, row = 1, ap = new Appearance { skin = 0, hair = 4, hairColor = 2, eyes = 3, mouth = 3, top = 2, topColor = 8, pants = 11, shoes = 10, accessory = 4 } },
            };
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 3; col++)
                {
                    bool isMine = row == 0 && col == 0;
                    var d = new GameObject(isMine ? "PlayerDesk" : "Desk").transform;
                    d.SetParent(root, false);
                    d.localPosition = new Vector3(-5 + col * 5, 0, 2.5f - row * 4.5f);

                    B("Top", new Vector3(0, 0.75f, 0), new Vector3(1.9f, 0.08f, 0.95f), desk, 0.035f, true, 1, 0, true, d);
                    B("Panel", new Vector3(0, 0.45f, 0.4f), new Vector3(1.7f, 0.55f, 0.04f), Color.Lerp(desk, deskLeg, 0.25f), 0.02f, false, 0.5f, 0, true, d);
                    foreach (var lx in new[] { -0.85f, 0.85f })
                        B("Leg", new Vector3(lx, 0.36f, 0), new Vector3(0.07f, 0.72f, 0.8f), deskLeg, 0.03f, false, 1, 0, true, d);

                    Vector3 monSize = isMine ? new Vector3(1.34f, 0.8f, 0.07f) : new Vector3(1.0f, 0.62f, 0.07f);
                    Vector2 scrSize = isMine ? new Vector2(1.24f, 0.7f) : new Vector2(0.9f, 0.52f);
                    float monY = isMine ? 1.3f : 1.22f;
                    var mon = B("Monitor", new Vector3(0, monY, 0.25f), monSize, dark, 0.05f, true, 1, 0, true, d);
                    B("Stand", new Vector3(0, 0.9f, 0.28f), new Vector3(0.08f, 0.24f, 0.06f), dark, 0.02f, false, 1, 0, true, d);
                    B("StandBase", new Vector3(0, 0.8f, 0.25f), new Vector3(0.34f, 0.03f, 0.2f), dark, 0.015f, false, 1, 0, true, d);
                    var scr = B("Screen", new Vector3(0, monY, 0.212f), new Vector3(scrSize.x, scrSize.y, 0.01f), isMine ? mine : screen, 0.03f, isMine, 0, isMine ? 1.3f : 0.8f, false, d);
                    if (isMine) { refs.screen = scr.transform; refs.screenSize = scrSize; }
                    // «код» на экране — цветные строчки
                    for (int k = 0; k < (isMine ? 0 : 5); k++)
                    {
                        float w = 0.2f + (float)rnd.NextDouble() * 0.45f;
                        B("CodeLine", new Vector3(-0.38f + w / 2 + (k % 2) * 0.06f, 1.41f - k * 0.08f, 0.205f), new Vector3(w, 0.03f, 0.005f),
                          isMine ? Pal.Ink : (k % 2 == 0 ? Pal.Hex("FF79C6") : Pal.Hex("2B2D42")), 0, false, 0, 0.3f, false, d);
                    }
                    B("Keyboard", new Vector3(0, 0.8f, -0.12f), new Vector3(0.62f, 0.03f, 0.2f), Pal.Hex("E9E6FF"), 0.015f, false, 1, 0, true, d);
                    B("Mouse", new Vector3(0.45f, 0.8f, -0.12f), new Vector3(0.07f, 0.03f, 0.11f), Pal.Hex("E9E6FF"), 0.03f, false, 1, 0, true, d);
                    P("Mug", PrimitiveType.Cylinder, new Vector3(-0.62f, 0.84f, -0.1f), new Vector3(0.1f, 0.06f, 0.1f), isMine ? Pal.Pink : white);
                    if ((row + col) % 2 == 1)
                    {
                        B("Papers", new Vector3(-0.55f, 0.8f, 0.15f), new Vector3(0.28f, 0.03f, 0.36f), white, 0.01f, false, 0.5f, 0, true, d);
                        P("MiniPlant", PrimitiveType.Sphere, new Vector3(0.75f, 0.9f, 0.3f), Vector3.one * 0.18f, Pal.Hex("6BCB5A"), false, 1, 0, d);
                    }

                    // кресло
                    var ch = new GameObject("Chair").transform; ch.SetParent(d, false); ch.localPosition = new Vector3(0, 0, -0.85f);
                    B("Seat", new Vector3(0, 0.46f, 0), new Vector3(0.6f, 0.12f, 0.58f), chair, 0.06f, true, 1, 0, true, ch);
                    B("Back", new Vector3(0, 0.86f, -0.3f), new Vector3(0.58f, 0.62f, 0.1f), chair, 0.05f, false, 1, 0, true, ch);
                    P("Pole", PrimitiveType.Cylinder, new Vector3(0, 0.23f, 0), new Vector3(0.07f, 0.2f, 0.07f), deskLeg);
                    B("Base", new Vector3(0, 0.05f, 0), new Vector3(0.55f, 0.05f, 0.55f), deskLeg, 0.025f, false, 1, 0, true, ch);

                    if (isMine)
                    {
                        playerDesk = d;
                        mon.AddComponent<ComputerDesk>();
                        scr.AddComponent<ComputerDesk>();
                        refs.playerChair = ch;
                        refs.chairObj = ch;
                        B("Sticky", new Vector3(0.72f, 1.55f, 0.205f), new Vector3(0.13f, 0.13f, 0.01f), Pal.Hex("FFE066"), 0.01f, false, 0.5f, 0, false, d).transform.localRotation = Quaternion.Euler(0, 0, 8);
                        B("Sticky2", new Vector3(-0.72f, 1.1f, 0.205f), new Vector3(0.13f, 0.13f, 0.01f), Pal.Hex("FF9FC6"), 0.01f, false, 0.5f, 0, false, d).transform.localRotation = Quaternion.Euler(0, 0, -6);
                        // уточка сидит на мониторе
                        var duck = Look.Node("Duck", d, new Vector3(0.45f, 1.78f, 0.25f));
                        P("DuckBody", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.16f, 0.12f, 0.18f), Pal.Hex("FFD23F"), false, 1, 0, duck);
                        P("DuckHead", PrimitiveType.Sphere, new Vector3(0, 0.09f, -0.05f), Vector3.one * 0.1f, Pal.Hex("FFD23F"), false, 1, 0, duck);
                        P("DuckBeak", PrimitiveType.Sphere, new Vector3(0, 0.08f, -0.11f), new Vector3(0.06f, 0.025f, 0.05f), Pal.Hex("FF9F43"), false, 1, 0, duck);
                        P("DuckEye", PrimitiveType.Sphere, new Vector3(0.03f, 0.11f, -0.09f), Vector3.one * 0.018f, Pal.Ink, false, 0, 0, duck);
                        P("DuckEye", PrimitiveType.Sphere, new Vector3(-0.03f, 0.11f, -0.09f), Vector3.one * 0.018f, Pal.Ink, false, 0, 0, duck);
                        duck.gameObject.AddComponent<Bobber>().amplitude = 0.01f;
                        Label("Твоё место", new Vector3(-0.2f, 1.95f, 0.25f), 0.02f, Pal.Ink, d);
                        var arrow = P("Arrow", PrimitiveType.Sphere, new Vector3(-0.2f, 2.25f, 0.25f), Vector3.one * 0.18f, Pal.Pink, false, 1, 0.4f, d);
                        arrow.AddComponent<Bobber>();
                    }
                    else
                    {
                        foreach (var cw in coworkers)
                            if (cw.col == col && cw.row == row)
                            {
                                var a = Look.Bean("Coworker", d, new Vector3(0, 0, -0.85f), 0, cw.ap);
                                a.SetSitInstant(1); a.typing = true; a.lookAtPlayer = true;
                            }
                    }
                }

            // ---------- Доска-канбан и тимлид ----------
            var board = new GameObject("Board").transform; board.SetParent(root, false); board.localPosition = new Vector3(7.5f, 0, 7.85f);
            B("Frame", new Vector3(0, 1.85f, 0), new Vector3(4.2f, 2.0f, 0.08f), Pal.Hex("B9BCD6"), 0.04f, true, 1, 0, true, board);
            B("Surface", new Vector3(0, 1.85f, -0.045f), new Vector3(4.0f, 1.8f, 0.02f), white, 0.02f, false, 0, 0, false, board);
            refs.board = Label("", new Vector3(-1.05f, 1.9f, -0.07f), 0.015f, Pal.Ink, board);
            string[] cols = { "TODO", "В РАБОТЕ", "ГОТОВО" };
            Color[] notes = { Pal.Hex("FFE066"), Pal.Hex("FF9FC6"), Pal.Hex("9EE6B8"), Pal.Hex("A6D8FF") };
            for (int c = 0; c < 3; c++)
            {
                Label(cols[c], new Vector3(0.35f + c * 0.58f, 2.6f, -0.07f), 0.009f, Pal.Muted, board);
                int cnt = 3 - c + rnd.Next(0, 2);
                for (int k = 0; k < cnt; k++)
                    B("Note", new Vector3(0.35f + c * 0.58f + ((k % 2) - 0.5f) * 0.1f, 2.35f - k * 0.3f, -0.065f), new Vector3(0.24f, 0.24f, 0.01f), notes[(c + k) % 4], 0.01f, false, 0, 0, false, board);
            }

            var lead = Look.Bean("TeamLead", root, new Vector3(8.2f, 0, 5.6f), 180,
                new Appearance { skin = 1, hair = 0, hairColor = 1, eyes = 0, mouth = 1, top = 2, topColor = 0, pants = 9, shoes = 9, accessory = 1 }, 1.12f);
            refs.lead = lead;
            lead.lookAtPlayer = true;
            var cap = lead.gameObject.AddComponent<CapsuleCollider>(); cap.center = new Vector3(0, 0.9f, 0); cap.height = 1.9f; cap.radius = 0.4f;
            lead.gameObject.AddComponent<TeamLeadNpc>();
            P("LeadMug", PrimitiveType.Cylinder, new Vector3(0, -0.38f, 0.12f), new Vector3(0.13f, 0.08f, 0.13f), Pal.Pink, false, 1, 0, lead.armL);
            var tag = Label("Тимлид Гена", new Vector3(8.2f, 2.55f, 5.6f), 0.017f, Pal.Ink);
            tag.gameObject.AddComponent<Billboard>();

            // ---------- Кофейный уголок (магазин) ----------
            var cm = new GameObject("CoffeeCorner").transform; cm.SetParent(root, false); cm.localPosition = new Vector3(-10.9f, 0, -5.5f);
            B("Counter", new Vector3(0, 0.5f, 0), new Vector3(1.3f, 1f, 2.4f), Pal.Hex("F7BE4B"), 0.05f, true, 1, 0, true, cm);
            B("CounterTop", new Vector3(0, 1.02f, 0), new Vector3(1.4f, 0.06f, 2.5f), white, 0.03f, false, 1, 0, true, cm);
            var mach = B("Machine", new Vector3(0, 1.45f, 0.3f), new Vector3(0.62f, 0.8f, 0.6f), Pal.Hex("3D3D5C"), 0.08f, true, 1, 0, true, cm);
            B("Nozzle", new Vector3(0.3f, 1.3f, 0.3f), new Vector3(0.1f, 0.12f, 0.2f), Pal.Hex("B9BCD6"), 0.03f, false, 1, 0, true, cm);
            B("Lamp", new Vector3(0.315f, 1.65f, 0.3f), new Vector3(0.02f, 0.1f, 0.24f), Pal.Pink, 0.01f, false, 0, 1.6f, false, cm);
            P("Cup", PrimitiveType.Cylinder, new Vector3(0.35f, 1.12f, 0.3f), new Vector3(0.12f, 0.06f, 0.12f), white, false, 1, 0, cm);
            for (int k = 0; k < 3; k++) P("Donut", PrimitiveType.Sphere, new Vector3(0.2f, 1.1f, -0.5f + k * 0.22f), new Vector3(0.18f, 0.07f, 0.18f), Pal.Hex(k == 1 ? "FF9FC6" : "C98E68"), false, 1, 0, cm);
            mach.AddComponent<CoffeeMachine>();
            var shopTag = Label("Кофе и апгрейды", new Vector3(0, 2.3f, 0.3f), 0.015f, Pal.Ink, cm);
            shopTag.gameObject.AddComponent<Billboard>();

            // Кулер
            var cool = new GameObject("Cooler").transform; cool.SetParent(root, false); cool.localPosition = new Vector3(-10.9f, 0, -2.6f);
            B("Body", new Vector3(0, 0.55f, 0), new Vector3(0.45f, 1.1f, 0.45f), white, 0.08f, true, 1, 0, true, cool);
            P("Bottle", PrimitiveType.Cylinder, new Vector3(0, 1.4f, 0), new Vector3(0.36f, 0.3f, 0.36f), Pal.Hex("8FD3FF"), false, 1, 0.25f, cool);

            // ---------- Зона отдыха: диван, столик, мешок ----------
            var lounge = new GameObject("Lounge").transform; lounge.SetParent(root, false); lounge.localPosition = new Vector3(10.6f, 0, -3.2f);
            Color sofa = Pal.Hex("8E7CF2");
            B("SofaSeat", new Vector3(0, 0.35f, 0), new Vector3(0.95f, 0.4f, 2.6f), sofa, 0.14f, true, 1, 0, true, lounge);
            B("SofaBack", new Vector3(0.42f, 0.8f, 0), new Vector3(0.28f, 0.75f, 2.6f), sofa, 0.12f, false, 1, 0, true, lounge);
            B("ArmL", new Vector3(0, 0.6f, 1.3f), new Vector3(0.95f, 0.5f, 0.25f), Color.Lerp(sofa, Pal.Ink, 0.12f), 0.1f, false, 1, 0, true, lounge);
            B("ArmR", new Vector3(0, 0.6f, -1.3f), new Vector3(0.95f, 0.5f, 0.25f), Color.Lerp(sofa, Pal.Ink, 0.12f), 0.1f, false, 1, 0, true, lounge);
            B("Pillow", new Vector3(0.18f, 0.72f, 0.7f), new Vector3(0.18f, 0.4f, 0.45f), Pal.Hex("FFD23F"), 0.09f, false, 1, 0, true, lounge);
            B("Table", new Vector3(-1.5f, 0.35f, 0), new Vector3(0.9f, 0.08f, 1.4f), Pal.Hex("F7BE4B"), 0.04f, true, 1, 0, true, lounge);
            P("TableLeg", PrimitiveType.Cylinder, new Vector3(-1.5f, 0.17f, 0), new Vector3(0.18f, 0.17f, 0.18f), deskLeg, false, 1, 0, lounge);
            B("Laptop", new Vector3(-1.5f, 0.41f, 0.2f), new Vector3(0.4f, 0.03f, 0.3f), Pal.Hex("B9BCD6"), 0.015f, false, 1, 0, true, lounge);
            P("BeanBag", PrimitiveType.Sphere, new Vector3(-2.8f, 0.35f, 1.2f), new Vector3(1.0f, 0.7f, 1.0f), Pal.Hex("4FC3A1"), true, 1, 0, lounge);

            // ---------- Книжная полка у западной стены ----------
            var shelf = new GameObject("Shelf").transform; shelf.SetParent(root, false); shelf.localPosition = new Vector3(-11.6f, 0, 3.2f);
            B("Frame", new Vector3(0, 1.1f, 0), new Vector3(0.5f, 2.2f, 2.2f), Pal.Hex("C98E68"), 0.04f, true, 1, 0, true, shelf);
            Color[] bookCols = { Pal.Hex("FF4F9A"), Pal.Hex("7C83FD"), Pal.Hex("FFD23F"), Pal.Hex("4FC3A1"), Pal.Hex("FFA24C"), Pal.Hex("9FD8FF") };
            for (int lvl = 0; lvl < 4; lvl++)
            {
                B("Board", new Vector3(0.05f, 0.25f + lvl * 0.5f, 0), new Vector3(0.48f, 0.04f, 2.1f), Pal.Hex("E7B084"), 0.01f, false, 0.5f, 0, true, shelf);
                float z = -0.95f;
                while (z < 0.9f)
                {
                    float w = 0.08f + (float)rnd.NextDouble() * 0.07f, hgt = 0.28f + (float)rnd.NextDouble() * 0.14f;
                    B("Book", new Vector3(0.08f, 0.27f + lvl * 0.5f + hgt / 2, z + w / 2), new Vector3(0.3f, hgt, w), bookCols[rnd.Next(bookCols.Length)], 0.012f, false, 0.6f, 0, true, shelf);
                    z += w + 0.01f;
                    if (rnd.NextDouble() < 0.12) z += 0.2f;
                }
            }

            // ---------- Постеры, логотип, часы ----------
            Poster(new Vector3(-11.88f, 2.3f, -1.2f), -90, "Сначала тесты,\nпотом релиз", Pal.Hex("FFD23F"));
            Poster(new Vector3(11.88f, 2.3f, 2.5f), 90, "Не деплой\nв пятницу!", Pal.Hex("FF9FC6"));
            Poster(new Vector3(-2.5f, 2.3f, -7.88f), 180, "Читай ошибки —\nони подсказывают", Pal.Hex("9EE6B8"));
            Label("КОДЗИЛЛА СОФТ", new Vector3(3.5f, 3.35f, -7.86f), 0.035f, Pal.Pink, null, 180);

            var clock = new GameObject("Clock").transform; clock.SetParent(root, false); clock.localPosition = new Vector3(4.3f, 2.9f, 7.87f);
            var face = P("Face", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.7f, 0.03f, 0.7f), white, false, 1, 0, clock);
            face.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var hourPivot = new GameObject("Hour").transform; hourPivot.SetParent(clock, false); hourPivot.localPosition = new Vector3(0, 0, -0.04f);
            B("HourHand", new Vector3(0, 0.1f, 0), new Vector3(0.04f, 0.2f, 0.01f), Pal.Ink, 0.01f, false, 0, 0, false, hourPivot);
            var minPivot = new GameObject("Minute").transform; minPivot.SetParent(clock, false); minPivot.localPosition = new Vector3(0, 0, -0.05f);
            B("MinHand", new Vector3(0, 0.14f, 0), new Vector3(0.03f, 0.28f, 0.01f), Pal.Pink, 0.01f, false, 0, 0, false, minPivot);
            var wc = clock.gameObject.AddComponent<WallClock>(); wc.hour = hourPivot; wc.minute = minPivot;

            // ---------- Растения ----------
            Vector3[] plants = { new Vector3(-11, 0, 7), new Vector3(11, 0, -7), new Vector3(11, 0, 1.2f), new Vector3(-11, 0, -0.5f), new Vector3(3, 0, -7), new Vector3(-6.5f, 0, 7) };
            foreach (var p in plants)
            {
                var pl = new GameObject("Plant").transform; pl.SetParent(root, false); pl.localPosition = p;
                B("Pot", new Vector3(0, 0.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), pot, 0.14f, true, 1, 0, true, pl);
                B("Rim", new Vector3(0, 0.6f, 0), new Vector3(0.68f, 0.1f, 0.68f), Color.Lerp(pot, Color.white, 0.2f), 0.05f, false, 1, 0, true, pl);
                int n = 3 + rnd.Next(3);
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2 / n + (float)rnd.NextDouble();
                    float s = 0.45f + (float)rnd.NextDouble() * 0.3f;
                    var leafCol = Color.Lerp(Pal.Hex("5FBF4A"), Pal.Hex("8FDB6A"), (float)rnd.NextDouble());
                    var leaf = P("Leaf", PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * 0.18f, 0.95f + (float)rnd.NextDouble() * 0.4f, Mathf.Sin(a) * 0.18f), new Vector3(s, s * 1.15f, s), leafCol, false, 1, 0, pl);
                    leaf.AddComponent<Sway>();
                }
            }

            // ---------- Светильники и свет ----------
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) l.gameObject.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                var lp = Look.Node("PendantLamp", root, new Vector3(-6 + i * 6, 0, 0.3f));
                P("Cord", PrimitiveType.Cylinder, new Vector3(0, 3.65f, 0), new Vector3(0.02f, 0.45f, 0.02f), Pal.Ink, false, 0, 0, lp);
                var shade = P("Shade", PrimitiveType.Sphere, new Vector3(0, 3.2f, 0), new Vector3(0.7f, 0.42f, 0.7f), i == 1 ? Pal.Hex("FF9FC6") : Pal.Hex("7BE0B5"), false, 1, 0, lp);
                shade.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var bulb = P("Bulb", PrimitiveType.Sphere, new Vector3(0, 3.02f, 0), Vector3.one * 0.2f, Pal.Hex("FFF3C4"), false, 0, 2.2f, lp);
                bulb.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                // тёплое пятно света на полу под лампой
                var glow = Look.FxMat(new Color(1f, 0.85f, 0.55f, 0.18f), true, true);
                Look.FxObject("FloorGlow", lp, Look.Quad(new Vector3(-1.6f, 0.02f, -1.6f), new Vector3(1.6f, 0.02f, -1.6f), new Vector3(-1.6f, 0.02f, 1.6f), new Vector3(1.6f, 0.02f, 1.6f)), glow);
                if (!Look.ToonAvailable)
                {
                    var pl = new GameObject("LampLight").AddComponent<Light>();
                    pl.type = LightType.Point; pl.range = 9; pl.intensity = 1.1f; pl.color = Pal.Hex("FFE2B0");
                    pl.transform.position = new Vector3(-6 + i * 6, 2.9f, 0.3f);
                }
            }
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = Pal.Hex("FFF0D2"); sun.intensity = 1.25f; sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.transform.rotation = Quaternion.Euler(52, 155, 0);
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Pal.Hex("D6ECFF");
            RenderSettings.ambientEquatorColor = Pal.Hex("FFE3C8");
            RenderSettings.ambientGroundColor = Pal.Hex("B89470");
            RenderSettings.fog = false;

            // Солнечные лучи из окон — наклонные светящиеся полосы по направлению солнца
            var beamMat = Look.FxMat(new Color(1f, 0.93f, 0.7f, 0.11f), false, true);
            if (beamMat != null)
            {
                Vector3 dir = sun.transform.forward;
                foreach (var x in new[] { -9f, -4f, 1f })
                    foreach (var yTop in new[] { 3.1f, 2.2f })
                    {
                        float len = (yTop - 0.02f) / Mathf.Max(0.2f, -dir.y);
                        Vector3 tl = new Vector3(x - 1.3f, yTop, 7.75f), tr = new Vector3(x + 1.3f, yTop, 7.75f);
                        Vector3 bl = tl + dir * len, br = tr + dir * len;
                        Look.FxObject("SunBeam", root, Look.Quad(bl, br, tl, tr), beamMat);
                    }
            }
            Look.Dust(new Vector3(0, 2f, 1.5f), new Vector3(22f, 3.4f, 12f));

            // Шкафчик с зеркалом — здесь меняют внешность
            var locker = Look.Node("Locker", root, new Vector3(-8f, 0, -7.6f));
            var body = B("LockerBody", new Vector3(0, 1.05f, 0), new Vector3(1.4f, 2.1f, 0.5f), Pal.Hex("7C83FD"), 0.06f, true, 1, 0, true, locker);
            B("DoorL", new Vector3(-0.35f, 1.05f, 0.26f), new Vector3(0.64f, 1.95f, 0.03f), Pal.Hex("8E95FF"), 0.03f, false, 0.6f, 0, true, locker);
            B("Mirror", new Vector3(0.35f, 1.3f, 0.26f), new Vector3(0.55f, 1.2f, 0.02f), Pal.Hex("DDF3FF"), 0.04f, false, 0.6f, 0.35f, false, locker);
            B("MirrorShine", new Vector3(0.25f, 1.5f, 0.275f), new Vector3(0.06f, 0.5f, 0.005f), Color.white, 0.01f, false, 0, 0.6f, false, locker).transform.localRotation = Quaternion.Euler(0, 0, -20);
            B("Handle", new Vector3(-0.08f, 1.05f, 0.29f), new Vector3(0.03f, 0.2f, 0.03f), Pal.Hex("FFD23F"), 0.01f, false, 1, 0, true, locker);
            P("Hat", PrimitiveType.Sphere, new Vector3(-0.3f, 2.2f, 0), new Vector3(0.4f, 0.22f, 0.38f), Pal.Hex("FF4F9A"), false, 1, 0, locker);
            body.AddComponent<Wardrobe>();
            var lockTag = Label("Гардероб", new Vector3(0, 2.6f, 0.2f), 0.016f, Pal.Ink, locker);
            lockTag.gameObject.AddComponent<Billboard>();
            refs.lockerSpot = Look.Node("LockerSpot", root, new Vector3(-8f, 0.1f, -6.1f));

            refs.spawn = new GameObject("Spawn").transform;
            refs.spawn.position = new Vector3(-5, 0.1f, -4.8f);
            refs.spawn.rotation = Quaternion.identity;
            refs.playerDesk = playerDesk;
            return refs;
        }

        // Стена с нижней панелью и плинтусом. inward — направление внутрь комнаты.
        static void Wall(string name, Vector3 basePos, Vector3 size, Color top, Color low, Vector3 inward)
        {
            B(name, basePos + Vector3.up * 2, size, top, 0, true, 0, 0, false);
            Vector3 thin = new Vector3(inward.x != 0 ? 0.04f : size.x, 1.1f, inward.z != 0 ? 0.04f : size.z);
            B(name + "Low", basePos + Vector3.up * 0.55f + inward * 0.12f, thin, low, 0, false, 0, 0, false);
            Vector3 skirt = new Vector3(inward.x != 0 ? 0.06f : size.x, 0.14f, inward.z != 0 ? 0.06f : size.z);
            B(name + "Skirt", basePos + Vector3.up * 0.07f + inward * 0.14f, skirt, Pal.Hex("FFF9F0"), 0, false, 0, 0, false);
            Vector3 rail = new Vector3(inward.x != 0 ? 0.06f : size.x, 0.06f, inward.z != 0 ? 0.06f : size.z);
            B(name + "Rail", basePos + Vector3.up * 1.12f + inward * 0.14f, rail, Pal.Hex("FFF9F0"), 0, false, 0, 0, false);
        }

        static void Poster(Vector3 pos, float yaw, string text, Color c)
        {
            var t = new GameObject("Poster").transform; t.SetParent(root, false);
            t.localPosition = pos; t.localRotation = Quaternion.Euler(0, yaw, 0);
            Look.RBox("Frame", t, Vector3.zero, new Vector3(1.5f, 1.0f, 0.04f), Pal.Hex("FFF9F0"), 0.04f, false, 1, 0, false);
            Look.RBox("Paper", t, new Vector3(0, 0, -0.025f), new Vector3(1.36f, 0.86f, 0.01f), c, 0.03f, false, 0, 0, false);
            Label(text, new Vector3(0, 0, -0.04f), 0.014f, Pal.Ink, t);
        }
    }

    // ------------------ Интерактивные объекты ------------------
    public abstract class Interactable : MonoBehaviour
    {
        public abstract string Prompt { get; }
        public abstract void Interact(GameRoot g);
    }

    public class ComputerDesk : Interactable
    {
        public override string Prompt { get { return "[E] Сесть за компьютер"; } }
        public override void Interact(GameRoot g) { g.OpenIde(); }
    }

    public class TeamLeadNpc : Interactable
    {
        public override string Prompt { get { return "[E] Поговорить с тимлидом"; } }
        public override void Interact(GameRoot g)
        {
            var a = GetComponent<CharacterAnim>(); if (a != null) a.Wave();
            g.TalkToLead();
        }
    }

    public class Wardrobe : Interactable
    {
        public override string Prompt { get { return "[E] Переодеться"; } }
        public override void Interact(GameRoot g) { g.OpenWardrobe(false); }
    }

    public class CoffeeMachine : Interactable
    {
        public override string Prompt { get { return "[E] Кофе и апгрейды"; } }
        public override void Interact(GameRoot g) { g.OpenShop(); }
    }

    public class BugCritter : Interactable
    {
        public override string Prompt { get { return "[ЛКМ или E] Поймать баг!"; } }
        public override void Interact(GameRoot g) { Catch(g); }

        Vector3 target; float speed, hopT, dying = -1;
        readonly List<Transform> legs = new List<Transform>();
        Transform bodyT;
        static readonly Rect Bounds = new Rect(-10.5f, -6.8f, 21, 13.6f);
        static readonly string[] Colors = { "9BE15D", "B28DFF", "FF9F43", "5CD6FF" };

        public static BugCritter Spawn(Vector3 pos)
        {
            var go = new GameObject("Bug");
            go.transform.position = pos;
            var b = go.AddComponent<BugCritter>();
            var t = go.transform;
            var c = Pal.Hex(Colors[Random.Range(0, Colors.Length)]);

            b.bodyT = new GameObject("BodyPivot").transform; b.bodyT.SetParent(t, false);
            Look.Prim("Body", b.bodyT, PrimitiveType.Sphere, new Vector3(0, 0.16f, -0.02f), new Vector3(0.36f, 0.24f, 0.44f), c);
            Look.Prim("Shell", b.bodyT, PrimitiveType.Sphere, new Vector3(0, 0.22f, -0.06f), new Vector3(0.32f, 0.16f, 0.34f), Color.Lerp(c, Pal.Ink, 0.25f));
            Look.Prim("Spot", b.bodyT, PrimitiveType.Sphere, new Vector3(0.07f, 0.29f, -0.05f), new Vector3(0.08f, 0.03f, 0.08f), Color.Lerp(c, Color.white, 0.6f), false, 0);
            Look.Prim("Head", b.bodyT, PrimitiveType.Sphere, new Vector3(0, 0.2f, 0.2f), new Vector3(0.26f, 0.22f, 0.22f), Color.Lerp(c, Color.white, 0.15f));
            foreach (var sx in new[] { -1f, 1f })
            {
                var eye = Look.Prim("Eye", b.bodyT, PrimitiveType.Sphere, new Vector3(sx * 0.065f, 0.26f, 0.29f), new Vector3(0.1f, 0.11f, 0.06f), Color.white, false, 0.6f);
                Look.Prim("Pupil", eye.transform, PrimitiveType.Sphere, new Vector3(0, -0.05f, 0.4f), new Vector3(0.55f, 0.6f, 0.4f), Pal.Ink, false, 0);
                var ant = Look.Prim("Antenna", b.bodyT, PrimitiveType.Cylinder, new Vector3(sx * 0.07f, 0.36f, 0.22f), new Vector3(0.015f, 0.08f, 0.015f), Pal.Ink, false, 0);
                ant.transform.localRotation = Quaternion.Euler(-25, 0, sx * -25);
                Look.Prim("Tip", b.bodyT, PrimitiveType.Sphere, new Vector3(sx * 0.105f, 0.43f, 0.26f), Vector3.one * 0.05f, Pal.Pink, false, 0);
            }
            for (int i = 0; i < 3; i++) foreach (var sx in new[] { -1f, 1f })
                {
                    var pivot = new GameObject("LegPivot").transform; pivot.SetParent(t, false);
                    pivot.localPosition = new Vector3(sx * 0.14f, 0.1f, -0.12f + i * 0.12f);
                    var leg = Look.RBox("Leg", pivot, new Vector3(sx * 0.07f, -0.04f, 0), new Vector3(0.14f, 0.03f, 0.03f), Pal.Ink, 0.012f, false, 0);
                    b.legs.Add(pivot);
                }
            var col = go.AddComponent<SphereCollider>(); col.radius = 0.35f; col.center = new Vector3(0, 0.15f, 0); col.isTrigger = true;
            b.speed = Random.Range(1.2f, 2.2f);
            b.NewTarget();
            return b;
        }

        void NewTarget() { target = new Vector3(Random.Range(Bounds.xMin, Bounds.xMax), 0, Random.Range(Bounds.yMin, Bounds.yMax)); }

        void Update()
        {
            if (dying >= 0)
            {
                dying += Time.deltaTime;
                float k = dying / 0.35f;
                transform.localScale = new Vector3(1 + k, Mathf.Max(0.05f, 1 - k), 1 + k);
                if (k >= 1) Destroy(gameObject);
                return;
            }
            var to = target - transform.position; to.y = 0;
            if (to.magnitude < 0.3f) NewTarget();
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), Time.deltaTime * 6);
            transform.position += transform.forward * speed * Time.deltaTime;
            hopT += Time.deltaTime * speed * 9;
            var p = transform.position; p.y = 0; transform.position = p;
            bodyT.localPosition = Vector3.up * Mathf.Abs(Mathf.Sin(hopT)) * 0.04f;
            bodyT.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(hopT) * 5f);
            for (int i = 0; i < legs.Count; i++)
                legs[i].localRotation = Quaternion.Euler(0, Mathf.Sin(hopT + i * 1.3f) * 25f, 0);
        }

        public void Catch(GameRoot g)
        {
            if (dying >= 0) return;
            dying = 0;
            g.OnBugCaught();
        }
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var c = Camera.main; if (c == null) return;
            transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position);
        }
    }

    public class Bobber : MonoBehaviour
    {
        public float amplitude = 0.06f;
        Vector3 start;
        void Start() { start = transform.localPosition; }
        void Update() { transform.localPosition = start + Vector3.up * Mathf.Sin(Time.time * 2.2f) * amplitude; }
    }
}
