# -*- coding: utf-8 -*-
# Персонажи v3 (стажёр, Гена, Dev1–Dev4) — цельные модели со скелетом (Codezilla Games).
# Тело — одна сетка без стыков (каркас из колец + одно сглаживание), одежда — оболочки поверх тела
# с теми же весами, голова и лицо — жёсткие детали на кости Head.
# Запуск в Blender:  exec(open(r"<repo>/Art/characters_v3.py", encoding="utf-8").read()); BUILD_ALL()
# Один персонаж: BUILD3("Gena"). Поза и лицо для проверки: pose3(arm, {...}), set_face(arm, "joy"), show_outfit(arm, "qa").
import bpy, bmesh, math
from mathutils import Vector as V, Matrix, Quaternion
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree

TAU = 2 * math.pi
FWD = V((0, -1, 0))          # персонаж смотрит в −Y, левая сторона персонажа — +X

# ---------------------------------------------------------------- материалы
def lin(h):
    h = h.lstrip('#'); c = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c) + (1.0,)

def mat(name, hexcol, rough=0.85):
    m = bpy.data.materials.get(name)
    if m: return m
    m = bpy.data.materials.new(name)
    try: m.use_nodes = True
    except Exception: pass
    b = m.node_tree.nodes.get("Principled BSDF") if m.node_tree else None
    if b:
        b.inputs["Base Color"].default_value = lin(hexcol); b.inputs["Roughness"].default_value = rough
    m.diffuse_color = lin(hexcol); m.roughness = rough
    return m

def shade(hexcol, k=0.78):
    return "".join("%02X" % int(int(hexcol[i:i + 2], 16) * k) for i in (0, 2, 4))

# ---------------------------------------------------------------- скелет
GROUPS = ["Hips", "Torso", "Neck", "Head", "HipL", "KneeL", "FootL", "HipR", "KneeR", "FootR",
          "ShoulderL", "ElbowL", "HandL", "ShoulderR", "ElbowR", "HandR"]
GI = {n: i for i, n in enumerate(GROUPS)}

SH = (0.21, 0.0, 1.41); EL = (0.265, 0.01, 1.11); WR = (0.29, -0.035, 0.85)
def mx(p, s): return V((p[0] * s, p[1], p[2]))

# Параметры персонажа: ширина корпуса (tw) и форма головы (ширина, высота) — как в v2
SPEC = {"tw": 1.0, "head": (1.0, 1.0)}
def TW(): return SPEC.get("tw", 1.0)
def HX(): return SPEC.get("head", (1.0, 1.0))[0]
def HZ(): return SPEC.get("head", (1.0, 1.0))[1]
def mw(p, s): return V((p[0] * TW() * s, p[1], p[2]))     # точка руки: плечи шире у широких персонажей

def apply_spec(spec):
    global SPEC, SH, EL, WR, EYE_X, EYE_Z, MOUTH_Z
    SPEC = dict(spec); tw = TW()
    SH = (0.21 * tw, 0.0, 1.41); EL = (0.265 * tw, 0.01, 1.11); WR = (0.29 * tw, -0.035, 0.85)
    EYE_X = 0.068 * HX(); EYE_Z = 1.852 + (HZ() - 1) * 0.2; MOUTH_Z = 1.56 + 0.09 * HZ()

def bone_table(eyes):
    t = [("Hips", (0, 0, 0.93), (0, 0, 1.0), None),
         ("Torso", (0, 0, 0.96), (0, 0, 1.38), "Hips"),
         ("Neck", (0, 0, 1.47), (0, -0.01, 1.6), "Torso"),
         ("Head", (0, -0.01, 1.6), (0, -0.01, 1.86), "Neck")]
    for sd, s in (("L", 1), ("R", -1)):
        t += [("Hip" + sd, mx((0.1, 0, 0.9), s), mx((0.1, 0, 0.52), s), "Hips"),
              ("Knee" + sd, mx((0.1, 0, 0.52), s), mx((0.1, 0, 0.1), s), "Hip" + sd),
              ("Foot" + sd, mx((0.1, 0, 0.1), s), mx((0.1, -0.13, 0.03), s), "Knee" + sd),
              ("Shoulder" + sd, mx(SH, s), mx(EL, s), "Torso"),
              ("Elbow" + sd, mx(EL, s), mx(WR, s), "Shoulder" + sd),
              ("Hand" + sd, mx(WR, s), mw((0.294, -0.042, 0.72), s), "Elbow" + sd)]
    for sd, c in eyes.items():
        t.append(("Lid" + sd, tuple(c), tuple(c + V((0, 0, 0.05))), "Head"))
    return t

def make_armature(coll, eyes):
    arm = bpy.data.armatures.new("Rig_Intern")
    ob = bpy.data.objects.new("Root", arm); coll.objects.link(ob)
    vl = bpy.context.view_layer
    vl.objects.active = ob
    with bpy.context.temp_override(active_object=ob, object=ob, selected_objects=[ob]):
        bpy.ops.object.mode_set(mode='EDIT')
        for n, h, tl, p in bone_table(eyes):
            b = arm.edit_bones.new(n); b.head = h; b.tail = tl; b.roll = 0.0
            if p: b.parent = arm.edit_bones[p]; b.use_connect = False
        bpy.ops.object.mode_set(mode='OBJECT')
    arm.display_type = 'STICK'; ob.show_in_front = True
    return ob

def parent_to_bone(ob, arm_ob, bone):
    mw = ob.matrix_world.copy()
    ob.parent = arm_ob; ob.parent_type = 'BONE'; ob.parent_bone = bone
    bpy.context.view_layer.update()
    ob.matrix_world = mw

def parent_to_armature(ob, arm_ob):
    ob.parent = arm_ob; ob.parent_type = 'OBJECT'; ob.matrix_parent_inverse = Matrix.Identity(4)
    m = ob.modifiers.new("Armature", 'ARMATURE'); m.object = arm_ob; m.use_vertex_groups = True
    for n in GROUPS:
        if ob.vertex_groups.get(n) is None: ob.vertex_groups.new(name=n)

# ---------------------------------------------------------------- геометрия
def frame(axis, ref=FWD):
    z = V(axis).normalized(); x = V(ref) - z * V(ref).dot(z)
    if x.length < 1e-6: x = V((1, 0, 0)) - z * z.x
    x.normalize(); return x, z.cross(x), z

def ring_pts(c, axis, rx, ry, n, ref=FWD, p=2.0, phase=0.0):
    """Кольцо из n точек. ry — радиус вперёд-назад (вдоль ref), rx — вбок."""
    x, y, z = frame(axis, ref); c = V(c); out = []
    for k in range(n):
        a = TAU * (k + phase) / n; ca, sa = math.cos(a), math.sin(a)
        ex = math.copysign(abs(ca) ** (2 / p), ca); ey = math.copysign(abs(sa) ** (2 / p), sa)
        out.append(c + x * (ex * ry) + y * (ey * rx))
    return out

def angle_in(v, c, x, y): d = v.co - c; return math.atan2(d.dot(y), d.dot(x)) % TAU

class Cage:
    """Каркас: вершины с весами костей и параметром вдоль конечности, грани с меткой части тела."""
    def __init__(self):
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()
        self.tl = self.bm.verts.layers.float.new('lt')
        self.pl = self.bm.faces.layers.int.new('part')
        self.cl = self.bm.edges.layers.float.new('crease_edge')

    def vert(self, co, w, t=0.0):
        v = self.bm.verts.new(V(co))
        for n, x in w.items():
            if x > 0: v[self.dl][GI[n]] = x
        v[self.tl] = t
        return v

    def ring(self, pts, w, t=0.0): return [self.vert(p, w, t) for p in pts]

    def face(self, vs, part):
        f = self.bm.faces.get(vs)
        if f is None: f = self.bm.faces.new(vs)
        f[self.pl] = part; return f

    def bridge(self, A, B, part, axis=None, ref=FWD):
        ca = sum((v.co for v in A), V()) / len(A); cb = sum((v.co for v in B), V()) / len(B)
        x, y, z = frame(axis if axis is not None else (cb - ca), ref)
        a = sorted(A, key=lambda v: angle_in(v, ca, x, y)); b = sorted(B, key=lambda v: angle_in(v, cb, x, y))
        aa = [angle_in(v, ca, x, y) for v in a]; bb = [angle_in(v, cb, x, y) for v in b]
        na, nb = len(a), len(b)
        def dang(p, q): return abs(((p - q + math.pi) % TAU) - math.pi)
        if na == nb:
            o = min(range(nb), key=lambda o: sum(dang(aa[i], bb[(i + o) % nb]) for i in range(na)))
            return [self.face([a[i], a[(i + 1) % na], b[(i + 1 + o) % nb], b[(i + o) % nb]], part) for i in range(na)]
        o = min(range(nb), key=lambda j: dang(aa[0], bb[j]))
        b = b[o:] + b[:o]; bb = bb[o:] + bb[:o]
        off = ((bb[0] - aa[0] + math.pi) % TAU) - math.pi
        ua = [(t - aa[0]) % TAU for t in aa] + [TAU]
        ub = [(t - bb[0]) % TAU + off for t in bb] + [TAU + off]
        i = j = 0; out = []
        while i < na or j < nb:
            if j >= nb or (i < na and ua[i + 1] <= ub[j + 1]):
                out.append(self.face([a[i], a[(i + 1) % na], b[j % nb]], part)); i += 1
            else:
                out.append(self.face([a[i % na], b[(j + 1) % nb], b[j]], part)); j += 1
        return out

    def cap(self, R, part, axis, ref=FWD):
        c = sum((v.co for v in R), V()) / len(R); x, y, z = frame(axis, ref)
        r = sorted(R, key=lambda v: angle_in(v, c, x, y))
        if len(r) == 8:
            for q in ((0, 1, 2, 3), (4, 5, 6, 7), (0, 3, 4, 7)): self.face([r[i] for i in q], part)
        else:
            w = {GROUPS[i]: r[0][self.dl][i] for i in r[0][self.dl].keys()}
            m = self.vert(c + z * 0.004, w, r[0][self.tl])
            for i in range(len(r)): self.face([r[i], r[(i + 1) % len(r)], m], part)

    def mesh(self, name):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces[:])
        me = bpy.data.meshes.new(name); self.bm.to_mesh(me); self.bm.free()
        return me

def subdivide(ob, levels=1):
    m = ob.modifiers.new("Sub", 'SUBSURF'); m.levels = levels; m.render_levels = levels
    m.use_creases = True; m.use_limit_surface = False; m.quality = 3
    dg = bpy.context.evaluated_depsgraph_get(); dg.update()
    ev = ob.evaluated_get(dg)
    names = [g.name for g in ob.vertex_groups]
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data; ob.modifiers.remove(m); ob.data = me
    if old.users == 0: bpy.data.meshes.remove(old)
    for n in names:
        if ob.vertex_groups.get(n) is None: ob.vertex_groups.new(name=n)
    return ob

def new_obj(name, me, coll):
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    for n in GROUPS:
        if me.vertices and ob.vertex_groups.get(n) is None: ob.vertex_groups.new(name=n)
    return ob

def flat(me):
    try: me.shade_flat()
    except Exception:
        for p in me.polygons: p.use_smooth = False

def smooth(me):
    try: me.shade_smooth()
    except Exception:
        for p in me.polygons: p.use_smooth = True

# ---------------------------------------------------------------- тело
K8, K12 = 1.08, 1.035       # поправка на усадку при сглаживании (8- и 12-угольные кольца)
P_TORSO, P_NECK, P_ARM, P_HAND, P_LEG, P_FOOT = 1, 2, 3, 4, 5, 6

TORSO = [  # z, полуширина, полуглубина, сдвиг вперёд(-)/назад(+), веса
    (0.860, 0.150, 0.100, 0.006, None),
    (0.950, 0.144, 0.094, 0.000, {"Hips": 0.7, "Torso": 0.3}),
    (1.060, 0.141, 0.092, -0.004, {"Hips": 0.2, "Torso": 0.8}),
    (1.170, 0.149, 0.098, -0.006, {"Torso": 1}),
    (1.270, 0.158, 0.104, -0.004, {"Torso": 1}),
    (1.350, 0.163, 0.104, 0.000, {"Torso": 1}),
    (1.430, 0.150, 0.096, 0.004, {"Torso": 1}),
    (1.470, 0.098, 0.072, 0.008, {"Torso": 0.85, "Neck": 0.15}),
]
NECK = [(1.500, 0.052, 0.050, 0.004, {"Torso": 0.45, "Neck": 0.55}),
        (1.565, 0.048, 0.047, -0.004, {"Neck": 0.6, "Head": 0.4}),
        (1.635, 0.045, 0.044, -0.012, {"Head": 1})]

def arm_rings(s):
    dU = (V(EL) - mw((0.226, 0.002, 1.30), 1)).normalized(); dF = (V(WR) - V(EL)).normalized()
    S, E = "Shoulder", "Elbow"
    R = [((0.207, 0.000, 1.368), V((1, 0, -0.75)), 0.056, 0.056, {S: 0.75, "Torso": 0.25}, 0.08),
         ((0.226, 0.002, 1.300), dU, 0.048, 0.048, {S: 1}, 0.20),
         ((0.242, 0.005, 1.220), dU, 0.045, 0.045, {S: 1}, 0.35),
         ((0.257, 0.008, 1.150), dU, 0.041, 0.041, {S: 0.75, E: 0.25}, 0.47),
         ((0.265, 0.010, 1.110), (dU + dF).normalized(), 0.039, 0.039, {S: 0.5, E: 0.5}, 0.53),
         ((0.269, 0.005, 1.065), dF, 0.038, 0.038, {S: 0.2, E: 0.8}, 0.60),
         ((0.279, -0.013, 0.970), dF, 0.035, 0.038, {E: 1}, 0.80),
         ((0.289, -0.033, 0.868), dF, 0.026, 0.033, {E: 0.6, "Hand": 0.4}, 1.00)]
    out = []
    for c, ax, rx, ry, w, t in R:
        ax = V((ax[0] * s, ax[1], ax[2]))
        w = {(k + ("L" if s > 0 else "R") if k in (S, E, "Hand") else k): x for k, x in w.items()}
        out.append((mw(c, s), ax, rx * K8, ry * K8, w, t))
    return out

LEG = [((0.093, 0.002, 0.775), 0.066, 0.072, {"Hip": 0.85, "Hips": 0.15}, 0.08, P_LEG),
       ((0.098, 0.000, 0.665), 0.063, 0.066, {"Hip": 1}, 0.30, P_LEG),
       ((0.100, -0.002, 0.578), 0.056, 0.058, {"Hip": 0.8, "Knee": 0.2}, 0.46, P_LEG),
       ((0.100, -0.006, 0.520), 0.052, 0.055, {"Hip": 0.5, "Knee": 0.5}, 0.55, P_LEG),
       ((0.100, -0.002, 0.462), 0.051, 0.054, {"Hip": 0.2, "Knee": 0.8}, 0.62, P_LEG),
       ((0.100, 0.007, 0.365), 0.052, 0.058, {"Knee": 1}, 0.75, P_LEG),
       ((0.100, 0.004, 0.240), 0.042, 0.045, {"Knee": 1}, 0.88, P_LEG),
       ((0.100, 0.000, 0.130), 0.036, 0.038, {"Knee": 1}, 1.00, P_LEG),
       ((0.100, -0.012, 0.070), 0.036, 0.042, {"Knee": 0.3, "Foot": 0.7}, 1.08, P_FOOT)]

FINGERS = [((0.026, 0.021, 0.017), 6), ((0.028, 0.023, 0.018), 1),
           ((0.026, 0.021, 0.017), -4), ((0.021, 0.017, 0.014), -9)]
CURL = (8, 16, 20)

def hand_ring(c, w, th, s):
    c = V(c); b, p = [], []
    for i, f in enumerate((-0.5, -0.25, 0.0, 0.25, 0.5)):
        pull = 0.35 if i in (0, 4) else 0.0
        y = c.y + w * f
        b.append(V((c.x + s * th / 2 * (1 - pull), y, c.z)))
        p.append(V((c.x - s * th / 2 * (1 - pull), y, c.z)))
    return b, p

def extrude_step(cage, f, d, L, u, su=1.0, sw=1.0):
    nf = bmesh.ops.extrude_discrete_faces(cage.bm, faces=[f])['faces'][0]
    c = nf.calc_center_median(); n = nf.normal.copy()
    if n.length < 1e-6: nf.normal_update(); n = nf.normal.copy()
    if n.dot(d) < 0: n = -n
    q = n.rotation_difference(d)
    uu = q @ u; uu = (uu - d * uu.dot(d)).normalized()
    for v in nf.verts:
        loc = q @ (v.co - c); cu = loc.dot(uu); rest = loc - uu * cu
        v.co = c + uu * cu * su + rest * sw + d * L
    return nf

def build_hand(cage, wrist, s):
    sd = "L" if s > 0 else "R"; H = {"Hand" + sd: 1.0}
    rings = []
    for c, w, th, wt, t in (((0.291, -0.037, 0.836), 0.064, 0.030, {"Hand" + sd: 0.8, "Elbow" + sd: 0.2}, 1.1),
                            ((0.293, -0.040, 0.800), 0.074, 0.034, H, 1.2),
                            ((0.294, -0.043, 0.757), 0.078, 0.030, H, 1.3)):
        b, p = hand_ring(mw(c, s), w * 1.06, th * 1.25, s)
        rings.append(([cage.vert(x, wt, t) for x in b], [cage.vert(x, wt, t) for x in p]))
    loop = lambda r: r[0] + r[1][::-1]
    # ещё одно 8-кольцо под запястьем: переход 8→10 не портит край рукава после сглаживания
    dF = V((WR[0] - EL[0], WR[1] - EL[1], WR[2] - EL[2])).normalized(); dF.x *= s
    w2 = cage.ring(ring_pts(mw((0.2905, -0.036, 0.853), s), dF, 0.027 * K8, 0.034 * K8, 8), {"Hand" + sd: 0.6, "Elbow" + sd: 0.4}, 1.04)
    cage.bridge(wrist, w2, P_HAND)
    cage.bridge(w2, loop(rings[0]), P_HAND, axis=V((0, 0, -1)))
    thumb_face = None
    for (b0, p0), (b1, p1) in zip(rings, rings[1:]):
        L0, L1 = loop((b0, p0)), loop((b1, p1)); n = len(L0)
        for i in range(n):
            f = cage.face([L0[i], L0[(i + 1) % n], L1[(i + 1) % n], L1[i]], P_HAND)
            if thumb_face is None and set(f.verts) == {b0[0], p0[0], p1[0], b1[0]}: thumb_face = f
    bk, pm = rings[-1]
    palm_in = V((-s, 0, 0))      # ладонь смотрит внутрь, к телу
    for k, (segs, spread) in enumerate(FINGERS):
        f = cage.face([bk[k], bk[k + 1], pm[k + 1], pm[k]], P_HAND)
        d = V((0, 0, -1)); d.rotate(Matrix.Rotation(math.radians(-spread), 3, V((1, 0, 0))))
        for j, L in enumerate(segs):
            dj = d.copy(); dj.rotate(Matrix.Rotation(math.radians(sum(CURL[:j + 1])), 3, V((0, s, 0))))
            f = extrude_step(cage, f, dj, L, V((1, 0, 0)), su=(0.72 if j == 0 else 0.9), sw=(0.9 if j < 2 else 0.8))
        f = extrude_step(cage, f, dj, 0.004, V((1, 0, 0)), su=0.55, sw=0.55)
    if thumb_face is not None:
        d = V((-0.30 * s, -0.80, -0.52)).normalized(); f = thumb_face
        for j, (L, sc) in enumerate(((0.020, 0.75), (0.020, 0.9), (0.016, 0.85))):
            dj = d.copy(); dj.rotate(Matrix.Rotation(math.radians(-14 * j * s), 3, V((0, 0, 1))))
            dj.z -= 0.18 * j; dj.normalize()
            f = extrude_step(cage, f, dj, L, V((0, 0, 1)), su=sc, sw=sc)
        extrude_step(cage, f, dj, 0.004, V((0, 0, 1)), su=0.55, sw=0.55)

def build_body(coll):
    cg = Cage()
    T = []
    for i, (z, rx, ry, yo, w) in enumerate(TORSO):
        rx, ry = rx * TW(), ry * TW()
        pts = ring_pts((0, yo, z), (0, 0, 1), rx * K12, ry * K12, 12, p=2.4)
        if w is None:     # низ таза: бока тянутся за бёдрами
            vs = []
            for pnt in pts:
                k = min(1.0, abs(pnt.x) / (rx * K12)) ** 1.5 * 0.45
                sd = "L" if pnt.x > 0 else "R"
                vs.append(cg.vert(pnt, {"Hips": 1 - k, "Hip" + sd: k} if k > 0.01 else {"Hips": 1}))
            T.append(vs)
        else:
            T.append(cg.ring(pts, w))
    holes = {"L": (2, 3), "R": (8, 9)}
    for i in range(len(T) - 1):
        for k in range(12):
            if i in (4, 5) and (k in holes["L"] or k in holes["R"]): continue
            cg.face([T[i][k], T[i + 1][k], T[i + 1][(k + 1) % 12], T[i][(k + 1) % 12]], P_TORSO)
    # шея
    prev = T[-1]
    for z, rx, ry, yo, w in NECK:
        R = cg.ring(ring_pts((0, yo, z), (0, 0, 1), rx * K12, ry * K12, 12), w)
        cg.bridge(prev, R, P_NECK, axis=V((0, 0, 1))); prev = R
    cg.cap(prev, P_NECK, V((0, 0, 1)))
    # руки
    for sd, s in (("L", 1), ("R", -1)):
        k0 = 2 if s > 0 else 8
        hole = [T[4][k0], T[4][k0 + 1], T[4][k0 + 2], T[5][k0 + 2], T[6][k0 + 2], T[6][k0 + 1], T[6][k0], T[5][k0]]
        for v in hole:
            d = v[cg.dl]; t = d.get(GI["Torso"], 0.0)
            d[GI["Torso"]] = t * 0.65; d[GI["Shoulder" + sd]] = t * 0.35
        prev = hole
        for c, ax, rx, ry, w, t in arm_rings(s):
            R = cg.ring(ring_pts(c, ax, rx, ry, 8), w, t)
            cg.bridge(prev, R, P_ARM); prev = R
        build_hand(cg, prev, s)
    # ноги
    M = cg.vert((0, 0.006, 0.800), {"Hips": 0.6, "HipL": 0.2, "HipR": 0.2})
    for sd, s in (("L", 1), ("R", -1)):
        top = (T[0][0:7] if s > 0 else T[0][6:12] + [T[0][0]]) + [M]
        prev = top
        for c, rx, ry, w, t, part in LEG:
            w = {(k + sd if k in ("Hip", "Knee", "Foot") else k): x for k, x in w.items()}
            R = cg.ring(ring_pts(mx(c, s), (0, 0, -1), rx * K8, ry * K8, 8), w, t)
            cg.bridge(prev, R, part, axis=V((0, 0, -1))); prev = R
        cg.cap(prev, P_FOOT, V((0, 0, -1)))
    loose = [v for v in cg.bm.verts if not v.link_faces]
    bmesh.ops.delete(cg.bm, geom=loose, context='VERTS')
    me = cg.mesh("Body")
    ob = new_obj("Body", me, coll)
    subdivide(ob, 1)
    flat(ob.data)
    return ob

# ---------------------------------------------------------------- голова
HEAD_PROFILE = [(0.0, 0.0), (0.062, 0.006), (0.100, 0.034), (0.127, 0.088), (0.142, 0.165),
                (0.147, 0.255), (0.140, 0.345), (0.117, 0.418), (0.072, 0.462), (0.0, 0.478)]
HEAD_BASE, HEAD_TILT, HEAD_SY = V((0, 0, 1.56)), math.radians(9), 0.96
EYE_X, EYE_Z, EYE_R, EYE_OUT = 0.068, 1.852, 0.065, 0.26
MOUTH_Z = 1.650

def prof_at(s, prof=HEAD_PROFILE):
    segs = [(V(a), V(b)) for a, b in zip(prof, prof[1:])]
    L = [(b - a).length for a, b in segs]; tot = sum(L); d = s * tot
    for (a, b), l in zip(segs, L):
        if d <= l or (a, b) == segs[-1]:
            p = a + (b - a) * min(1.0, d / max(l, 1e-9)); return p.x, p.y
        d -= l
    return prof[-1]

def faces_near(bm, pred): return [f for f in bm.faces if pred(f.calc_center_median())]

def build_head(coll):
    bm = bmesh.new(); bmesh.ops.create_cube(bm, size=2.0)
    bmesh.ops.subdivide_edges(bm, edges=bm.edges[:], cuts=4, use_grid_fill=True)
    for v in bm.verts:
        d = v.co.normalized(); th = math.acos(max(-1.0, min(1.0, d.z)))
        R, h = prof_at(1 - th / math.pi)
        hv = V((d.x, d.y, 0))
        hv = hv.normalized() if hv.length > 1e-9 else V()
        v.co = V((hv.x * R * HX(), hv.y * R * HEAD_SY, h * HZ()))
    bm.normal_update()
    # нос: два лица по центру спереди, выдавливаем и лепим спинку, кончик и крылья
    nose = faces_near(bm, lambda c: abs(c.x) < 0.02 and c.y < -0.09 and 0.09 * HZ() < c.z < 0.28 * HZ())
    nose.sort(key=lambda f: f.calc_center_median().z)
    r = bmesh.ops.extrude_face_region(bm, geom=nose)
    drop_faces(bm, nose)
    nv = sorted({v for v in r['geom'] if isinstance(v, bmesh.types.BMVert)}, key=lambda v: -v.co.z)
    zs = sorted({round(v.co.z, 4) for v in nv}, reverse=True)
    for v in nv:
        row = zs.index(round(v.co.z, 4))          # 0 — верх (переносица), 1 — кончик, 2 — низ
        fwd, sx, dz = ((0.012, 0.72, -0.004), (0.050, 1.12, -0.012), (0.036, 0.95, 0.012))[min(row, 2)]
        v.co.x *= sx; v.co.y -= fwd; v.co.z += dz
    bm.normal_update()
    # уши
    for s in (1, -1):
        ez = 0.235 * HZ()
        cand = faces_near(bm, lambda c: c.x * s > 0.11 * HX() and abs(c.z - ez) < 0.05 and abs(c.y - 0.012) < 0.04)
        if not cand: continue
        f = min(cand, key=lambda f: abs(f.calc_center_median().z - ez) + abs(f.calc_center_median().y - 0.012))
        for step, (dx, sy, sz, dy) in enumerate(((0.016, 0.85, 1.05, 0.004), (0.009, 0.7, 0.8, 0.008))):
            nf = bmesh.ops.extrude_discrete_faces(bm, faces=[f])['faces'][0]
            c = nf.calc_center_median()
            for v in nf.verts:
                o = v.co - c; v.co = c + V((o.x, o.y * sy, o.z * sz)) + V((dx * s, dy, 0))
            f = nf
    # наклон вперёд и на место
    rot = Matrix.Rotation(HEAD_TILT, 4, 'X')
    bmesh.ops.transform(bm, matrix=Matrix.Translation(HEAD_BASE) @ rot, verts=bm.verts[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new("Head"); bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new("HeadMesh", me); coll.objects.link(ob)
    subdivide(ob, 1); flat(ob.data)
    return ob

def bvh_of(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data); bm.transform(ob.matrix_world)
    t = BVHTree.FromBMesh(bm); bm.free(); return t

def hit_front(bvh, x, z, dirn=V((0, 1, 0))):
    loc, n, i, d = bvh.ray_cast(V((x, -1.0, z)), dirn)
    return (loc, n) if loc is not None else (None, None)

def sphere_bm(r, u=24, v=16):
    bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r); return bm

def obj_from_bm(name, bm, coll, m, origin=V(), smooth_shade=False):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(m)
    (smooth if smooth_shade else flat)(me)
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    ob.matrix_world = Matrix.Translation(origin)
    return ob

def eye_centers(bvh):
    out = {}
    for sd, s in (("L", 1), ("R", -1)):
        loc, n = hit_front(bvh, EYE_X * s, EYE_Z)
        y = loc.y + EYE_R * (1 - 2 * EYE_OUT)
        out[sd] = V((EYE_X * s, y, EYE_Z))
    return out

# ---------------------------------------------------------------- лицо
EMO = {  # имя: (рот, наклон бровей, подъём, веко 0..1, зрачок) — как Catalog в Unity
    "calm": ("smile", 0, 0.0, 0.12, 1.0), "joy": ("grin", 0, 0.008, 0.05, 1.1),
    "delight": ("grin", -6, 0.018, 0.0, 1.35), "surprise": ("o", 0, 0.028, 0.0, 0.7),
    "sad": ("frown", 16, 0.004, 0.35, 1.0), "angry": ("flat", -20, -0.008, 0.3, 0.8),
    "sly": ("smirk", 0, 0.0, 0.45, 0.9), "sleepy": ("flat", 0, -0.006, 0.68, 1.0)}
MOUTHS = ["smile", "grin", "flat", "o", "frown", "smirk"]

def build_eyes(coll, eyes, skin):
    white = mat("lpEye", "F4F4F2", 0.45); pupil = mat("lpPupil", "1B1020", 0.4); hi = mat("lpShine", "FFFFFF", 0.3)
    parts = {}
    for sd, c in eyes.items():
        s = 1 if sd == "L" else -1
        parts["Eye" + sd] = obj_from_bm("Eye" + sd, sphere_bm(EYE_R, 28, 18), coll, white, c, True)
        d = V((-0.05 * s, -1, -0.06)).normalized()             # чуть внутрь и вниз — «смотрит на собеседника»
        pc = c + d * (EYE_R - 0.006)
        bm = sphere_bm(0.021, 18, 12)
        q = V((0, 0, 1)).rotation_difference(d)
        bmesh.ops.scale(bm, vec=V((1, 1, 0.42)), verts=bm.verts[:])
        bmesh.ops.rotate(bm, cent=V(), matrix=q.to_matrix(), verts=bm.verts[:])
        pu = obj_from_bm("Pupil" + sd, bm, coll, pupil, pc, True); parts["Pupil" + sd] = pu
        hb = sphere_bm(0.0045, 10, 8)
        side = d.cross(V((0, 0, 1))).normalized() * (-s); up = side.cross(d).normalized() * (-s)
        hl = obj_from_bm("PupilShine" + sd, hb, coll, hi, c + (d * EYE_R + side * 0.008 + up * 0.009).normalized() * (EYE_R + 0.0), True)
        hl.parent = pu; hl.matrix_parent_inverse = pu.matrix_world.inverted()
        # веко: купол чуть больше глаза, в открытом положении отвёрнут назад-вверх (как в v2)
        rl = EYE_R + 0.0105                                       # внутренняя сторона века — снаружи зрачка и блика
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=24, v_segments=16, radius=rl)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.co.z < -1e-6], context='VERTS')
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.0045)
        bmesh.ops.rotate(bm, cent=V(), matrix=Matrix.Rotation(math.radians(-75), 3, 'X'), verts=bm.verts[:])
        parts["LidMesh" + sd] = obj_from_bm("LidMesh" + sd, bm, coll, skin, c, True)
    return parts

def brow_obj(coll, bvh, s, m):
    xi, xo, n = 0.020, 0.122, 7
    zb = EYE_Z + EYE_R + 0.022
    pts = []
    for i in range(n):
        u = i / (n - 1); x = xi + (xo - xi) * u
        zc = zb + 0.010 * math.sin(math.pi * min(1, u * 1.15)) - 0.010 * u
        th = 0.0165 * (1 - u) + 0.009 * u
        row = []
        for z in (zc + th / 2, zc - th / 2):
            loc, nrm = hit_front(bvh, x * s, z)
            row.append((loc + nrm * 0.0075, loc - nrm * 0.012))
        pts.append(row)
    bm = bmesh.new(); ring = []
    for (tf, tb), (bf, bb) in pts:
        ring.append([bm.verts.new(p) for p in (tf, bf, bb, tb)])
    for a, b in zip(ring, ring[1:]):
        for k in range(4): bm.faces.new([a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]])
    bm.faces.new(ring[0]); bm.faces.new(ring[-1][::-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    c = sum((v.co for v in bm.verts), V()) / len(bm.verts)
    bmesh.ops.translate(bm, vec=-c, verts=bm.verts[:])
    return obj_from_bm("Brow" + ("L" if s > 0 else "R"), bm, coll, m, c, False)

def shape2d(kind, **k):
    """Плоская фигура в координатах (x, z) относительно центра рта: список граней-списков точек."""
    faces = []
    if kind == "crescent":
        a, top, bot, n = k["a"], k["top"], k["bot"], k.get("n", 12)
        xs = [-a + 2 * a * i / n for i in range(n + 1)]
        T = [(x, top(x)) for x in xs]; B = [(x, bot(x)) for x in xs]
        for i in range(n):
            q = [T[i], T[i + 1], B[i + 1], B[i]]
            if i == 0: q = [T[0], T[1], B[1]]
            elif i == n - 1: q = [T[i], T[i + 1], B[i]]
            faces.append(q)
    elif kind == "ellipse":
        cx, cz, rx, rz, n = k["cx"], k["cz"], k["rx"], k["rz"], k.get("n", 16)
        P = [(cx + rx * math.cos(TAU * i / n), cz + rz * math.sin(TAU * i / n)) for i in range(n)]
        for i in range(n): faces.append([(cx, cz), P[i], P[(i + 1) % n]])
    return faces

def mouth_parts():
    dark, teeth, tongue = "lpMouth", "lpTeeth", "lpTongue"
    S = {}
    a = 0.052
    S["smile"] = [(dark, shape2d("crescent", a=a, top=lambda x: 3.0 * x * x, bot=lambda x: 9.5 * x * x - 0.0176)),
                  (teeth, shape2d("crescent", a=0.033, n=8, top=lambda x: 3.0 * x * x - 0.0016, bot=lambda x: 3.0 * x * x - 0.0016 - 0.0062 * (1 - (x / 0.033) ** 4)))]
    a = 0.066
    S["grin"] = [(dark, shape2d("crescent", a=a, n=14, top=lambda x: 1.2 * x * x + 0.004, bot=lambda x: 8.2 * x * x - 0.0265)),
                 (teeth, shape2d("crescent", a=0.05, n=10, top=lambda x: 1.2 * x * x + 0.0025, bot=lambda x: 1.2 * x * x + 0.0025 - 0.0095 * (1 - (x / 0.05) ** 4))),
                 (tongue, shape2d("ellipse", cx=0, cz=-0.0165, rx=0.022, rz=0.0075, n=14))]
    S["flat"] = [(dark, shape2d("crescent", a=0.042, n=10, top=lambda x: 0.0034 * (1 - (x / 0.042) ** 8) ** 0.5,
                                bot=lambda x: -0.0034 * (1 - (x / 0.042) ** 8) ** 0.5))]
    S["o"] = [(dark, shape2d("ellipse", cx=0, cz=-0.004, rx=0.019, rz=0.025, n=18)),
              (tongue, shape2d("ellipse", cx=0, cz=-0.018, rx=0.011, rz=0.006, n=12))]
    S["frown"] = [(dark, shape2d("crescent", a=0.045, top=lambda x: -8 * x * x + 0.004, bot=lambda x: -4 * x * x - 0.004))]
    def sm_c(x): return 0.12 * x + 3.0 * x * x
    def sm_h(x): return 0.0048 * max(0.0, 1 - ((x - 0.005) / 0.045) ** 2) ** 0.5
    S["smirk"] = [(dark, shape2d("crescent", a=0.045, n=12, top=lambda x: sm_c(x + 0.005) + sm_h(x + 0.005), bot=lambda x: sm_c(x + 0.005) - sm_h(x + 0.005)))]
    return S

def build_mouths(coll, bvh, head_bone_parent):
    mats = {"lpMouth": mat("lpMouth", "5C3438", 0.8), "lpTeeth": mat("lpTeeth", "EFE8D6", 0.7), "lpTongue": mat("lpTongue", "C8646A", 0.8)}
    loc0, _ = hit_front(bvh, 0.0, MOUTH_Z)
    groups = {}
    for mid, parts in mouth_parts().items():
        g = bpy.data.objects.new("Mouth_" + mid, None); g.empty_display_size = 0.03; coll.objects.link(g)
        g.matrix_world = Matrix.Translation(loc0); groups[mid] = g
        for layer, (mname, faces) in enumerate(parts):
            bm = bmesh.new(); cache = {}
            def vert(p):
                key = (round(p[0], 6), round(p[1], 6))
                if key not in cache:
                    loc, n = hit_front(bvh, p[0], MOUTH_Z + p[1])
                    cache[key] = bm.verts.new(loc + n * (0.0022 + 0.0012 * layer))
                return cache[key]
            for f in faces:
                vs = [vert(p) for p in f]
                if len(set(vs)) >= 3:
                    try: bm.faces.new(list(dict.fromkeys(vs)))
                    except ValueError: pass
            bm.normal_update()
            for f in bm.faces:
                if f.normal.y > 0: f.normal_flip()
            bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.008)
            bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
            bmesh.ops.translate(bm, vec=-loc0, verts=bm.verts[:])
            o = obj_from_bm("Mouth_%s__%s" % (mid, "ABC"[layer]), bm, coll, mats[mname], loc0, False)
            o.parent = g; o.matrix_parent_inverse = g.matrix_world.inverted()
    return groups

# ---------------------------------------------------------------- одежда: оболочки поверх тела
class BodyRef:
    """Веса тела для отдельных деталей (галстук, пряжка, обувь): берём у ближайшей вершины тела."""
    def __init__(self, body):
        me = body.data; self.kd = KDTree(len(me.vertices))
        for v in me.vertices: self.kd.insert(v.co, v.index)
        self.kd.balance(); self.w = [{g.group: g.weight for g in v.groups} for v in me.vertices]
    def assign(self, bm):
        dl = bm.verts.layers.deform.verify()
        for v in bm.verts:
            co, i, d = self.kd.find(v.co)
            for g, x in self.w[i].items(): v[dl][g] = x

def fz(f): return f.calc_center_median().z
def flt(f, tl): return sum(v[tl] for v in f.verts) / len(f.verts)

def body_copy(body, keep):
    bm = bmesh.new(); bm.from_mesh(body.data)
    tl = bm.verts.layers.float.get('lt'); pl = bm.faces.layers.int.get('part')
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f, f[pl], tl)], context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')
    return bm, tl, pl

def inflate(bm, off):
    bm.normal_update()
    new = {v: v.co + v.normal * off * min(1.25, v.calc_shell_factor()) for v in bm.verts}
    for v, c in new.items(): v.co = c

def boundary_band(bm, seed, rows):
    """Грани в rows рядов от края, вершины которого проходят seed(v): манжеты, подол."""
    verts = {v for e in bm.edges if e.is_boundary and all(seed(v) for v in e.verts) for v in e.verts}
    faces = set()
    for _ in range(rows):
        new = {f for v in verts for f in v.link_faces} - faces
        faces |= new; verts = {v for f in new for v in f.verts}
    return list(faces)

def drop_faces(bm, faces):
    """Удалить грани и оставшиеся без граней рёбра/вершины."""
    bmesh.ops.delete(bm, geom=[f for f in faces if f.is_valid], context='FACES_ONLY')
    bmesh.ops.delete(bm, geom=[e for e in bm.edges if not e.link_faces], context='EDGES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_edges], context='VERTS')

def region_normals(verts, faces):
    fs = set(faces); out = {}
    for v in verts:
        n = V()
        for f in v.link_faces:
            if f in fs: n += f.normal
        out[v] = n.normalized() if n.length > 0 else v.normal.copy()
    return out

def raise_faces(bm, faces, h, mi=None):
    if not faces: return []
    bm.normal_update()
    r = bmesh.ops.extrude_face_region(bm, geom=faces)
    drop_faces(bm, faces)
    nf = [e for e in r['geom'] if isinstance(e, bmesh.types.BMFace)]
    nv = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bm.normal_update(); rn = region_normals(nv, nf)
    for v in nv: v.co += rn[v] * h
    if mi is not None:
        walls = {f for v in nv for f in v.link_faces}
        for f in walls: f.material_index = mi
    return nf

def lip(bm, depth, mi=None):
    bm.normal_update()
    bnd = [e for e in bm.edges if e.is_boundary]
    if not bnd: return
    nrm = {v: v.normal.copy() for e in bnd for v in e.verts}
    r = bmesh.ops.extrude_edge_only(bm, edges=bnd)
    for v in (e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)):
        src = next((e.other_vert(v) for e in v.link_edges if e.other_vert(v) in nrm and (e.other_vert(v).co - v.co).length < 1e-7), None)
        if src is not None: v.co = src.co - nrm[src] * depth
    if mi is not None:
        for f in (e for e in r['geom'] if isinstance(e, bmesh.types.BMFace)): f.material_index = mi

def finish(bm, name, coll, mats, arm_ob, flat_shade=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats: me.materials.append(m)
    (flat if flat_shade else smooth)(me)
    ob = new_obj(name, me, coll)
    if arm_ob: parent_to_armature(ob, arm_ob)
    return ob

def conform_strip(bvh, pts2d, off, thick, fwd_ray=True):
    """Полоска по поверхности: точки (x, z, ширина) → замкнутая лента толщиной thick над поверхностью."""
    bm = bmesh.new(); rings = []
    for i, (x, z, w) in enumerate(pts2d):
        a = pts2d[max(i - 1, 0)]; b = pts2d[min(i + 1, len(pts2d) - 1)]
        tx, tz = b[0] - a[0], b[1] - a[1]; L = math.hypot(tx, tz) or 1.0
        nx, nz = -tz / L, tx / L                      # поперёк штриха
        row = []
        for k in (-0.5, 0.5):
            loc, n = hit_front(bvh, x + nx * w * k, z + nz * w * k)
            if loc is None: continue
            row.append((loc + n * off, loc + n * (off + thick)))
        if len(row) == 2: rings.append([bm.verts.new(row[0][0]), bm.verts.new(row[1][0]), bm.verts.new(row[1][1]), bm.verts.new(row[0][1])])
    for a, b in zip(rings, rings[1:]):
        for k in range(4): bm.faces.new([a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]])
    if rings:
        bm.faces.new(rings[0]); bm.faces.new(rings[-1][::-1])
    return bm

def tube_bm(path, radii, n=8, up=V((0, 0, 1)), caps=True):
    bm = bmesh.new(); rings = []
    for i, p in enumerate(path):
        t = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)]).normalized()
        rx, ry = radii[i] if isinstance(radii[i], tuple) else (radii[i], radii[i])
        x = (up - t * up.dot(t)).normalized(); y = t.cross(x)
        rings.append([bm.verts.new(p + x * math.cos(TAU * k / n) * ry + y * math.sin(TAU * k / n) * rx) for k in range(n)])
    for a, b in zip(rings, rings[1:]):
        for k in range(n): bm.faces.new([a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]])
    if caps: bm.faces.new(rings[0][::-1]); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm

def thick_ribbon(bm, rows, dirs, t, closed=False):
    """Лента с толщиной: rows — профили (списки точек) вдоль ленты, dirs — куда откладывать толщину."""
    O = [[bm.verts.new(p) for p in r] for r in rows]
    I = [[bm.verts.new(p - d * t) for p in r] for r, d in zip(rows, dirs)]
    n, m = len(rows), len(rows[0])
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        for k in range(m - 1):
            bm.faces.new([O[i][k], O[j][k], O[j][k + 1], O[i][k + 1]])
            bm.faces.new([I[i][k + 1], I[j][k + 1], I[j][k], I[i][k]])
        bm.faces.new([O[i][0], I[i][0], I[j][0], O[j][0]])
        bm.faces.new([O[i][m - 1], O[j][m - 1], I[j][m - 1], I[i][m - 1]])
    if not closed:
        for r in (0, n - 1):
            for k in range(m - 1): bm.faces.new([O[r][k], O[r][k + 1], I[r][k + 1], I[r][k]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm

def bvh_bm(bm):
    c = bm.copy(); t = BVHTree.FromBMesh(c); c.free(); return t

def front_angle(c): return math.degrees(math.atan2(abs(c.x), -c.y))

def top_keep(hem_z, long=True):
    def keep(f, part, tl):
        if part == P_TORSO: return fz(f) > hem_z
        if part == P_ARM: return long or flt(f, tl) < 0.45
        return False
    return keep

def union_bvh(*bms):
    u = bmesh.new()
    for b in bms:
        me = bpy.data.meshes.new("_tmp"); b.to_mesh(me); u.from_mesh(me); bpy.data.meshes.remove(me)
    t = BVHTree.FromBMesh(u); u.free(); return t

def merge_into(dst, src):
    me = bpy.data.meshes.new("_m"); src.to_mesh(me); src.free(); dst.from_mesh(me); bpy.data.meshes.remove(me)

def boundary_loops(bm):
    edges = [e for e in bm.edges if e.is_boundary]; seen = set(); loops = []
    adj = {}
    for e in edges:
        for v in e.verts: adj.setdefault(v, []).append(e.other_vert(v))
    for v0 in adj:
        if v0 in seen: continue
        comp, stack = [], [v0]
        while stack:
            v = stack.pop()
            if v in seen: continue
            seen.add(v); comp.append(v); stack.extend(adj[v])
        loops.append(comp)
    return loops

def neck_edge(bm):
    """Горловина — самая верхняя из центральных дырок оболочки."""
    loops = [l for l in boundary_loops(bm) if abs(sum(v.co.x for v in l) / len(l)) < 0.03]
    top = max(loops, key=lambda l: sum(v.co.z for v in l) / len(l))
    top = sorted(top, key=lambda v: math.atan2(v.co.x, -(v.co.y - 0.006)))
    return [v.co.copy() for v in top]

def with_body_bvh(bm, body):
    bb = bmesh.new(); bb.from_mesh(body.data); t = union_bvh(bm, bb); bb.free(); return t

def conform_grid(bvh, x0, x1, z0, z1, off, thick, nx=6, nz=3):
    """Прямоугольная наклейка по поверхности (принт, текст): сетка nx×nz с толщиной."""
    bm = bmesh.new(); F, B = {}, {}
    for i in range(nx + 1):
        for j in range(nz + 1):
            x = x0 + (x1 - x0) * i / nx; z = z0 + (z1 - z0) * j / nz
            loc, n = hit_front(bvh, x, z)
            if loc is None: return None
            F[i, j] = bm.verts.new(loc + n * (off + thick)); B[i, j] = bm.verts.new(loc + n * off)
    for i in range(nx):
        for j in range(nz):
            bm.faces.new([F[i, j], F[i + 1, j], F[i + 1, j + 1], F[i, j + 1]])
            bm.faces.new([B[i, j + 1], B[i + 1, j + 1], B[i + 1, j], B[i, j]])
    ring = [(i, 0) for i in range(nx)] + [(nx, j) for j in range(nz)] + [(i, nz) for i in range(nx, 0, -1)] + [(0, j) for j in range(nz, 0, -1)]
    for a, b in zip(ring, ring[1:] + ring[:1]): bm.faces.new([F[a], B[a], B[b], F[b]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm

def buttons_bm(bvh, pts, r=0.0075, off=0.0015):
    out = bmesh.new()
    for x, z in pts:
        loc, n = hit_front(bvh, x, z)
        if loc is None: continue
        b = sphere_bm(r, 10, 6); bmesh.ops.scale(b, vec=V((1, 1, 0.4)), verts=b.verts[:])
        q = V((0, 0, 1)).rotation_difference(n)
        bmesh.ops.transform(b, matrix=Matrix.Translation(loc + n * off) @ q.to_matrix().to_4x4(), verts=b.verts[:])
        merge_into(out, b)
    return out

def neck_ribbon(edge_pts, kind):
    """Горловина: shirt — воротник с уголками и разрезом, band — трикотажная резинка, stand — стойка флиски."""
    cz = sum(q.z for q in edge_pts) / len(edge_pts); cy = 0.004
    pts = [(math.atan2(q.x, -(q.y - cy)), q) for q in edge_pts]
    if kind == "shirt": pts = [x for x in pts if abs(x[0]) > 0.2]
    pts.sort(key=lambda x: x[0] % TAU)
    rows, dirs = [], []
    for phi, q in pts:
        fr = max(0.0, math.cos(phi)); radial = V((math.sin(phi), -math.cos(phi), 0))
        p0 = q - radial * 0.004 + V((0, 0, -0.004))
        if kind == "shirt":
            rn = 0.061
            p1 = V((0, cy, cz + 0.030 - 0.010 * fr)) + radial * rn
            p2 = V((0, cy, cz + 0.004 - 0.028 * fr ** 3)) + radial * (rn + 0.034 + 0.012 * fr)
            rows.append([p0, p1, p2])
        elif kind == "band":
            p1 = V((0, cy, cz + 0.016 - 0.004 * fr)) + radial * 0.064
            rows.append([p0, (p0 + p1) / 2 + radial * 0.003 + V((0, 0, 0.002)), p1])
        else:   # stand
            p1 = V((0, cy, cz + 0.050 - 0.006 * fr)) + radial * 0.078
            rows.append([p0, (p0 + p1) / 2 + radial * 0.006, p1])
        dirs.append(radial)
    bm = bmesh.new(); thick_ribbon(bm, rows, dirs, 0.004 if kind == "shirt" else 0.006, closed=(kind != "shirt"))
    return bm

def plaid(bm, tl, pl, mi1, mi2):
    for f in bm.faces:
        c = f.calc_center_median()
        if f[pl] == P_TORSO:
            u = (math.atan2(c.x, -c.y) % TAU) / TAU * 24 + 0.5; v = c.z / 0.045
        elif f[pl] == P_ARM:
            t = min(1.0, max(0.0, flt(f, tl))); s = 1 if c.x > 0 else -1
            ax = mx(SH, s).lerp(mx(WR, s), t); d = c - ax
            u = (math.atan2(d.x * s, -d.y) % TAU) / TAU * 8 + 0.5; v = t * 14
        else: continue
        a = int(math.floor(u)) % 3 == 0; b = int(math.floor(v)) % 3 == 0
        f.material_index = mi2 if (a and b) else (mi1 if (a or b) else 0)

def build_shirt(body, ref, coll, arm, p, base, cuff_m, collar_m, tie_hex=None, pocket=False, check=None, buttons=False, hem=0.905, neck="shirt"):
    mats = [base, cuff_m] + (list(check) if check else [])
    bm, tl, pl = body_copy(body, top_keep(hem)); inflate(bm, 0.006)
    cuffs = boundary_band(bm, lambda v: v[tl] > 0.9, 1)
    edge = neck_edge(bm); bvh = with_body_bvh(bm, body)
    if check: plaid(bm, tl, pl, 2, 3)
    lip(bm, 0.005)
    raise_faces(bm, cuffs, 0.003, mi=1)
    if pocket:
        pk = [f for f in bm.faces if f[pl] == P_TORSO and 1.24 < fz(f) < 1.335 and f.calc_center_median().y < 0
              and 0.045 < f.calc_center_median().x < 0.115]
        raise_faces(bm, pk, 0.0035)
    shirt = finish(bm, p + "Shirt", coll, mats, arm)
    cb = neck_ribbon(edge, neck); ref.assign(cb); finish(cb, p + ("Collar" if neck == "shirt" else "NeckRib"), coll, [collar_m], arm)
    if tie_hex:
        tie = mat("lp_tie_" + tie_hex, tie_hex, 0.85)
        blade = [(0.0, z, 0.030 + (0.058 - 0.030) * (1.445 - z) / (1.445 - 1.17)) for z in [1.445 - 0.275 * i / 10 for i in range(11)]]
        blade += [(0.0, 1.150, 0.034), (0.0, 1.132, 0.006)]
        tb = conform_strip(bvh, blade, 0.0035, 0.0045)
        merge_into(tb, conform_strip(bvh, [(0.0, 1.476, 0.040), (0.0, 1.462, 0.036), (0.0, 1.444, 0.026)], 0.004, 0.012))
        bmesh.ops.recalc_face_normals(tb, faces=tb.faces[:])
        ref.assign(tb); finish(tb, p + "Tie", coll, [tie], arm)
    if buttons:
        bb = buttons_bm(bvh, [(0.0, z) for z in (1.08, 1.17, 1.26, 1.35, 1.43)])
        ref.assign(bb); finish(bb, p + "Buttons", coll, [mat("lpWhite", "F2EFE8", 0.85)], arm, flat_shade=False)
    return shirt, bvh

def build_vest(body, ref, coll, arm, p, m_vest, v_neck=True, off=0.019, hem=0.915, collar=False, zip_m=None, hem_m=None):
    keep = lambda f, part, tl: part == P_TORSO and fz(f) > hem
    bm, tl, pl = body_copy(body, keep); inflate(bm, off)
    if v_neck:      # вырез «V»: режем двумя плоскостями — края ровные, без ступенек
        zb, xt, zt = 1.08, 0.075, 1.49
        for sgn in (1, -1):
            front = [f for f in bm.faces if f.calc_center_median().y < 0 and fz(f) > zb - 0.03]
            geom = list({v for f in front for v in f.verts}) + list({e for f in front for e in f.edges}) + front
            bmesh.ops.bisect_plane(bm, geom=geom, plane_co=V((0, 0, zb)), plane_no=V((zt - zb, 0, -xt * sgn)).normalized())
        drop_faces(bm, [f for f in bm.faces if f.calc_center_median().y < 0 and fz(f) > zb
                        and abs(f.calc_center_median().x) < xt * (fz(f) - zb) / (zt - zb)])
    hemf = boundary_band(bm, lambda v: v.co.z < hem + 0.06, 2) if hem_m is not None else []
    edge = neck_edge(bm) if collar else None
    bvh = bvh_bm(bm)
    lip(bm, off * 0.65)
    if hemf: raise_faces(bm, hemf, 0.005, mi=1)
    mats = [m_vest] + ([hem_m] if hem_m is not None else [])
    vest = finish(bm, p + "Vest", coll, mats, arm)
    if collar and edge:
        cb = neck_ribbon(edge, "stand"); ref.assign(cb); finish(cb, p + "VestCollar", coll, [m_vest], arm)
    if zip_m is not None:
        zb = conform_strip(bvh, [(0.0, z, 0.008) for z in [1.44 - 0.5 * i / 12 for i in range(13)]], 0.0015, 0.002)
        merge_into(zb, conform_grid(bvh, -0.009, 0.009, 1.385, 1.415, 0.002, 0.004, 2, 1))
        ref.assign(zb); finish(zb, p + "Zip", coll, [zip_m], arm)
    if v_neck:
        bb = buttons_bm(bvh, [(0.0, z) for z in (0.975, 1.02, 1.065)], r=0.009)
        ref.assign(bb); finish(bb, p + "Buttons", coll, [mat("lpBrass", "D4A84A", 0.5)], arm, flat_shade=False)
    return vest

def build_tee(body, ref, coll, arm, p, base, dark, decal):
    bm, tl, pl = body_copy(body, top_keep(0.0, long=False)); inflate(bm, 0.016)
    hemf = boundary_band(bm, lambda v: v.co.z < 0.95 and v[tl] < 0.05, 1)
    slv = boundary_band(bm, lambda v: v[tl] > 0.25, 1)
    edge = neck_edge(bm); bvh = bvh_bm(bm)
    lip(bm, 0.012)
    raise_faces(bm, hemf, 0.004); raise_faces(bm, slv, 0.004)
    tee = finish(bm, p + "Tee", coll, [base, dark], arm)
    nb = neck_ribbon(edge, "band"); ref.assign(nb); finish(nb, p + "NeckRib", coll, [dark], arm)
    if decal == "print":            # «</>» на тёмной плашке
        bg = conform_grid(bvh, -0.085, 0.085, 1.185, 1.305, 0.0012, 0.0012, 8, 4)
        ref.assign(bg); finish(bg, p + "PrintBg", coll, [mat("lp_acc_printbg", "1B1F4A", 0.8)], arm)
        yl = bmesh.new()
        for sx in (-1, 1):
            merge_into(yl, conform_strip(bvh, [(0.03 * sx, 1.268, 0.011), (0.058 * sx, 1.245, 0.011), (0.03 * sx, 1.222, 0.011)], 0.0026, 0.0014))
        bmesh.ops.recalc_face_normals(yl, faces=yl.faces[:])
        ref.assign(yl); finish(yl, p + "PrintA", coll, [mat("lp_acc_print", "FFD23F", 0.8)], arm)
        gr = conform_strip(bvh, [(-0.013, 1.217, 0.011), (0.0, 1.245, 0.011), (0.013, 1.273, 0.011)], 0.0026, 0.0014)
        ref.assign(gr); finish(gr, p + "PrintB", coll, [mat("lp_acc_print2", "7BE0B5", 0.8)], arm)
    elif decal == "slogan":         # три строчки «текста»
        ink = bmesh.new()
        for z, words in ((1.300, (0.050, 0.075)), (1.258, (0.040, 0.060, 0.050)), (1.216, (0.085,))):
            total = sum(words) + 0.014 * (len(words) - 1); x = -total / 2
            for w in words:
                g = conform_grid(bvh, x, x + w, z - 0.0105, z + 0.0105, 0.0012, 0.0012, 3, 1)
                if g: merge_into(ink, g)
                x += w + 0.014
        ref.assign(ink); finish(ink, p + "Text", coll, [mat("lp_acc_ink", "1B1F4A", 0.8)], arm)
    return tee

def prof_r(h):
    for (r0, z0), (r1, z1) in zip(HEAD_PROFILE, HEAD_PROFILE[1:]):
        if z0 <= h <= z1: return r0 + (r1 - r0) * (h - z0) / max(1e-6, z1 - z0)
    return 0.0

def head_world(x, y, h):
    """Точка в осях головы (до наклона, h — высота от основания) → мир."""
    return HEAD_BASE + Matrix.Rotation(HEAD_TILT, 3, 'X') @ V((x, y, h))

def head_local(p):
    return Matrix.Rotation(-HEAD_TILT, 3, 'X') @ (V(p) - HEAD_BASE)

def build_hood_up(coll, arm, p, base, dark):
    """Капюшон на голове: жёстко на кости Head (в виде от первого лица прячется вместе с головой)."""
    N = 24; hs = [0.02, 0.07, 0.13, 0.19, 0.25, 0.31, 0.36, 0.40, 0.435, 0.46]
    bm = bmesh.new(); rings = []
    for h in hs:
        R = prof_r(h) + 0.026
        rings.append([bm.verts.new(head_world(math.sin(TAU * k / N) * R * HX(), -math.cos(TAU * k / N) * R * HEAD_SY, h * HZ() + 0.008)) for k in range(N)])
    top = bm.verts.new(head_world(0, 0, (HEAD_PROFILE[-1][1] + 0.028) * HZ()))
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(N): faces.append(bm.faces.new([a[k], a[(k + 1) % N], b[(k + 1) % N], b[k]]))
    for k in range(N): bm.faces.new([rings[-1][k], rings[-1][(k + 1) % N], top])
    hole = []
    for f in faces:
        c = head_local(f.calc_center_median())
        if 0.05 * HZ() < c.z < 0.405 * HZ() and c.y < 0 and front_angle(c) < 60: hole.append(f)
    drop_faces(bm, hole)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.012)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(p + "HoodUp"); bm.to_mesh(me); bm.free(); me.materials.append(base); flat(me)
    ob = bpy.data.objects.new(p + "HoodUp", me); coll.objects.link(ob); parent_to_bone(ob, arm, "Head")
    # горловина-«шарф» между капюшоном и худи: гнётся шеей
    cg = Cage(); prev = None
    for z, rx, ry, yo, w in ((1.585, 0.112, 0.100, -0.012, {"Head": 0.7, "Neck": 0.3}), (1.535, 0.114, 0.100, -0.004, {"Neck": 1.0}),
                             (1.482, 0.122 * TW(), 0.096, 0.006, {"Torso": 0.8, "Neck": 0.2})):
        R = cg.ring(ring_pts((0, yo, z), (0, 0, 1), rx, ry, 16), w)
        if prev: cg.bridge(prev, R, 1, axis=V((0, 0, -1)))
        prev = R
    bmesh.ops.recalc_face_normals(cg.bm, faces=cg.bm.faces[:])
    bmesh.ops.solidify(cg.bm, geom=cg.bm.faces[:], thickness=0.008)
    bm2 = cg.bm; cg.bm = None
    return finish(bm2, p + "Cowl", coll, [dark], arm)

def build_hoodie(body, ref, coll, arm, p, base, dark, hood_up=False):
    white = mat("lpWhite", "F2EFE8", 0.85)
    bm, tl, pl = body_copy(body, top_keep(0.0)); inflate(bm, 0.017)
    hem = boundary_band(bm, lambda v: v.co.z < 1.0 and v[tl] < 0.5, 2)
    cuffs = boundary_band(bm, lambda v: v[tl] > 0.9, 2)
    pocket = [f for f in bm.faces if f[pl] == P_TORSO and 0.955 < fz(f) < 1.07 and f.calc_center_median().y < 0
              and front_angle(f.calc_center_median()) < 50]
    bvh = with_body_bvh(bm, body)
    lip(bm, 0.013)
    raise_faces(bm, hem, 0.005, mi=1); raise_faces(bm, cuffs, 0.005, mi=1)
    raise_faces(bm, [f for f in pocket if f.is_valid], 0.006, mi=1)
    hood = finish(bm, p + "Hoodie", coll, [base, dark], arm)
    if hood_up:
        build_hood_up(coll, arm, p, base, dark)
    else:
        path, radii = [], []
        for i in range(15):
            f = -1 + 2 * i / 14; phi = f * 2.2
            path.append(V((math.sin(phi) * 0.118 * TW(), 0.008 + math.cos(phi) * 0.098, 1.468 + 0.022 * math.cos(phi) ** 2)))
            k = 1 - abs(f) ** 3 * 0.55; radii.append((0.036 * k, 0.029 * k))
        hb = tube_bm(path, radii, 8); ref.assign(hb); finish(hb, p + "Hood", coll, [dark], arm)
    sb = bmesh.new()
    for s in (1, -1):
        pts = [(0.03 * s + 0.004 * s * i / 8, 1.458 - 0.13 * i / 8, 0.0065) for i in range(9)]
        pts += [(0.034 * s, 1.326, 0.009), (0.034 * s, 1.31, 0.009)]
        merge_into(sb, conform_strip(bvh, pts, 0.002, 0.005))
    bmesh.ops.recalc_face_normals(sb, faces=sb.faces[:])
    ref.assign(sb); finish(sb, p + "Strings", coll, [white], arm)
    return hood

def build_turtleneck(body, ref, coll, arm, p, base, dark):
    keep0 = top_keep(0.905)
    keep = lambda f, part, tl: keep0(f, part, tl) or (part == P_NECK and fz(f) < 1.578)
    bm, tl, pl = body_copy(body, keep); inflate(bm, 0.006)
    cuffs = boundary_band(bm, lambda v: v[tl] > 0.9, 1)
    roll = boundary_band(bm, lambda v: v.co.z > 1.5, 2)
    lip(bm, 0.005)
    raise_faces(bm, cuffs, 0.003); raise_faces(bm, roll, 0.008, mi=1)
    return finish(bm, p + "Turtleneck", coll, [base, dark], arm)

def build_top(kind, body, ref, coll, arm, oid, col):
    p = "Top_%s__" % oid
    base = mat("lp_shirt_" + col, col, 0.9); dark = mat("lp_acc_%s_d" % col, shade(col), 0.9)
    white = mat("lpWhite", "F2EFE8", 0.85)
    if kind == "shirt_tie":
        build_shirt(body, ref, coll, arm, p, base, white, white, tie_hex="C8453A", pocket=True)
    elif kind == "vest":
        build_shirt(body, ref, coll, arm, p, base, base, base, tie_hex="3E5BA8")
        build_vest(body, ref, coll, arm, p, mat("lp_acc_vest", "8C6A4F", 0.9))
    elif kind == "flannel":
        build_shirt(body, ref, coll, arm, p, base, dark, base, buttons=True,
                    check=(mat("lp_acc_check", shade(col, 0.62), 0.9), mat("lp_acc_check_x", shade(col, 0.42), 0.9)))
    elif kind == "fleece":
        under = mat("lp_acc_undershirt", "5A5F73", 0.9)
        build_shirt(body, ref, coll, arm, p, under, under, under, neck="band")
        build_vest(body, ref, coll, arm, p, base, v_neck=False, off=0.023, hem=0.88, collar=True,
                   zip_m=mat("lp_acc_zip", "C9CCF0", 0.5), hem_m=dark)
    elif kind == "turtleneck":
        build_turtleneck(body, ref, coll, arm, p, base, dark)
    elif kind in ("tee_print", "tee_slogan"):
        build_tee(body, ref, coll, arm, p, base, dark, "print" if kind == "tee_print" else "slogan")
    elif kind in ("hoodie", "hoodie_up"):
        build_hoodie(body, ref, coll, arm, p, base, dark, hood_up=(kind == "hoodie_up"))

COVERS_BELT = ("hoodie", "hoodie_up", "tee_print", "tee_slogan", "vest", "fleece")

def build_pants(body, ref, coll, arm, oid, col, kind, buckle=True):
    p = "Bottom_%s__" % oid
    base = mat("lp_pants_" + col, col, 0.9); belt = mat("lpBelt", "4A3326", 0.85); dark = mat("lp_acc_%s_d" % col, shade(col), 0.9)
    keep = lambda f, part, tl: (part == P_TORSO and fz(f) < 0.99) or part == P_LEG or (part == P_FOOT and fz(f) > 0.075)
    bm, tl, pl = body_copy(body, keep); inflate(bm, 0.009 if kind != "cargo" else 0.012)
    if kind == "slacks":      # стрелки на брюках: передняя линия чуть вперёд
        for v in bm.verts:
            if 0.14 < v.co.z < 0.80:
                c = V((0.1 if v.co.x > 0 else -0.1, 0.0, v.co.z)); d = (v.co - c); d.z = 0
                if d.length > 0 and math.degrees(math.atan2(abs(d.x), -d.y)) < 12: v.co.y -= 0.004
    cuffs = [f for f in bm.faces if f[pl] in (P_LEG, P_FOOT) and 0.118 < fz(f) < 0.175] if kind == "jeans" else []
    beltf = [f for f in bm.faces if f[pl] == P_TORSO and fz(f) > 0.957] if kind != "cargo" else []
    pockets = []
    if kind == "cargo":       # накладные карманы на бёдрах
        for f in bm.faces:
            if f[pl] != P_LEG: continue
            c = f.calc_center_median(); s = 1 if c.x > 0 else -1
            d = V((c.x - 0.1 * s, c.y, 0))
            if 0.565 < c.z < 0.735 and d.length > 0 and abs(math.degrees(math.atan2(-d.y, d.x * s))) < 38: pockets.append(f)
    lip(bm, 0.006)
    raise_faces(bm, cuffs, 0.006, mi=2); raise_faces(bm, beltf, 0.005, mi=1)
    if pockets:
        top = raise_faces(bm, pockets, 0.008, mi=2)
        flap = [f for f in top if f.is_valid and fz(f) > 0.69]
        raise_faces(bm, flap, 0.004, mi=2)
    bvh = bvh_bm(bm)
    pants = finish(bm, p + {"slacks": "Slacks", "jeans": "Jeans", "cargo": "Cargo"}[kind], coll, [base, belt, dark], arm)
    loc, n = hit_front(bvh, 0.0, 0.978)
    if loc is not None and buckle and kind != "cargo":
        bb = bmesh.new(); bmesh.ops.create_cube(bb, size=1.0)
        bmesh.ops.scale(bb, vec=V((0.048, 0.012, 0.036)), verts=bb.verts[:])
        q = V((0, -1, 0)).rotation_difference(n)
        bmesh.ops.transform(bb, matrix=Matrix.Translation(loc + n * 0.003) @ q.to_matrix().to_4x4(), verts=bb.verts[:])
        ref.assign(bb); finish(bb, p + "Buckle", coll, [mat("lpBrass", "D4A84A", 0.5)], arm)
    return pants

SHOES = {
    "sneakers": [(0.085, 0.036, 0.074), (0.066, 0.047, 0.104), (0.022, 0.052, 0.112), (-0.030, 0.056, 0.096),
                 (-0.080, 0.057, 0.074), (-0.125, 0.053, 0.058), (-0.160, 0.045, 0.047), (-0.184, 0.031, 0.036)],
    "boots": [(0.082, 0.040, 0.130), (0.062, 0.051, 0.158), (0.020, 0.056, 0.160), (-0.030, 0.058, 0.118),
              (-0.080, 0.058, 0.086), (-0.123, 0.054, 0.066), (-0.158, 0.047, 0.053), (-0.180, 0.033, 0.041)]}

def build_shoes(ref, coll, arm, oid, col, kind):
    p = "Shoes_%s__" % oid
    upper = mat("lp_boots_" + col, col, 0.85)
    sole = mat("lp_acc_sole", "E9E4D8", 0.9) if kind == "sneakers" else mat("lp_acc_sole_dark", "1B1520", 0.9)
    lace = mat("lpDark", "2D3052", 0.8) if col.upper() not in ("2E2638", "1B1520") else mat("lpWhite", "F2EFE8", 0.85)
    for sd, s in (("L", 1), ("R", -1)):
        cg = Cage(); rings = []
        for y, w, h in SHOES[kind]:
            pts = [(-w, 0.28 * h), (-0.72 * w, 0.0), (0.72 * w, 0.0), (w, 0.28 * h), (w, 0.72 * h), (0.6 * w, h), (-0.6 * w, h), (-w, 0.72 * h)]
            rings.append([cg.vert(V((0.103 * s + x, y, z)), {"Foot" + sd: 1.0}) for x, z in pts])
        for a, b in zip(rings, rings[1:]):
            for k in range(8): cg.face([a[k], a[(k + 1) % 8], b[(k + 1) % 8], b[k]], 1)
        for r in (rings[0], rings[-1]):
            for q in ((0, 1, 2, 3), (4, 5, 6, 7), (0, 3, 4, 7)): cg.face([r[i] for i in q], 1)
        for e in cg.bm.edges:                      # подошва остаётся плоской и чёткой
            if all(v.co.z < 1e-4 for v in e.verts) or (sum(v.co.z < 1e-4 for v in e.verts) == 1 and abs(e.verts[0].co.y - e.verts[1].co.y) < 1e-4):
                e[cg.cl] = 1.0
        me = cg.mesh(p + "Shoe" + sd); ob = new_obj(p + "Shoe" + sd, me, coll)
        subdivide(ob, 1)
        bm = bmesh.new(); bm.from_mesh(ob.data); bm.normal_update()
        for f in bm.faces: f.material_index = 0
        raise_faces(bm, [f for f in bm.faces if fz(f) < 0.016], 0.0035, mi=1)
        if kind == "sneakers":
            for f in bm.faces:
                c = f.calc_center_median()
                if c.y < -0.142 and c.z < 0.05: f.material_index = 1
        bvh = bvh_bm(bm)
        me2 = bpy.data.meshes.new("_x"); bm.to_mesh(me2); bm.free()
        old = ob.data; ob.data = me2; bpy.data.meshes.remove(old)
        for m in (upper, sole): ob.data.materials.append(m)
        flat(ob.data); parent_to_armature(ob, arm)
        lb = bmesh.new()                            # шнурки поперёк подъёма
        for y in ((-0.050, -0.072, -0.094) if kind == "sneakers" else (-0.02, -0.045, -0.07, -0.095)):
            loc, n, i, d = bvh.ray_cast(V((0.103 * s, y, 0.4)), V((0, 0, -1)))
            if loc is None: continue
            c = bmesh.new(); bmesh.ops.create_cube(c, size=1.0)
            bmesh.ops.scale(c, vec=V((0.05 if kind == "sneakers" else 0.046, 0.009, 0.006)), verts=c.verts[:])
            q = V((0, 0, 1)).rotation_difference(n)
            bmesh.ops.transform(c, matrix=Matrix.Translation(loc + n * 0.0015) @ q.to_matrix().to_4x4(), verts=c.verts[:])
            merge_into(lb, c)
        dl = lb.verts.layers.deform.verify()
        for v in lb.verts: v[dl][GI["Foot" + sd]] = 1.0
        finish(lb, p + "Laces" + sd, coll, [lace], arm)

# ---------------------------------------------------------------- аксессуары (жёстко на кости Head)
def torus_bm(c, axis, R, r, N=28, n=8):
    x, y, z = frame(axis, V((0, 0, 1)))
    bm = bmesh.new(); rings = []
    for i in range(N):
        a = TAU * i / N; d = x * math.cos(a) + y * math.sin(a); cc = c + d * R
        rings.append([bm.verts.new(cc + (d * math.cos(TAU * k / n) + z * math.sin(TAU * k / n)) * r) for k in range(n)])
    for i in range(N):
        a, b = rings[i], rings[(i + 1) % N]
        for k in range(n): bm.faces.new([a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm

def acc_group(coll, arm, name):
    g = bpy.data.objects.new("Acc_" + name, None); g.empty_display_size = 0.03; coll.objects.link(g)
    g.matrix_world = Matrix.Translation(head_world(0, 0, 0.24)); parent_to_bone(g, arm, "Head")
    return g

def acc_part(coll, g, name, bm, m, smooth_shade=False):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free(); me.materials.append(m)
    (smooth if smooth_shade else flat)(me)
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    ob.parent = g; ob.matrix_parent_inverse = g.matrix_world.inverted()
    return ob

def ear_point(head, s):
    vs = [head.matrix_world @ v.co for v in head.data.vertices]
    ez = HEAD_BASE.z + 0.235 * HZ()
    cand = [p for p in vs if abs(p.z - ez) < 0.05 and p.x * s > 0]
    return max(cand, key=lambda p: p.x * s)

def head_shell(head, hmin, off, top_extra=0.0):
    """Оболочка по верху головы (шапки): грани выше hmin в осях головы, раздутые на off."""
    bm = bmesh.new(); bm.from_mesh(head.data); bm.transform(head.matrix_world)
    drop_faces(bm, [f for f in bm.faces if head_local(f.calc_center_median()).z < hmin * HZ()])
    bm.normal_update()
    new = {}
    for v in bm.verts:
        h = head_local(v.co).z; k = max(0.0, (h - hmin * HZ()) / (0.48 * HZ() - hmin * HZ()))
        new[v] = v.co + v.normal * off + Matrix.Rotation(HEAD_TILT, 3, 'X') @ V((0, 0, top_extra * k * k))
    for v, c in new.items(): v.co = c
    return bm

def hat_shell(h_front, h_back, off, N=24, M=8, top_extra=0.0):
    """Купол по форме головы с ровным нижним краем (спереди выше бровей, сзади ниже)."""
    bm = bmesh.new(); rings = []; htop = HEAD_PROFILE[-1][1]
    for j in range(M):
        t = j / M; ring = []
        for k in range(N):
            a = TAU * k / N; h0 = h_back + (h_front - h_back) * (0.5 + 0.5 * math.cos(a))
            h = h0 + (htop - h0) * (1 - (1 - t) ** 1.3)
            R = prof_r(min(h, htop - 1e-4)) + off
            ring.append(bm.verts.new(head_world(math.sin(a) * R * HX(), -math.cos(a) * R * HEAD_SY, h * HZ() + off * 0.4 + top_extra * t * t)))
        rings.append(ring)
    pole = bm.verts.new(head_world(0, 0, htop * HZ() + off + top_extra))
    for a, b in zip(rings, rings[1:]):
        for k in range(N): bm.faces.new([a[k], a[(k + 1) % N], b[(k + 1) % N], b[k]])
    for k in range(N): bm.faces.new([rings[-1][k], rings[-1][(k + 1) % N], pole])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm, rings

def build_accessories(kinds, coll, arm, head, bvh, eyes):
    dark = mat("lpDark", "2D3052", 0.8)
    for kind in kinds:
        if kind == "glasses":
            g = acc_group(coll, arm, "Glasses"); bm = bmesh.new()
            R = EYE_X - 0.003
            for sd, s in (("L", 1), ("R", -1)):
                c = eyes[sd] + V((0, -0.044, 0.002))
                merge_into(bm, torus_bm(c, V((0, -1, 0)), R, 0.0048))
                pts = [c + V((R * s, 0, 0.004))]
                ey = ear_point(head, s).y
                for i in range(1, 7):
                    y = c.y + (ey + 0.02 - c.y) * i / 6
                    loc, n, _, _ = bvh.ray_cast(V((0.6 * s, y, EYE_Z + 0.004)), V((-s, 0, 0)))
                    if loc is not None: pts.append(loc + n * 0.007)
                pts.append(pts[-1] + V((0, 0.012, -0.03)))
                merge_into(bm, tube_bm(pts, [0.0042] * len(pts), 6))
            acc_part(coll, g, "Glasses", bm, dark, smooth_shade=True)
        elif kind == "headphones":
            g = acc_group(coll, arm, "Headphones"); pink = mat("lp_acc_hp", "E0567F", 0.8)
            EL_, ER_ = ear_point(head, 1), ear_point(head, -1)
            C = (EL_ + ER_) / 2; C.z = EL_.z; pts = []
            for i in range(13):
                a = math.pi * i / 12; d = V((math.cos(a), 0, math.sin(a)))
                loc, n, _, _ = bvh.ray_cast(C + d * 0.6, -d)
                if loc is not None: pts.append(loc + d * 0.022)
            pts[0] = EL_ + V((0.032, 0, 0.03)); pts[-1] = ER_ + V((-0.032, 0, 0.03))
            acc_part(coll, g, "HPBand", tube_bm(pts, [(0.026, 0.011)] * len(pts), 8, up=V((0, 1, 0))), dark)
            for E, s in ((EL_, 1), (ER_, -1)):
                cup = tube_bm([E + V((0.012 * s, 0, 0)), E + V((0.024 * s, 0, 0)), E + V((0.05 * s, 0, 0)), E + V((0.058 * s, 0, 0))],
                              [0.05, 0.058, 0.058, 0.045], 16, up=V((0, 0, 1)))
                acc_part(coll, g, "HPCup", cup, pink)
                pad = tube_bm([E + V((-0.004 * s, 0, 0)), E + V((0.014 * s, 0, 0))], [0.046, 0.05], 16, up=V((0, 0, 1)))
                acc_part(coll, g, "HPPad", pad, dark)
        elif kind == "cap":
            g = acc_group(coll, arm, "Cap"); cm = mat("lp_acc_cap_3E5BA8", "3E5BA8", 0.85)
            bm, rings = hat_shell(0.408, 0.345, 0.012)
            bot = rings[0]; N = len(bot); rows, dirs = [], []
            for k in list(range(N - 4, N)) + list(range(0, 5)):          # передняя дуга ±~70°
                a = TAU * k / N; ad = math.degrees((a + math.pi) % TAU - math.pi)
                u = min(1.0, abs(ad) / 75.0); v0 = bot[k].co
                out = Matrix.Rotation(HEAD_TILT, 3, 'X') @ V((math.sin(a) * HX(), -math.cos(a) * HEAD_SY, 0)).normalized()
                L = 0.088 * (1 - u * u) ** 0.5 + 0.006
                tip = v0 + out * L + V((0, 0, -0.012 * (1 - u) - 0.004))
                rows.append([v0 - out * 0.004 + V((0, 0, 0.002)), v0 + out * L * 0.5 + V((0, 0, -0.004 * (1 - u))), tip])
                dirs.append(Matrix.Rotation(HEAD_TILT, 3, 'X') @ V((0, 0, -1)))
            bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=0.006)
            merge_into(bm, thick_ribbon(bmesh.new(), rows, dirs, 0.006))
            btn = sphere_bm(0.013, 10, 6); bmesh.ops.translate(btn, vec=head_world(0, 0, 0.478 * HZ() + 0.016), verts=btn.verts[:])
            merge_into(bm, btn)
            acc_part(coll, g, "Cap", bm, cm)
        elif kind == "beanie":
            g = acc_group(coll, arm, "Beanie")
            bc = mat("lp_acc_beanie_C8453A", "C8453A", 0.95); bd = mat("lp_acc_beanie_d", "8E2F28", 0.95)
            bm, rings = hat_shell(0.405, 0.325, 0.017, top_extra=0.05)
            cuff = [f for f in bm.faces if any(v in rings[0] or v in rings[1] for v in f.verts) and all(v in rings[0] or v in rings[1] or v in rings[2] for v in f.verts)]
            for f in bm.faces: f.material_index = 0
            lip(bm, 0.012)
            raise_faces(bm, cuff, 0.009, mi=1)
            me = bpy.data.meshes.new("Beanie"); bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:]); bm.to_mesh(me); bm.free()
            me.materials.append(bc); me.materials.append(bd); flat(me)
            ob = bpy.data.objects.new("Beanie", me); coll.objects.link(ob); ob.parent = g; ob.matrix_parent_inverse = g.matrix_world.inverted()
            pom = sphere_bm(0.042, 14, 10); bmesh.ops.translate(pom, vec=head_world(0, 0.0, 0.478 * HZ() + 0.078), verts=pom.verts[:])
            acc_part(coll, g, "BeaniePom", pom, mat("lpWhite", "F2EFE8", 0.85))
        elif kind == "crown":
            g = acc_group(coll, arm, "Crown"); gold = mat("lpGold", "F2C23A", 0.45)
            N = 16; h0 = 0.395; rows, dirs = [], []
            for k in range(N + 1):
                a = TAU * k / N; R = prof_r(h0) + 0.012
                d = V((math.sin(a) * HX(), -math.cos(a) * HEAD_SY, 0))
                base = head_world(d.x * R, d.y * R, h0 * HZ())
                mid = head_world(d.x * (R + 0.004), d.y * (R + 0.004), h0 * HZ() + 0.03)
                top = head_world(d.x * (R + 0.008), d.y * (R + 0.008), h0 * HZ() + (0.085 if k % 2 == 0 else 0.045))
                rows.append([base, mid, top]); dirs.append(Matrix.Rotation(HEAD_TILT, 3, 'X') @ d.normalized())
            bm = thick_ribbon(bmesh.new(), rows[:-1], dirs[:-1], 0.008, closed=True)
            for k in range(0, N, 2):
                gem = sphere_bm(0.009, 8, 6); pt = rows[k][1] + dirs[k] * 0.004
                bmesh.ops.translate(gem, vec=pt, verts=gem.verts[:]); merge_into(bm, gem)
            acc_part(coll, g, "Crown", bm, gold)

# ---------------------------------------------------------------- персонажи
OUTFITS3 = {   # верх, низ, обувь, цвета (верх, низ, обувь) — как OUTFITS в v2
    "classic": ("shirt_tie", "slacks", "boots", "92A7D2", "383A68", "2E2638"),
    "junior": ("hoodie", "jeans", "sneakers", "7A6FC4", "4A6FA5", "F2EFE8"),
    "backend": ("flannel", "cargo", "boots", "B8453A", "6B6B4A", "3B2B22"),
    "frontend": ("tee_print", "jeans", "sneakers", "E0567F", "4A6FA5", "2E2638"),
    "teamlead": ("vest", "slacks", "boots", "F2EFE8", "4A4A5E", "3B2B22"),
    "devops": ("fleece", "cargo", "boots", "4FA38A", "4A4A5E", "2E2638"),
    "designer": ("turtleneck", "slacks", "sneakers", "2A2A36", "8C6A4F", "F2EFE8"),
    "qa": ("tee_slogan", "jeans", "sneakers", "F2EFE8", "383A68", "C8453A"),
    "hackathon": ("hoodie_up", "cargo", "sneakers", "E8782F", "2E2638", "F2EFE8"),
}
CHARACTERS = {   # как в v2: цвет кожи, ширина корпуса, форма головы, костюмы, аксессуары
    "Intern": dict(skin="D8B888", tw=1.00, head=(1.00, 1.00), outfits=list(OUTFITS3), acc=["glasses", "headphones", "cap", "beanie", "crown"]),
    "Gena":   dict(skin="CFA77A", tw=1.12, head=(1.05, 0.95), outfits=["teamlead"], acc=["glasses"]),
    "Dev1":   dict(skin="E3C49B", tw=1.00, head=(0.95, 1.08), outfits=["junior"], acc=["headphones"]),
    "Dev2":   dict(skin="A87A52", tw=1.00, head=(1.00, 1.00), outfits=["backend"], acc=["beanie"]),
    "Dev3":   dict(skin="E0C09A", tw=1.00, head=(1.00, 1.00), outfits=["qa"], acc=["cap"]),
    "Dev4":   dict(skin="C49A6C", tw=1.05, head=(1.08, 0.95), outfits=["frontend"], acc=["glasses"]),
}

def trim_body(body):
    """Убираем кожу там, где её всегда закрывает одежда: меньше треугольников и ничего не пролезает сквозь ткань.
    Оставляем плечи у горловины, таз, щиколотки и руки ниже середины плеча (под короткий рукав)."""
    bm = bmesh.new(); bm.from_mesh(body.data)
    tl = bm.verts.layers.float.get('lt'); pl = bm.faces.layers.int.get('part')
    def hidden(f):
        c = f.calc_center_median(); part = f[pl]
        if part == P_TORSO: return 1.0 < c.z < 1.40
        if part == P_LEG: return 0.2 < c.z < 0.8
        if part == P_ARM: return flt(f, tl) < 0.3
        return False
    drop_faces(bm, [f for f in bm.faces if hidden(f)])
    names = [g.name for g in body.vertex_groups]
    bm.to_mesh(body.data); bm.free()
    for n in names:
        if body.vertex_groups.get(n) is None: body.vertex_groups.new(name=n)

def BUILD3(name="Intern", spec=None, x=0.0):
    """Собирает персонажа в коллекции Char_<name>_v3; x — куда поставить в сцене (для экспорта вернуть в 0)."""
    spec = dict(CHARACTERS[name] if spec is None else spec); apply_spec(spec)
    sc = bpy.context.scene
    cname = "Char_%s_v3" % name
    coll = bpy.data.collections.get(cname)
    if coll:
        for o in list(coll.objects): bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new(cname)
    if coll.name not in sc.collection.children: sc.collection.children.link(coll)
    skin = mat("lp_skin_" + spec["skin"], spec["skin"], 0.9)
    body = build_body(coll); body.data.materials.append(skin)
    head = build_head(coll); head.data.materials.append(skin)
    bvh = bvh_of(head); eyes = eye_centers(bvh)
    arm = make_armature(coll, eyes); arm.data.name = "Rig_" + name
    parent_to_armature(body, arm)
    parent_to_bone(head, arm, "Head")
    parts = build_eyes(coll, eyes, skin)
    for n, o in parts.items(): parent_to_bone(o, arm, ("Lid" + n[-1]) if n.startswith("LidMesh") else "Head")
    brow = mat("lpBrow", "3A2A20", 0.9)
    for s in (1, -1): parent_to_bone(brow_obj(coll, bvh, s, brow), arm, "Head")
    for g in build_mouths(coll, bvh, None).values(): parent_to_bone(g, arm, "Head")
    tip = max((v.co for v in head.data.vertices if 1.62 < v.co.z < 1.82), key=lambda c: -c.y)
    nt = bpy.data.objects.new("NoseTip", None); nt.empty_display_size = 0.02; coll.objects.link(nt)
    nt.matrix_world = Matrix.Translation(head.matrix_world @ tip); parent_to_bone(nt, arm, "Head")
    ref = BodyRef(body)
    for oid in spec["outfits"]:
        top, bottom, shoes, ct, cb, cs = OUTFITS3[oid]
        build_top(top, body, ref, coll, arm, oid, ct)
        build_pants(body, ref, coll, arm, oid, cb, bottom, buckle=(top not in COVERS_BELT))
        build_shoes(ref, coll, arm, oid, cs, shoes)
    build_accessories(spec.get("acc", []), coll, arm, head, bvh, eyes)
    trim_body(body)
    for o in coll.objects:        # многоугольники (после разрезов) — сразу в треугольники: Unity отбрасывает «самопересекающиеся»
        if o.type == 'MESH' and any(len(p.vertices) > 4 for p in o.data.polygons):
            bm = bmesh.new(); bm.from_mesh(o.data)
            bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method='BEAUTY', ngon_method='BEAUTY')
            bm.to_mesh(o.data); bm.free()
    arm["character"] = name
    arm.location.x = x
    return arm

def BUILD_ALL(spacing=1.1):
    arms = {}
    for i, n in enumerate(CHARACTERS):
        arms[n] = BUILD3(n, x=i * spacing)
    for n, a in arms.items():
        show_outfit(a, CHARACTERS[n]["outfits"][0]); show_acc(a, None if n == "Intern" else "all")
    return arms

# ---------------------------------------------------------------- просмотр в Blender
def char_objects(arm):
    return [o for o in arm.children_recursive]

def base_name(o): return o.name.split(".")[0]

def show_outfit(arm, oid):
    for o in char_objects(arm):
        n = base_name(o)
        for pre in ("Top_", "Bottom_", "Shoes_"):
            if n.startswith(pre):
                on = n[len(pre):].startswith(oid + "__")
                o.hide_render = not on; o.hide_viewport = not on

def show_acc(arm, which):
    """which: None — ничего, 'all' — всё, что есть, или имя (Glasses, Cap, ...)."""
    for o in char_objects(arm):
        if base_name(o).startswith("Acc_"):
            on = which == "all" or (which is not None and base_name(o) == "Acc_" + which)
            for x in [o] + list(o.children): x.hide_render = not on; x.hide_viewport = not on

def pose3(arm, rots, hips_drop=0.0):
    """Поза как в Unity: у каждой кости вращение в осях родителя (X — вбок, Y — вперёд-назад, Z — вверх)."""
    for pb in arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'; pb.rotation_quaternion = (1, 0, 0, 0); pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    acc = {}
    for b in arm.data.bones:
        pr = acc.get(b.parent.name, Matrix.Identity(3)) if b.parent else Matrix.Identity(3)
        R = Matrix.Identity(3)
        for ax, deg in rots.get(b.name, []): R = R @ Matrix.Rotation(math.radians(deg), 3, ax)
        acc[b.name] = pr @ R
        pb = arm.pose.bones[b.name]
        head = pb.matrix.translation.copy()
        if b.name == "Hips": head.z -= hips_drop
        pb.matrix = Matrix.Translation(head) @ (acc[b.name] @ b.matrix_local.to_3x3()).to_4x4()
        bpy.context.view_layer.update()

def set_face(arm, emo):
    """Эмоция как в Unity (Catalog.Emo*): рот, веки, зрачки, брови. Вызывать в позе покоя."""
    m, tilt, raise_, lid, pk = EMO[emo]
    objs = {base_name(o): o for o in char_objects(arm)}
    for o in char_objects(arm):
        n = base_name(o)
        if n.startswith("Mouth_") and "__" not in n:
            on = n == "Mouth_" + m
            for x in [o] + list(o.children): x.hide_render = not on; x.hide_viewport = not on
    for sd, s in (("L", 1), ("R", -1)):
        b = arm.data.bones["Lid" + sd]; pb = arm.pose.bones["Lid" + sd]
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = (b.matrix_local.to_3x3().inverted() @ Matrix.Rotation(math.radians(165 * lid), 3, 'X') @ b.matrix_local.to_3x3()).to_quaternion()
        objs["Pupil" + sd].scale = (pk, pk, pk)
        br = objs["Brow" + sd]
        if "base_mw" not in br: br["base_mw"] = [list(r) for r in br.matrix_world]
        B = Matrix([list(r) for r in br["base_mw"]])
        extra = 0.02 if (emo == "sly" and s == 1) else 0.0
        R = Matrix.Rotation(math.radians(tilt * s), 3, 'Y') @ B.to_3x3()
        br.matrix_world = Matrix.Translation(B.translation + V((0, 0, raise_ + extra))) @ R.to_4x4()
