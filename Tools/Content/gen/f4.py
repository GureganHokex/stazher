#!/usr/bin/env python3
# Генератор контента «Стажёра»: track frontend, part f4 (middle).
# Темы: fe-ts-advanced, fe-state, fe-performance, fe-event-loop, fe-cors.
import json, os

OUT = "Tools/Content/out/frontend_f4.json"
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}


def xp(d, mult=1.5):
    return int(round(XP_BASE[d] * mult / 5.0)) * 5


def task(tid, topic, typ, d, character, story, question, explanation, hints,
         code=None, language=None, options=None, answer=None, tests=None, entry=None, time_limit=None):
    content = {
        "question": question,
        "code": code,
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content.update({"options": options, "correct_answer": answer, "test_cases": tests})
    return {
        "task_id": tid,
        "topic_id": topic,
        "grade": "middle",
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
# fe-ts-advanced
# =====================================================================
TOPICS.append({"topic_id": "fe-ts-advanced", "theory": """На уровне Middle TypeScript — не подписи к переменным, а способ сделать так, чтобы неправильный код не собирался. Главные инструменты — дженерики, утилитарные типы, сужение и discriminated unions.

Дженерики. Функция работает с любым типом, но сохраняет связь между входом и выходом. Ограничение extends говорит, что тип обязан уметь. K extends keyof T — «K — один из ключей T», а T[K] — тип значения по этому ключу:

    function pluck<T, K extends keyof T>(items: readonly T[], key: K): T[K][] {
      return items.map((item) => item[key]);
    }
    pluck(products, 'price');  // number[]
    pluck(products, 'prise');  // ошибка компиляции

Утилитарные типы выводят новые типы из существующих, чтобы не копировать поля руками:
• Partial<T> — все поля необязательные (тело PATCH), Required<T> — наоборот;
• Pick<T, 'a' | 'b'> — только выбранные поля, Omit<T, 'id'> — все, кроме указанных;
• Record<K, V> — объект ровно с ключами K: Record<Status, string> не даст забыть ни один статус;
• Readonly<T>, ReturnType<typeof fn>, Awaited<T>, T['id'] (indexed access) — тоже пригодятся.

Сужение типов (narrowing). Внутри if TypeScript уточняет тип: typeof x === 'string', x instanceof Error, 'field' in x, x !== null. Свои проверки — type guard с предикатом value is T. Предикат — обещание компилятору: если проверка внутри врёт, врёт и тип.

Discriminated union — объединение вариантов с общим полем-литералом. У каждого варианта только свои поля, и у отменённого заказа просто нет trackingNumber:

    type Order =
      | { status: 'pending' }
      | { status: 'shipped'; trackingNumber: string }
      | { status: 'cancelled'; reason: string };

Исчерпывающий switch: в default значение присваивается переменной типа never. Добавят в union новый вариант и забудут case — сборка упадёт, а не прод:

    default: {
      const unreachable: never = order;
      throw new Error(`Неизвестный статус: ${JSON.stringify(unreachable)}`);
    }

Типизация ответа API. res.json() возвращает Promise<any>, а any молча превращается во что угодно. Подпись Promise<Product[]> ничего не проверяет: типы стираются при сборке, в рантайме их нет. Как правильно:
• описывать реальный контракт (Paginated<T> = { items: T[]; total: number }), а лучше генерировать типы из OpenAPI;
• сырой ответ считать unknown и проверять форму на границе: type guard или схема (zod, valibot);
• возвращать результат, который нельзя использовать без проверки ошибки: { ok: true; data: T } | { ok: false; error: string }.

Частые ошибки:
• any и as там, где лень думать: as не проверяет, а приказывает компилятору поверить;
• «суп» из необязательных полей вместо union: status: string и пять полей с «?»;
• default: return 'Неизвестно' в switch по union — новый вариант пройдёт незаметно;
• ручные копии типов, которые со временем разъезжаются с оригиналом."""})

# ---- fe-ts-advanced-01: дженерик с ограничением keyof ----
TS01_START = """type Product = {
  id: number;
  title: string;
  price: number;
  stock: number;
};

export function sortBy(items: any[], key: string, dir: 'asc' | 'desc' = 'asc'): any[] {
  return items.sort((a, b) => {
    const cmp = a[key] > b[key] ? 1 : a[key] < b[key] ? -1 : 0;
    return dir === 'asc' ? cmp : -cmp;
  });
}

// в таблице товаров: sortBy(products, column.key, direction)
"""
TS01_ANSWER = """type Product = {
  id: number;
  title: string;
  price: number;
  stock: number;
};

export function sortBy<T, K extends keyof T>(
  items: readonly T[],
  key: K,
  dir: 'asc' | 'desc' = 'asc',
): T[] {
  return [...items].sort((a, b) => {
    const cmp = a[key] > b[key] ? 1 : a[key] < b[key] ? -1 : 0;
    return dir === 'asc' ? cmp : -cmp;
  });
}

// sortBy(products, 'price')  — ок, вернёт Product[]
// sortBy(products, 'prise')  — ошибка компиляции
"""
TASKS.append(task(
    "fe-ts-advanced-01", "fe-ts-advanced", "write_code", 3, "qa",
    "Ира: «В админке Маркета сортировка по колонке „Цена“ ничего не делает. Нашла: в конфиге колонки опечатка — ключ 'prise', а TypeScript промолчал. И ещё после сортировки таблицы у меня поменялся порядок в соседнем виджете „Популярное“ — он получает тот же массив».",
    "Перепиши sortBy как дженерик: T — тип элемента, K ограничен ключами T (K extends keyof T), key имеет тип K, items — массив T, результат — T[]. Без any. Исходный массив не мутируй: сортируй копию ([...items], slice() или toSorted()).",
    "Ограничение K extends keyof T связывает ключ с типом элемента: для Product допустимы только 'id' | 'title' | 'price' | 'stock', и опечатка 'prise' становится ошибкой компиляции, а не тихим багом. Дженерик T сохраняет тип на выходе: из sortBy(products, 'price') вернётся Product[], а не any[], так что дальше по коду тоже работают подсказки и проверки. Вторая проблема — Array.prototype.sort сортирует на месте и мутирует массив, который пришёл снаружи; если его же рендерит другой виджет, порядок «поедет» и там. Копия через [...items] или toSorted() делает функцию чистой, а readonly T[] в параметре явно обещает вызывающему, что его массив не тронут.",
    ["Какой тип должен быть у key, чтобы туда нельзя было передать произвольную строку?",
     "Сигнатура: sortBy<T, K extends keyof T>(items: readonly T[], key: K, ...): T[].",
     "sort() меняет массив на месте — отсортируй копию: [...items].sort(...)."],
    code=TS01_START, language="typescript", answer=TS01_ANSWER,
    tests=[
        rx("Дженерик с ограничением K extends keyof T", r"sortBy\s*<\s*T\b[^,>]*,\s*K\s+extends\s+keyof\s+T\s*>"),
        rx("Параметр key имеет тип K", r"\bkey\s*:\s*K\b"),
        rx("items — массив T", r"\bitems\s*:\s*(?:readonly\s+T\s*\[\s*\]|T\s*\[\s*\]|ReadonlyArray\s*<\s*T\s*>|Array\s*<\s*T\s*>)"),
        rx("Функция возвращает T[]", r"\)\s*:\s*(?:T\s*\[\s*\]|Array\s*<\s*T\s*>)\s*\{"),
        nrx("Нет any", r"\bany\b"),
        rx("Сортируется копия, а не исходный массив", r"\[\s*\.\.\.\s*items\s*\]\s*\.sort\(|items\s*\.slice\(\s*\)\s*\.sort\(|items\s*\.toSorted\(|Array\.from\(\s*items\s*\)\s*\.sort\("),
    ]))

# ---- fe-ts-advanced-02: find_bug, any из res.json() ----
TS02_CODE = """// api/products.ts
export type Product = { id: number; title: string; price: number };

// Контракт бэка: было   GET /api/products -> Product[]
// с релиза бэка 3.12:   GET /api/products -> { items: Product[]; total: number; page: number }

export async function fetchProducts(page = 1): Promise<Product[]> {
  const res = await fetch(`/api/products?page=${page}`);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

// catalog/loadCatalog.ts
export async function loadCatalog(): Promise<string[]> {
  const products = await fetchProducts();
  return products.map((p) => `${p.title} — ${p.price} ₽`);
}

// Sentry: TypeError: products.map is not a function (1 283 события за 20 минут)
"""
TASKS.append(task(
    "fe-ts-advanced-02", "fe-ts-advanced", "find_bug", 3, "devops_colleague",
    "Дима: «Ночью выкатили бэк Маркета, и каталог пустой: в Sentry сотни TypeError. Бэкендеры откатываться не хотят — говорят, про пагинацию в списке товаров предупреждали заранее. Но у вас же TypeScript, почему сборка фронта не упала?»",
    "Почему TypeScript не заметил изменения контракта и как это правильно исправить?",
    "res.json() в типах DOM объявлен как Promise<any>, а any совместим с чем угодно — компилятор без вопросов принимает его как Product[]. Аннотация Promise<Product[]> — это обещание, которое никто не проверяет: типы стираются при сборке, и в рантайме приходит объект { items, total, page }, у которого нет map. strict тут не поможет — TypeScript в принципе не видит, что вернёт сервер. Каст через as и дженерик у fetch (его у fetch нет) — тот же самообман в другой обёртке. Правильно — описать реальный контракт (Paginated<Product>), вернуть data.items и проверять форму ответа на границе с API: схемой (zod, valibot) или type guard, а типы по возможности генерировать из OpenAPI, чтобы изменение контракта ломало сборку.",
    ["Какой тип возвращает res.json() по объявлению в lib.dom?",
     "Существуют ли типы TypeScript во время выполнения программы?"],
    code=TS02_CODE, language="typescript",
    options=[
        "В tsconfig не включён strict — со strict: true компилятор увидел бы, что сервер теперь присылает объект",
        "res.json() отдаёт any, и он молча стал Product[] — описать Paginated<Product>, вернуть items, проверять ответ схемой",
        "Написать return res.json() as Product[] — явное приведение заставит TypeScript проверить, что пришёл массив",
        "Вызывать fetch с дженериком: fetch<Product[]>(url) — тогда TypeScript проверит тело ответа при сборке",
        "Не хватает await перед res.json(): без него функция возвращает промис вместо массива, отсюда TypeError",
    ],
    answer=1))

# ---- fe-ts-advanced-03: Partial / Pick / Omit / Record ----
TS03_START = """export type ProductStatus = 'draft' | 'active' | 'archived';

export interface Product {
  id: number;
  title: string;
  price: number;
  stock: number;
  status: ProductStatus;
  createdAt: string;
}

// TODO: типы ниже скопированы руками и уже разъехались с Product
export type NewProduct = {
  title: string;
  price: number;
  stock: number;
};

export type ProductPatch = {
  title: string;
  price: number;
  stock: number;
};

export const statusLabels: { [key: string]: string } = {
  draft: 'Черновик',
  active: 'В продаже',
};

// request<T> — наша обёртка над fetch с проверкой ответа (api/client.ts)
declare function request<T>(url: string, init?: RequestInit): Promise<T>;

export function createProduct(data: NewProduct): Promise<Product> {
  return request<Product>('/api/admin/products', { method: 'POST', body: JSON.stringify(data) });
}

export function updateProduct(id: number, patch: ProductPatch): Promise<Product> {
  return request<Product>(`/api/admin/products/${id}`, { method: 'PATCH', body: JSON.stringify(patch) });
}
"""
TS03_ANSWER = """export type ProductStatus = 'draft' | 'active' | 'archived';

export interface Product {
  id: number;
  title: string;
  price: number;
  stock: number;
  status: ProductStatus;
  createdAt: string;
}

// id и createdAt выдаёт сервер
export type NewProduct = Omit<Product, 'id' | 'createdAt'>;

// в PATCH уходят только изменённые поля
export type ProductPatch = Partial<Pick<Product, 'title' | 'price' | 'stock'>>;

export const statusLabels: Record<ProductStatus, string> = {
  draft: 'Черновик',
  active: 'В продаже',
  archived: 'В архиве',
};

// request<T> — наша обёртка над fetch с проверкой ответа (api/client.ts)
declare function request<T>(url: string, init?: RequestInit): Promise<T>;

export function createProduct(data: NewProduct): Promise<Product> {
  return request<Product>('/api/admin/products', { method: 'POST', body: JSON.stringify(data) });
}

export function updateProduct(id: Product['id'], patch: ProductPatch): Promise<Product> {
  return request<Product>(`/api/admin/products/${id}`, { method: 'PATCH', body: JSON.stringify(patch) });
}
"""
TASKS.append(task(
    "fe-ts-advanced-03", "fe-ts-advanced", "write_code", 4, "manager",
    "Стас: «В админке Маркета две беды. Товар без статуса создаётся, хотя статус обязателен. А архивные товары в списке без бейджа — просто пустое место. И поле редактирования цены почему-то требует заново заполнить название. Марина говорит, всё из-за типов, скопированных руками. Поправь, пожалуйста».",
    "Выведи типы из Product утилитарными типами вместо копий: NewProduct — через Omit (всё, кроме id и createdAt); ProductPatch — через Partial и Pick (title, price, stock — все необязательные); statusLabels — Record<ProductStatus, string>, допиши archived: 'В архиве'; параметр id в updateProduct — Product['id'].",
    "Ручные копии типов расходятся с оригиналом: в NewProduct забыли status — и форма создаёт товар без статуса, а ProductPatch требует все поля, хотя PATCH должен уметь менять одно. Omit<Product, 'id' | 'createdAt'> автоматически подхватит любое новое поле Product, а Partial<Pick<...>> честно описывает «любое подмножество из этих трёх полей». Index signature { [key: string]: string } разрешает любые ключи и не требует ни одного, поэтому забытый archived никто не заметил; Record<ProductStatus, string> требует ровно все статусы — добавят новый статус в union, и компилятор покажет, где не хватает подписи. Product['id'] (indexed access) связывает тип параметра с моделью: если id станет строкой (UUID), сигнатура обновится сама.",
    ["Какой утилитарный тип убирает поля, а какой — оставляет только выбранные?",
     "PATCH-тело: Partial<Pick<Product, 'title' | 'price' | 'stock'>>.",
     "Record<ProductStatus, string> заставит перечислить все статусы — компилятор подскажет, какого не хватает."],
    code=TS03_START, language="typescript", answer=TS03_ANSWER,
    tests=[
        rx("NewProduct = Omit<Product, 'id' | 'createdAt'>", r"type\s+NewProduct\s*=\s*Omit\s*<\s*Product\s*,\s*['\"](?:id|createdAt)['\"]\s*\|\s*['\"](?:id|createdAt)['\"]\s*>"),
        rx("ProductPatch — Partial и Pick по title, price, stock",
           r"type\s+ProductPatch\s*=\s*(?:Partial\s*<\s*Pick\s*<\s*Product\s*,(?=[^>]*['\"]title['\"])(?=[^>]*['\"]price['\"])(?=[^>]*['\"]stock['\"])[^>]*>\s*>|Pick\s*<\s*Partial\s*<\s*Product\s*>\s*,(?=[^>]*['\"]title['\"])(?=[^>]*['\"]price['\"])(?=[^>]*['\"]stock['\"])[^>]*>)"),
        rx("statusLabels типизирован как Record<ProductStatus, string>", r"statusLabels\s*:\s*Record\s*<\s*ProductStatus\s*,\s*string\s*>"),
        rx("Есть подпись для archived", r"archived\s*:\s*['\"]В архиве['\"]"),
        rx("id в updateProduct — Product['id']", r"updateProduct\s*\(\s*id\s*:\s*Product\s*\[\s*['\"]id['\"]\s*\]"),
        nrx("Нет index signature [key: string]", r"\[\s*key\s*:\s*string\s*\]"),
    ]))

# ---- fe-ts-advanced-04: discriminated union + never ----
TS04_START = """export interface Order {
  id: string;
  status: string; // 'pending' | 'shipped' | 'delivered' | 'cancelled'
  trackingNumber?: string;
  deliveredAt?: string;
  cancelReason?: string;
}

export function orderStatusText(order: Order): string {
  if (order.status === 'pending') return 'Собираем заказ';
  if (order.status === 'shipped') return `В пути, трек ${order.trackingNumber}`;
  if (order.status === 'cancelled') return `Отменён: ${order.cancelReason}`;
  return `Доставлен ${order.deliveredAt}`;
}
"""
TS04_ANSWER = """type OrderBase = { id: string };

export type Order =
  | (OrderBase & { status: 'pending' })
  | (OrderBase & { status: 'shipped'; trackingNumber: string })
  | (OrderBase & { status: 'delivered'; deliveredAt: string })
  | (OrderBase & { status: 'cancelled'; cancelReason: string })
  | (OrderBase & { status: 'refunded'; refundedAt: string });

function assertNever(value: never): never {
  throw new Error(`Неизвестный статус заказа: ${JSON.stringify(value)}`);
}

export function orderStatusText(order: Order): string {
  switch (order.status) {
    case 'pending':
      return 'Собираем заказ';
    case 'shipped':
      return `В пути, трек ${order.trackingNumber}`;
    case 'delivered':
      return `Доставлен ${order.deliveredAt}`;
    case 'cancelled':
      return `Отменён: ${order.cancelReason}`;
    case 'refunded':
      return `Деньги вернули ${order.refundedAt}`;
    default:
      return assertNever(order);
  }
}
"""
TASKS.append(task(
    "fe-ts-advanced-04", "fe-ts-advanced", "write_code", 4, "teamlead",
    "Марина: «Бэк добавил статус заказа refunded. Клиентка открыла „Мои заказы“ и увидела „Доставлен undefined“ у заказа, за который ей вернули деньги. Статус у нас — просто string, полей с вопросиками пять, а в конце — return „на всё остальное“. Хочу, чтобы в следующий раз новый статус ломал сборку, а не прод».",
    "Опиши Order как discriminated union по полю status: pending; shipped с trackingNumber; delivered с deliveredAt; cancelled с cancelReason; refunded с refundedAt (все поля обязательные, string, у каждого варианта — только свои). Перепиши orderStatusText на switch (order.status) с case для каждого статуса (для refunded — «Деньги вернули <дата>»), а в default — только проверка исчерпываемости через never: const unreachable: never = order или assertNever(order).",
    "Когда status — просто string, а поля необязательные, TypeScript не знает, какие поля есть у какого статуса, и не может проверить, что все статусы обработаны: новый refunded провалился в последний return и вывел undefined. В discriminated union литерал status — «дискриминант»: внутри case 'shipped' компилятор сужает тип и знает, что trackingNumber точно есть, а обратиться к нему в case 'cancelled' уже нельзя. В default после всех case тип order сужается до never; если кто-то добавит в union новый вариант и забудет case, там окажется не never, и присваивание в never не скомпилируется. assertNever вдобавок бросает понятную ошибку в рантайме, если бэк пришлёт статус, которого нет в типах. Главное — не ставить в default «запасной» текст: он глушит именно ту проверку, ради которой всё затевалось.",
    ["Вынеси статусы в литеральные типы: status: 'shipped' вместо status: string.",
     "Каждый вариант union — свой объект со своими обязательными полями; общее id можно вынести в базовый тип и склеить через &.",
     "В default: const unreachable: never = order; — не скомпилируется, если какой-то статус забыт."],
    code=TS04_START, language="typescript", answer=TS04_ANSWER,
    tests=[
        rx("Есть вариант со status: 'refunded'", r"status\s*:\s*['\"]refunded['\"]"),
        rx("У shipped свой обязательный trackingNumber", r"['\"]shipped['\"][^}|]*trackingNumber\s*:\s*string|trackingNumber\s*:\s*string[^}|]*['\"]shipped['\"]"),
        nrx("status больше не string", r"status\s*:\s*string"),
        nrx("Поля статусов не необязательные", r"(?:trackingNumber|deliveredAt|cancelReason|refundedAt)\s*\?\s*:"),
        rx("switch по order.status", r"switch\s*\(\s*order\.status\s*\)"),
        rx("Есть case 'refunded'", r"case\s+['\"]refunded['\"]\s*:"),
        rx("Проверка исчерпываемости через never", r":\s*never\s*=\s*order\b|assertNever\s*\(\s*order\s*\)"),
    ]))

# ---- fe-ts-advanced-05: типизация ответа API через unknown + type guard ----
TS05_START = """export interface Review {
  id: number;
  author: string;
  rating: number;
  text: string;
}

// TODO: ApiResult<T> — либо данные, либо ошибка

export async function fetchReviews(productId: number): Promise<any> {
  const res = await fetch(`/api/products/${productId}/reviews`);
  const body = await res.json();
  return body as Review[];
}
"""
TS05_ANSWER = """export interface Review {
  id: number;
  author: string;
  rating: number;
  text: string;
}

export type ApiResult<T> =
  | { ok: true; data: T }
  | { ok: false; error: string };

export function isReview(value: unknown): value is Review {
  return (
    typeof value === 'object' &&
    value !== null &&
    'id' in value && typeof value.id === 'number' &&
    'author' in value && typeof value.author === 'string' &&
    'rating' in value && typeof value.rating === 'number' &&
    'text' in value && typeof value.text === 'string'
  );
}

export async function fetchReviews(productId: number): Promise<ApiResult<Review[]>> {
  const res = await fetch(`/api/products/${productId}/reviews`);
  if (!res.ok) {
    return { ok: false, error: `HTTP ${res.status}` };
  }
  const body: unknown = await res.json();
  if (!Array.isArray(body) || !body.every(isReview)) {
    return { ok: false, error: 'Неожиданный формат ответа' };
  }
  return { ok: true, data: body };
}

// const result = await fetchReviews(812);
// if (!result.ok) showError(result.error); else renderReviews(result.data);
"""
TASKS.append(task(
    "fe-ts-advanced-05", "fe-ts-advanced", "write_code", 5, "teamlead",
    "Марина: «После истории с пагинацией договорились: на границе с API — никаких any и слепых as. Начни с отзывов: сейчас fetchReviews возвращает any, при 500 мы пытаемся рендерить HTML-страницу ошибки как массив, а компонент вообще не узнаёт, что что-то пошло не так. Zod пока не тащим — сделай на чистом TypeScript».",
    "Доведи модуль до ума: 1) type ApiResult<T> — union по полю ok: { ok: true; data: T } | { ok: false; error: string }; 2) isReview(value: unknown): value is Review — type guard, проверяет typeof всех четырёх полей; 3) fetchReviews(productId: number): Promise<ApiResult<Review[]>> — при !res.ok возвращает ошибку, тело ответа кладёт в переменную типа unknown, проверяет Array.isArray и every(isReview). Без any и без as.",
    "unknown — честный тип для данных извне: с ним нельзя ничего сделать, пока не докажешь форму, в отличие от any, который разрешает всё. Type guard с предикатом value is Review превращает рантайм-проверку в знание компилятора: после body.every(isReview) TypeScript считает body массивом Review, и никакой каст не нужен. Проверка 'id' in value с последующим typeof value.id работает без as благодаря сужению через in (TypeScript 4.9+). ApiResult<T> — discriminated union по ok: чтобы добраться до data, компонент обязан проверить result.ok, а значит, обработать ошибку — забыть про неё уже нельзя. Проверка res.ok до парсинга отсекает ответы 500 и 404, а проверка формы ловит случай, когда бэк молча поменял контракт. В большом проекте ту же роль играют схемы zod/valibot, но принцип тот же: граница с внешним миром — unknown и проверка.",
    ["Какой тип дать сырому телу ответа, чтобы компилятор запретил пользоваться им без проверки?",
     "Type guard: function isReview(value: unknown): value is Review { ... typeof value.id === 'number' ... }.",
     "Сначала if (!res.ok) return { ok: false, ... }, потом const body: unknown = await res.json() и Array.isArray(body) && body.every(isReview)."],
    code=TS05_START, language="typescript", answer=TS05_ANSWER,
    tests=[
        rx("ApiResult<T>: вариант успеха с ok: true и data: T", r"type\s+ApiResult\s*<\s*T\s*>\s*=[\s\S]*?\{(?=[^}]*\bok\s*:\s*true\b)(?=[^}]*\bdata\s*:\s*T\b)[^}]*\}"),
        rx("ApiResult<T>: вариант ошибки с ok: false и error: string", r"\{(?=[^}]*\bok\s*:\s*false\b)(?=[^}]*\berror\s*:\s*string\b)[^}]*\}"),
        rx("isReview — type guard от unknown", r"(?:function\s+isReview\s*|isReview\s*=\s*)\(\s*\w+\s*:\s*unknown\s*\)\s*:\s*\w+\s+is\s+Review\b"),
        rx("fetchReviews возвращает Promise<ApiResult<Review[]>>", r"fetchReviews\s*\([^)]*\)\s*:\s*Promise\s*<\s*ApiResult\s*<\s*(?:Review\s*\[\s*\]|Array\s*<\s*Review\s*>)\s*>\s*>"),
        rx("Тело ответа — unknown", r":\s*unknown\s*=\s*await\s+res\.json\(\s*\)"),
        rx("Проверка статуса ответа res.ok", r"\bres\.ok\b"),
        rx("Проверка массива через every(isReview)", r"Array\.isArray\s*\([\s\S]*\.every\s*\(\s*(?:isReview|\(?\s*\w+\s*\)?\s*=>\s*isReview\s*\(\s*\w+\s*\))\s*\)"),
        nrx("Нет any", r"\bany\b"),
        nrx("Нет приведения через as", r"\bas\s+(?!const\b)[A-Za-z_{\[]"),
    ]))

# =====================================================================
# fe-state
# =====================================================================
TOPICS.append({"topic_id": "fe-state", "theory": """Состояние — это данные, от которых зависит интерфейс. Middle сначала решает, какого рода это состояние, и только потом — где его хранить.

• Локальное UI-состояние: открыта ли модалка, текст в поле, активная вкладка. Живёт в useState/useReducer компонента; поднимай выше, только когда оно реально нужно соседям.
• Состояние в URL: фильтры, сортировка, страница, поисковый запрос. Ссылку можно отправить коллеге, работает кнопка «Назад».
• Глобальное клиентское состояние: корзина гостя, тема, черновики — то, что нужно многим компонентам и чего нет на сервере. Место ему — стор: Zustand, Redux Toolkit.
• Серверное состояние: товары, заказы, отзывы. Это кэш чужих данных: он устаревает, его надо перезапрашивать, дедуплицировать и инвалидировать. Для этого есть TanStack Query (или RTK Query), а не useEffect + useState + флаги isLoading руками.

Контекст против стора. Context — способ передать значение вниз без пропсов, а не хранилище. Когда value меняется, перерисовываются все, кто вызвал useContext, и подписаться на «кусочек» нельзя. Поэтому контекст хорош для редко меняющегося: тема, текущий пользователь, локаль, QueryClient. Часто меняющиеся данные, которые читают многие, — в стор с подпиской через селектор: компонент перерисуется, только если изменился его кусок.

Редьюсер — чистая функция (state, action) => newState:
• state не мутируем: возвращаем новый объект и копируем только изменённые ветки ({ ...state, items: [...] });
• никаких побочных эффектов — запросов, таймеров, Math.random;
• неизвестный экшен — тот же state (та же ссылка = «ничего не изменилось»).
React, Redux и мемоизация сравнивают по ссылке (===). Мутация сохраняет ссылку, и изменение становится невидимым: не перерисовывается компонент, отдаёт старый результат мемоизированный селектор, ломается undo и история в Redux DevTools. В createSlice из Redux Toolkit писать state.items.push(...) можно — Immer сам сделает копию; в обычном редьюсере — нельзя.

Селекторы достают и вычисляют данные из стора: selectCartTotal(state). Производное (сумму корзины, отфильтрованный список) не храни в сторе — вычисляй. Если селектор возвращает новый массив или объект (filter, map), его мемоизируют (createSelector из reselect): пересчёт только при изменении входов, иначе — тот же результат по ссылке. Без этого useSelector перерисовывает компонент на каждое изменение стора.

TanStack Query, основное:

    const { data, isPending, error } = useQuery({
      queryKey: ['reviews', productId],
      queryFn: () => fetchReviews(productId),
      staleTime: 60_000,
    });

    const qc = useQueryClient();
    const addReview = useMutation({
      mutationFn: postReview,
      onSuccess: () => qc.invalidateQueries({ queryKey: ['reviews', productId] }),
    });

• queryKey — адрес данных в кэше: одинаковый ключ в двух компонентах — один запрос;
• staleTime — сколько данные считаются свежими; gcTime — сколько неиспользуемый кэш живёт в памяти;
• после изменения — invalidateQueries или оптимистичное обновление через setQueryData.

Частые ошибки: всё подряд в глобальном сторе; копия серверных данных в Redux «чтобы было под рукой» — два источника правды; один огромный контекст на всё приложение; мутация state в редьюсере."""})

# ---- fe-state-01: architecture, где какое состояние ----
TASKS.append(task(
    "fe-state-01", "fe-state", "architecture", 3, "teamlead",
    "Гена: «Собираем новую страницу каталога Маркета: фильтры и сортировка, список товаров с API, счётчик корзины в шапке, модалка быстрого просмотра. Джун предлагает сложить всё в один AppContext с useState — „зачем нам ещё библиотеки“. Как разложишь состояние?»",
    "Какое распределение состояния правильное для этой страницы?",
    "У четырёх частей разная природа, и хранить их стоит по-разному. Флаг модалки нужен одному компоненту — useState рядом с ним, и его изменение ничего больше не перерисовывает. Фильтры в URL дают ссылку, которой можно поделиться, и рабочую кнопку «Назад». Товары — серверное состояние: TanStack Query с ключом ['products', filters] сам кэширует, дедуплицирует запросы и перезапрашивает при смене фильтров, без ручных isLoading. Корзину читают шапка и карточки, и меняется она часто — стор с селекторами перерисует только тех, кому нужен изменившийся кусок. Один AppContext перерисует всех потребителей на каждое изменение любой части, Redux «для всего» заставит вручную писать кэш серверных данных, а проброс корзины пропсами через всё дерево — prop drilling, который больно поддерживать.",
    ["Раздели данные по природе: чьё это состояние и кому оно нужно?",
     "Товары с API — это кэш данных сервера. Какой инструмент заточен именно под это?",
     "Чем хорош URL для фильтров с точки зрения пользователя?"],
    options=[
        "Всё в один AppContext на уровне App: фильтры, товары, корзина и флаг модалки — один источник правды без лишних библиотек",
        "Модалка — useState рядом, фильтры — в URL, товары — TanStack Query по ['products', filters], корзина — Zustand с селекторами",
        "Всё в глобальный Redux-стор: товары грузить в useEffect и класть туда же, флаг модалки тоже в стор — вдруг понадобится открыть её из другого места",
        "Фильтры, товары и модалку — в useState страницы каталога, а корзину прокидывать пропсами от App через шапку и каталог до каждой карточки",
    ],
    answer=1))

# ---- fe-state-02: write_code, иммутабельный редьюсер корзины ----
ST02_HARNESS = """
// Не меняй: так проверяется редьюсер — после каждого экшена сохраняем снимок состояния.
// Если редьюсер мутирует state, старые снимки «поедут» вместе с новым.
function replay(initial, actions) {
  const history = [];
  let state = initial;
  for (const action of actions) {
    const next = cartReducer(state, action);
    history.push({ same: next === state, state: next });
    state = next;
  }
  return history;
}
"""
ST02_START = """// Состояние корзины: { items: [{ id, price, qty }], ...другие поля (например, promo) }
// Экшены:
//   { type: 'add', item: { id, price } }  — добавить 1 шт. (если уже есть — qty + 1, в конец не дублировать)
//   { type: 'setQty', id, qty }           — задать количество; qty <= 0 удаляет позицию
//   { type: 'remove', id }                — удалить позицию
//   { type: 'clear' }                     — очистить корзину
// Неизвестный экшен — вернуть тот же объект state.

function cartReducer(state, action) {
  // твой код
  return state;
}
""" + ST02_HARNESS
ST02_ANSWER = """// Состояние корзины: { items: [{ id, price, qty }], ...другие поля (например, promo) }
// Экшены:
//   { type: 'add', item: { id, price } }  — добавить 1 шт. (если уже есть — qty + 1, в конец не дублировать)
//   { type: 'setQty', id, qty }           — задать количество; qty <= 0 удаляет позицию
//   { type: 'remove', id }                — удалить позицию
//   { type: 'clear' }                     — очистить корзину
// Неизвестный экшен — вернуть тот же объект state.

function cartReducer(state, action) {
  switch (action.type) {
    case 'add': {
      const exists = state.items.some((i) => i.id === action.item.id);
      const items = exists
        ? state.items.map((i) => (i.id === action.item.id ? { ...i, qty: i.qty + 1 } : i))
        : [...state.items, { ...action.item, qty: 1 }];
      return { ...state, items };
    }
    case 'setQty':
      if (action.qty <= 0) {
        return { ...state, items: state.items.filter((i) => i.id !== action.id) };
      }
      return {
        ...state,
        items: state.items.map((i) => (i.id === action.id ? { ...i, qty: action.qty } : i)),
      };
    case 'remove':
      return { ...state, items: state.items.filter((i) => i.id !== action.id) };
    case 'clear':
      return { ...state, items: [] };
    default:
      return state;
  }
}
""" + ST02_HARNESS
LAMP = {"id": "lamp", "price": 1490}
CABLE = {"id": "cable", "price": 350}


def ci(item, q):
    d = dict(item)
    d["qty"] = q
    return d


TASKS.append(task(
    "fe-state-02", "fe-state", "write_code", 3, "qa",
    "Ира: «Баг в корзине Маркета. Жму „+“ у лампы — в Redux DevTools все прошлые состояния тоже показывают новое количество, кнопка „Отменить последнее действие“ ничего не отменяет, а счётчик в шапке иногда отстаёт на одно нажатие. Гена глянул и сказал, что логика правильная, а редьюсер мутирует state, — переписать его надо тебе».",
    "Напиши cartReducer(state, action) для экшенов add, setQty, remove, clear (формат — в комментарии в коде). Редьюсер не должен мутировать state и вложенные объекты позиций: каждый раз возвращай новый объект с новым массивом items, остальные поля state (например, promo) сохраняй. Неизвестный экшен возвращает тот же объект state. Функцию replay не меняй — через неё идёт проверка.",
    "Redux, React и DevTools сравнивают состояние по ссылке: если редьюсер сделал state.items.push(...) или item.qty += 1, объект остался тем же, и все сохранённые снимки истории указывают на него — поэтому «прошлые» состояния показывают новое значение, а undo откатывается «в то же самое». Иммутабельное обновление копирует только изменённый путь: новый корневой объект ({ ...state }), новый массив items (map/filter/spread) и новый объект изменённой позиции ({ ...i, qty }); неизменённые позиции переиспользуются по ссылке — это дёшево и позволяет memo-компонентам пропускать рендер. Для неизвестного экшена возвращаем тот же state: одинаковая ссылка — сигнал «ничего не изменилось», и подписчики не перерисуются. В createSlice из Redux Toolkit ту же работу за тебя делает Immer, но понимать, что происходит под капотом, нужно.",
    ["Какие методы массива возвращают новый массив, а какие меняют исходный?",
     "Для add: если позиция есть — items.map с { ...i, qty: i.qty + 1 }, иначе [...items, { ...action.item, qty: 1 }].",
     "Всегда возвращай { ...state, items: новыйМассив }, а в default — просто state."],
    code=ST02_START, language="javascript", entry="replay", answer=ST02_ANSWER,
    tests=[
        {"input": [{"items": []}, [{"type": "add", "item": LAMP}, {"type": "add", "item": LAMP}, {"type": "add", "item": CABLE}]],
         "expected": [
             {"same": False, "state": {"items": [ci(LAMP, 1)]}},
             {"same": False, "state": {"items": [ci(LAMP, 2)]}},
             {"same": False, "state": {"items": [ci(LAMP, 2), ci(CABLE, 1)]}},
         ]},
        {"input": [{"items": [ci(LAMP, 2), ci(CABLE, 1)], "promo": "LAMP10"},
                   [{"type": "setQty", "id": "lamp", "qty": 5}, {"type": "setQty", "id": "cable", "qty": 0}]],
         "expected": [
             {"same": False, "state": {"items": [ci(LAMP, 5), ci(CABLE, 1)], "promo": "LAMP10"}},
             {"same": False, "state": {"items": [ci(LAMP, 5)], "promo": "LAMP10"}},
         ]},
        {"input": [{"items": [ci(LAMP, 1), ci(CABLE, 1)]},
                   [{"type": "checkout/started"}, {"type": "remove", "id": "lamp"}, {"type": "clear"}]],
         "expected": [
             {"same": True, "state": {"items": [ci(LAMP, 1), ci(CABLE, 1)]}},
             {"same": False, "state": {"items": [ci(CABLE, 1)]}},
             {"same": False, "state": {"items": []}},
         ]},
        {"input": [{"items": [ci(CABLE, 3)], "promo": None},
                   [{"type": "add", "item": CABLE}, {"type": "add", "item": LAMP}, {"type": "setQty", "id": "cable", "qty": 1}]],
         "expected": [
             {"same": False, "state": {"items": [ci(CABLE, 4)], "promo": None}},
             {"same": False, "state": {"items": [ci(CABLE, 4), ci(LAMP, 1)], "promo": None}},
             {"same": False, "state": {"items": [ci(CABLE, 1), ci(LAMP, 1)], "promo": None}},
         ]},
    ]))

# ---- fe-state-03: find_bug (исполняемый), мутация ломает мемоизированный селектор ----
ST03_COMMON_TOP = """// Мини-версия createSelector из reselect: пересчитывает результат,
// только если какой-то из входов изменился по ссылке.
function createSelector(inputs, compute) {
  let lastArgs = null;
  let lastResult;
  let recomputations = 0;
  const selector = (state) => {
    const args = inputs.map((select) => select(state));
    const changed = lastArgs === null || args.some((arg, i) => arg !== lastArgs[i]);
    if (changed) {
      recomputations += 1;
      lastArgs = args;
      lastResult = compute(...args);
    }
    return lastResult;
  };
  selector.recomputations = () => recomputations;
  return selector;
}

function makeSelectVisibleTitles() {
  return createSelector(
    [(state) => state.products, (state) => state.filters],
    (products, filters) =>
      products
        .filter((p) => !filters.inStockOnly || p.stock > 0)
        .sort((a, b) => (filters.sort === 'cheap' ? a.price - b.price : b.price - a.price))
        .map((p) => p.title)
  );
}
"""
ST03_BOTTOM = """
// Не меняй: так тест «рендерит» каталог после каждого экшена
function scenario(products, actions) {
  const selectVisibleTitles = makeSelectVisibleTitles();
  let state = { products, filters: { inStockOnly: false, sort: 'cheap' } };
  const screens = [selectVisibleTitles(state)];
  for (const action of actions) {
    state = catalogReducer(state, action);
    screens.push(selectVisibleTitles(state));
  }
  return { screens, recomputations: selectVisibleTitles.recomputations() };
}
"""
ST03_BUGGY = ST03_COMMON_TOP + """
function catalogReducer(state, action) {
  switch (action.type) {
    case 'toggleInStock':
      state.filters.inStockOnly = !state.filters.inStockOnly;
      return { ...state };
    case 'setSort':
      return { ...state, filters: { ...state.filters, sort: action.sort } };
    case 'setProducts':
      return { ...state, products: action.products };
    default:
      return state;
  }
}
""" + ST03_BOTTOM
ST03_FIXED = ST03_COMMON_TOP + """
function catalogReducer(state, action) {
  switch (action.type) {
    case 'toggleInStock':
      return { ...state, filters: { ...state.filters, inStockOnly: !state.filters.inStockOnly } };
    case 'setSort':
      return { ...state, filters: { ...state.filters, sort: action.sort } };
    case 'setProducts':
      return { ...state, products: action.products };
    default:
      return state;
  }
}
""" + ST03_BOTTOM
P = [
    {"title": "Лампа Эдисона", "price": 490, "stock": 0},
    {"title": "Торшер «Маяк»", "price": 5990, "stock": 3},
    {"title": "Гирлянда", "price": 1290, "stock": 12},
]
ALL_CHEAP = ["Лампа Эдисона", "Гирлянда", "Торшер «Маяк»"]
IN_CHEAP = ["Гирлянда", "Торшер «Маяк»"]
IN_EXP = ["Торшер «Маяк»", "Гирлянда"]
TOGGLE = {"type": "toggleInStock"}
TASKS.append(task(
    "fe-state-03", "fe-state", "find_bug", 4, "client",
    "Покупатель «Лампового Маркета» пишет в поддержку: «Включаю галочку „Только в наличии“ — список не меняется, лампа, которой нет, так и висит. Потом переключаю сортировку на „Сначала дорогие“ — и вдруг фильтр срабатывает. Вы там издеваетесь?» Ира воспроизвела на стейдже с первого раза.",
    "Каталог показывает список через мемоизированный селектор. Найди, почему после переключения «Только в наличии» список не обновляется, и исправь. Мемоизацию не отключай: селектор должен пересчитываться только когда реально изменились товары или фильтры. Функцию scenario не меняй.",
    "createSelector пересчитывает результат, только если входы изменились по ссылке — так и должно быть, это и есть мемоизация. А редьюсер на toggleInStock мутирует объект filters и возвращает { ...state }: корень новый, но state.filters — тот же объект, что и раньше, поэтому селектор считает, что ничего не изменилось, и отдаёт закэшированный старый список. setSort создаёт новый filters, поэтому смена сортировки «внезапно» применяет и спрятанный фильтр — ровно то, что видел покупатель. Исправление — иммутабельно обновить вложенный объект: { ...state, filters: { ...state.filters, inStockOnly: !state.filters.inStockOnly } }. Отключать мемоизацию или сравнивать глубоко — лечить симптом: первое убивает производительность, второе всё равно не сработает, потому что кэш держит ссылку на тот же мутированный объект.",
    ["Селектор написан верно. Посмотри, что именно редьюсер возвращает на toggleInStock.",
     "Сравни state.filters до и после toggleInStock через ===.",
     "Обнови вложенный объект иммутабельно: filters: { ...state.filters, inStockOnly: ... }."],
    code=ST03_BUGGY, language="javascript", entry="scenario", answer=ST03_FIXED,
    tests=[
        {"input": [P, [TOGGLE]],
         "expected": {"screens": [ALL_CHEAP, IN_CHEAP], "recomputations": 2}},
        {"input": [P, [TOGGLE, {"type": "noop"}, {"type": "setSort", "sort": "expensive"}]],
         "expected": {"screens": [ALL_CHEAP, IN_CHEAP, IN_CHEAP, IN_EXP], "recomputations": 3}},
        {"input": [P, [TOGGLE, TOGGLE]],
         "expected": {"screens": [ALL_CHEAP, IN_CHEAP, ALL_CHEAP], "recomputations": 3}},
        {"input": [P, [{"type": "setProducts", "products": [{"title": "Ночник", "price": 890, "stock": 5}]}, TOGGLE]],
         "expected": {"screens": [ALL_CHEAP, ["Ночник"], ["Ночник"]], "recomputations": 3}},
    ]))

# ---- fe-state-04: architecture, серверное состояние → TanStack Query ----
ST04_CODE = """// ProductPage.tsx — как сейчас
useEffect(() => {
  dispatch(productLoading(productId));
  fetchProduct(productId)
    .then((product) => dispatch(productLoaded(product)))
    .catch((e) => dispatch(productFailed({ productId, error: String(e) })));
}, [productId]);

useEffect(() => {
  dispatch(reviewsLoading(productId));
  fetchReviews(productId)
    .then((list) => dispatch(reviewsLoaded({ productId, list })))
    .catch((e) => dispatch(reviewsFailed({ productId, error: String(e) })));
}, [productId]);

const onSubmitReview = async (form: ReviewForm) => {
  await postReview(productId, form);
  toast('Спасибо за отзыв!');
};

// store: productsById, reviewsByProduct, loadingById, errorById — 4 слайса, ~600 строк
"""
TASKS.append(task(
    "fe-state-04", "fe-state", "architecture", 5, "qa",
    "Ира: «Завела три бага по странице товара, и все про данные: отправленный отзыв не появляется, пока не обновишь страницу; при возврате на товар — снова спиннер и тот же запрос; после смены цены в админке мини-корзина и карточка показывают разные цены. Марина говорит, что серверные данные у нас в Redux и флаги загрузки руками, и просит предложить решение, после которого такое не будет всплывать снова».",
    "Какое решение правильное?",
    "Все три бага — симптомы того, что серверное состояние хранится как клиентское: кэш без понятия о свежести, без инвалидации и с несколькими копиями одних и тех же данных. TanStack Query решает ровно эту задачу: queryKey ['product', id] — единый адрес данных, одинаковый ключ в карточке и мини-корзине означает одну копию и один запрос; staleTime позволяет показывать кэш при возврате на страницу без спиннера и обновлять его в фоне; invalidateQueries после useMutation перезапросит отзывы сразу после отправки. Самописный кэш в Redux с loadedAt — это переизобретение того же с багами и сотнями строк поддержки. Копировать данные из useQuery в Redux — худший вариант: два источника правды, и рассинхрон вернётся. Контекст не даёт ни кэша, ни инвалидации и перерисует всё приложение, а отказ от кэша вернёт спиннеры и лишние запросы на каждый рендер. Если проект целиком на Redux Toolkit, тот же подход даёт RTK Query — важен принцип, а не библиотека.",
    ["Чьи это данные — клиента или сервера? Что с ними происходит со временем?",
     "Почему после отправки отзыва список не узнаёт, что он устарел?",
     "Ищи вариант, где у данных один источник правды и есть инвалидация после мутации."],
    code=ST04_CODE, language="tsx",
    options=[
        "Добавить в Redux-слайсы поле loadedAt и middleware, которое перезапрашивает данные старше минуты, а после postReview вручную диспатчить reviewsLoaded с новым отзывом",
        "Серверные данные — в TanStack Query (['product', id], ['reviews', id], staleTime), отзыв — useMutation с invalidateQueries",
        "Подключить TanStack Query, но результат каждого useQuery копировать в Redux через useEffect, чтобы остальной код продолжал читать данные из стора",
        "Перенести товары и отзывы в React Context на уровне App и обновлять контекст после каждого запроса и мутации",
        "Отключить кэширование совсем: грузить товар и отзывы заново при каждом рендере, чтобы данные всегда были свежими",
    ],
    answer=1))

# =====================================================================
# fe-performance
# =====================================================================
TOPICS.append({"topic_id": "fe-performance", "theory": """Производительность начинается с измерения: сначала метрика и профайлер, потом оптимизация. Иначе чинишь не то.

Web Vitals — метрики реальных пользователей. Держать их надо в «зелёной» зоне по 75-му перцентилю:
• LCP (Largest Contentful Paint) — когда отрисовался самый большой элемент первого экрана. Хорошо — до 2,5 с. Портят тяжёлый JS до первого рендера, медленный API, большая hero-картинка, loading="lazy" на ней.
• INP (Interaction to Next Paint) — задержка от клика или ввода до следующей отрисовки. Хорошо — до 200 мс. Портят долгие задачи в главном потоке. INP заменил FID в марте 2024 года.
• CLS (Cumulative Layout Shift) — насколько «прыгает» вёрстка. Хорошо — до 0,1. Портят картинки без width/height, баннеры, вставленные над контентом, шрифты с другой метрикой.
Лабораторные данные (Lighthouse) — подсказка, полевые (CrUX, свой RUM через библиотеку web-vitals) — правда.

Лишние ререндеры. Компонент перерисовывается, когда меняется его state, перерисовался родитель или изменился контекст, который он читает. Один ререндер дешёвый; проблема — когда их тысячи или компонент тяжёлый.
• memo(Component) пропускает рендер, если пропсы поверхностно равны. Но {...}, [...] и () => {} прямо в JSX создаются заново каждый рендер — и memo бесполезен. Лечится useMemo/useCallback или выносом констант из компонента.
• value={{ user, setUser }} у провайдера — новый объект каждый рендер, все потребители перерисовываются даже внутри memo. Мемоизируй value и дели контексты: часто меняющееся отдельно от стабильного.
• Дорогие вычисления (фильтр и сортировка тысяч строк) — в useMemo с правильными зависимостями.
• Длинные списки — виртуализация (TanStack Virtual, react-window): в DOM только видимые строки.
• Инструменты: React DevTools Profiler (почему компонент перерисовался, подсветка обновлений), вкладка Performance.
React Compiler (стабильная версия вышла в 2025 году) мемоизирует автоматически, но включён он не везде, и понимать причины ререндеров всё равно нужно.

Размер бандла. Весь JS надо скачать, распарсить и выполнить — на бюджетном телефоне это секунды.
• Code splitting по маршрутам: const Admin = lazy(() => import('./Admin')) и <Suspense fallback={...}>.
• Тяжёлое — по требованию: const { renderPdf } = await import('./pdf') по клику.
• Анализ: rollup-plugin-visualizer, vite-bundle-visualizer. Ищи moment с локалями, целый lodash, дубли библиотек.
• Tree shaking работает с ES-модулями: import { debounce } from 'lodash-es', а не import _ from 'lodash'.
• Бюджет размера в CI (size-limit), чтобы регресс ловился на PR.
• Картинки: AVIF/WebP, srcset, width/height; LCP-картинке — fetchpriority="high", а не lazy.

Частые ошибки: оптимизировать без замеров; обвешивать всё useMemo «на всякий случай»; вынести библиотеки в vendor-чанк и считать, что бандл стал меньше."""})

# ---- fe-performance-01: architecture, бандл и code splitting ----
PF01_CODE = """$ npx vite build
dist/index.html                    0.61 kB │ gzip:   0.38 kB
dist/assets/index-4f9c2a1b.css    48.20 kB │ gzip:   9.87 kB
dist/assets/index-8d31e0c7.js  2 418.37 kB │ gzip: 781.02 kB
(!) Some chunks are larger than 500 kB after minification.

vite-bundle-visualizer, index-8d31e0c7.js (минифицировано):
  echarts                   1 010 kB   используется только на /admin/analytics
  @react-pdf/renderer         620 kB   только кнопка «Скачать счёт» в заказе
  moment + все локали         295 kB   formatDate() в 6 местах
  react + react-dom           190 kB
  код приложения              300 kB

Lighthouse, мобильный профиль, главная: LCP 5.9 s, TBT 1 850 ms
"""
TASKS.append(task(
    "fe-performance-01", "fe-performance", "architecture", 3, "manager",
    "Стас: «Маркетинг жалуется: главная Маркета на телефоне открывается шесть секунд, покупатели уходят. Джун уже предлагает вынести все библиотеки в vendor-чанк — „браузер закэширует, и всё полетит“. Гена скинул вывод сборки и визуализатор. Что делаем, чтобы стало быстро уже в этом спринте?»",
    "Какое решение даст реальный эффект для первой загрузки главной?",
    "Главная тащит 2,4 МБ JS, из которых больше 1,6 МБ — графики админки и генератор PDF, которые покупателю на главной не нужны вообще. Code splitting решает это в корне: страницы админки через React.lazy уезжают в отдельные чанки, @react-pdf/renderer подгружается динамическим import() только по клику, а moment с локалями заменяется на Intl.DateTimeFormat или date-fns с tree shaking — главная становится в разы легче. Бюджет размера в CI не даст бандлу снова разрастись незаметно. vendor-чанк меняет только нарезку: при первом визите скачать и выполнить придётся столько же. Brotli уменьшит трафик на 15–20%, но не время парсинга и выполнения, которое на телефоне и съедает секунды. Service Worker ускорит только повторные визиты, а переезд на SSR — огромная работа, после которой тот же бандл всё равно придётся гидрировать.",
    ["Какие из библиотек в бандле нужны посетителю главной?",
     "Первая загрузка: важно, сколько JS скачать, распарсить и выполнить, а не как он нарезан.",
     "React.lazy для маршрутов, import() по клику для редких функций."],
    code=PF01_CODE, language="text",
    options=[
        "Настроить manualChunks: все node_modules в один vendor-чанк — браузер закэширует его отдельно, и главная станет быстрой",
        "Code splitting: админку — через React.lazy, PDF — через import() по клику, moment заменить на Intl; бюджет бандла в CI",
        "Включить brotli вместо gzip с максимальным уровнем сжатия — бандл станет заметно меньше, и загрузка ускорится",
        "Зарегистрировать Service Worker, который закэширует бандл, — со второго визита всё будет открываться мгновенно",
        "Перевести проект на SSR, чтобы HTML приходил с сервера готовым и LCP не ждал загрузки бандла",
    ],
    answer=1))

# ---- fe-performance-02: incident, CLS после релиза ----
PF02_CODE = """RUM (web-vitals), мобильные, p75, страница /schedule
                 релиз 2.14      релиз 2.15 (вчера)
LCP              2.1 s           2.2 s
INP              160 ms          170 ms
CLS              0.03            0.34        ← poor (> 0.25)

onCLS с attribution, главные источники сдвигов:
  largestShiftTarget         доля    когда
  div.promo-banner           71%     ~1.2 s после загрузки, сразу после ответа GET /api/promo
  img.coach-avatar           22%     по мере загрузки фото тренеров
  прочее                      7%

Что вошло в 2.15:
  SchedulePage.tsx:   {promo && <PromoBanner promo={promo} />}   // над <ScheduleList />
  ScheduleCard.tsx:   <img src={coach.photoUrl} alt={coach.name} className="coach-avatar" />
"""
TASKS.append(task(
    "fe-performance-02", "fe-performance", "incident", 4, "client",
    "Артур, директор фитнес-клуба «Жми»: «После вашего вчерашнего обновления расписание на телефоне прыгает! Клиенты хотят нажать „Записаться“, а попадают в „Отменить запись“ у соседнего занятия. С утра три жалобы, одна женщина осталась без места на йоге. Срочно разберитесь!»",
    "Какие действия вернут CLS в норму и не испортят другие метрики? Выбери все правильные.",
    "Метрики однозначно указывают на CLS: 0,34 против 0,03 до релиза, а LCP и INP почти не изменились. Атрибуция называет два источника: баннер, который появляется над расписанием через 1,2 с после ответа API и сдвигает всё вниз, и фото тренеров без размеров, которые растягивают карточки по мере загрузки. Лечится резервированием места: у баннера — фиксированная min-height или скелетон той же высоты (либо показ без сдвига — ниже списка или поверх как плашка), у картинок — width и height или aspect-ratio, чтобы браузер знал размер до загрузки. loading=\"lazy\" не убирает сдвиг — картинка без размеров прыгнет позже. React.memo влияет на число ререндеров, а не на геометрию вёрстки. Спиннер на всю страницу до ответа /api/promo спрячет сдвиг ценой +1,2 с к LCP — обмен одной плохой метрики на другую.",
    ["Какая из трёх метрик ушла в красную зону?",
     "Сдвиг возникает, когда элемент появляется или меняет размер, а место под него не зарезервировано.",
     "Чем браузер может заранее узнать размер картинки?"],
    code=PF02_CODE, language="text", time_limit=20,
    options=[
        "Зарезервировать место под промо-баннер (min-height или скелетон) либо показывать его без сдвига — ниже списка или поверх",
        "Добавить loading=\"lazy\" всем картинкам: они будут грузиться позже и перестанут сдвигать вёрстку",
        "Прописать фото тренеров width и height (или aspect-ratio в CSS), чтобы браузер оставил под них место",
        "Обернуть ScheduleList в React.memo — меньше ререндеров расписания, значит, меньше сдвигов при загрузке",
        "Показывать спиннер на всю страницу, пока не придёт ответ /api/promo, — тогда баннер появится вместе с расписанием",
    ],
    answer=[0, 2]))

# ---- fe-performance-03: code_review, лишние ререндеры ----
PF03_CODE = """import { createContext, memo, useContext, useState } from 'react';

type Product = { id: number; title: string; price: number };

const CartContext = createContext<{ count: number; add: (id: number) => void } | null>(null);

export function App({ products }: { products: Product[] }) {
  const [cart, setCart] = useState<number[]>([]);
  const [query, setQuery] = useState('');

  const add = (id: number) => setCart((prev) => [...prev, id]);

  return (
    <CartContext.Provider value={{ count: cart.length, add }}>
      <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Поиск" />
      <Catalog products={products} query={query} />
    </CartContext.Provider>
  );
}

function Catalog({ products, query }: { products: Product[]; query: string }) {
  const visible = products
    .filter((p) => p.title.toLowerCase().includes(query.toLowerCase()))
    .sort((a, b) => a.price - b.price);

  return (
    <ul>
      {visible.map((p) => (
        <ProductCard
          key={p.id}
          product={p}
          options={{ showRating: true, compact: false }}
          onOpen={() => console.log('open', p.id)}
        />
      ))}
    </ul>
  );
}

const ProductCard = memo(function ProductCard({ product, options, onOpen }: {
  product: Product;
  options: { showRating: boolean; compact: boolean };
  onOpen: () => void;
}) {
  const cart = useContext(CartContext)!;
  return (
    <li onClick={onOpen} className={options.compact ? 'card card--compact' : 'card'}>
      {product.title} — {product.price} ₽
      <button onClick={(e) => { e.stopPropagation(); cart.add(product.id); }}>
        В корзину ({cart.count})
      </button>
    </li>
  );
});
"""
TASKS.append(task(
    "fe-performance-03", "fe-performance", "code_review", 4, "teamlead",
    "Марина: «PR с новым каталогом на 3 000 товаров. На стейдже поиск лагает на каждой букве, а „В корзину“ отвечает с задержкой. Profiler показывает, что на каждое действие перерисовываются все карточки — хотя автор честно обернул ProductCard в memo. Отметь, что реально нужно исправить».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "memo сравнивает пропсы по ссылке, а options={{...}} и onOpen={() => ...} создаются заново на каждый рендер Catalog — сравнение всегда проваливается, и memo только добавляет работы; options выносится в константу вне компонента, обработчик — через useCallback или передачу стабильной функции и id. Вторая причина хуже: value={{ count, add }} — новый объект на каждый рендер App (то есть на каждую букву в поиске), а все 3 000 карточек читают контекст, поэтому перерисовываются в обход memo; вдобавок каждая карточка показывает count и обязана перерисоваться при каждом добавлении. Лечится мемоизацией value, стабильным add и разделением: карточкам — только add, счётчик — в шапку (отдельный контекст или стор с селектором). Третья — фильтр и сортировка 3 000 элементов на каждый рендер, включая добавление в корзину, когда query не менялся: это useMemo с зависимостями [products, query]. Индекс в key ломает сопоставление элементов при фильтрации, функциональный setCart — правильный приём, setQuery и так стабилен, а onChange у input не влияет на карточки, и удалять memo отовсюду — не решение.",
    ["Что именно сравнивает memo и что приходит в пропсы карточки на каждом рендере?",
     "Что происходит с потребителями контекста, если value — новый объект?",
     "Какие вычисления в Catalog повторяются, даже когда query не менялся?"],
    code=PF03_CODE, language="tsx",
    options=[
        "options и onOpen создаются заново на каждый рендер — memo(ProductCard) сравнивает по ссылке и не срабатывает",
        "key={p.id} лучше заменить на key={index}: индексы стабильнее, и React быстрее сравнивает числа, чем id",
        "value={{ count, add }} — новый объект на каждый рендер App, и все карточки с useContext перерисуются в обход memo",
        "setCart((prev) => [...prev, id]) вызывает лишний рендер — надо писать setCart([...cart, id]), так дешевле",
        "Фильтр и сортировка 3 000 товаров идут на каждый рендер Catalog, даже при добавлении в корзину, — нужен useMemo",
        "memo нужно убрать отовсюду: сравнение пропсов всегда дороже рендера, а React и так всё оптимизирует",
        "onChange={(e) => setQuery(e.target.value)} обернуть в useCallback — новая стрелка перерисовывает каталог",
    ],
    answer=[0, 2, 4]))

# ---- fe-performance-04: estimation, «сделай быстрее» ----
TASKS.append(task(
    "fe-performance-04", "fe-performance", "estimation", 4, "manager",
    "Стас: «Маркетинг говорит, мы теряем мобильных покупателей, потому что „сайт тормозит“. Сделай Маркет быстрым! Сколько тебе нужно времени? Завтра планирование, мне нужна цифра».",
    "Прежде чем назвать срок, выбери вопросы, которые обязательно нужно выяснить.",
    "«Сделай быстрым» — не задача, а направление: без цели и замеров оценка будет гаданием. Важные страницы и сценарии определяют, где оптимизировать в первую очередь — ускорить оформление заказа ценнее, чем страницу «О компании». Текущие полевые метрики (p75 LCP, INP, CLS) и целевые значения превращают «быстро» в проверяемый критерий готовности; если RUM нет, его настройка — первый пункт плана. Устройства и сеть аудитории меняют приоритеты: на бюджетном Android главный враг — объём JS, а не сервер. Сторонние скрипты (аналитика, чат, A/B-тесты) часто съедают больше, чем свой код, и их нельзя трогать без владельца. Разумный ответ Стасу: «Замеры и план — 2 дня; быстрые победы (code splitting маршрутов, картинки, отложенные сторонние скрипты) — ещё 3–5 дней; дальше — оценка по пунктам плана».",
    ["Как понять, что работа закончена, если «быстро» не измерено?",
     "Что сильнее всего влияет на скорость у мобильных пользователей и кто за это отвечает?"],
    time_limit=15,
    options=[
        "Какие страницы и сценарии важнее всего для денег: главная, каталог, карточка, оформление?",
        "Какие сейчас полевые метрики (p75 LCP, INP, CLS по CrUX или RUM) и какую цель считаем «быстро»?",
        "Какую библиотеку анимаций предпочитает маркетинг — Framer Motion или GSAP, как на промо-лендинге?",
        "На каких устройствах и в какой сети наши мобильные покупатели (доля бюджетных Android, 4G)?",
        "Можно ли трогать сторонние скрипты — аналитику, чат поддержки, A/B-тесты — и кто за них отвечает?",
        "Можно ли выкатить все оптимизации одним большим релизом без ревью, чтобы успеть к распродаже?",
        "Какого цвета будет новый прелоадер и нужен ли он вообще на мобильной версии главной?",
        "Сколько всего строк кода во фронтенде и какая часть из них покрыта юнит-тестами?",
    ],
    answer=[0, 1, 3, 4]))

# =====================================================================
# fe-event-loop
# =====================================================================
TOPICS.append({"topic_id": "fe-event-loop", "theory": """JavaScript в браузере выполняется в одном главном потоке. Там же браузер обрабатывает клики и рисует страницу. Пока работает твой код, интерфейс стоит.

Стек вызовов — функции, которые выполняются прямо сейчас. Когда стек пуст, event loop берёт следующую работу из очередей.

Задачи (macrotasks): выполнение скрипта, колбэк setTimeout/setInterval, обработчик события (click, input), сообщение из MessageChannel или Worker.

Микрозадачи (microtasks): колбэки Promise.then/catch/finally, продолжение async-функции после await, queueMicrotask, MutationObserver.

Один оборот цикла:
• выполнить одну задачу целиком;
• выполнить ВСЕ микрозадачи, включая добавленные по ходу;
• если пора рисовать кадр (обычно раз в ~16 мс при 60 Гц) — колбэки requestAnimationFrame, стили, layout, paint;
• взять следующую задачу.

Отсюда правила:
• синхронный код всегда раньше любого колбэка;
• все микрозадачи — раньше следующего setTimeout, даже с задержкой 0;
• await — «остаток функции положи в микрозадачу»: код после await выполнится после текущего синхронного кода;
• между микрозадачами браузер не рисует: бесконечная цепочка микрозадач вешает вкладку так же, как while (true);
• setTimeout(fn, 0) — не «сразу», а «новой задачей, не раньше чем через 0 мс» (при глубокой вложенности таймеров — от 4 мс).

    console.log('A');
    setTimeout(() => console.log('B'), 0);
    Promise.resolve().then(() => console.log('C'));
    console.log('D');
    // A D C B

requestAnimationFrame(cb) вызывает cb прямо перед следующей отрисовкой — туда кладут визуальные изменения и анимации, а не вычисления.

Долгие задачи. Задача дольше 50 мс — long task: клики и ввод ждут в очереди, INP растёт. Если обработчик сделал setLoading(true) и сразу две секунды считает, спиннер не появится: отрисовка будет только после задачи. Что делать:
• делать меньше работы: не рендерить 50 000 строк (виртуализация), предпосчитать данные один раз, мемоизировать;
• откладывать неважное: debounce ввода, в React — useDeferredValue и startTransition (ввод рисуется сразу, тяжёлый список — когда будет время);
• дробить работу на куски и уступать поток между ними: await scheduler.yield() (есть ещё не во всех браузерах — проверяй globalThis.scheduler?.yield), фолбэк — await new Promise((r) => setTimeout(r, 0)). Уступка через микрозадачу (await Promise.resolve()) не помогает — до отрисовки дело не дойдёт;
• уносить тяжёлые вычисления в Web Worker — отдельный поток, общение через postMessage.

Смотреть глазами: DevTools → Performance. Задачи длиннее 50 мс помечены красным уголком, там же видно, что именно их занимает."""})

# ---- fe-event-loop-01: quiz, порядок вывода ----
EL01_CODE = """console.log('1: старт');

setTimeout(() => console.log('2: timeout'), 0);

Promise.resolve()
  .then(() => console.log('3: then 1'))
  .then(() => console.log('4: then 2'));

queueMicrotask(() => console.log('5: microtask'));

(async () => {
  console.log('6: async start');
  await null;
  console.log('7: after await');
})();

console.log('8: конец');
"""
TASKS.append(task(
    "fe-event-loop-01", "fe-event-loop", "quiz", 3, "qa",
    "Ира: «Баг аналитики: событие „корзина открыта“ улетает раньше, чем „товар добавлен“, хотя в коде вызовы идут наоборот. Автор уверен, что await и setTimeout(0) — „одно и то же, просто потом“. Я набросала упрощённый пример — скажи, в каком порядке он напечатает строки, тогда станет ясно, где собака зарыта».",
    "В каком порядке выведутся номера строк?",
    "Сначала весь синхронный код: 1, затем тело async-функции до первого await — 6, затем 8. К этому моменту в очереди микрозадач по порядку постановки: колбэк первого then (промис уже выполнен), queueMicrotask и продолжение async-функции после await. Они выполняются по очереди: 3, 5, 7. Второй then встаёт в очередь только когда выполнился первый, поэтому 4 идёт после 7 — но всё ещё до таймера, ведь очередь микрозадач опустошается полностью. setTimeout — это новая задача, она выполняется последней: 2. Отсюда и баг аналитики: код после await и код в setTimeout(0) — совсем не «одно и то же потом».",
    ["Сначала выпиши всё синхронное, включая начало async-функции до await.",
     "Второй then попадает в очередь только после выполнения первого.",
     "setTimeout ждёт, пока опустеет очередь микрозадач."],
    code=EL01_CODE, language="javascript",
    options=[
        "1, 8, 6, 3, 4, 5, 7, 2",
        "1, 6, 8, 3, 4, 5, 7, 2",
        "1, 6, 8, 3, 5, 7, 4, 2",
        "1, 6, 8, 2, 3, 5, 7, 4",
        "1, 6, 7, 8, 3, 5, 4, 2",
    ],
    answer=2))

# ---- fe-event-loop-02: write_code, модель event loop без таймеров ----
EL02_START = """// Операции (у каждой ровно одно из полей log / microtask / timeout):
//   { log: 'A' }                          — вывести 'A'
//   { microtask: [ ...операции ] }        — как queueMicrotask(() => { ...операции })
//   { timeout: [ ...операции ], delay }   — как setTimeout(() => { ...операции }, delay); нет delay — значит 0
//
// Пример: [{ log: 'A' }, { timeout: [{ log: 'B' }] }, { microtask: [{ log: 'C' }] }]
// выведет ['A', 'C', 'B'].

function runEventLoop(script) {
  const output = [];
  // твой код: очередь микрозадач, список таймеров, «текущее время»
  return output;
}
"""
EL02_ANSWER = """// Операции (у каждой ровно одно из полей log / microtask / timeout):
//   { log: 'A' }                          — вывести 'A'
//   { microtask: [ ...операции ] }        — как queueMicrotask(() => { ...операции })
//   { timeout: [ ...операции ], delay }   — как setTimeout(() => { ...операции }, delay); нет delay — значит 0
//
// Пример: [{ log: 'A' }, { timeout: [{ log: 'B' }] }, { microtask: [{ log: 'C' }] }]
// выведет ['A', 'C', 'B'].

function runEventLoop(script) {
  const output = [];
  const microtasks = [];
  const timers = [];
  let now = 0;
  let seq = 0;

  function execute(ops) {
    for (const op of ops) {
      if ('log' in op) {
        output.push(op.log);
      } else if ('microtask' in op) {
        microtasks.push(op.microtask);
      } else if ('timeout' in op) {
        timers.push({ at: now + (op.delay || 0), seq: seq++, ops: op.timeout });
      }
    }
  }

  function drainMicrotasks() {
    while (microtasks.length > 0) {
      execute(microtasks.shift());
    }
  }

  execute(script);
  drainMicrotasks();

  while (timers.length > 0) {
    timers.sort((a, b) => a.at - b.at || a.seq - b.seq);
    const timer = timers.shift();
    now = Math.max(now, timer.at);
    execute(timer.ops);
    drainMicrotasks();
  }
  return output;
}
"""
TASKS.append(task(
    "fe-event-loop-02", "fe-event-loop", "write_code", 4, "teamlead",
    "Гена: «Тесты автосохранения в Кодзилла Трекере флакают: где-то стоит настоящий setTimeout, где-то промисы, и порядок плавает. Переведём их на fake timers из Vitest, но сначала хочу, чтобы ты понимал, что у них внутри. Напиши модель очереди задач — без настоящих таймеров, на виртуальном времени».",
    "Напиши runEventLoop(script) и верни массив выведенных строк. Правила: сначала синхронно выполняется весь script; затем все микрозадачи, включая добавленные по ходу; затем таймеры по одному — раньше тот, у кого меньше время срабатывания (виртуальное «сейчас» в момент постановки + delay), при равенстве — поставленный раньше; после каждого таймера снова выполняются все микрозадачи. Когда срабатывает таймер, «сейчас» становится равным его времени.",
    "Модель повторяет настоящий цикл: одна задача целиком, затем полностью опустошаем очередь микрозадач (while, а не один проход — микрозадачи могут ставить новые), затем следующая задача. Таймеры — не очередь, а список с временем срабатывания: берём самый ранний, а при равном времени — поставленный раньше, для этого нужен счётчик seq. Время таймера считается от «сейчас» в момент постановки: таймер на 50 мс, поставленный внутри таймера на 10 мс, сработает на 60-й миллисекунде. Ровно так работают vi.useFakeTimers и jest fake timers: они подменяют setTimeout, хранят список таймеров с виртуальным временем и по команде advanceTimersByTime выполняют их по порядку — поэтому тесты становятся детерминированными и не ждут реальных секунд.",
    ["Нужны три вещи: массив микрозадач, список таймеров и переменная «текущее время».",
     "Опустошай микрозадачи в while (queue.length) — внутри могут добавиться новые.",
     "У таймера храни { at: now + delay, seq }, сортируй по at, при равенстве — по seq."],
    code=EL02_START, language="javascript", entry="runEventLoop", answer=EL02_ANSWER,
    tests=[
        {"input": [[{"log": "sync 1"}, {"timeout": [{"log": "timeout"}]}, {"microtask": [{"log": "micro"}]}, {"log": "sync 2"}]],
         "expected": ["sync 1", "sync 2", "micro", "timeout"]},
        {"input": [[
            {"timeout": [{"log": "T1"}, {"microtask": [{"log": "M в T1"}]}]},
            {"timeout": [{"log": "T2"}]},
            {"microtask": [{"log": "M1"}, {"microtask": [{"log": "M2"}]}, {"timeout": [{"log": "T3"}]}]},
            {"log": "S"},
        ]],
         "expected": ["S", "M1", "M2", "T1", "M в T1", "T2", "T3"]},
        {"input": [[{"timeout": [{"log": "slow"}], "delay": 100}, {"timeout": [{"log": "fast"}], "delay": 10}, {"timeout": [{"log": "zero"}]}]],
         "expected": ["zero", "fast", "slow"]},
        {"input": [[{"timeout": [{"log": "A"}, {"timeout": [{"log": "C"}], "delay": 50}], "delay": 10}, {"timeout": [{"log": "B"}], "delay": 55}]],
         "expected": ["A", "B", "C"]},
        {"input": [[{"timeout": [{"log": "A"}, {"timeout": [{"log": "A2"}]}], "delay": 10}, {"timeout": [{"log": "B"}], "delay": 10}]],
         "expected": ["A", "B", "A2"]},
        {"input": [[]], "expected": []},
    ]))

# ---- fe-event-loop-03: incident, фильтрация 50 000 товаров ----
EL03_CODE = """Chrome DevTools → Performance, CPU 4x slowdown, ввод «лампа» в поиск (5 нажатий)

Task 1 180 ms  ▲ Long task   Event: input
  ├─ onChange → setQuery
  ├─ React render: InventoryTable              1 020 ms
  │    ├─ filterProducts                        410 ms   50 000 × normalize(p.title) + new RegExp(query, 'i')
  │    ├─ sortByStock                           190 ms
  │    └─ render <Row> × 50 000                 420 ms
  └─ Commit + Layout                            160 ms
... ещё 4 таких задачи

Interaction to Next Paint: 1 260 ms (poor)
Total Blocking Time: 4 800 ms
DOM nodes: 412 000

// InventoryTable.tsx
const visible = sortByStock(filterProducts(products, query));
return <table><tbody>{visible.map((p) => <Row key={p.sku} product={p} />)}</tbody></table>;
"""
TASKS.append(task(
    "fe-event-loop-03", "fe-event-loop", "incident", 4, "manager",
    "Стас: «Склад в панике: в админке остатков Маркета 50 000 товаров, и поиск превратился в пытку. Печатаешь „лампа“ — буквы появляются через секунду по одной, Chrome предлагает закрыть вкладку. Кладовщики работают через Excel. Сегодня нужно хоть как-то починить, я записал профиль, как ты учил».",
    "Какие действия действительно уберут подвисание? Выбери все правильные.",
    "Профиль показывает одну long task на каждое нажатие: фильтр, сортировка и рендер 50 000 строк занимают главный поток больше секунды, и браузер не может ни нарисовать букву, ни обработать следующее нажатие. Работать надо на всех трёх участках. Виртуализация оставляет в DOM несколько десятков видимых строк вместо 50 000 (и 412 000 узлов) — исчезают и 420 мс рендера, и дорогой layout. useDeferredValue/startTransition или debounce отделяют срочное (отрисовать ввод) от тяжёлого (пересчитать список), а useMemo не пересчитывает список без нужды. Нормализация названий один раз при загрузке убирает 50 000 вызовов normalize и лишний RegExp на каждое нажатие; если фильтр всё равно тяжёлый — его место в Web Worker. setTimeout(…, 0) лишь переносит ту же секундную задачу чуть позже, а await внутри цикла превращает её в цепочку микрозадач, между которыми браузер тоже не рисует, — поток занят столько же.",
    ["Из чего состоит одна долгая задача? Посмотри на три крупные строки внутри render.",
     "Помогает ли перенос работы в setTimeout или в микрозадачи, если работа та же самая?",
     "Сколько строк таблицы реально видно на экране?"],
    code=EL03_CODE, language="text", time_limit=20,
    options=[
        "Виртуализировать таблицу (TanStack Virtual или react-window): в DOM только видимые строки, а не 50 000",
        "Обернуть фильтрацию в setTimeout(…, 0) — она станет асинхронной и перестанет блокировать поток",
        "Отделить ввод от пересчёта: useDeferredValue или startTransition (либо debounce), список мемоизировать",
        "Переписать filterProducts на async-функцию с await внутри цикла — браузер будет успевать рисовать между итерациями",
        "Нормализовать названия один раз при загрузке, а не на каждое нажатие; тяжёлый фильтр — в Web Worker",
        "Попросить склад работать в Chrome с большим количеством оперативной памяти и закрывать другие вкладки",
    ],
    answer=[0, 2, 4]))

# ---- fe-event-loop-04: find_bug (с выбором), спиннер не появляется ----
EL04_CODE = """import { useState } from 'react';
import { buildCsv, download, type Order } from './export';

export function ExportButton({ orders }: { orders: Order[] }) {
  const [busy, setBusy] = useState(false);

  const handleExport = () => {
    setBusy(true);
    const csv = buildCsv(orders); // 80 000 заказов, ~2 с синхронной работы
    download(csv, 'orders.csv');
    setBusy(false);
  };

  return (
    <button onClick={handleExport} disabled={busy}>
      {busy ? 'Готовим файл…' : 'Выгрузить CSV'}
    </button>
  );
}
"""
TASKS.append(task(
    "fe-event-loop-04", "fe-event-loop", "find_bug", 5, "qa",
    "Ира: «Выгрузка заказов в админке Маркета. Шаги: 1) нажать „Выгрузить CSV“; 2) нажать ещё раз, пока страница висит. Ожидаю: надпись „Готовим файл…“ и неактивную кнопку. Фактически: надпись не меняется ни на миг, страница замирает на 2 секунды, а потом скачиваются два одинаковых файла».",
    "В чём причина обоих симптомов и какое исправление правильное?",
    "Клик — одна задача event loop, и весь handleExport выполняется в ней синхронно. React объединяет setBusy(true) и setBusy(false) в один батч, а отрисовать что-либо браузер может только после завершения задачи — к этому моменту busy уже снова false, и «Готовим файл…» не появится ни на кадр. Второй клик, сделанный во время зависания, не теряется: он ждёт в очереди задач и обрабатывается сразу после первой, когда кнопка всё ещё активна, — отсюда второй файл. Правильное решение — не держать главный поток: сборку CSV унести в Web Worker (или дробить на куски с уступкой потока) и сбрасывать busy по завершении — тогда надпись отрисуется сразу, а disabled заблокирует повторный клик. setTimeout вокруг setBusy(false) покажет надпись только после двухсекундной заморозки, на мгновение; await у setState не существует, а ref не вызывает перерисовку вовсе.",
    ["Когда браузер может отрисовать новое состояние кнопки — во время обработчика или после?",
     "Куда деваются клики, сделанные, пока главный поток занят?",
     "Решение должно освободить главный поток, а не переставить вызовы setState."],
    code=EL04_CODE, language="tsx",
    options=[
        "setBusy асинхронный — нужно написать await setBusy(true), тогда надпись успеет смениться до сборки файла",
        "Обработчик — одна задача: 2 с buildCsv блокируют отрисовку, второй клик ждёт в очереди — унести сборку в Web Worker",
        "Заменить useState на useRef: ref меняется синхронно, и кнопка сразу станет disabled для второго клика",
        "Достаточно обернуть setBusy(false) в setTimeout(…, 0) — тогда React успеет показать «Готовим файл…» на время сборки",
        "disabled не блокирует клики, пока идёт обработчик, — добавить кнопке pointer-events: none на время сборки",
    ],
    answer=1))

# =====================================================================
# fe-cors
# =====================================================================
TOPICS.append({"topic_id": "fe-cors", "theory": """Origin — это схема + хост + порт. https://lampmarket.ru, https://www.lampmarket.ru, http://lampmarket.ru и https://lampmarket.ru:8443 — четыре разных origin.

Same-origin policy — правило браузера: скрипт страницы не может прочитать ответ с другого origin, если тот явно не разрешил. Иначе любой открытый сайт читал бы твою почту и банк с твоими куками. CORS — способ сервера сказать браузеру «этому origin можно». Решает браузер, заголовки ставит сервер. curl, Postman и бэкенды CORS не проверяют: это защита пользователя в браузере, а не API.

Простые запросы — GET, HEAD или POST без своих заголовков, с Content-Type text/plain, multipart/form-data или application/x-www-form-urlencoded. Браузер шлёт их сразу с заголовком Origin; сервер выполняет запрос в любом случае, но без подходящего Access-Control-Allow-Origin браузер не отдаст ответ скрипту.

Остальное требует preflight: PUT, PATCH, DELETE, Content-Type: application/json, Authorization, любые свои заголовки. Сначала уходит OPTIONS:

    OPTIONS /api/cart
    Origin: https://lampmarket.ru
    Access-Control-Request-Method: PATCH
    Access-Control-Request-Headers: authorization, content-type

Сервер отвечает разрешениями (обычно 204), и только потом уходит настоящий запрос:

    Access-Control-Allow-Origin: https://lampmarket.ru
    Access-Control-Allow-Methods: GET, POST, PATCH, DELETE
    Access-Control-Allow-Headers: Authorization, Content-Type
    Access-Control-Max-Age: 7200

Max-Age кэширует результат preflight; браузеры ограничивают его сверху (Chrome — 2 часа).

Куки и credentials. По умолчанию кросс-доменный fetch куки не шлёт. С credentials: 'include' (в axios — withCredentials: true) сервер обязан ответить:
• Access-Control-Allow-Origin с конкретным origin — звёздочка * с credentials не работает;
• Access-Control-Allow-Credentials: true;
• Vary: Origin, если origin подставляется из списка, — чтобы кэш или CDN не отдал ответ с чужим origin.
Для кросс-сайтовых запросов кука ещё должна быть SameSite=None; Secure.

Детали, на которых спотыкаются:
• CORS-заголовки нужны и на ответах с ошибками (401, 500), иначе вместо статуса фронт получит TypeError: Failed to fetch. В nginx — add_header … always;
• свой заголовок ответа (X-Total-Count) скрипт увидит, только если он есть в Access-Control-Expose-Headers;
• mode: 'no-cors' — не «выключить CORS», а получить непрозрачный ответ, который нельзя прочитать;
• в консоли CORS-ошибка, а в Network у запроса статус 200 — запрос дошёл, ответ спрятан.

Где решать:
• в разработке — прокси dev-сервера (server.proxy в Vite): браузер ходит на localhost:5173/api, Vite пересылает запрос на API. Для браузера это один origin, CORS не нужен;
• в проде — либо один домен (фронт и /api за одним reverse proxy), либо CORS на бэкенде со строгим списком origin. Нельзя «отражать» любой пришедший Origin вместе с Allow-Credentials: true — это то же самое, что выключить защиту."""})

# ---- fe-cors-01: incident, новый origin не в allowlist ----
CR01_CODE = """Консоль браузера на https://www.vysota-bc.ru/booking:
  Access to fetch at 'https://booking-api.kodzilla.ru/v1/rooms?date=2026-09-24' from origin
  'https://www.vysota-bc.ru' has been blocked by CORS policy: No 'Access-Control-Allow-Origin'
  header is present on the requested resource.
  GET https://booking-api.kodzilla.ru/v1/rooms?date=2026-09-24 net::ERR_FAILED 200 (OK)

$ curl -si -H 'Origin: https://vysota-bc.ru' 'https://booking-api.kodzilla.ru/v1/rooms?date=2026-09-24' | head -4
HTTP/2 200
content-type: application/json
access-control-allow-origin: https://vysota-bc.ru
vary: Origin

$ curl -si -H 'Origin: https://www.vysota-bc.ru' 'https://booking-api.kodzilla.ru/v1/rooms?date=2026-09-24' | head -4
HTTP/2 200
content-type: application/json
vary: Origin

# booking-api, переменные окружения прода
CORS_ALLOWED_ORIGINS=https://vysota-bc.ru,https://stage.vysota-bc.ru
"""
TASKS.append(task(
    "fe-cors-01", "fe-cors", "incident", 3, "client",
    "Вера, управляющая бизнес-центра «Высота»: «С утра на нашем сайте не работает бронирование переговорок — виджет пустой, арендаторы звонят на ресепшен. Вчера наш подрядчик перевёл сайт на адрес с www, сказал, что „это ни на что не влияет“. Помогите, у нас в 10 утра совет директоров, и им нужна переговорка!»",
    "Что сломалось и как правильно починить?",
    "Origin — это схема, хост и порт целиком, поэтому https://www.vysota-bc.ru и https://vysota-bc.ru для браузера разные сайты. curl показывает, что API работает и отвечает 200, но для нового origin не возвращает Access-Control-Allow-Origin: его нет в списке CORS_ALLOWED_ORIGINS. Запрос дошёл и выполнился, а браузер спрятал ответ от скрипта виджета — отсюда 200 в Network и CORS-ошибка в консоли. Правильная починка — добавить https://www.vysota-bc.ru в allowlist на бэкенде (а подрядчику — настроить редирект с голого домена на www или наоборот, чтобы у сайта был один адрес). mode: 'no-cors' даст непрозрачный ответ, который нельзя прочитать; звёздочка с credentials не работает и снимает защиту; Access-Control-Allow-Origin — заголовок ответа сервера, из запроса он ничего не решает; а просить пользователей отключать защиту в браузере — не решение.",
    ["Сравни два ответа curl: чем они отличаются?",
     "Из чего состоит origin и совпадает ли он у старого и нового адреса сайта?",
     "Кто разрешает кросс-доменный доступ: клиент в запросе или сервер в ответе?"],
    code=CR01_CODE, language="text", time_limit=15,
    options=[
        "API упал или не успевает ответить — ответ не дошёл до браузера; перезапустить booking-api",
        "www.vysota-bc.ru — другой origin, и его нет в списке разрешённых: добавить в CORS_ALLOWED_ORIGINS",
        "Поставить в виджете fetch(url, { mode: 'no-cors' }) — браузер перестанет блокировать запрос",
        "Разрешить всех: Access-Control-Allow-Origin: * вместе с Access-Control-Allow-Credentials: true",
        "Добавить заголовок Access-Control-Allow-Origin в запрос виджета — тогда сервер пропустит новый домен",
        "Попросить арендаторов поставить расширение браузера, отключающее CORS, пока чиним",
    ],
    answer=1))

# ---- fe-cors-02: quiz, preflight / credentials / * ----
TASKS.append(task(
    "fe-cors-02", "fe-cors", "quiz", 4, "devops_colleague",
    "Дима: «После перехода „КофеБота“ на токены в заголовке запросов к API в логах стало вдвое больше, половина из них — OPTIONS. Бэкендеры нервничают, а джун с фронта уверяет их, что CORS защищает API от ботов. Давай разложим по полочкам, прежде чем идти к ним».",
    "Выбери все верные утверждения.",
    "PATCH и Content-Type: application/json не входят в список «простых» запросов, поэтому браузер сначала спрашивает разрешения OPTIONS-запросом. Заголовок Authorization тоже не из безопасного списка — вот откуда удвоение запросов после перехода на токены в заголовке; Access-Control-Max-Age позволяет браузеру кэшировать ответ preflight и не слать OPTIONS перед каждым вызовом. Простой GET уходит сразу и выполняется на сервере, даже если CORS-заголовков в ответе нет, — браузер лишь прячет ответ от скрипта; поэтому изменяющие данные операции на GET — опасная идея. Звёздочка с credentials не работает: для запросов с куками нужен конкретный origin и Access-Control-Allow-Credentials: true. И CORS вообще не защищает API: curl и боты его не проверяют — он защищает пользователя в браузере.",
    ["Что делает запрос «непростым»: метод, тип тела, заголовки?",
     "Кто проверяет CORS-заголовки — сервер или браузер?"],
    options=[
        "fetch с методом PATCH и Content-Type: application/json вызовет preflight-запрос OPTIONS",
        "Простой GET без своих заголовков уйдёт без preflight и выполнится на сервере, даже если в ответе нет CORS-заголовков, — браузер лишь не отдаст ответ скрипту",
        "С credentials: 'include' можно отвечать Access-Control-Allow-Origin: * — звёздочка разрешает всех, в том числе запросы с куками",
        "Заголовок Authorization: Bearer … в запросе вызывает preflight",
        "CORS защищает API от запросов из curl, Postman и ботов",
        "Access-Control-Max-Age позволяет браузеру закэшировать ответ на preflight и не слать OPTIONS перед каждым запросом",
    ],
    answer=[0, 1, 3, 5]))

# ---- fe-cors-03: architecture, прокси в dev и CORS в проде ----
TASKS.append(task(
    "fe-cors-03", "fe-cors", "architecture", 4, "teamlead",
    "Марина: «Делаем личный кабинет для фитнес-клуба „Жми“. Фронт — app.zhmi-fit.ru, API — api.zhmi-fit.ru, авторизация через httpOnly-куку. Локально фронт крутится на localhost:5173 и ходит на стейдж-API. Джун предлагает поставить на бэке Access-Control-Allow-Origin: * „и для локалки, и для прода, чтобы больше никогда не мучиться“. Как делаем?»",
    "Какая схема правильная для разработки и для прода?",
    "Джуновская звёздочка не заработает: запросы с кукой требуют конкретного origin и Access-Control-Allow-Credentials: true, а с * браузер ответ отклонит. Отражать любой Origin с credentials ещё хуже — это разрешение любому сайту делать запросы от имени залогиненного пользователя и читать ответы, то есть полное отключение защиты. В разработке CORS проще не настраивать вовсе: прокси Vite (server.proxy) пересылает /api на стейдж, и для браузера фронт и API оказываются на одном origin localhost:5173; иногда ещё понадобится переписать домен куки (cookieDomainRewrite). В проде — строгий allowlist из https://app.zhmi-fit.ru с Allow-Credentials и Vary: Origin, либо API под тем же доменом через reverse proxy (/api), тогда CORS не нужен совсем. Флаг --disable-web-security годится разве что для минутного эксперимента, и app.zhmi-fit.ru с api.zhmi-fit.ru — всё равно разные origin, так что в проде CORS нужен; а публичный CORS-прокси отправит куки и данные пользователей через чужой сервер.",
    ["Что браузер требует от ответа, если запрос идёт с куками?",
     "Как сделать, чтобы в разработке запрос для браузера вообще не был кросс-доменным?",
     "Чем опасно разрешать любой origin вместе с credentials?"],
    options=[
        "На бэке везде Access-Control-Allow-Origin: * и Allow-Credentials: true — один конфиг для всех окружений",
        "Бэк отражает любой пришедший Origin в Access-Control-Allow-Origin вместе с Allow-Credentials: true — работает везде без списков",
        "В dev — прокси Vite (server.proxy /api → стейдж), CORS не нужен; в проде — allowlist app.zhmi-fit.ru с credentials",
        "Разработчикам запускать Chrome с флагом --disable-web-security, а в проде оставить как есть — там фронт и API на одном домене zhmi-fit.ru",
        "Ходить в API через публичный CORS-прокси и в разработке, и в проде — он сам добавит нужные заголовки",
    ],
    answer=2))

# ---- fe-cors-04: find_bug (с выбором), nginx add_header без always ----
CR04_CODE = """# Консоль браузера, когда access-токен протух:
#   Access to fetch at 'https://api.lampmarket.ru/api/cart' from origin 'https://lampmarket.ru'
#   has been blocked by CORS policy: No 'Access-Control-Allow-Origin' header is present on the requested resource.
#   GET https://api.lampmarket.ru/api/cart net::ERR_FAILED 401 (Unauthorized)
# Sentry: TypeError: Failed to fetch  (фронт показывает экран «Нет сети»)
# С живым токеном всё работает.

server {
    listen 443 ssl;
    server_name api.lampmarket.ru;

    location /api/ {
        if ($request_method = OPTIONS) {
            add_header Access-Control-Allow-Origin "https://lampmarket.ru";
            add_header Access-Control-Allow-Credentials "true";
            add_header Access-Control-Allow-Methods "GET, POST, PUT, PATCH, DELETE";
            add_header Access-Control-Allow-Headers "Authorization, Content-Type";
            add_header Access-Control-Max-Age 7200;
            return 204;
        }

        add_header Access-Control-Allow-Origin "https://lampmarket.ru";
        add_header Access-Control-Allow-Credentials "true";
        proxy_pass http://market-api:8000;
    }
}
"""
TASKS.append(task(
    "fe-cors-04", "fe-cors", "find_bug", 5, "devops_colleague",
    "Дима: «Фронтенд-алерт: когда у пользователя протухает access-токен, Маркет показывает экран „Нет сети“ вместо тихого обновления токена. Фронт ловит 401 и делает refresh — это покрыто тестами, я проверял. Бэк честно отвечает 401. CORS мы настраиваем в nginx, конфиг ниже. Где собака зарыта?»",
    "Почему фронт получает TypeError вместо ответа 401 и как это исправить?",
    "Директива add_header в nginx по умолчанию добавляет заголовок только к ответам с кодами 200, 201, 204, 206, 301, 302, 303, 304, 307 и 308. Пока токен живой, бэк отвечает 200 — заголовки на месте. Протух — бэк отвечает 401, nginx отдаёт его без Access-Control-Allow-Origin, браузер прячет ответ от скрипта, и fetch падает с TypeError: Failed to fetch — код не видит статус 401 и не может запустить refresh, поэтому считает, что пропала сеть. Лечится параметром always у CORS-заголовков (add_header … always), чтобы они были на любом ответе, включая 4xx и 5xx. OPTIONS в Allow-Methods перечислять не нужно, Credentials с конкретным origin — как раз правильная пара, кэш preflight тут ни при чём, а отвечать 200 вместо 401 — ломать семантику HTTP ради обхода симптома.",
    ["Сравни: при каком статусе ответа всё работает, а при каком — нет?",
     "Всегда ли add_header в nginx добавляет заголовок? Загляни в документацию директивы.",
     "У add_header есть параметр, который снимает ограничение по кодам ответа."],
    code=CR04_CODE, language="nginx",
    options=[
        "add_header без always не добавляется к 401: ответ уходит без CORS-заголовков, fetch падает с TypeError — нужно always",
        "В preflight не хватает OPTIONS в Access-Control-Allow-Methods — браузер отклоняет последующий запрос",
        "Access-Control-Allow-Credentials: true нельзя сочетать с конкретным origin — только со звёздочкой *",
        "Бэк должен отвечать на протухший токен 200 с { error: 'expired' } — тогда CORS не помешает фронту",
        "Access-Control-Max-Age 7200 закэшировал старый preflight без Authorization — нужно поставить 0",
    ],
    answer=0))

# ---- перестановка вариантов, чтобы верный ответ не стоял всегда на одном месте ----
def reorder(tid, order):
    t = next(x for x in TASKS if x["task_id"] == tid)
    c = t["content"]
    assert sorted(order) == list(range(len(c["options"])))
    c["options"] = [c["options"][i] for i in order]
    ans = c["correct_answer"]
    c["correct_answer"] = sorted(order.index(i) for i in ans) if isinstance(ans, list) else order.index(ans)


import random


def shuffle_options(all_tasks, salt="f4"):
    """Детерминированно перемешиваем варианты всех задач с выбором (seed — task_id)."""
    for tk in all_tasks:
        c = tk["content"]
        if not isinstance(c.get("options"), list):
            continue
        order = list(range(len(c["options"])))
        random.Random(salt + ":" + tk["task_id"]).shuffle(order)
        reorder(tk["task_id"], order)


shuffle_options(TASKS)

# =====================================================================
data = {"track": "frontend", "part": "f4", "topics": TOPICS, "tasks": TASKS}
os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)

if __name__ == "__main__":
    from collections import Counter
    print("topics:", len(TOPICS), "tasks:", len(TASKS))
    for tp in TOPICS:
        print(" ", tp["topic_id"], "theory", len(tp["theory"]), "chars;",
              [(t["task_id"][-2:], t["type"], t["difficulty"]) for t in TASKS if t["topic_id"] == tp["topic_id"]])
    print("types:", dict(Counter(t["type"] for t in TASKS)))
    print("characters:", dict(Counter(t["character"] for t in TASKS)))
