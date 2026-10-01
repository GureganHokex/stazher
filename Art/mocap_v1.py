# -*- coding: utf-8 -*-
# Живые движения из базы захвата движений CMU (D-10, доводка версии 0.9, Codezilla Games).
# Данные: Carnegie Mellon University Graphics Lab Motion Capture Database (mocap.cs.cmu.edu), BVH-перевод
# Bruce Hahne (cgspeed). «The data used in this project was obtained from mocap.cs.cmu.edu. The database was created
# with funding from NSF EIA-0196217.» Свободно для любых целей, включая коммерческие.
#
# Что делает: читает BVH, находит ровный участок шага, вырезает один цикл (от касания левой пяткой до следующего),
# убирает перемещение и поворот корня (персонаж идёт на месте, земля едет под ногами), переносит на наш скелет
# по направлениям костей (длины костей — наши, ступни ставит IK на высоту и в след записи, масштаб — по длине ног),
# сшивает конец цикла с началом. Результат — mocap_clips.json (позы по кадрам, 30 к/с), его читает anim_v4.build_clips.
# Нужен numpy; запускается в облачной песочнице, на компьютере достаточно готового mocap_clips.json.
import math, json, os
import numpy as np

# ---------------------------------------------------------------- BVH
class Bvh:
    def __init__(self, path):
        txt = open(path, encoding="utf-8", errors="ignore").read().split("\n")
        self.names, self.parent, self.offset, self.channels = [], [], [], []
        stack, i = [], 0
        while i < len(txt):
            line = txt[i].strip(); i += 1
            if not line: continue
            tok = line.split()
            if tok[0] in ("ROOT", "JOINT"):
                self.names.append(tok[1]); self.parent.append(stack[-1] if stack else -1); self.channels.append([])
                self.offset.append((0.0, 0.0, 0.0)); cur = len(self.names) - 1
            elif tok[0] == "End":
                self.names.append(self.names[stack[-1]] + "_end"); self.parent.append(stack[-1]); self.channels.append([])
                self.offset.append((0.0, 0.0, 0.0)); cur = len(self.names) - 1
            elif tok[0] == "{": stack.append(cur)
            elif tok[0] == "}": stack.pop()
            elif tok[0] == "OFFSET": self.offset[stack[-1]] = tuple(float(v) for v in tok[1:4])
            elif tok[0] == "CHANNELS": self.channels[stack[-1]] = tok[2:]
            elif tok[0] == "MOTION": break
        n = int(txt[i].split()[1]); self.dt = float(txt[i + 1].split()[2]); i += 2
        data = []
        for k in range(n):
            if i + k < len(txt) and txt[i + k].strip(): data.append([float(v) for v in txt[i + k].split()])
        self.data = np.array(data)
        self.offset = np.array(self.offset)
        self.index = {nm: j for j, nm in enumerate(self.names)}

    def positions(self):
        """Мировые положения суставов: [кадр, сустав, xyz] (единицы BVH)."""
        F, J = self.data.shape[0], len(self.names)
        P = np.zeros((F, J, 3)); R = np.zeros((F, J, 3, 3))
        col = 0; cols = []
        for j in range(J):
            cols.append(col); col += len(self.channels[j])
        for j in range(J):
            ch = self.channels[j]; c0 = cols[j]
            loc = np.tile(np.eye(3), (F, 1, 1)); pos = np.tile(self.offset[j], (F, 1))
            for k, c in enumerate(ch):
                v = self.data[:, c0 + k]
                if c.endswith("position"):
                    pos[:, "XYZ".index(c[0])] = v + (0 if self.parent[j] >= 0 else 0)
                else:
                    a = np.radians(v); ca, sa = np.cos(a), np.sin(a)
                    m = np.tile(np.eye(3), (F, 1, 1))
                    ax = "XYZ".index(c[0])
                    i1, i2 = [(1, 2), (2, 0), (0, 1)][ax]
                    m[:, i1, i1] = ca; m[:, i1, i2] = -sa; m[:, i2, i1] = sa; m[:, i2, i2] = ca
                    loc = loc @ m
            p = self.parent[j]
            if p < 0:
                P[:, j] = pos; R[:, j] = loc
            else:
                P[:, j] = P[:, p] + np.einsum("fij,fj->fi", R[:, p], pos if ch and ch[0].endswith("position") else np.tile(self.offset[j], (F, 1)))
                R[:, j] = R[:, p] @ loc
        return P, R

# ---------------------------------------------------------------- в наши оси и контакты
MIR = np.array([-1.0, 1.0, 1.0])     # BVH CMU: +X — влево персонажа; у нас +X — вправо

class Take:
    """Запись в наших осях (метры, X вправо, Y вверх), масштаб — по длине ноги нашего скелета."""
    def __init__(self, path, leg_ours):
        self.b = b = Bvh(path)
        P, R = b.positions()
        I = b.index
        leg = np.linalg.norm(P[0, I["LeftUpLeg"]] - P[0, I["LeftLeg"]]) + np.linalg.norm(P[0, I["LeftLeg"]] - P[0, I["LeftFoot"]])
        self.s = leg_ours / leg
        self.P = P[1:] * MIR * self.s            # кадр 0 — добавленная T-поза
        M = np.diag(MIR)
        # повороты от Т-позы записи (в ней у некоторых суставов есть небольшие довороты)
        R0inv = np.transpose(R[0], (0, 2, 1))
        Rrel = np.einsum("fkij,kjl->fkil", R[1:], R0inv)
        self.R = np.einsum("ij,fkjl,lm->fkim", M, Rrel, M)
        self.fps = 1.0 / b.dt
        self.I = I
        self.n = len(self.P)
        # земля: нижняя точка подушечек ступней по всей записи (5-й процентиль)
        toes = np.concatenate([self.P[:, I["LeftToeBase"], 1], self.P[:, I["RightToeBase"], 1]])
        self.ground = np.percentile(toes, 3)
        self.P[:, :, 1] -= self.ground

    def j(self, name): return self.P[:, self.I[name]]

    def contacts(self, side, hmax=0.06, vmax=0.35):
        """Опора ступни по кадрам: подушечка или голеностоп у земли и почти не едут."""
        a = self.j(side + "Foot"); t = self.j(side + "ToeBase")
        def speed(p):
            v = np.zeros(len(p)); v[1:] = np.linalg.norm(np.diff(p[:, [0, 2]], axis=0), axis=1) * self.fps; v[0] = v[1]; return v
        va, vt = speed(a), speed(t)
        ha = a[:, 1] - np.percentile(a[:, 1], 3); ht = t[:, 1]
        c = ((ht < hmax) & (vt < vmax)) | ((ha < hmax) & (va < vmax))
        # без дребезга: короткие провалы и всплески убираем
        c = c.astype(float)
        k = max(1, int(self.fps * 0.03))
        out = c.copy()
        for i in range(len(c)):
            out[i] = 1.0 if c[max(0, i - k):i + k + 1].mean() > 0.5 else 0.0
        return out

    def strikes(self, side):
        c = self.contacts(side)
        return [i for i in range(1, len(c)) if c[i] > 0.5 and c[i - 1] < 0.5]

# ---------------------------------------------------------------- перенос цикла на наш скелет
def mat2q(m):
    """Матрица 3×3 → кватернион (x, y, z, w)."""
    m00, m01, m02 = m[0]; m10, m11, m12 = m[1]; m20, m21, m22 = m[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2; q = ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s)
    elif m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2; q = (0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s)
    elif m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2; q = ((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s)
    else:
        s = math.sqrt(1.0 + m22 - m00 - m11) * 2; q = ((m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s)
    return qnorm(q)

def roty(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])

def bone_w(rest_dir, rest_hint, dir_, hint):
    """Мировой поворот кости: направление покоя → dir_, побочная ось покоя → hint (насколько можно)."""
    return qmul(qlook(dir_, hint), qconj(qlook(rest_dir, rest_hint)))

def V(a): return (float(a[0]), float(a[1]), float(a[2]))

def retarget(take, a, b, sk, n=None, hand_curl=24.0, thumb=16.0, wrist=(8.0, 4.0, 0.0), facing=None, canon=None):
    """Цикл take[a..b] (кадры записи) → (позы, контакты L/R, T, v, dir). Персонаж смотрит вдоль +Z.
    canon — точное направление хода (0 — вперёд, 90 — вправо, 180 — назад): курс подбирается под него."""
    I = take.I
    T = (b - a) / take.fps
    if n is None: n = max(8, int(round(T * FPS)))
    P, R = take.P, take.R
    def at(arr, x):
        i = int(math.floor(x)); f = x - i; i = max(0, min(i, len(arr) - 2))
        return arr[i] * (1 - f) + arr[i + 1] * f
    xs = [a + (b - a) * i / n for i in range(n + 1)]
    Ps = np.array([at(P, x) for x in xs]); Rs = np.array([at(R, x) for x in xs])
    # курс: куда смотрит таз в среднем за цикл
    rv = (Ps[:, I["RightUpLeg"]] - Ps[:, I["LeftUpLeg"]]).mean(axis=0)
    fac = np.array([-rv[2], 0.0, rv[0]]) if facing is None else facing
    psi = math.atan2(fac[0], fac[2])
    if canon is not None:
        h0 = (Ps[0, I["LeftUpLeg"]] + Ps[0, I["RightUpLeg"]]) / 2; h1 = (Ps[-1, I["LeftUpLeg"]] + Ps[-1, I["RightUpLeg"]]) / 2
        psi = math.atan2(h1[0] - h0[0], h1[2] - h0[2]) - math.radians(canon)
    Rot = roty(-psi)
    Ps = np.einsum("ij,fkj->fki", Rot, Ps); Rs = np.einsum("ij,fkjl->fkil", Rot, Rs)
    hip = (Ps[:, I["LeftUpLeg"]] + Ps[:, I["RightUpLeg"]]) / 2
    d = hip[-1] - hip[0]; d[1] = 0.0
    v = float(np.hypot(d[0], d[2]) / T); dirv = float(math.degrees(math.atan2(d[0], d[2])))
    # на месте: убираем ровное движение вперёд, начало — в нуле
    for i in range(n + 1):
        Ps[i, :, 0] -= hip[0][0] + d[0] * i / n
        Ps[i, :, 2] -= hip[0][2] + d[2] * i / n
    # высота: голеностопы записи в опоре — на высоту наших голеностопов
    cl = take.contacts("Left"); cr = take.contacts("Right")
    ca = np.array([at(cl[:, None], x)[0] for x in xs]); cb = np.array([at(cr[:, None], x)[0] for x in xs])
    ank = np.concatenate([Ps[ca > 0.5, I["LeftFoot"], 1], Ps[cb > 0.5, I["RightFoot"], 1]])
    dy = sk.rest["FootL"][1] - (np.percentile(ank, 15) if len(ank) else 0.05)   # стопа стоит ровно — нижние значения
    Ps[:, :, 1] += dy
    poses = []
    rest = sk.rest
    def rdir(a_, b_): return vnorm(vsub(rest[b_], rest[a_]))
    for i in range(n + 1):
        p = Pose(); Pq = Ps[i]; Rq = Rs[i]
        J = lambda nm: V(Pq[I[nm]])
        Wq = lambda nm: mat2q(Rq[I[nm]])
        W = {}
        # таз и позвоночник — повороты записи (Т-поза записи и наш покой — оба прямые)
        W["Hips"] = Wq("Hips")
        W["Spine"] = Wq("LowerBack"); W["Spine2"] = Wq("Spine"); W["Torso"] = Wq("Spine1")
        W["Neck"] = qslerp(Wq("Neck"), Wq("Neck1"), 0.5); W["Head"] = Wq("Head")
        # ключицы — по направлению, ось «вверх» — от груди
        up_t = qrot(W["Torso"], Y)
        for sd, ms in (("L", "Left"), ("R", "Right")):
            W["Clavicle" + sd] = bone_w(rdir("Clavicle" + sd, "Shoulder" + sd), Y, vnorm(vsub(J(ms + "Arm"), J(ms + "Shoulder"))), up_t)
        # руки — по направлениям плеча и предплечья, ось сгиба локтя — из их плоскости
        right_t = qrot(W["Torso"], X)
        for sd, ms in (("L", "Left"), ("R", "Right")):
            S, E, Wr = J(ms + "Arm"), J(ms + "ForeArm"), J(ms + "Hand")
            d1, d2 = vnorm(vsub(E, S)), vnorm(vsub(Wr, E))
            ax = vcross(d2, d1); k = min(1.0, max(0.0, (vlen(ax) - 0.08) / 0.25))
            ax = vnorm(vadd(vmul(vnorm(ax) if vlen(ax) > 1e-6 else right_t, k), vmul(right_t, 1 - k)))
            r1 = vnorm(vsub(rest["Shoulder" + sd], rest["Elbow" + sd])); r2 = vnorm(vsub(rest["Elbow" + sd], rest["Hand" + sd]))
            W["Shoulder" + sd] = qmul(qlook(vmul(d1, -1.0), ax), qconj(qlook(r1, X)))
            W["Elbow" + sd] = qmul(qlook(vmul(d2, -1.0), ax), qconj(qlook(r2, X)))
        # ноги — по направлениям бедра и голени, ось колена — из их плоскости
        right_h = qrot(W["Hips"], X)
        for sd, ms in (("L", "Left"), ("R", "Right")):
            H, K, A = J(ms + "UpLeg"), J(ms + "Leg"), J(ms + "Foot")
            d1, d2 = vnorm(vsub(K, H)), vnorm(vsub(A, K))
            ax = vcross(d1, d2); k = min(1.0, max(0.0, (vlen(ax) - 0.05) / 0.2))
            ax = vnorm(vadd(vmul(vnorm(ax) if vlen(ax) > 1e-6 else right_h, k), vmul(right_h, 1 - k)))
            r1 = vnorm(vsub(rest["Hip" + sd], rest["Knee" + sd])); r2 = vnorm(vsub(rest["Knee" + sd], rest["Foot" + sd]))
            W["Hip" + sd] = qmul(qlook(vmul(d1, -1.0), ax), qconj(qlook(r1, X)))
            W["Knee" + sd] = qmul(qlook(vmul(d2, -1.0), ax), qconj(qlook(r2, X)))
            W["Foot" + sd] = Wq(ms + "Foot"); W["Toe" + sd] = Wq(ms + "ToeBase")
        # локальные повороты
        for nm in sk.order:
            if nm not in W: continue
            par = sk.parent[nm]
            pw = QI
            q = par
            while q is not None and q not in W: q = sk.parent[q]
            if q is not None: pw = W[q]
            # промежуточные кости без своей цели (их нет у этих костей) — покой
            p.rot[nm] = qmul(qconj(pw), W[nm])
        # корень: середина тазобедренных суставов — как у записи
        hm = V((Pq[I["LeftUpLeg"]] + Pq[I["RightUpLeg"]]) / 2)
        rhm = vmul(vadd(rest["HipL"], rest["HipR"]), 0.5)
        p.root = vsub(vsub(hm, rest["Hips"]), qrot(W["Hips"], vsub(rhm, rest["Hips"])))
        # ступни — точно в след записи (длины костей у нас свои)
        legs = {}
        for sd, ms in (("L", "Left"), ("R", "Right")):
            K = J(ms + "Leg"); H = J(ms + "UpLeg")
            pole = vnorm(vsub(K, vmul(vadd(H, J(ms + "Foot")), 0.5)))
            fq = W["Foot" + sd]
            leg_ik(sk, p, sd, J(ms + "Foot"), fq, pole=pole)
            p.rot["Toe" + sd] = qmul(qconj(fq), W["Toe" + sd])
            legs[sd] = (J(ms + "Foot"), fq, pole)
        p._legs = legs
        # кисти — спокойные, пальцы полусогнуты
        for sd in "LR":
            p.rot["Hand" + sd] = qmul(qaxis(Y, wrist[2] * (1 if sd == "R" else -1)), qmul(qaxis(X, -wrist[1]), qaxis(Z, wrist[0] * (-1 if sd == "R" else 1))))
            keep = p.rot["Hand" + sd]
            hand_pose(p, sd, hand_curl, thumb, spread=2.0)
            p.rot["Hand" + sd] = keep
        if canon is not None and abs(canon) > 30.0:
            # вбок и назад актёр смотрел, куда идёт; в игре голова смотрит вперёд (туда, куда целится камера)
            head_look(p, sk, yaw=0.0, pitch=4.0)
        poses.append(p)
    # шов цикла: разницу последнего и первого кадра раскладываем по всему циклу
    first, last = poses[0], poses[-1]
    for nm in list(first.rot.keys()):
        if nm not in last.rot: continue
        err = qmul(qconj(last.rot[nm]), first.rot[nm])
        for i in range(n + 1):
            poses[i].rot[nm] = qmul(poses[i].rot[nm], qslerp(QI, err, i / n))
    rerr = vsub(first.root, last.root)
    for i in range(n + 1): poses[i].root = vadd(poses[i].root, vmul(rerr, i / n))
    # ступни заново после шва: цели голеностопов и повороты стоп тоже сшиты
    for sd in "LR":
        A0, f0, _ = poses[0]._legs[sd]; An, fn, _ = poses[-1]._legs[sd]
        dA = vsub(A0, An); ef = qmul(f0, qconj(fn))
        for i, p in enumerate(poses):
            A, fq, pole = p._legs[sd]
            A = vadd(A, vmul(dA, i / n)); fq = qmul(qslerp(QI, ef, i / n), fq)
            toe_w = qmul(fq, p.rot["Toe" + sd])
            leg_ik(sk, p, sd, A, fq, pole=pole)
            p._legs[sd] = (A, fq, pole)
    # подошвы не уходят под землю: наша стопа больше, чем у актёра; в переносе — ещё просвет 1,5 см
    for sd in "LR":
        cc = ca if sd == "L" else cb
        need = []
        for i, p in enumerate(poses):
            wp, wr = sk.fk(p)
            lo = min(vadd(wp["Foot" + sd], qrot(wr["Foot" + sd], pt))[1] for pt in (HEEL, BALL))
            lo = min(lo, vadd(wp["Toe" + sd], qrot(wr["Toe" + sd], vsub(TIP, TOEJ)))[1])
            want = 0.0 if cc[i] > 0.5 else 0.015
            need.append(max(0.0, want - lo))
        m = len(need)
        sm = [max(need[(i + k) % m] * math.exp(-(k / 2.5) ** 2) for k in range(-5, 6)) for i in range(m)]
        for i, p in enumerate(poses):
            if sm[i] <= 1e-4: continue
            A, fq, pole = p._legs[sd]
            leg_ik(sk, p, sd, vadd(A, (0.0, sm[i], 0.0)), fq, pole=pole)
    con = {"L": [float(min(1.0, max(0.0, c))) for c in ca[:n]], "R": [float(min(1.0, max(0.0, c))) for c in cb[:n]]}
    return poses[:n], con, T, v, dirv

# ---------------------------------------------------------------- набор клипов
# (имя, запись, кадры цикла, направление, кисти: сгиб пальцев, большой палец)
CLIPS = [
    ("walk", "07_01", 65, 197, 0.0, 24.0, 16.0),
    ("jog", "16_35", 6, 103, 0.0, 40.0, 22.0),
    ("run", "09_04", 1, 91, 0.0, 52.0, 26.0),
    ("sprint", "143_01", 54, 96, 0.0, 58.0, 30.0),
    ("walk_b", "113_01", 214, 385, 180.0, 24.0, 16.0),
    ("walk_r", "113_18", 540, 705, 90.0, 24.0, 16.0),
    ("walk_l", "113_18", 1475, 1637, -90.0, 24.0, 16.0),
]

def build_all(sk, bvh_dir, out_path):
    out = {}
    for name, take, a, b, canon, curl, th in CLIPS:
        t = Take(os.path.join(bvh_dir, take + ".bvh"), sk.thigh + sk.shin)
        poses, con, T, v, d = retarget(t, a, b, sk, hand_curl=curl, thumb=th, canon=canon)
        bones = [nm for nm in sk.names if not nm.startswith("Lid")]
        out[name] = dict(src="CMU %s %d-%d" % (take, a, b), len=round(T, 4), speed=round(v, 4), dir=canon,
                         contact={k: [round(x, 3) for x in vv] for k, vv in con.items()},
                         root=[[round(c, 5) for c in p.root] for p in poses],
                         rot={bn: [[round(c, 5) for c in p.rot.get(bn, QI)] for p in poses] for bn in bones})
        print("%-7s %s T=%.2f v=%.2f dir=%.0f кадров %d" % (name, out[name]["src"], T, v, canon, len(poses)))
    with open(out_path, "w", encoding="utf-8") as fh: json.dump(out, fh, separators=(",", ":"))
    return out
