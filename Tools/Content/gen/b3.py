#!/usr/bin/env python3
"""Генератор контента «Стажёра»: backend, часть b3 (junior_plus).

Темы: be-sql-joins, be-validation, be-api-errors, be-auth, be-docker-local.
Запуск: python3 gen/b3.py  ->  out/backend_b3.json
"""
import json
import random
import os

GRADE = "junior_plus"
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}


def xp(d):
    return int(round(XP_BASE[d] * XP_MULT[GRADE] / 5.0)) * 5


TASKS = []
TOPICS = []


def topic(topic_id, theory):
    TOPICS.append({"topic_id": topic_id, "theory": theory.strip("\n")})


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


def task(topic_id, n, typ, diff, char, story, question, explanation, hints,
         code=None, language=None, options=None, answer=None, tests=None,
         entry=None, time_limit=None):
    content = {
        "question": question.strip("\n"),
        "code": code,
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content["options"] = options
    content["correct_answer"] = answer
    content["test_cases"] = tests
    TASKS.append({
        "task_id": "%s-%02d" % (topic_id, n),
        "topic_id": topic_id,
        "grade": GRADE,
        "type": typ,
        "difficulty": diff,
        "xp_reward": xp(diff),
        "time_limit_minutes": time_limit,
        "character": char,
        "story": story.strip("\n"),
        "content": content,
        "explanation": explanation.strip("\n"),
        "hints": hints,
    })


# =====================================================================
# be-sql-joins
# =====================================================================
T = "be-sql-joins"

topic(T, """
JOIN склеивает строки двух таблиц по условию, а GROUP BY сворачивает много строк в одну с агрегатами. На этих двух вещах держатся почти все отчёты: «выручка по категориям», «клиенты без заказов», «топ тренеров за месяц».

• INNER JOIN (или просто JOIN) оставляет только пары, для которых выполнилось условие ON. Клиент без заказов в результат не попадёт.
• LEFT JOIN оставляет все строки левой таблицы. Если пары справа нет, её колонки заполняются NULL.
• Анти-джойн — «те, у кого нет ни одного…»: LEFT JOIN и проверка ключа правой таблицы на IS NULL (или NOT EXISTS).

    SELECT c.id, c.name
    FROM customers c
    LEFT JOIN orders o ON o.customer_id = c.id
    WHERE o.id IS NULL;

Главная ловушка LEFT JOIN — фильтр по правой таблице. WHERE выполняется после соединения и выкидывает строки, где справа NULL: LEFT JOIN молча превращается в INNER. Нужны все клиенты и только оплаченные заказы — условие пиши в ON:

    LEFT JOIN orders o ON o.customer_id = c.id AND o.status = 'paid'

Порядок выполнения: FROM и JOIN → WHERE → GROUP BY → HAVING → SELECT → ORDER BY → LIMIT. Отсюда правила:
• WHERE фильтрует строки до группировки и не видит агрегатов. HAVING фильтрует готовые группы: HAVING COUNT(*) >= 3.
• При GROUP BY в SELECT можно выводить только колонки из GROUP BY и агрегаты. SQLite прощает лишнюю колонку и берёт значение из случайной строки группы, PostgreSQL честно падает с ошибкой.

Агрегаты: COUNT, SUM, AVG, MIN, MAX.
• COUNT(*) считает строки, COUNT(col) — строки, где col не NULL. После LEFT JOIN у клиента без заказов одна строка с NULL: COUNT(*) даст 1, COUNT(o.id) — 0.
• COUNT(DISTINCT col) считает уникальные значения.
• SUM, AVG, MIN, MAX пропускают NULL. Если в группе одни NULL, SUM вернёт NULL, а не 0 — в отчётах пиши COALESCE(SUM(x), 0).

Размножение строк (fan-out). Присоедини к заказам позиции заказа (один-ко-многим) — и каждый заказ повторится столько раз, сколько в нём позиций. SUM(o.total) после такого JOIN завышен в разы, а COUNT(o.id) считает позиции, а не заказы. Что делать: не джойнить лишнего, использовать COUNT(DISTINCT …), а «многие» сначала агрегировать в подзапросе и только потом соединять.

Подзапросы:
• скалярный возвращает одно значение: WHERE price > (SELECT AVG(price) FROM products);
• коррелированный ссылается на внешнюю строку и считается для каждой: «цена выше средней по своей категории»;
• EXISTS / IN проверяют наличие: WHERE EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = c.id). С NOT IN осторожно: один NULL в подзапросе — и результат пустой;
• подзапрос во FROM или CTE (WITH stats AS (…)) — готовая табличка, которую дальше джойнят.

Привычки, которые экономят нервы: короткие алиасы таблиц, проверка числа строк до и после JOIN и явный ORDER BY — без него порядок строк не гарантирован.
""")

SQL1 = """CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT, email TEXT);
CREATE TABLE orders (id INTEGER PRIMARY KEY, customer_id INTEGER, total INTEGER, status TEXT);
INSERT INTO customers VALUES
  (1, 'Аня', 'anya@mail.ru'),
  (2, 'Борис', 'boris@mail.ru'),
  (3, 'Вика', 'vika@mail.ru'),
  (4, 'Гоша', 'gosha@mail.ru'),
  (5, 'Даша', 'dasha@mail.ru');
INSERT INTO orders VALUES
  (1, 1, 2490, 'paid'),
  (2, 1, 990, 'paid'),
  (3, 3, 15990, 'cancelled'),
  (4, 4, 450, 'new');

-- твой запрос ниже
"""

task(T, 1, "write_code", 2, "manager",
     story="Стас: «Маркетинг хочет отправить промокод всем, кто зарегистрировался в „Ламповом Маркете“, но так ни разу и не оформил заказ. Отменённый заказ — всё равно заказ, таким не шлём. Список email нужен до обеда».",
     question="Допиши запрос: выведи id и email клиентов, у которых нет ни одного заказа (в любом статусе). Сортировка по id.",
     code=SQL1, language="sql",
     answer=SQL1 + """SELECT c.id, c.email
FROM customers c
LEFT JOIN orders o ON o.customer_id = c.id
WHERE o.id IS NULL
ORDER BY c.id;
""",
     tests=[{"input": None, "expected": [[2, "boris@mail.ru"], [5, "dasha@mail.ru"]]}],
     explanation="LEFT JOIN сохраняет всех клиентов, а у тех, кто ничего не заказывал, колонки orders заполняются NULL. Условие WHERE o.id IS NULL оставляет ровно таких — это приём «анти-джойн». Проверять нужно ключ правой таблицы: у настоящего заказа id никогда не бывает NULL, а вот другие колонки (например, status) могли бы оказаться NULL и у реальной строки. Ту же задачу решает WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = c.id), в PostgreSQL оба варианта обычно дают одинаковый план. А NOT IN (SELECT customer_id FROM orders) опасен: попадётся в подзапросе хоть один NULL — и результат станет пустым.",
     hints=["Обычный JOIN выкинет клиентов без заказов. Какой JOIN сохранит всех из левой таблицы?",
            "После LEFT JOIN у клиентов без заказов в колонках orders стоит NULL.",
            "LEFT JOIN orders o ON o.customer_id = c.id … WHERE o.id IS NULL"])

SQL2 = """CREATE TABLE trainers (id INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE workouts (id INTEGER PRIMARY KEY, trainer_id INTEGER, starts_at TEXT);
CREATE TABLE bookings (id INTEGER PRIMARY KEY, workout_id INTEGER, client TEXT, status TEXT);
INSERT INTO trainers VALUES (1, 'Олег'), (2, 'Марта'), (3, 'Кирилл'), (4, 'Соня');
INSERT INTO workouts VALUES
  (1, 1, '2026-09-02 19:00'), (2, 1, '2026-09-09 19:00'),
  (3, 2, '2026-09-03 08:00'),
  (4, 3, '2026-08-28 18:00'), (5, 3, '2026-09-05 18:00'),
  (6, 4, '2026-09-06 10:00');
INSERT INTO bookings VALUES
  (1, 1, 'Аня', 'visited'), (2, 1, 'Боря', 'visited'), (3, 1, 'Вера', 'cancelled'),
  (4, 2, 'Аня', 'visited'), (5, 2, 'Гена', 'visited'),
  (6, 3, 'Даня', 'visited'), (7, 3, 'Ева', 'visited'), (8, 3, 'Женя', 'visited'),
  (9, 4, 'Зоя', 'visited'), (10, 4, 'Илья', 'visited'), (11, 4, 'Катя', 'visited'),
  (12, 5, 'Лёва', 'visited'), (13, 5, 'Маша', 'no_show'),
  (14, 6, 'Нина', 'visited'), (15, 6, 'Оля', 'visited'), (16, 6, 'Петя', 'no_show'), (17, 6, 'Рома', 'cancelled');

-- твой запрос ниже
"""

task(T, 2, "write_code", 3, "client",
     story="Артур, директор фитнес-клуба «Жми»: «Хочу премировать тренеров за сентябрь. Считаем только тех, у кого за месяц набралось хотя бы 3 реальных посещения — отмены и неявки не в счёт».",
     question="Допиши запрос: для каждого тренера посчитай посещения (записи bookings со статусом 'visited') на тренировках, которые начинались в сентябре 2026. Выведи имя тренера и число посещений — только тех, у кого посещений не меньше 3. Сортировка: по числу посещений по убыванию, при равенстве — по имени.",
     code=SQL2, language="sql",
     answer=SQL2 + """SELECT t.name, COUNT(*) AS visits
FROM trainers t
JOIN workouts w ON w.trainer_id = t.id
JOIN bookings b ON b.workout_id = w.id
WHERE b.status = 'visited'
  AND w.starts_at >= '2026-09-01' AND w.starts_at < '2026-10-01'
GROUP BY t.id, t.name
HAVING COUNT(*) >= 3
ORDER BY visits DESC, t.name;
""",
     tests=[{"input": None, "expected": [["Олег", 4], ["Марта", 3]]}],
     explanation="JOIN собирает цепочку тренер → тренировка → запись, WHERE оставляет только посещения за сентябрь, и лишь потом GROUP BY сворачивает строки по тренеру. Условие «не меньше 3» относится к уже посчитанному агрегату, поэтому оно в HAVING: WHERE выполняется до группировки и про COUNT ничего не знает. Обычный JOIN здесь уместен — тренеры без посещений в отчёт и не должны попасть. Диапазон >= '2026-09-01' AND < '2026-10-01' надёжнее, чем LIKE '2026-09%': он так же работает с настоящим timestamp в PostgreSQL и может использовать индекс. Группируй по t.id, а не только по имени, чтобы два тренера-тёзки не слились в одну строку. Без фильтра по статусу в отчёт попала бы Соня, а без фильтра по датам — Кирилл с его августом.",
     hints=["Цепочка trainers → workouts → bookings — это два JOIN.",
            "Статус и даты фильтруй в WHERE, а «не меньше 3 посещений» — условие на агрегат.",
            "GROUP BY t.id, t.name HAVING COUNT(*) >= 3 ORDER BY visits DESC, t.name"])

SQL3_SCHEMA = """CREATE TABLE clients (id INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE orders (id INTEGER PRIMARY KEY, client_id INTEGER, status TEXT, total INTEGER);
INSERT INTO clients VALUES (1, 'Алла'), (2, 'Вадим'), (3, 'Галина'), (4, 'Павел');
INSERT INTO orders VALUES
  (1, 1, 'paid', 540),
  (2, 1, 'paid', 320),
  (3, 1, 'cancelled', 780),
  (4, 2, 'cancelled', 450),
  (5, 4, 'paid', 990);

-- запрос из отчёта
"""

task(T, 3, "find_bug", 3, "client",
     story="Нина, владелица пекарни «Батон»: «Хочу подарить купон всем постоянным клиентам. В отчёте должны быть все — даже те, кто ещё ни разу не оплатил заказ, у них пусть стоит ноль. А у меня в отчёте всего двое. Где остальные?»",
     question="Запрос должен вернуть всех клиентов и число их оплаченных заказов (0, если оплаченных нет), сортировка по имени. Найди и исправь ошибки в запросе.",
     code=SQL3_SCHEMA + """SELECT c.name, COUNT(*) AS paid_orders
FROM clients c
LEFT JOIN orders o ON o.client_id = c.id
WHERE o.status = 'paid'
GROUP BY c.id, c.name
ORDER BY c.name;
""",
     language="sql",
     answer=SQL3_SCHEMA + """SELECT c.name, COUNT(o.id) AS paid_orders
FROM clients c
LEFT JOIN orders o ON o.client_id = c.id AND o.status = 'paid'
GROUP BY c.id, c.name
ORDER BY c.name;
""",
     tests=[{"input": None, "expected": [["Алла", 2], ["Вадим", 0], ["Галина", 0], ["Павел", 1]]}],
     explanation="Ошибок две, и первая прячет вторую. Условие o.status = 'paid' в WHERE выполняется уже после соединения: у Галины справа NULL, у Вадима — отменённый заказ, и WHERE выкидывает обоих, то есть LEFT JOIN фактически стал INNER. Фильтр по правой таблице нужно перенести в ON — тогда он решает, какие заказы приклеятся, а клиенты остаются все. После этого всплывает вторая ошибка: COUNT(*) считает строки, а у клиента без оплаченных заказов строка одна, с NULL, и он получит 1 вместо 0. COUNT(o.id) считает только не-NULL значения и даёт честный ноль.",
     hints=["Сколько строк возвращает запрос сейчас и кого не хватает?",
            "WHERE по колонке правой таблицы после LEFT JOIN отбрасывает строки с NULL. Где ещё можно написать условие?",
            "Перенеси статус в ON и посмотри, сколько теперь насчитает COUNT(*) у Галины."])

SQL4 = """CREATE TABLE categories (id INTEGER PRIMARY KEY, name TEXT);
CREATE TABLE products (id INTEGER PRIMARY KEY, category_id INTEGER, title TEXT, price INTEGER, is_active INTEGER);
INSERT INTO categories VALUES (1, 'Лампы'), (2, 'Кабели'), (3, 'Умный дом');
INSERT INTO products VALUES
  (1, 1, 'Лампа Эдисона', 490, 1),
  (2, 1, 'Торшер Луч', 5990, 1),
  (3, 1, 'Ночник Сова', 1290, 1),
  (4, 1, 'Бра Модерн', 3190, 1),
  (5, 1, 'Люстра Каскад', 25990, 0),
  (6, 2, 'Кабель USB-C 1м', 390, 1),
  (7, 2, 'Кабель HDMI 2м', 790, 1),
  (8, 2, 'Удлинитель 5м', 1190, 1),
  (9, 3, 'Умная лампа', 2490, 1),
  (10, 3, 'Датчик движения', 1490, 1),
  (11, NULL, 'Подарочная карта', 3000, 1);

-- твой запрос ниже
"""

task(T, 4, "write_code", 4, "manager",
     story="Стас: «Категорийщики хотят видеть товары, которые заметно дороже остальных в своей категории, — будем двигать их в блок „Премиум“. Только архивные не трогай: в прошлый раз архивная люстра за 26 тысяч испортила всю статистику».",
     question="Допиши запрос: выведи название категории, название товара и цену для активных товаров (is_active = 1), цена которых строго выше средней цены активных товаров их категории. Товары без категории не выводи. Сортировка: по названию категории, затем по цене по убыванию.",
     code=SQL4, language="sql",
     answer=SQL4 + """SELECT c.name, p.title, p.price
FROM products p
JOIN categories c ON c.id = p.category_id
WHERE p.is_active = 1
  AND p.price > (
    SELECT AVG(p2.price)
    FROM products p2
    WHERE p2.category_id = p.category_id AND p2.is_active = 1
  )
ORDER BY c.name, p.price DESC;
""",
     tests=[{"input": None, "expected": [["Кабели", "Удлинитель 5м", 1190], ["Лампы", "Торшер Луч", 5990],
                                         ["Лампы", "Бра Модерн", 3190], ["Умный дом", "Умная лампа", 2490]]}],
     explanation="Средняя цена у каждой категории своя, поэтому нужен коррелированный подзапрос: он ссылается на p.category_id внешней строки и пересчитывается для каждого товара. Фильтр is_active нужен дважды — во внешнем запросе (выводим только активные) и в подзапросе (среднее считаем по активным), иначе люстра за 25 990 поднимет среднее по лампам до 7390 и в отчёт не попадёт ни одна лампа. INNER JOIN с categories заодно отсекает товар без категории. Кабель HDMI стоит ровно столько, сколько в среднем стоят кабели (790), и «строго выше» его не пропускает. Альтернатива — один раз посчитать средние в подзапросе во FROM (или в CTE) с GROUP BY category_id и приджойнить: на больших таблицах такой вариант часто понятнее и не медленнее.",
     hints=["Среднюю по своей категории даст подзапрос, который ссылается на категорию внешнего товара.",
            "В подзапросе тоже отфильтруй is_active = 1, иначе архивная люстра испортит среднее.",
            "WHERE p.is_active = 1 AND p.price > (SELECT AVG(p2.price) FROM products p2 WHERE p2.category_id = p.category_id AND p2.is_active = 1)"])

task(T, 5, "code_review", 4, "teamlead",
     story="Марина, сеньор: «Джун принёс запрос для отчёта „Самые ценные клиенты сентября“. Финансы сверили с банком: у некоторых клиентов оплат в отчёте втрое больше, чем было на самом деле. Посмотри запрос и отметь настоящие проблемы».",
     question="Выбери все замечания, которые действительно стоит оставить в ревью.",
     code="""-- customers(id, email)
-- orders(id, customer_id, created_at)
-- order_items(id, order_id, product_id, qty)
-- payments(id, order_id, amount, status)  -- status: succeeded | failed | refunded

SELECT c.id, c.email,
       COUNT(o.id)   AS orders_count,
       SUM(p.amount) AS paid_total
FROM customers c
JOIN orders o        ON o.customer_id = c.id
JOIN order_items oi  ON oi.order_id = o.id
LEFT JOIN payments p ON p.order_id = o.id
WHERE o.created_at >= '2026-09-01' AND o.created_at < '2026-10-01'
GROUP BY c.id, c.email
HAVING SUM(p.amount) > 10000
ORDER BY paid_total DESC;
""",
     language="sql",
     options=[
         "JOIN с order_items не нужен и размножает строки: заказ из трёх позиций попадёт в SUM платежей трижды",
         "В сумму идут неуспешные и возвращённые платежи — нужен p.status = 'succeeded', причём в ON, а не в WHERE",
         "COUNT(o.id) считает строки после JOIN, а не заказы — нужен COUNT(DISTINCT o.id) или агрегация платежей в подзапросе",
         "HAVING SUM(p.amount) > 10000 лучше перенести в WHERE: фильтр до группировки отсекает строки раньше и работает быстрее",
         "ORDER BY по алиасу paid_total не сработает: алиасы из SELECT не видны в ORDER BY, нужно повторить SUM(p.amount)",
         "c.email в GROUP BY лишний и заставляет базу сравнивать длинные строки — группируй только по c.id",
     ],
     answer=[0, 1, 2],
     explanation="Главная беда — размножение строк (fan-out). orders → order_items — связь один-ко-многим, и после JOIN каждый заказ повторяется столько раз, сколько в нём позиций; платежи приклеиваются к каждой копии, поэтому SUM(p.amount) растёт кратно числу позиций — отсюда «втрое больше». По той же причине COUNT(o.id) считает строки, а не заказы. Лечение: убрать ненужный JOIN с позициями, а платежи сначала сгруппировать по заказу в подзапросе (SELECT order_id, SUM(amount) … GROUP BY order_id) и только потом соединять. Неуспешные платежи отсекают в ON, чтобы заказы без оплат не пропали из LEFT JOIN. HAVING в WHERE не перенести — WHERE выполняется до группировки и не видит SUM, сортировка по алиасу в PostgreSQL работает, а лишняя колонка в GROUP BY при группировке по первичному ключу ничего не стоит.",
     hints=["Сколько строк получится для одного заказа с тремя позициями и одним платежом?",
            "Какие статусы бывают у платежей и все ли из них — деньги на счёте?"])


# =====================================================================
# be-validation
# =====================================================================
T = "be-validation"

topic(T, """
Всё, что приходит снаружи — тело запроса, query-параметры, заголовки, файлы, вебхуки партнёров, — недоверенные данные. Валидация на входе API решает две задачи: не пустить мусор в бизнес-логику и базу и объяснить клиенту, что именно не так, чтобы фронт показал подсказку под нужным полем.

Что проверяют:
• обязательность и тип: поле есть, и это число, а не строка;
• диапазоны и длины: количество от 1 до 99, имя от 2 до 50 символов;
• формат: email, телефон, дата;
• допустимые значения: статус только из списка;
• связи полей: дата окончания позже даты начала;
• бизнес-правила (товар есть на складе) — это уже слой сервиса, а не схема.

Хорошая ошибка валидации — «поле → что не так», и сразу все ошибки, а не первая: иначе пользователь чинит форму по одному полю за отправку. FastAPI в этом случае отвечает 422 со списком ошибок, у каждой есть loc (путь к полю), msg и type.

Pydantic v2 — стандарт в FastAPI. Поля описывают типами, ограничения задают через Field:

    class OrderItemIn(BaseModel):
        model_config = ConfigDict(extra="forbid")
        product_id: int = Field(gt=0)
        quantity: int = Field(ge=1, le=99)
        comment: str | None = Field(default=None, max_length=500)
        price: Decimal = Field(max_digits=10, decimal_places=2)

Что важно знать:
• По умолчанию режим lax: строка "3" станет int 3, 3.0 — тоже 3, а 2.5 даст ошибку. True в поле int станет 1. Не нужно такое приведение — включай strict=True для поля или модели.
• В v2 тип str | None (он же Optional[str]) не делает поле необязательным. Необязательным его делает только значение по умолчанию: comment: str | None = None.
• @field_validator("name") вместе с @classmethod получает уже проверенное значение и обязан вернуть результат. Забыл return — в поле тихо окажется None. Ошибку сообщают через raise ValueError("текст").
• @model_validator(mode="after") — для проверок, где участвуют несколько полей.
• extra="forbid" запрещает лишние поля, по умолчанию они молча отбрасываются.
• EmailStr требует пакет email-validator. Деньги — Decimal, а не float: во float 0.1 + 0.2 не равно 0.3.

Разделяй модели входа и выхода. Во входную модель кладут только то, что клиенту разрешено менять. Окажутся там role, owner_id или balance, а код сделает setattr всех полей в ORM-объект — получится mass assignment: пользователь сам назначит себя админом. Для PATCH поля делают необязательными и применяют только присланные: data.model_dump(exclude_unset=True).

Без Pydantic валидатор пишут руками по тем же принципам: каждое поле проверяется отдельно, ошибки копятся в словаре, порядок проверок — «есть ли → тип → формат и диапазон». Краевые случаи: в Python bool — подкласс int, и isinstance(True, int) вернёт True, поэтому bool проверяют отдельно; строка из пробелов после strip() пустая.
""")

task(T, 1, "quiz", 2, "qa",
     story="Ира: «Мобилка шлёт количество товара как попало: то числом, то строкой. Пишу тест-кейсы на корзину — скажи, что из этого сейчас проходит валидацию, а что получит 422. От этого зависит, включать ли strict-режим».",
     question="Поле корзины описано как quantity: int = Field(gt=0, le=99), Pydantic v2 в режиме по умолчанию (lax). Какие значения quantity пройдут валидацию? Выбери все верные.",
     code="""from pydantic import BaseModel, Field


class CartItemIn(BaseModel):
    product_id: int
    quantity: int = Field(gt=0, le=99)
""",
     language="python",
     options=[
         "\"3\"",
         "3.0",
         "2.5",
         "0",
         "100",
         "\"три\"",
     ],
     answer=[0, 1],
     explanation="В lax-режиме Pydantic приводит типы, когда при этом ничего не теряется: строка \"3\" превращается в число 3, а 3.0 — в целое 3, потому что дробной части нет. 2.5 не пройдёт: при переводе в int потеряется половинка, и Pydantic вернёт ошибку int_from_float. 0 и 100 — нормальные целые, но нарушают ограничения gt=0 и le=99. \"три\" числом не станет никак. Если строки от мобилки прощать нельзя, поле объявляют с strict=True — тогда пройдут только настоящие целые числа.",
     hints=["В lax-режиме Pydantic приводит типы, если при этом ничего не теряется.",
            "Проверь каждое значение дважды: приводится ли оно к int и попадает ли в диапазон 1..99."])

VAL_START = """def validate_signup(data):
    errors = {}
    # твой код
    return errors
"""

VAL_ANSWER = """def is_valid_email(email):
    if email.count("@") != 1:
        return False
    local, domain = email.split("@")
    if not local or "." not in domain:
        return False
    return not domain.startswith(".") and not domain.endswith(".")


def validate_signup(data):
    errors = {}

    name = data.get("name")
    if name is None:
        errors["name"] = "обязательное поле"
    elif not isinstance(name, str):
        errors["name"] = "неверный тип"
    elif not 2 <= len(name.strip()) <= 50:
        errors["name"] = "от 2 до 50 символов"

    phone = data.get("phone")
    if phone is None:
        errors["phone"] = "обязательное поле"
    elif not isinstance(phone, str):
        errors["phone"] = "неверный тип"
    elif not (len(phone) == 12 and phone.startswith("+7") and phone[2:].isdigit()):
        errors["phone"] = "формат +7XXXXXXXXXX"

    age = data.get("age")
    if age is None:
        errors["age"] = "обязательное поле"
    elif isinstance(age, bool) or not isinstance(age, int):
        errors["age"] = "неверный тип"
    elif not 14 <= age <= 80:
        errors["age"] = "от 14 до 80"

    email = data.get("email")
    if email is not None:
        if not isinstance(email, str):
            errors["email"] = "неверный тип"
        elif not is_valid_email(email):
            errors["email"] = "некорректный email"

    return errors
"""

task(T, 2, "write_code", 3, "client",
     story="Артур из «Жми»: «На сайте форма записи на пробную тренировку, а в базе у нас люди с именем из пробелов, телефоном „позвоните мне“ и возрастом 250. Пусть сервер проверяет данные и говорит, какое поле не так, — фронт покажет подсказку под полем».",
     question="""Напиши validate_signup(data): data — словарь из JSON. Верни словарь {поле: сообщение} со всеми ошибками (пустой словарь — всё в порядке). На каждое поле — одно сообщение, первое сработавшее по порядку:
• name, phone, age — обязательные: поля нет или оно null → "обязательное поле". email — необязательный: отсутствие и null — норма.
• Неверный тип → "неверный тип": name, phone, email — строки, age — целое число (bool не подходит).
• name: после strip() длина от 2 до 50, иначе "от 2 до 50 символов".
• phone: ровно "+7" и 10 цифр, иначе "формат +7XXXXXXXXXX".
• age: от 14 до 80 включительно, иначе "от 14 до 80".
• email: ровно одна "@", часть до неё непустая, часть после содержит точку, но не начинается и не заканчивается ею, иначе "некорректный email".""",
     code=VAL_START, language="python", entry="validate_signup",
     answer=VAL_ANSWER,
     tests=[
         {"input": [{"name": "Ира", "phone": "+79991234567", "age": 29, "email": "ira@zhmi.ru"}], "expected": {}},
         {"input": [{"name": "  Олег  ", "phone": "+79990000000", "age": 14}], "expected": {}},
         {"input": [{"name": "   ", "phone": "89991234567", "age": 13, "email": "oleg@zhmi"}],
          "expected": {"name": "от 2 до 50 символов", "phone": "формат +7XXXXXXXXXX", "age": "от 14 до 80", "email": "некорректный email"}},
         {"input": [{"phone": "+7999123456a", "age": "25", "email": None}],
          "expected": {"name": "обязательное поле", "phone": "формат +7XXXXXXXXXX", "age": "неверный тип"}},
         {"input": [{"name": "Катя", "phone": 79991234567, "age": True, "email": "katya@@zhmi.ru"}],
          "expected": {"phone": "неверный тип", "age": "неверный тип", "email": "некорректный email"}},
         {"input": [{"name": None, "phone": "+7999123456", "age": 30.0, "email": "@zhmi.ru"}],
          "expected": {"name": "обязательное поле", "phone": "формат +7XXXXXXXXXX", "age": "неверный тип", "email": "некорректный email"}},
         {"input": [{"name": "Ян", "phone": "+79991234567", "age": 80, "email": "yan@.ru"}],
          "expected": {"email": "некорректный email"}},
         {"input": [{"name": "Ян", "phone": "+79991234567", "age": 81, "email": 5}],
          "expected": {"age": "от 14 до 80", "email": "неверный тип"}},
     ],
     explanation="Каждое поле проверяется независимо, а ошибки копятся в словаре — фронт покажет подсказки под всеми полями сразу, а не по одной за отправку. Внутри поля проверки идут от общего к частному: есть ли значение, того ли оно типа, и только потом формат и диапазон. Иначе len() от числа или сравнение строки с числом уронят валидатор с TypeError, и клиент получит 500 вместо понятной ошибки. Ловушка с возрастом: bool в Python — подкласс int, isinstance(True, int) вернёт True, поэтому bool отсекают отдельно (а 30.0 — float, и он тоже не подходит). Строку из пробелов ловит strip(). В проекте то же сделает Pydantic, но логику «тип → формат → диапазон, ошибки по полям» нужно понимать, чтобы писать свои валидаторы.",
     hints=["Для каждого поля: есть ли → тип → формат или диапазон. if/elif даст ровно одно сообщение на поле.",
            "isinstance(True, int) — это True. Проверь bool раньше int.",
            "Email: email.count(\"@\") == 1, потом split(\"@\") на две части и проверки домена."])

task(T, 3, "find_bug", 3, "qa",
     story="Ира: «После вчерашнего релиза у всех новых пользователей пустое имя в профиле, в базе name = NULL. Шаги: регистрируюсь с name = \"  анна   петрова \", ответ 201, открываю профиль — имени нет. Телефон при этом сохраняется нормально».",
     question="Почему имя превращается в NULL и как это исправить?",
     code="""from pydantic import BaseModel, Field, field_validator


class SignupIn(BaseModel):
    name: str = Field(min_length=2, max_length=50)
    phone: str

    @field_validator("name")
    @classmethod
    def normalize_name(cls, v: str) -> str:
        v = " ".join(v.split())
        v.title()

    @field_validator("phone")
    @classmethod
    def normalize_phone(cls, v: str) -> str:
        digits = "".join(ch for ch in v if ch.isdigit())
        if len(digits) != 11:
            raise ValueError("в телефоне должно быть 11 цифр")
        return "+7" + digits[1:]
""",
     language="python",
     options=[
         "Валидатор ничего не возвращает, и Pydantic кладёт в поле None. Нужно return v.title(): сам v.title() строку не меняет",
         "Без mode=\"before\" field_validator срабатывает уже после сохранения модели, поэтому имя в базу не попадает",
         "@classmethod под @field_validator лишний: из-за него в v приходит класс, а не значение поля, и имя теряется",
         "Field(min_length=2) проверяется после валидатора и при нарушении не бросает ошибку, а обнуляет поле",
         "Поле name нужно объявить как str | None = None: без значения по умолчанию Pydantic не передаёт его в ORM",
     ],
     answer=0,
     explanation="В Pydantic v2 after-валидатор (режим по умолчанию) получает уже проверенное значение и возвращает новое, и в модель попадает именно возвращённое. Функция без return возвращает None, и Pydantic послушно кладёт None в поле name: повторной проверки типа после твоего валидатора нет, поэтому и 422 не случится. Рядом вторая мелочь: строки неизменяемы, v.title() создаёт новую строку и ничего не меняет в v. Исправление — return v.title(). @classmethod под @field_validator — рекомендованная документацией Pydantic v2 форма (класс приходит в cls, а значение поля — в v), а mode=\"before\" лишь запустил бы валидатор до проверки типа: без return результат был бы тем же None. Валидатор телефона значение возвращает, поэтому телефоны и сохраняются: сравнение двух похожих функций часто сразу показывает баг.",
     hints=["Сравни последние строки normalize_name и normalize_phone.",
            "Что возвращает функция Python, в которой нет return?"])

task(T, 4, "code_review", 4, "teamlead",
     story="Марина: «PR с редактированием карточки товара для продавцов маркетплейса. Продавец может менять название, описание, цену, скидку и публикацию. Отметь, что нужно исправить до мерджа».",
     question="Выбери все замечания, которые действительно стоит оставить в ревью.",
     code="""from pydantic import BaseModel, Field


class ProductUpdate(BaseModel):
    title: str = Field(min_length=1, max_length=200)
    description: str | None
    price: float = Field(gt=0)
    discount_percent: int
    is_published: bool = False
    seller_id: int


@router.patch("/products/{product_id}")
def update_product(
    product_id: int,
    data: ProductUpdate,
    seller: Seller = Depends(current_seller),
    db: Session = Depends(get_db),
):
    product = get_own_product(db, product_id, seller)  # 404, если товар чужой
    for field, value in data.model_dump().items():
        setattr(product, field, value)
    db.commit()
    return product
""",
     language="python",
     options=[
         "seller_id во входной модели: продавец перепишет товар на другого продавца. Служебные поля задаёт сервер",
         "У discount_percent нет границ: скидка -50% или 150% пройдёт валидацию — нужен Field(ge=0, le=90)",
         "Для PATCH все поля обязательны, а model_dump() без exclude_unset=True перезапишет всё, и is_published сбросится в False",
         "Цена во float — при расчёте скидок копейки поплывут; для денег нужен Decimal",
         "Pydantic здесь лишний: проверки через if в самой ручке понятнее и не требуют отдельной модели на эндпоинт",
         "str | None нужно заменить на Optional[str]: FastAPI не разбирает такой синтаксис объединения в схеме",
         "Проверку владения товаром нужно перенести в валидатор Pydantic-модели, а не делать в get_own_product",
     ],
     answer=[0, 1, 2, 3],
     explanation="seller_id во входной модели — классический mass assignment: цикл setattr перенесёт в ORM-объект всё, что прислал клиент, включая поле, которое должен задавать только сервер. Скидка без границ — дыра в бизнес-логике: валидация должна отсекать значения, у которых нет смысла. PATCH — частичное обновление: поля делают необязательными (= None) и применяют только присланные через model_dump(exclude_unset=True), иначе дефолт is_published=False снимет товар с публикации при правке цены. Деньги во float накапливают ошибку округления, Decimal с decimal_places=2 её убирает. А str | None и Optional[str] — одно и то же, и проверка владения правильно живёт в сервисе, у которого есть доступ к базе.",
     hints=["Какие поля продавцу вообще разрешено менять?",
            "Что произойдёт, если клиент пришлёт только {\"price\": 990}?"])


# =====================================================================
# be-api-errors
# =====================================================================
T = "be-api-errors"

topic(T, """
Ошибка — тоже часть API. Фронт, мобилка и партнёры строят логику на статус-коде и теле ошибки, поэтому и то и другое должно быть предсказуемым.

4xx — виноват клиент, повтор того же запроса не поможет. 5xx — виноват сервер, запрос можно повторить позже.
• 400 Bad Request — запрос в целом некорректен: битый JSON, неверный параметр.
• 401 Unauthorized — сервер не знает, кто ты: нет токена или он недействителен.
• 403 Forbidden — знает, но нельзя: ресурс тебе виден, а действие запрещено (поддержка открывает заказ, но отменять не может) или эндпоинт не для твоей роли.
• 404 Not Found — ресурса нет. На чужой ресурс, запрошенный по id, тоже отвечают 404, а не 403: ответ не должен подтверждать, что заказ с таким id вообще существует, иначе перебором id можно узнать, какие заказы есть.
• 409 Conflict — конфликт с текущим состоянием: email уже занят, заказ уже отправлен, товар закончился.
• 422 Unprocessable Content — формат верный, но данные не прошли валидацию. FastAPI отвечает 422 на ошибки Pydantic.
• 429 Too Many Requests — сработал лимит запросов.
• 500 Internal Server Error — необработанное исключение, то есть баг. 503 — сервис или его зависимость временно недоступны.

Единый формат. Все ошибки API выглядят одинаково, и фронт разбирает их одним кодом. Есть стандарт Problem Details (RFC 9457, тип application/problem+json): поля type, title, status, detail и свои расширения. Многие команды делают свой простой формат:

    {"error": {"code": "out_of_stock", "message": "Товара нет на складе", "details": {"sku": "LMP-12"}}}

code — машиночитаемый, по нему клиент решает, что делать; message — для человека; details — подробности, например ошибки по полям.

Как устроить в коде:
• Бизнес-логика ничего не знает про HTTP и бросает доменные исключения: NotFound, Conflict, Forbidden.
• Одно место превращает исключения в ответы. В FastAPI это обработчики исключений:

    class AppError(Exception):
        status = 500
        code = "internal_error"

    @app.exception_handler(AppError)
    async def app_error_handler(request: Request, exc: AppError):
        return JSONResponse(status_code=exc.status,
                            content={"error": {"code": exc.code, "message": str(exc)}})

• В except сначала ловят конкретные исключения, потом общие: except Exception, стоящий первым, перехватит всё.
• Неожиданное исключение → 500 с общим текстом. Трейсбек, str(exc), SQL и пути к файлам клиенту не отдают никогда: это подсказка атакующему и иногда утечка данных. Подробности пишут в лог через logger.exception(...), а в ответ кладут request_id, по которому поддержка найдёт запись.
• Не отвечай на ошибку статусом 200 и {"ok": false}: ретраи, кэши, мониторинг и алерты смотрят на статус и решат, что всё хорошо.
• Не глотай исключения молча (except: pass). Не знаешь, что делать с ошибкой, — не лови её, пусть дойдёт до общего обработчика.

HTTPException удобна прямо в роуте: raise HTTPException(status_code=404, detail="Заказ не найден"). Но в сервисном слое лучше свои исключения: их переиспользуют в фоновых задачах и тестируют без HTTP.
""")

task(T, 1, "quiz", 2, "qa",
     story="Ира: «Прохожусь по чек-листу API „Лампового Маркета“ и сверяю статус-коды. Помоги понять, где мы отвечаем правильно, а где мне заводить баги».",
     question="Отметь все пары «ситуация → код ответа», которые правильные. Выбери все верные.",
     options=[
         "Запрос к /orders без токена → 401",
         "Токен валидный, но покупатель вызывает эндпоинт только для админов → 403",
         "Регистрация с email, который уже занят → 409",
         "GET /orders/999, такого заказа нет → 400",
         "В теле запроса quantity: \"abc\" → 500",
         "База недоступна, запрос упал с исключением → 422",
     ],
     answer=[0, 1, 2],
     explanation="401 — сервер не знает, кто ты (токена нет или он недействителен), 403 — знает, но не пускает. Занятый email — конфликт с текущим состоянием данных, для него есть 409. Несуществующий заказ — 404, а не 400: запрос составлен правильно, просто ресурса нет. quantity: \"abc\" — ошибка клиента, FastAPI сам ответит 422 с описанием поля; 500 здесь означал бы, что кривой ввод уронил сервер. Недоступная база — проблема сервера: 500 или 503, но не 4xx, иначе клиент решит, что виноват сам, и не станет повторять запрос.",
     hints=["4xx — виноват клиент, 5xx — сервер. Начни с этого.",
            "401 — «кто ты?», 403 — «знаю тебя, но нельзя»."])

ERR_COMMON = """class AppError(Exception):
    status = 500
    code = "internal_error"

    def __init__(self, message, details=None):
        super().__init__(message)
        self.message = message
        self.details = details


class NotFound(AppError):
    status = 404
    code = "not_found"


class Forbidden(AppError):
    status = 403
    code = "forbidden"


class Conflict(AppError):
    status = 409
    code = "conflict"


class Invalid(AppError):
    status = 422
    code = "validation_error"


def cancel_order(orders, order_id, user, reason):
    # бизнес-логика: про HTTP ничего не знает
    if not reason or not reason.strip():
        raise Invalid("Проверь поля запроса", {"reason": "укажи причину отмены"})
    order = None
    for o in orders:
        if o["id"] == order_id:
            order = o
    # чужой заказ покупатель не видит: ответ такой же, как на несуществующий,
    # чтобы по id нельзя было узнать, есть ли такой заказ у кого-то ещё
    if order is None or (user["role"] == "customer" and order["user_id"] != user["id"]):
        raise NotFound(f"Заказ {order_id} не найден")
    # поддержка видит любой заказ, но отменить его может только сам покупатель
    if user["role"] != "customer":
        raise Forbidden("Отменить заказ может только покупатель")
    if order["status"] == "shipped":
        raise Conflict("Заказ уже отправлен, отменить нельзя")
    order["status"] = "cancelled"
    return {"id": order["id"], "status": order["status"]}


"""

ORDERS = [{"id": 7, "user_id": 1, "status": "new"}, {"id": 8, "user_id": 2, "status": "shipped"}]


def orders():
    return [dict(o) for o in ORDERS]


INTERNAL = {"status": 500, "body": {"error": {"code": "internal_error", "message": "Внутренняя ошибка сервера"}}}
ANNA = {"id": 1, "role": "customer"}
BORIS = {"id": 2, "role": "customer"}
SUPPORT = {"id": 50, "role": "support"}

task(T, 2, "write_code", 3, "teamlead",
     story="Гена: «В сервисе заказов каждый роут сам решает, что отдать при ошибке: где 400, где 500 с текстом исключения, а на чужой заказ мы отвечаем 403 — и перебором id любой узнает, какие заказы существуют. Давай как положено: сервис бросает доменные исключения, а одна функция превращает их в HTTP-ответ единого формата».",
     question="""Допиши handle_cancel(orders, order_id, user, reason): вызови cancel_order и верни словарь {"status": код, "body": тело}. user — текущий пользователь вида {"id": 1, "role": "customer"}.
• Успех → status 200, body — то, что вернул cancel_order.
• AppError и его наследники → status из exc.status, body {"error": {"code": exc.code, "message": exc.message}}; если exc.details не None — добавь в "error" ещё ключ "details".
• Любое другое исключение — это баг → status 500, body {"error": {"code": "internal_error", "message": "Внутренняя ошибка сервера"}}. Текст исключения клиенту не отдавай.
Классы исключений и cancel_order менять не нужно. Посмотри, как cancel_order разводит 404 и 403: чужой заказ для покупателя — NotFound, а Forbidden — когда заказ виден, но действие запрещено.""",
     code=ERR_COMMON + """def handle_cancel(orders, order_id, user, reason):
    # твой код
    pass
""",
     language="python", entry="handle_cancel",
     answer=ERR_COMMON + """def handle_cancel(orders, order_id, user, reason):
    try:
        result = cancel_order(orders, order_id, user, reason)
    except AppError as e:
        error = {"code": e.code, "message": e.message}
        if e.details is not None:
            error["details"] = e.details
        return {"status": e.status, "body": {"error": error}}
    except Exception:
        # в сервисе здесь logger.exception(...): подробности — в лог, не клиенту
        return {
            "status": 500,
            "body": {"error": {"code": "internal_error", "message": "Внутренняя ошибка сервера"}},
        }
    return {"status": 200, "body": result}
""",
     tests=[
         {"input": [orders(), 7, ANNA, "передумал"], "expected": {"status": 200, "body": {"id": 7, "status": "cancelled"}}},
         {"input": [orders(), 9, ANNA, "передумал"],
          "expected": {"status": 404, "body": {"error": {"code": "not_found", "message": "Заказ 9 не найден"}}}},
         {"input": [orders(), 8, ANNA, "передумал"],
          "expected": {"status": 404, "body": {"error": {"code": "not_found", "message": "Заказ 8 не найден"}}}},
         {"input": [orders(), 7, SUPPORT, "клиент попросил в чате"],
          "expected": {"status": 403, "body": {"error": {"code": "forbidden", "message": "Отменить заказ может только покупатель"}}}},
         {"input": [orders(), 8, BORIS, "поздно"],
          "expected": {"status": 409, "body": {"error": {"code": "conflict", "message": "Заказ уже отправлен, отменить нельзя"}}}},
         {"input": [orders(), 7, ANNA, "   "],
          "expected": {"status": 422, "body": {"error": {"code": "validation_error", "message": "Проверь поля запроса",
                                                         "details": {"reason": "укажи причину отмены"}}}}},
         {"input": [[{"id": 7, "user_id": 1}], 7, ANNA, "передумал"], "expected": INTERNAL},
         {"input": [orders(), 7, ANNA, 404], "expected": INTERNAL},
     ],
     explanation="Сервис бросает доменные исключения и ничего не знает про HTTP, а перевод в ответ живёт в одном месте — поэтому формат ошибок одинаковый во всём API и меняется в одной функции. Порядок except важен: сначала AppError, потом Exception, иначе общий обработчик перехватит и 404, и 409. Благодаря наследованию хватает одного except AppError: статус и код берутся из атрибутов конкретного класса. Неожиданное исключение (здесь KeyError у заказа без статуса или AttributeError у числового reason) — это баг: клиенту уходит общий текст, а подробности должны уйти в лог через logger.exception вместе с request_id. В FastAPI ровно это делают обработчики @app.exception_handler(AppError) и @app.exception_handler(Exception). Отдельно про чужой заказ: покупатель получает тот же 404 «Заказ 8 не найден», что и на несуществующий, — с 403 ответ подтвердил бы, что заказ с таким id есть, и перебором id можно было бы собрать, какие заказы существуют и сколько их. 403 уместен, когда ресурс тебе виден, но действие запрещено: поддержка открывает любой заказ, а отменить его может только покупатель.",
     hints=["Оберни вызов cancel_order в try/except.",
            "Два except: сначала AppError (у него есть status, code, message, details), потом Exception.",
            "details добавляй в словарь ошибки, только если e.details is not None."])

task(T, 3, "code_review", 3, "teamlead",
     story="Марина: «Джун добавил обработку ошибок в создание заказа и говорит, что теперь сервис „никогда не падает“. Посмотри PR внимательно и отметь то, что реально нужно исправить».",
     question="Выбери все замечания, которые действительно стоит оставить в ревью.",
     code="""import traceback

from fastapi import APIRouter, Depends, HTTPException

router = APIRouter()


@router.post("/orders", status_code=201)
def create_order(
    data: OrderIn,
    user: User = Depends(current_user),
    db: Session = Depends(get_db),
):
    try:
        order = orders_service.create(db, user.id, data)
    except OutOfStock as e:
        raise HTTPException(status_code=500, detail=f"Товара {e.sku} нет на складе")
    except Exception as e:
        return {
            "ok": False,
            "error": repr(e),
            "traceback": traceback.format_exc(),
        }
    return order
""",
     language="python",
     options=[
         "Клиенту уходят трейсбек и repr(e): пути к файлам, куски SQL, данные запроса. Наружу — общий текст и request_id",
         "Неожиданная ошибка уходит со статусом 201 и ok: false — ретраи и мониторинг 5xx посчитают заказ созданным",
         "Исключение нигде не логируется. Лучше не ловить Exception в роуте: глобальный обработчик залогирует и вернёт 500",
         "«Нет на складе» — не ошибка сервера: нужен 4xx (например 409 с кодом out_of_stock), а не 500",
         "except OutOfStock должен стоять после except Exception: сначала общий случай, потом частный, иначе он недостижим",
         "HTTPException внутри except не сработает: FastAPI обрабатывает только исключения, брошенные вне блока try",
         "status_code=201 для POST неверен: создание заказа должно отвечать 200, а 201 — только для загрузки файлов",
     ],
     answer=[0, 1, 2, 3],
     explanation="Этот код «никогда не падает» ценой того, что ошибки становятся невидимыми и опасными. Трейсбек и repr(e) раскрывают устройство сервиса — пути, SQL, иногда персональные данные из запроса; клиенту положен общий текст, а детали — в лог. Словарь, возвращённый из except, уходит со статусом роута 201: для ретраев, кэшей и алертов это успешно созданный заказ, которого нет. Проглоченное исключение без лога не увидит никто, кроме клиента, поэтому неизвестные ошибки в роуте лучше не ловить вовсе. Нехватка товара — ожидаемая бизнес-ситуация, то есть 4xx (обычно 409), иначе алерт по 5xx будет будить дежурного из-за обычных покупателей. Порядок except здесь как раз правильный: частный случай первым, а HTTPException, брошенная из except, спокойно долетает до FastAPI.",
     hints=["Представь, что клиент — злоумышленник. Что он узнает из ответа?",
            "Какой статус получит клиент, если сработает except Exception?",
            "Кто виноват, когда товара нет на складе: сервер или ситуация?"])

task(T, 4, "architecture", 4, "manager",
     story="Стас: «Мобильщики жалуются: у нас ошибки в пяти разных форматах, где-то вообще голый текст, а где-то 200 с ok: false. Им надо показывать ошибку под полем формы и по-разному реагировать на „нет на складе“ и „промокод истёк“. Давай раз и навсегда договоримся, как отдаём ошибки».",
     question="Какой подход к ошибкам выбрать для всего API?",
     code="""POST /cart/items     422  {"detail": [{"loc": ["body", "quantity"], "msg": "Input should be greater than 0"}]}
POST /orders         200  {"ok": false, "error": "Товара нет на складе"}
POST /promo/apply    400  Промокод истёк
GET  /orders/123     404  {"detail": "Not Found"}
POST /auth/login     500  {"message": "Invalid credentials", "trace": "Traceback (most recent call last): ..."}
""",
     language="text",
     options=[
         "Единый формат: верный HTTP-статус и тело Problem Details (RFC 9457) с машиночитаемым code и ошибками по полям, из одного обработчика",
         "Всегда отвечать 200 и {\"ok\": false, \"message\": \"…\"}: мобилке не придётся разбирать статус-коды, проверка одна — по полю ok в теле ответа",
         "Оставить форматы как есть, но подробно описать каждый в README и OpenAPI, чтобы мобильщики знали, что где ждать",
         "Отдавать понятный текст на русском, а клиенты пусть ищут в нём ключевые слова вроде «склад» или «промокод»",
         "Передавать код ошибки в заголовке X-Error-Code, а тело оставлять пустым — формат тела не придётся согласовывать",
     ],
     answer=0,
     explanation="Клиенту нужны две вещи: статус, чтобы понять класс проблемы и решить, повторять ли запрос, и машиночитаемый code, чтобы по-разному реагировать на out_of_stock и promo_expired, не разбирая человеческий текст. Problem Details (RFC 9457, тип application/problem+json) — готовый стандарт с полями type, title, status, detail и расширениями вроде errors по полям, его понимают многие клиентские библиотеки. Один глобальный обработчик гарантирует, что формат не разъедется, а новые ручки получат его бесплатно. «Всегда 200» ломает ретраи, кэши и мониторинг ошибок; README только документирует зоопарк, разбор текста развалится при первой правке формулировки, а пустое тело не скажет, какие поля неверны. Цена решения — разовая миграция: старые ответы придётся поменять, а мобилке — обновить разбор ошибок, поэтому переход согласуют и выкатывают вместе.",
     hints=["Что клиенту нужно из ответа, чтобы решить, что делать: повторить запрос, подсветить поле или показать сообщение?",
            "Есть ли стандарт формата ошибок, чтобы не изобретать свой?"])


# =====================================================================
# be-auth
# =====================================================================
T = "be-auth"

topic(T, """
Аутентификация отвечает на вопрос «кто ты», авторизация — «что тебе можно». Провал первой — 401, второй — 403.

Пароли никогда не хранят в открытом виде и не шифруют: зашифрованное можно расшифровать. Хранят хеш медленной функцией с солью:
• Argon2id — текущая рекомендация OWASP; bcrypt — проверенный вариант, но работает только с первыми 72 байтами пароля.
• sha256 и md5 для паролей не годятся даже с солью: они быстрые, и видеокарта перебирает миллиарды вариантов в секунду.
• Соль — случайная строка для каждого пользователя. Она защищает от радужных таблиц и одинаковых хешей у одинаковых паролей. Argon2 и bcrypt генерируют её сами и хранят внутри хеша.
• В Python: argon2-cffi или pwdlib, библиотека bcrypt. Проверяют через verify — сравнение за постоянное время.

    from argon2 import PasswordHasher
    ph = PasswordHasher()
    stored = ph.hash(password)      # в базу
    ph.verify(stored, password)     # True или VerifyMismatchError

Логин отвечает одинаково на «нет пользователя» и «неверный пароль» («Неверный email или пароль»), иначе перебором можно узнать, кто зарегистрирован. Плюс лимит попыток.

Сессии: после логина сервер создаёт запись сессии (в базе или Redis) и отдаёт её случайный id в куке, выход — удаление записи. Флаги куки: HttpOnly (JS не прочитает её при XSS), Secure (только HTTPS), SameSite=Lax или Strict (защита от CSRF).

JWT — подписанный токен header.payload.signature в base64url. Он не зашифрован: payload прочитает кто угодно, секретов туда не кладут. Стандартные claims: sub — id пользователя (строка), exp — когда истекает, iat — когда выдан, nbf, iss, aud. Время — секунды Unix-эпохи (UTC).

Проверка токена:
• подпись с явным списком алгоритмов: jwt.decode(token, key, algorithms=["HS256"]) — иначе возможны атаки с alg=none и подменой алгоритма;
• exp: токен недействителен, когда now >= exp; небольшой leeway (30–60 секунд) прощает расхождение часов серверов;
• iat из будущего подозрителен — такой токен отклоняют;
• тип: refresh-токен нельзя использовать вместо access.

Access-токен живёт 5–15 минут и не сверяется с базой, поэтому до истечения его не отозвать. Refresh-токен живёт дни или недели, хранится на сервере (лучше его хеш) и нужен только для получения нового access. При обновлении refresh меняют (rotation), при выходе — удаляют. «Выйти со всех устройств» — удалить все refresh-токены пользователя.

Токен передают в заголовке Authorization: Bearer <token> или в httpOnly-куке. Никогда в URL: адрес оседает в истории браузера, логах nginx и заголовке Referer.

Роли (RBAC): у пользователя роль, у роли — набор разрешений. Права проверяют на сервере в каждом эндпоинте (в FastAPI — зависимостью) по принципу «запрещено всё, что не разрешено явно»: неизвестная роль не получает ничего. И не забывай про владение: покупатель отменяет свой заказ, но не чужой. На чужой объект, запрошенный по id, обычно отвечают 404, а не 403 — ответ не должен подтверждать, что объект существует; 403 оставляют для случаев, когда объект виден, но действие запрещено.
""")

task(T, 1, "quiz", 2, "teamlead",
     story="Гена: «В старом КофеБоте пароли лежат как sha256(пароль). Переписываем регистрацию — давай сразу решим, как хранить пароли правильно, чтобы потом не краснеть».",
     question="Как правильно хранить пароли пользователей?",
     options=[
         "Медленная функция для паролей — Argon2id или bcrypt: соль внутри, стоимость вычисления настраивается",
         "sha256(пароль): хеш необратим, по нему пароль не восстановить, так что утечка базы не страшна",
         "sha256(соль + пароль) с уникальной случайной солью для каждого пользователя, соль — в соседней колонке",
         "Шифровать пароль AES-256, ключ хранить в переменных окружения — так можно напомнить пароль пользователю",
         "Хранить как есть, но закрыть базу от интернета и выдать к ней доступ только двум администраторам",
     ],
     answer=0,
     explanation="sha256 и md5 созданы быть быстрыми: видеокарта считает миллиарды таких хешей в секунду, и после утечки базы короткие пароли подбираются за минуты. Соль спасает от радужных таблиц и одинаковых хешей у одинаковых паролей, но не от скорости перебора. Argon2id и bcrypt специально медленные (Argon2 ещё и требует много памяти), а их стоимость можно поднимать вместе с ростом мощности железа; соль они генерируют и хранят сами. Шифрование обратимо — утёк ключ, утекли все пароли, а «напомнить пароль» не нужно: нужен сброс. Открытые пароли не спасёт и закрытая база — они утекают через бэкапы, логи и SQL-инъекции.",
     hints=["Представь: базу украли, и атакующий перебирает пароли на видеокарте.",
            "Главное свойство функции для паролей — не необратимость, а медленность."])

PERM_BUG = """PERMISSIONS = {
    "admin": {"view", "edit", "cancel", "refund"},
    "manager": {"view", "edit", "cancel"},
    "customer": {"view", "cancel"},
}


def can(user, action, order):
    if user["role"] == "admin" or "manager":
        return action in PERMISSIONS[user["role"]]
    # остальные работают только со своими заказами
    if order["user_id"] != user["id"]:
        return False
    if action == "cancel" and order["status"] != "new":
        return False
    return action in PERMISSIONS[user["role"]]
"""

PERM_FIX = """PERMISSIONS = {
    "admin": {"view", "edit", "cancel", "refund"},
    "manager": {"view", "edit", "cancel"},
    "customer": {"view", "cancel"},
}


def can(user, action, order):
    role = user["role"]
    allowed = PERMISSIONS.get(role, set())  # неизвестная роль — никаких прав
    if role in ("admin", "manager"):
        return action in allowed
    # остальные работают только со своими заказами
    if order["user_id"] != user["id"]:
        return False
    if action == "cancel" and order["status"] != "new":
        return False
    return action in allowed
"""

task(T, 2, "find_bug", 3, "qa",
     story="Ира: «Баг-репорт QA-812: захожу обычным покупателем, открываю /orders/15 — это заказ другого человека, и я его вижу и даже могу отменить. А под тестовой ролью guest вообще 500».",
     question="can(user, action, order) решает, можно ли пользователю действие с заказом. admin и manager могут всё из своего набора прав. Остальные — только со своими заказами и только то, что разрешено их роли; отменить можно лишь заказ в статусе new. Неизвестная роль не может ничего. Найди и исправь ошибки.",
     code=PERM_BUG, language="python", entry="can",
     answer=PERM_FIX,
     tests=[
         {"input": [{"id": 1, "role": "admin"}, "refund", {"user_id": 5, "status": "paid"}], "expected": True},
         {"input": [{"id": 2, "role": "manager"}, "refund", {"user_id": 5, "status": "paid"}], "expected": False},
         {"input": [{"id": 3, "role": "manager"}, "edit", {"user_id": 5, "status": "new"}], "expected": True},
         {"input": [{"id": 5, "role": "customer"}, "view", {"user_id": 5, "status": "paid"}], "expected": True},
         {"input": [{"id": 7, "role": "customer"}, "view", {"user_id": 5, "status": "paid"}], "expected": False},
         {"input": [{"id": 5, "role": "customer"}, "cancel", {"user_id": 5, "status": "shipped"}], "expected": False},
         {"input": [{"id": 5, "role": "customer"}, "cancel", {"user_id": 5, "status": "new"}], "expected": True},
         {"input": [{"id": 9, "role": "guest"}, "view", {"user_id": 9, "status": "new"}], "expected": False},
     ],
     explanation="Условие user[\"role\"] == \"admin\" or \"manager\" Python читает как (user[\"role\"] == \"admin\") or \"manager\", а непустая строка всегда истинна: в первую ветку попадает любой пользователь, и проверки владения и статуса не выполняются никогда. Правильно — role in (\"admin\", \"manager\"). Вторая ошибка — PERMISSIONS[role] для неизвестной роли падает с KeyError, и клиент получает 500. По принципу «запрещено всё, что не разрешено явно» используют PERMISSIONS.get(role, set()): незнакомая роль получает пустой набор прав. Такие баги в авторизации коварны: тесты «админ может» проходят, а тесты «покупатель не может» часто никто не пишет.",
     hints=["Как Python вычисляет выражение x == \"admin\" or \"manager\"?",
            "Что вернёт PERMISSIONS[user[\"role\"]] для роли guest?",
            "role in (\"admin\", \"manager\") и PERMISSIONS.get(role, set())"])

CLAIMS_OK = {"sub": "42", "iat": 1790000000, "exp": 1790000900, "type": "access", "role": "customer"}


def claims(**kw):
    c = dict(CLAIMS_OK)
    for k, v in kw.items():
        if v is None:
            c.pop(k, None)
        else:
            c[k] = v
    return c


OK42 = {"ok": True, "user_id": "42"}
INVALID = {"ok": False, "error": "invalid_token"}

task(T, 3, "write_code", 3, "devops_colleague",
     story="Дима: «Разбирал вчерашний инцидент и нашёл зоопарк: подпись токена везде проверяет библиотека, а дальше самодеятельность — где-то токен живёт вечно, где-то refresh пускают вместо access. Напиши одну функцию проверки claims — раскатаем её на все сервисы».",
     question="""Напиши check_claims(claims, now): claims — словарь из JWT, подпись которого уже проверена; now — текущее время в секундах Unix. Проверяй по порядку и возвращай первую сработавшую ошибку:
1. sub — непустая строка, exp и iat — числа (int или float, но не bool). Иначе {"ok": False, "error": "invalid_token"}.
2. type не равен "access" (в том числе поля нет) → {"ok": False, "error": "wrong_token_type"}.
3. iat > now + LEEWAY (выдан «в будущем») → {"ok": False, "error": "invalid_token"}.
4. now >= exp + LEEWAY → {"ok": False, "error": "token_expired"}.
Если всё в порядке — {"ok": True, "user_id": sub}. LEEWAY = 30 секунд — запас на расхождение часов.""",
     code="""LEEWAY = 30  # секунд


def check_claims(claims, now):
    # твой код
    pass
""",
     language="python", entry="check_claims",
     answer="""LEEWAY = 30  # секунд


def is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def check_claims(claims, now):
    sub = claims.get("sub")
    if not isinstance(sub, str) or not sub:
        return {"ok": False, "error": "invalid_token"}
    if not is_number(claims.get("exp")) or not is_number(claims.get("iat")):
        return {"ok": False, "error": "invalid_token"}
    if claims.get("type") != "access":
        return {"ok": False, "error": "wrong_token_type"}
    if claims["iat"] > now + LEEWAY:
        return {"ok": False, "error": "invalid_token"}
    if now >= claims["exp"] + LEEWAY:
        return {"ok": False, "error": "token_expired"}
    return {"ok": True, "user_id": sub}
""",
     tests=[
         {"input": [claims(), 1790000500], "expected": OK42},
         {"input": [claims(), 1790000929], "expected": OK42},
         {"input": [claims(), 1790000930], "expected": {"ok": False, "error": "token_expired"}},
         {"input": [claims(type="refresh", exp=1792592000), 1790000500], "expected": {"ok": False, "error": "wrong_token_type"}},
         {"input": [claims(type=None), 1790000500], "expected": {"ok": False, "error": "wrong_token_type"}},
         {"input": [claims(exp="1790000900"), 1790000500], "expected": INVALID},
         {"input": [claims(exp=True), 1790000500], "expected": INVALID},
         {"input": [claims(sub=42), 1790000500], "expected": INVALID},
         {"input": [claims(sub=""), 1790000500], "expected": INVALID},
         {"input": [claims(iat=None), 1790000500], "expected": INVALID},
         {"input": [claims(iat=1790000200, exp=1790001100), 1790000100], "expected": INVALID},
         {"input": [claims(sub="7", iat=1790000000.5, exp=1790000900.5), 1790000930], "expected": {"ok": True, "user_id": "7"}},
     ],
     explanation="Проверки идут от структуры к смыслу: сначала убеждаемся, что поля есть и нужного типа, иначе сравнение строки с числом упадёт с TypeError и вместо 401 получится 500. bool отсекают отдельно, потому что в Python True — это int. Refresh-токен отклоняем, даже если он жив: он долгоживущий и нужен только эндпоинту обновления, и если его примет обычный API, кража refresh станет кражей доступа на недели. Граница exp включительная, как в PyJWT: токен недействителен, когда now >= exp + leeway, а leeway прощает рассинхрон часов. В продакшене базовые проверки делает jwt.decode(token, key, algorithms=[\"HS256\"], leeway=30, options={\"require\": [\"exp\", \"iat\", \"sub\"]}), но свои правила поверх (тип токена, роли) всё равно пишешь сам.",
     hints=["Начни с типов: isinstance(x, (int, float)) and not isinstance(x, bool).",
            "claims.get(\"type\") вернёт None, если поля нет, — и это тоже не \"access\".",
            "Истёк, если now >= claims[\"exp\"] + LEEWAY."])

task(T, 4, "code_review", 3, "teamlead",
     story="Марина: «Джун переписал логин личного кабинета „Лампового Маркета“: после входа фронт забирает токен из адреса страницы. Работает, но я бы это в прод не пустила. Отметь настоящие проблемы».",
     question="Выбери все замечания, которые действительно стоит оставить в ревью.",
     code="""@router.post("/auth/login")
def login(form: LoginIn, db: Session = Depends(get_db)):
    user = db.scalar(select(User).where(User.email == form.email))
    if user is None:
        raise HTTPException(status_code=404, detail="Пользователь с таким email не найден")
    if user.password != form.password:
        raise HTTPException(status_code=401, detail="Неверный пароль")

    now = datetime.now(timezone.utc)
    token = jwt.encode(
        {"sub": str(user.id), "type": "access", "iat": now, "exp": now + timedelta(minutes=15)},
        settings.jwt_secret,
        algorithm="HS256",
    )
    return RedirectResponse(f"{settings.frontend_url}/cabinet?token={token}", status_code=303)
""",
     language="python",
     options=[
         "Пароль хранится и сравнивается в открытом виде — нужен хеш Argon2id или bcrypt и проверка через verify",
         "Разные ответы на «нет такого email» и «неверный пароль» выдают, кто зарегистрирован, — нужен один 401",
         "Токен в URL оседает в истории браузера, логах nginx и Referer — отдавай его в теле ответа или httpOnly-куке",
         "HS256 небезопасен: симметричный ключ легко подобрать, для любого сервиса допустим только RS256",
         "exp нужно считать в московском времени, иначе у пользователей в Москве токены будут истекать на 3 часа раньше",
         "15 минут для access-токена мало — выдавай его на 30 дней, чтобы пользователь не перелогинивался",
         "where(User.email == …) нужно заменить на filter_by(email=…) — только так запрос защищён от SQL-инъекций",
     ],
     answer=[0, 1, 2],
     explanation="Открытые пароли — худшее, что может утечь из базы: люди используют один пароль везде. Храни хеш Argon2id или bcrypt и проверяй через verify — он ещё и сравнивает за постоянное время. Раздельные 404 и 401 превращают форму логина в справочник «есть ли у вас такой клиент»; ответ должен быть одинаковым, а в идеале и время ответа тоже (когда пользователя нет, прогоняют verify по фиктивному хешу). Токен в URL живёт в истории, логах и Referer, откуда его легко утащить. HS256 с длинным случайным секретом нормален, когда токены проверяет тот же сервис (RS256 нужен, если проверять будут сторонние сервисы без доступа к секрету). Время в JWT — всегда секунды UTC, долгий access-токен нельзя отозвать, а where и filter_by одинаково передают значение bind-параметром.",
     hints=["Что увидит атакующий, если украдёт базу? А если переберёт email через форму?",
            "Где оседает URL после редиректа?"])

task(T, 5, "estimation", 4, "manager", time_limit=15,
     story="Стас: «Для админки „Лампового Маркета“ нужны роли: админ, менеджер склада и поддержка, у каждого свои права. И кнопка „выйти со всех устройств“, чтобы уволенного сотрудника выкидывало сразу. Сейчас все сотрудники входят по JWT, и роль у всех одна — admin. Сколько дней? Мне к планёрке».",
     question="Прежде чем назвать срок, выбери вопросы, которые обязательно нужно выяснить.",
     options=[
         "Что конкретно может каждая роль — есть ли готовая матрица «роль → действия» или составляем её вместе?",
         "Выход со всех устройств нужен мгновенно или допустимо, что access-токен доживёт свои 15 минут?",
         "Кто и где будет назначать роли и нужен ли журнал, кто кому какие права выдал?",
         "Какую роль получит каждый из текущих сотрудников при переезде на новую схему?",
         "Какого цвета и формы будут бейджи ролей в шапке админки — нужен ли макет от дизайнера?",
         "Можно ли обойтись без автотестов на права, чтобы успеть к пятнице, а проверить всё руками?",
         "Может, заодно перепишем админку на другой фреймворк, раз всё равно трогаем авторизацию?",
     ],
     answer=[0, 1, 2, 3],
     explanation="Объём работы тут определяют права, отзыв токенов и миграция. Без матрицы «роль → действия» непонятно, сколько эндпоинтов трогать, а проверка роли нужна в каждом. Выход со всех устройств при коротком access-токене — это хранение refresh-токенов в базе и их отзыв; если нужно «прямо сейчас», добавится проверка версии токена или денилиста на каждый запрос, а это заметно дороже. Экран назначения ролей и журнал изменений — отдельная задача, о которой часто забывают при оценке, а миграция действующих сотрудников — риск запереть кого-то вне админки. Разумная оценка: роли и проверки 2–3 дня, отзыв refresh-токенов 1–2 дня, экран ролей с журналом 2 дня, миграция и тесты 1–2 дня — около 1,5–2 недель, если матрица прав уже есть.",
     hints=["Какие ответы изменят объём работы в разы?",
            "Как отозвать JWT, который по определению не сверяется с базой?"])


# =====================================================================
# be-docker-local
# =====================================================================
T = "be-docker-local"

topic(T, """
Docker нужен разработчику, чтобы проект запускался одинаково у всех: одна команда поднимает API и PostgreSQL нужных версий, без «а у меня работает».

Dockerfile для Python-сервиса:

    FROM python:3.12-slim
    ENV PYTHONUNBUFFERED=1
    WORKDIR /app
    COPY requirements.txt .
    RUN pip install --no-cache-dir -r requirements.txt
    COPY . .
    RUN useradd --create-home app
    USER app
    CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]

• slim-образ с явной версией, а не latest.
• Сначала зависимости, потом код: слой с pip install пересобирается, только когда меняется requirements.txt.
• PYTHONUNBUFFERED=1 — логи сразу видны в docker logs, а не копятся в буфере.
• --host 0.0.0.0: внутри контейнера сервер должен слушать все интерфейсы, на 127.0.0.1 его не достать даже через проброшенный порт.
• CMD в exec-форме (JSON-массив), чтобы приложение получило SIGTERM и корректно завершилось. --reload — только для разработки.
• .dockerignore: .git, .venv, __pycache__, .env — мусор и секреты не должны попасть в образ.

docker compose описывает сервисы в compose.yaml (или docker-compose.yml); ключ version устарел, его не пишут.

    services:
      api:
        build: .
        ports: ["8000:8000"]
        env_file: .env
        depends_on:
          db:
            condition: service_healthy
      db:
        image: postgres:16
        environment:
          POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
        volumes: ["pgdata:/var/lib/postgresql/data"]
        healthcheck:
          test: ["CMD-SHELL", "pg_isready -U postgres"]
          interval: 5s
    volumes:
      pgdata:

Сеть. Compose создаёт общую сеть, и сервисы находят друг друга по имени: из api база доступна как db:5432. localhost внутри контейнера — это сам контейнер. ports: "5432:5432" пробрасывает порт на твой компьютер (для psql или DBeaver с хоста), контейнерам между собой он не нужен.

depends_on без условия ждёт только запуска контейнера, а не готовности базы. С healthcheck и condition: service_healthy api стартует, когда PostgreSQL реально принимает соединения. Уметь переподключаться приложению всё равно полезно.

Переменные: environment — прямо в файле, env_file — из файла. ${VAR} compose подставляет из окружения или из .env рядом с compose.yaml. .env не коммитят, в репозиторий кладут .env.example. Образ postgres при первом запуске требует POSTGRES_PASSWORD, без него контейнер сразу завершится.

Тома. Данные контейнера исчезают вместе с ним. Именованный том (pgdata) переживает docker compose down, а down -v удаляет и тома — база будет пустой. Bind mount (./app:/app/app) пробрасывает код с диска — удобно для разработки с --reload. С postgres:18 путь поменялся: PGDATA теперь /var/lib/postgresql/18/docker, и том монтируют в /var/lib/postgresql. Со старым путём …/data образ 18 молча пишет данные мимо именованного тома — в анонимный, и после пересоздания контейнера база пустая.

Команды: docker compose up -d --build, ps, logs -f api, exec db psql -U market, down.
""")

task(T, 1, "find_bug", 2, "qa",
     story="Ира: «Хочу поднять Маркет локально для тестов, делаю всё по README: docker compose up — и база сразу умирает, а api за ней. Вот compose.yaml и лог. Что не так?»",
     question="""После docker compose up в логе:

db-1  | Error: Database is uninitialized and superuser password is not specified.
db-1  |        You must specify POSTGRES_PASSWORD to a non-empty value for the
db-1  |        superuser. For example, "-e POSTGRES_PASSWORD=password" on "docker run".
db-1 exited with code 1

В чём причина и как исправить?""",
     code="""services:
  api:
    build: .
    ports:
      - "8000:8000"
    environment:
      DATABASE_URL: postgresql+psycopg://market:market@db:5432/market
    depends_on:
      - db

  db:
    image: postgres:16
    environment:
      POSTGRES_USER: market
      POSTGRES_DB: market
    ports:
      - "5432:5432"
""",
     language="yaml",
     options=[
         "Образу postgres при первом запуске нужен пароль суперпользователя: добавить POSTGRES_PASSWORD, как в DATABASE_URL (лучше из .env)",
         "depends_on стоит не в том сервисе: api стартует первым и не даёт базе проинициализироваться — перенести его в db",
         "Порт 5432 на хосте занят локальным PostgreSQL — поменять проброс на \"5433:5432\", и база стартует",
         "В начале файла не хватает version: \"3.8\" — без неё compose читает environment по старой схеме и теряет переменные",
         "POSTGRES_USER может быть только postgres: образ не создаёт других суперпользователей — поменять имя в обоих местах",
     ],
     answer=0,
     explanation="Официальный образ postgres при первом запуске инициализирует кластер и создаёт суперпользователя, а без пароля отказывается это делать — о чём прямо пишет в логе. Нужно добавить POSTGRES_PASSWORD, совпадающий с паролем в DATABASE_URL; значение лучше не хардкодить, а подставлять через ${POSTGRES_PASSWORD} из .env, который не коммитится. POSTGRES_USER может быть любым — образ создаст суперпользователя с таким именем. Занятый порт дал бы ошибку «port is already allocated» ещё до старта контейнера, а ключ version устарел, и compose его просто игнорирует. Начинай с лога: здесь он буквально называет переменную, которой не хватает.",
     hints=["Прочитай лог целиком — он называет переменную.",
            "Сравни логин и пароль в DATABASE_URL с тем, что получает сервис db."])

DOCKER_ANSWER = r"""FROM python:3.12-slim

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

RUN useradd --create-home --uid 10001 app
USER app

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
"""

task(T, 2, "write_code", 3, "devops_colleague",
     story="Дима, DevOps: «Смотрю Dockerfile API „Лампового Маркета“: образ на гигабайт, каждый коммит ставит зависимости заново, логи в docker logs приходят пачками раз в минуту, а снаружи контейнера API вообще не отвечает. Перепиши по-человечески».",
     question="""Перепиши Dockerfile:
• базовый образ python:3.12-slim;
• переменная окружения PYTHONUNBUFFERED=1;
• сначала скопируй requirements.txt и поставь зависимости (pip без кэша), потом скопируй остальной код;
• процесс запускается не от root;
• EXPOSE 8000;
• CMD в exec-форме: uvicorn app.main:app на хосте 0.0.0.0 и порту 8000, без --reload.""",
     code="""FROM python:3.12
COPY . /app
WORKDIR /app
RUN pip install -r requirements.txt
CMD uvicorn app.main:app --host 127.0.0.1 --port 8000 --reload
""",
     language="dockerfile",
     answer=DOCKER_ANSWER,
     tests=[
         {"input": "Базовый образ python:3.12-slim",
          "expected": {"regex": r"^FROM\s+python:3\.12-slim\b", "flags": "im"}},
         {"input": "Задан PYTHONUNBUFFERED=1",
          "expected": {"regex": r"PYTHONUNBUFFERED[=\s]+['\"]?1\b", "flags": "i"}},
         {"input": "requirements.txt копируется и ставится до остального кода",
          "expected": {"regex": r"COPY\s+requirements\.txt[^\n]*\n(?:[^\n]*\n)*?RUN\s[^\n]*pip\s+install[^\n]*\n(?:[^\n]*\n)*?COPY\s", "flags": "i"}},
         {"input": "Весь код не копируется до установки зависимостей",
          "expected": {"not_regex": r"^COPY\s+\.\s+\S+[^\n]*\n(?:[^\n]*\n)*?RUN\s[^\n]*pip\s+install", "flags": "im"}},
         {"input": "pip ставит пакеты без кэша",
          "expected": {"regex": r"pip\s+install\s[^\n]*--no-cache-dir|PIP_NO_CACHE_DIR", "flags": "i"}},
         {"input": "Процесс запускается не от root",
          "expected": {"regex": r"^USER\s+(?!root\b)\S+", "flags": "im"}},
         {"input": "EXPOSE 8000",
          "expected": {"regex": r"^EXPOSE\s+8000\b", "flags": "im"}},
         {"input": "CMD в exec-форме, uvicorn слушает 0.0.0.0",
          "expected": {"regex": r"^CMD\s*\[[^\]\n]*\"uvicorn\"[^\]\n]*(?:\"--host\"\s*,\s*\"|\"--host=)0\.0\.0\.0\"", "flags": "im"}},
         {"input": "Нет --reload",
          "expected": {"not_regex": r"--reload", "flags": "i"}},
     ],
     explanation="Порядок слоёв решает скорость сборки: пока requirements.txt не менялся, Docker берёт слой с pip install из кэша, и правка кода пересобирает только последний COPY. slim-образ в разы меньше полного, а --no-cache-dir не оставляет в нём кэш pip. Без PYTHONUNBUFFERED Python буферизует stdout, когда вывод идёт не в терминал, поэтому логи и приходят пачками. --host 127.0.0.1 — причина, почему API «не отвечает»: внутри контейнера это его собственный loopback, а проброшенный порт приходит на внешний интерфейс контейнера, так что нужен 0.0.0.0. В shell-форме CMD процессом PID 1 становится /bin/sh, и SIGTERM от docker stop может не дойти до uvicorn; exec-форма запускает его напрямую. --reload — режим разработки, в образе он только тратит CPU на слежку за файлами.",
     hints=["Сначала COPY requirements.txt и RUN pip install, потом COPY . .",
            "Внутри контейнера 127.0.0.1 — это сам контейнер. Какой адрес означает «все интерфейсы»?",
            "CMD [\"uvicorn\", \"app.main:app\", \"--host\", \"0.0.0.0\", \"--port\", \"8000\"]"])

task(T, 3, "incident", 3, "devops_colleague", time_limit=15,
     story="Дима: «Демо клиенту через 20 минут, а api в compose перезапускается по кругу. Разработчик клянётся, что у него на ноуте всё работает: он запускает uvicorn прямо на машине, а базу — в докере. Вот что я вижу».",
     question="Почему api не может подключиться к базе и что исправить?",
     code="""$ docker compose ps
NAME           IMAGE         SERVICE   STATUS                          PORTS
market-api-1   market-api    api       Restarting (3) 4 seconds ago
market-db-1    postgres:16   db        Up 2 minutes (healthy)          0.0.0.0:5432->5432/tcp

$ docker compose logs api --tail 5
api-1  | INFO:     Started server process [1]
api-1  | INFO:     Waiting for application startup.
api-1  | sqlalchemy.exc.OperationalError: (psycopg.OperationalError) connection failed: connection to server at "127.0.0.1", port 5432 failed: Connection refused
api-1  |     Is the server running on that host and accepting TCP/IP connections?
api-1  | ERROR:    Application startup failed. Exiting.

$ docker compose exec db psql -U market -c 'select 1'
 ?column?
----------
        1
(1 row)

$ grep DATABASE_URL .env
DATABASE_URL=postgresql+psycopg://market:market@localhost:5432/market
""",
     language="text",
     options=[
         "Внутри контейнера api localhost — это сам api. В DATABASE_URL нужен хост db, имя сервиса в compose: …@db:5432/market",
         "База не успевает стартовать к моменту подключения api — добавить sleep 30 в команду запуска api или ретраи в startup",
         "У db порт проброшен только наружу, а в сеть compose — нет: добавить в сервис db expose: \"5432\"",
         "Неверный пароль в DATABASE_URL: удалить том базы и пересоздать её с паролем из .env",
         "Прописать api network_mode: host, чтобы localhost внутри контейнера указывал на машину, как на ноуте",
         "PostgreSQL в контейнере слушает только 127.0.0.1 — поправить listen_addresses в postgresql.conf",
     ],
     answer=0,
     explanation="Ключ в логе: соединение с 127.0.0.1:5432 отклонено, хотя db в статусе healthy и отвечает на psql. У каждого контейнера свой сетевой стек, и localhost внутри api — это сам api, где никакой PostgreSQL не слушает. Compose кладёт сервисы в общую сеть с DNS по именам, поэтому из api база доступна как db:5432 — и expose для этого не нужен, внутри сети compose порты сервисов и так доступны друг другу. На ноуте всё работало, потому что uvicorn запускался на хосте, а ports: 5432:5432 пробрасывал порт базы на localhost машины — отсюда и .env с localhost; для compose DATABASE_URL лучше задать прямо в environment сервиса api. sleep бесполезен — база готова уже две минуты, неверный пароль выглядел бы как «password authentication failed», а network_mode: host ломает изоляцию и по-разному работает на Linux и в Docker Desktop.",
     hints=["Посмотри на адрес в ошибке и на статус db.",
            "Что такое localhost для процесса внутри контейнера?",
            "Сервисы compose видят друг друга по именам сервисов."])

COMPOSE_START = """services:
  api:
    build: .
    ports:
      - "8000:8000"
    environment:
      DATABASE_URL: postgresql+psycopg://market:market@localhost:5432/market
    depends_on:
      - db

  db:
    image: postgres
    environment:
      POSTGRES_USER: market
      POSTGRES_PASSWORD: market
      POSTGRES_DB: market
    ports:
      - "5432:5432"
"""

COMPOSE_ANSWER = """services:
  api:
    build: .
    ports:
      - "8000:8000"
    environment:
      DATABASE_URL: postgresql+psycopg://market:${POSTGRES_PASSWORD}@db:5432/market
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:16
    environment:
      POSTGRES_USER: market
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_DB: market
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U market -d market"]
      interval: 5s
      timeout: 3s
      retries: 10

volumes:
  pgdata:
"""

task(T, 4, "write_code", 4, "teamlead",
     story="Гена: «Давай доведём compose.yaml Маркета до ума, чтобы новичок поднимал проект одной командой: база не теряет данные после down, api не стартует раньше базы, пароли не лежат в git».",
     question="""Перепиши compose.yaml:
• db — образ postgres:16, а не безымянный latest;
• пароль не хардкодь: POSTGRES_PASSWORD: ${POSTGRES_PASSWORD} (значение подтянется из .env рядом с compose.yaml), и в DATABASE_URL тоже подставь ${POSTGRES_PASSWORD};
• DATABASE_URL ведёт на сервис db, а не на localhost;
• данные базы — в именованном томе pgdata, смонтированном в /var/lib/postgresql/data; том объявлен в корневом ключе volumes;
• у db есть healthcheck через pg_isready;
• api ждёт, пока db станет healthy (condition: service_healthy).""",
     code=COMPOSE_START, language="yaml",
     answer=COMPOSE_ANSWER,
     tests=[
         {"input": "db на образе postgres:16",
          "expected": {"regex": r"image:\s*['\"]?postgres:16\b", "flags": "i"}},
         {"input": "Пароль не захардкожен",
          "expected": {"not_regex": r"POSTGRES_PASSWORD\s*[:=]\s*['\"]?market\b", "flags": "i"}},
         {"input": "POSTGRES_PASSWORD берётся из ${POSTGRES_PASSWORD}",
          "expected": {"regex": r"POSTGRES_PASSWORD\s*[:=]\s*['\"]?\$\{POSTGRES_PASSWORD[}:?-]", "flags": "i"}},
         {"input": "DATABASE_URL ведёт на db:5432",
          "expected": {"regex": r"DATABASE_URL\s*[:=]\s*['\"]?postgres[^\n]*?@db:5432/", "flags": "i"}},
         {"input": "Пароль в DATABASE_URL тоже из переменной",
          "expected": {"regex": r"DATABASE_URL\s*[:=]\s*['\"]?postgres[^\n]*?:\$\{POSTGRES_PASSWORD[^}\n]*\}@", "flags": "i"}},
         {"input": "Именованный том pgdata смонтирован в /var/lib/postgresql/data",
          "expected": {"regex": r"-\s*['\"]?pgdata:/var/lib/postgresql/data", "flags": "i"}},
         {"input": "Том pgdata объявлен в корневом volumes",
          "expected": {"regex": r"^volumes:[ \t]*\n[ \t]+pgdata:", "flags": "m"}},
         {"input": "У db есть healthcheck с pg_isready",
          "expected": {"regex": r"healthcheck:[\s\S]*?pg_isready", "flags": "i"}},
         {"input": "api ждёт healthy базу",
          "expected": {"regex": r"condition:\s*['\"]?service_healthy", "flags": "i"}},
     ],
     explanation="Каждая строка закрывает конкретную проблему. Тег postgres:16 фиксирует мажорную версию: latest однажды приедет новой мажоркой, которая не прочитает старые файлы данных. Пароль через ${POSTGRES_PASSWORD} compose подставит из .env, который лежит в .gitignore, — в репозитории остаётся только .env.example. Хост db работает, потому что сервисы compose находят друг друга по именам в общей сети. Именованный том pgdata переживает docker compose down (удалит его только down -v), а без него после пересоздания контейнера база окажется пустой. healthcheck с pg_isready и condition: service_healthy заставляют api ждать, пока PostgreSQL реально готов принимать соединения, а не просто пока запущен его контейнер.",
     hints=["Начни с db: тег образа, том и healthcheck.",
            "depends_on в длинной форме: db: → condition: service_healthy.",
            "Корневой ключ volumes: с вложенным pgdata: объявляет именованный том."])


# =====================================================================
shuffle_options(TASKS)
out = {"track": "backend", "part": "b3", "topics": TOPICS, "tasks": TASKS}
path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "out", "backend_b3.json")
path = os.path.normpath(path)
with open(path, "w", encoding="utf-8") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
print("written", path, "topics", len(TOPICS), "tasks", len(TASKS))
for t in TOPICS:
    print("  theory", t["topic_id"], len(t["theory"]))
