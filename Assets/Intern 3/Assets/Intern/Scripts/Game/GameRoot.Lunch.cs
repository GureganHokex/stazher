// GameRoot: обеденный перерыв — выход в город, бой, магазины, итоги обеда, арсенал у шкафчика в офисе.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public partial class GameRoot
    {
        public LunchReport LastLunch { get; private set; }
        public PlayerCombat Combat { get { return combat; } }
        public Arsenal Arsenal { get { return arsenal; } }
        public string ShopKind { get; private set; }
        public string ShopTitle { get; private set; }
        public int ShopVersion { get; private set; }     // меняется после покупки — интерфейс перерисовывает витрину
        Mode shopFrom = Mode.Lunch;

        void SetupArsenal(GameObject playerGo)
        {
            arsenal = new Arsenal(Save);
            if (combat == null) combat = playerGo.AddComponent<PlayerCombat>();
            combat.Init(player, arsenal);
        }

        // Худи «Dark Theme» носится и в офисе
        public Appearance LookNow()
        {
            if (Save.look == null) Save.look = new Appearance();
            if (!(Save.hoodie && Save.hoodieOn)) return Save.look;
            var a = Save.look.Clone(); a.top = 1; a.topColor = 8; return a;
        }

        // ================== выход и возврат ==================
        public void TryStartLunch()
        {
            if (mode != Mode.Walk) return;
            string why;
            if (!Work.CanLunch(out why)) { Toast(why); return; }
            Work.StartLunch(); Persist();
            if (FiredReport != null) return;      // самоволка оказалась последней каплей
            if (leadWalker != null) leadWalker.ResetHome();
            TutEvent("lunch");
            StartCoroutine(ToCity());
        }

        IEnumerator ToCity()
        {
            mode = Mode.Transition;
            yield return Fade(1f, 0.35f);
            if (city == null)
            {
                float t0 = Time.realtimeSinceStartup;
                city = CityBuilder.Build();
                city.Prewarm(new[] { "Dev1", "Dev2", "Dev3", "Dev4" }, 12);   // тела горожан про запас, пока экран тёмный
                Debug.Log("[Стажёр] Город построен за " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " мс: дверей " + city.doors.Count + ", узлов " + city.nodes.Count + ", баланс: " + Balance.Source);
            }
            city.root.gameObject.SetActive(true);
            Gore.Enabled = GameConfig.S.blood;
            bool training = Tutorial != null && Tutorial.Active && Tutorial.Step == Tut.FirstKills;
            lunch = new LunchRun(city, DayLength.LunchSeconds(GameConfig.S.dayLength), () => player.Position, () => player.cam.transform, Save.hoodie && Save.hoodieOn, training)
            { Say = Toast, Coupon = t => { Save.coupons++; Toast(t); } };
            player.Teleport(city.spawn.position, 0f); player.FaceCameraYaw(0f);
            player.cinematic = false; player.avatar.SetHeadVisible(!player.firstPerson);
            WeaponModel.Golden = PathComplete && Levels.TitleIndex(Level) >= 3;   // перк Architect: золотое оружие
            combat.Begin(lunch);
            mode = Mode.Lunch; SetCursor(true); lastInput = Time.unscaledTime;
            Toast("Обед! " + Mathf.RoundToInt(lunch.duration / 60f) + " минут. Гуманитарий +10 монет, технарей не трогать: штраф 20.");
            if (arsenal.Gun == null && Save.totalLunches <= 2) Toast("Оружейная «Железо» и ларёк с патронами — на площади, прямо по проспекту.");
            yield return Fade(0f, 0.35f);
        }

        public void EndLunch(bool timeUp)
        {
            if ((mode != Mode.Lunch && mode != Mode.Shop) || lunch == null) return;
            StartCoroutine(ToOffice(!timeUp && !lunch.knockedOut));
        }

        IEnumerator ToOffice(bool early)
        {
            mode = Mode.Transition;
            bool ko = lunch.knockedOut;
            if (ko) Toast("Тебя вырубили! Половина монет обеда потеряна, «Сытый» сгорел.");
            yield return Fade(1f, ko ? 0.8f : 0.35f);
            FinishLunchNow(early);
            // итоги поверх офиса: камера за стажёром у двери
            player.cinematic = false; player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.LunchSummary; SetCursor(false);
            yield return Fade(0f, 0.35f);
        }

        public void UiLunchSummaryDone()
        {
            if (mode != Mode.LunchSummary) return;
            mode = Mode.Walk; SetCursor(true); lastInput = Time.unscaledTime;
            if (LastLunch != null && !LastLunch.knockedOut) Toast("«Сытый»: +10% XP за задачи до " + WorkDay.TimeText(Save.satedUntil) + ".");
        }

        // Закончить обед сразу (конец таймера, выход в меню или из игры): монеты — в кошелёк, стажёр — в офис
        void FinishLunchNow(bool early = false)
        {
            if (lunch == null) return;
            var rep = lunch.Report(early);
            Debug.Log("[Стажёр] Обед: тел в пуле " + (city != null ? city.Pooled : 0) + ", " + lunch.PerfLine() + ", выбито " + lunch.kills + ", убежали " + lunch.escaped + ", монет " + lunch.coins);
            int coins = lunch.coins;
            if (lunch.knockedOut && coins > 0) { rep.lost = Mathf.RoundToInt(coins * Balance.D.knockedLose); coins -= rep.lost; }
            rep.coins = coins;
            Save.money = Mathf.Max(0, Save.money + (coins > 0 ? Work.Earn(coins) : coins));
            Work.EndLunch(coins, lunch.kills);
            if (lunch.knockedOut) { Save.satedUntil = 0f; Save.knockouts++; }
            Save.bestLunch = Mathf.Max(Save.bestLunch, coins); Save.bestSeries = Mathf.Max(Save.bestSeries, rep.bestSeries);
            LastLunch = rep;
            lunch.Cleanup(); lunch = null;
            if (combat != null) combat.End();
            if (city != null) city.root.gameObject.SetActive(false);
            if (exitSpot != null) player.Teleport(exitSpot.position, 0f); else player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.FaceCameraYaw(0f);
            TutEvent("lunchEnd");
            Persist(); UpdateBoard();
        }

        // ================== кадр обеда ==================
        void LunchUpdate()
        {
            player.Tick(true);
            FindFocus();
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (focus != null && InputX.Interact()) focus.Interact(this);
            if (!locked && InputX.Click()) SetCursor(true);
            if (InputX.ToggleView()) { player.ToggleView(); Save.firstPerson = player.firstPerson; Persist(); }
            if (combat != null) combat.Tick(Time.deltaTime, locked && mode == Mode.Lunch);
            LunchClock();
            if (mode == Mode.Lunch && InputX.Esc()) PauseFrom(Mode.Lunch);
        }

        void LunchClock()
        {
            if (lunch == null) return;
            lunch.Tick(Time.deltaTime);
            Work.Advance(Time.deltaTime * 60f / lunch.duration, false);
            if (lunch.Over) EndLunch(!lunch.knockedOut);
        }

        // Магазин: таймер обеда идёт, стажёр стоит у прилавка
        void ShopUpdate()
        {
            player.Tick(false);
            if (lunch != null) { if (combat != null) combat.Tick(Time.deltaTime, false); LunchClock(); }
            if (mode == Mode.Shop && InputX.Esc()) CloseShop();
        }

        // ================== магазины ==================
        public void OpenCityShop(string kind, string title)
        {
            if (mode != Mode.Lunch && mode != Mode.Walk) return;
            shopFrom = mode; ShopKind = kind; ShopTitle = title; ShopVersion++;
            mode = Mode.Shop; SetCursor(false);
            if (player.avatar != null) player.avatar.moveSpeed = 0f;
        }

        public void CloseShop()
        {
            if (mode != Mode.Shop) return;
            mode = shopFrom == Mode.Lunch && lunch != null ? Mode.Lunch : Mode.Walk;
            SetCursor(true); lastInput = Time.unscaledTime;
            if (combat != null && lunch != null) combat.Rebuild();
            if (lunch == null && player != null) player.SetAvatar(LookNow());
        }

        // Деньги в кармане: в городе — ещё и монеты этого обеда
        public int Wallet { get { return Save.money + (lunch != null ? Mathf.Max(0, lunch.coins) : 0); } }
        public int GradeForShop { get { return debugAllGrades ? 3 : Mathf.Min(GradeIdx, 3); } }
        bool debugAllGrades;   // только в редакторе: F5 на обеде открывает всё оружие

        bool Pay(int price)
        {
            if (price > Wallet) { Toast("Не хватает монет: нужно " + price + ", есть " + Wallet + "."); return false; }
            int rest = lunch != null ? lunch.SpendFromLunch(price) : price;
            Save.money -= rest; ShopVersion++; Persist();
            return true;
        }

        // Купон декана действует в мастерской «Патч»
        public int PatchPrice(int price) { return Save.coupons > 0 ? Mathf.RoundToInt(price * (1f - Balance.D.couponDiscount) / 10f) * 10 : price; }
        void UseCoupon(int full, int paid) { if (Save.coupons > 0 && paid < full) { Save.coupons--; Toast("Купон декана использован: −" + (full - paid) + " монет."); } }

        public void BuyWeapon(string id)
        {
            var w = Balance.Weapon(id); if (w == null || arsenal.Owns(id)) return;
            if (GradeForShop < w.grade) { Toast("Нужен грейд " + Balance.GradeName(w.grade) + "."); return; }
            if (!Pay(w.price)) return;
            arsenal.Add(id); Persist();
            Toast("Куплено: " + w.name + (w.Melee ? "" : " и два магазина патронов") + ".");
        }

        public void EquipWeapon(string id) { arsenal.Equip(id); ShopVersion++; Persist(); }

        public void UpgradeWeapon(string id)
        {
            var w = Balance.Weapon(id); var s = arsenal.Get(id); if (w == null || s == null || s.level >= 5) return;
            int full = Balance.UpgradePrice(w, s.level), price = PatchPrice(full);
            if (!Pay(price)) return;
            UseCoupon(full, price);
            s.level++; Persist();
            Toast(w.name + ": уровень " + s.level + (s.level == 3 ? " — быстрая перезарядка" : s.level == 5 ? " — перк: " + w.perk : " — урон +10%") + ".");
        }

        public void BuyAttach(string id)
        {
            var a = Balance.Attach(id); if (a == null || arsenal.HasAttach(id)) return;
            if (GradeForShop < a.grade) { Toast("Нужен грейд " + Balance.GradeName(a.grade) + "."); return; }
            int price = PatchPrice(a.price);
            if (!Pay(price)) return;
            UseCoupon(a.price, price);
            Save.attachments.Add(id);
            // сразу ставим на текущий ствол, если слот свободен
            var gun = arsenal.Gun; var gs = gun != null ? arsenal.Get(gun.id) : null;
            if (gs != null && string.IsNullOrEmpty(arsenal.Installed(gs, a.slot))) arsenal.Install(gs, a.slot, id);
            Persist(); Toast("Куплено: " + a.name + ".");
        }

        public void InstallAttach(string weaponId, string slot, string attachId)
        {
            var s = arsenal.Get(weaponId); if (s == null) return;
            arsenal.Install(s, slot, arsenal.Installed(s, slot) == attachId ? "" : attachId);
            ShopVersion++; Persist();
        }

        public void BuyAmmo(string id)
        {
            var a = Balance.Ammo(id); if (a == null) return;
            if (!Pay(a.price)) return;
            arsenal.AddAmmo(id, a.count); Persist();
        }

        public void BuyFood(string what)
        {
            var B = Balance.D;
            if (lunch == null) return;
            if (what == "shawarma") { if (lunch.hp >= lunch.maxHp) { Toast("Здоровье и так полное."); return; } if (Pay(B.shawarmaPrice)) { lunch.Heal(B.shawarmaHeal); Toast("Шаурма: +" + B.shawarmaHeal + " здоровья."); } }
            else if (what == "energy") { if (Pay(B.energyPrice)) { lunch.Energy(); Toast("Энергетик: +20% скорости на минуту."); } }
        }

        public void BuyHoodie()
        {
            var B = Balance.D;
            if (Save.hoodie) return;
            if (GradeForShop < B.hoodieGrade) { Toast("Нужен грейд " + Balance.GradeName(B.hoodieGrade) + "."); return; }
            if (!Pay(B.hoodiePrice)) return;
            Save.hoodie = Save.hoodieOn = true; Persist();
            if (lunch != null) lunch.hoodie = true;
            player.SetAvatar(LookNow()); if (combat != null && lunch != null) combat.Rebuild();
            Toast("Худи «Dark Theme»: −25% входящего урона. Снять можно в арсенале у шкафчика.");
        }

        public void ToggleHoodie()
        {
            if (!Save.hoodie) return;
            Save.hoodieOn = !Save.hoodieOn; ShopVersion++; Persist();
            if (lunch != null) lunch.hoodie = Save.hoodieOn;
            player.SetAvatar(LookNow()); if (combat != null && lunch != null) combat.Rebuild();
        }

#if UNITY_EDITOR
        int debugShop;
        // Только в редакторе: F1 — к следующему прилавку, F5 — +3000 монет
        void DebugLunch()
        {
            if (InputX.DebugDoor() && city != null)
            {
                var shops = city.root.GetComponentsInChildren<CityShop>();
                if (shops.Length == 0) return;
                var s = shops[debugShop++ % shops.Length];
                var c = s.GetComponent<Collider>().bounds.center; c.y = 0;
                var best = s.front; best.y = 0;
                var look = c - best; float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
                player.Teleport(best + Vector3.up * 0.1f, yaw); player.FaceCameraYaw(yaw);
                Debug.Log("[Стажёр] F1: у прилавка " + s.title);
            }
            if (InputX.DebugLead())
            {
                Save.money += 3000; debugAllGrades = true;
                foreach (var w in Save.arsenal) w.level = Mathf.Max(w.level, 5);   // уровень 5 — перки: метание ножа, длинный рывок катаны
                if (combat != null) combat.Rebuild();
                Toast("Отладка: +3000 монет, всё оружие открыто, купленное — уровень 5");
            }
        }
#else
        void DebugLunch() { }
#endif

        // ================== арсенал у шкафчика в офисе ==================
        void PlaceArsenalCase()
        {
            var at = refs.lockerSpot != null ? refs.lockerSpot.position : new Vector3(-8f, 0, -6.1f);
            var root = new GameObject("ArsenalCase").transform;
            root.position = new Vector3(at.x + 1.6f, 0, at.z - 1.1f);
            Look.RBox("CaseBody", root, new Vector3(0, 0.55f, 0), new Vector3(0.9f, 1.1f, 0.45f), Pal.Hex("2B2D42"), 0.05f, true, 0.6f);
            Look.RBox("CaseLid", root, new Vector3(0, 1.12f, 0), new Vector3(0.95f, 0.06f, 0.5f), Pal.Hex("C8453A"), 0.02f, false, 0.6f);
            Look.RBox("CaseGun", root, new Vector3(0, 0.75f, 0.23f), new Vector3(0.7f, 0.1f, 0.03f), Pal.Hex("8A8FA8"), 0.02f, false, 0.6f);
            var tag = OfficeBuilder.Label("Арсенал", new Vector3(0, 1.5f, 0), 0.014f, Pal.Ink, root);
            tag.gameObject.AddComponent<Billboard>();
            root.GetComponentInChildren<BoxCollider>().gameObject.AddComponent<ArsenalCase>();
        }
    }

    // Кейс у шкафчика: обвесы, выбор оружия, худи — между обедами
    public class ArsenalCase : Interactable
    {
        public override string Prompt { get { return "[E] Арсенал: оружие и обвесы"; } }
        public override void Interact(GameRoot g) { g.OpenCityShop("arsenal", "Арсенал"); }
    }
}
