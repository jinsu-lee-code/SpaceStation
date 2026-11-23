# -*- coding: utf-8 -*-
# 14-5 X1c 습격 함대 모델 (2026-11-24): 정찰선 · 호위함 · 기함, 세력 (무법 선단 · 상인 연합 · 학술 함대)마다 같은 틀 + 색 · 장식
#   사용자: "습격해 오는 강대한 세력의 함선이니 위엄이 있게, 기함은 기존 모델링 톤이라도 위협적일 만큼 크게"
#   단위 = 칸 (1 = 셀 하나, GridConfig.CellSize). 기함 약 14칸 · 호위함 약 3칸 · 정찰선 약 1.3칸 (이민선 2.2칸 · 모듈 1칸과 견줌)
#   좌표: Blender y = 배 길이 (코 = -y 쪽), z = 위 → Unity 코 = +Z (이민선과 같음, FBX -Z 앞 · Y 위)
#   공통 언어: 갈라진 이빨 뱃머리 (기함 = 가운데 큰 포 · 호위함 = 감지 눈) · 망치머리 함교 빛 띠 · 작은 창 줄 (크기 단서) · 빨간 항법등
#   세력 구분 = 밝기 + 장식 모양 (색약 배려): 무법 = 어두운 몸 + 가시, 상인 = 중간 밝기 + 컨테이너 · 포드, 학술 = 밝은 몸 + 빛 고리 · 접시
#   모델링 규칙: docs/modeling/00-checklist.md · 04-exterior.md (덩어리마다 부호 있는 부피로 바깥 · 붙는 부품은 몸 속에 묻음 · 같은 평면 겹침 0 ·
#               곡면만 부드러운 음영 · n각형은 삼각형으로 · 작은 구멍 빽빽한 무늬 없음)
# 사용: blender -b --factory-startup -P BlenderWork/RaidFleet/raidfleet_builder.py -- --sheet
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
GIT = os.path.normpath(os.path.join(HERE, '..', '..'))
PROJ = os.path.join(GIT, 'SpaceStation', 'Assets', '_Project')
FONT = os.path.join(PROJ, 'Art', 'Fonts', 'Maplestory-Bold.ttf')
PREVIEW = os.path.join(HERE, 'preview')
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []

SLOTS = ['Hull', 'Plate', 'Trim', 'Accent', 'Glow', 'Eye', 'Nav', 'Gun', 'Dark']
SI = {n: i for i, n in enumerate(SLOTS)}
EMIT = {'Glow': 4.5, 'Eye': 3.0, 'Nav': 5.0}   # 시안 1: 7이면 AgX에서 창이 흰색으로 날아감 (세력 색이 안 보임)

# (선형 색, 거칠기, 금속성). 게임 해는 따뜻하고 앞 위에서만 비침 (04 7-7) → 어두운 몸은 금속성을 낮게 (옆면이 검게 죽지 않게)
PAL = {
    # 시안 1 (11-24): 녹 빛 판이 넓어 주황 배처럼 보였음 → 몸 = 어두운 색이 대부분, 판 = 몸보다 조금 다른 어두운 색, 세력 색은 줄 · 이빨 끝 · 문장에만
    'Outlaw': {
        'Hull': ((0.045, 0.047, 0.055), 0.55, 0.1), 'Plate': ((0.12, 0.05, 0.045), 0.6, 0.05), 'Trim': ((0.16, 0.16, 0.17), 0.5, 0.2),
        'Accent': ((0.80, 0.12, 0.04), 0.5, 0.0), 'Glow': ((1.0, 0.25, 0.06), 0.5, 0.0), 'Eye': ((1.0, 0.18, 0.08), 0.5, 0.0),
        'Nav': ((1.0, 0.10, 0.05), 0.5, 0.0), 'Gun': ((0.09, 0.09, 0.10), 0.45, 0.3), 'Dark': ((0.015, 0.015, 0.018), 0.8, 0.0)},
    'Merchant': {
        'Hull': ((0.06, 0.10, 0.12), 0.5, 0.1), 'Plate': ((0.16, 0.17, 0.19), 0.55, 0.15), 'Trim': ((0.12, 0.12, 0.13), 0.5, 0.2),
        'Accent': ((0.85, 0.55, 0.12), 0.4, 0.5), 'Glow': ((1.0, 0.62, 0.18), 0.5, 0.0), 'Eye': ((1.0, 0.72, 0.30), 0.5, 0.0),
        'Nav': ((1.0, 0.10, 0.05), 0.5, 0.0), 'Gun': ((0.10, 0.10, 0.11), 0.45, 0.3), 'Dark': ((0.015, 0.015, 0.018), 0.8, 0.0)},
    'Scholar': {
        'Hull': ((0.48, 0.54, 0.66), 0.5, 0.05), 'Plate': ((0.06, 0.10, 0.24), 0.5, 0.1), 'Trim': ((0.20, 0.22, 0.28), 0.5, 0.2),
        'Accent': ((0.15, 0.45, 1.0), 0.45, 0.0), 'Glow': ((0.30, 0.90, 1.0), 0.5, 0.0), 'Eye': ((0.50, 0.95, 1.0), 0.5, 0.0),
        'Nav': ((1.0, 0.10, 0.05), 0.5, 0.0), 'Gun': ((0.12, 0.13, 0.16), 0.45, 0.3), 'Dark': ((0.015, 0.015, 0.018), 0.8, 0.0)},
}
FACS = ['Outlaw', 'Merchant', 'Scholar']
FAC_KO = {'Outlaw': '무법 선단', 'Merchant': '상인 연합', 'Scholar': '학술 함대'}

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene


def make_mat(name, rgb, rough, metal=0.0, emit=0.0):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = tuple(rgb) + (1,)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if emit:
        b.inputs['Emission Color'].default_value = tuple(rgb) + (1,)
        b.inputs['Emission Strength'].default_value = emit
    m.diffuse_color = tuple(rgb) + (1,)
    return m


MAT = {f: {s: make_mat('RF_%s_%s' % (f, s), *PAL[f][s], emit=EMIT.get(s, 0.0)) for s in SLOTS} for f in FACS}

# ======================= 도형 도우미 (이민선 빌더와 같은 방식: 조각마다 닫힌 덩어리, 부호 있는 부피로 바깥) =======================
PARTS = []


def new_bm():
    bm = bmesh.new()
    for n in ('m', 'g', 's'):
        bm.faces.layers.int.new(n)
    return bm


def LY(bm, n):
    return bm.faces.layers.int[n]


def setf(bm, f, mat, smooth=0, group=0):
    f[LY(bm, 'm')] = SI[mat]; f[LY(bm, 's')] = smooth; f[LY(bm, 'g')] = group


def signed_volume(faces):
    vol = 0.0
    for f in faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1):
            vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    return vol


def orient(bm):
    if signed_volume(bm.faces) < 0:
        for f in bm.faces:
            f.normal_flip()


def add(bm):
    orient(bm)
    PARTS.append(bm)
    return bm


def loft(rings, mat, cap0=None, cap1=None, closed=False, group=None, smooth=0):
    bm = new_bm()
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    kn = len(rings) if closed else len(rings) - 1
    for k in range(kn):
        a, b = vs[k], vs[(k + 1) % len(rings)]
        for j in range(n):
            j2 = (j + 1) % n
            setf(bm, bm.faces.new((a[j], a[j2], b[j2], b[j])), mat(k, j), smooth, group(k, j) if group else 0)
    if cap0:
        setf(bm, bm.faces.new(list(reversed(vs[0]))), cap0)
    if cap1:
        setf(bm, bm.faces.new(vs[-1]), cap1)
    return add(bm)


def inset(bm, names, frame='Trim', thickness=0.02, depth=-0.01):
    """같은 묶음 번호 면끼리 하나로 (0 = 면마다 따로) 안으로 들여 틀을 만든다 (들이기 전 normal_update, 04 7-12)."""
    gl, ml, sl = LY(bm, 'g'), LY(bm, 'm'), LY(bm, 's')
    idx = {SI[n] for n in names}
    groups = {}
    solo = 0
    for f in bm.faces:
        if f[ml] in idx:
            if f[gl]:
                key = f[gl]
            else:
                solo += 1; key = ('solo', solo)
            groups.setdefault(key, []).append(f)
    for faces in groups.values():
        bm.normal_update()
        res = bmesh.ops.inset_region(bm, faces=faces, thickness=thickness, depth=depth, use_even_offset=True)
        for f in res['faces']:
            f[ml] = SI[frame]; f[sl] = 0; f[gl] = 0
    orient(bm)


def frame_ring(p, axis, r, seg, phase=0.0):
    axis = axis.normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    return [p + (u * math.cos(2 * math.pi * i / seg + phase) + v * math.sin(2 * math.pi * i / seg + phase)) * r for i in range(seg)]


def lathe(origin, axis, profile, mats, seg=16, cap0=None, cap1=None, closed=False, smooth=1, phase=0.0):
    """회전체. profile = [(반지름, 축 방향 거리)], mats = 띠마다 재질 목록 또는 mat(k, j)."""
    axis = axis.normalized()
    rings = [frame_ring(origin + axis * d, axis, r, seg, phase) for r, d in profile]
    f = mats if callable(mats) else (lambda k, j: mats[k % len(mats)])
    return loft(rings, f, cap0, cap1, closed, smooth=smooth)


def cone(base, axis, r, h, mat, seg=5):
    bm = new_bm()
    axis = axis.normalized()
    ring = [bm.verts.new(p) for p in frame_ring(base, axis, r, seg)]
    apex = bm.verts.new(base + axis * h)
    for i in range(seg):
        setf(bm, bm.faces.new((ring[i], ring[(i + 1) % seg], apex)), mat)
    setf(bm, bm.faces.new(list(reversed(ring))), mat)
    return add(bm)


def box(center, size, mat, rot=None):
    bm = new_bm()
    c = Vector(center)
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    pts = [Vector((sx * hx, sy * hy, sz * hz)) for sx, sy, sz in
           ((-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1))]
    if rot:
        pts = [rot @ p for p in pts]
    v = [bm.verts.new(c + p) for p in pts]
    for idx in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
        setf(bm, bm.faces.new([v[i] for i in idx]), mat)
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
            setf(bm, bm.faces.new((a[i], a[j], b[j], b[i])), mat, 1)
    setf(bm, bm.faces.new(list(reversed(rings[0]))), mat)
    setf(bm, bm.faces.new(rings[-1]), mat)
    return add(bm)


def slab_xy(pts, z0, z1, face='Plate', rim='Trim'):
    bm = new_bm()
    lo = [bm.verts.new((x, y, z0)) for x, y in pts]
    hi = [bm.verts.new((x, y, z1)) for x, y in pts]
    n = len(pts)
    setf(bm, bm.faces.new(hi), face); setf(bm, bm.faces.new(list(reversed(lo))), face)
    for i in range(n):
        j = (i + 1) % n
        setf(bm, bm.faces.new((lo[i], lo[j], hi[j], hi[i])), rim)
    bm.normal_update()
    return add(bm)


def slab_yz(pts, x0, x1, face='Plate', rim='Trim'):
    bm = new_bm()
    a = [bm.verts.new((x0, y, z)) for y, z in pts]
    b = [bm.verts.new((x1, y, z)) for y, z in pts]
    n = len(pts)
    setf(bm, bm.faces.new(b), face); setf(bm, bm.faces.new(list(reversed(a))), face)
    for i in range(n):
        j = (i + 1) % n
        setf(bm, bm.faces.new((a[i], a[j], b[j], b[i])), rim)
    bm.normal_update()
    return add(bm)


def ccw(pts):
    n = len(pts)
    a = sum(pts[i][0] * pts[(i + 1) % n][1] - pts[(i + 1) % n][0] * pts[i][1] for i in range(n))
    return list(pts) if a > 0 else list(reversed(pts))


def offset_poly(pts, d):
    """볼록 다각형 (반시계)을 안쪽으로 d만큼."""
    n = len(pts)
    lines = []
    for i in range(n):
        p, q = Vector(pts[i]), Vector(pts[(i + 1) % n])
        e = (q - p).normalized()
        lines.append((p + Vector((-e.y, e.x)) * d, e))
    res = []
    for i in range(n):
        (p1, e1), (p2, e2) = lines[i - 1], lines[i]
        den = e1.x * e2.y - e1.y * e2.x
        if abs(den) < 1e-9:
            res.append(p2); continue
        dp = p2 - p1
        t = (dp.x * e2.y - dp.y * e2.x) / den
        res.append(p1 + e1 * t)
    return [(v.x, v.y) for v in res]


def frustum(plan, z0, z1, d, side='Plate', top='Hull', bottom='Trim', group=None):
    """평면 다각형 (x, y) → 위가 d만큼 좁은 비스듬한 벽 덩어리. side(nx, ny) = 바깥을 보는 방향별 재질."""
    plan = ccw(plan); n = len(plan)
    up = offset_poly(plan, d) if d else plan
    bm = new_bm()
    lo = [bm.verts.new((x, y, z0)) for x, y in plan]
    hi = [bm.verts.new((x, y, z1)) for x, y in up]
    for i in range(n):
        j = (i + 1) % n
        ex, ey = plan[j][0] - plan[i][0], plan[j][1] - plan[i][1]
        ln = math.hypot(ex, ey) or 1.0
        nx, ny = ey / ln, -ex / ln
        m = side(nx, ny) if callable(side) else side
        g = group(nx, ny) if group else 0
        setf(bm, bm.faces.new((lo[i], lo[j], hi[j], hi[i])), m, 0, g)
    setf(bm, bm.faces.new(hi), top)
    setf(bm, bm.faces.new(list(reversed(lo))), bottom)
    return add(bm)


def rotate(bm, R, origin):
    o = Vector(origin)
    for v in bm.verts:
        v.co = o + R @ (v.co - o)


def nav(p, s=0.06):
    box(p, (s, s, s), 'Nav')


# ---------------- 몸통 단면: 위 갑판이 평평한 각진 마름모 14점 ----------------
# 윗옆면을 둘로 나눔 (UPB = 옆 모서리 바로 위 띠 = 창 줄 자리, 위에서도 보임), 옆 모서리 밑 띠 (WIN)는 포대 날개가 붙는 자리
_T = 0.353
UNIT = [(0.42, 1.0), (0.85, 0.55), (0.925, 0.275), (1.0, 0.0), (1.0 - 0.4 * _T, -0.85 * _T), (0.6, -0.85), (0.18, -1.0)]
SLOPE, UPA, UPB, WIN, LOWSIDE, BELLY, KEEL, DECK = (0, 12), (1, 11), (2, 10), (3, 9), (4, 8), (5, 7), (6,), (13,)
UPSIDE = UPA + UPB

# 윗단 (계단식 상부 구조) 단면: 아래 (몸 속) → 가파른 벽 (창 줄) → 비스듬한 모서리 → 평평한 위
TWALL, TCHAM, TTOP, TBOT = (0, 4), (1, 3), (2,), (5,)


def tier(keys, extra, mat_fn, cap0='Trim', cap1='Trim', group_fn=None):
    """keys = [(y, 반폭, 아래 z, 위 z)] — 아래 z는 밑 단 속에 묻힘."""
    ys = sorted({round(k[0], 4) for k in keys} | {round(y, 4) for y in extra if keys[0][0] < y < keys[-1][0]})
    rings = []
    for y in ys:
        w, z0, z1 = interp(keys, y)
        h = z1 - z0
        pts = [(w, z0), (w * 0.97, z1 - 0.3 * h), (w * 0.78, z1), (-w * 0.78, z1), (-w * 0.97, z1 - 0.3 * h), (-w, z0)]
        rings.append([Vector((x, y, z)) for x, z in pts])
    grp = (lambda k, j: group_fn(ys[k], ys[k + 1], k, j)) if group_fn else None
    return loft(rings, lambda k, j: mat_fn(ys[k], ys[k + 1], k, j), cap0, cap1, group=grp)


def window_band(y0, w0, w1, step, j, walls, lit='Eye', seed=3):
    """y0가 창 구간 [w0, w1) 안의 짝수 띠면 창 (가끔 꺼진 창)."""
    if not (w0 - 1e-6 <= y0 < w1 - 1e-6) or j not in walls:
        return None
    i = int(round((y0 - w0) / step))
    if i % 2:
        return None
    return 'Dark' if (i * seed + j) % 7 == 0 else lit


def sec(y, w, zt, zb, cx=0.0, cz=0.0):
    right = [(a * w, b * (zt if b > 0 else zb)) for a, b in UNIT]
    left = [(-x, z) for x, z in reversed(right)]
    return [Vector((cx + x, y, cz + z)) for x, z in right + left]


def base_mat(j):
    if j in DECK or j in SLOPE: return 'Hull'
    if j in UPSIDE or j in BELLY: return 'Plate'
    if j in KEEL: return 'Trim'
    return 'Hull'


def interp(keys, y):
    for a, b in zip(keys, keys[1:]):
        if a[0] - 1e-9 <= y <= b[0] + 1e-9:
            t = (y - a[0]) / (b[0] - a[0]) if b[0] > a[0] else 0.0
            return [p + (q - p) * t for p, q in zip(a[1:], b[1:])]
    return list(keys[-1][1:]) if y > keys[-1][0] else list(keys[0][1:])


def hull(keys, extra, mat_fn, group_fn=None, cap0='Trim', cap1='Trim', cx=0.0, cz=0.0):
    """keys = [(y, 반폭, 위 높이, 아래 깊이[, cx, cz])] 사이를 잇는 몸통. extra = 띠를 더 나눌 y (창 · 줄무늬)."""
    ys = sorted({round(k[0], 4) for k in keys} | {round(y, 4) for y in extra if keys[0][0] < y < keys[-1][0]})
    rings = []
    for y in ys:
        v = interp(keys, y)
        rings.append(sec(y, v[0], v[1], v[2], v[3] if len(v) > 3 else cx, v[4] if len(v) > 4 else cz))
    grp = (lambda k, j: group_fn(ys[k], ys[k + 1], k, j)) if group_fn else None
    return loft(rings, lambda k, j: mat_fn(ys[k], ys[k + 1], k, j), cap0, cap1, group=grp)


def turret(x, y, z, s, barrels=2):
    """포탑: 받침 원통 (몸 속에 묻음) + 앞이 뾰족한 머리 + 포신 (앞 = -y)."""
    lathe(Vector((x, y, z - 0.06 * s)), Vector((0, 0, 1)), [(0.24 * s, 0), (0.24 * s, 0.14 * s), (0.19 * s, 0.18 * s)], ['Trim', 'Plate'],
          seg=12, cap0='Trim', cap1='Trim')
    plan = [(x + a * s, y + b * s) for a, b in ((-0.17, -0.1), (-0.09, -0.26), (0.09, -0.26), (0.17, -0.1), (0.17, 0.16), (-0.17, 0.16))]
    frustum(plan, z + 0.1 * s, z + 0.27 * s, 0.03 * s, side=lambda nx, ny: 'Trim' if ny < -0.5 else 'Plate', top='Hull')
    for o in {1: (0.0,), 2: (-0.06, 0.06), 3: (-0.085, 0.0, 0.085)}[barrels]:
        tube([Vector((x + o * s, y - 0.2 * s, z + 0.185 * s)), Vector((x + o * s, y - 0.72 * s, z + 0.185 * s))], 0.028 * s, 'Gun', seg=6)


def emblem(fac, y, z, R, x):
    """세력 문장 (꼬리 지느러미 양쪽으로 조금 솟음): 무법 = 아래로 향한 세모, 상인 = 마름모, 학술 = 원."""
    if fac == 'Outlaw':
        pts = [(-R * 0.9, R * 0.6), (0.0, -R), (R * 0.9, R * 0.6)]
    elif fac == 'Merchant':
        pts = [(0.0, R), (-R * 0.7, 0.0), (0.0, -R), (R * 0.7, 0.0)]
    else:
        pts = [(R * math.cos(2 * math.pi * i / 14), R * math.sin(2 * math.pi * i / 14)) for i in range(14)]
    slab_yz([(y + a, z + b) for a, b in pts], -x, x, 'Accent', 'Accent')


def nozzle(x, y, z, r, seg=16):
    """엔진 노즐 (몸 끝 속에서 시작, 뒤 = +y): 바깥 → 벌어진 테 → 안쪽 어두운 벽 → 빛나는 바닥."""
    lathe(Vector((x, y, z)), Vector((0, 1, 0)), [(r, 0), (r, 0.3 * r / 0.44), (1.2 * r, 0.85 * r / 0.44), (1.1 * r, 0.9 * r / 0.44), (0.72 * r, 0.62 * r / 0.44)],
          ['Trim', 'Gun', 'Accent', 'Dark'], seg=seg, cap0='Trim', cap1='Glow')


def ring_band(origin, axis, r0, r1, h, seg, mats=('Trim', 'Glow', 'Accent', 'Trim')):
    """납작한 고리 (단면 네모, 닫힌 회전체): 앞면 · 바깥 벽 · 뒷면 · 안쪽 벽."""
    return lathe(origin, axis, [(r0, 0), (r1, 0), (r1, h), (r0, h)], list(mats), seg=seg, closed=True)


def dish(p, axis, R, mats=('Hull', 'Accent', 'Hull', 'Trim'), seg=14):
    lathe(Vector(p), Vector(axis), [(0.08 * R, -0.04 * R), (R, 0.27 * R), (R, 0.36 * R), (0.08 * R, 0.1 * R)], list(mats), seg=seg, closed=True)


# ======================= 기함 (약 14칸) =======================
# 시안 1 (11-24): 낮고 납작해 "긴 배"로 보였고 옆 포대가 쌍동선처럼 떨어져 가벼워 보였음 →
#   몸을 1.5배 높게 · 배 밑 칼날 용골 · 계단 세 단 (단마다 창 줄 = 크기 단서) + 계단식 포탑 둘 · 포대 날개는 두꺼운 판으로 몸에 붙임
def flagship(fac):
    E = 1e-6
    # 1) 몸통: 앞 3 → 뒤 13.2, 윗옆 창 줄 (0.3칸마다), 배 밑 격납고 빛 (칼날 용골 앞), 엔진 앞 환기구, 엔진 구획
    keys = [(3.0, 1.2, .55, .5), (3.6, 1.55, .7, .62), (4.6, 1.9, .82, .72), (6.6, 2.25, .95, .8), (9.0, 2.3, .98, .8),
            (10.4, 2.2, .96, .78), (11.8, 1.95, .9, .72), (12.5, 1.8, .86, .68), (13.2, 1.75, .84, .66)]
    W0, W1, ST = 4.2, 11.4, 0.3
    extra = [3.9, 4.05, 11.55, 11.7] + [W0 + ST * i for i in range(int(round((W1 - W0) / ST)) + 1)]

    def mat(y0, y1, k, j):
        if y0 >= 12.5 - E: return 'Trim'
        if (abs(y0 - 3.9) < 1e-4 or abs(y0 - 11.55) < 1e-4) and j in DECK + SLOPE + UPSIDE: return 'Accent'
        w = window_band(y0, W0, W1, ST, j, UPB)
        if w: return w
        if j in KEEL and 4.8 - E <= y0 < 5.95: return 'Glow'
        if 11.7 - E <= y0 < 12.5 - E and j in UPA: return 'Dark'
        return base_mat(j)

    def grp(y0, y1, k, j):
        if j in KEEL and 4.8 - E <= y0 < 5.95: return 900
        if 11.7 - E <= y0 < 12.5 - E and j in UPA: return 800 + j
        return 0

    body = hull(keys, extra, mat, grp)
    inset(body, ('Eye',), 'Trim', 0.05, -0.02)
    inset(body, ('Dark',), 'Trim', 0.05, -0.02)
    inset(body, ('Glow',), 'Trim', 0.08, -0.06)

    # 2) 갈라진 이빨 뱃머리 둘 (끝 = 세력 색, 살짝 안으로 굽고 끝이 처짐) + 가운데 큰 포 (시간 안에 못 꺾으면 쏘는 '큰 포격')
    pys = [0.0, 0.25, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5]
    pk = [(0.0, .07, .06, .05), (0.5, .26, .22, .19), (1.5, .45, .40, .34), (2.5, .55, .48, .42), (3.5, .6, .52, .46)]
    pcx = lambda y: 0.66 + 0.05 * y
    pcz = lambda y: -0.09 * (1 - y / 3.5)
    for sx in (-1, 1):
        rings = []
        for y in pys:
            w, zt, zb = interp(pk, y)
            rings.append(sec(y, w, zt, zb, sx * pcx(y), pcz(y)))
        loft(rings, lambda k, j: ('Accent' if j in DECK + SLOPE + UPSIDE + WIN else 'Trim') if k < 2 else base_mat(j), 'Trim', 'Trim')
    cannon = lathe(Vector((0, 3.4, -0.02)), Vector((0, -1, 0)),
                   [(0.25, 0.0), (0.25, 0.55), (0.3, 0.6), (0.3, 0.78), (0.22, 0.83), (0.22, 1.25), (0.3, 1.3), (0.3, 1.48), (0.22, 1.53),
                    (0.22, 1.95), (0.33, 2.1), (0.33, 2.35)],
                   ['Gun', 'Trim', 'Accent', 'Trim', 'Gun', 'Trim', 'Accent', 'Trim', 'Gun', 'Trim', 'Gun'], seg=16, cap0='Trim', cap1='Glow')
    inset(cannon, ('Glow',), 'Dark', 0.07, -0.12)

    # 3) 포대 날개 (두꺼운 판으로 몸 옆에 붙음) + 끝 포대 + 포탑 (날개 위 둘 · 포대 위 셋)
    for sx in (-1, 1):
        slab_xy(ccw([(sx * 1.7, 5.2), (sx * 3.3, 6.1), (sx * 3.3, 10.2), (sx * 1.7, 11.0)]), -0.2, 0.02, 'Hull', 'Trim')   # 윗옆 창 줄 아래
        sk = [(5.4, .07, .07, .06), (6.1, .55, .32, .28), (6.6, .62, .34, .30), (9.6, .62, .34, .30), (10.2, .42, .26, .22), (10.6, .2, .14, .12)]

        def smat(y0, y1, k, j):
            if y0 < 6.1 - E: return 'Accent' if j in DECK + SLOPE + UPSIDE + WIN else 'Trim'
            if j in DECK: return 'Hull'
            if j in SLOPE: return 'Plate'
            if j in KEEL + BELLY: return 'Trim'
            return 'Hull'

        hull(sk, [], smat, cx=sx * 3.6)
        for ty in (6.9, 8.1, 9.3):
            turret(sx * 3.6, ty, 0.34, 1.0, 2)
        for ty in (7.2, 9.2):
            turret(sx * 2.6, ty, 0.02, 0.85, 2)
        nav((sx * 3.6, 5.42, 0.0))
        nav((sx * 1.95, 12.2, 0.0))
        turret(sx * 1.05, 4.75, 0.8, 0.8, 2)

    # 4) 배 밑 칼날 용골 (수직 무게)
    slab_yz([(6.0, -0.6), (12.6, -0.55), (12.4, -1.25), (11.2, -1.7), (8.4, -1.55)], -0.12, 0.12, 'Plate', 'Trim')
    nav((0, 11.2, -1.72), 0.07)

    # 5) 계단 세 단 (벽마다 창 줄) + 계단식 큰 포탑 둘 + 망치머리 함교 (가는 빛 띠)
    def tmat(w0, w1, st, front_y, top='Hull', wall='Plate'):
        def f(y0, y1, k, j):
            if y0 < front_y - E: return 'Accent' if j in TCHAM + TTOP else wall
            w = window_band(y0, w0, w1, st, j, TWALL, seed=5)
            if w: return w
            return top if j in TTOP + TCHAM else 'Trim' if j in TBOT else wall
        return f

    t1 = tier([(4.4, .3, .7, .9), (5.2, .95, .7, 1.32), (6.0, 1.15, .72, 1.38), (11.0, 1.15, .74, 1.38), (11.4, .9, .72, 1.3)],
              [4.6] + [6.0 + 0.25 * i for i in range(21)], tmat(6.0, 11.0, 0.25, 4.6))
    t2 = tier([(6.2, .25, 1.28, 1.45), (7.0, .8, 1.28, 1.78), (10.6, .8, 1.28, 1.78), (10.9, .6, 1.28, 1.7)],
              [6.4] + [7.0 + 0.25 * i for i in range(15)], tmat(7.0, 10.6, 0.25, 6.4))
    t3 = tier([(7.9, .2, 1.7, 1.85), (8.4, .55, 1.7, 2.1), (10.4, .55, 1.7, 2.1), (10.6, .4, 1.7, 2.05)],
              [8.05] + [8.4 + 0.25 * i for i in range(9)], tmat(8.4, 10.4, 0.25, 8.05))
    for t in (t1, t2, t3):
        inset(t, ('Eye', 'Dark'), 'Trim', 0.045, -0.02)
    turret(0.0, 5.9, 1.36, 1.7, 3)
    turret(0.0, 7.35, 1.77, 1.3, 2)
    frustum([(0, 8.8), (0.32, 9.0), (0.32, 10.0), (0, 10.25), (-0.32, 10.0), (-0.32, 9.0)], 2.0, 2.65, 0.06, side='Plate', top='Trim')
    # 함교 머리: 시안 1의 납작한 판 + 넓은 빛 띠는 "모자"처럼 보였음 → 더 두껍게 · 앞만 가는 눈 틈 (틀 두께로 좁힘)
    head = frustum([(0.0, 8.55), (0.95, 8.85), (1.1, 9.2), (0.8, 9.85), (-0.8, 9.85), (-1.1, 9.2), (-0.95, 8.85)], 2.6, 2.98, 0.05,
                   side=lambda nx, ny: 'Eye' if ny < -0.25 else 'Plate', top='Hull', group=lambda nx, ny: 700 if ny < -0.25 else 0)
    inset(head, ('Eye',), 'Hull', 0.13, -0.03)
    tube([Vector((0, 9.5, 2.95)), Vector((0, 9.55, 3.5))], 0.035, 'Trim', seg=6)
    nav((0, 9.55, 3.53))

    # 6) 꼬리 지느러미 + 세력 문장
    slab_yz([(10.3, 1.2), (13.0, 0.55), (13.1, 1.1), (12.3, 2.7), (11.7, 2.75)], -0.06, 0.06, 'Plate', 'Trim')
    emblem(fac, 12.05, 1.95, 0.4, 0.075)
    nav((0, 12.0, 2.76))

    # 7) 엔진 셋
    nozzle(0.0, 13.05, -0.05, 0.5)
    for sx in (-1, 1):
        nozzle(sx * 1.05, 13.05, 0.05, 0.38)

    # 8) 세력 장식
    if fac == 'Merchant':   # 포대 날개 위 · 뒤 갑판 컨테이너 + 포대 밑 화물 포드
        for sx in (-1, 1):
            for i, y in enumerate((6.2, 8.2, 10.1)):
                box((sx * 2.6, y, 0.15), (0.55, 0.5, 0.3), 'Accent' if (i + (sx > 0)) % 2 == 0 else 'Plate')
            for i, y in enumerate((11.75, 12.25)):
                box((sx * 0.55, y, 1.0), (0.5, 0.44, 0.3), 'Accent' if (i + (sx > 0)) % 2 == 0 else 'Plate')
            lathe(Vector((sx * 3.6, 6.5, -0.5)), Vector((0, 1, 0)),
                  [(0.08, 0), (0.18, 0.08), (0.22, 0.24), (0.22, 0.9), (0.22, 1.0), (0.22, 1.8), (0.22, 1.9), (0.22, 2.6), (0.18, 2.76), (0.08, 2.84)],
                  ['Plate', 'Plate', 'Plate', 'Accent', 'Plate', 'Accent', 'Plate', 'Plate', 'Plate'], seg=14, cap0='Trim', cap1='Trim')
    elif fac == 'Scholar':  # 함교 둘레 빛 고리 + 포대 앞 감지 접시 + 지느러미 안테나
        ring_band(Vector((0, 9.4, 2.28)), Vector((0, 0, 1)), 1.3, 1.46, 0.08, 32, ('Trim', 'Glow', 'Accent', 'Trim'))
        for a in (45, 135, 225, 315):
            c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
            tube([Vector((c * 0.25, 9.4 + s * 0.25, 2.32)), Vector((c * 1.34, 9.4 + s * 1.34, 2.32))], 0.03, 'Trim', seg=6)
        for sx in (-1, 1):
            tube([Vector((sx * 3.6, 6.15, 0.25)), Vector((sx * 3.6, 6.02, 0.5))], 0.03, 'Trim', seg=6)
            dish((sx * 3.6, 6.0, 0.52), (0, -1, 0.35), 0.3)
        tube([Vector((0, 11.95, 2.7)), Vector((0, 12.05, 3.3))], 0.022, 'Trim', seg=6)
        tube([Vector((0, 12.25, 2.65)), Vector((0, 12.45, 3.1))], 0.022, 'Trim', seg=6)
        nav((0, 12.05, 3.32), 0.05)
    else:                   # 무법: 이빨 바깥 가시 · 포구 앞 안쪽 송곳니 · 포대 앞 충각 · 함교 뿔 · 용골 이빨
        for sx in (-1, 1):
            for y in (0.7, 1.5, 2.3):
                w, zt, zb = interp(pk, y)
                cone(Vector((sx * (pcx(y) + w * 0.93), y, pcz(y))), Vector((sx * 0.75, -0.6, 0.1)), 0.09, 0.45, 'Trim')
            for y in (0.35, 0.75):
                w, zt, zb = interp(pk, y)
                cone(Vector((sx * (pcx(y) - w * 0.9), y, pcz(y))), Vector((-sx, -0.25, 0)), 0.05, 0.18, 'Trim')
            cone(Vector((sx * 3.6, 5.5, 0.0)), Vector((0, -1, 0)), 0.13, 0.8, 'Trim', seg=4)
            cone(Vector((sx * 1.05, 9.2, 2.73)), Vector((sx * 0.7, 0.4, 0.5)), 0.08, 0.55, 'Trim')
        cone(Vector((0, 12.0, 2.7)), Vector((0, 0.7, 0.5)), 0.07, 0.45, 'Trim')
        for y, z in ((9.2, -1.6), (10.4, -1.66)):
            cone(Vector((0, y, z + 0.05)), Vector((0, -0.3, -1)), 0.07, 0.3, 'Trim', seg=4)
    return finish('Flagship_' + fac, fac)


# ======================= 호위함 (약 3칸) =======================
def escort(fac):
    E = 1e-6
    keys = [(0.8, .3, .15, .13), (1.1, .44, .2, .17), (1.5, .55, .23, .19), (2.3, .6, .24, .19), (2.7, .52, .22, .18), (2.95, .46, .21, .17)]
    W0, W1, ST = 1.2, 2.6, 0.1
    extra = [1.0, 1.06, 2.75] + [W0 + ST * i for i in range(int(round((W1 - W0) / ST)) + 1)]

    def mat(y0, y1, k, j):
        if y0 >= 2.75 - E: return 'Trim'
        if abs(y0 - 1.0) < 1e-4 and j in DECK + SLOPE + UPSIDE: return 'Accent'
        w = window_band(y0, W0, W1, ST, j, UPB)
        if w: return w
        return base_mat(j)

    body = hull(keys, extra, mat)
    inset(body, ('Eye', 'Dark'), 'Trim', 0.014, -0.006)

    pys = [0.0, 0.15, 0.3, 0.45, 0.6, 0.75, 1.0]
    pk = [(0.0, .025, .022, .02), (0.15, .07, .06, .05), (0.45, .12, .1, .09), (0.75, .14, .12, .1), (1.0, .15, .125, .11)]
    pcx = lambda y: 0.19 + 0.04 * y
    pcz = lambda y: -0.025 * (1 - y)
    for sx in (-1, 1):
        rings = []
        for y in pys:
            w, zt, zb = interp(pk, y)
            rings.append(sec(y, w, zt, zb, sx * pcx(y), pcz(y)))
        loft(rings, lambda k, j: ('Accent' if j in DECK + SLOPE + UPSIDE + WIN else 'Trim') if k < 2 else base_mat(j), 'Trim', 'Trim')
    eye = lathe(Vector((0, 0.85, 0)), Vector((0, -1, 0)), [(0.05, 0), (0.05, 0.18), (0.062, 0.21), (0.04, 0.24)], ['Gun', 'Trim', 'Gun'],
                seg=12, cap0='Trim', cap1='Eye')
    inset(eye, ('Eye',), 'Dark', 0.008, -0.01)

    for sx in (-1, 1):   # 포 날개 (판으로 몸에 붙음) + 끝 포 꼬투리 + 앞 포신
        slab_xy(ccw([(sx * 0.45, 1.35), (sx * 0.75, 1.6), (sx * 0.75, 2.3), (sx * 0.45, 2.5)]), -0.06, 0.0, 'Hull', 'Trim')
        pk2 = [(1.2, .03, .03, .025), (1.42, .12, .08, .07), (2.25, .14, .09, .08), (2.5, .07, .055, .045)]
        hull(pk2, [], lambda y0, y1, k, j: ('Accent' if j in DECK + SLOPE + UPSIDE + WIN else 'Trim') if y0 < 1.42 - E else
             ('Hull' if j in DECK + SLOPE else 'Trim' if j in KEEL + BELLY else 'Plate'), cx=sx * 0.82)
        tube([Vector((sx * 0.82, 1.3, 0.0)), Vector((sx * 0.82, 0.88, 0.0))], 0.028, 'Gun', seg=6)
        nav((sx * 0.82, 2.51, 0.0), 0.025)

    def tm(y0, y1, k, j):
        if y0 < 1.1 - E: return 'Accent' if j in TCHAM + TTOP else 'Plate'
        w = window_band(y0, 1.3, 2.5, 0.08, j, TWALL, seed=5)
        if w: return w
        return 'Hull' if j in TTOP + TCHAM else 'Trim' if j in TBOT else 'Plate'

    t = tier([(0.95, .08, .14, .2), (1.3, .25, .17, .34), (2.5, .25, .19, .34), (2.62, .18, .19, .32)], [1.1] + [1.3 + 0.08 * i for i in range(16)], tm)
    inset(t, ('Eye', 'Dark'), 'Trim', 0.012, -0.005)
    turret(0.0, 1.45, 0.335, 0.5, 2)
    frustum([(0, 1.86), (0.08, 1.9), (0.08, 2.18), (0, 2.24), (-0.08, 2.18), (-0.08, 1.9)], 0.32, 0.45, 0.01, side='Plate', top='Trim')
    head = frustum([(0, 1.8), (0.25, 1.88), (0.29, 1.98), (0.22, 2.16), (-0.22, 2.16), (-0.29, 1.98), (-0.25, 1.88)], 0.43, 0.54, 0.012,
                   side=lambda nx, ny: 'Eye' if ny < -0.25 else 'Plate', top='Hull', group=lambda nx, ny: 700 if ny < -0.25 else 0)
    inset(head, ('Eye',), 'Hull', 0.036, -0.008)

    slab_yz([(2.3, 0.26), (2.92, 0.15), (2.98, 0.26), (2.78, 0.6), (2.62, 0.61)], -0.016, 0.016, 'Plate', 'Trim')
    emblem(fac, 2.73, 0.42, 0.08, 0.022)
    nav((0, 2.7, 0.61), 0.025)
    slab_yz([(1.5, -0.12), (2.7, -0.12), (2.6, -0.3), (2.25, -0.4), (1.8, -0.36)], -0.02, 0.02, 'Plate', 'Trim')   # 작은 칼날 용골
    for sx in (-1, 1):
        nozzle(sx * 0.2, 2.88, 0.0, 0.12, seg=12)

    if fac == 'Merchant':
        for sx in (-1, 1):
            box((sx * 0.15, 2.8, 0.25), (0.13, 0.18, 0.1), 'Accent' if sx > 0 else 'Plate')
            box((sx * 0.6, 2.0, 0.03), (0.12, 0.26, 0.09), 'Plate' if sx > 0 else 'Accent')
            lathe(Vector((sx * 0.82, 1.5, -0.12)), Vector((0, 1, 0)), [(0.02, 0), (0.05, 0.04), (0.05, 0.35), (0.05, 0.43), (0.05, 0.8), (0.02, 0.84)],
                  ['Plate', 'Plate', 'Accent', 'Plate', 'Plate'], seg=10, cap0='Trim', cap1='Trim')
    elif fac == 'Scholar':
        ring_band(Vector((0, 1.73, 0)), Vector((0, 1, 0)), 1.02, 1.08, 0.06, 36, ('Accent', 'Glow', 'Accent', 'Trim'))
        for sx in (-1, 1):
            tube([Vector((sx * 0.94, 1.76, 0.0)), Vector((sx * 1.04, 1.76, 0.0))], 0.012, 'Trim', seg=6)
            for sz in (-1, 1):
                tube([Vector((sx * 0.15, 1.76, sz * 0.14)), Vector((sx * 0.52, 1.76, sz * 0.88))], 0.012, 'Trim', seg=6)
    else:
        for sx in (-1, 1):
            for y in (0.3, 0.65):
                w, zt, zb = interp(pk, y)
                cone(Vector((sx * (pcx(y) + w * 0.93), y, pcz(y))), Vector((sx * 0.8, -0.55, 0)), 0.03, 0.16, 'Trim')
            cone(Vector((sx * 0.95, 1.9, 0.0)), Vector((sx, -0.3, 0)), 0.03, 0.14, 'Trim')
            cone(Vector((sx * 0.28, 2.0, 0.48)), Vector((sx * 0.7, 0.4, 0.55)), 0.02, 0.14, 'Trim')
    return finish('Escort_' + fac, fac)


# ======================= 정찰선 (약 1.3칸) =======================
def scout(fac):
    keys = [(0.0, .02, .015, .012), (0.18, .07, .04, .032), (0.5, .12, .06, .045), (0.85, .13, .065, .05), (1.1, .11, .058, .045), (1.2, .095, .052, .04)]
    E = 1e-6

    def mat(y0, y1, k, j):
        if y0 < 0.18 - E: return 'Trim'
        if abs(y0 - 0.3) < 1e-4 and j in DECK + SLOPE + UPSIDE: return 'Accent'
        if y0 >= 1.1 - E: return 'Trim'
        return base_mat(j)

    hull(keys, [0.3, 0.34], mat)
    canopy = frustum([(0, 0.26), (0.045, 0.36), (0.05, 0.55), (-0.05, 0.55), (-0.045, 0.36)], 0.03, 0.085, 0.012,
                     side=lambda nx, ny: 'Eye' if ny < -0.3 else 'Plate', top='Hull', group=lambda nx, ny: 700 if ny < -0.3 else 0)
    inset(canopy, ('Eye',), 'Trim', 0.006, -0.004)
    for sx in (-1, 1):   # 아래로 꺾인 날개 (끝 = 세력 색, 끝 조각이 조금 두꺼워 같은 평면이 생기지 않음)
        R = Matrix.Rotation(math.radians(sx * 12), 3, 'Y')
        o = (sx * 0.06, 0.0, 0.0)
        wing = slab_xy(ccw([(sx * 0.06, 0.48), (sx * 0.5, 0.92), (sx * 0.52, 1.06), (sx * 0.06, 1.0)]), -0.012, 0.012, 'Plate', 'Trim')
        rotate(wing, R, o)
        # 끝 조각 테두리가 날개 테두리와 같은 평면에 겹치지 않게 바깥으로 4mm 키움 (안쪽 자른 변도 같이 들어가 날개 속)
        tip = slab_xy(offset_poly(ccw([(sx * 0.4, 0.82), (sx * 0.5, 0.92), (sx * 0.52, 1.06), (sx * 0.41, 1.0457)]), -0.004), -0.017, 0.017, 'Accent', 'Accent')
        rotate(tip, R, o)
        p = Vector(o) + R @ (Vector((sx * 0.515, 1.0, 0.0)) - Vector(o))
        nav(p, 0.022)
        a = Vector(o) + R @ (Vector((sx * 0.25, 0.78, -0.022)) - Vector(o))
        tube([a, a + Vector((0, -0.3, 0))], 0.012, 'Gun', seg=6)
    nozzle(0.0, 1.15, 0.0, 0.05, seg=12)

    if fac == 'Merchant':
        lathe(Vector((0, 0.45, -0.07)), Vector((0, 1, 0)), [(0.015, 0), (0.035, 0.04), (0.035, 0.25), (0.035, 0.32), (0.035, 0.5), (0.015, 0.55)],
              ['Plate', 'Plate', 'Accent', 'Plate', 'Plate'], seg=10, cap0='Trim', cap1='Trim')
    elif fac == 'Scholar':
        tube([Vector((0, 0.78, 0.04)), Vector((0, 0.74, 0.12))], 0.008, 'Trim', seg=6)
        dish((0, 0.735, 0.125), (0, -0.6, 0.8), 0.07)
        for sx in (-1, 1):
            tube([Vector((sx * 0.03, 0.22, 0.02)), Vector((sx * 0.05, -0.12, 0.02))], 0.005, 'Trim', seg=5)
    else:
        cone(Vector((0, 0.03, 0)), Vector((0, -1, 0)), 0.014, 0.12, 'Trim', seg=4)
        for sx in (-1, 1):
            R = Matrix.Rotation(math.radians(sx * 12), 3, 'Y')
            o = Vector((sx * 0.06, 0.0, 0.0))
            for lx, ly in ((0.22, 0.64), (0.38, 0.80)):
                b = o + R @ (Vector((sx * lx, ly, 0.0)) - o)
                cone(b, Vector((sx * 0.4, -0.9, 0.0)), 0.015, 0.09, 'Trim', seg=4)
    return finish('Scout_' + fac, fac)


# ======================= 합치기 · 검사 =======================
def coplanar_hits(big, cl, eps=0.002):
    """서로 다른 조각의 같은 방향 같은 평면 겹침 (보이는 깜빡임 후보)."""
    buckets = {}
    for f in big.faces:
        n = f.normal
        if n.length < 0.5:
            continue
        p = [v.co.copy() for v in f.verts]
        d = n.dot(p[0])
        nk = (round(n.x * 25), round(n.y * 25), round(n.z * 25))
        buckets.setdefault(nk, {}).setdefault(round(d / 0.006), []).append((f[cl], n.copy(), d, p))
    hits = []

    def overlap(t1, t2, n):
        u = n.orthogonal().normalized(); v = n.cross(u)
        A = [(q.dot(u), q.dot(v)) for q in t1]; B = [(q.dot(u), q.dot(v)) for q in t2]
        for poly in (A, B):
            for i in range(3):
                ex, ey = poly[(i + 1) % 3][0] - poly[i][0], poly[(i + 1) % 3][1] - poly[i][1]
                ax, ay = -ey, ex
                la = math.hypot(ax, ay)
                if la < 1e-12:
                    return False
                pa = [(x * ax + y * ay) / la for x, y in A]; pb = [(x * ax + y * ay) / la for x, y in B]
                if min(max(pa), max(pb)) - max(min(pa), min(pb)) <= eps:
                    return False
        return True

    for nk, dd in buckets.items():
        for dk, lst in dd.items():
            cand = lst + dd.get(dk + 1, [])
            for i, a in enumerate(lst):
                for b in cand[i + 1:]:
                    if a[0] == b[0] or abs(a[2] - b[2]) > 0.003 or a[1].dot(b[1]) < 0.995:
                        continue
                    if overlap(a[3], b[3], a[1]):
                        hits.append(sum(a[3], Vector()) / 3)
    return hits


def finish(name, fac):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    SC.collection.objects.link(ob)
    for s in SLOTS:
        me.materials.append(MAT[fac][s])
    big = bmesh.new()
    cl = big.faces.layers.int.new('c'); sl = big.faces.layers.int.new('s')
    for ci, bm in enumerate(PARTS):
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        ml0, sl0 = LY(bm, 'm'), LY(bm, 's')
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts])
            nf.material_index = f[ml0]; nf[cl] = ci; nf[sl] = f[sl0]
        bm.free()
    n_parts = len(PARTS)
    PARTS.clear()
    big.normal_update()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    big.normal_update()
    open_ = sum(1 for e in big.edges if len(e.link_faces) == 1)
    nonman = sum(1 for e in big.edges if len(e.link_faces) > 2)
    degenerate = sum(1 for f in big.faces if f.calc_area() < 1e-9)
    chunks = {}
    for f in big.faces:
        chunks.setdefault(f[cl], []).append(f)
    flipped = sum(1 for fs in chunks.values() if signed_volume(fs) < 0)
    hits = coplanar_hits(big, cl)
    lo = Vector([min(v.co[i] for v in big.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in big.verts) for i in range(3)])
    c = (lo + hi) / 2
    bmesh.ops.translate(big, vec=-c, verts=big.verts)
    smooth = [f[sl] for f in big.faces]
    tris = len(big.faces)
    big.to_mesh(me); big.free()
    for p, s in zip(me.polygons, smooth):
        p.use_smooth = bool(s)
    size = hi - lo
    print('RAID %-18s parts %3d tris %6d open %d nonmanifold %d flipped %d degenerate %d coplanar %d size %.2f x %.2f x %.2f'
          % (name, n_parts, tris, open_, nonman, flipped, degenerate, len(hits), size.x, size.y, size.z))
    for h in hits[:6]:
        print('   coplanar at (%.3f, %.3f, %.3f)' % tuple(h))
    return ob


# ======================= 시안 =======================
LABEL_MAT = make_mat('LabelMat', (0.0, 0.0, 0.0), 0.5)
_lb = next(x for x in LABEL_MAT.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
_lb.inputs['Emission Color'].default_value = (0.92, 0.94, 0.98, 1); _lb.inputs['Emission Strength'].default_value = 1.6
STATION_MAT = make_mat('StationRef', (0.42, 0.46, 0.52), 0.55, 0.1)


def station_meshes():
    """정거장 크기 기준: 실제 바깥 모듈 FBX (재질은 회색 하나로 — 크기 · 모양 비교용)."""
    names = ['SM_Core', 'SM_Habitat', 'SM_Solar', 'SM_Turret', 'SM_Storage', 'SM_Farm', 'SM_Shield', 'SM_Oxygen', 'SM_ResearchLab']
    out = {}
    for nm in names:
        path = os.path.join(PROJ, 'Art', 'Models', 'Modules', nm + '.fbx')
        before = set(bpy.data.objects)
        try:
            bpy.ops.import_scene.fbx(filepath=path)
        except Exception as e:
            print('import fail', nm, e)
            continue
        bpy.context.view_layer.update()
        new = [o for o in bpy.data.objects if o not in before]
        bm = bmesh.new()
        for o in new:
            if o.type != 'MESH':
                continue
            tmp = bmesh.new(); tmp.from_mesh(o.data); tmp.transform(o.matrix_world)
            tm = bpy.data.meshes.new('tmp'); tmp.to_mesh(tm); tmp.free()
            bm.from_mesh(tm); bpy.data.meshes.remove(tm)
        me = bpy.data.meshes.new('REF_' + nm); bm.to_mesh(me); bm.free()
        me.materials.clear(); me.materials.append(STATION_MAT)
        for o in new:
            bpy.data.objects.remove(o, do_unlink=True)
        if me.vertices:
            lo = Vector([min(v.co[i] for v in me.vertices) for i in range(3)]); hi = Vector([max(v.co[i] for v in me.vertices) for i in range(3)])
            print('REF %s size %.2f x %.2f x %.2f center %.2f %.2f %.2f' % ((nm,) + tuple(hi - lo) + tuple((lo + hi) / 2)))
        out[nm] = me
    return out


def place_station(meshes, offset=Vector()):
    # 코어 = 2 × 2 × 2 (원점 칸 + x · y −1), 거주 · 농장 = x로 2칸 (원점 칸 + x −1) — 가져온 크기 · 중심으로 확인 (REF 줄)
    layout = [((0, 0, 0), 'SM_Core'), ((2, 0, 0), 'SM_Habitat'), ((-2, 0, 0), 'SM_Habitat'), ((2, -1, 0), 'SM_Habitat'), ((-2, -1, 0), 'SM_Habitat'),
              ((3, 0, 0), 'SM_Storage'), ((-4, 0, 0), 'SM_Storage'), ((2, 1, 0), 'SM_Farm'), ((-2, 1, 0), 'SM_Farm'),
              ((0, 1, 0), 'SM_ResearchLab'), ((-1, 1, 0), 'SM_ResearchLab'), ((0, 2, 0), 'SM_Oxygen'), ((-1, 2, 0), 'SM_Oxygen'),
              ((0, 0, 2), 'SM_Shield'), ((-1, -1, 2), 'SM_Turret'), ((3, 1, 0), 'SM_Turret'), ((-4, 1, 0), 'SM_Turret'),
              ((0, -2, 0), 'SM_Turret'), ((-1, -2, 0), 'SM_Turret'), ((4, 0, 0), 'SM_Solar'), ((4, 1, 0), 'SM_Solar'), ((4, -1, 0), 'SM_Solar'),
              ((-5, 0, 0), 'SM_Solar'), ((-5, 1, 0), 'SM_Solar'), ((-5, -1, 0), 'SM_Solar'), ((3, -1, 0), 'SM_Storage'), ((-4, -1, 0), 'SM_Storage')]
    obs = []
    for (x, y, z), nm in layout:
        if nm not in meshes:
            continue
        o = bpy.data.objects.new('Ref_' + nm, meshes[nm]); SC.collection.objects.link(o)
        o.location = offset + Vector((x, y, z))
        obs.append(o)
    return obs


def setup_render():
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            SC.render.engine = eng; break
        except TypeError:
            pass
    try:
        SC.eevee.taa_render_samples = 32
    except Exception:
        pass
    wd = bpy.data.worlds.new('W'); wd.use_nodes = True
    bg = next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs['Color'].default_value = (0.02, 0.025, 0.045, 1); bg.inputs['Strength'].default_value = 0.7
    SC.world = wd
    for nm, d, e in (('Key', (1.0, 1.0, -1.2), 3.6), ('Rim', (-0.7, -1.0, -0.35), 1.6), ('Under', (0.4, 0.3, 1.0), 0.35)):
        ld = bpy.data.lights.new(nm, 'SUN'); ld.energy = e
        lo = bpy.data.objects.new(nm, ld); SC.collection.objects.link(lo)
        lo.rotation_euler = Vector(d).normalized().to_track_quat('-Z', 'Y').to_euler()
    SC.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in SC.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
    cd = bpy.data.cameras.new('Cam'); cd.clip_start = 0.05; cd.clip_end = 400
    cam = bpy.data.objects.new('Cam', cd); SC.collection.objects.link(cam); SC.camera = cam
    os.makedirs(PREVIEW, exist_ok=True)
    return cam, cd


def cam_label(cam, cd, text, W, H, top=False):
    d = 1.0
    hw = cd.ortho_scale / 2 if cd.type == 'ORTHO' else d * 18.0 / cd.lens
    hh = hw * H / W
    cu = bpy.data.curves.new('T', 'FONT'); cu.body = text; cu.size = hw * 0.055; cu.align_x = 'LEFT'
    if os.path.exists(FONT):
        cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    ob = bpy.data.objects.new('CamLabel', cu); SC.collection.objects.link(ob)
    ob.data.materials.append(LABEL_MAT)
    ob.parent = cam; ob.location = (-hw * 0.95, hh * 0.8 if top else -hh * 0.88, -d)
    ob.visible_shadow = False
    return ob


def world_label(text, at, size):
    cu = bpy.data.curves.new('T', 'FONT'); cu.body = text; cu.size = size; cu.align_x = 'CENTER'
    if os.path.exists(FONT):
        cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    ob = bpy.data.objects.new('WLabel', cu); SC.collection.objects.link(ob)
    ob.data.materials.append(LABEL_MAT)
    ob.location = at; ob.rotation_euler = (math.radians(90), 0, math.radians(90))   # +x 쪽 카메라를 봄
    ob.visible_shadow = False
    return ob


def shoot(cam, cd, name, visible, loc, tgt, lens=50, res=(1200, 700), text=None, ortho=None, top=False):
    for o in SC.objects:
        if o.type in ('MESH',) or o.name.startswith('WLabel'):
            o.hide_render = o not in visible
    cam.location = Vector(loc)
    cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    if ortho:
        cd.type = 'ORTHO'; cd.ortho_scale = ortho
    else:
        cd.type = 'PERSP'; cd.lens = lens
    SC.render.resolution_x, SC.render.resolution_y = res
    SC.render.resolution_percentage = 100
    lab = cam_label(cam, cd, text, *res, top=top) if text else None
    SC.render.filepath = os.path.join(PREVIEW, name + '.png')
    bpy.ops.render.render(write_still=True)
    if lab:
        bpy.data.objects.remove(lab, do_unlink=True)
    return SC.render.filepath


def look(ob, center, d, dist):
    d = Vector(d).normalized()
    return center + d * dist, center


# ---------------- 그림 합치기 (bpy 그림 + numpy, 행 0 = 아래) ----------------
def load_px(path, width=None, height=None):
    im = bpy.data.images.load(path)
    w, h = im.size
    if width:
        im.scale(width, max(1, round(h * width / w)))
    elif height:
        im.scale(max(1, round(w * height / h)), height)
    w, h = im.size
    a = np.empty(w * h * 4, dtype=np.float32); im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, 4)


GAP = np.array([0.09, 0.1, 0.13, 1.0], dtype=np.float32)


def hcat(arrs, gap=6):
    h = arrs[0].shape[0]
    out = []
    for i, a in enumerate(arrs):
        if i:
            out.append(np.tile(GAP, (h, gap, 1)))
        out.append(a)
    return np.concatenate(out, 1)


def vcat(arrs, gap=6):   # 위 → 아래 순서로 받음
    w = arrs[0].shape[1]
    out = []
    for i, a in enumerate(reversed(arrs)):
        if i:
            out.append(np.tile(GAP, (gap, w, 1)))
        out.append(a)
    return np.concatenate(out, 0)


def save_px(a, path):
    h, w = a.shape[:2]
    im = bpy.data.images.new('sheet', w, h)
    im.pixels.foreach_set(a.astype(np.float32).ravel())
    im.filepath_raw = path; im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)
    print('SHEET', path, w, h)


def sheet():
    ships = {}
    for fac in FACS:
        ships[('Scout', fac)] = scout(fac)
        ships[('Escort', fac)] = escort(fac)
        ships[('Flagship', fac)] = flagship(fac)
    for o in ships.values():
        o.location = (0, 0, 0)
    ark = None
    ark_path = os.path.join(GIT, 'BlenderWork', 'ArkShip', 'arkship.blend')
    if os.path.exists(ark_path):
        with bpy.data.libraries.load(ark_path, link=False) as (src, dst):
            dst.objects = [n for n in src.objects if n == 'ArkShip']
        for o in dst.objects:
            if o is not None:
                SC.collection.objects.link(o); ark = o
    if ark:
        L = max(v.co.y for v in ark.data.vertices) - min(v.co.y for v in ark.data.vertices)
        ark.scale = (2.2 / L,) * 3
        print('ARK length %.2f -> scale %.3f' % (L, 2.2 / L))
    meshes = station_meshes()
    station = place_station(meshes)
    core1 = bpy.data.objects.new('Ref_Storage1', meshes['SM_Storage']) if 'SM_Storage' in meshes else None   # 1칸 모듈 (코어는 2 × 2 × 2)
    if core1:
        SC.collection.objects.link(core1)
    cam, cd = setup_render()
    shots = {}

    # A) 습격 장면: 앞 = 정거장 (실제 모듈 26칸), 뒤 = 무법 선단 기함 · 호위함 둘 · 정찰선 둘 (실제 비율)
    F, E1, E2, S1, S2 = ships[('Flagship', 'Outlaw')], ships[('Escort', 'Outlaw')], None, ships[('Scout', 'Outlaw')], None
    E2 = bpy.data.objects.new('Escort_Outlaw_b', E1.data); SC.collection.objects.link(E2)
    S2 = bpy.data.objects.new('Scout_Outlaw_b', S1.data); SC.collection.objects.link(S2)
    F.location = (0.5, 14.0, 6.0); F.rotation_euler = (0, 0, math.radians(-14))
    E1.location = (-8.0, 9.5, 3.4); E2.location = (8.5, 10.0, 2.2)
    S1.location = (-3.5, 6.0, 3.6); S2.location = (4.5, 5.2, 2.8)
    for o in (E1, E2, S1, S2):
        o.rotation_euler = (0, 0, 0)
    vis = set(station) | {F, E1, E2, S1, S2}
    shots['raid'] = shoot(cam, cd, 'raid_scene', vis, (-19.0, -9.0, 9.0), (0.0, 8.0, 3.0), lens=30, res=(1600, 900),
                          text='무법 선단 습격 · 기함 약 14칸 · 호위함 약 3칸 · 정찰선 약 1.3칸 (아래 = 정거장 실제 모듈 27개)')
    shots['raid2'] = shoot(cam, cd, 'raid_scene2', vis, (-4.0, -7.0, 0.8), (1.0, 12.0, 6.0), lens=24, res=(1600, 900),
                           text='정거장 쪽에서 올려다본 기함')
    # 견줌: 기함 1.5배 (약 21칸) — 정거장이 클 때 크기 배율 (RaidFleetConfig.SizeScale, 최대 2배)을 모습에도 쓰는 경우
    F.scale = (1.5,) * 3; F.location = (0.5, 15.0, 7.5)   # 같은 자리 (커진 만큼만 조금 위 · 뒤로)
    shots['raid_big'] = shoot(cam, cd, 'raid_scene_big', vis, (-19.0, -9.0, 9.0), (0.0, 8.0, 3.0), lens=30, res=(1600, 900),
                              text='견줌 · 기함 1.5배 (약 21칸)')
    F.scale = (1.0,) * 3

    # B) 크기 비교 (옆에서 직교): 기함 · 호위함 · 정찰선 · 이민선 · 모듈 1칸
    F.rotation_euler = (0, 0, 0); E1.rotation_euler = (0, 0, 0); S1.rotation_euler = (0, 0, 0)
    F.location = (0, 7.0, 0); E1.location = (0, 17.2, 0); S1.location = (0, 20.4, 0)
    vis = {F, E1, S1}
    labels = [world_label('기함 약 14칸', (0.5, 7.0, -3.3), 0.7), world_label('호위함 약 3칸', (0.5, 17.2, -3.3), 0.55),
              world_label('정찰선', (0.5, 20.4, -3.3), 0.45)]
    if ark:
        ark.location = (0, 21.8, 0); ark.rotation_euler = (0, 0, 0); vis.add(ark)
        labels.append(world_label('이민선 2.2칸', (0.5, 22.9, -3.3), 0.45))
    if core1:
        core1.location = (0, 25.6, 0); vis.add(core1)
        labels.append(world_label('모듈 1칸', (0.5, 25.6, -3.3), 0.45))
    vis |= set(labels)
    shots['lineup'] = shoot(cam, cd, 'lineup_side', vis, (40.0, 13.2, 0.2), (0.0, 13.2, 0.2), res=(1600, 560), ortho=28.0,
                            text='옆에서 본 크기 비교 (직교, 실제 비율)', top=True)
    for lb in labels:
        bpy.data.objects.remove(lb, do_unlink=True)

    # C) 세력별 기함 (앞 왼쪽 위 3/4)
    deco = {'Outlaw': '이빨 · 포대 앞 충각 · 함교 뿔 (가시)', 'Merchant': '포대 날개 · 뒤 갑판 컨테이너 · 포대 밑 화물 포드',
            'Scholar': '함교 둘레 빛 고리 · 포대 앞 감지 접시 · 안테나'}
    for fac in FACS:
        o = ships[('Flagship', fac)]; o.location = (0, 0, 0); o.rotation_euler = (0, 0, 0)
        loc, tgt = look(o, Vector((0, 0.6, 0.2)), (-1.0, -1.1, 0.75), 21.0)
        shots['flag_' + fac] = shoot(cam, cd, 'flag_' + fac, {o}, loc, tgt, lens=50, res=(1200, 700),
                                     text='%s 기함 · %s' % (FAC_KO[fac], deco[fac]))
    # D) 세력별 호위함 · 정찰선
    for fac in FACS:
        o = ships[('Escort', fac)]; o.location = (0, 0, 0)
        loc, tgt = look(o, Vector((0, 0.1, 0.05)), (-1.0, -1.1, 0.75), 5.6)
        shots['esc_' + fac] = shoot(cam, cd, 'esc_' + fac, {o}, loc, tgt, lens=50, res=(800, 520), text='%s 호위함' % FAC_KO[fac])
        o = ships[('Scout', fac)]; o.location = (0, 0, 0)
        loc, tgt = look(o, Vector((0, 0.05, 0.0)), (-1.0, -1.1, 0.75), 2.5)
        shots['sct_' + fac] = shoot(cam, cd, 'sct_' + fac, {o}, loc, tgt, lens=50, res=(800, 520), text='%s 정찰선' % FAC_KO[fac])
    # E) 무법 기함 옆 · 위 · 뒤
    o = ships[('Flagship', 'Outlaw')]; o.location = (0, 0, 0)
    for tag, d, dist, txt in (('side', (-1.0, 0.0, 0.12), 20.0, '기함 옆'), ('top', (0.0, -0.06, 1.0), 21.0, '기함 위 (코 = 왼쪽)'),
                              ('rear', (0.85, 1.0, 0.55), 19.0, '기함 뒤 (엔진 셋 · 꼬리 문장)')):
        o.rotation_euler = (0, 0, math.radians(-90) if tag == 'top' else 0)   # 위에서 볼 땐 길이를 가로로
        loc, tgt = look(o, Vector((0, 0.3, 0.2)), d, dist)
        shots['flag_' + tag] = shoot(cam, cd, 'flag_outlaw_' + tag, {o}, loc, tgt, lens=50, res=(900, 600), text=txt)
    o.rotation_euler = (0, 0, 0)

    # 시트 셋
    s1 = vcat([load_px(shots['raid'], 1600), load_px_row([shots['raid_big'], shots['raid2']], 1600), load_px(shots['lineup'], 1600)])
    save_px(s1, os.path.join(PREVIEW, 'sheet_1_scale.png'))
    s2 = vcat([load_px(shots['flag_' + f], 1200) for f in FACS] +
              [load_px_row([shots['flag_side'], shots['flag_top'], shots['flag_rear']], 1200)])
    save_px(s2, os.path.join(PREVIEW, 'sheet_2_flagship.png'))
    rows = [hcat([load_px(shots['esc_' + f], 597), load_px(shots['sct_' + f], 597)]) for f in FACS]
    save_px(vcat(rows), os.path.join(PREVIEW, 'sheet_3_small.png'))


def load_px_row(paths, width, gap=6):
    """가로로 나란히 놓아 전체 너비를 width로."""
    n = len(paths)
    each = (width - gap * (n - 1)) // n
    arrs = [load_px(p, each) for p in paths]
    h = min(a.shape[0] for a in arrs)
    row = hcat([a[:h] for a in arrs], gap)
    if row.shape[1] < width:
        row = np.concatenate([row, np.tile(GAP, (h, width - row.shape[1], 1))], 1)
    return row


if '--sheet' in argv:
    sheet()
else:
    for fac in FACS:
        scout(fac); escort(fac); flagship(fac)
