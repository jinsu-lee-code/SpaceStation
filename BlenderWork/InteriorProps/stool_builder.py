# 동물 주민 발판 (사용자 결정 2026-10-20: 작업대에 손이 안 닿던 동물 주민이 발판에 올라서서 일함)
#   AnimalStool15 · 30 · 45 : 높이 0.15 · 0.30 · 0.45m — Unity가 작업 자리 앞 작업대 높이에 맞는 것을 골라 발밑에 둔다
#   알아보는 단서: 노란 도장 상판 + 검은 고무 미끄럼 방지 홈 · 둥근 금속관 다리(옆에서 ㄷ자, 아래로 벌어짐) · 고무 발
#   0.45는 뒤쪽에 아래 디딤판 하나 더 (올라가는 쪽, 앞 = 작업대 쪽은 비움)
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/stool_builder.py -- <fbx 출력 폴더>
# 좌표: 미터. 바닥 = z 0, 앞 = Blender −y (Unity +z, 작업대 쪽). 원점 = 바닥 가운데 (= 동물 발 자리)
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'StoolPaint': ((0.86, 0.6, 0.12), 0.3),    # 노란 도장 (게임 조명이 따뜻해 주황빛 노랑으로 보임)
    'StoolGrip': ((0.06, 0.06, 0.07), 0.1),    # 검은 고무
    'StoolMetal': ((0.58, 0.6, 0.64), 0.55),   # 금속관
}
GRIP = 0.008   # 상판 위 고무 + 홈 높이 → 발이 닿는 높이 = H + GRIP (Unity AnimalModelSet.StoolHeights와 맞춤)
W, D = 0.46, 0.36

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


def prism(pts, a0, a1, mat, bevel=0.0, seg=2):
    """위에서 본 단면 pts(x, y)를 z a0 ~ a1로. 베벨 뒤 재질 · 면 방향을 다시 정한다 (MODELING 2)."""
    bm = bmesh.new()
    lo = [bm.verts.new((u, v, a0)) for u, v in pts]
    hi = [bm.verts.new((u, v, a1)) for u, v in pts]
    n = len(pts)
    bm.faces.new(hi); bm.faces.new(list(reversed(lo)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    orient_outward(bm)
    if bevel > 0:
        cap = [e for e in bm.edges if all(abs(v.co.z - a1) < 1e-7 for v in e.verts) or all(abs(v.co.z - a0) < 1e-7 for v in e.verts)]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=seg, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        orient_outward(bm)
    PARTS.append((bm, mat))
    return bm


def block(cx, cy, z0, w, d, h, mat, r=0.01, bevel=0.0):
    return prism(rrect(cx, cy, w, d, r, 3), z0, z0 + h, mat, bevel)


def tube(points, r, mat, seg=10):
    """점들을 따라 쓴 관 (끝은 덮개). 꺾이는 곳은 bend()로 미리 둥글게."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[min(k + 1, n - 1)] - points[max(k - 1, 0)]).normalized()
        up = Vector((1, 0, 0)) if abs(t.x) < 0.9 else Vector((0, 1, 0))
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


def bend(points, radius=0.03, steps=4):
    """꺾인 점 목록의 안쪽 꼭짓점을 radius 원호로 바꿈 (관 꺾임이 납작해지지 않게)."""
    out = [points[0]]
    for k in range(1, len(points) - 1):
        a, p, b = points[k - 1], points[k], points[k + 1]
        da, db = (a - p), (b - p)
        r = min(radius, da.length * 0.45, db.length * 0.45)
        s, e = p + da.normalized() * r, p + db.normalized() * r
        for i in range(steps + 1):
            t = i / steps
            out.append((1 - t) ** 2 * s + 2 * (1 - t) * t * p + t ** 2 * e)  # 2차 베지어
    out.append(points[-1])
    return out


def build(H):
    top0 = H - 0.035
    block(0, 0, top0, W, D, 0.035, 'StoolPaint', r=0.03, bevel=0.006)                         # 상판
    block(0, 0, H - 0.0005, W - 0.05, D - 0.05, 0.0055, 'StoolGrip', r=0.02)                  # 고무 판 (상판 속 0.5mm부터)
    for i in range(5):                                                                          # 미끄럼 방지 홈 (가로 줄)
        y = -0.12 + i * 0.06
        block(0, y, H + 0.004 + 0.0004 * (i % 2), W - 0.09, 0.022, 0.004 - 0.0004 * (i % 2), 'StoolGrip', r=0.008)
    # 앞 모서리 금속 덧판 (발끝이 닿는 쪽 — 상판보다 살짝 크고 아래로)
    block(0, -(D / 2 - 0.012), top0 - 0.01, W - 0.02, 0.032, 0.03, 'StoolMetal', r=0.012, bevel=0.003)
    # 다리: 옆마다 ㄷ자 금속관 (아래로 벌어짐) — 앞 발 → 위 앞 → 위 뒤 → 뒤 발
    leg_r = 0.016
    for sx in (1, -1):
        x0, x1 = sx * 0.2, sx * 0.17                     # 아래 · 위 가로 자리
        yf0, yf1 = -(D / 2 - 0.01), -(D / 2 - 0.05)       # 앞 다리 아래 · 위
        yb0, yb1 = D / 2 - 0.01, D / 2 - 0.05             # 뒤 다리
        zt = top0 - leg_r + 0.004                          # 상판 밑에 살짝 묻힘
        pts = [Vector((x0, yf0, 0.025)), Vector((x1, yf1, zt)), Vector((x1, yb1, zt)), Vector((x0, yb0, 0.025))]
        tube(bend(pts, 0.035), leg_r, 'StoolMetal', seg=10)
        for y in (yf0, yb0):                                                                    # 고무 발
            block(x0, y, 0.0, 0.05, 0.05, 0.03, 'StoolGrip', r=0.022, bevel=0.004)
    if H >= 0.25:  # 다리 사이 가로대 (높은 발판만 — 흔들리지 않게)
        zc = H * 0.38
        for y in (-(D / 2 - 0.03), D / 2 - 0.03):
            tube([Vector((-0.192, y * 0.94, zc)), Vector((0.192, y * 0.94, zc))], 0.011, 'StoolMetal', seg=8)
    if H >= 0.4:   # 뒤쪽 아래 디딤판 (올라가는 쪽)
        hz = H * 0.5
        block(0, D / 2 + 0.1, hz - 0.03, W - 0.04, 0.2, 0.03, 'StoolPaint', r=0.025, bevel=0.005)
        block(0, D / 2 + 0.1, hz - 0.0005, W - 0.09, 0.15, 0.005, 'StoolGrip', r=0.015)
        for sx in (1, -1):
            # 디딤판 다리 (뒤로 벌어짐) + 몸통 뒤 다리에 잇는 받침
            tube(bend([Vector((sx * 0.19, D / 2 + 0.2, 0.025)), Vector((sx * 0.17, D / 2 + 0.17, hz - 0.03 - 0.011)),
                       Vector((sx * 0.17, D / 2 - 0.035, hz - 0.03 - 0.011))], 0.03), 0.013, 'StoolMetal', seg=8)
            block(sx * 0.19, D / 2 + 0.2, 0.0, 0.045, 0.045, 0.028, 'StoolGrip', r=0.02, bevel=0.004)


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
    # 검사: 열린 · 비다양체 가장자리 · 뒤집힌 덩어리 (MODELING 8-2), 덩어리 부피는 무게중심 기준 double
    bm = bmesh.new(); bm.from_mesh(me)
    open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    zero = sum(1 for f in bm.faces if f.calc_area() < 1e-9)
    seen = set(); flipped = 0; chunks = 0
    for f0 in bm.faces:
        if f0.index in seen: continue
        stack = [f0]; group = []; seen.add(f0.index)
        while stack:
            f = stack.pop(); group.append(f)
            for e in f.edges:
                for g in e.link_faces:
                    if g.index not in seen: seen.add(g.index); stack.append(g)
        cen = Vector((0, 0, 0)); cnt = 0
        for f in group:
            for v in f.verts: cen += v.co; cnt += 1
        cen /= max(1, cnt)
        vol = 0.0
        for f in group:
            vs = [v.co - cen for v in f.verts]
            for i in range(1, len(vs) - 1): vol += float(vs[0].dot(vs[i].cross(vs[i + 1]))) / 6
        chunks += 1
        if vol < 0: flipped += 1
    bm.free()
    b0 = [min(v.co[i] for v in me.vertices) for i in range(3)]; b1 = [max(v.co[i] for v in me.vertices) for i in range(3)]
    print('STOOL %s verts %d tris %d open %d nonmanifold %d zero %d chunks %d flipped %d bounds x %.3f~%.3f y %.3f~%.3f z %.3f~%.3f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, zero, chunks, flipped, b0[0], b1[0], b0[1], b1[1], b0[2], b1[2]))
    return ob


objs = []
for h in (0.15, 0.30, 0.45):
    build(h)
    objs.append(finish('AnimalStool%02d' % round(h * 100)))
for i, ob in enumerate(objs):
    ob.location.x = (i - 1) * 0.75
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'stool.blend'))

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
for tag, d in (('q', Vector((0.5, -0.8, 0.5))), ('back', Vector((-0.6, 0.8, 0.45))), ('side', Vector((1, 0, 0.15)))):
    d = d.normalized(); cam.location = Vector((0, 0, 0.22)) + d * 3; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = 2.4; sc.render.resolution_x = 960; sc.render.resolution_y = 480
    sc.render.filepath = os.path.join(pv, 'stool_%s.png' % tag); bpy.ops.render.render(write_still=True)
