// Объяснение строк кода, запуск, пошаговая отладка и проверка задач.
using System;
using System.Collections.Generic;
using System.Linq;
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
                if (s is WhileS) Walk(((WhileS)s).Body, res);
                if (s is ForS) Walk(((ForS)s).Body, res);
                if (s is DefS) Walk(((DefS)s).Body, res);
            }
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
                if (a.Targets[0] is TupleE)
                    return "Распаковка: значение справа — это набор из нескольких элементов, они по очереди раскладываются в " + tgt + ".";
                if (a.Targets[0] is IndexE)
                    return "Записываем значение " + Code(v) + " в " + Code(a.Targets[0]) + " — меняем элемент списка или словаря по индексу/ключу.";
                if (IsCall(v, "input"))
                    return "input() ждёт, пока пользователь что-то введёт, и возвращает это как СТРОКУ. Результат кладём в переменную " + tgt + ".";
                if ((IsCall(v, "int") || IsCall(v, "float")) && ((CallE)v).Args.Count == 1 && IsCall(((CallE)v).Args[0], "input"))
                    return "Читаем ввод через input() и сразу превращаем строку в число через " + ((NameE)((CallE)v).F).Id + "(). Число кладём в " + tgt + ".";
                if (IsMethodCall(v, "split"))
                    return "split() разрезает строку на части (по пробелам, если не указано иное) и возвращает список. Список кладём в " + tgt + ".";
                if (v is ListCompE)
                    return "Генератор списка: проходим по " + Code(((ListCompE)v).Iter) + " и для каждого элемента собираем " + Code(((ListCompE)v).Elt) + " в новый список " + tgt + ".";
                if (v is ListE && ((ListE)v).Items.Count == 0) return "Создаём пустой список и кладём его в " + tgt + ". Потом в него можно добавлять элементы через .append().";
                if (v is DictE && ((DictE)v).Keys.Count == 0) return "Создаём пустой словарь " + tgt + ". Словарь хранит пары «ключ: значение».";
                if (v is ListE) return "Создаём список из " + ((ListE)v).Items.Count + " элемент(ов) и кладём его в " + tgt + ".";
                if (v is DictE) return "Создаём словарь с парами «ключ: значение» и кладём его в " + tgt + ".";
                if (v is ConstE) return "Создаём переменную " + tgt + " и кладём в неё " + DescribeConst(((ConstE)v).V) + ". Если переменная уже была — старое значение заменится.";
                if (v is FStrE) return "Собираем строку через f-строку: всё в {фигурных скобках} заменяется значениями. Результат кладём в " + tgt + ".";
                return "Вычисляем выражение справа от = (" + Code(v) + ") и кладём результат в переменную " + tgt + ".";
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
                if (IsMethodCall(e, "append")) { var c = (CallE)e; return "Добавляем " + Code(c.Args.FirstOrDefault()) + " в конец списка " + Code(((AttrE)c.F).Obj) + "."; }
                if (IsMethodCall(e, "sort")) return "Сортируем список " + Code(((AttrE)((CallE)e).F).Obj) + " прямо на месте (он сам изменится).";
                if (IsMethodCall(e, "pop")) return "Удаляем элемент из списка " + Code(((AttrE)((CallE)e).F).Obj) + ".";
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
                return head + Code(i.Cond) + "." + tail + CondHint(i.Cond);
            }
            if (s is WhileS)
            {
                var w = (WhileS)s;
                if (w.Cond is ConstE && Equals(((ConstE)w.Cond).V, true))
                    return "Бесконечный цикл while True: повторяется вечно, пока внутри не сработает break.";
                return "Цикл while: пока условие " + Code(w.Cond) + " истинно, повторяем блок с отступом. Условие проверяется перед каждым повтором — не забудь менять переменные внутри, иначе цикл станет бесконечным.";
            }
            if (s is ForS)
            {
                var f = (ForS)s; string t = Code(f.Target);
                if (IsCall(f.Iter, "range"))
                {
                    var args = ((CallE)f.Iter).Args.Select(Code).ToArray();
                    string range = args.Length == 1 ? "от 0 до " + args[0] + " - 1" :
                                   args.Length == 2 ? "от " + args[0] + " до " + args[1] + " - 1" :
                                   "от " + (args.Length > 0 ? args[0] : "?") + " до " + (args.Length > 1 ? args[1] : "?") + " (не включая) с шагом " + (args.Length > 2 ? args[2] : "1");
                    return "Цикл for: переменная " + t + " по очереди принимает числа " + range + ", и для каждого числа выполняется блок с отступом. Последнее число range() не включается!";
                }
                if (IsMethodCall(f.Iter, "items"))
                    return "Цикл по словарю: на каждом шаге берём пару «ключ, значение» и кладём их в " + t + ".";
                if (f.Target is TupleE) return "Цикл for: на каждом шаге берём очередной элемент из " + Code(f.Iter) + " и раскладываем его в " + t + ".";
                return "Цикл for: по очереди берём каждый элемент из " + Code(f.Iter) + ", кладём его в переменную " + t + " и выполняем блок с отступом.";
            }
            if (s is DefS)
            {
                var d = (DefS)s;
                return "Объявляем функцию " + d.Name + "(" + string.Join(", ", d.Params.ToArray()) + "). Код внутри НЕ выполняется сейчас — только когда где-то ниже напишут " + d.Name + "(...)." +
                       (d.Params.Count > 0 ? " Параметры — это переменные, в которые попадут переданные значения." : "");
            }
            if (s is ReturnS)
            {
                var r = (ReturnS)s;
                return r.Value == null ? "Выходим из функции, ничего не возвращая (результат будет None)." :
                    "Возвращаем " + Code(r.Value) + " туда, откуда функцию вызвали. После return функция сразу заканчивается.";
            }
            if (s is BreakS) return "break — досрочно выходим из ближайшего цикла.";
            if (s is ContinueS) return "continue — пропускаем остаток текущего шага цикла и переходим к следующему.";
            if (s is PassS) return "pass — «ничего не делать». Заглушка там, где Python требует хотя бы одну строку.";
            return "";
        }

        static string CondHint(Expr c)
        {
            var ce = c as CompareE;
            if (ce != null && ce.Ops.Contains("==")) return " Помни: == сравнивает, а = присваивает.";
            if (ce != null && ce.Ops.Contains("%")) return "";
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
            if (v is long) return "целое число " + v;
            if (v is double) return "дробное число " + PyOps.Repr(v);
            if (v is bool) return PyOps.Repr(v) + " (логическое значение)";
            if (v == null) return "None (пустое значение)";
            return PyOps.Repr(v);
        }

        // Превращаем выражение обратно в код для объяснений
        public static string Code(Expr e)
        {
            if (e == null) return "";
            if (e is ConstE) return PyOps.Repr(((ConstE)e).V);
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
                var args = c.Args.Select(Code).Concat(c.Kw.Select(k => k.Key + "=" + Code(k.Value))).ToArray();
                return Code(c.F) + "(" + string.Join(", ", args) + ")";
            }
            if (e is AttrE) return Code(((AttrE)e).Obj) + "." + ((AttrE)e).Name;
            if (e is IndexE) return Code(((IndexE)e).Obj) + "[" + Code(((IndexE)e).Idx) + "]";
            if (e is SliceE) { var s = (SliceE)e; return Code(s.Lo) + ":" + Code(s.Hi) + (s.Step != null ? ":" + Code(s.Step) : ""); }
            if (e is ListE) return "[" + string.Join(", ", ((ListE)e).Items.Select(Code).ToArray()) + "]";
            if (e is TupleE) return string.Join(", ", ((TupleE)e).Items.Select(Code).ToArray());
            if (e is DictE) { var d = (DictE)e; return "{" + string.Join(", ", d.Keys.Select((k, i) => Code(k) + ": " + Code(d.Vals[i])).ToArray()) + "}"; }
            if (e is IfExpE) { var i = (IfExpE)e; return Code(i.A) + " if " + Code(i.Cond) + " else " + Code(i.B); }
            if (e is ListCompE) { var l = (ListCompE)e; return "[" + Code(l.Elt) + " for " + Code(l.Target) + " in " + Code(l.Iter) + (l.Cond != null ? " if " + Code(l.Cond) : "") + "]"; }
            if (e is FStrE)
            {
                var f = (FStrE)e; var sb = new StringBuilder("f\"");
                for (int i = 0; i < f.Parts.Count; i++)
                    sb.Append(f.Parts[i] is string ? (string)f.Parts[i] : "{" + Code((Expr)f.Parts[i]) + (f.Specs[i] != null ? ":" + f.Specs[i] : "") + "}");
                return sb.Append("\"").ToString();
            }
            return "…";
        }
        static string Wrap(Expr e) { return (e is BinE || e is BoolOpE || e is CompareE) ? "(" + Code(e) + ")" : Code(e); }
    }

    // ================== Запуск и проверка ==================
    [Serializable]
    public class TestCase
    {
        public string[] inputs = new string[0];
        public string expected = "";
    }

    public class RunResult
    {
        public string Console = "";   // всё, что видно в консоли (включая ввод)
        public string Printed = "";   // только print — сравнивается с ожидаемым
        public PyError Error;
        public int Steps;
    }

    public class CheckResult
    {
        public bool Passed;
        public string InputsText, Expected, Actual, Note;
        public PyError Error;
    }

    public static class PyRun
    {
        public static RunResult Run(string code, IEnumerable<string> inputs, int maxSteps = 200000)
        {
            var r = new RunResult();
            var console = new StringBuilder();
            var ip = new Interp { MaxSteps = maxSteps };
            if (inputs != null) foreach (var s in inputs) ip.Inputs.Enqueue(s);
            ip.OnOutput = s => console.Append(s);
            try { ip.Run(Parser.ParseProgram(code)); }
            catch (PyError e) { r.Error = e; }
            catch (StackOverflowException) { r.Error = new PyError("RecursionError", "Слишком глубокая рекурсия.", ip.CurrentLine); }
            catch (Exception e) { r.Error = new PyError("InternalError", "Внутренняя ошибка игры: " + e.Message + ". Сообщи разработчику :)", ip.CurrentLine); }
            r.Console = console.ToString(); r.Printed = ip.Printed.ToString(); r.Steps = ip.Steps;
            return r;
        }

        public static string Normalize(string s)
        {
            var lines = (s ?? "").Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd()).ToList();
            while (lines.Count > 0 && lines[lines.Count - 1] == "") lines.RemoveAt(lines.Count - 1);
            return string.Join("\n", lines.ToArray());
        }

        public static List<CheckResult> Check(string code, IList<TestCase> tests)
        {
            var res = new List<CheckResult>();
            foreach (var t in tests)
            {
                var r = Run(code, t.inputs);
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
            thread = new Thread(Worker) { IsBackground = true, Name = "PyDebug" };
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

        public void StepInto() { if (!Paused) return; mode = Mode.Into; gate.Release(); }
        public void StepOver() { if (!Paused) return; lock (sync) overDepth = depth; mode = Mode.Over; gate.Release(); }
        public void Continue() { if (!Paused) return; mode = Mode.Run; gate.Release(); }
        public void Stop() { stopRequested = true; gate.Release(); }
    }
}
