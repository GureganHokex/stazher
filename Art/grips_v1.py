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
    "shotgun": dict(GripR=(0, -0.026, -0.138), GripL=(0, -0.050, 0.310), Eye=(0, 0.032, -0.080), Muzzle=(0, 0.006, 0.6205), Stock=(0, -0.050, -0.444), Trigger=(0, -0.038, -0.044)),
    "rifle": dict(GripR=(0, -0.070, -0.068), GripL=(0, -0.022, 0.270), Eye=(0, 0.054, -0.122), Muzzle=(0, 0.006, 0.510), Stock=(0, -0.032, -0.344), Trigger=(0, -0.026, 0.010)),
    "sniper": dict(GripR=(0, -0.060, -0.150), GripL=(0, -0.050, 0.250), Eye=(0, 0.050, -0.270), Muzzle=(0, 0, 0.7125), Stock=(0, -0.040, -0.530), Trigger=(0, -0.052, -0.052)),
    "mg": dict(GripR=(0, -0.090, -0.122), GripL=(0, -0.006, 0.280), Eye=(0, 0.074, -0.142), Muzzle=(0, 0, 0.681), Stock=(0, -0.020, -0.456), Trigger=(0, -0.052, -0.040)),
}

# Как держать. place: "pistol" — рукоять от середины плеч (оси персонажа), "shoulder" — затыльник от правого плечевого сустава.
# Кисть: f — куда смотрят пальцы (от запястья к костяшкам), n — куда смотрит ладонь, at — сдвиг точки хвата от якоря (оси оружия).
# Пальцы: curl — сгиб среднего, безымянного, мизинца (по суставам), per — свой сгиб пальцу, thumb — большой, trig — указательный на спуск.
HOLD = {
    # пистолет: стойка «равнобедренный треугольник» — руки почти прямые, рукоять у линии глаз, левая ладонь
    # закрывает левую сторону рукояти поверх пальцев правой, большие пальцы вперёд вдоль рамы
    "pistol": dict(place="pistol", at=(0.03, 0.15, 0.55), twist=0.0, head=(0.0, 4.0, 0.0),
                   # правая: перепонка большого пальца на затылке рукояти, ладонь на правой щёчке, костяшки у переднего угла,
                   # средний, безымянный и мизинец обхватывают переднюю грань и ложатся на левую щёчку; большой — вдоль рамы слева
                   R=dict(K=(0.020, -0.050, -0.020), f=(0.22, -0.16, 0.97), n=(-0.97, 0.0, 0.22), curl=62.0, thumb=12.0, trig=True,
                          tips={"Middle": (-0.022, -0.050, -0.040), "Ring": (-0.022, -0.066, -0.044), "Pinky": (-0.020, -0.081, -0.048)},
                          thumb_tip=(-0.022, -0.019, -0.030), pole=(0.6, -1.0, -0.2)),
                   # левая: пятка ладони закрывает левую щёчку и кончики пальцев правой, пальцы обхватывают пальцы правой спереди,
                   # указательный — под спусковой скобой, большой — вперёд вдоль рамы под большим правой
                   L=dict(K=(-0.032, -0.066, -0.004), f=(0.30, -0.45, 0.84), n=(0.93, 0.15, -0.33), curl=58.0, thumb=6.0,
                          tips={"Index": (0.028, -0.054, -0.014), "Middle": (0.030, -0.068, -0.016), "Ring": (0.028, -0.083, -0.020), "Pinky": (0.024, -0.096, -0.024)},
                          thumb_tip=(-0.026, -0.031, 0.010), pole=(-0.6, -1.0, -0.2))),
    # винтовки: затыльник в плечевой впадине, корпус развёрнут (левое плечо вперёд), голова склонена к прикладу,
    # правая — на шейке приклада или пистолетной рукояти, локоть в сторону; левая — под цевьём ладонью вверх, локоть вниз
    "sniper": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=38.0, head=(0.0, 16.0, 14.0),
                   # правая на наклонной шейке: ладонь справа, пальцы обхватывают спереди на левую сторону, большой сверху налево
                   R=dict(K=(0.022, -0.058, -0.1025), f=(0.15, -0.70, 0.69), n=(-0.97, 0.0, 0.22), curl=60.0, thumb=30.0, trig=True,
                          tips={"Middle": (-0.023, -0.055, -0.115), "Ring": (-0.023, -0.068, -0.125), "Pinky": (-0.022, -0.080, -0.135)},
                          thumb_tip=(-0.022, -0.022, -0.120), pole=(1.0, -0.35, -0.3)),
                   # левая под цевьём ладонью вверх: большой слева вдоль ложи, пальцы обхватывают справа
                   L=dict(K=(0.004, -0.0587, 0.25), f=(0.7, 0.15, 0.7), n=(0.25, 1.0, 0.0), curl=45.0, thumb=20.0,
                          tips={"Index": (0.030, -0.012, 0.28), "Middle": (0.030, -0.014, 0.262), "Ring": (0.029, -0.017, 0.245), "Pinky": (0.027, -0.020, 0.23)},
                          thumb_tip=(-0.030, -0.010, 0.28), pole=(-0.15, -1.0, 0.1))),
    "rifle": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=34.0, head=(0.0, 14.0, 12.0),
                  R=dict(K=(0.021, -0.052, -0.040), f=(0.2, -0.28, 0.94), n=(-0.97, 0.0, 0.22), curl=60.0, thumb=35.0, trig=True,
                         tips={"Middle": (-0.022, -0.052, -0.058), "Ring": (-0.022, -0.069, -0.063), "Pinky": (-0.021, -0.085, -0.068)},
                         thumb_tip=(-0.024, -0.024, -0.040), pole=(1.0, -0.6, -0.3)),
                  L=dict(K=(0.004, -0.0315, 0.27), f=(0.7, 0.15, 0.7), n=(0.25, 1.0, 0.0), curl=50.0, thumb=25.0,
                         tips={"Index": (0.033, 0.008, 0.300), "Middle": (0.033, 0.006, 0.282), "Ring": (0.032, 0.003, 0.265), "Pinky": (0.030, 0.0, 0.250)},
                         thumb_tip=(-0.032, 0.008, 0.300), pole=(-0.2, -1.0, 0.1))),
    "smg": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=30.0, head=(0.0, 14.0, 12.0),
                R=dict(K=(0.023, -0.060, -0.023), f=(0.2, -0.1, 0.97), n=(-0.97, 0.0, 0.22), curl=60.0, thumb=35.0, trig=True,
                       tips={"Middle": (-0.025, -0.060, -0.045), "Ring": (-0.025, -0.077, -0.047), "Pinky": (-0.024, -0.093, -0.050)},
                       thumb_tip=(-0.026, -0.040, -0.050), pole=(1.0, -0.7, -0.3)),
                L=dict(K=(0.004, -0.0465, 0.20), f=(0.7, 0.15, 0.7), n=(0.25, 1.0, 0.0), curl=52.0, thumb=25.0,
                       tips={"Index": (0.031, -0.010, 0.23), "Middle": (0.031, -0.012, 0.212), "Ring": (0.030, -0.015, 0.195), "Pinky": (0.028, -0.018, 0.18)},
                       thumb_tip=(-0.031, -0.008, 0.23), pole=(-0.2, -1.0, 0.1))),
    "shotgun": dict(place="shoulder", at=(-0.05, 0.0, 0.07), twist=36.0, head=(0.0, 14.0, 12.0),
                    # правая на шейке приклада (наклон ~33°): пальцы снизу обхватывают на левую сторону, большой сверху
                    R=dict(K=(0.026, -0.040, -0.130), f=(0.1, -0.84, 0.55), n=(-0.97, 0.0, 0.0), curl=60.0, thumb=30.0, trig=True,
                           tips={"Middle": (-0.023, -0.020, -0.125), "Ring": (-0.023, -0.030, -0.140), "Pinky": (-0.022, -0.040, -0.153)},
                           thumb_tip=(-0.022, 0.012, -0.115), pole=(1.0, -0.5, -0.3)),
                    # левая на цевье-помпе
                    L=dict(K=(0.004, -0.059, 0.31), f=(0.7, 0.15, 0.7), n=(0.25, 1.0, 0.0), curl=48.0, thumb=22.0,
                           tips={"Index": (0.033, -0.016, 0.34), "Middle": (0.033, -0.018, 0.322), "Ring": (0.032, -0.021, 0.305), "Pinky": (0.030, -0.024, 0.29)},
                           thumb_tip=(-0.033, -0.014, 0.34), pole=(-0.15, -1.0, 0.1))),
    "mg": dict(place="shoulder", at=(-0.05, -0.02, 0.07), twist=34.0, head=(0.0, 12.0, 10.0),
               R=dict(K=(0.020, -0.062, -0.080), f=(0.2, -0.18, 0.96), n=(-0.97, 0.0, 0.22), curl=60.0, thumb=35.0, trig=True,
                      tips={"Middle": (-0.022, -0.062, -0.100), "Ring": (-0.022, -0.078, -0.104), "Pinky": (-0.021, -0.094, -0.107)},
                      thumb_tip=(-0.023, -0.045, -0.110), pole=(1.0, -0.6, -0.3)),
               L=dict(K=(0.004, -0.0495, 0.27), f=(0.7, 0.15, 0.7), n=(0.25, 1.0, 0.0), curl=50.0, thumb=25.0,
                      tips={"Index": (0.026, -0.020, 0.30), "Middle": (0.026, -0.022, 0.282), "Ring": (0.025, -0.025, 0.265), "Pinky": (0.023, -0.028, 0.25)},
                      thumb_tip=(-0.026, -0.018, 0.30), pole=(-0.2, -1.0, 0.1))),
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

# ---------------------------------------------------------------- пальцы по цели
# Рукоять пистолета (оси оружия, м; замер сетки): ширина ±1,5 см, передняя и задняя грань с наклоном рукояти
# Тела оружия для рук (оси оружия, м; замер сетки): рукоять пистолетного типа с наклоном («raked»: передняя и задняя грань
# прямыми z(y)), брусок («aabb»: цевьё, ствольная коробка) и капсула («cap»: шейка приклада, наклонная рукоять)
GRIPBOX = {
    "pistol": dict(y=(-0.125, -0.02), x=0.015, front=(-0.032, 0.275), back=(-0.080, 0.225), y0=-0.07),
    "rifle": [dict(y=(-0.125, -0.024), x=0.014, front=(-0.0585, 0.30), back=(-0.102, 0.0), y0=-0.07),
              dict(type="aabb", x=(-0.0245, 0.0245), y=(-0.0185, 0.032), z=(0.12, 0.36)),
              dict(type="aabb", x=(-0.016, 0.016), y=(-0.025, 0.02), z=(-0.11, 0.12))],
    "smg": [dict(y=(-0.135, -0.040), x=0.017, front=(-0.0366, 0.055), back=(-0.090, 0.0), y0=-0.07),
            dict(type="aabb", x=(-0.023, 0.023), y=(-0.034, 0.034), z=(0.13, 0.32)),
            dict(type="aabb", x=(-0.02, 0.02), y=(-0.035, 0.035), z=(-0.11, 0.13))],
    "mg": [dict(y=(-0.13, -0.04), x=0.0165, front=(-0.0948, 0.19), back=(-0.119, 0.29), y0=-0.07),
           dict(type="aabb", x=(-0.0175, 0.0175), y=(-0.0365, 0.05), z=(0.12, 0.38)),
           dict(type="aabb", x=(-0.02, 0.02), y=(-0.045, 0.06), z=(-0.14, 0.12))],
    "shotgun": [dict(type="cap", a=(0.0, 0.002, -0.108), b=(0.0, -0.042, -0.175), r=0.021),
                dict(type="aabb", x=(-0.0255, 0.0255), y=(-0.046, 0.004), z=(0.228, 0.394)),
                dict(type="aabb", x=(-0.0215, 0.0215), y=(-0.040, 0.03), z=(-0.108, 0.118))],
    "sniper": [dict(type="cap", a=(0.0, -0.010, -0.112), b=(0.0, -0.095, -0.178), r=0.023),
               dict(type="aabb", x=(-0.0219, 0.0219), y=(-0.046, 0.014), z=(0.12, 0.42)),
               dict(type="aabb", x=(-0.0172, 0.0172), y=(-0.0172, 0.0172), z=(-0.12, 0.12))],
}

def box_pen(box, p, r=0.008):
    """Насколько точка (оси оружия) с радиусом пальца r вошла в тело оружия."""
    if box is None: return 0.0
    if isinstance(box, (list, tuple)):
        return max([box_pen(b, p, r) for b in box] + [0.0])
    t = box.get("type", "raked")
    if t == "aabb":
        d = min(p[0] - (box["x"][0] - r), (box["x"][1] + r) - p[0], p[1] - (box["y"][0] - r), (box["y"][1] + r) - p[1],
                p[2] - (box["z"][0] - r), (box["z"][1] + r) - p[2])
        return max(0.0, d)
    if t == "cap":
        a, b = box["a"], box["b"]; ab = vsub(b, a)
        k = max(0.0, min(1.0, vdot(vsub(p, a), ab) / max(1e-9, vdot(ab, ab))))
        c = vadd(a, vmul(ab, k))
        return max(0.0, box["r"] + r - vlen(vsub(p, c)))
    if not (box["y"][0] - r < p[1] < box["y"][1] + r): return 0.0
    fz = box["front"][0] + box["front"][1] * (p[1] - box["y0"]); bz = box["back"][0] + box["back"][1] * (p[1] - box["y0"])
    d = min(box["x"] + r - abs(p[0]), fz + r - p[2], p[2] - (bz - r))
    return max(0.0, d)

def palm_pen(sd, Hp, Hq, Wp, Wq, box):
    """Насколько ладонь (кожа ладони, оси кисти) вошла в рукоять."""
    s = -1.0 if sd == "R" else 1.0
    iq = qconj(Wq); worst = 0.0
    for y, x in ((-0.03, 0.026), (-0.05, 0.026), (-0.07, 0.022), (-0.085, 0.016)):
        for z in (-0.02, 0.005, 0.03):
            p = qrot(iq, vsub(vadd(Hp, qrot(Hq, (s * x, y, z))), Wp))
            worst = max(worst, box_pen(box, p, 0.0))
    return worst

def finger_pts(sk, Hp, Hq, sd, f, rots):
    """Суставы пальца и кончик (оси персонажа) при поворотах суставов rots."""
    names = [f + str(j) + sd for j in (1, 2, 3)]
    p, q, prev, out = Hp, Hq, "Hand" + sd, []
    for n, r in zip(names, rots):
        p = vadd(p, qrot(q, vsub(sk.rest[n], sk.rest[prev]))); q = qmul(q, r); out.append(p); prev = n
    out.append(vadd(p, qrot(q, vsub(sk.tail[names[2]], sk.rest[names[2]]))))
    return out

def _cost(pts, Wp, Wq, target, box):
    iq = qconj(Wq)
    loc = [qrot(iq, vsub(p, Wp)) for p in pts]
    pen = 0.0
    for a, b in zip(loc, loc[1:]):
        for t in (0.5, 1.0): pen += box_pen(box, vlerp(a, b, t))
    return vlen(vsub(loc[-1], target)) + 3.0 * pen

def wrap_finger(sk, pose, sd, f, Hp, Hq, Wp, Wq, target, box):
    """Сгиб пальца (основной сустав и два дальних), при котором кончик ближе всего к цели и палец не входит в рукоять."""
    ax = (0.0, 0.0, 1.0 if sd == "L" else -1.0)
    best = None
    for a in range(0, 101, 5):
        for b in range(0, 116, 5):
            rots = (qaxis(ax, a), qaxis(ax, b), qaxis(ax, b * 0.7))
            c = _cost(finger_pts(sk, Hp, Hq, sd, f, rots), Wp, Wq, target, box)
            if best is None or c < best[0]: best = (c, rots)
    for j, r in enumerate(best[1]): pose.rot[f + str(j + 1) + sd] = r
    return best[0]

def aim_thumb(sk, pose, sd, Hp, Hq, Wp, Wq, target, box):
    """Большой палец: разворот основания (два угла) и сгиб двух фаланг — кончик к цели, не входя в рукоять."""
    s = 1.0 if sd == "L" else -1.0
    tdir = vnorm((0.3 * s, -0.52, 0.8)); tax = vnorm(vcross(tdir, (0.0, 0.0, -1.0)))
    best = None
    for a1 in range(-80, 81, 8):
        for a2 in range(-80, 81, 8):
            base = qmul(qaxis(Y, a1), qaxis(X, a2))
            for c in (0.0, 10.0, 20.0, 35.0):
                rots = (base, qaxis(tax, c), qaxis(tax, c * 0.8))
                cst = _cost(finger_pts(sk, Hp, Hq, sd, "Thumb", rots), Wp, Wq, target, box)
                if best is None or cst < best[0]: best = (cst, rots)
    for j, r in enumerate(best[1]): pose.rot["Thumb" + str(j + 1) + sd] = r
    return best[0]

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
        hq = qmul(Wq, hand_rot(sd, d["f"], d["n"]))
        if "K" in d:
            # средняя костяшка — в точку K (оси оружия); ладонь не входит в рукоять — кисть отодвигается от неё
            wrist = vsub(wpoint(Wp, Wq, d["K"]), qrot(hq, vsub(sk.rest["Middle1" + sd], sk.rest["Hand" + sd])))
            box = GRIPBOX.get(wid)
            if box is not None:
                nw = qrot(hq, (-1.0, 0.0, 0.0) if sd == "R" else (1.0, 0.0, 0.0))     # куда смотрит ладонь
                for _ in range(40):
                    # рукоять может утопать в мякоти ладони на palm_in (рука мультяшно толстая — иначе пальцы не обхватят)
                    if palm_pen(sd, wrist, hq, Wp, Wq, box) < d.get("palm_in", 0.009): break
                    wrist = vsub(wrist, vmul(nw, 0.002))
        else:
            hole = wpoint(Wp, Wq, vadd(A[d["anchor"]], d["at"]))
            wrist = vsub(hole, qrot(hq, PALM[sd]))
        arm_ik(sk, pose, sd, wrist, hand_q=hq, pole=vnorm(d["pole"]))
        keep = pose.rot["Hand" + sd]
        hand_pose(pose, sd, curl=d["curl"], thumb=d["thumb"], per=d.get("per"))
        pose.rot["Hand" + sd] = keep
        if d.get("tips") or d.get("thumb_tip"):
            wp, wr = sk.fk(pose); Hp, Hq = wp["Hand" + sd], wr["Hand" + sd]
            box = GRIPBOX.get(wid)
            for fn, T in d.get("tips", {}).items(): wrap_finger(sk, pose, sd, fn, Hp, Hq, Wp, Wq, T, box)
            if d.get("thumb_tip"): aim_thumb(sk, pose, sd, Hp, Hq, Wp, Wq, d["thumb_tip"], box)
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
