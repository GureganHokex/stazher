// Спринт 5 версии 0.9 «Живые падения»: после смерти горожанин становится ragdoll — физическое тело на костях модели.
//  • 11 частей: таз, грудь, голова, бёдра, голени, плечи, предплечья (на «Низком» — 6: колени и локти не гнутся,
//    голова заодно с грудью);
//  • суставы с пределами: колени и локти гнутся только в одну сторону, шея и поясница — немного;
//  • в момент попадания тело получает толчок по направлению выстрела или удара и сохраняет скорость бега;
//  • части сталкиваются с землёй, стенами, скамейками, машинами и другими телами;
//  • через пару секунд тело замирает: части становятся неподвижными, а сетки «запекаются» — больше не пересчитываются
//    каждый кадр. Одновременно двигаются не больше MaxMoving тел, самые старые замирают раньше.
// Суставы собираются в позе покоя (все кости без поворота), потом тело возвращается в позу момента смерти —
// так пределы суставов считаются от естественного положения, а не от шага или замаха.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public class Ragdoll : MonoBehaviour
    {
        public static bool Enabled = true;                                      // выключается только для снимков «до»
        public static int MaxMoving { get { return GameConfig.S != null && GameConfig.S.quality <= 0 ? 4 : 8; } }
        static readonly List<Ragdoll> moving = new List<Ragdoll>();
        public static int Moving { get { return moving.Count; } }

        // Знак поворота: предел «скрутки» сустава в PhysX противоположен углу Эйлера в Unity (проверено ragtest)
        static readonly float TwistSign = -1f;

        class Part { public string name; public Transform bone; public Rigidbody rb; }
        readonly List<Part> parts = new List<Part>();
        readonly List<Joint> joints = new List<Joint>();
        readonly List<GameObject> colliderObjects = new List<GameObject>();
        readonly List<Collider> colliders = new List<Collider>();
        readonly List<KeyValuePair<Transform, Pose>> saved = new List<KeyValuePair<Transform, Pose>>();
        readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
        readonly List<Bounds> skinBounds = new List<Bounds>();
        readonly List<GameObject> baked = new List<GameObject>();
        CharacterAnim anim;
        GameObject blob;
        Rigidbody chest;
        float bornAt, calmFor;
        public bool Frozen { get; private set; }
        public bool Simple { get; private set; }
        public int IgnoredPairs { get; private set; }
        public static bool DevTrace;                                            // ragtest: записывать скорости первых кадров
        readonly System.Text.StringBuilder trace = new System.Text.StringBuilder();
        int traceFrames;
        public Vector3 HipsPosition { get { return anim != null && anim.hips != null ? anim.hips.position : transform.position; } }

        static PhysicsMaterial bodyMat;
        static PhysicsMaterial BodyMat
        {
            get
            {
                // трение как у одежды об асфальт: тело не скользит по площади, как по льду
                if (bodyMat == null) bodyMat = new PhysicsMaterial("Body") { dynamicFriction = 0.9f, staticFriction = 1f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum };
                return bodyMat;
            }
        }

        // Тело после смерти: vel — скорость на бегу, impulse — толчок попадания в точке point. null — модель без скелета
        public static Ragdoll Make(CharacterAnim a, Vector3 vel, Vector3 point, Vector3 impulse)
        {
            if (!Enabled || a == null || !a.imported || a.rig == null || a.hips == null || a.torso == null || a.head == null
                || a.legL == null || a.legR == null || a.kneeL == null || a.kneeR == null || a.armL == null || a.armR == null || a.elbowL == null || a.elbowR == null) return null;
            var rd = a.GetComponent<Ragdoll>();
            if (rd == null) rd = a.gameObject.AddComponent<Ragdoll>();
            rd.Build(a, GameConfig.S != null && GameConfig.S.quality <= 0);
            rd.Launch(vel, point, impulse);
            return rd;
        }

        static Transform Child(Transform t, string prefix)
        {
            if (t == null) return null;
            foreach (Transform c in t) if (c.name.StartsWith(prefix) && !c.name.EndsWith("_bind")) return c;
            return null;
        }

        void Build(CharacterAnim a, bool simple)
        {
            anim = a; Simple = simple; Frozen = false; calmFor = 0f;
            a.enabled = false;
            var bones = new List<Transform> { a.rig, a.hips, a.torso, a.head, a.legL, a.kneeL, a.legR, a.kneeR, a.armL, a.elbowL, a.armR, a.elbowR };
            if (a.neck != null) bones.Add(a.neck);
            saved.Clear();
            foreach (var b in bones) saved.Add(new KeyValuePair<Transform, Pose>(b, new Pose(b.localPosition, b.localRotation)));
            // поза покоя: у нормализованного скелета все кости без поворота относительно персонажа
            foreach (var b in bones) if (b != a.rig) b.localRotation = Quaternion.identity;

            var pelvis = Body("pelvis", a.hips, 12f);
            Box(a.hips, new Vector3(0, -0.01f, 0), new Vector3(0.34f, 0.22f, 0.24f));
            chest = Body("chest", a.torso, 18f);
            Box(a.torso, new Vector3(0, 0.27f, 0), new Vector3(0.38f, 0.44f, 0.25f));
            // поясница: вперёд до 50°, назад 20°, вбок и скрутка по 15° (пределы шире походки — иначе тело дёргает в первый кадр)
            Link(chest, pelvis, Vector3.right, Vector3.forward, -20f, 50f, 15f, 15f);
            Sphere(a.head, new Vector3(0, 0.2f, 0.03f), 0.2f);
            if (!simple)
            {
                var head = Body("head", a.head, 5f);
                Link(head, chest, Vector3.right, Vector3.forward, -30f, 40f, 25f, 25f);   // шея: кивок вперёд 40°, назад 30°
            }
            foreach (int s in new[] { -1, 1 })
            {
                var hip = s < 0 ? a.legL : a.legR; var knee = s < 0 ? a.kneeL : a.kneeR;
                var foot = Child(knee, "Foot");
                var thigh = Body(s < 0 ? "thighL" : "thighR", hip, 7.5f);
                Capsule(hip, knee.localPosition, 0.085f, 0f);
                // бедро: вперёд до 100°, назад 50° (на бегу нога уходит назад на 46°), в сторону 30°
                Link(thigh, pelvis, Vector3.right, Vector3.forward, -100f, 50f, 30f, 12f);
                Rigidbody shin = thigh;
                if (!simple)
                {
                    shin = Body(s < 0 ? "shinL" : "shinR", knee, 3.5f);
                    Link(shin, thigh, Vector3.right, Vector3.forward, 0f, 135f, 3f, 3f);   // колено: только назад, до 135°
                }
                if (foot != null)
                {
                    Capsule(knee, foot.localPosition, 0.07f, 0f);
                    Box(foot, new Vector3(0, 0.01f, 0.05f), new Vector3(0.11f, 0.09f, 0.26f));   // ступня с ботинком
                }
                else Capsule(knee, new Vector3(0, -0.42f, 0), 0.07f, 0f);

                var arm = s < 0 ? a.armL : a.armR; var elbow = s < 0 ? a.elbowL : a.elbowR;
                var hand = Child(elbow, "Hand");
                var upper = Body(s < 0 ? "armL" : "armR", arm, 2.5f);
                Capsule(arm, elbow.localPosition, 0.06f, 0f);
                // плечо: вперёд до 150°, назад 50°, в сторону до 80°
                Link(upper, chest, Vector3.right, Vector3.forward, -150f, 50f, 80f, 25f);
                if (!simple)
                {
                    var fore = Body(s < 0 ? "foreL" : "foreR", elbow, 2f);
                    Link(fore, upper, Vector3.right, Vector3.forward, -140f, 0f, 3f, 3f);   // локоть: только вперёд, до 140°
                }
                Capsule(elbow, hand != null ? hand.localPosition : new Vector3(0, -0.25f, 0), 0.055f, 0.1f);
            }
            // вернуть позу момента смерти
            foreach (var kv in saved) { kv.Key.localPosition = kv.Value.position; kv.Key.localRotation = kv.Value.rotation; }
            Physics.SyncTransforms();
        }

        Rigidbody Body(string name, Transform bone, float mass)
        {
            var rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.linearDamping = 0.1f; rb.angularDamping = 1.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.solverIterations = 12; rb.solverVelocityIterations = 4;
            rb.maxDepenetrationVelocity = 2f; rb.maxAngularVelocity = 25f;
            parts.Add(new Part { name = name, bone = bone, rb = rb });
            return rb;
        }

        // Сустав: axis — ось сгиба (в осях кости в покое), swing — ось отведения; min..max — угол Эйлера вокруг axis
        // в Unity (положительный у колена — голень назад, отрицательный у локтя — предплечье вперёд)
        void Link(Rigidbody child, Rigidbody parent, Vector3 axis, Vector3 swing, float min, float max, float swing1, float swing2)
        {
            var j = child.gameObject.AddComponent<CharacterJoint>();
            j.axis = axis; j.swingAxis = swing;
            float lo = TwistSign < 0f ? -max : min, hi = TwistSign < 0f ? -min : max;
            j.lowTwistLimit = new SoftJointLimit { limit = Mathf.Clamp(lo, -177f, 177f) };
            j.highTwistLimit = new SoftJointLimit { limit = Mathf.Clamp(hi, -177f, 177f) };
            j.swing1Limit = new SoftJointLimit { limit = swing1 };
            j.swing2Limit = new SoftJointLimit { limit = swing2 };
            j.enableProjection = true; j.projectionDistance = 0.04f; j.projectionAngle = 12f;
            j.enablePreprocessing = false; j.enableCollision = false;
            j.connectedBody = parent;
            joints.Add(j);
        }

        GameObject ColObject(Transform bone, Vector3 center, Quaternion rot)
        {
            var go = new GameObject("RagCol");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = center; go.transform.localRotation = rot;
            colliderObjects.Add(go);
            return go;
        }
        void Box(Transform bone, Vector3 center, Vector3 size)
        {
            var c = ColObject(bone, center, Quaternion.identity).AddComponent<BoxCollider>(); c.size = size; c.sharedMaterial = BodyMat; colliders.Add(c);
        }
        void Sphere(Transform bone, Vector3 center, float r)
        {
            var c = ColObject(bone, center, Quaternion.identity).AddComponent<SphereCollider>(); c.radius = r; c.sharedMaterial = BodyMat; colliders.Add(c);
        }
        // Капсула от кости до точки end (в осях кости), extra — продлить за конец (кисть)
        void Capsule(Transform bone, Vector3 end, float r, float extra)
        {
            float len = end.magnitude; if (len < 0.01f) return;
            var dir = end / len;
            var c = ColObject(bone, dir * (len + extra) * 0.5f, Quaternion.FromToRotation(Vector3.up, dir)).AddComponent<CapsuleCollider>();
            c.direction = 1; c.radius = r; c.height = len + extra + r; c.sharedMaterial = BodyMat; colliders.Add(c);
        }

        void Launch(Vector3 vel, Vector3 point, Vector3 impulse)
        {
            vel.y = Mathf.Max(vel.y, 0f);
            foreach (var p in parts) { p.rb.linearVelocity = vel; p.rb.angularVelocity = Vector3.zero; }
            // толчок: 70% в часть тела, ближайшую к попаданию, остальное в грудь — тело откидывает целиком
            if (impulse.sqrMagnitude > 0.0001f)
            {
                Part best = null; float bd = float.MaxValue;
                foreach (var p in parts) { float d = (p.rb.worldCenterOfMass - point).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
                if (best != null) best.rb.AddForceAtPosition(impulse * 0.7f, point, ForceMode.Impulse);
                if (chest != null) chest.AddForce(impulse * 0.3f, ForceMode.Impulse);
            }
            // части, которые в позе смерти уже пересекаются (рука у груди при прицеливании), друг друга не толкают —
            // иначе тело «взрывается»; остальные сталкиваются как обычно
            IgnoredPairs = 0;
            for (int i = 0; i < colliders.Count; i++)
                for (int k = i + 1; k < colliders.Count; k++)
                {
                    var c1 = colliders[i]; var c2 = colliders[k];
                    if (c1.attachedRigidbody == c2.attachedRigidbody) continue;
                    Vector3 d; float dist;
                    if (Physics.ComputePenetration(c1, c1.transform.position, c1.transform.rotation, c2, c2.transform.position, c2.transform.rotation, out d, out dist))
                    { Physics.IgnoreCollision(c1, c2, true); IgnoredPairs++; }
                }
            skins.Clear(); skinBounds.Clear();
            foreach (var s in anim.rig.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                skins.Add(s); skinBounds.Add(s.localBounds);
                s.updateWhenOffscreen = true;   // тело уходит от точки смерти — границы считаем по костям
            }
            var b = anim.transform.Find("BlobShadow"); blob = b != null ? b.gameObject : null;
            if (blob != null) blob.SetActive(false);
            bornAt = Time.time; trace.Length = 0; traceFrames = 0;
            trace.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " старт {0:0.0} {1:0.0} {2:0.0}, толчок {3:0}", vel.x, vel.y, vel.z, impulse.magnitude));
            moving.Add(this);
            while (moving.Count > MaxMoving) moving[0].Freeze();
        }

        void FixedUpdate()
        {
            if (!DevTrace || Frozen || parts.Count == 0 || traceFrames >= 14) return;
            traceFrames++;
            float maxV = 0f; string who = "";
            foreach (var p in parts) { float v = p.rb.linearVelocity.magnitude; if (v > maxV) { maxV = v; who = p.name; } }
            var pv = parts[0].rb.linearVelocity;
            trace.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, " [{0:0.00}: {1} {2:0.0}, таз {3:0.0} {4:0.0} {5:0.0}]", Time.time - bornAt, who, maxV, pv.x, pv.y, pv.z));
        }

        void Update()
        {
            if (Frozen || parts.Count == 0) return;
            float maxV = 0f, maxW = 0f;
            foreach (var p in parts) { maxV = Mathf.Max(maxV, p.rb.linearVelocity.magnitude); maxW = Mathf.Max(maxW, p.rb.angularVelocity.magnitude); }
            calmFor = maxV < 0.15f && maxW < 1.2f ? calmFor + Time.deltaTime : 0f;
            if ((calmFor > 0.5f && Time.time - bornAt > 1.2f) || Time.time - bornAt > 6f) Freeze();
        }

        // Замереть: части неподвижны, сетки запекаются в обычные — без пересчёта костей каждый кадр
        public void Freeze()
        {
            if (Frozen) return;
            Frozen = true; moving.Remove(this);
            foreach (var p in parts) { p.rb.isKinematic = true; p.rb.interpolation = RigidbodyInterpolation.None; }
            foreach (var s in skins)
            {
                if (s == null || !s.enabled || s.sharedMesh == null) continue;
                var m = new Mesh { name = s.sharedMesh.name + "_pose" };
                s.BakeMesh(m, false);
                var go = new GameObject(s.name + "_baked");
                go.transform.SetParent(s.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = s.sharedMaterials; r.shadowCastingMode = s.shadowCastingMode; r.receiveShadows = s.receiveShadows;
                s.enabled = false;
                baked.Add(go);
            }
        }

        void Unbake()
        {
            foreach (var go in baked)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
                DestroyImmediate(go);
            }
            baked.Clear();
            foreach (var s in skins) if (s != null) s.enabled = true;
        }

        // Выстрел или удар по лежащему телу: толкнуть (если не слишком много тел уже в движении)
        public void Shove(Vector3 point, Vector3 impulse)
        {
            if (parts.Count == 0) return;
            if (Frozen)
            {
                if (moving.Count >= MaxMoving) return;
                Unbake();
                foreach (var p in parts) { p.rb.isKinematic = false; p.rb.interpolation = RigidbodyInterpolation.Interpolate; }
                Frozen = false; calmFor = 0f; bornAt = Time.time; moving.Add(this);
            }
            Part best = null; float bd = float.MaxValue;
            foreach (var p in parts) { float d = (p.rb.worldCenterOfMass - point).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            if (best != null) best.rb.AddForceAtPosition(impulse, point, ForceMode.Impulse);
        }

        // Тело возвращается в пул: снять физику, вернуть кости и сетки как было
        public void Restore()
        {
            moving.Remove(this);
            Unbake();
            foreach (var j in joints) if (j != null) DestroyImmediate(j);
            joints.Clear();
            foreach (var go in colliderObjects) if (go != null) DestroyImmediate(go);
            colliderObjects.Clear(); colliders.Clear();
            foreach (var p in parts) if (p.rb != null) DestroyImmediate(p.rb);
            parts.Clear();
            foreach (var kv in saved) if (kv.Key != null) { kv.Key.localPosition = kv.Value.position; kv.Key.localRotation = kv.Value.rotation; }
            saved.Clear();
            for (int i = 0; i < skins.Count; i++) if (skins[i] != null) { skins[i].updateWhenOffscreen = false; skins[i].localBounds = skinBounds[i]; }
            skins.Clear(); skinBounds.Clear();
            if (blob != null) blob.SetActive(true);
            Frozen = false; chest = null;
            if (anim != null) anim.enabled = true;
        }

        void OnDestroy() { moving.Remove(this); }

        // Для ragtest: углы колен и локтей (Эйлер в Unity), сколько пар частей не сталкиваются, в движении ли
        public static string DevReport(CharacterAnim a)
        {
            var rd = a != null ? a.GetComponent<Ragdoll>() : null;
            if (rd == null || rd.parts.Count == 0) return null;
            // поворот кости = скрутка вокруг оси X (сгиб) и отклонение от неё: «сгиб~отклонение» в градусах
            System.Func<Transform, string> ang = t =>
            {
                if (t == null) return "-";
                var q = t.localRotation; if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
                float twist = 2f * Mathf.Atan2(q.x, q.w) * Mathf.Rad2Deg;
                var tq = new Quaternion(q.x, 0f, 0f, q.w); float n = Mathf.Sqrt(tq.x * tq.x + tq.w * tq.w);
                if (n > 1e-5f) { tq.x /= n; tq.w /= n; } else tq = Quaternion.identity;
                float swing = Quaternion.Angle(Quaternion.identity, t.localRotation * Quaternion.Inverse(tq));
                return Mathf.RoundToInt(Mathf.DeltaAngle(0f, twist)) + "~" + Mathf.RoundToInt(swing);
            };
            float low = float.MaxValue; foreach (var c in rd.colliders) if (c != null) low = Mathf.Min(low, c.bounds.min.y);
            return "низ тела " + low.ToString("0.00") + ", колени " + ang(a.kneeL) + " " + ang(a.kneeR) + ", локти " + ang(a.elbowL) + " " + ang(a.elbowR) + ", бёдра " + ang(a.legL) + " " + ang(a.legR) +
                   ", плечи " + ang(a.armL) + " " + ang(a.armR) + ", шея " + ang(a.head) +
                   ", поясница " + ang(a.torso) + ", частей " + rd.parts.Count + (rd.Simple ? " (упрощ.)" : "") + ", пересечений в начале " + rd.IgnoredPairs +
                   (rd.Frozen ? ", замерло" : ", движется") + (rd.trace.Length > 0 ? "\n    скорости:" + rd.trace : "");
        }
    }
}
