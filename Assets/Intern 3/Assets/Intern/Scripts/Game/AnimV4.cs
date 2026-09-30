// Спринт 7 версии 0.9 «Живые персонажи»: анимации v4 по клипам из Blender (Art/anim_v4.py → Resources/Anim/clips_v4).
//  • скелет как у человека: три позвонка, ключицы, пальцы, носок стопы;
//  • походка смешивается по скорости (шаг 1.4 → бег 3.8 → спринт 6.5 м/с) и направлению (вперёд, назад, вбок) в одной фазе шага;
//  • ступни стоят на земле: в опоре нога «прилипает» к месту касания (IK), подстраивается под высоту земли;
//    на месте, если корпус повернулся или уехал, ноги переступают;
//  • действия (печать, прицел, взмах, удар, бросок, вздрагивание) накладываются на верх тела, ноги продолжают своё;
//  • наклон в повороты и при разгоне, взгляд головой, дыхание.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Intern.Game
{
    public class AnimClip
    {
        public string name;
        public bool loop;
        public float len, speed, dir;
        public int frames;
        public Vector3[] root;
        public Quaternion[][] rot;          // [кость][кадр]; null — кость не задана (покой), длина 1 — постоянная
        public float[] contactL, contactR;
        public bool[] mask;                 // для действий: какими костями управляет (null — всеми)

        // кадры и доля между ними для времени t (секунды)
        public void Frame(float t, out int i0, out int i1, out float f)
        {
            if (frames <= 1 || len <= 0f) { i0 = i1 = 0; f = 0f; return; }
            if (loop)
            {
                float x = Mathf.Repeat(t / len, 1f) * frames;
                i0 = Mathf.Min((int)x, frames - 1); i1 = (i0 + 1) % frames; f = x - i0;
            }
            else
            {
                float x = Mathf.Clamp01(t / len) * (frames - 1);
                i0 = Mathf.Min((int)x, frames - 1); i1 = Mathf.Min(i0 + 1, frames - 1); f = x - i0;
            }
        }

        public float Contact(bool left, float t)
        {
            var c = left ? contactL : contactR;
            if (c == null) return 1f;
            int i0, i1; float f; Frame(t, out i0, out i1, out f);
            return Mathf.Lerp(c[i0], c[i1], f);
        }
    }

    public static class AnimLib
    {
        public static string[] Bones = new string[0];
        public static readonly Dictionary<string, int> BoneIndex = new Dictionary<string, int>();
        static Dictionary<string, AnimClip> clips;
        static bool tried;
        public static string Error;

        public static bool Ready { get { Load(); return clips != null && clips.Count > 0; } }

        // Play без перезагрузки домена: статика живёт между запусками — клипы читаем заново (файл могли обновить)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { tried = false; clips = null; Error = null; }
        public static int Count { get { return clips != null ? clips.Count : 0; } }

        public static AnimClip Get(string n)
        {
            Load();
            AnimClip c;
            return clips != null && clips.TryGetValue(n, out c) ? c : null;
        }

        public static void Load()
        {
            if (tried) return;
            tried = true;
            var ta = Resources.Load<TextAsset>("Anim/clips_v4");
            if (ta == null) { Error = "нет Resources/Anim/clips_v4"; return; }
            try { Parse(ta.text); }
            catch (Exception e) { Error = e.Message; clips = null; Debug.LogWarning("[Стажёр] анимации v4 не прочитались: " + e.Message); }
        }

        static float F(string s) { return float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); }

        public static void Parse(string text)
        {
            clips = new Dictionary<string, AnimClip>();
            AnimClip cur = null;
            var sep = new[] { ' ', '\t', '\r' };
            foreach (var raw in text.Split('\n'))
            {
                if (raw.Length == 0 || raw[0] == '#') continue;
                var p = raw.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length == 0) continue;
                switch (p[0])
                {
                    case "bones":
                        Bones = new string[p.Length - 1]; BoneIndex.Clear();
                        for (int i = 1; i < p.Length; i++) { Bones[i - 1] = p[i]; BoneIndex[p[i]] = i - 1; }
                        break;
                    case "clip":
                        cur = new AnimClip { name = p[1] };
                        for (int i = 2; i + 1 < p.Length; i += 2)
                        {
                            switch (p[i])
                            {
                                case "loop": cur.loop = p[i + 1] == "1"; break;
                                case "len": cur.len = F(p[i + 1]); break;
                                case "speed": cur.speed = F(p[i + 1]); break;
                                case "dir": cur.dir = F(p[i + 1]); break;
                                case "frames": cur.frames = int.Parse(p[i + 1], CultureInfo.InvariantCulture); break;
                            }
                        }
                        cur.rot = new Quaternion[Bones.Length][];
                        break;
                    case "mask":
                        cur.mask = new bool[Bones.Length];
                        for (int i = 1; i < p.Length; i++) { int bi; if (BoneIndex.TryGetValue(p[i], out bi)) cur.mask[bi] = true; }
                        break;
                    case "root":
                        cur.root = new Vector3[cur.frames];
                        for (int i = 0; i < cur.frames; i++) cur.root[i] = new Vector3(F(p[1 + i * 3]), F(p[2 + i * 3]), F(p[3 + i * 3]));
                        break;
                    case "contact":
                        var c = new float[cur.frames];
                        for (int i = 0; i < cur.frames; i++) c[i] = F(p[2 + i]);
                        if (p[1] == "L") cur.contactL = c; else cur.contactR = c;
                        break;
                    case "b":
                    {
                        int bi = int.Parse(p[1], CultureInfo.InvariantCulture);
                        int n = (p.Length - 2) / 4;
                        var q = new Quaternion[n];
                        for (int i = 0; i < n; i++) q[i] = new Quaternion(F(p[2 + i * 4]), F(p[3 + i * 4]), F(p[4 + i * 4]), F(p[5 + i * 4]));
                        cur.rot[bi] = q;
                        break;
                    }
                    case "end":
                        if (cur != null) clips[cur.name] = cur;
                        cur = null;
                        break;
                }
            }
        }

        // Кадр клипа в буфер позы: w — вес при накоплении (сумма весов = 1), mask — только эти кости
        public static void Accumulate(AnimClip c, float t, Vector4[] acc, ref Vector3 root, float w)
        {
            int i0, i1; float f; c.Frame(t, out i0, out i1, out f);
            if (c.root != null) root += Vector3.Lerp(c.root[i0], c.root[i1], f) * w;
            for (int b = 0; b < acc.Length; b++)
            {
                var r = c.rot[b];
                Quaternion q;
                if (r == null) q = Quaternion.identity;
                else if (r.Length == 1) q = r[0];
                else q = Nlerp(r[i0], r[i1], f);
                var a = acc[b];
                float d = a.x * q.x + a.y * q.y + a.z * q.z + a.w * q.w;
                float s = d < 0f ? -w : w;
                acc[b] = new Vector4(a.x + q.x * s, a.y + q.y * s, a.z + q.z * s, a.w + q.w * s);
            }
        }

        public static void Sample(AnimClip c, float t, Quaternion[] outRot, out Vector3 root)
        {
            int i0, i1; float f; c.Frame(t, out i0, out i1, out f);
            root = c.root != null ? Vector3.Lerp(c.root[i0], c.root[i1], f) : Vector3.zero;
            for (int b = 0; b < outRot.Length; b++)
            {
                var r = c.rot[b];
                outRot[b] = r == null ? Quaternion.identity : r.Length == 1 ? r[0] : Nlerp(r[i0], r[i1], f);
            }
        }

        public static Quaternion Nlerp(Quaternion a, Quaternion b, float t)
        {
            float d = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
            float s = d < 0f ? -t : t, k = 1f - t;
            var q = new Quaternion(a.x * k + b.x * s, a.y * k + b.y * s, a.z * k + b.z * s, a.w * k + b.w * s);
            float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return m > 1e-6f ? new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m) : Quaternion.identity;
        }

        public static Quaternion Norm(Vector4 a)
        {
            float m = Mathf.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z + a.w * a.w);
            return m > 1e-6f ? new Quaternion(a.x / m, a.y / m, a.z / m, a.w / m) : Quaternion.identity;
        }
    }
}
