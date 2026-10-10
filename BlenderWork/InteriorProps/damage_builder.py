# 11-17 ① 현장 긴급 수리 — 파손 지점 소품 (2026-10-11)
# 파손된 방의 벽에 붙는 "왜 연기 · 스파크가 나는지" 보이는 소품 3종 + 고친 자리에 남는 덧댄 판 1종
#   DmgPanel  : 뜯겨 나간 벽 점검 패널 — 틀 · 안쪽 공간 · 꺾여 매달린 덮개 · 늘어진 전선 3가닥(구리 끝) · 그을음
#   DmgPipe   : 터진 배관 — 벽에서 나와 가로지르는 관 · 가운데가 찢겨 벌어짐(톱니 끝) · 꺾인 오른쪽 · 받침 2개 · 젖은 얼룩
#   DmgBox    : 불탄 배전함 — 속 빈 함 · 열린 문 · 차단기 2줄(일부 녹아 기울어짐) · 아래로 나가는 배관 3개 · 위로 번진 그을음
#   DmgPatch  : 고친 자리 — 리벳 박은 덧댄 판 · 용접 자국 테두리 · 경고 줄무늬 테이프
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/damage_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 벽면 = Blender xz 평면(y = 0), 방 쪽 = Blender −y (Unity +z — LookRotation(벽 법선)으로 붙임). 원점 = 벽면 위 소품 가운데
# 같은 평면 겹침 금지(MODELING.md 3): 벽에 붙는 판의 뒷면은 서로 다른 깊이(d)에서 시작
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'Panel': ((0.42, 0.45, 0.49), 0.35),    # 벽 패널 금속
    'Metal': ((0.55, 0.58, 0.62), 0.55),    # 관 · 받침 · 리벳
    'Device': ((0.16, 0.18, 0.2), 0.3),     # 배전함 · 장비 몸통
    'Dark': ((0.03, 0.03, 0.035), 0.1),     # 안쪽 빈 공간
    'Scorch': ((0.035, 0.03, 0.028), 0.05), # 그을음 · 탄 부품
    'ScorchM': ((0.13, 0.12, 0.115), 0.05), # 그을음 중간 겹
    'ScorchL': ((0.3, 0.29, 0.28), 0.08),   # 그을음 바깥 겹 (벽보다 조금 어두움)
    'Copper': ((0.85, 0.48, 0.22), 0.6),    # 전선 끝
    'WireRed': ((0.7, 0.08, 0.06), 0.3),
    'WireYellow': ((0.9, 0.7, 0.1), 0.3),
    'WireBlack': ((0.05, 0.05, 0.06), 0.3),
    'Hazard': ((0.95, 0.72, 0.08), 0.35),   # 경고 노랑
    'Weld': ((0.3, 0.29, 0.28), 0.4),       # 용접 자국
    'Stain': ((0.1, 0.11, 0.1), 0.6),       # 젖은 얼룩 (번들)
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_Dmg' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    m.diffuse_color = c + (1,)
    MATS[n] = m
PARTS = []
rng = random.Random(11)


def P(x, d, z):
    """소품 좌표 (가로 x, 벽에서 나온 거리 d, 높이 z) → Blender (x, −d, z)."""
    return Vector((x, -d, z))


def orient_outward(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def add(bm, mat):
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def rrect(cx, cz, w, h, r, seg=4):
    pts = []
    r = min(r, w / 2 - 1e-4, h / 2 - 1e-4)
    for ox, oz, a0 in ((w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)))
    return pts


def slab_pts(pts, d0, d1, mat, bevel=0.0):
    """벽에서 본 단면(x, z) 다각형을 d0 ~ d1 두께로."""
    bm = bmesh.new()
    lo = [bm.verts.new(P(x, d0, z)) for x, z in pts]
    hi = [bm.verts.new(P(x, d1, z)) for x, z in pts]
    n = len(pts)
    bm.faces.new(hi); bm.faces.new(list(reversed(lo)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    orient_outward(bm)
    if bevel > 0:
        cap = [e for e in bm.edges if e.verts[0].co.y == e.verts[1].co.y]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=1, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return add(bm, mat)


def box(cx, cz, w, h, d0, d1, mat, r=0.004, bevel=0.0):
    return slab_pts(rrect(cx, cz, w, h, r, 2), d0, d1, mat, bevel)


def frame(outer, inner_back, inner_front, d0, d1, mat):
    bm = bmesh.new()
    ob = [bm.verts.new(P(x, d0, z)) for x, z in outer]
    of = [bm.verts.new(P(x, d1, z)) for x, z in outer]
    fi = [bm.verts.new(P(x, d1, z)) for x, z in inner_front]
    bi = [bm.verts.new(P(x, d0, z)) for x, z in inner_back]
    n = len(outer)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((ob[i], ob[j], of[j], of[i]))
        bm.faces.new((of[i], of[j], fi[j], fi[i]))
        bm.faces.new((fi[i], fi[j], bi[j], bi[i]))
        bm.faces.new((bi[i], bi[j], ob[j], ob[i]))
    return add(bm, mat)


def frame_ring(p, axis, r, seg):
    """점 p에서 axis에 수직인 원 (seg개 점)."""
    axis = axis.normalized()
    up = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((1, 0, 0))
    u = axis.cross(up).normalized(); v = axis.cross(u).normalized()
    return [p + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r for i in range(seg)]


def tube(points, r, mat, seg=10, end_jag=(0.0, 0.0)):
    """폴리라인(Blender 좌표)을 따라가는 관 + 양 끝 막음. end_jag = (시작, 끝) 톱니 깊이 — 찢긴 관 끝."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        if k == 0: t = points[1] - points[0]
        elif k == n - 1: t = points[-1] - points[-2]
        else: t = (points[k + 1] - points[k - 1])
        ring = frame_ring(p, t, r, seg)
        jag = end_jag[0] if k == 0 else end_jag[1] if k == n - 1 else 0.0
        if jag > 0:
            tn = t.normalized() * (-1 if k == 0 else 1)
            ring = [q + tn * (jag if i % 2 == 0 else -jag * 0.3) * (0.6 + 0.4 * rng.random()) for i, q in enumerate(ring)]
        rings.append([bm.verts.new(q) for q in ring])
    for k in range(n - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    return add(bm, mat)


def cyl(p0, p1, r, mat, seg=16):
    return tube([p0, p1], r, mat, seg)


def blob(cx, cz, rx, rz, d0, d1, mat, n=14, wobble=0.25):
    """불규칙한 얼룩 판 (그을음 · 젖은 자국)."""
    pts = []
    for i in range(n):
        a = 2 * math.pi * i / n
        k = 1 + wobble * (rng.random() * 2 - 1)
        pts.append((cx + rx * k * math.cos(a), cz + rz * k * math.sin(a)))
    return slab_pts(pts, d0, d1, mat)


def scorch(cx, cz, rx, rz, mat_core='Scorch'):
    """그을음 · 얼룩: 바깥이 옅고 가운데가 진한 세 겹 (한 장이면 각진 검은 판처럼 보였음). 겹마다 깊이를 달리해 같은 평면 없음."""
    blob(cx, cz, rx, rz, 0.0012, 0.0017, 'ScorchL', n=24, wobble=0.18)
    blob(cx, cz + rz * 0.05, rx * 0.72, rz * 0.72, 0.0019, 0.0024, 'ScorchM', n=22, wobble=0.22)
    blob(cx, cz + rz * 0.1, rx * 0.42, rz * 0.42, 0.0026, 0.0031, mat_core, n=20, wobble=0.26)


def rotate(bm, pivot, axis, deg):
    m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-pivot)
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def bezier(p0, p1, p2, p3, steps=10):
    out = []
    for i in range(steps + 1):
        t = i / steps
        out.append(p0 * (1 - t) ** 3 + p1 * 3 * (1 - t) ** 2 * t + p2 * 3 * (1 - t) * t * t + p3 * t ** 3)
    return out


# ================= DmgPanel =================
def build_panel():
    scorch(0.0, 0.02, 0.5, 0.42)
    W, H = 0.62, 0.5
    frame(rrect(0, 0, W, H, 0.03), rrect(0, 0, 0.5, 0.38, 0.015), rrect(0, 0, 0.52, 0.40, 0.018), 0.0040, 0.0300, 'Panel')
    box(0, 0, 0.51, 0.39, 0.0035, 0.0062, 'Dark', r=0.015)                     # 안쪽 바닥 (뚫린 속)
    for x in (-0.17, 0.17):
        box(x, 0, 0.025, 0.36, 0.0062, 0.0160, 'Metal', r=0.004)                 # 안쪽 레일
    box(-0.05, 0.06, 0.16, 0.1, 0.0070, 0.0240, 'Device', r=0.008, bevel=0.002)  # 안쪽 장비 상자
    # 꺾여 매달린 덮개: 아래 경첩(z −0.19, d 0.03)에서 바깥 · 아래로 두 번 꺾임
    low = box(0, -0.19 + 0.1, 0.48, 0.2, 0.0300, 0.0420, 'Panel', r=0.006)
    rotate(low, P(0, 0.03, -0.19), Vector((1, 0, 0)), 148)   # 경첩 아래로 늘어져 매달림 (180 = 벽에 납작)
    # 위 조각은 아래 조각 끝에서 더 꺾임 (끝 위치 = 경첩 + 회전한 0.2)
    a1 = math.radians(148)
    tip = P(0, 0.03, -0.19) + Vector((0, -math.sin(a1) * 0.2, math.cos(a1) * 0.2))
    up = box(0, -0.19 + 0.2 + 0.09, 0.47, 0.18, 0.0305, 0.0415, 'Panel', r=0.006)
    rotate(up, P(0, 0.03, -0.19), Vector((1, 0, 0)), 148)
    rotate(up, tip, Vector((1, 0, 0)), -24)
    # 늘어진 전선 3가닥 + 구리 끝
    for x0, mat, drop, side in ((-0.09, 'WireRed', 0.42, -0.06), (0.0, 'WireYellow', 0.5, 0.02), (0.08, 'WireBlack', 0.38, 0.09)):
        pts = bezier(P(x0, 0.012, 0.08), P(x0, 0.12, 0.1), P(x0 + side, 0.2, -0.05), P(x0 + side * 1.4, 0.16, -drop))
        tube(pts, 0.011, mat, seg=8)
        end = pts[-1]
        d = (pts[-1] - pts[-2]).normalized()
        for k, off in enumerate((Vector((0.006, 0, 0)), Vector((-0.006, 0, 0.002)), Vector((0, 0.006, -0.002)))):
            cyl(end + off * 0.6, end + off * 2.0 + d * 0.03, 0.0028, 'Copper', seg=6)


# ================= DmgPipe =================
def build_pipe():
    scorch(0.02, -0.2, 0.3, 0.2, 'Stain')
    R, D = 0.045, 0.12
    # 벽에서 나오는 양 끝 (꺾임 이음 + 테)
    for sx in (-1, 1):
        x = sx * 0.58
        cyl(P(x, 0.002, 0), P(x, D, 0), R, 'Metal')
        cyl(P(x, 0.0025, 0), P(x, 0.022, 0), R + 0.022, 'Metal')          # 벽 테
        cyl(P(x, D - R - 0.004, 0), P(x, D + R + 0.004, 0), R + 0.004, 'Metal')  # 꺾임 덩어리
    # 왼쪽 관 (곧음, 찢긴 끝)
    tube([P(-0.58, D, 0), P(-0.3, D, 0), P(-0.1, D + 0.005, 0.01), P(-0.05, D + 0.02, 0.045)], R, 'Metal', seg=14, end_jag=(0.0, 0.035))
    # 오른쪽 관: 찢긴 끝이 아래 · 앞으로 꺾여 벌어짐
    tube([P(0.58, D, 0), P(0.32, D, 0), P(0.16, D + 0.015, -0.03), P(0.07, D + 0.055, -0.1)], R, 'Metal', seg=14, end_jag=(0.0, 0.035))
    # 이음 테 (관 위)
    for x in (-0.42, 0.42):
        cyl(P(x - 0.012, D, 0), P(x + 0.012, D, 0), R + 0.012, 'Metal')
    # 받침 2개: 벽 판 + 팔 + 관을 감싼 띠
    for x in (-0.3, 0.3):
        box(x, 0, 0.06, 0.16, 0.0020, 0.0140, 'Device', r=0.006)
        cyl(P(x, 0.014, -0.05), P(x, D - R + 0.004, -0.05), 0.012, 'Device', seg=10)
        cyl(P(x - 0.02, D, 0), P(x + 0.02, D, 0), R + 0.009, 'Device', seg=16)
    # 경고 표지 (관 위 작은 판)
    box(-0.42, 0.13, 0.12, 0.06, 0.0018, 0.0060, 'Hazard', r=0.004)


# ================= DmgBox =================
def build_box():
    scorch(0.0, 0.3, 0.32, 0.4)                                               # 위로 번진 그을음
    BW, BH, BD = 0.42, 0.52, 0.16
    frame(rrect(0, 0, BW, BH, 0.02), rrect(0, 0, 0.36, 0.46, 0.012), rrect(0, 0, 0.37, 0.47, 0.014), 0.0035, BD, 'Device')
    box(0, 0, 0.365, 0.465, 0.0032, 0.0200, 'Dark', r=0.012)                  # 함 안쪽 뒤판
    # 차단기 2줄 × 6 (일부 탐 · 기울어짐)
    for row, z in enumerate((0.1, -0.08)):
        box(0, z, 0.33, 0.012, 0.0200, 0.0450, 'Metal', r=0.003)              # 레일
        for i in range(6):
            x = -0.14 + i * 0.056
            burnt = (row, i) in ((0, 3), (0, 4), (1, 1))
            b = box(x, z, 0.04 + 0.001 * (i % 2), 0.085, 0.0450 + 0.0006 * i, 0.0820 + 0.0007 * i, 'Scorch' if burnt else 'Panel', r=0.004)
            if burnt:
                rotate(b, P(x, 0.045, z - 0.04), Vector((1, 0, 0)), -14 - 6 * i % 9)
            else:
                box(x, z + 0.02, 0.012, 0.02, 0.0830 + 0.0007 * i, 0.0950 + 0.0007 * i, 'Device', r=0.003)  # 손잡이
    # 열린 문: 왼쪽 경첩(x −0.185, d BD)에서 110° 열림
    door = box(0, 0, 0.37, 0.47, BD + 0.001, BD + 0.016, 'Device', r=0.014)
    knob = box(0.15, 0, 0.02, 0.07, BD + 0.016, BD + 0.03, 'Metal', r=0.005)
    sticker = box(0.0, 0.12, 0.12, 0.08, BD + 0.016, BD + 0.0185, 'Hazard', r=0.006)
    for bm in (door, knob, sticker):
        rotate(bm, P(-0.185, BD + 0.008, 0), Vector((0, 0, 1)), -110)
    # 아래로 나가는 배관 3개 (벽으로 꺾여 들어감)
    for x in (-0.11, 0.0, 0.11):
        tube([P(x, 0.08, -BH / 2 + 0.01), P(x, 0.08, -0.42), P(x, 0.04, -0.47), P(x, 0.002, -0.48)], 0.016, 'Metal', seg=10)


# ================= DmgPatch =================
def build_patch():
    PW, PH = 0.56, 0.44
    frame(rrect(0, 0, PW + 0.03, PH + 0.03, 0.03), rrect(0, 0, PW - 0.004, PH - 0.004, 0.02), rrect(0, 0, PW - 0.004, PH - 0.004, 0.02), 0.0020, 0.0095, 'Weld')
    box(0, 0, PW, PH, 0.0025, 0.0080, 'Metal', r=0.02, bevel=0.0015)
    for i in range(10):                                                        # 리벳 둘레
        a = 2 * math.pi * i / 10
        x, z = 0.24 * math.cos(a), 0.18 * math.sin(a)
        cyl(P(x, 0.0070, z), P(x, 0.0125, z), 0.008, 'Metal', seg=10)
    # 경고 줄무늬 테이프 2줄 (칸마다 두께를 엇갈려 꼭짓점이 합쳐지지 않게, MODELING.md 3)
    for z in (0.11, -0.11):
        for k in range(8):
            x = -0.21 + k * 0.06
            box(x, z, 0.06 - 0.002 * (k % 2), 0.045, 0.0080, 0.0094 + 0.0006 * (k % 2), 'Hazard' if k % 2 == 0 else 'WireBlack', r=0.001)


def finish(name):
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = []
    for _, mat in PARTS:
        if mat not in used: used.append(mat)
    for n in used: me.materials.append(MATS[n])
    big = bmesh.new()
    for bm, mat in PARTS:
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = used.index(mat)
        bm.free()
    PARTS.clear()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    big.to_mesh(me); big.free()
    for p in me.polygons: p.use_smooth = True
    bpy.context.view_layer.objects.active = ob
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(35))
    except Exception as e:
        print('smooth skipped', e)
    # 검사: 열린 · 비다양체 가장자리, 덩어리별 부호 있는 부피
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
    print('DMG %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d bounds x %.3f~%.3f d %.3f~%.3f z %.3f~%.3f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, b0[0], b1[0], -b1[1], -b0[1], b0[2], b1[2]))
    return ob


objs = []
for name, fn in (('DmgPanel', build_panel), ('DmgPipe', build_pipe), ('DmgBox', build_box), ('DmgPatch', build_patch)):
    fn()
    objs.append(finish(name))
for i, ob in enumerate(objs):
    ob.location.x = i * 1.6 - 2.4
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'damage.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for ob in objs:
        loc = ob.location.copy(); ob.location = (0, 0, 0)
        for x in bpy.context.view_layer.objects: x.select_set(x is ob)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_%s.fbx' % ob.name), use_selection=True, object_types={'MESH'},
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                                 mesh_smooth_type='FACE')
        ob.location = loc

# 미리보기: 벽 판 앞에 네 소품
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng; break
    except TypeError:
        pass
wall = bpy.data.meshes.new('Wall'); wo = bpy.data.objects.new('Wall', wall); sc.collection.objects.link(wo)
bm = bmesh.new(); bmesh.ops.create_cube(bm, size=1.0); bm.to_mesh(wall); bm.free()
wo.scale = (7.0, 0.02, 1.8); wo.location = (0, 0.012, 0)
wm = bpy.data.materials.new('WallMat'); wm.use_nodes = True
next(x for x in wm.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (0.62, 0.64, 0.66, 1)
wall.materials.append(wm)
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.35, 0.37, 0.4, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.0
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(55), 0, math.radians(-30))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d, scale, target in (('front', Vector((0, -1, 0.0)), 6.4, Vector((0, 0, 0))), ('q', Vector((0.55, -0.8, 0.35)), 6.4, Vector((0, 0, 0))),
                              ('panel', Vector((0.5, -0.8, 0.25)), 0.9, Vector((-2.4, 0, -0.05))), ('pipe', Vector((0.4, -0.8, 0.45)), 1.0, Vector((-0.8, 0, -0.05))),
                              ('box', Vector((-0.45, -0.8, 0.3)), 0.95, Vector((0.8, 0, 0.05))), ('patch', Vector((0.3, -0.9, 0.3)), 0.75, Vector((2.4, 0, 0)))):
    d = d.normalized(); cam.location = target + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 760; sc.render.resolution_y = 520
    sc.render.filepath = os.path.join(pv, 'dmg_%s.png' % tag); bpy.ops.render.render(write_still=True)
