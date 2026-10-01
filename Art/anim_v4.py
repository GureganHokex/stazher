# -*- coding: utf-8 -*-
# Анимации персонажей v4 (спринт 7 версии 0.9 «Живые персонажи», Codezilla Games).
#
# Клипы собираются как у аниматора: ключевые позы и кривые, только записаны кодом. Ноги в опоре
# стоят на земле точно (обратная кинематика от точки опоры стопы: пятка → вся стопа → подушечка),
# в переносе стопа идёт по дуге; таз качается, поворачивается и проседает, корпус крутится навстречу
# тазу, руки машут в противофазе ногам с запаздыванием локтя, голова держит взгляд.
#
# Оси — как у персонажа в Unity: X вправо, Y вверх, Z вперёд. Вращение каждой кости — относительно
# родителя, в позе покоя все кости без поворота (так их видит CharacterAnim после NormalizeRig).
#
# Работает и в обычном Python (сборка clips_v4.json для игры), и в Blender (просмотр клипов на модели):
#   exec(open(r"<repo>/Art/anim_v4.py", encoding="utf-8").read()); preview_all(arm)
import math, json

# ============================================================ математика
def vadd(a, b): return (a[0] + b[0], a[1] + b[1], a[2] + b[2])
def vsub(a, b): return (a[0] - b[0], a[1] - b[1], a[2] - b[2])
def vmul(a, k): return (a[0] * k, a[1] * k, a[2] * k)
def vdot(a, b): return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
def vcross(a, b): return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
def vlen(a): return math.sqrt(vdot(a, a))
def vnorm(a):
    l = vlen(a); return (a[0] / l, a[1] / l, a[2] / l) if l > 1e-12 else (0.0, 0.0, 0.0)
def vlerp(a, b, t): return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)

QI = (0.0, 0.0, 0.0, 1.0)
X, Y, Z = (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0)

def qaxis(ax, deg):
    ax = vnorm(ax); h = math.radians(deg) * 0.5; s = math.sin(h)
    return (ax[0] * s, ax[1] * s, ax[2] * s, math.cos(h))

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)

def qconj(q): return (-q[0], -q[1], -q[2], q[3])

def qrot(q, v):
    t = vmul(vcross(q[:3], v), 2.0)
    return vadd(vadd(v, vmul(t, q[3])), vcross(q[:3], t))

def qnorm(q):
    l = math.sqrt(sum(c * c for c in q)); return tuple(c / l for c in q)

def qeuler(x=0.0, y=0.0, z=0.0):
    """Как Quaternion.Euler в Unity: сначала Z, потом X, потом Y."""
    return qmul(qaxis(Y, y), qmul(qaxis(X, x), qaxis(Z, z)))

def qslerp(a, b, t):
    d = sum(a[i] * b[i] for i in range(4))
    if d < 0: b = tuple(-c for c in b); d = -d
    if d > 0.9995: return qnorm(tuple(a[i] + (b[i] - a[i]) * t for i in range(4)))
    th = math.acos(min(1.0, d)); s = math.sin(th)
    ka, kb = math.sin((1 - t) * th) / s, math.sin(t * th) / s
    return tuple(a[i] * ka + b[i] * kb for i in range(4))

def qfrom_basis(xa, ya, za):
    """Кватернион поворота, переводящего оси X, Y, Z в столбцы xa, ya, za (ортонормированные)."""
    m00, m01, m02 = xa[0], ya[0], za[0]
    m10, m11, m12 = xa[1], ya[1], za[1]
    m20, m21, m22 = xa[2], ya[2], za[2]
    tr = m00 + m11 + m22
    if tr > 0:
        s = math.sqrt(tr + 1.0) * 2; return qnorm(((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s))
    if m00 > m11 and m00 > m22:
        s = math.sqrt(1.0 + m00 - m11 - m22) * 2; return qnorm((0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s))
    if m11 > m22:
        s = math.sqrt(1.0 + m11 - m00 - m22) * 2; return qnorm(((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s))
    s = math.sqrt(1.0 + m22 - m00 - m11) * 2; return qnorm(((m02 + m20) / s, (m12 + m21) / s, 0.25 * s, (m10 - m01) / s))

def qlook(yv, xhint):
    """Поворот, у которого ось Y идёт вдоль yv, а X — как можно ближе к xhint."""
    yv = vnorm(yv); xv = vsub(xhint, vmul(yv, vdot(xhint, yv)))
    if vlen(xv) < 1e-6: xv = vsub(X, vmul(yv, vdot(X, yv)))
    xv = vnorm(xv); zv = vcross(xv, yv)
    return qfrom_basis(xv, yv, zv)

def qangle(q):
    return math.degrees(2 * math.acos(min(1.0, abs(q[3]))))

def smooth(t): t = min(1.0, max(0.0, t)); return t * t * (3 - 2 * t)
def lerp(a, b, t): return a + (b - a) * t
def wrap(p): return p - math.floor(p)

def hermite(p0, m0, p1, m1, t):
    t2, t3 = t * t, t * t * t
    return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1

def curve(keys, t, loop=False):
    """Кривая по ключам [(t, value), ...] c гладкой (катмулл-ром) интерполяцией; loop — ключи на отрезке [0,1) по кругу."""
    n = len(keys)
    if loop:
        t = wrap(t)
        ks = [(k - 1.0, v) for k, v in keys[-2:]] + list(keys) + [(k + 1.0, v) for k, v in keys[:2]]
    else:
        if t <= keys[0][0]: return keys[0][1]
        if t >= keys[-1][0]: return keys[-1][1]
        ks = [(2 * keys[0][0] - keys[1][0], keys[0][1])] + list(keys) + [(2 * keys[-1][0] - keys[-2][0], keys[-1][1])]
    for i in range(1, len(ks) - 2):
        t0, v0 = ks[i]; t1, v1 = ks[i + 1]
        if t0 <= t <= t1:
            tp, vp = ks[i - 1]; tn, vn = ks[i + 2]
            h = t1 - t0
            m0 = (v1 - vp) / (t1 - tp) * h if not (not loop and i == 1) else (v1 - v0)
            m1 = (vn - v0) / (tn - t0) * h if not (not loop and i == len(ks) - 3) else (v1 - v0)
            if not loop and i == 1: m0 = 0.0
            if not loop and i == len(ks) - 3: m1 = 0.0
            return hermite(v0, m0, v1, m1, (t - t0) / h)
    return ks[-1][1]

# ============================================================ скелет
def b2u(v): return (-v[0], v[2], -v[1])     # оси Blender (Z вверх, персонаж смотрит в −Y) → оси Unity

class Skel:
    def __init__(self, data):
        self.names = [d[0] for d in data]
        self.parent = {d[0]: d[1] for d in data}
        self.rest = {d[0]: b2u(d[2]) for d in data}
        self.tail = {d[0]: b2u(d[3]) for d in data}
        self.children = {n: [] for n in self.names}
        for n in self.names:
            if self.parent[n]: self.children[self.parent[n]].append(n)
        self.order = []
        def walk(n):
            self.order.append(n)
            for c in self.children[n]: walk(c)
        for n in self.names:
            if not self.parent[n]: walk(n)
        self.thigh = vlen(vsub(self.rest["KneeL"], self.rest["HipL"]))
        self.shin = vlen(vsub(self.rest["FootL"], self.rest["KneeL"]))
        self.upper = vlen(vsub(self.rest["ElbowL"], self.rest["ShoulderL"]))
        self.fore = vlen(vsub(self.rest["HandL"], self.rest["ElbowL"]))

    def fk(self, pose, upto=None):
        """Мировые (в осях персонажа) положения и повороты костей для позы."""
        wp, wr = {}, {}
        for n in self.order:
            p = self.parent[n]; lr = pose.rot.get(n, QI)
            if p is None:
                wp[n] = vadd(self.rest[n], pose.root); wr[n] = lr
            else:
                wp[n] = vadd(wp[p], qrot(wr[p], vsub(self.rest[n], self.rest[p])))
                wr[n] = qmul(wr[p], lr)
            if upto and n == upto: break
        return wp, wr

class Pose:
    def __init__(self):
        self.rot = {}; self.root = (0.0, 0.0, 0.0)
    def copy(self):
        p = Pose(); p.rot = dict(self.rot); p.root = self.root; return p
    def set(self, bone, q): self.rot[bone] = q; return self
    def add(self, bone, q):   # доворот поверх уже заданного (в осях родителя)
        self.rot[bone] = qmul(q, self.rot.get(bone, QI)); return self
    def post(self, bone, q):  # доворот в осях самой кости
        self.rot[bone] = qmul(self.rot.get(bone, QI), q); return self

def blend(a, b, t, bones):
    p = Pose(); p.root = vlerp(a.root, b.root, t)
    for n in bones:
        qa, qb = a.rot.get(n, QI), b.rot.get(n, QI)
        if qa is QI and qb is QI: continue
        p.rot[n] = qslerp(qa, qb, t)
    return p

# ---------- обратная кинематика ноги и руки
def two_bone(root, target, la, lb, pole):
    """Колено/локоть: положение среднего сустава так, чтобы кончик попал в target, сгиб — в сторону pole."""
    d = vsub(target, root); dist = vlen(d)
    L = la + lb; k0 = 0.994 * L
    if dist > k0:   # мягкий предел: колено не щёлкает в прямую, а плавно доходит почти до конца
        dist_c = k0 + (0.9995 * L - k0) * (1 - math.exp(-(dist - k0) / (0.9995 * L - k0)))
    else:
        dist_c = max(dist, abs(la - lb) + 1e-4)
    dn = vnorm(d)
    a = (la * la + dist_c * dist_c - lb * lb) / (2 * la * dist_c)
    a = max(-1.0, min(1.0, a))
    h = la * math.sqrt(max(0.0, 1 - a * a))
    pd = vsub(pole, vmul(dn, vdot(pole, dn)))
    if vlen(pd) < 1e-6: pd = vsub(Z, vmul(dn, vdot(Z, dn)))
    pd = vnorm(pd)
    mid = vadd(vadd(root, vmul(dn, la * a)), vmul(pd, h))
    tip = vadd(root, vmul(dn, dist_c))
    return mid, tip, dist - dist_c

def leg_ik(sk, pose, sd, ankle, foot_q, pole=Z):
    """Ставит бедро и колено так, чтобы голеностоп попал в ankle; стопа — мировой поворот foot_q. Возвращает недостачу длины."""
    hip, knee, foot = "Hip" + sd, "Knee" + sd, "Foot" + sd
    wp, wr = sk.fk(pose)
    H = wp[hip]; par = wr["Hips"]
    la, lb = vlen(vsub(sk.rest[knee], sk.rest[hip])), vlen(vsub(sk.rest[foot], sk.rest[knee]))
    # полюс колена: вперёд по направлению стопы и чуть наружу
    K, A, short = two_bone(H, ankle, la, lb, pole)
    d1, d2 = vnorm(vsub(K, H)), vnorm(vsub(A, K))
    ax = vcross(d1, d2)
    if vlen(ax) < 1e-4: ax = vcross(pole, d1)
    # ось сгиба колена в покое — +X (сгиб колена = поворот вокруг +X); у бедра и голени X — ось сгиба, Y — вдоль кости вверх
    ax = vnorm(ax)
    r1 = vnorm(vsub(sk.rest[hip], sk.rest[knee])); r2 = vnorm(vsub(sk.rest[knee], sk.rest[foot]))
    W1 = qmul(qlook(vmul(d1, -1.0), ax), qconj(qlook(r1, X)))
    W2 = qmul(qlook(vmul(d2, -1.0), ax), qconj(qlook(r2, X)))
    pose.rot[hip] = qmul(qconj(par), W1)
    pose.rot[knee] = qmul(qconj(W1), W2)
    pose.rot[foot] = qmul(qconj(W2), foot_q)
    return short

def swing_twist(q, axis):
    """Раскладка поворота на скрутку вокруг оси axis и остальное (q = swing·twist)."""
    p = vmul(axis, vdot(q[:3], axis))
    tw = qnorm((p[0], p[1], p[2], q[3])) if (vdot(p, p) + q[3] * q[3]) > 1e-12 else QI
    return qmul(q, qconj(tw)), tw

def arm_ik(sk, pose, sd, wrist, hand_q=None, pole=None, twist_share=0.55):
    """Рука: плечо и локоть так, чтобы запястье попало в wrist; локоть — в сторону pole (по умолчанию назад-наружу)."""
    sh, el, ha = "Shoulder" + sd, "Elbow" + sd, "Hand" + sd
    s = -1.0 if sd == "L" else 1.0
    wp, wr = sk.fk(pose)
    S = wp[sh]; par = wr["Clavicle" + sd]
    la, lb = vlen(vsub(sk.rest[el], sk.rest[sh])), vlen(vsub(sk.rest[ha], sk.rest[el]))
    if pole is None: pole = vnorm((0.5 * s, -0.2, -1.0))
    E, W, short = two_bone(S, wrist, la, lb, pole)
    d1, d2 = vnorm(vsub(E, S)), vnorm(vsub(W, E))
    ax = vcross(d2, d1)                     # сгиб локтя вперёд = поворот вокруг −X, ось берём так, чтобы X совпал в покое
    if vlen(ax) < 1e-4: ax = vcross(d1, pole)
    ax = vnorm(ax)
    r1 = vnorm(vsub(sk.rest[sh], sk.rest[el])); r2 = vnorm(vsub(sk.rest[el], sk.rest[ha]))
    W1 = qmul(qlook(vmul(d1, -1.0), ax), qconj(qlook(r1, X)))
    W2 = qmul(qlook(vmul(d2, -1.0), ax), qconj(qlook(r2, X)))
    pose.rot[sh] = qmul(qconj(par), W1)
    pose.rot[el] = qmul(qconj(W1), W2)
    if hand_q is not None:
        # поворот кисти вокруг предплечья (пронация) делят предплечье и кисть — запястье не перекручивается
        rel = qmul(qconj(W2), hand_q)
        fa = vnorm(vsub(sk.rest[ha], sk.rest[el]))
        sw, tw = swing_twist(rel, fa)
        part = qslerp(QI, tw, twist_share)
        pose.rot[el] = qmul(pose.rot[el], part)
        pose.rot[ha] = qmul(qconj(part), rel)
    return short

# ============================================================ стопа
# точки подошвы относительно голеностопа в покое (оси персонажа): пятка, подушечка (сгиб носка), кончик
HEEL = (0.0, -0.100, -0.072)
BALL = (0.0, -0.100, 0.092)
TIP = (0.0, -0.100, 0.180)
TOEJ = (0.0, -0.072, 0.092)                 # сустав носка (голова кости Toe) относительно голеностопа

def foot_q(yaw, pitch, roll=0.0):
    """Мировой поворот стопы: yaw — куда смотрит носок, pitch > 0 — носок вверх, roll — наклон наружу/внутрь."""
    return qmul(qaxis(Y, yaw), qmul(qaxis(X, -pitch), qaxis(Z, roll)))

def stance_ankle(anchor, yaw, pitch, roll_toe=-90.0):
    """Голеностоп в опоре: anchor — точка земли под подушечкой (стопа стоит ровно). Носок вверх — поворот вокруг пятки,
    пятка вверх — вокруг подушечки (пальцы лежат на земле), дальше roll_toe — перекат через кончики пальцев.
    Возвращает (голеностоп, поворот носка относительно стопы)."""
    qy = qaxis(Y, yaw)
    if pitch >= 0:
        heel_g = vadd(anchor, qrot(qy, vsub(HEEL, BALL)))
        heel_g = (heel_g[0], 0.0, heel_g[2])
        return vadd(heel_g, qrot(foot_q(yaw, pitch), vmul(HEEL, -1.0))), QI
    if pitch >= roll_toe:
        return vadd(anchor, qrot(foot_q(yaw, pitch), vmul(BALL, -1.0))), qaxis(X, pitch)
    # перекат через кончики: сгиб носка застыл на roll_toe, вся стопа с носком поворачивается вокруг кончика
    tip_g = vadd(anchor, qrot(qy, vsub(TIP, BALL)))
    F = foot_q(yaw, pitch); tl = qaxis(X, roll_toe); TW = qmul(F, tl)
    ankle = vsub(vsub(tip_g, qrot(TW, vsub(TIP, TOEJ))), qrot(F, TOEJ))
    return ankle, tl

def toe_local(pitch_world_toe_target, foot_pitch):
    """Носок: поворот относительно стопы, чтобы мировой наклон носка был pitch_world_toe_target."""
    return qaxis(X, -(pitch_world_toe_target - foot_pitch))

# ============================================================ кисти
FINGERS4 = ("Index", "Middle", "Ring", "Pinky")

def hand_pose(pose, sd, curl=20.0, thumb=15.0, spread=0.0, wrist=(0.0, 0.0, 0.0), per=None, tip=None):
    """Пальцы: curl — сгиб каждого сустава (градусы; кулак ≈ 80), thumb — сгиб большого, spread — развести,
    wrist — сгиб кисти (вперёд-назад к ладони, к большому пальцу, скрутка), per — свой сгиб для пальца {"Index": 10}."""
    s = 1.0 if sd == "L" else -1.0                     # ладонь левой смотрит в +X: сгиб пальцев — вокруг +Z
    ax = (0.0, 0.0, s)
    k = (0.85, 1.0, 0.75)
    for i, f in enumerate(FINGERS4):
        c = (per or {}).get(f, curl)
        sp = spread * (1.5 - i) * 0.6
        for j in range(3):
            q = qaxis(ax, c * k[j])
            if j == 0 and sp: q = qmul(qaxis(X, -sp), q)
            pose.rot[f + str(j + 1) + sd] = q
    # большой палец: сгиб к ладони (назад и внутрь)
    tdir = (0.3 * s, -0.52, 0.8)
    tax = vnorm(vcross(vnorm(tdir), (0.0, 0.0, -1.0)))
    for j, kk in enumerate((0.6, 1.0, 0.8)):
        pose.rot["Thumb" + str(j + 1) + sd] = qaxis(tax, thumb * kk)
    fx, dev, tw = wrist
    # сгиб кисти к ладони — вокруг той же оси, что пальцы; к большому пальцу (вперёд) — вокруг X; скрутка — вокруг Y
    pose.rot["Hand" + sd] = qmul(qaxis(Y, tw * s), qmul(qaxis(X, -dev), qaxis(ax, fx)))

# ============================================================ походка
class Gait:
    """Цикл шага: левая пятка касается земли в фазе 0, правая — в 0.5. Всё задаётся в долях цикла.
    Персонаж стоит на месте (корень не едет), земля под опорной ногой уезжает назад со скоростью v."""
    def __init__(self, **k):
        d = dict(
            v=1.4, T=1.0, duty=0.6,                     # скорость, длительность цикла, доля опоры
            dir=0.0,                                    # направление движения, градусы (0 — вперёд, 90 — вправо, 180 — назад)
            yaw_off=0.0,                                # таз и стопы довёрнуты к движению (боковой ход), корпус — вперёд
            foot_yaw=None,                              # стопы довёрнуты сильнее таза (боковой ход): None — как таз
            width=0.085, toe_out=6.0,                   # полуширина следа, развод носков
            front=0.40,                                 # где подушечка стопы в момент касания (впереди таза, м)
            land=14.0, flat=0.16, heel_off=0.55, push=-55.0, roll_toe=-38.0,   # наклон стопы: касание, стопа ровно (доля опоры), пятка отрывается, толчок, перекат через пальцы
            lift=0.23, lift_at=0.28, clear=0.035, clear_at=0.62, swing_fwd=0.0,  # перенос: подъём пятки, просвет
            arc=0.012, swing_shape=(1.3, 1.6), swing_ends=(0.0, 0.0),                 # дуга переноса наружу; стопа после отрыва ещё уходит назад (m0) и перед касанием подтягивается (m1)
            drop=0.035, bob=0.018, bob_at=0.5,          # таз: просадка, качание вверх-вниз (0.5 — высоко в середине опоры)
            sway=0.022, yaw=4.5, roll=4.0, tilt=2.0, lean=2.0, tilt_bob=1.0,
            twist=0.9, spine_roll=0.8, head_pitch=0.0,  # корпус крутится навстречу тазу, выравнивает наклон
            arm=16.0, arm_base=-2.0, arm_lag=0.06, elbow=16.0, elbow_amp=14.0, arm_out=7.0, arm_cross=0.0,
            curl=22.0, thumb=14.0, clav=2.0, arm_twist=0.0,
            posture=1.0,                                # изгибы позвоночника и шеи (D-05)
        )
        d.update(k); self.__dict__.update(d)

    def swing_cdf(self, s):
        """Доля пути стопы по земле к моменту s переноса: профиль скорости s^a·(1−s)^b (a, b — swing_shape)."""
        tab = getattr(self, "_cdf", None)
        if tab is None:
            a, b = self.swing_shape
            n = 256; acc = [0.0]
            for i in range(n):
                x = (i + 0.5) / n
                acc.append(acc[-1] + (x ** a) * ((1 - x) ** b))
            tot = acc[-1]; tab = [x / tot for x in acc]; self._cdf = tab
        x = min(1.0, max(0.0, s)) * (len(tab) - 1); i = min(int(x), len(tab) - 2); f = x - i
        return tab[i] * (1 - f) + tab[i + 1] * f

    def dirv(self):
        a = math.radians(self.dir); return (math.sin(a), 0.0, math.cos(a))

    def foot(self, sd, u):
        """Нога в фазе u (своей): голеностоп, мировой поворот стопы, наклон носка, опора 0..1."""
        v, T, duty = self.v, self.T, self.duty
        dv = self.dirv(); s = -1.0 if sd == "L" else 1.0
        yaw = (self.yaw_off if self.foot_yaw is None else self.foot_yaw) + s * self.toe_out
        right = qrot(qaxis(Y, self.yaw_off), X)
        lat = vmul(right, s * self.width)
        stride = v * T
        # опора: точка под подушечкой, в середине опоры — чуть впереди таза
        def anchor(us):
            along = self.front - stride * us
            return vadd(lat, vmul(dv, along))
        def st_pitch(us):
            f = us / duty
            if f < self.flat: return self.land * (1 - smooth(f / self.flat))
            if f < self.heel_off: return 0.0
            g = (f - self.heel_off) / (1 - self.heel_off)
            return self.push * (g ** 1.6)
        if u < duty:
            p = st_pitch(u)
            A, toe = stance_ankle(anchor(u), yaw, p, self.roll_toe)
            return A, foot_q(yaw, p), toe, 1.0, p
        # перенос
        sw = (u - duty) / (1 - duty)
        P0, toe0 = stance_ankle(anchor(duty), yaw, self.push, self.roll_toe)
        P1, _ = stance_ankle(anchor(0.0), yaw, self.land, self.roll_toe)
        e = (u - duty) * T                               # секунды переноса
        Tsw = (1 - duty) * T
        # вдоль движения: скорость стопы относительно земли — плавный горб от нуля (отрыв) до нуля (касание),
        # пик чуть позже середины переноса. Стопа отрывается и ставится без проскальзывания
        # на быстром беге стопа отрывается и ставится с остаточной скоростью (swing_ends — доли v), иначе вынос вперёд огромный
        a0, a1 = vdot(P0, dv), vdot(P1, dv)
        ks, ke = self.swing_ends
        B = (a1 - a0) / Tsw + v * (1 - 0.5 * (ks + ke))
        along = a0 + Tsw * (v * (ks * (sw - 0.5 * sw * sw) + ke * 0.5 * sw * sw) + B * self.swing_cdf(sw)) - v * Tsw * sw
        # вбок — по прямой с лёгкой дугой наружу
        side0, side1 = vdot(P0, right), vdot(P1, right)
        side = lerp(side0, side1, smooth(sw)) + s * self.arc * math.sin(math.pi * sw)
        # высота голеностопа: пятка уходит вверх, потом нога проходит низко и опускается
        h = curve([(0.0, P0[1]), (self.lift_at, P0[1] * 0.3 + self.lift), (self.clear_at, 0.100 + self.clear), (1.0, P1[1])], sw)
        A = vadd(vadd(vmul(dv, along), vmul(right, side)), (0.0, h, 0.0))
        # убираем составляющую dv вдоль right (если движение не вперёд) — она уже в along
        A = vsub(A, vmul(right, vdot(vmul(dv, along), right)))
        pitch = curve([(0.0, self.push), (0.3, self.push * 0.35), (0.62, -2.0), (0.88, self.land * 0.8), (1.0, self.land)], sw)
        toe = qslerp(toe0, QI, smooth(sw / 0.35))
        return A, foot_q(yaw, pitch), toe, 0.0, pitch

    def prepare(self, sk, n=72):
        """Где опорная нога не дотягивается — таз опускается (плавно по всему циклу)."""
        self._drop = None
        L = (sk.thigh + sk.shin) * 0.9955
        need = []
        for i in range(n):
            pose, c = self.sample(sk, i / n, ik=False)
            wp, wr = sk.fk(pose)
            d = 0.0
            for sd, off in (("L", 0.0), ("R", 0.5)):
                u = wrap(i / n - off)
                if self.duty + 0.04 < u < 0.93: continue         # нога в переносе таз не держит
                A, fq, toe, cc, pitch = self.foot(sd, u)
                H = wp["Hip" + sd]
                hz = vlen((H[0] - A[0], 0.0, H[2] - A[2]))
                if hz < L:
                    d = max(d, H[1] - (A[1] + math.sqrt(L * L - hz * hz)))
                else:
                    d = max(d, H[1] - A[1])
            need.append(max(0.0, d))
        # расширяем и сглаживаем по кругу (таз не дёргается)
        w = max(2, n // 24)
        dil = [max(need[(i + k) % n] for k in range(-w, w + 1)) for i in range(n)]
        sm = []
        for i in range(n):
            acc = tot = 0.0
            for k in range(-w, w + 1):
                g = math.exp(-(k / (0.6 * w)) ** 2); acc += dil[(i + k) % n] * g; tot += g
            sm.append(max(acc / tot, need[i]))
        self._drop = sm
        return max(need)

    def extra_drop(self, phi):
        d = getattr(self, "_drop", None)
        if not d: return 0.0
        n = len(d); x = wrap(phi) * n; i = int(x) % n; f = x - int(x)
        return d[i] * (1 - f) + d[(i + 1) % n] * f

    def sample(self, sk, phi, ik=True):
        pose = Pose()
        g = self; tau = 2 * math.pi
        # ---- таз
        # середина опоры левой ноги — в фазе duty/2·…: высоко при bob_at=0.5 (ходьба), низко при bob_at=0 (бег)
        mid = g.duty * 0.5
        yb = -g.drop + g.bob * math.cos(2 * tau * (phi - mid) + (0.0 if g.bob_at >= 0.5 else math.pi))
        xs = -g.sway * math.cos(tau * (phi - mid))            # к опорной ноге (левая — −X)
        pose.root = qrot(qaxis(Y, g.yaw_off), (xs, yb - self.extra_drop(phi), 0.0))
        pyaw = g.yaw * math.cos(tau * phi)                    # левое бедро вперёд при касании левой пяткой
        proll = -g.roll * math.cos(tau * (phi - mid * 0.7))   # таз опускается на стороне ноги в переносе
        ptilt = g.lean + g.tilt * 0.3 + g.tilt_bob * math.cos(2 * tau * (phi - mid))
        hips_q = qmul(qaxis(Y, g.yaw_off + pyaw), qmul(qaxis(X, ptilt), qaxis(Z, proll)))
        pose.rot["Hips"] = hips_q
        # ---- корпус: наклон, скрутка навстречу тазу, выравнивание
        tw = -(g.yaw_off + pyaw * (1 + g.twist)) / 3.0
        rl = -proll * g.spine_roll / 3.0
        lean_sp = -g.tilt_bob * math.cos(2 * tau * (phi - mid)) / 3.0
        for i, b in enumerate(("Spine", "Spine2", "Torso")):
            pose.rot[b] = qmul(qaxis(Y, tw), qmul(qaxis(X, lean_sp + (g.lean * 0.3 if i == 2 else 0.0)), qaxis(Z, rl)))
        posture_spine(pose, g.posture)
        # ---- ноги
        contacts = {}
        if not ik: return pose, contacts
        for sd, off in (("L", 0.0), ("R", 0.5)):
            u = wrap(phi - off)
            A, fq, toe, c, pitch = self.foot(sd, u)
            fy = g.yaw_off if g.foot_yaw is None else 0.5 * (g.yaw_off + g.foot_yaw)
            leg_ik(sk, pose, sd, A, fq, pole=qrot(qaxis(Y, fy), Z))
            pose.rot["Toe" + sd] = toe
            contacts[sd] = c
        # ---- руки: в противофазе ногам, локоть запаздывает
        for sd, off, s in (("L", 0.5, -1.0), ("R", 0.0, 1.0)):
            ph = tau * (phi - off - g.arm_lag)
            fwd = math.cos(ph)                                  # 1 — рука впереди
            sh = g.arm_base - g.arm * fwd                       # < 0 — вперёд (минус по X в Unity)
            el = g.elbow + g.elbow_amp * (0.5 + 0.5 * math.cos(ph - 0.5))
            q = qmul(qaxis(Z, s * (g.arm_out - g.arm_cross * max(0.0, fwd))), qaxis(X, sh))
            pose.rot["Shoulder" + sd] = q
            pose.rot["Elbow" + sd] = qaxis(X, -el)
            pose.rot["Clavicle" + sd] = qaxis(Y, s * g.clav * fwd * -1.0)
            hand_pose(pose, sd, g.curl, g.thumb, wrist=(8.0, 4.0, 0.0))
        posture_shoulders(pose, g.posture)
        # ---- голова: держит взгляд вперёд
        wp, wr = sk.fk(pose)
        tq = wr["Torso"]
        want = qaxis(X, g.head_pitch)
        rel = qmul(qconj(tq), want)
        pose.rot["Neck"] = qslerp(QI, rel, 0.5); pose.rot["Head"] = qslerp(QI, rel, 0.5)
        posture_neck(pose, g.posture)
        return pose, contacts

# ============================================================ Blender: просмотр клипов на модели
def u2b_q(q):
    """Поворот в осях Unity → те же оси в Blender (u = M·b, M: x→−x, y→z, z→−y)."""
    import mathutils
    x, y, z, w = q
    Ru = mathutils.Quaternion((w, x, y, z)).to_matrix()
    M = mathutils.Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
    return (M.transposed() @ Ru @ M)

def apply_to_blender(arm, pose, frame=None):
    """Поза из осей Unity на арматуру Blender (пересчёт в оси каждой кости); frame — поставить ключ."""
    import mathutils
    M = mathutils.Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))
    for pb in arm.pose.bones:
        b = pb.bone
        q = pose.rot.get(b.name, QI)
        Lb = u2b_q(q)
        R = b.matrix_local.to_3x3()
        basis = (R.inverted() @ Lb @ R).to_quaternion()
        pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = basis
        if b.parent is None:
            off = M.transposed() @ mathutils.Vector(pose.root)
            pb.location = R.inverted() @ off
        else:
            pb.location = (0, 0, 0)
        if frame is not None:
            pb.keyframe_insert("rotation_quaternion", frame=frame, group=b.name)
            if b.parent is None: pb.keyframe_insert("location", frame=frame, group=b.name)

def bake_action(arm, name, poses, fps=30, loop=True, root_motion=None):
    """Клип как действие Blender (ключ в каждом кадре). root_motion — скорость (м/с), чтобы персонаж ехал вперёд при просмотре."""
    import bpy
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    arm.animation_data_create()
    act = bpy.data.actions.new(name); arm.animation_data.action = act
    for i, p in enumerate(poses):
        apply_to_blender(arm, p, frame=i + 1)
    act.use_fake_user = True
    try:
        act.use_frame_range = True; act.frame_start = 1; act.frame_end = len(poses) + (0 if loop else 0)
    except Exception: pass
    return act

# ============================================================ библиотека клипов
FPS = 30

# руки при ходьбе (D-04): позади локоть почти прямой, впереди сгиб ~35°; плечевой пояс чуть ходит за рукой
WALK = dict(v=1.4, T=0.86, duty=0.6, lift=0.18, lift_at=0.3, arm_out=1.5, front=0.36, drop=0.017, bob=0.012, land=12, swing_shape=(1.0, 1.4),
            arm=19.0, arm_base=1.0, elbow=9.0, elbow_amp=26.0, arm_lag=0.07, clav=3.5, yaw=5.0, twist=1.1, heel_off=0.5)
RUN = dict(v=3.8, T=0.667, duty=0.32, front=0.34, drop=0.05, bob=0.03, bob_at=0.0, land=5, flat=0.2, heel_off=0.45, push=-60, roll_toe=-40,
           lift=0.32, lift_at=0.33, clear=0.14, clear_at=0.62, lean=8, yaw=7, roll=5, sway=0.015, width=0.07,
           swing_shape=(0.7, 1.1), swing_ends=(0.3, 0.1),
           arm=35, arm_base=-12, elbow=80, elbow_amp=20, arm_out=4, arm_cross=8, curl=45, thumb=25, clav=4)
# трусца между шагом и бегом (2.4 м/с): уже есть полёт, руки согнуты наполовину
JOG = dict(v=2.4, T=0.75, duty=0.42, front=0.32, drop=0.035, bob=0.018, bob_at=0.0, land=8, flat=0.2, heel_off=0.5, push=-58, roll_toe=-40,
           lift=0.26, lift_at=0.32, clear=0.08, clear_at=0.62, lean=5, yaw=6, roll=4.5, sway=0.018, width=0.075,
           swing_shape=(0.85, 1.25), swing_ends=(0.15, 0.05), arm=25, arm_base=-8, elbow=62, elbow_amp=18, arm_out=2, arm_cross=5, curl=35, thumb=20, clav=3)
SPRINT = dict(v=6.5, T=0.6, duty=0.2, front=0.26, drop=0.05, bob=0.03, bob_at=0.0, land=-10, flat=0.25, heel_off=0.35, push=-60, roll_toe=-40,
              swing_shape=(0.7, 1.1), swing_ends=(0.6, 0.6), lift=0.45, lift_at=0.35, clear=0.22, clear_at=0.62, lean=13, yaw=8, roll=5, sway=0.012, width=0.06,
              arm=52, arm_base=-15, elbow=88, elbow_amp=20, arm_out=5, arm_cross=10, curl=40, thumb=20, clav=6)
# назад: носок касается первым, стопа уходит с пятки; вбок — таз и стопы довёрнуты к движению, грудь смотрит вперёд
WALK_B = dict(WALK, v=1.1, dir=180.0, land=-16, flat=0.2, heel_off=0.6, push=14, front=0.16, lift=0.12, lift_at=0.35, clear=0.05, clear_at=0.55,
              yaw=3.0, arm=10.0)
RUN_B = dict(RUN, v=2.6, T=0.62, duty=0.36, dir=180.0, land=-22, flat=0.25, heel_off=0.7, push=10, roll_toe=-60, front=0.12, lift=0.22, lift_at=0.4,
             clear=0.1, clear_at=0.6, lean=2, arm=22, swing_shape=(0.8, 1.2), swing_ends=(0.2, 0.2))
def STRAFE(base, side, yaw_off=35.0, **k):
    d = dict(base, dir=90.0 * side, yaw_off=yaw_off * side, width=0.1, yaw=base.get("yaw", 4.5) * 0.5)
    d.update(k); return d

def cycle(g, sk, n):
    g.prepare(sk)
    return [g.sample(sk, i / n) for i in range(n)]

# ---------- позы без шага: стоя, сидя
def stand_feet(pose, sk, lx=-0.105, rx=0.105, lz=0.0, rz=0.02, toe_out=8.0, lift=None):
    lift = lift or {}
    for sd, x, z in (("L", lx, lz), ("R", rx, rz)):
        s = -1.0 if sd == "L" else 1.0
        h = lift.get(sd, 0.0)
        A = (x, 0.100 + h, z)
        leg_ik(sk, pose, sd, A, foot_q(s * toe_out, 0.0), pole=qrot(qaxis(Y, s * toe_out * 0.6), Z))
        pose.rot["Toe" + sd] = QI

def relaxed_arms(pose, t=0.0, breath=0.0, sway=0.0):
    for sd, s in (("L", -1.0), ("R", 1.0)):
        pose.rot["Clavicle" + sd] = qaxis(Z, s * 1.5 * breath)
        pose.rot["Shoulder" + sd] = qmul(qaxis(Z, s * (1.0 + 0.8 * breath)), qaxis(X, -4.0 + sway * s))
        pose.rot["Elbow" + sd] = qaxis(X, -(13.0 + 1.5 * breath))
        hand_pose(pose, sd, 24.0, 16.0, spread=2.0, wrist=(10.0, 4.0, 6.0))

# ---------- осанка (доводка 0.9, D-05): позвоночник не доска — поясница прогнута, грудной отдел чуть скруглён,
# шея наклонена вперёд, голова держит взгляд ровно; плечи опущены и чуть вперёд
POSTURE = dict(pelvis=5.0, lumbar=-6.0, mid=-1.0, thor=5.0, neck=11.0, clav_down=3.0, clav_fwd=4.0)

def posture_spine(pose, k=1.0):
    """Добавить изгибы позвоночника к уже выставленным поворотам таза и корпуса (до IK ног)."""
    P = POSTURE
    pose.rot["Hips"] = qmul(pose.rot.get("Hips", QI), qaxis(X, P["pelvis"] * k))
    for b, a in (("Spine", P["lumbar"]), ("Spine2", P["mid"]), ("Torso", P["thor"])):
        pose.rot[b] = qmul(pose.rot.get(b, QI), qaxis(X, a * k))

def posture_neck(pose, k=1.0):
    """После head_look: шея вперёд, голова назад на столько же — взгляд не меняется."""
    a = POSTURE["neck"] * k
    pose.rot["Neck"] = qmul(pose.rot.get("Neck", QI), qaxis(X, a))
    pose.rot["Head"] = qmul(qaxis(X, -a), pose.rot.get("Head", QI))

def posture_shoulders(pose, k=1.0):
    for sd, s in (("L", -1.0), ("R", 1.0)):
        # правая ключица (+X): вперёд — поворот вокруг Y на минус, вниз — вокруг Z на минус; левая — наоборот
        pose.rot["Clavicle" + sd] = qmul(pose.rot.get("Clavicle" + sd, QI), qmul(qaxis(Y, -s * POSTURE["clav_fwd"] * k), qaxis(Z, -s * POSTURE["clav_down"] * k)))

def head_look(pose, sk, yaw=0.0, pitch=0.0, roll=0.0, neck_share=0.45):
    """Голова смотрит в заданную сторону (в осях персонажа), что бы ни делал корпус."""
    wp, wr = sk.fk(pose)
    want = qmul(qaxis(Y, yaw), qmul(qaxis(X, pitch), qaxis(Z, roll)))
    rel = qmul(qconj(wr["Torso"]), want)
    pose.rot["Neck"] = qslerp(QI, rel, neck_share); pose.rot["Head"] = qslerp(QI, rel, 1 - neck_share)
    # после шеи голове остаётся остаток: пересчитываем точно
    wp, wr = sk.fk(pose)
    pose.rot["Head"] = qmul(qconj(wr["Neck"]), want)

def idle_pose(sk, t, L=8.0):
    """Стоит: дыхание (2 вдоха за цикл), перенос веса с ноги на ногу, лёгкие движения головы."""
    tau = 2 * math.pi
    pose = Pose()
    br = 0.5 - 0.5 * math.cos(tau * 2 * t / L)                 # 0 — выдох, 1 — вдох
    w = math.sin(tau * t / L)                                   # перенос веса: −1 на левую, +1 на правую
    pose.root = (0.028 * w, -0.012 - 0.006 * abs(w) + 0.002 * br, 0.0)
    pose.rot["Hips"] = qmul(qaxis(Y, 2.0 * w), qmul(qaxis(X, 1.5), qaxis(Z, -2.5 * w)))
    for i, b in enumerate(("Spine", "Spine2", "Torso")):
        pose.rot[b] = qmul(qaxis(Y, -0.8 * w), qmul(qaxis(X, -0.9 * br + (0.6 if i == 0 else 0.0)), qaxis(Z, 1.0 * w)))
    posture_spine(pose)
    stand_feet(pose, sk)
    relaxed_arms(pose, t, br, sway=0.8 * math.sin(tau * t / L + 1.0))
    posture_shoulders(pose)
    head_look(pose, sk, yaw=3.0 * math.sin(tau * t / L * 1.0 + 0.7), pitch=2.0 + 1.5 * math.sin(tau * 2 * t / L + 2.0) - 1.0 * br)
    posture_neck(pose)
    return pose

SEAT_DROP = 0.31
def sit_pose(sk, s, t=0.0):
    """Посадка: s = 0 стоит, 1 сидит (таз на 0.31 ниже, бёдра вперёд). По пути корпус наклоняется вперёд, руки идут вперёд."""
    tau = 2 * math.pi
    e = smooth(s)
    pose = Pose()
    bend = math.sin(math.pi * min(1.0, s * 1.1)) * (1 - 0.3 * s)      # наклон вперёд в середине движения
    br = 0.5 - 0.5 * math.cos(tau * t / 4.0)
    pose.root = (0.0, -SEAT_DROP * e + 0.002 * br * e, -0.06 * bend)
    pose.rot["Hips"] = qaxis(X, 26.0 * bend - 4.0 * e)
    for b in ("Spine", "Spine2", "Torso"):
        pose.rot[b] = qaxis(X, 4.0 * bend + 2.0 * e - 0.8 * br)
    # ступни: встают чуть впереди, пока садится (небольшой подшаг)
    fz = 0.36 * smooth((s - 0.15) / 0.7)
    lift = {"L": 0.04 * math.sin(math.pi * min(1, max(0, (s - 0.15) / 0.35))), "R": 0.04 * math.sin(math.pi * min(1, max(0, (s - 0.45) / 0.35)))}
    stand_feet(pose, sk, lx=-0.12, rx=0.12, lz=fz, rz=fz + 0.02 * (1 - e), toe_out=8.0 + 4 * e, lift=lift)
    relaxed_arms(pose, t, br)
    for sd, s2 in (("L", -1.0), ("R", 1.0)):
        pose.add("Shoulder" + sd, qaxis(X, -30.0 * bend - 22.0 * e))
        pose.rot["Elbow" + sd] = qaxis(X, -(13.0 + 30.0 * e + 20 * bend))
    head_look(pose, sk, pitch=6.0 * e + 10.0 * bend)
    return pose

def seated_typing(sk, t, typing=1.0, L=5.0):
    """Сидит за клавиатурой: корпус подан вперёд, запястья над клавишами (IK), ладони вниз, пальцы стучат, взгляд — в монитор.
    Клавиатура у коллег — в 0.75 м перед центром сиденья на высоте 0.81 (замер в игре)."""
    pose = sit_pose(sk, 1.0, t)
    tau = 2 * math.pi
    br = 0.5 - 0.5 * math.cos(tau * t / L)
    pose.rot["Hips"] = qaxis(X, 6.0)
    for b in ("Spine", "Spine2", "Torso"):
        pose.rot[b] = qaxis(X, 5.0 + 0.6 * math.sin(tau * t / L) - 0.6 * br)
    for sd, s in (("L", -1.0), ("R", 1.0)):
        pose.rot["Clavicle" + sd] = qmul(qaxis(Y, -s * 10.0), qaxis(Z, s * (2.0 + 1.0 * br)))
    for sd, s in (("L", -1.0), ("R", 1.0)):
        k = math.sin(tau * t / L * 7 + (0 if sd == "L" else 1.7))
        wrist = (s * 0.14 + 0.012 * k * typing, 0.855 + 0.005 * max(0.0, k) * typing, 0.60 + 0.012 * k * typing)
        # ладонь вниз, пальцы вперёд, кисти чуть развёрнуты внутрь
        hand_q = qmul(qaxis(Y, -s * 14.0), qmul(qaxis(Z, s * 90.0), qaxis(X, -84.0)))
        arm_ik(sk, pose, sd, wrist, hand_q, pole=vnorm((s * 1.0, -1.0, -0.2)))
        ax = (0.0, 0.0, 1.0 if sd == "L" else -1.0)
        for i, f in enumerate(FINGERS4):
            ph = t * (12 + 2 * i) / L * tau + i * 1.9 + (0 if sd == "L" else 0.8)
            tap = max(0.0, math.sin(ph)) ** 3
            c = 22.0 + 30.0 * tap * typing
            for j, kk in enumerate((0.8, 1.0, 0.7)):
                pose.rot[f + str(j + 1) + sd] = qaxis(ax, c * kk)
        tdir = (0.3 * (1.0 if sd == "L" else -1.0), -0.52, 0.8)
        tax = vnorm(vcross(vnorm(tdir), (0.0, 0.0, -1.0)))
        for j, kk in enumerate((0.5, 0.8, 0.6)):
            pose.rot["Thumb" + str(j + 1) + sd] = qaxis(tax, 18.0 * kk)
    head_look(pose, sk, pitch=6.0 + 1.5 * math.sin(tau * t / L * 2), yaw=2.0 * math.sin(tau * t / L))
    return pose

# ---------- действия верхней частью тела (в игре накладываются поверх ног)
def base_stand(sk, t=0.0):
    return idle_pose(sk, t)

def keyed(keys, t):
    """keys: [(t, value)], ровная интерполяция без выхода за значения (для поз по ключам)."""
    if t <= keys[0][0]: return keys[0][1]
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t <= t1:
            return v0 + (v1 - v0) * smooth((t - t0) / (t1 - t0))
    return keys[-1][1]

def wave_pose(sk, t, L=1.8):
    """Машет правой рукой: поднял (0.3 с), три взмаха кистью с предплечьем, опустил."""
    pose = base_stand(sk, t)
    up = keyed([(0.0, 0.0), (0.3, 1.0), (L - 0.35, 1.0), (L, 0.0)], t)
    wv = math.sin(2 * math.pi * (t - 0.3) / 0.42) * up
    for b in ("Spine2", "Torso"): pose.add(b, qaxis(Z, 2.0 * up))
    pose.rot["ClavicleR"] = qmul(qaxis(Z, 14.0 * up), qaxis(Y, -4.0 * up))
    rest = Pose(); rest.rot = dict(pose.rot); rest.root = pose.root
    wp, wr = sk.fk(pose)
    target = vadd(wp["ShoulderR"], qrot(wr["Torso"], (0.17 + 0.08 * wv, 0.36 - 0.02 * abs(wv), 0.10)))
    palm = qmul(wr["Torso"], qmul(qaxis(Z, 8.0 * wv), qmul(qaxis(Y, -90.0), qaxis(X, 180.0))))
    arm_ik(sk, pose, "R", target, palm, pole=vnorm((1.0, -0.9, 0.1)))
    for b in ("ShoulderR", "ElbowR", "HandR"): pose.rot[b] = qslerp(rest.rot.get(b, QI), pose.rot[b], up)
    hand_pose(pose, "R", curl=6.0 * up + 24 * (1 - up), thumb=4.0, spread=12.0 * up)
    pose.rot["HandR"] = qslerp(rest.rot.get("HandR", QI), qmul(qconj(sk.fk(pose)[1]["ElbowR"]), palm), up)
    head_look(pose, sk, yaw=-4.0 * up, pitch=-3.0 * up, roll=4.0 * up)
    return pose

def swing_pose(sk, t, L=0.5):
    """Удар правой рукой сверху вниз с разворотом корпуса: замах 0.14 с, удар 0.12 с, возврат."""
    pose = base_stand(sk, 0.0)
    k_up = keyed([(0.0, 0.0), (0.14, 1.0), (0.26, 0.0)], t)          # замах
    k_hit = keyed([(0.14, 0.0), (0.24, 1.0), (0.34, 0.9), (L, 0.0)], t)   # удар и проводка
    twist = 16.0 * k_up - 22.0 * k_hit
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, twist / 3.0), qaxis(X, -2.0 * k_up + 5.0 * k_hit))
    pose.rot["Hips"] = qmul(qaxis(Y, twist * 0.3), qaxis(X, 3.0 * k_hit))
    stand_feet(pose, sk, lz=0.12, rz=-0.08)
    up = qmul(qaxis(Z, 60.0), qaxis(X, -150.0))
    hit = qmul(qaxis(Z, 10.0), qaxis(X, -25.0))
    rest = pose.rot["ShoulderR"]
    q = qslerp(rest, up, k_up); q = qslerp(q, hit, k_hit)
    pose.rot["ShoulderR"] = q
    pose.rot["ElbowR"] = qaxis(X, -(15.0 + 85.0 * k_up - 55.0 * k_up * k_hit + 10.0 * k_hit))
    pose.rot["ClavicleR"] = qaxis(Z, 10.0 * k_up)
    hand_pose(pose, "R", curl=78.0, thumb=45.0, wrist=(-20.0 * k_up + 25.0 * k_hit, 10.0, 0.0))
    pose.add("ShoulderL", qaxis(X, -25.0 * k_hit + 10.0 * k_up)); pose.rot["ElbowL"] = qaxis(X, -(25.0 + 30.0 * k_up))
    hand_pose(pose, "L", curl=45.0, thumb=25.0)
    head_look(pose, sk, pitch=6.0 * k_hit)
    return pose

def throw_pose(sk, t, L=0.7):
    """Бросок сверху правой рукой: замах — кисть уходит за плечо (0.28 с), бросок вперёд-вверх (0.1 с), проводка к левому бедру.
    Кисть идёт по ключам (IK), корпус раскручивается от таза к груди, левая рука сначала указывает на цель."""
    pose = base_stand(sk, 0.0)
    kb = keyed([(0.0, 0.0), (0.28, 1.0), (0.34, 0.85), (0.4, 0.0)], t)       # замах
    kf = keyed([(0.28, 0.0), (0.38, 1.0), (0.5, 1.0), (L, 0.0)], t)          # бросок и проводка
    twist = 30.0 * kb - 28.0 * kf
    pose.rot["Hips"] = qmul(qaxis(Y, twist * 0.4), qaxis(X, 2.0 + 5.0 * kf))
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, twist * 0.2), qaxis(X, -3.0 * kb + 6.0 * kf))
    stand_feet(pose, sk, lz=0.16, rz=-0.1)
    wp, wr = sk.fk(pose)
    chest = wr["Torso"]
    # траектория кисти в осях груди
    keys_x = [(0.0, 0.10), (0.28, 0.22), (0.38, 0.06), (0.5, -0.12), (L, 0.10)]
    keys_y = [(0.0, -0.55), (0.28, 0.22), (0.38, 0.18), (0.5, -0.35), (L, -0.55)]
    keys_z = [(0.0, 0.10), (0.28, -0.22), (0.38, 0.45), (0.5, 0.35), (L, 0.10)]
    loc = (curve(keys_x, t), curve(keys_y, t), curve(keys_z, t))
    wrist = vadd(wp["Torso"], qrot(chest, vadd(loc, (0.0, 0.1, 0.0))))
    palm = qmul(chest, qmul(qaxis(X, -60.0 * kb - 110.0 * kf * (1 - kb)), qaxis(Z, 90.0)))
    rest = dict(pose.rot)
    arm_ik(sk, pose, "R", wrist, palm, pole=vnorm((1.0, -0.4 + 0.8 * kb, -0.6)))
    w = max(kb, kf, smooth(min(1.0, t / 0.08)) * smooth(min(1.0, (L - t) / 0.12)))
    for b in ("ShoulderR", "ElbowR", "HandR"): pose.rot[b] = qslerp(rest.get(b, QI), pose.rot[b], w)
    hand_pose(pose, "R", curl=65.0 * (1 - kf) + 12.0 * kf, thumb=40.0 * (1 - kf), spread=8.0 * kf)
    pose.rot["HandR"] = qslerp(rest.get("HandR", QI), qmul(qconj(sk.fk(pose)[1]["ElbowR"]), palm), w)
    # левая рука: указывает вперёд на замахе, потом прижимается
    pose.rot["ShoulderL"] = qslerp(pose.rot["ShoulderL"], qmul(qaxis(Z, -25.0), qaxis(X, -80.0)), kb * 0.9)
    pose.rot["ElbowL"] = qaxis(X, -(15.0 + 50.0 * kf))
    head_look(pose, sk, yaw=-twist * 0.6, pitch=-4.0 * kb + 3.0 * kf)
    return pose

def aim_pose(sk, t=0.0, two=False, pitch=0.0):
    """Прицел: одна рука — правая вытянута к цели; двумя — приклад у плеча, левая держит цевьё. pitch — вниз +."""
    pose = base_stand(sk, t)
    tw = 18.0 if two else 8.0
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, tw / 3.0), qaxis(X, 1.0 + pitch / 6.0))
    pose.rot["Hips"] = qmul(qaxis(Y, tw * 0.4), qaxis(X, 2.0))
    stand_feet(pose, sk, lz=0.10, rz=-0.06, toe_out=12.0)
    wp, wr = sk.fk(pose)
    chest = wr["Torso"]
    if two:
        # в осях груди: правая кисть у плеча, левая впереди
        rw = vadd(wp["Torso"], qrot(chest, (0.10, 0.09, 0.26)))
        lw = vadd(wp["Torso"], qrot(chest, (-0.02, 0.07, 0.52)))
        arm_ik(sk, pose, "R", rw, qmul(chest, qmul(qaxis(Y, -15.0), qmul(qaxis(Z, 90.0), qaxis(X, -85.0)))), pole=vnorm((1.0, -0.5, -0.2)))
        arm_ik(sk, pose, "L", lw, qmul(chest, qmul(qaxis(Y, 20.0), qmul(qaxis(Z, -90.0), qaxis(X, -80.0)))), pole=vnorm((-0.6, -1.0, 0.0)))
        keep = pose.rot["HandL"]; hand_pose(pose, "L", curl=55.0, thumb=30.0); pose.rot["HandL"] = keep
    else:
        rw = vadd(wp["ShoulderR"], qrot(chest, (-0.06, -0.02, 0.52)))
        arm_ik(sk, pose, "R", rw, qmul(chest, qmul(qaxis(Z, 90.0), qaxis(X, -88.0))), pole=vnorm((1.0, -1.0, -0.3)))
    keep = pose.rot["HandR"]; hand_pose(pose, "R", curl=62.0, thumb=35.0, per={"Index": 25.0}); pose.rot["HandR"] = keep
    head_look(pose, sk, pitch=pitch + (4.0 if two else 0.0), roll=(6.0 if two else 0.0))
    return pose

def hold_pose(sk, t=0.0):
    """Держит предмет в правой руке (нож): локоть согнут, кисть перед собой."""
    pose = base_stand(sk, t)
    pose.rot["ShoulderR"] = qmul(qaxis(Z, 12.0), qaxis(X, -20.0))
    pose.rot["ElbowR"] = qaxis(X, -62.0)
    hand_pose(pose, "R", curl=75.0, thumb=45.0, wrist=(-10.0, 15.0, 30.0))
    return pose

def hit_pose(sk, t, L=0.55):
    """Попадание: корпус дёргается назад, голова запрокидывается, руки вздрагивают, потом приходит в себя."""
    pose = base_stand(sk, 0.0)
    k = keyed([(0.0, 0.0), (0.07, 1.0), (0.2, 0.8), (L, 0.0)], t)
    pose.root = vadd(pose.root, (0.0, -0.03 * k, -0.04 * k))
    pose.rot["Hips"] = qmul(pose.rot["Hips"], qaxis(X, -6.0 * k))
    for b in ("Spine", "Spine2", "Torso"): pose.add(b, qmul(qaxis(X, -5.0 * k), qaxis(Y, 4.0 * k)))
    stand_feet(pose, sk)
    for sd, s in (("L", -1.0), ("R", 1.0)):
        pose.add("Shoulder" + sd, qmul(qaxis(Z, s * 22.0 * k), qaxis(X, -25.0 * k)))
        pose.rot["Elbow" + sd] = qaxis(X, -(13.0 + 45.0 * k))
        hand_pose(pose, sd, curl=24.0 + 20 * k, thumb=16.0, spread=10.0 * k)
    head_look(pose, sk, pitch=-14.0 * k, yaw=8.0 * k)
    return pose

def air_pose(sk, rise):
    """В воздухе: rise = 1 — толчок вверх (колени подтянуты, руки вверх), 0 — падение (ноги вниз к приземлению)."""
    pose = Pose()
    pose.root = (0.0, 0.02, 0.0)
    pose.rot["Hips"] = qaxis(X, 4.0)
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qaxis(X, 2.0 - 2.0 * rise)
    for sd, s, a in (("L", -1.0, 1.0), ("R", 1.0, 0.6)):
        knee_up = 0.10 + 0.14 * rise * a
        A = (s * 0.11, 0.9 - 0.62 + knee_up + 0.05, 0.06 + 0.10 * rise * a)
        leg_ik(sk, pose, sd, vadd(A, (0, 0.0, 0.0)), foot_q(s * 6.0, -25.0 + 10 * rise), pole=Z)
        pose.rot["Toe" + sd] = qaxis(X, 10.0)
        pose.rot["Clavicle" + sd] = qaxis(Z, s * 8.0)
        pose.rot["Shoulder" + sd] = qmul(qaxis(Z, s * (30.0 + 25.0 * (1 - rise))), qaxis(X, -30.0 * rise - 10.0))
        pose.rot["Elbow" + sd] = qaxis(X, -(30.0 + 20.0 * rise))
        hand_pose(pose, sd, curl=20.0, thumb=10.0, spread=8.0)
    head_look(pose, sk, pitch=-4.0 * rise + 8.0 * (1 - rise))
    return pose

def land_pose(sk, t, L=0.4):
    """Приземление: колени пружинят, корпус наклоняется вперёд, руки в стороны — и обратно в стойку."""
    k = keyed([(0.0, 1.0), (0.06, 1.2), (0.16, 0.7), (L, 0.0)], t)
    pose = base_stand(sk, 0.0)
    pose.root = vadd(pose.root, (0.0, -0.12 * k, -0.03 * k))
    pose.rot["Hips"] = qmul(qaxis(X, 16.0 * k), pose.rot["Hips"])
    for b in ("Spine", "Spine2", "Torso"): pose.add(b, qaxis(X, 3.0 * k))
    stand_feet(pose, sk, lz=0.02, rz=-0.02)
    for sd, s in (("L", -1.0), ("R", 1.0)):
        pose.add("Shoulder" + sd, qmul(qaxis(Z, s * 25.0 * k), qaxis(X, -20.0 * k)))
        pose.rot["Elbow" + sd] = qaxis(X, -(13.0 + 20.0 * k))
    head_look(pose, sk, pitch=6.0 * k)
    return pose

# ============================================================ сборка клипов для игры
WALK_R = STRAFE(WALK, 1, 45.0, width=0.13, v=1.3, clear=0.06, arc=0.03, foot_yaw=75.0)
WALK_L = STRAFE(WALK, -1, 45.0, width=0.13, v=1.3, clear=0.06, arc=0.03, foot_yaw=-75.0)
RUN_R = STRAFE(RUN, 1, 40.0, width=0.12, v=3.4, foot_yaw=60.0)
RUN_L = STRAFE(RUN, -1, 40.0, width=0.12, v=3.4, foot_yaw=-60.0)
# по диагонали: таз разворачивается к движению (вперёд-вбок) или от него (назад-вбок) — ноги идут почти прямо
def DIAG(base, d, yaw, **k):
    r = dict(base, dir=d, yaw_off=yaw, foot_yaw=yaw * 1.1, yaw=base.get("yaw", 4.5) * 0.7)
    r.update(k); return r
WALK_FR = DIAG(WALK, 45.0, 38.0, v=1.35)
WALK_FL = DIAG(WALK, -45.0, -38.0, v=1.35)
WALK_BR = DIAG(WALK_B, 135.0, -38.0, v=1.15)
WALK_BL = DIAG(WALK_B, -135.0, 38.0, v=1.15)
RUN_FR = DIAG(RUN, 45.0, 32.0, v=3.6)
RUN_FL = DIAG(RUN, -45.0, -32.0, v=3.6)
RUN_BR = DIAG(RUN_B, 135.0, -32.0, v=2.7)
RUN_BL = DIAG(RUN_B, -135.0, 32.0, v=2.7)

UPPER = None   # заполняется в build_clips: кости выше таза (для действий поверх походки)

def gait_clip(name, params, sk):
    g = Gait(**params); g.prepare(sk)
    n = max(8, int(round(g.T * FPS)))
    frames, contact = [], {"L": [], "R": []}
    for i in range(n):
        p, c = g.sample(sk, i / n)
        frames.append(p)
        for sd in "LR": contact[sd].append(c.get(sd, 0.0))
    return dict(name=name, loop=True, len=g.T, speed=g.v, dir=g.dir, frames=frames, contact=contact)

def pose_clip(name, fn, L, loop=False, fps=FPS, mask=None, **extra):
    n = max(1, int(round(L * fps)))
    ts = [L * i / n for i in range(n)] if loop else [L * i / n for i in range(n + 1)]
    d = dict(name=name, loop=loop, len=L, speed=0.0, dir=0.0, frames=[fn(t) for t in ts], contact=None, mask=mask)
    d.update(extra); return d

def build_clips(sk):
    upper = [n for n in sk.names if n not in ("Hips",) and not n.startswith(("Hip", "Knee", "Foot", "Toe", "Lid"))]
    arm_r = [n for n in sk.names if n.endswith("R") and not n.startswith(("Hip", "Knee", "Foot", "Toe", "Lid"))]
    C = []
    C.append(gait_clip("walk", WALK, sk))
    C.append(gait_clip("walk_b", WALK_B, sk))
    C.append(gait_clip("walk_l", WALK_L, sk))
    C.append(gait_clip("walk_r", WALK_R, sk))
    C.append(gait_clip("run", RUN, sk))
    C.append(gait_clip("run_b", RUN_B, sk))
    C.append(gait_clip("run_l", RUN_L, sk))
    C.append(gait_clip("run_r", RUN_R, sk))
    C.append(gait_clip("sprint", SPRINT, sk))
    C.append(gait_clip("jog", JOG, sk))
    for n, d in (("walk_fr", WALK_FR), ("walk_fl", WALK_FL), ("walk_br", WALK_BR), ("walk_bl", WALK_BL),
                 ("run_fr", RUN_FR), ("run_fl", RUN_FL), ("run_br", RUN_BR), ("run_bl", RUN_BL)):
        C.append(gait_clip(n, d, sk))
    C.append(pose_clip("idle", lambda t: idle_pose(sk, t), 8.0, loop=True, fps=15))
    C.append(pose_clip("sit", lambda t: sit_pose(sk, t), 1.0))                       # 0..1 с = 0..1 посадки
    C.append(pose_clip("sit_idle", lambda t: sit_pose(sk, 1.0, t), 4.0, loop=True, fps=15))
    C.append(pose_clip("type", lambda t: seated_typing(sk, t), 5.0, loop=True, mask=upper))
    C.append(pose_clip("wave", lambda t: wave_pose(sk, t), 1.8, mask=upper))
    C.append(pose_clip("swing", lambda t: swing_pose(sk, t), 0.5, mask=upper))
    C.append(pose_clip("throw", lambda t: throw_pose(sk, t), 0.7, mask=upper))
    C.append(pose_clip("hit", lambda t: hit_pose(sk, t), 0.55, mask=upper))
    C.append(pose_clip("land", lambda t: land_pose(sk, t), 0.4))
    C.append(pose_clip("aim1", lambda t: aim_pose(sk, 0.0, two=False), 0.0, mask=upper))
    C.append(pose_clip("aim2", lambda t: aim_pose(sk, 0.0, two=True), 0.0, mask=upper))
    C.append(pose_clip("hold", lambda t: hold_pose(sk, 0.0), 0.0, mask=arm_r))
    C.append(pose_clip("air_up", lambda t: air_pose(sk, 1.0), 0.0))
    C.append(pose_clip("air_down", lambda t: air_pose(sk, 0.0), 0.0))
    return C

def fmt(x): 
    s = "%.4f" % x
    s = s.rstrip("0").rstrip(".") if "." in s else s
    return "0" if s in ("-0", "") else s

def export_clips(sk, path):
    """Текстовый формат для Unity (Resources/Anim/clips_v4.txt): кости, затем клипы; повороты — x y z w,
    неизменная кость пишется одним кадром."""
    bones = [n for n in sk.names if not n.startswith("Lid")]
    clips = build_clips(sk)
    out = ["# клипы персонажей v4 — собирает Art/anim_v4.py (export_clips); не править руками", "fps %d" % FPS, "bones " + " ".join(bones)]
    for c in clips:
        fr = c["frames"]; n = len(fr)
        out.append("clip %s loop %d len %s speed %s dir %s frames %d" % (c["name"], 1 if c["loop"] else 0, fmt(c["len"]), fmt(c["speed"]), fmt(c["dir"]), n))
        if c.get("mask"): out.append("mask " + " ".join(c["mask"]))
        out.append("root " + " ".join(fmt(v) for p in fr for v in p.root))
        if c["contact"]:
            for sd in "LR": out.append("contact %s %s" % (sd, " ".join(fmt(v) for v in c["contact"][sd])))
        for bi, b in enumerate(bones):
            qs = []
            prev = None
            for p in fr:
                q = p.rot.get(b, QI)
                if prev is not None and sum(q[i] * prev[i] for i in range(4)) < 0: q = tuple(-x for x in q)   # без скачков знака
                qs.append(q); prev = q
            const = all(max(abs(q[i] - qs[0][i]) for i in range(4)) < 2e-4 for q in qs)
            if const and qs[0] == QI: continue
            vals = qs[:1] if const else qs
            out.append("b %d %s" % (bi, " ".join(fmt(v) for q in vals for v in q)))
        out.append("end")
    txt = "\n".join(out) + "\n"
    with open(path, "w", encoding="utf-8") as f: f.write(txt)
    return len(clips), len(txt)

def preview_all(arm, sk):
    """Все клипы — действиями Blender на арматуре (для просмотра и правки)."""
    acts = []
    for c in build_clips(sk):
        fr = c["frames"]
        if len(fr) == 1: fr = fr * 2
        acts.append(bake_action(arm, "anim_" + c["name"], fr).name)
    return acts
