// IDE, спринт 7 «Окружение»: терминал с настоящим bash в песочнице Docker и задачи-сценарии по шагам.
// Вывод терминала живёт в GameRoot (EnvTerm) — он общий для всех задач; здесь строка ввода, шаги, проверки,
// «Что произошло» и уборка. Файл сценария (например, site/index.html) открывается в редакторе прямо из папки work.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Intern.Game
{
    public partial class IdeScreen
    {
        static readonly Color EnvColor = new Color(0.306f, 0.788f, 0.690f);   // #4EC9B0

        // терминал
        bool termFocus;
        string termInput = ""; int termCur, histPos = -1; string histDraft = "";
        Label termOut, termIn;
        int termShownVersion = -1;
        bool termBlink = true, lastShellBusy; float termBlinkAt;
        // сценарий
        string envFilePath; bool envFileMissing;
        bool envChecking, envPreparing, envViewBusy;
        string envNote, envPassedExplain, envView;
        int cmdSeq, stepStartSeq;
        ShellRecord envLastRec; List<string> envExplain = new List<string>();
        float envProbeAt, dockerWaitUntil, cleanupConfirmUntil;
        int cleanupConfirm;                     // 1 — «Убрать за собой», 2 — «Удалить всё»: ждём второго нажатия
        EnvState seenState = (EnvState)(-1); bool seenUp;
        readonly HashSet<string> envHints = new HashSet<string>();    // «id:шаг» — подсказка открыта
        readonly HashSet<string> envSolves = new HashSet<string>();   // «id:шаг» — команда показана
        readonly Dictionary<string, int> envFails = new Dictionary<string, int>();

        EnvScenario Sc { get { return Task != null ? Task.scenario : null; } }
        int EnvStepNo { get { return Sc != null ? g.EnvStepOf(Task) : 0; } }
        bool EnvFinished { get { return Sc != null && EnvStepNo >= Sc.steps.Count; } }
        EnvStep CurStep { get { int i = EnvStepNo; return Sc != null && i < Sc.steps.Count ? Sc.steps[i] : null; } }
        bool EnvFileReadOnly { get { return envFilePath == null || envFileMissing; } }

        string EnvStatusMode()
        {
            var sh = g.Env;
            if (envChecking) return "Проверка шага…";
            if (sh.Busy) return "Терминал: выполняется…";
            if (sh.Probing) return "Проверяю Docker…";
            return sh.CanRunCommands ? "Песочница работает" : "";
        }

        // ======================= файл сценария в редакторе =======================
        void EnvLoadEditor(TaskData t)
        {
            var s = t.scenario;
            envFilePath = s != null && !string.IsNullOrEmpty(s.file) ? DockerGuard.MapPath("/work/" + s.file, "/work", DevEnv.WorkDir) : null;
            envFileMissing = false;
            if (envFilePath != null)
            {
                string text = ReadWorkFile(envFilePath);
                if (text != null) { ed.Language = t.language; ed.Text = text; return; }
                envFileMissing = true;
            }
            ed.Language = "text";
            ed.Text = envFileMissing ? "# Файл " + s.file + " появится, когда задача будет подготовлена в песочнице.\n\n" + t.starter : t.starter;
        }

        static string ReadWorkFile(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Replace("\r\n", "\n") : null; }
            catch (Exception e) { Debug.LogWarning("[Стажёр] Не прочитать " + path + ": " + e.Message); return null; }
        }

        // Команда в терминале могла поменять файл — показать новую версию (если в редакторе нет несохранённых правок)
        void EnvReloadFile()
        {
            if (Task == null || envFilePath == null) return;
            if (!EnvFileReadOnly && Code != savedCode) return;
            string text = ReadWorkFile(envFilePath);
            if (text == null || (!envFileMissing && text == Code)) return;
            ed.Language = Task.language; ed.Text = text; savedCode = text; envFileMissing = false;
            SetText(statusLang, "{ } " + LangName(ed.Language));
            RefreshEditorFlags(); RefreshStatus();
        }

        void EnvSaveFile()
        {
            if (EnvFileReadOnly || Code == savedCode) return;
            try { File.WriteAllText(envFilePath, Code, new UTF8Encoding(false)); savedCode = Code; }
            catch (Exception e) { Notice("Не удалось сохранить " + FileName(Task) + ": " + e.Message, "error", K.Red); }
        }

        // ======================= жизненный цикл =======================
        void EnvOpened()
        {
            termFocus = true; termInput = ""; termCur = 0; histPos = -1;
            envNote = null; envPassedExplain = null; envView = null; envChecking = false; cleanupConfirm = 0;
            stepStartSeq = cmdSeq; termShownVersion = -1;
            seenState = (EnvState)(-1);
            if (!g.EnvOpen(Task)) return;
            if (g.Env.State == EnvState.Unknown && !g.Env.Probing) g.Env.Probe();
            envProbeAt = Time.unscaledTime + 15f;
        }

        void EnvUpdate()
        {
            var sh = g.Env; float now = Time.unscaledTime;
            if (bottom == Bottom.Terminal && termOut != null && termShownVersion != g.EnvTermVersion) TermUpdateOut();
            if (sh.Busy != lastShellBusy) { lastShellBusy = sh.Busy; TermUpdateIn(); RefreshStatus(); RefreshEditorFlags(); if (side == Side.Task) RefreshSide(); }
            if (termFocus && Active && now > termBlinkAt) { termBlink = !termBlink; termBlinkAt = now + 0.53f; TermUpdateIn(); }
            if (cleanupConfirm != 0 && now > cleanupConfirmUntil) { cleanupConfirm = 0; RefreshRight(); }
            if (!g.EnvOpen(Task)) return;
            // Docker запустился, песочница собрана или упала — перерисовать и сделать следующий шаг сам
            if (!sh.Probing && (sh.State != seenState || sh.ContainerUp != seenUp)) { seenState = sh.State; seenUp = sh.ContainerUp; EnvStateChanged(); }
            // пока песочница не готова — спрашиваем Docker раз в 15 с (после «Запустить Docker Desktop» — раз в 4 с)
            if (!sh.CanRunCommands && !sh.Probing && !sh.Busy && now > envProbeAt) { envProbeAt = now + (now < dockerWaitUntil ? 4f : 15f); sh.Probe(); }
        }

        void EnvStateChanged()
        {
            var sh = g.Env;
            RefreshStatus(); RefreshRight(); if (side == Side.Task) RefreshSide(); TermUpdateIn();
            if (sh.Busy || envPreparing) return;
            if (sh.State == EnvState.Ready && !sh.ContainerUp && EnvMayAutostart) { sh.StartContainer(null); return; }
            if (sh.CanRunCommands) EnvPrepareNow();
            else { EnvRefreshView(); EnvCheckStep(false); }   // шаги миссии про сам Docker проверяются и без песочницы
        }

        // Песочницу можно поднять самим: это обычный сценарий или миссия уже прошла шаг «Запусти песочницу»
        bool EnvMayAutostart
        {
            get
            {
                if (Sc == null) return false;
                if (!Sc.mission) return true;
                int start = Sc.steps.FindIndex(x => x.action == "start");
                return start < 0 || EnvStepNo > start;
            }
        }

        bool EnvBuildAllowed
        {
            get
            {
                if (Sc == null) return false;
                if (!Sc.mission) return true;
                int b = Sc.steps.FindIndex(x => x.action == "build");
                return b < 0 || EnvStepNo >= b;
            }
        }

        void EnvPrepareNow()
        {
            if (envPreparing) return;
            envPreparing = true;
            var t = Task;
            g.EnvPrepare(t, ok =>
            {
                envPreparing = false;
                if (t != Task) return;
                if (ok) EnvReloadFile();
                EnvRefreshView();
                EnvCheckStep(false);
                RefreshSide(); RefreshRight();
            });
        }

        // ======================= проверка шага =======================
        void EnvCheckStep(bool manual)
        {
            var t = Task; var st = CurStep; var sh = g.Env;
            if (t == null || Sc == null) return;
            if (!g.EnvOpen(t)) { if (manual) Notice("Сначала пройди миссию «Настрой окружение».", "lock", K.Muted); return; }
            if (st == null) { if (manual) Notice("Все шаги уже пройдены. «Заново» наверху — пройти ещё раз.", "check", K.Green); return; }
            if (envChecking) return;
            var c = st.check;
            bool needsCmd = c.kind == "last" || c.kind == "ips";
            if (needsCmd && cmdSeq <= stepStartSeq) { if (manual) EnvFail("Сначала выполни команду этого шага в терминале внизу.", false); return; }
            bool needsSandbox = c.kind == "sandbox" || c.kind == "file" || c.kind == "ips";
            if (needsSandbox && !sh.CanRunCommands)
            {
                if (manual) EnvFail(sh.State == EnvState.Ready ? "Песочница остановлена — запусти её справа («Запустить песочницу»)." : sh.StateText, false);
                return;
            }
            if (!manual && c.kind == "host" && sh.State == EnvState.Unknown) return;   // дождёмся проверки Docker
            envChecking = true;
            if (manual) envNote = null;
            RefreshEditorFlags(); RefreshStatus(); if (side == Side.Task) RefreshSide();
            sh.Check(c, (ok, note) =>
            {
                envChecking = false;
                bool same = t == Task && CurStep == st;
                if (same)
                {
                    if (ok) EnvStepPassed(st);
                    else if (manual) EnvFail(note ?? "Пока не выполнено.", true);
                    else if (c.kind == "ips" && note != null && !note.StartsWith("В выводе")) { envNote = note; RefreshSide(); }   // IP в выводе есть, но не те — подскажем сразу
                }
                RefreshEditorFlags(); RefreshStatus(); if (side == Side.Task && !ok) RefreshSide();
            });
        }

        void EnvFail(string note, bool counted)
        {
            envNote = note;
            if (counted) { string k = Task.id + ":" + EnvStepNo; int f; envFails.TryGetValue(k, out f); envFails[k] = f + 1; }
            var st = CurStep;
            Notice((st != null ? "Шаг «" + st.title + "»: " : "") + note, "error", K.Orange);
            if (side == Side.Task) RefreshSide();
        }

        void EnvStepPassed(EnvStep st)
        {
            int no = EnvStepNo, total = Sc.steps.Count;
            bool wasDone = g.IsDone(Task);
            envPassedExplain = st.explain; envNote = null;
            g.EnvAdvance(Task, envHints.Any(h => h.StartsWith(Task.id + ":")), Spent);
            stepStartSeq = cmdSeq;
            if (no + 1 >= total)
                Notice(wasDone ? "Сценарий пройден ещё раз — хорошая тренировка!" : Sc.mission ? "Миссия выполнена: окружение настроено!" : "Сценарий пройден!", "check", K.Green);
            else Notice("Шаг " + (no + 1) + " из " + total + " выполнен: " + st.title, "check", K.Green);
            RefreshAll();
            if (no + 1 < total) EnvCheckStep(false);   // следующий шаг, может быть, уже сделан (Docker стоит, файл есть)
        }

        // Команда в терминале закончилась (зовёт GameRoot, в главном потоке)
        public void OnShellFinished(ShellRecord rec)
        {
            cmdSeq++;
            envLastRec = rec; envExplain = ShellExplain.Explain(rec.cmd);
            string o = rec.output ?? "";
            if (rec.code != 0 && !rec.cmd.TrimStart().StartsWith("docker") && (o.Contains("No such container") || o.Contains("is not running")))
            { g.Env.ContainerUp = false; g.Env.Probe(); }   // песочницу остановили снаружи — поднимем заново
            if (!IsScenario) return;
            EnvReloadFile();
            EnvRefreshView();
            TermUpdateIn();
            RefreshRight(); RefreshStatus();
            if (g.EnvOpen(Task) && !EnvFinished) EnvCheckStep(false);
        }

        // ======================= действия шагов =======================
        static string ActionLabel(string a)
        {
            switch (a)
            {
                case "docker-site": return "Открыть docker.com";
                case "docker-start": return "Запустить Docker Desktop";
                case "build": return "Собрать песочницу";
                case "start": return "Запустить песочницу";
                default: return null;
            }
        }
        static string ActionIcon(string a) { return a == "docker-site" ? "browser" : a == "build" ? "stack" : "play"; }

        void EnvAction(string action)
        {
            var sh = g.Env;
            switch (action)
            {
                case "docker-site":
                    Application.OpenURL("https://www.docker.com/products/docker-desktop/");
                    Notice("Открыл сайт Docker в браузере. Установи Docker Desktop и возвращайся — шаг засчитается сам или по «Проверить».", "browser", K.Blue);
                    dockerWaitUntil = Time.unscaledTime + 600f;
                    break;
                case "docker-start":
                    if (Shell.StartDockerDesktop())
                    {
                        dockerWaitUntil = Time.unscaledTime + 180f; envProbeAt = Time.unscaledTime + 4f;
                        g.EnvEcho("<color=#9D9D9D>Запускаю Docker Desktop… Первый запуск — минута-две.</color>\n");
                        Notice("Запускаю Docker Desktop. Как только движок загрузится, шаг засчитается сам.", "play", K.Green);
                    }
                    else Notice("Не нашёл Docker Desktop в Program Files. Установи его (шаг 1) или запусти вручную из меню «Пуск».", "warning", K.Orange);
                    break;
                case "build":
                    if (sh.Busy) return;
                    bottom = Bottom.Terminal;
                    sh.BuildImage(DevEnv.SandboxDockerfile(), null);   // в конце сборки Shell сам перепроверит Docker
                    RefreshBottom();
                    break;
                case "start":
                    if (sh.Busy) return;
                    bottom = Bottom.Terminal;
                    sh.StartContainer(null);
                    RefreshBottom();
                    break;
            }
            RefreshSide(); RefreshStatus();
        }

        void EnvResetClicked()
        {
            if (!confirmReset) { confirmReset = true; confirmResetUntil = Time.unscaledTime + 3f; RefreshEditorFlags(); return; }
            confirmReset = false;
            if (!g.EnvOpen(Task)) { RefreshEditorFlags(); return; }
            if (Sc.mission && g.IsDone(Task)) { Notice("Миссия уже пройдена — окружение настроено. Для тренировки открой задачи раздела «Окружение».", "md"); RefreshEditorFlags(); return; }
            string pre = Task.id + ":";
            g.EnvReset(Task);
            envHints.RemoveWhere(h => h.StartsWith(pre)); envSolves.RemoveWhere(h => h.StartsWith(pre));
            foreach (var k in envFails.Keys.Where(k => k.StartsWith(pre)).ToList()) envFails.Remove(k);
            envNote = null; envPassedExplain = null; stepStartSeq = cmdSeq;
            savedCode = Code;   // правки файла пропадут: подготовка задачи создаст его заново
            Notice("Задача начата заново" + (string.IsNullOrEmpty(Sc.setup) ? "." : ": готовлю файлы в песочнице."), "reset", K.Muted);
            if (g.Env.CanRunCommands) EnvPrepareNow(); else EnvCheckStep(false);
            RefreshAll();
        }

        void EnvCleanupClick(int mode)
        {
            if (cleanupConfirm != mode) { cleanupConfirm = mode; cleanupConfirmUntil = Time.unscaledTime + 3f; RefreshRight(); return; }
            cleanupConfirm = 0;
            bottom = Bottom.Terminal;
            g.EnvCleanup(mode == 2, res => { Notice("Уборка: " + res.Replace("\n", "; "), "check", K.Green); envView = null; EnvRefreshView(); RefreshAll(); });
            RefreshAll();
        }

        void EnvOpenFolder()
        {
            try
            {
                Directory.CreateDirectory(DevEnv.WorkDir);
                Application.OpenURL(new Uri(DevEnv.WorkDir).AbsoluteUri);
            }
            catch (Exception e) { Notice("Не открыть папку: " + e.Message, "error", K.Red); }
        }

        // «Что произошло»: граф коммитов или контейнеры игры — после каждой команды
        void EnvRefreshView()
        {
            var s = Sc; var sh = g.Env; var t = Task;
            if (s == null || string.IsNullOrEmpty(s.view) || envViewBusy) return;
            if (s.view.StartsWith("git:"))
            {
                if (!sh.CanRunCommands) return;
                string dir = s.view.Substring(4);
                envViewBusy = true;
                sh.Async(() => DevEnv.Exec("cd " + EnvCheck.Quote(dir) + " 2>/dev/null && git log --graph --oneline --decorate --all -n 14 2>/dev/null || echo '(репозитория ещё нет)'", 20000),
                    r => { envViewBusy = false; if (t != Task) return; envView = r.Ok ? r.Out.TrimEnd() : "(не удалось: " + r.Text + ")"; RefreshRight(); });
            }
            else if (s.view == "docker")
            {
                if (sh.State == EnvState.Unknown || sh.State == EnvState.NoDocker || sh.State == EnvState.DockerStopped) return;
                envViewBusy = true;
                sh.Async(() => DevEnv.DockerCmd("ps -a --filter label=" + DevEnv.Label + " --format \"{{.Names}} · {{.Image}} · {{.Status}} · {{.Ports}}\"", 15000),
                    r =>
                    {
                        envViewBusy = false; if (t != Task) return;
                        envView = !r.Ok ? "(Docker не ответил)" : r.Out.Trim().Length == 0 ? "(контейнеров нет)" : string.Join("\n", r.Out.Trim().Split('\n').Select(x => x.Trim().TrimEnd('·', ' ')).ToArray());
                        RefreshRight();
                    });
            }
        }

        // ======================= терминал =======================
        string PromptMarkup() { return "<color=#89D185>intern@stazher</color><color=#CCCCCC>:</color><color=#3B8EEA>" + K.Esc(g.Env.Cwd) + "</color><color=#CCCCCC>$</color> "; }

        void TermBuild(VisualElement c)
        {
            termOut = K.T("", 15f, K.Text, true, false, true); termOut.style.whiteSpace = WhiteSpace.PreWrap; c.Add(termOut);
            termIn = K.T("", 15f, K.Text, true, false, true); termIn.style.whiteSpace = WhiteSpace.PreWrap; c.Add(termIn);
            SetTermOut();
            termIn.text = TermInputMarkup();
            bottomScroll.ToBottom();
        }

        void SetTermOut()
        {
            string s = g.EnvTerm.ToString().TrimEnd('\n');
            termOut.text = s;
            termOut.style.display = s.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            termShownVersion = g.EnvTermVersion;
        }

        void TermUpdateOut()
        {
            if (termOut == null || termOut.panel == null) return;
            SetTermOut();
            termIn.text = TermInputMarkup();
            bottomScroll.ToBottom();
        }

        void TermUpdateIn()
        {
            if (termIn == null || termIn.panel == null || !IsScenario) return;
            termIn.text = TermInputMarkup();
        }

        string TermInputMarkup()
        {
            var sh = g.Env;
            if (sh.Busy) return "<color=#6E7681>выполняется…" + (termFocus ? "  Ctrl+C — прервать" : "") + "</color>";
            var sb = new StringBuilder(PromptMarkup());
            string a = termInput.Substring(0, termCur), b = termInput.Substring(termCur);
            sb.Append(K.Esc(a));
            if (termFocus && Active)
            {
                bool nl = b.Length == 0 || b[0] == '\n';
                string ch = nl ? " " : b.Substring(0, 1);
                if (termBlink) sb.Append("<mark=#AEAFAD99>").Append(K.Esc(ch)).Append("</mark>"); else sb.Append(K.Esc(ch));
                sb.Append(K.Esc(nl ? b : b.Substring(1)));
            }
            else
            {
                sb.Append(K.Esc(b));
                if (termInput.Length == 0) sb.Append("<color=#6E7681>кликни сюда и набери команду</color>");
            }
            return sb.ToString();
        }

        void EnvFocusByClick(VisualElement v)
        {
            bool was = termFocus;
            if (bottom == Bottom.Terminal && Inside(v, bottomScroll)) termFocus = true;
            else if (InEditor(v)) termFocus = false;
            if (was != termFocus) { ed.Active = Active && !termFocus; ed.Place(); TermTouched(); }
        }
        static bool Inside(VisualElement v, VisualElement parent) { for (; v != null; v = v.parent) if (v == parent) return true; return false; }

        void TermSet(string s) { termInput = s ?? ""; termCur = termInput.Length; TermTouched(); }
        void TermInsert(string s) { termInput = termInput.Insert(termCur, s); termCur += s.Length; TermTouched(); }
        void TermTouched() { termBlink = true; termBlinkAt = Time.unscaledTime + 0.53f; TermUpdateIn(); bottomScroll.ToBottom(); }

        // Клавиши в терминале (как в bash). false — отдать хоткеям IDE (Ctrl+Enter, Ctrl+S, Ctrl+B, F-клавиши)
        bool TermKey(Event e)
        {
            bool ctrl = e.control || e.command;
            var sh = g.Env;
            if (e.keyCode == KeyCode.Escape) { escClosedAt = Time.frameCount; termFocus = false; ed.Active = Active; ed.Place(); TermUpdateIn(); return true; }
            if (ctrl && !e.alt)
            {
                switch (e.keyCode)
                {
                    case KeyCode.C:
                        if (sh.Busy) sh.Interrupt();
                        else { g.EnvEcho(PromptMarkup() + K.Esc(termInput) + "<color=#9D9D9D>^C</color>\n"); histPos = -1; TermSet(""); }
                        return true;
                    case KeyCode.L: g.EnvClear(); return true;
                    case KeyCode.V: TermInsert((GUIUtility.systemCopyBuffer ?? "").Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd('\n')); return true;
                    case KeyCode.U: termInput = termInput.Substring(termCur); termCur = 0; TermTouched(); return true;
                    case KeyCode.K: termInput = termInput.Substring(0, termCur); TermTouched(); return true;
                    case KeyCode.A: termCur = 0; TermTouched(); return true;
                    case KeyCode.E: termCur = termInput.Length; TermTouched(); return true;
                    case KeyCode.W:
                        {
                            int i = termCur;
                            while (i > 0 && termInput[i - 1] == ' ') i--;
                            while (i > 0 && termInput[i - 1] != ' ') i--;
                            termInput = termInput.Remove(i, termCur - i); termCur = i; TermTouched(); return true;
                        }
                    case KeyCode.LeftArrow: { int i = termCur; while (i > 0 && termInput[i - 1] == ' ') i--; while (i > 0 && termInput[i - 1] != ' ') i--; termCur = i; TermTouched(); return true; }
                    case KeyCode.RightArrow: { int i = termCur; while (i < termInput.Length && termInput[i] == ' ') i++; while (i < termInput.Length && termInput[i] != ' ') i++; termCur = i; TermTouched(); return true; }
                }
                return false;
            }
            switch (e.keyCode)
            {
                case KeyCode.Return: case KeyCode.KeypadEnter: TermSubmit(); return true;
                case KeyCode.Backspace: if (termCur > 0) { termInput = termInput.Remove(termCur - 1, 1); termCur--; } TermTouched(); return true;
                case KeyCode.Delete: if (termCur < termInput.Length) termInput = termInput.Remove(termCur, 1); TermTouched(); return true;
                case KeyCode.LeftArrow: termCur = Mathf.Max(0, termCur - 1); TermTouched(); return true;
                case KeyCode.RightArrow: termCur = Mathf.Min(termInput.Length, termCur + 1); TermTouched(); return true;
                case KeyCode.Home: termCur = 0; TermTouched(); return true;
                case KeyCode.End: termCur = termInput.Length; TermTouched(); return true;
                case KeyCode.UpArrow: TermHistory(-1); return true;
                case KeyCode.DownArrow: TermHistory(1); return true;
                case KeyCode.PageUp: bottomScroll.Wheel(-240f); return true;
                case KeyCode.PageDown: bottomScroll.Wheel(240f); return true;
                case KeyCode.Tab: TermComplete(); return true;
            }
            if (e.keyCode >= KeyCode.F1 && e.keyCode <= KeyCode.F15) return false;
            char ch = e.character;
            if (ch == '\t' || ch == '\n' || ch == '\r') return true;   // приходят вторым событием — уже обработаны
            if (ch >= ' ' && (!ctrl || e.alt)) { TermInsert(ch.ToString()); return true; }
            return e.keyCode == KeyCode.None ? false : true;
        }

        void TermHistory(int dir)
        {
            var h = g.Env.History;
            if (h.Count == 0) return;
            if (histPos < 0) { if (dir > 0) return; histDraft = termInput; histPos = h.Count; }
            histPos += dir;
            if (histPos >= h.Count) { histPos = -1; TermSet(histDraft); return; }
            histPos = Mathf.Max(0, histPos);
            TermSet(h[histPos]);
        }

        void TermSubmit()
        {
            var sh = g.Env;
            if (sh.Busy) { Notice("Команда ещё выполняется. Ctrl+C — прервать.", "clock", K.Muted); return; }
            string line = termInput;
            g.EnvEcho(PromptMarkup() + K.Esc(line) + "\n");
            histPos = -1; TermSet("");
            string cmd = line.Trim();
            if (cmd.Length == 0) return;
            if (TermBuiltin(cmd)) { if (sh.History.Count == 0 || sh.History[sh.History.Count - 1] != cmd) sh.History.Add(cmd); return; }
            SaveCode();   // правки файла в редакторе — на диск до команды: терминал видит ту же папку
            g.ReportWork(WorkKind.Terminal);
            sh.Submit(line);
            TermUpdateIn();
        }

        // Команды самого терминала IDE
        bool TermBuiltin(string cmd)
        {
            switch (cmd)
            {
                case "clear": g.EnvClear(); return true;
                case "help":
                    g.EnvEcho("<color=#9D9D9D>Это bash в песочнице (Ubuntu в Docker). Папка /work = Документы\\Стажёр\\work на твоём ПК, её же видит IDE.\n" +
                              "  Enter — выполнить, ↑/↓ — история, Tab — дополнить имя файла, Ctrl+C — прервать, Ctrl+L или clear — очистить.\n" +
                              "  docker … — идёт в Docker на твоём ПК через фильтр игры: только безопасные команды, порты — на 127.0.0.1.\n" +
                              "  Интерактивные программы (vim, nano, top, less) здесь не открываются: файлы правь в IDE.</color>\n");
                    return true;
                case "history":
                    {
                        var h = g.Env.History; var sb = new StringBuilder();
                        for (int i = Math.Max(0, h.Count - 30); i < h.Count; i++) sb.Append((i + 1).ToString().PadLeft(5)).Append("  ").Append(K.Esc(h[i])).Append('\n');
                        g.EnvEcho(sb.ToString());
                        return true;
                    }
                case "exit": case "logout":
                    g.EnvEcho("<color=#9D9D9D>Терминал закрывать не нужно: выйти из-за компьютера — Esc (сначала кликни в редактор).</color>\n");
                    return true;
            }
            return false;
        }

        // Tab: дополнить имя файла или команды (compgen в песочнице)
        void TermComplete()
        {
            var sh = g.Env;
            if (!sh.CanRunCommands || sh.Busy) return;
            string before = termInput.Substring(0, termCur);
            int ws = before.LastIndexOf(' ') + 1;
            string word = before.Substring(ws);
            if (word.IndexOfAny(new[] { '$', '`', '"', '\'' }) >= 0) return;
            bool first = before.Substring(0, ws).Trim().Length == 0;
            string snapshot = termInput; int cur = termCur;
            string script = "cd " + EnvCheck.Quote(sh.Cwd) + " 2>/dev/null; compgen " + (first && !word.Contains("/") ? "-c -f" : "-f") + " -- " + EnvCheck.Quote(word) +
                            " 2>/dev/null | sort -u | head -60 | while IFS= read -r f; do if [ -d \"$f\" ]; then echo \"$f/\"; else echo \"$f\"; fi; done";
            sh.Async(() => DevEnv.Exec(script, 8000), r =>
            {
                if (termInput != snapshot || termCur != cur || Task == null || !IsScenario) return;
                var opts = r.Out.Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
                if (opts.Count == 0) return;
                string common = opts[0];
                foreach (var o in opts) { int k = 0; while (k < common.Length && k < o.Length && common[k] == o[k]) k++; common = common.Substring(0, k); }
                if (common.Length > word.Length) TermInsert(common.Substring(word.Length) + (opts.Count == 1 && !common.EndsWith("/") ? " " : ""));
                else if (opts.Count > 1) g.EnvEcho(PromptMarkup() + K.Esc(termInput) + "\n" + K.Esc(string.Join("   ", opts.ToArray())) + "\n");
            });
        }

        // ======================= боковая панель: шаги =======================
        static Label Badge(string text, Color col)
        {
            var b = K.T(text, 11f, K.EditorBg, false, true); K.Pad(b, 2f, 7f, 2f, 7f); b.style.backgroundColor = col; K.Radius(b, 3f); b.style.marginLeft = 6f; return b;
        }

        void SideScenario(VisualElement c)
        {
            var t = Task; var s = Sc;
            int step = EnvStepNo, n = s.steps.Count;
            bool fin = step >= n, done = g.IsDone(t);
            var head = K.Box(true); head.style.alignItems = Align.Center; head.style.marginTop = 4f;
            var code = K.T(TaskCode(t), 12f, Color.white, false, true); K.Pad(code, 2f, 8f, 2f, 8f); code.style.backgroundColor = K.Accent; K.Radius(code, 3f); head.Add(code);
            head.Add(Badge("ОКРУЖЕНИЕ", EnvColor));
            if (s.mission) head.Add(Badge("МИССИЯ", K.Brand));
            c.Add(head);
            Para(c, "<b>" + K.Esc(t.title) + "</b>", 21f, K.TextHi, 10f);
            var meta = K.Box(true); meta.style.alignItems = Align.Center; meta.style.marginTop = 6f; meta.style.flexWrap = Wrap.Wrap;
            var stars = K.Box(true); stars.style.alignItems = Align.Center;
            for (int i = 1; i <= 5; i++) stars.Add(new Icon("star", i <= t.difficulty ? K.Sun : Pal.Hex("3C3C3C"), 13f));
            meta.Add(stars);
            var xp = K.T("+" + t.xp + " XP", 13f, K.Green, false, true); xp.style.marginLeft = 10f; meta.Add(xp);
            if (done) { var di = new Icon("check", K.Green, 14f); di.style.marginLeft = 10f; meta.Add(di); var dn = K.T("пройдено", 13f, K.Green, false, true); dn.style.marginLeft = 3f; meta.Add(dn); }
            c.Add(meta);
            // отправитель
            var who = K.Box(true); who.style.alignItems = Align.Center; who.style.marginTop = 16f;
            var av = K.Box(); av.style.width = 30f; av.style.height = 30f; K.Radius(av, 15f); av.style.backgroundColor = CharacterColor(t.character); av.style.justifyContent = Justify.Center; av.style.alignItems = Align.Center;
            string init = string.IsNullOrEmpty(t.sender) ? "?" : t.sender.Split(' ').Last().Substring(0, 1).ToUpper();
            av.Add(K.T(init, 14f, Color.white, false, true)); who.Add(av);
            var wl = K.T("<b>" + K.Esc(t.sender) + "</b>  <color=#9D9D9D>пишет</color>", 14f, K.Text, false, false, true); wl.style.marginLeft = 10f; wl.style.flexShrink = 1f; who.Add(wl);
            c.Add(who);
            var story = Para(c, K.Esc(t.story), 15f, K.Text, 8f);
            story.style.backgroundColor = Pal.Hex("202020"); K.Pad(story, 10f, 12f, 10f, 12f); K.Radius(story, 6f); K.Line(story, Pal.Hex("2B2B2B"), 1f, 1f, 1f, 1f);
            if (!g.EnvOpen(t)) { Card(c, "Сначала пройди миссию «Настрой окружение»: без песочницы терминалу негде выполнять команды.", K.Orange); return; }
            // шаги
            Section(c, "ШАГИ  " + Mathf.Min(step, n) + " / " + n);
            for (int i = 0; i < n; i++) StepRow(c, i, step);
            if (fin)
            {
                Section(c, "ГОТОВО");
                Card(c, s.mission ? "Окружение настроено. Терминал твой: пробуй любые команды — песочница всё выдержит. Задачи по bash, Git и Docker — в проводнике, раздел «Окружение»."
                                  : "Сценарий пройден. Пройти ещё раз с нуля — «Заново» наверху (задача подготовится заново, награды второй раз не будет).", K.Green);
                var nx = g.EnvTasks.FirstOrDefault(x => x != t && g.EnvOpen(x) && !g.IsDone(x));
                var main = g.CurrentTaskPublic;
                var target = nx ?? main;
                if (target != null)
                {
                    var nb = Btn.Text((nx != null ? "Дальше: " : "К задачам: ") + TaskCode(target) + " " + K.Esc(target.title), () => Open(target), K.Button, K.ButtonHover, 15f, Color.white, "continue", Color.white);
                    nb.style.height = 36f; nb.style.marginTop = 10f; nb.style.justifyContent = Justify.Center; c.Add(nb);
                }
            }
            // теория
            if (Diff == Difficulty.Hard) { Section(c, "ТЕОРИЯ"); Para(c, "Теория скрыта на тяжёлой сложности. man и --help в терминале — как на настоящей работе.", 14f, K.Muted); }
            else
            {
                var th = new Btn(() => { theoryOpen = !theoryOpen; RefreshSide(); }); th.style.marginTop = 16f; th.style.height = 26f; K.Radius(th, 3f);
                th.Add(new Icon(theoryOpen ? "chevD" : "chevR", K.Muted, 16f)); var tl = K.T("ТЕОРИЯ · ОКРУЖЕНИЕ", 12f, K.Muted, false, true); tl.style.marginLeft = 4f; th.Add(tl); c.Add(th);
                if (theoryOpen) TheoryText(c, t.theory);
            }
        }

        void StepRow(VisualElement c, int i, int cur)
        {
            var st = Sc.steps[i]; bool past = i < cur, now = i == cur;
            var row = K.Box(true); row.style.alignItems = Align.Center; row.style.marginTop = now ? 12f : 6f;
            row.Add(new Icon(past ? "check" : now ? "continue" : "ring", past ? K.Green : now ? K.Sun : K.Dim, 16f));
            var l = K.T((i + 1) + ". " + K.Esc(st.title), now ? 16f : 14f, past ? K.Muted : now ? K.TextHi : K.Dim, false, now); l.style.marginLeft = 8f; l.style.flexShrink = 1f; row.Add(l);
            c.Add(row);
            if (past && i == cur - 1 && !string.IsNullOrEmpty(st.explain) && envPassedExplain == st.explain) { var ex = Para(c, K.Esc(st.explain), 13f, K.Green, 4f); ex.style.marginLeft = 24f; }
            if (!now) return;
            var box = K.Box(); box.style.marginLeft = 7f; box.style.marginTop = 6f; K.Pad(box, 2f, 0f, 6f, 14f); K.Line(box, K.Sun, 0f, 0f, 0f, 2f); c.Add(box);
            Para(box, K.Esc(st.text), 15f, K.Text, 2f);
            string al = ActionLabel(st.action);
            if (al != null)
            {
                var ab = Btn.Text(al, () => EnvAction(st.action), K.Button, K.ButtonHover, 14f, Color.white, ActionIcon(st.action), Color.white);
                ab.style.alignSelf = Align.FlexStart; ab.style.marginTop = 10f; ab.Enabled = !g.Env.Busy; box.Add(ab);
            }
            string hk = Task.id + ":" + i;
            if (!string.IsNullOrEmpty(st.hint))
            {
                if (envHints.Contains(hk)) { var h = Para(box, "<color=#75BEFF>Подсказка.</color> " + K.Esc(st.hint), 14f, K.Text, 10f); K.Line(h, K.Blue, 0f, 0f, 0f, 3f); K.Pad(h, 4f, 0f, 4f, 10f); }
                else if (Diff == Difficulty.Easy) SmallBtn(box, "Подсказка", () => { g.ReportWork(WorkKind.Hint); envHints.Add(hk); RefreshSide(); }, "md", K.Blue);
                else if (Diff == Difficulty.Medium)
                {
                    var b = SmallBtn(box, "Подсказка — 30 монет", () => { if (g.Save.money < 30) return; g.ReportWork(WorkKind.Hint); g.Save.money -= 30; g.Persist(); envHints.Add(hk); RefreshSide(); RefreshTitle(); }, "coin", K.Sun);
                    b.Enabled = g.Save.money >= 30;
                }
            }
            int fails; envFails.TryGetValue(hk, out fails);
            if (!string.IsNullOrEmpty(st.solve))
            {
                if (envSolves.Contains(hk))
                {
                    Section(box, "КОМАНДА");
                    CodeBlock(box, st.solve, Pal.Hex("B5CEA8"));
                    SmallBtn(box, "Вставить в терминал", () => { termFocus = true; ed.Active = false; ed.Place(); bottom = Bottom.Terminal; RefreshBottom(); TermSet(st.solve); }, "terminal", EnvColor);
                }
                else if (Diff == Difficulty.Easy && fails >= 3)
                    SmallBtn(box, "Показать команду", () => { envSolves.Add(hk); RefreshSide(); }, "warning", Pal.Hex("FFB4B4"));
            }
            var r2 = K.Box(true); r2.style.alignItems = Align.Center; r2.style.marginTop = 12f; r2.style.flexWrap = Wrap.Wrap;
            var cb = Btn.Text(envChecking ? "Проверяю…" : "Проверить шаг", CheckTask, K.Brand, K.BrandHover, 14f, Color.white, "check", Color.white); cb.Enabled = !envChecking; r2.Add(cb);
            var kh = K.T("Ctrl+Enter · и сама после каждой команды", 12f, K.Dim); kh.style.marginLeft = 10f; r2.Add(kh);
            box.Add(r2);
            if (envNote != null) Para(box, K.Esc(envNote), 14f, Pal.Hex("FF8FA3"), 8f);
        }

        Btn SmallBtn(VisualElement c, string text, Action a, string icon, Color iconCol)
        {
            var b = Btn.Text(text, a, K.Button2, K.Button2Hover, 13f, K.Text, icon, iconCol);
            b.style.alignSelf = Align.FlexStart; b.style.marginTop = 8f; b.style.height = 28f; c.Add(b);
            return b;
        }

        // ======================= правая панель: окружение =======================
        void RightScenario(VisualElement c)
        {
            rightTitle.text = "ОКРУЖЕНИЕ";
            var sh = g.Env;
            Color col; string head;
            switch (sh.State)
            {
                case EnvState.NoDocker: col = K.Red; head = "Docker не установлен"; break;
                case EnvState.DockerStopped: col = K.Orange; head = "Docker не запущен"; break;
                case EnvState.NoImage: col = K.Sun; head = "Песочница не собрана"; break;
                case EnvState.Ready: col = sh.ContainerUp ? K.Green : K.Sun; head = sh.ContainerUp ? "Песочница работает" : "Песочница остановлена"; break;
                default: col = K.Muted; head = "Проверяю Docker…"; break;
            }
            var card = K.Box(); card.style.backgroundColor = Pal.Hex("202020"); K.Radius(card, 6f); K.Pad(card, 10f, 12f, 12f, 12f); K.Line(card, col, 0f, 0f, 0f, 3f); card.style.marginTop = 4f;
            var hr = K.Box(true); hr.style.alignItems = Align.Center;
            hr.Add(new Icon("dot", col, 14f)); var hl = K.T("<b>" + head + "</b>" + (sh.Probing && sh.State != EnvState.Unknown ? "  <color=#9D9D9D>проверяю…</color>" : ""), 15f, K.TextHi); hl.style.marginLeft = 6f; hr.Add(hl);
            card.Add(hr);
            var dl = K.T(K.Esc(sh.StateText), 13f, K.Muted, false, false, true); dl.style.marginTop = 6f; card.Add(dl);
            if (g.EnvOpen(Task))
            {
                if (sh.State == EnvState.NoDocker) SmallBtn(card, "Скачать Docker Desktop", () => EnvAction("docker-site"), "browser", K.Blue);
                else if (sh.State == EnvState.DockerStopped) SmallBtn(card, "Запустить Docker Desktop", () => EnvAction("docker-start"), "play", K.Green);
                else if (sh.State == EnvState.NoImage && EnvBuildAllowed) SmallBtn(card, "Собрать песочницу", () => EnvAction("build"), "stack", K.Sun).Enabled = !sh.Busy;
                else if (sh.State == EnvState.Ready && !sh.ContainerUp && (EnvMayAutostart || EnvBuildAllowed)) SmallBtn(card, "Запустить песочницу", () => EnvAction("start"), "play", K.Green).Enabled = !sh.Busy;
            }
            SmallBtn(card, "Проверить снова", () => { sh.Probe(); RefreshRight(); }, "reset", K.Muted).Enabled = !sh.Probing;
            c.Add(card);
            // папка
            Section(c, "ПАПКА /work НА ТВОЁМ ПК");
            var pl = K.T(K.Esc(DevEnv.WorkDir), 13f, K.Text, true, false, true); pl.style.whiteSpace = WhiteSpace.Normal; pl.style.marginTop = 4f; c.Add(pl);
            SmallBtn(c, "Открыть в проводнике", EnvOpenFolder, "folder", Pal.Hex("DCB67A"));
            // что произошло
            Section(c, "ЧТО ПРОИЗОШЛО");
            if (envLastRec == null) Para(c, "Выполни команду в терминале — здесь появится разбор: что делает каждая её часть.", 14f, K.Muted, 4f);
            else
            {
                CodeBlock(c, "$ " + envLastRec.cmd, Pal.Hex("DCDCAA"));
                foreach (var line in envExplain) Para(c, line, 14f, K.Text, 4f);
                int code = envLastRec.code;
                string ce = code == 0 ? "<color=#89D185>Код выхода 0 — команда прошла успешно.</color>"
                          : "<color=#F48771>Код выхода " + code + " — ошибка.</color>" + (code == 127 ? " 127 — такой команды нет (опечатка?)." : code == 126 ? " 126 — файл не запускается (нет прав?)." : code == 130 || code == 137 || code == 143 ? " Команду прервали." : " Читай сообщение об ошибке в терминале — обычно там сказано, что не так.");
                Para(c, ce, 13f, K.Text, 8f);
            }
            if (Sc != null && !string.IsNullOrEmpty(Sc.view))
            {
                Section(c, Sc.view.StartsWith("git:") ? "ГРАФ КОММИТОВ" : "КОНТЕЙНЕРЫ ИГРЫ");
                if (envView == null) Para(c, envViewBusy ? "Загружаю…" : "Появится, когда Docker и песочница будут готовы.", 13f, K.Muted, 4f);
                else CodeBlock(c, envView, Pal.Hex("9CDCFE"));
            }
            // уборка
            Section(c, "УБОРКА");
            Para(c, "Всё, что игра создаёт в Docker, помечено меткой stazher=1. Твои собственные контейнеры и образы она не трогает.", 13f, K.Muted, 4f);
            bool dockerUp = sh.State == EnvState.NoImage || sh.State == EnvState.Ready;
            var b1 = SmallBtn(c, cleanupConfirm == 1 ? "Точно убрать?" : "Убрать за собой", () => EnvCleanupClick(1), "close", cleanupConfirm == 1 ? K.Red : K.Muted); b1.Enabled = dockerUp && !sh.Busy;
            Para(c, "Контейнеры задач (например, site). Песочница и файлы в /work останутся.", 12f, K.Dim, 4f);
            var b2 = SmallBtn(c, cleanupConfirm == 2 ? "Точно удалить всё?" : "Удалить всё, что создала игра", () => EnvCleanupClick(2), "warning", cleanupConfirm == 2 ? K.Red : K.Muted); b2.Enabled = dockerUp && !sh.Busy;
            Para(c, "Ещё и песочницу, её образ и образы, скачанные в игре (nginx:alpine и другие). Файлы в /work останутся, песочницу можно собрать заново.", 12f, K.Dim, 4f);
        }

        // ======================= проводник: раздел «Окружение» =======================
        void EnvSection(VisualElement c)
        {
            var list = g.EnvTasks;
            var h = K.Box(true); h.style.alignItems = Align.Center; h.style.marginTop = 12f;
            h.Add(new Icon("terminal", EnvColor, 14f)); var ht = K.T("ОКРУЖЕНИЕ", 12f, EnvColor, false, true); ht.style.marginLeft = 6f; h.Add(ht);
            h.Add(K.Spacer()); h.Add(K.T(g.EnvDoneCount + " / " + list.Count, 12f, K.Muted)); c.Add(h);
            foreach (var t in list)
            {
                bool open = g.EnvOpen(t), cur = t == Task, done = g.IsDone(t);
                var tt = t;
                var row = new Btn(() =>
                {
                    if (!open) { Notice("Сначала пройди миссию «Настрой окружение»: этим задачам нужен терминал с песочницей.", "lock", K.Muted); return; }
                    if (tt != Task) Open(tt);
                });
                row.style.height = 28f; K.Pad(row, 0f, 8f, 0f, 8f);
                row.SetColors(cur ? K.Press : Color.clear, cur ? K.Press : K.Hover);
                row.Add(FileIcon(t, open));
                var nl = K.T(TaskCode(t).ToLower().Replace("-", "_") + Ext(t), 14f, open ? (cur ? K.TextHi : K.Text) : K.Dim, true); nl.style.marginLeft = 8f; nl.style.flexShrink = 0f; row.Add(nl);
                var ttl = K.T(K.Esc(t.title), 13f, K.Muted); ttl.style.marginLeft = 10f; ttl.style.flexShrink = 1f; ttl.style.overflow = Overflow.Hidden; row.Add(ttl);
                row.Add(K.Spacer());
                if (done) row.Add(new Icon("check", K.Green, 16f));
                else if (open) row.Add(K.T(g.EnvStepOf(t) + "/" + t.scenario.steps.Count, 12f, K.Muted));
                c.Add(row);
            }
            if (!g.EnvMissionDone) Para(c, "Начни с миссии: Гена поможет поставить Docker и запустить песочницу для терминала.", 12f, K.Muted, 4f);
        }
    }
}
