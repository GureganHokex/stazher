// Обеденный перерыв: таймер, горожане (лимит 60 гуманитариев за обед), монеты, здоровье стажёра, шум выстрелов.
// Город — City.cs, горожане — Citizens.cs, оружие — Weapons.cs, цифры — Balance.cs (lunch.json).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    // Дверь из офиса на обед
    public class ExitDoor : Interactable
    {
        public GameRoot game;
        public override string Prompt
        {
            get
            {
                if (game == null || game.Work == null) return "[E] Выйти на обед";
                string why;
                return game.Work.CanLunch(out why) ? "[E] Выйти на обед" : "[E] Выход";
            }
        }
        public override void Interact(GameRoot g) { g.TryStartLunch(); }
        public override PlaqueInfo Plaque(GameRoot g) { return g.DoorPlaque(); }
    }

    // Итоги одного обеда
    public class LunchReport
    {
        public int coins, kills, escaped, hidden, fines, spent, bestSeries, lost, techHits, hpLeft;
        public bool knockedOut, coupon, deanMet, earlyReturn;
        public float seconds;
    }

    // ======================= один обед =======================
    public class LunchRun
    {
        static BalanceData B { get { return Balance.D; } }
        public int Cap { get { return B.cap; } }

        public readonly float duration;
        public float timeLeft;
        public int coins, kills, fines, spawned, escaped, hidden, spent, techHits, bestSeries;
        public bool Paused;
        public int series; public float seriesAt = -99f;
        public int penaltyShown; public float penaltyAt = -99f;
        public Action<string> Say;
        public Action<string> Coupon;

        // здоровье стажёра
        public int hp, maxHp;
        public float hurtAt = -99f, stunUntil, slowUntil, energyUntil;
        public bool knockedOut, hoodie, couponDropped, deanMet;
        public int Filming { get; private set; }

        readonly CityRefs city;
        readonly Func<Vector3> playerPos;
        readonly Func<Transform> cam;
        readonly List<CityNpc> npcs = new List<CityNpc>();
        readonly List<CitizenDef> hiddenInArchive = new List<CitizenDef>();
        readonly List<GameObject> tracked = new List<GameObject>();
        public readonly System.Random Rnd = new System.Random();
        float spawnT, deanAt = -1f, lodT;
        // замер FPS за обед (S2-12): средний и худший 1% кадров
        readonly List<float> frames = new List<float>(8192);
        public int MaxAlive { get; private set; }

        public readonly bool training;   // первый обед с обучением: урон вдвое меньше, замечают ближе, подписи видно издалека

        public LunchRun(CityRefs city, float seconds, Func<Vector3> playerPos, Func<Transform> cam, bool hoodie, bool training = false)
        {
            this.city = city; duration = timeLeft = seconds; this.playerPos = playerPos; this.cam = cam; this.hoodie = hoodie; this.training = training;
            maxHp = hp = B.playerHp;
            // на обучении первые юристы гуляют прямо на проспекте
            if (training) { var law = Balance.Citizen("lawyer"); for (int i = 0; i < 4 && law != null; i++) { var at = SpawnPoint("av", true); if (at != null) { Add(law, at.Value); spawned++; } } }
            // сразу на улицах: гуманитарии в переулках и на площади, технари на проспекте
            for (int i = 0; i < 10; i++) SpawnHumanitarian(true);
            for (int i = 0; i < 4; i++) SpawnTechie(true);
            spawnT = B.spawnEvery;
            if (Rnd.NextDouble() < B.deanChance) deanAt = Mathf.Lerp(60f, Mathf.Max(90f, seconds - 90f), (float)Rnd.NextDouble());
        }

        public CityRefs City { get { return city; } }
        public Vector3 PlayerPos { get { return playerPos(); } }
        public bool Over { get { return timeLeft <= 0f || knockedOut; } }
        public float Elapsed { get { return duration - timeLeft; } }
        public int AliveHumanitarians { get { int n = 0; foreach (var c in npcs) if (c != null && c.Alive && c.Humanitarian) n++; return n; } }
        public int AliveTechies { get { int n = 0; foreach (var c in npcs) if (c != null && c.Alive && !c.Humanitarian) n++; return n; }  }
        public int Left { get { return Mathf.Max(0, Cap - spawned); } }
        public float NoticeRange { get { return B.noticeRange * (Filming > 0 ? 2f : 1f) * (training ? 0.6f : 1f); } }
        public float PlayerSpeedMul
        {
            get
            {
                if (Time.time < stunUntil) return 0f;
                float m = Time.time < slowUntil ? 0.5f : 1f;
                if (Time.time < energyUntil) m *= 1f + B.energySpeed;
                return m;
            }
        }

        public void Tick(float dt)
        {
            if (Paused) return;
            frames.Add(Time.unscaledDeltaTime);
            timeLeft = Mathf.Max(0f, timeLeft - dt);
            spawnT -= dt;
            if (spawnT <= 0f)
            {
                spawnT = B.spawnEvery;
                if (AliveHumanitarians < B.maxHumanitarians && spawned < Cap) SpawnHumanitarian(false);
                if (AliveTechies < B.maxTechies && Rnd.NextDouble() < 0.6) SpawnTechie(false);
            }
            if (deanAt > 0f && Elapsed >= deanAt) { deanAt = -1f; SpawnDean(); }
            if (series > 0 && Time.unscaledTime - seriesAt > 2.2f) series = 0;
            npcs.RemoveAll(n => n == null);
            foreach (var n in npcs.ToArray()) if (n.Recyclable) Recycle(n);
            int film = 0; foreach (var n in npcs) if (n.Alive && n.State == CityNpc.St.Film) film++;
            Filming = film;
            MaxAlive = Mathf.Max(MaxAlive, npcs.Count);
            // подписи над головами — только у тех, кто рядом
            lodT -= dt;
            if (lodT <= 0f)
            {
                lodT = 0.3f; var p = playerPos();
                float lod = training ? 60f : 28f;
                foreach (var n in npcs) if (n.Alive) n.SetLabel((n.transform.position - p).sqrMagnitude < lod * lod);
            }
        }

        // ---------- кто и где появляется ----------
        // Точка появления вне поля зрения: из дверей контор и из концов переулков
        Vector3? SpawnPoint(string street, bool initial)
        {
            var p = playerPos(); var c = cam != null ? cam() : null;
            var cands = new List<Vector3>();
            if (initial) { for (int i = 0; i < 12; i++) cands.Add(city.RandomNode(Rnd, street) + new Vector3((float)Rnd.NextDouble() * 4f - 2f, 0, (float)Rnd.NextDouble() * 4f - 2f)); }
            else
            {
                foreach (var d in city.doors) if (d.kind == "door" && (street == null || d.street == street)) cands.Add(d.pos);
                foreach (var e in city.alleyEnds) if (street == null || city.StreetAt(e) != null && city.StreetAt(e).id == street) cands.Add(e);
            }
            for (int tries = 0; tries < 24 && cands.Count > 0; tries++)
            {
                var q = cands[Rnd.Next(cands.Count)];
                var to = q - p; to.y = 0; float d = to.magnitude;
                if (d < (initial ? 18f : 14f)) continue;
                if (c != null && d < 45f)
                {
                    var f = c.forward; f.y = 0;
                    if (Vector3.Dot(f.normalized, to / d) > 0.35f && d < 45f && !initial) continue;   // на глазах не появляются
                }
                q.y = 0f; return q;
            }
            return null;
        }

        CitizenDef PickHumanitarian(out string street)
        {
            var list = new List<CitizenDef>(); float total = 0f;
            foreach (var c in B.citizens) if (c.humanitarian && c.weight > 0f) { list.Add(c); total += c.weight; }
            float r = (float)Rnd.NextDouble() * total;
            foreach (var c in list) { r -= c.weight; if (r <= 0f) { street = Rnd.NextDouble() < 0.75 ? c.street : null; return c; } }
            street = null; return list[0];
        }

        void SpawnHumanitarian(bool initial)
        {
            if (spawned >= Cap) return;
            string street; var def = PickHumanitarian(out street);
            var at = SpawnPoint(street, initial) ?? SpawnPoint(null, initial);
            if (at == null) return;
            if (def.id == "critic" && spawned + 3 <= Cap)
            {
                // искусствоведы ходят по трое
                var lead = Add(def, at.Value); spawned++;
                for (int i = 1; i <= 2; i++) { var f = Add(def, at.Value + new Vector3(i * 0.8f, 0, -0.8f)); f.leader = lead; f.SetScatter(i); spawned++; }
                return;
            }
            if (def.id == "critic") def = Balance.Citizen("lawyer");
            Add(def, at.Value); spawned++;
        }

        void SpawnTechie(bool initial)
        {
            var list = new List<CitizenDef>(); foreach (var c in B.citizens) if (!c.humanitarian) list.Add(c);
            if (list.Count == 0) return;
            var def = list[Rnd.Next(list.Count)];
            string street = Rnd.NextDouble() < 0.6 ? (Rnd.NextDouble() < 0.5 ? "av" : "square") : null;
            var at = SpawnPoint(street, initial) ?? SpawnPoint(null, initial);
            if (at != null) Add(def, at.Value);
        }

        void SpawnDean()
        {
            var def = Balance.Citizen("dean"); if (def == null) return;
            var at = SpawnPoint("uni", false) ?? SpawnPoint(null, false); if (at == null) return;
            Add(def, at.Value); deanMet = true;
            if (Say != null) Say("В городе декан гумфака! 300 здоровья, за него купон −20% в мастерской.");
        }

        CityNpc Add(CitizenDef def, Vector3 at)
        {
            var n = CityNpc.Create(this, def, at, Rnd);
            npcs.Add(n); return n;
        }

        // Юрист позвонил коллегам: через 10 с прибегает второй, с портфелем
        public void CallColleague(CityNpc from)
        {
            if (spawned >= Cap) return;
            var def = Balance.Citizen("lawyer"); if (def == null) return;
            var near = FleeDoor(from != null ? from.transform.position : playerPos(), 0);
            var n = Add(def, near); spawned++;
            n.colleague = true; n.Alert();
            if (Say != null) Say("Прибежал коллега юриста — с портфелем наперевес.");
        }

        // Стихи: соседи-гуманитарии идут послушать
        public void PoemAt(Vector3 at, CityNpc poet)
        {
            foreach (var n in npcs) if (n != poet && n.Alive && n.Calm && (n.transform.position - at).sqrMagnitude < 22f * 22f) n.Listen(at);
        }

        // Выстрел: кто слышит — пугается; спрятавшиеся в архиве выходят на шум
        public void Noise(Vector3 at, float radius)
        {
            if (radius <= 0f) return;
            foreach (var n in npcs) if (n.Alive && (n.transform.position - at).sqrMagnitude < radius * radius) n.Hear(at);
            if (hiddenInArchive.Count > 0 && city.archiveDoor != Vector3.zero && (city.archiveDoor - at).sqrMagnitude < (radius + 12f) * (radius + 12f))
            {
                foreach (var def in hiddenInArchive) { var n = Add(def, city.archiveDoor); n.Curious(at); }
                if (Say != null) Say("Из архива выглянули историки: что за шум?");
                hidden -= hiddenInArchive.Count; hiddenInArchive.Clear();
            }
        }

        // Ближайшая дверь, которая не за спиной у стажёра (skip — искусствоведы разбегаются в разные стороны)
        public Vector3 FleeDoor(Vector3 from, int skip)
        {
            var p = playerPos(); p.y = 0;
            var scored = new List<KeyValuePair<float, Vector3>>();
            foreach (var d in city.doors)
            {
                if (d.kind != "door" || !d.open) continue;
                var toDoor = d.pos - from; toDoor.y = 0;
                var toPlayer = p - from; toPlayer.y = 0;
                float score = toDoor.magnitude;
                if (toPlayer.sqrMagnitude > 0.01f && Vector3.Dot(toDoor.normalized, toPlayer.normalized) > 0.2f) score += 60f;   // бежать мимо стажёра — плохая идея
                scored.Add(new KeyValuePair<float, Vector3>(score, d.pos));
            }
            if (scored.Count == 0) return from;
            scored.Sort((a, b) => a.Key.CompareTo(b.Key));
            return scored[Mathf.Min(skip * 2, scored.Count - 1)].Value;
        }

        // ---------- стажёр ----------
        public void HurtPlayer(int dmg, Vector3 from, float stun, float slow)
        {
            if (knockedOut || Paused) return;
            if (hoodie) dmg = Mathf.RoundToInt(dmg * (1f - B.hoodieArmor));
            if (training) dmg = Mathf.Max(1, dmg / 2);
            hp = Mathf.Max(0, hp - dmg); hurtAt = Time.unscaledTime;
            if (stun > 0f) stunUntil = Mathf.Max(stunUntil, Time.time + stun);
            if (slow > 0f) slowUntil = Mathf.Max(slowUntil, Time.time + slow);
            if (hp <= 0) knockedOut = true;
        }
        public void SlowPlayer(float seconds) { slowUntil = Mathf.Max(slowUntil, Time.time + seconds); hurtAt = Time.unscaledTime - 0.3f; }
        public void Heal(int n) { hp = Mathf.Min(maxHp, hp + n); }
        public void Energy() { energyUntil = Time.time + B.energySeconds; }

        // ---------- счёт ----------
        public void OnKill(CityNpc n)
        {
            if (!n.Humanitarian) return;
            coins += B.reward; kills++;
            series += B.reward; seriesAt = Time.unscaledTime;
            bestSeries = Mathf.Max(bestSeries, series);
            if (n.Type == "dean" && !couponDropped) { couponDropped = true; if (Coupon != null) Coupon("Декан повержен! Купон «−20% в мастерской» — твой."); }
        }

        public void OnWrongHit(CityNpc n, string cry)
        {
            coins -= B.penalty; fines += B.penalty; techHits++;
            penaltyShown = B.penalty; penaltyAt = Time.unscaledTime;
            if (Say != null) Say("«" + cry + "» Это технарь: штраф " + B.penalty + " монет.");
        }

        public void OnEscaped(CityNpc n)
        {
            if (n.Humanitarian) escaped++;
            n.gameObject.SetActive(false); n.Recyclable = true;   // исчез в подъезде; тело заберём в пул в своём кадре
        }

        public void OnHidden(CityNpc n)
        {
            hidden++; hiddenInArchive.Add(n.def);
            n.gameObject.SetActive(false); n.Recyclable = true;
        }

        // Покупки в городе: сначала из монет обеда, потом из кошелька
        public int SpendFromLunch(int price) { int take = Mathf.Clamp(coins, 0, price); coins -= take; spent += price; return price - take; }

        // Ближайший спокойный гуманитарий — для маркера обучения
        public CityNpc NearestTarget(Vector3 from)
        {
            CityNpc best = null; float bd = float.MaxValue;
            foreach (var n in npcs) if (n != null && n.Alive && n.Humanitarian && n.gameObject.activeSelf) { float d = (n.transform.position - from).sqrMagnitude; if (d < bd) { bd = d; best = n; } }
            return best;
        }

        // Тело — обратно в пул города
        public void Recycle(CityNpc n) { npcs.Remove(n); city.Recycle(n); }

        public void Track(GameObject go) { tracked.RemoveAll(g => g == null); tracked.Add(go); }

        // Пауза останавливает и физику: падающие тела и выпавшие портфели замирают вместе с игрой
        public void SetPaused(bool p) { Paused = p; Physics.simulationMode = p ? SimulationMode.Script : SimulationMode.FixedUpdate; }

        // Средний FPS и FPS худшего 1% кадров
        public string PerfLine()
        {
            if (frames.Count < 30) return "мало кадров";
            var f = new List<float>(frames); f.Sort();
            float sum = 0f; foreach (var x in f) sum += x;
            float avg = f.Count / Mathf.Max(0.001f, sum);
            int n = Mathf.Max(1, f.Count / 100); float worst = 0f; for (int i = f.Count - n; i < f.Count; i++) worst += f[i];
            return "средний FPS " + Mathf.RoundToInt(avg) + ", худший 1% — " + Mathf.RoundToInt(n / Mathf.Max(0.001f, worst)) + " FPS, кадров " + f.Count + ", горожан одновременно до " + MaxAlive;
        }

        public LunchReport Report(bool early)
        {
            return new LunchReport { coins = coins, kills = kills, escaped = escaped, hidden = hidden, fines = fines, spent = spent, bestSeries = bestSeries,
                                     techHits = techHits, hpLeft = hp, knockedOut = knockedOut, coupon = couponDropped, deanMet = deanMet, seconds = Elapsed, earlyReturn = early };
        }

        // Только для проверки в редакторе: ближайший живой горожанин встаёт перед игроком
        public void DebugPull(Vector3 at, float yaw)
        {
            CityNpc best = null; float bd = float.MaxValue;
            foreach (var n in npcs) if (n != null && n.Alive) { float d = (n.transform.position - at).sqrMagnitude; if (d < bd) { bd = d; best = n; } }
            if (best != null) best.DebugPlace(at, yaw);
        }

        // Только для инструментов редактора: горожанин нужного вида в точке — стоит и ждёт (проверка падений)
        public CityNpc DevSpawn(string id, Vector3 at, float yaw)
        {
            var def = Balance.Citizen(id); if (def == null) return null;
            var n = Add(def, at); n.DebugPlace(at, yaw); return n;
        }

        public void Cleanup()
        {
            Physics.simulationMode = SimulationMode.FixedUpdate;
            foreach (var n in npcs.ToArray()) if (n != null) city.Recycle(n);
            npcs.Clear(); hiddenInArchive.Clear();
            foreach (var g in tracked) if (g != null) UnityEngine.Object.Destroy(g);
            tracked.Clear();
            Gore.Clear();
        }
    }

    // ======================= кровь =======================
    public static class Gore
    {
        public static bool Enabled = true;
        static readonly List<GameObject> pools = new List<GameObject>();
        const int MaxPools = 64;

        public static void Hit(Vector3 pos, Vector3 dir)
        {
            if (!Enabled) return;
            Burst(pos, dir, new Color(0.62f, 0.04f, 0.06f, 1f), 28, 1.2f);
            // пятно на асфальте под местом удара
            Pool(new Vector3(pos.x, 0.02f, pos.z) + new Vector3(dir.x, 0, dir.z).normalized * 0.4f, 0.35f);
            // брызги на стене, если она рядом за спиной у жертвы
            RaycastHit h; var d = dir; if (d.sqrMagnitude < 0.01f) return; d.Normalize();
            int mask = Physics.DefaultRaycastLayers & ~(1 << 2);
            if (Physics.Raycast(pos + d * 0.45f, d, out h, 2.8f, mask, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<CityNpc>() == null && Mathf.Abs(h.normal.y) < 0.5f)
                Splat(h.point + h.normal * 0.02f, h.normal, UnityEngine.Random.Range(0.35f, 0.7f));
        }

        // Пыль от пули в стену
        public static void Dust(Vector3 pos, Vector3 normal)
        {
            Burst(pos, normal, new Color(0.75f, 0.72f, 0.68f, 1f), 10, 0.2f);
        }

        static void Burst(Vector3 pos, Vector3 dir, Color c, int count, float gravity)
        {
            var mat = Look.FxMat(c, true, false);
            if (mat == null) return;
            var go = new GameObject("Burst"); go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.duration = 0.2f; main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            main.startColor = c;
            main.gravityModifier = gravity; main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.05f;
            if (dir.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(dir);
            var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = mat; pr.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            UnityEngine.Object.Destroy(go, 2f);
        }

        // Лужа, которая растекается за пару секунд
        public static void Pool(Vector3 pos, float radius = 0.9f)
        {
            if (!Enabled) return;
            var mat = Look.FxMat(new Color(0.42f, 0.02f, 0.04f, 0.92f), true, false);
            if (mat == null) return;
            var mesh = Look.Quad(new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1));
            var go = Look.FxObject("BloodPool", null, mesh, mat);
            if (go == null) return;
            pos.y = 0.015f + (pools.Count % 50) * 0.0003f;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
            var grow = go.AddComponent<PoolGrow>(); grow.target = radius * UnityEngine.Random.Range(0.8f, 1.2f);
            Keep(go);
        }

        // Пятно на стене
        static void Splat(Vector3 pos, Vector3 normal, float size)
        {
            var mat = Look.FxMat(new Color(0.45f, 0.02f, 0.04f, 0.85f), true, false);
            if (mat == null) return;
            var mesh = Look.Quad(new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0));
            var go = Look.FxObject("BloodSplat", null, mesh, mat);
            if (go == null) return;
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f));
            var grow = go.AddComponent<PoolGrow>(); grow.target = size; grow.speed = 6f;
            Keep(go);
        }

        // Не больше 64 пятен: старые плавно тают
        static void Keep(GameObject go)
        {
            pools.RemoveAll(p => p == null);
            pools.Add(go);
            if (pools.Count > MaxPools) { var old = pools[0]; pools.RemoveAt(0); var g = old.GetComponent<PoolGrow>(); if (g != null) g.Fade(); else UnityEngine.Object.Destroy(old); }
        }

        public static void Clear()
        {
            foreach (var p in pools) if (p != null) UnityEngine.Object.Destroy(p);
            pools.Clear();
        }
    }

    public class PoolGrow : MonoBehaviour
    {
        public float target = 0.9f, speed = 0.5f;
        float t, fade = -1f;
        public void Fade() { fade = 1f; }
        void Update()
        {
            if (fade >= 0f)
            {
                fade -= Time.deltaTime; transform.localScale = Vector3.one * Mathf.Max(0.01f, target * fade);
                if (fade <= 0f) Destroy(gameObject);
                return;
            }
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.deltaTime * speed);
            float k = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.one * Mathf.Max(0.05f, target * k);
        }
    }
}
