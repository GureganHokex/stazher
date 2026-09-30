// Все цифры обеда в одном месте: оружие, улучшения, обвесы, расходники, горожане, лимиты.
// Источник правды — Resources/Balance/lunch.json (его же читает модель баланса Tools/Balance/model.py).
// Если файла нет или он битый, берутся значения по умолчанию ниже (они совпадают с файлом).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public enum WeaponKind { Melee, Pistol, Smg, Shotgun, Rifle, Sniper, Mg }

    [Serializable]
    public class WeaponDef
    {
        public string id, name, kind, perk, about;
        public int grade;            // 0 Стажёр, 1 Junior, 2 Junior+, 3 Middle
        public int price;
        public int damage;
        public int pellets = 1;      // дробовик — 12 дробин
        public float rate = 2f;      // ударов или выстрелов в секунду
        public int mag;              // патронов в магазине (0 — ближний бой)
        public string ammo = "";     // тип патронов
        public float reload = 1.5f;  // секунд на перезарядку
        public float spread = 1f;    // разброс от бедра, градусы
        public float recoil = 1f;    // подброс камеры, градусы
        public float reach = 1.2f;   // дальность удара ближнего боя
        public float range = 90f;    // дальность выстрела
        public float zoom = 1f;      // встроенная оптика (снайперка — 4)
        public bool auto;            // стреляет, пока зажата ЛКМ
        public float knockback;      // отбрасывание (бита)

        public WeaponKind Kind { get { try { return (WeaponKind)Enum.Parse(typeof(WeaponKind), kind, true); } catch { return WeaponKind.Melee; } } }
        public bool Melee { get { return Kind == WeaponKind.Melee; } }
        public int Slot { get { return Melee ? 0 : 1; } }
    }

    [Serializable]
    public class AttachDef
    {
        public string id, name, slot, about;   // slot: sight | barrel | mag | rail
        public int grade, price;
        public float hipSpread = 1f, adsSpread = 1f, recoil = 1f, magMult = 1f, reloadMult = 1f, zoom = 1f, noise = 0f;
    }

    [Serializable]
    public class AmmoDef { public string id, name; public int count, price; }

    [Serializable]
    public class CitizenDef
    {
        public string id, name, about;
        public bool humanitarian;
        public int hp;
        public float walk = 1.2f, run = 3.6f;
        public int damage;           // урон, если нападает
        public string color = "FF4F9A";
        public string street = "";   // где чаще встречается: law, book, art, uni, square
        public float weight = 1f;    // частота появления
    }

    [Serializable]
    public class BalanceData
    {
        // лимиты обеда
        public int cap = 60, maxHumanitarians = 18, maxTechies = 8, reward = 10, penalty = 20;
        public float spawnEvery = 8f, noticeRange = 12f, hearRange = 25f, silencedHear = 8f;
        public int playerHp = 100;
        public float deanChance = 0.3f; public int deanHp = 300; public float couponDiscount = 0.2f;
        public float knockedLose = 0.5f;
        public int hallBonus = 40;   // за зачистку дома, куда можно войти (спринт 6 версии 0.9)
        // улучшения: доли цены за уровни 1..5, прибавка урона за уровень
        public float[] upgradeCost = { 0.2f, 0.3f, 0.45f, 0.65f, 0.9f };
        public float upgradeDamage = 0.1f, fastReload = 0.7f;
        public int knifeUpgradeBase = 300;
        // худи
        public int hoodiePrice = 1000, hoodieGrade = 1; public float hoodieArmor = 0.25f;
        // еда
        public int shawarmaPrice = 30, shawarmaHeal = 50, energyPrice = 25; public float energySpeed = 0.2f, energySeconds = 60f;

        public WeaponDef[] weapons;
        public AttachDef[] attachments;
        public AmmoDef[] ammo;
        public CitizenDef[] citizens;
    }

    public static class Balance
    {
        static BalanceData data;
        public static string Source = "код";

        public static BalanceData D { get { if (data == null) Load(); return data; } }

        public static void Load()
        {
            data = Defaults();
            try
            {
                var ta = Resources.Load<TextAsset>("Balance/lunch");
                if (ta != null)
                {
                    var fromFile = JsonUtility.FromJson<BalanceData>(ta.text);
                    if (fromFile != null && fromFile.weapons != null && fromFile.weapons.Length > 0 && fromFile.citizens != null && fromFile.citizens.Length > 0)
                    { data = fromFile; Source = "Resources/Balance/lunch.json"; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Стажёр] lunch.json не прочитан, беру значения по умолчанию: " + e.Message); }
        }

#if UNITY_EDITOR
        // В редакторе: если файла баланса ещё нет — выгрузить значения по умолчанию, чтобы было что править
        public static void ExportIfMissing()
        {
            try
            {
                if (Resources.Load<TextAsset>("Balance/lunch") != null) return;
                string dir = System.IO.Path.Combine(Application.dataPath, "Intern 3/Assets/Intern/Resources/Balance");
                if (!System.IO.Directory.Exists(System.IO.Path.Combine(Application.dataPath, "Intern 3/Assets/Intern/Resources"))) return;
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "lunch.json"), JsonUtility.ToJson(Defaults(), true), new System.Text.UTF8Encoding(false));
                UnityEditor.AssetDatabase.Refresh();
                Debug.Log("[Стажёр] Выгружен Resources/Balance/lunch.json");
            }
            catch (Exception e) { Debug.LogWarning("[Стажёр] lunch.json не выгружен: " + e.Message); }
        }
#endif

        public static WeaponDef Weapon(string id) { foreach (var w in D.weapons) if (w.id == id) return w; return null; }
        public static AttachDef Attach(string id) { foreach (var a in D.attachments) if (a.id == id) return a; return null; }
        public static AmmoDef Ammo(string id) { foreach (var a in D.ammo) if (a.id == id) return a; return null; }
        public static CitizenDef Citizen(string id) { foreach (var c in D.citizens) if (c.id == id) return c; return null; }

        // Цена следующего уровня улучшения (level — текущий, 0..4)
        public static int UpgradePrice(WeaponDef w, int level)
        {
            if (level < 0 || level >= D.upgradeCost.Length) return 0;
            int basePrice = w.price > 0 ? w.price : D.knifeUpgradeBase;
            return Mathf.RoundToInt(basePrice * D.upgradeCost[level] / 10f) * 10;
        }

        public static readonly string[] GradeNames = { "Стажёр", "Junior", "Junior+", "Middle" };
        public static string GradeName(int g) { return GradeNames[Mathf.Clamp(g, 0, 3)]; }

        public static BalanceData Defaults()
        {
            var d = new BalanceData();
            d.weapons = new[]
            {
                W("knife", "Нож «Канцелярский»", "melee", 0, 0, 25, 2.2f, 0, "", 0f, "метать (ПКМ)", "Бесшумный, два удара в секунду.", reach: 1.1f),
                W("bat", "Бита «Ctrl+Z»", "melee", 0, 400, 45, 1.4f, 0, "", 0f, "удар сбивает с ног", "Отбрасывает. Бесшумная.", reach: 1.4f, knock: 1.6f),
                W("pistol", "Пистолет «Hello World»", "pistol", 0, 700, 30, 3.5f, 8, "pistol", 1.1f, "пуля пробивает насквозь", "8 патронов, точный.", spread: 1.2f, recoil: 1.6f),
                W("katana", "Катана «Рефакторинг»", "melee", 1, 1500, 70, 1.6f, 0, "", 0f, "рывок длиннее", "Рывок вперёд на ПКМ.", reach: 1.7f),
                W("smg", "ПП «Fork»", "smg", 1, 1800, 16, 10f, 30, "auto", 1.6f, "+10 патронов в магазине", "10 выстрелов в секунду.", spread: 3.2f, recoil: 0.7f, auto: true),
                W("shotgun", "Дробовик «Hotfix»", "shotgun", 2, 3200, 8, 1.2f, 6, "shell", 2.2f, "кучнее дробь", "12 дробин, в упор кладёт любого.", spread: 6.5f, recoil: 4f, pellets: 12, range: 30f),
                W("rifle", "Автомат «Pipeline»", "rifle", 2, 4200, 26, 8f, 30, "auto", 1.8f, "без отдачи", "8 выстрелов в секунду.", spread: 2.2f, recoil: 0.9f, auto: true),
                W("sniper", "Винтовка «Prod Deploy»", "sniper", 3, 6000, 90, 0.9f, 5, "rifle", 2.4f, "пробивает насквозь", "5 патронов, оптика 4×.", spread: 0.3f, recoil: 6f, range: 160f, zoom: 4f),
                W("mg", "Пулемёт «DDoS»", "mg", 3, 8000, 24, 12f, 100, "belt", 3.5f, "не перегревается", "12 выстрелов в секунду, лента на 100.", spread: 3.6f, recoil: 0.8f, auto: true),
            };
            d.attachments = new[]
            {
                A("laser", "Лазер «Pointer»", "rail", 0, 300, "Точнее стрельба от бедра.", hip: 0.6f),
                A("reddot", "Коллиматор «Debug»", "sight", 0, 350, "+15% точности в прицеле.", ads: 0.85f, zoom: 1.35f),
                A("grip", "Рукоять «Stable Release»", "rail", 1, 500, "−30% отдачи.", recoil: 0.7f),
                A("buffer", "Магазин «Buffer»", "mag", 1, 600, "+50% патронов.", mag: 1.5f),
                A("silencer", "Глушитель «Silent Mode»", "barrel", 1, 900, "Выстрел слышно за 8 м вместо 25.", noise: 8f),
                A("profiler", "Оптика «Profiler» 4×", "sight", 2, 1400, "Приближение в 4 раза.", zoom: 4f),
                A("heap", "Барабан «Heap»", "mag", 2, 1800, "+150% патронов, перезарядка дольше.", mag: 2.5f, reload: 1.4f),
            };
            d.ammo = new[]
            {
                new AmmoDef { id = "pistol", name = "Патроны для пистолета", count = 30, price = 10 },
                new AmmoDef { id = "auto", name = "Патроны для ПП и автомата", count = 60, price = 20 },
                new AmmoDef { id = "shell", name = "Дробь", count = 12, price = 15 },
                new AmmoDef { id = "rifle", name = "Винтовочные", count = 10, price = 20 },
                new AmmoDef { id = "belt", name = "Лента для пулемёта", count = 100, price = 30 },
            };
            d.citizens = new[]
            {
                C("lawyer", "Юрист", true, 60, 1.2f, 4.4f, 15, "FF4F9A", "law", 1.4f, "Убегает и звонит коллегам: через 10 с прибегает второй юрист с портфелем."),
                C("notary", "Нотариус", true, 80, 0.9f, 2.2f, 10, "E8782F", "law", 0.9f, "Медленный, не убегает. Кидает печать: урон и оглушение на 1 с."),
                C("philologist", "Филолог", true, 40, 1.2f, 4.0f, 0, "7A6FC4", "book", 1f, "Убегает зигзагом и на бегу поправляет ударения."),
                C("journalist", "Журналист", true, 50, 1.3f, 3.8f, 0, "D9A441", "book", 0.9f, "Держит дистанцию и снимает: пока снимает, остальные замечают тебя вдвое дальше."),
                C("philosopher", "Философ", true, 70, 0.8f, 2.6f, 8, "7FB58A", "uni", 0.9f, "Стоит и рассуждает. «А зачем?» замедляет на 2 с."),
                C("poet", "Поэт", true, 40, 1.4f, 5.6f, 0, "E0567F", "art", 1f, "Самый быстрый. На его стихи сбегаются соседи."),
                C("historian", "Историк", true, 60, 1.1f, 3.6f, 0, "8C6A4F", "uni", 0.9f, "Прячется в архиве, выманить можно шумом."),
                C("critic", "Искусствовед", true, 50, 1.2f, 3.9f, 0, "92A7D2", "art", 0.8f, "Ходит группами по 3, группа разбегается в разные стороны."),
                C("accountant", "Бухгалтер", false, 9999, 1.2f, 3.4f, 0, "89D185", "square", 1f, "Калькулятор. Трогать нельзя: штраф 20."),
                C("engineer", "Инженер", false, 9999, 1.1f, 3.2f, 0, "89D185", "square", 1f, "Каска. Трогать нельзя: штраф 20."),
                C("cashier", "Кассир", false, 9999, 1.2f, 3.4f, 0, "89D185", "square", 1f, "Бейдж. Трогать нельзя: штраф 20."),
                C("itguy", "Коллега-айтишник", false, 9999, 1.3f, 3.6f, 0, "89D185", "square", 1f, "Ноутбук. Трогать нельзя: штраф 20."),
                C("dean", "Декан гумфака", true, 300, 0.9f, 2.8f, 20, "FFD23F", "uni", 0f, "Редкий гость: 300 здоровья, купон −20% в мастерской."),
            };
            return d;
        }

        static WeaponDef W(string id, string name, string kind, int grade, int price, int dmg, float rate, int mag, string ammo, float reload, string perk, string about,
                           float reach = 1.2f, float knock = 0f, float spread = 1f, float recoil = 1f, bool auto = false, int pellets = 1, float range = 90f, float zoom = 1f)
        {
            return new WeaponDef { id = id, name = name, kind = kind, grade = grade, price = price, damage = dmg, rate = rate, mag = mag, ammo = ammo, reload = reload,
                                   perk = perk, about = about, reach = reach, knockback = knock, spread = spread, recoil = recoil, auto = auto, pellets = pellets, range = range, zoom = zoom };
        }

        static AttachDef A(string id, string name, string slot, int grade, int price, string about,
                           float hip = 1f, float ads = 1f, float recoil = 1f, float mag = 1f, float reload = 1f, float zoom = 1f, float noise = 0f)
        {
            return new AttachDef { id = id, name = name, slot = slot, grade = grade, price = price, about = about,
                                   hipSpread = hip, adsSpread = ads, recoil = recoil, magMult = mag, reloadMult = reload, zoom = zoom, noise = noise };
        }

        static CitizenDef C(string id, string name, bool hum, int hp, float walk, float run, int dmg, string color, string street, float weight, string about)
        {
            return new CitizenDef { id = id, name = name, humanitarian = hum, hp = hp, walk = walk, run = run, damage = dmg, color = color, street = street, weight = weight, about = about };
        }
    }
}
