// Хват оружия (спринт 8 версии 0.9): позы рук с каждым стволом собраны и проверены рендером в Blender
// (Art/grips_v1.py → Resources/Anim/grips_v1.txt). На каждое оружие: где оно стоит относительно груди (кость Torso),
// где запястья и как повёрнуты кисти относительно оружия, где локти, как согнуты пальцы. Поза тела — клип hold_<оружие>.
// Холодное оружие (Art/melee_v1.py): как оружие сидит в правой кисти, где левая кисть на рукояти, длина ударов серии
// и момент попадания; стойка — клип mguard_<оружие>, удары — m_<оружие>_<n>.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Intern.Game
{
    public static class GripLib
    {
        public class Grip
        {
            public string id; public float twist;
            public Vector3 chestPos; public Quaternion chestRot = Quaternion.identity;
            public readonly Vector3[] handPos = new Vector3[2], elbow = new Vector3[2];
            public readonly Quaternion[] handRot = { Quaternion.identity, Quaternion.identity };
            public readonly Quaternion[][] fingers = new Quaternion[2][];   // 0 — правая, 1 — левая; по 15: указательный, средний, безымянный, мизинец, большой
            // холодное: оружие в осях правой кисти, вторая рука (handPos/handRot[1]), удары
            public bool melee, two; public string guard; public Vector3 attachPos; public Quaternion attachRot = Quaternion.identity;
            public readonly List<float> strikeLen = new List<float>(), strikeHit = new List<float>();
        }

        static Dictionary<string, Grip> all;
        public static string Error;
        public static int Count { get { Load(); return all.Count; } }

        public static Grip Get(string id)
        {
            Load();
            Grip g; return id != null && all.TryGetValue(id, out g) ? g : null;
        }

        static void Load()
        {
            if (all != null) return;
            all = new Dictionary<string, Grip>();
            var ta = Resources.Load<TextAsset>("Anim/grips_v1");
            if (ta == null) { Error = "нет Resources/Anim/grips_v1"; return; }
            var ci = CultureInfo.InvariantCulture;
            Grip cur = null;
            try
            {
                foreach (var raw in ta.text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    var p = line.Split(' ');
                    System.Func<int, float> F = i => float.Parse(p[i], ci);
                    switch (p[0])
                    {
                        case "weapon": cur = new Grip { id = p[1], twist = p.Length > 3 ? F(3) : 0f }; all[cur.id] = cur; break;
                        case "melee": cur = new Grip { id = p[1], melee = true, guard = "mguard_" + p[1] }; all[cur.id] = cur; break;
                        case "attach": cur.attachPos = new Vector3(F(1), F(2), F(3)); cur.attachRot = new Quaternion(F(4), F(5), F(6), F(7)); break;
                        case "strike": cur.strikeLen.Add(F(2)); cur.strikeHit.Add(F(3)); break;
                        case "chest": cur.chestPos = new Vector3(F(1), F(2), F(3)); cur.chestRot = new Quaternion(F(4), F(5), F(6), F(7)); break;
                        case "hand": { int s = p[1] == "R" ? 0 : 1; if (s == 1 && cur.melee) cur.two = true; cur.handPos[s] = new Vector3(F(2), F(3), F(4)); cur.handRot[s] = new Quaternion(F(5), F(6), F(7), F(8)); break; }
                        case "elbow": { int s = p[1] == "R" ? 0 : 1; cur.elbow[s] = new Vector3(F(2), F(3), F(4)); break; }
                        case "fingers":
                        {
                            int s = p[1] == "R" ? 0 : 1; var q = new Quaternion[15];
                            for (int k = 0; k < 15; k++) q[k] = new Quaternion(F(2 + k * 4), F(3 + k * 4), F(4 + k * 4), F(5 + k * 4));
                            cur.fingers[s] = q; break;
                        }
                    }
                }
            }
            catch (System.Exception e) { Error = "grips_v1: " + e.Message; }
        }

        // Пальцы из позы; squeeze — дожать указательный (градусы, на выстреле)
        public static void SetFingers(Transform[] f, Quaternion[] q, bool right, float squeeze = 0f, float w = 1f)
        {
            if (f == null || q == null) return;
            Vector3 ax = right ? Vector3.back : Vector3.forward;
            for (int k = 0; k < 15 && k < f.Length && k < q.Length; k++)
            {
                if (f[k] == null) continue;
                var r = q[k];
                if (k < 3 && squeeze != 0f) r = Quaternion.AngleAxis(squeeze * (k == 1 ? 1f : 0.7f), ax) * r;
                f[k].localRotation = w >= 0.999f ? r : Quaternion.Slerp(f[k].localRotation, r, w);
            }
        }
    }
}
