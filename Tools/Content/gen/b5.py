#!/usr/bin/env python3
"""Генератор контента «Стажёра»: backend, часть b5 (middle).

Темы: be-queues, be-async, be-security, be-observability, be-integration-tests.
Запуск: python3 gen/b5.py  ->  out/backend_b5.json
"""
import json
import random
import os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "out", "backend_b5.json")

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}

TOPICS = []
TASKS = []


def xp(d):
    return int(round(XP_BASE[d] * 1.5 / 5.0)) * 5


def C(s):
    """Код без ведущего перевода строки."""
    return s.lstrip("\n")


def topic(tid, theory):
    theory = theory.strip("\n")
    assert 1200 <= len(theory) <= 3500, (tid, len(theory))
    TOPICS.append({"topic_id": tid, "theory": theory})


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


def task(tid, n, typ, d, char, story, question, code=None, language=None, options=None,
         answer=None, tests=None, entry=None, explanation="", hints=(), time_limit=None):
    content = {"question": question, "code": code, "language": language}
    if entry:
        content["entry"] = entry
    content.update({"options": options, "correct_answer": answer, "test_cases": tests})
    TASKS.append({
        "task_id": "%s-%02d" % (tid, n),
        "topic_id": tid,
        "grade": "middle",
        "type": typ,
        "difficulty": d,
        "xp_reward": xp(d),
        "time_limit_minutes": time_limit,
        "character": char,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": list(hints),
    })


def run_ref(code, entry, args_list):
    """Считает expected по эталону (используется там, где руками легко ошибиться)."""
    ns = {}
    exec(code, ns)
    return [ns[entry](*args) for args in args_list]


# =====================================================================
# be-queues — Очереди: Celery, RabbitMQ, Kafka
# =====================================================================
T = "be-queues"

topic(T, """
Очередь нужна, когда работу не надо (или нельзя) делать прямо в HTTP-запросе: отправить чек, сгенерировать PDF, начислить бонусы, дёрнуть медленный внешний API. Запрос кладёт сообщение и сразу отвечает, а воркеры обрабатывают его в фоне. Заодно сглаживаются пики, и сервисы не зависят друг от друга напрямую.

Инструменты:
• Celery — фоновые задачи в Python поверх брокера (RabbitMQ или Redis): send_receipt.delay(order_id).
• RabbitMQ — брокер с очередями: сообщение удаляется после ack консьюмера, без ack возвращается в очередь.
• Kafka — распределённый лог: сообщения хранятся заданное время (retention), каждый читатель сам двигает свою позицию (offset) и может перечитать старое.

Гарантии доставки. На практике почти всегда at-least-once: воркер обработал сообщение, но упал до ack или коммита offset — сообщение придёт снова. Отсюда главное правило: обработчик обязан быть идемпотентным — повтор не меняет результат.

Как это сделать:
• у каждого сообщения уникальный id (event_id);
• перед обработкой проверяем, не видели ли его, а id записываем в той же транзакции, что и сам эффект: таблица processed_events с PRIMARY KEY и INSERT ... ON CONFLICT DO NOTHING;
• где можно, делаем операции естественно идемпотентными: «установить статус paid» вместо «прибавить 100 баллов».

    def handle(event, db):
        if db.is_processed(event["id"]):
            return  # дубль: просто подтверждаем
        with db.transaction():
            add_bonus(event["user_id"], event["points"])
            db.mark_processed(event["id"])

Ретраи. Повторяем только временные ошибки: таймаут, обрыв соединения, 502/503, 429. Ответы 400/404/422 повторять бессмысленно — плох сам запрос. Паузы растут экспоненциально и ограничены сверху: delay = min(base * 2 ** (n - 1), max_delay). В проде добавляют jitter — случайный разброс, чтобы тысячи воркеров не ретраили хором. Попыток — конечное число, потом сообщение уходит в DLQ (dead letter queue) на разбор. В Celery это autoretry_for, retry_backoff, retry_backoff_max, retry_jitter и max_retries.

Kafka:
• топик делится на партиции; порядок гарантирован только внутри одной партиции;
• партицию выбирают по хэшу ключа: все события с ключом order_id попадут в одну партицию и прочитаются по порядку; без ключа события одного заказа разъедутся по разным партициям;
• число партиций можно только увеличить, и тогда ключи перераспределяются — порядок для них на время ломается;
• consumer group: каждую партицию в группе читает ровно один консьюмер; консьюмеров больше, чем партиций, — лишние простаивают; разные группы читают топик независимо;
• offset коммитим после обработки: коммит до обработки — риск потерять сообщение при падении;
• lag — сколько сообщений группа ещё не обработала; растущий lag — консьюмеры не успевают или застряли;
• если между вызовами poll()/consume() прошло больше max.poll.interval.ms (по умолчанию 5 минут), консьюмера выкидывают из группы, начинается ребаланс, а незакоммиченную пачку прочитают заново.

Частые ошибки: задачу ставят в очередь до коммита транзакции (воркер не находит заказ — ставь после коммита или используй transactional outbox), передают в задачу объект вместо id, ретраят бесконечно без DLQ.
""")

task(T, 1, "quiz", 3, "teamlead",
     "Гена: «Прежде чем пускать тебя в консьюмеры заказов, проверю, как ты понимаешь Kafka. "
     "В топике order-events 6 партиций, сервис уведомлений читает его группой notifications, "
     "аналитика — своей группой analytics».",
     "Выбери все верные утверждения.",
     options=[
         "Если запустить 8 экземпляров сервиса уведомлений в группе notifications, два из них будут простаивать",
         "События одного заказа обработаются строго по порядку, даже если продюсер отправляет их без ключа",
         "Группа analytics получит все те же сообщения, независимо от того, что уже прочитала группа notifications",
         "Как только группа notifications прочитала сообщение, Kafka удаляет его из топика",
         "Если коммитить offset до обработки сообщения, при падении консьюмера это сообщение может потеряться",
         "Число партиций можно поднять с 6 до 12 в любой момент, и порядок событий по ключу от этого не пострадает",
     ],
     answer=[0, 2, 4],
     explanation="В группе каждую партицию читает ровно один консьюмер, поэтому при 6 партициях восьмой и седьмой экземпляры "
                 "сидят без работы. Разные группы независимы: у каждой свой offset, и analytics прочитает всё сама. "
                 "Kafka не удаляет сообщения после чтения — они живут до истечения retention. Коммит offset до обработки "
                 "даёт at-most-once: упали посередине — сообщение считается прочитанным и теряется. Порядок гарантирован "
                 "только внутри партиции: без ключа события заказа попадут в разные партиции, а при увеличении их числа "
                 "меняется соответствие ключ → партиция, и порядок для ключа на время ломается.",
     hints=[
         "Сколько консьюмеров одной группы может читать одну партицию?",
         "Kafka — это лог с retention, а не очередь, из которой забирают сообщения.",
         "Партиция выбирается как hash(key) % число_партиций. Что будет, если число партиций изменится?",
     ])

IDEM_STARTER = C('''
def apply_bonus_events(events, processed_ids):
    balances = {}
    # твой код
    return {"balances": balances, "processed": sorted(processed_ids)}
''')

IDEM_REF = C('''
def apply_bonus_events(events, processed_ids):
    processed = set(processed_ids)
    balances = {}
    for event in events:
        event_id = event["id"]
        if event_id in processed:
            continue  # дубль: уже обработан раньше или в этой же пачке
        processed.add(event_id)
        if event["points"] <= 0:
            continue  # битое сообщение: не начисляем, но и читать снова не будем
        user = event["user_id"]
        balances[user] = balances.get(user, 0) + event["points"]
    return {"balances": balances, "processed": sorted(processed)}
''')


def ev(i, u, p):
    return {"id": i, "user_id": u, "points": p}


task(T, 2, "write_code", 3, "qa",
     "Ира: «У трёх покупателей бонусы за один заказ начислились дважды. В логах видно: после рестарта пода "
     "консьюмер заново прочитал пачку bonus-events. Гена говорит, что Kafka доставляет at-least-once и дубли будут всегда, "
     "так что чинить надо обработчик»." ,
     "Напиши apply_bonus_events(events, processed_ids). events — сообщения в порядке чтения, вида "
     "{\"id\": \"e01\", \"user_id\": \"u1\", \"points\": 50}; processed_ids — id, обработанные раньше (хранятся в базе). "
     "Верни {\"balances\": {user_id: сколько баллов начислено сейчас}, \"processed\": отсортированный список всех "
     "обработанных id — старых и новых}. Каждое сообщение учитывается ровно один раз: и если его id уже есть в processed_ids, "
     "и если оно повторилось в пачке. Сообщение с points <= 0 — битое: баллы не начисляем, но id помечаем обработанным. "
     "В balances — только пользователи, которым что-то начислили.",
     code=IDEM_STARTER, language="python", entry="apply_bonus_events", answer=IDEM_REF,
     tests=[
         {"input": [[ev("e01", "u1", 50), ev("e02", "u2", 30), ev("e01", "u1", 50)], []],
          "expected": {"balances": {"u1": 50, "u2": 30}, "processed": ["e01", "e02"]}},
         {"input": [[ev("e05", "u1", 100), ev("e06", "u1", 20)], ["e03", "e05"]],
          "expected": {"balances": {"u1": 20}, "processed": ["e03", "e05", "e06"]}},
         {"input": [[ev("e07", "u3", 0), ev("e08", "u3", -10), ev("e09", "u4", 15)], []],
          "expected": {"balances": {"u4": 15}, "processed": ["e07", "e08", "e09"]}},
         {"input": [[ev("e10", "u5", -5), ev("e10", "u5", -5), ev("e11", "u5", 40), ev("e11", "u5", 40),
                     ev("e12", "u5", 10)], ["e02"]],
          "expected": {"balances": {"u5": 50}, "processed": ["e02", "e10", "e11", "e12"]}},
         {"input": [[], ["e01"]], "expected": {"balances": {}, "processed": ["e01"]}},
     ],
     explanation="При at-least-once дубль — нормальная ситуация, а не авария: консьюмер упал после обработки, но до коммита "
                 "offset, и пачка пришла снова. Поэтому решение — дедупликация по id сообщения: множество processed "
                 "проверяется до эффекта и пополняется сразу, так что ловятся и старые дубли, и повторы внутри пачки. "
                 "Битое сообщение тоже помечаем обработанным, иначе оно будет приходить вечно (в проде его ещё отправляют в DLQ). "
                 "В реальном сервисе processed — таблица с PRIMARY KEY по event_id, и запись id идёт в той же транзакции, "
                 "что и начисление: иначе между ними снова можно упасть.",
     hints=[
         "Сделай из processed_ids множество — проверка «уже видели?» станет O(1).",
         "Добавляй id в множество до проверки points, чтобы битые сообщения тоже считались обработанными.",
         "Порядок в цикле: дубль? → пропустить; иначе пометить; points <= 0? → пропустить; иначе начислить.",
     ])

RETRY_BUGGY = C(r'''
RETRYABLE = {"timeout", "429", "502", "503"}


def retry_plan(outcomes, base_delay, max_delay, max_attempts):
    """Считает, как пройдут ретраи вызова платёжного шлюза.

    outcomes[i] — результат i-й попытки: "ok", "timeout", "429", "503", "400"...
    Повторяем только временные ошибки из RETRYABLE, всего не больше max_attempts попыток.
    Пауза перед n-м повтором: base_delay * 2 ** (n - 1), но не больше max_delay.
    Возвращает {"status": "ok" | "failed", "attempts": сколько попыток сделали,
    "delays": паузы перед каждым повтором}.
    """
    delays = []
    attempt = 0
    while attempt < max_attempts:
        result = outcomes[attempt]
        attempt += 1
        if result == "ok":
            return {"status": "ok", "attempts": attempt, "delays": delays}
        if attempt < max_attempts:
            delays.append(base_delay * 2 ** (attempt - 1))
    return {"status": "failed", "attempts": attempt, "delays": delays}
''')

RETRY_FIXED = C(r'''
RETRYABLE = {"timeout", "429", "502", "503"}


def retry_plan(outcomes, base_delay, max_delay, max_attempts):
    """Считает, как пройдут ретраи вызова платёжного шлюза.

    outcomes[i] — результат i-й попытки: "ok", "timeout", "429", "503", "400"...
    Повторяем только временные ошибки из RETRYABLE, всего не больше max_attempts попыток.
    Пауза перед n-м повтором: base_delay * 2 ** (n - 1), но не больше max_delay.
    Возвращает {"status": "ok" | "failed", "attempts": сколько попыток сделали,
    "delays": паузы перед каждым повтором}.
    """
    delays = []
    attempt = 0
    while attempt < max_attempts:
        result = outcomes[attempt]
        attempt += 1
        if result == "ok":
            return {"status": "ok", "attempts": attempt, "delays": delays}
        if result not in RETRYABLE:
            break  # 4xx повторять бессмысленно
        if attempt < max_attempts:
            delays.append(min(base_delay * 2 ** (attempt - 1), max_delay))
    return {"status": "failed", "attempts": attempt, "delays": delays}
''')

task(T, 3, "find_bug", 4, "devops_colleague",
     "Дима: «Платёжный шлюз прислал письмо: мы по пять раз подряд шлём им запросы, на которые они уже ответили "
     "400 Bad Request, грозят забанить по IP. А ещё одна задача в воркере висела 17 минут между попытками. "
     "Глянь нашу обёртку ретраев — по-моему, она живёт своей жизнью»." ,
     "retry_plan моделирует ретраи (outcomes — ответы шлюза на каждую попытку по порядку, их всегда хватает). "
     "Исправь функцию так, чтобы она работала по контракту из докстринга.",
     code=RETRY_BUGGY, language="python", entry="retry_plan", answer=RETRY_FIXED,
     tests=[
         {"input": [["timeout", "503", "ok"], 1, 30, 5],
          "expected": {"status": "ok", "attempts": 3, "delays": [1, 2]}},
         {"input": [["400", "400", "400", "400", "400"], 1, 30, 5],
          "expected": {"status": "failed", "attempts": 1, "delays": []}},
         {"input": [["timeout", "timeout", "timeout", "timeout", "timeout", "timeout"], 2, 10, 6],
          "expected": {"status": "failed", "attempts": 6, "delays": [2, 4, 8, 10, 10]}},
         {"input": [["503", "429", "422", "ok"], 1, 60, 5],
          "expected": {"status": "failed", "attempts": 3, "delays": [1, 2]}},
         {"input": [["ok"], 1, 30, 3],
          "expected": {"status": "ok", "attempts": 1, "delays": []}},
         {"input": [["timeout", "timeout", "timeout", "ok"], 1, 30, 3],
          "expected": {"status": "failed", "attempts": 3, "delays": [1, 2]}},
     ],
     explanation="Багов два, и оба из письма Димы. Первый: множество RETRYABLE объявлено, но не используется, поэтому "
                 "на 400/422 код честно делает все попытки — а ошибка 4xx значит, что плох сам запрос, и повтор ничего не изменит, "
                 "только разозлит партнёра. Второй: пауза растёт как 2 ** n без потолка — при 10 попытках это уже 512 × base, "
                 "отсюда задача, висящая минутами; min(..., max_delay) ограничивает рост. В проде к паузе ещё добавляют "
                 "случайный jitter, чтобы воркеры не ретраили синхронно, а после последней попытки отправляют задачу в DLQ. "
                 "И помни: таймаут не означает, что платёж не прошёл, — ретраить списание можно только с ключом идемпотентности.",
     hints=[
         "Сравни докстринг с кодом построчно: какое требование нигде не используется?",
         "Что код делает с результатом \"400\"? А чему равна пауза перед 6-м повтором при base_delay=2?",
         "Нужны break для результата не из RETRYABLE и min(base_delay * 2 ** (attempt - 1), max_delay).",
     ])

task(T, 4, "architecture", 4, "manager",
     "Стас: «Клиенты жалуются: в приложении заказ висит в статусе „Оплачен“, хотя его уже доставили. "
     "Бывает не у всех, где-то у одного заказа из пары сотен. Это же просто статусы, почему они путаются?»",
     "Почему статусы перепутываются и какое решение правильное?",
     code=C('''
# сервис заказов (продюсер)
producer.send(
    "order-events",
    value={"order_id": order.id, "status": new_status, "at": now_iso()},
)

# топик order-events: 12 партиций
# консьюмер order-status: 12 подов в одной группе, на каждое событие
#   UPDATE orders SET status = :status WHERE id = :order_id
'''),
     language="python",
     options=[
         "Слать события с ключом order_id — события заказа лягут в одну партицию; в консьюмере не откатывать статус назад",
         "Сделать в топике одну партицию: тогда порядок будет общим для всех событий, а 12 подов разберут нагрузку",
         "Добавить в консьюмер паузу в 1 секунду перед обработкой каждого события, чтобы ранние события заказа успели дойти первыми",
         "Увеличить число подов консьюмера до 24, чтобы события обрабатывались быстрее и не обгоняли друг друга",
         "Перейти на RabbitMQ: там очередь FIFO, и порядок сообщений гарантирован при любом числе консьюмеров",
     ],
     answer=0,
     explanation="Без ключа продюсер раскладывает события по разным партициям, их читают разные поды с разной скоростью, "
                 "и «delivered» может записаться раньше «paid» — тогда «paid» перезатирает финальный статус. Kafka гарантирует "
                 "порядок только внутри партиции, поэтому ключ order_id — стандартное решение: заказы по-прежнему "
                 "распределены по 12 партициям, а события одного заказа идут строго друг за другом. Проверка версии в "
                 "консьюмере — страховка от ретраев продюсера и ручных перезаливок. Одна партиция даёт порядок ценой "
                 "параллельности (читать сможет один под), пауза лишь уменьшает вероятность гонки, 24 пода при 12 партициях — "
                 "12 простаивающих. У RabbitMQ с несколькими конкурирующими консьюмерами порядок обработки тоже не гарантирован.",
     hints=[
         "Где в Kafka вообще есть гарантия порядка?",
         "Как продюсер решает, в какую партицию положить сообщение?",
     ])

task(T, 5, "incident", 5, "devops_colleague",
     "Дима: «С обеда растёт lag у консьюмера чеков receipts-worker: клиенты два часа не получают чеки на почту, "
     "а некоторым пришло по три-четыре одинаковых. Поды живы, CPU почти ноль. Скинул всё, что есть»." ,
     "Что происходит и что делать? Выбери все верные утверждения и действия.",
     code=C('''
$ kafka-consumer-groups.sh --bootstrap-server kafka:9092 --describe --group receipts-worker
GROUP            TOPIC       PARTITION  CURRENT-OFFSET  LOG-END-OFFSET  LAG    CONSUMER-ID
receipts-worker  order-paid  0          1840211         1852977         12766  rdkafka-5c1e0f...
receipts-worker  order-paid  1          1838004         1851139         13135  rdkafka-a2b07d...
receipts-worker  order-paid  2          1839950         1852460         12510  rdkafka-91de4a...
# через 30 минут: CURRENT-OFFSET не сдвинулся ни в одной партиции, LAG вырос ещё на ~4000

$ kubectl logs receipts-worker-7b9c-2 --since=15m
14:02:11 INFO  got batch: 500 messages from order-paid [1]
14:02:13 INFO  receipt sent order=88123 took=1.9s
14:02:15 INFO  receipt sent order=88124 took=2.1s
...
14:07:11 WARN  MAXPOLL [thrd:main]: Application maximum poll interval (300000ms) exceeded by 412ms (adjust max.poll.interval.ms for long-running message processing): leaving group
...
14:18:52 INFO  receipt sent order=88622 took=2.0s
14:18:52 ERROR commit failed: KafkaError{code=UNKNOWN_MEMBER_ID,val=25,str="Commit failed: Broker: Unknown member"}
14:18:55 INFO  partitions assigned: order-paid [1] offset 1838004
14:18:55 INFO  got batch: 500 messages from order-paid [1]
14:18:57 INFO  receipt sent order=88123 took=2.0s

# код консьюмера (confluent-kafka)
batch = consumer.consume(num_messages=500, timeout=1.0)
for msg in batch:
    send_receipt(msg)                   # рендер PDF + SMTP, ~2 с на сообщение
consumer.commit(asynchronous=False)     # коммит после всей пачки
# max.poll.interval.ms = 300000 (по умолчанию)
'''),
     language="text",
     options=[
         "Пачка обрабатывается ~16 минут, дольше max.poll.interval.ms: консьюмер вылетает из группы до коммита и читает её заново",
         "Уменьшить пачку до 20–50 сообщений или коммитить offset каждые N сообщений, чтобы укладываться в max.poll.interval.ms",
         "Сделать отправку чека идемпотентной: отмечать в базе, что чек по заказу отправлен, и пропускать повтор",
         "Добавить ещё 6 подов receipts-worker, чтобы параллельно разгрести lag, пока клиенты ждут чеки на почту",
         "Сбросить offset группы на latest: lag сразу обнулится, и новые чеки снова пойдут без задержки",
         "Перезапустить все поды receipts-worker: после рестарта консьюмеры переподключатся и начнут с чистого листа",
     ],
     answer=[0, 1, 2],
     time_limit=20,
     explanation="Ключ — строчка MAXPOLL и «Unknown member» при коммите: консьюмер не успевает вернуться к consume() за 5 минут, "
                 "брокер считает его мёртвым и отдаёт партицию заново, а коммит пачки уже отклоняется. Offset не двигается, "
                 "та же пачка обрабатывается по кругу — отсюда и рост lag, и дубли писем. Лечение — укладываться в интервал: "
                 "маленькие пачки и частые коммиты (поднимать max.poll.interval.ms тоже можно, но это прячет проблему и "
                 "замедляет обнаружение реально зависших консьюмеров). Идемпотентность нужна в любом случае: at-least-once "
                 "всегда даст повторы. Новые поды не помогут — партиций всего 3, лишние будут простаивать; сброс на latest "
                 "выбросит ~38 тысяч неотправленных чеков; рестарт снова начнёт с того же закоммиченного offset.",
     hints=[
         "Сравни время обработки одной пачки с max.poll.interval.ms.",
         "Почему в логах после ребаланса снова order=88123?",
         "Сколько партиций у топика и сколько консьюмеров группа реально может занять?",
     ])


# =====================================================================
# be-async — Асинхронность в Python
# =====================================================================
T = "be-async"

topic(T, """
asyncio — это один поток и event loop, который переключается между задачами в точках await. Пока корутина ждёт ответа сети, цикл обслуживает другие запросы. Поэтому один процесс uvicorn держит сотни одновременных соединений — но только пока никто не блокирует цикл.

Базовые правила:
• async def создаёт корутину. Вызов без await ничего не выполняет, а возвращает объект корутины; Python потом предупредит «coroutine ... was never awaited».
• await пишется только внутри async def. asyncio.run() запускает новый цикл — внутри уже работающего (например, в хендлере FastAPI) он упадёт с RuntimeError.
• Любой блокирующий вызов внутри async def останавливает весь цикл: requests, time.sleep, синхронный драйвер БД (psycopg2), тяжёлые вычисления. Все остальные запросы воркера, включая /health, ждут.

FastAPI и блокировки:
• async def-эндпоинт выполняется прямо в event loop — внутри только асинхронные библиотеки: httpx.AsyncClient, asyncpg или AsyncSession из SQLAlchemy, redis.asyncio, await asyncio.sleep().
• Обычный def-эндпоинт FastAPI запускает в пуле потоков (по умолчанию 40) — синхронный код там не блокирует цикл.
• Синхронную функцию из async-кода вызывают через await asyncio.to_thread(func, arg) или run_in_threadpool из Starlette. CPU-тяжёлое (PDF, отчёты) — в очередь задач или пул процессов.

Параллельные запросы. Три независимых await подряд занимают сумму времени, gather — максимум:

    order, delivery = await asyncio.gather(
        get_order(order_id),
        get_delivery(order_id),
    )

По умолчанию gather пробрасывает первое исключение; с return_exceptions=True ошибки вернутся в списке результатов. asyncio.TaskGroup (Python 3.11+) при ошибке одной задачи отменяет остальные. create_task с немедленным await параллельности не даёт: сначала создай все задачи, потом жди.

Таймауты. У каждого внешнего вызова должен быть таймаут. requests по умолчанию ждёт вечно; у httpx по умолчанию 5 секунд, но timeout=None его отключает. Общий таймаут на кусок кода:

    async with asyncio.timeout(2):
        recs = await get_recommendations(user_id)

Старый способ — asyncio.wait_for(coro, 2). При таймауте некритичную часть (рекомендации) лучше отдать пустой, чем повесить всю страницу.

Практика:
• один httpx.AsyncClient на приложение (создаётся в lifespan), а не на каждый запрос — иначе теряется пул соединений;
• симптом блокировки цикла: CPU низкий, база свободна, а все запросы и health-check тормозят одновременно;
• найти виновника: py-spy dump --pid <pid> покажет, где стоит главный поток; режим отладки asyncio (PYTHONASYNCIODEBUG=1) пишет в лог «Executing <Task ...> took 1.2 seconds».
""")

task(T, 1, "find_bug", 3, "qa",
     "Ира: «Отмена заказа сломалась: POST /orders/{id}/cancel всегда отвечает 500, даже для несуществующего id. "
     "Шаги: создать заказ, нажать „Отменить“. Кусок кода и лог приложила»." ,
     "В чём причина 500 и как правильно исправить?",
     code=C('''
async def get_order(db: AsyncSession, order_id: int) -> Order | None:
    return await db.get(Order, order_id)


@router.post("/orders/{order_id}/cancel")
async def cancel_order(order_id: int, db: AsyncSession = Depends(get_db)):
    order = get_order(db, order_id)
    if order is None:
        raise HTTPException(status_code=404, detail="Order not found")
    if order.status == "shipped":
        raise HTTPException(status_code=409, detail="Order already shipped")
    order.status = "cancelled"
    await db.commit()
    return {"id": order.id, "status": order.status}

# лог:
# AttributeError: 'coroutine' object has no attribute 'status'
# RuntimeWarning: coroutine 'get_order' was never awaited
'''),
     language="python",
     options=[
         "get_order вызвана без await: в order лежит корутина, а не заказ. Нужно order = await get_order(db, order_id)",
         "db.get не находит свежий заказ и возвращает None — перед ним нужно вызвать await db.refresh(), чтобы сессия увидела данные",
         "FastAPI не поддерживает async в POST-хендлерах с зависимостями — надо убрать async у cancel_order",
         "Корутину нужно выполнить синхронно: order = asyncio.run(get_order(db, order_id))",
         "Проверку if order is None нужно заменить на if not order: пустой результат AsyncSession — не None",
     ],
     answer=0,
     explanation="Вызов async-функции без await не выполняет её, а возвращает объект корутины. Он не None, поэтому проверка на 404 "
                 "проскакивает (отсюда 500 даже для несуществующего id), а на order.status всё падает — у корутины нет такого атрибута. "
                 "Второе предупреждение прямо говорит, что корутину так никто и не дождался. asyncio.run внутри работающего цикла "
                 "FastAPI упадёт с RuntimeError, а убирать async бессмысленно — внутри всё равно нужен await db.commit(). "
                 "Такие ошибки ловят mypy или pyright ещё до запуска: они видят, что у Coroutine нет атрибута status.",
     hints=[
         "Что возвращает вызов async-функции, если перед ним нет await?",
         "Почему не сработала проверка на None, хотя заказа с таким id нет?",
     ])

task(T, 2, "find_bug", 4, "manager",
     "Стас: «Страница заказа в приложении грузится полторы секунды, а вчера вечером, пока лежал сервис рекомендаций, "
     "вообще отваливалась через минуту с 504. Пользователи уходят. Сделай, чтобы летало»." ,
     "Какое исправление решает обе проблемы — и медленную загрузку, и зависание?",
     code=C('''
http = httpx.AsyncClient(timeout=None)  # создаётся один раз при старте приложения


async def fetch_json(url: str) -> dict:
    resp = await http.get(url)
    resp.raise_for_status()
    return resp.json()


@router.get("/orders/{order_id}/page")
async def order_page(order_id: int):
    # каждый сервис отвечает ~500 мс, друг от друга запросы не зависят
    order = await fetch_json(f"{ORDERS_URL}/orders/{order_id}")
    delivery = await fetch_json(f"{DELIVERY_URL}/tracking/{order_id}")
    recs = await fetch_json(f"{RECS_URL}/recommendations?order={order_id}")
    return {"order": order, "delivery": delivery, "recommendations": recs}
'''),
     language="python",
     options=[
         "Запросы — одновременно через asyncio.gather, клиенту — таймаут (httpx.Timeout(2.0)), а рекомендации при ошибке — пустым списком",
         "Сделать order_page обычной def-функцией — FastAPI выполнит её в пуле потоков, и три запроса пойдут параллельно",
         "Обернуть каждый вызов в asyncio.create_task(...) и сразу делать await этой задачи — так запросы к трём сервисам станут конкурентными",
         "Поднять число воркеров uvicorn с 4 до 16, чтобы медленные запросы не занимали все обработчики",
         "Создавать новый httpx.AsyncClient внутри fetch_json, чтобы соединения разных запросов не мешали друг другу",
     ],
     answer=0,
     explanation="Три await подряд выполняются последовательно: 500 + 500 + 500 мс. Запросы независимы, поэтому gather запускает "
                 "их одновременно, и страница ждёт только самый медленный — около 500 мс. Вторая проблема — timeout=None: он "
                 "отключает стандартные 5 секунд httpx, и зависший сервис рекомендаций держит запрос, пока nginx не оборвёт его "
                 "через 60 секунд с 504. Рекомендации некритичны, поэтому при ошибке их отдают пустыми (return_exceptions=True "
                 "или try/except вокруг вызова), а заказ показывается. create_task с немедленным await — то же последовательное "
                 "ожидание, def-хендлер не распараллелит вызовы, больше воркеров не сократит время одного запроса, а клиент на "
                 "каждый вызов лишь убьёт пул соединений.",
     hints=[
         "Сколько времени займут три await подряд, если каждый — 500 мс?",
         "Что делает параметр timeout=None у httpx.AsyncClient?",
         "Нужны и параллельность, и таймаут, и запасной ответ для некритичной части.",
     ])

task(T, 3, "code_review", 4, "teamlead",
     "Марина: «Джун переписал расчёт доставки на async, „чтобы было быстрее“. На нагрузочном тесте RPS всего сервиса "
     "упал в десять раз. Посмотри PR и отметь, что реально надо чинить»." ,
     "Выбери все замечания, которые действительно стоит оставить в ревью.",
     code=C('''
import time

import requests
from fastapi import APIRouter

router = APIRouter()


@router.get("/delivery/price")
async def delivery_price(city: str, weight_kg: float):
    for attempt in range(3):
        try:
            resp = requests.get(
                "https://api.courier.example/v1/price",
                params={"city": city, "weight": weight_kg},
            )
            resp.raise_for_status()
            break
        except requests.RequestException:
            time.sleep(1)
    else:
        return {"price": None, "error": "courier unavailable"}
    price = resp.json()["price"]
    return {"price": round(price * 1.2, 2)}
'''),
     language="python",
     options=[
         "requests.get — синхронный вызов в async def: пока курьеры отвечают, event loop стоит. Нужен httpx.AsyncClient или def-хендлер",
         "time.sleep(1) между попытками тоже блокирует event loop — в async-коде нужен await asyncio.sleep(1)",
         "У запроса нет таймаута: requests по умолчанию ждёт бесконечно, и зависший API курьеров подвесит обработчик",
         "GET к внешнему API нельзя ретраить: повторный запрос может посчитать доставку дважды и списать деньги",
         "for … else читается плохо и путает новичков — лучше while True со счётчиком попыток и явным break",
         "async def всегда быстрее def, поэтому остальные эндпоинты сервиса тоже стоит перевести на async",
     ],
     answer=[0, 1, 2],
     explanation="Слово async не делает код асинхронным: requests и time.sleep блокируют единственный поток event loop, и пока "
                 "один запрос ждёт курьеров (а при ретраях ещё и спит по секунде), все остальные запросы воркера стоят — "
                 "отсюда падение RPS всего сервиса. Лечится асинхронным клиентом и await asyncio.sleep, либо обычным def-хендлером, "
                 "который FastAPI унесёт в пул потоков. Отсутствие таймаута у requests — отдельная мина: зависание партнёра "
                 "становится зависанием нашего API. GET идемпотентен, ретраить его можно; for-else — законная конструкция "
                 "(вкусовщина), а async сам по себе ничего не ускоряет — выигрыш только там, где много ожидания I/O.",
     hints=[
         "Что происходит с остальными запросами, пока этот ждёт ответа внутри requests.get?",
         "Какая функция здесь «спит», не отдавая управление циклу?",
         "Сколько по умолчанию ждёт requests, если сервер не отвечает?",
     ])

task(T, 4, "incident", 5, "devops_colleague",
     "Дима: «Распродажа, а Маркет встал: все эндпоинты отвечают по 20–30 секунд, даже /health, Kubernetes уже "
     "перезапускает поды по liveness. CPU у подов около 3%, база скучает. Снял py-spy с одного пода»." ,
     "Что стало причиной и что делать? Выбери все верные утверждения и действия.",
     code=C('''
$ kubectl top pods -l app=market-api
NAME                         CPU(cores)   MEMORY(bytes)
market-api-6d8f7b9c4-2kqzp   31m          212Mi
market-api-6d8f7b9c4-9xw4t   28m          208Mi

$ kubectl get events --field-selector involvedObject.name=market-api-6d8f7b9c4-2kqzp | tail -2
Warning  Unhealthy  Liveness probe failed: Get "http://10.0.3.17:8000/health": context deadline exceeded (Client.Timeout exceeded while awaiting headers)
Normal   Killing    Container api failed liveness probe, will be restarted

$ py-spy dump --pid 1
Process 1: /usr/local/bin/python /usr/local/bin/uvicorn app.main:app --host 0.0.0.0 --port 8000
Thread 1 (idle): "MainThread"
    recv_into (ssl.py:1295)
    readinto (socket.py:720)
    _read_status (http/client.py:292)
    begin (http/client.py:331)
    getresponse (http/client.py:1428)
    urlopen (urllib3/connectionpool.py:793)
    send (requests/adapters.py:667)
    request (requests/sessions.py:589)
    get (requests/api.py:73)
    check_stock (app/services/warehouse.py:16)
    add_to_cart (app/api/cart.py:31)
    run_endpoint_function (fastapi/routing.py:212)
    ...
    _run_once (asyncio/base_events.py:1987)
    run_forever (asyncio/base_events.py:641)

$ sed -n '15,18p' app/services/warehouse.py
def check_stock(product_id: int) -> int:
    resp = requests.get(f"{WAREHOUSE_URL}/stock/{product_id}", timeout=10)
    resp.raise_for_status()
    return resp.json()["available"]

# сервис склада во время распродажи: p50 = 4.8 s
'''),
     language="text",
     options=[
         "add_to_cart — async-хендлер, а check_stock синхронно зовёт requests.get: пока склад отвечает 5 с, event loop пода стоит",
         "Быстро смягчить: сделать add_to_cart def-хендлером или вызвать check_stock через await asyncio.to_thread(...)",
         "Исправить по-настоящему: check_stock на httpx.AsyncClient с коротким таймаутом (1–2 с) и понятной ошибкой",
         "Отключить liveness-пробу: проблема в Kubernetes, который зря убивает живые поды посреди распродажи",
         "Поднять CPU requests и limits подам: под нагрузкой распродажи им не хватает процессора на event loop",
         "Увеличить пул соединений к PostgreSQL, чтобы запросы корзины не ждали свободного соединения",
     ],
     answer=[0, 1, 2],
     time_limit=20,
     explanation="py-spy показывает, что главный поток — тот самый, где крутится event loop, — сидит внутри requests.get, "
                 "вызванного из async-хендлера add_to_cart. Пока склад отвечает почти 5 секунд, цикл не может обслужить никого: "
                 "запросы копятся, /health не отвечает, и Kubernetes добивает поды. Низкий CPU и свободная база — классический "
                 "признак блокировки, а не нехватки ресурсов. Быстрое смягчение — увести блокирующий вызов в пул потоков "
                 "(def-хендлер или asyncio.to_thread), настоящее исправление — асинхронный клиент с коротким таймаутом. "
                 "Отключение liveness спрячет симптом и оставит поды висеть, а CPU и пул БД здесь ни при чём.",
     hints=[
         "Какой поток показывает py-spy и что в нём работает кроме твоего кода?",
         "CPU 3% и свободная база — это нехватка ресурсов или ожидание?",
         "Ищи, где синхронная библиотека вызывается из async def.",
     ])


# =====================================================================
# be-security — Безопасность бэкенда
# =====================================================================
T = "be-security"

topic(T, """
Безопасность — не отдельная фича, а привычка на каждом ревью. Ориентир — OWASP Top 10, список главных рисков веб-приложений; актуальная версия — 2025. На первом месте, как и в 2021, — A01 Broken Access Control (нарушение контроля доступа, в 2025 сюда влили и SSRF). Дальше: A02 Security Misconfiguration, A03 Software Supply Chain Failures (уязвимые и подменённые зависимости, сборка, CI), A04 Cryptographic Failures, A05 Injection, A06 Insecure Design, A07 Authentication Failures, A08 Software or Data Integrity Failures, A09 Security Logging and Alerting Failures и новая A10 Mishandling of Exceptional Conditions — ошибки обработки сбоев, когда система при исключении «открывается» (fail open).

SQL-инъекция. Ввод пользователя, склеенный с SQL через f-строку или +, становится частью запроса: q = "' OR 1=1 --" отдаст всё. Лечится только параметрами — драйвер передаёт значение отдельно от текста запроса:

    db.execute(text("SELECT id FROM reviews WHERE text ILIKE :p"), {"p": f"%{q}%"})

ORM (filter, ilike) тоже параметризует. Не работают чёрные списки слов, ручное экранирование и проверки на фронтенде. Имя колонки или направление сортировки параметром не передать — только белый список: {"date": "created_at", "price": "price"}.get(sort).

Контроль доступа и IDOR. IDOR — объект достают по id из запроса и не проверяют владельца: /orders/10452 отдаёт чужой заказ. Правило: каждый запрос к данным фильтруется по пользователю из токена — WHERE id = :id AND user_id = :uid. На чужой объект — 404, а не 403, чтобы не подтверждать, что он есть. UUID вместо чисел усложняет перебор, но проверку не заменяет. Админские эндпоинты проверяют роль, а не только факт входа; входная схема не принимает поля вроде role и is_admin.

Секреты. Пароли БД, ключи API и облака не живут в коде, Dockerfile и git — только переменные окружения или секрет-менеджер (Vault, Kubernetes Secrets). .env — в .gitignore, в CI — gitleaks или secret scanning. Секрет попал в репозиторий — он скомпрометирован: сначала отзываем и перевыпускаем, потом чистим. Токены и пароли не пишем в логи.

Rate limiting защищает логин, отправку SMS и писем, сброс пароля и дорогие эндпоинты. Алгоритмы:
• fixed window — счётчик на минуту (в Redis: INCR + EXPIRE); просто, но на стыке окон пролезает двойная пачка;
• sliding window log — храним время каждого пропущенного запроса и считаем попавшие в последние N секунд; точно, памяти на ключ не больше лимита;
• token bucket — токены пополняются с постоянной скоростью, допускает короткие всплески.
Ключ лимита — IP, аккаунт, телефон, API-ключ, часто несколько сразу. При превышении — 429 Too Many Requests и заголовок Retry-After. Если подов несколько, счётчики держат в Redis, а не в памяти процесса.

Атаки на вход. Brute force — много паролей к одному аккаунту, спасает лимит на аккаунт. Credential stuffing — пары логин-пароль из чужих утечек с тысяч IP, по 1–2 попытки на аккаунт: лимиты по IP и аккаунту почти не срабатывают. Защита — CAPTCHA при аномальном трафике, проверка паролей по базам утечек, 2FA и алерт на всплеск неудачных входов. Ошибка входа одна для всех случаев («неверный email или пароль»), чтобы не подсказывать, какие email зарегистрированы.
""")

task(T, 1, "quiz", 3, "teamlead",
     "Гена: «Пришёл отчёт внешнего пентеста „Лампового Маркета“. Прежде чем раздавать задачи, разложим находки "
     "по OWASP Top 10. Начнём с категории номер один — Broken Access Control»." ,
     "Какие находки из отчёта относятся к Broken Access Control? Выбери все верные.",
     options=[
         "GET /api/orders/10452 отдаёт заказ другого покупателя, если подставить чужой id",
         "POST /api/admin/refunds проверяет только, что пользователь залогинен, но не его роль",
         "Ссылка /invoices/2026-000871.pdf из письма открывается без входа, номера идут подряд",
         "Поиск по товарам падает с ошибкой SQL, если ввести в строку поиска одинарную кавычку",
         "Пароли пользователей хранятся как MD5 без соли, хеши видны в дампе базы из бэкапа",
         "В проде работает версия библиотеки Pillow с известной CVE, обновление отложено на квартал",
     ],
     answer=[0, 1, 2],
     explanation="Broken Access Control (A01 в OWASP Top 10:2025) — когда система пускает пользователя к данным или действиям, которые ему не положены. Чужой заказ по id и чужие счета перебором номера — это IDOR, самый частый вид такой уязвимости; эндпоинт возвратов без проверки роли — повышение привилегий. Кавычка, ломающая SQL, — признак инъекции (A05 Injection), MD5 без соли — A04 Cryptographic Failures, старая Pillow с CVE — A03 Software Supply Chain Failures: в версии 2025 так расширили бывшую категорию «уязвимые и устаревшие компоненты». Раскладка по категориям нужна не для отчёта: она подсказывает, где искать похожие дыры по всему коду.",
     hints=[
         "Access Control — про вопрос «а этому пользователю сюда можно?».",
         "Найди находки, где проблема не в коде запроса или хранении, а в том, кто получает доступ.",
     ])

task(T, 2, "find_bug", 3, "qa",
     "Ира: «Нашла странное в поиске по отзывам. Если ввести ' OR 1=1 --, вылезают отзывы других товаров и даже скрытые "
     "модерацией. А если ввести одну кавычку — 500. Шаги: GET /api/reviews?product_id=5&q=' OR 1=1 --»." ,
     "В чём уязвимость и как её правильно закрыть?",
     code=C('''
@router.get("/reviews")
def search_reviews(product_id: int, q: str, db: Session = Depends(get_db)):
    sql = (
        "SELECT id, author, text FROM reviews "
        f"WHERE product_id = {product_id} AND text ILIKE '%{q}%' AND hidden = false "
        "ORDER BY created_at DESC LIMIT 20"
    )
    return db.execute(text(sql)).mappings().all()
'''),
     language="python",
     options=[
         "SQL-инъекция: q вклеивается в SQL. Передавать значения параметрами: text(\"… ILIKE :pattern …\") и {\"pattern\": f\"%{q}%\"}",
         "Экранировать ввод: удалять из q опасные слова (OR, SELECT, DROP), кавычки и символы -- перед подстановкой в текст запроса",
         "Ограничить длину поля поиска 50 символами на фронтенде и запретить в нём кавычки и спецсимволы",
         "Обернуть execute в try/except и при ошибке SQL возвращать пустой список, чтобы не светить 500 наружу",
         "Заменить ILIKE на точное сравнение = : строку, которая должна совпасть целиком, инъекцией не обойти",
     ],
     answer=0,
     explanation="Строка ' OR 1=1 -- закрывает кавычку, добавляет всегда истинное условие, а -- превращает остаток запроса "
                 "(hidden = false, ORDER BY, LIMIT) в комментарий — отсюда чужие и скрытые отзывы. Одна кавычка ломает синтаксис, "
                 "поэтому 500. Параметризованный запрос передаёт q отдельно от текста SQL, и база видит в нём только значение, "
                 "что бы там ни было. Знаки % для ILIKE добавляются в значение параметра, а не в SQL. Чёрные списки обходятся "
                 "(регистр, комментарии, кодировки), фронтенд атакующему не нужен — он шлёт запрос напрямую, try/except прячет "
                 "500, но не дыру, а = с f-строкой уязвим точно так же.",
     hints=[
         "Подставь значение q из шагов Иры в f-строку и прочитай получившийся SQL.",
         "Что делает -- в SQL?",
         "Защищает не фильтрация ввода, а разделение кода запроса и данных.",
     ])

LIMITER_STARTER = C('''
class SlidingWindowLimiter:
    def __init__(self, limit, window):
        self.limit = limit      # сколько запросов можно
        self.window = window    # за сколько секунд
        self.hits = {}          # key -> время пропущенных запросов

    def allow(self, key, now):
        # твой код
        return True


def simulate(requests, limit, window):
    # requests — список [key, now] в порядке прихода; менять не нужно
    limiter = SlidingWindowLimiter(limit, window)
    return [limiter.allow(key, now) for key, now in requests]
''')

LIMITER_REF = C('''
class SlidingWindowLimiter:
    def __init__(self, limit, window):
        self.limit = limit      # сколько запросов можно
        self.window = window    # за сколько секунд
        self.hits = {}          # key -> время пропущенных запросов

    def allow(self, key, now):
        start = now - self.window
        # старые отметки выбрасываем — память на ключ не больше limit
        recent = [t for t in self.hits.get(key, []) if t > start]
        if len(recent) >= self.limit:
            self.hits[key] = recent
            return False
        recent.append(now)
        self.hits[key] = recent
        return True


def simulate(requests, limit, window):
    # requests — список [key, now] в порядке прихода; менять не нужно
    limiter = SlidingWindowLimiter(limit, window)
    return [limiter.allow(key, now) for key, now in requests]
''')

PHONE = "+79990001122"
task(T, 3, "write_code", 4, "devops_colleague",
     "Дима: «На /api/auth/sms-code кто-то всю ночь запрашивал коды — мы платим за каждую SMS, ушло 40 тысяч рублей. "
     "Нужен лимит: не больше N кодов на номер за окно. Только не фиксированное окно по минутам — на стыке минут "
     "пролезает двойная пачка»." ,
     "Допиши метод allow(key, now) класса SlidingWindowLimiter (скользящее окно по журналу запросов): верни True, если запрос "
     "с этим ключом можно пропустить, и False, если лимит исчерпан. В окно входят только пропущенные запросы со временем t, "
     "где now - window < t <= now; отклонённые запросы не засчитываются. now — секунды, передаётся аргументом и не убывает. "
     "Функция simulate уже написана — через неё идут тесты.",
     code=LIMITER_STARTER, language="python", entry="simulate", answer=LIMITER_REF,
     tests=[
         {"input": [[[PHONE, 0], [PHONE, 10], [PHONE, 20], [PHONE, 30]], 3, 60],
          "expected": [True, True, True, False]},
         {"input": [[["a", 0], ["a", 30], ["a", 59], ["a", 60], ["a", 61]], 2, 60],
          "expected": [True, True, False, True, False]},
         {"input": [[["a", 0], ["b", 0], ["a", 1], ["b", 1], ["a", 2]], 2, 10],
          "expected": [True, True, True, True, False]},
         {"input": [[["a", 0], ["a", 5], ["a", 6], ["a", 7], ["a", 10], ["a", 11]], 2, 10],
          "expected": [True, True, False, False, True, False]},
         {"input": [[["x", 0], ["x", 4], ["x", 5], ["x", 9], ["x", 10]], 1, 5],
          "expected": [True, False, True, False, True]},
     ],
     explanation="Скользящее окно по журналу хранит время каждого пропущенного запроса и на каждом вызове считает, сколько из них "
                 "попало в последние window секунд. В отличие от фиксированного окна, здесь нельзя отправить limit запросов в конце "
                 "одной минуты и ещё limit в начале следующей. Старые отметки выбрасываются при каждом вызове, поэтому на ключ "
                 "хранится не больше limit чисел. Отклонённые запросы не пишутся в журнал — иначе клиент, который продолжает "
                 "стучаться, никогда бы не разблокировался. В проде журнал живёт в Redis (sorted set: ZREMRANGEBYSCORE, ZCARD, ZADD "
                 "атомарно), чтобы лимит был общим для всех подов, а клиент получает 429 и Retry-After.",
     hints=[
         "Сначала отфильтруй из self.hits[key] отметки, которые уже вышли из окна: t > now - window.",
         "Если оставшихся отметок уже limit — верни False, ничего не добавляя.",
         "Иначе добавь now в список, сохрани его в self.hits[key] и верни True.",
     ])

task(T, 4, "code_review", 4, "teamlead",
     "Марина: «PR с выгрузкой счетов для личного кабинета. Автор торопится к релизу и просит „просто апрувнуть“. "
     "Отметь, что блокирует мердж»." ,
     "Выбери все замечания, которые действительно нужно исправить до мерджа.",
     code=C('''
import boto3
from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import text

router = APIRouter()

s3 = boto3.client(
    "s3",
    aws_access_key_id="AKIAIOSFODNN7EXAMPLE",
    aws_secret_access_key="wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
)


@router.get("/invoices")
def list_invoices(sort: str = "created_at", user=Depends(current_user), db=Depends(get_db)):
    rows = db.execute(
        text(f"SELECT id, number, total FROM invoices WHERE user_id = :uid ORDER BY {sort} DESC"),
        {"uid": user.id},
    )
    return rows.mappings().all()


@router.get("/invoices/{invoice_id}/pdf")
def invoice_pdf(invoice_id: int, user=Depends(current_user), db=Depends(get_db)):
    inv = db.execute(
        text("SELECT number, s3_key FROM invoices WHERE id = :id"), {"id": invoice_id}
    ).first()
    if inv is None:
        raise HTTPException(status_code=404)
    url = s3.generate_presigned_url(
        "get_object", Params={"Bucket": "lamp-invoices", "Key": inv.s3_key}, ExpiresIn=300
    )
    return {"number": inv.number, "url": url}
'''),
     language="python",
     options=[
         "Ключи AWS захардкожены и попадут в git и образ. Брать доступ из окружения или IAM-роли, а эти ключи отозвать",
         "sort подставляется в ORDER BY f-строкой — SQL-инъекция. Имя колонки параметром не передать, нужен белый список",
         "invoice_pdf не проверяет владельца: перебором id можно скачать чужие счета (IDOR). Нужно AND user_id = :uid",
         "Presigned URL на 300 секунд — уязвимость: ссылку могут переслать, файлы нужно отдавать только через наш сервер",
         "Вместо 404 для чужого счёта нужно отдавать 403, чтобы пользователь понимал, что счёт существует, но не его",
         "Сырой SQL через text() — всегда уязвимость, даже с параметрами: переписать все запросы на ORM",
     ],
     answer=[0, 1, 2],
     explanation="Ключи в коде навсегда остаются в истории git и в каждом собранном образе — их надо брать из окружения "
                 "или роли, а раз они уже в PR, считать утёкшими и перевыпустить. В ORDER BY параметр не работает (это имя "
                 "колонки, а не значение), поэтому f-строка тут — инъекция через sort, лечится словарём допустимых полей. "
                 "invoice_pdf ищет счёт только по id — классический IDOR, фильтр по user_id обязателен. Короткоживущая "
                 "presigned-ссылка — нормальная практика, она выдаётся только после проверки прав. 404 на чужой объект "
                 "правильнее 403: не раскрываем, что такой счёт есть. text() с параметрами (:uid) безопасен — опасна только "
                 "склейка строк.",
     hints=[
         "Посмотри на всё, что попадает в SQL не через :параметр.",
         "Чей счёт вернёт invoice_pdf, если подставить чужой invoice_id?",
         "Что произойдёт с ключами после git push?",
     ])

task(T, 5, "incident", 5, "devops_colleague",
     "Дима: «С двух ночи на /api/auth/login в 30 раз больше запросов, чем обычно, почти все 401. Двое клиентов уже написали, "
     "что с их аккаунтов оформили заказы на чужие адреса. Лимит по IP у нас стоит — 20 запросов в минуту, но он не срабатывает. "
     "Что делаем?»" ,
     "Что это за атака и что делать? Выбери все верные утверждения и действия.",
     code=C('''
# ingress, POST /api/auth/login, 02:00–02:10
requests: 184312   status 401: 181950   status 200: 2362
unique client IPs: 61874      max requests per IP per minute: 4
unique emails tried: 179004   max attempts per email: 2
top User-Agent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ..." (98%)

# market-api, выборка
02:03:11 INFO  login failed email=ivan.p***@mail.ru ip=185.220.x.x reason=bad_password
02:03:11 INFO  login failed email=olga.s***@yandex.ru ip=45.155.x.x reason=user_not_found
02:03:12 INFO  login ok email=a.smirnov***@gmail.com ip=103.47.x.x
02:03:40 INFO  order created user_id=88213 ip=103.47.x.x address_changed=true
'''),
     language="text",
     options=[
         "Это credential stuffing: пары логин-пароль из чужих утечек с десятков тысяч IP по 1–2 попытки — лимиты почти не срабатывают",
         "Сейчас: CAPTCHA на вход на время атаки; взломанным аккаунтам сбросить сессии и пароли, придержать их заказы",
         "Дальше: проверять пароли по базам утечек, предложить 2FA, настроить алерт на всплеск неудачных входов",
         "Снизить лимит по IP до 3 запросов в минуту и банить адрес на сутки после превышения — ботнет упрётся в лимит",
         "Возвращать разные ошибки — «пользователь не найден» и «неверный пароль», — чтобы честные клиенты быстрее понимали, что не так",
         "Заблокировать в iptables все IP из логов атаки по одному, начиная с самых активных",
     ],
     answer=[0, 1, 2],
     time_limit=20,
     explanation="Цифры выдают атаку: 180 тысяч разных email по одной-две попытки с 62 тысяч IP, а 2362 успешных входа — это "
                 "пароли, которые люди повторяли на других сайтах. Лимит по IP рассчитан на одного злоумышленника с одного адреса, "
                 "лимит по аккаунту — на перебор паролей к одному аккаунту, а здесь нагрузка размазана по обоим измерениям. "
                 "Поэтому сначала челлендж для всего входа и спасение уже взломанных аккаунтов, потом системные меры: проверка "
                 "по утечкам, 2FA, алерт на аномалии. Лимит в 3 запроса ударит по клиентам за общим NAT и почти не замедлит "
                 "ботнет, блокировать 62 тысячи меняющихся IP бесполезно, а разные тексты ошибок подарят атакующему список "
                 "зарегистрированных email.",
     hints=[
         "Посмотри на max requests per IP и max attempts per email. Почему лимит молчит?",
         "Откуда у атакующих пароли, которые подходят в 1,3% случаев?",
         "Нужны и срочные меры для уже взломанных аккаунтов, и защита на будущее.",
     ])


# =====================================================================
# be-observability — Логи и метрики
# =====================================================================
T = "be-observability"

topic(T, """
Наблюдаемость — способность по логам, метрикам и трейсам понять, что происходит в проде, не заходя на сервер. Логи отвечают на вопрос «что случилось с этим запросом», метрики — «насколько всё хорошо в целом», трейсы — «где именно запрос провёл время».

Структурные логи. Одна строка — один JSON-объект с постоянными полями: ts, level, msg, request_id и контекст (order_id, user_id, duration_ms). msg — неизменный текст («order paid»), переменные — в отдельных полях, иначе по ним не отфильтровать и не посчитать. Тогда в Loki или ELK легко найти все ERROR по order_id=1001 или запросы с duration_ms > 1000.

    logger.info("order paid", extra={"order_id": 1001, "amount": 3490})
    {"ts": "2026-09-24T10:15:00Z", "level": "INFO", "msg": "order paid", "order_id": 1001, "amount": 3490}

В Python это делает JSON-форматтер для logging или библиотека structlog. Никогда не логируй пароли, токены, заголовок Authorization, номера карт и тела запросов целиком — маскируй.

Уровни: DEBUG — подробности для отладки, в проде выключен; INFO — бизнес-события (заказ создан, оплата прошла); WARNING — странное, но обработанное (ретрай, деградация, медленный запрос); ERROR — запрос не выполнен по нашей вине, надо разбираться. 404 или ошибка валидации от клиента — не ERROR.

Correlation id (request id) связывает все строки одного запроса во всех сервисах:
• middleware берёт X-Request-ID из входящего заголовка или генерирует uuid4;
• кладёт его в contextvars.ContextVar, а не в глобальную переменную: в async-коде одновременно живут сотни запросов;
• добавляет во все логи, прокидывает в заголовки исходящих HTTP-запросов и сообщений очереди, возвращает в ответе.
Следующий шаг — распределённый трейсинг (OpenTelemetry, заголовок traceparent).

Метрики. Для сервисов — RED: Rate (запросов в секунду), Errors (доля ошибок), Duration (распределение времени ответа). Для ресурсов — USE: Utilization, Saturation, Errors. В Prometheus это counter http_requests_total{method, route, status} и histogram http_request_duration_seconds{method, route}. В лейблах — шаблон маршрута (/orders/{id}), а не реальный URL, и никогда user_id или order_id: каждое значение лейбла — отдельный временной ряд, и база метрик взорвётся.

Перцентили. Среднее прячет хвост: 98 быстрых ответов и 2 по 5 секунд дают «нормальное» среднее. p95 — значение, быстрее которого 95% запросов. Метод ближайшего ранга: отсортируй n значений и возьми элемент номер ceil(p * n / 100), считая с 1. Перцентили разных подов нельзя усреднять — агрегируют гистограммы (histogram_quantile в PromQL).

Алерты:
• на симптомы, которые чувствует пользователь: доля 5xx, p99 выше цели — а не CPU 80%;
• с окном (for: 5m), чтобы не будить из-за одного всплеска;
• каждый алерт требует действия и имеет runbook, остальное — на дашборд;
• цель задаёт SLO: например, 99.5% запросов без 5xx и p99 < 1.5 с.
""")

LOG_STARTER = C('''
import json


def log_line(ts, level, msg, request_id, fields):
    # сейчас так: "2026-09-24T10:15:00Z info order paid amount=3490 ..."
    parts = [f"{k}={v}" for k, v in fields.items()]
    return f"{ts} {level} {msg} " + " ".join(parts)
''')

LOG_REF = C('''
import json

SECRET_KEYS = {"password", "token", "authorization", "card_number"}


def log_line(ts, level, msg, request_id, fields):
    record = {"ts": ts, "level": level.upper(), "msg": msg}
    if request_id is not None:
        record["request_id"] = request_id
    for key in sorted(fields):
        value = fields[key]
        if key in SECRET_KEYS:
            value = "***"
        record[key] = value
    return json.dumps(record)
''')

LOG_ARGS = [
    ["2026-09-24T10:15:00Z", "info", "order paid", "req-7f3a", {"order_id": 1001, "amount": 3490, "user_id": 42}],
    ["2026-09-24T10:16:02Z", "warning", "login failed", "req-0b12", {"email": "ira@example.com", "password": "qwerty123"}],
    ["2026-09-24T10:17:45Z", "ERROR", "payment gateway timeout", None,
     {"order_id": 1002, "token": "eyJhbGciOiJIUzI1NiJ9", "retry": True, "gateway": "sbp"}],
    ["2026-09-24T10:18:00Z", "debug", "cache miss", "req-99", {}],
    ["2026-09-24T10:19:30Z", "Info", "checkout started", "req-c4d1",
     {"card_number": "4276 1600 0000 0000", "authorization": "Bearer abc.def", "coupon": None, "cart_total": 12990}],
]
LOG_EXP = run_ref(LOG_REF, "log_line", LOG_ARGS)
assert LOG_EXP[0] == ('{"ts": "2026-09-24T10:15:00Z", "level": "INFO", "msg": "order paid", "request_id": "req-7f3a", '
                      '"amount": 3490, "order_id": 1001, "user_id": 42}'), LOG_EXP[0]

task(T, 1, "write_code", 3, "devops_colleague",
     "Дима: «Переехали на Loki, а логи Маркета — сплошные строки вида „User 42 paid order 1001 in 350ms“: ни отфильтровать "
     "по заказу, ни посчитать. И вчера в лог улетел токен авторизации клиента. Давай нормальные JSON-логи»." ,
     "Напиши log_line(ts, level, msg, request_id, fields): собери словарь и верни json.dumps(словарь). Порядок ключей: "
     "ts, level (всегда в верхнем регистре), msg, request_id, затем поля из fields в алфавитном порядке. Если request_id "
     "равен None, ключа request_id нет. Значения полей password, token, authorization и card_number замени на \"***\".",
     code=LOG_STARTER, language="python", entry="log_line", answer=LOG_REF,
     tests=[{"input": a, "expected": e} for a, e in zip(LOG_ARGS, LOG_EXP)],
     explanation="JSON-строка с постоянными ключами превращает лог в данные: Loki или ELK разберут её сами, и можно искать "
                 "по order_id, строить графики по duration_ms и склеивать события по request_id. msg остаётся неизменным текстом, "
                 "а всё переменное уходит в поля — поэтому одинаковые события группируются. Секреты маскируются в самом форматтере, "
                 "а не «когда вспомнят»: один забытый токен в логах — это утечка, ведь логи читают многие и хранят долго. "
                 "Фиксированный порядок ключей удобен людям при чтении и делает вывод предсказуемым для тестов. В проде "
                 "то же самое делает logging.Formatter или structlog, а не ручная сборка строк.",
     hints=[
         "Собирай словарь по шагам: сначала ts, level, msg, потом request_id, потом поля — Python сохраняет порядок вставки.",
         "sorted(fields) даёт ключи словаря в алфавитном порядке.",
         "Для секретных ключей подставь \"***\" вместо значения, а в конце верни json.dumps(record).",
     ])

LAT_STARTER = C('''
import math


def latency_report(latencies_ms, slo_p99_ms):
    # сейчас на дашборде только среднее
    avg = round(sum(latencies_ms) / len(latencies_ms))
    return {"count": len(latencies_ms), "avg": avg}
''')

LAT_REF = C('''
import math


def percentile(sorted_values, p):
    # метод ближайшего ранга: элемент номер ceil(p * n / 100), нумерация с 1
    rank = math.ceil(p * len(sorted_values) / 100)
    return sorted_values[max(rank, 1) - 1]


def latency_report(latencies_ms, slo_p99_ms):
    n = len(latencies_ms)
    if n == 0:
        return {"count": 0, "avg": None, "p50": None, "p95": None, "p99": None, "slo_ok": None}
    values = sorted(latencies_ms)
    p99 = percentile(values, 99)
    return {
        "count": n,
        "avg": round(sum(values) / n),
        "p50": percentile(values, 50),
        "p95": percentile(values, 95),
        "p99": p99,
        "slo_ok": p99 <= slo_p99_ms,
    }
''')

LAT_20 = [120, 95, 110, 3400, 130, 105, 98, 101, 115, 140, 99, 102, 97, 125, 108, 2900, 111, 104, 100, 118]
LAT_100 = [80 + (i * 37) % 170 for i in range(96)]
for pos, v in ((13, 1200), (41, 4100), (67, 1850), (88, 2600)):
    LAT_100.insert(pos, v)
LAT_ARGS = [
    [LAT_20, 1000],
    [[200, 180, 220], 250],
    [LAT_100, 1500],
    [[42], 50],
    [[], 800],
]
for a in LAT_ARGS:
    if a[0]:
        s, n = sum(a[0]), len(a[0])
        assert (s * 2) % n != 0 or s % n == 0, a  # среднее не ровно x.5 (banker's rounding)
LAT_EXP = run_ref(LAT_REF, "latency_report", LAT_ARGS)
assert LAT_EXP[0]["p95"] == 2900 and LAT_EXP[0]["p99"] == 3400 and LAT_EXP[0]["p50"] == 108, LAT_EXP[0]
assert LAT_EXP[2]["p99"] == 2600 and LAT_EXP[2]["p95"] < 300, LAT_EXP[2]

task(T, 2, "write_code", 4, "manager",
     "Стас: «На дашборде средний ответ каталога 180 мс — красота. А в поддержку пишут, что каталог „думает“ по 3–4 секунды. "
     "Гена говорит, что среднее врёт и смотреть надо p95 и p99. Сделай отчёт, в котором видно правду»." ,
     "Напиши latency_report(latencies_ms, slo_p99_ms). Верни словарь: count — число значений, avg — среднее, округлённое "
     "через round до целого, p50, p95, p99 — перцентили методом ближайшего ранга (отсортируй значения и возьми элемент номер "
     "ceil(p * n / 100), нумерация с 1), slo_ok — True, если p99 <= slo_p99_ms. Для пустого списка: count = 0, остальные "
     "значения — None.",
     code=LAT_STARTER, language="python", entry="latency_report", answer=LAT_REF,
     tests=[{"input": a, "expected": e} for a, e in zip(LAT_ARGS, LAT_EXP)],
     explanation="Среднее складывает быстрые и медленные запросы в одно число, и два запроса по 3 секунды тонут среди "
                 "восемнадцати по 100 мс. Перцентиль отвечает на вопрос, который важен пользователю: «как долго ждут 1% самых "
                 "невезучих». Метод ближайшего ранга прост: отсортировать и взять элемент ceil(p * n / 100) — индекс в списке на "
                 "единицу меньше. В тесте на 100 значений видно, зачем нужен именно p99: p95 ещё в норме, а p99 уже 2,6 секунды "
                 "и нарушает SLO. Пустой список — частый случай ночью или на новом эндпоинте, и отчёт не должен падать с "
                 "ZeroDivisionError. В Prometheus то же считают по гистограммам через histogram_quantile, а перцентили разных "
                 "подов никогда не усредняют.",
     hints=[
         "Отсортируй значения один раз и вынеси расчёт перцентиля в отдельную функцию.",
         "Номер элемента — math.ceil(p * n / 100), индекс в списке — на единицу меньше.",
         "Пустой список обработай первым делом, до деления на n.",
     ])

task(T, 3, "code_review", 4, "devops_colleague",
     "Дима: «Разбирали вчерашний инцидент с оплатой и не смогли склеить логи Маркета и платёжного сервиса: request_id у них разные, а у части строк Маркета вообще чужой. Вот PR, который добавил request_id. Отметь, что нужно поправить»." ,
     "Выбери все замечания, которые действительно стоит оставить в ревью.",
     code=C('''
import logging
import uuid

import httpx
from fastapi import FastAPI, Request

app = FastAPI()
logger = logging.getLogger("market")
CURRENT_REQUEST_ID = None
payments = httpx.AsyncClient(base_url="http://payments:8000", timeout=3.0)


@app.middleware("http")
async def request_id_middleware(request: Request, call_next):
    global CURRENT_REQUEST_ID
    CURRENT_REQUEST_ID = str(uuid.uuid4())
    body = await request.body()
    logger.info("request started", extra={
        "request_id": CURRENT_REQUEST_ID,
        "path": request.url.path,
        "body": body.decode(),
    })
    response = await call_next(request)
    response.headers["X-Request-ID"] = CURRENT_REQUEST_ID
    return response


@app.post("/orders/{order_id}/pay")
async def pay(order_id: int, request: Request):
    payload = await request.json()
    resp = await payments.post("/charge", json={"order_id": order_id, "card": payload["card"]})
    logger.info("charge sent", extra={"request_id": CURRENT_REQUEST_ID, "status": resp.status_code})
    return resp.json()
'''),
     language="python",
     options=[
         "CURRENT_REQUEST_ID — глобальная переменная: пока один запрос ждёт await, другой её перезаписывает. Нужен ContextVar",
         "Входящий X-Request-ID игнорируется, а в payments id не передаётся — брать его из заголовка и прокидывать дальше",
         "В лог пишется всё тело запроса — туда попадут данные банковских карт. Тело целиком не логировать",
         "uuid4 генерируется медленно и нагружает CPU — лучше брать номер запроса из глобального счётчика",
         "logger.info нужно заменить на print: так логи точно попадут в stdout контейнера и не потеряются",
         "request_id лишний: строки одного запроса и так склеиваются по времени, пути и номеру пода в логе",
     ],
     answer=[0, 1, 2],
     explanation="В async-приложении в одном потоке одновременно живут сотни запросов, и глобальная переменная общая для всех: "
                 "между await запрос Б перезаписывает id запроса А — отсюда «чужие» request_id. ContextVar хранит значение "
                 "отдельно для каждой задачи asyncio. Correlation id полезен, только если путешествует вместе с запросом: "
                 "принимаем входящий заголовок от фронта или шлюза и передаём его дальше в payments, иначе у каждого сервиса "
                 "свой id. Тело запроса целиком — утечка данных карт в систему логов, куда у многих есть доступ. uuid4 "
                 "генерируется за микросекунды, а счётчик не уникален между подами; print теряет уровни и структуру; по "
                 "времени при сотнях RPS строки разных запросов неизбежно перемешаются.",
     hints=[
         "Что станет с CURRENT_REQUEST_ID, если два запроса обрабатываются одновременно?",
         "Как платёжный сервис узнает, какой request_id был у Маркета?",
         "Что лежит в теле запроса к /pay?",
     ])

task(T, 4, "architecture", 5, "devops_colleague",
     "Дима: «checkout-api выходит в прод. Сейчас на него один алерт — CPU > 80%, и прошлой ночью он молчал, пока 40% оплат "
     "падали с 502. Предложи, какие метрики сервис должен отдавать и на что будить дежурного. Ночью будить только по делу»." ,
     "Какой вариант метрик и алертов выбрать?",
     code=C('''
SLO checkout-api: 99.5% запросов без 5xx, p99 < 1.5 s (окно 30 дней)
Трафик: ~30 RPS ночью, до 300 RPS днём, 12 эндпоинтов
Сейчас: алерт node_cpu > 80% (5 мин) -> звонок дежурному
'''),
     language="text",
     options=[
         "Counter запросов и histogram длительности по route и status; будить по доле 5xx > 2% (5 мин) и p99 > 1.5 s (10 мин)",
         "То же, но добавить в лейблы метрик user_id и order_id, чтобы по алерту сразу видеть всех пострадавших клиентов",
         "Отправлять дежурному в Telegram каждую строку уровня ERROR из логов сервиса — так ничего не пропустим",
         "Gauge со средним временем ответа за минуту и звонок дежурному, если среднее больше 1.5 s дольше 5 минут",
         "Оставить алерт по CPU, снизив порог до 60%, и добавить такой же по памяти и числу рестартов пода",
     ],
     answer=0,
     explanation="RED-метрики описывают то, что чувствует клиент: сколько запросов, сколько из них с ошибкой и как долго они "
                 "идут. Алерты на долю 5xx и p99 привязаны к SLO и срабатывают на симптом — ровно то, что пропустил CPU-алерт: "
                 "при 502 от платёжки процессор отдыхает. Окна в 5–10 минут отсекают одиночные всплески, а на 30 RPS это тысячи "
                 "запросов — статистика надёжная. user_id и order_id в лейблах создают по временному ряду на каждого клиента "
                 "и кладут Prometheus — пострадавших ищут в логах по request_id. Алерт на каждую ERROR-строку — шум, к которому "
                 "дежурный быстро привыкнет, среднее прячет хвост, а CPU и память — причины, а не симптомы, им место на дашборде. "
                 "Следующий шаг зрелости — алерты по скорости сжигания бюджета ошибок SLO (burn rate).",
     hints=[
         "Что заметил бы клиент прошлой ночью, а что — CPU-алерт?",
         "Сколько временных рядов даст лейбл user_id?",
         "Ищи вариант с RED-метриками и алертами на симптомы, привязанными к SLO.",
     ])


# =====================================================================
# be-integration-tests — Интеграционные тесты
# =====================================================================
T = "be-integration-tests"

topic(T, """
Unit-тест проверяет функцию в изоляции, интеграционный — как код работает вместе с настоящей базой, миграциями и HTTP-слоем. Только он поймает кривой SQL, нарушенный constraint, забытый commit, неверную сериализацию ответа. Пирамида: много быстрых unit-тестов, меньше интеграционных, совсем немного e2e.

Тестовая база:
• та же СУБД и версия, что в проде (PostgreSQL 16), а не SQLite: у них разные типы, JSONB, ON CONFLICT, регистр в LIKE, поведение транзакций;
• поднимается в Docker: testcontainers-python прямо из фикстуры или service-контейнер в CI (GitHub Actions, GitLab CI);
• контейнер — один на всю сессию (scope="session"), схема — через alembic upgrade head, заодно проверяются миграции;
• никогда не гоняй тесты на общем staging и тем более на проде.

    @pytest.fixture(scope="session")
    def engine():
        with PostgresContainer("postgres:16-alpine") as pg:
            engine = create_engine(pg.get_connection_url())
            run_migrations(engine)
            yield engine

Фикстуры pytest: scope (function по умолчанию, module, session), yield для уборки после теста, общие — в conftest.py. Данные тест создаёт сам через фабрику make_order(**overrides): сразу видно, от чего зависит проверка.

Изоляция транзакцией. Каждый тест работает внутри транзакции, которую в конце откатывают: база снова чистая, и это быстрее TRUNCATE. В SQLAlchemy 2.0: connection.begin(), затем Session(bind=connection, join_transaction_mode="create_savepoint") — commit() в коде приложения фиксирует только savepoint, внешняя транзакция всё равно откатится. Важно: sequence (SERIAL/IDENTITY) ROLLBACK не откатывает, поэтому не завязывайся на конкретные id. Если код ходит в базу из других соединений или фоновых воркеров, откат не поможет — тогда TRUNCATE ... RESTART IDENTITY между тестами.

Тестовый клиент. FastAPI TestClient вызывает приложение прямо в процессе, без uvicorn. Через app.dependency_overrides[get_db] эндпоинты получают тестовую сессию. Для async-тестов — httpx.AsyncClient(transport=ASGITransport(app=app)). Внешние сервисы (платежи, SMS, почта) по-настоящему не вызываем: мокаем на границе (respx для httpx) или подменяем фейком.

Параллельный запуск (pytest-xdist -n auto): у каждого воркера своя база, имя строят из фикстуры worker_id.

Флаки-тесты — то зелёные, то красные. Частые причины:
• зависимость от порядка тестов и общих данных;
• time.sleep вместо ожидания условия или синхронного выполнения задачи;
• время и таймзоны: datetime.now(), полночь, конец месяца (замораживай время через time-machine или freezegun);
• ожидание порядка строк без ORDER BY;
• сеть и внешние сервисы.
Флак чинят, находя причину. Автоперезапуск (pytest-rerunfailures) — только временная мера с задачей в трекере, иначе тестам перестают верить.
""")

task(T, 1, "quiz", 3, "teamlead",
     "Марина: «Прежде чем пустить тебя в conftest.py, проверим основы. У нас PostgreSQL в контейнере на всю сессию тестов, "
     "каждый тест — в своей транзакции с откатом, запросы к API — через TestClient»." ,
     "Выбери все верные утверждения.",
     options=[
         "Контейнер с базой поднимают фикстурой со scope=\"session\": старт PostgreSQL занимает секунды, и делать это в каждом тесте слишком дорого",
         "Откат транзакции в конце теста возвращает базу в исходное состояние полностью, включая счётчики SERIAL/IDENTITY",
         "TestClient вызывает приложение прямо в процессе теста, поэтому запускать uvicorn для тестов не нужно",
         "Через app.dependency_overrides можно подменить get_db, чтобы эндпоинты работали в сессии с транзакцией теста",
         "Интеграционный тест обязан ходить и в настоящий платёжный шлюз — иначе это не интеграция",
         "Если тест иногда падает в CI, достаточно повесить на него автоперезапуск reruns=3 — это и есть исправление",
     ],
     answer=[0, 2, 3],
     explanation="Контейнер на сессию — компромисс между честностью (настоящий PostgreSQL) и скоростью: секунды на старт "
                 "платятся один раз. TestClient гоняет ASGI-приложение в том же процессе, а dependency_overrides подсовывает "
                 "эндпоинтам тестовую сессию — так откат теста покрывает и то, что записал API. Но sequence живут вне транзакций: "
                 "выданный nextval не возвращается при ROLLBACK, поэтому id от теста к тесту растут. Внешние сервисы в "
                 "интеграционных тестах мокают на границе — наша интеграция здесь с базой и HTTP-слоем, а реальный шлюз даст "
                 "медленные и нестабильные тесты. Автоперезапуск прячет флак, а не лечит его.",
     hints=[
         "Какие объекты PostgreSQL живут вне транзакций?",
         "С чем именно интегрируется код в таком тесте, а что лучше подменить?",
     ])

task(T, 2, "architecture", 4, "teamlead",
     "Гена: «Интеграционные тесты Маркета гоняются на SQLite в памяти — быстро, но вчера на проде упала миграция с JSONB "
     "и сломался INSERT ... ON CONFLICT, а тесты были зелёные. Есть ещё общий стенд staging, но там живут данные QA. "
     "CI-раннеры умеют Docker, на весь прогон — не больше 5 минут, тестов около 600»." ,
     "Как организовать тестовую базу?",
     options=[
         "PostgreSQL как в проде, в Docker: контейнер на сессию, схема через alembic upgrade head, каждый тест — в транзакции с откатом",
         "Оставить SQLite, а PostgreSQL-специфичные места (JSONB, ON CONFLICT) закрыть моками — тесты останутся быстрыми",
         "Гонять тесты на общей staging-базе: там настоящий PostgreSQL той же версии и реалистичные данные от QA",
         "Поднимать отдельный контейнер PostgreSQL на каждый тест — максимальная изоляция, и ни один тест не помешает другому",
         "Отказаться от интеграционных тестов и проверять всё unit-тестами с моком Session — это быстрее и стабильнее",
     ],
     answer=0,
     explanation="Баги были именно в местах, где SQLite ведёт себя иначе, чем PostgreSQL, поэтому тестовая база должна быть той же "
                 "СУБД и версии, что в проде. Один контейнер на сессию стоит несколько секунд, миграции через alembic заодно "
                 "проверяют сами миграции, а откат транзакции после каждого теста даёт изоляцию почти бесплатно — 600 тестов "
                 "укладываются в бюджет. Моки на месте JSONB и ON CONFLICT проверяют мок, а не SQL. Staging общий: тесты будут "
                 "мешать QA и друг другу, данные меняются — прямой путь к флакам. Контейнер на тест — это 600 × несколько секунд, "
                 "полчаса и больше. Unit-тесты с моком Session вообще не выполняют SQL и пропустят ровно такие баги.",
     hints=[
         "Какие баги пропустили текущие тесты и почему?",
         "Посчитай, сколько займёт старт контейнера для каждого из 600 тестов.",
     ])

task(T, 3, "code_review", 4, "qa",
     "Ира: «Тесты заказов в CI то зелёные, то красные — за неделю девять перезапусков пайплайна. У автора локально всё "
     "проходит. Посмотри, что тут делает их нестабильными»." ,
     "Выбери замечания, которые объясняют нестабильность этих тестов.",
     code=C('''
import time
from datetime import date

from fastapi.testclient import TestClient

from app.main import app

client = TestClient(app)


def test_create_order():
    resp = client.post("/orders", json={"product_id": 5, "qty": 2})
    assert resp.status_code == 201


def test_get_order():
    resp = client.get("/orders/1")  # заказ из test_create_order
    assert resp.json()["qty"] == 2


def test_order_confirmation_email_sent():
    order = client.post("/orders", json={"product_id": 5, "qty": 1}).json()
    time.sleep(2)  # ждём, пока фоновая задача отправит письмо
    assert client.get(f"/orders/{order['id']}").json()["email_sent"] is True


def test_orders_created_today():
    client.post("/orders", json={"product_id": 7, "qty": 1})
    resp = client.get(f"/orders?created_on={date.today()}")
    assert [o["product_id"] for o in resp.json()] == [5, 5, 7]
'''),
     language="python",
     options=[
         "test_get_order опирается на заказ из test_create_order и на id=1: при другом порядке или сдвинутой sequence он падает",
         "time.sleep(2) — гадание на времени: в загруженном CI фоновая задача не успевает. Выполнять её синхронно, письмо мокать",
         "test_orders_created_today видит чужие заказы, сравнивает без сортировки и зависит от date.today() около полуночи",
         "Вместо TestClient нужно слать requests на запущенный uvicorn — тесты будут ближе к проду и стабильнее",
         "Нужно повесить автоперезапуск reruns=3 на все тесты заказов — случайные падения CI перестанут мешать релизам",
         "assert resp.status_code == 201 неверен: POST в этом API всегда должен возвращать 200, как остальные ручки",
     ],
     answer=[0, 1, 2],
     explanation="Все три настоящих замечания — классические источники флаков. Тест, который опирается на данные другого теста "
                 "и на конкретный id, проходит только при удачном порядке запуска и на чистой базе. sleep подбирается на быстрой "
                 "машине автора и не выдерживает загруженного CI; надёжно — выполнить задачу синхронно или ждать условие с "
                 "таймаутом. Последний тест хрупок трижды: общие данные, порядок без ORDER BY и «сегодня», которое у теста и "
                 "сервера может отличаться. Настоящий uvicorn добавит сетевые проблемы, а не стабильность, reruns лишь прячет "
                 "причину, а 201 Created — правильный ответ на создание ресурса.",
     hints=[
         "Какой тест упадёт, если запустить его отдельно или в другом порядке?",
         "Почему у автора на ноутбуке 2 секунд хватает, а в CI — нет?",
         "Что ещё, кроме данных этого теста, вернёт GET /orders?created_on=...?",
     ])

task(T, 4, "estimation", 4, "manager",
     "Стас: «Гена сказал, что после вчерашнего фейла с миграцией тесты Маркета надо перевести с SQLite на настоящий "
     "PostgreSQL. Сколько это займёт? Мне надо понять, сдвигается ли релиз»." ,
     "Прежде чем назвать срок, выбери вопросы и риски, которые обязательно нужно выяснить.",
     options=[
         "Есть ли Docker на CI-раннерах (для testcontainers или service-контейнера) и сколько ресурсов им дают?",
         "Сколько сейчас тестов, сколько идёт прогон и какой бюджет времени на него в CI?",
         "Сколько тестов завязаны на поведение SQLite (типы, LIKE, конкретные id) и сломаются на PostgreSQL?",
         "Применяются ли в тестах миграции alembic или create_all — и проходят ли миграции на чистой базе?",
         "Какого цвета бейдж статуса тестов в README и нужно ли показывать там покрытие?",
         "Можно ли на время перехода отключить тесты в CI, чтобы они не мешали релизу?",
         "Какую IDE используют разработчики и нужно ли настроить в ней запуск тестов с Docker?",
     ],
     answer=[0, 1, 2, 3],
     time_limit=15,
     explanation="Срок определяют инфраструктура и объём поломок. Без Docker на раннерах подход меняется целиком. Число тестов и "
                 "бюджет времени решают, хватит ли одной базы с откатом транзакций или сразу нужен параллельный запуск с базой "
                 "на воркер. Главный риск — тесты, привязанные к особенностям SQLite: их починка может занять больше, чем вся "
                 "инфраструктура. Если миграции ни разу не применялись к чистой базе, они наверняка сломаются. Реалистичная "
                 "оценка: фикстуры с контейнером и миграциями — 1 день, изоляция и dependency_overrides — 1 день, починка "
                 "упавших тестов — от 1 до 3 дней, CI — полдня; итого 3–5 дней, и её можно вести параллельно релизу, не "
                 "отключая старые тесты.",
     hints=[
         "Какие ответы могут изменить объём работы в разы?",
         "Что может сломаться при переезде с SQLite на PostgreSQL, кроме самой инфраструктуры?",
     ])

task(T, 5, "find_bug", 5, "teamlead",
     "Марина: «test_create_order проходит, если запускать его одного, и падает в полном прогоне. Автор уже час смотрит "
     "на фикстуру, уверен, что изоляция сломана, и собирается переписать всё на TRUNCATE. Разберись, пока он не начал»." ,
     "В чём настоящая причина падения и как правильно исправить?",
     code=C('''
# conftest.py
@pytest.fixture
def db_session(engine):
    connection = engine.connect()
    outer = connection.begin()
    session = Session(bind=connection, join_transaction_mode="create_savepoint")
    app.dependency_overrides[get_db] = lambda: session
    yield session
    app.dependency_overrides.clear()
    session.close()
    outer.rollback()
    connection.close()


@pytest.fixture
def client(db_session):
    return TestClient(app)


# tests/test_orders.py
def test_create_order(client):
    resp = client.post("/orders", json={"product_id": 5, "qty": 2})
    assert resp.status_code == 201
    assert len(client.get("/orders").json()) == 1
    assert resp.json() == {"id": 1, "product_id": 5, "qty": 2, "status": "new"}

# $ pytest tests/test_orders.py::test_create_order  ->  1 passed
# $ pytest                                          ->  FAILED tests/test_orders.py::test_create_order
# E   AssertionError: assert {'id': 14, 'product_id': 5, 'qty': 2, 'status': 'new'} == {'id': 1, ...}
'''),
     language="python",
     options=[
         "Изоляция работает, но sequence не откатывается ROLLBACK — прошлые тесты израсходовали номера. Не проверять конкретный id",
         "Фикстуре db_session нужен scope=\"session\": с function-scope транзакции разных тестов не откатываются",
         "join_transaction_mode=\"create_savepoint\" ломает откат: commit() в эндпоинте фиксирует данные навсегда",
         "После каждого теста делать TRUNCATE orders: данные обнулятся, и тест снова будет видеть id = 1",
         "TestClient кэширует ответы между тестами, поэтому и id остаётся старым — нужно создавать новый клиент внутри каждого теста",
     ],
     answer=0,
     explanation="Строка с len(...) == 1 прошла, значит, данные предыдущих тестов не видны и откат работает. Упала только проверка "
                 "id: nextval() в PostgreSQL выдаёт номер вне транзакции и не возвращает его при ROLLBACK — так sequence "
                 "остаются быстрыми при конкурентной записи. Поэтому 13 заказов из прошлых тестов исчезли, а номера 1–13 — нет. "
                 "Правильный тест не завязан на конкретный id: сравниваем бизнес-поля и берём id из ответа. create_savepoint — "
                 "как раз рекомендованный режим SQLAlchemy 2.0 для таких фикстур, scope=\"session\" наоборот смешает данные "
                 "всех тестов, а простой TRUNCATE не сбрасывает sequence (для этого нужен RESTART IDENTITY) и сделает прогон "
                 "медленнее, не убрав хрупкость теста.",
     hints=[
         "Какая из трёх проверок прошла, а какая упала? Что это говорит об изоляции?",
         "Откатывает ли ROLLBACK значения, выданные nextval()?",
     ])


# ==== конец тем ====

def main():
    shuffle_options(TASKS)
    data = {"track": "backend", "part": "b5", "topics": TOPICS, "tasks": TASKS}
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("written", os.path.normpath(OUT), "topics", len(TOPICS), "tasks", len(TASKS))


if __name__ == "__main__":
    main()
