#!/usr/bin/env python3
"""Генератор контента: track devops, part d1 (junior)."""
import json
import os
import random

OUT = "Tools/Content/out/devops_d1.json"

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}

TOPICS = []
TASKS = []


def topic(topic_id, theory):
    TOPICS.append({"topic_id": topic_id, "theory": theory.strip("\n")})


def shuffle_options(task_id, options, answer):
    """Детерминированно перемешивает варианты, чтобы верный ответ не стоял всегда на одном месте."""
    perm = list(range(len(options)))
    random.Random("d1:" + task_id).shuffle(perm)
    options = [options[i] for i in perm]
    if isinstance(answer, list):
        answer = sorted(perm.index(i) for i in answer)
    else:
        answer = perm.index(answer)
    return options, answer


def task(n, topic_id, typ, diff, char, story, question, explanation, hints, *,
         code=None, lang=None, options=None, answer=None, tests=None, entry=None, time=None):
    if options is not None:
        options, answer = shuffle_options("%s-%02d" % (topic_id, n), options, answer)
    content = {
        "question": question.strip(),
        "code": code,
        "language": lang,
    }
    if entry:
        content["entry"] = entry
    content["options"] = options
    content["correct_answer"] = answer
    content["test_cases"] = tests
    TASKS.append({
        "task_id": "%s-%02d" % (topic_id, n),
        "topic_id": topic_id,
        "grade": "junior",
        "type": typ,
        "difficulty": diff,
        "xp_reward": XP_BASE[diff],
        "time_limit_minutes": time,
        "character": char,
        "story": story.strip(),
        "content": content,
        "explanation": explanation.strip(),
        "hints": hints,
    })


def rx(desc, pattern, flags="im"):
    return {"input": desc, "expected": {"regex": pattern, "flags": flags}}


def nrx(desc, pattern, flags="im"):
    return {"input": desc, "expected": {"not_regex": pattern, "flags": flags}}


# =====================================================================
# do-linux-processes
# =====================================================================
T = "do-linux-processes"
topic(T, """
Процесс — это запущенная программа. У него есть PID, родитель (PPID), владелец, состояние и расход CPU и памяти. Всё, что крутится на сервере (nginx, Postgres, твой воркер), — процессы. Первое, что делает дежурный, — выясняет, кто из них что делает.

Смотрим на процессы:
    ps aux --sort=-%mem | head       # топ по памяти
    ps -ef --forest                  # дерево: кто кого запустил
    pgrep -af gunicorn               # PID и командная строка по имени
    top                              # живая картина: M — сортировка по памяти, P — по CPU

Как читать вывод:
• %CPU больше 100 — нормально для многопоточного процесса: 390% — это почти четыре ядра.
• RSS (RES в top) — физическая память, которую процесс реально занимает. VSZ (VIRT) — виртуальная, она почти всегда огромная, по ней не суди.
• load average — средняя очередь на CPU (в Linux ещё и ожидание диска) за 1, 5 и 15 минут. Сравнивай с числом ядер (nproc): load 8 на 4 ядрах — перегруз.
• STAT: R — работает, S — спит, D — ждёт диск (много D — проблема с IO), Z — зомби (процесс завершился, а родитель не забрал его код выхода).

Память смотри через free -h, и главная колонка там — available, а не free. Linux занимает свободную память под кэш файлов (buff/cache) и отдаёт её по первому требованию, поэтому «free 200M» при «available 5G» — это нормально. Плохо, когда available близко к нулю и растёт swap.

Сигналы — способ сказать процессу «сделай что-то»:
• SIGTERM (15) — «пожалуйста, завершись». Его kill шлёт по умолчанию. Процесс может перехватить сигнал, дописать запросы, закрыть соединения — это graceful shutdown.
• SIGKILL (9) — ядро убивает процесс сразу, перехватить нельзя. Незаписанные данные, временные файлы, lock-файлы — всё остаётся как было. Это крайняя мера: сначала TERM, подождать, и только потом -9.
• SIGINT (2) — то же, что Ctrl+C. SIGHUP (1) — «терминал закрылся», по умолчанию завершает процесс; демоны вроде nginx по HUP перечитывают конфиг.
    kill 4312                  # SIGTERM
    kill -9 4312               # SIGKILL, если TERM не помог
    pkill -f 'celery worker'   # по командной строке: осторожно, заденет всё похожее

Код выхода 128 + N означает «убит сигналом N»: 137 = 128 + 9 (SIGKILL, часто от OOM killer), 143 = 128 + 15 (SIGTERM).

Фоновые процессы. «команда &» запускает процесс в фоне текущей оболочки, jobs, fg и bg им управляют. Но когда SSH-сессия обрывается, оболочка получает SIGHUP и рассылает его своим заданиям — они умирают. nohup, setsid и tmux от этого спасают, а для настоящего сервиса правильный путь — юнит systemd (или разовый systemd-run): он перезапустит процесс и соберёт логи.

OOM killer. Когда память и swap кончились совсем, ядро выбирает процесс с самым большим oom_score (обычно самый «толстый») и убивает его через SIGKILL. Следы — в журнале ядра:
    journalctl -k | grep -iE 'oom|killed process'
    dmesg -T | grep -i oom
Важно: убитый процесс не обязательно виноват. Память съел отчёт, а убили Postgres, потому что он больше. Ищи, кто рос, и ограничивай память виновнику (MemoryMax в systemd, limits у контейнера), а не просто перезапускай жертву.

Частые ошибки: сразу kill -9; pkill python, который убивает все Python-процессы на сервере; смотреть на VSZ и free вместо RSS и available.
""")

task(1, T, "quiz", 1, "teamlead",
     "Гена: «Воркер КофеБота завис: заказы висят, в логах тишина. Вижу, ты уже набираешь kill -9 — стоп. Сначала скажи, чем kill отличается от kill -9».",
     "Какое утверждение о kill и kill -9 верное?",
     """
kill без флага шлёт SIGTERM (15) — вежливую просьбу завершиться. Процесс может перехватить этот сигнал: дописать текущие запросы, закрыть соединения с базой, удалить lock-файл. kill -9 шлёт SIGKILL, который перехватить нельзя: ядро снимает процесс сразу, и всё недоделанное остаётся как есть. Поэтому порядок такой: сначала kill PID, подождать несколько секунд, проверить через ps, и только если процесс не ушёл — kill -9. Именно так работают docker stop и systemctl stop: сначала TERM, после таймаута — KILL.
""",
     ["Процесс может перехватить один из этих сигналов, а другой — нет.",
      "Какой номер сигнала у kill по умолчанию: 9 или 15?"],
     options=[
         "kill без флага шлёт SIGTERM, который процесс может перехватить и завершиться сам; SIGKILL (-9) перехватить нельзя",
         "kill без флага шлёт SIGKILL, а -9 — мягкое завершение: процессу дают 9 секунд, чтобы сохранить данные",
         "Оба шлют SIGTERM, просто kill -9 повторяет сигнал девять раз подряд, пока процесс не выйдет",
         "SIGKILL процесс может перехватить и дописать данные, а SIGTERM ядро выполняет сразу, в обход обработчиков",
     ],
     answer=0)

task(2, T, "find_bug", 2, "devops_colleague",
     "Дима: «Кто-то запускает импорт каталога Маркета руками на воркере. Каждый раз, когда он закрывает ноутбук, импорт умирает на середине. Посмотри, как запускали, и скажи, как надо».",
     "Почему импорт умирает при закрытии ноутбука и как правильно запускать долгую задачу на сервере?",
     """
Фоновое задание, запущенное через &, остаётся ребёнком интерактивной оболочки. Когда SSH-соединение обрывается, оболочка получает SIGHUP («терминал пропал») и рассылает его своим заданиям, а действие по умолчанию для SIGHUP — завершить процесс. Перенаправление вывода в файл тут ни при чём, OOM не видно ни в логах, ни в симптомах. Правильно — отвязать задачу от сессии: для разовой задачи sudo systemd-run --unit=catalog-import /srv/market/import_catalog.sh --full (логи потом в journalctl -u catalog-import), для ручной работы — tmux, в крайнем случае nohup или setsid. Keepalive в sshd не поможет: закрытый ноутбук всё равно порвёт соединение.
""",
     ["Какой сигнал получает оболочка, когда пропадает терминал?",
      "Что делает с процессом SIGHUP, если процесс его не обрабатывает?",
      "Задачу нужно отвязать от SSH-сессии: systemd-run, tmux или nohup."],
     code="""deploy@market-worker-1:/srv/market$ ./import_catalog.sh --full > import.log 2>&1 &
[1] 48213
# ...ноутбук закрыли, SSH-соединение оборвалось. Через 20 минут:

deploy@market-worker-1:~$ pgrep -af import_catalog
deploy@market-worker-1:~$ tail -n 2 /srv/market/import.log
[12:41:07] imported 18000/52000 products
[12:41:12] imported 18500/52000 products
deploy@market-worker-1:~$ journalctl -k --since "12:40" | grep -i oom
deploy@market-worker-1:~$
""",
     lang="text",
     options=[
         "При обрыве SSH оболочка получает SIGHUP и рассылает его своим заданиям. Отвязать импорт от сессии: systemd-run, tmux или nohup",
         "Импорт убивает OOM killer: он первым выбирает долгие фоновые процессы. Добавить swap и запускать импорт через nice -n 19",
         "Знак & даёт процессу низкий приоритет, и ядро приостанавливает его, пока сессия простаивает. Запускать импорт без &",
         "Перенаправление > import.log 2>&1 привязывает вывод к терминалу, и без него запись падает. Писать лог через tee в /tmp",
         "Срабатывает таймаут простоя SSH, и sshd убивает все процессы пользователя. Поднять ClientAliveInterval в sshd_config",
     ],
     answer=0)

task(3, T, "write_code", 2, "devops_colleague",
     "Дима: «В ночной отчёт по серверам хочу добавить топ процессов по памяти. Вывод ps я уже собираю, а вот разбор напиши ты. Только учти, что gunicorn — это восемь воркеров, их надо считать вместе».",
     """
Напиши функцию top_memory(lines, n). lines — строки вывода команды `ps -eo pid,user,rss,comm --no-headers`: PID, пользователь, RSS в килобайтах и имя команды (имя может содержать пробел, например «Web Content»). Сложи RSS по одинаковым именам команд и верни первые n пар [имя, мегабайты], где мегабайты — целое число kb // 1024. Сортировка по суммарному RSS по убыванию, при равенстве — по имени по алфавиту. Пустые строки пропускай.
""",
     """
Каждую строку делим по пробелам через split() — он сам справляется с выравниванием в выводе ps. RSS — третья колонка, имя команды — всё, что после неё: " ".join(parts[3:]) сохраняет имена с пробелами. Суммируем по имени в словаре, иначе восемь воркеров gunicorn по 100 МБ затеряются, хотя вместе они съедают почти гигабайт. Сортируем по ключу (-сумма, имя): минус даёт убывание по памяти, имя — стабильный порядок при равенстве. В мегабайты переводим уже после суммирования, чтобы не накопить ошибку округления. RSS, а не VSZ — потому что это реально занятая физическая память.
""",
     ["Сначала накопи словарь {имя: сумма_kb}, потом сортируй.",
      "sorted(totals.items(), key=lambda kv: (-kv[1], kv[0])) сортирует по убыванию суммы, при равенстве — по имени.",
      "Имя команды: \" \".join(parts[3:]), RSS: int(parts[2])."],
     code="""def top_memory(lines, n):
    # lines — строки `ps -eo pid,user,rss,comm --no-headers`
    # верни [[команда, мегабайты], ...]
    pass
""",
     lang="python",
     entry="top_memory",
     answer="""def top_memory(lines, n):
    totals = {}
    for line in lines:
        parts = line.split()
        if len(parts) < 4:
            continue
        comm = " ".join(parts[3:])
        totals[comm] = totals.get(comm, 0) + int(parts[2])
    items = sorted(totals.items(), key=lambda kv: (-kv[1], kv[0]))
    return [[comm, kb // 1024] for comm, kb in items[:n]]
""",
     tests=[
         {"input": [["  812 postgres  524288 postgres",
                     " 1201 www-data  102400 gunicorn",
                     " 1202 www-data   98304 gunicorn",
                     " 1203 www-data  100352 gunicorn",
                     "  640 root        8192 sshd",
                     "  901 redis      65536 redis-server"], 3],
          "expected": [["postgres", 512], ["gunicorn", 294], ["redis-server", 64]]},
         {"input": [["10 app 2048 nginx", "11 app 2048 cron", "12 app 1000 bash"], 5],
          "expected": [["cron", 2], ["nginx", 2], ["bash", 0]]},
         {"input": [["", "   ", "  1 root 4096 systemd"], 1],
          "expected": [["systemd", 4]]},
         {"input": [["77 ira 307200 Web Content", "78 ira 204800 Web Content", "90 ira 409600 firefox"], 2],
          "expected": [["Web Content", 500], ["firefox", 400]]},
     ])

task(4, T, "incident", 3, "devops_colleague",
     "Дима: «В 02:14 упал Postgres Лампового Маркета. Он сам поднялся, но минут десять сайт отдавал 500. Я собрал всё, что нашёл. Скажи, что случилось и что делаем прямо сейчас».",
     "Что произошло и что нужно сделать? Выбери все верные пункты.",
     """
Строка «Out of memory: Killed process 1187 (postgres)» — работа OOM killer: память кончилась, и ядро убило самый крупный процесс, а это Postgres (anon-rss + shmem-rss почти 5 ГБ). Но снимки top показывают, кто на самом деле рос: report-gen прибавлял по 0,7–0,8 ГБ каждые 5 минут, а Postgres всё время держался на 4,7 ГБ. Он жертва, а не виновник. Отчёт всё ещё работает, available уже 640 МБ, так что следующее падение базы близко: отчёт надо остановить (kill PID, а если не уйдёт — kill -9) и ограничить ему память через MemoryMax в юните или унести на отдельную машину, а потом разбираться, почему он грузит всё в память. oom_score_adj=-1000 для Postgres полезен как дополнительная мера, но сам по себе просто переведёт удар на другой процесс. Ночной reboot ничего не лечит.
""",
     ["Кого убили и кто при этом рос — это один процесс?",
      "Сравни RES report-gen и postgres на снимках в 02:00, 02:05 и 02:10.",
      "Посмотри на available в выводе free прямо сейчас."],
     code="""$ journalctl -k -S "02:00" -U "02:30" | grep -iE 'oom-killer|killed process'
Sep 24 02:14:07 db-1 kernel: report-gen invoked oom-killer: gfp_mask=0x140cca(GFP_HIGHUSER_MOVABLE|__GFP_COMP), order=0, oom_score_adj=0
Sep 24 02:14:07 db-1 kernel: Out of memory: Killed process 1187 (postgres) total-vm:6951220kB, anon-rss:2811904kB, file-rss:0kB, shmem-rss:2097152kB, UID:113 pgtables:10244kB oom_score_adj:0

$ tail -n 4 /var/log/postgresql/postgresql-16-main.log
2026-09-24 02:14:07.311 MSK [1052] LOG:  server process (PID 1187) was terminated by signal 9: Killed
2026-09-24 02:14:07.311 MSK [1052] LOG:  terminating any other active server processes
2026-09-24 02:14:08.020 MSK [1052] LOG:  all server processes terminated; reinitializing
2026-09-24 02:14:08.160 MSK [4410] LOG:  database system was interrupted; last known up at 2026-09-24 02:09:41 MSK

# снимки top, которые cron пишет раз в 5 минут
02:00  PID 2231  report    RES 0.9g  %CPU 97.5  report-gen
02:00  PID 1187  postgres  RES 4.7g  %CPU 10.2  postgres
02:05  PID 2231  report    RES 1.6g  %CPU 98.1  report-gen
02:05  PID 1187  postgres  RES 4.7g  %CPU 11.0  postgres
02:10  PID 2231  report    RES 2.4g  %CPU 99.4  report-gen
02:10  PID 1187  postgres  RES 4.7g  %CPU 12.3  postgres

$ free -h        # сейчас, 02:31, report-gen всё ещё работает
               total        used        free      shared  buff/cache   available
Mem:           7.8Gi       6.9Gi       112Mi       2.0Gi       780Mi       640Mi
Swap:             0B          0B          0B
""",
     lang="text",
     options=[
         "OOM killer убил Postgres, но виновник — report-gen: его память росла, пока у сервера не кончилась вся память",
         "Postgres завис на тяжёлом запросе, и его перезапустил watchdog — проблема в базе, нужно обновить Postgres",
         "Сейчас остановить report-gen (kill PID, если не выйдет — kill -9), пока он снова не уронил базу",
         "Ограничить отчёту память (MemoryMax в юните или отдельная машина) и выяснить, почему он всё держит в памяти",
         "Поставить Postgres oom_score_adj=-1000, чтобы OOM killer его не трогал, — этого достаточно, отчёт пусть работает",
         "Настроить ежедневную перезагрузку сервера в 01:55, перед запуском отчёта, чтобы память каждый раз была чистой",
     ],
     answer=[0, 2, 3],
     time=15)

task(5, T, "estimation", 3, "manager",
     "Стас: «Бухгалтерия всё равно хочет свой ночной отчёт report-gen, тот самый, из-за которого упала база. Дима говорит, надо “починить по-нормальному”. Сколько дней тебе нужно? Мне надо спланировать спринт».",
     "Прежде чем назвать срок, выбери вопросы, которые действительно влияют на оценку.",
     """
Оценка зависит от того, что именно придётся менять. Объём данных и его рост показывают, хватит ли ограничить память или отчёт надо переписывать на чтение порциями (серверный курсор, батчи). Вопрос о машине и лимите памяти даёт быстрое смягчение: MemoryMax в юните или запуск на реплике — это полдня. Срок готовности определяет, можно ли обменять скорость на память. Владелец кода решает, можем ли мы вообще его менять. Шрифты и ОС на ноутбуках бухгалтерии на работу не влияют. Разумный ответ Стасу: «Сегодня за полдня ограничим память и перенесём запуск на реплику, чтобы база больше не падала. Переписать отчёт на потоковую обработку — 2–3 дня плюс проверка на копии боевых данных».
""",
     ["Какие ответы меняют объём работы в разы?",
      "Есть быстрое смягчение и есть настоящее исправление — для оценки нужно понять оба."],
     options=[
         "Сколько данных обрабатывает отчёт и растёт ли объём? Можно ли читать их порциями?",
         "Обязательно ли запускать отчёт на сервере базы? Есть ли реплика, можно ли ограничить ему память?",
         "К какому часу отчёт должен быть готов — можно ли ему работать дольше, но с меньшей памятью?",
         "Кто поддерживает report-gen — можем ли мы менять его код, или это чужой бинарник?",
         "Каким шрифтом и в каком формате бухгалтерия печатает отчёт — PDF или Excel с логотипом?",
         "Какая операционная система стоит на ноутбуках бухгалтерии, с которых открывают отчёт?",
     ],
     answer=[0, 1, 2, 3],
     time=10)


# =====================================================================
# do-linux-perms
# =====================================================================
T = "do-linux-perms"
topic(T, """
У каждого файла в Linux есть владелец и группа, а права задаются для трёх категорий: владелец (u), группа (g) и остальные (o). Процесс всегда работает от имени какого-то пользователя, и ядро проверяет права при каждом открытии файла, каталога или сокета.

    $ ls -l /srv/market
    -rw-r----- 1 market www-data  812 Sep 20 10:02 .env
    drwxr-x--- 5 market market   4096 Sep 20 10:02 app
    srw-rw---- 1 market www-data    0 Sep 24 09:00 gunicorn.sock

Первый символ — тип: «-» — файл, d — каталог, l — ссылка, s — сокет. Дальше три тройки rwx: для владельца, группы и остальных. Потом идут владелец и группа.

Права в цифрах: r = 4, w = 2, x = 1, числа в каждой тройке складываются.
• 644 (rw-r--r--) — обычный файл: пишет владелец, читают все.
• 640 — конфиг: читает ещё и группа, остальным ничего.
• 600 — секреты и приватные ключи: только владелец.
• 755 / 750 — каталоги и исполняемые файлы.
• 700 — личный каталог, например ~/.ssh.

У каталога права значат другое: r — посмотреть список файлов, w — создавать и удалять файлы внутри, x — войти в каталог и открыть файл по имени. Без x на каталог файл внутри не открыть, даже если на сам файл права есть. Если у файла 644, а ты получаешь Permission denied, проверь все каталоги на пути: namei -l /srv/market/app/config.py покажет права каждого уровня.

Команды:
    chmod 640 .env                 # права цифрами
    chmod g+r,o-rwx .env           # символами: добавить или убрать
    chown market:www-data .env     # владелец и группа
    find /srv/market -type d -exec chmod 750 {} +
    find /srv/market -type f -exec chmod 640 {} +

chmod -R 640 на целом дереве — ошибка: каталоги потеряют x, и в них нельзя будет войти. Права для каталогов и файлов ставь раздельно, через find.

Пользователи и группы. id user показывает uid, gid и группы. Сервисы запускают от отдельных системных пользователей без шелла: useradd --system --shell /usr/sbin/nologin market. Доступ между сервисами дают через группы: usermod -aG market www-data добавит пользователя nginx в группу market. Флаг -a обязателен: без него usermod -G заменит все группы пользователя. Группы процесс получает при старте, поэтому после usermod сервис нужно перезапустить, а самому — перелогиниться.

sudo выполняет команду от root или от другого пользователя (sudo -u postgres psql). Перенаправление > выполняет твоя оболочка ещё до запуска sudo, поэтому sudo echo x > /etc/file даст Permission denied. Правильно так: echo x | sudo tee /etc/file > /dev/null (tee -a — дописать в конец). Правила sudo правят через visudo, а не прямо в /etc/sudoers.

SSH строго проверяет права. Если приватный ключ доступен кому-то кроме владельца, клиент откажется его использовать, а sshd не станет читать authorized_keys из каталога, открытого на запись всем. Норма: ~/.ssh — 700, приватный ключ — 600, authorized_keys — 600, владелец — сам пользователь.

Частые ошибки:
• chmod 777 «чтобы заработало» — теперь любой процесс на сервере может подменить твой файл. Решай через владельца и группы;
• запускать сервис от root, «чтобы не было проблем с правами»;
• забыть перезапустить процесс после usermod -aG.
""")

task(1, T, "quiz", 1, "teamlead",
     "Марина: «Разбираю сервер Маркета после стажёра. Прежде чем что-то менять, убедись, что понимаешь, кто здесь что может. Вот листинг».",
     "Выбери все верные утверждения.",
     """
Разбираем по тройкам. У .env права rw-r----- = 640: владелец market читает и пишет, группа www-data только читает, остальные ничего не могут. Пользователь www-data входит в группу www-data, значит, прочитать файл может, а изменить — нет. Каталог app — rwxr-x--- = 750, группа market; www-data в неё не входит и попадает в «остальные», у которых прав нет, поэтому войти в app не сможет. У deploy.sh для остальных r-x, а /srv/market открыт всем на вход (x), так что запустить скрипт может любой пользователь.
""",
     ["Сначала определи, в какую категорию попадает www-data для каждого файла: владелец, группа или остальные.",
      "r = 4, w = 2, x = 1. Сложи числа в каждой тройке."],
     code="""$ ls -ld /srv/market
drwxr-xr-x 3 market market 4096 Sep 20 10:02 /srv/market
$ ls -l /srv/market
-rw-r----- 1 market www-data  812 Sep 20 10:02 .env
drwxr-x--- 5 market market   4096 Sep 20 10:02 app
-rwxr-xr-x 1 market market    934 Sep 20 10:02 deploy.sh
$ id www-data
uid=33(www-data) gid=33(www-data) groups=33(www-data)
""",
     lang="text",
     options=[
         "www-data может прочитать .env: он в группе www-data, а у группы есть r",
         "www-data может изменить .env: группа www-data указана у файла, значит, может и писать",
         "www-data может зайти в каталог app: у каталога есть x, а /srv/market открыт всем",
         "deploy.sh может запустить любой пользователь сервера: у остальных есть r-x",
         "Права .env в цифрах — 640: rw- для владельца, r-- для группы",
         "Права каталога app в цифрах — 755: rwx для владельца, r-x для группы и остальных",
     ],
     answer=[0, 3, 4])

task(2, T, "find_bug", 2, "devops_colleague",
     "Дима: «Стажёр вчера “чинил” доступ на CI-раннере и сделал chmod -R 777 ~/.ssh. Теперь деплой не может зайти на сервер по ключу. Что сломалось и как чинить по-взрослому?»",
     "Какое исправление правильное?",
     """
Клиент OpenSSH отказывается использовать приватный ключ, если он доступен кому-то кроме владельца, — об этом прямо написано: «Permissions 0777 ... are too open». Правильные права: каталог ~/.ssh — 700, приватный ключ — 600, публичный ключ и known_hosts — 644. Но это только половина дела: сутки ключ был доступен на чтение всем пользователям и процессам раннера, так что его надо считать скомпрометированным — выпустить новый и убрать старый из authorized_keys на серверах. 755 не поможет: ssh отвергает ключ при любых правах для группы и остальных. ssh-add проверяет права так же, а StrictHostKeyChecking вообще про проверку ключа сервера, а не клиента. Права root:root на каталог просто отберут доступ у самого runner.
""",
     ["Прочитай предупреждение ssh: что именно ему не нравится?",
      "Какие права должны быть у приватного ключа, чтобы его мог читать только владелец?",
      "Ключ был открыт на чтение всем почти сутки. Можно ли ему дальше доверять?"],
     code="""runner@ci-runner-2:~$ ssh -i ~/.ssh/deploy_ed25519 deploy@market-web-1
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
@         WARNING: UNPROTECTED PRIVATE KEY FILE!          @
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
Permissions 0777 for '/home/runner/.ssh/deploy_ed25519' are too open.
It is required that your private key files are NOT accessible by others.
This private key will be ignored.
Load key "/home/runner/.ssh/deploy_ed25519": bad permissions
deploy@market-web-1: Permission denied (publickey).

runner@ci-runner-2:~$ ls -la ~/.ssh
drwxrwxrwx 2 runner runner 4096 Sep 23 18:40 .
drwxr-x--- 6 runner runner 4096 Sep 23 18:40 ..
-rwxrwxrwx 1 runner runner  411 Sep 12 11:05 deploy_ed25519
-rwxrwxrwx 1 runner runner  101 Sep 12 11:05 deploy_ed25519.pub
-rwxrwxrwx 1 runner runner 2890 Sep 23 18:02 known_hosts
""",
     lang="text",
     options=[
         "chmod 700 ~/.ssh, 600 на приватный ключ, 644 на .pub и known_hosts — и перевыпустить ключ: он был открыт всем",
         "chmod 755 на ключ и на ~/.ssh: ssh ругается только на право записи для остальных, чтение ему не мешает",
         "Добавить ключ в агент через ssh-add: агент не проверяет права на файл, и 777 ему не помеха",
         "Прописать в ~/.ssh/config StrictHostKeyChecking no — тогда ssh перестанет проверять права на ключ",
         "Оставить 777 на файлах, но сделать chown root:root на ~/.ssh — чужие туда больше не войдут",
     ],
     answer=0)

task(3, T, "find_bug", 2, "teamlead",
     "Гена: «Джун прислал скрипт тюнинга для сервера базы. Запускает под своим пользователем, всё через sudo, а он падает с Permission denied. Найди, в чём дело».",
     "Почему скрипт падает и как это исправить?",
     """
sudo поднимает права только для команды, которую запускает, то есть для echo. Перенаправление > /etc/sysctl.d/60-market.conf обрабатывает текущая оболочка — от обычного пользователя и ещё до запуска sudo. Поэтому ошибка возникает на открытии файла, а не в echo. Стандартное решение — отдать запись процессу, запущенному через sudo: echo ... | sudo tee файл > /dev/null (tee -a — дописать в конец). Ещё вариант — sudo sh -c 'echo ... > файл'. chmod 777 на /etc/sysctl.d — дыра в безопасности, а sudo touch создаст файл root:root, и та же запись в него всё равно не пройдёт. shellcheck ловит эту ошибку сам (SC2024).
""",
     ["Кто открывает файл для записи: echo или оболочка?",
      "sudo действует только на команду, которая идёт сразу за ним.",
      "Нужна команда, которая сама пишет в файл и которую можно запустить через sudo."],
     code="""#!/usr/bin/env bash
set -euo pipefail

sudo echo "vm.swappiness = 10" > /etc/sysctl.d/60-market.conf
sudo sysctl --system

# $ ./tune.sh
# ./tune.sh: line 4: /etc/sysctl.d/60-market.conf: Permission denied
""",
     lang="bash",
     options=[
         "Перенаправление > делает оболочка без root, ещё до sudo. Писать через tee: echo \"...\" | sudo tee файл > /dev/null",
         "Каталог /etc/sysctl.d закрыт на запись даже для sudo — сначала открыть его: sudo chmod 777 /etc/sysctl.d",
         "echo — встроенная команда bash, и sudo её не находит: писать полный путь sudo /bin/echo, тогда запись пройдёт",
         "Мешает set -u: sudo сбрасывает окружение, переменные пропадают, и скрипт падает на строке с sudo",
         "Файл должен существовать заранее: сначала sudo touch /etc/sysctl.d/60-market.conf, потом та же команда",
     ],
     answer=0)

task(4, T, "write_code", 2, "devops_colleague",
     "Дима: «На сервере пекарни “Батон” кто-то сделал chmod -R 777 /srv/baton, “чтобы заработала загрузка фото”. Верни нормальные права скриптом: код принадлежит baton, nginx из группы www-data только читает».",
     """
Допиши скрипт вместо chmod -R 777:
• всё дерево $APP_DIR принадлежит пользователю baton и группе www-data (рекурсивно);
• каталоги — 750, файлы — 640 (через find, раздельно для -type d и -type f);
• файл $APP_DIR/.env — 600, его читает только сам сервис;
• никаких 777 и никакого chmod -R с одинаковыми цифрами для файлов и каталогов.
""",
     """
chown -R baton:www-data отдаёт дерево сервису, а группе www-data — только то, что разрешат права. Каталогам нужен x, иначе в них нельзя войти, а файлам x не нужен вовсе. Поэтому права ставим раздельно через find -type d и find -type f, а {} + передаёт chmod файлы пачками, а не по одному. 750 и 640 значат: владелец пишет, nginx читает, остальные ничего не видят. .env с паролями закрываем до 600: nginx он не нужен. Загрузка фото после этого работает, потому что baton — владелец и может писать в свои каталоги. 777 давал писать туда любому процессу на сервере, в том числе взломанному.
""",
     ["Сначала владелец: chown -R пользователь:группа каталог.",
      "find \"$APP_DIR\" -type d -exec chmod 750 {} + — то же для файлов с -type f и 640.",
      "Отдельной строкой chmod 600 \"$APP_DIR/.env\"."],
     code="""#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/srv/baton"

# было так — убери и сделай правильно:
chmod -R 777 "$APP_DIR"
""",
     lang="bash",
     answer="""#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/srv/baton"

chown -R baton:www-data "$APP_DIR"
find "$APP_DIR" -type d -exec chmod 750 {} +
find "$APP_DIR" -type f -exec chmod 640 {} +
chmod 600 "$APP_DIR/.env"
""",
     tests=[
         rx("Владелец baton, группа www-data, рекурсивно", r"chown\s+(-R|--recursive)\s+baton[:.]www-data\b"),
         rx("Каталогам 750 через find -type d", r"find\s+[^\n]*-type\s+d\b[^\n]*chmod\s+0?750\b"),
         rx("Файлам 640 через find -type f", r"find\s+[^\n]*-type\s+f\b[^\n]*chmod\s+0?640\b"),
         rx(".env закрыт до 600", r"chmod\s+0?600\s+\"?(\$\{?APP_DIR\}?|/srv/baton)/\.env"),
         nrx("Нет 777", r"777"),
         nrx("Нет рекурсивного chmod с одинаковыми цифрами", r"chmod\s+-R\s+0?[0-7]{3}\b"),
     ])

task(5, T, "incident", 3, "client",
     "Вера, управляющая БЦ «Высота»: «Сайт бронирования переговорок с утра показывает 502 Bad Gateway! Вчера вечером ваши что-то обновляли — у меня люди не могут забронировать зал на совещание».",
     "Что сломалось и какое исправление правильное?",
     """
В логе nginx всё сказано: connect() к unix-сокету gunicorn падает с «13: Permission denied». Воркеры nginx работают от www-data. Сокет — srw-rw---- vysota:vysota, а каталог /run/vysota — drwxr-x--- vysota:vysota. www-data не входит в группу vysota и не может даже войти в каталог. Правильное решение — выдать доступ через группу: usermod -aG vysota www-data и перезапустить nginx, чтобы воркеры получили новую группу. Другой вариант — настроить, чтобы gunicorn создавал сокет с группой www-data. chmod 777 на сокет не поможет вовсе: каталог закрыт для остальных. Воркеры от root и сокет в /tmp — это дыры, а перезапуск gunicorn создаст сокет с теми же правами.
""",
     ["От какого пользователя работают воркеры nginx?",
      "Проверь права не только сокета, но и каталога, в котором он лежит.",
      "Доступ лучше дать через группу, а не через права для всех."],
     code="""$ sudo tail -n 1 /var/log/nginx/error.log
2026/09/24 09:02:11 [crit] 1422#1422: *310 connect() to unix:/run/vysota/gunicorn.sock failed (13: Permission denied) while connecting to upstream, client: 192.0.2.24, server: vysota-booking.ru, request: "GET / HTTP/1.1", upstream: "http://unix:/run/vysota/gunicorn.sock:/", host: "vysota-booking.ru"

$ ls -ld /run/vysota /run/vysota/gunicorn.sock
drwxr-x--- 2 vysota vysota 60 Sep 24 08:59 /run/vysota
srw-rw---- 1 vysota vysota  0 Sep 24 08:59 /run/vysota/gunicorn.sock

$ ps -o user,pid,cmd -C nginx
USER         PID CMD
root        1421 nginx: master process /usr/sbin/nginx -g daemon on; master_process on;
www-data    1422 nginx: worker process

$ id www-data
uid=33(www-data) gid=33(www-data) groups=33(www-data)
""",
     lang="text",
     options=[
         "chmod 777 /run/vysota/gunicorn.sock — nginx получит доступ к сокету, и 502 сразу пропадут",
         "Добавить www-data в группу vysota (usermod -aG vysota www-data) и перезапустить nginx",
         "Прописать в nginx.conf user root; — воркеры от root откроют любой сокет без проблем с правами",
         "Перенести сокет в /tmp/gunicorn.sock: в /tmp у всех есть права, и nginx туда достучится",
         "Перезапустить gunicorn: сокет пересоздастся, и systemd выставит на него правильные права",
         "Увеличить worker_connections: воркеры nginx не успевают подключиться к upstream под нагрузкой",
     ],
     answer=1,
     time=15)


# =====================================================================
# do-systemd-logs
# =====================================================================
T = "do-systemd-logs"
topic(T, """
systemd — первый процесс в системе (PID 1). Он запускает сервисы и следит за ними. Каждый сервис описан юнитом — текстовым файлом. Свои юниты кладут в /etc/systemd/system/, юниты из пакетов лежат в /usr/lib/systemd/system/.

    [Unit]
    Description=Lamp Market API
    After=network-online.target
    Wants=network-online.target

    [Service]
    User=market
    WorkingDirectory=/srv/market
    EnvironmentFile=/etc/market/api.env
    ExecStart=/srv/market/venv/bin/gunicorn app.main:app -b 127.0.0.1:8000
    Restart=on-failure
    RestartSec=5

    [Install]
    WantedBy=multi-user.target

• ExecStart — полный путь к программе. Это не шелл: пайпы, && и > файл здесь не работают.
• User — от кого работает процесс. Без необходимости не root. Такой пользователь должен существовать, иначе сервис упадёт со status=217/USER.
• Restart=on-failure перезапускает процесс, если он упал: ненулевой код, сигнал, таймаут. Restart=always — даже после нормального выхода. Без Restart= упавший сервис так и останется лежать. RestartSec — пауза между попытками. Больше 5 запусков за 10 секунд (StartLimitBurst / StartLimitIntervalSec) — и systemd сдаётся: сервис в failed до ручного вмешательства.
• WantedBy=multi-user.target — к какой цели подключить сервис при enable (автозапуск при загрузке). enable — это только автозапуск, перезапуск при падении он не включает.

Команды:
    systemctl daemon-reload              # после любой правки юнит-файла
    systemctl enable --now market-api    # автозапуск + запустить сейчас
    systemctl status market-api          # состояние, PID, последние строки лога
    systemctl restart market-api
    systemctl list-units --failed
    systemctl cat market-api             # что реально загружено
    systemctl edit market-api            # override, не трогая исходный файл

Код в systemctl status подсказывает, где искать: 203/EXEC — ExecStart не найден или не исполняемый; 217/USER — нет такого пользователя; 200/CHDIR — нет WorkingDirectory; 1/FAILURE — упало само приложение, смотри его лог; signal=KILL — часто OOM.

Журнал. Всё, что сервис пишет в stdout и stderr, попадает в journald:
    journalctl -u market-api -f                  # следить в реальном времени
    journalctl -u market-api --since "1 hour ago"
    journalctl -u market-api -b                  # с последней загрузки
    journalctl -p err -S today                   # ошибки за сегодня
    journalctl -k                                # ядро: OOM, диски
    journalctl --disk-usage; journalctl --vacuum-size=1G

Файловые логи и logrotate. Если приложение пишет в свой файл, файл растёт, пока не кончится диск. logrotate раз в сутки переименовывает его, сжимает старые копии и удаляет самые давние:
    /var/log/market/*.log {
        daily
        rotate 14
        compress
        delaycompress
        missingok
        notifempty
        copytruncate
    }
Процесс пишет в открытый дескриптор, а не «по имени». После переименования он продолжит писать в старый файл, поэтому нужен либо copytruncate (скопировать и обнулить), либо postrotate с командой, после которой приложение переоткроет лог.

Правило дежурного: файл, удалённый через rm, место не освобождает, пока процесс держит его открытым. Найти такие файлы — lsof +L1. Освободить место — перезапустить процесс или обнулить файл. На будущее: большие логи не удаляют, а обнуляют — truncate -s 0 файл или : > файл.
""")

task(1, T, "find_bug", 2, "devops_colleague",
     "Дима: «Ночью КофеБот упал с ошибкой и пролежал до утра — офис без кофе, все злые. Почему он падает раз в неделю, ищут разработчики, но сервис должен подниматься сам. Глянь юнит».",
     "Что нужно исправить в юните, чтобы бот сам поднимался после падения?",
     """
В юните нет директивы Restart=, а по умолчанию она равна no: упал — лежит. Restart=on-failure перезапускает сервис после ненулевого кода выхода, сигнала или таймаута, а RestartSec=5 даёт паузу, чтобы не молотить перезапусками. После правки файла обязательно systemctl daemon-reload, иначе systemd продолжит работать со старой версией юнита. enable отвечает только за автозапуск при загрузке, и сервис уже enabled. WantedBy и After — про порядок и цели загрузки, а не про перезапуск. Cron-костыль поднимет сервис с задержкой до минуты, и systemd ничего не будет знать о падениях.
""",
     ["Какая директива в [Service] отвечает за поведение после падения?",
      "Сервис уже enabled — значит, дело не в автозапуске.",
      "После правки юнита нужна ещё одна команда systemctl."],
     code="""# /etc/systemd/system/kofebot.service
[Unit]
Description=KofeBot
After=network-online.target
Wants=network-online.target

[Service]
User=kofebot
WorkingDirectory=/opt/kofebot
EnvironmentFile=/etc/kofebot/bot.env
ExecStart=/opt/kofebot/venv/bin/python -m kofebot

[Install]
WantedBy=multi-user.target

$ systemctl status kofebot
× kofebot.service - KofeBot
     Loaded: loaded (/etc/systemd/system/kofebot.service; enabled; preset: enabled)
     Active: failed (Result: exit-code) since Thu 2026-09-24 03:17:42 MSK; 5h 41min ago
    Process: 902 ExecStart=/opt/kofebot/venv/bin/python -m kofebot (code=exited, status=1/FAILURE)
   Main PID: 902 (code=exited, status=1/FAILURE)
""",
     lang="text",
     options=[
         "Добавить в [Service] Restart=on-failure и RestartSec=5, затем systemctl daemon-reload и systemctl restart kofebot",
         "Заменить WantedBy=multi-user.target на WantedBy=default.target — тогда сервис будет перезапускаться",
         "Добавить в cron строку * * * * * systemctl start kofebot",
         "Выполнить systemctl enable kofebot — enabled-сервисы systemd перезапускает автоматически",
         "Добавить After=kofebot.service, чтобы юнит зависел сам от себя и поднимался",
     ],
     answer=0)

task(2, T, "write_code", 2, "teamlead",
     "Гена: «На утренней планёрке хочу видеть, какие сервисы ночью сыпали ошибками. Дима выгружает journalctl -p err -o short в файл, а посчитать надо тебе».",
     """
Напиши функцию errors_by_source(lines). lines — строки вывода journalctl в формате short: «Sep 24 03:17:42 web-1 kofebot[902]: сообщение». Источник — пятое поле без двоеточия и без [PID] (kofebot, systemd, kernel). Посчитай строки по источникам и верни список [источник, количество], отсортированный по количеству по убыванию, при равенстве — по имени. Пропускай пустые строки, служебные строки journalctl (начинаются с «-- ») и строки-продолжения многострочных сообщений (начинаются с пробела).
""",
     """
Формат short стабилен: месяц, день, время, хост, источник с двоеточием, потом сообщение. Поэтому split() и пятое поле (индекс 4) надёжнее, чем искать первое двоеточие: оно встречается уже во времени. [PID] отрезаем через split("[")[0], иначе kofebot[902] и kofebot[903] окажутся разными источниками. Строки «-- Boot ... --» и «-- No entries --» journalctl печатает сам, а продолжения трейсбеков идут с отступом. Если их посчитать, один упавший сервис с длинным трейсбеком «наберёт» десятки ошибок. Сортировка по (-count, name) даёт стабильный топ, который удобно сравнивать день ко дню.
""",
     ["Разбей строку через split() — источник будет в parts[4].",
      "Убери последний символ «:» и отрежь всё начиная с «[».",
      "Проверки до разбора: not line.strip(), line.startswith(\"-- \"), line[0] == \" \"."],
     code="""def errors_by_source(lines):
    # lines — вывод journalctl -p err -o short, по строке
    # верни [[источник, число], ...]
    pass
""",
     lang="python",
     entry="errors_by_source",
     answer="""def errors_by_source(lines):
    counts = {}
    for line in lines:
        if not line.strip() or line.startswith("-- ") or line[0] == " ":
            continue
        parts = line.split()
        if len(parts) < 5 or not parts[4].endswith(":"):
            continue
        name = parts[4][:-1].split("[")[0]
        counts[name] = counts.get(name, 0) + 1
    return [[name, n] for name, n in sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))]
""",
     tests=[
         {"input": [[
             "-- Boot 5c1e2d3f4a5b46c7a8d9e0f1a2b3c4d5 --",
             "Sep 24 02:14:07 db-1 kernel: Out of memory: Killed process 1187 (postgres)",
             "Sep 24 03:17:41 web-1 kofebot[902]: Traceback (most recent call last):",
             "                                    File \"/opt/kofebot/kofebot/__main__.py\", line 41, in <module>",
             "Sep 24 03:17:42 web-1 systemd[1]: kofebot.service: Main process exited, code=exited, status=1/FAILURE",
             "Sep 24 03:17:42 web-1 systemd[1]: kofebot.service: Failed with result 'exit-code'.",
             "Sep 24 04:02:10 web-1 market-api[1423]: ERROR: payment gateway timeout order=88123",
             "Sep 24 04:05:55 web-1 market-api[1423]: ERROR: payment gateway timeout order=88140",
             "Sep 24 04:06:01 web-1 market-api[1424]: ERROR: payment gateway timeout order=88141",
         ]],
          "expected": [["market-api", 3], ["systemd", 2], ["kernel", 1], ["kofebot", 1]]},
         {"input": [["-- No entries --"]], "expected": []},
         {"input": [[]], "expected": []},
         {"input": [["Sep 24 05:00:00 web-1 nginx[800]: open() failed",
                     "",
                     "Sep 24 05:00:01 web-1 cron[77]: (root) ERROR (getpwnam() failed)"]],
          "expected": [["cron", 1], ["nginx", 1]]},
     ])

task(3, T, "incident", 2, "manager",
     "Стас: «Сайт пекарни “Батон” лежит после вечерней выкладки, Нина звонила уже два раза! Вот что мне скинул Дима, пока у него не сел телефон. Разберись, пожалуйста, быстро».",
     "Почему сервис не стартует и что нужно сделать?",
     """
status=217/USER и «Failed to determine user credentials» значат, что systemd не нашёл пользователя из User=. Сервис падает ещё до запуска gunicorn, поэтому в логах нет ни одной строки от приложения. Пользователь в системе называется baton-api (через дефис), а в юните указан baton_api (через подчёркивание). Исправить User=, выполнить systemctl daemon-reload и перезапустить сервис. «No such process» здесь — просто текст errno ESRCH, который вернул поиск пользователя, к OOM он отношения не имеет. Проблемы с venv дали бы 203/EXEC, а занятый порт — ошибку уже от самого gunicorn. Убрать User= и работать от root — значит превратить опечатку в дыру в безопасности.
""",
     ["Посмотри на код после status= — он говорит, на каком шаге упал запуск.",
      "Сравни User= в юните с тем, что вернул getent passwd, посимвольно."],
     code="""$ systemctl status baton-api
× baton-api.service - Baton delivery API
     Loaded: loaded (/etc/systemd/system/baton-api.service; enabled; preset: enabled)
     Active: failed (Result: exit-code) since Thu 2026-09-24 19:42:05 MSK; 3min ago
    Process: 51877 ExecStart=/srv/baton/venv/bin/gunicorn app:app -b 127.0.0.1:8001 (code=exited, status=217/USER)
   Main PID: 51877 (code=exited, status=217/USER)

$ journalctl -u baton-api -n 3 --no-pager
Sep 24 19:42:05 baton-1 (gunicorn)[51877]: baton-api.service: Failed to determine user credentials: No such process
Sep 24 19:42:05 baton-1 (gunicorn)[51877]: baton-api.service: Failed at step USER spawning /srv/baton/venv/bin/gunicorn: No such process
Sep 24 19:42:05 baton-1 systemd[1]: baton-api.service: Main process exited, code=exited, status=217/USER

$ grep '^User=' /etc/systemd/system/baton-api.service
User=baton_api

$ getent passwd | grep baton
baton-api:x:998:998::/srv/baton:/usr/sbin/nologin
""",
     lang="text",
     options=[
         "В юните User=baton_api, а в системе пользователь baton-api. Поправить User=, затем daemon-reload и restart",
         "После выкладки в venv не оказалось gunicorn — переустановить зависимости: pip install -r requirements.txt",
         "Порт 8001 занят старым процессом gunicorn — освободить его через fuser -k 8001/tcp и перезапустить сервис",
         "Убрать строку User=, пусть сервис пока работает от root — так он точно стартует, а права поправим потом",
         "«No such process» значит, что процесс сразу убил OOM killer, — добавить серверу памяти или swap",
     ],
     answer=0,
     time=10)

task(4, T, "write_code", 3, "devops_colleague",
     "Дима: «Хочу, чтобы новый сервер для записи на тренировки “Жми” поднимался одним скриптом. Установка API как сервиса — на тебе: юнит, автозапуск, перезапуск при падении».",
     """
Допиши install_service.sh. В heredoc заполни секции юнита /etc/systemd/system/zhmi-api.service:
• сервис работает от пользователя zhmi, рабочий каталог — /srv/zhmi;
• команда запуска — /srv/zhmi/venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8002;
• при падении перезапускается с паузой 5 секунд (Restart=on-failure, RestartSec=5);
• при enable подключается к multi-user.target.
После записи файла перечитай конфигурацию systemd и включи сервис с немедленным запуском.
""",
     """
Heredoc с кавычками (<<'EOF') записывает текст как есть, без подстановки переменных, — для юнитов это правильно. В [Service] указываем пользователя, каталог и полный путь к uvicorn внутри venv: systemd не активирует venv и не ищет программу по PATH шелла. Restart=on-failure с RestartSec=5 поднимет API после падения, а WantedBy=multi-user.target нужен, чтобы enable подключил сервис к обычной загрузке. После записи файла обязателен systemctl daemon-reload, иначе systemd не увидит новый юнит. enable --now одной командой включает автозапуск и стартует сервис. Скрипт можно запускать повторно: он перезапишет юнит и снова всё применит.
""",
     ["В [Service] нужны User, WorkingDirectory, ExecStart, Restart и RestartSec.",
      "В [Install] — одна строка WantedBy=…",
      "После EOF: systemctl daemon-reload, затем systemctl enable --now zhmi-api."],
     code="""#!/usr/bin/env bash
set -euo pipefail

UNIT=/etc/systemd/system/zhmi-api.service

cat > "$UNIT" <<'EOF'
[Unit]
Description=Zhmi booking API
After=network-online.target
Wants=network-online.target

[Service]
# твой код

[Install]
# твой код
EOF

# перечитай юниты и включи сервис
""",
     lang="bash",
     answer="""#!/usr/bin/env bash
set -euo pipefail

UNIT=/etc/systemd/system/zhmi-api.service

cat > "$UNIT" <<'EOF'
[Unit]
Description=Zhmi booking API
After=network-online.target
Wants=network-online.target

[Service]
User=zhmi
Group=zhmi
WorkingDirectory=/srv/zhmi
ExecStart=/srv/zhmi/venv/bin/uvicorn app.main:app --host 127.0.0.1 --port 8002
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now zhmi-api.service
""",
     tests=[
         rx("Сервис работает от пользователя zhmi", r"^User=zhmi\s*$"),
         rx("Рабочий каталог /srv/zhmi", r"^WorkingDirectory=/srv/zhmi/?\s*$"),
         rx("ExecStart с полным путём к uvicorn", r"^ExecStart=/srv/zhmi/venv/bin/uvicorn\s+app\.main:app\b"),
         rx("Перезапуск при падении", r"^Restart=(on-failure|always)\s*$"),
         rx("Пауза перед перезапуском 5 секунд", r"^RestartSec=5s?\s*$"),
         rx("Автозапуск в multi-user.target", r"^WantedBy=multi-user\.target\s*$"),
         rx("daemon-reload после записи юнита", r"^EOF\s*$[\s\S]*systemctl\s+daemon-reload"),
         rx("Сервис включён и запущен",
            r"systemctl\s+enable\s+--now\s+zhmi-api|systemctl\s+enable\s+zhmi-api(\.service)?\s+--now|systemctl\s+enable\s+zhmi-api[\s\S]*systemctl\s+(re)?start\s+zhmi-api"),
     ])

task(5, T, "incident", 3, "devops_colleague",
     "Дима: «Маркет не принимает заказы: в логах “No space left on device”. Я сгоряча удалил огромный лог приложения, а место так и не освободилось. Объясни, что происходит и что делаем».",
     "Выбери все правильные действия.",
     """
du по каталогам насчитывает около 7,5 ГБ, а df показывает 50 ГБ занято. Разницу держит удалённый файл: lsof +L1 показывает app.log с пометкой (deleted), NLINK 0 и размером около 38 ГБ. rm убрал только имя из каталога, а данные остаются на диске, пока gunicorn держит файл открытым и пишет в него. Освободить место можно перезапуском сервиса (дескриптор закроется) или обнулением файла через /proc/PID/fd/N — без простоя. Чтобы это не повторилось, нужен logrotate для /var/log/market/*.log, и большие логи надо обнулять (truncate -s 0 или : > файл), а не удалять. Повторный rm ничего не даст, reboot — лишний простой, а /var/lib/postgresql — это данные базы, а не кэш.
""",
     ["Сравни df и du: где пропали около 38 ГБ?",
      "Что значит (deleted) и NLINK 0 в выводе lsof?",
      "Пока процесс держит файл открытым, данные остаются на диске."],
     code="""$ df -h /var
Filesystem      Size  Used Avail Use% Mounted on
/dev/vdb1        50G   50G     0 100% /var

$ sudo du -sh /var/log/market /var/log/journal /var/lib/postgresql
12K     /var/log/market
1.1G    /var/log/journal
6.3G    /var/lib/postgresql

$ sudo lsof +L1 /var
COMMAND   PID   USER   FD   TYPE DEVICE    SIZE/OFF NLINK   NODE NAME
gunicorn 1423 market    5w   REG  252,17 41318752256     0 131090 /var/log/market/app.log (deleted)
gunicorn 1424 market    5w   REG  252,17 41318752256     0 131090 /var/log/market/app.log (deleted)

$ cat /etc/logrotate.d/market
cat: /etc/logrotate.d/market: No such file or directory
""",
     lang="text",
     options=[
         "Перезапустить market-api или обнулить удалённый app.log через дескриптор: truncate -s 0 /proc/1423/fd/5",
         "Ещё раз выполнить rm -rf /var/log/market/* — первое удаление не завершилось, потому что файл был занят",
         "Настроить logrotate для /var/log/market/*.log: daily, rotate, compress и copytruncate",
         "Почистить /var/lib/postgresql: 6,3 ГБ там — кэш базы, он пересоздастся при старте",
         "На будущее: большие логи обнулять (truncate -s 0 или : > файл), а не удалять через rm",
         "Перезагрузить сервер — только так ядро освободит место, занятое удалёнными файлами",
     ],
     answer=[0, 2, 4],
     time=20)


# =====================================================================
# do-networks
# =====================================================================
T = "do-networks"
topic(T, """
«Сайт не открывается» — самая частая жалоба, с которой приходят к DevOps. Задача — за пару минут найти шаг, на котором рвётся цепочка: имя → IP → сеть и файрвол → порт → процесс → приложение. Иди по шагам и не гадай.

Шаг 1. DNS: во что резолвится имя?
    dig +short lampovy-market.ru            # только IP
    dig lampovy-market.ru A                 # полный ответ: статус, TTL, кто ответил
    dig @1.1.1.1 lampovy-market.ru          # спросить конкретный резолвер
Статус в ответе: NXDOMAIN — такого имени нет; NOERROR без ответа — имя есть, а записи этого типа нет; SERVFAIL — резолвер не смог получить ответ. Число перед IN — TTL: сколько секунд резолверы кэшируют ответ. Поэтому перед переездом TTL снижают заранее, минимум за время старого TTL, иначе часть пользователей ещё сутки будет ходить на старый IP. /etc/hosts на машине главнее DNS: забытая строка там часто объясняет «у меня работает, а у всех нет».

Шаг 2. Слушает ли кто-нибудь порт?
    sudo ss -tlnp                     # TCP, LISTEN, цифрами, с процессами
    ss -tan state established | wc -l # сколько установленных соединений
Смотри на адрес: 127.0.0.1:8000 принимает только локальные подключения, а 0.0.0.0:8000, [::]:8000 или *:8000 — со всех интерфейсов. Приложение на 127.0.0.1 снаружи недоступно, и это правильно, если перед ним стоит nginx.

Шаг 3. Доходит ли пакет?
    curl -v http://10.0.0.5:8000/health
    nc -vz 10.0.0.5 5432              # просто проверить TCP-порт
ICMP часто закрыт, поэтому молчащий ping ещё ничего не доказывает.

Ошибки подключения — половина диагностики:
• Could not resolve host / Name or service not known — проблема в DNS или опечатка в имени.
• Connection refused — пакет дошёл до машины, но на этом адресе и порту никто не слушает, и ядро сразу ответило отказом. Реже так отвечает файрвол с правилом REJECT.
• Connection timed out — ответа нет вообще: пакеты молча отбрасывает файрвол или security group, либо хост недоступен.
• Ошибки TLS — соединение есть, проблема в сертификате. HTTP 502/504 — до прокси достучались, ломается то, что за ним.

Шаг 4. Файрвол. На сервере это nftables или iptables, обычно через обёртку: ufw на Ubuntu, firewalld на RHEL-подобных. Плюс облачные security groups, которые работают ещё до сервера.
    sudo ufw status verbose
    sudo ufw allow 443/tcp
    sudo nft list ruleset
Политика по умолчанию — «входящие запрещены, исходящие разрешены», открывай только нужное: 22 (лучше с ограничением по IP), 80, 443. Базы, Redis и экспортеры метрик наружу не открывают: они слушают 127.0.0.1 или внутреннюю сеть. Прежде чем включать ufw на удалённом сервере, разреши SSH, иначе отрежешь себя.

Порты, которые стоит помнить: 22 SSH, 53 DNS, 80/443 HTTP(S), 5432 PostgreSQL, 6379 Redis, 3306 MySQL, 9100 node_exporter, 8000/8080 — типичные порты приложений.

Частые ошибки: открыть порт в ufw и забыть про security group (или наоборот); проверить с самого сервера через localhost и решить, что всё работает; менять DNS, не снизив заранее TTL; выставить Redis на 0.0.0.0 без пароля.
""")

task(1, T, "quiz", 1, "qa",
     "Ира: «Проверяю три стенда Маркета через nc, как Дима учил. Все три не отвечают, но ошибки разные. Подскажи, что каждая значит, чтобы я завела баги на правильных людей».",
     "Выбери все верные утверждения.",
     """
Разные ошибки показывают разные шаги цепочки. «Name or service not known» — имя не превратилось в IP, до сети дело не дошло: проблема в DNS-записи или в написании имени. «Connection refused» — пакет дошёл до хоста, и тот сразу ответил отказом. Обычно это значит, что на этом адресе и порту никто не слушает: сервис лежит или слушает только 127.0.0.1. Файрвол с REJECT тоже так умеет, поэтому слово «точно» тут неверно. «Timed out» — ответа нет совсем: пакеты молча отбрасывает файрвол или security group, либо хост выключен. Медленное приложение дало бы соединение и ждущий ответ, а не таймаут на подключении. А nc с именами работает прекрасно: getaddrinfo в тексте ошибки — это и есть его попытка превратить имя в IP.
""",
     ["Какая ошибка возникает ещё до того, как пакет ушёл в сеть?",
      "Отказ — это ответ. Таймаут — отсутствие ответа. Кто может не отвечать совсем?"],
     code="""$ nc -vz -w 5 stage1.lampovy-market.ru 8000
nc: getaddrinfo for host "stage1.lampovy-market.ru" port 8000: Name or service not known

$ nc -vz -w 5 10.20.0.12 8000
nc: connect to 10.20.0.12 port 8000 (tcp) failed: Connection refused

$ nc -vz -w 5 10.20.0.13 8000
nc: connect to 10.20.0.13 port 8000 (tcp) timed out: Operation now in progress
""",
     lang="text",
     options=[
         "stage1: имя не резолвится — ошибка в DNS-записи или опечатка, до сервера дело не дошло",
         "10.20.0.12: хост ответил отказом — скорее всего, на порту 8000 никто не слушает",
         "10.20.0.13: ответа нет совсем — пакеты молча отбрасывает файрвол, либо хост выключен",
         "10.20.0.12: отказ в соединении — это точно файрвол, сервис тут ни при чём",
         "10.20.0.13: таймаут значит, что приложение медленно отвечает, — надо оптимизировать SQL",
         "stage1: DNS в порядке, просто nc не умеет подключаться по имени — нужно указывать IP",
     ],
     answer=[0, 1, 2])

task(2, T, "incident", 2, "client",
     "Нина, владелица пекарни «Батон»: «Разработчик говорит, что новый сайт доставки уже запущен на сервере и порт открыт. А у меня в браузере ничего не открывается! Он отвечает, что у него всё работает».",
     "Почему сайт не открывается снаружи и как это правильно исправить?",
     """
Снаружи — Connection refused, а на самом сервере curl к 127.0.0.1:8080 даёт 200. ss показывает причину: node слушает 127.0.0.1:8080, то есть принимает только локальные подключения. Правило ufw для 8080 есть, а закрытая security group дала бы таймаут, а не мгновенный отказ: пакет доходит до сервера, но на внешнем адресе порт никто не слушает. DNS тоже ни при чём — Нина ходит по IP. Правильная схема для прода — nginx на 80/443 с proxy_pass на 127.0.0.1:8080: TLS, сжатие и статика на nginx, приложение наружу не торчит. Правило 8080 в ufw после этого стоит закрыть. Слушать 0.0.0.0 можно разве что временно, для проверки.
""",
     ["Сравни, как отвечает сервис с сервера и снаружи.",
      "Посмотри на Local Address у процесса node в выводе ss."],
     code="""# с ноутбука Нины
$ curl -sS http://198.51.100.40:8080/
curl: (7) Failed to connect to 198.51.100.40 port 8080 after 41 ms: Connection refused

# на сервере
$ curl -s -o /dev/null -w '%{http_code}\\n' http://127.0.0.1:8080/
200

$ sudo ss -tlnp
State   Recv-Q  Send-Q   Local Address:Port   Peer Address:Port  Process
LISTEN  0       511          127.0.0.1:8080        0.0.0.0:*      users:(("node",pid=7712,fd=21))
LISTEN  0       128            0.0.0.0:22          0.0.0.0:*      users:(("sshd",pid=611,fd=3))
LISTEN  0       128               [::]:22             [::]:*      users:(("sshd",pid=611,fd=4))

$ sudo ufw status
Status: active

To                         Action      From
--                         ------      ----
22/tcp                     ALLOW       Anywhere
8080/tcp                   ALLOW       Anywhere
""",
     lang="text",
     options=[
         "Приложение слушает только 127.0.0.1:8080. Поставить перед ним nginx на 80/443 с proxy_pass на 127.0.0.1:8080",
         "ufw тут ни при чём, 8080 закрыт в security group облака — открыть его там для всех адресов, как в ufw",
         "У Нины закэшировался старый DNS-ответ — подождать сутки, пока истечёт TTL, или сбросить кэш",
         "Процесс node падает на внешних запросах — перезапустить его через pm2 с автоперезапуском",
         "Провайдер Нины блокирует нестандартный порт 8080 — пусть проверит сайт с мобильного интернета",
     ],
     answer=0,
     time=10)

task(3, T, "write_code", 2, "teamlead",
     "Марина: «Хочу в ежедневный аудит серверов проверку: какие порты слушаются снаружи, хотя не должны. На прошлой неделе нашли Redis на 0.0.0.0 без пароля. Вывод ss соберём сами, напиши функцию разбора».",
     """
Напиши exposed_ports(lines, allowed). lines — строки вывода `ss -tlnH` (без заголовка), allowed — список разрешённых портов. Локальный адрес — четвёртая колонка, порт — всё после последнего двоеточия. Loopback-адреса (начинаются с «127.», а также [::1]) не считаются внешними. Верни отсортированный по возрастанию список портов (int, без повторов), которые слушаются на НЕ-loopback адресах (0.0.0.0, [::], *, конкретный IP) и не входят в allowed. Пустые строки пропускай.
""",
     """
Адрес с портом берём из четвёртой колонки и делим по последнему двоеточию через rpartition. Это важно для IPv6: в [::]:22 двоеточий несколько, а порт — только после последнего. Loopback определяем по префиксу 127. (сюда попадает и 127.0.0.53%lo у systemd-resolved) и по [::1]: такие порты снаружи недоступны и аудиту не интересны. Всё остальное — 0.0.0.0, [::], * или конкретный IP интерфейса — видно из сети, и если порта нет в allowed, это находка. Множество убирает дубли: один и тот же сервис часто слушает и IPv4, и IPv6. В примере аудит ловит Redis на 0.0.0.0:6379 и node_exporter на *:9100 — ровно то, что обычно забывают закрыть.
""",
     ["Четвёртая колонка — parts[3], порт — после последнего «:» (rpartition).",
      "Loopback: addr.startswith(\"127.\") или addr == \"[::1]\".",
      "Собирай порты в set, а в конце верни sorted(...)."],
     code="""def exposed_ports(lines, allowed):
    # lines — строки `ss -tlnH`, allowed — разрешённые порты
    pass
""",
     lang="python",
     entry="exposed_ports",
     answer="""def exposed_ports(lines, allowed):
    found = set()
    for line in lines:
        parts = line.split()
        if len(parts) < 4:
            continue
        addr, _, port = parts[3].rpartition(":")
        if addr.startswith("127.") or addr == "[::1]":
            continue
        if int(port) not in allowed:
            found.add(int(port))
    return sorted(found)
""",
     tests=[
         {"input": [["LISTEN 0      4096   127.0.0.53%lo:53        0.0.0.0:*",
                     "LISTEN 0      128          0.0.0.0:22        0.0.0.0:*",
                     "LISTEN 0      511          0.0.0.0:80        0.0.0.0:*",
                     "LISTEN 0      200        127.0.0.1:5432      0.0.0.0:*",
                     "LISTEN 0      511          0.0.0.0:6379      0.0.0.0:*",
                     "LISTEN 0      128             [::]:22           [::]:*",
                     "LISTEN 0      511            [::1]:6379         [::]:*",
                     "LISTEN 0      4096               *:9100            *:*"], [22, 80, 443]],
          "expected": [6379, 9100]},
         {"input": [["LISTEN 0 200 127.0.0.1:5432 0.0.0.0:*", "LISTEN 0 511 [::1]:6379 [::]:*"], [22]],
          "expected": []},
         {"input": [["LISTEN 0 4096 0.0.0.0:8000 0.0.0.0:*", "LISTEN 0 4096 [::]:8000 [::]:*",
                     "LISTEN 0 128 10.0.0.5:5432 0.0.0.0:*", ""], []],
          "expected": [5432, 8000]},
         {"input": [[], [22]], "expected": []},
     ])

task(4, T, "incident", 3, "devops_colleague",
     "Дима: «Переносим staging Маркета на новую виртуалку. Nginx поднял, сертификат есть, на сервере всё отвечает, а снаружи браузер крутится и отваливается по таймауту. Я уже час смотрю, нужен свежий взгляд».",
     "Что мешает и какое действие правильное?",
     """
Идём по цепочке. DNS указывает на 203.0.113.77, и это адрес сервера. Nginx слушает 0.0.0.0:80 и 0.0.0.0:443. Локальный запрос через --resolve на 127.0.0.1 даёт 200, значит, nginx и сертификат в порядке. Снаружи при этом не refused, а таймаут: пакеты кто-то молча выбрасывает. ufw показывает политику deny (incoming) и единственное правило для 22/tcp. Лечится так: ufw allow 80/tcp и ufw allow 443/tcp (или профиль «Nginx Full»). Если не поможет, следующий подозреваемый — security group облака. Выключать файрвол целиком нельзя: наружу сразу откроется всё, что слушает 0.0.0.0. somaxconn и перевыпуск сертификата не помогут, потому что до TLS дело даже не доходит.
""",
     ["Refused или timeout — что это говорит о том, кто отвечает на пакеты?",
      "Проверь по очереди: DNS → слушает ли nginx → отвечает ли локально → файрвол.",
      "Посмотри, какие порты разрешены в ufw."],
     code="""# с ноутбука
$ curl -sv --connect-timeout 10 https://stage.lampovy-market.ru/health
*   Trying 203.0.113.77:443...
* Connection timed out after 10001 milliseconds
curl: (28) Connection timed out after 10001 milliseconds

$ dig +short stage.lampovy-market.ru
203.0.113.77

# на сервере
$ ip -4 addr show eth0 | grep inet
    inet 203.0.113.77/24 brd 203.0.113.255 scope global eth0

$ sudo ss -tlnp '( sport = :443 or sport = :80 )'
State  Recv-Q Send-Q Local Address:Port  Peer Address:Port Process
LISTEN 0      511          0.0.0.0:443        0.0.0.0:*     users:(("nginx",pid=2210,fd=7))
LISTEN 0      511          0.0.0.0:80         0.0.0.0:*     users:(("nginx",pid=2210,fd=6))

$ curl -s -o /dev/null -w '%{http_code}\\n' --resolve stage.lampovy-market.ru:443:127.0.0.1 https://stage.lampovy-market.ru/health
200

$ sudo ufw status verbose
Status: active
Logging: on (low)
Default: deny (incoming), allow (outgoing), disabled (routed)
New profiles: skip

To                         Action      From
--                         ------      ----
22/tcp                     ALLOW IN    Anywhere
22/tcp (v6)                ALLOW IN    Anywhere (v6)
""",
     lang="text",
     options=[
         "A-запись осталась от старого стенда и указывает не на тот сервер — поменять её на новый IP",
         "nginx слушает только localhost — поменять listen на 0.0.0.0:443 и перечитать конфиг",
         "ufw отбрасывает входящие на 80 и 443 (разрешён только 22): ufw allow 80/tcp и ufw allow 443/tcp",
         "Сертификат выпущен для другого имени, и браузер обрывает TLS-рукопожатие — перевыпустить его",
         "Выключить файрвол целиком (ufw disable): на staging он не нужен, а время дороже",
         "Сервер не успевает принимать соединения — увеличить net.core.somaxconn и backlog в listen",
     ],
     answer=2,
     time=15)

task(5, T, "architecture", 3, "manager",
     "Стас: «В субботу переезжаем сайт бронирования БЦ “Высота” на новый сервер. Вера просила две вещи: чтобы брони не терялись и чтобы никто не видел ошибок. Как будем переключать?»",
     "Какой план переключения правильный?",
     """
TTL 86400 значит, что резолверы провайдеров и браузеры могут держать старый IP до суток после смены записи. Поэтому TTL снижают заранее — минимум за время старого TTL, то есть за сутки. Тогда в момент переключения кэши живут всего 5 минут. База должна быть одна: если два сервера сутки работают со своими копиями, брони разойдутся (split brain), и склеивать их придётся руками. Старый сервер после переключения не выключают, а превращают в прокси на новый: запоздавшие клиенты попадут в ту же базу и не увидят ошибок. Снизить TTL в момент переключения бесполезно — уже закэшированные ответы доживут свои 86400 секунд. Просить пользователей править /etc/hosts — не план.
""",
     ["Что означает 86400 в ответе dig?",
      "Когда нужно снижать TTL, чтобы это успело подействовать?",
      "Что будет с бронями, если сутки работают два сервера с разными копиями базы?"],
     code="""$ dig vysota-booking.ru A +noall +answer
vysota-booking.ru.      86400   IN      A       198.51.100.23

# старый сервер: 198.51.100.23, новый: 203.0.113.50
# PostgreSQL с бронями сейчас на старом сервере, переезжает вместе с приложением
""",
     lang="text",
     options=[
         "В субботу поменять A-запись и сразу выключить старый сервер: современные резолверы обновляются за пару минут",
         "За сутки снизить TTL до 300. В субботу перенести базу и сменить A-запись, а старый сервер оставить прокси на новый до истечения кэшей",
         "Поднять новый сервер с копией базы, переключить A-запись и сутки держать оба, каждый со своей базой, а в понедельник слить брони",
         "Разослать пользователям инструкцию: очистить DNS-кэш и прописать новый IP в /etc/hosts до конца недели",
         "В субботу сразу после смены IP поставить TTL 0 — резолверы увидят новый TTL и мгновенно сбросят кэш",
     ],
     answer=1)


# =====================================================================
# do-bash
# =====================================================================
T = "do-bash"
topic(T, """
Bash-скрипт — это те же команды, что ты вводишь руками, плюс переменные, условия и циклы. DevOps пишет такие скрипты постоянно: деплой, бэкапы, чистка, проверки. Главное требование к скрипту для прода — падать громко, а не продолжать молча после ошибки.

Начало любого скрипта:
    #!/usr/bin/env bash
    set -euo pipefail

• -e — выйти, если команда вернула ненулевой код. Исключения: проверки в if и while, части && и ||.
• -u — ошибка при обращении к неустановленной переменной. Опечатка $BAKUP_DIR не превратится молча в пустую строку.
• -o pipefail — пайплайн считается упавшим, если упала любая команда в нём, а не только последняя. Без этого pg_dump | gzip > dump.gz «успешен», даже когда pg_dump упал: gzip-то отработал.

Переменные и кавычки:
    name="market"                         # без пробелов вокруг =
    echo "Бэкап ${name}_$(date +%F).sql.gz"
Переменные всегда бери в двойные кавычки: "$file". Без кавычек bash разобьёт значение по пробелам и раскроет * и ?, и файл «отчёт за май.csv» превратится в три аргумента. Одинарные кавычки ничего не подставляют: '$HOME' — это буквально $HOME.

Классика жанра:
    rm -rf $BUILD_DIR/*          # если BUILD_DIR пустая, получится rm -rf /*
    rm -rf "${BUILD_DIR:?}"/*    # :? — упасть, если переменная пустая или не задана

Коды выхода. Каждая команда возвращает число: 0 — успех, всё остальное — ошибка. Код последней команды лежит в $?. Скрипт тоже должен завершаться осознанно: exit 0 при успехе, ненулевой код при ошибке — по нему CI и cron понимают, всё ли хорошо. Ошибки пиши в stderr: echo "нет файла" >&2.
    if grep -q "ERROR" app.log; then echo "есть ошибки"; fi
    systemctl is-active --quiet nginx || echo "nginx лежит" >&2
grep возвращает 1, если ничего не нашёл. Под set -e такая команда вне if и || завершит скрипт. То же с (( count++ )) при count=0: выражение равно 0, а это «ложь». Пиши count=$((count + 1)).

Условия удобнее писать через [[ ]]:
    [[ -f "$file" ]]      # файл существует
    [[ -d "$dir" ]] || mkdir -p "$dir"
    [[ "$env" == "prod" ]]
    [[ "$version" =~ ^v[0-9]+$ ]]   # регулярка, её не берут в кавычки
Флаги проверок: -f файл, -d каталог, -e существует, -s не пустой, -z пустая строка, -n непустая.

Аргументы скрипта: $1, $2… — позиционные, $# — их количество, "$@" — все сразу, каждый отдельным словом (всегда в кавычках), $0 — имя скрипта. Проверяй аргументы в начале:
    if [[ $# -ne 2 ]]; then
        echo "usage: $0 <env> <version>" >&2
        exit 2
    fi

case удобен для разбора вариантов:
    case "$env" in
        staging|prod) ;;
        *) echo "unknown env: $env" >&2; exit 2 ;;
    esac

Цикл по файлам — через glob, а не через ls:
    for f in /var/log/market/*.log; do
        [[ -e "$f" ]] || continue    # если файлов нет, glob останется строкой
        gzip "$f"
    done
for f in $(ls ...) ломается на пробелах в именах. Для рекурсивного обхода: find ... -print0 | while IFS= read -r -d '' f; do ...; done.

Проверяй скрипты через shellcheck: он ловит почти всё из этого урока — пропущенные кавычки, ls в цикле, sudo с перенаправлением.
""")

task(1, T, "quiz", 1, "teamlead",
     "Гена: «Проверка логов в CI падает “без причины” — причём как раз на чистых логах. Прежде чем чинить, разберись, как тут работают коды выхода».",
     "В app.log нет ни одной строки с ERROR. Что сделает скрипт?",
     """
grep -c печатает количество совпадений, но код возврата у него такой же, как у обычного grep: 0 — нашёл, 1 — не нашёл, 2 — ошибка. У присваивания с подстановкой $(...) код равен коду этой подстановки, поэтому errors=$(...) вернёт 1, и set -e завершит скрипт до echo. Снаружи это выглядит как «упал без причины» — и ровно на чистых логах. Исправление: errors=$(grep -c "ERROR" app.log || true) — вывод «0» сохранится, а ненулевой код погасит || true. Если нужно только проверить факт, пиши if grep -q ...; then — внутри if set -e не срабатывает.
""",
     ["Какой код возврата у grep, когда он ничего не нашёл?",
      "Код присваивания с $(...) — это код команды внутри подстановки."],
     code="""#!/usr/bin/env bash
set -euo pipefail

errors=$(grep -c "ERROR" /var/log/market/app.log)
echo "Ошибок: $errors"
""",
     lang="bash",
     options=[
         "Выведет «Ошибок: 0» и завершится с кодом 0: код подстановки $(...) в присваивании не проверяется",
         "grep -c напечатает 0, но вернёт код 1, и set -e завершит скрипт с кодом 1 до echo",
         "Выведет «Ошибок: » с пустым значением: grep -c ничего не печатает, если совпадений нет",
         "Упадёт на set -u: переменная, заданная через подстановку, считается неустановленной",
     ],
     answer=1)

task(2, T, "find_bug", 2, "devops_colleague",
     "Дима: «Хотели откатить базу КофеБота, а в бэкапах за неделю пустые файлы по 20 байт. При этом скрипт каждую ночь писал “Бэкап готов”, и cron молчал. Найди, почему никто не заметил».",
     "Почему скрипт считает бэкап успешным, хотя pg_dump падает?",
     """
Без pipefail код пайплайна — это код последней команды, то есть gzip. gzip честно сжал пустой поток (20 байт — это заголовок gzip без данных) и вернул 0, поэтому set -e ничего не заметил, файл ушёл в S3, а в лог записалось «Бэкап готов». С set -euo pipefail пайплайн вернёт код упавшего pg_dump, и скрипт остановится до загрузки. Отдельно надо починить саму причину — пароль (например, через ~/.pgpass с правами 600). А лучше ещё и проверять бэкап: gzip -t, pg_restore --list или регулярное тестовое восстановление. set -e прекрасно работает и для команд с перенаправлением, имя файла с датой каждый день новое, а gzip и aws s3 cp тут ни при чём.
""",
     ["Какой код возвращает пайплайн a | b по умолчанию?",
      "gzip успешно сжимает даже пустой поток.",
      "Посмотри на строку set в начале скрипта — чего там не хватает?"],
     code="""#!/usr/bin/env bash
set -eu

BACKUP_DIR="/var/backups/kofebot"
FILE="$BACKUP_DIR/kofebot_$(date +%F).sql.gz"

pg_dump -h db-1 -U kofebot kofebot | gzip > "$FILE"
aws s3 cp "$FILE" "s3://kodzilla-backups/kofebot/"
echo "Бэкап готов: $FILE"

# /var/log/kofebot-backup.log:
# pg_dump: error: connection to server at "db-1" (10.0.0.12), port 5432 failed: FATAL:  password authentication failed for user "kofebot"
# upload: ../../var/backups/kofebot/kofebot_2026-09-23.sql.gz to s3://kodzilla-backups/kofebot/kofebot_2026-09-23.sql.gz
# Бэкап готов: /var/backups/kofebot/kofebot_2026-09-23.sql.gz
""",
     lang="bash",
     options=[
         "Без pipefail код пайплайна — код gzip, а gzip успешно сжал пустой поток. Нужно set -euo pipefail",
         "set -e не действует на команды с перенаправлением > — после pg_dump нужно проверять $? вручную",
         "gzip портит дамп при сжатии на лету — писать .sql без сжатия, а сжимать уже в S3",
         "date +%F даёт одинаковое имя, и при повторном запуске cron файл перезаписывается пустым",
         "aws s3 cp копирует файл, пока gzip ещё пишет в него, — нужен sync или sleep 5 перед загрузкой",
     ],
     answer=0)

task(3, T, "write_code", 2, "teamlead",
     "Марина: «Деплой Маркета запускают руками, и вчера кто-то выкатил на прод версию “latets”. Напиши обёртку deploy.sh, которая проверит аргументы, прежде чем что-то делать».",
     """
Допиши deploy.sh <env> <version>:
• строгий режим set -euo pipefail;
• если аргументов не ровно два — usage в stderr и выход с кодом 2;
• env может быть только staging или prod, иначе ошибка в stderr и код 2;
• version должна выглядеть как v1.2.3 — проверь регуляркой через [[ =~ ]], иначе ошибка в stderr и код 2;
• в конце вызови ./scripts/rollout.sh с env и version, переменные — в двойных кавычках.
""",
     """
Строгий режим и проверка $# в самом начале не дают скрипту дойти до выкладки с кривыми аргументами. Без -u пустой $2 превратился бы в пустую строку, а не в ошибку. case удобно перечисляет допустимые окружения и одной веткой * ловит всё остальное. [[ =~ ]] проверяет формат версии, и «latets» или «1.2» отсекаются ещё до деплоя. Регулярку не берут в кавычки, иначе bash сравнит строку буквально. Сообщения идут в stderr (>&2), а код 2 по традиции означает «неправильное использование». По нему CI отличит ошибку в вызове от упавшего деплоя. Кавычки вокруг "$env" и "$version" защищают от пробелов и символов glob в значениях.
""",
     ["Проверку [[ $# -ne 2 ]] ставь до того, как читать $1 и $2.",
      "case \"$env\" in staging|prod) ;; *) ... exit 2 ;; esac",
      "[[ ! \"$version\" =~ ^v[0-9]+\\.[0-9]+\\.[0-9]+$ ]] — регулярка без кавычек."],
     code="""#!/usr/bin/env bash

env=$1
version=$2

./scripts/rollout.sh $env $version
""",
     lang="bash",
     answer=r"""#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "usage: $0 <staging|prod> <vX.Y.Z>" >&2
    exit 2
}

[[ $# -eq 2 ]] || usage

env="$1"
version="$2"

case "$env" in
    staging|prod) ;;
    *) echo "unknown env: $env" >&2; exit 2 ;;
esac

if [[ ! "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "bad version: $version (expected vX.Y.Z)" >&2
    exit 2
fi

./scripts/rollout.sh "$env" "$version"
""",
     tests=[
         rx("Строгий режим set -euo pipefail", r"^\s*set\s+(-(euo|ueo)\s+pipefail|-(eu|ue)\s+-o\s+pipefail)"),
         rx("Проверяется число аргументов", r"\$#\"?\s*(-ne|-eq|-lt|-gt|!=|==|<|>)\s*\"?2\b"),
         rx("Ошибки завершаются с кодом 2", r"\bexit\s+2\b"),
         rx("Сообщения об ошибках — в stderr", r">&2|>\s*/dev/stderr"),
         rx("env проверяется на staging/prod", r"staging\s*\|\s*prod|prod\s*\|\s*staging|(==|!=|=)\s*\"?staging\"?[\s\S]*(==|!=|=)\s*\"?prod\b|(==|!=|=)\s*\"?prod\"?[\s\S]*(==|!=|=)\s*\"?staging\b"),
         rx("Версия проверяется регуляркой через =~", r"=~\s*\S+"),
         rx("rollout.sh вызывается с переменными в кавычках", r"rollout\.sh\s+\"\$\{?(env|1)\}?\"\s+\"\$\{?(version|2)\}?\""),
     ])

task(4, T, "write_code", 3, "devops_colleague",
     "Дима: «Артур из “Жми” каждый день выгружает отчёты в CSV на сервер, и они копятся тысячами. Нужен скрипт для cron, который сожмёт их и уберёт в архив. Имена там человеческие, с пробелами — “отчёт март.csv”».",
     """
Напиши archive_exports.sh <dir>:
• set -euo pipefail;
• если аргументов не ровно один — usage в stderr и exit 2; если каталога нет — ошибка в stderr и exit 1;
• создай каталог "$dir/archive" (mkdir -p);
• для каждого *.csv в каталоге (цикл for по glob, не по ls): сожми через gzip и перенеси .gz в archive;
• если CSV нет, цикл должен просто ничего не делать;
• в конце выведи «archived: N», где N — число обработанных файлов. Счётчик увеличивай так, чтобы set -e его не уронил.
""",
     """
for f in "$dir"/*.csv раскрывает glob в список файлов, где каждое имя — отдельное слово, даже с пробелами. Поэтому «отчёт март.csv» останется одним файлом. Вывод $(ls) делится по пробелам, и имя развалится на куски. Если CSV нет, glob не раскрывается и f становится буквальной строкой «…/*.csv». Проверка [[ -e "$f" ]] || continue (или shopt -s nullglob) это ловит. Все переменные в кавычках, иначе gzip получит два аргумента вместо одного. Счётчик — count=$((count + 1)): выражение (( count++ )) при count=0 возвращает код 1, и set -e завершит скрипт на первом же файле. Коды выхода различают ошибку вызова (2) и отсутствие каталога (1), а cron по ним поймёт, что что-то не так.
""",
     ["Цикл: for f in \"$dir\"/*.csv; do ... done — кавычки вокруг переменной, звёздочка снаружи.",
      "Первой строкой в цикле: [[ -e \"$f\" ]] || continue.",
      "gzip \"$f\" создаст \"$f.gz\" — его и переноси. Счётчик: count=$((count + 1))."],
     code="""#!/usr/bin/env bash
set -euo pipefail

dir=$1

for f in $(ls $dir/*.csv); do
    gzip $f
    mv $f.gz $dir/archive/
done
""",
     lang="bash",
     answer="""#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "usage: $0 <dir>" >&2
    exit 2
fi

dir="$1"
if [[ ! -d "$dir" ]]; then
    echo "no such directory: $dir" >&2
    exit 1
fi

mkdir -p "$dir/archive"

count=0
for f in "$dir"/*.csv; do
    [[ -e "$f" ]] || continue
    gzip "$f"
    mv "$f.gz" "$dir/archive/"
    count=$((count + 1))
done

echo "archived: $count"
""",
     tests=[
         rx("Проверяется, что аргумент ровно один", r"\$#\"?\s*(-ne|-eq|-lt|-gt|!=|==|<|>)\s*\"?1\b"),
         rx("Проверяется, что каталог существует", r"-d\s+\"\$\{?(dir|1)\}?\""),
         rx("Нет каталога — exit 1", r"\bexit\s+1\b"),
         rx("Создаётся каталог archive", r"mkdir\s+-p\s+\"\$\{?(dir|1)\}?\"?/archive"),
         rx("Цикл по glob с переменной в кавычках", r"for\s+\w+\s+in\s+\"\$\{?(dir|1)\}?/?\"/?\*\.csv"),
         nrx("Нет разбора вывода ls", r"\$\(\s*ls\b|`\s*ls\b"),
         rx("Пропуск, если CSV нет", r"\[\[?\s*-[ef]\s+\"\$\{?\w+\}?\"\s*\]\]?\s*\|\|\s*(continue|break)|!\s+-[ef]\s+\"\$\{?\w+\}?\"[\s\S]{0,60}continue|shopt\s+-s\s+nullglob"),
         rx("gzip получает имя файла в кавычках", r"gzip\s+(-\S+\s+)*\"\$\{?\w+\}?\""),
         rx("Итог «archived: N»", r"echo\s+\"?archived:\s*\$\{?\w+\}?"),
         nrx("Счётчик не роняет скрипт под set -e (без count++)", r"\w+\+\+\s*\)\)|\blet\s+\"?\w+\+\+"),
     ])

task(5, T, "code_review", 3, "teamlead",
     "Марина: «Джун принёс скрипт очистки старых релизов для серверов Маркета. Его будут запускать по cron от root на всех машинах. Отметь, что реально нужно исправить до мерджа».",
     "Выбери все замечания, которые действительно стоит оставить в ревью.",
     """
Самое опасное — пустой аргумент. Без проверки и без set -u переменная RELEASES_DIR пуста, cd без аргумента уводит в домашний каталог root, а rm -rf $RELEASES_DIR/$r превращается в rm -rf /имя. Скрипт начнёт сносить каталоги в корне системы. Второе — нет кавычек и цикл идёт по выводу ls: имя с пробелом развалится на слова, и rm получит не те пути. Третье — логика: после отката симлинк current указывает на старый релиз, который уже не входит в пять самых свежих, и скрипт удалит живую версию. tail -n +6 начинает с шестой строки, то есть оставляет ровно пять релизов, так что off-by-one здесь нет. Шебанг и лишний echo — не проблемы: вывод cron как раз полезен в логе.
""",
     ["Что произойдёт, если cron вызовет скрипт без аргумента?",
      "Посчитай, сколько строк пропустит tail -n +6.",
      "Куда указывает current после отката на прошлую версию?"],
     code="""#!/bin/bash
# cleanup_releases.sh — оставляет 5 последних релизов
# /srv/market/releases/<release>, /srv/market/current -> releases/<release>
RELEASES_DIR=$1
KEEP=5

cd $RELEASES_DIR
for r in $(ls -t | tail -n +$((KEEP + 1))); do
    rm -rf $RELEASES_DIR/$r
    echo "removed $r"
done
echo "done"
""",
     lang="bash",
     options=[
         "Нет проверки аргумента и set -euo pipefail: с пустым RELEASES_DIR cd уйдёт в $HOME, а rm -rf удалит /<имя>",
         "Нет кавычек, и цикл идёт по выводу ls: имя релиза с пробелом развалится на слова, и rm удалит не то",
         "Не учтён симлинк current: после отката живой релиз может оказаться старше пяти и будет удалён",
         "tail -n +$((KEEP + 1)) оставляет шесть релизов, а не пять: off-by-one, нужно +$KEEP",
         "Шебанг лучше заменить на #!/bin/sh: dash стартует быстрее, а bash-фишки тут не нужны",
         "echo \"removed $r\" лишний: вывод cron никто не читает, а лог только забивает почту root",
     ],
     answer=[0, 1, 2])


# =====================================================================
# do-dockerfile
# =====================================================================
T = "do-dockerfile"
topic(T, """
Dockerfile — рецепт образа. Каждая инструкция (RUN, COPY, ADD) создаёт слой, образ — это стопка слоёв только для чтения, а контейнер кладёт сверху тонкий слой для записи.

    FROM python:3.12-slim              # базовый образ, всегда с конкретным тегом
    WORKDIR /app
    COPY requirements.txt .            # файлы из контекста сборки
    RUN pip install --no-cache-dir -r requirements.txt
    COPY . .
    ENV PYTHONUNBUFFERED=1
    EXPOSE 8000                        # документация: порт приложения
    USER app
    CMD ["gunicorn", "app.main:app", "-b", "0.0.0.0:8000"]

• CMD — команда по умолчанию, ENTRYPOINT — «исполняемый файл» образа. Пиши их в exec-форме (JSON-массив): процесс станет PID 1 и получит SIGTERM при docker stop. В shell-форме сигнал достанется /bin/sh, и через 10 секунд контейнер убьют по SIGKILL.
• COPY лучше ADD: ADD сам распаковывает архивы и скачивает URL, а это чаще сюрприз, чем польза.
• В контейнере приложение слушает 0.0.0.0, а не 127.0.0.1, иначе проброс порта не сработает.

Кэш слоёв. Docker пересобирает слой, если изменилась инструкция или скопированные ею файлы, а заодно и все слои после него. Поэтому сначала идёт то, что меняется редко (пакеты, файл зависимостей и их установка), и только потом код. apt-get update и install пишут в одном RUN, с очисткой в том же слое:
    RUN apt-get update \\
     && apt-get install -y --no-install-recommends libpq5 \\
     && rm -rf /var/lib/apt/lists/*
Удаление в следующем RUN место не вернёт: файлы уже остались в предыдущем слое.

Теги. latest — не «последняя стабильная», а тег по умолчанию, который постоянно переезжает: сегодня одна версия, завтра другая, и сборка ломается без единой правки в коде. Фиксируй версию (python:3.12-slim, node:24-alpine), а для полной воспроизводимости — digest (@sha256:…). Обновляй базовый образ осознанно, отдельным PR. Варианты -slim и -alpine в разы меньше полного образа.

Секреты. Всё, что попало в слой, остаётся в образе навсегда: docker history и docker inspect покажут ARG и ENV, а распакованный слой — файл, удалённый следующей инструкцией. Токены для приватных пакетов передают через BuildKit secret. Он монтируется только на время одного RUN и в слой не попадает:
    RUN --mount=type=secret,id=npmrc,target=/root/.npmrc npm ci
    # сборка: docker build --secret id=npmrc,src=$HOME/.npmrc .
Можно отдать секрет и сразу в переменную: --mount=type=secret,id=token,env=TOKEN. Рантайм-секреты (пароль базы) в образ не кладут: их передают при запуске.

.dockerignore. docker build отправляет демону весь каталог — контекст сборки. Без .dockerignore туда попадут .git, node_modules, .venv, логи и .env: сборка тормозит, а COPY . . унесёт секреты в образ.
    .git
    node_modules
    .venv
    .env
    *.log

Multi-stage. Компилятор, dev-зависимости и исходники в рантайме не нужны. Собираем в одной стадии, а в финальную копируем только результат:
    FROM golang:1.27 AS build
    WORKDIR /src
    COPY go.mod go.sum ./
    RUN go mod download
    COPY . .
    RUN CGO_ENABLED=0 go build -o /out/app ./cmd/app

    FROM gcr.io/distroless/static-debian13:nonroot
    COPY --from=build /out/app /app
    ENTRYPOINT ["/app"]

Не root. По умолчанию процесс в контейнере работает от root. Создай пользователя и переключись на него через USER. COPY создаёт файлы от root, как бы ни был указан USER, поэтому каталоги, куда приложение пишет, отдай ему явно: COPY --chown=app:app или chown в RUN до USER. Код пусть остаётся только для чтения.
""")

task(1, T, "find_bug", 1, "qa",
     "Ира: «Ночная сборка образа “Кодзилла Трекера” упала, хотя за день никто ничего не коммитил. Вчера этот же коммит собирался нормально. Прикладываю Dockerfile и кусок лога».",
     "Что сломало сборку и как предотвратить это в будущем?",
     """
Код и requirements.txt не менялись, а digest базового образа сменился: тег latest переехал на новую версию Python. Под неё у старого закреплённого lxml нет готового wheel, pip пытается собрать его из исходников и падает. latest — просто подвижный тег, поэтому сборка «вчера работала, сегодня нет» здесь закономерна. Версию базового образа нужно фиксировать (python:3.12-slim, а для полной воспроизводимости — digest) и обновлять осознанно, отдельным PR с прогоном тестов. Кэш, PyPI, ADD и --no-cache-dir к этой ошибке отношения не имеют.
""",
     ["Сравни digest базового образа во вчерашней и сегодняшней сборке.",
      "Куда указывает тег latest и может ли он меняться сам?"],
     code="""FROM python:latest
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
CMD ["gunicorn", "tracker.wsgi:application", "-b", "0.0.0.0:8000"]

# --- лог ночной сборки ---
# #5 [1/5] FROM docker.io/library/python:latest@sha256:3f1c0e…   (вчера было sha256:9a0e41…)
# #8 [3/5] RUN pip install --no-cache-dir -r requirements.txt
# #8 9.81 Collecting lxml==4.9.3
# #8 10.20   Building wheel for lxml (pyproject.toml): finished with status 'error'
# #8 10.21 error: failed-wheel-build-for-install
# ERROR: failed to solve: process "/bin/sh -c pip install --no-cache-dir -r requirements.txt" did not complete successfully: exit code: 1
""",
     lang="dockerfile",
     options=[
         "latest переехал на новую версию Python, под которую у lxml 4.9.3 нет wheel. Зафиксировать тег или digest образа",
         "Сломался кэш слоёв BuildKit после обновления раннера — выполнить docker builder prune и пересобрать",
         "PyPI ночью отдавал ошибки, и pip не скачал готовый wheel — просто перезапустить сборку утром",
         "COPY не подхватывает изменения requirements.txt из кэша — заменить на ADD, он всегда перечитывает файл",
         "--no-cache-dir не даёт pip собрать wheel из исходников — убрать флаг, чтобы появился кэш сборки",
     ],
     answer=0)

task(2, T, "find_bug", 2, "teamlead",
     "Марина: «Образ API каталога собирается пять минут, из них три — “transferring context”. А ещё безопасники нашли внутри образа наш .env с ключом платёжки. Найди общую причину».",
     "Какая общая причина у медленной сборки и утечки .env, и что сделать?",
     """
docker build отправляет демону весь каталог — это контекст сборки. Без .dockerignore туда уезжают .git, .venv, логи и .env, отсюда почти 2 ГБ и три минуты передачи. Затем COPY . . кладёт всё это в слой образа. .dockerignore с .git, .venv, __pycache__, logs и .env решает обе проблемы. Но ключ уже лежит в образах, которые могли уйти в registry, поэтому его надо ротировать: удалить старые теги недостаточно. RUN rm в конце не поможет — файл останется в предыдущем слое. ADD ничего не фильтрует, --compress лишь слегка ускорит передачу, а COPY после USER всё равно создаёт файлы от root (если не указан --chown) — и любой, кто скачал образ, распакует слой с .env.
""",
     ["Что docker build отправляет демону перед сборкой?",
      "Что именно копирует COPY . .?",
      "Если секрет уже побывал в образе — чего недостаточно, кроме пересборки?"],
     code="""FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
RUN useradd --create-home app
USER app
CMD ["gunicorn", "catalog.main:app", "-k", "uvicorn.workers.UvicornWorker", "-b", "0.0.0.0:8000"]

# $ ls -a
# .  ..  .env  .git  .venv  Dockerfile  catalog  logs  requirements.txt  tests
#
# $ docker build -t catalog-api .
#  => [internal] load build context                                183.4s
#  => => transferring context: 1.94GB                              183.1s
#
# $ docker run --rm catalog-api cat /app/.env
# DATABASE_URL=postgresql://catalog:***@db-1:5432/catalog
# PAYMENT_SECRET_KEY=***
""",
     lang="dockerfile",
     options=[
         "Нет .dockerignore: .git, .venv и .env уходят в контекст, а COPY . . — в образ. Добавить .dockerignore и сменить ключи",
         "COPY . . копирует скрытые файлы, а ADD . . — нет: заменить COPY на ADD, и .env с .git в образ не попадут",
         "Добавить в конец RUN rm -f /app/.env — секрет исчезнет из образа, и безопасники его не найдут",
         "Собирать с docker build --compress: контекст сожмётся в разы, а .env останется только на машине сборки",
         "Перенести COPY . . ниже USER app — файлы скопируются от app, и .env внутри будет не прочитать",
     ],
     answer=0)

task(3, T, "write_code", 2, "devops_colleague",
     "Дима: «КофеБот ставит зависимости из нашего приватного PyPI, и URL с токеном сейчас лежит в ENV — любой, у кого есть доступ к registry, увидит его в docker inspect. Переделай на BuildKit secret».",
     """
Перепиши Dockerfile:
• убери ARG PIP_TOKEN и ENV PIP_INDEX_URL — токена не должно быть ни в инструкциях, ни в метаданных образа;
• в RUN с pip install подключи секрет с id pip_index_url прямо в переменную PIP_INDEX_URL: --mount=type=secret,id=pip_index_url,env=PIP_INDEX_URL;
• сохрани порядок для кэша: requirements.txt и pip install — до COPY . .
(Сборка будет такой: docker build --secret id=pip_index_url,env=PIP_INDEX_URL .)
""",
     """
ARG и ENV попадают в метаданные и историю образа: docker history и docker inspect покажут токен любому, кто может скачать образ. Секрет BuildKit монтируется только на время одного RUN. С env=PIP_INDEX_URL он становится переменной окружения этой команды, и pip подхватывает её как адрес индекса. После RUN секрета нет ни в слое, ни в метаданных. Строка # syntax=docker/dockerfile:1 подключает актуальный синтаксис Dockerfile, в котором есть --mount. Порядок «requirements → pip install → код» сохраняет кэш: при правке кода зависимости не переустанавливаются. Уже утёкший токен после такой правки всё равно нужно перевыпустить.
""",
     ["Удали строки ARG и ENV с токеном целиком.",
      "Флаг --mount пишется сразу после RUN, до самой команды.",
      "RUN --mount=type=secret,id=pip_index_url,env=PIP_INDEX_URL pip install --no-cache-dir -r requirements.txt"],
     code="""# syntax=docker/dockerfile:1
FROM python:3.12-slim
ARG PIP_TOKEN
ENV PIP_INDEX_URL=https://__token__:${PIP_TOKEN}@pypi.kodzilla.dev/simple
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
USER nobody
CMD ["python", "-m", "kofebot"]
""",
     lang="dockerfile",
     answer="""# syntax=docker/dockerfile:1
FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN --mount=type=secret,id=pip_index_url,env=PIP_INDEX_URL \\
    pip install --no-cache-dir -r requirements.txt
COPY . .
USER nobody
CMD ["python", "-m", "kofebot"]
""",
     tests=[
         rx("pip install запускается с secret-mount pip_index_url", r"RUN\s+--mount=(?=\S*type=secret)(?=\S*id=pip_index_url)\S*[^\n]*(\\\s*\n[^\n]*)*pip\s+install"),
         rx("Секрет попадает в переменную PIP_INDEX_URL", r"--mount=\S*env=PIP_INDEX_URL\b"),
         nrx("Нет ARG с токеном", r"^\s*ARG\s+\w*TOKEN"),
         nrx("Токена и индекса нет в ENV", r"^\s*ENV\s+[^\n]*(TOKEN|PIP_INDEX_URL)"),
         rx("Зависимости ставятся до копирования кода", r"COPY\s+requirements\.txt[\s\S]*pip\s+install[\s\S]*COPY\s+\.\s+\."),
     ])

task(4, T, "find_bug", 2, "qa",
     "Ира: «После того как КофеБот перевели с root на отдельного пользователя, сломалась загрузка аватарок. Шаги: открыть профиль → выбрать картинку → “Сохранить” → 500. В логе контейнера Permission denied».",
     "В чём причина и как исправить правильно?",
     """
COPY всегда создаёт файлы от root, если не указан --chown, и USER на это не влияет. Поэтому /app/media принадлежит root:root с правами 755, и пользователь kofebot писать туда не может. Правильно — отдать ему только тот каталог, куда приложение пишет: RUN mkdir -p /app/media && chown -R kofebot:kofebot /app/media до строки USER. Код при этом остаётся read-only, и взломанный процесс не сможет его подменить. Сами загрузки лучше держать в томе или объектном хранилище, иначе они пропадут при пересоздании контейнера. Вернуть root или сделать chmod 777 — значит отказаться от смысла этой задачи. Перенос USER выше COPY владельца файлов не изменит, а VOLUME при создании тома копирует каталог из образа вместе с его владельцем, то есть тем же root.
""",
     ["От какого пользователя COPY создаёт файлы?",
      "Посмотри на владельца /app/media в выводе ls.",
      "Выдай права на запись только туда, куда приложение действительно пишет."],
     code="""FROM python:3.12-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY . .
RUN useradd --system --no-create-home kofebot
USER kofebot
CMD ["gunicorn", "kofebot.web:app", "-b", "0.0.0.0:8000"]

# лог контейнера:
# PermissionError: [Errno 13] Permission denied: '/app/media/avatars/u_1042.png'
#
# $ docker run --rm kofebot ls -ld /app/media/avatars
# drwxr-xr-x 2 root root 4096 Sep 24 10:12 /app/media/avatars
""",
     lang="dockerfile",
     options=[
         "/app/media создан COPY от root. До USER отдать его kofebot: RUN mkdir -p /app/media && chown -R kofebot /app/media",
         "Вернуть запуск от root: USER только ломает запись, а изоляцию и так обеспечивает контейнер",
         "Добавить RUN chmod -R 777 /app перед USER — приложение сможет писать куда угодно",
         "Поставить USER kofebot перед COPY . . — тогда COPY создаст файлы от kofebot, и запись заработает",
         "Добавить VOLUME /app/media: Docker создаст том с владельцем из инструкции USER, и запись заработает",
     ],
     answer=0)

task(5, T, "write_code", 3, "teamlead",
     "Гена: «Образ фронта “Лампового Маркета” весит 1,4 ГБ: внутри node_modules, исходники и dev-сервер Vite, который мы по ошибке гоняем на проде. Сделай по-человечески: собираем в node, отдаём статику через nginx».",
     """
Перепиши Dockerfile в две стадии:
• стадия build (так она и называется) на образе node в варианте alpine или slim с явной версией, например node:24-alpine: сначала копируются package.json и package-lock.json и ставятся зависимости через npm ci, потом копируется код и выполняется npm run build (результат — в /app/dist);
• финальная стадия на nginx в alpine-варианте с явной версией (например, nginx:1.30-alpine) копирует только /app/dist из стадии build в /usr/share/nginx/html;
• никаких latest, FROM без тега и полных образов без -alpine/-slim — задача про размер; никакого dev-сервера.
""",
     """
Multi-stage разделяет сборку и рантайм. Node, node_modules и исходники нужны только в стадии build, а в финальный образ через COPY --from=build попадает лишь папка dist со статикой. Итог — десятки мегабайт вместо 1,4 ГБ, и у атакующего в образе нет ни npm, ни исходников. Урезанные варианты -alpine и -slim сами по себе в разы меньше полных образов на Debian, а версия в теге (node:24-alpine, nginx:1.30-alpine) защищает от внезапных обновлений. npm ci ставит ровно то, что записано в package-lock.json, и падает при расхождении — сборка воспроизводима. Отдельный COPY манифестов перед npm ci кэширует слой с зависимостями, так что правка компонента не переустанавливает пакеты. Dev-сервер Vite — инструмент разработчика: он медленный и небезопасный для прода.
""",
     ["Первая строка: FROM node:24-alpine AS build (подойдёт и другая LTS-версия, но с -alpine или -slim).",
      "Порядок в стадии build: COPY манифестов → RUN npm ci → COPY . . → RUN npm run build.",
      "Вторая стадия: FROM nginx:1.30-alpine и COPY --from=build /app/dist /usr/share/nginx/html."],
     code="""FROM node:latest
WORKDIR /app
COPY . .
RUN npm install
EXPOSE 5173
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
""",
     lang="dockerfile",
     answer="""FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.30-alpine
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
""",
     tests=[
         rx("Стадия build на node с версией в варианте alpine или slim (например, node:24-alpine)",
            r"^FROM\s+(--platform=\S+\s+)?node:\d+(\.\d+){0,2}-(alpine[\d.]*|((bookworm|trixie)-)?slim)(@sha256:[0-9a-f]{64})?\s+AS\s+build\s*$"),
         rx("Сначала манифесты и npm ci, потом код", r"COPY\s+package[^\n]*\n(.*\n)*?RUN\s+npm\s+ci\b[^\n]*\n(.*\n)*?COPY\s+\.\s+\.", "i"),
         rx("Сборка через npm run build", r"RUN\s+npm\s+run\s+build\b"),
         rx("Финальная стадия на nginx с версией в alpine-варианте (например, nginx:1.30-alpine)",
            r"^FROM\s+(--platform=\S+\s+)?(nginx|nginxinc/nginx-unprivileged):\d+(\.\d+){0,2}-alpine[\d.]*(-slim)?(@sha256:[0-9a-f]{64})?\s*$"),
         rx("В финал копируется только dist из стадии build", r"COPY\s+--from=build\s+/app/dist/?\s+/usr/share/nginx/html/?"),
         nrx("Нет latest и FROM без тега", r":latest\b|^FROM\s+[^\s:@]+(\s+AS\s+\w+)?\s*$"),
         nrx("Нет dev-сервера", r"run\s*\"?,?\s*\"?dev\b"),
     ])


# =====================================================================
data = {"track": "devops", "part": "d1", "topics": TOPICS, "tasks": TASKS}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)

if __name__ == "__main__":
    from collections import Counter
    print("tasks:", len(TASKS))
    for tp in TOPICS:
        print(tp["topic_id"], "theory", len(tp["theory"]), "tasks",
              [(t["type"], t["difficulty"]) for t in TASKS if t["topic_id"] == tp["topic_id"]])
    print(Counter(t["type"] for t in TASKS))
