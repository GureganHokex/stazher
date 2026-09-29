# Модель баланса обеда и работы: сколько монет за 30 игровых дней и когда покупается каждое оружие,
# а после конца пути — темп уровней и титулов (Senior, Lead, Principal, Architect) на тикетах дня.
# Читает те же данные, что игра: Resources/Balance/lunch.json и задачи Resources/Tasks/tracks/*.json.
# Формулы уровней и тикетов повторяют Scripts/Game/Levels.cs; selftest игры (BalanceSim) считает
# дни до титулов тем же способом — числа должны совпадать.
# Запуск из корня репозитория:  python Tools/Balance/model.py [--days 30] [--track backend]
# Пишет таблицу по дням в Tools/Balance/model_out.csv и сводку в консоль.
import argparse, csv, json, os, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Assets", "Intern 3", "Assets", "Intern", "Resources")
GRADES = ["intern", "junior", "junior_plus", "middle"]
GRADE_NAMES = ["Стажёр", "Junior", "Junior+", "Middle"]

# Допущения модели (из плана): задач в день по грейдам и выбитых за обед лучшим оружием
TASKS_PER_DAY = [12, 10, 8, 7]
KILLS = {"knife": 22, "bat": 26, "pistol": 32, "katana": 36, "smg": 42, "shotgun": 46, "rifle": 50, "sniper": 50, "mg": 56}
CONSUMABLES_SHARE = 0.12   # патроны и шаурма съедают 10–15% обеда
BUY_ORDER = ["bat", "pistol", "katana", "smg", "shotgun", "rifle", "sniper", "mg"]

# ---- уровни и тикеты дня (как в Levels.cs) ----
TITLES = [("Senior", 30), ("Lead", 40), ("Principal", 50), ("Architect", 60)]
TICKETS_PER_DAY, TICKET_SHARE, DAY_BONUS_SHARE = 7, 0.6, 0.2
TICKET_COINS_PER_XP = 2   # монеты за тикет — вдвое больше опыта, премия за день в монетах = её опыту


def level_cost(level): return 200 + 20 * max(1, level)
def total_for(level): level = max(1, level); return 200 * (level - 1) + 10 * level * (level - 1)


def level_of(xp):
    lv = 1
    while total_for(lv + 1) <= xp: lv += 1
    return lv


def round5(v): return max(5, int(round(v / 5.0)) * 5)
def basis(level): return level_cost(max(level, 20))


def ticket_xp(level, kind, difficulty, practice=False):
    xp = basis(level) * TICKET_SHARE / TICKETS_PER_DAY
    xp *= 0.75 if kind == "ask" else 1.15
    xp *= 0.8 + 0.1 * min(5, max(1, difficulty))
    if practice: xp *= 0.3
    return round5(xp)


def day_bonus(level): return round5(basis(level) * DAY_BONUS_SHARE)


def can_generate(t):
    # как TaskGen.CanGenerate: код на Python или JavaScript (с функцией), эталон строкой и тесты
    c = t.get("content") or {}
    if t.get("type") not in ("write_code", "find_bug") or c.get("options") or not isinstance(c.get("correct_answer"), str) or not c.get("test_cases"): return False
    return c.get("language") == "python" or (c.get("language") == "javascript" and bool(c.get("entry")))


def path_tasks(track):
    names = ["fullstack"] if track == "fullstack" else ["common", track]
    res = []
    for name in names: res += load_json(os.path.join(RES, "Tasks", "tracks", name + ".json")).get("tasks", [])
    return res


def tickets_day(level, src):
    """Опыт за день: все тикеты дня (среднее по исходным задачам, 60% «Почини баг») и премия за закрытый день."""
    mean = sum(0.6 * ticket_xp(level, "fix", t.get("difficulty", 1)) + 0.4 * ticket_xp(level, "ask", t.get("difficulty", 1)) for t in src) / len(src)
    return int(round(mean * TICKETS_PER_DAY)) + day_bonus(level)


def title_days(track):
    tasks = path_tasks(track)
    xp = sum(int(t.get("xp_reward", 20)) for t in tasks)
    start = level_of(xp)
    src = [t for t in tasks if can_generate(t)]
    days, day = {}, 0
    while day < 400 and len(days) < len(TITLES) and src:
        day += 1
        xp += tickets_day(level_of(xp), src)
        for name, lv in TITLES:
            if name not in days and level_of(xp) >= lv: days[name] = day
    return start, days


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def load_tasks(track):
    tasks = []
    for name in ["common", track]:
        data = load_json(os.path.join(RES, "Tasks", "tracks", name + ".json"))
        topics = {r.get("topic_id"): (GRADES.index(r.get("grade", "junior")) if r.get("grade") in GRADES else 1, i) for i, r in enumerate(data.get("roadmap", []))}
        for t in data.get("tasks", []):
            g, order = topics.get(t.get("topic_id"), (1, 999))
            if t.get("grade") in GRADES: g = GRADES.index(t["grade"])
            tasks.append((g, 0 if name == "common" else 1, order, int(t.get("xp_reward", 20))))
    tasks.sort()
    return [(g, xp) for g, _, _, xp in tasks]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--days", type=int, default=30)
    ap.add_argument("--track", default="backend")
    a = ap.parse_args()

    bal = load_json(os.path.join(RES, "Balance", "lunch.json"))
    weapons = {w["id"]: w for w in bal["weapons"]}
    reward, cap = bal.get("reward", 10), bal.get("cap", 60)
    tasks = load_tasks(a.track)
    print("Задач на пути %s: %d, из них по грейдам: %s" % (a.track, len(tasks), [sum(1 for g, _ in tasks if g == i) for i in range(4)]))

    src = [t for t in path_tasks(a.track) if can_generate(t)]
    xp_total = 0
    wallet, done, owned, best = 0, 0, ["knife"], "knife"
    total_work = total_lunch = 0
    bought_on = {}
    rows = []
    for day in range(1, a.days + 1):
        grade = tasks[done][0] if done < len(tasks) else 3
        n = TASKS_PER_DAY[min(grade, 3)]
        today = tasks[done:done + n]
        done += len(today)
        work = sum(xp for _, xp in today)
        xp_total += work
        if done >= len(tasks) and not today:  # направление пройдено: тикеты дня из генератора
            lv = level_of(xp_total)
            day_xp = tickets_day(lv, src)
            xp_total += day_xp
            work += (day_xp - day_bonus(lv)) * TICKET_COINS_PER_XP + day_bonus(lv)
        grade = tasks[done][0] if done < len(tasks) else 3
        kills = min(cap, KILLS.get(best, 22))
        lunch = int(round(kills * reward * (1 - CONSUMABLES_SHARE)))
        wallet += work + lunch
        total_work += work; total_lunch += lunch
        bought = []
        for wid in BUY_ORDER:
            w = weapons.get(wid)
            if not w or wid in owned or w["grade"] > grade: continue
            if wallet >= w["price"]:
                wallet -= w["price"]; owned.append(wid); bought.append(w["name"]); bought_on[wid] = day
                if KILLS.get(wid, 0) >= KILLS.get(best, 0): best = wid
            break   # копим на следующее по порядку
        rows.append({"день": day, "грейд": GRADE_NAMES[min(grade, 3)], "уровень": level_of(xp_total), "задач": len(today), "монеты за работу": work,
                     "выбито за обед": kills, "монеты за обед": lunch, "кошелёк": wallet, "куплено": "; ".join(bought)})

    out = os.path.join(os.path.dirname(__file__), "model_out.csv")
    with open(out, "w", encoding="utf-8-sig", newline="") as f:
        wr = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        wr.writeheader(); wr.writerows(rows)

    print("За %d дней: работа %d, обеды %d (%.0f%% от всего)" % (a.days, total_work, total_lunch, 100.0 * total_lunch / max(1, total_work + total_lunch)))
    for wid in BUY_ORDER:
        w = weapons.get(wid)
        if w: print("  %-26s %6d монет, грейд %-8s куплен: %s" % (w["name"], w["price"], GRADE_NAMES[w["grade"]], "день %d" % bought_on[wid] if wid in bought_on else "не успел"))
    print("Максимум за обед: %d монет (лимит %d гуманитариев × %d)" % (cap * reward, cap, reward))
    print("После конца пути (все тикеты дня каждый день; план для backend: Senior ~6, Lead ~16, Principal ~30, Architect ~45):")
    for tr in ["backend", "frontend", "devops", "fullstack"]:
        start, days = title_days(tr)
        print("  %-9s путь -> уровень %d; %s" % (tr, start, ", ".join("%s — день %d" % (n, days[n]) for n, _ in TITLES if n in days)))
    print("Таблица по дням:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
