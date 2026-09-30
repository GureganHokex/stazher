// Гладкие модели персонажей (спринт 3 версии 0.9): на «Высоком» и «Ультра» вместо угловатых low-poly сеток —
// те же сетки, разбитые на более мелкие треугольники по кривизне поверхности (PN-треугольники: исходные вершины
// остаются на месте, середины рёбер выгибаются по нормалям — одежда не проваливается в тело), и сглаженные нормали.
// Острые углы (подошвы, воротники, очки) сохраняются: нормали сглаживаются только между гранями с углом меньше 60°.
// Один раз на исходную сетку и уровень; кости, веса и материалы — как у исходной.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Intern.Game
{
    public static class MeshSmooth
    {
        public const float CreaseAngle = 60f;
        static readonly Dictionary<Mesh, Mesh>[] cache = { null, new Dictionary<Mesh, Mesh>(), new Dictionary<Mesh, Mesh>() };
        static readonly HashSet<Mesh> unreadable = new HashSet<Mesh>();

        // level 0 — исходная сетка; 1 — каждый треугольник на 4; 2 — на 16
        public static Mesh Get(Mesh src, int level)
        {
            if (src == null || level <= 0 || src.blendShapeCount > 0) return src;
            level = Mathf.Min(level, 2);
            Mesh m;
            if (cache[level].TryGetValue(src, out m) && m != null) return m;
            if (!src.isReadable) { if (unreadable.Add(src)) Debug.LogWarning("[Стажёр] сетка " + src.name + " не читается (Read/Write в импорте FBX) — остаётся угловатой"); return src; }
            try { m = Build(src, level); }
            catch (System.Exception e) { Debug.LogWarning("[Стажёр] не сгладилась сетка " + src.name + ": " + e.Message); m = src; }
            cache[level][src] = m;
            return m;
        }

        // ---------- рабочее представление: общие вершины без швов ----------
        class Work
        {
            public readonly List<Vector3> p = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<BoneWeight> w = new List<BoneWeight>();
            public readonly List<int> tri = new List<int>();      // по 3 индекса
            public readonly List<int> sub = new List<int>();      // подсетка каждого треугольника
            public Vector3[] n;                                    // сглаженная нормаль вершины (по всем граням)
            public bool skinned;
        }

        static Mesh Build(Mesh src, int level)
        {
            var wk = Weld(src);
            for (int i = 0; i < level; i++) { SmoothNormals(wk); wk = Tessellate(wk); }
            return Output(src, wk, level);
        }

        static Work Weld(Mesh src)
        {
            var wk = new Work();
            var vs = src.vertices; var uvs = src.uv; var bws = src.boneWeights;
            wk.skinned = bws != null && bws.Length == vs.Length;
            bool hasUv = uvs != null && uvs.Length == vs.Length;
            var map = new int[vs.Length];
            var index = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < vs.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(vs[i].x * 20000f), Mathf.RoundToInt(vs[i].y * 20000f), Mathf.RoundToInt(vs[i].z * 20000f));
                int id;
                if (!index.TryGetValue(k, out id))
                {
                    id = wk.p.Count; index[k] = id;
                    wk.p.Add(vs[i]); wk.uv.Add(hasUv ? uvs[i] : Vector2.zero); wk.w.Add(wk.skinned ? bws[i] : new BoneWeight());
                }
                map[i] = id;
            }
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (src.GetTopology(s) != MeshTopology.Triangles) continue;
                var t = src.GetTriangles(s);
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = map[t[i]], b = map[t[i + 1]], c = map[t[i + 2]];
                    if (a == b || b == c || a == c) continue;
                    wk.tri.Add(a); wk.tri.Add(b); wk.tri.Add(c); wk.sub.Add(s);
                }
            }
            return wk;
        }

        static Vector3 FaceNormal(Work wk, int t, out float area)
        {
            var a = wk.p[wk.tri[t * 3]]; var b = wk.p[wk.tri[t * 3 + 1]]; var c = wk.p[wk.tri[t * 3 + 2]];
            var cr = Vector3.Cross(b - a, c - a);
            area = cr.magnitude;
            return area > 1e-12f ? cr / area : Vector3.zero;
        }

        static void SmoothNormals(Work wk)
        {
            var n = new Vector3[wk.p.Count];
            int nt = wk.sub.Count;
            for (int t = 0; t < nt; t++)
            {
                float ar; var fn = FaceNormal(wk, t, out ar);
                for (int k = 0; k < 3; k++) n[wk.tri[t * 3 + k]] += fn * ar;
            }
            for (int i = 0; i < n.Length; i++) n[i] = n[i].sqrMagnitude > 1e-20f ? n[i].normalized : Vector3.up;
            wk.n = n;
        }

        // PN-треугольники, середины рёбер: B = (Pa + Pb)/2 − ((Pb−Pa)·Na·Na + (Pa−Pb)·Nb·Nb)/8
        static Work Tessellate(Work wk)
        {
            var o = new Work { skinned = wk.skinned };
            o.p.AddRange(wk.p); o.uv.AddRange(wk.uv); o.w.AddRange(wk.w);
            var mids = new Dictionary<long, int>();
            System.Func<int, int, int> Mid = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                int id;
                if (mids.TryGetValue(key, out id)) return id;
                Vector3 pa = wk.p[a], pb = wk.p[b], na = wk.n[a], nb = wk.n[b];
                var m = (pa + pb) * 0.5f - (Vector3.Dot(pb - pa, na) * na + Vector3.Dot(pa - pb, nb) * nb) * 0.125f;
                id = o.p.Count; mids[key] = id;
                o.p.Add(m); o.uv.Add((wk.uv[a] + wk.uv[b]) * 0.5f); o.w.Add(o.skinned ? Blend(wk.w[a], wk.w[b]) : new BoneWeight());
                return id;
            };
            int nt = wk.sub.Count;
            for (int t = 0; t < nt; t++)
            {
                int a = wk.tri[t * 3], b = wk.tri[t * 3 + 1], c = wk.tri[t * 3 + 2], s = wk.sub[t];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                Add(o, a, ab, ca, s); Add(o, ab, b, bc, s); Add(o, ca, bc, c, s); Add(o, ab, bc, ca, s);
            }
            return o;
        }

        static void Add(Work o, int a, int b, int c, int s) { o.tri.Add(a); o.tri.Add(b); o.tri.Add(c); o.sub.Add(s); }

        // веса середины ребра: половина от каждого конца, четыре самых больших, сумма 1
        static BoneWeight Blend(BoneWeight x, BoneWeight y)
        {
            var d = new Dictionary<int, float>(8);
            System.Action<int, float> put = (i, v) => { if (v <= 0f) return; float c; d.TryGetValue(i, out c); d[i] = c + v * 0.5f; };
            put(x.boneIndex0, x.weight0); put(x.boneIndex1, x.weight1); put(x.boneIndex2, x.weight2); put(x.boneIndex3, x.weight3);
            put(y.boneIndex0, y.weight0); put(y.boneIndex1, y.weight1); put(y.boneIndex2, y.weight2); put(y.boneIndex3, y.weight3);
            var list = new List<KeyValuePair<int, float>>(d);
            list.Sort((p, q) => q.Value.CompareTo(p.Value));
            float sum = 0f; for (int i = 0; i < list.Count && i < 4; i++) sum += list[i].Value;
            if (sum <= 0f) return x;
            var r = new BoneWeight();
            if (list.Count > 0) { r.boneIndex0 = list[0].Key; r.weight0 = list[0].Value / sum; }
            if (list.Count > 1) { r.boneIndex1 = list[1].Key; r.weight1 = list[1].Value / sum; }
            if (list.Count > 2) { r.boneIndex2 = list[2].Key; r.weight2 = list[2].Value / sum; }
            if (list.Count > 3) { r.boneIndex3 = list[3].Key; r.weight3 = list[3].Value / sum; }
            return r;
        }

        // Сетка для Unity: нормали по углам треугольников — сглаживаем только грани с углом меньше CreaseAngle,
        // вершина с разными нормалями делится на несколько
        static Mesh Output(Mesh src, Work wk, int level)
        {
            int nt = wk.sub.Count;
            var fn = new Vector3[nt]; var fa = new float[nt];
            for (int t = 0; t < nt; t++) fn[t] = FaceNormal(wk, t, out fa[t]);
            var around = new List<int>[wk.p.Count];
            for (int t = 0; t < nt; t++)
                for (int k = 0; k < 3; k++) { int v = wk.tri[t * 3 + k]; (around[v] ?? (around[v] = new List<int>(6))).Add(t); }
            float cosLim = Mathf.Cos(CreaseAngle * Mathf.Deg2Rad);
            var outP = new List<Vector3>(wk.p.Count * 2); var outN = new List<Vector3>(wk.p.Count * 2);
            var outUv = new List<Vector2>(wk.p.Count * 2); var outW = new List<BoneWeight>(wk.p.Count * 2);
            var split = new Dictionary<long, int>();
            var subTris = new List<int>[src.subMeshCount];
            for (int s = 0; s < subTris.Length; s++) subTris[s] = new List<int>();
            for (int t = 0; t < nt; t++)
            {
                if (fa[t] < 1e-10f) continue;   // вырожденный треугольник не виден, а его нулевая нормаль дала бы NaN в свете и белую вспышку
                for (int k = 0; k < 3; k++)
                {
                    int v = wk.tri[t * 3 + k];
                    Vector3 sum = Vector3.zero;
                    foreach (var o in around[v]) if (Vector3.Dot(fn[o], fn[t]) >= cosLim) sum += fn[o] * fa[o];
                    var nrm = sum.sqrMagnitude > 1e-20f ? sum.normalized : fn[t];
                    if (!(nrm.sqrMagnitude > 0.5f)) nrm = Vector3.up;   // и на всякий случай — никогда не нулевая и не NaN
                    // одинаковые нормали у вершины — одна вершина сетки
                    long q = (Mathf.RoundToInt(nrm.x * 31f) + 32) * 4096L + (Mathf.RoundToInt(nrm.y * 31f) + 32) * 64L + (Mathf.RoundToInt(nrm.z * 31f) + 32);
                    long key = (long)v * 262144L + q;
                    int id;
                    if (!split.TryGetValue(key, out id))
                    {
                        id = outP.Count; split[key] = id;
                        outP.Add(wk.p[v]); outN.Add(nrm); outUv.Add(wk.uv[v]); outW.Add(wk.w[v]);
                    }
                    subTris[wk.sub[t]].Add(id);
                }
            }
            var m = new Mesh { name = src.name + "_smooth" + level };
            if (outP.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(outP); m.SetNormals(outN); m.SetUVs(0, outUv);
            if (wk.skinned) { m.boneWeights = outW.ToArray(); m.bindposes = src.bindposes; }
            m.subMeshCount = src.subMeshCount;
            for (int s = 0; s < subTris.Length; s++) m.SetTriangles(subTris[s], s, false);
            m.bounds = src.bounds;
            m.UploadMeshData(false);   // сетку читают повторно только из кэша исходника
            return m;
        }
    }
}
