// Стили интерфейса. Мультяшная палитра в духе low-poly офиса.
using UnityEngine;

namespace Intern.Game
{
    public static class Pal
    {
        public static readonly Color Ink = Hex("1B1F4A");
        public static readonly Color Panel = Hex("22275A");
        public static readonly Color PanelLight = Hex("2E3470");
        public static readonly Color Editor = Hex("15183A");
        public static readonly Color Sun = Hex("FFD23F");
        public static readonly Color Pink = Hex("FF4F9A");
        public static readonly Color Sky = Hex("9FD8FF");
        public static readonly Color Mint = Hex("7BE0B5");
        public static readonly Color Text = Hex("ECEAFF");
        public static readonly Color Muted = Hex("9AA0D6");

        public static Color Hex(string h)
        {
            Color c; ColorUtility.TryParseHtmlString("#" + h, out c); return c;
        }
    }

    public class UiKit
    {
        public GUIStyle panel, panelLight, editorBg, consoleBg, h1, h2, h3, body, small, code, codeRich, console,
                        btn, btnAlt, btnDanger, btnGhost, lineNo, tag, toast, center, swatch;
        public Texture2D glare;
        public Font mono;
        Texture2D white;

        public static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c); t.Apply(); return t;
        }

        public Texture2D White { get { return white; } }

        // Скруглённый прямоугольник со сглаживанием; lipH — тёмная «кромка» снизу (объёмная кнопка)
        public static Texture2D RoundTex(Color c, int r, Color lip, int lipH)
        {
            int w = r * 2 + 4, h = r * 2 + 4 + lipH;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, w - r), cy = Mathf.Clamp(y + 0.5f, r, h - r);
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    var col = y < lipH ? lip : c;
                    col.a *= a;
                    px[y * w + x] = col;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static GUIStyle Rounded(Color c, int r, int pad)
        {
            var s = new GUIStyle { padding = new RectOffset(pad, pad, pad, pad), border = new RectOffset(r + 2, r + 2, r + 2, r + 2) };
            s.normal.background = RoundTex(c, r, c, 0);
            return s;
        }

        static void AllStates(GUIStyle s, Texture2D bg, Color text)
        {
            s.normal.background = bg; s.hover.background = bg; s.active.background = bg; s.focused.background = bg;
            s.onNormal.background = bg; s.onHover.background = bg; s.onActive.background = bg; s.onFocused.background = bg;
            s.normal.textColor = text; s.hover.textColor = text; s.active.textColor = text; s.focused.textColor = text;
            s.onNormal.textColor = text; s.onHover.textColor = text; s.onActive.textColor = text; s.onFocused.textColor = text;
        }

        static GUIStyle Label(int size, Color c, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, wordWrap = true, richText = true };
            s.normal.textColor = c;
            return s;
        }

        static GUIStyle Button(Color bg, Color hover, Color press, Color text)
        {
            const int r = 10, lip = 4;
            var s = new GUIStyle(GUI.skin.button) { fontSize = 15, fontStyle = FontStyle.Bold, padding = new RectOffset(16, 16, 8, 8 + lip), richText = true, alignment = TextAnchor.MiddleCenter };
            s.border = new RectOffset(r + 2, r + 2, r + 2, r + 2 + lip);
            Color edge = Color.Lerp(bg, new Color(0.05f, 0.05f, 0.15f), 0.35f);
            s.normal.background = RoundTex(bg, r, edge, lip);
            s.hover.background = RoundTex(hover, r, edge, lip);
            s.active.background = RoundTex(press, r, press, lip);
            s.focused.background = s.normal.background;
            s.normal.textColor = text; s.hover.textColor = text; s.active.textColor = text; s.focused.textColor = text;
            return s;
        }

        public UiKit()
        {
            white = Tex(Color.white);
            mono = Font.CreateDynamicFontFromOSFont(PickMono(), 16);

            panel = Rounded(Pal.Panel, 14, 14);
            panelLight = Rounded(Pal.PanelLight, 14, 14);
            editorBg = Rounded(Pal.Editor, 12, 0);
            consoleBg = Rounded(Pal.Hex("0E1030"), 12, 0);

            h1 = Label(44, Pal.Sun, FontStyle.Bold);
            h2 = Label(20, Pal.Text, FontStyle.Bold);
            h3 = Label(16, Pal.Sun, FontStyle.Bold);
            body = Label(15, Pal.Text);
            small = Label(13, Pal.Muted);
            center = Label(15, Pal.Text); center.alignment = TextAnchor.MiddleCenter;
            tag = Label(12, Pal.Ink, FontStyle.Bold); tag.normal.background = RoundTex(Pal.Sky, 6, Pal.Sky, 0); tag.border = new RectOffset(8, 8, 8, 8); tag.padding = new RectOffset(7, 7, 2, 2); tag.wordWrap = false;

            code = new GUIStyle(GUI.skin.textArea) { font = mono, fontSize = 16, wordWrap = false, richText = false, padding = new RectOffset(8, 8, 8, 8) };
            AllStates(code, null, Pal.Text);
            code.normal.background = null;
            codeRich = new GUIStyle(code) { richText = true };
            lineNo = new GUIStyle(GUI.skin.label) { font = mono, fontSize = 16, alignment = TextAnchor.UpperRight, padding = new RectOffset(0, 6, 0, 0) };
            lineNo.normal.textColor = Pal.Muted;
            console = new GUIStyle(GUI.skin.label) { font = mono, fontSize = 14, wordWrap = true, richText = true, padding = new RectOffset(10, 10, 8, 8) };
            console.normal.textColor = Pal.Hex("CFE8FF");

            btn = Button(Pal.Sun, Pal.Hex("FFE27A"), Pal.Hex("F2B705"), Pal.Ink);
            btnAlt = Button(Pal.Hex("3A4090"), Pal.Hex("4A52B0"), Pal.Hex("2E347A"), Pal.Text);
            btnDanger = Button(Pal.Pink, Pal.Hex("FF77B0"), Pal.Hex("E03C84"), Pal.Text);
            btnGhost = Button(Pal.Panel, Pal.PanelLight, Pal.Editor, Pal.Sky); btnGhost.fontStyle = FontStyle.Normal;

            swatch = new GUIStyle { border = new RectOffset(12, 12, 12, 12) };
            swatch.normal.background = RoundTex(Color.white, 10, Color.white, 0);
            swatch.hover.background = RoundTex(new Color(1, 1, 1, 0.85f), 10, Color.white, 0);
            swatch.active.background = swatch.normal.background;
            glare = GlareTex();

            toast = Label(17, Pal.Ink, FontStyle.Bold); toast.alignment = TextAnchor.MiddleCenter; toast.wordWrap = false;   // однострочные: иначе при масштабе GUI текст переносится и обрезается
            toast.normal.background = RoundTex(Pal.Sun, 14, Pal.Hex("E0A800"), 5); toast.border = new RectOffset(16, 16, 16, 21); toast.padding = new RectOffset(20, 20, 10, 15);
        }

        // Первый моноширинный шрифт, который реально есть в системе (Mac — Menlo, Windows — Consolas)
        static string PickMono()
        {
            var installed = new System.Collections.Generic.HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var n in new[] { "Menlo", "SF Mono", "Monaco", "Consolas", "Cascadia Mono", "Courier New", "DejaVu Sans Mono" })
                if (installed.Contains(n)) return n;
            return "Courier New";
        }

        // Безопасно показать произвольный текст в rich text
        public static string Esc(string s)
        {
            return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<\u200B");
        }

        public void Fill(Rect r, Color c)
        {
            var old = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * old.a); GUI.DrawTexture(r, white); GUI.color = old;
        }

        // Блик на «стекле» монитора: светлая диагональная полоса
        static Texture2D GlareTex()
        {
            const int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = (x + (n - y)) / (2f * n);           // 0 — левый верхний угол
                    float band = Mathf.Clamp01(1 - Mathf.Abs(d - 0.28f) / 0.1f);
                    float corner = Mathf.Clamp01(1 - d * 1.6f) * 0.5f;
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Max(band, corner));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
