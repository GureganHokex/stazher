// Спринт 5 «Без потолка»: уровень игрока по опыту, титулы после Middle и смена компании.
// Чистая логика без Unity-объектов — её гоняет и selftest.
using System;
using UnityEngine;

namespace Intern.Game
{
    public static class Levels
    {
        // Сколько опыта стоит переход с уровня n на n+1
        public static int Cost(int level) { return 200 + 20 * Mathf.Max(1, level); }

        // Сколько опыта всего нужно, чтобы стоять на уровне level (уровень 1 — с нуля)
        public static int TotalFor(int level)
        {
            level = Mathf.Max(1, level);
            return 200 * (level - 1) + 10 * level * (level - 1);
        }

        public static int Of(int xp)
        {
            if (xp <= 0) return 1;
            // приближение из квадратного уравнения, потом доводка — без цикла по всем уровням
            int l = Mathf.Max(1, (int)((-190 + Math.Sqrt(190.0 * 190.0 + 40.0 * (xp + 200))) / 20.0));
            while (l > 1 && TotalFor(l) > xp) l--;
            while (TotalFor(l + 1) <= xp) l++;
            return l;
        }

        // Прогресс внутри уровня: сколько набрано и сколько нужно до следующего
        public static void Progress(int xp, out int level, out int into, out int need)
        {
            level = Of(xp);
            into = xp - TotalFor(level);
            need = Cost(level);
        }

        // ---------- титулы после Middle ----------
        public static readonly int[] TitleAt = { 30, 40, 50, 60 };
        public static readonly string[] TitleNames = { "Senior", "Lead", "Principal", "Architect" };
        public const int StarStep = 10;   // после Architect — звезда каждые 10 уровней

        // −1 — ещё Middle; 0..3 — Senior … Architect
        public static int TitleIndex(int level)
        {
            int ti = -1;
            for (int i = 0; i < TitleAt.Length; i++) if (level >= TitleAt[i]) ti = i;
            return ti;
        }

        public static int Stars(int level) { return level >= TitleAt[TitleAt.Length - 1] ? (level - TitleAt[TitleAt.Length - 1]) / StarStep : 0; }

        public static string Title(int level)
        {
            int ti = TitleIndex(level);
            if (ti < 0) return "Middle";
            int st = Stars(level);
            if (st <= 0) return TitleNames[ti];
            return TitleNames[ti] + " " + (st <= 5 ? new string('★', st) : "★×" + st);
        }

        // Бонус к монетам за задачи от титула: Senior +10%, Lead +20%, Principal +30%, Architect +40%, каждая ★ ещё +5%
        public static float TitleBonus(int level)
        {
            int ti = TitleIndex(level);
            return ti < 0 ? 0f : 0.1f * (ti + 1) + 0.05f * Stars(level);
        }

        // Следующая ступень: имя и уровень (для подсказок «до Senior ещё …»)
        public static string NextTitle(int level, out int at)
        {
            int ti = TitleIndex(level);
            if (ti + 1 < TitleAt.Length) { at = TitleAt[ti + 1]; return TitleNames[ti + 1]; }
            int last = TitleAt[TitleAt.Length - 1];
            at = last + (Stars(level) + 1) * StarStep;
            return Title(at);
        }

        // ---------- компании ----------
        public const int CompanyLevel = 60;           // сменить компанию можно с Architect
        public const float CompanyBonus = 0.1f;       // +10% монет за каждую смену
        static readonly string[] Companies = { "Кодзилла Софт", "ОблакоТех", "Байт и Ко", "Кибер-Пельмень", "Нейролампа", "Гигабайт Групп", "Квант Код" };
        public static string CompanyName(int n) { return n >= 0 && n < Companies.Length ? Companies[n] : "Компания №" + (n + 1); }
    }

    // Звёзды темы: ★ — тема сдана, ★★ — 5 задач из генератора без подсказок, ★★★ — ещё и 10 подряд вовремя
    public static class TopicStars
    {
        public const int Clean = 5, Streak = 10, Bonus2 = 50, Bonus3 = 150;

        public static int Of(TopicStat st, bool topicDone)
        {
            if (!topicDone) return 0;
            if (st == null || st.clean < Clean) return 1;
            return st.best >= Streak ? 3 : 2;
        }

        // Учесть сданную задачу из генератора. Возвращает новую звезду (2 или 3), если она только что получена, иначе 0
        public static int Record(TopicStat st, bool clean, bool onTime)
        {
            st.solved++;
            if (clean) st.clean++;
            st.streak = onTime ? st.streak + 1 : 0;
            st.best = Math.Max(st.best, st.streak);
            int stars = Of(st, true), got = 0;
            if (stars > st.stars && stars >= 2) got = stars;
            st.stars = Math.Max(st.stars, stars);
            return got;
        }

        public static int BonusFor(int stars) { return stars == 2 ? Bonus2 : stars == 3 ? Bonus3 : 0; }
    }
}
