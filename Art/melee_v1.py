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

def hand_in_weapon(sd):
    """Кисть в осях оружия: большой палец к острию (+z), пальцы вниз к лезвию (−y), ладони друг к другу через рукоять."""
    return hand_rot(sd, (0.0, -1.0, 0.0), (-1.0, 0.0, 0.0) if sd == "R" else (1.0, 0.0, 0.0))

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

def sample_keys(keys, u):
    """Ключи → (G, B, U, twist, lean, step) в момент u (гладко, без выхода за значения)."""
    if u <= keys[0][0]: k = keys[0]; return k[1], k[2], k[3], k[4], k[5], k[6]
    for a, b in zip(keys, keys[1:]):
        if u <= b[0]:
            t = smooth((u - a[0]) / max(1e-4, b[0] - a[0]))
            G = vlerp(a[1], b[1], t)
            Bv = vnorm(vlerp(vnorm(a[2]), vnorm(b[2]), t)); Uv = vnorm(vlerp(vnorm(a[3]), vnorm(b[3]), t))
            return G, Bv, Uv, lerp(a[4], b[4], t), lerp(a[5], b[5], t), lerp(a[6], b[6], t)
    k = keys[-1]; return k[1], k[2], k[3], k[4], k[5], k[6]

# ---------------------------------------------------------------- поза
def melee_pose(sk, wid, G, B, U, tw, lean, step):
    """Поза с холодным оружием: (pose, Wp, Wq)."""
    m = MEL[wid]
    pose = base_stand(sk, 0.0)
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, tw * 0.7 / 3.0), qaxis(X, lean / 3.0))
    pose.rot["Hips"] = qmul(qaxis(Y, tw * 0.3), qaxis(X, 2.0 + lean * 0.2))
    pose.root = (0.0, -0.02 - abs(step) * 0.3, step)
    stand_feet(pose, sk, lz=0.16 + step, rz=-0.1 + step * 0.3, toe_out=16.0)
    Wq = wq_from(B, U)
    Rp = m["R"]
    Wp = vsub(G, qrot(Wq, Rp))
    for sd in (("R", "L") if m["two"] else ("R",)):
        hole = vadd(Wp, qrot(Wq, m[sd]))
        hq = qmul(Wq, hand_in_weapon(sd))
        wrist = vsub(hole, qrot(hq, PALM[sd]))
        s = 1.0 if sd == "R" else -1.0
        pole = vnorm((0.8 * s, -1.0, -0.35))
        arm_ik(sk, pose, sd, wrist, hand_q=hq, pole=pole)
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
    return pose, Wp, Wq

def guard_pose(sk, wid):
    G, B, U, tw, lean = GUARD[wid]
    return melee_pose(sk, wid, G, B, U, tw, lean, 0.0)

def strike_pose(sk, wid, k, t):
    st = STRIKES[wid][k]
    G, B, U, tw, lean, step = sample_keys(st["keys"], min(1.0, t / st["L"]))
    return melee_pose(sk, wid, G, B, U, tw, lean, step)

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
        hq = hand_in_weapon("R")
        wrist = vsub(m["R"], qrot(hq, PALM["R"]))           # запястье в осях оружия
        iq = qconj(hq)
        out.append("melee %s" % wid)
        out.append("attach " + " ".join(fmt(v) for v in tuple(qrot(iq, vmul(wrist, -1.0))) + tuple(iq)))
        if m["two"]:
            lq = hand_in_weapon("L")
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

if __name__ == "__main__":
    # python3 melee_v1.py skeleton_v4.json <Resources/Anim>
    import sys, json, os
    here = os.path.dirname(os.path.abspath(sys.argv[0]))
    G = globals(); keep = G["__name__"]; G["__name__"] = "lib"
    for f in ("anim_v4.py", "grips_v1.py", "melee_v1.py"):
        exec(open(os.path.join(here, f), encoding="utf-8").read(), G)
    G["__name__"] = keep
    sk = Skel(json.load(open(sys.argv[1])))
    print(export_all_melee(sk, sys.argv[2]))
