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
        enum Mode { Menu, Walk, Transition, Ide, Dialog, Pause, Wardrobe }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GameRoot>() != null) return;
            new GameObject("InternGame").AddComponent<GameRoot>();
        }

        public SaveData Save;
        public TaskFile Tasks;
        public UiKit Ui;
        IdeWindow ide;          // старая IDE на IMGUI — запасной вариант
        IdeScreen ideUi;        // новая IDE на UI Toolkit, рисуется на экране монитора
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

        // тосты
        readonly Queue<string> toasts = new Queue<string>();
        string toast; float toastUntil;

        public string RankName { get { return Progress.Rank(Save.done.Count, Tasks.tasks.Length); } }

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
            Tasks = Progress.LoadTasks("python");
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
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        public void Persist() { Progress.Save(Save); }

        public bool IsUnlocked(int i)
        {
            return i == 0 || Save.done.Contains(Tasks.tasks[i - 1].id) || Save.done.Contains(Tasks.tasks[i].id);
        }

        public TaskData CurrentTaskPublic { get { return CurrentTask; } }

        TaskData CurrentTask
        {
            get
            {
                for (int i = 0; i < Tasks.tasks.Length; i++)
                    if (!Save.done.Contains(Tasks.tasks[i].id)) return Tasks.tasks[i];
                return Tasks.tasks.Length > 0 ? Tasks.tasks[Tasks.tasks.Length - 1] : null;
            }
        }

        void UpdateBoard()
        {
            int done = Save.done.Count, total = Tasks.tasks.Length;
            var cur = CurrentTask;
            refs.board.text = "СПРИНТ 1: Python\n\nСделано: " + done + " / " + total +
                         (cur != null && done < total ? "\nСейчас: " + cur.title : "\nГлава пройдена!") +
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
                    if (InputX.Esc()) { mode = Mode.Pause; SetCursor(false); }
                    break;
                case Mode.Ide:
                    if (ideUi != null) ideUi.Tick(Time.deltaTime); else ide.Tick(Time.deltaTime);
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
                    if (InputX.Esc()) Resume();
                    break;
            }
            if (toast == null || Time.unscaledTime > toastUntil)
            {
                toast = toasts.Count > 0 ? toasts.Dequeue() : null;
                if (toast != null) toastUntil = Time.unscaledTime + 3.2f;
            }
            bugs.RemoveAll(b => b == null);
            if (ideUi != null) ideUi.Update(Time.deltaTime);
#if UNITY_EDITOR
            if (ideUi != null && InputX.DebugDump()) { Debug.Log("[Стажёр] F7: mode=" + mode + ", ideActive=" + ideUi.Active + ", task=" + (ideUi.Task != null ? ideUi.Task.id : "-")); ideUi.DebugDump(System.IO.Path.GetFullPath(Application.dataPath + "/../Temp")); Toast("Снимок IDE сохранён"); }
#endif
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

        void Resume() { mode = Mode.Walk; SetCursor(true); }

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
            string oldRank = RankName;
            float mult = 1f;
            var diff = (Difficulty)Save.difficulty;
            if (diff == Difficulty.Medium) mult *= 1.2f;
            if (diff == Difficulty.Hard) mult *= late ? 0.5f : 1.6f;
            if (usedSolution) mult *= 0.5f;
            int reward = Mathf.RoundToInt(t.reward * mult);
            Save.money += reward; Save.done.Add(t.id);
            Persist(); UpdateBoard();
            Toast("Задача сдана! +" + reward + " монет" + (late ? " (дедлайн сорван)" : ""));
            if (player.avatar != null) player.avatar.React(2, 3f); // восторг
            if (RankName != oldRank) Toast("ПОВЫШЕНИЕ! Теперь ты " + RankName + ". Загляни в гардероб — там кое-что новое.");
            var next = CurrentTask;
            if (next != null && next != t && !Save.done.Contains(next.id)) Toast("Новая задача: " + next.title);
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
            int done = Save.done.Count, total = Tasks.tasks.Length;
            string text;
            if (done == 0)
                text = "О, новенький! Я Гена. Твой стол — тот, где уточка на мониторе. Садись, открывай первый тикет.\n\n" +
                       "Главное правило: если непонятно, что делает код, жми «Отладка» и проходи его по шагам. Так учатся все, даже сеньоры.";
            else if (done >= total)
                text = "Ты закрыл весь спринт по Python. Теперь ты " + RankName + " — и это заслуженно.\n\n" +
                       "Дальше будут главы про классы, файлы, тесты, Git и SQL. А пока — перерешай задачи на тяжёлой сложности, это отличная тренировка.";
            else
                text = "Ты сейчас " + RankName + ", сделано " + done + " из " + total + ". Следующая задача: «" + CurrentTask.title + "».\n\n" + Tip(done);
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

        void StartGame(Difficulty d, bool fresh)
        {
            if (fresh)
            {
                Progress.Wipe(); Save = new SaveData(); ide = new IdeWindow(this); if (ideUi != null) ideUi.ResetProgress(CurrentTask);
                foreach (var b in bugs) if (b != null) Destroy(b.gameObject);
                player.SetAvatar(Save.look);
            }
            Save.difficulty = (int)d; Persist(); UpdateBoard();
            if (fresh || !Save.hasCharacter) { OpenWardrobe(true); return; }
            player.Teleport(refs.spawn.position, refs.spawn.eulerAngles.y);
            player.FaceCameraYaw(refs.spawn.eulerAngles.y);
            player.cinematic = false;
            player.BlendFromCurrent(1.0f);
            player.avatar.SetHeadVisible(!player.firstPerson);
            mode = Mode.Walk; SetCursor(true);
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
                case Mode.Menu: DrawMenu(W, H); break;
                case Mode.Walk: DrawHud(W, H); break;
                case Mode.Ide:
                    if (ideUi != null)
                    {
                        GUI.matrix = Matrix4x4.identity;
                        if (Event.current.type == EventType.KeyDown) player.avatar.typingUntil = Time.time + 0.4f;
                        ideUi.HandleEvent(Event.current);
                    }
                    else DrawIdeOnMonitor();
                    GUI.matrix = baseMatrix; break;
                case Mode.Dialog: DrawHud(W, H); DrawDialog(W, H); break;
                case Mode.Pause: DrawPause(W, H); break;
                case Mode.Wardrobe: wardrobe.Draw(W, H); break;
            }
            if (toast != null)
            {
                var sz = Ui.toast.CalcSize(new GUIContent(toast));
                GUI.Label(new Rect((W - sz.x) / 2, H - 120, sz.x, sz.y), toast, Ui.toast);
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
            GUI.Label(new Rect(30, 48, 400, 40), cur != null && Save.done.Count < Tasks.tasks.Length ? "Задача: " + cur.title : "Спринт закрыт!", Ui.small);
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
                if (GUILayout.Button("Продолжить  (" + RankName + ", " + Save.done.Count + "/" + Tasks.tasks.Length + ", " + Progress.DifficultyName((Difficulty)Save.difficulty) + ")", Ui.btn, GUILayout.Height(50)))
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

        void OnApplicationQuit() { if (ideUi != null) ideUi.Close(); else ide.Close(); Persist(); }
        void OnDestroy() { if (ideUi != null) ideUi.Dispose(); }

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
