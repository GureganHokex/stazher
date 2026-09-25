// Точка входа: игра сама собирается в ЛЮБОЙ сцене при нажатии Play.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Intern.Game
{
    public class GameRoot : MonoBehaviour
    {
        public enum Mode { Menu, Walk, Transition, Ide, Dialog, Pause, Wardrobe, Lunch, DaySummary, Fired }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameRoot>() != null || SelfTest.Requested) return;
            new GameObject("InternGame").AddComponent<GameRoot>();
        }

        public SaveData Save;
        public TaskFile Tasks;
        public UiKit Ui;
        IdeWindow ide;          // старая IDE на IMGUI — запасной вариант
        IdeScreen ideUi;        // новая IDE на UI Toolkit, рисуется на экране монитора
        GameUi ui;              // меню, пауза, настройки, HUD на UI Toolkit (если не создался — старый IMGUI)
        Transform ideQuad;      // своя плоскость экрана перед монитором (у экрана из модели нет развёртки и он утоплен в корпус)
        float ideQuadW, ideQuadH;
        WardrobeScreen wardrobe;
        PlayerController player;
        OfficeRefs refs;
        Mode mode = Mode.Menu;
        Interactable focus;
        readonly List<BugCritter> bugs = new List<BugCritter>();
        float ideShownAt;
        bool firstWardrobe;

        // диалог
        string dlgTitle, dlgText;
        List<KeyValuePair<string, Action>> dlgButtons = new List<KeyValuePair<string, Action>>();

        // рабочий день, обед, контроль Гены
        public WorkDay Work { get; private set; }
        CityRefs city;
        LunchRun lunch;
        Knife knife;
        Transform exitSpot;
        Mode pausedFrom = Mode.Walk;
        bool dayOverPending, dayOverNoticed;
        float lastInput, fadeAlpha, lastEditAt = -99f;
        string ideTaskId; float ideTaskTime;
        readonly HashSet<string> theoryCredited = new HashSet<string>();
        public DayReport LastReport { get; private set; }
        public DayReport FiredReport { get; private set; }
        public LunchRun Lunch { get { return lunch; } }

        // тосты
        readonly Queue<string> toasts = new Queue<string>();
        string toast; float toastUntil;

        // ---------- направление, грейд, прогресс ----------
        public TrackPath Path { get; private set; }
        HashSet<string> doneSet; SaveData doneOwner; int doneCount = -1;
        public HashSet<string> Done
        {
            get
            {
                if (doneSet == null || doneOwner != Save || doneCount != Save.done.Count) { doneSet = new HashSet<string>(Save.done); doneOwner = Save; doneCount = Save.done.Count; }
                return doneSet;
            }
        }
        public string Profession { get { return string.IsNullOrEmpty(Save.profession) ? "backend" : Save.profession; } }
        public string ProfessionName { get { return Professions.Name(Profession); } }
        public int GradeIdx { get { return Path.GradeIndex(Done); } }          // 0..3, 4 — направление пройдено
        public bool PathComplete { get { return GradeIdx >= 4; } }
        public string RankName { get { return Grades.Name(Mathf.Min(GradeIdx, 3)); } }
        public string RankFull { get { return RankName + " · " + ProfessionName; } }

        void LoadPath()
        {
            Path = Tracks.BuildPath(Profession);
            Tasks = new TaskFile { language = Profession, tasks = Path.Tasks };
            Debug.Log("[Стажёр] Направление " + Profession + ": тем " + Path.Topics.Count + ", задач " + Path.Tasks.Length + ", грейд " + RankName);
        }

        // Старые сохранения (15 задач Python, id py01…) → задачи направления Backend
        void MigrateSave()
        {
            if (Save.version >= 2) return;
            bool had = Save.done.Count > 0 || Save.codeIds.Count > 0 || Save.hasCharacter;
            if (had)
            {
                var map = Tracks.LegacyMap();
                string n;
                Save.done = Save.done.Select(id => map.TryGetValue(id, out n) ? n : id).Distinct().ToList();
                for (int i = 0; i < Save.codeIds.Count; i++) if (map.TryGetValue(Save.codeIds[i], out n)) Save.codeIds[i] = n;
                if (string.IsNullOrEmpty(Save.profession)) Save.profession = "backend";
                Save.xp = Save.done.Count * 30;
            }
            Save.version = 2;
            if (had) { Progress.Save(Save); Debug.Log("[Стажёр] Сохранение перенесено на направления: сдано " + Save.done.Count); }
        }

        // Версия 3: рабочий день. Старые сохранения начинают с понедельника, 9:00, с ножом
        void MigrateDay()
        {
            if (Save.version >= 3) return;
            WorkDay.Reset(Save);
            Save.version = 3;
            if (Progress.HasSave()) Progress.Save(Save);
        }

        void SetupWork()
        {
            Work = new WorkDay(Save, () => GradeIdx);
            Work.Lead = t => { Toast("Гена: " + t); if (refs != null && refs.lead != null) refs.lead.React(5, 2.5f); };
            Work.Notice = t => Toast(t);
            Work.DayOver = () => { dayOverPending = true; dayOverNoticed = false; };
            Work.Fired = OnFired;
        }

#if UNITY_EDITOR
        // Отладка в редакторе: весь лог игры ещё и в файл Temp/intern_log.txt (удобно смотреть снаружи)
        static System.IO.StreamWriter logFile;
        static void LogToFile(string msg, string stack, LogType type)
        {
            try
            {
                if (logFile == null) { logFile = new System.IO.StreamWriter(System.IO.Path.GetFullPath(Application.dataPath + "/../Temp/intern_log.txt"), false, new System.Text.UTF8Encoding(false)); logFile.AutoFlush = true; }
                logFile.WriteLine("[" + Time.frameCount + " " + type + "] " + msg);
                if (type == LogType.Exception || type == LogType.Error) logFile.WriteLine(stack);
            }
            catch { }
        }
#endif

        void Awake()
        {
#if UNITY_EDITOR
            Application.logMessageReceived -= LogToFile; Application.logMessageReceived += LogToFile;
#endif
            Save = Progress.Load();
            if (Save.look == null) Save.look = new Appearance();
            if (Save.owned == null) Save.owned = new List<string>();
            MigrateSave();
            MigrateDay();
            LoadPath();
            SetupWork();
            ide = new IdeWindow(this);
            wardrobe = new WardrobeScreen(this);

            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
            // Офис из Blender, если модели лежат в Resources/Models, иначе — собранный из кода
            refs = ModelLib.HasOffice ? ImportedOffice.Build() : OfficeBuilder.Build();
            Debug.Log("[Стажёр] Офис: " + (ModelLib.HasOffice ? "модель из Blender" : "собран из кода") +
                      ", игрок: " + (ModelLib.HasCharacter("Intern") ? "модель из Blender" : "из кода") +
                      ", монитор найден: " + (refs.screen != null) + ", Гена: " + (refs.lead != null));
            var pgo = new GameObject("Player");
            player = pgo.AddComponent<PlayerController>();
            player.firstPerson = Save.firstPerson;
            player.SetAvatar(Save.look);
            player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.cinematic = true;
            UpdateBoard();
            PlaceExitDoor();
            SetCursor(false);
            // Новая IDE: панель UI Toolkit рисуется в текстуру, текстура — на экран монитора
            if (refs.screen != null)
            {
                float aspect = refs.screenSize.y > 0.01f ? refs.screenSize.x / refs.screenSize.y : 16f / 9f;
                ideUi = IdeScreen.TryCreate(this, aspect, ScreenToMonitor);
                if (ideUi != null)
                {
                    BuildIdeQuad(ModelLib.ScreenMaterial(ideUi.Texture, 0.9f, Color.white));
                    if (CurrentTask != null) ideUi.Open(CurrentTask);
                }
            }
            // Настройки (экран, графика, звук, управление) и новый интерфейс
            GameConfig.Load(); GameConfig.ApplyAll();
            ui = GameUi.TryCreate(this);
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void Persist() { if (FiredReport == null) Progress.Save(Save); }

        public bool IsUnlocked(int i) { return i >= 0 && i < Tasks.tasks.Length && Path.TaskOpen(Tasks.tasks[i], Done); }
        public bool IsOpen(TaskData t) { return Path.TaskOpen(t, Done); }
        public bool IsDone(TaskData t) { return t != null && Done.Contains(t.id); }

        public TaskData CurrentTaskPublic { get { return CurrentTask; } }

        TaskData CurrentTask
        {
            get
            {
                var t = Path.Current(Done);
                if (t != null) return t;
                return Tasks.tasks.Length > 0 ? Tasks.tasks[Tasks.tasks.Length - 1] : null;
            }
        }

        void UpdateBoard()
        {
            int done = DoneCount, total = TotalCount;
            var cur = CurrentTask;
            var tp = cur != null ? Path.TopicOf(cur) : null;
            refs.board.text = ProfessionName.ToUpperInvariant() + " · " + RankName + "\n\nСделано: " + done + " / " + total +
                         (cur != null && !PathComplete ? "\nТема: " + (tp != null ? tp.title : "") + "\nСейчас: " + cur.key + " " + cur.title : "\nНаправление пройдено!") +
                         "\n\nБагов поймано: " + Save.bugsCaught;
        }

        // ================== Цикл ==================
        void Update()
        {
            switch (mode)
            {
                case Mode.Menu:
                    player.Tick(false);
                    player.MenuOrbit(Time.time);
                    if (InputX.Esc() && ui != null) ui.Back();
#if UNITY_EDITOR
                    // только в редакторе: F9 в меню — отметить/снять пройденными Backend, Frontend и DevOps (проверка Fullstack)
                    if (InputX.DebugCareer())
                    {
                        bool open = Career.FullstackOpen;
                        foreach (var p in new[] { "backend", "frontend", "devops" }) { if (open) Career.Unmark(p); else Career.MarkDone(p); }
                        Debug.Log("[Стажёр] F9: Fullstack " + (Career.FullstackOpen ? "открыт" : "закрыт"));
                        if (ui != null) ui.RefreshMenuPublic();
                    }
#endif
                    break;
                case Mode.Walk:
                    player.Tick(true);
                    FindFocus();
                    if (focus != null && (InputX.Interact() || (focus is BugCritter && InputX.Click()))) focus.Interact(this);
                    else if (InputX.Click() && Cursor.lockState != CursorLockMode.Locked) SetCursor(true);
                    if (InputX.ToggleView()) { player.ToggleView(); Save.firstPerson = player.firstPerson; Persist(); }
#if UNITY_EDITOR
                    if (InputX.DebugSit()) OpenIde();   // только в редакторе: сразу за компьютер (для тестов)
#endif
                    if (InputX.Esc()) PauseFrom(Mode.Walk);
                    else if (dayOverPending) ShowDaySummary();
                    break;
                case Mode.Lunch:
                    player.Tick(true);
                    FindFocus();
                    if (focus != null && InputX.Interact()) focus.Interact(this);
                    else if (InputX.Attack()) { if (Cursor.lockState != CursorLockMode.Locked) SetCursor(true); else Attack(); }
                    if (InputX.ToggleView()) { player.ToggleView(); Save.firstPerson = player.firstPerson; Persist(); }
                    if (lunch != null)
                    {
                        lunch.Tick(Time.deltaTime);
                        Work.Advance(Time.deltaTime * 60f / lunch.duration, false);
                        if (lunch.Over) EndLunch(true);
                    }
                    if (mode == Mode.Lunch && InputX.Esc()) PauseFrom(Mode.Lunch);
                    break;
                case Mode.Ide:
                    if (ideUi != null) ideUi.Tick(Time.deltaTime); else ide.Tick(Time.deltaTime);
                    TrackTheory(Time.deltaTime);
                    if (dayOverPending && !dayOverNoticed) { dayOverNoticed = true; if (ideUi != null) ideUi.GameNotice("18:00 — рабочий день окончен. Доделай задачу и вставай из-за стола (Esc)."); }
                    if (InputX.Esc() && !(ideUi != null && ideUi.WantsEsc)) CloseIde();
#if UNITY_EDITOR
                    if (InputX.DebugClose()) CloseIde();   // только в редакторе: F6 = «Выйти» (для тестов)
#endif
                    break;
                case Mode.Dialog:
                    player.Tick(false);
                    if (InputX.Esc()) CloseDialog();
                    break;
                case Mode.Wardrobe:
                    UpdateWardrobeCamera();
                    break;
                case Mode.Pause:
                    player.Tick(false);
                    if (InputX.Esc() && (ui == null || !ui.Back())) Resume();
                    break;
                case Mode.DaySummary:
                case Mode.Fired:
                    player.Tick(false);
                    break;
            }
            TickWorkday();
            if (InputX.Screenshot()) TakeScreenshot();
            if (toast == null || Time.unscaledTime > toastUntil)
            {
                toast = toasts.Count > 0 ? toasts.Dequeue() : null;
                if (toast != null) toastUntil = Time.unscaledTime + 3.2f;
            }
            bugs.RemoveAll(b => b == null);
            if (ideUi != null) ideUi.Update(Time.deltaTime);
            if (ui != null) ui.Tick(Time.unscaledDeltaTime);
#if UNITY_EDITOR
            if (ideUi != null && InputX.DebugDump()) { Debug.Log("[Стажёр] F7: mode=" + mode + ", ideActive=" + ideUi.Active + ", task=" + (ideUi.Task != null ? ideUi.Task.id : "-")); ideUi.DebugDump(System.IO.Path.GetFullPath(Application.dataPath + "/../Temp")); Toast("Снимок IDE сохранён"); }
#endif
        }

        // F12 — скриншот: в игре — в папку Screenshots рядом с сохранениями, в редакторе — в Temp/Screenshots проекта
        void TakeScreenshot()
        {
            try
            {
#if UNITY_EDITOR
                string dir = System.IO.Path.GetFullPath(Application.dataPath + "/../Temp/Screenshots");
#else
                string dir = System.IO.Path.Combine(Application.persistentDataPath, "Screenshots");
#endif
                System.IO.Directory.CreateDirectory(dir);
                string file = System.IO.Path.Combine(dir, "stazher_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
                ScreenCapture.CaptureScreenshot(file);
                Debug.Log("[Стажёр] Скриншот: " + file);
#if !UNITY_EDITOR
                Toast("Скриншот сохранён: " + file);
#endif
            }
            catch (Exception e) { Debug.LogWarning("[Стажёр] Скриншот не сохранился: " + e.Message); }
        }

        public string ScreenRendererInfo()
        {
            if (refs == null || refs.screen == null) return "screen: null";
            var r = refs.screen.GetComponent<Renderer>();
            var mf = refs.screen.GetComponent<MeshFilter>();
            string s = "mats: " + (r != null ? r.sharedMaterials.Length : 0) + ", props: " + (r != null && r.sharedMaterial != null ? string.Join(",", r.sharedMaterial.GetTexturePropertyNames()) : "") + "\n";
            if (r != null && r.sharedMaterial != null) foreach (var pn in r.sharedMaterial.GetTexturePropertyNames()) { var tx = r.sharedMaterial.GetTexture(pn); s += "  " + pn + " = " + (tx != null ? tx.name + " " + tx.GetType().Name : "null") + "\n"; }
            if (r != null && r.sharedMaterial != null) s += "  keywords: " + string.Join(" ", r.sharedMaterial.shaderKeywords) + "\n";
            s += "screen: " + refs.screen.name + ", renderer: " + (r != null) + ", mat: " + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name + " / tex " + (r.sharedMaterial.mainTexture != null ? r.sharedMaterial.mainTexture.name : "null") : "null");
            if (mf != null && mf.sharedMesh != null)
            {
                var m = mf.sharedMesh; var uv = m.isReadable ? m.uv : new Vector2[0];
                Vector2 mn = new Vector2(9, 9), mx = new Vector2(-9, -9);
                foreach (var u in uv) { mn = Vector2.Min(mn, u); mx = Vector2.Max(mx, u); }
                s += "\nmesh: " + m.name + " verts " + m.vertexCount + " bounds " + m.bounds + " uv " + mn + " .. " + mx;
            }
            return s;
        }

        void FindFocus()
        {
            focus = null;
            if (player.firstPerson)
            {
                var cam = player.cam.transform;
                RaycastHit hit;
                if (Physics.Raycast(cam.position, cam.forward, out hit, 3.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                    focus = hit.collider.GetComponentInParent<Interactable>();
                return;
            }
            // От третьего лица: ближайший интерактивный объект перед персонажем
            var p = player.Position + Vector3.up * 1.0f;
            var fwd = player.transform.forward;
            float best = float.MaxValue;
            foreach (var col in Physics.OverlapSphere(p + fwd * 0.9f, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                var it = col.GetComponentInParent<Interactable>();
                if (it == null) continue;
                var to = col.ClosestPoint(p) - p; to.y = 0;
                float score = to.magnitude - Vector3.Dot(to.normalized, fwd) * 0.6f;
                if (score < best) { best = score; focus = it; }
            }
        }

        void Resume()
        {
            mode = pausedFrom == Mode.Lunch && lunch != null ? Mode.Lunch : pausedFrom == Mode.Ide ? Mode.Ide : Mode.Walk;
            if (lunch != null) lunch.SetPaused(false);
            SetCursor(mode != Mode.Ide);
            lastInput = Time.unscaledTime;
        }

        void PauseFrom(Mode from)
        {
            pausedFrom = from;
            if (lunch != null) lunch.SetPaused(true);
            mode = Mode.Pause; SetCursor(false);
        }

        public void Toast(string s)
        {
            if (mode == Mode.Ide && ideUi != null) { ideUi.GameNotice(s); return; }   // за компьютером — уведомлением в IDE
            toasts.Enqueue(s);
        }

        // ================== Компьютер: посадка и работа ==================
        public void OpenIde()
        {
            if (mode != Mode.Walk) return;
            var open = ideUi != null ? ideUi.Task : ide.Task;
            var t = open ?? CurrentTask;
            if (t == null) return;
            if (open == null) { if (ideUi != null) ideUi.Open(t); else ide.Open(t); }
            StartCoroutine(SitDown());
        }

        public void CloseIde()
        {
            if (mode != Mode.Ide) return;
            if (ideUi != null) ideUi.Close(); else ide.Close();
            StartCoroutine(StandUp());
        }

        static float Smooth(float k) { k = Mathf.Clamp01(k); return k * k * (3 - 2 * k); }

        Vector3 SeatPos { get { var p = refs.playerChair.position; p.y = 0; return p; } }
        float DeskYaw { get { return refs.playerDesk.eulerAngles.y; } }
        Vector3 DeskFwd { get { return Quaternion.Euler(0, DeskYaw, 0) * Vector3.forward; } }

        // Кадр «из-за плеча», пока персонаж садится
        void ShoulderPose(out Vector3 pos, out Quaternion rot)
        {
            var right = Quaternion.Euler(0, DeskYaw, 0) * Vector3.right;
            pos = SeatPos - DeskFwd * 1.6f + Vector3.up * 1.85f + right * 0.45f;
            rot = Quaternion.LookRotation(refs.screen.position - pos);
        }

        // Камера ровно перед экраном: монитор занимает почти весь кадр
        void MonitorPose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            fov = 40f;
            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.6f;
            float halfV = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), halfH = halfV * aspect;
            float dH = refs.screenSize.y * 0.5f / 0.9f / halfV;
            float dW = refs.screenSize.x * 0.5f / 0.94f / halfH;
            float d = Mathf.Max(dH, dW);
            // направление «от экрана к креслу» — работает и для модели из Blender, и для офиса из кода
            var n = SeatPos - refs.screen.position; n.y = 0;
            n = n.sqrMagnitude > 0.001f ? n.normalized : -refs.screen.forward;
            pos = refs.screen.position + n * d;
            rot = Quaternion.LookRotation(-n, Vector3.up);
        }

        IEnumerator CameraMove(Vector3 p1, Quaternion r1, float f1, float time)
        {
            Vector3 p0; Quaternion r0; float f0;
            player.GetCamera(out p0, out r0, out f0);
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                float k = Smooth(t / time);
                player.SetCamera(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k), Mathf.Lerp(f0, f1, k));
                yield return null;
            }
            player.SetCamera(p1, r1, f1);
        }

        Vector3 DeskRight { get { return Quaternion.Euler(0, DeskYaw, 0) * Vector3.right; } }
        Vector3 chairRestPos; Quaternion chairRestRot; bool chairRestSaved;

        void SaveChairRest()
        {
            if (refs.chairObj == null || chairRestSaved) return;
            chairRestPos = refs.chairObj.position; chairRestRot = refs.chairObj.rotation; chairRestSaved = true;
        }

        void SetChair(Vector3 pos, float swivel)
        {
            if (refs.chairObj == null) return;
            refs.chairObj.position = pos;
            refs.chairObj.rotation = Quaternion.Euler(0, swivel, 0) * chairRestRot;
        }

        Vector3 ChairBase { get { SaveChairRest(); return refs.chairObj != null ? new Vector3(chairRestPos.x, 0, chairRestPos.z) : SeatPos; } }

        // Кат-сцена: подойти, отодвинуть кресло, сесть с «плюх», подкатиться к столу
        IEnumerator SitDown()
        {
            mode = Mode.Transition;
            focus = null;
            player.cinematic = true;
            player.EnablePhysics(false);
            var av = player.avatar;
            av.SetHeadVisible(true);
            SaveChairRest();

            Vector3 c0 = ChairBase, c1 = c0 - DeskFwd * 0.38f;
            Vector3 side = c0 - DeskFwd * 0.55f + DeskRight * 0.6f;
            Vector3 start = player.Position; start.y = 0;
            float startYaw = player.transform.eulerAngles.y;
            Vector3 camA; Quaternion rotA;
            ShoulderPose(out camA, out rotA);

            // 1. подходим к креслу сбоку
            Vector3 p0; Quaternion r0; float f0;
            player.GetCamera(out p0, out r0, out f0);
            float dist = Vector3.Distance(start, side);
            float T1 = Mathf.Clamp(dist / 3f, 0.35f, 0.9f);
            float yawToChair = Quaternion.LookRotation(c0 - side).eulerAngles.y;
            for (float t = 0; t < T1; t += Time.deltaTime)
            {
                float k = Smooth(t / T1);
                player.Place(Vector3.Lerp(start, side, k), Mathf.LerpAngle(startYaw, yawToChair, Smooth(t / (T1 * 0.7f))));
                av.moveSpeed = dist / T1 * (1 - k * 0.6f);
                player.SetCamera(Vector3.Lerp(p0, camA, k), Quaternion.Slerp(r0, rotA, k), Mathf.Lerp(f0, 55f, k));
                yield return null;
            }
            av.moveSpeed = 0;

            // 2. отодвигаем кресло на себя, оно поворачивается к нам
            for (float t = 0; t < 0.45f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.45f);
                SetChair(Vector3.Lerp(c0, c1, k), 25f * k);
                yield return null;
            }

            // 3. шаг к креслу и разворот к столу
            Vector3 front = c1 + DeskFwd * 0.06f;
            for (float t = 0; t < 0.45f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.45f);
                player.Place(Vector3.Lerp(side, front, k), Mathf.LerpAngle(yawToChair, DeskYaw, k));
                av.moveSpeed = 1.2f * (1 - k);
                SetChair(c1, 25f * (1 - k));
                yield return null;
            }
            av.moveSpeed = 0;

            // 4. садимся: небольшой наклон вперёд и «плюх»
            av.sitTarget = 1;
            bool plopped = false;
            for (float t = 0; t < 0.5f; t += Time.deltaTime)
            {
                player.Place(Vector3.Lerp(front, c1, Smooth(t / 0.5f)), DeskYaw);
                if (!plopped && t > 0.36f) { av.Plop(); plopped = true; }
                yield return null;
            }

            // 5. подкатываемся к столу вместе с креслом
            for (float t = 0; t < 0.55f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.55f);
                var pos = Vector3.Lerp(c1, c0, k);
                SetChair(pos, Mathf.Sin(k * Mathf.PI) * 6f);
                player.Place(pos, DeskYaw);
                yield return null;
            }
            SetChair(c0, 0); player.Place(c0, DeskYaw);
            av.handsOnDesk = true;

            // 6. камера наезжает на монитор
            Vector3 camB; Quaternion rotB; float fovB;
            MonitorPose(out camB, out rotB, out fovB);
            Vector3 cc0; Quaternion q0; float fv0;
            player.GetCamera(out cc0, out q0, out fv0);
            for (float t = 0; t < 0.8f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.8f);
                if (k > 0.5f) av.SetHeadVisible(false);
                player.SetCamera(Vector3.Lerp(cc0, camB, k), Quaternion.Slerp(q0, rotB, k), Mathf.Lerp(fv0, fovB, k));
                yield return null;
            }
            player.SetCamera(camB, rotB, fovB);
            mode = Mode.Ide;
            ideShownAt = Time.unscaledTime;
            if (ideUi != null) ideUi.SetActive(true);
            SetCursor(false);
        }

        // Кат-сцена: откатиться, встать, отойти; кресло откатывается на место
        IEnumerator StandUp()
        {
            mode = Mode.Transition;
            var av = player.avatar;
            Vector3 camA; Quaternion rotA;
            ShoulderPose(out camA, out rotA);
            yield return StartCoroutine(CameraMoveWithHead(camA, rotA, 55f, 0.6f));

            Vector3 c0 = ChairBase, c1 = c0 - DeskFwd * 0.38f;
            Vector3 side = c0 - DeskFwd * 0.6f + DeskRight * 0.6f;
            av.handsOnDesk = false;
            for (float t = 0; t < 0.45f; t += Time.deltaTime)
            {
                var pos = Vector3.Lerp(c0, c1, Smooth(t / 0.45f));
                SetChair(pos, 0); player.Place(pos, DeskYaw);
                yield return null;
            }
            av.sitTarget = 0;
            for (float t = 0; t < 0.45f; t += Time.deltaTime) yield return null;
            float yawOut = Quaternion.LookRotation(side - c1).eulerAngles.y;
            for (float t = 0; t < 0.5f; t += Time.deltaTime)
            {
                float k = Smooth(t / 0.5f);
                player.Place(Vector3.Lerp(c1, side, k), Mathf.LerpAngle(DeskYaw, yawOut, k));
                av.moveSpeed = 1.3f * Mathf.Sin(k * Mathf.PI);
                SetChair(Vector3.Lerp(c1, c0, Smooth(Mathf.Clamp01(t / 0.5f - 0.2f))), 0);
                yield return null;
            }
            av.moveSpeed = 0;
            SetChair(c0, 0);
            player.Teleport(side + Vector3.up * 0.05f, yawOut);
            player.FaceCameraYaw(yawOut);
            player.cinematic = false;
            player.BlendFromCurrent(0.5f);
            player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.Walk;
            SetCursor(true);
        }

        IEnumerator CameraMoveWithHead(Vector3 p1, Quaternion r1, float f1, float time)
        {
            Vector3 p0; Quaternion r0; float f0;
            player.GetCamera(out p0, out r0, out f0);
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                float k = Smooth(t / time);
                if (k > 0.4f) player.avatar.SetHeadVisible(true);
                player.SetCamera(Vector3.Lerp(p0, p1, k), Quaternion.Slerp(r0, r1, k), Mathf.Lerp(f0, f1, k));
                yield return null;
            }
            player.SetCamera(p1, r1, f1);
        }

        // Прямоугольник экрана монитора в координатах интерфейса
        Rect MonitorRect()
        {
            var scr = refs.screen;
            var c = scr.position;
            var rt = scr.right * refs.screenSize.x * 0.5f;
            var up = scr.up * refs.screenSize.y * 0.5f;
            var cam = player.cam;
            Vector3 a = cam.WorldToScreenPoint(c - rt - up), b = cam.WorldToScreenPoint(c + rt + up);
            Vector3 a2 = cam.WorldToScreenPoint(c + rt - up), b2 = cam.WorldToScreenPoint(c - rt + up);
            float xMin = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(a2.x, b2.x)), xMax = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(a2.x, b2.x));
            float yMin = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(a2.y, b2.y)), yMax = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(a2.y, b2.y));
            return new Rect(xMin, Screen.height - yMax, xMax - xMin, yMax - yMin);
        }

        // ================== Прогресс ==================
        public void CompleteTask(TaskData t, bool usedSolution, bool late)
        {
            if (Save.done.Contains(t.id)) { Toast("Эта задача уже сдана. Повторить — всегда полезно!"); return; }
            int oldGrade = GradeIdx;
            bool fsWasOpen = Career.FullstackOpen;
            float mult = 1f;
            var diff = (Difficulty)Save.difficulty;
            if (diff == Difficulty.Medium) mult *= 1.2f;
            if (diff == Difficulty.Hard) mult *= late ? 0.5f : 1.6f;
            else if (late) mult *= 0.75f;   // задача с таймером (инцидент) сдана после срока
            if (usedSolution) mult *= 0.5f;
            int reward = Mathf.Max(1, Mathf.RoundToInt(t.reward * mult));
            int xp = usedSolution ? t.xp / 2 : t.xp;
            bool sated = Work != null && Work.Sated;
            if (sated) xp = Mathf.RoundToInt(xp * 1.1f);
            Save.money += reward; Save.xp += xp; Save.done.Add(t.id);
            Save.dayTasks++; Save.dayXp += xp; Save.dayMoney += reward;
            if (Work != null) Work.Activity(WorkKind.Solved);
            Persist(); UpdateBoard();
            Toast("Задача сдана! +" + xp + " XP" + (sated ? " (сытый +10%)" : "") + ", +" + reward + " монет" + (late ? " (срок сорван)" : ""));
            if (player.avatar != null) player.avatar.React(2, 3f); // восторг
            var tp = Path.TopicOf(t);
            if (tp != null && TrackPath.TopicDone(tp, Done)) Toast("Тема закрыта: " + tp.title);
            int g = GradeIdx;
            if (g >= 4 && oldGrade < 4)
            {
                Career.MarkDone(Profession);
                Toast("НАПРАВЛЕНИЕ ПРОЙДЕНО! Ты — Middle " + ProfessionName + "-разработчик.");
                if (!fsWasOpen && Career.FullstackOpen) Toast("ОТКРЫТ FULLSTACK! Сменить направление можно в паузе (Esc).");
                else if (Profession != "fullstack") Toast("Попробуй другое направление: смена — в паузе (Esc), прогресс сохранится.");
            }
            else if (g > oldGrade)
            {
                Toast("ПОВЫШЕНИЕ! Теперь ты " + RankName + " " + ProfessionName + ". Открыты новые темы.");
                if (g == 2) Toast("В гардеробе открылась корона для Junior+.");
            }
            var next = CurrentTask;
            if (next != null && next != t && !Save.done.Contains(next.id)) Toast("Новая задача: " + next.key + " " + next.title);
        }

        public void SpawnBug()
        {
            if (player.avatar != null) player.avatar.React(4, 2.5f); // грусть
            if (bugs.Count >= 12) return;
            var p = refs.spawn.position + UnityEngine.Random.insideUnitSphere * 3f; p.y = 0;
            p.x = Mathf.Clamp(p.x, -10.5f, 10.5f); p.z = Mathf.Clamp(p.z, -6.5f, 6.5f);
            bugs.Add(BugCritter.Spawn(p));
        }

        public void OnBugCaught()
        {
            Save.bugsCaught++; Save.money += 5; Persist(); UpdateBoard();
            Toast("Баг пойман! +5 монет");
        }

        // ================== Гардероб ==================
        public void OpenWardrobe(bool firstTime)
        {
            firstWardrobe = firstTime;
            mode = Mode.Wardrobe;
            player.cinematic = true;
            var spot = refs.lockerSpot.position; spot.y = 0;
            player.Place(spot, 0);
            player.avatar.SetHeadVisible(true);
            player.avatar.moveSpeed = 0; player.avatar.grounded = true;
            wardrobe.Open(Save.look, firstTime);
            SetCursor(false);
            UpdateWardrobeCamera();
        }

        void UpdateWardrobeCamera()
        {
            var spot = refs.lockerSpot.position; spot.y = 0;
            player.Place(spot, 180 + wardrobe.Spin + 180);
            var camRight = Vector3.left; // камера смотрит на -z, её «право» — это -x
            float h = player.Tall ? 1.2f : 1.0f, dist = player.Tall ? 2.9f : 2.5f;
            var target = spot + Vector3.up * h + camRight * (player.Tall ? 0.7f : 0.62f);
            var pos = target + new Vector3(0, 0.2f, dist);
            Vector3 p0; Quaternion r0; float f0;
            player.GetCamera(out p0, out r0, out f0);
            float k = 1 - Mathf.Exp(-8f * Time.deltaTime);
            player.SetCamera(Vector3.Lerp(p0, pos, k), Quaternion.Slerp(r0, Quaternion.LookRotation(target - pos), k), Mathf.Lerp(f0, 45f, k));
        }

        public void PreviewAppearance(Appearance ap) { player.SetAvatar(ap); player.avatar.SetHeadVisible(true); }

        public void CloseWardrobe(Appearance result, int cost)
        {
            if (cost > 0)
            {
                Save.money -= cost;
                if (!Save.owned.Contains("top" + result.top)) Save.owned.Add("top" + result.top);
                if (!Save.owned.Contains("acc" + result.accessory)) Save.owned.Add("acc" + result.accessory);
                if (!Save.owned.Contains("outfit" + result.outfit)) Save.owned.Add("outfit" + result.outfit);
                Toast("Обновка! −" + cost + " монет");
            }
            Save.look = result.Clone();
            Save.hasCharacter = true;
            Persist();
            player.SetAvatar(Save.look);
            var spot = refs.lockerSpot.position;
            player.Teleport(spot, 0);
            player.FaceCameraYaw(0);
            player.cinematic = false;
            player.BlendFromCurrent(0.6f);
            player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.Walk; SetCursor(true);
            if (firstWardrobe) Toast("Первый рабочий день! Подойди к тимлиду у доски и нажми E.");
        }

        // ================== Диалоги ==================
        void OpenDialog(string title, string text, params KeyValuePair<string, Action>[] buttons)
        {
            dlgTitle = title; dlgText = text; dlgButtons = buttons.ToList();
            mode = Mode.Dialog; SetCursor(false);
        }
        void CloseDialog() { mode = Mode.Walk; SetCursor(true); }
        static KeyValuePair<string, Action> Btn(string s, Action a) { return new KeyValuePair<string, Action>(s, a); }

        public void TalkToLead()
        {
            if (refs.lead != null) refs.lead.React(1, 2.5f);
            int done = DoneCount, total = TotalCount;
            string text;
            var cur = CurrentTask;
            if (done == 0)
                text = "О, новенький! Я Гена. Твой стол — тот, где уточка на мониторе. Ты у нас на направлении " + ProfessionName + ".\n\n" +
                       "Сначала общая база: терминал, Git, HTTP, дебаг — без этого никуда. Потом — задачи твоего направления, от Junior до Middle. " +
                       "Все тикеты — в IDE за компьютером, слева «Проводник» со всеми темами.";
            else if (PathComplete)
                text = "Ты прошёл всё направление " + ProfessionName + ". Для меня ты теперь Middle — и это заслуженно: " +
                       "ревью, инциденты, архитектура, оценки — ты всё это уже делал руками.\n\n" +
                       (Career.FullstackOpen ? "Fullstack открыт — переключайся в паузе (Esc), там сквозные задачи через весь стек." :
                        "Хочешь вырасти шире — возьми другое направление в паузе (Esc). Общая база уже закрыта, начнёшь сразу с Junior-тем.");
            else
                text = "Ты сейчас " + RankName + " " + ProfessionName + ", сделано " + done + " из " + total + ". Следующая задача: " + cur.key + " «" + cur.title + "».\n\n" + Tip(done);
            OpenDialog("Тимлид Гена", text, Btn("Понял, иду работать", CloseDialog));
        }

        static string Tip(int done)
        {
            string[] tips = {
                "Совет: читай ошибки внимательно. Там всегда есть номер строки.",
                "Совет: input() всегда возвращает строку. Числа нужно превращать через int().",
                "Совет: отступы в Python — это часть синтаксиса. 4 пробела на уровень.",
                "Совет: поставь точку остановки кликом по номеру строки, и отладчик остановится там.",
                "Совет: если цикл не заканчивается — проверь, меняется ли переменная из условия.",
                "Совет: range(1, n) не включает n. Это частый источник багов «на единицу».",
                "Совет: сначала напиши план в комментариях, потом код.",
                "Совет: в инцидентах сначала останавливай кровотечение — откат, фичефлаг, — а разбор причин потом.",
                "Совет: на ревью отделяй блокеры от вкусовщины. Автору важно понимать, что обязательно, а что — пожелание.",
                "Совет: оценка — это не «сколько я буду печатать код», а ещё тесты, ревью, выкладка и неизвестные.",
                "Совет: прежде чем гуглить ошибку, прочитай её целиком. Половина ответа обычно уже там.",
            };
            return tips[done % tips.Length];
        }

        public void OpenShop()
        {
            var list = new List<KeyValuePair<string, Action>>();
            list.Add(Btn("Кофе — 20 монет (быстрее ходишь 30 сек)", () =>
            {
                if (Save.money < 20) { Toast("Не хватает монет"); return; }
                Save.money -= 20; Persist(); player.speedBoostUntil = Time.time + 30; Toast("Бодрость +100!"); CloseDialog();
            }));
            if (!Save.hasDuck) list.Add(Btn("Резиновая уточка — 250 (ошибки на русском на тяжёлой)", () =>
            {
                if (Save.money < 250) { Toast("Не хватает монет"); return; }
                Save.money -= 250; Save.hasDuck = true; Persist(); Toast("Уточка на столе. Объясняй ей код вслух — это реально помогает!"); CloseDialog();
            }));
            if (!Save.hasMonitor) list.Add(Btn("Второй монитор — 400 (разбор строк на любой сложности)", () =>
            {
                if (Save.money < 400) { Toast("Не хватает монет"); return; }
                Save.money -= 400; Save.hasMonitor = true; Persist(); Toast("Второй монитор подключён!"); CloseDialog();
            }));
            list.Add(Btn("Уйти", CloseDialog));
            OpenDialog("Кофемашина", "У тебя " + Save.money + " монет. Что берём?", list.ToArray());
        }

        void StartGame(Difficulty d, bool fresh, string profession = null)
        {
            if (fresh)
            {
                Progress.Wipe(); Save = new SaveData { version = 3, profession = Career.CanPick(profession) && !string.IsNullOrEmpty(profession) ? profession : "backend" };
                WorkDay.Reset(Save);
                LoadPath();
                SetupWork();
                LastReport = null; FiredReport = null; dayOverPending = false; theoryCredited.Clear();
                ide = new IdeWindow(this); if (ideUi != null) ideUi.ResetProgress(CurrentTask);
                foreach (var b in bugs) if (b != null) Destroy(b.gameObject);
                player.SetAvatar(Save.look);
            }
            Save.difficulty = (int)d; Persist(); UpdateBoard();
            lastInput = Time.unscaledTime;
            if (Work.Ended) dayOverPending = true;
            if (fresh) Toast("Направление: " + ProfessionName + ". Начинаем с общей базы — грейд «Стажёр».");
            if (fresh || !Save.hasCharacter) { OpenWardrobe(true); return; }
            player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.FaceCameraYaw(refs.spawn.eulerAngles.y);
            player.cinematic = false;
            player.BlendFromCurrent(1.0f);
            player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.Walk; SetCursor(true);
            Toast(Work.WeekdayFull + ", " + WorkDay.TimeText(Work.Minute) + ". День " + Save.day + (Work.Strikes > 0 ? ", выговоров " + Work.Strikes + " из " + Work.StrikeLimit : "") + ".");
        }

        // ================== Интерфейс ==================
        void OnGUI()
        {
            if (Ui == null) Ui = new UiKit();
            float s = Screen.height / 900f;
            var baseMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1));
            GUI.matrix = baseMatrix;
            float W = Screen.width / s, H = 900f;

            switch (mode)
            {
                case Mode.Menu: if (ui == null) DrawMenu(W, H); break;
                case Mode.Walk: if (ui == null) DrawHud(W, H); break;
                case Mode.Ide:
                    if (ideUi != null)
                    {
                        GUI.matrix = Matrix4x4.identity;
                        if (Event.current.type == EventType.KeyDown) player.avatar.typingUntil = Time.time + 0.4f;
                        ideUi.HandleEvent(Event.current);
                    }
                    else DrawIdeOnMonitor();
                    GUI.matrix = baseMatrix; break;
                case Mode.Dialog: if (ui == null) DrawHud(W, H); DrawDialog(W, H); break;
                case Mode.Pause: if (ui == null) DrawPause(W, H); break;
                case Mode.Wardrobe: wardrobe.Draw(W, H); break;
            }
            if (toast != null && ui == null)
            {
                var sz = Ui.toast.CalcSize(new GUIContent(toast));
                GUI.Label(new Rect((W - sz.x) / 2, H - 120, sz.x, sz.y), toast, Ui.toast);
            }
            if (fadeAlpha > 0.001f)
            {
                GUI.matrix = Matrix4x4.identity; GUI.depth = -1000;
                GUI.color = new Color(0.05f, 0.06f, 0.17f, fadeAlpha);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        void DrawIdeOnMonitor()
        {
            if (Event.current.type == EventType.KeyDown) player.avatar.typingUntil = Time.time + 0.4f;
            var r = MonitorRect();
            if (r.height < 10) return;
            const float Hv = 820f;
            float s = r.height / Hv, Wv = r.width / s;
            float a = Mathf.Clamp01((Time.unscaledTime - ideShownAt) / 0.35f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(r.x, r.y, 0), Quaternion.identity, new Vector3(s, s, 1));
            GUI.color = new Color(1, 1, 1, a);
            ide.Draw(Wv, Hv);
            // блик «стекла» и вспышка включения экрана
            GUI.color = new Color(1, 1, 1, 0.06f * a);
            GUI.DrawTexture(new Rect(0, 0, Wv, Hv), Ui.glare);
            if (a < 1)
            {
                GUI.color = new Color(1, 1, 1, (1 - a) * 0.8f);
                float h = Hv * (0.02f + a * 0.98f);
                GUI.DrawTexture(new Rect(0, (Hv - h) / 2, Wv, h), Ui.White);
            }
            GUI.color = Color.white;
        }

        void DrawHud(float W, float H)
        {
            var cur = CurrentTask;
            GUI.Box(new Rect(16, 16, 420, 78), GUIContent.none, Ui.panel);
            GUI.Label(new Rect(30, 22, 400, 26), "<b>" + RankName + "</b>   Монеты: " + Save.money, Ui.body);
            GUI.Label(new Rect(30, 48, 400, 40), cur != null && !PathComplete ? "Задача: " + cur.key + " " + cur.title : "Направление пройдено!", Ui.small);
            if (bugs.Count > 0) GUI.Label(new Rect(30, 100, 400, 24), "<color=#FF4F9A>Багов в офисе: " + bugs.Count + "</color>", Ui.body);

            if (player.firstPerson)
                Ui.Fill(new Rect(W / 2 - 3, H / 2 - 3, 6, 6), focus != null ? Pal.Sun : new Color(1, 1, 1, 0.8f));
            if (focus != null && mode == Mode.Walk)
            {
                var sz = Ui.toast.CalcSize(new GUIContent(focus.Prompt));
                GUI.Label(new Rect((W - sz.x) / 2, H * 0.68f, sz.x, sz.y), focus.Prompt, Ui.toast);
            }
            if (Cursor.lockState != CursorLockMode.Locked && mode == Mode.Walk)
                GUI.Label(new Rect(0, H - 60, W, 30), "Кликни в окно игры, чтобы управлять мышью", Ui.center);
            GUI.Label(new Rect(W - 380, 18, 364, 80), "WASD — ходить, Shift — бег, Space — прыжок\nE — действие, V — вид от 1-го/3-го лица\nEsc — пауза", new GUIStyle(Ui.small) { alignment = TextAnchor.UpperRight });
        }

        void DrawDialog(float W, float H)
        {
            float w = Mathf.Min(720, W - 40), h = 200 + dlgButtons.Count * 46;
            var r = new Rect((W - w) / 2, H - h - 40, w, h);
            GUI.Box(r, GUIContent.none, Ui.panelLight);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 16, r.width - 40, r.height - 32));
            GUILayout.Label(dlgTitle, Ui.h3);
            GUILayout.Label(UiKit.Esc(dlgText), Ui.body);
            GUILayout.FlexibleSpace();
            foreach (var b in dlgButtons.ToList())
                if (GUILayout.Button(b.Key, b.Key == "Уйти" || b.Key.StartsWith("Понял") ? Ui.btnAlt : Ui.btn, GUILayout.Height(40))) b.Value();
            GUILayout.EndArea();
        }

        void DrawMenu(float W, float H)
        {
            Ui.Fill(new Rect(0, 0, Mathf.Min(720, W * 0.55f), H), new Color(0.106f, 0.122f, 0.29f, 0.78f));
            float w = Mathf.Min(560, W * 0.5f - 60), x = 50;
            GUILayout.BeginArea(new Rect(x, 90, w, H - 120));
            var title = new GUIStyle(Ui.h1) { fontSize = 84 };
            GUILayout.Label("Стажёр", title);
            GUILayout.Label("Тебя взяли на стажировку в «Кодзилла Софт». Решай тикеты, лови баги, расти до Junior+.", Ui.body);
            GUILayout.Space(24);
            if (Progress.HasSave() && Save.hasCharacter)
            {
                if (GUILayout.Button("Продолжить  (" + RankFull + ", " + DoneCount + "/" + TotalCount + ", " + Progress.DifficultyName((Difficulty)Save.difficulty) + ")", Ui.btn, GUILayout.Height(50)))
                    StartGame((Difficulty)Save.difficulty, false);
                GUILayout.Space(16);
                GUILayout.Label("Новая игра (прогресс сбросится):", Ui.small);
            }
            else GUILayout.Label("Выбери сложность:", Ui.h3);
            DiffButton(Difficulty.Easy, "Каждая строка объясняется, подсказки бесплатно, можно подсмотреть решение.");
            DiffButton(Difficulty.Medium, "Разбор строк — только в отладчике, подсказки за монеты. Награды ×1.2.");
            DiffButton(Difficulty.Hard, "Дедлайны, без теории и подсказок, ошибки без перевода. Награды ×1.6.");
            GUILayout.EndArea();
        }

        void DiffButton(Difficulty d, string desc)
        {
            GUILayout.Space(8);
            if (GUILayout.Button(Progress.DifficultyName(d), d == Difficulty.Easy ? Ui.btn : d == Difficulty.Medium ? Ui.btnAlt : Ui.btnDanger, GUILayout.Height(46)))
                StartGame(d, true);
            GUILayout.Label(desc, Ui.small);
        }

        void DrawPause(float W, float H)
        {
            Ui.Fill(new Rect(0, 0, W, H), new Color(0.106f, 0.122f, 0.29f, 0.7f));
            float w = 440, x = (W - w) / 2;
            GUILayout.BeginArea(new Rect(x, 150, w, 620));
            GUILayout.Label("Пауза", new GUIStyle(Ui.h1) { fontSize = 60 });
            if (GUILayout.Button("Продолжить", Ui.btn, GUILayout.Height(48))) Resume();
            GUILayout.Space(10);
            if (GUILayout.Button("Гардероб — сменить внешность", Ui.btnAlt, GUILayout.Height(44))) OpenWardrobe(false);
            if (GUILayout.Button(player.firstPerson ? "Камера: от первого лица" : "Камера: от третьего лица", Ui.btnAlt, GUILayout.Height(44)))
            { player.ToggleView(); Save.firstPerson = player.firstPerson; Persist(); }
            GUILayout.Space(14);
            GUILayout.Label("Сложность (можно менять в любой момент):", Ui.small);
            GUILayout.BeginHorizontal();
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
                if (GUILayout.Button(Progress.DifficultyName(d), Save.difficulty == (int)d ? Ui.btn : Ui.btnGhost, GUILayout.Height(40)))
                { Save.difficulty = (int)d; Persist(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(14);
            if (GUILayout.Button("В главное меню", Ui.btnAlt, GUILayout.Height(42))) { mode = Mode.Menu; player.cinematic = true; SetCursor(false); }
            if (GUILayout.Button("Выйти из игры", Ui.btnGhost, GUILayout.Height(42)))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            GUILayout.EndArea();
        }

        void OnApplicationQuit() { if (ideUi != null) ideUi.Close(); else ide.Close(); if (lunch != null) FinishLunchNow(); if (FiredReport == null) Persist(); }
        void OnDestroy() { if (ideUi != null) ideUi.Dispose(); if (ui != null) ui.Dispose(); }

        // ================== Рабочий день ==================
        void TickWorkday()
        {
            if (Work == null || FiredReport != null) return;
            if (InputX.AnyInput()) lastInput = Time.unscaledTime;
#if UNITY_EDITOR
            // только в редакторе: F3 — плюс игровой час, F2 — обеду осталось 5 секунд
            if (InputX.DebugHour() && mode != Mode.Menu && mode != Mode.Lunch && !Work.Ended) { Work.Advance(60f, true); Debug.Log("[Стажёр] F3: " + Work.Clock + ", штрафы " + Save.dayFines + ", выговоры " + Save.strikes); }
            if (InputX.DebugLunchEnd() && lunch != null) lunch.timeLeft = Mathf.Min(lunch.timeLeft, 5f);
            if (InputX.DebugDoor() && mode == Mode.Walk && exitSpot != null) { player.Teleport(exitSpot.position + Vector3.forward * 0.4f, 180f); player.FaceCameraYaw(180f); }
            if (InputX.DebugSit() && lunch != null && mode == Mode.Lunch)
            {
                var f = Quaternion.Euler(0, player.CamYaw, 0) * Vector3.forward;
                lunch.DebugPull(player.Position + f * 1.3f, player.CamYaw + 180f);
            }
#endif
            // часы идут в офисе, за компьютером, в разговоре и в гардеробе
            bool office = mode == Mode.Walk || mode == Mode.Ide || mode == Mode.Dialog || mode == Mode.Wardrobe || (mode == Mode.Transition && lunch == null);
            if (office && !Work.Ended) Work.Advance(Time.deltaTime / DayLength.SecondsPerGameMinute(GameConfig.S.dayLength), true);
            // автопауза: 2 минуты без ввода — часы и обед стоят
            if ((mode == Mode.Walk || mode == Mode.Ide || mode == Mode.Lunch) && Time.unscaledTime - lastInput > 120f)
            {
                PauseFrom(mode);
                Toast("Автопауза: 2 минуты без действий. Часы остановлены.");
                lastInput = Time.unscaledTime;
            }
        }

        // Работа из IDE: правки, запуски, проверки, подсказки
        public void ReportWork(WorkKind kind)
        {
            if (Work == null) return;
            if (kind == WorkKind.Edit)
            {
                float now = Time.unscaledTime, gap = Mathf.Min(now - lastEditAt, 5f);
                lastEditAt = now;
                if (gap > 0f) Work.Activity(WorkKind.Edit, gap);
                return;
            }
            Work.Activity(kind);
        }

        // Теория новой задачи: минута за открытой задачей засчитывается один раз
        void TrackTheory(float dt)
        {
            var t = ideUi != null ? ideUi.Task : ide.Task;
            if (t == null) return;
            if (t.id != ideTaskId) { ideTaskId = t.id; ideTaskTime = 0f; }
            ideTaskTime += dt;
            if (ideTaskTime >= 60f && !Save.done.Contains(t.id) && theoryCredited.Add(t.id)) Work.Activity(WorkKind.Theory);
        }

        void ShowDaySummary()
        {
            dayOverPending = false;
            LastReport = Work.Finish();
            if (FiredReport != null) return;      // уволили по итогам дня
            Work.NextDay();
            Persist(); UpdateBoard();
            mode = Mode.DaySummary; player.cinematic = true; SetCursor(false);
        }

        public void UiNextDay()
        {
            if (mode != Mode.DaySummary) return;
            player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.FaceCameraYaw(refs.spawn.eulerAngles.y);
            player.cinematic = false; player.BlendFromCurrent(0.8f);
            mode = Mode.Walk; SetCursor(true); lastInput = Time.unscaledTime;
            Toast(Work.WeekdayFull + ", 9:00. День " + Save.day + ". Гена ждёт тикеты!");
        }

        public void UiSummaryToMenu() { if (mode == Mode.DaySummary) { mode = Mode.Menu; player.cinematic = true; SetCursor(false); } }

        void OnFired()
        {
            FiredReport = new DayReport { day = Save.day, tasks = Save.done.Count, money = Save.money, kills = Save.totalKills, lunchMoney = Save.totalLunches, fines = Save.totalFines, strikes = Save.strikes, limit = Work.StrikeLimit, weekday = RankFull };
            if (mode == Mode.Ide) { if (ideUi != null) ideUi.Close(); else ide.Close(); }
            if (lunch != null) { lunch.Cleanup(); lunch = null; if (city != null) city.root.gameObject.SetActive(false); ShowKnife(false); }
            Progress.Wipe();
            dayOverPending = false;
            StartCoroutine(FiredScene());
        }

        IEnumerator FiredScene()
        {
            mode = Mode.Transition;
            yield return Fade(1f, 0.5f);
            if (player.avatar != null) player.avatar.React(4, 30f);
            mode = Mode.Fired; player.cinematic = true; SetCursor(false);
            yield return Fade(0f, 0.5f);
        }

        public void UiFiredNewGame()
        {
            if (mode != Mode.Fired) return;
            mode = Mode.Menu; player.cinematic = true; SetCursor(false);
            if (ui != null) ui.OpenNewGamePublic();
        }

        IEnumerator Fade(float to, float seconds)
        {
            float from = fadeAlpha;
            for (float t = 0; t < 1f; t += Time.unscaledDeltaTime / seconds) { fadeAlpha = Mathf.Lerp(from, to, t); yield return null; }
            fadeAlpha = to;
        }

        // ================== Дверь на обед ==================
        void PlaceExitDoor()
        {
            // Ищем глухую стену офиса со стороны входа (минимальный z) и ставим на неё дверь
            // у стены несколько слоёв (стена, панели, поручень, плинтус) — дверь ставим на самый внутренний
            var south = new List<Bounds>();
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if (!r.name.StartsWith("Wall") || b.size.x < 4f || b.size.z > 0.8f || b.center.z > 0f) continue;
                south.Add(b);
            }
            float wallZ = -6.95f, xMin = -10f, xMax = 10f; bool found = south.Count > 0;
            if (found)
            {
                float outer = south.Min(b => b.center.z);
                var layer = south.Where(b => b.center.z < outer + 0.4f).ToList();
                wallZ = layer.Max(b => b.max.z); xMin = layer.Max(b => b.min.x); xMax = layer.Min(b => b.max.x);
            }
            float x = Mathf.Clamp(-3.3f, xMin + 1.5f, xMax - 1.5f);
            var root = new GameObject("ExitDoor").transform;
            root.position = new Vector3(x, 0, wallZ + 0.02f);
            Look.RBox("Frame", root, new Vector3(0, 1.15f, 0.04f), new Vector3(1.5f, 2.3f, 0.08f), Pal.Hex("2B2D42"), 0.03f, false, 0.6f);
            Look.RBox("Leaf", root, new Vector3(0, 1.1f, 0.09f), new Vector3(1.2f, 2.15f, 0.05f), Pal.Hex("8C6A4F"), 0.02f, false, 0.6f);
            Look.RBox("Handle", root, new Vector3(0.45f, 1.05f, 0.14f), new Vector3(0.14f, 0.04f, 0.05f), Pal.Hex("D8DCE8"), 0.01f, false, 0.4f);
            Look.RBox("ExitSign", root, new Vector3(0, 2.52f, 0.06f), new Vector3(0.8f, 0.26f, 0.06f), Pal.Hex("3E9B5A"), 0.02f, false, 0.4f, 1.2f);
            OfficeBuilder.Label("ВЫХОД", new Vector3(0, 2.52f, 0.1f), 0.011f, Color.white, root, 180f);
            OfficeBuilder.Label("Обед\n12:00–16:00", new Vector3(0, 1.6f, 0.13f), 0.009f, Pal.Hex("F4F1EA"), root, 180f);
            var hit = new GameObject("ExitDoorZone"); hit.transform.SetParent(root, false);
            hit.transform.localPosition = new Vector3(0, 1.1f, 0.35f);
            var col = hit.AddComponent<BoxCollider>(); col.size = new Vector3(1.4f, 2.2f, 0.6f); col.isTrigger = true;
            hit.AddComponent<ExitDoor>().game = this;
            exitSpot = new GameObject("ExitSpot").transform;
            exitSpot.position = new Vector3(x, 0.1f, wallZ + 1.4f);
            Debug.Log("[Стажёр] Дверь на обед: x " + x.ToString("0.0") + ", стена z " + wallZ.ToString("0.00") + (found ? "" : " (стена не найдена, запасное место)"));
        }

        public void TryStartLunch()
        {
            if (mode != Mode.Walk) return;
            string why;
            if (!Work.CanLunch(out why)) { Toast(why); return; }
            Work.StartLunch(); Persist();
            if (FiredReport != null) return;      // самоволка оказалась последней каплей
            StartCoroutine(ToCity());
        }

        IEnumerator ToCity()
        {
            mode = Mode.Transition;
            yield return Fade(1f, 0.35f);
            if (city == null) city = CityBuilder.Build();
            city.root.gameObject.SetActive(true);
            Gore.Enabled = GameConfig.S.blood;
            lunch = new LunchRun(city, DayLength.LunchSeconds(GameConfig.S.dayLength), () => player.Position) { Say = Toast };
            player.Teleport(city.spawn.position, 0f); player.FaceCameraYaw(0f);
            player.cinematic = false; player.avatar.SetHeadVisible(!player.firstPerson);
            ShowKnife(true);
            mode = Mode.Lunch; SetCursor(true); lastInput = Time.unscaledTime;
            Toast("Обед! " + Mathf.RoundToInt(lunch.duration / 60f) + " минут. Юрист +10 монет, бухгалтеров не трогать.");
            yield return Fade(0f, 0.35f);
        }

        void ShowKnife(bool on)
        {
            if (on) { if (knife == null) knife = new Knife(); knife.Attach(player.avatar); }
            if (knife != null) knife.Show(on);
            if (player.avatar != null) player.avatar.holdRight = on;
        }

        public void EndLunch(bool timeUp)
        {
            if (mode != Mode.Lunch || lunch == null) return;
            StartCoroutine(ToOffice(timeUp));
        }

        IEnumerator ToOffice(bool timeUp)
        {
            mode = Mode.Transition;
            yield return Fade(1f, 0.35f);
            int coins = lunch.coins, kills = lunch.kills, fines = lunch.fines;
            FinishLunchNow();
            player.cinematic = false; player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.Walk; SetCursor(true); lastInput = Time.unscaledTime;
            Toast((timeUp ? "Обед закончился. " : "") + "За обед " + (coins >= 0 ? "+" : "") + coins + " монет, выбито " + kills + (fines > 0 ? ", штрафы −" + fines : "") + ". «Сытый»: +10% XP до " + WorkDay.TimeText(Save.satedUntil) + ".");
            yield return Fade(0f, 0.35f);
        }

        // Закончить обед сразу (конец таймера, выход в меню или из игры): монеты — в кошелёк, стажёр — в офис
        void FinishLunchNow()
        {
            if (lunch == null) return;
            Save.money = Mathf.Max(0, Save.money + lunch.coins);
            Work.EndLunch(lunch.coins, lunch.kills);
            lunch.Cleanup(); lunch = null;
            if (city != null) city.root.gameObject.SetActive(false);
            ShowKnife(false);
            if (exitSpot != null) player.Teleport(exitSpot.position, 0f); else player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.FaceCameraYaw(0f);
            Persist(); UpdateBoard();
        }

        void Attack()
        {
            if (knife == null || !knife.Ready || player.avatar == null) return;
            knife.Used();
            player.FaceYaw(player.CamYaw);
            player.avatar.swingStart = Time.time;
            StartCoroutine(HitAfter(0.12f));
        }

        IEnumerator HitAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (lunch == null) yield break;
            var fwd = Quaternion.Euler(0, player.CamYaw, 0) * Vector3.forward;
            var c = player.Position + Vector3.up * 1.0f + fwd * Knife.Reach;
            CityNpc best = null; float bd = 99f;
            foreach (var col in Physics.OverlapSphere(c, Knife.Radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var n = col.GetComponentInParent<CityNpc>();
                if (n == null || !n.Alive) continue;
                var to = n.transform.position - player.Position; to.y = 0;
                float d = to.magnitude;
                if (d < bd && (d < 0.5f || Vector3.Dot(to / d, fwd) > 0.1f)) { bd = d; best = n; }
            }
            if (best != null) best.Hit(Knife.Damage, player.Position);
        }

        // ================== Для интерфейса (GameUi) ==================
        public Mode CurMode { get { return mode; } }
        public bool HasProgress { get { return Progress.HasSave() && Save.hasCharacter; } }
        public int DoneCount { get { return Path.DoneCount(Done); } }
        public int TotalCount { get { return Tasks.tasks.Length; } }
        public int BugCount { get { return bugs.Count; } }
        public bool FirstPerson { get { return player != null && player.firstPerson; } }
        public string FocusPrompt { get { return focus != null && mode == Mode.Walk ? focus.Prompt : null; } }
        public string ToastText { get { return toast; } }
        public float ToastAge { get { return Time.unscaledTime - (toastUntil - 3.2f); } }
        public float ToastLeft { get { return toastUntil - Time.unscaledTime; } }
        public string TaskCodeOf(TaskData t) { return t == null ? "" : !string.IsNullOrEmpty(t.key) ? t.key : "KOD-" + (101 + Mathf.Max(0, Array.IndexOf(Tasks.tasks, t))); }
        public void UiContinue() { StartGame((Difficulty)Save.difficulty, false); }
        public void UiNewGame(Difficulty d) { StartGame(d, true, "backend"); }
        public void UiNewGame(Difficulty d, string profession) { StartGame(d, true, profession); }

        // Смена направления посреди игры: сданные задачи (и общая база) остаются засчитанными
        public bool UiSetProfession(string p)
        {
            if (p == Profession || !Career.CanPick(p)) return false;
            if (ideUi != null && ideUi.Task != null) ideUi.Close();
            Save.profession = p; Persist();
            LoadPath(); UpdateBoard();
            ide = new IdeWindow(this);
            if (ideUi != null && CurrentTask != null) ideUi.Open(CurrentTask);
            Toast("Направление: " + ProfessionName + " · " + RankName + ". Сдано " + DoneCount + " из " + TotalCount + ".");
            return true;
        }
        public void UiResume() { if (mode == Mode.Pause) Resume(); }
        public void UiWardrobe() { OpenWardrobe(false); }
        public void UiToggleView() { player.ToggleView(); Save.firstPerson = player.firstPerson; Persist(); }
        public void UiSetDifficulty(Difficulty d) { Save.difficulty = (int)d; Persist(); }
        public void UiToMenu()
        {
            if (lunch != null) FinishLunchNow();
            mode = Mode.Menu; player.cinematic = true; SetCursor(false);
        }
        public void UiQuit()
        {
            Persist(); GameConfig.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // Точка на экране (координаты OnGUI) → точка на панели IDE: луч из камеры в плоскость экрана монитора
        // Экран IDE: прямоугольник перед корпусом монитора (из офиса Blender — готовый, иначе строим сами)
        void BuildIdeQuad(Material mat)
        {
            if (refs.screenQuad != null)
            {
                ideQuad = refs.screenQuad;
                ideQuad.GetComponent<Renderer>().sharedMaterial = mat;
            }
            else
            {
                var src = refs.screen.GetComponent<Renderer>();
                if (src == null) return;
                ideQuad = ModelLib.FrontQuad(src, SeatPos + Vector3.up * refs.screen.position.y, null, mat, refs.screen.root.GetComponentsInChildren<Renderer>());
            }
            ideQuadW = ideQuad.lossyScale.x; ideQuadH = ideQuad.lossyScale.y;
            Debug.Log("[Стажёр] Экран IDE: " + ideQuadW.ToString("0.000") + "×" + ideQuadH.ToString("0.000") + " м");
        }

        Vector2? ScreenToMonitor(Vector2 gui)
        {
            if (ideQuad != null && player != null && player.cam != null && ideUi != null)
            {
                var qray = player.cam.ScreenPointToRay(new Vector3(gui.x, Screen.height - gui.y, 0));
                var qplane = new Plane(-ideQuad.forward, ideQuad.position);
                float qd;
                if (!qplane.Raycast(qray, out qd)) return null;
                var ql = qray.GetPoint(qd) - ideQuad.position;
                float qu = Vector3.Dot(ql, ideQuad.right) / ideQuadW + 0.5f, qv = Vector3.Dot(ql, ideQuad.up) / ideQuadH + 0.5f;
                if (qu < -0.05f || qu > 1.05f || qv < -0.05f || qv > 1.05f) return null;
                return new Vector2(qu * ideUi.PanelWidth, (1 - qv) * ideUi.PanelHeight);
            }
            if (refs == null || refs.screen == null || player == null || player.cam == null || ideUi == null) return null;
            var scr = refs.screen;
            var mf = scr.GetComponent<MeshFilter>();
            Bounds b = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(refs.screenSize.x, refs.screenSize.y, 0.001f));
            bool wideX = b.size.x >= b.size.z;
            Vector3 c = scr.TransformPoint(b.center);
            Vector3 axW = scr.TransformVector(wideX ? new Vector3(b.size.x, 0, 0) : new Vector3(0, 0, b.size.z));
            Vector3 axH = scr.TransformVector(new Vector3(0, b.size.y, 0));
            var cam = player.cam;
            if (Vector3.Dot(axW, cam.transform.right) < 0) axW = -axW;
            if (Vector3.Dot(axH, Vector3.up) < 0) axH = -axH;
            var ray = cam.ScreenPointToRay(new Vector3(gui.x, Screen.height - gui.y, 0));
            var plane = new Plane(Vector3.Cross(axW, axH).normalized, c);
            float d;
            if (!plane.Raycast(ray, out d)) return null;
            var local = ray.GetPoint(d) - c;
            float u = Vector3.Dot(local, axW) / axW.sqrMagnitude + 0.5f, v = Vector3.Dot(local, axH) / axH.sqrMagnitude + 0.5f;
            if (u < -0.05f || u > 1.05f || v < -0.05f || v > 1.05f) return null;
            return new Vector2(u * ideUi.PanelWidth, (1 - v) * ideUi.PanelHeight);
        }
    }
}
