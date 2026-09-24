#!/usr/bin/env python3
"""Генератор контента: frontend, часть f1 (fe-html, fe-css-basics, fe-css-layout, fe-dom)."""
import json
import os
import random

OUT = "Tools/Content/out/frontend_f1.json"

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}


def xp(d, g):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


def C(s):
    """Код: убираем ведущий перевод строки, гарантируем завершающий."""
    s = s.lstrip("\n")
    return s if s.endswith("\n") else s + "\n"


def T(s):
    """Теория: убираем крайние переводы строк."""
    return s.strip("\n")


def task(task_id, topic, typ, diff, char, story, question, explanation, hints,
         code=None, language=None, options=None, answer=None, tests=None,
         entry=None, time_limit=None, grade="junior"):
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
    return {
        "task_id": task_id,
        "topic_id": topic,
        "grade": grade,
        "type": typ,
        "difficulty": diff,
        "xp_reward": xp(diff, grade),
        "time_limit_minutes": time_limit,
        "character": char,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": hints,
    }


def chk(desc, regex=None, flags="i", not_regex=None):
    e = {"regex": regex} if regex is not None else {"not_regex": not_regex}
    e["flags"] = flags
    return {"input": desc, "expected": e}


# регулярки для атрибутов: порядок атрибутов в теге любой, кавычки любые
Q = r"""["']?"""


def attr(name, value):
    return r"\b" + name + "=" + Q + value + r"\b"


REQ = r"\srequired\b"


def tag_with(tag, *conds):
    return "<" + tag + "".join("(?=[^>]*" + c + ")" for c in conds) + "[^>]*>"


topics = []
tasks = []

# =====================================================================
# fe-html
# =====================================================================
topics.append({"topic_id": "fe-html", "theory": T("""
HTML описывает смысл и структуру страницы, а не её внешний вид. Разметку читают браузер, поисковик, скринридер и твои коллеги. Если всё сделано на div, никто из них не поймёт, где меню, а где главное.

Семантические теги
• header — шапка страницы или раздела: логотип, меню.
• nav — основная навигация.
• main — основное содержимое, на странице ровно один.
• article — самостоятельный кусок: карточка товара, отзыв, пост.
• section — тематический раздел со своим заголовком; aside — побочное (фильтры, «похожие товары»).
• footer — подвал: контакты, копирайт.
• h1–h6 — заголовки по уровням, без пропусков. h1 обычно один. Уровень выбирают по смыслу, а не по размеру шрифта — размер задаёт CSS.

div и span остаются для обёрток «только ради стилей». То, что делает действие, — button; то, что ведёт на другую страницу, — a с href.

Формы

    <form action="/api/orders" method="post">
      <label for="phone">Телефон</label>
      <input id="phone" name="phone" type="tel" required>
      <button type="submit">Заказать</button>
    </form>

• action — куда отправить, method — как. GET кладёт данные в адрес (?q=лампа) — для поиска и фильтров. POST кладёт их в тело запроса — для заказов, регистрации и всего, что меняет данные.
• name — ключ, под которым значение уйдёт на сервер. Нет name — поле не отправится вообще. id серверу не виден, он нужен для label, CSS и JS.
• label связывает подпись с полем: for совпадает с id поля (или поле лежит прямо внутри label). Клик по подписи ставит фокус в поле, скринридер её читает. placeholder — не замена label: он исчезает, как только начинаешь печатать.
• type выбирай по смыслу: email, tel, number, date, password, checkbox, radio. На телефоне type="tel" откроет цифровую клавиатуру.
• radio с одинаковым name — группа, выбрать можно один. checkbox отправляется, только если отмечен.
• select с option — список, textarea — многострочный текст.
• disabled — поле неактивно и НЕ отправляется; readonly — менять нельзя, но значение уйдёт.
• button внутри form по умолчанию type="submit". Кнопке, которая не должна отправлять форму, пиши type="button". Правило: type у кнопки всегда явно.

Встроенная валидация
Браузер не отправит форму, пока поля не пройдут проверки из атрибутов:
• required — поле обязательно (у checkbox — должна стоять галочка, у select — выбрана не первая опция-заглушка с пустым value);
• minlength / maxlength — длина строки;
• min / max / step — для number и date; по умолчанию step="1", поэтому для копеек нужен step="0.01";
• pattern — регулярка на всё значение целиком, проверяется только у непустого поля: pattern="[0-9]{6}";
• type="email" и type="url" проверяют формат.

Невалидные поля получают псевдокласс :invalid, novalidate на form отключает проверки. Сравнить два поля между собой («пароли совпадают») браузер сам не умеет — это делают на JS через setCustomValidity. И главное: браузерная валидация — для удобства, а не для защиты. Её обходят через DevTools или curl, поэтому сервер проверяет всё заново.

Частые ошибки
• поле без name: «форма ушла, а данных нет»;
• кнопка без type внутри формы, которая внезапно отправляет форму;
• label, у которого for не совпадает с id;
• несколько h1 и заголовки, выбранные «по размеру».

Проверять, что реально уходит на сервер, удобно в DevTools → Network: открой запрос и посмотри вкладку Payload.
""")})

tasks.append(task(
    "fe-html-01", "fe-html", "write_code", 1, "teamlead",
    "Гена: «Нина из пекарни “Батон” прислала вёрстку главной от старого фрилансера — сплошные div. Поисковик не понимает, где меню, а где контент, скринридер тоже. Переведи на семантические теги, классы не трогай — на них завязаны стили».",
    "Замени div-обёртки на семантические теги: шапку (.header) — на header, меню (.menu) — на nav, основное содержимое (.content) — на main, подвал (.footer) — на footer. Заголовок «Свежий хлеб с доставкой за час» сделай единственным h1. Классы оставь как есть.",
    "Семантические теги говорят браузеру, поисковику и скринридеру, что это за блок: header — шапка, nav — навигация, main — главное содержимое (он на странице один), footer — подвал. Скринридер умеет прыгать сразу к main или nav, а поисковик лучше понимает, что на странице главное. h1 — главный заголовок: div с крупным шрифтом выглядит так же, но для машин это просто текст. Классы можно оставить — стили продолжат работать, потому что привязаны к классам, а не к тегам. Правило на каждый день: сначала выбери тег по смыслу, а внешний вид задай в CSS.",
    ["Для каждого div с классом header, menu, content и footer есть тег с тем же смыслом.",
     "Не забудь поменять и закрывающие теги: </div> → </nav> и так далее.",
     "Заголовок: <h1 class=\"title\">Свежий хлеб с доставкой за час</h1> внутри main."],
    code=C("""
<div class="header">
  <div class="logo">Пекарня «Батон»</div>
  <div class="menu">
    <a href="/catalog">Каталог</a>
    <a href="/delivery">Доставка</a>
    <a href="/contacts">Контакты</a>
  </div>
</div>
<div class="content">
  <div class="title">Свежий хлеб с доставкой за час</div>
  <p>Печём с шести утра, привозим тёплым.</p>
</div>
<div class="footer">
  <p>© 2026 Пекарня «Батон»</p>
</div>
"""),
    language="html",
    answer=C("""
<header class="header">
  <div class="logo">Пекарня «Батон»</div>
  <nav class="menu">
    <a href="/catalog">Каталог</a>
    <a href="/delivery">Доставка</a>
    <a href="/contacts">Контакты</a>
  </nav>
</header>
<main class="content">
  <h1 class="title">Свежий хлеб с доставкой за час</h1>
  <p>Печём с шести утра, привозим тёплым.</p>
</main>
<footer class="footer">
  <p>© 2026 Пекарня «Батон»</p>
</footer>
"""),
    tests=[
        chk("Шапка страницы — тег header", r"<header[\s>]"),
        chk("Все ссылки меню лежат внутри nav", r"<nav[\s>][\s\S]*?/catalog[\s\S]*?/contacts[\s\S]*?</nav>"),
        chk("Основное содержимое — main, внутри него h1 «Свежий хлеб…»",
            r"<main[\s>][\s\S]*?<h1[^>]*>\s*Свежий хлеб[\s\S]*?</h1>[\s\S]*?</main>"),
        chk("Подвал — тег footer", r"<footer[\s>][\s\S]*?</footer>"),
        chk("h1 на странице ровно один", not_regex=r"<h1[\s>][\s\S]*<h1[\s>]"),
        chk("Не осталось div с классами header, menu, content, title, footer",
            not_regex=r"<div[^>]*class=" + Q + r"(header|menu|content|title|footer)\b"),
    ],
))

tasks.append(task(
    "fe-html-02", "fe-html", "quiz", 2, "qa",
    "Ира: «Пишу тест-кейсы на форму регистрации “Лампового Маркета”. Хочу понять, что браузер отловит сам, а что должен проверять наш код. Помоги разделить».",
    "Какие проверки браузер выполнит сам при отправке этой формы, без единой строки JavaScript? Выбери все верные.",
    "required, type=\"email\", minlength и pattern — встроенная валидация: браузер не отправит форму и покажет подсказку у поля. pattern проверяет значение целиком и только у непустого поля: индекс можно не заполнять, но если заполнил — ровно 6 цифр. Совпадение двух полей браузер сам не сравнивает — это делают на JS через setCustomValidity или на сервере. Уникальность почты знает только база, то есть бэкенд. Правила «хотя бы одна цифра» в разметке нет, а браузер проверяет только то, что написано в атрибутах. И помни: всё это для удобства пользователя, а не для защиты — сервер обязан проверить данные заново.",
    ["Браузер знает только то, что написано в атрибутах полей.",
     "Для каких проверок нужно знать значение другого поля или данные из базы?"],
    code=C("""
<form action="/api/register" method="post">
  <label>Почта <input name="email" type="email" required></label>
  <label>Пароль <input name="password" type="password" required minlength="8"></label>
  <label>Повтори пароль <input name="password2" type="password" required></label>
  <label>Индекс <input name="zip" pattern="[0-9]{6}"></label>
  <button type="submit">Зарегистрироваться</button>
</form>
"""),
    language="html",
    options=[
        "Почта не пустая и похожа на адрес (есть @)",
        "Пароль не короче 8 символов",
        "Пароль и повтор пароля совпадают",
        "Индекс — ровно 6 цифр, если его заполнили",
        "Такая почта ещё не зарегистрирована",
        "В пароле есть хотя бы одна цифра",
    ],
    answer=[0, 1, 3],
))

tasks.append(task(
    "fe-html-03", "fe-html", "find_bug", 2, "qa",
    "Ира: «Оформление заказа в “Ламповом Маркете”. Шаги: 1) ввожу адрес; 2) ввожу промокод LAMP10; 3) жму “Применить”. Ожидаю: сумма пересчиталась со скидкой. Факт: страница перезагружается, и заказ уходит на оплату без скидки».",
    "Почему кнопка «Применить» отправляет заказ и как это правильно исправить?",
    "У button внутри form по умолчанию type=\"submit\", поэтому клик по «Применить» делает две вещи: запускает наш обработчик и отправляет форму. Адрес заполнен, валидация пройдена — заказ улетает на /checkout. Явный type=\"button\" превращает её в «просто кнопку» без действия по умолчанию. Можно было бы вызвать e.preventDefault() в обработчике, но правильнее описать смысл в разметке: тогда кнопка не отправит форму, даже если скрипт не загрузился. Обёртка в div ничего не меняет — кнопка всё равно остаётся внутри form. Правило на будущее: у каждой button пиши type явно.",
    ["Какой type у кнопки, если его не указать?",
     "Посчитай, сколько кнопок в этой форме на самом деле умеют её отправлять."],
    code=C("""
<form action="/checkout" method="post">
  <label for="address">Адрес доставки</label>
  <input id="address" name="address" required>

  <label for="promo">Промокод</label>
  <input id="promo" name="promo">
  <button class="promo-apply">Применить</button>

  <button type="submit">Оплатить заказ</button>
</form>

<script>
  document.querySelector('.promo-apply').addEventListener('click', () => {
    applyPromo(document.querySelector('#promo').value);
  });
</script>
"""),
    language="html",
    options=[
        "Поле промокода без required — браузер считает форму заполненной и отправляет её по любой кнопке",
        "У «Применить» нет type, а button в форме по умолчанию — submit. Нужно type=\"button\"",
        "Скрипт стоит после формы и не успевает повесить обработчик — его надо перенести в head",
        "Обернуть «Применить» вместе с полем промокода в отдельный div — тогда кнопка перестанет относиться к форме",
        "Форме нужен атрибут novalidate — иначе браузерная валидация сама отправляет форму после проверки",
    ],
    answer=1,
))

tasks.append(task(
    "fe-html-04", "fe-html", "write_code", 3, "client",
    "Артур, директор фитнес-клуба «Жми»: «Хочу, чтобы на пробную тренировку записывались прямо с сайта, а не звонили на ресепшен. Имя, телефон, почта по желанию, направление и галочка, что можно перезвонить. И чтобы пустую заявку отправить было нельзя».",
    "Сверстай форму записи. Отправка — POST на /api/trial. У каждого поля id и name совпадают, у каждого своя подпись label for:\n"
    "• name — имя: обязательно, не короче 2 символов;\n"
    "• phone — телефон: type=\"tel\", обязательно;\n"
    "• email — почта: type=\"email\", необязательно;\n"
    "• direction — select, обязателен: первый вариант — заглушка «Выбери направление» с пустым value, дальше варианты со значениями yoga, crossfit, boxing;\n"
    "• agree — checkbox «Можно перезвонить», обязателен;\n"
    "• кнопка «Записаться» с type=\"submit\".",
    "name — ключ, под которым значение уйдёт на сервер, а id связывает поле с label: клик по подписи ставит фокус в поле, и скринридер читает подпись. type=\"tel\" открывает на телефоне цифровую клавиатуру, type=\"email\" проверяет формат. required у select блокирует отправку, только пока выбрана первая опция-заглушка с пустым value: без такой заглушки первый вариант выбран сразу, и проверка всегда проходит. required у checkbox значит «галочка обязательна». Без method=\"post\" форма ушла бы GET-запросом, и телефон с почтой оказались бы в адресной строке и логах сервера.",
    ["Собери пары: <label for=\"x\"> и поле с id=\"x\" и name=\"x\".",
     "Ограничения задаются атрибутами: required, minlength=\"2\", type=\"tel\", type=\"email\".",
     "Заглушка в select: <option value=\"\">Выбери направление</option>."],
    code=C("""
<form>
  <!-- поля формы -->

  <button>Записаться</button>
</form>
"""),
    language="html",
    answer=C("""
<form action="/api/trial" method="post">
  <label for="name">Имя</label>
  <input id="name" name="name" type="text" required minlength="2" autocomplete="name">

  <label for="phone">Телефон</label>
  <input id="phone" name="phone" type="tel" required autocomplete="tel" placeholder="+7 900 000-00-00">

  <label for="email">Почта</label>
  <input id="email" name="email" type="email" autocomplete="email">

  <label for="direction">Направление</label>
  <select id="direction" name="direction" required>
    <option value="">Выбери направление</option>
    <option value="yoga">Йога</option>
    <option value="crossfit">Кроссфит</option>
    <option value="boxing">Бокс</option>
  </select>

  <input id="agree" name="agree" type="checkbox" required>
  <label for="agree">Можно перезвонить</label>

  <button type="submit">Записаться</button>
</form>
"""),
    tests=[
        chk("Форма: method=\"post\", action=\"/api/trial\"",
            tag_with("form", r"\baction=" + Q + r"/api/trial\b", attr("method", "post"))),
        chk("Имя: id и name = name, required, minlength=\"2\"",
            tag_with("input", attr("name", "name"), attr("id", "name"), REQ, attr("minlength", "2"))),
        chk("Телефон: id и name = phone, type=\"tel\", required",
            tag_with("input", attr("name", "phone"), attr("id", "phone"), attr("type", "tel"), REQ)),
        chk("Почта: id и name = email, type=\"email\"",
            tag_with("input", attr("name", "email"), attr("id", "email"), attr("type", "email"))),
        chk("Направление: select с id и name = direction, required и вариантами yoga, crossfit, boxing",
            tag_with("select", attr("name", "direction"), attr("id", "direction"), REQ)
            + "".join(r"(?=(?:(?!</select>)[\s\S])*?value=" + Q + v + r"\b)" for v in ("yoga", "crossfit", "boxing"))),
        chk("Первый вариант select — заглушка с пустым value",
            tag_with("select", attr("name", "direction")) + r"""\s*<option[^>]*\bvalue=(""|'')"""),
        chk("Согласие: checkbox с id и name = agree, required",
            tag_with("input", attr("name", "agree"), attr("id", "agree"), attr("type", "checkbox"), REQ)),
        chk("У полей name, phone, email, direction, agree есть label for",
            "".join(r"(?=[\s\S]*<label[^>]*\bfor=" + Q + f + r"\b)" for f in ("name", "phone", "email", "direction", "agree"))),
        chk("Кнопка отправки с type=\"submit\"", tag_with("button", attr("type", "submit"))),
    ],
))

tasks.append(task(
    "fe-html-05", "fe-html", "incident", 3, "devops_colleague",
    "Дима: «Алерт по “Батону”: с 11:15 ни одного заказа с сайта, Нина уже звонила Стасу. В 11:12 фронты выкатили v1.9.0 — “форма заказа с подписями для доступности”. Бэкенд живой, но отвечает 422. Логи и дифф ниже, решай быстро».",
    "Что сломалось и что делать? Выбери все верные утверждения и действия.",
    "Логи говорят прямо: запросы доходят, но в теле только comment, и бэкенд честно отвечает 422 «Field required». Дифф объясняет почему: при редизайне атрибут name заменили на id, а браузер (и FormData) отправляет только поля с name — id нужен лишь для label, CSS и JS. Пока прод теряет заказы, первое действие — откат на рабочую версию: это быстрее и безопаснее любой починки на горячую. Потом возвращаем полям name, оставляем id и label (доступность — хорошая идея, просто сделанная наполовину) и проверяем тело запроса во вкладке Network. Ослабить валидацию на бэкенде — значит принимать заказы без телефона и адреса, а рестарт подов и novalidate причину не трогают: required лишь не даёт отправить пустое поле, но никак не влияет на то, что поле без name вообще не попадает в запрос. После разбора стоит добавить e2e-тест «оформить заказ», чтобы такое ловилось до прода.",
    ["Посмотри, какие поля на самом деле доходят до бэкенда.",
     "Чем отличаются удалённые и добавленные строки input в диффе?",
     "Что делаем в первую очередь, когда прод теряет заказы прямо сейчас?"],
    code=C("""
$ kubectl logs deploy/baton-api --since=30m | grep orders | tail -4
11:16:02 INFO  POST /api/orders -> 422 (3 ms) form_fields=['comment']
11:16:02 WARN  OrderIn validation: name: Field required; phone: Field required; address: Field required
11:19:47 INFO  POST /api/orders -> 422 (2 ms) form_fields=['comment']
11:19:47 WARN  OrderIn validation: name: Field required; phone: Field required; address: Field required

$ git diff v1.8.0 v1.9.0 -- src/order-form.html
-  <input name="name" placeholder="Имя" required>
-  <input name="phone" type="tel" placeholder="Телефон" required>
-  <input name="address" placeholder="Адрес" required>
+  <label for="name">Имя</label>
+  <input id="name" required>
+  <label for="phone">Телефон</label>
+  <input id="phone" type="tel" required>
+  <label for="address">Адрес</label>
+  <input id="address" required>
   <textarea name="comment" placeholder="Комментарий"></textarea>
"""),
    language="text",
    options=[
        "У новых полей есть id, но нет name, а браузер отправляет только поля с name — до API доходит один comment",
        "Бэкенд деградировал после выкладки — перезапустить поды baton-api и посмотреть, уйдут ли 422",
        "Сразу откатить фронт на v1.8.0, чтобы заказы снова пошли, и уже потом чинить",
        "Пока чиним фронт, сделать поля OrderIn на бэкенде необязательными, чтобы заказы принимались",
        "В фиксе вернуть полям name (id оставить для label) и проверить в Network, что уходят name, phone и address",
        "Виноват required на новых полях: браузер не отдаёт такие поля без проверки — добавить форме novalidate",
    ],
    answer=[0, 2, 4],
    time_limit=15,
))

# =====================================================================
# fe-css-basics
# =====================================================================
topics.append({"topic_id": "fe-css-basics", "theory": T("""
CSS — это правила «кому → что»: селектор выбирает элементы, в фигурных скобках — свойства.

    .card__title { font-size: 1.125rem; }

Селекторы
• тег: button; класс: .btn; id: #checkout; атрибут: [type="email"];
• потомок на любой глубине: .card a; прямой ребёнок: .menu > li;
• псевдоклассы — состояния: :hover, :focus-visible (фокус с клавиатуры), :disabled, :invalid, :first-child, :not(.active);
• псевдоэлементы — части элемента: ::before, ::after, ::placeholder.

В командах стилизуют почти всегда классами, часто по BEM: .card, .card__title, .btn--primary. id в CSS — плохая привычка: он слишком «тяжёлый», и перебить его потом можно только ещё более тяжёлым селектором.

Каскад и специфичность
Если к элементу подходят несколько правил с одним свойством, победитель выбирается так:
• !important сильнее обычных объявлений;
• дальше решает специфичность — тройка (id, классы/атрибуты/псевдоклассы, теги/псевдоэлементы). #checkout button = (1,0,1), .btn-primary = (0,1,0), .card .btn:hover = (0,3,0). Сравнивают слева направо: один id сильнее любого числа классов;
• при равной специфичности побеждает правило, объявленное позже (ниже в файле или в файле, подключённом позже);
• инлайн-стиль style="…" сильнее любого селектора.
:where() обнуляет специфичность, а :is() и :not() берут её у самого «тяжёлого» аргумента.

!important — последнее средство, а не способ починки: следующему разработчику придётся ставить !important поверх твоего. Если правило «не работает», открой DevTools → Elements → Styles: зачёркнутое свойство покажет, кто его перебил, а вкладка Computed — итоговое значение.

Наследуются color, font-*, line-height; margin, padding, border и background — нет.

Блочная модель
Каждый элемент — прямоугольник: content → padding → border → margin. По умолчанию box-sizing: content-box, и width задаёт только контент:

    width: 300px; padding: 20px; border: 1px solid → на экране 342px

С box-sizing: border-box padding и border входят в width, и размеры считать проще. Поэтому почти в каждом проекте в начале стилей стоит:

    *, *::before, *::after { box-sizing: border-box; }

Схлопывание margin: вертикальные отступы соседних блоков не складываются — остаётся больший (24px и 16px → 24px). Коварнее другое: margin-top первого ребёнка «проваливается» сквозь родителя без padding и border и становится отступом самого родителя. Лечится padding у родителя или display: flow-root. Внутри flex- и grid-контейнеров margin не схлопываются, горизонтальные — никогда.

Единицы
• px — абсолютные: рамки, тени, мелкие детали;
• rem — от шрифта корня (html), а он по умолчанию берётся из настроек браузера (обычно 16px). Для шрифтов и отступов: человек увеличил шрифт — вёрстка выросла вместе с ним. Поэтому не задавай html { font-size } в px;
• em — от шрифта элемента (для font-size — от родителя), во вложенных блоках накапливается: 1.2em внутри 1.2em = 1.44;
• % — от родителя (width — от ширины контейнера);
• vw / vh — проценты от окна; для высоты экрана на мобильных лучше dvh.
""")})

tasks.append(task(
    "fe-css-basics-01", "fe-css-basics", "quiz", 1, "client",
    "Нина: «Мама читает наш сайт с телефона, у неё в настройках стоит крупный шрифт. На других сайтах текст крупный, а у нас мелкий. Почему так? Можно поправить?»",
    "Все размеры шрифта на сайте «Батона» заданы в px. Как переделать, чтобы текст следовал настройке шрифта в браузере и не рос сам собой во вложенных блоках?",
    "rem считается от размера шрифта корневого элемента html, а он по умолчанию берётся из настроек браузера. Поставил человек крупный шрифт — 1rem стал, например, 20px вместо 16, и весь текст вырос пропорционально. Но правило html { font-size: 16px } жёстко перебивает эту настройку, поэтому его нужно убрать (или задать 100%). em тоже относительные, но считаются от родителя и накапливаются: 1.2em внутри 1.2em — уже 1.44. vw зависят только от ширины окна и игнорируют настройки, pt — такая же абсолютная единица, как px (1pt = 1/72 дюйма). А медиазапроса «у пользователя крупный шрифт» в CSS нет.",
    ["Какая единица считается от настройки браузера, а не от родителя?",
     "Посмотри внимательно на правило для html."],
    code=C("""
html { font-size: 16px; }
body { font-size: 14px; }
h1 { font-size: 32px; }
.price { font-size: 18px; }
"""),
    language="css",
    options=[
        "Перевести размеры в em — они относительные и тоже учитывают настройку браузера",
        "Убрать html { font-size: 16px } и перевести размеры в rem",
        "Перевести в vw — текст будет подстраиваться под ширину экрана",
        "Перевести в pt — это типографские единицы, браузер масштабирует их сам",
        "Оставить px, но добавить медиазапрос для телефонов с увеличенным шрифтом",
    ],
    answer=1,
))

tasks.append(task(
    "fe-css-basics-02", "fe-css-basics", "find_bug", 2, "qa",
    "Ира: «Страница оплаты “Лампового Маркета”: кнопка “Оплатить” серая, а по макету оранжевая. В DevTools у .btn-primary свойство background зачёркнуто. Кэш чистила — не помогло».",
    "Почему побеждает серый фон и как исправить правильно?",
    "Браузер сначала сравнивает специфичность и только при равенстве смотрит на порядок. У #checkout button есть id — (1,0,1), и его не перебьёт никакое количество классов: button.btn.btn-primary — всего (0,2,1). !important «починит» кнопку, но начнёт войну: следующему, кому понадобится другой цвет, придётся ставить !important поверх. Правильное лечение — снизить специфичность базового правила: стилизовать кнопки классом .btn, а не через id формы. Тогда .btn и .btn-primary равны по весу, и побеждает объявленный позже. Кэш тут ни при чём (Ира его чистила, а DevTools показывает оба правила), и background прекрасно перебивает другой background — дело только в весе селекторов. Зачёркнутое свойство в DevTools всегда подскажет, какое правило его перебило.",
    ["Посчитай специфичность обоих селекторов: (id, классы, теги).",
     "Когда вообще важен порядок подключения файлов?"],
    code=C("""
/* разметка:
<form id="checkout">
  …
  <button class="btn btn-primary">Оплатить</button>
</form>
*/

/* base.css */
#checkout button {
  background: #9e9e9e;
  color: #fff;
}

/* buttons.css — подключён ПОСЛЕ base.css */
.btn-primary {
  background: #ff7a00;
}
"""),
    language="css",
    options=[
        "buttons.css подключён позже и должен побеждать — значит, браузер держит старый файл из кэша CDN, нужно сбросить кэш",
        "Добавить !important в .btn-primary — это стандартный способ поднять правило из файла, подключённого позже",
        "У #checkout button специфичность (1,0,1) выше — базовые стили кнопок перенести на класс .btn",
        "Написать button.btn.btn-primary — три части селектора перевесят #checkout button, где их всего две",
        "Сокращённое background не перебивает уже заданный фон — в .btn-primary нужно писать background-color",
    ],
    answer=2,
))

tasks.append(task(
    "fe-css-basics-03", "fe-css-basics", "code_review", 2, "teamlead",
    "Марина: «Стас жаловался, что на главной Маркета вторая промо-карточка съезжает под первую. Стажёр прислал PR с промо-блоком — посмотри его до меня. Отметь только то, что правда надо исправить перед мерджем, вкусовщину оставь при себе».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Главный баг — блочная модель: при content-box width: 50% относится только к контенту, а padding 2×24px и border 2×1px прибавляются сверху, поэтому каждая карточка шире половины, и flex-wrap переносит вторую на новую строку. Лечится box-sizing: border-box, лучше сразу глобально для *, *::before, *::after. Селекторы через #promo весят (1,1,0), и модификатор .promo__card--accent смог перебить цвет заголовка только через !important — это начало войны специфичности, поэтому стили пишут на классах. Фиксированная height: 180px выпустит текст за карточку, как только заголовок станет длиннее или человек увеличит шрифт в браузере: нужна высота по содержимому или min-height. А вот width у flex-элемента прекрасно работает (при flex-basis: auto он и задаёт базовый размер), и порядок свойств внутри правила на расчёт ширины не влияет. CSS-переменные для цветов — хорошая идея на будущее, но не блокер, а flexbox давно поддерживают все актуальные браузеры, и float для раскладки — шаг назад.",
    ["Посчитай ширину карточки: 50% + padding + border — сколько выходит?",
     "Зачем в правиле для акцентной карточки понадобился !important?",
     "Что будет с карточкой высотой 180px, если заголовок займёт три строки?"],
    code=C("""
/* PR #214: промо-блок на главной — две карточки в ряд */
#promo {
  display: flex;
  flex-wrap: wrap;
}

#promo .promo__card {
  width: 50%;
  height: 180px;
  padding: 24px;
  border: 1px solid #e0e0e0;
}

#promo .promo__title {
  font-size: 1.375rem;
  color: #212121;
}

.promo__card--accent .promo__title {
  color: #ff7a00 !important;
}
"""),
    language="css",
    options=[
        "При content-box к width: 50% прибавляются padding и border, карточка шире половины — нужен box-sizing: border-box",
        "Селекторы через #promo слишком тяжёлые: из-за них акцентной карточке уже понадобился !important — перейти на классы",
        "height: 180px фиксирует высоту: длинный заголовок или крупный шрифт в браузере вылезут за карточку — лучше min-height",
        "У flex-элементов width игнорируется, поэтому карточки и переносятся — заменить width: 50% на flex-basis: 50%",
        "Цвета #e0e0e0, #212121 и #ff7a00 вынести в CSS-переменные — без дизайн-токенов такой PR мерджить нельзя",
        "padding нужно объявить раньше width: браузер применяет свойства по порядку и сейчас считает ширину без отступов",
        "Flexbox ненадёжен в старых Safari — две карточки проще и безопаснее разложить через float: left с clearfix",
    ],
    answer=[0, 1, 2],
))

tasks.append(task(
    "fe-css-basics-04", "fe-css-basics", "write_code", 3, "teamlead",
    "Марина: «Сверстай стили карточки товара для Маркета. Правила у нас простые: только классы, никаких id и !important, шрифты в rem, у кнопки обязательно есть наведение, видимый фокус с клавиатуры и disabled — Ира всё это проверяет».",
    "Допиши стили:\n"
    "• для всех элементов (и ::before/::after) — box-sizing: border-box;\n"
    "• .card — padding 16px, рамка 1px solid #e0e0e0, скругление 8px;\n"
    "• .card__title — font-size 1.125rem;\n"
    "• .btn — фон #ff7a00, белый текст, без рамки (border: none), font-size 1rem, cursor: pointer;\n"
    "• .btn:hover — фон #e56d00;\n"
    "• .btn:focus-visible — заметная обводка outline (например, 3px solid #1a73e8);\n"
    "• .btn:disabled — фон #bdbdbd и cursor: not-allowed.\n"
    "Без id-селекторов и !important.",
    "Глобальный border-box избавляет от пересчёта ширины при каждом padding. Селекторы только по классам держат специфичность низкой и одинаковой, поэтому состояние или модификатор легко перебивает базовое правило без !important. :hover, :focus-visible и :disabled — псевдоклассы состояний. :focus-visible показывает обводку при навигации с клавиатуры и не мешает при клике мышкой; просто убирать outline нельзя, иначе человек без мыши не увидит, где он находится. Шрифт в rem растёт вместе с настройкой браузера, а серый фон и cursor: not-allowed честно показывают, что кнопку нажать нельзя.",
    ["Селектор для всех элементов — *, для псевдоэлементов — *::before и *::after.",
     "Состояния пишутся через двоеточие: .btn:hover { … }.",
     "Обводка фокуса: .btn:focus-visible { outline: 3px solid #1a73e8; }"],
    code=C("""
/* Карточка товара «Лампового Маркета» */

.card {
}

.btn {
}
"""),
    language="css",
    answer=C("""
/* Карточка товара «Лампового Маркета» */

*,
*::before,
*::after {
  box-sizing: border-box;
}

.card {
  padding: 16px;
  border: 1px solid #e0e0e0;
  border-radius: 8px;
}

.card__title {
  font-size: 1.125rem;
}

.btn {
  padding: 0.5rem 1rem;
  border: none;
  background: #ff7a00;
  color: #fff;
  font-size: 1rem;
  cursor: pointer;
}

.btn:hover {
  background: #e56d00;
}

.btn:focus-visible {
  outline: 3px solid #1a73e8;
  outline-offset: 2px;
}

.btn:disabled {
  background: #bdbdbd;
  cursor: not-allowed;
}
"""),
    tests=[
        chk("Для всех элементов — box-sizing: border-box",
            r"(^|,)\s*\*\s*(,[^{}]*)?\{[^}]*box-sizing\s*:\s*(border-box|inherit)", "im"),
        chk(".card: padding 16px, рамка 1px solid #e0e0e0, скругление 8px",
            r"\.card\s*\{(?=[^}]*padding\s*:\s*(16px|1rem)\s*[;}])(?=[^}]*border\s*:\s*1px\s+solid\s+#e0e0e0)(?=[^}]*border-radius\s*:\s*(8px|0?\.5rem))"),
        chk(".card__title — font-size: 1.125rem", r"\.card__title\s*\{[^}]*font-size\s*:\s*1\.125rem"),
        chk(".btn: фон #ff7a00, белый текст, без рамки, 1rem, pointer",
            r"\.btn\s*\{(?=[^}]*background(-color)?\s*:\s*#ff7a00)(?=([^}]*[\s;])?color\s*:\s*(#fff\b|#ffffff\b|white\b))(?=[^}]*border\s*:\s*(none|0)\s*[;}])(?=[^}]*font-size\s*:\s*1rem)(?=[^}]*cursor\s*:\s*pointer)"),
        chk(".btn:hover — фон #e56d00", r"\.btn:hover\s*\{[^}]*background(-color)?\s*:\s*#e56d00"),
        chk(".btn:focus-visible — видимая обводка outline",
            r"\.btn:focus-visible\s*\{[^}]*outline\s*:(?!\s*(none|0)\s*[;}])[^;}]*\w"),
        chk(".btn:disabled — фон #bdbdbd и cursor: not-allowed",
            r"\.btn(:disabled|\[disabled\])\s*\{(?=[^}]*background(-color)?\s*:\s*#bdbdbd)(?=[^}]*cursor\s*:\s*not-allowed)"),
        chk("Нет !important", not_regex=r"!\s*important"),
        chk("Нет id-селекторов", not_regex=r"(^|\})[^{}]*#(?![0-9a-f]{3,8}\b)[a-z_][\w-]*[^{}]*\{"),
    ],
))

tasks.append(task(
    "fe-css-basics-05", "fe-css-basics", "find_bug", 3, "qa",
    "Ира: «Страница доставки “Батона”: у зелёной плашки с акцией заголовок прилип к верхнему краю, а сама плашка отъехала от меню на лишние 24px. Во всех браузерах одинаково, воспроизводится на любом экране».",
    "Почему отступ заголовка оказался снаружи плашки и как это исправить?",
    "Вертикальные margin родителя и его первого ребёнка схлопываются, если между ними нет ничего «твёрдого»: padding, border или нового контекста форматирования. Тогда margin-top: 24px заголовка становится отступом всей плашки: она уезжает вниз, а заголовок внутри остаётся прижатым к её верхнему краю. Правильно, когда внутренние отступы задаёт сам контейнер через padding, а у заголовка margin-top: 0. display: flow-root создаёт новый блочный контекст и тоже останавливает схлопывание. Отрицательный margin лишь маскирует проблему и сломается при первой правке, а стили браузера для h2 всегда проигрывают классу — тег тут ни при чём. border-radius только скругляет углы и ничего не сдвигает. Внутри flex- и grid-контейнеров margin не схлопываются вовсе — ещё одна причина, почему карточки часто верстают на них.",
    ["Выдели плашку в DevTools: где на самом деле нарисован оранжевый отступ 24px?",
     "Что стоит между верхней границей плашки и заголовком — padding, border?"],
    code=C("""
/* разметка:
<nav class="menu">…</nav>
<section class="promo">
  <h2 class="promo__title">Бесплатная доставка от 1500 ₽</h2>
  <p>Только по выходным</p>
</section>
*/

.promo {
  background: #e8f5e9;
  border-radius: 12px;
}

.promo__title {
  margin-top: 24px;
}
"""),
    language="css",
    options=[
        "Браузерный стиль h2 перебивает margin-top из класса — заменить заголовок на div или сбросить стили h2 через reset",
        "Схлопывание margin: у .promo нет padding и border, и отступ заголовка уходит наружу — задать плашке padding",
        "Фон секции не распространяется на margin детей — достаточно задать .promo__title такой же фон, как у плашки",
        "Компенсировать лишний отступ: добавить .promo { margin-top: -24px }, а у заголовка оставить margin как есть",
        "border-radius резервирует место под скругление и сдвигает содержимое вниз — уменьшить радиус плашки до 4px",
    ],
    answer=1,
))

# =====================================================================
# fe-css-layout
# =====================================================================
topics.append({"topic_id": "fe-css-layout", "theory": T("""
Flexbox — раскладка в одном направлении: шапка, строка кнопок, элемент списка. Grid — в двух: каталог карточек, каркас страницы.

Flexbox
display: flex ставится на КОНТЕЙНЕР, а раскладываются его прямые дети — flex-элементы.

    .header { display: flex; justify-content: space-between; align-items: center; gap: 16px; }

• flex-direction: row (по умолчанию) или column задаёт главную ось;
• justify-content — выравнивание по главной оси: flex-start, center, space-between;
• align-items — по поперечной: stretch (по умолчанию), center, flex-start;
• gap — расстояние между элементами без margin-хаков;
• flex-wrap: wrap — переносить элементы на новую строку;
• flex: 1 у элемента — «займи оставшееся место», flex-shrink: 0 — «не сжимайся» (иконки, цены);
• margin-left: auto (или margin-top: auto в колонке) отталкивает элемент к краю.

Подвох: у flex-элемента min-width: auto — он не сжимается уже своего содержимого. Длинное слово или строка с white-space: nowrap вылезают за экран, и text-overflow: ellipsis не срабатывает. Лечится min-width: 0 у flex-элемента.

Grid

    .catalog { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; }

• fr — доля свободного места: repeat(3, 1fr) — три равные колонки;
• repeat(auto-fill, minmax(220px, 1fr)) — столько колонок, сколько влезет, но не уже 220px;
• grid-column: span 2 — элемент на две колонки;
• grid-template-areas — каркас страницы «картинкой» из имён областей.
Элементы одной строки грида по умолчанию растягиваются на одну высоту. Сделай карточку flex-колонкой, дай кнопке margin-top: auto — и все кнопки встанут в ряд.

Адаптивность
Без этой строки в head мобильный браузер отрисует страницу шириной около 980px и уменьшит её, а медиазапросы для узких экранов не сработают — браузер считает, что ширина 980px:

    <meta name="viewport" content="width=device-width, initial-scale=1">

Mobile-first: базовые стили пишешь для узкого экрана, а усложняешь через min-width:

    .catalog { display: grid; gap: 16px; }
    @media (min-width: 600px) { .catalog { grid-template-columns: repeat(2, 1fr); } }

Так телефон не разбирает и не перебивает десктопные правила, а код читается «от простого к сложному». Точки перелома выбирай по контенту — там, где вёрстка начинает ломаться, а не по моделям телефонов. Проверяй в DevTools → Device Toolbar и на настоящем телефоне.

Адаптивные картинки
• img { max-width: 100%; height: auto; } — картинка не вылезет из контейнера;
• srcset + sizes — браузер сам выберет файл под ширину экрана и плотность пикселей:

    <img src="bread-800.webp"
         srcset="bread-400.webp 400w, bread-800.webp 800w, bread-1600.webp 1600w"
         sizes="(min-width: 1024px) 25vw, 100vw"
         width="800" height="533" alt="Бородинский хлеб" loading="lazy">

• width и height резервируют место, и страница не прыгает при загрузке;
• loading="lazy" — только для картинок ниже первого экрана;
• picture с source — когда нужны разные форматы (AVIF, WebP) или другой кадр на мобильном.
""")})

tasks.append(task(
    "fe-css-layout-01", "fe-css-layout", "write_code", 1, "client",
    "Артур: «В шапке сайта “Жми” логотип должен быть слева, меню справа, а они стоят друг под другом. Прошлый подрядчик уверял, что всё настроил, какой-то space-between прописал. Завтра запускаем рекламу — почините, пожалуйста, сегодня».",
    "Исправь стили шапки: логотип слева, меню справа, оба выровнены по центру по вертикали. Flex-раскладку (display: flex, justify-content: space-between, align-items: center) задай контейнеру .header, у .logo и .nav её убери. padding шапки оставь.",
    "Flexbox всегда про пару «контейнер — дети»: display: flex меняет раскладку прямых потомков элемента, а не его самого. В исходном коде flex-контейнерами стали .logo и .nav — они раскладывали своё содержимое, а сами остались блочными и встали друг под другом. Когда display: flex, justify-content: space-between и align-items: center стоят на .header, логотип и меню становятся flex-элементами одной строки: space-between прижимает первый к левому краю, последний — к правому, а align-items: center выравнивает их по вертикали. flex-direction: row указывать не нужно — это значение по умолчанию. Запомни вопрос, который решает половину багов с флексом: «кто здесь контейнер, а кто — его дети?»",
    ["Кто родитель у .logo и .nav? Именно ему нужен display: flex.",
     "justify-content и align-items управляют детьми того элемента, у которого стоит display: flex.",
     ".header { display: flex; justify-content: space-between; align-items: center; padding: 16px; }"],
    code=C("""
/* разметка:
<header class="header">
  <a class="logo" href="/">Жми</a>
  <nav class="nav">…</nav>
</header>
*/

.header {
  padding: 16px;
}

.logo,
.nav {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
"""),
    language="css",
    answer=C("""
/* разметка:
<header class="header">
  <a class="logo" href="/">Жми</a>
  <nav class="nav">…</nav>
</header>
*/

.header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px;
}
"""),
    tests=[
        chk(".header — flex-контейнер (display: flex)", r"\.header\s*\{[^}]*display\s*:\s*flex\s*[;}]"),
        chk(".header — justify-content: space-between", r"\.header\s*\{[^}]*justify-content\s*:\s*space-between"),
        chk(".header — align-items: center", r"\.header\s*\{[^}]*align-items\s*:\s*center"),
        chk("padding шапки на месте", r"\.header\s*\{[^}]*padding\s*:\s*(16px|1rem)"),
        chk("У .logo и .nav больше нет justify-content: space-between",
            not_regex=r"(^|[},])\s*\.(logo|nav)\b[^{}]*\{[^}]*justify-content\s*:\s*space-between"),
    ],
))

tasks.append(task(
    "fe-css-layout-02", "fe-css-layout", "write_code", 2, "manager",
    "Стас: «Каталог Маркета на телефоне — каша, а на широком мониторе — три огромные карточки. Хочу: телефон — одна колонка, планшет — две, десктоп — четыре. И кнопки “В корзину” в одном ряду, даже если названия разной длины».",
    "Перепиши стили mobile-first:\n"
    "• .catalog — grid, по умолчанию одна колонка, gap 16px;\n"
    "• от 600px — две равные колонки, от 1024px — четыре (медиазапросы только с min-width, колонки через fr);\n"
    "• .card — flex-колонка (display: flex; flex-direction: column);\n"
    "• .card__buy — прижата к низу карточки через margin-top: auto.",
    "Mobile-first значит: базовые стили — для самого узкого экрана, а медиазапросы с min-width добавляют колонки по мере роста ширины. Грид здесь удобнее флекса: колонки задаются одной строкой, gap работает без хаков с margin, а ширину карточек не нужно считать в процентах. 1fr — доля свободного места, поэтому repeat(4, 1fr) делит ширину на четыре равные колонки уже за вычетом gap. Элементы одной строки грида по умолчанию растягиваются на одну высоту, а margin-top: auto во flex-колонке забирает всё свободное место над кнопкой — и кнопки выстраиваются по нижнему краю. Если в карточках бывают очень длинные слова, пиши repeat(4, minmax(0, 1fr)) — так колонка не раздуется из-за содержимого.",
    ["Начни с .catalog { display: grid; gap: 16px; } — одна колонка получится сама.",
     "@media (min-width: 600px) { .catalog { grid-template-columns: repeat(2, 1fr); } }",
     "Кнопку к низу прижимает margin-top: auto внутри flex-колонки."],
    code=C("""
.catalog {
  display: flex;
}

.card {
  width: 33%;
}

@media (max-width: 600px) {
  .card {
    width: 100%;
  }
}
"""),
    language="css",
    answer=C("""
.catalog {
  display: grid;
  grid-template-columns: 1fr;
  gap: 16px;
}

@media (min-width: 600px) {
  .catalog {
    grid-template-columns: repeat(2, 1fr);
  }
}

@media (min-width: 1024px) {
  .catalog {
    grid-template-columns: repeat(4, 1fr);
  }
}

.card {
  display: flex;
  flex-direction: column;
}

.card__buy {
  margin-top: auto;
}
"""),
    tests=[
        chk(".catalog — display: grid и gap 16px",
            r"\.catalog\s*\{(?=[^}]*display\s*:\s*grid)(?=[^}]*gap\s*:\s*(16px|1rem))"),
        chk("От 600px (min-width) — две равные колонки",
            r"@media[^{]*(min-width\s*:\s*600px|width\s*>=\s*600px)[^{]*\{[^@]*?\.catalog\s*\{[^}]*grid-template-columns\s*:\s*(repeat\(\s*2\s*,\s*(1fr|minmax\(\s*0(px)?\s*,\s*1fr\s*\))\s*\)|1fr\s+1fr\s*[;}])"),
        chk("От 1024px (min-width) — четыре равные колонки",
            r"@media[^{]*(min-width\s*:\s*1024px|width\s*>=\s*1024px)[^{]*\{[^@]*?\.catalog\s*\{[^}]*grid-template-columns\s*:\s*(repeat\(\s*4\s*,\s*(1fr|minmax\(\s*0(px)?\s*,\s*1fr\s*\))\s*\)|1fr\s+1fr\s+1fr\s+1fr\s*[;}])"),
        chk("Mobile-first: нет медиазапросов с max-width", not_regex=r"@media[^{]*(max-width|width\s*<)"),
        chk(".card — flex-колонка",
            r"\.card\s*\{(?=[^}]*display\s*:\s*flex)(?=[^}]*flex-(direction|flow)\s*:\s*column)"),
        chk(".card__buy прижата к низу: margin-top: auto", r"\.card__buy\s*\{[^}]*margin-top\s*:\s*auto"),
    ],
))

tasks.append(task(
    "fe-css-layout-03", "fe-css-layout", "architecture", 2, "client",
    "Нина: «Фотограф снял нам хлеб — фотки чудесные, по 4–5 МБ. На главной их двенадцать. Покупатели жалуются, что с телефона в метро сайт открывается минуту. Сделайте, чтобы было и красиво, и быстро».",
    "Гена прогнал главную через Lighthouse. Какое решение выбрать?",
    "CSS-ширина меняет только отображение: телефон всё равно скачает 4,6 МБ, чтобы показать картинку в 358px, а loading=\"lazy\" лишь откладывает эту загрузку. Одна версия на 800px уже лучше, но телефону это лишние килобайты, а на широком ретина-экране фото будет мыльным. srcset с набором ширин и sizes отдают выбор браузеру: он знает ширину экрана и плотность пикселей и качает ровно нужный файл, а WebP/AVIF весят в разы меньше JPEG. width и height резервируют место до загрузки и убирают скачки вёрстки (CLS), а loading=\"lazy\" откладывает фото, до которых человек ещё не долистал (первый экран лениво грузить нельзя — ухудшится LCP). Подстановка src из JS ждёт загрузки скрипта и мешает браузеру начать качать картинки заранее, а background-image лишает фото alt и семантики — для контентных картинок это не подходит.",
    ["Посмотри на «показано 358×239»: сколько пикселей реально нужно телефону?",
     "Какой механизм HTML позволяет браузеру самому выбрать файл под экран?"],
    code=C("""
Lighthouse — главная сайта «Батон» (эмуляция: Moto G Power, медленный 4G)
Performance: 31
LCP (Largest Contentful Paint): 9,8 s
CLS (Cumulative Layout Shift): 0,31

Properly size images — возможная экономия 44 МБ
  /img/borodinsky.jpg   4,6 МБ   4000×2667, показано 358×239
  /img/baguette.jpg     4,1 МБ   4000×2667, показано 358×239
  … и ещё 10 файлов
Image elements do not have explicit width and height
"""),
    language="text",
    options=[
        "Оставить оригиналы, но задать img { width: 100% } и loading=\"lazy\" — браузер уменьшит фото и скачает его, когда дойдёт",
        "Пережать все фото в одну версию 800px (JPEG, качество 70) — компромисс между телефоном и ретиной без лишней разметки",
        "Нарезать фото в несколько ширин в WebP/AVIF, отдать через srcset + sizes с width и height, ниже первого экрана — lazy",
        "Подставлять src из JavaScript: при загрузке смотреть ширину экрана и devicePixelRatio и выбирать нужный файл",
        "Перенести фото в CSS background-image с image-set() и переключать версии медиазапросами под ширину экрана",
    ],
    answer=2,
))

tasks.append(task(
    "fe-css-layout-04", "fe-css-layout", "estimation", 3, "manager",
    "Стас: «Нина хочет, чтобы сайт “Батона” нормально выглядел на телефонах — сейчас там десктопная вёрстка 2019 года, на мобилке всё мелкое и едет вбок. Сколько тебе нужно? Скажи сегодня, клиент ждёт цифру».",
    "Прежде чем называть срок, выбери вопросы, которые нужно обязательно выяснить.",
    "Срок адаптива определяют объём, дизайн и сложные блоки. Страниц может быть 30, а уникальных шаблонов — 5, и считать нужно шаблоны. Без макета половина времени уйдёт на придумывание и согласование с Ниной. Таблицы, слайдеры и карты на узком экране приходится переделывать, а не «сжимать». Аналитика по устройствам говорит, на каких экранах и браузерах тестировать и нужны ли старые Safari. Цвет кнопки и число товаров объём не меняют (товары выводит один и тот же шаблон карточки), а переписывание на React — отдельный проект, а не адаптив: Tailwind даёт удобные медиазапросы, но вёрстку под телефон всё равно придётся делать руками. Разумная оценка: день на аудит, meta viewport и базовую сетку, по полдня–дню на шаблон и день на сложные блоки и проверку на реальных телефонах — для 5 шаблонов с готовым макетом это 4–6 рабочих дней, без макета плюс 2–3 дня.",
    ["Какие ответы меняют объём работы в разы?",
     "Подумай про дизайн, количество страниц и самые неудобные блоки."],
    options=[
        "Есть ли макет мобильной версии от дизайнера или адаптив придумываю я сам?",
        "Сколько на сайте уникальных шаблонов страниц: главная, каталог, корзина, контакты?",
        "С каких устройств и браузеров заходят покупатели — есть ли аналитика, нужны ли старые iPhone?",
        "Какого цвета будет кнопка «В корзину» на мобильных — оставляем оранжевую или дизайнер предложит новую?",
        "Что делать на узком экране со сложными блоками — таблицей цен, слайдером, картой зон доставки?",
        "Можно ли заодно переписать сайт на React с Tailwind — тогда адаптив получится «из коробки»?",
        "Сколько товаров в каталоге и как часто Нина добавляет новые позиции?",
    ],
    answer=[0, 1, 2, 4],
    time_limit=10,
))

tasks.append(task(
    "fe-css-layout-05", "fe-css-layout", "find_bug", 3, "qa",
    "Ира: «Корзина Маркета на iPhone SE (375px). Шаги: добавить “Настольную лампу Lumen Pro X2 с беспроводной зарядкой и тремя режимами”. Ожидаю: название обрезается многоточием. Факт: цена и крестик уехали за правый край, появился горизонтальный скролл».",
    "Почему многоточие не срабатывает и строка вылезает за экран? Как исправить?",
    "У flex-элементов по умолчанию min-width: auto — они не могут стать уже своего минимального содержимого. Для строки с white-space: nowrap минимум — вся её длина, поэтому .item__info распирает ряд, а overflow у заголовка так и не начинает обрезать текст. min-width: 0 разрешает блоку сжаться до выделенного места: у .item__title появляется граница, и text-overflow: ellipsis срабатывает. flex-shrink: 0 у цены и кнопки — полезная добавка, чтобы их не сплющило, но переполнение она не лечит: .item__info всё равно шире экрана. flex-wrap унесёт цену вниз, а длинное название так и останется шире экрана. width: 100% вместо flex: 1 ничего не даст — минимальная ширина всё равно auto, а text-overflow работает только у блочных элементов, так что замена p на строчный span сделает хуже. Этот баг встречается почти в каждом списке с длинными названиями — запомни min-width: 0.",
    ["Какой минимальной ширины может быть flex-элемент по умолчанию?",
     "Обрезать текст можно, только если блок уже своего содержимого. Что мешает .item__info сжаться?"],
    code=C("""
/* разметка:
<li class="item">
  <img class="item__img" src="/img/lumen-x2.webp" width="56" height="56" alt="">
  <div class="item__info">
    <p class="item__title">Настольная лампа Lumen Pro X2 с беспроводной зарядкой и тремя режимами</p>
  </div>
  <span class="item__price">4 990 ₽</span>
  <button class="item__remove" type="button" aria-label="Удалить">✕</button>
</li>
*/

.item {
  display: flex;
  align-items: center;
  gap: 12px;
}

.item__info {
  flex: 1;
}

.item__title {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
"""),
    language="css",
    options=[
        "text-overflow: ellipsis не работает у p внутри flex-ряда — заменить p на span с display: inline",
        "flex: 1 заставляет .item__info расти без ограничений — заменить его на width: 100%, чтобы блок не выходил за ряд",
        "У flex-элемента по умолчанию min-width: auto — задать .item__info { min-width: 0 }, и многоточие появится",
        "Добавить .item { flex-wrap: wrap } — цена и крестик перенесутся на новую строку, и скролл пропадёт",
        "Цене и крестику задать flex-shrink: 0 — сейчас их сплющивает, и они выталкивают строку за край экрана",
    ],
    answer=2,
))

# =====================================================================
# fe-dom
# =====================================================================
topics.append({"topic_id": "fe-dom", "theory": T("""
DOM — дерево объектов, которое браузер строит из HTML. JS читает и меняет это дерево, а браузер перерисовывает страницу.

Найти элементы

    const form = document.querySelector('#trial-form');  // первый подходящий или null
    const buttons = document.querySelectorAll('.book');  // статичный NodeList, есть forEach

Ошибка «Cannot read properties of null (reading 'addEventListener')» значит, что элемент не нашёлся. Частая причина — скрипт в head выполнился раньше, чем браузер разобрал body. Лечится <script src="app.js" defer> (выполнится после разбора HTML) или type="module" — модули отложены по умолчанию.

Менять элементы
• el.textContent = 'Вы записаны' — вставляет ТЕКСТ, теги не разбираются. Для любых данных от пользователя и API;
• el.innerHTML = '<b>…</b>' — разбирает строку как HTML. Чужие данные без экранирования = XSS. Старое содержимое вместе с обработчиками уничтожается;
• el.classList.add / remove / toggle('active'); el.dataset.id — атрибут data-id (всегда строка);
• document.createElement('li'), parent.append(el), el.remove().

Собираешь разметку строкой — экранируй данные, амперсанд первым:

    const escapeHtml = (s) => String(s).replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;').replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;').replaceAll("'", '&#39;');

События

    button.addEventListener('click', onClick);  // передаём функцию, а не onClick()

• e.target — элемент, где событие возникло (может быть иконка внутри кнопки);
• e.currentTarget — элемент, на котором висит обработчик;
• e.preventDefault() — отменить действие браузера: отправку формы с перезагрузкой, переход по ссылке;
• e.stopPropagation() — остановить всплытие. Это другое, и нужно оно редко.

Всплытие: событие срабатывает на target, потом на родителе, на его родителе — и так до document. Поэтому клик по кнопке внутри списка услышит и обработчик на списке. Всплывают click, input, submit, keydown; focus и blur — нет (всплывающие аналоги — focusin и focusout).

Делегирование — один обработчик на контейнере вместо сотни на кнопках:

    list.addEventListener('click', (e) => {
      const btn = e.target.closest('[data-action]');
      if (!btn || !list.contains(btn)) return;
      const id = btn.closest('[data-id]').dataset.id;
      if (btn.dataset.action === 'add') addToCart(id);
    });

Работает и для элементов, добавленных позже. closest() идёт от элемента вверх, поэтому клик по иконке внутри кнопки тоже найдёт кнопку.

Формы: слушай submit на form, а не click на кнопке — submit сработает и по кнопке, и по Enter, и только после встроенной валидации. Первой строкой — e.preventDefault(), данные — через new FormData(form).

Частые ошибки
• addEventListener('click', handler()) — функция вызвалась сразу;
• return false в addEventListener ничего не отменяет;
• обработчики на элементах, которые потом пересоздаются через innerHTML;
• innerHTML += в цикле — каждый раз заново разбирает весь список;
• e.target вместо closest() при делегировании.
""")})

tasks.append(task(
    "fe-dom-01", "fe-dom", "quiz", 1, "teamlead",
    "Гена: «Прежде чем пускать тебя в корзину Маркета, проверим, как ты понимаешь события. Не запуская код — что будет в консоли после клика по кнопке “Удалить”?»",
    "Пользователь кликнул по кнопке «Удалить». Что появится в консоли и в каком порядке?",
    "Событие click сначала срабатывает на элементе, по которому кликнули, — на кнопке, поэтому первым выводится button. Потом оно всплывает вверх по родителям: li, затем ul#cart, где висит второй обработчик. e.target — элемент, где событие возникло (кнопка с классом remove), а e.currentTarget — элемент, на котором висит текущий обработчик (#cart). После первого обработчика событие не останавливается: прервать всплытие можно только явным e.stopPropagation(). На всплытии и e.target построено делегирование событий.",
    ["Событие идёт от кнопки вверх к родителям или сверху вниз?",
     "e.target — где кликнули, e.currentTarget — где слушают."],
    code=C("""
// <ul id="cart">
//   <li class="item" data-id="7">
//     Лампа «Эдисон» <button class="remove">Удалить</button>
//   </li>
// </ul>
const cart = document.querySelector('#cart');
const removeBtn = document.querySelector('.remove');

cart.addEventListener('click', (e) => {
  console.log('cart:', e.target.className, e.currentTarget.id);
});

removeBtn.addEventListener('click', () => {
  console.log('button');
});
"""),
    language="javascript",
    options=[
        "cart: remove cart, затем button",
        "button, затем cart: remove cart",
        "button, затем cart: item cart",
        "Только button — событие обработано и дальше не идёт",
        "button, затем cart: remove remove",
    ],
    answer=1,
))

tasks.append(task(
    "fe-dom-02", "fe-dom", "find_bug", 2, "qa",
    "Ира: «Форма записи “Жми”. Шаги: заполнить имя и телефон, нажать “Записаться”. Ожидаю: надпись “Вы записаны!”. Факт: страница мигает и перезагружается, в Network запрос к /api/trial со статусом (canceled), заявки в админке нет. Консоль пустая».",
    "Почему заявка не доходит и как это исправить?",
    "У submit есть действие по умолчанию: браузер сам отправляет форму и загружает новую страницу. Обработчик успевает начать fetch, но тут же начинается перезагрузка, и браузер отменяет незавершённый запрос — отсюда (canceled) в Network. e.preventDefault() отменяет именно действие по умолчанию, и дальше страницей управляет наш код. stopPropagation останавливает всплытие, а не отправку, — это частая путаница. return false отменяет действие только в старых обработчиках вида onsubmit=\"…\" и в jQuery, а в addEventListener игнорируется. Переход на click тоже не поможет: клик по submit-кнопке всё равно отправляет форму, а срабатывает ещё до встроенной валидации. async/await не спасёт: браузер не ждёт промис из обработчика и начинает отправку сразу после его синхронной части.",
    ["Что браузер делает с формой по умолчанию, когда её отправляют?",
     "stopPropagation и preventDefault — про разные вещи. Какая из них про действие браузера?"],
    code=C("""
const form = document.querySelector('#trial-form');
const message = document.querySelector('#message');

form.addEventListener('submit', (e) => {
  const data = Object.fromEntries(new FormData(form));

  fetch('/api/trial', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(data),
  }).then(() => {
    message.textContent = 'Вы записаны!';
  });
});
"""),
    language="javascript",
    options=[
        "Вызвать e.stopPropagation(): submit всплывает до document, и уже он перезагружает страницу",
        "Добавить return false в конце обработчика — это отменяет и отправку формы, и всплытие события",
        "Слушать click на кнопке «Записаться» вместо submit — клик срабатывает раньше, чем браузер отправит форму",
        "Вызвать e.preventDefault() в начале обработчика: иначе форма уходит с перезагрузкой, и fetch отменяется",
        "Сделать обработчик async и написать await fetch — тогда браузер дождётся ответа и только потом перезагрузит",
    ],
    answer=3,
))

tasks.append(task(
    "fe-dom-03", "fe-dom", "write_code", 2, "teamlead",
    "Марина: «В “Ламповом Маркете” отзывы собираются строкой и вставляются через innerHTML. Вчера кто-то оставил отзыв с тегом <b> — полстраницы стало жирным. Хорошо, что не со скриптом. Сделай нормальную сборку разметки с экранированием, покроем её тестами».",
    "Напиши escapeHtml(value) и renderReviews(reviews). reviews — массив объектов {author, text, rating}. renderReviews возвращает строку:\n"
    "• для пустого массива — <p class=\"empty\">Отзывов пока нет</p>;\n"
    "• иначе <ul class=\"reviews\">, внутри на каждый отзыв <li data-rating=\"RATING\"><b>AUTHOR</b>: TEXT</li>, затем </ul> — без пробелов и переносов между тегами.\n"
    "Все подставляемые значения (включая rating) пропускай через escapeHtml: & → &amp;, < → &lt;, > → &gt;, \" → &quot;, ' → &#39;.",
    "Когда строка уходит в innerHTML, браузер разбирает её как HTML, поэтому данные от пользователей сначала экранируют: < и > превращаются в &lt; и &gt;, и тег становится просто текстом. Амперсанд заменяют первым, иначе испортишь уже сделанные замены: &lt; превратится в &amp;lt;. Кавычки важны для атрибутов: без &quot; значение rating могло бы «закрыть» data-rating и дописать свой атрибут-обработчик вроде onmouseover. String(value) нужен, потому что rating приходит числом, а у чисел нет replaceAll. Если разметку не обязательно собирать строкой, ещё надёжнее создавать элементы через createElement и заполнять их через textContent.",
    ["Начни с escapeHtml: String(value) и цепочка replaceAll, & — первым.",
     "Для списка: reviews.map(...) даёт массив строк li, дальше join('').",
     "Не забудь пустой массив — он возвращает абзац, а не пустой ul."],
    code=C("""
function escapeHtml(value) {
  // твой код
}

function renderReviews(reviews) {
  // твой код
}
"""),
    language="javascript",
    entry="renderReviews",
    answer=C("""
function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function renderReviews(reviews) {
  if (reviews.length === 0) {
    return '<p class="empty">Отзывов пока нет</p>';
  }
  const items = reviews.map(
    (r) =>
      `<li data-rating="${escapeHtml(r.rating)}"><b>${escapeHtml(r.author)}</b>: ${escapeHtml(r.text)}</li>`
  );
  return `<ul class="reviews">${items.join('')}</ul>`;
}
"""),
    tests=[
        {"input": [[]], "expected": '<p class="empty">Отзывов пока нет</p>'},
        {"input": [[{"author": "Ира", "text": "Отличная лампа", "rating": 5}]],
         "expected": '<ul class="reviews"><li data-rating="5"><b>Ира</b>: Отличная лампа</li></ul>'},
        {"input": [[{"author": "<script>alert(1)</script>", "text": "Тёплый свет & мягкий <b>", "rating": 4}]],
         "expected": '<ul class="reviews"><li data-rating="4"><b>&lt;script&gt;alert(1)&lt;/script&gt;</b>: Тёплый свет &amp; мягкий &lt;b&gt;</li></ul>'},
        {"input": [[{"author": 'Олег "Лампочкин"', "text": "It's ok", "rating": 3},
                    {"author": "Аня", "text": "a &lt; b", "rating": '5" onmouseover="alert(1)'}]],
         "expected": '<ul class="reviews"><li data-rating="3"><b>Олег &quot;Лампочкин&quot;</b>: It&#39;s ok</li>'
                     '<li data-rating="5&quot; onmouseover=&quot;alert(1)"><b>Аня</b>: a &amp;lt; b</li></ul>'},
    ],
))

tasks.append(task(
    "fe-dom-04", "fe-dom", "find_bug", 2, "qa",
    "Ира: «Каталог Маркета: кнопка “В корзину” срабатывает через раз. Нажимаю на текст — товар добавляется, на иконку тележки — ничего. С сердечком у кнопки “В избранное” то же самое».",
    "Клик слушает весь список (делегирование), а решение, что делать, принимает чистая функция resolveAction(path). path — цепочка элементов от e.target вверх до списка, как в e.composedPath(); у элемента есть tag и dataset. Функция должна вернуть { action, id } для ближайшей кнопки с data-action (id — число из data-id карточки) или null, если клик был не по кнопке. Найди и исправь ошибку.",
    "e.target — самый глубокий элемент под курсором: при клике по иконке это svg или даже path внутри него, а не кнопка. Код смотрел только на path[0], поэтому всё, что вложено в кнопку, «проглатывалось». Правильно идти вверх по цепочке и брать первый элемент с data-action — ровно это делает e.target.closest('[data-action]') в реальном обработчике. В браузере это выглядит так: const btn = e.target.closest('[data-action]'); if (!btn || !list.contains(btn)) return; Вынести такое решение в чистую функцию — хороший приём: её можно покрыть тестами без браузера.",
    ["Чем окажется path[0], если кликнуть по иконке внутри кнопки?",
     "Ищи кнопку по всей цепочке: path.find(...).",
     "Это аналог e.target.closest('[data-action]')."],
    code=C("""
// Разметка карточки:
// <li data-id="42">
//   <button data-action="add"><svg class="icon">…</svg> В корзину</button>
//   <button data-action="fav"><span>♡</span></button>
// </li>
// path — от e.target вверх до <ul class="catalog">, например:
// [{ tag: 'svg', dataset: {} }, { tag: 'button', dataset: { action: 'add' } },
//  { tag: 'li', dataset: { id: '42' } }, { tag: 'ul', dataset: {} }]

function resolveAction(path) {
  const target = path[0];
  if (!target.dataset.action) {
    return null;
  }
  const card = path.find((el) => el.dataset.id);
  return { action: target.dataset.action, id: Number(card.dataset.id) };
}
"""),
    language="javascript",
    entry="resolveAction",
    answer=C("""
// Разметка карточки:
// <li data-id="42">
//   <button data-action="add"><svg class="icon">…</svg> В корзину</button>
//   <button data-action="fav"><span>♡</span></button>
// </li>
// path — от e.target вверх до <ul class="catalog">, например:
// [{ tag: 'svg', dataset: {} }, { tag: 'button', dataset: { action: 'add' } },
//  { tag: 'li', dataset: { id: '42' } }, { tag: 'ul', dataset: {} }]

function resolveAction(path) {
  const button = path.find((el) => el.dataset.action);
  if (!button) {
    return null;
  }
  const card = path.find((el) => el.dataset.id);
  return { action: button.dataset.action, id: Number(card.dataset.id) };
}
"""),
    tests=[
        {"input": [[{"tag": "button", "dataset": {"action": "add"}},
                    {"tag": "li", "dataset": {"id": "42"}},
                    {"tag": "ul", "dataset": {}}]],
         "expected": {"action": "add", "id": 42}},
        {"input": [[{"tag": "svg", "dataset": {}},
                    {"tag": "button", "dataset": {"action": "add"}},
                    {"tag": "li", "dataset": {"id": "42"}},
                    {"tag": "ul", "dataset": {}}]],
         "expected": {"action": "add", "id": 42}},
        {"input": [[{"tag": "span", "dataset": {}},
                    {"tag": "button", "dataset": {"action": "fav"}},
                    {"tag": "li", "dataset": {"id": "7"}},
                    {"tag": "ul", "dataset": {}}]],
         "expected": {"action": "fav", "id": 7}},
        {"input": [[{"tag": "path", "dataset": {}},
                    {"tag": "svg", "dataset": {}},
                    {"tag": "button", "dataset": {"action": "add"}},
                    {"tag": "li", "dataset": {"id": "5"}},
                    {"tag": "ul", "dataset": {}}]],
         "expected": {"action": "add", "id": 5}},
        {"input": [[{"tag": "li", "dataset": {"id": "42"}},
                    {"tag": "ul", "dataset": {}}]],
         "expected": None},
        {"input": [[{"tag": "ul", "dataset": {}}]], "expected": None},
    ],
))

tasks.append(task(
    "fe-dom-05", "fe-dom", "code_review", 3, "teamlead",
    "Марина: «Посмотри PR стажёра: расписание “Жми” с фильтром по дню и кнопками записи. У него на локалке “всё работает”, но я вижу минимум три вещи, которые нельзя мерджить. Отметь их».",
    "Выбери все замечания, которые действительно стоит оставить в ревью.",
    "Обработчики, повешенные на конкретные кнопки, живут, пока живут эти кнопки: после фильтра render через innerHTML удаляет старые элементы и создаёт новые, уже «глухие». Делегирование на #schedule решает это раз и навсегда — список можно перерисовывать сколько угодно. Вставка note через innerHTML — XSS: тег вроде <img onerror> от любого, у кого есть доступ к админке, выполнится у каждого посетителя. innerHTML += в цикле каждый раз сериализует и заново разбирает весь уже вставленный список — сборка через map и join с одним присваиванием быстрее и проще. querySelector не узкое место, а шаблонные строки — нормальный инструмент: опасна не строка, а отсутствие экранирования. preventDefault в submit как раз обязателен: форма без action отправляется на текущий адрес, и страница перезагрузится. А в стрелочных функциях e.target работает отлично — у них нет только своего this.",
    ["Что происходит с обработчиками, когда список перерисовывается через innerHTML?",
     "Откуда приходит note и кто может туда написать?",
     "Сколько раз браузер разбирает HTML списка в цикле с +=?"],
    code=C("""
// slots приходят из API: { id, time, title, coach, note }.
// note тренеры пишут в админке — там бывает что угодно.
const list = document.querySelector('#schedule');
const filter = document.querySelector('#day-filter');

function render(slots) {
  list.innerHTML = '';
  for (const slot of slots) {
    list.innerHTML += `
      <li>
        <b>${slot.time}</b> ${slot.title}, тренер ${slot.coach}
        <p class="note">${slot.note}</p>
        <button class="book" type="button" data-id="${slot.id}">Записаться</button>
      </li>`;
  }
}

function bindButtons() {
  document.querySelectorAll('.book').forEach((btn) => {
    btn.addEventListener('click', () => bookSlot(Number(btn.dataset.id)));
  });
}

filter.addEventListener('submit', (e) => {
  e.preventDefault();
  render(getSlots(filter.elements.day.value));
});

render(getSlots('mon'));
bindButtons();
"""),
    language="javascript",
    options=[
        "Кнопки получают обработчики один раз: после фильтра render их пересоздаёт, и новые не работают — делегировать на #schedule",
        "querySelector заметно медленнее getElementById — на странице расписания стоит заменить все вызовы, это даст выигрыш",
        "note из админки попадает в innerHTML без экранирования — это XSS; выводить через textContent или экранировать",
        "Шаблонные строки для HTML — антипаттерн: браузер разбирает их медленнее конкатенации через +, лучше переписать",
        "innerHTML += в цикле каждый раз заново разбирает весь список — собрать строку через map/join и присвоить один раз",
        "e.preventDefault() в submit лишний: у формы фильтра нет action, значит перезагрузки и так не будет",
        "В стрелочных обработчиках e.target указывает на window, а не на кнопку — заменить их на обычные function",
    ],
    answer=[0, 2, 4],
))

def shuffle_options(all_tasks, salt="f1"):
    """В исходнике варианты идут в удобном для автора порядке; в выдаче — детерминированно перемешаны."""
    for t in all_tasks:
        c = t["content"]
        if not isinstance(c.get("options"), list):
            continue
        opts, ans = c["options"], c["correct_answer"]
        order = list(range(len(opts)))
        random.Random(salt + ":" + t["task_id"]).shuffle(order)
        c["options"] = [opts[i] for i in order]
        c["correct_answer"] = sorted(order.index(i) for i in ans) if isinstance(ans, list) else order.index(ans)


shuffle_options(tasks)

data = {"track": "frontend", "part": "f1", "topics": topics, "tasks": tasks}

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8") as f:
    json.dump(data, f, ensure_ascii=False, indent=1)

if __name__ == "__main__":
    for tp in topics:
        print(tp["topic_id"], "theory:", len(tp["theory"]))
    print("tasks:", len(tasks), "->", OUT)
