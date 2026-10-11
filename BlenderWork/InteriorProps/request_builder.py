# 11-17 ③ 주민 요청 소품 (2026-10-11)
#   ReqToolbox : 공구함 — 빨간 몸통 · 뚜껑 이음 · 접이 손잡이 · 잠금쇠 2 · 앞 이름표
#   ReqMedkit  : 구급함 — 흰 하드 케이스 · 회색 이음 띠 · 뚜껑 빨간 십자 · 앞 손잡이 · 잠금쇠
#   ReqSnack   : 간식 바구니 — 엮은 바구니(세로 살) · 체크 천 · 사과 · 귤 · 빵 · 아치 손잡이
#   ReqVent    : 덜컹거리는 환풍구 (벽 소품) — 틀(VentFrame) + 덮개(VentCover, 원점 = 왼쪽 위 나사 = 흔들리는 축)
#                + 나머지 나사 3개(VentScrews, 고치면 보임)
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/request_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 바닥 소품 = 바닥 z 0 · 앞 = Blender −y (Unity +z), 원점 = 바닥 가운데
#       벽 소품 = 벽면이 Blender xz 평면(y 0), 방 쪽 = −y, 원점 = 환풍구 가운데
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'TbRed': ((0.72, 0.12, 0.1), 0.35),
    'TbDark': ((0.08, 0.08, 0.09), 0.2),
    'Metal': ((0.66, 0.68, 0.72), 0.6),
    'Label': ((0.88, 0.86, 0.78), 0.2),
    'KitWhite': ((0.9, 0.91, 0.9), 0.45),
    'KitGray': ((0.42, 0.45, 0.48), 0.3),
    'KitRed': ((0.82, 0.1, 0.12), 0.4),
    'Wicker': ((0.62, 0.43, 0.22), 0.15),
    'WickerDark': ((0.42, 0.27, 0.13), 0.15),
    'Cloth': ((0.82, 0.3, 0.3), 0.05),
    'Apple': ((0.75, 0.08, 0.08), 0.55),
    'Orange': ((0.95, 0.5, 0.08), 0.35),
    'Bread': ((0.78, 0.55, 0.28), 0.15),
    'Leaf': ((0.25, 0.55, 0.2), 0.3),
    'VentFrame': ((0.6, 0.62, 0.65), 0.4),
    'VentGrille': ((0.46, 0.49, 0.52), 0.4),
    'VentDark': ((0.05, 0.05, 0.06), 0.1),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_' + n); m.use_nodes = True
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


def to3(axis, u, v, w):
    if axis == 'z': return (u, v, w)      # 위에서 본 단면 (x, y), 높이 w
    if axis == 'y': return (u, w, v)      # 앞에서 본 단면 (x, z), 깊이 w
    return (w, u, v)                      # 옆에서 본 단면 (y, z), 가로 w


def prism(pts, a0, a1, mat, axis='z', bevel=0.0, seg=2):
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


def ring_prism(outer, inner, a0, a1, mat, axis='y'):
    """구멍 뚫린 판 (바깥 · 안쪽 윤곽 점 수가 같아야 함)."""
    bm = bmesh.new()
    n = len(outer)
    ol = [bm.verts.new(to3(axis, u, v, a0)) for u, v in outer]
    oh = [bm.verts.new(to3(axis, u, v, a1)) for u, v in outer]
    il = [bm.verts.new(to3(axis, u, v, a0)) for u, v in inner]
    ih = [bm.verts.new(to3(axis, u, v, a1)) for u, v in inner]
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((ol[i], ol[j], oh[j], oh[i]))   # 바깥 옆
        bm.faces.new((ih[i], ih[j], il[j], il[i]))   # 안쪽 옆
        bm.faces.new((oh[i], oh[j], ih[j], ih[i]))   # 한쪽 면
        bm.faces.new((il[i], il[j], ol[j], ol[i]))   # 반대 면
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def block(cx, cy, z0, w, d, h, mat, r=0.01, bevel=0.0):
    """위에서 본 둥근 직사각형(가운데 cx, cy — Blender 좌표)을 z0 ~ z0+h로."""
    return prism(rrect(cx, cy, w, d, r, 3), z0, z0 + h, mat, 'z', bevel)


def circle(cx, cy, r, seg=20):
    return [(cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def tube(points, r, mat, seg=10):
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


def blob(c, r, mat, sx=1.0, sy=1.0, sz=1.0, u=16, v=10):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=r)
    for vt in bm.verts:
        vt.co = Vector((vt.co.x * sx + c[0], vt.co.y * sy + c[1], vt.co.z * sz + c[2]))
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def arch(x0, x1, z0, top, y=0.0, n=12):
    """x0 → x1 아치 (가운데 높이 top) 점 목록."""
    return [Vector((x0 + (x1 - x0) * i / n, y, z0 + (top - z0) * math.sin(math.pi * i / n))) for i in range(n + 1)]


# ================= 공구함 =================
def build_toolbox():
    W, D = 0.44, 0.2
    block(0, 0, 0.0, W - 0.03, D - 0.03, 0.014, 'TbDark', r=0.01)                         # 바닥 받침
    block(0, 0, 0.012, W, D, 0.13, 'TbRed', r=0.014, bevel=0.006)                          # 몸통 (윗면 0.142)
    block(0, 0, 0.144, W + 0.006, D + 0.006, 0.046, 'TbRed', r=0.016, bevel=0.009)         # 뚜껑 (이음 틈 2mm)
    block(0, 0, 0.13, W + 0.004, D + 0.004, 0.0115, 'TbDark', r=0.016)                     # 이음 띠 (어두운 선, 윗면은 몸통 아래)
    # 접이 손잡이: 받침 2 + 고무 손잡이 봉
    for x in (-0.13, 0.13):
        block(x, 0, 0.188, 0.04, 0.05, 0.016, 'TbDark', r=0.012, bevel=0.003)
    tube(arch(-0.13, 0.13, 0.198, 0.25), 0.012, 'TbDark', seg=10)
    # 잠금쇠 2 (이음 띠를 가로지름)
    for x in (-0.15, 0.15):
        prism(rrect(x, 0.145, 0.045, 0.06, 0.008, 2), -(D / 2 + 0.014), -(D / 2 - 0.004), 'Metal', 'y', bevel=0.002)
    # 앞 이름표 + 글자 자리
    prism(rrect(0, 0.075, 0.13, 0.05, 0.008, 2), -(D / 2 + 0.006), -(D / 2 - 0.003), 'Label', 'y', bevel=0.002)
    prism(rrect(-0.012, 0.08, 0.08, 0.01, 0.003, 2), -(D / 2 + 0.0085), -(D / 2 + 0.005), 'TbDark', 'y')
    prism(rrect(-0.025, 0.064, 0.055, 0.008, 0.003, 2), -(D / 2 + 0.0083), -(D / 2 + 0.005), 'TbDark', 'y')
    # 옆 끝 금속 모서리 보강
    for sx in (1, -1):
        prism(rrect(0, 0.075, D - 0.04, 0.09, 0.012, 2), sx * (W / 2 - 0.004), sx * (W / 2 + 0.006), 'Metal', 'x', bevel=0.002)


# ================= 구급함 =================
def cross_pts(cx, cy, a, t):
    """가운데 (cx, cy), 팔 길이 a(가운데에서 끝까지), 팔 두께 t의 십자 윤곽."""
    h = t / 2
    return [(cx + h, cy + a), (cx - h, cy + a), (cx - h, cy + h), (cx - a, cy + h), (cx - a, cy - h), (cx - h, cy - h),
            (cx - h, cy - a), (cx + h, cy - a), (cx + h, cy - h), (cx + a, cy - h), (cx + a, cy + h), (cx + h, cy + h)]


def build_medkit():
    W, D, H = 0.36, 0.26, 0.12
    block(0, 0, 0.0, W - 0.04, D - 0.04, 0.01, 'KitGray', r=0.02)                          # 바닥 받침
    block(0, 0, 0.008, W, D, H - 0.008, 'KitWhite', r=0.035, bevel=0.012)                  # 몸통 (윗면 0.12)
    block(0, 0, 0.055, W + 0.006, D + 0.006, 0.016, 'KitGray', r=0.038, bevel=0.003)       # 이음 띠
    prism(cross_pts(0, 0, 0.065, 0.04), H - 0.002, H + 0.004, 'KitRed', 'z', bevel=0.0015) # 뚜껑 빨간 십자
    # 앞 손잡이 (받침 2 + 봉)
    for x in (-0.07, 0.07):
        prism(rrect(x, 0.063, 0.03, 0.03, 0.008, 2), -(D / 2 + 0.016), -(D / 2 - 0.004), 'KitGray', 'y', bevel=0.003)
    tube([Vector((-0.07, -(D / 2 + 0.012), 0.063)), Vector((-0.06, -(D / 2 + 0.034), 0.063)), Vector((0.06, -(D / 2 + 0.034), 0.063)),
          Vector((0.07, -(D / 2 + 0.012), 0.063))], 0.009, 'KitGray', seg=8)
    # 잠금쇠 2
    for x in (-0.13, 0.13):
        prism(rrect(x, 0.063, 0.035, 0.05, 0.008, 2), -(D / 2 + 0.013), -(D / 2 - 0.004), 'Metal', 'y', bevel=0.002)
    # 앞면 작은 십자
    prism(cross_pts(0, 0.093, 0.014, 0.009), -(D / 2 + 0.008), -(D / 2 - 0.004), 'KitRed', 'y')


# ================= 간식 바구니 =================
def build_snack():
    W, D, H = 0.36, 0.24, 0.13
    block(0, 0, 0.0, W - 0.03, D - 0.03, 0.012, 'WickerDark', r=0.03)                      # 바닥 테
    block(0, 0, 0.01, W, D, H - 0.01, 'Wicker', r=0.045, bevel=0.008)                      # 바구니 몸통 (윗면 0.13)
    block(0, 0, H - 0.012, W + 0.012, D + 0.012, 0.018, 'WickerDark', r=0.05, bevel=0.005) # 위 테두리
    # 엮은 세로 살 (앞뒤 · 양옆, 몸통 밖으로 2~4mm, 이웃 살과 깊이를 엇갈림)
    for i in range(7):
        x = -0.105 + i * 0.035
        for sy in (1, -1):
            d0 = D / 2 - 0.004
            prism(rrect(x, 0.065, 0.012, 0.09, 0.004, 1), sy * d0, sy * (D / 2 + 0.003 + 0.001 * (i % 2)), 'WickerDark', 'y')
    for i in range(5):
        y = -0.07 + i * 0.035
        for sx in (1, -1):
            prism(rrect(y, 0.065, 0.012, 0.09, 0.004, 1), sx * (W / 2 - 0.004), sx * (W / 2 + 0.003 + 0.001 * (i % 2)), 'WickerDark', 'x')
    # 체크 천 (가운데가 살짝 부푼 판)
    block(0, 0.0, H + 0.004, W - 0.05, D - 0.05, 0.012, 'Cloth', r=0.04, bevel=0.004)
    # 간식: 사과 2 · 귤 2 · 빵 1
    blob((-0.09, 0.03, H + 0.055), 0.042, 'Apple', sz=0.92)
    tube([Vector((-0.09, 0.03, H + 0.09)), Vector((-0.087, 0.032, H + 0.108))], 0.004, 'WickerDark', seg=6)
    blob((-0.035, -0.045, H + 0.05), 0.038, 'Apple', sz=0.92)
    blob((0.03, 0.05, H + 0.048), 0.037, 'Orange', sz=0.88)
    blob((0.036, 0.05, H + 0.08), 0.012, 'Leaf', sx=1.6, sy=0.9, sz=0.5)
    blob((0.075, -0.035, H + 0.046), 0.034, 'Orange', sz=0.88)
    blob((0.1, 0.045, H + 0.045), 0.055, 'Bread', sx=1.0, sy=0.62, sz=0.6)
    # 아치 손잡이 (양옆 테두리에서)
    tube(arch(-(W / 2 - 0.015), W / 2 - 0.015, H + 0.002, H + 0.2), 0.011, 'WickerDark', seg=10)


# ================= 환풍구 (벽) =================
def build_vent():
    """틀 · 덮개 · 나사를 각각 PARTS 묶음으로 반환 (finish를 셋 따로 부름)."""
    W, H = 0.5, 0.36
    # 틀: 구멍 뚫린 판 (벽에서 2mm 띄움) + 안쪽 어두운 덕트 판
    ring_prism(rrect(0, 0, W, H, 0.02, 3), rrect(0, 0, W - 0.07, H - 0.07, 0.008, 3), -0.002, -0.024, 'VentFrame')
    prism(rrect(0, 0, W - 0.06, H - 0.06, 0.01, 3), -0.003, -0.006, 'VentDark', 'y')
    # 틀 고정 나사 4 (틀 모서리)
    for sx in (1, -1):
        for sz in (1, -1):
            prism(circle(sx * (W / 2 - 0.018), sz * (H / 2 - 0.018), 0.007, 10), -0.023, -0.028, 'Metal', 'y', bevel=0.0015)
    frame = list(PARTS); PARTS.clear()

    # 덮개: 테두리 + 비스듬한 살 7 (틀 앞면 −0.024보다 1mm 앞)
    CW, CH = 0.42, 0.28
    ring_prism(rrect(0, 0, CW, CH, 0.014, 3), rrect(0, 0, CW - 0.04, CH - 0.04, 0.006, 3), -0.026, -0.036, 'VentGrille')
    n = 7
    for i in range(n):
        z = -(CH / 2 - 0.035) + i * (CH - 0.07) / (n - 1)
        # 살 단면 (옆에서 본 y, z): 아래로 기운 평행사변형 — 깊이 −0.027 ~ −0.034
        pts = [(-0.027, z + 0.012), (-0.034, z + 0.004), (-0.034, z - 0.012), (-0.027, z - 0.004)]
        prism([(p[0], p[1]) for p in pts], -(CW / 2 - 0.01), CW / 2 - 0.01, 'VentGrille', 'x')  # 끝은 테두리 속
    # 흔들리는 축 나사 (왼쪽 위)
    PIVOT = Vector((-(CW / 2 - 0.016), 0, CH / 2 - 0.016))
    prism(circle(PIVOT.x, PIVOT.z, 0.008, 10), -0.035, -0.041, 'Metal', 'y', bevel=0.0015)
    cover = list(PARTS); PARTS.clear()

    # 나머지 나사 3 (고친 뒤 보임)
    for sx, sz in ((1, 1), (1, -1), (-1, -1)):
        prism(circle(sx * (CW / 2 - 0.016), sz * (CH / 2 - 0.016), 0.008, 10), -0.035, -0.041, 'Metal', 'y', bevel=0.0015)
    screws = list(PARTS); PARTS.clear()
    return frame, cover, screws, PIVOT


def finish(name, parts=None, origin=None):
    src = parts if parts is not None else PARTS
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = []
    for _, mat in src:
        if mat not in used: used.append(mat)
    for n in used: me.materials.append(MATS[n])
    big = bmesh.new()
    for bm, mat in src:
        vm = {v: big.verts.new(v.co) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = used.index(mat)
        bm.free()
    src.clear()
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
    print('REQ %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d bounds x %.3f~%.3f y %.3f~%.3f z %.3f~%.3f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, b0[0], b1[0], b0[1], b1[1], b0[2], b1[2]))
    return ob


groups = {}
for name, fn in (('ReqToolbox', build_toolbox), ('ReqMedkit', build_medkit), ('ReqSnack', build_snack)):
    fn()
    groups[name] = [finish(name)]
frame, cover, screws, pivot = build_vent()
groups['ReqVent'] = [finish('VentFrame', frame), finish('VentCover', cover, pivot), finish('VentScrews', screws)]

layout = {'ReqToolbox': (-0.75, 0, 0), 'ReqMedkit': (-0.25, 0, 0), 'ReqSnack': (0.25, 0, 0), 'ReqVent': (0.8, 0, 0.3)}
for name, obs in groups.items():
    off = Vector(layout[name])
    for ob in obs:
        ob['base'] = list(ob.location)
        ob.location = ob.location + off
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'request.blend'))

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

# 덮개를 흔들린 자세로 (미리보기 — 왼쪽 위 나사 축으로 9° 기울어짐, 나머지 나사 숨김)
vc = bpy.data.objects['VentCover']; vc.rotation_euler = (0, math.radians(-9), 0)
bpy.data.objects['VentScrews'].hide_render = True

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
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(50), 0, math.radians(-35))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d, scale in (('q', Vector((0.35, -0.8, 0.55)), 2.1), ('front', Vector((0, -1, 0.15)), 2.1)):
    d = d.normalized(); cam.location = Vector((0.02, 0, 0.18)) + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 1000; sc.render.resolution_y = 520
    sc.render.filepath = os.path.join(pv, 'request_%s.png' % tag); bpy.ops.render.render(write_still=True)
# 가까이
for name, (cx, cz), scale in (('ReqToolbox', (-0.75, 0.13), 0.6), ('ReqMedkit', (-0.25, 0.08), 0.5), ('ReqSnack', (0.25, 0.15), 0.55), ('ReqVent', (0.8, 0.3), 0.6)):
    d = Vector((0.4, -0.85, 0.45 if name != 'ReqVent' else 0.1)).normalized()
    cam.location = Vector((cx, 0, cz)) + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 600; sc.render.resolution_y = 450
    sc.render.filepath = os.path.join(pv, 'request_%s.png' % name); bpy.ops.render.render(write_still=True)
