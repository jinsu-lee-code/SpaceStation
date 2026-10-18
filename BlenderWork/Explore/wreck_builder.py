# 13-5a 탐사 지점 — 난파선 (2026-10-18, 사용자 결정: 난파선만 2안 = Blender 모델)
# 두 동강 난 옛 수송선 (인간 미스터리 T8의 기록 조각이 나오는 곳)
#   앞 동체 : 조종석 유리 · 옆 창 (몇 개만 불이 남음) · 주황 띠 · 꺾인 안테나와 접시
#   뒤 동체 : 넓어진 엔진 구획 · 노즐 2개(안쪽 그을음) · 날개 하나는 온전, 하나는 부러져 아래로 꺾임 · 붉은 신호등(Unity가 깜박임)
#   찢긴 단면: 톱니 + 튀어나온 이빨은 바깥으로 말림 · 단면 둘레 그을음 · 안쪽 어두운 벽 · 갈비 틀 · 바닥판 · 끊긴 배관 · 두 동체를 잇는 늘어진 전선
# 사용: blender -b --factory-startup -P BlenderWork/Explore/wreck_builder.py -- <fbx 출력 폴더>
# 좌표: Blender y = 배 길이(코 0 → 엔진 +), z = 위. 마지막에 경계 상자 가운데를 원점으로 (Unity에서 그 자리를 축으로 천천히 돎).
# 단면 = 모서리를 깎은 팔각형, 변마다 3칸 (점 24개). 재질은 면마다 정함 → 창 · 띠 · 그을음이 덧댄 판이 아니라 같은 면이라 같은 평면 겹침이 없다 (MODELING.md 3).
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

# 미리보기 색 (Unity 재질은 ChapterSetup이 이름으로 바꿔 끼움)
COLORS = {
    'Hull': ((0.62, 0.65, 0.7), 0.4),
    'Hull2': ((0.5, 0.53, 0.58), 0.4),
    'Stripe': ((0.8, 0.33, 0.08), 0.35),
    'Frame': ((0.28, 0.3, 0.34), 0.5),
    'Torn': ((0.72, 0.68, 0.62), 0.55),
    'Dark': ((0.035, 0.037, 0.045), 0.15),
    'Burn': ((0.045, 0.04, 0.035), 0.05),    # 그을음 진한 겹
    'BurnM': ((0.13, 0.12, 0.11), 0.06),     # 중간 겹
    'BurnL': ((0.3, 0.3, 0.31), 0.1),        # 옅은 겹 (단면에서 멀수록)
    'Glass': ((0.04, 0.07, 0.1), 0.9),
    'GlassDark': ((0.02, 0.025, 0.03), 0.8),
    'Lit': ((1.0, 0.72, 0.38), 0.5),
    'Vent': ((0.06, 0.065, 0.07), 0.3),
    'CableR': ((0.6, 0.08, 0.06), 0.3),
    'CableY': ((0.85, 0.65, 0.1), 0.3),
    'Beacon': ((1.0, 0.15, 0.1), 0.5),
}
EMIT = {'Lit': 3.0, 'Beacon': 4.0}

bpy.ops.wm.read_factory_settings(use_empty=True)
NAMES = list(COLORS.keys())
MI = {n: i for i, n in enumerate(NAMES)}
MATS = []
for n in NAMES:
    c, smooth = COLORS[n]
    m = bpy.data.materials.new('M_Wreck' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    if n in EMIT:
        b.inputs['Emission Color'].default_value = c + (1,); b.inputs['Emission Strength'].default_value = EMIT[n]
    m.diffuse_color = c + (1,)
    MATS.append(m)

FRONT, REAR = [], []
rng = random.Random(13)


def new_bm():
    bm = bmesh.new()
    bm.faces.layers.int.new('m')
    return bm


def orient_outward(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def add(bm, group):
    orient_outward(bm)
    group.append(bm)
    return bm


def setm(f, bm, name):
    f[bm.faces.layers.int['m']] = MI[name]


# ---------------- 단면 · 고리 ----------------
W, H, C, T = 1.5, 1.25, 0.32, 0.06   # 폭 · 높이 · 모서리 깎기 · 선체 두께
N = 24


def section(w, h, c):
    a, b = w / 2 - c, h / 2 - c
    corners = [(w / 2, -b), (w / 2, b), (a, h / 2), (-a, h / 2), (-w / 2, b), (-w / 2, -b), (-a, -h / 2), (a, -h / 2)]
    pts = []
    for i in range(8):
        p0, p1 = corners[i], corners[(i + 1) % 8]
        for s in range(3):
            t = s / 3
            pts.append((p0[0] + (p1[0] - p0[0]) * t, p0[1] + (p1[1] - p0[1]) * t))
    return pts


OUTER = section(W, H, C)
INNER = section(W - 2 * T, H - 2 * T, C - T * 0.41)
RIB_IN = section(W - 2 * T - 0.18, H - 2 * T - 0.18, C - T * 0.41 - 0.07)
RIB_OUT = section(W - 2 * T + 0.04, H - 2 * T + 0.04, C - T * 0.41 + 0.016)  # 선체 속으로 0.02 묻힘


def side_of(j):
    """점 j → 변 번호 (0 오른쪽 세로 · 1 위 오른쪽 깎은 면 · 2 위 · 3 · 4 왼쪽 세로 · 5 · 6 아래 · 7)."""
    return j // 3


def ring(sec, y, s=1.0, zoff=0.0, ys=None, push=None):
    out = []
    for j, (x, z) in enumerate(sec):
        yy = y + (ys[j] if ys else 0.0)
        p = Vector((x * s, yy, z * s + zoff))
        if push:
            d = Vector((x, 0, z)).normalized()
            p += d * push[j]
        out.append(p)
    return out


def loft(rings, mat, cap_start=None, cap_end=None, closed=False, group=FRONT):
    """고리 목록을 차례로 잇는 덩어리. mat(k, j) = k번째 띠의 j번째 면 재질."""
    bm = new_bm()
    vs = [[bm.verts.new(p) for p in r] for r in rings]
    n = len(rings[0])
    kn = len(rings) if closed else len(rings) - 1
    faces = {}
    for k in range(kn):
        a, b = vs[k], vs[(k + 1) % len(rings)]
        for j in range(n):
            j2 = (j + 1) % n
            f = bm.faces.new((a[j], a[j2], b[j2], b[j]))
            setm(f, bm, mat(k, j))
            faces[(k, j)] = f
    if cap_start:
        setm(bm.faces.new(list(reversed(vs[0]))), bm, cap_start)
    if cap_end:
        setm(bm.faces.new(vs[-1]), bm, cap_end)
    add(bm, group)
    return bm, faces


def inset(bm, faces, frame='Frame', thickness=0.022, depth=-0.012):
    """창 · 환기구: 면을 안으로 들여 틀을 만든다 (새 옆면 = 틀 재질)."""
    if not faces:
        return
    bm.normal_update()  # 막 만든 면은 법선이 0 → 들이기가 두께 0 틀(넓이 0 삼각형 144개)을 만들었음
    res = bmesh.ops.inset_individual(bm, faces=faces, thickness=thickness, depth=depth, use_even_offset=True)
    for f in res['faces']:
        setm(f, bm, frame)
    orient_outward(bm)


def lathe(origin, axis, profile, mats, seg=16, closed=False, cap_start=None, cap_end=None, group=FRONT):
    """원형 회전체. profile = [(반지름, 축 방향 거리)], mats[k] = k번째 띠 재질."""
    axis = axis.normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    rings = []
    for r, d in profile:
        rings.append([origin + axis * d + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)])
    return loft(rings, lambda k, j: mats[k % len(mats)], cap_start, cap_end, closed, group)


def slab_xy(pts, z0, z1, mat, group):
    """위에서 본 다각형(x, y)을 z0 ~ z1 두께로 (날개 · 바닥판)."""
    bm = new_bm()
    lo = [bm.verts.new((x, y, z0)) for x, y in pts]
    hi = [bm.verts.new((x, y, z1)) for x, y in pts]
    n = len(pts)
    setm(bm.faces.new(hi), bm, mat); setm(bm.faces.new(list(reversed(lo))), bm, mat)
    for i in range(n):
        j = (i + 1) % n
        setm(bm.faces.new((lo[i], lo[j], hi[j], hi[i])), bm, mat)
    bm.normal_update()
    return add(bm, group)


def frame_ring(p, axis, r, seg):
    axis = axis.normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    return [p + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)]


def tube(points, r, mat, group, seg=8, end_jag=0.0):
    """폴리라인 관 (전선 · 배관). end_jag = 끝 톱니 (끊긴 배관)."""
    bm = new_bm()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[1] - points[0]) if k == 0 else (points[-1] - points[-2]) if k == n - 1 else (points[k + 1] - points[k - 1])
        rr = frame_ring(p, t, r, seg)
        if k == n - 1 and end_jag > 0:
            tn = t.normalized()
            rr = [q + tn * (end_jag if i % 2 == 0 else -end_jag * 0.3) * (0.6 + 0.4 * rng.random()) for i, q in enumerate(rr)]
        rings.append([bm.verts.new(q) for q in rr])
    for k in range(n - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            setm(bm.faces.new((a[i], a[j], b[j], b[i])), bm, mat)
    setm(bm.faces.new(list(reversed(rings[0]))), bm, mat)
    setm(bm.faces.new(rings[-1]), bm, mat)
    return add(bm, group)


def bezier(p0, p1, p2, p3, steps=10):
    return [p0 * (1 - t) ** 3 + p1 * 3 * (1 - t) ** 2 * t + p2 * 3 * (1 - t) * t * t + p3 * t ** 3 for t in (i / steps for i in range(steps + 1))]


# ---------------- 찢긴 단면 ----------------
JAG = []
for j in range(N):
    a = 2 * math.pi * j / N
    v = 0.16 * math.sin(a * 2 + 0.7) + 0.1 * (rng.random() * 2 - 1) + (0.1 if j % 2 == 0 else -0.06)
    JAG.append(max(-0.3, min(0.32, v)))
PETAL = [0.09 * max(0.0, (JAG[j] - 0.05) / 0.27) for j in range(N)]  # 튀어나온 이빨은 바깥으로 말림

# ================= 앞 동체 =================
Y_TEAR = 4.25
front_rings = [(0.00, 0.30, -0.12), (0.25, 0.56, -0.08), (0.70, 0.82, -0.03), (1.20, 0.96, 0.0), (1.60, 1.0, 0.0)]
front_rings += [(1.9 + 0.3 * i, 1.0, 0.0) for i in range(7)]   # 1.9 ~ 3.7
NB = len(front_rings) - 1                                       # 보통 띠 수 (11)
rings = [ring(OUTER, y, s, z) for y, s, z in front_rings]
rings.append(ring(OUTER, Y_TEAR, ys=JAG, push=PETAL))            # 바깥 찢긴 고리
rings.append(ring(INNER, Y_TEAR - 0.03, ys=JAG, push=PETAL))     # 안쪽 찢긴 고리
for y, _, _ in reversed(front_rings[5:]):                       # 안쪽 벽도 바깥과 같은 자리에 고리 (휘었을 때 안쪽 벽이 곧은 채 바깥을 뚫지 않게)
    rings.append(ring(INNER, y))
rings.append(ring(INNER, 2.0))                                  # 안쪽 벽 끝

LIT = {(0, 1), (0, 4), (4, 2)}       # (변, 창 번호) — 불이 남은 창
HEAT_BASE = [0.95, 0.58, 0.32, 0.12]   # 단면에서 띠 거리별 그을음 세기


def heat_noise(phase):
    """둘레를 따라 부드럽게 변하는 얼룩 (면마다 따로 굴리면 바둑판처럼 보였음)."""
    p1, p2 = rng.random() * 6.3, rng.random() * 6.3
    return [0.24 * math.sin(2 * math.pi * j / N * 2 + p1 + phase) + 0.13 * math.sin(2 * math.pi * j / N * 5 + p2) + 0.08 * (rng.random() * 2 - 1) for j in range(N)]


FRONT_HEAT = [heat_noise(d * 0.9) for d in range(4)]
REAR_HEAT = [heat_noise(d * 0.9 + 2) for d in range(4)]


def scorch(d, j, table):
    """단면에서 d번째 띠의 j번째 면 그을음: 진함 · 중간 · 옅음 세 겹 (MODELING.md 7-4) 또는 None."""
    if d < 0 or d >= len(HEAT_BASE):
        return None
    h = HEAT_BASE[d] + table[d][j]
    return 'Burn' if h > 0.78 else 'BurnM' if h > 0.5 else 'BurnL' if h > 0.26 else None


def front_mat(k, j):
    s, sub = side_of(j), j % 3
    if k == NB + 1: return 'Torn'          # 단면 테두리
    if k >= NB + 2: return 'Dark'          # 안쪽 벽
    if k == 0: return 'Hull'
    if k == 1: return 'Glass' if s == 2 else 'Hull'
    if k == 2: return 'Glass' if s in (1, 2, 3) else 'Hull'
    if k == 3: return 'Stripe'
    win = k - 4                             # 옆 창 번호 0 ~ 6
    if s in (0, 4) and sub == 1 and 0 <= win <= 6 and k < NB:
        return 'Lit' if (s, win) in LIT else 'GlassDark'
    burn = scorch(NB - k, j, FRONT_HEAT)
    if burn: return burn
    if s in (1, 2, 3) and k % 2 == 1: return 'Hull2'
    return 'Hull'


bm_front, ff = loft(rings, front_mat, cap_start='Frame', cap_end='Dark', group=FRONT)
inset(bm_front, [f for f in bm_front.faces if f[bm_front.faces.layers.int['m']] in (MI['GlassDark'], MI['Lit'])])
inset(bm_front, [f for f in bm_front.faces if f[bm_front.faces.layers.int['m']] == MI['Glass']], thickness=0.03, depth=-0.01)

# 안쪽 갈비 틀 · 바닥판 · 천장 배관 · 안테나
for y in (2.5, 3.1, 3.64):
    loft([ring(RIB_OUT, y), ring(RIB_OUT, y + 0.06), ring(RIB_IN, y + 0.06), ring(RIB_IN, y)], lambda k, j: 'Frame', closed=True, group=FRONT)
# 바닥판 · 천장 배관은 길이 방향으로 잘게 나눔 (선체와 같이 휘도록)
FLOOR_X = [-0.7 + 1.4 * i / 7 for i in range(8)]
FLOOR_JAG = [0.27, 0.2, 0.36, 0.05, 0.42, 0.15, 0.3, 0.1]   # 끊긴 끝 톱니 (가로 칸마다)


def floor_ring(y, jag=None):
    top = [Vector((x, y + (jag[i] if jag else 0.0), -0.18)) for i, x in enumerate(FLOOR_X)]
    bot = [Vector((x, y + (jag[i] if jag else 0.0), -0.22)) for i, x in reversed(list(enumerate(FLOOR_X)))]
    return top + bot


loft([floor_ring(1.95 + 0.25 * i) for i in range(9)] + [floor_ring(4.15), floor_ring(4.15, FLOOR_JAG)], lambda k, j: 'Frame',
     cap_start='Frame', cap_end='Frame', group=FRONT)
tube([Vector((-0.38, 1.9 + 0.25 * i, 0.47)) for i in range(9)] + [Vector((-0.36, 4.2, 0.46)), Vector((-0.3, 4.55, 0.4))], 0.05, 'Frame', FRONT, seg=10, end_jag=0.04)
tube([Vector((0.32, 2.5, 0.6)), Vector((0.32, 2.5, 0.98)), Vector((0.42, 2.4, 1.18))], 0.028, 'Frame', FRONT, seg=8)
lathe(Vector((0.42, 2.4, 1.18)), Vector((0.35, -0.35, 0.87)), [(0.018, -0.01), (0.3, 0.09), (0.3, 0.115), (0.018, 0.02)], ['Hull', 'Frame', 'Hull2', 'Frame'], seg=14, closed=True, group=FRONT)

# 앞 동체 휨 (사용자 피드백 2026-10-18 "부서진 것 치고 너무 일자 → 구부정하게"): 조종석 뒤(BEND_Y0)부터 찢긴 쪽으로 갈수록 위로 활처럼 휨.
# 위쪽(z = +BEND_R)을 중심으로 한 원호에 길이를 그대로 감음 → 단면은 모양을 지킨 채 기울어짐
# 옆으로도 조금 휨(+x 쪽) → 위에서 봐도 곧지 않게. 옆 휨 먼저, 그다음 위 휨
BEND_Y0, BEND_DEG_AT = 1.4, (4.7, 30.0)          # 위 휨: (이 y에서, 이만큼 기욺)
SIDE_DEG_AT = 14.0                               # 옆 휨: 같은 y에서 이만큼
BEND_R = (BEND_DEG_AT[0] - BEND_Y0) / math.radians(BEND_DEG_AT[1])
SIDE_R = (BEND_DEG_AT[0] - BEND_Y0) / math.radians(SIDE_DEG_AT)


def arc(u, y, R):
    """y를 길이로 삼아 (u = 휘는 쪽 좌표)를 반지름 R 원호에 감음. 돌려줌: (새 u, 새 y)."""
    if y <= BEND_Y0:
        return u, y
    phi = (y - BEND_Y0) / R
    r = R - u
    return R - r * math.cos(phi), BEND_Y0 + r * math.sin(phi)


def bend(p):
    x, y = arc(p.x, p.y, SIDE_R)
    z, y = arc(p.z, y, BEND_R)
    return Vector((x, y, z))


def bend_frame(p):
    """점 p 자리에서 휜 선체의 방향 (옆 휨 → 위 휨)."""
    side = max(0.0, (p.y - BEND_Y0) / SIDE_R)
    _, y1 = arc(p.x, p.y, SIDE_R)
    up = max(0.0, (y1 - BEND_Y0) / BEND_R)
    return Matrix.Rotation(up, 4, 'X') @ Matrix.Rotation(-side, 4, 'Z')


for bm in FRONT:
    for v in bm.verts:
        v.co = bend(v.co)
    orient_outward(bm)

# ================= 뒤 동체 =================
Y2 = 5.1
JAG2 = [max(-0.27, min(0.27, -JAG[j] * 0.8 + 0.05 * (rng.random() * 2 - 1))) for j in range(N)]
PETAL2 = [0.08 * max(0.0, (-JAG2[j] - 0.05) / 0.22) for j in range(N)]
rear_out = [(5.6, 1.0), (6.0, 1.0), (6.12, 1.08), (6.4, 1.08), (6.7, 1.08), (7.0, 1.08), (7.3, 1.08), (7.45, 1.08), (7.6, 0.96)]
rings = [ring(INNER, 6.2), ring(INNER, Y2 + 0.03, ys=JAG2, push=PETAL2), ring(OUTER, Y2, ys=JAG2, push=PETAL2)]
rings += [ring(OUTER, y, s) for y, s in rear_out]


def rear_mat(k, j):
    s, sub = side_of(j), j % 3
    if k == 0: return 'Dark'
    if k == 1: return 'Torn'
    burn = scorch(k - 2, j, REAR_HEAT) if 2 <= k <= 5 else None
    if burn: return burn
    if k == 4: return 'Frame'                       # 넓어지는 턱
    if k == 5: return 'Stripe'
    if k in (6, 7) and s in (0, 4) and sub == 1: return 'Vent'
    if k == 9: return 'Frame'
    return 'Hull2' if s in (1, 2, 3) and k % 2 == 0 else 'Hull'


bm_rear, _ = loft(rings, rear_mat, cap_start='Dark', cap_end='Frame', group=REAR)
inset(bm_rear, [f for f in bm_rear.faces if f[bm_rear.faces.layers.int['m']] == MI['Vent']], thickness=0.025, depth=-0.02)
loft([ring(RIB_OUT, 5.72), ring(RIB_OUT, 5.78), ring(RIB_IN, 5.78), ring(RIB_IN, 5.72)], lambda k, j: 'Frame', closed=True, group=REAR)
slab_xy([(-0.7, 6.3), (-0.7, 5.0), (-0.42, 4.86), (-0.15, 5.05), (0.12, 4.82), (0.4, 5.02), (0.7, 4.95), (0.7, 6.3)], -0.22, -0.18, 'Frame', REAR)
tube([Vector((-0.38, 6.3, 0.47)), Vector((-0.38, 5.4, 0.47)), Vector((-0.4, 4.95, 0.5))], 0.05, 'Frame', REAR, seg=10, end_jag=0.04)
for x in (-0.38, 0.38):  # 엔진 노즐 (바깥 금속 · 테 · 안쪽 그을음)
    lathe(Vector((x, 0, -0.08)), Vector((0, 1, 0)), [(0.26, 7.45), (0.33, 8.25), (0.29, 8.27), (0.18, 7.75)], ['Frame', 'Torn', 'Burn'],
          seg=18, cap_start='Frame', cap_end='Burn', group=REAR)
slab_xy([(-0.75, 6.35), (-0.75, 7.3), (-1.95, 7.15), (-1.95, 6.8)], -0.03, 0.03, 'Hull', REAR)  # 온전한 날개
slab_xy([(-1.92, 6.78), (-1.92, 7.17), (-2.02, 7.12), (-2.02, 6.83)], -0.05, 0.05, 'Frame', REAR)  # 날개 끝 등
broken = slab_xy([(0.75, 6.35), (0.75, 7.3), (1.25, 7.25), (1.12, 7.02), (1.3, 6.85), (1.08, 6.62), (1.2, 6.45)], -0.03, 0.03, 'Hull', REAR)
bmesh.ops.rotate(broken, verts=broken.verts, cent=Vector((0.8, 6.8, 0.0)), matrix=Matrix.Rotation(math.radians(18), 3, 'Y'))
lathe(Vector((0, 6.9, 0.64)), Vector((0, 0, 1)), [(0.075, 0.0), (0.075, 0.1)], ['Frame'], seg=12, cap_start='Frame', cap_end='Frame', group=REAR)
lathe(Vector((0, 6.9, 0.64)), Vector((0, 0, 1)), [(0.055, 0.095), (0.055, 0.14), (0.032, 0.175)], ['Beacon'], seg=12, cap_start='Beacon', cap_end='Beacon', group=REAR)

# 뒤 동체: 앞 동체가 휜 끝 방향을 따라간 뒤 아래 용골(PIVOT)을 경첩 삼아 꺾여 내려감 + 옆으로 틀어지고 비틀림
# (위쪽 틈이 크게 벌어지고 아래는 거의 붙은 V자 — 등이 부러져 굽은 모습). 전선은 꺾은 뒤 위치로 이음
PIVOT = Vector((0, 4.7, -0.6))
BREAK_DEG, YAW_DEG, ROLL_DEG, PULL = 42.0, 10.0, 12.0, 0.3   # PULL = 아래 이음매 쪽으로 당김 (아래는 거의 붙고 위만 벌어지게)
REAR_M = Matrix.Translation(bend(PIVOT)) @ bend_frame(PIVOT) @ Matrix.Rotation(math.radians(-BREAK_DEG), 4, 'X') \
    @ Matrix.Rotation(math.radians(YAW_DEG), 4, 'Z') @ Matrix.Rotation(math.radians(ROLL_DEG), 4, 'Y') @ Matrix.Translation(-PIVOT - Vector((0, PULL, 0)))
for bm in REAR:
    bmesh.ops.transform(bm, matrix=REAR_M, verts=bm.verts)
    orient_outward(bm)

# 두 단면이 서로 파고들지 않는지 (가장 가까운 점 사이 거리)
front_tear = [bend(p) for p in ring(OUTER, Y_TEAR, ys=JAG, push=PETAL)]
rear_tear = [REAR_M @ p for p in ring(OUTER, Y2, ys=JAG2, push=PETAL2)]
print('WRECK gap min %.3f max %.3f' % (min((a - b).length for a in front_tear for b in rear_tear),
                                       max(min((a - b).length for b in rear_tear) for a in front_tear)))

# 늘어진 전선: 둘은 두 동체를 잇고(위로 벌어진 틈을 가로질러 처짐), 둘은 앞 동체에서 끊겨 늘어짐
for x, mat, sag in ((0.2, 'CableR', 0.45), (0.05, 'CableY', 0.6)):
    a = bend(Vector((x, 3.2, 0.58))); b = REAR_M @ Vector((x + 0.05, 5.6, 0.58))
    c1 = bend(Vector((x, 4.1, 0.42)))
    c2 = (bend(Vector((x, 4.5, 0.3))) + REAR_M @ Vector((x + 0.05, 4.9, 0.3))) * 0.5 - Vector((0, 0, sag))
    tube(bezier(a, c1, c2, REAR_M @ Vector((x + 0.05, 5.15, 0.5)), 10) + [b], 0.022, mat, FRONT)
for x, mat, drop in ((0.45, 'CableY', 0.85), (-0.15, 'CableR', 0.7)):
    tube([bend(p) for p in bezier(Vector((x, 3.3, 0.58)), Vector((x, 4.1, 0.45)), Vector((x + 0.05, 4.5, 0.1)), Vector((x + 0.12, 4.65, -drop)), 10)],
         0.02, mat, FRONT)


# ---------------- 합치기 · 검사 · 내보내기 ----------------
def finish(name):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = sorted({f[bm.faces.layers.int['m']] for bm in FRONT + REAR for f in bm.faces})
    for i in used: me.materials.append(MATS[i])
    big = new_bm()
    lay = big.faces.layers.int['m']
    for bm in FRONT + REAR:
        src = bm.faces.layers.int['m']
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf[lay] = f[src]; nf.material_index = used.index(f[src])
        bm.free()
    big.normal_update()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    # 경계 상자 가운데를 원점으로
    lo = Vector([min(v.co[i] for v in big.verts) for i in range(3)]); hi = Vector([max(v.co[i] for v in big.verts) for i in range(3)])
    bmesh.ops.translate(big, vec=-(lo + hi) / 2, verts=big.verts)
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
    print('WRECK %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d degenerate %d size %.2f x %.2f x %.2f mats %s'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, degenerate, size.x, size.y, size.z, [MATS[i].name for i in used]))
    return ob


ob = finish('Wreck')
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'wreck.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_Wreck.fbx'), use_selection=True, object_types={'MESH'},
                             axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                             mesh_smooth_type='FACE')

# 미리보기 (우주 배경 · 해 하나)
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng; break
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
for tag, d, dist, target in (('q', Vector((1.0, -0.55, 0.45)), 13.0, Vector((0, 0, 0))), ('side', Vector((1, 0.05, 0.12)), 12.0, Vector((0, 0, 0))),
                             ('tear', Vector((0.9, 0.25, 0.4)), 5.5, Vector((0, 0.5, 0))), ('rear', Vector((-0.6, 0.8, 0.35)), 11.0, Vector((0, 0, 0))),
                             ('top', Vector((0.15, -0.1, 1.0)), 13.0, Vector((0, 0, 0)))):
    d = d.normalized(); cam.location = target + d * dist; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    sc.render.resolution_x = 900; sc.render.resolution_y = 600
    sc.render.filepath = os.path.join(pv, 'wreck_%s.png' % tag); bpy.ops.render.render(write_still=True)
