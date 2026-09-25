// Интерфейс обеда: таймер и компас, монеты и серия, здоровье, оружие и патроны, прицел, оптика,
// магазины города (оружейная, мастерская, ларёк, шаурма), арсенал у шкафчика и итоги обеда.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public partial class GameUi
    {
        VisualElement shopLayer, lunchSum, lunchSumCard, hpFill, hpCard, weaponCard, reloadBar, reloadFill, heatBar, heatFill, compass, hurtVeil, scope, cross, statusRow;
        Label hpLabel, weaponName, ammoLabel, ammoReserve, weaponHint, slot1, slot2, statusLabel, couponLabel;
        readonly List<Label> compassMarks = new List<Label>();
        int shopBuilt = -1;
        int shownCoins; float coinsShownAt;

        // ======================= HUD обеда =======================
        VisualElement BuildLunchHud()
        {
            var layer = Layer();
            hurtVeil = Layer(); hurtVeil.style.backgroundColor = new Color(0.8f, 0f, 0.05f, 0f); layer.Add(hurtVeil);
            scope = new ScopeOverlay(); K.Fill(scope); scope.style.display = DisplayStyle.None; layer.Add(scope);

            // таймер обеда сверху по центру и компас под ним
            var top = K.Box(); top.pickingMode = PickingMode.Ignore; top.style.position = Position.Absolute; top.style.left = 0f; top.style.right = 0f; top.style.top = 18f;
            top.style.alignItems = Align.Center;
            var pill = K.Box(true); pill.pickingMode = PickingMode.Ignore; pill.style.alignItems = Align.Center; pill.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.92f);
            K.Radius(pill, 26f); Border(pill, new Color(Sun.r, Sun.g, Sun.b, 0.6f), 2f); K.Pad(pill, 8f, 22f, 8f, 16f);
            pill.Add(new Icon("clock", Sun, 26f));
            var lt = K.B("ОБЕД", 14f, Muted); lt.style.marginLeft = 10f; lt.style.letterSpacing = 1.5f; pill.Add(lt);
            lunchTimer = K.B("6:00", 34f, Text); lunchTimer.style.marginLeft = 12f; lunchTimer.style.unityFontStyleAndWeight = FontStyle.Bold; pill.Add(lunchTimer);
            top.Add(pill);
            compass = K.Box(); compass.pickingMode = PickingMode.Ignore; compass.style.width = 720f; compass.style.height = 34f; compass.style.marginTop = 8f;
            compass.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.55f); K.Radius(compass, 17f); compass.style.overflow = Overflow.Hidden;
            var tick = K.Box(); tick.style.position = Position.Absolute; tick.style.left = 359f; tick.style.top = 0f; tick.style.width = 2f; tick.style.height = 34f; tick.style.backgroundColor = new Color(Sun.r, Sun.g, Sun.b, 0.7f); compass.Add(tick);
            top.Add(compass);
            layer.Add(top);

            // монеты за этот обед: «+10» рядом со счётчиком складываются в серию
            var card = K.Box(); card.pickingMode = PickingMode.Ignore; card.style.position = Position.Absolute; card.style.left = 24f; card.style.top = 22f; card.style.width = 340f;
            card.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.9f); K.Radius(card, 18f); Border(card, new Color(Line.r, Line.g, Line.b, 0.8f), 1f); K.Pad(card, 14f, 20f, 16f, 20f);
            card.Add(SectionLabel("ЗА ЭТОТ ОБЕД"));
            lunchCoinsRow = K.Box(true); lunchCoinsRow.pickingMode = PickingMode.Ignore; lunchCoinsRow.style.alignItems = Align.Center; lunchCoinsRow.style.marginTop = 6f;
            lunchCoinsRow.Add(new Icon("coin", Sun, 30f));
            lunchCoins = K.B("0", 38f, Sun); lunchCoins.style.marginLeft = 10f; lunchCoinsRow.Add(lunchCoins);
            lunchSeries = K.B("", 26f, Mint); lunchSeries.style.marginLeft = 14f; lunchCoinsRow.Add(lunchSeries);
            lunchPenalty = K.B("", 26f, Pink); lunchPenalty.style.marginLeft = 10f; lunchCoinsRow.Add(lunchPenalty);
            card.Add(lunchCoinsRow);
            lunchKills = K.T("", 16f, Muted); lunchKills.style.marginTop = 6f; card.Add(lunchKills);
            couponLabel = K.T("", 15f, Sun); couponLabel.style.marginTop = 4f; card.Add(couponLabel);
            layer.Add(card);

            // здоровье — слева внизу
            hpCard = K.Box(); hpCard.pickingMode = PickingMode.Ignore; hpCard.style.position = Position.Absolute; hpCard.style.left = 24f; hpCard.style.bottom = 26f; hpCard.style.width = 340f;
            hpCard.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.9f); K.Radius(hpCard, 16f); K.Pad(hpCard, 12f, 18f, 14f, 18f);
            var hr = K.Box(true); hr.style.alignItems = Align.Center; hr.pickingMode = PickingMode.Ignore;
            hr.Add(K.B("ЗДОРОВЬЕ", 12f, Muted)); hr.Add(K.Spacer()); hpLabel = K.B("100", 20f, Text); hr.Add(hpLabel); hpCard.Add(hr);
            var bar = K.Box(); bar.pickingMode = PickingMode.Ignore; bar.style.height = 14f; bar.style.marginTop = 6f; bar.style.backgroundColor = Well; K.Radius(bar, 7f); bar.style.overflow = Overflow.Hidden;
            hpFill = K.Box(); hpFill.pickingMode = PickingMode.Ignore; hpFill.style.height = 14f; hpFill.style.width = Length.Percent(100f); hpFill.style.backgroundColor = Mint; K.Radius(hpFill, 7f); bar.Add(hpFill);
            hpCard.Add(bar);
            statusLabel = K.T("", 14f, Sun); statusLabel.style.marginTop = 6f; hpCard.Add(statusLabel);
            layer.Add(hpCard);

            // оружие — справа внизу
            weaponCard = K.Box(); weaponCard.pickingMode = PickingMode.Ignore; weaponCard.style.position = Position.Absolute; weaponCard.style.right = 24f; weaponCard.style.bottom = 26f; weaponCard.style.width = 360f;
            weaponCard.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.9f); K.Radius(weaponCard, 16f); K.Pad(weaponCard, 12f, 18f, 14f, 18f);
            var slots = K.Box(true); slots.pickingMode = PickingMode.Ignore;
            slot1 = SlotChip("1  Ближний"); slot2 = SlotChip("2  Огнестрел"); slots.Add(slot1); slots.Add(slot2); weaponCard.Add(slots);
            weaponName = K.B("", 20f, Text); weaponName.style.marginTop = 8f; weaponCard.Add(weaponName);
            var ar = K.Box(true); ar.style.alignItems = Align.FlexEnd; ar.pickingMode = PickingMode.Ignore; ar.style.marginTop = 2f;
            ammoLabel = K.B("", 40f, Sun); ar.Add(ammoLabel); ammoReserve = K.B("", 18f, Muted); ammoReserve.style.marginLeft = 8f; ammoReserve.style.marginBottom = 6f; ar.Add(ammoReserve);
            weaponCard.Add(ar);
            reloadBar = K.Box(); reloadBar.pickingMode = PickingMode.Ignore; reloadBar.style.height = 6f; reloadBar.style.backgroundColor = Well; K.Radius(reloadBar, 3f); reloadBar.style.marginTop = 4f;
            reloadFill = K.Box(); reloadFill.pickingMode = PickingMode.Ignore; reloadFill.style.height = 6f; reloadFill.style.backgroundColor = Sky; K.Radius(reloadFill, 3f); reloadBar.Add(reloadFill); weaponCard.Add(reloadBar);
            heatBar = K.Box(); heatBar.pickingMode = PickingMode.Ignore; heatBar.style.height = 6f; heatBar.style.backgroundColor = Well; K.Radius(heatBar, 3f); heatBar.style.marginTop = 4f;
            heatFill = K.Box(); heatFill.pickingMode = PickingMode.Ignore; heatFill.style.height = 6f; heatFill.style.backgroundColor = Pink; K.Radius(heatFill, 3f); heatBar.Add(heatFill); weaponCard.Add(heatBar);
            weaponHint = K.T("", 14f, Muted, false, false, true); weaponHint.style.marginTop = 6f; weaponCard.Add(weaponHint);
            layer.Add(weaponCard);

            // прицел
            cross = new Crosshair(); cross.style.position = Position.Absolute; cross.style.left = Length.Percent(50f); cross.style.top = Length.Percent(50f);
            cross.style.width = 80f; cross.style.height = 80f; cross.style.marginLeft = -40f; cross.style.marginTop = -40f; layer.Add(cross);

            var hintRow = K.Box(true); hintRow.pickingMode = PickingMode.Ignore; hintRow.style.position = Position.Absolute; hintRow.style.left = 0f; hintRow.style.right = 0f; hintRow.style.bottom = 30f;
            hintRow.style.justifyContent = Justify.Center;
            var hp = K.Box(true); hp.pickingMode = PickingMode.Ignore; hp.style.backgroundColor = new Color(Card.r, Card.g, Card.b, 0.85f); K.Radius(hp, 14f); K.Pad(hp, 6f, 16f, 6f, 16f);
            lunchHint = K.T("", 15f, Text); hp.Add(lunchHint);
            hintRow.Add(hp); layer.Add(hintRow);
            return layer;
        }

        Label SlotChip(string text)
        {
            var l = K.B(text, 13f, Muted); K.Pad(l, 3f, 10f, 3f, 10f); K.Radius(l, 8f); l.style.marginRight = 6f; l.style.backgroundColor = Well; return l;
        }

        void UpdateLunchHud()
        {
            var L = g.Lunch; if (L == null) return;
            var C = g.Combat; var A = g.Arsenal;
            int sec = Mathf.CeilToInt(L.timeLeft);
            string t = (sec / 60) + ":" + (sec % 60).ToString("00");
            if (lunchTimer.text != t) { lunchTimer.text = t; lunchTimer.style.color = sec <= 30 ? Pink : Text; }
            // счётчик догоняет сумму, когда серия закончилась
            bool inSeries = L.series > 0;
            int target = inSeries ? L.coins - L.series : L.coins;
            if (shownCoins != target && Time.unscaledTime - coinsShownAt > 0.03f)
            {
                shownCoins += Math.Sign(target - shownCoins) * Mathf.Max(1, Mathf.Abs(target - shownCoins) / 6);
                coinsShownAt = Time.unscaledTime;
            }
            string key = shownCoins + "|" + L.series + "|" + L.kills + "|" + L.escaped + "|" + L.hidden + "|" + L.Left + "|" + (Time.unscaledTime - L.penaltyAt < 1.6f) + "|" + g.Save.coupons + "|" + L.Filming;
            if (key != lastLunch)
            {
                lastLunch = key;
                lunchCoins.text = shownCoins.ToString();
                lunchSeries.text = inSeries ? "+" + L.series : "";
                lunchPenalty.text = Time.unscaledTime - L.penaltyAt < 1.6f ? "−" + L.penaltyShown : "";
                lunchKills.text = "Выбито " + L.kills + "  ·  убежали " + L.escaped + (L.hidden > 0 ? "  ·  в архиве " + L.hidden : "") + "  ·  ещё " + L.Left + (L.Filming > 0 ? "\nТебя снимает журналист: замечают вдвое дальше" : "");
                couponLabel.text = g.Save.coupons > 0 ? "Купон декана: −20% в мастерской «Патч»" : "";
                couponLabel.style.display = g.Save.coupons > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            float age = Time.unscaledTime - L.seriesAt;
            float bump = inSeries && age < 0.25f ? 1f + (0.25f - age) * 1.2f : 1f;
            lunchSeries.style.scale = new Scale(new Vector3(bump, bump, 1f));

            // здоровье и состояния
            float hpK = L.maxHp > 0 ? (float)L.hp / L.maxHp : 0f;
            hpFill.style.width = Length.Percent(hpK * 100f);
            hpFill.style.backgroundColor = hpK > 0.5f ? Mint : hpK > 0.25f ? Sun : Pink;
            hpLabel.text = L.hp + (L.hoodie ? "  ·  худи −25%" : "");
            string st = "";
            if (Time.time < L.stunUntil) st = "Оглушён печатью!";
            else if (Time.time < L.slowUntil) st = "«А зачем?» — замедлен";
            if (Time.time < L.energyUntil) st += (st.Length > 0 ? "  ·  " : "") + "Энергетик: быстрее";
            statusLabel.text = st; statusLabel.style.display = st.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            float hurt = Mathf.Clamp01(1f - (Time.unscaledTime - L.hurtAt) / 0.5f) * 0.35f + (hpK < 0.25f ? 0.08f + Mathf.Sin(Time.unscaledTime * 5f) * 0.04f : 0f);
            hurtVeil.style.backgroundColor = new Color(0.8f, 0f, 0.05f, hurt);

            // оружие
            if (C != null && A != null)
            {
                var w = C.Current; bool gun = !w.Melee;
                weaponName.text = w.name + (A.Level(w.id) > 0 ? "  ур. " + A.Level(w.id) : "");
                slot1.style.backgroundColor = C.slot == 0 ? Sun : Well; slot1.style.color = C.slot == 0 ? Ink : Muted;
                slot2.style.backgroundColor = C.slot == 1 ? Sun : Well; slot2.style.color = C.slot == 1 ? Ink : (A.Gun == null ? new Color(Muted.r, Muted.g, Muted.b, 0.4f) : Muted);
                slot2.text = "2  " + (A.Gun != null ? A.Gun.name.Split(' ')[0] : "нет");
                ammoLabel.text = gun ? C.MagNow.ToString() : "∞";
                ammoLabel.style.color = gun && C.MagNow == 0 ? Pink : Sun;
                ammoReserve.text = gun ? "/ " + C.MagSize + "   запас " + C.Reserve : (C.KnifeAway ? "нож летит…" : "");
                reloadBar.style.display = C.Reloading ? DisplayStyle.Flex : DisplayStyle.None;
                reloadFill.style.width = Length.Percent(C.ReloadProgress * 100f);
                bool heat = gun && A.Overheats(w);
                heatBar.style.display = heat ? DisplayStyle.Flex : DisplayStyle.None;
                if (heat) { heatFill.style.width = Length.Percent(Mathf.Clamp01(C.Heat / 60f) * 100f); heatFill.style.backgroundColor = C.Overheated ? Pink : Sun; }
                string hint = w.Melee ? (w.id == "katana" ? "ПКМ — рывок" + (C.DashReady < 1f ? " (" + Mathf.RoundToInt(C.DashReady * 100f) + "%)" : "") : w.id == "knife" && A.Perk(w) ? "ПКМ — метнуть нож" : "Бесшумно: не пугает тех, кто не видит")
                                     : "ПКМ — прицел  ·  R — перезарядка" + (w.auto ? "  ·  зажми ЛКМ" : "");
                weaponHint.text = hint;
                ((Crosshair)cross).Set(gun ? C.SpreadNow : 0f, gun, C.Scoped);
                cross.style.display = C.Scoped ? DisplayStyle.None : DisplayStyle.Flex;
                scope.style.display = C.Scoped ? DisplayStyle.Flex : DisplayStyle.None;
                string lh = "ЛКМ — " + (gun ? "огонь" : "удар") + "  ·  1 / 2 — оружие  ·  E — дверь, магазин  ·  гуманитарий +10, технарь −20";
                if (lunchHint.text != lh) lunchHint.text = lh;
            }

            // компас: метки переулков и офиса по направлению взгляда
            var cam = Camera.main;
            if (cam != null && g.Lunch.City != null)
            {
                var marks = g.Lunch.City.landmarks;
                while (compassMarks.Count < marks.Count)
                {
                    var l = K.B("", 14f, Text); l.style.position = Position.Absolute; l.style.top = 7f; l.style.unityTextAlign = TextAnchor.MiddleCenter; l.style.width = 180f;
                    compass.Add(l); compassMarks.Add(l);
                }
                float yaw = cam.transform.eulerAngles.y; var p = g.Lunch.PlayerPos;
                for (int i = 0; i < marks.Count; i++)
                {
                    var to = marks[i].Value - p; to.y = 0;
                    float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    float d = Mathf.DeltaAngle(yaw, bearing);
                    var l = compassMarks[i];
                    if (Mathf.Abs(d) > 80f || to.magnitude < 4f) { l.style.display = DisplayStyle.None; continue; }
                    l.style.display = DisplayStyle.Flex;
                    l.style.left = 360f + d / 80f * 340f - 90f;
                    l.text = marks[i].Key + "  " + Mathf.RoundToInt(to.magnitude) + " м";
                    l.style.color = marks[i].Key == "Офис" ? Sun : Text;
                }
            }
        }

        // ======================= магазины и арсенал =======================
        ScrollBox shopScroll;

        void BuildShop()
        {
            shopLayer.Clear(); shopBuilt = g.ShopVersion;
            float keep = shopScroll != null ? shopScroll.Scroll : 0f;
            VisualElement wrap;
            var card = CardBox(980f, out wrap);
            card.style.maxHeight = 900f;
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            string kind = g.ShopKind;
            head.Add(new Icon(kind == "food" ? "fire" : kind == "ammo" ? "stack" : kind == "patch" ? "gear" : "server", Sun, 34f));
            var ht = K.B(g.ShopTitle, 38f, Sun, true); ht.style.marginLeft = 14f; head.Add(ht);
            head.Add(K.Spacer());
            head.Add(new Icon("coin", Sun, 26f)); var wl = K.B(g.Wallet.ToString(), 28f, Sun); wl.style.marginLeft = 8f; head.Add(wl);
            card.Add(head);
            string sub = g.Lunch != null ? "Таймер обеда идёт, пока ты у прилавка. Сначала тратятся монеты этого обеда." : "Между обедами: выбери, с чем идти в город, и переставь обвесы.";
            var st = K.T(sub, 15f, Muted, false, false, true); st.style.marginTop = 4f; st.style.marginBottom = 10f; card.Add(st);

            shopScroll = new ScrollBox(); shopScroll.style.height = 560f;
            shopScroll.RegisterCallback<WheelEvent>(e => { shopScroll.Wheel(e.delta.y * 40f); e.StopPropagation(); });
            var list = shopScroll.Content;
            switch (kind)
            {
                case "weapons": ShopWeapons(list); break;
                case "patch": ShopPatch(list); break;
                case "ammo": ShopAmmo(list); break;
                case "food": ShopFood(list); break;
                default: ShopArsenal(list); break;
            }
            card.Add(shopScroll);
            var close = new UiBtn(g.Lunch != null ? "Назад на улицу" : "Закрыть", () => g.CloseShop(), Ghost, GhostHover, GhostLip, Text, "close", "Esc", 56f, false);
            card.Add(close);
            shopLayer.Add(wrap);
            shopScroll.schedule.Execute(() => { shopScroll.Scroll = keep; shopScroll.Apply(); });
        }

        // Строка витрины: название, описание, цифры, кнопка
        VisualElement ShopRow(string title, string about, string stats, string button, Action act, bool enabled, Color accent, string state = null)
        {
            var r = K.Box(true); r.style.alignItems = Align.Center; K.Pad(r, 10f, 8f, 10f, 4f); r.pickingMode = PickingMode.Ignore;
            r.style.borderBottomWidth = 1f; r.style.borderBottomColor = new Color(Line.r, Line.g, Line.b, 0.5f);
            var col = K.Box(); K.Grow(col); col.pickingMode = PickingMode.Ignore;
            var tl = K.Box(true); tl.style.alignItems = Align.Center; tl.pickingMode = PickingMode.Ignore;
            tl.Add(K.B(title, 19f, Text));
            if (state != null) { var sl = K.B(state, 13f, accent); sl.style.marginLeft = 10f; K.Pad(sl, 2f, 8f, 2f, 8f); K.Radius(sl, 6f); sl.style.backgroundColor = new Color(accent.r, accent.g, accent.b, 0.16f); tl.Add(sl); }
            col.Add(tl);
            if (!string.IsNullOrEmpty(about)) { var a = K.T(about, 14f, Muted, false, false, true); a.style.marginTop = 2f; col.Add(a); }
            if (!string.IsNullOrEmpty(stats)) { var s2 = K.T(stats, 14f, Sky, false, false, true); s2.style.marginTop = 2f; col.Add(s2); }
            r.Add(col);
            if (button != null)
            {
                var b = new UiBtn(button, act, enabled ? Sun : Ghost, enabled ? SunHover : Ghost, enabled ? SunLip : GhostLip, enabled ? Ink : Muted, null, null, 46f, false, true);
                b.style.width = 250f; b.style.marginTop = 0f; b.style.marginLeft = 12f; b.Enabled = enabled;
                r.Add(b);
            }
            return r;
        }

        static string WeaponStats(WeaponDef w, Arsenal a)
        {
            int dmg = a != null ? a.Damage(w) : w.damage;
            if (w.Melee) return "Урон " + dmg + "  ·  " + w.rate.ToString("0.#") + " удара/с  ·  грейд " + Balance.GradeName(w.grade);
            return "Урон " + dmg + (w.pellets > 1 ? " × " + w.pellets : "") + "  ·  " + w.rate.ToString("0.#") + " выстр./с  ·  магазин " + (a != null ? a.MagSize(w) : w.mag) + "  ·  грейд " + Balance.GradeName(w.grade);
        }

        void ShopWeapons(VisualElement list)
        {
            var A = g.Arsenal; int grade = g.GradeForShop;
            list.Add(SectionLabel("ОРУЖИЕ  ·  открывается по грейду, как лицензия"));
            foreach (var w in Balance.D.weapons)
            {
                if (w.id == "knife") continue;
                bool owned = A.Owns(w.id), locked = grade < w.grade, equipped = owned && (A.Melee.id == w.id || (A.Gun != null && A.Gun.id == w.id));
                string btn = owned ? (equipped ? "В руках" : "Взять с собой") : locked ? "Нужен " + Balance.GradeName(w.grade) : "Купить — " + w.price;
                Action act = owned ? (Action)(() => g.EquipWeapon(w.id)) : () => g.BuyWeapon(w.id);
                list.Add(ShopRow(w.name, w.about, WeaponStats(w, A), btn, act, owned ? !equipped : !locked && g.Wallet >= w.price, owned ? Mint : locked ? Muted : Sun, owned ? "есть" : null));
            }
            var B = Balance.D;
            list.Add(SectionLabel("ЭКИПИРОВКА"));
            bool hl = grade < B.hoodieGrade;
            list.Add(ShopRow("Худи «Dark Theme»", "−25% входящего урона. Носится и в офисе.", null,
                g.Save.hoodie ? (g.Save.hoodieOn ? "Снять" : "Надеть") : hl ? "Нужен " + Balance.GradeName(B.hoodieGrade) : "Купить — " + B.hoodiePrice,
                g.Save.hoodie ? (Action)(() => g.ToggleHoodie()) : () => g.BuyHoodie(), g.Save.hoodie || (!hl && g.Wallet >= B.hoodiePrice), g.Save.hoodie ? Mint : Sun, g.Save.hoodie ? "есть" : null));
        }

        void ShopPatch(VisualElement list)
        {
            var A = g.Arsenal; int grade = g.GradeForShop;
            list.Add(SectionLabel("УЛУЧШЕНИЯ  ·  5 уровней: +10% урона, на 3-м быстрая перезарядка, на 5-м перк" + (g.Save.coupons > 0 ? "  ·  купон −20%" : "")));
            foreach (var ws in g.Save.arsenal)
            {
                var w = Balance.Weapon(ws.id); if (w == null) continue;
                bool max = ws.level >= 5;
                int full = Balance.UpgradePrice(w, ws.level), price = g.PatchPrice(full);
                string about = "Уровень " + ws.level + " из 5" + (max ? ". Перк: " + w.perk : ws.level + 1 == 3 ? ". Следующий: быстрая перезарядка" : ws.level + 1 == 5 ? ". Следующий: перк — " + w.perk : "");
                list.Add(ShopRow(w.name, about, WeaponStats(w, A), max ? "Максимум" : "Улучшить — " + price + (price < full ? " (было " + full + ")" : ""), () => g.UpgradeWeapon(w.id), !max && g.Wallet >= price, max ? Mint : Sun, "ур. " + ws.level));
            }
            list.Add(SectionLabel("ОБВЕСЫ  ·  покупаются один раз, переставляются бесплатно"));
            var gun = A.Gun; var gs = gun != null ? A.Get(gun.id) : null;
            foreach (var a in Balance.D.attachments)
            {
                bool owned = A.HasAttach(a.id), locked = grade < a.grade;
                string slotName = Arsenal.SlotNames[Array.IndexOf(Arsenal.SlotIds, a.slot)];
                string where = owned ? A.WhereInstalled(a.id) : null;
                if (owned)
                {
                    bool onGun = gs != null && where == gun.id;
                    list.Add(ShopRow(a.name, a.about, "Слот: " + slotName + (where != null ? "  ·  стоит на: " + Balance.Weapon(where).name : "  ·  не установлен"),
                        gs == null ? "Нет огнестрела" : onGun ? "Снять" : "Поставить на " + gun.name.Split(' ')[0], () => g.InstallAttach(gun.id, a.slot, a.id), gs != null, Mint, "есть"));
                }
                else
                {
                    int price = g.PatchPrice(a.price);
                    list.Add(ShopRow(a.name, a.about, "Слот: " + slotName + "  ·  грейд " + Balance.GradeName(a.grade), locked ? "Нужен " + Balance.GradeName(a.grade) : "Купить — " + price, () => g.BuyAttach(a.id), !locked && g.Wallet >= price, Sun));
                }
            }
        }

        void ShopAmmo(VisualElement list)
        {
            var A = g.Arsenal;
            list.Add(SectionLabel("ПАТРОНЫ  ·  запас общий для всего оружия этого калибра"));
            foreach (var a in Balance.D.ammo)
            {
                var users = new List<string>(); foreach (var w in Balance.D.weapons) if (w.ammo == a.id) users.Add(w.name.Split(' ')[0]);
                list.Add(ShopRow(a.name + ", " + a.count + " шт", "Для: " + string.Join(", ", users.ToArray()), "В запасе: " + A.AmmoOf(a.id), "Купить — " + a.price, () => g.BuyAmmo(a.id), g.Wallet >= a.price, Sun));
            }
        }

        void ShopFood(VisualElement list)
        {
            var B = Balance.D; var L = g.Lunch;
            list.Add(SectionLabel("ЕДА"));
            list.Add(ShopRow("Шаурма", "+" + B.shawarmaHeal + " здоровья сразу.", L != null ? "Сейчас: " + L.hp + " из " + L.maxHp : null, "Купить — " + B.shawarmaPrice, () => g.BuyFood("shawarma"), L != null && L.hp < L.maxHp && g.Wallet >= B.shawarmaPrice, Sun));
            list.Add(ShopRow("Энергетик", "+20% скорости на " + Mathf.RoundToInt(B.energySeconds) + " секунд.", null, "Купить — " + B.energyPrice, () => g.BuyFood("energy"), L != null && g.Wallet >= B.energyPrice, Sun));
        }

        void ShopArsenal(VisualElement list)
        {
            var A = g.Arsenal;
            list.Add(SectionLabel("С ЧЕМ ИДТИ НА ОБЕД"));
            foreach (var ws in g.Save.arsenal)
            {
                var w = Balance.Weapon(ws.id); if (w == null) continue;
                bool equipped = A.Melee.id == w.id || (A.Gun != null && A.Gun.id == w.id);
                list.Add(ShopRow(w.name, (w.Melee ? "Ближний бой" : "Огнестрел") + "  ·  уровень " + ws.level, WeaponStats(w, A), equipped ? "В руках" : "Взять", () => g.EquipWeapon(w.id), !equipped, equipped ? Mint : Sun));
            }
            if (g.Save.hoodie) list.Add(ShopRow("Худи «Dark Theme»", "−25% входящего урона.", null, g.Save.hoodieOn ? "Снять" : "Надеть", () => g.ToggleHoodie(), true, Mint));
            var gun = A.Gun;
            list.Add(SectionLabel("ОБВЕСЫ" + (gun != null ? " НА «" + gun.name.ToUpperInvariant() + "»" : "")));
            if (gun == null || g.Save.attachments.Count == 0)
            {
                list.Add(ShopRow(gun == null ? "Огнестрела пока нет" : "Обвесов пока нет", gun == null ? "Оружейная «Железо» — на площади в городе." : "Мастерская «Патч» — на площади в городе.", null, null, null, false, Muted));
                return;
            }
            var gs = A.Get(gun.id);
            foreach (var id in g.Save.attachments)
            {
                var a = Balance.Attach(id); if (a == null) continue;
                string where = A.WhereInstalled(id);
                bool on = where == gun.id;
                list.Add(ShopRow(a.name, a.about, "Слот: " + Arsenal.SlotNames[Array.IndexOf(Arsenal.SlotIds, a.slot)] + (where != null && !on ? "  ·  сейчас на: " + Balance.Weapon(where).name : ""), on ? "Снять" : "Поставить", () => g.InstallAttach(gun.id, a.slot, a.id), gs != null, on ? Mint : Sun, on ? "стоит" : null));
            }
        }

        // ======================= итоги обеда =======================
        void BuildLunchSummary()
        {
            lunchSum.Clear();
            var r = g.LastLunch; if (r == null) return;
            VisualElement wrap;
            lunchSumCard = CardBox(640f, out wrap);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.pickingMode = PickingMode.Ignore;
            head.Add(new Icon(r.knockedOut ? "warning" : "coin", r.knockedOut ? Pink : Sun, 36f));
            var ht = K.B(r.knockedOut ? "Тебя вырубили" : "Итоги обеда", 44f, r.knockedOut ? Pink : Sun, true); ht.style.marginLeft = 14f; head.Add(ht);
            lunchSumCard.Add(head);
            int m = Mathf.FloorToInt(r.seconds / 60f), s = Mathf.FloorToInt(r.seconds % 60f);
            var sub = K.T((r.earlyReturn ? "Вернулся раньше: " : "Обед длился ") + m + ":" + s.ToString("00") + (r.knockedOut ? " — очнулся в офисе" : ""), 16f, Muted); sub.style.marginTop = 2f; sub.style.marginBottom = 14f; lunchSumCard.Add(sub);
            lunchSumCard.Add(StatRow("star", "Выбито гуманитариев", r.kills.ToString(), Mint));
            lunchSumCard.Add(StatRow("coin", "Монеты за обед", (r.coins >= 0 ? "+" : "") + r.coins, Sun));
            if (r.lost > 0) lunchSumCard.Add(StatRow("warning", "Потеряно, когда вырубили", "−" + r.lost, Pink));
            if (r.fines > 0) lunchSumCard.Add(StatRow("warning", "Штрафы за технарей", "−" + r.fines + "  (" + r.techHits + ")", Pink));
            if (r.spent > 0) lunchSumCard.Add(StatRow("stack", "Потрачено в магазинах", r.spent.ToString(), Text));
            lunchSumCard.Add(StatRow("door", "Убежали", r.escaped.ToString(), Text));
            if (r.hidden > 0) lunchSumCard.Add(StatRow("folder", "Спрятались в архиве", r.hidden.ToString(), Text));
            if (r.bestSeries > 0) lunchSumCard.Add(StatRow("fire", "Лучшая серия", "+" + r.bestSeries, Mint));
            if (r.coupon) lunchSumCard.Add(StatRow("star", "Декан гумфака", "купон −20% в «Патче»", Sun));
            string note = r.knockedOut ? "Половина монет обеда сгорела, бонуса «Сытый» нет. Шаурма на площади лечит +50." :
                          "«Сытый»: +10% XP за задачи ещё 2 часа. Гена ждёт тикеты.";
            var nl = K.T(note, 16f, r.knockedOut ? Pink : Muted, false, false, true); nl.style.marginTop = 14f; lunchSumCard.Add(nl);
            lunchSumCard.Add(new UiBtn("В офис", () => g.UiLunchSummaryDone(), Sun, SunHover, SunLip, Ink, "play", null, 60f, false));
            lunchSum.Add(wrap);
        }
    }

    // Прицел: четыре штриха, расходятся с разбросом
    public class Crosshair : VisualElement
    {
        float spread; bool gun;
        public Crosshair() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        public void Set(float s, bool isGun, bool scoped)
        {
            if (Mathf.Abs(s - spread) < 0.05f && isGun == gun) return;
            spread = s; gun = isGun; MarkDirtyRepaint();
        }
        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D; float c = contentRect.width / 2f;
            p.strokeColor = new Color(1f, 1f, 1f, 0.9f); p.lineWidth = 2.2f; p.lineCap = LineCap.Round;
            if (!gun) { p.fillColor = new Color(1f, 1f, 1f, 0.9f); p.BeginPath(); p.Arc(new Vector2(c, c), 3f, 0f, 360f); p.Fill(); return; }
            float gap = 5f + spread * 5f, len = 9f;
            p.BeginPath(); p.MoveTo(new Vector2(c, c - gap)); p.LineTo(new Vector2(c, c - gap - len)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c, c + gap)); p.LineTo(new Vector2(c, c + gap + len)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c - gap, c)); p.LineTo(new Vector2(c - gap - len, c)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c + gap, c)); p.LineTo(new Vector2(c + gap + len, c)); p.Stroke();
        }
    }

    // Оптика 4×: чёрное поле с круглым окном и перекрестьем
    public class ScopeOverlay : VisualElement
    {
        public ScopeOverlay() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect; if (r.width <= 0f) return;
            var p = ctx.painter2D; var c = r.center; float rad = r.height * 0.42f;
            p.fillColor = new Color(0f, 0f, 0f, 0.96f);
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath();
            p.MoveTo(c + new Vector2(rad, 0)); p.Arc(c, rad, 0f, 360f); p.ClosePath();
            p.Fill(FillRule.OddEven);
            p.strokeColor = new Color(0f, 0f, 0f, 0.9f); p.lineWidth = 2f;
            p.BeginPath(); p.MoveTo(new Vector2(c.x - rad, c.y)); p.LineTo(new Vector2(c.x + rad, c.y)); p.Stroke();
            p.BeginPath(); p.MoveTo(new Vector2(c.x, c.y - rad)); p.LineTo(new Vector2(c.x, c.y + rad)); p.Stroke();
            p.strokeColor = new Color(1f, 0.2f, 0.2f, 0.9f); p.fillColor = new Color(1f, 0.2f, 0.2f, 0.9f);
            p.BeginPath(); p.Arc(c, 2.5f, 0f, 360f); p.Fill();
        }
    }
}
