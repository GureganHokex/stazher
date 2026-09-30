// Спринт 2 версии 0.9 «Плашки вместо надписей»:
//  • плашка сверху экрана — подходишь к доске, Гене, столу, кофемашине, двери или арсеналу, и сверху появляется карточка
//    в стиле подсказки «E Доска задач спринта»: что это, что здесь можно сделать и сводка (раньше — парящий 3D-текст);
//  • облачка реплик Гены и горожан — элементы интерфейса у головы персонажа, чётким шрифтом игры.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public partial class GameUi
    {
        const float PlaqueTop = 22f, GoalTop = 118f;
        VisualElement plaqueRow, plaqueCard, plaqueIconWrap, plaqueItems;
        Icon plaqueIcon;
        Label plaqueTitle, plaqueSub;
        string plaqueKey, plaqueTitleShown;
        float plaqueNext;
        bool plaqueShown;

        // ======================= плашка сверху =======================
        VisualElement BuildPlaque()
        {
            plaqueRow = K.Box(true); plaqueRow.pickingMode = PickingMode.Ignore; plaqueRow.style.position = Position.Absolute;
            plaqueRow.style.left = 0f; plaqueRow.style.right = 0f; plaqueRow.style.top = PlaqueTop; plaqueRow.style.justifyContent = Justify.Center;
            plaqueCard = K.Box(); plaqueCard.pickingMode = PickingMode.Ignore; plaqueCard.style.maxWidth = 900f;
            plaqueCard.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.94f); K.Radius(plaqueCard, 22f);
            Border(plaqueCard, new Color(Sun.r, Sun.g, Sun.b, 0.55f), 2f); K.Pad(plaqueCard, 12f, 22f, 14f, 14f);
            var head = K.Box(true); head.pickingMode = PickingMode.Ignore; head.style.alignItems = Align.Center;
            plaqueIconWrap = K.Box(); plaqueIconWrap.pickingMode = PickingMode.Ignore; plaqueIconWrap.style.width = 42f; plaqueIconWrap.style.height = 42f; plaqueIconWrap.style.flexShrink = 0f;
            K.Radius(plaqueIconWrap, 21f); plaqueIconWrap.style.backgroundColor = new Color(Sun.r, Sun.g, Sun.b, 0.16f);
            plaqueIconWrap.style.alignItems = Align.Center; plaqueIconWrap.style.justifyContent = Justify.Center;
            plaqueIcon = new Icon("task", Sun, 22f); plaqueIconWrap.Add(plaqueIcon); head.Add(plaqueIconWrap);
            var col = K.Box(); col.pickingMode = PickingMode.Ignore; col.style.marginLeft = 12f; col.style.flexShrink = 1f;
            plaqueTitle = K.B("", 20f, Text); col.Add(plaqueTitle);
            plaqueSub = K.T("", 14f, Muted, false, false, true); plaqueSub.style.marginTop = 1f; col.Add(plaqueSub);
            head.Add(col);
            plaqueCard.Add(head);
            plaqueItems = K.Box(true); plaqueItems.pickingMode = PickingMode.Ignore; plaqueItems.style.flexWrap = Wrap.Wrap; plaqueItems.style.marginTop = 10f; plaqueItems.style.marginLeft = 54f;
            plaqueCard.Add(plaqueItems);
            plaqueRow.Add(plaqueCard);
            plaqueRow.style.display = DisplayStyle.None;
            return plaqueRow;
        }

        // prompt — текущая подсказка действия: сменилась точка — плашку пересобираем сразу, иначе раз в 0,25 с
        void UpdatePlaque(string prompt)
        {
            if (prompt == null) { plaqueRow.style.display = DisplayStyle.None; plaqueShown = false; plaqueKey = null; plaqueTitleShown = null; return; }
            if (plaqueShown && Time.unscaledTime < plaqueNext) return;
            plaqueNext = Time.unscaledTime + 0.25f;
            var p = g.FocusPlaque;
            if (p == null) { plaqueRow.style.display = DisplayStyle.None; plaqueShown = false; plaqueKey = null; plaqueTitleShown = null; return; }
            plaqueRow.style.display = DisplayStyle.Flex; plaqueShown = true;
            string key = p.Key;
            if (key == plaqueKey) return;
            plaqueKey = key;
            plaqueIcon.Set(p.icon ?? "task", Sun);
            plaqueTitle.text = K.Esc(p.title);
            plaqueSub.text = K.Esc(p.sub ?? "");
            plaqueSub.style.display = string.IsNullOrEmpty(p.sub) ? DisplayStyle.None : DisplayStyle.Flex;
            plaqueItems.Clear();
            foreach (var it in p.items) plaqueItems.Add(PlaqueChip(it[0], it[1]));
            plaqueItems.style.display = p.items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (p.title != plaqueTitleShown) { plaqueTitleShown = p.title; Pop(plaqueCard); }
        }

        VisualElement PlaqueChip(string label, string value)
        {
            var c = K.Box(true); c.pickingMode = PickingMode.Ignore; c.style.alignItems = Align.Center; c.style.backgroundColor = Well;
            K.Radius(c, 10f); K.Pad(c, 5f, 11f, 6f, 11f); c.style.marginRight = 8f; c.style.marginBottom = 6f; c.style.maxWidth = 520f;
            var l = K.T(K.Esc(label), 13f, Muted); c.Add(l);
            var v = K.B(K.Esc(value), 15f, Text); v.style.marginLeft = 7f; v.style.flexShrink = 1f;
            v.style.overflow = Overflow.Hidden; v.style.textOverflow = TextOverflow.Ellipsis;
            c.Add(v);
            return c;
        }

        // цель обучения встаёт под плашку, пока она видна
        float GoalRowTop()
        {
            if (!plaqueShown || plaqueRow.style.display == DisplayStyle.None) return GoalTop;
            float h = plaqueCard.layout.height;
            if (float.IsNaN(h) || h < 10f) h = 90f;
            return Mathf.Max(GoalTop, PlaqueTop + h + 12f);
        }

        // ======================= имена горожан над головой =======================
        const float TagMaxDist = 24f;
        const int TagMax = 14;
        VisualElement tagLayer;
        class TagView { public VisualElement root, dot; public Label text; public int version = -1; }
        readonly Dictionary<NameTag, TagView> tagViews = new Dictionary<NameTag, TagView>();
        readonly List<NameTag> tagList = new List<NameTag>();
        readonly List<NameTag> tagGone = new List<NameTag>();

        TagView MakeTag()
        {
            var v = new TagView();
            v.root = K.Box(true); v.root.pickingMode = PickingMode.Ignore; v.root.style.position = Position.Absolute; v.root.style.alignItems = Align.Center;
            v.root.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
            v.root.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            v.root.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.82f); K.Radius(v.root, 11f); K.Pad(v.root, 3f, 10f, 4f, 8f);
            v.dot = K.Box(); v.dot.pickingMode = PickingMode.Ignore; v.dot.style.width = 8f; v.dot.style.height = 8f; K.Radius(v.dot, 4f); v.root.Add(v.dot);
            v.text = K.B("", 13f, Text); v.text.style.marginLeft = 6f; v.root.Add(v.text);
            tagLayer.Add(v.root);
            return v;
        }

        void UpdateTags(bool on)
        {
            var cam = Camera.main;
            tagList.Clear();
            if (on && cam != null && root.panel != null)
            {
                var cp = cam.transform.position;
                foreach (var t in NameTag.Active)
                {
                    if (t == null || string.IsNullOrEmpty(t.Text)) continue;
                    var a = t.Anchor;
                    if ((a - cp).sqrMagnitude > TagMaxDist * TagMaxDist) continue;
                    if (cam.WorldToViewportPoint(a).z < 0.5f) continue;
                    bool talking = false;
                    foreach (var b in SpeechBubble.Active) if (b != null && b.Owner == t.transform.parent) { talking = true; break; }
                    if (!talking) tagList.Add(t);
                }
                if (tagList.Count > TagMax)
                {
                    tagList.Sort((x, y) => (x.Anchor - cp).sqrMagnitude.CompareTo((y.Anchor - cp).sqrMagnitude));
                    tagList.RemoveRange(TagMax, tagList.Count - TagMax);
                }
            }
            tagGone.Clear();
            foreach (var kv in tagViews)
            {
                if (kv.Key == null) { kv.Value.root.RemoveFromHierarchy(); tagGone.Add(kv.Key); continue; }
                if (!tagList.Contains(kv.Key)) kv.Value.root.style.display = DisplayStyle.None;
            }
            foreach (var k in tagGone) tagViews.Remove(k);
            if (tagList.Count == 0) return;
            float pw = root.layout.width, ph = root.layout.height;
            if (float.IsNaN(pw) || pw < 10f) return;
            var camPos = cam.transform.position;
            foreach (var t in tagList)
            {
                TagView v;
                if (!tagViews.TryGetValue(t, out v)) { v = MakeTag(); tagViews[t] = v; }
                v.root.style.display = DisplayStyle.Flex;
                if (v.version != t.Version)
                {
                    v.version = t.Version;
                    v.text.text = K.Esc(t.Text);
                    v.dot.style.backgroundColor = t.Color;
                    v.text.style.color = Color.Lerp(t.Color, Color.white, 0.55f);
                }
                var pp = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, t.Anchor, cam);
                if (pp.x < -50f || pp.x > pw + 50f || pp.y < 0f || pp.y > ph + 30f) { v.root.style.display = DisplayStyle.None; continue; }
                v.root.style.left = pp.x; v.root.style.top = pp.y;
                float dist = Vector3.Distance(camPos, t.Anchor);
                float s = Mathf.Clamp(7f / Mathf.Max(0.1f, dist), 0.62f, 1f);
                v.root.style.scale = new Scale(new Vector3(s, s, 1f));
                v.root.style.opacity = Mathf.Clamp01((TagMaxDist - dist) / 5f);
            }
        }

        // ======================= облачка реплик =======================
        const float BubbleMaxDist = 30f;
        const int BubbleMax = 5;
        VisualElement bubbleLayer;

        class BubbleView { public VisualElement root, card, tail; public Label speaker, text; public int version = -1; public float shownAt; }
        readonly Dictionary<SpeechBubble, BubbleView> bubbleViews = new Dictionary<SpeechBubble, BubbleView>();
        readonly List<SpeechBubble> bubbleList = new List<SpeechBubble>();
        readonly List<SpeechBubble> bubbleGone = new List<SpeechBubble>();

        BubbleView MakeBubble()
        {
            var v = new BubbleView();
            v.root = K.Box(); v.root.pickingMode = PickingMode.Ignore; v.root.style.position = Position.Absolute; v.root.style.alignItems = Align.Center;
            v.root.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
            v.root.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(100f));
            v.card = K.Box(); v.card.pickingMode = PickingMode.Ignore; v.card.style.maxWidth = 340f;
            K.Radius(v.card, 16f); K.Pad(v.card, 8f, 14f, 10f, 14f);
            Border(v.card, new Color(Ink.r, Ink.g, Ink.b, 0.22f), 1.5f);
            v.speaker = K.B("", 11f, new Color(Ink.r, Ink.g, Ink.b, 0.55f)); v.speaker.style.letterSpacing = 1.2f; v.speaker.style.marginBottom = 2f;
            v.card.Add(v.speaker);
            v.text = K.T("", 17f, Ink, false, false, true); v.card.Add(v.text);
            v.tail = K.Box(); v.tail.pickingMode = PickingMode.Ignore; v.tail.style.width = 14f; v.tail.style.height = 14f; v.tail.style.marginTop = -8f;
            v.tail.style.rotate = new Rotate(45f);
            K.Line(v.tail, new Color(Ink.r, Ink.g, Ink.b, 0.22f), 0f, 1.5f, 1.5f, 0f);
            v.root.Add(v.card); v.root.Add(v.tail);
            bubbleLayer.Add(v.root);
            return v;
        }

        void UpdateBubbles(bool on)
        {
            var cam = Camera.main;
            bubbleList.Clear();
            if (on && cam != null && root.panel != null)
            {
                var cp = cam.transform.position;
                foreach (var b in SpeechBubble.Active)
                {
                    if (b == null || b.Text == null) continue;
                    var a = b.Anchor;
                    if ((a - cp).sqrMagnitude > BubbleMaxDist * BubbleMaxDist) continue;
                    if (cam.WorldToViewportPoint(a).z < 0.3f) continue;
                    bubbleList.Add(b);
                }
                int max = g.CurMode == GameRoot.Mode.Lunch ? 2 : BubbleMax;   // на обеде — не больше двух реплик сразу
                if (bubbleList.Count > max)
                {
                    bubbleList.Sort((x, y) => (x.Anchor - cp).sqrMagnitude.CompareTo((y.Anchor - cp).sqrMagnitude));
                    bubbleList.RemoveRange(max, bubbleList.Count - max);
                }
            }
            bubbleGone.Clear();
            foreach (var kv in bubbleViews)
            {
                if (kv.Key == null) { kv.Value.root.RemoveFromHierarchy(); bubbleGone.Add(kv.Key); continue; }
                if (!bubbleList.Contains(kv.Key)) kv.Value.root.style.display = DisplayStyle.None;
            }
            foreach (var k in bubbleGone) bubbleViews.Remove(k);
            if (bubbleList.Count == 0) return;

            float pw = root.layout.width, ph = root.layout.height;
            if (float.IsNaN(pw) || pw < 10f) return;
            var camPos = cam.transform.position;
            foreach (var b in bubbleList)
            {
                BubbleView v;
                if (!bubbleViews.TryGetValue(b, out v)) { v = MakeBubble(); bubbleViews[b] = v; }
                v.root.style.display = DisplayStyle.Flex;
                if (v.version != b.Version)
                {
                    v.version = b.Version; v.shownAt = Time.unscaledTime;
                    v.text.text = K.Esc(b.Text);
                    v.speaker.text = string.IsNullOrEmpty(b.Speaker) ? "" : b.Speaker.ToUpperInvariant();
                    v.speaker.style.display = string.IsNullOrEmpty(b.Speaker) ? DisplayStyle.None : DisplayStyle.Flex;
                    var fill = b.Fill; fill.a = 0.97f;
                    v.card.style.backgroundColor = fill; v.tail.style.backgroundColor = fill;
                }
                var pp = RuntimePanelUtils.CameraTransformWorldToPanel(root.panel, b.Anchor, cam);
                float h = v.root.layout.height, w = v.root.layout.width;
                if (float.IsNaN(h) || h < 10f) h = 80f;
                if (float.IsNaN(w) || w < 10f) w = 240f;
                // облачко целиком на экране: у краёв прижимаем, хвостик остаётся над головой по смыслу
                float x = Mathf.Clamp(pp.x, w * 0.5f + 12f, pw - w * 0.5f - 12f);
                float y = Mathf.Clamp(pp.y, h + 12f, ph - 12f);
                v.root.style.left = x; v.root.style.top = y;
                float dist = Vector3.Distance(camPos, b.Anchor);
                float k = Mathf.Clamp01((Time.unscaledTime - v.shownAt) / 0.18f); k = 1f - (1f - k) * (1f - k);
                float s = Mathf.Clamp(9f / Mathf.Max(0.1f, dist), 0.72f, 1f) * (0.85f + 0.15f * k);
                v.root.style.scale = new Scale(new Vector3(s, s, 1f));
                v.root.style.opacity = k;
            }
        }
    }

    // Имя горожанина над головой: данные для интерфейса (раньше — 3D-текст, расплывался и мельтешил)
    public class NameTag : MonoBehaviour
    {
        public static readonly List<NameTag> Active = new List<NameTag>();
        public string Text; public Color Color; public float HeadY = 2.2f; public bool Humanitarian; public int Version;
        public void Set(string text, Color c, float headY, bool humanitarian) { Text = text; Color = c; HeadY = headY; Humanitarian = humanitarian; Version++; }
        void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
        void OnDisable() { Active.Remove(this); }
        public Vector3 Anchor { get { var o = transform.parent; return o != null ? o.position + Vector3.up * HeadY * o.lossyScale.y : transform.position; } }
    }
}
