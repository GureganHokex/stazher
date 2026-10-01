// Процедурные текстуры оружия (доводка версии 0.9, D-06). Модели из Blender (Art/weapons_v1.py) получили развёртку
// «кубом» в метрах; здесь — бесшовные карты 256×256, которые кладутся поверх цвета материала:
//   металл — лёгкая шлифовка и пятна, полимер — мелкое зерно, дерево — волокна и годовые кольца, клинок — шлиф вдоль,
//   рукоять — насечка «ромбом», обмотка катаны — плетение, кожа — мелкая шагрень.
// Цвет — множитель около единицы (материал остаётся своего цвета), рельеф — карта нормалей. Строится один раз за запуск.
using System.Collections.Generic;
using UnityEngine;

namespace Intern.Game
{
    public static class WeaponTex
    {
        public struct Set { public Texture2D albedo, normal; public float tiling, bump; }
        static readonly Dictionary<string, Set> cache = new Dictionary<string, Set>();
        const int N = 256;

        // Вид поверхности по имени материала из Blender
        public static string KindOf(string mat)
        {
            switch (mat)
            {
                case "wpn_metal": case "wpn_steel": case "wpn_alu": case "wpn_mag": case "wpn_dark": case "wpn_gold": case "wpn_brass": return "metal";
                case "wpn_polymer": case "wpn_fde": case "wpn_olive": case "wpn_tape": case "wpn_rubber": return "polymer";
                case "wpn_wood": case "wpn_woodd": return "wood";
                case "wpn_blade": case "wpn_edge": return "blade";
                case "wpn_grip": return "grip";
                case "wpn_wrap": return "wrap";
                case "wpn_leather": case "wpn_leatherd": return "leather";
            }
            return null;
        }

        public static bool Get(string kind, out Set s)
        {
            s = default(Set);
            if (kind == null) return false;
            if (cache.TryGetValue(kind, out s)) return s.albedo != null;
            var h = new float[N * N]; var a = new float[N * N];
            float tiling = 10f, bump = 0.3f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N, v = (y + 0.5f) / N; int i = y * N + x;
                    switch (kind)
                    {
                        case "metal":
                        {
                            float spots = Fbm(u, v, 4, 4, 11), brush = Value(u * 3f, v * 160f, 3, 160, 12);
                            h[i] = brush;
                            a[i] = 0.95f + 0.06f * spots + 0.03f * brush;
                            tiling = 8f; bump = 0.05f; break;
                        }
                        case "polymer":
                        {
                            float g1 = Value(u * 96f, v * 96f, 96, 96, 21), g2 = Value(u * 48f, v * 48f, 48, 48, 22);
                            h[i] = 0.6f * g1 + 0.4f * g2;
                            a[i] = 0.95f + 0.07f * Fbm(u, v, 4, 3, 23) + 0.03f * g1;
                            tiling = 18f; bump = 0.18f; break;
                        }
                        case "wood":
                        {
                            float t = v * 7f + 0.9f * Fbm(u, v, 2, 3, 31);
                            float g = t - Mathf.Floor(t);
                            float late = Mathf.Exp(-(g - 0.82f) * (g - 0.82f) / 0.006f);       // тёмная полоса позднего дерева
                            float pores = Value(u * 6f, v * 220f, 6, 220, 32);
                            h[i] = -0.6f * late + 0.4f * pores;
                            a[i] = 1.05f - 0.30f * late - 0.08f * pores + 0.05f * Fbm(u, v, 3, 3, 33);
                            tiling = 4f; bump = 0.25f; break;
                        }
                        case "blade":
                        {
                            float st = Value(u * 2f, v * 240f, 2, 240, 41);
                            h[i] = st;
                            a[i] = 0.95f + 0.07f * st + 0.03f * Fbm(u, v, 3, 3, 42);
                            tiling = 5f; bump = 0.12f; break;
                        }
                        case "grip":
                        {
                            // насечка ромбом: 4 ромба на тайл
                            float p1 = Mathf.Abs(Mathf.Sin(Mathf.PI * (u + v) * 4f)), p2 = Mathf.Abs(Mathf.Sin(Mathf.PI * (u - v) * 4f));
                            h[i] = (1f - p1) * (1f - p2);
                            a[i] = 0.9f + 0.12f * h[i] + 0.04f * Value(u * 64f, v * 64f, 64, 64, 51);
                            tiling = 12f; bump = 0.9f; break;
                        }
                        case "wrap":
                        {
                            // плетение шнура: ромбы с полосками
                            float p1 = Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 2f + v * 6f))), p2 = Mathf.Abs(Mathf.Sin(Mathf.PI * (u * 2f - v * 6f)));
                            h[i] = Mathf.Max(p1, p2);
                            a[i] = 0.82f + 0.25f * h[i];
                            tiling = 8f; bump = 0.8f; break;
                        }
                        default:   // leather
                        {
                            float c = Value(u * 40f, v * 40f, 40, 40, 61), f = Value(u * 120f, v * 120f, 120, 120, 62);
                            h[i] = 0.7f * c + 0.3f * f;
                            a[i] = 0.92f + 0.12f * Fbm(u, v, 4, 3, 63) + 0.04f * c;
                            tiling = 10f; bump = 0.5f; break;
                        }
                    }
                }
            var alb = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { name = "wpn_" + kind + "_alb", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var nrm = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "wpn_" + kind + "_nrm", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var ca = new Color32[N * N]; var cn = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float g = Mathf.Clamp01(a[i] * 0.85f);                      // 1.0 множителя ≈ 0.85 в текстуре (запас на светлое)
                    byte gb = (byte)(g * 255f);
                    ca[i] = new Color32(gb, gb, gb, 255);
                    float dx = h[y * N + (x + 1) % N] - h[y * N + (x + N - 1) % N];
                    float dy = h[((y + 1) % N) * N + x] - h[((y + N - 1) % N) * N + x];
                    var n = new Vector3(-dx * 4f, -dy * 4f, 1f).normalized;
                    // упаковка как у карт нормалей Unity на ПК (DXT5nm): x в альфе, y в зелёном
                    cn[i] = new Color32(255, (byte)((n.y * 0.5f + 0.5f) * 255f), (byte)((n.z * 0.5f + 0.5f) * 255f), (byte)((n.x * 0.5f + 0.5f) * 255f));
                }
            alb.SetPixels32(ca); alb.Apply(true, true);
            nrm.SetPixels32(cn); nrm.Apply(true, true);
            s = new Set { albedo = alb, normal = nrm, tiling = tiling, bump = bump };
            cache[kind] = s;
            return true;
        }

        // Наложить на материал URP Lit: цвет материала делится на 0.85 (текстура — множитель с запасом на светлое)
        public static void Apply(Material m, string kind)
        {
            Set s;
            if (m == null || !Get(kind, out s)) return;
            if (m.HasProperty("_BaseColor")) { var c = m.GetColor("_BaseColor"); m.SetColor("_BaseColor", new Color(Mathf.Min(1f, c.r / 0.85f), Mathf.Min(1f, c.g / 0.85f), Mathf.Min(1f, c.b / 0.85f), c.a)); }
            if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", s.albedo); m.SetTextureScale("_BaseMap", new Vector2(s.tiling, s.tiling)); }
            if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", s.normal); m.SetFloat("_BumpScale", s.bump); m.EnableKeyword("_NORMALMAP"); }
        }

        // ---------- бесшовный шум ----------
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }

        // Значение шума в точке (x, y) решётки с периодом (px, py) — бесшовно на тайле
        static float Value(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0; fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int X0 = ((x0 % px) + px) % px, X1 = (X0 + 1) % px, Y0 = ((y0 % py) + py) % py, Y1 = (Y0 + 1) % py;
            float a = Hash(X0, Y0, seed), b = Hash(X1, Y0, seed), c = Hash(X0, Y1, seed), d = Hash(X1, Y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        static float Fbm(float u, float v, int f0, int oct, int seed)
        {
            float sum = 0f, amp = 1f, tot = 0f; int f = f0;
            for (int o = 0; o < oct; o++) { sum += Value(u * f, v * f, f, f, seed + o * 7) * amp; tot += amp; amp *= 0.5f; f *= 2; }
            return sum / tot;
        }
    }
}
