#!/usr/bin/env python3
"""Генератор контента «Стажёра»: backend, часть b2.

Темы (junior): be-unit-tests, be-rest-crud, be-fastapi, be-sql-basics, be-orm.
Запуск: python3 gen/b2.py  →  out/backend_b2.json
"""
import json
import random
import os
import textwrap

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "out", "backend_b2.json")

XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}
GRADE = "junior"


def xp(d, g=GRADE):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


def D(s):
    """Убирает общий отступ и крайние пустые строки (для кода — с финальным \\n)."""
    return textwrap.dedent(s).strip("\n") + "\n"


def T(s):
    """Текст (вопрос/теория): убирает общий отступ и крайние переводы строк."""
    return textwrap.dedent(s).strip("\n")


TOPICS = []
TASKS = []


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


def task(topic_id, n, type_, difficulty, character, story, question, explanation, hints,
         code=None, language=None, entry=None, options=None, correct_answer=None, test_cases=None,
         time_limit=None):
    content = {
        "question": question,
        "code": code,
        "language": language,
    }
    if entry:
        content["entry"] = entry
    content.update({
        "options": options,
        "correct_answer": correct_answer,
        "test_cases": test_cases,
    })
    TASKS.append({
        "task_id": "%s-%02d" % (topic_id, n),
        "topic_id": topic_id,
        "grade": GRADE,
        "type": type_,
        "difficulty": difficulty,
        "xp_reward": xp(difficulty),
        "time_limit_minutes": time_limit,
        "character": character,
        "story": story,
        "content": content,
        "explanation": explanation,
        "hints": hints,
    })


def sql_answer(starter, query, marker="-- твой запрос ниже\n"):
    """Вставляет запрос игрока сразу после маркера в заготовке."""
    assert marker in starter
    return starter.replace(marker, marker + query, 1)


# =====================================================================
# be-unit-tests
# =====================================================================
UT = "be-unit-tests"

topic(UT, """
    Unit-тест — маленькая программа, которая вызывает твою функцию с известными данными и проверяет результат. Тесты ловят регрессии (починил одно — сломал другое), документируют поведение и дают смелость рефакторить. В Python стандарт индустрии — pytest.

    pytest сам находит тесты: файлы test_*.py, функции test_*, классы Test* без __init__. Проверка — обычный assert, при падении pytest покажет, что было слева и справа. Хороший тест читается как три шага: подготовка, действие, проверка (Arrange — Act — Assert).

        def test_total_with_promo():
            cart = Cart()
            cart.add("lamp", price=1000, qty=2)
            cart.apply_promo("LAMP10")
            assert cart.total() == 1800

    Запуск: pytest — все тесты, pytest tests/test_cart.py -v — один файл подробно, pytest -k promo — по части имени, pytest -x — остановиться на первом падении.

    Инструменты pytest:
    • pytest.raises — ждём исключение. Внутри with — только строка, которая должна упасть: всё, что стоит в блоке после неё, не выполнится. Проверки состояния — после блока.

        with pytest.raises(ValueError, match="unknown promo"):
            cart.apply_promo("FREE100")
        assert cart.total() == 2000

    • pytest.approx — сравнение дробных: 0.1 + 0.2 == 0.3 ложно, а 0.1 + 0.2 == pytest.approx(0.3) истинно.
    • Фикстуры (@pytest.fixture) — подготовка данных и ресурсов. По умолчанию фикстура создаётся заново для каждого теста (scope="function"), поэтому тесты не влияют друг на друга. Код после yield — уборка. Общие фикстуры кладут в conftest.py.
    • @pytest.mark.parametrize("text, minutes", [...]) — один тест, много наборов данных; в отчёте каждый набор — отдельный тест.

    Моки. Unit-тест не ходит в сеть, базу и платёжный шлюз — такие зависимости подменяют. unittest.mock.patch("модуль.имя") заменяет объект на MagicMock на время теста: return_value задаёт ответ, assert_called_once_with(...) проверяет вызов. Главное правило: патчи имя там, где его ищут, а не там, где его объявили. Если в orders/service.py написано from payments.client import charge, патчить нужно "orders.service.charge". Для подмены атрибутов и переменных окружения в pytest есть фикстура monkeypatch.

    Что тестировать:
    • обычный сценарий (happy path);
    • границы: 0, пустая строка, пустой список, значение ровно на пороге (< или <=?);
    • ошибки: неверный ввод даёт понятное исключение или отказ, а не мусор;
    • классы эквивалентности: не сто похожих случаев, а по одному из каждой группы.

    Тестируй поведение, а не реализацию: тест не должен ломаться, если ты заменил цикл на any(). 100% покрытия (pytest-cov) значит только, что каждая строка выполнилась, а не что проверены все ситуации.

    TDD — цикл «красный → зелёный → рефакторинг»: пишешь тест на новое поведение и видишь, что он падает; пишешь минимум кода, чтобы он прошёл; наводишь порядок, пока тесты зелёные. Тест, который ты ни разу не видел красным, может не проверять вообще ничего.

    Частые ошибки: тест без assert; assert внутри pytest.raises после падающей строки; тесты, зависящие от порядка запуска (общий изменяемый список на уровне модуля); настоящие сетевые вызовы; time.sleep в тестах; проверка через print.
""")

task(UT, 1, "write_code", 2, "teamlead",
     "Гена: «В Кодзилла Трекере оценку задачи вводят текстом: “2h 30m”, “1d”. Я по TDD сначала написал тесты — сейчас они красные, потому что функции ещё нет. Твоя очередь: сделай их зелёными».",
     T("""
        Напиши parse_estimate(text) — оценку в минутах. Единицы: m — минуты, h — часы, d — рабочий день (8 часов). Части разделены пробелами, их может быть несколько подряд; пустая строка — 0. Функция должна пройти тесты Гены:

            import pytest
            from tracker.estimates import parse_estimate

            @pytest.mark.parametrize("text, minutes", [
                ("45m", 45),
                ("2h", 120),
                ("1h 30m", 90),
                ("  3h   15m ", 195),
                ("1d 2h", 600),
                ("", 0),
            ])
            def test_parse_estimate(text, minutes):
                assert parse_estimate(text) == minutes
     """),
     "Это классический цикл TDD: тесты написаны заранее и служат спецификацией — по ним видно и формат ввода, и граничные случаи (лишние пробелы, пустая строка, дни). parametrize превращает одну функцию в шесть тестов, и pytest покажет, какой именно набор упал. split() без аргументов режет по любым пробельным символам и выкидывает пустые куски, поэтому «  3h   15m » и пустая строка обрабатываются без спецкода: для пустой строки цикл просто не выполнится и вернётся 0. Словарь единиц вместо цепочки if делает функцию расширяемой: добавить недели w — одна строка. После «зелёного» шага наступает рефакторинг — например, вынести 8 часов в константу, пока тесты страхуют.",
     ["Разбей строку на части через split() — он сам справится с лишними пробелами.",
      "У каждой части последний символ — единица, всё до него — число: part[-1] и part[:-1].",
      "Заведи словарь {\"m\": 1, \"h\": 60, \"d\": 8 * 60} и суммируй число × множитель."],
     code=D("""
        DAY_HOURS = 8


        def parse_estimate(text):
            # верни оценку в минутах
            pass
     """),
     language="python", entry="parse_estimate",
     correct_answer=D("""
        DAY_HOURS = 8
        UNIT_MINUTES = {"m": 1, "h": 60, "d": DAY_HOURS * 60}


        def parse_estimate(text):
            total = 0
            for part in text.split():
                unit = part[-1]
                amount = int(part[:-1])
                total += amount * UNIT_MINUTES[unit]
            return total
     """),
     test_cases=[
         {"input": ["45m"], "expected": 45},
         {"input": ["2h"], "expected": 120},
         {"input": ["1h 30m"], "expected": 90},
         {"input": ["  3h   15m "], "expected": 195},
         {"input": ["1d 2h"], "expected": 600},
         {"input": [""], "expected": 0},
     ])

task(UT, 2, "find_bug", 2, "qa",
     "Ира: «Баг: если ввести несуществующий промокод, корзина всё равно пересчитывается со скидкой 10%. Но тест test_unknown_promo_keeps_total зелёный! Как тест может быть зелёным, если баг воспроизводится?»",
     "Почему test_unknown_promo_keeps_total не ловит баг и как исправить тест?",
     "Когда apply_promo бросает ValueError, выполнение блока with сразу прерывается: pytest.raises ловит ожидаемое исключение, и тест считается пройденным. Строка assert стоит после падающего вызова внутри того же блока — до неё дело никогда не доходит. Поэтому внутри with pytest.raises оставляют только одну строку, которая должна упасть, а состояние проверяют после блока. Фикстура по умолчанию создаётся заново для каждого теста, так что тесты друг другу не мешают; pytest.raises ловит и наследников ValueError; 2000 == 2000.0 в Python истинно. А если бы apply_promo вообще не бросала исключение, pytest.raises сам уронил бы тест с «DID NOT RAISE» — зелёный тест как раз доказывает, что исключение есть. Урок TDD: тест, который ты ни разу не видел красным, может ничего не проверять — убедись, что он падает на баговом коде.",
     ["Подумай, какая строка теста выполнится после того, как apply_promo бросит исключение.",
      "Код в блоке with после строки с исключением пропускается.",
      "Assert нужно вынести из блока with pytest.raises."],
     code=D("""
        import pytest
        from market.cart import Cart


        @pytest.fixture
        def cart():
            c = Cart()
            c.add("lamp-e27", price=1000, qty=2)
            return c


        def test_total(cart):
            assert cart.total() == 2000


        def test_known_promo(cart):
            cart.apply_promo("LAMP10")
            assert cart.total() == 1800


        def test_unknown_promo_keeps_total(cart):
            with pytest.raises(ValueError):
                cart.apply_promo("FREE100")
                assert cart.total() == 2000
     """),
     language="python",
     options=[
         "assert стоит внутри with pytest.raises после строки с исключением и никогда не выполняется. Вынести assert за блок with",
         "Фикстура cart одна на все тесты модуля, и test_known_promo уже применил к ней скидку. Нужно пересоздавать корзину внутри теста",
         "pytest.raises(ValueError) ловит только точный тип, а не наследников — надёжнее pytest.raises(Exception), чтобы не зависеть от иерархии",
         "total() возвращает float 2000.0, а сравнение 2000.0 == 2000 ненадёжно — сравнивать суммы нужно через pytest.approx",
         "apply_promo с неизвестным кодом, видимо, не бросает ValueError, а тихо его игнорирует — тест надо переписать без pytest.raises",
     ],
     correct_answer=0)

task(UT, 3, "code_review", 2, "teamlead",
     "Марина: «PR с проверкой свободного слота для переговорок “Высоты”. Покрытие 100%, автор просит мёрдж. Но трёх тестов на логику пересечения интервалов маловато. Каких кейсов не хватает?»",
     "Выбери все тест-кейсы, которые действительно нужно добавить.",
     "Покрытие показывает, что каждая строка выполнилась, а не что проверены все ситуации: здесь вся логика в одной строке с all(), и её «покрывает» любой вызов. Баги в интервалах живут на границах, поэтому нужны классы эквивалентности: касание вплотную (проверяет < против <=), частичное пересечение слева и справа, полное накрытие, а для валидации — граница start == end. Тест на all() против цикла проверяет реализацию и сломается при безобидном рефакторинге. Тест с порогом в 1 мс — нестабильный (flaky): зависит от загрузки CI-машины. Перебор всех минут суток не добавляет новых классов, только время прогона.",
     ["Нарисуй на бумаге два отрезка времени: какими способами они могут располагаться друг относительно друга?",
      "Проверь границы: что будет, если встречи касаются концами? А если start == end?",
      "Хороший тест проверяет поведение, а не то, как функция устроена внутри."],
     code=D('''
        # rooms/slots.py
        def is_slot_free(bookings, start, end):
            """bookings — список пар (начало, конец) в минутах от начала дня.
            Интервалы полуоткрытые: [начало, конец)."""
            if start >= end:
                raise ValueError("start must be before end")
            return all(end <= b_start or start >= b_end for b_start, b_end in bookings)


        # tests/test_slots.py
        import pytest
        from rooms.slots import is_slot_free


        def test_empty_day_is_free():
            assert is_slot_free([], 600, 660)


        def test_same_slot_is_busy():
            assert not is_slot_free([(600, 660)], 600, 660)


        def test_reversed_interval_raises():
            with pytest.raises(ValueError):
                is_slot_free([], 660, 600)
     '''),
     language="python",
     options=[
         "Встреча вплотную: занято 9:00–10:00, проверяем 10:00–11:00 — слот должен быть свободен",
         "Частичное пересечение: занято 10:00–11:00, проверяем 10:30–11:30 и 9:30–10:30 — занято",
         "Новая встреча целиком накрывает существующую: занято 10:00–10:30, проверяем 9:00–12:00 — занято",
         "Пустой интервал start == end тоже должен бросать ValueError — эта граница сейчас не проверена",
         "Тест, что is_slot_free использует all(), а не цикл for: иначе при рефакторинге логика незаметно поменяется",
         "Тест производительности: 100 000 броней должны проверяться быстрее 1 мс, иначе переговорки будут тормозить",
         "Перебрать в цикле все start от 0 до 1439 с шагом в минуту и для каждого проверить, что функция не падает",
     ],
     correct_answer=[0, 1, 2, 3])

task(UT, 4, "write_code", 3, "client",
     "Артур, директор фитнес-клуба «Жми»: «Администраторы вбивают телефоны кто как хочет, и СМС о тренировках уходят в никуда. Гена говорит, тесты на нормализацию уже написаны — по всем форматам, что нашлись в нашей базе. Осталось сделать саму функцию. Когда СМС начнут доходить?»",
     T("""
        Напиши normalize_phone(raw): приведи российский номер к виду +7XXXXXXXXXX (после +7 ровно 10 цифр). Всё, кроме цифр, выбрасываем. 11 цифр, начинающихся с 7 или 8, — отбрасываем первую; 10 цифр — берём как есть; всё остальное — номер не распознан, верни None. Тесты Гены:

            import pytest
            from gym.phones import normalize_phone

            @pytest.mark.parametrize("raw, expected", [
                ("+7 (912) 345-67-89", "+79123456789"),
                ("8 912 345 67 89", "+79123456789"),
                ("9123456789", "+79123456789"),
                ("  +7-912-345-6789  ", "+79123456789"),
            ])
            def test_valid_phones(raw, expected):
                assert normalize_phone(raw) == expected

            @pytest.mark.parametrize("raw", [
                "",
                "12345",
                "+1 212 555 0100",
                "8 (912) 345-67-89 доб. 12",
            ])
            def test_invalid_phones(raw):
                assert normalize_phone(raw) is None
     """),
     "Тесты разбиты на два класса эквивалентности: разные записи одного валидного номера и мусор, который нельзя «угадать». Сначала нормализуем ввод — оставляем только цифры, тогда скобки, дефисы, пробелы и плюс перестают влиять. Потом решаем по длине: 11 цифр с ведущей 7 или 8 — это код страны или междугородняя восьмёрка, её отрезаем; ровно 10 цифр — номер без кода. Американский номер тоже даёт 11 цифр, но начинается с 1 — поэтому важна проверка первой цифры, а номер с добавочным длиннее 11 цифр и честно отклоняется. Возврат None вместо «почти правильной» строки — осознанное решение: лучше попросить администратора исправить номер, чем слать СМС в никуда.",
     ["Собери строку только из цифр: \"\".join([ch for ch in raw if ch.isdigit()]).",
      "Если цифр 11 и первая из них 7 или 8 — отрежь первую.",
      "В конце: если цифр не ровно 10 — None, иначе \"+7\" + цифры."],
     code=D("""
        def normalize_phone(raw):
            # оставь только цифры, потом разберись с длиной
            pass
     """),
     language="python", entry="normalize_phone",
     correct_answer=D("""
        def normalize_phone(raw):
            digits = "".join([ch for ch in raw if ch.isdigit()])
            if len(digits) == 11 and digits[0] in "78":
                digits = digits[1:]
            if len(digits) != 10:
                return None
            return "+7" + digits
     """),
     test_cases=[
         {"input": ["+7 (912) 345-67-89"], "expected": "+79123456789"},
         {"input": ["8 912 345 67 89"], "expected": "+79123456789"},
         {"input": ["9123456789"], "expected": "+79123456789"},
         {"input": ["  +7-912-345-6789  "], "expected": "+79123456789"},
         {"input": [""], "expected": None},
         {"input": ["12345"], "expected": None},
         {"input": ["+1 212 555 0100"], "expected": None},
         {"input": ["8 (912) 345-67-89 доб. 12"], "expected": None},
     ])

task(UT, 5, "find_bug", 3, "devops_colleague",
     "Дима: «CI на ветке с оплатой красный: test_checkout_marks_paid падает с ConnectionError: payments-sandbox.lampovy.local. Юнит-тесты вообще не должны ходить в сеть — там же мок стоит! Разберись».",
     "Почему мок не сработал и тест ходит в настоящий платёжный шлюз? Выбери правильный диагноз.",
     "patch подменяет атрибут в указанном модуле. Строка from market.payments.client import charge при импорте service скопировала ссылку на функцию в пространство имён market.orders.service. Тест патчит market.payments.client.charge, но checkout ищет имя charge в своём модуле и находит там старую, настоящую функцию. Правило: патчи там, где имя используется, — patch(\"market.orders.service.charge\"). Порядок аргументов верный: декоратор patch передаёт мок первым позиционным аргументом, а фикстуры pytest подставляет по имени. MagicMock спокойно принимает вложенные атрибуты вроде return_value.ok, а переменная окружения не отменит прямой вызов настоящей charge. Отключать сеть в CI полезно как страховка, но тест от этого не станет правильным — он просто упадёт с другой ошибкой.",
     ["Посмотри, как service получает функцию charge: через import модуля или через from ... import?",
      "После from X import charge у модуля service своя ссылка на функцию.",
      "Какой путь нужно передать в patch, чтобы подменить именно ту ссылку, которую вызывает checkout?"],
     code=D("""
        # market/orders/service.py
        from market.payments.client import charge


        def checkout(order):
            result = charge(order.user_id, order.total)
            order.status = "paid" if result.ok else "payment_failed"
            return order


        # tests/test_service.py
        from unittest.mock import patch

        from market.orders.service import checkout


        @patch("market.payments.client.charge")
        def test_checkout_marks_paid(mock_charge, order):
            mock_charge.return_value.ok = True

            checkout(order)

            assert order.status == "paid"
            mock_charge.assert_called_once_with(order.user_id, order.total)
     """),
     language="python",
     options=[
         "service импортировал charge через from … import и держит свою ссылку — патчить нужно patch(\"market.orders.service.charge\")",
         "Аргументы перепутаны: мок от @patch должен идти последним, после фикстуры order, иначе pytest подставит его не в тот параметр",
         "return_value.ok = True не срабатывает: у MagicMock нельзя задавать вложенные атрибуты, нужен настоящий объект ответа",
         "В CI не отключена сеть: если запретить исходящие соединения, мок сработает и тест пройдёт",
         "@patch не действует вместе с фикстурами — нужно заменить его на monkeypatch.setenv(\"PAYMENTS_URL\", \"\") внутри теста",
     ],
     correct_answer=0)


def ref_cases(code, entry, inputs):
    """Считает expected, исполняя эталон (CPython). Значения потом проверяет validate.py."""
    ns = {"__name__": "__main__"}
    exec(compile(code, "<ref>", "exec"), ns)
    fn = ns[entry]

    def norm(v):
        if isinstance(v, (tuple, list)):
            return [norm(x) for x in v]
        if isinstance(v, dict):
            return {str(k): norm(x) for k, x in v.items()}
        return v

    return [{"input": inp, "expected": norm(fn(*inp))} for inp in inputs]


# =====================================================================
# be-rest-crud
# =====================================================================
RC = "be-rest-crud"

topic(RC, """
    REST — стиль проектирования HTTP API, в котором всё строится вокруг ресурсов. Ресурс — существительное: товар, заказ, бронь. У коллекции и у отдельного элемента свои адреса:

        /products            — коллекция товаров
        /products/812        — один товар
        /orders/301/items    — позиции заказа 301

    Правила адресов: существительные во множественном числе, без глаголов (не /getProducts и не /deleteOrder — действие задаёт метод), нижний регистр, слова через дефис. Фильтры, сортировка и поиск — в query-параметрах: /products?category=lamps&sort=-price.

    CRUD и методы:
    • Create — POST /products → 201 Created, в теле созданный объект, в заголовке Location: /products/813.
    • Read — GET /products (список) и GET /products/812 → 200; такого нет — 404.
    • Update — PUT /products/812 заменяет ресурс целиком (не прислал поле — оно сбросится); PATCH /products/812 меняет только переданные поля. Ответ — 200 с обновлённым объектом (или 204 без тела).
    • Delete — DELETE /products/812 → 204 No Content, тела нет; такого нет — 404.

    Безопасность и идемпотентность. Безопасный метод ничего не меняет на сервере: GET, HEAD, OPTIONS. Поэтому удалять через GET нельзя: браузер делает префетч ссылок, краулеры ходят по всем URL, прокси повторяют GET при сбоях. Идемпотентный метод при повторе оставляет сервер в том же состоянии, что и после первого вызова: GET, PUT, DELETE. Повторный DELETE вернёт 404 вместо 204, но состояние то же — ресурса нет, — значит, он идемпотентен. POST не идемпотентен: два одинаковых POST /orders создадут два заказа. PATCH идемпотентен, только если присылает новые значения, а не изменения: {"price": 990} — да, {"stock_delta": -1} — нет. Это важно для ретраев: после обрыва сети клиент может спокойно повторить только идемпотентный запрос.

    Коды ответов, которые нужно знать наизусть:
    • 200 OK — успех с телом; 201 Created — создали; 204 No Content — успех без тела.
    • 400 Bad Request — запрос не разобрать (битый JSON).
    • 401 Unauthorized — клиент не представился; 403 Forbidden — представился, но нельзя.
    • 404 Not Found — ресурса нет.
    • 405 Method Not Allowed — ресурс есть, а метод не поддерживается.
    • 409 Conflict — конфликт с текущим состоянием: email уже занят, на это время уже есть бронь.
    • 422 Unprocessable Content — JSON корректный, но данные не проходят проверку (цена отрицательная, нет обязательного поля). FastAPI отдаёт 422 на ошибки валидации — ещё до вызова твоего кода.
    • 500 — ошибка сервера. Если клиент прислал плохие данные, а ты ответил 500, — это твой баг.

    Частые ошибки: 200 {"error": "not found"} — мониторинг, кэши и клиенты сочтут это успехом; глаголы в URL; изменение данных через GET; 200 вместо 201 при создании; переиспользование id удалённых записей — на старый id ссылаются заказы, закладки и кэши, и они внезапно начнут показывать чужой объект.
""")

task(RC, 1, "quiz", 2, "devops_colleague",
     "Дима: «В логах API видно: мобильное приложение Маркета при плохой сети повторяет запрос, если не дождалось ответа. Вчера так задвоились два заказа. Надо понимать, какие запросы можно ретраить без последствий, а какие наделают дублей».",
     "Какие запросы идемпотентны — повтор такого же запроса оставит сервер в том же состоянии, что и первый? Выбери все верные.",
     "GET ничего не меняет, поэтому он и безопасный, и идемпотентный. PUT присылает ресурс целиком: сколько раз ни запиши один и тот же адрес, результат одинаковый. DELETE после первого вызова оставляет «товара в корзине нет» — повтор вернёт 404, но состояние не изменится, а идемпотентность — именно про состояние сервера, а не про код ответа. POST создаёт новый ресурс при каждом вызове: повтор даст второй заказ или второй комментарий. PATCH с изменением (stock_delta: -1) при каждом повторе уменьшает остаток ещё раз, а вот PATCH с новым значением ({\"stock\": 7}) был бы идемпотентным. Для безопасных повторов POST используют ключ идемпотентности — об этом в теме про проектирование API.",
     ["Идемпотентность — про состояние сервера после запроса, а не про код ответа.",
      "Что будет, если выполнить запрос дважды: появится ли второй объект или изменится ли значение ещё раз?",
      "PATCH бывает и таким, и таким — смотри, что лежит в теле."],
     options=[
         "GET /products/812",
         "POST /orders с телом корзины",
         "PUT /users/7/address с полным адресом в теле",
         "DELETE /cart/items/15",
         "PATCH /products/812 с телом {\"stock_delta\": -1}",
         "POST /orders/301/comments с текстом комментария",
     ],
     correct_answer=[0, 2, 3])

RC2_ANSWER = D("""
    ALLOWED = {"GET", "POST", "PUT", "PATCH", "DELETE"}
    WITH_BODY = {"POST", "PUT", "PATCH"}


    def response_code(method, exists, body_ok=True, duplicate=False):
        if method not in ALLOWED:
            return 405
        if method in WITH_BODY and not body_ok:
            return 422
        if method == "POST":
            return 409 if duplicate else 201
        if not exists:
            return 404
        if method == "DELETE":
            return 204
        return 200
""")

task(RC, 2, "write_code", 2, "qa",
     "Ира: «В API абонементов “Жми” коды ответов кто во что горазд: удаление отвечает 200, дубль абонемента — 500, битое тело — 404. Давай зафиксируем правила в одной функции, а я по ней напишу тесты на все ручки».",
     T("""
        Напиши response_code(method, exists, body_ok=True, duplicate=False) — какой код должен вернуть API абонементов. Проверяй правила в таком порядке:
        • метод не из GET, POST, PUT, PATCH, DELETE → 405;
        • у POST, PUT и PATCH невалидное тело (body_ok=False) → 422;
        • POST /memberships создаёт абонемент: если такой уже есть (duplicate=True) → 409, иначе 201; exists для POST не важен;
        • GET, PUT, PATCH и DELETE работают с /memberships/{id}: абонемента нет (exists=False) → 404;
        • иначе GET, PUT, PATCH → 200, DELETE → 204.
     """),
     "Функция повторяет порядок, в котором запрос проходит через настоящий сервис. Сначала роутер: неизвестный метод отсекается с 405 (и заголовком Allow со списком разрешённых). Потом валидация тела — во FastAPI она выполняется до вызова ручки, поэтому PUT с битым телом на несуществующий id получит 422, а не 404. Дальше бизнес-логика: 409 Conflict — когда запрос корректен, но конфликтует с текущим состоянием (второй активный абонемент); 404 — ресурса нет. Успех различаем по смыслу: 201 — создали новое, 204 — удалили и тела не будет, 200 — вернули данные. Такая таблица правил — отличная основа для параметризованных тестов.",
     ["Сначала отсеки неизвестные методы, потом невалидное тело — именно в таком порядке.",
      "POST работает с коллекцией, поэтому exists для него не проверяется: только duplicate.",
      "Для остальных методов: нет ресурса — 404, DELETE — 204, иначе 200."],
     code=D("""
        def response_code(method, exists, body_ok=True, duplicate=False):
            # верни HTTP-код ответа
            pass
     """),
     language="python", entry="response_code",
     correct_answer=RC2_ANSWER,
     test_cases=ref_cases(RC2_ANSWER, "response_code", [
         ["GET", True],
         ["GET", False],
         ["POST", False],
         ["POST", False, True, True],
         ["POST", False, False, True],
         ["PUT", False, False],
         ["PUT", False],
         ["PATCH", True],
         ["DELETE", True],
         ["DELETE", False],
         ["TRACE", True],
     ]))

task(RC, 3, "code_review", 2, "teamlead",
     "Марина: «Джун набросал API записи на тренировки для “Жми” — пока только описание, до кода не дошло. Хорошо, что не дошло. Отметь, что нужно исправить до того, как он начнёт писать ручки».",
     "Выбери все замечания, которые действительно стоит оставить в ревью.",
     "Удаление через GET опасно: GET обязан быть безопасным, и его без спроса выполняют префетч браузера, краулеры и прокси при ретраях — бронь исчезнет сама. Ошибка с кодом 200 ломает всё вокруг: клиентский код, мониторинг ошибок и кэши считают ответ успешным. Создание ресурса по REST — 201 Created и заголовок Location с адресом новой брони. Глагол в пути (/getTrainings) дублирует метод; ресурс — существительное, а фильтр по дате — query-параметр. Тело у GET стандартом не определено, многие прокси и клиенты его выбрасывают; PUT прекрасно отправляется из fetch и любого HTTP-клиента; id в пути — обычная практика, безопасность обеспечивает проверка прав, а не прятанье id.",
     ["Какие из запросов могут выполниться сами, без действия пользователя?",
      "Посмотри на коды: как клиент поймёт, что бронь не найдена?",
      "Вспомни, как в REST называют ресурсы и что возвращают при создании."],
     code=D("""
        API записи на тренировки «Жми» — черновик, PR #214

        GET    /getTrainings?date=2026-10-01   → 200 [{"id": 5, "title": "Йога", ...}]
        POST   /trainings/{id}/book            → 200 {"booking_id": 91}
        GET    /bookings/{id}                  → 200 {"id": 91, "training_id": 5, ...}
                                                 бронь не найдена → 200 {"error": "not found"}
        GET    /bookings/{id}/delete           → 200 {"ok": true}
        PUT    /bookings/{id}                  → 200 {...}   (полная замена брони)
     """),
     language="text",
     options=[
         "Удаление через GET /bookings/{id}/delete: префетч браузера, краулер или ретрай прокси удалят бронь сами. Нужен DELETE /bookings/{id}",
         "«Не найдено» отдаётся с кодом 200 — клиент, мониторинг и кэши считают это успехом. Нужен 404",
         "Создание брони отвечает 200 — правильно 201 и заголовок Location: /bookings/91",
         "/getTrainings — глагол в пути. Ресурс называется существительным: GET /trainings?date=2026-10-01",
         "Дату лучше передавать в теле GET-запроса в JSON, а не в query-параметре: так её проще валидировать схемой",
         "PUT нужно заменить на POST: браузерные формы и часть HTTP-клиентов не умеют отправлять PUT",
         "id брони в URL — уязвимость: его видно в логах прокси, id нужно передавать только в заголовке запроса",
     ],
     correct_answer=[0, 1, 2, 3])

RC4_BUGGY = D('''
    class ProductRepo:
        """Товары в памяти: CRUD поверх словаря. Методы возвращают (код, тело)."""

        def __init__(self):
            self.items = {}

        def create(self, data):
            new_id = len(self.items) + 1
            product = dict(data)
            product["id"] = new_id
            self.items[new_id] = product
            return 201, product

        def get(self, product_id):
            if product_id not in self.items:
                return 404, None
            return 200, self.items[product_id]

        def replace(self, product_id, data):  # PUT — полная замена
            if product_id not in self.items:
                return 404, None
            product = dict(data)
            product["id"] = product_id
            self.items[product_id] = product
            return 200, product

        def delete(self, product_id):
            if product_id not in self.items:
                return 404, None
            del self.items[product_id]
            return 204, None

        def list_all(self):
            return 200, [self.items[k] for k in sorted(self.items)]


    def run(commands):
        """Выполняет сценарий Иры и возвращает [код, тело] каждого шага."""
        repo = ProductRepo()
        log = []
        for cmd in commands:
            action = cmd[0]
            if action == "create":
                status, body = repo.create(cmd[1])
            elif action == "get":
                status, body = repo.get(cmd[1])
            elif action == "replace":
                status, body = repo.replace(cmd[1], cmd[2])
            elif action == "delete":
                status, body = repo.delete(cmd[1])
            else:
                status, body = repo.list_all()
            log.append([status, body])
        return log
''')

RC4_FIXED = RC4_BUGGY.replace(
    "    def __init__(self):\n        self.items = {}\n",
    "    def __init__(self):\n        self.items = {}\n        self.next_id = 1\n",
).replace(
    "        new_id = len(self.items) + 1\n",
    "        new_id = self.next_id\n        self.next_id += 1\n",
)
assert RC4_FIXED != RC4_BUGGY and "self.next_id += 1" in RC4_FIXED

LAMP = {"name": "Лампа E27", "price": 190}
TORCH = {"name": "Торшер «Вечер»", "price": 8900}
BRA = {"name": "Бра «Маяк»", "price": 5600}

task(RC, 4, "find_bug", 3, "qa",
     "Ира: «Шаги: создать лампу, создать торшер, удалить лампу, создать бра. Ожидаю: три разных id, в каталоге торшер и бра. Факт: бра получило id 2 и затёрло торшер — он пропал из каталога!»",
     T("""
        run(commands) прогоняет сценарий через хранилище товаров ProductRepo и возвращает [код, тело] каждого шага. Найди и исправь ошибку. Требование: id никогда не переиспользуются — даже после удаления последнего товара новый получает следующий по счёту номер, ведь на старые id ссылаются заказы и закладки.
     """),
     "len(self.items) + 1 — это число товаров, а не следующий свободный номер. После удаления товаров становится меньше, и новый id совпадает с уже занятым: запись в словаре молча перезаписывается, и торшер исчезает. Исправление — отдельный счётчик next_id, который только растёт; в базе данных ту же роль играют последовательности (SERIAL/IDENTITY в PostgreSQL). Вариант max(id) + 1 тоже плох: после удаления последнего товара его id достанется новому, и старые ссылки на заказы и кэши начнут показывать чужой товар. Остальной CRUD написан правильно: 201 при создании, 204 без тела при удалении, 404 для отсутствующего ресурса.",
     ["Сколько товаров будет в словаре после двух созданий и одного удаления? Какой id получит следующий?",
      "Нужен источник id, который не уменьшается при удалении.",
      "Заведи в __init__ счётчик self.next_id = 1 и увеличивай его в create."],
     code=RC4_BUGGY,
     language="python", entry="run",
     correct_answer=RC4_FIXED,
     test_cases=ref_cases(RC4_FIXED, "run", [
         [[["create", LAMP], ["create", TORCH], ["delete", 1], ["create", BRA], ["list"]]],
         [[["create", LAMP], ["create", TORCH], ["delete", 2], ["create", BRA], ["get", 2], ["get", 3]]],
         [[["create", LAMP], ["replace", 1, {"name": "Лампа E27 LED", "price": 210}], ["get", 1],
           ["delete", 1], ["delete", 1], ["get", 1], ["replace", 1, LAMP]]],
     ]))

task(RC, 5, "estimation", 3, "manager",
     "Стас: «Артуру из “Жми” нужен API абонементов: создать, посмотреть, продлить, удалить. Обычный CRUD, за день же сделаешь? Мне нужна цифра к созвону в 15:00».",
     "Прежде чем назвать срок, выбери вопросы, которые обязательно нужно задать Стасу.",
     "«Обычный CRUD» прячет решения, которые меняют объём в разы. Удаление: на абонемент ссылаются записи на тренировки, поэтому нужно решить — архивировать (soft delete с историей) или запрещать удаление, а не просто DELETE. Уникальность активного абонемента — это ограничение в базе и ответ 409 при конфликте. Кто вызывает API — значит, какая авторизация и какие роли. «Продлить» может оказаться оплатой, а это интеграция с платёжкой — отдельная задача на неделю. Язык и фреймворк уже выбраны стеком проекта, а объём кода в строках ничего не говорит о сроке. Разумный ответ Стасу: «Голый CRUD с миграцией, валидацией и тестами — 1–1,5 дня; с ролями и архивом — 2–3 дня; продление с оплатой — оцениваю отдельно после уточнения».",
     ["Какие ответы добавят в задачу новые таблицы, проверки или интеграции?",
      "Подумай про связи абонемента с другими данными и про права.",
      "Слово «продлить» может означать совсем разные вещи."],
     options=[
         "Что происходит с записями на тренировки при удалении абонемента — удалять совсем или архивировать с историей?",
         "Может ли у клиента быть два активных абонемента сразу? Что отвечать на попытку создать второй?",
         "Кто вызывает API — админка клуба, приложение клиента или оба? Кто что может менять?",
         "«Продлить» — это поменять дату окончания или купить новый период с оплатой?",
         "На каком языке писать сервис — на Python, как весь бэкенд, или попробовать Go, раз сервис новый?",
         "Сколько примерно строк кода должно получиться, чтобы понять, укладываемся ли в день?",
         "Можно ли не писать тесты, раз это простой CRUD, а проверить всё руками через Swagger?",
     ],
     correct_answer=[0, 1, 2, 3],
     time_limit=10)


# =====================================================================
# be-fastapi
# =====================================================================
FA = "be-fastapi"

topic(FA, """
    FastAPI — самый популярный Python-фреймворк для API. Ты пишешь функции с аннотациями типов, а он сам разбирает запрос, валидирует данные через Pydantic, отвечает 422 на ошибки и генерирует документацию.

        from fastapi import FastAPI, HTTPException
        from pydantic import BaseModel

        app = FastAPI()

        class ProductIn(BaseModel):
            name: str
            price: int

        class ProductOut(BaseModel):
            id: int
            name: str
            price: int

        @app.post("/products", response_model=ProductOut, status_code=201)
        def create_product(data: ProductIn):
            return repo.create(data)

        @app.get("/products/{product_id}", response_model=ProductOut)
        def get_product(product_id: int):
            product = repo.get(product_id)
            if product is None:
                raise HTTPException(status_code=404, detail="Product not found")
            return product

    Запуск: fastapi dev main.py или uvicorn main:app --reload. Документация — /docs (Swagger UI), схема — /openapi.json.

    Откуда берутся параметры:
    • Path — имя есть в пути: {product_id}. Тип берётся из аннотации: /products/abc при product_id: int → 422.
    • Query — простой параметр, которого нет в пути. Без значения по умолчанию он обязателен, со значением — нет: def list_products(category: str, limit: int = 20). Ограничения и повторяющиеся параметры — через Query внутри Annotated: limit: Annotated[int, Query(ge=1, le=100)] = 20, size: Annotated[list[str], Query()] = [] для ?size=s&size=l. bool понимает true/false, 1/0, yes/no, on/off.
    • Body — параметр с типом Pydantic-модели: JSON разбирается и проверяется, лишние поля по умолчанию отбрасываются.
    Всё это проверяется до вызова твоей функции: на ошибку клиент получит 422 с полем detail, где указано, какой параметр и почему не прошёл.

    Модель ответа. response_model (или аннотация возвращаемого типа) фильтрует ответ: наружу уходят только поля схемы. Поэтому модели входа, ответа и базы разделяют — хэш пароля или закупочная цена не должны оказаться в ProductOut. Если вернуть None, а схема ждёт объект, ответ не пройдёт проверку и клиент получит 500. «Не найдено» — это raise HTTPException(status_code=404).

    Коды: status_code=201 в декораторе для создания, 204 для удаления. HTTPException прерывает обработку и отдаёт код с телом {"detail": ...}.

    Зависимости (Depends) — общий код, который FastAPI выполняет перед ручкой: сессия БД, текущий пользователь. Рекомендуемая запись — db: Annotated[Session, Depends(get_db)]; старая db: Session = Depends(get_db) тоже работает и часто встречается. Функцию передают без скобок. Код после yield в зависимости выполняется, когда ответ готов, — так закрывают сессии.

    Ресурсы на всё приложение (пул БД, HTTP-клиент) создают в lifespan: async-контекстный менеджер, код до yield — при старте, после — при остановке; app = FastAPI(lifespan=lifespan). @app.on_event("startup") устарел.

    Роутеры: APIRouter(prefix="/users", tags=["users"]) делит приложение на модули и подключается через app.include_router(router).

    Порядок роутов важен: FastAPI проверяет их в порядке объявления. Если /users/{user_id} объявлен раньше /users/me, запрос /users/me уйдёт в первый роут и получит 422 — "me" не число. Конкретные пути объявляй раньше параметризованных.

    def или async def? Обычные def FastAPI запускает в пуле потоков — это нормально для синхронной работы с БД. async def нужен, когда внутри есть await, а блокирующий вызов внутри async def тормозит весь сервер — подробно в теме про асинхронность.
""")

task(FA, 1, "quiz", 1, "qa",
     "Ира: «Проверяю новый эндпоинт каталога. Хочу понять, какие из моих запросов FastAPI отобьёт с 422 ещё до кода ручки — чтобы не заводить на это баги».",
     "Какие запросы вернут 422? Выбери все верные.",
     "У category нет значения по умолчанию, поэтому FastAPI считает этот query-параметр обязательным: без него ответ 422 с detail вида {\"loc\": [\"query\", \"category\"], \"msg\": \"Field required\"}. limit объявлен как int, а строку ten в число не превратить — тоже 422. in_stock=1 — корректный bool: FastAPI понимает 1/0, true/false, yes/no, on/off. Порядок query-параметров в URL не важен. Все эти проверки FastAPI делает по аннотациям типов до вызова функции — а в /docs видно, какие параметры обязательны.",
     ["Какой параметр объявлен без значения по умолчанию?",
      "Что будет, если в int-параметр передать текст?"],
     code=D("""
        @app.get("/products")
        def list_products(category: str, limit: int = 20, in_stock: bool = False):
            ...
     """),
     language="python",
     options=[
         "GET /products?category=lamps",
         "GET /products",
         "GET /products?category=lamps&limit=ten",
         "GET /products?category=lamps&in_stock=1",
         "GET /products?limit=5&category=cables",
     ],
     correct_answer=[1, 2])

FA2_ANSWER = D("""
    SIZES = ["s", "m", "l"]
    BOOLS = {"true": True, "1": True, "false": False, "0": False}


    def parse_menu_query(query):
        params = {"size": [], "max_price": None, "vegan": False, "limit": 20}
        for pair in query.split("&"):
            key, _, value = pair.partition("=")
            if key == "size":
                if value not in SIZES:
                    return [422, "size"]
                params["size"].append(value)
            elif key == "max_price":
                if not value.isdigit():
                    return [422, "max_price"]
                params["max_price"] = int(value)
            elif key == "vegan":
                if value not in BOOLS:
                    return [422, "vegan"]
                params["vegan"] = BOOLS[value]
            elif key == "limit":
                if not value.isdigit() or not 1 <= int(value) <= 50:
                    return [422, "limit"]
                params["limit"] = int(value)
        return [200, params]
""")

task(FA, 2, "write_code", 2, "manager",
     "Стас: «Офис просит фильтр меню в КофеБоте уже сегодня: размер, максимальная цена, веганское. Гена говорит, бот живёт на старом самописном роутере и FastAPI туда пока не затащить — значит, query-строку разбираем руками. Успеешь до вечера?»",
     T("""
        Напиши parse_menu_query(query) для GET /menu?… Верни [200, params] или [422, "имя_параметра"] — для первого по порядку параметра, который не прошёл проверку. Параметры:
        • size — одно из s, m, l; можно повторять (size=s&size=l) → список в порядке появления; по умолчанию [];
        • max_price — целое число ≥ 0; по умолчанию None;
        • vegan — true/false или 1/0; по умолчанию False;
        • limit — целое от 1 до 50; по умолчанию 20.
        Неизвестные параметры игнорируй. Пустая строка — все значения по умолчанию. Значения приходят уже без %-кодирования.

        Пример: "size=s&size=l&vegan=1" → [200, {"size": ["s", "l"], "max_price": None, "vegan": True, "limit": 20}]
     """),
     "Ровно это FastAPI делает по сигнатуре def menu(size: Annotated[list[Literal[\"s\", \"m\", \"l\"]], Query()] = [], max_price: Annotated[int | None, Query(ge=0)] = None, vegan: bool = False, limit: Annotated[int, Query(ge=1, le=50)] = 20): разбивает строку на пары, собирает повторяющиеся ключи в список, приводит типы, проверяет диапазоны, подставляет значения по умолчанию и отвечает 422 с именем параметра. partition(\"=\") удобнее split: он всегда возвращает три части, даже если знака = нет, — тогда значение пустое и не пройдёт проверку. isdigit() отсекает и буквы, и минус, поэтому -100 не станет ценой. Неизвестные параметры вроде utm_source игнорируем, как и FastAPI: их добавляют маркетинговые ссылки.",
     ["Разбей строку по & и каждую пару через partition(\"=\") на ключ и значение.",
      "Начни со словаря значений по умолчанию и обновляй его; size копи в список через append.",
      "Для чисел проверь value.isdigit() до int(value), для limit — ещё и диапазон 1..50."],
     code=D("""
        def parse_menu_query(query):
            params = {"size": [], "max_price": None, "vegan": False, "limit": 20}
            # разбери query и проверь значения
            return [200, params]
     """),
     language="python", entry="parse_menu_query",
     correct_answer=FA2_ANSWER,
     test_cases=ref_cases(FA2_ANSWER, "parse_menu_query", [
         [""],
         ["size=s&size=l&vegan=1"],
         ["max_price=250&limit=5&utm_source=tg"],
         ["vegan=false&limit=50&size=m"],
         ["size=xl"],
         ["limit=0"],
         ["max_price=-100"],
         ["vegan=yes"],
         ["size=m&limit=abc&vegan=maybe"],
         ["limit=51"],
     ]))

task(FA, 3, "find_bug", 2, "qa",
     "Ира: «Открываю профиль в приложении Маркета — GET /users/me отвечает 422: “Input should be a valid integer, unable to parse string as an integer”. А GET /users/15 работает. Шаги: залогиниться, нажать “Профиль”».",
     "Почему /users/me отвечает 422? Выбери правильный диагноз и исправление.",
     "FastAPI сопоставляет путь с роутами в порядке их объявления. Шаблон /users/{user_id} подходит под /users/me — первым совпал он, FastAPI пытается превратить \"me\" в int и отвечает 422 ещё до вызова функции. До get_me запрос просто не доходит. Исправление — объявить конкретный путь /me раньше параметризованного /{user_id}. Параметр user_id в get_me не нужен: пользователь берётся из зависимости get_current_user, а у каждого роута своя модель ответа. prefix у роутера с такими путями работает как обычно, а Depends принимает саму функцию — со скобками ты передал бы результат вызова.",
     ["Какой из двух роутов объявлен первым и подходит ли его шаблон под /users/me?",
      "Сообщение об ошибке — про преобразование в int. Где в коде int?",
      "Поменяй роуты местами."],
     code=D("""
        from fastapi import APIRouter, Depends, HTTPException
        from sqlalchemy.orm import Session

        router = APIRouter(prefix="/users", tags=["users"])


        @router.get("/{user_id}", response_model=UserPublic)
        def get_user(user_id: int, db: Session = Depends(get_db)):
            user = db.get(User, user_id)
            if user is None:
                raise HTTPException(status_code=404, detail="User not found")
            return user


        @router.get("/me", response_model=UserPrivate)
        def get_me(current_user: User = Depends(get_current_user)):
            return current_user
     """),
     language="python",
     options=[
         "Роуты проверяются по порядку: /{user_id} объявлен раньше и перехватывает /me, а \"me\" не парсится в int. Объявить /me выше",
         "В get_me не хватает параметра user_id: int — FastAPI ищет его в пути и отвечает 422, когда не находит",
         "prefix=\"/users\" конфликтует с путём /me: в роутере с префиксом пути без параметров не работают, нужен полный путь",
         "Нужно писать Depends(get_current_user()) со скобками, иначе зависимость не вызывается и пользователь не приходит",
         "У одного роутера может быть только одна модель ответа: UserPrivate конфликтует с UserPublic и ломает валидацию",
     ],
     correct_answer=0)

task(FA, 4, "code_review", 3, "teamlead",
     "Марина: «PR с каталогом для Маркета. Хранение в памяти — это прототип, база будет в следующем PR, так что про словарь не пиши. Отметь то, что уйдёт в контракт API и что нельзя мёрджить».",
     "Выбери все замечания, которые действительно нужно оставить.",
     "Если товара нет, PRODUCTS.get вернёт None, а response_model ждёт объект Product: FastAPI не пропустит такой ответ и клиент получит 500 вместо понятного 404 — нужно raise HTTPException(status_code=404). response_model=Product выводит наружу всё, что есть в схеме, включая закупочную цену: модели входа, хранения и ответа нужно разделять (ProductPublic без purchase_price). Создание ресурса по контракту — 201, это задаётся status_code=201 в декораторе. Обычные def FastAPI вызывает в пуле потоков, так что async не обязателен; product_id: int на /products/abc даст 422, а не 500; наследование Pydantic-моделей — нормальный способ переиспользовать поля.",
     ["Что вернёт get_product для несуществующего id и что с этим сделает response_model?",
      "Какие поля схемы Product увидит любой покупатель?",
      "Какой код должен вернуть POST, который создал ресурс?"],
     code=D("""
        from itertools import count

        from fastapi import FastAPI
        from pydantic import BaseModel

        app = FastAPI()


        class ProductIn(BaseModel):
            name: str
            price: int
            purchase_price: int  # закупочная цена — коммерческая тайна


        class Product(ProductIn):
            id: int


        PRODUCTS: dict[int, Product] = {}
        _ids = count(1)


        @app.post("/products", response_model=Product)
        def create_product(data: ProductIn):
            product = Product(id=next(_ids), **data.model_dump())
            PRODUCTS[product.id] = product
            return product


        @app.get("/products/{product_id}", response_model=Product)
        def get_product(product_id: int):
            return PRODUCTS.get(product_id)
     """),
     language="python",
     options=[
         "Для несуществующего id get_product вернёт None, это не пройдёт response_model — клиент получит 500 вместо 404",
         "response_model=Product отдаёт наружу purchase_price — нужна отдельная схема ответа без закупочной цены",
         "POST создаёт ресурс, а отвечает 200. Нужно status_code=201 в декораторе",
         "Ручки нужно сделать async def: обычные def блокируют event loop, и под нагрузкой сервис встанет",
         "product_id: int небезопасен — на /products/abc приложение упадёт с 500, надо принимать str и приводить вручную",
         "Product наследуется от ProductIn — Pydantic так не умеет, поля родителя не попадут в схему ответа",
     ],
     correct_answer=[0, 1, 2])


# =====================================================================
# be-sql-basics
# =====================================================================
SQ = "be-sql-basics"

topic(SQ, """
    SQL — язык запросов к реляционным базам: PostgreSQL, MySQL, SQLite. Данные лежат в таблицах: строка — запись, столбец — поле с типом.

    Типы, которые встретишь каждый день (PostgreSQL):
    • integer и bigint — целые числа, bigint для id больших таблиц;
    • numeric(10, 2) — деньги. Никогда не float/real: в нём 0.1 + 0.2 не равно 0.3. Другой вариант — хранить копейки в integer;
    • text и varchar(n) — строки;
    • boolean — true/false;
    • timestamptz — дата и время с часовым поясом, date — только дата;
    • NULL — «значение неизвестно», бывает в любом типе. NOT NULL запрещает его.

    Чтение:

        SELECT id, name, price
        FROM products
        WHERE category = 'lamps' AND stock > 0
        ORDER BY price DESC, id
        LIMIT 10 OFFSET 20;

    • Перечисляй нужные столбцы вместо SELECT *: меньше данных по сети, и код не сломается от новой колонки.
    • Условия: =, <> (не равно), <, >=, BETWEEN 100 AND 500 (включительно), IN ('lamps', 'bulbs'), LIKE 'Лампа%' (% — любые символы). Строки — в одинарных кавычках.
    • AND выполняется раньше OR, как умножение раньше сложения: a OR b AND c — это a OR (b AND c). Смешиваешь AND и OR — ставь скобки.
    • Без ORDER BY порядок строк не гарантирован, даже если «всегда шло по id». LIMIT без ORDER BY вернёт случайные строки.
    • DISTINCT убирает повторяющиеся строки.

    NULL. Любое сравнение с NULL даёт не true и не false, а NULL — и WHERE такую строку отбрасывает:
    • WHERE phone = NULL не вернёт ничего — пиши IS NULL / IS NOT NULL;
    • WHERE promo_code <> 'STAFF' потеряет строки, где promo_code — NULL. Нужно (promo_code IS NULL OR promo_code <> 'STAFF') или COALESCE(promo_code, '') <> 'STAFF';
    • COALESCE(a, b) возвращает первое значение, которое не NULL;
    • при сортировке по возрастанию PostgreSQL ставит NULL в конец (меняется через NULLS FIRST/LAST), SQLite — в начало.

    Изменение данных:

        INSERT INTO products (name, category, price) VALUES ('Бра «Маяк»', 'lamps', 5600);
        UPDATE products SET price = 990 WHERE id = 812;
        DELETE FROM carts WHERE user_id IS NULL AND updated_at < '2026-09-01';

    • В INSERT всегда перечисляй столбцы — их порядок в таблице может поменяться.
    • UPDATE и DELETE без WHERE меняют ВСЮ таблицу. Правило: сначала SELECT с тем же WHERE — посмотри, какие и сколько строк попадут, потом меняй.
    • psql по умолчанию работает в autocommit: каждая команда фиксируется сразу, и ROLLBACK после неё уже не поможет. Ручные правки на проде — только в явной транзакции:

        BEGIN;
        UPDATE products SET price = 990 WHERE id = 812;   -- ответ: UPDATE 1
        COMMIT;   -- или ROLLBACK, если строк неожиданно много

    • RETURNING (PostgreSQL, SQLite 3.35+) сразу покажет изменённые строки: UPDATE … RETURNING id, price.

    Даты в формате ISO (2026-09-01 12:00) правильно сравниваются даже как строки — поэтому в SQLite и в задачах их хранят именно так.
""")

SQ1_STARTER = D("""
    CREATE TABLE products (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        category TEXT NOT NULL,
        price INTEGER NOT NULL,   -- в рублях
        stock INTEGER NOT NULL
    );
    INSERT INTO products VALUES
        (1, 'Торшер «Вечер»', 'lamps', 8900, 4),
        (2, 'Лампа E27 LED 10W', 'bulbs', 190, 500),
        (3, 'Люстра «Каскад»', 'lamps', 24500, 0),
        (4, 'Настольная лампа «Студент»', 'lamps', 2400, 17),
        (5, 'Бра «Маяк»', 'lamps', 5600, 2),
        (6, 'Удлинитель 5 м', 'cables', 890, 40),
        (7, 'Светильник «Луна»', 'lamps', 12900, 1),
        (8, 'Ночник «Сова»', 'lamps', 1200, 30);

    -- твой запрос ниже
""")

task(SQ, 1, "write_code", 1, "manager",
     "Стас: «Для баннера на главной нужны три самых дорогих светильника, которые есть на складе. Дай название и цену — маркетинг вставит в макет».",
     "Допиши запрос: выведи name и price трёх самых дорогих товаров категории 'lamps', у которых stock больше 0, от дорогих к дешёвым.",
     "WHERE отбирает строки по двум условиям сразу: категория и наличие на складе — люстра «Каскад» самая дорогая, но её нет в наличии, и на баннер она не попадает. ORDER BY price DESC ставит дорогие первыми (по умолчанию сортировка по возрастанию — ASC). LIMIT 3 отрезает первые три строки уже после сортировки. Без ORDER BY LIMIT вернул бы три случайные строки — порядок в таблице ничем не гарантирован.",
     ["Два условия в WHERE соединяются через AND.",
      "Сортировка от большего к меньшему — ORDER BY price DESC.",
      "Ограничь выдачу через LIMIT 3."],
     code=SQ1_STARTER, language="sql",
     correct_answer=sql_answer(SQ1_STARTER, D("""
        SELECT name, price
        FROM products
        WHERE category = 'lamps' AND stock > 0
        ORDER BY price DESC
        LIMIT 3;
     """)),
     test_cases=[{"input": None, "expected": [
         ["Светильник «Луна»", 12900],
         ["Торшер «Вечер»", 8900],
         ["Бра «Маяк»", 5600],
     ]}])

SQ2_BUGGY = D("""
    CREATE TABLE orders (
        id INTEGER PRIMARY KEY,
        customer_email TEXT NOT NULL,
        promo_code TEXT,   -- NULL, если промокод не вводили
        paid_at TEXT       -- NULL, пока заказ не оплачен
    );
    INSERT INTO orders VALUES
        (101, 'anna@example.com', NULL, NULL),
        (102, 'boris@example.com', 'LAMP10', NULL),
        (103, 'gena@codezilla.dev', 'STAFF', NULL),
        (104, 'vika@example.com', NULL, '2026-09-20 14:02'),
        (105, 'oleg@example.com', NULL, NULL),
        (106, 'marina@codezilla.dev', 'STAFF', '2026-09-21 10:15');

    -- кому отправить напоминание об оплате
    SELECT id, customer_email
    FROM orders
    WHERE paid_at IS NULL AND promo_code <> 'STAFF'
    ORDER BY id;
""")
SQ2_FIXED = SQ2_BUGGY.replace(
    "WHERE paid_at IS NULL AND promo_code <> 'STAFF'",
    "WHERE paid_at IS NULL\n  AND (promo_code IS NULL OR promo_code <> 'STAFF')")
assert SQ2_FIXED != SQ2_BUGGY

task(SQ, 2, "find_bug", 2, "qa",
     "Ира: «Напоминание “оплатите заказ” получили не все. Заказы без промокода вообще не попали в выборку! Сотрудников с промокодом STAFF мы специально не беспокоим, а всех остальных должны».",
     "Запрос должен вернуть неоплаченные заказы всех клиентов, кроме заказов с промокодом STAFF. Найди и исправь ошибку.",
     "У заказов без промокода promo_code — NULL, а сравнение NULL <> 'STAFF' даёт не true, а NULL («неизвестно»). WHERE пропускает только строки, где условие истинно, поэтому все заказы без промокода молча выпали. Исправление — явно разрешить NULL: (promo_code IS NULL OR promo_code <> 'STAFF'). Скобки обязательны, иначе OR «перетянет» условие на paid_at. Равноценные варианты — COALESCE(promo_code, '') <> 'STAFF' или, в PostgreSQL, promo_code IS DISTINCT FROM 'STAFF'. Правило на всю жизнь: видишь в WHERE <>, NOT IN или NOT LIKE по nullable-колонке — подумай про NULL.",
     ["Какое значение promo_code у заказов 101 и 105?",
      "Чему равно NULL <> 'STAFF' — true, false или чему-то третьему?",
      "Добавь проверку promo_code IS NULL через OR и не забудь скобки."],
     code=SQ2_BUGGY, language="sql",
     correct_answer=SQ2_FIXED,
     test_cases=[{"input": None, "expected": [
         [101, "anna@example.com"],
         [102, "boris@example.com"],
         [105, "oleg@example.com"],
     ]}])

SQ3_STARTER = D("""
    CREATE TABLE carts (
        id INTEGER PRIMARY KEY,
        user_id INTEGER,          -- NULL у гостей
        updated_at TEXT NOT NULL
    );
    INSERT INTO carts VALUES
        (1, NULL, '2026-06-11 09:30'),
        (2, 42, '2026-05-02 18:00'),
        (3, NULL, '2026-09-15 21:10'),
        (4, NULL, '2026-08-31 23:59'),
        (5, 7, '2026-09-20 12:00'),
        (6, NULL, '2026-09-01 00:00');

    -- твой запрос ниже


    -- проверка: какие корзины остались (не меняй)
    SELECT id FROM carts ORDER BY id;
""")

task(SQ, 3, "write_code", 2, "devops_colleague",
     "Дима: «Таблица carts на проде разрослась до 30 ГБ — гостевые корзины никто не чистит. Нужен запрос для ночной чистки. Только аккуратно: корзины залогиненных пользователей не трогаем ни при каком раскладе».",
     "Напиши DELETE: удали гостевые корзины (user_id не указан), которые не обновлялись с 1 сентября 2026 года, — updated_at раньше '2026-09-01'. Проверочный SELECT в конце не меняй.",
     "Гостевая корзина — это user_id IS NULL; условие user_id = NULL не совпадёт ни с одной строкой, и запрос молча ничего не удалит. Второе условие отсекает свежие корзины: ISO-даты сравниваются как строки правильно, а '2026-09-01 00:00' не меньше '2026-09-01', поэтому корзина 6 остаётся — граница не входит. Забудешь про user_id — удалишь корзину пользователя 42 со всеми товарами. Хорошая привычка перед любым DELETE: выполнить SELECT с тем же WHERE и посмотреть, какие строки попадут. На большой таблице в проде такую чистку ещё и делят на пачки, чтобы не держать блокировки долго.",
     ["Гостевые корзины — это те, где user_id IS NULL, а не = NULL.",
      "Второе условие: updated_at < '2026-09-01'. Соедини условия через AND.",
      "DELETE FROM carts WHERE … ; — и перед этим мысленно прогони SELECT с тем же WHERE."],
     code=SQ3_STARTER, language="sql",
     correct_answer=sql_answer(SQ3_STARTER, D("""
        DELETE FROM carts
        WHERE user_id IS NULL
          AND updated_at < '2026-09-01';
     """)),
     test_cases=[{"input": None, "expected": [[2], [3], [5], [6]]}])

SQ4_BUGGY = D("""
    CREATE TABLE products (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        category TEXT NOT NULL,
        stock INTEGER NOT NULL,
        discontinued INTEGER NOT NULL DEFAULT 0,   -- 1 = снят с производства
        is_visible INTEGER NOT NULL DEFAULT 1
    );
    INSERT INTO products (id, name, category, stock, discontinued) VALUES
        (1, 'Торшер «Вечер»', 'lamps', 0, 0),
        (2, 'Лампа «Студент»', 'lamps', 12, 1),
        (3, 'Бра «Маяк»', 'lamps', 5, 0),
        (4, 'Удлинитель 5 м', 'cables', 0, 0),
        (5, 'Кабель HDMI 2 м', 'cables', 30, 1),
        (6, 'Светильник «Луна»', 'lamps', 3, 0);

    -- скрыть с витрины лампы, которых нет на складе или которые сняты с производства
    UPDATE products
    SET is_visible = 0
    WHERE stock = 0 OR discontinued = 1 AND category = 'lamps';

    -- проверка
    SELECT id, is_visible FROM products ORDER BY id;
""")
SQ4_FIXED = SQ4_BUGGY.replace(
    "WHERE stock = 0 OR discontinued = 1 AND category = 'lamps';",
    "WHERE category = 'lamps' AND (stock = 0 OR discontinued = 1);")
assert SQ4_FIXED != SQ4_BUGGY

task(SQ, 4, "find_bug", 3, "manager",
     "Стас: «Попросил скрыть с витрины лампы, которых нет на складе или которые сняты с производства. Лампы скрылись, но вместе с ними пропал удлинитель — а это вообще кабели! Клиенты звонят, не могут заказать».",
     "UPDATE должен скрыть (is_visible = 0) только товары категории 'lamps', у которых stock = 0 или discontinued = 1. Найди и исправь ошибку.",
     "AND связывает сильнее, чем OR, поэтому условие читается как stock = 0 OR (discontinued = 1 AND category = 'lamps'). Любой товар с нулевым остатком — даже удлинитель — попадает под первую часть независимо от категории. Скобки возвращают задуманный смысл: category = 'lamps' AND (stock = 0 OR discontinued = 1). Правило: смешиваешь AND и OR — всегда ставь скобки, даже если помнишь приоритет, так запрос читается однозначно. А перед UPDATE на проде — SELECT с тем же WHERE: удлинитель в выдаче ты бы заметил сразу.",
     ["Какие строки попали под UPDATE? Посмотри на удлинитель: какая часть условия для него истинна?",
      "Что выполняется раньше — AND или OR?",
      "Сгруппируй два условия про склад и производство скобками."],
     code=SQ4_BUGGY, language="sql",
     correct_answer=SQ4_FIXED,
     test_cases=[{"input": None, "expected": [[1, 0], [2, 0], [3, 1], [4, 1], [5, 1], [6, 1]]}])

task(SQ, 5, "incident", 3, "devops_colleague",
     "Дима: «Алерт: за пять минут 300 заказов, и все по 990 ₽ — весь каталог стоит 990. Глянь историю своей psql-сессии на проде, вот она. Ты вроде хотел поменять цену одной лампы?»",
     "Выбери все правильные действия.",
     "UPDATE без WHERE изменил все 48 213 товаров, а psql работает в autocommit — изменение зафиксировалось сразу, поэтому ROLLBACK ответил «нет транзакции» и ничего не вернул; никакие права администратора этого не изменят. Первое — честно сообщить команде, что именно выполнено: без этого Дима будет искать причину вслепую. Второе — остановить ущерб: каждая минута с неверными ценами — новые убыточные заказы, поэтому оформление закрывают флагом или режимом обслуживания. Третье — восстановить данные из бэкапа: копия базы на момент до ошибки (PITR) поднимается рядом, и цены переносятся одним UPDATE по id — «по памяти» 48 тысяч цен не восстановить. Что делать с уже оформленными заказами, решает бизнес (Стас), а не удаление данных. После инцидента — постмортем без поиска виноватых и правило: ручные правки на проде только в BEGIN … COMMIT после проверочного SELECT, а лучше — скриптом через ревью.",
     ["Посмотри, что psql ответил на ROLLBACK. Была ли открыта транзакция?",
      "Сначала — остановить ущерб и сообщить, потом чинить данные, потом делать выводы.",
      "Откуда можно взять правильные цены на 48 тысяч товаров?"],
     code=D("""
        lampovy_prod=> SELECT id, name, price FROM products WHERE id = 812;
         id  |           name           | price
        -----+--------------------------+-------
         812 | Настольная лампа «Ампир» |  1290
        (1 row)

        lampovy_prod=> UPDATE products SET price = 990;
        UPDATE 48213
        lampovy_prod=> ROLLBACK;
        WARNING:  there is no transaction in progress
        ROLLBACK

        [12:41] ALERT orders_per_minute = 61 (обычно 4)
        [12:41] ALERT avg_order_total = 1 050 ₽ (обычно 6 800 ₽)
     """),
     language="text",
     options=[
         "Сразу написать в канал инцидентов и Диме, какую команду выполнил и во сколько",
         "Остановить ущерб: закрыть оформление заказов фиче-флагом или режимом обслуживания",
         "Поднять копию базы на момент до ошибки (PITR) и перенести цены обратно одним UPDATE по id",
         "После инцидента: ручные правки на проде — только в BEGIN … COMMIT после проверочного SELECT",
         "Выполнить ROLLBACK ещё раз от суперпользователя postgres — у него откат сработает",
         "Удалить заказы за последние пять минут, чтобы убытка не было, и извиниться перед клиентами",
         "Восстановить цены вручную: выгрузить прайсы поставщиков и пересчитать наценку по формуле",
     ],
     correct_answer=[0, 1, 2, 3],
     time_limit=15)


# =====================================================================
# be-orm
# =====================================================================
OR = "be-orm"

topic(OR, """
    ORM (Object-Relational Mapping) связывает таблицы с классами: строка — объект, столбец — атрибут. Ты пишешь на Python, ORM строит SQL. Главные ORM в Python — SQLAlchemy (FastAPI-проекты, «Ламповый Маркет») и Django ORM.

    Модели в SQLAlchemy 2.0:

        class Base(DeclarativeBase):
            pass

        class Product(Base):
            __tablename__ = "products"
            id: Mapped[int] = mapped_column(primary_key=True)
            name: Mapped[str] = mapped_column(String(200))
            price: Mapped[int]
            discount: Mapped[int | None]     # допускает NULL
            reviews: Mapped[list["Review"]] = relationship(back_populates="product")

        class Review(Base):
            __tablename__ = "reviews"
            id: Mapped[int] = mapped_column(primary_key=True)
            product_id: Mapped[int] = mapped_column(ForeignKey("products.id"))
            product: Mapped["Product"] = relationship(back_populates="reviews")

    ForeignKey — связь в самой базе, relationship — удобный доступ из Python: review.product, product.reviews. relationship без внешнего ключа не заработает.

    Сессия. Engine держит пул соединений, Session — единица работы: копит изменения и отправляет их в базу.
    • session.add(obj) — пометить к сохранению; session.commit() — записать и зафиксировать транзакцию. Без commit при закрытии сессии всё откатится. id у нового объекта появляется после flush или commit.
    • session.rollback() — отменить незафиксированное; после ошибки в транзакции без него сессией пользоваться нельзя.
    • session.get(Product, 812) — поиск по первичному ключу, None если нет.
    • В FastAPI сессия живёт один запрос: зависимость открывает её и закрывает в finally, даже если ручка упала.

        def get_db():
            db = SessionLocal()
            try:
                yield db
            finally:
                db.close()

    Запросы:

        stmt = (select(Product)
                .where(Product.category == "lamps", Product.price < 2000)
                .order_by(Product.price.desc())
                .limit(20))
        products = session.scalars(stmt).all()

    • Несколько условий в where() объединяются через AND; для OR — or_(a, b).
    • Операторы Python and, or, not, in, is ORM перехватить не может. Пиши and_/or_, Product.id.in_([1, 2]), Product.discount.is_(None).
    • select() только строит запрос, в базу он уходит при session.scalars() или execute(). echo=True у engine печатает весь SQL в лог.
    • Сырой SQL — только с параметрами: text("SELECT … WHERE name = :name") и {"name": q}. Никогда не собирай его f-строкой — это SQL-инъекция.
    • Связи по умолчанию ленивые: product.reviews в цикле по товарам — отдельный запрос на каждый товар (проблема N+1, у неё своя тема).

    Django ORM — то же другими словами: Product.objects.filter(category="lamps", price__lt=2000).order_by("-price")[:20]. Лукапы: __lt, __gte, __in, __icontains, __isnull. QuerySet ленивый — запрос выполняется при итерации.

    Миграции. Схема базы меняется только миграциями — версионированными скриптами в репозитории. В SQLAlchemy это Alembic:

        alembic revision --autogenerate -m "add products.is_archived"
        alembic upgrade head

    autogenerate сравнивает модели с базой, но может ошибаться: переименование колонки он увидит как удаление и добавление — с потерей данных. Поэтому миграцию всегда читают глазами. Base.metadata.create_all() создаёт только отсутствующие таблицы и никогда не меняет существующие — годится для тестов, не для прода. В Django: makemigrations и migrate.
""")

task(OR, 1, "quiz", 1, "teamlead",
     "Гена: «Прежде чем писать запросы через SQLAlchemy, научись читать их как SQL — в логах с echo=True ты увидишь именно SQL, а не Python. Что уйдёт в базу вот отсюда?»",
     "Какой SQL выполнит этот код (база — PostgreSQL)?",
     "Несколько условий в where() через запятую SQLAlchemy соединяет через AND. .desc() превращается в ORDER BY … DESC прямо в SQL, а limit(5) — в LIMIT: фильтрацию, сортировку и ограничение делает база, а не Python. Значения уходят отдельными параметрами (%(category_1)s), а не вклеиваются в текст запроса — это защита от SQL-инъекций. select() только описывает запрос, выполняет его session.scalars(); commit нужен для записи изменений, а не для чтения. Вместо * ORM перечисляет колонки модели — поэтому они видны в логе.",
     ["Как SQLAlchemy соединяет несколько условий, переданных в where() через запятую?",
      "Где выполняются сортировка и LIMIT — в базе или в Python?"],
     code=D("""
        stmt = (
            select(Product)
            .where(Product.category == "lamps", Product.price < 2000)
            .order_by(Product.price.desc())
            .limit(5)
        )
        products = session.scalars(stmt).all()
     """),
     language="python",
     options=[
         "SELECT * FROM products WHERE category = 'lamps' OR price < 2000 ORDER BY price DESC LIMIT 5",
         "SELECT products.id, products.name, products.category, products.price FROM products WHERE products.category = %(category_1)s AND products.price < %(price_1)s ORDER BY products.price DESC LIMIT %(param_1)s",
         "SELECT products.id, products.name, products.category, products.price FROM products WHERE products.category = %(category_1)s AND products.price < %(price_1)s LIMIT %(param_1)s — сортировку по убыванию SQLAlchemy делает в Python",
         "Ничего не уйдёт: без session.commit() запрос не выполняется",
         "SELECT products.id, products.name, products.category, products.price FROM products — фильтр, сортировку и лимит SQLAlchemy применяет к объектам в Python",
     ],
     correct_answer=1)

task(OR, 2, "find_bug", 2, "qa",
     "Ира: «Страница “Неоплаченные заказы” в личном кабинете всегда пустая, хотя в базе у тестового пользователя три неоплаченных заказа. Ошибок в логах нет, ответ 200 и пустой список».",
     "Почему unpaid_orders всегда возвращает пустой список? Выбери правильный диагноз и исправление.",
     "Order.user_id == user_id возвращает не bool, а объект SQL-выражения. Но and — оператор самого Python: он вызывает bool() у выражения, и SQLAlchemy для == отвечает простым False (сравнение по идентичности объектов). Order.paid_at is None — тоже чистый Python, всегда False, ведь атрибут модели не None. В итоге в where() попадает обычный False, и в базу уходит WHERE false — ошибки нет, строк нет. Правильно: условия через запятую (или and_()), а проверка на NULL — .is_(None). Замена is None на == None сама по себе не поможет: and всё равно превратит условие в False. commit нужен для записи, а не для чтения; scalars возвращает объекты Order из каждой строки, а не одну строку; а user_id в функцию приходит числом (user_id: int), так что типы тут ни при чём.",
     ["Что делает Python с выражением A and B — и может ли библиотека это перехватить?",
      "Чему равно Order.paid_at is None, если Order.paid_at — атрибут класса модели?",
      "В SQLAlchemy условия передают в where() через запятую, а NULL проверяют через .is_(None)."],
     code=D("""
        from sqlalchemy import select
        from sqlalchemy.orm import Session

        from market.models import Order


        def unpaid_orders(session: Session, user_id: int) -> list[Order]:
            stmt = (
                select(Order)
                .where(Order.user_id == user_id and Order.paid_at is None)
                .order_by(Order.created_at)
            )
            return list(session.scalars(stmt))
     """),
     language="python",
     options=[
         "and и is None вычисляет сам Python, и в where() уходит просто False. Нужно .where(Order.user_id == user_id, Order.paid_at.is_(None))",
         "Перед select не хватает session.commit(): без него сессия не видит заказы, созданные другими транзакциями",
         "Order.paid_at is None — проверка Python; достаточно заменить её на Order.paid_at == None, и SQLAlchemy построит IS NULL",
         "session.scalars возвращает только первую колонку первой строки — нужно session.execute(stmt).all()",
         "user_id приходит из URL строкой, а в базе он int, поэтому сравнение Order.user_id == user_id не совпадает",
     ],
     correct_answer=0)

task(OR, 3, "architecture", 2, "devops_colleague",
     "Дима: «Гене нужна колонка products.is_archived: NOT NULL, по умолчанию false. В таблице 48 тысяч товаров, есть прод и стейдж, у пяти разработчиков свои локальные базы. Как вносим изменение схемы?»",
     "Какой способ правильный?",
     "Миграция в репозитории — единственный вариант, который воспроизводим везде: один и тот же скрипт проходит ревью вместе с кодом, применяется на локальных базах, стейдже и проде, а таблица alembic_version помнит, что уже применено. Для NOT NULL-колонки в непустой таблице нужен server_default, иначе ALTER упадёт на существующих строках. create_all не делает ALTER — он создаёт только отсутствующие таблицы, так что колонка просто не появится и приложение упадёт на первом запросе. Ручной ALTER в psql приводит к дрейфу схем: у пяти разработчиков и на стейдже базы разойдутся, а истории изменений не будет. Пересоздание таблицы с перезаливкой — это простой, риск потерять данные и сломать внешние ключи, и его всё равно придётся повторять на каждой базе. JSON-поле обходит схему ценой типов, ограничений NOT NULL и нормальных индексов.",
     ["Изменение должно одинаково попасть в шесть с лишним баз и пройти ревью.",
      "Что на самом деле делает create_all с уже существующей таблицей?",
      "Схема должна жить в репозитории рядом с кодом, как и всё остальное."],
     options=[
         "Поле в модель + Alembic-миграция (--autogenerate) с server_default: проверить глазами, закоммитить с кодом, upgrade head при деплое",
         "Добавить поле в модель: Base.metadata.create_all(engine) при старте приложения сам добавит недостающую колонку в существующую таблицу products",
         "Выполнить ALTER TABLE руками в psql на проде и стейдже, потом поправить модель и разослать SQL разработчикам",
         "Сделать бэкап, пересоздать products через create_all и залить данные обратно — схема точно совпадёт с моделью",
         "Не менять схему: хранить is_archived в JSON-поле extra, которое уже есть у товаров, — миграция не понадобится",
     ],
     correct_answer=0)

OR4_ANSWER = D("""
    def matches(value, lookup, arg):
        if lookup == "isnull":
            return (value is None) == arg
        if lookup == "exact":
            return value == arg
        if value is None:
            return False
        if lookup == "lt":
            return value < arg
        if lookup == "lte":
            return value <= arg
        if lookup == "gt":
            return value > arg
        if lookup == "gte":
            return value >= arg
        if lookup == "icontains":
            return arg.lower() in value.lower()
        if lookup == "in":
            return value in arg
        raise ValueError(f"unknown lookup: {lookup}")


    def filter_rows(rows, filters):
        result = []
        for row in rows:
            ok = True
            for key, arg in filters.items():
                field, _, lookup = key.partition("__")
                if not lookup:
                    lookup = "exact"
                if not matches(row.get(field), lookup, arg):
                    ok = False
                    break
            if ok:
                result.append(row["id"])
        return sorted(result)
""")

BAKERY = [
    {"id": 3, "name": "Бородинский", "category": "bread", "price": 110, "allergens": None},
    {"id": 1, "name": "Багет классический", "category": "bread", "price": 90, "allergens": None},
    {"id": 5, "name": "Багет с сыром", "category": "bread", "price": 150, "allergens": "milk"},
    {"id": 2, "name": "Круассан с миндалём", "category": "pastry", "price": 140, "allergens": "nuts"},
    {"id": 6, "name": "Эклер", "category": "dessert", "price": None, "allergens": "milk"},
    {"id": 4, "name": "Синнабон", "category": "pastry", "price": 190, "allergens": "milk"},
]

task(OR, 4, "write_code", 3, "teamlead",
     "Гена: «Сайт “Батона” у нас на Django. Чтобы unit-тесты сервиса каталога бегали за секунды, мы подменяем базу фейковым репозиторием на словарях — и он должен понимать те же фильтры, что Product.objects.filter(). Допиши разбор лукапов».",
     T("""
        Напиши filter_rows(rows, filters): rows — список словарей-строк, filters — словарь в стиле Django ORM. Верни список id подходящих строк по возрастанию. Ключ фильтра — «поле» или «поле__лукап»:
        • без лукапа (price) — точное равенство, как exact;
        • __lt, __lte, __gt, __gte — меньше, не больше, больше, не меньше;
        • __icontains — подстрока без учёта регистра;
        • __in — значение входит в список;
        • __isnull — True: значение None, False: не None.
        Все условия соединяются через AND, как аргументы filter(). Пустой filters — все строки. Строка, где значение поля None, проходит только exact с None и isnull — как NULL в SQL.

        Пример: filter_rows(rows, {"price__lt": 150, "allergens__isnull": True}) — дешёвые позиции без аллергенов.
     """),
     "Лукап — это имя поля и оператор через двойное подчёркивание; partition(\"__\") отделяет их за один вызов, а отсутствие лукапа означает exact. Каждый лукап — это кусок SQL: price__lt=150 превращается в WHERE price < 150, category__in — в IN (…), allergens__isnull=True — в IS NULL, а name__icontains в PostgreSQL — в UPPER(name) LIKE UPPER('%…%'). Поэтому фейк честно повторяет семантику NULL: эклер без цены не проходит ни «меньше», ни «больше» — как и в базе, где сравнение с NULL даёт «неизвестно». Django так же превращает filter(price=None) в IS NULL, поэтому exact проверяется до отсечения None. Все условия одного filter() — это AND. Неизвестный лукап лучше сразу уронить с ошибкой, чем молча пропустить: иначе тест будет зелёным на неправильном фильтре.",
     ["Раздели ключ на поле и лукап: key.partition(\"__\"). Пустой лукап — это exact.",
      "Вынеси проверку одного условия во вспомогательную функцию matches(value, lookup, arg).",
      "Сначала обработай isnull и exact, потом: если значение None — строка не подходит; в конце отсортируй id."],
     code=D("""
        def filter_rows(rows, filters):
            # верни отсортированный список id строк, подходящих под все фильтры
            pass
     """),
     language="python", entry="filter_rows",
     correct_answer=OR4_ANSWER,
     test_cases=ref_cases(OR4_ANSWER, "filter_rows", [
         [BAKERY, {}],
         [BAKERY, {"category": "bread"}],
         [BAKERY, {"price__lt": 150}],
         [BAKERY, {"price__lte": 150, "allergens__isnull": True}],
         [BAKERY, {"name__icontains": "БАГЕТ"}],
         [BAKERY, {"category__in": ["pastry", "dessert"], "price__gte": 150}],
         [BAKERY, {"allergens__isnull": False, "price__gt": 100}],
         [BAKERY, {"price": 190}],
     ]))

task(OR, 5, "code_review", 3, "teamlead",
     "Марина: «PR с отзывами на товары Маркета: модель, эндпоинт добавления и поиск по тексту. Автор говорит, что “локально всё работает”. Отметь то, что нельзя мёрджить».",
     "Выбери все замечания, которые действительно нужно оставить.",
     "В add_review нет db.commit(): add только ставит объект в очередь, без flush у него нет id, а при закрытии сессии незафиксированная транзакция откатывается — клиент получит 201 и {\"id\": null}, а отзыв не сохранится. Поиск собирает SQL f-строкой из пользовательского q — классическая SQL-инъекция: запрос с кавычкой в q меняет смысл SQL; нужны bind-параметры или выражения ORM. В get_db close() стоит после yield без finally: если ручка упадёт, код после yield не выполнится, сессия и соединение повиснут до сборщика мусора, и под нагрузкой пул закончится. ForeignKey обязателен — relationship опирается на него; nullable-текст отзыва — нормальное решение, а индекс по product_id ускоряет выборку отзывов товара. sessionmaker создаётся один раз на приложение, сессия — на запрос.",
     ["Что должно случиться, чтобы данные из session.add реально оказались в базе?",
      "Откуда берётся q и что будет, если в нём окажется кавычка?",
      "Выполнится ли db.close(), если внутри ручки вылетит исключение?"],
     code=D('''
        # market/db.py
        engine = create_engine(settings.database_url)
        SessionLocal = sessionmaker(bind=engine)


        def get_db():
            db = SessionLocal()
            yield db
            db.close()


        # market/reviews/models.py
        class Review(Base):
            __tablename__ = "reviews"

            id: Mapped[int] = mapped_column(primary_key=True)
            product_id: Mapped[int] = mapped_column(ForeignKey("products.id"), index=True)
            rating: Mapped[int]
            text: Mapped[str | None]

            product: Mapped["Product"] = relationship(back_populates="reviews")


        # market/reviews/api.py
        @router.post("/products/{product_id}/reviews", status_code=201)
        def add_review(product_id: int, data: ReviewIn, db: Session = Depends(get_db)):
            review = Review(product_id=product_id, **data.model_dump())
            db.add(review)
            return {"id": review.id}


        @router.get("/products/{product_id}/reviews/search")
        def search_reviews(product_id: int, q: str, db: Session = Depends(get_db)):
            sql = f"SELECT id, rating, text FROM reviews WHERE product_id = {product_id} AND text LIKE '%{q}%'"
            rows = db.execute(text(sql)).all()
            return [dict(row._mapping) for row in rows]
     '''),
     language="python",
     options=[
         "В add_review нет db.commit(): отзыв не сохранится, а review.id будет None",
         "search_reviews собирает SQL f-строкой из q — SQL-инъекция. Нужны bind-параметры (:pattern) или Review.text.contains(q)",
         "get_db закрывает сессию не в finally: если ручка упадёт, соединение не вернётся в пул",
         "ForeignKey лишний: связь уже описана через relationship, и SQLAlchemy сам поймёт, по какой колонке джойнить",
         "Mapped[str | None] нужно заменить на Mapped[str]: NULL в текстовых полях ломает поиск через LIKE",
         "index=True на product_id лишний: индекс замедляет каждую вставку отзыва, а таблица пока маленькая",
         "sessionmaker нужно создавать заново в каждом запросе, иначе сессии разных пользователей перемешаются",
     ],
     correct_answer=[0, 1, 2])


# =====================================================================
if __name__ == "__main__":
    shuffle_options(TASKS)
    data = {"track": "backend", "part": "b2", "topics": TOPICS, "tasks": TASKS}
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("written", os.path.abspath(OUT), "topics", len(TOPICS), "tasks", len(TASKS))
