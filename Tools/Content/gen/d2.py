#!/usr/bin/env python3
"""Генератор контента «Стажёра»: track devops, part d2 (junior_plus).
Темы: do-compose, do-ci, do-nginx, do-cloud.
Результат: Tools/Content/out/devops_d2.json
"""
import json
import os
import random

OUT = "Tools/Content/out/devops_d2.json"

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}
GRADE = "junior_plus"


def xp(d, g=GRADE):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


def task(tid, typ, d, character, story, question, explanation, hints,
         code=None, language=None, options=None, answer=None, tests=None,
         time_limit=None, entry=None):
    topic = tid.rsplit("-", 1)[0]
    if options is not None:
        # Детерминированно перемешиваем варианты, чтобы верный ответ не стоял всегда на одном месте
        perm = list(range(len(options)))
        random.Random("d2:" + tid).shuffle(perm)
        options = [options[i] for i in perm]
        if isinstance(answer, list):
            answer = sorted(perm.index(i) for i in answer)
        else:
            answer = perm.index(answer)
    content = {
        "question": question,
        "code": code,
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content.update({
        "options": options,
        "correct_answer": answer,
        "test_cases": tests,
    })
    return {
        "task_id": tid,
        "topic_id": topic,
        "grade": GRADE,
        "type": typ,
        "difficulty": d,
        "xp_reward": xp(d),
        "time_limit_minutes": time_limit,
        "character": character,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": hints,
    }


def rx(desc, pattern, flags="im"):
    return {"input": desc, "expected": {"regex": pattern, "flags": flags}}


def nrx(desc, pattern, flags="im"):
    return {"input": desc, "expected": {"not_regex": pattern, "flags": flags}}


TOPICS = []
TASKS = []

# =====================================================================
# do-compose
# =====================================================================
TOPICS.append({"topic_id": "do-compose", "theory": """\
Docker Compose запускает несколько связанных контейнеров одной командой: приложение, базу, Redis, воркер. Всё описано в файле compose.yaml (старое имя docker-compose.yml тоже читается). Команда — docker compose через пробел (Compose v2); строка version: в начале файла устарела и только вызывает предупреждение.

    services:
      api:
        build: .
        ports:
          - "8000:8000"
        environment:
          DATABASE_URL: postgresql://app:${POSTGRES_PASSWORD}@db:5432/app
        depends_on:
          db:
            condition: service_healthy
      db:
        image: postgres:17
        environment:
          POSTGRES_USER: app
          POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
          POSTGRES_DB: app
        volumes:
          - pgdata:/var/lib/postgresql/data
        healthcheck:
          test: ["CMD-SHELL", "pg_isready -U app -d app"]
          interval: 5s
          retries: 10
    volumes:
      pgdata:

Сети. Compose создаёт сеть <проект>_default, и сервисы в ней находят друг друга по имени через DNS Docker: api ходит в базу по адресу db:5432. localhost внутри контейнера — это сам контейнер, а не твой ноутбук и не соседний сервис. Контейнеры из разных compose-проектов друг друга не видят: нужна общая сеть — её создаёт один проект (или docker network create), а в другом compose.yaml объявляют с external: true и подключают к сервису через networks.

Порты. ports публикует порт на хосте: "8000:8000" — это хост:контейнер. По умолчанию порт открыт на всех интерфейсах (0.0.0.0), и Docker пробивает его в обход ufw. Базе в проде ports не нужен — приложение ходит к ней по внутренней сети; для доступа с самого сервера пиши "127.0.0.1:5432:5432".

Тома.
• Именованный том (pgdata:/var/lib/postgresql/data) живёт отдельно от контейнера и переживает его пересоздание. Объявляется в секции volumes: верхнего уровня.
• Bind mount (./src:/app/src) — папка с хоста, удобно в разработке.
• docker compose down удаляет контейнеры и сеть, а down -v — ещё и тома, то есть данные базы.
• В образах postgres:18+ данные лежат в /var/lib/postgresql/18/docker, и том монтируют в /var/lib/postgresql. Старый путь /var/lib/postgresql/data там не работает: данные молча уедут в анонимный том и пропадут при пересоздании контейнера. Для postgres:17 и старше — /var/lib/postgresql/data.

depends_on и healthcheck. Короткая форма depends_on: [db] задаёт только порядок старта: api запустится, как только создан контейнер db, хотя Postgres внутри ещё несколько секунд инициализируется. Итог — connection refused при первом запуске. Правильно: healthcheck у базы плюс condition: service_healthy. Для одноразовых задач вроде миграций есть service_completed_successfully. И всё равно приложение должно уметь переподключаться.

Переменные. environment задаёт их прямо в файле, env_file подкладывает из файла в контейнер, а ${VAR} подставляется при чтении compose.yaml из окружения или файла .env рядом. Пароли — в .env из .gitignore, в репозиторий — .env.example.

YAML. Отступы — только пробелы, и уровень отступа определяет смысл. services, volumes, networks — ключи верхнего уровня, без отступа. Съехавший на два пробела volumes: превращается в сервис с именем volumes. Итоговую конфигурацию проверяет docker compose config.

Каждый день:
    docker compose up -d --build
    docker compose ps
    docker compose logs -f api
    docker compose exec db psql -U app
    docker compose down

Для прода: restart: unless-stopped и конкретные теги образов вместо latest."""})

TASKS.append(task(
    "do-compose-01", "write_code", 2, "teamlead",
    "Гена: «Для сайта пекарни Батон новый разработчик полдня ставил Postgres руками и так и не завёл. "
    "Давай сделаем compose.yaml: одна команда — и бэкенд с базой запущены».",
    "Допиши compose.yaml для локальной разработки: сервис web (собирается из текущей папки) публикует порт 8000 "
    "и получает DATABASE_URL=postgresql://baton:baton@db:5432/baton; сервис db на образе postgres с конкретной версией "
    "(например, postgres:17) с пользователем, паролем и базой baton; данные базы — в именованном томе pgdata, смонтированном "
    "в папку данных образа (для postgres:17 — /var/lib/postgresql/data, для 18+ — /var/lib/postgresql).",
    "Сервисы одного compose-проекта сидят в общей сети и находят друг друга по имени, поэтому в DATABASE_URL хост — db, "
    "а не localhost. Образ с конкретной версией (postgres:17) не перескочит сам на несовместимую мажорную версию, как это "
    "бывает с latest. Переменные POSTGRES_USER, POSTGRES_PASSWORD и POSTGRES_DB официальный образ читает при первом запуске "
    "и создаёт пользователя и базу. Именованный том pgdata хранит данные вне контейнера: пересоздал контейнер — база на месте; "
    "объявить том нужно в секции volumes верхнего уровня. Пароль baton в файле допустим только локально — в проде он уезжает в .env или секреты.",
    ["Хост базы для web — это имя сервиса из compose.yaml.",
     "Тому нужны две записи: в volumes сервиса db и в секции volumes: верхнего уровня.",
     "В db: volumes: - pgdata:/var/lib/postgresql/data, а в конце файла без отступа volumes: и под ним pgdata:"],
    code="""\
services:
  web:
    build: .
    # опубликуй порт 8000 и передай DATABASE_URL

  # добавь сервис db
""",
    language="yaml",
    answer="""\
services:
  web:
    build: .
    ports:
      - "8000:8000"
    environment:
      DATABASE_URL: postgresql://baton:baton@db:5432/baton

  db:
    image: postgres:17
    environment:
      POSTGRES_USER: baton
      POSTGRES_PASSWORD: baton
      POSTGRES_DB: baton
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
""",
    tests=[
        rx("web публикует порт 8000", r"['\"]?(127\.0\.0\.1:)?8000:8000\b"),
        rx("DATABASE_URL ведёт на сервис db, а не на localhost",
           r"DATABASE_URL\s*[:=]\s*['\"]?postgres(ql)?(\+\w+)?://[^@\s]+@db:5432/baton"),
        rx("db на образе postgres с конкретной версией", r"image:\s*['\"]?postgres:\d+"),
        rx("Задан пароль базы", r"POSTGRES_PASSWORD\s*[:=]\s*['\"]?\S+"),
        rx("Том pgdata смонтирован в папку данных Postgres", r"-\s*['\"]?pgdata:/var/lib/postgresql(/data)?/?['\"]?\s*$"),
        nrx("Для postgres:17 и старше том монтируется именно в /var/lib/postgresql/data",
            r"(?=[\s\S]*image:\s*['\"]?postgres:(1[0-7]|[1-9])(\D|$))[\s\S]*pgdata:/var/lib/postgresql/?['\"]?\s*$"),
        rx("Том pgdata объявлен в секции volumes верхнего уровня", r"^volumes:\s*\n\s+pgdata:"),
        nrx("Для postgres:18+ путь /var/lib/postgresql/data не подходит (там том монтируют в /var/lib/postgresql)",
            r"(?=[\s\S]*image:\s*['\"]?postgres:(1[89]|[2-9]\d))[\s\S]*pgdata:/var/lib/postgresql/data"),
    ],
))

TASKS.append(task(
    "do-compose-02", "find_bug", 2, "qa",
    "Ира: «Скопировала compose.yaml Маркета из вики, чтобы поднять стенд для тестов. docker compose up сразу падает: "
    "ругается на сервис с именем volumes и лишнее свойство pgdata. Сервиса volumes у нас вроде нет…»",
    "Почему compose не запускается и как исправить файл?",
    "В YAML вложенность задаётся отступами. Блок volumes: в конце файла стоит на два пробела правее края, поэтому стал ключом "
    "внутри services — compose видит сервис с именем volumes и незнакомым полем pgdata, а db ссылается на том, который нигде "
    "не объявлен. Секция volumes: должна начинаться без отступа, на одном уровне с services:. Пустое значение pgdata: — "
    "нормальный YAML (null), compose создаст том с настройками по умолчанию. Строка version устарела и ни на что не влияет, "
    "а именованный том — как раз рекомендуемый способ хранить данные базы. Такие ошибки заранее ловит docker compose config.",
    ["Посмотри, на каком уровне отступа стоит каждая секция верхнего уровня.",
     "Сколько пробелов перед volumes: в конце файла и сколько перед services:?"],
    code="""\
services:
  api:
    build: .
    ports:
      - "8000:8000"
    environment:
      DATABASE_URL: postgresql://market:market@db:5432/market

  db:
    image: postgres:17
    environment:
      POSTGRES_USER: market
      POSTGRES_PASSWORD: market
      POSTGRES_DB: market
    volumes:
      - pgdata:/var/lib/postgresql/data

  volumes:
    pgdata:
""",
    language="yaml",
    options=[
        "volumes: съехал на два пробела и стал «сервисом» внутри services — сдвинуть его влево, на уровень services:",
        "Не хватает строки version: \"3.8\": без неё compose не знает секцию volumes верхнего уровня",
        "Пустое значение pgdata: — невалидный YAML, compose ждёт хотя бы pgdata: {} или driver: local",
        "Именованные тома создают только через docker volume create, а в файле нужен bind mount ./pgdata",
        "У db не хватает depends_on: [volumes] — сервис стартует раньше, чем создан том с данными",
    ],
    answer=0,
))

TASKS.append(task(
    "do-compose-03", "find_bug", 3, "qa",
    "Ира: «Баг со стенда: на чистой машине после docker compose down -v и docker compose up сервис api падает с "
    "connection refused на db:5432 прямо во время миграций. Если запустить второй раз — всё работает. Воспроизводится стабильно».",
    "В чём причина и какое исправление правильное?",
    "Короткая форма depends_on гарантирует лишь порядок: контейнер api стартует после того, как запущен контейнер db. "
    "Но на пустом томе Postgres сначала инициализирует кластер и создаёт пользователя — несколько секунд он не принимает "
    "соединения, и миграции в этот момент получают connection refused. Healthcheck с pg_isready сообщает Docker, когда база "
    "реально готова, а condition: service_healthy заставляет api ждать именно этого. sleep — гадание: на медленной машине "
    "15 секунд не хватит, на быстрой это лишнее ожидание. restart: always маскирует проблему, а порядок сервисов в файле ни на что не влияет.",
    ["Чем «контейнер запущен» отличается от «база готова принимать соединения»?",
     "У depends_on есть длинная форма с условием.",
     "healthcheck с test: [\"CMD-SHELL\", \"pg_isready -U market -d market\"] у db и condition: service_healthy у api."],
    code="""\
services:
  api:
    build: .
    command: sh -c "alembic upgrade head && uvicorn app.main:app --host 0.0.0.0 --port 8000"
    environment:
      DATABASE_URL: postgresql://market:${POSTGRES_PASSWORD}@db:5432/market
    depends_on:
      - db

  db:
    image: postgres:17
    environment:
      POSTGRES_USER: market
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: market
    volumes:
      - pgdata:/var/lib/postgresql/data

volumes:
  pgdata:
""",
    language="yaml",
    options=[
        "depends_on ждёт только старта контейнера db. Нужен healthcheck с pg_isready у db и condition: service_healthy у api",
        "Добавить в command api sleep 15 перед миграциями — Postgres на пустом томе стартует около 10 секунд",
        "Compose стартует сервисы сверху вниз: перенести db выше api, и база будет готова к миграциям",
        "Заменить в DATABASE_URL хост db на localhost: контейнеры на одной машине, а DNS Docker отвечает медленно",
        "Поставить api restart: on-failure — вторая попытка проходит, так что Compose сам доведёт запуск до конца",
    ],
    answer=0,
))

TASKS.append(task(
    "do-compose-04", "incident", 3, "devops_colleague",
    "Дима: «Я перенёс Postgres в общий compose-проект infra, Трекер переехал туда же и работает. "
    "А КофеБот живёт в своём проекте и крутится в рестартах — не видит базу. Офис без кофе, глянь, пожалуйста».",
    "Почему api КофеБота не видит базу и как это правильно исправить?",
    "could not translate host name — это не отказ базы, а DNS: имя postgres не находится. Compose создаёт отдельную сеть на "
    "каждый проект, и встроенный DNS Docker знает только контейнеры из той же сети: api живёт в coffeebot_default, а база — "
    "в infra_default. Решение — подключить api к сети базы: в compose КофеБота объявить networks: infra: external: true, "
    "name: infra_default и добавить её сервису api. Публикация порта с localhost не поможет: localhost внутри контейнера — "
    "это сам контейнер, а база к тому же окажется открыта наружу. IP контейнера меняется при каждом пересоздании, "
    "а неверный пароль выглядел бы как password authentication failed.",
    ["Ошибка про пароль, про порт или про имя хоста?",
     "Сравни колонку NETWORKS у двух контейнеров."],
    code="""\
$ docker compose logs api --tail 2
api-1  | sqlalchemy.exc.OperationalError: (psycopg2.OperationalError) could not translate host name "postgres" to address: Name or service not known
api-1  | ERROR:    Application startup failed. Exiting.

$ docker ps --format 'table {{.Names}}\\t{{.Status}}\\t{{.Networks}}'
NAMES               STATUS                          NETWORKS
coffeebot-api-1     Restarting (3) 4 seconds ago    coffeebot_default
infra-postgres-1    Up 2 hours (healthy)            infra_default
infra-tracker-1     Up 2 hours                      infra_default

$ grep DATABASE_URL compose.yaml
      DATABASE_URL: postgresql://coffee:${COFFEE_DB_PASSWORD}@postgres:5432/coffee

$ docker network ls
NETWORK ID     NAME                DRIVER    SCOPE
3f1c0d9a2b11   bridge              bridge    local
8a7e44c1d0f2   coffeebot_default   bridge    local
c52b9e7f3a10   infra_default       bridge    local
""",
    language="text",
    options=[
        "Postgres не готов принимать соединения — добавить healthcheck и condition: service_healthy в compose КофеБота",
        "Контейнеры в разных сетях: имя postgres резолвится только в infra_default. Подключить api к ней как к external-сети",
        "Опубликовать у postgres порт 5432:5432 и указать в DATABASE_URL localhost:5432 — порт хоста виден всем",
        "Взять IP контейнера postgres из docker inspect и прописать его в DATABASE_URL вместо имени хоста",
        "После переезда базы не совпадает пароль COFFEE_DB_PASSWORD — сменить пароль пользователя coffee",
    ],
    answer=1,
    time_limit=15,
))

TASKS.append(task(
    "do-compose-05", "code_review", 4, "teamlead",
    "Марина: «Джун подготовил compose.yaml, с которым сайт Батона поедет на VPS в прод. Локально всё работает. "
    "Отметь, что нужно поправить до выкладки».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Настоящих проблем четыре. Пароль в файле попадает в историю git и ко всем, у кого есть доступ к репозиторию. "
    "Опубликованный 5432 открывает базу всему интернету, причём Docker добавляет свои правила iptables раньше ufw, так что "
    "файрвол не спасёт. latest делает выкладку невоспроизводимой и лишает быстрого отката. Без именованного тома данные "
    "лежат в анонимном томе, и после down и up база стартует пустой — для прода это катастрофа. Остальное неверно: version "
    "устарел и ни на что не влияет, unless-stopped как раз нужен в проде, healthcheck необходим для service_healthy, "
    "а маппинг 80:8000 — обычная практика: порт на хосте открывает демон Docker, а приложение в контейнере слушает свой 8000.",
    ["Представь, что репозиторий утёк, а сервер просканировали снаружи.",
     "Что будет с базой после docker compose down и up?"],
    code="""\
services:
  web:
    image: registry.kodzilla.ru/baton/site:latest
    restart: unless-stopped
    ports:
      - "80:8000"
    environment:
      DATABASE_URL: postgresql://baton:Baton2026!@db:5432/baton
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:17
    restart: unless-stopped
    ports:
      - "5432:5432"
    environment:
      POSTGRES_USER: baton
      POSTGRES_PASSWORD: Baton2026!
      POSTGRES_DB: baton
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U baton -d baton"]
      interval: 5s
      timeout: 3s
      retries: 10
""",
    language="yaml",
    options=[
        "Пароль базы лежит в файле из git — вынести в .env из .gitignore и подставлять через ${POSTGRES_PASSWORD}",
        "Нужна строка version: \"3.9\" — без неё Compose игнорирует condition: service_healthy в depends_on",
        "5432 у db опубликован на всех интерфейсах, а Docker обходит ufw — убрать ports или привязать к 127.0.0.1",
        "restart: unless-stopped опасен: после ребута сервера упавший контейнер поднимется сам, лучше restart: \"no\"",
        "Образ web с тегом latest: непонятно, что в проде, и откатиться не на что — нужен тег версии или SHA",
        "healthcheck у базы лишний: depends_on с condition и так ждёт, пока Postgres начнёт принимать соединения",
        "У db нет тома для данных: после down и up база поднимется пустой — нужен именованный том",
        "Маппинг 80:8000 не сработает: контейнер без root не может занять на хосте порт ниже 1024",
    ],
    answer=[0, 2, 4, 6],
))

# =====================================================================
# do-ci
# =====================================================================
TOPICS.append({"topic_id": "do-ci", "theory": """\
CI (continuous integration) — на каждый push и pull request сервер сам собирает проект и гоняет проверки: линтер, тесты, сборку образа. CD (continuous delivery/deployment) — проверенная сборка сама уезжает на стенд или в прод. Пайплайн описывается YAML-файлом в репозитории: в GitHub Actions это .github/workflows/*.yml, в GitLab CI — .gitlab-ci.yml в корне.

Устройство. Пайплайн состоит из стадий (stages), стадии — из джоб, джобы — из шагов. Джобы одной стадии идут параллельно, стадии — по очереди; упала джоба — дальше не идём. Каждая джоба стартует в чистом контейнере или ВМ раннера: там нет ничего с твоего ноутбука и из прошлых запусков. Отсюда «у меня работает, а в CI падает» — пакет, поставленный руками, но не записанный в requirements.txt.

GitHub Actions:
    on:
      push:
        branches: [main]
      pull_request:
    permissions:
      contents: read
    jobs:
      test:
        runs-on: ubuntu-latest
        steps:
          - uses: actions/checkout@v7
          - uses: actions/setup-python@v7
            with:
              python-version: "3.12"
              cache: pip
          - run: pip install -r requirements.txt
          - run: pytest -q

GitLab CI:
    stages: [test, build]
    test:
      stage: test
      image: python:3.12-slim
      script:
        - pip install -r requirements.txt
        - pytest -q
    build:
      stage: build
      image: docker:29
      services: [docker:29-dind]
      rules:
        - if: $CI_COMMIT_TAG
      script:
        - docker build -t "$CI_REGISTRY_IMAGE:$CI_COMMIT_TAG" .

Кэш и артефакты — разные вещи.
• Кэш ускоряет сборку (пакеты pip, npm) и может пропасть в любой момент. Ключ — по файлу зависимостей. GitLab кэширует только папки внутри проекта, поэтому pip направляют в $CI_PROJECT_DIR/.cache/pip.
• Артефакт — результат джобы, нужный следующим джобам или людям: собранный фронт, JUnit-отчёт тестов. Задавай срок хранения (expire_in).

Секреты.
• Никогда не пиши токены и пароли в YAML: файл видят все, у кого есть доступ к репозиторию, а история git хранит всё. GitHub: Settings → Secrets и ${{ secrets.NAME }} в файле. GitLab: CI/CD Variables с флагами Masked (скрыть в логе) и Protected (только для защищённых веток и тегов).
• Пароль в docker login — через --password-stdin, а не -p.
• Секрет попал в git — сначала перевыпусти его, потом чисти историю.
• Урезай права: permissions для GITHUB_TOKEN, для облака — OIDC с короткоживущими токенами вместо вечных ключей.

Деплой по тегу. Релиз — это тег v1.4.0: в GitHub on: push: tags: ["v*"], в GitLab rules: - if: $CI_COMMIT_TAG. Образ тегируй версией или SHA коммита, а не latest — видно, что крутится в проде, и есть куда откатиться. Для прода добавляют ручное подтверждение (environments с reviewers, when: manual). Если переменные Protected, релизные теги тоже защищают (Protected tags), иначе в пайплайне тега переменные пусты.

Упавший пайплайн: открой первую красную джобу и ищи первую ошибку в логе — строка ERROR: Job failed лишь итог. Воспроизведи шаг в том же образе: docker run --rm -it python:3.12-slim bash."""})

TASKS.append(task(
    "do-ci-01", "write_code", 2, "teamlead",
    "Гена: «В репозитории КофеБота есть workflow, но он красный с первого дня: pip не находит requirements.txt. "
    "Почини и заодно сделай, чтобы тесты шли на каждый PR — хватит мерджить непроверенное».",
    "Перепиши .github/workflows/tests.yml: запуск на push в main и на любой pull_request; первым шагом checkout репозитория; "
    "Python 3.12 через actions/setup-python с кэшем pip; затем установка зависимостей и pytest. "
    "Ограничь права GITHUB_TOKEN до contents: read.",
    "Раннер стартует с пустой рабочей папкой — пока actions/checkout не скачал репозиторий, requirements.txt просто нет, "
    "отсюда и ошибка. setup-python ставит нужную версию Python, а cache: pip сохраняет скачанные пакеты между запусками "
    "с ключом по файлу зависимостей — установка ускоряется в разы. Триггер pull_request проверяет код до мерджа, а push "
    "только в main не гоняет пайплайн дважды для каждой ветки с PR. Блок permissions урезает права автоматического токена "
    "до чтения: если сторонний action окажется вредоносным, он не сможет пушить в репозиторий.",
    ["Откуда на чистом раннере возьмутся файлы репозитория?",
     "Перед установкой зависимостей нужны два шага с uses.",
     "on: push: branches: [main] и pull_request:, а на верхнем уровне permissions: contents: read."],
    code="""\
name: tests

on: push

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - run: pip install -r requirements.txt
      - run: pytest -q
""",
    language="yaml",
    answer="""\
name: tests

on:
  push:
    branches: [main]
  pull_request:

permissions:
  contents: read

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-python@v7
        with:
          python-version: "3.12"
          cache: pip
      - run: pip install -r requirements.txt
      - run: pytest -q
""",
    tests=[
        rx("Репозиторий забирается через actions/checkout", r"uses:\s*['\"]?actions/checkout@v\d+"),
        rx("Python 3.12 через actions/setup-python",
           r"uses:\s*['\"]?actions/setup-python@v\d+[\s\S]*?python-version:\s*['\"]?3\.12\b"),
        rx("Включён кэш pip", r"cache:\s*['\"]?pip\b"),
        rx("Запуск на pull request", r"^\s*pull_request\s*:|on:\s*\[[^\]]*pull_request"),
        rx("push только в main", r"push:\s*\n\s+branches:\s*(\[\s*['\"]?main\b|\n\s+-\s*['\"]?main\b)"),
        rx("checkout идёт раньше установки зависимостей", r"actions/checkout[\s\S]*pip\s+install"),
        rx("Права токена — только чтение", r"permissions:\s*\n\s+contents:\s*read"),
    ],
))

TASKS.append(task(
    "do-ci-02", "find_bug", 2, "teamlead",
    "Марина: «PR с автодеплоем сайта Батона: пайплайн зелёный, образ пушится, всё работает. "
    "Но мерджить я это не дам. Найди, что не так».",
    "Что не так в workflow и как это правильно исправить?",
    "Всё, что лежит в YAML, лежит в репозитории: файл видят все, у кого есть доступ, и он навсегда остаётся в истории git, "
    "клонах и форках. --password-stdin защищает только от попадания пароля в список процессов, но не от того, что он записан "
    "в файле. Правильно — хранить токен в Secrets и подставлять через ${{ secrets.REGISTRY_TOKEN }}: GitHub маскирует его "
    "в логах и не отдаёт пайплайнам из форков. Раз токен уже побывал в коммите, он скомпрометирован — его отзывают и "
    "выпускают новый. base64 — это кодировка, а не шифрование, а приватный репозиторий всё равно видят все сотрудники и интеграции.",
    ["Кто может прочитать этот файл — сейчас и через год?",
     "Удалить строку из файла недостаточно: где ещё остался токен?"],
    code="""\
name: deploy

on:
  push:
    tags: ["v*"]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - name: Build and push image
        env:
          REGISTRY_TOKEN: kz_reg_7Hq2mV9xLpR4sT1wZ8yB3nK6
        run: |
          echo "$REGISTRY_TOKEN" | docker login registry.kodzilla.ru -u ci-bot --password-stdin
          docker build -t registry.kodzilla.ru/baton/site:${{ github.ref_name }} .
          docker push registry.kodzilla.ru/baton/site:${{ github.ref_name }}
""",
    language="yaml",
    options=[
        "Всё в порядке: токен передаётся через --password-stdin и не попадает ни в лог, ни в список процессов",
        "Токен лежит в репозитории открытым текстом: перенести в ${{ secrets.REGISTRY_TOKEN }} и перевыпустить",
        "Закодировать токен в base64 и декодировать в шаге run — тогда в файле его нельзя будет прочитать",
        "Сделать репозиторий приватным: тогда файл workflow увидят только сотрудники с доступом",
        "Заменить env: на with: — значения из with GitHub автоматически маскирует как секреты",
    ],
    answer=1,
))

TASKS.append(task(
    "do-ci-03", "estimation", 3, "manager",
    "Стас: «Нина из пекарни Батон устала ждать, пока Дима вручную обновит сайт. Хочет, чтобы после мерджа всё "
    "выкатывалось само. Сколько тебе нужно дней? Мне сегодня надо ей ответить».",
    "Прежде чем называть срок на CI/CD для сайта Батона, выбери вопросы, которые обязательно нужно выяснить.",
    "От этих ответов объём работы меняется в разы. Текущий процесс деплоя и доступы определяют, что именно автоматизировать "
    "и где хранить ключи. Если тестов нет, пайплайн будет «зелёным» на любом коде — писать их отдельная задача. Стенд и ручное "
    "подтверждение добавляют окружение и настройку. Миграции и откат — самая рискованная часть: автодеплой без плана отката "
    "хуже ручного. Разумная оценка: тесты + деплой по SSH на один сервер — 1–2 дня, стейдж с подтверждением — ещё день, "
    "миграции и откат — ещё 1–2 дня, тесты оцениваются отдельно. Бейдж и редактор на срок не влияют, а Kubernetes для сайта пекарни — раздувание задачи.",
    ["Какие ответы изменят объём работы в разы?",
     "Что будет, если автоматический деплой сломает прод?"],
    options=[
        "Как сейчас выкатывается сайт: какой сервер, какие ручные шаги и доступы?",
        "Какого цвета и формы будет бейдж статуса сборки в README репозитория?",
        "Есть ли автотесты, которые можно гонять перед деплоем, или их придётся писать?",
        "Выкатываем сразу в прод или нужен тестовый стенд и ручное подтверждение?",
        "Может, заодно переведём сайт на Kubernetes, раз уж всё равно делаем CI?",
        "Меняется ли схема базы при релизах и как откатывать неудачную выкладку?",
        "В каком редакторе удобнее писать YAML пайплайна — VS Code или JetBrains?",
    ],
    answer=[0, 2, 3, 5],
    time_limit=12,
))

TASKS.append(task(
    "do-ci-04", "incident", 3, "devops_colleague",
    "Дима: «В проде Маркета баг с оплатой, хотфикс уже в main, я поставил тег v2.3.1 — а джоба сборки по тегу упала. "
    "Вчера на main эта же джоба прошла. Помоги разобраться, горит».",
    "Почему упала джоба и что нужно сделать?",
    "docker login жалуется, что --username не задан: -u получил пустую строку, значит, в джобе переменная DEPLOY_REGISTRY_USER не определена. Обе переменные "
    "помечены Protected: GitLab отдаёт их только пайплайнам защищённых веток и тегов. main защищена, поэтому вчера всё работало, "
    "а тег v2.3.1 — нет. Правильное исправление — защитить релизные теги шаблоном v* (заодно ставить их смогут только мейнтейнеры) "
    "и перезапустить пайплайн. Снять Protected — значит отдать ключи от реестра любой ветке, где кто угодно отправит их наружу "
    "одной строкой в скрипте. Протухший токен дал бы unauthorized от реестра, без демона Docker была бы ошибка Cannot connect to the Docker daemon, а Masked влияет только на вывод в лог и stdin не мешает.",
    ["Какого значения не хватает docker login судя по сообщению об ошибке?",
     "Чем пайплайн ветки main отличается от пайплайна тега?",
     "Protected-переменные доступны только защищённым веткам и тегам."],
    code="""\
Pipeline #48213 · tag v2.3.1 · job build (stage: build)

Running with gitlab-runner 18.2.1
  on kz-shared-runner-2 J8xQ3p1z
Preparing the "docker" executor
Using docker image docker:29 ...
Getting source from Git repository
Checking out 5d1e9a07 as detached HEAD (ref is v2.3.1)...
Executing "step_script" stage of the job script
$ echo "$DEPLOY_REGISTRY_PASSWORD" | docker login registry.kodzilla.ru -u "$DEPLOY_REGISTRY_USER" --password-stdin
the --password-stdin option requires --username to be set
Cleaning up project directory and file based variables
ERROR: Job failed: exit code 1

--- Settings → CI/CD → Variables
KEY                          VISIBILITY   FLAGS
DEPLOY_REGISTRY_USER         Visible      Protected
DEPLOY_REGISTRY_PASSWORD     Masked       Protected

--- Settings → Repository
Protected branches:  main
Protected tags:      (нет)
""",
    language="text",
    options=[
        "Токен реестра протух после ротации — перевыпустить DEPLOY_REGISTRY_PASSWORD и перезапустить джобу",
        "Protected-переменные не попадают в пайплайн незащищённого тега v2.3.1 — добавить v* в Protected tags",
        "Снять с переменных флаг Protected — тогда они будут доступны пайплайну любого тега и любой ветки",
        "Джоба не подняла сервис docker:29-dind, и docker login не достучался до демона — добавить services",
        "Masked-переменные нельзя читать из stdin — передавать пароль через -p \"$DEPLOY_REGISTRY_PASSWORD\"",
    ],
    answer=1,
    time_limit=15,
))

TASKS.append(task(
    "do-ci-05", "write_code", 4, "devops_colleague",
    "Дима: «Маркет переехал в GitLab. Тесты бегут, но медленно — pip каждый раз качает всё заново. "
    "А образ для релиза я до сих пор собираю руками на ноутбуке. Допиши .gitlab-ci.yml, чтобы релиз собирался по тегу».",
    "Доработай .gitlab-ci.yml: (1) кэш pip в .cache/pip внутри проекта (переменная PIP_CACHE_DIR) с ключом по requirements.txt; "
    "(2) pytest пишет JUnit-отчёт report.xml, он сохраняется в artifacts:reports:junit; (3) новая стадия build и джоба build, "
    "которая запускается только для тегов (rules с $CI_COMMIT_TAG), логинится во встроенный реестр GitLab через --password-stdin "
    "с $CI_REGISTRY_USER и $CI_REGISTRY_PASSWORD, собирает и пушит образ $CI_REGISTRY_IMAGE:$CI_COMMIT_TAG.",
    "GitLab кэширует только пути внутри папки проекта, поэтому pip направляют в $CI_PROJECT_DIR/.cache/pip, а ключ по "
    "requirements.txt сбрасывает кэш ровно тогда, когда меняются зависимости. JUnit-отчёт GitLab показывает прямо в merge request — "
    "видно, какой тест упал, без чтения лога; when: always сохраняет его и при красных тестах. rules с $CI_COMMIT_TAG запускают "
    "сборку только для тегов, а тег образа совпадает с версией релиза — понятно, что крутится в проде, и есть куда откатиться. "
    "$CI_REGISTRY_PASSWORD — временный пароль, который живёт, пока идёт джоба, хранить его в настройках не нужно; "
    "--password-stdin не светит его в командной строке. Для сборки образа внутри контейнера нужен сервис docker:dind.",
    ["Кэш: cache → key → files и paths, плюс variables с PIP_CACHE_DIR.",
     "Для сборки образа нужны image: docker:29 и services: docker:29-dind.",
     "rules: - if: $CI_COMMIT_TAG; логин — echo \"$CI_REGISTRY_PASSWORD\" | docker login \"$CI_REGISTRY\" -u \"$CI_REGISTRY_USER\" --password-stdin"],
    code="""\
stages:
  - test

test:
  stage: test
  image: python:3.12-slim
  script:
    - pip install -r requirements.txt
    - pytest -q
""",
    language="yaml",
    answer="""\
stages:
  - test
  - build

variables:
  PIP_CACHE_DIR: "$CI_PROJECT_DIR/.cache/pip"

test:
  stage: test
  image: python:3.12-slim
  cache:
    key:
      files:
        - requirements.txt
    paths:
      - .cache/pip
  script:
    - pip install -r requirements.txt
    - pytest -q --junitxml=report.xml
  artifacts:
    when: always
    expire_in: 1 week
    reports:
      junit: report.xml

build:
  stage: build
  image: docker:29
  services:
    - docker:29-dind
  variables:
    DOCKER_TLS_CERTDIR: "/certs"
  rules:
    - if: $CI_COMMIT_TAG
  script:
    - echo "$CI_REGISTRY_PASSWORD" | docker login "$CI_REGISTRY" -u "$CI_REGISTRY_USER" --password-stdin
    - docker build -t "$CI_REGISTRY_IMAGE:$CI_COMMIT_TAG" .
    - docker push "$CI_REGISTRY_IMAGE:$CI_COMMIT_TAG"
""",
    tests=[
        rx("Объявлена стадия build", r"^stages:\s*(\n\s*-\s*\w+[ \t]*)*\n\s*-\s*build\b|^stages:\s*\[[^\]]*\bbuild\b"),
        rx("pip кэширует в папку проекта", r"PIP_CACHE_DIR:\s*['\"]?\$\{?CI_PROJECT_DIR\}?/\.cache/pip"),
        rx("Ключ кэша — по requirements.txt",
           r"key:\s*\n\s+files:\s*(\[\s*['\"]?|\n\s+-\s*['\"]?)requirements\.txt"),
        rx("В кэш попадает .cache/pip", r"paths:\s*(\[\s*|\n\s+-\s*)['\"]?\.cache/pip"),
        rx("pytest пишет JUnit-отчёт", r"--junitxml[= ]\S+"),
        rx("Отчёт сохраняется в artifacts:reports:junit", r"reports:\s*\n\s+junit:\s*['\"]?\S+"),
        rx("build запускается только для тегов", r"rules:\s*\n\s+-\s*if:\s*['\"]?\$\{?CI_COMMIT_TAG\b"),
        rx("Логин в реестр через --password-stdin", r"docker\s+login\b[^\n]*--password-stdin"),
        nrx("Пароль не передаётся через -p/--password", r"docker\s+login[^\n]*\s(-p|--password)[\s=]"),
        rx("Пушится образ с тегом релиза",
           r"docker\s+push\s+['\"]?\$\{?CI_REGISTRY_IMAGE\}?:\$\{?CI_COMMIT_TAG\}?"),
    ],
))

# =====================================================================
# do-nginx
# =====================================================================
TOPICS.append({"topic_id": "do-nginx", "theory": """\
Nginx обычно стоит перед приложением как reverse proxy: принимает соединения браузеров, терминирует TLS, сам отдаёт статику, а остальные запросы передаёт приложению (gunicorn или uvicorn на 127.0.0.1:8000, контейнер в той же сети). Главный конфиг — /etc/nginx/nginx.conf, сайты — в /etc/nginx/conf.d/*.conf или в sites-available с симлинком в sites-enabled.

server и location. server — виртуальный хост: какой порт слушать (listen) и на какие домены отвечать (server_name). location — правила для путей. Выбор: сначала точное совпадение (location = /path), потом самый длинный подходящий префикс; если у него нет модификатора ^~, проверяются регулярки (~) в порядке объявления. Порядок префиксных location в файле роли не играет.

Статика и SPA:
    root /var/www/app;
    location / {
        try_files $uri $uri/ /index.html;
    }
root приклеивает URI к пути (/img/a.png → /var/www/app/img/a.png), alias заменяет префикс location на указанный путь.

Проксирование:
    location /api/ {
        proxy_pass http://127.0.0.1:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
Без этих заголовков приложение видит Host из proxy_pass, IP nginx вместо клиента и думает, что запрос пришёл по http, — ломаются ссылки, редиректы, логи и rate limit.

Слэш в proxy_pass меняет путь:
• proxy_pass http://app:8000; — без URI: путь уходит как есть, /api/orders → /api/orders;
• proxy_pass http://app:8000/; — с URI (даже просто /): совпавший префикс location заменяется на него, /api/orders → /orders.

TLS:
    server {
        listen 80;
        server_name example.ru;
        return 301 https://$host$request_uri;
    }
    server {
        listen 443 ssl;
        http2 on;
        server_name example.ru;
        ssl_certificate /etc/letsencrypt/live/example.ru/fullchain.pem;
        ssl_certificate_key /etc/letsencrypt/live/example.ru/privkey.pem;
        ssl_protocols TLSv1.2 TLSv1.3;
    }
Сертификаты Let's Encrypt выпускает и продлевает certbot. Указывай fullchain.pem, а не cert.pem, иначе часть клиентов не построит цепочку доверия. Заголовок Strict-Transport-Security велит браузеру ходить только по https.

Ещё полезное: client_max_body_size (по умолчанию 1m, больше — ответ 413), autoindex (листинг папки, держи выключенным), server_tokens off, gzip on.

502 и 504:
• 502 Bad Gateway — nginx не получил нормального ответа: приложение не запущено или слушает другой порт (connect() failed (111: Connection refused)), оборвало ответ (upstream prematurely closed connection), нет прав на unix-сокет (13: Permission denied).
• 504 Gateway Timeout — приложение не ответило за proxy_read_timeout (upstream timed out (110)).
Смотри /var/log/nginx/error.log: в строке ошибки есть upstream — адрес, куда nginx реально ходил. Сравни его с выводом ss -ltnp.

Любая правка — через nginx -t (проверка), затем systemctl reload nginx: reload применяет конфиг без обрыва соединений. nginx -T покажет весь итоговый конфиг."""})

TASKS.append(task(
    "do-nginx-01", "write_code", 2, "teamlead",
    "Гена: «Фронт Батона собран в /var/www/baton, бэкенд крутится на 127.0.0.1:8000. А на домене сейчас открывается "
    "приветственная страница nginx. Настрой server так, чтобы фронт и API жили на одном домене».",
    "Допиши server: корень статики /var/www/baton; для SPA неизвестные пути отдают /index.html (try_files); запросы /api/ "
    "проксируются на http://127.0.0.1:8000 без изменения пути (бэкенд сам ждёт префикс /api) с заголовками Host, X-Real-IP, "
    "X-Forwarded-For и X-Forwarded-Proto.",
    "root задаёт папку, к которой nginx приклеивает URI запроса. try_files сначала ищет файл, потом папку, а если ничего нет — "
    "отдаёт index.html: так React-роутер сам разберётся с путём /orders/42, и обновление страницы не приведёт к 404. "
    "Для /api/ proxy_pass без URI (без слэша после порта) передаёт путь как есть — бэкенд получит /api/orders. Заголовки нужны "
    "бэкенду, чтобы знать настоящий домен, IP клиента (для логов и rate limit) и протокол (для правильных ссылок и редиректов): "
    "без них он видит только nginx.",
    ["Для SPA: try_files $uri $uri/ /index.html;",
     "Слэш после порта в proxy_pass меняет путь — здесь он не нужен.",
     "Четыре proxy_set_header: Host $host, X-Real-IP $remote_addr, X-Forwarded-For $proxy_add_x_forwarded_for, X-Forwarded-Proto $scheme."],
    code="""\
server {
    listen 80;
    server_name baton-bakery.ru;

    # статика фронта: /var/www/baton

    # API: 127.0.0.1:8000
}
""",
    language="nginx",
    answer="""\
server {
    listen 80;
    server_name baton-bakery.ru;

    root /var/www/baton;

    location / {
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://127.0.0.1:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
""",
    tests=[
        rx("Корень статики — /var/www/baton", r"^\s*root\s+/var/www/baton/?\s*;"),
        rx("SPA: неизвестные пути отдают /index.html", r"try_files\s+\$uri\s+(\$uri/\s+)?/index\.html\s*;"),
        rx("/api/ проксируется на 127.0.0.1:8000 без изменения пути",
           r"location\s+/api/?\s*\{[^}]*proxy_pass\s+http://(127\.0\.0\.1|localhost):8000\s*;"),
        rx("Передаётся Host", r"proxy_set_header\s+Host\s+\$host\s*;"),
        rx("Передаётся X-Real-IP", r"proxy_set_header\s+X-Real-IP\s+\$remote_addr\s*;"),
        rx("Передаётся X-Forwarded-For", r"proxy_set_header\s+X-Forwarded-For\s+\$proxy_add_x_forwarded_for\s*;"),
        rx("Передаётся X-Forwarded-Proto", r"proxy_set_header\s+X-Forwarded-Proto\s+\$scheme\s*;"),
    ],
))

TASKS.append(task(
    "do-nginx-02", "find_bug", 3, "qa",
    "Ира: «На странице товара не грузятся отзывы: фронт получает 404 на /api/reviews/812. Если дёрнуть сервис отзывов "
    "напрямую по /api/reviews/812 — всё отвечает. Шаги: открыть любой товар, вкладка „Отзывы“».",
    "Почему сервис отзывов отвечает 404 и как исправить конфиг?",
    "Если в proxy_pass указан URI — хоть один слэш после адреса, — nginx заменяет совпавший префикс location на этот URI: "
    "/api/reviews/812 превращается в /812, что и видно в логе сервиса. Без URI (proxy_pass http://reviews;) путь передаётся "
    "без изменений, и сервис получает /api/reviews/812. Вариант с /api/ дал бы /api/812. Порядок location в файле не важен: "
    "из префиксных location nginx выбирает самый длинный совпавший, поэтому /api/reviews/ и так ловит запросы к отзывам. "
    "А 404 от самого сервиса доказывает, что имя reviews резолвится и запрос до него доходит; query string на выбор location не влияет вовсе.",
    ["Сравни путь в запросе фронта и путь в логе сервиса.",
     "Что nginx делает с путём, если в proxy_pass после адреса есть слэш?"],
    code="""\
# access-лог сервиса reviews:
# INFO:     172.18.0.5:41812 - "GET /812?page=1 HTTP/1.0" 404 Not Found

upstream reviews {
    server reviews:8002;
}

server {
    listen 80;
    server_name lampovy-market.ru;

    location /api/reviews/ {
        proxy_pass http://reviews/;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    location /api/ {
        proxy_pass http://api:8000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
""",
    language="nginx",
    options=[
        "Слэш в proxy_pass заменяет префикс location: /api/reviews/812 уходит как /812. Нужно proxy_pass http://reviews;",
        "Заменить на proxy_pass http://reviews/api/; — так префикс /api сохранится, а лишний reviews/ отрежется",
        "location /api/reviews/ со слэшем не ловит запросы с query string (?page=1) — убрать слэш в location",
        "Блок location /api/ объявлен ниже и перехватывает запросы — поменять местами /api/ и /api/reviews/",
        "nginx резолвит имя reviews один раз при старте и держит старый IP — указать IP контейнера или resolver",
    ],
    answer=0,
))

TASKS.append(task(
    "do-nginx-03", "write_code", 3, "client",
    "Артур, директор фитнес-клуба «Жми»: «Клиенты жалуются, что браузер пишет „Не защищено“ прямо на странице записи, "
    "где вводят телефон. Дима сказал, сертификат уже выпущен, осталось настроить nginx. Сделайте, чтобы был замочек».",
    "Перепиши конфиг: server на 80 порту только редиректит на https с кодом 301, сохраняя хост и путь; server на 443 с ssl "
    "и http2, сертификат и ключ из /etc/letsencrypt/live/zhmi-fitness.ru/ (fullchain.pem и privkey.pem), только TLSv1.2 и TLSv1.3, "
    "заголовок HSTS на год (max-age=31536000) и прежнее проксирование на 127.0.0.1:3000, плюс заголовок X-Forwarded-Proto.",
    "Первый server ничего не проксирует, а отвечает 301 на тот же хост и путь по https — старые ссылки и закладки продолжают "
    "работать. listen 443 ssl включает TLS, а http2 on — синтаксис nginx 1.25.1+ (параметр http2 в listen устарел). fullchain.pem "
    "содержит сертификат вместе с промежуточными: с одним cert.pem часть клиентов, особенно на Android, покажет ошибку доверия. "
    "TLS 1.0 и 1.1 давно считаются небезопасными, но в старых инструкциях и дистрибутивных конфигах встречаются до сих пор — "
    "поэтому протоколы задают явно. HSTS велит браузеру год ходить только по https, а X-Forwarded-Proto нужен приложению, "
    "чтобы строить ссылки и редиректы с https.",
    ["Нужно два блока server: один на 80, другой на 443.",
     "Редирект: return 301 https://$host$request_uri;",
     "add_header Strict-Transport-Security \"max-age=31536000\" always;"],
    code="""\
server {
    listen 80;
    server_name zhmi-fitness.ru;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
""",
    language="nginx",
    answer="""\
server {
    listen 80;
    server_name zhmi-fitness.ru;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name zhmi-fitness.ru;

    ssl_certificate     /etc/letsencrypt/live/zhmi-fitness.ru/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/zhmi-fitness.ru/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;

    add_header Strict-Transport-Security "max-age=31536000" always;

    location / {
        proxy_pass http://127.0.0.1:3000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
""",
    tests=[
        rx("HTTP редиректит на HTTPS с кодом 301",
           r"return\s+301\s+https://(\$host|\$server_name|zhmi-fitness\.ru)\$request_uri\s*;"),
        rx("Слушается 443 с ssl", r"listen\s+(\[::\]:)?443\s+ssl\b"),
        rx("Сертификат — fullchain.pem",
           r"ssl_certificate\s+/etc/letsencrypt/live/zhmi-fitness\.ru/fullchain\.pem\s*;"),
        rx("Ключ — privkey.pem",
           r"ssl_certificate_key\s+/etc/letsencrypt/live/zhmi-fitness\.ru/privkey\.pem\s*;"),
        rx("Только TLSv1.2 и TLSv1.3", r"ssl_protocols\s+TLSv1\.[23](\s+TLSv1\.[23])?\s*;"),
        nrx("Нет устаревших TLSv1 и TLSv1.1", r"ssl_protocols[^;]*TLSv1(\.1)?[\s;]"),
        rx("Включён HSTS", r"add_header\s+Strict-Transport-Security\s+['\"]?max-age=\d+"),
        rx("Передаётся X-Forwarded-Proto", r"proxy_set_header\s+X-Forwarded-Proto\s+\$scheme\s*;"),
    ],
))

TASKS.append(task(
    "do-nginx-04", "incident", 3, "devops_colleague",
    "Дима: «После утреннего деплоя КофеБот отдаёт 502 на всё API — офис без кофе. Сервис вроде запущен, systemd говорит "
    "active. Скинул, что успел посмотреть».",
    "В чём причина 502 и что делать?",
    "502 значит, что nginx не получил от приложения нормального ответа. В error.log причина указана прямо: connect() failed "
    "(111: Connection refused) на 127.0.0.1:8000 — по этому адресу никто не слушает. ss показывает, что gunicorn после деплоя "
    "слушает 8080: новый gunicorn.conf.py поменял bind. Лечится выравниванием портов — быстрее и безопаснее вернуть приложению "
    "прежний порт; если правишь nginx, сначала nginx -t, потом reload. Таймаут выглядел бы как 504 и upstream timed out (110), "
    "а ufw по умолчанию не фильтрует трафик на loopback. Сервис запущен, так что рестарт ничего не даст, а сертификат касается "
    "связи браузер — nginx, а не nginx — приложение.",
    ["Найди в error.log, куда именно nginx пытается подключиться.",
     "Сравни адрес upstream с тем, что показывает ss."],
    code="""\
$ curl -sI https://coffee.kodzilla.local/api/menu | head -2
HTTP/2 502
server: nginx

$ sudo tail -n 2 /var/log/nginx/error.log
2026/09/24 10:41:07 [error] 1183#1183: *5821 connect() failed (111: Connection refused) while connecting to upstream, client: 10.0.4.23, server: coffee.kodzilla.local, request: "GET /api/menu HTTP/2.0", upstream: "http://127.0.0.1:8000/api/menu", host: "coffee.kodzilla.local"
2026/09/24 10:41:09 [error] 1183#1183: *5824 connect() failed (111: Connection refused) while connecting to upstream, client: 10.0.4.17, server: coffee.kodzilla.local, request: "POST /api/orders HTTP/2.0", upstream: "http://127.0.0.1:8000/api/orders", host: "coffee.kodzilla.local"

$ systemctl status coffeebot-api --no-pager | head -3
● coffeebot-api.service - KofeBot API (gunicorn)
     Loaded: loaded (/etc/systemd/system/coffeebot-api.service; enabled; preset: enabled)
     Active: active (running) since Thu 2026-09-24 10:32:15 MSK; 9min ago

$ sudo ss -ltnp | grep -E 'nginx|gunicorn'
LISTEN 0  511   0.0.0.0:443     0.0.0.0:*  users:(("nginx",pid=1183,fd=7))
LISTEN 0  511   0.0.0.0:80      0.0.0.0:*  users:(("nginx",pid=1183,fd=6))
LISTEN 0  2048  127.0.0.1:8080  0.0.0.0:*  users:(("gunicorn",pid=20931,fd=5))

$ git -C /opt/coffeebot show --stat --oneline HEAD
9f3c2e1 Вынес настройки gunicorn в gunicorn.conf.py
 deploy/coffeebot-api.service | 2 +-
 gunicorn.conf.py             | 5 +++++
""",
    language="text",
    options=[
        "Приложение после деплоя не успевает ответить — увеличить proxy_read_timeout до 60 секунд",
        "gunicorn после деплоя слушает 127.0.0.1:8080, а nginx ходит на 8000 — вернуть bind на 8000 и перезапустить",
        "Воркеры gunicorn падают при старте, хотя systemd пишет active, — выполнить systemctl restart coffeebot-api",
        "Деплой включил ufw, и он закрыл порт 8000 на loopback — разрешить его: ufw allow 8000/tcp",
        "Истёк TLS-сертификат: nginx не может установить защищённое соединение с бэкендом",
    ],
    answer=1,
    time_limit=15,
))

TASKS.append(task(
    "do-nginx-05", "code_review", 4, "teamlead",
    "Марина: «Джун переписал конфиг nginx для Маркета перед запуском отзывов с фотографиями — до 10 МБ на фото. "
    "Посмотри PR и отметь то, что реально надо исправить».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Без proxy_set_header бэкенд получает Host из proxy_pass (api:8000) и адрес nginx вместо клиента: ломаются абсолютные "
    "ссылки, редиректы уходят на http, логи и rate limit видят один IP на всех. TLS 1.0 и 1.1 устарели, сканеры помечают их "
    "как уязвимость. autoindex превращает папку загрузок в публичный каталог — можно выкачать все файлы пользователей. "
    "Лимит тела запроса по умолчанию 1 МБ, так что фото отзывов упрутся в 413 — лимит поднимают хотя бы для эндпоинта загрузки. "
    "Остальное неверно: nginx резолвит имена из proxy_pass при старте, http2 on — как раз новый синтаксис, if внутри location — "
    "известный источник сюрпризов, а root в location допустим.",
    ["Какие данные о клиенте теряются по пути через прокси?",
     "Какой максимальный размер тела запроса nginx пропускает по умолчанию?"],
    code="""\
server {
    listen 443 ssl;
    http2 on;
    server_name lampovy-market.ru;

    ssl_certificate     /etc/letsencrypt/live/lampovy-market.ru/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/lampovy-market.ru/privkey.pem;
    ssl_protocols TLSv1 TLSv1.1 TLSv1.2 TLSv1.3;

    location / {
        root /var/www/market;
        try_files $uri /index.html;
    }

    location /uploads/ {
        alias /srv/market/uploads/;
        autoindex on;
    }

    location /api/ {
        proxy_pass http://api:8000;
    }
}
""",
    language="nginx",
    options=[
        "Нет proxy_set_header Host, X-Forwarded-For и X-Forwarded-Proto: бэкенд видит api:8000, IP nginx и http",
        "proxy_pass с именем хоста ненадёжен: nginx резолвит api при каждом запросе — указать IP контейнера",
        "В ssl_protocols остались устаревшие TLSv1 и TLSv1.1 — оставить только TLSv1.2 и TLSv1.3",
        "http2 on; — устаревшая директива, правильно писать listen 443 ssl http2; как в документации",
        "autoindex on на /uploads/ отдаёт любому список всех загруженных файлов — выключить",
        "try_files лучше заменить на if (!-f $request_filename) { rewrite ... } — так надёжнее для SPA",
        "Не задан client_max_body_size: по умолчанию 1 МБ, и фото на 10 МБ получат 413",
        "root внутри location не работает — его можно указывать только на уровне server, иначе 404",
    ],
    answer=[0, 2, 4, 6],
))

# =====================================================================
# do-cloud
# =====================================================================
TOPICS.append({"topic_id": "do-cloud", "theory": """\
Облако — это аренда серверов, сетей и готовых сервисов через API с оплатой по факту. У AWS, Yandex Cloud, VK Cloud и Selectel названия разные, а кирпичи одни и те же.

Виртуальные машины. ВМ — это vCPU, RAM, диск и образ ОС. Платишь за время работы; у остановленной ВМ процессор и память не тарифицируются, но диск, снимки и зарезервированный публичный IP продолжают стоить денег. Прерываемые (spot/preemptible) ВМ в разы дешевле, но облако может выключить их в любой момент — годятся для CI и тестов, не для базы.

Сеть. Ресурсы живут в облачной сети (VPC) с приватными подсетями. Публичный IP получает только то, что должно быть видно из интернета, — балансировщик или web-сервер; база и воркеры ходят наружу через NAT-шлюз.

Группы безопасности (security groups) — файрвол на уровне сетевого интерфейса:
• запрещено всё, что явно не разрешено; правило = протокол + порт + источник;
• stateful: ответы на разрешённые соединения проходят автоматически;
• источник — CIDR (203.0.113.10/32) или другая группа безопасности; правило «база принимает 5432 только от группы приложения» не ломается при смене IP.
Типовая схема: 80/443 — от 0.0.0.0/0, 22 — только с VPN или бастиона, 5432 — только от группы приложения. Открытые миру SSH и базы сканеры находят за минуты.

Managed-сервисы (PostgreSQL, Redis, Kubernetes): бэкапы, обновления и отказоустойчивость берёт на себя провайдер. Дороже голой ВМ, но дешевле потерянных данных и твоих ночей.

Объектное хранилище (S3-совместимое): бакеты и объекты по ключу вида products/812/main.webp — для статики, фото, бэкапов, логов. Это не файловая система и не база.
• По умолчанию бакет приватный. Публичное чтение — только для того, что и так видно всем, и лучше через CDN: трафик дешевле, есть защита от хотлинка.
• Документы с персональными данными — только в приватном бакете; пользователю бэкенд выдаёт presigned URL, который живёт несколько минут.
• Политика с Principal "*" на весь бакет — классическая утечка.

IAM решает, кто (пользователь, сервисный аккаунт) какое действие над каким ресурсом может выполнять. Принцип наименьших привилегий: у каждого сервиса свой сервисный аккаунт с минимальными правами на конкретные ресурсы.

    {
      "Effect": "Allow",
      "Action": ["s3:PutObject", "s3:DeleteObject"],
      "Resource": "arn:aws:s3:::baton-site/*"
    }

• Аккаунт владельца (root) — только с MFA и не для повседневной работы; личные ключи людей в автоматике не используют.
• CI получает доступ через OIDC-федерацию: токен на минуты, вечных ключей нет. Где федерации нет — статический ключ в секретах CI и регулярная ротация.

Стоимость.
• Считай заранее в калькуляторе провайдера. Главные статьи: ВМ, managed-базы, исходящий трафик (egress), диски и снимки, публичные IP, NAT. Входящий трафик обычно бесплатный, исходящий — платный.
• Бюджеты с алертами (на 80% и 100% от ожидаемого) и метки owner/env на всех ресурсах — у каждой ВМ должен быть хозяин.
• Rightsizing: смотри на реальную загрузку и уменьшай то, что простаивает. Забытые тестовые стенды и крупные файлы в публичном доступе — самые частые причины внезапного счёта."""})

TASKS.append(task(
    "do-cloud-01", "quiz", 2, "devops_colleague",
    "Дима: «Переносим Батон в облако: ВМ web с nginx и приложением и ВМ db с Postgres в одной приватной сети. "
    "Сейчас группы безопасности открыты на всё — так нельзя. Составь входящие правила».",
    "Какие входящие правила групп безопасности должны быть? Выбери все верные.",
    "Группа безопасности — файрвол на уровне сетевого интерфейса: разрешено только то, что явно открыто, а ответный трафик "
    "проходит автоматически. Сайт должен быть доступен всем, поэтому 80 и 443 открыты миру. Базу видит только приложение: "
    "источником правила указывают группу web, а не адреса — так правило не сломается, когда ВМ пересоздадут с новым IP. "
    "SSH из всего интернета за минуты находят сканеры и начинают перебирать пароли, поэтому 22 открывают только с VPN или "
    "бастиона. Открытая наружу база «со сложным паролем» — частая причина утечек: к ней стучатся боты с эксплойтами под "
    "известные уязвимости. А «временно на отладку» обычно остаётся навсегда.",
    ["Кому на самом деле нужно подключаться к каждому порту?",
     "Источником правила может быть не только IP, но и другая группа безопасности."],
    options=[
        "web: 80/tcp и 443/tcp от 0.0.0.0/0",
        "web: 22/tcp от 0.0.0.0/0 — вдруг придётся зайти из кафе",
        "db: 5432/tcp только от группы безопасности web",
        "db: 5432/tcp от 0.0.0.0/0 — пароль же сложный",
        "web: 22/tcp только с IP офисного VPN или бастиона",
        "db: все порты от 0.0.0.0/0 на время отладки",
    ],
    answer=[0, 2, 4],
))

TASKS.append(task(
    "do-cloud-02", "architecture", 2, "devops_colleague",
    "Дима: «Фронт Батона теперь лежит в объектном хранилище, в бакете baton-site. Хочу, чтобы GitHub Actions после сборки "
    "сам заливал туда файлы. Как выдадим пайплайну доступ в облако?»",
    "Какой способ выдать CI доступ правильный?",
    "Принцип наименьших привилегий: у каждой системы своя учётная запись и ровно те права, которые нужны для её задачи, "
    "на конкретном ресурсе. Пайплайну нужно только класть и удалять файлы в одном бакете — если его доступ утечёт, злоумышленник "
    "сможет испортить сайт, но не удалит базы и не запустит майнеры за твой счёт. OIDC-федерация выдаёт токен на минуты под "
    "конкретный репозиторий и ветку, так что вечный ключ вообще не нужно хранить. Ключи владельца и админская роль дают полный "
    "доступ ко всему облаку, личные ключи привязывают прод к человеку (уволился — деплой сломался, а в аудите не отличить CI "
    "от Димы), а публичная запись позволит любому подменить сайт: адреса раннеров GitHub общие для всех его пользователей. Если облако не умеет OIDC, берут статический ключ того же сервисного аккаунта в секретах CI и регулярно его ротируют.",
    ["Что сможет сделать злоумышленник, если этот доступ утечёт?",
     "Какой минимальный набор действий и над каким ресурсом нужен пайплайну?"],
    options=[
        "Ключи владельца облачного аккаунта в секретах GitHub: прав точно хватит, а секреты GitHub и так зашифрованы",
        "Сервисный аккаунт с ролью администратора каталога: вдруг пайплайну понадобится что-то ещё, кроме бакета",
        "Отдельный сервисный аккаунт с правом только писать и удалять объекты в baton-site; токен — через OIDC",
        "Личные ключи Димы в секретах CI: они уже работают, а Дима отвечает за инфраструктуру и не против",
        "Открыть бакет на публичную запись только для IP-адресов раннеров GitHub — ключи вообще не понадобятся",
    ],
    answer=2,
))

TASKS.append(task(
    "do-cloud-03", "find_bug", 3, "qa",
    "Ира: «Нашла дыру: открыла в режиме инкогнито ссылку на счёт чужого заказа из market-media/invoices/ — и скачался PDF "
    "с ФИО и адресом. Фото товаров из того же бакета открываются, как и должны».",
    "В чём ошибка в политике бакета и как правильно исправить?",
    "Principal \"*\" с s3:GetObject на ресурс market-media/* разрешает анониму читать любой объект бакета, и счета попали "
    "под правило вместе с фотографиями. Минимальная правка — сузить Resource до market-media/products/*, но надёжнее разделить "
    "данные: публичный бакет только для контента, который и так виден всем, и приватный — для документов с персональными данными. "
    "Такие файлы отдают по presigned URL: бэкенд проверяет, что заказ принадлежит пользователю, и выдаёт подписанную ссылку на "
    "несколько минут. Случайные имена не защищают — ссылки утекают через историю браузера, логи и пересылки. Principal \"*\" — это любой, включая анонимов, но роль фронтенда тут не поможет: фото должны открываться у всех. ListBucket вообще даёт только список ключей. "
    "\"2012-10-17\" — действующая версия языка политик, а не дата, которую надо обновлять.",
    ["На какие объекты распространяется Resource с market-media/*?",
     "Как отдать файл конкретному пользователю, не делая его публичным?"],
    code="""\
Бакет market-media:
  products/812/main.webp
  products/812/side.webp
  invoices/2026/09/order-48213.pdf

Политика бакета:
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "PublicReadProductPhotos",
      "Effect": "Allow",
      "Principal": "*",
      "Action": "s3:GetObject",
      "Resource": "arn:aws:s3:::market-media/*"
    }
  ]
}
""",
    language="text",
    options=[
        "market-media/* открыл весь бакет, включая invoices/. Публичным оставить products/*, счета — приватно и по presigned URL",
        "Principal \"*\" значит «любой аккаунт облака» — заменить на ARN роли фронтенда, и аноним ничего не скачает",
        "Заменить s3:GetObject на s3:ListBucket: объекты будут видны в списке, но скачать их без подписи будет нельзя",
        "Version 2012-10-17 устарела и включает старую модель доступа — поставить актуальную дату, правило станет строже",
        "Переименовать счета в случайные UUID: ссылку никто не угадает, а фото товаров откроются как раньше",
    ],
    answer=0,
))

TASKS.append(task(
    "do-cloud-04", "architecture", 3, "client",
    "Нина, владелица пекарни «Батон»: «Под праздники сайт на старом хостинге падает, а однажды пропали заказы за неделю. "
    "Хочу в облако, но без космических счетов: у нас 300–500 заказов в день, в праздники втрое больше. Своего админа у меня нет».",
    "Какую схему предложить Нине?",
    "Сначала нагрузка: 1 500 заказов в праздничный день даже со всеми просмотрами каталога — единицы запросов в секунду, "
    "с этим справится одна небольшая ВМ. Главная боль Нины — пропавшие данные, поэтому базу отдают в managed-сервис: бэкапы, "
    "обновления и восстановление на момент времени делает провайдер, админ не нужен. Статика и фото в объектном хранилище "
    "разгружают ВМ и стоят копейки. Kubernetes и кластеры в трёх зонах в разы дороже и требуют специалиста, которого у Нины нет. "
    "Postgres на той же ВМ с дампами на тот же диск повторит историю с потерей заказов — копии сгорят вместе с ВМ, а железный сервер на год — замороженные деньги и "
    "то же ручное администрирование. Перед предложением цену прикидывают в калькуляторе облака.",
    ["Прикинь, сколько запросов в секунду дают 1 500 заказов в день.",
     "Что у Нины болит сильнее: скорость или сохранность данных?"],
    options=[
        "Managed Kubernetes из трёх нод, PostgreSQL-кластер с репликами в трёх зонах и Redis — праздники не страшны",
        "Одна ВМ (2 vCPU, 4 ГБ) с nginx и приложением, managed PostgreSQL с автобэкапами, фото — в объектном хранилище",
        "Одна ВМ с приложением и Postgres, pg_dump по cron на тот же диск — дёшево, и заказы больше не пропадут",
        "Три ВМ приложения за балансировщиком в разных зонах и Postgres на отдельной ВМ с ручной репликацией",
        "Выделенный физический сервер на год вперёд с запасом мощности — дешевле облака при постоянной нагрузке",
    ],
    answer=1,
))

TASKS.append(task(
    "do-cloud-05", "incident", 4, "manager",
    "Стас: «Финдиректор в шоке: счёт за облако Маркета за август в пять раз больше июльского. Все клянутся, что ничего "
    "не запускали. Разберись до вечера, пока нам не выставили ещё за сентябрь».",
    "Что нужно сделать? Выбери все правильные действия.",
    "Сравнение месяцев сразу показывает две выросшие статьи: ВМ (+238 тыс.) и исходящий трафик хранилища (+67 тыс.), а база "
    "и хранение почти не изменились. В списке ВМ — четыре мощные машины loadtest-gen без меток, созданные 4 августа и не "
    "выключенные после нагрузочного теста: их подтверждают с владельцем и удаляют вместе с публичными IP. Трафик съело одно "
    "промо-видео, вставленное на чужом форуме, — каждый просмотр оплачивает Маркет; крупные файлы отдают через CDN с защитой "
    "от хотлинка или по подписанным ссылкам. Чтобы не узнавать о таком из счёта через месяц, нужны бюджеты с алертами и метки "
    "владельца на каждом ресурсе. Урезать прод-базу, удалять бэкапы и выключать мониторинг — копеечная экономия ценой риска "
    "для прода, а переезд не лечит причину.",
    ["Сравни статьи по месяцам: какие выросли, а какие нет?",
     "У каких ВМ нет меток и когда они созданы?",
     "Кто смотрит видео, если 91% запросов приходит с чужого форума?"],
    code="""\
Биллинг «market-prod», ₽ без НДС        Июль      Август
Compute (ВМ)                          41 200     279 400
Managed PostgreSQL                    28 400      28 900
Object Storage — хранение              3 100       3 300
Object Storage — исходящий трафик      1 900      69 400
Публичные IP-адреса                    2 300       5 100
Итого                                 76 900     386 100

Консоль → Compute → Виртуальные машины
NAME             vCPU  RAM     STATUS   CREATED     LABELS
market-api-1        4   8 GB   RUNNING  2025-11-02  env=prod,owner=backend
market-api-2        4   8 GB   RUNNING  2025-11-02  env=prod,owner=backend
market-worker-1     2   4 GB   RUNNING  2026-02-17  env=prod,owner=backend
loadtest-gen-1     16  32 GB   RUNNING  2026-08-04  -
loadtest-gen-2     16  32 GB   RUNNING  2026-08-04  -
loadtest-gen-3     16  32 GB   RUNNING  2026-08-04  -
loadtest-gen-4     16  32 GB   RUNNING  2026-08-04  -

Object Storage → топ объектов по исходящему трафику, август
OBJECT                                SIZE     GET       TRAFFIC   TOP REFERER
market-media/promo/lamp-show-4k.mp4   1.2 GB   37 512    45.0 TB   forum.gadget-talk.ru (91%)
market-media/products/812/main.webp   180 KB   402 310   72.4 GB   lampovy-market.ru
""",
    language="text",
    options=[
        "Найти владельца loadtest-машин, подтвердить, что они не нужны, и удалить их вместе с публичными IP",
        "Закрыть хотлинк промо-видео: убрать публичный доступ к promo/ и отдавать его через CDN с проверкой Referer",
        "Настроить бюджет с алертами на 80% и 100% обычного счёта и обязательные метки owner/env",
        "Срочно уменьшить managed PostgreSQL вдвое: после ВМ это самая дорогая статья, экономия надёжная",
        "Удалить все снимки дисков и старые бэкапы: хранение тоже выросло, а данные и так есть в самой базе",
        "Отключить мониторинг и сбор логов на проде до конца месяца — метрики тоже тарифицируются",
        "Переехать к другому облачному провайдеру, где исходящий трафик дешевле, — проблема в тарифах",
    ],
    answer=[0, 1, 2],
    time_limit=20,
))


def main():
    data = {"track": "devops", "part": "d2", "topics": TOPICS, "tasks": TASKS}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("written", OUT, "tasks:", len(TASKS))
    for tp in TOPICS:
        print(" ", tp["topic_id"], "theory chars:", len(tp["theory"]))


if __name__ == "__main__":
    main()
