# 11-17 ④ 내부 사고 대응 소품 (2026-10-11) — 모두 벽에 붙는 소품
#   IncLeak       : 배관 누출 — 벽에서 나와 가로지르는 관(양 끝 벽 플랜지) · 받침 2 · 이음(새는 곳) · 압력계 · 색 띠(M_IncBand = 물/산소 색)
#                   + 게이트 밸브 몸통 · 보닛 · 줄기 / ValveWheel(빨간 핸들 바퀴, 원점 = 바퀴 가운데, 벽 법선 축으로 돌림)
#   IncBreaker    : 차단기 함 — 속 빈 함 · 안쪽 판 · 스위치 받침 6 · 이름표 줄 / BreakerDoor(원점 = 왼쪽 경첩) · 경고 삼각형 · 손잡이
#                   / Switch0~5(원점 = 회전축, 가로축으로 ±28° — 위 = 켬) / Led0~5(M_IncLed, Unity가 빨강 · 초록으로 물들임)
#   IncFireBox    : 불이 나는 배전함 — 함 · 덮개 나사 · 위아래 전선관 · 고압 경고판 / Soot(위로 번진 그을음 세 겹 — 불을 끈 뒤 보임)
#   IncExtinguisher: 소화기 — ExtBracket(벽 받침판 · 고정 띠) / ExtBottle(빨간 통 · 둥근 어깨 · 머리 밸브 · 손잡이 · 압력계 · 호스 · 노즐 · 흰 띠)
#   IncFilter     : 공기 정화 필터함 — 함 · 위 환기 그릴 · 아래 카트리지 칸 · 작은 화면 / FilterLed(M_IncLed)
#                   / Cartridge(주름 필터 앞면 = M_IncFilter, Unity가 더러움(갈색) · 새것(흰색)으로 물들임, 당김 손잡이 — 원점 = 가운데, −y로 뽑힘)
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/incident_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 벽면 = Blender xz 평면(y = 0), 방 쪽 = −y (Unity +z — LookRotation(벽 법선)으로 붙임). 원점 = 벽면 위 소품 가운데
# 크기: 벽 자리 검사(가로 ±0.36 · 세로 ±0.34 · 앞 0.4 빈 곳) 안에 들어가게
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'Pipe': ((0.5, 0.54, 0.56), 0.5),
    'Metal': ((0.66, 0.68, 0.72), 0.6),
    'Dark': ((0.06, 0.06, 0.07), 0.2),
    'Device': ((0.17, 0.19, 0.22), 0.35),
    'Housing': ((0.55, 0.58, 0.6), 0.35),
    'Red': ((0.75, 0.08, 0.06), 0.45),
    'Hazard': ((0.95, 0.72, 0.08), 0.35),
    'White': ((0.9, 0.9, 0.88), 0.3),
    'Rubber': ((0.05, 0.05, 0.055), 0.15),
    'Glass': ((0.75, 0.8, 0.82), 0.85),
    'Scorch': ((0.035, 0.03, 0.028), 0.05),
    'ScorchM': ((0.13, 0.12, 0.115), 0.05),
    'ScorchL': ((0.3, 0.29, 0.28), 0.08),
    'Screen': ((0.12, 0.55, 0.6), 0.5),
    'Band': ((0.92, 0.92, 0.92), 0.4),     # Unity: 물(파랑) · 산소(청록)
    'Led': ((0.92, 0.92, 0.92), 0.5),      # Unity: 빨강 · 초록 + 발광
    'Filter': ((0.92, 0.92, 0.9), 0.1),    # Unity: 더러움 · 새것
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_Inc' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    m.diffuse_color = c + (1,)
    MATS[n] = m
PARTS = []


def orient_outward(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def rrect(cx, cy, w, h, r, seg=4):
    pts = []
    r = min(r, w / 2 - 1e-4, h / 2 - 1e-4)
    for ox, oy, a0 in ((w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cy + oy + r * math.sin(a)))
    return pts


def circle(cx, cy, r, seg=20):
    return [(cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def to3(axis, u, v, w):
    if axis == 'z': return (u, v, w)      # 위에서 본 단면 (x, y), 높이 w
    if axis == 'y': return (u, w, v)      # 앞에서 본 단면 (x, z), 깊이 w(음수 = 방 쪽)
    return (w, u, v)                      # 옆에서 본 단면 (y, z), 가로 w


def prism(pts, a0, a1, mat, axis='y', bevel=0.0, seg=2):
    bm = bmesh.new()
    lo = [bm.verts.new(to3(axis, u, v, a0)) for u, v in pts]
    hi = [bm.verts.new(to3(axis, u, v, a1)) for u, v in pts]
    n = len(pts)
    bm.faces.new(hi); bm.faces.new(list(reversed(lo)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    orient_outward(bm)
    if bevel > 0:
        k = {'x': 0, 'y': 1, 'z': 2}[axis]
        cap = [e for e in bm.edges if all(abs(v.co[k] - a1) < 1e-7 for v in e.verts) or all(abs(v.co[k] - a0) < 1e-7 for v in e.verts)]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=seg, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def front(pts, d0, d1, mat, bevel=0.0):
    """앞에서 본 단면 (x, z)을 벽에서 d0 ~ d1(양수 = 방 쪽 거리)만큼."""
    return prism(pts, -d0, -d1, mat, 'y', bevel)


def box(cx, cz, w, h, d0, d1, mat, r=0.008, bevel=0.0):
    return front(rrect(cx, cz, w, h, r, 3), d0, d1, mat, bevel)


def tube(points, r, mat, seg=12, cap=True):
    bm = bmesh.new()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[min(k + 1, n - 1)] - points[max(k - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        u = t.cross(up).normalized(); v = t.cross(u).normalized()
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r) for i in range(seg)])
    for k in range(n - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def torus(c, R, r, mat, axis=Vector((0, -1, 0)), seg=28, rseg=10):
    """가운데 c, 축 axis(고리 면의 법선)인 고리."""
    bm = bmesh.new()
    axis = axis.normalized()
    a = Vector((1, 0, 0)) if abs(axis.x) < 0.9 else Vector((0, 0, 1))
    u = axis.cross(a).normalized(); v = axis.cross(u).normalized()
    rings = []
    for i in range(seg):
        t = 2 * math.pi * i / seg
        dirv = u * math.cos(t) + v * math.sin(t)
        center = c + dirv * R
        rings.append([bm.verts.new(center + (dirv * math.cos(2 * math.pi * k / rseg) + axis * math.sin(2 * math.pi * k / rseg)) * r) for k in range(rseg)])
    for i in range(seg):
        A, B = rings[i], rings[(i + 1) % seg]
        for k in range(rseg):
            m = (k + 1) % rseg
            bm.faces.new((A[k], A[m], B[m], B[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def cyl_y(cx, cz, r, d0, d1, mat, seg=20, bevel=0.0):
    """벽에서 나오는 원기둥 (가운데 x, z)."""
    return front(circle(cx, cz, r, seg), d0, d1, mat, bevel)


def cyl_x(cy, cz, r, x0, x1, mat, seg=20, bevel=0.0):
    """벽을 따라 가로로 누운 원기둥 (단면 가운데 y = −d, z)."""
    return prism(circle(cy, cz, r, seg), x0, x1, mat, 'x', bevel)


def cyl_z(cx, cy, r, z0, z1, mat, seg=20, bevel=0.0):
    return prism(circle(cx, cy, r, seg), z0, z1, mat, 'z', bevel)


def dome(c, r, mat, sz=1.0, u=20, v=10):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    for vt in bm.verts:
        vt.co = Vector((vt.co.x + c[0], vt.co.y + c[1], vt.co.z * sz + c[2]))
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def take():
    out = list(PARTS); PARTS.clear(); return out


# ================= 배관 누출 =================
def build_leak():
    D = 0.09   # 관 가운데가 벽에서 떨어진 거리
    R = 0.032
    X = 0.29
    # 관 한 줄: 벽 속 → 꺾임(반지름 0.05, 점 여러 개로 둥글게) → 가로 → 꺾임 → 벽 속
    pts = [Vector((-X, 0.02, 0)), Vector((-X, -(D - 0.05), 0))]
    for i in range(1, 7):
        a = math.radians(90 * i / 6)
        pts.append(Vector((-X + 0.05 - 0.05 * math.cos(a), -(D - 0.05) - 0.05 * math.sin(a), 0)))
    pts.append(Vector((X - 0.05, -D, 0)))
    for i in range(1, 7):
        a = math.radians(90 * i / 6)
        pts.append(Vector((X - 0.05 + 0.05 * math.sin(a), -(D - 0.05) - 0.05 * math.cos(a), 0)))
    pts.append(Vector((X, 0.02, 0)))
    tube(pts, R, 'Pipe', seg=16)
    # 벽 플랜지 (벽면에 반쯤 묻힘) + 볼트
    for sx in (-1, 1):
        cyl_y(sx * X, 0, 0.058, -0.008, 0.012, 'Metal', 24, bevel=0.003)
        for k in range(6):
            a = 2 * math.pi * k / 6 + 0.3
            cyl_y(sx * X + 0.044 * math.cos(a), 0.044 * math.sin(a), 0.006, 0.011, 0.017, 'Dark', 8)
    # 받침 2: 벽 판 + 아래 받침대 + 관을 감싼 띠
    for x in (-0.15, 0.16):
        box(x, -0.075, 0.05, 0.05, 0.001, 0.012, 'Device', r=0.006)
        box(x, -0.052, 0.022, 0.04, 0.01, D, 'Device', r=0.004)              # 벽에서 관 아래까지 뻗은 받침대
        cyl_x(-D, 0, R + 0.008, x - 0.012, x + 0.012, 'Device', 20, bevel=0.002)
    # 이음 (새는 곳, 볼트 6) — 가운데 왼쪽
    cyl_x(-D, 0, R + 0.014, -0.075, -0.035, 'Metal', 24, bevel=0.003)
    for k in range(6):
        a = 2 * math.pi * k / 6
        prism(circle(-D + 0.036 * math.cos(a), 0.036 * math.sin(a), 0.005, 8), -0.08, -0.03, 'Dark', 'x')
    # 색 띠 (물 · 산소)
    cyl_x(-D, 0, R + 0.003, 0.19, 0.228, 'Band', 24)
    # 게이트 밸브 몸통 · 보닛 · 줄기 (가운데 오른쪽)
    VX = 0.06
    cyl_x(-D, 0, R + 0.02, VX - 0.05, VX + 0.05, 'Red', 24, bevel=0.006)
    cyl_y(VX, 0, 0.03, D + 0.03, D + 0.11, 'Red', 20, bevel=0.004)
    cyl_y(VX, 0, 0.036, D + 0.1, D + 0.12, 'Metal', 20, bevel=0.003)
    cyl_y(VX, 0, 0.009, D + 0.115, D + 0.165, 'Metal', 10)
    # 압력계: 관 위로 작은 관 → 원판 계기 (앞을 봄)
    tube([Vector((-0.22, -D, 0.02)), Vector((-0.22, -D, 0.1))], 0.01, 'Metal', seg=10)
    cyl_y(-0.22, 0.13, 0.042, D - 0.012, D + 0.012, 'Metal', 28, bevel=0.003)
    cyl_y(-0.22, 0.13, 0.034, D + 0.011, D + 0.0135, 'White', 28)
    box(-0.212, 0.138, 0.004, 0.026, D + 0.0134, D + 0.0145, 'Red', r=0.0015)   # 바늘
    cyl_y(-0.22, 0.13, 0.036, D + 0.012, D + 0.016, 'Glass', 28)
    static = take()
    # 핸들 바퀴 (원점 = 바퀴 가운데)
    WC = Vector((VX, -(D + 0.165), 0))
    torus(WC, 0.068, 0.011, 'Red')
    for k in range(3):
        a = 2 * math.pi * k / 3 + 0.5
        tube([WC + Vector((math.cos(a), 0, math.sin(a))) * 0.012, WC + Vector((math.cos(a), 0, math.sin(a))) * 0.064], 0.0065, 'Red', seg=8)
    cyl_y(VX, 0, 0.016, D + 0.155, D + 0.178, 'Metal', 14, bevel=0.002)
    wheel = take()
    return [('LeakPipe', static, None), ('ValveWheel', wheel, WC)]


# ================= 차단기 함 =================
SW_X = (-0.11, 0.0, 0.11)
SW_Z = (0.06, -0.13)


def build_breaker():
    W, H, Dp = 0.46, 0.58, 0.11
    t = 0.014
    box(0, 0, W, H, 0.002, 0.014, 'Device', r=0.012)                                      # 뒤판
    box(0, H / 2 - t / 2, W, t, 0.014, Dp, 'Device', r=0.004)                              # 위 · 아래 옆판 (가로 전체)
    box(0, -(H / 2 - t / 2), W, t, 0.014, Dp, 'Device', r=0.004)
    box(-(W / 2 - t / 2), 0, t, H - 2 * t + 0.004, 0.014, Dp - 0.002, 'Device', r=0.003)  # 왼 · 오 옆판 (위아래 판 사이, 살짝 덜 나옴)
    box(W / 2 - t / 2, 0, t, H - 2 * t + 0.004, 0.014, Dp - 0.002, 'Device', r=0.003)
    box(0, 0, W - 2 * t - 0.01, H - 2 * t - 0.01, 0.0135, 0.022, 'Dark', r=0.004)         # 안쪽 판
    # 이름표 줄 · 위 표시 띠
    box(0, H / 2 - 0.06, 0.3, 0.035, 0.022, 0.026, 'White', r=0.004)
    for i, w in enumerate((0.14, 0.08)):
        box(-0.07 + w / 2 - 0.04 + i * 0.12, H / 2 - 0.06, w, 0.008, 0.026, 0.0275, 'Dark', r=0.002)
    # 스위치 받침 6 + 각 아래 이름표
    for z in SW_Z:
        box(0, z - 0.075, 0.36, 0.022, 0.022, 0.026, 'White', r=0.003)
        for x in SW_X:
            box(x, z, 0.06, 0.095, 0.022, 0.034, 'Rubber', r=0.008, bevel=0.002)
            box(x, z, 0.03, 0.06, 0.034, 0.036, 'Dark', r=0.004)                           # 레버 홈
    static = take()
    # 문 (원점 = 왼쪽 경첩, 닫힌 자리)
    HX = -W / 2
    box(0, 0, W - 0.004, H - 0.004, Dp + 0.001, Dp + 0.016, 'Housing', r=0.012, bevel=0.003)
    for i in range(5):                                                                     # 통풍 슬릿
        box(0.11, -0.17 + i * 0.022, 0.12, 0.008, Dp + 0.0158, Dp + 0.018, 'Dark', r=0.003)
    tri = [(0, 0.17), (-0.075, 0.04), (0.075, 0.04)]                                       # 경고 삼각형 + 번개
    front(tri, Dp + 0.0158, Dp + 0.0175, 'Hazard')
    bolt = [(0.008, 0.15), (-0.016, 0.095), (0.002, 0.095), (-0.01, 0.055), (0.018, 0.112), (0.0, 0.112)]
    front(bolt, Dp + 0.0172, Dp + 0.0185, 'Dark')
    box(W / 2 - 0.045, 0, 0.024, 0.11, Dp + 0.015, Dp + 0.03, 'Metal', r=0.008, bevel=0.002)   # 손잡이
    for z in (H / 2 - 0.08, -(H / 2 - 0.08)):                                              # 경첩 (문 왼쪽)
        prism(circle(-(Dp + 0.012), z, 0.008, 10), HX - 0.004, HX + 0.016, 'Metal', 'x')
    door = take()
    out = [('BreakerBox', static, None), ('BreakerDoor', door, Vector((HX, -(Dp + 0.008), 0)))]
    # 스위치 레버 (원점 = 회전축, 기본 = 가운데) + 경고등
    i = 0
    for z in SW_Z:
        for x in SW_X:
            piv = Vector((x, -0.034, z))
            box(x, z, 0.02, 0.022, 0.033, 0.088, 'White', r=0.005, bevel=0.002)             # 레버 (앞으로 나온 막대)
            box(x, z, 0.028, 0.028, 0.083, 0.097, 'Rubber', r=0.008)                          # 끝 손잡이
            out.append(('Switch%d' % i, take(), piv))
            dome((x, -0.024, z + 0.066), 0.011, 'Led', sz=0.6, u=14, v=8)
            out.append(('Led%d' % i, take(), Vector((x, -0.024, z + 0.066))))
            i += 1
    return out


# ================= 불이 나는 배전함 =================
def build_firebox():
    W, H, Dp = 0.34, 0.28, 0.11
    box(0, -0.02, W, H, 0.002, Dp, 'Device', r=0.014, bevel=0.004)
    box(0, -0.02, W - 0.03, H - 0.03, Dp, Dp + 0.008, 'Housing', r=0.01, bevel=0.002)      # 덮개
    for sx in (-1, 1):
        for sz in (-1, 1):
            cyl_y(sx * (W / 2 - 0.03), -0.02 + sz * (H / 2 - 0.03), 0.007, Dp + 0.007, Dp + 0.012, 'Metal', 10)
    # 고압 경고판
    box(0, 0.02, 0.15, 0.07, Dp + 0.0075, Dp + 0.0095, 'Hazard', r=0.005)
    front([(0.006, 0.05), (-0.012, 0.022), (0.0, 0.022), (-0.008, -0.005), (0.014, 0.03), (0.002, 0.03)], Dp + 0.0093, Dp + 0.0105, 'Dark')
    box(0, -0.075, 0.18, 0.022, Dp + 0.0075, Dp + 0.0095, 'White', r=0.004)
    # 전선관: 위 2 · 아래 1 (함 윗면에서 벽을 따라 올라가 벽 속으로)
    for x, top in ((-0.08, 0.3), (0.06, 0.28)):
        tube([Vector((x, -0.05, H / 2 - 0.03)), Vector((x, -0.05, top - 0.04)), Vector((x, -0.035, top - 0.015)), Vector((x, 0.01, top))], 0.016, 'Metal', seg=12)
        cyl_z(x, -0.05, 0.024, H / 2 - 0.022, H / 2 - 0.004, 'Dark', 14)
    tube([Vector((0.0, -0.05, -H / 2 - 0.0)), Vector((0.0, -0.05, -0.26)), Vector((0.0, -0.03, -0.28)), Vector((0.0, 0.01, -0.29))], 0.016, 'Metal', seg=12)
    static = take()
    # 그을음 (불을 끈 뒤): 함 위로 번진 세 겹 — 바깥일수록 옅고 넓게, 겹마다 깊이 다르게
    for (w, h, cz, d, mat) in ((0.46, 0.4, 0.18, 0.0012, 'ScorchL'), (0.34, 0.3, 0.16, 0.0022, 'ScorchM'), (0.22, 0.2, 0.14, 0.0032, 'Scorch')):
        pts = []
        for k in range(24):
            a = 2 * math.pi * k / 24
            rr = 1 + 0.12 * math.sin(3 * a + 0.7) + 0.07 * math.sin(5 * a)
            pts.append((w / 2 * rr * math.cos(a), cz + h / 2 * rr * math.sin(a) * (1.15 if math.sin(a) > 0 else 0.6)))
        front(pts, d - 0.0008, d, mat)
    box(0, -0.02 + H / 2 - 0.03, W - 0.04, 0.04, Dp + 0.0082, Dp + 0.0092, 'Scorch', r=0.006)  # 덮개 윗부분 그을림
    soot = take()
    return [('FireBox', static, None), ('Soot', soot, None)]


# ================= 소화기 =================
def build_extinguisher():
    # 받침: 벽 판 + 위아래 고정 띠(통을 감쌈)
    box(0, -0.03, 0.12, 0.4, 0.001, 0.012, 'Device', r=0.01, bevel=0.003)
    BY = -0.088   # 통 가운데 (벽에서 0.088)
    for z in (0.05, -0.17):
        cyl_z(0, BY, 0.076, z - 0.012, z + 0.012, 'Rubber', 28)
        box(0, z, 0.05, 0.024, 0.01, 0.02, 'Device', r=0.004)
    bracket = take()
    # 통: 몸통 · 둥근 어깨 · 바닥 테 · 흰 띠 · 목 · 머리 밸브 · 손잡이 · 압력계 · 호스 · 노즐
    cyl_z(0, BY, 0.07, -0.25, 0.1, 'Red', 28)
    dome((0, BY, 0.1), 0.07, 'Red', sz=0.55, u=28, v=12)
    cyl_z(0, BY, 0.072, -0.255, -0.235, 'Rubber', 28)
    cyl_z(0, BY, 0.0715, -0.08, -0.02, 'White', 28)
    cyl_z(0, BY, 0.022, 0.13, 0.175, 'Metal', 16)
    box(0, 0.19, 0.06, 0.04, -BY - 0.018, -BY + 0.018, 'Metal', r=0.008, bevel=0.002)     # 머리 밸브 몸통 (앞에서 본 x, z)
    tube([Vector((-0.005, BY, 0.205)), Vector((0.06, BY, 0.235)), Vector((0.1, BY, 0.235))], 0.008, 'Dark', seg=8)  # 위 손잡이 레버
    tube([Vector((-0.005, BY, 0.18)), Vector((0.06, BY, 0.19)), Vector((0.095, BY, 0.2))], 0.008, 'Dark', seg=8)    # 아래 손잡이
    cyl_y(-0.032, 0.19, 0.016, -BY + 0.012, -BY + 0.026, 'Metal', 16)
    cyl_y(-0.032, 0.19, 0.012, -BY + 0.025, -BY + 0.028, 'White', 16)
    hose = [Vector((0.03, BY - 0.012, 0.18)), Vector((0.07, BY - 0.03, 0.13)), Vector((0.085, BY - 0.04, 0.02)), Vector((0.084, BY - 0.04, -0.12)),
            Vector((0.08, BY - 0.035, -0.15))]
    tube(hose, 0.011, 'Rubber', seg=10)
    cyl_z(0.08, BY - 0.035, 0.016, -0.2, -0.145, 'Dark', 12)                               # 노즐
    bottle = take()
    return [('ExtBracket', bracket, None), ('ExtBottle', bottle, None)]


# ================= 공기 정화 필터함 =================
def build_filter():
    W, H, Dp = 0.6, 0.5, 0.13
    box(0, 0, W, H, 0.002, Dp, 'Housing', r=0.02, bevel=0.006)
    # 위 환기 그릴 (어두운 틈 + 비스듬한 살)
    box(0, 0.12, W - 0.08, 0.18, Dp - 0.004, Dp + 0.002, 'Dark', r=0.008)
    for i in range(7):
        z = 0.045 + i * 0.024
        prism([(-(Dp + 0.0), z + 0.007), (-(Dp + 0.008), z - 0.002), (-(Dp + 0.008), z - 0.008), (-(Dp + 0.0), z + 0.001)],
              -(W / 2 - 0.045), W / 2 - 0.045, 'Housing', 'x')
    # 아래 카트리지 칸 (어두운 구멍 테)
    box(0, -0.12, W - 0.08, 0.2, Dp - 0.004, Dp + 0.003, 'Dark', r=0.008)
    # 작은 화면 · 상태등 자리
    box(0.2, 0.225 - 0.0, 0.12, 0.03, Dp, Dp + 0.004, 'Device', r=0.004)
    box(0.19, 0.225, 0.07, 0.016, Dp + 0.0035, Dp + 0.005, 'Screen', r=0.002)
    for sx in (-1, 1):                                                                         # 모서리 나사
        for sz in (-1, 1):
            cyl_y(sx * (W / 2 - 0.022), sz * (H / 2 - 0.022), 0.007, Dp - 0.001, Dp + 0.004, 'Metal', 10)
    unit = take()
    dome((-0.22, -(Dp + 0.002), 0.225), 0.012, 'Led', sz=0.6, u=14, v=8)
    led = take()
    # 카트리지 (원점 = 가운데): 틀 + 주름 앞면 + 당김 손잡이
    CZ = -0.12
    box(0, CZ, W - 0.1, 0.18, 0.02, Dp + 0.006, 'Device', r=0.006)                             # 틀 (칸 안으로 들어감)
    n = 15
    for i in range(n):
        x = -(W / 2 - 0.07) + i * (W - 0.14) / (n - 1)
        prism([(x - 0.009, CZ + 0.075), (x + 0.009, CZ + 0.075), (x + 0.009, CZ - 0.075), (x - 0.009, CZ - 0.075)],
              -(Dp + 0.006), -(Dp + 0.012 + 0.002 * (i % 2)), 'Filter', 'y')
    box(0, CZ + 0.0, 0.12, 0.03, Dp + 0.012, Dp + 0.03, 'Metal', r=0.008, bevel=0.002)        # 당김 손잡이
    cart = take()
    return [('FilterUnit', unit, None), ('FilterLed', led, None), ('Cartridge', cart, Vector((0, -(Dp + 0.01), CZ)))]


def finish(name, parts, origin=None):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = []
    for _, mat in parts:
        if mat not in used: used.append(mat)
    for n in used: me.materials.append(MATS[n])
    big = bmesh.new()
    for bm, mat in parts:
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = used.index(mat)
        bm.free()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    big.to_mesh(me); big.free()
    if origin is not None:
        me.transform(Matrix.Translation(-origin)); ob.location = origin
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
    bm.free()
    b0 = [min(v.co[i] for v in me.vertices) for i in range(3)]; b1 = [max(v.co[i] for v in me.vertices) for i in range(3)]
    print('INC %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d bounds x %.3f~%.3f y %.3f~%.3f z %.3f~%.3f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, b0[0], b1[0], b0[1], b1[1], b0[2], b1[2]))
    return ob


groups = {}
for name, fn in (('IncLeak', build_leak), ('IncBreaker', build_breaker), ('IncFireBox', build_firebox),
                 ('IncExtinguisher', build_extinguisher), ('IncFilter', build_filter)):
    groups[name] = [finish(n, parts, origin) for n, parts, origin in fn()]

layout = {'IncLeak': (-1.6, 0), 'IncBreaker': (-0.8, 0), 'IncFireBox': (0.0, 0), 'IncExtinguisher': (0.7, 0), 'IncFilter': (1.5, 0)}
for name, obs in groups.items():
    off = Vector((layout[name][0], 0, layout[name][1]))
    for ob in obs:
        ob['base'] = list(ob.location)
        ob.location = ob.location + off
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'incident.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for name, obs in groups.items():
        locs = [ob.location.copy() for ob in obs]
        for ob in obs: ob.location = Vector(ob['base'])
        for x in bpy.context.view_layer.objects: x.select_set(x in obs)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_%s.fbx' % name), use_selection=True, object_types={'MESH'},
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                                 mesh_smooth_type='FACE')
        for ob, l in zip(obs, locs): ob.location = l

# 미리보기: 벽판 하나 뒤에 두고, 차단기 문은 열고 스위치 일부 내림, LED 색 · 그을음 표시
wall = bpy.data.meshes.new('Wall'); wo = bpy.data.objects.new('Wall', wall); bpy.context.scene.collection.objects.link(wo)
bmw = bmesh.new(); bmesh.ops.create_cube(bmw, size=1.0); bmw.to_mesh(wall); bmw.free()
wo.scale = (4.2, 0.05, 1.2); wo.location = (0, 0.026, 0)
wm = bpy.data.materials.new('WallPrev'); wm.use_nodes = True
next(x for x in wm.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (0.5, 0.5, 0.52, 1); wall.materials.append(wm)
bpy.data.objects['BreakerDoor'].rotation_euler = (0, 0, math.radians(-105))
for i in range(6):
    bpy.data.objects['Switch%d' % i].rotation_euler = (math.radians(28 if i in (1, 4) else -28), 0, 0)
for nm, col in (('M_IncLed', (0.9, 0.1, 0.08, 1)), ('M_IncBand', (0.2, 0.5, 0.95, 1)), ('M_IncFilter', (0.45, 0.36, 0.22, 1))):
    b = next(x for x in MATS[nm[5:]].node_tree.nodes if x.type == 'BSDF_PRINCIPLED'); b.inputs['Base Color'].default_value = col
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng; break
    except TypeError:
        pass
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.4, 0.42, 0.46, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.0
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(60), 0, math.radians(-25))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d, cx, scale in (('q', Vector((0.35, -0.85, 0.3)), 0.0, 3.8), ('front', Vector((0, -1, 0.0)), 0.0, 3.8)):
    d = d.normalized(); cam.location = Vector((cx, 0, 0)) + d * 4; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 1400; sc.render.resolution_y = 460
    sc.render.filepath = os.path.join(pv, 'incident_%s.png' % tag); bpy.ops.render.render(write_still=True)
for name in layout:
    cx = layout[name][0]
    d = Vector((0.45, -0.85, 0.25)).normalized()
    cam.location = Vector((cx, -0.08, 0)) + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = 0.85; sc.render.resolution_x = 600; sc.render.resolution_y = 600
    sc.render.filepath = os.path.join(pv, 'incident_%s.png' % name); bpy.ops.render.render(write_still=True)
