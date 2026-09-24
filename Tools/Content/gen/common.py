#!/usr/bin/env python3
"""Генератор контента «Стажёра»: track common, part common (общая база для всех направлений)."""
import json
import random
import os

ROAD = json.load(open("Tools/Content/spec/roadmap.json", encoding="utf-8"))
GRADE = {t["topic_id"]: t["grade"] for t in ROAD["common"]}
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}

TOPICS, TASKS = [], []


def s(text):
    """Убирает ведущий перевод строки у тройных кавычек."""
    return text[1:] if text.startswith("\n") else text


def topic(tid, theory):
    assert tid in GRADE, tid
    TOPICS.append({"topic_id": tid, "theory": s(theory).strip()})


def shuffle_options(tasks):
    """Детерминированно перемешивает варианты (seed = task_id) и пересчитывает индексы ответа,
    чтобы верный вариант не угадывался по позиции (одиночные ответы раскладываются по позициям равномерно).
    В исходнике верные варианты идут первыми."""
    used = {}  # сколько раз верный ответ (одиночный) уже стоял на каждой позиции
    for t in tasks:
        c = t["content"]
        opts = c.get("options")
        if not isinstance(opts, list) or t.get("legacy"):
            continue
        ans = c["correct_answer"]
        good = set(ans) if isinstance(ans, list) else {ans}
        for attempt in range(50):
            order = list(range(len(opts)))
            random.Random(t["task_id"] if attempt == 0 else "%s#%d" % (t["task_id"], attempt)).shuffle(order)
            idx = sorted(order.index(i) for i in good)
            if isinstance(ans, list):
                # несколько верных: не оставляем их все подряд в начале списка
                if idx != list(range(len(idx))):
                    break
                continue
            # одиночный ответ: берём перестановку, где он попадает на одну из самых «редких» позиций
            if used.get(idx[0], 0) <= min(used.get(p, 0) for p in range(len(opts))):
                break
        c["options"] = [opts[i] for i in order]
        if isinstance(ans, list):
            c["correct_answer"] = idx
        else:
            c["correct_answer"] = idx[0]
            used[idx[0]] = used.get(idx[0], 0) + 1


def task(tid, typ, diff, char, story, question, *, code=None, lang=None, options=None, answer=None,
         tests=None, entry=None, explanation, hints, time=None):
    topic_id = tid.rsplit("-", 1)[0]
    grade = GRADE[topic_id]
    content = {"question": question, "code": s(code) if code else None, "language": lang}
    if entry:
        content["entry"] = entry
    content.update({"options": options, "correct_answer": s(answer) if isinstance(answer, str) else answer,
                    "test_cases": tests})
    TASKS.append({
        "task_id": tid,
        "topic_id": topic_id,
        "grade": grade,
        "type": typ,
        "difficulty": diff,
        "xp_reward": int(round(XP_BASE[diff] * XP_MULT[grade] / 5.0)) * 5,
        "time_limit_minutes": time,
        "character": char,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": list(hints),
    })


def rx(desc, pattern, flags="m"):
    return {"input": desc, "expected": {"regex": pattern, "flags": flags}}


def nrx(desc, pattern, flags="m"):
    return {"input": desc, "expected": {"not_regex": pattern, "flags": flags}}


# =====================================================================
# c-terminal — Терминал и Linux для разработчика (intern)
# =====================================================================
topic("c-terminal", """
Терминал (консоль, shell) — программа, в которой ты управляешь компьютером текстовыми командами. На серверах графического интерфейса нет, а git, пакетные менеджеры, Docker и сборка проектов живут именно здесь. На macOS и Linux обычно bash или zsh, на Windows ставят WSL или Git Bash — команды те же.

Команда = имя + флаги + аргументы:

    ls -la src/

Навигация
• pwd — где я сейчас; ls — что в папке (-l подробно, -a со скрытыми файлами — их имена начинаются с точки).
• cd папка — перейти; cd .. — на уровень выше; cd или cd ~ — домой; cd - — в предыдущую папку.
• Путь от корня (/home/intern/projects) — абсолютный, от текущей папки (projects/lamp-market) — относительный. Точка — текущая папка, две точки — родительская, ~ — домашняя.
• Tab дописывает имена файлов и команд, стрелка вверх листает историю, Ctrl+R ищет по ней, Ctrl+C прерывает зависшую команду.

Файлы
• mkdir -p a/b/c — создать папку вместе с промежуточными (и без ошибки, если уже есть).
• cp откуда куда — копировать (cp -r для папок), mv — переместить или переименовать, touch — создать пустой файл.
• rm file, rm -r папка — удалить навсегда, корзины нет. Перед rm -rf перечитай путь дважды.
• cat — вывести файл, less — листать (q — выход), head -n 20 / tail -n 20 — начало и конец, tail -f — следить за логом в реальном времени.

Права
В выводе ls -l первая колонка вроде -rwxr-xr-- — права владельца, группы и всех остальных: r — чтение, w — запись, x — запуск. Скрипт без x не запустится через ./script.sh — будет Permission denied. Лечится chmod +x script.sh. В цифрах: 755 — владелец может всё, остальные читают и запускают; 644 — обычный файл. chmod 777 «чтобы заработало» — плохая привычка: писать в файл сможет кто угодно.

Потоки и пайпы
• команда > file — записать вывод в файл (старое содержимое пропадёт!), >> — дописать в конец, 2> — перенаправить ошибки, 2>&1 — ошибки туда же, куда обычный вывод.
• Вертикальная черта | отдаёт вывод одной команды на вход другой:

    grep "ERROR" app.log | tail -n 5
    grep -c "ERROR" app.log

• grep "текст" file — строки с текстом: -i без учёта регистра, -n с номерами строк, -r по всем файлам папки, -v наоборот (без текста), -c посчитать строки. wc -l — число строк, sort и uniq -c — сортировка и подсчёт повторов.

Переменные окружения
Программы берут настройки (адрес базы, ключи API) из переменных окружения. echo $HOME — посмотреть одну, env — все.

    export DATABASE_URL=postgresql://dev:dev@localhost:5432/market

Без export переменная видна только самому shell, а запущенные из него программы её не получат. Переменная живёт до закрытия терминала; постоянные настройки кладут в ~/.bashrc (~/.zshrc) или в файл .env проекта. PATH — список папок, где shell ищет команды: command not found обычно значит, что программа не установлена или её папки нет в PATH. which python покажет, какой именно python запустится.

Не помнишь флаг — команда --help или man команда.
""")

task("c-terminal-01", "quiz", 1, "manager",
     "Оля, HR: «Привет и добро пожаловать в «Кодзилла Софт»! Первый день — настройка ноутбука, инструкция начинается со слов “открой терминал и перейди в папку с проектом”. Гена говорит, без терминала тут никуда, так что давай проверим, как ты ориентируешься в папках».",
     "Ты в папке /home/intern/projects/lamp-market. Выполняешь по очереди cd .. и cd coffee-bot/src. Что после этого покажет pwd?",
     code="""
$ pwd
/home/intern/projects/lamp-market
$ cd ..
$ cd coffee-bot/src
$ pwd
""", lang="bash",
     options=[
         "/home/intern/projects/coffee-bot/src",
         "/home/intern/coffee-bot/src",
         "/home/intern/projects/lamp-market/coffee-bot/src",
         "/coffee-bot/src",
         "Ошибку: из папки lamp-market нельзя попасть в coffee-bot",
     ],
     answer=0,
     explanation="Две точки — это родительская папка, поэтому cd .. переводит из /home/intern/projects/lamp-market в /home/intern/projects. Путь coffee-bot/src не начинается с косой черты, значит, он относительный и считается от текущей папки: получаем /home/intern/projects/coffee-bot/src. Путь с / в начале — абсолютный, он считается от корня диска, так что /coffee-bot/src — совсем другое место. Если заблудился, pwd всегда скажет, где ты, а cd без аргументов вернёт в домашнюю папку.",
     hints=["Что означают две точки: текущую папку или родительскую?",
            "Путь без / в начале отсчитывается от папки, в которой ты сейчас."])

task("c-terminal-02", "write_code", 1, "manager",
     "Оля, HR: «Следующий пункт онбординга — запустить Ламповый Маркет у себя. Репозиторий ты уже склонировал в ~/projects. Гена просил передать: нужен файл .env из шаблона .env.example и папка var/log для логов — её в репозитории нет. И всё из терминала, мышкой не надо».",
     "Напиши команды по порядку: 1) перейди в папку ~/projects/lamp-market; 2) скопируй .env.example в .env; 3) создай папку var/log вместе с промежуточной var; 4) выведи подробный список всех файлов папки, включая скрытые.",
     code="""
# 1. Перейди в папку проекта

# 2. Сделай файл настроек из шаблона

# 3. Создай папку для логов вместе с промежуточной

# 4. Покажи все файлы, включая скрытые, подробно

""", lang="bash",
     answer="""
# 1. Перейди в папку проекта
cd ~/projects/lamp-market
# 2. Сделай файл настроек из шаблона
cp .env.example .env
# 3. Создай папку для логов вместе с промежуточной
mkdir -p var/log
# 4. Покажи все файлы, включая скрытые, подробно
ls -la
""",
     tests=[
         rx("Переход в ~/projects/lamp-market",
            r"""^\s*cd\s+["']?(~|\$HOME|\$\{HOME\}|/home/[^/\s]+)/projects/lamp-market/?["']?\s*(&&|;|\|\||$)"""),
         rx("Копия .env.example в .env",
            r"""(^|&&|;)\s*cp\s+(-[a-zA-Z]+\s+)*(\./)?\.env\.example\s+(\./)?\.env\s*(&&|;|$)"""),
         rx("Папка var/log создаётся с флагом -p",
            r"""(^|&&|;)\s*mkdir\s+(-[a-zA-Z]*p[a-zA-Z]*|--parents)\s+(\./)?var/log/?\s*(&&|;|$)"""),
         rx("ls с подробным выводом и скрытыми файлами",
            r"""(^|&&|;)\s*ls\b(?=[^\n]*\s-[a-zA-Z]*l)(?=[^\n]*\s-[a-zA-Z]*[aA])"""),
         rx("Сначала переход в папку, потом копирование",
            r"""^\s*cd\s[^\n]*lamp-market[\s\S]*\bcp\s"""),
     ],
     explanation="Тильда — сокращение домашней папки, поэтому cd ~/projects/lamp-market работает из любого места. cp копирует файл, а шаблон .env.example остаётся нетронутым — он лежит в git, чтобы у всех был образец настроек, а сам .env в git не коммитят. mkdir -p создаёт сразу всю цепочку папок и не ругается, если они уже есть, — поэтому его любят в скриптах. Файлы, чьи имена начинаются с точки, скрыты: без флага -a ты бы просто не увидел только что созданный .env, а -l показывает права, размер и дату изменения.",
     hints=["Понадобятся четыре команды: cd, cp, mkdir и ls.",
            "Промежуточные папки создаёт флаг -p у mkdir, скрытые файлы показывает флаг -a у ls.",
            "cp .env.example .env, затем mkdir -p var/log и ls -la."])

LOGP = r"""(["']?\$\{?LOG\}?["']?|/var/log/lamp-market/app\.log)"""
task("c-terminal-03", "write_code", 2, "qa",
     "Ира: «На стейдже Лампового Маркета с утра сыплются ошибки оплаты. Лог огромный, в редакторе он даже не открывается. Для баг-репорта мне нужны цифры: сколько строк с ERROR и последние пять таких строк отдельным файлом. И подскажи, где в коде вообще живёт этот PaymentTimeout».",
     "Путь к логу уже лежит в переменной LOG. Напиши три команды: 1) выведи количество строк с ERROR в логе; 2) сохрани последние 5 строк с ERROR в файл errors.txt в текущей папке; 3) найди во всех файлах папки src (рекурсивно) текст PaymentTimeout и выведи совпадения с номерами строк. Сам лог не изменяй.",
     code="""
LOG=/var/log/lamp-market/app.log

# 1. Сколько строк с ERROR

# 2. Последние 5 строк с ERROR — в файл errors.txt

# 3. Где в src упоминается PaymentTimeout (с номерами строк)

""", lang="bash",
     answer="""
LOG=/var/log/lamp-market/app.log

# 1. Сколько строк с ERROR
grep -c "ERROR" "$LOG"

# 2. Последние 5 строк с ERROR — в файл errors.txt
grep "ERROR" "$LOG" | tail -n 5 > errors.txt

# 3. Где в src упоминается PaymentTimeout (с номерами строк)
grep -rn "PaymentTimeout" src/
""",
     tests=[
         rx("Подсчёт строк с ERROR (grep -c или grep | wc -l)",
            r"""^[^#\n]*grep\b(?=[^\n|]*\s-[a-zA-Z]*c)[^\n|]*ERROR[^\n|]*""" + LOGP + r"""\s*$|^[^#\n]*grep\b[^\n|]*ERROR[^\n|]*""" + LOGP + r"""\s*\|\s*wc\s+-l"""),
         rx("grep по ERROR, затем tail на 5 строк и запись в errors.txt",
            r"""^[^#\n]*grep\b[^\n|]*ERROR[^\n|]*""" + LOGP + r"""\s*\|\s*tail\s+(-n\s*5|-5|--lines[=\s]5)\s*>\s*["']?(\./)?errors\.txt"""),
         rx("Рекурсивный поиск PaymentTimeout в src с номерами строк",
            r"""^[^#\n]*grep\b(?=[^\n]*\s-[a-zA-Z]*[rR])(?=[^\n]*\s-[a-zA-Z]*n)[^\n]*PaymentTimeout[^\n]*\s["']?(\./)?src/?["']?\s*$"""),
         nrx("Лог не перезаписывается и не дописывается",
             r""">+\s*["']?(\$\{?LOG\}?|/var/log/lamp-market/app\.log)"""),
     ],
     explanation="grep -c считает строки, в которых нашёлся текст (не количество вхождений), — ровно то, что нужно для отчёта. Во второй команде пайп отдаёт найденные строки команде tail, та оставляет последние пять, а > записывает их в errors.txt (файл создастся или перезапишется). Флаг -r обходит все файлы папки, -n добавляет номера строк — так сразу видно, куда смотреть в редакторе. Путь в двойных кавычках (\"$LOG\") — хорошая привычка: переменная с пробелом внутри не развалится на два аргумента. Главная опасность — случайно написать > в сам лог: он обнулится, и улики пропадут.",
     hints=["grep умеет считать строки сам — посмотри флаг -c.",
            "Вывод одной команды передаётся в другую через |, в файл — через >.",
            "grep \"ERROR\" \"$LOG\" | tail -n 5 > errors.txt; для поиска по папке — grep -rn."])

task("c-terminal-04", "find_bug", 2, "qa",
     "Ира: «Поднимаю Маркет локально для регресса по твоей инструкции: базу запустила, адрес подключения задала, а приложение всё равно падает на старте. Скинула свой терминал — Гена говорит, проблема в одной строчке».",
     "Почему приложение не видит переменную, хотя echo её выводит? Выбери правильный диагноз и исправление.",
     code="""
$ DATABASE_URL=postgresql://dev:dev@localhost:5432/market
$ echo $DATABASE_URL
postgresql://dev:dev@localhost:5432/market
$ python -m app
Traceback (most recent call last):
  File "<frozen runpy>", line 198, in _run_module_as_main
  File "<frozen runpy>", line 88, in _run_code
  File "/home/intern/projects/lamp-market/app/__main__.py", line 1, in <module>
    from app.config import settings
  File "/home/intern/projects/lamp-market/app/config.py", line 7, in <module>
    DATABASE_URL = os.environ["DATABASE_URL"]
                   ~~~~~~~~~~^^^^^^^^^^^^^^^^
  File "<frozen os>", line 679, in __getitem__
KeyError: 'DATABASE_URL'
""", lang="text",
     options=[
         "Переменная задана без export: её видит только shell, а дочерний процесс python не получает. Нужно export DATABASE_URL=…",
         "В значении есть двоеточия и @ — shell понимает их как спецсимволы, значение нужно взять в кавычки или экранировать обратным слэшем",
         "Python читает переменные окружения только из ~/.bashrc — нужно дописать туда строку и открыть новый терминал",
         "Приложение нужно запустить через sudo: без прав администратора процесс не видит переменные окружения",
         "echo показал значение, значит, переменная задана верно — ошибка в config.py: нужен os.getenv вместо os.environ[]",
     ],
     answer=0,
     explanation="Запись VAR=значение создаёт переменную самого shell: echo её видит, потому что подстановку $DATABASE_URL делает тот же shell. Но дочерние процессы (python, node, docker) получают только переменные окружения — те, что помечены export. Отсюда KeyError в os.environ. Вариант VAR=значение команда задаёт переменную только для одного запуска — удобно для разовых проверок. ~/.bashrc нужен, чтобы настройка переживала закрытие терминала, но и там без export переменная до python не дойдёт, а sudo по умолчанию, наоборот, сбрасывает окружение. Двоеточия и @ shell не трогает — кавычки нужны для пробелов и $, а os.getenv вернул бы None, и приложение упало бы чуть позже.",
     hints=["echo выполняет сам shell, а python — это отдельный процесс. Что процессы наследуют от shell?",
            "Посмотри, каким словом в теории помечают переменные для дочерних процессов."])

task("c-terminal-05", "incident", 2, "devops_colleague",
     "Дима: «Ночная выгрузка заказов для пекарни «Батон» не отработала, а Нина ждёт файл к девяти утра. Вчера вечером кто-то переписал скрипт выгрузки. Я на другом инциденте — глянь, что с ним, вот вывод с сервера».",
     "Что сломалось и как быстрее всего вернуть выгрузку?",
     code="""
$ crontab -l | grep export
0 3 * * * /opt/baton/export_orders.sh >> /var/log/baton/export.log 2>&1

$ tail -n 3 /var/log/baton/export.log
2026-09-23 03:00:01 export started
2026-09-23 03:00:04 export finished: 214 orders
/bin/sh: 1: /opt/baton/export_orders.sh: Permission denied

$ ls -l /opt/baton/
total 12
-rw-r--r-- 1 deploy deploy 1840 Sep 23 21:14 export_orders.sh
drwxr-xr-x 2 deploy deploy 4096 Sep 23 03:00 out
-rw-r--r-- 1 deploy deploy  312 Sep 12 10:02 README.md
""", lang="text",
     options=[
         "Пропал бит x (-rw-r--r--): chmod +x export_orders.sh, запустить выгрузку вручную, закоммитить права в репозиторий",
         "Сделать chmod -R 777 /opt/baton, чтобы прав точно хватило и cron, и пользователю deploy, и выгрузке в папку /opt/baton/out",
         "Сломался cron: запись в crontab есть, а скрипт не стартовал — переустановить cron и перезагрузить весь сервер",
         "Файл повреждён при правке — удалить export_orders.sh и восстановить его из бэкапа недельной давности",
         "Дописать sudo в начало строки в crontab, чтобы скрипт запускался с правами root и ему хватало прав",
     ],
     answer=0,
     explanation="Лог показывает Permission denied при запуске, а ls -l — права -rw-r--r--: читать и писать можно, запускать — нет. Скорее всего, файл пересоздали при правке или копировании, и бит x потерялся. chmod +x (или chmod 755) возвращает право на запуск; раз cron уже отработал в 3:00, выгрузку нужно запустить руками и убедиться по логу, что заказы выгрузились. git хранит бит исполнения, поэтому правильные права стоит закоммитить — иначе следующий деплой сломает всё снова. chmod 777 дал бы любому пользователю сервера право дописать в скрипт свои команды, которые cron потом выполнит, — это дыра в безопасности.",
     hints=["Посмотри на первую колонку в выводе ls -l.",
            "Какой буквы не хватает в правах, чтобы файл можно было запустить?"],
     time=15)


# =====================================================================
# c-git-basics — Git: коммиты и история (intern)
# =====================================================================
topic("c-git-basics", """
Git — система контроля версий: хранит историю проекта снимками (коммитами) и позволяет команде работать над одним кодом. Удалённый репозиторий на GitHub или GitLab — общая копия, у каждого разработчика есть своя локальная.

Три зоны, которые надо понимать:
• рабочая папка — файлы, которые ты правишь;
• индекс (staging area) — то, что попадёт в следующий коммит;
• история — сделанные коммиты. У коммита есть хеш (например, a3f9c21), автор, дата, сообщение и ссылка на предыдущий коммит.

Обычный цикл работы:

    git clone git@github.com:codezilla/coffee-bot.git
    git status
    git diff
    git add src/menu.py
    git diff --staged
    git commit -m "Add raf to the menu"
    git push

• git clone скачивает репозиторий, git init создаёт новый в текущей папке.
• git status — что изменено, что в индексе, какие файлы git ещё не отслеживает (Untracked). Смотри его перед каждым коммитом.
• git diff — изменения, которые ещё не в индексе; git diff --staged — то, что уйдёт в коммит.
• git log --oneline — краткая история, git log -p — с изменениями, git show хеш — один коммит.
• git push отправляет коммиты на сервер, git pull забирает чужие.

Ловушки индекса
• git add кладёт в индекс состояние файла на момент команды. Поправил файл после add — добавь его снова, иначе в коммит уйдёт старая версия.
• git commit -a (или -am) добавляет только изменения уже отслеживаемых файлов. Новые файлы надо добавить явно через git add.
• git add . хватает всё подряд, включая мусор. Добавляй конкретные файлы или хотя бы проверяй git status.
• Отменить незакоммиченное: git restore файл — вернуть файл к последнему коммиту (правки пропадут!), git restore --staged файл — убрать из индекса, сами правки останутся.

.gitignore
Список того, что git не должен отслеживать: зависимости (node_modules/, .venv/), кэш (__pycache__/), сборка (dist/), логи (*.log), локальные настройки и секреты (.env).

    node_modules/
    __pycache__/
    *.log
    .env

Шаблон *.log — все файлы с таким расширением, dist/ — папка, /config.local — только в корне репозитория. .gitignore действует лишь на неотслеживаемые файлы. Если файл уже закоммичен, сначала убери его из индекса: git rm --cached .env — файл останется на диске, а git перестанет его отслеживать.

Хороший коммит
• Один коммит — одно логическое изменение. «Починил доставку» и «обновил README» — два коммита: их проще ревьюить и откатывать по отдельности.
• Заголовок до ~70 символов отвечает на вопрос «что сделает этот коммит»: Fix free delivery threshold, «Исправь порог бесплатной доставки» — язык и стиль задаёт команда (многие используют Conventional Commits: fix:, feat:). При необходимости — пустая строка и тело: зачем и почему.
• «fix», «wip», «правки» — плохие сообщения: через полгода никто не поймёт, что там.
""")

task("c-git-basics-01", "write_code", 1, "manager",
     "Оля, HR: «У нас традиция: каждый новичок первым коммитом добавляет себя в TEAM.md КофеБота — так я вижу, что доступ к GitHub у тебя настроен. Гена разрешил коммитить прямо в main, но только этот файл: дальше всё через ветки».",
     "Напиши команды: склонируй git@github.com:codezilla/coffee-bot.git; перейди в папку coffee-bot; допиши в конец TEAM.md строку «- Стажёр, команда КофеБота» (через echo и перенаправление, не затирая файл); добавь в индекс только TEAM.md; сделай коммит с понятным сообщением (не короче 8 символов); отправь коммит на сервер.",
     code="""
# склонируй репозиторий и перейди в него


# допиши строку в TEAM.md


# добавь в индекс, закоммить, отправь

""", lang="bash",
     answer="""
# склонируй репозиторий и перейди в него
git clone git@github.com:codezilla/coffee-bot.git
cd coffee-bot

# допиши строку в TEAM.md
echo "- Стажёр, команда КофеБота" >> TEAM.md

# добавь в индекс, закоммить, отправь
git status
git add TEAM.md
git commit -m "Add intern to TEAM.md"
git push
""",
     tests=[
         rx("Клонирование репозитория", r"""^\s*git\s+clone\s+git@github\.com:codezilla/coffee-bot\.git\b"""),
         rx("Переход в папку coffee-bot", r"""^\s*cd\s+["']?(\./)?coffee-bot/?["']?\s*(&&|;|\|\||$)"""),
         rx("Строка дописывается через >>", r"""^[^#\n]*echo\s+[^\n]*>>\s*["']?(\./)?TEAM\.md"""),
         nrx("TEAM.md не перезаписывается одинарным >", r"""(^|[^>])>\s*["']?(\./)?TEAM\.md"""),
         rx("В индекс добавлен TEAM.md", r"""^\s*git\s+add\s+["']?(\./)?TEAM\.md"""),
         nrx("Без git add . / -A и commit -a", r"""git\s+add\s+(\.|-A|--all)(\s|$)|git\s+commit\s+-[a-zA-Z]*a"""),
         rx("Коммит с сообщением от 8 символов", r"""^\s*git\s+commit\s+(-m\s*|--message[=\s])["'][^"'\n]{8,}["']"""),
         rx("push после коммита", r"""^\s*git\s+commit\b[\s\S]*^\s*git\s+push\b"""),
     ],
     explanation="git clone создаёт папку с именем репозитория, поэтому дальше нужен cd coffee-bot. Двойная стрелка >> дописывает строку в конец файла, а одинарная > перезаписала бы TEAM.md целиком — и из списка пропала бы вся команда. Добавлять в индекс лучше конкретный файл: git add . заодно утащит всё, что случайно лежит в папке. git status перед коммитом — полезная привычка, а сообщение коммита должно объяснять, что изменилось. git push отправляет коммит на сервер; если попросит ключ — значит, SSH-доступ к GitHub ещё не настроен.",
     hints=["Сначала git clone, потом cd в папку с именем репозитория.",
            "Дописать в конец файла — это >>, а не >.",
            "git add TEAM.md, git commit -m \"...\", git push."])

task("c-git-basics-02", "quiz", 2, "qa",
     "Ира: «Смотрю твой коммит «Fix cart total» — там только половина фикса, тест корзины всё ещё красный. Что у тебя было перед коммитом?» Ты листаешь терминал и находишь вывод git status, сделанный прямо перед git commit -m \"Fix cart total\".",
     "Что именно попало в коммит?",
     code="""
$ git status
On branch fix/cart-total
Changes to be committed:
  (use "git restore --staged <file>..." to unstage)
	modified:   src/cart.py

Changes not staged for commit:
  (use "git add <file>..." to update what will be committed)
  (use "git restore <file>..." to discard changes in working directory)
	modified:   src/cart.py
	modified:   src/pricing.py

Untracked files:
  (use "git add <file>..." to include in what will be committed)
	tests/test_cart_total.py
""", lang="text",
     options=[
         "src/cart.py в виде на момент git add; поздние правки cart.py, pricing.py и новый тест в коммит не попали",
         "Все изменения: src/cart.py, src/pricing.py и новый тест — commit -m забирает всё, что изменено в папке",
         "src/cart.py со всеми последними правками, а pricing.py и тест — нет, потому что их не добавляли",
         "Ничего: git не даёт коммитить, пока в рабочей папке есть неотслеживаемые файлы вроде нового теста",
     ],
     answer=0,
     explanation="src/cart.py есть сразу в двух разделах: значит, ты сделал git add, а потом продолжил править файл. Индекс хранит снимок файла на момент add, поэтому в коммит ушла только первая часть правок cart.py. pricing.py изменён, но не добавлен, а tests/test_cart_total.py git вообще пока не отслеживает. Исправить: git add src/cart.py src/pricing.py tests/test_cart_total.py, проверить git diff --staged и сделать следующий коммит. Неотслеживаемые файлы коммиту не мешают — они просто в него не попадают.",
     hints=["Обрати внимание: src/cart.py встречается в выводе дважды.",
            "Индекс хранит снимок файла на момент git add, а не ссылку на файл."])

task("c-git-basics-03", "find_bug", 2, "devops_colleague",
     "Дима: «Твой коммит с зонами доставки «Батона» уехал на стейдж, и сервис сразу упал с ModuleNotFoundError. Говоришь, локально всё работает? Скинул тебе твой же терминал и лог стейджа».",
     "Почему на стейдже нет модуля и как это исправить?",
     code="""
$ git commit -am "Add delivery zones for Baton"
[main 4f2a9c1] Add delivery zones for Baton
 1 file changed, 12 insertions(+), 3 deletions(-)
$ git push
To github.com:codezilla/baton-delivery.git
   91be03d..4f2a9c1  main -> main
$ git status
On branch main
Your branch is up to date with 'origin/main'.

Untracked files:
  (use "git add <file>..." to include in what will be committed)
	src/delivery_zones.py

nothing added to commit but untracked files present (use "git add" to track)

# лог стейджа
  File "/app/src/delivery.py", line 3, in <module>
    from src.delivery_zones import ZONES
ModuleNotFoundError: No module named 'src.delivery_zones'
""", lang="text",
     options=[
         "commit -a не берёт новые файлы, а delivery_zones.py остался Untracked. Нужно git add, коммит и push",
         "На стейдже остался старый кэш .pyc после деплоя — достаточно перезапустить сервис, и модуль найдётся",
         "Файл попал в .gitignore, поэтому git его не отправил — нужно удалить .gitignore из репозитория",
         "Обычный push отправляет только изменённые файлы, новые не уходят — нужен git push --force",
         "Импорт должен быть относительным: from .delivery_zones import ZONES, иначе на стейдже путь другой",
     ],
     answer=0,
     explanation="В выводе коммита видно «1 file changed» — ушёл только изменённый delivery.py. Флаг -a добавляет в коммит изменения и удаления уже отслеживаемых файлов, но никогда не добавляет новые. git status после пуша прямо говорит, что src/delivery_zones.py — Untracked; будь он в .gitignore, git бы его вообще не показал. Локально всё работает, потому что файл лежит у тебя на диске. Правило: перед push смотри git status и добавляй новые файлы явно; --force тут ничего не даст, а в общей ветке ещё и перепишет чужую историю.",
     hints=["Посмотри, сколько файлов попало в коммит, и что осталось в git status.",
            "Что именно делает флаг -a у git commit с новыми файлами?"])

task("c-git-basics-04", "write_code", 2, "teamlead",
     "Марина, сеньор: «В репозитории КофеБота кто-то закоммитил .env с локальными настройками, и теперь он у всех постоянно в изменениях и конфликтах. Секретов там нет, но файлу в git не место. Заодно пусть git игнорирует __pycache__/ и все логи».",
     "Напиши команды: допиши в .gitignore три строки — .env, __pycache__/ и *.log (не затирая то, что там уже есть); перестань отслеживать .env, не удаляя его с диска; добавь .gitignore в индекс и сделай коммит.",
     code="""
# 1. Допиши три шаблона в .gitignore


# 2. Убери файл настроек из git, но оставь его на диске


# 3. Закоммить изменения

""", lang="bash",
     answer="""
# 1. Допиши три шаблона в .gitignore
echo ".env" >> .gitignore
echo "__pycache__/" >> .gitignore
echo "*.log" >> .gitignore

# 2. Убери файл настроек из git, но оставь его на диске
git rm --cached .env

# 3. Закоммить изменения
git add .gitignore
git commit -m "Stop tracking .env, ignore caches and logs"
""",
     tests=[
         rx(".env добавлен в .gitignore",
            r"""^\s*\.env\s*$|\.env\b(?!\.)["']?[^\n]*>>\s*["']?\.gitignore"""),
         rx("__pycache__/ добавлен в .gitignore",
            r"""^\s*__pycache__/?\s*$|__pycache__/?["']?[^\n]*>>\s*["']?\.gitignore"""),
         rx("*.log добавлен в .gitignore",
            r"""^\s*\*\.log\s*$|\*\.log["']?[^\n]*>>\s*["']?\.gitignore"""),
         nrx(".gitignore не перезаписывается одинарным >", r"""(^|[^>])>\s*["']?\.gitignore"""),
         rx(".env убран из индекса через git rm --cached",
            r"""git\s+rm\s+(-r\s+)?--cached\s+(-r\s+)?["']?\.env["']?(\s|$)"""),
         nrx(".env не удаляется с диска", r"""^\s*(git\s+)?rm\s+(?![^\n]*--cached)[^\n]*\.env\b"""),
         rx(".gitignore добавлен и закоммичен",
            r"""git\s+add\s+[^\n]*\.gitignore[\s\S]*git\s+commit\s+[^\n]*-[a-zA-Z]*m"""),
     ],
     explanation=".gitignore работает только для файлов, которые git ещё не отслеживает: раз .env уже закоммичен, одна строка в .gitignore ничего не изменит. git rm --cached убирает файл из индекса — в коммите он будет помечен как удалённый из репозитория, но на твоём диске останется. Обычный git rm или rm удалил бы и локальную копию. Дописывать нужно через >>, чтобы не потерять старые правила. Важная деталь для команды: когда коллеги подтянут этот коммит, git удалит их локальный .env или откажется делать pull, если файл изменён, — предупреди всех заранее сохранить копию.",
     hints=["Чем отличается git rm от git rm --cached?",
            "Каждый шаблон дописывай отдельной строкой: echo \"...\" >> .gitignore.",
            "git rm --cached .env, потом git add .gitignore и git commit."])

task("c-git-basics-05", "write_code", 2, "teamlead",
     "Гена: «Вижу, ты за утро поправил расчёт доставки, написал к нему тест и заодно обновил README. Не мешай всё в кучу: фикс и документация — два разных коммита. Их проще ревьюить, а фикс, если что, можно откатить отдельно».",
     "Изменены src/delivery.py, tests/test_delivery.py и README.md. Сделай два коммита: первый — фикс (src/delivery.py и tests/test_delivery.py), второй — только README.md. Перед каждым коммитом проверь, что уходит в коммит, через git diff --staged. Не используй git add . и git commit -a.",
     code="""
git status
# коммит 1: фикс доставки и тест


# коммит 2: README

""", lang="bash",
     answer="""
git status
# коммит 1: фикс доставки и тест
git add src/delivery.py tests/test_delivery.py
git diff --staged
git commit -m "Fix free delivery threshold check"

# коммит 2: README
git add README.md
git diff --staged
git commit -m "Describe delivery rules in README"
""",
     tests=[
         rx("src/delivery.py добавлен и закоммичен до README.md",
            r"""^\s*git\s+add\s+[^\n]*src/delivery\.py[\s\S]*?^\s*git\s+commit\b[\s\S]*?^\s*git\s+add\s+[^\n]*README\.md"""),
         rx("Тест добавлен и закоммичен до README.md",
            r"""^\s*git\s+add\s+[^\n]*tests/test_delivery\.py[\s\S]*?^\s*git\s+commit\b[\s\S]*?^\s*git\s+add\s+[^\n]*README\.md"""),
         rx("README.md закоммичен вторым коммитом",
            r"""^\s*git\s+add\s+[^\n]*README\.md[\s\S]*^\s*git\s+commit\b"""),
         nrx("README.md не добавляется вместе с фиксом",
             r"""^\s*git\s+add\s+[^\n]*README\.md[\s\S]*^\s*git\s+add\s+[^\n]*(src/delivery\.py|tests/test_delivery\.py)|^\s*git\s+add\s+[^\n]*(README\.md[^\n]*delivery|delivery[^\n]*README\.md)"""),
         rx("git diff --staged перед каждым из двух коммитов",
            r"""(git\s+diff\s+(--staged|--cached)[\s\S]*){2}"""),
         nrx("Без git add . / -A и commit -a", r"""git\s+add\s+(\.|-A|--all)(\s|$)|git\s+commit\s+-[a-zA-Z]*a"""),
     ],
     explanation="Атомарные коммиты — одна логическая правка на коммит — окупаются постоянно: ревьюер видит фикс без шума, git revert откатит ровно фикс, а поиск виновного коммита (git bisect) укажет на конкретное изменение. git diff --staged показывает именно содержимое индекса, то есть то, что попадёт в коммит, — лучший способ не закоммитить лишнего. git add . и commit -a смешали бы всё в один коммит. Если в одном файле перемешаны две правки, их можно разнести по коммитам через git add -p — он предложит добавить изменения по кусочкам.",
     hints=["Сначала git add двух файлов фикса, проверка, коммит — и только потом README.",
            "git diff --staged показывает, что лежит в индексе.",
            "git add src/delivery.py tests/test_delivery.py → git diff --staged → git commit -m \"...\", затем то же для README.md."])


# =====================================================================
# c-http — HTTP: запросы и ответы (intern)
# =====================================================================
topic("c-http", """
HTTP — протокол, по которому браузер, мобильное приложение или другой сервис общаются с сервером. Всё общение — пары «запрос → ответ».

Запрос состоит из:
• метода — что сделать: GET (получить), POST (создать), PUT (заменить целиком), PATCH (изменить часть), DELETE (удалить);
• URL — адреса ресурса: https://lamp-market.example/api/products?category=lamps&page=2 — схема, хост, путь, после ? — query-параметры через &;
• заголовков — метаданных вида Имя: значение;
• тела — данных (у GET тела обычно нет).
Ответ — это статус-код, заголовки и тело.

Коды статусов по классам
• 2xx — успех: 200 OK, 201 Created (создано, часто с заголовком Location), 204 No Content (успех без тела).
• 3xx — перенаправление: 301/308 — навсегда, 302/307 — временно, 304 Not Modified — бери из своего кэша.
• 4xx — ошибка в запросе клиента: 400 — кривой запрос, 401 — не аутентифицирован (нет токена или он истёк), 403 — аутентифицирован, но нельзя, 404 — не найдено, 405 — метод не поддерживается, 409 — конфликт, 422 — данные не прошли валидацию, 429 — слишком много запросов.
• 5xx — ошибка на стороне сервера: 500 — упал код, 502 — прокси получил от бэкенда плохой ответ или не получил никакого, 503 — сервис недоступен или перегружен, 504 — бэкенд не ответил вовремя.
Для баг-репорта: при 4xx сначала проверь, что отправил клиент; при 5xx — смотри логи сервера.

Заголовки на каждый день
• Content-Type — формат тела: application/json, text/html, multipart/form-data (файлы).
• Accept — какой формат клиент хочет получить. Authorization: Bearer токен — кто делает запрос.
• Cookie / Set-Cookie — куки, Location — куда перенаправить или где новый ресурс.
• Cache-Control — можно ли кэшировать ответ и сколько: no-store, no-cache, max-age=60, public.
Имена заголовков не зависят от регистра, в HTTP/2 их пишут строчными.

JSON — основной формат тела в API. Правила строже, чем в JavaScript: ключи и строки только в двойных кавычках, никакой запятой после последнего элемента, никаких комментариев. Значения — строки, числа, true/false, null, массивы и объекты.

    {"product_id": 812, "quantity": 2, "gift": false, "comment": null}

curl — HTTP-клиент в терминале, им проверяют API без фронтенда:

    curl -i http://localhost:8000/api/products/812
    curl -i -X POST http://localhost:8000/api/reviews \\
      -H "Content-Type: application/json" \\
      -H "Authorization: Bearer $TOKEN" \\
      -d '{"product_id": 815, "rating": 5, "text": "Светит тепло"}'

• -i — показать статус и заголовки ответа, -v — весь обмен подробно, -s — без индикатора загрузки.
• -X — метод (с -d по умолчанию уже POST), -H — заголовок, -d — тело, -L — следовать редиректам.
• Тело — в одинарных кавычках, чтобы shell не трогал двойные кавычки внутри JSON. А заголовок с $TOKEN — в двойных: в одинарных переменная не подставится.

Вкладка Network в DevTools браузера (F12) показывает каждый запрос страницы: метод, статус, время, заголовки, тело запроса (Payload) и ответ (Preview/Response). Правый клик по запросу → Copy as cURL — и его можно повторить в терминале. Это лучший способ приложить воспроизведение к баг-репорту, только вырежи токены и куки перед отправкой в общий чат.
""")

task("c-http-01", "quiz", 1, "qa",
     "Ира: «Прогнала регресс корзины Лампового Маркета — в Network четыре красных запроса. Хочу завести баг на бэкенд, но не хочу дёргать ребят зря. Какой из них точно их зона?»",
     "Какой ответ говорит об ошибке на стороне сервера, а не о том, что клиент прислал что-то не то?",
     code="""
POST /api/cart/items         400 Bad Request
GET  /api/profile            401 Unauthorized
GET  /api/products/999999    404 Not Found
GET  /api/cart               503 Service Unavailable
""", lang="text",
     options=[
         "POST /api/cart/items — 400",
         "GET /api/profile — 401",
         "GET /api/products/999999 — 404",
         "GET /api/cart — 503",
     ],
     answer=3,
     explanation="Коды 5xx означают, что запрос, возможно, был правильным, но сервер не смог его обработать: 503 — сервис недоступен или перегружен, это к бэкенду или инфраструктуре. Коды 4xx говорят о проблеме в самом запросе: 400 — неверное тело, 401 — нет токена или он истёк (перелогинься), 404 — такого товара нет. 4xx тоже может быть багом, но искать его надо в том, что отправляет клиент (например, фронтенд собирает неверное тело). В баг-репорт приложи метод, URL, статус, тело запроса и ответа — или сразу Copy as cURL.",
     hints=["Вспомни, какой класс кодов отвечает за ошибки сервера.",
            "4xx — «ты прислал что-то не то», 5xx — «я сломался»."])

task("c-http-02", "write_code", 2, "qa",
     "Ира: «Фронт корзины ещё не готов, а мне сегодня надо проверить ручку добавления в корзину. Бэкенд Маркета у тебя поднят локально на 8000-м порту, токен тестового покупателя лежит в переменной TOKEN. Сделай запрос curl-ом и покажи ответ вместе с заголовками — приложу к тест-кейсу».",
     "Напиши curl-запрос: метод POST на http://localhost:8000/api/cart/items; заголовки Content-Type: application/json и Authorization: Bearer с токеном из переменной TOKEN; тело {\"product_id\": 812, \"quantity\": 2}. Ответ выведи вместе со статусом и заголовками.",
     code="""
# POST http://localhost:8000/api/cart/items
# тело: product_id = 812, quantity = 2
# токен — в переменной TOKEN
curl
""", lang="bash",
     answer="""
# POST http://localhost:8000/api/cart/items
# тело: product_id = 812, quantity = 2
# токен — в переменной TOKEN
curl -i -X POST http://localhost:8000/api/cart/items \\
  -H "Content-Type: application/json" \\
  -H "Authorization: Bearer $TOKEN" \\
  -d '{"product_id": 812, "quantity": 2}'
""",
     tests=[
         rx("Запрос на http://localhost:8000/api/cart/items",
            r"""^[^#\n]*curl\b[\s\S]*?(^|\s|["'])(http://)?localhost:8000/api/cart/items"""),
         rx("Метод POST (-X POST или тело через -d/--json)",
            r"""-X\s*["']?POST|--request[=\s]+["']?POST|\s(-d|--data|--data-raw|--json)(\s|=)"""),
         rx("Заголовок Content-Type: application/json", r"""Content-Type:\s*application/json|--json\s""", "im"),
         rx("Токен берётся из переменной TOKEN", r"""Authorization:\s*Bearer\s+\$\{?TOKEN\}?""", "im"),
         rx("Тело с product_id 812 и quantity 2",
            r"""^[^#\n]*(-d|--data|--data-raw|--json)[=\s](?=[^\n]*"product_id"\s*:\s*812\b)(?=[^\n]*"quantity"\s*:\s*2\b)"""),
         rx("Вывод статуса и заголовков (-i или -v)",
            r"""^[^#\n]*curl\b[\s\S]*?(\s-[a-hj-zA-Z]*i[a-zA-Z]*|\s--include|\s-v|\s--verbose)(\s|$)"""),
     ],
     explanation="-X POST задаёт метод (с -d curl и так отправит POST), -H добавляет заголовки, -d — тело запроса. Content-Type: application/json сообщает серверу, как разбирать тело: без него curl отправит application/x-www-form-urlencoded, и бэкенд не поймёт JSON. Частая ловушка с кавычками: тело JSON берём в одинарные, чтобы shell не трогал двойные кавычки внутри, а заголовок с $TOKEN — в двойные, иначе переменная не подставится и сервер ответит 401. Флаг -i показывает строку статуса (для создания ожидаем 201 или 200) и заголовки ответа.",
     hints=["Метод задаётся через -X, заголовки — через -H, тело — через -d.",
            "В одинарных кавычках переменные не подставляются — для заголовка с $TOKEN нужны двойные.",
            "curl -i -X POST URL -H \"Content-Type: application/json\" -H \"Authorization: Bearer $TOKEN\" -d '{...}'"])

task("c-http-03", "find_bug", 2, "client",
     "Вера, управляющая бизнес-центром «Высота»: «Наш администратор бронирует переговорки через ваш API, как вы показывали на обучении. А сервер ругается на каждый запрос, хотя всё вроде правильно. Вот что он отправляет».",
     "Почему сервер отклоняет запрос и как его исправить?",
     code="""
$ curl -i -X POST https://booking.vysota.example/api/bookings \\
    -H "Content-Type: application/json" \\
    -H "Authorization: Bearer $TOKEN" \\
    -d '{"room": "Эверест", "date": "2026-10-02", "from": "10:00", "to": "11:30",}'
HTTP/2 422
content-type: application/json

{"detail":[{"type":"json_invalid","loc":["body",73],"msg":"JSON decode error","input":{},"ctx":{"error":"Expecting property name enclosed in double quotes"}}]}
""", lang="text",
     options=[
         "После последнего поля лишняя запятая — это невалидный JSON, сервер не смог разобрать тело. Убрать запятую",
         "Кириллицу нельзя передавать в JSON без экранирования \\uXXXX — название переговорки надо закодировать",
         "Время нужно передавать числом минут от начала дня, а не строкой \"10:00\", поэтому сервер и отвечает 422",
         "Для создания брони нужен метод PUT, а не POST: POST сервер принимает только для поиска свободных слотов переговорок",
         "Токен истёк — сервер не может прочитать тело без авторизации и отвечает 422; нужно получить новый токен",
     ],
     answer=0,
     explanation="Ответ прямо говорит, что случилось: json_invalid, «Expecting property name enclosed in double quotes» на 73-м символе — парсер после запятой ждал следующий ключ, а встретил закрывающую скобку. В JavaScript-объектах висячая запятая разрешена, а в JSON — нет. Кириллица в JSON — обычные UTF-8 строки, а истёкший токен дал бы 401, а не 422. Быстро проверить JSON можно прямо в терминале: echo '…' | python3 -m json.tool или jq . — они укажут на ошибку ещё до отправки.",
     hints=["Прочитай поле error в ответе сервера — там написано, что он ожидал.",
            "Посмотри внимательно на конец тела запроса."])

task("c-http-04", "incident", 2, "manager",
     "Стас: «Срочно! Час назад снизили цену на лампу «Эдисон» с 2490 до 1990 — стартовала акция, реклама уже крутится. А покупатели видят старую цену и пишут в поддержку. В админке цена новая, я проверил!»",
     "Почему покупатели видят старую цену и что сделать прямо сейчас?",
     code="""
$ curl -si https://lamp-market.example/api/products/812
HTTP/2 200
content-type: application/json
cache-control: public, max-age=86400
age: 3512
x-cache: HIT

{"id": 812, "name": "Лампа настольная Эдисон", "price": 2490}

# напрямую в бэкенд, мимо CDN
$ curl -si http://catalog.internal:8000/api/products/812
HTTP/1.1 200 OK
content-type: application/json
cache-control: public, max-age=86400

{"id": 812, "name": "Лампа настольная Эдисон", "price": 1990}
""", lang="text",
     options=[
         "CDN отдаёт копию часовой давности (x-cache: HIT, age: 3512), max-age разрешает сутки: сбросить кэш CDN и уменьшить max-age",
         "Бэкенд отдаёт старую цену из реплики базы, которая отстала от мастера, — ещё раз сохранить цену в админке",
         "Сервер отвечает 200 вместо 304 Not Modified, поэтому браузеры не перезапрашивают цену — поменять код ответа",
         "Покупатели не обновили страницу после старта акции — разослать им просьбу нажать Ctrl+F5",
         "Каталог завис на старой версии данных после старта акции — перезапустить сервис каталога, чтобы он заново перечитал цены из базы",
     ],
     answer=0,
     explanation="Запрос напрямую в бэкенд возвращает 1990 — значит, база и код в порядке. Через публичный адрес ответ приходит из CDN: x-cache: HIT (взят из кэша), age: 3512 — копии почти час, то есть её сохранили до смены цены. Заголовок cache-control: public, max-age=86400 сам разрешил CDN и браузерам хранить ответ сутки. Прямо сейчас — инвалидировать кэш CDN для этих URL; дальше — для данных, которые часто меняются (цены, остатки), ставить короткий max-age или no-cache и сбрасывать кэш при изменении. Учти, что браузеры тоже могли закэшировать ответ, поэтому короткий max-age важнее разового сброса. Ctrl+F5 поможет одному человеку, а не всем покупателям.",
     hints=["Сравни два ответа: что отличается, кроме цены?",
            "Что означают заголовки age и x-cache: HIT?"],
     time=15)


# =====================================================================
# c-debugging — Дебаг: как искать ошибки (intern)
# =====================================================================
topic("c-debugging", """
Баг — это разница между тем, что программа должна делать, и тем, что она делает. Дебаг — не угадывание, а расследование: собрать факты, выдвинуть гипотезу, проверить её, сузить круг поиска.

1. Прочитай ошибку целиком
Трейсбек (stack trace) — цепочка вызовов, которая привела к ошибке. В Python читай снизу вверх: последняя строка — тип исключения и сообщение, над ней — где оно возникло, ещё выше — кто вызвал эту функцию.

    Traceback (most recent call last):
      File "/app/market/views.py", line 88, in product_page
        seller = get_seller_name(product["seller_id"])
      File "/app/market/sellers.py", line 14, in get_seller_name
        return row["name"].title()
    AttributeError: 'NoneType' object has no attribute 'title'

Читаем: в sellers.py на 14-й строке row["name"] оказался None, а код ждал строку. Если нижние строки трейсбека ведут внутрь библиотеки, поднимайся вверх до первого файла вашего проекта: чаще всего виноваты данные, которые ваш код туда передал. В JavaScript сообщение наверху, а под ним строки at функция (файл:строка:колонка) — самая верхняя и есть место ошибки.

Частые исключения: TypeError — операция с неподходящим типом (строка вместо числа); KeyError / IndexError — нет такого ключа или индекса; AttributeError: 'NoneType' … — функция вернула None, а ты ждал объект; ValueError — не то значение, например int("abc"); ZeroDivisionError. В JS — TypeError: Cannot read properties of undefined.

2. Воспроизведи
Пока баг не воспроизводится стабильно, чинить рано — не поймёшь, починил ли. Выясни шаги, входные данные и окружение (браузер, версия, пользователь). Сократи до минимального примера — самый маленький вход, на котором ломается. Лучше всего сразу оформить его тестом: тест красный → чинишь → зелёный, и баг больше не вернётся незаметно.

3. Сужай поиск
• Проверяй предположения: «здесь точно число?» — выведи и посмотри. print(repr(x)) показывает кавычки и скрытые пробелы: 'латте ' — это не 'латте'.
• Дели пополам: проверь данные в середине цепочки. Там уже плохие — ищи раньше, хорошие — позже. Из тысячи строк за десять шагов остаётся одна. Для истории коммитов то же самое делает git bisect.
• Точки останова (breakpoints): ставишь точку на строке в IDE, запускаешь в режиме отладки — программа останавливается, и видно все переменные. Step over — следующая строка, step into — зайти внутрь функции. Прямо в коде: breakpoint() в Python, debugger; в JS.
• Сравни с рабочей версией: что поменялось — код, данные, конфиг, окружение?

4. Логи
Ищи первую ошибку по времени, а не последнюю: одна причина часто порождает лавину следствий. Смотри на время, уровень (ERROR, WARNING) и идентификатор запроса или заказа — по нему связываются строки одной операции.

5. Ловушки
• Лечить симптом: обернуть всё в try/except: pass — баг спрячется и вылезет в другом месте.
• Менять несколько вещей сразу — потом непонятно, что помогло.
• Не дочитать сообщение об ошибке — в нём часто уже написан ответ.
• Час без прогресса — пора сформулировать вопрос коллеге (об этом следующая тема).
""")

task("c-debugging-01", "quiz", 1, "teamlead",
     "Гена: «КофеБот иногда падает при оформлении заказа. Вот трейсбек из логов. Прежде чем лезть в код, прочитай его и скажи, что случилось и откуда начинать поиск».",
     "Что произошло и где начинать поиск?",
     code="""
Traceback (most recent call last):
  File "/app/bot/handlers.py", line 57, in on_order
    receipt = build_receipt(order)
  File "/app/bot/receipt.py", line 21, in build_receipt
    total = sum_items(order["items"])
  File "/app/bot/receipt.py", line 9, in sum_items
    total += item["price"] * item["qty"]
             ~~~~~~~~~~~~~~^~~~~~~~~~~~~
TypeError: can't multiply sequence by non-int of type 'str'
""", lang="text",
     options=[
         "receipt.py, строка 9: цена и количество — строки. Искать, откуда в заказ приходят строки, и приводить типы на входе",
         "Ошибка в handlers.py на строке 57: build_receipt вызвана с неправильным аргументом — начинать поиск с обработчика",
         "Python не умеет умножать внутри цикла по словарям — вынести умножение в отдельную функцию и вызвать её",
         "В заказе нет поля price — добавить проверку наличия ключа перед умножением, и ошибка уйдёт",
         "Количество пришло дробным (например, 1.5), а строку можно умножать только на целое — привести qty через int()",
     ],
     answer=0,
     explanation="Трейсбек читаем снизу: TypeError, «can't multiply sequence by non-int of type 'str'» — Python пытался умножить последовательность (строку) на строку, а не на дробное число: тип второго множителя прямо назван в сообщении. Строка над сообщением показывает место: receipt.py, строка 9, item[\"price\"] * item[\"qty\"]. Выше — цепочка вызовов, которая туда привела; handlers.py лишь передал заказ дальше. Раз оба значения — строки, они, скорее всего, пришли из JSON или формы без преобразования; чинить надо на входе данных (int(...) при разборе заказа), а не прятать ошибку в try/except. Отсутствующее поле дало бы KeyError.",
     hints=["Начни с последней строки: какой тип ошибки и что в сообщении?",
            "«sequence» в сообщении — это строка. Что будет, если перемножить две строки?"])

task("c-debugging-02", "find_bug", 1, "qa",
     "Ира: «Баг: страница товара без отзывов отдаёт 500. Шаги: 1) открыть новый товар «Гирлянда Уют», у которого ещё нет ни одного отзыва; 2) страница падает. В логах ZeroDivisionError: division by zero в average_rating. У товаров с отзывами всё в порядке».",
     "average_rating(ratings) получает список оценок от 1 до 5 и возвращает среднее, округлённое до одного знака. Воспроизведи баг по шагам Иры и исправь функцию: для товара без отзывов она должна вернуть 0.0.",
     code="""
def average_rating(ratings):
    total = 0
    for r in ratings:
        total += r
    return round(total / len(ratings), 1)
""", lang="python", entry="average_rating",
     answer="""
def average_rating(ratings):
    if not ratings:
        return 0.0
    total = 0
    for r in ratings:
        total += r
    return round(total / len(ratings), 1)
""",
     tests=[
         {"input": [[5, 4, 4]], "expected": 4.3},
         {"input": [[5]], "expected": 5.0},
         {"input": [[]], "expected": 0.0},
         {"input": [[3, 4]], "expected": 3.5},
     ],
     explanation="Воспроизвести просто: вызов average_rating([]) — у товара без отзывов список пустой, len(ratings) равен нулю, и деление падает. Лечится проверкой в начале функции (guard clause): если оценок нет, сразу возвращаем 0.0. Что возвращать для пустого списка — 0.0 или None — решает продукт: например, None позволил бы фронту показать «пока нет отзывов» вместо нуля звёзд. Хорошая практика — добавить тест на пустой список, чтобы баг не вернулся при следующем рефакторинге.",
     hints=["Что вернёт len(ratings), если отзывов нет?",
            "Проверь пустой список в самом начале функции.",
            "if not ratings: return 0.0"])

task("c-debugging-03", "find_bug", 2, "qa",
     "Ира: «Баг: КофеБот падает на части заказов. В логах то KeyError: 'Латте', то KeyError: 'раф '. Воспроизводится, если написать напиток с большой буквы или поставить пробел у запятой. Меню я проверила — оно в порядке. Почини причину, а не заворачивай всё в try/except».",
     "order_total(lines) считает сумму заказа по строкам вида «напиток, количество» из сообщения в Telegram. Люди пишут как попало: с заглавной буквы, с пробелами до и после запятой. Исправь код так, чтобы такие заказы считались правильно. Меню не меняй.",
     code="""
MENU = {"эспрессо": 150, "американо": 170, "латте": 220, "раф": 260}


def parse_line(line):
    name, qty = line.split(",")
    return name, int(qty)


def order_total(lines):
    total = 0
    for line in lines:
        name, qty = parse_line(line)
        total += MENU[name] * qty
    return total
""", lang="python", entry="order_total",
     answer="""
MENU = {"эспрессо": 150, "американо": 170, "латте": 220, "раф": 260}


def parse_line(line):
    name, qty = line.split(",")
    return name.strip().lower(), int(qty)


def order_total(lines):
    total = 0
    for line in lines:
        name, qty = parse_line(line)
        total += MENU[name] * qty
    return total
""",
     tests=[
         {"input": [["эспрессо,1", "латте,2"]], "expected": 590},
         {"input": [["Латте, 2"]], "expected": 440},
         {"input": [["раф ,1", " американо,3"]], "expected": 770},
         {"input": [[]], "expected": 0},
     ],
     explanation="Ключ в сообщении KeyError выводится через repr, поэтому в 'раф ' виден пробел перед закрывающей кавычкой — это и есть подсказка. Проверить гипотезу можно, выведя print(repr(name)) в parse_line. Данные от пользователя надо нормализовать на входе, в одном месте: strip() убирает пробелы по краям, lower() приводит к нижнему регистру (для кириллицы тоже работает). Количество int() разбирает и с пробелами, поэтому падало именно название. try/except вокруг MENU[name] спрятал бы ошибку, и люди просто не получали бы кофе без объяснений.",
     hints=["Посмотри на ключ в KeyError: 'раф ' — что стоит внутри кавычек?",
            "Нормализуй название в parse_line: убери пробелы по краям и приведи к нижнему регистру.",
            "return name.strip().lower(), int(qty)"])

task("c-debugging-04", "incident", 2, "devops_colleague",
     "Дима: «С 9:12 КофеБот не принял ни одного заказа, офис без кофе. В 9:00 выкатили версию 2.4.0. В логах куча KeyError, ребята уже бросились править названия напитков. Глянь сам, пока они не натворили дел».",
     "Что на самом деле сломалось и что делать?",
     code="""
09:00:01 INFO     bot started, version=2.4.0
09:00:02 INFO     connected to Telegram API
09:12:40 INFO     order #5812 received: "латте, 2"
09:12:40 WARNING  menu cache is empty, loading from file
09:12:40 ERROR    failed to load menu: FileNotFoundError: [Errno 2] No such file or directory: '/app/data/menu.json'
09:12:40 ERROR    order #5812 failed: KeyError: 'латте'
09:12:55 INFO     order #5813 received: "капучино, 1"
09:12:55 WARNING  menu cache is empty, loading from file
09:12:55 ERROR    failed to load menu: FileNotFoundError: [Errno 2] No such file or directory: '/app/data/menu.json'
09:12:55 ERROR    order #5813 failed: KeyError: 'капучино'
...

$ ls /app/data /app/resources
ls: cannot access '/app/data': No such file or directory
/app/resources:
menu.json
""", lang="text",
     options=[
         "Первопричина — FileNotFoundError: в 2.4.0 меню переехало в /app/resources, а бот ищет /app/data. Откатиться, потом исправить путь",
         "Люди пишут названия напитков с ошибками и строчными буквами — добавить нормализацию названий в парсер заказов, чтобы убрать KeyError",
         "Telegram API отдаёт заказы с задержкой и обрывает сессию — подождать, пока Telegram восстановится",
         "Боту не хватает памяти для кэша меню, поэтому он пустой, — поднять лимит памяти и перезапустить контейнер",
         "Слишком много заказов одновременно после релиза — ограничить частоту запросов к боту до восстановления",
     ],
     answer=0,
     explanation="Читаем лог по времени: на каждом заказе сначала FileNotFoundError при загрузке меню и только потом KeyError. Меню пустое, поэтому любой напиток «не найден» — KeyError лишь симптом. ls подтверждает: в 2.4.0 папку переименовали, файл теперь в /app/resources. Раньше всё работало, сломалось сразу после релиза — значит, самое быстрое смягчение — откат, а исправление пути выкатываем уже спокойно. На будущее бот должен падать сразу при старте, если меню не загрузилось, а не рапортовать «started» и ломаться на первом заказе.",
     hints=["Найди самую первую ошибку по времени, а не самую частую.",
            "Что должно было случиться до KeyError, чтобы в меню не нашлось даже латте?"],
     time=15)


# =====================================================================
# c-docs — Документация и вопросы коллегам (intern)
# =====================================================================
topic("c-docs", """
Разработчик читает больше, чем пишет: документацию, чужой код, issue на GitHub. Умение быстро найти ответ — половина скорости работы.

Где искать, примерно по порядку
• Сообщение об ошибке. Скопируй его текст (без своих путей и имён) и поищи дословно, в кавычках. Часто первая же ссылка — issue с тем же случаем.
• Документация проекта: README, CONTRIBUTING, папка docs, внутренняя вики. Там ответы на «как запустить» и «как у нас принято».
• Официальная документация инструмента — главный источник правды. Проверь, что читаешь документацию своей версии: переключатель версии обычно наверху страницы, а свою версию смотри в requirements.txt, package.json или через python --version. Quickstart и Tutorial — чтобы начать, Reference (API) — за деталями: сигнатуры, параметры, значения по умолчанию, пометки Deprecated («устарело, используйте …»).
• Встроенная справка: команда --help, man команда (поиск внутри — /слово), help() в Python, подсказки IDE.
• Changelog и release notes — если «вчера работало, а после обновления сломалось».
• Issues и Discussions на GitHub — известные баги и обходные пути.
• Stack Overflow, статьи, ИИ-ассистенты — быстро, но проверяй дату ответа и версию и сверяй с документацией. Не копируй в прод код, который не понимаешь. И не отправляй во внешние сервисы код компании, логи с персональными данными и секреты.
• Исходники и тесты библиотеки — когда документации не хватает, тесты часто лучший пример использования.

Когда спрашивать
Таймбокс: разбирайся сам 20–30 минут. Нет прогресса — спрашивай. Молчать полдня хуже, чем задать «глупый» вопрос: команда теряет время, а срок горит. Но и спрашивать через минуту, не прочитав ошибку, тоже плохо.

Как задать вопрос, чтобы на него ответили
• Сразу к делу — без «привет, можно вопрос?» и ожидания ответа.
• Контекст: что делаешь и зачем (задача, ветка, какую команду запускаешь).
• Что ожидал и что получил. Текст ошибки — текстом в блоке кода, а не скриншотом: его можно скопировать и поискать.
• Что уже пробовал и что выяснил — чтобы тебе не советовали то же самое.
• Окружение, если важно: ОС, версии, ветка.
• Пиши в общий канал команды, а не в личку одному человеку: ответит тот, кто свободен, а ответ увидят и другие.
• Спрашивай о задаче, а не только о своём решении (проблема XY): не «как вырезать последние три символа строки», а «как получить расширение файла» — возможно, есть способ лучше.

Пример:

    КофеБот, ветка feature/menu-sync: при запуске python bot.py падает
    telegram.error.InvalidToken: The token was rejected by the server.
    Токен тестового бота взял из вики, в .env он есть (echo $TELEGRAM_TOKEN пустой —
    похоже, .env не подхватывается). Как у нас принято грузить .env локально?

После ответа
Поблагодари, отпишись, что помогло, и, если ответа не было в документации, допиши его в README или вики. Следующий новичок скажет спасибо.
""")

task("c-docs-01", "quiz", 1, "manager",
     "Оля, HR: «Вижу, ты второй час сидишь над запуском тестов Маркета. Не стесняйся спрашивать — для этого есть канал #lamp-market-dev, там быстро отвечают, если вопрос понятный. Какое сообщение напишешь?»",
     "Какое сообщение в общий канал команды лучше всего?",
     options=[
         "pytest на main: OperationalError: connection refused (localhost:5432) в test_orders.py. Postgres в Docker запущен, .env есть. Куда смотреть?",
         "Привет! Можно вопрос? Второй час бьюсь с тестами Маркета, уже всё перепробовал и ничего не помогает — кто сможет глянуть сегодня?",
         "Гена, привет! Созвонимся минут на двадцать? Тесты Маркета у меня не запускаются, проще показать экран, чем объяснять в чате",
         "[скриншот терминала] Вот что выдаёт pytest на моей машине — у кого-нибудь было такое? Контейнер вроде запущен, .env тоже на месте",
         "У меня не работают тесты на main, pytest падает с какой-то ошибкой про базу. Кто-нибудь может помочь? Очень срочно, горит задача LAMP-231, релиз завтра",
     ],
     answer=0,
     explanation="Хороший вопрос экономит время того, кто отвечает: в нём есть контекст (что запускаешь и на какой ветке), точный текст ошибки, который можно поискать, и то, что уже проверено. По такому сообщению коллега сразу видит, что база не принимает подключение, и может предложить следующий шаг. «Можно вопрос? Ничего не помогает» не содержит ни одной зацепки и заставляет переспрашивать, «какая-то ошибка про базу» и «очень срочно» тоже не помогают ответить. Просьба созвониться, адресованная одному человеку, отвлекает его, а ответ не увидят остальные. Скриншот нельзя скопировать и найти поиском — текст ошибки вставляй текстом.",
     hints=["Представь, что тебе нужно ответить на это сообщение, не задавая встречных вопросов.",
            "Контекст, текст ошибки, что уже пробовал."])

task("c-docs-02", "find_bug", 2, "devops_colleague",
     "Дима: «Обновил образ КофеБота до Python 3.12 — в CI полезли DeprecationWarning. Строку ты когда-то скопировал из ответа на Stack Overflow 2015 года. Открой официальную документацию и сделай, как советуют там, пока предупреждение не превратилось в ошибку».",
     "Какое исправление правильное по документации?",
     code="""
# bot/orders.py
created_at = datetime.utcnow()

# вывод pytest
bot/orders.py:14: DeprecationWarning: datetime.datetime.utcnow() is deprecated and scheduled for removal in a future version. Use timezone-aware objects to represent datetimes in UTC: datetime.datetime.now(datetime.UTC).

# docs.python.org/3.12/library/datetime.html
classmethod datetime.utcnow()
    Return the current UTC date and time, with tzinfo None.
    ...
    Warning: Because naive datetime objects are treated by many datetime
    methods as local times, it is preferred to use aware datetimes to
    represent times in UTC. As such, the recommended way to create an
    object representing the current time in UTC is by calling
    datetime.now(timezone.utc).
    Deprecated since version 3.12: Use datetime.now() with UTC instead.
""", lang="text",
     options=[
         "Заменить на datetime.now(timezone.utc) — текущее время в UTC с часовым поясом, как советует документация",
         "Отключить предупреждение: warnings.filterwarnings(\"ignore\", category=DeprecationWarning) в conftest.py",
         "Заменить на datetime.now() — это то же текущее время, только без устаревшего метода и предупреждения",
         "Зафиксировать Python 3.11 в Dockerfile и CI, где предупреждения нет, и вернуться к вопросу позже",
         "Заменить на datetime.utcnow().replace(tzinfo=None) — явно указать, что пояс не нужен, и предупреждение уйдёт",
     ],
     answer=0,
     explanation="Документация говорит прямо: utcnow() устарел с версии 3.12 и будет удалён, а рекомендуемая замена — datetime.now(timezone.utc) (или datetime.now(datetime.UTC)). Такой объект «знает» свой часовой пояс, и его нельзя случайно принять за местное время. datetime.now() без аргументов вернёт местное время сервера — на машине с часовым поясом Москвы все заказы сдвинутся на три часа. utcnow().replace(...) по-прежнему вызывает устаревший метод, так что предупреждение останется. Отключать предупреждения или замораживать Python — значит отложить поломку до следующего обновления. Ответ 2015 года был верным для своего времени, поэтому всегда смотри дату ответа и сверяйся с документацией своей версии. После замены проверь места, где created_at сравнивается с «наивными» датами: сравнение aware и naive падает с TypeError.",
     hints=["В документации есть строчка «the recommended way to create an object representing the current time in UTC».",
            "Чем datetime.now() без аргументов отличается от времени в UTC?"])

task("c-docs-03", "write_code", 2, "qa",
     "Ира: «После вчерашних правок доставка «Батона» считается странно. Хочу понять, что менялось в src/delivery.py за последнюю неделю и кем. Гена сказал, что git log это умеет, но я запуталась в справке. Вот кусок из man git-log, помоги составить команды».",
     "По выдержке из документации напиши две команды git log. Первая: коммиты за последнюю неделю, которые меняли src/delivery.py, по одной строке на коммит. Вторая: то же, но только коммиты автора Pavel и сразу с самими изменениями (патчами).",
     code="""
# Выдержка из man git-log:
#   --since=<date>, --after=<date>
#       Show commits more recent than a specific date.
#   --author=<pattern>
#       Limit the commits output to ones with author header lines
#       that match the specified pattern.
#   --oneline
#       This is a shorthand for "--pretty=oneline --abbrev-commit" used together.
#   -p, -u, --patch
#       Generate patch.
#   [--] <path>...
#       Show only commits that are enough to explain how the files
#       that match the specified paths came to be.

# Команда 1: коротко, за неделю, только src/delivery.py

# Команда 2: за неделю, только автор Pavel, с патчами

""", lang="bash",
     answer="""
# Выдержка из man git-log:
#   --since=<date>, --after=<date>
#       Show commits more recent than a specific date.
#   --author=<pattern>
#       Limit the commits output to ones with author header lines
#       that match the specified pattern.
#   --oneline
#       This is a shorthand for "--pretty=oneline --abbrev-commit" used together.
#   -p, -u, --patch
#       Generate patch.
#   [--] <path>...
#       Show only commits that are enough to explain how the files
#       that match the specified paths came to be.

# Команда 1: коротко, за неделю, только src/delivery.py
git log --oneline --since="1 week ago" -- src/delivery.py

# Команда 2: за неделю, только автор Pavel, с патчами
git log -p --since="1 week ago" --author="Pavel" -- src/delivery.py
""",
     tests=[
         rx("Есть git log с --oneline", r"""^\s*git\s+log\b[^\n]*\s--oneline\b"""),
         rx("Период задан через --since или --after",
            r"""^\s*git\s+log\b[^\n]*\s--(since|after)[=\s]+["']?((1|one|a)[\s.]*weeks?|7[\s.]*days|\d{4}-\d{2}-\d{2})""", "im"),
         rx("Команды ограничены файлом src/delivery.py", r"""^\s*git\s+log\b[^\n]*\s(--\s+)?src/delivery\.py\b"""),
         rx("Вторая команда: автор Pavel и патчи",
            r"""^\s*git\s+log\b(?=[^\n]*--author[=\s]+["']?Pavel)(?=[^\n]*(\s-p\b|\s-u\b|\s--patch\b))"""),
         rx("Обе команды — с периодом за неделю",
            r"""(^\s*git\s+log\b[^\n]*--(since|after)[^\n]*$[\s\S]*){2}"""),
     ],
     explanation="Всё нужное есть в выдержке: --since ограничивает период, --author фильтрует по автору (это шаблон, поэтому подойдёт и часть имени), --oneline сжимает коммит до строки, -p добавляет diff. Дату git понимает и в человеческом виде («1 week ago», «2 weeks ago», «yesterday»), и как 2026-09-17. Двойной дефис -- отделяет пути от имён веток: без него git может спутать файл с веткой, если имена совпадут. Навык здесь не в том, чтобы помнить флаги, а в том, чтобы за минуту найти их в man или --help: внутри man поиск — косая черта и слово.",
     hints=["Каждое условие из задания — это отдельный флаг из выдержки.",
            "--since понимает строки вроде \"1 week ago\".",
            "git log --oneline --since=\"1 week ago\" -- src/delivery.py"])

task("c-docs-04", "code_review", 2, "manager",
     "Оля, HR: «Стажёр из соседней команды обновил README КофеБота — как запустить бота локально. Ты сам недавно проходил онбординг и помнишь, где спотыкался. Посмотри свежим глазом, прежде чем Гена смержит: хочу, чтобы следующему новичку было проще».",
     "Выбери замечания, которые действительно стоит оставить к этому README.",
     code="""
# КофеБот

Бот для заказа кофе в офисе «Кодзилла Софт».

## Как запустить

1. pip install -r requirements.txt
2. python -m venv .venv
3. source .venv/bin/activate
4. Создай файл .env и положи туда токен бота
5. python bot.py

## Если не работает

Напиши Паше, он знает.
""", lang="text",
     options=[
         "pip install идёт до создания и активации venv — пакеты уедут в системный Python. Сначала venv и activate, потом pip",
         "Не сказано, где взять токен и какие ещё переменные нужны — добавить .env.example и ссылку, как получить тестового бота",
         "Не указана нужная версия Python — при другой версии люди получат непонятные ошибки установки",
         "«Напиши Паше» — знание в голове одного человека: описать частые ошибки и решения или дать ссылку на вики",
         "README надо писать только на английском: так принято в индустрии, иначе репозиторий выглядит непрофессионально",
         "Нумерованный список лучше заменить маркированным — шаги не обязательно выполнять строго по порядку",
         "Нужно добавить подробную историю проекта с 2019 года, чтобы новичок понимал, почему бот устроен именно так",
     ],
     answer=[0, 1, 2, 3],
     explanation="README — первое, что читает новичок, и каждый пропущенный шаг стоит ему часов. Порядок команд здесь ломает установку: pip install до активации окружения ставит пакеты в системный Python, а бот потом запустится без них. «Положи токен» без .env.example и инструкции — гарантированный вопрос в чат, как и отсутствие версии Python. «Напиши Паше» не работает, когда Паша в отпуске или уволился: знание должно жить в документации. Язык README выбирает команда, порядок шагов как раз важен — поэтому нумерованный список здесь правильный, а история проекта новичку для запуска не нужна.",
     hints=["Пройди по шагам README мысленно на чистом ноутбуке. Где застрянешь?",
            "Какие вопросы новичок всё равно задаст в чат, прочитав этот README?"])


# =====================================================================
# c-git-branches — Git: ветки, merge и pull request (junior)
# =====================================================================
topic("c-git-branches", """
Ветка в git — просто подвижный указатель на коммит. Когда ты коммитишь, указатель текущей ветки сдвигается на новый коммит. HEAD показывает, где ты сейчас (обычно — на какой ветке). Ветки дешёвые, поэтому под каждую задачу заводят свою.

    git branch                        # локальные ветки
    git branch -a                     # и удалённые (remotes/origin/...)
    git switch main                   # перейти на ветку (старый вариант: git checkout main)
    git switch -c fix/LAMP-251-cart   # создать ветку от текущего места и перейти в неё
    git push -u origin fix/LAMP-251-cart   # первый push, -u связывает с удалённой веткой
    git log --oneline --graph --all   # история всех веток картинкой
    git branch -d fix/LAMP-251-cart   # удалить смерженную ветку (-D — принудительно)

Важно: git switch -c создаёт ветку от того коммита, на котором ты стоишь. Поэтому перед новой задачей: git switch main, git pull — и только потом новая ветка.

Удалённые ветки
origin/main — локальная копия того, что было на сервере при последнем git fetch. fetch скачивает новые коммиты, но не трогает твои ветки. git pull = fetch + merge удалённой ветки в текущую (или rebase, если так настроено).

Merge
git merge feature вливает ветку feature в текущую. Бывает два случая:
• fast-forward — текущая ветка не ушла вперёд с момента ответвления, и git просто передвигает её указатель на последний коммит feature. Новых коммитов нет, история прямая;
• merge-коммит — обе ветки получили новые коммиты, и git создаёт коммит слияния с двумя родителями. Если обе меняли одни и те же строки — конфликт (это следующая тема).
git merge --no-ff создаёт merge-коммит даже там, где возможен fast-forward, — чтобы в истории было видно, что фича шла отдельной веткой. Кнопка «Create a merge commit» на GitHub работает именно так.

Pull request (в GitLab — merge request)
Это просьба влить ветку в main с обсуждением и проверками:
• ветка от свежего main с понятным именем: feature/LAMP-231-reviews-filter, fix/LAMP-251-cart-rounding;
• осмысленные коммиты, push, PR с описанием: что сделано, зачем, как проверить;
• CI гоняет тесты и линтеры, коллеги ревьюят, ты правишь замечания новыми коммитами в ту же ветку — PR обновится сам;
• main ушёл вперёд — подтяни его в свою ветку (git fetch и git merge origin/main), прогони тесты и сделай обычный push, force push для этого не нужен;
• после апрува — merge (обычный, squash — все коммиты в один, или rebase — как принято в команде), ветку удаляют.
main обычно защищён (protected branch): пушить в него напрямую нельзя, только через PR с зелёным CI и апрувом. Даже срочный хотфикс идёт через PR — его просто ревьюят быстро.

Модели ветвления
• GitHub flow / trunk-based — одна основная ветка main и короткоживущие ветки задач (дни, а не недели); релизы из main, недоделанное прячут за фиче-флагами. Так работает большинство команд.
• Git flow — ветки develop, release/*, hotfix/* и main. Тяжелее, встречается там, где релизы редкие и версионные: мобильные приложения, коробочный софт.
Чем дольше живёт ветка, тем больнее её вливать: мерджи чаще, PR — меньше.
""")

task("c-git-branches-01", "write_code", 1, "teamlead",
     "Гена: «Бери задачу LAMP-231 — фильтр отзывов по рейтингу. Работаем как обычно: ветка от свежего main, имя по шаблону feature/<номер>-<кратко>. Как только будет первый коммит, сразу пушь ветку, чтобы открыть черновик PR».",
     "Допиши команды: 1) переключись на main и подтяни свежие изменения; 2) создай ветку feature/LAMP-231-reviews-filter и перейди в неё; 3) после коммита (он уже есть в заготовке) отправь ветку на origin, связав её с удалённой веткой.",
     code="""
# 1. Свежий main


# 2. Новая ветка для задачи


# (тут ты пишешь код)
git add src/reviews/filters.py tests/test_reviews_filters.py
git commit -m "Add rating filter for reviews"

# 3. Первый push ветки

""", lang="bash",
     answer="""
# 1. Свежий main
git switch main
git pull

# 2. Новая ветка для задачи
git switch -c feature/LAMP-231-reviews-filter

# (тут ты пишешь код)
git add src/reviews/filters.py tests/test_reviews_filters.py
git commit -m "Add rating filter for reviews"

# 3. Первый push ветки
git push -u origin feature/LAMP-231-reviews-filter
""",
     tests=[
         rx("Сначала переход на main, потом pull", r"""^\s*git\s+(switch|checkout)\s+main\b[\s\S]*^\s*git\s+pull\b"""),
         rx("Ветка создаётся после pull",
            r"""^\s*git\s+pull\b[\s\S]*^\s*git\s+(switch\s+(-c|--create)|checkout\s+-b)\s+feature/LAMP-231-reviews-filter\b"""),
         rx("Ветка создаётся до коммита",
            r"""(switch\s+(-c|--create)|checkout\s+-b)\s+feature/LAMP-231-reviews-filter\b[\s\S]*^\s*git\s+commit\b"""),
         rx("push с -u (--set-upstream) в origin",
            r"""^\s*git\s+push\s+(-u|--set-upstream)\s+origin\s+(feature/LAMP-231-reviews-filter|HEAD)\b"""),
         nrx("Никаких push в main", r"""git\s+push\s+[^\n]*\bmain\b"""),
     ],
     explanation="git switch -c создаёт ветку от текущего коммита, поэтому сначала переходим на main и делаем git pull — иначе ветка начнётся от устаревшего кода или, хуже, от чужой незаконченной ветки. Имя с номером задачи сразу связывает ветку с тикетом. Первый push делаем с -u (--set-upstream): git запомнит, что локальная ветка связана с origin/feature/LAMP-231-reviews-filter, и дальше хватит просто git push и git pull. После push GitHub сам предложит открыть PR — можно сразу черновиком (draft), чтобы команда видела, что задача в работе.",
     hints=["Перед созданием ветки убедись, что main свежий.",
            "Создать ветку и перейти в неё — git switch -c имя.",
            "git push -u origin feature/LAMP-231-reviews-filter"])

task("c-git-branches-02", "quiz", 2, "teamlead",
     "Марина, сеньор: «Перед мерджем посмотри на граф и скажи, что сделает git merge. На собеседованиях джунов это спрашивают почти всегда, а в работе ещё чаще приходится понимать самому».",
     "Ты на ветке main. Что произойдёт после git merge feature/LAMP-231-reviews-filter?",
     code="""
$ git log --oneline --graph --all
* 7d3e1a9 (feature/LAMP-231-reviews-filter) Add tests for rating filter
* c41b8f2 Add rating filter for reviews
* 9a0f5d3 (HEAD -> main, origin/main) Fix typo in product card
* 2b7c6e1 Add product card skeleton

$ git merge feature/LAMP-231-reviews-filter
""", lang="text",
     options=[
         "Fast-forward: main передвинется на 7d3e1a9, новых коммитов не появится",
         "Git создаст новый merge-коммит с двумя родителями: 9a0f5d3 и 7d3e1a9",
         "Будет конфликт: обе ветки ссылаются на коммит 9a0f5d3 и меняли историю после него",
         "Коммиты c41b8f2 и 7d3e1a9 скопируются в main с новыми хешами поверх 9a0f5d3",
         "main передвинется на 7d3e1a9, а ветка feature удалится автоматически",
     ],
     answer=0,
     explanation="main указывает на 9a0f5d3, и этот коммит — прямой предок обоих коммитов фичи: после ответвления в main ничего не добавилось. Расходящейся истории нет, поэтому git делает fast-forward — просто переносит указатель main на 7d3e1a9. Merge-коммит появился бы, если бы в main за это время пришли новые коммиты, или при git merge --no-ff (так работает кнопка «Create a merge commit» на GitHub). Новые хеши — это поведение rebase и cherry-pick, конфликтовать тут нечему, а ветку после мерджа удаляют руками: git branch -d.",
     hints=["Посмотри, появлялись ли в main новые коммиты после того, как от него ответвилась фича.",
            "Если догонять нечего, git может просто передвинуть указатель."])

task("c-git-branches-03", "find_bug", 2, "qa",
     "Ира: «Открыла твой PR «Fix cart total rounding», чтобы проверить округление, а там девять коммитов и 23 файла, половина — фильтр отзывов, который ещё даже не прошёл ревью. Что ты туда запихнул?»",
     "Что пошло не так и как правильно исправить?",
     code="""
$ history | tail -n 5
  502  git switch feature/LAMP-231-reviews-filter
  503  git commit -am "Add rating filter for reviews"
  504  git switch -c fix/LAMP-251-cart-rounding
  505  git commit -am "Fix cart total rounding"
  506  git push -u origin fix/LAMP-251-cart-rounding

$ git log --oneline --graph -5
* e5a1c07 (HEAD -> fix/LAMP-251-cart-rounding, origin/fix/LAMP-251-cart-rounding) Fix cart total rounding
* 7d3e1a9 (origin/feature/LAMP-231-reviews-filter, feature/LAMP-231-reviews-filter) Add rating filter for reviews
* 0b9d4c2 Add rating param to reviews API
* 5f2e8a1 Add reviews filter UI draft
* 3c6b1f0 Add reviews filter model
""", lang="text",
     options=[
         "Ветка fix создана от ветки фильтра, а не от main. Пересоздать её от свежего main и перенести туда только коммит округления",
         "GitHub неправильно посчитал diff из-за кэша — достаточно закрыть и заново открыть тот же PR, и лишние коммиты сами уйдут из списка",
         "Поменять в PR базовую ветку на ветку фильтра и влить фикс туда — в main он попадёт вместе с фильтром",
         "Сделать git merge main в ветку фикса — git увидит, что коммиты фильтра не из main, и сам уберёт их из PR",
         "Удалить ветку фильтра локально и на GitHub — тогда её коммиты пропадут из PR автоматически",
     ],
     answer=0,
     explanation="В истории команд видно: git switch -c выполнен, когда ты стоял на ветке фильтра, а новая ветка всегда начинается от текущего коммита. Поэтому ветка фикса содержит все коммиты фильтра, и PR в main честно их показывает. Правильно — начать заново от свежего main (git switch main, git pull, git switch -c …) и перенести один коммит e5a1c07 — это делает git cherry-pick из следующей темы. Merge main ничего не удалит, а удаление ветки фильтра не уберёт её коммиты из истории фикса. Смена базы PR сделала бы срочный фикс заложником чужого ревью. Чтобы не ошибаться, можно явно указывать точку старта: git switch -c fix/… origin/main.",
     hints=["От какой ветки ты стоял, когда выполнял git switch -c?",
            "Новая ветка начинается от текущего коммита, а не от main."])

task("c-git-branches-04", "incident", 2, "devops_colleague",
     "Дима: «На проде Лампового Маркета падает оплата картой — примерно каждая пятая покупка. Ты нашёл баг и уже закоммитил фикс — отлично, но он почему-то не выкатывается. Деплой у нас запускается по мерджу в main, я жду».",
     "Что делать, чтобы фикс быстро и правильно попал на прод?",
     code="""
$ git branch --show-current
main
$ git log --oneline -2
3c9e7b2 (HEAD -> main) Fix payment amount rounding for card payments
a81f4d0 (origin/main) Merge pull request #477 from codezilla/feature/gift-cards
$ git push origin main
Enumerating objects: 7, done.
Writing objects: 100% (4/4), 512 bytes | 512.00 KiB/s, done.
remote: error: GH006: Protected branch update failed for refs/heads/main.
remote: error: Changes must be made through a pull request.
To github.com:codezilla/lamp-market.git
 ! [remote rejected] main -> main (protected branch hook declined)
error: failed to push some refs to 'github.com:codezilla/lamp-market.git'
""", lang="text",
     options=[
         "Создать от текущего коммита ветку hotfix/…, запушить её, открыть PR и позвать ревьюера в канал инцидента",
         "Сделать git push --force origin main — у хотфикса приоритет, а история main от этого не пострадает",
         "Попросить Диму временно снять защиту с main, запушить фикс и сразу включить защиту обратно",
         "Скинуть Диме патч в личку — пусть сам положит исправленный файл на сервер, так быстрее всего",
         "Подождать до утра, когда Гена сможет посмотреть фикс и сам смержить его в main без спешки",
     ],
     answer=0,
     explanation="main защищён: сервер принимает изменения только через pull request, и force push тоже будет отклонён. Это не помеха, а страховка: даже в инциденте нужен зелёный CI и второй взгляд — фикс, выкаченный в спешке без проверки, часто становится вторым инцидентом. git switch -c hotfix/payment-rounding создаст ветку прямо на коммите 3c9e7b2, дальше push, PR и просьба о срочном ревью в канале инцидента — это минуты. Снятие защиты и правки прямо на сервере ломают прослеживаемость, а следующий деплой затрёт ручную правку. После мерджа локальный main стоит привести к серверному (git fetch и git reset --hard origin/main — коммит при этом сохранён в ветке hotfix).",
     hints=["Прочитай ошибку сервера: как он разрешает менять main?",
            "Коммит уже есть — его можно просто положить в новую ветку."],
     time=15)

task("c-git-branches-05", "write_code", 3, "teamlead",
     "Марина, сеньор: «Твой PR с фильтром отзывов висит два дня, а за это время в main влили рефакторинг API отзывов. GitHub пишет “This branch is out-of-date with the base branch”, и CI не даёт смержить. Обнови ветку — только без force push, её уже ревьюят».",
     "Ты на ветке feature/LAMP-231-reviews-filter. Напиши команды: скачай свежие изменения с сервера; влей актуальный origin/main в свою ветку через merge; прогони тесты командой make test; отправь обновлённую ветку обычным push.",
     code="""
git branch --show-current
# feature/LAMP-231-reviews-filter

""", lang="bash",
     answer="""
git branch --show-current
# feature/LAMP-231-reviews-filter
git fetch origin
git merge origin/main
make test
git push
""",
     tests=[
         rx("Свежие изменения скачаны (fetch или pull)", r"""^\s*git\s+(fetch|pull)\b"""),
         rx("В ветку влит origin/main",
            r"""^\s*git\s+(merge\s+(--no-ff\s+)?origin/main\b|pull\s+(--no-rebase\s+)?origin\s+main\b)"""),
         rx("Сначала скачать, потом влить",
            r"""^\s*git\s+fetch\b[\s\S]*^\s*git\s+merge\s+(--no-ff\s+)?origin/main\b|^\s*git\s+pull\s+(--no-rebase\s+)?origin\s+main\b"""),
         rx("Тесты после мерджа и до push",
            r"""(merge\s+(--no-ff\s+)?origin/main|pull\s+(--no-rebase\s+)?origin\s+main)\b[\s\S]*^\s*make\s+test\b[\s\S]*^\s*git\s+push\b"""),
         nrx("Без force push", r"""git\s+push\b[^\n]*(--force|\s-f\b|\s\+)"""),
         nrx("Без rebase (он потребует force push)", r"""git\s+(rebase\b|pull\s+[^\n]*--rebase)"""),
     ],
     explanation="git fetch обновляет origin/main — твою локальную копию серверного main, а git merge origin/main вливает её в текущую ветку отдельным merge-коммитом. Если твои изменения и рефакторинг задели одни и те же строки, будет конфликт — его разрешают до коммита. Тесты обязательны: каждая сторона по отдельности зелёная, но вместе они могут сломаться, особенно после рефакторинга API. История ветки только выросла, поэтому хватает обычного git push, а у ревьюеров не пропадут комментарии к коммитам. Rebase дал бы более прямую историю, но переписал бы уже опубликованные коммиты и потребовал force push — на ветке под ревью так не делают. Кнопка «Update branch» на GitHub делает тот же merge.",
     hints=["Сначала обнови origin/main с сервера, потом влей его в свою ветку.",
            "После мерджа всегда прогоняй тесты: две зелёные ветки вместе могут дать красную.",
            "git fetch origin → git merge origin/main → make test → git push"])


# =====================================================================
# c-git-conflicts — Git: конфликты, rebase и откат (junior)
# =====================================================================
topic("c-git-conflicts", """
Конфликт возникает, когда две ветки поменяли одни и те же строки (или одна удалила файл, а другая его правила) и git не может сам решить, какой вариант верный. Это нормальная часть работы, а не авария.

Как выглядит конфликт

    <<<<<<< HEAD
    FREE_DELIVERY_FROM = 3000
    =======
    FREE_DELIVERY_FROM = 3500
    >>>>>>> main

Между <<<<<<< и ======= — версия текущей ветки (HEAD), между ======= и >>>>>>> — версия вливаемой. git status покажет такие файлы в разделе Unmerged paths (both modified).

Как разрешать
• Разберись, зачем каждая сторона меняла код: git log, описание PR, спроси автора. Часто нужны обе правки, а не «мой вариант» или «их вариант».
• Отредактируй файл до нужного итогового кода и удали все маркеры.
• Прогони тесты, затем git add файл и git commit (при merge) или git rebase --continue (при rebase).
• Запутался — git merge --abort или git rebase --abort вернут всё как было.
Конфликтов меньше, когда PR маленькие, main вливается в ветку часто, а форматирование всего файла не смешивается с логикой.

Rebase
git rebase main переносит твои коммиты так, будто ветка начата от свежего main: git по очереди «переигрывает» каждый коммит поверх main. История становится прямой, но у коммитов появляются новые хеши — это уже другие коммиты. Отсюда правила:
• ребейзи только свою ветку, с которой больше никто не работает; общие ветки (main, develop) не ребейзят никогда;
• после rebase уже запушенной ветки обычный push отклонят — нужен git push --force-with-lease: в отличие от --force, он не затрёт чужие коммиты, если кто-то успел запушить.

Interactive rebase — причесать свои коммиты перед ревью:

    git rebase -i main

Откроется план — список коммитов, сверху самые старые. Меняешь слово в начале строки: pick — оставить, reword — поменять сообщение, squash — склеить с предыдущей строкой и объединить сообщения, fixup — склеить и выбросить сообщение, drop — удалить коммит (или просто удалить строку). Строки можно переставлять: fixup приклеивается к коммиту строкой выше. Сохранил и закрыл редактор — git выполнит план сверху вниз.

Откат изменений
• git revert хеш — новый коммит, отменяющий изменения указанного. История не переписывается, поэтому это безопасный способ откатить то, что уже в общей ветке. Merge-коммит откатывают с указанием основной линии: git revert -m 1 хеш.
• git reset хеш — передвинуть указатель ветки: --soft (изменения останутся в индексе), --mixed (по умолчанию, останутся в рабочей папке), --hard (изменения удаляются). Только для коммитов, которые ещё не запушены.
• git reflog — журнал всех перемещений HEAD. Коммиты, «потерянные» после reset --hard или неудачного rebase, лежат в нём ещё как минимум 30 дней: нашёл хеш — git branch rescue хеш или git reset --hard хеш.

Перенос и откладывание
• git cherry-pick хеш — скопировать один коммит в текущую ветку (те же изменения, новый хеш). Удобно, чтобы перенести хотфикс.
• git stash push -m "описание" — спрятать незакоммиченные изменения и получить чистую рабочую папку; git stash list — список, git stash pop — вернуть и удалить из списка, git stash apply — вернуть, оставив копию. Новые неотслеживаемые файлы stash по умолчанию не берёт — для них флаг -u.
""")

task("c-git-conflicts-01", "write_code", 2, "teamlead",
     "Гена: «Влил main в твою ветку с экспресс-доставкой для «Батона» — и получил конфликт в delivery.py. В main Нина попросила поднять порог бесплатной доставки до 3500 ₽, а у тебя в ветке — экспресс за +390 ₽. Нужны оба изменения, не выкидывай ни одно».",
     "Разреши конфликт в delivery_price: убери маркеры и оставь код, в котором работают оба изменения — доставка бесплатная от 3500 ₽ (иначе 299 ₽), а за экспресс всегда добавляется 390 ₽, даже если обычная доставка бесплатная.",
     code="""
def delivery_price(total, express=False):
<<<<<<< HEAD
    if total >= 3000:
        price = 0
    else:
        price = 299
    if express:
        price += 390
    return price
=======
    if total >= 3500:
        return 0
    return 299
>>>>>>> main
""", lang="python", entry="delivery_price",
     answer="""
def delivery_price(total, express=False):
    if total >= 3500:
        price = 0
    else:
        price = 299
    if express:
        price += 390
    return price
""",
     tests=[
         {"input": [3200], "expected": 299},
         {"input": [3500], "expected": 0},
         {"input": [4000, True], "expected": 390},
         {"input": [1000, True], "expected": 689},
         {"input": [3499], "expected": 299},
     ],
     explanation="Ты вливал main в свою ветку, поэтому HEAD — это твоя версия (экспресс, но со старым порогом 3000), а блок после ======= — версия main (новый порог, но без экспресса). Выбрать одну сторону целиком нельзя: пропадёт либо экспресс, либо решение Нины. Правильное разрешение — взять структуру твоего кода и порог 3500 из main. После правки удаляешь все маркеры, прогоняешь тесты, делаешь git add delivery.py и git commit. Тесты на обе правки — лучший способ убедиться, что при разрешении ничего не потерялось.",
     hints=["Сверху — твоя ветка (HEAD), снизу — main. Что полезного в каждой из них?",
            "Возьми свою версию с экспрессом и поменяй в ней порог.",
            "if total >= 3500: price = 0, иначе 299; при express прибавь 390."])

task("c-git-conflicts-02", "write_code", 2, "manager",
     "Стас: «Срочно: на главной Маркета опечатка в баннере про доставку, а реклама уже крутится! Бросай на пять минут фильтр отзывов и поправь в отдельной ветке от свежего main. Гена просил передать: незакоммиченные правки фильтра не потеряй и в хотфикс не тащи».",
     "В ветке feature/LAMP-231-reviews-filter у тебя незакоммиченные изменения. Допиши команды: 1) спрячь их в stash с описанием; 2) перейди на main, подтяни изменения и создай ветку fix/LAMP-260-banner-typo; правка и коммит уже есть в заготовке; 3) запушь ветку хотфикса с привязкой к origin; 4) вернись в ветку фильтра и достань спрятанные изменения.",
     code="""
git status --short
#  M src/reviews/filters.py
#  M src/reviews/api.py

# 1. Спрячь незаконченную работу


# 2. Свежий main и ветка для хотфикса


# правка опечатки
sed -i 's/Беслпатная доставка/Бесплатная доставка/' templates/index.html
git add templates/index.html
git commit -m "Fix typo in delivery banner"

# 3. Отправь ветку хотфикса


# 4. Вернись к фильтру и достань изменения

""", lang="bash",
     answer="""
git status --short
#  M src/reviews/filters.py
#  M src/reviews/api.py

# 1. Спрячь незаконченную работу
git stash push -m "reviews filter WIP"

# 2. Свежий main и ветка для хотфикса
git switch main
git pull
git switch -c fix/LAMP-260-banner-typo

# правка опечатки
sed -i 's/Беслпатная доставка/Бесплатная доставка/' templates/index.html
git add templates/index.html
git commit -m "Fix typo in delivery banner"

# 3. Отправь ветку хотфикса
git push -u origin fix/LAMP-260-banner-typo

# 4. Вернись к фильтру и достань изменения
git switch feature/LAMP-231-reviews-filter
git stash pop
""",
     tests=[
         rx("Изменения спрятаны в stash до перехода на main",
            r"""^\s*git\s+stash(\s+(push|save))?\b[^\n]*\n[\s\S]*^\s*git\s+(switch|checkout)\s+main\b"""),
         rx("У stash есть описание",
            r"""^\s*git\s+stash\s+((push\s+)?([^\n]*\s)?(-m|--message)[=\s]+\S|save\s+\S)"""),
         rx("main → pull → новая ветка fix/LAMP-260-banner-typo",
            r"""^\s*git\s+(switch|checkout)\s+main\b[\s\S]*^\s*git\s+pull\b[\s\S]*^\s*git\s+(switch\s+(-c|--create)|checkout\s+-b)\s+fix/LAMP-260-banner-typo\b"""),
         rx("Коммит сделан уже в ветке хотфикса",
            r"""(switch\s+(-c|--create)|checkout\s+-b)\s+fix/LAMP-260-banner-typo\b[\s\S]*^\s*git\s+commit\b"""),
         rx("push ветки хотфикса с -u",
            r"""^\s*git\s+push\s+(-u|--set-upstream)\s+origin\s+(fix/LAMP-260-banner-typo|HEAD)\b"""),
         rx("Возврат в ветку фильтра, затем stash pop/apply после коммита",
            r"""^\s*git\s+commit\b[\s\S]*^\s*git\s+(switch|checkout)\s+feature/LAMP-231-reviews-filter\b[\s\S]*^\s*git\s+stash\s+(pop|apply)\b"""),
         nrx("Спрятанное не выбрасывается", r"""git\s+stash\s+(drop|clear)\b"""),
     ],
     explanation="git stash push -m убирает незакоммиченные правки в отдельное хранилище и возвращает рабочую папку к последнему коммиту — теперь можно спокойно переключаться, и изменения фильтра не уедут в хотфикс. Описание помогает не гадать через неделю, что лежит в stash@{2}. Ветка хотфикса создаётся от свежего main, иначе в PR попадёт лишнее. После push возвращаемся в свою ветку и делаем git stash pop: он применяет изменения и удаляет запись из списка (apply оставил бы копию). Помни, что stash по умолчанию не прячет новые неотслеживаемые файлы — для них нужен флаг -u.",
     hints=["Перед переключением веток рабочая папка должна быть чистой — для этого есть stash.",
            "Порядок: stash → switch main → pull → switch -c → (коммит) → push -u → switch обратно → stash pop.",
            "git stash push -m \"reviews filter WIP\" … git stash pop"])

task("c-git-conflicts-03", "write_code", 3, "teamlead",
     "Марина, сеньор: «Перед ревью приведи историю ветки в порядок: я не хочу читать коммиты «wip» и «опечатка», а отладочный print вообще не должен попасть в main. Ты уже запустил git rebase -i main, и открылся редактор с планом».",
     "Перед тобой файл-план git rebase -i (это не скрипт, а инструкции для git). Отредактируй его: 1) коммит «опечатка в фильтре» приклей к коммиту «Add rating filter for reviews», выбросив его сообщение; 2) коммит «wip» так же приклей к «Add tests for rating filter»; 3) коммит «debug print» удали; 4) порядок двух основных коммитов не меняй.",
     code="""
pick a1c3e5f Add rating filter for reviews
pick b2d4f60 Add tests for rating filter
pick c7e9a12 wip
pick d8f0b23 debug print
pick e4a6c34 опечатка в фильтре

# Rebase 9f2b7d1..e4a6c34 onto 9f2b7d1 (5 commands)
#
# Commands:
# p, pick <commit> = use commit
# r, reword <commit> = use commit, but edit the commit message
# s, squash <commit> = use commit, but meld into previous commit
# f, fixup <commit> = like squash, but keep only the previous commit's log message
# d, drop <commit> = remove commit
#
# These lines can be re-ordered; they are executed from top to bottom.
""", lang="bash",
     answer="""
pick a1c3e5f Add rating filter for reviews
fixup e4a6c34 опечатка в фильтре
pick b2d4f60 Add tests for rating filter
fixup c7e9a12 wip
drop d8f0b23 debug print

# Rebase 9f2b7d1..e4a6c34 onto 9f2b7d1 (5 commands)
#
# Commands:
# p, pick <commit> = use commit
# r, reword <commit> = use commit, but edit the commit message
# s, squash <commit> = use commit, but meld into previous commit
# f, fixup <commit> = like squash, but keep only the previous commit's log message
# d, drop <commit> = remove commit
#
# These lines can be re-ordered; they are executed from top to bottom.
""",
     tests=[
         rx("«опечатка» — fixup сразу под коммитом фильтра",
            r"""^\s*(p|pick)\s+a1c3e5f\b[^\n]*\n\s*(f|fixup)\s+e4a6c34\b"""),
         rx("«wip» — fixup сразу под коммитом с тестами",
            r"""^\s*(p|pick)\s+b2d4f60\b[^\n]*\n\s*(f|fixup)\s+c7e9a12\b"""),
         nrx("«debug print» удалён (drop или строка убрана)",
             r"""^\s*(p|pick|r|reword|e|edit|s|squash|f|fixup)\s+d8f0b23\b"""),
         rx("Основные коммиты остались в прежнем порядке",
            r"""^\s*(p|pick)\s+a1c3e5f\b[\s\S]*^\s*(p|pick)\s+b2d4f60\b"""),
     ],
     explanation="Git выполняет план сверху вниз, а fixup склеивает коммит с тем, что стоит строкой выше. Поэтому «опечатку» надо не только пометить fixup, но и переставить сразу под коммит фильтра — оставь её внизу, и она приклеится к чужому коммиту. «wip» уже стоит под тестами, ему достаточно сменить pick на fixup. squash тоже склеил бы коммиты, но открыл бы редактор с объединёнными сообщениями, а нам они не нужны. Для «debug print» подойдёт drop или просто удалённая строка. После rebase у коммитов новые хеши, так что уже запушенную ветку отправляют через git push --force-with-lease — на своей ветке до ревью это нормально. Лайфхак на будущее: git commit --fixup a1c3e5f и затем git rebase -i --autosquash main — git сам расставит строки.",
     hints=["fixup приклеивается к строке, которая стоит прямо над ним. Строки можно переставлять.",
            "«опечатку» нужно перенести наверх, под коммит фильтра.",
            "pick a1c3e5f → fixup e4a6c34 → pick b2d4f60 → fixup c7e9a12 → drop d8f0b23"])

task("c-git-conflicts-04", "find_bug", 3, "teamlead",
     "Гена: «Говоришь, хотел убрать один неудачный коммит, а выполнил git reset --hard HEAD~3, и три коммита с сегодняшней работой пропали из git log? Ветку ты ещё не пушил. Спокойно, в git почти ничего не теряется. Покажи reflog».",
     "Как вернуть три пропавших коммита?",
     code="""
$ git reflog -6
4b1e9d0 (HEAD -> feature/LAMP-231-reviews-filter) HEAD@{0}: reset: moving to HEAD~3
e8c2f71 HEAD@{1}: commit: Add rating filter to reviews API
a93d5b6 HEAD@{2}: commit: Add rating filter UI
71f0c2e HEAD@{3}: commit: Add tests for rating filter
4b1e9d0 (HEAD -> feature/LAMP-231-reviews-filter) HEAD@{4}: commit: Add reviews sorting
2d6a8f3 HEAD@{5}: checkout: moving from main to feature/LAMP-231-reviews-filter

$ git status --short
$
""", lang="text",
     options=[
         "git reset --hard e8c2f71 (или HEAD@{1}): ветка снова укажет на последний коммит, и все три вернутся",
         "Никак: reset --hard удаляет коммиты безвозвратно, а ветка не пушилась — придётся писать весь код заново",
         "git revert HEAD~3 — revert создаёт обратный коммит и как раз отменяет последний reset",
         "git stash pop — перед reset --hard git автоматически сохраняет незакоммиченные изменения в stash",
         "git pull — коммиты подтянутся с сервера, куда их отправила IDE при автосохранении",
         "git cherry-pick 71f0c2e — он вернёт этот коммит и все, что шли после него",
     ],
     answer=0,
     explanation="reset --hard лишь передвинул указатель ветки на три коммита назад, сами коммиты никуда не делись — просто на них больше ничего не ссылается. reflog записывает каждое перемещение HEAD, и в HEAD@{1} виден хеш e8c2f71 — состояние прямо перед reset. git reset --hard e8c2f71 возвращает ветку туда; это безопасно, потому что git status пустой и терять нечего (проверяй это всегда перед --hard). Ещё осторожнее — сначала git branch rescue e8c2f71. revert создаёт новые коммиты с обратными изменениями, stash тут ни при чём, с сервера ничего не скачается, раз ветка не пушилась, а cherry-pick копирует один коммит, а не три. Чтобы убрать один коммит, правильнее было сделать git revert или drop в git rebase -i.",
     hints=["reflog — журнал всех мест, где побывал HEAD. Где он был прямо перед reset?",
            "Нужно передвинуть ветку обратно на хеш из HEAD@{1}."])

task("c-git-conflicts-05", "incident", 3, "devops_colleague",
     "Дима: «После мерджа PR #482 с новым расчётом скидок корзина на проде Маркета отдаёт 500 на каждом третьем заказе. Деплой идёт автоматически из main, main защищён. Автор PR в отпуске, чинить вперёд некому. Нужно вернуть прод как было, и быстро».",
     "Выбери все правильные действия, чтобы безопасно вернуть прод.",
     code="""
$ git log --oneline --graph -7 origin/main
*   f41c9e2 Merge pull request #482 from codezilla/feature/discounts-v2
|\\
| * 8d20b7a Apply discount before delivery
| * 3a7f6c1 Rewrite discount calculation
|/
*   c0e5d18 Merge pull request #479 from codezilla/fix/cart-rounding
|\\
| * 51b9a2e Fix cart total rounding

$ git show --no-patch --format='%h parents: %p' f41c9e2
f41c9e2 parents: c0e5d18 8d20b7a

# Метрики: 5xx корзины 0.1% до 14:02 (деплой f41c9e2), после — 31%
""", lang="text",
     options=[
         "Откатить мердж: git revert -m 1 f41c9e2 в отдельной ветке, быстрый PR с ревью и мердж в main — деплой выкатит откат",
         "Сразу написать в канал инцидента, что откатываем #482, чтобы никто не мерджил в main поверх",
         "После отката сверить 5xx с нормой и завести задачу на возврат фичи — через revert revert-коммита и фикс",
         "git reset --hard c0e5d18 на main и git push --force: история станет чистой, будто #482 и не было",
         "Сделать git revert 8d20b7a — достаточно отменить последний коммит фичи, где скидка применяется до доставки",
         "Удалить ветку feature/discounts-v2 на GitHub — её изменения исчезнут из main вместе с веткой",
     ],
     answer=[0, 1, 2],
     explanation="Ошибки начались ровно с деплоя f41c9e2, значит, самое быстрое смягчение — откатить этот мердж. git revert -m 1 создаёт новый коммит, который отменяет всё, что принёс merge; -m 1 говорит, что основная линия — первый родитель c0e5d18, то есть сторона main. История не переписывается, поэтому откат проходит обычным путём через PR и CI (на GitHub для этого есть кнопка Revert в самом PR). reset с force push переписал бы общую историю и отклонён защитой main, revert одного коммита оставит половину фичи, а удаление ветки не трогает уже влитые коммиты. Важно понимать и последствие: git считает эти изменения уже влитыми, поэтому повторный мердж той же ветки их не вернёт — нужен revert revert-коммита плюс исправление.",
     hints=["Какой способ отката не переписывает историю общей ветки?",
            "У merge-коммита два родителя — revert должен знать, какой из них основная линия.",
            "Инцидент — это ещё и коммуникация и проверка результата."],
     time=20)


# =====================================================================
# c-networks — Основы сетей: DNS, порты, TCP (junior)
# =====================================================================
topic("c-networks", """
Когда код ходит в базу или в чужой API, а браузер открывает сайт, под капотом работает сеть. Сетевым инженером быть не нужно, но без базы ошибки вроде connection refused и could not resolve host превращаются в магию.

IP-адрес и порт
• IP-адрес — адрес устройства в сети: IPv4 — четыре числа от 0 до 255 (203.0.113.10), IPv6 — длиннее (2001:db8::1).
• Частные диапазоны работают только внутри локальных сетей, из интернета туда напрямую не попасть: 10.0.0.0/8, 172.16.0.0/12 (это 172.16.x.x–172.31.x.x) и 192.168.0.0/16. Остальные адреса — публичные.
• 127.0.0.1 и имя localhost — петлевой интерфейс, «этот же компьютер»: запрос никуда не уходит.
• Порт — номер программы на устройстве (0–65535). 22 — SSH, 53 — DNS, 80 — HTTP, 443 — HTTPS, 5432 — PostgreSQL, 6379 — Redis; в разработке часто 3000, 5173, 8000, 8080. Один порт одновременно слушает только одна программа, иначе — Address already in use.
• Сервер слушает конкретный адрес и порт. 127.0.0.1:8000 доступен только с этой же машины, 0.0.0.0:8000 — на всех сетевых интерфейсах, в том числе из локальной сети. Внутри Docker-контейнера localhost — сам контейнер, а не твой ноутбук и не соседний контейнер.

DNS
DNS превращает имена в адреса. Записи: A — имя → IPv4, AAAA — имя → IPv6, CNAME — псевдоним другого имени, MX — почтовые серверы, TXT — произвольный текст (например, подтверждение домена). У каждой записи есть TTL — сколько секунд резолверы и браузеры могут держать ответ в кэше. Поменял запись — старый ответ у кого-то проживёт до конца TTL, поэтому перед переездом TTL заранее снижают до минут. Порядок поиска: файл /etc/hosts, кэш ОС и браузера, DNS-резолвер провайдера или компании. Проверить: nslookup имя или dig +short имя.

TCP и UDP
• TCP устанавливает соединение (рукопожатие SYN → SYN-ACK → ACK), гарантирует доставку и порядок, переотправляет потерянное. На нём HTTP/1.1 и HTTP/2, базы данных, SSH.
• UDP просто отправляет пакеты: без соединения и гарантий, зато быстрее. На нём DNS-запросы, звонки, игры и QUIC, поверх которого работает HTTP/3.

TLS и HTTPS
HTTPS — это HTTP внутри TLS. TLS шифрует трафик и доказывает, что сервер тот, за кого себя выдаёт: сервер показывает сертификат, подписанный центром сертификации (CA). Клиент проверяет, что сертификат не просрочен, подписан доверенным CA и выдан на это имя (имена перечислены в поле SAN; *.example.com покрывает один уровень поддоменов). Любое несовпадение — ошибка. Отключать проверку (curl -k, verify=False) в рабочем коде нельзя: это открывает дорогу атаке «человек посередине».

Что происходит, когда вводишь адрес в браузере
1) Браузер разбирает URL: схема https, хост, порт (по умолчанию 443), путь.
2) DNS: имя → IP-адрес.
3) TCP-соединение с IP:443.
4) TLS-рукопожатие: проверка сертификата и обмен ключами.
5) HTTP-запрос и ответ с HTML.
6) Браузер разбирает HTML, догружает CSS, JS и картинки (снова запросы) и рисует страницу.

По ошибке видно, на каком шаге сломалось:
• Could not resolve host, ENOTFOUND, NXDOMAIN — DNS;
• Connection refused — хост доступен, но на порту никто не слушает: сервис не запущен, другой порт или он слушает только 127.0.0.1;
• Connection timed out — пакеты теряются: файрвол, неверный IP, хост недоступен;
• ошибки сертификата — TLS;
• любой HTTP-код (404, 502) — сеть в порядке, сервер ответил, проблема выше.
""")

task("c-networks-01", "quiz", 2, "qa",
     "Ира: «Хочу проверить с телефона твою страницу записи на тренировки для «Жми». Телефон и ноутбук в одной офисной Wi-Fi. На ноутбуке по localhost:8000 всё открывается, а с телефона по http://192.168.1.23:8000 — «Не удаётся получить доступ к сайту». Файрвол я уже проверила».",
     "Почему с телефона страница не открывается?",
     code="""
$ ip -4 addr show wlan0 | grep inet
    inet 192.168.1.23/24 brd 192.168.1.255 scope global dynamic wlan0

$ uvicorn app.main:app --port 8000
INFO:     Started server process [48213]
INFO:     Uvicorn running on http://127.0.0.1:8000 (Press CTRL+C to quit)
""", lang="text",
     options=[
         "Сервер слушает только 127.0.0.1 — петлевой интерфейс самого ноутбука. Запустить с --host 0.0.0.0",
         "Порт 8000 — нестандартный, мобильные браузеры его блокируют; нужен порт 80 или 443",
         "192.168.1.23 — частный адрес, к нему нельзя подключиться даже из той же Wi-Fi-сети",
         "Телефоны не открывают сайты по http без сертификата — нужно настроить HTTPS для dev-сервера",
         "Телефон не знает имени ноутбука — на нём нужно прописать localhost в файле hosts",
     ],
     answer=0,
     explanation="В логе сервера видно адрес, который он слушает: 127.0.0.1:8000. Это петлевой интерфейс — запросы на него приходят только с этой же машины, поэтому по localhost всё работает, а подключение с телефона на 192.168.1.23:8000 некому принять. Флаг --host 0.0.0.0 заставит сервер слушать все интерфейсы, в том числе Wi-Fi. Частные адреса как раз и работают внутри локальной сети, порт 8000 ничем не хуже 80, а localhost на телефоне — это сам телефон. Помни, что 0.0.0.0 открывает dev-сервер всем в сети: в офисе нормально, в кафе — лучше не надо.",
     hints=["Посмотри, какой адрес пишет сервер в строке «running on».",
            "127.0.0.1 — это «этот же компьютер». Чей компьютер для телефона?"])

task("c-networks-02", "write_code", 2, "devops_colleague",
     "Дима: «В админке «Высоты» хотим подсвечивать, откуда пришёл запрос: из внутренней сети бизнес-центра или снаружи. Нужна функция, которая по IPv4-адресу скажет, что это за адрес. Без библиотек — заодно запомнишь диапазоны, они в конфигах на каждом шагу».",
     "Напиши ip_kind(ip): для строки с IPv4-адресом верни \"loopback\", если адрес из 127.0.0.0/8; \"private\", если он из частных диапазонов 10.0.0.0/8, 172.16.0.0/12 или 192.168.0.0/16; иначе \"public\". Адреса на входе всегда корректные.",
     code="""
def ip_kind(ip):
    # твой код
    pass
""", lang="python", entry="ip_kind",
     answer="""
def ip_kind(ip):
    parts = [int(p) for p in ip.split(".")]
    a, b = parts[0], parts[1]
    if a == 127:
        return "loopback"
    if a == 10:
        return "private"
    if a == 172 and 16 <= b <= 31:
        return "private"
    if a == 192 and b == 168:
        return "private"
    return "public"
""",
     tests=[
         {"input": ["127.0.0.1"], "expected": "loopback"},
         {"input": ["10.20.30.40"], "expected": "private"},
         {"input": ["172.16.0.5"], "expected": "private"},
         {"input": ["172.31.255.1"], "expected": "private"},
         {"input": ["172.32.0.1"], "expected": "public"},
         {"input": ["192.168.1.23"], "expected": "private"},
         {"input": ["192.169.0.1"], "expected": "public"},
         {"input": ["100.1.2.3"], "expected": "public"},
         {"input": ["8.8.8.8"], "expected": "public"},
     ],
     explanation="Число после косой черты — сколько первых бит адреса фиксировано. /8 — первый байт: всё, что начинается с 10. /16 — первые два байта: 192.168. /12 — первый байт и старшие 4 бита второго: 172 и второе число от 16 до 31 (в двоичном виде 0001xxxx). Поэтому адрес нужно разбить на числа и сравнивать их, а не строки: проверка startswith(\"10\") ошибочно примет 100.1.2.3, а startswith(\"172.1\") — 172.1.0.1 и 172.100.0.1, зато пропустит 172.20.0.1. В реальном коде на Python есть модуль ipaddress (ip_address(ip).is_private), но сами диапазоны стоит знать наизусть — по ним сразу видно, почему база 10.0.3.12 не открывается с твоего ноутбука.",
     hints=["Разбей адрес по точкам и преврати части в числа.",
            "172.16.0.0/12 — это 172.16.x.x … 172.31.x.x.",
            "Сравнивай первое число, а для 172 и 192 — ещё и второе."])

task("c-networks-03", "find_bug", 2, "client",
     "Нина, владелица пекарни «Батон»: «Мы запустили мобильное приложение, а оно пишет “Ошибка соединения”. Сайт в браузере при этом открывается! Разработчик приложения прислал вот это и говорит, что проблема на вашей стороне».",
     "На каком шаге ломается соединение и как это правильно исправить?",
     code="""
$ curl -v https://api.baton-bakery.example/menu
*   Trying 198.51.100.24:443...
* Connected to api.baton-bakery.example (198.51.100.24) port 443
* TLSv1.3 (OUT), TLS handshake, Client hello (1):
* TLSv1.3 (IN), TLS handshake, Server hello (2):
* TLSv1.3 (IN), TLS handshake, Certificate (11):
* Server certificate:
*  subject: CN=baton-bakery.example
*  start date: Sep  1 00:00:00 2026 GMT
*  expire date: Nov 30 23:59:59 2026 GMT
*  subjectAltName does not match api.baton-bakery.example
* SSL: no alternative certificate subject name matches target host name 'api.baton-bakery.example'
curl: (60) SSL: no alternative certificate subject name matches target host name 'api.baton-bakery.example'
""", lang="text",
     options=[
         "TLS: сертификат выпущен на baton-bakery.example, а имени api.baton-bakery.example в нём нет. Перевыпустить с ним в SAN",
         "DNS: имя api.baton-bakery.example не находится, поэтому приложение не может подключиться — добавить A-запись",
         "Сертификат просрочен или ещё не вступил в силу по часам телефона — продлить его и перевыпустить",
         "Попросить разработчика приложения отключить проверку сертификата для нашего домена — сайт ведь наш",
         "Порт 443 закрыт файрволом для мобильных сетей, а из офисной сети открыт — открыть его для всех",
     ],
     answer=0,
     explanation="Идём по шагам: имя разрешилось в 198.51.100.24 — DNS в порядке; «Connected … port 443» — TCP-соединение установлено, файрвол пропускает. Ломается TLS-рукопожатие: сертификат выдан на baton-bakery.example, а в запросе api.baton-bakery.example — такого имени в сертификате нет. Срок действия до 30 ноября 2026 года, так что он не просрочен. Сайт в браузере открывается, потому что живёт на имени, которое в сертификате есть. Отключение проверки сертификата в приложении сделало бы его уязвимым к подмене сервера — правильно перевыпустить сертификат со всеми нужными именами.",
     hints=["Пройди по строкам вывода: DNS прошёл? TCP-соединение установлено?",
            "Сравни имя в subject сертификата и имя, по которому идёт запрос."])

task("c-networks-04", "incident", 3, "client",
     "Артур, директор фитнес-клуба «Жми»: «Вчера вечером вы перевезли сайт записи на новый сервер. Сегодня у половины клиентов сайт не открывается, у остальных всё нормально. У меня дома открывается, а у администратора на ресепшене — нет! Через час запись на групповые, люди звонят».",
     "Что происходит и что сделать прямо сейчас, чтобы сайт открывался у всех?",
     code="""
# Вчера 21:40 — A-запись book.zhmi.example изменена: 203.0.113.10 -> 198.51.100.7
# Вчера 22:00 — на старом сервере 203.0.113.10 остановлен nginx

$ dig book.zhmi.example A +noall +answer @ns1.zhmi-dns.example   # авторитетный сервер
book.zhmi.example.      86400   IN      A       198.51.100.7

$ dig book.zhmi.example A +noall +answer                         # резолвер провайдера на ресепшене
book.zhmi.example.      49210   IN      A       203.0.113.10

$ curl -sv https://book.zhmi.example/ -o /dev/null
*   Trying 203.0.113.10:443...
* connect to 203.0.113.10 port 443 from 192.168.0.14 port 51322 failed: Connection refused
* Failed to connect to book.zhmi.example port 443 after 38 ms: Couldn't connect to server
""", lang="text",
     options=[
         "Старая A-запись закэширована у резолверов на сутки: поднять на старом сервере nginx с прокси на новый, пока кэши не истекут",
         "Сертификат на новом сервере выпущен не на book.zhmi.example — у части браузеров строгая проверка, поэтому перевыпустить его",
         "Новый сервер не выдерживает нагрузку и сбрасывает соединения — добавить ещё один и балансировщик",
         "Попросить клиентов очистить кэш браузера и DNS-кэш на своих устройствах — старый адрес запомнили они",
         "Изменить A-запись ещё раз или поставить TTL 60 секунд — резолверы увидят обновление и перечитают её",
     ],
     answer=0,
     explanation="Авторитетный DNS-сервер уже отдаёт новый адрес, а резолвер провайдера — старый, и в его ответе видно оставшееся время жизни: 49210 секунд, почти 14 часов. Запись закэширована до смены, и пока TTL не истечёт, резолвер не будет переспрашивать. Клиенты с таким резолвером идут на старый сервер, где nginx остановлен, — отсюда быстрый Connection refused. «У половины работает» — у них другие резолверы, которые спросили уже после смены. Повлиять на чужие кэши нельзя: повторная смена записи до них не дойдёт, очистка кэша браузера не тронет кэш провайдера. Поэтому прямо сейчас — вернуть обслуживание на старом адресе (прокси на новый сервер), а на будущее снижать TTL до 300 секунд за сутки до переезда и не выключать старый сервер, пока не истечёт старый TTL.",
     hints=["Сравни ответы двух dig: адрес и число во второй колонке.",
            "Вторая колонка в ответе резолвера — сколько секунд ещё жить кэшу.",
            "Чужие кэши сбросить нельзя. Что можно сделать со старым сервером?"],
     time=20)


# =====================================================================
# c-estimation — Оценка задач и уточняющие вопросы (junior)
# =====================================================================
topic("c-estimation", """
«Сколько займёт?» — вопрос, который тебе будут задавать каждую неделю. Оценка — не обещание и не угадайка, а прогноз с понятными допущениями. Хорошая оценка помогает команде планировать, плохая — срывает сроки и подрывает доверие.

Почему оценки промахиваются
• Оценивают только «написать код», забывая тесты, ревью и правки, миграции, деплой, документацию и общение.
• Неизвестные: чужой API без документации, старый код, который никто не помнит, размытые требования.
• Оптимизм: оценка «если всё пойдёт хорошо». Так почти никогда не бывает.

Сначала вопросы
Прежде чем называть срок, выясни то, что меняет объём работы в разы:
• Что считается готовым (критерии приёмки)? Кто пользователь и какую проблему решаем?
• Где и на каких платформах это должно работать: сайт, приложение, админка, уведомления?
• Граничные случаи: пусто, много данных, ошибки, повторное нажатие, отмена.
• Объём данных и нагрузка, права доступа, безопасность и персональные данные.
• Интеграции: есть ли у внешнего сервиса документация и тестовая среда, кто и когда выдаёт доступы.
• Что важнее: успеть к дате с урезанным объёмом или сделать всё.
Не трать вопросы на то, что не влияет на оценку, и на то, что можешь выяснить сам за пять минут.

Декомпозиция
Разбей задачу на куски не больше дня: кусок «на три дня» — это не оценка, а надежда. Режь вертикально — по работающим сценариям: сначала самый простой сценарий целиком (API + интерфейс + тест), потом следующий. Горизонтальная нарезка «неделя на базу, неделя на API, неделя на фронт» опасна: до самого конца ничего не работает и ошибки находятся поздно. Тесты, ревью, деплой и миграции — отдельными строчками плана.

Техники
• Диапазон вместо точки: «3–5 дней» честнее, чем «4».
• Трёхточечная оценка: оптимистичная O, реалистичная M, пессимистичная P; ожидание ≈ (O + 4M + P) / 6. Большой разрыв между O и P — сигнал, что много неизвестного.
• Spike — исследование с ограничением по времени: «день на то, чтобы разобраться с API службы доставки, потом дам оценку».
• Стори-пойнты — относительная сложность в условных единицах (обычно 1, 2, 3, 5, 8, 13), а не часы. Команда сравнивает задачу с эталонной и голосует на planning poker; большой разброс голосов — повод обсудить, кто что знает. Сколько пойнтов команда закрывает за спринт (velocity), показывает история. Сравнивать velocity разных команд бессмысленно.

Как сообщать
Называй оценку вместе с допущениями и рисками: «Фильтр отзывов — 2–3 дня, если фильтруем по уже существующим полям. Поиск по тексту — отдельная задача, оценю после спайка». На «а побыстрее?» отвечай вариантами объёма, а не выбрасыванием тестов. И главное правило: понял, что не успеваешь, — скажи сразу, с новой оценкой и вариантами. Плохая новость во вторник лучше сюрприза в пятницу.
""")

task("c-estimation-01", "estimation", 1, "client",
     "Нина, владелица пекарни «Батон»: «Хочу, чтобы покупатели могли оставить комментарий к заказу — ну, там, “без кунжута” или “позвоните за час”. Это ведь просто одно поле? Сколько по времени?»",
     "Выбери вопросы, которые стоит задать Нине до того, как назвать срок.",
     options=[
         "Где комментарий должен быть виден: только в админке или ещё в чеке, у курьера, в письме клиенту?",
         "Можно ли менять комментарий после оформления заказа — и до какого момента?",
         "Нужен ли комментарий и в новом мобильном приложении, или только на сайте?",
         "Каким шрифтом и цветом выводить поле комментария на странице оформления заказа?",
         "На каком языке программирования и фреймворке написан ваш сайт и кто его делал?",
         "Сколько примерно комментариев в день вы ожидаете и какой они будут длины?",
     ],
     answer=[0, 1, 2],
     explanation="«Просто поле» на сайте с выводом в админке — это колонка в базе, поле в форме, отображение и тесты: примерно день. Но каждое новое место, где комментарий должен появиться, добавляет работу: чек, уведомление курьеру, письмо, мобильное приложение (а у «Батона» оно теперь есть). Редактирование после оформления — отдельный сценарий со своими правилами. Шрифт решит дизайн, стек проекта ты узнаешь сам из репозитория, а количество коротких текстов в день на объём работы не влияет. Разумный ответ Нине: «Сайт и админка — 1 день; с уведомлением курьеру и приложением — 3–4 дня».",
     hints=["Какие ответы изменят объём работы в разы?",
            "Не задавай клиенту вопросы, ответ на которые знаешь сам или которые не влияют на срок."],
     time=10)

task("c-estimation-02", "architecture", 2, "manager",
     "Стас: «Хочу в следующий спринт подписку на снижение цены в Ламповом Маркете: нажал „Сообщить о скидке“ — получил письмо, когда цена упала. Прежде чем назовёшь срок, разбей на подзадачи. Какой план берём?»",
     "Какая декомпозиция лучше подходит и для оценки, и для работы?",
     options=[
         "По сценариям: подписка (API, кнопка, тесты) — 1 д; поиск снижения цены — 1 д; письма через очередь — 1 д; отписка — 0,5 д; ревью и стейдж — 1 д",
         "По слоям: весь бэкенд (таблицы, API, фоновая проверка цен, письма) — 5 дней; весь фронтенд — 4 дня; тестирование всего вместе в самом конце — 2 дня",
         "Одной задачей: «Сделать подписку на снижение цены» — 7 дней, детали уточним по ходу работы, всё равно заранее всего не предусмотреть",
         "По техническим шагам: спроектировать все таблицы, написать все модели, все эндпоинты, сверстать все экраны — по 2 дня на каждый пункт",
     ],
     answer=0,
     explanation="Хорошая декомпозиция режет задачу вертикально: каждый пункт — работающий кусок сценария с тестами, не больше дня. После первого пункта подписку уже можно потрогать руками, а ошибка в оценке видна на второй день, а не на девятый. Отдельно учтены ревью, фиче-флаг и проверка на стейдже — работа, которую обычно забывают. Горизонтальные планы (бэкенд, потом фронт; таблицы, потом модели) ничего не дают потрогать до конца, а тестирование в самом конце гарантирует поздние сюрпризы. Одна строка на семь дней — не оценка, а надежда. Итог плана — около 4,5 дня, с запасом на неизвестное честно назвать «5–6 дней».",
     hints=["В каком плане что-то работающее появится уже после первого дня?",
            "Проверь, где учтены тесты, ревью и выкладка."])

task("c-estimation-03", "quiz", 2, "manager",
     "Стас: «Ну что, в пятницу выкатываем онлайн-оплату записи для «Жми»? Я Артуру уже пообещал, он разослал клиентам анонс».",
     "Сейчас вторник. Ты оценил интеграцию оплаты в 3 дня, но выяснилось: у платёжного провайдера нет тестовой среды, а доступ к боевому кабинету выдают до пяти рабочих дней. Что делаешь?",
     options=[
         "Сразу пишу Стасу: пятница под угрозой из-за доступов; предлагаю варианты — сдвиг, запись без онлайн-оплаты, фиче-флаг",
         "Молчу и работаю по вечерам: доступы могут выдать и раньше, а поднимать панику из-за одного риска пока рано",
         "Предупреждаю в четверг вечером, когда станет точно понятно, что не успеваю, чтобы не дёргать Стаса и Артура раньше времени",
         "Пишу интеграцию «вслепую» по документации и выкатываю в пятницу без проверки — поправим уже на живых платежах",
         "Сам переношу релиз на следующую неделю и пишу об этом Артуру напрямую, чтобы он успел предупредить клиентов",
     ],
     answer=0,
     explanation="Появился риск, который ты не мог учесть в оценке, — значит, пора пересмотреть её и сообщить сразу, пока у Стаса и Артура есть время что-то поменять. Хорошее сообщение содержит факт, влияние на срок, варианты с компромиссами и срок следующего апдейта — тогда решение принимает тот, кто отвечает за продукт и общается с клиентом, а не ты молча или в обход него. Во вторник можно перестроить анонс или выпустить запись без онлайн-оплаты; в четверг вечером — уже нет. Работа по ночам не решит проблему доступов, а выкладка непроверенной оплаты на живых деньгах — риск куда дороже переноса.",
     hints=["Когда у менеджера больше всего вариантов что-то поправить?",
            "Хорошее сообщение о риске — это факт, влияние, варианты и следующий шаг."])

task("c-estimation-04", "estimation", 3, "manager",
     "Стас: «Хочу вход через Google в Ламповом Маркете — все так делают, это же одна кнопка. К пятнице успеешь? Маркетинг уже готовит рассылку».",
     "Выбери риски и вопросы, которые обязательно нужно прояснить, прежде чем соглашаться на срок.",
     options=[
         "Что делать, если аккаунт с тем же email уже есть и зарегистрирован по паролю: склеивать, спрашивать или запрещать?",
         "Кто создаст проект в Google Cloud Console и настроит redirect URI для стейджа и прода — и есть ли у нас туда доступ?",
         "Нужен ли вход через Google и в мобильном приложении, или только на сайте?",
         "Какие данные забираем из Google-профиля и как это согласуется с политикой обработки персональных данных?",
         "Какую иконку и какой текст поставить на кнопку входа и нужен ли для неё отдельный макет от дизайнера?",
         "Можно ли взять готовую библиотеку для OAuth, или протокол безопаснее написать с нуля самим?",
         "Нужно ли заодно переписать весь модуль авторизации на новый фреймворк, раз всё равно туда лезем?",
     ],
     answer=[0, 1, 2, 3],
     explanation="«Одна кнопка» прячет несколько больших кусков. Склейка с существующими аккаунтами — главный риск и по сложности, и по безопасности: склеивать можно только по подтверждённому email (email_verified), иначе возможен захват чужого аккаунта. Доступ к Google Cloud Console и настройка redirect URI для каждого окружения могут заблокировать работу на дни, если консоль ведёт другой отдел. Мобильное приложение — отдельная интеграция со своим SDK. Список забираемых данных — вопрос юристов и политики конфиденциальности. Иконку подскажут гайдлайны Google, библиотеку ты выберешь сам (писать OAuth с нуля не нужно), а переписывать авторизацию — не эта задача. Честный ответ Стасу: «Только сайт, со склейкой по подтверждённому email — 3–4 дня, если доступы к консоли будут сегодня; мобильное приложение — отдельно».",
     hints=["Что будет с покупателем, у которого уже есть аккаунт на тот же email?",
            "Какие вещи зависят не от тебя, а от других людей и доступов?"],
     time=15)


# =====================================================================
# c-code-review — Код-ревью: как давать и принимать (junior_plus)
# =====================================================================
topic("c-code-review", """
Код-ревью — проверка изменений коллегой до мерджа. Цель — не найти, к чему придраться, а чтобы в main попал правильный, понятный и безопасный код, а знание о нём было не только у автора.

Что смотреть — по убыванию важности
1) Решает ли PR задачу: соответствует тикету, нет ли лишнего.
2) Корректность: граничные случаи (пусто, ноль, None/null, очень много, повторный вызов), обработка ошибок, одновременные запросы.
3) Безопасность: SQL-запросы, собранные из строк (f-строки, шаблонные строки) — это инъекция; проверка прав (может ли этот пользователь трогать этот объект); секреты в коде; персональные данные в логах.
4) Тесты: есть ли, проверяют ли поведение и ошибки, а не только «счастливый путь».
5) Производительность там, где она важна: запросы в цикле, загрузка всего без пагинации, сетевые вызовы без таймаута.
6) Читаемость: понятные имена, размер функций, нет дублирования, комментарии объясняют «почему».
7) Стиль — форматирование, кавычки, порядок импортов — пусть проверяют линтер и форматтер в CI. Спорить о пробелах на ревью — пустая трата времени.

Как писать комментарии
• О коде, а не о человеке: «при пустом списке здесь будет деление на ноль», а не «ты опять не подумал».
• Объясни почему и предложи вариант: «запрос собран шаблонной строкой — это SQL-инъекция; передай телефон параметром $1».
• Отмечай важность. Многие команды используют метки: blocker: — без этого нельзя мерджить, question: — хочу понять, suggestion: — можно лучше, nit: — мелочь, на усмотрение автора.
• Не понял — спроси: «зачем здесь sleep(2)?». Возможно, ты чего-то не знаешь.
• Отмечай и хорошее — это тоже обратная связь.
• Не переписывай PR под свой вкус: если варианты одинаково хороши, выбор за автором.

Размер PR
Чем больше PR, тем хуже ревью: 50 строк прочитают внимательно, 2000 — пролистают и напишут LGTM. Ориентир — до 200–400 строк изменений и одна логическая задача. Рефакторинг, переименования и форматирование — отдельными PR. Большую фичу режут на несколько PR, а недоделанное прячут за фиче-флагом.

Как принимать ревью
• Перед запросом ревью сделай self-review: прочитай свой diff целиком, как чужой, убери отладочный код, заполни описание PR (что, зачем, как проверить, скриншоты для интерфейса).
• Замечание — про код, а не про тебя. Не оправдывайся и не спорь на эмоциях.
• Согласен — исправь и ответь коротко: «поправил в a1b2c3d». Не согласен — объясни аргументами и данными, предложи компромисс или созвон, если переписка затянулась. Не понял — переспроси.
• Тред обычно закрывает тот, кто его открыл: не нажимай Resolve молча, если не исправил.
• Нашёл проблему вне рамок задачи — заведи отдельный тикет, а не раздувай PR.

Ревьюить быстро — тоже часть работы: PR, который висит три дня, тормозит всю команду. Во многих командах норма — первый ответ на ревью в течение рабочего дня.
""")

task("c-code-review-01", "quiz", 2, "teamlead",
     "Марина, сеньор, оставила в твоём PR комментарий: «Зачем тут кэш списка товаров на 10 минут? Цены меняются, покупатель увидит старую. Убери». Кэш ты добавил не просто так: без него каталог на стейдже грузился 3 секунды.",
     "Как лучше ответить на это замечание?",
     options=[
         "Ответить с цифрами: без кэша каталог грузится 3 с (замер в PR); предложить TTL 60 с и сброс кэша при смене цены",
         "Молча убрать кэш: сеньор видит больше рисков, а скорость каталога можно будет поправить потом отдельной задачей в бэклоге",
         "Ответить: «У меня всё работает, кэш нужен для скорости, это вкусовщина» — и сразу нажать Resolve",
         "Написать Гене в личку, что Марина придирается, и попросить его апрувнуть PR без её замечания",
         "Не отвечать на тред и перекинуть ревью на другого сеньора, который к кэшам относится спокойнее",
     ],
     answer=0,
     explanation="В замечании Марины есть настоящий риск — устаревшие цены, а у твоего решения есть настоящая причина — медленный каталог. Хороший ответ показывает данные (замер), признаёт её риск и предлагает компромисс, который закрывает обе проблемы: короткий TTL и сброс кэша при изменении цены. Решение остаётся открытым для обсуждения, а тред закроет тот, кто его открыл. Молча убрать кэш — потерять производительность и знание, почему он был нужен. «Вкусовщина» с Resolve отмахивается от реального бага, а обход ревьюера через личку или другого человека подрывает доверие в команде.",
     hints=["Кто из вас прав? Может быть, оба?",
            "Факты, признание риска и предложение, которое решает обе проблемы."])

task("c-code-review-02", "code_review", 2, "teamlead",
     "Гена: «Стажёр написал эндпоинт поиска заказов по телефону для админки «Батона». Сделай первое ревью сам, я посмотрю за тобой. Отметь только то, что реально нужно исправить перед мерджем».",
     "Выбери замечания, которые действительно нужно оставить в ревью.",
     code="""
app.get("/api/admin/orders", async (req, res) => {
  const phone = req.query.phone;
  try {
    const result = await db.query(
      `SELECT * FROM orders WHERE customer_phone = '${phone}' ORDER BY created_at DESC`
    );
    res.json(result.rows);
  } catch (e) {
    console.log(e);
  }
});
""", lang="javascript",
     options=[
         "Телефон подставляется в SQL шаблонной строкой — SQL-инъекция. Нужен запрос с параметром: customer_phone = $1 и [phone]",
         "В catch ошибка только пишется в консоль, ответ не отправляется — запрос повиснет. Вернуть 500 или next(e)",
         "Не видно проверки, что запрос делает администратор: телефоны и адреса клиентов получит кто угодно",
         "Стрелочные функции в обработчиках Express работают медленнее обычных — перепиши на function",
         "Переименуй result в data и phone в customerPhone — так принято в нашем код-стайле, без этого не мерджим",
         "Шаблонные строки не поддерживаются в старых версиях Node.js — замени на конкатенацию строк",
     ],
     answer=[0, 1, 2],
     explanation="Главная проблема — SQL-инъекция: значение из query-параметра попадает прямо в текст запроса, и строка вроде ' OR '1'='1 вернёт все заказы, а то и удалит таблицу. Параметризованный запрос передаёт значение отдельно от SQL, и база никогда не выполнит его как код. Пустой catch — второй блокер: клиент не получит ответа, а ошибку никто не увидит в мониторинге. Третий — доступ: админский эндпоинт с персональными данными должен явно требовать роль администратора. Стрелочные функции не медленнее, шаблонные строки в Node.js поддерживаются давно (а конкатенация не спасла бы от инъекции), имя переменной — дело вкуса.",
     hints=["Что будет, если в phone передать строку с одинарной кавычкой?",
            "Что получит клиент, если база упадёт?",
            "Кто может вызвать этот URL?"])

task("c-code-review-03", "code_review", 3, "teamlead",
     "Марина, сеньор: «PR в КофеБот: ежедневный отчёт офис-менеджеру — сколько и чего заказали и какой средний чек. Автор просит апрув до вечера. Посмотри внимательно, у меня тут минимум два блокера».",
     "Выбери замечания, без исправления которых PR нельзя мерджить.",
     code="""
import requests

BOT_TOKEN = "7312894410:AAHk3v9QeX2mZrT8wLp0dYs1uN4bG"


def send_daily_report(orders, chat_id):
    counts = {}
    total = 0
    for order in orders:
        counts[order["drink"]] = counts.get(order["drink"], 0) + 1
        total += order["price"]

    lines = [f"{drink}: {n}" for drink, n in counts.items()]
    lines.append(f"Итого: {total} ₽")
    lines.append(f"Средний чек: {total / len(counts):.0f} ₽")

    try:
        requests.post(
            f"https://api.telegram.org/bot{BOT_TOKEN}/sendMessage",
            json={"chat_id": chat_id, "text": "\\n".join(lines)},
        )
    except Exception:
        pass
""", lang="python",
     options=[
         "Токен бота прямо в коде: он попадёт в историю git. Брать его из переменной окружения, а этот токен перевыпустить",
         "Средний чек делится на число разных напитков (len(counts)), а не заказов; а в день без заказов — ZeroDivisionError",
         "except Exception: pass и requests.post без timeout: при сбое Telegram отчёт молча не придёт или функция зависнет",
         "f-строки лучше заменить на .format(): так шаблон отчёта можно будет вынести в конфиг и переводить",
         "Без Counter из collections подсчёт напитков будет неверным при повторяющихся названиях — переписать",
         "Функцию нужно сделать асинхронной, иначе бот будет тормозить на время отправки отчёта всем пользователям",
         "Переменную n нужно назвать count, а lines — report_lines: с такими именами PR нельзя мерджить",
     ],
     answer=[0, 1, 2],
     explanation="Токен в коде — утечка секрета: он останется в истории git навсегда, даже если потом строку удалить, поэтому его нужно не только вынести в переменную окружения, но и перевыпустить. Средний чек считается неправильно: делить нужно на len(orders), а не на количество видов напитков, и отдельно обработать день без заказов, иначе в выходные функция упадёт. Пустой except вместе с запросом без timeout превращает любой сбой в тишину: офис-менеджер просто не получит отчёт, и никто не узнает почему. Counter сделал бы код короче, но и словарь работает; async для отчёта раз в день не нужен; f-строки — нормальный современный стиль. Замечание про имя n уместно, но с меткой nit — оно не блокирует мердж.",
     hints=["Проверь на примере: 3 заказа латте по 220 ₽ — какой получится средний чек?",
            "Что увидит пользователь и разработчик, если Telegram ответит ошибкой?",
            "Что станет с токеном, когда этот PR смержат?"])

task("c-code-review-04", "architecture", 3, "teamlead",
     "Гена: «Твой PR «Фильтр отзывов» разросся: сама фича — 350 строк, а по дороге ты переименовал модуль reviews в feedback по всему проекту (1900 строк) и прогнал форматтер по старым файлам. Марина открыла PR и закрыла вкладку. Что будем делать?»",
     "Как правильно довести эти изменения до main?",
     options=[
         "Разбить на PR: форматирование, переименование и фича поверх — механику проверят быстро, 350 строк фичи внимательно",
         "Оставить один PR, но попросить Марину и Гену выделить на ревью целый день и пройтись по нему вместе",
         "Сделать squash всех коммитов в один — ревьюить один коммит проще, чем историю из двадцати мелких",
         "Смержить без ревью, раз переименование и форматтер логику не меняют, а баги фичи починить потом",
         "Выбросить переименование и форматирование, не обсуждая с командой, — фиче они не нужны, а ревью только мешают и тормозят",
     ],
     answer=0,
     explanation="Ревьюер не может внимательно прочитать 2000+ строк, а когда механические изменения перемешаны с логикой, настоящие ошибки фичи тонут в шуме переименований. Отдельные PR решают это: форматирование и переименование проверяются по диагонали (логика не меняется, CI зелёный), а 350 строк фичи получают нормальное ревью. Такие PR легче откатывать и реже конфликтуют. Squash не уменьшает diff, мердж без ревью нарушает процесс, а выбрасывать полезный рефакторинг молча тоже не стоит — лучше обсудить. Про форматтер: договоритесь о нём с командой, включите проверку в CI и сделайте один отдельный PR на переформатирование — его коммит можно добавить в .git-blame-ignore-revs, чтобы git blame показывал настоящих авторов строк.",
     hints=["Сколько строк реально нужно читать внимательно, а сколько — механические изменения?",
            "Что проще откатить и проверить: один огромный PR или несколько маленьких?"])

task("c-code-review-05", "code_review", 4, "teamlead",
     "Марина, сеньор: «Последний PR на сегодня — проверка пересечения броней переговорок для «Высоты». Вера жаловалась, что двое сотрудников забронировали «Эверест» на одно и то же время. Тесты зелёные, но я им не очень верю».",
     "Выбери все замечания, которые действительно нужно исправить перед мерджем.",
     code="""
// Бронь: { room: "Эверест", start: "2026-10-02T10:00", end: "2026-10-02T11:30" }
// Время всегда в одном формате и в часовом поясе бизнес-центра.

function overlaps(a, b) {
  return a.start > b.start && a.start < b.end;
}

async function createBooking(db, booking) {
  const existing = await db.bookings.findAll({ room: booking.room });
  for (const b of existing) {
    if (overlaps(booking, b)) {
      throw new Error("Переговорка занята");
    }
  }
  return db.bookings.insert(booking);
}

test("нельзя забронировать занятую переговорку", async () => {
  const db = fakeDb([{ room: "Эверест", start: "2026-10-02T10:00", end: "2026-10-02T11:30" }]);
  await expect(
    createBooking(db, { room: "Эверест", start: "2026-10-02T10:30", end: "2026-10-02T11:00" })
  ).rejects.toThrow("Переговорка занята");
});
""", lang="javascript",
     options=[
         "overlaps пропускает бронь 09:30–10:30 и бронь с тем же началом 10:00. Верное условие: a.start < b.end && b.start < a.end",
         "Проверка и вставка не атомарны: два запроса одновременно не увидят пересечения. Нужно ограничение в базе или блокировка",
         "Тест проверяет только бронь внутри существующей — нужны пересечение слева, то же начало и соседняя 11:30–12:00",
         "Даты-строки нельзя сравнивать через > и <: сравнение пойдёт посимвольно и сломается — только через new Date()",
         "for...of с await внутри работает медленно — замени на forEach, чтобы проверки шли параллельно",
         "Текст ошибки нужно писать на английском: русские строки в исключениях ломают логирование в Node.js",
     ],
     answer=[0, 1, 2],
     explanation="Два интервала пересекаются, когда каждый начинается раньше, чем кончается другой: a.start < b.end && b.start < a.end. Текущее условие ловит только брони, начавшиеся строго внутри существующей, поэтому пропускает пересечение слева и одинаковое начало — отсюда и двойные брони. Вторая причина жалобы Веры — гонка: между findAll и insert проходит время, и два параллельных запроса оба видят свободную переговорку; надёжно это решается только в базе. Тест зелёный, потому что проверяет единственный удобный случай, — граничные случаи и есть смысл ревью тестов. Строки ISO в одном формате и часовом поясе сравниваются корректно, а forEach с async-кодом, наоборот, создал бы новые баги.",
     hints=["Нарисуй на бумаге две брони: 10:00–11:30 и 09:30–10:30. Что вернёт overlaps?",
            "Что будет, если два сотрудника нажмут «Забронировать» в одну и ту же секунду?",
            "Какие случаи тест не покрывает?"])


# === END TOPICS ===

def build():
    shuffle_options(TASKS)
    data = {"track": "common", "part": "common", "topics": TOPICS, "tasks": TASKS}
    for tp in TOPICS:
        n = len(tp["theory"])
        if not 1200 <= n <= 3500:
            print("WARN theory length", tp["topic_id"], n)
    os.makedirs("Tools/Content/out", exist_ok=True)
    with open("Tools/Content/out/common.json", "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("topics:", len(TOPICS), "tasks:", len(TASKS))


if __name__ == "__main__":
    build()
