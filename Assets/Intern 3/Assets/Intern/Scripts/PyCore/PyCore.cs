// Игровой Python: значения, числа, ошибки, лексер.
// Этот файл НЕ зависит от Unity — его можно тестировать отдельно (см. /home/claude/pytests).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Intern.Py
{
    // ---------- Ошибки ----------
    public class PyError : Exception
    {
        public string PyType;    // "NameError", "TypeError"... (как в трейсбеке CPython)
        public string Ru;        // дружелюбное объяснение на русском
        public int Line;
        public string PyMsg;     // сообщение как в CPython — его вернёт str(e) в except
        public object[] ExcArgs; // e.args, если их нельзя получить из PyMsg
        public object Value;     // объект исключения Python (PyInstance), если уже создан
        public bool Fatal;       // не перехватывается try/except (лимит шагов, внутренние сбои)
        // Последняя созданная ошибка потока: при раскрутке стека Interp.Exec (в finally, без catch/rethrow — это дорого в Mono)
        // проставляет ей номер строки, если он неизвестен (0).
        [ThreadStatic] public static PyError LastCreated;
        public PyError(string type, string ru, int line = 0) : base(type + ": " + ru) { PyType = type; Ru = ru; Line = line; LastCreated = this; }
        public PyError(string type, string ru, string pyMsg, int line) : this(type, ru, line) { PyMsg = pyMsg; }
    }

    public class StopExecution : Exception { }

    // ---------- Значения ----------
    // None -> null, bool -> bool, int -> long (или BigInteger, если не влезает), float -> double, str -> string
    public sealed class PyEllipsis { public static readonly PyEllipsis I = new PyEllipsis(); }
    public sealed class PyNotImplemented { public static readonly PyNotImplemented I = new PyNotImplemented(); }

    public class PyList
    {
        public List<object> Items;
        public PyList() { Items = new List<object>(); }
        public PyList(IEnumerable<object> e) { Items = new List<object>(e); }
        public static PyList Wrap(List<object> l) { var r = new PyList(0); r.Items = l; return r; }
        PyList(int dummy) { }
    }
    public class PyTuple
    {
        public object[] Items;
        public PyTuple(object[] i) { Items = i; }
        public static readonly PyTuple Empty = new PyTuple(new object[0]);
    }
    public class PyRange
    {
        public long Start, Stop, Step = 1;
        public long Length
        {
            get
            {
                if (Step > 0) return Stop > Start ? (Stop - Start - 1) / Step + 1 : 0;
                return Start > Stop ? (Start - Stop - 1) / (-Step) + 1 : 0;
            }
        }
        public long At(long i) { return Start + i * Step; }
    }

    public enum DictKind { Dict, Counter, DefaultDict, OrderedDict }

    // Словарь с порядком вставки (как в CPython 3.7+)
    public class PyDict
    {
        static readonly object Hole = new object();
        internal static readonly object NoneKey = new object();   // None как ключ (Dictionary в C# не принимает null)
        static object K(object k) { return k ?? NoneKey; }
        readonly List<object> ks = new List<object>();
        readonly List<object> vs = new List<object>();
        readonly Dictionary<object, int> idx = new Dictionary<object, int>(PyKeyComparer.Instance);
        int holes;
        public int Version;             // меняется при изменении размера (для ошибки «changed size during iteration»)
        public DictKind Kind;
        public object Factory;          // default_factory для defaultdict

        public int Count { get { return idx.Count; } }
        public bool TryGet(object k, out object v)
        {
            int i;
            if (idx.Count == 0) PyOps.Hash(k);   // пустой Dictionary не считает хеш — а «unhashable type» должен быть и тут
            if (idx.TryGetValue(K(k), out i)) { v = vs[i]; return true; }
            v = null; return false;
        }
        public bool ContainsKey(object k) { if (idx.Count == 0) PyOps.Hash(k); return idx.ContainsKey(K(k)); }
        public object Get(object k) { object v; TryGet(k, out v); return v; }
        public void Set(object k, object v)
        {
            int i;
            if (idx.TryGetValue(K(k), out i)) { vs[i] = v; return; }
            idx[K(k)] = ks.Count; ks.Add(k); vs.Add(v); Version++;
        }
        public bool Remove(object k)
        {
            int i;
            if (idx.Count == 0) PyOps.Hash(k);
            if (!idx.TryGetValue(K(k), out i)) return false;
            idx.Remove(K(k)); ks[i] = Hole; vs[i] = null; holes++; Version++;
            if (holes > 16 && holes > idx.Count) Compact();
            return true;
        }
        public void Clear() { ks.Clear(); vs.Clear(); idx.Clear(); holes = 0; Version++; }
        void Compact()
        {
            var nk = new List<object>(); var nv = new List<object>();
            for (int i = 0; i < ks.Count; i++) if (ks[i] != Hole) { nk.Add(ks[i]); nv.Add(vs[i]); }
            ks.Clear(); vs.Clear(); idx.Clear(); holes = 0;
            for (int i = 0; i < nk.Count; i++) { idx[K(nk[i])] = i; ks.Add(nk[i]); vs.Add(nv[i]); }
        }
        // Позиционный обход (для итераторов): возвращает false, когда позиции кончились
        public int RawCount { get { return ks.Count; } }
        public bool RawAt(int i, out object k, out object v)
        {
            k = ks[i]; v = vs[i];
            return k != Hole;
        }
        public List<object> KeyList() { var r = new List<object>(idx.Count); for (int i = 0; i < ks.Count; i++) if (ks[i] != Hole) r.Add(ks[i]); return r; }
        public List<object> ValueList() { var r = new List<object>(idx.Count); for (int i = 0; i < ks.Count; i++) if (ks[i] != Hole) r.Add(vs[i]); return r; }
        public IEnumerable<KeyValuePair<object, object>> Pairs()
        {
            for (int i = 0; i < ks.Count; i++) if (ks[i] != Hole) yield return new KeyValuePair<object, object>(ks[i], vs[i]);
        }
        public KeyValuePair<object, object> PopLast()
        {
            for (int i = ks.Count - 1; i >= 0; i--)
                if (ks[i] != Hole) { var p = new KeyValuePair<object, object>(ks[i], vs[i]); Remove(ks[i]); return p; }
            throw new InvalidOperationException();
        }
        public PyDict CopyAs(DictKind kind)
        {
            var d = new PyDict { Kind = kind, Factory = Factory };
            foreach (var p in Pairs()) d.Set(p.Key, p.Value);
            return d;
        }
    }

    public class PyKeyComparer : IEqualityComparer<object>
    {
        public static readonly PyKeyComparer Instance = new PyKeyComparer();
        public new bool Equals(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == PyDict.NoneKey || b == PyDict.NoneKey) return false;
            return PyOps.Eq(a, b);
        }
        public int GetHashCode(object o) { long h = PyOps.Hash(o == PyDict.NoneKey ? null : o); return (int)(h ^ (h >> 32)); }
    }

    // Множество: повторяет хеш-таблицу CPython (setobject.c), чтобы порядок печати совпадал с CPython
    public class PySet
    {
        const int LinearProbes = 9, PerturbShift = 5, MinSize = 8;
        struct Entry { public object Key; public long Hash; public byte State; } // 0 пусто, 1 занято, 2 удалено
        Entry[] table; int mask, fill, used, finger;
        public bool Frozen;
        public int Version;
        public int Count { get { return used; } }

        public PySet() { table = new Entry[MinSize]; mask = MinSize - 1; }

        public bool Contains(object key) { return Find(key, PyOps.Hash(key)) >= 0; }

        int Find(object key, long hash)
        {
            ulong perturb = (ulong)hash; ulong m = (ulong)mask; ulong i = (ulong)hash & m;
            while (true)
            {
                int probes = (i + LinearProbes <= m) ? LinearProbes : 0;
                ulong j = i;
                while (true)
                {
                    var e = table[j];
                    if (e.State == 0) return -1;
                    if (e.State == 1 && e.Hash == hash && (ReferenceEquals(e.Key, key) || PyOps.Eq(e.Key, key))) return (int)j;
                    if (probes-- == 0) break;
                    j++;
                }
                perturb >>= PerturbShift;
                i = (i * 5 + 1 + perturb) & m;
            }
        }

        public void Add(object key) { AddEntry(key, PyOps.Hash(key)); }

        void AddEntry(object key, long hash)
        {
            ulong perturb = (ulong)hash; ulong m = (ulong)mask; ulong i = (ulong)hash & m;
            long freeslot = -1;
            while (true)
            {
                int probes = (i + LinearProbes <= m) ? LinearProbes : 0;
                ulong j = i;
                while (true)
                {
                    var e = table[j];
                    if (e.State == 0)
                    {
                        if (freeslot >= 0)
                        {
                            used++; Version++;
                            table[freeslot] = new Entry { Key = key, Hash = hash, State = 1 };
                            return;
                        }
                        fill++; used++; Version++;
                        table[j] = new Entry { Key = key, Hash = hash, State = 1 };
                        if ((long)fill * 5 >= (long)mask * 3) Resize(used > 50000 ? used * 2 : used * 4);
                        return;
                    }
                    if (e.State == 1 && e.Hash == hash && (ReferenceEquals(e.Key, key) || PyOps.Eq(e.Key, key))) return;
                    if (e.State == 2) freeslot = (long)j;   // CPython 3.11 берёт последний встреченный «удалённый» слот (проверено по внутренностям set)
                    if (probes-- == 0) break;
                    j++;
                }
                perturb >>= PerturbShift;
                i = (i * 5 + 1 + perturb) & m;
            }
        }

        static void InsertClean(Entry[] t, int mask, object key, long hash)
        {
            ulong perturb = (ulong)hash; ulong m = (ulong)mask; ulong i = (ulong)hash & m;
            while (true)
            {
                if (t[i].State == 0) { t[i] = new Entry { Key = key, Hash = hash, State = 1 }; return; }
                if (i + LinearProbes <= m)
                    for (ulong j = 1; j <= LinearProbes; j++)
                        if (t[i + j].State == 0) { t[i + j] = new Entry { Key = key, Hash = hash, State = 1 }; return; }
                perturb >>= PerturbShift;
                i = (i * 5 + 1 + perturb) & m;
            }
        }

        void Resize(int minused)
        {
            int newsize = MinSize;
            while (newsize <= minused) newsize <<= 1;
            var old = table;
            table = new Entry[newsize]; mask = newsize - 1;
            fill = used;
            foreach (var e in old) if (e.State == 1) InsertClean(table, mask, e.Key, e.Hash);
        }

        public bool Discard(object key)
        {
            int j = Find(key, PyOps.Hash(key));
            if (j < 0) return false;
            table[j].State = 2; table[j].Key = null; table[j].Hash = -1; used--; Version++;
            return true;
        }

        public object Pop()
        {
            int j = finger & mask;
            while (table[j].State != 1) { j++; if (j > mask) j = 0; }
            var k = table[j].Key;
            table[j].State = 2; table[j].Key = null; table[j].Hash = -1; used--; Version++;
            finger = j + 1;
            return k;
        }

        public void Clear() { table = new Entry[MinSize]; mask = MinSize - 1; fill = used = 0; Version++; }

        // Слить другое множество (set_merge)
        public void Merge(PySet other)
        {
            if (other == this || other.used == 0) return;
            if ((long)(fill + other.used) * 5 >= (long)mask * 3) Resize((used + other.used) * 2);
            if (fill == 0 && mask == other.mask && other.fill == other.used)
            {
                for (int i = 0; i <= other.mask; i++) table[i] = other.table[i];
                fill = other.fill; used = other.used; Version++;
                return;
            }
            if (fill == 0)
            {
                fill = other.used; used = other.used; Version++;
                foreach (var e in other.table) if (e.State == 1) InsertClean(table, mask, e.Key, e.Hash);
                return;
            }
            foreach (var e in other.table) if (e.State == 1) AddEntry(e.Key, e.Hash);
        }

        public void UpdateFromDict(PyDict d)
        {
            if ((long)(fill + d.Count) * 5 >= (long)mask * 3) Resize((used + d.Count) * 2);
            foreach (var p in d.Pairs()) Add(p.Key);
        }

        // после массового удаления (difference_update, -=): если «удалённых» слотов больше четверти — перестроить таблицу, как CPython
        public void ResizeAfterDiscards()
        {
            if ((fill - used) <= mask / 4) return;
            Resize(used > 50000 ? used * 2 : used * 4);
        }

        // забрать таблицу другого множества целиком (set_swap_bodies в intersection_update / &=)
        public void TakeBody(PySet other)
        {
            table = other.table; mask = other.mask; fill = other.fill; used = other.used; Version++;
            other.table = new Entry[MinSize]; other.mask = MinSize - 1; other.fill = other.used = 0;
        }

        public int RawCount { get { return table.Length; } }
        public bool RawAt(int i, out object key) { key = table[i].Key; return table[i].State == 1; }
        public List<object> ToList()
        {
            var r = new List<object>(used);
            foreach (var e in table) if (e.State == 1) r.Add(e.Key);
            return r;
        }
        public PySet Copy() { var s = new PySet { Frozen = Frozen }; s.Merge(this); return s; }
    }

    public class PyDictView { public PyDict D; public int Kind; } // 0 keys, 1 values, 2 items

    // Одноразовый итератор (map, zip, enumerate, генераторное выражение, iter(...))
    public class PyIterator
    {
        public IEnumerator<object> E; public string Kind;
        public PyIterator(IEnumerable<object> e, string kind) { E = e.GetEnumerator(); Kind = kind; }
    }

    public delegate object BuiltinFn(List<object> args, Dictionary<string, object> kw, int line);

    public class PyBuiltin
    {
        public string Name; public BuiltinFn Fn;
        public PyBuiltin(string n, BuiltinFn f) { Name = n; Fn = f; }
    }

    public class PyFunction
    {
        public string Name;
        public List<Param> Params = new List<Param>();
        public object[] Defaults;                  // по параметрам: значение по умолчанию или NoDefault
        public List<Stmt> Body; public Expr LambdaBody;
        public Frame Closure;                      // область, где функцию объявили (для замыканий)
        public ScopeInfo Scope;
        public PyClass DefiningClass;              // для super() без аргументов
        public string Qual;                        // __qualname__: f.<locals>.g, C.method
        public int Line;
        public PyDict Attrs;                       // func.attr = ...
        public static readonly object NoDefault = new object();
    }

    // метод встроенного типа, привязанный к значению: "abc".upper
    public class PyBoundMethod { public object Self; public string Name; }
    // метод пользовательского класса, привязанный к объекту (или классу — для classmethod)
    public class PyMethod { public object Self; public object Func; }
    public class PyProperty { public object Fget, Fset, Fdel; }
    public class PyStaticMethod { public object Func; }
    public class PyClassMethod { public object Func; }
    public class PySuper { public PyClass ThisClass; public object Obj; public PyClass ObjType; }
    public class PyModule { public string Name; public Dictionary<string, object> Dict = new Dictionary<string, object>(); }

    public class PyClass
    {
        public string Name, Module = "__main__";
        public List<PyClass> Bases = new List<PyClass>();
        public List<PyClass> Mro = new List<PyClass>();
        public Dictionary<string, object> Dict = new Dictionary<string, object>();
        public List<string> Order = new List<string>();
        public bool Builtin;                       // int, str, Exception...
        public BuiltinFn Ctor;                     // конструктор встроенного типа
        public bool IsException;
        public void Set(string k, object v) { if (!Dict.ContainsKey(k)) Order.Add(k); Dict[k] = v; }
        public object Find(string name)
        {
            object v;
            foreach (var c in Mro) if (c.Dict.TryGetValue(name, out v)) return v;
            return null;
        }
        public bool Has(string name) { foreach (var c in Mro) if (c.Dict.ContainsKey(name)) return true; return false; }
        public bool IsSubOf(PyClass other) { return Mro.Contains(other); }
    }

    public class PyInstance
    {
        public PyClass Cls;
        public Dictionary<string, object> Attrs = new Dictionary<string, object>();
        public List<string> Order = new List<string>();
        public readonly long Id;
        static long nextId = 1;
        public PyInstance(PyClass c) { Cls = c; Id = System.Threading.Interlocked.Increment(ref nextId); }
        public void Set(string k, object v) { if (!Attrs.ContainsKey(k)) Order.Add(k); Attrs[k] = v; }
        public bool Del(string k) { if (!Attrs.Remove(k)) return false; Order.Remove(k); return true; }
    }

    // ---------- Глубина рекурсии во встроенных операциях ----------
    // repr/==/hash/json глубоко вложенных данных рекурсивны в C#; переполнение стека в Mono не ловится и роняет игру.
    // Как в CPython (Py_EnterRecursiveCall), глубже ~1000 уровней — RecursionError.
    public static class PyDepth
    {
        [ThreadStatic] static int depth;
        public const int Limit = 990;
        public static void Enter(string what)
        {
            if (++depth > Limit)
            {
                depth--;
                throw new PyError("RecursionError", "Слишком глубокая вложенность данных (списки в списках больше чем на " + Limit + " уровней).", "maximum recursion depth exceeded" + what, 0);
            }
        }
        public static void Leave() { depth--; }
    }

    // ---------- Строки по кодовым точкам ----------
    // В C# строка — UTF-16, а в Python длина и индексы считаются в кодовых точках: len("😀") == 1.
    // Для строк без суррогатных пар (почти всегда) всё идёт по быстрому пути; результат проверки кэшируется по ссылке.
    public static class PyStr
    {
        [ThreadStatic] static string cacheS;
        [ThreadStatic] static bool cacheSurr;
        [ThreadStatic] static int[] cacheOffs;   // UTF-16 смещения кодовых точек (+ длина в конце), только для строк с суррогатами

        public static bool HasSurr(string s)
        {
            if (ReferenceEquals(s, cacheS)) return cacheSurr;
            bool r = false;
            for (int i = 0; i < s.Length; i++) if (char.IsSurrogate(s[i])) { r = true; break; }
            if (s.Length > 16) { cacheS = s; cacheSurr = r; cacheOffs = null; }
            return r;
        }
        static int[] Offs(string s)
        {
            if (ReferenceEquals(s, cacheS) && cacheOffs != null) return cacheOffs;
            var l = new List<int>(s.Length + 1);
            for (int i = 0; i < s.Length; i++)
            {
                l.Add(i);
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            }
            l.Add(s.Length);
            var arr = l.ToArray();
            cacheS = s; cacheSurr = true; cacheOffs = arr;
            return arr;
        }
        public static int Len(string s) { return HasSurr(s) ? Offs(s).Length - 1 : s.Length; }
        // i-я кодовая точка (i уже проверен)
        public static string At(string s, int i)
        {
            if (!HasSurr(s)) return s[i].ToString();
            var o = Offs(s);
            return s.Substring(o[i], o[i + 1] - o[i]);
        }
        public static string[] Cps(string s)
        {
            if (!HasSurr(s)) { var r = new string[s.Length]; for (int i = 0; i < s.Length; i++) r[i] = s[i].ToString(); return r; }
            var o = Offs(s); var res = new string[o.Length - 1];
            for (int i = 0; i < res.Length; i++) res[i] = s.Substring(o[i], o[i + 1] - o[i]);
            return res;
        }
        // индекс кодовой точки -> смещение UTF-16 (с обрезкой по границам)
        public static int ToU16(string s, long cp)
        {
            if (!HasSurr(s)) return (int)Math.Max(0, Math.Min(cp, s.Length));
            var o = Offs(s);
            if (cp <= 0) return 0;
            if (cp >= o.Length - 1) return s.Length;
            return o[cp];
        }
        // смещение UTF-16 -> индекс кодовой точки
        public static long ToCp(string s, long u16)
        {
            if (u16 < 0 || !HasSurr(s)) return u16;
            var o = Offs(s);
            int idx = Array.BinarySearch(o, (int)u16);
            return idx >= 0 ? idx : ~idx;
        }
        // сравнение по кодовым точкам (как в CPython); CompareOrdinal ошибается только при суррогатах
        public static int Compare(string a, string b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                char x = a[i], y = b[i];
                if (x == y) continue;
                bool sx = char.IsSurrogate(x), sy = char.IsSurrogate(y);
                if (sx == sy) return x < y ? -1 : 1;
                return sx ? 1 : -1;   // суррогат = кодовая точка ≥ 0x10000 (или 0xD800..) больше любого не-суррогата ≥ 0xE000
            }
            return a.Length.CompareTo(b.Length);
        }
    }

    // ---------- Числа ----------
    public static class PyNum
    {
        static readonly BigInteger LMin = long.MinValue, LMax = long.MaxValue;
        public static object Norm(BigInteger b) { if (b >= LMin && b <= LMax) return (long)b; return b; }
        public static bool IsInt(object o) { return o is long || o is bool || o is BigInteger; }
        public static bool IsNum(object o) { return o is long || o is double || o is bool || o is BigInteger; }
        public static BigInteger Big(object o)
        {
            if (o is long) return (long)o;
            if (o is bool) return ((bool)o) ? BigInteger.One : BigInteger.Zero;
            return (BigInteger)o;
        }
        public static double ToDouble(object o)
        {
            if (o is double) return (double)o;
            if (o is long) return (long)o;
            if (o is bool) return ((bool)o) ? 1 : 0;
            return BigToDouble((BigInteger)o);
        }
        public static int BitLength(BigInteger b)
        {
            if (b.Sign < 0) b = -b;
            if (b.IsZero) return 0;
            var bytes = b.ToByteArray(); int n = bytes.Length - 1;
            while (n > 0 && bytes[n] == 0) n--;
            int bits = n * 8; int top = bytes[n];
            while (top > 0) { bits++; top >>= 1; }
            return bits;
        }
        static double Scale2(double x, int e)
        {
            while (e > 1000) { x *= Math.Pow(2, 1000); e -= 1000; }
            while (e < -1000) { x *= Math.Pow(2, -1000); e += 1000; }
            return x * Math.Pow(2, e);
        }
        public static double BigToDouble(BigInteger b)
        {
            int sign = b.Sign; if (sign < 0) b = -b;
            if (b <= LMax) return sign * (double)(long)b;
            int bits = BitLength(b);
            if (bits > 1024) throw new PyError("OverflowError", "Целое число слишком большое, чтобы превратить его в float.", "int too large to convert to float", 0);
            int shift = bits - 54;
            BigInteger top = b >> shift;
            bool sticky = !(b & ((BigInteger.One << shift) - 1)).IsZero;
            long t = (long)top; bool half = (t & 1) != 0; t >>= 1;
            if (half && (sticky || (t & 1) != 0)) t++;
            double r = Scale2(t, shift + 1);
            if (double.IsInfinity(r)) throw new PyError("OverflowError", "Целое число слишком большое, чтобы превратить его в float.", "int too large to convert to float", 0);
            return sign * r;
        }
        // num/den (оба > 0), с правильным округлением до ближайшего double
        public static double RatioToDouble(BigInteger num, BigInteger den)
        {
            if (num.IsZero) return 0.0;
            int nb = BitLength(num), db = BitLength(den);
            int shift = 55 - (nb - db);
            BigInteger n2 = shift >= 0 ? num << shift : num, d2 = shift >= 0 ? den : den << -shift;
            BigInteger rem; BigInteger q = BigInteger.DivRem(n2, d2, out rem);
            bool sticky = !rem.IsZero;
            while (BitLength(q) > 54) { if (!q.IsEven) sticky = true; q >>= 1; shift--; }
            // q: 54 бита; value = q * 2^-shift
            int exp2 = 54 - shift;      // value в [2^(exp2-1), 2^exp2)
            int keep = 53;
            if (exp2 - 1 < -1022) keep = 53 - (-1022 - (exp2 - 1));   // субнормальные
            if (keep < 0) return 0.0;
            int drop = 54 - keep;
            BigInteger mant = q >> drop;
            BigInteger lost = q - (mant << drop);
            BigInteger halfv = BigInteger.One << (drop - 1);
            if (lost > halfv || (lost == halfv && (sticky || !mant.IsEven))) mant += 1;
            else if (lost == halfv && !sticky && mant.IsEven) { }
            return Scale2((double)(long)mant, drop - shift);
        }
        public static bool TryParseFloat(string s, out double d)
        {
            d = 0; s = AsciiDigits(s).Trim();
            if (s.Length == 0) return false;
            int i = 0; bool neg = false;
            if (s[0] == '+' || s[0] == '-') { neg = s[0] == '-'; i = 1; }
            string rest = s.Substring(i).ToLowerInvariant();
            if (rest == "inf" || rest == "infinity") { d = neg ? double.NegativeInfinity : double.PositiveInfinity; return true; }
            if (rest == "nan") { d = double.NaN; return true; }
            var digits = new StringBuilder(); int fracDigits = 0; bool any = false, dot = false; char prev = '\0';
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') { digits.Append(c); any = true; if (dot) fracDigits++; }
                else if (c == '_' && prev >= '0' && prev <= '9' && i + 1 < s.Length && char.IsDigit(s[i + 1])) { }
                else if (c == '.' && !dot) dot = true;
                else break;
                prev = c;
            }
            if (!any) return false;
            long exp = 0;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++; bool eneg = false;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) { eneg = s[i] == '-'; i++; }
                if (i >= s.Length || !char.IsDigit(s[i])) return false;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) { if (s[i] != '_' && exp < 100000) exp = exp * 10 + (s[i] - '0'); i++; }
                if (eneg) exp = -exp;
            }
            if (i != s.Length) return false;
            d = DecimalToDouble(digits.ToString(), exp - fracDigits);
            if (neg) d = -d;
            return true;
        }
        // Цифры любых алфавитов (١٢٣) -> ASCII, как делает CPython в int()/float()
        public static string AsciiDigits(string s)
        {
            bool need = false;
            foreach (char c in s) if (c > 127 && char.IsDigit(c)) { need = true; break; }
            if (!need) return s;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s) sb.Append(c > 127 && char.IsDigit(c) ? (char)('0' + (int)char.GetNumericValue(c)) : c);
            return sb.ToString();
        }
        public static double DecimalToDouble(string digits, long exp10)
        {
            digits = digits.TrimStart('0');
            if (digits.Length == 0) return 0.0;
            if (exp10 + digits.Length > 330) return double.PositiveInfinity;
            if (exp10 + digits.Length < -345) return 0.0;
            var n = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            if (exp10 >= 0)
            {
                var v = n * BigInteger.Pow(10, (int)exp10);
                if (BitLength(v) > 1024) return double.PositiveInfinity;
                try { return BigToDouble(v); } catch (PyError) { return double.PositiveInfinity; }
            }
            return RatioToDouble(n, BigInteger.Pow(10, (int)-exp10));
        }

        public static void Decompose(double v, out BigInteger mant, out int exp2)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            int be = (int)((bits >> 52) & 0x7FF);
            long f = bits & 0xFFFFFFFFFFFFFL;
            if (be == 0) exp2 = -1074; else { f |= 1L << 52; exp2 = be - 1075; }
            mant = f;
        }

        public static BigInteger FloatToBig(double d)
        {
            d = Math.Truncate(d);
            if (Math.Abs(d) < 9e18) return new BigInteger((long)d);
            BigInteger m; int e; Decompose(Math.Abs(d), out m, out e);
            var r = e >= 0 ? m << e : m >> -e;
            return d < 0 ? -r : r;
        }

        // Кратчайшая запись, которая однозначно читается обратно (алгоритм Burger & Dybvig), как repr() в CPython
        public static void Shortest(double v, out string digits, out int k)
        {
            BigInteger fm; int e; Decompose(v, out fm, out e);
            long f = (long)fm;
            bool even = (f & 1) == 0;
            bool edge = f == (1L << 52);
            BigInteger r, s, mp, mm;
            if (e >= 0)
            {
                BigInteger be = BigInteger.One << e;
                if (!edge) { r = fm * be * 2; s = 2; mp = be; mm = be; }
                else { r = fm * be * 4; s = 4; mp = be * 2; mm = be; }
            }
            else
            {
                if (e == -1074 || !edge) { r = fm * 2; s = BigInteger.One << (1 - e); mp = 1; mm = 1; }
                else { r = fm * 4; s = BigInteger.One << (2 - e); mp = 2; mm = 1; }
            }
            int est = (int)Math.Ceiling(Math.Log10(v) - 1e-10);
            if (est >= 0) s *= BigInteger.Pow(10, est);
            else { var sc = BigInteger.Pow(10, -est); r *= sc; mp *= sc; mm *= sc; }
            bool tooLow = even ? (r + mp >= s) : (r + mp > s);
            if (tooLow) { k = est + 1; s *= 10; } else k = est;
            var sb = new StringBuilder();
            while (true)
            {
                r *= 10; mp *= 10; mm *= 10;
                BigInteger dd = BigInteger.DivRem(r, s, out r);
                int d = (int)dd;
                bool tc1 = even ? (r <= mm) : (r < mm);
                bool tc2 = even ? (r + mp >= s) : (r + mp > s);
                if (!tc1 && !tc2) { sb.Append((char)('0' + d)); continue; }
                if (tc1 && !tc2) sb.Append((char)('0' + d));
                else if (!tc1) sb.Append((char)('0' + d + 1));
                else sb.Append((char)('0' + (r * 2 < s ? d : d + 1)));
                break;
            }
            digits = sb.ToString();
        }

        public static string FloatRepr(double d)
        {
            if (double.IsPositiveInfinity(d)) return "inf";
            if (double.IsNegativeInfinity(d)) return "-inf";
            if (double.IsNaN(d)) return "nan";
            bool neg = d < 0 || (d == 0 && BitConverter.DoubleToInt64Bits(d) < 0);
            if (d == 0) return neg ? "-0.0" : "0.0";
            string digits; int k; Shortest(Math.Abs(d), out digits, out k);
            string body;
            int n = digits.Length;
            if (k > -4 && k <= 16)
            {
                if (k <= 0) body = "0." + new string('0', -k) + digits;
                else if (k >= n) body = digits + new string('0', k - n) + ".0";
                else body = digits.Substring(0, k) + "." + digits.Substring(k);
            }
            else
            {
                int x = k - 1;
                body = digits.Substring(0, 1) + (n > 1 ? "." + digits.Substring(1) : "") + "e" + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
            }
            return neg ? "-" + body : body;
        }

        // |v| * 10^p, округлённое к ближайшему целому (половинки — к чётному), точно
        static BigInteger ScaledRound(double v, int p)
        {
            BigInteger m; int e; Decompose(Math.Abs(v), out m, out e);
            BigInteger num = m, den = BigInteger.One;
            if (e >= 0) num <<= e; else den <<= -e;
            if (p >= 0) num *= BigInteger.Pow(10, p); else den *= BigInteger.Pow(10, -p);
            BigInteger rem; var q = BigInteger.DivRem(num, den, out rem);
            var twice = rem * 2;
            if (twice > den || (twice == den && !q.IsEven)) q += 1;
            return q;
        }
        static bool NegBit(double d) { return BitConverter.DoubleToInt64Bits(d) < 0; }

        // Формат 'f': точное округление, как в CPython
        public static string FormatFixed(double d, int prec)
        {
            if (double.IsNaN(d)) return "nan";
            if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
            var q = ScaledRound(d, prec).ToString(CultureInfo.InvariantCulture);
            if (prec > 0)
            {
                if (q.Length <= prec) q = new string('0', prec - q.Length + 1) + q;
                q = q.Substring(0, q.Length - prec) + "." + q.Substring(q.Length - prec);
            }
            return (NegBit(d) ? "-" : "") + q;
        }
        // Цифры (prec+1 значащих) и десятичный порядок для формата 'e'
        public static void ExpDigits(double d, int prec, out string digits, out int exp)
        {
            double a = Math.Abs(d);
            if (a == 0) { digits = new string('0', prec + 1); exp = 0; return; }
            int x = (int)Math.Floor(Math.Log10(a));
            var lo = BigInteger.Pow(10, prec); var hi = lo * 10;
            for (int guard = 0; guard < 4; guard++)
            {
                var q = ScaledRound(a, prec - x);
                if (q >= hi) { if (q == hi && guard > 0) { digits = "1" + new string('0', prec); exp = x + 1; return; } x++; continue; }
                if (q < lo) { x--; continue; }
                digits = q.ToString(CultureInfo.InvariantCulture); exp = x; return;
            }
            var qq = ScaledRound(a, prec - x); digits = qq.ToString(CultureInfo.InvariantCulture);
            if (digits.Length > prec + 1) { digits = digits.Substring(0, prec + 1); exp = x + 1; } else exp = x;
        }
        public static string FormatExp(double d, int prec, bool upper, bool alt = false)
        {
            if (double.IsNaN(d)) return upper ? "NAN" : "nan";
            if (double.IsInfinity(d)) return (d > 0 ? "" : "-") + (upper ? "INF" : "inf");
            string digits; int x; ExpDigits(d, prec, out digits, out x);
            string mant = digits.Substring(0, 1) + (prec > 0 || alt ? "." + digits.Substring(1) : "");
            return (NegBit(d) ? "-" : "") + mant + (upper ? "E" : "e") + (x < 0 ? "-" : "+") + Math.Abs(x).ToString("00", CultureInfo.InvariantCulture);
        }
        // Формат 'g' (и формат без типа с точностью, если noType)
        public static string FormatGeneral(double d, int prec, bool upper, bool alt, bool noType)
        {
            if (double.IsNaN(d)) return upper ? "NAN" : "nan";
            if (double.IsInfinity(d)) return (d > 0 ? "" : "-") + (upper ? "INF" : "inf");
            if (prec == 0) prec = 1;
            int x;
            if (d == 0) x = 0; else { string dg; ExpDigits(d, prec - 1, out dg, out x); }
            string s;
            if (x >= -4 && x < (noType ? prec - 1 : prec))
            {
                s = FormatFixed(d, Math.Max(0, prec - 1 - x));
                if (!alt && s.Contains(".")) { s = s.TrimEnd('0'); if (s.EndsWith(".")) s = noType ? s + "0" : s.Substring(0, s.Length - 1); }
                else if (alt && !s.Contains(".")) s += ".";
                else if (noType && !s.Contains(".")) s += ".0";
            }
            else
            {
                s = FormatExp(d, prec - 1, upper, alt);
                if (!alt)
                {
                    int ei = s.IndexOf(upper ? 'E' : 'e');
                    string m = s.Substring(0, ei), ex = s.Substring(ei);
                    if (m.Contains(".")) { m = m.TrimEnd('0'); if (m.EndsWith(".")) m = m.Substring(0, m.Length - 1); }
                    s = m + ex;
                }
            }
            return s;
        }
        // round(x) для float: половинки — к чётному (Math.Round в Mono ошибается на 0.49999999999999994)
        public static double RoundHalfEven(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) >= 4503599627370496.0) return d;
            double f = Math.Floor(d), diff = d - f;
            if (diff > 0.5) return f + 1;
            if (diff < 0.5) return f;
            return Math.IEEERemainder(f, 2) == 0 ? f : f + 1;
        }
        // round(x, n) для float — через точное десятичное округление
        public static double RoundFloat(double d, long n)
        {
            if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) return d;
            if (n > 330) return d;
            if (n < -330) return NegBit(d) ? -0.0 : 0.0;
            var q = ScaledRound(d, (int)n);
            double r = n >= 0 ? RatioToDouble(q, BigInteger.Pow(10, (int)n)) : BigToDoubleSafe(q * BigInteger.Pow(10, (int)-n));
            return NegBit(d) ? -r : r;
        }
        static double BigToDoubleSafe(BigInteger b) { try { return BigToDouble(b); } catch (PyError) { return double.PositiveInfinity; } }

        // ---------- целочисленная арифметика с переходом на BigInteger ----------
        public static object Add(object a, object b)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b, r = unchecked(x + y);
                if (((x ^ r) & (y ^ r)) < 0) return Norm((BigInteger)x + y);
                return r;
            }
            return Norm(Big(a) + Big(b));
        }
        public static object Sub(object a, object b)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b, r = unchecked(x - y);
                if (((x ^ y) & (x ^ r)) < 0) return Norm((BigInteger)x - y);
                return r;
            }
            return Norm(Big(a) - Big(b));
        }
        public static object Mul(object a, object b)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b;
                if (x > -2000000000L && x < 2000000000L && y > -2000000000L && y < 2000000000L) return x * y;
                return Norm((BigInteger)x * y);
            }
            return Norm(Big(a) * Big(b));
        }
        public static object FloorDiv(object a, object b)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b;
                if (!(x == long.MinValue && y == -1))
                {
                    long q = x / y; if ((x % y != 0) && ((x < 0) != (y < 0))) q--; return q;
                }
            }
            BigInteger bx = Big(a), by = Big(b), rem;
            var qq = BigInteger.DivRem(bx, by, out rem);
            if (!rem.IsZero && ((rem.Sign < 0) != (by.Sign < 0))) qq -= 1;
            return Norm(qq);
        }
        public static object Mod(object a, object b)
        {
            if (a is long && b is long)
            {
                long x = (long)a, y = (long)b;
                if (y == -1) return 0L;
                long r = x % y; if (r != 0 && ((r < 0) != (y < 0))) r += y; return r;
            }
            BigInteger bx = Big(a), by = Big(b);
            var rr = BigInteger.Remainder(bx, by);
            if (!rr.IsZero && ((rr.Sign < 0) != (by.Sign < 0))) rr += by;
            return Norm(rr);
        }
        public static int CompareInts(object a, object b)
        {
            if (a is long && b is long) return ((long)a).CompareTo((long)b);
            return Big(a).CompareTo(Big(b));
        }
        public static int CompareNum(object a, object b)
        {
            if (IsInt(a) && IsInt(b)) return CompareInts(a, b);
            if (a is double && b is double) return ((double)a).CompareTo((double)b);
            // int против float: точно
            double d; object i; int flip = 1;
            if (a is double) { d = (double)a; i = b; } else { d = (double)b; i = a; flip = -1; }
            int c;
            if (double.IsNaN(d)) return 0;
            if (double.IsPositiveInfinity(d)) c = 1;
            else if (double.IsNegativeInfinity(d)) c = -1;
            else
            {
                if (i is long && Math.Abs((long)i) < (1L << 53)) c = d.CompareTo((double)(long)i);
                else if (i is bool) c = d.CompareTo(((bool)i) ? 1.0 : 0.0);
                else
                {
                    var fl = FloatToBig(Math.Floor(d)); var bi = Big(i);
                    c = fl.CompareTo(bi);
                    if (c == 0 && d != Math.Floor(d)) c = 1;
                }
            }
            return c * flip;
        }
        public static bool NumEq(object a, object b)
        {
            if (a is long && b is long) return (long)a == (long)b;
            if (a is double && b is double) return (double)a == (double)b;
            if ((a is double && double.IsNaN((double)a)) || (b is double && double.IsNaN((double)b))) return false;
            return CompareNum(a, b) == 0;
        }
    }

    // ---------- Операции над значениями ----------
    public static partial class PyOps
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string TypeName(object o)
        {
            if (o == null) return "NoneType";
            if (o is bool) return "bool";
            if (o is long || o is BigInteger) return "int";
            if (o is double) return "float";
            if (o is string) return "str";
            if (o is PyList) return "list";
            if (o is PyTuple) return "tuple";
            if (o is PyDict)
            {
                switch (((PyDict)o).Kind) { case DictKind.Counter: return "Counter"; case DictKind.DefaultDict: return "defaultdict"; case DictKind.OrderedDict: return "OrderedDict"; }
                return "dict";
            }
            if (o is PySet) return ((PySet)o).Frozen ? "frozenset" : "set";
            if (o is PyRange) return "range";
            if (o is PyInstance) return ((PyInstance)o).Cls.Name;
            if (o is PyFunction) return "function";
            if (o is PyBuiltin || o is PyBoundMethod) return "builtin_function_or_method";
            if (o is PyMethod) return "method";
            if (o is PyClass) return "type";
            if (o is PyModule) return "module";
            if (o is PyIterator) return ((PyIterator)o).Kind;
            if (o is PyDictView) { var k = ((PyDictView)o).Kind; return k == 0 ? "dict_keys" : k == 1 ? "dict_values" : "dict_items"; }
            if (o is PyProperty) return "property";
            if (o is PyStaticMethod) return "staticmethod";
            if (o is PyClassMethod) return "classmethod";
            if (o is PySuper) return "super";
            if (o is PyEllipsis) return "ellipsis";
            if (o is PyNotImplemented) return "NotImplementedType";
            return "object";
        }

        public static string TypeNameRu(object o)
        {
            switch (TypeName(o))
            {
                case "int": return "целое число (int)";
                case "float": return "дробное число (float)";
                case "str": return "строка (str)";
                case "bool": return "логическое значение (bool)";
                case "list": return "список (list)";
                case "dict": return "словарь (dict)";
                case "tuple": return "кортеж (tuple)";
                case "set": return "множество (set)";
                case "NoneType": return "None (пустое значение)";
                case "function": case "builtin_function_or_method": case "method": return "функция";
                case "type": return "класс";
                case "module": return "модуль";
            }
            if (o is PyInstance) return "объект класса " + ((PyInstance)o).Cls.Name;
            return TypeName(o);
        }

        // родительный падеж — для фраз «У списка (list) нет ...»
        public static string TypeNameRuGen(object o)
        {
            switch (TypeName(o))
            {
                case "int": return "целого числа (int)";
                case "float": return "дробного числа (float)";
                case "str": return "строки (str)";
                case "bool": return "логического значения (bool)";
                case "list": return "списка (list)";
                case "dict": return "словаря (dict)";
                case "tuple": return "кортежа (tuple)";
                case "set": return "множества (set)";
                case "NoneType": return "None (пустого значения)";
                case "function": case "builtin_function_or_method": case "method": return "функции";
                case "type": return "класса";
                case "module": return "модуля";
            }
            if (o is PyInstance) return "объекта класса " + ((PyInstance)o).Cls.Name;
            return "значения типа " + TypeName(o);
        }

        public static bool IsNum(object o) { return PyNum.IsNum(o); }
        public static bool IsInt(object o) { return PyNum.IsInt(o); }
        public static long ToLong(object o)
        {
            if (o is long) return (long)o;
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is BigInteger)
            {
                var b = (BigInteger)o;
                if (b > long.MaxValue) return long.MaxValue;
                if (b < long.MinValue) return long.MinValue;
                return (long)b;
            }
            if (o is double)
                throw new PyError("TypeError", "Здесь нужно целое число, а не дробное.", "'float' object cannot be interpreted as an integer", 0);
            throw new PyError("TypeError", "Здесь нужно целое число, а тут " + TypeNameRu(o) + ".", "'" + TypeName(o) + "' object cannot be interpreted as an integer", 0);
        }
        public static double ToDouble(object o) { return PyNum.ToDouble(o); }
        public static long RangeLen(PyRange r) { return r.Length; }
        public static string FloatStr(double d) { return PyNum.FloatRepr(d); }

        public static bool Truthy(object o)
        {
            if (o == null) return false;
            if (o is bool) return (bool)o;
            if (o is long) return (long)o != 0;
            if (o is double) return (double)o != 0.0;
            if (o is string) return ((string)o).Length > 0;
            if (o is PyList) return ((PyList)o).Items.Count > 0;
            if (o is PyDict) return ((PyDict)o).Count > 0;
            if (o is PyTuple) return ((PyTuple)o).Items.Length > 0;
            if (o is PySet) return ((PySet)o).Count > 0;
            if (o is BigInteger) return !((BigInteger)o).IsZero;
            if (o is PyRange) return ((PyRange)o).Length > 0;
            if (o is PyDictView) return ((PyDictView)o).D.Count > 0;
            if (o is PyInstance && Interp.Current != null) return Interp.Current.InstanceTruthy((PyInstance)o);
            return true;
        }

        public static string IntStr(BigInteger b)
        {
            var s = b.ToString(Inv);
            if (s.Length - (b.Sign < 0 ? 1 : 0) > 4300)
                throw new PyError("ValueError", "Число слишком длинное для печати (больше 4300 цифр).",
                    "Exceeds the limit (4300 digits) for integer string conversion; use sys.set_int_max_str_digits() to increase the limit", 0);
            return s;
        }

        public static string Str(object o)
        {
            if (o is string) return (string)o;
            if (o is PyInstance && Interp.Current != null) return Interp.Current.InstanceStr((PyInstance)o);
            return Repr(o);
        }

        public static string StrRepr(string s)
        {
            char q = s.IndexOf('\'') >= 0 && s.IndexOf('"') < 0 ? '"' : '\'';
            var sb = new StringBuilder(s.Length + 2);
            sb.Append(q);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == q || c == '\\') { sb.Append('\\').Append(c); continue; }
                if (c == '\n') { sb.Append("\\n"); continue; }
                if (c == '\r') { sb.Append("\\r"); continue; }
                if (c == '\t') { sb.Append("\\t"); continue; }
                if (c < 0x20 || c == 0x7f) { sb.Append("\\x").Append(((int)c).ToString("x2")); continue; }
                if (c < 0x7f) { sb.Append(c); continue; }
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[i + 1]); i++; continue; }
                var cat = char.GetUnicodeCategory(c);
                bool printable = !(cat == UnicodeCategory.Control || cat == UnicodeCategory.Format || cat == UnicodeCategory.Surrogate ||
                                   cat == UnicodeCategory.PrivateUse || cat == UnicodeCategory.OtherNotAssigned || cat == UnicodeCategory.LineSeparator ||
                                   cat == UnicodeCategory.ParagraphSeparator || cat == UnicodeCategory.SpaceSeparator);
                if (printable) sb.Append(c);
                else if (c <= 0xff) sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append("\\u").Append(((int)c).ToString("x4"));
            }
            return sb.Append(q).ToString();
        }

        [ThreadStatic] static HashSet<object> reprBusy;

        public static string Repr(object o)
        {
            if (o == null) return "None";
            if (o is bool) return ((bool)o) ? "True" : "False";
            if (o is long) return ((long)o).ToString(Inv);
            if (o is double) return PyNum.FloatRepr((double)o);
            if (o is string) return StrRepr((string)o);
            if (o is BigInteger) return IntStr((BigInteger)o);
            if (o is PyList || o is PyTuple || o is PyDict || o is PySet || o is PyDictView || o is PyInstance)
            {
                if (reprBusy == null) reprBusy = new HashSet<object>(ReferenceEqualityComparer.Instance);
                if (reprBusy.Contains(o)) return o is PyList ? "[...]" : o is PyDict ? "{...}" : o is PyTuple ? "(...)" : "...";
                PyDepth.Enter(" while getting the repr of an object");
                reprBusy.Add(o);
                try { return ReprContainer(o); }
                finally { reprBusy.Remove(o); PyDepth.Leave(); }
            }
            if (o is PyRange)
            {
                var r = (PyRange)o;
                return r.Step == 1 ? "range(" + r.Start + ", " + r.Stop + ")" : "range(" + r.Start + ", " + r.Stop + ", " + r.Step + ")";
            }
            if (o is PyFunction) { var f = (PyFunction)o; return "<function " + (f.Qual ?? ((f.DefiningClass != null ? f.DefiningClass.Name + "." : "") + f.Name)) + " at " + FakeAddr(f) + ">"; }
            if (o is PyBuiltin) return "<built-in function " + ((PyBuiltin)o).Name + ">";
            if (o is PyBoundMethod) { var b = (PyBoundMethod)o; return "<built-in method " + b.Name + " of " + TypeName(b.Self) + " object at " + FakeAddr(b.Self) + ">"; }
            if (o is PyMethod) { var m = (PyMethod)o; var fn = m.Func as PyFunction; return "<bound method " + (fn != null ? fn.Qual ?? ((fn.DefiningClass != null ? fn.DefiningClass.Name + "." : "") + fn.Name) : "?") + " of " + Repr(m.Self) + ">"; }
            if (o is PyClass) { var c = (PyClass)o; return "<class '" + (c.Module == "builtins" ? "" : c.Module + ".") + c.Name + "'>"; }
            if (o is PyModule) return "<module '" + ((PyModule)o).Name + "'>";
            if (o is PyIterator) return "<" + ((PyIterator)o).Kind + " object at " + FakeAddr(o) + ">";
            if (o is PyEllipsis) return "Ellipsis";
            if (o is PyNotImplemented) return "NotImplemented";
            if (o is PyProperty) return "<property object at " + FakeAddr(o) + ">";
            return "<" + TypeName(o) + " object at " + FakeAddr(o) + ">";
        }

        static string ReprContainer(object o)
        {
            if (o is PyList)
            {
                var l = ((PyList)o).Items; var sb = new StringBuilder("[");
                for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append(", "); sb.Append(Repr(l[i])); }
                return sb.Append("]").ToString();
            }
            if (o is PyTuple)
            {
                var t = ((PyTuple)o).Items;
                if (t.Length == 1) return "(" + Repr(t[0]) + ",)";
                var sb = new StringBuilder("(");
                for (int i = 0; i < t.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(Repr(t[i])); }
                return sb.Append(")").ToString();
            }
            if (o is PyDict)
            {
                var d = (PyDict)o;
                IEnumerable<KeyValuePair<object, object>> pairs = d.Pairs();
                if (d.Kind == DictKind.Counter && Interp.Current != null) pairs = Interp.Current.CounterOrder(d);
                var sb = new StringBuilder("{"); bool first = true;
                foreach (var p in pairs) { if (!first) sb.Append(", "); first = false; sb.Append(Repr(p.Key)).Append(": ").Append(Repr(p.Value)); }
                sb.Append("}");
                switch (d.Kind)
                {
                    case DictKind.Counter: return d.Count == 0 ? "Counter()" : "Counter(" + sb + ")";
                    case DictKind.DefaultDict: return "defaultdict(" + Repr(d.Factory) + ", " + sb + ")";
                    case DictKind.OrderedDict:
                        {
                            if (d.Count == 0) return "OrderedDict()";
                            var parts = new List<string>();
                            foreach (var p in d.Pairs()) parts.Add("(" + Repr(p.Key) + ", " + Repr(p.Value) + ")");
                            return "OrderedDict([" + string.Join(", ", parts.ToArray()) + "])";
                        }
                }
                return sb.ToString();
            }
            if (o is PySet)
            {
                var s = (PySet)o;
                if (s.Count == 0) return s.Frozen ? "frozenset()" : "set()";
                var sb = new StringBuilder(s.Frozen ? "frozenset({" : "{"); bool first = true;
                foreach (var x in s.ToList()) { if (!first) sb.Append(", "); first = false; sb.Append(Repr(x)); }
                return sb.Append(s.Frozen ? "})" : "}").ToString();
            }
            if (o is PyDictView)
            {
                var v = (PyDictView)o; var parts = new List<string>();
                foreach (var p in v.D.Pairs()) parts.Add(v.Kind == 0 ? Repr(p.Key) : v.Kind == 1 ? Repr(p.Value) : "(" + Repr(p.Key) + ", " + Repr(p.Value) + ")");
                return TypeName(v) + "([" + string.Join(", ", parts.ToArray()) + "])";
            }
            var inst = (PyInstance)o;
            if (Interp.Current != null) return Interp.Current.InstanceRepr(inst);
            return "<" + inst.Cls.Module + "." + inst.Cls.Name + " object at " + FakeAddr(inst) + ">";
        }

        public static string FakeAddr(object o)
        {
            long id = o is PyInstance ? ((PyInstance)o).Id : (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o) & 0xFFFFFF);
            return "0x7f" + (0x3a2c000000L + id * 0x40).ToString("x10");
        }

        // Безопасное представление для отладчика: не вызывает пользовательский код
        public static string SafeRepr(object o, int depth = 0)
        {
            if (depth > 3) return "…";
            if (o is PyInstance)
            {
                var inst = (PyInstance)o;
                var parts = new List<string>();
                foreach (var k in inst.Order) { if (parts.Count >= 6) { parts.Add("…"); break; } parts.Add(k + "=" + SafeRepr(inst.Attrs[k], depth + 1)); }
                return inst.Cls.Name + "(" + string.Join(", ", parts.ToArray()) + ")";
            }
            if (o is PyList || o is PyTuple || o is PySet)
            {
                var items = o is PyList ? ((PyList)o).Items : o is PyTuple ? new List<object>(((PyTuple)o).Items) : ((PySet)o).ToList();
                var parts = new List<string>();
                foreach (var x in items) { if (parts.Count >= 30) { parts.Add("…"); break; } parts.Add(SafeRepr(x, depth + 1)); }
                if (o is PyTuple) return parts.Count == 1 ? "(" + parts[0] + ",)" : "(" + string.Join(", ", parts.ToArray()) + ")";
                if (o is PySet) return parts.Count == 0 ? "set()" : "{" + string.Join(", ", parts.ToArray()) + "}";
                return "[" + string.Join(", ", parts.ToArray()) + "]";
            }
            if (o is PyDict)
            {
                var parts = new List<string>();
                foreach (var p in ((PyDict)o).Pairs()) { if (parts.Count >= 30) { parts.Add("…"); break; } parts.Add(SafeRepr(p.Key, depth + 1) + ": " + SafeRepr(p.Value, depth + 1)); }
                return "{" + string.Join(", ", parts.ToArray()) + "}";
            }
            if (o is PyMethod || o is PyDictView) return "<" + TypeName(o) + ">";
            try { return Repr(o); } catch (Exception) { return "<" + TypeName(o) + ">"; }
        }

        // ---------- Равенство ----------
        public static bool Eq(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is string) return b is string && (string)a == (string)b;
            if (PyNum.IsNum(a) && PyNum.IsNum(b)) return PyNum.NumEq(a, b);
            if (a is PyInstance || b is PyInstance)
            {
                if (Interp.Current != null) return Interp.Current.InstanceEq(a, b);
                return ReferenceEquals(a, b);
            }
            if (ReferenceEquals(a, b)) return true;
            if ((a is PyList && b is PyList) || (a is PyTuple && b is PyTuple) || (a is PyDict && b is PyDict))
            {
                PyDepth.Enter(" in comparison");
                try { return ContainerEq(a, b); }
                finally { PyDepth.Leave(); }
            }
            if (a is PySet && b is PySet)
            {
                var x = (PySet)a; var y = (PySet)b;
                if (x.Count != y.Count) return false;
                foreach (var k in x.ToList()) if (!y.Contains(k)) return false;
                return true;
            }
            if (a is PyRange && b is PyRange)
            {
                var x = (PyRange)a; var y = (PyRange)b;
                if (x.Length != y.Length) return false;
                if (x.Length == 0) return true;
                if (x.Start != y.Start) return false;
                return x.Length == 1 || x.Step == y.Step;
            }
            if (a is PyDictView && b is PyDictView && ((PyDictView)a).Kind != 1)
            {
                var x = (PyDictView)a; var y = (PyDictView)b;
                return x.Kind == y.Kind && Eq(ViewToSet(x), ViewToSet(y));
            }
            if (a is PyMethod && b is PyMethod) return ReferenceEquals(((PyMethod)a).Self, ((PyMethod)b).Self) && ReferenceEquals(((PyMethod)a).Func, ((PyMethod)b).Func);
            return false;
        }
        static bool ContainerEq(object a, object b)
        {
            if (a is PyList) return SeqEq(((PyList)a).Items, ((PyList)b).Items);
            if (a is PyTuple) return SeqEq(((PyTuple)a).Items, ((PyTuple)b).Items);
            var x = (PyDict)a; var y = (PyDict)b;
            if (x.Count != y.Count) return false;
            foreach (var p in x.Pairs()) { object v; if (!y.TryGet(p.Key, out v) || !(ReferenceEquals(p.Value, v) || Eq(p.Value, v))) return false; }
            return true;
        }
        public static PySet ViewToSet(PyDictView v)
        {
            var s = new PySet();
            foreach (var p in v.D.Pairs()) s.Add(v.Kind == 0 ? p.Key : new PyTuple(new[] { p.Key, p.Value }));
            return s;
        }
        static bool SeqEq(IList<object> x, IList<object> y)
        {
            if (x.Count != y.Count) return false;
            for (int i = 0; i < x.Count; i++) if (!ReferenceEquals(x[i], y[i]) && !Eq(x[i], y[i])) return false;
            return true;
        }

        // ---------- Хеш (как в CPython для чисел и кортежей) ----------
        const ulong HashMod = (1UL << 61) - 1;
        public static long HashInt(BigInteger b)
        {
            var m = BigInteger.Remainder(BigInteger.Abs(b), HashMod);
            long h = (long)(ulong)m;
            if (b.Sign < 0) h = -h;
            return h == -1 ? -2 : h;
        }
        public static long HashLong(long v)
        {
            if (v == long.MinValue) return HashInt(v);
            long a = Math.Abs(v);
            long h = (long)((ulong)a % HashMod);
            if (v < 0) h = -h;
            return h == -1 ? -2 : h;
        }
        public static long HashDouble(double v)
        {
            if (double.IsInfinity(v)) return v > 0 ? 314159 : -314159;
            if (double.IsNaN(v)) return 0;
            if (v == Math.Floor(v) && Math.Abs(v) < 9e18) return HashLong((long)v);
            int e; double m = Frexp(v, out e);
            int sign = 1;
            if (m < 0) { sign = -1; m = -m; }
            ulong x = 0;
            while (m != 0)
            {
                x = ((x << 28) & HashMod) | x >> (61 - 28);
                m *= 268435456.0; e -= 28;
                ulong y = (ulong)m; m -= y; x += y;
                if (x >= HashMod) x -= HashMod;
            }
            e = e >= 0 ? e % 61 : 61 - 1 - ((-1 - e) % 61);
            x = ((x << e) & HashMod) | x >> (61 - e);
            long r = (long)x * sign;
            return r == -1 ? -2 : r;
        }
        static double Frexp(double d, out int e)
        {
            long bits = BitConverter.DoubleToInt64Bits(d);
            int be = (int)((bits >> 52) & 0x7FF);
            if (be == 0)
            {
                // субнормальное: нормализуем
                d *= 18014398509481984.0; // 2^54
                bits = BitConverter.DoubleToInt64Bits(d); be = (int)((bits >> 52) & 0x7FF);
                e = be - 1022 - 54;
            }
            else e = be - 1022;
            long mb = (bits & ~(0x7FFL << 52)) | (1022L << 52);
            return BitConverter.Int64BitsToDouble(mb);
        }
        public static long HashString(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
            long r = (long)(h >> 1);
            return r == -1 ? -2 : r;
        }
        public static long Hash(object o)
        {
            if (o == null) return 0x5f4a3c2b1L;
            if (o is string) return HashString((string)o);
            if (o is long) return HashLong((long)o);
            if (o is bool) return ((bool)o) ? 1 : 0;
            if (o is double) return HashDouble((double)o);
            if (o is BigInteger) return HashInt((BigInteger)o);
            if (o is PyTuple)
            {
                const ulong P1 = 11400714785074694791UL, P2 = 14029467366897019727UL, P5 = 2870177450012600261UL;
                var items = ((PyTuple)o).Items;
                ulong acc = P5;
                foreach (var it in items)
                {
                    ulong lane;
                    if (it is PyTuple) { PyDepth.Enter(""); try { lane = (ulong)Hash(it); } finally { PyDepth.Leave(); } }
                    else lane = (ulong)Hash(it);
                    acc += lane * P2;
                    acc = (acc << 31) | (acc >> 33);
                    acc *= P1;
                }
                acc += (ulong)items.Length ^ (P5 ^ 3527539UL);
                if (acc == ulong.MaxValue) return 1546275796;
                return (long)acc;
            }
            if (o is PySet)
            {
                var s = (PySet)o;
                if (!s.Frozen) throw Unhashable(o);
                ulong h = 0;
                foreach (var k in s.ToList()) { ulong x = (ulong)Hash(k); h ^= ((x ^ 89869747UL) ^ (x << 16)) * 3644798167UL; }
                h ^= ((ulong)s.Count + 1) * 1927868237UL;
                return (long)h == -1 ? 590923713 : (long)h;
            }
            if (o is PyList || o is PyDict || o is PyDictView) throw Unhashable(o);
            if (o is PyInstance && Interp.Current != null) return Interp.Current.InstanceHash((PyInstance)o);
            if (o is PyInstance) return ((PyInstance)o).Id * 16;
            if (o is PyMethod) return Hash(((PyMethod)o).Self) ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(((PyMethod)o).Func);
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
        public static PyError Unhashable(object o)
        {
            return new PyError("TypeError",
                TypeNameRu(o) + " нельзя использовать как ключ словаря или элемент множества — это изменяемое значение. Используй кортеж (tuple) или строку.",
                "unhashable type: '" + TypeName(o) + "'", 0);
        }
    }

    public class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
        public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
    }

    // ---------- Лексер ----------
    public enum T { Name, Num, Str, FStr, Op, Newline, Indent, Dedent, EOF }

    public class Token
    {
        public T Type; public string Text; public object Val; public int Line;
        public override string ToString() { return Type + ":" + Text; }
    }

    public static class Lexer
    {
        static readonly string[] Ops3 = { "**=", "//=", ">>=", "<<=", "..." };
        static readonly string[] Ops2 = { "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "**", "//", "->", ":=", "<<", ">>" };
        const string Ops1 = "+-*/%<>=()[]{}:,.;@&|^~";

        public static List<Token> Lex(string src)
        {
            var toks = new List<Token>();
            var indents = new Stack<int>(); indents.Push(0);
            src = (src ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
            if (src.Length > 0 && src[0] == '﻿') src = src.Substring(1);
            int i = 0, line = 1, depth = 0; bool lineStart = true;
            var brackets = new Stack<KeyValuePair<char, int>>();

            while (i < src.Length)
            {
                if (lineStart && depth == 0)
                {
                    int col = 0;
                    while (i < src.Length && (src[i] == ' ' || src[i] == '\f' || src[i] == '\t')) { col += src[i] == '\t' ? 4 : 1; i++; }
                    if (i >= src.Length) break;
                    if (src[i] == '\n') { i++; line++; continue; }
                    if (src[i] == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
                    if (src[i] == '\\' && i + 1 < src.Length && src[i + 1] == '\n') { i += 2; line++; continue; }
                    if (col > indents.Peek())
                    {
                        if (indents.Count > 100) throw new PyError("IndentationError", "Слишком много уровней отступа (больше 100). Вынеси часть кода в функцию.", "too many levels of indentation", line);
                        indents.Push(col); toks.Add(new Token { Type = T.Indent, Line = line });
                    }
                    else
                    {
                        while (col < indents.Peek()) { indents.Pop(); toks.Add(new Token { Type = T.Dedent, Line = line }); }
                        if (col != indents.Peek())
                            throw new PyError("IndentationError", "Отступ в этой строке не совпадает ни с одним уровнем выше. Обычно отступ — ровно 4 пробела на каждый уровень.", line);
                    }
                    lineStart = false;
                }
                char c = src[i];
                char n = i + 1 < src.Length ? src[i + 1] : '\0';

                if (c == '\n')
                {
                    if (depth == 0) { toks.Add(new Token { Type = T.Newline, Line = line }); lineStart = true; }
                    i++; line++; continue;
                }
                if (c == ' ' || c == '\f' || c == '\t') { i++; continue; }
                if (c == '#') { while (i < src.Length && src[i] != '\n') i++; continue; }
                if (c == '\\' && n == '\n') { i += 2; line++; continue; }

                if (char.IsDigit(c) || (c == '.' && char.IsDigit(n)))
                {
                    toks.Add(ReadNumber(src, ref i, line));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int s = i;
                    while (i < src.Length && (char.IsLetterOrDigit(src[i]) || src[i] == '_')) i++;
                    string name = src.Substring(s, i - s);
                    if (name.Length <= 2 && i < src.Length && (src[i] == '"' || src[i] == '\''))
                    {
                        string low = name.ToLowerInvariant();
                        if (low == "f" || low == "r" || low == "rf" || low == "fr" || low == "u")
                        {
                            bool isF = low.Contains("f"), isRaw = low.Contains("r");
                            int startLine = line;
                            string str = ReadString(src, ref i, ref line, isRaw);
                            toks.Add(new Token { Type = isF ? T.FStr : T.Str, Text = str, Val = str, Line = startLine });
                            continue;
                        }
                        if (low == "b" || low == "br" || low == "rb")
                            throw new PyError("SyntaxError", "Байтовые строки (b\"...\") не поддерживаются в игровом Python — используй обычные строки.", line);
                    }
                    toks.Add(new Token { Type = T.Name, Text = name, Line = line });
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    int startLine = line;
                    string str = ReadString(src, ref i, ref line, false);
                    toks.Add(new Token { Type = T.Str, Text = str, Val = str, Line = startLine });
                    continue;
                }

                if ((c == '&' && n == '&') || (c == '|' && n == '|'))
                    throw new PyError("SyntaxError", "В Python логические «и»/«или» пишутся словами and / or, а не && или ||.", line);
                if (c == '!' && n != '=') throw new PyError("SyntaxError", "В Python «не равно» пишется как !=, а «не» — словом not.", line);

                string op = null;
                foreach (var o in Ops3) if (string.CompareOrdinal(src, i, o, 0, 3) == 0) { op = o; break; }
                if (op == null) foreach (var o in Ops2) if (string.CompareOrdinal(src, i, o, 0, 2) == 0) { op = o; break; }
                if (op == null && Ops1.IndexOf(c) >= 0) op = c.ToString();
                if (op != null)
                {
                    if (op == "(" || op == "[" || op == "{")
                    {
                        if (depth >= 200) throw new PyError("SyntaxError", "Слишком много вложенных скобок (больше 200).", "too many nested parentheses", line);
                        depth++; brackets.Push(new KeyValuePair<char, int>(c, line));
                    }
                    if (op == ")" || op == "]" || op == "}")
                    {
                        if (depth == 0) throw new PyError("SyntaxError", "Лишняя закрывающая скобка «" + op + "».", line);
                        var open = brackets.Pop(); depth--;
                        char expect = open.Key == '(' ? ')' : open.Key == '[' ? ']' : '}';
                        if (op[0] != expect) throw new PyError("SyntaxError", "Скобка «" + open.Key + "» из строки " + open.Value + " закрыта не той скобкой «" + op + "».", line);
                    }
                    if (op == ":=")
                        throw new PyError("SyntaxError", "Оператор := («морж») пока не поддерживается в игровом Python — присвой значение отдельной строкой.", line);
                    toks.Add(new Token { Type = T.Op, Text = op, Line = line });
                    i += op.Length; continue;
                }
                if (c == '“' || c == '”' || c == '«' || c == '»' || c == '‘' || c == '’')
                    throw new PyError("SyntaxError", "Используй обычные кавычки \" или ', а не типографские «» или “”.", line);
                if (c == '$' || c == '?' || c == '`')
                    throw new PyError("SyntaxError", "Символ «" + c + "» в Python не используется (вне строк).", line);
                throw new PyError("SyntaxError", "Непонятный символ «" + c + "».", line);
            }
            if (depth > 0)
            {
                var open = brackets.Peek();
                throw new PyError("SyntaxError", "Скобка «" + open.Key + "» в строке " + open.Value + " не закрыта.", open.Value);
            }
            if (toks.Count > 0 && toks[toks.Count - 1].Type != T.Newline && toks[toks.Count - 1].Type != T.Dedent)
                toks.Add(new Token { Type = T.Newline, Line = line });
            while (indents.Count > 1) { indents.Pop(); toks.Add(new Token { Type = T.Dedent, Line = line }); }
            toks.Add(new Token { Type = T.EOF, Line = line });
            return toks;
        }

        static Token ReadNumber(string src, ref int i, int line)
        {
            int s = i;
            char c = src[i];
            char n = i + 1 < src.Length ? src[i + 1] : '\0';
            object v;
            if (c == '0' && "xXoObB".IndexOf(n) >= 0 && n != '\0')
            {
                int radix = (n == 'x' || n == 'X') ? 16 : (n == 'o' || n == 'O') ? 8 : 2;
                i += 2;
                BigInteger acc = 0; int count = 0;
                while (i < src.Length)
                {
                    char d = src[i];
                    if (d == '_') { i++; continue; }
                    int dv = HexVal(d);
                    if (dv < 0 || dv >= radix) break;
                    acc = acc * radix + dv; count++; i++;
                }
                if (count == 0) throw new PyError("SyntaxError", "После 0x/0o/0b должны идти цифры.", line);
                v = PyNum.Norm(acc);
            }
            else
            {
                bool isFloat = false;
                while (i < src.Length && (char.IsDigit(src[i]) || (src[i] == '_' && i + 1 < src.Length && char.IsDigit(src[i + 1])))) i++;
                if (i < src.Length && src[i] == '.' && !(i + 1 < src.Length && src[i + 1] == '.'))
                {
                    isFloat = true; i++;
                    while (i < src.Length && (char.IsDigit(src[i]) || (src[i] == '_' && i + 1 < src.Length && char.IsDigit(src[i + 1])))) i++;
                }
                if (i < src.Length && (src[i] == 'e' || src[i] == 'E'))
                {
                    int save = i; i++;
                    if (i < src.Length && (src[i] == '+' || src[i] == '-')) i++;
                    if (i < src.Length && char.IsDigit(src[i])) { isFloat = true; while (i < src.Length && (char.IsDigit(src[i]) || src[i] == '_')) i++; }
                    else i = save;
                }
                string txt = src.Substring(s, i - s).Replace("_", "");
                if (i < src.Length && (src[i] == 'j' || src[i] == 'J'))
                    throw new PyError("SyntaxError", "Комплексные числа (1j) не поддерживаются в игровом Python.", line);
                if (isFloat)
                {
                    double d;
                    if (!PyNum.TryParseFloat(txt, out d)) throw new PyError("SyntaxError", "Не получилось прочитать число «" + txt + "».", line);
                    v = d;
                }
                else
                {
                    if (txt.Length > 1 && txt[0] == '0' && txt.TrimStart('0').Length > 0)
                        throw new PyError("SyntaxError", "Целое число не может начинаться с нуля: «" + txt + "». Убери ведущие нули.", line);
                    long lv;
                    if (long.TryParse(txt, NumberStyles.None, CultureInfo.InvariantCulture, out lv)) v = lv;
                    else v = PyNum.Norm(BigInteger.Parse(txt, CultureInfo.InvariantCulture));
                }
            }
            var tok = new Token { Type = T.Num, Text = src.Substring(s, i - s), Val = v, Line = line };
            if (i < src.Length && (char.IsLetter(src[i]) || src[i] == '_'))
                throw new PyError("SyntaxError", "Имя переменной не может начинаться с цифры: «" + tok.Text + src[i] + "...».", line);
            return tok;
        }

        static int HexVal(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static string ReadString(string src, ref int i, ref int line, bool raw)
        {
            char q = src[i];
            bool triple = i + 2 < src.Length && src[i + 1] == q && src[i + 2] == q;
            int startLine = line;
            i += triple ? 3 : 1;
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= src.Length) throw new PyError("SyntaxError", "Строка не закрыта: не хватает закрывающей кавычки " + q + ".", startLine);
                char c = src[i];
                if (triple)
                {
                    if (c == q && i + 2 < src.Length && src[i + 1] == q && src[i + 2] == q) { i += 3; break; }
                }
                else
                {
                    if (c == q) { i++; break; }
                    if (c == '\n') throw new PyError("SyntaxError", "Строка не закрыта: не хватает закрывающей кавычки " + q + " в конце.", startLine);
                }
                if (c == '\n') line++;
                if (c == '\\' && !raw && i + 1 < src.Length)
                {
                    char e = src[i + 1]; i += 2;
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '0': case '1': case '2': case '3': case '4': case '5': case '6': case '7':
                            {
                                int code = e - '0', cnt = 1;
                                while (cnt < 3 && i < src.Length && src[i] >= '0' && src[i] <= '7') { code = code * 8 + (src[i] - '0'); i++; cnt++; }
                                sb.Append((char)code);
                                break;
                            }
                        case 'a': sb.Append('\a'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'v': sb.Append('\v'); break;
                        case '\\': sb.Append('\\'); break;
                        case '\'': sb.Append('\''); break;
                        case '"': sb.Append('"'); break;
                        case '\n': line++; break;
                        case 'x':
                        case 'u':
                        case 'U':
                            {
                                int len = e == 'x' ? 2 : e == 'u' ? 4 : 8, code = 0;
                                for (int k = 0; k < len; k++)
                                {
                                    int hv = i < src.Length ? HexVal(src[i]) : -1;
                                    if (hv < 0) throw new PyError("SyntaxError", "Неверная escape-последовательность \\" + e + " в строке.", line);
                                    code = code * 16 + hv; i++;
                                }
                                sb.Append(char.ConvertFromUtf32(code));
                                break;
                            }
                        default: sb.Append('\\').Append(e); break;
                    }
                    continue;
                }
                if (c == '\\' && raw && i + 1 < src.Length && (src[i + 1] == q || src[i + 1] == '\\'))
                { sb.Append(c).Append(src[i + 1]); i += 2; continue; }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }
    }
}
