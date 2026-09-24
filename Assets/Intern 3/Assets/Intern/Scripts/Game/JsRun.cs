// JsRun — запуск и проверка JavaScript-кода игрока (через Jint, Plugins/Jint).
//
//   JsCheckResult r = JsRun.Check(code, "cartTotal", testCases);   // testCases: List<object> из MiniJson (content.test_cases)
//   string text     = JsRun.Run(code);                              // кнопка «Запустить»: вывод console.* + ошибка
//   JsRunOutput o   = JsRun.RunFull(code);                          // то же, но по полям (строки, ошибка, строка ошибки)
//   JsError e       = JsRun.SyntaxCheck(code);                      // быстрая проверка синтаксиса (null — всё ок)
//   var job = JsRun.StartCheck(...); ... if (job.IsDone) use(job.Result);   // без блокировки кадра (из Update)
//   JsRun.Prewarm();                                                // при старте: прогреть JIT в фоне
// Check/RunFull блокируют вызывающий поток до конца проверки (обычно миллисекунды, в худшем случае —
// TotalTimeoutSeconds + 5 с), поэтому из Update лучше StartCheck/StartRun.
//
// Правила проверки (как у валидатора контента, spec/validate.py + node):
//   • test = { "input": …, "expected": … }; если input — массив, это список аргументов (fn(...input)), иначе один аргумент;
//   • аргументы передаются как свежие JSON-значения (JSON.parse) в каждом тесте;
//   • если функция вернула thenable (Promise, async), результат дожидается (then-цепочки, async/await, таймеры);
//   • результат превращается в JSON (как JSON.stringify; undefined → null) и глубоко сравнивается с expected:
//     числа — с допуском 1e-6 (целые и дробные сравниваются как числа), bool ≠ число, у объектов важен набор ключей.
// Среда как в Node: console.* (формат util.inspect), queueMicrotask, setTimeout/setInterval/setImmediate (+clear*),
// structuredClone, performance.now. Порядок микрозадач — как в Node (спецификация ES; await — 1 тик). Таймеры идут
// в виртуальном времени: ожидание не тратит реальное время, порядок срабатывания — как в Node.
// Безопасность: каждый тест — в свежем движке; лимиты времени, числа операторов, глубины рекурсии, стека, роста памяти
// и размера массивов (JsLimits). Jint работает в отдельном потоке со своим стеком (глубокая рекурсия не роняет игру),
// вызывающий поток ждёт его с жёстким таймаутом. Если потоки недоступны (WebGL) — выполняется в вызывающем потоке.
// Движок живёт только внутри своего потока, наружу отдаются готовые строки/объекты. Unity API не используется.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Jint;
using Jint.Constraints;
using Jint.Native;
using Jint.Runtime;
using Jint.Runtime.Interop;

namespace Intern.Game
{
    /// <summary>Лимиты выполнения JS. Значения по умолчанию подобраны для задач игры.</summary>
    public sealed class JsLimits
    {
        public double TimeoutSeconds = 2.0;          // один тест (загрузка кода + вызов + ожидание Promise) или один «Запустить»
        public double TotalTimeoutSeconds = 6.0;     // вся проверка; когда кончится — оставшиеся тесты пропускаются
        public int MaxStatements = 10000000;         // операторов за один заход в движок (0 — без лимита)
        public int MaxRecursion = 3000;              // глубина вложенных вызовов JS (0 — без лимита; стек всё равно под защитой)
        public long MaxMemoryBytes = 256L * 1024 * 1024; // на сколько может вырасти куча за запуск (0 — без лимита)
        public uint MaxArraySize = 5000000;          // максимальная длина массива
        public int MaxOutputChars = 20000;           // сколько символов console.* сохраняем, дальше обрезаем
        public double MaxTimerMs = 60000;            // горизонт виртуального времени таймеров (бесконечный setInterval)
        public int StackBudgetBytes = 12 * 1024 * 1024;  // сколько стека может занять JS (≈3000 уровней рекурсии)
        public int ThreadStackBytes = 64 * 1024 * 1024;  // стек рабочего потока (резерв; запас сверх бюджета — для рекурсии
                                                         // внутри встроенных функций Jint); 0 — выполнять в вызывающем потоке
        public int InlineStackBudgetBytes = 256 * 1024;  // бюджет стека JS, если потока нет (главный поток Unity)
    }

    /// <summary>Ошибка в коде игрока (или сработавший лимит), уже с русским текстом.</summary>
    public sealed class JsError
    {
        public string Type;     // SyntaxError, TypeError, ReferenceError, … или Timeout, StepLimit, RecursionLimit,
                                // MemoryLimit, TimerLimit, PromisePending, NoFunction, Hang, Internal
        public string Message;  // сообщение движка (англ.) или русское описание лимита
        public int Line;        // строка в коде игрока (с 1), 0 — неизвестно
        public int Column;      // столбец (с 1), 0 — неизвестно
        public string Text;     // для игрока: «SyntaxError в строке 3: …», «TypeError: … (строка 5)»
        public string Hint;     // подсказка по-русски или null
        public override string ToString() { return Hint == null ? Text : Text + "\n" + Hint; }
    }

    public sealed class JsTestResult
    {
        public int Index;           // номер теста с 0
        public bool Passed;
        public bool Skipped;        // не запускался: закончилось общее время проверки
        public string Call;         // как вызывали: cartTotal(["lamp"], {"lamp":990})
        public string InputText;    // только аргументы через запятую (JSON)
        public string Expected;     // JSON
        public string Actual;       // JSON результата; null, если функция упала
        public string Note;         // чем отличается результат (по-русски), null — если нечего сказать
        public JsError Error;       // ошибка выполнения или сработавший лимит
        public string Log;          // что напечатали console.* во время теста
        public double Ms;
    }

    public sealed class JsCheckResult
    {
        public List<JsTestResult> Tests = new List<JsTestResult>();
        public int PassedCount;
        public bool AllPassed;
        public JsError LoadError;   // синтаксис / ошибка при загрузке кода / нет функции — тогда все тесты не пройдены
        public double TotalMs;
    }

    public sealed class JsLogLine
    {
        public bool IsError;        // console.error / console.warn / console.assert / console.trace
        public string Text;
    }

    public sealed class JsRunOutput
    {
        public string Output = "";  // весь вывод console.* (строки через \n)
        public List<JsLogLine> Lines = new List<JsLogLine>();
        public bool Truncated;      // вывод обрезан по JsLimits.MaxOutputChars
        public JsError Error;
        public double Ms;
        public bool Ok { get { return Error == null; } }

        /// <summary>Готовый текст для консоли IDE: вывод, затем ошибка (если была).</summary>
        public string Text
        {
            get
            {
                if (Error == null) return Output;
                string err = "Ошибка: " + Error.ToString();
                return Output.Length == 0 ? err : Output + (Output.EndsWith("\n") ? "" : "\n") + err;
            }
        }
    }

    /// <summary>Фоновая проверка/запуск: опрашивай IsDone из Update, потом читай Result.</summary>
    public sealed class JsJob<TResult> where TResult : class
    {
        volatile bool done;
        TResult result;
        public bool IsDone { get { return done; } }
        public TResult Result { get { return done ? result : null; } }
        internal void Finish(TResult r) { result = r; done = true; }
    }

    public static class JsRun
    {
        /// <summary>Имя «файла» кода игрока в сообщениях движка.</summary>
        public const string SourceName = "code.js";
        const string RunnerSource = "<runner>";
        const int MaxLoopSteps = 1000000;      // колбэков таймеров за один запуск

        static readonly Regex EntryRe = new Regex(@"^[A-Za-z_$][A-Za-z0-9_$]*(\.[A-Za-z_$][A-Za-z0-9_$]*)*$");
        static readonly Regex StackPosRe = new Regex(Regex.Escape(SourceName) + @":(\d+):(\d+)");

        // ======================= публичное API =======================

        /// <summary>Проверить функцию entry по тестам (элементы — Dictionary с "input" и "expected").</summary>
        public static JsCheckResult Check(string code, string entry, IList<object> testCases, JsLimits limits = null)
        {
            var lim = limits ?? new JsLimits();
            int hardMs = (int)((lim.TotalTimeoutSeconds + 5) * 1000);
            return OnWorker(() => CheckCore(code, entry, testCases, lim), lim, hardMs,
                () => HangCheck(entry, testCases, Hang()), e => HangCheck(entry, testCases, InternalError(e)));
        }

        /// <summary>То же, тесты в виде JSON-строки (массив объектов {input, expected}).</summary>
        public static JsCheckResult Check(string code, string entry, string testCasesJson, JsLimits limits = null)
        {
            return Check(code, entry, ParseTests(testCasesJson), limits);
        }

        /// <summary>Кнопка «Запустить»: выполняет код целиком, возвращает вывод console.* и ошибку (если была).</summary>
        public static string Run(string code)
        {
            return RunFull(code, null).Text;
        }

        public static JsRunOutput RunFull(string code, JsLimits limits = null)
        {
            var lim = limits ?? new JsLimits();
            int hardMs = (int)((lim.TimeoutSeconds + 5) * 1000);
            return OnWorker(() => RunCore(code, lim), lim, hardMs,
                () => new JsRunOutput { Error = Hang() }, e => new JsRunOutput { Error = InternalError(e) });
        }

        /// <summary>Проверка в фоне — кадр игры не блокируется.</summary>
        public static JsJob<JsCheckResult> StartCheck(string code, string entry, IList<object> testCases, JsLimits limits = null)
        {
            var lim = limits ?? new JsLimits();
            return StartJob(() => CheckCore(code, entry, testCases, lim), lim, e => HangCheck(entry, testCases, InternalError(e)));
        }

        public static JsJob<JsRunOutput> StartRun(string code, JsLimits limits = null)
        {
            var lim = limits ?? new JsLimits();
            return StartJob(() => RunCore(code, lim), lim, e => new JsRunOutput { Error = InternalError(e) });
        }

        /// <summary>Прогрев в фоне (JIT-компиляция Jint): вызови при старте игры, чтобы первая проверка не ждала ~0,5 с.</summary>
        public static void Prewarm()
        {
            StartCheck("function f(a) { console.log(a); return Promise.resolve([a, { b: a }]); }", "f",
                new List<object> { new Dictionary<string, object> { { "input", new List<object> { 1L } }, { "expected", null } } });
        }

        /// <summary>Только синтаксис (без выполнения). null — ошибок нет.</summary>
        public static JsError SyntaxCheck(string code)
        {
            try
            {
                Engine.PrepareScript(code ?? "", SourceName);
                return null;
            }
            catch (Exception e)
            {
                var prep = e as ScriptPreparationException;
                if (e is InsufficientExecutionStackException || (prep != null && !(prep.InnerException is Acornima.ParseErrorException)))
                    return Err("SyntaxError", e.Message, "Код слишком глубоко вложен (скобки, операции, функции) — его не получается разобрать.",
                        "Упрости выражение: раздели его на несколько строк и промежуточных переменных.");
                return FromException(e, null, null);
            }
        }

        /// <summary>Глубокое сравнение JSON-значений по правилам проверки (числа с допуском 1e-6).</summary>
        public static bool JsonEquals(object expected, object actual)
        {
            if (expected == null || actual == null) return expected == null && actual == null;
            if (IsNum(expected) && IsNum(actual))
            {
                if (expected is long && actual is long) return (long)expected == (long)actual;
                double x = ToD(expected), y = ToD(actual);
                if (double.IsNaN(x) || double.IsNaN(y)) return double.IsNaN(x) && double.IsNaN(y);
                if (double.IsInfinity(x) || double.IsInfinity(y)) return x == y;
                return Math.Abs(x - y) <= Math.Max(1e-6 * Math.Max(Math.Abs(x), Math.Abs(y)), 1e-6);
            }
            if (expected is bool || actual is bool)
                return expected is bool && actual is bool && (bool)expected == (bool)actual;
            var es = expected as string;
            var gs = actual as string;
            if (es != null || gs != null) return es != null && gs != null && string.Equals(es, gs, StringComparison.Ordinal);
            var ed = expected as IDictionary<string, object>;
            var gd = actual as IDictionary<string, object>;
            if (ed != null || gd != null)
            {
                if (ed == null || gd == null || ed.Count != gd.Count) return false;
                foreach (var kv in ed)
                {
                    object other;
                    if (!gd.TryGetValue(kv.Key, out other) || !JsonEquals(kv.Value, other)) return false;
                }
                return true;
            }
            var el = expected as IList;
            var gl = actual as IList;
            if (el != null || gl != null)
            {
                if (el == null || gl == null || el.Count != gl.Count) return false;
                for (int i = 0; i < el.Count; i++)
                    if (!JsonEquals(el[i], gl[i])) return false;
                return true;
            }
            return expected.Equals(actual);
        }

        // ======================= проверка по тестам =======================

        static JsCheckResult CheckCore(string code, string entry, IList<object> tests, JsLimits lim)
        {
            var total = Stopwatch.StartNew();
            var res = new JsCheckResult();
            code = code ?? "";
            tests = tests ?? new List<object>();
            for (int i = 0; i < tests.Count; i++) res.Tests.Add(Describe(i, entry, tests[i]));

            JsError load = null;
            if (string.IsNullOrEmpty(entry) || !EntryRe.IsMatch(entry))
                load = Err("NoFunction", "bad entry", "В задаче не указано имя функции для проверки (entry).", null);
            if (load == null) load = SyntaxCheck(code);

            for (int i = 0; i < tests.Count && load == null; i++)
            {
                var tr = res.Tests[i];
                double left = lim.TotalTimeoutSeconds - total.Elapsed.TotalSeconds;
                if (left < 0.05)
                {
                    tr.Skipped = true;
                    tr.Error = Err("Timeout", "total time budget",
                        "Тест не запускался: закончилось общее время проверки (" + Sec(lim.TotalTimeoutSeconds) + ").",
                        "Сначала исправь тесты, которые работают слишком долго.");
                    continue;
                }
                var t = tests[i] as IDictionary<string, object>;
                if (t == null)
                {
                    tr.Error = Err("Internal", "bad test", "Неверный формат теста " + (i + 1) + " в задаче.", null);
                    continue;
                }
                object input, expected;
                t.TryGetValue("input", out input);
                t.TryGetValue("expected", out expected);
                load = RunTest(tr, code, entry, input, expected, lim, Math.Min(lim.TimeoutSeconds, left));
            }

            if (load != null)
            {
                res.LoadError = load;
                foreach (var tr in res.Tests)
                    if (tr.Error == null && !tr.Passed) { tr.Error = load; tr.Skipped = false; }
            }
            foreach (var tr in res.Tests) if (tr.Passed) res.PassedCount++;
            res.AllPassed = res.Tests.Count > 0 && res.PassedCount == res.Tests.Count && res.LoadError == null;
            res.TotalMs = total.Elapsed.TotalMilliseconds;
            return res;
        }

        static JsTestResult Describe(int index, string entry, object test)
        {
            var tr = new JsTestResult { Index = index };
            var t = test as IDictionary<string, object>;
            object input = null, expected = null;
            if (t != null)
            {
                t.TryGetValue("input", out input);
                t.TryGetValue("expected", out expected);
            }
            var args = input as IList;
            if (args != null)
            {
                var parts = new List<string>();
                foreach (var a in args) parts.Add(MiniJson.Serialize(a));
                tr.InputText = string.Join(", ", parts.ToArray());
            }
            else tr.InputText = MiniJson.Serialize(input);
            tr.Call = (entry ?? "?") + "(" + tr.InputText + ")";
            tr.Expected = MiniJson.Serialize(expected);
            return tr;
        }

        /// <summary>Один тест в свежем движке. Возвращает ошибку загрузки (общую для всех тестов) или null.</summary>
        static JsError RunTest(JsTestResult tr, string code, string entry, object input, object expected, JsLimits lim, double budget)
        {
            var sw = Stopwatch.StartNew();
            JsError loadError = null;
            Session s = null;
            try
            {
                s = new Session(lim, StackBudget(lim), false);
                s.Deadline.Begin(TimeSpan.FromSeconds(budget));
                // 1. код игрока целиком (объявления функций, код верхнего уровня; его микрозадачи выполняются тут же)
                try { s.Engine.Execute(code, SourceName); }
                catch (Exception e) { loadError = FromException(e, s, lim, budget); }

                // 2. функция entry (в т. ч. const f = () => …)
                JsValue fn = null;
                if (loadError == null)
                {
                    string type;
                    try { type = s.Engine.Evaluate("typeof " + entry, RunnerSource).ToString(); }
                    catch (Exception) { type = "undefined"; }
                    if (type == "undefined")
                        loadError = Err("NoFunction", entry + " is not defined", "Функция «" + entry + "» не найдена.",
                            "Не переименовывай функцию из заготовки и объяви её на верхнем уровне кода.");
                    else if (type != "function")
                        loadError = Err("NoFunction", entry + " is not a function", "«" + entry + "» — не функция (typeof = " + type + ").",
                            "Проверка вызывает " + entry + "(…) — это должна быть функция.");
                    else fn = s.Engine.Evaluate(entry, RunnerSource);
                }

                // 3. вызов; микрозадачи выполняются внутри того же захода в движок, таймеры — по одному, пока Promise не решится
                if (loadError == null)
                {
                    try
                    {
                        s.Engine.Invoke(s.Setup, fn, MiniJson.Serialize(input), input is IList);
                        var box = s.Engine.Evaluate("__internRun()", RunnerSource).AsObject();
                        int loop = 0;
                        while (box.Get("state").AsNumber() == 0 && loop >= 0)
                        {
                            loop = s.Step();
                            if (loop == 0) break;
                        }
                        double state = box.Get("state").AsNumber();
                        JsValue value = box.Get("value");
                        if (state == 0)
                            tr.Error = loop == -1
                                ? Err("PromisePending", "promise never settled (timers)",
                                    "Promise не завершился: таймеры работали бы дольше " + Sec(lim.MaxTimerMs / 1000) + ".",
                                    "Проверь, что повторяющиеся таймеры (setInterval) останавливаются и Promise когда-нибудь выполняется.")
                                : Err("PromisePending", "promise never settled", "Promise так и не завершился.",
                                    "Проверь, что вызывается resolve/reject и что все await дожидаются значений, а не зависают.");
                        else if (state == 2)
                            tr.Error = FromThrown(value, null, s);
                        else
                        {
                            object got = FixMarkers(MiniJson.Parse(s.Engine.Invoke(s.ToJson, value).ToString()));
                            tr.Actual = MiniJson.Serialize(got);
                            tr.Passed = JsonEquals(expected, got);
                            if (!tr.Passed) tr.Note = Explain(expected, got, value.IsUndefined());
                        }
                    }
                    catch (Exception e) { tr.Error = FromException(e, s, lim, budget); }
                }
                s.Deadline.End();
            }
            catch (Exception e)
            {
                tr.Error = FromException(e, s, lim, budget);
            }
            finally
            {
                if (s != null)
                {
                    tr.Log = s.OutputText;
                    s.Dispose();
                }
            }
            if (loadError != null) tr.Error = loadError;
            tr.Ms = sw.Elapsed.TotalMilliseconds;
            return loadError;
        }

        static JsCheckResult HangCheck(string entry, IList<object> tests, JsError error)
        {
            var res = new JsCheckResult { LoadError = error, TotalMs = -1 };
            tests = tests ?? new List<object>();
            for (int i = 0; i < tests.Count; i++)
            {
                var tr = Describe(i, entry, tests[i]);
                tr.Error = res.LoadError;
                res.Tests.Add(tr);
            }
            return res;
        }

        static IList<object> ParseTests(string json)
        {
            object v;
            string err;
            if (string.IsNullOrEmpty(json) || !MiniJson.TryParse(json, out v, out err)) return new List<object>();
            return v as IList<object> ?? new List<object>();
        }

        // ======================= «Запустить» =======================

        static JsRunOutput RunCore(string code, JsLimits lim)
        {
            var sw = Stopwatch.StartNew();
            var o = new JsRunOutput();
            code = code ?? "";
            o.Error = SyntaxCheck(code);
            if (o.Error != null) { o.Ms = sw.Elapsed.TotalMilliseconds; return o; }

            Session s = null;
            try
            {
                s = new Session(lim, StackBudget(lim), true);
                s.Deadline.Begin(TimeSpan.FromSeconds(lim.TimeoutSeconds));
                try
                {
                    s.Engine.Execute(code, SourceName);      // микрозадачи (then/await) выполняются тут же
                    o.Error = s.TakeUnhandled();
                    while (o.Error == null)                  // цикл событий: таймеры по одному, после каждого — микрозадачи
                    {
                        int r = s.Step();
                        if (r == 0) break;
                        if (r == -1)
                        {
                            o.Error = Err("TimerLimit", "virtual time limit",
                                "Выполнение остановлено: таймеры работали бы дольше " + Sec(lim.MaxTimerMs / 1000) + ".",
                                "Похоже на setInterval без clearInterval — останови повторяющийся таймер, когда он больше не нужен.");
                            break;
                        }
                        o.Error = s.TakeUnhandled();
                    }
                }
                catch (Exception e) { o.Error = FromException(e, s, lim, lim.TimeoutSeconds); }
                s.Deadline.End();
            }
            catch (Exception e)
            {
                o.Error = FromException(e, s, lim, lim.TimeoutSeconds);
            }
            finally
            {
                if (s != null)
                {
                    o.Output = s.OutputText;
                    o.Lines = s.Lines;
                    o.Truncated = s.Truncated;
                    s.Dispose();
                }
            }
            o.Ms = sw.Elapsed.TotalMilliseconds;
            return o;
        }

        // ======================= движок =======================

        // Окружение: console (как в Node), таймеры в виртуальном времени, queueMicrotask, structuredClone, запуск теста
        // и JSON результата. Выполняется до кода игрока, поэтому нужные встроенные функции захвачены до того, как игрок
        // может их подменить. Внутри нет двойных кавычек (строка C# @"...").
        const string Prelude = @"(function (sink, advance, perfNow, promiseInfo, fatal, maxJsonDepth, maxResultDepth, maxTimeMs) {
  'use strict';
  // ---------- захваченные встроенные функции (игрок может их подменить, мы — нет) ----------
  var G = globalThis, call = Function.prototype.call;
  function uncurry(f) { return call.bind(f); }
  var keysOf = Object.keys, getProto = Object.getPrototypeOf, gopd = Object.getOwnPropertyDescriptor,
    gops = Object.getOwnPropertySymbols, defProp = Object.defineProperty, isArray = Array.isArray, objIs = Object.is,
    hasOwn = uncurry(Object.prototype.hasOwnProperty), propIsEnum = uncurry(Object.prototype.propertyIsEnumerable),
    fnToString = uncurry(Function.prototype.toString), objToString = uncurry(Object.prototype.toString),
    symToString = uncurry(Symbol.prototype.toString), errToString = uncurry(Error.prototype.toString),
    dateGetTime = uncurry(Date.prototype.getTime), dateToISO = uncurry(Date.prototype.toISOString),
    dateToString = uncurry(Date.prototype.toString), reToString = uncurry(RegExp.prototype.toString),
    mapForEach = uncurry(Map.prototype.forEach), setForEach = uncurry(Set.prototype.forEach),
    mapGet = uncurry(Map.prototype.get), mapSet = uncurry(Map.prototype.set), mapHas = uncurry(Map.prototype.has),
    mapDelete = uncurry(Map.prototype['delete']),
    mapSizeOf = uncurry(gopd(Map.prototype, 'size').get), setSizeOf = uncurry(gopd(Set.prototype, 'size').get),
    setAdd = uncurry(Set.prototype.add), setHas = uncurry(Set.prototype.has),
    numValueOf = uncurry(Number.prototype.valueOf), strValueOf = uncurry(String.prototype.valueOf),
    boolValueOf = uncurry(Boolean.prototype.valueOf), symValueOf = uncurry(Symbol.prototype.valueOf),
    bigValueOf = uncurry(BigInt.prototype.valueOf),
    aPush = uncurry(Array.prototype.push), aJoin = uncurry(Array.prototype.join), aSlice = uncurry(Array.prototype.slice),
    aIndexOf = uncurry(Array.prototype.indexOf), aSplice = uncurry(Array.prototype.splice), aUnshift = uncurry(Array.prototype.unshift),
    aSort = uncurry(Array.prototype.sort), aPop = uncurry(Array.prototype.pop),
    sIndexOf = uncurry(String.prototype.indexOf), sSlice = uncurry(String.prototype.slice), sRepeat = uncurry(String.prototype.repeat),
    sSplit = uncurry(String.prototype.split), sReplace = uncurry(String.prototype.replace), sCharCodeAt = uncurry(String.prototype.charCodeAt),
    sStartsWith = uncurry(String.prototype.startsWith), sEndsWith = uncurry(String.prototype.endsWith), sPadStart = uncurry(String.prototype.padStart),
    sPadEnd = uncurry(String.prototype.padEnd), sTrim = uncurry(String.prototype.trim), numToString = uncurry(Number.prototype.toString),
    numToFixed = uncurry(Number.prototype.toFixed), reExec = uncurry(RegExp.prototype.exec), reTest = uncurry(RegExp.prototype.test),
    pThen = uncurry(Promise.prototype.then), rApply = Reflect.apply, parse = JSON.parse, stringify = JSON.stringify,
    MapC = Map, SetC = Set, WeakMapC = WeakMap, WeakSetC = WeakSet, PromiseC = Promise, ErrorC = Error, DateC = Date,
    RegExpC = RegExp, ObjectC = Object, ArrayC = Array, TypeErrorC = TypeError, RangeErrorC = RangeError,
    NumberC = Number, StringC = String, BooleanC = Boolean, SymbolC = Symbol, BigIntC = BigInt,
    ArrayBufferC = ArrayBuffer, DataViewC = DataView, Uint8ArrayC = Uint8Array, isView = ArrayBuffer.isView,
    SymIterator = Symbol.iterator, SymToStringTag = Symbol.toStringTag,
    mMin = Math.min, mMax = Math.max, mFloor = Math.floor, mRound = Math.round, mSqrt = Math.sqrt,
    numIsFinite = Number.isFinite, numIsInteger = Number.isInteger, numIsNaN = Number.isNaN, parseIntF = parseInt, parseFloatF = parseFloat,
    resolved = Promise.resolve();
  var RUNNER = '<runner>';

  // =====================================================================================
  // inspect — как util.inspect в Node 22 (depth 2, breakLength 80, compact 3, без цветов)
  // =====================================================================================
  var kObjectType = 0, kArrayType = 1, kArrayExtrasType = 2;
  var keyStrRegExp = /^[a-zA-Z_][a-zA-Z_0-9]*$/, numberRegExp = /^(0|[1-9][0-9]*)$/;

  function hex2(n) { var h = numToString(n, 16).toUpperCase(); return h.length < 2 ? '0' + h : h; }
  function meta(c) {
    switch (c) {
      case 8: return '\\b'; case 9: return '\\t'; case 10: return '\\n'; case 12: return '\\f';
      case 13: return '\\r'; case 39: return '\\\''; case 92: return '\\\\';
    }
    return '\\x' + hex2(c);
  }
  function escapeChars(str, quote) {
    var result = '', last = 0, n = str.length;
    for (var i = 0; i < n; i++) {
      var p = sCharCodeAt(str, i);
      if (p === quote || p === 92 || p < 32 || (p > 126 && p < 160)) {
        result += sSlice(str, last, i) + meta(p);
        last = i + 1;
      } else if (p >= 0xd800 && p <= 0xdfff) {
        if (p <= 0xdbff && i + 1 < n) {
          var p2 = sCharCodeAt(str, i + 1);
          if (p2 >= 0xdc00 && p2 <= 0xdfff) { i++; continue; }
        }
        result += sSlice(str, last, i) + '\\u' + numToString(p, 16);
        last = i + 1;
      }
    }
    return last === 0 ? str : result + sSlice(str, last);
  }
  function strEscape(str) {
    var quote = 39;
    if (sIndexOf(str, '\'') !== -1) {
      if (sIndexOf(str, '\x22') === -1) quote = -1;
      else if (sIndexOf(str, '`') === -1 && sIndexOf(str, '${') === -1) quote = -2;
    }
    var r = escapeChars(str, quote);
    return quote === -1 ? '\x22' + r + '\x22' : quote === -2 ? '`' + r + '`' : '\'' + r + '\'';
  }
  function formatNumber(n) { return objIs(n, -0) ? '-0' : '' + n; }
  function splitLines(s) {
    var res = [], start = 0;
    for (var i = 0; i < s.length; i++) if (sCharCodeAt(s, i) === 10) { aPush(res, sSlice(s, start, i + 1)); start = i + 1; }
    if (start < s.length || res.length === 0) aPush(res, sSlice(s, start));
    return res;
  }
  function formatPrimitive(ctx, value) {
    switch (typeof value) {
      case 'string':
        var trailer = '';
        if (value.length > ctx.maxStringLength) {
          var rem = value.length - ctx.maxStringLength;
          value = sSlice(value, 0, ctx.maxStringLength);
          trailer = '... ' + rem + ' more character' + (rem > 1 ? 's' : '');
        }
        if (value.length > 16 && value.length > ctx.breakLength - ctx.indentationLvl - 4) {
          var parts = splitLines(value), out = [];
          for (var i = 0; i < parts.length; i++) aPush(out, strEscape(parts[i]));
          return aJoin(out, ' +\n' + sRepeat(' ', ctx.indentationLvl + 2)) + trailer;
        }
        return strEscape(value) + trailer;
      case 'number': return formatNumber(value);
      case 'bigint': return '' + value + 'n';
      case 'boolean': return value ? 'true' : 'false';
      case 'undefined': return 'undefined';
      default: return symToString(value);
    }
  }
  function isInstanceof(o, C) { try { return o instanceof C; } catch (e) { return false; } }
  function getConstructorName(obj) {
    var p = obj, firstProto;
    while (p !== null && p !== undefined) {
      var d;
      try { d = gopd(p, 'constructor'); } catch (e) { d = undefined; }
      if (d !== undefined && typeof d.value === 'function' && d.value.name !== '' && isInstanceof(obj, d.value)) return '' + d.value.name;
      p = getProto(p);
      if (firstProto === undefined) firstProto = p;
    }
    return firstProto === null ? null : 'Object';
  }
  function getPrefix(constructor, tag, fallback, size) {
    size = size || '';
    if (constructor === null) {
      if (tag !== '' && fallback !== tag) return '[' + fallback + size + ': null prototype] [' + tag + '] ';
      return '[' + fallback + size + ': null prototype] ';
    }
    if (tag !== '' && constructor !== tag) return constructor + size + ' [' + tag + '] ';
    return constructor + size + ' ';
  }
  function getKeys(value) {
    var keys = keysOf(value), syms = gops(value);
    for (var i = 0; i < syms.length; i++) if (propIsEnum(value, syms[i])) aPush(keys, syms[i]);
    return keys;
  }
  function isIndexKey(k) { return reTest(numberRegExp, k) && +k < 4294967295; }
  function getNonIndexKeys(value) {
    var res = [], syms = gops(value), i;
    if (value.length <= 20000) {
      var ks = keysOf(value);
      for (i = 0; i < ks.length; i++) if (!isIndexKey(ks[i])) aPush(res, ks[i]);
    }
    for (i = 0; i < syms.length; i++) if (propIsEnum(value, syms[i])) aPush(res, syms[i]);
    return res;
  }
  function isMap(v) { if (!isInstanceof(v, MapC)) return false; try { mapSizeOf(v); return true; } catch (e) { return false; } }
  function isSet(v) { if (!isInstanceof(v, SetC)) return false; try { setSizeOf(v); return true; } catch (e) { return false; } }
  function isDate(v) { if (!isInstanceof(v, DateC)) return false; try { dateGetTime(v); return true; } catch (e) { return false; } }
  function isRegExp(v) { return isInstanceof(v, RegExpC) && objToString(v) === '[object RegExp]'; }
  function isError(v) { return isInstanceof(v, ErrorC) || objToString(v) === '[object Error]'; }
  function isArguments(v) { return objToString(v) === '[object Arguments]'; }
  function isTypedArray(v) { return isView(v) && !isInstanceof(v, DataViewC); }
  function boxedType(v) {
    if (isInstanceof(v, NumberC)) { try { numValueOf(v); return 'Number'; } catch (e) { } }
    if (isInstanceof(v, StringC)) { try { strValueOf(v); return 'String'; } catch (e) { } }
    if (isInstanceof(v, BooleanC)) { try { boolValueOf(v); return 'Boolean'; } catch (e) { } }
    if (isInstanceof(v, BigIntC)) { try { bigValueOf(v); return 'BigInt'; } catch (e) { } }
    if (isInstanceof(v, SymbolC)) { try { symValueOf(v); return 'Symbol'; } catch (e) { } }
    return null;
  }
  function promiseState(v) { var s = promiseInfo(v, false); return s; }

  function newCtx(opts) {
    var ctx = { seen: [], circular: undefined, indentationLvl: 0, currentDepth: 0, depth: 2, breakLength: 80, compact: 3,
      maxArrayLength: 100, maxStringLength: 10000 };
    if (opts !== null && typeof opts === 'object') {
      if (opts.depth !== undefined) ctx.depth = opts.depth === null ? Infinity : opts.depth;
      if (typeof opts.breakLength === 'number') ctx.breakLength = opts.breakLength;
      if (opts.maxArrayLength !== undefined) ctx.maxArrayLength = opts.maxArrayLength === null ? Infinity : opts.maxArrayLength;
      if (opts.maxStringLength !== undefined) ctx.maxStringLength = opts.maxStringLength === null ? Infinity : opts.maxStringLength;
    }
    return ctx;
  }
  function inspect(value, opts) { return formatValue(newCtx(opts), value, 0); }

  function formatValue(ctx, value, recurseTimes) {
    if (typeof value !== 'object' && typeof value !== 'function') return formatPrimitive(ctx, value);
    if (value === null) return 'null';
    if (aIndexOf(ctx.seen, value) !== -1) {
      var index = 1;
      if (ctx.circular === undefined) { ctx.circular = new MapC(); mapSet(ctx.circular, value, index); }
      else {
        index = mapGet(ctx.circular, value);
        if (index === undefined) { index = mapSizeOf(ctx.circular) + 1; mapSet(ctx.circular, value, index); }
      }
      return '[Circular *' + index + ']';
    }
    return formatRaw(ctx, value, recurseTimes);
  }

  function formatRaw(ctx, value, recurseTimes) {
    var keys, constructor = getConstructorName(value), tag, base = '', formatter = formatNothing, braces,
      noIterator = true, extrasType = kObjectType, prefix, size;
    try { tag = value[SymToStringTag]; } catch (e) { tag = ''; }
    if (typeof tag !== 'string' || (tag !== '' && propIsEnum(value, SymToStringTag))) tag = '';
    if (SymIterator in value || constructor === null) {
      noIterator = false;
      if (isArray(value)) {
        prefix = (constructor !== 'Array' || tag !== '') ? getPrefix(constructor, tag, 'Array', '(' + value.length + ')') : '';
        keys = getNonIndexKeys(value);
        braces = [prefix + '[', ']'];
        if (value.length === 0 && keys.length === 0) return braces[0] + ']';
        extrasType = kArrayExtrasType;
        formatter = formatArray;
      } else if (isSet(value)) {
        size = setSizeOf(value);
        prefix = getPrefix(constructor, tag, 'Set', '(' + size + ')');
        keys = getKeys(value);
        formatter = formatSet;
        if (size === 0 && keys.length === 0) return prefix + '{}';
        braces = [prefix + '{', '}'];
      } else if (isMap(value)) {
        size = mapSizeOf(value);
        prefix = getPrefix(constructor, tag, 'Map', '(' + size + ')');
        keys = getKeys(value);
        formatter = formatMap;
        if (size === 0 && keys.length === 0) return prefix + '{}';
        braces = [prefix + '{', '}'];
      } else if (isTypedArray(value)) {
        keys = getNonIndexKeys(value);
        size = value.length;
        prefix = getPrefix(constructor, tag, '', '(' + size + ')');
        braces = [prefix + '[', ']'];
        if (size === 0 && keys.length === 0) return braces[0] + ']';
        formatter = formatTypedArray;
        extrasType = kArrayExtrasType;
      } else {
        noIterator = true;
      }
    }
    if (noIterator) {
      keys = getKeys(value);
      braces = ['{', '}'];
      var boxed;
      if (typeof value === 'function') {
        base = getFunctionBase(value, constructor, tag);
        if (keys.length === 0) return base;
      } else if (constructor === 'Object') {
        if (isArguments(value)) braces[0] = '[Arguments] {';
        else if (tag !== '') braces[0] = getPrefix(constructor, tag, 'Object') + '{';
        if (keys.length === 0) return braces[0] + '}';
      } else if (isRegExp(value)) {
        base = reToString(value);
        prefix = getPrefix(constructor, tag, 'RegExp');
        if (prefix !== 'RegExp ') base = prefix + base;
        if (keys.length === 0 || recurseTimes > ctx.depth) return base;
      } else if (isDate(value)) {
        base = numIsNaN(dateGetTime(value)) ? dateToString(value) : dateToISO(value);
        prefix = getPrefix(constructor, tag, 'Date');
        if (prefix !== 'Date ') base = prefix + base;
        if (keys.length === 0) return base;
      } else if (isError(value)) {
        base = formatError(value, constructor, tag, ctx, keys);
        if (keys.length === 0) return base;
      } else if (isInstanceof(value, ArrayBufferC)) {
        prefix = getPrefix(constructor, tag, 'ArrayBuffer');
        formatter = formatArrayBuffer;
        braces[0] = prefix + '{';
        aUnshift(keys, 'byteLength');
      } else if (promiseState(value) >= 0) {
        braces[0] = getPrefix(constructor, tag, 'Promise') + '{';
        formatter = formatPromise;
      } else if (isInstanceof(value, WeakSetC)) {
        braces[0] = getPrefix(constructor, tag, 'WeakSet') + '{';
        formatter = formatWeak;
      } else if (isInstanceof(value, WeakMapC)) {
        braces[0] = getPrefix(constructor, tag, 'WeakMap') + '{';
        formatter = formatWeak;
      } else if ((boxed = boxedType(value)) !== null) {
        base = getBoxedBase(value, ctx, keys, constructor, tag, boxed);
        if (keys.length === 0) return base;
      } else {
        if (keys.length === 0) return getPrefix(constructor, tag, 'Object') + '{}';
        braces[0] = getPrefix(constructor, tag, 'Object') + '{';
      }
    }
    if (recurseTimes > ctx.depth) {
      var cname = sSlice(getPrefix(constructor, tag, 'Object'), 0, -1);
      return constructor !== null ? '[' + cname + ']' : cname;
    }
    recurseTimes += 1;
    aPush(ctx.seen, value);
    ctx.currentDepth = recurseTimes;
    var output = formatter(ctx, value, recurseTimes);
    for (var i = 0; i < keys.length; i++) aPush(output, formatProperty(ctx, value, recurseTimes, keys[i], extrasType));
    if (ctx.circular !== undefined) {
      var idx = mapGet(ctx.circular, value);
      if (idx !== undefined) {
        var reference = '<ref *' + idx + '>';
        base = base === '' ? reference : reference + ' ' + base;
      }
    }
    aPop(ctx.seen);
    return reduceToSingleString(ctx, output, base, braces, extrasType, recurseTimes, value);
  }

  function formatNothing() { return []; }
  function remainingText(n) { return '... ' + n + ' more item' + (n > 1 ? 's' : ''); }

  function formatArray(ctx, value, recurseTimes) {
    var valLen = value.length, len = mMin(mMax(0, ctx.maxArrayLength), valLen), remaining = valLen - len, output = [];
    for (var i = 0; i < len; i++) {
      if (!hasOwn(value, i)) return formatSpecialArray(ctx, value, recurseTimes, len, output, i);
      aPush(output, formatProperty(ctx, value, recurseTimes, i, kArrayType));
    }
    if (remaining > 0) aPush(output, remainingText(remaining));
    return output;
  }
  function formatSpecialArray(ctx, value, recurseTimes, maxLength, output, i) {
    var keys = keysOf(value), index = i;
    for (; i < keys.length && output.length < maxLength; i++) {
      var key = keys[i], tmp = +key;
      if (tmp > 4294967294) break;
      if ('' + index !== key) {
        if (!reTest(numberRegExp, key)) break;
        var emptyItems = tmp - index;
        aPush(output, '<' + emptyItems + ' empty item' + (emptyItems > 1 ? 's' : '') + '>');
        index = tmp;
        if (output.length === maxLength) break;
      }
      aPush(output, formatProperty(ctx, value, recurseTimes, key, kArrayType));
      index++;
    }
    var remaining = value.length - index;
    if (output.length !== maxLength) {
      if (remaining > 0) aPush(output, '<' + remaining + ' empty item' + (remaining > 1 ? 's' : '') + '>');
    } else if (remaining > 0) {
      aPush(output, remainingText(remaining));
    }
    return output;
  }
  function formatTypedArray(ctx, value) {
    var length = value.length, maxLength = mMin(mMax(0, ctx.maxArrayLength), length), remaining = length - maxLength, output = [];
    for (var i = 0; i < maxLength; ++i) aPush(output, typeof value[i] === 'bigint' ? value[i] + 'n' : formatNumber(value[i]));
    if (remaining > 0) aPush(output, remainingText(remaining));
    return output;
  }
  function formatArrayBuffer(ctx, value) {
    var bytes;
    try { bytes = new Uint8ArrayC(value); } catch (e) { return ['(detached)']; }
    var n = mMin(ctx.maxArrayLength, bytes.length), parts = [];
    for (var i = 0; i < n; i++) aPush(parts, sSlice('0' + numToString(bytes[i], 16), -2));
    var str = aJoin(parts, ' '), remaining = bytes.length - ctx.maxArrayLength;
    if (remaining > 0) str += ' ... ' + remaining + ' more byte' + (remaining > 1 ? 's' : '');
    return ['[Uint8Contents]: <' + str + '>'];
  }
  function formatSet(ctx, value, recurseTimes) {
    var length = setSizeOf(value), maxLength = mMin(mMax(0, ctx.maxArrayLength), length), remaining = length - maxLength,
      output = [], i = 0;
    ctx.indentationLvl += 2;
    setForEach(value, function (v) { if (i++ < maxLength) aPush(output, formatValue(ctx, v, recurseTimes)); });
    if (remaining > 0) aPush(output, remainingText(remaining));
    ctx.indentationLvl -= 2;
    return output;
  }
  function formatMap(ctx, value, recurseTimes) {
    var length = mapSizeOf(value), maxLength = mMin(mMax(0, ctx.maxArrayLength), length), remaining = length - maxLength,
      output = [], i = 0;
    ctx.indentationLvl += 2;
    mapForEach(value, function (v, k) {
      if (i++ < maxLength) aPush(output, formatValue(ctx, k, recurseTimes) + ' => ' + formatValue(ctx, v, recurseTimes));
    });
    if (remaining > 0) aPush(output, remainingText(remaining));
    ctx.indentationLvl -= 2;
    return output;
  }
  function formatPromise(ctx, value, recurseTimes) {
    var state = promiseInfo(value, false);
    if (state === 0) return ['<pending>'];
    ctx.indentationLvl += 2;
    var str = formatValue(ctx, promiseInfo(value, true), recurseTimes);
    ctx.indentationLvl -= 2;
    return [state === 2 ? '<rejected> ' + str : str];
  }
  function formatWeak() { return ['<items unknown>']; }
  function formatProperty(ctx, value, recurseTimes, key, type) {
    var name, str, desc;
    try { desc = gopd(value, key); } catch (e) { desc = undefined; }
    if (desc === undefined) desc = { value: value[key], enumerable: false };   // byteLength у ArrayBuffer
    if (desc.value !== undefined) {
      ctx.indentationLvl += 2;
      str = formatValue(ctx, desc.value, recurseTimes);
      ctx.indentationLvl -= 2;
    } else if (desc.get !== undefined) {
      str = desc.set !== undefined ? '[Getter/Setter]' : '[Getter]';
    } else if (desc.set !== undefined) {
      str = '[Setter]';
    } else {
      str = 'undefined';
    }
    if (type === kArrayType) return str;
    if (typeof key === 'symbol') name = '[' + escapeChars(symToString(key), 39) + ']';
    else if (key === '__proto__') name = '[\'__proto__\']';
    else if (desc.enumerable === false) name = '[' + escapeChars(key, 39) + ']';
    else if (reTest(keyStrRegExp, key)) name = key;
    else name = strEscape(key);
    return name + ': ' + str;
  }
  function getFunctionBase(value, constructor, tag) {
    var src = '';
    try { src = fnToString(value); } catch (e) { }
    if (sStartsWith(src, 'class') && sEndsWith(src, '}')) {
      var sl = sSlice(src, 5, -1), bracketIndex = sIndexOf(sl, '{');
      if (bracketIndex !== -1 && (sIndexOf(sSlice(sl, 0, bracketIndex), '(') === -1 ||
          reExec(/^(\s+[^(]*?)\s*{/, sReplace(sl, /(\/\/.*?\n)|(\/\*(.|\n)*?\*\/)/g, '')) !== null)) return getClassBase(value, constructor, tag);
    }
    var type = 'Function';
    if (tag === 'GeneratorFunction' || tag === 'AsyncGeneratorFunction') type = 'Generator' + type;
    if (tag === 'AsyncFunction' || tag === 'AsyncGeneratorFunction') type = 'Async' + type;
    var base = '[' + type;
    if (constructor === null) base += ' (null prototype)';
    if (value.name === '') base += ' (anonymous)';
    else base += ': ' + value.name;
    base += ']';
    if (constructor !== type && constructor !== null) base += ' ' + constructor;
    if (tag !== '' && constructor !== tag) base += ' [' + tag + ']';
    return base;
  }
  function getClassBase(value, constructor, tag) {
    var name = (hasOwn(value, 'name') && value.name) || '(anonymous)', base = 'class ' + name;
    if (constructor !== 'Function' && constructor !== null) base += ' [' + constructor + ']';
    if (tag !== '' && constructor !== tag) base += ' [' + tag + ']';
    if (constructor !== null) {
      var superName = getProto(value).name;
      if (superName) base += ' extends ' + superName;
    } else {
      base += ' extends [null prototype]';
    }
    return '[' + base + ']';
  }
  function getBoxedBase(value, ctx, keys, constructor, tag, type) {
    var prim;
    if (type === 'Number') prim = numValueOf(value);
    else if (type === 'String') { prim = strValueOf(value); aSplice(keys, 0, value.length); }
    else if (type === 'Boolean') prim = boolValueOf(value);
    else if (type === 'BigInt') prim = bigValueOf(value);
    else prim = symValueOf(value);
    var base = '[' + type;
    if (type !== constructor) base += constructor === null ? ' (null prototype)' : ' (' + constructor + ')';
    base += ': ' + formatPrimitive(newCtx(), prim) + ']';
    if (tag !== '' && tag !== constructor) base += ' [' + tag + ']';
    return base;
  }
  function errorStack(err) {
    var head, st;
    try { head = errToString(err); } catch (e) { head = 'Error'; }
    try { st = err.stack; } catch (e) { st = undefined; }
    if (typeof st !== 'string' || st === '') return head;
    var lines = sSplit(st, '\n'), keep = [], i = 0;
    if (lines.length && !sStartsWith(sTrim(lines[0]), 'at ')) { head = lines[0]; i = 1; }
    for (; i < lines.length; i++) {
      var l = lines[i];
      if (sTrim(l) === '' || sIndexOf(l, RUNNER) !== -1) continue;
      aPush(keep, sStartsWith(l, '    ') ? l : '    ' + sTrim(l));
    }
    return keep.length ? head + '\n' + aJoin(keep, '\n') : head;
  }
  function improveStack(stack, constructor, name, tag) {
    var len = name.length;
    if (constructor === null || (sEndsWith(name, 'Error') && sStartsWith(stack, name) &&
        (stack.length === len || stack[len] === ':' || stack[len] === '\n'))) {
      var fallback = 'Error';
      if (constructor === null) {
        var start = reExec(/^([A-Z][a-z_ A-Z0-9[\]()-]+)(?::|\n\s+at)/, stack) || reExec(/^([a-z_A-Z0-9-]*Error)$/, stack);
        fallback = (start && start[1]) || '';
        len = fallback.length;
        fallback = fallback || 'Error';
      }
      var prefix = sSlice(getPrefix(constructor, tag, fallback), 0, -1);
      if (name !== prefix) {
        if (sIndexOf(prefix, name) !== -1) stack = len === 0 ? prefix + ': ' + stack : prefix + sSlice(stack, len);
        else stack = prefix + ' [' + name + ']' + sSlice(stack, len);
      }
    }
    return stack;
  }
  function formatError(err, constructor, tag, ctx, keys) {
    var name = err.name != null ? '' + err.name : 'Error', stack = errorStack(err), j, ix;
    if (keys.length !== 0) {
      var names = ['name', 'message', 'stack'];
      for (j = 0; j < 3; j++) {
        ix = aIndexOf(keys, names[j]);
        if (ix !== -1 && sIndexOf(stack, '' + err[names[j]]) !== -1) aSplice(keys, ix, 1);
      }
    }
    if ('cause' in err && (keys.length === 0 || aIndexOf(keys, 'cause') === -1)) aPush(keys, 'cause');
    if (isArray(err.errors) && (keys.length === 0 || aIndexOf(keys, 'errors') === -1)) aPush(keys, 'errors');
    stack = improveStack(stack, constructor, name, tag);
    var msg = err.message, pos = (msg && sIndexOf(stack, '' + msg)) || -1;
    if (pos !== -1) pos += ('' + msg).length;
    if (sIndexOf(stack, '\n    at', pos) === -1) stack = '[' + stack + ']';
    if (ctx.indentationLvl !== 0) stack = sReplace(stack, /\n/g, '\n' + sRepeat(' ', ctx.indentationLvl));
    return stack;
  }
  function isBelowBreakLength(ctx, output, start, base) {
    var totalLength = output.length + start;
    if (totalLength + output.length > ctx.breakLength) return false;
    for (var i = 0; i < output.length; i++) {
      totalLength += output[i].length;
      if (totalLength > ctx.breakLength) return false;
    }
    return base === '' || sIndexOf(base, '\n') === -1;
  }
  function groupArrayElements(ctx, output, value) {
    var totalLength = 0, maxLength = 0, i = 0, outputLength = output.length;
    if (ctx.maxArrayLength < output.length) outputLength--;
    var separatorSpace = 2, dataLen = [];
    for (; i < outputLength; i++) {
      var len = output[i].length;
      dataLen[i] = len;
      totalLength += len + separatorSpace;
      if (maxLength < len) maxLength = len;
    }
    var actualMax = maxLength + separatorSpace;
    if (actualMax * 3 + ctx.indentationLvl < ctx.breakLength && (totalLength / actualMax > 5 || maxLength <= 6)) {
      var averageBias = mSqrt(actualMax - totalLength / output.length), biasedMax = mMax(actualMax - 3 - averageBias, 1);
      var columns = mMin(mRound(mSqrt(2.5 * biasedMax * outputLength) / biasedMax), mFloor((ctx.breakLength - ctx.indentationLvl) / actualMax),
        ctx.compact * 4, 15);
      if (columns <= 1) return output;
      var tmp = [], maxLineLength = [];
      for (i = 0; i < columns; i++) {
        var lineLength = 0;
        for (var j = i; j < output.length; j += columns) if (dataLen[j] > lineLength) lineLength = dataLen[j];
        aPush(maxLineLength, lineLength + separatorSpace);
      }
      var padStart = true;
      if (value !== undefined) {
        for (i = 0; i < output.length; i++) {
          if (typeof value[i] !== 'number' && typeof value[i] !== 'bigint') { padStart = false; break; }
        }
      }
      for (i = 0; i < outputLength; i += columns) {
        var max = mMin(i + columns, outputLength), str = '', k = i;
        for (; k < max - 1; k++) {
          var padding = maxLineLength[k - i] + output[k].length - dataLen[k];
          str += padStart ? sPadStart(output[k] + ', ', padding, ' ') : sPadEnd(output[k] + ', ', padding, ' ');
        }
        if (padStart) str += sPadStart(output[k], maxLineLength[k - i] + output[k].length - dataLen[k] - separatorSpace, ' ');
        else str += output[k];
        aPush(tmp, str);
      }
      if (ctx.maxArrayLength < output.length) aPush(tmp, output[outputLength]);
      output = tmp;
    }
    return output;
  }
  function reduceToSingleString(ctx, output, base, braces, extrasType, recurseTimes, value) {
    var entries = output.length;
    if (extrasType === kArrayExtrasType && entries > 6) output = groupArrayElements(ctx, output, value);
    if (ctx.currentDepth - recurseTimes < ctx.compact && entries === output.length) {
      var start = output.length + ctx.indentationLvl + braces[0].length + base.length + 10;
      if (isBelowBreakLength(ctx, output, start, base)) {
        var joined = aJoin(output, ', ');
        if (sIndexOf(joined, '\n') === -1) return (base ? base + ' ' : '') + braces[0] + ' ' + joined + ' ' + braces[1];
      }
    }
    var indentation = '\n' + sRepeat(' ', ctx.indentationLvl);
    return (base ? base + ' ' : '') + braces[0] + indentation + '  ' + aJoin(output, ',' + indentation + '  ') + indentation + braces[1];
  }

  // ---------- console.log(...) как util.format ----------
  var builtinCtors = ['Object', 'Error', 'Array', 'Date', 'RegExp', 'Function', 'Map', 'Set', 'Promise', 'Number', 'String', 'Boolean',
    'Symbol', 'BigInt', 'TypeError', 'RangeError', 'SyntaxError', 'ReferenceError', 'EvalError', 'URIError', 'AggregateError'];
  function hasBuiltInToString(value) {
    if (typeof value.toString !== 'function') return true;
    if (hasOwn(value, 'toString')) return false;
    var p = value;
    do { p = getProto(p); } while (p !== null && !hasOwn(p, 'toString'));
    if (p === null) return true;
    var d = gopd(p, 'constructor');
    return d !== undefined && typeof d.value === 'function' && aIndexOf(builtinCtors, d.value.name) !== -1;
  }
  function formatArgs(args) {
    var first = args[0], a = 0, str = '', joinStr = '';
    if (typeof first === 'string') {
      if (args.length === 1) return first;
      var tempStr, lastPos = 0;
      for (var i = 0; i < first.length - 1; i++) {
        if (sCharCodeAt(first, i) === 37) {
          var nextChar = sCharCodeAt(first, ++i);
          if (a + 1 !== args.length) {
            var arg;
            switch (nextChar) {
              case 115: // s
                arg = args[++a];
                if (typeof arg === 'number') tempStr = formatNumber(arg);
                else if (typeof arg === 'bigint') tempStr = arg + 'n';
                else if (typeof arg !== 'object' || arg === null || !hasBuiltInToString(arg)) tempStr = StringC(arg);
                else tempStr = inspect(arg, { depth: 0 });
                break;
              case 106: // j
                try { tempStr = stringify(args[++a]); } catch (e) { tempStr = '[Circular]'; }
                break;
              case 100: // d
                arg = args[++a];
                if (typeof arg === 'bigint') tempStr = arg + 'n';
                else if (typeof arg === 'symbol') tempStr = 'NaN';
                else tempStr = formatNumber(NumberC(arg));
                break;
              case 79: tempStr = inspect(args[++a]); break; // O
              case 111: tempStr = inspect(args[++a], { depth: 4 }); break; // o
              case 105: // i
                arg = args[++a];
                if (typeof arg === 'bigint') tempStr = arg + 'n';
                else if (typeof arg === 'symbol') tempStr = 'NaN';
                else tempStr = formatNumber(parseIntF(arg));
                break;
              case 102: // f
                arg = args[++a];
                tempStr = typeof arg === 'symbol' ? 'NaN' : formatNumber(parseFloatF(arg));
                break;
              case 99: a += 1; tempStr = ''; break; // c
              case 37: str += sSlice(first, lastPos, i); lastPos = i + 1; continue;
              default: continue;
            }
            if (lastPos !== i - 1) str += sSlice(first, lastPos, i - 1);
            str += tempStr;
            lastPos = i + 1;
          } else if (nextChar === 37) {
            str += sSlice(first, lastPos, i);
            lastPos = i + 1;
          }
        }
      }
      if (lastPos !== 0) {
        a++;
        joinStr = ' ';
        if (lastPos < first.length) str += sSlice(first, lastPos);
      }
    }
    while (a < args.length) {
      var value = args[a];
      str += joinStr;
      str += typeof value !== 'string' ? inspect(value) : value;
      joinStr = ' ';
      a++;
    }
    return str;
  }

  // ---------- console ----------
  var groupIndent = '', counts = new MapC(), timersLabels = new MapC();
  function emit(stream, text) {
    if (groupIndent.length !== 0) {
      if (sIndexOf(text, '\n') !== -1) text = sReplace(text, /\n/g, '\n' + groupIndent);
      text = groupIndent + text;
    }
    sink(stream, text);
  }
  function formatTime(ms) {
    var hours = 0, minutes = 0, seconds = 0;
    if (ms >= 1000) {
      if (ms >= 60000) {
        if (ms >= 3600000) { hours = mFloor(ms / 3600000); ms = ms % 3600000; }
        minutes = mFloor(ms / 60000);
        ms = ms % 60000;
      }
      seconds = ms / 1000;
    }
    if (hours !== 0 || minutes !== 0) {
      var parts = sSplit(numToFixed(seconds, 3), '.'), res = hours !== 0 ? hours + ':' + sPadStart('' + minutes, 2, '0') : minutes;
      return res + ':' + sPadStart(parts[0], 2, '0') + '.' + parts[1] + ' (' + (hours !== 0 ? 'h:m' : '') + 'm:ss.mmm)';
    }
    if (seconds !== 0) return numToFixed(seconds, 3) + 's';
    return NumberC(numToFixed(ms, 3)) + 'ms';
  }
  function tableInspect(v) {
    var depth = v !== null && typeof v === 'object' && !isArray(v) && keysOf(v).length > 2 ? -1 : 0;
    return inspect(v, { depth: depth, maxArrayLength: 3, breakLength: Infinity });
  }
  function renderRow(row, widths) {
    var out = '│ ';
    for (var i = 0; i < row.length; i++) {
      var cell = row[i], needed = widths[i] - cell.length;
      out += cell + sRepeat(' ', needed);
      if (i !== row.length - 1) out += ' │ ';
    }
    return out + ' │';
  }
  function renderTable(head, columns) {
    var rows = [], widths = [], i, j, longest = 0;
    for (i = 0; i < head.length; i++) widths[i] = head[i].length;
    for (i = 0; i < columns.length; i++) if (columns[i].length > longest) longest = columns[i].length;
    for (i = 0; i < head.length; i++) {
      var column = columns[i];
      for (j = 0; j < longest; j++) {
        if (rows[j] === undefined) rows[j] = [];
        var value = rows[j][i] = hasOwn(column, j) ? column[j] : '';
        if (value.length > widths[i]) widths[i] = value.length;
      }
    }
    var divider = [];
    for (i = 0; i < widths.length; i++) aPush(divider, sRepeat('─', widths[i] + 2));
    var result = '┌' + aJoin(divider, '┬') + '┐\n' + renderRow(head, widths) + '\n' +
      '├' + aJoin(divider, '┼') + '┤\n';
    for (i = 0; i < rows.length; i++) result += renderRow(rows[i], widths) + '\n';
    return result + '└' + aJoin(divider, '┴') + '┘';
  }
  function consoleTable(data, properties) {
    if (data === null || typeof data !== 'object') return con.log(data);
    var i, keys, values;
    if (isMap(data)) {
      var mk = [], mv = [], idx = [], n = 0;
      mapForEach(data, function (v, k) { aPush(idx, tableInspect(n++)); aPush(mk, tableInspect(k)); aPush(mv, tableInspect(v)); });
      return emit(0, renderTable(['(iteration index)', 'Key', 'Values'], [idx, mk, mv]));
    }
    if (isSet(data)) {
      var sv = [], sidx = [], sn = 0;
      setForEach(data, function (v) { aPush(sidx, tableInspect(sn++)); aPush(sv, tableInspect(v)); });
      return emit(0, renderTable(['(iteration index)', 'Values'], [sidx, sv]));
    }
    var map = ObjectC.create(null), mapKeys = [], hasPrimitives = false, valuesKeyArray = [], indexKeyArray = keysOf(data);
    for (i = 0; i < indexKeyArray.length; i++) {
      var item = data[indexKeyArray[i]];
      var primitive = item === null || (typeof item !== 'function' && typeof item !== 'object');
      if (properties === undefined && primitive) {
        hasPrimitives = true;
        valuesKeyArray[i] = tableInspect(item);
      } else {
        var ks = properties || keysOf(item);
        for (var k = 0; k < ks.length; k++) {
          var key = ks[k];
          if (map[key] === undefined) { map[key] = []; aPush(mapKeys, key); }
          if ((primitive && properties) || !hasOwn(item, key)) map[key][i] = '';
          else map[key][i] = tableInspect(item[key]);
        }
      }
    }
    keys = aSlice(mapKeys);
    values = [];
    for (i = 0; i < mapKeys.length; i++) aPush(values, map[mapKeys[i]]);
    if (hasPrimitives) { aPush(keys, 'Values'); aPush(values, valuesKeyArray); }
    aUnshift(keys, '(index)');
    aUnshift(values, indexKeyArray);
    emit(0, renderTable(keys, values));
  }
  function label(l) { return l === undefined ? 'default' : '' + l; }
  var con = {};
  con.log = function log() { emit(0, formatArgs(arguments)); };
  con.info = function info() { emit(0, formatArgs(arguments)); };
  con.debug = function debug() { emit(0, formatArgs(arguments)); };
  con.dirxml = con.log;
  con.error = function error() { emit(1, formatArgs(arguments)); };
  con.warn = function warn() { emit(1, formatArgs(arguments)); };
  con.trace = function trace() {
    var head = arguments.length ? 'Trace: ' + formatArgs(arguments) : 'Trace', st = errorStack(new ErrorC('x'));
    var nl = sIndexOf(st, '\n');
    emit(1, nl === -1 ? head : head + sSlice(st, nl));
  };
  con.dir = function dir(obj, options) { emit(0, inspect(obj, options)); };
  con.table = function table(data, properties) { consoleTable(data, properties); };
  con.assert = function assert(expression) {
    if (expression) return;
    var args = aSlice(arguments, 1);
    if (args.length && typeof args[0] === 'string') args[0] = 'Assertion failed: ' + args[0];
    else aUnshift(args, 'Assertion failed');
    emit(1, formatArgs(args));
  };
  con.count = function count(l) {
    l = label(l);
    var c = (mapGet(counts, l) || 0) + 1;
    mapSet(counts, l, c);
    emit(0, l + ': ' + c);
  };
  con.countReset = function countReset(l) {
    l = label(l);
    if (!mapHas(counts, l)) { emit(1, '(node) Warning: Count for \'' + l + '\' does not exist'); return; }
    mapSet(counts, l, 0);
  };
  con.group = function group() { if (arguments.length) emit(0, formatArgs(arguments)); groupIndent += '  '; };
  con.groupCollapsed = con.group;
  con.groupEnd = function groupEnd() { groupIndent = sSlice(groupIndent, 0, groupIndent.length - 2); };
  con.time = function time(l) {
    l = label(l);
    if (mapHas(timersLabels, l)) { emit(1, '(node) Warning: Label \'' + l + '\' already exists for console.time()'); return; }
    mapSet(timersLabels, l, perfNow());
  };
  function timeLogImpl(name, l, data) {
    l = label(l);
    if (!mapHas(timersLabels, l)) { emit(1, '(node) Warning: No such label \'' + l + '\' for console.' + name + '()'); return false; }
    var text = l + ': ' + formatTime(perfNow() - mapGet(timersLabels, l));
    if (data && data.length) text += ' ' + formatArgs(data);
    emit(0, text);
    return true;
  }
  con.timeEnd = function timeEnd(l) { if (timeLogImpl('timeEnd', l)) mapDelete(timersLabels, label(l)); };
  con.timeLog = function timeLog(l) { timeLogImpl('timeLog', l, aSlice(arguments, 1)); };
  con.clear = function clear() { };

  // ---------- микрозадачи и таймеры (виртуальное время, порядок как в Node) ----------
  function describeArg(v) {
    if (v === null || v === undefined) return 'Received ' + v;
    if (typeof v === 'function') return 'Received function ' + (v.name || '<anonymous>');
    if (typeof v === 'object') {
      var c = getConstructorName(v);
      return c ? 'Received an instance of ' + c : 'Received ' + inspect(v, { depth: -1 });
    }
    var shown = inspect(v);
    if (shown.length > 28) shown = sSlice(shown, 0, 25) + '...';
    return 'Received type ' + typeof v + ' (' + shown + ')';
  }
  function checkCallback(cb) {
    if (typeof cb !== 'function') {
      var e = new TypeErrorC('The \x22callback\x22 argument must be of type function. ' + describeArg(cb));
      e.code = 'ERR_INVALID_ARG_TYPE';
      throw e;
    }
  }
  function queueMicrotask(callback) {
    checkCallback(callback);
    pThen(resolved, function () {
      try { callback(); } catch (e) { fatal(e); }
    });
  }
  var timers = [], immediates = [], checkQueue = [], active = new MapC(), vnow = 0, seq = 0, nextId = 1, loopStarted = false, phase = 0;
  function addTimer(callback, after, args, repeat) {
    checkCallback(callback);
    after = after * 1;
    if (!(after >= 1 && after <= 2147483647)) after = 1;
    var t = { id: nextId++, when: vnow + after, seq: seq++, cb: callback, args: args, repeat: repeat ? after : 0, active: true };
    aPush(timers, t);
    mapSet(active, t.id, t);
    return t.id;
  }
  function removeFrom(list, t) { var i = aIndexOf(list, t); if (i !== -1) aSplice(list, i, 1); }
  function removeTimer(id) {
    if (id !== null && typeof id === 'object' && typeof id.id === 'number') id = id.id;
    var t = mapGet(active, id);
    if (t === undefined) return;
    t.active = false;
    mapDelete(active, id);
    removeFrom(timers, t);
    removeFrom(immediates, t);
    removeFrom(checkQueue, t);
  }
  function setTimeout(callback, after) { return addTimer(callback, after, aSlice(arguments, 2), false); }
  function setInterval(callback, repeat) { return addTimer(callback, repeat, aSlice(arguments, 2), true); }
  function setImmediate(callback) {
    checkCallback(callback);
    var im = { id: nextId++, cb: callback, args: aSlice(arguments, 1), active: true };
    aPush(immediates, im);
    mapSet(active, im.id, im);
    return im.id;
  }
  function clearTimeout(id) { removeTimer(id); }
  // Один шаг цикла событий. 0 — делать больше нечего, 1 — выполнен колбэк (после него движок выполнит все микрозадачи),
  // 2 — таймеры ушли дальше maxTimeMs виртуального времени (бесконечный setInterval?).
  function step() {
    if (!loopStarted) { loopStarted = true; vnow += 1; advance(1); }   // пока скрипт работал, «прошла» 1 мс — таймеры 0 мс уже готовы
    for (;;) {
      if (phase === 0) {
        var best = -1;
        for (var i = 0; i < timers.length; i++) {
          var t = timers[i];
          if (t.when <= vnow && (best < 0 || t.when < timers[best].when || (t.when === timers[best].when && t.seq < timers[best].seq))) best = i;
        }
        if (best >= 0) {
          var timer = timers[best];
          aSplice(timers, best, 1);
          if (timer.repeat === 0) { timer.active = false; mapDelete(active, timer.id); }
          try {
            rApply(timer.cb, undefined, timer.args);
          } finally {
            if (timer.repeat > 0 && timer.active) { timer.when = vnow + timer.repeat; timer.seq = seq++; aPush(timers, timer); }
          }
          return 1;
        }
        phase = 1;
        checkQueue = immediates;
        immediates = [];
      }
      while (checkQueue.length) {
        var im = checkQueue[0];
        aSplice(checkQueue, 0, 1);
        if (!im.active) continue;
        im.active = false;
        mapDelete(active, im.id);
        rApply(im.cb, undefined, im.args);
        return 1;
      }
      phase = 0;
      if (timers.length === 0 && immediates.length === 0) return 0;
      if (immediates.length === 0) {
        var next = Infinity;
        for (var k = 0; k < timers.length; k++) if (timers[k].when < next) next = timers[k].when;
        if (next > vnow) { advance(next - vnow); vnow = next; }
      }
      if (vnow > maxTimeMs) return 2;
    }
  }
  function pendingTimers() { return timers.length + immediates.length + checkQueue.length; }

  // ---------- structuredClone (упрощённый, но с циклами, Map/Set/Date/RegExp) ----------
  function cloneError(msg) { var e = new ErrorC(msg); e.name = 'DataCloneError'; e.code = 25; return e; }
  function structuredClone(value) {
    if (arguments.length === 0) throw new TypeErrorC('The \x22value\x22 argument must be specified');
    var seen = new MapC();
    function clone(v) {
      if (typeof v === 'symbol') throw cloneError(symToString(v) + ' could not be cloned.');
      if (typeof v === 'function') throw cloneError(fnToString(v) + ' could not be cloned.');
      if (v === null || typeof v !== 'object') return v;
      if (mapHas(seen, v)) return mapGet(seen, v);
      var out, i, ks;
      if (isArray(v)) {
        out = new ArrayC(v.length);
        mapSet(seen, v, out);
        ks = keysOf(v);
        for (i = 0; i < ks.length; i++) out[ks[i]] = clone(v[ks[i]]);
        return out;
      }
      if (isDate(v)) { out = new DateC(dateGetTime(v)); mapSet(seen, v, out); return out; }
      if (isRegExp(v)) { out = new RegExpC(v.source, v.flags); mapSet(seen, v, out); return out; }
      if (isMap(v)) {
        out = new MapC(); mapSet(seen, v, out);
        mapForEach(v, function (val, key) { mapSet(out, clone(key), clone(val)); });
        return out;
      }
      if (isSet(v)) {
        out = new SetC(); mapSet(seen, v, out);
        setForEach(v, function (val) { setAdd(out, clone(val)); });
        return out;
      }
      if (isError(v)) {
        var C = G[v.name] && typeof G[v.name] === 'function' && isInstanceof(G[v.name].prototype, ErrorC) ? G[v.name] : ErrorC;
        out = new C(v.message);
        mapSet(seen, v, out);
        if ('cause' in v) out.cause = clone(v.cause);
        return out;
      }
      var bt = boxedType(v);
      if (bt === 'Number') return new NumberC(numValueOf(v));
      if (bt === 'String') return new StringC(strValueOf(v));
      if (bt === 'Boolean') return new BooleanC(boolValueOf(v));
      if (bt !== null) throw cloneError(inspect(v) + ' could not be cloned.');
      if (isInstanceof(v, ArrayBufferC)) { out = v.slice(0); mapSet(seen, v, out); return out; }
      if (isTypedArray(v)) { out = new v.constructor(v); mapSet(seen, v, out); return out; }
      if (promiseState(v) >= 0 || isInstanceof(v, WeakMapC) || isInstanceof(v, WeakSetC))
        throw cloneError('#<' + (getConstructorName(v) || 'Object') + '> could not be cloned.');
      out = {};
      mapSet(seen, v, out);
      ks = keysOf(v);
      for (i = 0; i < ks.length; i++) out[ks[i]] = clone(v[ks[i]]);
      return out;
    }
    return clone(value);
  }

  function hidden(o, name, v) { defProp(o, name, { value: v, writable: true, configurable: true, enumerable: false }); }

  // ---------- защита от глубокой рекурсии внутри встроенных функций Jint ----------
  // join/toString/toLocaleString вложенных массивов и flat(Infinity) рекурсивны в C#, минуя проверки стека.
  // Обёртки на JS добавляют по кадру JS на уровень вложенности — его видят лимит рекурсии и защита стека.
  var AP = ArrayC.prototype, origJoin = AP.join, origToLocale = AP.toLocaleString, origFlat = AP.flat;
  function nestingOver(arr, limit) {
    var st = [arr, 0], seenSet = new SetC();
    while (st.length) {
      var d = aPop(st), a = aPop(st);
      if (d > limit) return true;
      if (setHas(seenSet, a)) continue;
      setAdd(seenSet, a);
      var n = a.length >>> 0;
      for (var i = 0; i < n; i++) { var x = a[i]; if (isArray(x)) aPush(st, x, d + 1); }
    }
    return false;
  }
  var wrappers = {
    join: function join(separator) { return rApply(origJoin, this, arguments); },
    toLocaleString: function toLocaleString() { return rApply(origToLocale, this, arguments); },
    flat: function flat() {
      var depth = arguments.length > 0 && arguments[0] !== undefined ? +arguments[0] : 1;
      if (depth > 64 && isArray(this) && nestingOver(this, 2000)) throw new RangeErrorC('Maximum call stack size exceeded');
      return rApply(origFlat, this, arguments);
    }
  };
  hidden(AP, 'join', wrappers.join);
  hidden(AP, 'toLocaleString', wrappers.toLocaleString);
  hidden(AP, 'flat', wrappers.flat);

  // ---------- глобальные функции среды ----------
  hidden(G, 'console', con);
  hidden(G, 'queueMicrotask', queueMicrotask);
  hidden(G, 'setTimeout', setTimeout);
  hidden(G, 'setInterval', setInterval);
  hidden(G, 'setImmediate', setImmediate);
  hidden(G, 'clearTimeout', clearTimeout);
  hidden(G, 'clearInterval', clearTimeout);
  hidden(G, 'clearImmediate', clearTimeout);
  hidden(G, 'structuredClone', structuredClone);
  var perf = {};
  hidden(perf, 'now', function now() { return perfNow(); });
  hidden(perf, 'timeOrigin', 0);
  hidden(G, 'performance', perf);

  // ---------- проверка функции: вызов, ожидание Promise, JSON результата ----------
  var pending = null;
  function setup(fn, argsJson, spread) { pending = [fn, parse(argsJson), spread]; }
  function run() {
    var p = pending;
    pending = null;
    var r = p[2] ? rApply(p[0], undefined, p[1]) : p[0](p[1]);
    var box = { state: 1, value: r };
    if (r !== null && (typeof r === 'object' || typeof r === 'function') && typeof r.then === 'function') {
      box.state = 0;
      pThen(PromiseC.resolve(r), function (x) { box.state = 1; box.value = x; }, function (e) { box.state = 2; box.value = e; });
    }
    return box;
  }
  defProp(G, '__internRun', { value: run });
  defProp(G, '__internStep', { value: step });

  // JSON.stringify в Jint рекурсивен: слишком глубокую вложенность отсекаем заранее, без рекурсии
  function tooDeep(v, maxDepth) {
    if (v === null || typeof v !== 'object') return false;
    var st = [v, 1], seenSet = new SetC();
    while (st.length) {
      var d = aPop(st), o = aPop(st);
      if (d > maxDepth) return true;
      if (setHas(seenSet, o)) continue;
      setAdd(seenSet, o);
      var arr = isArray(o), ks = arr ? null : keysOf(o), n = arr ? o.length : ks.length;
      for (var i = 0; i < n; i++) {
        var x = arr ? o[i] : o[ks[i]];
        if (x !== null && typeof x === 'object') aPush(st, x, d + 1);
      }
    }
    return false;
  }
  var stringify0 = stringify;
  var safeStringify = function stringify(value, replacer, space) {
    if (tooDeep(value, maxJsonDepth)) throw new RangeErrorC('Maximum call stack size exceeded');
    return rApply(stringify0, JSON, arguments);
  };
  defProp(JSON, 'stringify', { value: safeStringify, writable: true, configurable: true, enumerable: false });

  function toJson(v) {
    if (tooDeep(v, maxResultDepth)) throw new RangeErrorC('Результат вложен слишком глубоко (больше ' + maxResultDepth + ' уровней), его не получается превратить в JSON');
    var s = stringify(v === undefined ? null : v, function (k, x) {
      if (typeof x === 'number' && !numIsFinite(x)) return '\u0001' + x;
      if (typeof x === 'bigint') return '\u0001' + x;
      return x;
    });
    return s === undefined ? 'null' : s;
  }
  return { setup: setup, toJson: toJson, inspect: function (v) { return inspect(v); }, pendingTimers: pendingTimers };
})
";

        static readonly object preludeLock = new object();

        static Prepared<Acornima.Ast.Script> PreludeScript()
        {
            lock (preludeLock)
            {
                if (preparedPrelude == null) preparedPrelude = new PreparedBox { Value = Engine.PrepareScript(Prelude, RunnerSource) };
                return preparedPrelude.Value;
            }
        }
        sealed class PreparedBox { public Prepared<Acornima.Ast.Script> Value; }
        static PreparedBox preparedPrelude;

        sealed class UncaughtJsException : Exception
        {
            public readonly JsValue Value;
            public UncaughtJsException(JsValue value) : base("uncaught exception in microtask") { Value = value; }
        }

        /// <summary>Часы движка: реальное время + «перемотанное» ожидание таймеров. Date.now/new Date/performance.now.</summary>
        sealed class SessionClock : Jint.Runtime.DefaultTimeSystem
        {
            readonly DateTimeOffset start = DateTimeOffset.UtcNow;
            readonly Stopwatch sw = Stopwatch.StartNew();
            double skipped;
            public SessionClock() : base(TimeZoneInfo.Utc, EnUs) { }
            public override DateTimeOffset GetUtcNow() { return start.AddTicks((long)(Math.Floor(sw.Elapsed.TotalMilliseconds + skipped) * TimeSpan.TicksPerMillisecond)); }
            public double PerfNow { get { return sw.Elapsed.TotalMilliseconds + skipped; } }
            public void Advance(double ms) { if (ms > 0 && !double.IsInfinity(ms)) skipped += ms; }
        }

        /// <summary>Один движок Jint со своим выводом. Живёт и умирает в одном потоке.</summary>
        sealed class Session : IDisposable
        {
            public Engine Engine;
            public readonly OperationDeadlineConstraint Deadline = new OperationDeadlineConstraint();
            public JsValue Setup, ToJson, Inspect;
            public List<JsLogLine> Lines = new List<JsLogLine>();
            public bool Truncated;
            readonly StringBuilder output = new StringBuilder();
            readonly int maxChars;
            readonly SessionClock clock = new SessionClock();
            readonly List<KeyValuePair<JsValue, JsValue>> unhandled = new List<KeyValuePair<JsValue, JsValue>>();
            int steps;

            public Session(JsLimits lim, long stackBudget, bool trackRejections)
            {
                maxChars = lim.MaxOutputChars;
                var deadline = Deadline;
                var stack = new StackConstraint(stackBudget);
                var heap = lim.MaxMemoryBytes > 0 ? new HeapConstraint(lim.MaxMemoryBytes) : null;
                var clk = clock;
                // JSON.stringify/parse в Jint рекурсивны: глубину ограничиваем по доступному стеку (~4 КБ на уровень)
                int jsonDepth = (int)Math.Max(64, Math.Min(5000, stackBudget / 4096));
                Engine = new Engine(o =>
                {
                    o.Constraint(deadline);
                    o.Constraint(stack);
                    if (heap != null) o.Constraint(heap);
                    o.MaxJsonParseDepth(jsonDepth);
                    if (lim.MaxStatements > 0) o.MaxStatements(lim.MaxStatements);
                    if (lim.MaxRecursion > 0) o.LimitRecursion(lim.MaxRecursion);
                    if (lim.MaxArraySize > 0) o.MaxArraySize(lim.MaxArraySize);
                    o.RegexTimeoutInterval(TimeSpan.FromSeconds(Math.Max(0.2, Math.Min(lim.TimeoutSeconds, 2.0))));
                    o.Constraints.StackOverflowGuard = true;   // там, где рантайм умеет мерить стек (в Mono — нет, см. StackConstraint)
                    o.Culture(EnUs);
                    o.LocalTimeZone(TimeZoneInfo.Utc);
                    o.TimeSystem = clk;
                    o.RetainFunctionSourceText();
                });
                if (trackRejections)
                {
                    var list = unhandled;
                    Engine.Advanced.PromiseRejectionTracker += (sender, e) =>
                    {
                        var p = e.Promise;
                        if (e.Operation == Jint.Native.Promise.PromiseRejectionOperation.Reject)
                            list.Add(new KeyValuePair<JsValue, JsValue>(p, e.Value ?? JsValue.Undefined));
                        else list.RemoveAll(x => ReferenceEquals(x.Key, p));
                    };
                }
                var sink = new ClrFunction(Engine, "sink", (thisObj, args) =>
                {
                    bool isErr = args.Length > 0 && args[0].IsNumber() && args[0].AsNumber() == 1;
                    Emit(isErr, args.Length > 1 ? args[1].ToString() : "");
                    return JsValue.Undefined;
                });
                var advance = new ClrFunction(Engine, "advance", (thisObj, args) =>
                {
                    if (args.Length > 0 && args[0].IsNumber()) clk.Advance(args[0].AsNumber());
                    return JsValue.Undefined;
                });
                var perfNow = new ClrFunction(Engine, "now", (thisObj, args) => JsNumber.Create(clk.PerfNow));
                var promiseInfo = new ClrFunction(Engine, "promiseInfo", (thisObj, args) => PromiseInfo(args));
                var fatal = new ClrFunction(Engine, "fatal", (thisObj, args) =>
                {
                    throw new UncaughtJsException(args.Length > 0 ? args[0] : JsValue.Undefined);
                });
                var prepared = PreludeScript();
#if __MonoCS__
                var factory = Engine.Evaluate(ref prepared);   // mcs (сборка/тесты) видит in-параметр как ref
#else
                var factory = Engine.Evaluate(prepared);
#endif
                var helpers = Engine.Invoke(factory, new object[] { sink, advance, perfNow, promiseInfo, fatal, jsonDepth,
                    Math.Min(jsonDepth, 500), lim.MaxTimerMs }).AsObject();
                Setup = helpers.Get("setup");
                ToJson = helpers.Get("toJson");
                Inspect = helpers.Get("inspect");
            }

            /// <summary>Один шаг цикла событий (таймер или setImmediate + все микрозадачи после него).
            /// 1 — выполнен, 0 — больше нечего делать, -1 — виртуальное время или число шагов вышло за лимит.</summary>
            public int Step()
            {
                if (++steps > MaxLoopSteps) return -1;
                double r = Engine.Evaluate("__internStep()", RunnerSource).AsNumber();
                return r == 1 ? 1 : r == 0 ? 0 : -1;
            }

            /// <summary>Необработанный reject после очередной порции микрозадач (как unhandledRejection в Node).</summary>
            public JsError TakeUnhandled()
            {
                if (unhandled.Count == 0) return null;
                var err = FromThrown(unhandled[0].Value, null, this);
                unhandled.Clear();
                err.Text = "Необработанная ошибка в Promise: " + err.Text;
                if (err.Hint == null) err.Hint = "Добавь await или .catch(...) к вызову async-функции.";
                return err;
            }

            void Emit(bool isErr, string text)
            {
                if (Truncated) return;
                if (output.Length + text.Length + 1 > maxChars)
                {
                    int room = Math.Max(0, maxChars - output.Length);
                    text = text.Substring(0, Math.Min(text.Length, room)) + "… (вывод обрезан)";
                    Truncated = true;
                }
                output.Append(text).Append('\n');
                Lines.Add(new JsLogLine { IsError = isErr, Text = text });
            }

            public string OutputText { get { return output.ToString(); } }

            public string Show(JsValue v)
            {
                try { return Engine.Invoke(Inspect, v).ToString(); }
                catch (Exception) { return v.ToString(); }
            }

            public void Dispose()
            {
                if (Engine != null) Engine.Dispose();
                Engine = null;
            }
        }

        // Состояние Promise для console.log(promise): у Jint оно внутреннее, читаем через отражение
        // (если его нет — Promise печатается как «Promise {}»). 0 — pending, 1 — fulfilled, 2 — rejected, -1 — не Promise.
        static readonly Type PromiseType = typeof(JsValue).Assembly.GetType("Jint.Native.JsPromise");
        static readonly PropertyInfo PromiseStateProp = PromiseType == null ? null
            : PromiseType.GetProperty("State", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        static readonly PropertyInfo PromiseValueProp = PromiseType == null ? null
            : PromiseType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        static JsValue PromiseInfo(JsValue[] args)
        {
            var p = args.Length > 0 ? args[0] : JsValue.Undefined;
            bool wantValue = args.Length > 1 && args[1].IsBoolean() && args[1].AsBoolean();
            if (PromiseType == null || PromiseStateProp == null || p == null || !PromiseType.IsInstanceOfType(p))
                return wantValue ? JsValue.Undefined : JsNumber.Create(-1);
            try
            {
                int state = Convert.ToInt32(PromiseStateProp.GetValue(p, null), CultureInfo.InvariantCulture);
                if (!wantValue) return JsNumber.Create(state);
                var v = state == 0 || PromiseValueProp == null ? null : PromiseValueProp.GetValue(p, null) as JsValue;
                return v ?? JsValue.Undefined;
            }
            catch (Exception)
            {
                return wantValue ? JsValue.Undefined : JsNumber.Create(-1);
            }
        }

        /// <summary>
        /// Защита от переполнения стека. Jint рекурсивен, а переполнение стека в Mono роняет или вешает весь процесс;
        /// RuntimeHelpers.TryEnsureSufficientExecutionStack в Mono не работает, а MaxRecursion ловит не все пути
        /// (valueOf, getter, Proxy, генераторы…). Поэтому меряем, насколько стек вырос с момента создания движка:
        /// разница адресов локальной переменной и статического поля (Unsafe.ByteOffset — без unsafe-кода).
        /// Проверка «амортизированная» — Jint зовёт её раз в 64 оператора и на границах вызовов.
        /// </summary>
        sealed class StackConstraint : Constraint
        {
            static byte anchor;
            readonly long baseline, budget;

            public StackConstraint(long budgetBytes)
            {
                budget = budgetBytes;
                baseline = Probe();
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static long Probe()
            {
                byte local = 0;
                return (long)Unsafe.ByteOffset(ref anchor, ref local);
            }

            public override bool IsAmortizable { get { return true; } }

            public override void Check()
            {
                long used = baseline - Probe();
                if (used < 0) used = -used;
                if (used > budget) throw new InsufficientExecutionStackException("JS stack budget exceeded");
            }

            public override void Reset() { }
        }

        /// <summary>Рост кучи с момента создания движка (GC.GetTotalMemory работает и в Boehm-GC Unity, и в SGen).</summary>
        sealed class HeapConstraint : Constraint
        {
            readonly long baseline, limit;

            public HeapConstraint(long limitBytes)
            {
                limit = limitBytes;
                baseline = GC.GetTotalMemory(false);
            }

            public override bool IsAmortizable { get { return true; } }

            public override void Check()
            {
                if (GC.GetTotalMemory(false) - baseline > limit)
                    throw new MemoryLimitExceededException("heap grew by more than " + (limit >> 20) + " MB");
            }

            public override void Reset() { }
        }

        [ThreadStatic] static long workerStackBytes;   // > 0 — мы в своём потоке с таким стеком

        static long StackBudget(JsLimits lim)
        {
            long size = workerStackBytes;
            if (size <= 0) return Math.Max(64 * 1024, lim.InlineStackBudgetBytes);
            long cap = Math.Max(size / 2, size - 4L * 1024 * 1024);   // запас на кадры C# и размотку исключения
            return Math.Max(64 * 1024, Math.Min(cap, lim.StackBudgetBytes > 0 ? lim.StackBudgetBytes : cap));
        }

        static CultureInfo enUs;
        static CultureInfo EnUs
        {
            get
            {
                if (enUs == null)
                {
                    try { enUs = CultureInfo.GetCultureInfo("en-US"); }
                    catch (Exception) { enUs = CultureInfo.InvariantCulture; }
                }
                return enUs;
            }
        }

        // ======================= потоки =======================

        static TResult OnWorker<TResult>(Func<TResult> work, JsLimits lim, int hardMs, Func<TResult> onHang, Func<Exception, TResult> onError)
            where TResult : class
        {
            if (lim.ThreadStackBytes <= 0) return Safe(work, onError);
            TResult result = null;
            Thread th;
            try
            {
                int size = lim.ThreadStackBytes;
                th = new Thread(() => { workerStackBytes = size; result = Safe(work, onError); }, size);
                th.IsBackground = true;
                th.Name = "Intern.JsRun";
                th.Start();
            }
            catch (Exception)
            {
                return Safe(work, onError);   // платформа без потоков (WebGL) — выполняем здесь же
            }
            if (!th.Join(Math.Max(1000, hardMs))) return onHang();   // поток доработает в фоне и остановится по своему лимиту
            return result ?? onHang();
        }

        static JsJob<TResult> StartJob<TResult>(Func<TResult> work, JsLimits lim, Func<Exception, TResult> onError) where TResult : class
        {
            var job = new JsJob<TResult>();
            try
            {
                int size = lim.ThreadStackBytes > 0 ? lim.ThreadStackBytes : 64 * 1024 * 1024;
                var th = new Thread(() => { workerStackBytes = size; job.Finish(Safe(work, onError)); }, size);
                th.IsBackground = true;
                th.Name = "Intern.JsRun";
                th.Start();
            }
            catch (Exception)
            {
                job.Finish(Safe(work, onError));
            }
            return job;
        }

        static TResult Safe<TResult>(Func<TResult> work, Func<Exception, TResult> onError)
        {
            try { return work(); }
            catch (Exception e) { return onError(e); }
        }

        static JsError Hang()
        {
            return Err("Hang", "watchdog", "Проверка зависла и была остановлена.",
                "Похоже на очень долгую операцию или бесконечный цикл — упрости код и попробуй ещё раз.");
        }

        static JsError InternalError(Exception ex)
        {
            return Err("Internal", ex.GetType().Name + ": " + ex.Message,
                "Внутренняя ошибка проверки: " + ex.GetType().Name + ": " + ex.Message, "Сообщи разработчикам :)");
        }

        // ======================= ошибки =======================

        static JsError Err(string type, string message, string text, string hint)
        {
            return new JsError { Type = type, Message = message, Text = text, Hint = hint };
        }

        static string Sec(double s)
        {
            return s.ToString("0.#", CultureInfo.InvariantCulture) + " с";
        }

        static JsError FromException(Exception ex, Session s, JsLimits lim, double budgetSec = 0)
        {
            var prep = ex as ScriptPreparationException;
            if (prep != null && prep.InnerException != null) ex = prep.InnerException;

            var parse = ex as Acornima.ParseErrorException;
            if (parse != null)
            {
                var e = new JsError { Type = "SyntaxError", Message = parse.Description, Line = parse.LineNumber, Column = parse.Column };
                e.Text = "SyntaxError в строке " + e.Line + ": " + e.Message;
                e.Hint = HintFor(e.Message);
                return e;
            }

            var js = ex as JavaScriptException;
            if (js != null) return FromThrown(js.Error, js, s);

            var uncaught = ex as UncaughtJsException;
            if (uncaught != null) return FromThrown(uncaught.Value, null, s);

            if (ex is StatementsCountOverflowException)
                return Err("StepLimit", ex.Message,
                    "Слишком много шагов (больше " + (lim != null ? lim.MaxStatements.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ") : "лимита") + ") — похоже на бесконечный цикл.",
                    "Проверь, что условие цикла когда-нибудь становится ложным, а счётчик меняется.");
            if (ex is TimeoutException || ex is OperationCanceledException || ex is ExecutionCanceledException)
                return Err("Timeout", ex.Message,
                    "Превышено время: код работал дольше " + Sec(budgetSec > 0 ? budgetSec : (lim != null ? lim.TimeoutSeconds : 2)) + " — похоже на бесконечный цикл.",
                    "Проверь условия выхода из циклов и рекурсии.");
            var rec = ex as RecursionDepthOverflowException;
            if (rec != null)
                return Err("RecursionLimit", ex.Message,
                    "Слишком глубокая рекурсия (больше " + (lim != null ? lim.MaxRecursion : 0) + " вложенных вызовов" +
                    (string.IsNullOrEmpty(rec.CallExpressionReference) || rec.CallExpressionReference == "apply" ? "" : ", вызов " + rec.CallExpressionReference) + ").",
                    "Проверь базовый случай — условие, при котором функция перестаёт вызывать саму себя.");
            if (ex is MemoryLimitExceededException || ex is OutOfMemoryException)
                return Err("MemoryLimit", ex.Message, "Код израсходовал слишком много памяти.",
                    "Похоже, массив или строка растут без остановки (например, в бесконечном цикле).");
            if (ex is InsufficientExecutionStackException || ex is StackOverflowException)
                return Err("RecursionLimit", ex.Message, "Слишком глубокая рекурсия — не хватило стека.",
                    "Проверь базовый случай — условие, при котором функция перестаёт вызывать саму себя.");
            var rej = ex as PromiseRejectedException;
            if (rej != null) return FromThrown(rej.RejectedValue, null, s);

            return InternalError(ex);
        }

        /// <summary>Выброшенное JS-значение (throw / reject) → ошибка с типом, сообщением и строкой.</summary>
        static JsError FromThrown(JsValue thrown, Exception ex, Session s)
        {
            var e = new JsError { Type = "Error" };
            string stack = null;
            bool isErrorObject = false;
            if (thrown != null && thrown.IsObject())
            {
                try
                {
                    var obj = thrown.AsObject();
                    JsValue name = obj.Get("name"), msg = obj.Get("message"), st = obj.Get("stack");
                    if (name.IsString() && msg.IsString())
                    {
                        isErrorObject = true;
                        e.Type = name.ToString().Length > 0 ? name.ToString() : "Error";
                        e.Message = msg.ToString();
                    }
                    if (st.IsString()) stack = st.ToString();
                }
                catch (Exception) { }
            }
            if (!isErrorObject)
            {
                e.Type = "Throw";
                e.Message = thrown == null ? "undefined" : (s != null ? s.Show(thrown) : thrown.ToString());
            }

            // строка: где выброшено (для throw), иначе — из стека ошибки
            Acornima.SourceLocation loc;
            if (ex != null && JintException.TryGetJavaScriptLocation(ex, out loc) && loc.SourceFile == SourceName && loc.Start.Line > 0)
            {
                e.Line = loc.Start.Line;
                e.Column = loc.Start.Column + 1;
            }
            if (e.Line == 0 && stack != null)
            {
                var m = StackPosRe.Match(stack);
                if (m.Success)
                {
                    e.Line = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    e.Column = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                }
            }
            if (e.Line == 0 && ex != null)
            {
                string callStack;
                if (JintException.TryGetJavaScriptCallStack(ex, out callStack) && callStack != null)
                {
                    var m = StackPosRe.Match(callStack);
                    if (m.Success)
                    {
                        e.Line = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        e.Column = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                }
            }

            string where = e.Line > 0 ? " (строка " + e.Line + ")" : "";
            if (!isErrorObject) e.Text = "Выброшено значение " + e.Message + where;
            else if (e.Type == "SyntaxError" && e.Line > 0) e.Text = "SyntaxError в строке " + e.Line + ": " + e.Message;
            else e.Text = e.Type + ": " + e.Message + where;
            e.Hint = HintFor(e.Message);
            return e;
        }

        static readonly KeyValuePair<Regex, string>[] Hints =
        {
            H(@"^(\S+) is not defined", "«$1» не объявлена: проверь имя (регистр букв важен) и объявление через let/const/function."),
            H(@"Cannot read propert(y|ies) .*(undefined|null)", "Берёшь свойство у undefined/null: проверь, что переменная получила значение и индекс не вышел за границы массива."),
            H(@"Cannot set propert(y|ies) .*(undefined|null)", "Записываешь свойство в undefined/null: сначала создай объект или массив."),
            H(@"is not a function", "Вызываешь как функцию то, что функцией не является: проверь имя метода и тип значения."),
            H(@"is not a constructor", "Через new можно создавать только классы и функции-конструкторы."),
            H(@"Assignment to constant variable", "const нельзя переприсвоить — объяви переменную через let."),
            H(@"before initialization", "Переменная используется раньше строки, где она объявлена через let/const."),
            H(@"has already been declared", "Такое имя уже объявлено — переименуй переменную или убери повторный let/const."),
            H(@"is not iterable", "for...of, spread (...) и деструктуризация массива работают только с массивами, строками, Map и Set."),
            H(@"Maximum call stack size exceeded", "Слишком глубокая рекурсия: проверь базовый случай — условие выхода из рекурсии."),
            H(@"Invalid array length", "Длина массива должна быть целым неотрицательным числом."),
            H(@"Invalid string length", "Строка получилась слишком длинной — похоже, она растёт в бесконечном цикле."),
            H(@"argument must be of type function", "Сюда нужно передать функцию (колбэк), а не результат её вызова."),
            H(@"await is only valid|Cannot use keyword 'await'|await.*async", "await можно писать только внутри async-функции."),
            H(@"Unexpected end of input", "Код оборвался: не хватает закрывающей скобки }, ) или ]."),
            H(@"Unterminated string|Invalid or unexpected token", "Похоже, не закрыта кавычка или встретился неожиданный символ."),
            H(@"Unterminated template", "Не закрыта обратная кавычка ` у шаблонной строки."),
            H(@"Unexpected token|Unexpected identifier|Unexpected number|Unexpected string", "Лишний или пропущенный символ: проверь скобки, запятые и точки с запятой рядом."),
            H(@"Invalid left-hand side in assignment", "Слева от = должно стоять имя переменной или свойство (сравнение пишется === )."),
            H(@"Illegal return statement", "return можно писать только внутри функции."),
            H(@"Illegal break statement|Illegal continue statement", "break/continue можно писать только внутри цикла (или switch)."),
        };

        static KeyValuePair<Regex, string> H(string pattern, string hint)
        {
            return new KeyValuePair<Regex, string>(new Regex(pattern, RegexOptions.IgnoreCase), hint);
        }

        static string HintFor(string message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            foreach (var h in Hints)
            {
                var m = h.Key.Match(message);
                if (m.Success) return m.Result(h.Value);
            }
            return null;
        }

        // ======================= сравнение и пояснения =======================

        static bool IsNum(object o)
        {
            return o is long || o is double || o is int || o is float || o is decimal || o is short || o is byte
                || o is sbyte || o is ushort || o is uint || o is ulong;
        }

        static double ToD(object o)
        {
            return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        // "\u0001NaN", "\u0001Infinity", "\u0001-Infinity", "\u0001123" (BigInt) → числа
        static object FixMarkers(object v)
        {
            var s = v as string;
            if (s != null)
            {
                if (s.Length == 0 || s[0] != '\u0001') return v;
                string r = s.Substring(1);
                if (r == "NaN") return double.NaN;
                if (r == "Infinity") return double.PositiveInfinity;
                if (r == "-Infinity") return double.NegativeInfinity;
                long l;
                if (long.TryParse(r, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out l)) return l;
                double d;
                if (double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return r;
            }
            var list = v as List<object>;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) list[i] = FixMarkers(list[i]);
                return list;
            }
            var dict = v as Dictionary<string, object>;
            if (dict != null)
            {
                var ks = new List<string>(dict.Keys);
                foreach (var k in ks) dict[k] = FixMarkers(dict[k]);
                return dict;
            }
            return v;
        }

        static string Explain(object expected, object got, bool returnedUndefined)
        {
            if (returnedUndefined && expected != null) return "Функция ничего не вернула (undefined). Не забыл return?";
            return FirstDiff(expected, got, "");
        }

        static string FirstDiff(object e, object g, string path)
        {
            if (JsonEquals(e, g)) return null;
            string where = path.Length == 0 ? "" : "В " + path + ": ";
            var ed = e as IDictionary<string, object>;
            var gd = g as IDictionary<string, object>;
            if (ed != null && gd != null)
            {
                foreach (var k in ed.Keys)
                    if (!gd.ContainsKey(k)) return where + "не хватает ключа «" + k + "».";
                foreach (var k in gd.Keys)
                    if (!ed.ContainsKey(k)) return where + "лишний ключ «" + k + "».";
                foreach (var kv in ed)
                {
                    var r = FirstDiff(kv.Value, gd[kv.Key], path + Prop(kv.Key));
                    if (r != null) return r;
                }
                return where + "объекты отличаются.";
            }
            var el = e as IList;
            var gl = g as IList;
            if (el != null && gl != null)
            {
                int n = Math.Min(el.Count, gl.Count);
                for (int i = 0; i < n; i++)
                {
                    var r = FirstDiff(el[i], gl[i], path + "[" + i + "]");
                    if (r != null) return r;
                }
                if (el.Count != gl.Count)
                    return where + (gl.Count > el.Count ? "лишние элементы" : "не хватает элементов") +
                        ": ожидалось элементов — " + el.Count + ", получилось — " + gl.Count + ".";
                return where + "массивы отличаются.";
            }
            string ke = Kind(e), kg = Kind(g);
            string s = where + "ожидалось " + Short(e) + ", получилось " + Short(g);
            if (ke != kg) s += " (получилось " + kg + ", а нужно " + ke + ")";
            else if (e is string && string.Equals((string)e, (string)g, StringComparison.OrdinalIgnoreCase))
                s += " — отличаются только большие/маленькие буквы";
            else if (e is string && ((string)e).Trim() == ((string)g).Trim())
                s += " — отличаются пробелы по краям";
            else if (e is long && g is double && !double.IsNaN((double)g) && Math.Floor((double)g) != (double)g)
                s += " — получилось дробное число, а нужно целое";
            return s + ".";
        }

        static string Prop(string key)
        {
            return Regex.IsMatch(key, @"^[A-Za-z_$][A-Za-z0-9_$]*$") ? "." + key : "[" + MiniJson.Quote(key) + "]";
        }

        static string Kind(object v)
        {
            if (v == null) return "null";
            if (v is bool) return "true/false";
            if (v is string) return "строка";
            if (IsNum(v)) return "число";
            if (v is IDictionary<string, object>) return "объект";
            if (v is IList) return "массив";
            return v.GetType().Name;
        }

        static string Short(object v)
        {
            string s = MiniJson.Serialize(v);
            return s.Length > 80 ? s.Substring(0, 77) + "…" : s;
        }
    }
}
