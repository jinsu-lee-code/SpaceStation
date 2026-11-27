# CONNECT_1121 A6 단계별 상황 소품 (2026-11-27) — 바닥에 세우는 소품 (기본 사고 소품 = incident_builder.py, 벽에 붙음)
#   IncDecoBox : 2단계 ② 장식 조명 분전함 — 둥근 받침 · 크림색 몸 · 빨간 지붕 · 청록 문(번개 표지 · 손잡이 · 손잡이 받침) ·
#                꼭대기 램프 목 · 뒤로 바닥 속까지 들어가는 전선관 (실물 단서 = 번개 표지 · 손잡이 · 램프 · 전선관)
#                / DecoLed (M_IncLed — Unity가 고장 = 빨강 깜빡임 · 고침 = 초록 켜짐으로 물들임, 밝기 차이로도 읽힘)
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/stage_incident_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 바닥 = z 0, 앞 = −y (Unity +z — LookRotation(플레이어 쪽)으로 세움). 원점 = 바닥 가운데
# 체크리스트: 조각끼리 한쪽을 5 mm 겹침 (맞대기만 금지) · 떠 있는 조각 없음 · 작은 구멍 무늬 없음 · 연출(불꽃)은 Unity InteriorFx.Spark
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'Pipe': ((0.5, 0.54, 0.56), 0.5),
    'Metal': ((0.66, 0.68, 0.72), 0.6),
    'Housing': ((0.2, 0.22, 0.25), 0.35),
    'Cream': ((0.93, 0.88, 0.76), 0.3),
    'Red': ((0.78, 0.14, 0.12), 0.45),
    'Teal': ((0.08, 0.5, 0.55), 0.4),
    'Hazard': ((0.95, 0.72, 0.08), 0.35),
    'Led': ((0.92, 0.92, 0.92), 0.5),      # Unity: 빨강 · 초록 + 발광
    'Wicker': ((0.72, 0.52, 0.30), 0.3),
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
    if axis == 'y': return (u, w, v)      # 앞에서 본 단면 (x, z), 깊이 w (음수 = 앞)
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


def tube(points, r, mat, seg=12, up=None):
    bm = bmesh.new()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[min(k + 1, n - 1)] - points[max(k - 1, 0)]).normalized()
        ref = up if up is not None else (Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0)))   # 평면 곡선은 고정 (뒤틀림 방지)
        u = t.cross(ref).normalized(); v = t.cross(u).normalized()
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


# ================= 2단계 ② 장식 조명 분전함 =================
BODY_W, BODY_D = 0.44, 0.30
FRONT = -BODY_D / 2          # 몸 앞면 y
LAMP_Z = 1.085               # 램프 가운데 높이


def build_decobox():
    # 받침 (몸 밑 5 mm 겹침) · 몸 · 지붕 두 단
    prism(rrect(0, 0, 0.50, 0.36, 0.05), 0.0, 0.06, 'Housing', bevel=0.008)
    prism(rrect(0, 0, BODY_W, BODY_D, 0.06), 0.055, 0.90, 'Cream', bevel=0.01)
    prism(rrect(0, 0, 0.50, 0.36, 0.07), 0.895, 0.95, 'Red', bevel=0.01)
    prism(rrect(0, 0, 0.36, 0.24, 0.05), 0.945, 0.99, 'Red', bevel=0.008)
    # 문 (앞면에 2 mm 묻고 12 mm 나옴) · 번개 표지 (문에 1 mm 묻음) · 손잡이 받침 둘 · 손잡이
    prism(rrect(0, 0.50, 0.32, 0.56, 0.03), FRONT + 0.002, FRONT - 0.012, 'Teal', axis='y', bevel=0.004)
    bolt = [(0.018, 0.665), (-0.040, 0.565), (-0.004, 0.565), (-0.026, 0.475), (0.040, 0.590), (0.004, 0.590)]
    prism(bolt, FRONT - 0.011, FRONT - 0.016, 'Hazard', axis='y')
    for z in (0.41, 0.51):
        prism([(0.12 + p[0], z + p[1]) for p in circle(0, 0, 0.009, 12)], FRONT - 0.010, FRONT - 0.030, 'Metal', axis='y')
    tube([Vector((0.12, FRONT - 0.034, z)) for z in (0.395, 0.46, 0.525)], 0.011, 'Metal')
    # 꼭대기 램프 목 (지붕 속 5 mm부터) — 램프 알은 따로 (DecoLed)
    prism(circle(0, 0, 0.028, 16), 0.985, LAMP_Z - 0.035, 'Metal')
    # 뒤 전선관: 몸 속에서 나와 뒤로 꺾여 바닥 속까지
    back = BODY_D / 2
    tube([Vector((0.12, back - 0.02, 0.22)), Vector((0.12, back + 0.07, 0.22)), Vector((0.12, back + 0.11, 0.17)),
          Vector((0.12, back + 0.12, 0.06)), Vector((0.12, back + 0.12, -0.03))], 0.018, 'Pipe')
    body = take()
    dome((0, 0, LAMP_Z), 0.052, 'Led', sz=1.15, u=20, v=10)   # 목 위 7 mm 겹침
    led = take()
    return [('IncDecoBox', body, Vector((0, 0, 0))), ('DecoLed', led, Vector((0, 0, LAMP_Z)))]


# ================= 2단계 ① 놀이기구 운행 조작반 =================
# 크림 몸 · 빨간 지붕 · 청록 판에 레버 6개 (Switch0~5, 원점 = 축) · 경고등 6개 (Led0~5) — 차단기와 같은 이름 (코드가 같은 규칙으로 씀)
# 레버는 앞(−y)으로 나온 막대 + 노란 손잡이 (경고등 빨강 · 초록과 헷갈리지 않게), Unity가 X축으로 ±28° 돌려 내림(경고) · 올림(정상)
CONSOLE_FRONT = -0.18       # 몸 앞면 y
BOARD_FRONT = -0.06         # 판 앞면 y
LEVER_Z = 1.02
LED_Z = 1.21


def lever_x(i):
    return -0.325 + 0.13 * i


def build_rideconsole():
    out = []
    prism(rrect(0, 0, 0.98, 0.46, 0.06), 0.0, 0.06, 'Housing', bevel=0.008)
    prism(rrect(0, 0, 0.88, 0.36, 0.06), 0.055, 0.86, 'Cream', bevel=0.01)
    prism(rrect(0, 0, 0.94, 0.42, 0.06), 0.855, 0.90, 'Red', bevel=0.01)
    # 아래 띠 (몸 앞면에 2 mm 묻음)
    prism(rrect(0, 0.15, 0.80, 0.05, 0.02), CONSOLE_FRONT + 0.002, CONSOLE_FRONT - 0.008, 'Hazard', axis='y', bevel=0.003)
    # 레버 판 (윗띠 위 1 cm 겹침) · 레버 홈 (판에 5 mm 묻음)
    prism(rrect(0, 1.10, 0.86, 0.42, 0.04), 0.10, BOARD_FRONT, 'Teal', axis='y', bevel=0.006)
    for i in range(6):
        prism(rrect(lever_x(i), LEVER_Z, 0.05, 0.15, 0.02), BOARD_FRONT + 0.005, BOARD_FRONT - 0.008, 'Housing', axis='y', bevel=0.002)
    # 지붕 (판 위 5 mm 겹침, 앞으로 조금 나옴)
    prism(rrect(0, 0.02, 1.00, 0.38, 0.07), 1.305, 1.36, 'Red', bevel=0.012)
    out.append(('IncRideConsole', take(), Vector((0, 0, 0))))
    for i in range(6):
        x = lever_x(i)
        tube([Vector((x, BOARD_FRONT + 0.004, LEVER_Z)), Vector((x, BOARD_FRONT - 0.10, LEVER_Z))], 0.011, 'Metal')
        dome((x, BOARD_FRONT - 0.115, LEVER_Z), 0.024, 'Hazard', u=16, v=8)
        out.append(('Switch%d' % i, take(), Vector((x, BOARD_FRONT - 0.004, LEVER_Z))))
    for i in range(6):
        dome((lever_x(i), BOARD_FRONT - 0.006, LED_Z), 0.022, 'Led', u=16, v=8)   # 반쯤 판에 묻음
        out.append(('Led%d' % i, take(), Vector((lever_x(i), BOARD_FRONT - 0.006, LED_Z))))
    return out


# ================= 2단계 ③ 놀이 소품 흩어짐 =================
# ToyBasket (등나무색 둥근 바구니 · 빨간 띠 · 크림 손잡이) + ToyBall (빨간 공 · 크림 띠) · ToyStar (노란 별 쿠션) · ToyRing (청록 고리)
# 원점 = 바닥에 닿는 가운데 (Unity가 바닥 자리에 바로 놓음). 바구니 속은 비어 있음 (소품이 들어감) — 구멍 무늬 없음 (짠 결은 띠 두 줄로만)
BASKET_R, BASKET_IN, BASKET_H = 0.30, 0.27, 0.30


def annulus(cx, cy, r_out, r_in, z0, z1, mat, seg=32):
    """속이 빈 원통 벽 (닫힌 고체: 바깥 · 안 · 위 · 아래 고리)."""
    bm = bmesh.new()
    rings = []
    for r, z in ((r_out, z0), (r_out, z1), (r_in, z1), (r_in, z0)):
        rings.append([bm.verts.new((cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg), z)) for i in range(seg)])
    for k in range(4):
        a, b = rings[k], rings[(k + 1) % 4]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def torus(c, R, r, mat, seg=28, sides=10, axis='z'):
    bm = bmesh.new()
    rings = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        ring = []
        for k in range(sides):
            b = 2 * math.pi * k / sides
            d = R + r * math.cos(b)
            if axis == 'z':
                p = (c[0] + d * math.cos(a), c[1] + d * math.sin(a), c[2] + r * math.sin(b))
            else:   # 세로 고리 (x-z 평면)
                p = (c[0] + d * math.cos(a), c[1] + r * math.sin(b), c[2] + d * math.sin(a))
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(seg):
        a, b = rings[i], rings[(i + 1) % seg]
        for k in range(sides):
            l = (k + 1) % sides
            bm.faces.new((a[k], a[l], b[l], b[k]))
    orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def star(cx, cy, r_out, r_in, n=5):
    return [(cx + (r_out if i % 2 == 0 else r_in) * math.cos(math.pi / 2 + math.pi * i / n),
             cy + (r_out if i % 2 == 0 else r_in) * math.sin(math.pi / 2 + math.pi * i / n)) for i in range(2 * n)]


def build_toys():
    out = []
    # 바구니: 바닥판 (벽 속 5 mm) · 벽 · 띠 두 줄 (벽 밖에 3 mm 묻음) · 테두리 고리 · 손잡이 (테두리 속까지)
    prism(circle(0, 0, BASKET_IN + 0.005, 32), 0.0, 0.03, 'Wicker')
    annulus(0, 0, BASKET_R, BASKET_IN, 0.0, BASKET_H, 'Wicker')
    for z in (0.08, 0.20):
        annulus(0, 0, BASKET_R + 0.012, BASKET_R - 0.003, z, z + 0.035, 'Red')
    torus((0, 0, BASKET_H), (BASKET_R + BASKET_IN) / 2, 0.022, 'Wicker', seg=36, sides=10)
    arc = [Vector((math.cos(math.pi * t / 12) * (BASKET_R - 0.015), 0, BASKET_H - 0.01 + math.sin(math.pi * t / 12) * 0.26)) for t in range(13)]
    tube(arc, 0.016, 'Cream', up=Vector((0, 1, 0)))
    out.append(('ToyBasket', take(), Vector((0, 0, 0))))
    # 공 (지름 26 cm) · 띠 (공에 반쯤 묻음)
    dome((0, 0, 0.13), 0.13, 'Red', u=24, v=12)
    torus((0, 0, 0.13), 0.128, 0.012, 'Cream', seg=32, sides=8)
    out.append(('ToyBall', take(), Vector((0, 0, 0))))
    # 별 쿠션
    prism(star(0, 0, 0.17, 0.08), 0.0, 0.08, 'Hazard', bevel=0.02, seg=3)
    out.append(('ToyStar', take(), Vector((0, 0, 0))))
    # 고리 (바닥에 누움)
    torus((0, 0, 0.03), 0.11, 0.03, 'Teal', seg=32, sides=12)
    out.append(('ToyRing', take(), Vector((0, 0, 0))))
    return out


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
for name, fn in (('IncDecoBox', build_decobox), ('IncRideConsole', build_rideconsole), ('IncToys', build_toys)):
    groups[name] = [finish(n, parts, origin) for n, parts, origin in fn()]
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'stage_incident.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for name, obs in groups.items():
        for x in bpy.context.view_layer.objects: x.select_set(x in obs)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_%s.fbx' % name), use_selection=True, object_types={'MESH'},
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                                 mesh_smooth_type='FACE')

# 미리보기: 바닥판 위, 램프 빨강 (고장 모습)
fl = bpy.data.meshes.new('Floor'); fo = bpy.data.objects.new('Floor', fl); bpy.context.scene.collection.objects.link(fo)
bmf = bmesh.new(); bmesh.ops.create_cube(bmf, size=1.0); bmf.to_mesh(fl); bmf.free()
fo.scale = (2.0, 2.0, 0.02); fo.location = (0, 0, -0.011)
fm = bpy.data.materials.new('FloorPrev'); fm.use_nodes = True
next(x for x in fm.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (0.45, 0.47, 0.42, 1); fl.materials.append(fm)
b = next(x for x in MATS['Led'].node_tree.nodes if x.type == 'BSDF_PRINCIPLED'); b.inputs['Base Color'].default_value = (0.9, 0.1, 0.08, 1)
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
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(55), 0, math.radians(-30))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for gname, prefix, center, scale in (('IncDecoBox', 'stage_decobox', 0.55, 1.45), ('IncRideConsole', 'stage_console', 0.7, 1.7), ('IncToys', 'stage_toys', 0.25, 1.6)):
    for other, obs in groups.items():
        for x in obs:
            x.hide_render = other != gname
    if gname == 'IncToys':   # 미리보기만: 바구니 옆에 소품을 늘어놓음 (내보낸 뒤라 FBX와 무관)
        for x in groups[gname]:
            x.location = {'ToyBall': Vector((0.55, -0.1, 0)), 'ToyStar': Vector((-0.6, -0.1, 0)), 'ToyRing': Vector((0.05, -0.55, 0))}.get(x.name, x.location)
    for tag, d in (('front', Vector((0.0, -1.0, 0.25))), ('q', Vector((0.6, -0.8, 0.45))), ('back', Vector((-0.5, 0.85, 0.3)))):
        d = d.normalized(); cam.location = Vector((0, 0, center)) + d * 4; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        cd.ortho_scale = scale; sc.render.resolution_x = 600; sc.render.resolution_y = 700
        sc.render.filepath = os.path.join(pv, '%s_%s.png' % (prefix, tag)); bpy.ops.render.render(write_still=True)
