# -*- coding: utf-8 -*-
# Хват оружия v1 (спринт 8 версии 0.9 «Оружие в руках», Codezilla Games).
# Поза тела и рук с каждым стволом: где стоит оружие относительно груди, где запястья и как повёрнуты кисти,
# как согнуты пальцы (указательный — на спуске). Позы считаются поверх anim_v4.py (оси персонажа как в Unity:
# X вправо, Y вверх, Z вперёд), проверяются рендером в Blender (preview) и выгружаются для игры (export).
# Оружие — в своих осях (x вправо, y вверх, z вперёд, метры), точки — из weapons_v1.py.
#
# Blender:  ns = {}; exec(open(r"<repo>/Art/anim_v4.py", encoding="utf-8").read(), ns); exec(open(r"<repo>/Art/grips_v1.py", encoding="utf-8").read(), ns)
#           ns["preview"](arm, "pistol")       # поза на модели и оружие в руках
# Python:   python3 grips_v1.py <skeleton_v4.json> <out grips_v1.txt>
import math

# Точка хвата в кулаке, в осях кисти (как CharacterAnim.PalmR/PalmL): ось рукояти проходит здесь
PALM = {"R": (-0.036, -0.088, 0.008), "L": (0.036, -0.088, 0.008)}

# Точки оружия (оси оружия) — из weapons_v1.py
WPN = {
    "pistol": dict(GripR=(0, -0.064, -0.054), GripL=(-0.014, -0.078, -0.046), Eye=(0, 0.026, -0.072), Muzzle=(0, 0, 0.114), Trigger=(0, -0.021, 0.014)),
    "smg": dict(GripR=(0, -0.078, -0.055), GripL=(0, -0.042, 0.200), Eye=(0, 0.050, -0.098), Muzzle=(0, 0, 0.324), Stock=(0, 0.0, -0.281), Trigger=(0, -0.040, 0.016)),
    "shotgun": dict(GripR=(0, -0.050, -0.150), GripL=(0, -0.050, 0.310), Eye=(0, 0.032, -0.080), Muzzle=(0, 0.006, 0.6205), Stock=(0, -0.050, -0.444), Trigger=(0, -0.038, 0.006)),
    "rifle": dict(GripR=(0, -0.070, -0.068), GripL=(0, -0.022, 0.270), Eye=(0, 0.054, -0.122), Muzzle=(0, 0.006, 0.510), Stock=(0, -0.032, -0.344), Trigger=(0, -0.026, 0.010)),
    "sniper": dict(GripR=(0, -0.060, -0.150), GripL=(0, -0.050, 0.250), Eye=(0, 0.050, -0.270), Muzzle=(0, 0, 0.7125), Stock=(0, -0.040, -0.530), Trigger=(0, -0.052, -0.022)),
    "mg": dict(GripR=(0, -0.090, -0.122), GripL=(0, -0.006, 0.280), Eye=(0, 0.074, -0.142), Muzzle=(0, 0, 0.681), Stock=(0, -0.020, -0.456), Trigger=(0, -0.052, -0.040)),
}

# Как держать. place: "pistol" — рукоять от середины плеч (оси персонажа), "shoulder" — затыльник от правого плечевого сустава.
# Кисть: f — куда смотрят пальцы (от запястья к костяшкам), n — куда смотрит ладонь, at — сдвиг точки хвата от якоря (оси оружия).
# Пальцы: curl — сгиб среднего, безымянного, мизинца (по суставам), per — свой сгиб пальцу, thumb — большой, trig — указательный на спуск.
HOLD = {
    # пистолет: стойка «равнобедренный треугольник» — руки почти прямые, рукоять у линии глаз, левая ладонь
    # закрывает левую сторону рукояти поверх пальцев правой, большие пальцы вперёд вдоль рамы
    "pistol": dict(place="pistol", at=(0.03, 0.15, 0.55), twist=0.0, head=(0.0, 4.0, 0.0),
                   R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.4, 0.92), n=(-1.0, 0.0, 0.0), curl=62.0, thumb=12.0, trig=True,
                          pole=(0.6, -1.0, -0.2)),
                   L=dict(anchor="GripR", at=(-0.024, -0.022, 0.028), f=(0.25, -0.6, 0.76), n=(1.0, 0.15, -0.1), curl=58.0, thumb=6.0,
                          pole=(-0.6, -1.0, -0.2))),
    # винтовки: затыльник в плечевой впадине, корпус развёрнут (левое плечо вперёд), голова склонена к прикладу,
    # правая — на шейке приклада или пистолетной рукояти, локоть в сторону; левая — под цевьём ладонью вверх, локоть вниз
    "sniper": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=38.0, head=(0.0, 16.0, 14.0),
                   R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.55, 0.84), n=(-1.0, 0.0, 0.0), curl=60.0, thumb=30.0, trig=True,
                          pole=(1.0, -0.35, -0.3)),
                   L=dict(anchor="GripL", at=(0.0, -0.004, 0.0), f=(0.6, -0.1, 0.79), n=(-0.35, 1.0, 0.0), curl=45.0, thumb=20.0,
                          pole=(-0.15, -1.0, 0.1))),
    "rifle": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=34.0, head=(0.0, 14.0, 12.0),
                  R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.45, 0.89), n=(-1.0, 0.0, 0.0), curl=60.0, thumb=35.0, trig=True,
                         pole=(1.0, -0.6, -0.3)),
                  L=dict(anchor="GripL", at=(0.0, 0.0, 0.0), f=(0.62, 0.05, 0.78), n=(-0.3, 1.0, 0.0), curl=50.0, thumb=25.0,
                         pole=(-0.2, -1.0, 0.1))),
    "smg": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=30.0, head=(0.0, 14.0, 12.0),
                R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.45, 0.89), n=(-1.0, 0.0, 0.0), curl=60.0, thumb=35.0, trig=True,
                       pole=(1.0, -0.7, -0.3)),
                L=dict(anchor="GripL", at=(0.0, 0.0, 0.0), f=(0.62, 0.05, 0.78), n=(-0.3, 1.0, 0.0), curl=52.0, thumb=25.0,
                       pole=(-0.2, -1.0, 0.1))),
    "shotgun": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=36.0, head=(0.0, 14.0, 12.0),
                    R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.55, 0.84), n=(-1.0, 0.0, 0.0), curl=60.0, thumb=30.0, trig=True,
                           pole=(1.0, -0.5, -0.3)),
                    L=dict(anchor="GripL", at=(0.0, 0.004, 0.0), f=(0.6, -0.05, 0.8), n=(-0.35, 1.0, 0.0), curl=48.0, thumb=22.0,
                           pole=(-0.15, -1.0, 0.1))),
    "mg": dict(place="shoulder", at=(-0.05, -0.02, 0.07), twist=34.0, head=(0.0, 12.0, 10.0),
               R=dict(anchor="GripR", at=(0.0, 0.0, 0.0), f=(0.0, -0.45, 0.89), n=(-1.0, 0.0, 0.0), curl=60.0, thumb=35.0, trig=True,
                      pole=(1.0, -0.6, -0.3)),
               L=dict(anchor="GripL", at=(0.0, -0.012, 0.0), f=(0.62, 0.05, 0.78), n=(-0.3, 1.0, 0.0), curl=50.0, thumb=25.0,
                      pole=(-0.2, -1.0, 0.1))),
}

# ---------------------------------------------------------------- кисть из направлений
def hand_rot(sd, f, n):
    """Поворот кисти (оси кисти в покое = оси персонажа): пальцы (−Y кисти) вдоль f, ладонь (−X у правой, +X у левой) — n."""
    f = vnorm(f); n = vnorm(vsub(n, vmul(f, vdot(n, f))))
    ya = vmul(f, -1.0)
    xa = vmul(n, -1.0) if sd == "R" else n
    za = vcross(xa, ya)
    return qfrom_basis(xa, ya, za)

def wpoint(Wp, Wq, p): return vadd(Wp, qrot(Wq, p))

# ---------------------------------------------------------------- поза
def grip_pose(sk, wid, pitch=0.0):
    """Поза с оружием wid и положение оружия: (pose, Wp, Wq) — оружие в осях персонажа (Wp — начало, Wq — поворот)."""
    h = HOLD[wid]; A = WPN[wid]
    pose = base_stand(sk, 0.0)
    tw = h["twist"]
    for b in ("Spine", "Spine2", "Torso"): pose.rot[b] = qmul(qaxis(Y, tw / 3.0), qaxis(X, 1.0 + pitch / 6.0))
    pose.rot["Hips"] = qmul(qaxis(Y, tw * 0.3), qaxis(X, 2.0))
    stand_feet(pose, sk, lz=0.12 if tw > 10 else 0.04, rz=-0.08 if tw > 10 else 0.0, toe_out=14.0 if tw > 10 else 8.0)
    wp, wr = sk.fk(pose)
    aim = qmul(qaxis(Y, 0.0), qaxis(X, pitch))            # оружие смотрит вперёд (и вниз на pitch)
    if h["place"] == "pistol":
        mid = vmul(vadd(wp["ShoulderL"], wp["ShoulderR"]), 0.5)
        grip = vadd(mid, qrot(aim, h["at"]))
        Wq = aim
        Wp = vsub(grip, qrot(Wq, A["GripR"]))
    else:
        pocket = vadd(wp["ShoulderR"], qrot(wr["Torso"], h["at"]))
        Wq = aim
        Wp = vsub(pocket, qrot(Wq, A["Stock"]))
    for sd in ("R", "L"):
        d = h[sd]
        hole = wpoint(Wp, Wq, vadd(A[d["anchor"]], d["at"]))
        hq = qmul(Wq, hand_rot(sd, d["f"], d["n"]))
        wrist = vsub(hole, qrot(hq, PALM[sd]))
        arm_ik(sk, pose, sd, wrist, hand_q=hq, pole=vnorm(d["pole"]))
        keep = pose.rot["Hand" + sd]
        hand_pose(pose, sd, curl=d["curl"], thumb=d["thumb"], per=d.get("per"))
        pose.rot["Hand" + sd] = keep
        if d.get("trig"):
            trigger_finger(sk, pose, sd, wpoint(Wp, Wq, vadd(A["Trigger"], (0.0, -0.011, 0.004))))
    hy, hp, hr = h["head"]
    head_look(pose, sk, yaw=hy - tw * 0.0, pitch=hp + pitch, roll=hr)
    return pose, Wp, Wq

def trigger_finger(sk, pose, sd, target):
    """Указательный: сгиб, при котором подушечка (центр последней фаланги) ближе всего к спуску."""
    s = 1.0 if sd == "L" else -1.0
    ax = (0.0, 0.0, s)
    best, bd = 0.0, 9.0
    for i in range(0, 41):
        a = i * 2.5
        for j, k in enumerate((0.7, 1.0, 0.8)): pose.rot["Index" + str(j + 1) + sd] = qaxis(ax, a * k)
        wp, wr = sk.fk(pose)
        pad = vadd(wp["Index3" + sd], qrot(wr["Index3" + sd], (0.0, -0.011, 0.0)))
        d = vlen(vsub(pad, target))
        if d < bd: bd, best = d, a
    for j, k in enumerate((0.7, 1.0, 0.8)): pose.rot["Index" + str(j + 1) + sd] = qaxis(ax, best * k)
    return bd

# ---------------------------------------------------------------- Blender: просмотр
def u2b_v(v): return (-v[0], -v[2], v[1])

def place_weapon(wid, Wp, Wq):
    """Корень оружия W_<wid> из Weapons_v1 — в позу (оси персонажа → Blender)."""
    import bpy, mathutils
    o = bpy.data.objects.get("W_" + wid)
    M = mathutils.Matrix(((-1, 0, 0), (0, 0, 1), (0, -1, 0)))   # u = M b
    Ru = mathutils.Quaternion((Wq[3], Wq[0], Wq[1], Wq[2])).to_matrix()
    Rb = M.transposed() @ Ru @ M
    T = mathutils.Matrix.Translation(mathutils.Vector(u2b_v(Wp))) @ Rb.to_4x4()
    o.matrix_world = T
    return o

def preview(arm, sk, wid, pitch=0.0):
    pose, Wp, Wq = grip_pose(sk, wid, pitch)
    apply_to_blender(arm, pose)
    place_weapon(wid, Wp, Wq)
    return pose, Wp, Wq

# ---------------------------------------------------------------- экспорт для игры
def rel(Wp, Wq, p, q):
    """Точка и поворот в осях оружия."""
    iq = qconj(Wq)
    return qrot(iq, vsub(p, Wp)), qmul(iq, q)

def export(sk, path, wids=None):
    """grips_v1.txt: на каждое оружие — запястья и повороты кистей в осях оружия, локти (для полюса),
    пальцы (15 поворотов на кисть), оружие в осях груди (Torso) и поворот корпуса."""
    out = ["# хват оружия v1 — собирает Art/grips_v1.py (export); не править руками"]
    for wid in (wids or sorted(HOLD)):
        pose, Wp, Wq = grip_pose(sk, wid)
        wp, wr = sk.fk(pose)
        tp, tq = rel(wp["Torso"], wr["Torso"], Wp, Wq)    # оружие в осях груди
        out.append("weapon %s twist %s" % (wid, fmt(HOLD[wid]["twist"])))
        out.append("chest " + " ".join(fmt(v) for v in tuple(tp) + tuple(tq)))
        for sd in ("R", "L"):
            p, q = rel(Wp, Wq, wp["Hand" + sd], wr["Hand" + sd])
            e, _ = rel(Wp, Wq, wp["Elbow" + sd], wr["Elbow" + sd])
            out.append("hand %s %s" % (sd, " ".join(fmt(v) for v in tuple(p) + tuple(q))))
            out.append("elbow %s %s" % (sd, " ".join(fmt(v) for v in e)))
            fq = []
            for f in ("Index", "Middle", "Ring", "Pinky", "Thumb"):
                for j in (1, 2, 3): fq += list(pose.rot.get(f + str(j) + sd, QI))
            out.append("fingers %s %s" % (sd, " ".join(fmt(v) for v in fq)))
        out.append("end")
    with open(path, "w", encoding="utf-8") as fh: fh.write("\n".join(out) + "\n")
    return len(out)

def grip_clips(sk):
    """Позы с оружием — клипами hold_<оружие> (верх тела), для слоя прицела в игре."""
    upper = [n for n in sk.names if n not in ("Hips",) and not n.startswith(("Hip", "Knee", "Foot", "Toe", "Lid"))]
    return [pose_clip("hold_" + wid, (lambda w: (lambda t: grip_pose(sk, w)[0]))(wid), 0.0, mask=upper) for wid in sorted(HOLD)]

def export_all(sk, anim_dir):
    """Resources/Anim: clips_v4.txt (клипы anim_v4 + hold_*) и grips_v1.txt."""
    import os
    base = build_clips
    globals()["build_clips"] = lambda s: base(s) + grip_clips(s)
    try:
        n = export_clips(sk, os.path.join(anim_dir, "clips_v4.txt"))
    finally:
        globals()["build_clips"] = base
    return n, export(sk, os.path.join(anim_dir, "grips_v1.txt"))

if __name__ == "__main__":
    import sys, json
    exec(open(sys.argv[0].replace("grips_v1.py", "anim_v4.py"), encoding="utf-8").read())
    sk = Skel(json.load(open(sys.argv[1])))
    print(export_all(sk, sys.argv[2]))
