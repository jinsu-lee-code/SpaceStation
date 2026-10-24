# 13-6b 이민선 (2026-10-19, 사용자 결정)
#   1차: 셔틀 + 캡슐 묶음 + 피난선 컨테이너를 다 섞음 → "너무 이상" → 컨테이너 · 덧댄 판을 덜어냄
#   2차: "화물선처럼 매끈한 흰색이어야 그을음이 잘 보임 · 얼룩처럼 곳곳에 · 캡슐 때문에 끊긴 느낌 → 일자형 + 캡슐은 좌우 또는 후면 상단" → 후면 상단으로 확정
#   지금: 일자형 흰 동체 하나(코 도킹 고리 → 조종석 유리 → 승객 창 줄 → 엔진 구획) + 생활 캡슐 2개(VARIANT: top = 뒤쪽 위(확정) · sides = 좌우) +
#         선체 곳곳에 번진 그을음 얼룩 = 노이즈로 칠한 색을 UV 텍스처 T_ArkHull로 구움 (면 재질로 칠하면 디지털 위장 무늬처럼 각졌음, MODELING.md 7-11)
# 종족 띠(Band)는 흰 재질 슬롯 → Unity가 재질 이름으로 찾아 종족 색으로 물들임 (MODELING.md 7-4).
# 사용: blender -b --factory-startup -P BlenderWork/ArkShip/arkship_builder.py -- [fbx 출력 폴더] [variant=sides|top]
# 좌표: Blender y = 배 길이(코 0 → 엔진 +), z = 위 → Unity에서 코 = +Z. 원점 = 코 도킹 고리 끝 가운데 (Unity가 도킹 자리에 그 점을 맞춤).
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
VARIANT = next((a.split('=', 1)[1] for a in argv if a.startswith('variant=')), 'top')
OUT = next((a for a in argv if not a.startswith('variant=')), None)

# 미리보기 색 (Unity 재질은 이름으로 바꿔 끼움)
COLORS = {
    'Hull': ((0.9, 0.91, 0.93), 0.55),       # 매끈한 흰 선체
    'Frame': ((0.3, 0.32, 0.36), 0.5),
    'Dark': ((0.05, 0.055, 0.065), 0.2),
    'Band': ((0.55, 0.36, 0.2), 0.35),       # 종족 띠 (미리보기 = 곰 갈색, Unity는 흰색 + 물들임)
    'Glass': ((0.05, 0.09, 0.13), 0.9),
    'Lit': ((1.0, 0.76, 0.42), 0.5),
    'Burn': ((0.06, 0.05, 0.045), 0.05),     # 그을음 진한 겹
    'BurnM': ((0.2, 0.18, 0.16), 0.06),      # 중간 겹
    'BurnL': ((0.48, 0.46, 0.44), 0.1),      # 옅은 겹
    'Vent': ((0.07, 0.075, 0.08), 0.3),
    'Glow': ((0.35, 0.75, 1.0), 0.5),        # 노즐 불빛
    'NavR': ((1.0, 0.12, 0.08), 0.5),
    'NavG': ((0.15, 1.0, 0.3), 0.5),
}
EMIT = {'Lit': 2.5, 'Glow': 4.0, 'NavR': 3.0, 'NavG': 3.0}

bpy.ops.wm.read_factory_settings(use_empty=True)
NAMES = list(COLORS.keys())
MI = {n: i for i, n in enumerate(NAMES)}
MATS = []
for n in NAMES:
    c, smooth = COLORS[n]
    m = bpy.data.materials.new('M_Ark' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    if n in EMIT:
        b.inputs['Emission Color'].default_value = c + (1,); b.inputs['Emission Strength'].default_value = EMIT[n]
    m.diffuse_color = c + (1,)
    MATS.append(m)

PARTS = []
rng = random.Random(31)


def new_bm():
    bm = bmesh.new()
    bm.faces.layers.int.new('m')
    bm.faces.layers.int.new('g')   # 창 묶음 번호 (같은 번호 면은 창 하나로 들임)
    return bm


def orient_outward(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def add(bm):
    orient_outward(bm)
    PARTS.append(bm)
    return bm


def setm(f, bm, name):
    f[bm.faces.layers.int['m']] = MI[name]


def mat_of(bm, f):
    return NAMES[f[bm.faces.layers.int['m']]]


def loft(rings, mat, cap_start=None, cap_end=None, closed=False, group=None):
    """고리 목록을 차례로 잇는 덩어리. mat(k, j) = k번째 띠의 j번째 면 재질, group(k, j) = 창 묶음 번호 (0 = 없음)."""
    bm = new_bm()
    gl = bm.faces.layers.int['g']
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    kn = len(rings) if closed else len(rings) - 1
    for k in range(kn):
        a, b = vs[k], vs[(k + 1) % len(rings)]
        for j in range(n):
            j2 = (j + 1) % n
            f = bm.faces.new((a[j], a[j2], b[j2], b[j]))
            setm(f, bm, mat(k, j))
            if group:
                f[gl] = group(k, j)
    if cap_start:
        setm(bm.faces.new(list(reversed(vs[0]))), bm, cap_start)
    if cap_end:
        setm(bm.faces.new(vs[-1]), bm, cap_end)
    return add(bm)


def inset(bm, names, frame='Frame', thickness=0.02, depth=-0.01):
    """창 · 환기구: 같은 묶음 번호의 면끼리 창 하나로 안으로 들여 틀을 만든다 (MODELING 7-10: 들이기 전 normal_update)."""
    gl = bm.faces.layers.int['g']
    groups = {}
    for f in bm.faces:
        if mat_of(bm, f) in names:
            groups.setdefault(f[gl] if f[gl] else -f.index - 1, []).append(f)
    for faces in groups.values():
        bm.normal_update()
        res = bmesh.ops.inset_region(bm, faces=faces, thickness=thickness, depth=depth, use_even_offset=True)
        for f in res['faces']:
            setm(f, bm, frame)
    orient_outward(bm)


def frame_ring(p, axis, r, seg):
    axis = axis.normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    return [p + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)]


def lathe(origin, axis, profile, mat, seg=16, cap_start=None, cap_end=None, closed=False, group=None):
    """원형 회전체. profile = [(반지름, 축 방향 거리)], mat(k, j) 또는 재질 이름 목록(띠마다)."""
    axis = axis.normalized()
    rings = [frame_ring(origin + axis * d, axis, r, seg) for r, d in profile]
    f = mat if callable(mat) else (lambda k, j: mat[k % len(mat)])
    return loft(rings, f, cap_start, cap_end, closed, group)


def box(center, size, mat, rot=None):
    bm = new_bm()
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    pts = [Vector((sx * hx, sy * hy, sz * hz)) for sx, sy, sz in
           ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
    if rot:
        pts = [rot @ p for p in pts]
    v = [bm.verts.new(center + p) for p in pts]
    for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        setm(bm.faces.new([v[i] for i in idx]), bm, mat)
    return add(bm)


def tube(points, r, mat, seg=8):
    bm = new_bm()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[1] - points[0]) if k == 0 else (points[-1] - points[-2]) if k == n - 1 else (points[k + 1] - points[k - 1])
        rings.append([bm.verts.new(q) for q in frame_ring(p, t, r, seg)])
    for k in range(n - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            setm(bm.faces.new((a[i], a[j], b[j], b[i])), bm, mat)
    setm(bm.faces.new(list(reversed(rings[0]))), bm, mat)
    setm(bm.faces.new(rings[-1]), bm, mat)
    return add(bm)


def slab_xy(pts, z0, z1, mat):
    bm = new_bm()
    lo = [bm.verts.new((x, y, z0)) for x, y in pts]
    hi = [bm.verts.new((x, y, z1)) for x, y in pts]
    n = len(pts)
    setm(bm.faces.new(hi), bm, mat); setm(bm.faces.new(list(reversed(lo))), bm, mat)
    for i in range(n):
        j = (i + 1) % n
        setm(bm.faces.new((lo[i], lo[j], hi[j], hi[i])), bm, mat)
    bm.normal_update()
    return add(bm)


# ---------------- 동체 단면 (모서리가 둥근 사각형 = 초타원, 48점 — 매끈하게) ----------------
NP = 48
W, H = 1.1, 1.0


def squircle(w, h, n=3.2):
    pts = []
    for j in range(NP):
        t = 2 * math.pi * j / NP
        c, s = math.cos(t), math.sin(t)
        pts.append((w / 2 * math.copysign(abs(c) ** (2 / n), c), h / 2 * math.copysign(abs(s) ** (2 / n), s)))
    return pts


SEC = squircle(W, H)


def ring(y, s=1.0, sz=None, dz=0.0):
    sz = s if sz is None else sz
    return [Vector((x * s, y, z * sz + dz)) for x, z in SEC]


def face_angle(j):
    return 2 * math.pi * (j + 0.5) / NP   # 0 오른쪽 · π/2 위 · π 왼쪽 · 3π/2 아래


def low_of(j):
    return 0.5 - 0.5 * math.sin(face_angle(j))


def is_side_window(j):
    """옆 창: 오른쪽(j = 47, 0) · 왼쪽(23, 24) 가운데 높이 두 칸을 창 하나로."""
    return j in (47, 0, 23, 24)


# ================= 일자형 동체 =================
# (y, 폭 배율, 높이 배율, 역할)
BODY = [(0.22, 0.52, 0.5, 'nose'), (0.36, 0.72, 0.7, 'nose'), (0.52, 0.86, 0.84, 'glass'), (0.7, 0.95, 0.94, 'glass'), (0.88, 1.0, 1.0, 'glass'),
        (1.04, 1.0, 1.0, 'band'), (1.18, 1.0, 1.0, 'hull')]
y = 1.18
while y < 3.45:   # 승객 구획: 0.15마다 창 띠 · 선체 띠 번갈아
    y = round(y + 0.15, 3)
    BODY.append((y, 1.0, 1.0, 'hull'))
BODY += [(3.6, 1.0, 1.0, 'band'), (3.74, 1.0, 1.0, 'hull'), (3.9, 1.02, 1.02, 'frame'), (3.98, 1.02, 1.02, 'vent'), (4.16, 1.02, 1.02, 'vent'),
         (4.34, 1.02, 1.02, 'hull'), (4.48, 0.96, 0.96, 'end')]
LIT_WINDOWS = {(1, 0), (2, 1), (4, 0), (5, 1), (6, 0), (7, 0), (7, 1)}   # (창 번호, 0 오른쪽 / 1 왼쪽) — 불 켜진 승객 창


def body_role(k):
    """띠 k(고리 k ~ k+1)의 역할 = 앞 고리의 역할, 승객 구획은 창 띠 · 선체 띠 번갈아."""
    y0 = BODY[k][0]
    role = BODY[k][3]
    if role == 'hull' and 1.33 <= y0 < 3.45:
        idx = round((y0 - 1.33) / 0.15)
        return ('win', idx // 2) if idx % 2 == 0 else ('hull', None)
    return (role, None)


def body_base(k, j):
    role, w = body_role(k)
    a = face_angle(j)
    if role == 'nose': return 'Hull'
    if role == 'glass':
        if math.radians(55) < a < math.radians(125): return 'Glass'        # 조종석 위 유리
        if k == 3 and (is_side_window(j)): return 'Glass'                  # 조종석 옆 작은 창
        return 'Hull'
    if role == 'band': return 'Band'
    if role == 'win' and is_side_window(j):
        return 'Lit' if (w, 0 if j in (47, 0) else 1) in LIT_WINDOWS else 'Glass'
    if role == 'frame': return 'Frame'
    if role == 'vent' and (j in (2, 3, 4, 43, 44, 45, 19, 20, 21, 26, 27, 28)): return 'Vent'
    return 'Hull'


def body_mat(k, j):
    return body_base(k, j)   # 그을음 얼룩은 면 재질이 아니라 구운 텍스처로 (면 단위로 칠하면 디지털 위장 무늬처럼 각졌음)


def body_group(k, j):
    role, w = body_role(k)
    if role == 'glass':
        a = face_angle(j)
        if math.radians(55) < a < math.radians(125):
            return 100 + k * 3 + (0 if a < math.radians(78) else 1 if a < math.radians(102) else 2)   # 띠마다 유리 3장
        return 200 + k * 2 + (0 if j in (47, 0) else 1)
    if role == 'win':
        return 300 + k * 2 + (0 if j in (47, 0) else 1)
    if role == 'vent':
        return 400 + k * 4 + (0 if j < 6 else 1 if j < 24 else 2 if j < 30 else 3)
    return 0


body = loft([ring(y, s, sz) for y, s, sz, _ in BODY], body_mat, cap_start='Frame', cap_end='Frame', group=body_group)
inset(body, ('Lit',), thickness=0.016, depth=-0.012)
inset(body, ('Glass',), thickness=0.022, depth=-0.012)
inset(body, ('Vent',), thickness=0.018, depth=-0.016)

# 코 도킹 고리 (해치) — 동체 앞 뚜껑 속에서 시작 (맞닿는 틈 방지, MODELING 4)
lathe(Vector((0, 0.24, 0)), Vector((0, -1, 0)), [(0.22, -0.03), (0.22, 0.1), (0.28, 0.12), (0.28, 0.2), (0.21, 0.22)], ['Frame', 'Hull', 'Frame', 'Hull'],
      seg=24, cap_start='Frame', cap_end='Dark')
for a in range(4):  # 도킹 고리 걸쇠
    ang = math.pi / 4 + a * math.pi / 2
    box(Vector((math.cos(ang) * 0.26, 0.1, math.sin(ang) * 0.26)), (0.06, 0.12, 0.06), 'Frame', rot=Matrix.Rotation(-ang, 3, 'Y'))

# 안테나 + 접시 (조종석 뒤 위)
tube([Vector((-0.2, 1.3, 0.46)), Vector((-0.2, 1.3, 0.72)), Vector((-0.24, 1.42, 0.86))], 0.02, 'Frame')
lathe(Vector((-0.24, 1.42, 0.86)), Vector((-0.2, 0.35, 0.9)), [(0.016, -0.01), (0.2, 0.065), (0.2, 0.085), (0.016, 0.02)], ['Hull', 'Frame', 'Hull', 'Frame'],
      seg=14, closed=True)

# 엔진: 노즐 2개 (동체 끝 뚜껑 속에서 시작) + 방열 날개 2장
Y_END = BODY[-1][0]
for x in (-0.24, 0.24):
    lathe(Vector((x, 0, 0)), Vector((0, 1, 0)), [(0.18, Y_END - 0.04), (0.18, Y_END + 0.06), (0.22, Y_END + 0.46), (0.19, Y_END + 0.48), (0.12, Y_END + 0.2)],
          ['BurnM', 'Burn', 'Frame', 'Dark'], seg=18, cap_start='Frame', cap_end='Glow')
for sx in (-1, 1):
    x0 = sx * 0.5
    slab_xy([(x0, 3.82), (x0, 4.4), (sx * 1.15, 4.3), (sx * 1.15, 3.98)], -0.025, 0.025, 'Hull')
    slab_xy([(sx * 1.12, 3.96), (sx * 1.12, 4.32), (sx * 1.2, 4.29), (sx * 1.2, 4.0)], -0.04, 0.04, 'Frame')

# ================= 생활 캡슐 2개 =================
CAP_SEG = 24


def capsule(origin, length, radius, lit=(), nav=None, nav_side=1):
    """생활 캡슐: 끝이 둥근 원통, 창 띠(3칸마다 창 · 일부만 불빛) + 앞뒤 종족 띠 + 그을음 얼룩."""
    L, R = length, radius
    prof = [(R * 0.33, 0.0), (R * 0.72, L * 0.045), (R * 0.93, L * 0.115), (R, L * 0.21), (R, L * 0.36), (R, L * 0.44), (R, L * 0.58), (R, L * 0.66),
            (R, L * 0.79), (R * 0.93, L * 0.885), (R * 0.72, L * 0.955), (R * 0.33, L)]
    def mat(k, j):
        if k == 4: return 'Band'
        if k == 5: return ('Lit' if j in lit else 'Glass') if j % 3 == 0 else 'Hull'
        if k == 6: return 'Band'
        return 'Hull'

    bm = lathe(origin, Vector((0, 1, 0)), prof, mat, seg=CAP_SEG, cap_start='Hull', cap_end='Hull')
    inset(bm, ('Lit', 'Glass'), thickness=0.014, depth=-0.01)
    if nav:   # 항법등: 캡슐 앞쪽 바깥면에 반쯤 묻힘
        p = origin + Vector((nav_side * R * 0.83, L * 0.16, 0.0))
        lathe(p, Vector((nav_side, 0, 0)), [(0.035, 0.0), (0.035, 0.06), (0.022, 0.09)], [nav], seg=10, cap_start=nav, cap_end=nav)
    return bm


if VARIANT == 'sides':
    # 좌우: 동체 옆(폭 0.55)에서 0.06 띄워 받침대 2개로 (받침대는 동체 · 캡슐 속에 묻힘)
    R, L, Y0 = 0.34, 1.5, 1.7
    for sx, lit, nav in ((1, {0, 3, 9, 15, 18}, 'NavG'), (-1, {3, 6, 12, 15, 21}, 'NavR')):
        cx = sx * (W / 2 + 0.06 + R)
        capsule(Vector((cx, Y0, -0.08)), L, R, lit=lit, nav=nav, nav_side=sx)
        for y in (Y0 + 0.4, Y0 + 1.1):
            box(Vector((sx * (W / 2 + 0.04), y, -0.08)), (0.14, 0.1, 0.1), 'Frame')
else:
    # 후면 상단: 동체 위(높이 0.5)에 0.05 띄워 두 개 나란히, 안장 받침대
    R, L, Y0 = 0.27, 1.55, 2.25
    for sx, lit, nav in ((1, {0, 9, 18}, 'NavG'), (-1, {3, 12, 21}, 'NavR')):
        cx = sx * 0.29
        capsule(Vector((cx, Y0, H / 2 + 0.05 + R)), L, R, lit=lit, nav=nav, nav_side=sx)
        for y in (Y0 + 0.35, Y0 + 1.15):
            box(Vector((cx, y, H / 2 + 0.06)), (0.12, 0.1, 0.12), 'Frame')

# ---------------- 합치기 · 검사 · 내보내기 ----------------


def finish(name):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = sorted({f[bm.faces.layers.int['m']] for bm in PARTS for f in bm.faces})
    for i in used: me.materials.append(MATS[i])
    big = new_bm()
    lay = big.faces.layers.int['m']
    for bm in PARTS:
        src = bm.faces.layers.int['m']
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf[lay] = f[src]; nf.material_index = used.index(f[src])
        bm.free()
    big.normal_update()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    lo = Vector([min(v.co[i] for v in big.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in big.verts) for i in range(3)])
    # 원점 = 코 끝(도킹 고리) 가운데: x · z는 고리 축(0), y는 가장 앞
    bmesh.ops.translate(big, vec=Vector((0, -lo.y, 0)), verts=big.verts)
    big.to_mesh(me); big.free()
    for p in me.polygons: p.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    except Exception as e:
        print('smooth skipped', e)
    bm = bmesh.new(); bm.from_mesh(me)
    open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    seen = set(); flipped = 0; chunks = 0
    for f0 in bm.faces:
        if f0.index in seen: continue
        stack = [f0]; group = []; seen.add(f0.index)
        while stack:
            f = stack.pop(); group.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        vol = 0.0
        for f in group:
            vs = [v.co for v in f.verts]
            for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
        chunks += 1
        if vol < 0: flipped += 1
    degenerate = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
    bm.free()
    size = hi - lo
    print('ARK %s %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d degenerate %d size %.2f x %.2f x %.2f mats %s'
          % (VARIANT, name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, degenerate, size.x, size.y, size.z, [MATS[i].name for i in used]))
    return ob


ob = finish('ArkShip')

# ---------------- 그을음 얼룩 텍스처 굽기 ----------------
# 면 재질로 칠하면 디지털 위장 무늬처럼 각졌음(사용자: "얼룩 느낌이 나도록 곳곳에") → 흰 선체 색을 노이즈 얼룩으로 칠해 UV 텍스처로 굽는다.
# 노이즈는 오브젝트 좌표 기준이라 UV 섬 경계에서도 이어진다. 아래쪽 · 앞코 · 엔진 쪽에 조금 더 모임. Unity는 M_ArkHull 기본 텍스처로 씀.
TEX = 1024
HULL_TEX = os.path.join(HERE, 'T_ArkHull.png')


def scorch_material():
    m = bpy.data.materials.new('BakeScorch'); m.use_nodes = True
    nt = m.node_tree; N = nt.nodes; Lk = nt.links
    bsdf = next(x for x in N if x.type == 'BSDF_PRINCIPLED')
    tc = N.new('ShaderNodeTexCoord')
    sep = N.new('ShaderNodeSeparateXYZ'); Lk.new(tc.outputs['Object'], sep.inputs[0])

    def noise(scale, detail, rough):
        n = N.new('ShaderNodeTexNoise')
        n.inputs['Scale'].default_value = scale; n.inputs['Detail'].default_value = detail; n.inputs['Roughness'].default_value = rough
        Lk.new(tc.outputs['Object'], n.inputs['Vector'])
        return n.outputs['Fac']

    def math_node(op, a, b, clamp=False):
        n = N.new('ShaderNodeMath'); n.operation = op; n.use_clamp = clamp
        for i, v in enumerate((a, b)):
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = v
            else:
                Lk.new(v, n.inputs[i])
        return n.outputs[0]

    big = noise(1.6, 6.0, 0.62)                        # 큰 얼룩
    fine = noise(7.5, 4.0, 0.55)                       # 가장자리 결
    f = math_node('ADD', big, math_node('MULTIPLY', math_node('SUBTRACT', fine, 0.5), 0.35))
    f = math_node('ADD', f, math_node('MULTIPLY', sep.outputs['Z'], -0.22))                                       # 아래쪽일수록
    f = math_node('ADD', f, math_node('MULTIPLY', math_node('SUBTRACT', 1.0, math_node('DIVIDE', sep.outputs['Y'], 1.4), True), 0.14))   # 앞코
    f = math_node('ADD', f, math_node('MULTIPLY', math_node('DIVIDE', math_node('SUBTRACT', sep.outputs['Y'], 3.3), 1.2, True), 0.16))   # 엔진 쪽
    ramp = N.new('ShaderNodeValToRGB'); Lk.new(f, ramp.inputs['Fac'])
    els = ramp.color_ramp.elements
    els[0].position = 0.0; els[0].color = COLORS['Hull'][0] + (1,)
    els[1].position = 1.0; els[1].color = (0.05, 0.045, 0.04, 1)
    for pos, c in ((0.55, COLORS['Hull'][0]), (0.6, (0.66, 0.64, 0.61)), (0.67, (0.36, 0.33, 0.3)), (0.75, (0.12, 0.11, 0.1))):
        e = els.new(pos); e.color = c + (1,)
    Lk.new(ramp.outputs['Color'], bsdf.inputs['Base Color'])
    img = bpy.data.images.new('T_ArkHull', TEX, TEX, alpha=False)
    tex = N.new('ShaderNodeTexImage'); tex.image = img
    N.active = tex
    return m, img


def bake_hull(src):
    # UV: 모든 면 (창 · 틀은 텍스처를 안 쓰지만 UV가 있어도 됨)
    bpy.context.view_layer.objects.active = src
    for x in bpy.context.view_layer.objects: x.select_set(x is src)
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.006)
    bpy.ops.object.mode_set(mode='OBJECT')
    # 선체 면만 남긴 사본에 얼룩 재질 → 굽기
    hull_slot = next(i for i, m in enumerate(src.data.materials) if m.name == 'M_ArkHull')
    cp = src.copy(); cp.data = src.data.copy(); bpy.context.scene.collection.objects.link(cp)
    bm = bmesh.new(); bm.from_mesh(cp.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != hull_slot], context='FACES')
    for f in bm.faces: f.material_index = 0
    bm.to_mesh(cp.data); bm.free()
    cp.data.materials.clear()
    mat, img = scorch_material(); cp.data.materials.append(mat)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'; sc.cycles.samples = 1
    sc.render.bake.use_pass_direct = False; sc.render.bake.use_pass_indirect = False; sc.render.bake.use_pass_color = True
    sc.render.bake.margin = 6
    for x in bpy.context.view_layer.objects: x.select_set(x is cp)
    bpy.context.view_layer.objects.active = cp
    bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_clear=True)
    img.filepath_raw = HULL_TEX; img.file_format = 'PNG'; img.save()
    bpy.data.objects.remove(cp)
    # 미리보기 재질도 구운 텍스처로
    hm = src.data.materials[hull_slot]
    tex = hm.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = img
    hm.node_tree.links.new(tex.outputs['Color'], next(x for x in hm.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'])
    print('ARK baked', HULL_TEX)


bake_hull(ob)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'arkship.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_ArkShip.fbx'), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                             mesh_smooth_type='FACE')
    import shutil
    shutil.copyfile(HULL_TEX, os.path.join(OUT, 'T_ArkHull.png'))

# 미리보기 (우주 배경 · 해 하나)
sc = bpy.context.scene
for eng_name in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng_name; break
    except TypeError:
        pass
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.02, 0.025, 0.04, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.5
lo_ = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo_); lo_.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))
fill = bpy.data.lights.new('Fill', 'SUN'); fill.energy = 0.6
fo = bpy.data.objects.new('Fill', fill); sc.collection.objects.link(fo); fo.rotation_euler = (math.radians(-60), 0, math.radians(150))
cd = bpy.data.cameras.new('C'); cd.lens = 50; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
mid = Vector((0, 2.5, 0.1))
for tag, d, dist in (('q', Vector((1.0, -0.6, 0.45)), 8.0), ('side', Vector((1, 0.02, 0.1)), 7.5), ('rear', Vector((-0.7, 0.8, 0.35)), 7.5),
                     ('below', Vector((0.6, -0.3, -0.7)), 7.5)):
    d = d.normalized(); cam.location = mid + d * dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x = 900; sc.render.resolution_y = 600
    sc.render.filepath = os.path.join(pv, 'ark_%s_%s.png' % (VARIANT, tag)); bpy.ops.render.render(write_still=True)
