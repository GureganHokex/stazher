// Спринт 10 «Языки: Go»: компилируемые языки запускаются в контейнере языка (официальный образ) рядом с песочницей.
// Контейнер stazher-<язык> живёт, пока идёт игра: папка work смонтирована как /work, сети нет, память и число
// процессов ограничены, кэш сборки лежит в томе stazher-<язык>-cache (второй запуск в разы быстрее первого).
// Код игрока и тесты пишутся в work/.stazher/run/<задача>, команда выполняется через docker exec с timeout.
// Образ скачивается при первом запуске и попадает в список «скачанных в игре» — «Удалить всё» уберёт и его.
// Unity API здесь нет: всё можно звать из фонового потока.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Intern.Py;

namespace Intern.Game
{
    public class LangSpec
    {
        public string id, name, image, container, cacheVolume, cachePath, src, test, runCmd, testCmd, size;   // size — место на диске после docker pull
        public string parser = "go";     // go — go test -json; check — строки ##TEST|имя|PASS/FAIL (свой набор проверок языка)
        public string testShow, runShow; // что показать игроку в терминале вместо полной команды
        public string testMarker;        // без этого в файле тестов задача считается сломанной (selftest)
        public string[] env = new string[0];
        public Dictionary<string, string> extraFiles = new Dictionary<string, string>();
    }

    // Итог проверки тестами: строки для вкладки «Тесты», строка ошибки для редактора, вывод для терминала
    public class BoxReport
    {
        public List<CheckResult> results = new List<CheckResult>();
        public int errLine = -1;
        public string errText, log = "", setupError;
        public bool buildFailed, panicked, timedOut, pulled;
        public double ms;
    }

    public class BoxRun
    {
        public string output = "", setupError;
        public int code = -1, errLine = -1;
        public string errText;
        public bool timedOut, pulled;
        public double ms;
    }

    // Фоновая работа с результатом и строками хода работы (скачиваю образ, запускаю контейнер…)
    public sealed class BoxJob<T> where T : class
    {
        volatile bool done; T result;
        readonly Queue<string> notes = new Queue<string>();
        public bool IsDone { get { return done; } }
        public T Result { get { return result; } }
        public void Note(string s) { lock (notes) notes.Enqueue(s); }
        public List<string> TakeNotes() { lock (notes) { var l = new List<string>(notes); notes.Clear(); return l; } }
        public static BoxJob<T> Start(Func<BoxJob<T>, T> work)
        {
            var j = new BoxJob<T>();
            new Thread(() =>
            {
                T r = null;
                try { r = work(j); }
                catch (Exception e) { j.Note("Внутренняя ошибка раннера: " + e.Message); }
                j.result = r; j.done = true;
            }) { IsBackground = true }.Start();
            return j;
        }
    }

    public static class LangBox
    {
        public const int RunTimeoutSec = 20;
        public const int MaxOutput = 20000;

        public static readonly LangSpec Go = new LangSpec
        {
            id = "go", name = "Go", image = "golang:1.24-alpine", container = "stazher-go",
            cacheVolume = "stazher-go-cache", cachePath = "/root/.cache",
            src = "main.go", test = "main_test.go", runCmd = "go run .", testCmd = "go test -json -count=1 .", size = "около 400 МБ",
            testShow = "go test -v", runShow = "go run .", testMarker = "func Test",
            env = new[] { "GOTOOLCHAIN=local", "GOFLAGS=-mod=mod", "GOPROXY=off", "CGO_ENABLED=0" },
            extraFiles = new Dictionary<string, string> { { "go.mod", "module stazher\n\ngo 1.24\n" } },
        };

        // Спринт 12: Java. Тесты — MainTest.java на своём маленьком наборе проверок Check.java (без JUnit: сети в контейнере нет)
        public static readonly LangSpec Java = new LangSpec
        {
            id = "java", name = "Java", image = "eclipse-temurin:21-jdk-alpine", container = "stazher-java",
            src = "Main.java", test = "MainTest.java", parser = "check", size = "около 550 МБ",
            runCmd = "sh -c \"rm -rf out && javac -J-Xmx256m -encoding UTF-8 -d out Main.java && java -Xmx256m -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp out Main\"",
            testCmd = "sh -c \"rm -rf out && javac -J-Xmx256m -encoding UTF-8 -d out *.java && java -Xmx256m -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -cp out MainTest\"",
            testShow = "javac *.java && java MainTest", runShow = "javac Main.java && java Main", testMarker = "Check.",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "Check.java", CheckJava } },
        };

        // Спринт 13: C#. Компилятор Roslyn (csc) из SDK вызывается напрямую — без msbuild и NuGet сборка занимает пару секунд и не требует сети.
        // Тесты — Tests.cs на своём наборе проверок Check.cs; частые using подключены через Usings.cs, как в новом проекте dotnet
        public static readonly LangSpec CSharp = new LangSpec
        {
            id = "csharp", name = "C#", image = "mcr.microsoft.com/dotnet/sdk:10.0-alpine", container = "stazher-csharp",
            src = "Program.cs", test = "Tests.cs", parser = "check", size = "около 1,1 ГБ",
            runCmd = "sh stazher-cs.sh run", testCmd = "sh stazher-cs.sh test",
            testShow = "csc Program.cs Tests.cs && dotnet Tests.dll", runShow = "csc Program.cs && dotnet Program.dll", testMarker = "Check.",
            env = new[] { "LANG=C.UTF-8", "DOTNET_CLI_TELEMETRY_OPTOUT=1", "DOTNET_NOLOGO=1", "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1" },
            extraFiles = new Dictionary<string, string> { { "Check.cs", CheckCs }, { "Usings.cs", UsingsCs }, { "stazher-cs.sh", CsScript } },
        };

        public static LangSpec For(string lang) { return lang == "go" ? Go : lang == "java" ? Java : lang == "csharp" ? CSharp : null; }
        public static IEnumerable<LangSpec> All { get { yield return Go; yield return Java; yield return CSharp; } }

        // Tools/s13/Check.cs — набор проверок для Tests.cs
        public const string CheckCs =
            "// Проверки «Стажёра» для задач на C#: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Исключение в проверяемом коде ловится внутри проверки — остальные тесты всё равно выполнятся.\n" +
            "using System.Collections;\n" +
            "using System.Diagnostics;\n" +
            "using System.Globalization;\n" +
            "using System.Text;\n" +
            "\n" +
            "public static class Check\n" +
            "{\n" +
            "    public static void Eq(string name, Func<object> got, object want)\n" +
            "    {\n" +
            "        object g;\n" +
            "        try { g = got(); } catch (Exception e) { Fail(name, \"исключение \" + Ex(e)); return; }\n" +
            "        if (Same(g, want)) Pass(name); else Fail(name, \"получено \" + Show(g) + \", ожидалось \" + Show(want));\n" +
            "    }\n" +
            "\n" +
            "    public static void Near(string name, Func<double> got, double want)\n" +
            "    {\n" +
            "        double g;\n" +
            "        try { g = got(); } catch (Exception e) { Fail(name, \"исключение \" + Ex(e)); return; }\n" +
            "        if (Math.Abs(g - want) < 1e-9) Pass(name); else Fail(name, \"получено \" + Show(g) + \", ожидалось \" + Show(want));\n" +
            "    }\n" +
            "\n" +
            "    public static void Ok(string name, Func<bool> cond, string why)\n" +
            "    {\n" +
            "        try { if (cond()) Pass(name); else Fail(name, why); }\n" +
            "        catch (Exception e) { Fail(name, \"исключение \" + Ex(e)); }\n" +
            "    }\n" +
            "\n" +
            "    public static void Throws<T>(string name, Action act) where T : Exception\n" +
            "    {\n" +
            "        try { act(); Fail(name, \"исключения не было, ожидалось \" + typeof(T).Name); }\n" +
            "        catch (Exception e)\n" +
            "        {\n" +
            "            if (e is T) Pass(name);\n" +
            "            else Fail(name, \"исключение \" + e.GetType().Name + \", ожидалось \" + typeof(T).Name + Where(e));\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    static bool Integral(object o) { return o is int || o is long || o is short || o is byte || o is sbyte || o is uint || o is ushort; }\n" +
            "    static bool Real(object o) { return o is double || o is float || o is decimal; }\n" +
            "\n" +
            "    static bool Same(object a, object b)\n" +
            "    {\n" +
            "        if (a == null || b == null) return a == b;\n" +
            "        if (Integral(a) && Integral(b)) return Convert.ToInt64(a) == Convert.ToInt64(b);\n" +
            "        if ((Real(a) || Integral(a)) && (Real(b) || Integral(b)) && (Real(a) || Real(b)))\n" +
            "            return Math.Abs(Convert.ToDouble(a) - Convert.ToDouble(b)) < 1e-9;\n" +
            "        if (a is string || b is string) return Equals(a, b);\n" +
            "        if (a is IDictionary da && b is IDictionary db)\n" +
            "        {\n" +
            "            if (da.Count != db.Count) return false;\n" +
            "            foreach (DictionaryEntry e in da) { if (!db.Contains(e.Key) || !Same(e.Value, db[e.Key])) return false; }\n" +
            "            return true;\n" +
            "        }\n" +
            "        if (a is IEnumerable ea && b is IEnumerable eb && !(a is IDictionary) && !(b is IDictionary))\n" +
            "        {\n" +
            "            var la = ea.Cast<object>().ToList(); var lb = eb.Cast<object>().ToList();\n" +
            "            if (la.Count != lb.Count) return false;\n" +
            "            for (int i = 0; i < la.Count; i++) if (!Same(la[i], lb[i])) return false;\n" +
            "            return true;\n" +
            "        }\n" +
            "        return Equals(a, b);\n" +
            "    }\n" +
            "\n" +
            "    static string Show(object o)\n" +
            "    {\n" +
            "        if (o == null) return \"null\";\n" +
            "        if (o is string s) return \"\\\"\" + s + \"\\\"\";\n" +
            "        if (o is char c) return \"'\" + c + \"'\";\n" +
            "        if (o is bool bo) return bo ? \"true\" : \"false\";\n" +
            "        if (o is double d) return d.ToString(\"R\", CultureInfo.InvariantCulture);\n" +
            "        if (o is float f) return f.ToString(\"R\", CultureInfo.InvariantCulture);\n" +
            "        if (o is decimal m) return m.ToString(CultureInfo.InvariantCulture);\n" +
            "        if (o is IDictionary dict)\n" +
            "        {\n" +
            "            var parts = new List<string>();\n" +
            "            foreach (DictionaryEntry e in dict) parts.Add(Show(e.Key) + \": \" + Show(e.Value));\n" +
            "            parts.Sort(StringComparer.Ordinal);\n" +
            "            return \"{\" + string.Join(\", \", parts) + \"}\";\n" +
            "        }\n" +
            "        if (o is IEnumerable en) return \"[\" + string.Join(\", \", en.Cast<object>().Select(Show)) + \"]\";\n" +
            "        return Convert.ToString(o, CultureInfo.InvariantCulture);\n" +
            "    }\n" +
            "\n" +
            "    static string Ex(Exception e) { return e.GetType().Name + \": \" + e.Message + Where(e); }\n" +
            "\n" +
            "    static string Where(Exception e)\n" +
            "    {\n" +
            "        var st = new StackTrace(e, true);\n" +
            "        foreach (var fr in st.GetFrames() ?? new StackFrame[0])\n" +
            "        {\n" +
            "            var file = fr.GetFileName();\n" +
            "            if (file != null && Path.GetFileName(file) == \"Program.cs\" && fr.GetFileLineNumber() > 0) return \" (Program.cs:\" + fr.GetFileLineNumber() + \")\";\n" +
            "        }\n" +
            "        return \"\";\n" +
            "    }\n" +
            "\n" +
            "    static void Pass(string n) { Console.WriteLine(\"##TEST|\" + Clean(n) + \"|PASS\"); }\n" +
            "    static void Fail(string n, string m) { Console.WriteLine(\"##TEST|\" + Clean(n) + \"|FAIL|\" + Clean(m)); }\n" +
            "    static string Clean(string s) { return (s ?? \"\").Replace(\"|\", \"/\").Replace(\"\\r\", \"\").Replace(\"\\n\", \" ⏎ \"); }\n" +
            "}\n";

        // Tools/s13/Usings.cs — global using, как ImplicitUsings в проекте dotnet
        public const string UsingsCs =
            "// Как в новом проекте dotnet (ImplicitUsings): частые пространства имён подключены сами\n" +
            "global using System;\n" +
            "global using System.Collections.Generic;\n" +
            "global using System.IO;\n" +
            "global using System.Linq;\n" +
            "global using System.Net.Http;\n" +
            "global using System.Threading;\n" +
            "global using System.Threading.Tasks;\n";

        // Tools/s13/stazher-cs.sh — сборка csc без msbuild и NuGet (сети в контейнере нет) и запуск через dotnet
        public const string CsScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: сборка и запуск C# без msbuild и NuGet — компилятор Roslyn (csc) из .NET SDK, запуск через dotnet.\n" +
            "# sh stazher-cs.sh run  — Program.cs;  sh stazher-cs.sh test — Program.cs + Tests.cs + Check.cs\n" +
            "D=${DOTNET_ROOT:-$(dirname \"$(readlink -f \"$(command -v dotnet)\")\")}\n" +
            "CSC=$(ls -d \"$D\"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | tail -n 1)\n" +
            "REF=$(ls -d \"$D\"/packs/Microsoft.NETCore.App.Ref/*/ref/net* 2>/dev/null | tail -n 1)\n" +
            "[ -n \"$CSC\" ] && [ -n \"$REF\" ] || { echo \"stazher: не найден компилятор csc или сборки .NET в $D\" >&2; exit 2; }\n" +
            "rm -rf out && mkdir out || exit 2\n" +
            "if [ \"$1\" = test ]; then NAME=Tests; SRC=\"Program.cs Tests.cs Check.cs Usings.cs\"; MAIN=-main:Tests; else NAME=Program; SRC=\"Program.cs Usings.cs\"; MAIN=; fi\n" +
            "for f in \"$REF\"/*.dll; do echo \"-r:$f\"; done > out/refs.rsp\n" +
            "dotnet \"$CSC\" -nologo -noconfig -langversion:latest -nullable:disable -debug:portable -target:exe -nowarn:CS8933 $MAIN -out:out/$NAME.dll @out/refs.rsp $SRC >out/csc.txt 2>&1\n" +
            "code=$?\n" +
            "grep -v \"^$\" out/csc.txt | grep -v \"warning CS\" >&2\n" +
            "[ $code -eq 0 ] || exit 1\n" +
            "printf '{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}' > out/$NAME.runtimeconfig.json\n" +
            "exec dotnet out/$NAME.dll\n";

        public const string CheckJava =
            "// Проверки «Стажёра» для задач на Java: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Исключение в проверяемом коде ловится внутри проверки — остальные тесты всё равно выполнятся.\n" +
            "import java.util.Arrays;\n" +
            "import java.util.Objects;\n" +
            "import java.util.function.Supplier;\n" +
            "\n" +
            "public final class Check {\n" +
            "    private Check() {}\n" +
            "\n" +
            "    public static void eq(String name, Supplier<?> got, Object want) {\n" +
            "        Object g;\n" +
            "        try { g = got.get(); } catch (Throwable e) { fail(name, \"исключение \" + e + where(e)); return; }\n" +
            "        if (same(g, want)) pass(name); else fail(name, \"получено \" + show(g) + \", ожидалось \" + show(want));\n" +
            "    }\n" +
            "\n" +
            "    public static void near(String name, Supplier<? extends Number> got, double want) {\n" +
            "        Number g;\n" +
            "        try { g = got.get(); } catch (Throwable e) { fail(name, \"исключение \" + e + where(e)); return; }\n" +
            "        if (g != null && Math.abs(g.doubleValue() - want) < 1e-9) pass(name); else fail(name, \"получено \" + g + \", ожидалось \" + want);\n" +
            "    }\n" +
            "\n" +
            "    public static void ok(String name, Supplier<Boolean> cond, String why) {\n" +
            "        try { if (Boolean.TRUE.equals(cond.get())) pass(name); else fail(name, why); }\n" +
            "        catch (Throwable e) { fail(name, \"исключение \" + e + where(e)); }\n" +
            "    }\n" +
            "\n" +
            "    public static void throwsEx(String name, Runnable r, Class<? extends Throwable> type) {\n" +
            "        try { r.run(); fail(name, \"исключения не было, ожидалось \" + type.getSimpleName()); }\n" +
            "        catch (Throwable e) {\n" +
            "            if (type.isInstance(e)) pass(name);\n" +
            "            else fail(name, \"исключение \" + e.getClass().getSimpleName() + \", ожидалось \" + type.getSimpleName() + where(e));\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    static boolean same(Object a, Object b) {\n" +
            "        if (a == null || b == null) return a == b;\n" +
            "        if (a.getClass().isArray() || b.getClass().isArray()) return Arrays.deepEquals(new Object[] { a }, new Object[] { b });\n" +
            "        if (a instanceof Number && b instanceof Number && !(a instanceof Double) && !(b instanceof Double) && !(a instanceof Float))\n" +
            "            return ((Number) a).longValue() == ((Number) b).longValue();\n" +
            "        return Objects.equals(a, b);\n" +
            "    }\n" +
            "\n" +
            "    static String show(Object o) {\n" +
            "        if (o == null) return \"null\";\n" +
            "        if (o instanceof String) return \"\\\"\" + o + \"\\\"\";\n" +
            "        if (o instanceof Character) return \"'\" + o + \"'\";\n" +
            "        if (o.getClass().isArray()) { String s = Arrays.deepToString(new Object[] { o }); return s.substring(1, s.length() - 1); }\n" +
            "        return String.valueOf(o);\n" +
            "    }\n" +
            "\n" +
            "    static String where(Throwable e) {\n" +
            "        for (StackTraceElement s : e.getStackTrace())\n" +
            "            if (\"Main.java\".equals(s.getFileName())) return \" (Main.java:\" + s.getLineNumber() + \")\";\n" +
            "        return \"\";\n" +
            "    }\n" +
            "\n" +
            "    static void pass(String n) { System.out.println(\"##TEST|\" + clean(n) + \"|PASS\"); }\n" +
            "    static void fail(String n, String m) { System.out.println(\"##TEST|\" + clean(n) + \"|FAIL|\" + clean(m)); }\n" +
            "    static String clean(String s) { return String.valueOf(s).replace(\"|\", \"/\").replace(\"\\r\", \"\").replace(\"\\n\", \" ⏎ \"); }\n" +
            "}\n" +
            "";

        // ---------- контейнер языка ----------
        public static bool HasImage(LangSpec s) { return DevEnv.DockerCmd("image inspect --format \"{{.Id}}\" " + s.image, 15000).Ok; }

        public static bool EnsureContainer(LangSpec s, out string error)
        {
            error = null;
            Directory.CreateDirectory(DevEnv.WorkDir);
            var st = DevEnv.DockerCmd("inspect --format \"{{.State.Running}}\" " + s.container, 15000);
            if (st.Ok && st.Out.Trim() == "true") return true;
            if (st.Ok)
            {
                if (DevEnv.DockerCmd("start " + s.container, 30000).Ok) return true;
                DevEnv.DockerCmd("rm -f " + s.container, 30000);
            }
            if (!string.IsNullOrEmpty(s.cacheVolume)) DevEnv.DockerCmd("volume create --label " + DevEnv.Label + " " + s.cacheVolume, 30000);
            var sb = new StringBuilder("run -d --name " + s.container + " --label " + DevEnv.Label + " --network none --memory 1g --pids-limit 256");
            foreach (var e in s.env) sb.Append(" -e ").Append(e);
            sb.Append(" -v \"").Append(DevEnv.WorkDir).Append(":/work\"");
            if (!string.IsNullOrEmpty(s.cacheVolume)) sb.Append(" -v ").Append(s.cacheVolume).Append(':').Append(s.cachePath);
            sb.Append(" -w /work ").Append(s.image).Append(" tail -f /dev/null");
            var r = DevEnv.DockerCmd(sb.ToString(), 60000);
            if (!r.Ok) { error = r.Text; return false; }
            return true;
        }

        // Образ на месте и контейнер запущен; pulled — образ пришлось скачать
        static string Prepare(LangSpec s, Action<string> note, out bool pulled)
        {
            pulled = false;
            if (!HasImage(s))
            {
                if (note != null) note("docker pull " + s.image + " — первый запуск " + s.name + ": скачиваю образ компилятора (на диске займёт " + s.size + "), это один раз.");
                var p = DevEnv.DockerCmd("pull " + s.image, 20 * 60 * 1000);
                if (!p.Ok) return "Не удалось скачать образ " + s.image + ": " + FirstLine(p.Text) + ". Проверь интернет и что Docker Desktop запущен.";
                pulled = true;
            }
            string err;
            if (!EnsureContainer(s, out err)) return "Контейнер " + s.container + " не запустился: " + FirstLine(err);
            return null;
        }

        // ---------- папка запуска ----------
        public static string SafeId(string id) { return Regex.Replace(string.IsNullOrEmpty(id) ? "task" : id, "[^A-Za-z0-9_-]", "_"); }
        public static string HostDir(string id) { return Path.Combine(Path.Combine(Path.Combine(DevEnv.WorkDir, ".stazher"), "run"), SafeId(id)); }
        public static string BoxDir(string id) { return "/work/.stazher/run/" + SafeId(id); }

        // Файлы задачи в папку запуска: старые исходники оттуда убираются, чтобы не мешали сборке
        public static void WriteFiles(LangSpec s, string id, IDictionary<string, string> files)
        {
            var dir = HostDir(id);
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir)) { try { File.Delete(f); } catch (Exception) { } }
            var enc = new UTF8Encoding(false);
            foreach (var kv in s.extraFiles) File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value.Replace("\r\n", "\n"), enc);   // CRLF сломал бы sh-скрипт
            foreach (var kv in files) File.WriteAllText(Path.Combine(dir, kv.Key), (kv.Value ?? "").Replace("\r\n", "\n"), enc);
        }

        public static ProcResult Exec(LangSpec s, string id, string cmd, int timeoutSec)
        {
            var r = DevEnv.Run(DevEnv.Docker, "exec -w " + BoxDir(id) + " " + s.container + " timeout -s KILL " + timeoutSec + " " + cmd, (timeoutSec + 20) * 1000);
            if (r.Code == 137) r.TimedOut = true;   // timeout -s KILL (или память кончилась)
            return r;
        }

        // ---------- проверка тестами и запуск ----------
        public static BoxReport Test(LangSpec s, string id, string code, string testCode, Action<string> note)
        {
            var rep = new BoxReport();
            bool pulled;
            rep.setupError = Prepare(s, note, out pulled); rep.pulled = pulled;
            if (rep.setupError != null) return rep;
            WriteFiles(s, id, new Dictionary<string, string> { { s.src, code }, { s.test, testCode } });
            if (note != null) note(s.testShow + "   # в контейнере " + s.image);
            var r = Exec(s, id, s.testCmd, RunTimeoutSec + 10);
            rep = s.parser == "check" ? ParseCheck(r.Out, r.Err, s.src, r.TimedOut ? 0 : r.Code) : ParseGoTest(r.Out + "\n" + r.Err, s.src);
            rep.pulled = pulled; rep.ms = r.Ms;
            if (r.TimedOut && !rep.buildFailed)
            {
                rep.timedOut = true;
                rep.results.Add(new CheckResult { Passed = false, InputsText = "время", Note = "Тесты не уложились в " + (RunTimeoutSec + 10) + " с — похоже на бесконечный цикл (или программе не хватило памяти)." });
            }
            if (rep.results.Count == 0 && rep.setupError == null)
                rep.results.Add(new CheckResult { Passed = false, InputsText = "тесты", Note = "Тесты не запустились: " + FirstLine(r.Text) });
            return rep;
        }

        public static BoxRun Run(LangSpec s, string id, string code, Action<string> note)
        {
            var res = new BoxRun();
            bool pulled;
            res.setupError = Prepare(s, note, out pulled); res.pulled = pulled;
            if (res.setupError != null) return res;
            WriteFiles(s, id, new Dictionary<string, string> { { s.src, code } });
            var r = Exec(s, id, s.runCmd, RunTimeoutSec);
            res.code = r.Code; res.timedOut = r.TimedOut; res.ms = r.Ms;
            string outp = r.Out + (r.Err.Length > 0 ? (r.Out.Length > 0 && !r.Out.EndsWith("\n") ? "\n" : "") + r.Err : "");
            if (outp.Length > MaxOutput) outp = outp.Substring(0, MaxOutput) + "\n… вывод обрезан …";
            res.output = CleanPaths(outp);
            string msg; int line = ErrorLine(r.Err, s.src, out msg);
            if (line > 0) { res.errLine = line; res.errText = msg; }
            return res;
        }

        // ---------- разбор go test -json ----------
        static readonly Regex SrcErr = new Regex(@"(?:^|\s)(?:\./)?(main(?:_test)?\.go):(\d+)(?::(\d+))?:?\s*(.*)$");
        static readonly Regex TraceLine = new Regex(@"/(main(?:_test)?\.go):(\d+)");
        static readonly Regex TestMsg = new Regex(@"^\s*main_test\.go:\d+:\s?");

        public static BoxReport ParseGoTest(string output, string srcName)
        {
            var rep = new BoxReport();
            var order = new List<string>();
            var status = new Dictionary<string, string>();
            var outBy = new Dictionary<string, StringBuilder>();
            var parents = new HashSet<string>();
            var build = new StringBuilder(); var pkg = new StringBuilder(); var log = new StringBuilder();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0) continue;
                Dictionary<string, object> o = null;
                if (line.StartsWith("{")) { try { o = TestJson.Parse(line) as Dictionary<string, object>; } catch (Exception) { o = null; } }
                if (o == null) { build.AppendLine(line); continue; }
                string action = S(o, "Action"), test = S(o, "Test"), text = S(o, "Output");
                if (action == "build-output") { if (text != null && !text.StartsWith("#")) build.Append(text); continue; }
                if (action == "output" && text != null) log.Append(text);
                if (string.IsNullOrEmpty(test)) { if (action == "output" && text != null) pkg.Append(text); continue; }
                if (!status.ContainsKey(test))
                {
                    order.Add(test); status[test] = "run";
                    int slash = test.LastIndexOf('/');
                    if (slash > 0) parents.Add(test.Substring(0, slash));
                }
                if (action == "output" && text != null)
                {
                    StringBuilder sb; if (!outBy.TryGetValue(test, out sb)) outBy[test] = sb = new StringBuilder();
                    sb.Append(text);
                }
                if (action == "pass" || action == "fail" || action == "skip") status[test] = action;
            }
            rep.log = CleanPaths(log.ToString());
            // паника: сообщение и строка в main.go
            string panic = null; int panicLine = -1;
            foreach (var kv in outBy.Values.Select(v => v.ToString()).Concat(new[] { pkg.ToString(), build.ToString() }))
            {
                int pi = kv.IndexOf("panic: ", StringComparison.Ordinal);
                if (pi < 0) continue;
                int end = kv.IndexOf('\n', pi); string pl = (end > 0 ? kv.Substring(pi, end - pi) : kv.Substring(pi)).Replace(" [recovered]", "");
                if (panic == null) panic = pl.Trim();
                var m = TraceLine.Matches(kv).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
                if (m != null && panicLine < 0) panicLine = int.Parse(m.Groups[2].Value);
            }
            var leaves = order.Where(t => !parents.Contains(t)).ToList();
            foreach (var t in leaves)
            {
                StringBuilder sb; outBy.TryGetValue(t, out sb);
                var msgs = new List<string>();
                if (sb != null)
                    foreach (var l in sb.ToString().Split('\n'))
                    {
                        string tl = l.TrimEnd();
                        if (tl.Trim().Length == 0 || tl.StartsWith("=== ") || tl.TrimStart().StartsWith("--- ") || tl.StartsWith("panic:") || tl.StartsWith("\t") || tl.StartsWith("goroutine")) continue;
                        if (TestMsg.IsMatch(tl)) msgs.Add(TestMsg.Replace(tl, ""));
                        else if (tl.StartsWith("    ")) msgs.Add(tl.Trim());
                    }
                string st; status.TryGetValue(t, out st);
                var r = new CheckResult { Passed = st == "pass", InputsText = Pretty(t), Expected = "", Actual = "" };
                if (!r.Passed) r.Note = msgs.Count > 0 ? string.Join("\n", msgs.ToArray()) : panic != null ? "Программа упала: " + panic : st == "run" ? "Тест не закончился." : "Тест не прошёл.";
                rep.results.Add(r);
            }
            if (panic != null)
            {
                rep.panicked = true; rep.errLine = panicLine;
                rep.errText = "Программа упала: " + panic + (panicLine > 0 ? " (строка " + panicLine + ")" : "") + ". Остальные тесты не запускались.";
            }
            string buildText = build.ToString().Trim();
            if (leaves.Count == 0 && (buildText.Length > 0 || pkg.ToString().Contains("[build failed]") || pkg.ToString().Contains("setup failed")))
            {
                rep.buildFailed = true;
                var lines = buildText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#") && !l.StartsWith("FAIL")).ToList();
                var nice = new List<string>();
                foreach (var l in lines.Take(8))
                {
                    var m = SrcErr.Match(l);
                    if (!m.Success) { nice.Add(l); continue; }
                    string file = m.Groups[1].Value, text = Explain(m.Groups[4].Value);
                    int ln = int.Parse(m.Groups[2].Value);
                    if (file == srcName)
                    {
                        if (rep.errLine < 0) { rep.errLine = ln; rep.errText = "Ошибка компиляции в строке " + ln + ": " + text; }
                        nice.Add("строка " + ln + ": " + text);
                    }
                    else nice.Add("тесты (" + file + ", строка " + ln + "): " + text + (m.Groups[4].Value.StartsWith("undefined:") ? " — не переименовывай функции из задания." : ""));
                }
                if (rep.errText == null) rep.errText = "Код не собрался" + (nice.Count > 0 ? ": " + nice[0] : ".");
                rep.results.Add(new CheckResult { Passed = false, InputsText = "сборка", Expected = "", Actual = "", Note = "Код не компилируется, тесты не запускались.\n" + string.Join("\n", nice.ToArray()) });
            }
            return rep;
        }

        // ---------- разбор ##TEST (Java, C# и дальше) ----------
        static readonly Regex JavacErr = new Regex(@"^(\w+\.java):(\d+): error: (.*)$");
        static readonly Regex CscErr = new Regex(@"^(\w+\.cs)\((\d+),(\d+)\): error (CS\d+): (.*)$");
        static readonly Regex SrcAt = new Regex(@"\((\w+\.(?:java|cs)):(\d+)\)");   // (Main.java:4), (Program.cs:5) — из Check и трассировок Java
        static readonly Regex CsAt = new Regex(@"(\w+\.cs):line (\d+)");            // трассировка .NET: in /work/…/Program.cs:line 5

        public class CompileError { public string file, text; public int line; }

        // Ошибки компилятора javac или csc по порядку, с переводом
        public static List<CompileError> CompileErrors(string err)
        {
            var list = new List<CompileError>();
            var lines = (err ?? "").Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                var m = JavacErr.Match(l);
                if (m.Success)
                {
                    string text = Explain(m.Groups[3].Value);
                    for (int j = i + 1; j < Math.Min(lines.Length, i + 5); j++)
                    {
                        string sym = lines[j].Trim();
                        if (sym.StartsWith("symbol:")) { text += " — " + sym.Substring(7).Trim(); break; }
                        if (JavacErr.IsMatch(sym)) break;
                    }
                    list.Add(new CompileError { file = m.Groups[1].Value, line = int.Parse(m.Groups[2].Value), text = text });
                    continue;
                }
                m = CscErr.Match(l);
                if (m.Success)
                {
                    string msg = m.Groups[5].Value, code = m.Groups[4].Value, text = Explain(msg);
                    text = text == msg ? code + ": " + msg : text.Replace("(" + msg + ")", "(" + code + ": " + msg + ")");
                    list.Add(new CompileError { file = m.Groups[1].Value, line = int.Parse(m.Groups[2].Value), text = text });
                }
            }
            return list;
        }

        // code — код выхода процесса тестов: не 0 при уже напечатанных проверках значит, что тесты оборвались посередине
        public static BoxReport ParseCheck(string stdout, string stderr, string srcName, int code = 0)
        {
            var rep = new BoxReport();
            var log = new StringBuilder();
            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("##TEST|")) { if (line.Length > 0) log.AppendLine(line); continue; }
                var p = line.Split(new[] { '|' }, 4);
                if (p.Length < 3) continue;
                bool ok = p[2] == "PASS";
                string note = p.Length > 3 ? p[3].Replace(" ⏎ ", "\n") : null;
                rep.results.Add(new CheckResult { Passed = ok, InputsText = p[1], Expected = "", Actual = "", Note = ok ? null : note ?? "Тест не прошёл." });
                if (!ok && rep.errLine < 0 && note != null)
                {
                    var m = SrcAt.Match(note);
                    if (m.Success && m.Groups[1].Value == srcName) { rep.errLine = int.Parse(m.Groups[2].Value); rep.errText = "Ошибка в строке " + rep.errLine + ": " + note; }
                }
            }
            string err = string.Join("\n", (stderr ?? "").Replace("\r", "").Split('\n').Where(l => !l.StartsWith("Picked up ") && !l.StartsWith("NOTE: Picked up")).ToArray()).Trim();
            if (err.Length > 0) log.AppendLine(err.Length > 4000 ? err.Substring(0, 4000) + "\n…" : err);
            rep.log = CleanPaths(log.ToString());
            if (rep.results.Count > 0)
            {
                // процесс оборвался посреди проверок (переполнение стека, выход из программы) — остальные тесты не выполнились
                if (code != 0 && code != 137)
                {
                    string cx; int cl = RuntimeError(err, srcName, out cx);
                    string why = "Тесты оборвались на середине (код выхода " + code + ")" + (cx != null ? ": " + cx : "") + ". Остальные проверки не выполнились.";
                    rep.panicked = true;
                    if (rep.errLine < 0 && cl > 0) { rep.errLine = cl; rep.errText = why; }
                    rep.results.Add(new CheckResult { Passed = false, InputsText = "запуск", Expected = "", Actual = "", Note = why });
                }
                return rep;
            }
            // тестов нет: не собралось или упало до первой проверки
            var nice = new List<string>();
            foreach (var e in CompileErrors(err))
            {
                if (nice.Count >= 8) break;
                if (e.file == srcName)
                {
                    if (rep.errLine < 0) { rep.errLine = e.line; rep.errText = "Ошибка компиляции в строке " + e.line + ": " + e.text; }
                    nice.Add("строка " + e.line + ": " + e.text);
                }
                else nice.Add("тесты (" + e.file + ", строка " + e.line + "): " + e.text + " — не меняй имена и параметры методов и классов из задания" + (e.file.EndsWith(".cs") ? " и оставь их public." : "."));
            }
            if (nice.Count > 0)
            {
                rep.buildFailed = true;
                rep.results.Add(new CheckResult { Passed = false, InputsText = "сборка", Expected = "", Actual = "", Note = "Код не компилируется, тесты не запускались.\n" + string.Join("\n", nice.ToArray()) });
                return rep;
            }
            string ex; int exLine = RuntimeError(err, srcName, out ex);
            if (ex != null)
            {
                rep.panicked = true; rep.errLine = exLine; rep.errText = "Программа упала: " + ex + (exLine > 0 ? " (строка " + exLine + ")" : "");
                rep.results.Add(new CheckResult { Passed = false, InputsText = "запуск", Expected = "", Actual = "", Note = rep.errText });
            }
            return rep;
        }

        // Падение программы: «Exception in thread "main" java.lang.X: …» (Java), «Unhandled exception. System.X: …» и «Stack overflow.» (.NET);
        // строка — из трассировки исходника игрока (Main.java:4 / Program.cs:line 5), -1 если её нет
        static int RuntimeError(string err, string srcName, out string ex)
        {
            ex = null; err = err ?? "";
            Func<int, string> lineAt = i => { int end = err.IndexOf('\n', i); return (end > 0 ? err.Substring(i, end - i) : err.Substring(i)).Trim(); };
            int k = err.IndexOf("Unhandled exception. ", StringComparison.Ordinal);
            if (k >= 0)
            {
                ex = lineAt(k).Substring("Unhandled exception. ".Length).Trim();
                var m = CsAt.Matches(err).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
                return m != null ? int.Parse(m.Groups[2].Value) : -1;
            }
            if (err.Contains("Stack overflow."))
            {
                ex = "переполнение стека (Stack overflow) — похоже на бесконечную рекурсию";
                return -1;
            }
            k = err.IndexOf("Exception in thread", StringComparison.Ordinal);
            if (k < 0) k = err.IndexOf("Error: ", StringComparison.Ordinal);
            if (k < 0) return -1;
            ex = lineAt(k).Replace("Exception in thread \"main\" ", "").Trim();
            if (ex.Contains("StackOverflowError")) ex += " — похоже на бесконечную рекурсию";
            var j = SrcAt.Matches(err).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
            return j != null ? int.Parse(j.Groups[2].Value) : -1;
        }

        // Строка ошибки из вывода запуска (компиляция или падение); 0 — ошибки нет
        public static int ErrorLine(string stderr, string srcName, out string msg)
        {
            msg = null;
            if (srcName.EndsWith(".java") || srcName.EndsWith(".cs"))
            {
                var ce = CompileErrors(stderr).FirstOrDefault(e => e.file == srcName);
                if (ce != null) { msg = "Ошибка компиляции в строке " + ce.line + ": " + ce.text; return ce.line; }
                string ex; int ln = RuntimeError(stderr, srcName, out ex);
                if (ex != null) { msg = "Программа упала: " + ex; return ln; }
                return 0;
            }
            foreach (var raw in (stderr ?? "").Split('\n'))
            {
                var m = SrcErr.Match(raw.Trim());
                if (m.Success && m.Groups[1].Value == srcName) { msg = "Ошибка компиляции в строке " + m.Groups[2].Value + ": " + Explain(m.Groups[4].Value); return int.Parse(m.Groups[2].Value); }
            }
            int pi = (stderr ?? "").IndexOf("panic: ", StringComparison.Ordinal);
            if (pi >= 0)
            {
                int end = stderr.IndexOf('\n', pi); string pl = end > 0 ? stderr.Substring(pi, end - pi) : stderr.Substring(pi);
                var t = TraceLine.Matches(stderr).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
                msg = "Программа упала: " + pl.Trim();
                return t != null ? int.Parse(t.Groups[2].Value) : -1;
            }
            return 0;
        }

        // Частые ошибки компилятора Go — по-русски, оригинал в скобках
        public static string Explain(string e)
        {
            e = (e ?? "").Trim();
            Match m;
            if ((m = Regex.Match(e, @"^declared and not used: (\w+)$")).Success) return "переменная " + m.Groups[1].Value + " объявлена, но не используется — Go такое не компилирует (" + e + ")";
            if ((m = Regex.Match(e, "^\"([^\"]+)\" imported and not used$")).Success) return "пакет " + m.Groups[1].Value + " импортирован, но не используется (" + e + ")";
            if ((m = Regex.Match(e, @"^undefined: (\S+)$")).Success) return m.Groups[1].Value + " не объявлено (" + e + ")";
            if (e == "missing return") return "функция должна вернуть значение: не хватает return (" + e + ")";
            if (e.StartsWith("syntax error")) return "синтаксическая ошибка (" + e + ")";
            if (e.StartsWith("cannot use ")) return "не тот тип значения (" + e + ")";
            if (e.Contains("mismatched types")) return "разные типы в одном выражении (" + e + ")";
            // javac
            if (e == "cannot find symbol") return "имя не найдено: опечатка или не объявлено (" + e + ")";
            if ((m = Regex.Match(e, "^'(.+)' expected$")).Success) return "не хватает «" + m.Groups[1].Value + "» (" + e + ")";
            if (e == "missing return statement") return "метод должен вернуть значение: не хватает return (" + e + ")";
            if (e.StartsWith("incompatible types")) return "несовместимые типы (" + e + ")";
            if ((m = Regex.Match(e, @"^variable (\w+) might not have been initialized$")).Success) return "переменной " + m.Groups[1].Value + " не присвоено значение (" + e + ")";
            if ((m = Regex.Match(e, @"^variable (\w+) is already defined")).Success) return "переменная " + m.Groups[1].Value + " уже объявлена (" + e + ")";
            if (e == "unreachable statement") return "до этой строки выполнение никогда не дойдёт (" + e + ")";
            if (e.StartsWith("class ") && e.Contains("is public, should be declared in a file named")) return "публичный класс должен называться как файл — Main (" + e + ")";
            if (e.Contains("unreported exception")) return "проверяемое исключение нужно поймать или объявить в throws (" + e + ")";
            // csc (C#)
            if ((m = Regex.Match(e, @"^The name '(\w+)' does not exist in the current context$")).Success) return "имя " + m.Groups[1].Value + " не найдено: опечатка или не объявлено (" + e + ")";
            if ((m = Regex.Match(e, @"^([;{}()\[\],]|\w+) expected$")).Success) return "не хватает «" + m.Groups[1].Value + "» (" + e + ")";
            if (e.EndsWith("not all code paths return a value")) return "не все ветки метода возвращают значение: не хватает return (" + e + ")";
            if ((m = Regex.Match(e, @"^Cannot implicitly convert type '(.+?)' to '(.+?)'")).Success) return "нельзя просто так превратить " + m.Groups[1].Value + " в " + m.Groups[2].Value + (e.Contains("explicit conversion exists") ? " — нужно явное приведение, например (" + m.Groups[2].Value + ")" : "") + " (" + e + ")";
            if ((m = Regex.Match(e, @"^Use of unassigned local variable '(\w+)'")).Success) return "переменной " + m.Groups[1].Value + " не присвоено значение (" + e + ")";
            if ((m = Regex.Match(e, @"^A local variable or function named '(\w+)' is already defined")).Success) return m.Groups[1].Value + " уже объявлена (" + e + ")";
            if (e.EndsWith("is inaccessible due to its protection level")) return "член класса закрыт (private) — снаружи его не видно, нужен public (" + e + ")";
            if ((m = Regex.Match(e, @"^'(.+?)' does not contain a definition for '(\w+)'")).Success) return "у " + m.Groups[1].Value + " нет члена " + m.Groups[2].Value + ": опечатка в имени? (" + e + ")";
            if ((m = Regex.Match(e, @"^No overload for method '(\w+)' takes (\d+) arguments$")).Success) return "у метода " + m.Groups[1].Value + " нет варианта с таким числом аргументов: " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^Operator '(.+?)' cannot be applied to operands of type '(.+?)' and '(.+?)'$")).Success) return "оператор " + m.Groups[1].Value + " не работает с " + m.Groups[2].Value + " и " + m.Groups[3].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^Argument (\d+): cannot convert from '(.+?)' to '(.+?)'$")).Success) return "аргумент " + m.Groups[1].Value + ": нужен " + m.Groups[3].Value + ", а передан " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^The type or namespace name '(\w+)' could not be found")).Success) return "тип " + m.Groups[1].Value + " не найден: опечатка или не подключён using (" + e + ")";
            if ((m = Regex.Match(e, @"^Invalid expression term '(.+)'$")).Success) return "здесь не может стоять «" + m.Groups[1].Value + "» (" + e + ")";
            if (e.StartsWith("Only assignment, call, increment, decrement")) return "это выражение ничего не делает — пропущено присваивание или вызов? (" + e + ")";
            if (e.StartsWith("Cannot assign to") && e.Contains("readonly")) return "поле readonly можно задать только в конструкторе (" + e + ")";
            if (e.EndsWith("unassigned local variable") || e.Contains("must be assigned")) return "значение не присвоено до использования (" + e + ")";
            return e;
        }

        static string Pretty(string test)
        {
            int slash = test.IndexOf('/');
            return slash > 0 ? test.Substring(slash + 1).Replace('_', ' ') : test;
        }

        static string CleanPaths(string s) { return Regex.Replace(s ?? "", @"/work/\.stazher/run/[A-Za-z0-9_-]+/", ""); }
        static string FirstLine(string s) { s = (s ?? "").Trim(); int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) ? v as string : null; }
    }
}
