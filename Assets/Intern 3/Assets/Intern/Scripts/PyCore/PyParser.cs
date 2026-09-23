// Игровой Python: синтаксическое дерево и парсер.
using System;
using System.Collections.Generic;

namespace Intern.Py
{
    // ---------- Дерево ----------
    public abstract class Node { public int Line; }
    public abstract class Expr : Node { }
    public abstract class Stmt : Node { }

    public class ConstE : Expr { public object V; }
    public class FStrE : Expr { public List<object> Parts = new List<object>(); public List<string> Specs = new List<string>(); } // string или Expr
    public class NameE : Expr { public string Id; }
    public class BinE : Expr { public string Op; public Expr L, R; }
    public class UnaryE : Expr { public string Op; public Expr E; }
    public class BoolOpE : Expr { public string Op; public Expr L, R; }
    public class CompareE : Expr { public Expr L; public List<string> Ops = new List<string>(); public List<Expr> Rs = new List<Expr>(); }
    public class CallE : Expr { public Expr F; public List<Expr> Args = new List<Expr>(); public List<KeyValuePair<string, Expr>> Kw = new List<KeyValuePair<string, Expr>>(); }
    public class AttrE : Expr { public Expr Obj; public string Name; }
    public class IndexE : Expr { public Expr Obj; public Expr Idx; }
    public class SliceE : Expr { public Expr Lo, Hi, Step; }
    public class ListE : Expr { public List<Expr> Items = new List<Expr>(); }
    public class TupleE : Expr { public List<Expr> Items = new List<Expr>(); }
    public class DictE : Expr { public List<Expr> Keys = new List<Expr>(), Vals = new List<Expr>(); }
    public class IfExpE : Expr { public Expr Cond, A, B; }
    public class ListCompE : Expr { public Expr Elt, Target, Iter, Cond; }

    public class ExprS : Stmt { public Expr E; }
    public class AssignS : Stmt { public List<Expr> Targets = new List<Expr>(); public Expr Value; }
    public class AugAssignS : Stmt { public Expr Target; public string Op; public Expr Value; }
    public class IfS : Stmt { public Expr Cond; public List<Stmt> Body; public List<Stmt> Else; public bool IsElif; }
    public class WhileS : Stmt { public Expr Cond; public List<Stmt> Body; }
    public class ForS : Stmt { public Expr Target; public Expr Iter; public List<Stmt> Body; }
    public class DefS : Stmt { public string Name; public List<string> Params = new List<string>(); public List<Expr> Defaults = new List<Expr>(); public List<Stmt> Body; }
    public class ReturnS : Stmt { public Expr Value; }
    public class BreakS : Stmt { }
    public class ContinueS : Stmt { }
    public class PassS : Stmt { }

    // ---------- Парсер ----------
    public class Parser
    {
        List<Token> t; int p;

        static readonly HashSet<string> Keywords = new HashSet<string> {
            "if","elif","else","while","for","in","def","return","break","continue","pass",
            "and","or","not","True","False","None","is","lambda","import","from","class",
            "try","except","finally","with","as","global","nonlocal","yield","raise","del","assert" };
        static readonly HashSet<string> NotYet = new HashSet<string> {
            "lambda","import","from","class","try","except","finally","with","global","nonlocal","yield","raise","del","assert" };

        public Parser(List<Token> tokens) { t = tokens; }

        public static List<Stmt> ParseProgram(string src)
        {
            var ps = new Parser(Lexer.Lex(src));
            var list = new List<Stmt>();
            while (ps.Peek.Type != T.EOF)
            {
                if (ps.Peek.Type == T.Newline) { ps.p++; continue; }
                if (ps.Peek.Type == T.Indent)
                    throw new PyError("IndentationError", "Лишний отступ в начале строки. Отступ нужен только внутри блока после двоеточия (if, for, while, def).", ps.Peek.Line);
                list.Add(ps.Statement());
            }
            return list;
        }

        Token Peek { get { return t[p]; } }
        Token PeekAt(int k) { return t[Math.Min(p + k, t.Count - 1)]; }
        bool IsOp(string s) { return Peek.Type == T.Op && Peek.Text == s; }
        bool IsKw(string s) { return Peek.Type == T.Name && Peek.Text == s; }
        Token Next() { return t[p++]; }

        void ExpectOp(string s, string ruHint)
        {
            if (!IsOp(s)) throw new PyError("SyntaxError", ruHint, Peek.Line);
            p++;
        }

        string Describe(Token k)
        {
            switch (k.Type)
            {
                case T.Newline: return "конец строки";
                case T.EOF: return "конец программы";
                case T.Indent: return "отступ";
                case T.Dedent: return "конец блока";
                case T.Str: return "строку";
                case T.Num: return "число " + k.Text;
                default: return "«" + k.Text + "»";
            }
        }

        // ----- Инструкции -----
        Stmt Statement()
        {
            var k = Peek;
            if (k.Type == T.Name)
            {
                if (NotYet.Contains(k.Text))
                    throw new PyError("SyntaxError", "Конструкция «" + k.Text + "» пока не поддерживается в игровом Python — она откроется в следующих главах.", k.Line);
                switch (k.Text)
                {
                    case "if": return IfStmt(false);
                    case "while": return WhileStmt();
                    case "for": return ForStmt();
                    case "def": return DefStmt();
                    case "elif":
                    case "else":
                        throw new PyError("SyntaxError", "«" + k.Text + "» должен стоять сразу после блока if и на том же уровне отступа, что и if.", k.Line);
                }
            }
            var s = SimpleStmt();
            EndOfLine();
            return s;
        }

        void EndOfLine()
        {
            if (Peek.Type == T.Newline) { p++; return; }
            if (Peek.Type == T.EOF || Peek.Type == T.Dedent) return;
            if (IsOp(":")) throw new PyError("SyntaxError", "Неожиданное двоеточие. Двоеточие ставится только после if, elif, else, for, while и def.", Peek.Line);
            if (IsOp("=") ) throw new PyError("SyntaxError", "Здесь нельзя присваивать. Если хотел сравнить — используй == (два знака равно).", Peek.Line);
            throw new PyError("SyntaxError", "Не ожидал здесь " + Describe(Peek) + ". Возможно, пропущена запятая, оператор или скобка.", Peek.Line);
        }

        Stmt SimpleStmt()
        {
            int line = Peek.Line;
            if (IsKw("pass")) { p++; return new PassS { Line = line }; }
            if (IsKw("break")) { p++; return new BreakS { Line = line }; }
            if (IsKw("continue")) { p++; return new ContinueS { Line = line }; }
            if (IsKw("return"))
            {
                p++;
                Expr v = null;
                if (Peek.Type != T.Newline && Peek.Type != T.EOF && Peek.Type != T.Dedent) v = TestList();
                return new ReturnS { Line = line, Value = v };
            }
            if (Peek.Type == T.Name && Peek.Text == "print" && PeekAt(1).Type == T.Str)
                throw new PyError("SyntaxError", "print — это функция, ей нужны скобки: print(\"...\").", line);

            var e = TestList();
            if (IsOp("="))
            {
                var a = new AssignS { Line = line };
                a.Targets.Add(CheckTarget(e));
                while (IsOp("="))
                {
                    p++;
                    var v = TestList();
                    if (IsOp("=")) a.Targets.Add(CheckTarget(v)); else { a.Value = v; break; }
                }
                return a;
            }
            if (Peek.Type == T.Op && (Peek.Text == "+=" || Peek.Text == "-=" || Peek.Text == "*=" || Peek.Text == "/=" || Peek.Text == "//=" || Peek.Text == "%=" || Peek.Text == "**="))
            {
                string op = Next().Text; op = op.Substring(0, op.Length - 1);
                if (!(e is NameE || e is IndexE || e is AttrE)) throw new PyError("SyntaxError", "Слева от «" + op + "=» должна быть переменная.", line);
                return new AugAssignS { Line = line, Target = e, Op = op, Value = Test() };
            }
            return new ExprS { Line = line, E = e };
        }

        Expr CheckTarget(Expr e)
        {
            if (e is NameE)
            {
                var id = ((NameE)e).Id;
                return e;
            }
            if (e is IndexE || e is AttrE) return e;
            if (e is TupleE || e is ListE)
            {
                var items = e is TupleE ? ((TupleE)e).Items : ((ListE)e).Items;
                foreach (var it in items) CheckTarget(it);
                return e;
            }
            if (e is ConstE && ((ConstE)e).V is string)
                throw new PyError("SyntaxError", "Слева от = должно быть имя переменной без кавычек. Строку в кавычках нельзя «переназначить».", e.Line);
            if (e is ConstE)
                throw new PyError("SyntaxError", "Слева от = должно быть имя переменной, а не значение. Пиши так: x = 5, а не 5 = x.", e.Line);
            if (e is CallE)
                throw new PyError("SyntaxError", "Нельзя присвоить значение вызову функции. Если хотел сравнить — используй ==.", e.Line);
            throw new PyError("SyntaxError", "Слева от = должно быть имя переменной.", e.Line);
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
                // блок в одну строку: if x: print(x)
                body.Add(SimpleStmt()); EndOfLine();
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
                body.Add(Statement());
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
            var c = Test();
            if (IsOp("=")) throw new PyError("SyntaxError", "В условии для сравнения нужно ==, а одиночное = — это присваивание.", Peek.Line);
            return c;
        }

        Stmt WhileStmt()
        {
            int line = Next().Line;
            return new WhileS { Line = line, Cond = NamedCond(), Body = Block("while-условия") };
        }

        Stmt ForStmt()
        {
            int line = Next().Line;
            var target = TargetList();
            if (!IsKw("in")) throw new PyError("SyntaxError", "В цикле for нужно слово in: for элемент in коллекция:", Peek.Line);
            p++;
            var iter = TestList();
            return new ForS { Line = line, Target = target, Iter = iter, Body = Block("for") };
        }

        Expr TargetList()
        {
            int line = Peek.Line;
            var items = new List<Expr>();
            do
            {
                if (IsOp(",")) p++;
                if (Peek.Type != T.Name || Keywords.Contains(Peek.Text)) throw new PyError("SyntaxError", "После for должно идти имя переменной.", Peek.Line);
                items.Add(new NameE { Id = Next().Text, Line = line });
            } while (IsOp(","));
            if (items.Count == 1) return items[0];
            var te = new TupleE { Line = line }; te.Items.AddRange(items); return te;
        }

        Stmt DefStmt()
        {
            int line = Next().Line;
            if (Peek.Type != T.Name || Keywords.Contains(Peek.Text)) throw new PyError("SyntaxError", "После def должно идти имя функции.", Peek.Line);
            var d = new DefS { Line = line, Name = Next().Text };
            ExpectOp("(", "После имени функции нужны круглые скобки: def " + d.Name + "():");
            while (!IsOp(")"))
            {
                if (Peek.Type != T.Name) throw new PyError("SyntaxError", "Параметры функции — это имена через запятую.", Peek.Line);
                d.Params.Add(Next().Text);
                if (IsOp("=")) { p++; d.Defaults.Add(Test()); }
                else if (d.Defaults.Count > 0) throw new PyError("SyntaxError", "Параметры без значения по умолчанию должны идти до параметров со значением.", Peek.Line);
                if (IsOp(",")) p++; else if (!IsOp(")")) throw new PyError("SyntaxError", "Между параметрами нужна запятая.", Peek.Line);
            }
            p++;
            if (IsOp("->")) { p++; Test(); }
            d.Body = Block("объявления функции");
            return d;
        }

        // ----- Выражения -----
        Expr TestList()
        {
            int line = Peek.Line;
            var first = Test();
            if (!IsOp(",")) return first;
            var te = new TupleE { Line = line }; te.Items.Add(first);
            while (IsOp(","))
            {
                p++;
                if (Peek.Type == T.Newline || IsOp("=") || IsOp(")") || Peek.Type == T.EOF) break;
                te.Items.Add(Test());
            }
            return te;
        }

        Expr Test()
        {
            var e = OrTest();
            if (IsKw("if") )
            {
                int line = Peek.Line; p++;
                var cond = OrTest();
                if (!IsKw("else")) throw new PyError("SyntaxError", "В выражении «A if условие else B» не хватает else.", Peek.Line);
                p++;
                return new IfExpE { Line = line, Cond = cond, A = e, B = Test() };
            }
            return e;
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
            if (IsKw("not")) { int l = Next().Line; return new UnaryE { Line = l, Op = "not", E = NotTest() }; }
            return Comparison();
        }

        Expr Comparison()
        {
            var left = Arith();
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
                c.Ops.Add(op); c.Rs.Add(Arith());
            }
            return (Expr)c ?? left;
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
            while (Peek.Type == T.Op && (Peek.Text == "*" || Peek.Text == "/" || Peek.Text == "//" || Peek.Text == "%"))
            { var op = Next(); e = new BinE { Line = op.Line, Op = op.Text, L = e, R = Factor() }; }
            return e;
        }
        Expr Factor()
        {
            if (Peek.Type == T.Op && (Peek.Text == "-" || Peek.Text == "+"))
            { var op = Next(); return new UnaryE { Line = op.Line, Op = op.Text, E = Factor() }; }
            return Power();
        }
        Expr Power()
        {
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
                        if (Peek.Type == T.Name && PeekAt(1).Type == T.Op && PeekAt(1).Text == "=")
                        {
                            string name = Next().Text; p++;
                            c.Kw.Add(new KeyValuePair<string, Expr>(name, Test()));
                        }
                        else
                        {
                            if (c.Kw.Count > 0) throw new PyError("SyntaxError", "Обычные аргументы должны идти до именованных (вида sep=\" \").", Peek.Line);
                            c.Args.Add(Test());
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
                    Expr idx = Subscript();
                    ExpectOp("]", "Не хватает закрывающей квадратной скобки ].");
                    e = new IndexE { Line = line, Obj = e, Idx = idx };
                }
                else if (IsOp("."))
                {
                    int line = Next().Line;
                    if (Peek.Type != T.Name) throw new PyError("SyntaxError", "После точки должно идти имя метода, например .upper()", Peek.Line);
                    e = new AttrE { Line = line, Obj = e, Name = Next().Text };
                }
                else return e;
            }
        }

        Expr Subscript()
        {
            int line = Peek.Line;
            Expr lo = null, hi = null, st = null;
            if (!IsOp(":")) { lo = Test(); if (!IsOp(":")) return lo; }
            p++;
            if (!IsOp("]") && !IsOp(":")) hi = Test();
            if (IsOp(":")) { p++; if (!IsOp("]")) st = Test(); }
            return new SliceE { Line = line, Lo = lo, Hi = hi, Step = st };
        }

        Expr Atom()
        {
            var k = Peek;
            switch (k.Type)
            {
                case T.Num: p++; return new ConstE { Line = k.Line, V = k.Val };
                case T.Str:
                case T.FStr:
                    {
                        // соседние строки склеиваются: "a" "b"
                        Expr acc = null;
                        while (Peek.Type == T.Str || Peek.Type == T.FStr)
                        {
                            var s = Next();
                            Expr part = s.Type == T.Str ? (Expr)new ConstE { Line = s.Line, V = s.Val } : ParseFString(s);
                            acc = acc == null ? part : new BinE { Line = s.Line, Op = "+", L = acc, R = part };
                        }
                        return acc;
                    }
                case T.Name:
                    if (k.Text == "True") { p++; return new ConstE { Line = k.Line, V = true }; }
                    if (k.Text == "False") { p++; return new ConstE { Line = k.Line, V = false }; }
                    if (k.Text == "None") { p++; return new ConstE { Line = k.Line, V = null }; }
                    if (k.Text == "true" || k.Text == "false")
                        throw new PyError("SyntaxError", "В Python логические значения пишутся с большой буквы: True и False.", k.Line);
                    if (Keywords.Contains(k.Text))
                    {
                        if (NotYet.Contains(k.Text)) throw new PyError("SyntaxError", "«" + k.Text + "» пока не поддерживается в игровом Python.", k.Line);
                        throw new PyError("SyntaxError", "«" + k.Text + "» — ключевое слово Python, его нельзя использовать здесь (и нельзя называть так переменные).", k.Line);
                    }
                    p++; return new NameE { Line = k.Line, Id = k.Text };
                case T.Op:
                    if (k.Text == "(")
                    {
                        p++;
                        if (IsOp(")")) { p++; return new TupleE { Line = k.Line }; }
                        var e = Test();
                        if (IsOp(","))
                        {
                            var te = new TupleE { Line = k.Line }; te.Items.Add(e);
                            while (IsOp(",")) { p++; if (IsOp(")")) break; te.Items.Add(Test()); }
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
                        var first = Test();
                        if (IsKw("for"))
                        {
                            p++;
                            var lc = new ListCompE { Line = k.Line, Elt = first, Target = TargetList() };
                            if (!IsKw("in")) throw new PyError("SyntaxError", "В генераторе списка нужно слово in.", Peek.Line);
                            p++;
                            lc.Iter = OrTest();
                            if (IsKw("if")) { p++; lc.Cond = OrTest(); }
                            ExpectOp("]", "Не хватает закрывающей скобки ] в генераторе списка.");
                            return lc;
                        }
                        l.Items.Add(first);
                        while (IsOp(",")) { p++; if (IsOp("]")) break; l.Items.Add(Test()); }
                        ExpectOp("]", "Элементы списка разделяются запятыми, а список закрывается скобкой ].");
                        return l;
                    }
                    if (k.Text == "{")
                    {
                        p++;
                        var d = new DictE { Line = k.Line };
                        while (!IsOp("}"))
                        {
                            d.Keys.Add(Test());
                            ExpectOp(":", "В словаре после ключа нужно двоеточие: {\"ключ\": значение}.");
                            d.Vals.Add(Test());
                            if (IsOp(",")) p++;
                            else if (!IsOp("}")) throw new PyError("SyntaxError", "Пары в словаре разделяются запятыми.", Peek.Line);
                        }
                        p++;
                        return d;
                    }
                    break;
            }
            if (k.Type == T.Newline || k.Type == T.EOF)
                throw new PyError("SyntaxError", "Строка оборвалась: не хватает значения или выражения в конце.", k.Line);
            if (k.Type == T.Indent)
                throw new PyError("IndentationError", "Неожиданный отступ.", k.Line);
            throw new PyError("SyntaxError", "Не ожидал здесь " + Describe(k) + ".", k.Line);
        }

        FStrE ParseFString(Token s)
        {
            var f = new FStrE { Line = s.Line };
            string src = s.Text; int i = 0;
            var lit = new System.Text.StringBuilder();
            while (i < src.Length)
            {
                char c = src[i];
                if (c == '{' && i + 1 < src.Length && src[i + 1] == '{') { lit.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < src.Length && src[i + 1] == '}') { lit.Append('}'); i += 2; continue; }
                if (c == '}') throw new PyError("SyntaxError", "В f-строке лишняя «}». Чтобы вывести саму скобку, пиши }}.", s.Line);
                if (c == '{')
                {
                    if (lit.Length > 0) { f.Parts.Add(lit.ToString()); f.Specs.Add(null); lit.Length = 0; }
                    int depth = 0, j = i + 1, colon = -1; char quote = '\0';
                    for (; j < src.Length; j++)
                    {
                        char d = src[j];
                        if (quote != '\0') { if (d == quote) quote = '\0'; continue; }
                        if (d == '"' || d == '\'') { quote = d; continue; }
                        if (d == '(' || d == '[' || d == '{') depth++;
                        else if (d == ')' || d == ']') depth--;
                        else if (d == '}') { if (depth == 0) break; depth--; }
                        else if (d == ':' && depth == 0 && colon < 0) colon = j;
                    }
                    if (j >= src.Length) throw new PyError("SyntaxError", "В f-строке не закрыта фигурная скобка {.", s.Line);
                    string exprSrc = src.Substring(i + 1, (colon >= 0 ? colon : j) - i - 1);
                    string spec = colon >= 0 ? src.Substring(colon + 1, j - colon - 1) : null;
                    if (exprSrc.Trim().Length == 0) throw new PyError("SyntaxError", "В f-строке пустые скобки {}. Внутри нужно имя переменной, например {name}.", s.Line);
                    if (exprSrc.EndsWith("=")) exprSrc = exprSrc.Substring(0, exprSrc.Length - 1);
                    List<Token> sub;
                    try { sub = Lexer.Lex(exprSrc.Trim()); }
                    catch (PyError e) { e.Line = s.Line; throw; }
                    foreach (var tk in sub) tk.Line = s.Line;
                    var ps = new Parser(sub);
                    var ex = ps.Test();
                    f.Parts.Add(ex); f.Specs.Add(spec);
                    i = j + 1; continue;
                }
                lit.Append(c); i++;
            }
            if (lit.Length > 0) { f.Parts.Add(lit.ToString()); f.Specs.Add(null); }
            return f;
        }
    }
}
