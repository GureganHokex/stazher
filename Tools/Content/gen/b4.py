# -*- coding: utf-8 -*-
# Генератор контента: backend, часть b4 (middle): be-api-design, be-indexes,
# be-transactions, be-n-plus-one, be-caching.
import json
import random
import os

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}
GRADE = "middle"

TOPICS = []
TASKS = []


def C(s):
    """Код/лог из тройных кавычек: убираем ведущий перевод строки."""
    return s.lstrip("\n")


def T(s):
    """Текст теории: убираем крайние пустые строки."""
    return s.strip("\n")


def topic(topic_id, theory):
    TOPICS.append({"topic_id": topic_id, "theory": T(theory)})


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


def task(tid, typ, diff, character, story, question, explanation, hints,
         code=None, language=None, options=None, answer=None, tests=None,
         entry=None, time_limit=None):
    topic_id = tid.rsplit("-", 1)[0]
    content = {
        "question": question,
        "code": code,
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content["options"] = options
    content["correct_answer"] = answer
    content["test_cases"] = tests
    TASKS.append({
        "task_id": tid,
        "topic_id": topic_id,
        "grade": GRADE,
        "type": typ,
        "difficulty": diff,
        "xp_reward": int(round(XP_BASE[diff] * XP_MULT[GRADE] / 5.0)) * 5,
        "time_limit_minutes": time_limit,
        "character": character,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": hints,
    })


# =====================================================================
# be-api-design
# =====================================================================
topic("be-api-design", """
API живёт дольше, чем кажется: мобильное приложение, которое ты сломал сегодня, пользователи обновят через полгода. Поэтому на уровне Middle думают не только «как вернуть данные», но и «как это переживёт ретраи, миллион строк и два года развития».

Пагинация. Offset (LIMIT 20 OFFSET 10000) простая, но база всё равно читает и выбрасывает 10 000 строк, а если между запросами появился новый заказ, записи на страницах съезжают — дубли и пропуски. Курсорная (keyset) пагинация запоминает, на какой записи остановились, и продолжает строго после неё:

    SELECT id, created_at, total FROM orders
    WHERE user_id = $1 AND (created_at, id) < ($2, $3)
    ORDER BY created_at DESC, id DESC
    LIMIT 21;

• Ключ сортировки должен быть уникальным. created_at не уникален (импорт пачкой, одна миллисекунда), поэтому добавляют id как tie-breaker — и в ORDER BY, и в курсор.
• Берут limit + 1 строку: если пришла лишняя, следующая страница есть.
• Курсор для клиента — непрозрачная строка (например, base64 от JSON), чтобы формат можно было менять.
• Под запрос нужен индекс (user_id, created_at DESC, id DESC).
• limit ограничивают сверху (например, 100).

Offset оставь для админок с номерами страниц и небольших таблиц.

Фильтры и сортировка — через query-параметры с белым списком полей: ?status=paid&sort=-created_at. Имя поля из запроса никогда не подставляют в SQL как есть.

Коды ответов: 201 Created и заголовок Location — ресурс создан; 202 Accepted — приняли в фоновую обработку; 204 — успех без тела; 400 — запрос не разобрать; 409 — конфликт с текущим состоянием; 422 — тело понятно, но не прошло проверку (FastAPI отдаёт его на ошибки валидации).

Идемпотентность. GET, PUT и DELETE идемпотентны по смыслу, POST — нет: ретрай после таймаута создаст второй заказ и второе списание. Решение — заголовок Idempotency-Key (его стандартизируют в IETF, так работают платёжные API):
• клиент генерирует UUID на одну операцию и при ретрае шлёт тот же ключ;
• сервер хранит ключ в паре с клиентом, отпечаток тела и сохранённый ответ; повтор с тем же телом получает тот же ответ, а заказ заново не создаётся;
• тот же ключ с другим телом — 422, параллельный запрос с ключом, который ещё обрабатывается, — 409, запрос без обязательного ключа — 400;
• ключи хранят ограниченное время (обычно сутки), а гонку двух одинаковых запросов закрывает уникальный индекс по (user_id, key).

Версионирование. Версию обычно пишут в URL (/v1/...), реже в заголовке. Неломающие изменения: новое поле в ответе, новый эндпоинт, новый необязательный параметр — клиенты обязаны игнорировать незнакомые поля. Ломающие: удалить или переименовать поле, сменить тип или формат, сделать параметр обязательным, ужесточить валидацию. Вместо ломающего изменения сначала пробуют расширение: новое поле рядом со старым, старое помечают deprecated в OpenAPI и заголовками Deprecation и Sunset, ждут, пока старые клиенты уйдут, и только потом удаляют. Новая версия /v2 — для крупной переделки, а не ради одного поля.
""")

task(
    "be-api-design-01", "quiz", 3, "manager",
    "Стас: «Мобильщики жалуются: после наших релизов у части пользователей падает приложение. Вот что мы хотим поменять в API заказов в этом месяце. Скажи, что из этого сломает старые версии — их обновляют месяцами».",
    "Какие изменения в /v1/orders ломающие для уже установленных версий приложения? Выбери все верные.",
    "Ломающее изменение — любое, после которого старый клиент, написанный по прежнему контракту, получает не то, что ожидает, или получает отказ. Переименование total ломает всех, кто читает total. Обязательное поле в запросе ломает старые версии, которые его не присылают: они начнут получать 422. Смена типа id с числа на строку ломает парсинг и типы в клиенте. Новое поле в ответе, новый эндпоинт и новый необязательный параметр — расширения: старые клиенты их просто не используют, если следуют правилу «незнакомые поля игнорируем». Осторожнее с новыми значениями enum в ответе: строгий клиент может на них упасть, поэтому это согласуют отдельно.",
    ["Представь приложение полугодовой давности: какие из этих изменений оно заметит?",
     "Расширение контракта безопасно, изменение или сужение — нет.",
     "Опасно всё, что меняет уже существующие поля или ужесточает требования к запросу."],
    options=[
        "Добавить в ответ заказа новое поле delivery_slot",
        "Переименовать поле total в total_amount",
        "Сделать поле comment в теле POST /v1/orders обязательным (сейчас оно необязательное)",
        "Сменить тип id заказа с числа на строку (UUID)",
        "Добавить эндпоинт GET /v1/orders/{id}/receipt",
        "Добавить в POST /v1/orders необязательный параметр promo_code",
    ],
    answer=[1, 2, 3],
)

API_PAGE_BUG = C('''
def fetch_page(orders, limit, cursor=None):
    # orders — все заказы клиента (строки таблицы).
    # Новые сверху. cursor — строка, которую клиент присылает,
    # чтобы получить следующую страницу.
    rows = sorted(orders, key=lambda o: o["created_at"], reverse=True)
    if cursor is not None:
        rows = [o for o in rows if o["created_at"] < cursor]
    page = rows[:limit]
    next_cursor = None
    if len(rows) > limit:
        next_cursor = page[-1]["created_at"]
    return {"items": [o["id"] for o in page], "next_cursor": next_cursor}


def collect_all(orders, limit):
    # так мобильное приложение листает ленту до конца — не меняй
    seen = []
    cursor = None
    while True:
        page = fetch_page(orders, limit, cursor)
        seen.extend(page["items"])
        cursor = page["next_cursor"]
        if cursor is None:
            return seen
''')

API_PAGE_FIX = C('''
def fetch_page(orders, limit, cursor=None):
    # orders — все заказы клиента (строки таблицы).
    # Новые сверху, при равном времени — больший id выше.
    # cursor — "created_at|id" последней записи предыдущей страницы.
    rows = sorted(orders, key=lambda o: (o["created_at"], o["id"]), reverse=True)
    if cursor is not None:
        last_created, last_id = cursor.split("|")
        last = (last_created, int(last_id))
        rows = [o for o in rows if (o["created_at"], o["id"]) < last]
    page = rows[:limit]
    next_cursor = None
    if len(rows) > limit:
        tail = page[-1]
        created = tail["created_at"]
        tail_id = tail["id"]
        next_cursor = f"{created}|{tail_id}"
    return {"items": [o["id"] for o in page], "next_cursor": next_cursor}


def collect_all(orders, limit):
    # так мобильное приложение листает ленту до конца — не меняй
    seen = []
    cursor = None
    while True:
        page = fetch_page(orders, limit, cursor)
        seen.extend(page["items"])
        cursor = page["next_cursor"]
        if cursor is None:
            return seen
''')


def _o(i, t):
    return {"id": i, "created_at": t}


task(
    "be-api-design-02", "find_bug", 4, "qa",
    "Ира: «В приложении в истории заказов при прокрутке пропадают заказы. Воспроизвела на стейдже: у клиента 7 заказов, листаю по 3 — вижу только 5. Пропадают те, что прилетели пачкой из импорта с маркетплейса: у них одинаковое время создания».",
    "Исправь fetch_page так, чтобы collect_all возвращал все заказы без пропусков и дублей в порядке: новые сверху, при одинаковом created_at — больший id выше. Формат курсора выбирай сам, collect_all не меняй.",
    "Курсор хранил только created_at, а фильтр был строгим: created_at < cursor. Если граница страницы проходит внутри группы заказов с одинаковым временем, все оставшиеся заказы этой группы отсекаются — они не меньше курсора. Лечится уникальным ключом сортировки: (created_at, id). В курсор кладём обе части, сортируем по обеим и берём строки, у которых кортеж строго меньше. В PostgreSQL это WHERE (created_at, id) < ($1, $2) ORDER BY created_at DESC, id DESC и индекс (user_id, created_at DESC, id DESC). Заодно порядок внутри группы становится детерминированным: без id в ORDER BY база вправе отдавать одинаковые по времени строки в любом порядке, и они будут прыгать между страницами.",
    ["Посмотри, что происходит с заказами, у которых created_at равен курсору.",
     "Одного created_at мало, чтобы однозначно указать место в ленте. Что уникально у каждого заказа?",
     "Сортируй по кортежу (created_at, id), клади в курсор обе части и фильтруй по (created_at, id) < курсора."],
    code=API_PAGE_BUG, language="python", entry="collect_all",
    answer=API_PAGE_FIX,
    tests=[
        {"input": [[_o(101, "2026-09-20T10:00:00"), _o(102, "2026-09-21T09:30:00"),
                    _o(103, "2026-09-21T09:30:00"), _o(104, "2026-09-21T09:30:00"),
                    _o(105, "2026-09-22T18:15:00"), _o(106, "2026-09-19T08:00:00"),
                    _o(107, "2026-09-21T09:30:00")], 3],
         "expected": [105, 107, 104, 103, 102, 101, 106]},
        {"input": [[_o(1, "2026-09-01T12:00:00"), _o(2, "2026-09-01T12:00:00"),
                    _o(3, "2026-09-01T12:00:00"), _o(4, "2026-09-01T12:00:00"),
                    _o(5, "2026-09-01T12:00:00")], 2],
         "expected": [5, 4, 3, 2, 1]},
        {"input": [[_o(7, "2026-09-03T10:00:00"), _o(8, "2026-09-01T10:00:00"),
                    _o(9, "2026-09-02T10:00:00")], 10],
         "expected": [7, 9, 8]},
        {"input": [[], 5], "expected": []},
    ],
)

IDEM_STARTER = C('''
def create_order(db, user_id, body):
    # INSERT INTO orders (...) RETURNING id — настоящая запись в базу
    order_id = len(db["orders"]) + 1
    db["orders"].append({"id": order_id, "user_id": user_id, "items": body["items"]})
    return order_id


def handle_post_order(db, idem_store, user_id, key, body):
    # POST /orders с заголовком Idempotency-Key.
    # Верни [HTTP-статус, order_id или None].
    # idem_store — словарь, где ты хранишь всё нужное между запросами.
    # твой код
    pass


def run(requests):
    # Тестовый стенд, не меняй: прогоняет запросы по очереди.
    # requests — список [user_id, idempotency_key или None, тело запроса]
    db = {"orders": []}
    idem_store = {}
    responses = []
    for user_id, key, body in requests:
        responses.append(handle_post_order(db, idem_store, user_id, key, body))
    return {"responses": responses, "orders_in_db": len(db["orders"])}
''')

IDEM_FIX = C('''
def create_order(db, user_id, body):
    # INSERT INTO orders (...) RETURNING id — настоящая запись в базу
    order_id = len(db["orders"]) + 1
    db["orders"].append({"id": order_id, "user_id": user_id, "items": body["items"]})
    return order_id


def handle_post_order(db, idem_store, user_id, key, body):
    if not key:
        return [400, None]
    store_key = (user_id, key)
    saved = idem_store.get(store_key)
    if saved is not None:
        if saved["body"] != body:
            return [422, None]
        return saved["response"]
    order_id = create_order(db, user_id, body)
    response = [201, order_id]
    idem_store[store_key] = {"body": body, "response": response}
    return response


def run(requests):
    # Тестовый стенд, не меняй: прогоняет запросы по очереди.
    # requests — список [user_id, idempotency_key или None, тело запроса]
    db = {"orders": []}
    idem_store = {}
    responses = []
    for user_id, key, body in requests:
        responses.append(handle_post_order(db, idem_store, user_id, key, body))
    return {"responses": responses, "orders_in_db": len(db["orders"])}
''')

task(
    "be-api-design-03", "write_code", 4, "teamlead",
    "Гена: «Мобилка ретраит POST /orders при плохой сети, а наш же балансировщик — при таймауте. Вчера клиент получил три одинаковых заказа и три списания. Сделай создание заказа идемпотентным по заголовку Idempotency-Key — приложение уже шлёт UUID на каждую попытку оформления».",
    "Реализуй handle_post_order. Правила: нет ключа (None или пустая строка) — [400, None], заказ не создаётся; новый ключ — создать заказ и вернуть [201, order_id]; повтор того же ключа тем же пользователем с тем же телом — вернуть сохранённый ответ, не создавая заказ; тот же ключ с другим телом — [422, None]; ключи разных пользователей друг другу не мешают.",
    "Идемпотентность POST строится на том, что сервер помнит ключ вместе с результатом. Повтор с тем же ключом получает тот же ответ (тот же order_id и статус), поэтому клиенту не важно, дошёл ли первый ответ. Тело сравниваем, чтобы поймать ошибку клиента: один ключ на две разные операции — это 422, а не тихая подмена. Ключ хранится в паре с user_id: UUID генерируют клиенты, и совпадение у разных пользователей не должно отдавать чужой заказ. В проде вместо словаря — таблица с уникальным индексом (user_id, key), отпечатком тела (хеш) и ответом; запись ключа делают в той же транзакции, что и заказ, или сначала вставляют ключ со статусом «в обработке» через INSERT ... ON CONFLICT DO NOTHING — тогда два параллельных одинаковых запроса не создадут два заказа, а второй получит 409. Ключи чистят по TTL, обычно через сутки.",
    ["Что нужно запомнить после первого успешного запроса, чтобы ответить на повтор?",
     "Храни по ключу (user_id, key) тело запроса и готовый ответ.",
     "Порядок проверок: нет ключа → 400; ключ уже есть → сравни тело (422 или сохранённый ответ); иначе создай заказ и запомни."],
    code=IDEM_STARTER, language="python", entry="run",
    answer=IDEM_FIX,
    tests=[
        {"input": [[[1, "a1f3", {"items": [10]}], [1, "a1f3", {"items": [10]}], [1, "a1f3", {"items": [10]}]]],
         "expected": {"responses": [[201, 1], [201, 1], [201, 1]], "orders_in_db": 1}},
        {"input": [[[1, "k-77", {"items": [10]}], [1, "k-77", {"items": [10, 11]}]]],
         "expected": {"responses": [[201, 1], [422, None]], "orders_in_db": 1}},
        {"input": [[[1, None, {"items": [3]}], [1, "", {"items": [3]}]]],
         "expected": {"responses": [[400, None], [400, None]], "orders_in_db": 0}},
        {"input": [[[1, "same", {"items": [5]}], [2, "same", {"items": [5]}], [1, "same", {"items": [5]}]]],
         "expected": {"responses": [[201, 1], [201, 2], [201, 1]], "orders_in_db": 2}},
        {"input": [[[1, "k1", {"items": [1]}], [1, "k2", {"items": [1]}]]],
         "expected": {"responses": [[201, 1], [201, 2]], "orders_in_db": 2}},
    ],
)

task(
    "be-api-design-04", "architecture", 5, "manager",
    "Стас: «Бухгалтерия требует отдавать цены в копейках и с валютой: из-за float в чеках уже вылезли расхождения на копейку. Сделай, чтобы у мобилки ничего не сломалось, но и зоопарк версий API не разводи».",
    "Как провести это изменение в API каталога? Выбери одно решение.",
    "Смена типа поля price — ломающее изменение: старые версии приложения ждут число и упадут на объекте. Принудительное обновление не спасает: 24% пользователей увидят экран «обновитесь» вместо каталога, а у 6% на версиях без этого механизма приложение просто упадёт. Отдельная /v2 ради одного поля удваивает код, тесты и документацию на годы, при этом остальные эндпоинты в ней не меняются. Определение формата по User-Agent — скрытое версионирование: ломается на прокси и кэшах, не видно в контракте, и через год никто не вспомнит, откуда ветки в коде. Строка «1490.00 RUB» тоже ломает старых клиентов и заставляет новых парсить текст. Правильный путь — расширение контракта (expand/contract): новое поле price_money рядом со старым, старое помечено deprecated и заголовками Deprecation/Sunset, а удаляют его по данным аналитики, когда доля старых версий упадёт ниже порога, согласованного с продуктом.",
    ["Какие из вариантов ломают версии, которые уже установлены у пользователей?",
     "Можно ли дать новым клиентам новый формат, не отнимая старый у старых?",
     "Ищи вариант, который добавляет, а не меняет, и заранее планирует удаление старого поля."],
    code=C('''
GET /v1/products/812            # сейчас
{"id": 812, "title": "Лампа Эдисона E27", "price": 1490.0}

# что хотят новые клиенты
{"price": {"amount": 149000, "currency": "RUB"}}

# версии приложения за 30 дней (iOS + Android)
5.x (текущая)        70%
4.x                  24%
3.x и старше          6%   # принудительного обновления там нет
'''),
    language="text",
    options=[
        "Добавить рядом price_money: {amount, currency}, а price пометить deprecated (Deprecation/Sunset) и убрать по данным аналитики",
        "Поменять price на объект прямо в /v1 и включить в приложении принудительное обновление для всех версий ниже 5.0",
        "Выпустить /v2 для всего API: скопировать все роутеры, в /v2 отдавать новый формат и поддерживать обе версии параллельно",
        "Определять версию приложения по User-Agent и отдавать price в старом или новом формате в зависимости от этой версии",
        "Отдавать price строкой \"1490.00 RUB\": число и валюту в одном поле поймут и старые, и новые версии клиентов",
    ],
    answer=0,
)


# =====================================================================
# be-indexes
# =====================================================================
topic("be-indexes", """
Индекс — отдельная структура, по которой база находит строки, не читая всю таблицу. В PostgreSQL по умолчанию это B-tree: отсортированное дерево значений со ссылками на строки. Он ускоряет =, <, >, BETWEEN, IN, ORDER BY и LIKE 'abc%', но каждый индекс замедляет INSERT/UPDATE/DELETE и занимает место. Индексы создают под конкретные запросы, а не «на всякий случай».

EXPLAIN показывает план, EXPLAIN ANALYZE ещё и выполняет запрос и выводит реальное время (UPDATE/DELETE оборачивай в BEGIN ... ROLLBACK). BUFFERS показывает, сколько страниц взято из кэша (hit) и прочитано с диска (read); с PostgreSQL 18 EXPLAIN ANALYZE выводит их сам, в старых версиях пиши EXPLAIN (ANALYZE, BUFFERS). Что смотреть:
• Seq Scan — проход по всей таблице. Для горячего запроса к большой таблице — тревожный знак.
• Index Scan / Index Only Scan — поиск по индексу; Only — даже без чтения самой таблицы.
• Bitmap Heap Scan — индекс отобрал много строк, их читают пачкой.
• rows в оценке против actual rows — расхождение в сотни раз означает устаревшую статистику (ANALYZE).
• Rows Removed by Filter — сколько строк прочитали зря. Миллионы — индекс не подходит под условие.
• Sort Method: external merge — сортировка не влезла в work_mem и ушла на диск.

Составной индекс (a, b, c) отсортирован по a, внутри — по b, потом по c. Он работает для левого префикса: a; a и b; a, b и c. Порядок колонок: сначала те, что сравниваются на равенство, потом колонка диапазона или сортировки:

    -- WHERE seller_id = ? AND status = ? ORDER BY created_at DESC LIMIT 50
    CREATE INDEX ON orders (seller_id, status, created_at DESC);

Условие только на b такой индекс почти не ускорит (skip scan из PostgreSQL 18 помогает, лишь когда в a мало разных значений). А индекс (created_at, seller_id) под этот запрос заставит перебирать все свежие заказы подряд.

Почему индекс не используется:
• функция или приведение типа над колонкой: lower(email) = ..., date(created_at) = ..., id::text = .... Лечится диапазоном (created_at >= '2026-09-20' AND created_at < '2026-09-21') или индексом по выражению: CREATE INDEX ON users (lower(email));
• LIKE '%abc' и '%abc%': B-tree ищет только по началу строки. Для подстроки нужен GIN-индекс с pg_trgm (gin_trgm_ops), для хвоста — отдельная колонка или индекс по выражению;
• LIKE 'abc%' при локали базы не C работает только с индексом text_pattern_ops;
• низкая селективность: если условие отбирает треть таблицы, Seq Scan честно дешевле;
• «не равно» (<>, NOT IN) B-tree не ускоряет.

Частичный индекс хранит только нужные строки: CREATE INDEX ON orders (created_at) WHERE status = 'new'. Неиспользуемые индексы ищут в pg_stat_user_indexes (idx_scan = 0) и удаляют.

На проде индекс на большой таблице создают только так:

    CREATE INDEX CONCURRENTLY orders_user_created_idx ON orders (user_id, created_at DESC);

Обычный CREATE INDEX берёт блокировку, при которой чтение идёт, а INSERT/UPDATE/DELETE ждут до конца построения — на десятках миллионов строк это минуты простоя. CONCURRENTLY строится дольше, не работает внутри транзакции (в Alembic нужен autocommit_block), а при ошибке оставляет индекс INVALID — его удаляют и создают заново.
""")

task(
    "be-indexes-01", "find_bug", 3, "qa",
    "Ира: «Отчёт „Заказы за день“ в админке открывается 40 секунд, бухгалтерия уже шутит, что успевает сходить за кофе. Индекс на created_at есть, я сама видела его в миграции».",
    "Почему индекс не помогает и как правильно исправить запрос?",
    "Индекс orders_created_at_idx хранит отсортированные значения created_at, а условие сравнивает результат функции date(created_at). Искать по B-tree можно только по тому, что в нём лежит, поэтому планировщик читает всю таблицу и считает функцию для каждой из 22,5 млн строк — это видно по Parallel Seq Scan и Rows Removed by Filter. Диапазон created_at >= '2026-09-20' AND created_at < '2026-09-21' — то же самое по смыслу, но колонка стоит «голой», и индекс работает. Другой вариант — индекс по выражению ((created_at::date)), но это лишний индекс на запись, а для timestamptz он вообще невозможен: приведение к дате зависит от часового пояса. ANALYZE и REINDEX не помогут — индекс не битый и статистика ни при чём, enable_seqscan = off не научит план использовать неподходящий индекс, а LIMIT обрежет отчёт.",
    ["Сравни, что лежит в индексе и что написано в WHERE.",
     "Может ли B-tree по created_at найти строки по значению date(created_at)?",
     "Перепиши условие так, чтобы колонка стояла без функции: полуинтервал от начала дня до начала следующего."],
    code=C('''
shop=> \\d orders
   Column   |            Type             | Nullable
------------+-----------------------------+----------
 id         | bigint                      | not null
 user_id    | bigint                      | not null
 total      | numeric(12,2)               | not null
 created_at | timestamp without time zone | not null
Indexes:
    "orders_pkey" PRIMARY KEY, btree (id)
    "orders_created_at_idx" btree (created_at)

shop=> EXPLAIN ANALYZE
shop-> SELECT id, total FROM orders WHERE date(created_at) = '2026-09-20';
                                   QUERY PLAN
--------------------------------------------------------------------------------
 Gather  (cost=1000.00..812345.10 rows=112643 width=16) (actual time=4.113..38911.207 rows=23817 loops=1)
   Workers Planned: 2
   Workers Launched: 2
   ->  Parallel Seq Scan on orders  (cost=0.00..800080.80 rows=46935 width=16) (actual time=3.870..38874.442 rows=7939 loops=3)
         Filter: (date(created_at) = '2026-09-20'::date)
         Rows Removed by Filter: 7501457
 Planning Time: 0.112 ms
 Execution Time: 38932.580 ms
'''),
    language="text",
    options=[
        "Индексу мешает date() над колонкой: переписать на created_at >= '2026-09-20' AND created_at < '2026-09-21'",
        "Индекс orders_created_at_idx повреждён после сбоя — нужно выполнить REINDEX INDEX CONCURRENTLY orders_created_at_idx",
        "Статистика устарела: планировщик ждёт 112 тысяч строк вместо 24 — выполнить ANALYZE orders, и он выберет индекс",
        "Запретить последовательное сканирование: SET enable_seqscan = off перед запросом отчёта, и база возьмёт индекс",
        "Добавить в отчёт LIMIT 1000 и пагинацию, чтобы Seq Scan заканчивался раньше и страница открывалась быстро",
    ],
    answer=0,
)

task(
    "be-indexes-02", "architecture", 4, "teamlead",
    "Марина: «Крупные продавцы маркетплейса жалуются: лента оплаченных заказов в кабинете открывается по две секунды. План ниже. Предложи один индекс — в orders пишут тысячи раз в минуту, лишние индексы нам дорого обходятся».",
    "Какой индекс решит проблему этого запроса? Выбери один вариант.",
    "Сейчас планировщик идёт по индексу created_at с конца и для каждой строки проверяет продавца и статус: чтобы набрать 50 подходящих, он прочитал и выбросил 2,4 млн строк (Rows Removed by Filter) и 160 тысяч страниц с диска. Индекс (seller_id, status, created_at DESC) устроен ровно под запрос: колонки с равенством идут первыми, поэтому все оплаченные заказы продавца 42 лежат в индексе одним непрерывным куском, и внутри него они уже отсортированы по времени — база прочитает 50 записей и остановится. Индекс с created_at на первом месте повторяет нынешнюю проблему: перебор свежих заказов всех продавцов. (status, created_at) отбирает все оплаченные заказы маркетплейса — снова фильтр по продавцу. Два одиночных индекса через BitmapAnd найдут все 210 тысяч оплаченных заказов продавца, а потом их придётся сортировать ради 50 строк, и индексов на запись станет два. work_mem тут ни при чём: в плане нет сортировки, проблема в чтении лишних строк. DESC в индексе не обязателен — B-tree читается и в обратную сторону, но он делает намерение явным.",
    ["Посмотри на Rows Removed by Filter: сколько строк прочитано зря?",
     "Правило составного индекса: сначала колонки, сравниваемые на равенство, потом колонка сортировки.",
     "Нужен индекс, где заказы одного продавца с одним статусом лежат подряд и уже отсортированы по времени."],
    code=C('''
shop=> EXPLAIN (ANALYZE, BUFFERS)
shop-> SELECT id, total, created_at FROM orders
shop-> WHERE seller_id = 42 AND status = 'paid'
shop-> ORDER BY created_at DESC
shop-> LIMIT 50;
                                   QUERY PLAN
--------------------------------------------------------------------------------
 Limit  (cost=0.56..2210.71 rows=50 width=24) (actual time=0.061..1874.330 rows=50 loops=1)
   Buffers: shared hit=48211 read=161940
   ->  Index Scan Backward using orders_created_at_idx on orders  (cost=0.56..9314262.11 rows=210700 width=24) (actual time=0.060..1874.301 rows=50 loops=1)
         Filter: ((seller_id = 42) AND (status = 'paid'::text))
         Rows Removed by Filter: 2381554
         Buffers: shared hit=48211 read=161940
 Planning Time: 0.190 ms
 Execution Time: 1874.402 ms

-- orders: 40 млн строк, продавцов ~9 000, статусов 6
-- индексы: orders_pkey (id), orders_created_at_idx (created_at)
'''),
    language="text",
    options=[
        "CREATE INDEX CONCURRENTLY ON orders (created_at DESC, seller_id, status)",
        "CREATE INDEX CONCURRENTLY ON orders (seller_id, status, created_at DESC)",
        "Два индекса: (seller_id) и (status) — планировщик объединит их через BitmapAnd",
        "CREATE INDEX CONCURRENTLY ON orders (status, created_at DESC)",
        "Индекс не нужен: поднять work_mem, чтобы сортировка шла в памяти",
    ],
    answer=1,
)

task(
    "be-indexes-03", "code_review", 4, "teamlead",
    "Гена: «Джун принёс PR „ускоряем админку колл-центра“: поиск клиента по хвосту телефона и по e-mail, счётчик на дашборде и три индекса под них. В customers 12 млн строк, в orders 40 млн, из них 95% уже доставлены. Посмотри, что реально надо поправить, я весь день на созвонах».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Все три индекса из PR запросы не ускорят. LIKE '%' || :digits начинается с подстановочного знака, а B-tree ищет только по началу строки — будет полный проход по 12 млн клиентов; нужна колонка с последними цифрами телефона (или индекс по выражению right(phone, 4)) либо GIN-индекс pg_trgm. Условие lower(email) = ... не использует индекс по email — индекс должен быть построен по тому же выражению: CREATE INDEX ... ON customers (lower(email)), либо e-mail нормализуют при записи. Индекс по status для status <> 'delivered' бесполезен: «не равно» B-tree не ускоряет, а шесть значений на 40 млн строк — низкая селективность; такой индекс только замедлит каждую запись в orders. Для счётчика подойдёт частичный индекс WHERE status <> 'delivered' — он маленький, ведь незакрытых заказов 5%. UNIQUE не меняет того, как ищет LIKE; сортировка по первичному ключу для 20 строк дешёвая и делает выдачу стабильной; кэша результатов запросов в PostgreSQL нет; а без CONCURRENTLY построение индекса заблокирует запись в таблицу на всё время построения.",
    ["Для каждого запроса спроси: может ли индекс из миграции найти строки по такому условию?",
     "B-tree умеет искать по началу строки и по точному значению колонки, но не по функции от неё.",
     "Подумай, сколько строк отберёт status <> 'delivered' и что B-tree делает с «не равно»."],
    code=C('''
-- migrations/0147_admin_search.sql
CREATE INDEX CONCURRENTLY customers_phone_idx ON customers (phone);
CREATE INDEX CONCURRENTLY customers_email_idx ON customers (email);
CREATE INDEX CONCURRENTLY orders_status_idx ON orders (status);

-- queries/admin_search.sql

-- name: search_by_phone_tail
-- оператор колл-центра вводит последние 4 цифры телефона
SELECT id, full_name, phone FROM customers
WHERE phone LIKE '%' || :digits
ORDER BY id DESC
LIMIT 20;

-- name: find_by_email
-- e-mail ищем без учёта регистра
SELECT id, full_name, email FROM customers
WHERE lower(email) = lower(:email);

-- name: open_orders_count
-- счётчик «незакрытые заказы» на дашборде
SELECT count(*) FROM orders WHERE status <> 'delivered';
'''),
    language="sql",
    options=[
        "LIKE '%' || :digits начинается с %, B-tree по phone не поможет. Нужна колонка или индекс по хвосту телефона либо pg_trgm",
        "Индекс по email не подходит под lower(email) = … — нужен индекс по lower(email) или хранить e-mail в нижнем регистре",
        "orders_status_idx не ускорит status <> 'delivered' и замедлит запись; для счётчика — частичный индекс WHERE status <> 'delivered'",
        "customers_phone_idx надо сделать UNIQUE: уникальный индекс планировщик выбирает охотнее, и LIKE начнёт его использовать",
        "ORDER BY id DESC в поиске по телефону заставит сортировать все совпадения — сортировку лучше убрать, порядок не важен",
        "Вместо трёх индексов проще включить кэш результатов запросов в postgresql.conf — повторные поиски будут мгновенными",
        "CONCURRENTLY лишнее: без него индексы построятся вдвое быстрее, а блокировка на время построения займёт пару секунд",
    ],
    answer=[0, 1, 2],
)

task(
    "be-indexes-04", "incident", 5, "devops_colleague",
    "Дима: «С 14:02 на проде не оформляются заказы: в Sentry пачка таймаутов на INSERT и UPDATE в orders, при этом каталог и поиск открываются. В 14:00 выкатили релиз — в нём только миграция с новым индексом. Что делаем?»",
    "Выбери все правильные действия: что сделать прямо сейчас и как выкатить индекс, чтобы это не повторилось.",
    "Обычный CREATE INDEX берёт на таблицу блокировку SHARE: чтение с ней совместимо (поэтому каталог жив), а INSERT, UPDATE и DELETE ждут до конца построения. На 38 ГБ это десятки минут, и всё это время заказы теряются, поэтому ждать нельзя. pg_cancel_backend отменяет построение, транзакция миграции откатывается, блокировка снимается — запросы из очереди сразу проходят. Перезапуск подов не поможет: блокировку держит сессия миграции, а не приложение. max_connections лишь добавит ожидающих, а VACUUM FULL берёт блокировку ACCESS EXCLUSIVE и остановит даже чтение. Повторно индекс строят через CREATE INDEX CONCURRENTLY: он не блокирует запись, но не может выполняться в транзакции, поэтому в Alembic нужен autocommit_block. Если такой запуск упадёт, останется индекс со статусом INVALID — его удаляют через DROP INDEX CONCURRENTLY и строят заново. Чтобы история не повторилась, миграции проверяют линтером (например, squawk) ещё в CI.",
    ["Кто держит блокировку и кто её ждёт? Посмотри на wait в pg_stat_activity.",
     "Какую блокировку берёт обычный CREATE INDEX и что с ней несовместимо?",
     "Сейчас — снять блокировку, отменив построение. Потом — строить индекс так, чтобы запись не останавливалась."],
    code=C('''
$ kubectl logs deploy/market-api --since=10m | grep ERROR | tail -3
14:08:41 ERROR POST /orders 504 upstream timeout after 30.0s (INSERT INTO orders ...)
14:08:43 ERROR POST /payments/callback 504 upstream timeout after 30.0s (UPDATE orders SET status ...)
14:08:44 ERROR POST /orders 504 upstream timeout after 30.0s (INSERT INTO orders ...)

$ grep -A2 "def upgrade" migrations/versions/0148_orders_user_created_idx.py
def upgrade():
    op.create_index("orders_user_created_idx", "orders", ["user_id", "created_at"])

shop=# SELECT pid, now() - query_start AS running, wait_event_type AS wait, left(query, 55) AS query
shop-#   FROM pg_stat_activity WHERE state = 'active' ORDER BY query_start LIMIT 4;
  pid  | running  | wait |                          query
-------+----------+------+---------------------------------------------------------
 71502 | 00:08:52 | IO   | CREATE INDEX orders_user_created_idx ON orders (user_id
 71633 | 00:06:11 | Lock | INSERT INTO orders (user_id, status, total, created_at)
 71640 | 00:06:09 | Lock | UPDATE orders SET status = 'paid' WHERE id = 55120931
 71652 | 00:06:02 | Lock | INSERT INTO orders (user_id, status, total, created_at)
(4 rows)
-- всего в ожидании Lock: 214 запросов

shop=# SELECT pg_size_pretty(pg_relation_size('orders'));
 pg_size_pretty
----------------
 38 GB
'''),
    language="text",
    options=[
        "Отменить построение: SELECT pg_cancel_backend(71502) — транзакция миграции откатится, и блокировка снимется",
        "Переписать миграцию на CREATE INDEX CONCURRENTLY (postgresql_concurrently=True в autocommit_block) и выкатить заново",
        "Подождать: индекс строится уже девятую минуту, скоро достроится, и очередь запросов рассосётся сама",
        "Перезапустить поды market-api, чтобы сбросить зависшие соединения и освободить пул для новых заказов",
        "Поднять max_connections в PostgreSQL, чтобы новым запросам хватало соединений, пока строится индекс",
        "Выполнить VACUUM FULL orders: таблица сожмётся, и индекс по 38 ГБ достроится в разы быстрее",
    ],
    answer=[0, 1],
    time_limit=15,
)


# =====================================================================
# be-transactions
# =====================================================================
topic("be-transactions", """
Транзакция — группа операций, которая выполняется целиком или никак. ACID: атомарность (всё или ничего), согласованность (ограничения базы соблюдены), изоляция (параллельные транзакции не мешают друг другу — насколько позволяет уровень), долговечность (после COMMIT данные не пропадут даже при падении сервера).

Аномалии параллельной работы:
• грязное чтение — видим чужие незакоммиченные данные (в PostgreSQL не бывает ни на каком уровне);
• неповторяемое чтение — прочитали строку дважды и получили разные значения: между чтениями кто-то закоммитил UPDATE;
• фантом — повторный запрос с тем же WHERE вернул новые строки;
• потерянное обновление (lost update) — две транзакции прочитали balance = 500, каждая посчитала своё и записала, одно изменение затёрлось;
• write skew — две транзакции проверили одно условие («на дежурстве больше одного админа») и каждая изменила свою строку, вместе нарушив его.

Уровни изоляции в PostgreSQL:
• READ COMMITTED (по умолчанию) — каждый запрос видит данные, закоммиченные к его началу. Возможны неповторяемое чтение, фантомы и lost update, если код читает, считает и записывает.
• REPEATABLE READ — вся транзакция видит один снимок, фантомов в PostgreSQL тоже нет. Если строку, которую ты обновляешь, после начала твоей транзакции изменила и закоммитила другая, получишь could not serialize access (SQLSTATE 40001).
• SERIALIZABLE — результат как при последовательном выполнении, ловит и write skew. Цена — ошибки сериализации, которые ретраят всей транзакцией.

Уровень задают на транзакцию: SET TRANSACTION ISOLATION LEVEL REPEATABLE READ. READ UNCOMMITTED в PostgreSQL работает как READ COMMITTED.

Как не терять обновления. Если логика умещается в SQL, лучший вариант — атомарный UPDATE; 0 обновлённых строк значит, что товара не хватило:

    UPDATE products SET stock = stock - 2 WHERE id = 812 AND stock >= 2;

Пессимистичная блокировка: SELECT ... FOR UPDATE блокирует прочитанные строки до конца транзакции, конкуренты ждут. NOWAIT — не ждать, а сразу получить ошибку; SKIP LOCKED — пропускать занятые строки (так устроены очереди задач в базе).

Оптимистичная блокировка: в таблице колонка version, и запись проходит, только если версия не изменилась с момента чтения. 0 обновлённых строк — кто-то успел раньше: перечитай и повтори или верни пользователю 409. Подходит, когда конфликты редки, а держать блокировку долго не хочется (в SQLAlchemy — version_id_col):

    UPDATE wallets SET balance = :new_balance, version = version + 1
    WHERE id = :id AND version = :read_version;

Дедлок: транзакция A держит строку 1 и ждёт строку 2, а B держит 2 и ждёт 1. Через deadlock_timeout (по умолчанию 1 с) PostgreSQL находит цикл и отменяет одну из транзакций с ошибкой deadlock detected (SQLSTATE 40P01). Лечение: брать блокировки в одном порядке (например, по возрастанию id), держать транзакции короткими, ретраить 40P01 и 40001 всей транзакцией.

Частые ошибки:
• HTTP-запросы и отправка писем внутри открытой транзакции: блокировки висят секундами, а письмо уйдёт, даже если COMMIT упадёт;
• забытый commit — сессия висит в состоянии idle in transaction и держит блокировки;
• «сначала SELECT, потом INSERT» без блокировки и уникального ограничения — два параллельных запроса одновременно пройдут проверку.
""")

task(
    "be-transactions-01", "quiz", 3, "teamlead",
    "Гена: «Ночной отчёт по складу иногда не сходится: в шапке 1 000 товаров, а сумма по категориям внизу — 998. Отчёт — это десяток запросов подряд, а днём в это время идёт выгрузка остатков. Кто-то предложил включить REPEATABLE READ. Прежде чем включать, проверим, что ты понимаешь, как он работает в PostgreSQL».",
    "Какие утверждения про REPEATABLE READ в PostgreSQL верны? Выбери все верные.",
    "На REPEATABLE READ транзакция берёт снимок данных при первом запросе и видит его до конца, поэтому все запросы отчёта согласованы между собой — именно это и чинит расхождение в отчёте Гены. Реализация PostgreSQL строже стандарта: снимок защищает и от фантомов, хотя SQL-стандарт на этом уровне их допускает. Плата за снимок — конфликты при записи: если строку, которую ты обновляешь, уже изменила и закоммитила параллельная транзакция, PostgreSQL выдаст ошибку сериализации 40001, и повторять нужно всю транзакцию, а не один запрос. От write skew защищает только SERIALIZABLE. Грязного чтения в PostgreSQL не бывает вообще, а уровень изоляции задаётся для каждой транзакции отдельно, например SET TRANSACTION ISOLATION LEVEL REPEATABLE READ или параметром сессии в SQLAlchemy.",
    ["Что именно видит транзакция на этом уровне: последние закоммиченные данные или снимок?",
     "Вспомни, от какой аномалии защищает только SERIALIZABLE.",
     "Верных утверждений три: про снимок, про фантомы и про ошибку при конкурентном обновлении."],
    options=[
        "Все запросы транзакции видят один снимок, взятый при первом запросе: чужие коммиты посреди отчёта не видны",
        "В PostgreSQL на этом уровне не бывает и фантомов, хотя стандарт SQL их здесь допускает",
        "UPDATE строки, которую успела изменить и закоммитить параллельная транзакция, падает с ошибкой сериализации 40001",
        "Уровень защищает от любых аномалий параллельной записи, включая write skew, — SERIALIZABLE нужен только для отчётов",
        "На этом уровне транзакция может прочитать незакоммиченные изменения другой транзакции, если та ещё не завершилась",
        "Уровень изоляции задаётся один раз на всю базу в postgresql.conf и не меняется для отдельных транзакций",
    ],
    answer=[0, 1, 2],
)

WALLET_STARTER = C('''
def read_wallet(db):
    # SELECT balance, version FROM wallets WHERE user_id = :uid
    snapshot = {"balance": db["balance"], "version": db["version"]}
    # Имитация гонки: пока мы считаем, другой воркер успевает
    # закоммитить своё изменение этого же кошелька.
    if db["races"]:
        delta = db["races"].pop(0)
        db["balance"] += delta
        db["version"] += 1
    return snapshot


def save_wallet(db, new_balance, expected_version):
    # UPDATE wallets SET balance = :new_balance, version = version + 1
    # WHERE user_id = :uid AND version = :expected_version
    # Верни rowcount: 1 — записали, 0 — версия уже другая.
    # твой код
    pass


def charge(db, amount, max_attempts):
    # Списать amount бонусов. Верни "ok", "insufficient" или "conflict".
    # твой код
    pass


def simulate(balance, races, amount, max_attempts):
    # Тестовый стенд, не меняй.
    db = {"balance": balance, "version": 1, "races": list(races)}
    result = charge(db, amount, max_attempts)
    return {"result": result, "balance": db["balance"], "version": db["version"]}
''')

WALLET_FIX = C('''
def read_wallet(db):
    # SELECT balance, version FROM wallets WHERE user_id = :uid
    snapshot = {"balance": db["balance"], "version": db["version"]}
    # Имитация гонки: пока мы считаем, другой воркер успевает
    # закоммитить своё изменение этого же кошелька.
    if db["races"]:
        delta = db["races"].pop(0)
        db["balance"] += delta
        db["version"] += 1
    return snapshot


def save_wallet(db, new_balance, expected_version):
    # UPDATE wallets SET balance = :new_balance, version = version + 1
    # WHERE user_id = :uid AND version = :expected_version
    if db["version"] != expected_version:
        return 0
    db["balance"] = new_balance
    db["version"] += 1
    return 1


def charge(db, amount, max_attempts):
    for _ in range(max_attempts):
        wallet = read_wallet(db)
        if wallet["balance"] < amount:
            return "insufficient"
        if save_wallet(db, wallet["balance"] - amount, wallet["version"]) == 1:
            return "ok"
    return "conflict"


def simulate(balance, races, amount, max_attempts):
    # Тестовый стенд, не меняй.
    db = {"balance": balance, "version": 1, "races": list(races)}
    result = charge(db, amount, max_attempts)
    return {"result": result, "balance": db["balance"], "version": db["version"]}
''')

task(
    "be-transactions-02", "write_code", 4, "manager",
    "Стас: «Бухгалтерия нашла дыру в бонусах: при оформлении заказа клиенту списали 200 бонусов, а кэшбэк за другой заказ, начисленный в ту же секунду, пропал. Гена говорит, воркеры читают кошелёк, считают баланс в Python и пишут его целиком, а держать блокировку на время расчёта нельзя — там поход в сервис лояльности. Сделай оптимистичную блокировку по версии, как он предлагает».",
    "Реализуй save_wallet и charge. save_wallet записывает баланс и увеличивает version на 1, только если версия в базе равна expected_version, и возвращает число обновлённых строк (1 или 0). charge на каждой попытке заново читает кошелёк через read_wallet: бонусов меньше amount — верни \"insufficient\"; запись прошла — \"ok\"; за max_attempts попыток записать не удалось — \"conflict\".",
    "Потерянное обновление возникает, когда запись «баланс = X» не проверяет, что баланс всё ещё тот, от которого считали. Версия делает запись условной: UPDATE ... WHERE version = :read_version пройдёт, только если с момента чтения строку никто не менял, а version = version + 1 сообщает об изменении всем остальным. Rowcount 0 — не ошибка базы, а сигнал «данные устарели»: нужно перечитать кошелёк и пересчитать всё заново, включая проверку достаточности бонусов, потому что баланс мог уменьшиться (тест с -400), а запись со старым снимком всё равно не пройдёт по версии. Число попыток ограничивают, чтобы под сильной конкуренцией воркер не крутился вечно; дальше задачу возвращают в очередь или отдают пользователю 409. Если конфликтов много, оптимистичная схема начинает проигрывать пессимистичной (SELECT ... FOR UPDATE) или атомарному UPDATE balance = balance - :amount.",
    ["save_wallet — это условный UPDATE: сравни версию в базе с той, что была при чтении.",
     "После неудачной записи старый снимок бесполезен: читай кошелёк заново в начале каждой попытки.",
     "Цикл for на max_attempts: read_wallet → проверка баланса → save_wallet; если вернул 1 — \"ok\". После цикла — \"conflict\"."],
    code=WALLET_STARTER, language="python", entry="simulate",
    answer=WALLET_FIX,
    tests=[
        {"input": [500, [], 200, 3], "expected": {"result": "ok", "balance": 300, "version": 2}},
        {"input": [500, [100], 200, 3], "expected": {"result": "ok", "balance": 400, "version": 3}},
        {"input": [500, [-400], 200, 3], "expected": {"result": "insufficient", "balance": 100, "version": 2}},
        {"input": [500, [10, 10, 10], 200, 3], "expected": {"result": "conflict", "balance": 530, "version": 4}},
        {"input": [500, [10, 10], 200, 5], "expected": {"result": "ok", "balance": 320, "version": 4}},
        {"input": [150, [], 150, 1], "expected": {"result": "ok", "balance": 0, "version": 2}},
    ],
)

task(
    "be-transactions-03", "code_review", 4, "client",
    "Артур, директор фитнес-клуба «Жми»: «На вечернюю йогу на 20 мест записалось 23 человека, люди стояли друг у друга на ковриках! А двоим ещё и пришло по две SMS о записи. Разберитесь, пока тренер не уволился».",
    "Это код записи на тренировку. Выбери все замечания, которые действительно нужно исправить.",
    "Переполнение — классическая гонка «проверил, потом записал»: на READ COMMITTED два запроса одновременно читают booked = 19, оба проходят проверку и оба увеличивают счётчик (заодно одно из увеличений может затереться). Лечится блокировкой строки слота через SELECT ... FOR UPDATE (второй запрос подождёт и увидит уже 20) или одним атомарным UPDATE ... WHERE booked < capacity с проверкой числа обновлённых строк. SMS внутри транзакции — двойная проблема: шлюз отвечает секундами, и всё это время транзакция открыта (а с FOR UPDATE ещё и очередь на слот), а если COMMIT упадёт, человек получит SMS о записи, которой нет; отправлять нужно после коммита, надёжнее — через очередь. Двойная SMS — это двойной клик: без UNIQUE (slot_id, user_id) база спокойно создаёт две записи одного человека. READ UNCOMMITTED в PostgreSQL работает как READ COMMITTED и гонку не лечит; подсчёт COUNT(*) вместо счётчика — та же гонка «проверил, потом вставил»; 409 для «мест нет» — правильный код, а db.get — современный API SQLAlchemy 2.0.",
    ["Что будет, если два человека нажмут «Записаться» на последнее место в одну и ту же миллисекунду?",
     "Что произойдёт с SMS, если db.commit() упадёт?",
     "Откуда у одного человека могут взяться две записи на одну тренировку?"],
    code=C('''
@router.post("/slots/{slot_id}/bookings", status_code=201)
def book(slot_id: int, user: User = Depends(current_user), db: Session = Depends(get_db)):
    slot = db.get(Slot, slot_id)
    if slot is None:
        raise HTTPException(404, "Тренировка не найдена")
    if slot.booked >= slot.capacity:
        raise HTTPException(409, "Мест нет")
    slot.booked += 1
    db.add(Booking(slot_id=slot.id, user_id=user.id))
    # HTTP-запрос к SMS-шлюзу, отвечает до 5 секунд
    sms_gateway.send(user.phone, f"Вы записаны: {slot.title}, {slot.starts_at:%d.%m %H:%M}")
    db.commit()
    return {"slot_id": slot.id}
'''),
    language="python",
    options=[
        "Гонка: два запроса читают booked = 19 и оба проходят проверку. Нужен with_for_update или атомарный UPDATE … WHERE booked < capacity",
        "SMS уходит внутри открытой транзакции до commit: шлюз держит её секундами, а при сбое commit придёт SMS о несуществующей записи",
        "Нет уникального ограничения на (slot_id, user_id): двойной клик создаёт две записи одного человека",
        "Поставить уровень изоляции READ UNCOMMITTED, чтобы транзакции сразу видели чужие изменения счётчика booked",
        "Убрать счётчик booked и каждый раз считать SELECT COUNT(*) по bookings — данные всегда точные, и гонки не будет",
        "Для «мест нет» нужен код 500, а не 409: запись не удалась по вине сервера, и клиент должен повторить позже",
        "db.get устарел в SQLAlchemy 2.0 — надёжнее db.query(Slot).filter(Slot.id == slot_id).first()",
    ],
    answer=[0, 1, 2],
)

task(
    "be-transactions-04", "incident", 5, "devops_colleague",
    "Дима: «С утра в логах PostgreSQL пачками deadlock detected, часть оформлений падает с 500. Началось после вчерашнего релиза „резервируем остатки сразу при оформлении“. Глянь логи, я пока держу оборону».",
    "Выбери все действия, которые действительно устраняют проблему.",
    "В логе классический цикл: процесс 48211 (заказ 55120931, товары [877, 1043]) уже обновил 877 и ждёт 1043, а процесс 48197 (заказ 55120932, [1043, 877]) держит 1043 и ждёт 877. Причина — транзакции блокируют одни и те же строки в разном порядке, как лежат товары в корзине. Если сортировать позиции по product_id перед UPDATE (или заранее взять блокировки одним SELECT ... WHERE id = ANY(:ids) ORDER BY id FOR UPDATE), все транзакции захватывают строки в одном порядке, и цикл невозможен: вторая просто подождёт первую. Полностью исключить дедлоки в нагруженной системе нельзя, поэтому ошибку 40P01 ловят и повторяют всю транзакцию с небольшой случайной паузой — после отмены одной из транзакций вторая уже прошла. deadlock_timeout лишь задерживает обнаружение: транзакции будут висеть дольше. SERIALIZABLE дедлоки не убирает и добавляет ошибки сериализации. Коммит каждого UPDATE по отдельности ломает атомарность: половина заказа зарезервирована, половина нет, а убивать долгие транзакции по крону — лечить симптомы, заодно отрубая честные запросы.",
    ["Сравни порядок товаров в двух заказах из лога приложения.",
     "Дедлок возможен, только если транзакции ждут друг друга по кругу. Как сделать круг невозможным?",
     "Одно действие убирает причину, второе делает систему устойчивой к редким оставшимся случаям."],
    code=C('''
# postgresql-2026-09-23.log
2026-09-23 11:42:07.318 MSK [48211] ERROR:  deadlock detected
2026-09-23 11:42:07.318 MSK [48211] DETAIL:  Process 48211 waits for ShareLock on transaction 91204481; blocked by process 48197.
        Process 48197 waits for ShareLock on transaction 91204477; blocked by process 48211.
        Process 48211: UPDATE products SET stock = stock - 1 WHERE id = 1043
        Process 48197: UPDATE products SET stock = stock - 2 WHERE id = 877
2026-09-23 11:42:07.318 MSK [48211] HINT:  See server log for query details.
2026-09-23 11:42:07.318 MSK [48211] CONTEXT:  while updating tuple (4127,19) in relation "products"
2026-09-23 11:42:07.318 MSK [48211] STATEMENT:  UPDATE products SET stock = stock - 1 WHERE id = 1043

$ grep -c "deadlock detected" postgresql-2026-09-23.log
184

# market-api, та же секунда
11:42:06.290 INFO  checkout order=55120931 reserve product_ids=[877, 1043]
11:42:06.291 INFO  checkout order=55120932 reserve product_ids=[1043, 877]
11:42:07.320 ERROR checkout order=55120931 psycopg.errors.DeadlockDetected: deadlock detected -> 500
11:42:07.326 INFO  checkout order=55120932 reserved ok

# checkout.py (релиз 2026.09.22)
with session.begin():
    for item in order.items:            # в том порядке, как лежат в корзине
        session.execute(
            update(Product)
            .where(Product.id == item.product_id)
            .values(stock=Product.stock - item.qty)
        )
    session.add(Reservation(order_id=order.id))
'''),
    language="text",
    options=[
        "Блокировать строки в одном порядке: сортировать позиции по product_id перед UPDATE или взять SELECT … ORDER BY id FOR UPDATE",
        "Ловить DeadlockDetected (SQLSTATE 40P01) и повторять всю транзакцию 2–3 раза с небольшой случайной паузой",
        "Увеличить deadlock_timeout до 30 секунд, чтобы PostgreSQL реже отменял транзакции и заказы успевали пройти",
        "Перевести checkout на уровень SERIALIZABLE: база сама упорядочит конкурирующие транзакции, и дедлоков не будет",
        "Убрать общую транзакцию: пусть каждый UPDATE коммитится сам — блокировки будут держаться миллисекунды",
        "Поставить cron, который раз в минуту завершает через pg_terminate_backend транзакции дольше секунды",
    ],
    answer=[0, 1],
    time_limit=20,
)


# =====================================================================
# be-n-plus-one
# =====================================================================
topic("be-n-plus-one", """
N+1 — самая частая причина тормозов в коде с ORM. Один запрос достаёт список из N объектов, потом код в цикле обращается к связи каждого, и ORM лениво догружает её отдельным запросом. Итого 1 + N походов в базу, а если связей две — 1 + 2N. Каждый запрос быстрый, но сетевые задержки складываются: 50 товаров с двумя связями — 101 запрос вместо 2–3.

    products = db.scalars(select(Product).limit(50)).all()
    for p in products:
        print(p.brand.name)   # тут ORM делает SELECT ... FROM brands WHERE id = ?

Коварство в том, что обращение к атрибуту выглядит бесплатным, а запросы прячутся в сериализаторах, шаблонах, property моделей и Pydantic-схемах с from_attributes.

Как заметить:
• лог SQL: echo=True в SQLAlchemy или логгер django.db.backends — десятки одинаковых запросов с разными id;
• трейсы (Jaeger, Sentry): «лесенка» из одинаковых спанов внутри одного запроса;
• pg_stat_statements: у простого запроса по первичному ключу огромное calls;
• в тестах — проверка числа запросов: assertNumQueries в Django, счётчик на событии before_cursor_execute в SQLAlchemy;
• lazy="raise" (raiseload) на связях: ленивая загрузка бросит исключение, и N+1 не пройдёт незамеченным.

Как лечить в SQLAlchemy:
• joinedload — LEFT JOIN в том же запросе. Хорош для many-to-one и one-to-one (товар → бренд).
• selectinload — второй запрос WHERE id IN (...). Лучший выбор для коллекций (товар → картинки).
• несколько joinedload по коллекциям — ловушка: строки перемножаются (картинки × отзывы на каждый товар), и база гонит в приложение декартово произведение. Для коллекций бери selectinload. При joinedload коллекции в 2.0 обязателен .unique().

В Django: select_related("brand") — JOIN для ForeignKey и OneToOne, prefetch_related("images") — отдельный запрос для обратных связей и M2M, Prefetch("images", queryset=...) — с фильтром и сортировкой.

Если нужна только статистика (число отзывов, средний рейтинг), коллекцию не грузят вовсе: считают в SQL через GROUP BY, annotate(Count(...)) или хранят денормализованный счётчик.

В async SQLAlchemy неявная ленивая загрузка невозможна: обращение к незагруженной связи падает с MissingGreenlet. Связи загружают заранее через options(...) или явно: await obj.awaitable_attrs.author.

Без ORM паттерн тот же — батч:

    ids = {o["user_id"] for o in orders}
    users = {u["id"]: u for u in fetch_users(ids)}   # WHERE id = ANY(:ids)
    for o in orders:
        o["user"] = users.get(o["user_id"])

Собрал id, сделал один запрос, разложил по словарю, собрал ответ в памяти. В GraphQL для этого есть DataLoader. И не забывай про пагинацию: даже идеальные 3 запроса на 100 000 строк — плохой эндпоинт.
""")

task(
    "be-n-plus-one-01", "incident", 3, "manager",
    "Стас: «После вчерашнего релиза каталог „Лампового Маркета“ стал заметно тормозить, p95 вырос с 60 мс почти до секунды. Мы всего-то добавили в карточку бренд и обложку! Дима говорит, база не перегружена, просто „очень много мелких запросов“. Разберись».",
    "Что стало причиной и как её правильно устранить?",
    "Трейс — учебная «лесенка»: один запрос за 48 товарами и дальше по два одинаковых запроса на каждый товар, всего 97 походов в базу. Это N+1 из PR: p.brand.name и p.images лениво догружаются ORM при сериализации каждой карточки. Каждый запрос выполняется за доли миллисекунды, но сетевые задержки, пул и ORM складываются в сотни миллисекунд, а pg_stat_statements показывает миллион вызовов в час. Лечение — загрузить связи вместе с товарами: joinedload для many-to-one бренда (JOIN в том же запросе) и selectinload для коллекции картинок (один запрос WHERE product_id IN (...)) — вместо 97 запросов станет 2. Индекс не нужен: сам запрос товаров занимает 9 мс. Кэш в Redis оставит те же 96 сетевых походов, только в другое хранилище, и добавит инвалидацию. Больше соединений в пуле не уменьшит число запросов, страница поменьше спрячет проблему, а в async-коде неявная ленивая загрузка вообще упадёт с MissingGreenlet.",
    ["Посчитай запросы в трейсе на одну страницу из 48 товаров.",
     "Какие строки PR обращаются к связям товара?",
     "Связи нужно загрузить заранее, в самом запросе товаров."],
    code=C('''
# Jaeger: GET /catalog?category=12&page=1
GET /catalog                                                   312 ms
├─ SELECT products ... WHERE category_id = $1 LIMIT 48           9 ms
├─ SELECT brands.id, brands.name ... WHERE brands.id = $1         3 ms
├─ SELECT product_images.url ... WHERE product_id = $1           3 ms
├─ SELECT brands.id, brands.name ... WHERE brands.id = $1         3 ms
├─ SELECT product_images.url ... WHERE product_id = $1           3 ms
│  ... ещё 92 таких же спана
└─ serialize response                                            14 ms

# pg_stat_statements, топ по calls за последний час
   calls   | mean_ms | query
-----------+---------+-------------------------------------------------------------------
 1 204 311 |    0.21 | SELECT brands.id, brands.name FROM brands WHERE brands.id = $1
 1 198 877 |    0.34 | SELECT product_images.id, product_images.url, ... WHERE product_images.product_id = $1
    24 950 |    6.80 | SELECT products.id, products.title, ... WHERE products.category_id = $1 LIMIT $2

# PR #2291 «бренд и обложка в карточке каталога»
 def product_card(p: Product) -> dict:
     return {
         "id": p.id,
         "title": p.title,
         "price": p.price,
+        "brand": p.brand.name,
+        "cover": p.images[0].url if p.images else None,
     }
'''),
    language="text",
    options=[
        "N+1: бренд и картинки ORM лениво догружает для каждого товара — загрузить их заранее через joinedload и selectinload",
        "Не хватает индекса по products.category_id: после релиза запрос товаров стал заметно медленнее и тянет за собой все остальные",
        "Кэшировать бренды и картинки в Redis по id: запросов в базу станет меньше, и p95 вернётся к прежним 60 мс",
        "Увеличить пул соединений к базе с 20 до 200, чтобы мелкие запросы не стояли в очереди за свободным соединением",
        "Уменьшить страницу каталога с 48 до 12 товаров — запросов станет вчетверо меньше, а пользователи не заметят",
        "Перевести эндпоинт на async — запросы за брендами и картинками пойдут параллельно, а не друг за другом",
    ],
    answer=0,
    time_limit=15,
)

task(
    "be-n-plus-one-02", "find_bug", 4, "teamlead",
    "Марина: «Переводим API отзывов на async SQLAlchemy. На стейдже /products/{id}/reviews отвечает 500. В синхронной версии тот же код работал — правда, делал по запросу на каждого автора отзыва. Найди, в чём дело, и почини так, чтобы не вернуть старую проблему».",
    "Что вызывает ошибку и какое исправление правильное?",
    "В синхронной сессии обращение r.author незаметно выполняло SELECT по автору — это и был N+1: 20 отзывов, 21 запрос. В async-сессии ORM не может неявно сделать IO внутри обычного обращения к атрибуту: для этого нужен await, а доступ к атрибуту не асинхронный. Поэтому ленивая загрузка падает с MissingGreenlet. Правильное исправление — загрузить авторов вместе с отзывами: .options(selectinload(Review.author)) или joinedload для такой many-to-one связи. Это разом чинит ошибку и убирает N+1: вместо 21 запроса будет 1–2. scalars() у результата синхронный, и await перед ним не нужен. asyncio.run внутри работающего event loop сам упадёт, commit не имеет отношения к чтению связей, а синхронный def с AsyncSession вообще не заработает. Чтобы N+1 не возвращался незаметно, на связях ставят lazy=\"raise\".",
    ["Какая строка кода обращается к базе, хотя в ней нет await?",
     "Что делает ORM, когда ты читаешь незагруженную связь r.author?",
     "Загрузи авторов вместе с отзывами через options(...)."],
    code=C('''
@router.get("/products/{product_id}/reviews")
async def product_reviews(product_id: int, db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(Review)
        .where(Review.product_id == product_id)
        .order_by(Review.created_at.desc())
        .limit(20)
    )
    reviews = result.scalars().all()
    return [
        {"text": r.text, "stars": r.stars, "author": r.author.display_name}
        for r in reviews
    ]

# лог стейджа:
# sqlalchemy.exc.MissingGreenlet: greenlet_spawn has not been called;
# can't call await_only() here. Was IO attempted in an unexpected place?
'''),
    language="python",
    options=[
        "r.author — ленивая загрузка, а async-сессия не умеет неявно ходить в базу. Нужен .options(selectinload(Review.author)) — уйдёт и N+1",
        "Забыли await перед result.scalars(): в async-режиме нужно await result.scalars().all(), иначе вернётся корутина",
        "Обернуть обращение к r.author в asyncio.run(), чтобы запрос за автором отзыва выполнился синхронно прямо внутри генератора списка отзывов",
        "Добавить await db.commit() перед return: без коммита async-сессия не подгружает связи объектов",
        "Сделать эндпоинт синхронным (def вместо async def), оставив AsyncSession: ленивая загрузка снова заработает",
    ],
    answer=0,
)

task(
    "be-n-plus-one-03", "code_review", 4, "teamlead",
    "Гена: «Джун „починил“ N+1 на странице категории: в PR теперь joinedload на всё подряд. На стейдже запросов действительно мало, а страница всё равно отвечает полторы секунды и съедает 400 МБ памяти на воркер. У популярных ламп по 300 отзывов и 12 фото. Глянь, что оставить в ревью».",
    "Выбери все замечания, которые действительно стоит оставить.",
    "joinedload по двум коллекциям в одном запросе перемножает строки: для товара с 12 фото и 300 отзывами база вернёт 3 600 строк, на 20 товаров — десятки тысяч, и ORM потом склеивает их обратно. Отсюда память и время. Коллекции грузят через selectinload — отдельный запрос WHERE product_id IN (...) на каждую, без перемножения. Но отзывы здесь не нужны целиком: ради среднего и количества не тянут 6 000 объектов — считают в SQL (avg, count с GROUP BY) или хранят денормализованные rating и reviews_count в товаре. А p.seller вообще не загружен — это остаток N+1, ещё 20 запросов. Остальные замечания неверны: при LIMIT с joinedload коллекций SQLAlchemy сам заворачивает запрос в подзапрос, и LIMIT применяется к товарам; joinedload для many-to-one бренда — правильный выбор, строки он не множит; .unique() в 2.0 обязателен при joinedload коллекции — без него будет ошибка.",
    ["Сколько строк вернёт JOIN товара с 12 фото и 300 отзывами одновременно?",
     "Какие данные из отзывов реально нужны в ответе?",
     "Пройди по всем полям в ответе: все ли связи загружены заранее?"],
    code=C('''
@router.get("/categories/{category_id}/products")
def category_products(category_id: int, db: Session = Depends(get_db)):
    stmt = (
        select(Product)
        .where(Product.category_id == category_id)
        .options(
            joinedload(Product.brand),
            joinedload(Product.images),
            joinedload(Product.reviews),
        )
        .order_by(Product.id)
        .limit(20)
    )
    products = db.scalars(stmt).unique().all()
    return [
        {
            "id": p.id,
            "title": p.title,
            "brand": p.brand.name,
            "cover": p.images[0].url if p.images else None,
            "rating": round(sum(r.stars for r in p.reviews) / len(p.reviews), 1) if p.reviews else None,
            "reviews_count": len(p.reviews),
            "seller": p.seller.name,
        }
        for p in products
    ]
'''),
    language="python",
    options=[
        "joinedload двух коллекций перемножает строки: 12 фото × 300 отзывов = 3 600 строк на товар. Коллекции — через selectinload",
        "Ради рейтинга и количества грузятся все отзывы целиком — посчитать avg/count в SQL или хранить денормализованные поля",
        "p.seller не загружен заранее — на каждый товар уйдёт отдельный запрос, N+1 остался",
        "LIMIT 20 вместе с joinedload обрежет строки JOIN, а не товары — на странице окажется меньше 20 товаров",
        "joinedload(Product.brand) тоже надо заменить на selectinload: JOIN всегда медленнее отдельного запроса по IN",
        ".unique() лишний: он заставляет SQLAlchemy сравнивать объекты в Python и только тратит память воркера",
    ],
    answer=[0, 1, 2],
)

BATCH_STARTER = C('''
from collections import defaultdict


class FakeDB:
    # Имитация базы КофеБота: каждый метод — один SQL-запрос.
    def __init__(self, orders, users, items):
        self.orders = orders
        self.users = users
        self.items = items
        self.queries = 0

    def fetch_orders(self):
        # SELECT id, user_id FROM orders ORDER BY id
        self.queries += 1
        return [dict(o) for o in self.orders]

    def fetch_user(self, user_id):
        # SELECT id, name FROM users WHERE id = %s
        self.queries += 1
        for u in self.users:
            if u["id"] == user_id:
                return dict(u)
        return None

    def fetch_users(self, user_ids):
        # SELECT id, name FROM users WHERE id = ANY(%s)
        self.queries += 1
        wanted = set(user_ids)
        return [dict(u) for u in self.users if u["id"] in wanted]

    def fetch_items(self, order_id):
        # SELECT order_id, drink, price FROM order_items WHERE order_id = %s ORDER BY id
        self.queries += 1
        return [dict(i) for i in self.items if i["order_id"] == order_id]

    def fetch_items_for_orders(self, order_ids):
        # SELECT order_id, drink, price FROM order_items WHERE order_id = ANY(%s) ORDER BY id
        self.queries += 1
        wanted = set(order_ids)
        return [dict(i) for i in self.items if i["order_id"] in wanted]


def order_history(db):
    result = []
    for order in db.fetch_orders():
        user = db.fetch_user(order["user_id"])
        items = db.fetch_items(order["id"])
        result.append({
            "id": order["id"],
            "user": user["name"] if user else None,
            "drinks": [i["drink"] for i in items],
            "total": sum(i["price"] for i in items),
        })
    return result


def run(orders, users, items):
    # Тестовый стенд, не меняй.
    db = FakeDB(orders, users, items)
    history = order_history(db)
    return {"history": history, "queries": db.queries}
''')

BATCH_FIX = BATCH_STARTER.replace(C('''
def order_history(db):
    result = []
    for order in db.fetch_orders():
        user = db.fetch_user(order["user_id"])
        items = db.fetch_items(order["id"])
        result.append({
'''), C('''
def order_history(db):
    orders = db.fetch_orders()
    if not orders:
        return []
    user_ids = list({o["user_id"] for o in orders})
    users = {u["id"]: u for u in db.fetch_users(user_ids)}
    items_by_order = defaultdict(list)
    for item in db.fetch_items_for_orders([o["id"] for o in orders]):
        items_by_order[item["order_id"]].append(item)
    result = []
    for order in orders:
        user = users.get(order["user_id"])
        items = items_by_order[order["id"]]
        result.append({
'''))
assert BATCH_FIX != BATCH_STARTER

_U = [{"id": 1, "name": "Гена"}, {"id": 2, "name": "Ира"}, {"id": 3, "name": "Дима"}]

task(
    "be-n-plus-one-04", "write_code", 5, "qa",
    "Ира: «История заказов в КофеБоте у активных ребят открывается по три секунды. Посмотрела лог: на 300 заказов — 601 запрос к базе. Перепиши загрузку батчами. Тест посчитает запросы, так что без фокусов».",
    "Перепиши order_history: при любом количестве заказов — ровно 3 запроса (заказы, пользователи, позиции), если заказов нет — 1 запрос. Результат не должен измениться: заказы в порядке, в котором их вернула база, напитки — в порядке позиций, удалённый пользователь — None, заказ без позиций — пустой список и total 0. FakeDB и run не меняй.",
    "Батчинг — ручной аналог selectinload: сначала один запрос за основным списком, потом по одному запросу на каждую связь сразу для всех id (WHERE id = ANY(:ids)), а сборка ответа — в памяти через словари. Пользователей раскладываем в dict по id — поиск за O(1) вместо перебора. Позиции группируем через defaultdict(list) по order_id: заказ без позиций сам получит пустой список, а порядок позиций сохранится, потому что мы добавляем их в том порядке, в котором вернула база. Множество user_id убирает дубли — у одного сотрудника десятки заказов. Ранний выход при пустом списке экономит два бессмысленных запроса с пустым IN. Число запросов теперь не зависит от N: 3 на 5 заказов и 3 на 300. На больших списках id ещё бьют на чанки по несколько тысяч и держат пагинацию, чтобы не тащить в память всю историю.",
    ["Какие методы FakeDB принимают сразу список id?",
     "Сначала собери все user_id и id заказов, сделай по одному запросу, разложи результат по словарям.",
     "users = {u[\"id\"]: u for u in db.fetch_users(ids)}; позиции — в defaultdict(list) по order_id; при пустом списке заказов сразу верни []."],
    code=BATCH_STARTER, language="python", entry="run",
    answer=BATCH_FIX,
    tests=[
        {"input": [
            [{"id": 10, "user_id": 1}, {"id": 11, "user_id": 2}, {"id": 12, "user_id": 1}],
            _U,
            [{"order_id": 10, "drink": "Капучино", "price": 180},
             {"order_id": 11, "drink": "Латте", "price": 200},
             {"order_id": 10, "drink": "Круассан", "price": 150},
             {"order_id": 12, "drink": "Американо", "price": 120}]],
         "expected": {"history": [
             {"id": 10, "user": "Гена", "drinks": ["Капучино", "Круассан"], "total": 330},
             {"id": 11, "user": "Ира", "drinks": ["Латте"], "total": 200},
             {"id": 12, "user": "Гена", "drinks": ["Американо"], "total": 120}],
             "queries": 3}},
        {"input": [[], _U, []], "expected": {"history": [], "queries": 1}},
        {"input": [
            [{"id": 20, "user_id": 99}, {"id": 21, "user_id": 3}],
            _U,
            [{"order_id": 20, "drink": "Раф", "price": 230}]],
         "expected": {"history": [
             {"id": 20, "user": None, "drinks": ["Раф"], "total": 230},
             {"id": 21, "user": "Дима", "drinks": [], "total": 0}],
             "queries": 3}},
        {"input": [
            [{"id": i, "user_id": 2} for i in range(30, 36)],
            _U,
            [{"order_id": i, "drink": "Эспрессо", "price": 100} for i in range(30, 36)]],
         "expected": {"history": [
             {"id": i, "user": "Ира", "drinks": ["Эспрессо"], "total": 100} for i in range(30, 36)],
             "queries": 3}},
    ],
)


# =====================================================================
# be-caching
# =====================================================================
topic("be-caching", """
Кэш хранит результат дорогой операции, чтобы не повторять её. Уровни: память процесса (быстро, но у каждого пода своя копия), Redis (общий для всех подов), HTTP-кэш и CDN (для публичных ответов). Прежде чем кэшировать, проверь, нельзя ли просто ускорить запрос индексом: кэш добавляет устаревание данных, инвалидацию и ещё одну систему, которая может упасть.

Cache-aside (ленивый кэш) — самый частый паттерн:

    def get_product(pid):
        key = f"product:{pid}:v2"
        cached = redis.get(key)
        if cached is not None:
            return json.loads(cached)
        product = load_from_db(pid)
        redis.set(key, json.dumps(product), ex=300)
        return product

TTL (ex=300) — страховка: даже если инвалидацию где-то забыли, данные устареют не больше чем на 5 минут. TTL выбирают из бизнеса: сколько можно показывать старую цену.

Инвалидация при записи: сначала запись в базу и COMMIT, потом DEL ключа. Не записывай в кэш новое значение из кода записи: при двух параллельных изменениях в кэше может остаться проигравшее. Удалять надёжнее — следующий читатель возьмёт свежие данные из базы. Если объект входит в несколько ключей (карточка, список категории, поиск), инвалидировать нужно все, поэтому ключи проектируют заранее. Версия в ключе (...:v2) позволяет сменить формат значения без очистки Redis.

Ключ должен включать всё, от чего зависит ответ: id, страницу, фильтры, язык, валюту.

Что нельзя кэшировать:
• персональные данные под общим ключом — корзину, профиль, персональные цены. Первый пользователь «прогреет» ключ, остальные увидят его данные. Кэшируй общую часть, а персональную накладывай после чтения из кэша;
• ответы с Set-Cookie и всё, что зависит от авторизации, — в CDN и общих прокси;
• данные, где нужна точность прямо сейчас: остаток при оформлении заказа, баланс перед списанием — это проверяют по базе.

Cache stampede (dogpile): популярный ключ протух, и сотни запросов одновременно идут строить его в базу — CPU базы 100%, пул соединений кончился. Защита:
• лок на перестроение: SET key:lock 1 NX EX 30 — строит один запрос, остальные ждут или отдают старое значение;
• раннее обновление и stale-while-revalidate: храни значение дольше TTL и обновляй в фоне, пока отдаёшь старое;
• jitter — случайный разброс TTL, чтобы тысячи ключей, прогретых разом, не протухли в одну секунду;
• прогрев после деплоя и очистки кэша.

Redis в проде: maxmemory и политика вытеснения (например, allkeys-lru), никаких KEYS * (используй SCAN), таймауты на клиенте. Если Redis упал, сервис должен работать через базу, пусть медленнее. Hit rate и задержки кэша выводят в метрики — без них не понять, помогает ли кэш вообще.
""")

task(
    "be-caching-01", "estimation", 3, "manager",
    "Стас: «Каталог в час пик тормозит, ребята говорят — нужен Redis. Прикрути кэш к каталогу, это же пара часов? Мне к планёрке нужна цифра».",
    "Прежде чем называть срок, выбери вопросы, которые обязательно нужно выяснить.",
    "Кэш — не «пара часов», а решение с последствиями, и оценка зависит от ответов. Сначала — что именно тормозит: если виноват один запрос без индекса, правильная задача — индекс на полдня, а не кэш. Допустимая свежесть данных и источник изменений задают TTL и схему инвалидации: цена из выгрузки раз в час и ручная правка в админке требуют разного. Персональные данные в выдаче (оптовые цены, «в избранном») меняют дизайн ключей: общую часть кэшируют, персональную накладывают сверху, иначе будет утечка чужих данных. Наличие Redis в инфраструктуре и поведение при его падении — это часть работы, которую часто забывают: мониторинг, таймауты, fallback на базу. Выбор клиентской библиотеки и название ключей решаются внутри команды и на срок почти не влияют. Разумная оценка: cache-aside для страниц категорий с TTL и инвалидацией по событию изменения цены, метриками hit rate и нагрузочным тестом — 3–4 дня, если Redis уже есть; если выяснится, что хватит индекса, — полдня.",
    ["Какие ответы могут изменить объём работы в разы или вообще отменить кэш?",
     "Подумай о свежести цен, персональных данных и о том, что будет, если Redis упадёт."],
    options=[
        "Что именно тормозит — все запросы каталога или пара тяжёлых? Есть ли метрики или профиль?",
        "Насколько свежими должны быть цены и остатки и откуда они меняются: выгрузка из 1С, правки в админке?",
        "Есть ли в выдаче каталога персональное: оптовые цены, отметка «в избранном», история просмотров?",
        "Есть ли Redis в инфраструктуре, кто его поддерживает и что делать сервису, если Redis недоступен?",
        "Какую клиентскую библиотеку взять для Redis — redis-py или что-то побыстрее, с поддержкой кластера?",
        "Как называть ключи в Redis — на английском или транслитом, и нужен ли единый префикс для каталога?",
        "Не лучше ли сразу взять Memcached вместо Redis — говорят, на простых ключах он быстрее?",
    ],
    answer=[0, 1, 2, 3],
    time_limit=12,
)

CACHE_STARTER = C('''
class FakeDB:
    def __init__(self, products):
        self.rows = {p["id"]: dict(p) for p in products}
        self.reads = 0

    def select_product(self, product_id):
        # SELECT id, title, price FROM products WHERE id = %s
        self.reads += 1
        row = self.rows.get(product_id)
        return dict(row) if row else None

    def update_price(self, product_id, price):
        # UPDATE products SET price = %s WHERE id = %s; COMMIT
        self.rows[product_id]["price"] = price


class FakeCache:
    # Упрощённый Redis: хранит значение и момент, когда оно протухнет.
    def __init__(self):
        self.data = {}

    def get(self, key, now):
        entry = self.data.get(key)
        if entry is None or entry["expires_at"] <= now:
            return None
        return entry["value"]

    def set(self, key, value, ttl, now):
        # SET key value EX ttl
        self.data[key] = {"value": value, "expires_at": now + ttl}

    def delete(self, key):
        # DEL key
        self.data.pop(key, None)


def get_product(db, cache, product_id, now, ttl):
    # Cache-aside: верни товар (словарь) или None, если его нет.
    # твой код
    pass


def change_price(db, cache, product_id, price):
    # Смена цены из админки.
    # твой код
    pass


def run(products, events, ttl):
    # Тестовый стенд, не меняй.
    # events: ["get", product_id, now] или ["update", product_id, new_price]
    db = FakeDB(products)
    cache = FakeCache()
    answers = []
    for ev in events:
        if ev[0] == "get":
            p = get_product(db, cache, ev[1], ev[2], ttl)
            answers.append(p["price"] if p else None)
        else:
            change_price(db, cache, ev[1], ev[2])
    return {"answers": answers, "db_reads": db.reads}
''')

CACHE_FIX = CACHE_STARTER.replace(C('''
def get_product(db, cache, product_id, now, ttl):
    # Cache-aside: верни товар (словарь) или None, если его нет.
    # твой код
    pass


def change_price(db, cache, product_id, price):
    # Смена цены из админки.
    # твой код
    pass
'''), C('''
def product_key(product_id):
    return f"product:{product_id}"


def get_product(db, cache, product_id, now, ttl):
    key = product_key(product_id)
    cached = cache.get(key, now)
    if cached is not None:
        return cached
    product = db.select_product(product_id)
    if product is not None:
        cache.set(key, product, ttl, now)
    return product


def change_price(db, cache, product_id, price):
    db.update_price(product_id, price)
    cache.delete(product_key(product_id))
'''))
assert CACHE_FIX != CACHE_STARTER

_P = [{"id": 1, "title": "Лампа Эдисона E27", "price": 1490},
      {"id": 2, "title": "Кабель USB-C", "price": 350}]

task(
    "be-caching-02", "write_code", 4, "devops_colleague",
    "Дима: «Карточку товара дёргают 3 000 раз в секунду, и почти всегда — одни и те же сто товаров, база греется. Сделай cache-aside. Только Стас предупредил: после смены цены в админке покупатель должен сразу видеть новую, а не через пять минут. Время в функцию передаём аргументом, чтобы это нормально тестировалось».",
    "Реализуй get_product и change_price. get_product: сначала кэш (ключ зависит от id товара), при промахе — чтение из базы и запись в кэш на ttl секунд; несуществующий товар — вернуть None и не кэшировать. change_price: обновить цену в базе и инвалидировать кэш этого товара. FakeDB, FakeCache и run не меняй.",
    "Cache-aside: читаем кэш, при промахе идём в базу и кладём результат с TTL. TTL здесь отвечает за то, чтобы данные не жили вечно, если какое-то изменение прошло мимо кода инвалидации (миграция, ручной UPDATE). Инвалидация — отдельная обязанность кода записи: после UPDATE удаляем ключ, и следующий читатель возьмёт свежую цену из базы. Удалять надёжнее, чем записывать новое значение: при двух параллельных сменах цены запись в кэш могла бы оставить проигравший вариант. Порядок важен: сначала база и коммит, потом DEL — иначе между удалением и коммитом читатель успеет положить в кэш старую цену. Ключ обязан включать id, иначе все товары делят одну запись. None не кэшируем: FakeCache не отличает «нет в кэше» от «в кэше None», а в реальном Redis для кэширования отсутствия (защита от перебора несуществующих id) нужен отдельный маркер и короткий TTL.",
    ["Сначала cache.get(key, now); если там не None — это ответ.",
     "При промахе: db.select_product, и если товар найден — cache.set(key, product, ttl, now).",
     "change_price: db.update_price(...), затем cache.delete(того же ключа)."],
    code=CACHE_STARTER, language="python", entry="run",
    answer=CACHE_FIX,
    tests=[
        {"input": [_P, [["get", 1, 0], ["get", 1, 30], ["get", 1, 59], ["get", 1, 60], ["get", 1, 100]], 60],
         "expected": {"answers": [1490, 1490, 1490, 1490, 1490], "db_reads": 2}},
        {"input": [_P, [["get", 1, 0], ["update", 1, 990], ["get", 1, 5], ["get", 1, 6]], 300],
         "expected": {"answers": [1490, 990, 990], "db_reads": 2}},
        {"input": [_P, [["get", 1, 0], ["get", 2, 1], ["get", 1, 2], ["get", 2, 3]], 60],
         "expected": {"answers": [1490, 350, 1490, 350], "db_reads": 2}},
        {"input": [_P, [["get", 3, 0], ["get", 3, 1]], 60],
         "expected": {"answers": [None, None], "db_reads": 2}},
        {"input": [_P, [["get", 1, 0], ["get", 2, 0], ["update", 2, 300], ["get", 1, 1], ["get", 2, 1]], 60],
         "expected": {"answers": [1490, 350, 1490, 300], "db_reads": 3}},
    ],
)

PERSONAL_BUG = C('''
class FakeCache:
    def __init__(self):
        self.data = {}

    def get(self, key):
        return self.data.get(key)

    def set(self, key, value):
        self.data[key] = value


class FakeDB:
    def __init__(self, products):
        self.products = products
        self.reads = 0

    def select_category(self, category_id):
        # SELECT id, price FROM products WHERE category_id = %s ORDER BY id
        self.reads += 1
        return [dict(p) for p in self.products if p["category_id"] == category_id]


def catalog_page(db, cache, category_id, user):
    # user — {"id": ..., "discount": процент} или None для гостя
    key = f"catalog:{category_id}"
    cached = cache.get(key)
    if cached is not None:
        return cached
    products = db.select_category(category_id)
    discount = user["discount"] if user else 0
    page = [
        {"id": p["id"], "price": p["price"] * (100 - discount) // 100}
        for p in products
    ]
    cache.set(key, page)
    return page


def run(products, requests):
    # Тестовый стенд, не меняй. requests — список [category_id, user или None]
    db = FakeDB(products)
    cache = FakeCache()
    pages = [catalog_page(db, cache, category_id, user) for category_id, user in requests]
    return {"pages": pages, "db_reads": db.reads}
''')

PERSONAL_FIX = PERSONAL_BUG.replace(C('''
    key = f"catalog:{category_id}"
    cached = cache.get(key)
    if cached is not None:
        return cached
    products = db.select_category(category_id)
    discount = user["discount"] if user else 0
    page = [
        {"id": p["id"], "price": p["price"] * (100 - discount) // 100}
        for p in products
    ]
    cache.set(key, page)
    return page
'''), C('''
    key = f"catalog:{category_id}"
    products = cache.get(key)
    if products is None:
        products = db.select_category(category_id)
        cache.set(key, products)
    # персональная часть — поверх общего кэша, в кэш не попадает
    discount = user["discount"] if user else 0
    return [
        {"id": p["id"], "price": p["price"] * (100 - discount) // 100}
        for p in products
    ]
'''))
assert PERSONAL_FIX != PERSONAL_BUG

_CP = [{"id": 1, "category_id": 5, "price": 1000},
       {"id": 2, "category_id": 5, "price": 350},
       {"id": 3, "category_id": 6, "price": 2000}]

task(
    "be-caching-03", "find_bug", 4, "qa",
    "Ира: «Странный баг в каталоге: обычный покупатель видит цены со скидкой 15%, как у оптовиков, а вчера было наоборот — оптовик жаловался на розничные цены. После сброса кэша всё „перемешивается“ заново. Стас уже спрашивает, не утекают ли у нас персональные условия».",
    "Исправь catalog_page: каждый пользователь должен видеть цены со своей скидкой (гость — без скидки), а список товаров категории — читаться из базы один раз и дальше браться из кэша для всех пользователей.",
    "В общий ключ catalog:{category_id} попал уже персонализированный ответ: цены с учётом скидки того, кто первым прогрел кэш. Все остальные получают его цены — это утечка персональных условий, и Ира правильно заметила, что картина меняется после каждого сброса кэша. Добавить user_id в ключ — плохое решение: каталог станет кэшироваться отдельно для каждого пользователя, hit rate упадёт почти до нуля, а память Redis займут тысячи копий одного и того же. Правильно разделить данные: общую часть (товары и базовые цены) кэшировать по общему ключу, а персональную (скидку) накладывать после чтения из кэша. Тот же принцип работает и для CDN: публичная страница кэшируется, а персональные блоки догружаются отдельным некэшируемым запросом. Заодно обрати внимание: кэшируемый список теперь используется всеми запросами, поэтому его нельзя изменять на месте — мы строим новый список.",
    ["Чьи цены окажутся в кэше после первого запроса?",
     "Раздели данные: что одинаково для всех, а что зависит от пользователя?",
     "Кэшируй сырой список товаров из базы, а скидку применяй уже после cache.get, каждый раз заново."],
    code=PERSONAL_BUG, language="python", entry="run",
    answer=PERSONAL_FIX,
    tests=[
        {"input": [_CP, [[5, {"id": 7, "discount": 15}], [5, None], [5, {"id": 9, "discount": 5}]]],
         "expected": {"pages": [
             [{"id": 1, "price": 850}, {"id": 2, "price": 297}],
             [{"id": 1, "price": 1000}, {"id": 2, "price": 350}],
             [{"id": 1, "price": 950}, {"id": 2, "price": 332}]],
             "db_reads": 1}},
        {"input": [_CP, [[5, None], [5, {"id": 7, "discount": 15}]]],
         "expected": {"pages": [
             [{"id": 1, "price": 1000}, {"id": 2, "price": 350}],
             [{"id": 1, "price": 850}, {"id": 2, "price": 297}]],
             "db_reads": 1}},
        {"input": [_CP, [[5, None], [6, None], [6, {"id": 7, "discount": 10}], [5, None]]],
         "expected": {"pages": [
             [{"id": 1, "price": 1000}, {"id": 2, "price": 350}],
             [{"id": 3, "price": 2000}],
             [{"id": 3, "price": 1800}],
             [{"id": 1, "price": 1000}, {"id": 2, "price": 350}]],
             "db_reads": 2}},
    ],
)

task(
    "be-caching-04", "incident", 5, "devops_colleague",
    "Дима: «Каждые пять минут — ровно в :00, :05, :10 — CPU базы улетает в 100%, и секунд на двадцать главная „Лампового Маркета“ отвечает 504. Между пиками всё тихо, запросов больше не стало. Глянь метрики и логи, пока маркетинг не запустил рассылку».",
    "Выбери все действия, которые действительно решают проблему.",
    "Картина — классический cache stampede. Ключ главной живёт ровно 300 секунд, и в момент протухания 2 000 запросов в секунду одновременно получают промах и каждый запускает тяжёлое построение на 2,4 секунды. Сотни одинаковых запросов забивают пул и CPU базы, построение от этого замедляется, и промахов становится ещё больше. Лечат две вещи: лок на перестроение (SET ...:lock NX EX — строит один запрос, остальные коротко ждут или отдают старое значение) и обновление заранее: фоновая задача пересобирает ключ до истечения TTL или сервис отдаёт устаревшее значение, пока строится новое (stale-while-revalidate), — тогда популярный ключ никогда не пропадает целиком. Короткий TTL сделает штормы чаще, а пул на 1 000 соединений лишь добавит нагрузки упёршейся в CPU базе. Кэш в памяти каждого пода превратит один шторм в N штормов, а отключение кэша сделает из пика постоянную нагрузку. Случайный разброс TTL (jitter) полезен, когда одновременно протухают тысячи разных ключей; здесь ключ один, и нужен именно контроль над его перестроением.",
    ["Сколько запросов одновременно видят промах в 19:05:00 и что каждый из них делает?",
     "Нужно, чтобы значение строил один запрос, а не тысяча.",
     "Второе направление — сделать так, чтобы горячий ключ вообще не пропадал из кэша."],
    code=C('''
# Grafana: главная страница, 19:04:50–19:10:00
time      rps_home  redis_hit  db_active_conn  db_cpu   p99_home
19:04:50    2 150     99.6%          6           18%      85 ms
19:05:00    2 180     61.2%        200          100%   9 400 ms
19:05:10    2 140     74.9%        200          100%  12 100 ms
19:05:20    2 170     99.5%         31           64%     410 ms
19:05:30    2 160     99.6%          7           19%      88 ms
...
19:10:00    2 190     58.7%        200          100%  10 800 ms

# market-api, 19:05:00–19:05:03
19:05:00.004 INFO cache miss key=home:blocks:v3 -> build_home_blocks()
19:05:00.006 INFO cache miss key=home:blocks:v3 -> build_home_blocks()
19:05:00.009 INFO cache miss key=home:blocks:v3 -> build_home_blocks()
... ещё 1 312 строк "cache miss key=home:blocks:v3" за 2 секунды
19:05:02.771 WARN db pool exhausted: waited 5.0s for connection
19:05:03.118 INFO home:blocks:v3 built in 2.41s, SET EX 300

# home.py
blocks = cache.get("home:blocks:v3")
if blocks is None:
    blocks = build_home_blocks()          # 6 тяжёлых запросов, ~2.4 с
    cache.set("home:blocks:v3", blocks, ex=300)
'''),
    language="text",
    options=[
        "Лок на перестроение: первый запрос берёт SET home:blocks:v3:lock 1 NX EX 30 и строит, остальные ждут или отдают старое",
        "Обновлять ключ заранее в фоне или отдавать устаревшее значение, пока строится новое (stale-while-revalidate)",
        "Уменьшить TTL до 30 секунд, чтобы кэш был свежее и каждое перестроение проходило быстрее",
        "Увеличить пул соединений к базе до 1 000, чтобы запросы не ждали по 5 секунд свободного соединения",
        "Перенести кэш главной из Redis в память каждого пода, чтобы убрать сетевой поход при каждом запросе",
        "Отключить кэш главной: пики явно из-за него, а без кэша нагрузка на базу будет ровной и предсказуемой",
        "Добавить к TTL случайный разброс (jitter) ±30 секунд, чтобы ключи протухали не одновременно",
    ],
    answer=[0, 1],
    time_limit=20,
)


# =====================================================================
if __name__ == "__main__":
    shuffle_options(TASKS)
    out = {"track": "backend", "part": "b4", "topics": TOPICS, "tasks": TASKS}
    path = "Tools/Content/out/backend_b4.json"
    with open(path, "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    print("written", path, len(TOPICS), "topics", len(TASKS), "tasks")
    for tp in TOPICS:
        print(tp["topic_id"], len(tp["theory"]))
