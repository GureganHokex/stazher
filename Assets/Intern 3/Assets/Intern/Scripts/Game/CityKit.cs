// Спринт 4 версии 0.9 «Красивый город»: детали, из которых собирается улица.
//  • BoxBatch — тысячи мелких коробок (рамы, подоконники, карнизы, витрины) складываются в одну сетку на цвет:
//    сотни объектов вместо десятков тысяч, одна отрисовка на материал;
//  • текстуры земли (асфальт, плитка, брусчатка) рисуются кодом;
//  • вывески-коробы с рамкой и подсветкой, деревья, фонари, остановка;
//  • фонтан: чаша с водой, по которой бежит рябь, струи, перелив из верхней чаши, брызги и шум воды;
//  • дымка вдали, пока открыт город.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public class BoxBatch
    {
        // Сетка на группу «обводка + свечение»: цвет каждой коробки — в её вершинах (шейдер Intern/Toon, _VColor).
        // Без мультяшного шейдера — сетка на цвет, как раньше
        class Buf { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>(); public readonly List<Color32> col = new List<Color32>(); public readonly List<int> t = new List<int>(); public Color c; public float outline, emission; }
        readonly Dictionary<string, Buf> bufs = new Dictionary<string, Buf>();
        static readonly Vector3[] Dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        const int MaxVerts = 60000;   // сетки делятся на куски: 16-битные индексы и отсечение по частям города

        public int Count { get; private set; }

        // Цвет вершины идёт в шейдер как есть, а цвет материала Unity переводит в линейное пространство — переводим сами
        static Color32 VCol(Color c) { return QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c; }

        Buf Get(Color c, float outline, float emission, Vector3 at)
        {
            bool vcol = Look.ToonAvailable;
            // кусок города 48 × 48 м — чтобы невидимое за спиной отсекалось
            string cell = Mathf.FloorToInt(at.x / 48f) + ":" + Mathf.FloorToInt(at.z / 48f);
            string key = (vcol ? "v" : ColorUtility.ToHtmlStringRGBA(c)) + "|" + outline + "|" + emission + "|" + cell;
            Buf b;
            if (!bufs.TryGetValue(key, out b) || b.v.Count > MaxVerts)
            {
                if (b != null) { int k = 1; while (bufs.ContainsKey(key + "#" + k)) k++; bufs[key + "#" + k] = b; }
                b = new Buf { c = c, outline = outline, emission = emission };
                bufs[key] = b;
            }
            return b;
        }

        // Коробка: центр и размеры в координатах родителя, поворот — вокруг центра
        public void Box(Vector3 center, Vector3 size, Quaternion rot, Color c, float emission = 0f, float outline = 0f)
        {
            var b = Get(c, outline, emission, center);
            Color32 c32 = VCol(c);
            var h = size * 0.5f;
            foreach (var d in Dirs)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                float hd = Mathf.Abs(Vector3.Dot(d, h)), hu = Mathf.Abs(Vector3.Dot(u, h)), hw = Mathf.Abs(Vector3.Dot(w, h));
                int s = b.v.Count;
                var nn = rot * d;
                b.v.Add(center + rot * (d * hd - u * hu - w * hw)); b.v.Add(center + rot * (d * hd + u * hu - w * hw));
                b.v.Add(center + rot * (d * hd + u * hu + w * hw)); b.v.Add(center + rot * (d * hd - u * hu + w * hw));
                for (int k = 0; k < 4; k++) { b.n.Add(nn); b.col.Add(c32); }
                // обход по часовой, если смотреть с нормали (лицевая сторона в Unity)
                b.t.Add(s); b.t.Add(s + 1); b.t.Add(s + 2); b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 3);
            }
            Count++;
        }

        // Колесо, бочка, столбик: цилиндр вдоль оси axis (в координатах родителя), seg граней
        public void Cylinder(Vector3 center, Vector3 axis, float radius, float length, Color c, int seg = 10, float outline = 0f)
        {
            var b = Get(c, outline, 0f, center);
            Color32 c32 = VCol(c);
            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized, w = Vector3.Cross(axis, u);
            Vector3 a0 = center - axis * length * 0.5f, a1 = center + axis * length * 0.5f;
            for (int i = 0; i < seg; i++)
            {
                float t0 = i * Mathf.PI * 2f / seg, t1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 r0 = (u * Mathf.Cos(t0) + w * Mathf.Sin(t0)), r1 = (u * Mathf.Cos(t1) + w * Mathf.Sin(t1));
                // бок
                int s = b.v.Count;
                b.v.Add(a0 + r0 * radius); b.v.Add(a1 + r0 * radius); b.v.Add(a1 + r1 * radius); b.v.Add(a0 + r1 * radius);
                b.n.Add(r0); b.n.Add(r0); b.n.Add(r1); b.n.Add(r1);
                for (int k = 0; k < 4; k++) b.col.Add(c32);
                AddQuad(b, s, (r0 + r1) * 0.5f);
                // торцы
                foreach (var end in new[] { -1, 1 })
                {
                    var a = end < 0 ? a0 : a1; var nn = axis * end;
                    s = b.v.Count;
                    b.v.Add(a); b.v.Add(a + r0 * radius); b.v.Add(a + r1 * radius);
                    for (int k = 0; k < 3; k++) { b.n.Add(nn); b.col.Add(c32); }
                    // лицевая сторона в Unity: Cross(b − a, c − a) смотрит наружу (по часовой, если глядеть с нормали)
                    var tn = Vector3.Cross(b.v[s + 1] - b.v[s], b.v[s + 2] - b.v[s]);
                    if (Vector3.Dot(tn, nn) > 0f) { b.t.Add(s); b.t.Add(s + 1); b.t.Add(s + 2); } else { b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1); }
                }
            }
            Count++;
        }

        // четырёхугольник s..s+3: лицевая сторона — по нормали want
        static void AddQuad(Buf b, int s, Vector3 want)
        {
            var tn = Vector3.Cross(b.v[s + 1] - b.v[s], b.v[s + 2] - b.v[s]);
            if (Vector3.Dot(tn, want) > 0f) { b.t.Add(s); b.t.Add(s + 1); b.t.Add(s + 2); b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 3); }
            else { b.t.Add(s); b.t.Add(s + 2); b.t.Add(s + 1); b.t.Add(s); b.t.Add(s + 3); b.t.Add(s + 2); }
        }

        // Плоское стекло (витрины): прямоугольник w × h в плоскости XY поворота rot; все стёкла куска города — одна сетка
        class GlassBuf { public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); }
        readonly Dictionary<string, GlassBuf> glass = new Dictionary<string, GlassBuf>();
        public void Glass(Vector3 center, float w, float h, Quaternion rot)
        {
            string cell = Mathf.FloorToInt(center.x / 48f) + ":" + Mathf.FloorToInt(center.z / 48f);
            GlassBuf g; if (!glass.TryGetValue(cell, out g)) { g = new GlassBuf(); glass[cell] = g; }
            int s = g.v.Count; float hw = w / 2f, hh = h / 2f;
            g.v.Add(center + rot * new Vector3(-hw, -hh, 0)); g.v.Add(center + rot * new Vector3(hw, -hh, 0));
            g.v.Add(center + rot * new Vector3(-hw, hh, 0)); g.v.Add(center + rot * new Vector3(hw, hh, 0));
            g.uv.Add(new Vector2(0, 0)); g.uv.Add(new Vector2(1, 0)); g.uv.Add(new Vector2(0, 1)); g.uv.Add(new Vector2(1, 1));
            g.t.Add(s); g.t.Add(s + 2); g.t.Add(s + 1); g.t.Add(s + 1); g.t.Add(s + 2); g.t.Add(s + 3);
        }

        public void Flush(Transform parent, string name, bool shadows = true)
        {
            var gm = CityKit.FlatGlass();
            if (gm != null)
                foreach (var g in glass.Values)
                {
                    var m = new Mesh { name = name + "_glass" };
                    m.SetVertices(g.v); m.SetUVs(0, g.uv); m.SetTriangles(g.t, 0);
                    var cols = new List<Color>(); for (int i = 0; i < g.v.Count; i++) cols.Add(Color.white); m.SetColors(cols);
                    m.RecalculateNormals(); m.RecalculateBounds();
                    Look.FxObject(name + "Glass", parent, m, gm);
                }
            glass.Clear();
            foreach (var b in bufs.Values)
            {
                if (b.v.Count == 0) continue;
                var m = new Mesh { name = name + "_mesh" };
                if (b.v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(b.v); m.SetNormals(b.n); m.SetColors(b.col); m.SetTriangles(b.t, 0);
                m.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = Look.VertexColorMat(b.outline, b.emission) ?? Look.Mat(b.c, b.outline, b.emission);
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            bufs.Clear();
        }
    }

    public static class CityKit
    {
        // ---------- текстуры земли ----------
        static readonly Dictionary<string, Material> texMats = new Dictionary<string, Material>();

        static Material TexMat(string key, int n, System.Func<int, int, Color> px, float smooth = 0.15f)
        {
            Material m;
            if (texMats.TryGetValue(key, out m) && m != null) return m;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4, name = "City_" + key };
            var cols = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) cols[y * n + x] = px(x, y);
            tex.SetPixels(cols); tex.Apply(true);
            var sh = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
            m = new Material(sh) { name = "City_" + key, mainTexture = tex, color = Color.white };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            texMats[key] = m;
            return m;
        }

        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; } }

        // асфальт: тёмно-серый с зерном; одна текстура на 4 м
        public static Material Asphalt()
        {
            return TexMat("asphalt", 128, (x, y) =>
            {
                float g = 0.30f + (Hash(x, y) - 0.5f) * 0.07f + (Hash(x / 4, y / 4) - 0.5f) * 0.04f;
                return new Color(g * 0.95f, g * 0.96f, g * 1.06f);
            }, 0.25f);
        }

        // тротуарная плитка 40 × 40 см со швами; одна текстура — 4 × 4 плитки (1,6 м)
        public static Material Sidewalk()
        {
            return TexMat("sidewalk", 128, (x, y) =>
            {
                int tx = x / 32, ty = y / 32; bool seam = x % 32 < 2 || y % 32 < 2;
                float g = 0.70f + (Hash(tx, ty) - 0.5f) * 0.06f + (Hash(x, y) - 0.5f) * 0.03f;
                if (seam) g -= 0.16f;
                return new Color(g * 0.98f, g * 0.97f, g * 1.02f);
            });
        }

        // брусчатка площади: тёплый кирпич «ёлочкой» со сдвигом рядов; текстура — 2 м
        public static Material Paving()
        {
            return TexMat("paving", 128, (x, y) =>
            {
                int row = y / 16; int off = (row % 2) * 16; int bx = (x + off) / 32;
                bool seam = y % 16 < 2 || (x + off) % 32 < 2;
                float k = Hash(bx, row);
                var c = Color.Lerp(Pal.Hex("D3C6AE"), Pal.Hex("BFAE92"), k) * (0.97f + (Hash(x, y) - 0.5f) * 0.05f);
                if (k > 0.86f) c = Color.Lerp(c, Pal.Hex("B98F74"), 0.55f);   // редкие терракотовые камни
                if (seam) c = Color.Lerp(c, Pal.Hex("8A8072"), 0.6f);
                c.a = 1f; return c;
            });
        }

        // булыжник переулков: скруглённые камни разного оттенка; текстура — 1,2 м
        public static Material Cobble(string tint)
        {
            var baseC = Pal.Hex(tint);
            return TexMat("cobble" + tint, 128, (x, y) =>
            {
                int cx = x / 16, cy = y / 16;
                float fx = (x % 16) / 16f - 0.5f, fy = (y % 16) / 16f - 0.5f;
                float d = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fy));
                float k = Hash(cx, cy);
                var c = baseC * (0.88f + k * 0.2f);
                c = Color.Lerp(c, c * 1.12f, 0.5f - fx - fy);   // светлее к верхнему левому краю камня
                if (d > 0.42f) c = Color.Lerp(baseC * 0.6f, c, (0.5f - d) / 0.08f);
                c.a = 1f; return c;
            });
        }

        // Горизонтальная плоскость с повтором текстуры каждые tile метров (центр по x, z, размер)
        public static GameObject Ground(Transform parent, string name, Vector3 center, float w, float len, Material mat, float tile)
        {
            var m = new Mesh { name = name };
            float hx = w / 2f, hz = len / 2f;
            m.vertices = new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(w / tile, 0), new Vector2(0, len / tile), new Vector2(w / tile, len / tile) };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            m.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.localPosition = center;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        // ---------- вывеска-короб ----------
        // Ширина текста в метрах для надписи OfficeBuilder.Label того же размера
        public static float TextWidth(string text, float size, bool bold)
        {
            var font = OfficeBuilder.DefaultFont; if (font == null) return text.Length * size * 3.4f;
            var style = bold ? FontStyle.Bold : FontStyle.Normal;
            font.RequestCharactersInTexture(text, 128, style);
            float px = 0f;
            foreach (var ch in text) { CharacterInfo ci; if (font.GetCharacterInfo(ch, out ci, 128, style)) px += ci.advance; else px += 64f; }
            return px * size * 0.5f * 0.1f;   // Label: characterSize = size / 2, мир ≈ пиксели × characterSize / 10
        }

        // Короб над входом: подложка цвета бренда, светлая рамка, надпись и полоска подсветки снизу.
        // pos — центр лицевой стороны на стене, along — вдоль стены, n — наружу
        public static void SignBox(BoxBatch bb, Transform parent, string text, Vector3 pos, Vector3 along, Vector3 n, Color board, Color ink, float size, float maxWidth)
        {
            float tw = TextWidth(text, size, true);
            if (tw > maxWidth - 0.5f) { size *= (maxWidth - 0.5f) / tw; tw = maxWidth - 0.5f; }
            float th = size * 64f * 0.1f;      // высота строки
            float w = tw + 0.6f, h = Mathf.Max(0.55f, th * 1.25f + 0.25f);
            var rot = Quaternion.LookRotation(n);
            bb.Box(pos + n * 0.09f, new Vector3(w, h, 0.18f), rot, board, 0f, 0.6f);
            var frame = Color.Lerp(board, Color.white, 0.55f);
            bb.Box(pos + n * 0.19f + Vector3.up * (h / 2f - 0.03f), new Vector3(w, 0.06f, 0.03f), rot, frame);
            bb.Box(pos + n * 0.19f - Vector3.up * (h / 2f - 0.03f), new Vector3(w, 0.06f, 0.03f), rot, frame);
            bb.Box(pos + n * 0.19f + along * (w / 2f - 0.03f), new Vector3(0.06f, h, 0.03f), rot, frame);
            bb.Box(pos + n * 0.19f - along * (w / 2f - 0.03f), new Vector3(0.06f, h, 0.03f), rot, frame);
            bb.Box(pos + n * 0.14f - Vector3.up * (h / 2f + 0.05f), new Vector3(w * 0.9f, 0.05f, 0.08f), rot, Pal.Hex("FFF3C4"), 2.2f);   // подсветка
            var t = OfficeBuilder.Label(text, pos + n * 0.215f, size, ink, parent, Quaternion.LookRotation(n).eulerAngles.y + 180f);
            t.fontStyle = FontStyle.Bold;
        }

        // ---------- дерево: ствол, несколько шапок листвы, решётка у корней ----------
        static readonly string[] Greens = { "5FBF4A", "4CAF50", "6CC24A", "3E9B5A", "7FCB5B" };
        public static void Tree(Transform parent, BoxBatch bb, Vector3 p, System.Random rnd, float scale = 1f)
        {
            Look.Prim("Trunk", parent, PrimitiveType.Cylinder, p + Vector3.up * 1.3f * scale, new Vector3(0.28f, 1.3f, 0.28f) * scale, Pal.Hex("6B4226"), true, 0.6f);
            Look.Prim("Branch", parent, PrimitiveType.Cylinder, p + new Vector3(0.35f, 2.4f, 0.1f) * scale, new Vector3(0.12f, 0.5f, 0.12f) * scale, Pal.Hex("6B4226"), false, 0.4f).transform.localRotation = Quaternion.Euler(0, 0, -35f);
            int k = 5 + rnd.Next(3);
            for (int i = 0; i < k; i++)
            {
                float a = i * Mathf.PI * 2f / k + (float)rnd.NextDouble();
                float r = i == 0 ? 0f : 0.9f + (float)rnd.NextDouble() * 0.4f;
                var c = new Vector3(Mathf.Cos(a) * r, (i == 0 ? 3.9f : 3.1f + (float)rnd.NextDouble() * 0.8f), Mathf.Sin(a) * r) * scale;
                float s = (i == 0 ? 2.2f : 1.5f + (float)rnd.NextDouble() * 0.5f) * scale;
                Look.Prim("Leaves", parent, PrimitiveType.Sphere, p + c, new Vector3(s, s * 0.85f, s), Pal.Hex(Greens[rnd.Next(Greens.Length)]), false, 0.5f);
            }
            var rot = Quaternion.identity;
            bb.Box(p + Vector3.up * 0.02f, new Vector3(1.4f, 0.04f, 1.4f), rot, Pal.Hex("3A3C4E"));
            bb.Box(p + Vector3.up * 0.025f, new Vector3(1.0f, 0.04f, 1.0f), rot, Pal.Hex("5A4636"));
        }

        // круглая клумба с цветами вокруг дерева или сама по себе
        public static void Flowerbed(Transform parent, BoxBatch bb, Vector3 p, float r, System.Random rnd)
        {
            int seg = 14;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                bb.Box(p + dir * r + Vector3.up * 0.22f, new Vector3(2f * Mathf.PI * r / seg * 1.06f, 0.44f, 0.25f), Quaternion.LookRotation(dir), Pal.Hex("B8B0A0"), 0f, 0.4f);
            }
            Look.Prim("Soil", parent, PrimitiveType.Cylinder, p + Vector3.up * 0.3f, new Vector3(r * 2f - 0.2f, 0.12f, r * 2f - 0.2f), Pal.Hex("5A4636"), false, 0f);
            string[] fl = { "FF4F9A", "FFD23F", "F4F1EA", "B28DFF", "FF8A5C" };
            for (int i = 0; i < 26; i++)
            {
                float a = (float)rnd.NextDouble() * Mathf.PI * 2f, rr = (0.35f + (float)rnd.NextDouble() * 0.6f) * (r - 0.3f);
                var q = p + new Vector3(Mathf.Cos(a) * rr, 0.48f, Mathf.Sin(a) * rr);
                bb.Box(q + Vector3.down * 0.06f, new Vector3(0.05f, 0.14f, 0.05f), Quaternion.identity, Pal.Hex("3E9B5A"));
                bb.Box(q + Vector3.up * 0.03f, new Vector3(0.14f, 0.09f, 0.14f), Quaternion.Euler(0, a * 57f, 0), Pal.Hex(fl[rnd.Next(fl.Length)]), 0.15f);
            }
            for (int i = 0; i < 5; i++)
            {
                float a = i * 1.26f; var q = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r * 0.55f);
                Look.Prim("Bush", parent, PrimitiveType.Sphere, q + Vector3.up * 0.55f, new Vector3(0.55f, 0.45f, 0.55f), Pal.Hex(Greens[i % Greens.Length]), false, 0.4f);
            }
        }

        // ---------- фонарь: основание, столб, кронштейн, плафон ----------
        public static void Lamp(BoxBatch bb, Vector3 p, Vector3 armDir)
        {
            var dark = Pal.Hex("2B2D42"); var id = Quaternion.identity;
            bb.Box(p + Vector3.up * 0.25f, new Vector3(0.34f, 0.5f, 0.34f), id, dark, 0f, 0.4f);
            bb.Box(p + Vector3.up * 2.2f, new Vector3(0.13f, 3.9f, 0.13f), id, dark, 0f, 0.4f);
            bb.Box(p + Vector3.up * 4.12f + armDir * 0.35f, new Vector3(Mathf.Abs(armDir.x) > 0.5f ? 0.8f : 0.08f, 0.08f, Mathf.Abs(armDir.z) > 0.5f ? 0.8f : 0.08f), id, dark, 0f, 0.4f);
            var head = p + Vector3.up * 3.95f + armDir * 0.7f;
            bb.Box(head + Vector3.up * 0.14f, new Vector3(0.42f, 0.08f, 0.42f), id, dark, 0f, 0.4f);
            bb.Box(head, new Vector3(0.3f, 0.22f, 0.3f), id, Pal.Hex("FFE7A8"), 1.4f);
        }

        // ---------- остановка: крыша, стеклянная стенка, лавка, табличка «А» ----------
        public static void BusStop(Transform parent, BoxBatch bb, Vector3 p, Vector3 along, Vector3 n)
        {
            var rot = Quaternion.LookRotation(n);
            var frame = Pal.Hex("3A3C4E");
            bb.Box(p + Vector3.up * 2.55f + n * 0.35f, new Vector3(3.4f, 0.12f, 1.6f), rot, Pal.Hex("E8782F"), 0f, 0.5f);
            foreach (int s in new[] { -1, 1 }) bb.Box(p + along * (s * 1.6f) + Vector3.up * 1.25f - n * 0.3f, new Vector3(0.1f, 2.5f, 0.1f), rot, frame, 0f, 0.4f);
            bb.Box(p + Vector3.up * 0.45f - n * 0.1f, new Vector3(2.4f, 0.08f, 0.45f), rot, Pal.Hex("8C6A4F"), 0f, 0.4f);
            foreach (int s in new[] { -1, 1 }) bb.Box(p + along * (s * 1.0f) + Vector3.up * 0.22f - n * 0.1f, new Vector3(0.08f, 0.44f, 0.4f), rot, frame);
            if (FlatGlass() != null)
            {
                var g = Look.FxObject("StopGlass", parent, Look.Quad(new Vector3(-1.6f, 0.3f, 0), new Vector3(1.6f, 0.3f, 0), new Vector3(-1.6f, 2.45f, 0), new Vector3(1.6f, 2.45f, 0)), FlatGlass());
                g.transform.localPosition = p - n * 0.32f; g.transform.localRotation = rot;
            }
            // табличка на столбе
            var pole = p + along * 2.2f + n * 0.4f;
            bb.Box(pole + Vector3.up * 1.4f, new Vector3(0.08f, 2.8f, 0.08f), rot, frame, 0f, 0.4f);
            bb.Box(pole + Vector3.up * 2.75f, new Vector3(0.55f, 0.55f, 0.05f), rot, Pal.Hex("3A7BD5"), 0.2f, 0.4f);
            var t = OfficeBuilder.Label("А", pole + Vector3.up * 2.75f + n * 0.03f, 0.06f, Color.white, parent, rot.eulerAngles.y + 180f);
            t.fontStyle = FontStyle.Bold;
            var t2 = OfficeBuilder.Label("А", pole + Vector3.up * 2.75f - n * 0.03f, 0.06f, Color.white, parent, rot.eulerAngles.y);
            t2.fontStyle = FontStyle.Bold;
        }

        // прозрачное ровное стекло (шейдер эффектов, режим «плоский»)
        static Material flatGlass;
        public static Material FlatGlass()
        {
            if (flatGlass != null) return flatGlass;
            var m = Look.FxMat(new Color(0.78f, 0.9f, 1f, 0.22f), false, false);
            if (m == null) return null;
            flatGlass = new Material(m) { name = "City_FlatGlass" };
            flatGlass.SetFloat("_Radial", 2f);   // 2 — равномерная прозрачность
            return flatGlass;
        }

        // ---------- фонтан ----------
        public static void Fountain(Transform parent, BoxBatch bb, Vector3 p)
        {
            var stone = Pal.Hex("C9C1B0"); var stoneD = Pal.Hex("A8A090"); var cap = Pal.Hex("E4DDCC");
            const int seg = 24; const float R = 3.1f;
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var rot = Quaternion.LookRotation(dir);
                float len = 2f * Mathf.PI * R / seg * 1.08f;
                bb.Box(p + dir * R + Vector3.up * 0.28f, new Vector3(len, 0.56f, 0.45f), rot, stone, 0f, 0.5f);
                bb.Box(p + dir * R + Vector3.up * 0.6f, new Vector3(len * 1.03f, 0.1f, 0.62f), rot, cap, 0f, 0.4f);
            }
            // коллайдер чаши, чтобы сквозь фонтан не ходили
            var col = new GameObject("FountainCollider"); col.transform.SetParent(parent, false); col.transform.localPosition = p + Vector3.up * 0.4f;
            var cc = col.AddComponent<CapsuleCollider>(); cc.radius = R + 0.3f; cc.height = 0.8f; cc.direction = 1;
            Look.Prim("FountainFloor", parent, PrimitiveType.Cylinder, p + Vector3.up * 0.06f, new Vector3(R * 2f - 0.3f, 0.06f, R * 2f - 0.3f), Pal.Hex("2F6F86"), false, 0f);
            var water = WaterSurface.Create(parent, p + Vector3.up * 0.44f, R - 0.2f, 16, 48, 0.7f);
            // пьедестал и верхняя чаша
            Look.Prim("FountainColumn", parent, PrimitiveType.Cylinder, p + Vector3.up * 0.95f, new Vector3(0.7f, 0.95f, 0.7f), stoneD, false, 0.5f);
            Look.Prim("FountainColumnRing", parent, PrimitiveType.Cylinder, p + Vector3.up * 0.55f, new Vector3(1.0f, 0.1f, 1.0f), cap, false, 0.4f);
            Look.Prim("FountainBowl", parent, PrimitiveType.Cylinder, p + Vector3.up * 1.82f, new Vector3(2.5f, 0.1f, 2.5f), stone, false, 0.5f);
            Look.Prim("FountainBowlUnder", parent, PrimitiveType.Sphere, p + Vector3.up * 1.72f, new Vector3(2.3f, 0.4f, 2.3f), stoneD, false, 0.4f);
            const int seg2 = 16; const float R2 = 1.22f;
            for (int i = 0; i < seg2; i++)
            {
                float a = i * Mathf.PI * 2f / seg2; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                bb.Box(p + dir * R2 + Vector3.up * 1.98f, new Vector3(2f * Mathf.PI * R2 / seg2 * 1.1f, 0.22f, 0.14f), Quaternion.LookRotation(dir), cap, 0f, 0.4f);
            }
            WaterSurface.Create(parent, p + Vector3.up * 2.0f, R2 - 0.1f, 8, 32, 0.45f);
            Look.Prim("FountainSpout", parent, PrimitiveType.Cylinder, p + Vector3.up * 2.25f, new Vector3(0.16f, 0.25f, 0.16f), cap, false, 0.4f);
            Look.Prim("FountainKnob", parent, PrimitiveType.Sphere, p + Vector3.up * 2.52f, new Vector3(0.24f, 0.24f, 0.24f), cap, false, 0.4f);

            var drop = Look.FxMat(new Color(0.82f, 0.94f, 1f, 0.75f), true, false);
            if (drop != null)
            {
                // главная струя вверх
                Jet(parent, "JetMain", p + Vector3.up * 2.6f, Quaternion.Euler(-90f, 0, 0), drop, 170f, 3.3f, 3.9f, 0.07f, 0.12f, 1.0f, 1.2f, 5f);
                // перелив с верхней чаши — завеса капель по кругу
                var cur = Jet(parent, "Overflow", p + Vector3.up * 2.02f, Quaternion.Euler(-90f, 0, 0), drop, 260f, 0.25f, 0.45f, 0.05f, 0.08f, 0.5f, 0.65f, 0f);
                var sh = cur.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = R2 + 0.02f; sh.radiusThickness = 0f;
                var vel = cur.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local; vel.radial = new ParticleSystem.MinMaxCurve(0.35f);
                vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(0f); vel.z = new ParticleSystem.MinMaxCurve(0f);
                // восемь струй от бортика к центру
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var look = Quaternion.LookRotation((-dir + Vector3.up * 1.25f).normalized);
                    Jet(parent, "RimJet", p + dir * (R - 0.15f) + Vector3.up * 0.62f, look, drop, 45f, 3.0f, 3.3f, 0.05f, 0.09f, 0.75f, 0.85f, 2f);
                }
                // брызги там, где вода падает
                var spl = Jet(parent, "Splash", p + Vector3.up * 0.47f, Quaternion.Euler(-90f, 0, 0), drop, 90f, 0.6f, 1.4f, 0.03f, 0.07f, 0.25f, 0.45f, 35f);
                var ss = spl.shape; ss.radius = 1.6f; ss.radiusThickness = 1f;
            }
            FountainSound(parent, p + Vector3.up * 1f);
            if (water != null) water.name = "FountainWater";
        }

        static ParticleSystem Jet(Transform parent, string name, Vector3 pos, Quaternion rot, Material mat, float rate, float v0, float v1, float s0, float s1, float l0, float l1, float angle)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = pos; go.transform.localRotation = rot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = 1f; main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(l0, l1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(v0, v1);
            main.startSize = new ParticleSystem.MinMaxCurve(s0, s1);
            main.gravityModifier = 1f; main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = angle; sh.radius = 0.04f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                       new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.6f;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        // шум воды: мягкий «белый» шум с редкими всплесками, 3 секунды по кругу, слышно в радиусе ~25 м
        static AudioClip waterClip;
        static void FountainSound(Transform parent, Vector3 pos)
        {
            if (waterClip == null)
            {
                const int rate = 22050; int n = rate * 3; var data = new float[n];
                var r = new System.Random(5); float lp = 0f, lp2 = 0f;
                for (int i = 0; i < n; i++)
                {
                    float w = (float)r.NextDouble() * 2f - 1f;
                    lp += (w - lp) * 0.35f; lp2 += (lp - lp2) * 0.2f;
                    float splash = r.NextDouble() < 0.0015 ? 0.6f : 0f;
                    data[i] = (lp - lp2 * 0.6f) * 0.55f + splash * w;
                }
                // стык конца с началом без щелчка
                for (int i = 0; i < 2000; i++) { float k = i / 2000f; data[n - 2000 + i] = Mathf.Lerp(data[n - 2000 + i], data[i], k); }
                waterClip = AudioClip.Create("FountainWater", n, 1, rate, false);
                waterClip.SetData(data, 0);
            }
            var go = new GameObject("FountainSound"); go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = waterClip; src.loop = true; src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f; src.maxDistance = 28f; src.volume = 0.55f; src.dopplerLevel = 0f;
            go.AddComponent<SfxVolume>().baseVolume = 0.55f;
            src.Play();
        }
    }

    // Громкость источника следует за настройкой «Эффекты»
    public class SfxVolume : MonoBehaviour
    {
        public float baseVolume = 1f;
        AudioSource src;
        void Awake() { src = GetComponent<AudioSource>(); }
        void OnEnable() { if (src != null && src.clip != null && !src.isPlaying) src.Play(); }
        void Update() { if (src != null) src.volume = baseVolume * Mathf.Clamp01(GameConfig.S.sfx); }
    }

    // Вода в чаше: круг из колец, вершины качаются — рябь от струй к краю и лёгкое волнение
    public class WaterSurface : MonoBehaviour
    {
        Mesh mesh; Vector3[] v0, v; float radius, strength; Renderer rend; int frame;
        static Material waterMat;

        public static WaterSurface Create(Transform parent, Vector3 localPos, float radius, int rings, int segs, float strength)
        {
            var go = new GameObject("Water"); go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
            var w = go.AddComponent<WaterSurface>(); w.radius = radius; w.strength = strength;
            var verts = new List<Vector3> { Vector3.zero }; var tris = new List<int>();
            for (int r = 1; r <= rings; r++)
            {
                float rr = radius * r / rings;
                for (int s = 0; s < segs; s++) { float a = s * Mathf.PI * 2f / segs; verts.Add(new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr)); }
            }
            for (int s = 0; s < segs; s++) { tris.Add(0); tris.Add(1 + (s + 1) % segs); tris.Add(1 + s); }
            for (int r = 1; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = 1 + (r - 1) * segs + s, b = 1 + (r - 1) * segs + (s + 1) % segs, c = 1 + r * segs + s, d = 1 + r * segs + (s + 1) % segs;
                    tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
                }
            w.mesh = new Mesh { name = "Water" }; w.mesh.MarkDynamic();
            w.mesh.SetVertices(verts); w.mesh.SetTriangles(tris, 0); w.mesh.RecalculateNormals(); w.mesh.RecalculateBounds();
            w.v0 = verts.ToArray(); w.v = verts.ToArray();
            go.AddComponent<MeshFilter>().sharedMesh = w.mesh;
            w.rend = go.AddComponent<MeshRenderer>(); w.rend.sharedMaterial = Mat(); w.rend.shadowCastingMode = ShadowCastingMode.Off;
            return w;
        }

        static Material Mat()
        {
            if (waterMat != null) return waterMat;
            var sh = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.defaultShader : Shader.Find("Standard");
            waterMat = new Material(sh) { name = "City_Water", color = Pal.Hex("4FA3C9") };
            if (waterMat.HasProperty("_BaseColor")) waterMat.SetColor("_BaseColor", Pal.Hex("4FA3C9"));
            if (waterMat.HasProperty("_Smoothness")) waterMat.SetFloat("_Smoothness", 0.93f);
            return waterMat;
        }

        void Update()
        {
            if (mesh == null || rend == null || !rend.isVisible) return;
            if ((++frame & 1) == 1) return;   // раз в два кадра хватает глазу
            float t = Time.time, amp = 0.02f * strength;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v0[i]; float r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                float y = amp * Mathf.Sin(r * 8.5f - t * 5.2f) * Mathf.Exp(-r * 0.35f)
                        + amp * 0.5f * Mathf.Sin(p.x * 2.7f + t * 1.9f) + amp * 0.4f * Mathf.Sin(p.z * 3.3f - t * 1.4f);
                if (r > radius * 0.97f) y *= 0.3f;
                v[i].y = y;
            }
            mesh.vertices = v;
            mesh.RecalculateNormals();
        }
    }

    // Дымка вдали, пока открыт город (офис живёт без неё)
    public class CityAtmosphere : MonoBehaviour
    {
        void OnEnable()
        {
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Pal.Hex("C9D6E8"); RenderSettings.fogStartDistance = 38f; RenderSettings.fogEndDistance = 175f;
        }
        void OnDisable() { RenderSettings.fog = false; }
    }
}
