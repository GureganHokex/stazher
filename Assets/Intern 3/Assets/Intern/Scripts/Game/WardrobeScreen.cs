// Гардероб: настройка внешности. Можно примерить всё, платные вещи покупаются при выходе.
using System;
using UnityEngine;

namespace Intern.Game
{
    public class WardrobeScreen
    {
        readonly GameRoot g;
        UiKit S { get { return g.Ui; } }
        Appearance draft, original;
        bool firstTime;
        int tab;
        Vector2 scroll;
        public float Spin;
        static readonly string[] TabsBean = { "Лицо", "Волосы", "Одежда", "Аксессуары" };
        static readonly string[] TabsModel = { "Костюм", "Цвета", "Эмоция", "Аксессуары" };
        bool Model { get { return ModelLib.HasCharacter("Intern"); } }
        string[] Tabs { get { return Model ? TabsModel : TabsBean; } }

        public WardrobeScreen(GameRoot g) { this.g = g; }

        public void Open(Appearance current, bool first)
        {
            original = current.Clone(); draft = current.Clone();
            firstTime = first; tab = 0; Spin = 0; scroll = Vector2.zero;
        }

        bool Owned(string key, int price) { return price == 0 || g.Save.owned.Contains(key); }
        bool RankOk { get { return g.GradeIdx >= 2; } }   // корона — с грейда Junior+

        int Cost
        {
            get
            {
                int c = 0;
                if (!Model && !Owned("top" + draft.top, Catalog.TopPrice(draft.top))) c += Catalog.TopPrice(draft.top);
                if (Model && !Owned("outfit" + draft.outfit, Catalog.OutfitPrice(draft.outfit))) c += Catalog.OutfitPrice(draft.outfit);
                if (!Owned("acc" + draft.accessory, Catalog.AccPrice(draft.accessory))) c += Catalog.AccPrice(draft.accessory);
                return c;
            }
        }

        void Changed() { g.PreviewAppearance(draft); }

        public void Draw(float W, float H)
        {
            // Поворот персонажа перетаскиванием мыши (вне панели)
            var e = Event.current;
            float panelW = Mathf.Min(470, W * 0.42f), px = W - panelW - 20;
            if (e.type == EventType.MouseDrag && e.mousePosition.x < px) { Spin -= e.delta.x * 0.6f; e.Use(); }

            GUI.Label(new Rect(30, 26, px - 60, 60), firstTime ? "Создай своего стажёра" : "Гардероб", new GUIStyle(S.h1) { fontSize = 40 });
            GUI.Label(new Rect(32, 80, px - 60, 30), "Тяни мышью, чтобы повернуть персонажа", S.small);
            if (GUI.Button(new Rect(30, H - 80, 56, 48), "<", S.btnAlt)) Spin += 45;
            if (GUI.Button(new Rect(94, H - 80, 56, 48), ">", S.btnAlt)) Spin -= 45;

            var panel = new Rect(px, 20, panelW, H - 40);
            GUI.Box(panel, GUIContent.none, S.panel);
            GUILayout.BeginArea(new Rect(panel.x + 16, panel.y + 14, panel.width - 32, panel.height - 28));

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
                if (GUILayout.Button(Tabs[i], tab == i ? S.btn : S.btnGhost, GUILayout.Height(40))) { tab = i; scroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (Model) DrawModelTab(); else
            switch (tab)
            {
                case 0:
                    Header("Цвет кожи");
                    Swatches(Catalog.Skins, draft.skin, v => draft.skin = v);
                    Header("Глаза");
                    Options(Catalog.EyeNames, draft.eyes, v => draft.eyes = v, null);
                    Header("Рот");
                    Options(Catalog.MouthNames, draft.mouth, v => draft.mouth = v, null);
                    GUILayout.Space(6);
                    if (GUILayout.Button(draft.blush ? "Румянец: есть" : "Румянец: нет", S.btnAlt, GUILayout.Height(38))) { draft.blush = !draft.blush; Changed(); }
                    break;
                case 1:
                    Header("Причёска");
                    Options(Catalog.HairNames, draft.hair, v => draft.hair = v, null);
                    Header("Цвет волос");
                    Swatches(Catalog.HairColors, draft.hairColor, v => draft.hairColor = v);
                    break;
                case 2:
                    Header("Верх");
                    Options(Catalog.TopNames, draft.top, v => draft.top = v, i => PriceTag("top" + i, Catalog.TopPrice(i), false));
                    Header("Цвет верха");
                    Swatches(Catalog.Cloth, draft.topColor, v => draft.topColor = v);
                    Header("Штаны");
                    Swatches(Catalog.Cloth, draft.pants, v => draft.pants = v);
                    Header("Обувь");
                    Swatches(Catalog.Cloth, draft.shoes, v => draft.shoes = v);
                    break;
                default:
                    Header("Аксессуар");
                    Options(Catalog.AccNames, draft.accessory, v => draft.accessory = v, i => PriceTag("acc" + i, Catalog.AccPrice(i), Catalog.AccNeedsJuniorPlus(i)));
                    GUILayout.Space(6);
                    GUILayout.Label("Корону выдают за звание Junior+. Остальное можно купить за монеты — примерять бесплатно.", S.small);
                    break;
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Случайно", S.btnGhost, GUILayout.Height(36))) Randomize();
            int cost = Cost;
            bool crownBlocked = draft.accessory == 5 && !RankOk;
            if (crownBlocked) GUILayout.Label("<color=#FF4F9A>Корона станет доступна на звании Junior+.</color>", S.small);
            else if (cost > 0) GUILayout.Label("К покупке: " + cost + " монет (у тебя " + g.Save.money + ")", S.body);
            GUILayout.BeginHorizontal();
            if (!firstTime && GUILayout.Button("Отмена", S.btnAlt, GUILayout.Height(46))) g.CloseWardrobe(original, 0);
            GUI.enabled = !crownBlocked && cost <= g.Save.money;
            string ok = cost > 0 ? "Купить и надеть" : (firstTime ? "Готово, в офис!" : "Готово");
            if (GUILayout.Button(ok, S.btn, GUILayout.Height(46))) g.CloseWardrobe(draft, cost);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // Вкладки для модели из Blender: костюмы, цвета, эмоции, аксессуары
        void DrawModelTab()
        {
            switch (tab)
            {
                case 0:
                    Header("Костюм");
                    for (int i = 0; i < Catalog.OutfitNames.Length; i++)
                    {
                        string label = Catalog.OutfitNames[i] + PriceTag("outfit" + i, Catalog.OutfitPrice(i), false);
                        if (GUILayout.Button(label, i == draft.outfit ? S.btn : S.btnAlt, GUILayout.Height(36)) && i != draft.outfit)
                        { draft.outfit = i; draft.topColor = draft.pants = draft.shoes = draft.tie = -1; Changed(); }
                    }
                    GUILayout.Label("При смене костюма цвета возвращаются к «родным». Перекрасить можно во вкладке «Цвета».", S.small);
                    break;
                case 1:
                    Header("Цвет кожи");
                    Swatches(Catalog.Skins, draft.skin, v => draft.skin = v);
                    ColorRow("Верх", draft.topColor, v => draft.topColor = v);
                    ColorRow("Низ", draft.pants, v => draft.pants = v);
                    ColorRow("Обувь", draft.shoes, v => draft.shoes = v);
                    if (Catalog.OutfitIds[Mathf.Clamp(draft.outfit, 0, Catalog.OutfitIds.Length - 1)] == "classic")
                        ColorRow("Галстук", draft.tie, v => draft.tie = v);
                    break;
                case 2:
                    Header("Эмоция");
                    Options(Catalog.EmotionNames, draft.emotion, v => draft.emotion = v, null);
                    GUILayout.Space(6);
                    GUILayout.Label("С этим лицом стажёр ходит по офису. После сданной задачи он на пару секунд придёт в восторг, после упавшего теста — расстроится.", S.small);
                    break;
                default:
                    Header("Аксессуар");
                    Options(Catalog.AccNames, draft.accessory, v => draft.accessory = v, i => PriceTag("acc" + i, Catalog.AccPrice(i), Catalog.AccNeedsJuniorPlus(i)));
                    GUILayout.Space(6);
                    GUILayout.Label("Корону выдают за звание Junior+. Остальное можно купить за монеты — примерять бесплатно.", S.small);
                    break;
            }
        }

        void ColorRow(string title, int current, Action<int> set)
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.Label(title, S.h3, GUILayout.Width(120));
            if (GUILayout.Button(current < 0 ? "Как у костюма (выбрано)" : "Как у костюма", current < 0 ? S.btn : S.btnGhost, GUILayout.Height(28), GUILayout.ExpandWidth(false)) && current >= 0)
            { set(-1); Changed(); }
            GUILayout.EndHorizontal();
            Swatches(Catalog.Cloth, current, set);
        }

        string PriceTag(string key, int price, bool needsRank)
        {
            if (needsRank) return RankOk ? "" : "  (Junior+)";
            return Owned(key, price) ? "" : "  · " + price;
        }

        void Header(string t) { GUILayout.Space(10); GUILayout.Label(t, S.h3); }

        void Options(string[] names, int current, Action<int> set, Func<int, string> suffix)
        {
            for (int i = 0; i < names.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + 2, names.Length); j++)
                {
                    string label = names[j] + (suffix != null ? suffix(j) : "");
                    if (GUILayout.Button(label, j == current ? S.btn : S.btnAlt, GUILayout.Height(38)) && j != current) { set(j); Changed(); }
                }
                GUILayout.EndHorizontal();
            }
        }

        void Swatches(Color[] colors, int current, Action<int> set)
        {
            const float size = 38, gap = 8;
            int perRow = 8;
            for (int i = 0; i < colors.Length; i += perRow)
            {
                var row = GUILayoutUtility.GetRect(perRow * (size + gap), size + gap);
                for (int j = i; j < Mathf.Min(i + perRow, colors.Length); j++)
                {
                    var r = new Rect(row.x + (j - i) * (size + gap), row.y, size, size);
                    if (j == current) { var old = GUI.backgroundColor; GUI.backgroundColor = Pal.Sun; GUI.Box(new Rect(r.x - 4, r.y - 4, size + 8, size + 8), GUIContent.none, S.swatch); GUI.backgroundColor = old; }
                    var prev = GUI.backgroundColor;
                    GUI.backgroundColor = colors[j];
                    if (GUI.Button(r, GUIContent.none, S.swatch) && j != current) { set(j); Changed(); }
                    GUI.backgroundColor = prev;
                }
            }
        }

        void Randomize()
        {
            var r = new System.Random();
            draft.skin = r.Next(Catalog.Skins.Length);
            draft.hair = r.Next(Catalog.HairNames.Length);
            draft.hairColor = r.Next(Catalog.HairColors.Length);
            draft.eyes = r.Next(Catalog.EyeNames.Length);
            draft.mouth = r.Next(Catalog.MouthNames.Length);
            draft.topColor = r.Next(Catalog.Cloth.Length);
            draft.pants = r.Next(Catalog.Cloth.Length);
            draft.shoes = r.Next(Catalog.Cloth.Length);
            draft.blush = r.Next(2) == 0;
            draft.tie = r.Next(Catalog.Cloth.Length);
            if (Model)
            {
                int o = r.Next(Catalog.OutfitIds.Length);
                draft.outfit = Owned("outfit" + o, Catalog.OutfitPrice(o)) ? o : r.Next(2);
                draft.emotion = r.Next(Catalog.EmotionNames.Length);
                draft.topColor = draft.pants = draft.shoes = draft.tie = r.Next(3) == 0 ? r.Next(Catalog.Cloth.Length) : -1;
            }
            // случайно выбираем только из уже доступных вещей
            int top = r.Next(Catalog.TopNames.Length);
            draft.top = Owned("top" + top, Catalog.TopPrice(top)) ? top : 0;
            int acc = r.Next(Catalog.AccNames.Length);
            draft.accessory = (Owned("acc" + acc, Catalog.AccPrice(acc)) && !(Catalog.AccNeedsJuniorPlus(acc) && !RankOk)) ? acc : 0;
            Changed();
        }
    }
}
