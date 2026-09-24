// Объяснение строк кода, запуск, проверка задач (stdio и функции) и пошаговая отладка.
//
// ===== API проверки задач =====
// Все вызовы синхронные: код выполняется в отдельном потоке с большим стеком (PyRun.OnBigStack), вызывающий поток ждёт.
// Бесконечные циклы останавливает лимит шагов (maxSteps — инструкции и элементы «ленивых» коллекций) → PyError TimeoutError.
//
// 1) Программы (stdin/stdout), как раньше:
//      List<CheckResult> PyRun.Check(string code, IList<TestCase> tests, int maxSteps = 1000000)
//    TestCase.inputs — строки ввода, TestCase.expected — точный вывод (сравнение без хвостовых пробелов и пустых строк по краям,
//    как norm_out в content/spec/validate.py). Подсказки input("...") тоже попадают в вывод — как в CPython.
//    Каждый тест — отдельный запуск программы. Упала программа (Error != null) — тест не пройден.
//
// 2) Функции (задачи с content.entry):
//      List<CheckResult> PyRun.CheckFunction(string code, string entry, IList<FuncTest> tests, int maxSteps = 1000000)
//    Как run_py_func в validate.py: код модуля выполняется ОДИН раз (print при загрузке не мешает), затем для каждого теста
//    вызывается entry(...). Состояние модуля (глобальные переменные, изменяемые значения по умолчанию) сохраняется между тестами,
//    аргументы для каждого теста создаются заново. Лимит шагов — на загрузку и на каждый тест отдельно.
//    FuncTest.input: List<object>/object[] — раскладывается в позиционные аргументы (entry(*input)); любое другое значение —
//    единственный аргумент. FuncTest.expected — ожидаемый результат.
//    Значения — JSON-подобные C#: long/int, double/float, string, bool, null, List<object>/object[], Dictionary<string, object>
//    (и BigInteger для больших целых).
//    Сравнение (как eq() в validate.py): кортежи = списки, множества = отсортированные списки, ключи словарей приводятся к str,
//    float с допуском 1e-6 (math.isclose(rel_tol=1e-6, abs_tol=1e-6)), 2 == 2.0, bool не равен числу (True != 1),
//    объекты своих классов сравниваются как str(obj).
//    На каждый тест — CheckResult:
//      Passed     — прошёл ли тест;
//      InputsText — вызов в виде Python: "entry(1, [2, 3])";
//      Expected   — repr ожидаемого значения; Actual — repr полученного (например "(2, 'a')", "0.30000000000000004");
//      Got        — полученное значение в JSON-подобном виде (как его увидел бы validate.py);
//      Error      — PyError, если функция упала или код не загрузился (тип как в CPython, Ru — объяснение по-русски, Line — строка);
//      Note       — короткое пояснение для игрока (что не так).
//
// 3) Прямо из JSON задачи (content.test_cases), для обоих режимов:
//      List<CheckResult> PyRun.CheckJson(string code, string entry, string testCasesJson, int maxSteps = 1000000)
//    entry пустой/null — stdio-режим: input — строка ввода (строки через \n), expected — строка вывода.
//    Иначе — проверка функции entry. Разбор JSON — TestJson.Parse (без зависимостей от Unity).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;

namespace Intern.Py
{
    // ================== Объяснятель ==================
    public static class Explainer
    {
        public static List<KeyValuePair<int, string>> ExplainProgram(string code, out PyError error)
        {
            error = null;
            var res = new List<KeyValuePair<int, string>>();
            try
            {
                var prog = Parser.ParseProgram(code);
                Walk(prog, res);
            }
            catch (PyError e) { error = e; }
            return res.OrderBy(x => x.Key).ToList();
        }

        static void Walk(List<Stmt> body, List<KeyValuePair<int, string>> res)
        {
            if (body == null) return;
            foreach (var s in body)
            {
                res.Add(new KeyValuePair<int, string>(s.Line, Explain(s)));
                if (s is IfS) { Walk(((IfS)s).Body, res); Walk(((IfS)s).Else, res); }
                if (s is WhileS) { Walk(((WhileS)s).Body, res); Walk(((WhileS)s).OrElse, res); }
                if (s is ForS) { Walk(((ForS)s).Body, res); Walk(((ForS)s).OrElse, res); }
                if (s is DefS) Walk(((DefS)s).Body, res);
                if (s is ClassS) Walk(((ClassS)s).Body, res);
                if (s is TryS)
                {
                    var t = (TryS)s;
                    Walk(t.Body, res);
                    foreach (var h in t.Handlers)
                    {
                        res.Add(new KeyValuePair<int, string>(h.Line, ExplainHandler(h)));
                        Walk(h.Body, res);
                    }
                    Walk(t.Else, res); Walk(t.Finally, res);
                }
            }
        }

        static string ExplainHandler(Handler h)
        {
            if (h.Type == null) return "except без типа ловит ЛЮБУЮ ошибку. Так лучше не делать: можно случайно спрятать настоящий баг. Указывай конкретный тип, например except ValueError.";
            return "Сюда попадём, если в блоке try случилась ошибка " + Code(h.Type) + (h.Name != null ? "; сама ошибка (объект исключения) будет в переменной " + h.Name + " — например, print(" + h.Name + ") покажет её текст" : "") +
                ". Программа не упадёт, а выполнит этот блок.";
        }

        public static string Explain(Stmt s)
        {
            if (s is AssignS)
            {
                var a = (AssignS)s;
                string tgt = string.Join(" и ", a.Targets.Select(Code).ToArray());
                var v = a.Value;
                if (a.Targets[0] is TupleE && v is TupleE)
                    return "Одновременное присваивание: сначала вычисляются все значения справа (" + Code(v) + "), потом раскладываются по переменным слева (" + tgt + "). Так удобно менять значения местами.";
                if (a.Targets[0] is TupleE && ((TupleE)a.Targets[0]).Items.Any(x => x is StarredE))
                    return "Распаковка со звёздочкой: переменные без * получают по одному элементу, а переменная со * — список всех остальных.";
                if (a.Targets[0] is TupleE)
                    return "Распаковка: значение справа — это набор из нескольких элементов, они по очереди раскладываются в " + tgt + ".";
                if (a.Targets[0] is IndexE)
                    return "Записываем значение " + Code(v) + " в " + Code(a.Targets[0]) + " — меняем элемент списка или словаря по индексу/ключу.";
                if (a.Targets[0] is AttrE && Code(((AttrE)a.Targets[0]).Obj) == "self")
                    return "Сохраняем " + Code(v) + " в атрибут объекта " + Code(a.Targets[0]) + ". Атрибуты живут вместе с объектом и доступны во всех его методах.";
                if (IsCall(v, "input"))
                    return "input() ждёт, пока пользователь что-то введёт, и возвращает это как СТРОКУ. Результат кладём в переменную " + tgt + ".";
                if ((IsCall(v, "int") || IsCall(v, "float")) && ((CallE)v).Args.Count == 1 && IsCall(((CallE)v).Args[0], "input"))
                    return "Читаем ввод через input() и сразу превращаем строку в число через " + ((NameE)((CallE)v).F).Id + "(). Число кладём в " + tgt + ".";
                if (IsMethodCall(v, "split"))
                    return "split() разрезает строку на части (по пробелам, если не указано иное) и возвращает список. Список кладём в " + tgt + ".";
                if (v is CompE)
                {
                    var c = (CompE)v;
                    string what = c.Kind == "dict" ? "словарь" : c.Kind == "set" ? "множество" : c.Kind == "gen" ? "генератор (значения считаются по одному, по мере надобности)" : "список";
                    return "Генератор: проходим по " + Code(c.Fors[0].Iter) + (c.Fors.Any(f => f.Ifs.Count > 0) ? " (с фильтром if)" : "") + " и для каждого элемента собираем " +
                        (c.Kind == "dict" ? Code(c.Elt) + ": " + Code(c.Val) : Code(c.Elt)) + " в новый " + what + " " + tgt + ".";
                }
                if (v is LambdaE) return "lambda — короткая безымянная функция: " + Code(v) + ". Кладём её в " + tgt + " — теперь её можно вызывать как " + tgt + "(...).";
                if (v is ListE && ((ListE)v).Items.Count == 0) return "Создаём пустой список и кладём его в " + tgt + ". Потом в него можно добавлять элементы через .append().";
                if (v is DictE && ((DictE)v).Keys.Count == 0) return "Создаём пустой словарь " + tgt + ". Словарь хранит пары «ключ: значение».";
                if (IsCall(v, "set") && ((CallE)v).Args.Count == 0) return "Создаём пустое множество " + tgt + ". Множество хранит уникальные значения (повторы выкидываются). Пустое множество — только set(), потому что {} — это словарь.";
                if (v is ListE) return "Создаём список из " + ((ListE)v).Items.Count + " элемент(ов) и кладём его в " + tgt + ".";
                if (v is DictE) return "Создаём словарь с парами «ключ: значение» и кладём его в " + tgt + ".";
                if (v is SetE) return "Создаём множество (уникальные значения без порядка) и кладём его в " + tgt + ".";
                if (v is ConstE) return "Создаём переменную " + tgt + " и кладём в неё " + DescribeConst(((ConstE)v).V) + ". Если переменная уже была — старое значение заменится.";
                if (v is FStrE) return "Собираем строку через f-строку: всё в {фигурных скобках} заменяется значениями. Результат кладём в " + tgt + ".";
                if (v is CallE && ((CallE)v).F is NameE && char.IsUpper(((NameE)((CallE)v).F).Id[0]))
                    return "Создаём объект класса " + ((NameE)((CallE)v).F).Id + " (при этом выполняется его __init__) и кладём его в " + tgt + ".";
                return "Вычисляем выражение справа от = (" + Code(v) + ") и кладём результат в переменную " + tgt + ".";
            }
            if (s is AnnAssignS)
            {
                var a = (AnnAssignS)s;
                return "Переменная " + Code(a.Target) + " с подсказкой типа " + Code(a.Annotation) + ". Python тип не проверяет — подсказка для читателя и редактора." +
                    (a.Value != null ? " Кладём в неё " + Code(a.Value) + "." : "");
            }
            if (s is AugAssignS)
            {
                var a = (AugAssignS)s; string t = Code(a.Target), v = Code(a.Value);
                switch (a.Op)
                {
                    case "+": return "Увеличиваем " + t + " на " + v + ". Это короткая запись для " + t + " = " + t + " + " + v + ".";
                    case "-": return "Уменьшаем " + t + " на " + v + ". Короткая запись для " + t + " = " + t + " - " + v + ".";
                    case "*": return "Умножаем " + t + " на " + v + " и сохраняем результат обратно в " + t + ".";
                    case "/": return "Делим " + t + " на " + v + " и сохраняем результат (дробное число) обратно в " + t + ".";
                    default: return "Короткая запись для " + t + " = " + t + " " + a.Op + " " + v + ".";
                }
            }
            if (s is ExprS)
            {
                var e = ((ExprS)s).E;
                if (IsCall(e, "print"))
                {
                    var c = (CallE)e;
                    if (c.Args.Count == 0) return "print() без аргументов выводит пустую строку.";
                    return "Выводим на экран: " + string.Join(", ", c.Args.Select(Code).ToArray()) +
                        (c.Args.Count > 1 ? ". Несколько значений через запятую печатаются через пробел." : ".") +
                        (c.Args.Any(x => x is FStrE) ? " В f-строке всё внутри {} заменяется значениями." : "");
                }
                if (e is ConstE && ((ConstE)e).V is string) return "Строка документации (docstring): описание того, что делает функция или класс. На выполнение не влияет.";
                if (e is ConstE && ((ConstE)e).V is PyEllipsis) return "... (многоточие) — заглушка «здесь пока ничего нет», как pass.";
                if (IsMethodCall(e, "append")) { var c = (CallE)e; return "Добавляем " + Code(c.Args.FirstOrDefault()) + " в конец списка " + Code(((AttrE)c.F).Obj) + "."; }
                if (IsMethodCall(e, "sort")) return "Сортируем список " + Code(((AttrE)((CallE)e).F).Obj) + " прямо на месте (он сам изменится, а sort() вернёт None).";
                if (IsMethodCall(e, "pop")) return "Удаляем элемент из " + Code(((AttrE)((CallE)e).F).Obj) + ".";
                if (e is CallE && ((CallE)e).F is AttrE && Code(((AttrE)((CallE)e).F).Obj) == "super()") return "Вызываем метод родительского класса через super() — чтобы не дублировать его код.";
                if (e is CallE) return "Вызываем " + Code(e) + ". Результат вызова тут никуда не сохраняется.";
                return "Вычисляем " + Code(e) + ", но результат никуда не сохраняется и не выводится — эта строка, скорее всего, ничего не делает. Может, забыл print() или = ?";
            }
            if (s is IfS)
            {
                var i = (IfS)s;
                string head = i.IsElif ? "elif: если все условия выше оказались ложными, проверяем " : "Проверяем условие ";
                string tail = " Если оно истинно (True) — выполняются строки с отступом ниже.";
                if (i.Else != null && i.Else.Count == 1 && i.Else[0] is IfS && ((IfS)i.Else[0]).IsElif) tail += " Иначе переходим к следующему elif.";
                else if (i.Else != null) tail += " Иначе — выполняется блок после else.";
                else tail += " Иначе блок просто пропускается.";
                if (i.Cond is CompareE && ((CompareE)i.Cond).L is NameE && ((NameE)((CompareE)i.Cond).L).Id == "__name__")
                    return "Классическая проверка «запущен ли файл как программа»: код ниже выполнится при запуске, но не при импорте файла как модуля.";
                return head + Code(i.Cond) + "." + tail + CondHint(i.Cond);
            }
            if (s is WhileS)
            {
                var w = (WhileS)s;
                if (w.Cond is ConstE && Equals(((ConstE)w.Cond).V, true))
                    return "Бесконечный цикл while True: повторяется вечно, пока внутри не сработает break (или return).";
                return "Цикл while: пока условие " + Code(w.Cond) + " истинно, повторяем блок с отступом. Условие проверяется перед каждым повтором — не забудь менять переменные внутри, иначе цикл станет бесконечным." +
                    (w.OrElse != null ? " Блок else после цикла выполнится, только если цикл закончился без break." : "");
            }
            if (s is ForS)
            {
                var f = (ForS)s; string t = Code(f.Target);
                string tail = f.OrElse != null ? " Блок else после цикла выполнится, только если цикл дошёл до конца без break." : "";
                if (IsCall(f.Iter, "range"))
                {
                    var args = ((CallE)f.Iter).Args.Select(Code).ToArray();
                    string range = args.Length == 1 ? "от 0 до " + args[0] + " - 1" :
                                   args.Length == 2 ? "от " + args[0] + " до " + args[1] + " - 1" :
                                   "от " + (args.Length > 0 ? args[0] : "?") + " до " + (args.Length > 1 ? args[1] : "?") + " (не включая) с шагом " + (args.Length > 2 ? args[2] : "1");
                    return "Цикл for: переменная " + t + " по очереди принимает числа " + range + ", и для каждого числа выполняется блок с отступом. Последнее число range() не включается!" + tail;
                }
                if (IsCall(f.Iter, "enumerate"))
                    return "Цикл с enumerate(): на каждом шаге получаем пару (номер, элемент) и раскладываем её в " + t + ". Удобно, когда нужен и индекс, и значение." + tail;
                if (IsCall(f.Iter, "zip"))
                    return "Цикл с zip(): идём сразу по нескольким коллекциям параллельно, на каждом шаге берём по элементу из каждой и кладём в " + t + "." + tail;
                if (IsMethodCall(f.Iter, "items"))
                    return "Цикл по словарю: на каждом шаге берём пару «ключ, значение» и кладём их в " + t + "." + tail;
                if (f.Target is TupleE) return "Цикл for: на каждом шаге берём очередной элемент из " + Code(f.Iter) + " и раскладываем его в " + t + "." + tail;
                return "Цикл for: по очереди берём каждый элемент из " + Code(f.Iter) + ", кладём его в переменную " + t + " и выполняем блок с отступом." + tail;
            }
            if (s is DefS)
            {
                var d = (DefS)s;
                var names = d.ParamNames();
                string deco = "";
                foreach (var de in d.Decorators)
                {
                    string dn = Code(de);
                    if (dn == "property") deco += " Декоратор @property делает метод свойством: к нему обращаются без скобок — obj." + d.Name + ".";
                    else if (dn.EndsWith(".setter")) deco += " Это сеттер свойства: выполняется при присваивании obj." + d.Name + " = значение (удобно проверять значение).";
                    else if (dn == "staticmethod") deco += " @staticmethod — метод без self: обычная функция, которая лежит внутри класса.";
                    else if (dn == "classmethod") deco += " @classmethod — первым параметром получает сам класс (cls), а не объект. Часто так делают альтернативные конструкторы.";
                    else deco += " Декоратор @" + dn + " оборачивает функцию: " + d.Name + " = " + dn + "(" + d.Name + ").";
                }
                if (d.Name == "__init__")
                    return "Конструктор __init__: выполняется автоматически при создании объекта и заполняет его атрибуты через self.имя = значение." + deco;
                if (d.Name.StartsWith("__") && d.Name.EndsWith("__"))
                    return "Магический метод " + d.Name + ": Python вызывает его сам — " + MagicHint(d.Name) + deco;
                if (names.Count > 0 && (names[0] == "self" || names[0] == "cls"))
                    return "Объявляем метод " + d.Name + "(" + string.Join(", ", names.ToArray()) + "). " + (names[0] == "self" ? "self — это объект, у которого метод вызвали: obj." + d.Name + "(...)." : "cls — это сам класс.") + deco;
                return "Объявляем функцию " + d.Name + "(" + string.Join(", ", names.ToArray()) + "). Код внутри НЕ выполняется сейчас — только когда где-то ниже напишут " + d.Name + "(...)." +
                       (names.Count > 0 ? " Параметры — это переменные, в которые попадут переданные значения." : "") +
                       (names.Any(n => n.StartsWith("*")) ? " *args собирает лишние позиционные аргументы в кортеж, **kwargs — именованные в словарь." : "") + deco;
            }
            if (s is ClassS)
            {
                var c = (ClassS)s;
                string bases = c.Bases.Count > 0 ? " — наследник " + string.Join(", ", c.Bases.Select(Code).ToArray()) + " (получает все его методы и может их переопределить)" : "";
                return "Объявляем класс " + c.Name + bases + ". Класс — это «чертёж» объектов: внутри описаны их методы. Объект создаётся вызовом " + c.Name + "(...), при этом выполняется __init__.";
            }
            if (s is ReturnS)
            {
                var r = (ReturnS)s;
                return r.Value == null ? "Выходим из функции, ничего не возвращая (результат будет None)." :
                    "Возвращаем " + Code(r.Value) + " туда, откуда функцию вызвали. После return функция сразу заканчивается.";
            }
            if (s is TryS)
                return "try: выполняем блок ниже «под присмотром». Если в нём случится ошибка (исключение), программа не упадёт, а перейдёт к подходящему except." +
                    (((TryS)s).Finally != null ? " Блок finally выполнится в любом случае — удобно для «уборки»." : "");
            if (s is RaiseS)
            {
                var r = (RaiseS)s;
                if (r.Exc == null) return "raise без аргументов — выбрасываем пойманное исключение дальше (например, после записи в лог).";
                return "raise — выбрасываем исключение " + Code(r.Exc) + ". Функция сразу прерывается; ошибку можно поймать выше через try/except, иначе программа упадёт.";
            }
            if (s is AssertS) return "assert — проверяем, что " + Code(((AssertS)s).Test) + " истинно. Если нет — программа падает с AssertionError. Так ловят ошибки в логике на ранней стадии.";
            if (s is DelS) return "del — удаляем " + string.Join(", ", ((DelS)s).Targets.Select(Code).ToArray()) + ".";
            if (s is GlobalS) return "global " + string.Join(", ", ((GlobalS)s).Names.ToArray()) + " — внутри функции будем менять глобальную переменную, а не создавать локальную с тем же именем.";
            if (s is NonlocalS) return "nonlocal " + string.Join(", ", ((NonlocalS)s).Names.ToArray()) + " — меняем переменную из внешней функции (замыкание), а не создаём новую.";
            if (s is ImportS)
            {
                var ns = ((ImportS)s).Names;
                var first = ns.Count > 0 ? (ns[0].Value ?? ns[0].Key) : "math";
                string ex = ns.Count > 0 && ns[0].Key == "json" ? first + ".dumps(...)" : ns.Count > 0 && ns[0].Key == "collections" ? first + ".Counter(...)" : ns.Count > 0 && ns[0].Key == "math" ? first + ".sqrt(...)" : first + ".имя";
                return "Подключаем модуль " + string.Join(", ", ns.Select(n => n.Key + (n.Value != null ? " (под именем " + n.Value + ")" : "")).ToArray()) + " — его функции доступны через точку, например " + ex + ".";
            }
            if (s is ImportFromS) { var im = (ImportFromS)s; return "Берём из модуля " + im.Module + " нужные имена: " + (im.Star ? "все" : string.Join(", ", im.Names.Select(n => n.Key).ToArray())) + " — их можно использовать напрямую, без " + im.Module + "."; }
            if (s is BreakS) return "break — досрочно выходим из ближайшего цикла.";
            if (s is ContinueS) return "continue — пропускаем остаток текущего шага цикла и переходим к следующему.";
            if (s is PassS) return "pass — «ничего не делать». Заглушка там, где Python требует хотя бы одну строку.";
            return "";
        }

        static string MagicHint(string n)
        {
            switch (n)
            {
                case "__str__": return "при print(obj) и str(obj). Должен вернуть строку.";
                case "__repr__": return "когда объект печатается внутри списка или в отладчике. Должен вернуть строку, похожую на код создания объекта.";
                case "__eq__": return "при сравнении obj == other.";
                case "__lt__": return "при сравнении obj < other — и поэтому при сортировке.";
                case "__len__": return "при len(obj).";
                case "__contains__": return "при проверке x in obj.";
                case "__getitem__": return "при obj[ключ].";
                case "__setitem__": return "при obj[ключ] = значение.";
                case "__iter__": return "когда по объекту идут циклом for.";
                case "__hash__": return "когда объект кладут в множество или делают ключом словаря.";
                case "__add__": return "при obj + other.";
                case "__call__": return "когда объект вызывают как функцию: obj(...).";
                case "__enter__": case "__exit__": return "в конструкции with.";
            }
            return "в особых ситуациях (оператор или встроенная функция).";
        }

        static string CondHint(Expr c)
        {
            var ce = c as CompareE;
            if (ce != null && ce.Ops.Contains("==")) return " Помни: == сравнивает, а = присваивает.";
            if (ce != null && (ce.Ops.Contains("is") || ce.Ops.Contains("is not")) && !(ce.Rs[0] is ConstE && ((ConstE)ce.Rs[0]).V == null)) return " Осторожно: is проверяет, что это один и тот же объект. Для сравнения значений нужен ==.";
            if (c is BinE && ((BinE)c).Op == "%") return " Остаток от деления, равный 0, считается False!";
            return "";
        }

        static bool IsCall(Expr e, string name)
        {
            var c = e as CallE; return c != null && c.F is NameE && ((NameE)c.F).Id == name;
        }
        static bool IsMethodCall(Expr e, string name)
        {
            var c = e as CallE; return c != null && c.F is AttrE && ((AttrE)c.F).Name == name;
        }

        static string DescribeConst(object v)
        {
            if (v is string) return "текст " + PyOps.Repr(v) + " (строку)";
            if (v is long || v is BigInteger) return "целое число " + PyOps.Repr(v);
            if (v is double) return "дробное число " + PyOps.Repr(v);
            if (v is bool) return PyOps.Repr(v) + " (логическое значение)";
            if (v == null) return "None (пустое значение)";
            return PyOps.Repr(v);
        }

        // Превращаем выражение обратно в код для объяснений
        public static string Code(Expr e)
        {
            if (e == null) return "";
            if (e is ConstE) { var v = ((ConstE)e).V; return v is PyEllipsis ? "..." : PyOps.Repr(v); }
            if (e is NameE) return ((NameE)e).Id;
            if (e is BinE) { var b = (BinE)e; return Wrap(b.L) + " " + b.Op + " " + Wrap(b.R); }
            if (e is UnaryE) { var u = (UnaryE)e; return u.Op == "not" ? "not " + Wrap(u.E) : u.Op + Wrap(u.E); }
            if (e is BoolOpE) { var b = (BoolOpE)e; return Code(b.L) + " " + b.Op + " " + Code(b.R); }
            if (e is CompareE)
            {
                var c = (CompareE)e; var sb = new StringBuilder(Code(c.L));
                for (int i = 0; i < c.Ops.Count; i++) sb.Append(" ").Append(c.Ops[i]).Append(" ").Append(Code(c.Rs[i]));
                return sb.ToString();
            }
            if (e is CallE)
            {
                var c = (CallE)e;
                var args = c.Args.Select(Code).Concat(c.Kw.Select(k => k.Key == null ? "**" + Code(k.Value) : k.Key + "=" + Code(k.Value))).ToArray();
                return Code(c.F) + "(" + string.Join(", ", args) + ")";
            }
            if (e is AttrE) return Code(((AttrE)e).Obj) + "." + ((AttrE)e).Name;
            if (e is IndexE) return Code(((IndexE)e).Obj) + "[" + Code(((IndexE)e).Idx) + "]";
            if (e is SliceE) { var s = (SliceE)e; return Code(s.Lo) + ":" + Code(s.Hi) + (s.Step != null ? ":" + Code(s.Step) : ""); }
            if (e is StarredE) return "*" + Code(((StarredE)e).E);
            if (e is ListE) return "[" + string.Join(", ", ((ListE)e).Items.Select(Code).ToArray()) + "]";
            if (e is SetE) return "{" + string.Join(", ", ((SetE)e).Items.Select(Code).ToArray()) + "}";
            if (e is TupleE) { var t = (TupleE)e; return t.Items.Count == 0 ? "()" : t.Items.Count == 1 ? "(" + Code(t.Items[0]) + ",)" : string.Join(", ", t.Items.Select(Code).ToArray()); }
            if (e is DictE) { var d = (DictE)e; return "{" + string.Join(", ", d.Keys.Select((k, i) => k == null ? "**" + Code(d.Vals[i]) : Code(k) + ": " + Code(d.Vals[i])).ToArray()) + "}"; }
            if (e is IfExpE) { var i = (IfExpE)e; return Code(i.A) + " if " + Code(i.Cond) + " else " + Code(i.B); }
            if (e is LambdaE) { var l = (LambdaE)e; return "lambda " + string.Join(", ", l.Params.Select(p => (p.Kind == 1 ? "*" : p.Kind == 3 ? "**" : "") + p.Name + (p.Default != null ? "=" + Code(p.Default) : "")).ToArray()) + ": " + Code(l.Body); }
            if (e is CompE)
            {
                var c = (CompE)e;
                var sb = new StringBuilder(c.Kind == "dict" ? Code(c.Elt) + ": " + Code(c.Val) : Code(c.Elt));
                foreach (var f in c.Fors)
                {
                    sb.Append(" for ").Append(Code(f.Target)).Append(" in ").Append(Code(f.Iter));
                    foreach (var cond in f.Ifs) sb.Append(" if ").Append(Code(cond));
                }
                return c.Kind == "list" ? "[" + sb + "]" : c.Kind == "gen" ? "(" + sb + ")" : "{" + sb + "}";
            }
            if (e is FStrE)
            {
                var f = (FStrE)e; var sb = new StringBuilder("f\"");
                for (int i = 0; i < f.Parts.Count; i++)
                    sb.Append(f.Parts[i] is string ? (string)f.Parts[i] : "{" + Code((Expr)f.Parts[i]) + (f.Convs[i] != '\0' ? "!" + f.Convs[i] : "") + (f.SpecText[i] != null ? ":" + f.SpecText[i] : "") + "}");
                return sb.Append("\"").ToString();
            }
            return "…";
        }
        static string Wrap(Expr e) { return (e is BinE || e is BoolOpE || e is CompareE || e is IfExpE || e is LambdaE) ? "(" + Code(e) + ")" : Code(e); }
    }

    // ================== Запуск и проверка ==================
    [Serializable]
    public class TestCase
    {
        public string[] inputs = new string[0];
        public string expected = "";
    }

    // Тест функции: input — список аргументов (или одно значение), expected — ожидаемый результат (JSON-подобные значения)
    public class FuncTest
    {
        public object input;
        public object expected;
        public FuncTest() { }
        public FuncTest(object input, object expected) { this.input = input; this.expected = expected; }
    }

    public class RunResult
    {
        public string Console = "";   // всё, что видно в консоли (включая ввод)
        public string Printed = "";   // stdout программы — сравнивается с ожидаемым
        public PyError Error;
        public int Steps;
    }

    public class CheckResult
    {
        public bool Passed;
        public string InputsText, Expected, Actual, Note;
        public PyError Error;
        public object Got;            // для проверки функций: результат в JSON-подобном виде
    }

    public static class PyRun
    {
        // Стек потока интерпретатора. Хватает на 1000 уровней рекурсии Python (с запасом ×2). Больше не нужно:
        // сборщик мусора Mono в сборке игры просматривает весь зарезервированный стек потока, и при 256 МБ
        // каждая сборка мусора во время проверки стоила ~19 с (16 МБ — ~0,08 с).
        public static int StackSize = 16 * 1024 * 1024;

        // Выполнить действие в отдельном потоке с большим стеком (рекурсия в интерпретаторе глубокая)
        public static void OnBigStack(Action a)
        {
            Exception err = null;
            Thread th;
            try
            {
                th = new Thread(() => { try { a(); } catch (Exception e) { err = e; } }, StackSize);
                th.IsBackground = true;
                th.Start();
            }
            catch (Exception) { a(); return; }
            th.Join();
            if (err != null) throw new Exception("PyRun: " + err.Message, err);
        }

        static PyError Internal(Exception e, int line)
        {
            if (e is PyError) return (PyError)e;
            if (e is InsufficientExecutionStackException || e is StackOverflowException)
                return new PyError("RecursionError", "Слишком глубокая рекурсия.", "maximum recursion depth exceeded", line);
            return new PyError("InternalError", "Внутренняя ошибка игры: " + e.GetType().Name + ": " + e.Message + ". Сообщи разработчику :)", line);
        }

        public static RunResult Run(string code, IEnumerable<string> inputs, int maxSteps = 1000000)
        {
            var r = new RunResult();
            var list = inputs == null ? new List<string>() : inputs.ToList();
            OnBigStack(() =>
            {
                var console = new StringBuilder();
                var ip = new Interp { MaxSteps = maxSteps };
                foreach (var s in list) ip.Inputs.Enqueue(s);
                ip.OnOutput = s => console.Append(s);
                try { ip.Run(Parser.ParseProgram(code)); }
                catch (PyError e) { r.Error = e; }
                catch (Exception e) { r.Error = Internal(e, ip.CurrentLine); }
                r.Console = console.ToString(); r.Printed = ip.Printed.ToString(); r.Steps = ip.Steps;
            });
            return r;
        }

        // Нормализация вывода — как norm_out в content/spec/validate.py
        public static string Normalize(string s)
        {
            var lines = (s ?? "").Replace("\r", "").Trim('\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
            return string.Join("\n", lines).Trim();
        }

        public static List<CheckResult> Check(string code, IList<TestCase> tests, int maxSteps = 1000000)
        {
            var res = new List<CheckResult>();
            foreach (var t in tests)
            {
                var r = Run(code, t.inputs, maxSteps);
                var cr = new CheckResult
                {
                    InputsText = t.inputs == null || t.inputs.Length == 0 ? "(без ввода)" : string.Join(" | ", t.inputs),
                    Expected = Normalize(t.expected),
                    Actual = Normalize(r.Printed),
                    Error = r.Error
                };
                cr.Passed = r.Error == null && cr.Actual == cr.Expected;
                if (!cr.Passed) cr.Note = r.Error != null ? "Программа упала с ошибкой " + r.Error.PyType + " в строке " + r.Error.Line + "." : Diff(cr.Expected, cr.Actual);
                res.Add(cr);
            }
            return res;
        }

        static string Diff(string exp, string act)
        {
            if (act.Length == 0) return "Программа ничего не вывела. Не забыл print()?";
            if (exp.ToLower() == act.ToLower()) return "Почти! Отличаются только большие/маленькие буквы.";
            if (exp.Replace(" ", "") == act.Replace(" ", "")) return "Почти! Отличаются только пробелы.";
            var e = exp.Split('\n'); var a = act.Split('\n');
            for (int i = 0; i < Math.Max(e.Length, a.Length); i++)
            {
                string el = i < e.Length ? e[i] : null, al = i < a.Length ? a[i] : null;
                if (el == al) continue;
                if (el == null) return "Выведено лишнее: строка " + (i + 1) + " «" + al + "» не нужна.";
                if (al == null) return "Не хватает строк вывода: ожидалась строка " + (i + 1) + " «" + el + "».";
                if (el.TrimEnd('.', '!', '?') == al.TrimEnd('.', '!', '?')) return "Строка " + (i + 1) + ": отличаются только знаки препинания в конце.";
                if (al.EndsWith(".0") && el == al.Substring(0, al.Length - 2)) return "Строка " + (i + 1) + ": получилось дробное число " + al + ", а нужно целое " + el + ". Попробуй // вместо / или int(...).";
                return "Строка вывода " + (i + 1) + ": ожидалось «" + el + "», а получилось «" + al + "».";
            }
            return "Вывод отличается.";
        }

        // ---------- Проверка функций ----------
        public static List<CheckResult> CheckFunction(string code, string entry, IList<FuncTest> tests, int maxSteps = 1000000)
        {
            var res = new List<CheckResult>();
            OnBigStack(() =>
            {
                var ip = new Interp { MaxSteps = maxSteps };
                var console = new StringBuilder();
                ip.OnOutput = s => console.Append(s);
                PyError loadErr = null;
                object fn = null;
                try { ip.Run(Parser.ParseProgram(code)); }
                catch (PyError e) { loadErr = e; }
                catch (Exception e) { loadErr = Internal(e, ip.CurrentLine); }
                if (loadErr == null)
                {
                    if (!ip.Globals.Vars.TryGetValue(entry ?? "", out fn))
                        loadErr = new PyError("NameError", "В коде нет функции «" + entry + "». Тесты вызывают именно " + entry + "(...) — проверь имя после def.", "name '" + entry + "' is not defined", 0);
                    else if (!ip.IsCallable(fn))
                        loadErr = new PyError("TypeError", "«" + entry + "» — это не функция, а " + PyOps.TypeNameRu(fn) + ".", "'" + PyOps.TypeName(fn) + "' object is not callable", 0);
                }
                foreach (var t in tests)
                {
                    var cr = new CheckResult();
                    List<object> args;
                    var inList = AsList(t.input);
                    args = inList != null ? inList.Select(ToPy).ToList() : new List<object> { ToPy(t.input) };
                    var expected = NormJson(t.expected);
                    var prev = Interp.Current;
                    Interp.Current = ip;
                    try
                    {
                        cr.InputsText = (entry ?? "?") + "(" + string.Join(", ", args.Select(a => PyOps.Repr(a)).ToArray()) + ")";
                        cr.Expected = PyOps.Repr(ToPy(expected));
                        if (loadErr != null)
                        {
                            cr.Error = loadErr; cr.Actual = "";
                            cr.Note = loadErr.PyType == "NameError" && loadErr.Line == 0 ? loadErr.Ru : "Код не запустился: ошибка " + loadErr.PyType + " в строке " + loadErr.Line + ".";
                            res.Add(cr); continue;
                        }
                        ip.Steps = 0;
                        var r = ip.Call(fn, args, new Dictionary<string, object>(), 0);   // args — свежие объекты этого теста (ToPy)
                        cr.Actual = PyOps.Repr(r);
                        cr.Got = ToJsonLike(r, ip);
                        cr.Passed = JsonEq(cr.Got, expected);
                        if (!cr.Passed)
                        {
                            cr.Note = r == null ? "Функция вернула None — возможно, забыт return (или результат печатается через print вместо return)." :
                                "Функция вернула " + Short(cr.Actual) + ", а ожидалось " + Short(cr.Expected) + ".";
                        }
                    }
                    catch (PyError e) { cr.Error = e; cr.Actual = cr.Actual ?? ""; cr.Note = "Функция упала с ошибкой " + e.PyType + (e.Line > 0 ? " в строке " + e.Line : "") + "."; }
                    catch (Exception e) { cr.Error = Internal(e, ip.CurrentLine); cr.Actual = ""; cr.Note = cr.Error.Ru; }
                    finally { Interp.Current = prev; }
                    res.Add(cr);
                }
            });
            return res;
        }

        static string Short(string s) { return s.Length > 200 ? s.Substring(0, 197) + "..." : s; }

        // Универсальная проверка по test_cases из JSON задачи
        public static List<CheckResult> CheckJson(string code, string entry, string testCasesJson, int maxSteps = 1000000)
        {
            var arr = TestJson.Parse(testCasesJson) as List<object>;
            if (arr == null) throw new ArgumentException("test_cases: ожидался JSON-массив");
            if (string.IsNullOrEmpty(entry))
            {
                var tests = new List<TestCase>();
                foreach (Dictionary<string, object> tc in arr)
                {
                    object inp, exp;
                    tc.TryGetValue("input", out inp); tc.TryGetValue("expected", out exp);
                    tests.Add(new TestCase { inputs = SplitInput(inp as string), expected = exp as string ?? "" });
                }
                return Check(code, tests, maxSteps);
            }
            var ft = new List<FuncTest>();
            foreach (Dictionary<string, object> tc in arr)
            {
                object inp, exp;
                tc.TryGetValue("input", out inp); tc.TryGetValue("expected", out exp);
                ft.Add(new FuncTest(inp, exp));
            }
            return CheckFunction(code, entry, ft, maxSteps);
        }

        // "a\nb\n" -> ["a", "b"] (как stdin в CPython)
        public static string[] SplitInput(string s)
        {
            if (string.IsNullOrEmpty(s)) return new string[0];
            var parts = s.Replace("\r\n", "\n").Split('\n').ToList();
            if (parts.Count > 0 && parts[parts.Count - 1] == "") parts.RemoveAt(parts.Count - 1);
            return parts.ToArray();
        }

        static List<object> AsList(object o)
        {
            if (o is string || o == null) return null;
            if (o is List<object>) return (List<object>)o;
            if (o is PyList) return ((PyList)o).Items;
            var l = o as IList;
            if (l != null) { var r = new List<object>(); foreach (var x in l) r.Add(x); return r; }
            return null;
        }

        // JSON-подобное C# значение -> значение игрового Python
        public static object ToPy(object v)
        {
            if (v == null || v is bool || v is long || v is double || v is string || v is BigInteger) return v;
            if (v is int || v is short || v is byte || v is sbyte || v is ushort || v is uint) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is ulong) return PyNum.Norm(new BigInteger((ulong)v));
            if (v is float || v is decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (v is PyList || v is PyTuple || v is PyDict || v is PySet || v is PyInstance) return v;
            var dict = v as IDictionary;
            if (dict != null)
            {
                var d = new PyDict();
                foreach (DictionaryEntry kv in dict) d.Set(ToPy(kv.Key), ToPy(kv.Value));
                return d;
            }
            var list = v as IEnumerable;
            if (list != null) { var l = new PyList(); foreach (var x in list) l.Items.Add(ToPy(x)); return l; }
            return v;
        }

        // Нормализуем ожидаемое значение к виду long/double/string/bool/null/List/Dictionary
        static object NormJson(object v)
        {
            if (v == null || v is bool || v is long || v is double || v is string || v is BigInteger) return v;
            if (v is int || v is short || v is byte) return Convert.ToInt64(v, CultureInfo.InvariantCulture);
            if (v is float || v is decimal) return Convert.ToDouble(v, CultureInfo.InvariantCulture);
            var dict = v as IDictionary;
            if (dict != null)
            {
                var d = new Dictionary<string, object>();
                foreach (DictionaryEntry kv in dict) d[Convert.ToString(kv.Key, CultureInfo.InvariantCulture)] = NormJson(kv.Value);
                return d;
            }
            var list = v as IEnumerable;
            if (list != null) { var l = new List<object>(); foreach (var x in list) l.Add(NormJson(x)); return l; }
            return v;
        }

        // Результат Python -> JSON-подобное значение (как norm() + json.dumps(default=str) в validate.py)
        public static object ToJsonLike(object v, Interp ip)
        {
            if (v == null || v is bool || v is long || v is double || v is string || v is BigInteger) return v;
            PyDepth.Enter("");
            try { return ToJsonLikeInner(v, ip); }
            finally { PyDepth.Leave(); }
        }

        static object ToJsonLikeInner(object v, Interp ip)
        {
            if (v is PyList || v is PyTuple)
            {
                IEnumerable<object> items = v is PyList ? (IEnumerable<object>)((PyList)v).Items : ((PyTuple)v).Items;
                return items.Select(x => ToJsonLike(x, ip)).ToList();
            }
            var d = v as PyDict;
            if (d != null)
            {
                var r = new Dictionary<string, object>();
                foreach (var p in d.Pairs()) r[PyOps.Str(p.Key)] = ToJsonLike(p.Value, ip);
                return r;
            }
            var s = v as PySet;
            if (s != null)
            {
                var l = s.ToList();
                try { var tmp = new PyList(l); ip.Call(ip.Builtins["sorted"], new List<object> { tmp }, new Dictionary<string, object>(), 0); l = ((PyList)ip.Call(ip.Builtins["sorted"], new List<object> { tmp }, new Dictionary<string, object>(), 0)).Items; }
                catch (PyError) { }
                return l.Select(x => ToJsonLike(x, ip)).ToList();
            }
            return PyOps.Str(v);
        }

        static bool ToF(object o, out double d)
        {
            d = 0;
            if (o is double) { d = (double)o; return true; }
            if (o is long) { d = (long)o; return true; }
            if (o is bool) { d = (bool)o ? 1 : 0; return true; }
            if (o is BigInteger) { try { d = PyNum.BigToDouble((BigInteger)o); return true; } catch (PyError) { return false; } }
            if (o is string) return PyNum.TryParseFloat((string)o, out d);
            return false;
        }

        // Сравнение как eq() в validate.py
        public static bool JsonEq(object a, object b)
        {
            if (a is double || b is double)
            {
                double x, y;
                if (!ToF(a, out x) || !ToF(b, out y)) return false;
                if (x == y) return true;
                if (double.IsInfinity(x) || double.IsInfinity(y) || double.IsNaN(x) || double.IsNaN(y)) return false;
                double diff = Math.Abs(x - y);
                return diff <= 1e-6 * Math.Max(Math.Abs(x), Math.Abs(y)) || diff <= 1e-6;
            }
            if ((a is bool) != (b is bool)) return false;
            var la = a as List<object>; var lb = b as List<object>;
            if (la != null || lb != null)
            {
                if (la == null || lb == null || la.Count != lb.Count) return false;
                for (int i = 0; i < la.Count; i++) if (!JsonEq(la[i], lb[i])) return false;
                return true;
            }
            var da = a as Dictionary<string, object>; var db = b as Dictionary<string, object>;
            if (da != null || db != null)
            {
                if (da == null || db == null || da.Count != db.Count) return false;
                foreach (var kv in da) { object v; if (!db.TryGetValue(kv.Key, out v) || !JsonEq(kv.Value, v)) return false; }
                return true;
            }
            if (a == null || b == null) return a == null && b == null;
            if ((a is long || a is BigInteger) && (b is long || b is BigInteger)) return PyNum.Big(a) == PyNum.Big(b);
            return a.Equals(b);
        }
    }

    // ================== Мини-JSON (для test_cases) ==================
    public static class TestJson
    {
        // object -> Dictionary<string, object> (порядок ключей сохраняется), array -> List<object>,
        // целые -> long (или BigInteger), дробные -> double, true/false -> bool, null -> null
        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("TestJson: лишние символы на позиции " + i);
            return v;
        }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("TestJson: неожиданный конец");
            char c = s[i];
            if (c == '{')
            {
                i++; var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); var k = Str(s, ref i); Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("TestJson: ожидалось ':' на позиции " + i);
                    i++; d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("TestJson: ожидалось ',' или '}' на позиции " + i);
                }
            }
            if (c == '[')
            {
                i++; var l = new List<object>();
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("TestJson: ожидалось ',' или ']' на позиции " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            if (string.CompareOrdinal(s, i, "NaN", 0, 3) == 0) { i += 3; return double.NaN; }
            if (string.CompareOrdinal(s, i, "Infinity", 0, 8) == 0) { i += 8; return double.PositiveInfinity; }
            if (string.CompareOrdinal(s, i, "-Infinity", 0, 9) == 0) { i += 9; return double.NegativeInfinity; }
            int st = i; bool flt = false;
            if (s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
            {
                if (s[i] == '.' || s[i] == 'e' || s[i] == 'E') flt = true;
                i++;
            }
            var txt = s.Substring(st, i - st);
            if (txt.Length == 0) throw new FormatException("TestJson: непонятный символ на позиции " + i);
            if (flt) { double d; if (!PyNum.TryParseFloat(txt, out d)) throw new FormatException("TestJson: число " + txt); return d; }
            long lv;
            if (long.TryParse(txt, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out lv)) return lv;
            return BigInteger.Parse(txt, CultureInfo.InvariantCulture);
        }
        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("TestJson: ожидалась строка на позиции " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i];
                if (c == '"') { i++; return sb.ToString(); }
                if (c != '\\') { sb.Append(c); i++; continue; }
                char e = s[i + 1]; i += 2;
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
        }
    }

    // ================== Пошаговая отладка ==================
    // Программа выполняется в отдельном потоке и «замирает» перед строками,
    // пока игрок не нажмёт «Шаг» или «Продолжить».
    public class DebugSession
    {
        enum Mode { Into, Over, Run }

        readonly string code; readonly List<string> inputs;
        public HashSet<int> Breakpoints = new HashSet<int>();

        readonly object sync = new object();
        readonly SemaphoreSlim gate = new SemaphoreSlim(0);
        Thread thread;
        volatile bool stopRequested;
        volatile Mode mode = Mode.Into;
        int overDepth;
        List<VarInfo> prevVars = new List<VarInfo>();
        int prevLine;

        // Состояние для интерфейса (читать под lock через свойства)
        bool paused, finished; int line, depth; List<VarInfo> vars = new List<VarInfo>();
        List<string> changes = new List<string>(); StringBuilder console = new StringBuilder();
        PyError error; string frameName = "<модуль>";
        public int StepCount;

        public DebugSession(string code, IEnumerable<string> inputs, IEnumerable<int> breakpoints)
        {
            this.code = code; this.inputs = inputs == null ? new List<string>() : inputs.ToList();
            if (breakpoints != null) foreach (var b in breakpoints) Breakpoints.Add(b);
        }

        public bool Paused { get { lock (sync) return paused; } }
        public bool Finished { get { lock (sync) return finished; } }
        public int Line { get { lock (sync) return line; } }
        public string FrameName { get { lock (sync) return frameName; } }
        public List<VarInfo> Vars { get { lock (sync) return vars.ToList(); } }
        public List<string> Changes { get { lock (sync) return changes.ToList(); } }
        public string Console { get { lock (sync) return console.ToString(); } }
        public PyError Error { get { lock (sync) return error; } }

        public void Start(bool stepFromFirstLine)
        {
            mode = stepFromFirstLine ? Mode.Into : Mode.Run;
            thread = new Thread(Worker, PyRun.StackSize) { IsBackground = true, Name = "PyDebug" };
            thread.Start();
        }

        void Worker()
        {
            var ip = new Interp();
            foreach (var s in inputs) ip.Inputs.Enqueue(s);
            ip.OnOutput = s => { lock (sync) console.Append(s); };
            ip.OnLine = Hook;
            try { ip.Run(Parser.ParseProgram(code)); }
            catch (StopExecution) { }
            catch (PyError e) { lock (sync) { error = e; line = e.Line; vars = ip.Snapshot(); } }
            catch (Exception e) { lock (sync) error = new PyError("InternalError", "Внутренняя ошибка игры: " + e.Message, ip.CurrentLine); }
            lock (sync) { finished = true; paused = false; if (error == null) { changes = Diff(prevVars, ip.Snapshot(), prevLine); vars = ip.Snapshot(); } }
        }

        void Hook(int ln, Interp ip)
        {
            if (stopRequested) throw new StopExecution();
            StepCount++;
            bool pause = mode == Mode.Into
                      || (mode == Mode.Over && ip.Depth <= overDepth)
                      || Breakpoints.Contains(ln);
            if (!pause) return;
            var snap = ip.Snapshot();
            lock (sync)
            {
                changes = Diff(prevVars, snap, prevLine);
                prevVars = snap; prevLine = ln;
                vars = snap; line = ln; depth = ip.Depth; paused = true;
                frameName = ip.Depth > 1 ? ip.Stack[ip.Stack.Count - 1].Name : "<модуль>";
            }
            gate.Wait();
            lock (sync) paused = false;
            if (stopRequested) throw new StopExecution();
        }

        static List<string> Diff(List<VarInfo> before, List<VarInfo> after, int atLine)
        {
            var res = new List<string>();
            foreach (var v in after)
            {
                var old = before.FirstOrDefault(b => b.Name == v.Name && b.Scope == v.Scope);
                if (old == null) res.Add("новая переменная " + v.Name + " = " + v.Value);
                else if (old.Value != v.Value) res.Add(v.Name + ": " + old.Value + " → " + v.Value);
            }
            return res;
        }

        // Paused сбрасывается сразу (под lock): повторное нажатие до следующей остановки не «проглотит» точку останова
        void Resume(Mode m)
        {
            lock (sync)
            {
                if (!paused) return;
                paused = false;
                if (m == Mode.Over) overDepth = depth;
                mode = m;
            }
            gate.Release();
        }
        public void StepInto() { Resume(Mode.Into); }
        public void StepOver() { Resume(Mode.Over); }
        public void Continue() { Resume(Mode.Run); }
        public void Stop() { stopRequested = true; gate.Release(); }
    }
}
