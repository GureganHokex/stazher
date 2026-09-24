#!/usr/bin/env python3
"""Генератор контента «Стажёра»: frontend, часть f2 (junior): fe-js-basics, fe-js-closures-this, fe-js-async, fe-react-basics."""
import json
import random

OUT = "Tools/Content/out/frontend_f2.json"
GRADE = "junior"
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}


def xp(d):
    return int(round(XP_BASE[d] * 1.0 / 5.0)) * 5


def task(tid, typ, d, character, story, question, explanation, hints, code=None, language=None,
         options=None, answer=None, tests=None, entry=None, time_limit=None):
    topic = tid.rsplit("-", 1)[0]
    content = {"question": question, "code": code, "language": language}
    if entry:
        content["entry"] = entry
    content.update({"options": options, "correct_answer": answer, "test_cases": tests})
    return {
        "task_id": tid, "topic_id": topic, "grade": GRADE, "type": typ, "difficulty": d,
        "xp_reward": xp(d), "time_limit_minutes": time_limit, "character": character,
        "story": story, "content": content, "explanation": explanation, "hints": hints,
    }


def t(inp, exp):
    return {"input": inp, "expected": exp}


def rx(desc, pattern, flags="i"):
    return {"input": desc, "expected": {"regex": pattern, "flags": flags}}


def nrx(desc, pattern, flags="i"):
    return {"input": desc, "expected": {"not_regex": pattern, "flags": flags}}


TOPICS = []
TASKS = []

# =====================================================================================
# fe-js-basics — JavaScript: основы
# =====================================================================================
TOPICS.append({"topic_id": "fe-js-basics", "theory": """JavaScript — язык фронтенда: всё, что происходит в браузере после загрузки страницы, написано на нём (или на TypeScript, который компилируется в JS). Большинство багов у джунов — не в сложных вещах, а в основах. Разберём их.

Типы. Примитивы: string, number (одно число и для целых, и для дробных), boolean, null, undefined, bigint, symbol. Всё остальное — объекты: массивы, функции, даты. typeof null возвращает 'object' — историческая ошибка языка, поэтому на null проверяй через value === null.

Переменные. По умолчанию пиши const, let — если значение будет переприсвоено. var не используй: его область видимости — вся функция, а не блок. const запрещает переприсвоить переменную, но не менять объект: const cart = []; cart.push(item) — работает.

Сравнение. === сравнивает без приведения типов, == сначала приводит типы по запутанным правилам:
    '5' == 5            // true
    0 == ''             // true
    null == undefined   // true
    '5' === 5           // false
Правило команды — всегда ===. Единственное популярное исключение — value == null: оно ловит сразу null и undefined. Ещё две ловушки: NaN не равен даже себе (проверяй Number.isNaN), а объекты и массивы сравниваются по ссылке: [1] === [1] — false.

Ложные (falsy) значения: false, 0, '', null, undefined, NaN. Поэтому price || 100 заменит и честный ноль. Если 0 и пустая строка — нормальные значения, используй ??: он подставляет запасное значение только вместо null и undefined. Для вложенных полей есть ?.: order.client?.name не упадёт, если client нет.

Массивы. Эти методы возвращают новое значение и не трогают исходный массив:
• map — преобразовать каждый элемент;
• filter — оставить подходящие;
• reduce — свернуть массив в одно значение: сумму, объект-сводку;
• find, some, every, includes — поиск и проверки.
    const total = items.reduce((sum, item) => sum + item.price * item.qty, 0);
Всегда передавай в reduce начальное значение — без него на пустом массиве будет TypeError. forEach ничего не возвращает, он для побочных эффектов.

Ловушка sort: он меняет исходный массив и по умолчанию сравнивает элементы как строки, поэтому [10, 9, 1].sort() даёт [1, 10, 9]. Правильно — копия и компаратор: [...prices].sort((a, b) => a - b). В современных браузерах есть toSorted — сразу возвращает копию. reverse и splice тоже мутируют.

Объекты и деструктуризация. Поля читают через точку или скобки (obj[key] — когда ключ в переменной). Object.keys, values и entries превращают объект в массив. Spread { ...product, price: 990 } делает поверхностную копию с изменением. Деструктуризация достаёт поля в переменные, умеет значения по умолчанию и переименование:
    const { id, title: name, items = [] } = order;
Значение по умолчанию срабатывает только для undefined, не для null: если бэкенд прислал client: null, запись const { client: { name } = {} } = order упадёт. Для таких полей пиши client ?? {}.

Числа: 0.1 + 0.2 === 0.30000000000000004. Деньги считай в копейках (целых числах) или округляй перед показом."""})

TASKS.append(task(
    "fe-js-basics-01", "write_code", 1, "manager",
    "Стас, продакт «Лампового Маркета»: «На главной хотим блок „В наличии“. Бэкенд отдаёт весь каталог, а нам нужны только названия товаров, которые реально есть на складе».",
    "Напиши функцию inStockTitles(products): верни массив названий (title) товаров, у которых stock больше 0, в исходном порядке. Используй filter и map.",
    "filter оставляет элементы, для которых колбэк вернул true, а map превращает каждый оставшийся товар в его название. Оба метода возвращают новый массив и не трогают исходный — это важно, потому что каталог нужен и другим блокам страницы. Порядок filter → map логичный: сначала отсекаем лишнее, потом преобразуем только нужное. Можно было написать filter((p) => p.stock), и для чисел это сработает, но явное stock > 0 читается лучше и не зависит от правил falsy-значений.",
    ["Сначала отфильтруй товары с остатком, потом возьми у каждого title.",
     "products.filter((p) => p.stock > 0) вернёт массив нужных товаров.",
     "Допиши .map((p) => p.title) и верни результат."],
    code="""function inStockTitles(products) {
  // твой код
}
""",
    language="javascript", entry="inStockTitles",
    answer="""function inStockTitles(products) {
  return products
    .filter((product) => product.stock > 0)
    .map((product) => product.title);
}
""",
    tests=[
        t([[{"title": "Лампа Эдисона", "stock": 5}, {"title": "Торшер «Луна»", "stock": 0},
            {"title": "Ночник «Кот»", "stock": 12}]], ["Лампа Эдисона", "Ночник «Кот»"]),
        t([[{"title": "Гирлянда", "stock": 0}]], []),
        t([[{"title": "Лампа 12 Вт", "stock": 1}]], ["Лампа 12 Вт"]),
        t([[]], []),
    ],
))

TASKS.append(task(
    "fe-js-basics-02", "find_bug", 2, "qa",
    "Ира из QA: «Блок „Самое дорогое“ на главной Маркета ставит лампу за 990 ₽ выше торшера за 15 990 ₽. А после того как блок отрисовался, каталог под ним тоже перемешался. Шаги: открыть главную, сравнить блок и каталог».",
    "homeBlock(prices, n) должна вернуть top — n самых больших цен по убыванию — и catalog — исходный массив цен в исходном порядке. Найди и исправь ошибки в topPrices.",
    "У sort две ловушки. Без компаратора он сравнивает элементы как строки: '15990' < '990', потому что символ '1' меньше '9', — отсюда странный порядок. И sort сортирует массив на месте: prices — это тот же массив, что использует каталог, поэтому каталог тоже перемешался (reverse тоже мутирует). Исправление — сортировать копию ([...prices] или prices.slice()) с числовым компаратором (a, b) => b - a: отрицательный результат ставит a раньше b, так получается порядок по убыванию. В современных браузерах то же делает prices.toSorted((a, b) => b - a).",
    ["Проверь, как sort() без аргументов сравнивает 990 и 15990.",
     "sort и reverse меняют исходный массив. Откуда тогда catalog берёт порядок?",
     "return [...prices].sort((a, b) => b - a).slice(0, n);"],
    code="""// Возвращает n самых больших цен по убыванию
function topPrices(prices, n) {
  return prices.sort().reverse().slice(0, n);
}

// Так блок используется на главной: исходный массив дальше нужен каталогу
function homeBlock(prices, n) {
  const top = topPrices(prices, n);
  return { top, catalog: prices };
}
""",
    language="javascript", entry="homeBlock",
    answer="""// Возвращает n самых больших цен по убыванию
function topPrices(prices, n) {
  return [...prices].sort((a, b) => b - a).slice(0, n);
}

// Так блок используется на главной: исходный массив дальше нужен каталогу
function homeBlock(prices, n) {
  const top = topPrices(prices, n);
  return { top, catalog: prices };
}
""",
    tests=[
        t([[990, 15990, 2490, 100], 2], {"top": [15990, 2490], "catalog": [990, 15990, 2490, 100]}),
        t([[5, 3, 8], 3], {"top": [8, 5, 3], "catalog": [5, 3, 8]}),
        t([[1200, 300], 5], {"top": [1200, 300], "catalog": [1200, 300]}),
    ],
))

TASKS.append(task(
    "fe-js-basics-03", "write_code", 2, "teamlead",
    "Гена, тимлид: «В КофеБоте хотим утреннюю сводку для офис-менеджера: сколько каких напитков заказали, чтобы понимать, сколько молока докупать. Заказы уже приходят массивом, осталось посчитать».",
    "Напиши summary(orders): верни объект, где ключ — напиток (drink), а значение — сколько всего чашек заказали (сумма cups). Если в заказе нет поля cups — это одна чашка. Используй reduce.",
    "reduce проходит по массиву и накапливает результат в аккумуляторе — здесь это объект-сводка, который начинается с {}. Для каждого заказа берём текущее значение по ключу напитка (или 0, если такого напитка ещё не было) и прибавляем чашки. Деструктуризация { drink, cups = 1 } прямо в параметрах подставляет единицу, если поля cups нет. Начальное значение {} обязательно: без него reduce возьмёт первым аккумулятором сам первый заказ, а на пустом массиве упадёт с TypeError. Ключ из переменной записываем через скобки: acc[drink].",
    ["Начальное значение аккумулятора — пустой объект {}.",
     "Внутри: acc[drink] = (acc[drink] ?? 0) + cups; и не забудь return acc.",
     "Значение по умолчанию для cups можно задать в деструктуризации параметра: ({ drink, cups = 1 })."],
    code="""function summary(orders) {
  // верни объект вида { 'латте': 3, 'капучино': 1 }
}
""",
    language="javascript", entry="summary",
    answer="""function summary(orders) {
  return orders.reduce((acc, { drink, cups = 1 }) => {
    acc[drink] = (acc[drink] ?? 0) + cups;
    return acc;
  }, {});
}
""",
    tests=[
        t([[{"drink": "латте", "cups": 2}, {"drink": "капучино"}, {"drink": "латте", "cups": 1}]],
          {"латте": 3, "капучино": 1}),
        t([[{"drink": "раф", "cups": 3}]], {"раф": 3}),
        t([[{"drink": "американо"}, {"drink": "американо"}, {"drink": "флэт уайт", "cups": 4}]],
          {"американо": 2, "флэт уайт": 4}),
        t([[]], {}),
    ],
))

TASKS.append(task(
    "fe-js-basics-04", "code_review", 3, "teamlead",
    "Марина, сеньор: «Посмотри PR стажёра — бейдж корзины в шапке сайта. Код короткий, но граблей в нём хватает. Отметь, что реально надо исправить до мерджа, вкусовщину не пиши».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Три проблемы здесь реальные и проявятся у пользователей. sort мутирует массив: items — это состояние корзины, и после рендера бейджа порядок товаров в самой корзине поменяется. || подставляет 10 вместо любого falsy-значения, включая честный 0: промокод «бесплатная доставка» с percent: 0 внезапно даст скидку 10%, а ?? подставит запасное значение только для null и undefined. На пустой корзине sorted[0] — undefined, и .title уронит шапку на каждой странице. А promo == null — как раз нормальная идиома: она ловит и null, и undefined, замена на === null сломает случай, когда промокода нет вовсе. for...of для массива — правильный выбор, а объект в ответе читается лучше безымянного массива.",
    ["Какие методы массива меняют исходный массив?",
     "Что вернёт 0 || 10? А 0 ?? 10?",
     "Что лежит в sorted[0], если корзина пустая?"],
    code="""// Бейдж корзины в шапке: сумма и самый дорогой товар
function cartBadge(items, promo) {
  const sorted = items.sort((a, b) => b.price - a.price);
  let total = 0;
  for (const item of sorted) {
    total += item.price * item.qty;
  }
  if (promo == null) {
    return { total, top: sorted[0].title };
  }
  // если в промокоде процент не указан, по правилам акции скидка 10%
  const percent = promo.percent || 10;
  return {
    total: Math.round((total * (100 - percent)) / 100),
    top: sorted[0].title,
  };
}
""",
    language="javascript",
    options=[
        "promo == null — нестрогое сравнение, а по правилам команды только ===: нужно promo === null",
        "items.sort() сортирует массив корзины на месте — поменяется порядок товаров в самой корзине, нужна копия",
        "for...of заметно медленнее forEach на больших массивах — переписать цикл на items.forEach или reduce",
        "promo.percent || 10 превращает промокод с percent: 0 в скидку 10% — нужно ?? вместо ||",
        "На пустой корзине sorted[0].title упадёт с TypeError — нужен отдельный случай для пустого массива",
        "Лучше возвращать массив [total, top] вместо объекта — так короче и удобнее деструктурировать",
    ],
    answer=[1, 3, 4],
))

TASKS.append(task(
    "fe-js-basics-05", "write_code", 3, "client",
    "Нина, владелица пекарни «Батон»: «В админке карточка заказа то пустая, то вообще белый экран. Разработчик бэкенда говорит: у гостевых заказов client: null, а у старых заказов поля client нет совсем. Сделайте, чтобы карточка работала всегда».",
    "Напиши orderCard(raw): верни объект { id, customer, phone, total, itemsCount }. customer — raw.client.name, а если клиента или имени нет — 'Гость'; phone — raw.client.phone или null; total — сумма price * qty по raw.items; itemsCount — сумма qty. Если items нет — total и itemsCount равны 0. Учти, что client бывает null и бывает не передан. Используй деструктуризацию со значениями по умолчанию.",
    "Деструктуризация достаёт нужные поля одной строкой и задаёт значения по умолчанию. Но дефолт срабатывает только для undefined: если бэкенд прислал client: null, запись const { client: { name } = {} } = raw упадёт с TypeError. Поэтому client достаём отдельно, а поля из него — из client ?? {}: оператор ?? подставит пустой объект и для null, и для undefined. items = [] защищает reduce от отсутствующего поля, а начальное значение 0 — от пустого массива. Такая нормализация на входе — хорошая привычка: дальше компонент карточки работает с предсказуемой формой данных и не обвешан проверками.",
    ["Дефолты в деструктуризации работают только для undefined. Что будет с null?",
     "Достань client отдельно, а потом: const { name = 'Гость', phone = null } = client ?? {};",
     "total и itemsCount — два reduce по items с начальным значением 0."],
    code="""function orderCard(raw) {
  // верни { id, customer, phone, total, itemsCount }
}
""",
    language="javascript", entry="orderCard",
    answer="""function orderCard(raw) {
  const { id, client, items = [] } = raw;
  const { name = 'Гость', phone = null } = client ?? {};
  const total = items.reduce((sum, { price, qty }) => sum + price * qty, 0);
  const itemsCount = items.reduce((sum, { qty }) => sum + qty, 0);
  return { id, customer: name, phone, total, itemsCount };
}
""",
    tests=[
        t([{"id": 101, "client": {"name": "Нина", "phone": "+79990001122"},
            "items": [{"title": "Багет", "price": 120, "qty": 2}, {"title": "Круассан", "price": 95, "qty": 3}]}],
          {"id": 101, "customer": "Нина", "phone": "+79990001122", "total": 525, "itemsCount": 5}),
        t([{"id": 102, "client": None, "items": [{"title": "Бородинский", "price": 80, "qty": 1}]}],
          {"id": 102, "customer": "Гость", "phone": None, "total": 80, "itemsCount": 1}),
        t([{"id": 103, "client": {"name": "Артур"}}],
          {"id": 103, "customer": "Артур", "phone": None, "total": 0, "itemsCount": 0}),
        t([{"id": 104, "items": []}],
          {"id": 104, "customer": "Гость", "phone": None, "total": 0, "itemsCount": 0}),
    ],
))

# =====================================================================================
# fe-js-closures-this — Замыкания и this
# =====================================================================================
TOPICS.append({"topic_id": "fe-js-closures-this", "theory": """Область видимости — где переменная доступна. У let и const она блочная (внутри { }), у параметров и переменных функции — всё её тело. Внутренняя функция видит переменные внешней, внешняя внутренние — нет.

Замыкание — это функция вместе с переменными того места, где она создана. Функция помнит их и после того, как внешняя функция отработала:
    function createCounter() {
      let count = 0;          // живёт в замыкании
      return () => {
        count += 1;
        return count;
      };
    }
    const next = createCounter();
    next(); // 1
    next(); // 2
Каждый вызов createCounter создаёт новую переменную count, поэтому у двух счётчиков состояние не пересекается. Снаружи до count не добраться — это приватное состояние без классов.

Где замыкания на работе:
• обёртки над функциями: once (вызвать один раз), memoize (кэшировать результат), debounce и throttle;
• обработчики событий и колбэки, которые помнят данные своего элемента;
• компоненты React: обработчики внутри компонента замыкаются на пропсы и state текущего рендера.

Главная ошибка — положить состояние не туда. Переменная, объявленная внутри возвращаемой функции, создаётся заново при каждом её вызове и ничего не помнит. Состояние обёртки объявляй во внешней функции, рядом с return.

Debounce — «подожди, пока пользователь закончит». Каждый новый вызов отменяет отложенный, и функция срабатывает через wait мс после последнего вызова. Нужен для поиска по мере ввода, автосохранения, ресайза. В браузере его пишут через таймер, id которого хранится в замыкании:
    function debounce(fn, wait) {
      let timer = null;
      return (...args) => {
        clearTimeout(timer);
        timer = setTimeout(() => fn(...args), wait);
      };
    }
Throttle — родственник: не чаще одного раза за период (скролл, движение мыши).

this — это не «где функция написана», а «как её вызвали»:
• obj.method() — this равен obj;
• просто fn() — this равен undefined (в модулях и классах всегда строгий режим; в старом нестрогом коде — глобальный объект);
• new Fn() — новый объект;
• fn.call(obj, a, b) и fn.apply(obj, [a, b]) — this задан явно;
• fn.bind(obj) — новая функция с навсегда привязанным this.

У стрелочных функций своего this нет — они берут его из места, где созданы. Поэтому стрелка идеальна как колбэк внутри метода и плоха как метод объекта: this там будет внешний.

Потеря this — классика. Метод, переданный как значение, отрывается от объекта:
    prices.map(this.format)            // внутри format this — undefined
    prices.map((p) => this.format(p))  // ок: стрелка берёт this метода
    prices.map(this.format.bind(this)) // ок
То же самое с setTimeout(obj.method) и обработчиками событий. Если пишешь универсальную обёртку (once, memoize), пробрасывай контекст: fn.apply(this, args), а саму обёртку делай обычной функцией, не стрелкой."""})

TASKS.append(task(
    "fe-js-closures-this-01", "write_code", 1, "teamlead",
    "Гена: «В Кодзилла Трекере на доске две колонки — „К работе“ и „В работе“, у каждой свой счётчик задач. Сделай фабрику счётчиков на замыкании: без глобальных переменных и без классов».",
    "Напиши createCounter(start): верни объект с методами increment() (+1), decrement() (−1, но не ниже 0), reset() (вернуть к start) и value() (текущее значение). Функция runBoard уже написана — она создаёт два счётчика и прогоняет команды.",
    "Переменная count объявлена внутри createCounter, и все четыре метода замыкаются на неё: помнят её и после того, как createCounter отработала. Каждый вызов createCounter создаёт новую count, поэтому у колонок „К работе“ и „В работе“ состояние не пересекается — это и проверяет runBoard. Снаружи count можно поменять только через методы, так что правило «не ниже нуля» не обойти. Если бы count лежала в глобальной переменной, обе колонки делили бы один счётчик.",
    ["Объяви let count = start внутри createCounter, до return.",
     "Верни объект с четырьмя методами, которые читают и меняют count.",
     "В decrement: if (count > 0) count -= 1;"],
    code="""function createCounter(start) {
  // твой код: верни { increment, decrement, reset, value }
}

// Уже написано: у каждой колонки доски свой счётчик
function runBoard(start, commands) {
  const todo = createCounter(start);
  const doing = createCounter(start);
  const columns = { todo, doing };
  for (const [column, action] of commands) {
    columns[column][action]();
  }
  return { todo: todo.value(), doing: doing.value() };
}
""",
    language="javascript", entry="runBoard",
    answer="""function createCounter(start) {
  let count = start;
  return {
    increment() {
      count += 1;
    },
    decrement() {
      if (count > 0) {
        count -= 1;
      }
    },
    reset() {
      count = start;
    },
    value() {
      return count;
    },
  };
}

// Уже написано: у каждой колонки доски свой счётчик
function runBoard(start, commands) {
  const todo = createCounter(start);
  const doing = createCounter(start);
  const columns = { todo, doing };
  for (const [column, action] of commands) {
    columns[column][action]();
  }
  return { todo: todo.value(), doing: doing.value() };
}
""",
    tests=[
        t([0, [["todo", "increment"], ["todo", "increment"], ["doing", "increment"]]], {"todo": 2, "doing": 1}),
        t([1, [["doing", "decrement"], ["doing", "decrement"], ["todo", "increment"]]], {"todo": 2, "doing": 0}),
        t([3, [["todo", "increment"], ["todo", "reset"], ["doing", "decrement"]]], {"todo": 3, "doing": 2}),
        t([0, []], {"todo": 0, "doing": 0}),
    ],
))

TASKS.append(task(
    "fe-js-closures-this-02", "quiz", 2, "teamlead",
    "Марина на ревью: «В классе PriceFormatter метод format передали в map как есть, и на стейдже сразу TypeError. Прежде чем чинить, скажи, какие варианты вообще сработают».",
    "Выбери все варианты тела formatAll, которые исправят ошибку.",
    "this определяется в момент вызова. this.format — просто ссылка на функцию; map вызывает её как обычную функцию, без объекта слева от точки, а в классе всегда строгий режим, поэтому this внутри — undefined. Стрелка (price) => this.format(price) берёт this из formatAll, где он указывает на экземпляр. bind(this) создаёт копию функции с привязанным this, а второй аргумент map — thisArg — задаёт this для колбэка. Обычная function внутри map получает собственный this — снова undefined, а PriceFormatter.format ищет статический метод, которого нет, и map получит undefined вместо функции.",
    ["Кто стоит слева от точки, когда map вызывает format?",
     "Стрелочная функция берёт this снаружи, обычная — нет.",
     "Посмотри, какие аргументы принимает map, кроме колбэка."],
    code="""class PriceFormatter {
  constructor(currency) {
    this.currency = currency;
  }

  format(price) {
    return `${price} ${this.currency}`;
  }

  formatAll(prices) {
    return prices.map(this.format);
  }
}

new PriceFormatter('₽').formatAll([990, 1490]);
// TypeError: Cannot read properties of undefined (reading 'currency')
""",
    language="javascript",
    options=[
        "return prices.map(function (price) { return this.format(price); });",
        "return prices.map((price) => this.format(price));",
        "return prices.map(this.format.bind(this));",
        "return prices.map(PriceFormatter.format);",
        "return prices.map(this.format, this);",
    ],
    answer=[1, 2, 4],
))

TASKS.append(task(
    "fe-js-closures-this-03", "find_bug", 2, "qa",
    "Ира из QA: «На сайте „Батона“ жму „Оплатить“ два раза подряд — создаются два заказа, Нина уже звонила. Гена говорит, что оплату обернули в once, но почему-то не помогает».",
    "payClicks(clicks) имитирует clicks нажатий на кнопку, обёрнутую в once: оплата должна пройти ровно один раз, а каждое нажатие — вернуть чек первого вызова. Найди и исправь ошибку в once.",
    "Переменные called и result объявлены внутри возвращаемой функции, поэтому при каждом нажатии создаются заново: called всегда false, и fn вызывается каждый раз. Состояние обёртки должно жить в замыкании — во внешней функции once, которая выполняется один раз на обёртку. Тогда все вызовы pay видят одну и ту же пару called/result. В реальном проекте once на фронте — только первая линия защиты: кнопку ещё блокируют на время запроса, а сервер проверяет ключ идемпотентности, чтобы повторный запрос не создал второй заказ.",
    ["Сколько раз выполняется строка let called = false?",
     "Состояние должно пережить вызов обёртки — где тогда его объявить?",
     "Перенеси let called и let result в once, перед return function."],
    code="""// Возвращает функцию, которая вызывает fn только в первый раз,
// а дальше отдаёт результат первого вызова
function once(fn) {
  return function (...args) {
    let called = false;
    let result;
    if (!called) {
      called = true;
      result = fn(...args);
    }
    return result;
  };
}

// Имитация кнопки «Оплатить»: clicks — сколько раз нажали
function payClicks(clicks) {
  let charges = 0;
  const pay = once(() => {
    charges += 1;
    return `order-${charges}`;
  });
  const receipts = [];
  for (let i = 0; i < clicks; i += 1) {
    receipts.push(pay());
  }
  return { charges, receipts };
}
""",
    language="javascript", entry="payClicks",
    answer="""// Возвращает функцию, которая вызывает fn только в первый раз,
// а дальше отдаёт результат первого вызова
function once(fn) {
  let called = false;
  let result;
  return function (...args) {
    if (!called) {
      called = true;
      result = fn(...args);
    }
    return result;
  };
}

// Имитация кнопки «Оплатить»: clicks — сколько раз нажали
function payClicks(clicks) {
  let charges = 0;
  const pay = once(() => {
    charges += 1;
    return `order-${charges}`;
  });
  const receipts = [];
  for (let i = 0; i < clicks; i += 1) {
    receipts.push(pay());
  }
  return { charges, receipts };
}
""",
    tests=[
        t([1], {"charges": 1, "receipts": ["order-1"]}),
        t([3], {"charges": 1, "receipts": ["order-1", "order-1", "order-1"]}),
        t([2], {"charges": 1, "receipts": ["order-1", "order-1"]}),
        t([0], {"charges": 0, "receipts": []}),
    ],
))

TASKS.append(task(
    "fe-js-closures-this-04", "find_bug", 3, "teamlead",
    "Гена: «Сделали memoize, чтобы не пересчитывать цены по курсу в каждой карточке. Для обычных функций работает, а как обернули метод объекта — каталог падает с TypeError: Cannot read properties of undefined (reading 'calls')».",
    "convertPrices(rate, pricesUsd) должна вернуть цены в рублях (prices) и сколько раз курс реально пересчитывался (calls — повторные цены берутся из кэша). Найди и исправь ошибку в memoize; метод toRub и convertPrices не трогай.",
    "converter.toRub(usd) вызывает обёртку из memoize с this = converter. Но обёртка вызывает исходную функцию как fn(...args) — без объекта слева от точки, и this внутри toRub становится undefined (строгий режим, как в модулях). Обёртка должна пробрасывать контекст: fn.apply(this, args) или fn.call(this, ...args). Это работает только потому, что обёртка — обычная function: у стрелки нет своего this, и она взяла бы его из memoize. Так устроены once, memoize и debounce в lodash: универсальная обёртка передаёт дальше и аргументы, и this.",
    ["С каким this вызывается обёртка, когда пишут converter.toRub(10)?",
     "А с каким this обёртка вызывает fn?",
     "Замени fn(...args) на fn.apply(this, args)."],
    code="""'use strict'; // как в ES-модулях: там строгий режим включён всегда

function memoize(fn) {
  const cache = new Map();
  return function (...args) {
    const key = JSON.stringify(args);
    if (!cache.has(key)) {
      cache.set(key, fn(...args));
    }
    return cache.get(key);
  };
}

function convertPrices(rate, pricesUsd) {
  const converter = {
    rate,
    calls: 0,
    toRub: memoize(function (usd) {
      this.calls += 1;
      return Math.round(usd * this.rate);
    }),
  };
  const prices = pricesUsd.map((usd) => converter.toRub(usd));
  return { prices, calls: converter.calls };
}
""",
    language="javascript", entry="convertPrices",
    answer="""'use strict'; // как в ES-модулях: там строгий режим включён всегда

function memoize(fn) {
  const cache = new Map();
  return function (...args) {
    const key = JSON.stringify(args);
    if (!cache.has(key)) {
      cache.set(key, fn.apply(this, args));
    }
    return cache.get(key);
  };
}

function convertPrices(rate, pricesUsd) {
  const converter = {
    rate,
    calls: 0,
    toRub: memoize(function (usd) {
      this.calls += 1;
      return Math.round(usd * this.rate);
    }),
  };
  const prices = pricesUsd.map((usd) => converter.toRub(usd));
  return { prices, calls: converter.calls };
}
""",
    tests=[
        t([90, [10, 20, 10]], {"prices": [900, 1800, 900], "calls": 2}),
        t([92.5, [4, 4, 4]], {"prices": [370, 370, 370], "calls": 1}),
        t([80, [1.25, 3]], {"prices": [100, 240], "calls": 2}),
        t([100, []], {"prices": [], "calls": 0}),
    ],
))

TASKS.append(task(
    "fe-js-closures-this-05", "write_code", 3, "manager",
    "Стас: «Поиск по каталогу Маркета шлёт запрос на каждую букву — бэкенд уже жалуется. Сделай как у всех: запрос уходит, только когда человек перестал печатать».",
    "Таймеров в игре нет, поэтому время передаём явно. Напиши debounce(fn, wait): верни функцию debounced(now, value), где now — время ввода в мс. Правила: если с предыдущего ввода до нового вызова прошло меньше wait мс, предыдущий ввод просто отменяется; если wait или больше — его таймер уже сработал, вызови fn(предыдущее значение, его время + wait). Новый ввод становится отложенным. У debounced есть метод flush(): если есть отложенный ввод, он вызывает для него fn(value, time + wait) и очищает его. searchRequests уже написана.",
    "Отложенный ввод — единственное состояние debounce, и оно живёт в замыкании: переменная pending объявлена в debounce и доступна и debounced, и flush. При каждом вызове проверяем, успел бы сработать таймер предыдущего ввода (now − time ≥ wait): если да — отправляем его со временем time + wait, если нет — просто затираем новым. flush отправляет последний ввод: в браузере это сделал бы таймер, когда пользователь перестал печатать. Настоящий debounce делает то же через clearTimeout и setTimeout, храня в замыкании id таймера. Итог: вместо запроса на каждую букву — один запрос на каждую паузу в наборе.",
    ["Храни в замыкании последний ввод: объект { value, time } или null.",
     "При вызове: если pending есть и now - pending.time >= wait, вызови fn(pending.value, pending.time + wait). Потом запомни новый ввод.",
     "Функции можно добавить свойство: debounced.flush = function () { ... };"],
    code="""// Таймеров нет: время ввода передаём явно, now — в миллисекундах
function debounce(fn, wait) {
  // твой код: верни функцию debounced(now, value) с методом debounced.flush()
}

// Уже написано: events — пары [время, текст в поле], возвращает отправленные запросы
function searchRequests(events, wait) {
  const sent = [];
  const search = debounce((query, at) => sent.push([query, at]), wait);
  for (const [now, query] of events) {
    search(now, query);
  }
  search.flush();
  return sent;
}
""",
    language="javascript", entry="searchRequests",
    answer="""// Таймеров нет: время ввода передаём явно, now — в миллисекундах
function debounce(fn, wait) {
  let pending = null; // { value, time } — ввод, который ждёт отправки

  function debounced(now, value) {
    if (pending !== null && now - pending.time >= wait) {
      fn(pending.value, pending.time + wait);
    }
    pending = { value, time: now };
  }

  debounced.flush = function () {
    if (pending !== null) {
      fn(pending.value, pending.time + wait);
      pending = null;
    }
  };

  return debounced;
}

// Уже написано: events — пары [время, текст в поле], возвращает отправленные запросы
function searchRequests(events, wait) {
  const sent = [];
  const search = debounce((query, at) => sent.push([query, at]), wait);
  for (const [now, query] of events) {
    search(now, query);
  }
  search.flush();
  return sent;
}
""",
    tests=[
        t([[[0, "л"], [120, "ла"], [250, "лам"], [900, "ламп"]], 300], [["лам", 550], ["ламп", 1200]]),
        t([[[0, "торшер"], [1000, "люстра"]], 300], [["торшер", 300], ["люстра", 1300]]),
        t([[[0, "н"], [300, "но"], [450, "ноч"]], 300], [["н", 300], ["ноч", 750]]),
        t([[[10, "г"], [60, "ги"], [110, "гир"]], 200], [["гир", 310]]),
        t([[], 300], []),
    ],
))

# =====================================================================================
# fe-js-async — Промисы и async/await
# =====================================================================================
TOPICS.append({"topic_id": "fe-js-async", "theory": """Сеть, диск, таймеры — это долго, а интерфейс не должен замирать. Поэтому в JS такие операции асинхронные: функция сразу возвращает промис — обещание результата, — а сам результат приходит позже.

Промис бывает в трёх состояниях: pending (ждём), fulfilled (успех, есть значение) и rejected (ошибка, есть причина). Состояние меняется один раз. Готовые промисы создают через Promise.resolve(value) и Promise.reject(new Error('...')) — удобно для тестов и заглушек.

Старый синтаксис — цепочки:
    fetchUser(id)
      .then((user) => user.name)
      .catch(() => 'Гость')
      .finally(() => hideSpinner());

Современный — async/await, читается как обычный код:
    async function loadName(id) {
      try {
        const user = await fetchUser(id);
        return user.name;
      } catch (error) {
        return 'Гость';
      } finally {
        hideSpinner();
      }
    }
• async-функция всегда возвращает промис, даже если внутри return 5.
• await приостанавливает функцию до результата. Если промис отклонён, await бросает ошибку — её ловит обычный try/catch.
• finally выполняется при любом исходе — место для сброса флага загрузки.

Частые ошибки:
• Забытый await. В переменной оказывается сам промис, а не данные. Промис — объект, он всегда truthy, поэтому if (checkStock(id)) всегда истинно.
• return fetchX() внутри try без await: ошибка вылетит уже после выхода из try, мимо catch. Пиши return await fetchX().
• forEach(async ...) не ждёт колбэки — используй for...of с await или Promise.all.
• Необработанный reject: в консоли и Sentry Unhandled Promise Rejection, а пользователь видит вечный спиннер.

Последовательно или параллельно. Два await подряд — это последовательно: второй запрос стартует, только когда закончился первый. Если запросы друг от друга не зависят, запусти их одновременно:
    const [user, orders] = await Promise.all([fetchUser(id), fetchOrders(id)]);
Время — максимум из двух, а не сумма. Последовательно нужно, когда следующему запросу нужен результат предыдущего или когда API не выдержит сотню запросов разом.

Комбинаторы:
• Promise.all — ждёт все; если хоть один упал, весь результат — ошибка, остальные значения теряются. Для данных, без которых дальше нельзя.
• Promise.allSettled — ждёт все и сам не падает; возвращает массив { status: 'fulfilled', value } или { status: 'rejected', reason } в порядке входа. Для массовых операций и независимых блоков.
• Promise.race — первый завершившийся (например, запрос против тайм-аута); Promise.any — первый успешный.

Хорошая привычка — делить данные на критичные (без них экран бессмысленен — показываем ошибку) и некритичные (отзывы, рекомендации — при ошибке просто прячем блок)."""})

TASKS.append(task(
    "fe-js-async-01", "write_code", 1, "manager",
    "Стас: «В шапке Маркета показываем имя пользователя. Сегодня сервис профилей лёг — и вместе с ним вся страница. Пусть в таком случае в шапке просто будет „Гость“».",
    "Напиши async-функцию headerName(users, id): дождись fetchProfile(users, id) и верни имя из профиля (name). Если запрос упал с любой ошибкой — верни 'Гость'. Используй async/await и try/catch.",
    "await ждёт промис из fetchProfile. Если промис отклонён, await бросает исключение прямо в этой строке, и его ловит обычный try/catch — как синхронную ошибку. В catch возвращаем запасное значение, и шапка отрисуется даже без сервиса профилей. Сама async-функция вернёт промис со строкой, поэтому вызывающий код тоже делает await. В реальном проекте в catch ещё логируют ошибку (например, в Sentry), чтобы падение сервиса не прошло незамеченным.",
    ["Оберни await fetchProfile(users, id) в try.",
     "В catch верни 'Гость'.",
     "const profile = await fetchProfile(users, id); return profile.name;"],
    code="""// Имитация запроса к сервису профилей: users — «база» на сервере, null — сервис лежит
function fetchProfile(users, id) {
  if (users === null) {
    return Promise.reject(new Error('503: сервис профилей недоступен'));
  }
  if (!(id in users)) {
    return Promise.reject(new Error(`404: профиль ${id} не найден`));
  }
  return Promise.resolve(users[id]);
}

async function headerName(users, id) {
  // твой код
}
""",
    language="javascript", entry="headerName",
    answer="""// Имитация запроса к сервису профилей: users — «база» на сервере, null — сервис лежит
function fetchProfile(users, id) {
  if (users === null) {
    return Promise.reject(new Error('503: сервис профилей недоступен'));
  }
  if (!(id in users)) {
    return Promise.reject(new Error(`404: профиль ${id} не найден`));
  }
  return Promise.resolve(users[id]);
}

async function headerName(users, id) {
  try {
    const profile = await fetchProfile(users, id);
    return profile.name;
  } catch (error) {
    return 'Гость';
  }
}
""",
    tests=[
        t([{"7": {"name": "Ира"}}, 7], "Ира"),
        t([{"7": {"name": "Ира"}, "12": {"name": "Стас"}}, 12], "Стас"),
        t([{"7": {"name": "Ира"}}, 8], "Гость"),
        t([None, 7], "Гость"),
    ],
))

TASKS.append(task(
    "fe-js-async-02", "find_bug", 2, "qa",
    "Ира из QA: «Корзина пропускает товары, которых нет на складе. Шаги: положить в корзину лампу с остатком 0 → нажать „Оформить“ → заказ создаётся, хотя должна быть плашка „Нет в наличии“».",
    "missingItems(ids, stock) должна вернуть id товаров, которых нет в наличии, в порядке ids. Наличие проверяется асинхронным запросом checkStock. Найди и исправь ошибку.",
    "checkStock возвращает не boolean, а промис — он станет true или false позже. Без await в available лежит сам объект Promise, а любой объект truthy, поэтому !available всегда false и ни один товар не считается отсутствующим. await распаковывает промис в значение. Такие ошибки подсвечивает правило no-misused-promises из typescript-eslint — его стоит включать в проекте. Здесь запросы идут по очереди; если товаров много, быстрее запустить их разом через Promise.all(ids.map(...)) и потом отфильтровать.",
    ["Что возвращает checkStock — boolean или что-то другое?",
     "Любой объект, включая Promise, в условии считается true.",
     "Добавь await перед checkStock(stock, id)."],
    code="""// Имитация запроса к складу
function checkStock(stock, id) {
  return Promise.resolve((stock[id] ?? 0) > 0);
}

// Возвращает id товаров, которых нет в наличии
async function missingItems(ids, stock) {
  const missing = [];
  for (const id of ids) {
    const available = checkStock(stock, id);
    if (!available) {
      missing.push(id);
    }
  }
  return missing;
}
""",
    language="javascript", entry="missingItems",
    answer="""// Имитация запроса к складу
function checkStock(stock, id) {
  return Promise.resolve((stock[id] ?? 0) > 0);
}

// Возвращает id товаров, которых нет в наличии
async function missingItems(ids, stock) {
  const missing = [];
  for (const id of ids) {
    const available = await checkStock(stock, id);
    if (!available) {
      missing.push(id);
    }
  }
  return missing;
}
""",
    tests=[
        t([["lamp", "cable"], {"lamp": 0, "cable": 3}], ["lamp"]),
        t([["bulb"], {"bulb": 10}], []),
        t([["torch", "garland"], {}], ["torch", "garland"]),
    ],
))

TASKS.append(task(
    "fe-js-async-03", "write_code", 2, "manager",
    "Стас: «В админке Маркета контент-менеджер выделяет 50 товаров и жмёт „Снять с публикации“. Если хоть один запрос падает, сейчас вся операция показывает „Ошибка“, и непонятно, что снялось, а что нет. Нужен нормальный отчёт».",
    "Напиши bulkUnpublish(ids, locked): отправь запросы unpublish для всех ids параллельно и дождись всех, даже если часть упала. Верни { done, failed }: done — id снятых товаров, failed — массив { id, reason }, где reason — текст ошибки (message). Порядок — как в ids. Используй Promise.allSettled.",
    "Promise.all тут не подходит: при первой же ошибке он отклоняется целиком, и не узнать, какие товары всё-таки снялись. Promise.allSettled ждёт все промисы и возвращает массив результатов в том же порядке, что и вход: у каждого status 'fulfilled' с value или 'rejected' с reason. По индексу сопоставляем результат с id и раскладываем по двум спискам. Запросы при этом идут параллельно: ids.map запускает их все сразу, а allSettled только ждёт. Такой отчёт и показывают пользователю: «Снято 48 из 50, 2 товара в активных заказах».",
    ["ids.map((id) => unpublish(locked, id)) — массив промисов, запросы стартуют сразу.",
     "Promise.allSettled вернёт массив { status, value } или { status, reason } в том же порядке, что ids.",
     "reason — объект Error, текст ошибки лежит в reason.message."],
    code="""// Имитация запроса: снять товар с публикации
function unpublish(locked, id) {
  if (locked.includes(id)) {
    return Promise.reject(new Error(`товар ${id} в активном заказе`));
  }
  return Promise.resolve(id);
}

async function bulkUnpublish(ids, locked) {
  // твой код: верни { done: [...], failed: [{ id, reason }] }
}
""",
    language="javascript", entry="bulkUnpublish",
    answer="""// Имитация запроса: снять товар с публикации
function unpublish(locked, id) {
  if (locked.includes(id)) {
    return Promise.reject(new Error(`товар ${id} в активном заказе`));
  }
  return Promise.resolve(id);
}

async function bulkUnpublish(ids, locked) {
  const results = await Promise.allSettled(ids.map((id) => unpublish(locked, id)));
  const done = [];
  const failed = [];
  results.forEach((result, i) => {
    if (result.status === 'fulfilled') {
      done.push(ids[i]);
    } else {
      failed.push({ id: ids[i], reason: result.reason.message });
    }
  });
  return { done, failed };
}
""",
    tests=[
        t([[1, 2, 3], [2]], {"done": [1, 3], "failed": [{"id": 2, "reason": "товар 2 в активном заказе"}]}),
        t([[5, 6], []], {"done": [5, 6], "failed": []}),
        t([[7, 8], [8, 7]], {"done": [], "failed": [{"id": 7, "reason": "товар 7 в активном заказе"},
                                                    {"id": 8, "reason": "товар 8 в активном заказе"}]}),
        t([[], []], {"done": [], "failed": []}),
    ],
))

TASKS.append(task(
    "fe-js-async-04", "architecture", 3, "teamlead",
    "Гена: «Страница товара открывается 1,6 секунды, а когда лежит сервис отзывов — вообще белый экран. Вот как сейчас грузятся данные. Как переделать?»",
    "Какой вариант загрузки данных страницы товара правильный?",
    "Сейчас четыре последовательных await — это 4 × 400 = 1,6 с, и любая ошибка, включая отзывы, роняет всю функцию. product, stock и reviews друг от друга не зависят, их можно запустить одновременно; similar нужен categoryId, поэтому он стартует сразу после product — итого около 800 мс. Критичные данные ждём через Promise.all: без товара и наличия страница бессмысленна, честнее показать экран ошибки. Некритичные — отзывы и похожие товары — обрабатываем отдельно, их ошибка скрывает блок, а не страницу. Один Promise.all на всё невозможен из-за зависимости от categoryId и снова сделал бы отзывы критичными; try/catch вокруг последовательных запросов чинит надёжность, но не скорость; allSettled без разбора покажет пустую страницу без товара, а кэш в localStorage отдаст устаревшие цены и остатки и не поможет при первом заходе.",
    ["Какие запросы зависят от результата других?",
     "Без каких данных страница товара не имеет смысла?",
     "Параллельно — всё, что можно; ошибки некритичных блоков — отдельно от критичных."],
    code="""async function loadProductPage(id) {
  const product = await api.getProduct(id);                 // ~400 мс, без него страницы нет
  const stock = await api.getStock(id);                     // ~400 мс, без него нельзя купить
  const reviews = await api.getReviews(id);                 // ~400 мс, сервис часто лежит
  const similar = await api.getSimilar(product.categoryId); // ~400 мс, нужен categoryId
  return { product, stock, reviews, similar };
}
""",
    language="javascript",
    options=[
        "Запустить все четыре запроса одним Promise.all и показать экран ошибки, если упадёт любой, — загрузка за ~400 мс",
        "product, stock, reviews — параллельно, similar — после product; product и stock — через Promise.all, остальное — со своим catch",
        "Оставить последовательные await, но обернуть каждый в свой try/catch — упавшие отзывы больше не уронят страницу",
        "Загрузить всё через Promise.allSettled и рендерить страницу при любом исходе — так ни один сервис её не уронит",
        "Кэшировать ответы всех четырёх запросов в localStorage на сутки — повторные заходы станут мгновенными",
    ],
    answer=1,
))

TASKS.append(task(
    "fe-js-async-05", "estimation", 3, "manager",
    "Стас: «Хочу, чтобы по кнопке „Оформить“ корзина проверяла наличие и актуальные цены всех товаров, а если что-то поменялось — показывала это покупателю. Сколько сделаешь? Нужна цифра к планированию».",
    "Прежде чем называть срок, выбери вопросы, которые обязательно нужно выяснить.",
    "Оценка упирается в бэкенд и в поведение при ошибках. Если есть эндпоинт проверки всей корзины, фронт делает один запрос; если нет — либо десятки параллельных запросов с риском упереться в лимиты API, либо задача бэкенду, и срок растёт. Политика при частичной ошибке решает, all или allSettled, и какие экраны рисовать. Сценарии изменения цены и наличия — это UI, тексты и, возможно, макет от дизайнера. Состояние загрузки и защиту от двойного нажатия часто забывают, а потом ловят двойные заказы. Цвет подсветки и выбор then или await на срок не влияют, а переписывание корзины на TypeScript — отдельная задача. Разумная оценка: при готовом эндпоинте 1–1,5 дня с тестами, без него — плюс 1–2 дня бэкенда.",
    ["Какие ответы изменят объём работы в разы?",
     "Подумай про ошибки, состояния загрузки и то, что увидит покупатель."],
    time_limit=10,
    options=[
        "Есть ли эндпоинт проверки всей корзины одним запросом или придётся дёргать API по каждому товару?",
        "Каким цветом подсветить изменившуюся цену — красным, как в макете акций, или фирменным оранжевым?",
        "Что делать, если проверка части товаров упала: блокировать оформление или пускать дальше?",
        "На чём писать проверку — на async/await или на цепочках then, как в старом коде корзины?",
        "Что показать, если цена изменилась или товар закончился: убрать его или попросить подтвердить?",
        "Сколько можно ждать проверку и что показываем в это время — спиннер, блок повторного нажатия?",
        "Можно ли заодно переписать корзину на TypeScript, раз всё равно придётся её трогать?",
    ],
    answer=[0, 2, 4, 5],
))

# =====================================================================================
# fe-react-basics — Основы React
# =====================================================================================
TOPICS.append({"topic_id": "fe-react-basics", "theory": """React описывает интерфейс как функцию от данных: ты пишешь, как экран выглядит при текущем состоянии, а React сам обновляет DOM, когда состояние меняется. Стандарт — функциональные компоненты и хуки; классовые компоненты встречаются только в старом коде.

Компонент — функция с большой буквы: принимает объект пропсов и возвращает JSX.
    function ProductCard({ title, price }) {
      return (
        <div className="card">
          <h3>{title}</h3>
          <p>{price} ₽</p>
        </div>
      );
    }
JSX — не HTML: className вместо class, htmlFor вместо for, обработчики в camelCase (onClick). В фигурных скобках — любое JS-выражение. Вернуть можно один корневой элемент; если обёртка не нужна — фрагмент <>…</>.

Списки рендерят через map, и каждому элементу нужен key — стабильный и уникальный среди соседей, обычно id из данных:
    {products.map((p) => <ProductCard key={p.id} title={p.title} price={p.price} />)}
По key React понимает, какой элемент какой между рендерами. Индекс массива в key ломается при удалении, вставке и сортировке: состояние (введённый текст, счётчик) «переезжает» к соседнему элементу. Индекс допустим только для статичного списка, который никогда не меняется.

Условный рендер:
• ранний return: if (items.length === 0) return <p>Пусто</p>;
• тернарник: {isOpen ? <Details /> : null};
• &&: {isOpen && <Details />}.
Ловушка: {items.length && <List />} при пустом массиве выведет на экран 0. Слева от && ставь boolean: items.length > 0.

Состояние — хук useState:
    const [count, setCount] = useState(0);
Вызов setCount запускает перерендер компонента с новым значением. Правила:
• Не мутируй state. React сравнивает старое и новое значение по ссылке: если после push передать тот же массив, он решит, что ничего не изменилось. Создавай новое: setItems([...items, item]), setItems(items.filter((i) => i.id !== id)), setUser({ ...user, name }).
• Если новое значение зависит от старого — передавай функцию: setCount((c) => c + 1).
• Не вызывай setState прямо в теле компонента: рендер → setState → рендер… React остановит это ошибкой Too many re-renders.
• Не храни в state то, что можно посчитать из пропсов или другого state (длину списка, отфильтрованные элементы), — считай прямо в рендере.
• Хуки вызываются только на верхнем уровне компонента — не в условиях и циклах.

Обработчик передают как функцию, а не как результат её вызова:
    <button onClick={handleClick}>        ок
    <button onClick={() => remove(id)}>   ок, когда нужен аргумент
    <button onClick={remove(id)}>         ошибка: remove вызовется при рендере"""})

TASKS.append(task(
    "fe-react-basics-01", "write_code", 1, "client",
    "Вера, управляющая бизнес-центра «Высота»: «На сайте бронирования нужен список свободных переговорок. Если свободных нет — пусть так и пишет, а то сейчас просто пустое место, и арендаторы звонят мне».",
    "Допиши компонент RoomList({ rooms }): если массив rooms пустой — верни <p>Свободных переговорок нет</p>. Иначе верни <ul>, где на каждую комнату — <li> с её названием (room.name) и key из room.id.",
    "Ранний return делает пустой случай явным и не смешивает его с разметкой списка. map превращает массив данных в массив элементов, а key={room.id} помогает React сопоставлять элементы между рендерами: переговорки бронируют и освобождают, список постоянно меняется, и индекс в key привёл бы к путанице. Ключ должен быть стабильным и уникальным среди соседей — id из базы подходит идеально. Проверка rooms.length === 0 спасает и от классической ловушки: {rooms.length && …} при пустом массиве вывел бы на экран 0.",
    ["Сначала обработай пустой массив ранним return.",
     "Список: <ul>{rooms.map((room) => <li key={room.id}>{room.name}</li>)}</ul>"],
    code="""export default function RoomList({ rooms }) {
  // если rooms пустой — сообщение, иначе список
  return null;
}
""",
    language="jsx",
    answer="""export default function RoomList({ rooms }) {
  if (rooms.length === 0) {
    return <p>Свободных переговорок нет</p>;
  }

  return (
    <ul>
      {rooms.map((room) => (
        <li key={room.id}>{room.name}</li>
      ))}
    </ul>
  );
}
""",
    tests=[
        rx("Пустой список: <p>Свободных переговорок нет</p>", r"<p>\s*Свободных переговорок нет\s*</p>"),
        rx("Проверяется, что массив пустой (length)", r"\.length\s*(?:===?\s*0|!==?\s*0|>\s*0|<\s*1)|!\s*\w+\.length"),
        rx("Список рендерится через map внутри <ul>", r"<ul>[\s\S]*\.map\("),
        rx("У <li> есть key из id комнаты", r"<li[^>]*key=\{\s*(?:\w+\.)?id\s*\}"),
        rx("В <li> выводится название комнаты", r"\{\s*(?:\w+\.)?name\s*\}"),
        nrx("Индекс массива не используется как key", r"key=\{\s*(?:i|idx|index)\s*\}"),
        nrx("Нет ловушки {rooms.length && …}, которая выводит 0", r"\{\s*\w+\.length\s*&&"),
    ],
))

TASKS.append(task(
    "fe-react-basics-02", "find_bug", 2, "qa",
    "Ира из QA: «Корзина Маркета: у лампы ставлю количество 3, потом удаляю лампу — и тройка „переезжает“ на следующий товар, кабель. Шаги: 2 товара в корзине → лампе 3 шт. → „Удалить“ у лампы → у кабеля теперь 3».",
    "Почему количество «переезжает» на соседний товар и как это исправить?",
    "React сопоставляет элементы списка между рендерами по key, а состояние (здесь qty) принадлежит компоненту с конкретным ключом. При key={index} после удаления лампы кабель получает индекс 0 — React считает, что это тот же компонент, что раньше показывал лампу, и оставляет ему qty = 3, а удаляет последний компонент. С key={item.id} ключ едет вместе с товаром, и удаляется именно компонент лампы. useState(item.qty ?? 1) не поможет: начальное значение читается только при монтировании, а компонент с key=0 никто не пересоздаёт. Новые стрелки в onClick на сопоставление элементов не влияют. Вообще количество лучше хранить в состоянии корзины, а не в каждом CartItem, но и тогда стабильный key обязателен: от него зависят фокус, анимации и неконтролируемые поля ввода.",
    ["Как React понимает, какой компонент какому товару соответствует после удаления?",
     "Какой индекс получит кабель, когда лампу удалят?"],
    code="""import { useState } from 'react';

function CartItem({ item, onRemove }) {
  const [qty, setQty] = useState(1);
  return (
    <li>
      {item.title}
      <input type="number" value={qty} onChange={(e) => setQty(Number(e.target.value))} />
      <button onClick={() => onRemove(item.id)}>Удалить</button>
    </li>
  );
}

export default function Cart({ items, onRemove }) {
  return (
    <ul>
      {items.map((item, index) => (
        <CartItem key={index} item={item} onRemove={onRemove} />
      ))}
    </ul>
  );
}
""",
    language="jsx",
    options=[
        "useState(1) задаёт одно значение на весь список — нужно useState(item.qty ?? 1), чтобы у товара было своё",
        "onClick={() => onRemove(item.id)} создаёт новую функцию на каждый рендер, и из-за этого React путает состояние",
        "key={index}: после удаления индексы сдвигаются, и состояние лампы достаётся кабелю — нужен key={item.id}",
        "Количество нужно вынести в переменную модуля вне компонента, чтобы оно не терялось при удалении товара",
        "У input должен быть type=\"text\": number-поле в React не сбрасывается при перерендере списка",
    ],
    answer=2,
))

TASKS.append(task(
    "fe-react-basics-03", "write_code", 2, "client",
    "Нина из «Батона»: «На странице торта длинный состав — он занимает пол-экрана с телефона. Хочу, чтобы сначала он был свёрнут, по кнопке „Показать состав“ раскрывался, а кнопка тогда писала „Скрыть состав“».",
    "Допиши компонент Ingredients({ text }): храни флаг открытости в useState (по умолчанию false). Кнопка по клику переключает флаг, её текст — «Показать состав» или «Скрыть состав» в зависимости от флага. Абзац <p>{text}</p> рендерится, только когда флаг true.",
    "useState(false) хранит, раскрыт ли состав, а вызов setIsOpen запускает перерендер с новым значением. В onClick передаём функцию — стрелку, которая вызовет setIsOpen по клику; запись onClick={setIsOpen(!isOpen)} вызвала бы его прямо во время рендера и зациклила компонент. Функциональная форма setIsOpen((open) => !open) берёт актуальное значение, даже если обновлений несколько подряд. Текст кнопки и видимость <p> вычисляются из одного флага, поэтому интерфейс не может «разъехаться»: кнопка говорит «Скрыть», а текст спрятан.",
    ["const [isOpen, setIsOpen] = useState(false);",
     "onClick={() => setIsOpen((open) => !open)}",
     "{isOpen && <p>{text}</p>} и тернарник для текста кнопки."],
    code="""import { useState } from 'react';

export default function Ingredients({ text }) {
  // добавь состояние и переключение
  return (
    <div>
      <button>Показать состав</button>
      <p>{text}</p>
    </div>
  );
}
""",
    language="jsx",
    answer="""import { useState } from 'react';

export default function Ingredients({ text }) {
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div>
      <button onClick={() => setIsOpen((open) => !open)}>
        {isOpen ? 'Скрыть состав' : 'Показать состав'}
      </button>
      {isOpen && <p>{text}</p>}
    </div>
  );
}
""",
    tests=[
        rx("Флаг хранится в useState(false)", r"\[\s*\w+\s*,\s*set\w+\s*\]\s*=\s*(?:React\.)?useState\(\s*false\s*\)"),
        rx("По клику вызывается обработчик-функция", r"onClick=\{\s*(?:\([^)]*\)\s*=>|\w+\s*=>|[A-Za-z_$][\w$]*\s*\})"),
        nrx("onClick получает функцию, а не результат вызова setState", r"onClick=\{\s*set\w*\("),
        rx("Есть текст «Скрыть состав»", r"Скрыть состав"),
        rx("Текст кнопки выбирается по флагу", r"\{\s*!?\w+\s*\?\s*['\"`](?:Скрыть|Показать)"),
        rx("Состав рендерится только при открытом флаге", r"\{\s*\w+\s*(?:&&|\?)\s*\(?\s*<p>\s*\{\s*text\s*\}"),
    ],
))

TASKS.append(task(
    "fe-react-basics-04", "find_bug", 2, "qa",
    "Ира из QA: «Запись на тренировки „Жми“: жму „Записаться“ на йогу — в console.log массив уже с моей записью, а список „Мои тренировки“ на экране пустой. Появляется, только если записаться ещё куда-нибудь или перезагрузить страницу».",
    "Почему список «Мои тренировки» не обновляется и как это исправить?",
    "push меняет тот же массив, который уже лежит в state, и setBooked получает ту же ссылку. React сравнивает новое значение со старым через Object.is: ссылка та же — значит, ничего не изменилось, и перерендер пропускается. console.log при этом показывает обновлённый массив, что и сбивает с толку. Правильно создавать новый массив: setBooked([...booked, training]), а ещё лучше функциональной формой setBooked((prev) => [...prev, training]), чтобы не зависеть от устаревшего значения. То же правило для объектов: { ...obj, field: value } вместо obj.field = value. Остальные версии мимо: setState ничего не возвращает, и await его не ускорит; ключи должны быть уникальны только среди соседей в одном списке; а начальное значение useState используется лишь при первом рендере.",
    ["Какой массив передаётся в setBooked — новый или тот же самый?",
     "Как React решает, изменилось ли состояние?"],
    code="""import { useState } from 'react';

export default function Schedule({ trainings }) {
  const [booked, setBooked] = useState([]);

  function book(training) {
    booked.push(training);
    setBooked(booked);
    console.log(booked);
  }

  return (
    <div>
      {trainings.map((t) => (
        <button key={t.id} onClick={() => book(t)}>
          Записаться: {t.title}
        </button>
      ))}
      <h2>Мои тренировки</h2>
      <ul>
        {booked.map((t) => (
          <li key={t.id}>{t.title}</li>
        ))}
      </ul>
    </div>
  );
}
""",
    language="jsx",
    options=[
        "setBooked асинхронный — нужно написать await setBooked(booked), тогда список успеет обновиться",
        "push меняет тот же массив, и setBooked получает прежнюю ссылку — React не видит изменений, нужен новый массив",
        "У кнопок и у <li> одинаковые key из t.id — React путает элементы, к ключам нужно добавить префикс",
        "console.log в обработчике выполняется до рендера и блокирует его — лог нужно убрать",
        "useState([]) создаёт новый пустой массив на каждом рендере, поэтому записи теряются — вынести [] в константу",
    ],
    answer=1,
))

TASKS.append(task(
    "fe-react-basics-05", "incident", 3, "devops_colleague",
    "Дима, DevOps: «После релиза в 14:05 страница „Избранное“ в Маркете — белый экран у всех, в Sentry сотни событий. Кроме этой страницы в релизе ничего не менялось. Лог и дифф ниже — откатываем или чиним вперёд? Решай быстро».",
    "Что стало причиной и что делать? Выбери все верные утверждения и действия.",
    "Ошибка #301 — это Too many re-renders: setState вызывается во время рендера, рендер снова вызывает setState, и так по кругу, пока React не остановится. В диффе таких мест два. setCount(items.length) выполняется в теле компонента на каждом рендере, а count вообще не нужен в state — это items.length, его считают прямо в рендере. onClick={setFilter('stock')} вызывает setFilter сразу при рендере, а в onClick попадает результат вызова; правильно onClick={() => setFilter('stock')}. Белый экран у всех — сначала откат на v2.30.0, чтобы вернуть сервис, потом спокойный фикс и выкладка. Медленный filter и кэш CDN тут ни при чём (ошибка — ровно в новом коде релиза), а ErrorBoundary лишь заменит белый экран сообщением об ошибке: страница всё равно не работает, поэтому сначала откат.",
    ["Что вызывается при каждом рендере Favorites?",
     "В onClick передают функцию или результат её вызова?",
     "Прод лежит у всех — что быстрее вернёт сервис: откат или новый фикс?"],
    time_limit=15,
    code="""[Sentry] Error: Minified React error #301; visit https://react.dev/errors/301 for the full message
  #301: Too many re-renders. React limits the number of renders to prevent an infinite loop.
    at Favorites (src/Favorites.jsx:7:3)
  events: 412   users: 389   first seen: 14:06   release: market-web@2.31.0

$ git diff v2.30.0 v2.31.0 -- src/Favorites.jsx
@@ -4,10 +4,13 @@ import { useState } from 'react';
 export default function Favorites({ items }) {
   const [filter, setFilter] = useState('all');
+  const [count, setCount] = useState(0);
+  setCount(items.length);
   const visible = filter === 'all' ? items : items.filter((i) => i.inStock);

   return (
     <div>
-      <button onClick={() => setFilter('stock')}>В наличии</button>
+      <button onClick={setFilter('stock')}>В наличии</button>
+      <span>Всего: {count}</span>
       <FavoritesList items={visible} />
     </div>
   );
""",
    language="text",
    options=[
        "setCount(items.length) в теле компонента: каждый рендер вызывает setState, и рендеры идут по кругу",
        "Причина — items.filter на каждом рендере: на большом избранном он блокирует поток, и React падает",
        "onClick={setFilter('stock')} вызывает setFilter при рендере — нужно onClick={() => setFilter('stock')}",
        "Прод лежит у всех: сначала откатить на v2.30.0, потом чинить и выкладывать заново",
        "Обернуть Favorites в ErrorBoundary — белый экран сменится сообщением, и откатываться не придётся",
        "count не нужен в state: это items.length, его можно посчитать прямо в рендере",
        "Сбросить кэш CDN — у части пользователей застрял старый бандл, отсюда и ошибки в Sentry",
    ],
    answer=[0, 2, 3, 5],
))

def shuffle_options(all_tasks, salt="f2"):
    """В исходнике варианты идут в удобном для автора порядке; в выдаче — детерминированно перемешаны."""
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

data = {"track": "frontend", "part": "f2", "topics": TOPICS, "tasks": TASKS}
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)

if __name__ == "__main__":
    from collections import Counter
    print("tasks:", len(TASKS), dict(Counter(x["type"] for x in TASKS)))
    for tp in TOPICS:
        print(tp["topic_id"], "theory:", len(tp["theory"]))
