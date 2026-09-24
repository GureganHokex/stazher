# Контент «Стажёра»: направления и задачи

Игра читает направления из `Assets/Intern 3/Assets/Intern/Resources/Tasks/tracks/*.json`
(common, backend, frontend, devops, fullstack). Формат — `{track, roadmap[], tasks[]}`:
темы с грейдами и зависимостями (`requires`) и задачи 7 типов. Подробные правила — `spec/FORMAT.md`.

Здесь лежит то, из чего эти файлы собраны. Все команды запускаются из корня репозитория.

## Что где

- `spec/FORMAT.md` — формат задачи, типы, правила ответов и проверок, XP, сложность, персонажи.
- `spec/roadmap.py` → `spec/roadmap.json` — дерево тем всех направлений (86 тем).
- `gen/*.py` — генераторы частей контента (common, b1–b5, f1–f5, d1–d4, fs) → `out/*.json`.
  `out/backend_b1.legacy.json` — 15 старых задач Python, перенесённых в Backend (вход для `gen/b1.py`).
- `spec/validate.py` — проверка части: схема, XP, баланс типов и персонажей, «угадывание ответа по форме»,
  и главное — исполнение: эталон проходит тесты, заготовка — нет (Python — подмножество игрового интерпретатора,
  JavaScript — node, SQL — sqlite3, YAML/TS/bash — синтаксис, регулярки — как в игре).
- `spec/merge.py` — сборка `out/*.json` в файлы направлений прямо в `Resources/Tasks/tracks/`.
- `tests/TracksTest.cs` — прогон всего контента через код игры (Tracks, PyCore, Jint, SQLite, регулярки .NET)
  и симуляция прохождения каждого направления (что грейды открываются и нет тупиков).

## Как поправить задачу

1. Правь генератор нужной части (`gen/<часть>.py`), затем:

       python3 Tools/Content/gen/f2.py
       python3 Tools/Content/spec/validate.py Tools/Content/out/frontend_f2.json --fix

   (`--fix` сам пересчитает xp_reward; для JS-проверок нужен node, для TS — `npm install` в `tools/`.)
2. Собери направления: `python3 Tools/Content/spec/merge.py` — в конце должно быть «problems: нет».
3. Можно править и сами JSON в `Resources/Tasks/tracks/` вручную — формат тот же; игра подхватит без сборки.

## Как игра использует контент

- Путь игрока = общая база + выбранное направление (Fullstack — только свои сквозные темы, открывается после
  Backend, Frontend и DevOps). Грейд — самый младший, в котором остались незакрытые темы.
- Тема открывается, когда игрок дорос до её грейда и закрыл темы из `requires`; задачи темы — по очереди.
- Проверка: choice → индексы вариантов; python → PyCore (функции через `entry` или ввод/вывод);
  javascript → Jint; sql → SQLite (результат последнего запроса); остальное → регулярки из `test_cases`.
