// Выполнение SQL-задач игрока через системный SQLite (без сторонних пакетов, P/Invoke):
//   Windows 10/11 — встроенная winsqlite3.dll (System32), macOS — libsqlite3.dylib, Linux — libsqlite3.so(.0).
// Каждый запуск — новая база в памяти (":memory:"): скрипт создаёт таблицы, наполняет их и делает запрос.
// Поведение повторяет проверку контента (spec/validate.py, модуль sqlite3 Python): результат — таблица
// ПОСЛЕДНЕЙ инструкции, возвращающей строки; неявный BEGIN перед INSERT/UPDATE/DELETE/REPLACE.
// Run блокирует поток до TimeoutMs — для долгих запросов можно звать из фонового потока (каждый Run независим).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
#if UNITY_5_3_OR_NEWER
using AOT;
#endif

namespace Intern.Game
{
    // Результат скрипта: таблица последнего SELECT (или ошибка).
    public sealed class SqlResult
    {
        public string Error;                 // null — успех; иначе «SQL: <сообщение SQLite> (строка N)»
        public int ErrorLine;                // строка скрипта (с 1), где началась упавшая инструкция; 0 — нет
        public List<string> Columns;         // null — ни одна инструкция не вернула строк
        public List<List<object>> Rows = new List<List<object>>();   // long / double / string / byte[] / null
        public int TotalRows;                // сколько строк вернул запрос (Rows хранит не больше SqlRun.MaxRows)
        public bool Truncated;               // строк больше SqlRun.CountLimit — чтение остановлено
        public int Statements;               // сколько инструкций выполнено
        public double Millis;
        public bool Ok { get { return Error == null; } }
        public bool HasTable { get { return Columns != null; } }
    }

    // Проверка результата против ожидаемых строк (по форме как Intern.Py.CheckResult).
    public sealed class SqlCheck
    {
        public bool Passed;
        public string Expected, Actual, Note;   // Expected/Actual — готовые ASCII-таблицы для показа
        public SqlResult Result;
    }

    public static class SqlRun
    {
        public static int TimeoutMs = 3000;       // весь скрипт; дольше — прерываем (sqlite3_progress_handler)
        public static int MaxRows = 1000;         // строк результата хранится
        public static int CountLimit = 100000;    // дальше строки даже не считаем (бесконечный WITH RECURSIVE)
        public static int MaxValueBytes = 10000000;   // SQLITE_LIMIT_LENGTH: самая длинная строка/BLOB
        public static int DisplayRows = 20;       // строк в таблице терминала
        public static int MaxCellWidth = 40;
        public static bool ImplicitTransactions = true;   // как sqlite3 в Python (isolation_level="") — так проверяется контент

        // ================== Загрузка библиотеки ==================
        static Native api;
        static bool tried;
        static string loadError;
        static readonly object loadLock = new object();

        public static bool Available { get { return Load() != null; } }
        public static string Library { get { var n = Load(); return n != null ? n.Name : null; } }   // "winsqlite3" | "/usr/lib/libsqlite3.dylib" | "sqlite3" | "libsqlite3.so.0"
        public static string Version { get { var n = Load(); return n != null ? Utf8(n.libversion(), -1) : null; } }
        public static string LoadError { get { Load(); return loadError; } }

        // Пробуем библиотеки по очереди: winsqlite3 (Windows 10/11, System32), /usr/lib/libsqlite3.dylib (macOS),
        // sqlite3 (Linux/macOS или своя sqlite3.dll в Assets/Plugins), libsqlite3.so.0 (Linux без dev-пакета). Проба гоняет все нужные функции,
        // так что отсутствие любой точки входа (EntryPointNotFound) тоже переключает на следующую библиотеку.
        static Native Load()
        {
            lock (loadLock)
            {
                if (tried) return api;
                tried = true;
                var errs = new List<string>();
                foreach (Native n in new Native[] { new WinNative(), new MacNative(), new CNative(), new SoNative() })
                {
                    try
                    {
                        var r = new SqlResult();
                        Exec(n, "SELECT 1, 2.5, 'ёж', x'00ff', NULL", r, 5000, 10, 10);
                        var row = r.Rows.Count == 1 ? r.Rows[0] : null;
                        if (r.Ok && row != null && row.Count == 5 && Equals(row[0], 1L) && Equals(row[1], 2.5) && Equals(row[2], "ёж")
                            && row[3] is byte[] && ((byte[])row[3]).Length == 2 && row[4] == null)
                        { api = n; loadError = null; return api; }
                        errs.Add(n.Name + ": проба вернула " + (r.Error ?? "не то"));
                    }
                    catch (DllNotFoundException) { errs.Add(n.Name + ": не найдена"); }
                    catch (EntryPointNotFoundException e) { errs.Add(n.Name + ": нет функции " + e.Message); }
                    catch (BadImageFormatException) { errs.Add(n.Name + ": не та разрядность"); }
                    catch (Exception e) { errs.Add(n.Name + ": " + e.GetType().Name + " " + e.Message); }
                }
                loadError = "SQLite недоступен на этой системе (" + string.Join("; ", errs.ToArray()) + ")";
                return null;
            }
        }

        // ================== Запуск ==================
        public static SqlResult Run(string script)
        {
            var sw = Stopwatch.StartNew();
            var r = new SqlResult();
            var n = Load();
            if (n == null) r.Error = "SQL: " + loadError;
            else
            {
                try { Exec(n, script ?? "", r, TimeoutMs, MaxRows, CountLimit); }
                catch (Exception e) { r.Error = "SQL: внутренняя ошибка игры (" + e.GetType().Name + ": " + e.Message + "). Сообщи разработчику :)"; }
            }
            if (r.Error != null) { r.Columns = null; r.Rows = new List<List<object>>(); r.TotalRows = 0; r.Truncated = false; }
            r.Millis = sw.Elapsed.TotalMilliseconds;
            return r;
        }

        const int OK = 0, INTERRUPT = 9, ROW = 100, DONE = 101;
        const int T_INT = 1, T_FLOAT = 2, T_TEXT = 3, T_BLOB = 4;
        const int LIMIT_LENGTH = 0, LIMIT_ATTACHED = 7;

        [ThreadStatic] static long deadline;
        [ThreadStatic] static bool timedOut;

        // Зовётся из SQLite каждые ProgressOps инструкций VM; не 0 — прервать запрос (SQLITE_INTERRUPT).
        internal static int ProgressTick()
        {
            if (Stopwatch.GetTimestamp() <= deadline) return 0;
            timedOut = true;
            return 1;
        }
        const int ProgressOps = 4000;

        static void Exec(Native n, string script, SqlResult r, int timeoutMs, int maxRows, int countLimit)
        {
            if (script.Length > 0 && script[0] == '\uFEFF') script = script.Substring(1);
            byte[] sql = Encoding.UTF8.GetBytes(script);
            IntPtr buf = Marshal.AllocHGlobal(sql.Length + 1);
            IntPtr db = IntPtr.Zero;
            deadline = timeoutMs > 0 ? Stopwatch.GetTimestamp() + (long)timeoutMs * Stopwatch.Frequency / 1000 : long.MaxValue;   // ≤ 0 — без лимита
            timedOut = false;
            try
            {
                Marshal.Copy(sql, 0, buf, sql.Length);
                Marshal.WriteByte(buf, sql.Length, 0);
                int rc = n.open(Encoding.UTF8.GetBytes(":memory:\0"), out db);
                if (rc != OK) { r.Error = "SQL: не удалось открыть базу в памяти (код " + rc + ")"; return; }
                n.limit(db, LIMIT_ATTACHED, 0);          // без ATTACH / VACUUM INTO: никаких файлов на диске игрока
                n.limit(db, LIMIT_LENGTH, MaxValueBytes);
                n.progress_handler(db, ProgressOps, true);

                int off = 0;
                while (off < sql.Length)
                {
                    int start = SkipSpace(sql, off);
                    if (start >= sql.Length) break;
                    IntPtr stmt, tail;
                    // nByte с учётом завершающего \0: иначе SQLite копирует весь хвост скрипта на каждой инструкции (O(n²))
                    rc = n.prepare_v2(db, buf + off, sql.Length - off + 1, out stmt, out tail);
                    if (rc != OK) { Fail(n, db, rc, r, sql, start); return; }
                    int next = tail == IntPtr.Zero ? sql.Length : (int)(tail.ToInt64() - buf.ToInt64());
                    if (stmt == IntPtr.Zero) { if (next <= off) break; off = next; continue; }   // только комментарии / «;»
                    try
                    {
                        if (ImplicitTransactions && IsDml(sql, start) && n.get_autocommit(db) != 0)
                        {
                            string err = Simple(n, db, "BEGIN");
                            if (err != null) { r.Error = "SQL: " + err + " (строка " + Line(sql, start) + ")"; r.ErrorLine = Line(sql, start); return; }
                        }
                        r.Statements++;
                        int cols = n.column_count(stmt);
                        List<string> names = null; List<List<object>> rows = null;
                        int total = 0; bool trunc = false;
                        if (cols > 0)
                        {
                            names = new List<string>(cols);
                            for (int i = 0; i < cols; i++) names.Add(Utf8(n.column_name(stmt, i), -1));
                            rows = new List<List<object>>();
                        }
                        while (true)
                        {
                            rc = n.step(stmt);
                            if (rc == ROW)
                            {
                                if (cols == 0) continue;
                                total++;
                                if (rows.Count < maxRows)
                                {
                                    var row = new List<object>(cols);
                                    for (int i = 0; i < cols; i++) row.Add(Value(n, stmt, i));
                                    rows.Add(row);
                                }
                                else if (total >= countLimit) { trunc = true; break; }
                            }
                            else if (rc == DONE) break;
                            else { Fail(n, db, rc, r, sql, start); return; }
                        }
                        if (cols > 0) { r.Columns = names; r.Rows = rows; r.TotalRows = total; r.Truncated = trunc; }
                    }
                    finally { n.finalize(stmt); }
                    if (Stopwatch.GetTimestamp() > deadline && next < sql.Length && SkipSpace(sql, next) < sql.Length)
                    {   // много коротких инструкций: progress_handler внутри каждой не успевает сработать
                        timedOut = true; Fail(n, db, INTERRUPT, r, sql, SkipSpace(sql, next)); return;
                    }
                    off = next;
                }
            }
            finally
            {
                if (db != IntPtr.Zero) { n.progress_handler(db, 0, false); n.close(db); }
                Marshal.FreeHGlobal(buf);
            }
        }

        static void Fail(Native n, IntPtr db, int rc, SqlResult r, byte[] sql, int start)
        {
            string msg = Utf8(n.errmsg(db), -1);
            if ((rc & 0xff) == INTERRUPT && timedOut)
                msg = "запрос выполнялся дольше " + (TimeoutMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture)
                    + " с и был прерван — проверь условия JOIN и рекурсию (WITH RECURSIVE без условия остановки)";
            else if (msg.StartsWith("too many attached databases"))
                msg = "ATTACH и VACUUM в игре отключены: база живёт только в памяти";
            r.ErrorLine = Line(sql, start);
            r.Error = "SQL: " + msg + " (строка " + r.ErrorLine + ")";
        }

        // Выполнить служебную инструкцию без результата; вернуть текст ошибки или null.
        static string Simple(Native n, IntPtr db, string text)
        {
            IntPtr stmt, tail;
            byte[] b = Encoding.UTF8.GetBytes(text + "\0");
            IntPtr p = Marshal.AllocHGlobal(b.Length);
            try
            {
                Marshal.Copy(b, 0, p, b.Length);
                int rc = n.prepare_v2(db, p, b.Length, out stmt, out tail);
                if (rc != OK) return Utf8(n.errmsg(db), -1);
                rc = n.step(stmt);
                string err = rc == DONE || rc == ROW ? null : Utf8(n.errmsg(db), -1);
                n.finalize(stmt);
                return err;
            }
            finally { Marshal.FreeHGlobal(p); }
        }

        static object Value(Native n, IntPtr stmt, int i)
        {
            switch (n.column_type(stmt, i))
            {
                case T_INT: return n.column_int64(stmt, i);
                case T_FLOAT: return n.column_double(stmt, i);
                case T_TEXT: { IntPtr p = n.column_text(stmt, i); return Utf8(p, n.column_bytes(stmt, i)); }
                case T_BLOB:
                    {
                        IntPtr p = n.column_blob(stmt, i); int len = n.column_bytes(stmt, i);
                        var b = new byte[len]; if (len > 0 && p != IntPtr.Zero) Marshal.Copy(p, b, 0, len);
                        return b;
                    }
                default: return null;
            }
        }

        static string Utf8(IntPtr p, int len)
        {
            if (p == IntPtr.Zero) return "";
            if (len < 0) { len = 0; while (Marshal.ReadByte(p, len) != 0) len++; }
            if (len == 0) return "";
            var b = new byte[len];
            Marshal.Copy(p, b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        // Пропустить пробелы и комментарии (-- … и /* … */) — туда, где реально начинается инструкция.
        static int SkipSpace(byte[] s, int i)
        {
            while (i < s.Length)
            {
                byte c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v') { i++; continue; }
                if (c == '-' && i + 1 < s.Length && s[i + 1] == '-') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    i += 2;
                    while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/')) i++;
                    i = Math.Min(s.Length, i + 2);
                    continue;
                }
                break;
            }
            return i;
        }

        static int Line(byte[] s, int pos)
        {
            int line = 1;
            for (int i = 0; i < pos && i < s.Length; i++) if (s[i] == '\n') line++;
            return line;
        }

        // Как в Python sqlite3: неявный BEGIN только перед инструкциями, начинающимися с INSERT/UPDATE/DELETE/REPLACE.
        static bool IsDml(byte[] s, int i)
        {
            return Prefix(s, i, "insert") || Prefix(s, i, "update") || Prefix(s, i, "delete") || Prefix(s, i, "replace");
        }
        static bool Prefix(byte[] s, int i, string w)
        {
            if (i + w.Length > s.Length) return false;
            for (int k = 0; k < w.Length; k++) if (char.ToLowerInvariant((char)s[i + k]) != w[k]) return false;
            return true;
        }

        // ================== Проверка ==================
        // expectedRows — test_cases[i].expected из JSON: список строк, каждая — список значений
        // (long / double / string / bool / null). Числа сравниваются как числа (3 == 3.0), дробные — с допуском 1e-6.
        public static SqlCheck Check(string script, IList expectedRows)
        {
            var r = Run(script);
            var c = new SqlCheck { Result = r };
            int ecols = expectedRows != null && expectedRows.Count > 0 && expectedRows[0] is IList ? ((IList)expectedRows[0]).Count : -1;
            var hdr = r.HasTable && (ecols < 0 || ecols == r.Columns.Count) ? r.Columns : null;
            c.Expected = expectedRows == null ? "(нет ожидания)" : FormatTable(hdr, ToRows(expectedRows), expectedRows.Count, false, DisplayRows);
            if (!r.Ok) { c.Actual = r.Error; c.Note = "Скрипт упал с ошибкой в строке " + r.ErrorLine + "."; return c; }
            if (!r.HasTable) { c.Actual = "(нет таблицы)"; c.Note = "Последняя инструкция не вернула строк. Допиши в конце запрос SELECT."; return c; }
            c.Actual = FormatTable(r.Columns, r.Rows, r.TotalRows, r.Truncated, DisplayRows);
            if (expectedRows == null) { c.Note = "У задачи нет ожидаемого результата."; return c; }
            if (r.Truncated || r.TotalRows > r.Rows.Count)
            { c.Note = "Запрос вернул слишком много строк (" + r.TotalRows + (r.Truncated ? "+" : "") + ") — проверь условия JOIN и WHERE."; return c; }
            string note;
            c.Passed = RowsEqual(expectedRows, r.Rows, r.Columns, out note);
            c.Note = note;
            return c;
        }

        public static bool RowsEqual(IList expected, List<List<object>> actual, IList<string> columns, out string note)
        {
            note = null;
            int ne = expected.Count, na = actual.Count;
            if (ne != na)
            {
                if (na == 0) note = "Запрос не вернул ни одной строки, а ожидалось " + Rows(ne) + ". Проверь условия WHERE и JOIN.";
                else if (na > ne) note = "Получилось " + Rows(na) + ", а нужно " + ne + ". Лишние строки: не хватает условия в WHERE, или JOIN размножил строки (нужны GROUP BY / DISTINCT?).";
                else note = "Получилось " + Rows(na) + ", а нужно " + ne + ". Не хватает строк: условие слишком строгое или нужен LEFT JOIN.";
                return false;
            }
            for (int i = 0; i < ne; i++)
            {
                var er = expected[i] as IList; var ar = actual[i];
                if (er != null && RowEquals(er, ar)) continue;
                if (SameMultiset(expected, actual)) { note = "Строки верные, но порядок другой. Проверь ORDER BY (направление ASC/DESC и поля)."; return false; }
                if (er == null) { note = "Строка " + (i + 1) + ": ожидание задачи записано неверно. Сообщи разработчику :)"; return false; }
                if (er.Count != ar.Count)
                { note = "В строке " + (i + 1) + " колонок " + ar.Count + ", а нужно " + er.Count + ". Проверь список полей после SELECT."; return false; }
                for (int j = 0; j < er.Count; j++)
                {
                    if (ValueEquals(ar[j], er[j])) continue;
                    string col = columns != null && j < columns.Count ? " («" + columns[j] + "»)" : "";
                    note = "Строка " + (i + 1) + ", колонка " + (j + 1) + col + ": ожидалось " + Literal(er[j]) + ", а получилось " + Literal(ar[j]) + "." + TypeHint(er[j], ar[j]);
                    return false;
                }
            }
            return true;
        }

        static bool RowEquals(IList e, IList a)
        {
            if (e.Count != a.Count) return false;
            for (int j = 0; j < e.Count; j++) if (!ValueEquals(a[j], e[j])) return false;
            return true;
        }

        static bool SameMultiset(IList expected, List<List<object>> actual)
        {
            var used = new bool[actual.Count];
            foreach (var eo in expected)
            {
                var e = eo as IList; if (e == null) return false;
                bool found = false;
                for (int k = 0; k < actual.Count && !found; k++)
                    if (!used[k] && RowEquals(e, actual[k])) { used[k] = true; found = true; }
                if (!found) return false;
            }
            return true;
        }

        static string TypeHint(object e, object a)
        {
            if (IsNum(e) && a is string) return " Получилась строка, а нужно число — возможно, лишние кавычки или printf.";
            if (e is string && IsNum(a)) return " Получилось число, а нужна строка.";
            if (IsNum(e) && a == null) return " Получился NULL — возможно, нужен COALESCE или LEFT JOIN дал пустые значения.";
            if (IsInt(e) && a is double) return " Получилось дробное число — проверь деление и ROUND.";
            return "";
        }

        // Равенство значений как в spec/validate.py (eq): если хоть одно дробное — сравнение чисел с допуском
        // (строка-число тоже приводится, как float() в Python); bool не равен числу; иначе точное равенство.
        public static bool ValueEquals(object a, object b)
        {
            if (a is double || a is float || b is double || b is float)
            {
                double x, y;
                if (!ToDouble(a, out x) || !ToDouble(b, out y)) return false;
                if (x == y) return true;
                if (double.IsInfinity(x) || double.IsInfinity(y) || double.IsNaN(x) || double.IsNaN(y)) return false;
                return Math.Abs(x - y) <= Math.Max(1e-6 * Math.Max(Math.Abs(x), Math.Abs(y)), 1e-6);
            }
            if ((a is bool) != (b is bool)) return false;
            if (a is bool) return (bool)a == (bool)b;
            if (IsInt(a) && IsInt(b)) return Convert.ToInt64(a) == Convert.ToInt64(b);
            if (a == null || b == null) return a == null && b == null;
            if (a is string && b is string) return string.Equals((string)a, (string)b, StringComparison.Ordinal);
            if (a is byte[] && b is byte[])
            {
                var p = (byte[])a; var q = (byte[])b;
                if (p.Length != q.Length) return false;
                for (int i = 0; i < p.Length; i++) if (p[i] != q[i]) return false;
                return true;
            }
            var la = a as IList; var lb = b as IList;
            if (la != null && lb != null) return RowEquals(lb, la);
            return a.Equals(b);
        }

        static bool IsInt(object o) { return o is long || o is int || o is short || o is sbyte || o is byte || o is ushort || o is uint; }
        static bool IsNum(object o) { return IsInt(o) || o is double || o is float; }

        static bool ToDouble(object o, out double d)
        {
            d = 0;
            if (o is double) { d = (double)o; return true; }
            if (o is float) { d = (float)o; return true; }
            if (o is bool) { d = (bool)o ? 1 : 0; return true; }
            if (IsInt(o)) { d = Convert.ToInt64(o); return true; }
            var s = o as string;
            if (s == null) return false;
            s = s.Trim().Replace("_", "");
            string l = s.ToLowerInvariant().TrimStart('+');
            if (l == "inf" || l == "infinity") { d = double.PositiveInfinity; return true; }
            if (l == "-inf" || l == "-infinity") { d = double.NegativeInfinity; return true; }
            if (l == "nan" || l == "-nan") { d = double.NaN; return true; }
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        }

        // ================== Форматирование для терминала ==================
        public static string Format(SqlResult r) { return Format(r, DisplayRows); }

        public static string Format(SqlResult r, int maxRows)
        {
            if (r == null) return "";
            if (!r.Ok) return r.Error;
            if (!r.HasTable)
                return "Готово, выполнено инструкций: " + r.Statements + ". Запроса с результатом (SELECT) нет — таблицу показать нечем.";
            return FormatTable(r.Columns, r.Rows, r.TotalRows, r.Truncated, maxRows);
        }

        //  +----+------------+
        //  | id | created_at |
        //  +----+------------+
        //  |  3 | 2026-08-28 |
        //  +----+------------+
        //  1 строка
        // columns == null — без заголовка. totalRows — сколько строк всего (для «… и ещё N»).
        public static string FormatTable(IList<string> columns, IList<IList> rows, int totalRows, bool truncated, int maxRows)
        {
            rows = rows ?? new List<IList>();
            int shown = Math.Min(rows.Count, Math.Max(0, maxRows));
            int ncol = columns != null ? columns.Count : 0;
            for (int i = 0; i < shown; i++) if (rows[i] != null) ncol = Math.Max(ncol, rows[i].Count);
            var cells = new List<string[]>(); var right = new bool[ncol]; var w = new int[ncol];
            var isNum = new bool[ncol]; var seen = new bool[ncol];
            for (int i = 0; i < shown; i++)
            {
                var row = rows[i] ?? new object[0];
                var c = new string[ncol];
                for (int j = 0; j < ncol; j++)
                {
                    object v = j < row.Count ? row[j] : null;
                    c[j] = j < row.Count ? Cell(v) : "";
                    if (v != null && j < row.Count) { if (!seen[j]) { seen[j] = true; isNum[j] = true; } if (!IsNum(v)) isNum[j] = false; }
                    w[j] = Math.Max(w[j], c[j].Length);
                }
                cells.Add(c);
            }
            for (int j = 0; j < ncol; j++)
            {
                right[j] = seen[j] && isNum[j];
                if (columns != null && j < columns.Count) w[j] = Math.Max(w[j], Clip(columns[j] ?? "").Length);
                w[j] = Math.Max(w[j], 1);
            }
            var sb = new StringBuilder();
            var border = new StringBuilder("+");
            for (int j = 0; j < ncol; j++) border.Append('-', w[j] + 2).Append('+');
            string line = border.ToString();
            if (ncol > 0)
            {
                sb.Append(line).Append('\n');
                if (columns != null)
                {
                    sb.Append('|');
                    for (int j = 0; j < ncol; j++) sb.Append(' ').Append(Pad(j < columns.Count ? Clip(columns[j] ?? "") : "", w[j], false)).Append(" |");
                    sb.Append('\n').Append(line).Append('\n');
                }
                foreach (var c in cells)
                {
                    sb.Append('|');
                    for (int j = 0; j < ncol; j++) sb.Append(' ').Append(Pad(c[j], w[j], right[j])).Append(" |");
                    sb.Append('\n');
                }
                if (columns != null || cells.Count > 0) sb.Append(line).Append('\n');
            }
            totalRows = Math.Max(totalRows, rows.Count);
            if (totalRows > shown)
                sb.Append("… и ещё ").Append(totalRows - shown).Append(truncated ? "+" : "").Append(' ')
                  .Append(Plural(totalRows - shown, "строка", "строки", "строк"))
                  .Append(" (всего ").Append(totalRows).Append(truncated ? "+" : "").Append(")\n");
            else sb.Append(totalRows).Append(' ').Append(Plural(totalRows, "строка", "строки", "строк")).Append('\n');
            return sb.ToString().TrimEnd('\n');
        }

        public static string FormatTable(IList<string> columns, List<List<object>> rows, int totalRows, bool truncated, int maxRows)
        {
            var l = new List<IList>(rows != null ? rows.Count : 0);
            if (rows != null) foreach (var r in rows) l.Add(r);
            return FormatTable(columns, l, totalRows, truncated, maxRows);
        }

        static List<IList> ToRows(IList expected)
        {
            var l = new List<IList>();
            foreach (var o in expected) l.Add(o as IList ?? new object[] { o });
            return l;
        }

        static string Pad(string s, int w, bool right) { return right ? s.PadLeft(w) : s.PadRight(w); }

        static string Cell(object v)
        {
            return Clip(v is string ? (string)v : FormatValue(v));
        }

        static string Clip(string s)
        {
            s = s.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", " ");
            return s.Length > MaxCellWidth ? s.Substring(0, MaxCellWidth - 1) + "…" : s;
        }

        // Значение для показа: NULL, 42, 3.5 (как repr в Python: 3.0, 1e+20), текст как есть, BLOB как x'00ff'.
        public static string FormatValue(object v)
        {
            if (v == null) return "NULL";
            if (v is string) return (string)v;
            if (v is bool) return (bool)v ? "true" : "false";
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v);
                if (double.IsNaN(d)) return "nan";
                if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
                string s = d.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");
                if (s.Contains("e") && !s.Contains("e-") && !s.Contains("e+")) s = s.Replace("e", "e+");
                if (s.IndexOf('.') < 0 && s.IndexOf('e') < 0) s += ".0";
                return s;
            }
            if (v is byte[])
            {
                var b = (byte[])v; var sb = new StringBuilder("x'");
                for (int i = 0; i < b.Length && i < 32; i++) sb.Append(b[i].ToString("x2"));
                return sb.Append(b.Length > 32 ? "…'" : "'").ToString();
            }
            if (v is IFormattable) return ((IFormattable)v).ToString(null, CultureInfo.InvariantCulture);
            return v.ToString();
        }

        // Значение как SQL-литерал — для подсказок: 'текст', 42, NULL.
        public static string Literal(object v)
        {
            if (v is string) return "'" + ((string)v).Replace("'", "''") + "'";
            return FormatValue(v);
        }

        static string Rows(int n) { return n + " " + Plural(n, "строка", "строки", "строк"); }

        static string Plural(int n, string one, string few, string many)
        {
            n = Math.Abs(n) % 100;
            if (n >= 11 && n <= 14) return many;
            switch (n % 10) { case 1: return one; case 2: case 3: case 4: return few; default: return many; }
        }

        // ================== Нативный слой ==================
        // Четыре копии одной и той же C API (имя библиотеки в DllImport — только константа): соглашение о вызовах winsqlite3 — stdcall (важно только для x86),
        // у libsqlite3 — cdecl. На x64 разницы нет.
        abstract class Native
        {
            public abstract string Name { get; }
            public abstract int open(byte[] filenameUtf8z, out IntPtr db);
            public abstract int close(IntPtr db);
            public abstract int prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);
            public abstract int step(IntPtr stmt);
            public abstract int finalize(IntPtr stmt);
            public abstract int column_count(IntPtr stmt);
            public abstract int column_type(IntPtr stmt, int i);
            public abstract long column_int64(IntPtr stmt, int i);
            public abstract double column_double(IntPtr stmt, int i);
            public abstract IntPtr column_text(IntPtr stmt, int i);
            public abstract IntPtr column_blob(IntPtr stmt, int i);
            public abstract int column_bytes(IntPtr stmt, int i);
            public abstract IntPtr column_name(IntPtr stmt, int i);
            public abstract IntPtr errmsg(IntPtr db);
            public abstract IntPtr libversion();
            public abstract int limit(IntPtr db, int id, int value);
            public abstract int get_autocommit(IntPtr db);
            public abstract void progress_handler(IntPtr db, int nOps, bool on);
        }

        sealed class WinNative : Native
        {
            const string L = "winsqlite3";
            const CallingConvention C = CallingConvention.StdCall;
            [UnmanagedFunctionPointer(C)] delegate int ProgressFn(IntPtr arg);
            static readonly ProgressFn progress = OnProgress;   // держим ссылку, чтобы GC не собрал делегат
            [MonoPInvokeCallback(typeof(ProgressFn))] static int OnProgress(IntPtr arg) { return ProgressTick(); }

            [DllImport(L, CallingConvention = C)] static extern int sqlite3_open(byte[] filename, out IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_close(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_step(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_type(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern long sqlite3_column_int64(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern double sqlite3_column_double(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_bytes(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_libversion();
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_limit(IntPtr db, int id, int value);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_get_autocommit(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern void sqlite3_progress_handler(IntPtr db, int nOps, ProgressFn cb, IntPtr arg);

            public override string Name { get { return L; } }
            public override int open(byte[] f, out IntPtr db) { return sqlite3_open(f, out db); }
            public override int close(IntPtr db) { return sqlite3_close(db); }
            public override int prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr st, out IntPtr tail) { return sqlite3_prepare_v2(db, sql, n, out st, out tail); }
            public override int step(IntPtr st) { return sqlite3_step(st); }
            public override int finalize(IntPtr st) { return sqlite3_finalize(st); }
            public override int column_count(IntPtr st) { return sqlite3_column_count(st); }
            public override int column_type(IntPtr st, int i) { return sqlite3_column_type(st, i); }
            public override long column_int64(IntPtr st, int i) { return sqlite3_column_int64(st, i); }
            public override double column_double(IntPtr st, int i) { return sqlite3_column_double(st, i); }
            public override IntPtr column_text(IntPtr st, int i) { return sqlite3_column_text(st, i); }
            public override IntPtr column_blob(IntPtr st, int i) { return sqlite3_column_blob(st, i); }
            public override int column_bytes(IntPtr st, int i) { return sqlite3_column_bytes(st, i); }
            public override IntPtr column_name(IntPtr st, int i) { return sqlite3_column_name(st, i); }
            public override IntPtr errmsg(IntPtr db) { return sqlite3_errmsg(db); }
            public override IntPtr libversion() { return sqlite3_libversion(); }
            public override int limit(IntPtr db, int id, int v) { return sqlite3_limit(db, id, v); }
            public override int get_autocommit(IntPtr db) { return sqlite3_get_autocommit(db); }
            public override void progress_handler(IntPtr db, int n, bool on) { sqlite3_progress_handler(db, on ? n : 0, on ? progress : null, IntPtr.Zero); }
        }

        // macOS: полный путь (dyld найдёт библиотеку в shared cache, даже если файла на диске нет). Идёт раньше "sqlite3":
        // в конфиге Mono бывает <dllmap dll="sqlite3" target="libsqlite3.so.0" os="!windows"/>, и тогда "sqlite3" на Mac не найдётся.
        sealed class MacNative : Native
        {
            const string L = "/usr/lib/libsqlite3.dylib";
            const CallingConvention C = CallingConvention.Cdecl;
            [UnmanagedFunctionPointer(C)] delegate int ProgressFn(IntPtr arg);
            static readonly ProgressFn progress = OnProgress;
            [MonoPInvokeCallback(typeof(ProgressFn))] static int OnProgress(IntPtr arg) { return ProgressTick(); }

            [DllImport(L, CallingConvention = C)] static extern int sqlite3_open(byte[] filename, out IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_close(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_step(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_type(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern long sqlite3_column_int64(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern double sqlite3_column_double(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_bytes(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_libversion();
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_limit(IntPtr db, int id, int value);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_get_autocommit(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern void sqlite3_progress_handler(IntPtr db, int nOps, ProgressFn cb, IntPtr arg);

            public override string Name { get { return L; } }
            public override int open(byte[] f, out IntPtr db) { return sqlite3_open(f, out db); }
            public override int close(IntPtr db) { return sqlite3_close(db); }
            public override int prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr st, out IntPtr tail) { return sqlite3_prepare_v2(db, sql, n, out st, out tail); }
            public override int step(IntPtr st) { return sqlite3_step(st); }
            public override int finalize(IntPtr st) { return sqlite3_finalize(st); }
            public override int column_count(IntPtr st) { return sqlite3_column_count(st); }
            public override int column_type(IntPtr st, int i) { return sqlite3_column_type(st, i); }
            public override long column_int64(IntPtr st, int i) { return sqlite3_column_int64(st, i); }
            public override double column_double(IntPtr st, int i) { return sqlite3_column_double(st, i); }
            public override IntPtr column_text(IntPtr st, int i) { return sqlite3_column_text(st, i); }
            public override IntPtr column_blob(IntPtr st, int i) { return sqlite3_column_blob(st, i); }
            public override int column_bytes(IntPtr st, int i) { return sqlite3_column_bytes(st, i); }
            public override IntPtr column_name(IntPtr st, int i) { return sqlite3_column_name(st, i); }
            public override IntPtr errmsg(IntPtr db) { return sqlite3_errmsg(db); }
            public override IntPtr libversion() { return sqlite3_libversion(); }
            public override int limit(IntPtr db, int id, int v) { return sqlite3_limit(db, id, v); }
            public override int get_autocommit(IntPtr db) { return sqlite3_get_autocommit(db); }
            public override void progress_handler(IntPtr db, int n, bool on) { sqlite3_progress_handler(db, on ? n : 0, on ? progress : null, IntPtr.Zero); }
        }

        sealed class CNative : Native
        {
            const string L = "sqlite3";
            const CallingConvention C = CallingConvention.Cdecl;
            [UnmanagedFunctionPointer(C)] delegate int ProgressFn(IntPtr arg);
            static readonly ProgressFn progress = OnProgress;
            [MonoPInvokeCallback(typeof(ProgressFn))] static int OnProgress(IntPtr arg) { return ProgressTick(); }

            [DllImport(L, CallingConvention = C)] static extern int sqlite3_open(byte[] filename, out IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_close(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_step(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_type(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern long sqlite3_column_int64(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern double sqlite3_column_double(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_bytes(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_libversion();
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_limit(IntPtr db, int id, int value);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_get_autocommit(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern void sqlite3_progress_handler(IntPtr db, int nOps, ProgressFn cb, IntPtr arg);

            public override string Name { get { return L; } }
            public override int open(byte[] f, out IntPtr db) { return sqlite3_open(f, out db); }
            public override int close(IntPtr db) { return sqlite3_close(db); }
            public override int prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr st, out IntPtr tail) { return sqlite3_prepare_v2(db, sql, n, out st, out tail); }
            public override int step(IntPtr st) { return sqlite3_step(st); }
            public override int finalize(IntPtr st) { return sqlite3_finalize(st); }
            public override int column_count(IntPtr st) { return sqlite3_column_count(st); }
            public override int column_type(IntPtr st, int i) { return sqlite3_column_type(st, i); }
            public override long column_int64(IntPtr st, int i) { return sqlite3_column_int64(st, i); }
            public override double column_double(IntPtr st, int i) { return sqlite3_column_double(st, i); }
            public override IntPtr column_text(IntPtr st, int i) { return sqlite3_column_text(st, i); }
            public override IntPtr column_blob(IntPtr st, int i) { return sqlite3_column_blob(st, i); }
            public override int column_bytes(IntPtr st, int i) { return sqlite3_column_bytes(st, i); }
            public override IntPtr column_name(IntPtr st, int i) { return sqlite3_column_name(st, i); }
            public override IntPtr errmsg(IntPtr db) { return sqlite3_errmsg(db); }
            public override IntPtr libversion() { return sqlite3_libversion(); }
            public override int limit(IntPtr db, int id, int v) { return sqlite3_limit(db, id, v); }
            public override int get_autocommit(IntPtr db) { return sqlite3_get_autocommit(db); }
            public override void progress_handler(IntPtr db, int n, bool on) { sqlite3_progress_handler(db, on ? n : 0, on ? progress : null, IntPtr.Zero); }
        }

        // Linux без пакета libsqlite3-dev: есть только libsqlite3.so.0 (DllImport("sqlite3") ищет libsqlite3.so).
        sealed class SoNative : Native
        {
            const string L = "libsqlite3.so.0";
            const CallingConvention C = CallingConvention.Cdecl;
            [UnmanagedFunctionPointer(C)] delegate int ProgressFn(IntPtr arg);
            static readonly ProgressFn progress = OnProgress;
            [MonoPInvokeCallback(typeof(ProgressFn))] static int OnProgress(IntPtr arg) { return ProgressTick(); }

            [DllImport(L, CallingConvention = C)] static extern int sqlite3_open(byte[] filename, out IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_close(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_prepare_v2(IntPtr db, IntPtr sql, int nByte, out IntPtr stmt, out IntPtr tail);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_step(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_finalize(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_count(IntPtr stmt);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_type(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern long sqlite3_column_int64(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern double sqlite3_column_double(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_column_bytes(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_column_name(IntPtr stmt, int i);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_errmsg(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern IntPtr sqlite3_libversion();
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_limit(IntPtr db, int id, int value);
            [DllImport(L, CallingConvention = C)] static extern int sqlite3_get_autocommit(IntPtr db);
            [DllImport(L, CallingConvention = C)] static extern void sqlite3_progress_handler(IntPtr db, int nOps, ProgressFn cb, IntPtr arg);

            public override string Name { get { return L; } }
            public override int open(byte[] f, out IntPtr db) { return sqlite3_open(f, out db); }
            public override int close(IntPtr db) { return sqlite3_close(db); }
            public override int prepare_v2(IntPtr db, IntPtr sql, int n, out IntPtr st, out IntPtr tail) { return sqlite3_prepare_v2(db, sql, n, out st, out tail); }
            public override int step(IntPtr st) { return sqlite3_step(st); }
            public override int finalize(IntPtr st) { return sqlite3_finalize(st); }
            public override int column_count(IntPtr st) { return sqlite3_column_count(st); }
            public override int column_type(IntPtr st, int i) { return sqlite3_column_type(st, i); }
            public override long column_int64(IntPtr st, int i) { return sqlite3_column_int64(st, i); }
            public override double column_double(IntPtr st, int i) { return sqlite3_column_double(st, i); }
            public override IntPtr column_text(IntPtr st, int i) { return sqlite3_column_text(st, i); }
            public override IntPtr column_blob(IntPtr st, int i) { return sqlite3_column_blob(st, i); }
            public override int column_bytes(IntPtr st, int i) { return sqlite3_column_bytes(st, i); }
            public override IntPtr column_name(IntPtr st, int i) { return sqlite3_column_name(st, i); }
            public override IntPtr errmsg(IntPtr db) { return sqlite3_errmsg(db); }
            public override IntPtr libversion() { return sqlite3_libversion(); }
            public override int limit(IntPtr db, int id, int v) { return sqlite3_limit(db, id, v); }
            public override int get_autocommit(IntPtr db) { return sqlite3_get_autocommit(db); }
            public override void progress_handler(IntPtr db, int n, bool on) { sqlite3_progress_handler(db, on ? n : 0, on ? progress : null, IntPtr.Zero); }
        }
    }

#if !UNITY_5_3_OR_NEWER
    // Вне Unity (тесты под mono): заглушка атрибута из UnityEngine (AOT.MonoPInvokeCallbackAttribute, нужен для IL2CPP).
    [AttributeUsage(AttributeTargets.Method)]
    sealed class MonoPInvokeCallbackAttribute : Attribute { public MonoPInvokeCallbackAttribute(Type t) { } }
#endif
}
