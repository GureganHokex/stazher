# -*- coding: utf-8 -*-
# Удары холодным оружием v1 (спринт 8 версии 0.9 «Оружие в руках», Codezilla Games).
# Нож, бита, катана: стойка и серии ударов — как у аниматора, ключевыми позами: путь рукояти и направление клинка,
# разворот таза и груди (сначала таз, потом плечи, потом руки), наклон, взгляд. Руки ставит IK по точкам хвата
# (правая выше по рукояти, левая у навершия), пальцы обхватывают рукоять. Проверка — рендером в Blender, выгрузка —
# клипами mguard_<оружие> и m_<оружие>_<n> в clips_v4.txt и креплением оружия к кисти в grips_v1.txt.
# Оси персонажа как в Unity: X вправо, Y вверх, Z вперёд; оружие — x вправо, y вверх (обух), z к острию.
# Нужны anim_v4.py и grips_v1.py (hand_rot, PALM, place_weapon).
import math

# Точки хвата (оси оружия): правая ближе к клинку, левая — к навершию; кисти обхватывают рукоять с двух сторон
MEL = {
    "knife": dict(two=False, R=(0.0, 0.0, -0.012)),
    "bat": dict(two=True, R=(0.0, 0.0, 0.122), L=(0.0, 0.0, 0.036)),
    "katana": dict(two=True, R=(0.0, 0.0, 0.198), L=(0.0, 0.0, 0.085)),
}
CURL = {"knife": 66.0, "bat": 64.0, "katana": 62.0}
# Рукоять лежит в ладони наискось (D-09): от основания мизинца к основанию указательного, а не поперёк кисти.
# Остриё смотрит не по большому пальцу, а на DIAG° дальше — в сторону пальцев; так клинок продолжает предплечье,
# и запястью не нужно выгибаться к мизинцу, чтобы держать оружие перед собой
DIAG = {"knife": 24.0, "bat": 30.0, "katana": 34.0}

def hand_in_weapon(sd, wid=None):
    """Кисть в осях оружия: остриё (+z) — по большому пальцу с уклоном к пальцам, пальцы обхватывают рукоять снизу
    (к лезвию, −y), ладони друг к другу через рукоять."""
    a = math.radians(DIAG.get(wid, 0.0))
    return hand_rot(sd, (0.0, -math.cos(a), math.sin(a)), (-1.0, 0.0, 0.0) if sd == "R" else (1.0, 0.0, 0.0))

# ---------------------------------------------------------------- естественная рука (D-09)
# Запястье человека: к ладони до ~70°, к тыльной стороне до ~55°, к большому пальцу до ~20°, к мизинцу до ~35°;
# скрутка (пронация/супинация) — в предплечье, до ~80° от среднего положения; локоть не выше плеча без нужды
# и не заходит за середину груди. Среди положений локтя вокруг оси «плечо — запястье» выбирается то,
# где запястье и предплечье ближе всего к удобному положению.
def arm_metrics(sk, pose, sd):
    wp, wr = sk.fk(pose)
    S, E, W = wp["Shoulder" + sd], wp["Elbow" + sd], wp["Hand" + sd]
    d1, d2 = vnorm(vsub(E, S)), vnorm(vsub(W, E))
    elbow = math.degrees(math.acos(max(-1.0, min(1.0, vdot(d1, d2)))))
    fa = vnorm(vsub(sk.rest["Hand" + sd], sk.rest["Elbow" + sd]))
    def tw_ang(q):
        sw, tw = swing_twist(q, fa)
        a = 2.0 * math.degrees(math.atan2(vdot(tw[:3], fa), tw[3]))
        return (a + 180.0) % 360.0 - 180.0, sw
    te, _ = tw_ang(pose.rot.get("Elbow" + sd, QI))
    th, sw = tw_ang(pose.rot.get("Hand" + sd, QI))
    f = qrot(sw, (0.0, -1.0, 0.0))
    flex = math.degrees(math.atan2(-f[0] if sd == "R" else f[0], -f[1]))
    rad = math.degrees(math.atan2(f[2], -f[1]))
    return dict(elbow=elbow, twist=te + th, twist_hand=th, flex=flex, rad=rad, S=S, E=E, W=W)

def arm_cost(m, sd):
    def over(x, lim): return max(0.0, x - lim)
    s = 1.0 if sd == "R" else -1.0
    c = 0.05 * (over(m["flex"], 65.0) ** 2 + over(-m["flex"], 50.0) ** 2 + over(m["rad"], 18.0) ** 2 + over(-m["rad"], 30.0) ** 2)
    c += 0.05 * over(abs(m["twist"]), 75.0) ** 2 + 0.08 * over(abs(m["twist_hand"]), 35.0) ** 2
    c += 0.0015 * (m["flex"] ** 2 + m["rad"] ** 2) + 0.0005 * m["twist"] ** 2
    S, E = m["S"], m["E"]
    c += 300.0 * over(E[1] - S[1], -0.02) ** 2                 # локоть выше плеча
    c += 400.0 * over(-(E[0] - S[0]) * s, 0.10) ** 2           # локоть к середине груди
    c += 200.0 * over(-(E[2] - S[2]), 0.22) ** 2               # локоть далеко за спиной
    return c

def arm_ik_nat(sk, pose, sd, wrist, hand_q, pole0, phi=None, span=110.0, step=10.0):
    """arm_ik с выбором поворота локтя вокруг оси «плечо — запястье»: phi задан — ставит его, иначе ищет лучший."""
    wp, _ = sk.fk(pose)
    axis = vnorm(vsub(wrist, wp["Shoulder" + sd]))
    def put(a):
        arm_ik(sk, pose, sd, wrist, hand_q=hand_q, pole=qrot(qaxis(axis, a), pole0))
        m = arm_metrics(sk, pose, sd)
        return arm_cost(m, sd), m
    if phi is not None:
        c, m = put(phi); return phi, c, m
    best = None
    a = -span
    while a <= span + 1e-6:
        c, m = put(a)
        if best is None or c < best[1] - 1e-9 or (abs(c - best[1]) < 1e-9 and abs(a) < abs(best[0])): best = (a, c)
        a += step
    for h in (step / 2.0, step / 4.0, step / 8.0):
        for a in (best[0] - h, best[0] + h):
            c, m = put(a)
            if c < best[1]: best = (a, c)
    c, m = put(best[0])
    return best[0], c, m

def wq_from(B, U):
    """Поворот оружия: z вдоль B, y — как можно ближе к U."""
    z = vnorm(B); y = vsub(U, vmul(z, vdot(U, z)))
    if vlen(y) < 1e-5: y = vsub(Y, vmul(z, vdot(Y, z)))
    y = vnorm(y); x = vcross(y, z)
    return qfrom_basis(x, y, z)

# ---------------------------------------------------------------- ключи
# Ключ: (u, G — точка хвата правой (оси персонажа, м), B — к острию, U — обух/верх оружия, twist — разворот груди
# (+ вправо), lean — наклон вперёд, step — перенос веса вперёд (м))
GUARD = {
    "knife": ((0.16, 1.18, 0.30), (0.05, 0.42, 0.9), (0.0, 0.9, -0.4), 8.0, 4.0),
    "bat": ((0.16, 1.20, 0.30), (0.28, 0.9, 0.32), (0.0, -0.33, 0.94), 14.0, 4.0),
    "katana": ((0.05, 1.10, 0.36), (0.0, 0.5, 0.87), (0.0, 0.87, -0.5), 10.0, 5.0),
}

def K(u, G, B, U, tw, lean=0.0, step=0.0): return (u, G, B, U, tw, lean, step)

STRIKES = {
    "bat": [
        # 1. наотмашь справа налево: замах за правое плечо (таз и грудь закручены вправо), таз раскручивается,
        #    руки ведут рукоять вперёд, бита выходит плашмя, удар перед собой, проводка влево
        dict(name="наотмашь справа", L=0.62, hit=0.56, keys=[
            K(0.0, *GUARD["bat"]),
            K(0.30, (0.30, 1.36, 0.0), (0.70, 0.50, -0.51), (-0.3, 0.75, 0.6), 46.0, -2.0),
            K(0.46, (0.22, 1.20, 0.22), (0.88, 0.18, -0.44), (0.0, 0.95, 0.3), 18.0, 4.0, 0.03),
            K(0.56, (-0.02, 1.13, 0.46), (0.22, 0.05, 0.97), (0.0, 1.0, 0.0), -14.0, 8.0, 0.06),
            K(0.70, (-0.28, 1.18, 0.32), (-0.92, 0.12, 0.36), (0.0, 1.0, 0.0), -48.0, 6.0, 0.05),
            K(0.84, (-0.30, 1.30, 0.18), (-0.78, 0.5, 0.36), (0.3, 0.8, -0.5), -55.0, 3.0, 0.02),
            K(1.0, *GUARD["bat"])]),
        # 2. обратным ходом слева направо: из проводки — короткий замах у левого плеча и хлёсткий удар вправо
        dict(name="наотмашь слева", L=0.6, hit=0.55, keys=[
            K(0.0, *GUARD["bat"]),
            K(0.32, (-0.22, 1.42, 0.06), (-0.62, 0.62, -0.48), (0.0, 0.62, 0.78), -40.0, -1.0),
            K(0.46, (-0.18, 1.20, 0.30), (-0.86, 0.15, 0.48), (0.0, 1.0, 0.0), -12.0, 4.0, 0.03),
            K(0.55, (0.02, 1.14, 0.47), (0.0, 0.05, 1.0), (0.0, 1.0, 0.0), 12.0, 7.0, 0.05),
            K(0.70, (0.24, 1.18, 0.30), (0.92, 0.12, 0.36), (0.0, 1.0, 0.0), 40.0, 5.0, 0.04),
            K(0.85, (0.26, 1.30, 0.16), (0.72, 0.55, 0.0), (-0.3, 0.6, -0.7), 36.0, 3.0),
            K(1.0, *GUARD["bat"])]),
        # 3. сверху: бита поднимается двумя руками над головой (за голову), корпус выпрямлен, рубящий удар вниз-вперёд с наклоном
        dict(name="сверху", L=0.85, hit=0.62, keys=[
            K(0.0, *GUARD["bat"]),
            K(0.40, (0.08, 1.76, 0.12), (0.05, 0.55, -0.83), (0.0, 0.83, 0.55), 6.0, -8.0),
            K(0.52, (0.06, 1.72, 0.30), (0.02, 0.92, 0.38), (0.0, -0.38, 0.92), 2.0, 2.0, 0.03),
            K(0.62, (0.02, 1.20, 0.52), (0.0, -0.45, 0.89), (0.0, 0.89, 0.45), 0.0, 18.0, 0.08),
            K(0.78, (0.02, 1.02, 0.44), (0.0, -0.88, 0.47), (0.0, 0.47, 0.88), 0.0, 16.0, 0.06),
            K(1.0, *GUARD["bat"])]),
    ],
    "knife": [
        dict(name="справа налево", L=0.32, hit=0.52, keys=[
            K(0.0, *GUARD["knife"]),
            K(0.28, (0.30, 1.30, 0.18), (0.72, 0.3, 0.62), (0.0, 0.9, -0.3), 26.0, 0.0),
            K(0.52, (0.02, 1.25, 0.50), (0.12, 0.1, 0.99), (0.0, 1.0, -0.1), -6.0, 6.0, 0.03),
            K(0.78, (-0.26, 1.18, 0.34), (-0.82, 0.0, 0.57), (0.0, 1.0, 0.0), -26.0, 4.0, 0.02),
            K(1.0, *GUARD["knife"])]),
        dict(name="слева направо", L=0.32, hit=0.52, keys=[
            K(0.0, *GUARD["knife"]),
            K(0.28, (-0.18, 1.32, 0.24), (-0.75, 0.25, 0.6), (0.0, -0.9, -0.3), -24.0, 0.0),
            K(0.52, (0.06, 1.25, 0.50), (-0.1, 0.1, 0.99), (0.0, -1.0, -0.1), 4.0, 6.0, 0.03),
            K(0.78, (0.30, 1.18, 0.30), (0.82, 0.0, 0.57), (0.0, -1.0, 0.0), 22.0, 4.0, 0.02),
            K(1.0, *GUARD["knife"])]),
        dict(name="укол", L=0.42, hit=0.5, keys=[
            K(0.0, *GUARD["knife"]),
            K(0.30, (0.20, 1.16, 0.06), (0.02, 0.18, 0.98), (0.0, 1.0, -0.18), 18.0, -2.0),
            K(0.50, (0.08, 1.24, 0.66), (-0.04, 0.04, 1.0), (0.0, 1.0, 0.0), -20.0, 10.0, 0.10),
            K(0.72, (0.08, 1.24, 0.62), (-0.04, 0.04, 1.0), (0.0, 1.0, 0.0), -16.0, 8.0, 0.08),
            K(1.0, *GUARD["knife"])]),
    ],
    "katana": [
        # кэса-гири: сверху справа наискось вниз влево
        dict(name="кэса", L=0.46, hit=0.52, keys=[
            K(0.0, *GUARD["katana"]),
            K(0.30, (0.20, 1.62, 0.06), (0.35, 0.72, -0.6), (0.0, 0.6, 0.8), 30.0, -3.0),
            K(0.52, (0.0, 1.22, 0.46), (-0.32, -0.12, 0.94), (-0.6, 0.78, -0.1), -10.0, 8.0, 0.05),
            K(0.76, (-0.22, 0.98, 0.32), (-0.72, -0.56, 0.4), (-0.6, 0.6, -0.5), -32.0, 10.0, 0.04),
            K(1.0, *GUARD["katana"])]),
        # снизу вверх слева направо
        dict(name="снизу вверх", L=0.46, hit=0.52, keys=[
            K(0.0, *GUARD["katana"]),
            K(0.30, (-0.20, 0.98, 0.26), (-0.62, -0.5, 0.6), (0.62, -0.75, 0.2), -32.0, 6.0),
            K(0.52, (0.02, 1.20, 0.48), (0.12, 0.3, 0.95), (0.6, -0.78, 0.18), 4.0, 6.0, 0.05),
            K(0.76, (0.22, 1.48, 0.28), (0.58, 0.72, 0.38), (0.4, -0.6, 0.7), 26.0, 2.0, 0.03),
            K(1.0, *GUARD["katana"])]),
        # горизонтально справа налево
        dict(name="горизонтально", L=0.46, hit=0.52, keys=[
            K(0.0, *GUARD["katana"]),
            K(0.32, (0.26, 1.28, 0.08), (0.72, 0.2, -0.66), (0.0, 1.0, 0.0), 40.0, 0.0),
            K(0.52, (0.0, 1.22, 0.48), (0.08, 0.04, 1.0), (0.0, 1.0, 0.0), 0.0, 6.0, 0.05),
            K(0.76, (-0.26, 1.24, 0.28), (-1.0, 0.04, 0.12), (0.0, 1.0, 0.0), -40.0, 4.0, 0.04),
            K(1.0, *GUARD["katana"])]),
        # сверху вниз (мэн)
        dict(name="сверху вниз", L=0.62, hit=0.6, keys=[
            K(0.0, *GUARD["katana"]),
            K(0.38, (0.04, 1.80, 0.12), (0.0, 0.62, -0.78), (0.0, 0.78, 0.62), 4.0, -6.0),
            K(0.60, (0.02, 1.26, 0.52), (0.0, -0.18, 0.98), (0.0, 0.98, 0.18), 0.0, 14.0, 0.08),
            K(0.80, (0.02, 1.06, 0.46), (0.0, -0.66, 0.75), (0.0, 0.75, 0.66), 0.0, 12.0, 0.06),
            K(1.0, *GUARD["katana"])]),
        # укол
        dict(name="укол", L=0.52, hit=0.5, keys=[
            K(0.0, *GUARD["katana"]),
            K(0.30, (0.10, 1.12, 0.10), (0.0, 0.14, 0.99), (0.0, 0.99, -0.14), 16.0, -2.0),
            K(0.50, (0.02, 1.20, 0.70), (0.0, 0.05, 1.0), (0.0, 1.0, 0.0), -14.0, 12.0, 0.12),
            K(0.72, (0.02, 1.20, 0.66), (0.0, 0.05, 1.0), (0.0, 1.0, 0.0), -10.0, 10.0, 0.10),
            K(1.0, *GUARD["katana"])]),
    ],
}

def seg_of(keys, u):
    """Отрезок ключей и доля t на нём (гладко): (i, t) — между keys[i] и keys[i+1]."""
    if u <= keys[0][0]: return 0, 0.0
    for i, (a, b) in enumerate(zip(keys, keys[1:])):
        if u <= b[0]: return i, smooth((u - a[0]) / max(1e-4, b[0] - a[0]))
    return len(keys) - 2, 1.0

def sample_keys(keys, u):
    """Ключи → (G, B, U, twist, lean, step) в момент u (гладко, без выхода за значения)."""
    i, t = seg_of(keys, u); a, b = keys[i], keys[i + 1]
    G = vlerp(a[1], b[1], t)
    Bv = vnorm(vlerp(vnorm(a[2]), vnorm(b[2]), t)); Uv = vnorm(vlerp(vnorm(a[3]), vnorm(b[3]), t))
    return G, Bv, Uv, lerp(a[4], b[4], t), lerp(a[5], b[5], t), lerp(a[6], b[6], t)

# Поправки поз (D-09): оружие проворачивается в кистях вокруг своей оси (roll) и чуть наклоняется (t1, t2, °),
# локти встают вокруг оси «плечо — запястье» (phiR, phiL, °) — так, чтобы запястья и предплечья оставались
# в человеческих пределах. Подбирает solve_fix() покадрово (30 к/с) с плавностью между кадрами, удар начинается
# и заканчивается поправкой стойки. Хранятся в melee_fix.json рядом со скриптом.
FIX = {}
ROLL_FREE = {"bat": 180.0, "katana": 40.0, "knife": 70.0}   # насколько можно провернуть оружие в руках
def load_fix(path=None):
    import os, json
    cands = [path] if path else []
    try: cands.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), "melee_fix.json"))
    except NameError: pass
    try:
        import bpy
        if bpy.data.filepath: cands.append(os.path.join(os.path.dirname(bpy.data.filepath), "melee_fix.json"))
    except Exception: pass
    cands.append("melee_fix.json")
    for c in cands:
        if c and os.path.exists(c):
            FIX.clear(); FIX.update(json.load(open(c, encoding="utf-8"))); return len(FIX)
    return 0

def fix_at(wid, k, t):
    """Поправка в момент t удара k (k < 0 — стойка)."""
    if k < 0: return FIX.get("%s/g" % wid)
    fr = FIX.get("%s/%d" % (wid, k))
    if not fr: return None
    x = max(0.0, t * FPS); i = min(int(x), len(fr) - 1); j = min(i + 1, len(fr) - 1); f = x - int(x)
    return [lerp(a, b, f) for a, b in zip(fr[i], fr[j])]

def fixed_wq(B, U, f):
    Wq = wq_from(B, U)
    if f is None: return Wq
    return qmul(qmul(qaxis(Y, f[2]), qaxis(X, f[1])), qmul(Wq, qaxis(Z, f[0])))

# ---------------------------------------------------------------- поза
def body_pose(sk, tw, lean, step):
    pose = base_stand(sk, 0.0)
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, tw * 0.7 / 3.0), qaxis(X, lean / 3.0))
    pose.rot["Hips"] = qmul(qaxis(Y, tw * 0.3), qaxis(X, 2.0 + lean * 0.2))
    pose.root = (0.0, -0.02 - abs(step) * 0.3, step)
    return pose

def place_arms(sk, pose, wid, G, Wq, phis, prev=None, info=None, coarse=False):
    """Руки на оружие. phis = (phiR, phiL) — заданы, или None — подбор (возле prev, если есть)."""
    m = MEL[wid]
    Wp = vsub(G, qrot(Wq, m["R"]))
    total = 0.0
    for sd in (("R", "L") if m["two"] else ("R",)):
        hole = vadd(Wp, qrot(Wq, m[sd]))
        hq = qmul(Wq, hand_in_weapon(sd, wid))
        wrist = vsub(hole, qrot(hq, PALM[sd]))
        s = 1.0 if sd == "R" else -1.0
        pole = vnorm((0.8 * s, -1.0, -0.35))
        ix = 0 if sd == "R" else 1
        if phis is not None:
            ph, c, met = arm_ik_nat(sk, pose, sd, wrist, hq, pole, phi=phis[ix])
        else:
            p0 = None if prev is None else prev[ix]
            lo, hi, st = (-110.0, 110.0, 20.0 if coarse else 10.0) if p0 is None else (p0 - 40.0, p0 + 40.0, 5.0)
            best = None; a = lo
            while a <= hi + 1e-6:
                _, c, met = arm_ik_nat(sk, pose, sd, wrist, hq, pole, phi=a)
                if p0 is not None: c += 0.002 * (a - p0) ** 2
                if best is None or c < best[1]: best = (a, c, met)
                a += st
            for h in (st / 2.0, st / 4.0):
                for a in (best[0] - h, best[0] + h):
                    _, c, met = arm_ik_nat(sk, pose, sd, wrist, hq, pole, phi=a)
                    if p0 is not None: c += 0.002 * (a - p0) ** 2
                    if c < best[1]: best = (a, c, met)
            ph, c, met = arm_ik_nat(sk, pose, sd, wrist, hq, pole, phi=best[0])
        total += c
        if info is not None: info[sd] = (ph, c, met)
    return Wp, total

def melee_pose(sk, wid, G, B, U, tw, lean, step, fix=None, info=None):
    """Поза с холодным оружием: (pose, Wp, Wq). fix = (roll, t1, t2, phiR, phiL) или None."""
    m = MEL[wid]
    pose = body_pose(sk, tw, lean, step)
    stand_feet(pose, sk, lz=0.16 + step, rz=-0.1 + step * 0.3, toe_out=16.0)
    Wq = fixed_wq(B, U, fix)
    inf = {} if info is None else info
    Wp, cost = place_arms(sk, pose, wid, G, Wq, None if fix is None else (fix[3], fix[4]), info=inf)
    for sd in (("R", "L") if m["two"] else ("R",)):
        keep = pose.rot["Hand" + sd]
        hand_pose(pose, sd, curl=CURL[wid], thumb=48.0)
        pose.rot["Hand" + sd] = keep
    if not m["two"]:
        # левая — кулак у груди, локоть прижат
        wp, wr = sk.fk(pose)
        chest = wr["Torso"]
        lw = vadd(wp["Torso"], qrot(chest, (-0.14, 0.02, 0.24)))
        arm_ik(sk, pose, "L", lw, hand_q=qmul(chest, qmul(qaxis(Z, -70.0), qaxis(X, -40.0))), pole=vnorm((-0.6, -1.0, -0.2)))
        keep = pose.rot["HandL"]; hand_pose(pose, "L", curl=78.0, thumb=40.0); pose.rot["HandL"] = keep
    head_look(pose, sk, yaw=-tw * 0.5, pitch=6.0 + lean * 0.3)
    inf["cost"] = cost
    return pose, Wp, Wq

def guard_pose(sk, wid, info=None):
    G, B, U, tw, lean = GUARD[wid]
    return melee_pose(sk, wid, G, B, U, tw, lean, 0.0, fix=fix_at(wid, -1, 0.0), info=info)

def strike_pose(sk, wid, k, t, info=None):
    st = STRIKES[wid][k]
    u = min(1.0, t / st["L"])
    G, B, U, tw, lean, step = sample_keys(st["keys"], u)
    return melee_pose(sk, wid, G, B, U, tw, lean, step, fix=fix_at(wid, k, t), info=info)

def solve_fix(sk, path=None, log=print):
    """Покадровый подбор поправок: стойка — полным перебором поворота оружия в руках, удар — от кадра к кадру
    (с плавностью), к концу удара поправка стягивается к поправке стойки."""
    import json
    W_TILT, W_ROLL, W_SMOOTH, W_END, TILT = 0.004, 0.0015, 0.004, 0.02, 60.0
    out = {}
    def evaluate(wid, G, B, U, tw, lean, step, r, prev, anchor, wa, coarse=False):
        pose = body_pose(sk, tw, lean, step)
        info = {}
        place_arms(sk, pose, wid, G, fixed_wq(B, U, [r[0], r[1], r[2]]), None, prev=None if prev is None else prev[3:], info=info, coarse=coarse)
        c = sum(info[sd][1] for sd in ("R", "L") if sd in info)
        lim = ROLL_FREE[wid]
        c += W_TILT * (r[1] ** 2 + r[2] ** 2) + (W_ROLL * r[0] ** 2 if lim < 179.0 else 0.0)
        if abs(r[0]) > lim: c += 0.05 * (abs(r[0]) - lim) ** 2
        if prev is not None: c += W_SMOOTH * sum((a - b) ** 2 for a, b in zip(r, prev[:3]))
        if anchor is not None and wa > 0: c += wa * sum((a - b) ** 2 for a, b in zip(r, anchor[:3]))
        f = [r[0], r[1], r[2], info["R"][0], info["L"][0] if "L" in info else 0.0]
        if anchor is not None and wa > 0: c += wa * 0.5 * ((f[3] - anchor[3]) ** 2 + (f[4] - anchor[4]) ** 2)
        return c, f
    def descend(wid, G, B, U, tw, lean, step, r, prev, anchor, wa, steps):
        best, bf = evaluate(wid, G, B, U, tw, lean, step, r, prev, anchor, wa)
        for h in steps:
            moved = True; n = 0
            while moved and n < 12:
                moved = False; n += 1
                for ax in range(3):
                    for sg in (-1.0, 1.0):
                        r2 = list(r); r2[ax] += sg * h
                        if ax > 0 and abs(r2[ax]) > TILT: continue
                        c, f = evaluate(wid, G, B, U, tw, lean, step, r2, prev, anchor, wa)
                        if c < best - 1e-6: r, best, bf, moved = r2, c, f, True
        return bf, best
    for wid in sorted(MEL):
        G, B, U, tw, lean = GUARD[wid]
        lim = ROLL_FREE[wid]
        cands = []
        roll = -lim
        while roll <= lim + 1e-6:
            c, f = evaluate(wid, G, B, U, tw, lean, 0.0, [roll, 0.0, 0.0], None, None, 0.0)
            cands.append((c, roll)); roll += 15.0
        cands.sort()
        best = None
        for c0, roll in cands[:3]:
            f, c = descend(wid, G, B, U, tw, lean, 0.0, [roll, 0.0, 0.0], None, None, 0.0, (6.0, 3.0, 1.5))
            if best is None or c < best[1]: best = (f, c)
        gf = best[0]; out["%s/g" % wid] = gf
        log("%s стойка: %s цена %.2f" % (wid, ["%.0f" % v for v in gf], best[1]))
        for k, st in enumerate(STRIKES[wid]):
            n = int(math.ceil(st["L"] * FPS)) + 1
            # 1) поворот и наклон оружия в руках по кадрам — путь по сетке (динамическое программирование):
            #    сумма неудобства рук + плата за резкую смену между кадрами; начало и конец — как в стойке
            stp = 15.0
            ns = int(round(lim / stp)) if lim < 179.0 else 12
            rolls = [gf[0] + stp * q for q in range(-ns, ns + (1 if lim < 179.0 else 0))]
            states = [(r, a, b) for r in rolls for a in (-50.0, -25.0, 0.0, 25.0) for b in (-25.0, 0.0, 25.0)]
            def dist(a, b):
                d = abs(a - b)
                return min(d, 360.0 - d) if lim >= 179.0 else d
            def tdist(p, q): return dist(p[0], q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2
            keyp = []
            for i in range(n):
                u = min(1.0, i / FPS / st["L"])
                keyp.append(sample_keys(st["keys"], u))
            C = [None] * n
            for i in range(1, n - 1):
                Gk, Bk, Uk, twk, leank, stepk = keyp[i]
                C[i] = [min(150.0, evaluate(wid, Gk, Bk, Uk, twk, leank, stepk, list(sv), None, None, 0.0, coarse=True)[0]) for sv in states]
            g0 = min(range(len(states)), key=lambda q: tdist(states[q], gf[:3]))
            INF = 1e18
            D = [[INF] * len(states) for _ in range(n)]; P = [[0] * len(states) for _ in range(n)]
            D[0][g0] = 0.0
            for i in range(1, n):
                for q in range(len(states)):
                    if i == n - 1 and q != g0: continue
                    bestv, bp = INF, 0
                    for q0 in range(len(states)):
                        if D[i - 1][q0] >= INF: continue
                        v = D[i - 1][q0] + 0.015 * tdist(states[q], states[q0])
                        if v < bestv: bestv, bp = v, q0
                    D[i][q] = bestv + (C[i][q] if C[i] is not None else 0.0); P[i][q] = bp
            q = g0; route = [0] * n
            for i in range(n - 1, -1, -1):
                route[i] = q; q = P[i][q]
            # 2) уточнение от кадра к кадру: наклон, точный поворот, локти — возле найденного пути
            frames = [list(gf)]; prev = gf; worst = 0.0
            for i in range(1, n):
                Gk, Bk, Uk, twk, leank, stepk = keyp[i]
                u = min(1.0, i / FPS / st["L"])
                r0, a0, b0 = states[route[i]]
                if lim >= 179.0:   # без скачка через ±180
                    while r0 - prev[0] > 180.0: r0 -= 360.0
                    while r0 - prev[0] < -180.0: r0 += 360.0
                wa = W_END * max(0.0, (u - 0.8) / 0.2) ** 2 * 10.0
                f, c = descend(wid, Gk, Bk, Uk, twk, leank, stepk, [r0, a0, b0], prev, gf, wa, (4.0, 2.0, 1.0))
                frames.append(f); prev = f; worst = max(worst, c)
            # последний кадр — стойка (с учётом полного оборота)
            last = list(gf)
            if lim >= 179.0:
                while last[0] - frames[-2][0] > 180.0: last[0] -= 360.0
                while last[0] - frames[-2][0] < -180.0: last[0] += 360.0
            frames[-1] = last
            out["%s/%d" % (wid, k)] = frames
            log("  %s %d: кадров %d, путь %s, худшая цена %.1f" % (wid, k + 1, n, ["%d/%d/%d" % states[q] for q in route], worst))
    FIX.clear(); FIX.update(out)
    if path:
        with open(path, "w", encoding="utf-8") as fh: json.dump({k: v for k, v in out.items()}, fh)
    return out

# ---------------------------------------------------------------- Blender: просмотр
def preview_melee(arm, sk, wid, k=None, u=0.0):
    if k is None: pose, Wp, Wq = guard_pose(sk, wid)
    else: pose, Wp, Wq = strike_pose(sk, wid, k, u * STRIKES[wid][k]["L"])
    apply_to_blender(arm, pose)
    place_weapon(wid, Wp, Wq)
    return pose, Wp, Wq

# ---------------------------------------------------------------- экспорт
def melee_clips(sk):
    upper = [n for n in sk.names if n not in ("Hips",) and not n.startswith(("Hip", "Knee", "Foot", "Toe", "Lid"))]
    C = []
    for wid in sorted(MEL):
        C.append(pose_clip("mguard_" + wid, (lambda w: (lambda t: guard_pose(sk, w)[0]))(wid), 0.0, mask=upper))
        for k, st in enumerate(STRIKES[wid]):
            C.append(pose_clip("m_%s_%d" % (wid, k + 1), (lambda w, kk: (lambda t: strike_pose(sk, w, kk, t)[0]))(wid, k), st["L"], mask=upper))
    return C

def melee_grips(sk):
    """Строки для grips_v1.txt: оружие в осях правой кисти (крепление), левая кисть в осях оружия, удары (длина, попадание)."""
    out = []
    for wid in sorted(MEL):
        m = MEL[wid]
        hq = hand_in_weapon("R", wid)
        wrist = vsub(m["R"], qrot(hq, PALM["R"]))           # запястье в осях оружия
        iq = qconj(hq)
        out.append("melee %s" % wid)
        out.append("attach " + " ".join(fmt(v) for v in tuple(qrot(iq, vmul(wrist, -1.0))) + tuple(iq)))
        if m["two"]:
            lq = hand_in_weapon("L", wid)
            lw = vsub(m["L"], qrot(lq, PALM["L"]))
            out.append("hand L " + " ".join(fmt(v) for v in tuple(lw) + tuple(lq)))
        for k, st in enumerate(STRIKES[wid]):
            out.append("strike %d %s %s" % (k + 1, fmt(st["L"]), fmt(st["hit"])))
        out.append("end")
    return out

def export_all_melee(sk, anim_dir):
    """Resources/Anim: clips_v4.txt (anim_v4 + hold_* + mguard_* + m_*) и grips_v1.txt (огнестрел + холодное)."""
    import os
    G = globals(); base = build_clips
    G["build_clips"] = lambda s: base(s) + grip_clips(s) + melee_clips(s)
    try:
        n = export_clips(sk, os.path.join(anim_dir, "clips_v4.txt"))
    finally:
        G["build_clips"] = base
    gp = os.path.join(anim_dir, "grips_v1.txt")
    m = export(sk, gp)
    lines = melee_grips(sk)
    with open(gp, "a", encoding="utf-8") as fh: fh.write("\n".join(lines) + "\n")
    return n, m, len(lines)

load_fix()

if __name__ == "__main__":
    # python3 melee_v1.py skeleton_v4.json <Resources/Anim>
    import sys, json, os
    here = os.path.dirname(os.path.abspath(sys.argv[0]))
    G = globals(); keep = G["__name__"]; G["__name__"] = "lib"
    for f in ("anim_v4.py", "grips_v1.py", "melee_v1.py"):
        exec(open(os.path.join(here, f), encoding="utf-8").read(), G)
    G["__name__"] = keep
    sk = Skel(json.load(open(sys.argv[1])))
    if len(sys.argv) > 3 and sys.argv[3] == "solve":
        solve_fix(sk, os.path.join(here, "melee_fix.json"))
    load_fix(os.path.join(here, "melee_fix.json"))
    print(export_all_melee(sk, sys.argv[2]))
