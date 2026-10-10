# 11-17 ② 보급품 줍기 — 보급 상자 (2026-10-11)
#   SupplyCrate  : 자원 상자 — 단단한 화물 상자 · 고무 모서리 · 뚜껑 이음 띠 · 양옆 손잡이 · 잠금쇠 · 앞 라벨판
#                  띠 · 라벨(재질 M_CrateBand, 흰색)은 Unity가 자원 색으로 물들임 (MaterialPropertyBlock)
#   SupplyCase   : 특별 상자 — 둥근 하드 케이스 · 주황 강조 띠 · 윗면 운반 손잡이 · 옆 홈 · 가운데 봉인 원판
#                  봉인(재질 M_CaseSeal, 흰색)은 Unity가 보상 색(연구 포인트 · 무료 수리 · 만족도)으로 물들임 + 발광
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/supply_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 바닥 = z 0, 앞 = Blender −y (Unity +z). 원점 = 바닥 가운데
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'CrateBody': ((0.3, 0.34, 0.33), 0.25),   # 자원 상자 몸통 (회녹)
    'CrateRubber': ((0.06, 0.06, 0.07), 0.12),
    'CrateMetal': ((0.6, 0.62, 0.66), 0.55),
    'CrateBand': ((0.92, 0.92, 0.92), 0.35),  # Unity에서 자원 색으로
    'CrateDark': ((0.1, 0.11, 0.12), 0.3),
    'CaseBody': ((0.11, 0.13, 0.18), 0.4),    # 특별 상자 몸통 (짙은 남색)
    'CaseAccent': ((0.95, 0.58, 0.16), 0.45), # 주황 강조
    'CaseSeal': ((0.92, 0.92, 0.92), 0.6),    # Unity에서 보상 색 + 발광
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


def P(x, d, z):
    return Vector((x, -d, z))


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


# ================= 자원 상자 =================
def build_crate():
    W, D, H = 0.6, 0.42, 0.38
    block(0, 0, 0.012, W, D, H - 0.024, 'CrateBody', r=0.03, bevel=0.008)                     # 몸통
    block(0, 0, H - 0.115, W + 0.008, D + 0.008, 0.022, 'CrateBand', r=0.034, bevel=0.003)    # 뚜껑 이음 띠 (자원 색)
    block(0, 0, 0.0, W - 0.03, D - 0.03, 0.016, 'CrateDark', r=0.02)                           # 바닥 받침
    # 고무 모서리 8개 (위 · 아래)
    for sx in (1, -1):
        for sy in (1, -1):
            for z0 in (0.004, H - 0.072):
                block(sx * (W / 2 - 0.03), sy * (D / 2 - 0.03), z0, 0.075, 0.075, 0.068 + (0.002 if z0 > 0.1 else 0), 'CrateRubber', r=0.03, bevel=0.006)
    # 옆 손잡이 (양옆, 오목한 받침 + 손잡이 봉)
    for sx in (1, -1):
        x = sx * (W / 2 + 0.002)
        prism(rrect(0, H * 0.6, 0.16, 0.06, 0.02, 3), x - sx * 0.012, x + sx * 0.004, 'CrateDark', 'x')
        tube([Vector((x + sx * 0.002, -0.06, H * 0.6 - 0.012)), Vector((x + sx * 0.022, -0.05, H * 0.6 - 0.012)),
              Vector((x + sx * 0.022, 0.05, H * 0.6 - 0.012)), Vector((x + sx * 0.002, 0.06, H * 0.6 - 0.012))], 0.009, 'CrateMetal', seg=8)
    # 앞 잠금쇠 2개
    for x in (-0.17, 0.17):
        prism(rrect(x, H - 0.1, 0.05, 0.07, 0.008, 2), -(D / 2 + 0.012), -(D / 2 - 0.002), 'CrateMetal', 'y', bevel=0.002)
    # 앞 라벨판 (자원 색) + 어두운 글자 자리 줄 2개
    prism(rrect(0, H * 0.42, 0.24, 0.12, 0.012, 3), -(D / 2 + 0.006), -(D / 2 - 0.003), 'CrateBand', 'y', bevel=0.002)
    for i, w in enumerate((0.15, 0.1)):
        prism(rrect(-0.05 + w / 2 - 0.025, H * 0.42 + 0.025 - i * 0.04, w, 0.014, 0.004, 2), -(D / 2 + 0.0085), -(D / 2 + 0.005), 'CrateDark', 'y')
    # 뚜껑 윗면 홈 2줄
    for y in (-0.08, 0.08):
        block(0, y, H - 0.013, W - 0.14, 0.03, 0.006, 'CrateDark', r=0.008)


# ================= 특별 상자 =================
def build_case():
    W, D, H = 0.52, 0.36, 0.3
    block(0, 0, 0.01, W, D, H - 0.02, 'CaseBody', r=0.06, bevel=0.018)                      # 둥근 하드 케이스
    block(0, 0, H * 0.55, W + 0.01, D + 0.01, 0.03, 'CaseAccent', r=0.065, bevel=0.004)    # 주황 이음 띠
    block(0, 0, 0.0, W - 0.04, D - 0.04, 0.014, 'CrateDark', r=0.04)                        # 바닥 받침
    # 옆 세로 홈 (양옆 4줄)
    for sx in (1, -1):
        for i in range(4):
            y = -0.09 + i * 0.06
            prism(rrect(y, H * 0.3, 0.022, 0.12, 0.008, 2), sx * (W / 2 - 0.006), sx * (W / 2 + 0.006 + 0.0005 * i), 'CrateDark', 'x')
    # 모서리 주황 보강 4개 (아래)
    for sx in (1, -1):
        for sy in (1, -1):
            block(sx * (W / 2 - 0.045), sy * (D / 2 - 0.045), 0.005, 0.1, 0.1, 0.09, 'CaseAccent', r=0.045, bevel=0.008)
    # 윗면 운반 손잡이 (받침 2 + 봉)
    for x in (-0.11, 0.11):
        block(x, 0, H - 0.012, 0.04, 0.05, 0.03, 'CrateDark', r=0.015, bevel=0.004)
    tube([Vector((-0.11, 0, H + 0.012)), Vector((-0.1, 0, H + 0.055)), Vector((-0.06, 0, H + 0.07)),
          Vector((0.06, 0, H + 0.07)), Vector((0.1, 0, H + 0.055)), Vector((0.11, 0, H + 0.012))], 0.014, 'CrateRubber', seg=10)
    # 앞 봉인 원판 (보상 색 · 발광) + 금속 테
    prism(circle(0, H * 0.32, 0.07, 28), -(D / 2 + 0.008), -(D / 2 - 0.004), 'CrateMetal', 'y', bevel=0.003)
    prism(circle(0, H * 0.32, 0.052, 28), -(D / 2 + 0.013), -(D / 2 + 0.004), 'CaseSeal', 'y', bevel=0.002)
    # 앞 잠금쇠 2개 (주황)
    for x in (-0.17, 0.17):
        prism(rrect(x, H * 0.55 + 0.015, 0.045, 0.07, 0.01, 2), -(D / 2 + 0.014), -(D / 2 - 0.004), 'CaseAccent', 'y', bevel=0.003)


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
    print('SUP %s verts %d faces %d open %d nonmanifold %d chunks %d flipped %d bounds x %.3f~%.3f y %.3f~%.3f z %.3f~%.3f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, chunks, flipped, b0[0], b1[0], b0[1], b1[1], b0[2], b1[2]))
    return ob


objs = []
for name, fn in (('SupplyCrate', build_crate), ('SupplyCase', build_case)):
    fn()
    objs.append(finish(name))
objs[0].location.x = -0.45; objs[1].location.x = 0.45
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'supply.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for ob in objs:
        loc = ob.location.copy(); ob.location = (0, 0, 0)
        for x in bpy.context.view_layer.objects: x.select_set(x is ob)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_%s.fbx' % ob.name), use_selection=True, object_types={'MESH'},
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                                 mesh_smooth_type='FACE')
        ob.location = loc

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
for tag, d in (('q', Vector((0.5, -0.8, 0.5))), ('back', Vector((-0.6, 0.7, 0.4)))):
    d = d.normalized(); cam.location = Vector((0, 0, 0.18)) + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = 1.7; sc.render.resolution_x = 800; sc.render.resolution_y = 480
    sc.render.filepath = os.path.join(pv, 'supply_%s.png' % tag); bpy.ops.render.render(write_still=True)
