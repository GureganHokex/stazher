// Тимлид Гена ходит по офису: подходит к стажёру, говорит (облачко над головой) и возвращается к доске.
// Путь ищется по сетке проходимости офиса: ячейка свободна, если в неё помещается человек.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    // Сетка проходимости офиса и поиск пути (Дейкстра по 8 соседям)
    public class OfficeGrid
    {
        public const float Cell = 0.3f;
        readonly float x0, z0;
        readonly int w, h;
        readonly bool[] free;
        float[] dist; int[] parent;

        public int FreeCount { get; private set; }

        public OfficeGrid(Rect area, Collider ignore)
        {
            x0 = area.xMin; z0 = area.yMin;
            w = Mathf.CeilToInt(area.width / Cell); h = Mathf.CeilToInt(area.height / Cell);
            free = new bool[w * h];
            bool was = ignore != null && ignore.enabled;
            if (was) ignore.enabled = false;
            Physics.SyncTransforms();
            int mask = Physics.DefaultRaycastLayers & ~(1 << 2);   // слой 2 — игрок
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    var p = World(x, z);
                    bool ok = !Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.26f, mask, QueryTriggerInteraction.Ignore);
                    // под ногами должен быть пол
                    if (ok) ok = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 0.6f, mask, QueryTriggerInteraction.Ignore);
                    free[z * w + x] = ok;
                    if (ok) FreeCount++;
                }
            if (was) ignore.enabled = true;
        }

        Vector3 World(int x, int z) { return new Vector3(x0 + (x + 0.5f) * Cell, 0f, z0 + (z + 0.5f) * Cell); }
        public Vector3 World(int i) { return World(i % w, i / w); }
        int Index(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - x0) / Cell), z = Mathf.FloorToInt((p.z - z0) / Cell);
            if (x < 0 || z < 0 || x >= w || z >= h) return -1;
            return z * w + x;
        }
        bool Free(int x, int z) { return x >= 0 && z >= 0 && x < w && z < h && free[z * w + x]; }
        public bool FreeAt(Vector3 p) { int i = Index(p); return i >= 0 && free[i]; }

        // Ближайшая свободная ячейка к точке
        public int NearestFree(Vector3 p, float maxDist)
        {
            int best = -1; float bd = maxDist * maxDist;
            int r = Mathf.CeilToInt(maxDist / Cell);
            int cx = Mathf.FloorToInt((p.x - x0) / Cell), cz = Mathf.FloorToInt((p.z - z0) / Cell);
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!Free(x, z)) continue;
                    var q = World(x, z); float d = (q.x - p.x) * (q.x - p.x) + (q.z - p.z) * (q.z - p.z);
                    if (d < bd) { bd = d; best = z * w + x; }
                }
            return best;
        }

        // Расстояния от старта до всех достижимых ячеек
        public bool Flood(Vector3 from)
        {
            int n = w * h;
            if (dist == null) { dist = new float[n]; parent = new int[n]; }
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; parent[i] = -1; }
            int s = FreeAt(from) ? Index(from) : NearestFree(from, 1.5f);
            if (s < 0) return false;
            var heap = new MinHeap(); dist[s] = 0f; heap.Push(0f, s);
            while (heap.Count > 0)
            {
                float d; int i = heap.Pop(out d);
                if (d > dist[i]) continue;
                int x = i % w, z = i / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = x + dx, nz = z + dz;
                        if (!Free(nx, nz)) continue;
                        if (dx != 0 && dz != 0 && (!Free(x + dx, z) || !Free(x, z + dz))) continue;   // не срезаем углы
                        float nd = d + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        int j = nz * w + nx;
                        if (nd < dist[j]) { dist[j] = nd; parent[j] = i; heap.Push(nd, j); }
                    }
            }
            return true;
        }

        public bool Reachable(int i) { return i >= 0 && dist != null && dist[i] < float.MaxValue; }
        public float Cost(int i) { return Reachable(i) ? dist[i] * Cell : float.MaxValue; }

        // Место рядом со стажёром: на расстоянии разговора, достижимое, поближе к Гене
        // view — куда смотрит камера игрока: Гена встаёт перед ним, чтобы было видно облачко
        public int SpotNear(Vector3 target, Vector3 view, float want = 1.6f)
        {
            view.y = 0; bool hasView = view.sqrMagnitude > 0.01f; if (hasView) view.Normalize();
            int best = -1; float bs = float.MaxValue;
            int r = Mathf.CeilToInt(2.2f / Cell);
            int cx = Mathf.FloorToInt((target.x - x0) / Cell), cz = Mathf.FloorToInt((target.z - z0) / Cell);
            for (int z = cz - r; z <= cz + r; z++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!Free(x, z)) continue;
                    int i = z * w + x;
                    if (!Reachable(i)) continue;
                    var q = World(x, z); q.y = 0; var t = target; t.y = 0;
                    float d = Vector3.Distance(q, t);
                    if (d < 0.75f || d > 2.2f) continue;
                    float score = Mathf.Abs(d - want) * 3f + Cost(i) * 0.02f;
                    if (hasView) score += (1f - Vector3.Dot((q - t) / d, view)) * 1.5f;
                    if (score < bs) { bs = score; best = i; }
                }
            return best;
        }

        // Путь до ячейки: точки по прямым участкам, без лишних изломов
        public List<Vector3> PathTo(int goal, Vector3 from)
        {
            var cells = new List<Vector3>();
            if (!Reachable(goal)) return cells;
            for (int i = goal; i >= 0; i = parent[i]) cells.Add(World(i));
            cells.Reverse();
            var pts = new List<Vector3>();
            var cur = from; cur.y = 0;
            int k = 0;
            while (k < cells.Count)
            {
                int far = k;
                for (int j = cells.Count - 1; j > k; j--) if (Line(cur, cells[j])) { far = j; break; }
                pts.Add(cells[far]); cur = cells[far]; k = far + 1;
            }
            return pts;
        }

        // Прямая проходима: все ячейки вдоль отрезка свободны
        public bool Line(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0; float len = Vector3.Distance(a, b);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.4f)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector3.Lerp(a, b, (float)i / steps);
                if (!FreeAt(p)) return i == 0;   // сама стартовая точка может стоять впритык к препятствию
            }
            return true;
        }

        class MinHeap
        {
            readonly List<float> k = new List<float>(); readonly List<int> v = new List<int>();
            public int Count { get { return k.Count; } }
            public void Push(float key, int val)
            {
                k.Add(key); v.Add(val); int i = k.Count - 1;
                while (i > 0) { int p = (i - 1) / 2; if (k[p] <= k[i]) break; Swap(i, p); i = p; }
            }
            public int Pop(out float key)
            {
                key = k[0]; int res = v[0], last = k.Count - 1;
                k[0] = k[last]; v[0] = v[last]; k.RemoveAt(last); v.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < k.Count && k[l] < k[m]) m = l;
                    if (r < k.Count && k[r] < k[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return res;
            }
            void Swap(int a, int b) { float t = k[a]; k[a] = k[b]; k[b] = t; int u = v[a]; v[a] = v[b]; v[b] = u; }
        }
    }

    // Облачко с репликой над головой: всегда лицом к камере
    public class SpeechBubble : MonoBehaviour
    {
        TextMesh text;
        GameObject box, tail;
        MeshRenderer textR;
        string shown;
        bool sized;
        Transform owner;
        float headY, width, height;
        int side = 1;                    // 1 — справа от головы, -1 — слева
        Color fill = Color.white;

        // Облачко сбоку от головы (как в комиксе): так его видно, даже когда Гена стоит вплотную
        public static SpeechBubble Create(Transform owner, float headHeight)
        {
            var node = new GameObject("SpeechBubble");
            node.transform.SetParent(owner, false);
            node.AddComponent<Billboard>();
            var b = node.AddComponent<SpeechBubble>();
            b.owner = owner; b.headY = headHeight;
            b.text = OfficeBuilder.Label("", new Vector3(0, 0, -0.07f), 0.0082f, Pal.Ink, node.transform);
            b.text.transform.localRotation = Quaternion.identity;
            b.textR = b.text.GetComponent<MeshRenderer>();
            node.SetActive(false);
            return b;
        }

        public void Show(string line, Color bg)
        {
            gameObject.SetActive(true);
            shown = Wrap(line, 30); fill = bg;
            text.text = shown; sized = false;
        }

        public void Hide() { gameObject.SetActive(false); }

        void LateUpdate()
        {
            Place();
            if (sized || textR == null) return;
            // размер текста известен после генерации сетки — подгоняем под него фон
            var b = textR.localBounds.size;
            if (b.x < 0.01f)
            {
                int lines = shown.Split('\n').Length, len = 0;
                foreach (var l in shown.Split('\n')) len = Mathf.Max(len, l.Length);
                b = new Vector3(len * 0.028f, lines * 0.06f, 0);
            }
            var s = text.transform.localScale;
            float bw = Mathf.Round((b.x * s.x + 0.22f) * 20f) / 20f, bh = Mathf.Round((b.y * s.y + 0.16f) * 20f) / 20f;
            if (box != null) Destroy(box);
            if (tail != null) Destroy(tail);
            box = Look.RBox("Bubble", transform, Vector3.zero, new Vector3(bw, bh, 0.1f), fill, 0.05f, false, 0.7f, 0.55f, false);
            tail = Look.RBox("Tail", transform, new Vector3(-bw * 0.5f, -bh * 0.25f, 0.01f), new Vector3(0.16f, 0.16f, 0.08f), fill, 0.02f, false, 0.7f, 0.55f, false);
            tail.transform.localRotation = Quaternion.Euler(0, 0, 45f);
            text.transform.localPosition = new Vector3(0, 0, -0.07f);
            width = bw; height = bh; sized = true;
            Place();
        }

        // справа от головы Гены с точки зрения камеры
        void Place()
        {
            var cam = Camera.main; if (cam == null || owner == null) return;
            var right = cam.transform.right; right.y = 0; right.Normalize();
            var head = owner.position + Vector3.up * headY;
            // облачко с той стороны, где больше места на экране
            float vx = cam.WorldToViewportPoint(head).x;
            if (side > 0 && vx > 0.6f) side = -1; else if (side < 0 && vx < 0.4f) side = 1;
            transform.position = head + right * side * (width * 0.5f + 0.28f);
            if (tail != null) tail.transform.localPosition = new Vector3(-side * width * 0.5f, -height * 0.25f, 0.01f);
        }

        public static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder(); int col = 0;
            foreach (var word in s.Split(' '))
            {
                if (col > 0 && col + 1 + word.Length > width) { sb.Append('\n'); col = 0; }
                else if (col > 0) { sb.Append(' '); col++; }
                sb.Append(word); col += word.Length;
            }
            return sb.ToString();
        }
    }

    // Гена: стоит у доски, подходит к стажёру по делу, говорит и уходит обратно
    public class LeadWalker : MonoBehaviour
    {
        enum St { Home, ToPlayer, Talking, Listening, Back }

        CharacterAnim anim;
        Collider body;
        Vector3 home; float homeYaw;
        Func<Vector3> playerPos, viewDir;
        Func<bool> active;
        Action<string> say;              // текст реплики — в уведомление (видно и за компьютером)
        Action<string> sayFar;           // не дошёл — крикнул издалека
        SpeechBubble bubble;
        GameObject nameTag;

        St st = St.Home;
        OfficeGrid grid;
        List<Vector3> path = new List<Vector3>();
        int pathIdx;
        float yaw, walkTime, replanIn, lineLeft, pauseLeft, waitLeft;   // waitLeft — ждёт ответа стажёра (E), не уходит
        Vector3 aimAt;                   // где был стажёр, когда строили путь
        readonly List<KeyValuePair<LeadMood, string>> lines = new List<KeyValuePair<LeadMood, string>>();
        LeadMood mood;

        public const float Speed = 2.1f, GiveUp = 22f;
        public bool Busy { get { return st != St.Home; } }
        public string State { get { return st.ToString(); } }

        public void Init(CharacterAnim a, Func<Vector3> player, Func<Vector3> view, Func<bool> isActive, Action<string> onSay, Action<string> onFar)
        {
            viewDir = view;
            anim = a; body = GetComponent<Collider>();
            if (GetComponent<Rigidbody>() == null) { var rb = gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; }
            home = transform.position; homeYaw = transform.eulerAngles.y; yaw = homeYaw;
            playerPos = player; active = isActive; say = onSay; sayFar = onFar;
            bubble = SpeechBubble.Create(transform, 1.75f);
            // табличка «Тимлид Гена» прячется, пока над головой облачко
            foreach (var tm in GetComponentsInChildren<TextMesh>(true)) if (tm.text == "Тимлид Гена") nameTag = tm.gameObject;
        }

        // Подойти к стажёру и сказать. Если уже идёт или говорит — реплика встаёт в очередь.
        // wait > 0 — после реплики ждать, пока стажёр заговорит (E), столько секунд
        public void Visit(LeadMood m, string text, float wait = 0f)
        {
            lines.Add(new KeyValuePair<LeadMood, string>(m, text));
            waitLeft = Mathf.Max(waitLeft, wait);
            if (st == St.ToPlayer || st == St.Talking || st == St.Listening) return;
            Plan();
        }

        // Стажёр сам заговорил с Геной: он останавливается и отдаёт всё, что хотел сказать
        public List<string> Interrupt()
        {
            var res = new List<string>();
            foreach (var l in lines) res.Add(l.Value);
            lines.Clear(); waitLeft = 0f;
            st = St.Listening; path.Clear(); HideBubble();
            if (anim != null) anim.moveSpeed = 0f;
            FacePlayer(true);
            return res;
        }

        public void GoHome()
        {
            HideBubble();
            if (lines.Count > 0) { Plan(); return; }
            var d = transform.position - home; d.y = 0;
            if (d.magnitude < 0.5f) { st = St.Home; return; }
            PlanHome();
        }

        // Мгновенно к доске (новый день, обед, новая игра)
        public void ResetHome()
        {
            lines.Clear(); path.Clear(); HideBubble(); waitLeft = 0f;
            transform.position = home; yaw = homeYaw; transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (anim != null) anim.moveSpeed = 0f;
            st = St.Home;
        }

        void BuildGrid()
        {
            grid = new OfficeGrid(new Rect(-12f, -8f, 24f, 16f), body);
        }

        void Plan(bool rebuild = true)
        {
            if (rebuild || grid == null) BuildGrid();
            if (!grid.Flood(transform.position)) { Far(); return; }
            aimAt = playerPos();
            int spot = grid.SpotNear(aimAt, viewDir != null ? viewDir() : Vector3.zero);
            if (spot < 0) { Far(); return; }
            path = grid.PathTo(spot, transform.position); pathIdx = 0;
            if (st != St.ToPlayer) Log("идёт к стажёру: свободных ячеек " + grid.FreeCount + ", точек пути " + path.Count + ", до места " + grid.Cost(spot).ToString("0.0") + " м");
            st = St.ToPlayer; walkTime = 0f; replanIn = 1f;
            if (lines.Count > 0 && anim != null) anim.React(Emotion(lines[0].Key), 2f);
            if (path.Count == 0) Arrive();
        }

        void PlanHome()
        {
            if (grid == null) BuildGrid();
            if (!grid.Flood(transform.position)) { ResetHome(); return; }
            int goal = grid.NearestFree(home, 1.2f);
            path = grid.PathTo(goal, transform.position); pathIdx = 0;
            if (path.Count > 0) path.Add(home);
            st = St.Back;
            if (path.Count == 0) ResetHome();
        }

        // До стажёра не дойти — говорит издалека
        void Far()
        {
            Log("не дошёл, говорит издалека");
            foreach (var l in lines) if (sayFar != null) sayFar(l.Value);
            lines.Clear();
            PlanHome();
        }

        void Arrive()
        {
            Log("подошёл за " + walkTime.ToString("0.0") + " с, реплик " + lines.Count);
            st = St.Talking; path.Clear();
            if (anim != null) anim.moveSpeed = 0f;
            lineLeft = 0f; pauseLeft = 0f;
            NextLine();
        }

        void NextLine()
        {
            if (lines.Count == 0) { if (waitLeft <= 0f) HideBubble(); pauseLeft = 0.8f; return; }
            var l = lines[0]; lines.RemoveAt(0);
            mood = l.Key;
            lineLeft = Mathf.Clamp(2.5f + l.Value.Length * 0.045f, 4f, 9f);
            bubble.Show(l.Value, BubbleColor(mood));
            if (nameTag != null) nameTag.SetActive(false);
            if (anim != null)
            {
                anim.React(Emotion(mood), lineLeft);
                if (mood == LeadMood.Praise) anim.Wave();
            }
            if (say != null) say(l.Value);
        }

        void HideBubble() { bubble.Hide(); if (nameTag != null) nameTag.SetActive(true); }

        static int Emotion(LeadMood m) { return m == LeadMood.Praise || m == LeadMood.Info ? 1 : m == LeadMood.Warn ? 3 : 5; }
        static Color BubbleColor(LeadMood m)
        {
            return m == LeadMood.Info ? Pal.Hex("DCEBFF") : m == LeadMood.Praise ? Pal.Hex("D8F7E4") : m == LeadMood.Warn ? Pal.Hex("FFF3C4") : m == LeadMood.Fine ? Pal.Hex("FFE3EA") : Pal.Hex("FFC2D4");
        }

        void FacePlayer(bool instant)
        {
            var to = playerPos() - transform.position; to.y = 0;
            if (to.sqrMagnitude < 0.01f) return;
            float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            yaw = instant ? want : Mathf.MoveTowardsAngle(yaw, want, 360f * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        void Update()
        {
            if (active != null && !active()) { if (anim != null) anim.moveSpeed = 0f; return; }
            float dt = Time.deltaTime;
            switch (st)
            {
                case St.Home:
                    yaw = Mathf.MoveTowardsAngle(yaw, homeYaw, 240f * dt); transform.rotation = Quaternion.Euler(0, yaw, 0);
                    break;
                case St.ToPlayer:
                    walkTime += dt; replanIn -= dt;
                    if (walkTime > GiveUp) { Far(); break; }
                    // стажёр ушёл далеко от места, куда шли, — перестраиваем путь
                    if (replanIn <= 0f)
                    {
                        replanIn = 1f;
                        var p = playerPos(); p.y = 0; var a = aimAt; a.y = 0;
                        if ((p - a).magnitude > 1.5f) { float keep = walkTime; Plan(false); walkTime = keep; if (st != St.ToPlayer) break; }
                    }
                    var me = transform.position; me.y = 0; var pl = playerPos(); pl.y = 0;
                    // стажёр уже рядом (сам подошёл или стоит на пути) — останавливаемся и говорим
                    float gap = (pl - me).magnitude;
                    if (gap < 1.3f || (pathIdx >= path.Count - 1 && gap < 1.8f)) { Arrive(); break; }
                    if (Walk(dt)) Arrive();
                    break;
                case St.Talking:
                    FacePlayer(false);
                    if (lineLeft > 0f) { lineLeft -= dt; if (lineLeft <= 0f) NextLine(); }
                    else if (lines.Count > 0) NextLine();
                    else
                    {
                        pauseLeft -= dt;
                        if (pauseLeft > 0f) break;
                        // ждёт ответа: облачко висит, пока стажёр не нажмёт E, не уйдёт далеко или не выйдет время
                        if (waitLeft > 0f)
                        {
                            waitLeft -= dt;
                            var wp = playerPos() - transform.position; wp.y = 0f;
                            if (wp.magnitude > 9f) waitLeft = 0f;
                            if (waitLeft > 0f) break;
                        }
                        HideBubble(); PlanHome();
                    }
                    break;
                case St.Listening:
                    FacePlayer(false);
                    break;
                case St.Back:
                    if (Walk(dt)) { st = St.Home; if (anim != null) anim.moveSpeed = 0f; Log("вернулся к доске"); }
                    break;
            }
        }

        static void Log(string m) { Debug.Log("[Стажёр] Гена: " + m); }

        // Шаг по пути. true — дошли до конца
        bool Walk(float dt)
        {
            if (pathIdx >= path.Count) { if (anim != null) anim.moveSpeed = 0f; return true; }
            var me = transform.position; var target = path[pathIdx]; target.y = me.y;
            var to = target - me; float d = to.magnitude;
            if (d < 0.08f) { pathIdx++; return pathIdx >= path.Count; }
            var dir = to / d;
            float want = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, want, 420f * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            // на крутом повороте притормаживает
            float turn = Mathf.Abs(Mathf.DeltaAngle(yaw, want));
            float sp = Speed * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(turn / 90f));
            transform.position = me + dir * Mathf.Min(d, sp * dt);
            if (anim != null) anim.moveSpeed = sp;
            return false;
        }
    }
}
