#!/usr/bin/env python3
"""Проверка файлов контента «Стажёра».

python3 validate.py out/backend_b1.json [--fix]

Проверяет схему, роадмап, баланс сложности и XP, и ИСПОЛНЯЕТ код:
python (CPython + подмножество «игрового Python»), javascript (node), sql (sqlite),
статические проверки (regex) для yaml/dockerfile/bash/typescript/html/css/nginx,
синтаксис фрагментов (python, js, jsx/tsx/ts через esbuild, yaml, bash).
--fix пересчитывает xp_reward по формуле.
"""
import ast, json, math, os, re, sqlite3, subprocess, sys, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
TOOLS = "Tools/Content/tools/node_modules/.bin"
ROAD = json.load(open(os.path.join(HERE, "roadmap.json"), encoding="utf-8"))
TOPICS = {t["topic_id"]: (tr, t) for tr in ROAD for t in ROAD[tr]}
GRADES = ["intern", "junior", "junior_plus", "middle"]
TYPES = ["quiz", "find_bug", "write_code", "code_review", "architecture", "incident", "estimation"]
CHARS = ["manager", "teamlead", "qa", "client", "devops_colleague"]
LANGS = ["python", "javascript", "typescript", "yaml", "bash", "sql", "dockerfile", "html", "css", "jsx", "tsx", "nginx", "hcl", "promql", "text", None]
EXEC_LANGS = ["python", "javascript", "sql"]
STATIC_LANGS = ["yaml", "bash", "dockerfile", "typescript", "html", "css", "jsx", "tsx", "nginx", "hcl", "promql"]
XP_BASE = {1: 20, 2: 35, 3: 55, 4: 80, 5: 110}
XP_MULT = {"intern": 1.0, "junior": 1.0, "junior_plus": 1.25, "middle": 1.5}
DIFF_RANGE = {"intern": (1, 2), "junior": (1, 3), "junior_plus": (2, 4), "middle": (3, 5)}


def xp_for(d, g):
    return int(round(XP_BASE[d] * XP_MULT[g] / 5.0)) * 5


# ---------------- игровой Python: разрешённое подмножество ----------------
ALLOWED_NODES = set("""Module Expr Assign AugAssign AnnAssign If While For Break Continue Pass Return FunctionDef ClassDef
Try ExceptHandler Raise Assert Global Nonlocal Delete Import ImportFrom alias Lambda IfExp ListComp SetComp DictComp GeneratorExp
comprehension BoolOp And Or BinOp Add Sub Mult Div FloorDiv Mod Pow BitAnd BitOr BitXor UnaryOp Not USub UAdd Compare Eq NotEq
Lt LtE Gt GtE In NotIn Is IsNot Call keyword Attribute Subscript Slice Starred Name Load Store Del Constant JoinedStr
FormattedValue List Tuple Dict Set arguments arg""".split())
ALLOWED_BUILTINS = set("""print input len range int float str bool list dict set tuple sorted reversed min max sum abs round
enumerate zip map filter any all isinstance repr type hasattr getattr setattr divmod pow chr ord super object
Exception ValueError TypeError KeyError IndexError ZeroDivisionError AttributeError RuntimeError NotImplementedError
PermissionError LookupError ArithmeticError NameError StopIteration AssertionError True False None self cls __name__
staticmethod classmethod property""".split())
ALLOWED_METHODS = set("""upper lower strip lstrip rstrip split rsplit join replace startswith endswith find rfind index count
isdigit isalpha isalnum isspace isupper islower capitalize title format zfill ljust rjust center partition rpartition splitlines
append extend insert pop remove sort reverse clear copy get keys values items update setdefault popitem fromkeys
add discard union intersection difference symmetric_difference issubset issuperset isdisjoint
most_common sqrt floor ceil dumps loads
__init__ __str__ __repr__ __eq__ __lt__ __len__ __contains__ __getitem__ __setitem__ __iter__ __hash__""".split())
ALLOWED_IMPORTS = {"math": None, "json": None, "collections": {"Counter", "defaultdict"}}
ALLOWED_DECORATORS = {"staticmethod", "classmethod", "property"}


def py_subset_errors(src):
    errs = []
    try:
        tree = ast.parse(src)
    except SyntaxError as e:
        return ["синтаксис: %s (строка %s)" % (e.msg, e.lineno)]
    defined = set()
    user_methods = set()
    for n in ast.walk(tree):
        if isinstance(n, (ast.FunctionDef, ast.ClassDef)):
            defined.add(n.name)
        if isinstance(n, ast.ClassDef):
            for b in n.body:
                if isinstance(b, ast.FunctionDef):
                    user_methods.add(b.name)
        if isinstance(n, ast.arg):
            defined.add(n.arg)
        if isinstance(n, ast.Name) and isinstance(n.ctx, ast.Store):
            defined.add(n.id)
        if isinstance(n, ast.ExceptHandler) and n.name:
            defined.add(n.name)
        if isinstance(n, (ast.Import, ast.ImportFrom)):
            for a in n.names:
                defined.add(a.asname or a.name)
        if isinstance(n, (ast.Global, ast.Nonlocal)):
            defined.update(n.names)
    for n in ast.walk(tree):
        name = type(n).__name__
        if name not in ALLOWED_NODES:
            errs.append("конструкция %s не поддерживается игровым Python" % name)
            continue
        if isinstance(n, ast.Import):
            for a in n.names:
                if a.name not in ALLOWED_IMPORTS:
                    errs.append("import %s запрещён (можно math, json, collections)" % a.name)
        if isinstance(n, ast.ImportFrom):
            allowed = ALLOWED_IMPORTS.get(n.module, False)
            if allowed is False:
                errs.append("from %s import запрещён" % n.module)
            elif allowed is not None:
                for a in n.names:
                    if a.name not in allowed:
                        errs.append("from %s import %s запрещён" % (n.module, a.name))
        if isinstance(n, (ast.FunctionDef, ast.ClassDef)):
            for d in n.decorator_list:
                dn = d.id if isinstance(d, ast.Name) else (d.attr if isinstance(d, ast.Attribute) else None)
                if dn is None or (dn not in ALLOWED_DECORATORS and dn not in defined and not dn.endswith("setter")):
                    errs.append("декоратор %s не поддерживается" % ast.unparse(d))
        if isinstance(n, ast.Name) and isinstance(n.ctx, ast.Load):
            if n.id not in defined and n.id not in ALLOWED_BUILTINS and n.id not in ("math", "json"):
                errs.append("имя %s не определено и не входит в разрешённые встроенные" % n.id)
        if isinstance(n, ast.Call) and isinstance(n.func, ast.Attribute):
            m = n.func.attr
            if m not in ALLOWED_METHODS and m not in user_methods and m not in defined:
                errs.append("метод .%s() не поддерживается игровым Python" % m)
        if isinstance(n, ast.BinOp) and isinstance(n.op, ast.Mod) and isinstance(n.left, ast.Constant) and isinstance(n.left.value, str):
            errs.append("%-форматирование строк не поддерживается — используй f-строки")
    return sorted(set(errs))


# ---------------- исполнение ----------------
PY_FUNC_RUNNER = r'''
import json, sys, math
data = json.loads(sys.stdin.read())
ns = {"__name__": "__main__"}
try:
    exec(compile(data["code"], "<task>", "exec"), ns)
except Exception as e:
    print(json.dumps({"error": "при загрузке: %s: %s" % (type(e).__name__, e)})); sys.exit(0)
fn = ns.get(data["entry"])
if fn is None:
    print(json.dumps({"error": "нет функции " + data["entry"]})); sys.exit(0)
def norm(v):
    if isinstance(v, (tuple, list)): return [norm(x) for x in v]
    if isinstance(v, dict): return {str(k): norm(x) for k, x in v.items()}
    if isinstance(v, set): return sorted(norm(x) for x in v)
    return v
out = []
for t in data["tests"]:
    try:
        r = fn(*t["input"]) if isinstance(t["input"], list) else fn(t["input"])
        out.append({"got": norm(r)})
    except Exception as e:
        out.append({"exc": "%s: %s" % (type(e).__name__, e)})
print(json.dumps({"results": out}, default=str))
'''

JS_RUNNER = r'''
const fs = require("fs");
const data = JSON.parse(fs.readFileSync(0, "utf8"));
(async () => {
  let fn;
  try { fn = new Function(data.code + "\n;return (typeof " + data.entry + " !== 'undefined') ? " + data.entry + " : undefined;")(); }
  catch (e) { console.log(JSON.stringify({ error: "при загрузке: " + e })); return; }
  if (typeof fn !== "function") { console.log(JSON.stringify({ error: "нет функции " + data.entry })); return; }
  const out = [];
  for (const t of data.tests) {
    try {
      let r = Array.isArray(t.input) ? fn(...t.input) : fn(t.input);
      if (r && typeof r.then === "function") r = await r;
      out.push({ got: r === undefined ? null : r });
    } catch (e) { out.push({ exc: String(e) }); }
  }
  console.log(JSON.stringify({ results: out }));
})();
'''


def eq(a, b):
    if isinstance(a, float) or isinstance(b, float):
        try:
            return math.isclose(float(a), float(b), rel_tol=1e-6, abs_tol=1e-6)
        except (TypeError, ValueError):
            return False
    if isinstance(a, bool) != isinstance(b, bool):
        return False
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(eq(x, y) for x, y in zip(a, b))
    if isinstance(a, dict) and isinstance(b, dict):
        return set(a) == set(b) and all(eq(a[k], b[k]) for k in a)
    return a == b


def run_py_func(code, entry, tests):
    r = subprocess.run([sys.executable, "-c", PY_FUNC_RUNNER], input=json.dumps({"code": code, "entry": entry, "tests": tests}),
                       capture_output=True, text=True, timeout=10)
    try:
        return json.loads(r.stdout.strip().splitlines()[-1])
    except Exception:
        return {"error": "runner: " + (r.stderr or r.stdout)[-400:]}


def run_py_stdio(code, stdin):
    with tempfile.NamedTemporaryFile("w", suffix=".py", delete=False, encoding="utf-8") as f:
        f.write(code); p = f.name
    try:
        r = subprocess.run([sys.executable, p], input=stdin or "", capture_output=True, text=True, timeout=10)
        return r.stdout, (r.stderr.strip().splitlines() or [""])[-1] if r.returncode else None
    except subprocess.TimeoutExpired:
        return "", "timeout"
    finally:
        os.unlink(p)


def norm_out(s):
    return "\n".join(l.rstrip() for l in (s or "").replace("\r", "").strip("\n").split("\n")).strip()


def run_js_func(code, entry, tests):
    r = subprocess.run(["node", "-e", JS_RUNNER], input=json.dumps({"code": code, "entry": entry, "tests": tests}),
                       capture_output=True, text=True, timeout=15)
    try:
        return json.loads(r.stdout.strip().splitlines()[-1])
    except Exception:
        return {"error": "runner: " + (r.stderr or r.stdout)[-400:]}


def sql_split(script):
    stmts, cur = [], ""
    for line in script.replace("\r", "").split("\n"):
        cur += line + "\n"
        if sqlite3.complete_statement(cur):
            if cur.strip():
                stmts.append(cur)
            cur = ""
    if cur.strip() and not all(l.strip().startswith("--") or not l.strip() for l in cur.split("\n")):
        stmts.append(cur)
    return stmts


def run_sql(script):
    con = sqlite3.connect(":memory:")
    rows = None
    try:
        for st in sql_split(script):
            c = con.execute(st)
            if c.description is not None:
                rows = [list(r) for r in c.fetchall()]
        return rows, None
    except Exception as e:
        return None, str(e)
    finally:
        con.close()


def regex_flags(f):
    fl = 0
    for ch in (f or ""):
        fl |= {"i": re.I, "m": re.M, "s": re.S}.get(ch, 0)
    return fl


def static_check(text, checks):
    fails = []
    for c in checks:
        e = c["expected"]
        if "regex" in e:
            if not re.search(e["regex"], text or "", regex_flags(e.get("flags"))):
                fails.append(c["input"])
        elif "not_regex" in e:
            if re.search(e["not_regex"], text or "", regex_flags(e.get("flags"))):
                fails.append(c["input"])
    return fails


def syntax_error(lang, code):
    if not code:
        return None
    try:
        if lang == "python":
            ast.parse(code); return None
        if lang == "yaml":
            import yaml
            list(yaml.safe_load_all(code)); return None
        if lang in ("javascript", "jsx", "typescript", "tsx"):
            loader = {"javascript": "js", "jsx": "jsx", "typescript": "ts", "tsx": "tsx"}[lang]
            r = subprocess.run([os.path.join(TOOLS, "esbuild"), "--loader=" + loader, "--log-level=error"], input=code,
                               capture_output=True, text=True, timeout=20)
            return None if r.returncode == 0 else r.stderr.strip()[:300]
        if lang == "bash":
            r = subprocess.run(["bash", "-n"], input=code, capture_output=True, text=True, timeout=10)
            return None if r.returncode == 0 else r.stderr.strip()[:300]
    except Exception as e:
        return str(e)[:300]
    return None


# ---------------- проверка задачи ----------------
def check_task(t, track, errs, warns, fix):
    tid = t.get("task_id", "?")
    E = lambda m: errs.append("%s: %s" % (tid, m))
    W = lambda m: warns.append("%s: %s" % (tid, m))
    need = ["task_id", "topic_id", "grade", "type", "difficulty", "xp_reward", "time_limit_minutes", "character", "story", "content", "explanation", "hints"]
    for k in need:
        if k not in t:
            E("нет поля " + k)
    if errs and errs[-1].startswith(tid + ": нет поля"):
        return
    top = TOPICS.get(t["topic_id"])
    if not top:
        E("неизвестная тема " + t["topic_id"]); return
    if top[0] != track:
        E("тема %s из направления %s" % (t["topic_id"], top[0]))
    grade = top[1]["grade"]
    if t["grade"] != grade:
        E("grade %s, а у темы %s" % (t["grade"], grade))
    if not re.fullmatch(re.escape(t["topic_id"]) + r"-\d\d", tid):
        E("task_id должен быть <topic_id>-NN")
    if t["type"] not in TYPES:
        E("тип " + str(t["type"]))
    d = t["difficulty"]
    if not isinstance(d, int) or not 1 <= d <= 5:
        E("difficulty 1..5"); d = 1
    lo, hi = DIFF_RANGE[grade]
    if not lo <= d <= hi:
        E("difficulty %d вне диапазона %s для %s" % (d, (lo, hi), grade))
    want = xp_for(d, grade)
    if t["xp_reward"] != want:
        if fix:
            t["xp_reward"] = want
        else:
            E("xp_reward %s, по формуле %s" % (t["xp_reward"], want))
    tl = t["time_limit_minutes"]
    if t["type"] == "incident" and not (isinstance(tl, int) and 5 <= tl <= 45):
        E("у incident нужен time_limit_minutes 5..45")
    if tl is not None and not (isinstance(tl, int) and 3 <= tl <= 90):
        E("time_limit_minutes: null или 3..90")
    if t["character"] not in CHARS:
        E("character " + str(t["character"]))
    if not isinstance(t["story"], str) or len(t["story"]) < 60:
        E("story слишком короткая")
    if not isinstance(t["explanation"], str) or len(t["explanation"]) < 120:
        E("explanation слишком короткое (нужно объяснить, почему ответ верный)")
    h = t["hints"]
    if not isinstance(h, list) or not 2 <= len(h) <= 4 or not all(isinstance(x, str) and x for x in h):
        E("hints: 2..4 строки")
    c = t["content"]
    for k in ["question", "code", "language", "options", "correct_answer", "test_cases"]:
        if k not in c:
            E("content без " + k)
    if any(e.startswith(tid + ": content без") for e in errs):
        return
    lang = c["language"]
    if lang not in LANGS:
        E("language " + str(lang))
    if not isinstance(c["question"], str) or len(c["question"]) < 20:
        E("question слишком короткий")
    if c["code"] is not None and not isinstance(c["code"], str):
        E("code: строка или null")
    typ = t["type"]
    opts, ans, tests = c["options"], c["correct_answer"], c["test_cases"]

    def check_choice(multi_ok=True, need_multi=False, nmin=3, nmax=8):
        if not isinstance(opts, list) or not nmin <= len(opts) <= nmax or not all(isinstance(o, str) and o.strip() for o in opts):
            E("options: %d..%d строк" % (nmin, nmax)); return
        if len(set(opts)) != len(opts):
            E("варианты повторяются")
        if isinstance(ans, bool) or not isinstance(ans, (int, list)):
            E("correct_answer: индекс или массив индексов"); return
        idx = ans if isinstance(ans, list) else [ans]
        if isinstance(ans, list) and not multi_ok:
            E("здесь correct_answer — один индекс")
        if need_multi and not isinstance(ans, list):
            E("здесь correct_answer — массив индексов")
        if not idx or any(not isinstance(i, int) or isinstance(i, bool) or not 0 <= i < len(opts) for i in idx) or len(set(idx)) != len(idx):
            E("correct_answer вне диапазона options")
        if isinstance(ans, list) and len(idx) == len(opts):
            E("все варианты верные — так нельзя")
        if tests is not None:
            E("у задач с выбором test_cases = null")

    def check_exec(buggy_must_fail):
        if not isinstance(ans, str) or not ans.strip():
            E("correct_answer — полный исправленный/эталонный код"); return
        stdio_noinput = lang == "python" and not c.get("entry") and isinstance(tests, list) and tests and all(not tc.get("input") for tc in tests)
        if not isinstance(tests, list) or len(tests) < (1 if stdio_noinput or lang == "sql" else 2):
            E("нужно минимум 2 test_cases"); return
        if opts is not None:
            E("options = null для задач с кодом")
        entry = c.get("entry")
        starter = c["code"] or ""
        if lang == "python":
            for src, nm in ((ans, "эталон"), (starter, "заготовка")):
                if src.strip():
                    for pe in py_subset_errors(src):
                        (E if nm == "эталон" or typ == "find_bug" else W)("%s: %s" % (nm, pe))
            if entry:
                res = run_py_func(ans, entry, tests)
                if "error" in res:
                    E("эталон: " + res["error"]); return
                for i, (tc, r) in enumerate(zip(tests, res["results"])):
                    if "exc" in r or not eq(r.get("got"), tc["expected"]):
                        E("эталон не проходит тест %d: ждали %r, получили %r" % (i + 1, tc["expected"], r.get("got", r.get("exc"))))
                if starter.strip():
                    rb = run_py_func(starter, entry, tests)
                    ok = "results" in rb and all("exc" not in r and eq(r.get("got"), tc["expected"]) for tc, r in zip(tests, rb["results"]))
                    if ok:
                        (E if buggy_must_fail else W)("исходный код уже проходит все тесты")
            else:
                for i, tc in enumerate(tests):
                    if not isinstance(tc.get("input"), str) or not isinstance(tc.get("expected"), str):
                        E("stdio-тест %d: input и expected — строки" % (i + 1)); return
                    out, err = run_py_stdio(ans, tc["input"])
                    if err or norm_out(out) != norm_out(tc["expected"]):
                        E("эталон не проходит тест %d: ждали %r, получили %r %s" % (i + 1, tc["expected"], out, err or ""))
                if starter.strip():
                    allok = True
                    for tc in tests:
                        out, err = run_py_stdio(starter, tc["input"])
                        if err or norm_out(out) != norm_out(tc["expected"]):
                            allok = False
                    if allok:
                        (E if buggy_must_fail else W)("исходный код уже проходит все тесты")
        elif lang == "javascript":
            if not entry:
                E("для javascript нужен content.entry (имя функции)"); return
            res = run_js_func(ans, entry, tests)
            if "error" in res:
                E("эталон: " + res["error"]); return
            for i, (tc, r) in enumerate(zip(tests, res["results"])):
                if "exc" in r or not eq(r.get("got"), tc["expected"]):
                    E("эталон не проходит тест %d: ждали %r, получили %r" % (i + 1, tc["expected"], r.get("got", r.get("exc"))))
            if starter.strip():
                rb = run_js_func(starter, entry, tests)
                ok = "results" in rb and all("exc" not in r and eq(r.get("got"), tc["expected"]) for tc, r in zip(tests, rb["results"]))
                if ok:
                    (E if buggy_must_fail else W)("исходный код уже проходит все тесты")
        elif lang == "sql":
            rows, err = run_sql(ans)
            if err:
                E("эталон SQL: " + err); return
            for i, tc in enumerate(tests):
                if not eq(rows, tc["expected"]):
                    E("эталон SQL тест %d: ждали %r, получили %r" % (i + 1, tc["expected"], rows))
            if starter.strip():
                rb, eb = run_sql(starter)
                if not eb and all(eq(rb, tc["expected"]) for tc in tests):
                    (E if buggy_must_fail else W)("исходный SQL уже даёт верный результат")
        elif lang in STATIC_LANGS:
            for i, tc in enumerate(tests):
                e = tc.get("expected")
                if not isinstance(tc.get("input"), str) or not isinstance(e, dict) or not ({"regex", "not_regex"} & set(e)):
                    E("статическая проверка %d: input — описание, expected — {regex|not_regex, flags}" % (i + 1)); return
                try:
                    re.compile(e.get("regex") or e.get("not_regex"), regex_flags(e.get("flags")))
                except re.error as ex:
                    E("regex %d: %s" % (i + 1, ex)); return
                if "(?P<" in (e.get("regex") or e.get("not_regex") or ""):
                    E("именованные группы (?P<..>) не переносимы — убери")
            if len(tests) < 3:
                E("для статической проверки нужно минимум 3 проверки")
            fails = static_check(ans, tests)
            if fails:
                E("эталон не проходит проверки: %s" % fails)
            if starter.strip() and not static_check(starter, tests):
                (E if buggy_must_fail else W)("заготовка уже проходит все проверки")
            se = syntax_error(lang, ans)
            if se:
                E("эталон: синтаксис: " + se)
            if lang == "bash":
                r = subprocess.run(["shellcheck", "-s", "bash", "-S", "warning", "-"], input=ans, capture_output=True, text=True)
                if r.returncode:
                    W("shellcheck: " + " | ".join(l for l in r.stdout.splitlines() if "SC" in l)[:300])
        else:
            E("язык %s нельзя проверить исполнением — используй вариант с options" % lang)

    if typ in ("quiz",):
        check_choice(nmin=3, nmax=6)
    elif typ == "architecture":
        check_choice(multi_ok=False, nmin=3, nmax=5)
    elif typ == "incident":
        check_choice(nmin=3, nmax=7)
        if not c["code"]:
            E("incident: в code нужны логи/метрики/вывод команд")
    elif typ == "estimation":
        check_choice(need_multi=True, nmin=5, nmax=9)
    elif typ == "code_review":
        check_choice(need_multi=True, nmin=4, nmax=8)
        if not c["code"]:
            E("code_review: нужен код на ревью")
    elif typ == "find_bug":
        if not c["code"]:
            E("find_bug: нужен код с ошибкой")
        if opts is None:
            check_exec(True)
        else:
            check_choice(multi_ok=False, nmin=3, nmax=6)
    elif typ == "write_code":
        check_exec(False)
    # синтаксис фрагментов кода (для задач с выбором)
    if c["code"] and lang in ("python", "javascript", "jsx", "typescript", "tsx", "yaml", "bash") and opts is not None:
        se = syntax_error(lang, c["code"])
        if se:
            (W if typ == "find_bug" else E)("code: синтаксис %s: %s" % (lang, se))


def validate(path, fix=False):
    data = json.load(open(path, encoding="utf-8"))
    errs, warns = [], []
    track = data.get("track")
    if track not in ROAD:
        return ["track: " + str(track)], []
    topics_here = {t["topic_id"] for t in data.get("topics", [])}
    for tp in data.get("topics", []):
        if tp["topic_id"] not in TOPICS or TOPICS[tp["topic_id"]][0] != track:
            errs.append("тема %s не из роадмапа %s" % (tp["topic_id"], track))
        th = tp.get("theory", "")
        if not isinstance(th, str) or len(th) < 600:
            errs.append("%s: theory короче 600 символов" % tp["topic_id"])
    ids = set()
    per_topic = {}
    for t in data.get("tasks", []):
        if t.get("task_id") in ids:
            errs.append("повтор task_id " + str(t.get("task_id")))
        ids.add(t.get("task_id"))
        per_topic.setdefault(t.get("topic_id"), []).append(t)
        try:
            check_task(t, track, errs, warns, fix)
        except Exception as e:
            errs.append("%s: сбой проверки: %r" % (t.get("task_id"), e))
    for tp in topics_here:
        lst = per_topic.get(tp, [])
        legacy = sum(1 for t in lst if t.get("legacy"))
        if not 3 <= len(lst) - legacy + min(legacy, 5) <= 5 + legacy:
            errs.append("%s: задач %d (нужно 3..5)" % (tp, len(lst)))
        if len({t["type"] for t in lst}) < 2:
            errs.append("%s: нужно хотя бы 2 разных типа задач" % tp)
        diffs = [t["difficulty"] for t in sorted(lst, key=lambda x: x["task_id"])]
        if diffs and diffs != sorted(diffs):
            warns.append("%s: сложность внутри темы не растёт по порядку: %s" % (tp, diffs))
    for tp in per_topic:
        if tp not in topics_here:
            errs.append("задачи для темы %s, но её нет в topics" % tp)
    # --- ответ не должен угадываться по форме, персонажи — разнообразны ---
    tasks = data.get("tasks", [])
    single = [t for t in tasks if isinstance(t.get("content", {}).get("options"), list)
              and isinstance(t["content"].get("correct_answer"), int) and not isinstance(t["content"]["correct_answer"], bool)]
    if len(single) >= 5:
        longest = 0
        pos = {}
        for t in single:
            o = t["content"]["options"]; a = t["content"]["correct_answer"]
            if 0 <= a < len(o):
                L = [len(x) for x in o]
                if L[a] == max(L) and L.count(max(L)) == 1 and L[a] > 1.15 * sorted(L)[-2]:
                    longest += 1
                pos[a] = pos.get(a, 0) + 1
        if longest > 0.3 * len(single):
            errs.append("правильный вариант заметно длиннее остальных в %d из %d задач с одним ответом (можно не больше 30%%)" % (longest, len(single)))
        if len(single) >= 6 and max(pos.values()) > 0.45 * len(single):
            errs.append("правильный ответ слишком часто на одной позиции: %s" % pos)
    ratios = []
    for t in tasks:
        c = t.get("content", {}); a = c.get("correct_answer"); o = c.get("options")
        if isinstance(a, list) and isinstance(o, list) and 0 < len(a) < len(o):
            good = [len(o[i]) for i in a if isinstance(i, int) and 0 <= i < len(o)]
            bad = [len(x) for i, x in enumerate(o) if i not in a]
            if good and bad:
                ratios.append((sum(good) / len(good)) / max(1, sum(bad) / len(bad)))
    if len(ratios) >= 3 and sum(ratios) / len(ratios) > 1.3:
        errs.append("в задачах с несколькими ответами верные варианты в среднем в %.2f раза длиннее неверных (нужно ≤ 1.3)" % (sum(ratios) / len(ratios)))
    if tasks:
        tl = sum(1 for t in tasks if t.get("character") == "teamlead")
        if tl > 0.35 * len(tasks):
            errs.append("teamlead пишет %d из %d задач (не больше 35%%) — раздай сюжеты Стасу, Ире, Диме, клиентам" % (tl, len(tasks)))
    if fix:
        json.dump(data, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    return errs, warns


if __name__ == "__main__":
    fix = "--fix" in sys.argv
    total_e = 0
    for p in [a for a in sys.argv[1:] if not a.startswith("--")]:
        e, w = validate(p, fix)
        total_e += len(e)
        print("== %s: ошибок %d, предупреждений %d" % (p, len(e), len(w)))
        for x in e:
            print("  ОШИБКА  " + x)
        for x in w:
            print("  внимание " + x)
    sys.exit(1 if total_e else 0)
