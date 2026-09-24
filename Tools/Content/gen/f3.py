#!/usr/bin/env python3
"""Генератор контента: track frontend, part f3 (junior_plus):
fe-react-components, fe-api-loading, fe-typescript, fe-vite."""
import json
import os
import random

GRADE = "junior_plus"
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}


def xp(d, g=GRADE):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


def src(s):
    """Код из тройных кавычек: без ведущего перевода строки, с одним завершающим."""
    return s.strip("\n") + "\n"


def rx(what, regex, flags=""):
    e = {"regex": regex}
    if flags:
        e["flags"] = flags
    return {"input": what, "expected": e}


def nrx(what, regex, flags=""):
    e = {"not_regex": regex}
    if flags:
        e["flags"] = flags
    return {"input": what, "expected": e}


def task(topic, n, typ, d, character, story, question, *, code=None, language=None, entry=None,
         options=None, answer=None, tests=None, explanation, hints, time_limit=None):
    content = {"question": question, "code": code, "language": language}
    if entry:
        content["entry"] = entry
    content.update({"options": options, "correct_answer": answer, "test_cases": tests})
    return {
        "task_id": "%s-%02d" % (topic, n),
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


TOPICS = []
TASKS = []

# =====================================================================
# fe-react-components
# =====================================================================
T = "fe-react-components"
TOPICS.append({"topic_id": T, "theory": """Компонент в React — функция, которая получает пропсы и возвращает разметку. Пропсы идут сверху вниз и доступны только для чтения: ребёнок не меняет то, что ему передали, а просит родителя через колбэк (onChange, onToggle, onSubmit).

Композиция и children

Всё, что написано между открывающим и закрывающим тегом компонента, приходит в проп children. Так строят «контейнеры»: карточки, модалки, панели, лейауты. Им всё равно, что внутри, поэтому не нужны пропсы text, icon, showButton и вечно растущий список флагов.

    function Card({ title, children }) {
      return <section><h2>{title}</h2>{children}</section>;
    }
    <Card title="Доставка"><p>Бесплатно от 1500 ₽</p></Card>

ref — тоже проп. С React 19 функциональный компонент получает его как обычный проп и пробрасывает дальше; forwardRef для этого больше не нужен, он остался в старом коде:

    function TextField({ ref, ...props }) {
      return <input ref={ref} {...props} />;
    }

Подъём состояния

Если двум компонентам нужно одно и то же состояние (фильтр и список, «открыта только одна панель»), его поднимают в ближайшего общего родителя. Родитель хранит значение и передаёт вниз само значение и функцию для его изменения. Так появляется единственный источник правды, и компоненты не рассинхронизируются.

Контролируемые формы

Контролируемое поле — значение живёт в состоянии, поле его только показывает:

    const [email, setEmail] = useState('');
    <input value={email} onChange={(e) => setEmail(e.target.value)} />

Значение всегда под рукой: для валидации, блокировки кнопки (disabled) и сброса (setEmail('')). Форму отправляют через onSubmit у <form> и первым делом вызывают e.preventDefault(), иначе браузер перезагрузит страницу. Частая ошибка — value без onChange: поле становится «только для чтения».

useEffect и зависимости

Эффект нужен, чтобы синхронизироваться с внешним миром: запросы, подписки, таймеры, события window. Второй аргумент решает, когда он запускается:
• без массива — после каждого рендера;
• [] — один раз после монтирования;
• [a, b] — после монтирования и каждый раз, когда изменились a или b.
Правило: всё из пропсов и состояния, что используется внутри эффекта, должно быть в зависимостях. Следить помогает линтер react-hooks/exhaustive-deps — не глуши его.

Частые ошибки
• Бесконечный цикл: эффект без массива (или с зависимостью от объекта, который создаётся заново на каждом рендере) вызывает setState → новый рендер → снова эффект.
• Устаревшее замыкание: функция видит значения того рендера, в котором создана. Колбэк setInterval внутри эффекта с [] навсегда видит начальное состояние. Лечится функциональным обновлением: setCount((c) => c + 1).
• Забытый cleanup: эффект может вернуть функцию очистки — React вызовет её перед повторным запуском эффекта и при размонтировании.

    useEffect(() => {
      const id = setInterval(tick, 1000);
      return () => clearInterval(id);
    }, []);

Отписывайся от всего, на что подписался: clearInterval, removeEventListener, socket.close(), controller.abort(). В dev-режиме StrictMode специально монтирует компоненты дважды, чтобы утечки без cleanup проявились сразу.
• Эффект ради вычислений: отфильтрованный список или сумму считай прямо в рендере, а не через useEffect + setState."""})

TASKS.append(task(
    T, 1, "write_code", 2, "client",
    "Артур, директор фитнес-клуба «Жми»: «Форма записи на пробную тренировку отправляет пустые заявки — менеджеры звонят в никуда. "
    "Сделайте, чтобы без имени и телефона кнопка не нажималась, а после отправки форма очищалась».",
    "Сделай форму TrialForm контролируемой: храни name, phone и slot в состоянии (useState), у каждого поля должны быть value и onChange. "
    "В handleSubmit отмени перезагрузку страницы, вызови onSubmit({ name, phone, slot }) с обрезанными пробелами (trim) и очисти форму "
    "(slot верни в 'morning'). Кнопка «Записаться» заблокирована (disabled), пока имя или телефон пустые.",
    code=src(r'''
import { useState } from 'react';

export function TrialForm({ onSubmit }) {
  function handleSubmit(e) {
    // отправь данные через onSubmit
  }

  return (
    <form onSubmit={handleSubmit}>
      <input name="name" placeholder="Имя" />
      <input name="phone" placeholder="Телефон" />
      <select name="slot">
        <option value="morning">Утро</option>
        <option value="evening">Вечер</option>
      </select>
      <button type="submit">Записаться</button>
    </form>
  );
}
'''),
    language="jsx",
    answer=src(r'''
import { useState } from 'react';

export function TrialForm({ onSubmit }) {
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [slot, setSlot] = useState('morning');

  const canSubmit = name.trim() !== '' && phone.trim() !== '';

  function handleSubmit(e) {
    e.preventDefault();
    onSubmit({ name: name.trim(), phone: phone.trim(), slot });
    setName('');
    setPhone('');
    setSlot('morning');
  }

  return (
    <form onSubmit={handleSubmit}>
      <input
        name="name"
        placeholder="Имя"
        value={name}
        onChange={(e) => setName(e.target.value)}
      />
      <input
        name="phone"
        placeholder="Телефон"
        value={phone}
        onChange={(e) => setPhone(e.target.value)}
      />
      <select name="slot" value={slot} onChange={(e) => setSlot(e.target.value)}>
        <option value="morning">Утро</option>
        <option value="evening">Вечер</option>
      </select>
      <button type="submit" disabled={!canSubmit}>Записаться</button>
    </form>
  );
}
'''),
    tests=[
        rx("Значения формы хранятся в состоянии (useState)", r"useState\("),
        rx("У всех трёх полей есть value из состояния", r"value=\{[\s\S]*value=\{[\s\S]*value=\{"),
        rx("У всех трёх полей есть onChange", r"onChange=\{[\s\S]*onChange=\{[\s\S]*onChange=\{"),
        rx("Перезагрузка страницы отменена через preventDefault()", r"\.preventDefault\(\s*\)"),
        rx("Данные уходят наружу через вызов onSubmit(...)", r"\bonSubmit\s*\("),
        rx("Кнопка блокируется через disabled", r"disabled=\{"),
        rx("После отправки форма очищается", r"set\w*\(\s*(''|\"\"|\{\s*name\s*:\s*(''|\"\")|\w*(initial|empty|default)\w*\s*\))", "i"),
    ],
    explanation="Контролируемое поле — это поле, чьё значение живёт в состоянии React: value={name} показывает состояние, а "
                "onChange={(e) => setName(e.target.value)} обновляет его на каждый ввод. Раз значение в состоянии, его легко проверить "
                "(canSubmit), отправить и сбросить: setName('') сразу очищает инпут без обращения к DOM. e.preventDefault() в onSubmit "
                "отменяет стандартную отправку формы с перезагрузкой страницы. trim() не даёт отправить «имя» из одних пробелов. "
                "А отправка через onSubmit у формы, а не через onClick кнопки, бесплатно работает и по Enter.",
    hints=[
        "Для каждого поля заведи состояние через useState и свяжи его с value.",
        "onChange={(e) => setPhone(e.target.value)} — так поле обновляет состояние.",
        "В handleSubmit: e.preventDefault(), onSubmit({...}), затем setName('') и т. д.; у кнопки — disabled={!name.trim() || !phone.trim()}.",
    ],
))

TASKS.append(task(
    T, 2, "write_code", 3, "client",
    "Нина, владелица пекарни «Батон»: «В FAQ про доставку можно раскрыть все вопросы сразу — на телефоне получается простыня. "
    "Хочу, чтобы открыт был только один. И в ответы надо вставлять ссылки и списки, а сейчас там только текст».",
    "Перепиши FAQ. 1) Panel принимает содержимое через children (вместо пропа text) и не хранит своё состояние: получает isOpen и "
    "onToggle от родителя. 2) Какая панель открыта (индекс или null), храни в DeliveryFaq; клик по открытой панели закрывает её. "
    "3) Используй Panel с вложенной разметкой: <Panel …>…</Panel> — например, со списком районов и ссылкой на телефон.",
    code=src(r'''
import { useState } from 'react';

function Panel({ title, text }) {
  const [isOpen, setIsOpen] = useState(false);

  return (
    <section className="panel">
      <button className="panel__title" onClick={() => setIsOpen(!isOpen)}>
        {title}
      </button>
      {isOpen && <p className="panel__body">{text}</p>}
    </section>
  );
}

export function DeliveryFaq() {
  return (
    <div className="faq">
      <Panel title="Сколько стоит доставка?" text="Бесплатно от 1500 ₽, иначе 199 ₽." />
      <Panel title="Куда возите?" text="По всему городу, кроме промзоны." />
      <Panel title="Как с вами связаться?" text="Звоните +7 900 123-45-67." />
    </div>
  );
}
'''),
    language="jsx",
    answer=src(r'''
import { useState } from 'react';

function Panel({ title, isOpen, onToggle, children }) {
  return (
    <section className="panel">
      <button className="panel__title" onClick={onToggle}>
        {title}
      </button>
      {isOpen && <div className="panel__body">{children}</div>}
    </section>
  );
}

export function DeliveryFaq() {
  const [openIndex, setOpenIndex] = useState(null);

  function toggle(index) {
    setOpenIndex((current) => (current === index ? null : index));
  }

  return (
    <div className="faq">
      <Panel title="Сколько стоит доставка?" isOpen={openIndex === 0} onToggle={() => toggle(0)}>
        <p>Бесплатно от 1500 ₽, иначе 199 ₽.</p>
      </Panel>
      <Panel title="Куда возите?" isOpen={openIndex === 1} onToggle={() => toggle(1)}>
        <ul>
          <li>Весь город, кроме промзоны</li>
          <li>Пригород — по предзаказу</li>
        </ul>
      </Panel>
      <Panel title="Как с вами связаться?" isOpen={openIndex === 2} onToggle={() => toggle(2)}>
        <p>
          Звоните: <a href="tel:+79001234567">+7 900 123-45-67</a>
        </p>
      </Panel>
    </div>
  );
}
'''),
    tests=[
        rx("Panel принимает children", r"Panel\s*(?:=\s*)?\(\s*\{[^}]*\bchildren\b|props\.children"),
        rx("Panel выводит {children}", r"\{\s*(props\.)?children\s*\}"),
        nrx("У Panel нет собственного useState",
            r"Panel\s*(?:=\s*)?\([^)]*\)\s*(?:=>\s*)?\{[\s\S]*?useState[\s\S]*?(?:function\s+DeliveryFaq|DeliveryFaq\s*=)"),
        rx("Состояние открытой панели хранится в DeliveryFaq", r"DeliveryFaq[\s\S]*useState\("),
        rx("Родитель передаёт в Panel isOpen и onToggle", r"^(?=[\s\S]*\bisOpen=\{)(?=[\s\S]*\bonToggle=\{)"),
        rx("Содержимое передаётся между тегами <Panel>…</Panel>", r"</Panel>"),
        rx("Повторный клик закрывает панель (null)", r"\?\s*(null|-1|undefined)\s*:|:\s*(null|-1|undefined)\s*\)"),
    ],
    explanation="Когда двум компонентам нужно согласованное состояние («открыта только одна панель»), его поднимают в ближайшего "
                "общего родителя. DeliveryFaq хранит openIndex — единственный источник правды, а Panel становится простым компонентом: "
                "показывает то, что ему передали (isOpen), и сообщает о клике через onToggle. Если бы каждая Panel хранила своё isOpen, "
                "соседи ничего не знали бы друг о друге и закрыть остальные было бы невозможно. children делает Panel контейнером: ей всё "
                "равно, что внутри — абзац, список или ссылка, — поэтому не нужны пропсы text, list, link и растущий набор флагов. "
                "Это и есть композиция.",
    hints=[
        "Кто должен знать, какая панель открыта, чтобы закрыть остальные?",
        "Держи в DeliveryFaq useState(null) с индексом открытой панели и передавай isOpen={openIndex === 0}.",
        "В Panel замени text на children, а setIsOpen — на onToggle из пропсов; повторный клик: current === index ? null : index.",
    ],
))

TASKS.append(task(
    T, 3, "find_bug", 3, "devops_colleague",
    "Дима: «У бэкенда Кодзилла Трекера RPS вырос в 50 раз. В логах один и тот же GET /api/projects/12/tasks от одного пользователя — "
    "по 40 раз в секунду, пока открыта вкладка. Похоже на фронт, глянь компонент списка задач».",
    "Почему компонент шлёт запросы без остановки и как это правильно исправить?",
    code=src(r'''
import { useEffect, useState } from 'react';

export function TaskList({ projectId }) {
  const [tasks, setTasks] = useState([]);

  useEffect(() => {
    fetch(`/api/projects/${projectId}/tasks`)
      .then((res) => res.json())
      .then((data) => setTasks(data));
  });

  return (
    <ul>
      {tasks.map((task) => (
        <li key={task.id}>{task.title}</li>
      ))}
    </ul>
  );
}
'''),
    language="jsx",
    options=[
        "Эффект без массива зависимостей запускается после каждого рендера, а setTasks вызывает новый рендер — нужен [projectId]",
        "Передать массив зависимостей [tasks] — эффект будет срабатывать, только когда задачи действительно изменились",
        "Передать пустой массив [] — запрос уйдёт один раз при монтировании, для списка задач этого достаточно",
        "Проблема в fetch без кэша — заменить его на axios, он сам кэширует повторные GET-запросы",
        "Перед setTasks сравнивать длину старого и нового массива и не обновлять состояние, если она та же",
    ],
    answer=0,
    explanation="Эффект без второго аргумента выполняется после каждого рендера. Здесь он загружает задачи и вызывает setTasks с новым "
                "массивом — это новая ссылка, React видит изменение состояния и рендерит снова, эффект опять запускается, и так без конца. "
                "Массив зависимостей говорит React, когда эффект перезапускать: запрос зависит только от projectId, значит [projectId]. "
                "[tasks] не спасёт: каждый ответ — новый массив, цикл останется. [] остановит цикл, но при переключении проекта список "
                "не обновится — это второй баг, и линтер react-hooks/exhaustive-deps на него укажет. Сравнение длины — костыль: "
                "задачи могут поменяться при той же длине.",
    hints=[
        "Когда запускается эффект, у которого нет второго аргумента?",
        "Что происходит после setTasks с новым массивом?",
        "От какого пропа зависит URL запроса?",
    ],
))

TASKS.append(task(
    T, 4, "find_bug", 3, "qa",
    "Ира: «Бронь переговорки в «Высоте» держится 5 минут, и на странице есть обратный отсчёт. Он показывает 4:59 и замирает. "
    "Проверила в Chrome и Firefox — одинаково. Шаги: выбрать переговорку → «Забронировать» → смотреть на таймер».",
    "Почему таймер останавливается на 4:59? Выбери верный диагноз и исправление.",
    code=src(r'''
import { useEffect, useState } from 'react';

function formatTime(total) {
  const m = Math.floor(total / 60);
  const s = String(total % 60).padStart(2, '0');
  return `${m}:${s}`;
}

export function HoldTimer({ seconds }) {
  const [left, setLeft] = useState(seconds);

  useEffect(() => {
    const id = setInterval(() => {
      setLeft(left - 1);
    }, 1000);
    return () => clearInterval(id);
  }, []);

  if (left <= 0) {
    return <p>Бронь истекла — выбери время заново</p>;
  }
  return <p>Бронь держится ещё {formatTime(left)}</p>;
}
'''),
    language="jsx",
    options=[
        "setInterval замирает в неактивной вкладке и под нагрузкой — заменить его на рекурсивный setTimeout",
        "Колбэк интервала навсегда видит left из первого рендера и каждую секунду ставит 299 — нужно setLeft((prev) => prev - 1)",
        "React не перерисовывает компонент, если состояние меняется из таймера, — нужно принудительно обновлять компонент",
        "Мешает clearInterval в cleanup: в dev StrictMode вызывает его сразу и останавливает интервал — cleanup убрать",
        "Уменьшать значение напрямую: left-- внутри колбэка, чтобы не зависеть от асинхронного setLeft",
    ],
    answer=1,
    explanation="Функция внутри эффекта — замыкание: она запоминает переменные того рендера, в котором создана. Эффект с [] запускается "
                "один раз, поэтому колбэк интервала навсегда видит left = 300 и каждую секунду вызывает setLeft(299). Состояние уже 299, "
                "React ничего не меняет — таймер «замирает». Функциональное обновление setLeft((prev) => prev - 1) получает актуальное "
                "значение от React и не зависит от замыкания. Можно было бы добавить left в зависимости, но тогда интервал пересоздавался "
                "бы каждую секунду. clearInterval в cleanup убирать нельзя — без него интервал переживёт размонтирование, а left-- "
                "мутирует константу и ломает правило «состояние меняется только через setState».",
    hints=[
        "Какое значение left «видит» функция, созданная при первом рендере?",
        "Эффект с [] запускается один раз — и колбэк интервала тоже создаётся один раз.",
        "У setState есть форма, которая принимает функцию от предыдущего значения.",
    ],
))

TASKS.append(task(
    T, 5, "find_bug", 4, "qa",
    "Ира: «Веб-версия КофеБота. Шаги: открыть заказ #41 → переключиться на #42 → снова на #41 → дождаться готовности. Ожидаю: одно "
    "“Кофе готов!”. Факт: тост приходит три раза, а статус иногда показывается от другого заказа. Локально в dev тост двоится сразу».",
    "В чём причина и какое исправление правильное?",
    code=src(r'''
import { useEffect, useState } from 'react';
import { showToast } from './toast';

export function OrderStatus({ orderId }) {
  const [status, setStatus] = useState('pending');

  useEffect(() => {
    const socket = new WebSocket(`wss://coffee.codzilla.dev/orders/${orderId}`);

    socket.onmessage = (event) => {
      const data = JSON.parse(event.data);
      setStatus(data.status);
      if (data.status === 'ready') {
        showToast('Кофе готов! Забирай на 3 этаже');
      }
    };
  }, [orderId]);

  return (
    <p>
      Заказ #{orderId}: {status}
    </p>
  );
}
'''),
    language="jsx",
    options=[
        "Каждая смена orderId открывает новый WebSocket, а старые никто не закрывает — вернуть из эффекта cleanup с socket.close()",
        "Заменить [orderId] на [] — сокет будет создаваться один раз, и дубли уведомлений пропадут",
        "Убрать <StrictMode> из main.jsx — это он в dev запускает эффекты дважды и дублирует уведомления",
        "Обернуть showToast в debounce на пару секунд — одинаковые уведомления склеятся в одно",
        "Хранить сокет в переменной модуля и перед созданием проверять if (!socket), чтобы не открывать второй",
    ],
    answer=0,
    explanation="Эффект, который подписывается на что-то внешнее (сокет, интервал, addEventListener), обязан вернуть функцию очистки. "
                "React вызывает её перед повторным запуском эффекта (сменился orderId) и при размонтировании. Без неё каждый переход между "
                "заказами оставляет живой сокет со своим onmessage: он продолжает вызывать setStatus и показывать тосты — отсюда дубли и "
                "статус чужого заказа. В dev StrictMode нарочно монтирует компонент дважды, чтобы такие утечки проявлялись сразу; "
                "выключить его — значит спрятать симптом. [] сломает переключение заказов, debounce маскирует проблему, а сокет в переменной "
                "модуля с проверкой if (!socket) так и останется подключённым к первому заказу — статусы остальных не придут вовсе.",
    hints=[
        "Что происходит со старым сокетом, когда orderId меняется?",
        "Эффект может вернуть функцию — React вызовет её перед следующим запуском и при размонтировании.",
        "Зачем StrictMode в dev запускает эффекты дважды?",
    ],
))

# =====================================================================
# fe-api-loading
# =====================================================================
T = "fe-api-loading"
TOPICS.append({"topic_id": T, "theory": """fetch: что важно помнить

fetch(url) возвращает промис с объектом Response. Промис отклоняется только при сетевой ошибке (нет связи, DNS, заблокировал CORS) или при отмене запроса. Ответы 404 и 500 для fetch — успешные, поэтому статус проверяют вручную:

    const res = await fetch('/api/products/7');
    if (!res.ok) throw new Error(`HTTP ${res.status}`); // ok — статусы 200–299
    const product = await res.json();

res.json() — тоже промис, и он отклоняется, если тело не JSON (например, HTML-страница nginx «502 Bad Gateway»). Для POST с JSON нужны заголовок и сериализация:

    fetch('/api/orders', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(order),
    });

Состояния экрана

У любого экрана с загрузкой минимум четыре состояния: загрузка, ошибка, пусто, данные. Пустой массив в начальном состоянии означает «ещё не знаем», а не «заказов нет». Храни статус явно ('loading' | 'error' | 'success'), для ошибки показывай понятный текст и кнопку «Повторить», для пустого списка — подсказку, что делать дальше.

Нормализация ошибок

Ошибки приходят в разных формах: отклонённый fetch, статус 4xx/5xx, битый JSON, detail от FastAPI (строка или массив ошибок валидации при 422). Сделай одну функцию-обёртку, которая приводит всё к одному виду, например { kind, status, message, retryable }. Компоненты тогда не разбирают fetch, а просто показывают message и решают, нужна ли кнопка «Повторить».

Ретраи

Повторять имеет смысл только то, что может пройти со второго раза:
• сетевые ошибки, 5xx, 408 и 429 (Too Many Requests — смотри заголовок Retry-After);
• не 4xx: неверные данные, нет прав или товара — повтор вернёт то же самое;
• только идемпотентные запросы (GET, PUT, DELETE); POST — лишь с ключом идемпотентности, иначе можно дважды создать заказ;
• ограниченное число попыток и растущая пауза: 200, 400, 800 мс (экспоненциальный backoff), лучше со случайным разбросом (jitter), чтобы клиенты не били в сервер одновременно.

Гонки ответов и отмена

Ответы приходят не в том порядке, в котором ушли запросы. Классика — поиск: ответ на «ла» пришёл после ответа на «лампа» и перетёр его. Защиты:
• requestId: каждый запрос получает номер из счётчика; после await сравниваешь его с номером последнего запроса и устаревший ответ выбрасываешь;
• AbortController: отменяет прошлый запрос и экономит трафик;
• в useEffect — отмена в cleanup:

    useEffect(() => {
      const controller = new AbortController();
      fetch(url, { signal: controller.signal })
        .then(handle)
        .catch((err) => { if (err.name !== 'AbortError') setError(err); });
      return () => controller.abort();
    }, [url]);

Отменённый запрос завершается ошибкой AbortError — пользователю её не показывают. На грейде Middle эту механику часто берут на себя библиотеки серверного состояния, но понимать, что они делают под капотом, нужно уже сейчас."""})

TASKS.append(task(
    T, 1, "write_code", 2, "teamlead",
    "Гена: «Страница товара в Маркете показывает пустую карточку, если товар удалён, и падает с «Cannot read properties of undefined», "
    "когда бэк отвечает 500. Всё потому, что мы не смотрим на статус ответа. Поправь загрузку».",
    "Допиши loadProduct(server, id): запроси fakeFetch(server, `/api/products/${id}`). Если ответ успешный (res.ok) — верни "
    "{ state: 'success', product: <JSON из ответа> }. Если статус 404 — { state: 'not_found' }. Любой другой неуспешный статус — "
    "{ state: 'error', status: <код ответа> }.",
    code=src(r'''
// Имитация fetch: server — «бэкенд», словарь url -> { status, body }.
// Неизвестный url — ответ 404.
function fakeFetch(server, url) {
  const r = server[url] ?? { status: 404, body: { detail: 'Not Found' } };
  return Promise.resolve({
    ok: r.status >= 200 && r.status < 300,
    status: r.status,
    json: () => Promise.resolve(r.body),
  });
}

async function loadProduct(server, id) {
  // твой код
}
'''),
    language="javascript",
    entry="loadProduct",
    answer=src(r'''
// Имитация fetch: server — «бэкенд», словарь url -> { status, body }.
// Неизвестный url — ответ 404.
function fakeFetch(server, url) {
  const r = server[url] ?? { status: 404, body: { detail: 'Not Found' } };
  return Promise.resolve({
    ok: r.status >= 200 && r.status < 300,
    status: r.status,
    json: () => Promise.resolve(r.body),
  });
}

async function loadProduct(server, id) {
  const res = await fakeFetch(server, `/api/products/${id}`);
  if (res.ok) {
    const product = await res.json();
    return { state: 'success', product };
  }
  if (res.status === 404) {
    return { state: 'not_found' };
  }
  return { state: 'error', status: res.status };
}
'''),
    tests=[
        {"input": [{"/api/products/7": {"status": 200, "body": {"id": 7, "title": "Лампа Эдисона E27", "price": 490}}}, 7],
         "expected": {"state": "success", "product": {"id": 7, "title": "Лампа Эдисона E27", "price": 490}}},
        {"input": [{"/api/products/7": {"status": 200, "body": {"id": 7, "title": "Лампа Эдисона E27", "price": 490}}}, 8],
         "expected": {"state": "not_found"}},
        {"input": [{"/api/products/3": {"status": 500, "body": {"detail": "Internal Server Error"}}}, 3],
         "expected": {"state": "error", "status": 500}},
        {"input": [{"/api/products/5": {"status": 403, "body": {"detail": "Forbidden"}}}, 5],
         "expected": {"state": "error", "status": 403}},
    ],
    explanation="Главная ловушка fetch: он отклоняет промис только при сетевой ошибке. Ответы 404 и 500 для него — нормальные ответы, "
                "промис успешно резолвится, и если сразу вызвать res.json(), код получит {detail: ...} вместо товара. Поэтому после "
                "запроса проверяй res.ok (true для статусов 200–299) и разбирай res.status: 404 — ожидаемая ситуация «товара нет», "
                "для неё показывают отдельный экран, а 5xx — сбой, после которого уместна кнопка «Повторить». res.json() тоже "
                "возвращает промис, поэтому перед ним нужен await.",
    hints=[
        "fetch не бросает ошибку на 404 и 500 — смотри на res.ok и res.status.",
        "Сначала проверь res.ok, потом отдельно res.status === 404.",
        "Не забудь await перед res.json() — это тоже промис.",
    ],
))

RETRY_FAKE = r'''
// Имитация сети: responses[i] — что сервер ответит на i-ю попытку:
// число — HTTP-статус, 'network' — соединение оборвалось (fetch отклоняет промис).
function fakeFetch(responses, i) {
  const r = responses[i] ?? 'network';
  if (r === 'network') {
    return Promise.reject(new TypeError('Failed to fetch'));
  }
  return Promise.resolve({ ok: r >= 200 && r < 300, status: r });
}
'''

TASKS.append(task(
    T, 2, "find_bug", 3, "qa",
    "Ира: «Проверяю промокод на оформлении заказа. На неверный код бэк отвечает 400, но в его логах по три одинаковых запроса, "
    "а сообщение об ошибке появляется с задержкой. Когда сервер реально лежит, всё правильно: три попытки с паузами».",
    "fetchWithRetry(responses, maxAttempts) должна повторять запрос только при сетевой ошибке, статусах 5xx и 429 — не больше "
    "maxAttempts попыток, с паузами 200, 400, 800… мс между ними. При остальных неуспешных статусах результат возвращается сразу. "
    "Функция возвращает { ok, status, attempts, waits }. Найди и исправь ошибку.",
    code=src(RETRY_FAKE + r'''
async function fetchWithRetry(responses, maxAttempts) {
  const waits = [];
  // имитация паузы: вместо настоящего таймера запоминаем, сколько ждали
  const sleep = (ms) => {
    waits.push(ms);
    return Promise.resolve();
  };

  let result = { ok: false, status: null, attempts: 0, waits };
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      const res = await fakeFetch(responses, attempt - 1);
      if (res.ok) {
        return { ok: true, status: res.status, attempts: attempt, waits };
      }
      result = { ok: false, status: res.status, attempts: attempt, waits };
    } catch (err) {
      result = { ok: false, status: null, attempts: attempt, waits };
    }
    if (attempt < maxAttempts) {
      await sleep(200 * 2 ** (attempt - 1));
    }
  }
  return result;
}
'''),
    language="javascript",
    entry="fetchWithRetry",
    answer=src(RETRY_FAKE + r'''
function isRetryable(status) {
  return status >= 500 || status === 429;
}

async function fetchWithRetry(responses, maxAttempts) {
  const waits = [];
  // имитация паузы: вместо настоящего таймера запоминаем, сколько ждали
  const sleep = (ms) => {
    waits.push(ms);
    return Promise.resolve();
  };

  let result = { ok: false, status: null, attempts: 0, waits };
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    try {
      const res = await fakeFetch(responses, attempt - 1);
      if (res.ok) {
        return { ok: true, status: res.status, attempts: attempt, waits };
      }
      result = { ok: false, status: res.status, attempts: attempt, waits };
      if (!isRetryable(res.status)) {
        return result; // 4xx: повтор вернёт то же самое
      }
    } catch (err) {
      result = { ok: false, status: null, attempts: attempt, waits };
    }
    if (attempt < maxAttempts) {
      await sleep(200 * 2 ** (attempt - 1));
    }
  }
  return result;
}
'''),
    tests=[
        {"input": [[503, 503, 200], 3], "expected": {"ok": True, "status": 200, "attempts": 3, "waits": [200, 400]}},
        {"input": [["network", 200], 3], "expected": {"ok": True, "status": 200, "attempts": 2, "waits": [200]}},
        {"input": [[400, 200], 3], "expected": {"ok": False, "status": 400, "attempts": 1, "waits": []}},
        {"input": [[500, 500, 500], 3], "expected": {"ok": False, "status": 500, "attempts": 3, "waits": [200, 400]}},
        {"input": [[429, 200], 2], "expected": {"ok": True, "status": 200, "attempts": 2, "waits": [200]}},
        {"input": [[404], 3], "expected": {"ok": False, "status": 404, "attempts": 1, "waits": []}},
        {"input": [["network", "network"], 2], "expected": {"ok": False, "status": None, "attempts": 2, "waits": [200]}},
    ],
    explanation="Ретрай имеет смысл, только если следующая попытка может закончиться иначе: сеть моргнула, сервер перегружен (5xx) "
                "или попросил подождать (429 Too Many Requests). 4xx означает «запрос неправильный» — неверный промокод, нет прав, "
                "нет товара, — и повтор вернёт ту же ошибку, только позже и с лишней нагрузкой на бэк. В исходном коде после любого "
                "неуспешного статуса цикл шёл на следующую попытку, поэтому 400 повторялся трижды. Исправление — сразу возвращать "
                "результат, если статус не ретраибельный. И помни: безопасно повторять идемпотентные запросы; POST с оплатой без "
                "ключа идемпотентности ретраить нельзя — можно списать деньги дважды.",
    hints=[
        "Сравни, что происходит при статусе 503 и при статусе 400.",
        "Какие статусы вообще имеет смысл повторять?",
        "После неуспешного ответа проверь статус: если это не 5xx и не 429 — верни result сразу.",
    ],
))

NORM_FAKE = r'''
// Имитация fetch. server — словарь url -> ответ:
//   { status, body } — обычный JSON-ответ;
//   { status, html } — тело не JSON (например, страница nginx «502 Bad Gateway»);
//   'offline'        — сети нет, fetch отклоняет промис.
// Неизвестный url — 404 { detail: 'Not Found' }.
function fakeFetch(server, url) {
  const r = server[url] ?? { status: 404, body: { detail: 'Not Found' } };
  if (r === 'offline') {
    return Promise.reject(new TypeError('Failed to fetch'));
  }
  return Promise.resolve({
    ok: r.status >= 200 && r.status < 300,
    status: r.status,
    json: () =>
      r.html !== undefined
        ? Promise.reject(new SyntaxError('Unexpected token < in JSON at position 0'))
        : Promise.resolve(r.body),
  });
}
'''

TASKS.append(task(
    T, 3, "write_code", 3, "teamlead",
    "Марина: «В коде Маркета ошибки API обрабатываются в каждом компоненте по-своему: где-то alert(err), где-то на экране "
    "«[object Object]». Сделай одну обёртку request, которая всегда возвращает ошибку в одном формате, — а UI уже решит, что показать».",
    "Напиши request(server, url). При успешном ответе с JSON верни { ok: true, data }. Иначе верни "
    "{ ok: false, error: { kind, status, message, retryable } }:\n"
    "• fetch отклонил промис (нет сети) → kind 'network', status null, message 'Нет соединения с сервером', retryable true;\n"
    "• статус не 2xx → kind 'http', status — код ответа; message — поле detail из JSON-тела: строка как есть, массив (ошибки "
    "валидации FastAPI, 422) — все msg через '; ', а если JSON-тела или detail нет — 'Ошибка <код>'; retryable — true только для 5xx и 429;\n"
    "• статус 2xx, но тело не JSON → kind 'parse', status — код ответа, message 'Некорректный ответ сервера', retryable false.",
    code=src(NORM_FAKE + r'''
async function request(server, url) {
  // твой код
}
'''),
    language="javascript",
    entry="request",
    answer=src(NORM_FAKE + r'''
function detailMessage(body) {
  const detail = body && body.detail;
  if (typeof detail === 'string') {
    return detail;
  }
  if (Array.isArray(detail)) {
    return detail.map((d) => d.msg).join('; ');
  }
  return null;
}

async function request(server, url) {
  let res;
  try {
    res = await fakeFetch(server, url);
  } catch (err) {
    return {
      ok: false,
      error: { kind: 'network', status: null, message: 'Нет соединения с сервером', retryable: true },
    };
  }

  let body = null;
  let parsed = true;
  try {
    body = await res.json();
  } catch (err) {
    parsed = false;
  }

  if (!res.ok) {
    return {
      ok: false,
      error: {
        kind: 'http',
        status: res.status,
        message: detailMessage(body) ?? `Ошибка ${res.status}`,
        retryable: res.status >= 500 || res.status === 429,
      },
    };
  }
  if (!parsed) {
    return {
      ok: false,
      error: { kind: 'parse', status: res.status, message: 'Некорректный ответ сервера', retryable: false },
    };
  }
  return { ok: true, data: body };
}
'''),
    tests=[
        {"input": [{"/api/cart": {"status": 200, "body": {"items": [{"id": 7, "qty": 2}], "total": 980}}}, "/api/cart"],
         "expected": {"ok": True, "data": {"items": [{"id": 7, "qty": 2}], "total": 980}}},
        {"input": [{"/api/cart": "offline"}, "/api/cart"],
         "expected": {"ok": False, "error": {"kind": "network", "status": None, "message": "Нет соединения с сервером", "retryable": True}}},
        {"input": [{"/api/products/99": {"status": 404, "body": {"detail": "Товар не найден"}}}, "/api/products/99"],
         "expected": {"ok": False, "error": {"kind": "http", "status": 404, "message": "Товар не найден", "retryable": False}}},
        {"input": [{"/api/profile": {"status": 422, "body": {"detail": [
            {"type": "value_error", "loc": ["body", "email"], "msg": "value is not a valid email address", "input": "nina.baton"},
            {"type": "missing", "loc": ["body", "phone"], "msg": "Field required", "input": None}]}}}, "/api/profile"],
         "expected": {"ok": False, "error": {"kind": "http", "status": 422,
                                             "message": "value is not a valid email address; Field required", "retryable": False}}},
        {"input": [{"/api/orders": {"status": 502, "html": "<html><body><h1>502 Bad Gateway</h1></body></html>"}}, "/api/orders"],
         "expected": {"ok": False, "error": {"kind": "http", "status": 502, "message": "Ошибка 502", "retryable": True}}},
        {"input": [{"/api/search": {"status": 429, "body": {"detail": "Слишком много запросов"}}}, "/api/search"],
         "expected": {"ok": False, "error": {"kind": "http", "status": 429, "message": "Слишком много запросов", "retryable": True}}},
        {"input": [{"/api/orders": {"status": 500, "body": {}}}, "/api/orders"],
         "expected": {"ok": False, "error": {"kind": "http", "status": 500, "message": "Ошибка 500", "retryable": True}}},
        {"input": [{"/api/cart": {"status": 200, "html": "<!doctype html><title>Техработы</title>"}}, "/api/cart"],
         "expected": {"ok": False, "error": {"kind": "parse", "status": 200, "message": "Некорректный ответ сервера", "retryable": False}}},
    ],
    explanation="Нормализация — один адаптер между сетью и UI: компонентам больше не нужно знать, как устроен fetch и что вернул "
                "FastAPI. Сетевую ошибку видно только по отклонённому промису fetch, поэтому его оборачивают в try/catch. HTTP-ошибку "
                "видно по res.ok, а текст берут из тела: FastAPI кладёт его в detail — строкой для HTTPException и массивом объектов "
                "с msg для ошибок валидации 422. Тело может оказаться не JSON (HTML-страница nginx при 502 или заглушка техработ), "
                "поэтому res.json() тоже в try/catch, а для такого случая есть запасное сообщение. Флаг retryable подсказывает UI, "
                "показывать ли кнопку «Повторить»: для 5xx и 429 повтор может помочь, для 4xx — нет.",
    hints=[
        "Разбери три места, где всё может сломаться: сам fetch, статус ответа и разбор JSON.",
        "Оберни await fakeFetch(...) и await res.json() в отдельные try/catch.",
        "Для detail проверь typeof detail === 'string' и Array.isArray(detail); во втором случае — detail.map((d) => d.msg).join('; ').",
    ],
))

TASKS.append(task(
    T, 4, "write_code", 3, "manager",
    "Стас: «Клиенты пишут в поддержку: открываешь «Мои заказы» — сначала «У вас пока нет заказов», через секунду появляются заказы. "
    "Кто-то уже решил, что заказ пропал. А если бэк лежит — просто пусто навсегда. Сделай по-человечески».",
    "Перепиши OrdersPage с явными состояниями: пока грузится — «Загружаем заказы…»; ошибка (сеть или статус не 2xx — проверь res.ok) — "
    "текст ошибки и кнопка «Повторить», которая перезапускает загрузку; пустой список — «У вас пока нет заказов» со ссылкой на каталог; "
    "иначе — список. Запрос отменяй через AbortController в cleanup эффекта, а AbortError не показывай как ошибку.",
    code=src(r'''
import { useEffect, useState } from 'react';

export function OrdersPage() {
  const [orders, setOrders] = useState([]);

  useEffect(() => {
    fetch('/api/orders')
      .then((res) => res.json())
      .then(setOrders);
  }, []);

  if (orders.length === 0) {
    return <p>У вас пока нет заказов</p>;
  }

  return (
    <ul>
      {orders.map((order) => (
        <li key={order.id}>
          Заказ №{order.id} — {order.total} ₽
        </li>
      ))}
    </ul>
  );
}
'''),
    language="jsx",
    answer=src(r'''
import { useEffect, useState } from 'react';

export function OrdersPage() {
  const [status, setStatus] = useState('loading');
  const [orders, setOrders] = useState([]);
  const [error, setError] = useState(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setStatus('loading');
    setError(null);

    fetch('/api/orders', { signal: controller.signal })
      .then((res) => {
        if (!res.ok) {
          throw new Error(`сервер ответил ${res.status}`);
        }
        return res.json();
      })
      .then((data) => {
        setOrders(data);
        setStatus('success');
      })
      .catch((err) => {
        if (err.name === 'AbortError') {
          return;
        }
        setError(err.message);
        setStatus('error');
      });

    return () => controller.abort();
  }, [attempt]);

  if (status === 'loading') {
    return <p role="status">Загружаем заказы…</p>;
  }

  if (status === 'error') {
    return (
      <div role="alert">
        <p>Не удалось загрузить заказы: {error}</p>
        <button type="button" onClick={() => setAttempt((n) => n + 1)}>
          Повторить
        </button>
      </div>
    );
  }

  if (orders.length === 0) {
    return (
      <p>
        У вас пока нет заказов. <a href="/catalog">Перейти в каталог</a>
      </p>
    );
  }

  return (
    <ul>
      {orders.map((order) => (
        <li key={order.id}>
          Заказ №{order.id} — {order.total} ₽
        </li>
      ))}
    </ul>
  );
}
'''),
    tests=[
        rx("Проверяется res.ok", r"\w+\.ok\b"),
        rx("Есть отдельное состояние загрузки", r"if\s*\([^)]*loading|loading['\"]?\s*(\)\s*)?(&&|\?)", "i"),
        rx("Есть отдельное состояние ошибки", r"if\s*\([^)]*['\"]error['\"]|if\s*\(\s*!?\s*error\s*\)|error['\"]?\s*(\)\s*)?(&&|\?)", "i"),
        rx("Кнопка «Повторить» с обработчиком клика", r"<button\b[^>]*onClick[\s\S]{0,300}?(Повтор|Попроб|Ещё раз|Еще раз|retry)", "i"),
        rx("Запрос получает signal от AbortController", r"new\s+AbortController\s*\(\s*\)[\s\S]*signal"),
        rx("В cleanup эффекта запрос отменяется", r"return\s*\(\s*\)\s*=>\s*\{?\s*\w+\.abort\(\s*\)"),
        rx("AbortError не показывается как ошибка", r"AbortError|signal\.aborted"),
        rx("В пустом состоянии есть ссылка на каталог", r"<a\s[^>]*href=|<Link\s[^>]*to="),
    ],
    explanation="Пустой массив в начальном состоянии нельзя трактовать как «заказов нет»: до ответа сервера мы просто ничего не знаем. "
                "Поэтому у экрана с загрузкой минимум четыре состояния — загрузка, ошибка, пусто и данные, — и каждое рисуется явно. "
                "Ошибкой считается и отклонённый промис (сеть), и ответ с !res.ok — сам fetch статус 500 ошибкой не считает. Кнопка "
                "«Повторить» меняет счётчик attempt из зависимостей эффекта, и эффект запускается заново. AbortController отменяет "
                "запрос, если пользователь ушёл со страницы (а в dev — при двойном запуске эффекта в StrictMode); отменённый запрос "
                "завершается AbortError, и показывать его пользователю не нужно.",
    hints=[
        "Заведи status со значениями 'loading' | 'error' | 'success' вместо того, чтобы угадывать по длине массива.",
        "В первом then проверь res.ok и брось ошибку, если он false, — тогда попадёшь в catch.",
        "const controller = new AbortController(); fetch(url, { signal: controller.signal }); return () => controller.abort(); "
        "Для «Повторить» добавь счётчик в зависимости эффекта.",
    ],
))

SEARCH_FAKE = r'''
// Имитация сети: ответ приходит через `ticks` микрозадач — чем больше, тем медленнее запрос.
function fakeSearch(query, ticks) {
  let p = Promise.resolve();
  for (let i = 0; i < ticks; i++) {
    p = p.then(() => {});
  }
  return p.then(() => ({ query, items: [`${query}: товар 1`, `${query}: товар 2`] }));
}
'''

SEARCH_SCENARIO = r'''
// Сценарий: пользователь быстро вводит запросы, у каждого своя «скорость» ответа.
// Эту функцию не меняй.
async function typeQueries(inputs) {
  const box = createSearchBox();
  await Promise.all(inputs.map(([query, ticks]) => box.onInput(query, ticks)));
  return box.screen;
}
'''

TASKS.append(task(
    T, 5, "find_bug", 4, "qa",
    "Ира: «Поиск в каталоге Маркета: быстро печатаю «лампа» — а в выдаче торшеры по запросу «ла». Воспроизводится на медленном 3G "
    "в DevTools: ответ на короткий запрос приходит позже длинного и перетирает его».",
    "createSearchBox — логика строки поиска: onInput запускает запрос на каждый ввод, ответы приходят в произвольном порядке. "
    "Исправь onInput так, чтобы на экране всегда оставался результат последнего введённого запроса, а устаревшие ответы отбрасывались "
    "(через requestId). Когда пришёл ответ на последний запрос, loading должен стать false. typeQueries — тестовый сценарий, его не меняй.",
    code=src(SEARCH_FAKE + r'''
function createSearchBox() {
  const screen = { query: '', items: [], loading: false };

  async function onInput(query, ticks) {
    screen.query = query;
    screen.loading = true;
    const data = await fakeSearch(query, ticks);
    screen.items = data.items;
    screen.loading = false;
  }

  return { screen, onInput };
}
''' + SEARCH_SCENARIO),
    language="javascript",
    entry="typeQueries",
    answer=src(SEARCH_FAKE + r'''
function createSearchBox() {
  const screen = { query: '', items: [], loading: false };
  let lastRequestId = 0;

  async function onInput(query, ticks) {
    const requestId = ++lastRequestId;
    screen.query = query;
    screen.loading = true;
    const data = await fakeSearch(query, ticks);
    if (requestId !== lastRequestId) {
      return; // ответ на устаревший запрос — выбрасываем
    }
    screen.items = data.items;
    screen.loading = false;
  }

  return { screen, onInput };
}
''' + SEARCH_SCENARIO),
    tests=[
        {"input": [[["л", 5], ["ла", 30], ["лам", 10]]],
         "expected": {"query": "лам", "items": ["лам: товар 1", "лам: товар 2"], "loading": False}},
        {"input": [[["лампа", 3]]],
         "expected": {"query": "лампа", "items": ["лампа: товар 1", "лампа: товар 2"], "loading": False}},
        {"input": [[["то", 20], ["тор", 2]]],
         "expected": {"query": "тор", "items": ["тор: товар 1", "тор: товар 2"], "loading": False}},
        {"input": [[["л", 1], ["ла", 2], ["лам", 40]]],
         "expected": {"query": "лам", "items": ["лам: товар 1", "лам: товар 2"], "loading": False}},
    ],
    explanation="Сеть не гарантирует порядок: короткий запрос «ла» может обрабатываться дольше, чем «лам», и его ответ придёт последним. "
                "Исходный код записывает на экран каждый пришедший ответ — побеждает самый медленный, а не самый свежий. requestId — "
                "простой счётчик: каждый ввод получает свой номер, а после await мы сравниваем его с номером последнего запроса. "
                "Не совпадает — ответ устарел: выбрасываем его и не трогаем ни items, ни loading. В браузере вдобавок отменяют прошлый "
                "запрос через AbortController, чтобы не тратить трафик, но проверка «мой ли это запрос» остаётся самой надёжной "
                "защитой — отменить можно не всё, а ответ может прийти до того, как отмена сработает.",
    hints=[
        "Какой ответ окажется на экране, если он пришёл последним, но был отправлен не последним?",
        "Заведи в createSearchBox счётчик lastRequestId и выдавай каждому вызову onInput свой номер.",
        "После await сравни свой requestId с lastRequestId и, если они не равны, просто выйди из функции.",
    ],
))

# =====================================================================
# fe-typescript
# =====================================================================
T = "fe-typescript"
TOPICS.append({"topic_id": T, "theory": """TypeScript — это JavaScript с типами, которые проверяются до запуска. В браузер уходит обычный JS: при сборке типы просто вырезаются (Vite и другие сборщики их даже не проверяют — это делает tsc --noEmit, отдельной командой или в CI). Отсюда главное правило: тип — это обещание, а не проверка. Если API вернул строку вместо числа, TypeScript об этом не узнает.

Базовые типы

    let title: string = 'Лампа';
    let prices: number[] = [490, 990];
    let inStock: boolean = true;

Для локальных переменных тип обычно не пишут — TypeScript выводит его сам, в том числе для параметров колбэков в map и reduce. Явно типизируют границы: параметры функций, пропсы, данные извне.

interface и type

    interface Product {
      id: number;
      title: string;
      oldPrice?: number;                        // необязательное: number | undefined
    }
    type OrderStatus = 'new' | 'paid' | 'shipped';  // union из литералов

interface удобен для объектов (умеет extends), type — для union и псевдонимов. Для объектов они почти равнозначны: выбери один стиль в проекте. status: 'new' | 'paid' лучше, чем string: опечатку компилятор не пропустит, а редактор подскажет варианты. null и undefined — отдельные типы: string | null значит «строка или null».

Функции и пропсы

    function formatPrice(value: number): string { ... }

    interface ButtonProps {
      variant?: 'primary' | 'ghost';
      onClick: (id: number) => void;
      children: React.ReactNode;
    }
    function Button({ variant = 'primary', onClick, children }: ButtonProps) { ... }

Сужение типов (narrowing)

С union-значением работают после проверки: typeof x === 'string', x === undefined, x !== null, Array.isArray(x), 'field' in obj. Внутри ветки TS знает точный тип. Осторожно с проверкой на истинность (if (!x)): она отсекает не только undefined и null, но и 0, '' и NaN — для чисел и строк это частый баг.

any, unknown, as, !
• any выключает проверки и «заражает» всё, к чему прикасается: у any можно взять любое поле, и компилятор промолчит. Если тип заранее неизвестен — unknown: его нельзя использовать, пока не сузишь.
• value as Product — не проверка, а приказ компилятору «поверь». Оправдан редко.
• x! (non-null assertion) — «здесь точно не null». Ошибся — падение в рантайме.
• // @ts-ignore глушит ошибку на следующей строке. Если без этого правда никак — // @ts-expect-error с комментарием, почему.

strict

"strict": true в tsconfig.json включает strictNullChecks, noImplicitAny и другие проверки. В новом проекте — только так; выключить strict, чтобы «собралось», — значит выбросить половину пользы TypeScript. Старый JS-проект переводят постепенно: allowJs, файл за файлом, начиная с общих модулей и типов API."""})

TASKS.append(task(
    T, 1, "quiz", 2, "teamlead",
    "Гена: «Бэкендеры по ошибке начали отдавать цену строкой: \"price\": \"1490\". У нас на фронте interface Product { price: number }, "
    "strict включён, сборка зелёная. Как думаешь, что увидит покупатель?»",
    "Бэк прислал price строкой \"1490\". Что произойдёт при вызове showTotal()?",
    code=src(r'''
interface Product {
  id: number;
  title: string;
  price: number;
}

async function loadProduct(id: number): Promise<Product> {
  const res = await fetch(`/api/products/${id}`);
  return res.json();
}

export async function showTotal(): Promise<void> {
  const product = await loadProduct(7); // бэк прислал { "id": 7, "title": "Лампа", "price": "1490" }
  const delivery = 199;
  console.log(product.price + delivery);
}
'''),
    language="typescript",
    options=[
        "TypeScript выбросит ошибку в браузере: price не соответствует типу number",
        "TypeScript сам приведёт строку к числу, раз в интерфейсе указан number, и выведет 1689",
        "В рантайме типов нет: price окажется строкой, + склеит строки, и в консоли будет \"1490199\"",
        "Проект перестанет собираться, потому что res.json() вернул строку вместо числа",
    ],
    answer=2,
    explanation="TypeScript проверяет код только при компиляции, а в браузер уходит обычный JavaScript — все interface и : number просто "
                "вырезаются. res.json() возвращает any, и TS верит на слово, что там Product. В рантайме price оказывается строкой "
                "\"1490\", а оператор + со строкой склеивает: получится \"1490199\". Сборка об этом не узнает — она не видит реальных "
                "ответов сервера. Поэтому данные извне (API, localStorage, параметры URL) либо проверяют в рантайме (вручную или "
                "схемой вроде zod), либо как минимум фиксируют контракт с бэком. Тип — это обещание, а не проверка.",
    hints=[
        "Что остаётся от interface после сборки?",
        "Что вернёт \"1490\" + 199 в JavaScript?",
    ],
))

TASKS.append(task(
    T, 2, "write_code", 3, "teamlead",
    "Марина: «Карточку товара писали ещё на JS, а теперь проект на TypeScript — и в ProductCard всё типизировано как any, компилятор нам "
    "ничем не помогает. Опиши типы нормально, чтобы ошибку в пропсах было видно ещё в редакторе».",
    "Типизируй ProductCard.tsx без any. 1) Опиши Product: id (number), title (string), price (number), необязательное oldPrice (number), "
    "status — одно из 'in_stock' | 'out_of_stock' | 'preorder'. 2) Опиши пропсы ProductCardProps: product, необязательный variant "
    "('compact' | 'full', по умолчанию 'full') и onAddToCart — функция, которая принимает id товара (number) и ничего не возвращает. "
    "3) Типизируй formatPrice: принимает number, возвращает string. 4) Кнопка «В корзину» заблокирована, если товара нет в наличии.",
    code=src(r'''
type ProductCardProps = any;

export function ProductCard({ product, variant, onAddToCart }: ProductCardProps) {
  return (
    <article className={`card card--${variant}`}>
      <h3>{product.title}</h3>
      <p>{formatPrice(product.price)}</p>
      {product.oldPrice !== undefined && <s>{formatPrice(product.oldPrice)}</s>}
      <button onClick={() => onAddToCart(product.id)}>В корзину</button>
    </article>
  );
}

function formatPrice(value: any) {
  return `${value.toLocaleString('ru-RU')} ₽`;
}
'''),
    language="tsx",
    answer=src(r'''
type ProductStatus = 'in_stock' | 'out_of_stock' | 'preorder';

interface Product {
  id: number;
  title: string;
  price: number;
  oldPrice?: number;
  status: ProductStatus;
}

interface ProductCardProps {
  product: Product;
  variant?: 'compact' | 'full';
  onAddToCart: (id: number) => void;
}

export function ProductCard({ product, variant = 'full', onAddToCart }: ProductCardProps) {
  return (
    <article className={`card card--${variant}`}>
      <h3>{product.title}</h3>
      <p>{formatPrice(product.price)}</p>
      {product.oldPrice !== undefined && <s>{formatPrice(product.oldPrice)}</s>}
      <button
        disabled={product.status === 'out_of_stock'}
        onClick={() => onAddToCart(product.id)}
      >
        В корзину
      </button>
    </article>
  );
}

function formatPrice(value: number): string {
  return `${value.toLocaleString('ru-RU')} ₽`;
}
'''),
    tests=[
        nrx("Нигде нет any", r"\bany\b"),
        rx("Product описан через interface или type", r"(interface|type)\s+Product\b"),
        rx("oldPrice — необязательное число", r"oldPrice\s*\?\s*:\s*number"),
        rx("status — union из трёх строковых литералов",
           r"(['\"](in_stock|out_of_stock|preorder)['\"]\s*\|\s*){2}['\"](in_stock|out_of_stock|preorder)['\"]"),
        rx("variant — необязательный проп", r"variant\s*\?\s*:"),
        rx("variant — union 'compact' | 'full'", r"['\"]compact['\"]\s*\|\s*['\"]full['\"]|['\"]full['\"]\s*\|\s*['\"]compact['\"]"),
        rx("По умолчанию variant = 'full'", r"variant\s*=\s*['\"]full['\"]"),
        rx("onAddToCart: (id: number) => void", r"onAddToCart\s*:\s*\(\s*\w+\s*:\s*number\s*\)\s*=>\s*void"),
        rx("formatPrice принимает number и возвращает string", r"formatPrice\s*(=\s*)?\(\s*\w+\s*:\s*number\s*\)\s*:\s*string"),
        rx("Кнопка заблокирована в зависимости от status", r"disabled=\{[^}]*status"),
    ],
    explanation="Типы пропсов — это контракт компонента: если кто-то передаст price строкой или забудет onAddToCart, редактор подсветит "
                "ошибку сразу, а не пользователь в проде. Необязательное поле oldPrice?: number имеет тип number | undefined, поэтому "
                "перед использованием его проверяют (!== undefined). Union из строковых литералов для status и variant лучше, чем string: "
                "опечатку вроде 'out_of_stok' компилятор не пропустит, а автодополнение подскажет варианты. Тип колбэка "
                "(id: number) => void описывает, что компонент передаст наружу. Значение по умолчанию в деструктуризации "
                "(variant = 'full') убирает undefined внутри компонента. any выключает все эти проверки — в строгом проекте ему не место.",
    hints=[
        "Начни с interface Product: какие поля есть у товара и какие из них необязательные?",
        "Необязательное поле — oldPrice?: number, набор допустимых строк — union: 'a' | 'b' | 'c'.",
        "Тип функции-пропа: onAddToCart: (id: number) => void; значение по умолчанию задай в деструктуризации: variant = 'full'.",
    ],
))

TASKS.append(task(
    T, 3, "find_bug", 3, "qa",
    "Ира: «В «Моих заказах» у каждого заказа вместо суммы и даты написано undefined: «Заказ №1042 от undefined: undefined ₽». "
    "Бэк клянётся, что ничего не менял, и в ответе всё есть. Сборка при этом зелёная».",
    "Почему TypeScript пропустил ошибку и как исправить код так, чтобы подобные ошибки ловил компилятор?",
    code=src(r'''
// ответ API: [{ "id": 1042, "created_at": "2026-09-20", "total_price": 2380 }]

async function loadOrders(): Promise<any[]> {
  const res = await fetch('/api/orders');
  return res.json();
}

function orderLine(order: any): string {
  return `Заказ №${order.id} от ${order.createdAt}: ${order.totalPrice} ₽`;
}

export async function renderOrders(): Promise<string[]> {
  const orders = await loadOrders();
  return orders.map(orderLine);
}
'''),
    language="typescript",
    options=[
        "Дописать запасные значения: ${order.totalPrice ?? 0} и ${order.createdAt ?? '—'} — undefined больше не появится",
        "Описать ответ интерфейсом OrderDto { id; created_at; total_price } вместо any — TS подсветит опечатки в полях",
        "Привести поля явно: (order.totalPrice as number) и (order.createdAt as string) — TS начнёт их проверять",
        "Включить noImplicitAny в tsconfig.json — компилятор запретит any в этом файле и сам найдёт опечатки",
        "Заменить шаблонную строку на конкатенацию через + — внутри шаблонов TS не проверяет обращения к полям",
    ],
    answer=1,
    explanation="any — выключатель проверок: у значения типа any можно взять любое поле, и компилятор промолчит. Поэтому опечатки в "
                "именах полей (createdAt вместо created_at, totalPrice вместо total_price) дошли до пользователя. Если описать реальную "
                "форму ответа интерфейсом, TS скажет: «Property 'totalPrice' does not exist on type 'OrderDto'. Did you mean "
                "'total_price'?» — ошибка найдена до коммита. ?? 0 и as number лечат симптом: пользователь увидит «0 ₽» вместо суммы. "
                "noImplicitAny тут ни при чём — any написан явно, а шаблонные строки TS проверяет так же, как любые выражения. "
                "Если форма данных действительно неизвестна, используй unknown: он заставит проверить тип перед использованием.",
    hints=[
        "Какие проверки TS делает для значения типа any?",
        "Сравни имена полей в коде и в ответе API.",
        "Что сказал бы компилятор на order.totalPrice, будь у order точный тип?",
    ],
))

TASKS.append(task(
    T, 4, "find_bug", 3, "manager",
    "Стас: «Поставщик выставил несколько ламп с нулевым остатком, а на сайте у них «Нет данных о наличии» вместо «Нет в наличии». "
    "Люди звонят и спрашивают, когда привезут. Что там с кодом?»",
    "Найди причину неверной надписи и выбери правильное исправление.",
    code=src(r'''
interface Product {
  id: number;
  title: string;
  stock?: number; // undefined — склад ещё не прислал остатки
}

export function stockLabel(product: Product): string {
  if (!product.stock) {
    return 'Нет данных о наличии';
  }
  if (product.stock === 0) {
    return 'Нет в наличии';
  }
  return `В наличии: ${product.stock} шт.`;
}
'''),
    language="typescript",
    options=[
        "!product.stock отсекает и 0 — нулевой остаток попадает в «нет данных»; проверять явно: stock === undefined",
        "Заменить === 0 на == 0 — после первой проверки TS сужает тип, и строгое сравнение с нулём не срабатывает",
        "Сделать поле обязательным (stock: number) — тогда undefined не будет, и первая проверка станет не нужна",
        "Писать product.stock! во всех местах, чтобы TS знал, что значение есть, и не сужал тип до undefined",
        "Обернуть условие в Boolean(product.stock) — явное приведение надёжнее, чем отрицание через !",
    ],
    answer=0,
    explanation="Проверка на истинность (if (!x)) — удобное сужение, но она отсекает все falsy-значения: undefined, null, 0, '' и NaN. "
                "Для остатка 0 — нормальное значение, и оно означает «нет в наличии». Поэтому нулевой остаток попадает в первую ветку, "
                "а вторая проверка вообще никогда не срабатывает. TypeScript тут не поможет: после !product.stock тип сужается до number, "
                "и сравнение с 0 выглядит законным. Сужать нужно явно: product.stock === undefined (или == null, если может прийти и null). "
                "Сделать поле обязательным — значит соврать компилятору о реальных данных, ! просто глушит проверку, а Boolean(x) — "
                "та же проверка на истинность другими словами.",
    hints=[
        "Какие значения в JavaScript считаются falsy?",
        "Что вернёт функция для stock = 0?",
        "Проверяй отсутствие значения явно, а не через !.",
    ],
))

TASKS.append(task(
    T, 5, "code_review", 4, "teamlead",
    "Марина: «Джун закрыл задачу «перевести корзину Маркета на TypeScript». Сборка зелёная, но зелёной её сделали не совсем честно. "
    "Отметь, что нужно поправить до мерджа».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    code=src(r'''
// tsconfig.json в этом же PR:  "strict": true  ->  "strict": false   («иначе не собиралось»)

interface CartItem {
  id: number;
  title: string;
  price: number;
  qty: number;
}

const PROMO_CODES: { [code: string]: number } = { LAMP10: 10, BATON5: 5 };

export function cartTotal(items: CartItem[]): number {
  return items.reduce((sum, item) => sum + item.price * item.qty, 0);
}

export async function loadCart(userId: number): Promise<CartItem[]> {
  const res = await fetch(`/api/users/${userId}/cart`);
  const data: any = await res.json();
  return data.items;
}

export function totalWithPromo(items: CartItem[], code?: string): number {
  // @ts-ignore
  const percent: number = PROMO_CODES[code];
  return cartTotal(items) * (1 - percent / 100);
}

export function renderBadge(count: number): void {
  const badge = document.querySelector('#cart-badge')!;
  badge.textContent = String(count);
}
'''),
    language="typescript",
    options=[
        "strict: false выключает strictNullChecks и noImplicitAny во всём проекте — вернуть strict и чинить ошибки",
        "@ts-ignore прячет настоящий баг: без промокода или с неизвестным кодом percent — undefined, итог — NaN",
        "data: any и нет проверки res.ok: при 401 или 500 вернётся undefined вместо массива, а TS промолчит",
        "querySelector(...)! упадёт на странице без бейджа (например, на чекауте) — проверить результат на null",
        "interface CartItem лучше заменить на type — interface считается устаревшим и хуже работает с union",
        "В reduce нужно явно указать типы (sum: number, item: CartItem) — иначе там неявный any, а strict выключен",
        "Все функции стоит переписать на стрелочные: в TypeScript так принято, и this в них не теряется",
    ],
    answer=[0, 1, 2, 3],
    explanation="Хороший перевод на TypeScript — это не «чтобы собралось», а чтобы компилятор ловил ошибки. strict: false выключает "
                "главное — проверку null/undefined и запрет неявного any — сразу для всего проекта, такие правки tsconfig не проходят "
                "ревью мимоходом. @ts-ignore заглушил честную ошибку компилятора, а за ней реальный баг: неизвестный промокод даёт NaN "
                "в сумме заказа. data: any снова отключает проверки там, где данные приходят извне, а без res.ok ответ 401 превратится "
                "в падение где-то дальше. Оператор ! — обещание «здесь точно не null», и на странице без бейджа оно ломает скрипт. "
                "А interface против type и стрелочные функции — вопрос стиля, типы параметров колбэка reduce TS выводит сам.",
    hints=[
        "Посмотри на всё, что «глушит» компилятор: настройки, комментарии, any и !.",
        "Что вернёт PROMO_CODES['SALE50'] и чему тогда будет равен итог?",
        "Какие типы TS выводит сам, без явных аннотаций?",
    ],
))

# =====================================================================
# fe-vite
# =====================================================================
T = "fe-vite"
TOPICS.append({"topic_id": T, "theory": """Vite — сборщик и dev-сервер, стандарт для новых React-проектов (Create React App официально объявлен устаревшим). В разработке Vite отдаёт код нативными ES-модулями и применяет правки через HMR почти мгновенно, а для прода собирает оптимизированный бандл (с Vite 8 — сборщиком Rolldown на Rust, раньше — Rollup).

Команды

    npm create vite@latest admin -- --template react-ts
    npm run dev       # vite: dev-сервер, по умолчанию порт 5173
    npm run build     # tsc -b && vite build: проверка типов и сборка в dist/
    npm run preview   # vite preview: локально открыть собранный dist/

Vite не проверяет типы — он их вырезает, поэтому в шаблоне перед сборкой стоит tsc.

Структура

index.html лежит в корне проекта и служит точкой входа: в нём <script type="module" src="/src/main.tsx">. Папка public/ копируется в сборку как есть (favicon, robots.txt). Картинки и стили, импортированные из src, получают хэш в имени — такие файлы можно кэшировать навсегда.

Переменные окружения

Файлы .env, .env.local, .env.[mode], .env.[mode].local (файлы *.local в git не коммитят). В клиентский код попадают только переменные с префиксом VITE_:

    const apiUrl = import.meta.env.VITE_API_URL;

Значения подставляются в код при сборке — чтобы поменять их, нужна пересборка. Встроенные: import.meta.env.MODE, DEV, PROD, BASE_URL. vite build по умолчанию работает в режиме production, а vite build --mode staging прочитает .env.staging. Всё из VITE_ видно любому в DevTools: секреты туда не кладут никогда. Типы своих переменных описывают в файле деклараций (например, src/vite-env.d.ts) через interface ImportMetaEnv.

vite.config.ts

    import { fileURLToPath, URL } from 'node:url';
    export default defineConfig({
      plugins: [react()],
      base: '/',
      resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
      server: { port: 5173, proxy: { '/api': 'http://localhost:8000' } },
      build: { outDir: 'dist', sourcemap: true },
    });

• resolve.alias — короткие импорты вида '@/components/Button'. TypeScript про алиас не знает: продублируй его в tsconfig, в compilerOptions.paths.
• server.proxy — dev-сервер пересылает /api на бэкенд: фронт и API на одном origin, никакого CORS в разработке. Полная форма: { target, changeOrigin: true, rewrite }. Работает только в dev и preview — на проде проксирует nginx.
• base — путь, с которого раздаётся приложение. Если сайт живёт в подпапке (example.com/booking/), нужен base: '/booking/', иначе index.html будет искать /assets/... в корне домена — и получится белый экран. Роутеру передай basename={import.meta.env.BASE_URL}.

Частые ошибки
• process.env в клиентском коде — в Vite его нет, используй import.meta.env.
• Переменная без префикса VITE_ — в коде будет undefined.
• Ожидание, что смена переменной окружения контейнера поменяет уже собранный бандл. Настройки, которые должны меняться без пересборки, грузят в рантайме — отдельным файлом конфигурации."""})

TASKS.append(task(
    T, 1, "quiz", 2, "devops_colleague",
    "Дима: «Конфиг сборки Маркета напрямую влияет на деплой, так что прежде чем пускать тебя в него, проверим, как ты понимаешь Vite. "
    "Вот несколько утверждений — какие из них правда?»",
    "Выбери все верные утверждения про Vite.",
    options=[
        "В клиентский код попадают только переменные окружения с префиксом VITE_ — остальные из .env в бандл не уходят",
        "import.meta.env.VITE_API_URL заменяется значением во время сборки: чтобы поменять адрес API в готовом dist/, нужно пересобрать проект",
        "Секретный ключ платёжной системы можно хранить в VITE_PAYMENT_SECRET: после минификации его не прочитать",
        "server.proxy из vite.config продолжит проксировать /api и на проде, когда dist/ раздаёт nginx",
        "vite build складывает результат в dist/, а vite preview позволяет локально открыть именно эту сборку",
        "Алиаса '@' в resolve.alias достаточно — TypeScript сам поймёт импорты вида '@/components/Button'",
    ],
    answer=[0, 1, 4],
    explanation="Vite отдаёт в клиент только переменные с префиксом VITE_ (префикс меняется через envPrefix) и подставляет их значения "
                "прямо в код во время сборки. Поэтому смена адреса API требует пересборки, а всё, что лежит в VITE_-переменной, видно "
                "любому в DevTools: минификация не шифрует строки. server.proxy — функция dev-сервера (и vite preview); в проде статику "
                "из dist/ раздаёт nginx или CDN, и проксирование /api настраивают уже там. vite build кладёт сборку в dist/, а vite "
                "preview — быстрый способ проверить её локально перед деплоем. resolve.alias знает только сборщик: редактору и tsc "
                "нужен такой же путь в compilerOptions.paths.",
    hints=[
        "В какой момент значения import.meta.env попадают в код?",
        "Кто раздаёт файлы на проде — dev-сервер Vite или что-то другое?",
        "Кто, кроме сборщика, разбирает импорты в проекте на TypeScript?",
    ],
))

TASKS.append(task(
    T, 2, "write_code", 3, "teamlead",
    "Гена: «Переносим фронт Маркета на Vite. Бэкенд локально крутится на localhost:8000, и его маршруты начинаются с /v1, без всякого "
    "/api. А фронт ходит на /api/… — пусть dev-сервер проксирует, тогда локально никакого CORS. И заведи алиас @, надоели ../../../».",
    "Допиши vite.config.ts: 1) алиас '@' → папка src; 2) dev-сервер на порту 3000; 3) прокси: запросы, начинающиеся с /api, уходят на "
    "http://localhost:8000 с changeOrigin: true, а префикс /api отрезается (/api/v1/products → /v1/products).",
    code=src(r'''
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
});
'''),
    language="typescript",
    answer=src(r'''
import { fileURLToPath, URL } from 'node:url';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:8000',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
      },
    },
  },
});
'''),
    tests=[
        rx("Алиас '@' указывает на src", r"['\"]@['\"]\s*:[^\n]*src|find\s*:\s*['\"]@['\"][^\n]*src"),
        rx("Dev-сервер на порту 3000", r"port\s*:\s*3000\b"),
        rx("Настроен прокси для /api", r"proxy\s*:\s*\{[\s\S]*['\"]\^?/api['\"]\s*:"),
        rx("target — бэкенд на localhost:8000", r"target\s*:\s*['\"]http://(localhost|127\.0\.0\.1):8000/?['\"]"),
        rx("changeOrigin: true", r"changeOrigin\s*:\s*true"),
        rx("Префикс /api отрезается через rewrite", r"rewrite\s*:[^\n]*replace\([^\n]*api"),
        rx("Плагин React на месте", r"plugins\s*:\s*\[[^\]]*react\(\s*\)"),
    ],
    explanation="Прокси dev-сервера решает две задачи: фронт ходит на относительный /api (тот же origin — никакого CORS в разработке), "
                "а Vite пересылает запросы бэкенду. changeOrigin: true подменяет заголовок Host на адрес цели — многие серверы и "
                "виртуальные хосты без этого отвечают 404. rewrite нужен, потому что у бэка маршруты без префикса: /api/v1/products "
                "должен превратиться в /v1/products. Алиас '@' убирает цепочки ../../, но продублируй его в tsconfig "
                "(compilerOptions.paths), иначе TypeScript и редактор импорт не поймут. И помни: всё из блока server работает только "
                "локально — на проде проксированием занимается nginx.",
    hints=[
        "Алиасы задаются в resolve.alias, настройки dev-сервера — в server.",
        "Прокси в полной форме — объект: '/api': { target, changeOrigin, rewrite }.",
        r"rewrite: (path) => path.replace(/^\/api/, '') отрежет префикс.",
    ],
))

TASKS.append(task(
    T, 3, "incident", 3, "devops_colleague",
    "Дима: «Выкатил новую версию бронирования переговорок «Высоты» — теперь оно живёт не на отдельном домене, а на vysota-bc.ru/booking/. "
    "Вера пишет, что там белый экран. Под живой, nginx на /booking/ отвечает 200. Вот что вижу».",
    "В чём причина белого экрана и что делать?",
    code=src(r'''
$ curl -s https://vysota-bc.ru/booking/ | grep -E '<script|<link'
    <script type="module" crossorigin src="/assets/index-B7kq2x9d.js"></script>
    <link rel="stylesheet" crossorigin href="/assets/index-Cw3fL0pa.css">

$ curl -s -o /dev/null -w '%{http_code}\n' https://vysota-bc.ru/assets/index-B7kq2x9d.js
404
$ curl -s -o /dev/null -w '%{http_code}\n' https://vysota-bc.ru/booking/assets/index-B7kq2x9d.js
200

# Консоль браузера на https://vysota-bc.ru/booking/
GET https://vysota-bc.ru/assets/index-B7kq2x9d.js net::ERR_ABORTED 404 (Not Found)
GET https://vysota-bc.ru/assets/index-Cw3fL0pa.css net::ERR_ABORTED 404 (Not Found)

# vite.config.ts в репозитории
export default defineConfig({ plugins: [react()] })
'''),
    language="text",
    options=[
        "Сборка не завершилась, и JS-файла в образе нет — перезапустить пайплайн и выкатить заново",
        "Сборка с base по умолчанию ('/') ищет ассеты в корне домена — задать base: '/booking/', пересобрать и выкатить",
        "У пользователей закэширован старый index.html со ссылками на прежние ассеты — попросить нажать Ctrl+F5",
        "Не задана VITE_API_URL: без неё приложение падает при старте, и React не монтируется",
        "Nginx отдаёт JS с неверным MIME-типом, и браузер отказывается его выполнять — добавить types",
        "Ошибка в коде React после переезда роутинга на /booking/ — откатить последний коммит",
    ],
    answer=1,
    time_limit=15,
    explanation="Логи говорят сами за себя: HTML ссылается на /assets/…, по этому адресу 404, а по /booking/assets/… файл на месте. "
                "Vite по умолчанию собирает с base: '/', то есть пути к скриптам и стилям абсолютные от корня домена. Когда приложение "
                "живёт в подпапке, браузер не может загрузить бандл, React не монтируется, и остаётся пустой <div id=\"root\"> — тот "
                "самый белый экран. Лечится опцией base: '/booking/' и пересборкой; если есть React Router, ему нужен "
                "basename={import.meta.env.BASE_URL}, иначе маршруты не совпадут. Сборка при этом цела (файл отдаётся с кодом 200), "
                "а кэш и MIME ни при чём — файл просто ищут не там.",
    hints=[
        "Сравни адрес, по которому браузер ищет JS, с адресом, где файл реально лежит.",
        "Какой префикс путей Vite подставляет в index.html по умолчанию?",
        "Смотри в сторону опции base в vite.config.ts.",
    ],
))

TASKS.append(task(
    T, 4, "estimation", 3, "manager",
    "Стас: «Гена говорит, что Create React App больше не поддерживается и админку Маркета пора переводить на Vite. Мне нужна оценка "
    "к планированию: день? неделя? Что тебе нужно узнать, чтобы назвать срок?»",
    "Выбери вопросы, от ответов на которые реально зависит оценка переезда админки с CRA на Vite.",
    options=[
        "Сколько мест читают process.env.REACT_APP_* — их придётся перевести на import.meta.env.VITE_*",
        "Делали ли eject или используют craco / react-app-rewired — свой webpack-конфиг придётся заменять",
        "На чём тесты: Jest из react-scripts переносить на Vitest или настраивать Jest отдельно",
        "Используются ли src/setupProxy.js и поле homepage в package.json — их заменят server.proxy и base",
        "Какого цвета будет экран загрузки после переезда — оставляем логотип или делаем скелетон?",
        "Может, заодно перепишем админку на Next.js — раз уж всё равно меняем сборщик?",
        "Сколько сотрудников заходит в админку в день и когда у неё пиковая нагрузка?",
        "Нужно ли заодно переписать оставшиеся классовые компоненты на хуки перед переездом?",
    ],
    answer=[0, 1, 2, 3],
    time_limit=15,
    explanation="Смена сборщика — задача про инфраструктуру проекта, и объём зависит от того, сколько «магии» CRA проект использовал. "
                "Переменные окружения меняют и префикс, и способ чтения — это поиск и правка по всему коду. Eject или craco означают "
                "свой webpack-конфиг, и его перенос может съесть больше всего времени. Тесты на Jest из react-scripts перестанут "
                "запускаться — обычно их переводят на Vitest с почти тем же API. setupProxy.js и homepage превращаются в server.proxy "
                "и base. Плюс неочевидное: index.html переезжает в корень с <script type=\"module\">, %PUBLIC_URL% убирается, а JSX "
                "в файлах .js придётся переименовать в .jsx. Разумная оценка: чистый CRA без eject — 1–2 дня, с кастомным webpack и "
                "сотней тестов — 3–5 дней, а Next.js и хуки — отдельные проекты, которые в эту оценку не входят.",
    hints=[
        "Что в CRA работало «само», а в Vite настраивается явно?",
        "Подумай про переменные окружения, тесты, прокси и кастомный webpack.",
    ],
))

TASKS.append(task(
    T, 5, "architecture", 4, "devops_colleague",
    "Дима: «Переходим на схему «собрали один раз — выкатываем везде»: один Docker-образ фронта Маркета едет на stage, потом на prod, "
    "плюс preview-стенды на каждый PR вида pr-123.stage.lampmarket.ru. А у вас адрес API, DSN Sentry и публичный ключ платёжного "
    "виджета зашиты через import.meta.env при сборке. Как будем конфигурировать?»",
    "Какой подход выбрать, чтобы один и тот же образ работал во всех окружениях?",
    code=src(r'''
// src/config.ts — сейчас
export const config = {
  apiUrl: import.meta.env.VITE_API_URL,
  sentryDsn: import.meta.env.VITE_SENTRY_DSN,
  paymentPublicKey: import.meta.env.VITE_PAYMENT_PUBLIC_KEY,
};
'''),
    language="typescript",
    options=[
        "Прокинуть VITE_API_URL и остальные значения как переменные окружения контейнера в Kubernetes — import.meta.env прочитает их при старте",
        "Собирать в CI отдельный образ под каждое окружение со своим .env.[mode] и выкатывать нужный",
        "Runtime-конфиг: entrypoint контейнера при старте пишет /config.js из env, index.html грузит его до бандла",
        "Выбирать настройки по window.location.hostname: держать в коде словарь «домен → адрес API»",
        "Хранить конфиг в localStorage: при первом заходе QA и разработчики вписывают адрес API вручную",
    ],
    answer=2,
    explanation="import.meta.env — не чтение переменных в рантайме: Vite на этапе сборки заменяет эти выражения строками, и в готовом "
                "бандле уже зашит конкретный адрес, так что переменные контейнера на собранный JS не влияют. Отдельный образ на окружение "
                "работает, но ломает главный принцип: на prod едет не тот артефакт, который проверяли на stage. Словарь по hostname "
                "не переживёт preview-стенды (каждый новый pr-123 — правка кода и пересборка) и засветит адреса всех окружений в бандле. "
                "localStorage — не конфигурация, а ручной труд. Runtime-конфиг отделяет сборку от настройки: образ один, а /config.js "
                "генерируется из env при старте контейнера. Цена — ещё один запрос при загрузке и дисциплина: туда кладут только "
                "публичные значения (всё, что попало в браузер, видно пользователю), а config.js отдают с Cache-Control: no-cache, "
                "иначе после смены настроек браузеры будут держать старую версию.",
    hints=[
        "Когда именно import.meta.env превращается в конкретное значение?",
        "Какой вариант позволяет выкатить на prod ровно тот образ, что проверили на stage?",
        "Конфиг можно загрузить отдельным файлом, который генерируется при старте контейнера.",
    ],
))

# =====================================================================
OUT = "Tools/Content/out/frontend_f3.json"


def shuffle_options(all_tasks, salt="f3"):
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

if __name__ == "__main__":
    data = {"track": "frontend", "part": "f3", "topics": TOPICS, "tasks": TASKS}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    for tp in TOPICS:
        print(tp["topic_id"], "theory:", len(tp["theory"]))
    print("tasks:", len(TASKS))
