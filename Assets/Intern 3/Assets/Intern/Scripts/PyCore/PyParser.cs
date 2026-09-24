// Игровой Python: синтаксическое дерево и парсер.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Intern.Py
{
    // ---------- Дерево ----------
    public abstract class Node { public int Line; }
    public abstract class Expr : Node { }
    public abstract class Stmt : Node { }

    public class ConstE : Expr { public object V; }
    // f-строка: Parts — string (текст) или Expr; Specs — формат (FStrE, может содержать {вложенные}) или null; Convs — 'r'/'s'/'a' или '\0'
    public class FStrE : Expr
    {
        public List<object> Parts = new List<object>();
        public List<FStrE> Specs = new List<FStrE>();
        public List<string> SpecText = new List<string>();
        public List<char> Convs = new List<char>();
    }
    public class NameE : Expr { public string Id; }
    public class BinE : Expr { public string Op; public Expr L, R; }
    public class UnaryE : Expr { public string Op; public Expr E; }
    public class BoolOpE : Expr { public string Op; public Expr L, R; }
    public class CompareE : Expr { public Expr L; public List<string> Ops = new List<string>(); public List<Expr> Rs = new List<Expr>(); }
    // Args может содержать StarredE (*args); в Kw ключ null означает **kwargs
    public class CallE : Expr { public Expr F; public List<Expr> Args = new List<Expr>(); public List<KeyValuePair<string, Expr>> Kw = new List<KeyValuePair<string, Expr>>(); }
    public class AttrE : Expr { public Expr Obj; public string Name; }
    public class IndexE : Expr { public Expr Obj; public Expr Idx; }
    public class SliceE : Expr { public Expr Lo, Hi, Step; }
    public class ListE : Expr { public List<Expr> Items = new List<Expr>(); }
    public class TupleE : Expr { public List<Expr> Items = new List<Expr>(); }
    public class SetE : Expr { public List<Expr> Items = new List<Expr>(); }
    // ключ null означает **словарь
    public class DictE : Expr { public List<Expr> Keys = new List<Expr>(), Vals = new List<Expr>(); }
    public class IfExpE : Expr { public Expr Cond, A, B; }
    public class StarredE : Expr { public Expr E; }
    public class LambdaE : Expr { public List<Param> Params = new List<Param>(); public Expr Body; public ScopeInfo Scope; }
    public class CompFor { public Expr Target, Iter; public List<Expr> Ifs = new List<Expr>(); }
    // генераторы: Kind = "list" | "set" | "dict" | "gen"; для dict Elt — ключ, Val — значение
    public class CompE : Expr { public string Kind; public Expr Elt, Val; public List<CompFor> Fors = new List<CompFor>(); }

    public class Param
    {
        public string Name; public Expr Default; public Expr Annotation;
        public int Kind;   // 0 — обычный, 1 — *args, 2 — только по имени (после *), 3 — **kwargs
        public bool PosOnly;   // до «/» — только по позиции
    }
    public class ScopeInfo
    {
        public HashSet<string> Locals = new HashSet<string>();
        public HashSet<string> Globals = new HashSet<string>();
        public HashSet<string> Nonlocals = new HashSet<string>();
    }

    public class ExprS : Stmt { public Expr E; }
    public class AssignS : Stmt { public List<Expr> Targets = new List<Expr>(); public Expr Value; }
    public class AugAssignS : Stmt { public Expr Target; public string Op; public Expr Value; }
    public class AnnAssignS : Stmt { public Expr Target; public Expr Annotation; public Expr Value; }
    public class IfS : Stmt { public Expr Cond; public List<Stmt> Body; public List<Stmt> Else; public bool IsElif; }
    public class WhileS : Stmt { public Expr Cond; public List<Stmt> Body; public List<Stmt> OrElse; }
    public class ForS : Stmt { public Expr Target; public Expr Iter; public List<Stmt> Body; public List<Stmt> OrElse; }
    public class DefS : Stmt
    {
        public string Name; public List<Param> Params = new List<Param>(); public List<Stmt> Body;
        public List<Expr> Decorators = new List<Expr>(); public Expr Returns; public ScopeInfo Scope;
        public List<string> ParamNames()
        {
            return Params.Select(x => (x.Kind == 1 ? "*" : x.Kind == 3 ? "**" : "") + x.Name).ToList();
        }
    }
    public class ClassS : Stmt { public string Name; public List<Expr> Bases = new List<Expr>(); public List<Stmt> Body; public List<Expr> Decorators = new List<Expr>(); }
    public class ReturnS : Stmt { public Expr Value; }
    public class BreakS : Stmt { }
    public class ContinueS : Stmt { }
    public class PassS : Stmt { }
    public class Handler : Node { public Expr Type; public string Name; public List<Stmt> Body; }
    public class TryS : Stmt { public List<Stmt> Body; public List<Handler> Handlers = new List<Handler>(); public List<Stmt> Else, Finally; }
    public class RaiseS : Stmt { public Expr Exc, Cause; }
    public class AssertS : Stmt { public Expr Test, Msg; }
    public class DelS : Stmt { public List<Expr> Targets = new List<Expr>(); }
    public class GlobalS : Stmt { public List<string> Names = new List<string>(); }
    public class NonlocalS : Stmt { public List<string> Names = new List<string>(); }
    public class ImportS : Stmt { public List<KeyValuePair<string, string>> Names = new List<KeyValuePair<string, string>>(); } // модуль, as
    public class ImportFromS : Stmt { public string Module; public List<KeyValuePair<string, string>> Names = new List<KeyValuePair<string, string>>(); public bool Star; }

    // ---------- Парсер ----------
    public class Parser
    {
        List<Token> t; int p;

        public static readonly HashSet<string> Keywords = new HashSet<string> {
            "if","elif","else","while","for","in","def","return","break","continue","pass",
            "and","or","not","True","False","None","is","lambda","import","from","class",
            "try","except","finally","with","as","global","nonlocal","yield","raise","del","assert","async","await" };
        static readonly HashSet<string> NotYet = new HashSet<string> { "with", "yield", "async", "await" };

        public Parser(List<Token> tokens) { t = tokens; }

        // Одинаковые константы модуля — один и тот же объект, как в CPython (кэш констант компилятора):
        // x = 1000; y = 1000; x is y → True, и "a" is "a" → True. Ключ учитывает тип: 1, 1.0 и True — разные константы.
        readonly Dictionary<string, object> consts = new Dictionary<string, object>();
        object Canon(object v)
        {
            if (v == null || v is bool) return v;
            string key;
            if (v is string) key = "s" + (string)v;
            else if (v is long) key = "i" + ((long)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if (v is System.Numerics.BigInteger) key = "i" + ((System.Numerics.BigInteger)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if (v is double) key = "f" + BitConverter.DoubleToInt64Bits((double)v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else return v;
            object c;
            if (consts.TryGetValue(key, out c)) return c;
            consts[key] = v;
            return v;
        }

        public static List<Stmt> ParseProgram(string src)
        {
            var ps = new Parser(Lexer.Lex(src));
            var list = new List<Stmt>();
            while (ps.Peek.Type != T.EOF)
            {
                if (ps.Peek.Type == T.Newline) { ps.p++; continue; }
                if (ps.Peek.Type == T.Indent)
                    throw new PyError("IndentationError", "Лишний отступ в начале строки. Отступ нужен только внутри блока после двоеточия (if, for, while, def).", ps.Peek.Line);
                if (ps.Peek.Type == T.Dedent) { ps.p++; continue; }
                ps.Statement(list);
            }
            CheckStructure(list, false, false, new List<ScopeInfo>(), true);
            return list;
        }

        // ----- Проверки, которые CPython делает при компиляции (до запуска программы) -----
        // return вне функции, break/continue вне цикла, nonlocal без привязки, global после присваивания.
        static void CheckStructure(List<Stmt> body, bool inFunc, bool inLoop, List<ScopeInfo> fnScopes, bool atModule)
        {
            if (body == null) return;
            HashSet<string> assignedSoFar = inFunc ? new HashSet<string>() : null;
            foreach (var s in body) CheckStmt(s, inFunc, inLoop, fnScopes, atModule, assignedSoFar);
        }

        static void CheckStmt(Stmt s, bool inFunc, bool inLoop, List<ScopeInfo> fnScopes, bool atModule, HashSet<string> assigned)
        {
            if (s is ReturnS && !inFunc)
                throw new PyError("SyntaxError", "return можно писать только внутри функции (после def).", "'return' outside function", s.Line);
            if (s is BreakS && !inLoop)
                throw new PyError("SyntaxError", "break можно использовать только внутри цикла for или while.", "'break' outside loop", s.Line);
            if (s is ContinueS && !inLoop)
                throw new PyError("SyntaxError", "continue можно использовать только внутри цикла for или while.", "'continue' not properly in loop", s.Line);
            var nl = s as NonlocalS;
            if (nl != null)
            {
                if (fnScopes.Count == 0)
                    throw new PyError("SyntaxError", "nonlocal пишут только во вложенной функции — он ссылается на переменную внешней функции. На уровне модуля используй global.", "nonlocal declaration not allowed at module level", s.Line);
                foreach (var n in nl.Names)
                {
                    bool found = false;
                    for (int i = fnScopes.Count - 2; i >= 0 && !found; i--)
                        if (fnScopes[i].Locals.Contains(n) || fnScopes[i].Nonlocals.Contains(n)) found = true;
                    if (!found)
                        throw new PyError("SyntaxError", "nonlocal " + n + ": во внешних функциях нет переменной «" + n + "». nonlocal работает только с переменными объемлющей функции (для глобальных — global).",
                            "no binding for nonlocal '" + n + "' found", s.Line);
                }
            }
            var gl = s as GlobalS;
            if (gl != null && assigned != null)
                foreach (var n in gl.Names)
                    if (assigned.Contains(n))
                        throw new PyError("SyntaxError", "global " + n + " нужно написать в начале функции — до того, как переменной что-то присвоили.", "name '" + n + "' is assigned to before global declaration", s.Line);
            if (assigned != null)
            {
                if (s is AssignS) foreach (var tg in ((AssignS)s).Targets) CollectTarget(tg, assigned);
                else if (s is AugAssignS) CollectTarget(((AugAssignS)s).Target, assigned);
                else if (s is AnnAssignS && ((AnnAssignS)s).Value != null) CollectTarget(((AnnAssignS)s).Target, assigned);
                else if (s is ForS) CollectTarget(((ForS)s).Target, assigned);
            }
            var d = s as DefS;
            if (d != null)
            {
                var sc = new List<ScopeInfo>(fnScopes) { d.Scope };
                CheckStructure(d.Body, true, false, sc, false);
                return;
            }
            var c = s as ClassS;
            if (c != null) { CheckStructure(c.Body, false, false, fnScopes, atModule && false); return; }
            var ifs = s as IfS;
            if (ifs != null) { CheckBlockIn(ifs.Body, inFunc, inLoop, fnScopes, atModule, assigned); CheckBlockIn(ifs.Else, inFunc, inLoop, fnScopes, atModule, assigned); return; }
            var fs = s as ForS;
            if (fs != null) { CheckBlockIn(fs.Body, inFunc, true, fnScopes, atModule, assigned); CheckBlockIn(fs.OrElse, inFunc, inLoop, fnScopes, atModule, assigned); return; }
            var ws = s as WhileS;
            if (ws != null) { CheckBlockIn(ws.Body, inFunc, true, fnScopes, atModule, assigned); CheckBlockIn(ws.OrElse, inFunc, inLoop, fnScopes, atModule, assigned); return; }
            var ts = s as TryS;
            if (ts != null)
            {
                CheckBlockIn(ts.Body, inFunc, inLoop, fnScopes, atModule, assigned);
                foreach (var h in ts.Handlers) CheckBlockIn(h.Body, inFunc, inLoop, fnScopes, atModule, assigned);
                CheckBlockIn(ts.Else, inFunc, inLoop, fnScopes, atModule, assigned);
                CheckBlockIn(ts.Finally, inFunc, inLoop, fnScopes, atModule, assigned);
            }
        }

        static void CheckBlockIn(List<Stmt> body, bool inFunc, bool inLoop, List<ScopeInfo> fnScopes, bool atModule, HashSet<string> assigned)
        {
            if (body == null) return;
            foreach (var s in body) CheckStmt(s, inFunc, inLoop, fnScopes, atModule, assigned);
        }

        Token Peek { get { return t[p]; } }
        Token PeekAt(int k) { return t[Math.Min(p + k, t.Count - 1)]; }
        bool IsOp(string s) { return Peek.Type == T.Op && Peek.Text == s; }
        bool IsKw(string s) { return Peek.Type == T.Name && Peek.Text == s; }
        Token Next() { return t[p++]; }
        bool AtLineEnd { get { return Peek.Type == T.Newline || Peek.Type == T.EOF || Peek.Type == T.Dedent; } }

        void ExpectOp(string s, string ruHint)
        {
            if (!IsOp(s)) throw new PyError("SyntaxError", ruHint, Peek.Line);
            p++;
        }

        string ExpectName(string ruHint)
        {
            if (Peek.Type != T.Name || Keywords.Contains(Peek.Text)) throw new PyError("SyntaxError", ruHint, Peek.Line);
            return Next().Text;
        }

        static string Describe(Token k)
        {
            switch (k.Type)
            {
                case T.Newline: return "конец строки";
                case T.EOF: return "конец программы";
                case T.Indent: return "отступ";
                case T.Dedent: return "конец блока";
                case T.Str: case T.FStr: return "строку";
                case T.Num: return "число " + k.Text;
                default: return "«" + k.Text + "»";
            }
        }

        static PyError NotYetError(string what, int line)
        {
            return new PyError("SyntaxError", "Конструкция «" + what + "» пока не поддерживается в игровом Python — она откроется в следующих главах.", line);
        }

        // ----- Инструкции -----
        void Statement(List<Stmt> into)
        {
            var k = Peek;
            if (k.Type == T.Name)
            {
                if (NotYet.Contains(k.Text)) throw NotYetError(k.Text, k.Line);
                if (k.Text == "match" && LooksLikeMatch()) throw NotYetError("match", k.Line);
                switch (k.Text)
                {
                    case "if": into.Add(IfStmt(false)); return;
                    case "while": into.Add(WhileStmt()); return;
                    case "for": into.Add(ForStmt()); return;
                    case "def": into.Add(DefStmt(new List<Expr>())); return;
                    case "class": into.Add(ClassStmt(new List<Expr>())); return;
                    case "try": into.Add(TryStmt()); return;
                    case "elif":
                    case "else":
                        throw new PyError("SyntaxError", "«" + k.Text + "» должен стоять сразу после блока if (или цикла) и на том же уровне отступа.", k.Line);
                    case "except":
                    case "finally":
                        throw new PyError("SyntaxError", "«" + k.Text + "» должен стоять сразу после блока try и на том же уровне отступа, что и try.", k.Line);
                }
            }
            if (IsOp("@")) { into.Add(Decorated()); return; }
            SimpleLine(into);
        }

        bool LooksLikeMatch()
        {
            var nx = PeekAt(1);
            if (nx.Type == T.Newline || nx.Type == T.EOF) return false;
            if (nx.Type == T.Op && nx.Text != "(" && nx.Text != "[" && nx.Text != "-" && nx.Text != "{") return false;
            int j = p + 1; int depth = 0;
            Token last = null;
            while (j < t.Count && t[j].Type != T.Newline && t[j].Type != T.EOF)
            {
                if (t[j].Type == T.Op && (t[j].Text == "(" || t[j].Text == "[" || t[j].Text == "{")) depth++;
                if (t[j].Type == T.Op && (t[j].Text == ")" || t[j].Text == "]" || t[j].Text == "}")) depth--;
                last = t[j]; j++;
            }
            return last != null && last.Type == T.Op && last.Text == ":" && depth == 0;
        }

        void SimpleLine(List<Stmt> into)
        {
            into.Add(SimpleStmt());
            while (IsOp(";"))
            {
                p++;
                if (AtLineEnd) break;
                into.Add(SimpleStmt());
            }
            EndOfLine();
        }

        void EndOfLine()
        {
            if (Peek.Type == T.Newline) { p++; return; }
            if (Peek.Type == T.EOF || Peek.Type == T.Dedent) return;
            if (IsOp(":")) throw new PyError("SyntaxError", "Неожиданное двоеточие. Двоеточие ставится после if, elif, else, for, while, def, class, try, except.", Peek.Line);
            if (IsOp("=")) throw new PyError("SyntaxError", "Здесь нельзя присваивать. Если хотел сравнить — используй == (два знака равно).", Peek.Line);
            if (Peek.Type == T.Name && (Peek.Text == "if" || Peek.Text == "for" || Peek.Text == "while"))
                throw new PyError("SyntaxError", "Не ожидал здесь «" + Peek.Text + "». Каждая инструкция пишется с новой строки.", Peek.Line);
            throw new PyError("SyntaxError", "Не ожидал здесь " + Describe(Peek) + ". Возможно, пропущена запятая, оператор или скобка.", Peek.Line);
        }

        static readonly HashSet<string> AugOps = new HashSet<string> { "+=", "-=", "*=", "/=", "//=", "%=", "**=", "&=", "|=", "^=", ">>=", "<<=" };

        Stmt SimpleStmt()
        {
            int line = Peek.Line;
            if (Peek.Type == T.Name)
            {
                switch (Peek.Text)
                {
                    case "pass": p++; return new PassS { Line = line };
                    case "break": p++; return new BreakS { Line = line };
                    case "continue": p++; return new ContinueS { Line = line };
                    case "return":
                        {
                            p++;
                            Expr v = null;
                            if (!AtLineEnd && !IsOp(";")) v = TestListStar();
                            return new ReturnS { Line = line, Value = v };
                        }
                    case "global":
                    case "nonlocal":
                        {
                            bool g = Next().Text == "global";
                            var names = new List<string>();
                            do
                            {
                                if (IsOp(",")) p++;
                                names.Add(ExpectName("После " + (g ? "global" : "nonlocal") + " перечисли имена переменных через запятую."));
                            } while (IsOp(","));
                            if (g) return new GlobalS { Line = line, Names = names };
                            return new NonlocalS { Line = line, Names = names };
                        }
                    case "del":
                        {
                            p++;
                            var d = new DelS { Line = line };
                            do
                            {
                                if (IsOp(",")) { p++; if (AtLineEnd) break; }
                                var e = BitOr();
                                CheckDelTarget(e);
                                d.Targets.Add(e);
                            } while (IsOp(","));
                            return d;
                        }
                    case "assert":
                        {
                            p++;
                            var a = new AssertS { Line = line, Test = Test() };
                            if (IsOp(",")) { p++; a.Msg = Test(); }
                            return a;
                        }
                    case "raise":
                        {
                            p++;
                            var r = new RaiseS { Line = line };
                            if (!AtLineEnd && !IsOp(";"))
                            {
                                r.Exc = Test();
                                if (IsKw("from")) { p++; r.Cause = Test(); }
                            }
                            return r;
                        }
                    case "import": return ImportStmt();
                    case "from": return FromStmt();
                    case "print":
                        if (PeekAt(1).Type == T.Str || PeekAt(1).Type == T.FStr || PeekAt(1).Type == T.Num || (PeekAt(1).Type == T.Name && !Keywords.Contains(PeekAt(1).Text)))
                            throw new PyError("SyntaxError", "print — это функция, ей нужны скобки: print(\"...\").", line);
                        break;
                }
            }

            var first = TestListStar();
            if (IsOp(":"))
            {
                if (!(first is NameE || first is AttrE || first is IndexE))
                    throw new PyError("SyntaxError", "Неожиданное двоеточие. Двоеточие ставится после if, elif, else, for, while, def, class, try, except.", Peek.Line);
                p++;
                var ann = new AnnAssignS { Line = line, Target = first, Annotation = Test() };
                if (IsOp("=")) { p++; ann.Value = TestListStar(); }
                return ann;
            }
            if (IsOp("="))
            {
                var a = new AssignS { Line = line };
                a.Targets.Add(CheckTarget(first));
                while (IsOp("="))
                {
                    p++;
                    if (IsKw("yield")) throw NotYetError("yield", Peek.Line);
                    var v = TestListStar();
                    if (IsOp("=")) a.Targets.Add(CheckTarget(v)); else { a.Value = v; break; }
                }
                return a;
            }
            if (Peek.Type == T.Op && AugOps.Contains(Peek.Text))
            {
                string op = Next().Text; op = op.Substring(0, op.Length - 1);
                if (!(first is NameE || first is IndexE || first is AttrE)) throw new PyError("SyntaxError", "Слева от «" + op + "=» должна быть переменная.", line);
                return new AugAssignS { Line = line, Target = first, Op = op, Value = TestListStar() };
            }
            return new ExprS { Line = line, E = first };
        }

        string DottedName()
        {
            var sb = new StringBuilder(ExpectName("После import нужно имя модуля, например: import math"));
            while (IsOp(".")) { p++; sb.Append('.').Append(ExpectName("После точки нужно имя модуля.")); }
            return sb.ToString();
        }

        Stmt ImportStmt()
        {
            int line = Next().Line;
            var s = new ImportS { Line = line };
            do
            {
                if (IsOp(",")) p++;
                string mod = DottedName(); string asn = null;
                if (IsKw("as")) { p++; asn = ExpectName("После as нужно имя."); }
                s.Names.Add(new KeyValuePair<string, string>(mod, asn));
            } while (IsOp(","));
            return s;
        }

        Stmt FromStmt()
        {
            int line = Next().Line;
            if (IsOp(".")) throw new PyError("SyntaxError", "Относительный импорт (from . import ...) не поддерживается: в игре программа — один файл.", line);
            var s = new ImportFromS { Line = line, Module = DottedName() };
            if (!IsKw("import")) throw new PyError("SyntaxError", "Нужно: from модуль import имя", Peek.Line);
            p++;
            if (IsOp("*")) { p++; s.Star = true; return s; }
            bool paren = IsOp("(");
            if (paren) p++;
            do
            {
                if (IsOp(",")) { p++; if (paren && IsOp(")")) break; }
                string nm = ExpectName("После import перечисли имена через запятую."); string asn = null;
                if (IsKw("as")) { p++; asn = ExpectName("После as нужно имя."); }
                s.Names.Add(new KeyValuePair<string, string>(nm, asn));
            } while (IsOp(","));
            if (paren) ExpectOp(")", "Не хватает закрывающей скобки ).");
            return s;
        }

        Expr CheckTarget(Expr e)
        {
            if (e is NameE || e is IndexE || e is AttrE) return e;
            if (e is TupleE || e is ListE)
            {
                var items = e is TupleE ? ((TupleE)e).Items : ((ListE)e).Items;
                int stars = 0;
                foreach (var it in items)
                {
                    if (it is StarredE) { stars++; CheckTarget(((StarredE)it).E); }
                    else CheckTarget(it);
                }
                if (stars > 1) throw new PyError("SyntaxError", "В распаковке может быть только одна переменная со звёздочкой (*).", e.Line);
                return e;
            }
            if (e is StarredE) throw new PyError("SyntaxError", "Переменная со звёздочкой (*rest) может стоять только среди нескольких переменных: first, *rest = ...", e.Line);
            if (e is ConstE && ((ConstE)e).V is string)
                throw new PyError("SyntaxError", "Слева от = должно быть имя переменной без кавычек. Строку в кавычках нельзя «переназначить».", e.Line);
            if (e is ConstE)
                throw new PyError("SyntaxError", "Слева от = должно быть имя переменной, а не значение. Пиши так: x = 5, а не 5 = x.", e.Line);
            if (e is CallE)
                throw new PyError("SyntaxError", "Нельзя присвоить значение вызову функции. Если хотел сравнить — используй ==.", e.Line);
            if (e is CompareE)
                throw new PyError("SyntaxError", "Слева от = стоит сравнение. Если хотел сравнить — используй == внутри if, а для присваивания слева должно быть только имя.", e.Line);
            throw new PyError("SyntaxError", "Слева от = должно быть имя переменной.", e.Line);
        }

        void CheckDelTarget(Expr e)
        {
            if (e is NameE || e is IndexE || e is AttrE) return;
            if (e is TupleE) { foreach (var x in ((TupleE)e).Items) CheckDelTarget(x); return; }
            if (e is ListE) { foreach (var x in ((ListE)e).Items) CheckDelTarget(x); return; }
            throw new PyError("SyntaxError", "После del пишут переменную, элемент списка/словаря (d[k]) или атрибут.", e.Line);
        }

        List<Stmt> Block(string owner)
        {
            if (!IsOp(":"))
                throw new PyError("SyntaxError", "После " + owner + " нужно поставить двоеточие «:» в конце строки.", Peek.Line);
            int colonLine = Peek.Line;
            p++;
            var body = new List<Stmt>();
            if (Peek.Type != T.Newline)
            {
                if (Peek.Type == T.EOF) throw new PyError("SyntaxError", "После двоеточия (строка " + colonLine + ") нужен блок кода с отступом.", colonLine);
                // блок в одну строку: if x: print(x)
                SimpleLine(body);
                return body;
            }
            p++;
            if (Peek.Type != T.Indent)
                throw new PyError("IndentationError", "После строки с двоеточием (строка " + colonLine + ") следующая строка должна быть с отступом — 4 пробела (или Tab).", Peek.Line);
            p++;
            while (Peek.Type != T.Dedent && Peek.Type != T.EOF)
            {
                if (Peek.Type == T.Newline) { p++; continue; }
                if (Peek.Type == T.Indent) throw new PyError("IndentationError", "Лишний отступ: эта строка сдвинута сильнее, чем строки выше в том же блоке.", Peek.Line);
                Statement(body);
            }
            if (Peek.Type == T.Dedent) p++;
            return body;
        }

        Stmt IfStmt(bool isElif)
        {
            int line = Next().Line;
            var cond = NamedCond();
            var s = new IfS { Line = line, Cond = cond, IsElif = isElif, Body = Block(isElif ? "elif-условия" : "if-условия") };
            if (IsKw("elif")) { s.Else = new List<Stmt> { IfStmt(true) }; }
            else if (IsKw("else")) { p++; s.Else = Block("else"); }
            return s;
        }

        Expr NamedCond()
        {
            if (IsOp(":")) throw new PyError("SyntaxError", "После if/while нужно условие, а потом двоеточие.", Peek.Line);
            var c = Test();
            if (IsOp("=")) throw new PyError("SyntaxError", "В условии для сравнения нужно ==, а одиночное = — это присваивание.", Peek.Line);
            return c;
        }

        Stmt WhileStmt()
        {
            int line = Next().Line;
            var w = new WhileS { Line = line, Cond = NamedCond(), Body = Block("while-условия") };
            if (IsKw("else")) { p++; w.OrElse = Block("else"); }
            return w;
        }

        Stmt ForStmt()
        {
            int line = Next().Line;
            var target = TargetList("После for должно идти имя переменной.");
            if (!IsKw("in")) throw new PyError("SyntaxError", "В цикле for нужно слово in: for элемент in коллекция:", Peek.Line);
            p++;
            var iter = TestListStar();
            var f = new ForS { Line = line, Target = target, Iter = iter, Body = Block("for") };
            if (IsKw("else")) { p++; f.OrElse = Block("else"); }
            return f;
        }

        // Цели для for и генераторов: x / x, y / (a, b), c / *rest
        Expr TargetList(string err)
        {
            int line = Peek.Line;
            var items = new List<Expr>();
            bool comma = false;
            while (true)
            {
                if (IsOp("*")) { int l = Next().Line; items.Add(new StarredE { Line = l, E = BitOr() }); }
                else
                {
                    if (Peek.Type == T.Name && Keywords.Contains(Peek.Text) && Peek.Text != "True" && Peek.Text != "False" && Peek.Text != "None")
                        throw new PyError("SyntaxError", err, Peek.Line);
                    if (Peek.Type != T.Name && !IsOp("(") && !IsOp("[")) throw new PyError("SyntaxError", err, Peek.Line);
                    items.Add(BitOr());
                }
                if (!IsOp(",")) break;
                p++; comma = true;
                if (IsKw("in")) break;
            }
            Expr res;
            if (items.Count == 1 && !comma) res = items[0];
            else { var te = new TupleE { Line = line }; te.Items.AddRange(items); res = te; }
            if (res is StarredE) throw new PyError("SyntaxError", "Переменная со звёздочкой (*rest) может стоять только среди нескольких переменных.", line);
            return CheckTarget(res);
        }

        Stmt Decorated()
        {
            var decos = new List<Expr>();
            while (IsOp("@"))
            {
                p++;
                decos.Add(Test());
                if (Peek.Type != T.Newline) throw new PyError("SyntaxError", "После декоратора (@...) должна идти новая строка с def или class.", Peek.Line);
                p++;
                while (Peek.Type == T.Newline) p++;
            }
            if (IsKw("def")) return DefStmt(decos);
            if (IsKw("class")) return ClassStmt(decos);
            throw new PyError("SyntaxError", "После декоратора (@...) должна идти функция (def) или класс (class).", Peek.Line);
        }

        List<Param> Params(string closer, bool annotations, string fname)
        {
            var ps = new List<Param>();
            bool star = false, sawDefault = false;
            var seen = new HashSet<string>();
            while (!IsOp(closer))
            {
                var pr = new Param();
                if (IsOp("/")) { p++; foreach (var q in ps) if (q.Kind == 0) q.PosOnly = true; if (IsOp(",")) p++; continue; }
                if (IsOp("**"))
                {
                    p++; pr.Kind = 3; pr.Name = ExpectName("После ** нужно имя, например **kwargs.");
                }
                else if (IsOp("*"))
                {
                    p++;
                    if (star) throw new PyError("SyntaxError", "Звёздочка * в параметрах может быть только одна.", Peek.Line);
                    star = true;
                    if (IsOp(",")) { p++; continue; }
                    pr.Kind = 1; pr.Name = ExpectName("После * нужно имя, например *args.");
                }
                else
                {
                    if (Peek.Type != T.Name || Keywords.Contains(Peek.Text))
                        throw new PyError("SyntaxError", "Параметры функции — это имена через запятую.", Peek.Line);
                    pr.Name = Next().Text; pr.Kind = star ? 2 : 0;
                }
                if (annotations && IsOp(":") && closer == ")") { p++; pr.Annotation = Test(); }
                if (IsOp("="))
                {
                    if (pr.Kind == 1 || pr.Kind == 3) throw new PyError("SyntaxError", "У *args и **kwargs не бывает значений по умолчанию.", Peek.Line);
                    p++; pr.Default = Test();
                    if (pr.Kind == 0) sawDefault = true;
                }
                else if (pr.Kind == 0 && sawDefault)
                    throw new PyError("SyntaxError", "Параметры без значения по умолчанию должны идти до параметров со значением.", Peek.Line);
                if (!seen.Add(pr.Name)) throw new PyError("SyntaxError", "Параметр «" + pr.Name + "» повторяется в " + fname + ".", Peek.Line);
                ps.Add(pr);
                if (IsOp(",")) p++;
                else if (!IsOp(closer)) throw new PyError("SyntaxError", "Между параметрами нужна запятая.", Peek.Line);
            }
            return ps;
        }

        Stmt DefStmt(List<Expr> decos)
        {
            int line = Next().Line;
            var d = new DefS { Line = line, Decorators = decos };
            d.Name = ExpectName("После def должно идти имя функции.");
            ExpectOp("(", "После имени функции нужны круглые скобки: def " + d.Name + "():");
            d.Params = Params(")", true, d.Name + "()");
            p++;
            if (IsOp("->")) { p++; d.Returns = Test(); }
            d.Body = Block("объявления функции");
            d.Scope = Analyze(d.Params, d.Body);
            return d;
        }

        Stmt ClassStmt(List<Expr> decos)
        {
            int line = Next().Line;
            var c = new ClassS { Line = line, Decorators = decos };
            c.Name = ExpectName("После class должно идти имя класса, например class User:");
            if (IsOp("("))
            {
                p++;
                while (!IsOp(")"))
                {
                    if (Peek.Type == T.Name && PeekAt(1).Type == T.Op && PeekAt(1).Text == "=") { p += 2; Test(); }
                    else c.Bases.Add(Test());
                    if (IsOp(",")) p++;
                    else if (!IsOp(")")) throw new PyError("SyntaxError", "Базовые классы перечисляются через запятую.", Peek.Line);
                }
                p++;
            }
            c.Body = Block("объявления класса");
            return c;
        }

        Stmt TryStmt()
        {
            int line = Next().Line;
            var s = new TryS { Line = line, Body = Block("try") };
            while (IsKw("except"))
            {
                var h = new Handler { Line = Next().Line };
                if (!IsOp(":"))
                {
                    h.Type = Test();
                    if (IsOp(",")) throw new PyError("SyntaxError", "Несколько типов исключений пишут в скобках: except (ValueError, TypeError):", Peek.Line);
                    if (IsKw("as")) { p++; h.Name = ExpectName("После as нужно имя переменной, например: except ValueError as e:"); }
                }
                h.Body = Block("except");
                s.Handlers.Add(h);
            }
            if (IsKw("else"))
            {
                if (s.Handlers.Count == 0) throw new PyError("SyntaxError", "Блок else у try возможен только после except.", Peek.Line);
                p++; s.Else = Block("else");
            }
            if (IsKw("finally")) { p++; s.Finally = Block("finally"); }
            if (s.Handlers.Count == 0 && s.Finally == null)
                throw new PyError("SyntaxError", "После блока try нужен хотя бы один except или finally.", Peek.Line);
            return s;
        }

        // ----- Анализ областей видимости (какие имена локальные) -----
        public static ScopeInfo Analyze(List<Param> ps, List<Stmt> body)
        {
            var sc = new ScopeInfo();
            foreach (var pr in ps) sc.Locals.Add(pr.Name);
            var assigned = new HashSet<string>();
            CollectBlock(body, assigned, sc);
            foreach (var n in assigned) sc.Locals.Add(n);
            foreach (var g in sc.Globals) sc.Locals.Remove(g);
            foreach (var g in sc.Nonlocals) sc.Locals.Remove(g);
            return sc;
        }
        static void CollectBlock(List<Stmt> body, HashSet<string> a, ScopeInfo sc)
        {
            if (body == null) return;
            foreach (var s in body) CollectStmt(s, a, sc);
        }
        static void CollectStmt(Stmt s, HashSet<string> a, ScopeInfo sc)
        {
            if (s is AssignS) { foreach (var tg in ((AssignS)s).Targets) CollectTarget(tg, a); }
            else if (s is AugAssignS) CollectTarget(((AugAssignS)s).Target, a);
            else if (s is AnnAssignS) CollectTarget(((AnnAssignS)s).Target, a);
            else if (s is ForS) { var f = (ForS)s; CollectTarget(f.Target, a); CollectBlock(f.Body, a, sc); CollectBlock(f.OrElse, a, sc); }
            else if (s is WhileS) { CollectBlock(((WhileS)s).Body, a, sc); CollectBlock(((WhileS)s).OrElse, a, sc); }
            else if (s is IfS) { CollectBlock(((IfS)s).Body, a, sc); CollectBlock(((IfS)s).Else, a, sc); }
            else if (s is DefS) a.Add(((DefS)s).Name);
            else if (s is ClassS) a.Add(((ClassS)s).Name);
            else if (s is TryS)
            {
                var tr = (TryS)s;
                CollectBlock(tr.Body, a, sc);
                foreach (var h in tr.Handlers) { if (h.Name != null) a.Add(h.Name); CollectBlock(h.Body, a, sc); }
                CollectBlock(tr.Else, a, sc); CollectBlock(tr.Finally, a, sc);
            }
            else if (s is DelS) { foreach (var tg in ((DelS)s).Targets) CollectTarget(tg, a); }
            else if (s is GlobalS) foreach (var n in ((GlobalS)s).Names) sc.Globals.Add(n);
            else if (s is NonlocalS) foreach (var n in ((NonlocalS)s).Names) sc.Nonlocals.Add(n);
            else if (s is ImportS) foreach (var kv in ((ImportS)s).Names) a.Add(kv.Value ?? kv.Key.Split('.')[0]);
            else if (s is ImportFromS) foreach (var kv in ((ImportFromS)s).Names) a.Add(kv.Value ?? kv.Key);
        }
        static void CollectTarget(Expr e, HashSet<string> a)
        {
            if (e is NameE) a.Add(((NameE)e).Id);
            else if (e is TupleE) foreach (var x in ((TupleE)e).Items) CollectTarget(x, a);
            else if (e is ListE) foreach (var x in ((ListE)e).Items) CollectTarget(x, a);
            else if (e is StarredE) CollectTarget(((StarredE)e).E, a);
        }

        // ----- Выражения -----
        Expr StarOrTest()
        {
            if (IsOp("*")) { int l = Next().Line; return new StarredE { Line = l, E = BitOr() }; }
            return Test();
        }

        Expr TestListStar()
        {
            int line = Peek.Line;
            var first = StarOrTest();
            if (!IsOp(",")) return first;
            var te = new TupleE { Line = line }; te.Items.Add(first);
            while (IsOp(","))
            {
                p++;
                if (AtLineEnd || IsOp("=") || IsOp(")") || IsOp(";") || IsOp(":") || (Peek.Type == T.Op && AugOps.Contains(Peek.Text))) break;
                te.Items.Add(StarOrTest());
            }
            return te;
        }

        // Ограничение вложенности выражений: парсер вызывается и в главном потоке игры (подсветка ошибок),
        // слишком глубокая рекурсия там уронила бы Unity. CPython тоже ограничивает вложенность (200 скобок).
        int nest;
        const int MaxNest = 200;
        void Deeper()
        {
            if (++nest > MaxNest)
                throw new PyError("SyntaxError", "Слишком глубокая вложенность в выражении (больше " + MaxNest + " уровней скобок или операторов). Разбей выражение на части.", "too many nested parentheses", Peek.Line);
        }

        public Expr Test()
        {
            Deeper();
            try { return TestInner(); }
            finally { nest--; }
        }

        Expr TestInner()
        {
            if (IsKw("lambda")) return Lambda();
            var e = OrTest();
            if (IsKw("if"))
            {
                int line = Peek.Line; p++;
                var cond = OrTest();
                if (!IsKw("else")) throw new PyError("SyntaxError", "В выражении «A if условие else B» не хватает else.", Peek.Line);
                p++;
                return new IfExpE { Line = line, Cond = cond, A = e, B = Test() };
            }
            return e;
        }

        Expr Lambda()
        {
            int line = Next().Line;
            var l = new LambdaE { Line = line };
            l.Params = Params(":", false, "lambda");
            ExpectOp(":", "После параметров lambda нужно двоеточие: lambda x: x * 2");
            l.Body = Test();
            l.Scope = Analyze(l.Params, new List<Stmt>());
            return l;
        }

        Expr OrTest()
        {
            var e = AndTest();
            while (IsKw("or")) { int l = Next().Line; e = new BoolOpE { Line = l, Op = "or", L = e, R = AndTest() }; }
            return e;
        }
        Expr AndTest()
        {
            var e = NotTest();
            while (IsKw("and")) { int l = Next().Line; e = new BoolOpE { Line = l, Op = "and", L = e, R = NotTest() }; }
            return e;
        }
        Expr NotTest()
        {
            if (IsKw("not")) { int l = Next().Line; Deeper(); try { return new UnaryE { Line = l, Op = "not", E = NotTest() }; } finally { nest--; } }
            return Comparison();
        }

        Expr Comparison()
        {
            var left = BitOr();
            CompareE c = null;
            while (true)
            {
                string op = null;
                if (Peek.Type == T.Op && (Peek.Text == "<" || Peek.Text == ">" || Peek.Text == "==" || Peek.Text == "!=" || Peek.Text == "<=" || Peek.Text == ">=")) op = Next().Text;
                else if (IsKw("in")) { p++; op = "in"; }
                else if (IsKw("not") && PeekAt(1).Type == T.Name && PeekAt(1).Text == "in") { p += 2; op = "not in"; }
                else if (IsKw("is")) { p++; if (IsKw("not")) { p++; op = "is not"; } else op = "is"; }
                if (op == null) break;
                if (c == null) c = new CompareE { Line = left.Line, L = left };
                c.Ops.Add(op); c.Rs.Add(BitOr());
            }
            return (Expr)c ?? left;
        }

        Expr BitOr()
        {
            var e = BitXor();
            while (IsOp("|")) { var op = Next(); e = new BinE { Line = op.Line, Op = "|", L = e, R = BitXor() }; }
            return e;
        }
        Expr BitXor()
        {
            var e = BitAnd();
            while (IsOp("^")) { var op = Next(); e = new BinE { Line = op.Line, Op = "^", L = e, R = BitAnd() }; }
            return e;
        }
        Expr BitAnd()
        {
            var e = Shift();
            while (IsOp("&")) { var op = Next(); e = new BinE { Line = op.Line, Op = "&", L = e, R = Shift() }; }
            return e;
        }
        Expr Shift()
        {
            var e = Arith();
            while (IsOp("<<") || IsOp(">>")) { var op = Next(); e = new BinE { Line = op.Line, Op = op.Text, L = e, R = Arith() }; }
            return e;
        }
        Expr Arith()
        {
            var e = Term();
            while (Peek.Type == T.Op && (Peek.Text == "+" || Peek.Text == "-"))
            { var op = Next(); e = new BinE { Line = op.Line, Op = op.Text, L = e, R = Term() }; }
            return e;
        }
        Expr Term()
        {
            var e = Factor();
            while (Peek.Type == T.Op && (Peek.Text == "*" || Peek.Text == "/" || Peek.Text == "//" || Peek.Text == "%" || Peek.Text == "@"))
            {
                var op = Next();
                if (op.Text == "@") throw new PyError("SyntaxError", "Оператор @ (умножение матриц) не поддерживается в игровом Python.", op.Line);
                e = new BinE { Line = op.Line, Op = op.Text, L = e, R = Factor() };
            }
            return e;
        }
        Expr Factor()
        {
            if (Peek.Type == T.Op && (Peek.Text == "-" || Peek.Text == "+" || Peek.Text == "~"))
            {
                var op = Next();
                Expr inner;
                Deeper();
                try { inner = Factor(); } finally { nest--; }
                if (op.Text == "-" && inner is ConstE && PyNum.IsNum(((ConstE)inner).V) && !(((ConstE)inner).V is bool))
                {
                    var v = ((ConstE)inner).V;
                    if (v is long && (long)v != long.MinValue) return new ConstE { Line = op.Line, V = Canon(-(long)v) };
                    if (v is double) return new ConstE { Line = op.Line, V = Canon(-(double)v) };
                    if (v is System.Numerics.BigInteger) return new ConstE { Line = op.Line, V = Canon(PyNum.Norm(-(System.Numerics.BigInteger)v)) };
                }
                return new UnaryE { Line = op.Line, Op = op.Text, E = inner };
            }
            return Power();
        }
        Expr Power()
        {
            if (IsKw("await")) throw NotYetError("await", Peek.Line);
            var e = Postfix();
            if (IsOp("**")) { var op = Next(); return new BinE { Line = op.Line, Op = "**", L = e, R = Factor() }; }
            return e;
        }

        Expr Postfix()
        {
            var e = Atom();
            while (true)
            {
                if (IsOp("("))
                {
                    var c = new CallE { Line = Next().Line, F = e };
                    while (!IsOp(")"))
                    {
                        if (IsOp("**")) { p++; c.Kw.Add(new KeyValuePair<string, Expr>(null, Test())); }
                        else if (IsOp("*")) { int l = Next().Line; c.Args.Add(new StarredE { Line = l, E = Test() }); }
                        else if (Peek.Type == T.Name && PeekAt(1).Type == T.Op && PeekAt(1).Text == "=")
                        {
                            string name = Next().Text; p++;
                            if (c.Kw.Any(k => k.Key == name)) throw new PyError("SyntaxError", "Именованный аргумент «" + name + "» передан дважды.", Peek.Line);
                            c.Kw.Add(new KeyValuePair<string, Expr>(name, Test()));
                        }
                        else
                        {
                            if (c.Kw.Count > 0) throw new PyError("SyntaxError", "Обычные аргументы должны идти до именованных (вида sep=\" \").", Peek.Line);
                            var a = Test();
                            if (IsKw("for"))
                            {
                                a = Comp("gen", a, null, a.Line);
                                if (c.Args.Count > 0 || !IsOp(")"))
                                    throw new PyError("SyntaxError", "Генераторное выражение внутри вызова с другими аргументами нужно взять в скобки: f((x for x in ...), 1).", Peek.Line);
                            }
                            c.Args.Add(a);
                        }
                        if (IsOp(",")) p++;
                        else if (!IsOp(")"))
                            throw new PyError("SyntaxError", "Между аргументами функции нужна запятая (или закрывающая скобка).", Peek.Line);
                    }
                    p++;
                    e = c;
                }
                else if (IsOp("["))
                {
                    int line = Next().Line;
                    Expr idx = SubscriptList();
                    ExpectOp("]", "Не хватает закрывающей квадратной скобки ].");
                    e = new IndexE { Line = line, Obj = e, Idx = idx };
                }
                else if (IsOp("."))
                {
                    int line = Next().Line;
                    if (Peek.Type != T.Name) throw new PyError("SyntaxError", "После точки должно идти имя метода или атрибута, например .upper()", Peek.Line);
                    e = new AttrE { Line = line, Obj = e, Name = Next().Text };
                }
                else return e;
            }
        }

        Expr SubscriptList()
        {
            int line = Peek.Line;
            var first = Subscript();
            if (!IsOp(",")) return first;
            var te = new TupleE { Line = line }; te.Items.Add(first);
            while (IsOp(",")) { p++; if (IsOp("]")) break; te.Items.Add(Subscript()); }
            return te;
        }

        Expr Subscript()
        {
            int line = Peek.Line;
            Expr lo = null, hi = null, st = null;
            if (!IsOp(":")) { lo = Test(); if (!IsOp(":")) return lo; }
            p++;
            if (!IsOp("]") && !IsOp(":") && !IsOp(",")) hi = Test();
            if (IsOp(":")) { p++; if (!IsOp("]") && !IsOp(",")) st = Test(); }
            return new SliceE { Line = line, Lo = lo, Hi = hi, Step = st };
        }

        CompE Comp(string kind, Expr elt, Expr val, int line)
        {
            var c = new CompE { Line = line, Kind = kind, Elt = elt, Val = val };
            while (IsKw("for") || IsKw("async"))
            {
                if (IsKw("async")) throw NotYetError("async", Peek.Line);
                p++;
                var f = new CompFor { Target = TargetList("После for в генераторе должно идти имя переменной.") };
                if (!IsKw("in")) throw new PyError("SyntaxError", "В генераторе нужно слово in: [x for x in коллекция].", Peek.Line);
                p++;
                f.Iter = OrTest();
                while (IsKw("if")) { p++; f.Ifs.Add(OrTestNoCond()); }
                c.Fors.Add(f);
            }
            return c;
        }
        Expr OrTestNoCond() { if (IsKw("lambda")) return Lambda(); return OrTest(); }

        Expr Atom()
        {
            var k = Peek;
            switch (k.Type)
            {
                case T.Num: p++; return new ConstE { Line = k.Line, V = Canon(k.Val) };
                case T.Str:
                case T.FStr:
                    {
                        // соседние строки склеиваются: "a" "b"
                        var toks = new List<Token>();
                        while (Peek.Type == T.Str || Peek.Type == T.FStr) toks.Add(Next());
                        if (toks.All(x => x.Type == T.Str))
                            return new ConstE { Line = k.Line, V = Canon(string.Concat(toks.Select(x => (string)x.Val))) };
                        var f = new FStrE { Line = k.Line };
                        foreach (var s in toks)
                        {
                            if (s.Type == T.Str) { if (((string)s.Val).Length > 0) AddLit(f, (string)s.Val); }
                            else
                            {
                                var sub = ParseFString(s.Text, s.Line);
                                for (int i = 0; i < sub.Parts.Count; i++)
                                {
                                    if (sub.Parts[i] is string) AddLit(f, (string)sub.Parts[i]);
                                    else { f.Parts.Add(sub.Parts[i]); f.Specs.Add(sub.Specs[i]); f.SpecText.Add(sub.SpecText[i]); f.Convs.Add(sub.Convs[i]); }
                                }
                            }
                        }
                        return f;
                    }
                case T.Name:
                    if (k.Text == "True") { p++; return new ConstE { Line = k.Line, V = true }; }
                    if (k.Text == "False") { p++; return new ConstE { Line = k.Line, V = false }; }
                    if (k.Text == "None") { p++; return new ConstE { Line = k.Line, V = null }; }
                    if (k.Text == "true" || k.Text == "false")
                        throw new PyError("SyntaxError", "В Python логические значения пишутся с большой буквы: True и False.", k.Line);
                    if (k.Text == "null" || k.Text == "nil")
                        throw new PyError("SyntaxError", "В Python «пустое значение» пишется как None.", k.Line);
                    if (Keywords.Contains(k.Text))
                    {
                        if (NotYet.Contains(k.Text)) throw NotYetError(k.Text, k.Line);
                        if (k.Text == "lambda") return Lambda();
                        throw new PyError("SyntaxError", "«" + k.Text + "» — ключевое слово Python, его нельзя использовать здесь (и нельзя называть так переменные).", k.Line);
                    }
                    p++; return new NameE { Line = k.Line, Id = k.Text };
                case T.Op:
                    if (k.Text == "...") { p++; return new ConstE { Line = k.Line, V = PyEllipsis.I }; }
                    if (k.Text == "(")
                    {
                        p++;
                        if (IsOp(")")) { p++; return new TupleE { Line = k.Line }; }
                        if (IsKw("yield")) throw NotYetError("yield", k.Line);
                        var e = StarOrTest();
                        if (IsKw("for"))
                        {
                            var g = Comp("gen", e, null, k.Line);
                            ExpectOp(")", "Не хватает закрывающей скобки ) в генераторном выражении.");
                            return g;
                        }
                        if (IsOp(",") || e is StarredE)
                        {
                            var te = new TupleE { Line = k.Line }; te.Items.Add(e);
                            while (IsOp(",")) { p++; if (IsOp(")")) break; te.Items.Add(StarOrTest()); }
                            e = te;
                        }
                        ExpectOp(")", "Не хватает закрывающей скобки ).");
                        return e;
                    }
                    if (k.Text == "[")
                    {
                        p++;
                        var l = new ListE { Line = k.Line };
                        if (IsOp("]")) { p++; return l; }
                        var first = StarOrTest();
                        if (IsKw("for"))
                        {
                            var lc = Comp("list", first, null, k.Line);
                            ExpectOp("]", "Не хватает закрывающей скобки ] в генераторе списка.");
                            return lc;
                        }
                        l.Items.Add(first);
                        while (IsOp(",")) { p++; if (IsOp("]")) break; l.Items.Add(StarOrTest()); }
                        ExpectOp("]", "Элементы списка разделяются запятыми, а список закрывается скобкой ].");
                        return l;
                    }
                    if (k.Text == "{")
                    {
                        p++;
                        if (IsOp("}")) { p++; return new DictE { Line = k.Line }; }
                        if (IsOp("**"))
                            return DictRest(new DictE { Line = k.Line });
                        var first = StarOrTest();
                        if (IsOp(":"))
                        {
                            p++;
                            var v = Test();
                            if (IsKw("for"))
                            {
                                var dc = Comp("dict", first, v, k.Line);
                                ExpectOp("}", "Не хватает закрывающей скобки } в генераторе словаря.");
                                return dc;
                            }
                            var d = new DictE { Line = k.Line };
                            d.Keys.Add(first); d.Vals.Add(v);
                            if (IsOp(",")) p++;
                            else if (!IsOp("}")) throw new PyError("SyntaxError", "Пары в словаре разделяются запятыми.", Peek.Line);
                            return DictRest(d);
                        }
                        if (IsKw("for"))
                        {
                            var sc = Comp("set", first, null, k.Line);
                            ExpectOp("}", "Не хватает закрывающей скобки } в генераторе множества.");
                            return sc;
                        }
                        var se = new SetE { Line = k.Line }; se.Items.Add(first);
                        while (IsOp(",")) { p++; if (IsOp("}")) break; se.Items.Add(StarOrTest()); }
                        ExpectOp("}", "Элементы множества разделяются запятыми, а закрывается оно скобкой }.");
                        return se;
                    }
                    break;
            }
            if (k.Type == T.Newline || k.Type == T.EOF)
                throw new PyError("SyntaxError", "Строка оборвалась: не хватает значения или выражения в конце.", k.Line);
            if (k.Type == T.Indent)
                throw new PyError("IndentationError", "Неожиданный отступ.", k.Line);
            throw new PyError("SyntaxError", "Не ожидал здесь " + Describe(k) + ".", k.Line);
        }

        Expr DictRest(DictE d)
        {
            while (!IsOp("}"))
            {
                if (IsOp("**")) { p++; d.Keys.Add(null); d.Vals.Add(BitOr()); }
                else
                {
                    d.Keys.Add(Test());
                    ExpectOp(":", "В словаре после ключа нужно двоеточие: {\"ключ\": значение}.");
                    d.Vals.Add(Test());
                }
                if (IsOp(",")) p++;
                else if (!IsOp("}")) throw new PyError("SyntaxError", "Пары в словаре разделяются запятыми.", Peek.Line);
            }
            p++;
            return d;
        }

        static void AddLit(FStrE f, string s)
        {
            int n = f.Parts.Count;
            if (n > 0 && f.Parts[n - 1] is string) { f.Parts[n - 1] = (string)f.Parts[n - 1] + s; return; }
            f.Parts.Add(s); f.Specs.Add(null); f.SpecText.Add(null); f.Convs.Add('\0');
        }

        public static FStrE ParseFString(string src, int line)
        {
            var f = new FStrE { Line = line };
            int i = 0;
            var lit = new StringBuilder();
            while (i < src.Length)
            {
                char c = src[i];
                if (c == '{' && i + 1 < src.Length && src[i + 1] == '{') { lit.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < src.Length && src[i + 1] == '}') { lit.Append('}'); i += 2; continue; }
                if (c == '}') throw new PyError("SyntaxError", "В f-строке лишняя «}». Чтобы вывести саму скобку, пиши }}.", line);
                if (c == '{')
                {
                    int depth = 0, j = i + 1, colon = -1, bang = -1; char quote = '\0';
                    for (; j < src.Length; j++)
                    {
                        char d = src[j];
                        if (quote != '\0') { if (d == quote) quote = '\0'; continue; }
                        if (colon >= 0)
                        {
                            if (d == '{') depth++;
                            else if (d == '}') { if (depth == 0) break; depth--; }
                            continue;
                        }
                        if (d == '"' || d == '\'') { quote = d; continue; }
                        if (d == '(' || d == '[' || d == '{') depth++;
                        else if (d == ')' || d == ']') depth--;
                        else if (d == '}') { if (depth == 0) break; depth--; }
                        else if (d == '!' && depth == 0 && j + 1 < src.Length && src[j + 1] != '=' && bang < 0) bang = j;
                        else if (d == ':' && depth == 0 && colon < 0) colon = j;
                    }
                    if (j >= src.Length) throw new PyError("SyntaxError", "В f-строке не закрыта фигурная скобка {.", line);
                    int exprEnd = bang >= 0 ? bang : colon >= 0 ? colon : j;
                    string exprSrc = src.Substring(i + 1, exprEnd - i - 1);
                    char conv = '\0';
                    if (bang >= 0)
                    {
                        string cv = src.Substring(bang + 1, (colon >= 0 ? colon : j) - bang - 1).Trim();
                        if (cv != "r" && cv != "s" && cv != "a") throw new PyError("SyntaxError", "В f-строке после ! может быть только r, s или a: {x!r}.", line);
                        conv = cv[0];
                    }
                    string spec = colon >= 0 ? src.Substring(colon + 1, j - colon - 1) : null;
                    if (exprSrc.Trim().Length == 0) throw new PyError("SyntaxError", "В f-строке пустые скобки {}. Внутри нужно имя переменной, например {name}.", line);
                    string trimmed = exprSrc.TrimEnd();
                    bool debug = false;
                    if (trimmed.EndsWith("=") && !trimmed.EndsWith("==") && !trimmed.EndsWith("!=") && !trimmed.EndsWith("<=") && !trimmed.EndsWith(">="))
                    {
                        debug = true;
                        lit.Append(exprSrc);
                        exprSrc = trimmed.Substring(0, trimmed.Length - 1);
                        if (conv == '\0' && spec == null) conv = 'r';
                    }
                    if (lit.Length > 0) { AddLit(f, lit.ToString()); lit.Length = 0; }
                    List<Token> sub;
                    try { sub = Lexer.Lex("(" + exprSrc.Trim() + ")"); }
                    catch (PyError e) { e.Line = line; throw; }
                    foreach (var tk in sub) tk.Line = line;
                    var ps = new Parser(sub);
                    Expr ex;
                    try { ex = ps.TestListStar(); }
                    catch (PyError e) { e.Line = line; throw; }
                    if (ps.Peek.Type != T.Newline && ps.Peek.Type != T.EOF)
                        throw new PyError("SyntaxError", "В f-строке внутри {} должно быть одно выражение.", line);
                    f.Parts.Add(ex);
                    f.Specs.Add(spec == null ? null : ParseFString(spec, line));
                    f.SpecText.Add(spec);
                    f.Convs.Add(conv);
                    i = j + 1; continue;
                }
                lit.Append(c); i++;
            }
            if (lit.Length > 0) AddLit(f, lit.ToString());
            return f;
        }
    }
}
