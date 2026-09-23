// Игровой Python: выполнение программы.
// Перед каждой инструкцией вызывается OnLine — так работает пошаговый дебаггер.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Intern.Py
{
    public class Frame
    {
        public string Name;                                   // "<модуль>" или имя функции
        public Dictionary<string, object> Vars = new Dictionary<string, object>();
        public List<string> Order = new List<string>();       // порядок появления переменных (для окна переменных)
        public void Set(string k, object v) { if (!Vars.ContainsKey(k)) Order.Add(k); Vars[k] = v; }
    }

    public class VarInfo { public string Scope, Name, Type, Value; }

    public class Interp
    {
        enum Flow { Normal, Break, Continue, Return }

        public Frame Globals = new Frame { Name = "<модуль>" };
        public List<Frame> Stack = new List<Frame>();
        public Dictionary<string, object> Builtins = new Dictionary<string, object>();

        public Action<int, Interp> OnLine;       // хук перед каждой строкой
        public Action<string> OnOutput;          // всё, что видно в консоли
        public StringBuilder Printed = new StringBuilder(); // только то, что вывел print (для проверки задач)
        public Queue<string> Inputs = new Queue<string>();

        public int MaxSteps = 200000;
        public int Steps;
        public int Depth { get { return Stack.Count; } }
        public int CurrentLine;

        object retVal;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Interp()
        {
            Stack.Add(Globals);
            InitBuiltins();
        }

        Frame Cur { get { return Stack[Stack.Count - 1]; } }

        public void Run(List<Stmt> program)
        {
            ExecBlock(program);
        }

        void Out(string s)
        {
            if (OnOutput != null) OnOutput(s);
        }

        // ---------- Снимок переменных для дебаггера ----------
        public List<VarInfo> Snapshot()
        {
            var res = new List<VarInfo>();
            if (Stack.Count > 1)
            {
                var f = Cur;
                foreach (var k in f.Order) if (f.Vars.ContainsKey(k)) res.Add(Info("внутри " + f.Name + "()", k, f.Vars[k]));
            }
            foreach (var k in Globals.Order)
            {
                if (!Globals.Vars.ContainsKey(k)) continue;
                var v = Globals.Vars[k];
                res.Add(Info("глобальная", k, v));
            }
            return res;
        }

        static VarInfo Info(string scope, string name, object v)
        {
            string r = PyOps.Repr(v);
            if (r.Length > 120) r = r.Substring(0, 117) + "...";
            return new VarInfo { Scope = scope, Name = name, Type = PyOps.TypeName(v), Value = r };
        }

        // ---------- Инструкции ----------
        Flow ExecBlock(List<Stmt> body)
        {
            foreach (var s in body)
            {
                var f = Exec(s);
                if (f != Flow.Normal) return f;
            }
            return Flow.Normal;
        }

        void Tick(Node s)
        {
            CurrentLine = s.Line;
            Steps++;
            if (Steps > MaxSteps)
                throw new PyError("TimeoutError", "Программа сделала слишком много шагов — похоже на бесконечный цикл. Проверь, что условие while когда-нибудь становится ложным (например, переменная меняется внутри цикла).", s.Line);
            if (OnLine != null) OnLine(s.Line, this);
        }

        Flow Exec(Stmt s)
        {
            Tick(s);
            try
            {
                return ExecInner(s);
            }
            catch (PyError e)
            {
                if (e.Line == 0) e.Line = s.Line;
                throw;
            }
        }

        Flow ExecInner(Stmt s)
        {
            if (s is ExprS) { Eval(((ExprS)s).E); return Flow.Normal; }
            if (s is AssignS)
            {
                var a = (AssignS)s;
                var v = Eval(a.Value);
                foreach (var tgt in a.Targets) Assign(tgt, v);
                return Flow.Normal;
            }
            if (s is AugAssignS)
            {
                var a = (AugAssignS)s;
                var cur = Eval(a.Target);
                var v = BinOp(a.Op, cur, Eval(a.Value), s.Line);
                Assign(a.Target, v);
                return Flow.Normal;
            }
            if (s is IfS)
            {
                var i = (IfS)s;
                if (PyOps.Truthy(Eval(i.Cond))) return ExecBlock(i.Body);
                if (i.Else != null)
                {
                    // elif — это вложенный IfS: у него свой шаг, чтобы дебаггер показал проверку
                    return ExecBlock(i.Else);
                }
                return Flow.Normal;
            }
            if (s is WhileS)
            {
                var w = (WhileS)s;
                bool first = true;
                while (true)
                {
                    if (!first) Tick(s);
                    first = false;
                    if (!PyOps.Truthy(Eval(w.Cond))) break;
                    var f = ExecBlock(w.Body);
                    if (f == Flow.Break) break;
                    if (f == Flow.Return) return f;
                }
                return Flow.Normal;
            }
            if (s is ForS)
            {
                var fs = (ForS)s;
                var iter = Eval(fs.Iter);
                bool first = true;
                foreach (var item in Iterate(iter, s.Line))
                {
                    if (!first) Tick(s);
                    first = false;
                    Assign(fs.Target, item);
                    var f = ExecBlock(fs.Body);
                    if (f == Flow.Break) break;
                    if (f == Flow.Return) return f;
                }
                return Flow.Normal;
            }
            if (s is DefS)
            {
                var d = (DefS)s;
                var fn = new PyFunction { Name = d.Name, Params = d.Params, Body = d.Body, Line = d.Line, Defaults = new List<object>() };
                foreach (var de in d.Defaults) fn.Defaults.Add(Eval(de));
                Cur.Set(d.Name, fn);
                return Flow.Normal;
            }
            if (s is ReturnS)
            {
                if (Stack.Count == 1) throw new PyError("SyntaxError", "return можно писать только внутри функции (после def).", s.Line);
                var r = (ReturnS)s;
                retVal = r.Value == null ? null : Eval(r.Value);
                return Flow.Return;
            }
            if (s is BreakS) return Flow.Break;
            if (s is ContinueS) return Flow.Continue;
            if (s is PassS) return Flow.Normal;
            throw new PyError("SyntaxError", "Неизвестная инструкция.", s.Line);
        }

        void Assign(Expr target, object v)
        {
            if (target is NameE)
            {
                var id = ((NameE)target).Id;
                Cur.Set(id, v);
                return;
            }
            if (target is TupleE || target is ListE)
            {
                var names = target is TupleE ? ((TupleE)target).Items : ((ListE)target).Items;
                var vals = Iterate(v, target.Line).ToList();
                if (vals.Count != names.Count)
                    throw new PyError("ValueError", "Слева " + names.Count + " переменных, а справа " + vals.Count + " значений — количество должно совпадать.", target.Line);
                for (int i = 0; i < names.Count; i++) Assign(names[i], vals[i]);
                return;
            }
            if (target is IndexE)
            {
                var ie = (IndexE)target;
                var obj = Eval(ie.Obj);
                var idx = Eval(ie.Idx);
                if (obj is PyList)
                {
                    var l = ((PyList)obj).Items;
                    int i = NormIndex(idx, l.Count, target.Line, "списка");
                    l[i] = v; return;
                }
                if (obj is PyDict) { ((PyDict)obj).Set(idx, v); return; }
                if (obj is string) throw new PyError("TypeError", "Строки нельзя менять по одному символу. Создай новую строку, например через replace() или сложение.", target.Line);
                if (obj is PyTuple) throw new PyError("TypeError", "Кортеж (tuple) нельзя изменять после создания.", target.Line);
                throw new PyError("TypeError", "Нельзя присваивать по индексу для " + PyOps.TypeNameRu(obj) + ".", target.Line);
            }
            throw new PyError("SyntaxError", "Так присваивать нельзя.", target.Line);
        }

        // ---------- Выражения ----------
        public object Eval(Expr e)
        {
            if (e is ConstE) return ((ConstE)e).V;
            if (e is NameE) return Lookup(((NameE)e).Id, e.Line);
            if (e is BinE) { var b = (BinE)e; return BinOp(b.Op, Eval(b.L), Eval(b.R), e.Line); }
            if (e is UnaryE)
            {
                var u = (UnaryE)e; var v = Eval(u.E);
                if (u.Op == "not") return !PyOps.Truthy(v);
                if (!PyOps.IsNum(v)) throw new PyError("TypeError", "Минус/плюс перед значением работает только с числами, а тут " + PyOps.TypeNameRu(v) + ".", e.Line);
                if (u.Op == "-") return PyOps.IsInt(v) ? (object)(-PyOps.ToLong(v)) : -PyOps.ToDouble(v);
                return PyOps.IsInt(v) ? (object)PyOps.ToLong(v) : v;
            }
            if (e is BoolOpE)
            {
                var b = (BoolOpE)e; var l = Eval(b.L);
                if (b.Op == "and") return PyOps.Truthy(l) ? Eval(b.R) : l;
                return PyOps.Truthy(l) ? l : Eval(b.R);
            }
            if (e is CompareE)
            {
                var c = (CompareE)e; var left = Eval(c.L);
                for (int i = 0; i < c.Ops.Count; i++)
                {
                    var right = Eval(c.Rs[i]);
                    if (!CompareOp(c.Ops[i], left, right, e.Line)) return false;
                    left = right;
                }
                return true;
            }
            if (e is IfExpE) { var ie = (IfExpE)e; return PyOps.Truthy(Eval(ie.Cond)) ? Eval(ie.A) : Eval(ie.B); }
            if (e is ListE) return new PyList(((ListE)e).Items.Select(Eval).ToList());
            if (e is TupleE) return new PyTuple(((TupleE)e).Items.Select(Eval).ToArray());
            if (e is DictE)
            {
                var d = (DictE)e; var r = new PyDict();
                for (int i = 0; i < d.Keys.Count; i++) r.Set(Eval(d.Keys[i]), Eval(d.Vals[i]));
                return r;
            }
            if (e is ListCompE)
            {
                var lc = (ListCompE)e; var r = new PyList();
                foreach (var item in Iterate(Eval(lc.Iter), e.Line).ToList())
                {
                    Assign(lc.Target, item);
                    if (lc.Cond == null || PyOps.Truthy(Eval(lc.Cond))) r.Items.Add(Eval(lc.Elt));
                }
                return r;
            }
            if (e is FStrE)
            {
                var f = (FStrE)e; var sb = new StringBuilder();
                for (int i = 0; i < f.Parts.Count; i++)
                {
                    if (f.Parts[i] is string) sb.Append((string)f.Parts[i]);
                    else sb.Append(Format(Eval((Expr)f.Parts[i]), f.Specs[i], e.Line));
                }
                return sb.ToString();
            }
            if (e is IndexE) { var ie = (IndexE)e; return GetIndex(Eval(ie.Obj), ie.Idx, e.Line); }
            if (e is AttrE)
            {
                var a = (AttrE)e; var obj = Eval(a.Obj);
                if (!HasMethod(obj, a.Name))
                    throw new PyError("AttributeError", "У " + PyOps.TypeNameRu(obj) + " нет метода «" + a.Name + "»." + MethodHint(obj, a.Name), e.Line);
                return new PyBoundMethod { Self = obj, Name = a.Name };
            }
            if (e is CallE) return EvalCall((CallE)e);
            if (e is SliceE) throw new PyError("SyntaxError", "Срез [a:b] можно использовать только в квадратных скобках после списка или строки.", e.Line);
            throw new PyError("SyntaxError", "Неизвестное выражение.", e.Line);
        }

        object Lookup(string id, int line)
        {
            object v;
            if (Stack.Count > 1 && Cur.Vars.TryGetValue(id, out v)) return v;
            if (Globals.Vars.TryGetValue(id, out v)) return v;
            if (Builtins.TryGetValue(id, out v)) return v;
            string hint = "";
            var all = new List<string>(Globals.Vars.Keys);
            if (Stack.Count > 1) all.AddRange(Cur.Vars.Keys);
            all.AddRange(Builtins.Keys);
            string close = all.FirstOrDefault(n => Similar(n, id));
            if (close != null) hint = " Может, ты имел в виду «" + close + "»?";
            else if (id == "Print" || id == "PRINT") hint = " Python различает большие и маленькие буквы: нужно print.";
            else hint = " Возможно, опечатка, или переменная создаётся ниже, чем используется. Если это текст — возьми его в кавычки.";
            throw new PyError("NameError", "Имя «" + id + "» не найдено." + hint, line);
        }

        static bool Similar(string a, string b)
        {
            if (a == b) return false;
            if (a.ToLower() == b.ToLower()) return true;
            if (Math.Abs(a.Length - b.Length) > 1 || a.Length < 3) return false;
            // расстояние Левенштейна <= 1
            int n = a.Length, m = b.Length; var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[n, m] <= 1;
        }

        bool CompareOp(string op, object a, object b, int line)
        {
            switch (op)
            {
                case "==": return PyOps.Eq(a, b);
                case "!=": return !PyOps.Eq(a, b);
                case "<": return PyOps.Compare(a, b, line) < 0;
                case ">": return PyOps.Compare(a, b, line) > 0;
                case "<=": return PyOps.Compare(a, b, line) <= 0;
                case ">=": return PyOps.Compare(a, b, line) >= 0;
                case "is": return (a == null && b == null) || (a is bool && b is bool && (bool)a == (bool)b) || ReferenceEquals(a, b);
                case "is not": return !CompareOp("is", a, b, line);
                case "in": return Contains(b, a, line);
                case "not in": return !Contains(b, a, line);
            }
            return false;
        }

        bool Contains(object container, object item, int line)
        {
            if (container is string)
            {
                if (!(item is string)) throw new PyError("TypeError", "Проверять «in» для строки можно только другой строкой.", line);
                return ((string)container).Contains((string)item);
            }
            if (container is PyDict) return ((PyDict)container).Map.ContainsKey(item);
            if (container is PyList || container is PyTuple || container is PyRange)
                return Iterate(container, line).Any(x => PyOps.Eq(x, item));
            throw new PyError("TypeError", "Оператор «in» не работает с " + PyOps.TypeNameRu(container) + ".", line);
        }

        public object BinOp(string op, object a, object b, int line)
        {
            if (PyOps.IsNum(a) && PyOps.IsNum(b))
            {
                bool ints = PyOps.IsInt(a) && PyOps.IsInt(b);
                switch (op)
                {
                    case "+": if (ints) return checked(PyOps.ToLong(a) + PyOps.ToLong(b)); return PyOps.ToDouble(a) + PyOps.ToDouble(b);
                    case "-": if (ints) return checked(PyOps.ToLong(a) - PyOps.ToLong(b)); return PyOps.ToDouble(a) - PyOps.ToDouble(b);
                    case "*": if (ints) return checked(PyOps.ToLong(a) * PyOps.ToLong(b)); return PyOps.ToDouble(a) * PyOps.ToDouble(b);
                    case "/":
                        if (PyOps.ToDouble(b) == 0) throw new PyError("ZeroDivisionError", "Делить на ноль нельзя. Проверь, что делитель не равен 0 (например, список не пустой).", line);
                        return PyOps.ToDouble(a) / PyOps.ToDouble(b);
                    case "//":
                        if (PyOps.ToDouble(b) == 0) throw new PyError("ZeroDivisionError", "Делить на ноль нельзя (даже целочисленно через //).", line);
                        if (ints) { long x = PyOps.ToLong(a), y = PyOps.ToLong(b); long q = x / y; if ((x % y != 0) && ((x < 0) != (y < 0))) q--; return q; }
                        return Math.Floor(PyOps.ToDouble(a) / PyOps.ToDouble(b));
                    case "%":
                        if (PyOps.ToDouble(b) == 0) throw new PyError("ZeroDivisionError", "Остаток от деления на ноль не существует.", line);
                        if (ints) { long x = PyOps.ToLong(a), y = PyOps.ToLong(b); long r = x % y; if (r != 0 && ((r < 0) != (y < 0))) r += y; return r; }
                        { double x = PyOps.ToDouble(a), y = PyOps.ToDouble(b); return x - y * Math.Floor(x / y); }
                    case "**":
                        if (ints && PyOps.ToLong(b) >= 0)
                        {
                            try { long r = 1, bs = PyOps.ToLong(a); for (long i = 0; i < PyOps.ToLong(b); i++) r = checked(r * bs); return r; }
                            catch (OverflowException) { throw new PyError("OverflowError", "Результат возведения в степень слишком большой.", line); }
                        }
                        return Math.Pow(PyOps.ToDouble(a), PyOps.ToDouble(b));
                }
            }
            if (op == "+")
            {
                if (a is string && b is string) return (string)a + (string)b;
                if (a is PyList && b is PyList) { var r = new PyList(((PyList)a).Items); r.Items.AddRange(((PyList)b).Items); return r; }
                if (a is PyTuple && b is PyTuple) return new PyTuple(((PyTuple)a).Items.Concat(((PyTuple)b).Items).ToArray());
                if (a is string && PyOps.IsNum(b))
                    throw new PyError("TypeError", "Нельзя сложить строку и число. Преврати число в строку через str(...) или используй f-строку: f\"текст {число}\". А если строка пришла из input() и это число — преврати её через int(...).", line);
                if (PyOps.IsNum(a) && b is string)
                    throw new PyError("TypeError", "Нельзя сложить число и строку. Если строка — это число из input(), преврати её через int(...).", line);
            }
            if (op == "*")
            {
                if (a is string && PyOps.IsInt(b)) return Repeat((string)a, PyOps.ToLong(b));
                if (PyOps.IsInt(a) && b is string) return Repeat((string)b, PyOps.ToLong(a));
                if (a is PyList && PyOps.IsInt(b)) { var r = new PyList(); for (long i = 0; i < PyOps.ToLong(b); i++) r.Items.AddRange(((PyList)a).Items); return r; }
                if (a is string && b is string) throw new PyError("TypeError", "Две строки нельзя перемножить. Если это числа из input(), сначала преврати их через int(...).", line);
            }
            if (op == "%" && a is string)
                throw new PyError("TypeError", "Форматирование через % не поддерживается — используй f-строки: f\"{x}\".", line);
            throw new PyError("TypeError", "Операция «" + op + "» не работает между " + PyOps.TypeNameRu(a) + " и " + PyOps.TypeNameRu(b) + "." +
                ((a is string || b is string) && op != "+" ? " Похоже, одно из значений — строка. Числа из input() нужно превращать через int(...)." : ""), line);
        }

        static string Repeat(string s, long n)
        {
            if (n <= 0) return "";
            if (s.Length * n > 100000) throw new PyError("MemoryError", "Слишком длинная строка получается.");
            var sb = new StringBuilder(); for (long i = 0; i < n; i++) sb.Append(s); return sb.ToString();
        }

        string Format(object v, string spec, int line)
        {
            if (string.IsNullOrEmpty(spec)) return PyOps.Str(v);
            spec = spec.Trim();
            try
            {
                if (spec.EndsWith("f"))
                {
                    int digits = spec.Contains(".") ? int.Parse(spec.Substring(spec.IndexOf('.') + 1).TrimEnd('f')) : 6;
                    if (!PyOps.IsNum(v)) throw new PyError("ValueError", "Формат :." + digits + "f подходит только для чисел.", line);
                    return PyOps.ToDouble(v).ToString("F" + digits, Inv);
                }
                if (spec == "d") return PyOps.ToLong(v).ToString(Inv);
                if (spec == ",") return PyOps.ToLong(v).ToString("#,0", Inv);
                if (spec.StartsWith(">") || spec.StartsWith("<") || spec.StartsWith("^"))
                {
                    int w = int.Parse(spec.Substring(1)); string s = PyOps.Str(v);
                    if (spec[0] == '>') return s.PadLeft(w);
                    if (spec[0] == '<') return s.PadRight(w);
                    int total = Math.Max(0, w - s.Length), left = total / 2; return new string(' ', left) + s + new string(' ', total - left);
                }
                int width;
                if (int.TryParse(spec, out width)) return PyOps.IsNum(v) ? PyOps.Str(v).PadLeft(width) : PyOps.Str(v).PadRight(width);
            }
            catch (FormatException) { }
            catch (InvalidCastException) { throw new PyError("ValueError", "Формат «" + spec + "» не подходит для " + PyOps.TypeNameRu(v) + ".", line); }
            throw new PyError("ValueError", "Формат «" + spec + "» в f-строке не поддерживается. Попробуй {x:.2f}.", line);
        }

        int NormIndex(object idx, int count, int line, string what)
        {
            if (!PyOps.IsInt(idx)) throw new PyError("TypeError", "Индекс " + what + " должен быть целым числом, а не " + PyOps.TypeNameRu(idx) + ".", line);
            long i = PyOps.ToLong(idx);
            if (i < 0) i += count;
            if (i < 0 || i >= count)
                throw new PyError("IndexError", "Индекс " + PyOps.ToLong(idx) + " выходит за границы " + what + " (элементов: " + count + ", допустимые индексы 0.." + (count - 1) + "). Помни: счёт начинается с 0.", line);
            return (int)i;
        }

        object GetIndex(object obj, Expr idxE, int line)
        {
            if (idxE is SliceE)
            {
                var s = (SliceE)idxE;
                object lo = s.Lo == null ? null : Eval(s.Lo), hi = s.Hi == null ? null : Eval(s.Hi), st = s.Step == null ? null : Eval(s.Step);
                List<object> src; bool isStr = obj is string;
                if (isStr) src = ((string)obj).Select(c => (object)c.ToString()).ToList();
                else if (obj is PyList) src = ((PyList)obj).Items;
                else if (obj is PyTuple) src = ((PyTuple)obj).Items.ToList();
                else throw new PyError("TypeError", "Срезы [a:b] работают со строками и списками, а не с " + PyOps.TypeNameRu(obj) + ".", line);
                int n = src.Count; long step = st == null ? 1 : PyOps.ToLong(st);
                if (step == 0) throw new PyError("ValueError", "Шаг среза не может быть 0.", line);
                long a, b;
                if (step > 0)
                {
                    a = lo == null ? 0 : PyOps.ToLong(lo); b = hi == null ? n : PyOps.ToLong(hi);
                    if (a < 0) a = Math.Max(0, a + n); if (b < 0) b = Math.Max(0, b + n);
                    a = Math.Min(a, n); b = Math.Min(b, n);
                }
                else
                {
                    a = lo == null ? n - 1 : PyOps.ToLong(lo); b = hi == null ? -1 - n : PyOps.ToLong(hi);
                    if (a < 0) a += n; if (b < 0) b += n; a = Math.Min(a, n - 1);
                }
                var res = new List<object>();
                if (step > 0) for (long i = a; i < b; i += step) res.Add(src[(int)i]);
                else for (long i = a; i > b && i >= 0; i += step) res.Add(src[(int)i]);
                if (isStr) return string.Concat(res.Select(x => (string)x));
                if (obj is PyTuple) return new PyTuple(res.ToArray());
                return new PyList(res);
            }
            var idx = Eval(idxE);
            if (obj is PyList) { var l = ((PyList)obj).Items; return l[NormIndex(idx, l.Count, line, "списка")]; }
            if (obj is PyTuple) { var l = ((PyTuple)obj).Items; return l[NormIndex(idx, l.Length, line, "кортежа")]; }
            if (obj is string) { var s = (string)obj; return s[NormIndex(idx, s.Length, line, "строки")].ToString(); }
            if (obj is PyDict)
            {
                object v;
                if (((PyDict)obj).Map.TryGetValue(idx, out v)) return v;
                throw new PyError("KeyError", "Ключа " + PyOps.Repr(idx) + " нет в словаре. Проверь его через «in» или используй .get(ключ, значение_по_умолчанию).", line);
            }
            if (obj is PyRange)
            {
                var r = (PyRange)obj; int i = NormIndex(idx, (int)PyOps.RangeLen(r), line, "range"); return r.Start + i * r.Step;
            }
            throw new PyError("TypeError", PyOps.TypeNameRu(obj) + " не поддерживает доступ по индексу [ ].", line);
        }

        public IEnumerable<object> Iterate(object o, int line)
        {
            if (o is PyList) { var l = ((PyList)o).Items; for (int i = 0; i < l.Count; i++) yield return l[i]; yield break; }
            if (o is PyTuple) { foreach (var x in ((PyTuple)o).Items) yield return x; yield break; }
            if (o is string) { foreach (var c in (string)o) yield return c.ToString(); yield break; }
            if (o is PyRange)
            {
                var r = (PyRange)o;
                if (r.Step > 0) for (long i = r.Start; i < r.Stop; i += r.Step) yield return i;
                else for (long i = r.Start; i > r.Stop; i += r.Step) yield return i;
                yield break;
            }
            if (o is PyDict) { foreach (var k in ((PyDict)o).Keys.ToList()) yield return k; yield break; }
            throw new PyError("TypeError", "По " + PyOps.TypeNameRu(o) + " нельзя пройтись циклом. " +
                (PyOps.IsInt(o) ? "Чтобы повторить что-то N раз, используй range(N)." : ""), line);
        }

        // ---------- Вызовы ----------
        object EvalCall(CallE c)
        {
            var f = Eval(c.F);
            var args = c.Args.Select(Eval).ToList();
            var kw = new Dictionary<string, object>();
            foreach (var p in c.Kw) kw[p.Key] = Eval(p.Value);
            return Call(f, args, kw, c.Line);
        }

        public object Call(object f, List<object> args, Dictionary<string, object> kw, int line)
        {
            if (f is PyBuiltin) return ((PyBuiltin)f).Fn(args, kw, line);
            if (f is PyBoundMethod) { var m = (PyBoundMethod)f; return CallMethod(m.Self, m.Name, args, kw, line); }
            if (f is PyFunction)
            {
                var fn = (PyFunction)f;
                if (Stack.Count > 150) throw new PyError("RecursionError", "Слишком глубокая рекурсия: функция вызывает сама себя без остановки. Нужен базовый случай с return.", line);
                int required = fn.Params.Count - fn.Defaults.Count;
                var frame = new Frame { Name = fn.Name };
                for (int i = 0; i < fn.Params.Count; i++)
                {
                    string pn = fn.Params[i];
                    if (i < args.Count) frame.Set(pn, args[i]);
                    else if (kw.ContainsKey(pn)) frame.Set(pn, kw[pn]);
                    else if (i >= required) frame.Set(pn, fn.Defaults[i - required]);
                    else throw new PyError("TypeError", "Функции " + fn.Name + "() не передали аргумент «" + pn + "». Она ждёт " + fn.Params.Count + " аргумент(а): " + string.Join(", ", fn.Params.ToArray()) + ".", line);
                }
                if (args.Count > fn.Params.Count)
                    throw new PyError("TypeError", "Функция " + fn.Name + "() принимает " + fn.Params.Count + " аргумент(а), а передали " + args.Count + ".", line);
                foreach (var k in kw.Keys) if (!fn.Params.Contains(k))
                        throw new PyError("TypeError", "У функции " + fn.Name + "() нет параметра «" + k + "».", line);
                Stack.Add(frame);
                try
                {
                    retVal = null;
                    var flow = ExecBlock(fn.Body);
                    if (flow == Flow.Break || flow == Flow.Continue) throw new PyError("SyntaxError", "break/continue можно использовать только внутри цикла.", line);
                    var r = flow == Flow.Return ? retVal : null;
                    retVal = null;
                    return r;
                }
                finally { Stack.RemoveAt(Stack.Count - 1); CurrentLine = line; }
            }
            if (f is PyTypeObj) return Call(Builtins[((PyTypeObj)f).Name], args, kw, line);
            throw new PyError("TypeError", PyOps.TypeNameRu(f) + " нельзя вызвать как функцию (скобки () после значения). " +
                (f is string ? "Может, между строкой и скобкой не хватает запятой или +?" : ""), line);
        }

        void Need(List<object> a, int min, int max, string name, int line)
        {
            if (a.Count < min || a.Count > max)
                throw new PyError("TypeError", name + "() ожидает " + (min == max ? min.ToString() : min + "–" + max) + " аргумент(а), а получила " + a.Count + ".", line);
        }

        void InitBuiltins()
        {
            Action<string, BuiltinFn> add = (n, fn) => Builtins[n] = new PyBuiltin(n, fn);

            add("print", (a, kw, line) =>
            {
                string sep = kw.ContainsKey("sep") ? PyOps.Str(kw["sep"]) : " ";
                string end = kw.ContainsKey("end") ? PyOps.Str(kw["end"]) : "\n";
                string s = string.Join(sep, a.Select(PyOps.Str).ToArray()) + end;
                Printed.Append(s); Out(s);
                return null;
            });
            add("input", (a, kw, line) =>
            {
                if (a.Count > 0) Out(PyOps.Str(a[0]));
                if (Inputs.Count == 0)
                    throw new PyError("EOFError", "input() ждёт ввод, но входных данных больше нет. Проверь, не вызываешь ли ты input() больше раз, чем нужно в задаче.", line);
                var s = Inputs.Dequeue();
                Out(s + "\n");
                return s;
            });
            add("len", (a, kw, line) =>
            {
                Need(a, 1, 1, "len", line); var o = a[0];
                if (o is string) return (long)((string)o).Length;
                if (o is PyList) return (long)((PyList)o).Items.Count;
                if (o is PyDict) return (long)((PyDict)o).Keys.Count;
                if (o is PyTuple) return (long)((PyTuple)o).Items.Length;
                if (o is PyRange) return PyOps.RangeLen((PyRange)o);
                throw new PyError("TypeError", "У " + PyOps.TypeNameRu(o) + " нет длины. len() работает со строками, списками и словарями.", line);
            });
            add("range", (a, kw, line) =>
            {
                Need(a, 1, 3, "range", line);
                foreach (var x in a) if (!PyOps.IsInt(x))
                        throw new PyError("TypeError", "range() принимает только целые числа, а получил " + PyOps.TypeNameRu(x) + "." + (x is string ? " Преврати строку в число: int(...)." : ""), line);
                var r = new PyRange { Start = 0, Step = 1 };
                if (a.Count == 1) r.Stop = PyOps.ToLong(a[0]);
                else { r.Start = PyOps.ToLong(a[0]); r.Stop = PyOps.ToLong(a[1]); if (a.Count == 3) r.Step = PyOps.ToLong(a[2]); }
                if (r.Step == 0) throw new PyError("ValueError", "Шаг range() не может быть 0.", line);
                return r;
            });
            add("int", (a, kw, line) =>
            {
                Need(a, 0, 1, "int", line);
                if (a.Count == 0) return 0L;
                var o = a[0];
                if (o is bool || o is long) return PyOps.ToLong(o);
                if (o is double) return (long)Math.Truncate((double)o);
                if (o is string)
                {
                    long v; var s = ((string)o).Trim().Replace("_", "");
                    if (long.TryParse(s, NumberStyles.AllowLeadingSign, Inv, out v)) return v;
                    throw new PyError("ValueError", "Строку " + PyOps.Repr(o) + " нельзя превратить в целое число." +
                        (s.Contains(".") || s.Contains(",") ? " Для дробных чисел используй float(...)." : " В ней должны быть только цифры."), line);
                }
                throw new PyError("TypeError", "int() не умеет превращать " + PyOps.TypeNameRu(o) + " в число.", line);
            });
            add("float", (a, kw, line) =>
            {
                Need(a, 0, 1, "float", line);
                if (a.Count == 0) return 0.0;
                var o = a[0];
                if (PyOps.IsNum(o)) return PyOps.ToDouble(o);
                if (o is string)
                {
                    double v; var s = ((string)o).Trim();
                    if (s.Contains(",")) throw new PyError("ValueError", "В Python дробная часть отделяется точкой, а не запятой: 3.5, а не 3,5.", line);
                    if (double.TryParse(s, NumberStyles.Float, Inv, out v)) return v;
                    throw new PyError("ValueError", "Строку " + PyOps.Repr(o) + " нельзя превратить в число.", line);
                }
                throw new PyError("TypeError", "float() не умеет превращать " + PyOps.TypeNameRu(o) + " в число.", line);
            });
            add("str", (a, kw, line) => { Need(a, 0, 1, "str", line); return a.Count == 0 ? "" : PyOps.Str(a[0]); });
            add("bool", (a, kw, line) => { Need(a, 0, 1, "bool", line); return a.Count == 0 ? false : PyOps.Truthy(a[0]); });
            add("list", (a, kw, line) => { Need(a, 0, 1, "list", line); return a.Count == 0 ? new PyList() : new PyList(Iterate(a[0], line).ToList()); });
            add("tuple", (a, kw, line) => { Need(a, 0, 1, "tuple", line); return new PyTuple(a.Count == 0 ? new object[0] : Iterate(a[0], line).ToArray()); });
            add("dict", (a, kw, line) =>
            {
                var d = new PyDict();
                if (a.Count == 1) foreach (var it in Iterate(a[0], line)) { var p = Iterate(it, line).ToList(); d.Set(p[0], p[1]); }
                foreach (var k in kw) d.Set(k.Key, k.Value);
                return d;
            });
            add("abs", (a, kw, line) =>
            {
                Need(a, 1, 1, "abs", line);
                if (!PyOps.IsNum(a[0])) throw new PyError("TypeError", "abs() работает только с числами.", line);
                return PyOps.IsInt(a[0]) ? (object)Math.Abs(PyOps.ToLong(a[0])) : Math.Abs((double)a[0]);
            });
            add("round", (a, kw, line) =>
            {
                Need(a, 1, 2, "round", line);
                if (!PyOps.IsNum(a[0])) throw new PyError("TypeError", "round() работает только с числами.", line);
                if (a.Count == 1) return (long)Math.Round(PyOps.ToDouble(a[0]), MidpointRounding.ToEven);
                return Math.Round(PyOps.ToDouble(a[0]), (int)PyOps.ToLong(a[1]), MidpointRounding.ToEven);
            });
            add("min", (a, kw, line) => MinMax(a, false, line));
            add("max", (a, kw, line) => MinMax(a, true, line));
            add("sum", (a, kw, line) =>
            {
                Need(a, 1, 2, "sum", line);
                object acc = a.Count == 2 ? a[1] : 0L;
                foreach (var x in Iterate(a[0], line))
                {
                    if (!PyOps.IsNum(x)) throw new PyError("TypeError", "sum() складывает только числа, а в коллекции есть " + PyOps.TypeNameRu(x) + " " + PyOps.Repr(x) + "." + (x is string ? " Преврати строки в числа: [int(x) for x in ...]." : ""), line);
                    acc = BinOp("+", acc, x, line);
                }
                return acc;
            });
            add("sorted", (a, kw, line) =>
            {
                Need(a, 1, 1, "sorted", line);
                var l = Iterate(a[0], line).ToList();
                SortList(l, kw, line);
                return new PyList(l);
            });
            add("reversed", (a, kw, line) => { Need(a, 1, 1, "reversed", line); var l = Iterate(a[0], line).ToList(); l.Reverse(); return new PyList(l); });
            add("enumerate", (a, kw, line) =>
            {
                Need(a, 1, 2, "enumerate", line);
                long start = a.Count == 2 ? PyOps.ToLong(a[1]) : 0;
                var r = new PyList(); foreach (var x in Iterate(a[0], line)) r.Items.Add(new PyTuple(new object[] { start++, x }));
                return r;
            });
            add("zip", (a, kw, line) =>
            {
                var lists = a.Select(x => Iterate(x, line).ToList()).ToList();
                var r = new PyList(); if (lists.Count == 0) return r;
                int n = lists.Min(x => x.Count);
                for (int i = 0; i < n; i++) r.Items.Add(new PyTuple(lists.Select(x => x[i]).ToArray()));
                return r;
            });
            add("type", (a, kw, line) => { Need(a, 1, 1, "type", line); return new PyTypeObj(PyOps.TypeName(a[0])); });
            add("isinstance", (a, kw, line) =>
            {
                Need(a, 2, 2, "isinstance", line);
                var tn = a[1] is PyBuiltin ? ((PyBuiltin)a[1]).Name : a[1] is PyTypeObj ? ((PyTypeObj)a[1]).Name : "";
                var n = PyOps.TypeName(a[0]);
                return n == tn || (tn == "int" && n == "bool");
            });
            add("ord", (a, kw, line) => { Need(a, 1, 1, "ord", line); var s = a[0] as string; if (s == null || s.Length != 1) throw new PyError("TypeError", "ord() принимает один символ.", line); return (long)s[0]; });
            add("chr", (a, kw, line) => { Need(a, 1, 1, "chr", line); return ((char)PyOps.ToLong(a[0])).ToString(); });
        }

        object MinMax(List<object> a, bool isMax, int line)
        {
            string name = isMax ? "max" : "min";
            var items = a.Count == 1 ? Iterate(a[0], line).ToList() : a;
            if (items.Count == 0) throw new PyError("ValueError", name + "() получила пустую коллекцию — не из чего выбирать.", line);
            var best = items[0];
            foreach (var x in items.Skip(1))
            {
                int c = PyOps.Compare(x, best, line);
                if (isMax ? c > 0 : c < 0) best = x;
            }
            return best;
        }

        void SortList(List<object> l, Dictionary<string, object> kw, int line)
        {
            bool rev = kw.ContainsKey("reverse") && PyOps.Truthy(kw["reverse"]);
            object key = kw.ContainsKey("key") ? kw["key"] : null;
            var keys = l.Select(x => key == null ? x : Call(key, new List<object> { x }, new Dictionary<string, object>(), line)).ToList();
            var idx = Enumerable.Range(0, l.Count).ToList();
            // стабильная сортировка
            var sorted = idx.OrderBy(i => i, Comparer<int>.Create((x, y) =>
            {
                int c = PyOps.Compare(keys[x], keys[y], line);
                if (rev) c = -c;
                return c != 0 ? c : x.CompareTo(y);
            })).Select(i => l[i]).ToList();
            l.Clear(); l.AddRange(sorted);
        }

        // ---------- Методы ----------
        static readonly Dictionary<string, string[]> Methods = new Dictionary<string, string[]>
        {
            { "str", new[] { "upper","lower","strip","lstrip","rstrip","split","join","replace","startswith","endswith","find","count","isdigit","isalpha","capitalize","title","index" } },
            { "list", new[] { "append","pop","insert","remove","index","count","sort","reverse","clear","copy","extend" } },
            { "dict", new[] { "get","keys","values","items","pop","update","clear","copy","setdefault" } },
        };

        bool HasMethod(object o, string name)
        {
            string[] ms;
            return Methods.TryGetValue(PyOps.TypeName(o), out ms) && ms.Contains(name);
        }

        string MethodHint(object o, string name)
        {
            if (o is string && name == "append") return " Строки неизменяемы: вместо append используй сложение s = s + \"...\".";
            if (o is PyList && (name == "add" || name == "push")) return " Чтобы добавить элемент в список, используй .append(x).";
            if (o is PyList && name == "length") return " Длину считают функцией len(список).";
            string[] ms;
            if (Methods.TryGetValue(PyOps.TypeName(o), out ms)) return " Доступные методы: " + string.Join(", ", ms) + ".";
            return "";
        }

        object CallMethod(object self, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            if (self is string)
            {
                var s = (string)self;
                switch (name)
                {
                    case "upper": return s.ToUpperInvariant();
                    case "lower": return s.ToLowerInvariant();
                    case "strip": return a.Count > 0 ? s.Trim(((string)a[0]).ToCharArray()) : s.Trim();
                    case "lstrip": return a.Count > 0 ? s.TrimStart(((string)a[0]).ToCharArray()) : s.TrimStart();
                    case "rstrip": return a.Count > 0 ? s.TrimEnd(((string)a[0]).ToCharArray()) : s.TrimEnd();
                    case "split":
                        if (a.Count == 0 || a[0] == null) return new PyList(s.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => (object)x));
                        if ((string)a[0] == "") throw new PyError("ValueError", "Разделитель в split() не может быть пустой строкой. Чтобы разбить на символы, используй list(строка).", line);
                        return new PyList(s.Split(new[] { (string)a[0] }, StringSplitOptions.None).Select(x => (object)x));
                    case "join":
                        Need(a, 1, 1, "join", line);
                        var parts = Iterate(a[0], line).ToList();
                        foreach (var x in parts) if (!(x is string))
                                throw new PyError("TypeError", "join() склеивает только строки, а в списке есть " + PyOps.TypeNameRu(x) + " " + PyOps.Repr(x) + ". Преврати элементы в строки: [str(x) for x in список].", line);
                        return string.Join(s, parts.Select(x => (string)x).ToArray());
                    case "replace": Need(a, 2, 2, "replace", line); return s.Replace(PyOps.Str(a[0]), PyOps.Str(a[1]));
                    case "startswith": return s.StartsWith(PyOps.Str(a[0]), StringComparison.Ordinal);
                    case "endswith": return s.EndsWith(PyOps.Str(a[0]), StringComparison.Ordinal);
                    case "find": return (long)s.IndexOf(PyOps.Str(a[0]), StringComparison.Ordinal);
                    case "index":
                        { int i = s.IndexOf(PyOps.Str(a[0]), StringComparison.Ordinal); if (i < 0) throw new PyError("ValueError", "Подстрока не найдена.", line); return (long)i; }
                    case "count":
                        { var sub = PyOps.Str(a[0]); if (sub.Length == 0) return (long)(s.Length + 1); long c = 0; int i = 0; while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { c++; i += sub.Length; } return c; }
                    case "isdigit": return s.Length > 0 && s.All(char.IsDigit);
                    case "isalpha": return s.Length > 0 && s.All(char.IsLetter);
                    case "capitalize": return s.Length == 0 ? s : char.ToUpper(s[0]) + s.Substring(1).ToLower();
                    case "title": return Inv.TextInfo.ToTitleCase(s.ToLower());
                }
            }
            if (self is PyList)
            {
                var l = ((PyList)self).Items;
                switch (name)
                {
                    case "append": Need(a, 1, 1, "append", line); l.Add(a[0]); return null;
                    case "extend": Need(a, 1, 1, "extend", line); l.AddRange(Iterate(a[0], line).ToList()); return null;
                    case "insert": Need(a, 2, 2, "insert", line); { long i = PyOps.ToLong(a[0]); if (i < 0) i = Math.Max(0, i + l.Count); l.Insert((int)Math.Min(i, l.Count), a[1]); } return null;
                    case "pop":
                        {
                            if (l.Count == 0) throw new PyError("IndexError", "pop() из пустого списка — удалять нечего.", line);
                            int i = a.Count == 0 ? l.Count - 1 : NormIndex(a[0], l.Count, line, "списка");
                            var v = l[i]; l.RemoveAt(i); return v;
                        }
                    case "remove":
                        {
                            int i = l.FindIndex(x => PyOps.Eq(x, a[0]));
                            if (i < 0) throw new PyError("ValueError", "remove(): элемента " + PyOps.Repr(a[0]) + " нет в списке.", line);
                            l.RemoveAt(i); return null;
                        }
                    case "index":
                        {
                            int i = l.FindIndex(x => PyOps.Eq(x, a[0]));
                            if (i < 0) throw new PyError("ValueError", "index(): элемента " + PyOps.Repr(a[0]) + " нет в списке.", line);
                            return (long)i;
                        }
                    case "count": return (long)l.Count(x => PyOps.Eq(x, a[0]));
                    case "sort": SortList(l, kw, line); return null;
                    case "reverse": l.Reverse(); return null;
                    case "clear": l.Clear(); return null;
                    case "copy": return new PyList(l);
                }
            }
            if (self is PyDict)
            {
                var d = (PyDict)self;
                switch (name)
                {
                    case "get": { Need(a, 1, 2, "get", line); object v; return d.Map.TryGetValue(a[0], out v) ? v : (a.Count > 1 ? a[1] : null); }
                    case "keys": return new PyList(d.Keys);
                    case "values": return new PyList(d.Keys.Select(k => d.Map[k]));
                    case "items": return new PyList(d.Keys.Select(k => (object)new PyTuple(new[] { k, d.Map[k] })));
                    case "pop":
                        {
                            object v;
                            if (d.Map.TryGetValue(a[0], out v)) { d.Remove(a[0]); return v; }
                            if (a.Count > 1) return a[1];
                            throw new PyError("KeyError", "Ключа " + PyOps.Repr(a[0]) + " нет в словаре.", line);
                        }
                    case "update": { var o = a[0] as PyDict; if (o != null) foreach (var k in o.Keys) d.Set(k, o.Map[k]); return null; }
                    case "setdefault": { object v; if (d.Map.TryGetValue(a[0], out v)) return v; var dv = a.Count > 1 ? a[1] : null; d.Set(a[0], dv); return dv; }
                    case "clear": d.Keys.Clear(); d.Map.Clear(); return null;
                    case "copy": { var c = new PyDict(); foreach (var k in d.Keys) c.Set(k, d.Map[k]); return c; }
                }
            }
            throw new PyError("AttributeError", "Метод «" + name + "» не найден.", line);
        }
    }
}
