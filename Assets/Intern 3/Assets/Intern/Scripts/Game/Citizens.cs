// Горожане на обеде: 8 видов гуманитариев, 4 вида технарей и декан.
// Спокойно гуляют по графу улиц, пока не заметят оружие (12 м, вдвое дальше, пока журналист снимает) или не услышат выстрел.
// Дальше у каждого своё: юрист убегает и зовёт коллегу, нотариус кидает печать, философ спрашивает «А зачем?» и т. д.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public class CityNpc : MonoBehaviour
    {
        public enum St { Walk, Idle, Alert, Flee, Attack, Throw, Film, Preach, Listen, Follow, Dead }

        static readonly string[] Models = { "Dev1", "Dev2", "Dev3", "Dev4" };
        static readonly string[] PhilologistLines = { "ЗвонИт, а не звОнит!", "Не «ложить», а «класть»!", "ТОрты, а не тортЫ!", "КвартАл! КвартАл!", "Кофе — он, а не оно!" };
        static readonly string[] PoemLines = { "Я помню чудное мгновенье…", "Мороз и солнце, день чудесный!", "Идёт бычок, качается…", "Люблю грозу в начале мая…" };
        static readonly string[] Cries = { "Я же бухгалтер!", "Я инженер!", "Я кассир!", "Я из IT!" };

        public CitizenDef def;
        public int hp;
        public bool colleague;           // юрист, которого позвали на помощь
        public CityNpc leader;           // искусствоведы ходят группой
        public St State { get { return st; } }
        public bool Alive { get { return st != St.Dead; } }
        public bool Humanitarian { get { return def.humanitarian; } }
        public string Type { get { return def.id; } }
        public bool Calm { get { return st == St.Walk || st == St.Idle || st == St.Listen || st == St.Follow; } }

        LunchRun run;
        CityRefs city;
        CharacterAnim anim;
        readonly List<GameObject> props = new List<GameObject>();
        public CharacterAnim Anim { get { return anim; } }
        public bool Recyclable;          // можно вернуть тело в пул (обед заберёт в своём кадре)
        NameTag tag;          // имя над головой — рисует интерфейс (спринт 4 версии 0.9)
        Ragdoll rag;          // тело после смерти (спринт 5 версии 0.9)
        SpeechBubble bubble;
        St st = St.Walk;
        List<Vector3> path = new List<Vector3>();
        int pi;
        float speed, yaw, idleUntil, nextThink, nextAct, windupAt = -1f, bubbleUntil, curiousUntil, alertEnd, lane, calledAt = -1f, frozenUntil, staggerUntil;
        Vector3 listenAt, knockVel, fleeDoor, prevPos, velocity;
        public Vector3 DevRunVel;        // только для проверки в редакторе: бежит с этой скоростью, ни на что не реагируя
        public CityHall hall;            // живёт в доме (спринт 6 версии 0.9): сидит за столом или стоит за шкафом
        bool seated;
        List<Vector3> chasePath = new List<Vector3>(); int chasePi; float chaseRepath;
        float stuckCheckAt, noSlideUntil; Vector3 stuckRef;
        bool hitOnce, toArchive;
        int scatter;

        // ---------- появление ----------
        public static CityNpc Create(LunchRun run, CitizenDef def, Vector3 pos, System.Random rnd)
        {
            string model = Models[rnd.Next(Models.Length)];
            float yaw0 = (float)rnd.NextDouble() * 360f;
            var ap = Outfit(def.id, rnd);
            // тело берём из пула: собрать скелет модели заново — самое дорогое при появлении горожанина
            CharacterAnim a = run.City.TakePooled(model);
            if (a != null)
            {
                a.gameObject.SetActive(true);
                a.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw0, 0));
                if (a.imported) a.Tint(ap); else a.Build(ap);
                a.SetEmotion(0);
            }
            else if (ModelLib.HasCharacter(model)) { a = CharacterAnim.Spawn(model, run.City.root, pos, yaw0, null); a.SetMaxSmooth(1); a.Tint(ap); a.SetEmotion(0); }
            else a = Look.Bean(model, run.City.root, pos - run.City.root.position, yaw0, ap);
            a.transform.position = pos;
            a.transform.localScale = Vector3.one * (def.id == "dean" ? 1.12f : 1f);
            var n = a.gameObject.AddComponent<CityNpc>();
            n.run = run; n.city = run.City; n.anim = a; n.def = def; n.yaw = yaw0;
            n.hp = def.hp; n.speed = def.walk * (0.9f + (float)rnd.NextDouble() * 0.2f);
            n.lane = (float)rnd.NextDouble() * 2f - 1f;
            var cap = a.GetComponent<CapsuleCollider>();
            if (cap == null) cap = a.gameObject.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0, 0.95f, 0); cap.height = 1.9f; cap.radius = 0.32f; cap.enabled = true;
            // кинематическое тело: двигаем через transform, и физике не приходится пересобирать статичный коллайдер
            if (a.GetComponent<Rigidbody>() == null) { var rb = a.gameObject.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; }
            // всё, что добавит Props, — приметы этого горожанина; при возврате в пул их снимаем
            var before = new HashSet<Transform>(a.GetComponentsInChildren<Transform>(true));
            n.Props(rnd);
            foreach (var t in a.GetComponentsInChildren<Transform>(true)) if (!before.Contains(t) && before.Contains(t.parent)) n.props.Add(t.gameObject);
            string label = run.training ? (def.humanitarian ? def.name + " · +10" : def.name + " · не трогать") : def.name;
            var tagGo = new GameObject("NameTag"); tagGo.transform.SetParent(a.transform, false);
            n.tag = tagGo.AddComponent<NameTag>(); n.tag.Set(label, Pal.Hex(def.humanitarian ? def.color : "89D185"), 2.2f, def.humanitarian);
            n.nextThink = Time.time + (float)rnd.NextDouble() * 0.3f;
            n.prevPos = a.transform.position;
            n.NewWalk(rnd);
            return n;
        }

        static Appearance Outfit(string id, System.Random rnd)
        {
            var ap = new Appearance { skin = rnd.Next(6), eyes = rnd.Next(4), mouth = 2, emotion = 0, pants = 9, shoes = 9, tie = -1 };
            switch (id)
            {
                case "lawyer": ap.topColor = 8; ap.pants = 1; ap.tie = 2; ap.shoes = 8; break;
                case "notary": ap.topColor = 9; ap.pants = 8; ap.tie = 4; break;
                case "philologist": ap.topColor = 6; ap.pants = 1; break;
                case "journalist": ap.topColor = 5; ap.pants = 8; break;
                case "philosopher": ap.topColor = 4; ap.pants = 9; break;
                case "poet": ap.topColor = 10; ap.pants = 8; break;
                case "historian": ap.topColor = 9; ap.pants = 1; ap.tie = 3; break;
                case "critic": ap.topColor = 0; ap.pants = 8; break;
                case "accountant": ap.topColor = 7; ap.tie = 4; ap.pants = 1; break;
                case "engineer": ap.topColor = 3; ap.pants = 1; break;
                case "cashier": ap.topColor = 2; ap.pants = 8; break;
                case "itguy": ap.topColor = 1; ap.pants = 1; break;
                case "dean": ap.topColor = 1; ap.tie = 5; ap.pants = 8; ap.shoes = 8; break;
            }
            return ap;
        }

        // Приметы профессии: в руке, на голове, на груди
        void Props(System.Random rnd)
        {
            var hand = anim.elbowR != null ? anim.elbowR : transform;
            float top = 0f;   // макушка: верх самого высокого меша
            foreach (var r in GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y - transform.position.y);
            top = Mathf.Clamp(top, 1.6f, 2.2f);
            var headT = anim.head != null ? anim.head : transform;
            System.Func<string, Vector3, Vector3, Color, GameObject> onHead = (n, off, size, c) =>
            {
                var g = Look.RBox(n, transform, Vector3.zero, size, c, Mathf.Min(size.x, size.y) * 0.4f, false, 0.6f);
                g.transform.position = transform.position + Vector3.up * top + transform.rotation * off;
                g.transform.SetParent(headT, true); return g;
            };
            System.Func<string, Vector3, Color, GameObject> onChest = (n, size, c) =>
            {
                var g = Look.RBox(n, transform, Vector3.zero, size, c, 0.01f, false, 0.6f);
                g.transform.position = transform.position + Vector3.up * top * 0.72f + transform.forward * 0.19f;
                g.transform.rotation = transform.rotation;
                g.transform.SetParent(anim.torso != null ? anim.torso : transform, true); return g;
            };
            anim.gripRight = def.id == "lawyer" || def.id == "notary" || def.id == "philologist" || def.id == "journalist" || def.id == "philosopher"
                || def.id == "historian" || def.id == "accountant" || def.id == "itguy" || def.id == "dean";
            switch (def.id)
            {
                case "lawyer": Look.RBox("Briefcase", hand, new Vector3(0, -0.32f, 0.02f), new Vector3(0.1f, 0.3f, 0.42f), Pal.Hex("6B4226"), 0.03f, false, 0.6f); break;
                case "notary":
                {
                    var stamp = Look.Prim("Stamp", hand, PrimitiveType.Cylinder, new Vector3(0, -0.32f, 0.06f), new Vector3(0.1f, 0.05f, 0.1f), Pal.Hex("C8453A"), false, 0.6f);
                    var grip = Look.Prim("StampGrip", hand, PrimitiveType.Sphere, new Vector3(0, -0.26f, 0.06f), new Vector3(0.07f, 0.07f, 0.07f), Pal.Hex("2B2D42"), false, 0.6f);
                    grip.transform.SetParent(stamp.transform, true);   // падает одним предметом
                    break;
                }
                case "philologist": Look.RBox("Book", hand, new Vector3(0, -0.3f, 0.08f), new Vector3(0.05f, 0.22f, 0.16f), Pal.Hex("7A6FC4"), 0.01f, false, 0.6f); break;
                case "journalist": Look.RBox("Phone", hand, new Vector3(0, -0.3f, 0.06f), new Vector3(0.02f, 0.14f, 0.075f), Pal.Hex("2B2D42"), 0.01f, false, 0.6f, 0.4f); onChest("Press", new Vector3(0.12f, 0.08f, 0.01f), Pal.Hex("F4F1EA")); break;
                case "philosopher": Look.Prim("Scroll", hand, PrimitiveType.Cylinder, new Vector3(0, -0.3f, 0.05f), new Vector3(0.06f, 0.12f, 0.06f), Pal.Hex("F2E6C8"), false, 0.6f); break;
                case "poet": onHead("Beret", new Vector3(0.03f, -0.05f, 0), new Vector3(0.3f, 0.07f, 0.3f), Pal.Hex("2E2638")); onChest("Scarf", new Vector3(0.34f, 0.08f, 0.05f), Pal.Hex("E0567F")); break;
                case "historian": Look.RBox("Folder", hand, new Vector3(0, -0.3f, 0.06f), new Vector3(0.03f, 0.26f, 0.2f), Pal.Hex("D9A441"), 0.01f, false, 0.6f); break;
                case "critic": onChest("Scarf", new Vector3(0.36f, 0.09f, 0.05f), Pal.Hex(rnd.Next(2) == 0 ? "F2C14E" : "92A7D2")); onHead("Glasses", new Vector3(0, -0.2f, 0.14f), new Vector3(0.18f, 0.035f, 0.02f), Pal.Hex("2B2D42")); break;
                case "accountant": Look.RBox("Calculator", hand, new Vector3(0, -0.3f, 0.08f), new Vector3(0.14f, 0.2f, 0.03f), Pal.Hex("4A4A5E"), 0.02f, false, 0.6f); break;
                case "engineer": onHead("Helmet", new Vector3(0, -0.06f, 0), new Vector3(0.32f, 0.14f, 0.34f), Pal.Hex("FFD23F")); break;
                case "cashier": onChest("Badge", new Vector3(0.09f, 0.06f, 0.01f), Pal.Hex("F4F1EA")); break;
                case "itguy": Look.RBox("Laptop", hand, new Vector3(0, -0.3f, 0.08f), new Vector3(0.03f, 0.24f, 0.32f), Pal.Hex("B9BCD6"), 0.01f, false, 0.6f); break;
                case "dean": onHead("Cap", new Vector3(0, -0.02f, 0), new Vector3(0.36f, 0.04f, 0.36f), Pal.Hex("2B2D42")); Look.RBox("Folder", hand, new Vector3(0, -0.32f, 0.04f), new Vector3(0.05f, 0.32f, 0.26f), Pal.Hex("C8453A"), 0.02f, false, 0.6f); break;
            }
        }

        // ---------- прогулка ----------
        void NewWalk(System.Random rnd)
        {
            if (leader != null && leader.Alive && leader.Calm) { st = St.Follow; return; }
            // в своём доме — бродит между столами и шкафами
            if (hall != null && hall.nodes.Count > 0 && city.HallAt(transform.position) == hall)
            {
                var to = city.nodes[hall.nodes[rnd.Next(hall.nodes.Count)]];
                path = city.Path(transform.position, to, lane * 0.3f); pi = 0; st = St.Walk; return;
            }
            string street = null;
            var here = city.StreetAt(transform.position);
            if (rnd.NextDouble() < 0.65) street = here != null ? here.id : def.street;
            var target = city.RandomNode(rnd, street == "square" && def.humanitarian ? null : street);
            path = city.Path(transform.position, target + new Vector3((float)rnd.NextDouble() - 0.5f, 0, (float)rnd.NextDouble() - 0.5f) * 3f, lane);
            pi = 0; st = St.Walk;
        }

        void Update()
        {
            if (st == St.Dead || run == null) return;
            float dt = Time.deltaTime;
            if (DevRunVel.sqrMagnitude > 0.01f)
            {
                transform.position += DevRunVel * dt; anim.moveSpeed = DevRunVel.magnitude;
                transform.rotation = Quaternion.LookRotation(new Vector3(DevRunVel.x, 0, DevRunVel.z)); return;
            }
            if (run.Paused || Time.time < frozenUntil) { anim.moveSpeed = 0f; return; }
            var me = transform.position; me.y = 0;
            var pl = run.PlayerPos; pl.y = 0;
            float dist = Vector3.Distance(me, pl);

            // отброс битой
            if (knockVel.sqrMagnitude > 0.01f)
            {
                var kp = transform.position + knockVel * dt; knockVel = Vector3.MoveTowards(knockVel, Vector3.zero, 18f * dt);
                transform.position = city != null ? city.Slide(transform.position, kp, kp) : kp;
                anim.moveSpeed = 0f; return;
            }
            if (Time.time < staggerUntil) { anim.moveSpeed = 0f; return; }

            if (Time.time >= nextThink) { nextThink = Time.time + 0.3f; Think(dist); }
            if (bubble != null && Time.time > bubbleUntil) bubble.Hide();

            switch (st)
            {
                case St.Idle:
                    anim.moveSpeed = 0f;
                    if (Time.time >= idleUntil) NewWalk(run.Rnd);
                    break;
                case St.Walk:
                case St.Listen:
                    if (Follow(speed, dt)) { if (st == St.Listen) { st = St.Idle; idleUntil = Time.time + 2f; } else { st = St.Idle; idleUntil = Time.time + Random.Range(0.5f, 3f); } }
                    if (st == St.Listen && Time.time > curiousUntil) NewWalk(run.Rnd);
                    break;
                case St.Follow:
                    if (leader == null || !leader.Alive || !leader.Calm) { leader = null; NewWalk(run.Rnd); break; }
                    var off = leader.transform.rotation * new Vector3(scatter == 1 ? -0.9f : 0.9f, 0, -0.9f);
                    MoveTo(leader.transform.position + off, leader.speed * 1.1f, dt, 0.3f);
                    break;
                case St.Alert:
                    anim.moveSpeed = 0f; Face(pl, dt);
                    if (Time.time >= alertEnd) React();
                    break;
                case St.Flee:
                    // первый обед с обучением: убегают вдвое медленнее — новичок успевает догнать
                    if (Follow(run.training ? def.run * 0.5f : def.run, dt, def.id == "philologist" ? 1.3f : 0f)) Escape();
                    if (def.id == "philologist" && Time.time >= nextAct) { nextAct = Time.time + 2.5f; Say(PhilologistLines[run.Rnd.Next(PhilologistLines.Length)], 2f); }
                    break;
                case St.Attack: AttackTick(pl, dist, dt); break;
                case St.Throw: ThrowTick(pl, dist, dt); break;
                case St.Film: FilmTick(pl, dist, dt); break;
                case St.Preach: PreachTick(pl, dist, dt); break;
            }
            if (calledAt > 0f && Time.time >= calledAt) { calledAt = -1f; run.CallColleague(this); }
        }

        // Скорость тела за кадр — её унесёт с собой падение на бегу. Считаем после всех перемещений кадра
        // (Update двигает горожанина с тем же deltaTime), сглаживая рывки
        void LateUpdate()
        {
            if (st == St.Dead) return;
            float dt = Time.deltaTime;
            if (dt > 0f) velocity = Vector3.Lerp(velocity, (transform.position - prevPos) / dt, 0.5f);
            prevPos = transform.position;
        }

        // Заметил ли стажёра с оружием
        void Think(float dist)
        {
            if (!Calm) return;
            if (!def.humanitarian) return;   // технари не боятся ножа, только выстрелов
            float range = run.NoticeRange;
            if (dist > range) return;
            if (!SeesPlayer()) return;   // за стеной
            Alert();
        }

        // Видит ли стажёра: между глазами и им нет стены, шкафа, машины
        bool SeesPlayer()
        {
            var eye = transform.position + Vector3.up * (seated ? 1.25f : 1.6f); var tgt = run.PlayerPos + Vector3.up * 1.3f;
            RaycastHit h;
            int mask = Physics.DefaultRaycastLayers & ~(1 << 2);
            return !(Physics.Linecast(eye, tgt, out h, mask, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<CityNpc>() == null);
        }

        // Встать из-за стола (заметил, испугался, позвали)
        void StandUp()
        {
            if (!seated) return;
            seated = false; anim.sitTarget = 0f; anim.typing = false;
            staggerUntil = Mathf.Max(staggerUntil, Time.time + 0.4f);
        }

        // Посадить или поставить в доме: sit — в кресло за стол, иначе стоит (за шкафом, в углу)
        public void PlaceInHall(CityHall h, Vector3 at, float yawDeg, bool sit)
        {
            hall = h; at.y = 0f;
            transform.position = at; transform.rotation = Quaternion.Euler(0, yawDeg, 0); yaw = yawDeg;
            prevPos = at; velocity = Vector3.zero; path.Clear(); pi = 0;
            st = St.Idle; seated = sit;
            idleUntil = sit ? float.MaxValue : Time.time + Random.Range(8f, 30f);
            if (sit) { anim.SetSitInstant(1f); anim.typing = !def.humanitarian || Random.value < 0.4f; }
        }

        // Услышал выстрел
        public void Hear(Vector3 at)
        {
            if (!Alive || !Calm) return;
            if (!def.humanitarian) { Panic(); return; }
            Alert();
        }

        public void Alert()
        {
            StandUp();
            if (!Alive || !Calm) return;
            st = St.Alert; alertEnd = Time.time + 0.35f;
            anim.React(3, 1.2f);
        }

        // Что делает, когда понял, что к чему
        void React()
        {
            switch (def.id)
            {
                case "lawyer":
                    if (colleague) { st = St.Attack; break; }
                    Flee(); if (calledAt < 0f) calledAt = Time.time + 10f; Say("Алло, коллеги! Тут стажёр с ножом!", 2.5f); break;
                case "notary": st = St.Throw; nextAct = Time.time + 1f; Say("Заверено!", 1.5f); break;
                case "journalist": st = St.Film; Say("Снимаю! Это эксклюзив!", 2f); break;
                case "philosopher": st = St.Preach; nextAct = Time.time + 0.5f; break;
                case "poet": Say(PoemLines[run.Rnd.Next(PoemLines.Length)], 3f); run.PoemAt(transform.position, this); Flee(); break;
                case "historian": toArchive = true; Flee(); break;
                case "critic": Flee(); break;
                case "dean": st = St.Attack; Say("Отчислю!", 2f); break;
                default: Flee(); break;
            }
        }

        void Panic() { if (st != St.Flee) { StandUp(); anim.React(3, 2f); Flee(); } }

        // Стихи поэта: соседи идут послушать
        public void Listen(Vector3 at)
        {
            if (!Alive || !Calm || !def.humanitarian || def.id == "poet") return;
            StandUp(); listenAt = at; st = St.Listen; curiousUntil = Time.time + 6f;
            path = city.Path(transform.position, at + Random.insideUnitSphere * 2f, lane); pi = 0;
        }

        // Вышел из архива на шум
        public void Curious(Vector3 at)
        {
            st = St.Listen; curiousUntil = Time.time + 5f;
            path = city.Path(transform.position, at, lane); pi = 0;
        }

        void Flee()
        {
            StandUp(); st = St.Flee;
            fleeDoor = toArchive && city.archiveDoor != Vector3.zero ? city.archiveDoor : run.FleeDoor(transform.position, def.id == "critic" ? scatter : 0);
            path = city.Path(transform.position, fleeDoor, lane * 0.5f); pi = 0;
        }

        void Escape()
        {
            if (toArchive) { run.OnHidden(this); return; }
            run.OnEscaped(this);
        }

        // ---------- нападение ----------
        void AttackTick(Vector3 pl, float dist, float dt)
        {
            if (city.AreaOf(run.PlayerPos) == null) { anim.moveSpeed = 0f; Face(pl, dt); return; }   // в магазин не заходят
            if (windupAt > 0f)
            {
                anim.moveSpeed = 0f; Face(pl, dt);
                if (Time.time >= windupAt)
                {
                    windupAt = -1f; nextAct = Time.time + 1.2f;
                    if (dist < 1.9f) run.HurtPlayer(def.damage, transform.position, 0f, 0f);
                }
                return;
            }
            if (dist > 1.3f || city.AreaOf(transform.position) != city.AreaOf(run.PlayerPos)) { Chase(run.PlayerPos, def.run, dt, 1.2f); return; }
            anim.moveSpeed = 0f; Face(pl, dt);
            if (Time.time >= nextAct) { windupAt = Time.time + 0.45f; anim.swingStart = Time.time + 0.15f; }
        }

        // Нотариус: держит дистанцию и кидает печать
        void ThrowTick(Vector3 pl, float dist, float dt)
        {
            if (dist > 11f || !SameArea()) { Chase(run.PlayerPos, def.walk * 1.4f, dt, 9f); return; }
            if (dist < 4.5f) { MoveTo(transform.position + (transform.position - pl).normalized * 3f, def.run, dt, 0.2f); return; }
            anim.moveSpeed = 0f; Face(pl, dt);
            if (Time.time >= nextAct && SameArea() && SeesPlayer())
            {
                nextAct = Time.time + 2.5f; if (anim.v4) anim.Throw(); else anim.swingStart = Time.time;
                Stamp.Throw(run, transform.position + Vector3.up * 1.6f + transform.forward * 0.4f, run.PlayerPos + Vector3.up * 1f, def.damage);
            }
        }

        // Журналист: держит 8–13 м и снимает; подойдёшь ближе 5 м — убежит
        void FilmTick(Vector3 pl, float dist, float dt)
        {
            anim.aimGun = true; anim.aimPitch = 0f;
            if (dist < 5f) { anim.aimGun = false; Flee(); return; }
            if (dist > 13f || !SameArea()) { Chase(run.PlayerPos, def.walk * 1.5f, dt, 10f); return; }
            if (dist < 8f) { MoveTo(transform.position + (transform.position - pl).normalized * 3f, def.walk * 1.6f, dt, 0.2f); return; }
            anim.moveSpeed = 0f; Face(pl, dt);
        }

        // Философ: стоит, рассуждает, «А зачем?» замедляет
        void PreachTick(Vector3 pl, float dist, float dt)
        {
            anim.moveSpeed = 0f; Face(pl, dt);
            if (dist < 7f && Time.time >= nextAct)
            {
                nextAct = Time.time + 5f;
                Say("А зачем?", 2f);
                run.SlowPlayer(2f);
            }
        }

        bool SameArea() { var a = city.AreaOf(run.PlayerPos); return a != null && a == city.AreaOf(transform.position); }

        // Догнать точку: в том же пространстве (улица или этот дом) — напрямую, иначе по графу через дверь
        bool Chase(Vector3 target, float spd, float dt, float stopAt)
        {
            string a = city.AreaOf(transform.position), b = city.AreaOf(target);
            if (a != null && a == b) { chasePath.Clear(); return MoveTo(target, spd, dt, stopAt); }
            if (Time.time >= chaseRepath || chasePi >= chasePath.Count)
            {
                chasePath = city.Path(transform.position, target, 0f); chasePi = 0; chaseRepath = Time.time + 0.7f;
            }
            if (chasePi < chasePath.Count && MoveTo(chasePath[chasePi], spd, dt, 0.4f)) chasePi++;
            var d = target - transform.position; d.y = 0;
            return d.magnitude <= stopAt;
        }

        // ---------- движение ----------
        bool Follow(float spd, float dt, float zig = 0f)
        {
            if (pi >= path.Count) { anim.moveSpeed = 0f; return true; }
            var target = path[pi];
            if (zig > 0f)
            {
                var d = target - transform.position; d.y = 0;
                if (d.sqrMagnitude > 1f) target += new Vector3(d.z, 0, -d.x).normalized * Mathf.Sin(Time.time * 4f) * zig;
            }
            if (MoveTo(target, spd, dt, 0.45f)) pi++;
            return pi >= path.Count;
        }

        bool MoveTo(Vector3 target, float spd, float dt, float stopAt)
        {
            var me = transform.position; me.y = 0; target.y = 0;
            var to = target - me; float d = to.magnitude;
            if (d <= stopAt) { anim.moveSpeed = 0f; return true; }
            var dir = to / d;
            yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 540f * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            var np = me + dir * Mathf.Min(d, spd * dt);
            // застрял между мебелью (за секунду почти не сдвинулся) — полсекунды идёт, не обходя препятствия
            if (Time.time >= stuckCheckAt)
            {
                if (stuckCheckAt > 0f && (me - stuckRef).magnitude < 0.15f && spd > 0.5f) noSlideUntil = Time.time + 0.6f;
                stuckCheckAt = Time.time + 1f; stuckRef = me;
            }
            transform.position = city != null && Time.time >= noSlideUntil ? city.Slide(me, np, target) : np;   // у машины и мебели — вдоль борта
            anim.moveSpeed = spd;
            return false;
        }

        void Face(Vector3 p, float dt)
        {
            var to = p - transform.position; to.y = 0; if (to.sqrMagnitude < 0.01f) return;
            yaw = Mathf.MoveTowardsAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 400f * dt);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        void Say(string line, float seconds)
        {
            if (bubble == null) { bubble = SpeechBubble.Create(transform, 2.05f); bubble.Speaker = def.name; }
            bubble.Show(line, def.humanitarian ? Pal.Hex("FFF3C4") : Pal.Hex("D8F7E4")); bubbleUntil = Time.time + seconds;
        }

        public void SetLabel(bool on) { if (tag != null && tag.gameObject.activeSelf != on) tag.gameObject.SetActive(on); }
        public void SetScatter(int i) { scatter = i; }

        // ---------- попадание ----------
        public void Hit(int damage, Vector3 from, Vector3 point, Vector3 dir, float knock, float stun)
        {
            if (st == St.Dead) return;
            Gore.Hit(point, dir);
            if (!def.humanitarian)
            {
                if (!hitOnce) { hitOnce = true; run.OnWrongHit(this, Cries[Mathf.Clamp(System.Array.IndexOf(new[] { "accountant", "engineer", "cashier", "itguy" }, def.id), 0, 3)]); }
                anim.React(5, 2f); anim.Flinch(); Say(Cries[Mathf.Clamp(System.Array.IndexOf(new[] { "accountant", "engineer", "cashier", "itguy" }, def.id), 0, 3)], 2f);
                if (st != St.Flee) Flee();
                return;
            }
            hp -= damage;
            if (hp > 0) StandUp();
            anim.React(4, 1.5f);
            if (hp > 0) anim.Flinch();
            if (run.training) stun = Mathf.Max(stun, 0.7f);   // обучение: после удара чуть замирает
            if (knock > 0f) { var k = dir; k.y = 0; knockVel = k.normalized * knock * 6f; }
            if (stun > 0f) staggerUntil = Time.time + stun;
            if (hp <= 0) { Die(point, dir, damage, knock); return; }
            // раненый реагирует сразу: философ и декан дерутся, остальные по своей схеме
            if (def.id == "philosopher" && st == St.Preach) { st = St.Attack; return; }
            if (Calm || st == St.Alert) React();
        }

        void Die(Vector3 point, Vector3 dir, int damage, float knock)
        {
            st = St.Dead;
            anim.moveSpeed = 0f; anim.aimGun = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = false;
            if (tag != null) tag.gameObject.SetActive(false);
            if (bubble != null) bubble.Hide();
            run.OnKill(this);
            // толчок: чем сильнее оружие, тем дальше отбрасывает; бита — сильнее всего и чуть вверх
            var d = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward * -1f;
            d.y = Mathf.Max(d.y, knock > 1f ? 0.22f : 0.1f); d.Normalize();
            float power = Mathf.Clamp(18f + damage * 0.9f, 25f, 100f) + knock * 55f;
            StartCoroutine(Fall(point, d * power));
        }

        IEnumerator Fall(Vector3 point, Vector3 impulse)
        {
            rag = Ragdoll.Make(anim, velocity, point, impulse);
            if (rag != null)
            {
                DropProps(impulse);
                float t0 = Time.time;
                while (rag != null && !rag.Frozen && Time.time - t0 < 3f) yield return null;
                if (rag != null) { var h = rag.HipsPosition; Gore.Pool(new Vector3(h.x, transform.position.y + 0.02f, h.z)); }
                yield return new WaitForSeconds(18f);
                if (run != null) Recyclable = true; else Destroy(gameObject);
                yield break;
            }
            // модель без скелета — падает целиком, как раньше
            var start = transform.rotation;
            var end = start * Quaternion.Euler(-88f, 0, 0);   // падает на спину
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.4f)
            {
                transform.rotation = Quaternion.Slerp(start, end, t * t);
                yield return null;
            }
            transform.rotation = end;
            anim.enabled = false;
            Gore.Pool(transform.position + transform.up * -0.4f + Vector3.up * 0.02f);
            yield return new WaitForSeconds(20f);
            if (run != null) Recyclable = true; else Destroy(gameObject);
        }

        // Снять всё своё перед возвратом тела в пул: приметы, подпись, облачко, позу
        public void Strip()
        {
            StopAllCoroutines();
            if (rag != null) { rag.Restore(); DestroyImmediate(rag); rag = null; }
            hall = null; seated = false; anim.sitTarget = 0f; anim.SetSitInstant(0f); anim.typing = false;
            foreach (var p in props) if (p != null) Destroy(p);
            props.Clear();
            if (tag != null) Destroy(tag.gameObject);
            if (bubble != null) Destroy(bubble.gameObject);
            anim.enabled = true; anim.moveSpeed = 0f; anim.aimGun = false; anim.holdRight = false; anim.swingStart = -9f; anim.gripRight = false;
            foreach (var c in GetComponents<Collider>()) c.enabled = true;
            transform.localScale = Vector3.one;
            run = null;
        }

        // Предметы из рук (портфель, папка, ноутбук, печать) и шапки при падении летят отдельно
        void DropProps(Vector3 impulse)
        {
            var hand = anim.elbowR;
            var bodyCols = GetComponentsInChildren<Collider>();
            foreach (var p in props)
            {
                if (p == null) continue;
                bool inHand = hand != null && p.transform.IsChildOf(hand);
                bool hat = p.name == "Beret" || p.name == "Helmet" || p.name == "Cap";
                if (!inHand && !(hat && Random.value < 0.7f)) continue;
                p.transform.SetParent(city.root, true);
                Collider col;
                if (p.GetComponent<MeshFilter>() != null) col = p.AddComponent<BoxCollider>(); else { var sc = p.AddComponent<SphereCollider>(); sc.radius = 0.08f; col = sc; }
                foreach (var bc in bodyCols) if (bc != null && bc.enabled) Physics.IgnoreCollision(col, bc, true);   // не расталкивать руку, из которой выпал
                var rb = p.AddComponent<Rigidbody>();
                rb.mass = inHand ? 1.5f : 0.4f; rb.angularDamping = 0.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var push = impulse.sqrMagnitude > 0.0001f ? impulse.normalized : Vector3.zero;
                rb.linearVelocity = velocity * 0.8f + push * (inHand ? 1.2f : 2.2f) + Vector3.up * (inHand ? 1.0f : 1.8f) + Random.insideUnitSphere * 0.5f;
                rb.angularVelocity = Random.insideUnitSphere * 7f;
            }
        }

        // Выстрел по лежащему телу — толкнуть его
        public void Shove(Vector3 point, Vector3 impulse) { if (rag != null) rag.Shove(point, impulse); }

        // Только для проверки в редакторе: смертельное попадание по направлению dir (knock — как у биты)
        public void DebugKill(Vector3 dir, float knock)
        {
            if (!Alive) return;
            Hit(hp + 1, transform.position - dir * 5f, transform.position + Vector3.up * 1.25f, dir, knock, 0f);
        }

        // Только для проверки в редакторе: снять заморозку DebugPlace и что сейчас на пути
        public void DevUnfreeze() { frozenUntil = 0f; idleUntil = 0f; }
        public string DevPathInfo { get { return "шаг " + pi + "/" + path.Count + (pi < path.Count ? " к " + path[pi].ToString("0.0") : "") + " из " + transform.position.ToString("0.0"); } }

        // Только для проверки в редакторе: поставить перед игроком и заморозить
        public void DebugPlace(Vector3 at, float yaw)
        {
            at.y = 0f; transform.position = at; transform.rotation = Quaternion.Euler(0, yaw, 0); this.yaw = yaw; prevPos = at; velocity = Vector3.zero;
            path.Clear(); st = St.Idle; idleUntil = Time.time + 15f; frozenUntil = Time.time + 15f;
        }
    }

    // Печать нотариуса: летит дугой, попала — урон и оглушение на 1 с
    public class Stamp : MonoBehaviour
    {
        LunchRun run; Vector3 a, b; float t, dur; int damage;
        public static void Throw(LunchRun run, Vector3 from, Vector3 to, int damage)
        {
            var go = Look.Prim("StampThrown", run.City.root, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.14f, 0.06f, 0.14f), Pal.Hex("C8453A"), false, 0.6f);
            go.transform.position = from;
            var s = go.AddComponent<Stamp>(); s.run = run; s.a = from; s.b = to; s.damage = damage;
            s.dur = Mathf.Clamp(Vector3.Distance(from, to) / 12f, 0.3f, 1.2f);
            run.Track(go);
        }
        void Update()
        {
            if (run == null || run.Paused) return;
            t += Time.deltaTime / dur;
            var p = Vector3.Lerp(a, b, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 1.2f;
            transform.position = p; transform.Rotate(720f * Time.deltaTime, 0, 0);
            if (t >= 1f)
            {
                var pl = run.PlayerPos + Vector3.up * 1f;
                if (Vector3.Distance(pl, b) < 1.3f) run.HurtPlayer(damage, a, 1f, 0f);
                Destroy(gameObject);
            }
        }
    }
}
