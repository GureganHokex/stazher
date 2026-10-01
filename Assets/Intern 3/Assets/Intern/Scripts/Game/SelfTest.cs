// Самопроверка сборки: Stazher.exe -selftest прогоняет эталонные решения всех задач через проверки игры
// (Python — PyCore, JavaScript — Jint, SQL — SQLite, конфиги — регулярки, выбор — варианты) и пишет отчёт
// в selftest.txt рядом с сохранениями (%USERPROFILE%\AppData\LocalLow\Codezilla Games\Стажёр). Потом выходит.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Intern.Py;

namespace Intern.Game
{
    public class SelfTest : MonoBehaviour
    {
        public static bool Requested { get { return Environment.GetCommandLineArgs().Any(a => a == "-selftest"); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            new GameObject("SelfTest").AddComponent<SelfTest>();
        }

        IEnumerator Start()
        {
            yield return null;
            var sb = new StringBuilder();
            int ok = 0, fail = 0;
            var started = DateTime.Now;
            sb.AppendLine("Стажёр " + Application.version + " · самопроверка " + started.ToString("yyyy-MM-dd HH:mm:ss"));
            // версия в меню — из сборки: «0.9.0» или «0.9.0-dev», и надпись в меню совпадает с ней
            bool verOk = System.Text.RegularExpressions.Regex.IsMatch(Application.version ?? "", @"^\d+\.\d+\.\d+(-dev)?$") && GameConfig.VersionLabel == "v" + Application.version;
            sb.AppendLine("версия в меню: " + GameConfig.VersionLabel + (verOk ? " — ок" : " — НЕ СОВПАДАЕТ со сборкой"));
            if (verOk) ok++; else fail++;
            sb.AppendLine("SQLite: " + (SqlRun.Available ? SqlRun.Library + " " + SqlRun.Version : "НЕТ — " + SqlRun.LoadError));
            var counts = new Dictionary<string, int>();
            var files = Tracks.All.ToList();
            foreach (var l in Languages.All) if (Tracks.LoadOptional("lang-" + l.id) != null) files.Add("lang-" + l.id);   // ветки языков (спринт 9)
            if (Tracks.LoadOptional("warmup") != null) files.Add("warmup");
            foreach (var track in files)
            {
                var data = Tracks.Load(track);
                foreach (var tp in data.topics)
                    foreach (var t in tp.tasks)
                    {
                        string mode = t.Mode, err = null;
                        var t0 = DateTime.Now;
                        try { err = Check(t); }
                        catch (Exception e) { err = "исключение: " + e.GetType().Name + ": " + e.Message; }
                        double ms = (DateTime.Now - t0).TotalMilliseconds;
                        if (ms > 3000) sb.AppendLine("медленно " + t.key + " " + t.id + " [" + mode + "]: " + ms.ToString("0") + " мс");
                        Debug.Log("[selftest] " + t.id + " " + mode + " " + ms.ToString("0") + " мс" + (err != null ? " FAIL" : ""));
                        int c; counts.TryGetValue(mode, out c); counts[mode] = c + 1;
                        if (err == null) ok++;
                        else { fail++; sb.AppendLine("FAIL " + t.key + " " + t.id + " [" + mode + "]: " + err); }
                    }
                yield return null;
            }
            foreach (var p in new[] { "backend", "frontend", "devops", "fullstack" })
            {
                var langs = Languages.For(p).Where(l => l.ready).Select(l => l.id).ToList();
                if (langs.Count == 0) langs.Add(Languages.Default(p));
                foreach (var lang in langs)
                {
                    string why;
                    var path = Tracks.BuildPath(p, lang);
                    bool pathOk = PathOk(path, p, lang, out why);
                    if (!pathOk) fail++;
                    sb.AppendLine("путь " + p + "/" + lang + ": задач " + path.Tasks.Length + (pathOk ? " — ок" : " — " + why));
                }
            }
            var wd = WorkdaySim();
            foreach (var line in wd) { fail++; sb.AppendLine("FAIL рабочий день: " + line); }
            sb.AppendLine("рабочий день: " + (wd.Count == 0 ? "сценарии прошли" : wd.Count + " ошибок"));
            var fd = FirstDaySim();
            foreach (var line in fd) { fail++; sb.AppendLine("FAIL первый день: " + line); }
            sb.AppendLine("первый день и спринт: " + (fd.Count == 0 ? "сценарии прошли" : fd.Count + " ошибок"));
            var genInfo = new List<string>();
            var gs = GenSim(null, genInfo);
            foreach (var line in gs) { fail++; sb.AppendLine("FAIL без потолка: " + line); }
            foreach (var line in genInfo) sb.AppendLine(line);
            sb.AppendLine("уровни, звёзды, генератор: " + (gs.Count == 0 ? "сценарии прошли" : gs.Count + " ошибок"));
            var balInfo = new List<string>();
            var bs = BalanceSim(null, balInfo);
            foreach (var line in bs) { fail++; sb.AppendLine("FAIL баланс: " + line); }
            foreach (var line in balInfo) sb.AppendLine(line);
            var envInfo = new List<string>();
            var es = EnvSim(null, envInfo);
            foreach (var line in es) { fail++; sb.AppendLine("FAIL окружение: " + line); }
            foreach (var line in envInfo) sb.AppendLine(line);
            sb.AppendLine("фильтр docker и сценарии: " + (es.Count == 0 ? "проверки прошли" : es.Count + " ошибок"));
            var hallBad = HallSim(); 
            foreach (var line in hallBad) { fail++; sb.AppendLine("FAIL дома: " + line); }
            sb.AppendLine("дома, куда можно войти: " + (hallBad.Count == 0 ? "проверки прошли" : hallBad.Count + " ошибок"));
            var anBad = new List<string>(); var anInfo = new List<string>();
            yield return AnimSim(anBad, anInfo);
            foreach (var line in anBad) { fail++; sb.AppendLine("FAIL анимация: " + line); }
            foreach (var line in anInfo) sb.AppendLine(line);
            var rdBad = new List<string>(); var rdInfo = new List<string>();
            yield return RagdollSim(rdBad, rdInfo);
            foreach (var line in rdBad) { fail++; sb.AppendLine("FAIL ragdoll: " + line); }
            foreach (var line in rdInfo) sb.AppendLine(line);
            sb.AppendLine("режимы: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value).ToArray()));
            sb.AppendLine("итог: " + ok + " ок, " + fail + " ошибок, " + (DateTime.Now - started).TotalSeconds.ToString("0") + " с");
            string file = Path.Combine(Application.persistentDataPath, "selftest.txt");
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[Стажёр] Самопроверка: " + ok + " ок, " + fail + " ошибок → " + file);
            Application.Quit(fail == 0 ? 0 : 1);
        }

        // Дома, куда можно войти (спринт 6 версии 0.9): четыре дома, у каждого дверь, кресла и укрытия для обитателей,
        // все узлы внутри связаны с улицей, а из любого кресла есть путь до двери бизнес-центра
        public static List<string> HallSim()
        {
            var bad = new List<string>();
            var city = CityBuilder.Build();
            try
            {
                if (city.halls.Count != 4) bad.Add("домов " + city.halls.Count + " вместо 4");
                // связность графа: обход от первого уличного узла
                var seen = new bool[city.nodes.Count]; var q = new Queue<int>(); q.Enqueue(0); seen[0] = true;
                while (q.Count > 0) { int u = q.Dequeue(); foreach (var v in city.links[u]) if (!seen[v]) { seen[v] = true; q.Enqueue(v); } }
                foreach (var h in city.halls)
                {
                    if (h.door == null) bad.Add(h.name + ": нет двери");
                    if (h.seats.Count < h.humanitarians.Length + h.techies.Length) bad.Add(h.name + ": кресел " + h.seats.Count + " на " + (h.humanitarians.Length + h.techies.Length) + " обитателей");
                    if (h.hides.Count == 0) bad.Add(h.name + ": негде спрятаться");
                    foreach (var n in h.nodes) if (!seen[n]) { bad.Add(h.name + ": узел внутри не связан с улицей"); break; }
                    foreach (var p in h.seats)
                    {
                        if (!h.Contains(p)) { bad.Add(h.name + ": кресло вне дома"); break; }
                        foreach (var r in city.blockers) if (r.Contains(new Vector2(p.x, p.z))) { bad.Add(h.name + ": кресло внутри мебели"); break; }
                    }
                    var path = city.Path(h.seats[0], city.officeDoor, 0f);
                    if (path.Count < 3) bad.Add(h.name + ": нет пути от кресла до офиса");
                    else
                    {
                        // путь выходит через дверь: одна из точек рядом с дверью снаружи
                        bool viaDoor = false; foreach (var p in path) if ((p - h.doorOut).sqrMagnitude < 1.5f) viaDoor = true;
                        if (!viaDoor) bad.Add(h.name + ": путь наружу не через дверь");
                    }
                }
            }
            finally { UnityEngine.Object.Destroy(city.root.gameObject); }
            return bad;
        }

        // Анимация v4 (спринт 7 версии 0.9): клипы прочитались, у модели скелет v4; горожанин идёт и бежит —
        // обе ступни касаются земли и не уходят под неё, опорная ступня почти не скользит, таз на своей высоте, без NaN
        IEnumerator AnimSim(List<string> bad, List<string> info)
        {
            if (!AnimLib.Ready) { bad.Add("клипы v4 не прочитались: " + (AnimLib.Error ?? "пусто")); yield break; }
            string[] need = { "idle", "walk", "walk_b", "walk_l", "walk_r", "run", "run_b", "run_l", "run_r", "sprint", "jog", "sit", "sit_idle", "type",
                              "wave", "swing", "throw", "hit", "land", "aim1", "aim2", "hold", "air_up", "air_down" };
            foreach (var n in need) if (AnimLib.Get(n) == null) bad.Add("нет клипа " + n);
            if (!ModelLib.HasCharacter("Dev1")) { info.Add("анимация v4: модели Dev1 нет — пропущено"); yield break; }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(2000f, -0.5f, 1000f); floor.transform.localScale = new Vector3(40f, 1f, 40f);   // верх пола — y = 0
            float ikD = CharacterAnim.IKDistance, lodD = CharacterAnim.LodDistance;
            CharacterAnim.IKDistance = CharacterAnim.LodDistance = 1e6f;          // камера далеко — всё равно считать ступни
            var a = CharacterAnim.Spawn("Dev1", null, new Vector3(2000f, 0f, 988f), 0f, null);
            yield return null;
            if (!a.v4) bad.Add("Dev1: скелет не v4 — анимация по-старому, кодом");
            else
            {
                // веса кожи: у тела десятки «главных» костей (если экспорт потерял группы весов, всё тело висит на тазу и не гнётся)
                var bodySmr = a.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("Body"));
                int owners = bodySmr != null && bodySmr.sharedMesh != null ? bodySmr.sharedMesh.boneWeights.Select(w => w.boneIndex0).Distinct().Count() : 0;
                if (owners < 30) bad.Add("Dev1: веса тела потеряны — главных костей " + owners + " (нужно 30+), тело не будет гнуться");
                var feet = new[] { ModelLib.Find(a.rig, "FootL"), ModelLib.Find(a.rig, "FootR") };
                var heel = new Vector3(0f, -0.1f, -0.072f); var ball = new Vector3(0f, -0.1f, 0.092f);
                var parts = new List<string>();
                foreach (float v in new[] { 1.4f, 3.8f })
                {
                    float t = 0f, low = float.MaxValue, slide = 0f, onGround = 0f, hipLo = float.MaxValue, hipHi = float.MinValue;
                    var touched = new bool[2]; bool nan = false, have = false; float lastY = 0f; Vector3 lastP = Vector3.zero; int lastK = -1;
                    a.transform.position = new Vector3(2000f, 0f, 988f);
                    while (t < 2.6f)
                    {
                        float dt = Time.deltaTime;
                        if (t > 0.6f)
                        {
                            // самая низкая точка подошв: не под полом; касание каждой ступни; скольжение опоры
                            int best = -1; float by = float.MaxValue; Vector3 bp = Vector3.zero;
                            for (int i = 0; i < 2; i++)
                                foreach (var o in new[] { heel, ball })
                                {
                                    var pt = feet[i].position + feet[i].rotation * o;
                                    if (float.IsNaN(pt.y)) nan = true;
                                    if (pt.y < 0.03f) touched[i] = true;
                                    if (pt.y < by) { by = pt.y; bp = pt; best = i * 2 + (o == heel ? 0 : 1); }
                                }
                            low = Mathf.Min(low, by);
                            if (have && best == lastK && by < 0.01f && lastY < 0.01f && Mathf.Abs(by - lastY) < 0.006f)
                            { var d = bp - lastP; d.y = 0f; slide += d.magnitude; onGround += dt; }
                            have = true; lastY = by; lastP = bp; lastK = best;
                            hipLo = Mathf.Min(hipLo, a.hips.position.y); hipHi = Mathf.Max(hipHi, a.hips.position.y);
                        }
                        a.moveSpeed = v;
                        a.transform.position += a.transform.forward * v * dt;
                        t += dt;
                        yield return null;
                    }
                    string tag = v < 2f ? "шаг" : "бег";
                    float sl = onGround > 0.1f ? slide / onGround : 0f;
                    if (nan) bad.Add(tag + ": NaN в костях ступней");
                    if (low < -0.04f) bad.Add(tag + ": ступня под полом на " + (-low).ToString("0.00") + " м");
                    if (!touched[0] || !touched[1]) bad.Add(tag + ": ступня не касается земли (" + (touched[0] ? "" : "левая ") + (touched[1] ? "" : "правая") + ")");
                    if (sl > (v < 2f ? 0.25f : 0.4f)) bad.Add(tag + ": опорная ступня скользит " + sl.ToString("0.00") + " м/с");
                    if (hipLo < 0.7f || hipHi > 1.15f) bad.Add(tag + ": таз на высоте " + hipLo.ToString("0.00") + "…" + hipHi.ToString("0.00") + " м");
                    parts.Add(tag + " " + v.ToString("0.0") + " м/с: скольжение " + sl.ToString("0.00") + " м/с, низ " + low.ToString("0.00") + ", таз " + hipLo.ToString("0.00") + "…" + hipHi.ToString("0.00"));
                }
                info.Add("анимация v4: клипов " + AnimLib.Count + ", " + string.Join("; ", parts.ToArray()));
                // помощники локтей и коленей: повёрнуты на половину сгиба, сечение растянуто не больше чем в 1,5 раза
                var jh = a.GetComponent<JointHelpers>();
                if (jh == null || jh.Count < 4) bad.Add("Dev1: нет помощников локтей и коленей (" + (jh == null ? 0 : jh.Count) + " из 4)");
                else
                {
                    var js = new List<string>();
                    for (int i = 0; i < jh.Count; i++)
                    {
                        float ha, ja, st; jh.DevState(i, out ha, out ja, out st);
                        if (Mathf.Abs(ha - ja * 0.5f) > 3f) bad.Add("помощник " + i + ": повёрнут на " + ha.ToString("0") + "° при сгибе " + ja.ToString("0") + "°");
                        if (st < 0.999f || st > JointHelpers.MaxStretch + 0.001f) bad.Add("помощник " + i + ": растяжение " + st.ToString("0.00"));
                        js.Add(ja.ToString("0") + "°→" + st.ToString("0.00"));
                    }
                    info.Add("помощники суставов (сгиб → растяжение): " + string.Join(", ", js.ToArray()));
                }
            }
            CharacterAnim.IKDistance = ikD; CharacterAnim.LodDistance = lodD;
            UnityEngine.Object.Destroy(a.gameObject);
            UnityEngine.Object.Destroy(floor);
            yield return null;
        }

        // Ragdoll (спринт 5 версии 0.9): горожанин падает с толчком на бегу и замирает; суставы в пределах, тело не под полом,
        // после возврата в пул кости и сетки как были. Полное тело и упрощённое («Низкое»)
        IEnumerator RagdollSim(List<string> bad, List<string> info)
        {
            if (!ModelLib.HasCharacter("Dev1")) { info.Add("ragdoll: модели Dev1 нет — пропущено"); yield break; }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(1000f, -0.5f, 1000f); floor.transform.localScale = new Vector3(30f, 1f, 30f);   // верх пола — y = 0
            int savedQ = GameConfig.S.quality;
            foreach (int q in new[] { 2, 0 })
            {
                GameConfig.S.quality = q;
                var a = CharacterAnim.Spawn("Dev1", null, new Vector3(1000f, 0f, 1000f), 0f, null);
                a.moveSpeed = 4f;
                for (int i = 0; i < 5; i++) yield return null;
                var knee0 = a.kneeL.localRotation;
                var rd = Ragdoll.Make(a, new Vector3(4f, 0f, 0f), a.torso.position + Vector3.up * 0.3f, new Vector3(0f, 6f, 70f));
                string tag = q == 0 ? "упрощённое" : "полное";
                if (rd == null) { bad.Add(tag + ": тело не собралось"); UnityEngine.Object.Destroy(a.gameObject); continue; }
                float t0 = Time.realtimeSinceStartup;
                while (!rd.Frozen && Time.realtimeSinceStartup - t0 < 8f) yield return null;
                if (!rd.Frozen) bad.Add(tag + ": тело не замерло за 8 с");
                float low = float.MaxValue; bool nan = false;
                foreach (var c in a.GetComponentsInChildren<Collider>()) { if (!c.enabled) continue; low = Mathf.Min(low, c.bounds.min.y); if (float.IsNaN(c.bounds.center.x)) nan = true; }
                if (nan) bad.Add(tag + ": NaN в положении частей");
                if (low < -0.08f) bad.Add(tag + ": часть тела под полом на " + (-low).ToString("0.00") + " м");
                var hp = a.hips.position; float moved = new Vector2(hp.x - 1000f, hp.z - 1000f).magnitude;
                if (moved > 6f) bad.Add(tag + ": тело улетело на " + moved.ToString("0.0") + " м");
                float kneeBend = Bend(a.kneeL), elbowBend = Bend(a.elbowL);
                if (kneeBend < -4f || kneeBend > 140f) bad.Add(tag + ": колено согнуто на " + kneeBend.ToString("0") + "° (можно 0…135)");
                if (elbowBend > 4f || elbowBend < -145f) bad.Add(tag + ": локоть согнут на " + elbowBend.ToString("0") + "° (можно −140…0)");
                // замершее тело запечено в обычные сетки: размер как у тела (раньше сетки v4 запекались без масштаба и вырастали)
                float rlo = float.MaxValue, rhi = float.MinValue;
                foreach (var r in a.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.activeInHierarchy && !r.name.StartsWith("Blob")) { rlo = Mathf.Min(rlo, r.bounds.min.y); rhi = Mathf.Max(rhi, r.bounds.max.y); }
                if (rhi - rlo > 2.2f || rlo < -0.4f) bad.Add(tag + ": запечённое тело не того размера (" + rlo.ToString("0.00") + "…" + rhi.ToString("0.00") + " м)");
                int parts = a.GetComponentsInChildren<Rigidbody>().Count(r => r.gameObject != a.gameObject);
                info.Add("ragdoll " + tag + ": частей " + parts + ", отлетело на " + moved.ToString("0.0") + " м, колено " + kneeBend.ToString("0") + "°, низ " + low.ToString("0.00"));
                rd.Restore(); UnityEngine.Object.DestroyImmediate(rd);
                if (a.GetComponentsInChildren<Rigidbody>().Any(r => r.gameObject != a.gameObject) || a.GetComponentsInChildren<Joint>().Length > 0) bad.Add(tag + ": после возврата в пул остались физические части");
                if (!a.enabled) bad.Add(tag + ": анимация не включилась после возврата");
                if (Quaternion.Angle(a.kneeL.localRotation, knee0) > 1f) bad.Add(tag + ": поза не вернулась");
                UnityEngine.Object.Destroy(a.gameObject);
                yield return null;
            }
            GameConfig.S.quality = savedQ;
            UnityEngine.Object.Destroy(floor);
        }

        // сгиб кости вокруг её оси X (колено: + назад, локоть: − вперёд)
        static float Bend(Transform t)
        {
            var q = t.localRotation; if (q.w < 0f) { q.x = -q.x; q.w = -q.w; }
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(q.x, q.w) * Mathf.Rad2Deg);
        }

        // Ускоренный рабочий день: часы, штрафы за простой, выговоры, самоволка, прогул, увольнение
        public static List<string> WorkdaySim()
        {
            var bad = new List<string>();
            Action<bool, string> expect = (c, msg) => { if (!c) bad.Add(msg); };

            // 1. Первый день без работы: без штрафов и выговоров, 18:00 наступает один раз
            {
                var s = new SaveData { version = 3, difficulty = 1, money = 100 }; WorkDay.Reset(s);
                var w = new WorkDay(s, () => 0); int over = 0; bool fired = false;
                w.DayOver = () => over++; w.Fired = () => fired = true;
                for (int i = 0; i < 600; i++) w.Advance(1f, true);
                var r = w.Finish();
                expect(over == 1, "первый день: 18:00 наступило " + over + " раз");
                expect(s.money == 100 && s.strikes == 0 && !fired && !r.truancy, "первый день: штрафы или выговоры (" + s.money + ", " + s.strikes + ")");
                expect(r.idleHours == 9 && r.workHours == 0, "первый день: часы учёта " + r.workHours + "/" + r.idleHours);
                expect(w.Clock == "Пн 18:00", "первый день: часы показывают " + w.Clock);
            }
            // 2. Второй день, средняя сложность: первый час простоя — предупреждение, потом штрафы, долг, замечания
            {
                var s = new SaveData { version = 3, difficulty = 1, money = 100 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                var moods = new List<LeadMood>(); w.Visit = (m, t) => moods.Add(m);
                w.Advance(180f, true);
                expect(s.money == 40 && s.dayFines == 60 && s.strikes == 0, "простой 3 часа: деньги " + s.money + ", штрафы " + s.dayFines + ", выговоры " + s.strikes);
                expect(moods.Count == 3 && moods[0] == LeadMood.Warn && moods[1] == LeadMood.Fine, "Гена подходил не так: " + string.Join(",", moods.Select(m => m.ToString()).ToArray()));
                w.Advance(120f, true);                                   // 14:00: 10 монет взял, 20 в долг
                expect(s.money == 0 && s.debt == 20 && s.strikes == 0 && !fired, "штраф без денег: деньги " + s.money + ", долг " + s.debt + ", выговоры " + s.strikes);
                int got = w.Earn(50); s.money += got;
                expect(got == 30 && s.debt == 0 && s.dayDebtPaid == 20, "доход не погасил долг: дошло " + got + ", долг " + s.debt);
                string why; expect(w.CanLunch(out why), "обед в 14:00 недоступен: " + why);
                w.StartLunch();                                          // самоволка в первый раз — замечание
                expect(!fired && s.strikes == 0 && s.warnedAwol, "первая самоволка дала выговор");
                w.Advance(60f, false);
                w.Advance(180f, true);                                   // 16:00 штраф 30, 17:00 долг 30, 18:00 долг 60
                expect(s.money == 0 && s.debt == 60 && s.strikes == 0, "после обеда: деньги " + s.money + ", долг " + s.debt + ", выговоры " + s.strikes);
                var r = w.Finish();                                      // первый прогул — замечание
                expect(!r.truancy && r.remark != null && s.strikes == 0 && s.cleanDays == 0, "первый прогул: выговоры " + s.strikes + ", замечание " + r.remark);
                expect(r.debt == 60, "долг в итогах дня " + r.debt);
                w.NextDay();
                w.Advance(60f, true);                                    // 10:00 предупреждение
                expect(s.debt == 60 && s.strikes == 0, "новый день: предупреждение стоило денег или выговор");
                w.Advance(60f, true);                                    // 11:00 долг 90 → выговор, долг списан
                expect(s.strikes == 1 && s.debt == 0 && !fired, "долг 90: выговоры " + s.strikes + ", долг " + s.debt);
                w.Advance(60f, true);                                    // 12:00 долг 30
                w.StartLunch();                                          // вторая самоволка — выговор, 2 из 2 → уволен
                expect(fired && w.IsFired, "вторая самоволка при 1 из 2 выговоров не уволила");
            }
            // 3. Честный день: работа каждый час, обед, задачи → без штрафов, чистый день
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 0 }; WorkDay.Reset(s); s.day = 3;
                var w = new WorkDay(s, () => 1);
                for (int h = 0; h < 3; h++) { w.Activity(WorkKind.Run); w.Advance(60f, true); }
                string why; expect(w.CanLunch(out why), "обед в 12:00: " + why);
                w.StartLunch(); for (int i = 0; i < 60; i++) w.Advance(1f, false); w.EndLunch(120, 12);
                expect(w.Sated, "после обеда нет бонуса «Сытый»");
                expect(!w.CanLunch(out why), "второй обед за день разрешён");
                for (int h = 0; h < 5; h++) { w.Activity(WorkKind.Check); s.dayTasks++; w.Advance(60f, true); }
                var r = w.Finish();
                expect(r.fines == 0 && s.strikes == 0 && !r.truancy, "честный день: штрафы " + r.fines + ", выговоры " + s.strikes);
                expect(r.lunchMoney == 120 && r.kills == 12 && s.cleanDays == 1, "честный день: обед " + r.lunchMoney + "/" + r.kills + ", чистых " + s.cleanDays);
                w.NextDay();
                expect(s.day == 4 && s.minute == WorkDay.Start && !s.lunchTaken && w.Clock == "Чт 9:00", "следующий день: " + w.Clock);
            }
            // 4. Правки в IDE: 2 минуты правок = рабочий час
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 500 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 3);
                w.Activity(WorkKind.Edit, 119f); w.Advance(60f, true);
                expect(s.dayIdleHours == 1 && s.money == 500, "119 с правок засчитаны как работа или предупреждение стоило денег");
                w.Activity(WorkKind.Edit, 60f); w.Activity(WorkKind.Edit, 61f); w.Advance(60f, true);
                expect(s.dayWorkHours == 1, "121 с правок не засчитаны");
                w.Advance(60f, true);
                expect(s.money == 400, "второй простой не оштрафован на 100 (Middle): " + s.money);
            }
            // 5. Прогул и снятие выговора за 5 чистых дней
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 1000 }; WorkDay.Reset(s); s.day = 5;
                var w = new WorkDay(s, () => 0);
                s.warnedTruancy = true;                                  // замечание уже было
                w.Activity(WorkKind.Run); w.Advance(540f, true);
                var r = w.Finish();
                expect(r.truancy && s.strikes == 1, "повторный прогул не дал выговор");
                s.cleanDays = 4; w.NextDay(); s.strikeToday = false;
                for (int h = 0; h < 9; h++) { w.Activity(WorkKind.Run); if (h < 3) s.dayTasks++; w.Advance(60f, true); }
                r = w.Finish();
                expect(r.strikeRemoved && s.strikes == 0 && !s.warnedTruancy, "пятый чистый день не снял выговор или замечание");
            }
            // 6. Тяжёлая: час на обеде не считается, выговор только когда долг дорос до трёх штрафов
            {
                var s = new SaveData { version = 3, difficulty = 2, money = 0 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); bool fired = false; w.Fired = () => fired = true;
                w.Activity(WorkKind.Run); w.Advance(170f, true);           // 9:00–11:50: работа, предупреждение
                expect(!fired && s.debt == 0, "тяжёлая: предупреждение уволило или стоило денег");
                w.Advance(10f, true);                                     // 12:00: долг 30
                w.StartLunch(); w.Advance(60f, false);                    // час на обеде не считается
                expect(s.dayIdleHours == 2 && s.debt == 30 && !fired, "тяжёлая: простои " + s.dayIdleHours + ", долг " + s.debt);
                w.Advance(60f, true);
                expect(!fired && s.debt == 60, "тяжёлая: долг 60 уже уволил");
                w.Advance(60f, true);
                expect(fired, "тяжёлая: долг 90 не дал выговор");
            }
            // 7. Похвала за три рабочих часа подряд
            {
                var s = new SaveData { version = 3, difficulty = 0, money = 0 }; WorkDay.Reset(s); s.day = 2;
                var w = new WorkDay(s, () => 0); var moods = new List<LeadMood>(); w.Visit = (m, t) => moods.Add(m);
                for (int h = 0; h < 3; h++) { w.Activity(WorkKind.Solved); w.Advance(60f, true); }
                expect(moods.Count == 1 && moods[0] == LeadMood.Praise, "нет похвалы за три часа работы");
            }
            return bad;
        }

        // Обучение первого дня по шагам и спринт на неделю: план, бонус, неполная неделя
        public static List<string> FirstDaySim()
        {
            var bad = new List<string>();
            Action<bool, string> expect = (c, msg) => { if (!c) bad.Add(msg); };
            {
                var s = new SaveData(); WorkDay.Reset(s);
                expect(s.tutorial == -1, "старое сохранение без обучения: шаг " + s.tutorial);
                var o = new Onboarding(s); var seen = new List<Tut>(); o.Entered = t => seen.Add(t);
                o.Begin();
                expect(o.Active && o.Step == Tut.MeetLead, "обучение не началось");
                o.Event("sit"); o.Event("talk");
                expect(o.Step == Tut.RunCode, "после знакомства и посадки шаг " + o.Step);
                o.Event("run"); expect(o.Step == Tut.SolveFirst, "после запуска шаг " + o.Step);
                s.dayTasks = 1; o.Event("solved"); expect(o.Step == Tut.SolveSecond, "после первой задачи шаг " + o.Step);
                s.minute = 600; s.dayTasks = 2; o.Event("solved"); expect(o.Step == Tut.WaitLunch, "после второй задачи до обеда шаг " + o.Step);
                o.Poll(700, 0, false); expect(o.Step == Tut.WaitLunch, "обед наступил раньше 12:00");
                o.Poll(720, 0, false); expect(o.Step == Tut.GoLunch, "в 12:00 шаг " + o.Step);
                o.Event("lunch"); expect(o.Step == Tut.FirstKills, "в городе шаг " + o.Step);
                o.Poll(730, 2, true); expect(o.Step == Tut.FirstKills, "двое выбитых уже засчитаны за троих");
                o.Poll(731, 3, true); expect(o.Step == Tut.BackToOffice, "трое выбитых: шаг " + o.Step);
                o.Event("lunchEnd"); expect(o.Step == Tut.WorkTillEvening, "после обеда шаг " + o.Step);
                o.Event("dayEnd"); expect(!o.Active && s.tutorial == -1, "обучение не закончилось в конце дня");
                // сел за стол раньше разговора — шаг «Сядь за компьютер» пропущен: 11 шагов с «Готово» минус 1
                expect(seen.Count == Onboarding.Count && !seen.Contains(Tut.SitDown), "шагов показано " + seen.Count);
                // ранний обед и пропуск
                var s2 = new SaveData(); var o2 = new Onboarding(s2); o2.Begin(); o2.Event("talk"); o2.Event("sit"); o2.Event("run"); o2.Event("solved");
                o2.Event("lunch"); expect(o2.Step == Tut.FirstKills, "обед до второй задачи: шаг " + o2.Step);
                var s3 = new SaveData(); var o3 = new Onboarding(s3); o3.Begin(); o3.Skip(); expect(!o3.Active, "пропуск обучения не сработал");
            }
            {
                var s = new SaveData(); WorkDay.Reset(s);
                var sp = new WeekSprint(s); var ids = new List<string>(); for (int i = 0; i < 40; i++) ids.Add("t" + i);
                sp.Plan(1, 0, ids);
                expect(sp.Planned && sp.Number == 1 && sp.Goal == 8 && sp.Tasks.Count == 11, "первый спринт: цель " + sp.Goal + ", задач " + sp.Tasks.Count);
                for (int i = 0; i < 8; i++) sp.TaskDone("t" + i);
                var r = sp.Close(0);
                expect(r.success && r.bonus == WeekSprint.BonusFor(8, 0) && !sp.Planned, "ретро первого спринта: " + r.text);
                sp.Plan(6, 1, ids);
                expect(sp.Number == 2 && sp.Goal == 15, "второй спринт Junior: цель " + sp.Goal);
                r = sp.Close(1); expect(!r.success && r.bonus == 0, "невыполненный спринт дал бонус");
                sp.Plan(8, 2, ids);   // среда: осталось 3 дня из 5
                expect(sp.Goal == Mathf.RoundToInt(12 * 3 / 5f), "спринт со среды: цель " + sp.Goal);
                expect(WeekSprint.Monday(6) && WeekSprint.Friday(5) && !WeekSprint.Friday(6), "дни недели");
            }
            return bad;
        }

        // Спринт 5: уровни и титулы, звёзды тем, генератор задач (эталон проходит, мутант валит тест), тикеты дня
        public static List<string> GenSim(Func<string, TrackData> load = null, List<string> report = null)
        {
            var bad = new List<string>();
            Action<bool, string> expect = (c, msg) => { if (!c) bad.Add(msg); };
            // ---------- уровни ----------
            for (int l = 1; l <= 200; l++)
            {
                if (Levels.Of(Levels.TotalFor(l)) != l) { bad.Add("уровень " + l + ": Of(TotalFor) = " + Levels.Of(Levels.TotalFor(l))); break; }
                if (l > 1 && Levels.Of(Levels.TotalFor(l) - 1) != l - 1) { bad.Add("уровень " + l + ": на 1 XP меньше — не " + (l - 1)); break; }
                if (Levels.TotalFor(l + 1) - Levels.TotalFor(l) != Levels.Cost(l)) { bad.Add("стоимость уровня " + l); break; }
            }
            expect(Levels.Of(0) == 1 && Levels.Cost(1) == 220 && Levels.TotalFor(2) == 220, "первый уровень: 220 XP");
            expect(Levels.Title(29) == "Middle" && Levels.Title(30) == "Senior" && Levels.Title(40) == "Lead" && Levels.Title(50) == "Principal" && Levels.Title(60) == "Architect", "титулы 30/40/50/60");
            expect(Levels.Title(70) == "Architect ★" && Levels.Title(95) == "Architect ★★★", "звёзды Architect: " + Levels.Title(70) + ", " + Levels.Title(95));
            expect(Mathf.Abs(Levels.TitleBonus(30) - 0.1f) < 1e-4f && Mathf.Abs(Levels.TitleBonus(80) - 0.5f) < 1e-4f && Levels.TitleBonus(29) == 0f, "бонус титула");
            int at; expect(Levels.NextTitle(35, out at) == "Lead" && at == 40, "следующий титул после 35");
            // ---------- звёзды ----------
            {
                var st = new TopicStat { id = "t" };
                expect(TopicStars.Of(st, false) == 0 && TopicStars.Of(st, true) == 1, "одна звезда за сданную тему");
                int got = 0;
                for (int i = 0; i < 4; i++) got += TopicStars.Record(st, true, true);
                expect(got == 0 && TopicStars.Of(st, true) == 1, "4 задачи — ещё одна звезда");
                expect(TopicStars.Record(st, true, false) == 2 && st.streak == 0, "5 без подсказок — две звезды, опоздание сбрасывает серию");
                for (int i = 0; i < 9; i++) got += TopicStars.Record(st, false, true);
                expect(got == 0 && st.best == 9, "9 подряд — ещё не три звезды");
                expect(TopicStars.Record(st, false, true) == 3 && TopicStars.Of(st, true) == 3, "10 подряд вовремя — три звезды");
                expect(TopicStars.Record(st, true, true) == 0, "звезда выдаётся один раз");
            }
            // ---------- генератор ----------
            load = load ?? Tracks.Load;
            var all = new Dictionary<string, TaskData>();
            var tracks = new Dictionary<string, TrackData>();
            foreach (var tr in Tracks.All) { var d = load(tr); tracks[tr] = d; foreach (var tp in d.topics) foreach (var t in tp.tasks) all[t.id] = t; }
            Func<string, TaskData> find = id => { TaskData t; return all.TryGetValue(id, out t) ? t : null; };
            int sources = 0, fixes = 0, asks = 0;
            foreach (var s in all.Values.Where(TaskGen.CanGenerate))
            {
                sources++;
                if (TaskGen.Original(s) == null) { bad.Add("генератор: эталон " + s.id + " не проходит свои тесты"); continue; }
                foreach (var seed in new[] { 1, 2 })
                {
                    var f = TaskGen.Build(TaskGen.Spec("fix", s.id, seed), find);
                    if (f != null)
                    {
                        fixes++;
                        var again = TaskGen.Build(TaskGen.Spec("fix", s.id, seed), find);
                        expect(again != null && again.starter == f.starter, "генератор: " + f.id + " собирается по-разному");
                        expect(f.starter != s.solution && f.solution == s.solution, "генератор: " + f.id + " — мутант совпал с эталоном");
                        var r = TaskGen.RunTests(s, f.starter, 300000);
                        expect(r.Count > 0 && r.Any(x => !x.Passed), "генератор: " + f.id + " — мутант проходит все тесты");
                        expect(f.hints.Length == 2 && f.explanation.Length > 0 && f.xp >= 5, "генератор: " + f.id + " — нет подсказок или разбора");
                    }
                    var a = TaskGen.Build(TaskGen.Spec("ask", s.id, seed), find);
                    if (a != null)
                    {
                        asks++;
                        expect(a.IsChoice && a.answer.Length == 1 && a.answer[0] >= 0 && a.answer[0] < a.options.Length, "генератор: " + a.id + " — неверный индекс ответа");
                        expect(a.options.Length >= 3 && a.options.Distinct().Count() == a.options.Length, "генератор: " + a.id + " — варианты повторяются или их мало");
                    }
                }
            }
            expect(sources >= 60, "генератор: исходных задач мало — " + sources);
            expect(fixes >= sources * 2 * 8 / 10, "генератор «Почини баг»: собрано " + fixes + " из " + sources * 2);
            expect(asks >= sources * 2 * 6 / 10, "генератор «Что вернёт»: собрано " + asks + " из " + sources * 2);
            foreach (var p in new[] { "backend", "frontend", "devops", "fullstack" })
            {
                var path = Tracks.BuildPath(p, n => tracks[n]);
                var d1 = TaskGen.Daily(path.Tasks, 12, p, find);
                var d2 = TaskGen.Daily(path.Tasks, 12, p, find);
                var d3 = TaskGen.Daily(path.Tasks, 13, p, find);
                expect(d1.Count >= 6 && d1.Count <= 8, "тикеты дня " + p + ": " + d1.Count);
                expect(d1.SequenceEqual(d2), "тикеты дня " + p + " не повторяются при пересборке");
                expect(!d1.SequenceEqual(d3), "тикеты дня " + p + " одинаковые в разные дни");
                expect(d1.Distinct().Count() == d1.Count, "тикеты дня " + p + " с повторами");
                if (report != null) report.Add("тикеты дня " + p + ": " + d1.Count + " (" + string.Join(", ", d1.Select(x => { var t = TaskGen.Build(x, find); return t != null ? t.key + " " + t.genKind : "?"; }).ToArray()) + ")");
            }
            var topic = tracks["backend"].topics.FirstOrDefault(tp => tp.tasks.Any(TaskGen.CanGenerate));
            if (topic != null)
            {
                var pr = TaskGen.Practice(topic.tasks, 1, find);
                var pt = pr != null ? TaskGen.Build(pr, find) : null;
                expect(pt != null && pt.topic == topic.id && pt.key == "TR-1", "тренировка по теме " + topic.id);
            }
            // ---------- смена компании ----------
            {
                var path = Tracks.BuildPath("backend", n => tracks[n]);
                var sv = new SaveData();
                foreach (var t in path.Tasks) sv.done.Add(t.id);
                sv.done.Add("fe-other-task");                           // задача другого направления — остаётся
                var intern = path.Topics.First(tp => tp.gradeIndex == 0).tasks[0].id;
                var middle = path.Topics.Last(tp => tp.gradeIndex == 3).tasks[0].id;
                sv.SetCode(intern, "x"); sv.SetCode(middle, "y"); sv.SetCode("gen:fix:a:1", "z");
                sv.daily.Add("gen:fix:a:1"); sv.genDone.Add("gen:fix:a:1"); sv.practice.Add("gen:ask:b:1000001");
                sv.xp = 50000; sv.money = 777; sv.sprintGoal = 10; sv.sprintNo = 3;
                Levels.ResetForCompany(sv, path);
                var set = new HashSet<string>(sv.done);
                expect(sv.company == 1 && sv.xp == 50000 && sv.money == 777, "смена компании: опыт и монеты должны остаться");
                expect(path.GradeIndex(set) == 1, "смена компании: грейд после смены — " + path.GradeIndex(set) + ", ждали Junior (1)");
                expect(set.Contains("fe-other-task") && set.Contains(intern) && !set.Contains(middle), "смена компании: сданные задачи сброшены неверно");
                expect(sv.GetCode(intern) == "x" && sv.GetCode(middle) == null && sv.GetCode("gen:fix:a:1") == null, "смена компании: код решений");
                expect(sv.daily.Count == 0 && sv.genDone.Count == 0 && sv.practice.Count == 0 && sv.sprintGoal == 0 && sv.sprintNo == 3, "смена компании: тикеты и спринт");
                expect(Mathf.Abs(Levels.CompanyBonus * sv.company - 0.1f) < 1e-4f && Levels.CompanyName(1) != Levels.CompanyName(0), "смена компании: бонус и имя");
            }
            if (report != null) report.Add("генератор: исходных задач " + sources + ", «Почини баг» " + fixes + ", «Что вернёт» " + asks);
            return bad;
        }

        // Спринт 6: темп после конца пути (та же формула, что в Tools/Balance/model.py). Все тикеты дня каждый день,
        // опыт тикета — среднее по исходным задачам пути (60% «Почини баг», 40% «Что вернёт»). Дни до титулов — в отчёт
        public static Dictionary<string, int[]> TitleDays(Func<string, TrackData> load)
        {
            var res = new Dictionary<string, int[]>();
            foreach (var p in new[] { "backend", "frontend", "devops", "fullstack" })
            {
                var path = Tracks.BuildPath(p, load);
                int xp = path.Tasks.Sum(t => t.xp);
                var src = path.Tasks.Where(TaskGen.CanGenerate).ToList();
                var days = new int[Levels.TitleAt.Length];
                int day = 0;
                while (day < 400 && days[days.Length - 1] == 0 && src.Count > 0)
                {
                    day++;
                    int lv = Levels.Of(xp);
                    double mean = src.Average(t => 0.6 * Levels.TicketXp(lv, "fix", t.difficulty, false) + 0.4 * Levels.TicketXp(lv, "ask", t.difficulty, false));
                    xp += (int)Math.Round(mean * Levels.TicketsPerDay) + Levels.DayBonus(lv);
                    for (int i = 0; i < days.Length; i++) if (days[i] == 0 && Levels.Of(xp) >= Levels.TitleAt[i]) days[i] = day;
                }
                res[p] = days;
            }
            return res;
        }

        public static List<string> BalanceSim(Func<string, TrackData> load = null, List<string> report = null)
        {
            var bad = new List<string>();
            var days = TitleDays(load ?? Tracks.Load);
            foreach (var kv in days)
                if (report != null) report.Add("темп после пути " + kv.Key + ": Senior — день " + kv.Value[0] + ", Lead — " + kv.Value[1] + ", Principal — " + kv.Value[2] + ", Architect — " + kv.Value[3]);
            // план (путь backend): Senior ~6, Lead ~16, Principal ~30, Architect ~45 игровых дней после конца пути
            var b = days["backend"];
            if (b[0] < 4 || b[0] > 9) bad.Add("баланс: до Senior " + b[0] + " дн., по плану около 6");
            if (b[1] < 12 || b[1] > 22) bad.Add("баланс: до Lead " + b[1] + " дн., по плану около 16");
            if (b[2] < 22 || b[2] > 38) bad.Add("баланс: до Principal " + b[2] + " дн., по плану около 30");
            if (b[3] < 32 || b[3] > 58) bad.Add("баланс: до Architect " + b[3] + " дн., по плану около 45");
            foreach (var kv in days) if (kv.Value[3] == 0 || kv.Value[3] > 80) bad.Add("баланс: " + kv.Key + " до Architect не дойти за 80 дней");
            // тренировка даёт меньше тикета, премия за день — пятая часть уровня
            if (Levels.TicketXp(30, "fix", 3, true) >= Levels.TicketXp(30, "fix", 3, false)) bad.Add("баланс: тренировка даёт не меньше тикета дня");
            if (Levels.DayBonus(30) != 160) bad.Add("баланс: премия за день на 30-м уровне " + Levels.DayBonus(30) + ", ждали 160");
            return bad;
        }

        // Спринт 7: фильтр docker-команд, разбор команд для «Что произошло», сценарии окружения и их проверки (на подделке, без Docker)
        public static List<string> EnvSim(string envJson = null, List<string> report = null)
        {
            var bad = new List<string>();
            Action<bool, string> T = (ok, what) => { if (!ok) bad.Add(what); };
            const string host = @"C:\Users\u\Documents\Стажёр\work";
            Func<string, DockerPlan> P = l => DockerGuard.Plan(l, "/work", host);
            var p = P("docker run -d --name site -p 8080:80 -v /work/site:/usr/share/nginx/html:ro nginx:alpine");
            T(p.Error == null, "docker run nginx отклонён: " + p.Error);
            T(p.ArgLine.Contains("--label stazher=1"), "run без метки stazher: " + p.ArgLine);
            T(p.Args.Contains("127.0.0.1:8080:80"), "порт не привязан к 127.0.0.1: " + p.ArgLine);
            T(p.Args.Contains(host + @"\site:/usr/share/nginx/html:ro"), "том /work/site не переведён в путь ПК: " + p.ArgLine);
            T(p.PulledImage == "nginx:alpine", "образ в run не распознан: " + p.PulledImage);
            foreach (var l in new[] { "docker run --privileged alpine", "docker run -v /:/host alpine", "docker run -v /var/run/docker.sock:/var/run/docker.sock alpine",
                                      "docker run --network host nginx", "docker run --net=host nginx", "docker run -P nginx", "docker system prune -af", "docker run -it ubuntu",
                                      "docker exec -it site sh", "docker build -t x /etc", "docker run --pid=host alpine", "docker run -v ../../..:/x alpine", "docker cp site:/etc/passwd .",
                                      "docker -H tcp://1.2.3.4 ps", "docker image prune -a", "docker run --mount type=bind,source=/,target=/h alpine", "docker run --cap-add=SYS_ADMIN alpine",
                                      "docker rm -f stazher-sandbox", "docker stop stazher-sandbox", "docker rmi stazher/sandbox:1" })
                T(P(l).Error != null, "опасная команда пропущена: " + l);
            var rm = P("docker rm -f site"); T(rm.Error == null && rm.Targets.SequenceEqual(new[] { "site" }), "docker rm: цели " + string.Join(",", rm.Targets.ToArray()));
            var ri = P("docker rmi nginx:alpine"); T(ri.ImageTargets.SequenceEqual(new[] { "nginx:alpine" }), "docker rmi: образ не распознан");
            var ex = P("docker exec site ls /usr/share/nginx/html"); T(ex.Error == null && ex.Targets.SequenceEqual(new[] { "site" }) && ex.Args.Contains("ls"), "docker exec: " + ex.Error + " " + ex.ArgLine);
            var rr = P("docker run --rm alpine echo привет"); T(rr.Error == null && rr.ArgLine.EndsWith("alpine echo привет"), "docker run --rm alpine echo: " + rr.Error + " " + rr.ArgLine);
            var bd = P("docker build -t site ./site"); T(bd.Error == null && bd.Args.Contains(host + @"\site") && bd.ArgLine.Contains("--label stazher=1"), "docker build ./site: " + bd.Error + " " + bd.ArgLine);
            var ps = P("docker ps -a"); T(ps.Error == null && ps.ArgLine == "ps -a", "docker ps -a: " + ps.ArgLine);
            T(DockerGuard.MapPath("../../etc", "/work/a", host) == null, "MapPath выпустил из /work");
            T(DockerGuard.MapPath("$(pwd)/x", "/work/site", host) == host + @"\site\x", "MapPath $(pwd): " + DockerGuard.MapPath("$(pwd)/x", "/work/site", host));
            T(DockerGuard.LocalPort("0.0.0.0:8080:80") == "127.0.0.1:8080:80", "порт 0.0.0.0 не переписан на 127.0.0.1");
            T(DockerGuard.LocalPort("80") == null, "порт без номера на ПК принят");
            var tk = DockerGuard.Tokenize("echo 'a b' \"c d\" e\\ f"); T(tk.SequenceEqual(new[] { "echo", "a b", "c d", "e f" }), "Tokenize: " + string.Join("|", tk.ToArray()));
            T(DevEnv.NormImage("nginx") == "nginx:latest" && DevEnv.NormImage("localhost:5000/x") == "localhost:5000/x:latest" && DevEnv.NormImage("nginx:alpine") == "nginx:alpine", "NormImage");
            T(DevEnv.ShellQuote("echo \"a b\"") == "\"echo \\\"a b\\\"\"", "ShellQuote: " + DevEnv.ShellQuote("echo \"a b\""));
            T(Shell.Marker.IsMatch("__STAZHER__0|/work/shop"), "маркер stazher-run");
            var df = DevEnv.SandboxDockerfile(); T(df.Contains("FROM ubuntu:24.04") && df.Contains("/usr/local/bin/stazher-run"), "Dockerfile песочницы");
            // цепочки команд: docker — на ПК, остальное — в песочнице
            var ch = Shell.SplitChain("cd /work && docker build -t x ./x || echo \"a && b\"; ls");
            T(ch.Count == 4 && ch[0].Key == "" && ch[1].Key == "&&" && ch[2].Key == "||" && ch[3].Key == ";" && ch[2].Value == "echo \"a && b\"", "SplitChain: " + string.Join(" / ", ch.Select(c => c.Key + "[" + c.Value + "]").ToArray()));
            T(Shell.PipeAt("docker logs site | grep GET") == 17 && Shell.PipeAt("echo 'a|b'") == -1 && Shell.PipeAt("a || b") == -1, "PipeAt");
            T(P("docker rm -f stazher-sandbox").Error != null, "песочницу можно удалить командой");
            // «Что произошло»
            var exl = ShellExplain.Explain("cut -d' ' -f1 logs/access.log | sort | uniq -c | sort -rn | head -5");
            T(exl.Any(x => x.Contains("конвейер")) && exl.Any(x => x.Contains("uniq")) && exl.Any(x => x.Contains("-rn")), "объяснение конвейера: " + string.Join(" / ", exl.ToArray()));
            var ops = ShellExplain.SplitOps("echo \"a | b\" > hello.txt && cat hello.txt");
            T(ops.Count == 4 && ops[1].Key == "> hello.txt" && ops[2].Key == "&&", "SplitOps: " + string.Join(" / ", ops.Select(o => o.Key ?? o.Value).ToArray()));
            T(ShellExplain.Explain("git switch -c fix/price").Any(x => x.Contains("git switch")), "объяснение git switch");
            // сценарии
            if (envJson == null) { var ta = Resources.Load<TextAsset>("Tasks/env"); envJson = ta != null ? ta.text : null; }
            if (envJson == null) { bad.Add("нет Resources/Tasks/env.json"); return bad; }
            var list = EnvContent.Parse(envJson);
            T(list.Count >= 4, "сценариев " + list.Count + ", ждали 4+");
            T(list.Count(s => s.mission) == 1 && list.Any(s => s.id == EnvContent.MissionId && s.mission), "миссия env-setup");
            var kinds = new HashSet<string> { "sandbox", "host", "http", "last", "file", "ips", "github" };
            foreach (var s in list)
            {
                var t = EnvContent.ToTask(s, 1);
                T(t.Mode == "scenario" && t.scenario == s, s.id + ": режим " + t.Mode);
                foreach (var st in s.steps)
                {
                    string w = s.id + " / " + st.title + ": ";
                    T(kinds.Contains(st.check.kind), w + "неизвестная проверка " + st.check.kind);
                    foreach (var re in new[] { st.check.expect, st.check.notExpect, st.check.lastCmd })
                        if (!string.IsNullOrEmpty(re)) { try { new System.Text.RegularExpressions.Regex(re); } catch (Exception e) { bad.Add(w + "регулярка " + re + ": " + e.Message); } }
                    T(!string.IsNullOrEmpty(st.check.expect) || !string.IsNullOrEmpty(st.check.notExpect) || st.check.kind == "ips", w + "проверка без expect");
                    T((st.check.kind != "last" && st.check.kind != "ips") || !string.IsNullOrEmpty(st.solve), w + "нет команды-решения");
                    if (st.check.kind == "last" && !string.IsNullOrEmpty(st.check.lastCmd) && st.solve != null)
                        T(System.Text.RegularExpressions.Regex.IsMatch(st.solve.Trim(), st.check.lastCmd), w + "решение не проходит last_cmd " + st.check.lastCmd);
                    if (st.solve != null)
                        foreach (var seg in Shell.SplitChain(st.solve))
                            if (seg.Value.StartsWith("docker"))
                            {
                                int pi = Shell.PipeAt(seg.Value);
                                var sp = P(pi >= 0 ? seg.Value.Substring(0, pi).Trim() : seg.Value);
                                T(sp.Error == null, w + "фильтр не пропускает решение «" + seg.Value + "»: " + sp.Error);
                            }
                    if (st.action != null) T(new[] { "docker-site", "docker-start", "build", "start" }.Contains(st.action), w + "неизвестное действие " + st.action);
                }
            }
            // проверки шагов на подделке
            string note;
            var fake = new FakeEnv();
            fake.host["image inspect --format {{.Id}} nginx:alpine"] = "sha256:abc";
            T(EnvCheck.Evaluate(new EnvCheckDef { kind = "host", cmd = "image inspect --format {{.Id}} nginx:alpine", expect = "sha256" }, fake, null, out note), "host-проверка: " + note);
            T(!EnvCheck.Evaluate(new EnvCheckDef { kind = "http", url = "http://localhost:8080/", expect = "x" }, fake, null, out note) && note != null, "http без ответа прошла");
            fake.http = "<h1>Мы открылись!</h1>";
            T(EnvCheck.Evaluate(new EnvCheckDef { kind = "http", url = "http://localhost:8080/", expect = "Мы открылись" }, fake, null, out note), "http-проверка: " + note);
            T(EnvCheck.Evaluate(new EnvCheckDef { kind = "last", lastCmd = "^pwd$", expect = "^/work" }, fake, new ShellRecord { cmd = "pwd", output = "/work\n" }, out note), "last pwd: " + note);
            T(!EnvCheck.Evaluate(new EnvCheckDef { kind = "last", lastCmd = "^pwd$", expect = "^/work" }, fake, new ShellRecord { cmd = "ls", output = "/work" }, out note), "last: чужая команда прошла");
            fake.sandbox["ref"] = "10.0.0.7\n192.168.1.4\n";
            T(EnvCheck.Evaluate(new EnvCheckDef { kind = "ips", refCmd = "ref" }, fake, new ShellRecord { cmd = "x", output = "    173 10.0.0.7\n    151 192.168.1.4\n" }, out note), "ips: " + note);
            T(!EnvCheck.Evaluate(new EnvCheckDef { kind = "ips", refCmd = "ref" }, fake, new ShellRecord { cmd = "x", output = "192.168.1.4\n10.0.0.7\n" }, out note), "ips: неверный порядок прошёл");
            T(!EnvCheck.Evaluate(new EnvCheckDef { kind = "host", cmd = "ps", notExpect = "\\bsite\\b" }, new FakeEnv { hostAll = "site\n" }, null, out note), "not_expect не сработал");
            // GitHub и ник игрока (спринт 11 «Свой форк»)
            {
                string saved = EnvCheck.Gh;
                var gh = new EnvCheckDef { kind = "github", url = "repos/{gh}/stazher", expect = "(?s)\"fork\":\\s*true.*\"full_name\":\\s*\"" + EnvCheck.Upstream + "\"", fail = "нет форка {gh}/stazher" };
                EnvCheck.Vars["gh"] = "";
                T(!EnvCheck.Evaluate(gh, new FakeEnv(), null, out note) && note == EnvCheck.NoNick, "github без ника: " + note);
                EnvCheck.Vars["gh"] = "Nick-1";
                T(EnvCheck.Fill("github.com/{gh}/stazher") == "github.com/Nick-1/stazher" && EnvCheck.Fill("^intern/{gh}$", true) == "^intern/" + System.Text.RegularExpressions.Regex.Escape("Nick-1") + "$", "подстановка {gh}");
                var fg = new FakeEnv { sandboxAll = "{\"full_name\": \"Nick-1/stazher\", \"fork\": true, \"parent\": {\"full_name\": \"" + EnvCheck.Upstream + "\"}}" };
                T(EnvCheck.Evaluate(gh, fg, null, out note), "github: форк не засчитан: " + note);
                T(!EnvCheck.Evaluate(gh, new FakeEnv { sandboxAll = "{\"message\": \"Not Found\"}" }, null, out note) && note == "нет форка Nick-1/stazher", "github 404: " + note);
                T(!EnvCheck.Evaluate(gh, new FakeEnv { sandboxAll = "{\"message\": \"API rate limit exceeded for 1.2.3.4\"}" }, null, out note) && note.Contains("60"), "github лимит: " + note);
                T(!EnvCheck.Evaluate(gh, new FakeEnv { sandboxAll = "{\"full_name\": \"Nick-1/stazher\", \"fork\": false}" }, null, out note), "github: не форк засчитан");
                T(EnvCheck.GhNick.IsMatch("Nick-1") && !EnvCheck.GhNick.IsMatch("-bad") && !EnvCheck.GhNick.IsMatch("a--b") && !EnvCheck.GhNick.IsMatch("имя"), "проверка ника GitHub");
                EnvCheck.Vars["gh"] = saved;
            }
            // разбор go test -json (раннер языков, спринт 10)
            {
                Func<string, string, string, string> J = (action, test, output) => "{\"Action\":\"" + action + "\"" + (test != null ? ",\"Test\":\"" + test + "\"" : "") + (output != null ? ",\"Output\":\"" + output + "\"" : "") + "}";
                var gt = LangBox.ParseGoTest(string.Join("\n", new[] { J("run", "TestA", null), J("run", "TestA/1_x_2", null), J("output", "TestA/1_x_2", "    main_test.go:9: A(1, 2) = 3, ожидалось 2\\n"), J("fail", "TestA/1_x_2", null),
                    J("run", "TestA/0", null), J("pass", "TestA/0", null), J("fail", "TestA", null) }), "main.go");
                T(gt.results.Count == 2 && !gt.results[0].Passed && gt.results[0].InputsText == "1 x 2" && gt.results[0].Note == "A(1, 2) = 3, ожидалось 2" && gt.results[1].Passed, "go test: подтесты " + string.Join(" / ", gt.results.Select(x => x.InputsText + ":" + x.Passed + ":" + x.Note).ToArray()));
                var bf = LangBox.ParseGoTest("{\"ImportPath\":\"stazher [stazher.test]\",\"Action\":\"build-output\",\"Output\":\"./main.go:6:5: declared and not used: x\\n\"}\n{\"Action\":\"build-fail\"}", "main.go");
                T(bf.buildFailed && bf.errLine == 6 && bf.results.Count == 1 && !bf.results[0].Passed, "go test: ошибка сборки " + bf.errLine + " " + bf.errText);
                string em; T(LangBox.ErrorLine("panic: boom\n\ngoroutine 1 [running]:\nmain.main()\n\t/work/.stazher/run/x/main.go:7 +0x18\n", "main.go", out em) == 7, "go run: строка паники");
            }
            // разбор проверок Java: строки ##TEST, ошибки javac, исключения (спринт 12)
            {
                var ck = LangBox.ParseCheck("##TEST|1990 × 2|PASS\n##TEST|пусто|FAIL|получено 1, ожидалось 0\n##TEST|падение|FAIL|исключение java.lang.ArithmeticException: / by zero (Main.java:4)\n", "", "Main.java");
                T(ck.results.Count == 3 && ck.results[0].Passed && !ck.results[1].Passed && ck.results[1].Note == "получено 1, ожидалось 0" && ck.errLine == 4 && !ck.buildFailed, "java: разбор ##TEST " + ck.results.Count + " " + ck.errLine);
                var jc = LangBox.ParseCheck("", "Main.java:3: error: cannot find symbol\n        return qtx * price;\n               ^\n  symbol:   variable qtx\n  location: class Main\n1 error\n", "Main.java");
                T(jc.buildFailed && jc.errLine == 3 && jc.errText.Contains("qtx") && jc.results.Count == 1 && !jc.results[0].Passed, "java: ошибка javac " + jc.errLine + " " + jc.errText);
                var jt = LangBox.ParseCheck("", "MainTest.java:5: error: cannot find symbol\n  symbol:   method total(int)\n", "Main.java");
                T(jt.buildFailed && jt.errLine < 0 && jt.results[0].Note.Contains("MainTest.java"), "java: ошибка в тестах " + (jt.results.Count > 0 ? jt.results[0].Note : "-"));
                var jx = LangBox.ParseCheck("", "Exception in thread \"main\" java.lang.StackOverflowError\n\tat Main.digitSum(Main.java:6)\n\tat Main.digitSum(Main.java:6)\n", "Main.java");
                T(jx.panicked && jx.errLine == 6 && jx.results.Count == 1, "java: исключение до проверок " + jx.errLine + " " + jx.errText);
                string je; T(LangBox.ErrorLine("Main.java:7: error: ';' expected\n        int x = 1\n                 ^\n1 error\n", "Main.java", out je) == 7 && je.Contains(";"), "javac: строка ошибки " + je);
                T(LangBox.ErrorLine("Exception in thread \"main\" java.lang.ArithmeticException: / by zero\n\tat Main.div(Main.java:3)\n\tat Main.main(Main.java:9)\n", "Main.java", out je) == 3 && je.Contains("by zero"), "java: строка исключения " + je);
                // C# (спринт 13): ошибки csc, исключения .NET, оборванный процесс тестов
                var cc = LangBox.ParseCheck("", "Program.cs(5,16): error CS0103: The name 'prce' does not exist in the current context\n", "Program.cs", 1);
                T(cc.buildFailed && cc.errLine == 5 && cc.errText.Contains("prce") && cc.errText.Contains("CS0103"), "c#: ошибка csc " + cc.errLine + " " + cc.errText);
                var cp = LangBox.ParseCheck("", "Tests.cs(5,37): error CS0122: 'Program.CartTotal(int, int)' is inaccessible due to its protection level\n", "Program.cs", 1);
                T(cp.buildFailed && cp.errLine < 0 && cp.results[0].Note.Contains("public"), "c#: private метод " + (cp.results.Count > 0 ? cp.results[0].Note : "-"));
                var cs = LangBox.ParseCheck("##TEST|1990 × 2|PASS\n", "Stack overflow.\nRepeated 261383 times:\n   at Program.Down(Int64)\n", "Program.cs", 134);
                T(cs.results.Count == 2 && cs.results[0].Passed && !cs.results[1].Passed && cs.results[1].Note.Contains("рекурси"), "c#: тесты оборвались " + cs.results.Count);
                var cx = LangBox.ParseCheck("", "Unhandled exception. System.DivideByZeroException: Attempted to divide by zero.\n   at Program.Main() in /work/.stazher/run/x/Program.cs:line 12\n", "Program.cs", 134);
                T(cx.panicked && cx.errLine == 12 && cx.errText.Contains("DivideByZero"), "c#: исключение " + cx.errLine + " " + cx.errText);
                string ce; T(LangBox.ErrorLine("Program.cs(16,33): error CS0266: Cannot implicitly convert type 'double' to 'int'. An explicit conversion exists (are you missing a cast?)\n", "Program.cs", out ce) == 16 && ce.Contains("(int)"), "c#: строка ошибки " + ce);
                var csharp = LangBox.For("csharp");
                T(csharp != null && csharp.extraFiles.ContainsKey("stazher-cs.sh") && csharp.extraFiles["Check.cs"].Contains("##TEST|") && !csharp.extraFiles["stazher-cs.sh"].Contains("\r"), "c#: раннер, Check.cs и скрипт сборки");
                // C++ (спринт 14): ошибки g++, санитайзеры, необработанное исключение
                var gp = LangBox.ParseCheck("", "main.cpp: In function 'int cartTotal(int, int)':\nmain.cpp:4:12: error: \u2018prce\u2019 was not declared in this scope; did you mean \u2018price\u2019?\n    4 |     return prce * qty;\n", "main.cpp", 1);
                T(gp.buildFailed && gp.errLine == 4 && gp.errText.Contains("price?"), "c++: ошибка g++ " + gp.errLine + " " + gp.errText);
                var gr = LangBox.ParseCheck("", "main.cpp:10:1: error: control reaches end of non-void function [-Werror=return-type]\n", "main.cpp", 1);
                T(gr.buildFailed && gr.errLine == 10 && gr.errText.Contains("return") && !gr.errText.Contains("[-Werror"), "c++: нет return " + gr.errText);
                var ga = LangBox.ParseCheck("##TEST|1990 × 2|PASS\n", "==417==ERROR: AddressSanitizer: heap-buffer-overflow on address 0x502000000024\nREAD of size 4 at 0x502000000024 thread T0\n    #0 0x563eaa50356d in at5(std::vector<int> const&) /work/.stazher/run/x/main.cpp:6\n    #1 0x563eaa5038a1 in operator() /work/.stazher/run/x/tests.cpp:3\n", "main.cpp", 134);
                T(ga.results.Count == 2 && !ga.results[1].Passed && ga.errLine == 6 && ga.results[1].Note.Contains("границ"), "c++: AddressSanitizer " + ga.errLine + " " + (ga.results.Count > 1 ? ga.results[1].Note : "-"));
                string gm; T(LangBox.ErrorLine("main.cpp:6:18: runtime error: division by zero\n    #0 0x55 in cartTotal(int, int) /work/.stazher/run/x/main.cpp:6\n", "main.cpp", out gm) == 6 && gm.Contains("деление на ноль"), "c++: UBSan " + gm);
                T(LangBox.ErrorLine("terminate called after throwing an instance of 'std::invalid_argument'\n  what():  stoi\n", "main.cpp", out gm) == -1 && gm.Contains("std::invalid_argument: stoi"), "c++: исключение " + gm);
                var cpp = LangBox.For("cpp");
                T(cpp != null && cpp.dockerfile != null && cpp.dockerfile.Contains("g++") && cpp.extraFiles["check.hpp"].Contains("##TEST|") && cpp.extraFiles["stazher-cpp.sh"].Contains("LC_ALL=C g++"), "c++: раннер, check.hpp и Dockerfile");
                // Rust (спринт 15): блоки ошибок rustc с подсказками, паники
                var re1 = LangBox.ParseCheck("", "error[E0425]: cannot find value `prce` in this scope\n --> main.rs:2:5\n  |\n2 |     prce * qty\n  |     ^^^^\n  |\nhelp: a local variable with a similar name exists\n\nerror[E0308]: mismatched types\n  --> main.rs:5:32\n   |\n 5 | fn total(prices: &Vec<i32>) -> i32 {\n   |    -----                       ^^^ expected `i32`, found `()`\n...\n10 |     sum;\n   |        - help: remove this semicolon to return this value\n\nerror: aborting due to 2 previous errors\n", "main.rs", 1);
                T(re1.buildFailed && re1.errLine == 2 && re1.errText.Contains("E0425") && re1.results[0].Note.Contains("убери ;"), "rust: ошибки rustc " + re1.errLine + " " + (re1.results.Count > 0 ? re1.results[0].Note : "-"));
                var re2 = LangBox.ParseCheck("##TEST|1990 × 2|FAIL|паника: attempt to divide by zero (main.rs:2)\n", "", "main.rs", 0);
                T(re2.errLine == 2 && !re2.results[0].Passed, "rust: паника в тесте " + re2.errLine);
                string rsm; T(LangBox.ErrorLine("thread 'main' (546) panicked at main.rs:7:5:\nattempt to divide by zero\nnote: run with `RUST_BACKTRACE=1`\n", "main.rs", out rsm) == 7 && rsm.Contains("деление на ноль"), "rust: паника при запуске " + rsm);
                var re3 = LangBox.ParseCheck("##TEST|ok|PASS\n", "\nthread 'main' (583) has overflowed its stack\nfatal runtime error: stack overflow, aborting\n", "main.rs", 134);
                T(re3.results.Count == 2 && re3.results[1].Note.Contains("рекурси"), "rust: переполнение стека");
                var rust = LangBox.For("rust");
                T(rust != null && rust.extraFiles["check.rs"].Contains("##TEST|") && rust.extraFiles["stazher-rs.sh"].Contains("rustc"), "rust: раннер и check.rs");
                // PHP (спринт 16): php -l, непойманные исключения, предупреждения
                var pp = LangBox.ParseCheck("", "Parse error: syntax error, unexpected token \"}\", expecting \";\" in main.php on line 4\nErrors parsing main.php\n", "main.php", 1);
                T(pp.buildFailed && pp.errLine == 4 && pp.errText.Contains("не хватает «;»"), "php: синтаксис " + pp.errLine + " " + pp.errText);
                var pt = LangBox.ParseCheck("##TEST|1990 × 2|FAIL|исключение ErrorException: Undefined variable $prce (main.php:3)\n", "", "main.php", 0);
                T(pt.errLine == 3 && !pt.results[0].Passed, "php: исключение в тесте " + pt.errLine);
                string pm; T(LangBox.ErrorLine("PHP Fatal error:  Uncaught DivisionByZeroError: Division by zero in /work/.stazher/run/x/main.php:7\nStack trace:\n#0 /work/.stazher/run/x/main.php(7): intdiv()\n", "main.php", out pm) == 7 && pm.Contains("деление на ноль"), "php: необработанное исключение " + pm);
                T(LangBox.ErrorLine("Warning: Undefined variable $prce in /work/.stazher/run/x/main.php on line 3\n", "main.php", out pm) == 3 && pm.Contains("$prce"), "php: предупреждение " + pm);
                var php = LangBox.For("php");
                T(php != null && php.extraFiles["check.php"].Contains("##TEST|") && php.extraFiles.ContainsKey("stazher-tests.php") && php.src == "main.php", "php: раннер и check.php");
                // Kotlin (спринт 17): kotlinc, исключения JVM с номером строки Main.kt
                var kc = LangBox.ParseCheck("", "Main.kt:2:12: error: unresolved reference 'prce'.\n    return prce * qty\n           ^^^^\nMain.kt:7:14: error: only safe (?.) or non-null asserted (!!.) calls are allowed on a nullable receiver of type 'String?'.\n", "Main.kt", 1);
                T(kc.buildFailed && kc.errLine == 2 && kc.errText.Contains("prce") && kc.errText.Contains("не найдено"), "kotlin: имя не найдено " + kc.errLine + " " + kc.errText);
                var kv = LangBox.ParseCheck("", "Main.kt:3:5: error: 'val' cannot be reassigned.\n    total += 5\n    ^^^^^\n", "Main.kt", 1);
                T(kv.buildFailed && kv.errLine == 3 && kv.errText.Contains("var"), "kotlin: val " + kv.errText);
                var kt = LangBox.ParseCheck("##TEST|x|FAIL|исключение ArithmeticException: / by zero (Main.kt:1)\n", "", "Main.kt", 0);
                T(kt.errLine == 1 && !kt.results[0].Passed, "kotlin: исключение в тесте " + kt.errLine);
                string km; T(LangBox.ErrorLine("Exception in thread \"main\" java.lang.ArrayIndexOutOfBoundsException: Index 5 out of bounds for length 2\n\tat java.base/java.util.Arrays$ArrayList.get(Arrays.java:4266)\n\tat MainKt.main(Main.kt:3)\n\tat MainKt.main(Main.kt)\n", "Main.kt", out km) == 3, "kotlin: исключение при запуске " + km);
                var kotlin = LangBox.For("kotlin");
                T(kotlin != null && kotlin.extraFiles["Check.kt"].Contains("##TEST|") && kotlin.extraFiles["stazher-kt.sh"].Contains("K2JVMCompiler") && kotlin.src == "Main.kt" && kotlin.RunSec > LangBox.RunTimeoutSec, "kotlin: раннер и Check.kt");
                // Swift (спринт 18): swiftc, падения из отчёта swift-backtrace со строкой main.swift
                var sc1 = LangBox.ParseCheck("", "main.swift:3:12: error: cannot find 'prce' in scope\n    return prce * qty\n           ^~~~\n", "main.swift", 1);
                T(sc1.buildFailed && sc1.errLine == 3 && sc1.errText.Contains("prce") && sc1.errText.Contains("не найдено"), "swift: имя не найдено " + sc1.errLine + " " + sc1.errText);
                var sc2 = LangBox.ParseCheck("", "out/main.swift:6:1: error: cannot assign to value: 'x' is a 'let' constant\n", "main.swift", 1);
                T(sc2.buildFailed && sc2.errLine == 6 && sc2.errText.Contains("var"), "swift: let в тестах " + sc2.errText);
                var sc3 = LangBox.ParseCheck("3980\n##TEST|1990 × 2|PASS\n", "Swift/ContiguousArrayBuffer.swift:691: Fatal error: Index out of range\n\n*** Program crashed: Illegal instruction at 0x00007de77a6bfd76 ***\n\nThread 0 \"prog\" crashed:\n\n  0      0x00007de77a6bfd76 _assertionFailure(_:_:file:line:flags:) + 438 in libswiftCore.so\n  2 [ra] 0x0000583b711c5e74 at(_:) + 99 in prog at out/main.swift:19:13\n  3 [ra] 0x0000583b711c661a closure #12 in tests() + 25 in prog at /work/.stazher/run/x/tests.swift:13:37\n", "main.swift", 132);
                T(sc3.results.Count == 2 && sc3.results[0].Passed && !sc3.results[1].Passed && sc3.results[1].Note.Contains("границ") && sc3.errLine == 19, "swift: падение в тестах " + sc3.errLine);
                string sm; T(LangBox.ErrorLine("\n*** Signal 4: Backtracing from 0x5580b3987dd8... done ***\n\n*** Swift runtime failure: arithmetic overflow ***\n\n  0 [inlined] [system] 0x00005580b3987dd8 Swift runtime failure: arithmetic overflow in prog at //<compiler-generated>\n  1                    0x00005580b3987dd8 add(_:_:) + 88 in prog at /work/.stazher/run/x/main.swift:2:14\n  2 [ra]               0x00005580b3987e9e main + 45 in prog at /work/.stazher/run/x/main.swift:5:7\n", "main.swift", out sm) == 2 && sm.Contains("переполнение"), "swift: переполнение " + sm);
                T(LangBox.ErrorLine("Stazher/main.swift:4: Fatal error: Unexpectedly found nil while unwrapping an Optional value\n\n*** Program crashed: Illegal instruction at 0x0000702b9969bd76 ***\n", "main.swift", out sm) == 4 && sm.Contains("nil"), "swift: nil в ! " + sm);
                var swift = LangBox.For("swift");
                T(swift != null && swift.extraFiles["check.swift"].Contains("##TEST|") && swift.extraFiles["stazher-swift.sh"].Contains("swiftc") && swift.src == "main.swift", "swift: раннер и check.swift");
                var java = LangBox.For("java");
                T(java != null && java.extraFiles.ContainsKey("Check.java") && java.extraFiles["Check.java"].Contains("##TEST|") && java.testMarker == "Check.", "java: раннер и Check.java");
            }
            if (report != null) report.Add("окружение: сценариев " + list.Count + ", шагов " + list.Sum(s => s.steps.Count));
            return bad;
        }

        public class FakeEnv : IEnvRunner
        {
            public Dictionary<string, string> sandbox = new Dictionary<string, string>(), host = new Dictionary<string, string>();
            public string http, hostAll, sandboxAll;
            public ProcResult Sandbox(string c) { string o = sandboxAll; if (o == null) sandbox.TryGetValue(c, out o); return new ProcResult { Code = o != null ? 0 : 1, Out = o ?? "" }; }
            public ProcResult Host(string a) { string o = hostAll; if (o == null) host.TryGetValue(a, out o); return new ProcResult { Code = o != null ? 0 : 1, Out = o ?? "" }; }
            public string Http(string url, out int status) { status = http != null ? 200 : 0; return http; }
            public string HostPath(string p) { return p; }
        }

        // Путь проходится до конца, зависимости есть в пути, в пути нет тем чужой ветки языка
        public static bool PathOk(TrackPath path, string profession, string lang, out string why)
        {
            why = null;
            var done = new HashSet<string>(); int steps = 0;
            for (var cur = path.Current(done); cur != null && steps < 5000; cur = path.Current(done)) { done.Add(cur.id); steps++; }
            if (steps != path.Tasks.Length || !path.Complete(done)) { why = "ТУПИК: пройдено " + steps; return false; }
            var ids = new HashSet<string>(path.Topics.Select(tp => tp.id));
            var lost = path.Topics.SelectMany(tp => tp.requires).Where(r => !ids.Contains(r)).ToList();
            if (lost.Count > 0) { why = "зависимости вне пути: " + string.Join(", ", lost.ToArray()); return false; }
            if (profession != "fullstack")
            {
                var alien = path.Topics.Where(tp => !string.IsNullOrEmpty(tp.lang) && tp.lang != lang).Select(tp => tp.id).ToList();
                if (alien.Count > 0) { why = "темы чужой ветки: " + string.Join(", ", alien.ToArray()); return false; }
            }
            return true;
        }

        // Синтаксис кода задачи без запуска: null — ок
        static string SyntaxOf(string lang, string code)
        {
            switch (Syntax.Norm(lang))
            {
                case "python":
                    try { Parser.ParseProgram(code); return null; } catch (PyError e) { return e.Message; }
                case "javascript": { var e = JsRun.SyntaxCheck(code); return e == null ? null : e.Text; }
                case "typescript": { var e = JsRun.SyntaxCheck(TsStrip.Strip(code)); return e == null ? null : e.Text; }
                default: return null;
            }
        }

        // Вывод программы «Что выведет?» настоящим движком игры; null — язык без запуска
        static string OutputOf(string lang, string code, out string err)
        {
            err = null;
            switch (Syntax.Norm(lang))
            {
                case "python": { var r = PyRun.Run(code, null); if (r.Error != null) err = r.Error.Message; return r.Printed; }
                case "javascript":
                case "typescript":
                    {
                        var r = JsRun.RunFull(Syntax.Norm(lang) == "typescript" ? TsStrip.Strip(code) : code);
                        if (r.Error != null) err = r.Error.Text; return r.Output;
                    }
                default: return null;
            }
        }

        public static string Check(TaskData t)
        {
            switch (t.Mode)
            {
                case "choice":
                    if (t.answer == null || t.answer.Length == 0 || t.answer.Any(a => a < 0 || a >= t.options.Length)) return "неверные индексы ответа";
                    if (t.type == "clickbug" || t.IsClickBug)
                    {
                        int n = (t.starter ?? "").Replace("\r\n", "\n").Split('\n').Length;
                        if (t.bugLine < 1 || t.bugLine > n) return "bug_line " + t.bugLine + " вне кода (строк " + n + ")";
                        var se = SyntaxOf(t.language, t.starter);
                        if (se != null) return "код с багом не разбирается: " + se;
                    }
                    return TaskChecks.Choice(t, t.answer.ToList()).Correct ? null : "эталонный ответ не принят";
                case "predict":
                    {
                        if (string.IsNullOrEmpty(t.output)) return "нет эталона вывода";
                        string err, got = OutputOf(t.language, t.starter, out err);
                        if (err != null) return "программа упала: " + err;
                        string note;
                        if (got != null && !TaskChecks.Predict(t, got, out note)) return "вывод движка игры не совпал с эталоном (" + note + "): " + got.Replace("\n", "⏎");
                        return TaskChecks.Predict(t, t.output, out note) ? null : "эталон не принят: " + note;
                    }
                case "cloze":
                    {
                        if (t.blanks == null || t.blanks.Count == 0) return "нет пропусков";
                        for (int i = 0; i < t.blanks.Count; i++)
                        {
                            var b = t.blanks[i];
                            if (!(t.starter ?? "").Contains("[[" + (i + 1) + "]]")) return "в коде нет метки [[" + (i + 1) + "]]";
                            if (b.answers.Length == 0 && string.IsNullOrEmpty(b.regex)) return "пропуск " + (i + 1) + " без ответа";
                            if (b.answers.Length > 0 && !TaskChecks.ClozeOne(b, b.answers[0])) return "пропуск " + (i + 1) + ": эталон не принят";
                            if (TaskChecks.ClozeOne(b, "___")) return "пропуск " + (i + 1) + ": принимает что угодно";
                        }
                        if (t.starter.Contains("[[" + (t.blanks.Count + 1) + "]]")) return "меток больше, чем пропусков";
                        var se = SyntaxOf(t.language, TaskChecks.ClozeCode(t, null));
                        return se == null ? null : "код с ответами не разбирается: " + se;
                    }
                case "parsons":
                    {
                        if (t.lines == null || t.lines.Length < 2) return "мало строк";
                        int w = Syntax.IndentWidth(t.language);
                        Func<string[], List<KeyValuePair<string, int>>> sol = ls => ls.Select(l => new KeyValuePair<string, int>(l.Trim(), TaskChecks.IndentOf(l, w))).ToList();
                        string note;
                        if (!TaskChecks.Parsons(t, sol(t.lines), out note)) return "эталон не принят: " + note;
                        if (t.alternatives != null)
                            foreach (var a in t.alternatives) if (!TaskChecks.Parsons(t, sol(a), out note)) return "допустимый порядок не принят: " + note;
                        if (TaskChecks.Parsons(t, sol(t.lines.Reverse().ToArray()), out note)) return "обратный порядок принят";
                        if (TaskChecks.IndentMatters(t.language) && t.lines.Any(l => TaskChecks.IndentOf(l, w) > 0)
                            && TaskChecks.Parsons(t, t.lines.Select(l => new KeyValuePair<string, int>(l.Trim(), 0)).ToList(), out note)) return "код без отступов принят";
                        var trimmed = new HashSet<string>(t.lines.Select(l => l.Trim()));
                        if ((t.distractors ?? new string[0]).Any(x => trimmed.Contains(x.Trim()))) return "лишняя строка совпадает с нужной";
                        var se = SyntaxOf(t.language, string.Join("\n", t.lines));
                        return se == null ? null : "собранный код не разбирается: " + se;
                    }
                case "box":
                    {
                        var spec = LangBox.For(t.language);
                        if (spec == null) return "нет раннера для языка " + t.language;
                        if (string.IsNullOrEmpty(t.testCode) || (spec.testMarker != null && !t.testCode.Contains(spec.testMarker))) return "нет тестов (" + spec.testShow + ")";
                        if (string.IsNullOrEmpty(t.solution)) return "нет эталона";
                        if (!string.IsNullOrEmpty(t.entry) && (!t.solution.Contains(t.entry) || !t.testCode.Contains(t.entry))) return "функции " + t.entry + " нет в эталоне или в тестах";
                        if (t.requirements == null || t.requirements.Count == 0) return "нет требований для проверки без Docker";
                        var rq = TaskChecks.Static(t.solution, t.requirements);
                        if (rq.Any(x => !x.Passed)) return "эталон не выполняет требования: " + string.Join(" | ", rq.Where(x => !x.Passed).Select(x => x.InputsText).ToArray());
                        if (TaskChecks.Static(t.starter, t.requirements).All(x => x.Passed)) return "заготовка выполняет все требования";
                        return null;   // сами тесты гоняет меню «Стажёр → Проверить ветки языков в Docker»
                    }
                case "ts":
                    {
                        var r = JsRun.Check(TsStrip.Strip(t.solution ?? ""), t.entry, t.testCases);
                        if (!r.AllPassed) { JsError e; var cr = TaskChecks.FromJs(r, out e); return "эталон не прошёл: " + string.Join(" | ", cr.Where(x => !x.Passed).Select(x => x.Note).ToArray()); }
                        if (t.requirements != null && t.requirements.Count > 0)
                        {
                            var rq = TaskChecks.Static(t.solution, t.requirements);
                            if (rq.Any(x => !x.Passed)) return "эталон не выполняет требования: " + string.Join(" | ", rq.Where(x => !x.Passed).Select(x => x.InputsText).ToArray());
                            if (TaskChecks.Static(t.starter, t.requirements).All(x => x.Passed)) return "заготовка выполняет все требования";
                        }
                        return JsRun.Check(TsStrip.Strip(t.starter ?? ""), t.entry, t.testCases).AllPassed ? "заготовка проходит тесты" : null;
                    }
                case "static":
                    {
                        var good = TaskChecks.Static(t.solution, t.testCases);
                        if (good.Count == 0 || good.Any(x => !x.Passed)) return "эталон не прошёл: " + string.Join(" | ", good.Where(x => !x.Passed).Select(x => x.InputsText).ToArray());
                        return TaskChecks.Static(t.starter, t.testCases).All(x => x.Passed) ? "заготовка проходит все требования" : null;
                    }
                case "sql":
                    {
                        SqlResult last;
                        var r = TaskChecks.Sql(t.solution, t.testCases, out last);
                        return r.Count > 0 && r.All(x => x.Passed) ? null : "эталон не прошёл: " + string.Join(" | ", r.Select(x => x.Note).ToArray());
                    }
                case "js":
                    {
                        var r = JsRun.Check(t.solution, t.entry, t.testCases);
                        if (r.AllPassed) return null;
                        JsError e; var cr = TaskChecks.FromJs(r, out e);
                        return "эталон не прошёл: " + string.Join(" | ", cr.Where(x => !x.Passed).Select(x => x.Note).ToArray());
                    }
                default:
                    {
                        List<CheckResult> r;
                        if (string.IsNullOrEmpty(t.entry)) r = PyRun.Check(t.solution, t.tests);
                        else r = PyRun.CheckFunction(t.solution, t.entry, t.testCases.OfType<Dictionary<string, object>>()
                            .Select(d => { object i, x; d.TryGetValue("input", out i); d.TryGetValue("expected", out x); return new FuncTest(i, x); }).ToList());
                        return r.Count > 0 && r.All(x => x.Passed) ? null : "эталон не прошёл: " + string.Join(" | ", r.Where(x => !x.Passed).Select(x => x.Note).ToArray());
                    }
            }
        }
    }
}
