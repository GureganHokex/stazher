# Модель баланса обеда и работы: сколько монет за 30 игровых дней и когда покупается каждое оружие.
# Читает те же данные, что игра: Resources/Balance/lunch.json и задачи Resources/Tasks/tracks/*.json.
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
        if done >= len(tasks):  # направление пройдено: дальше задачи из генератора уровня Middle
            work += max(0, n - len(today)) * 130
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
        rows.append({"день": day, "грейд": GRADE_NAMES[min(grade, 3)], "задач": len(today), "монеты за работу": work,
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
    print("Таблица по дням:", out)
    return 0


if __name__ == "__main__":
    sys.exit(main())
