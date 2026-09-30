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
        public string dockerfile;        // образ не скачивается, а собирается игрой из этого Dockerfile (docker build)
        public int slowSec;              // добавка к времени на запуск и тесты: у компиляторов на JVM долгий старт
        public int RunSec { get { return LangBox.RunTimeoutSec + slowSec; } }
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

        // Спринт 14: C++. Маленького официального образа с g++ нет (gcc:14 — около 1,4 ГБ), поэтому игра один раз собирает свой
        // из debian:trixie-slim. Сборка с -fsanitize=address,undefined: выход за границы, деление на ноль и рекурсия — со строкой кода
        public static readonly LangSpec Cpp = new LangSpec
        {
            id = "cpp", name = "C++", image = "stazher/cpp:1", container = "stazher-cpp", dockerfile = CppDockerfile,
            src = "main.cpp", test = "tests.cpp", parser = "check", size = "около 500 МБ",
            runCmd = "sh stazher-cpp.sh run", testCmd = "sh stazher-cpp.sh test",
            testShow = "g++ -std=c++20 -fsanitize=address,undefined -o tests tests.cpp && ./tests", runShow = "g++ -std=c++20 -fsanitize=address,undefined -o main main.cpp && ./main", testMarker = "check::",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "check.hpp", CheckHpp }, { "stazher-cpp.sh", CppScript } },
        };

        // Спринт 15: Rust. Официальный образ rust:1-alpine, сборка rustc без cargo (зависимостей в задачах нет, сети в контейнере тоже).
        // Отладочная сборка: переполнение, выход за границы и деление на ноль — паника со строкой
        public static readonly LangSpec Rust = new LangSpec
        {
            id = "rust", name = "Rust", image = "rust:1-alpine", container = "stazher-rust",
            src = "main.rs", test = "tests.rs", parser = "check", size = "около 1,4 ГБ",
            runCmd = "sh stazher-rs.sh run", testCmd = "sh stazher-rs.sh test",
            testShow = "rustc --edition 2021 tests.rs && ./tests", runShow = "rustc --edition 2021 main.rs && ./main", testMarker = "check::",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "check.rs", CheckRs }, { "stazher-rs.sh", RsScript } },
        };

        // Спринт 16: PHP. Официальный php:8.4-cli-alpine. Сборки нет — сначала php -l (синтаксис), потом запуск;
        // в тестах предупреждения (неизвестная переменная, нет ключа в массиве) становятся ошибками со строкой
        public static readonly LangSpec Php = new LangSpec
        {
            id = "php", name = "PHP", image = "php:8.4-cli-alpine", container = "stazher-php",
            src = "main.php", test = "tests.php", parser = "check", size = "около 160 МБ",
            runCmd = "sh stazher-php.sh run", testCmd = "sh stazher-php.sh test",
            testShow = "php tests.php", runShow = "php main.php", testMarker = "check::",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "check.php", CheckPhp }, { "stazher-tests.php", PhpTestsRunner }, { "stazher-php.sh", PhpScript } },
        };

        // Спринт 17: Kotlin. Образа с kotlinc нет — игра собирает свой поверх eclipse-temurin:21-jdk-alpine (он уже скачан для Java)
        // и готовит архив классов компилятора (CDS): сборка около 3 с вместо 8
        public static readonly LangSpec Kotlin = new LangSpec
        {
            id = "kotlin", name = "Kotlin", image = "stazher/kotlin:1", container = "stazher-kotlin", dockerfile = KtDockerfile,
            src = "Main.kt", test = "Tests.kt", parser = "check", size = "около 850 МБ", slowSec = 15,
            runCmd = "sh stazher-kt.sh run", testCmd = "sh stazher-kt.sh test",
            testShow = "kotlinc Main.kt Tests.kt -d out && kotlin -cp out StazherTests", runShow = "kotlinc Main.kt -d out && kotlin -cp out MainKt", testMarker = "Check.",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "Check.kt", CheckKt }, { "stazher-kt.sh", KtScript } },
        };

        // Спринт 18: Swift. Официальный образ swift:6.2 (компилятор swiftc и Foundation для Linux). Сборка без SwiftPM — одним swiftc,
        // отладочная (-Onone -g): выход за границы, переполнение и nil в ! — падение со строкой кода
        public static readonly LangSpec Swift = new LangSpec
        {
            id = "swift", name = "Swift", image = "swift:6.2", container = "stazher-swift",
            src = "main.swift", test = "tests.swift", parser = "check", size = "около 5 ГБ", slowSec = 10,
            runCmd = "sh stazher-swift.sh run", testCmd = "sh stazher-swift.sh test",
            testShow = "swiftc main.swift tests.swift check.swift -o tests && ./tests", runShow = "swiftc main.swift -o main && ./main", testMarker = "Check.",
            env = new[] { "LANG=C.UTF-8" },
            extraFiles = new Dictionary<string, string> { { "check.swift", CheckSwift }, { "stazher-swift.sh", SwiftScript } },
        };

        public static LangSpec For(string lang) { return lang == "go" ? Go : lang == "java" ? Java : lang == "csharp" ? CSharp : lang == "cpp" ? Cpp : lang == "rust" ? Rust : lang == "php" ? Php : lang == "kotlin" ? Kotlin : lang == "swift" ? Swift : null; }
        public static IEnumerable<LangSpec> All { get { yield return Go; yield return Java; yield return CSharp; yield return Cpp; yield return Rust; yield return Php; yield return Kotlin; yield return Swift; } }

        // <swift-embed> — генерирует Tools/s18/embed.py из check.swift и stazher-swift.sh
        public const string CheckSwift =
            "// Проверки «Стажёра» для задач на Swift: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Ошибка (throw) в проверяемом коде ловится внутри проверки; падение программы (fatal error) останавливает все тесты.\n" +
            "import Foundation\n" +
            "\n" +
            "enum Check {\n" +
            "    // Check.eq(\"имя\", ожидаемое) { код }\n" +
            "    static func eq<T: Equatable>(_ name: String, _ want: T, _ got: () throws -> T) {\n" +
            "        do {\n" +
            "            let g = try got()\n" +
            "            if g == want { pass(name) } else { fail(name, \"получено \\(show(g)), ожидалось \\(show(want))\") }\n" +
            "        } catch { fail(name, \"ошибка \\(show(error))\") }\n" +
            "    }\n" +
            "\n" +
            "    // Check.near(\"имя\", 2.25) { f(1.5) } — дробные сравниваются с допуском\n" +
            "    static func near(_ name: String, _ want: Double, _ got: () throws -> Double) {\n" +
            "        do {\n" +
            "            let g = try got()\n" +
            "            if abs(g - want) < 1e-9 { pass(name) } else { fail(name, \"получено \\(g), ожидалось \\(want)\") }\n" +
            "        } catch { fail(name, \"ошибка \\(show(error))\") }\n" +
            "    }\n" +
            "\n" +
            "    // Check.fails(\"имя\", BankError.notEnough) { try withdraw(100, 500) } — ждём именно эту ошибку\n" +
            "    static func fails<E: Error & Equatable, T>(_ name: String, _ want: E, _ f: () throws -> T) {\n" +
            "        do {\n" +
            "            let r = try f()\n" +
            "            fail(name, \"ошибки не было (результат \\(show(r))), ожидалась \\(show(want))\")\n" +
            "        } catch let e as E where e == want {\n" +
            "            pass(name)\n" +
            "        } catch {\n" +
            "            fail(name, \"ошибка \\(show(error)), ожидалась \\(show(want))\")\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    static func show(_ v: Any) -> String { String(reflecting: v).replacingOccurrences(of: \"Swift.\", with: \"\").replacingOccurrences(of: \"Stazher.\", with: \"\") }\n" +
            "    static func clean(_ s: String) -> String {\n" +
            "        s.replacingOccurrences(of: \"|\", with: \"/\").replacingOccurrences(of: \"\\r\", with: \"\").replacingOccurrences(of: \"\\n\", with: \" ⏎ \")\n" +
            "    }\n" +
            "    static func pass(_ n: String) { print(\"##TEST|\\(clean(n))|PASS\"); fflush(stdout) }\n" +
            "    static func fail(_ n: String, _ m: String) { print(\"##TEST|\\(clean(n))|FAIL|\\(clean(m))\"); fflush(stdout) }\n" +
            "}\n" +
            "";

        public const string SwiftScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: сборка и запуск Swift. sh stazher-swift.sh run — main.swift;  sh stazher-swift.sh test — main.swift + tests.swift + check.swift\n" +
            "# В тестах к копии main.swift дописывается вызов tests(): сначала выполняется код игрока, потом проверки (номера строк не сдвигаются)\n" +
            "rm -rf out && mkdir out || exit 2\n" +
            "if [ \"$1\" = test ]; then\n" +
            "    { cat main.swift; printf '\\ntests()\\n'; } > out/main.swift\n" +
            "    SRC=\"out/main.swift tests.swift check.swift\"\n" +
            "else\n" +
            "    SRC=\"main.swift\"\n" +
            "fi\n" +
            "swiftc -swift-version 5 -Onone -g -suppress-warnings -diagnostic-style llvm -module-name Stazher $SRC -lm -o out/prog 2>out/swiftc.txt\n" +
            "if [ $? -ne 0 ]; then grep -v \"emit-module command failed\" out/swiftc.txt >&2; exit 1; fi\n" +
            "# при падении — короткий отчёт: только упавший поток, без регистров и списка библиотек\n" +
            "export SWIFT_BACKTRACE=enable=yes,interactive=no,color=no,threads=crashed,registers=none,images=none,limit=12\n" +
            "exec ./out/prog\n" +
            "";
        // </swift-embed>

        // Tools/s17/Check.kt — набор проверок для Tests.kt и точка входа тестов
        public const string CheckKt =
            "// Проверки «Стажёра» для задач на Kotlin: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Исключение в проверяемом коде ловится внутри проверки — остальные тесты всё равно выполнятся.\n" +
            "object Check {\n" +
            "    // Check.eq(\"имя\", ожидаемое) { код }\n" +
            "    fun eq(name: String, want: Any?, got: () -> Any?) {\n" +
            "        val g = try { got() } catch (e: Throwable) { fail(name, \"исключение \" + describe(e)); return }\n" +
            "        if (same(g, want)) pass(name) else fail(name, \"получено ${show(g)}, ожидалось ${show(want)}\")\n" +
            "    }\n" +
            "\n" +
            "    // Check.near(\"имя\", 2.25) { f(1.5) } — дробные сравниваются с допуском\n" +
            "    fun near(name: String, want: Double, got: () -> Double) {\n" +
            "        val g = try { got() } catch (e: Throwable) { fail(name, \"исключение \" + describe(e)); return }\n" +
            "        if (Math.abs(g - want) < 1e-9) pass(name) else fail(name, \"получено $g, ожидалось $want\")\n" +
            "    }\n" +
            "\n" +
            "    // Check.throws<IllegalArgumentException>(\"имя\") { f(-1) }\n" +
            "    inline fun <reified T : Throwable> throws(name: String, noinline f: () -> Any?) = throwsOf(name, T::class.java, f)\n" +
            "\n" +
            "    fun throwsOf(name: String, type: Class<out Throwable>, f: () -> Any?) {\n" +
            "        try { f(); fail(name, \"исключения не было, ожидалось ${type.simpleName}\") }\n" +
            "        catch (e: Throwable) {\n" +
            "            if (type.isInstance(e)) pass(name) else fail(name, \"исключение ${describe(e)}, ожидалось ${type.simpleName}\")\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    private fun same(a: Any?, b: Any?): Boolean {\n" +
            "        if (a == null || b == null) return a == b\n" +
            "        if (a is Number && b is Number) {\n" +
            "            val real = a is Double || a is Float || b is Double || b is Float\n" +
            "            return if (real) Math.abs(a.toDouble() - b.toDouble()) < 1e-9 else a.toLong() == b.toLong()\n" +
            "        }\n" +
            "        if (a is Array<*> && b is Array<*>) return a.contentDeepEquals(b)\n" +
            "        if (a is IntArray && b is IntArray) return a.contentEquals(b)\n" +
            "        return a == b\n" +
            "    }\n" +
            "\n" +
            "    private fun show(v: Any?): String = when (v) {\n" +
            "        null -> \"null\"\n" +
            "        is String -> \"\\\"$v\\\"\"\n" +
            "        is Char -> \"'$v'\"\n" +
            "        is Array<*> -> v.contentDeepToString()\n" +
            "        is IntArray -> v.contentToString()\n" +
            "        else -> v.toString()\n" +
            "    }\n" +
            "\n" +
            "    private fun describe(e: Throwable): String {\n" +
            "        val at = e.stackTrace.firstOrNull { it.fileName == \"Main.kt\" }\n" +
            "        return \"${e.javaClass.simpleName}: ${e.message}\" + (if (at != null) \" (Main.kt:${at.lineNumber})\" else \"\")\n" +
            "    }\n" +
            "\n" +
            "    private fun clean(s: String) = s.replace(\"|\", \"/\").replace(\"\\r\", \"\").replace(\"\\n\", \" ⏎ \")\n" +
            "    fun pass(n: String) = println(\"##TEST|${clean(n)}|PASS\")\n" +
            "    fun fail(n: String, m: String) = println(\"##TEST|${clean(n)}|FAIL|${clean(m)}\")\n" +
            "}\n" +
            "\n" +
            "// Точка входа тестов: запускает tests() из Tests.kt\n" +
            "object StazherTests {\n" +
            "    @JvmStatic\n" +
            "    fun main(args: Array<String>) { tests() }\n" +
            "}\n";

        // Tools/s17/stazher-kt.sh — kotlinc через java с архивом классов
        public const string KtScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: сборка и запуск Kotlin. kotlinc запускается прямо через java с архивом классов (CDS) — быстрее обычного старта.\n" +
            "# sh stazher-kt.sh run — Main.kt;  sh stazher-kt.sh test — Main.kt + Tests.kt + Check.kt\n" +
            "K=${KOTLIN_HOME:-/opt/kotlinc}\n" +
            "JSA=; [ -f \"$K/kotlinc.jsa\" ] && JSA=\"-XX:SharedArchiveFile=$K/kotlinc.jsa -Xshare:auto -Xlog:cds=off -Xlog:cds+dynamic=off\"\n" +
            "ENC=\"-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8\"\n" +
            "rm -rf out && mkdir out || exit 2\n" +
            "if [ \"$1\" = test ]; then SRC=\"Main.kt Tests.kt Check.kt\"; MAIN=StazherTests; else SRC=\"Main.kt\"; MAIN=MainKt; fi\n" +
            "java -XX:TieredStopAtLevel=1 -XX:+UseSerialGC -Xmx512m -Xss4m $JSA $ENC -cp \"$K/lib/kotlin-compiler.jar\" \\\n" +
            "     org.jetbrains.kotlin.cli.jvm.K2JVMCompiler -kotlin-home \"$K\" -no-reflect -nowarn $SRC -d out 2>out/kotlinc.txt\n" +
            "if [ $? -ne 0 ]; then cat out/kotlinc.txt >&2; exit 1; fi\n" +
            "exec java -Xmx256m -Xss8m $ENC -cp \"out:$K/lib/kotlin-stdlib.jar\" $MAIN\n";

        // Tools/s17/Dockerfile — образ JDK 21 + kotlinc, игра собирает его сама
        public const string KtDockerfile =
            "# Компилятор Kotlin для «Стажёра»: JDK 21 (тот же, что для Java) + kotlinc. Образ собирает сама игра, один раз.\n" +
            "FROM eclipse-temurin:21-jdk-alpine\n" +
            "ARG KOTLIN=2.4.20\n" +
            "# kotlinc с GitHub JetBrains, запасной путь — официальный пакет JetBrains в npm\n" +
            "RUN set -e; ok=; \\\n" +
            "    for i in 1 2 3; do wget -q -O /tmp/k.zip \"https://github.com/JetBrains/kotlin/releases/download/v$KOTLIN/kotlin-compiler-$KOTLIN.zip\" && ok=1 && break; sleep 5; done; \\\n" +
            "    if [ -n \"$ok\" ]; then unzip -q /tmp/k.zip -d /opt; \\\n" +
            "    else wget -q -O /tmp/k.tgz \"https://registry.npmjs.org/kotlin-compiler/-/kotlin-compiler-$KOTLIN.tgz\" && tar xzf /tmp/k.tgz -C /tmp && mv /tmp/package /opt/kotlinc; fi; \\\n" +
            "    rm -f /tmp/k.zip /tmp/k.tgz; test -f /opt/kotlinc/lib/kotlin-compiler.jar\n" +
            "# архив классов компилятора (CDS): kotlinc стартует примерно втрое быстрее\n" +
            "RUN mkdir /tmp/w && cd /tmp/w && printf 'fun main() { println(listOf(1, 2).sum()) }\\n' > A.kt \\\n" +
            " && java -XX:TieredStopAtLevel=1 -XX:+UseSerialGC -Xmx512m -Xss4m -XX:ArchiveClassesAtExit=/opt/kotlinc/kotlinc.jsa \\\n" +
            "         -cp /opt/kotlinc/lib/kotlin-compiler.jar org.jetbrains.kotlin.cli.jvm.K2JVMCompiler -kotlin-home /opt/kotlinc -no-reflect A.kt -d out \\\n" +
            " && cd / && rm -rf /tmp/w\n" +
            "ENV LANG=C.UTF-8 KOTLIN_HOME=/opt/kotlinc\n" +
            "WORKDIR /work\n" +
            "LABEL stazher=1\n";

        // Tools/s16/check.php — набор проверок для tests.php
        public const string CheckPhp =
            "<?php\n" +
            "// Проверки «Стажёра» для задач на PHP: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Исключение в проверяемом коде ловится внутри проверки — остальные тесты всё равно выполнятся.\n" +
            "// Предупреждения PHP (например, неизвестная переменная) превращаются в ошибки, чтобы не проходить молча.\n" +
            "final class check\n" +
            "{\n" +
            "    // С этого момента предупреждения PHP становятся исключениями\n" +
            "    public static function strict(): void\n" +
            "    {\n" +
            "        set_error_handler(function (int $no, string $msg, string $file, int $line): bool {\n" +
            "            throw new ErrorException($msg, 0, $no, $file, $line);\n" +
            "        });\n" +
            "    }\n" +
            "\n" +
            "    public static function eq(string $name, callable $got, mixed $want): void\n" +
            "    {\n" +
            "        try { $g = $got(); } catch (Throwable $e) { self::fail($name, 'исключение ' . self::what($e)); return; }\n" +
            "        if (self::same($g, $want)) self::pass($name);\n" +
            "        else self::fail($name, 'получено ' . self::show($g) . ', ожидалось ' . self::show($want));\n" +
            "    }\n" +
            "\n" +
            "    public static function near(string $name, callable $got, float $want): void\n" +
            "    {\n" +
            "        try { $g = $got(); } catch (Throwable $e) { self::fail($name, 'исключение ' . self::what($e)); return; }\n" +
            "        if (is_numeric($g) && abs($g - $want) < 1e-9) self::pass($name);\n" +
            "        else self::fail($name, 'получено ' . self::show($g) . ', ожидалось ' . self::show($want));\n" +
            "    }\n" +
            "\n" +
            "    public static function throws(string $name, callable $f, string $class): void\n" +
            "    {\n" +
            "        try { $f(); self::fail($name, 'исключения не было, ожидалось ' . $class); }\n" +
            "        catch (Throwable $e) {\n" +
            "            if ($e instanceof $class) self::pass($name);\n" +
            "            else self::fail($name, 'исключение ' . self::what($e) . ', ожидалось ' . $class);\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    private static function same(mixed $g, mixed $w): bool\n" +
            "    {\n" +
            "        if ((is_int($g) || is_float($g)) && (is_int($w) || is_float($w)) && (is_float($g) || is_float($w))) return abs($g - $w) < 1e-9;\n" +
            "        if (is_array($g) && is_array($w)) {\n" +
            "            if (count($g) !== count($w) || array_keys($g) !== array_keys($w)) return false;\n" +
            "            foreach ($g as $k => $v) if (!self::same($v, $w[$k])) return false;\n" +
            "            return true;\n" +
            "        }\n" +
            "        return $g === $w;\n" +
            "    }\n" +
            "\n" +
            "    private static function show(mixed $v): string\n" +
            "    {\n" +
            "        if (is_string($v)) return '\"' . $v . '\"';\n" +
            "        if (is_bool($v)) return $v ? 'true' : 'false';\n" +
            "        if ($v === null) return 'null';\n" +
            "        if (is_float($v)) return var_export($v, true);\n" +
            "        if (is_array($v)) {\n" +
            "            $list = array_is_list($v);\n" +
            "            $parts = [];\n" +
            "            foreach ($v as $k => $x) $parts[] = ($list ? '' : self::show($k) . ' => ') . self::show($x);\n" +
            "            return '[' . implode(', ', $parts) . ']';\n" +
            "        }\n" +
            "        if (is_object($v)) return method_exists($v, '__toString') ? (string)$v : get_class($v);\n" +
            "        return (string)$v;\n" +
            "    }\n" +
            "\n" +
            "    private static function what(Throwable $e): string\n" +
            "    {\n" +
            "        $where = '';\n" +
            "        if (basename($e->getFile()) === 'main.php') $where = ' (main.php:' . $e->getLine() . ')';\n" +
            "        else foreach ($e->getTrace() as $f) if (isset($f['file']) && basename($f['file']) === 'main.php') { $where = ' (main.php:' . $f['line'] . ')'; break; }\n" +
            "        return get_class($e) . ': ' . $e->getMessage() . $where;\n" +
            "    }\n" +
            "\n" +
            "    private static function clean(string $s): string { return str_replace(['|', \"\\r\", \"\\n\"], ['/', '', ' ⏎ '], $s); }\n" +
            "    private static function pass(string $n): void { echo '##TEST|' . self::clean($n) . \"|PASS\\n\"; }\n" +
            "    private static function fail(string $n, string $m): void { echo '##TEST|' . self::clean($n) . '|FAIL|' . self::clean($m) . \"\\n\"; }\n" +
            "}\n";

        // Tools/s16/stazher-tests.php — подключает main.php без вывода и запускает tests.php
        public const string PhpTestsRunner =
            "<?php\n" +
            "// «Стажёр»: main.php подключается целиком — его вывод и предупреждения скрыты, а ошибка в коде верхнего уровня\n" +
            "// не мешает тестам (функции PHP объявляет до выполнения файла). Затем выполняются тесты из tests.php\n" +
            "require __DIR__ . '/check.php';\n" +
            "set_error_handler(fn() => true);\n" +
            "ob_start();\n" +
            "try {\n" +
            "    require __DIR__ . '/main.php';\n" +
            "} catch (Throwable $e) {\n" +
            "} finally {\n" +
            "    ob_end_clean();\n" +
            "    restore_error_handler();\n" +
            "}\n" +
            "check::strict();\n" +
            "require __DIR__ . '/tests.php';\n";

        // Tools/s16/stazher-php.sh — php -l, затем запуск
        public const string PhpScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: запуск PHP. Сначала проверка синтаксиса (php -l), затем запуск.\n" +
            "# sh stazher-php.sh run — main.php;  sh stazher-php.sh test — main.php + tests.php + check.php (предупреждения становятся ошибками)\n" +
            "OPTS=\"-d display_errors=stderr -d log_errors=0 -d html_errors=0 -d error_reporting=-1 -d memory_limit=256M\"\n" +
            "php $OPTS -l main.php >/dev/null || exit 1\n" +
            "if [ \"$1\" = test ]; then\n" +
            "  php $OPTS -l tests.php >/dev/null || exit 1\n" +
            "  exec php $OPTS stazher-tests.php\n" +
            "fi\n" +
            "exec php $OPTS main.php\n";

        // Tools/s15/check.rs — набор проверок для tests.rs, паники перехватываются
        public const string CheckRs =
            "// Проверки «Стажёра» для задач на Rust: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Паника в проверяемом коде перехватывается — остальные тесты всё равно выполнятся.\n" +
            "use std::cell::RefCell;\n" +
            "use std::fmt::Debug;\n" +
            "use std::panic::{self, AssertUnwindSafe};\n" +
            "\n" +
            "thread_local! { static LAST: RefCell<String> = RefCell::new(String::new()); }\n" +
            "\n" +
            "// Запоминать текст и место паники вместо печати в stderr\n" +
            "pub fn install() {\n" +
            "    panic::set_hook(Box::new(|info| {\n" +
            "        let p = info.payload();\n" +
            "        let msg = if let Some(s) = p.downcast_ref::<&str>() { s.to_string() } else if let Some(s) = p.downcast_ref::<String>() { s.clone() } else { \"паника\".to_string() };\n" +
            "        let at = info.location().map(|l| format!(\" ({}:{})\", l.file().rsplit('/').next().unwrap_or(\"\"), l.line())).unwrap_or_default();\n" +
            "        LAST.with(|x| *x.borrow_mut() = format!(\"{}{}\", msg, at));\n" +
            "    }));\n" +
            "}\n" +
            "\n" +
            "fn last_panic() -> String { LAST.with(|x| x.borrow().clone()) }\n" +
            "fn clean(s: &str) -> String { s.replace('|', \"/\").replace('\\r', \"\").replace('\\n', \" ⏎ \") }\n" +
            "fn pass(name: &str) { println!(\"##TEST|{}|PASS\", clean(name)); }\n" +
            "fn fail(name: &str, why: &str) { println!(\"##TEST|{}|FAIL|{}\", clean(name), clean(why)); }\n" +
            "\n" +
            "// eq(\"имя\", || f(1), ожидаемое)\n" +
            "pub fn eq<T, W, F>(name: &str, got: F, want: W) where T: PartialEq<W> + Debug, W: Debug, F: FnOnce() -> T {\n" +
            "    match panic::catch_unwind(AssertUnwindSafe(got)) {\n" +
            "        Ok(g) => if g == want { pass(name) } else { fail(name, &format!(\"получено {:?}, ожидалось {:?}\", g, want)) },\n" +
            "        Err(_) => fail(name, &format!(\"паника: {}\", last_panic())),\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "// near(\"имя\", || f(1.5), 2.25) — дробные сравниваются с допуском\n" +
            "pub fn near<F: FnOnce() -> f64>(name: &str, got: F, want: f64) {\n" +
            "    match panic::catch_unwind(AssertUnwindSafe(got)) {\n" +
            "        Ok(g) => if (g - want).abs() < 1e-9 { pass(name) } else { fail(name, &format!(\"получено {:?}, ожидалось {:?}\", g, want)) },\n" +
            "        Err(_) => fail(name, &format!(\"паника: {}\", last_panic())),\n" +
            "    }\n" +
            "}\n" +
            "\n" +
            "// panics(\"имя\", || f(-1)) — код обязан запаниковать\n" +
            "pub fn panics<T, F: FnOnce() -> T>(name: &str, f: F) {\n" +
            "    match panic::catch_unwind(AssertUnwindSafe(f)) {\n" +
            "        Ok(_) => fail(name, \"паники не было, а она ожидалась\"),\n" +
            "        Err(_) => pass(name),\n" +
            "    }\n" +
            "}\n";

        // Tools/s15/stazher-rs.sh — сборка rustc без cargo; тесты живут внутри модуля с main.rs
        public const string RsScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: сборка и запуск Rust без cargo (rustc, отладочная сборка: переполнение, выход за границы\n" +
            "# и деление на ноль — паника со строкой кода).\n" +
            "# sh stazher-rs.sh run  — main.rs;  sh stazher-rs.sh test — main.rs + tests.rs + check.rs\n" +
            "rm -rf out && mkdir out || exit 2\n" +
            "if [ \"$1\" = test ]; then\n" +
            "  # main.rs подключается целиком в модуль program и не меняется; тесты лежат внутри него и видят его функции\n" +
            "  printf 'mod check { include!(\"check.rs\"); }\\n#[allow(dead_code, unused)]\\nmod program {\\n    include!(\"main.rs\");\\n    pub mod stazher_tests { use super::*; use crate::check; include!(\"tests.rs\"); }\\n}\\nfn main() { check::install(); program::stazher_tests::tests(); }\\n' > stazher-tests.rs\n" +
            "  SRC=stazher-tests.rs; BIN=out/tests\n" +
            "else\n" +
            "  SRC=main.rs; BIN=out/main\n" +
            "fi\n" +
            "rustc --edition 2021 -A warnings -C debug-assertions=on -C overflow-checks=on -C opt-level=0 -o $BIN $SRC 2>out/rustc.txt\n" +
            "if [ $? -ne 0 ]; then cat out/rustc.txt >&2; exit 1; fi\n" +
            "RUST_BACKTRACE=0 exec ./$BIN\n";

        // Tools/s14/check.hpp — набор проверок для tests.cpp
        public const string CheckHpp =
            "// Проверки «Стажёра» для задач на C++: каждая проверка печатает строку ##TEST|имя|PASS или ##TEST|имя|FAIL|почему.\n" +
            "// Исключение в проверяемом коде ловится внутри проверки — остальные тесты всё равно выполнятся.\n" +
            "#pragma once\n" +
            "#include <cmath>\n" +
            "#include <cxxabi.h>\n" +
            "#include <cstdlib>\n" +
            "#include <exception>\n" +
            "#include <iostream>\n" +
            "#include <map>\n" +
            "#include <sstream>\n" +
            "#include <string>\n" +
            "#include <type_traits>\n" +
            "#include <typeinfo>\n" +
            "#include <unordered_map>\n" +
            "#include <vector>\n" +
            "\n" +
            "namespace check {\n" +
            "\n" +
            "inline std::string clean(std::string s) {\n" +
            "    std::string r;\n" +
            "    for (char c : s) {\n" +
            "        if (c == '|') r += '/';\n" +
            "        else if (c == '\\n') r += \" \\xE2\\x8F\\x8E \";\n" +
            "        else if (c != '\\r') r += c;\n" +
            "    }\n" +
            "    return r;\n" +
            "}\n" +
            "\n" +
            "inline void pass(const std::string& n) { std::cout << \"##TEST|\" << clean(n) << \"|PASS\" << std::endl; }\n" +
            "inline void fail(const std::string& n, const std::string& m) { std::cout << \"##TEST|\" << clean(n) << \"|FAIL|\" << clean(m) << std::endl; }\n" +
            "\n" +
            "inline std::string demangle(const char* name) {\n" +
            "    int st = 0;\n" +
            "    char* d = abi::__cxa_demangle(name, nullptr, nullptr, &st);\n" +
            "    std::string r = st == 0 && d ? d : name;\n" +
            "    std::free(d);\n" +
            "    return r;\n" +
            "}\n" +
            "\n" +
            "template <class T, class = void> struct is_range : std::false_type {};\n" +
            "template <class T> struct is_range<T, std::void_t<decltype(std::declval<const T&>().begin()), decltype(std::declval<const T&>().end())>> : std::true_type {};\n" +
            "template <class T> struct is_map : std::false_type {};\n" +
            "template <class K, class V, class... R> struct is_map<std::map<K, V, R...>> : std::true_type {};\n" +
            "template <class K, class V, class... R> struct is_map<std::unordered_map<K, V, R...>> : std::true_type {};\n" +
            "template <class T, class = void> struct streamable : std::false_type {};\n" +
            "template <class T> struct streamable<T, std::void_t<decltype(std::declval<std::ostream&>() << std::declval<const T&>())>> : std::true_type {};\n" +
            "\n" +
            "template <class T> std::string show(const T& v);\n" +
            "\n" +
            "template <class T> std::string show(const T& v) {\n" +
            "    std::ostringstream o;\n" +
            "    using D = std::decay_t<T>;\n" +
            "    if constexpr (std::is_same_v<D, std::string>) o << '\"' << v << '\"';\n" +
            "    else if constexpr (std::is_same_v<D, const char*> || std::is_same_v<D, char*>) o << '\"' << v << '\"';\n" +
            "    else if constexpr (std::is_same_v<D, bool>) o << (v ? \"true\" : \"false\");\n" +
            "    else if constexpr (std::is_same_v<D, char>) o << '\\'' << v << '\\'';\n" +
            "    else if constexpr (std::is_floating_point_v<D>) { o.precision(15); o << v; }\n" +
            "    else if constexpr (is_map<D>::value) {\n" +
            "        std::map<std::string, std::string> sorted;\n" +
            "        for (const auto& kv : v) sorted[show(kv.first)] = show(kv.second);\n" +
            "        o << '{'; bool first = true;\n" +
            "        for (const auto& kv : sorted) { if (!first) o << \", \"; first = false; o << kv.first << \": \" << kv.second; }\n" +
            "        o << '}';\n" +
            "    }\n" +
            "    else if constexpr (is_range<D>::value) {\n" +
            "        o << '['; bool first = true;\n" +
            "        for (const auto& x : v) { if (!first) o << \", \"; first = false; o << show(x); }\n" +
            "        o << ']';\n" +
            "    }\n" +
            "    else if constexpr (streamable<D>::value) o << v;\n" +
            "    else o << \"<значение \" << demangle(typeid(D).name()) << \">\";\n" +
            "    return o.str();\n" +
            "}\n" +
            "\n" +
            "inline std::string what(const std::exception& e) { return demangle(typeid(e).name()) + \": \" + e.what(); }\n" +
            "\n" +
            "template <class G, class W> bool same(const G& g, const W& w) {\n" +
            "    if constexpr (std::is_arithmetic_v<G> && std::is_arithmetic_v<W> && (std::is_floating_point_v<G> || std::is_floating_point_v<W>))\n" +
            "        return std::fabs(static_cast<double>(g) - static_cast<double>(w)) < 1e-9;\n" +
            "    else if constexpr (std::is_arithmetic_v<G> && std::is_arithmetic_v<W>)\n" +
            "        return static_cast<long long>(g) == static_cast<long long>(w);\n" +
            "    else return g == w;\n" +
            "}\n" +
            "\n" +
            "// eq(\"имя\", [] { return f(1); }, ожидаемое)\n" +
            "template <class F, class W> void eq(const std::string& name, F got, const W& want) {\n" +
            "    try {\n" +
            "        auto g = got();\n" +
            "        if (same(g, want)) pass(name); else fail(name, \"получено \" + show(g) + \", ожидалось \" + show(want));\n" +
            "    } catch (const std::exception& e) { fail(name, \"исключение \" + what(e)); }\n" +
            "    catch (...) { fail(name, \"исключение неизвестного типа\"); }\n" +
            "}\n" +
            "\n" +
            "// near(\"имя\", [] { return f(1.5); }, 2.25) — дробные сравниваются с допуском\n" +
            "template <class F> void near(const std::string& name, F got, double want) {\n" +
            "    try {\n" +
            "        double g = static_cast<double>(got());\n" +
            "        if (std::fabs(g - want) < 1e-9) pass(name); else fail(name, \"получено \" + show(g) + \", ожидалось \" + show(want));\n" +
            "    } catch (const std::exception& e) { fail(name, \"исключение \" + what(e)); }\n" +
            "}\n" +
            "\n" +
            "// throws<std::invalid_argument>(\"имя\", [] { f(-1); })\n" +
            "template <class E, class F> void throws(const std::string& name, F f) {\n" +
            "    std::string want = demangle(typeid(E).name());\n" +
            "    try { f(); fail(name, \"исключения не было, ожидалось \" + want); }\n" +
            "    catch (const E&) { pass(name); }\n" +
            "    catch (const std::exception& e) { fail(name, \"исключение \" + what(e) + \", ожидалось \" + want); }\n" +
            "    catch (...) { fail(name, \"исключение неизвестного типа, ожидалось \" + want); }\n" +
            "}\n" +
            "\n" +
            "}  // namespace check\n";

        // Tools/s14/stazher-cpp.sh — сборка g++ с санитайзерами и запуск; тесты стартуют до main игрока
        public const string CppScript =
            "#!/bin/sh\n" +
            "# «Стажёр»: сборка и запуск C++ (g++, C++20). Санитайзеры (address, undefined) показывают выход за границы,\n" +
            "# деление на ноль, переполнение и бесконечную рекурсию со строкой кода.\n" +
            "# sh stazher-cpp.sh run  — main.cpp;  sh stazher-cpp.sh test — main.cpp + tests.cpp + check.hpp\n" +
            "rm -rf out && mkdir out || exit 2\n" +
            "if [ \"$1\" = test ]; then\n" +
            "  # main.cpp подключается целиком и не меняется; тесты запускаются до его main и завершают программу\n" +
            "  printf '#include \"check.hpp\"\\n#include \"main.cpp\"\\n#include \"tests.cpp\"\\nnamespace { struct StazherTests { StazherTests() { tests(); std::cout.flush(); std::exit(0); } } stazher_tests; }\\n' > stazher-tests.cpp\n" +
            "  SRC=stazher-tests.cpp; BIN=out/tests\n" +
            "else\n" +
            "  SRC=main.cpp; BIN=out/main\n" +
            "fi\n" +
            "LC_ALL=C g++ -std=c++20 -g -O0 -Wall -Werror=return-type -fdiagnostics-color=never -fmax-errors=8 \\\n" +
            "    -fsanitize=address,undefined -fno-sanitize-recover=all -fno-omit-frame-pointer -o $BIN $SRC 2>out/gcc.txt\n" +
            "if [ $? -ne 0 ]; then grep -v \"^In file included from\\|^                 from\" out/gcc.txt >&2; exit 1; fi\n" +
            "ASAN_OPTIONS=detect_leaks=0:halt_on_error=1:exitcode=134 UBSAN_OPTIONS=print_stacktrace=1:halt_on_error=1:exitcode=134 exec ./$BIN\n";

        // Tools/s14/Dockerfile — образ компилятора C++, игра собирает его сама (docker build)
        public const string CppDockerfile =
            "# Компилятор C++ для «Стажёра»: g++ из Debian с санитайзерами. Образ собирает сама игра, один раз.\n" +
            "FROM debian:trixie-slim\n" +
            "RUN apt-get -o Acquire::Retries=5 update \\\n" +
            " && apt-get -o Acquire::Retries=5 install -y --no-install-recommends g++ \\\n" +
            " && rm -rf /var/lib/apt/lists/*\n" +
            "ENV LANG=C.UTF-8\n" +
            "WORKDIR /work\n" +
            "LABEL stazher=1\n";

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
            if (!HasImage(s) && s.dockerfile != null)
            {
                if (note != null) note("docker build -t " + s.image + " — первый запуск " + s.name + ": собираю образ компилятора: скачаю около 80 МБ пакетов, на диске займёт " + s.size + ". Это один раз, несколько минут.");
                var bld = DevEnv.DockerCmd("build --label " + DevEnv.Label + " -t " + s.image + " -", 20 * 60 * 1000, s.dockerfile.Replace("\r\n", "\n"));
                if (!bld.Ok) return "Не удалось собрать образ " + s.image + (bld.TimedOut ? " за 20 минут" : "") + ": " + BuildError(bld.Text) + ". Проверь интернет и что Docker Desktop запущен.";
                pulled = true;
            }
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

        // Для инструментов редактора (пакетный прогон): образ и контейнер языка готовы — null, иначе текст ошибки
        public static string PrepareBox(LangSpec s, Action<string> note) { bool pulled; return Prepare(s, note, out pulled); }

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
            var r = Exec(s, id, s.testCmd, s.RunSec + 10);
            rep = s.parser == "check" ? ParseCheck(r.Out, r.Err, s.src, r.TimedOut ? 0 : r.Code) : ParseGoTest(r.Out + "\n" + r.Err, s.src);
            rep.pulled = pulled; rep.ms = r.Ms;
            if (r.TimedOut && !rep.buildFailed)
            {
                rep.timedOut = true;
                rep.results.Add(new CheckResult { Passed = false, InputsText = "время", Note = "Тесты не уложились в " + (s.RunSec + 10) + " с — похоже на бесконечный цикл (или программе не хватило памяти)." });
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
            var r = Exec(s, id, s.runCmd, s.RunSec);
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
        static readonly Regex SrcAt = new Regex(@"\((\w+\.(?:java|cs|rs|php|kt)):(\d+)\)");   // (Main.java:4), (Program.cs:5) — из Check и трассировок Java
        static readonly Regex CsAt = new Regex(@"(\w+\.cs):line (\d+)");            // трассировка .NET: in /work/…/Program.cs:line 5
        static readonly Regex GppErr = new Regex(@"^(\w+\.(?:cpp|hpp|h)):(\d+):(\d+): (?:fatal )?error: (.*?)(?: \[-(?:Werror=[\w-]+|fpermissive)\])?$");
        static readonly Regex CppAt = new Regex(@"(?:^|[/\s])(\w+\.cpp):(\d+)");          // кадр трассировки санитайзера: …/main.cpp:6
        static readonly Regex KtErr = new Regex(@"^(\w+\.kt):(\d+):(\d+): error: (.*?)\.?$");
        static readonly Regex SwiftErr = new Regex(@"^(?:.*/)?(\w+\.swift):(\d+):(\d+): error: (.*)$");
        static readonly Regex SwiftCrash = new Regex(@"(?:(?:Fatal error|Precondition failed|Assertion failed): ([^\n]*)|\*\*\* Swift runtime failure: ([^\n]*?) \*\*\*|\*\*\* Program crashed: ([^\n]*?) at 0x)");
        static readonly Regex SwiftFrame = new Regex(@" at (?:\S*/)?(\w+\.swift):(\d+)");   // кадр отчёта swift-backtrace: … in prog at /work/…/main.swift:4:20
        static readonly Regex PhpParse = new Regex(@"^(?:PHP )?Parse error:\s+(.*) in (?:.*/)?(\w+\.php) on line (\d+)$");
        static readonly Regex PhpUncaught = new Regex(@"(?:PHP )?Fatal error:\s+Uncaught (\S+?): (.*?) in (?:.*/)?(\w+\.php):(\d+)");
        static readonly Regex PhpFatal = new Regex(@"(?:PHP )?Fatal error:\s+(.*) in (?:.*/)?(\w+\.php) on line (\d+)");
        static readonly Regex PhpWarn = new Regex(@"(?:PHP )?(?:Warning|Notice|Deprecated):\s+(.*) in (?:.*/)?(\w+\.php) on line (\d+)");
        static readonly Regex PhpFrame = new Regex(@"(\w+\.php)\((\d+)\)");
        static readonly Regex RustErr = new Regex(@"^error(?:\[(E\d+)\])?: (.*)$");
        static readonly Regex RustAt = new Regex(@"^\s*--> (?:.*/)?(\w+\.rs):(\d+):(\d+)");
        static readonly Regex RustPanic = new Regex(@"panicked at (?:.*/)?(\w+\.rs):(\d+):\d+:\s*\r?\n\s*(.*)");
        static readonly Regex UbsanErr = new Regex(@"^(\w+\.cpp):(\d+):\d+: runtime error: (.*)$", RegexOptions.Multiline);

        public class CompileError { public string file, text; public int line; }

        // Ошибки компилятора javac или csc по порядку, с переводом
        public static List<CompileError> CompileErrors(string err)
        {
            var list = new List<CompileError>();
            var lines = (err ?? "").Replace("\r", "").Replace('\u2018', '\'').Replace('\u2019', '\'').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                var sm = SwiftErr.Match(l);
                if (sm.Success) { list.Add(new CompileError { file = sm.Groups[1].Value, line = int.Parse(sm.Groups[2].Value), text = Explain(sm.Groups[4].Value) }); continue; }
                var km = KtErr.Match(l);
                if (km.Success) { list.Add(new CompileError { file = km.Groups[1].Value, line = int.Parse(km.Groups[2].Value), text = Explain(km.Groups[4].Value) }); continue; }
                var pm = PhpParse.Match(l);
                if (pm.Success) { list.Add(new CompileError { file = pm.Groups[2].Value, line = int.Parse(pm.Groups[3].Value), text = Explain(pm.Groups[1].Value) }); continue; }
                var rm = RustErr.Match(lines[i]);
                if (rm.Success && !rm.Groups[2].Value.StartsWith("aborting due to") && !rm.Groups[2].Value.StartsWith("could not compile"))
                {
                    // блок ошибки rustc: место (-->), ожидаемый и полученный тип, подсказки help — до следующего error/warning
                    int end = i + 1;
                    while (end < lines.Length && !lines[end].StartsWith("error") && !lines[end].StartsWith("warning") && !lines[end].StartsWith("Some errors") && !lines[end].StartsWith("For more information")) end++;
                    Match at = null;
                    for (int j = i + 1; j < end && at == null; j++) { var am = RustAt.Match(lines[j]); if (am.Success) at = am; }
                    string block = string.Join("\n", lines, i + 1, end - i - 1), msg = rm.Groups[2].Value;
                    string text = Explain(msg);
                    if (rm.Groups[1].Success) text = text == msg ? rm.Groups[1].Value + ": " + msg : text.Replace("(" + msg + ")", "(" + rm.Groups[1].Value + ": " + msg + ")");
                    var ef = Regex.Match(block, @"expected `([^`]+)`, found `([^`]+)`");
                    if (ef.Success && !msg.Contains("expected `")) text += ": ожидался " + ef.Groups[1].Value + ", а получен " + ef.Groups[2].Value;
                    if (block.Contains("remove this semicolon")) text += " — убери ; в конце последней строки, чтобы вернуть значение";
                    else if (block.Contains("a local variable with a similar name exists") || block.Contains("similar name exists")) text += " — есть похожее имя, опечатка?";
                    if (block.Contains("consider changing this to be mutable") || block.Contains("consider making this binding mutable")) text += " — объяви переменную как let mut";
                    if (block.Contains("consider cloning the value") || block.Contains("consider borrowing")) text += " — возьми ссылку & или сделай .clone()";
                    if (at != null) list.Add(new CompileError { file = at.Groups[1].Value, line = int.Parse(at.Groups[2].Value), text = text });
                    i = end - 1;
                    continue;
                }
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
                m = GppErr.Match(l);
                if (m.Success) { list.Add(new CompileError { file = m.Groups[1].Value, line = int.Parse(m.Groups[2].Value), text = Explain(m.Groups[4].Value) }); continue; }
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
            // Swift: fatal error, runtime failure или падение; строка — из первого кадра в файле игрока (отчёт swift-backtrace)
            if (srcName.EndsWith(".swift"))
            {
                var sc = SwiftCrash.Match(err);
                if (!sc.Success) return -1;
                string what = sc.Groups[1].Success ? sc.Groups[1].Value : sc.Groups[2].Success ? sc.Groups[2].Value : sc.Groups[3].Value;
                ex = ExplainRuntime("swift:" + what.Trim());
                var fr = SwiftFrame.Matches(err).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
                if (fr != null) return int.Parse(fr.Groups[2].Value);
                var fl = Regex.Match(err, @"(?:^|\n)(?:\w+/)*" + Regex.Escape(srcName) + @":(\d+): ");   // Stazher/main.swift:4: Fatal error: …
                return fl.Success ? int.Parse(fl.Groups[1].Value) : -1;
            }
            // PHP: необработанное исключение или фатальная ошибка
            var pu = PhpUncaught.Match(err);
            if (pu.Success)
            {
                ex = ExplainRuntime("php:" + pu.Groups[1].Value + ": " + pu.Groups[2].Value);
                if (pu.Groups[3].Value == srcName) return int.Parse(pu.Groups[4].Value);
                var fr = PhpFrame.Matches(err).Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
                return fr != null ? int.Parse(fr.Groups[2].Value) : -1;
            }
            var pf = PhpFatal.Match(err);
            if (pf.Success) { ex = ExplainRuntime("php:" + pf.Groups[1].Value); return pf.Groups[2].Value == srcName ? int.Parse(pf.Groups[3].Value) : -1; }
            // Rust: паника и переполнение стека
            var rp = RustPanic.Match(err);
            if (rp.Success) { ex = ExplainRuntime("rust:" + rp.Groups[3].Value.Trim()); return rp.Groups[1].Value == srcName ? int.Parse(rp.Groups[2].Value) : -1; }
            if (err.Contains("has overflowed its stack")) { ex = "переполнение стека — похоже на бесконечную рекурсию (stack overflow)"; return -1; }
            // C++: санитайзеры и необработанное исключение
            var ub = UbsanErr.Match(err);
            if (ub.Success) { ex = ExplainRuntime(ub.Groups[3].Value); return ub.Groups[1].Value == srcName ? int.Parse(ub.Groups[2].Value) : CppFrame(err, srcName); }
            var asan = Regex.Match(err, @"ERROR: AddressSanitizer: ([\w-]+)");
            if (asan.Success) { ex = ExplainRuntime("asan:" + asan.Groups[1].Value); return CppFrame(err.Substring(asan.Index), srcName); }
            int tc = err.IndexOf("terminate called after throwing an instance of '", StringComparison.Ordinal);
            if (tc >= 0)
            {
                string first = lineAt(tc), type = first.Substring(first.IndexOf('\'') + 1).TrimEnd('\'');
                var wm = Regex.Match(err.Substring(tc), @"what\(\):\s*(.*)");
                ex = "необработанное исключение " + type + (wm.Success ? ": " + wm.Groups[1].Value.Trim() : "");
                return -1;
            }
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

        static int CppFrame(string report, string srcName)
        {
            var f = CppAt.Matches(report ?? "").Cast<Match>().FirstOrDefault(x => x.Groups[1].Value == srcName);
            return f != null ? int.Parse(f.Groups[2].Value) : -1;
        }

        // Сообщения санитайзеров C++ — по-русски
        static string ExplainRuntime(string e)
        {
            Match m;
            switch (e)
            {
                case "asan:heap-buffer-overflow": case "asan:stack-buffer-overflow": case "asan:global-buffer-overflow": return "выход за границы массива или vector (AddressSanitizer: " + e.Substring(5) + ")";
                case "asan:stack-overflow": return "переполнение стека — похоже на бесконечную рекурсию (AddressSanitizer: stack-overflow)";
                case "asan:SEGV": return "обращение по неверному адресу — nullptr или испорченный указатель (AddressSanitizer: SEGV)";
                case "asan:heap-use-after-free": return "обращение к уже удалённой памяти (AddressSanitizer: heap-use-after-free)";
                case "asan:stack-use-after-return": case "asan:stack-use-after-scope": return "обращение к переменной, которой уже нет — ссылка на локальную переменную? (AddressSanitizer: " + e.Substring(5) + ")";
                case "asan:attempting": case "asan:double-free": return "память освобождена дважды (AddressSanitizer: double-free)";
            }
            if (e.StartsWith("php:"))
            {
                e = e.Substring(4);
                string core = Regex.Replace(e, @"^(?:ErrorException|Error|\w+Error): ", "");
                if (e.StartsWith("DivisionByZeroError") || core.StartsWith("Division by zero") || core.StartsWith("Modulo by zero")) return "деление на ноль (" + e + ")";
                if ((m = Regex.Match(core, @"^Undefined variable (\$\w+)")).Success) return "переменная " + m.Groups[1].Value + " не объявлена — опечатка? (" + e + ")";
                if ((m = Regex.Match(core, @"^Undefined array key (.+)$")).Success) return "в массиве нет ключа " + m.Groups[1].Value + " (" + e + ")";
                if ((m = Regex.Match(core, @"^Call to undefined function (\w+)\(\)")).Success) return "функция " + m.Groups[1].Value + "() не найдена (" + e + ")";
                if ((m = Regex.Match(core, @"must be of type (\S+), (\S+) given")).Success) return "не тот тип: нужен " + m.Groups[1].Value + ", а передан " + m.Groups[2].Value + " (" + e + ")";
                if (core.StartsWith("Too few arguments")) return "передано меньше аргументов, чем ждёт функция (" + e + ")";
                if ((m = Regex.Match(core, @"^Unsupported operand types: (.+)$")).Success) return "оператор не работает с такими типами: " + m.Groups[1].Value + " (" + e + ")";
                if (core.StartsWith("Allowed memory size")) return "кончилась память — похоже на бесконечную рекурсию или цикл (" + e + ")";
                if (core.StartsWith("Maximum execution time")) return "программа работает слишком долго (" + e + ")";
                return e;
            }
            if (e.StartsWith("swift:"))
            {
                e = e.Substring(6);
                if (e == "Index out of range") return "выход за границы массива (Index out of range)";
                if (e.StartsWith("Unexpectedly found nil while")) return "в опционале nil, а значение достали через ! (" + e + ")";
                if (e == "Division by zero") return "деление на ноль (" + e + ")";
                if (e.StartsWith("Division by zero in remainder")) return "остаток от деления на ноль (" + e + ")";
                if (e == "arithmetic overflow") return "переполнение Int: результат не помещается в тип (arithmetic overflow)";
                if (e.StartsWith("Range requires lowerBound <= upperBound") || e.StartsWith("Can't form Range")) return "диапазон задом наперёд: нижняя граница больше верхней (" + e + ")";
                if (e.StartsWith("Bad pointer dereference")) return "переполнение стека — похоже на бесконечную рекурсию (" + e + ")";
                if (e.StartsWith("Double value cannot be converted") || e.StartsWith("Not enough bits")) return "дробное число не помещается в Int (" + e + ")";
                return "программа остановлена: " + e;
            }
            if (e.StartsWith("rust:"))
            {
                e = e.Substring(5);
                if (e == "attempt to divide by zero" || e.StartsWith("attempt to calculate the remainder with a divisor of zero")) return "деление на ноль (" + e + ")";
                if ((m = Regex.Match(e, @"^attempt to (add|subtract|multiply|negate|shift left|shift right) with overflow$")).Success) return "переполнение числа: результат не помещается в тип (" + e + ")";
                if ((m = Regex.Match(e, @"^index out of bounds: the len is (\d+) but the index is (\d+)")).Success) return "индекс " + m.Groups[2].Value + " за границами: длина всего " + m.Groups[1].Value + " (" + e + ")";
                if (e.StartsWith("called `Option::unwrap()` on a `None` value")) return "unwrap() у None — значения нет (" + e + ")";
                if (e.StartsWith("called `Result::unwrap()` on an `Err` value")) return "unwrap() у Err — операция не удалась (" + e + ")";
                return "паника: " + e;
            }
            if (e.StartsWith("asan:")) return "ошибка работы с памятью (AddressSanitizer: " + e.Substring(5) + ")";
            if (e.StartsWith("division by zero")) return "деление на ноль (" + e + ")";
            if ((m = Regex.Match(e, @"^signed integer overflow: (.+) cannot be represented in type '(.+)'$")).Success) return "переполнение: " + m.Groups[1].Value + " не помещается в " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^index (-?\d+) out of bounds")).Success) return "индекс " + m.Groups[1].Value + " за границами массива (" + e + ")";
            if (e.Contains("null pointer")) return "обращение через nullptr (" + e + ")";
            return e;
        }

        // Строка ошибки из вывода запуска (компиляция или падение); 0 — ошибки нет
        public static int ErrorLine(string stderr, string srcName, out string msg)
        {
            msg = null;
            if (srcName.EndsWith(".java") || srcName.EndsWith(".cs") || srcName.EndsWith(".cpp") || srcName.EndsWith(".rs") || srcName.EndsWith(".php") || srcName.EndsWith(".kt") || srcName.EndsWith(".swift"))
            {
                var ce = CompileErrors(stderr).FirstOrDefault(e => e.file == srcName);
                if (ce != null) { msg = "Ошибка компиляции в строке " + ce.line + ": " + ce.text; return ce.line; }
                string ex; int ln = RuntimeError(stderr, srcName, out ex);
                if (ex != null) { msg = "Программа упала: " + ex; return ln; }
                var pw = PhpWarn.Match(stderr ?? "");   // PHP продолжает работу после предупреждения, но строку стоит подсветить
                if (pw.Success && pw.Groups[2].Value == srcName) { msg = "Предупреждение в строке " + pw.Groups[3].Value + ": " + ExplainRuntime("php:" + pw.Groups[1].Value); return int.Parse(pw.Groups[3].Value); }
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
            // kotlinc (Kotlin)
            if ((m = Regex.Match(e, @"^unresolved reference '(\w+)'$")).Success) return "имя " + m.Groups[1].Value + " не найдено: опечатка или не объявлено (" + e + ")";
            if ((m = Regex.Match(e, @"type mismatch: expected '(.+?)', actual '(.+?)'")).Success) return "не тот тип: ожидался " + m.Groups[1].Value + ", а получен " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"type mismatch: actual type is '(.+?)', but '(.+?)' was expected")).Success) return "не тот тип: ожидался " + m.Groups[2].Value + ", а получен " + m.Groups[1].Value + " (" + e + ")";
            if (e == "'val' cannot be reassigned") return "val нельзя переприсвоить — объяви переменную через var (" + e + ")";
            if ((m = Regex.Match(e, @"^only safe \(\?\.\) or non-null asserted \(!!\.\) calls are allowed on a nullable receiver of type '(.+?)'")).Success) return "значение типа " + m.Groups[1].Value + " может быть null — используй ?. или проверь на null (" + e + ")";
            if (e.StartsWith("'when' expression must be exhaustive")) return "when должен разобрать все варианты — добавь ветку else (" + e + ")";
            if ((m = Regex.Match(e, @"^no value passed for parameter '(\w+)'$")).Success) return "не передан аргумент " + m.Groups[1].Value + " (" + e + ")";
            if (e.StartsWith("too many arguments for")) return "передано слишком много аргументов (" + e + ")";
            if (e.StartsWith("none of the following candidates is applicable") || e.StartsWith("none of the following functions can be called")) return "нет подходящей функции для таких аргументов (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot access '(\w+)': it is private")).Success) return m.Groups[1].Value + " закрыт (private) (" + e + ")";
            // swiftc (Swift)
            if ((m = Regex.Match(e, @"^cannot find '(\w+)' in scope$")).Success) return "имя " + m.Groups[1].Value + " не найдено: опечатка или не объявлено (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot find type '(\w+)' in scope$")).Success) return "тип " + m.Groups[1].Value + " не найден: опечатка или не объявлен (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot assign to (?:value|property): '(\w+)' is a 'let' constant$")).Success) return m.Groups[1].Value + " объявлена через let — чтобы менять, объяви через var (" + e + ")";
            if ((m = Regex.Match(e, @"^left side of mutating operator isn't mutable: '(\w+)' is a 'let' constant$")).Success) return m.Groups[1].Value + " объявлена через let — чтобы менять, объяви через var (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot use mutating member on immutable value: '(\w+)' is a 'let' constant$")).Success) return m.Groups[1].Value + " — let: mutating-метод можно вызвать только у var (" + e + ")";
            if (e.StartsWith("cannot assign to value: '") && e.Contains("is immutable")) return "параметры функции — константы, их нельзя менять: заведи var-копию (" + e + ")";
            if ((m = Regex.Match(e, @"^value of optional type '(.+?)' must be unwrapped")).Success) return "значение типа " + m.Groups[1].Value + " может быть nil — разверни его: ?., ?? или if let (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot convert (?:value|return expression) of type '(.+?)' to (?:specified|expected argument|return|expected element) type '(.+?)'")).Success) return "не тот тип: ожидался " + m.Groups[2].Value + ", а получен " + m.Groups[1].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^binary operator '(.+?)' cannot be applied to operands of type '(.+?)' and '(.+?)'$")).Success) return "оператор " + m.Groups[1].Value + " не работает с " + m.Groups[2].Value + " и " + m.Groups[3].Value + " — Swift не смешивает типы, преобразуй явно: Double(x), String(x) (" + e + ")";
            if (e.StartsWith("missing return in")) return "функция должна вернуть значение: не хватает return (" + e + ")";
            if ((m = Regex.Match(e, @"^missing argument label '(\w+):' in call$")).Success) return "при вызове нужна метка " + m.Groups[1].Value + ": (" + e + ")";
            if ((m = Regex.Match(e, @"^extraneous argument label '(\w+):' in call$")).Success) return "лишняя метка " + m.Groups[1].Value + ": — у этого параметра метки нет (_) (" + e + ")";
            if (e.StartsWith("missing argument for parameter")) return "не передан аргумент (" + e + ")";
            if (e.StartsWith("extra argument")) return "передан лишний аргумент (" + e + ")";
            if (e.StartsWith("call can throw but is not marked with 'try'")) return "функция может бросить ошибку — вызывай её через try (" + e + ")";
            if (e.StartsWith("errors thrown from here are not handled")) return "ошибку отсюда никто не ловит — оберни в do/catch или используй try? (" + e + ")";
            if (e.StartsWith("switch must be exhaustive")) return "switch должен разобрать все варианты — добавь default (" + e + ")";
            if ((m = Regex.Match(e, @"^'(\w+)' is inaccessible due to 'private' protection level$")).Success) return m.Groups[1].Value + " закрыт (private) (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot assign to property: '(\w+)' setter is inaccessible")).Success) return m.Groups[1].Value + " снаружи можно только читать (private(set)) (" + e + ")";
            if ((m = Regex.Match(e, @"^type '(\w+)' does not conform to protocol '(\w+)'$")).Success) return "тип " + m.Groups[1].Value + " не выполняет договор " + m.Groups[2].Value + ": не хватает метода или свойства (" + e + ")";
            if ((m = Regex.Match(e, @"^expected '(.+?)'")).Success && !e.EndsWith(" expected")) return "не хватает «" + m.Groups[1].Value + "» (" + e + ")";
            if (e.StartsWith("consecutive statements on a line must be separated by")) return "две команды в одной строке — пропущен перенос строки или оператор (" + e + ")";
            // php -l (PHP)
            if ((m = Regex.Match(e, "^syntax error, unexpected (?:token )?\"(.+?)\", expecting \"(.+?)\"$")).Success) return "синтаксическая ошибка: перед «" + m.Groups[1].Value + "» не хватает «" + m.Groups[2].Value + "» (" + e + ")";
            if (e.StartsWith("syntax error, unexpected end of file")) return "файл закончился раньше времени — не закрыта скобка } или кавычка (" + e + ")";
            if ((m = Regex.Match(e, "^syntax error, unexpected identifier \"(\\w+)\"")).Success) return "неожиданное имя " + m.Groups[1].Value + " — пропущена ; на прошлой строке или $ перед переменной? (" + e + ")";
            if ((m = Regex.Match(e, "^syntax error, unexpected variable \"(\\$\\w+)\"")).Success) return "неожиданная переменная " + m.Groups[1].Value + " — пропущена ; на прошлой строке? (" + e + ")";
            if (e.StartsWith("syntax error")) return "синтаксическая ошибка (" + e + ")";
            if (e.StartsWith("cannot use ")) return "не тот тип значения (" + e + ")";
            if (e == "mismatched types") return "не тот тип (" + e + ")";   // rustc: подробности — в строке «ожидался…»
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
            // rustc (Rust)
            if ((m = Regex.Match(e, @"^cannot find (?:value|function|type|macro) `(\w+)` in this scope$")).Success) return "имя " + m.Groups[1].Value + " не найдено: опечатка или не объявлено (" + e + ")";
            if (e == "mismatched types") return "не тот тип (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot assign twice to immutable variable `(\w+)`$")).Success) return m.Groups[1].Value + " объявлена без mut — менять её нельзя (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot borrow `(.+?)` as mutable, as it is not declared as mutable$")).Success) return m.Groups[1].Value + " не объявлена как mut (" + e + ")";
            if ((m = Regex.Match(e, @"^(?:borrow|use) of moved value: `(.+?)`$")).Success) return m.Groups[1].Value + " уже перемещена в другое место и больше не твоя (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot borrow `(.+?)` as mutable more than once at a time$")).Success) return m.Groups[1].Value + " нельзя изменять из двух мест сразу (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot borrow `(.+?)` as (?:im)?mutable because it is also borrowed as")).Success) return m.Groups[1].Value + " уже занята другой ссылкой (" + e + ")";
            if ((m = Regex.Match(e, @"^`(.+?)` does not live long enough$")).Success) return m.Groups[1].Value + " живёт меньше, чем ссылка на неё (" + e + ")";
            if ((m = Regex.Match(e, @"^expected `(.+?)`, found (.+)$")).Success) return "здесь ожидался «" + m.Groups[1].Value + "» (" + e + ")";
            if ((m = Regex.Match(e, @"^this function takes (\d+) arguments? but (\d+) arguments? (?:was|were) supplied$")).Success) return "функция принимает " + m.Groups[1].Value + " аргумент(а), а передано " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^no method named `(\w+)` found for (.+)$")).Success) return "у " + m.Groups[2].Value + " нет метода " + m.Groups[1].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^no field `(\w+)` on type `(.+?)`$")).Success) return "у " + m.Groups[2].Value + " нет поля " + m.Groups[1].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^cannot (add|subtract|multiply|divide) `(.+?)` (?:to|from|by) `(.+?)`$")).Success) return "нельзя смешивать " + m.Groups[2].Value + " и " + m.Groups[3].Value + " — приведи типы через as (" + e + ")";
            if ((m = Regex.Match(e, @"^non-exhaustive patterns: (.+) not covered$")).Success) return "match разбирает не все варианты: не покрыт " + m.Groups[1].Value + " (" + e + ")";
            if (e.StartsWith("the `?` operator can only be used")) return "? можно писать только в функции, которая сама возвращает Result или Option (" + e + ")";
            // g++ (C++)
            if ((m = Regex.Match(e, @"^'(\w+)' was not declared in this scope(?:; did you mean '(\w+)'\?)?$")).Success) return "имя " + m.Groups[1].Value + " не объявлено" + (m.Groups[2].Success ? " — может, " + m.Groups[2].Value + "?" : ": опечатка или нет #include") + " (" + e + ")";
            if ((m = Regex.Match(e, @"^expected '(.+?)' before (.+)$")).Success) return "не хватает «" + m.Groups[1].Value + "» (" + e + ")";
            if (e.StartsWith("control reaches end of non-void function") || e.StartsWith("no return statement in function returning non-void")) return "не все ветки функции возвращают значение: не хватает return (" + e + ")";
            if ((m = Regex.Match(e, @"^(?:invalid conversion from|cannot convert) '(.+?)' to '(.+?)'")).Success) return "нельзя превратить " + m.Groups[1].Value + " в " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^'(\w+)' is not a member of '(.+?)'(?:; did you mean '(\w+)'\?)?")).Success) return "в " + m.Groups[2].Value + " нет " + m.Groups[1].Value + (m.Groups[3].Success ? " — может, " + m.Groups[3].Value + "?" : ": опечатка или нет #include") + " (" + e + ")";
            if ((m = Regex.Match(e, @"^'(\w+)' (?:in namespace '\w+' )?does not name a type")).Success) return m.Groups[1].Value + " — не тип: опечатка или нет #include (" + e + ")";
            if ((m = Regex.Match(e, @"^no matching function for call to '(.+)'$")).Success) return "нет подходящей функции для вызова " + m.Groups[1].Value + ": не те аргументы? (" + e + ")";
            if ((m = Regex.Match(e, @"^'(.+?)' is private within this context$")).Success) return m.Groups[1].Value + " закрыт (private) — снаружи класса его не видно (" + e + ")";
            if (e.StartsWith("passing 'const ") && e.Contains("discards qualifiers")) return "метод вызывается у const-объекта, но сам не помечен const (" + e + ")";
            if ((m = Regex.Match(e, @"^assignment of read-only (?:variable|location|member) '(.+?)'")).Success) return m.Groups[1].Value + " объявлен const — менять нельзя (" + e + ")";
            if ((m = Regex.Match(e, @"^(?:redeclaration|conflicting declaration) of '(.+?)'")).Success) return m.Groups[1].Value + " уже объявлен (" + e + ")";
            if ((m = Regex.Match(e, @"^invalid operands of types '(.+?)' and '(.+?)' to binary 'operator(.+?)'$")).Success) return "оператор " + m.Groups[3].Value + " не работает с " + m.Groups[1].Value + " и " + m.Groups[2].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^no match for 'operator(.+?)'")).Success) return "для этих типов нет оператора " + m.Groups[1].Value + " (" + e + ")";
            if ((m = Regex.Match(e, @"^'(?:class|struct) (.+?)' has no member named '(\w+)'(?:; did you mean '(\w+)'\?)?")).Success) return "у " + m.Groups[1].Value + " нет члена " + m.Groups[2].Value + (m.Groups[3].Success ? " — может, " + m.Groups[3].Value + "?" : "") + " (" + e + ")";
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
        // Причина неудачной сборки: строка с ERROR (BuildKit) или последняя содержательная — не ссылка «View build details»
        static string BuildError(string text)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("View build details")).ToArray();
            var err = lines.FirstOrDefault(l => l.StartsWith("ERROR") || l.Contains("failed to solve") || l.StartsWith("E: "));
            return err ?? (lines.Length > 0 ? lines[lines.Length - 1] : "нет вывода");
        }
        static string FirstLine(string s) { s = (s ?? "").Trim(); int i = s.IndexOf('\n'); return i > 0 ? s.Substring(0, i) : s; }
        static string S(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) ? v as string : null; }
    }
}
