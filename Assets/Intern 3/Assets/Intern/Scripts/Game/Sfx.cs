// Звуки оружия (спринт 8 версии 0.9 «Оружие в руках», задача 5): всё синтезируется кодом при первом звуке —
// выстрелы (щелчок фронта, хлопок газов, низ и хвост эха; у каждого ствола свои), выстрел с глушителем, механика
// (затвор, магазин, патрон, цевьё, крышка, щелчок пустого), звон гильз, свист клинка, удары по телу, по дереву,
// попадание в землю. Источники — пул из 24 штук в мире, громкость — настройка «Эффекты».
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public static class Sfx
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip[]> clips = new Dictionary<string, AudioClip[]>();
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform root;
        static int next;
        public static int Played;   // для самопроверки

        // ---------- синтез ----------
        class Buf
        {
            public float[] d; public System.Random r;
            public Buf(float sec, int seed) { d = new float[Mathf.Max(1, (int)(sec * Rate))]; r = new System.Random(seed); }
            public float N() { return (float)r.NextDouble() * 2f - 1f; }
            // шум: огибающая (атака, спад), фильтр: lp — доля низких (0..1, меньше — глуше), hp — срез низа
            public void Noise(float at, float gain, float attack, float tau, float lp, float hp = 0f, float len = 9f)
            {
                int i0 = (int)(at * Rate); float a = 0f, b = 0f;
                for (int i = i0; i < d.Length; i++)
                {
                    float t = (i - i0) / (float)Rate; if (t > len) break;
                    float env = (attack > 0f ? Mathf.Min(1f, t / attack) : 1f) * Mathf.Exp(-t / tau);
                    if (env < 0.0005f && t > attack) break;
                    float x = N(); a += (x - a) * lp; b += (a - b) * hp;
                    d[i] += (a - b) * env * gain;
                }
            }
            // тон: частота скользит от f0 к f1, затухание tau
            public void Tone(float at, float gain, float f0, float f1, float tau, float attack = 0.002f, float len = 9f)
            {
                int i0 = (int)(at * Rate); float ph = 0f;
                for (int i = i0; i < d.Length; i++)
                {
                    float t = (i - i0) / (float)Rate; if (t > len) break;
                    float env = Mathf.Min(1f, t / attack) * Mathf.Exp(-t / tau);
                    if (env < 0.0005f && t > attack) break;
                    float f = f1 + (f0 - f1) * Mathf.Exp(-t / (tau * 0.6f));
                    ph += 2f * Mathf.PI * f / Rate;
                    d[i] += Mathf.Sin(ph) * env * gain;
                }
            }
            // металлический «дзынь»: несколько негармоничных обертонов
            public void Ping(float at, float gain, float f, float tau)
            {
                Tone(at, gain, f, f, tau, 0.0005f);
                Tone(at, gain * 0.6f, f * 2.76f, f * 2.76f, tau * 0.7f, 0.0005f);
                Tone(at, gain * 0.35f, f * 5.4f, f * 5.4f, tau * 0.5f, 0.0005f);
            }
            public void Click(float at, float gain, float f = 3200f)
            {
                Noise(at, gain, 0.0003f, 0.003f, 0.9f, 0.4f);
                Ping(at, gain * 0.5f, f, 0.012f);
            }
            public AudioClip Clip(string name)
            {
                float m = 0f; foreach (var x in d) m = Mathf.Max(m, Mathf.Abs(x));
                if (m > 0.98f) for (int i = 0; i < d.Length; i++) d[i] = d[i] / m * 0.98f;
                // мягкий конец
                int f = Mathf.Min(d.Length, Rate / 50);
                for (int i = 0; i < f; i++) d[d.Length - 1 - i] *= i / (float)f;
                var c = AudioClip.Create(name, d.Length, 1, Rate, false); c.SetData(d, 0);
                return c;
            }
        }

        // Выстрел: фронт, газы, низ, хвост
        static AudioClip Shot(string name, int seed, float crack, float blastTau, float lp, float f0, float f1, float boomTau, float tail, float tailTau, float boom = 0.9f)
        {
            var b = new Buf(0.15f + tailTau * 4f, seed);
            b.Noise(0f, crack, 0f, 0.004f, 0.95f, 0.5f);
            b.Noise(0.001f, 1.0f, 0.001f, blastTau, lp);
            b.Tone(0f, boom, f0, f1, boomTau, 0.003f);
            b.Noise(0.02f, tail, 0.02f, tailTau, lp * 0.5f);
            return b.Clip(name);
        }

        static void Build()
        {
            if (clips.Count > 0) return;
            System.Func<int, System.Func<int, AudioClip>, AudioClip[]> V = (n, f) => { var a = new AudioClip[n]; for (int i = 0; i < n; i++) a[i] = f(i); return a; };
            clips["shot_pistol"] = V(3, i => Shot("shot_pistol", 11 + i, 0.9f, 0.05f, 0.35f, 150f, 60f, 0.07f, 0.32f, 0.22f));
            clips["shot_smg"] = V(3, i => Shot("shot_smg", 21 + i, 0.7f, 0.035f, 0.42f, 170f, 70f, 0.05f, 0.22f, 0.14f, 0.7f));
            clips["shot_shotgun"] = V(2, i => Shot("shot_shotgun", 31 + i, 0.8f, 0.12f, 0.2f, 95f, 38f, 0.2f, 0.5f, 0.5f, 1.1f));
            clips["shot_rifle"] = V(3, i => Shot("shot_rifle", 41 + i, 1f, 0.06f, 0.3f, 125f, 48f, 0.1f, 0.4f, 0.32f));
            clips["shot_sniper"] = V(2, i => Shot("shot_sniper", 51 + i, 1f, 0.14f, 0.22f, 85f, 32f, 0.26f, 0.6f, 0.7f, 1.1f));
            clips["shot_mg"] = V(3, i => Shot("shot_mg", 61 + i, 0.9f, 0.07f, 0.28f, 115f, 45f, 0.1f, 0.38f, 0.28f));
            clips["shot_silenced"] = V(3, i => { var b = new Buf(0.25f, 71 + i); b.Noise(0f, 0.7f, 0.001f, 0.03f, 0.15f); b.Tone(0f, 0.5f, 180f, 90f, 0.04f); b.Click(0.004f, 0.35f, 2600f); return b.Clip("shot_silenced"); });
            clips["dry"] = V(2, i => { var b = new Buf(0.12f, 81 + i); b.Click(0f, 0.6f, 2900f + i * 300f); return b.Clip("dry"); });
            clips["slide"] = V(2, i => { var b = new Buf(0.3f, 91 + i); b.Click(0f, 0.6f, 2700f); b.Noise(0.01f, 0.25f, 0.01f, 0.05f, 0.6f, 0.3f, 0.08f); b.Click(0.11f, 0.9f, 2200f); b.Ping(0.11f, 0.3f, 1500f, 0.04f); return b.Clip("slide"); });
            clips["bolt"] = V(2, i => { var b = new Buf(0.35f, 101 + i); b.Click(0f, 0.7f, 2400f); b.Noise(0.015f, 0.3f, 0.01f, 0.06f, 0.55f, 0.3f, 0.09f); b.Click(0.14f, 1f, 1900f); b.Ping(0.14f, 0.35f, 1200f, 0.05f); return b.Clip("bolt"); });
            clips["mag_out"] = V(2, i => { var b = new Buf(0.3f, 111 + i); b.Click(0f, 0.6f, 3100f); b.Noise(0.02f, 0.25f, 0.02f, 0.06f, 0.45f, 0.25f, 0.12f); return b.Clip("mag_out"); });
            clips["mag_in"] = V(2, i => { var b = new Buf(0.25f, 121 + i); b.Noise(0f, 0.2f, 0.01f, 0.03f, 0.4f, 0.2f, 0.05f); b.Tone(0.05f, 0.4f, 220f, 160f, 0.03f); b.Click(0.05f, 1f, 2300f); return b.Clip("mag_in"); });
            clips["shell_in"] = V(3, i => { var b = new Buf(0.2f, 131 + i); b.Click(0f, 0.5f, 2000f); b.Noise(0.01f, 0.2f, 0.01f, 0.04f, 0.4f, 0.2f, 0.06f); b.Click(0.06f, 0.7f, 1800f); return b.Clip("shell_in"); });
            clips["pump"] = V(2, i => { var b = new Buf(0.45f, 141 + i); b.Noise(0f, 0.45f, 0.008f, 0.04f, 0.5f, 0.25f, 0.07f); b.Click(0.07f, 0.9f, 1700f); b.Noise(0.17f, 0.45f, 0.008f, 0.04f, 0.5f, 0.25f, 0.07f); b.Click(0.24f, 1f, 1900f); return b.Clip("pump"); });
            clips["cover"] = V(2, i => { var b = new Buf(0.3f, 151 + i); b.Click(0f, 0.8f, 1500f); b.Ping(0f, 0.3f, 900f, 0.06f); b.Noise(0.01f, 0.15f, 0.01f, 0.06f, 0.4f); return b.Clip("cover"); });
            clips["belt"] = V(1, i => { var b = new Buf(0.45f, 161); for (int k = 0; k < 9; k++) b.Ping(k * 0.04f + (float)b.r.NextDouble() * 0.01f, 0.2f, 2400f + (float)b.r.NextDouble() * 900f, 0.02f); return b.Clip("belt"); });
            clips["tink"] = V(5, i => { var b = new Buf(0.4f, 171 + i); float t = 0f, g = 0.5f; for (int k = 0; k < 3; k++) { b.Ping(t, g, 2800f + i * 260f + k * 120f, 0.05f); t += 0.05f + (float)b.r.NextDouble() * 0.06f; g *= 0.55f; } return b.Clip("tink"); });
            clips["thunk"] = V(3, i => { var b = new Buf(0.3f, 181 + i); b.Tone(0f, 0.6f, 180f + i * 20f, 120f, 0.04f); b.Noise(0f, 0.3f, 0.002f, 0.03f, 0.3f); b.Ping(0.01f, 0.2f, 700f, 0.05f); return b.Clip("thunk"); });
            clips["swoosh"] = V(3, i => Swoosh(191 + i, 0.22f + i * 0.03f, 0.5f));
            clips["swoosh_heavy"] = V(2, i => Swoosh(201 + i, 0.32f, 0.3f));
            clips["hit_flesh"] = V(3, i => { var b = new Buf(0.3f, 211 + i); b.Tone(0f, 0.9f, 110f + i * 12f, 60f, 0.05f); b.Noise(0f, 0.6f, 0.001f, 0.035f, 0.25f); b.Noise(0f, 0.35f, 0f, 0.008f, 0.8f, 0.4f); return b.Clip("hit_flesh"); });
            clips["hit_blade"] = V(3, i => { var b = new Buf(0.35f, 221 + i); b.Noise(0f, 0.6f, 0.002f, 0.06f, 0.75f, 0.45f); b.Tone(0f, 0.5f, 120f, 70f, 0.04f); b.Noise(0.01f, 0.4f, 0.001f, 0.03f, 0.3f); return b.Clip("hit_blade"); });
            clips["hit_bat"] = V(3, i => { var b = new Buf(0.4f, 231 + i); b.Tone(0f, 1f, 95f, 50f, 0.07f); b.Tone(0f, 0.5f, 340f + i * 30f, 300f, 0.035f); b.Tone(0f, 0.3f, 720f, 700f, 0.025f); b.Noise(0f, 0.5f, 0.001f, 0.03f, 0.3f); return b.Clip("hit_bat"); });
            clips["impact"] = V(4, i => { var b = new Buf(0.3f, 241 + i); b.Noise(0f, 0.5f, 0.001f, 0.05f, 0.35f, 0.05f); b.Tone(0f, 0.3f, 160f, 90f, 0.03f); b.Noise(0f, 0.3f, 0f, 0.006f, 0.9f, 0.5f); return b.Clip("impact"); });
        }

        static AudioClip Swoosh(int seed, float len, float bright)
        {
            var b = new Buf(len + 0.05f, seed);
            float a = 0f, c = 0f;
            for (int i = 0; i < b.d.Length; i++)
            {
                float t = i / (float)Rate, u = Mathf.Clamp01(t / len);
                float env = Mathf.Sin(Mathf.PI * u); env *= env;
                float k = Mathf.Lerp(0.05f, bright, Mathf.Sin(Mathf.PI * u));   // фильтр открывается к середине
                float x = b.N(); a += (x - a) * k; c += (a - c) * 0.08f;
                b.d[i] = (a - c) * env * 0.9f;
            }
            return b.Clip("swoosh");
        }

        // ---------- воспроизведение ----------
        static AudioSource Source()
        {
            if (root == null)
            {
                root = new GameObject("Sfx").transform;
                Object.DontDestroyOnLoad(root.gameObject);
                pool.Clear();
            }
            if (pool.Count < 24)
            {
                var go = new GameObject("SfxSrc"); go.transform.SetParent(root, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = 2f; s.maxDistance = 80f; s.dopplerLevel = 0f;
                pool.Add(s); return s;
            }
            for (int k = 0; k < pool.Count; k++) { var s = pool[(next + k) % pool.Count]; if (s != null && !s.isPlaying) { next = (next + k + 1) % pool.Count; return s; } }
            next = (next + 1) % pool.Count; return pool[next];
        }

        public static bool Has(string id) { Build(); return clips.ContainsKey(id); }

        // id — вид звука; spatial 1 — в мире, 0 — «в голове» (свои выстрелы чуть ближе к уху)
        public static void Play(string id, Vector3 at, float volume = 1f, float pitchVar = 0.05f, float spatial = 1f, float minDist = 2f)
        {
            Build();
            AudioClip[] list;
            if (!clips.TryGetValue(id, out list) || list.Length == 0) return;
            float vol = volume * Mathf.Clamp01(GameConfig.S.sfx);
            if (vol <= 0.001f) return;
            var s = Source(); if (s == null) return;
            s.transform.position = at;
            s.clip = list[Random.Range(0, list.Length)];
            s.volume = Mathf.Clamp01(vol); s.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            s.spatialBlend = spatial; s.minDistance = minDist;
            s.Play();
            Played++;
        }
    }
}
