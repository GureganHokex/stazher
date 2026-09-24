# -*- coding: utf-8 -*-
"""Генератор контента «Стажёра»: track fullstack, part fs.

python3 Tools/Content/gen/fs.py  ->  Tools/Content/out/fullstack.json
"""
import json
import os

OUT = "Tools/Content/out/fullstack.json"
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
GRADE = "middle"

TOPICS = []
TASKS = []


def code(s):
    """Убирает первый перевод строки у тройных кавычек и гарантирует \n в конце."""
    if s is None:
        return None
    if s.startswith("\n"):
        s = s[1:]
    if not s.endswith("\n"):
        s += "\n"
    return s


def theory(topic_id, text):
    TOPICS.append({"topic_id": topic_id, "theory": text.strip("\n")})


def task(task_id, typ, difficulty, character, story, question, *, code_=None, language=None,
         options=None, answer=None, tests=None, entry=None, explanation="", hints=(), time_limit=None):
    topic_id = task_id.rsplit("-", 1)[0]
    content = {
        "question": question,
        "code": code(code_),
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content["options"] = options
    content["correct_answer"] = answer
    content["test_cases"] = tests
    TASKS.append({
        "task_id": task_id,
        "topic_id": topic_id,
        "grade": GRADE,
        "type": typ,
        "difficulty": difficulty,
        "xp_reward": int(round(XP_BASE[difficulty] * 1.5 / 5.0)) * 5,
        "time_limit_minutes": time_limit,
        "character": character,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": list(hints),
    })


def rx(desc, pattern, flags="im"):
    return {"input": desc, "expected": {"regex": pattern, "flags": flags}}


# =====================================================================================
# fs-contract — Контракт API между фронтом и бэком
# =====================================================================================
theory("fs-contract", r"""
Контракт API — это договор между фронтом и бэком: какие есть эндпоинты, какие поля в запросе и ответе, их типы, обязательность и формат. Пока контракт живёт в голове и в чате, он ломается при каждом рефакторинге. На уровне Middle контракт — это файл, который проверяют машины.

Источник правды — OpenAPI-схема. FastAPI строит её из Pydantic-моделей (/openapi.json), фронт генерирует из неё TypeScript-типы и клиент (openapi-typescript, orval), а CI сравнивает схему из PR со схемой из main:

    npx openapi-typescript ./openapi.json -o src/api/schema.d.ts
    oasdiff breaking openapi.main.json openapi.pr.json --fail-on ERR

Обратно совместимо (старый фронт, открытые вкладки и мобилка продолжают работать):
• новое необязательное поле в запросе или новое поле в ответе;
• новый эндпоинт;
• обязательное поле запроса стало необязательным.

Ломает клиентов:
• удалить или переименовать поле ответа — старый код прочитает undefined;
• сделать поле ответа необязательным или nullable — у клиента упадёт order.total.toFixed();
• новое обязательное поле в запросе — старый фронт его не пришлёт и получит 422;
• сменить тип (id: number → string) или смысл поля под тем же именем (рубли → копейки);
• новое значение enum в ответе, если клиент проверяет его строго (z.enum, switch с never).

Принцип терпимого читателя: клиент игнорирует незнакомые поля и не падает на новом значении enum, а показывает запасной вариант. Сервер строг к тому, что принимает, и не отнимает то, что уже отдавал.

Имена полей. Python живёт в snake_case, TypeScript — в camelCase. Решите один раз и зафиксируйте это в схеме. Хотите camelCase в JSON — делайте его на бэке, чтобы схема и реальность совпадали:

    class OrderIn(BaseModel):
        model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)
        delivery_address: str

populate_by_name (в Pydantic 2.11+ — validate_by_name) позволяет принимать и старые имена, пока клиенты переезжают. Худший вариант — самописный конвертер ключей в обёртке над fetch, о котором бэк не знает: заменили обёртку — сломали контракт.

Типы данных на стыке:
• деньги — целые копейки (price_minor) или десятичная строка, но не float;
• id больше 2^53 − 1 (9007199254740991) в JavaScript теряют точность — отдавай их строкой;
• даты — ISO 8601 со смещением: 2026-09-24T18:30:00+03:00;
• null и «поля нет» — разные вещи; договоритесь, что значит каждое, и проверяй на фронте через ?? и !== undefined, а не через ||.

TypeScript-типы исчезают после сборки: если бэк прислал не то, TS не спасёт. На важных границах проверяй ответ в рантайме (zod, valibot) и отправляй расхождения в Sentry — так поломку контракта увидишь раньше жалоб.

Версионирование. Большинство изменений делай аддитивно: новое поле рядом со старым, старое помечай deprecated и удаляй, когда им никто не пользуется (смотри метрики по версиям клиентов). /api/v2 — крайняя мера, когда меняется сама модель: две версии придётся поддерживать месяцами.
""")

task(
    "fs-contract-01", "quiz", 4, "teamlead",
    "Марина: «У трети пользователей мобильное приложение Маркета трёхмесячной давности, и заставить их обновиться нельзя. "
    "Бэкенд-команда хочет „причесать“ API заказов одним PR. Прежде чем я его одобрю, скажи, какие пункты сломают уже выпущенные клиенты».",
    "Какие изменения API заказов сломают уже выпущенные фронт и мобилку? Выбери все верные.",
    options=[
        "Добавить в ответ GET /orders/{id} новое поле delivery_slot",
        "Переименовать поле ответа total в total_amount",
        "Добавить в POST /orders новое обязательное поле delivery_slot",
        "Сделать необязательным поле запроса comment, которое раньше было обязательным",
        "Отдавать id заказа строкой \"1842\" вместо числа 1842",
        "Добавить в ответ новое значение \"courier\" в enum status, если клиент проверяет ответ через z.enum([...]) без запасного варианта",
    ],
    answer=[1, 2, 4, 5],
    explanation="Старый клиент знает только прежний контракт. Переименованное поле он прочитает как undefined, новое обязательное поле "
    "в запросе не пришлёт и получит 422, а id-строку будет сравнивать с числом (\"1842\" === 1842 — false) и ломать на ней арифметику. "
    "Новое значение enum роняет клиента, который проверяет ответ строго: z.enum выбросит ошибку на \"courier\". "
    "Новое поле в ответе и ослабленное требование к запросу безопасны — старый код лишнего не заметит. "
    "Поэтому переименование делают как «добавить новое поле рядом, дождаться, пока старые клиенты уйдут, удалить старое», "
    "а клиенты пишут терпимыми к новым значениям enum.",
    hints=[
        "Представь приложение, код которого уже нельзя поменять: что оно отправляет и что ожидает прочитать?",
        "Добавить новое обычно безопасно, если это не новое требование к старому клиенту.",
        "Строгая валидация на клиенте превращает новое значение enum в ошибку.",
    ],
)

task(
    "fs-contract-02", "find_bug", 4, "qa",
    "Ира: «Идёт раскатка нового бэка каталога. У подарочного брелка то „0 ₽“, то „NaN ₽“, а у люстры под заказ — „NaN ₽“ или „0 ₽“, "
    "но никогда „Цена по запросу“. Зависит от того, на какой под попал запрос».",
    "Бэк переезжает с price (рубли, число или null) на price_minor (копейки, целое или null — «цена по запросу»). Во время rolling-деплоя "
    "фронт получает ответы и от старой, и от новой версии, а новая уже не отдаёт price. Исправь normalizeProduct: priceMinor — целое число "
    "копеек или null, 0 — нормальная цена; если пришли оба поля, главнее price_minor.",
    code_=r"""
// Приводит ответ любой версии бэка к модели фронта
function normalizeProduct(raw) {
  return {
    id: String(raw.id),
    title: raw.title || raw.name,
    priceMinor: raw.price_minor || Math.round(raw.price * 100),
  };
}
""",
    language="javascript", entry="normalizeProduct",
    answer=code(r"""
// Приводит ответ любой версии бэка к модели фронта
function normalizeProduct(raw) {
  let priceMinor = null;
  if (raw.price_minor !== undefined) {
    // новая версия: копейки, null значит «цена по запросу»
    priceMinor = raw.price_minor;
  } else if (raw.price !== undefined && raw.price !== null) {
    // старая версия: рубли во float — переводим в копейки с округлением
    priceMinor = Math.round(raw.price * 100);
  }
  return {
    id: String(raw.id),
    title: raw.title ?? raw.name,
    priceMinor,
  };
}
"""),
    tests=[
        {"input": [{"id": 1, "title": "Лампа Эдисона", "price_minor": 149000}],
         "expected": {"id": "1", "title": "Лампа Эдисона", "priceMinor": 149000}},
        {"input": [{"id": 2, "name": "Кабель USB-C", "price": 14.9}],
         "expected": {"id": "2", "title": "Кабель USB-C", "priceMinor": 1490}},
        {"input": [{"id": 3, "title": "Брелок в подарок", "price_minor": 0}],
         "expected": {"id": "3", "title": "Брелок в подарок", "priceMinor": 0}},
        {"input": [{"id": 4, "name": "Люстра под заказ", "price": None}],
         "expected": {"id": "4", "title": "Люстра под заказ", "priceMinor": None}},
        {"input": [{"id": 5, "title": "Бра", "price_minor": 99000, "price": 990.0}],
         "expected": {"id": "5", "title": "Бра", "priceMinor": 99000}},
    ],
    explanation="Оператор || проверяет не «есть ли значение», а «истинно ли оно»: 0 и null ложны, поэтому бесплатный брелок проваливается "
    "во вторую ветку, где price уже нет, и Math.round(undefined * 100) даёт NaN. А null * 100 в JavaScript равно 0 — так «цена по запросу» "
    "превращается в 0 ₽. Нужно различать «поля нет» (ответила старая версия) и «поле есть, но null» (новая версия сказала, что цены нет): "
    "проверка !== undefined, а для подстановки по умолчанию — оператор ??. Такой адаптер — нормальная практика терпимого читателя на время "
    "миграции; его удаляют, когда старых версий бэка не осталось.",
    hints=[
        "Чему равно 0 || 5? А null * 100?",
        "Отличай «поля нет в ответе» от «поле есть и равно null или 0».",
        "Проверь raw.price_minor !== undefined, а у старого price отдельно обработай null.",
    ],
)

task(
    "fs-contract-03", "write_code", 4, "teamlead",
    "Гена: «Второй раз за месяц бэк выкатил изменение, от которого упала мобилка. Хочу проверку в CI: берём схему из main и из PR "
    "и перечисляем ломающие изменения. Настоящий oasdiff прикрутим позже, а сейчас напиши ядро на упрощённой схеме».",
    "Напиши breaking_changes(old, new). Схема: {\"request\": {поле: {\"type\": str, \"required\": bool}}, \"response\": {...}}, любой из двух "
    "ключей может отсутствовать. Ломающие изменения:\n"
    "• request: новое обязательное поле → \"request.<поле>: new required\"; было необязательным, стало обязательным → "
    "\"request.<поле>: now required\"; сменился тип → \"request.<поле>: type <старый> -> <новый>\";\n"
    "• response: поле удалено → \"response.<поле>: removed\"; было обязательным, стало необязательным → \"response.<поле>: now optional\"; "
    "сменился тип → \"response.<поле>: type <старый> -> <новый>\".\n"
    "Тип и обязательность проверяются независимо. Остальное (новые необязательные поля запроса, новые поля ответа, удалённые поля запроса) "
    "не ломает. Верни отсортированный список строк.",
    code_=r"""
def breaking_changes(old, new):
    # old, new: {"request": {"поле": {"type": "string", "required": True}}, "response": {...}}
    problems = []
    # твой код
    return sorted(problems)
""",
    language="python", entry="breaking_changes",
    answer=code(r"""
def breaking_changes(old, new):
    problems = []

    # Запрос пишет старый клиент: опасно всё, что требует от него больше
    old_req = old.get("request", {})
    new_req = new.get("request", {})
    for name, field in new_req.items():
        before = old_req.get(name)
        if before is None:
            if field["required"]:
                problems.append(f"request.{name}: new required")
            continue
        if field["type"] != before["type"]:
            was, now = before["type"], field["type"]
            problems.append(f"request.{name}: type {was} -> {now}")
        if field["required"] and not before["required"]:
            problems.append(f"request.{name}: now required")

    # Ответ читает старый клиент: опасно всё, что у него отнимают
    old_resp = old.get("response", {})
    new_resp = new.get("response", {})
    for name, before in old_resp.items():
        after = new_resp.get(name)
        if after is None:
            problems.append(f"response.{name}: removed")
            continue
        if after["type"] != before["type"]:
            was, now = before["type"], after["type"]
            problems.append(f"response.{name}: type {was} -> {now}")
        if before["required"] and not after["required"]:
            problems.append(f"response.{name}: now optional")

    return sorted(problems)
"""),
    tests=[
        {"input": [
            {"request": {"items": {"type": "array", "required": True}},
             "response": {"id": {"type": "integer", "required": True}}},
            {"request": {"items": {"type": "array", "required": True}},
             "response": {"id": {"type": "integer", "required": True}}},
        ], "expected": []},
        {"input": [
            {"request": {"items": {"type": "array", "required": True},
                         "comment": {"type": "string", "required": False}},
             "response": {"id": {"type": "integer", "required": True},
                          "total": {"type": "number", "required": True},
                          "status": {"type": "string", "required": True}}},
            {"request": {"items": {"type": "array", "required": True},
                         "comment": {"type": "string", "required": True},
                         "delivery_slot": {"type": "string", "required": True},
                         "promo_code": {"type": "string", "required": False}},
             "response": {"id": {"type": "string", "required": True},
                          "status": {"type": "string", "required": False},
                          "total_minor": {"type": "integer", "required": True}}},
        ], "expected": [
            "request.comment: now required",
            "request.delivery_slot: new required",
            "response.id: type integer -> string",
            "response.status: now optional",
            "response.total: removed",
        ]},
        {"input": [
            {"request": {"email": {"type": "string", "required": True},
                         "phone": {"type": "string", "required": True}},
             "response": {"id": {"type": "integer", "required": True}}},
            {"request": {"email": {"type": "string", "required": True},
                         "phone": {"type": "string", "required": False},
                         "utm_source": {"type": "string", "required": False}},
             "response": {"id": {"type": "integer", "required": True},
                          "created_at": {"type": "string", "required": True}}},
        ], "expected": []},
        {"input": [
            {"request": {"qty": {"type": "integer", "required": True},
                         "gift_wrap": {"type": "boolean", "required": False}},
             "response": {"name": {"type": "string", "required": True},
                          "price": {"type": "number", "required": True}}},
            {"request": {"qty": {"type": "string", "required": True}},
             "response": {"name": {"type": "string", "required": True}}},
        ], "expected": ["request.qty: type integer -> string", "response.price: removed"]},
        {"input": [
            {"response": {"discount": {"type": "integer", "required": True}}},
            {"response": {"discount": {"type": "number", "required": False}}},
        ], "expected": ["response.discount: now optional", "response.discount: type integer -> number"]},
    ],
    explanation="Логика несимметрична, и в этом суть контракта. Запрос пишет клиент, а проверяет сервер: опасно всё, что требует от старого "
    "клиента больше, чем он умеет, — новые обязательные поля и смена типа. Ответ пишет сервер, а читает клиент: опасно всё, что отнимает "
    "ожидаемое, — удаление поля, «может не прийти» вместо «есть всегда», другой тип. Поэтому в запросе идём по полям новой схемы, а в ответе — "
    "по полям старой. Новое поле ответа безопасно, потому что клиенты игнорируют незнакомое. В настоящем CI то же делает oasdiff breaking "
    "на полной OpenAPI-схеме, и PR с ломающим изменением не мержится без явного решения команды.",
    hints=[
        "Раздели проверку на две части: для request перебирай поля новой схемы, для response — поля старой.",
        "Для request опасно то, что требует больше от клиента; для response — то, что клиенту больше не гарантируется.",
        "old.get(\"request\", {}) спасёт от отсутствующего ключа; в конце верни sorted(problems).",
    ],
)

task(
    "fs-contract-04", "code_review", 5, "teamlead",
    "Марина: «PR от бэкенд-команды: „цены в копейках и camelCase, как просил фронт“. Фронт в том же PR поправил свои типы руками. "
    "Мобилка и открытые вкладки старого фронта никуда не делись. Отметь, что должно остановить мерж».",
    "Выбери все замечания, которые действительно нужно оставить в ревью.",
    code_=r"""
--- a/backend/app/models.py  (без изменений, для контекста)
class Order(Base):
    total: Mapped[float] = mapped_column(Float)            # рубли

--- a/backend/app/schemas/order.py
+++ b/backend/app/schemas/order.py
 class OrderOut(BaseModel):
+    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)
+
     id: int
-    total: float
+    total: int                                            # теперь копейки
     created_at: datetime
+    promo_code: str | None

--- a/backend/app/api/orders.py
+++ b/backend/app/api/orders.py
 @router.get("/orders/{order_id}", response_model=OrderOut)
 async def get_order(order_id: int, user: User = Depends(current_user), db=Depends(get_db)):
     order = await orders_repo.get_for_user(db, order_id, user.id)
-    return order
+    return OrderOut(
+        id=order.id,
+        total=int(order.total * 100),
+        created_at=order.created_at,
+        promo_code=order.promo_code,
+    )

--- a/frontend/src/api/types.ts
+++ b/frontend/src/api/types.ts
 export interface Order {
   id: number;
-  total: number;
-  created_at: string;
+  total: number; // копейки
+  createdAt: string;
+  promoCode?: string;
 }
""",
    language="text",
    options=[
        "total сменил смысл при том же имени: был в рублях, стал в копейках — старые клиенты покажут цену ×100, нужно новое поле",
        "OrderOut лучше переименовать в OrderResponse — по гайдлайну команды схемы ответа называются с суффиксом Response",
        "int(order.total * 100) на float теряет копейки: int(19.99 * 100) даст 1998 — нужны Decimal и округление",
        "alias_generator=to_camel переименует в JSON все поля: created_at станет createdAt и сломает всех, кто читает snake_case",
        "Для денег надёжнее оставить float: копейки усложнят фронт, а округлить до двух знаков можно при выводе",
        "Типы фронта написаны руками и уже разошлись со схемой (promo_code nullable, в TS — promoCode?) — генерировать из OpenAPI",
        "Поле id стоит убрать из ответа — клиент и так знает его из URL, а лишние байты на мобилке дороги",
    ],
    answer=[0, 2, 3, 5],
    explanation="Самое опасное здесь — изменения, которые проходят тесты фронта из этого же PR, но ломают всех остальных клиентов. "
    "total с новым смыслом под старым именем — худший вид ломающего изменения: ошибки нет, просто цена в 100 раз больше. "
    "alias_generator меняет имена всех полей ответа разом, а старые вкладки и мобилка читают created_at. float для денег даёт 1998 копеек "
    "вместо 1999 — классика, из-за которой не сходятся отчёты. Рукописные типы уже разошлись со схемой (необязательное против nullable), "
    "и TS молча пропустит null. Правильный путь: total_minor рядом со старым полем, camelCase — только через новую версию схемы или никогда, "
    "типы — генерацией из OpenAPI и проверка дифа схемы в CI. Переименование класса — вкусовщина, а убрать id — вредный совет.",
    hints=[
        "Кто ещё, кроме фронта из этого PR, читает этот ответ?",
        "Посчитай: что даст int(19.99 * 100)?",
        "Сравни TS-тип promoCode с тем, что Pydantic запишет в OpenAPI для str | None без значения по умолчанию.",
    ],
)

task(
    "fs-contract-05", "incident", 5, "devops_colleague",
    "Дима: «С 14:05 оформление заказа не работает у всех: кнопка „Оформить“ показывает „Что-то пошло не так“. Бэк со вчера не деплоили, "
    "база в порядке. В 14:00 выкатывали фронт. Логи ниже».",
    "Что сломалось и что делаешь в первую очередь?",
    code_=r"""
$ kubectl logs deploy/market-api --since=20m | grep '"POST /api/orders"' | tail -3
14:06:12 INFO rid=7f3a1c "POST /api/orders" 422 3ms
14:06:15 INFO rid=9b20e4 "POST /api/orders" 422 2ms
14:06:19 INFO rid=c41d77 "POST /api/orders" 422 3ms

# тело запроса скопировано из DevTools и повторено через curl
$ curl -s https://market.example/api/orders -H 'Content-Type: application/json' \
    -b 'session=...' -d @body.json | jq -c '.detail[]'
{"type":"missing","loc":["body","delivery_address"],"msg":"Field required","input":{"deliveryAddress":"ул. Ленина, 5","paymentMethod":"card","items":[{"productId":812,"qty":1}]}}
{"type":"missing","loc":["body","payment_method"],"msg":"Field required","input":{"deliveryAddress":"ул. Ленина, 5","paymentMethod":"card","items":[{"productId":812,"qty":1}]}}
{"type":"missing","loc":["body","items",0,"product_id"],"msg":"Field required","input":{"productId":812,"qty":1}}

$ git -C frontend log --oneline -2
a91c0de refactor(api): новый http-клиент на fetch вместо старой обёртки над axios
5e02b17 fix(cart): отступы в мини-корзине
""",
    language="text",
    options=[
        "Бэк завис после ночной нагрузки — перезапустить поды market-api, а если не поможет, откатить бэк на позавчерашний релиз",
        "Срочно выкатить бэк с alias_generator=to_camel в модели заказа: фронт уже шлёт camelCase, а выкатка бэка займёт столько же, сколько откат фронта",
        "nginx обрезает тело запроса после перехода на fetch — поднять client_max_body_size и proxy_buffer_size на ингрессе",
        "Фронт стал слать ключи в camelCase (старая обёртка переводила их в snake_case) — откатить фронт, потом генерировать клиент из OpenAPI и добавить contract-тест в CI",
        "Новый клиент на fetch не отправляет куки — сломался CORS. Добавить на бэке Access-Control-Allow-Origin: * и на фронте credentials: \"include\"",
    ],
    answer=3,
    explanation="422 с type missing и loc [\"body\", \"delivery_address\"] — это Pydantic: обязательного поля нет. А в input видно, что оно "
    "пришло как deliveryAddress. Бэк не менялся, значит, контракт сломал фронт: старая обёртка над axios незаметно переводила ключи тела "
    "в snake_case, а новый клиент этого не делает. Первое действие — откат фронта: это минуты и безопасно. Хотфикс бэка во время инцидента "
    "рискован: alias_generator без populate_by_name сломает мобилку, которая шлёт snake_case. После инцидента — клиент, сгенерированный "
    "из OpenAPI, чтобы имена полей брались из схемы, и contract-тест в CI. nginx, CORS и перезапуск подов ни при чём: запрос дошёл до бэка "
    "и был осознанно отклонён.",
    hints=[
        "Сравни loc в ошибке с ключами в input — как они написаны?",
        "Что менялось в 14:00 и какой откат самый быстрый и безопасный?",
    ],
    time_limit=15,
)


# =====================================================================================
# fs-auth-e2e — Авторизация через весь стек
# =====================================================================================
theory("fs-auth-e2e", r"""
Логин — это цепочка через все слои: форма → HTTPS → nginx → бэк → база или Redis → Set-Cookie → браузер → каждый следующий запрос. Ломается она обычно на стыках.

Две рабочие схемы:
• Сессия на сервере. Бэк проверяет пароль (хеш argon2id или bcrypt), создаёт запись в sessions (Postgres или Redis) и отдаёт куку session_id с флагами HttpOnly; Secure; SameSite=Lax. «Выйти везде» — удалить записи. Минус — поход в хранилище сессий на каждый запрос.
• Access + refresh. Короткий access-JWT (5–15 минут) проверяется без базы. Длинный refresh лежит в HttpOnly-куке с узким Path=/api/auth/refresh, а в базе хранится только его хеш. При каждом обновлении refresh ротируется: выдаём новый, старый гасим.

Ротация с детектом повтора: если пришёл уже использованный refresh, его украли (или клиент глючит) — гасим всю сессию. Отсюда главный фронтовый баг: пять параллельных запросов получили 401 и запустили пять refresh; первый прошёл, остальные выглядят как повтор, и пользователя выкидывает. Лечится single-flight: одно обновление на всех, остальные ждут тот же промис.

    let refreshing = null;
    function refreshOnce() {
      if (!refreshing) refreshing = doRefresh().finally(() => { refreshing = null; });
      return refreshing;
    }

JWT нельзя отозвать — он валиден до exp. Поэтому «выйти со всех устройств» делают так: у пользователя в базе token_version (или список сессий); refresh сверяет версию и отказывает, а access доживает свои минуты. Для опасных действий (смена пароля, оплата, смена адреса) версию сверяют и на access — через кэш в Redis.

Кука и инфраструктура:
• Path и Domain считаются от публичного URL, который видит браузер. Если nginx срезает префикс /api, бэк думает, что живёт на /auth/refresh, — задай Path в конфиге или поправь его через proxy_cookie_path.
• Secure-кука и https-ссылки: за прокси бэк узнаёт схему из X-Forwarded-Proto (uvicorn --proxy-headers --forwarded-allow-ips).
• Несколько реплик — сессии не в памяти процесса, а в Redis или базе; секрет подписи JWT одинаковый у всех подов и хранится в секретах, а не в коде.
• Фронт и API на разных доменах — нужны CORS с credentials и SameSite=None; Secure. Проще держать API на том же домене под /api.

CSRF. Если авторизация в куках, браузер приложит их и к запросу с чужого сайта. SameSite=Lax закрывает кросс-сайтовые POST, но поддомены того же сайта считаются «своими». Надёжнее CSRF-токен (double submit: кука + заголовок X-CSRF-Token) или обязательный кастомный заголовок, который чужая страница без CORS не отправит. Токен в заголовке Authorization CSRF не боится, зато его может украсть XSS — поэтому refresh держат только в HttpOnly-куке.

Выход — это не «удалить куку на фронте»: сервер обязан отозвать refresh или сессию, иначе украденный токен продолжит работать. Сообщения об ошибке входа одинаковые для «нет такого email» и «неверный пароль», а попытки входа ограничиваются rate limit по IP и аккаунту.
""")

task(
    "fs-auth-e2e-01", "estimation", 4, "manager",
    "Стас: «После истории с угнанными аккаунтами директор хочет двухфакторку для входа в Маркет, коды по SMS. Сколько по времени? "
    "Мне к пятнице нужна цифра».",
    "Прежде чем называть срок, выбери вопросы и риски, которые сильнее всего меняют объём работы.",
    options=[
        "Обязательна ли двухфакторка для всех и что делать с текущими сессиями и мобилкой без экрана ввода кода?",
        "Какой SMS-провайдер, сколько стоит SMS и как защищаемся от SMS-флуда (лимиты на номер и IP, капча)?",
        "Как восстановить вход, если телефон потерян: резервные коды, поддержка, проверка личности?",
        "Какого цвета будет поле ввода кода и нужна ли маска с шестью отдельными ячейками, как в банках?",
        "Нужно ли „запомнить это устройство на 30 дней“, чтобы не спрашивать код при каждом входе?",
        "Нужна ли анимация у таймера „отправить код ещё раз“ и какой шрифт у цифр обратного отсчёта?",
        "Можно ли написать сервис кодов на Go вместо Python — говорят, он быстрее отправляет SMS?",
    ],
    answer=[0, 1, 2, 4],
    explanation="Двухфакторка задевает весь стек, и каждый из четырёх вопросов добавляет или убирает целые куски. Обязательность и мобилка "
    "решают, нужен ли релиз приложения и принудительный перевыпуск сессий. Провайдер — это интеграция, договор, деньги и защита: без лимитов "
    "на номер и IP бот разорит вас на SMS за ночь. Восстановление доступа — отдельный процесс с поддержкой, о котором забывают, а доверенные "
    "устройства — ещё таблица и кука. Разумная декомпозиция: бэк (хеш кода, TTL 5 минут, лимит попыток, rate limit, отправка через очередь "
    "с ретраями, аудит) — 4–5 дней, фронт (шаг ввода, таймер, autocomplete=\"one-time-code\", ошибки) — 2–3 дня, инфраструктура и выкладка "
    "за флагом — 1–2 дня; итого 2–3 недели с тестированием, мобилка отдельно. Стоит предложить и TOTP-приложение: оно бесплатное и не боится "
    "перевыпуска SIM-карты.",
    hints=[
        "Какие ответы добавят целые новые куски работы: релиз мобилки, интеграцию, процесс в поддержке?",
        "Подумай о деньгах и злоупотреблениях: кто заплатит за SMS, которые отправит бот?",
    ],
    time_limit=15,
)

task(
    "fs-auth-e2e-02", "code_review", 4, "teamlead",
    "Марина: «Джун переписал вход на access + refresh. Фронт держит access в памяти вкладки, refresh живёт в куке. Тесты зелёные, "
    "логин работает. Посмотри глазами безопасника: что не пропустим?»",
    "Выбери все замечания, которые нужно исправить до мержа.",
    code_=r"""
@router.post("/auth/login")
async def login(data: LoginIn, response: Response, db: AsyncSession = Depends(get_db)):
    user = await users_repo.by_email(db, data.email)
    if user is None or not verify_password(data.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Неверный email или пароль")

    access = create_jwt({"sub": str(user.id)}, ttl=timedelta(minutes=15))
    refresh = secrets.token_urlsafe(32)
    await sessions_repo.create(db, user_id=user.id, refresh_token=refresh,
                               expires_at=utcnow() + timedelta(days=30))

    response.set_cookie("refresh_token", refresh, max_age=30 * 24 * 3600,
                        httponly=False, secure=True, samesite="lax")
    return {"access_token": access, "user": {"id": user.id, "email": user.email}}


@router.post("/auth/refresh")
async def refresh(request: Request, db: AsyncSession = Depends(get_db)):
    token = request.cookies.get("refresh_token")
    session = await sessions_repo.by_refresh_token(db, token)
    if session is None or session.expires_at < utcnow():
        raise HTTPException(status_code=401)
    access = create_jwt({"sub": str(session.user_id)}, ttl=timedelta(minutes=15))
    return {"access_token": access}


@router.post("/auth/logout")
async def logout(response: Response):
    response.delete_cookie("refresh_token")
    return {"ok": True}
""",
    language="python",
    options=[
        "Кука refresh_token без HttpOnly: любой XSS прочитает 30-дневный токен — нужны httponly=True и узкий path",
        "При неизвестном email отвечать 404, а при неверном пароле — 401: пользователю будет понятнее, что исправлять",
        "Refresh-токен лежит в базе как есть: утечка дампа или бэкапа — готовые токены; хранить sha256 и искать по хешу",
        "Refresh не ротируется: один токен живёт 30 дней, кражу не заметить — выдавать новый и ловить повторное использование",
        "Access на 15 минут слишком короткий — сделать 30 дней, чтобы реже дёргать refresh и меньше нагружать базу",
        "logout только удаляет куку в браузере, а сессия в базе жива — украденный refresh продолжит работать",
        "secrets.token_urlsafe(32) предсказуем при высокой нагрузке — лучше генерировать refresh через str(uuid4())",
    ],
    answer=[0, 2, 3, 5],
    explanation="Каждое из четырёх замечаний закрывает свой путь атаки. Без HttpOnly refresh-токен достаётся любому внедрённому скрипту, "
    "а ведь ради защиты от XSS его и кладут в куку. Хранение в открытом виде превращает утечку базы или бэкапа в доступ ко всем аккаунтам; "
    "для случайного 256-битного токена достаточно sha256 (медленный bcrypt нужен паролям, а не случайным токенам). Без ротации кражу не "
    "обнаружить, а с ротацией повтор старого токена — сигнал погасить сессию. Выход, который не трогает сервер, — косметика. "
    "Разные ответы для «нет email» и «неверный пароль» позволяют перебором узнать, кто зарегистрирован; 30-дневный access невозможно отозвать; "
    "token_urlsafe(32) — 256 бит из криптостойкого генератора, uuid4 лучше не станет.",
    hints=[
        "Для каждой строки спроси: что получит атакующий, если украдёт куку, дамп базы или бэкап?",
        "Что происходит на сервере при logout и при refresh?",
    ],
)

task(
    "fs-auth-e2e-03", "find_bug", 4, "qa",
    "Ира: «Шаги: логинюсь, ухожу пить кофе на 20 минут, возвращаюсь, открываю дашборд заказов — меня выкидывает на логин. "
    "Если открыть страницу, где только один запрос к API, всё нормально. В логах бэка: refresh token reuse detected».",
    "Дашборд шлёт несколько запросов параллельно. Бэк ротирует refresh-токены и считает повторное использование старого кражей. "
    "Исправь клиент (createClient): при одновременных 401 должен выполняться ровно один refresh, а все запросы — дождаться его и повториться "
    "с новым токеном. loadDashboard(n) возвращает результаты n параллельных запросов и число вызовов refresh на сервере.",
    code_=r"""
// Фейковый бэкенд: refresh одноразовый (rotation), повтор старого — считаем кражей
function createServer() {
  const state = { access: null, refresh: 'r1', version: 1, refreshCalls: 0 };
  return {
    state,
    api(token) {
      return Promise.resolve({ status: token === state.access ? 200 : 401 });
    },
    refresh(token) {
      state.refreshCalls += 1;
      if (token !== state.refresh) {
        state.refresh = null; // reuse detected: гасим сессию
        return Promise.resolve({ status: 401 });
      }
      state.version += 1;
      state.access = 'a' + state.version;
      state.refresh = 'r' + state.version;
      return Promise.resolve({ status: 200, access: state.access, refresh: state.refresh });
    },
  };
}

function createClient(server) {
  const tokens = { access: 'a1-expired', refresh: 'r1' };

  async function refreshTokens() {
    const res = await server.refresh(tokens.refresh);
    if (res.status !== 200) throw new Error('logout');
    tokens.access = res.access;
    tokens.refresh = res.refresh;
  }

  async function request() {
    let res = await server.api(tokens.access);
    if (res.status === 401) {
      await refreshTokens();
      res = await server.api(tokens.access);
    }
    return res.status;
  }

  return { request };
}

async function loadDashboard(parallel) {
  const server = createServer();
  const client = createClient(server);
  const jobs = [];
  for (let i = 0; i < parallel; i++) {
    jobs.push(client.request().catch((e) => e.message));
  }
  const results = await Promise.all(jobs);
  return { results, refreshCalls: server.state.refreshCalls };
}
""",
    language="javascript", entry="loadDashboard",
    answer=code(r"""
// Фейковый бэкенд: refresh одноразовый (rotation), повтор старого — считаем кражей
function createServer() {
  const state = { access: null, refresh: 'r1', version: 1, refreshCalls: 0 };
  return {
    state,
    api(token) {
      return Promise.resolve({ status: token === state.access ? 200 : 401 });
    },
    refresh(token) {
      state.refreshCalls += 1;
      if (token !== state.refresh) {
        state.refresh = null; // reuse detected: гасим сессию
        return Promise.resolve({ status: 401 });
      }
      state.version += 1;
      state.access = 'a' + state.version;
      state.refresh = 'r' + state.version;
      return Promise.resolve({ status: 200, access: state.access, refresh: state.refresh });
    },
  };
}

function createClient(server) {
  const tokens = { access: 'a1-expired', refresh: 'r1' };
  let refreshing = null; // промис текущего обновления, общий для всех запросов

  async function refreshTokens() {
    const res = await server.refresh(tokens.refresh);
    if (res.status !== 200) throw new Error('logout');
    tokens.access = res.access;
    tokens.refresh = res.refresh;
  }

  function refreshOnce() {
    if (!refreshing) {
      refreshing = refreshTokens().finally(() => {
        refreshing = null;
      });
    }
    return refreshing;
  }

  async function request() {
    let res = await server.api(tokens.access);
    if (res.status === 401) {
      await refreshOnce();
      res = await server.api(tokens.access);
    }
    return res.status;
  }

  return { request };
}

async function loadDashboard(parallel) {
  const server = createServer();
  const client = createClient(server);
  const jobs = [];
  for (let i = 0; i < parallel; i++) {
    jobs.push(client.request().catch((e) => e.message));
  }
  const results = await Promise.all(jobs);
  return { results, refreshCalls: server.state.refreshCalls };
}
"""),
    tests=[
        {"input": [1], "expected": {"results": [200], "refreshCalls": 1}},
        {"input": [3], "expected": {"results": [200, 200, 200], "refreshCalls": 1}},
        {"input": [5], "expected": {"results": [200, 200, 200, 200, 200], "refreshCalls": 1}},
    ],
    explanation="Когда access истёк, все параллельные запросы получают 401 одновременно, и каждый запускает свой refresh со старым токеном. "
    "Первый проходит и ротирует токен, остальные приходят со старым — сервер видит повторное использование, считает это кражей и гасит сессию. "
    "Бэк прав: детект повтора — важная защита, чинить надо фронт. Single-flight: первый 401 запускает обновление и сохраняет промис, остальные "
    "ждут этот же промис и повторяют запрос с новым токеном. finally обнуляет промис, чтобы следующий истёкший токен снова можно было "
    "обновить. Если вкладок несколько, их тоже координируют — через BroadcastChannel или Web Locks API.",
    hints=[
        "Посчитай, сколько раз вызовется server.refresh при трёх параллельных запросах и с каким токеном.",
        "Храни промис текущего обновления в замыкании клиента и отдавай его всем, кто пришёл с 401.",
        "if (!refreshing) refreshing = refreshTokens().finally(() => { refreshing = null; }); return refreshing;",
    ],
)

task(
    "fs-auth-e2e-04", "architecture", 5, "manager",
    "Стас: «Клиентке угнали аккаунт — в заказах чужой адрес. Она сменила пароль, а злоумышленник всё ещё заходит. Нужна кнопка "
    "„Выйти на всех устройствах“, и смена пароля тоже должна выкидывать всех. Дима говорит, что с JWT так нельзя. Как сделаем?»",
    "Как реализовать „выйти на всех устройствах“ и выход после смены пароля? Выбери решение.",
    code_=r"""
Сейчас:
  access  — JWT (HS256), exp 15 минут, живёт в памяти вкладки, API проверяет его без базы
  refresh — случайная строка в HttpOnly-куке; в таблице sessions хранится sha256(refresh), срок 30 дней
  смена пароля обновляет users.password_hash — и всё
Нагрузка: 12 подов API, ~3 000 RPS; Redis уже есть в кластере
""",
    language="text",
    options=[
        "Сменить секрет подписи JWT и перезапустить поды: все выданные токены сразу станут недействительными, и злоумышленник вылетит",
        "Уменьшить время жизни access до 1 минуты: украденный токен протухнет почти сразу, а хранить ничего не придётся",
        "Записывать каждый выданный access-токен в Postgres и на каждом запросе API проверять, что он есть в таблице и не отозван",
        "token_version: «выйти везде» и смена пароля удаляют сессии и повышают версию; access сверяем с ней в Redis на важных эндпоинтах",
        "Разослать через BroadcastChannel всем открытым вкладкам команду удалить refresh-куку и очистить access в памяти",
    ],
    answer=3,
    explanation="Stateless-JWT нельзя отозвать, но можно сделать так, чтобы он прожил недолго и больше не обновился. Удаление сессий "
    "в базе отрезает refresh у всех устройств сразу, а token_version в токене позволяет дёшево отклонить ещё живые access: одна проверка "
    "в Redis вместо запроса в базу, и только там, где это критично (заказ, адрес, оплата). Смена секрета разлогинит всех пользователей "
    "Маркета ради одной клиентки. BroadcastChannel работает только в браузерах этого человека, а злоумышленник сидит в своём. Таблица всех "
    "access-токенов — это 3 000 запросов в секунду к Postgres и растущая таблица, по сути сессии с лишними шагами. Минутный access без отзыва "
    "сессий ничего не меняет: злоумышленник спокойно обновит его своим refresh.",
    hints=[
        "Что продлевает жизнь злоумышленника дольше 15 минут: access или refresh?",
        "Как отклонить ещё не истёкший JWT, не храня все токены и не трогая других пользователей?",
    ],
)

task(
    "fs-auth-e2e-05", "incident", 5, "devops_colleague",
    "Дима: «После переезда API с поддомена на market.example/api все жалуются: ровно через 15 минут после входа выкидывает на логин. "
    "Локально в docker compose всё работает. Поды здоровы, 5xx нет».",
    "Почему refresh перестал работать после переезда и как это исправить?",
    code_=r"""
# Было: API на https://api.market.example   Стало: https://market.example/api/  (переехали, чтобы убрать CORS)

# POST https://market.example/api/auth/login -> 200
set-cookie: refresh_token=Qm9vb...; HttpOnly; Secure; SameSite=Lax; Path=/auth/refresh; Max-Age=2592000

# через 15 минут: POST https://market.example/api/auth/refresh -> 401
Request headers:
  content-type: application/json
  origin: https://market.example
  (заголовка cookie нет)
Response: {"detail": "refresh token missing"}

# nginx (новый)
location /api/ {
    proxy_pass http://market-api:8000/;
}

# backend/app/api/auth.py
response.set_cookie("refresh_token", token, max_age=30 * 24 * 3600,
                    httponly=True, secure=True, samesite="lax", path="/auth/refresh")
""",
    language="text",
    options=[
        "После переезда у подов разные секреты подписи JWT: access, выданный одним подом, не принимает другой — вынести секрет в общий Secret",
        "SameSite=Lax блокирует куку на POST-запросах из fetch после переезда на новый домен — поставить SameSite=None; Secure",
        "Ingress не пропускает Set-Cookie от бэка — добавить proxy_pass_header Set-Cookie в location /api/",
        "Кука стоит с Path=/auth/refresh, а браузер ходит на /api/auth/refresh и не прикладывает её — поправить Path",
        "Refresh через новый ingress стал медленным и не успевает — увеличить время жизни access до суток, пока разбираемся",
    ],
    answer=3,
    explanation="Браузер прикладывает куку только к URL, путь которых начинается с её Path. На поддомене API жил без префикса, "
    "и /auth/refresh совпадал с адресом в браузере. Теперь nginx срезает /api (proxy_pass со слэшем на конце), бэк по-прежнему считает себя "
    "на /auth/..., а браузер ходит на /api/auth/refresh — кука не уходит, refresh отвечает 401, и через 15 минут, когда истекает access, "
    "пользователя выкидывает. Path нужно задавать от публичного URL: настройкой в бэке или proxy_cookie_path /auth/ /api/auth/ в nginx. "
    "Разные секреты ломали бы запросы сразу и случайно, SameSite=Lax не мешает same-site POST, а Set-Cookie прокси пропустил — он виден "
    "в ответе. Увеличенный access просто отложит тот же выход на сутки.",
    hints=[
        "Сравни Path в Set-Cookie с URL, на который уходит refresh.",
        "Что делает слэш в конце proxy_pass с префиксом /api/?",
    ],
    time_limit=20,
)


# =====================================================================================
# fs-feature-e2e — Фича целиком: от тикета до прода
# =====================================================================================
theory("fs-feature-e2e", r"""
Middle отличается от Junior тем, что берёт тикет «хотим избранное» и сам доводит его до прода: от схемы базы до графика в Grafana.

Декомпозиция по слоям:
• Контракт: эндпоинты, поля, ошибки, пагинация. Черновик OpenAPI согласуй с фронтом до кода — тогда фронт начнёт работать на моках параллельно.
• База: миграция (таблица, индексы, ограничения уникальности), объём данных через год, нужен ли бэкфилл.
• Бэк: эндпоинты, валидация, права (кто может), идемпотентность, тесты, логи и метрики.
• Фронт: все состояния экрана (загрузка, пусто, ошибка, нет прав), оптимистичные обновления, доступность, тексты.
• Инфраструктура: переменные окружения и секреты, воркер или крон, лимиты nginx (размер тела для загрузки файлов), хранилище, алерты.
• Выкладка: фиче-флаг, порядок деплоя, план отката, удаление флага.

Вертикальные срезы вместо слоёв. Не «неделя на бэк, потом неделя на фронт», а тонкий сквозной срез в первые дни: одна кнопка, один эндпоинт, одна таблица — и сразу на стейдж за флагом. Интеграционные сюрпризы (формат дат, размер тела, права) всплывут на второй день, а не в пятницу вечером.

Правило денег и прав: всё, что влияет на цену, остатки и доступ, решает сервер. Фронт может посчитать скидку для красоты, но итог заказа сервер считает сам, а скрытая кнопка — не защита, если эндпоинт открыт.

Фиче-флаг. Флаг вычисляется на бэке, фронт получает готовый результат, например из /api/config:

    {"flags": {"favorites": true, "new_checkout": false}}

Эндпоинт фичи проверяет тот же флаг — иначе кнопку спрятали, а API доступен всем. Порядок правил: аварийный выключатель сильнее всего, потом явные разрешения (тестировщики, сотрудники), потом процент. Процент считается от стабильного хеша имени флага и id пользователя, а не random() на каждый запрос, чтобы человек не прыгал между версиями посреди оформления заказа. Раскатка: выключено → сотрудники → 5% → 25% → 100%, на каждом шаге смотри ошибки и бизнес-метрику. Флаг — это долг: задачу на удаление заводи сразу.

Порядок выкладки аддитивной фичи: миграция (expand) → бэк с выключенным флагом → фронт с выключенным флагом → включение по шагам → удаление флага и старого кода. Каждый шаг безопасен сам по себе и откатывается отдельно.

Оценка фичи целиком. Разработчики оценивают «код», а время съедают стыки: согласование контракта, ревью, тестовые данные, стейдж, QA, инфраструктура, мониторинг. Разбивай на задачи по 0,5–2 дня с понятным результатом. Неизвестное (внешняя интеграция, новый тип хранилища) выноси в спайк с ограниченным временем. Называй оценку диапазоном и с условиями: «2 недели, если модерация фото ручная и S3 уже есть».

Definition of Done: код в main, миграции на проде, флаг включён на 100% или есть план, метрики и алерты настроены, документация API обновлена, удаление флага запланировано.
""")

task(
    "fs-feature-e2e-01", "estimation", 4, "manager",
    "Стас: «Хотим отзывы с фотографиями на карточке товара, как у больших маркетплейсов. Покупатель прикладывает до 5 фото, остальные "
    "листают их в галерее. Дизайн уже нарисован. Сколько займёт?»",
    "Какие вопросы и риски нужно выяснить до оценки, потому что они меняют объём работы в разы? Выбери все важные.",
    options=[
        "Нужна ли модерация фото до публикации (ручная, автоматическая, по жалобам) и кто будет модерировать?",
        "В каком порядке показывать фото в галерее — по дате или как загрузил покупатель?",
        "Кто может оставить отзыв с фото: любой залогиненный или только тот, кто купил товар?",
        "Какой максимальный размер и какие форматы фото (HEIC с айфонов?), нужны ли ресайз и превью?",
        "Какого цвета рамка у превью и скругляем ли углы, как у карточек товара?",
        "Есть ли у нас S3-хранилище и CDN, или их тоже придётся поднимать?",
        "Нужна ли анимация при открытии галереи и индикатор «3 из 5» под фото?",
    ],
    answer=[0, 2, 3, 5],
    explanation="Каждый из четырёх вопросов меняет архитектуру, а не пару строк. Модерация — это статусы, админка и, возможно, интеграция "
    "с автоматической проверкой. «Только купившие» — связь с заказами и защита от накруток. Размер и HEIC решают, как загружать (через API "
    "упрёмся в client_max_body_size и нагрузку на поды, поэтому presigned URL прямо в S3) и нужен ли воркер, который конвертирует, делает "
    "превью и вырезает EXIF с геолокацией. Нет S3 и CDN — ещё задачи для DevOps. Прикидка: миграция и API загрузки — 2 дня, воркер — 2 дня, "
    "фронт с прогрессом загрузки и галереей — 3 дня, модерация — 2 дня, инфраструктура и мониторинг — 1–2 дня; итого 2–2,5 недели "
    "с тестированием, а без модерации и с готовым S3 — около недели. Порядок фото, рамки и анимации дизайн уже решил.",
    hints=[
        "Какие ответы добавят целые компоненты: админку, воркер, новое хранилище?",
        "Подумай, через что пройдут 5 фото по 6 МБ от формы до хранилища.",
    ],
    time_limit=15,
)

task(
    "fs-feature-e2e-02", "write_code", 4, "teamlead",
    "Гена: «Фронт прячет новый checkout за флагом, который считает сам, а бэк про флаг не знает — кто-то уже нашёл новый эндпоинт через DevTools. "
    "Делаем по-взрослому: флаги считает бэк, отдаёт их фронту в /api/config и сам проверяет в эндпоинтах. Напиши вычисление».",
    "Напиши flags_for_user(flags, user) → словарь {имя флага: bool}. Конфиг флага может содержать enabled (по умолчанию True), "
    "allow_users (список id, по умолчанию пустой), staff (bool, по умолчанию False), percent (0–100, по умолчанию 0). Правила по порядку:\n"
    "1) enabled == False → False для всех (аварийный выключатель сильнее всего);\n"
    "2) id пользователя в allow_users → True;\n"
    "3) staff == True и user[\"is_staff\"] → True;\n"
    "4) иначе True, если bucket(имя, id) < percent.\n"
    "Функция bucket уже есть — не меняй её.",
    code_=r"""
def bucket(flag_name, user_id):
    # стабильная «корзина» 0..99: один и тот же пользователь всегда попадает в одну и ту же
    h = 0
    for ch in f"{flag_name}:{user_id}":
        h = (h * 31 + ord(ch)) % 4294967296
    h = (h * 2654435761) % 4294967296  # перемешиваем: соседние id не должны идти подряд
    return h // 65536 % 100


def flags_for_user(flags, user):
    # flags: {"new_checkout": {"enabled": True, "allow_users": [7], "staff": True, "percent": 5}}
    # user:  {"id": 7, "is_staff": False}
    result = {}
    # твой код
    return result
""",
    language="python", entry="flags_for_user",
    answer=code(r"""
def bucket(flag_name, user_id):
    # стабильная «корзина» 0..99: один и тот же пользователь всегда попадает в одну и ту же
    h = 0
    for ch in f"{flag_name}:{user_id}":
        h = (h * 31 + ord(ch)) % 4294967296
    h = (h * 2654435761) % 4294967296  # перемешиваем: соседние id не должны идти подряд
    return h // 65536 % 100


def flags_for_user(flags, user):
    result = {}
    for name, cfg in flags.items():
        if not cfg.get("enabled", True):
            result[name] = False
        elif user["id"] in cfg.get("allow_users", []):
            result[name] = True
        elif cfg.get("staff", False) and user.get("is_staff", False):
            result[name] = True
        else:
            result[name] = bucket(name, user["id"]) < cfg.get("percent", 0)
    return result
"""),
    tests="FLAG_TESTS",  # заполняется ниже по эталону
    explanation="Порядок правил — и есть смысл фиче-флага. Аварийный выключатель проверяется первым: если в пятницу вечером всё горит, "
    "флаг должен выключиться для всех, включая тестировщиков из allow_users. Дальше явные разрешения и сотрудники — так фичу смотрят изнутри "
    "до раскатки. Процент считается от стабильного хеша имени флага и id: один и тот же человек всегда в одной корзине и не прыгает между "
    "старым и новым checkout, а разные флаги достаются разным группам. Результат уходит фронту в /api/config, а эндпоинт новой фичи на бэке "
    "проверяет этот же флаг — скрытая кнопка не мешает вызвать API напрямую.",
    hints=[
        "Перебери flags.items() и для каждого флага пройди правила сверху вниз — первое сработавшее решает.",
        "Значения по умолчанию удобно брать через cfg.get(\"percent\", 0).",
        "Последнее правило: result[name] = bucket(name, user[\"id\"]) < cfg.get(\"percent\", 0).",
    ],
)

task(
    "fs-feature-e2e-03", "code_review", 5, "teamlead",
    "Марина: «Промокоды в корзине — фича целиком в одном PR: миграция, бэк и фронт. Стас хочет влить сегодня. Я вижу минимум четыре проблемы "
    "на стыках. Найди их».",
    "Выбери все замечания, которые действительно нужно исправить.",
    code_=r"""
--- /dev/null
+++ b/backend/migrations/versions/0142_promo_codes.py
+def upgrade():
+    op.create_table(
+        "promo_codes",
+        sa.Column("id", sa.Integer, primary_key=True),
+        sa.Column("code", sa.String(32), nullable=False),
+        sa.Column("percent", sa.Integer, nullable=False),
+        sa.Column("valid_until", sa.DateTime(timezone=True), nullable=False),
+    )
+    op.add_column("orders", sa.Column("promo_code_id", sa.Integer, nullable=True))

--- a/backend/app/api/orders.py
+++ b/backend/app/api/orders.py
 class OrderIn(BaseModel):
     items: list[OrderItemIn]
+    promo_code: str | None = None
+    total_minor: int                  # итог со скидкой, посчитан на фронте

 @router.post("/orders", status_code=201)
 async def create_order(data: OrderIn, user: User = Depends(current_user), db=Depends(get_db)):
-    total = await pricing.calc_total(db, data.items)
+    total = data.total_minor
+    promo = None
+    if data.promo_code:
+        promo = await promo_repo.by_code(db, data.promo_code)
     order = await orders_repo.create(
-        db, user_id=user.id, items=data.items, total_minor=total,
+        db, user_id=user.id, items=data.items, total_minor=total,
+        promo_code_id=promo.id if promo else None,
     )
     return {"id": order.id}

--- /dev/null
+++ b/frontend/src/features/cart/PromoField.tsx
+export function PromoField({ cart, onTotal }: Props) {
+  const { flags } = useConfig();
+  const [code, setCode] = useState("");
+  const [percent, setPercent] = useState(0);
+  if (!flags.promo) return null;
+
+  async function apply() {
+    const res = await fetch(`/api/promo/${code}`);
+    const promo = await res.json();       // 200: {percent, valid_until}; 404: {detail}
+    setPercent(promo.percent);
+    onTotal(Math.round((cart.subtotalMinor * (100 - promo.percent)) / 100));
+  }
+  // ...разметка поля и кнопки
""",
    language="text",
    options=[
        "Сервер берёт итог заказа из запроса: через DevTools любой отправит total_minor: 100 — итог должен считать сервер",
        "useState для кода промокода заменить на useRef — каждое нажатие клавиши вызывает лишний ререндер корзины",
        "У promo_codes.code нет уникального индекса: появятся дубли, а поиск по коду пойдёт полным сканом",
        "promo_code_id в orders сделать NOT NULL со значением по умолчанию 0 — nullable-колонки усложняют запросы",
        "Бэк применяет промокод без проверки флага promo и срока valid_until — флаг прячет только поле на фронте",
        "Нет проверки res.ok: на несуществующий промокод придёт 404 с {detail}, promo.percent будет undefined, и итог станет NaN",
        "fetch стоит заменить на axios — он быстрее и лучше подходит для запросов к нашему API",
    ],
    answer=[0, 2, 4, 5],
    explanation="Главное правило фуллстека: всё, что влияет на деньги и права, решает сервер. Итог, пришедший с фронта, — это пожелание "
    "клиента, а не цена: любой отправит total_minor: 100 и заплатит рубль. Флаг и срок действия, проверенные только в интерфейсе, не защищают "
    "API — выключенный флаг должен выключать и серверную логику, а просроченный промокод отклоняться на бэке. Без уникального индекса на code "
    "появятся дубли, и поиск по коду пойдёт полным сканом. На фронте без проверки res.ok ответ 404 превращает итог в NaN. useRef и axios — "
    "вкусовщина, а NOT NULL со значением 0 для внешнего ключа вреден: nullable-колонка как раз безопасна для expand-миграции.",
    hints=[
        "Что из этого может подделать пользователь с DevTools?",
        "Какие проверки существуют только в браузере?",
        "Что вернёт этот fetch, если промокода нет, и что станет с итогом?",
    ],
)

task(
    "fs-feature-e2e-04", "architecture", 5, "manager",
    "Стас: «Новый checkout готов, хочу в пятницу включить его всем — у конкурентов распродажа с понедельника. Фронт и бэк деплоятся "
    "отдельно, бэк раскатывается rolling по 10 подам. Как выкатываем, чтобы не опозориться?»",
    "Какой план выкатки нового checkout выбрать?",
    options=[
        "Смержить ветку new-checkout в main в пятницу вечером и задеплоить фронт и бэк одновременно: вечером на сайте меньше людей, а к понедельнику всё устаканится",
        "Поднять отдельный деплоймент checkout-v2 и в nginx через split_clients по $request_id отправлять на него 10% запросов — настоящая канарейка без изменений в коде",
        "Выкатить новый checkout сразу всем и удалить старый, чтобы не поддерживать две версии; если что-то пойдёт не так — kubectl rollout undo за минуту",
        "Показывать новый checkout пользователям с чётным user_id, а проверку сделать на фронте, чтобы не трогать бэк перед распродажей",
        "Бэк заранее с выключенным флагом, затем фронт; флаг считает бэк по стабильному хешу user_id: сотрудники → 5% → 25% → 100% с метриками на каждом шаге",
    ],
    answer=4,
    explanation="Поэтапная выкатка превращает один большой риск в серию маленьких. Бэк с новыми эндпоинтами уезжает заранее и ничего "
    "не меняет, пока флаг выключен; фронт тоже. Флаг на бэке по стабильному хешу держит пользователя в одной версии на всём пути оформления "
    "и закрывает API от тех, кому фичу не включили. Метрики на каждом шаге покажут проблему на 5% трафика, а не на 100%, а выключение флага "
    "откатывает фичу за секунды без деплоя. Сплит по запросам гоняет человека между версиями посреди оформления; большой мерж в пятницу "
    "вечером — лотерея; rollout undo не откатит ни данные, ни открытые вкладки. Чётные id с проверкой только на фронте — это сразу 50% "
    "без контроля и при открытом API.",
    hints=[
        "Что будет с пользователем, если его запросы попадают то в старую, то в новую версию?",
        "Какой способ позволяет выключить фичу за секунды и без деплоя?",
    ],
)


# =====================================================================================
# fs-realtime — Реалтайм: WebSocket и SSE
# =====================================================================================
theory("fs-realtime", r"""
Когда данные должны приходить сами (статус заказа, чат поддержки, остатки на распродаже), есть три способа.

• Polling — фронт раз в N секунд делает GET /api/orders/42. Просто, работает везде, но задержка до N секунд и много пустых запросов. Для «обновить раз в 30 секунд» — лучший выбор.
• SSE (Server-Sent Events) — обычный HTTP-ответ с Content-Type: text/event-stream, который не закрывается. Только сервер → клиент, текстовые события; EventSource сам переподключается и присылает Last-Event-ID. Идеален для статусов, уведомлений, стриминга ответа LLM.
• WebSocket — после Upgrade-рукопожатия двусторонний канал поверх одного TCP-соединения. Нужен для чатов, совместного редактирования, игр. Переподключение, heartbeat и восстановление состояния — твоя забота.

Формат SSE: поля id:, event:, data:; пустая строка завершает событие; строка, начинающаяся с двоеточия, — комментарий (им шлют heartbeat).

    id: 1042
    event: status
    data: {"order": 42, "status": "courier"}

EventSource умеет только GET без своих заголовков, поэтому POST-стриминг читают через fetch и response.body и парсят сами — помня, что сеть режет поток на куски как угодно и событие может прийти в двух чанках.

Через nginx:
• WebSocket требует HTTP/1.1 к бэку и проброса hop-by-hop заголовков Upgrade и Connection:

    map $http_upgrade $connection_upgrade { default upgrade; '' close; }
    location /ws/ {
        proxy_pass http://market_api;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 1h;
    }

• proxy_read_timeout по умолчанию 60 секунд: минута тишины — и nginx закрывает соединение. Лечится heartbeat (ping каждые 20–30 с) плюс увеличенный таймаут. В ingress на nginx — аннотации proxy-read-timeout и proxy-send-timeout; у облачных балансировщиков свой idle timeout.
• SSE ломает буферизация: nginx копит ответ и отдаёт пачкой. Нужен proxy_buffering off в location или заголовок X-Accel-Buffering: no от бэка; сжатие для стрима тоже выключают.

Масштабирование. Соединение живёт в конкретном процессе конкретного пода. Если событие «заказ передан курьеру» обработал другой под или воркер, его нужно доставить туда, где висит клиент: через pub/sub (Redis Pub/Sub, NATS) каждый под подписывается и рассылает своим клиентам. Sticky sessions этого не решают. Каждое соединение — файловый дескриптор и память, поэтому бэк асинхронный, а лимиты рассчитаны заранее.

Переподключение. Деплой или падение пода рвёт тысячи соединений разом. Клиент переподключается с экспоненциальной задержкой и случайным разбросом (jitter), иначе все вернутся в одну секунду. После переподключения состояние досинхронизируют: по Last-Event-ID или обычным REST-запросом.

Авторизация и лимиты. Браузерный WebSocket не умеет ставить заголовок Authorization — используют куку или короткоживущий тикет. Сервер обязан проверять Origin, иначе чужой сайт откроет сокет с куками пользователя (Cross-Site WebSocket Hijacking). По HTTP/1.1 браузер держит до 6 соединений на домен, и несколько вкладок с SSE упрутся в лимит; HTTP/2 эту проблему снимает.
""")

task(
    "fs-realtime-01", "write_code", 4, "devops_colleague",
    "Дима: «Фронт сделал живой статус заказа через SSE и чат поддержки на WebSocket. Локально через Vite всё летает, а на стейдже "
    "сокет закрывается с кодом 1006, и статусы приходят пачкой раз в несколько минут. Конфиг nginx твой — поправь».",
    "Допиши конфиг. /ws/ должен проксировать WebSocket: HTTP/1.1 к бэку, проброс Upgrade и Connection, таймаут чтения не меньше "
    "5 минут, чтобы тихое соединение не рвалось через 60 секунд. /api/orders/stream (SSE) должен отдаваться без буферизации.",
    code_=r"""
upstream market_api {
    server market-api:8000;
}

map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    server_name stage.market.example;

    location /api/ {
        proxy_pass http://market_api;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api/orders/stream {
        proxy_pass http://market_api;
        proxy_set_header Host $host;
    }

    location /ws/ {
        proxy_pass http://market_api;
        proxy_set_header Host $host;
    }
}
""",
    language="nginx",
    answer=code(r"""
upstream market_api {
    server market-api:8000;
}

map $http_upgrade $connection_upgrade {
    default upgrade;
    ''      close;
}

server {
    listen 80;
    server_name stage.market.example;

    location /api/ {
        proxy_pass http://market_api;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location /api/orders/stream {
        proxy_pass http://market_api;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header Connection "";
        proxy_buffering off;
        proxy_cache off;
        proxy_read_timeout 1h;
    }

    location /ws/ {
        proxy_pass http://market_api;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $connection_upgrade;
        proxy_read_timeout 1h;
        proxy_send_timeout 1h;
    }
}
"""),
    tests=[
        rx("WebSocket: к бэку идёт HTTP/1.1", r"location\s+/ws/\s*\{[^}]*proxy_http_version\s+1\.1\s*;", "i"),
        rx("WebSocket: проброшен заголовок Upgrade", r"location\s+/ws/\s*\{[^}]*proxy_set_header\s+Upgrade\s+\$http_upgrade\s*;", "i"),
        rx("WebSocket: проброшен заголовок Connection", r"location\s+/ws/\s*\{[^}]*proxy_set_header\s+Connection\s+(?:\$connection_upgrade|[\"']?upgrade[\"']?)\s*;", "i"),
        rx("WebSocket: proxy_read_timeout не меньше 5 минут",
           r"location\s+/ws/\s*\{[^}]*proxy_read_timeout\s+(?:(?:[3-9]\d\d|\d{4,})s?|(?:[5-9]|[1-9]\d+)m|[1-9]\d*[hd])\s*;", "i"),
        rx("SSE: буферизация выключена", r"location\s+/api/orders/stream\s*\{[^}]*proxy_buffering\s+off\s*;", "i"),
    ],
    explanation="WebSocket начинается как обычный HTTP-запрос с заголовками Upgrade: websocket и Connection: Upgrade. Это hop-by-hop "
    "заголовки: прокси их не пересылает, пока ты явно не пробросишь их через proxy_set_header, а рукопожатие требует HTTP/1.1 — по умолчанию "
    "nginx ходит к бэку по HTTP/1.0. map из конфига превращает $http_upgrade в upgrade или close, так что этот location переживёт и обычный "
    "запрос. proxy_read_timeout по умолчанию 60 секунд — поэтому тихий сокет рвётся ровно через минуту; помимо таймаута в приложении нужен "
    "heartbeat. SSE ломает буферизация: nginx копит ответ бэка и отдаёт пачкой, поэтому proxy_buffering off (или заголовок X-Accel-Buffering: no "
    "от бэка).",
    hints=[
        "Для WebSocket нужны три строки про HTTP/1.1, Upgrade и Connection — map уже есть в конфиге.",
        "Какое значение proxy_read_timeout по умолчанию и почему сокет рвётся ровно через минуту?",
        "Для SSE в location /api/orders/stream добавь proxy_buffering off;",
    ],
)

task(
    "fs-realtime-02", "find_bug", 4, "qa",
    "Ира: «Ответ бота в чате поддержки приходит стримом, и иногда в нём обрывки: вместо „Ваш заказ передан курьеру“ приходит "
    "„Ваш зак“ и следом пустое сообщение. Ещё раз в 15 секунд в чате появляется пустое сообщение. Воспроизводится на медленном 3G».",
    "Фронт читает стрим через fetch и сам разбирает text/event-stream. parseSSE(chunks) получает куски в том порядке, в каком они пришли "
    "из сети, и возвращает события {event, data}. Исправь: событие, разрезанное между чанками, собирается целиком; незаконченное событие "
    "в конце не отдаётся; блоки без строк data (heartbeat-комментарии «: ping») пропускаются. Несколько строк data склеиваются через \\n.",
    code_=r"""
function parseBlock(block) {
  const ev = { event: 'message', data: '' };
  const dataLines = [];
  for (const line of block.split('\n')) {
    if (line.startsWith(':')) continue; // комментарий, например heartbeat
    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') ev.event = value;
    if (field === 'data') dataLines.push(value);
  }
  ev.data = dataLines.join('\n');
  return ev;
}

function parseSSE(chunks) {
  const events = [];
  for (const chunk of chunks) {
    for (const block of chunk.split('\n\n')) {
      if (block.trim() === '') continue;
      events.push(parseBlock(block));
    }
  }
  return events;
}
""",
    language="javascript", entry="parseSSE",
    answer=code(r"""
function parseBlock(block) {
  const ev = { event: 'message', data: '' };
  const dataLines = [];
  for (const line of block.split('\n')) {
    if (line.startsWith(':')) continue; // комментарий, например heartbeat
    const colon = line.indexOf(':');
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? '' : line.slice(colon + 1);
    if (value.startsWith(' ')) value = value.slice(1);
    if (field === 'event') ev.event = value;
    if (field === 'data') dataLines.push(value);
  }
  if (dataLines.length === 0) return null; // heartbeat или пустой блок — не событие
  ev.data = dataLines.join('\n');
  return ev;
}

function parseSSE(chunks) {
  const events = [];
  let buffer = '';
  for (const chunk of chunks) {
    buffer += chunk;
    const blocks = buffer.split('\n\n');
    buffer = blocks.pop(); // хвост ещё не закончился пустой строкой — ждём следующий чанк
    for (const block of blocks) {
      const ev = parseBlock(block);
      if (ev) events.push(ev);
    }
  }
  return events;
}
"""),
    tests=[
        {"input": [["event: status\ndata: courier\n\n"]],
         "expected": [{"event": "status", "data": "courier"}]},
        {"input": [["data: Ваш зак", "аз передан курьеру\n\n"]],
         "expected": [{"event": "message", "data": "Ваш заказ передан курьеру"}]},
        {"input": [[": ping\n\n", "data: строка 1\ndata: строка 2\n\n"]],
         "expected": [{"event": "message", "data": "строка 1\nстрока 2"}]},
        {"input": [["data: {\"id\":1}\n\ndata: {\"id\"", ":2}\n\ndata: {\"id\":3}"]],
         "expected": [{"event": "message", "data": "{\"id\":1}"}, {"event": "message", "data": "{\"id\":2}"}]},
        {"input": [["data: a\n", "\ndata: b\n\n"]],
         "expected": [{"event": "message", "data": "a"}, {"event": "message", "data": "b"}]},
    ],
    explanation="Сеть ничего не знает о событиях SSE: TCP отдаёт байты кусками произвольного размера, и на медленном 3G одно событие легко "
    "разрезается на два чанка. Парсер, который разбирает каждый чанк отдельно, отдаёт обрывки. Правильная схема — буфер: дописываем чанк, "
    "режем по пустой строке, а последний кусок оставляем в буфере до следующего чанка, потому что он может быть незаконченным. Блок без строк "
    "data — не событие: так сервер шлёт heartbeat-комментарии «: ping», чтобы прокси не закрыл тихое соединение, и показывать их пользователю "
    "нельзя. Ровно так ведёт себя EventSource; стрим через fetch приходится разбирать вручную по тем же правилам.",
    hints=[
        "Что будет, если граница чанка пройдёт посреди строки data?",
        "Накапливай чанки в буфере и после split('\\n\\n') оставляй последний кусок в буфере.",
        "parseBlock должен вернуть null, если в блоке нет ни одной строки data.",
    ],
)

task(
    "fs-realtime-03", "incident", 5, "devops_colleague",
    "Дима: «Чат поддержки на WebSocket. Пользователи жалуются, что ответы оператора не доходят, пока не обновишь страницу. Началось после "
    "переезда на новый кластер. Под нагрузкой всё хорошо, страдают те, кто долго молчит в чате».",
    "Выбери все правильные действия, чтобы сообщения перестали теряться.",
    code_=r"""
# Консоль браузера у Иры (чат открыт, никто не пишет)
18:02:10 ws open   wss://market.example/ws/support?ticket=...
18:03:10 ws close  code=1006 reason="" wasClean=false
18:14:52 (оператор отправил ответ — у клиента тишина, переподключения нет)

# ingress (nginx), error.log
2026/09/24 18:03:10 [error] 31#31: *88213 upstream timed out (110: Connection timed out) while proxying upgraded connection, client: 10.2.0.14, server: market.example, request: "GET /ws/support?ticket=... HTTP/1.1", upstream: "http://10.4.1.23:8000/ws/support?ticket=..."

# Метрика длительности WS-сессий за сутки: p50 = 60.0 s, p95 = 60.1 s

# frontend/src/chat/socket.ts
const socket = new WebSocket(url);
socket.onclose = () => setStatus('offline');
""",
    language="text",
    options=[
        "Слать heartbeat (ping/pong или служебное сообщение) каждые 20–30 секунд, чтобы соединение не простаивало",
        "Включить sticky sessions на ingress — сообщения теряются, потому что клиент попадает на другой под",
        "Поднять proxy-read-timeout и proxy-send-timeout для /ws/ на ingress, например до 3600 секунд",
        "Увеличить memory limit подов чата — их убивает OOM, и соединения рвутся вместе с процессом",
        "На фронте при onclose переподключаться с backoff и jitter и догружать пропущенные сообщения по id последнего",
        "Перейти с WebSocket на polling раз в секунду — прокси не рвёт короткие запросы, и сообщения не теряются",
    ],
    answer=[0, 2, 4],
    explanation="Все сессии живут ровно 60 секунд, закрываются с кодом 1006, а nginx пишет upstream timed out while proxying upgraded "
    "connection — это proxy_read_timeout по умолчанию: минута без данных, и прокси рвёт соединение. Активные пользователи не страдают, потому "
    "что их сообщения сбрасывают таймер. Чинить нужно с трёх сторон: heartbeat держит соединение живым через любые прокси и балансировщики, "
    "увеличенный таймаут на ingress даёт запас, а переподключение с backoff и догрузкой пропущенного делает клиент устойчивым к любому обрыву — "
    "деплой всё равно порвёт все сокеты. Sticky sessions WebSocket не нужны: это одно TCP-соединение, оно и так живёт на одном поде. "
    "Признаков OOM нет, а polling раз в секунду умножит нагрузку ради почти всегда пустых ответов.",
    hints=[
        "Почему все сессии живут одинаковые 60 секунд?",
        "Что должен делать клиент, если соединение всё-таки порвалось, — ведь сейчас он просто пишет offline?",
    ],
    time_limit=20,
)

task(
    "fs-realtime-04", "architecture", 5, "devops_colleague",
    "Дима: «Живой статус заказа на SSE отлично работал, пока API был в одном поде. Под распродажу подняли 6 подов — и большинство "
    "пользователей перестали получать обновления: статус меняется только после F5. К Чёрной пятнице ждём 60 тысяч одновременных подключений».",
    "Как правильно доставлять события до клиентов?",
    code_=r"""
Сейчас:
  служба доставки шлёт POST /webhooks/delivery -> ingress -> случайный под API
  обработчик вебхука обновляет заказ и вызывает notify(user_id, event)
  notify() ищет подписчиков в connections: dict[user_id, list[asyncio.Queue]] — в памяти процесса
  SSE-соединение клиента живёт на том поде, куда он попал при открытии страницы
Есть в кластере: Redis, PostgreSQL, Kafka (для аналитики)
""",
    language="text",
    options=[
        "Включить sticky sessions по куке на ingress, чтобы пользователь всегда попадал на один и тот же под — как было, когда под был один",
        "Хранить открытые соединения в таблице PostgreSQL (под, user_id) и из обработчика вебхука писать в соединение по записи из таблицы",
        "Отказаться от SSE: фронт раз в секунду запрашивает GET /api/orders/{id} — это проще и не зависит от числа подов",
        "Redis Pub/Sub: вебхук публикует событие, каждый под рассылает его своим клиентам; после реконнекта — Last-Event-ID",
        "Вернуться к одному поду API, но дать ему 32 CPU и 64 ГБ памяти — 60 тысяч соединений в него поместятся",
    ],
    answer=3,
    explanation="Соединение — это сокет внутри конкретного процесса, и писать в него можно только из этого процесса. Пока под был один, "
    "вебхук и клиент встречались в одной памяти; с шестью подами вебхук попадает на случайный под, а клиент висит на другом. Pub/Sub "
    "развязывает их: кто угодно публикует событие, каждый под получает события и отдаёт своим клиентам. Redis Pub/Sub доставляет не более "
    "одного раза и не хранит историю, поэтому досинхронизация после переподключения — обязательная часть решения. Sticky sessions привязывают "
    "клиента к поду, но вебхук от службы доставки приходит без куки пользователя. Сокет нельзя положить в базу, один большой под — единая точка "
    "отказа, а polling для 60 тысяч клиентов — это 60 тысяч RPS ради почти всегда одинаковых ответов.",
    hints=[
        "На каком поде обрабатывается вебхук и на каком висит соединение пользователя?",
        "Нужен механизм, который доставит событие во все поды, а не в один случайный.",
    ],
)


# =====================================================================================
# fs-performance-e2e — Производительность всего стека
# =====================================================================================
theory("fs-performance-e2e", r"""
«Тормозит» — это не диагноз. Первый вопрос Middle: где именно уходит время и как это доказать цифрами.

Путь запроса и где смотреть:
• Браузер: скачивание и выполнение JS, рендер, картинки. DevTools → Performance, Lighthouse и Web Vitals от реальных пользователей (RUM): LCP ≤ 2,5 с, INP ≤ 200 мс, CLS ≤ 0,1.
• Сеть: DNS, TLS, TTFB, размер ответа, цепочки запросов. DevTools → Network → Timing: Waiting for server response (TTFB) — сервер и путь до него, Content Download — размер ответа и канал клиента.
• nginx: добавь в лог $request_time и $upstream_response_time. Первое — вся обработка до отправки последнего байта клиенту, второе — сколько ждали бэк. Большая разница — медленный клиент или огромный ответ.
• API: время в коде, сериализации, внешних вызовах, ожидании соединения из пула.
• База: EXPLAIN ANALYZE, pg_stat_statements, блокировки.

Сквозная видимость:
• Server-Timing — бэк сам пишет разбивку в заголовок, и DevTools покажет её рядом с запросом:

    Server-Timing: db;dur=38, cache;desc="miss", app;dur=112

• Трейсинг (OpenTelemetry): один trace id от браузера через nginx и API до базы, каждый шаг — span с началом и концом. Self time спана — длительность минус время, покрытое дочерними спанами; он показывает, кто тормозит сам, а кто ждёт других. Дети могут идти параллельно, а часы разных машин расходятся на миллисекунды — дочерний спан иногда «вылезает» за родителя.
• Перцентили: p50 — типичный пользователь, p95/p99 — те, кто уходит. Среднее прячет хвосты.

Частые причины на стыках:
• Водопад: бандл → рендер → запрос профиля → только потом запрос каталога. Независимые запросы запускай параллельно (Promise.all) или собирай в один агрегирующий эндпоинт; критичные данные — preload или SSR.
• Лишние данные: список товаров с полными описаниями и отзывами, 5 МБ JSON. Нужны отдельная лёгкая модель для списка, пагинация и сжатие. У nginx gzip_types по умолчанию только text/html — application/json надо добавить явно (или включить brotli).
• Внешние вызовы без таймаутов: один медленный сервис рекомендаций держит всю страницу. Параллельно, с таймаутом и деградацией — показать страницу без блока рекомендаций.
• Соединения к базе: при росте подов растёт число соединений к Postgres. Упёрлись в max_connections или пул — запросы ждут, а CPU базы низкий. Лечится бюджетом соединений и PgBouncer, а не новыми подами.
• Кэш не там: статику с хешем в имени кэшируй на год (immutable), персональные ответы — Cache-Control: private или no-store.

Порядок работы: воспроизвести и измерить (p95 до), найти самый большой кусок, исправить одну вещь, измерить снова (p95 после), закрепить результат алертом или бюджетом производительности в CI. Оптимизация без замера до и после — гадание.
""")

task(
    "fs-performance-e2e-01", "incident", 4, "manager",
    "Стас: «После вчерашнего релиза каталог на мобилках грузится 8 секунд, конверсия просела на 12%. Бэкендеры говорят, что у них всё быстро, "
    "фронтендеры — что они ничего не трогали. Разберись, кто прав».",
    "На каком участке уходит время и что делаешь в первую очередь?",
    code_=r"""
# DevTools -> Network, профиль Fast 4G: GET /api/products?category=lamps&limit=100
Status 200   Size 4.8 MB   Time 6.92 s
Timing:
  Stalled                          4 ms
  Request sent                     1 ms
  Waiting for server response    184 ms
  Content Download              6.73 s
Response headers:
  content-type: application/json
  server-timing: db;dur=41, app;dur=96
  (content-encoding отсутствует)

# nginx access.log, тот же запрос
"GET /api/products?category=lamps&limit=100" 200 5021934 rt=6.915 urt=0.181

# git log backend --oneline -1
c7d21aa feat(catalog): отдаём полную карточку в списке (description_html, specs, reviews_preview)
""",
    language="text",
    options=[
        "Медленная база: после релиза запрос по category стал тяжелее — добавить индекс по category и проверить план через EXPLAIN ANALYZE",
        "Медленный React-рендер списка из 100 карточек — обернуть карточки в React.memo и виртуализировать список",
        "Время уходит на скачивание 4,8 МБ несжатого JSON — вернуть лёгкую модель для списка и включить сжатие application/json в nginx",
        "Не хватает подов API под выросший трафик — увеличить реплики с 6 до 12 и поднять лимиты CPU",
        "Это медленный мобильный интернет пользователей: с нашей стороны сделать ничего нельзя, дождёмся, пока метрики вернутся",
    ],
    answer=2,
    explanation="Цифры отвечают сами. Сервер думал 184 мс (Server-Timing: база 41 мс, приложение 96 мс), nginx ждал бэк 0,18 с, а весь "
    "запрос занял 6,9 с — 6,7 с ушли на Content Download. Ответ весит 4,8 МБ, и content-encoding нет: JSON не сжат, потому что у nginx "
    "gzip_types по умолчанию только text/html. Вырос ответ из-за вчерашнего коммита: в список положили полные карточки с описаниями "
    "и отзывами. Лечим обе причины: лёгкая модель для списка и сжатие (JSON жмётся в 5–10 раз). Индексы, поды и React.memo не помогут: "
    "ни база, ни бэк, ни рендер не узкое место, и метрики каждого слоя это доказывают. А канал пользователя мы не выбираем — выбираем, "
    "сколько байт по нему гнать.",
    hints=[
        "Сложи время по этапам: сколько думал сервер и сколько качались байты?",
        "Сравни rt и urt в логе nginx и посмотри на размер ответа и заголовки.",
    ],
    time_limit=20,
)

task(
    "fs-performance-e2e-02", "write_code", 4, "teamlead",
    "Гена: «У нас наконец есть трейсинг от браузера до базы, но смотреть каждый трейс глазами неудобно. Нужна функция, которая по спанам "
    "одного трейса скажет, сколько времени каждый слой тормозил сам, а не ждал других. Покажем её в дашборде „куда уходит время“».",
    "Напиши self_time_by_layer(spans). Спан: {\"id\", \"parent\" (id или None), \"layer\", \"start\", \"end\"} — время в мс. Self time спана = "
    "(end − start) минус суммарная длина объединения интервалов его прямых детей, обрезанных границами родителя (дети могут идти параллельно "
    "и перекрываться, а из-за рассинхрона часов — вылезать за родителя). Сложи self time по layer и верни список [layer, ms], "
    "отсортированный по убыванию ms, при равенстве — по имени слоя.",
    code_=r"""
def self_time_by_layer(spans):
    # spans: [{"id": 1, "parent": None, "layer": "frontend", "start": 0, "end": 2000}, ...]
    result = []
    # твой код
    return result
""",
    language="python", entry="self_time_by_layer",
    answer=code(r"""
def self_time_by_layer(spans):
    children = {}
    for s in spans:
        if s["parent"] is not None:
            children.setdefault(s["parent"], []).append(s)

    totals = {}
    for s in spans:
        start, end = s["start"], s["end"]
        # интервалы детей, обрезанные границами родителя
        intervals = []
        for c in children.get(s["id"], []):
            a = max(c["start"], start)
            b = min(c["end"], end)
            if a < b:
                intervals.append([a, b])
        intervals.sort()

        # длина объединения: перекрывающиеся интервалы склеиваем
        covered = 0
        cur = None
        for a, b in intervals:
            if cur is None or a > cur[1]:
                if cur is not None:
                    covered += cur[1] - cur[0]
                cur = [a, b]
            else:
                cur[1] = max(cur[1], b)
        if cur is not None:
            covered += cur[1] - cur[0]

        layer = s["layer"]
        totals[layer] = totals.get(layer, 0) + (end - start - covered)

    result = [[layer, ms] for layer, ms in totals.items()]
    result.sort(key=lambda x: (-x[1], x[0]))
    return result
"""),
    tests=[
        {"input": [[
            {"id": 1, "parent": None, "layer": "frontend", "start": 0, "end": 2000},
            {"id": 2, "parent": 1, "layer": "network", "start": 300, "end": 1900},
            {"id": 3, "parent": 2, "layer": "api", "start": 350, "end": 1850},
            {"id": 4, "parent": 3, "layer": "db", "start": 400, "end": 700},
            {"id": 5, "parent": 3, "layer": "db", "start": 420, "end": 650},
            {"id": 6, "parent": 3, "layer": "external", "start": 700, "end": 1500},
        ]], "expected": [["external", 800], ["db", 530], ["api", 400], ["frontend", 400], ["network", 100]]},
        {"input": [[
            {"id": 1, "parent": None, "layer": "frontend", "start": 0, "end": 120},
        ]], "expected": [["frontend", 120]]},
        {"input": [[
            {"id": 1, "parent": None, "layer": "network", "start": 0, "end": 100},
            {"id": 2, "parent": 1, "layer": "api", "start": 50, "end": 180},
        ]], "expected": [["api", 130], ["network", 50]]},
        {"input": [[
            {"id": 10, "parent": None, "layer": "api", "start": 0, "end": 1000},
            {"id": 11, "parent": 10, "layer": "db", "start": 100, "end": 200},
            {"id": 12, "parent": 10, "layer": "db", "start": 300, "end": 400},
            {"id": 13, "parent": 10, "layer": "db", "start": 350, "end": 500},
        ]], "expected": [["api", 700], ["db", 350]]},
    ],
    explanation="Длительность спана включает ожидание детей, поэтому по ней не понять, кто тормозит: корневой спан браузера всегда самый длинный. "
    "Self time — время, когда спан работал сам: длительность минус объединение интервалов детей. Именно объединение, потому что дети часто идут "
    "параллельно (asyncio.gather, Promise.all), и простая сумма вычла бы одно и то же время дважды. Обрезка по границам родителя защищает "
    "от рассинхрона часов браузера и сервера — иначе self time уйдёт в минус. Так строятся flame graph и отчёты «куда уходит время» в Jaeger, "
    "Grafana Tempo и других системах трейсинга.",
    hints=[
        "Сначала построй словарь parent → список детей.",
        "Для объединения отсортируй интервалы по началу и склеивай, пока следующий начинается не позже конца текущего.",
        "Сортировка результата: result.sort(key=lambda x: (-x[1], x[0])).",
    ],
)

task(
    "fs-performance-e2e-03", "code_review", 5, "teamlead",
    "Марина: «PR „ускоряем главную“: фронт распилил агрегирующий /api/home на три запроса, бэк убрал таймаут у рекомендаций и добавил кэш, "
    "DevOps добавил кэш статики. Автор уверен, что станет быстрее. Что скажешь?»",
    "Выбери все замечания, которые действительно нужно оставить в ревью.",
    code_=r"""
--- a/frontend/src/pages/Home.tsx
+++ b/frontend/src/pages/Home.tsx
 export function Home() {
   const [data, setData] = useState<HomeData | null>(null);
   useEffect(() => {
     (async () => {
-      const res = await fetch("/api/home");
-      setData(await res.json());
+      const banners = await (await fetch("/api/banners")).json();
+      const hits = await (await fetch("/api/products/hits")).json();
+      const recs = await (await fetch("/api/recommendations")).json();
+      setData({ banners, hits, recs });
     })();
   }, []);

--- a/backend/app/api/recommendations.py
+++ b/backend/app/api/recommendations.py
 @router.get("/recommendations")
 async def recommendations(user: User = Depends(current_user)):
-    return await recs_client.for_user(user.id, timeout=0.3)
+    items = await recs_client.for_user(user.id)       # без таймаута: пусть досчитает
+    return JSONResponse(items, headers={"Cache-Control": "public, max-age=300"})

--- a/infra/nginx/market.conf
+++ b/infra/nginx/market.conf
+location /assets/ {
+    root /usr/share/nginx/html;
+    expires 1y;
+    add_header Cache-Control "public, immutable";
+}
""",
    language="text",
    options=[
        "Три независимых запроса идут по очереди через await — водопад: время главной стало суммой ответов; нужен Promise.all",
        "Кэшировать /assets/ на год нельзя: после релиза пользователи ещё год будут получать старую версию сайта",
        "Убран таймаут у внешнего сервиса рекомендаций: один медленный ответ подвесит главную — нужен таймаут и пустой список",
        "useEffect заменить на useLayoutEffect — он срабатывает раньше отрисовки, и данные начнут грузиться быстрее",
        "Cache-Control: public на персональных рекомендациях позволит CDN отдать их другим пользователям — нужен private",
        "fetch лучше заменить на XMLHttpRequest — он не создаёт промисы и поэтому быстрее на слабых телефонах",
    ],
    answer=[0, 2, 4],
    explanation="Все три настоящие проблемы — на стыках. Три независимых запроса через await по очереди — водопад: время главной стало суммой "
    "трёх ответов, а на мобильной сети каждый запрос ещё и дорогой; нужен Promise.all или агрегирующий эндпоинт. Внешний сервис без таймаута — "
    "самая частая причина «сайт висит»: медленные рекомендации держат запрос и ресурсы, правильно — короткий таймаут и деградация до пустого "
    "блока. public на персональном ответе разрешает CDN отдать рекомендации одного пользователя другому, нужен private. Годовой immutable-кэш "
    "для /assets/ как раз правильный: имена файлов содержат хеш, новая сборка — новые имена. useLayoutEffect и XMLHttpRequest на скорость "
    "загрузки не влияют.",
    hints=[
        "Зависит ли запрос хитов от ответа баннеров?",
        "Что будет с главной, если сервис рекомендаций начнёт отвечать 10 секунд?",
        "Для кого предназначен ответ /api/recommendations и кто ещё может его закэшировать?",
    ],
)

task(
    "fs-performance-e2e-04", "architecture", 5, "devops_colleague",
    "Дима: «В пик HPA раздул API с 8 до 24 подов — и стало только хуже: p99 вырос с 400 мс до 6 секунд, в логах ошибки подключения к базе. "
    "При этом CPU у Postgres 25%. Бэкендеры просят поднять maxReplicas до 48. Что делаем?»",
    "Что делаем вместо увеличения maxReplicas? Выбери правильное решение.",
    code_=r"""
market-api: HPA 8 -> 24 пода, в каждом 4 воркера uvicorn
SQLAlchemy (на процесс): pool_size=10, max_overflow=10
PostgreSQL: max_connections = 300, CPU 25%

$ kubectl logs -l app=market-api --since=10m | grep -c "too many clients already"
1873

=# SELECT state, count(*) FROM pg_stat_activity WHERE datname = 'market' GROUP BY state;
        state        | count
---------------------+-------
 idle                |   262
 active              |    18
 idle in transaction |    17

p99 API: 400 ms -> 6.2 s
""",
    language="text",
    options=[
        "Поднять max_connections в Postgres до 2000: CPU базы 25%, запас большой, соединений просто не хватает",
        "Добавить Redis-кэш на все запросы API, чтобы в базу уходило меньше запросов и соединения освобождались быстрее",
        "Разрешить HPA масштабироваться до 48 подов: запросов в пике больше, чем 24 пода успевают обработать",
        "Переключить все запросы API на read-реплику — соединения распределятся между двумя серверами, и лимит удвоится",
        "PgBouncer (transaction pooling) и бюджет соединений: поды × воркеры × пул ≤ лимита; ограничить maxReplicas",
    ],
    answer=4,
    explanation="Посчитай бюджет: 24 пода × 4 воркера × (10 + 10) — до 1 920 соединений, а Postgres принимает 300. Новые поды не ускоряют, "
    "а отнимают соединения: одни процессы держат idle-соединения в своих пулах, другие получают too many clients, и всё это при почти простаивающей "
    "базе — активных запросов 18. PgBouncer в transaction pooling превращает тысячи клиентских соединений в несколько десятков серверных, а пул "
    "на процесс уменьшают так, чтобы сумма по всем подам влезала в лимит; с asyncpg учти prepared statements (PgBouncer 1.21+ "
    "с max_prepared_statements или отключённый кэш на клиенте). 17 соединений idle in transaction — код держит транзакцию, пока ходит во внешние "
    "сервисы, это тоже надо чинить. max_connections на 2 000 съест память (каждое соединение в Postgres — отдельный процесс), кэш не лечит нехватку "
    "соединений, реплика не примет запись, а ещё больше подов — ещё больше соединений.",
    hints=[
        "Умножь поды на воркеры и на размер пула — и сравни с max_connections.",
        "База загружена на 25% и выполняет 18 запросов. Чего не хватает на самом деле — CPU или соединений?",
    ],
)


# =====================================================================================
# fs-release — Релиз без простоя
# =====================================================================================
theory("fs-release", r"""
Во время релиза одновременно живут разные версии: при rolling-деплое часть подов уже новые, часть старые; у пользователей открыты вкладки со старым JS на несколько дней; мобилка обновляется неделями. А база одна на всех. Главное правило: каждое изменение должно работать в паре с предыдущей версией соседа.

Expand/contract для схемы. Несовместимое изменение (переименовать колонку, сменить тип, разбить таблицу) делают за несколько релизов:
• Expand: добавить новое рядом со старым — nullable-колонку, новую таблицу. Старый код ничего не замечает.
• Двойная запись: новая версия пишет в обе колонки; строки, которые ещё пишут старые поды, подтягивает триггер или повторный бэкфилл.
• Бэкфилл: заполнить старые строки пачками по 1–10 тысяч, чтобы не держать долгие блокировки и не раздувать WAL.
• Переключить чтение на новую колонку.
• Contract: когда старых версий не осталось нигде, перестать писать в старое и удалить его отдельной миграцией.

Опасные миграции в PostgreSQL:
• RENAME COLUMN и DROP COLUMN мгновенно ломают поды, которые ещё не обновились;
• ADD COLUMN ... NOT NULL без DEFAULT падает на непустой таблице; DEFAULT с константой с PG 11 быстрый, с volatile-выражением переписывает таблицу;
• CREATE INDEX без CONCURRENTLY блокирует запись на время построения; CONCURRENTLY нельзя внутри транзакции (в Alembic — autocommit_block);
• ALTER COLUMN TYPE обычно переписывает таблицу под эксклюзивной блокировкой;
• любая DDL ждёт свою блокировку, а за ней в очередь встают все запросы к таблице — ставь SET lock_timeout = '5s' и повторяй.

    op.add_column("customers", sa.Column("phone_e164", sa.String(20), nullable=True))
    with op.get_context().autocommit_block():
        op.create_index("ix_customers_phone_e164", "customers", ["phone_e164"],
                        postgresql_concurrently=True)

Порядок выкладки:
• Новое поле или эндпоинт: миграция → бэк (умеет и старое, и новое) → фронт.
• Удаление: сначала фронт и мобилки перестают использовать → ждём, пока старые версии уйдут (смотри метрики по версии клиента) → бэк удаляет → миграция удаляет колонку.
• Миграции запускаются до выкатки кода (Job или init-шаг) и обязаны быть совместимы с кодом, который сейчас на проде. В CI это проверяют линтером миграций (squawk) и тестом «старая версия приложения против новой схемы».

Фронт при релизе:
• Ассеты с хешем в имени (index-3f2a9c.js) кэшируй навсегда, а index.html — no-cache, чтобы браузер узнал о новой сборке.
• Не удаляй ассеты прошлых сборок сразу: открытая вкладка лениво подгрузит старый чанк. Если его нет, SPA-fallback отдаст index.html вместо JS, и модуль не загрузится. Для /assets/ — честный 404 (try_files $uri =404), а на фронте ловят vite:preloadError и перезагружают страницу.

Откат. Откатить код легко, только если миграция обратно совместима. Down-миграции на проде с данными почти не запускают — идут вперёд исправляющим релизом. Самый быстрый откат фичи — выключить флаг.
""")

task(
    "fs-release-01", "find_bug", 4, "qa",
    "Ира: «После каждого релиза у части пользователей не открывается оформление заказа: белый экран, помогает только F5. Воспроизвела: "
    "открыла сайт до релиза, дождалась выкладки, нажала „Оформить“».",
    "Почему после релиза не загружается страница оформления и как это исправить?",
    code_=r"""
# Консоль браузера (вкладка открыта до релиза, после релиза нажали «Оформить»)
Failed to load module script: Expected a JavaScript module script but the server responded
with a MIME type of "text/html". Strict MIME type checking is enforced for module scripts per HTML spec.
TypeError: Failed to fetch dynamically imported module: https://market.example/assets/Checkout-3f2a9c.js

# Network
GET /assets/Checkout-3f2a9c.js   200   text/html   1.2 kB

# Dockerfile фронта: каждый релиз — новый образ, внутри только текущая сборка
FROM nginx:1.28-alpine
COPY dist/ /usr/share/nginx/html/
COPY nginx.conf /etc/nginx/conf.d/default.conf

# nginx.conf
server {
    listen 80;
    root /usr/share/nginx/html;
    location / {
        try_files $uri $uri/ /index.html;
    }
}
""",
    language="text",
    options=[
        "В nginx неправильный MIME-тип для .js — добавить types { application/javascript js; }, чтобы модули отдавались с правильным Content-Type",
        "Браузер закэшировал старый index.html — запретить кэширование всех файлов, включая /assets/, чтобы пользователи всегда получали свежую сборку",
        "Старой вкладке нужен чанк прошлой сборки, а SPA-fallback отдаёт index.html: для /assets/ — 404, хранить старые ассеты",
        "В новой сборке ошибка в коде Checkout — откатить релиз фронта и разобраться, почему чанк не собрался",
        "Динамическому import() не хватает CORS-заголовков — добавить для /assets/ Access-Control-Allow-Origin",
    ],
    answer=2,
    explanation="Вкладка, открытая до релиза, держит старый index.html и лениво подгружает чанк старой сборки — Checkout-3f2a9c.js. "
    "В новом образе его нет, а try_files ... /index.html на любой несуществующий путь отдаёт index.html с кодом 200 и типом text/html. "
    "Браузер отказывается исполнять HTML как модуль — белый экран. Отсутствующий ассет должен быть честным 404, старые ассеты нужно хранить "
    "хотя бы несколько релизов (не только внутри образа), а фронт ловит событие vite:preloadError и перезагружает страницу. MIME-типы настроены "
    "верно — это просто не тот файл; запрет кэша не поможет уже открытой вкладке; CORS для своего домена не нужен, а откат не поможет — "
    "это повторится на каждом релизе.",
    hints=[
        "Посмотри в Network: какой Content-Type пришёл на запрос .js-файла и почему статус 200?",
        "Есть ли файл Checkout-3f2a9c.js в новом образе?",
    ],
)

task(
    "fs-release-02", "code_review", 4, "teamlead",
    "Марина: «Миграция к фиче „телефон в формате E.164 и согласие на рассылку“. customers — 14 млн строк, API раскатывается rolling-ом по 10 подам, "
    "миграцию Job запускает перед выкаткой. Код приложения в этом же релизе уже читает phone_e164. Что не пропустим?»",
    "Выбери все замечания, которые нужно исправить до мержа.",
    code_=r"""
\"\"\"0157: phone -> phone_e164, согласие на рассылку, индекс по телефону\"\"\"
from alembic import op
import sqlalchemy as sa


def upgrade():
    op.alter_column("customers", "phone", new_column_name="phone_e164")
    op.add_column(
        "customers",
        sa.Column("marketing_consent", sa.Boolean(), nullable=False),
    )
    op.create_index("ix_customers_phone_e164", "customers", ["phone_e164"])
    op.execute("UPDATE customers SET phone_e164 = normalize_phone(phone_e164)")


def downgrade():
    op.drop_index("ix_customers_phone_e164")
    op.drop_column("customers", "marketing_consent")
    op.alter_column("customers", "phone_e164", new_column_name="phone")
""".replace('\\"', '"'),
    language="python",
    options=[
        "RENAME колонки сломает старые поды, которые во время rolling-деплоя читают phone, — нужен expand/contract",
        "downgrade лучше удалить: откатов схемы на проде всё равно не бывает, а непроверенный код только путает",
        "NOT NULL без server_default упадёт на непустой таблице, а старые поды не смогут вставлять строки",
        "Индекс лучше назвать idx_phone — короче и совпадает со стилем остальных индексов в проекте",
        "Индекс без CONCURRENTLY на 14 млн строк заблокирует запись в customers на минуты",
        "UPDATE всей таблицы одной транзакцией держит блокировки и раздувает WAL — бэкфилл пачками отдельным джобом",
        "Такие миграции надёжнее выполнять руками через psql на проде — так DBA всё проконтролирует",
    ],
    answer=[0, 2, 4, 5],
    explanation="Миграция запускается до выкатки и 10–20 минут живёт вместе со старыми подами, значит, обязана быть с ними совместима. RENAME "
    "мгновенно ломает все старые поды: они делают SELECT phone. NOT NULL без server_default упадёт на непустой таблице, а если бы и прошёл — "
    "старые поды не смогли бы вставлять строки. CREATE INDEX без CONCURRENTLY на 14 млн строк блокирует запись в customers, а UPDATE всей "
    "таблицы одной транзакцией держит блокировки, раздувает таблицу и WAL, отстают реплики. Правильно: nullable phone_e164 и marketing_consent "
    "с server_default в этом релизе, индекс CONCURRENTLY, бэкфилл пачками отдельным джобом, переключение чтения и удаление phone — в следующих "
    "релизах. downgrade держать не вредно, имя индекса — вкусовщина, а ручные правки через psql — путь к расхождению схем между окружениями.",
    hints=[
        "Какой код работает с базой в те 10–20 минут, пока идёт выкатка?",
        "Какие из этих операций берут долгую блокировку на таблицу в 14 млн строк?",
    ],
)

task(
    "fs-release-03", "write_code", 5, "teamlead",
    "Гена: «Переименовываем customers.name в full_name без простоя. Во время rolling-деплоя старые поды пишут только name, новые — обе колонки. "
    "Сделай expand-шаг так, чтобы после выкатки full_name был правильным у всех строк и мы могли переключить чтение».",
    "Допиши expand-миграцию (SQLite; в Postgres логика та же, только триггер на plpgsql): 1) добавь nullable-колонку full_name; 2) заполни её "
    "у существующих строк; 3) создай триггеры: если строку вставила или переименовала старая версия (full_name она не трогает), full_name "
    "берётся из name; если новая версия сама записала full_name, триггер его не перетирает. Код приложения ниже миграции не меняй — "
    "он имитирует старые и новые поды; проверяется последний SELECT.",
    code_=r"""
CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT);
INSERT INTO customers (id, name, phone) VALUES
  (1, 'Нина Батонова', '+79990000001'),
  (2, 'Артур Жмыхов', '+79990000002');

-- ===== expand-миграция: твой код ниже =====


-- ===== дальше работает приложение, не меняй =====
-- старая версия бэка (ещё крутится на части подов) знает только name
INSERT INTO customers (id, name, phone) VALUES (3, 'Вера Высоцкая', '+79990000003');
UPDATE customers SET name = 'Нина Хлебова' WHERE id = 1;
-- новая версия бэка пишет обе колонки
INSERT INTO customers (id, name, full_name, phone) VALUES (4, 'Ира', 'Ира Тестова', '+79990000004');
UPDATE customers SET name = 'Ирина', full_name = 'Ирина Тестова' WHERE id = 4;
INSERT INTO customers (id, name, full_name, phone) VALUES (5, 'Дима', 'Дмитрий Девопсов', '+79990000005');

SELECT id, name, full_name FROM customers ORDER BY id;
""",
    language="sql",
    answer=code(r"""
CREATE TABLE customers (id INTEGER PRIMARY KEY, name TEXT NOT NULL, phone TEXT);
INSERT INTO customers (id, name, phone) VALUES
  (1, 'Нина Батонова', '+79990000001'),
  (2, 'Артур Жмыхов', '+79990000002');

-- ===== expand-миграция: твой код ниже =====
ALTER TABLE customers ADD COLUMN full_name TEXT;

UPDATE customers SET full_name = name WHERE full_name IS NULL;

-- старая версия вставляет строку без full_name
CREATE TRIGGER customers_full_name_ins AFTER INSERT ON customers
WHEN NEW.full_name IS NULL
BEGIN
  UPDATE customers SET full_name = NEW.name WHERE id = NEW.id;
END;

-- старая версия меняет name, а full_name не трогает
CREATE TRIGGER customers_full_name_upd AFTER UPDATE OF name ON customers
WHEN NEW.full_name IS OLD.full_name
BEGIN
  UPDATE customers SET full_name = NEW.name WHERE id = NEW.id;
END;

-- ===== дальше работает приложение, не меняй =====
-- старая версия бэка (ещё крутится на части подов) знает только name
INSERT INTO customers (id, name, phone) VALUES (3, 'Вера Высоцкая', '+79990000003');
UPDATE customers SET name = 'Нина Хлебова' WHERE id = 1;
-- новая версия бэка пишет обе колонки
INSERT INTO customers (id, name, full_name, phone) VALUES (4, 'Ира', 'Ира Тестова', '+79990000004');
UPDATE customers SET name = 'Ирина', full_name = 'Ирина Тестова' WHERE id = 4;
INSERT INTO customers (id, name, full_name, phone) VALUES (5, 'Дима', 'Дмитрий Девопсов', '+79990000005');

SELECT id, name, full_name FROM customers ORDER BY id;
"""),
    tests=[{"input": None, "expected": [
        [1, "Нина Хлебова", "Нина Хлебова"],
        [2, "Артур Жмыхов", "Артур Жмыхов"],
        [3, "Вера Высоцкая", "Вера Высоцкая"],
        [4, "Ирина", "Ирина Тестова"],
        [5, "Дима", "Дмитрий Девопсов"],
    ]}],
    explanation="Expand-шаг должен сделать новую колонку правдой, ничего не требуя от старого кода. ADD COLUMN без NOT NULL мгновенный и не мешает "
    "старым подам, UPDATE заполняет существующие строки (на проде — пачками). Но пока идёт rolling-деплой, старые поды продолжают вставлять "
    "и менять строки только через name, и одноразовый бэкфилл их не догонит — нужны триггеры двойной записи. Условие WHEN отличает старую версию "
    "от новой: full_name не пришёл при вставке или не изменился при обновлении name — значит, писал старый код, и мы копируем name; если новая "
    "версия записала full_name сама, триггер её не трогает. Когда старых подов не останется, чтение переключают на full_name, а триггеры "
    "и колонку name удаляют отдельным contract-релизом.",
    hints=[
        "SQLite: ALTER TABLE customers ADD COLUMN full_name TEXT; потом UPDATE для существующих строк.",
        "Нужны два триггера: AFTER INSERT и AFTER UPDATE OF name.",
        "Условия: WHEN NEW.full_name IS NULL для вставки и WHEN NEW.full_name IS OLD.full_name для обновления.",
    ],
)

task(
    "fs-release-04", "incident", 5, "devops_colleague",
    "Дима: «Идёт выкатка API, 3 из 10 подов уже новые — и 70% запросов к заказам падают с 500. Миграцию Job прогнал перед выкаткой, она зелёная. "
    "Бэкендер кричит: „Срочно kubectl rollout undo!“ Что делаем?»",
    "Выбери все правильные действия — сейчас и после инцидента.",
    code_=r"""
$ kubectl rollout status deploy/market-api
Waiting for deployment "market-api" rollout to finish: 3 of 10 updated replicas are available...

$ kubectl logs market-api-6d9f7b8c4-q2wkp      # старый под
sqlalchemy.exc.ProgrammingError: (psycopg.errors.UndefinedColumn) column orders.address does not exist
LINE 1: SELECT orders.id, orders.address, orders.status, orders.total_minor FROM orders WHERE ...

$ kubectl logs market-api-7f4c9d6b2-m8xzt      # новый под
INFO "GET /api/orders/5512" 200 18ms

$ kubectl logs job/market-migrate-0158
INFO  [alembic.runtime.migration] Running upgrade 0157 -> 0158, rename orders.address to delivery_address
""",
    language="text",
    options=[
        "kubectl rollout undo — вернуть старую версию на все поды, раз ошибки начались именно с этой выкатки",
        "Не откатывать код, а довести выкатку: новая версия совместима с новой схемой — ускорить rollout и следить",
        "Срочно выполнить alembic downgrade, чтобы вернуть колонку address, и только потом разбираться",
        "В постмортеме: переименования только через expand/contract, а в CI — линтер миграций (например, squawk)",
        "Перезапустить старые поды — у SQLAlchemy устарел кэш схемы, после рестарта они увидят новую колонку",
        "В постмортеме: в CI прогонять тесты старой версии приложения против схемы после новой миграции",
    ],
    answer=[1, 3, 5],
    explanation="Миграция переименовала колонку до того, как обновились поды, — старая версия ищет orders.address и падает. rollout undo сделает "
    "все 10 подов старыми, и ошибок станет 100%: схема-то уже новая. downgrade под нагрузкой — ещё одна DDL с блокировкой, которая к тому же "
    "сломает три новых пода. Самое быстрое смягчение — доехать вперёд: новая версия совместима с новой схемой, ускорь выкатку и следи за ошибками. "
    "Дальше — чтобы не повторилось: переименования только через expand/contract, линтер миграций в CI и тест совместимости «старый код против "
    "новой схемы». Кэш схемы ни при чём — колонки просто нет.",
    hints=[
        "С какой схемой совместима старая версия, а с какой — новая?",
        "Что станет с ошибками, если все 10 подов окажутся старыми?",
    ],
    time_limit=15,
)


# =====================================================================================
# fs-incident-e2e — Инцидент через весь стек
# =====================================================================================
theory("fs-incident-e2e", r"""
Самые долгие инциденты — те, где каждый слой по отдельности «работает»: бэк отвечает 200, nginx зелёный, у разработчика в консоли чисто. Причина на стыке, и найти её помогает метод, а не угадывание.

Шаг 1. Масштаб и отличие. Кто страдает: все или часть? Какая часть — браузер, регион, залогиненные, давние пользователи, один под? Что отличает сломанный запрос от рабочего: заголовки, размер, куки, путь, время?

Шаг 2. Пройди запрос по цепочке и найди, где он пропадает или меняется:
• браузер: DevTools → Network (статус, заголовки запроса и ответа, размер), Console, Copy as cURL для воспроизведения;
• CDN: заголовки X-Cache или CF-Cache-Status и Age — ответ пришёл из кэша или от сервера;
• nginx или ingress: access log (статус, $request_time, $upstream_status) и error log. Коды, которые рождаются на прокси: 400 (слишком большие заголовки), 413 (тело больше client_max_body_size), 499 (клиент ушёл, не дождавшись ответа), 502 (бэк недоступен или ответил мусором), 504 (таймаут бэка);
• приложение: логи по request id; если запроса в логах бэка нет — он умер раньше;
• база, Redis, очередь, внешние API.

Шаг 3. Correlation id. nginx генерирует $request_id и передаёт его в X-Request-ID, бэк пишет его в каждую строку лога и возвращает клиенту, фронт показывает его в тексте ошибки. Тогда жалоба «ничего не работает» превращается в поиск одной строки.

    proxy_set_header X-Request-ID $request_id;
    log_format main '$remote_addr "$request" $status rt=$request_time urt=$upstream_response_time rid=$request_id';

Классика стыков:
• nginx теряет заголовки: имена с подчёркиванием молча отбрасываются (underscores_in_headers off по умолчанию), а proxy_set_header внутри location отменяет все proxy_set_header уровня server — пропадают Host и X-Forwarded-*.
• Кэш не для того: CDN или прокси кэширует персональный ответ, потому что Cache-Control это разрешает. Всё, что зависит от пользователя, — Cache-Control: private, no-store.
• Цепочка таймаутов и ретраев: фронт ждёт 10 с и повторяет POST, бэк работает 12 с и не знает, что клиент ушёл, — заказ создаётся трижды. Ретраи только для идемпотентных запросов или с Idempotency-Key.
• Разрастание: куки и JWT растут, и в какой-то момент заголовок не влезает в буфер прокси (large_client_header_buffers 4 8k по умолчанию).
• Часовые пояса и форматы: браузер шлёт локальное время, сервер считает в UTC.

Шаг 4. Сначала смягчи, потом чини: откат, выключение флага, очистка кэша, временное поднятие лимита. Исправление причины — отдельным спокойным релизом. Если затронуты чужие персональные данные, это ещё и инцидент безопасности: подключай безопасников и юристов сразу — по 152-ФЗ об утечке уведомляют Роскомнадзор в течение 24 часов.

Шаг 5. Blameless-постмортем: хронология, причина, почему не поймали раньше, действия с владельцами и сроками — тест, алерт, линтер конфигов, чтобы не повторился весь класс ошибок.
""")

task(
    "fs-incident-e2e-01", "incident", 4, "client",
    "Вера, управляющая бизнес-центра «Высота»: «Мои администраторы не могут зайти в систему бронирования переговорок — белая страница "
    "„400 Bad Request“. У меня на ноутбуке всё работает, у новых сотрудников тоже. Не работает у тех, кто пользуется системой с весны».",
    "Почему давние пользователи получают 400 и что с этим делать?",
    code_=r"""
# У администратора: GET https://booking.vysota.example/
Status: 400 Bad Request
<html><head><title>400 Request Header Or Cookie Too Large</title></head>
<body><center><h1>400 Bad Request</h1></center>
<center>Request Header Or Cookie Too Large</center><hr><center>nginx</center></body></html>

# Размер заголовка Cookie у разных людей, байт
  новый сотрудник       1 214
  Вера                  3 870
  администратор Олег    9 402

# Из чего состоит кука Олега
  access_token    4 010   JWT, в payload — права на все 212 переговорок
  recent_filters  3 950   сохранённые фильтры поиска, JSON, растёт с каждым поиском
  _ym_*, _ga*     1 442   аналитика

# В логах бэка запросов от Олега нет
""",
    language="text",
    options=[
        "У давних сотрудников истёк JWT, и бэк отвечает 400 на невалидный токен — продлить срок жизни токенов и попросить всех перелогиниться",
        "Администраторы открывают систему по старой ссылке с другого домена, и браузер блокирует запрос — не хватает CORS",
        "nginx упирается в client_max_body_size: у давних пользователей запросы тяжелее — поднять лимит до 10m в конфиге ингресса",
        "Cookie давних пользователей больше 8 КБ, и nginx отбивает запрос: временно поднять буферы, а по сути — ужать куки и JWT",
        "Попросить администраторов очистить кэш и куки браузера — после этого всё заработает, больше ничего не нужно",
    ],
    answer=3,
    explanation="Страница ошибки подписана nginx, а в логах бэка этих запросов нет — значит, запрос умер на прокси. Текст Request Header Or Cookie "
    "Too Large и размеры кук всё объясняют: по умолчанию заголовок запроса должен влезать в буферы large_client_header_buffers 4 8k, а у Олега "
    "одна строка Cookie — 9,4 КБ. Кука растёт у давних пользователей: фильтры копятся с каждым поиском, а JWT таскает права на 212 переговорок. "
    "Поднять буфер (в ingress — large-client-header-buffers в ConfigMap) можно, чтобы люди работали сегодня, но настоящая починка — на стыке "
    "фронта и бэка: фильтры в localStorage или на сервер, в токене только id и роль, права проверять на сервере. Кука уходит с каждым запросом, "
    "и каждый её байт умножается на число запросов. Очистка кэша поможет на неделю, client_max_body_size отвечает за тело (ошибка 413), а у GET "
    "тела нет.",
    hints=[
        "Кто сгенерировал страницу ошибки — бэк или прокси?",
        "Чем кука давнего пользователя отличается от куки нового сотрудника?",
    ],
    time_limit=20,
)

task(
    "fs-incident-e2e-02", "find_bug", 4, "devops_colleague",
    "Дима: «Вчера я добавил в nginx проброс X-Request-ID для трейсинга. Сегодня вход через Яндекс ID сломан: провайдер ругается на redirect_uri. "
    "А ещё в логах бэка какие-то редиректы на внутренний хост. Остальное вроде работает».",
    "Найди, что сломала вчерашняя правка, и выбери правильное исправление.",
    code_=r"""
server {
    listen 443 ssl;
    server_name market.example;

    proxy_set_header Host              $host;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;

    location /api/ {
        proxy_pass http://market_api;
        proxy_set_header X-Request-ID $request_id;    # добавлено вчера
    }

    location / {
        root /usr/share/nginx/html;
        try_files $uri /index.html;
    }
}

# Яндекс ID: redirect_uri=http://market_api/api/auth/yandex/callback не совпадает с зарегистрированным
# лог бэка (uvicorn --proxy-headers):
#   "GET /api/orders HTTP/1.1" 307 Temporary Redirect   location: http://market_api/api/orders/
""",
    language="nginx",
    options=[
        "В имени X-Request-ID есть дефисы, а nginx по умолчанию отбрасывает нестандартные заголовки — включить underscores_in_headers on и ignore_invalid_headers off",
        "В proxy_pass не хватает слэша в конце: http://market_api/ — без него nginx передаёт бэку неправильный путь, и тот отвечает редиректом",
        "proxy_set_header в location отменил заголовки уровня server: Host и X-Forwarded-Proto пропали — повторить их в location",
        "$request_id доступен только в HTTP/2, а к бэку nginx ходит по HTTP/1.x — выключить http2 на listen",
        "Яндекс ID поменял API на своей стороне — обновить библиотеку OAuth на бэке и перерегистрировать redirect_uri",
    ],
    answer=2,
    explanation="У proxy_set_header неочевидное наследование: директивы уровня server действуют в location, только если в самом location нет "
    "ни одной proxy_set_header. Вчерашняя строка с X-Request-ID молча отменила Host, X-Forwarded-For и X-Forwarded-Proto. nginx подставил Host "
    "по умолчанию — имя апстрима market_api, о https бэк больше не знает, и все абсолютные ссылки (redirect_uri для OAuth, редиректы на слэш) "
    "строятся как http://market_api/... Лечится повтором всех заголовков в location или общим include-файлом, который подключают в каждый "
    "location. Дефисы в именах заголовков нормальны — nginx отбрасывает имена с подчёркиваниями, а слэш в proxy_pass срезал бы префикс /api/ "
    "и сломал все пути.",
    hints=[
        "Откуда в ссылках взялось имя апстрима market_api вместо market.example?",
        "Наследуются ли proxy_set_header с уровня server, если в location есть своя proxy_set_header?",
    ],
)

task(
    "fs-incident-e2e-03", "incident", 5, "qa",
    "Ира: «Срочно! Клиентка пишет в поддержку: открыла корзину, а там чужие товары и чужой адрес доставки. Воспроизвела с двух аккаунтов — "
    "иногда вижу корзину второго. Час назад Дима включил CDN для /api/, чтобы разгрузить каталог».",
    "Выбери все правильные действия — и срочные, и для исправления причины.",
    code_=r"""
$ curl -sI https://market.example/api/cart -H "Cookie: session=IRA_SESSION"
HTTP/2 200
content-type: application/json
cache-control: public, max-age=60
x-cache: HIT
age: 41
x-request-id: 5c1e9a0b7d

# Правило CDN (добавлено в 13:02)
path: /api/*    cache: respect origin Cache-Control    cache key: host + path + query

# Middleware бэка (добавлено неделю назад, «чтобы браузер кэшировал каталог»)
response.headers.setdefault("Cache-Control", "public, max-age=60")
""",
    language="text",
    options=[
        "Сразу выключить кэширование /api/* на CDN (bypass) и сделать purge кэша",
        "Добавить Vary: User-Agent — тогда CDN будет хранить разные копии ответа для разных пользователей",
        "На персональных ответах — Cache-Control: private, no-store; публичный кэш — только явным списком эндпоинтов",
        "Перезапустить поды API, чтобы сбросить закэшированные ответы, и проверить корзину ещё раз",
        "Считать это утечкой персональных данных: подключить безопасность и юристов, выяснить, чьи данные видели",
        "Увеличить max-age до 3600 — CDN будет реже ходить на бэк, и путаницы между сессиями станет меньше",
    ],
    answer=[0, 2, 4],
    explanation="x-cache: HIT и age: 41 говорят, что ответ отдал CDN из кэша, а не бэк. Ключ кэша — хост, путь и query, куки в нём нет, "
    "поэтому первая закэшированная корзина уходит всем на 60 секунд. Бомба была заложена неделю назад: middleware ставит public, max-age=60 "
    "на все ответы подряд, а CDN, который уважает Cache-Control, её взорвал. Первым делом — остановить утечку (bypass и purge), затем чинить "
    "заголовки: персональное — private, no-store, публичный кэш — только явно перечисленным эндпоинтам. И это инцидент безопасности: чужие "
    "адреса — персональные данные, по 152-ФЗ об утечке уведомляют Роскомнадзор в течение 24 часов. Vary: User-Agent не разделит пользователей, "
    "перезапуск подов не очистит CDN, а больший max-age сделает только хуже.",
    hints=[
        "Кто на самом деле ответил на запрос — бэк или CDN? Посмотри на x-cache и age.",
        "Что входит в ключ кэша и чего в нём не хватает для корзины?",
    ],
    time_limit=15,
)

task(
    "fs-incident-e2e-04", "incident", 5, "client",
    "Нина, владелица пекарни «Батон»: «Сегодня утром три клиента пожаловались, что с них списали деньги за заказ три раза! И в админке по три "
    "одинаковых заказа. Что у вас там происходит?»",
    "Выбери все изменения, которые нужно сделать, чтобы это не повторилось.",
    code_=r"""
# nginx access.log, один клиент (время — окончание запроса)
09:14:13 "POST /api/orders" 499 rt=10.001 urt=10.001 rid=a1f0c2
09:14:23 "POST /api/orders" 499 rt=10.002 urt=10.000 rid=b7c2d9
09:14:33 "POST /api/orders" 499 rt=10.001 urt=10.001 rid=c90e41

# логи бэка по тем же rid
a1f0c2  order 5512 created, payment captured (12.4 s)
b7c2d9  order 5513 created, payment captured (12.3 s)
c90e41  order 5514 created, payment captured (11.9 s)

# POST /api/orders синхронно создаёт заказ и списывает оплату у провайдера
# задержка провайдера сегодня: p95 = 11.8 s (обычно 0.6 s)

# frontend/src/api/http.ts
const http = axios.create({ timeout: 10_000 });
axiosRetry(http, { retries: 2, retryCondition: (e) => e.code === "ECONNABORTED" });
""",
    language="text",
    options=[
        "Поднять timeout в axios до 60 секунд — провайдер успеет ответить, и повторов по таймауту не будет",
        "Фронт: не повторять POST по таймауту вслепую; на попытку оформления генерировать Idempotency-Key и слать его",
        "Раз в 5 минут удалять кроном дубли — заказы одного клиента на одинаковую сумму — и возвращать за них деньги",
        "Бэк: хранить Idempotency-Key под уникальным индексом (user_id, key) и на повтор отдавать уже созданный заказ",
        "Включить в nginx proxy_ignore_client_abort off, чтобы бэк прекращал обработку, когда клиент отключился",
        "Не держать HTTP-запрос на время оплаты: заказ в pending, оплата асинхронно, статус — опросом или SSE",
    ],
    answer=[1, 3, 5],
    explanation="499 в nginx — клиент закрыл соединение, не дождавшись ответа, но бэк об этом не знает и доводит заказ и списание до конца. "
    "Фронт ждёт 10 секунд, провайдер сегодня отвечает 12, а axios-retry, которому разрешили повторять по таймауту любой запрос, отправляет POST "
    "ещё дважды — три заказа и три списания. Повтор неидемпотентного запроса безопасен только с Idempotency-Key: фронт генерирует ключ на попытку "
    "оформления, бэк хранит его под уникальным индексом и на повтор отдаёт уже созданный заказ, а провайдер по тому же ключу не спишет деньги "
    "второй раз. И не стоит держать HTTP-запрос на время оплаты: заказ в pending, оплата асинхронно, статус — опросом или SSE. Больший таймаут "
    "только отодвигает проблему, proxy_ignore_client_abort off — и так значение по умолчанию (nginx рвёт соединение с бэком, но уже начатую "
    "обработку это не останавливает), а удаление «дублей» по сумме снесёт и настоящие одинаковые заказы и лечит последствия вместо причины.",
    hints=[
        "Что значит статус 499 и что при этом делает бэк?",
        "Как сервер может понять, что второй POST — это повтор первого, а не новый заказ?",
    ],
    time_limit=20,
)


# =====================================================================================
# fs-system-design — Системный дизайн: итоговое ревью
# =====================================================================================
theory("fs-system-design", r"""
Системный дизайн — умение за час пройти путь от «сделайте нам сервис» до схемы, которую не стыдно защищать на архитектурном ревью. Порядок всегда один.

1. Требования. Функциональные: что делает пользователь, какие сценарии главные. Нефункциональные: сколько пользователей и запросов, пики, допустимая задержка (p99), доступность (99,9% — это около 43 минут простоя в месяц), что важнее при сбое — согласованность или доступность, сколько хранить данные, требования закона к персональным данным.

2. Оценка на салфетке:
• 1 млн активных в день × 20 запросов = 20 млн в сутки ≈ 230 RPS в среднем; пик в 5–10 раз больше.
• Соотношение чтения и записи: каталог 100:1, чат 1:1 — от него зависит, помогут ли кэш и реплики.
• Объём: 50 тыс. заказов в день × 2 КБ × 365 ≈ 36 ГБ в год — это один Postgres, а не «big data».
• Файлы считаются отдельно: 10 тыс. фото в день × 2 МБ ≈ 7 ТБ в год — только объектное хранилище.

3. API и модель данных: ключевые сущности и связи, по каким полям ищем (будущие индексы), что должно быть уникальным и транзакционным.

4. Хранилища — по характеру данных:
• PostgreSQL — выбор по умолчанию для денег, заказов, пользователей: транзакции, ограничения, JOIN.
• Redis — кэш, сессии, счётчики, rate limit, короткие блокировки. Всё, что в нём, можно потерять.
• S3-совместимое хранилище + CDN — файлы и картинки; в базе только ключ и метаданные.
• Поиск (OpenSearch) и аналитика (ClickHouse) — отдельные копии данных под свои запросы.

5. Масштабирование чтения: CDN для публичного, кэш (cache-aside с TTL и инвалидацией), реплики базы. Реплика отстаёт, поэтому тот, кто только что изменил данные, читает с primary (read-your-writes). Шардирование — последнее средство.

6. Запись и асинхронность. Всё, что не нужно пользователю прямо сейчас (письма, превью, интеграции), — в очередь. Событие публикуют через outbox: запись в той же транзакции, что и бизнес-данные, отдельный процесс отправляет её в брокер. Доставка at-least-once, поэтому обработчики идемпотентны. Конкурентные операции над общим ресурсом (остатки, места) — атомарно в одном месте: условный UPDATE или DECR, а не «прочитал — проверил — записал».

7. Отказоустойчивость: нет единой точки отказа (минимум две реплики в разных зонах), таймауты на каждом внешнем вызове, ретраи с backoff только для идемпотентного, circuit breaker, деградация (страница без рекомендаций лучше, чем 500), очереди ожидания и rate limit на пиках, сверка с внешними системами на случай потерянных событий, бэкапы с проверенным восстановлением.

8. Наблюдаемость и стоимость: какие метрики и алерты скажут о проблеме раньше пользователей и сколько всё это стоит в месяц.

На ревью защищай компромиссы: почему выбрал это и что станет узким местом при росте в 10 раз. «Сделаем микросервисы на Kafka» без цифр — красный флаг; простая схема, которая с запасом выдерживает посчитанную нагрузку, — зрелое решение.
""")

task(
    "fs-system-design-01", "estimation", 4, "client",
    "Артур, директор фитнес-клуба «Жми»: «Мы открываем ещё 30 клубов по стране. Хочу единую запись на тренировки: сайт, приложение, "
    "и чтобы тренеры видели свои группы. Сколько будет стоить и за сколько сделаете?»",
    "Перед проектированием нужно понять требования. Выбери вопросы, ответы на которые сильнее всего меняют архитектуру и оценку.",
    options=[
        "Сколько клиентов и записей в день и есть ли пики — например, все записываются в понедельник в 9:00?",
        "В какой цвет красить кнопку „Записаться“ — в фирменный красный или нейтральный, как на сайте клуба?",
        "Что происходит, когда группа заполнена: лист ожидания, автозапись при отмене, штрафы за неявку?",
        "С чем интегрироваться: текущая CRM и абонементы, онлайн-оплата, турникеты, 1С?",
        "На каком языке писал прошлый подрядчик и можно ли переиспользовать его вёрстку?",
        "Насколько критичен простой: что будет, если запись недоступна час в понедельник утром?",
        "Нужна ли в приложении тёмная тема и своя иконка для каждого из 30 клубов?",
    ],
    answer=[0, 2, 3, 5],
    explanation="Архитектуру определяют нагрузка, правила конкуренции за места, интеграции и цена простоя. «Все записываются в понедельник "
    "в 9:00» — это пик в сотни запросов в секунду за одни и те же 20 мест: нужны атомарное бронирование, очередь ожидания и статическая витрина "
    "через CDN. Лист ожидания и штрафы — это фоновые задачи, уведомления и деньги. Интеграции с CRM, оплатой и турникетами часто съедают половину "
    "проекта, а требование к доступности решает, сколько реплик и зон нужно и нужен ли дежурный. Прикидка: 30 клубов × 2 000 клиентов × 3 записи "
    "в неделю ≈ 26 тысяч записей в день — один Postgres с запасом, сложность в пиках, а не в объёме. MVP (сайт, API, кабинет тренера) — 2–3 месяца "
    "командой из 3–4 человек, мобильное приложение и интеграции оцениваются отдельно после ответов.",
    hints=[
        "Какие ответы меняют схему, а не только экраны?",
        "Подумай о самом тяжёлом моменте недели для такой системы.",
    ],
    time_limit=15,
)

task(
    "fs-system-design-02", "architecture", 4, "teamlead",
    "Гена: «Архитектурное ревью каталога Маркета. 3 000 RPS в пик, почти всё — чтение. Primary Postgres на 80% CPU, реплик нет. "
    "Предложи план на ближайший квартал — и защити его цифрами».",
    "Какой план разгрузки базы каталога выбрать?",
    code_=r"""
Нагрузка в пик:
  GET /api/products/{id}          1 900 RPS   карточка, одинаковая для всех
  GET /api/products?category=...    900 RPS   листинги с фильтрами
  POST/PATCH (корзина, заказы)      100 RPS
Postgres primary: 16 vCPU, CPU 80%; топ запросов по времени — карточка и листинг
Изменения товаров: ~5 000 в день (админка и импорт цен и остатков из 1С)
""",
    language="text",
    options=[
        "Шардировать базу по product_id на 4 инстанса — нагрузка на чтение и запись разделится поровну, и запаса хватит на годы вперёд",
        "Карточки — в Redis (cache-aside, инвалидация по событию), публичное — через CDN, листинги — на реплику",
        "Перенести каталог в MongoDB — документная модель лучше подходит для карточек и быстрее на чтение",
        "Увеличить primary до 64 vCPU и ничего не менять в коде — самый быстрый способ получить запас на квартал",
        "Отдавать все ответы API с Cache-Control: max-age=86400, чтобы браузеры не ходили за ними сутки",
    ],
    answer=1,
    explanation="Нагрузка — почти чистое чтение одинаковых для всех данных, значит, выигрывает кэширование на каждом уровне. Карточки — идеальный "
    "кандидат для cache-aside: при 95% попаданий из 1 900 RPS в базу дойдёт около 100, инвалидация по событию держит цены свежими, а TTL страхует "
    "от потерянного события. Публичные ответы с коротким TTL на CDN снимают нагрузку ещё до API. Листинги с фильтрами кэшируются хуже — их "
    "отправляем на реплику, а то, что пользователь только что изменил (админка, корзина), читаем с primary, иначе из-за отставания реплики увидим "
    "старые данные. Шардирование и смена СУБД — месяцы работы ради проблемы, которую решают кэш и реплика; вертикальный рост покупает время, "
    "но остаётся единой точкой отказа; суточный кэш в браузере покажет устаревшие цены и остатки.",
    hints=[
        "Какая доля запросов — чтение одинаковых для всех данных?",
        "Что увидит администратор, если прочитает товар с реплики сразу после сохранения?",
    ],
)

task(
    "fs-system-design-03", "architecture", 5, "manager",
    "Стас: «Отзывы с фото, которые ты оценивал, одобрили. Ждём до 20 тысяч фото в день, с айфонов по 3–8 МБ в HEIC. Показывать нужно превью "
    "300×300 и большое фото 1280 px, на мобилках быстро. Как устроим загрузку и хранение?»",
    "Какую схему загрузки, обработки и хранения фото выбрать?",
    options=[
        "Фронт отправляет фото multipart-запросом в API, API сохраняет их в PostgreSQL (bytea) рядом с отзывом — одна транзакция и одни бэкапы — и отдаёт через эндпоинт",
        "Фото загружаются через API на PersistentVolume пода и отдаются nginx с того же тома — без внешних сервисов и лишних затрат",
        "Загрузка в S3 по presigned URL, но превью генерировать синхронно в запросе подтверждения, чтобы покупатель сразу увидел результат",
        "Presigned URL — загрузка прямо в S3; воркер из очереди конвертирует HEIC, вырезает EXIF и делает превью; раздача через CDN, в Postgres — только ключи и статус",
    ],
    answer=3,
    explanation="Посчитай: 20 тысяч фото × ~5 МБ ≈ 100 ГБ в день, около 36 ТБ в год. Через API это 100 ГБ трафика через поды и nginx с лимитами "
    "тела и таймаутами мобильного интернета, а в Postgres — раздутая база, бэкапы и репликация на десятки терабайт. Presigned URL отправляет байты "
    "мимо API прямо в объектное хранилище, а API только выдаёт разрешение и записывает метаданные. Конвертация HEIC и превью тяжелы по CPU "
    "и памяти — их место в воркере за очередью, где можно ретраить и масштабировать отдельно; синхронно в запросе они дадут таймауты и скачки "
    "CPU в API. Том пода не переживёт пересоздания и не масштабируется на несколько реплик. А EXIF вырезают ради приватности: в фото "
    "с телефона часто зашиты координаты дома покупателя.",
    hints=[
        "Умножь число фото на средний размер — сколько это в день и в год?",
        "Кому на самом деле нужно пропускать через себя байты фотографии?",
    ],
)

task(
    "fs-system-design-04", "architecture", 5, "manager",
    "Стас: «Чёрная пятница: в 12:00 продаём 1 000 умных ламп по 1 ₽. В прошлом году похожая акция положила сайт, а ещё мы продали 1 340 ламп "
    "из 1 000 — пришлось извиняться. Ждём 200 тысяч человек в первую минуту. Как сделать правильно?»",
    "Какую архитектуру акции выбрать, чтобы сайт выжил и не было оверселла?",
    code_=r"""
Как было в прошлом году:
  1) qty = SELECT stock FROM products WHERE id = 777
  2) if qty > 0:
         UPDATE products SET stock = :qty - 1 WHERE id = 777
         INSERT INTO orders (...)
  Страница акции рендерилась бэком на каждый запрос; 12 подов API, пул к базе — 300 соединений.
""",
    language="text",
    options=[
        "Обернуть шаги 1–2 в транзакцию с уровнем READ COMMITTED — тогда проверка и списание остатка станут атомарными",
        "Считать остаток на фронте и прятать кнопку «Купить», когда лампы закончились, — лишние запросы не дойдут до бэка",
        "Distributed lock в Redis на весь товар: каждый запрос берёт лок, читает остаток, пишет и отпускает лок",
        "Поднять пул соединений до 3 000 и число подов до 100 — в прошлом году просто не хватило мощности на пик",
        "CDN для статики, очередь ожидания и rate limit перед API, атомарный резерв (UPDATE … WHERE stock > 0) и бронь с TTL",
    ],
    answer=4,
    explanation="Проблемы прошлого года — перегрузка и оверселл — решаются на разных уровнях. «Прочитал остаток → записал остаток − 1» — гонка: "
    "сотни запросов читают одно значение и все продают, а READ COMMITTED (уровень по умолчанию) это не лечит. Атомарный условный UPDATE ... WHERE "
    "stock > 0 проверяет и уменьшает остаток одной операцией — продать больше 1 000 невозможно; Redis DECR даёт то же на ещё больших скоростях. "
    "Бронь с TTL возвращает лампы тех, кто не оплатил. От 200 тысяч человек защищают статика на CDN и очередь ожидания: в бэк проходит столько, "
    "сколько он выдерживает, остальные видят честное «вы в очереди». Глобальный лок пропускает запросы по одному и ломается при истечении лока, "
    "фронту нельзя доверять, а 3 000 соединений положат саму базу.",
    hints=[
        "Что увидят два запроса, которые одновременно выполнят шаг 1?",
        "Какая операция проверяет и уменьшает остаток за один шаг?",
        "Сколько из 200 тысяч запросов вообще должны дойти до базы?",
    ],
)

task(
    "fs-system-design-05", "architecture", 5, "client",
    "Нина, владелица пекарни «Батон»: «Клиенты оплатили, а в админке заказ „не оплачен“ — пекари его не пекут. Раз в пару дней такое. "
    "Ваш разработчик говорит, что „вебхук от банка потерялся“. Мне нужно, чтобы такого не было вообще».",
    "Как спроектировать приём оплаты надёжно?",
    code_=r"""
Сейчас:
  POST /webhooks/payment:
      проверить подпись -> найти заказ -> order.status = 'paid'
      -> отправить письмо через SMTP (2–8 с) -> commit -> 200
  Провайдер ретраит вебхук 3 раза с интервалом 1 минута, если не получил 2xx за 5 секунд
  После оплаты фронт показывает «Спасибо!» по редиректу с ?status=success
Сбои: деплой во время оплаты; медленный SMTP -> ответ дольше 5 с -> ретраи -> двойные письма, иногда заказ так и не отмечен
""",
    language="text",
    options=[
        "После редиректа с ?status=success фронт сам вызывает POST /api/orders/{id}/mark-paid — так статус обновится, даже если вебхук не дошёл",
        "Попросить банк увеличить таймаут вебхука до 60 секунд, чтобы медленный SMTP успевал отработать до ответа",
        "Вебхук идемпотентно пишет статус и outbox одной транзакцией и сразу отвечает 200; письма — воркер; крон сверяет pending",
        "Отправлять письмо до обновления статуса, чтобы клиент точно узнал об оплате, даже если дальше что-то упадёт",
        "Поднять второй под API и настроить PodDisruptionBudget, чтобы вебхук не терялся во время деплоя",
    ],
    answer=2,
    explanation="Вебхук — главный источник правды об оплате, но доставляется он «хотя бы раз» и без гарантий. Поэтому обработчик должен быть "
    "быстрым (ответить 200 за доли секунды, а медленный SMTP вынести в воркер через outbox — запись в той же транзакции, что и статус), "
    "идемпотентным (уникальность по id события: повторная доставка не шлёт второе письмо) и подстрахованным сверкой: если вебхук всё-таки "
    "потерялся — деплой, сбой сети, — крон сам спросит статус у провайдера. Редирект с ?status=success подделывается за секунду, поэтому статус "
    "оплаты ставит только сервер. Второй под полезен, но не спасает от медленного SMTP и потерь; просить банк о минутном таймауте — лечить "
    "симптом, а письмо до записи статуса — обещание, которое система может не выполнить.",
    hints=[
        "Что происходит с вебхуком, если SMTP отвечает 8 секунд, а провайдер ждёт 5?",
        "Что сделать, если вебхук не пришёл вовсе — у кого ещё можно узнать статус оплаты?",
    ],
)


# =====================================================================================
# Досчитываем тесты флагов по эталону и пишем файл
# =====================================================================================
def _fill_flag_tests():
    t = next(x for x in TASKS if x["task_id"] == "fs-feature-e2e-02")
    ns = {}
    exec(t["content"]["correct_answer"], ns)
    fn = ns["flags_for_user"]
    flags = {
        "new_checkout": {"enabled": True, "percent": 30},
        "favorites": {"enabled": True, "percent": 100},
        "promo": {"enabled": False, "percent": 100, "allow_users": [7]},
        "review_photos": {"enabled": True, "staff": True, "allow_users": [42]},
    }
    cases = [
        (flags, {"id": 7, "is_staff": False}),
        (flags, {"id": 42, "is_staff": False}),
        (flags, {"id": 1001, "is_staff": True}),
        (flags, {"id": 1003, "is_staff": False}),
        ({"dark_mode": {"percent": 50}, "beta_search": {}}, {"id": 5, "is_staff": True}),
        ({"dark_mode": {"percent": 50}, "beta_search": {}}, {"id": 512, "is_staff": False}),
    ]
    # граница: корзина ровно равна проценту — флаг должен быть выключен (bucket < percent)
    edge_user = {"id": 1003, "is_staff": False}
    edge_pct = ns["bucket"]("canary", edge_user["id"])
    cases.append(({"canary": {"percent": edge_pct}, "canary_next": {"percent": edge_pct + 1}}, edge_user))
    tests = [{"input": [f, u], "expected": fn(f, u)} for f, u in cases]
    assert tests[-1]["expected"] == {"canary": False, "canary_next": ns["bucket"]("canary_next", 1003) < edge_pct + 1}
    # процентная раскатка должна дать оба исхода, иначе тест ничего не проверяет
    nc = [tc["expected"]["new_checkout"] for tc in tests[:4]]
    dm = [tc["expected"]["dark_mode"] for tc in tests[4:6]]
    assert True in nc and False in nc and True in dm and False in dm, (nc, dm)
    t["content"]["test_cases"] = tests


_fill_flag_tests()


def shuffle_options(all_tasks, salt="fs"):
    """В исходнике варианты идут в удобном для автора порядке; в выдаче — детерминированно перемешаны."""
    import random
    for tk in all_tasks:
        c = tk["content"]
        if not isinstance(c.get("options"), list):
            continue
        opts, ans = c["options"], c["correct_answer"]
        order = list(range(len(opts)))
        random.Random(salt + ":" + tk["task_id"]).shuffle(order)
        c["options"] = [opts[i] for i in order]
        c["correct_answer"] = sorted(order.index(i) for i in ans) if isinstance(ans, list) else order.index(ans)


shuffle_options(TASKS)

data = {"track": "fullstack", "part": "fs", "topics": TOPICS, "tasks": TASKS}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)
print("topics:", len(TOPICS), "tasks:", len(TASKS))
