// Общие детали новой IDE на UI Toolkit: палитра в духе VS Code, шрифты, кнопки, иконки, прокрутка.
// Ввод UI Toolkit не использует: события мыши и клавиатуры приходят из OnGUI (см. IdeScreen.HandleEvent),
// поэтому всё работает одинаково и со старым Input Manager, и с Input System.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TextCore.Text;

namespace Intern.Game
{
    public static class K
    {
        static Color C(string h) { return Pal.Hex(h); }
        // Палитра Dark Modern (VS Code)
        public static readonly Color TitleBg = C("1F1F1F"), ActivityBg = C("181818"), SideBg = C("181818"), EditorBg = C("1F1F1F"),
            TabBg = C("181818"), TabActive = C("1F1F1F"), Border = C("2B2B2B"), Text = C("CCCCCC"), TextHi = C("E7E7E7"), Muted = C("9D9D9D"),
            Dim = C("6E7681"), Accent = C("0078D4"), Status = C("007ACC"), StatusDebug = C("CC6633"), Selection = C("264F78"),
            CurLine = C("262626"), Hover = C("2A2D2E"), Press = C("37373D"), Button = C("0078D4"), ButtonHover = C("026EC1"),
            Button2 = C("313131"), Button2Hover = C("3C3C3C"), Red = C("F14C4C"), Yellow = C("CCA700"), Green = C("89D185"),
            Orange = C("E8A33D"), Blue = C("3794FF"), Purple = C("C586C0"), Input = C("313131"), Widget = C("202020"),
            Brand = C("FF4F9A"), BrandHover = C("FF6BAD"), Sun = C("FFD23F"), DebugLine = C("4B4B18"), Breakpoint = C("E51400");

        public static string Hex(Color c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        // --------- шрифты: системные, через TextCore ---------
        static FontAsset mono, sans;
        public static FontAsset Mono { get { if (mono == null) mono = Load(new[] { "Consolas", "Cascadia Mono", "Cascadia Code", "JetBrains Mono", "Menlo", "SF Mono", "DejaVu Sans Mono", "Liberation Mono", "Courier New" }); return mono; } }
        public static FontAsset Sans { get { if (sans == null) sans = Load(new[] { "Segoe UI", "SF Pro Text", "Helvetica Neue", "Arial", "DejaVu Sans", "Liberation Sans" }); return sans; } }

        static FontAsset bold, black; static bool boldTried, blackTried;
        // Жирные начертания для меню и заголовков (если в системе нет — будет обычный шрифт с программным «жирным»)
        public static FontAsset SansBold { get { if (!boldTried) { boldTried = true; bold = LoadStyle(new[] { "Segoe UI", "SF Pro Text", "Helvetica Neue", "Arial", "DejaVu Sans" }, new[] { "Bold", "Semibold" }); } return bold; } }
        public static FontAsset SansBlack { get { if (!blackTried) { blackTried = true; black = LoadStyle(new[] { "Segoe UI", "Arial", "Helvetica Neue", "DejaVu Sans" }, new[] { "Black", "Heavy", "Bold" }); } return black; } }

        static FontAsset Load(string[] families)
        {
            foreach (var f in families)
            {
                FontAsset fa = null;
                try { fa = FontAsset.CreateFontAsset(f, "Regular"); } catch { }
                if (fa != null) { fa.name = f; return fa; }
            }
            try { return FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")); } catch { return null; }
        }

        static FontAsset LoadStyle(string[] families, string[] styles)
        {
            foreach (var st in styles)
                foreach (var f in families)
                {
                    FontAsset fa = null;
                    try { fa = FontAsset.CreateFontAsset(f, st); } catch { }
                    if (fa != null) { fa.name = f + " " + st; return fa; }
                }
            return null;
        }

        // Метка жирным начертанием (настоящим, если найдено, иначе программным)
        public static Label B(string text, float size, Color color, bool black = false)
        {
            var l = T(text, size, color);
            var fa = black ? (SansBlack ?? SansBold) : SansBold;
            if (fa != null) l.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(fa));
            else l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        public static void Font(VisualElement v, bool isMono)
        {
            var fa = isMono ? Mono : Sans;
            if (fa != null) v.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(fa));
        }

        // Ширина символа моноширинного шрифта в пикселях (по метрике глифа)
        public static float MonoAdvance(float fontSize)
        {
            var fa = Mono;
            if (fa == null) return fontSize * 0.6f;
            try
            {
                fa.TryAddCharacters("M");
                Character ch;
                if (fa.characterLookupTable.TryGetValue('M', out ch) && fa.faceInfo.pointSize > 0)
                    return ch.glyph.metrics.horizontalAdvance * fontSize / fa.faceInfo.pointSize;
            }
            catch { }
            return fontSize * 0.6f;
        }

        // Текст для rich text: «<» не должен превращаться в тег
        public static string Esc(string s) { return string.IsNullOrEmpty(s) ? "" : s.Replace("<", "<noparse><</noparse>"); }
        public static string Col(string s, Color c) { return "<color=" + Hex(c) + ">" + s + "</color>"; }

        // --------- построение ---------
        public static VisualElement Box(bool row = false, string name = null)
        {
            var v = new VisualElement { name = name };
            v.style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column;
            v.pickingMode = PickingMode.Position;
            return v;
        }

        public static Label T(string text, float size = 14, Color? color = null, bool mono = false, bool bold = false, bool wrap = false)
        {
            var l = new Label(text) { enableRichText = true };
            Font(l, mono);
            l.style.fontSize = size;
            l.style.color = color ?? Text;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            l.style.marginLeft = l.style.marginRight = l.style.marginTop = l.style.marginBottom = 0f;
            l.style.paddingLeft = l.style.paddingRight = l.style.paddingTop = l.style.paddingBottom = 0f;
            l.style.unityTextAlign = TextAnchor.MiddleLeft;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static void Pad(VisualElement v, float t, float r, float b, float l) { v.style.paddingTop = t; v.style.paddingRight = r; v.style.paddingBottom = b; v.style.paddingLeft = l; }
        public static void Pad(VisualElement v, float a) { Pad(v, a, a, a, a); }
        public static void Mar(VisualElement v, float t, float r, float b, float l) { v.style.marginTop = t; v.style.marginRight = r; v.style.marginBottom = b; v.style.marginLeft = l; }
        public static void Size(VisualElement v, float w, float h) { if (w >= 0) v.style.width = w; if (h >= 0) v.style.height = h; }
        public static void Radius(VisualElement v, float r) { v.style.borderTopLeftRadius = v.style.borderTopRightRadius = v.style.borderBottomLeftRadius = v.style.borderBottomRightRadius = r; }
        public static void Abs(VisualElement v, float l, float t) { v.style.position = Position.Absolute; v.style.left = l; v.style.top = t; }
        public static void Fill(VisualElement v) { v.style.position = Position.Absolute; v.style.left = v.style.top = v.style.right = v.style.bottom = 0f; }
        public static void Grow(VisualElement v) { v.style.flexGrow = 1f; v.style.flexShrink = 1f; }
        public static void Line(VisualElement v, Color c, float top, float right, float bottom, float left)
        {
            v.style.borderTopColor = v.style.borderRightColor = v.style.borderBottomColor = v.style.borderLeftColor = c;
            v.style.borderTopWidth = top; v.style.borderRightWidth = right; v.style.borderBottomWidth = bottom; v.style.borderLeftWidth = left;
        }

        public static VisualElement Spacer() { var v = Box(); Grow(v); v.pickingMode = PickingMode.Ignore; return v; }
    }

    // Кнопка/кликабельный элемент: нажатие и подсветку при наведении обрабатывает IdeScreen
    public class Btn : VisualElement
    {
        public Action Click;
        public Color Normal = Color.clear, Hovered = K.Hover;
        public string Tip;
        bool enabled = true, hover;
        // Панель IDE рисуется в текстуру, где прозрачность элемента не видна, — выключенную кнопку приглушаем цветом
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value; SetHover(hover);
                foreach (var l in this.Query<Label>().ToList()) l.style.opacity = value ? 1f : 0.5f;
                foreach (var ic in this.Query<Icon>().ToList()) ic.style.opacity = value ? 1f : 0.5f;
            }
        }
        public Btn(Action click, bool row = true) { Click = click; style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column; style.alignItems = Align.Center; pickingMode = PickingMode.Position; }
        public void SetColors(Color normal, Color hovered) { Normal = normal; Hovered = hovered; SetHover(hover); }
        public void SetHover(bool h)
        {
            hover = h;
            style.backgroundColor = !enabled ? (Normal.a > 0.01f ? Color.Lerp(Normal, K.EditorBg, 0.6f) : Normal) : h ? Hovered : Normal;
        }

        public static Btn Text(string text, Action click, Color bg, Color bgHover, float size = 14, Color? fg = null, string icon = null, Color? iconColor = null)
        {
            var b = new Btn(click);
            K.Pad(b, 0, 12, 0, 12); b.style.height = 30f; K.Radius(b, 3);
            b.SetColors(bg, bgHover);
            if (icon != null) { var ic = new Icon(icon, iconColor ?? fg ?? Color.white, 16); ic.style.marginRight = 7f; b.Add(ic); }
            b.Add(K.T(text, size, fg ?? Color.white));
            return b;
        }
    }

    // Иконки нарисованы векторно (Painter2D): кодиконов в проекте нет
    public class Icon : VisualElement
    {
        public string Kind; public Color Col;
        public Icon(string kind, Color col, float size = 18)
        {
            Kind = kind; Col = col; style.width = size; style.height = size; style.flexShrink = 0f;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }
        public void Set(string kind, Color col) { Kind = kind; Col = col; MarkDirtyRepaint(); }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; float s = contentRect.width; if (s <= 0) return;
            Func<float, float, Vector2> P = (x, y) => new Vector2(x * s, y * s);
            p.strokeColor = Col; p.fillColor = Col; p.lineWidth = Mathf.Max(1.3f, s * 0.085f); p.lineJoin = LineJoin.Round; p.lineCap = LineCap.Round;
            switch (Kind)
            {
                case "files":   // две страницы
                    Poly(p, false, P(.36f, .1f), P(.72f, .1f), P(.9f, .28f), P(.9f, .78f), P(.36f, .78f)); p.Stroke();
                    Poly(p, false, P(.24f, .26f), P(.1f, .26f), P(.1f, .92f), P(.66f, .92f), P(.66f, .84f)); p.Stroke(); break;
                case "task":    // планшет с листком
                    Poly(p, true, P(.2f, .16f), P(.8f, .16f), P(.8f, .92f), P(.2f, .92f)); p.Stroke();
                    Poly(p, true, P(.38f, .08f), P(.62f, .08f), P(.62f, .24f), P(.38f, .24f)); p.Fill();
                    Seg(p, P(.32f, .44f), P(.68f, .44f)); Seg(p, P(.32f, .6f), P(.68f, .6f)); Seg(p, P(.32f, .76f), P(.56f, .76f)); break;
                case "search":
                    p.BeginPath(); p.Arc(P(.42f, .42f), s * .28f, 0, 360); p.Stroke(); Seg(p, P(.63f, .63f), P(.9f, .9f)); break;
                case "branch":
                    p.BeginPath(); p.Arc(P(.3f, .2f), s * .1f, 0, 360); p.Stroke(); p.BeginPath(); p.Arc(P(.3f, .8f), s * .1f, 0, 360); p.Stroke();
                    p.BeginPath(); p.Arc(P(.72f, .32f), s * .1f, 0, 360); p.Stroke(); Seg(p, P(.3f, .3f), P(.3f, .7f));
                    p.BeginPath(); p.MoveTo(P(.72f, .42f)); p.BezierCurveTo(P(.72f, .6f), P(.3f, .52f), P(.3f, .7f)); p.Stroke(); break;
                case "debug":   // треугольник «запуск» и жучок
                    Poly(p, true, P(.14f, .12f), P(.62f, .42f), P(.14f, .72f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(.7f, .72f), s * .16f, 0, 360); p.Fill();
                    Seg(p, P(.5f, .66f), P(.58f, .7f)); Seg(p, P(.9f, .66f), P(.82f, .7f)); Seg(p, P(.52f, .86f), P(.6f, .8f)); Seg(p, P(.88f, .86f), P(.8f, .8f)); break;
                case "extensions":
                    Poly(p, true, P(.12f, .4f), P(.4f, .4f), P(.4f, .88f), P(.12f, .88f)); p.Stroke();
                    Poly(p, true, P(.4f, .6f), P(.88f, .6f), P(.88f, .88f), P(.4f, .88f)); p.Stroke();
                    Poly(p, true, P(.56f, .12f), P(.88f, .12f), P(.88f, .44f), P(.56f, .44f)); p.Stroke(); break;
                case "account":
                    p.BeginPath(); p.Arc(P(.5f, .36f), s * .18f, 0, 360); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f, 1.02f), s * .38f, 200, 340); p.Stroke(); break;
                case "gear":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .2f, 0, 360); p.Stroke();
                    for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; Seg(p, P(.5f + Mathf.Cos(a) * .3f, .5f + Mathf.Sin(a) * .3f), P(.5f + Mathf.Cos(a) * .4f, .5f + Mathf.Sin(a) * .4f)); } break;
                case "play":
                    Poly(p, true, P(.26f, .14f), P(.84f, .5f), P(.26f, .86f)); p.Fill(); break;
                case "check":
                    p.BeginPath(); p.MoveTo(P(.14f, .52f)); p.LineTo(P(.4f, .78f)); p.LineTo(P(.88f, .24f)); p.Stroke(); break;
                case "reset":
                    p.BeginPath(); p.Arc(P(.5f, .54f), s * .3f, -60, 220); p.Stroke();
                    Poly(p, true, P(.62f, .08f), P(.82f, .3f), P(.54f, .34f)); p.Fill(); break;
                case "close":
                    Seg(p, P(.24f, .24f), P(.76f, .76f)); Seg(p, P(.76f, .24f), P(.24f, .76f)); break;
                case "continue":
                    Seg(p, P(.2f, .16f), P(.2f, .84f)); Poly(p, true, P(.38f, .16f), P(.86f, .5f), P(.38f, .84f)); p.Fill(); break;
                case "stepOver":
                    p.BeginPath(); p.Arc(P(.5f, .6f), s * .32f, 200, 340); p.Stroke();
                    Poly(p, true, P(.9f, .44f), P(.76f, .62f), P(.64f, .38f)); p.Fill();
                    p.BeginPath(); p.Arc(P(.5f, .82f), s * .09f, 0, 360); p.Fill(); break;
                case "stepInto":
                    Seg(p, P(.5f, .1f), P(.5f, .56f)); Poly(p, true, P(.3f, .42f), P(.7f, .42f), P(.5f, .66f)); p.Fill();
                    p.BeginPath(); p.Arc(P(.5f, .84f), s * .09f, 0, 360); p.Fill(); break;
                case "stop":
                    Poly(p, true, P(.22f, .22f), P(.78f, .22f), P(.78f, .78f), P(.22f, .78f)); p.Fill(); break;
                case "py":      // значок Python-файла: жёлто-синие «змейки»
                    p.fillColor = Pal.Hex("3572A5"); Poly(p, true, P(.2f, .12f), P(.62f, .12f), P(.62f, .5f), P(.2f, .5f)); p.Fill();
                    p.fillColor = Pal.Hex("FFD43B"); Poly(p, true, P(.38f, .5f), P(.8f, .5f), P(.8f, .88f), P(.38f, .88f)); p.Fill(); break;
                case "md":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .36f, 0, 360); p.Stroke(); Seg(p, P(.5f, .44f), P(.5f, .72f));
                    p.BeginPath(); p.Arc(P(.5f, .3f), s * .05f, 0, 360); p.Fill(); break;
                case "folder":
                    Poly(p, true, P(.08f, .22f), P(.4f, .22f), P(.5f, .32f), P(.92f, .32f), P(.92f, .82f), P(.08f, .82f)); p.Stroke(); break;
                case "chevR":
                    p.BeginPath(); p.MoveTo(P(.38f, .22f)); p.LineTo(P(.66f, .5f)); p.LineTo(P(.38f, .78f)); p.Stroke(); break;
                case "chevL":
                    p.BeginPath(); p.MoveTo(P(.62f, .22f)); p.LineTo(P(.34f, .5f)); p.LineTo(P(.62f, .78f)); p.Stroke(); break;
                case "chevD":
                    p.BeginPath(); p.MoveTo(P(.22f, .38f)); p.LineTo(P(.5f, .66f)); p.LineTo(P(.78f, .38f)); p.Stroke(); break;
                case "lock":
                    Poly(p, true, P(.22f, .46f), P(.78f, .46f), P(.78f, .88f), P(.22f, .88f)); p.Fill();
                    p.BeginPath(); p.Arc(P(.5f, .46f), s * .2f, 180, 360); p.Stroke(); break;
                case "dot":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .3f, 0, 360); p.Fill(); break;
                case "ring":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .32f, 0, 360); p.Stroke(); break;
                case "error":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .4f, 0, 360); p.Stroke(); Seg(p, P(.36f, .36f), P(.64f, .64f)); Seg(p, P(.64f, .36f), P(.36f, .64f)); break;
                case "warning":
                    Poly(p, true, P(.5f, .1f), P(.92f, .86f), P(.08f, .86f)); p.Stroke(); Seg(p, P(.5f, .38f), P(.5f, .6f));
                    p.BeginPath(); p.Arc(P(.5f, .73f), s * .04f, 0, 360); p.Fill(); break;
                case "clock":   // циферблат со стрелками
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .38f, 0, 360); p.Stroke();
                    Seg(p, P(.5f, .5f), P(.5f, .26f)); Seg(p, P(.5f, .5f), P(.68f, .6f)); break;
                case "coin":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .4f, 0, 360); p.Fill();
                    p.strokeColor = new Color(0, 0, 0, 0.35f); p.BeginPath(); p.Arc(P(.5f, .5f), s * .24f, 0, 360); p.Stroke(); break;
                case "bell":
                    p.BeginPath(); p.MoveTo(P(.22f, .74f)); p.LineTo(P(.28f, .44f)); p.BezierCurveTo(P(.3f, .2f), P(.7f, .2f), P(.72f, .44f)); p.LineTo(P(.78f, .74f)); p.ClosePath(); p.Stroke();
                    Seg(p, P(.42f, .86f), P(.58f, .86f)); break;
                case "kodzilla":  // логотип студии: розовый ромб
                    p.fillColor = K.Brand; Poly(p, true, P(.5f, .06f), P(.94f, .5f), P(.5f, .94f), P(.06f, .5f)); p.Fill();
                    p.fillColor = Color.white; Poly(p, true, P(.5f, .32f), P(.68f, .5f), P(.5f, .68f), P(.32f, .5f)); p.Fill(); break;
                case "split":
                    Poly(p, true, P(.14f, .18f), P(.86f, .18f), P(.86f, .82f), P(.14f, .82f)); p.Stroke(); Seg(p, P(.5f, .18f), P(.5f, .82f)); break;
                case "more":
                    for (int i = 0; i < 3; i++) { p.BeginPath(); p.Arc(P(.22f + i * .28f, .5f), s * .07f, 0, 360); p.Fill(); } break;
                case "min": Seg(p, P(.2f, .5f), P(.8f, .5f)); break;
                // --- для меню игры ---
                case "door":    // дверь и стрелка наружу
                    Poly(p, false, P(.56f, .2f), P(.56f, .1f), P(.14f, .1f), P(.14f, .9f), P(.56f, .9f), P(.56f, .8f)); p.Stroke();
                    Seg(p, P(.4f, .5f), P(.9f, .5f)); Poly(p, false, P(.76f, .34f), P(.92f, .5f), P(.76f, .66f)); p.Stroke(); break;
                case "shirt":
                    Poly(p, true, P(.36f, .12f), P(.1f, .26f), P(.18f, .46f), P(.28f, .42f), P(.28f, .9f), P(.72f, .9f), P(.72f, .42f), P(.82f, .46f), P(.9f, .26f), P(.64f, .12f), P(.5f, .24f)); p.Stroke(); break;
                case "camera":
                    Poly(p, true, P(.08f, .3f), P(.34f, .3f), P(.42f, .18f), P(.62f, .18f), P(.7f, .3f), P(.92f, .3f), P(.92f, .82f), P(.08f, .82f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(.5f, .56f), s * .16f, 0, 360); p.Stroke(); break;
                case "home":
                    Poly(p, false, P(.1f, .5f), P(.5f, .12f), P(.9f, .5f)); p.Stroke();
                    Poly(p, false, P(.22f, .4f), P(.22f, .88f), P(.78f, .88f), P(.78f, .4f)); p.Stroke(); Poly(p, false, P(.42f, .88f), P(.42f, .64f), P(.58f, .64f), P(.58f, .88f)); p.Stroke(); break;
                case "plus":
                    Seg(p, P(.5f, .16f), P(.5f, .84f)); Seg(p, P(.16f, .5f), P(.84f, .5f)); break;
                case "monitor":
                    Poly(p, true, P(.08f, .14f), P(.92f, .14f), P(.92f, .7f), P(.08f, .7f)); p.Stroke(); Seg(p, P(.5f, .7f), P(.5f, .86f)); Seg(p, P(.3f, .88f), P(.7f, .88f)); break;
                case "image":   // горы и солнце — «графика»
                    Poly(p, true, P(.08f, .14f), P(.92f, .14f), P(.92f, .86f), P(.08f, .86f)); p.Stroke();
                    Poly(p, false, P(.14f, .8f), P(.4f, .48f), P(.58f, .68f), P(.7f, .56f), P(.88f, .8f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(.68f, .32f), s * .08f, 0, 360); p.Fill(); break;
                case "sound":
                    Poly(p, true, P(.1f, .38f), P(.28f, .38f), P(.5f, .16f), P(.5f, .84f), P(.28f, .62f), P(.1f, .62f)); p.Fill();
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .2f, -50, 50); p.Stroke(); p.BeginPath(); p.Arc(P(.5f, .5f), s * .36f, -50, 50); p.Stroke(); break;
                case "mouse":
                    p.BeginPath(); p.MoveTo(P(.26f, .4f)); p.BezierCurveTo(P(.26f, .06f), P(.74f, .06f), P(.74f, .4f)); p.LineTo(P(.74f, .64f));
                    p.BezierCurveTo(P(.74f, .98f), P(.26f, .98f), P(.26f, .64f)); p.ClosePath(); p.Stroke(); Seg(p, P(.5f, .16f), P(.5f, .36f)); break;
                case "layout":
                    Poly(p, true, P(.1f, .14f), P(.9f, .14f), P(.9f, .86f), P(.1f, .86f)); p.Stroke(); Seg(p, P(.1f, .34f), P(.9f, .34f)); Seg(p, P(.38f, .34f), P(.38f, .86f)); break;
                case "bug":
                    p.BeginPath(); p.Arc(P(.5f, .58f), s * .24f, 0, 360); p.Fill(); p.BeginPath(); p.Arc(P(.5f, .28f), s * .12f, 0, 360); p.Fill();
                    Seg(p, P(.16f, .44f), P(.3f, .5f)); Seg(p, P(.84f, .44f), P(.7f, .5f)); Seg(p, P(.12f, .66f), P(.28f, .64f)); Seg(p, P(.88f, .66f), P(.72f, .64f));
                    Seg(p, P(.18f, .88f), P(.32f, .76f)); Seg(p, P(.82f, .88f), P(.68f, .76f)); break;
                case "keyboard":
                    Poly(p, true, P(.06f, .26f), P(.94f, .26f), P(.94f, .78f), P(.06f, .78f)); p.Stroke(); Seg(p, P(.26f, .64f), P(.74f, .64f));
                    for (int i = 0; i < 5; i++) { p.BeginPath(); p.Arc(P(.18f + i * .16f, .44f), s * .035f, 0, 360); p.Fill(); } break;
                case "max": Poly(p, true, P(.22f, .22f), P(.78f, .22f), P(.78f, .78f), P(.22f, .78f)); p.Stroke(); break;
                // --- направления и файлы ---
                case "server":   // две стойки с огоньками
                    Poly(p, true, P(.12f, .14f), P(.88f, .14f), P(.88f, .44f), P(.12f, .44f)); p.Stroke();
                    Poly(p, true, P(.12f, .56f), P(.88f, .56f), P(.88f, .86f), P(.12f, .86f)); p.Stroke();
                    p.BeginPath(); p.Arc(P(.26f, .29f), s * .05f, 0, 360); p.Fill(); p.BeginPath(); p.Arc(P(.26f, .71f), s * .05f, 0, 360); p.Fill();
                    Seg(p, P(.5f, .29f), P(.76f, .29f)); Seg(p, P(.5f, .71f), P(.76f, .71f)); break;
                case "browser":  // окно браузера с </>
                    Poly(p, true, P(.08f, .14f), P(.92f, .14f), P(.92f, .86f), P(.08f, .86f)); p.Stroke(); Seg(p, P(.08f, .32f), P(.92f, .32f));
                    Poly(p, false, P(.36f, .48f), P(.24f, .6f), P(.36f, .72f)); p.Stroke(); Poly(p, false, P(.64f, .48f), P(.76f, .6f), P(.64f, .72f)); p.Stroke();
                    Seg(p, P(.55f, .46f), P(.45f, .74f)); break;
                case "cloud":
                    p.BeginPath(); p.MoveTo(P(.26f, .76f)); p.BezierCurveTo(P(.04f, .76f), P(.04f, .46f), P(.26f, .48f));
                    p.BezierCurveTo(P(.28f, .22f), P(.62f, .18f), P(.68f, .4f)); p.BezierCurveTo(P(.96f, .38f), P(.98f, .76f), P(.74f, .76f)); p.ClosePath(); p.Stroke(); break;
                case "stack":    // три слоя
                    Poly(p, true, P(.5f, .12f), P(.9f, .32f), P(.5f, .52f), P(.1f, .32f)); p.Stroke();
                    Poly(p, false, P(.1f, .5f), P(.5f, .7f), P(.9f, .5f)); p.Stroke();
                    Poly(p, false, P(.1f, .68f), P(.5f, .88f), P(.9f, .68f)); p.Stroke(); break;
                case "file":     // лист с загнутым углом
                    Poly(p, true, P(.2f, .08f), P(.6f, .08f), P(.82f, .3f), P(.82f, .92f), P(.2f, .92f)); p.Stroke(); Poly(p, false, P(.6f, .08f), P(.6f, .3f), P(.82f, .3f)); p.Stroke(); break;
                case "radioOn":
                    p.BeginPath(); p.Arc(P(.5f, .5f), s * .38f, 0, 360); p.Stroke(); p.BeginPath(); p.Arc(P(.5f, .5f), s * .2f, 0, 360); p.Fill(); break;
                case "boxOn":    // отмеченный флажок
                    Poly(p, true, P(.12f, .12f), P(.88f, .12f), P(.88f, .88f), P(.12f, .88f)); p.Fill();
                    p.strokeColor = K.EditorBg; p.BeginPath(); p.MoveTo(P(.26f, .52f)); p.LineTo(P(.43f, .69f)); p.LineTo(P(.76f, .32f)); p.Stroke(); break;
                case "box":
                    Poly(p, true, P(.12f, .12f), P(.88f, .12f), P(.88f, .88f), P(.12f, .88f)); p.Stroke(); break;
                case "star":
                    {
                        p.BeginPath();
                        for (int i = 0; i < 10; i++) { float a = -Mathf.PI / 2 + i * Mathf.PI / 5, r = i % 2 == 0 ? .44f : .19f; var q = P(.5f + Mathf.Cos(a) * r, .54f + Mathf.Sin(a) * r); if (i == 0) p.MoveTo(q); else p.LineTo(q); }
                        p.ClosePath(); p.Fill(); break;
                    }
                case "fire":     // инцидент
                    p.BeginPath(); p.MoveTo(P(.5f, .08f)); p.BezierCurveTo(P(.62f, .3f), P(.84f, .42f), P(.8f, .66f)); p.BezierCurveTo(P(.76f, .9f), P(.24f, .92f), P(.2f, .66f));
                    p.BezierCurveTo(P(.18f, .48f), P(.34f, .4f), P(.34f, .26f)); p.BezierCurveTo(P(.44f, .34f), P(.46f, .2f), P(.5f, .08f)); p.ClosePath(); p.Fill(); break;
            }
        }
        static void Poly(Painter2D p, bool close, params Vector2[] pts)
        {
            p.BeginPath(); p.MoveTo(pts[0]); for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]); if (close) p.ClosePath();
        }
        static void Seg(Painter2D p, Vector2 a, Vector2 b) { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }
    }

    // Простая прокрутка: содержимое сдвигается внутри окна, колесо мыши передаёт IdeScreen
    public class ScrollBox : VisualElement
    {
        public readonly VisualElement Content;
        readonly VisualElement thumb;
        public float Scroll;
        public bool StickToBottom;
        bool wasAtBottom = true;
        public ScrollBox()
        {
            style.overflow = Overflow.Hidden; style.flexGrow = 1f; style.flexShrink = 1f; pickingMode = PickingMode.Position;
            Content = K.Box(); Content.style.position = Position.Absolute; Content.style.left = 0f; Content.style.right = 10f; Content.style.top = 0f;
            Content.pickingMode = PickingMode.Ignore;
            Add(Content);
            thumb = K.Box(); thumb.style.position = Position.Absolute; thumb.style.right = 1f; thumb.style.width = 8f; thumb.style.backgroundColor = new Color(1, 1, 1, 0.14f);
            thumb.pickingMode = PickingMode.Ignore; Add(thumb);
            RegisterCallback<GeometryChangedEvent>(e => Apply());
            Content.RegisterCallback<GeometryChangedEvent>(e => { if (StickToBottom && wasAtBottom) Scroll = float.MaxValue; Apply(); });
        }
        public float Max { get { return Mathf.Max(0, Content.layout.height - layout.height); } }
        public void Wheel(float d) { Scroll += d; Apply(); }
        public void ToBottom() { Scroll = float.MaxValue; wasAtBottom = true; Apply(); }
        public void ToTop() { Scroll = 0; Apply(); }
        public void Apply()
        {
            float max = Max; if (float.IsNaN(max)) return;
            Scroll = Mathf.Clamp(Scroll, 0, max);
            wasAtBottom = Scroll >= max - 2;
            Content.style.top = -Scroll;
            float h = layout.height, ch = Content.layout.height;
            if (ch > h + 1 && h > 0)
            {
                thumb.style.display = DisplayStyle.Flex;
                float th = Mathf.Max(24, h * h / ch);
                thumb.style.height = th; thumb.style.top = (h - th) * (max > 0 ? Scroll / max : 0);
            }
            else thumb.style.display = DisplayStyle.None;
        }
    }
}
