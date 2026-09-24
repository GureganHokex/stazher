// Игровой Python: выполнение программы.
// Перед каждой инструкцией вызывается OnLine — так работает пошаговый дебаггер.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Intern.Py
{
    public class Frame
    {
        public string Name;                                   // "<модуль>" или имя функции
        public Dictionary<string, object> Vars = new Dictionary<string, object>();
        public List<string> Order = new List<string>();       // порядок появления переменных (для окна переменных)
        public Frame Parent;                                  // объемлющая область (замыкания)
        public ScopeInfo Info;                                // для кадров функций: какие имена локальные
        public PyFunction Func;
        public bool IsModule, IsClass, IsComp;
        public string Qual;                                   // __qualname__ области: f, f.<locals>.g, C, <listcomp>
        // префикс __qualname__ для того, что объявлено в этой области (как в CPython)
        public string QualPrefix { get { return IsModule || Qual == null ? "" : IsClass || IsComp ? Qual + "." : Qual + ".<locals>."; } }
        public void Set(string k, object v) { if (!Vars.ContainsKey(k)) Order.Add(k); Vars[k] = v; }
        public bool Del(string k) { if (!Vars.Remove(k)) return false; Order.Remove(k); return true; }
    }

    public class VarInfo { public string Scope, Name, Type, Value; }

    public class PySlice { public object Start, Stop, Step; }

    public partial class Interp
    {
        enum Flow { Normal, Break, Continue, Return }

        [ThreadStatic] public static Interp Current;

        public Frame Globals = new Frame { Name = "<модуль>", IsModule = true };
        public List<Frame> Stack = new List<Frame>();
        public Dictionary<string, object> Builtins = new Dictionary<string, object>();

        public Action<int, Interp> OnLine;       // хук перед каждой строкой
        public Action<string> OnOutput;          // всё, что видно в консоли
        public StringBuilder Printed = new StringBuilder(); // то, что программа вывела в stdout (print и подсказки input) — для проверки задач
        public Queue<string> Inputs = new Queue<string>();

        public int MaxSteps = 1000000;
        public int Steps;
        public int MaxDepth = 1000;              // глубина рекурсии, как sys.getrecursionlimit() в CPython
        public int MaxOutput = 4000000;
        public int Depth { get { return Stack.Count; } }
        public int CurrentLine;

        object retVal;
        readonly List<PyError> handling = new List<PyError>();
        static readonly Dictionary<string, object> NoKw = new Dictionary<string, object>();
        static readonly List<object> NoArgs = new List<object>();
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public Interp()
        {
            Stack.Add(Globals);
            InitTypes();
            InitBuiltins();
        }

        Frame Cur { get { return Stack[Stack.Count - 1]; } }

        public void Run(List<Stmt> program)
        {
            var prev = Current; Current = this;
            try
            {
                var fl = ExecBlock(program, Globals);
                if (fl == Flow.Break || fl == Flow.Continue)
                    throw new PyError("SyntaxError", "break/continue можно использовать только внутри цикла.", "'break' outside loop", CurrentLine);
            }
            catch (PyError e)
            {
                if (e.PyType == "SystemExit") return;
                throw;
            }
            finally { Current = prev; }
        }

        // Выполнить код в уже созданном интерпретаторе (для проверки функций)
        public void Enter() { Current = this; }

        void Out(string s)
        {
            if (OnOutput != null) OnOutput(s);
        }
        void Print(string s)
        {
            if (Printed.Length + s.Length > MaxOutput)
                throw new PyError("MemoryError", "Программа печатает слишком много текста — похоже на бесконечный цикл с print.", CurrentLine) { Fatal = true };
            Printed.Append(s); Out(s);
        }

        // ---------- Снимок переменных для дебаггера ----------
        public List<VarInfo> Snapshot()
        {
            var res = new List<VarInfo>();
            if (Stack.Count > 1)
            {
                var f = Cur;
                foreach (var k in f.Order) if (f.Vars.ContainsKey(k) && Visible(f.Vars[k])) res.Add(Info("внутри " + f.Name + "()", k, f.Vars[k]));
            }
            foreach (var k in Globals.Order)
            {
                if (!Globals.Vars.ContainsKey(k)) continue;
                var v = Globals.Vars[k];
                if (Visible(v)) res.Add(Info("глобальная", k, v));
            }
            return res;
        }
        static bool Visible(object v) { return !(v is PyModule) && !(v is PyClass); }

        static VarInfo Info(string scope, string name, object v)
        {
            string r = PyOps.SafeRepr(v);
            if (r.Length > 120) r = r.Substring(0, 117) + "...";
            bool fn = v is PyFunction || v is PyBuiltin || v is PyMethod || v is PyBoundMethod;
            return new VarInfo { Scope = scope, Name = name, Type = fn ? "function" : PyOps.TypeName(v), Value = r };
        }

        // ---------- Инструкции ----------
        Flow ExecBlock(List<Stmt> body, Frame f)
        {
            for (int i = 0; i < body.Count; i++)
            {
                var fl = Exec(body[i], f);
                if (fl != Flow.Normal) return fl;
            }
            return Flow.Normal;
        }

        void Tick(Node s)
        {
            CurrentLine = s.Line;
            if (++Steps > MaxSteps)
                throw new PyError("TimeoutError", "Программа сделала слишком много шагов — похоже на бесконечный цикл. Проверь, что условие while когда-нибудь становится ложным (например, переменная меняется внутри цикла).", s.Line) { Fatal = true };
            if (OnLine != null) OnLine(s.Line, this);
        }

        void CountStep(int line)
        {
            if (++Steps > MaxSteps)
                throw new PyError("TimeoutError", "Программа сделала слишком много шагов — похоже на бесконечный цикл или слишком большой объём вычислений.", line) { Fatal = true };
        }

        Flow Exec(Stmt s, Frame f)
        {
            Tick(s);
            bool done = false;
            try
            {
                var r = ExecInner(s, f);
                done = true;
                return r;
            }
            finally
            {
                // ошибка без номера строки получает строку самой внутренней инструкции (catch + throw при глубокой рекурсии очень медленный)
                if (!done) { var e = PyError.LastCreated; if (e != null && e.Line == 0) e.Line = s.Line; }
            }
        }

        Flow ExecInner(Stmt s, Frame f)
        {
            var es = s as ExprS;
            if (es != null) { Eval(es.E, f); return Flow.Normal; }
            var a = s as AssignS;
            if (a != null)
            {
                var v = Eval(a.Value, f);
                for (int i = 0; i < a.Targets.Count; i++) Assign(a.Targets[i], v, f);
                return Flow.Normal;
            }
            var aug = s as AugAssignS;
            if (aug != null) { AugAssign(aug, f); return Flow.Normal; }
            var ifs = s as IfS;
            if (ifs != null)
            {
                if (PyOps.Truthy(Eval(ifs.Cond, f))) return ExecBlock(ifs.Body, f);
                if (ifs.Else != null) return ExecBlock(ifs.Else, f);
                return Flow.Normal;
            }
            var fs = s as ForS;
            if (fs != null) return ExecFor(fs, f);
            var ws = s as WhileS;
            if (ws != null)
            {
                bool first = true;
                while (true)
                {
                    if (!first) Tick(s);
                    first = false;
                    if (!PyOps.Truthy(Eval(ws.Cond, f))) break;
                    var fl = ExecBlock(ws.Body, f);
                    if (fl == Flow.Break) return Flow.Normal;
                    if (fl == Flow.Return) return fl;
                }
                if (ws.OrElse != null) return ExecBlock(ws.OrElse, f);
                return Flow.Normal;
            }
            var rs = s as ReturnS;
            if (rs != null)
            {
                if (f.Info == null) throw new PyError("SyntaxError", "return можно писать только внутри функции (после def).", "'return' outside function", s.Line);
                retVal = rs.Value == null ? null : Eval(rs.Value, f);
                return Flow.Return;
            }
            if (s is BreakS) return Flow.Break;
            if (s is ContinueS) return Flow.Continue;
            if (s is PassS) return Flow.Normal;
            var ds = s as DefS;
            if (ds != null)
            {
                var decos = new List<object>();
                foreach (var d in ds.Decorators) decos.Add(Eval(d, f));
                object fn = MakeFunction(ds.Name, ds.Params, ds.Body, null, ds.Scope, f, ds.Line);
                for (int i = decos.Count - 1; i >= 0; i--) fn = Call(decos[i], new List<object> { fn }, NoKw, ds.Line);
                StoreName(ds.Name, fn, f, s.Line);
                return Flow.Normal;
            }
            var ts = s as TryS;
            if (ts != null) return ExecTry(ts, f);
            var cs = s as ClassS;
            if (cs != null) { ExecClass(cs, f); return Flow.Normal; }
            var ra = s as RaiseS;
            if (ra != null) throw DoRaise(ra, f);
            var ann = s as AnnAssignS;
            if (ann != null) { if (ann.Value != null) Assign(ann.Target, Eval(ann.Value, f), f); return Flow.Normal; }
            var asr = s as AssertS;
            if (asr != null)
            {
                if (!PyOps.Truthy(Eval(asr.Test, f)))
                {
                    object[] args = asr.Msg != null ? new[] { Eval(asr.Msg, f) } : new object[0];
                    string code = Explainer.Code(asr.Test);
                    throw new PyError("AssertionError", "Проверка assert не прошла: условие «" + code + "» оказалось ложным." +
                        (args.Length > 0 ? " Сообщение: " + PyOps.Str(args[0]) : ""), s.Line)
                    { ExcArgs = args, PyMsg = args.Length > 0 ? PyOps.Str(args[0]) : "" };
                }
                return Flow.Normal;
            }
            var del = s as DelS;
            if (del != null) { foreach (var tg in del.Targets) DeleteTarget(tg, f); return Flow.Normal; }
            if (s is GlobalS || s is NonlocalS) return Flow.Normal;
            var im = s as ImportS;
            if (im != null)
            {
                foreach (var kv in im.Names)
                {
                    var m = ImportModule(kv.Key, s.Line);
                    StoreName(kv.Value ?? kv.Key.Split('.')[0], m, f, s.Line);
                }
                return Flow.Normal;
            }
            var imf = s as ImportFromS;
            if (imf != null)
            {
                var m = ImportModule(imf.Module, s.Line);
                if (imf.Star) { foreach (var kv in m.Dict) if (!kv.Key.StartsWith("_")) StoreName(kv.Key, kv.Value, f, s.Line); return Flow.Normal; }
                foreach (var kv in imf.Names)
                {
                    object v;
                    if (!m.Dict.TryGetValue(kv.Key, out v))
                        throw new PyError("ImportError", "В модуле " + imf.Module + " нет «" + kv.Key + "» (или оно недоступно в игровом Python).",
                            "cannot import name '" + kv.Key + "' from '" + imf.Module + "'", s.Line);
                    StoreName(kv.Value ?? kv.Key, v, f, s.Line);
                }
                return Flow.Normal;
            }
            throw new PyError("SyntaxError", "Неизвестная инструкция.", s.Line);
        }

        Flow ExecFor(ForS fs, Frame f)
        {
            var iterable = Eval(fs.Iter, f);
            bool first = true;
            foreach (var item in IterateRaw(iterable, fs.Line))   // шаги считает сам цикл (Tick на каждой итерации)
            {
                if (!first) Tick(fs);
                first = false;
                Assign(fs.Target, item, f);
                var fl = ExecBlock(fs.Body, f);
                if (fl == Flow.Break) return Flow.Normal;
                if (fl == Flow.Return) return fl;
            }
            if (fs.OrElse != null) return ExecBlock(fs.OrElse, f);
            return Flow.Normal;
        }

        Flow ExecTry(TryS t, Frame f)
        {
            Flow flow = Flow.Normal;
            PyError pending = null;
            try
            {
                bool caught = false;
                try { flow = ExecBlock(t.Body, f); }
                catch (PyError e)
                {
                    if (e.Fatal) throw;
                    var h = FindHandler(t, e, f);
                    if (h == null) throw;
                    caught = true;
                    handling.Add(e);
                    try
                    {
                        if (h.Name != null) StoreName(h.Name, ExcValue(e), f, h.Line);
                        flow = ExecBlock(h.Body, f);
                    }
                    finally
                    {
                        handling.RemoveAt(handling.Count - 1);
                        if (h.Name != null) DeleteNameQuiet(h.Name, f);
                    }
                }
                if (!caught && flow == Flow.Normal && t.Else != null) flow = ExecBlock(t.Else, f);
            }
            catch (PyError e2)
            {
                if (t.Finally == null || e2.Fatal) throw;
                pending = e2;
            }
            if (t.Finally != null)
            {
                object saved = retVal;
                var ff = ExecBlock(t.Finally, f);
                if (ff != Flow.Normal) return ff;   // return/break в finally отменяет исключение — как в CPython
                if (pending != null) throw pending;
                retVal = saved;
            }
            return flow;
        }

        Handler FindHandler(TryS t, PyError e, Frame f)
        {
            PyClass cls = null;
            foreach (var h in t.Handlers)
            {
                if (h.Type == null) return h;
                var ty = Eval(h.Type, f);
                if (cls == null) cls = ExcClassOf(e);
                if (ExcMatches(cls, ty, h.Line)) return h;
            }
            return null;
        }

        bool ExcMatches(PyClass cls, object ty, int line)
        {
            var tup = ty as PyTuple;
            if (tup != null) { foreach (var x in tup.Items) if (ExcMatches(cls, x, line)) return true; return false; }
            var c = ty as PyClass;
            if (c == null || !(c.IsException || c == BaseExceptionClass))
                throw new PyError("TypeError", "После except нужно указать класс исключения (например ValueError), а тут " + PyOps.TypeNameRu(ty) + ".",
                    "catching classes that do not inherit from BaseException is not allowed", line);
            return cls.IsSubOf(c);
        }

        PyError DoRaise(RaiseS r, Frame f)
        {
            if (r.Exc == null)
            {
                if (handling.Count == 0)
                    return new PyError("RuntimeError", "raise без аргументов можно писать только внутри блока except — он повторно выбрасывает пойманное исключение.", "No active exception to reraise", r.Line);
                return handling[handling.Count - 1];
            }
            var v = Eval(r.Exc, f);
            object cause = null; bool hasCause = r.Cause != null;
            if (hasCause)
            {
                cause = Eval(r.Cause, f);
                var cc = cause as PyClass;
                if (cc != null && (cc.IsException || cc == BaseExceptionClass)) cause = Instantiate(cc, new List<object>(), NoKw, r.Line);
                if (cause != null && !(cause is PyInstance && ((PyInstance)cause).Cls.IsException))
                    throw new PyError("TypeError", "После from должно стоять исключение или None.", "exception causes must derive from BaseException", r.Line);
            }
            var err = MakeRaise(v, r.Line);
            var ei = err.Value as PyInstance;
            if (ei != null)
            {
                if (hasCause) { ei.Attrs["__cause__"] = cause; ei.Attrs["__suppress_context__"] = true; }
                if (handling.Count > 0)
                {
                    var ctx = ExcValue(handling[handling.Count - 1]);
                    if (!ReferenceEquals(ctx, ei)) ei.Attrs["__context__"] = ctx;
                }
            }
            return err;
        }

        public PyError MakeRaise(object v, int line)
        {
            var cls = v as PyClass;
            if (cls != null && (cls.IsException || cls == BaseExceptionClass)) v = Instantiate(cls, new List<object>(), NoKw, line);
            var inst = v as PyInstance;
            if (inst == null || !inst.Cls.IsException)
                throw new PyError("TypeError", "raise работает только с исключениями (классами-наследниками Exception), а тут " + PyOps.TypeNameRu(v) + ".",
                    "exceptions must derive from BaseException", line);
            string msg;
            try { msg = InstanceStr(inst); } catch (PyError) { msg = ""; }
            string ru = msg.Length == 0
                ? "Выброшено исключение " + inst.Cls.Name + " (без сообщения), и его не перехватил ни один try/except."
                : msg + "\n(Это исключение " + inst.Cls.Name + " выброшено через raise и не перехвачено try/except.)";
            return new PyError(inst.Cls.Name, ru, line) { Value = inst, PyMsg = msg };
        }

        void ExecClass(ClassS cs, Frame f)
        {
            var bases = new List<PyClass>();
            foreach (var b in cs.Bases)
            {
                var bv = Eval(b, f) as PyClass;
                if (bv == null) throw new PyError("TypeError", "В скобках после имени класса должны стоять классы-родители.", "bases must be types", cs.Line);
                if (bv.Module == "typing") continue;   // Generic[T], Protocol — только для подсказок типов
                if (bv.Builtin && !bv.IsException && bv != ObjectClass && bv != BaseExceptionClass)
                    throw new PyError("TypeError", "Наследоваться от встроенного типа " + bv.Name + " в игровом Python нельзя. Храни " + bv.Name + " в атрибуте объекта (композиция).", cs.Line);
                bases.Add(bv);
            }
            if (bases.Count == 0) bases.Add(ObjectClass);
            var cf = new Frame { Name = cs.Name, IsClass = true, Parent = f, Qual = f.QualPrefix + cs.Name };
            var fl = ExecBlock(cs.Body, cf);
            if (fl != Flow.Normal) throw new PyError("SyntaxError", "return/break/continue нельзя писать прямо в теле класса.", cs.Line);
            var cls = new PyClass { Name = cs.Name, Bases = bases };
            cls.Mro = ComputeMro(cls, cs.Line);
            cls.IsException = bases.Any(b => b.IsException || b == BaseExceptionClass);
            foreach (var k in cf.Order)
            {
                if (!cf.Vars.ContainsKey(k)) continue;
                var v = cf.Vars[k];
                if (k == "__new__" && v is PyFunction) v = new PyStaticMethod { Func = v };
                SetDefiningClass(v, cls);
                cls.Set(k, v);
            }
            if (cls.Dict.ContainsKey("__eq__") && !cls.Dict.ContainsKey("__hash__")) cls.Set("__hash__", null);
            if (!cls.Dict.ContainsKey("__doc__"))   // докстринг класса (не наследуется, поэтому None у каждого класса без него)
            {
                var ds0 = cs.Body.Count > 0 ? cs.Body[0] as ExprS : null;
                var dc = ds0 != null ? ds0.E as ConstE : null;
                cls.Dict["__doc__"] = dc != null && dc.V is string ? dc.V : null;
            }
            object res = cls;
            for (int i = cs.Decorators.Count - 1; i >= 0; i--) res = Call(Eval(cs.Decorators[i], f), new List<object> { res }, NoKw, cs.Line);
            StoreName(cs.Name, res, f, cs.Line);
        }

        static void SetDefiningClass(object v, PyClass cls)
        {
            var fn = v as PyFunction;
            if (fn != null) { if (fn.DefiningClass == null) fn.DefiningClass = cls; return; }
            if (v is PyStaticMethod) SetDefiningClass(((PyStaticMethod)v).Func, cls);
            else if (v is PyClassMethod) SetDefiningClass(((PyClassMethod)v).Func, cls);
            else if (v is PyProperty) { var p = (PyProperty)v; SetDefiningClass(p.Fget, cls); SetDefiningClass(p.Fset, cls); SetDefiningClass(p.Fdel, cls); }
        }

        List<PyClass> ComputeMro(PyClass cls, int line)
        {
            var seqs = new List<List<PyClass>>();
            foreach (var b in cls.Bases) seqs.Add(new List<PyClass>(b.Mro));
            seqs.Add(new List<PyClass>(cls.Bases));
            var res = new List<PyClass> { cls };
            while (true)
            {
                seqs.RemoveAll(s => s.Count == 0);
                if (seqs.Count == 0) return res;
                PyClass cand = null;
                foreach (var s in seqs)
                {
                    var h = s[0];
                    if (!seqs.Any(o => o.IndexOf(h) > 0)) { cand = h; break; }
                }
                if (cand == null) throw new PyError("TypeError", "Не получается выстроить порядок наследования (MRO) — проверь список родителей класса.", "Cannot create a consistent method resolution order (MRO)", line);
                res.Add(cand);
                foreach (var s in seqs) if (s.Count > 0 && s[0] == cand) s.RemoveAt(0);
            }
        }

        PyFunction MakeFunction(string name, List<Param> ps, List<Stmt> body, Expr lambdaBody, ScopeInfo scope, Frame f, int line)
        {
            var fn = new PyFunction { Name = name, Params = ps, Body = body, LambdaBody = lambdaBody, Scope = scope, Closure = f, Line = line, Qual = f.QualPrefix + name };
            fn.Defaults = new object[ps.Count];
            for (int i = 0; i < ps.Count; i++) fn.Defaults[i] = ps[i].Default != null ? Eval(ps[i].Default, f) : PyFunction.NoDefault;
            return fn;
        }

        // ---------- Имена ----------
        object LoadName(string id, Frame f, int line)
        {
            object v;
            if (f.Info != null)
            {
                if (f.Info.Locals.Contains(id))
                {
                    if (f.Vars.TryGetValue(id, out v)) return v;
                    throw new PyError("UnboundLocalError",
                        "Переменная «" + id + "» используется внутри функции раньше, чем ей присвоили значение. Python считает её локальной, потому что ниже в функции есть присваивание " + id + " = .... " +
                        "Если хотел изменить глобальную переменную — напиши в начале функции: global " + id + ".",
                        "cannot access local variable '" + id + "' where it is not associated with a value", line);
                }
                if (!f.Info.Globals.Contains(id))
                    for (var e = f.Parent; e != null && !e.IsModule; e = e.Parent)
                    {
                        if (e.IsClass) continue;
                        if (e.Vars.TryGetValue(id, out v)) return v;
                    }
            }
            else
            {
                if (f.Vars.TryGetValue(id, out v)) return v;
                for (var e = f.Parent; e != null && !e.IsModule; e = e.Parent)
                {
                    if (e.IsClass) continue;
                    if (e.Vars.TryGetValue(id, out v)) return v;
                }
            }
            if (Globals.Vars.TryGetValue(id, out v)) return v;
            if (Builtins.TryGetValue(id, out v)) return v;
            throw NameErr(id, f, line);
        }

        PyError NameErr(string id, Frame f, int line)
        {
            var all = new List<string>(Globals.Vars.Keys);
            for (var e = f; e != null; e = e.Parent) all.AddRange(e.Vars.Keys);
            all.AddRange(Builtins.Keys);
            string close = all.FirstOrDefault(n => Similar(n, id));
            string hint;
            if (close != null) hint = " Может, ты имел в виду «" + close + "»?";
            else if (id == "Print" || id == "PRINT") hint = " Python различает большие и маленькие буквы: нужно print.";
            else if (id == "self") hint = " self доступен только внутри методов класса — первым параметром: def метод(self, ...).";
            else if (id == "math" || id == "json" || id == "Counter" || id == "defaultdict") hint = " Похоже, забыт импорт: " + (id == "Counter" || id == "defaultdict" ? "from collections import " + id : "import " + id) + ".";
            else hint = " Возможно, опечатка, или переменная создаётся ниже, чем используется. Если это текст — возьми его в кавычки.";
            return new PyError("NameError", "Имя «" + id + "» не найдено." + hint, "name '" + id + "' is not defined", line);
        }

        static bool Similar(string a, string b)
        {
            if (a == b) return false;
            if (a.ToLower() == b.ToLower()) return true;
            if (Math.Abs(a.Length - b.Length) > 1 || a.Length < 3) return false;
            int n = a.Length, m = b.Length; var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[n, m] <= 1;
        }

        void StoreName(string id, object v, Frame f, int line)
        {
            if (f.Info != null)
            {
                if (f.Info.Globals.Contains(id)) { Globals.Set(id, v); return; }
                if (f.Info.Nonlocals.Contains(id))
                {
                    var e = FindNonlocal(id, f);
                    if (e == null) throw new PyError("SyntaxError", "nonlocal " + id + ": во внешней функции нет такой переменной.", "no binding for nonlocal '" + id + "' found", line);
                    e.Set(id, v); return;
                }
            }
            f.Set(id, v);
        }

        static Frame FindNonlocal(string id, Frame f)
        {
            for (var e = f.Parent; e != null && !e.IsModule; e = e.Parent)
            {
                if (e.IsClass || e.IsComp) continue;
                if ((e.Info != null && e.Info.Locals.Contains(id)) || e.Vars.ContainsKey(id)) return e;
            }
            return null;
        }

        void DeleteName(string id, Frame f, int line)
        {
            bool ok;
            if (f.Info != null && f.Info.Globals.Contains(id)) ok = Globals.Del(id);
            else if (f.Info != null && f.Info.Nonlocals.Contains(id)) { var e = FindNonlocal(id, f); ok = e != null && e.Del(id); }
            else ok = f.Del(id);
            if (!ok) throw new PyError("NameError", "Нельзя удалить «" + id + "» через del — такой переменной нет.", "name '" + id + "' is not defined", line);
        }
        void DeleteNameQuiet(string id, Frame f)
        {
            if (f.Info != null && f.Info.Globals.Contains(id)) Globals.Del(id); else f.Del(id);
        }

        void DeleteTarget(Expr t, Frame f)
        {
            if (t is NameE) { DeleteName(((NameE)t).Id, f, t.Line); return; }
            if (t is TupleE) { foreach (var x in ((TupleE)t).Items) DeleteTarget(x, f); return; }
            if (t is ListE) { foreach (var x in ((ListE)t).Items) DeleteTarget(x, f); return; }
            if (t is AttrE) { var a = (AttrE)t; DelAttr(Eval(a.Obj, f), a.Name, t.Line); return; }
            var ie = (IndexE)t;
            var obj = Eval(ie.Obj, f);
            if (ie.Idx is SliceE) DelSlice(obj, EvalSlice((SliceE)ie.Idx, f), t.Line);
            else DelItem(obj, Eval(ie.Idx, f), t.Line);
        }

        // ---------- Присваивание ----------
        void Assign(Expr target, object v, Frame f)
        {
            var ne = target as NameE;
            if (ne != null) { StoreName(ne.Id, v, f, target.Line); return; }
            if (target is TupleE || target is ListE)
            {
                var names = target is TupleE ? ((TupleE)target).Items : ((ListE)target).Items;
                Unpack(names, v, f, target.Line);
                return;
            }
            var ie = target as IndexE;
            if (ie != null)
            {
                var obj = Eval(ie.Obj, f);
                if (ie.Idx is SliceE) SetSlice(obj, EvalSlice((SliceE)ie.Idx, f), v, target.Line);
                else SetItem(obj, Eval(ie.Idx, f), v, target.Line);
                return;
            }
            var ae = target as AttrE;
            if (ae != null) { SetAttr(Eval(ae.Obj, f), ae.Name, v, target.Line); return; }
            throw new PyError("SyntaxError", "Так присваивать нельзя.", target.Line);
        }

        void Unpack(List<Expr> names, object v, Frame f, int line)
        {
            if (!IsIterable(v))
                throw new PyError("TypeError", "Нельзя разложить " + PyOps.TypeNameRu(v) + " по нескольким переменным — справа должна быть коллекция (список, кортеж, строка).",
                    "cannot unpack non-iterable " + PyOps.TypeName(v) + " object", line);
            List<object> vals;
            if (v is PyTuple) vals = new List<object>(((PyTuple)v).Items);
            else if (v is PyList) vals = new List<object>(((PyList)v).Items);
            else vals = ToList(v, line);
            int star = names.FindIndex(x => x is StarredE);
            if (star < 0)
            {
                if (vals.Count != names.Count)
                {
                    string pm = vals.Count > names.Count ? "too many values to unpack (expected " + names.Count + ")" : "not enough values to unpack (expected " + names.Count + ", got " + vals.Count + ")";
                    throw new PyError("ValueError", "Слева " + names.Count + " переменных, а справа " + vals.Count + " значений — количество должно совпадать.", pm, line);
                }
                for (int i = 0; i < names.Count; i++) Assign(names[i], vals[i], f);
                return;
            }
            int before = star, after = names.Count - star - 1;
            if (vals.Count < before + after)
                throw new PyError("ValueError", "Справа слишком мало значений: нужно хотя бы " + (before + after) + ", а их " + vals.Count + ".",
                    "not enough values to unpack (expected at least " + (before + after) + ", got " + vals.Count + ")", line);
            for (int i = 0; i < before; i++) Assign(names[i], vals[i], f);
            Assign(((StarredE)names[star]).E, PyList.Wrap(vals.GetRange(before, vals.Count - before - after)), f);
            for (int i = 0; i < after; i++) Assign(names[star + 1 + i], vals[vals.Count - after + i], f);
        }

        void AugAssign(AugAssignS a, Frame f)
        {
            int line = a.Line;
            var ne = a.Target as NameE;
            if (ne != null)
            {
                var cur = LoadName(ne.Id, f, line);
                StoreName(ne.Id, InplaceOp(a.Op, cur, Eval(a.Value, f), line), f, line);
                return;
            }
            var ae = a.Target as AttrE;
            if (ae != null)
            {
                var obj = Eval(ae.Obj, f);
                var cur = GetAttr(obj, ae.Name, line);
                SetAttr(obj, ae.Name, InplaceOp(a.Op, cur, Eval(a.Value, f), line), line);
                return;
            }
            var ie = (IndexE)a.Target;
            var o = Eval(ie.Obj, f);
            if (ie.Idx is SliceE)
            {
                var sl = EvalSlice((SliceE)ie.Idx, f);
                var cur = GetSlice(o, sl, line);
                SetSlice(o, sl, InplaceOp(a.Op, cur, Eval(a.Value, f), line), line);
                return;
            }
            var idx = Eval(ie.Idx, f);
            var c2 = GetItem(o, idx, line);
            SetItem(o, idx, InplaceOp(a.Op, c2, Eval(a.Value, f), line), line);
        }

        object InplaceOp(string op, object cur, object v, int line)
        {
            var l = cur as PyList;
            if (l != null)
            {
                if (op == "+")
                {
                    if (!IsIterable(v)) throw new PyError("TypeError", "К списку через += можно добавить только коллекцию. Чтобы добавить один элемент, используй .append(x).",
                        "'" + PyOps.TypeName(v) + "' object is not iterable", line);
                    var add = ToList(v, line);
                    SizeGuard((long)l.Items.Count + add.Count, line, false);
                    l.Items.AddRange(add); return l;
                }
                if (op == "*" && PyOps.IsInt(v)) { var rep = RepeatList(l.Items, PyOps.ToLong(v), line); l.Items.Clear(); l.Items.AddRange(rep); return l; }
            }
            var s = cur as PySet;
            if (s != null && !s.Frozen && v is PySet)
            {
                var o = (PySet)v;
                switch (op)
                {
                    case "|": s.Merge(o); return s;
                    case "&": { var keep = SetIntersection(s, o); if (!ReferenceEquals(keep, s)) s.TakeBody(keep); return s; }
                    case "-": DifferenceUpdate(s, o, line); return s;
                    case "^": foreach (var k in o.ToList()) { if (!s.Discard(k)) s.Add(k); } return s;
                }
            }
            var d = cur as PyDict;
            if (d != null && op == "|" && v is PyDict) { foreach (var p in ((PyDict)v).Pairs()) d.Set(p.Key, p.Value); return d; }
            if (d != null && d.Kind == DictKind.Counter && v is PyDict && (op == "+" || op == "-"))
            {
                var r = (PyDict)BinOp(op, d, v, line);
                d.Clear(); foreach (var p in r.Pairs()) d.Set(p.Key, p.Value); return d;
            }
            var inst = cur as PyInstance;
            if (inst != null)
            {
                string nm = InplaceName(op);
                if (nm != null && HasUser(inst.Cls, nm))
                {
                    var r = CallDunder(inst, nm, line, v);
                    if (!(r is PyNotImplemented)) return r;
                }
            }
            return BinOp(op, cur, v, line);
        }

        static string InplaceName(string op)
        {
            switch (op)
            {
                case "+": return "__iadd__"; case "-": return "__isub__"; case "*": return "__imul__"; case "/": return "__itruediv__";
                case "//": return "__ifloordiv__"; case "%": return "__imod__"; case "**": return "__ipow__";
                case "&": return "__iand__"; case "|": return "__ior__"; case "^": return "__ixor__";
            }
            return null;
        }

        // ---------- Выражения ----------
        public object Eval(Expr e) { return Eval(e, Cur); }

        public object Eval(Expr e, Frame f)
        {
            var ne = e as NameE;
            if (ne != null) return LoadName(ne.Id, f, e.Line);
            var ce = e as ConstE;
            if (ce != null) return ce.V;
            var be = e as BinE;
            if (be != null) return BinOp(be.Op, Eval(be.L, f), Eval(be.R, f), e.Line);
            var call = e as CallE;
            if (call != null) return EvalCall(call, f);
            var ae = e as AttrE;
            if (ae != null) return GetAttr(Eval(ae.Obj, f), ae.Name, e.Line);
            var cmp = e as CompareE;
            if (cmp != null)
            {
                var left = Eval(cmp.L, f);
                for (int i = 0; i < cmp.Ops.Count; i++)
                {
                    var right = Eval(cmp.Rs[i], f);
                    if (!CompareOp(cmp.Ops[i], left, right, e.Line)) return false;
                    left = right;
                }
                return true;
            }
            var ie = e as IndexE;
            if (ie != null)
            {
                var obj = Eval(ie.Obj, f);
                var sl = ie.Idx as SliceE;
                if (sl != null) return GetSlice(obj, EvalSlice(sl, f), e.Line);
                return GetItem(obj, Eval(ie.Idx, f), e.Line);
            }
            var bo = e as BoolOpE;
            if (bo != null)
            {
                var l = Eval(bo.L, f);
                if (bo.Op == "and") return PyOps.Truthy(l) ? Eval(bo.R, f) : l;
                return PyOps.Truthy(l) ? l : Eval(bo.R, f);
            }
            var ue = e as UnaryE;
            if (ue != null) return UnaryOp(ue.Op, Eval(ue.E, f), e.Line);
            var fe = e as FStrE;
            if (fe != null) return EvalFString(fe, f);
            var le = e as ListE;
            if (le != null) return PyList.Wrap(EvalItems(le.Items, f));
            var te = e as TupleE;
            if (te != null) return new PyTuple(EvalItems(te.Items, f).ToArray());
            var de = e as DictE;
            if (de != null)
            {
                var r = new PyDict();
                for (int i = 0; i < de.Keys.Count; i++)
                {
                    if (de.Keys[i] == null)
                    {
                        var m = Eval(de.Vals[i], f);
                        if (!(m is PyDict)) throw new PyError("TypeError", "После ** в словаре должен стоять словарь.", "'" + PyOps.TypeName(m) + "' object is not a mapping", e.Line);
                        foreach (var p in ((PyDict)m).Pairs()) r.Set(p.Key, p.Value);
                    }
                    else { var k = Eval(de.Keys[i], f); r.Set(k, Eval(de.Vals[i], f)); }
                }
                return r;
            }
            var comp = e as CompE;
            if (comp != null) return EvalComp(comp, f);
            var ife = e as IfExpE;
            if (ife != null) return PyOps.Truthy(Eval(ife.Cond, f)) ? Eval(ife.A, f) : Eval(ife.B, f);
            var se = e as SetE;
            if (se != null)
            {
                var items = EvalItems(se.Items, f);
                var s = new PySet();
                if (items.Count > 2 && se.Items.All(IsConstLike))
                {
                    // как в CPython: frozenset из литерала, затем компилятор пересобирает его (merge_consts) и SET_UPDATE
                    var tmp = new PySet(); foreach (var x in items) tmp.Add(x);
                    var tmp2 = new PySet(); foreach (var x in tmp.ToList()) tmp2.Add(x);
                    tmp2.Frozen = true; s.Merge(tmp2);
                }
                else foreach (var x in items) s.Add(x);
                return s;
            }
            var lam = e as LambdaE;
            if (lam != null) return MakeFunction("<lambda>", lam.Params, null, lam.Body, lam.Scope, f, e.Line);
            if (e is SliceE) throw new PyError("SyntaxError", "Срез [a:b] можно использовать только в квадратных скобках после списка или строки.", e.Line);
            if (e is StarredE) throw new PyError("SyntaxError", "Звёздочку *x можно использовать только при вызове функции, в списке/кортеже или при распаковке.", "can't use starred expression here", e.Line);
            throw new PyError("SyntaxError", "Неизвестное выражение.", e.Line);
        }

        // Константа после свёртки CPython (ast_opt): литерал, -литерал, кортеж констант, арифметика над константами.
        // Нужна, чтобы литерал множества из констант строился как в CPython (через frozenset) — от этого зависит порядок печати.
        static bool IsConstLike(Expr e)
        {
            if (e is ConstE) return !(((ConstE)e).V is PyEllipsis) || true;
            var u = e as UnaryE;
            if (u != null) return u.Op != "not" && IsConstLike(u.E) && !(u.E is TupleE);
            var t = e as TupleE;
            if (t != null) return t.Items.All(x => !(x is StarredE) && IsConstLike(x));
            var b = e as BinE;
            if (b != null) return IsConstLike(b.L) && IsConstLike(b.R) && !(b.L is TupleE) && !(b.R is TupleE);
            return false;
        }

        List<object> EvalItems(List<Expr> items, Frame f)
        {
            var res = new List<object>(items.Count);
            foreach (var x in items)
            {
                var st = x as StarredE;
                if (st != null) res.AddRange(ToList(Eval(st.E, f), x.Line));
                else res.Add(Eval(x, f));
            }
            return res;
        }

        PySlice EvalSlice(SliceE s, Frame f)
        {
            return new PySlice { Start = s.Lo == null ? null : Eval(s.Lo, f), Stop = s.Hi == null ? null : Eval(s.Hi, f), Step = s.Step == null ? null : Eval(s.Step, f) };
        }

        object EvalComp(CompE c, Frame f)
        {
            var first = Eval(c.Fors[0].Iter, f);
            if (!IsIterable(first))
                throw new PyError("TypeError", "В генераторе нужна коллекция, а тут " + PyOps.TypeNameRu(first) + "." + (PyOps.IsInt(first) ? " Чтобы повторить N раз, используй range(N)." : ""),
                    "'" + PyOps.TypeName(first) + "' object is not iterable", c.Line);
            var cf = new Frame { Name = "<" + c.Kind + "comp>", IsComp = true, Parent = f, Qual = f.QualPrefix + (c.Kind == "gen" ? "<genexpr>" : "<" + c.Kind + "comp>") };
            var items = CompItems(c, 0, cf, first);
            switch (c.Kind)
            {
                case "gen": return new PyIterator(items, "generator");
                case "list": { var l = new List<object>(); foreach (var x in items) l.Add(x); return PyList.Wrap(l); }
                case "set": { var s = new PySet(); foreach (var x in items) s.Add(x); return s; }
                default:
                    {
                        var d = new PyDict();
                        foreach (var x in items) { var kv = (KeyValuePair<object, object>)x; d.Set(kv.Key, kv.Value); }
                        return d;
                    }
            }
        }

        IEnumerable<object> CompItems(CompE c, int level, Frame cf, object src)
        {
            var fr = c.Fors[level];
            if (level > 0) src = Eval(fr.Iter, cf);
            foreach (var item in IterateRaw(src, c.Line))   // шаги считает CountStep ниже
            {
                CountStep(c.Line);
                Assign(fr.Target, item, cf);
                bool ok = true;
                for (int i = 0; i < fr.Ifs.Count; i++) if (!PyOps.Truthy(Eval(fr.Ifs[i], cf))) { ok = false; break; }
                if (!ok) continue;
                if (level + 1 < c.Fors.Count)
                {
                    foreach (var x in CompItems(c, level + 1, cf, null)) yield return x;
                }
                else if (c.Kind == "dict")
                {
                    var k = Eval(c.Elt, cf);
                    yield return new KeyValuePair<object, object>(k, Eval(c.Val, cf));
                }
                else yield return Eval(c.Elt, cf);
            }
        }

        string EvalFString(FStrE fe, Frame f)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fe.Parts.Count; i++)
            {
                var part = fe.Parts[i] as string;
                if (part != null) { sb.Append(part); continue; }
                var v = Eval((Expr)fe.Parts[i], f);
                char conv = fe.Convs[i];
                if (conv == 'r') v = PyOps.Repr(v);
                else if (conv == 's') v = PyOps.Str(v);
                else if (conv == 'a') v = Ascii(PyOps.Repr(v));
                string spec = fe.Specs[i] == null ? null : EvalFString(fe.Specs[i], f);
                sb.Append(FormatValue(v, spec, fe.Line));
            }
            return sb.ToString();
        }

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c < 128) sb.Append(c);
                else if (c <= 0xff) sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            return sb.ToString();
        }

        // ---------- Сравнения ----------
        bool CompareOp(string op, object a, object b, int line)
        {
            switch (op)
            {
                case "==": return PyOps.Eq(a, b);
                case "!=":
                    if (a is PyInstance && HasUser(((PyInstance)a).Cls, "__ne__")) return PyOps.Truthy(CallDunder((PyInstance)a, "__ne__", line, b));
                    return !PyOps.Eq(a, b);
                case "<": case ">": case "<=": case ">=": return PyOps.Truthy(RichCompare(a, b, op, line));
                case "is": return Is(a, b);
                case "is not": return !Is(a, b);
                case "in": return Contains(b, a, line);
                case "not in": return !Contains(b, a, line);
            }
            return false;
        }

        static bool Is(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is bool && b is bool) return (bool)a == (bool)b;
            if (a is long && b is long) { if (ReferenceEquals(a, b)) return true; long x = (long)a; return x == (long)b && x >= -5 && x <= 256; }
            if (a is string && b is string) return ReferenceEquals(a, b) || ((string)a == (string)b && ((string)a).Length <= 1);   // константы модуля — один объект (Parser.Canon)
            if (a is PyTuple && b is PyTuple && ((PyTuple)a).Items.Length == 0 && ((PyTuple)b).Items.Length == 0) return true;   // () — одиночка, как в CPython
            return ReferenceEquals(a, b);
        }

        public bool Less(object a, object b, int line) { return PyOps.Truthy(RichCompare(a, b, "<", line)); }

        static string Swap(string op) { return op == "<" ? ">" : op == ">" ? "<" : op == "<=" ? ">=" : "<="; }
        static string DunderOf(string op) { return op == "<" ? "__lt__" : op == ">" ? "__gt__" : op == "<=" ? "__le__" : "__ge__"; }
        static bool ApplyCmp(int c, string op) { return op == "<" ? c < 0 : op == ">" ? c > 0 : op == "<=" ? c <= 0 : c >= 0; }

        object RichCompare(object a, object b, string op, int line)
        {
            if (a is long && b is long) { long x = (long)a, y = (long)b; return op == "<" ? x < y : op == ">" ? x > y : op == "<=" ? x <= y : x >= y; }
            if (PyNum.IsNum(a) && PyNum.IsNum(b))
            {
                if ((a is double && double.IsNaN((double)a)) || (b is double && double.IsNaN((double)b))) return false;
                return ApplyCmp(PyNum.CompareNum(a, b), op);
            }
            if (a is string && b is string) return ApplyCmp(PyStr.Compare((string)a, (string)b), op);
            if ((a is PyList && b is PyList) || (a is PyTuple && b is PyTuple))
            {
                IList<object> x = a is PyList ? (IList<object>)((PyList)a).Items : ((PyTuple)a).Items;
                IList<object> y = b is PyList ? (IList<object>)((PyList)b).Items : ((PyTuple)b).Items;
                int n = Math.Min(x.Count, y.Count);
                for (int i = 0; i < n; i++)
                {
                    if (ReferenceEquals(x[i], y[i]) || PyOps.Eq(x[i], y[i])) continue;
                    PyDepth.Enter(" in comparison");
                    try { return RichCompare(x[i], y[i], op, line); }
                    finally { PyDepth.Leave(); }
                }
                return ApplyCmp(x.Count.CompareTo(y.Count), op);
            }
            if (a is PySet && b is PySet)
            {
                var x = (PySet)a; var y = (PySet)b;
                switch (op)
                {
                    case "<=": return IsSubset(x, y);
                    case "<": return x.Count < y.Count && IsSubset(x, y);
                    case ">=": return IsSubset(y, x);
                    default: return y.Count < x.Count && IsSubset(y, x);
                }
            }
            var ia = a as PyInstance;
            if (ia != null && HasUser(ia.Cls, DunderOf(op)))
            {
                var r = CallDunder(ia, DunderOf(op), line, b);
                if (!(r is PyNotImplemented)) return r;
            }
            var ib = b as PyInstance;
            if (ib != null && HasUser(ib.Cls, DunderOf(Swap(op))))
            {
                var r = CallDunder(ib, DunderOf(Swap(op)), line, a);
                if (!(r is PyNotImplemented)) return r;
            }
            string hint = "";
            if (a is string || b is string) hint = " Похоже, одно из значений — строка. Число из input() нужно превратить через int(...).";
            else if (a == null || b == null) hint = " Одно из значений — None: возможно, функция ничего не вернула (забыт return).";
            else if (ia != null || ib != null) hint = " Чтобы объекты класса можно было сравнивать (и сортировать), определи в классе метод __lt__.";
            throw new PyError("TypeError", "Нельзя сравнивать " + PyOps.TypeNameRu(a) + " и " + PyOps.TypeNameRu(b) + " через " + op + "." + hint,
                "'" + op + "' not supported between instances of '" + PyOps.TypeName(a) + "' and '" + PyOps.TypeName(b) + "'", line);
        }

        static bool IsSubset(PySet x, PySet y)
        {
            if (x.Count > y.Count) return false;
            foreach (var k in x.ToList()) if (!y.Contains(k)) return false;
            return true;
        }

        public bool Contains(object container, object item, int line)
        {
            var s = container as string;
            if (s != null)
            {
                if (!(item is string)) throw new PyError("TypeError", "Проверять «in» для строки можно только другой строкой, а тут " + PyOps.TypeNameRu(item) + ".",
                    "'in <string>' requires string as left operand, not " + PyOps.TypeName(item), line);
                return s.IndexOf((string)item, StringComparison.Ordinal) >= 0;
            }
            var l = container as PyList;
            if (l != null) { foreach (var x in l.Items) if (ReferenceEquals(x, item) || PyOps.Eq(x, item)) return true; return false; }
            var t = container as PyTuple;
            if (t != null) { foreach (var x in t.Items) if (ReferenceEquals(x, item) || PyOps.Eq(x, item)) return true; return false; }
            var d = container as PyDict;
            if (d != null) return d.ContainsKey(item);
            var st = container as PySet;
            if (st != null)
            {
                var ps = item as PySet;
                if (ps != null && !ps.Frozen) { var fz = ps.Copy(); fz.Frozen = true; return st.Contains(fz); }   // как set_contains в CPython
                return st.Contains(item);
            }
            var r = container as PyRange;
            if (r != null && (item is long || item is bool))
            {
                long v = PyOps.ToLong(item);
                if (r.Step > 0 ? (v < r.Start || v >= r.Stop) : (v > r.Start || v <= r.Stop)) return false;
                return (v - r.Start) % r.Step == 0;
            }
            var dv = container as PyDictView;
            if (dv != null)
            {
                if (dv.Kind == 0) return dv.D.ContainsKey(item);
                if (dv.Kind == 2)
                {
                    var tp = item as PyTuple; object v;
                    return tp != null && tp.Items.Length == 2 && dv.D.TryGet(tp.Items[0], out v) && PyOps.Eq(v, tp.Items[1]);
                }
            }
            var inst = container as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__contains__")) return PyOps.Truthy(CallDunder(inst, "__contains__", line, item));
            if (!IsIterable(container))
                throw new PyError("TypeError", "Оператор «in» ищет в коллекции, а справа от него " + PyOps.TypeNameRu(container) + ".",
                    "argument of type '" + PyOps.TypeName(container) + "' is not iterable", line);
            foreach (var x in Iterate(container, line)) if (ReferenceEquals(x, item) || PyOps.Eq(x, item)) return true;
            return false;
        }

        // ---------- Арифметика ----------
        object UnaryOp(string op, object v, int line)
        {
            if (op == "not") return !PyOps.Truthy(v);
            if (v is long)
            {
                long x = (long)v;
                if (op == "-") return x == long.MinValue ? PyNum.Norm(-(BigInteger)x) : (object)(-x);
                if (op == "+") return x;
                return ~x;
            }
            if (v is bool) v = ((bool)v) ? 1L : 0L;
            if (v is long) return UnaryOp(op, v, line);
            if (v is double) { if (op == "-") return -(double)v; if (op == "+") return v; }
            if (v is BigInteger)
            {
                var b = (BigInteger)v;
                return op == "-" ? PyNum.Norm(-b) : op == "+" ? v : PyNum.Norm(-b - 1);
            }
            var inst = v as PyInstance;
            if (inst != null)
            {
                string nm = op == "-" ? "__neg__" : op == "+" ? "__pos__" : "__invert__";
                if (HasUser(inst.Cls, nm)) return CallDunder(inst, nm, line);
            }
            if (v is PyDict && ((PyDict)v).Kind == DictKind.Counter && (op == "+" || op == "-"))
            {
                var r = new PyDict { Kind = DictKind.Counter };
                foreach (var p in ((PyDict)v).Pairs())
                {
                    var val = op == "-" ? BinOp("-", 0L, p.Value, line) : p.Value;
                    if (PyOps.Truthy(RichCompare(val, 0L, ">", line))) r.Set(p.Key, val);
                }
                return r;
            }
            throw new PyError("TypeError", "Знак " + op + " перед значением работает только с числами, а тут " + PyOps.TypeNameRu(v) + ".",
                "bad operand type for unary " + op + ": '" + PyOps.TypeName(v) + "'", line);
        }

        static PyError ZeroDiv(string pyMsg, string ru, int line) { return new PyError("ZeroDivisionError", ru, pyMsg, line); }

        public object BinOp(string op, object a, object b, int line)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b;
                switch (op)
                {
                    case "+": { long r = unchecked(x + y); if (((x ^ r) & (y ^ r)) < 0) return PyNum.Add(a, b); return r; }
                    case "-": { long r = unchecked(x - y); if (((x ^ y) & (x ^ r)) < 0) return PyNum.Sub(a, b); return r; }
                    case "*": return PyNum.Mul(a, b);
                    case "<": case ">": case "<=": case ">=": break;
                }
            }
            if (PyNum.IsNum(a) && PyNum.IsNum(b)) return NumOp(op, a, b, line);
            return ObjOp(op, a, b, line);
        }

        object NumOp(string op, object a, object b, int line)
        {
            bool ints = PyNum.IsInt(a) && PyNum.IsInt(b);
            if (ints)
            {
                if (a is bool && b is bool && (op == "&" || op == "|" || op == "^"))
                {
                    bool x = (bool)a, y = (bool)b;
                    return op == "&" ? x & y : op == "|" ? x | y : x ^ y;
                }
                if (a is bool) a = ((bool)a) ? 1L : 0L;
                if (b is bool) b = ((bool)b) ? 1L : 0L;
                switch (op)
                {
                    case "+": return PyNum.Add(a, b);
                    case "-": return PyNum.Sub(a, b);
                    case "*": return PyNum.Mul(a, b);
                    case "/":
                        {
                            if (IsZero(b)) throw ZeroDiv("division by zero", "Делить на ноль нельзя. Проверь, что делитель не равен 0 (например, список не пустой).", line);
                            if (a is long && b is long && Math.Abs((long)a) < (1L << 53) && Math.Abs((long)b) < (1L << 53)) return (double)(long)a / (long)b;
                            var x = PyNum.Big(a); var y = PyNum.Big(b);
                            bool neg = (x.Sign < 0) != (y.Sign < 0);
                            double r = PyNum.RatioToDouble(BigInteger.Abs(x), BigInteger.Abs(y));
                            if (double.IsInfinity(r)) throw new PyError("OverflowError", "Результат деления слишком большой для float.", "integer division result too large for a float", line);
                            return neg ? -r : r;
                        }
                    case "//":
                        if (IsZero(b)) throw ZeroDiv("integer division or modulo by zero", "Делить на ноль нельзя (даже целочисленно через //).", line);
                        return PyNum.FloorDiv(a, b);
                    case "%":
                        if (IsZero(b)) throw ZeroDiv("integer modulo by zero", "Остаток от деления на ноль не существует.", line);
                        return PyNum.Mod(a, b);
                    case "**":
                        {
                            var e = PyNum.Big(b);
                            if (e.Sign < 0)
                            {
                                if (IsZero(a)) throw ZeroDiv("0.0 cannot be raised to a negative power", "Ноль нельзя возводить в отрицательную степень.", line);
                                return FloatPow(PyNum.ToDouble(a), PyNum.ToDouble(b), line);
                            }
                            var bs = PyNum.Big(a);
                            if (bs.IsZero || bs.IsOne) return e.IsZero ? 1L : PyNum.Norm(bs);
                            if (bs == BigInteger.MinusOne) return e.IsEven ? 1L : -1L;
                            if (e > 1000000 || (double)e * PyNum.BitLength(bs) > 1000000)
                                throw new PyError("MemoryError", "Результат возведения в степень получается гигантским — игровой Python не может его посчитать.", line);
                            return PyNum.Norm(BigInteger.Pow(bs, (int)e));
                        }
                    case "&": return PyNum.Norm(PyNum.Big(a) & PyNum.Big(b));
                    case "|": return PyNum.Norm(PyNum.Big(a) | PyNum.Big(b));
                    case "^": return PyNum.Norm(PyNum.Big(a) ^ PyNum.Big(b));
                    case "<<":
                        {
                            var n = PyNum.Big(b);
                            if (n.Sign < 0) throw new PyError("ValueError", "Сдвиг на отрицательное число бит невозможен.", "negative shift count", line);
                            if (n > 100000) throw new PyError("MemoryError", "Слишком большой сдвиг.", line);
                            return PyNum.Norm(PyNum.Big(a) << (int)n);
                        }
                    case ">>":
                        {
                            var n = PyNum.Big(b);
                            if (n.Sign < 0) throw new PyError("ValueError", "Сдвиг на отрицательное число бит невозможен.", "negative shift count", line);
                            if (n > 100000) return PyNum.Big(a).Sign < 0 ? -1L : 0L;
                            return PyNum.Norm(PyNum.Big(a) >> (int)n);
                        }
                }
            }
            else
            {
                double x = PyNum.ToDouble(a), y = PyNum.ToDouble(b);
                switch (op)
                {
                    case "+": return x + y;
                    case "-": return x - y;
                    case "*": return x * y;
                    case "/":
                        if (y == 0) throw ZeroDiv("float division by zero", "Делить на ноль нельзя. Проверь, что делитель не равен 0 (например, список не пустой).", line);
                        return x / y;
                    case "//":
                        if (y == 0) throw ZeroDiv("float floor division by zero", "Делить на ноль нельзя (даже целочисленно через //).", line);
                        { double div, mod; FloatDivmod(x, y, out div, out mod); return div; }
                    case "%":
                        if (y == 0) throw ZeroDiv("float modulo", "Остаток от деления на ноль не существует.", line);
                        { double div, mod; FloatDivmod(x, y, out div, out mod); return mod; }
                    case "**": return FloatPow(x, y, line);
                }
            }
            throw new PyError("TypeError", "Операция «" + op + "» не работает для такой пары значений: " + PyOps.TypeNameRu(a) + " и " + PyOps.TypeNameRu(b) + ".",
                "unsupported operand type(s) for " + (op == "**" ? "** or pow()" : op) + ": '" + PyOps.TypeName(a) + "' and '" + PyOps.TypeName(b) + "'", line);
        }

        static bool IsZero(object o)
        {
            if (o is long) return (long)o == 0;
            if (o is BigInteger) return ((BigInteger)o).IsZero;
            if (o is bool) return !(bool)o;
            return (double)o == 0;
        }

        public static void FloatDivmod(double vx, double wx, out double floordiv, out double mod)
        {
            mod = vx % wx;
            double div = (vx - mod) / wx;
            if (mod != 0)
            {
                if ((wx < 0) != (mod < 0)) { mod += wx; div -= 1.0; }
            }
            else mod = wx < 0 ? -0.0 : 0.0;
            if (div != 0)
            {
                floordiv = Math.Floor(div);
                if (div - floordiv > 0.5) floordiv += 1.0;
            }
            else floordiv = BitConverter.DoubleToInt64Bits(vx / wx) < 0 ? -0.0 : 0.0;   // copysign(0.0, vx / wx)
        }

        object FloatPow(double x, double y, int line)
        {
            if (x == 0 && y < 0) throw ZeroDiv("0.0 cannot be raised to a negative power", "Ноль нельзя возводить в отрицательную степень.", line);
            if (x < 0 && y != Math.Floor(y) && !double.IsInfinity(y))
                throw new PyError("ValueError", "Отрицательное число в дробной степени даёт комплексное число — они не поддерживаются в игровом Python.", "complex numbers are not supported", line);
            double r = Math.Pow(x, y);
            if (double.IsInfinity(r) && !double.IsInfinity(x) && !double.IsInfinity(y))
                throw new PyError("OverflowError", "Результат возведения в степень слишком большой для float.", "(34, 'Numerical result out of range')", line);
            return r;
        }

        // Защита от «взрывного» роста (s += s, x = x + x в цикле): игра не должна зависнуть или упасть без памяти
        const int MaxItems = 10000000, MaxStrLen = 20000000;
        static void SizeGuard(long n, int line, bool str)
        {
            if (n > (str ? MaxStrLen : MaxItems))
                throw new PyError("MemoryError", str ? "Слишком длинная строка получается — похоже, она растёт в цикле без остановки." : "Слишком длинный список получается — похоже, он растёт в цикле без остановки.", line);
        }

        object ObjOp(string op, object a, object b, int line)
        {
            var sa = a as string;
            if (sa != null)
            {
                if (op == "+" && b is string) { SizeGuard((long)sa.Length + ((string)b).Length, line, true); return sa + (string)b; }
                if (op == "*" && PyNum.IsInt(b)) return Repeat(sa, PyOps.ToLong(b), line);
                if (op == "%") return PercentFormat(sa, b, line);
            }
            if (op == "*" && PyNum.IsInt(a) && b is string) return Repeat((string)b, PyOps.ToLong(a), line);
            var la = a as PyList;
            if (la != null)
            {
                if (op == "+" && b is PyList) { SizeGuard((long)la.Items.Count + ((PyList)b).Items.Count, line, false); var r = new List<object>(la.Items.Count + ((PyList)b).Items.Count); r.AddRange(la.Items); r.AddRange(((PyList)b).Items); return PyList.Wrap(r); }
                if (op == "*" && PyNum.IsInt(b)) return PyList.Wrap(RepeatList(la.Items, PyOps.ToLong(b), line));
            }
            if (op == "*" && PyNum.IsInt(a) && b is PyList) return PyList.Wrap(RepeatList(((PyList)b).Items, PyOps.ToLong(a), line));
            var ta = a as PyTuple;
            if (ta != null)
            {
                if (op == "+" && b is PyTuple) SizeGuard((long)ta.Items.Length + ((PyTuple)b).Items.Length, line, false);
                if (op == "+" && b is PyTuple) return new PyTuple(ta.Items.Concat(((PyTuple)b).Items).ToArray());
                if (op == "*" && PyNum.IsInt(b)) return new PyTuple(RepeatList(ta.Items, PyOps.ToLong(b), line).ToArray());
            }
            if (op == "*" && PyNum.IsInt(a) && b is PyTuple) return new PyTuple(RepeatList(((PyTuple)b).Items, PyOps.ToLong(a), line).ToArray());
            var xs = a as PySet;
            if (xs != null && b is PySet)
            {
                var ys = (PySet)b;
                PySet r = null;
                switch (op)
                {
                    case "|": r = xs.Copy(); r.Frozen = false; r.Merge(ys); break;
                    case "&": r = SetIntersection(xs, ys); break;
                    case "-": r = SetDifference(xs, ys); break;
                    case "^": r = ys.Copy(); r.Frozen = false; foreach (var k in xs.ToList()) { if (!r.Discard(k)) r.Add(k); } break;
                }
                if (r != null) { r.Frozen = xs.Frozen; return r; }
            }
            var da = a as PyDict;
            if (da != null && b is PyDict)
            {
                var db = (PyDict)b;
                if (da.Kind == DictKind.Counter && db.Kind == DictKind.Counter && op != "|")
                {
                    var r = CounterOp(op, da, db, line);
                    if (r != null) return r;
                }
                if (op == "|")
                {
                    if (da.Kind == DictKind.Counter && db.Kind == DictKind.Counter) return CounterOp("|", da, db, line);
                    var r = da.CopyAs(da.Kind); foreach (var p in db.Pairs()) r.Set(p.Key, p.Value); return r;
                }
            }
            var va = a as PyDictView;
            if (va != null && va.Kind != 1 && (op == "&" || op == "|" || op == "-" || op == "^"))
                return ObjOp(op, PyOps.ViewToSet(va), b is PyDictView ? PyOps.ViewToSet((PyDictView)b) : ToSet(b, line), line);
            // пользовательские классы
            string dn = OpDunder(op);
            if (dn != null)
            {
                var ia = a as PyInstance;
                if (ia != null && HasUser(ia.Cls, dn))
                {
                    var r = CallDunder(ia, dn, line, b);
                    if (!(r is PyNotImplemented)) return r;
                }
                var ib = b as PyInstance;
                if (ib != null && HasUser(ib.Cls, "__r" + dn.Substring(2)))
                {
                    var r = CallDunder(ib, "__r" + dn.Substring(2), line, a);
                    if (!(r is PyNotImplemented)) return r;
                }
            }
            // ошибки
            string ta_ = PyOps.TypeName(a), tb = PyOps.TypeName(b);
            if (op == "+")
            {
                if (a is string && PyNum.IsNum(b))
                    throw new PyError("TypeError", "Нельзя сложить строку и число. Преврати число в строку через str(...) или используй f-строку: f\"текст {число}\". А если строка пришла из input() и это число — преврати её через int(...).",
                        "can only concatenate str (not \"" + tb + "\") to str", line);
                if (a is string) throw new PyError("TypeError", "К строке можно прибавить только строку, а тут " + PyOps.TypeNameRu(b) + ". Преврати значение в строку через str(...).", "can only concatenate str (not \"" + tb + "\") to str", line);
                if (PyNum.IsNum(a) && b is string)
                    throw new PyError("TypeError", "Нельзя сложить число и строку. Если строка — это число из input(), преврати её через int(...).",
                        "unsupported operand type(s) for +: '" + ta_ + "' and 'str'", line);
                if (a is PyList) throw new PyError("TypeError", "К списку через + можно прибавить только список. Чтобы добавить один элемент, используй .append(x).", "can only concatenate list (not \"" + tb + "\") to list", line);
                if (a is PyTuple) throw new PyError("TypeError", "К кортежу можно прибавить только кортеж.", "can only concatenate tuple (not \"" + tb + "\") to tuple", line);
            }
            if (op == "*" && (a is string || b is string || a is PyList || b is PyList || a is PyTuple || b is PyTuple))
            {
                object other = (a is string || a is PyList || a is PyTuple) ? b : a;
                throw new PyError("TypeError", (a is string && b is string ? "Две строки нельзя перемножить. Если это числа из input(), сначала преврати их через int(...)." : "Строку или список можно умножить только на целое число, а тут " + PyOps.TypeNameRu(other) + "."),
                    "can't multiply sequence by non-int of type '" + PyOps.TypeName(other) + "'", line);
            }
            string hint = "";
            if ((a is string || b is string) && op != "+") hint = " Похоже, одно из значений — строка. Числа из input() нужно превращать через int(...).";
            else if (a == null || b == null) hint = " Одно из значений — None: возможно, функция ничего не вернула (забыт return) или переменная не заполнена.";
            else if (a is PyInstance) hint = " Чтобы объекты класса поддерживали «" + op + "», определи в классе метод " + dn + ".";
            throw new PyError("TypeError", "Операция «" + op + "» не работает для такой пары значений: " + PyOps.TypeNameRu(a) + " и " + PyOps.TypeNameRu(b) + "." + hint,
                "unsupported operand type(s) for " + (op == "**" ? "** or pow()" : op) + ": '" + ta_ + "' and '" + tb + "'", line);
        }

        static string OpDunder(string op)
        {
            switch (op)
            {
                case "+": return "__add__"; case "-": return "__sub__"; case "*": return "__mul__"; case "/": return "__truediv__";
                case "//": return "__floordiv__"; case "%": return "__mod__"; case "**": return "__pow__";
                case "&": return "__and__"; case "|": return "__or__"; case "^": return "__xor__"; case "<<": return "__lshift__"; case ">>": return "__rshift__";
            }
            return null;
        }

        PyDict CounterOp(string op, PyDict a, PyDict b, int line)
        {
            var r = new PyDict { Kind = DictKind.Counter };
            if (op == "+" || op == "-")
            {
                foreach (var p in a.Pairs())
                {
                    object bv; var v = b.TryGet(p.Key, out bv) ? BinOp(op, p.Value, bv, line) : p.Value;
                    if (PyOps.Truthy(RichCompare(v, 0L, ">", line))) r.Set(p.Key, v);
                }
                foreach (var p in b.Pairs())
                    if (!a.ContainsKey(p.Key))
                    {
                        var v = op == "+" ? p.Value : BinOp("-", 0L, p.Value, line);
                        if (PyOps.Truthy(RichCompare(v, 0L, ">", line))) r.Set(p.Key, v);
                    }
                return r;
            }
            if (op == "&")
            {
                foreach (var p in a.Pairs())
                {
                    object bv; if (!b.TryGet(p.Key, out bv)) continue;
                    var v = Less(p.Value, bv, line) ? p.Value : bv;
                    if (PyOps.Truthy(RichCompare(v, 0L, ">", line))) r.Set(p.Key, v);
                }
                return r;
            }
            if (op == "|")
            {
                foreach (var p in a.Pairs())
                {
                    object bv; var v = b.TryGet(p.Key, out bv) && Less(p.Value, bv, line) ? bv : p.Value;
                    if (PyOps.Truthy(RichCompare(v, 0L, ">", line))) r.Set(p.Key, v);
                }
                foreach (var p in b.Pairs())
                    if (!a.ContainsKey(p.Key) && PyOps.Truthy(RichCompare(p.Value, 0L, ">", line))) r.Set(p.Key, p.Value);
                return r;
            }
            return null;
        }

        PySet SetIntersection(PySet so, PySet other)
        {
            var result = new PySet();
            if (ReferenceEquals(so, other)) return so.Copy();
            if (other.Count > so.Count) { var t = so; so = other; other = t; }
            foreach (var k in other.ToList()) if (so.Contains(k)) result.Add(k);
            return result;
        }
        void DifferenceUpdate(PySet so, object other, int line)
        {
            if (ReferenceEquals(so, other)) { so.Clear(); return; }
            var os = other as PySet;
            if (os != null) foreach (var k in os.ToList()) so.Discard(k);
            else { RequireIterable(other, line); foreach (var x in Iterate(other, line).ToList()) so.Discard(x); }
            so.ResizeAfterDiscards();
        }

        PySet SetDifference(PySet so, PySet other)
        {
            if ((so.Count >> 2) > other.Count)
            {
                var r = so.Copy(); r.Frozen = false;
                foreach (var k in other.ToList()) r.Discard(k);
                r.ResizeAfterDiscards();
                return r;
            }
            var result = new PySet();
            foreach (var k in so.ToList()) if (!other.Contains(k)) result.Add(k);
            return result;
        }

        static string Repeat(string s, long n, int line)
        {
            if (n <= 0 || s.Length == 0) return "";
            if (n > 5000000 / s.Length) throw new PyError("MemoryError", "Слишком длинная строка получается.", line);
            var sb = new StringBuilder((int)(s.Length * n)); for (long i = 0; i < n; i++) sb.Append(s); return sb.ToString();
        }
        static List<object> RepeatList(IList<object> items, long n, int line)
        {
            var r = new List<object>();
            if (n <= 0 || items.Count == 0) return r;
            if (n > 5000000 / items.Count) throw new PyError("MemoryError", "Слишком длинный список получается.", line);
            for (long i = 0; i < n; i++) r.AddRange(items);
            return r;
        }

        // «f()» в сообщениях об ошибках вызова, как PyEval_GetFuncName + PyEval_GetFuncDesc
        static string CallDesc(object fn)
        {
            if (fn is PyFunction) return ((PyFunction)fn).Name + "()";
            if (fn is PyBuiltin) return ((PyBuiltin)fn).Name + "()";
            if (fn is PyBoundMethod) return ((PyBoundMethod)fn).Name + "()";
            if (fn is PyMethod) return CallDesc(((PyMethod)fn).Func);
            if (fn is PyClass) return ((PyClass)fn).Name + " object";
            return PyOps.TypeName(fn) + " object";
        }

        // ---------- Индексы и срезы ----------
        static long IndexValue(object o, int line, string what)
        {
            if (o is long) return (long)o;
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is BigInteger) return PyOps.ToLong(o);
            throw new PyError("TypeError", "Индекс " + what + " должен быть целым числом, а не " + PyOps.TypeNameRu(o) + "." +
                (o is string ? " Если это число из input(), преврати его через int(...)." : o is double ? " Используй целое число, например int(x) или //." : ""),
                what == "среза" ? "slice indices must be integers or None or have an __index__ method" :
                what == "строки" ? "string indices must be integers, not '" + PyOps.TypeName(o) + "'" : TypeKindEn(what) + " indices must be integers or slices, not " + PyOps.TypeName(o), line);
        }
        static string TypeKindEn(string what) { return what == "списка" ? "list" : what == "строки" ? "string" : what == "кортежа" ? "tuple" : "range"; }

        static int NormIndex(object idx, int count, int line, string what, string en)
        {
            long i = IndexValue(idx, line, what);
            if (i < 0) i += count;
            if (i < 0 || i >= count)
                throw new PyError("IndexError", "Индекс " + IndexValue(idx, line, what) + " выходит за границы " + what + " (элементов: " + count +
                    (count > 0 ? ", допустимые индексы 0.." + (count - 1) + " или -1..-" + count : ", он пустой") + "). Помни: счёт начинается с 0.",
                    en + " index out of range", line);
            return (int)i;
        }

        static void SliceIndices(PySlice s, long len, int line, out long start, out long stop, out long step, out long count)
        {
            step = s.Step == null ? 1 : IndexValue(s.Step, line, "среза");
            if (step == 0) throw new PyError("ValueError", "Шаг среза не может быть 0.", "slice step cannot be zero", line);
            if (s.Start == null) start = step < 0 ? len - 1 : 0;
            else
            {
                start = IndexValue(s.Start, line, "среза");
                if (start < 0) { start += len; if (start < 0) start = step < 0 ? -1 : 0; }
                else if (start >= len) start = step < 0 ? len - 1 : len;
            }
            if (s.Stop == null) stop = step < 0 ? -1 : len;
            else
            {
                stop = IndexValue(s.Stop, line, "среза");
                if (stop < 0) { stop += len; if (stop < 0) stop = step < 0 ? -1 : 0; }
                else if (stop >= len) stop = step < 0 ? len - 1 : len;
            }
            if (step < 0) count = stop < start ? (start - stop - 1) / (-step) + 1 : 0;
            else count = start < stop ? (stop - start - 1) / step + 1 : 0;
        }

        object GetSlice(object obj, PySlice sl, int line)
        {
            long start, stop, step, count;
            var s = obj as string;
            if (s != null && PyStr.HasSurr(s))
            {
                var cps = PyStr.Cps(s);
                SliceIndices(sl, cps.Length, line, out start, out stop, out step, out count);
                var sb2 = new StringBuilder();
                for (long i = 0, j = start; i < count; i++, j += step) sb2.Append(cps[(int)j]);
                return sb2.ToString();
            }
            if (s != null)
            {
                SliceIndices(sl, s.Length, line, out start, out stop, out step, out count);
                if (step == 1) return s.Substring((int)start, (int)count);
                var sb = new StringBuilder((int)count);
                for (long i = 0, j = start; i < count; i++, j += step) sb.Append(s[(int)j]);
                return sb.ToString();
            }
            IList<object> src = obj is PyList ? (IList<object>)((PyList)obj).Items : obj is PyTuple ? ((PyTuple)obj).Items : null;
            if (src != null)
            {
                SliceIndices(sl, src.Count, line, out start, out stop, out step, out count);
                var res = new List<object>((int)count);
                for (long i = 0, j = start; i < count; i++, j += step) res.Add(src[(int)j]);
                if (obj is PyTuple) return new PyTuple(res.ToArray());
                return PyList.Wrap(res);
            }
            var r = obj as PyRange;
            if (r != null)
            {
                SliceIndices(sl, r.Length, line, out start, out stop, out step, out count);
                long ns = r.At(start), nst = r.Step * step;
                return new PyRange { Start = ns, Stop = r.At(stop), Step = nst };   // как compute_slice в CPython
            }
            var inst = obj as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__getitem__")) return CallDunder(inst, "__getitem__", line, sl);
            throw new PyError("TypeError", "Срезы [a:b] работают со строками, списками и кортежами, а тут " + PyOps.TypeNameRu(obj) + ".",
                "'" + PyOps.TypeName(obj) + "' object is not subscriptable", line);
        }

        void SetSlice(object obj, PySlice sl, object v, int line)
        {
            var l = obj as PyList;
            if (l == null)
            {
                var inst = obj as PyInstance;
                if (inst != null && HasUser(inst.Cls, "__setitem__")) { CallDunder(inst, "__setitem__", line, sl, v); return; }
                throw new PyError("TypeError", PyOps.TypeNameRu(obj) + " нельзя менять через срез.", "'" + PyOps.TypeName(obj) + "' object does not support item assignment", line);
            }
            if (!IsIterable(v)) throw new PyError("TypeError", "Срезу списка можно присвоить только коллекцию.", "can only assign an iterable", line);
            var items = ToList(v, line);
            long start, stop, step, count;
            SliceIndices(sl, l.Items.Count, line, out start, out stop, out step, out count);
            if (step == 1)
            {
                if (stop < start) stop = start;
                l.Items.RemoveRange((int)start, (int)(stop - start));
                l.Items.InsertRange((int)start, items);
                return;
            }
            if (items.Count != count)
                throw new PyError("ValueError", "Срезу с шагом из " + count + " элементов пытаются присвоить " + items.Count + " значений — количество должно совпадать.",
                    "attempt to assign sequence of size " + items.Count + " to extended slice of size " + count, line);
            for (long i = 0, j = start; i < count; i++, j += step) l.Items[(int)j] = items[(int)i];
        }

        void DelSlice(object obj, PySlice sl, int line)
        {
            var l = obj as PyList;
            if (l == null) throw new PyError("TypeError", "Удалять срезом можно только из списка.", "'" + PyOps.TypeName(obj) + "' object does not support item deletion", line);
            long start, stop, step, count;
            SliceIndices(sl, l.Items.Count, line, out start, out stop, out step, out count);
            if (count == 0) return;
            var idx = new List<long>();
            for (long i = 0, j = start; i < count; i++, j += step) idx.Add(j);
            idx.Sort();
            for (int i = idx.Count - 1; i >= 0; i--) l.Items.RemoveAt((int)idx[i]);
        }

        public object GetItem(object obj, object idx, int line)
        {
            var l = obj as PyList;
            if (l != null) { if (idx is PySlice) return GetSlice(obj, (PySlice)idx, line); return l.Items[NormIndex(idx, l.Items.Count, line, "списка", "list")]; }
            var d = obj as PyDict;
            if (d != null)
            {
                object v;
                if (d.TryGet(idx, out v)) return v;
                if (d.Kind == DictKind.Counter) return 0L;
                if (d.Kind == DictKind.DefaultDict && d.Factory != null)
                {
                    v = Call(d.Factory, new List<object>(), NoKw, line);
                    d.Set(idx, v);
                    return v;
                }
                throw KeyErr(idx, line);
            }
            var s = obj as string;
            if (s != null) { if (idx is PySlice) return GetSlice(obj, (PySlice)idx, line); return PyStr.At(s, NormIndex(idx, PyStr.Len(s), line, "строки", "string")); }
            var t = obj as PyTuple;
            if (t != null) { if (idx is PySlice) return GetSlice(obj, (PySlice)idx, line); return t.Items[NormIndex(idx, t.Items.Length, line, "кортежа", "tuple")]; }
            var r = obj as PyRange;
            if (r != null)
            {
                if (idx is PySlice) return GetSlice(obj, (PySlice)idx, line);
                long n = r.Length; if (n > int.MaxValue) n = int.MaxValue;
                return r.At(NormIndex(idx, (int)n, line, "range", "range object"));
            }
            var inst = obj as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__getitem__")) return CallDunder(inst, "__getitem__", line, idx);
            if (obj is PyClass) return obj;   // list[int], Optional[str] и т. п. в аннотациях
            string hint = obj == null ? " Похоже, переменная равна None — возможно, функция ничего не вернула." :
                          obj is PySet ? " У множества нет порядка и индексов. Преврати его в список: list(s) или sorted(s)." :
                          obj is PyIterator ? " Это итератор (результат map/filter/zip/генератора) — преврати его в список: list(...)." :
                          (obj is PyFunction || obj is PyBuiltin || obj is PyMethod || obj is PyBoundMethod) ? " Это функция: чтобы её вызвать, нужны круглые скобки (), а не квадратные." : "";
            throw new PyError("TypeError", PyOps.TypeNameRu(obj) + " не поддерживает доступ по индексу [ ]." + hint,
                "'" + PyOps.TypeName(obj) + "' object is not subscriptable", line);
        }

        PyError KeyErr(object key, int line)
        {
            return new PyError("KeyError", "Ключа " + PyOps.Repr(key) + " нет в словаре. Проверь его через «in» или используй .get(ключ, значение_по_умолчанию).", line)
            { ExcArgs = new[] { key }, PyMsg = PyOps.Repr(key) };
        }

        public void SetItem(object obj, object idx, object v, int line)
        {
            var l = obj as PyList;
            if (l != null)
            {
                if (idx is PySlice) { SetSlice(obj, (PySlice)idx, v, line); return; }
                l.Items[NormIndex(idx, l.Items.Count, line, "списка", "list assignment")] = v; return;
            }
            var d = obj as PyDict;
            if (d != null) { d.Set(idx, v); return; }
            var inst = obj as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__setitem__")) { CallDunder(inst, "__setitem__", line, idx, v); return; }
            if (obj is string) throw new PyError("TypeError", "Строки нельзя менять по одному символу. Создай новую строку, например через replace() или сложение.", "'str' object does not support item assignment", line);
            if (obj is PyTuple) throw new PyError("TypeError", "Кортеж (tuple) нельзя изменять после создания.", "'tuple' object does not support item assignment", line);
            throw new PyError("TypeError", "Нельзя присваивать по индексу для " + PyOps.TypeNameRuGen(obj) + ".", "'" + PyOps.TypeName(obj) + "' object does not support item assignment", line);
        }

        void DelItem(object obj, object idx, int line)
        {
            var l = obj as PyList;
            if (l != null)
            {
                if (idx is PySlice) { DelSlice(obj, (PySlice)idx, line); return; }
                l.Items.RemoveAt(NormIndex(idx, l.Items.Count, line, "списка", "list assignment")); return;
            }
            var d = obj as PyDict;
            if (d != null) { if (!d.Remove(idx)) throw KeyErr(idx, line); return; }
            var inst = obj as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__delitem__")) { CallDunder(inst, "__delitem__", line, idx); return; }
            throw new PyError("TypeError", "Из " + PyOps.TypeNameRuGen(obj) + " нельзя удалять элементы через del.", "'" + PyOps.TypeName(obj) + "' object doesn't support item deletion", line);
        }

        // ---------- Итерация ----------
        public bool IsIterable(object o)
        {
            if (o is PyList || o is PyTuple || o is string || o is PyDict || o is PySet || o is PyRange || o is PyIterator || o is PyDictView) return true;
            var inst = o as PyInstance;
            return inst != null && (HasUser(inst.Cls, "__iter__") || HasUser(inst.Cls, "__getitem__"));
        }

        public List<object> ToList(object o, int line)
        {
            if (o is PyList) return new List<object>(((PyList)o).Items);
            if (o is PyTuple) return new List<object>(((PyTuple)o).Items);
            var r = new List<object>();
            foreach (var x in Iterate(o, line))
            {
                r.Add(x);
                if (r.Count > 10000000) throw new PyError("MemoryError", "Слишком большая коллекция получается.", line);
            }
            return r;
        }

        PySet ToSet(object o, int line)
        {
            var s = o as PySet;
            if (s != null) { var c = s.Copy(); c.Frozen = false; return c; }
            var r = new PySet();
            if (o is PyDict) { r.UpdateFromDict((PyDict)o); return r; }
            foreach (var x in Iterate(o, line)) r.Add(x);
            return r;
        }

        // Обход для встроенных функций (sum, max, sorted, list(...), join...): элементы «ленивых» источников
        // (range, map/zip/enumerate/генераторы) считаются шагами — иначе sum(range(10**9)) заморозил бы игру мимо лимита шагов.
        public IEnumerable<object> Iterate(object o, int line)
        {
            if (o is PyRange || o is PyIterator) return CountedIter(IterateRaw(o, line), line);
            return IterateRaw(o, line);
        }
        int lazyItems;
        IEnumerable<object> CountedIter(IEnumerable<object> src, int line)
        {
            // элемент встроенного обхода дешевле инструкции: считаем шагом каждый 4-й (sum(range(10**6)) укладывается в лимит)
            foreach (var x in src) { if ((++lazyItems & 3) == 0) CountStep(line); yield return x; }
        }

        // Обход без подсчёта шагов — для for и генераторов, которые считают шаги сами
        public IEnumerable<object> IterateRaw(object o, int line)
        {
            var l = o as PyList;
            if (l != null) return IterList(l.Items);
            var t = o as PyTuple;
            if (t != null) return t.Items;
            var s = o as string;
            if (s != null) return IterStr(s);
            var r = o as PyRange;
            if (r != null) return IterRange(r);
            var d = o as PyDict;
            if (d != null) return IterDict(d, 0, line);
            var st = o as PySet;
            if (st != null) return IterSet(st, line);
            var it = o as PyIterator;
            if (it != null) return IterIterator(it);
            var dv = o as PyDictView;
            if (dv != null) return IterDict(dv.D, dv.Kind, line);
            var inst = o as PyInstance;
            if (inst != null)
            {
                if (HasUser(inst.Cls, "__iter__")) return IterInstance(inst, line);
                if (HasUser(inst.Cls, "__getitem__")) return IterGetItem(inst, line);
            }
            throw new PyError("TypeError", "Пройтись циклом можно только по коллекции, а тут " + PyOps.TypeNameRu(o) + ". " +
                (PyOps.IsInt(o) ? "Чтобы повторить что-то N раз, используй range(N)." : o is PyInstance ? "Чтобы по объекту можно было пройтись циклом, определи в классе метод __iter__." : o == null ? "Похоже, переменная равна None — возможно, функция ничего не вернула." : ""),
                "'" + PyOps.TypeName(o) + "' object is not iterable", line);
        }

        static IEnumerable<object> IterList(List<object> l) { for (int i = 0; i < l.Count; i++) yield return l[i]; }
        static IEnumerable<object> IterStr(string s)
        {
            if (PyStr.HasSurr(s)) { foreach (var c in PyStr.Cps(s)) yield return c; yield break; }
            for (int i = 0; i < s.Length; i++) yield return s[i].ToString();
        }
        static IEnumerable<object> IterRange(PyRange r)
        {
            long n = r.Length, v = r.Start;
            for (long i = 0; i < n; i++, v += r.Step) yield return v;
        }
        static IEnumerable<object> IterIterator(PyIterator it) { while (it.E.MoveNext()) yield return it.E.Current; }
        IEnumerable<object> IterDict(PyDict d, int kind, int line)
        {
            int ver = d.Version, cnt = d.Count;
            for (int i = 0; i < d.RawCount; i++)
            {
                object k, v;
                if (!d.RawAt(i, out k, out v)) continue;
                yield return kind == 0 ? k : kind == 1 ? v : new PyTuple(new[] { k, v });
                if (d.Version != ver)
                    throw new PyError("RuntimeError", "Словарь изменился во время цикла по нему (добавили или удалили ключ). Пройдись по копии: for k in list(d): ...",
                        d.Count != cnt ? "dictionary changed size during iteration" : "dictionary keys changed during iteration", line);
            }
        }
        IEnumerable<object> IterSet(PySet s, int line)
        {
            int ver = s.Version;
            for (int i = 0; i < s.RawCount; i++)
            {
                object k;
                if (!s.RawAt(i, out k)) continue;
                yield return k;
                if (s.Version != ver)
                    throw new PyError("RuntimeError", "Множество изменилось во время цикла по нему. Пройдись по копии: for x in list(s): ...", "Set changed size during iteration", line);
            }
        }
        IEnumerable<object> IterInstance(PyInstance inst, int line)
        {
            var itr = CallDunder(inst, "__iter__", line);
            var ii = itr as PyInstance;
            if (ii != null && HasUser(ii.Cls, "__next__"))
            {
                while (true)
                {
                    object v;
                    try { v = CallDunder(ii, "__next__", line); }
                    catch (PyError e) { if (e.PyType == "StopIteration" || (e.Value is PyInstance && ((PyInstance)e.Value).Cls.IsSubOf(ExcClass("StopIteration")))) yield break; throw; }
                    yield return v;
                }
            }
            if (!IsIterable(itr) || itr is PyInstance)
                throw new PyError("TypeError", "Метод __iter__ должен вернуть итерируемый объект (например iter(self.items)), а вернул " + PyOps.TypeNameRu(itr) + ".",
                    "iter() returned non-iterator of type '" + PyOps.TypeName(itr) + "'", line);
            foreach (var x in Iterate(itr, line)) yield return x;
        }
        IEnumerable<object> IterGetItem(PyInstance inst, int line)
        {
            for (long i = 0; ; i++)
            {
                object v;
                try { v = CallDunder(inst, "__getitem__", line, i); }
                catch (PyError e) { if (e.PyType == "IndexError" || e.PyType == "StopIteration") yield break; throw; }
                yield return v;
            }
        }

        // ---------- Вызовы ----------
        object EvalCall(CallE c, Frame f)
        {
            var fn = Eval(c.F, f);
            List<object> args;
            if (c.Args.Count == 0) args = new List<object>();
            else
            {
                args = new List<object>(c.Args.Count);
                foreach (var a in c.Args)
                {
                    var st = a as StarredE;
                    if (st != null)
                    {
                        var v = Eval(st.E, f);
                        if (!IsIterable(v)) throw new PyError("TypeError", "После * в вызове должна стоять коллекция (список, кортеж).", CallDesc(fn) + " argument after * must be an iterable, not " + PyOps.TypeName(v), c.Line);
                        args.AddRange(ToList(v, c.Line));
                    }
                    else args.Add(Eval(a, f));
                }
            }
            Dictionary<string, object> kw = NoKw;
            if (c.Kw.Count > 0)
            {
                kw = new Dictionary<string, object>();
                foreach (var p in c.Kw)
                {
                    if (p.Key != null) { kw[p.Key] = Eval(p.Value, f); continue; }
                    var mv = Eval(p.Value, f);
                    var m = mv as PyDict;
                    if (m == null) throw new PyError("TypeError", "После ** в вызове должен стоять словарь.", CallDesc(fn) + " argument after ** must be a mapping, not " + PyOps.TypeName(mv), c.Line);
                    foreach (var kv in m.Pairs())
                    {
                        var ks = kv.Key as string;
                        if (ks == null) throw new PyError("TypeError", "Ключи словаря после ** должны быть строками.", "keywords must be strings", c.Line);
                        if (kw.ContainsKey(ks)) throw new PyError("TypeError", "Аргумент «" + ks + "» передан дважды.", "got multiple values for keyword argument '" + ks + "'", c.Line);
                        kw[ks] = kv.Value;
                    }
                }
            }
            if (args.Count == 0 && fn == SuperBuiltin) return MakeSuper(f, c.Line);
            return Call(fn, args, kw, c.Line);
        }

        public object Call(object f, List<object> args, Dictionary<string, object> kw, int line)
        {
            if (kw == null) kw = NoKw;
            var fn = f as PyFunction;
            if (fn != null) return CallFunction(fn, args, kw, line);
            var b = f as PyBuiltin;
            if (b != null)
            {
                // сбой C#-кода встроенной функции на неожиданном аргументе → TypeError, который можно поймать в try/except
                methodName = null;
                try { return b.Fn(args, kw, line); }
                catch (InvalidCastException e) { throw HostError(e, b.Name, line); }
                catch (NullReferenceException e) { throw HostError(e, b.Name, line); }
                catch (ArgumentException e) { throw HostError(e, b.Name, line); }
                catch (IndexOutOfRangeException e) { throw HostError(e, b.Name, line); }
                catch (FormatException e) { throw HostError(e, b.Name, line); }
                catch (OverflowException e) { throw HostError(e, b.Name, line); }
            }
            var m = f as PyMethod;
            if (m != null)
            {
                var a2 = new List<object>(args.Count + 1) { m.Self };
                a2.AddRange(args);
                return Call(m.Func, a2, kw, line);
            }
            var bm = f as PyBoundMethod;
            if (bm != null)
            {
                try { return CallMethod(bm.Self, bm.Name, args, kw, line); }
                catch (InvalidCastException e) { throw HostError(e, bm.Name, line); }
                catch (NullReferenceException e) { throw HostError(e, bm.Name, line); }
                catch (ArgumentException e) { throw HostError(e, bm.Name, line); }
                catch (IndexOutOfRangeException e) { throw HostError(e, bm.Name, line); }
                catch (FormatException e) { throw HostError(e, bm.Name, line); }
                catch (OverflowException e) { throw HostError(e, bm.Name, line); }
            }
            var cls = f as PyClass;
            if (cls != null) return Instantiate(cls, args, kw, line);
            var inst = f as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__call__")) return Call(Bind(inst.Cls.Find("__call__"), inst, inst.Cls), args, kw, line);
            if (f is PyStaticMethod) return Call(((PyStaticMethod)f).Func, args, kw, line);
            throw new PyError("TypeError", PyOps.TypeNameRu(f) + " нельзя вызвать как функцию (скобки () после значения). " +
                (f is string ? "Может, между строкой и скобкой не хватает запятой или +?" : f == null ? "Похоже, переменная равна None." : f is PyList || f is PyDict ? "Для доступа к элементу используй квадратные скобки: x[0]." : ""),
                "'" + PyOps.TypeName(f) + "' object is not callable", line);
        }

        static PyError HostError(Exception e, string fname, int line)
        {
            if (e is OverflowException)
                return new PyError("OverflowError", "Число слишком большое для " + fname + "().", "Python int too large to convert to C ssize_t", line);
            return new PyError("TypeError", "Функция " + fname + "() не может работать с такими аргументами — проверь их типы.", "bad argument type for " + fname + "()", line);
        }

        static string QualName(PyFunction fn) { return fn.Qual ?? (fn.DefiningClass != null && fn.Name != "<lambda>" ? fn.DefiningClass.Name + "." + fn.Name : fn.Name); }

        object CallFunction(PyFunction fn, List<object> args, Dictionary<string, object> kw, int line)
        {
            if (Stack.Count >= MaxDepth)
                throw new PyError("RecursionError", "Слишком глубокая рекурсия: функция " + fn.Name + "() вызывает сама себя слишком много раз (больше " + MaxDepth + "). Нужен базовый случай с return, который остановит рекурсию.",
                    "maximum recursion depth exceeded", line);
            var frame = new Frame { Name = fn.Name, Parent = fn.Closure, Info = fn.Scope, Func = fn, Qual = fn.Qual ?? fn.Name };
            BindArgs(fn, frame, args, kw, line);
            Stack.Add(frame);
            try
            {
                if (fn.LambdaBody != null) { CountStep(line); return Eval(fn.LambdaBody, frame); }   // у lambda нет инструкций — считаем сам вызов
                retVal = null;
                var flow = ExecBlock(fn.Body, frame);
                if (flow == Flow.Return) { var r = retVal; retVal = null; return r; }
                if (flow != Flow.Normal) throw new PyError("SyntaxError", "break/continue можно использовать только внутри цикла.", "'break' outside loop", line);
                return null;
            }
            finally { Stack.RemoveAt(Stack.Count - 1); CurrentLine = line; }
        }

        void BindArgs(PyFunction fn, Frame frame, List<object> args, Dictionary<string, object> kw, int line)
        {
            var ps = fn.Params; int np = ps.Count;
            string qn = QualName(fn);
            var vals = new object[np]; var set = new bool[np];
            int varargs = -1, varkw = -1, npos = 0, ai = 0;
            for (int i = 0; i < np; i++)
            {
                if (ps[i].Kind == 1) varargs = i;
                else if (ps[i].Kind == 3) varkw = i;
                else if (ps[i].Kind == 0) npos++;
            }
            for (int i = 0; i < np && ai < args.Count; i++)
            {
                if (ps[i].Kind != 0) break;
                vals[i] = args[ai++]; set[i] = true;
            }
            if (ai < args.Count)
            {
                if (varargs < 0)
                {
                    int ndef = 0; for (int i = 0; i < np; i++) if (ps[i].Kind == 0 && fn.Defaults[i] != PyFunction.NoDefault) ndef++;
                    string takes = ndef > 0 ? "from " + (npos - ndef) + " to " + npos : npos.ToString();
                    bool isMethod = fn.DefiningClass != null && npos > 0;
                    throw new PyError("TypeError", "Функция " + fn.Name + "() принимает " + (isMethod ? (npos - 1) + " аргумент(а) (не считая self)" : npos + " аргумент(а)") +
                        ", а передали " + (isMethod ? args.Count - 1 : args.Count) + ".",
                        qn + "() takes " + takes + " positional argument" + (npos == 1 && ndef == 0 ? "" : "s") + " but " + args.Count + (args.Count == 1 ? " was" : " were") + " given", line);
                }
                vals[varargs] = new PyTuple(args.GetRange(ai, args.Count - ai).ToArray()); set[varargs] = true;
            }
            else if (varargs >= 0) { vals[varargs] = PyTuple.Empty; set[varargs] = true; }
            PyDict extra = null;
            if (varkw >= 0) { extra = new PyDict(); vals[varkw] = extra; set[varkw] = true; }
            if (kw.Count > 0)
                foreach (var kv in kw)
                {
                    int idx = -1;
                    for (int i = 0; i < np; i++) if ((ps[i].Kind == 0 || ps[i].Kind == 2) && ps[i].Name == kv.Key) { idx = i; break; }
                    if (idx >= 0 && ps[idx].PosOnly)
                    {
                        if (extra != null) { extra.Set(kv.Key, kv.Value); continue; }
                        throw new PyError("TypeError", "Параметр «" + kv.Key + "» функции " + fn.Name + "() можно передать только по позиции (он стоит до «/»).",
                            qn + "() got some positional-only arguments passed as keyword arguments: '" + kv.Key + "'", line);
                    }
                    if (idx >= 0)
                    {
                        if (set[idx]) throw new PyError("TypeError", "Аргумент «" + kv.Key + "» функции " + fn.Name + "() передан дважды: и по позиции, и по имени.", qn + "() got multiple values for argument '" + kv.Key + "'", line);
                        vals[idx] = kv.Value; set[idx] = true;
                    }
                    else if (extra != null) extra.Set(kv.Key, kv.Value);
                    else throw new PyError("TypeError", "У функции " + fn.Name + "() нет параметра «" + kv.Key + "». Её параметры: " + string.Join(", ", ps.Select(x => x.Name).ToArray()) + ".",
                        qn + "() got an unexpected keyword argument '" + kv.Key + "'", line);
                }
            List<string> missing = null, missingKw = null;
            for (int i = 0; i < np; i++)
            {
                if (set[i]) continue;
                if (fn.Defaults[i] != PyFunction.NoDefault) { vals[i] = fn.Defaults[i]; continue; }
                if (ps[i].Kind == 0) (missing = missing ?? new List<string>()).Add(ps[i].Name);
                else (missingKw = missingKw ?? new List<string>()).Add(ps[i].Name);
            }
            if (missing != null || missingKw != null)
            {
                var ms = missing ?? missingKw;
                string kind = missing != null ? "positional" : "keyword-only";
                string list = ms.Count == 1 ? "'" + ms[0] + "'" : ms.Count == 2 ? "'" + ms[0] + "' and '" + ms[1] + "'" :
                    string.Join(", ", ms.Take(ms.Count - 1).Select(x => "'" + x + "'").ToArray()) + ", and '" + ms[ms.Count - 1] + "'";
                var visible = ps.Where(x => x.Kind == 0 || x.Kind == 2).Select(x => x.Name).Where(x => !(fn.DefiningClass != null && x == "self")).ToArray();
                throw new PyError("TypeError", "Функции " + fn.Name + "() не передали " + (ms.Count == 1 ? "аргумент «" + ms[0] + "»" : "аргументы «" + string.Join("», «", ms.ToArray()) + "»") +
                    ". Она ждёт: " + string.Join(", ", visible) + ".",
                    qn + "() missing " + ms.Count + " required " + kind + " argument" + (ms.Count == 1 ? "" : "s") + ": " + list, line);
            }
            for (int i = 0; i < np; i++) frame.Set(ps[i].Name, vals[i]);
        }

        void Need(List<object> a, int min, int max, string name, int line)
        {
            if (a.Count < min || a.Count > max)
            {
                // формулировки как в CPython 3.11: METH_O / METH_NOARGS («list.append() takes exactly one argument»), Argument Clinic («insert expected 2 arguments, got 1»)
                string qual = methodName == name && methodOwner != null ? methodOwner + "." + name : name;
                string pm = null;
                if (a.Count < min)
                    switch (name)
                    {
                        case "enumerate": pm = "enumerate() missing required argument 'iterable'"; break;
                        case "round": pm = "round() missing required argument 'number' (pos 1)"; break;
                        case "pow": pm = a.Count == 0 ? "pow() missing required argument 'base' (pos 1)" : "pow() missing required argument 'exp' (pos 2)"; break;
                        case "sum": pm = "sum() takes at least 1 positional argument (0 given)"; break;
                        case "find": case "rfind": case "index": case "rindex": case "count": case "startswith": case "endswith":
                            if (methodOwner == "str" && methodName == name) pm = name + "() takes at least 1 argument (0 given)"; break;
                    }
                else if (name == "str" && a.Count > max) pm = "str() takes at most 3 arguments (" + a.Count + " given)";
                if (pm != null) { }
                else if (min == max && min == 0) pm = qual + "() takes no arguments (" + a.Count + " given)";
                else if (min == max && min == 1) pm = qual + "() takes exactly one argument (" + a.Count + " given)";
                else if (min == max) pm = name + " expected " + min + " arguments, got " + a.Count;
                else if (a.Count < min) pm = name + " expected at least " + min + " argument" + (min == 1 ? "" : "s") + ", got " + a.Count;
                else pm = name + " expected at most " + max + " argument" + (max == 1 ? "" : "s") + ", got " + a.Count;
                throw new PyError("TypeError", name + "() ожидает " + (min == max ? min.ToString() : max == int.MaxValue ? "не меньше " + min : min + "–" + max) + " аргумент(а), а получила " + a.Count + ".", pm, line);
            }
        }
        static void NoKwArgs(Dictionary<string, object> kw, string name, int line)
        {
            if (kw.Count > 0) throw new PyError("TypeError", name + "() не принимает именованные аргументы (" + kw.Keys.First() + "=...).", name + "() takes no keyword arguments", line);
        }
        static object Kw(Dictionary<string, object> kw, string name, object def)
        {
            object v; return kw.TryGetValue(name, out v) ? v : def;
        }
        static void OnlyKw(Dictionary<string, object> kw, string fname, int line, params string[] allowed)
        {
            foreach (var k in kw.Keys)
                if (Array.IndexOf(allowed, k) < 0)
                    throw new PyError("TypeError", "У " + fname + "() нет параметра «" + k + "».", "'" + k + "' is an invalid keyword argument for " + fname + "()", line);
        }

        // ---------- Классы и объекты ----------
        public object Instantiate(PyClass cls, List<object> args, Dictionary<string, object> kw, int line)
        {
            if (cls.Ctor != null) return cls.Ctor(args, kw, line);
            object obj;
            var nw = cls.Find("__new__");
            if (nw != null && nw != ObjectNew)
            {
                var a2 = new List<object> { cls }; a2.AddRange(args);
                obj = Call(Unwrap(nw), a2, kw, line);
                var oi = obj as PyInstance;
                if (oi == null || !oi.Cls.IsSubOf(cls)) return obj;
            }
            else
            {
                var inst0 = new PyInstance(cls);
                if (cls.IsException) inst0.Attrs["args"] = new PyTuple(args.ToArray());
                obj = inst0;
            }
            var inst = (PyInstance)obj;
            var init = cls.Find("__init__");
            if (init == ObjectInit)
            {
                if ((args.Count > 0 || kw.Count > 0) && (nw == null || nw == ObjectNew))
                    throw new PyError("TypeError", "У класса " + cls.Name + " нет метода __init__, поэтому при создании объекта нельзя передавать аргументы. Добавь в класс def __init__(self, ...).",
                        cls.Name + "() takes no arguments", line);
            }
            else if (init != null)
            {
                var r = Call(Bind(init, inst, cls), args, kw, line);
                if (r != null) throw new PyError("TypeError", "__init__ не должен ничего возвращать (return значение) — он только настраивает объект.", "__init__() should return None, not '" + PyOps.TypeName(r) + "'", line);
            }
            return inst;
        }

        static object Unwrap(object v)
        {
            if (v is PyStaticMethod) return ((PyStaticMethod)v).Func;
            if (v is PyClassMethod) return ((PyClassMethod)v).Func;
            return v;
        }

        object Bind(object v, object self, PyClass cls)
        {
            if (v is PyFunction || v is PyBuiltin) return new PyMethod { Self = self, Func = v };
            if (v is PyStaticMethod) return ((PyStaticMethod)v).Func;
            if (v is PyClassMethod) return new PyMethod { Self = cls, Func = ((PyClassMethod)v).Func };
            return v;
        }

        // метод определён пользователем (не встроенная заглушка object/BaseException)
        public bool HasUser(PyClass cls, string name)
        {
            foreach (var c in cls.Mro)
            {
                object v;
                if (c.Dict.TryGetValue(name, out v)) return !c.Builtin && v != null;
            }
            return false;
        }

        object CallDunder(PyInstance inst, string name, int line, params object[] args)
        {
            var m = inst.Cls.Find(name);
            return Call(Bind(m, inst, inst.Cls), new List<object>(args), NoKw, line);
        }

        public object GetAttr(object obj, string name, int line)
        {
            var inst = obj as PyInstance;
            if (inst != null)
            {
                object v;
                PyClass owner = null; object clsAttr = null;
                foreach (var c in inst.Cls.Mro) if (c.Dict.TryGetValue(name, out clsAttr)) { owner = c; break; }
                var prop = clsAttr as PyProperty;
                if (prop != null)
                {
                    if (prop.Fget == null) throw new PyError("AttributeError", "У свойства «" + name + "» нет геттера.", "unreadable attribute", line);
                    return Call(prop.Fget, new List<object> { inst }, NoKw, line);
                }
                if (inst.Attrs.TryGetValue(name, out v)) return v;
                if (owner != null) return Bind(clsAttr, inst, inst.Cls);
                if (name == "__class__") return inst.Cls;
                if (name == "__dict__") { var d = new PyDict(); foreach (var k in inst.Order) d.Set(k, inst.Attrs[k]); return d; }
                if (inst.Cls.IsException && (name == "__cause__" || name == "__context__" || name == "__traceback__")) return null;
                if (inst.Cls.IsException && name == "__suppress_context__") return false;
                if (HasUser(inst.Cls, "__getattr__")) return CallDunder(inst, "__getattr__", line, name);
                string hint = "";
                var similar = inst.Order.Concat(inst.Cls.Mro.SelectMany(c => c.Order)).FirstOrDefault(n => Similar(n, name));
                if (similar != null) hint = " Может, ты имел в виду «" + similar + "»?";
                else hint = " Атрибуты объекта обычно создаются в __init__: self." + name + " = ...";
                throw new PyError("AttributeError", "У объекта класса " + inst.Cls.Name + " нет атрибута «" + name + "»." + hint,
                    "'" + inst.Cls.Name + "' object has no attribute '" + name + "'", line);
            }
            var cls = obj as PyClass;
            if (cls != null) return ClassAttr(cls, name, line);
            var mod = obj as PyModule;
            if (mod != null)
            {
                object v;
                if (mod.Dict.TryGetValue(name, out v)) return v;
                if (name == "__name__") return mod.Name;
                throw new PyError("AttributeError", "В модуле " + mod.Name + " нет «" + name + "» (или оно недоступно в игровом Python).", "module '" + mod.Name + "' has no attribute '" + name + "'", line);
            }
            var sup = obj as PySuper;
            if (sup != null) return SuperAttr(sup, name, line);
            var fn = obj as PyFunction;
            if (fn != null)
            {
                object ov;
                if (fn.Attrs != null && name.StartsWith("__") && fn.Attrs.TryGet(name, out ov)) return ov;   // wrapper.__name__ = fn.__name__
                if (name == "__name__") return fn.Name;
                if (name == "__qualname__") return QualName(fn);
                if (name == "__doc__") return FuncDoc(fn);
                if (name == "__module__") return "__main__";
                object v;
                if (fn.Attrs != null && fn.Attrs.TryGet(name, out v)) return v;
                throw new PyError("AttributeError", "У функции " + fn.Name + " нет атрибута «" + name + "».", "'function' object has no attribute '" + name + "'", line);
            }
            var pm = obj as PyMethod;
            if (pm != null)
            {
                if (name == "__self__") return pm.Self;
                if (name == "__func__") return pm.Func;
                return GetAttr(pm.Func, name, line);
            }
            var bi = obj as PyBuiltin;
            if (bi != null && name == "__name__") return bi.Name;
            var prp = obj as PyProperty;
            if (prp != null)
            {
                if (name == "setter") return new PyBuiltin("setter", (a, k, l) => new PyProperty { Fget = prp.Fget, Fset = a[0], Fdel = prp.Fdel });
                if (name == "getter") return new PyBuiltin("getter", (a, k, l) => new PyProperty { Fget = a[0], Fset = prp.Fset, Fdel = prp.Fdel });
                if (name == "deleter") return new PyBuiltin("deleter", (a, k, l) => new PyProperty { Fget = prp.Fget, Fset = prp.Fset, Fdel = a[0] });
                if (name == "fget") return prp.Fget;
                if (name == "fset") return prp.Fset;
            }
            if (name == "__class__") return ClassOf(obj);
            var sl = obj as PySlice;
            if (sl != null && (name == "start" || name == "stop" || name == "step")) return name == "start" ? sl.Start : name == "stop" ? sl.Stop : sl.Step;
            var rg = obj as PyRange;
            if (rg != null && (name == "start" || name == "stop" || name == "step")) return name == "start" ? rg.Start : name == "stop" ? rg.Stop : rg.Step;
            var dd = obj as PyDict;
            if (dd != null && name == "default_factory" && dd.Kind == DictKind.DefaultDict) return dd.Factory;
            if (PyNum.IsNum(obj) && (name == "real" || name == "numerator")) return obj is bool ? (object)(((bool)obj) ? 1L : 0L) : obj;
            if (PyNum.IsNum(obj) && name == "imag") return obj is double ? (object)0.0 : 0L;
            if (PyNum.IsInt(obj) && name == "denominator") return 1L;
            if (HasMethod(obj, name)) return new PyBoundMethod { Self = obj, Name = name };
            throw new PyError("AttributeError", "У " + PyOps.TypeNameRuGen(obj) + " нет метода или атрибута «" + name + "»." + MethodHint(obj, name),
                "'" + PyOps.TypeName(obj) + "' object has no attribute '" + name + "'", line);
        }

        static string FuncDoc(PyFunction fn)
        {
            if (fn.Body != null && fn.Body.Count > 0)
            {
                var es = fn.Body[0] as ExprS;
                if (es != null && es.E is ConstE && ((ConstE)es.E).V is string) return (string)((ConstE)es.E).V;
            }
            return null;
        }

        object ClassAttr(PyClass cls, string name, int line)
        {
            foreach (var c in cls.Mro)
            {
                object v;
                if (c.Dict.TryGetValue(name, out v))
                {
                    if (v is PyStaticMethod) return ((PyStaticMethod)v).Func;
                    if (v is PyClassMethod) return new PyMethod { Self = cls, Func = ((PyClassMethod)v).Func };
                    return v;
                }
            }
            switch (name)
            {
                case "__name__": case "__qualname__": return cls.Name;
                case "__module__": return cls.Module;
                case "__bases__": return new PyTuple(cls.Bases.Cast<object>().ToArray());
                case "__mro__": return new PyTuple(cls.Mro.Cast<object>().ToArray());
                case "__doc__": return null;
                case "__dict__": { var d = new PyDict(); foreach (var k in cls.Order) d.Set(k, cls.Dict[k]); return d; }
            }
            if (cls.Builtin && BuiltinTypeMethod(cls, name) != null) return BuiltinTypeMethod(cls, name);
            throw new PyError("AttributeError", "У класса " + cls.Name + " нет атрибута «" + name + "».", "type object '" + cls.Name + "' has no attribute '" + name + "'", line);
        }

        object BuiltinTypeMethod(PyClass cls, string name)
        {
            if (cls == DictClass && name == "fromkeys") return new PyBuiltin("fromkeys", (a, k, l) => DictFromKeys(new PyDict(), a, l));
            if (cls.Name == "Counter" || cls.Name == "defaultdict" || cls.Name == "OrderedDict")
            {
                if (name == "fromkeys") return new PyBuiltin("fromkeys", (a, k, l) => DictFromKeys(new PyDict { Kind = cls.Name == "OrderedDict" ? DictKind.OrderedDict : DictKind.Dict }, a, l));
            }
            string tn = cls.Name;
            HashSet<string> ms;
            if (TypeMethods.TryGetValue(tn, out ms) && ms.Contains(name))
                return new PyBuiltin(tn + "." + name, (a, k, l) =>
                {
                    if (a.Count == 0) throw new PyError("TypeError", "Метод " + tn + "." + name + "() нужно вызывать с объектом: " + tn + "." + name + "(значение).", "unbound method " + tn + "." + name + "() needs an argument", l);
                    var self = a[0];
                    if (!ClassOf(self).IsSubOf(cls)) throw new PyError("TypeError", tn + "." + name + "() ожидает " + tn + ", а получил " + PyOps.TypeNameRu(self) + ".", "descriptor '" + name + "' for '" + tn + "' objects doesn't apply to a '" + PyOps.TypeName(self) + "' object", l);
                    return CallMethod(self, name, a.GetRange(1, a.Count - 1), k, l);
                });
            return null;
        }

        object MakeSuper(Frame f, int line)
        {
            var fr = f;
            while (fr != null && (fr.Func == null || fr.Func.DefiningClass == null)) fr = fr.Parent;
            if (fr == null || fr.Func.Params.Count == 0 || !fr.Vars.ContainsKey(fr.Func.Params[0].Name))
                throw new PyError("RuntimeError", "super() без аргументов работает только внутри метода класса.", "super(): no arguments", line);
            var obj = fr.Vars[fr.Func.Params[0].Name];
            return MakeSuper(fr.Func.DefiningClass, obj, line);
        }

        object MakeSuper(PyClass cls, object obj, int line)
        {
            var objType = obj is PyClass && ((PyClass)obj).IsSubOf(cls) ? (PyClass)obj : ClassOf(obj);
            if (!objType.IsSubOf(cls)) throw new PyError("TypeError", "super(): объект не является экземпляром класса " + cls.Name + ".", "super(type, obj): obj must be an instance or subtype of type", line);
            return new PySuper { ThisClass = cls, Obj = obj, ObjType = objType };
        }

        object SuperAttr(PySuper s, string name, int line)
        {
            var mro = s.ObjType.Mro;
            int i = mro.IndexOf(s.ThisClass);
            for (int j = i + 1; j < mro.Count; j++)
            {
                object v;
                if (!mro[j].Dict.TryGetValue(name, out v)) continue;
                if (v is PyProperty) return Call(((PyProperty)v).Fget, new List<object> { s.Obj }, NoKw, line);
                if (s.Obj is PyClass && (v is PyFunction || v is PyBuiltin)) return v;
                return Bind(v, s.Obj, s.ObjType);
            }
            throw new PyError("AttributeError", "У родительских классов нет «" + name + "».", "'super' object has no attribute '" + name + "'", line);
        }

        public void SetAttr(object obj, string name, object v, int line)
        {
            var inst = obj as PyInstance;
            if (inst != null)
            {
                var ca = inst.Cls.Find(name) as PyProperty;
                if (ca != null)
                {
                    if (ca.Fset == null) throw new PyError("AttributeError", "Свойство «" + name + "» только для чтения: у него нет сеттера (@" + name + ".setter).",
                        "property '" + name + "' of '" + inst.Cls.Name + "' object has no setter", line);
                    Call(ca.Fset, new List<object> { inst, v }, NoKw, line);
                    return;
                }
                if (HasUser(inst.Cls, "__setattr__")) { CallDunder(inst, "__setattr__", line, name, v); return; }
                inst.Set(name, v);
                return;
            }
            var cls = obj as PyClass;
            if (cls != null)
            {
                if (cls.Builtin) throw new PyError("TypeError", "Встроенный тип " + cls.Name + " нельзя изменять.", "cannot set '" + name + "' attribute of immutable type '" + cls.Name + "'", line);
                SetDefiningClass(v, cls);
                cls.Set(name, v); return;
            }
            var fn = obj as PyFunction;
            if (fn != null) { if (fn.Attrs == null) fn.Attrs = new PyDict(); fn.Attrs.Set(name, v); return; }
            var mod = obj as PyModule;
            if (mod != null) { mod.Dict[name] = v; return; }
            throw new PyError("AttributeError", "Нельзя добавить атрибут «" + name + "»: это " + PyOps.TypeNameRu(obj) + ", а атрибуты можно задавать только объектам своих классов.",
                "'" + PyOps.TypeName(obj) + "' object has no attribute '" + name + "'", line);
        }

        void DelAttr(object obj, string name, int line)
        {
            var inst = obj as PyInstance;
            if (inst != null)
            {
                var ca = inst.Cls.Find(name) as PyProperty;
                if (ca != null && ca.Fdel != null) { Call(ca.Fdel, new List<object> { inst }, NoKw, line); return; }
                if (inst.Del(name)) return;
                throw new PyError("AttributeError", "У объекта нет атрибута «" + name + "».", "'" + inst.Cls.Name + "' object has no attribute '" + name + "'", line);
            }
            var cls = obj as PyClass;
            if (cls != null && !cls.Builtin && cls.Dict.Remove(name)) { cls.Order.Remove(name); return; }
            throw new PyError("AttributeError", "Нельзя удалить атрибут «" + name + "».", "'" + PyOps.TypeName(obj) + "' object has no attribute '" + name + "'", line);
        }

        // ---------- Поведение объектов (вызывается из PyOps) ----------
        public string InstanceStr(PyInstance o)
        {
            var m = o.Cls.Find("__str__");
            if (m == null || m == ObjectStr) return InstanceRepr(o);
            var r = Call(Bind(m, o, o.Cls), new List<object>(), NoKw, CurrentLine);
            if (!(r is string)) throw new PyError("TypeError", "Метод __str__ должен вернуть строку (return f\"...\"), а вернул " + PyOps.TypeNameRu(r) + ".", "__str__ returned non-string (type " + PyOps.TypeName(r) + ")", CurrentLine);
            return (string)r;
        }
        public string InstanceRepr(PyInstance o)
        {
            var m = o.Cls.Find("__repr__");
            if (m == null || m == ObjectRepr) return "<" + o.Cls.Module + "." + o.Cls.Name + " object at " + PyOps.FakeAddr(o) + ">";
            var r = Call(Bind(m, o, o.Cls), new List<object>(), NoKw, CurrentLine);
            if (!(r is string)) throw new PyError("TypeError", "Метод __repr__ должен вернуть строку, а вернул " + PyOps.TypeNameRu(r) + ".", "__repr__ returned non-string (type " + PyOps.TypeName(r) + ")", CurrentLine);
            return (string)r;
        }
        public bool InstanceEq(object a, object b)
        {
            var ia = a as PyInstance;
            if (ia != null && HasUser(ia.Cls, "__eq__"))
            {
                var r = CallDunder(ia, "__eq__", CurrentLine, b);
                if (!(r is PyNotImplemented)) return PyOps.Truthy(r);
            }
            var ib = b as PyInstance;
            if (ib != null && HasUser(ib.Cls, "__eq__"))
            {
                var r = CallDunder(ib, "__eq__", CurrentLine, a);
                if (!(r is PyNotImplemented)) return PyOps.Truthy(r);
            }
            return ReferenceEquals(a, b);
        }
        public long InstanceHash(PyInstance o)
        {
            foreach (var c in o.Cls.Mro)
            {
                object v;
                if (!c.Dict.TryGetValue("__hash__", out v)) continue;
                if (v == null) throw PyOps.Unhashable(o);
                if (c.Builtin) break;
                var r = Call(Bind(v, o, o.Cls), new List<object>(), NoKw, CurrentLine);
                if (!PyNum.IsInt(r)) throw new PyError("TypeError", "Метод __hash__ должен вернуть целое число (например hash((self.x, self.y))).", "__hash__ method should return an integer", CurrentLine);
                return PyOps.Hash(r);
            }
            return o.Id * 16;
        }
        public bool InstanceTruthy(PyInstance o)
        {
            if (HasUser(o.Cls, "__bool__"))
            {
                var r = CallDunder(o, "__bool__", CurrentLine);
                if (!(r is bool)) throw new PyError("TypeError", "Метод __bool__ должен вернуть True или False.", "__bool__ should return bool, returned " + PyOps.TypeName(r), CurrentLine);
                return (bool)r;
            }
            if (HasUser(o.Cls, "__len__")) return Len(o, CurrentLine) != 0;
            return true;
        }

        long Len(object o, int line)
        {
            if (o is string) return PyStr.Len((string)o);
            if (o is PyList) return ((PyList)o).Items.Count;
            if (o is PyDict) return ((PyDict)o).Count;
            if (o is PyTuple) return ((PyTuple)o).Items.Length;
            if (o is PySet) return ((PySet)o).Count;
            if (o is PyRange) return ((PyRange)o).Length;
            if (o is PyDictView) return ((PyDictView)o).D.Count;
            var inst = o as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__len__"))
            {
                var r = CallDunder(inst, "__len__", line);
                if (!PyNum.IsInt(r)) throw new PyError("TypeError", "Метод __len__ должен вернуть целое число.", "'" + PyOps.TypeName(r) + "' object cannot be interpreted as an integer", line);
                long n = PyOps.ToLong(r);
                if (n < 0) throw new PyError("ValueError", "Метод __len__ вернул отрицательное число.", "__len__() should return >= 0", line);
                return n;
            }
            throw new PyError("TypeError", "У " + PyOps.TypeNameRuGen(o) + " нет длины. len() работает со строками, списками, словарями, множествами." +
                (PyOps.IsInt(o) ? " Чтобы узнать количество цифр в числе, используй len(str(n))." : o is PyIterator ? " Это итератор — сначала преврати его в список: len(list(...))." : ""),
                "object of type '" + PyOps.TypeName(o) + "' has no len()", line);
        }

        // Порядок печати Counter: по убыванию количества (most_common)
        public IEnumerable<KeyValuePair<object, object>> CounterOrder(PyDict d)
        {
            var items = d.Pairs().ToList();
            try
            {
                var idx = Enumerable.Range(0, items.Count).ToArray();
                StableSortIdx(idx, (x, y) => Less(items[y].Value, items[x].Value, CurrentLine));
                return idx.Select(i => items[i]).ToList();
            }
            catch (PyError) { return items; }
        }

        // ---------- Сортировка (стабильная, только через <) ----------
        static void StableSortIdx(int[] a, Func<int, int, bool> before)
        {
            var tmp = new int[a.Length];
            MergeSort(a, tmp, 0, a.Length, before);
        }
        static void MergeSort(int[] a, int[] tmp, int lo, int hi, Func<int, int, bool> before)
        {
            if (hi - lo < 2) return;
            if (hi - lo <= 8)
            {
                for (int i = lo + 1; i < hi; i++)
                {
                    int x = a[i], j = i;
                    while (j > lo && before(x, a[j - 1])) { a[j] = a[j - 1]; j--; }
                    a[j] = x;
                }
                return;
            }
            int mid = (lo + hi) / 2;
            MergeSort(a, tmp, lo, mid, before);
            MergeSort(a, tmp, mid, hi, before);
            if (!before(a[mid], a[mid - 1])) return;
            int p = lo, q = mid, k = lo;
            while (p < mid && q < hi) tmp[k++] = before(a[q], a[p]) ? a[q++] : a[p++];
            while (p < mid) tmp[k++] = a[p++];
            while (q < hi) tmp[k++] = a[q++];
            Array.Copy(tmp, lo, a, lo, hi - lo);
        }

        void SortList(List<object> l, object key, bool rev, int line)
        {
            int n = l.Count;
            if (n < 2) { if (key != null && n == 1) Call(key, new List<object> { l[0] }, NoKw, line); return; }
            var keys = new object[n];
            for (int i = 0; i < n; i++) keys[i] = key == null ? l[i] : Call(key, new List<object> { l[i] }, NoKw, line);
            var idx = new int[n];
            for (int i = 0; i < n; i++) idx[i] = i;
            bool allLong = keys.All(x => x is long), allStr = !allLong && keys.All(x => x is string);
            Func<int, int, bool> before;
            if (allLong) before = rev ? (Func<int, int, bool>)((x, y) => (long)keys[y] < (long)keys[x]) : (x, y) => (long)keys[x] < (long)keys[y];
            else if (allStr) before = rev ? (Func<int, int, bool>)((x, y) => PyStr.Compare((string)keys[y], (string)keys[x]) < 0) : (x, y) => PyStr.Compare((string)keys[x], (string)keys[y]) < 0;
            else before = rev ? (Func<int, int, bool>)((x, y) => Less(keys[y], keys[x], line)) : (x, y) => Less(keys[x], keys[y], line);
            StableSortIdx(idx, before);
            var res = new object[n];
            for (int i = 0; i < n; i++) res[i] = l[idx[i]];
            l.Clear(); l.AddRange(res);
        }
    }
}

namespace Intern.Py
{
    // ================== Встроенные типы, исключения и функции ==================
    public partial class Interp
    {
        public PyClass ObjectClass, TypeClass, IntClass, FloatClass, StrClass, BoolClass, ListClass, TupleClass, DictClass, SetClass, FrozenSetClass,
            RangeClass, NoneClass, FunctionClass, BuiltinFnClass, MethodClass, ModuleClass, SliceClass, PropertyClass, StaticMethodClass, ClassMethodClass,
            SuperClass, BaseExceptionClass, CounterClass, DefaultDictClass, OrderedDictClass;
        PyBuiltin ObjectInit, ObjectStr, ObjectRepr, SuperBuiltin;
        PyStaticMethod ObjectNew;
        readonly Dictionary<string, PyClass> excClasses = new Dictionary<string, PyClass>();
        readonly Dictionary<string, PyClass> miscClasses = new Dictionary<string, PyClass>();
        readonly Dictionary<string, PyModule> modules = new Dictionary<string, PyModule>();

        PyClass MakeType(string name, PyClass baseCls, BuiltinFn ctor, string module = "builtins")
        {
            var c = new PyClass { Name = name, Builtin = true, Ctor = ctor, Module = module };
            if (baseCls != null) c.Bases.Add(baseCls);
            c.Mro.Add(c);
            if (baseCls != null) c.Mro.AddRange(baseCls.Mro);
            return c;
        }

        BuiltinFn NoCtor(string name)
        {
            return (a, kw, line) => { throw new PyError("TypeError", "Объекты типа " + name + " нельзя создавать напрямую.", "cannot create '" + name + "' instances", line); };
        }

        void InitTypes()
        {
            ObjectClass = MakeType("object", null, (a, kw, line) =>
            {
                if (a.Count > 0 || kw.Count > 0) throw new PyError("TypeError", "object() не принимает аргументы.", "object() takes no arguments", line);
                return new PyInstance(ObjectClass);
            });
            ObjectInit = new PyBuiltin("__init__", (a, kw, line) => null);
            ObjectNew = new PyStaticMethod
            {
                Func = new PyBuiltin("__new__", (a, kw, line) =>
                {
                    var c = a.Count > 0 ? a[0] as PyClass : null;
                    if (c == null) throw new PyError("TypeError", "object.__new__(X): X должен быть классом.", "object.__new__(X): X is not a type object", line);
                    var inst = new PyInstance(c);
                    if (c.IsException) inst.Attrs["args"] = new PyTuple(a.Skip(1).ToArray());
                    return inst;
                })
            };
            ObjectStr = new PyBuiltin("__str__", (a, kw, line) => PyOps.Repr(a[0]));
            ObjectRepr = new PyBuiltin("__repr__", (a, kw, line) =>
            {
                var inst = a[0] as PyInstance;
                return inst != null ? "<" + inst.Cls.Module + "." + inst.Cls.Name + " object at " + PyOps.FakeAddr(inst) + ">" : PyOps.Repr(a[0]);
            });
            ObjectClass.Set("__init__", ObjectInit);
            ObjectClass.Set("__new__", ObjectNew);
            ObjectClass.Set("__str__", ObjectStr);
            ObjectClass.Set("__repr__", ObjectRepr);
            ObjectClass.Set("__eq__", new PyBuiltin("__eq__", (a, kw, line) => ReferenceEquals(a[0], a[1]) ? (object)true : PyNotImplemented.I));
            ObjectClass.Set("__ne__", new PyBuiltin("__ne__", (a, kw, line) => !PyOps.Eq(a[0], a[1])));
            ObjectClass.Set("__hash__", new PyBuiltin("__hash__", (a, kw, line) => a[0] is PyInstance ? ((PyInstance)a[0]).Id * 16 : PyOps.Hash(a[0])));

            TypeClass = MakeType("type", ObjectClass, (a, kw, line) =>
            {
                if (a.Count == 1) return ClassOf(a[0]);
                if (a.Count == 3 && a[0] is string && a[1] is PyTuple && a[2] is PyDict)
                {
                    var bases = ((PyTuple)a[1]).Items.Select(x => (PyClass)x).ToList();
                    if (bases.Count == 0) bases.Add(ObjectClass);
                    var c = new PyClass { Name = (string)a[0], Bases = bases };
                    c.Mro = ComputeMro(c, line);
                    c.IsException = bases.Any(b => b.IsException);
                    foreach (var p in ((PyDict)a[2]).Pairs()) { SetDefiningClass(p.Value, c); c.Set((string)p.Key, p.Value); }
                    return c;
                }
                throw new PyError("TypeError", "type() принимает 1 или 3 аргумента.", "type() takes 1 or 3 arguments", line);
            });
            IntClass = MakeType("int", ObjectClass, IntCtor);
            BoolClass = MakeType("bool", IntClass, (a, kw, line) => { Need(a, 0, 1, "bool", line); return a.Count == 0 ? false : PyOps.Truthy(a[0]); });
            FloatClass = MakeType("float", ObjectClass, FloatCtor);
            StrClass = MakeType("str", ObjectClass, (a, kw, line) =>
            {
                Need(a, 0, 3, "str", line);
                if (a.Count == 0) return "";
                return PyOps.Str(a[0]);
            });
            ListClass = MakeType("list", ObjectClass, (a, kw, line) =>
            {
                NoKwArgs(kw, "list", line); Need(a, 0, 1, "list", line);
                if (a.Count == 0) return new PyList();
                RequireIterable(a[0], line);
                return PyList.Wrap(ToList(a[0], line));
            });
            TupleClass = MakeType("tuple", ObjectClass, (a, kw, line) =>
            {
                NoKwArgs(kw, "tuple", line); Need(a, 0, 1, "tuple", line);
                if (a.Count == 0) return PyTuple.Empty;
                if (a[0] is PyTuple) return a[0];
                RequireIterable(a[0], line);
                return new PyTuple(ToList(a[0], line).ToArray());
            });
            DictClass = MakeType("dict", ObjectClass, (a, kw, line) => { var d = new PyDict(); DictUpdate(d, a, kw, "dict", line); return d; });
            SetClass = MakeType("set", ObjectClass, (a, kw, line) =>
            {
                NoKwArgs(kw, "set", line); Need(a, 0, 1, "set", line);
                if (a.Count == 0) return new PySet();
                RequireIterable(a[0], line);
                return ToSet(a[0], line);
            });
            FrozenSetClass = MakeType("frozenset", ObjectClass, (a, kw, line) =>
            {
                NoKwArgs(kw, "frozenset", line); Need(a, 0, 1, "frozenset", line);
                var s = a.Count == 0 ? new PySet() : ToSet(a[0], line);
                s.Frozen = true; return s;
            });
            RangeClass = MakeType("range", ObjectClass, RangeCtor);
            NoneClass = MakeType("NoneType", ObjectClass, (a, kw, line) => null);
            FunctionClass = MakeType("function", ObjectClass, NoCtor("function"));
            BuiltinFnClass = MakeType("builtin_function_or_method", ObjectClass, NoCtor("builtin_function_or_method"));
            MethodClass = MakeType("method", ObjectClass, NoCtor("method"));
            ModuleClass = MakeType("module", ObjectClass, NoCtor("module"));
            SliceClass = MakeType("slice", ObjectClass, (a, kw, line) =>
            {
                Need(a, 1, 3, "slice", line);
                if (a.Count == 1) return new PySlice { Stop = a[0] };
                return new PySlice { Start = a[0], Stop = a[1], Step = a.Count > 2 ? a[2] : null };
            });
            PropertyClass = MakeType("property", ObjectClass, (a, kw, line) => new PyProperty
            {
                Fget = a.Count > 0 ? a[0] : Kw(kw, "fget", null),
                Fset = a.Count > 1 ? a[1] : Kw(kw, "fset", null),
                Fdel = a.Count > 2 ? a[2] : Kw(kw, "fdel", null)
            });
            StaticMethodClass = MakeType("staticmethod", ObjectClass, (a, kw, line) => { Need(a, 1, 1, "staticmethod", line); return new PyStaticMethod { Func = a[0] }; });
            ClassMethodClass = MakeType("classmethod", ObjectClass, (a, kw, line) => { Need(a, 1, 1, "classmethod", line); return new PyClassMethod { Func = a[0] }; });
            SuperClass = MakeType("super", ObjectClass, NoCtor("super"));
            CounterClass = MakeType("Counter", DictClass, CounterCtor, "collections");
            DefaultDictClass = MakeType("defaultdict", DictClass, (a, kw, line) =>
            {
                var d = new PyDict { Kind = DictKind.DefaultDict };
                if (a.Count > 0)
                {
                    if (a[0] != null && !IsCallable(a[0])) throw new PyError("TypeError", "Первым аргументом defaultdict нужно передать функцию или тип (например list или int) — без скобок.", "first argument must be callable or None", line);
                    d.Factory = a[0];
                }
                DictUpdate(d, a.Skip(1).ToList(), kw, "defaultdict", line);
                return d;
            }, "collections");
            OrderedDictClass = MakeType("OrderedDict", DictClass, (a, kw, line) => { var d = new PyDict { Kind = DictKind.OrderedDict }; DictUpdate(d, a, kw, "OrderedDict", line); return d; }, "collections");
            InitExceptions();
        }

        void RequireIterable(object o, int line)
        {
            if (!IsIterable(o))
                throw new PyError("TypeError", "Ожидалась коллекция (список, строка, range...), а тут " + PyOps.TypeNameRu(o) + "." +
                    (PyOps.IsInt(o) ? " Чтобы получить числа от 0 до N-1, используй range(N)." : ""),
                    "'" + PyOps.TypeName(o) + "' object is not iterable", line);
        }

        public PyClass ClassOf(object o)
        {
            if (o == null) return NoneClass;
            if (o is bool) return BoolClass;
            if (o is long || o is BigInteger) return IntClass;
            if (o is double) return FloatClass;
            if (o is string) return StrClass;
            if (o is PyList) return ListClass;
            if (o is PyTuple) return TupleClass;
            if (o is PyInstance) return ((PyInstance)o).Cls;
            var d = o as PyDict;
            if (d != null) return d.Kind == DictKind.Counter ? CounterClass : d.Kind == DictKind.DefaultDict ? DefaultDictClass : d.Kind == DictKind.OrderedDict ? OrderedDictClass : DictClass;
            if (o is PySet) return ((PySet)o).Frozen ? FrozenSetClass : SetClass;
            if (o is PyRange) return RangeClass;
            if (o is PyClass) return TypeClass;
            if (o is PyFunction) return FunctionClass;
            if (o is PyBuiltin || o is PyBoundMethod) return BuiltinFnClass;
            if (o is PyMethod) return MethodClass;
            if (o is PyModule) return ModuleClass;
            if (o is PySlice) return SliceClass;
            if (o is PyProperty) return PropertyClass;
            if (o is PyStaticMethod) return StaticMethodClass;
            if (o is PyClassMethod) return ClassMethodClass;
            if (o is PySuper) return SuperClass;
            return Misc(PyOps.TypeName(o));
        }
        PyClass Misc(string name)
        {
            PyClass c;
            if (!miscClasses.TryGetValue(name, out c)) { c = MakeType(name, ObjectClass, NoCtor(name)); miscClasses[name] = c; }
            return c;
        }

        // ---------- Исключения ----------
        void InitExceptions()
        {
            BaseExceptionClass = MakeType("BaseException", ObjectClass, null);
            BaseExceptionClass.IsException = true;
            excClasses["BaseException"] = BaseExceptionClass;
            Builtins["BaseException"] = BaseExceptionClass;
            BaseExceptionClass.Set("__init__", new PyBuiltin("__init__", (a, kw, line) =>
            {
                var inst = a[0] as PyInstance;
                if (inst != null) inst.Attrs["args"] = new PyTuple(a.Skip(1).ToArray());
                return null;
            }));
            BaseExceptionClass.Set("__str__", new PyBuiltin("__str__", (a, kw, line) => ExcStr((PyInstance)a[0])));
            BaseExceptionClass.Set("__repr__", new PyBuiltin("__repr__", (a, kw, line) =>
            {
                var inst = (PyInstance)a[0];
                var args = ExcArgsOf(inst);
                return inst.Cls.Name + "(" + string.Join(", ", args.Select(x => PyOps.Repr(x)).ToArray()) + ")";
            }));
            string[] tree = {
                "Exception:BaseException", "SystemExit:BaseException", "KeyboardInterrupt:BaseException", "GeneratorExit:BaseException",
                "ArithmeticError:Exception", "ZeroDivisionError:ArithmeticError", "OverflowError:ArithmeticError", "FloatingPointError:ArithmeticError",
                "AssertionError:Exception", "AttributeError:Exception", "EOFError:Exception", "ImportError:Exception", "ModuleNotFoundError:ImportError",
                "LookupError:Exception", "IndexError:LookupError", "KeyError:LookupError", "MemoryError:Exception",
                "NameError:Exception", "UnboundLocalError:NameError", "OSError:Exception", "PermissionError:OSError", "FileNotFoundError:OSError",
                "TimeoutError:OSError", "ConnectionError:OSError", "FileExistsError:OSError", "RuntimeError:Exception", "NotImplementedError:RuntimeError",
                "RecursionError:RuntimeError", "StopIteration:Exception", "SyntaxError:Exception", "IndentationError:SyntaxError", "TabError:IndentationError",
                "TypeError:Exception", "ValueError:Exception", "UnicodeError:ValueError", "Warning:Exception", "UserWarning:Warning",
                "DeprecationWarning:Warning", "RuntimeWarning:Warning" };
            foreach (var t in tree)
            {
                var parts = t.Split(':');
                var c = MakeType(parts[0], excClasses[parts[1]], null);
                c.IsException = true;
                excClasses[parts[0]] = c;
                Builtins[parts[0]] = c;
            }
            Builtins["EnvironmentError"] = excClasses["OSError"];
            Builtins["IOError"] = excClasses["OSError"];
        }

        public PyClass ExcClass(string name)
        {
            PyClass c;
            if (excClasses.TryGetValue(name, out c)) return c;
            c = MakeType(name, excClasses["Exception"], null);
            c.IsException = true;
            excClasses[name] = c;
            return c;
        }

        PyClass ExcClassOf(PyError e)
        {
            var inst = e.Value as PyInstance;
            return inst != null ? inst.Cls : ExcClass(e.PyType);
        }

        public object ExcValue(PyError e)
        {
            if (e.Value != null) return e.Value;
            var inst = new PyInstance(ExcClass(e.PyType));
            inst.Attrs["args"] = new PyTuple(e.ExcArgs ?? new object[] { e.PyMsg ?? e.Ru });
            e.Value = inst;
            return inst;
        }

        static object[] ExcArgsOf(PyInstance inst)
        {
            object a;
            if (inst.Attrs.TryGetValue("args", out a) && a is PyTuple) return ((PyTuple)a).Items;
            return new object[0];
        }

        string ExcStr(PyInstance inst)
        {
            var args = ExcArgsOf(inst);
            if (args.Length == 0) return "";
            if (args.Length == 1) return inst.Cls.IsSubOf(excClasses["KeyError"]) ? PyOps.Repr(args[0]) : PyOps.Str(args[0]);
            return PyOps.Repr(new PyTuple(args));
        }

        // ---------- Конструкторы встроенных типов ----------
        object IntCtor(List<object> a, Dictionary<string, object> kw, int line)
        {
            OnlyKw(kw, "int", line, "base");
            if (a.Count == 0 && kw.Count == 0) return 0L;
            Need(a, 1, 2, "int", line);
            var o = a[0];
            object bo = a.Count > 1 ? a[1] : Kw(kw, "base", null);
            if (bo != null)
            {
                if (!(o is string)) throw new PyError("TypeError", "int(x, base) с основанием работает только со строками.", "int() can't convert non-string with explicit base", line);
                long b = PyOps.ToLong(bo);
                if (b != 0 && (b < 2 || b > 36)) throw new PyError("ValueError", "Основание системы счисления должно быть от 2 до 36.", "int() base must be >= 2 and <= 36, or 0", line);
                return ParseInt((string)o, (int)b, line);
            }
            if (o is long || o is BigInteger) return o;
            if (o is bool) return ((bool)o) ? 1L : 0L;
            if (o is double)
            {
                double d = (double)o;
                if (double.IsNaN(d)) throw new PyError("ValueError", "NaN (не число) нельзя превратить в целое.", "cannot convert float NaN to integer", line);
                if (double.IsInfinity(d)) throw new PyError("OverflowError", "Бесконечность нельзя превратить в целое число.", "cannot convert float infinity to integer", line);
                return PyNum.Norm(PyNum.FloatToBig(d));
            }
            if (o is string) return ParseInt((string)o, 10, line);
            var inst = o as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__int__")) return CallDunder(inst, "__int__", line);
            if (inst != null && HasUser(inst.Cls, "__index__")) return CallDunder(inst, "__index__", line);
            throw new PyError("TypeError", "int() не умеет превращать " + PyOps.TypeNameRu(o) + " в число." + (o == null ? " Похоже, значение равно None." : o is PyList ? " Возможно, нужно взять элемент списка или пройтись по нему циклом." : ""),
                "int() argument must be a string, a bytes-like object or a real number, not '" + PyOps.TypeName(o) + "'", line);
        }

        object ParseInt(string orig, int b, int line)
        {
            string s = orig.Trim();
            bool neg = false;
            if (s.StartsWith("+") || s.StartsWith("-")) { neg = s[0] == '-'; s = s.Substring(1); }
            string low = s.ToLowerInvariant();
            if ((b == 16 || b == 0) && low.StartsWith("0x")) { s = s.Substring(2); b = 16; if (s.StartsWith("_")) s = s.Substring(1); }
            else if ((b == 8 || b == 0) && low.StartsWith("0o")) { s = s.Substring(2); b = 8; if (s.StartsWith("_")) s = s.Substring(1); }
            else if ((b == 2 || b == 0) && low.StartsWith("0b")) { s = s.Substring(2); b = 2; if (s.StartsWith("_")) s = s.Substring(1); }
            else if (b == 0) { b = 10; if (s.Length > 1 && s[0] == '0' && s.Trim('0', '_').Length > 0) s = "!"; }
            BigInteger acc = 0; bool ok = s.Length > 0; char prev = '_';
            if (s.StartsWith("_") || s.EndsWith("_")) ok = false;
            foreach (char c in s)
            {
                if (c == '_') { if (prev == '_') { ok = false; break; } prev = c; continue; }
                int dv = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'z' ? c - 'a' + 10 : c >= 'A' && c <= 'Z' ? c - 'A' + 10 : (char.IsDigit(c) ? (int)char.GetNumericValue(c) : 99);
                if (dv >= b) { ok = false; break; }
                acc = acc * b + dv; prev = c;
            }
            if (!ok)
            {
                string ru = "Строку " + PyOps.Repr(orig) + " нельзя превратить в целое число.";
                string t = orig.Trim();
                if (t.Length == 0) ru += " Строка пустая — возможно, ввод закончился или в нём лишняя пустая строка.";
                else if (t.Contains(".") || t.Contains(",")) ru += " Для дробных чисел используй float(...).";
                else if (t.Contains(" ")) ru += " В строке несколько значений через пробел — раздели их через split().";
                else ru += " В ней должны быть только цифры.";
                throw new PyError("ValueError", ru, "invalid literal for int() with base " + b + ": " + PyOps.Repr(orig), line);
            }
            return PyNum.Norm(neg ? -acc : acc);
        }

        object FloatCtor(List<object> a, Dictionary<string, object> kw, int line)
        {
            NoKwArgs(kw, "float", line);
            Need(a, 0, 1, "float", line);
            if (a.Count == 0) return 0.0;
            var o = a[0];
            if (PyNum.IsNum(o)) return PyNum.ToDouble(o);
            var s = o as string;
            if (s != null)
            {
                double v;
                if (PyNum.TryParseFloat(s, out v)) return v;
                if (s.Contains(",") && !s.Contains(".")) throw new PyError("ValueError", "В Python дробная часть отделяется точкой, а не запятой: 3.5, а не 3,5.", "could not convert string to float: " + PyOps.Repr(s), line);
                throw new PyError("ValueError", "Строку " + PyOps.Repr(o) + " нельзя превратить в число." + (s.Trim().Length == 0 ? " Строка пустая." : ""), "could not convert string to float: " + PyOps.Repr(s), line);
            }
            var inst = o as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__float__")) return CallDunder(inst, "__float__", line);
            throw new PyError("TypeError", "float() не умеет превращать " + PyOps.TypeNameRu(o) + " в число.",
                "float() argument must be a string or a real number, not '" + PyOps.TypeName(o) + "'", line);
        }

        object RangeCtor(List<object> a, Dictionary<string, object> kw, int line)
        {
            NoKwArgs(kw, "range", line);
            Need(a, 1, 3, "range", line);
            foreach (var x in a)
            {
                if (!PyOps.IsInt(x))
                    throw new PyError("TypeError", "range() принимает только целые числа, а получил " + PyOps.TypeNameRu(x) + "." +
                        (x is string ? " Преврати строку в число: int(...)." : x is double ? " Используй целое число: int(x) или деление //." : ""),
                        "'" + PyOps.TypeName(x) + "' object cannot be interpreted as an integer", line);
                if (x is BigInteger) throw new PyError("OverflowError", "Слишком большое число для range().", "Python int too large to convert to C ssize_t", line);
            }
            var r = new PyRange { Start = 0, Step = 1 };
            if (a.Count == 1) r.Stop = PyOps.ToLong(a[0]);
            else { r.Start = PyOps.ToLong(a[0]); r.Stop = PyOps.ToLong(a[1]); if (a.Count == 3) r.Step = PyOps.ToLong(a[2]); }
            if (r.Step == 0) throw new PyError("ValueError", "Шаг range() не может быть 0.", "range() arg 3 must not be zero", line);
            return r;
        }

        void DictUpdate(PyDict d, List<object> a, Dictionary<string, object> kw, string fname, int line)
        {
            if (a.Count > 1) throw new PyError("TypeError", fname + "() принимает не больше одного позиционного аргумента.", fname + " expected at most 1 argument, got " + a.Count, line);
            if (a.Count == 1)
            {
                var src = a[0] as PyDict;
                if (src != null) foreach (var p in src.Pairs()) d.Set(p.Key, p.Value);
                else if (a[0] is PyInstance && HasUser(((PyInstance)a[0]).Cls, "keys"))
                {
                    var inst = (PyInstance)a[0];
                    foreach (var k in Iterate(CallDunder(inst, "keys", line), line)) d.Set(k, GetItem(inst, k, line));
                }
                else
                {
                    if (!IsIterable(a[0])) throw new PyError("TypeError", fname + "() ожидает словарь или список пар (ключ, значение), а получил " + PyOps.TypeNameRu(a[0]) + ".", "'" + PyOps.TypeName(a[0]) + "' object is not iterable", line);
                    int i = 0;
                    foreach (var it in Iterate(a[0], line))
                    {
                        if (!IsIterable(it)) throw new PyError("TypeError", "Каждый элемент должен быть парой (ключ, значение), а элемент №" + i + " — " + PyOps.TypeNameRu(it) + ".",
                            "cannot convert dictionary update sequence element #" + i + " to a sequence", line);
                        var pr = ToList(it, line);
                        if (pr.Count != 2) throw new PyError("ValueError", "Каждый элемент должен быть парой (ключ, значение), а в элементе №" + i + " их " + pr.Count + ".",
                            "dictionary update sequence element #" + i + " has length " + pr.Count + "; 2 is required", line);
                        d.Set(pr[0], pr[1]); i++;
                    }
                }
            }
            foreach (var k in kw) d.Set(k.Key, k.Value);
        }

        object CounterCtor(List<object> a, Dictionary<string, object> kw, int line)
        {
            var c = new PyDict { Kind = DictKind.Counter };
            CounterUpdate(c, a, kw, 1, line);
            return c;
        }

        void CounterUpdate(PyDict c, List<object> a, Dictionary<string, object> kw, int sign, int line)
        {
            if (a.Count > 1) throw new PyError("TypeError", "Counter принимает не больше одного позиционного аргумента.", "expected at most 1 argument, got " + a.Count, line);
            if (a.Count == 1 && a[0] != null)
            {
                var src = a[0] as PyDict;
                if (src != null)
                {
                    foreach (var p in src.Pairs())
                    {
                        object cur; if (!c.TryGet(p.Key, out cur)) cur = 0L;
                        c.Set(p.Key, BinOp(sign > 0 ? "+" : "-", cur, p.Value, line));
                    }
                }
                else
                {
                    RequireIterable(a[0], line);
                    foreach (var x in Iterate(a[0], line))
                    {
                        object cur; if (!c.TryGet(x, out cur)) cur = 0L;
                        c.Set(x, BinOp(sign > 0 ? "+" : "-", cur, 1L, line));
                    }
                }
            }
            foreach (var k in kw)
            {
                object cur; if (!c.TryGet(k.Key, out cur)) cur = 0L;
                c.Set(k.Key, BinOp(sign > 0 ? "+" : "-", cur, k.Value, line));
            }
        }

        object DictFromKeys(PyDict d, List<object> a, int line)
        {
            Need(a, 1, 2, "fromkeys", line);
            var v = a.Count > 1 ? a[1] : null;
            foreach (var k in Iterate(a[0], line)) d.Set(k, v);
            return d;
        }

        public bool IsCallable(object o)
        {
            if (o is PyFunction || o is PyBuiltin || o is PyMethod || o is PyBoundMethod || o is PyClass || o is PyStaticMethod) return true;
            var inst = o as PyInstance;
            return inst != null && HasUser(inst.Cls, "__call__");
        }

        // ---------- Встроенные функции ----------
        void InitBuiltins()
        {
            Action<string, BuiltinFn> add = (n, fn) => Builtins[n] = new PyBuiltin(n, fn);
            Builtins["__name__"] = "__main__";
            Builtins["NotImplemented"] = PyNotImplemented.I;
            Builtins["Ellipsis"] = PyEllipsis.I;
            foreach (var c in new[] { ObjectClass, TypeClass, IntClass, BoolClass, FloatClass, StrClass, ListClass, TupleClass, DictClass, SetClass, FrozenSetClass,
                                      RangeClass, SliceClass, PropertyClass, StaticMethodClass, ClassMethodClass })
                Builtins[c.Name] = c;
            SuperBuiltin = new PyBuiltin("super", (a, kw, line) =>
            {
                if (a.Count == 2 && a[0] is PyClass) return MakeSuper((PyClass)a[0], a[1], line);
                throw new PyError("RuntimeError", "super() без аргументов работает только внутри метода класса.", "super(): no arguments", line);
            });
            Builtins["super"] = SuperBuiltin;

            add("print", (a, kw, line) =>
            {
                string sep = " ", end = "\n";
                foreach (var k in kw)
                {
                    if (k.Key == "sep" || k.Key == "end")
                    {
                        if (k.Value != null && !(k.Value is string))
                            throw new PyError("TypeError", "Параметр " + k.Key + " у print() должен быть строкой.", k.Key + " must be None or a string, not " + PyOps.TypeName(k.Value), line);
                        if (k.Value != null) { if (k.Key == "sep") sep = (string)k.Value; else end = (string)k.Value; }
                    }
                    else if (k.Key != "file" && k.Key != "flush")
                        throw new PyError("TypeError", "У print() нет параметра «" + k.Key + "». Есть sep= и end=.", "'" + k.Key + "' is an invalid keyword argument for print()", line);
                }
                var sb = new StringBuilder();
                for (int i = 0; i < a.Count; i++) { if (i > 0) sb.Append(sep); sb.Append(PyOps.Str(a[i])); }
                sb.Append(end);
                Print(sb.ToString());
                return null;
            });
            add("input", (a, kw, line) =>
            {
                Need(a, 0, 1, "input", line);
                if (a.Count > 0) Print(PyOps.Str(a[0]));
                if (Inputs.Count == 0)
                    throw new PyError("EOFError", "input() ждёт ввод, но входных данных больше нет. Проверь, не вызываешь ли ты input() больше раз, чем нужно в задаче.", "EOF when reading a line", line);
                var s = Inputs.Dequeue();
                Out(s + "\n");
                return s;
            });
            add("len", (a, kw, line) => { Need(a, 1, 1, "len", line); return Len(a[0], line); });
            add("repr", (a, kw, line) => { Need(a, 1, 1, "repr", line); return PyOps.Repr(a[0]); });
            add("ascii", (a, kw, line) => { Need(a, 1, 1, "ascii", line); return Ascii(PyOps.Repr(a[0])); });
            add("abs", (a, kw, line) =>
            {
                Need(a, 1, 1, "abs", line); var o = a[0];
                if (o is long) { long x = (long)o; return x == long.MinValue ? PyNum.Norm(BigInteger.Abs(x)) : (object)Math.Abs(x); }
                if (o is bool) return ((bool)o) ? 1L : 0L;
                if (o is double) return Math.Abs((double)o);
                if (o is BigInteger) return PyNum.Norm(BigInteger.Abs((BigInteger)o));
                var inst = o as PyInstance;
                if (inst != null && HasUser(inst.Cls, "__abs__")) return CallDunder(inst, "__abs__", line);
                throw new PyError("TypeError", "abs() работает только с числами, а тут " + PyOps.TypeNameRu(o) + ".", "bad operand type for abs(): '" + PyOps.TypeName(o) + "'", line);
            });
            add("round", (a, kw, line) =>
            {
                OnlyKw(kw, "round", line, "ndigits");
                Need(a, 1, 2, "round", line);
                object nd = a.Count > 1 ? a[1] : Kw(kw, "ndigits", null);
                return Round(a[0], nd, line);
            });
            add("min", (a, kw, line) => MinMax(a, kw, false, line));
            add("max", (a, kw, line) => MinMax(a, kw, true, line));
            add("sum", (a, kw, line) =>
            {
                OnlyKw(kw, "sum", line, "start");
                Need(a, 1, 2, "sum", line);
                object acc = a.Count == 2 ? a[1] : Kw(kw, "start", 0L);
                if (acc is string) throw new PyError("TypeError", "sum() не складывает строки — для них используй ''.join(список).", "sum() can't sum strings [use ''.join(seq) instead]", line);
                RequireIterable(a[0], line);
                foreach (var x in Iterate(a[0], line))
                {
                    if (acc is long && x is long)
                    {
                        long p = (long)acc, q = (long)x, r = unchecked(p + q);
                        if (((p ^ r) & (q ^ r)) >= 0) { acc = r; continue; }
                    }
                    if (x is string || (acc is PyList && !(x is PyList)))
                        throw new PyError("TypeError", "sum() складывает только числа, а в коллекции есть " + PyOps.TypeNameRu(x) + " " + PyOps.Repr(x) + "." +
                            (x is string ? " Преврати строки в числа: sum(int(x) for x in ...)." : ""),
                            "unsupported operand type(s) for +: '" + PyOps.TypeName(acc) + "' and '" + PyOps.TypeName(x) + "'", line);
                    acc = BinOp("+", acc, x, line);
                }
                return acc;
            });
            add("sorted", (a, kw, line) =>
            {
                OnlyKw(kw, "sorted", line, "key", "reverse");
                Need(a, 1, 1, "sorted", line);
                RequireIterable(a[0], line);
                var l = ToList(a[0], line);
                SortList(l, Kw(kw, "key", null), PyOps.ToLong(Kw(kw, "reverse", false)) != 0, line);
                return PyList.Wrap(l);
            });
            add("reversed", (a, kw, line) => { Need(a, 1, 1, "reversed", line); return Reversed(a[0], line); });
            add("enumerate", (a, kw, line) =>
            {
                OnlyKw(kw, "enumerate", line, "start", "iterable");
                if (a.Count == 0 && kw.ContainsKey("iterable")) a = new List<object> { kw["iterable"] };
                Need(a, 1, 2, "enumerate", line);
                object start = a.Count == 2 ? a[1] : Kw(kw, "start", 0L);
                if (!PyOps.IsInt(start)) throw new PyError("TypeError", "start в enumerate() должен быть целым числом.", "'" + PyOps.TypeName(start) + "' object cannot be interpreted as an integer", line);
                RequireIterable(a[0], line);
                return new PyIterator(Enumerate(Iterate(a[0], line), start is bool ? (object)(((bool)start) ? 1L : 0L) : start), "enumerate");
            });
            add("zip", (a, kw, line) =>
            {
                OnlyKw(kw, "zip", line, "strict");
                for (int i = 0; i < a.Count; i++)
                    if (!IsIterable(a[i])) throw new PyError("TypeError", "Аргумент №" + (i + 1) + " в zip() — " + PyOps.TypeNameRu(a[i]) + ", а нужна коллекция.", "'" + PyOps.TypeName(a[i]) + "' object is not iterable", line);
                var es = a.Select(x => Iterate(x, line).GetEnumerator()).ToList();
                return new PyIterator(Zip(es, PyOps.Truthy(Kw(kw, "strict", false)), line), "zip");
            });
            add("map", (a, kw, line) =>
            {
                NoKwArgs(kw, "map", line);
                if (a.Count < 2) throw new PyError("TypeError", "map() нужна функция и хотя бы одна коллекция: map(func, items).", "map() must have at least two arguments.", line);
                for (int i = 1; i < a.Count; i++) RequireIterable(a[i], line);
                var es = a.Skip(1).Select(x => Iterate(x, line).GetEnumerator()).ToList();
                return new PyIterator(MapIter(a[0], es, line), "map");
            });
            add("filter", (a, kw, line) =>
            {
                NoKwArgs(kw, "filter", line); Need(a, 2, 2, "filter", line);
                RequireIterable(a[1], line);
                return new PyIterator(FilterIter(a[0], Iterate(a[1], line), line), "filter");
            });
            add("any", (a, kw, line) => { Need(a, 1, 1, "any", line); RequireIterable(a[0], line); foreach (var x in Iterate(a[0], line)) if (PyOps.Truthy(x)) return true; return false; });
            add("all", (a, kw, line) => { Need(a, 1, 1, "all", line); RequireIterable(a[0], line); foreach (var x in Iterate(a[0], line)) if (!PyOps.Truthy(x)) return false; return true; });
            add("isinstance", (a, kw, line) =>
            {
                Need(a, 2, 2, "isinstance", line);
                return IsInstanceOf(ClassOf(a[0]), a[1], "isinstance", line);
            });
            add("issubclass", (a, kw, line) =>
            {
                Need(a, 2, 2, "issubclass", line);
                var c = a[0] as PyClass;
                if (c == null) throw new PyError("TypeError", "Первый аргумент issubclass() должен быть классом.", "issubclass() arg 1 must be a class", line);
                return IsInstanceOf(c, a[1], "issubclass", line);
            });
            add("hasattr", (a, kw, line) =>
            {
                Need(a, 2, 2, "hasattr", line);
                if (!(a[1] is string)) throw new PyError("TypeError", "Имя атрибута в hasattr() должно быть строкой.", "attribute name must be string, not '" + PyOps.TypeName(a[1]) + "'", line);
                try { GetAttr(a[0], (string)a[1], line); return true; }
                catch (PyError e) { if (e.PyType == "AttributeError") return false; throw; }
            });
            add("getattr", (a, kw, line) =>
            {
                Need(a, 2, 3, "getattr", line);
                if (!(a[1] is string)) throw new PyError("TypeError", "Имя атрибута в getattr() должно быть строкой.", "attribute name must be string, not '" + PyOps.TypeName(a[1]) + "'", line);
                if (a.Count == 2) return GetAttr(a[0], (string)a[1], line);
                try { return GetAttr(a[0], (string)a[1], line); }
                catch (PyError e) { if (e.PyType == "AttributeError") return a[2]; throw; }
            });
            add("setattr", (a, kw, line) =>
            {
                Need(a, 3, 3, "setattr", line);
                if (!(a[1] is string)) throw new PyError("TypeError", "Имя атрибута в setattr() должно быть строкой.", "attribute name must be string, not '" + PyOps.TypeName(a[1]) + "'", line);
                SetAttr(a[0], (string)a[1], a[2], line); return null;
            });
            add("delattr", (a, kw, line) => { Need(a, 2, 2, "delattr", line); DelAttr(a[0], PyOps.Str(a[1]), line); return null; });
            add("divmod", (a, kw, line) =>
            {
                Need(a, 2, 2, "divmod", line);
                object x = a[0], y = a[1];
                if (PyOps.IsInt(x) && PyOps.IsInt(y))
                {
                    if (IsZero(y)) throw ZeroDiv("integer division or modulo by zero", "Делить на ноль нельзя.", line);
                    if (x is bool) x = ((bool)x) ? 1L : 0L; if (y is bool) y = ((bool)y) ? 1L : 0L;
                    return new PyTuple(new[] { PyNum.FloorDiv(x, y), PyNum.Mod(x, y) });
                }
                if (PyNum.IsNum(x) && PyNum.IsNum(y))
                {
                    double dx = PyNum.ToDouble(x), dy = PyNum.ToDouble(y);
                    if (dy == 0) throw ZeroDiv("float divmod()", "Делить на ноль нельзя.", line);
                    double q, m; FloatDivmod(dx, dy, out q, out m);
                    return new PyTuple(new object[] { q, m });
                }
                throw new PyError("TypeError", "divmod() работает только с числами.", "unsupported operand type(s) for divmod(): '" + PyOps.TypeName(x) + "' and '" + PyOps.TypeName(y) + "'", line);
            });
            add("pow", (a, kw, line) =>
            {
                Need(a, 2, 3, "pow", line);
                if (a.Count == 2 || a[2] == null) return BinOp("**", a[0], a[1], line);
                if (!(PyOps.IsInt(a[0]) && PyOps.IsInt(a[1]) && PyOps.IsInt(a[2])))
                    throw new PyError("TypeError", "pow() с тремя аргументами работает только с целыми числами.", "pow() 3rd argument not allowed unless all arguments are integers", line);
                var m = PyNum.Big(a[2]);
                if (m.IsZero) throw new PyError("ValueError", "Модуль в pow() не может быть 0.", "pow() 3rd argument cannot be 0", line);
                var mm = BigInteger.Abs(m);
                var b = ((PyNum.Big(a[0]) % mm) + mm) % mm;
                var e = PyNum.Big(a[1]);
                if (e.Sign < 0)
                {
                    b = ModInverse(b, mm, line);
                    e = -e;
                }
                var r = BigInteger.ModPow(b, e, mm);
                if (m.Sign < 0 && !r.IsZero) r += m;
                return PyNum.Norm(r);
            });
            add("chr", (a, kw, line) =>
            {
                Need(a, 1, 1, "chr", line);
                if (!PyOps.IsInt(a[0])) throw new PyError("TypeError", "chr() принимает целое число — код символа.", "'" + PyOps.TypeName(a[0]) + "' object cannot be interpreted as an integer", line);
                long c = PyOps.ToLong(a[0]);
                if (c < 0 || c > 0x10FFFF) throw new PyError("ValueError", "Код символа должен быть от 0 до 1114111.", "chr() arg not in range(0x110000)", line);
                if (c >= 0xD800 && c <= 0xDFFF) return ((char)c).ToString();
                return char.ConvertFromUtf32((int)c);
            });
            add("ord", (a, kw, line) =>
            {
                Need(a, 1, 1, "ord", line);
                var s = a[0] as string;
                if (s == null) throw new PyError("TypeError", "ord() принимает строку из одного символа.", "ord() expected string of length 1, but " + PyOps.TypeName(a[0]) + " found", line);
                if (s.Length == 2 && char.IsSurrogatePair(s, 0)) return (long)char.ConvertToUtf32(s, 0);
                if (s.Length != 1) throw new PyError("TypeError", "ord() принимает ровно один символ, а получил строку длины " + s.Length + ".", "ord() expected a character, but string of length " + s.Length + " found", line);
                return (long)s[0];
            });
            add("bin", (a, kw, line) => { Need(a, 1, 1, "bin", line); return IntToBase(a[0], 2, "0b", line); });
            add("oct", (a, kw, line) => { Need(a, 1, 1, "oct", line); return IntToBase(a[0], 8, "0o", line); });
            add("hex", (a, kw, line) => { Need(a, 1, 1, "hex", line); return IntToBase(a[0], 16, "0x", line); });
            add("hash", (a, kw, line) => { Need(a, 1, 1, "hash", line); return PyOps.Hash(a[0]); });
            add("id", (a, kw, line) =>
            {
                Need(a, 1, 1, "id", line);
                var o = a[0];
                if (o is PyInstance) return 140000000000000L + ((PyInstance)o).Id * 64;
                return 140000000000000L + (long)(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o) & 0xFFFFFF) * 64;
            });
            add("callable", (a, kw, line) => { Need(a, 1, 1, "callable", line); return IsCallable(a[0]); });
            add("iter", (a, kw, line) =>
            {
                Need(a, 1, 1, "iter", line);
                if (a[0] is PyIterator) return a[0];
                var inst = a[0] as PyInstance;
                if (inst != null && HasUser(inst.Cls, "__iter__"))
                {
                    var r = CallDunder(inst, "__iter__", line);
                    if (r is PyInstance && HasUser(((PyInstance)r).Cls, "__next__")) return r;
                    return new PyIterator(Iterate(r, line), "iterator");
                }
                RequireIterable(a[0], line);
                string kind = a[0] is PyList ? "list_iterator" : a[0] is string ? "str_ascii_iterator" : a[0] is PyTuple ? "tuple_iterator" : a[0] is PyDict ? "dict_keyiterator" : a[0] is PySet ? "set_iterator" : a[0] is PyRange ? "range_iterator" : "iterator";
                return new PyIterator(Iterate(a[0], line), kind);
            });
            add("next", (a, kw, line) =>
            {
                Need(a, 1, 2, "next", line);
                var it = a[0] as PyIterator;
                if (it != null)
                {
                    if (it.E.MoveNext()) return it.E.Current;
                    if (a.Count > 1) return a[1];
                    throw new PyError("StopIteration", "Итератор закончился: в нём больше нет элементов.", "", line);
                }
                var inst = a[0] as PyInstance;
                if (inst != null && HasUser(inst.Cls, "__next__"))
                {
                    try { return CallDunder(inst, "__next__", line); }
                    catch (PyError e) { if (a.Count > 1 && ExcClassOf(e).IsSubOf(excClasses["StopIteration"])) return a[1]; throw; }
                }
                throw new PyError("TypeError", "next() работает только с итераторами. Для списка сначала сделай it = iter(список).", "'" + PyOps.TypeName(a[0]) + "' object is not an iterator", line);
            });
            add("format", (a, kw, line) => { Need(a, 1, 2, "format", line); return FormatValue(a[0], a.Count > 1 ? PyOps.Str(a[1]) : "", line); });
            add("vars", (a, kw, line) =>
            {
                Need(a, 1, 1, "vars", line);
                var inst = a[0] as PyInstance;
                if (inst != null) { var d = new PyDict(); foreach (var k in inst.Order) d.Set(k, inst.Attrs[k]); return d; }
                return GetAttr(a[0], "__dict__", line);
            });
            add("dir", (a, kw, line) =>
            {
                Need(a, 0, 1, "dir", line);
                var names = new SortedSet<string>(StringComparer.Ordinal);
                if (a.Count == 0) { foreach (var k in Globals.Vars.Keys) names.Add(k); }
                else
                {
                    var o = a[0];
                    var inst = o as PyInstance;
                    if (inst != null) foreach (var k in inst.Attrs.Keys) names.Add(k);
                    var cls = o as PyClass ?? ClassOf(o);
                    foreach (var c in cls.Mro) foreach (var k in c.Dict.Keys) names.Add(k);
                    HashSet<string> ms;
                    if (TypeMethods.TryGetValue(PyOps.TypeName(o), out ms)) foreach (var k in ms) names.Add(k);
                    var mod = o as PyModule;
                    if (mod != null) foreach (var k in mod.Dict.Keys) names.Add(k);
                }
                return new PyList(names.Cast<object>());
            });
            add("globals", (a, kw, line) => { var d = new PyDict(); foreach (var k in Globals.Order) if (Globals.Vars.ContainsKey(k)) d.Set(k, Globals.Vars[k]); return d; });
            BuiltinFn exit = (a, kw, line) => { throw new PyError("SystemExit", "Программа завершена через exit().", line) { ExcArgs = a.ToArray() }; };
            add("exit", exit);
            add("quit", exit);
            add("open", (a, kw, line) => { throw new PyError("OSError", "Работа с файлами в игровом Python недоступна: данные приходят через input(), а результат — через print().", "file access is not available", line); });
            BuiltinFn noEval = (a, kw, line) => { throw new PyError("NotImplementedError", "eval/exec недоступны в игровом Python — и в реальном коде их лучше избегать: это небезопасно.", "eval/exec are not available", line); };
            add("eval", noEval);
            add("exec", noEval);
        }

        static BigInteger ModInverse(BigInteger a, BigInteger m, int line)
        {
            BigInteger g = m, x = 0, x1 = 1, a1 = a;
            BigInteger b = m;
            BigInteger old_r = a, r = m, old_s = 1, s = 0;
            while (!r.IsZero)
            {
                var q = BigInteger.Divide(old_r, r);
                var t = r; r = old_r - q * r; old_r = t;
                t = s; s = old_s - q * s; old_s = t;
            }
            if (old_r != 1) throw new PyError("ValueError", "Для этого числа нет обратного по модулю.", "base is not invertible for the given modulus", line);
            return ((old_s % m) + m) % m;
        }

        object IsInstanceOf(PyClass c, object info, string fname, int line)
        {
            var cl = info as PyClass;
            if (cl != null) return c.IsSubOf(cl) || cl == ObjectClass;
            var t = info as PyTuple;
            if (t != null) { foreach (var x in t.Items) if ((bool)IsInstanceOf(c, x, fname, line)) return true; return false; }
            throw new PyError("TypeError", "Второй аргумент " + fname + "() должен быть классом (например int или str) или кортежем классов.",
                fname + "() arg 2 must be a type, a tuple of types, or a union", line);
        }

        object IntToBase(object o, int b, string prefix, int line)
        {
            if (!PyOps.IsInt(o)) throw new PyError("TypeError", "Функция ожидает целое число, а получила " + PyOps.TypeNameRu(o) + ".", "'" + PyOps.TypeName(o) + "' object cannot be interpreted as an integer", line);
            var v = PyNum.Big(o);
            bool neg = v.Sign < 0; v = BigInteger.Abs(v);
            return (neg ? "-" : "") + prefix + BigToBase(v, b);
        }
        static string BigToBase(BigInteger v, int b)
        {
            if (v.IsZero) return "0";
            if (b == 10) return v.ToString(Inv);
            var sb = new StringBuilder();
            while (!v.IsZero) { int d = (int)(v % b); sb.Insert(0, "0123456789abcdefghijklmnopqrstuvwxyz"[d]); v /= b; }
            return sb.ToString();
        }

        object Round(object x, object nd, int line)
        {
            if (x is bool) x = ((bool)x) ? 1L : 0L;
            if (nd != null && !PyOps.IsInt(nd)) throw new PyError("TypeError", "Второй аргумент round() — количество знаков — должен быть целым числом.", "'" + PyOps.TypeName(nd) + "' object cannot be interpreted as an integer", line);
            if (x is long || x is BigInteger)
            {
                if (nd == null) return x;
                long n = PyOps.ToLong(nd);
                if (n >= 0) return x;
                if (n < -400) return 0L;
                var pw = BigInteger.Pow(10, (int)-n);
                var v = PyNum.Big(x);
                BigInteger rem; var q = BigInteger.DivRem(v, pw, out rem);
                if (rem.Sign < 0) { rem += pw; q -= 1; }
                var twice = rem * 2;
                if (twice > pw || (twice == pw && !q.IsEven)) q += 1;
                return PyNum.Norm(q * pw);
            }
            if (x is double)
            {
                double d = (double)x;
                if (nd == null)
                {
                    if (double.IsNaN(d)) throw new PyError("ValueError", "NaN нельзя округлить до целого.", "cannot convert float NaN to integer", line);
                    if (double.IsInfinity(d)) throw new PyError("OverflowError", "Бесконечность нельзя округлить до целого.", "cannot convert float infinity to integer", line);
                    return PyNum.Norm(PyNum.FloatToBig(PyNum.RoundHalfEven(d)));
                }
                return PyNum.RoundFloat(d, PyOps.ToLong(nd));
            }
            var inst = x as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__round__")) return nd == null ? CallDunder(inst, "__round__", line) : CallDunder(inst, "__round__", line, nd);
            throw new PyError("TypeError", "round() работает только с числами, а тут " + PyOps.TypeNameRu(x) + "." + (x is string ? " Сначала преврати строку в число: float(...)." : ""),
                "type " + PyOps.TypeName(x) + " doesn't define __round__ method", line);
        }

        object MinMax(List<object> a, Dictionary<string, object> kw, bool isMax, int line)
        {
            string name = isMax ? "max" : "min";
            OnlyKw(kw, name, line, "key", "default");
            object key = Kw(kw, "key", null);
            List<object> items;
            if (a.Count == 0) throw new PyError("TypeError", name + "() нужен хотя бы один аргумент.", name + " expected at least 1 argument, got 0", line);
            if (a.Count == 1)
            {
                if (!IsIterable(a[0])) throw new PyError("TypeError", name + "() с одним аргументом ждёт коллекцию, а получил " + PyOps.TypeNameRu(a[0]) + ". Для нескольких чисел пиши " + name + "(a, b).",
                    "'" + PyOps.TypeName(a[0]) + "' object is not iterable", line);
                items = ToList(a[0], line);
            }
            else
            {
                if (kw.ContainsKey("default")) throw new PyError("TypeError", "default в " + name + "() можно указывать только вместе с одной коллекцией.", "Cannot specify a default for " + name + "() with multiple positional arguments", line);
                items = a;
            }
            if (items.Count == 0)
            {
                if (kw.ContainsKey("default")) return kw["default"];
                throw new PyError("ValueError", name + "() получила пустую коллекцию — не из чего выбирать. Можно задать значение по умолчанию: " + name + "(items, default=0).", name + "() arg is an empty sequence", line);
            }
            var best = items[0];
            var bestKey = key == null ? best : Call(key, new List<object> { best }, NoKw, line);
            for (int i = 1; i < items.Count; i++)
            {
                var x = items[i];
                var k = key == null ? x : Call(key, new List<object> { x }, NoKw, line);
                if (isMax ? PyOps.Truthy(RichCompare(k, bestKey, ">", line)) : PyOps.Truthy(RichCompare(k, bestKey, "<", line))) { best = x; bestKey = k; }
            }
            return best;
        }

        object Reversed(object o, int line)
        {
            var l = o as PyList;
            if (l != null) return new PyIterator(RevList(l.Items), "list_reverseiterator");
            var t = o as PyTuple;
            if (t != null) return new PyIterator(RevArr(t.Items), "reversed");
            var s = o as string;
            if (s != null) return new PyIterator(RevStr(s), "reversed");
            var r = o as PyRange;
            if (r != null) { long n = r.Length; return new PyIterator(IterRange(new PyRange { Start = r.At(n - 1), Stop = r.At(n - 1) - n * r.Step, Step = -r.Step }), "range_iterator"); }
            var d = o as PyDict;
            if (d != null) { var keys = d.KeyList(); keys.Reverse(); return new PyIterator(keys, "dict_reversekeyiterator"); }
            var dv = o as PyDictView;
            if (dv != null) { var items = ToList(dv, line); items.Reverse(); return new PyIterator(items, "dict_reverseiterator"); }
            var inst = o as PyInstance;
            if (inst != null)
            {
                if (HasUser(inst.Cls, "__reversed__")) return CallDunder(inst, "__reversed__", line);
                if (HasUser(inst.Cls, "__len__") && HasUser(inst.Cls, "__getitem__"))
                {
                    long n = Len(inst, line);
                    var res = new List<object>();
                    for (long i = n - 1; i >= 0; i--) res.Add(CallDunder(inst, "__getitem__", line, i));
                    return new PyIterator(res, "reversed");
                }
            }
            throw new PyError("TypeError", "reversed() работает со списками, строками, кортежами и range, а тут " + PyOps.TypeNameRu(o) + "." + (o is PySet ? " У множества нет порядка — сначала отсортируй его: sorted(s, reverse=True)." : o is PyIterator ? " Преврати итератор в список: reversed(list(...))." : ""),
                "'" + PyOps.TypeName(o) + "' object is not reversible", line);
        }
        static IEnumerable<object> RevList(List<object> l) { for (int i = l.Count - 1; i >= 0; i--) { if (i < l.Count) yield return l[i]; } }
        static IEnumerable<object> RevArr(object[] l) { for (int i = l.Length - 1; i >= 0; i--) yield return l[i]; }
        static IEnumerable<object> RevStr(string s)
        {
            if (PyStr.HasSurr(s)) { var cps = PyStr.Cps(s); for (int i = cps.Length - 1; i >= 0; i--) yield return cps[i]; yield break; }
            for (int i = s.Length - 1; i >= 0; i--) yield return s[i].ToString();
        }

        IEnumerable<object> Enumerate(IEnumerable<object> src, object start)
        {
            object i = start;
            foreach (var x in src) { yield return new PyTuple(new[] { i, x }); i = i is long && (long)i < long.MaxValue ? (object)((long)i + 1) : PyNum.Add(i, 1L); }
        }
        IEnumerable<object> Zip(List<IEnumerator<object>> es, bool strict, int line)
        {
            if (es.Count == 0) yield break;
            while (true)
            {
                var items = new object[es.Count];
                for (int i = 0; i < es.Count; i++)
                {
                    if (!es[i].MoveNext())
                    {
                        if (strict && (i > 0 || es.Skip(1).Any(e => e.MoveNext())))
                            throw new PyError("ValueError", "zip(strict=True): коллекции разной длины.", "zip() argument " + (i + 1) + " is shorter than argument 1", line);
                        yield break;
                    }
                    items[i] = es[i].Current;
                }
                yield return new PyTuple(items);
            }
        }
        IEnumerable<object> MapIter(object f, List<IEnumerator<object>> es, int line)
        {
            while (true)
            {
                var args = new List<object>(es.Count);
                foreach (var e in es) { if (!e.MoveNext()) yield break; args.Add(e.Current); }
                yield return Call(f, args, NoKw, line);
            }
        }
        IEnumerable<object> FilterIter(object f, IEnumerable<object> src, int line)
        {
            foreach (var x in src)
            {
                bool ok = f == null ? PyOps.Truthy(x) : PyOps.Truthy(Call(f, new List<object> { x }, NoKw, line));
                if (ok) yield return x;
            }
        }
    }
}

namespace Intern.Py
{
    // ================== Методы встроенных типов и форматирование ==================
    public partial class Interp
    {
        static HashSet<string> Set(string s) { return new HashSet<string>(s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)); }
        static readonly HashSet<string> DictMethods = Set("clear copy fromkeys get items keys pop popitem setdefault update values");
        static readonly Dictionary<string, HashSet<string>> TypeMethods = new Dictionary<string, HashSet<string>>
        {
            { "str", Set("capitalize casefold center count endswith expandtabs find format index isalnum isalpha isascii isdecimal isdigit isidentifier islower isnumeric isspace istitle isupper join ljust lower lstrip partition removeprefix removesuffix replace rfind rindex rjust rpartition rsplit rstrip split splitlines startswith strip swapcase title upper zfill encode") },
            { "list", Set("append clear copy count extend index insert pop remove reverse sort") },
            { "tuple", Set("count index") },
            { "dict", DictMethods },
            { "Counter", new HashSet<string>(DictMethods.Concat(Set("most_common elements subtract total"))) },
            { "defaultdict", DictMethods },
            { "OrderedDict", new HashSet<string>(DictMethods.Concat(Set("move_to_end"))) },
            { "set", Set("add clear copy difference difference_update discard intersection intersection_update isdisjoint issubset issuperset pop remove symmetric_difference symmetric_difference_update union update") },
            { "frozenset", Set("copy difference intersection isdisjoint issubset issuperset symmetric_difference union") },
            { "int", Set("bit_length conjugate") },
            { "bool", Set("bit_length conjugate") },
            { "float", Set("is_integer conjugate") },
            { "range", Set("count index") },
            { "dict_keys", Set("isdisjoint") },
            { "dict_items", Set("isdisjoint") },
        };

        bool HasMethod(object o, string name)
        {
            HashSet<string> ms;
            return TypeMethods.TryGetValue(PyOps.TypeName(o), out ms) && ms.Contains(name);
        }

        string MethodHint(object o, string name)
        {
            if (o is string && name == "append") return " Строки неизменяемы: вместо append используй сложение s = s + \"...\".";
            if (o is string && (name == "reverse" || name == "sort")) return " Строки неизменяемы: перевёрнутая строка — s[::-1], отсортированные символы — sorted(s).";
            if (o is PyList && (name == "add" || name == "push")) return " Чтобы добавить элемент в список, используй .append(x).";
            if (o is PyList && name == "length") return " Длину считают функцией len(список).";
            if (o is PyList && (name == "split" || name == "strip" || name == "lower" || name == "upper")) return " Это метод строки, а у тебя список. Возможно, нужно применить его к каждому элементу в цикле.";
            if (o is PyList && name == "find") return " У списка есть .index(x) — но он падает, если элемента нет; проверь сначала через «in».";
            if (o is PyDict && name == "has_key") return " В Python 3 наличие ключа проверяют так: ключ in словарь.";
            if (o is PyDict && (name == "append" || name == "add")) return " В словарь добавляют так: d[ключ] = значение.";
            if (o is PySet && name == "append") return " В множество добавляют через .add(x).";
            if (o == null) return " Значение равно None — возможно, функция ничего не вернула (забыт return) или метод вроде .sort() вернул None.";
            if (PyOps.IsNum(o) && (name == "append" || name == "split" || name == "upper" || name == "lower")) return " Это число, а не строка или список. Проверь, что лежит в переменной.";
            HashSet<string> ms;
            if (TypeMethods.TryGetValue(PyOps.TypeName(o), out ms))
            {
                var close = ms.FirstOrDefault(m => Similar(m, name));
                if (close != null) return " Может, ты имел в виду «" + close + "»?";
                return " Доступные методы: " + string.Join(", ", ms.OrderBy(x => x, StringComparer.Ordinal).ToArray()) + ".";
            }
            return "";
        }

        string methodOwner, methodName;   // для сообщений вида «list.append() takes exactly one argument»

        object CallMethod(object self, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            methodOwner = PyOps.TypeName(self); methodName = name;
            var s = self as string;
            if (s != null) return StrMethod(s, name, a, kw, line);
            var l = self as PyList;
            if (l != null) return ListMethod(l, name, a, kw, line);
            var d = self as PyDict;
            if (d != null) return DictMethod(d, name, a, kw, line);
            var st = self as PySet;
            if (st != null) return SetMethod(st, name, a, kw, line);
            var t = self as PyTuple;
            if (t != null)
            {
                if (name == "count") { Need(a, 1, 1, "count", line); return (long)t.Items.Count(x => ReferenceEquals(x, a[0]) || PyOps.Eq(x, a[0])); }
                if (name == "index")
                {
                    Need(a, 1, 3, "index", line);
                    int lo, hi; SeqBounds(a, t.Items.Length, line, out lo, out hi);
                    for (int i = lo; i < hi; i++) if (ReferenceEquals(t.Items[i], a[0]) || PyOps.Eq(t.Items[i], a[0])) return (long)i;
                    throw new PyError("ValueError", "index(): элемента " + PyOps.Repr(a[0]) + " нет в кортеже.", "tuple.index(x): x not in tuple", line);
                }
            }
            if (PyOps.IsInt(self))
            {
                if (name == "bit_length") return (long)PyNum.BitLength(PyNum.Big(self));
                if (name == "conjugate") return self is bool ? (object)(((bool)self) ? 1L : 0L) : self;
            }
            if (self is double)
            {
                double v = (double)self;
                if (name == "is_integer") return !double.IsInfinity(v) && !double.IsNaN(v) && v == Math.Floor(v);
                if (name == "conjugate") return v;
            }
            var r = self as PyRange;
            if (r != null)
            {
                Need(a, 1, 1, name, line);
                bool has = Contains(r, a[0], line);
                if (name == "count") return has ? 1L : 0L;
                if (!has) throw new PyError("ValueError", PyOps.Repr(a[0]) + " нет в range.", PyOps.Repr(a[0]) + " is not in range", line);
                return (PyOps.ToLong(a[0]) - r.Start) / r.Step;
            }
            var dv = self as PyDictView;
            if (dv != null && name == "isdisjoint")
            {
                Need(a, 1, 1, name, line);
                foreach (var x in Iterate(a[0], line)) if (Contains(dv, x, line)) return false;
                return true;
            }
            throw new PyError("AttributeError", "Метод «" + name + "» не найден.", "'" + PyOps.TypeName(self) + "' object has no attribute '" + name + "'", line);
        }

        static object Arg(List<object> a, Dictionary<string, object> kw, int i, string name, object def)
        {
            if (i < a.Count) return a[i];
            object v; return kw.TryGetValue(name, out v) ? v : def;
        }

        void SeqBounds(List<object> a, int len, int line, out int lo, out int hi)
        {
            long s = a.Count > 1 && a[1] != null ? IndexValue(a[1], line, "среза") : 0;
            long e = a.Count > 2 && a[2] != null ? IndexValue(a[2], line, "среза") : len;
            if (s < 0) { s += len; if (s < 0) s = 0; }
            if (e < 0) { e += len; if (e < 0) e = 0; }
            if (e > len) e = len;
            lo = (int)Math.Min(s, len); hi = (int)e;
        }

        // ---------- str ----------
        static bool IsPySpace(char c) { return char.IsWhiteSpace(c) || (c >= '\x1c' && c <= '\x1f'); }

        string StripChars(object chars, int line, string name)
        {
            if (chars == null) return null;
            var cs = chars as string;
            if (cs == null) throw new PyError("TypeError", name + "() принимает строку символов, которые нужно убрать.", name + " arg must be None or str", line);
            return cs;
        }
        static string Strip(string s, string chars, bool left, bool right)
        {
            int i = 0, j = s.Length;
            Func<char, bool> strip = chars == null ? (Func<char, bool>)IsPySpace : c => chars.IndexOf(c) >= 0;
            if (left) while (i < j && strip(s[i])) i++;
            if (right) while (j > i && strip(s[j - 1])) j--;
            return s.Substring(i, j - i);
        }

        static void AdjustIndices(ref long start, ref long end, long len)
        {
            if (end > len) end = len; else if (end < 0) { end += len; if (end < 0) end = 0; }
            if (start < 0) { start += len; if (start < 0) start = 0; }
        }

        void FindArgs(string s, List<object> a, int line, string name, out string sub, out long start, out long end)
        {
            Need(a, 1, 3, name, line);
            sub = a[0] as string;
            if (sub == null) throw new PyError("TypeError", name + "() ищет строку, а получил " + PyOps.TypeNameRu(a[0]) + ".", "must be str, not " + PyOps.TypeName(a[0]), line);
            long len = PyStr.Len(s);
            start = a.Count > 1 && a[1] != null ? IndexValue(a[1], line, "среза") : 0;
            end = a.Count > 2 && a[2] != null ? IndexValue(a[2], line, "среза") : len;
            AdjustIndices(ref start, ref end, len);
        }

        static long FindIn(string s, string sub, long start, long end, bool last)
        {
            if (start > s.Length || end - start < sub.Length) return -1;
            var part = s.Substring((int)start, (int)(end - start));
            int i = last ? part.LastIndexOf(sub, StringComparison.Ordinal) : part.IndexOf(sub, StringComparison.Ordinal);
            if (last && sub.Length == 0) i = part.Length;
            return i < 0 ? -1 : i + start;
        }

        // Полные таблицы регистра Unicode (SpecialCasing): ß → SS, лигатуры, İ → i̇
        static string PyUpper(string s)
        {
            var u = s.ToUpperInvariant();
            if (u.IndexOf('ß') < 0 && u.IndexOf('ŉ') < 0 && u.IndexOf('ς') < 0 && u.IndexOf('ǅ') < 0 && u.IndexOf('ǈ') < 0 && u.IndexOf('ǋ') < 0 && u.IndexOf('ǲ') < 0 && (u.IndexOf('\uFB00') < 0 && u.IndexOf('\uFB01') < 0 && u.IndexOf('\uFB02') < 0 && u.IndexOf('\uFB03') < 0 && u.IndexOf('\uFB04') < 0 && u.IndexOf('\uFB05') < 0 && u.IndexOf('\uFB06') < 0)) return u;
            var sb = new StringBuilder(u.Length + 4);
            foreach (char c in u)
            {
                switch (c)
                {
                    case 'ß': sb.Append("SS"); break;
                    case 'ς': sb.Append('Σ'); break;
                    case 'ǅ': sb.Append('Ǆ'); break;
                    case 'ǈ': sb.Append('Ǉ'); break;
                    case 'ǋ': sb.Append('Ǌ'); break;
                    case 'ǲ': sb.Append('Ǳ'); break;
                    case 'ŉ': sb.Append("ʼN"); break;
                    case '\uFB00': sb.Append("FF"); break;
                    case '\uFB01': sb.Append("FI"); break;
                    case '\uFB02': sb.Append("FL"); break;
                    case '\uFB03': sb.Append("FFI"); break;
                    case '\uFB04': sb.Append("FFL"); break;
                    case '\uFB05': case '\uFB06': sb.Append("ST"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
        static string PyLower(string s)
        {
            if (s.IndexOf('İ') < 0 && s.IndexOf('Σ') < 0 && s.IndexOf('ǅ') < 0 && s.IndexOf('ǈ') < 0 && s.IndexOf('ǋ') < 0 && s.IndexOf('ǲ') < 0) return s.ToLowerInvariant();
            var sb = new StringBuilder(s.Length + 2);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case 'İ': sb.Append("i\u0307"); break;
                    case 'ǅ': sb.Append('ǆ'); break;
                    case 'ǈ': sb.Append('ǉ'); break;
                    case 'ǋ': sb.Append('ǌ'); break;
                    case 'ǲ': sb.Append('ǳ'); break;
                    case 'Σ':   // конечная сигма: после буквы и не перед буквой
                        sb.Append(i > 0 && char.IsLetter(s[i - 1]) && !(i + 1 < s.Length && char.IsLetter(s[i + 1])) ? 'ς' : 'σ'); break;
                    default: sb.Append(char.ToLowerInvariant(c)); break;
                }
            }
            return sb.ToString();
        }

        object StrMethod(string s, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            switch (name)
            {
                case "upper": Need(a, 0, 0, name, line); return PyUpper(s);
                case "lower": Need(a, 0, 0, name, line); return PyLower(s);
                case "casefold": Need(a, 0, 0, name, line); return PyLower(s).Replace("ß", "ss");
                case "swapcase": { var sb = new StringBuilder(s.Length); foreach (char c in s) sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.IsLower(c) ? char.ToUpperInvariant(c) : c); return sb.ToString(); }
                case "capitalize": Need(a, 0, 0, name, line); return s.Length == 0 ? s : (s[0] == 'ß' ? "Ss" : char.ToUpperInvariant(s[0]).ToString()) + PyLower(s.Substring(1));
                case "title":
                    {
                        Need(a, 0, 0, name, line);
                        var sb = new StringBuilder(s.Length); bool prevCased = false;
                        foreach (char c in s)
                        {
                            bool cased = char.IsUpper(c) || char.IsLower(c) || char.GetUnicodeCategory(c) == UnicodeCategory.TitlecaseLetter;
                            if (cased) sb.Append(prevCased ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)); else sb.Append(c);
                            prevCased = cased;
                        }
                        return sb.ToString();
                    }
                case "strip": Need(a, 0, 1, name, line); return Strip(s, StripChars(Arg(a, kw, 0, "chars", null), line, name), true, true);
                case "lstrip": Need(a, 0, 1, name, line); return Strip(s, StripChars(Arg(a, kw, 0, "chars", null), line, name), true, false);
                case "rstrip": Need(a, 0, 1, name, line); return Strip(s, StripChars(Arg(a, kw, 0, "chars", null), line, name), false, true);
                case "split":
                case "rsplit":
                    {
                        OnlyKw(kw, name, line, "sep", "maxsplit");
                        Need(a, 0, 2, name, line);
                        var sepO = Arg(a, kw, 0, "sep", null);
                        long max = PyOps.ToLong(Arg(a, kw, 1, "maxsplit", -1L));
                        if (sepO != null && !(sepO is string)) throw new PyError("TypeError", "Разделитель в " + name + "() должен быть строкой.", "must be str or None, not " + PyOps.TypeName(sepO), line);
                        var sep = (string)sepO;
                        if (sep == "") throw new PyError("ValueError", "Разделитель в " + name + "() не может быть пустой строкой. Чтобы разбить на символы, используй list(строка).", "empty separator", line);
                        return PyList.Wrap(name == "split" ? Split(s, sep, max) : RSplit(s, sep, max));
                    }
                case "splitlines":
                    {
                        bool keep = PyOps.Truthy(Arg(a, kw, 0, "keepends", false));
                        var res = new List<object>(); int i = 0, n = s.Length;
                        while (i < n)
                        {
                            int j = i;
                            while (j < n && !IsLineBreak(s[j])) j++;
                            int eol = j;
                            if (j < n) { if (s[j] == '\r' && j + 1 < n && s[j + 1] == '\n') j += 2; else j++; }
                            res.Add(s.Substring(i, (keep ? j : eol) - i));
                            i = j;
                        }
                        return PyList.Wrap(res);
                    }
                case "join":
                    {
                        Need(a, 1, 1, name, line);
                        if (!IsIterable(a[0])) throw new PyError("TypeError", "join() склеивает элементы коллекции, а получил " + PyOps.TypeNameRu(a[0]) + ".", "can only join an iterable", line);
                        var sb = new StringBuilder(); int i = 0;
                        foreach (var x in Iterate(a[0], line))
                        {
                            var xs = x as string;
                            if (xs == null) throw new PyError("TypeError", "join() склеивает только строки, а в коллекции есть " + PyOps.TypeNameRu(x) + " " + PyOps.Repr(x) + ". Преврати элементы в строки: \"...\".join(str(x) for x in список).",
                                "sequence item " + i + ": expected str instance, " + PyOps.TypeName(x) + " found", line);
                            if (i > 0) sb.Append(s);
                            sb.Append(xs); i++;
                        }
                        return sb.ToString();
                    }
                case "replace":
                    {
                        Need(a, 2, 3, name, line);
                        var o = a[0] as string; var nw = a[1] as string;
                        if (o == null || nw == null) throw new PyError("TypeError", "replace() принимает строки: s.replace(\"что\", \"на что\").", "replace() argument " + (o == null ? 1 : 2) + " must be str, not " + PyOps.TypeName(o == null ? a[0] : a[1]), line);
                        long cnt = a.Count > 2 ? PyOps.ToLong(a[2]) : -1;
                        if (cnt < 0 && o.Length > 0) return s.Replace(o, nw);
                        var sb = new StringBuilder(); int i = 0; long done = 0;
                        if (o.Length == 0)
                        {
                            for (; i <= s.Length; i++)
                            {
                                if (cnt < 0 || done < cnt) { sb.Append(nw); done++; }
                                if (i < s.Length) sb.Append(s[i]);
                            }
                            return sb.ToString();
                        }
                        while (i < s.Length)
                        {
                            int j = done < cnt ? s.IndexOf(o, i, StringComparison.Ordinal) : -1;
                            if (j < 0) { sb.Append(s, i, s.Length - i); break; }
                            sb.Append(s, i, j - i).Append(nw); i = j + o.Length; done++;
                        }
                        return sb.ToString();
                    }
                case "startswith":
                case "endswith":
                    {
                        Need(a, 1, 3, name, line);
                        long start = a.Count > 1 && a[1] != null ? IndexValue(a[1], line, "среза") : 0, end = a.Count > 2 && a[2] != null ? IndexValue(a[2], line, "среза") : s.Length;
                        AdjustIndices(ref start, ref end, s.Length);
                        IEnumerable<object> cands = a[0] is PyTuple ? ((PyTuple)a[0]).Items : new[] { a[0] };
                        foreach (var c in cands)
                        {
                            var cs = c as string;
                            if (cs == null) throw new PyError("TypeError", name + "() принимает строку или кортеж строк.", name + " first arg must be str or a tuple of str, not " + PyOps.TypeName(c), line);
                            if (start > s.Length || end - start < cs.Length) continue;
                            var part = s.Substring((int)start, (int)(end - start));
                            if (name == "startswith" ? part.StartsWith(cs, StringComparison.Ordinal) : part.EndsWith(cs, StringComparison.Ordinal)) return true;
                        }
                        return false;
                    }
                case "find":
                case "rfind":
                case "index":
                case "rindex":
                    {
                        string sub; long start, end;
                        FindArgs(s, a, line, name, out sub, out start, out end);
                        long r;
                        if (PyStr.HasSurr(s))
                        {
                            if (start > PyStr.Len(s)) r = -1;
                            else { r = FindIn(s, sub, PyStr.ToU16(s, start), Math.Max(PyStr.ToU16(s, start), PyStr.ToU16(s, end)), name[0] == 'r'); r = PyStr.ToCp(s, r); }
                        }
                        else r = FindIn(s, sub, start, end, name[0] == 'r');
                        if (r < 0 && (name == "index" || name == "rindex"))
                            throw new PyError("ValueError", "Подстрока " + PyOps.Repr(sub) + " не найдена. Если её может не быть, используй find() — он вернёт -1 вместо ошибки.", "substring not found", line);
                        return r;
                    }
                case "count":
                    {
                        string sub; long start, end;
                        FindArgs(s, a, line, name, out sub, out start, out end);
                        if (start > PyStr.Len(s) || end < start) return 0L;
                        if (sub.Length == 0) return end - start + 1;
                        if (PyStr.HasSurr(s)) { start = PyStr.ToU16(s, start); end = PyStr.ToU16(s, end); }
                        long c = 0; int i = (int)start;
                        while (true)
                        {
                            int j = s.IndexOf(sub, i, StringComparison.Ordinal);
                            if (j < 0 || j + sub.Length > end) break;
                            c++; i = j + sub.Length;
                        }
                        return c;
                    }
                case "isdigit": case "isnumeric": case "isdecimal": return s.Length > 0 && s.All(char.IsDigit);
                case "isalpha": return s.Length > 0 && s.All(char.IsLetter);
                case "isalnum": return s.Length > 0 && s.All(char.IsLetterOrDigit);
                case "isspace": return s.Length > 0 && s.All(IsPySpace);
                case "isascii": return s.All(c => c < 128);
                case "isupper": return s.Any(char.IsUpper) && !s.Any(char.IsLower);
                case "islower": return s.Any(char.IsLower) && !s.Any(char.IsUpper);
                case "istitle": return s.Length > 0 && (string)StrMethod(s, "title", new List<object>(), NoKw, line) == s && s.Any(c => char.IsUpper(c) || char.IsLower(c));
                case "isidentifier": return s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_');
                case "zfill":
                    {
                        Need(a, 1, 1, name, line);
                        long w = PyOps.ToLong(a[0]);
                        int slen = PyStr.Len(s);
                        if (slen >= w) return s;
                        int pad = (int)w - slen;
                        if (s.Length > 0 && (s[0] == '+' || s[0] == '-')) return s[0] + new string('0', pad) + s.Substring(1);
                        return new string('0', pad) + s;
                    }
                case "ljust":
                case "rjust":
                case "center":
                    {
                        Need(a, 1, 2, name, line);
                        if (!PyOps.IsInt(a[0])) throw new PyError("TypeError", name + "() ожидает ширину — целое число.", "'" + PyOps.TypeName(a[0]) + "' object cannot be interpreted as an integer", line);
                        long w = PyOps.ToLong(a[0]);
                        char fill = ' ';
                        if (a.Count > 1)
                        {
                            var fs = a[1] as string;
                            if (fs == null || fs.Length != 1) throw new PyError("TypeError", "Символ-заполнитель должен быть ровно одним символом.", "The fill character must be exactly one character long", line);
                            fill = fs[0];
                        }
                        int slen = PyStr.Len(s);
                        if (slen >= w) return s;
                        int marg = (int)w - slen;
                        if (name == "ljust") return s + new string(fill, marg);
                        if (name == "rjust") return new string(fill, marg) + s;
                        int left = marg / 2 + (marg & (int)w & 1);
                        return new string(fill, left) + s + new string(fill, marg - left);
                    }
                case "partition":
                case "rpartition":
                    {
                        Need(a, 1, 1, name, line);
                        var sep = a[0] as string;
                        if (sep == null) throw new PyError("TypeError", name + "() принимает строку-разделитель.", "must be str, not " + PyOps.TypeName(a[0]), line);
                        if (sep.Length == 0) throw new PyError("ValueError", "Разделитель не может быть пустым.", "empty separator", line);
                        int i = name == "partition" ? s.IndexOf(sep, StringComparison.Ordinal) : s.LastIndexOf(sep, StringComparison.Ordinal);
                        if (i < 0) return name == "partition" ? new PyTuple(new object[] { s, "", "" }) : new PyTuple(new object[] { "", "", s });
                        return new PyTuple(new object[] { s.Substring(0, i), sep, s.Substring(i + sep.Length) });
                    }
                case "removeprefix": Need(a, 1, 1, name, line); { var p = PyOps.Str(a[0]); return p.Length > 0 && s.StartsWith(p, StringComparison.Ordinal) ? s.Substring(p.Length) : s; }
                case "removesuffix": Need(a, 1, 1, name, line); { var p = PyOps.Str(a[0]); return p.Length > 0 && s.EndsWith(p, StringComparison.Ordinal) ? s.Substring(0, s.Length - p.Length) : s; }
                case "expandtabs":
                    {
                        long ts = PyOps.ToLong(Arg(a, kw, 0, "tabsize", 8L));
                        var sb = new StringBuilder(); int col = 0;
                        foreach (char c in s)
                        {
                            if (c == '\t') { if (ts > 0) { int n = (int)(ts - col % ts); sb.Append(' ', n); col += n; } }
                            else { sb.Append(c); col = (c == '\n' || c == '\r') ? 0 : col + 1; }
                        }
                        return sb.ToString();
                    }
                case "format": return StrFormat(s, a, kw, line);
                case "encode": throw new PyError("NotImplementedError", "Байтовые строки (bytes) не поддерживаются в игровом Python.", "bytes are not supported", line);
            }
            throw new PyError("AttributeError", "У строки нет метода «" + name + "».", "'str' object has no attribute '" + name + "'", line);
        }

        static bool IsLineBreak(char c) { return c == '\n' || c == '\r' || c == '\v' || c == '\f' || c == '\x1c' || c == '\x1d' || c == '\x1e' || c == '\x85' || c == '\u2028' || c == '\u2029'; }

        static List<object> Split(string s, string sep, long max)
        {
            var res = new List<object>();
            if (sep == null)
            {
                int i = 0, n = s.Length;
                while (max != 0)
                {
                    while (i < n && IsPySpace(s[i])) i++;
                    if (i == n) break;
                    int j = i;
                    while (i < n && !IsPySpace(s[i])) i++;
                    res.Add(s.Substring(j, i - j));
                    if (max > 0) max--;
                }
                if (i < n)
                {
                    while (i < n && IsPySpace(s[i])) i++;
                    if (i != n) res.Add(s.Substring(i));
                }
                return res;
            }
            int pos = 0;
            while (max != 0)
            {
                int j = s.IndexOf(sep, pos, StringComparison.Ordinal);
                if (j < 0) break;
                res.Add(s.Substring(pos, j - pos));
                pos = j + sep.Length;
                if (max > 0) max--;
            }
            res.Add(s.Substring(pos));
            return res;
        }

        static List<object> RSplit(string s, string sep, long max)
        {
            var res = new List<object>();
            if (sep == null)
            {
                int i = s.Length - 1;
                while (max != 0)
                {
                    while (i >= 0 && IsPySpace(s[i])) i--;
                    if (i < 0) break;
                    int j = i;
                    while (i >= 0 && !IsPySpace(s[i])) i--;
                    res.Add(s.Substring(i + 1, j - i));
                    if (max > 0) max--;
                }
                if (i >= 0)
                {
                    while (i >= 0 && IsPySpace(s[i])) i--;
                    if (i >= 0) res.Add(s.Substring(0, i + 1));
                }
                res.Reverse();
                return res;
            }
            int end = s.Length;
            while (max != 0)
            {
                if (end < sep.Length) break;
                int j = s.LastIndexOf(sep, end - 1, end, StringComparison.Ordinal);
                if (j < 0) break;
                res.Add(s.Substring(j + sep.Length, end - j - sep.Length));
                end = j;
                if (max > 0) max--;
            }
            res.Add(s.Substring(0, end));
            res.Reverse();
            return res;
        }

        // ---------- list ----------
        object ListMethod(PyList pl, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            var l = pl.Items;
            switch (name)
            {
                case "append": NoKwArgs(kw, name, line); Need(a, 1, 1, "append", line); l.Add(a[0]); return null;
                case "extend":
                    Need(a, 1, 1, "extend", line);
                    if (!IsIterable(a[0])) throw new PyError("TypeError", "extend() добавляет элементы коллекции, а получил " + PyOps.TypeNameRu(a[0]) + ". Чтобы добавить один элемент, используй append().",
                        "'" + PyOps.TypeName(a[0]) + "' object is not iterable", line);
                    { var add = ToList(a[0], line); SizeGuard((long)l.Count + add.Count, line, false); l.AddRange(add); }
                    return null;
                case "insert":
                    {
                        Need(a, 2, 2, "insert", line);
                        long i = IndexValue(a[0], line, "списка");
                        if (i < 0) { i += l.Count; if (i < 0) i = 0; }
                        if (i > l.Count) i = l.Count;
                        l.Insert((int)i, a[1]); return null;
                    }
                case "pop":
                    {
                        Need(a, 0, 1, "pop", line);
                        if (l.Count == 0) throw new PyError("IndexError", "pop() из пустого списка — удалять нечего. Проверь, что список не пуст: if список: ...", "pop from empty list", line);
                        int i;
                        if (a.Count == 0) i = l.Count - 1;
                        else
                        {
                            long k = IndexValue(a[0], line, "списка");
                            if (k < 0) k += l.Count;
                            if (k < 0 || k >= l.Count) throw new PyError("IndexError", "pop(" + PyOps.Repr(a[0]) + "): такого индекса нет в списке (элементов: " + l.Count + ").", "pop index out of range", line);
                            i = (int)k;
                        }
                        var v = l[i]; l.RemoveAt(i); return v;
                    }
                case "remove":
                    {
                        Need(a, 1, 1, "remove", line);
                        int i = l.FindIndex(x => ReferenceEquals(x, a[0]) || PyOps.Eq(x, a[0]));
                        if (i < 0) throw new PyError("ValueError", "remove(): элемента " + PyOps.Repr(a[0]) + " нет в списке. Проверь сначала: if x in список: ...", "list.remove(x): x not in list", line);
                        l.RemoveAt(i); return null;
                    }
                case "index":
                    {
                        Need(a, 1, 3, "index", line);
                        int lo, hi; SeqBounds(a, l.Count, line, out lo, out hi);
                        for (int i = lo; i < hi && i < l.Count; i++) if (ReferenceEquals(l[i], a[0]) || PyOps.Eq(l[i], a[0])) return (long)i;
                        throw new PyError("ValueError", "index(): элемента " + PyOps.Repr(a[0]) + " нет в списке. Проверь сначала через «in».", PyOps.Repr(a[0]) + " is not in list", line);
                    }
                case "count": Need(a, 1, 1, "count", line); return (long)l.Count(x => ReferenceEquals(x, a[0]) || PyOps.Eq(x, a[0]));
                case "sort":
                    if (a.Count > 0) throw new PyError("TypeError", "sort() принимает только именованные параметры: sort(key=..., reverse=True).", "sort() takes no positional arguments", line);
                    OnlyKw(kw, "sort", line, "key", "reverse");
                    SortList(l, Kw(kw, "key", null), PyOps.ToLong(Kw(kw, "reverse", false)) != 0, line);
                    return null;
                case "reverse": Need(a, 0, 0, name, line); l.Reverse(); return null;
                case "clear": Need(a, 0, 0, name, line); l.Clear(); return null;
                case "copy": Need(a, 0, 0, name, line); return new PyList(l);
            }
            throw new PyError("AttributeError", "У списка нет метода «" + name + "».", "'list' object has no attribute '" + name + "'", line);
        }

        // ---------- dict ----------
        object DictMethod(PyDict d, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            switch (name)
            {
                case "get":
                    {
                        Need(a, 1, 2, "get", line);
                        object v;
                        return d.TryGet(a[0], out v) ? v : (a.Count > 1 ? a[1] : null);
                    }
                case "keys": Need(a, 0, 0, name, line); return new PyDictView { D = d, Kind = 0 };
                case "values": Need(a, 0, 0, name, line); return new PyDictView { D = d, Kind = 1 };
                case "items": Need(a, 0, 0, name, line); return new PyDictView { D = d, Kind = 2 };
                case "pop":
                    {
                        Need(a, 1, 2, "pop", line);
                        object v;
                        if (d.TryGet(a[0], out v)) { d.Remove(a[0]); return v; }
                        if (a.Count > 1) return a[1];
                        throw KeyErr(a[0], line);
                    }
                case "popitem":
                    {
                        if (d.Count == 0) throw new PyError("KeyError", "popitem(): словарь пуст.", line) { PyMsg = "'popitem(): dictionary is empty'", ExcArgs = new object[] { "popitem(): dictionary is empty" } };
                        bool last = !(d.Kind == DictKind.OrderedDict && !PyOps.Truthy(Arg(a, kw, 0, "last", true)));
                        if (!last)
                        {
                            var first = d.Pairs().First(); d.Remove(first.Key);
                            return new PyTuple(new[] { first.Key, first.Value });
                        }
                        var p = d.PopLast();
                        return new PyTuple(new[] { p.Key, p.Value });
                    }
                case "update":
                    if (d.Kind == DictKind.Counter) { CounterUpdate(d, a, kw, 1, line); return null; }
                    DictUpdate(d, a, kw, "update", line); return null;
                case "setdefault":
                    {
                        Need(a, 1, 2, "setdefault", line);
                        object v;
                        if (d.TryGet(a[0], out v)) return v;
                        var dv = a.Count > 1 ? a[1] : null;
                        d.Set(a[0], dv); return dv;
                    }
                case "clear": Need(a, 0, 0, name, line); d.Clear(); return null;
                case "copy": Need(a, 0, 0, name, line); return d.CopyAs(d.Kind);
                case "fromkeys": return DictFromKeys(new PyDict { Kind = d.Kind == DictKind.OrderedDict ? DictKind.OrderedDict : DictKind.Dict }, a, line);
                case "most_common":
                    {
                        Need(a, 0, 1, name, line);
                        var items = CounterOrder(d).ToList();
                        object n = Arg(a, kw, 0, "n", null);
                        if (n != null) { long k = PyOps.ToLong(n); if (k < 0) k = 0; if (k < items.Count) items = items.Take((int)k).ToList(); }
                        return new PyList(items.Select(p => (object)new PyTuple(new[] { p.Key, p.Value })));
                    }
                case "elements":
                    {
                        var res = new List<object>();
                        foreach (var p in d.Pairs()) { long c = PyNum.IsInt(p.Value) ? PyOps.ToLong(p.Value) : 0; for (long i = 0; i < c; i++) res.Add(p.Key); }
                        return new PyIterator(res, "itertools.chain");
                    }
                case "subtract": CounterUpdate(d, a, kw, -1, line); return null;
                case "total": { object acc = 0L; foreach (var v in d.ValueList()) acc = BinOp("+", acc, v, line); return acc; }
                case "move_to_end":
                    {
                        Need(a, 1, 2, name, line);
                        bool last = PyOps.Truthy(Arg(a, kw, 1, "last", true));
                        object v;
                        if (!d.TryGet(a[0], out v)) throw KeyErr(a[0], line);
                        if (last) { d.Remove(a[0]); d.Set(a[0], v); }
                        else
                        {
                            var rest = d.Pairs().Where(p => !PyOps.Eq(p.Key, a[0])).ToList();
                            d.Clear(); d.Set(a[0], v); foreach (var p in rest) d.Set(p.Key, p.Value);
                        }
                        return null;
                    }
            }
            throw new PyError("AttributeError", "У словаря нет метода «" + name + "».", "'dict' object has no attribute '" + name + "'", line);
        }

        // ---------- set ----------
        PySet AsSet(object o, int line)
        {
            var s = o as PySet;
            if (s != null) return s;
            RequireIterable(o, line);
            return ToSet(o, line);
        }

        object SetMethod(PySet s, string name, List<object> a, Dictionary<string, object> kw, int line)
        {
            bool mutating = name == "add" || name == "discard" || name == "remove" || name == "pop" || name == "clear" || name.EndsWith("update");
            if (s.Frozen && mutating) throw new PyError("AttributeError", "frozenset нельзя изменять.", "'frozenset' object has no attribute '" + name + "'", line);
            switch (name)
            {
                case "add": Need(a, 1, 1, name, line); s.Add(a[0]); return null;
                case "discard": Need(a, 1, 1, name, line); s.Discard(a[0]); return null;
                case "remove":
                    Need(a, 1, 1, name, line);
                    if (!s.Discard(a[0])) throw new PyError("KeyError", "Элемента " + PyOps.Repr(a[0]) + " нет в множестве. Если его может не быть — используй discard().", line) { ExcArgs = new[] { a[0] }, PyMsg = PyOps.Repr(a[0]) };
                    return null;
                case "pop":
                    Need(a, 0, 0, name, line);
                    if (s.Count == 0) throw new PyError("KeyError", "pop() из пустого множества.", line) { PyMsg = "'pop from an empty set'", ExcArgs = new object[] { "pop from an empty set" } };
                    return s.Pop();
                case "clear": s.Clear(); return null;
                case "copy": return s.Copy();
                case "union":
                    {
                        var r = s.Copy(); r.Frozen = false;
                        foreach (var o in a)
                        {
                            if (o is PySet) r.Merge((PySet)o);
                            else if (o is PyDict) r.UpdateFromDict((PyDict)o);
                            else { RequireIterable(o, line); foreach (var x in Iterate(o, line)) r.Add(x); }
                        }
                        r.Frozen = s.Frozen; return r;
                    }
                case "update":
                    foreach (var o in a)
                    {
                        if (o is PySet) s.Merge((PySet)o);
                        else if (o is PyDict) s.UpdateFromDict((PyDict)o);
                        else { RequireIterable(o, line); foreach (var x in Iterate(o, line)) s.Add(x); }
                    }
                    return null;
                case "intersection":
                case "intersection_update":
                    {
                        PySet r = s;
                        if (name == "intersection" || a.Count > 1) { r = s.Copy(); r.Frozen = false; }   // set_intersection_multi начинает с копии
                        foreach (var o in a)
                        {
                            if (o is PySet) r = SetIntersection(r, (PySet)o);
                            else { RequireIterable(o, line); var nr = new PySet(); foreach (var x in Iterate(o, line)) if (r.Contains(x)) nr.Add(x); r = nr; }
                        }
                        if (name == "intersection") { r.Frozen = s.Frozen; return r; }
                        if (!ReferenceEquals(r, s)) s.TakeBody(r);
                        return null;
                    }
                case "difference":
                case "difference_update":
                    {
                        // как set_difference_multi / set_difference_update_internal в CPython
                        PySet r = null;
                        for (int ai = 0; ai < a.Count; ai++)
                        {
                            var o = a[ai];
                            if (name == "difference" && ai == 0)
                            {
                                if (o is PySet) { r = SetDifference(s, (PySet)o); continue; }
                                if (o is PyDict && (s.Count >> 2) <= ((PyDict)o).Count)
                                {
                                    r = new PySet(); var dd = (PyDict)o;
                                    foreach (var k in s.ToList()) if (!dd.ContainsKey(k)) r.Add(k);
                                    continue;
                                }
                                r = s.Copy(); r.Frozen = false;
                            }
                            else if (r == null) r = s;
                            DifferenceUpdate(r, o, line);
                        }
                        if (name == "difference") { if (r == null) { r = s.Copy(); } r.Frozen = s.Frozen; return r; }
                        return null;
                    }
                case "symmetric_difference":
                case "symmetric_difference_update":
                    {
                        Need(a, 1, 1, name, line);
                        var other = a[0] as PySet ?? AsSet(a[0], line);
                        if (name == "symmetric_difference")
                        {
                            var r = a[0] is PySet ? other.Copy() : other;   // из списка CPython сразу строит новое множество
                            r.Frozen = false;
                            foreach (var k in s.ToList()) if (!r.Discard(k)) r.Add(k);
                            r.Frozen = s.Frozen; return r;
                        }
                        foreach (var k in other.ToList()) if (!s.Discard(k)) s.Add(k);
                        return null;
                    }
                case "issubset": Need(a, 1, 1, name, line); return IsSubset(s, AsSet(a[0], line));
                case "issuperset": Need(a, 1, 1, name, line); return IsSubset(AsSet(a[0], line), s);
                case "isdisjoint":
                    Need(a, 1, 1, name, line);
                    foreach (var x in Iterate(a[0], line)) if (s.Contains(x)) return false;
                    return true;
            }
            throw new PyError("AttributeError", "У множества нет метода «" + name + "».", "'set' object has no attribute '" + name + "'", line);
        }

        // ---------- Форматирование: format(), f-строки, str.format, % ----------
        class Spec { public char Fill = ' ', Align = '\0', Sign = '\0', Type = '\0', Group = '\0'; public bool Alt, Zero; public int Width = -1, Prec = -1; }

        static Spec ParseSpec(string spec, int line)
        {
            var f = new Spec(); int i = 0, n = spec.Length;
            bool fillSpecified = false;
            if (n >= 2 && "<>=^".IndexOf(spec[1]) >= 0) { f.Fill = spec[0]; f.Align = spec[1]; i = 2; fillSpecified = true; }
            else if (n >= 1 && "<>=^".IndexOf(spec[0]) >= 0) { f.Align = spec[0]; i = 1; }
            if (i < n && "+- ".IndexOf(spec[i]) >= 0) f.Sign = spec[i++];
            if (i < n && spec[i] == 'z') i++;
            if (i < n && spec[i] == '#') { f.Alt = true; i++; }
            // флаг 0 (как в CPython): заполнитель '0', если он не задан явно; выравнивание '=' — только для чисел и если оно не задано
            if (!fillSpecified && i < n && spec[i] == '0') { f.Zero = true; f.Fill = '0'; i++; }
            int ws = i; while (i < n && char.IsDigit(spec[i])) i++;
            if (i > ws) f.Width = int.Parse(spec.Substring(ws, i - ws), Inv);
            if (i < n && (spec[i] == ',' || spec[i] == '_')) f.Group = spec[i++];
            if (i < n && spec[i] == '.')
            {
                i++; int ps = i; while (i < n && char.IsDigit(spec[i])) i++;
                if (i == ps) throw new PyError("ValueError", "В формате после точки нужна точность: {x:.2f}.", "Format specifier missing precision", line);
                f.Prec = int.Parse(spec.Substring(ps, i - ps), Inv);
            }
            if (i < n) f.Type = spec[i++];
            if (i < n) throw new PyError("ValueError", "Непонятный формат «" + spec + "» в f-строке. Примеры: {x:.2f}, {x:>5}, {x:,}.", "Invalid format specifier '" + spec + "' for object of type '...'", line);
            if (f.Group != '\0')
            {
                bool ok = "defgEG%F".IndexOf(f.Type) >= 0 || f.Type == '\0' || (f.Group == '_' && "boxX".IndexOf(f.Type) >= 0);
                if (!ok) throw new PyError("ValueError", "Разделитель тысяч «" + f.Group + "» нельзя использовать с форматом «" + f.Type + "».", "Cannot specify '" + f.Group + "' with '" + f.Type + "'.", line);
            }
            return f;
        }

        static string Pad(string body, Spec f, char defAlign)
        {
            char align = f.Align == '\0' ? defAlign : f.Align;
            int blen = PyStr.Len(body);
            if (f.Width < 0 || blen >= f.Width) return body;
            int pad = f.Width - blen;
            switch (align)
            {
                case '<': return body + new string(f.Fill, pad);
                case '^': { int left = pad / 2; return new string(f.Fill, left) + body + new string(f.Fill, pad - left); }
                case '=':
                    {
                        int k = 0;
                        while (k < body.Length && (body[k] == '-' || body[k] == '+' || body[k] == ' ')) k++;
                        if (body.Length > k + 1 && body[k] == '0' && "xXbBoO".IndexOf(body[k + 1]) >= 0) k += 2;
                        return body.Substring(0, k) + new string(f.Fill, pad) + body.Substring(k);
                    }
                default: return new string(f.Fill, pad) + body;
            }
        }

        static string Group(string digits, char sep, int every)
        {
            if (sep == '\0' || digits.Length <= every) return digits;
            var sb = new StringBuilder();
            int first = digits.Length % every; if (first == 0) first = every;
            sb.Append(digits, 0, first);
            for (int i = first; i < digits.Length; i += every) sb.Append(sep).Append(digits, i, every);
            return sb.ToString();
        }

        // Числа: знак + префикс + цифры (с группировкой и нулевой подложкой как в CPython)
        static string Assemble(bool neg, string prefix, string intDigits, string rest, Spec f, int every, char defAlign)
        {
            string sign = neg ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "";
            if (f.Align == '=' && f.Fill == '0' && f.Width > 0)   // нули-заполнители тоже группируются: 0,000,007
            {
                int minw = f.Width - sign.Length - prefix.Length - rest.Length;
                string g = Group(intDigits, f.Group, every);
                while (g.Length < minw) { intDigits = "0" + intDigits; g = Group(intDigits, f.Group, every); }
                return sign + prefix + g + rest;
            }
            return Pad(sign + prefix + Group(intDigits, f.Group, every) + rest, f, defAlign);
        }

        public string FormatValue(object v, string spec, int line)
        {
            var inst = v as PyInstance;
            if (inst != null)
            {
                if (HasUser(inst.Cls, "__format__")) { var r = CallDunder(inst, "__format__", line, spec ?? ""); return PyOps.Str(r); }
                if (string.IsNullOrEmpty(spec)) return PyOps.Str(v);
                throw new PyError("TypeError", "Формат «" + spec + "» нельзя применить к объекту " + inst.Cls.Name + ". Сначала преврати его в строку: {str(obj):" + spec + "}.",
                    "unsupported format string passed to " + inst.Cls.Name + ".__format__", line);
            }
            if (string.IsNullOrEmpty(spec)) return PyOps.Str(v);
            var f = ParseSpec(spec, line);
            if (v is string) return FormatStr((string)v, f, spec, line);
            if (f.Zero && f.Align == '\0' && (PyNum.IsNum(v))) f.Align = '=';
            if (PyNum.IsInt(v))
            {
                if ("eEfFgG%".IndexOf(f.Type) >= 0 && f.Type != '\0') return FormatFloat(PyNum.ToDouble(v), f, spec, line);
                return FormatInt(PyNum.Big(v), f, spec, line);
            }
            if (v is double) return FormatFloat((double)v, f, spec, line);
            throw new PyError("TypeError", "Формат «" + spec + "» здесь не подходит: значение — " + PyOps.TypeNameRu(v) + "." + (v == null ? " Значение равно None." : " Сначала преврати значение в строку: {str(x):" + spec + "}."),
                "unsupported format string passed to " + PyOps.TypeName(v) + ".__format__", line);
        }

        static string FormatStr(string s, Spec f, string spec, int line)
        {
            if (f.Type != '\0' && f.Type != 's')
                throw new PyError("ValueError", "Формат «" + f.Type + "» не подходит для строки. Если это число из input(), сначала преврати его: int(...) или float(...).",
                    "Unknown format code '" + f.Type + "' for object of type 'str'", line);
            if (f.Sign != '\0') throw new PyError("ValueError", "Знак (+/-) в формате не подходит для строки.", "Sign not allowed in string format specifier", line);
            if (f.Align == '=') throw new PyError("ValueError", "Выравнивание «=» не подходит для строки.", "'=' alignment not allowed in string format specifier", line);
            if (f.Group != '\0') throw new PyError("ValueError", "Разделитель тысяч не подходит для строки.", "Cannot specify '" + f.Group + "' with 's'.", line);
            if (f.Alt) throw new PyError("ValueError", "Флаг # не подходит для строки.", "Alternate form (#) not allowed in string format specifier", line);
            if (f.Prec >= 0 && s.Length > f.Prec) s = s.Substring(0, f.Prec);
            return Pad(s, f, '<');
        }

        static string FormatInt(BigInteger v, Spec f, string spec, int line)
        {
            if (f.Prec >= 0) throw new PyError("ValueError", "Точность (.2) нельзя указывать для целого числа с этим форматом. Для дробного вида используй {x:.2f}.", "Precision not allowed in integer format specifier", line);
            bool neg = v.Sign < 0; var a = BigInteger.Abs(v);
            string digits, prefix = ""; int every = 3;
            switch (f.Type)
            {
                case '\0': case 'd': case 'n': digits = a.ToString(Inv); break;
                case 'b': digits = BigToBase(a, 2); prefix = f.Alt ? "0b" : ""; every = 4; break;
                case 'o': digits = BigToBase(a, 8); prefix = f.Alt ? "0o" : ""; every = 4; break;
                case 'x': digits = BigToBase(a, 16); prefix = f.Alt ? "0x" : ""; every = 4; break;
                case 'X': digits = BigToBase(a, 16).ToUpperInvariant(); prefix = f.Alt ? "0X" : ""; every = 4; break;
                case 'c':
                    if (f.Sign != '\0') throw new PyError("ValueError", "Знак нельзя указывать с форматом 'c'.", "Sign not allowed with integer format specifier 'c'", line);
                    if (f.Alt) throw new PyError("ValueError", "Флаг # нельзя указывать с форматом 'c'.", "Alternate form (#) not allowed with integer format specifier 'c'", line);
                    if (v.Sign < 0 || v > 0x10FFFF) throw new PyError("OverflowError", "Формат 'c' работает с кодами символов от 0 до 0x10FFFF.", "%c arg not in range(0x110000)", line);
                    if (v >= 0xD800 && v <= 0xDFFF) return Pad(((char)(int)v).ToString(), f, '<');
                    return Pad(char.ConvertFromUtf32((int)v), f, '<');
                default:
                    throw new PyError("ValueError", "Формат «" + f.Type + "» не подходит для целого числа.", "Unknown format code '" + f.Type + "' for object of type 'int'", line);
            }
            if (f.Group == ',' && "bxXo".IndexOf(f.Type) >= 0 && f.Type != '\0')
                throw new PyError("ValueError", "Запятая-разделитель работает только для десятичных чисел.", "Cannot specify ',' with '" + f.Type + "'.", line);
            return Assemble(neg, prefix, digits, "", f, every, '>');
        }

        static string FormatFloat(double v, Spec f, string spec, int line)
        {
            string body;
            char t = f.Type;
            switch (t)
            {
                case 'f': case 'F': body = PyNum.FormatFixed(v, f.Prec < 0 ? 6 : f.Prec); if (t == 'F') body = body.ToUpperInvariant(); if (f.Alt && !body.Contains(".") && !double.IsInfinity(v) && !double.IsNaN(v)) body += "."; break;
                case 'e': case 'E': body = PyNum.FormatExp(v, f.Prec < 0 ? 6 : f.Prec, t == 'E', f.Alt); break;
                case 'g': case 'G': case 'n': body = PyNum.FormatGeneral(v, f.Prec < 0 ? 6 : f.Prec, t == 'G', f.Alt, false); break;
                case '%': body = PyNum.FormatFixed(v * 100, f.Prec < 0 ? 6 : f.Prec); if (f.Alt && !body.Contains(".") && !double.IsInfinity(v) && !double.IsNaN(v)) body += "."; body += "%"; break;
                case '\0':
                    body = f.Prec < 0 ? PyNum.FloatRepr(v) : PyNum.FormatGeneral(v, f.Prec, false, f.Alt, true);
                    if (f.Alt && f.Prec < 0 && !body.Contains(".") && body.Contains("e") && !double.IsInfinity(v) && !double.IsNaN(v)) body = body.Replace("e", ".e");   // format(1e22, '#') → '1.e+22'
                    break;
                default:
                    throw new PyError("ValueError", "Формат «" + t + "» не подходит для дробного числа. Для дробных используй f, например {x:.2f}.", "Unknown format code '" + t + "' for object of type 'float'", line);
            }
            bool neg = body.StartsWith("-");
            if (neg) body = body.Substring(1);
            int k = 0;
            while (k < body.Length && char.IsDigit(body[k])) k++;
            string intPart = body.Substring(0, k), rest = body.Substring(k);
            if (intPart.Length == 0) return Pad((neg ? "-" : f.Sign == '+' ? "+" : f.Sign == ' ' ? " " : "") + body, f, '>');
            return Assemble(neg, "", intPart, rest, f, 3, '>');
        }

        // str.format
        string StrFormat(string fmt, List<object> args, Dictionary<string, object> kw, int line)
        {
            var sb = new StringBuilder(); int auto = 0; bool manual = false, autoUsed = false;
            int i = 0, n = fmt.Length;
            while (i < n)
            {
                char c = fmt[i];
                if (c == '{' && i + 1 < n && fmt[i + 1] == '{') { sb.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < n && fmt[i + 1] == '}') { sb.Append('}'); i += 2; continue; }
                if (c == '}') throw new PyError("ValueError", "В строке формата одиночная «}». Чтобы вывести скобку, пиши }}.", "Single '}' encountered in format string", line);
                if (c != '{') { sb.Append(c); i++; continue; }
                int depth = 1, j = i + 1;
                while (j < n && depth > 0) { if (fmt[j] == '{') depth++; else if (fmt[j] == '}') depth--; if (depth > 0) j++; }
                if (j >= n) throw new PyError("ValueError", "В строке формата не закрыта фигурная скобка {.", "expected '}' before end of string", line);
                string field = fmt.Substring(i + 1, j - i - 1);
                i = j + 1;
                string spec = null; char conv = '\0';
                int colon = -1, bang = -1, br = 0;
                for (int k = 0; k < field.Length; k++)
                {
                    if (field[k] == '[') br++; else if (field[k] == ']') br--;
                    else if (br == 0 && field[k] == '!' && bang < 0 && colon < 0) bang = k;
                    else if (br == 0 && field[k] == ':' && colon < 0) { colon = k; break; }
                }
                if (colon >= 0) { spec = field.Substring(colon + 1); field = field.Substring(0, colon); }
                if (bang >= 0) { var cv = field.Substring(bang + 1); if (cv.Length != 1 || "rsa".IndexOf(cv[0]) < 0) throw new PyError("ValueError", "После ! в формате может быть r, s или a.", "Unknown conversion specifier " + cv, line); conv = cv[0]; field = field.Substring(0, bang); }
                int p = 0;
                while (p < field.Length && field[p] != '.' && field[p] != '[') p++;
                string head = field.Substring(0, p);
                object v;
                if (head.Length == 0)
                {
                    if (manual) throw new PyError("ValueError", "Нельзя смешивать {} и {0} в одной строке формата.", "cannot switch from manual field specification to automatic field numbering", line);
                    autoUsed = true;
                    if (auto >= args.Count) throw new PyError("IndexError", "В строке больше {} , чем аргументов у format().", "Replacement index " + auto + " out of range for positional args tuple", line);
                    v = args[auto++];
                }
                else if (head.All(char.IsDigit))
                {
                    if (autoUsed) throw new PyError("ValueError", "Нельзя смешивать {} и {0} в одной строке формата.", "cannot switch from automatic field numbering to manual field specification", line);
                    manual = true;
                    int idx = int.Parse(head, Inv);
                    if (idx >= args.Count) throw new PyError("IndexError", "Нет аргумента с номером " + idx + " для format().", "Replacement index " + idx + " out of range for positional args tuple", line);
                    v = args[idx];
                }
                else
                {
                    if (!kw.TryGetValue(head, out v)) throw new PyError("KeyError", "В format() не передан аргумент «" + head + "=».", line) { ExcArgs = new object[] { head }, PyMsg = PyOps.Repr(head) };
                }
                while (p < field.Length)
                {
                    if (field[p] == '.')
                    {
                        int q = p + 1; while (q < field.Length && field[q] != '.' && field[q] != '[') q++;
                        v = GetAttr(v, field.Substring(p + 1, q - p - 1), line); p = q;
                    }
                    else
                    {
                        int q = field.IndexOf(']', p);
                        if (q < 0) throw new PyError("ValueError", "Не закрыта квадратная скобка в формате.", "Missing ']' in format string", line);
                        string key = field.Substring(p + 1, q - p - 1);
                        object kobj = key.Length > 0 && key.All(char.IsDigit) ? (object)long.Parse(key, Inv) : key;
                        v = GetItem(v, kobj, line); p = q + 1;
                    }
                }
                if (conv == 'r') v = PyOps.Repr(v); else if (conv == 's') v = PyOps.Str(v); else if (conv == 'a') v = Ascii(PyOps.Repr(v));
                if (spec != null && spec.Contains("{")) spec = StrFormatNested(spec, args, kw, ref auto, line);
                sb.Append(FormatValue(v, spec, line));
            }
            return sb.ToString();
        }
        string StrFormatNested(string spec, List<object> args, Dictionary<string, object> kw, ref int auto, int line)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < spec.Length; i++)
            {
                if (spec[i] != '{') { sb.Append(spec[i]); continue; }
                int j = spec.IndexOf('}', i);
                string name = spec.Substring(i + 1, j - i - 1);
                object v;
                if (name.Length == 0) v = args[auto++];
                else if (name.All(char.IsDigit)) v = args[int.Parse(name, Inv)];
                else v = kw[name];
                sb.Append(PyOps.Str(v)); i = j;
            }
            return sb.ToString();
        }

        // Старое %-форматирование: "%s: %.2f" % (name, x)
        string PercentFormat(string fmt, object arg, int line)
        {
            List<object> args; PyDict map = null;
            if (arg is PyTuple) args = new List<object>(((PyTuple)arg).Items);
            else { args = new List<object> { arg }; map = arg as PyDict; }
            int ai = 0; var sb = new StringBuilder(); int i = 0, n = fmt.Length;
            Func<object> next = () =>
            {
                if (ai >= args.Count) throw new PyError("TypeError", "В строке больше %-подстановок, чем значений справа от %.", "not enough arguments for format string", line);
                return args[ai++];
            };
            bool usedMap = false;
            while (i < n)
            {
                char c = fmt[i];
                if (c != '%') { sb.Append(c); i++; continue; }
                i++;
                if (i >= n) throw new PyError("ValueError", "Строка формата оборвалась после %.", "incomplete format", line);
                object v = null; bool haveV = false;
                if (fmt[i] == '(')
                {
                    int j = fmt.IndexOf(')', i);
                    if (map == null) throw new PyError("TypeError", "%(имя)s требует словарь справа от %.", "format requires a mapping", line);
                    var key = fmt.Substring(i + 1, j - i - 1);
                    object mv;
                    if (!map.TryGet(key, out mv)) throw KeyErr(key, line);
                    v = mv; haveV = true; usedMap = true; i = j + 1;
                }
                var f = new Spec();
                bool left = false;
                while (i < n && "-+ 0#".IndexOf(fmt[i]) >= 0)
                {
                    switch (fmt[i]) { case '-': left = true; break; case '+': f.Sign = '+'; break; case ' ': if (f.Sign != '+') f.Sign = ' '; break; case '0': f.Zero = true; break; case '#': f.Alt = true; break; }
                    i++;
                }
                if (i < n && fmt[i] == '*') { f.Width = (int)PyOps.ToLong(next()); i++; }
                else { int ws = i; while (i < n && char.IsDigit(fmt[i])) i++; if (i > ws) f.Width = int.Parse(fmt.Substring(ws, i - ws), Inv); }
                if (i < n && fmt[i] == '.')
                {
                    i++;
                    if (i < n && fmt[i] == '*') { f.Prec = (int)PyOps.ToLong(next()); i++; }
                    else { int ps = i; while (i < n && char.IsDigit(fmt[i])) i++; f.Prec = i > ps ? int.Parse(fmt.Substring(ps, i - ps), Inv) : 0; }
                }
                while (i < n && "hlL".IndexOf(fmt[i]) >= 0) i++;
                if (i >= n) throw new PyError("ValueError", "Строка формата оборвалась после %.", "incomplete format", line);
                char t = fmt[i++];
                if (t == '%') { sb.Append('%'); continue; }
                if (!haveV) v = next();
                if (f.Width >= 0 && left) f.Align = '<';
                else if (f.Zero && "diouxXeEfFgG".IndexOf(t) >= 0) { f.Fill = '0'; f.Align = '='; }
                else f.Align = '>';
                string piece;
                switch (t)
                {
                    case 's': { var s = PyOps.Str(v); if (f.Prec >= 0 && s.Length > f.Prec) s = s.Substring(0, f.Prec); f.Sign = '\0'; piece = Pad(s, f, '>'); break; }
                    case 'r': case 'a': { var s = t == 'a' ? Ascii(PyOps.Repr(v)) : PyOps.Repr(v); if (f.Prec >= 0 && s.Length > f.Prec) s = s.Substring(0, f.Prec); piece = Pad(s, f, '>'); break; }
                    case 'd': case 'i': case 'u':
                        {
                            object iv = v;
                            if (v is double) iv = PyNum.Norm(PyNum.FloatToBig((double)v));
                            if (!PyNum.IsInt(iv)) throw new PyError("TypeError", "%d ждёт число, а получил " + PyOps.TypeNameRu(v) + ".", "%" + t + " format: a real number is required, not " + PyOps.TypeName(v), line);
                            var b = PyNum.Big(iv); string digits = BigInteger.Abs(b).ToString(Inv);
                            if (f.Prec > digits.Length) digits = new string('0', f.Prec - digits.Length) + digits;
                            piece = Assemble(b.Sign < 0, "", digits, "", f, 3, '>'); break;
                        }
                    case 'x': case 'X': case 'o':
                        {
                            if (!PyNum.IsInt(v)) throw new PyError("TypeError", "%" + t + " ждёт целое число.", "%" + t + " format: an integer is required, not " + PyOps.TypeName(v), line);
                            var b = PyNum.Big(v); string digits = BigToBase(BigInteger.Abs(b), t == 'o' ? 8 : 16);
                            if (t == 'X') digits = digits.ToUpperInvariant();
                            string prefix = f.Alt ? (t == 'o' ? "0o" : t == 'x' ? "0x" : "0X") : "";
                            piece = Assemble(b.Sign < 0, prefix, digits, "", f, 3, '>'); break;
                        }
                    case 'e': case 'E': case 'f': case 'F': case 'g': case 'G':
                        {
                            if (!PyNum.IsNum(v)) throw new PyError("TypeError", "%" + t + " ждёт число, а получил " + PyOps.TypeNameRu(v) + ".", "must be real number, not " + PyOps.TypeName(v), line);
                            f.Type = t; piece = FormatFloat(PyNum.ToDouble(v), f, "", line); break;
                        }
                    case 'c':
                        piece = Pad(v is string ? (string)v : char.ConvertFromUtf32((int)PyOps.ToLong(v)), f, '>'); break;
                    default:
                        throw new PyError("ValueError", "Неизвестный формат %" + t + ".", "unsupported format character '" + t + "' (0x" + ((int)t).ToString("x") + ")", line);
                }
                sb.Append(piece);
            }
            if (!usedMap && arg is PyTuple && ai < args.Count) throw new PyError("TypeError", "Значений справа от % больше, чем %-подстановок в строке.", "not all arguments converted during string formatting", line);
            if (!usedMap && !(arg is PyTuple) && map == null && ai == 0) throw new PyError("TypeError", "В строке нет %-подстановок, а значение справа от % передано.", "not all arguments converted during string formatting", line);
            return sb.ToString();
        }
    }
}

namespace Intern.Py
{
    // ================== Модули: math, json, collections, typing, string, copy, functools ==================
    public partial class Interp
    {
        const string ModulesList = "math, json, collections (Counter, defaultdict, OrderedDict), typing, string, copy, functools";

        PyModule ImportModule(string name, int line)
        {
            PyModule m;
            if (modules.TryGetValue(name, out m)) return m;
            switch (name)
            {
                case "math": m = MathModule(); break;
                case "json": m = JsonModule(); break;
                case "collections": m = CollectionsModule(); break;
                case "typing": m = TypingModule(); break;
                case "string": m = StringModule(); break;
                case "copy": m = CopyModule(); break;
                case "functools": m = FunctoolsModule(); break;
                default:
                    throw new PyError("ModuleNotFoundError", "Модуль «" + name + "» недоступен в игровом Python. Можно импортировать: " + ModulesList + ".",
                        "No module named '" + name + "'", line);
            }
            modules[name] = m;
            return m;
        }

        static void Fn(PyModule m, string name, BuiltinFn f) { m.Dict[name] = new PyBuiltin(name, f); }

        // ---------- math ----------
        double FArg(object o, string fn, int line)
        {
            if (o is double) return (double)o;
            if (PyNum.IsInt(o)) return PyNum.ToDouble(o);
            var inst = o as PyInstance;
            if (inst != null && HasUser(inst.Cls, "__float__")) return PyNum.ToDouble(CallDunder(inst, "__float__", line));
            throw new PyError("TypeError", "math." + fn + "() работает с числами, а получила " + PyOps.TypeNameRu(o) + "." + (o is string ? " Сначала преврати строку в число: float(...)." : ""),
                "must be real number, not " + PyOps.TypeName(o), line);
        }
        static PyError Domain(int line)
        {
            return new PyError("ValueError", "Математическая ошибка: функция не определена для такого значения (например, корень или логарифм отрицательного числа).", "math domain error", line);
        }
        static PyError Range(int line) { return new PyError("OverflowError", "Результат слишком большой для float.", "math range error", line); }
        BigInteger IArg(object o, string fn, int line)
        {
            if (PyNum.IsInt(o)) return PyNum.Big(o);
            throw new PyError("TypeError", "math." + fn + "() работает с целыми числами, а получила " + PyOps.TypeNameRu(o) + ".", "'" + PyOps.TypeName(o) + "' object cannot be interpreted as an integer", line);
        }

        object FloorCeil(object o, int mode, int line)
        {
            if (PyNum.IsInt(o)) return o is bool ? (object)(((bool)o) ? 1L : 0L) : o;
            var inst = o as PyInstance;
            string dn = mode == 0 ? "__floor__" : mode == 1 ? "__ceil__" : "__trunc__";
            if (inst != null && HasUser(inst.Cls, dn)) return CallDunder(inst, dn, line);
            double d = FArg(o, mode == 0 ? "floor" : mode == 1 ? "ceil" : "trunc", line);
            if (double.IsNaN(d)) throw new PyError("ValueError", "NaN нельзя округлить до целого.", "cannot convert float NaN to integer", line);
            if (double.IsInfinity(d)) throw new PyError("OverflowError", "Бесконечность нельзя округлить до целого.", "cannot convert float infinity to integer", line);
            return PyNum.Norm(PyNum.FloatToBig(mode == 0 ? Math.Floor(d) : mode == 1 ? Math.Ceiling(d) : Math.Truncate(d)));
        }

        double LogOf(object o, int line)
        {
            if (o is BigInteger)
            {
                var b = (BigInteger)o;
                if (b.Sign <= 0) throw Domain(line);
                return BigInteger.Log(b);
            }
            double x = FArg(o, "log", line);
            if (x <= 0) throw Domain(line);
            return Math.Log(x);
        }

        PyModule MathModule()
        {
            var m = new PyModule { Name = "math" };
            m.Dict["pi"] = Math.PI; m.Dict["e"] = Math.E; m.Dict["tau"] = 2 * Math.PI;
            m.Dict["inf"] = double.PositiveInfinity; m.Dict["nan"] = double.NaN;
            Fn(m, "sqrt", (a, kw, line) => { Need(a, 1, 1, "sqrt", line); var x = FArg(a[0], "sqrt", line); if (x < 0) throw Domain(line); return Math.Sqrt(x); });
            Fn(m, "isqrt", (a, kw, line) =>
            {
                Need(a, 1, 1, "isqrt", line); var n = IArg(a[0], "isqrt", line);
                if (n.Sign < 0) throw new PyError("ValueError", "isqrt() не определён для отрицательных чисел.", "isqrt() argument must be nonnegative", line);
                if (n < 2) return PyNum.Norm(n);
                var x = (BigInteger)Math.Sqrt((double)n);
                while (x * x > n) x -= 1;
                while ((x + 1) * (x + 1) <= n) x += 1;
                return PyNum.Norm(x);
            });
            Fn(m, "floor", (a, kw, line) => { Need(a, 1, 1, "floor", line); return FloorCeil(a[0], 0, line); });
            Fn(m, "ceil", (a, kw, line) => { Need(a, 1, 1, "ceil", line); return FloorCeil(a[0], 1, line); });
            Fn(m, "trunc", (a, kw, line) => { Need(a, 1, 1, "trunc", line); return FloorCeil(a[0], 2, line); });
            Fn(m, "fabs", (a, kw, line) => { Need(a, 1, 1, "fabs", line); return Math.Abs(FArg(a[0], "fabs", line)); });
            Fn(m, "pow", (a, kw, line) =>
            {
                Need(a, 2, 2, "pow", line);
                double x = FArg(a[0], "pow", line), y = FArg(a[1], "pow", line);
                if (x < 0 && y != Math.Floor(y) && !double.IsInfinity(y)) throw Domain(line);
                if (x == 0 && y < 0) throw Domain(line);
                double r = Math.Pow(x, y);
                if (double.IsInfinity(r) && !double.IsInfinity(x) && !double.IsInfinity(y)) throw Range(line);
                return r;
            });
            Fn(m, "exp", (a, kw, line) => { Need(a, 1, 1, "exp", line); var x = FArg(a[0], "exp", line); var r = Math.Exp(x); if (double.IsInfinity(r) && !double.IsInfinity(x)) throw Range(line); return r; });
            Fn(m, "log", (a, kw, line) =>
            {
                Need(a, 1, 2, "log", line);
                double r = LogOf(a[0], line);
                if (a.Count == 1) return r;
                double b = LogOf(a[1], line);
                if (b == 0) throw ZeroDiv("float division by zero", "Логарифм по основанию 1 не определён.", line);
                return r / b;
            });
            Fn(m, "log2", (a, kw, line) =>
            {
                Need(a, 1, 1, "log2", line);
                if (a[0] is long && (long)a[0] > 0 && ((long)a[0] & ((long)a[0] - 1)) == 0) return (double)PyNum.BitLength((long)a[0]) - 1;
                return LogOf(a[0], line) / Math.Log(2);
            });
            Fn(m, "log10", (a, kw, line) =>
            {
                Need(a, 1, 1, "log10", line);
                if (a[0] is BigInteger) return LogOf(a[0], line) / Math.Log(10);
                var x = FArg(a[0], "log10", line); if (x <= 0) throw Domain(line); return Math.Log10(x);
            });
            Fn(m, "log1p", (a, kw, line) => { Need(a, 1, 1, "log1p", line); var x = FArg(a[0], "log1p", line); if (x <= -1) throw Domain(line); return Math.Abs(x) < 1e-5 ? x - x * x / 2 + x * x * x / 3 : Math.Log(1 + x); });
            Func<string, Func<double, double>, bool> unary = (n, f) => { Fn(m, n, (a, kw, line) => { Need(a, 1, 1, n, line); var r = f(FArg(a[0], n, line)); if (double.IsNaN(r) && !double.IsNaN(FArg(a[0], n, line))) throw Domain(line); return r; }); return true; };
            unary("sin", Math.Sin); unary("cos", Math.Cos); unary("tan", Math.Tan);
            unary("asin", Math.Asin); unary("acos", Math.Acos); unary("atan", Math.Atan);
            unary("sinh", Math.Sinh); unary("cosh", Math.Cosh); unary("tanh", Math.Tanh);
            unary("degrees", x => x * (180.0 / Math.PI)); unary("radians", x => x * (Math.PI / 180.0));
            Fn(m, "atan2", (a, kw, line) => { Need(a, 2, 2, "atan2", line); return Math.Atan2(FArg(a[0], "atan2", line), FArg(a[1], "atan2", line)); });
            Fn(m, "hypot", (a, kw, line) =>
            {
                double mx = 0; var xs = a.Select(x => Math.Abs(FArg(x, "hypot", line))).ToList();
                foreach (var x in xs) if (double.IsInfinity(x)) return double.PositiveInfinity;
                foreach (var x in xs) mx = Math.Max(mx, x);
                if (mx == 0) return 0.0;
                double sum = 0; foreach (var x in xs) { var t = x / mx; sum += t * t; }
                return mx * Math.Sqrt(sum);
            });
            Fn(m, "dist", (a, kw, line) =>
            {
                Need(a, 2, 2, "dist", line);
                var p = ToList(a[0], line); var q = ToList(a[1], line);
                if (p.Count != q.Count) throw new PyError("ValueError", "Точки в dist() должны иметь одинаковое число координат.", "both points must have the same number of dimensions", line);
                double sum = 0; for (int i = 0; i < p.Count; i++) { var d = FArg(p[i], "dist", line) - FArg(q[i], "dist", line); sum += d * d; }
                return Math.Sqrt(sum);
            });
            Fn(m, "gcd", (a, kw, line) => { BigInteger g = 0; foreach (var x in a) g = BigInteger.GreatestCommonDivisor(g, IArg(x, "gcd", line)); return PyNum.Norm(g); });
            Fn(m, "lcm", (a, kw, line) =>
            {
                BigInteger l = 1;
                foreach (var x in a) { var v = BigInteger.Abs(IArg(x, "lcm", line)); if (v.IsZero) return 0L; l = l / BigInteger.GreatestCommonDivisor(l, v) * v; }
                return PyNum.Norm(l);
            });
            Fn(m, "factorial", (a, kw, line) =>
            {
                Need(a, 1, 1, "factorial", line);
                if (a[0] is double) throw new PyError("TypeError", "factorial() принимает только целые числа.", "'float' object cannot be interpreted as an integer", line);
                var n = IArg(a[0], "factorial", line);
                if (n.Sign < 0) throw new PyError("ValueError", "Факториал отрицательного числа не определён.", "factorial() not defined for negative values", line);
                if (n > 20000) throw new PyError("MemoryError", "Слишком большой факториал для игрового Python.", line);
                BigInteger r = 1; for (int i = 2; i <= (int)n; i++) r *= i;
                return PyNum.Norm(r);
            });
            Fn(m, "comb", (a, kw, line) =>
            {
                Need(a, 2, 2, "comb", line);
                var n = IArg(a[0], "comb", line); var k = IArg(a[1], "comb", line);
                if (n.Sign < 0 || k.Sign < 0) throw new PyError("ValueError", "comb() не определён для отрицательных чисел.", (n.Sign < 0 ? "n" : "k") + " must be a non-negative integer", line);
                if (k > n) return 0L;
                if (k > n - k) k = n - k;
                BigInteger r = 1; for (BigInteger i = 1; i <= k; i++) r = r * (n - k + i) / i;
                return PyNum.Norm(r);
            });
            Fn(m, "perm", (a, kw, line) =>
            {
                Need(a, 1, 2, "perm", line);
                var n = IArg(a[0], "perm", line); var k = a.Count > 1 && a[1] != null ? IArg(a[1], "perm", line) : n;
                if (n.Sign < 0 || k.Sign < 0) throw new PyError("ValueError", "perm() не определён для отрицательных чисел.", (n.Sign < 0 ? "n" : "k") + " must be a non-negative integer", line);
                if (k > n) return 0L;
                BigInteger r = 1; for (BigInteger i = n - k + 1; i <= n; i++) r *= i;
                return PyNum.Norm(r);
            });
            Fn(m, "isclose", (a, kw, line) =>
            {
                Need(a, 2, 2, "isclose", line); OnlyKw(kw, "isclose", line, "rel_tol", "abs_tol");
                double x = FArg(a[0], "isclose", line), y = FArg(a[1], "isclose", line);
                double rt = FArg(Kw(kw, "rel_tol", 1e-9), "isclose", line), at = FArg(Kw(kw, "abs_tol", 0.0), "isclose", line);
                if (rt < 0 || at < 0) throw new PyError("ValueError", "Допуски в isclose() не могут быть отрицательными.", "tolerances must be non-negative", line);
                if (x == y) return true;
                if (double.IsInfinity(x) || double.IsInfinity(y)) return false;
                double diff = Math.Abs(y - x);
                return diff <= Math.Abs(rt * y) || diff <= Math.Abs(rt * x) || diff <= at;
            });
            Fn(m, "isfinite", (a, kw, line) => { Need(a, 1, 1, "isfinite", line); var x = FArg(a[0], "isfinite", line); return !double.IsInfinity(x) && !double.IsNaN(x); });
            Fn(m, "isinf", (a, kw, line) => { Need(a, 1, 1, "isinf", line); return double.IsInfinity(FArg(a[0], "isinf", line)); });
            Fn(m, "isnan", (a, kw, line) => { Need(a, 1, 1, "isnan", line); return double.IsNaN(FArg(a[0], "isnan", line)); });
            Fn(m, "copysign", (a, kw, line) =>
            {
                Need(a, 2, 2, "copysign", line);
                double x = Math.Abs(FArg(a[0], "copysign", line)), y = FArg(a[1], "copysign", line);
                return (y < 0 || (y == 0 && BitConverter.DoubleToInt64Bits(y) < 0)) ? -x : x;
            });
            Fn(m, "fmod", (a, kw, line) =>
            {
                Need(a, 2, 2, "fmod", line);
                double x = FArg(a[0], "fmod", line), y = FArg(a[1], "fmod", line);
                if (y == 0 || double.IsInfinity(x)) throw Domain(line);
                return x % y;
            });
            Fn(m, "modf", (a, kw, line) =>
            {
                Need(a, 1, 1, "modf", line);
                double x = FArg(a[0], "modf", line), ip = Math.Truncate(x);
                return new PyTuple(new object[] { double.IsInfinity(x) ? (x > 0 ? 0.0 : -0.0) : x - ip, ip });
            });
            Fn(m, "prod", (a, kw, line) =>
            {
                Need(a, 1, 1, "prod", line); OnlyKw(kw, "prod", line, "start");
                object acc = Kw(kw, "start", 1L);
                foreach (var x in Iterate(a[0], line)) acc = BinOp("*", acc, x, line);
                return acc;
            });
            Fn(m, "fsum", (a, kw, line) =>
            {
                Need(a, 1, 1, "fsum", line);
                var parts = new List<double>();
                foreach (var x in Iterate(a[0], line)) parts.Add(FArg(x, "fsum", line));
                return FSum(parts, line);
            });
            return m;
        }

        static double FSum(List<double> xs, int line)
        {
            double special = 0; bool haveSpecial = false;
            foreach (var x in xs)
                if (double.IsNaN(x) || double.IsInfinity(x))
                {
                    if (!haveSpecial) { special = x; haveSpecial = true; }
                    else if (double.IsNaN(x) || special != x) special = double.NaN;
                }
            if (haveSpecial) return special;
            BigInteger sum = 0; int minE = int.MaxValue;
            var ms = new List<BigInteger>(); var es = new List<int>();
            foreach (var x in xs)
            {
                if (x == 0) continue;
                BigInteger mant; int e; PyNum.Decompose(Math.Abs(x), out mant, out e);
                if (x < 0) mant = -mant;
                ms.Add(mant); es.Add(e); minE = Math.Min(minE, e);
            }
            if (ms.Count == 0) return xs.Count > 0 && xs.All(v => BitConverter.DoubleToInt64Bits(v) < 0) ? -0.0 : 0.0;
            for (int i = 0; i < ms.Count; i++) sum += ms[i] << (es[i] - minE);
            bool neg = sum.Sign < 0; sum = BigInteger.Abs(sum);
            double r = minE >= 0 ? PyNum.BigToDouble(sum << minE) : PyNum.RatioToDouble(sum, BigInteger.One << -minE);
            if (double.IsInfinity(r)) throw new PyError("OverflowError", "Сумма слишком большая для float.", "intermediate overflow in fsum", line);
            return neg ? -r : r;
        }

        // ---------- json ----------
        PyModule JsonModule()
        {
            var m = new PyModule { Name = "json" };
            var de = MakeType("JSONDecodeError", excClasses["ValueError"], null, "json");
            de.IsException = true; excClasses["JSONDecodeError"] = de;
            m.Dict["JSONDecodeError"] = de;
            Fn(m, "dumps", (a, kw, line) =>
            {
                Need(a, 1, 1, "dumps", line);
                OnlyKw(kw, "dumps", line, "ensure_ascii", "indent", "separators", "sort_keys", "default", "skipkeys", "allow_nan", "check_circular", "cls");
                return JsonDumps(a[0], kw, line);
            });
            Fn(m, "loads", (a, kw, line) =>
            {
                Need(a, 1, 1, "loads", line);
                var s = a[0] as string;
                if (s == null) throw new PyError("TypeError", "json.loads() разбирает строку, а получил " + PyOps.TypeNameRu(a[0]) + ".", "the JSON object must be str, bytes or bytearray, not " + PyOps.TypeName(a[0]), line);
                return new JsonParser { S = s, Line = line }.Top();
            });
            BuiltinFn noFile = (a, kw, line) => { throw new PyError("OSError", "json.dump/json.load работают с файлами, а файлы в игровом Python недоступны. Используй json.dumps / json.loads со строками.", "file access is not available", line); };
            Fn(m, "dump", noFile); Fn(m, "load", noFile);
            return m;
        }

        class JsonOpts { public bool Ascii = true, Sort, AllowNan = true, SkipKeys; public string Indent, ItemSep = ", ", KeySep = ": "; public object Default; }

        string JsonDumps(object o, Dictionary<string, object> kw, int line)
        {
            var op = new JsonOpts
            {
                Ascii = PyOps.Truthy(Kw(kw, "ensure_ascii", true)),
                Sort = PyOps.Truthy(Kw(kw, "sort_keys", false)),
                AllowNan = PyOps.Truthy(Kw(kw, "allow_nan", true)),
                SkipKeys = PyOps.Truthy(Kw(kw, "skipkeys", false)),
                Default = Kw(kw, "default", null)
            };
            var ind = Kw(kw, "indent", null);
            if (ind is string) op.Indent = (string)ind;
            else if (ind != null && PyNum.IsInt(ind)) op.Indent = new string(' ', (int)Math.Max(0, PyOps.ToLong(ind)));
            if (op.Indent != null) op.ItemSep = ",";
            var sep = Kw(kw, "separators", null);
            if (sep != null) { var l = ToList(sep, line); op.ItemSep = PyOps.Str(l[0]); op.KeySep = PyOps.Str(l[1]); }
            var sb = new StringBuilder();
            JsonEnc(sb, o, 0, op, new HashSet<object>(ReferenceEqualityComparer.Instance), line);
            return sb.ToString();
        }

        void JsonNewline(StringBuilder sb, JsonOpts op, int level)
        {
            if (op.Indent == null) return;
            sb.Append('\n');
            for (int i = 0; i < level; i++) sb.Append(op.Indent);
        }

        string JsonFloat(double d, JsonOpts op, int line)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
            {
                if (!op.AllowNan) throw new PyError("ValueError", "Бесконечность и NaN нельзя записать в строгий JSON.", "Out of range float values are not JSON compliant", line);
                return double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity";
            }
            return PyNum.FloatRepr(d);
        }

        void JsonEnc(StringBuilder sb, object o, int level, JsonOpts op, HashSet<object> stack, int line)
        {
            if (o is PyList || o is PyTuple || o is PyDict || o is PyInstance)
            {
                PyDepth.Enter(" while encoding a JSON object");
                try { JsonEncInner(sb, o, level, op, stack, line); }
                finally { PyDepth.Leave(); }
                return;
            }
            JsonEncInner(sb, o, level, op, stack, line);
        }

        void JsonEncInner(StringBuilder sb, object o, int level, JsonOpts op, HashSet<object> stack, int line)
        {
            if (o == null) { sb.Append("null"); return; }
            if (o is bool) { sb.Append((bool)o ? "true" : "false"); return; }
            if (o is long || o is BigInteger) { sb.Append(PyOps.Repr(o)); return; }
            if (o is double) { sb.Append(JsonFloat((double)o, op, line)); return; }
            var s = o as string;
            if (s != null) { JsonStr(sb, s, op.Ascii); return; }
            if (o is PyList || o is PyTuple)
            {
                IList<object> items = o is PyList ? (IList<object>)((PyList)o).Items : ((PyTuple)o).Items;
                if (items.Count == 0) { sb.Append("[]"); return; }
                if (!stack.Add(o)) throw new PyError("ValueError", "В данных есть циклическая ссылка — список содержит сам себя.", "Circular reference detected", line);
                sb.Append('[');
                JsonNewline(sb, op, level + 1);
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) { sb.Append(op.ItemSep); JsonNewline(sb, op, level + 1); }
                    JsonEnc(sb, items[i], level + 1, op, stack, line);
                }
                JsonNewline(sb, op, level);
                sb.Append(']');
                stack.Remove(o);
                return;
            }
            var d = o as PyDict;
            if (d != null)
            {
                if (d.Count == 0) { sb.Append("{}"); return; }
                if (!stack.Add(o)) throw new PyError("ValueError", "В данных есть циклическая ссылка — словарь содержит сам себя.", "Circular reference detected", line);
                var pairs = d.Pairs().ToList();
                if (op.Sort)
                {
                    var idx = Enumerable.Range(0, pairs.Count).ToArray();
                    StableSortIdx(idx, (x, y) => Less(pairs[x].Key, pairs[y].Key, line));
                    pairs = idx.Select(i => pairs[i]).ToList();
                }
                sb.Append('{');
                JsonNewline(sb, op, level + 1);
                bool first = true;
                foreach (var p in pairs)
                {
                    string key;
                    var k = p.Key;
                    if (k is string) key = (string)k;
                    else if (k is bool) key = (bool)k ? "true" : "false";
                    else if (k == null) key = "null";
                    else if (k is long || k is BigInteger) key = PyOps.Repr(k);
                    else if (k is double) key = JsonFloat((double)k, op, line);
                    else if (op.SkipKeys) continue;
                    else throw new PyError("TypeError", "Ключи словаря в JSON должны быть строками или числами, а тут " + PyOps.TypeNameRu(k) + ".", "keys must be str, int, float, bool or None, not " + PyOps.TypeName(k), line);
                    if (!first) { sb.Append(op.ItemSep); JsonNewline(sb, op, level + 1); }
                    first = false;
                    JsonStr(sb, key, op.Ascii);
                    sb.Append(op.KeySep);
                    JsonEnc(sb, p.Value, level + 1, op, stack, line);
                }
                JsonNewline(sb, op, level);
                sb.Append('}');
                stack.Remove(o);
                return;
            }
            if (op.Default != null)
            {
                if (!stack.Add(o)) throw new PyError("ValueError", "Циклическая ссылка в данных.", "Circular reference detected", line);
                JsonEnc(sb, Call(op.Default, new List<object> { o }, NoKw, line), level, op, stack, line);
                stack.Remove(o);
                return;
            }
            throw new PyError("TypeError", "Значение типа " + PyOps.TypeName(o) + " нельзя превратить в JSON. Преврати его в словарь, список, строку или число" +
                (o is PySet ? " (множество — в список: list(s) или sorted(s))." : o is PyInstance ? " (объект — в словарь, например vars(obj) или свой метод to_dict())." : "."),
                "Object of type " + PyOps.TypeName(o) + " is not JSON serializable", line);
        }

        static void JsonStr(StringBuilder sb, string s, bool ascii)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20 || (ascii && c > 0x7e)) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        class JsonParser
        {
            public string S; public int Line; int i;
            PyError Err(string msg, int pos)
            {
                int lineno = 1, lastNl = -1;
                for (int k = 0; k < pos && k < S.Length; k++) if (S[k] == '\n') { lineno++; lastNl = k; }
                int col = pos - lastNl;
                var e = new PyError("JSONDecodeError", "Строка — не корректный JSON: " + msg + " (строка " + lineno + ", символ " + col + "). Проверь кавычки (в JSON только двойные), запятые и скобки.",
                    msg + ": line " + lineno + " column " + col + " (char " + pos + ")", Line);
                return e;
            }
            void Ws() { while (i < S.Length && (S[i] == ' ' || S[i] == '\t' || S[i] == '\n' || S[i] == '\r')) i++; }
            public object Top()
            {
                Ws();
                var v = Value();
                Ws();
                if (i != S.Length) throw Err("Extra data", i);
                return v;
            }
            bool Lit(string w) { if (string.CompareOrdinal(S, i, w, 0, w.Length) == 0) { i += w.Length; return true; } return false; }
            object Value()
            {
                if (i >= S.Length) throw Err("Expecting value", i);
                char c = S[i];
                if (c == '{') { PyDepth.Enter(" while decoding a JSON object from a unicode string"); try { return Obj(); } finally { PyDepth.Leave(); } }
                if (c == '[') { PyDepth.Enter(" while decoding a JSON array from a unicode string"); try { return Arr(); } finally { PyDepth.Leave(); } }
                if (c == '"') return Str();
                if (Lit("true")) return true;
                if (Lit("false")) return false;
                if (Lit("null")) return null;
                if (Lit("NaN")) return double.NaN;
                if (Lit("Infinity")) return double.PositiveInfinity;
                if (Lit("-Infinity")) return double.NegativeInfinity;
                if (c == '-' || (c >= '0' && c <= '9')) return Num();
                throw Err("Expecting value", i);
            }
            object Num()
            {
                int st = i;
                if (S[i] == '-') i++;
                if (i >= S.Length || !char.IsDigit(S[i])) throw Err("Expecting value", st);
                if (S[i] == '0') i++; else while (i < S.Length && S[i] >= '0' && S[i] <= '9') i++;
                bool flt = false;
                if (i + 1 < S.Length && S[i] == '.' && S[i + 1] >= '0' && S[i + 1] <= '9') { flt = true; i++; while (i < S.Length && S[i] >= '0' && S[i] <= '9') i++; }
                if (i < S.Length && (S[i] == 'e' || S[i] == 'E'))
                {
                    int save = i; i++;
                    if (i < S.Length && (S[i] == '+' || S[i] == '-')) i++;
                    if (i < S.Length && S[i] >= '0' && S[i] <= '9') { flt = true; while (i < S.Length && S[i] >= '0' && S[i] <= '9') i++; }
                    else i = save;
                }
                var txt = S.Substring(st, i - st);
                if (flt) { double d; PyNum.TryParseFloat(txt, out d); return d; }
                return PyNum.Norm(BigInteger.Parse(txt, CultureInfo.InvariantCulture));
            }
            string Str()
            {
                int st = i; i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= S.Length) throw Err("Unterminated string starting at", st);
                    char c = S[i];
                    if (c == '"') { i++; return sb.ToString(); }
                    if (c < 0x20) throw Err("Invalid control character at", i);
                    if (c != '\\') { sb.Append(c); i++; continue; }
                    if (i + 1 >= S.Length) throw Err("Unterminated string starting at", st);
                    char e = S[i + 1];
                    switch (e)
                    {
                        case '"': sb.Append('"'); i += 2; break;
                        case '\\': sb.Append('\\'); i += 2; break;
                        case '/': sb.Append('/'); i += 2; break;
                        case 'b': sb.Append('\b'); i += 2; break;
                        case 'f': sb.Append('\f'); i += 2; break;
                        case 'n': sb.Append('\n'); i += 2; break;
                        case 'r': sb.Append('\r'); i += 2; break;
                        case 't': sb.Append('\t'); i += 2; break;
                        case 'u':
                            {
                                int code;
                                if (i + 6 > S.Length || !int.TryParse(S.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Err("Invalid \\uXXXX escape", i + 1);
                                sb.Append((char)code); i += 6; break;
                            }
                        default: throw Err("Invalid \\escape", i);
                    }
                }
            }
            object Obj()
            {
                i++; Ws();
                var d = new PyDict();
                if (i < S.Length && S[i] == '}') { i++; return d; }
                while (true)
                {
                    if (i >= S.Length || S[i] != '"') throw Err("Expecting property name enclosed in double quotes", i);
                    var k = Str(); Ws();
                    if (i >= S.Length || S[i] != ':') throw Err("Expecting ':' delimiter", i);
                    i++; Ws();
                    d.Set(k, Value()); Ws();
                    if (i < S.Length && S[i] == ',') { i++; Ws(); continue; }
                    if (i < S.Length && S[i] == '}') { i++; return d; }
                    throw Err("Expecting ',' delimiter", i);
                }
            }
            object Arr()
            {
                i++; Ws();
                var l = new PyList();
                if (i < S.Length && S[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Items.Add(Value()); Ws();
                    if (i < S.Length && S[i] == ',') { i++; Ws(); continue; }
                    if (i < S.Length && S[i] == ']') { i++; return l; }
                    throw Err("Expecting ',' delimiter", i);
                }
            }
        }

        // ---------- collections, typing, string ----------
        PyModule CollectionsModule()
        {
            var m = new PyModule { Name = "collections" };
            m.Dict["Counter"] = CounterClass;
            m.Dict["defaultdict"] = DefaultDictClass;
            m.Dict["OrderedDict"] = OrderedDictClass;
            return m;
        }

        PyModule TypingModule()
        {
            var m = new PyModule { Name = "typing" };
            foreach (var n in "Any List Dict Set FrozenSet Tuple Optional Union Callable Iterable Iterator Sequence Mapping MutableMapping Type Generic Protocol Literal Final ClassVar NoReturn Collection Deque DefaultDict Hashable Sized Awaitable Coroutine Generator".Split(' '))
                m.Dict[n] = MakeType(n, ObjectClass, NoCtor(n), "typing");
            m.Dict["TYPE_CHECKING"] = false;
            Fn(m, "TypeVar", (a, kw, line) => MakeType(a.Count > 0 ? PyOps.Str(a[0]) : "T", ObjectClass, NoCtor("TypeVar"), "typing"));
            Fn(m, "cast", (a, kw, line) => { Need(a, 2, 2, "cast", line); return a[1]; });
            Fn(m, "overload", (a, kw, line) => a[0]);
            Fn(m, "final", (a, kw, line) => a[0]);
            return m;
        }

        PyModule StringModule()
        {
            var m = new PyModule { Name = "string" };
            m.Dict["ascii_lowercase"] = "abcdefghijklmnopqrstuvwxyz";
            m.Dict["ascii_uppercase"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            m.Dict["ascii_letters"] = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
            m.Dict["digits"] = "0123456789";
            m.Dict["hexdigits"] = "0123456789abcdefABCDEF";
            m.Dict["octdigits"] = "01234567";
            m.Dict["punctuation"] = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
            m.Dict["whitespace"] = " \t\n\r\x0b\x0c";
            m.Dict["printable"] = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r\x0b\x0c";
            Fn(m, "capwords", (a, kw, line) =>
            {
                Need(a, 1, 2, "capwords", line);
                var parts = Split(PyOps.Str(a[0]), a.Count > 1 ? (string)a[1] : null, -1);
                return string.Join(a.Count > 1 ? (string)a[1] : " ", parts.Select(p => (string)StrMethod((string)p, "capitalize", new List<object>(), NoKw, line)).ToArray());
            });
            return m;
        }

        // ---------- copy ----------
        PyModule CopyModule()
        {
            var m = new PyModule { Name = "copy" };
            Fn(m, "copy", (a, kw, line) => { Need(a, 1, 1, "copy", line); return ShallowCopy(a[0]); });
            Fn(m, "deepcopy", (a, kw, line) => { Need(a, 1, 1, "deepcopy", line); return DeepCopy(a[0], new Dictionary<object, object>(ReferenceEqualityComparer.Instance)); });
            return m;
        }

        object ShallowCopy(object o)
        {
            if (o is PyList) return new PyList(((PyList)o).Items);
            if (o is PyDict) return ((PyDict)o).CopyAs(((PyDict)o).Kind);
            if (o is PySet) return ((PySet)o).Copy();
            var inst = o as PyInstance;
            if (inst != null)
            {
                var c = new PyInstance(inst.Cls);
                foreach (var kv in inst.Attrs) c.Attrs[kv.Key] = kv.Value;
                c.Order.AddRange(inst.Order);
                return c;
            }
            return o;
        }

        object DeepCopy(object o, Dictionary<object, object> memo)
        {
            PyDepth.Enter(" while calling a Python object");
            try { return DeepCopyInner(o, memo); }
            finally { PyDepth.Leave(); }
        }

        object DeepCopyInner(object o, Dictionary<object, object> memo)
        {
            if (o == null || o is string || o is long || o is double || o is bool || o is BigInteger || o is PyFunction || o is PyClass || o is PyBuiltin || o is PyModule || o is PyRange) return o;
            object done;
            if (memo.TryGetValue(o, out done)) return done;
            var l = o as PyList;
            if (l != null) { var r = new PyList(); memo[o] = r; foreach (var x in l.Items) r.Items.Add(DeepCopy(x, memo)); return r; }
            var t = o as PyTuple;
            if (t != null) { var items = t.Items.Select(x => DeepCopy(x, memo)).ToArray(); var r = new PyTuple(items); memo[o] = r; return r; }
            var d = o as PyDict;
            if (d != null) { var r = new PyDict { Kind = d.Kind, Factory = d.Factory }; memo[o] = r; foreach (var p in d.Pairs()) r.Set(DeepCopy(p.Key, memo), DeepCopy(p.Value, memo)); return r; }
            var s = o as PySet;
            if (s != null) { var r = new PySet { Frozen = s.Frozen }; memo[o] = r; foreach (var x in s.ToList()) r.Add(DeepCopy(x, memo)); return r; }
            var inst = o as PyInstance;
            if (inst != null)
            {
                var c = new PyInstance(inst.Cls); memo[o] = c;
                foreach (var k in inst.Attrs.Keys.ToList()) c.Attrs[k] = DeepCopy(inst.Attrs[k], memo);
                c.Order.AddRange(inst.Order);
                return c;
            }
            return o;
        }

        // ---------- functools ----------
        PyModule FunctoolsModule()
        {
            var m = new PyModule { Name = "functools" };
            Fn(m, "reduce", (a, kw, line) =>
            {
                Need(a, 2, 3, "reduce", line);
                var it = Iterate(a[1], line).GetEnumerator();
                object acc;
                if (a.Count == 3) acc = a[2];
                else if (it.MoveNext()) acc = it.Current;
                else throw new PyError("TypeError", "reduce() от пустой коллекции без начального значения.", "reduce() of empty iterable with no initial value", line);
                while (it.MoveNext()) acc = Call(a[0], new List<object> { acc, it.Current }, NoKw, line);
                return acc;
            });
            BuiltinFn makeCache = (a, kw, line) => MakeCached(a[0], line);
            Fn(m, "cache", makeCache);
            Fn(m, "lru_cache", (a, kw, line) =>
            {
                if (a.Count == 1 && IsCallable(a[0]) && !(a[0] is PyClass)) return MakeCached(a[0], line);
                return new PyBuiltin("lru_cache", makeCache);
            });
            Fn(m, "wraps", (a, kw, line) =>
            {
                Need(a, 1, 1, "wraps", line);
                var orig = a[0];
                return new PyBuiltin("wraps", (b, k2, l2) =>
                {
                    var w = b[0] as PyFunction; var of = orig as PyFunction;
                    if (w != null && of != null) w.Name = of.Name;
                    return b[0];
                });
            });
            Fn(m, "partial", (a, kw, line) =>
            {
                if (a.Count == 0) throw new PyError("TypeError", "partial() нужна функция.", "type 'partial' takes at least one argument", line);
                var f = a[0]; var pre = a.Skip(1).ToList(); var prekw = new Dictionary<string, object>(kw);
                return new PyBuiltin("partial", (b, k2, l2) =>
                {
                    var args = new List<object>(pre); args.AddRange(b);
                    var kws = new Dictionary<string, object>(prekw); foreach (var kv in k2) kws[kv.Key] = kv.Value;
                    return Call(f, args, kws, l2);
                });
            });
            Fn(m, "cmp_to_key", (a, kw, line) =>
            {
                Need(a, 1, 1, "cmp_to_key", line);
                var cmp = a[0];
                var k = new PyClass { Name = "functools.KeyWrapper" };
                k.Mro.Add(k); k.Mro.Add(ObjectClass); k.Bases.Add(ObjectClass);
                Func<string, Func<long, bool>, bool> op = (n, test) =>
                {
                    k.Set(n, new PyBuiltin(n, (b, k2, l2) =>
                    {
                        var x = ((PyInstance)b[0]).Attrs["obj"]; var y = ((PyInstance)b[1]).Attrs["obj"];
                        var r = Call(cmp, new List<object> { x, y }, NoKw, l2);
                        return test(PyOps.ToLong(PyNum.IsInt(r) ? r : (object)(long)Math.Sign(PyNum.ToDouble(r))));
                    }));
                    return true;
                };
                op("__lt__", c => c < 0); op("__gt__", c => c > 0); op("__le__", c => c <= 0); op("__ge__", c => c >= 0); op("__eq__", c => c == 0);
                return new PyBuiltin("K", (b, k2, l2) => { var inst = new PyInstance(k); inst.Attrs["obj"] = b[0]; return inst; });
            });
            Fn(m, "total_ordering", (a, kw, line) =>
            {
                Need(a, 1, 1, "total_ordering", line);
                var cls = a[0] as PyClass;
                if (cls == null) return a[0];
                var lt = HasUser(cls, "__lt__"); var le = HasUser(cls, "__le__"); var gt = HasUser(cls, "__gt__"); var ge = HasUser(cls, "__ge__");
                Func<object, object, int, bool> less = null;
                if (lt) less = (x, y, l) => PyOps.Truthy(CallDunder((PyInstance)x, "__lt__", l, y));
                else if (gt) less = (x, y, l) => PyOps.Truthy(CallDunder((PyInstance)y, "__gt__", l, x));
                else if (le) less = (x, y, l) => PyOps.Truthy(CallDunder((PyInstance)x, "__le__", l, y)) && !PyOps.Eq(x, y);
                else if (ge) less = (x, y, l) => !PyOps.Truthy(CallDunder((PyInstance)x, "__ge__", l, y));
                if (less == null) throw new PyError("ValueError", "total_ordering требует хотя бы один из методов __lt__, __le__, __gt__, __ge__.", "must define at least one ordering operation: < > <= >=", line);
                if (!lt) cls.Set("__lt__", new PyBuiltin("__lt__", (b, k2, l) => less(b[0], b[1], l)));
                if (!gt) cls.Set("__gt__", new PyBuiltin("__gt__", (b, k2, l) => less(b[1], b[0], l)));
                if (!le) cls.Set("__le__", new PyBuiltin("__le__", (b, k2, l) => less(b[0], b[1], l) || PyOps.Eq(b[0], b[1])));
                if (!ge) cls.Set("__ge__", new PyBuiltin("__ge__", (b, k2, l) => !less(b[0], b[1], l)));
                return cls;
            });
            return m;
        }

        object MakeCached(object f, int line)
        {
            if (!IsCallable(f)) throw new PyError("TypeError", "lru_cache/cache применяются к функции.", "Expected first argument to be callable", line);
            var cache = new PyDict();
            var name = f is PyFunction ? ((PyFunction)f).Name : "cached";
            return new PyBuiltin(name, (a, kw, l) =>
            {
                object key = kw.Count == 0 ? (object)new PyTuple(a.ToArray()) : new PyTuple(a.Concat(kw.SelectMany(p => new object[] { p.Key, p.Value })).ToArray());
                object v;
                if (cache.TryGet(key, out v)) return v;
                v = Call(f, a, kw, l);
                cache.Set(key, v);
                return v;
            });
        }
    }
}
