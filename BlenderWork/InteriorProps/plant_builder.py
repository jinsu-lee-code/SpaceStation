# 11-17 ③ 주민 요청 — 식물 다시 만들기 (2026-10-11)
# 예전 식물 = 찌그러뜨린 구(잎 덩어리) 몇 개 → 단순해 보였음 (사용자 피드백). 잎 하나하나를 모양 있게:
#   잎 = 가운데 잎맥이 솟은 마름모 단면을 잎 길이 따라 쓸어 만든 닫힌 얇은 판 (폭은 밑 · 끝이 좁고 가운데가 넓음, 길이 따라 아래로 휨, 가장자리가 살짝 내려가 오목)
#   SM_PlantPot / SM_PlantPotWilted : 화분 식물 (회전 링 · 휴게실 화분 흙 위) — 잎자루 + 큰 잎 14장 나선, 시든 것은 잎이 축 처지고 누렇게 + 흙 위 떨어진 잎
#   SM_PlantTree / SM_PlantTreeWilted : 수경 농장 돔 아래 나무 — 휘어진 줄기 · 가지 4 · 가지 끝과 줄기 꼭대기 잎 뭉치(작은 잎 여러 장), 흙 위 작은 덤불
#   SM_PlantPine / SM_PlantPineWilted : 3단계 바깥 정원 소나무 (2026-11-10) — 곧은 줄기 · 층층 가지 여섯 단 · 가지마다 바늘잎 (가는 잎) 묶음 · 꼭대기 잎
#   SM_PlantBlossom / SM_PlantBlossomWilted : 3단계 꽃나무 (2026-11-10) — 넓게 퍼진 가지 · 가지 끝 꽃 뭉치 (다섯 꽃잎 꽃 + 작은 잎)
# 원점 = 흙 윗면 가운데, 위 = +z (Unity +y). 상추(수경 선반)는 farm_builder 안에서 같은 잎 함수로 만든다.
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/plant_builder.py -- <fbx 출력 폴더>
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

COLORS = {
    'Leaf': ((0.16, 0.42, 0.14), 0.3),
    'LeafLight': ((0.32, 0.6, 0.2), 0.3),
    'LeafDark': ((0.08, 0.28, 0.1), 0.3),
    'Stem': ((0.3, 0.42, 0.16), 0.2),
    'Bark': ((0.3, 0.22, 0.15), 0.1),
    'Dry': ((0.55, 0.48, 0.18), 0.15),      # 시든 잎 (누런 갈색)
    'DryDark': ((0.4, 0.3, 0.13), 0.12),
    'Needle': ((0.1, 0.32, 0.18), 0.3),       # 소나무 바늘잎
    'NeedleLight': ((0.2, 0.45, 0.24), 0.3),
    'Blossom': ((0.97, 0.66, 0.77), 0.25),    # 꽃나무 꽃잎
    'BlossomLight': ((1.0, 0.86, 0.9), 0.25),
    'Pistil': ((1.0, 0.84, 0.36), 0.3),       # 꽃 가운데
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_Plant' + n); m.use_nodes = True
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


def leaf(base, yaw, pitch, length, width, droop, mat, segs=7, cup=0.25, thick=0.006, twist=0.0):
    """잎 한 장. base = 잎 밑(Blender 좌표), yaw = 수평 방향(라디안), pitch = 처음 올라가는 각(라디안, 0 = 수평),
    droop = 끝까지 아래로 더 꺾이는 각(라디안). 단면 = 왼 가장자리 · 잎맥 위 · 오른 가장자리 · 잎맥 아래 (마름모) → 닫힌 판."""
    bm = bmesh.new()
    fwd0 = Vector((math.cos(yaw), math.sin(yaw), 0))
    side = Vector((-math.sin(yaw), math.cos(yaw), 0))
    p = Vector(base)
    rows = []
    step = length / segs
    for i in range(segs + 1):
        u = i / segs
        a = pitch - droop * u * u
        d = fwd0 * math.cos(a) + Vector((0, 0, math.sin(a)))
        up = d.cross(side).normalized() * -1 if d.cross(side).length > 1e-6 else Vector((0, 0, 1))
        if up.z < 0: up = -up
        w = width * (math.sin(math.pi * min(u * 1.05, 1.0)) ** 0.75) * (0.35 if u < 0.08 else 1.0)
        rs = side * math.cos(twist * u) + up * math.sin(twist * u)
        ru = up * math.cos(twist * u) - side * math.sin(twist * u)
        L = p - rs * w * 0.5 - ru * cup * w * 0.5
        R = p + rs * w * 0.5 - ru * cup * w * 0.5
        T = p + ru * thick * (1.0 - 0.6 * u)
        B = p - ru * thick * 0.6 * (1.0 - 0.6 * u)
        rows.append(p.copy() if i == segs else None)
        rows[-1] = [bm.verts.new(L), bm.verts.new(T), bm.verts.new(R), bm.verts.new(B)] if i < segs else bm.verts.new(p)
        if i < segs:
            p = p + d * step
    base_v = bm.verts.new(Vector(base) - fwd0 * 0.002)
    first = rows[0]
    for k in range(4):
        bm.faces.new((base_v, first[(k + 1) % 4], first[k]))
    for i in range(segs - 1):
        a, b = rows[i], rows[i + 1]
        for k in range(4):
            bm.faces.new((a[k], a[(k + 1) % 4], b[(k + 1) % 4], b[k]))
    tip = rows[segs]
    last = rows[segs - 1]
    for k in range(4):
        bm.faces.new((last[k], last[(k + 1) % 4], tip))
    orient_outward(bm)
    PARTS.append((bm, mat))
    return p  # 끝 위치


def tube(points, r0, r1, mat, seg=8):
    """폴리라인을 따라 굵기 r0 → r1로 가늘어지는 관 (양 끝 막음)."""
    bm = bmesh.new()
    rings = []
    n = len(points)
    for k, p in enumerate(points):
        t = (points[min(k + 1, n - 1)] - points[max(k - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        u = t.cross(up).normalized(); v = t.cross(u).normalized()
        r = r0 + (r1 - r0) * k / max(1, n - 1)
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * i / seg) + v * math.sin(2 * math.pi * i / seg)) * r) for i in range(seg)])
    for k in range(n - 1):
        a, b = rings[k], rings[k + 1]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    orient_outward(bm)
    PARTS.append((bm, mat))


def curve(p0, d0, length, bend, steps=6):
    """p0에서 d0 방향으로 출발해 위로(bend > 0) / 아래로 휘는 곡선 점들."""
    pts = [Vector(p0)]
    d = Vector(d0).normalized()
    step = length / steps
    for i in range(steps):
        d = (d + Vector((0, 0, bend / steps))).normalized()
        pts.append(pts[-1] + d * step)
    return pts


# ================= 화분 식물 =================
def build_pot(wilted, seed=3):
    rnd = random.Random(seed)
    n = 18
    for i in range(n):
        yaw = i * 2.39996 + rnd.uniform(-0.15, 0.15)       # 황금각 나선
        inner = i / n                                        # 0 = 바깥 늙은 잎, 1 = 안쪽 어린 잎
        stem_len = 0.28 + 0.4 * inner + rnd.uniform(-0.04, 0.04)
        pitch_stem = math.radians(62 + 22 * inner)
        if wilted:
            pitch_stem -= math.radians(28)
        d0 = Vector((math.cos(yaw) * math.cos(pitch_stem), math.sin(yaw) * math.cos(pitch_stem), math.sin(pitch_stem)))
        pts = curve(Vector((rnd.uniform(-0.03, 0.03), rnd.uniform(-0.03, 0.03), -0.02)), d0, stem_len, -0.25 if wilted else 0.15, 5)
        tube(pts, 0.009, 0.006, 'DryDark' if wilted and i % 3 == 0 else 'Stem', seg=6)
        end = pts[-1]
        L = 0.42 + 0.18 * (1 - inner) + rnd.uniform(-0.04, 0.04)
        W = L * rnd.uniform(0.42, 0.5)
        pitch = math.radians(38 + 28 * inner) - (math.radians(45) if wilted else 0)
        droop = math.radians(62 + rnd.uniform(-10, 10)) + (math.radians(50) if wilted else 0)
        if wilted:
            mat = 'Dry' if i % 2 == 0 else 'DryDark' if i % 3 == 0 else 'Leaf'
        else:
            mat = 'LeafLight' if inner > 0.7 else 'Leaf' if i % 3 else 'LeafDark'
        leaf(end, yaw + rnd.uniform(-0.1, 0.1), pitch, L, W, droop, mat, cup=0.3, twist=rnd.uniform(-0.3, 0.3))
    if wilted:  # 흙 위에 떨어진 잎 3장
        for k in range(3):
            a = k * 2.1 + 0.4
            p = Vector((math.cos(a) * 0.2, math.sin(a) * 0.2, 0.004 + 0.002 * k))
            leaf(p, a + 1.3, math.radians(2), 0.2, 0.08, 0.0, 'DryDark' if k % 2 else 'Dry', cup=0.05, thick=0.003)


# ================= 나무 =================
def cluster(center, size, n, rnd, wilted, base_mat='Leaf'):
    """가지 끝 잎 뭉치: 가운데에서 사방으로 작은 잎 n장."""
    for k in range(n):
        yaw = rnd.uniform(0, 2 * math.pi)
        pitch = rnd.uniform(-0.3, 0.9) - (0.6 if wilted else 0)
        L = size * rnd.uniform(0.7, 1.0)
        off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.6, 0.6))) * size * 0.9
        if wilted:
            mat = 'Dry' if k % 2 == 0 else 'DryDark' if k % 3 == 0 else 'Leaf'
        else:
            mat = ('LeafLight', 'Leaf', 'LeafDark')[k % 3]
        leaf(center + off, yaw, pitch, L, L * 0.42, 0.6 + (0.8 if wilted else 0), mat, segs=5, cup=0.25, thick=0.005, twist=rnd.uniform(-0.4, 0.4))


def build_tree(wilted, seed=9):
    rnd = random.Random(seed)
    trunk = curve(Vector((0, 0, -0.03)), Vector((0.06, 0.03, 1)), 1.45, 0.0, 8)
    # 줄기를 살짝 S자로
    for i, p in enumerate(trunk):
        p.x += 0.05 * math.sin(i * 0.9); p.y += 0.03 * math.cos(i * 0.7)
    tube(trunk, 0.075, 0.035, 'Bark', seg=10)
    top = trunk[-1]
    clusters = [top + Vector((0, 0, 0.12))]
    for b in range(6):
        yaw = b * math.pi / 3 + 0.4 + rnd.uniform(-0.2, 0.2)
        start = trunk[4 + b % 4]
        d0 = Vector((math.cos(yaw), math.sin(yaw), 0.9 if not wilted else 0.4))
        pts = curve(start, d0, 0.55 + rnd.uniform(-0.05, 0.1), 0.35 if not wilted else -0.2, 5)
        tube(pts, 0.035, 0.014, 'Bark', seg=7)
        clusters.append(pts[-1])
        # 가지 중간 작은 잔가지 + 뭉치
        mid = pts[3]
        side = Vector((math.cos(yaw + 1.2), math.sin(yaw + 1.2), 0.5))
        twig = curve(mid, side, 0.25, 0.2, 3)
        tube(twig, 0.014, 0.008, 'Bark', seg=6)
        clusters.append(twig[-1])
    for i, c in enumerate(clusters):
        cluster(c, 0.28 if i == 0 else 0.24, 26 if i == 0 else (9 if wilted else 18), rnd, wilted)
    # 흙 위 작은 덤불 6 (예전 화단 덤불 자리, 반지름 0.58)
    for k in range(6):
        a = k * math.pi / 3 + 0.2
        c = Vector((math.cos(a) * 0.58, math.sin(a) * 0.58, 0.0))
        for j in range(5):
            yaw = a + j * 1.25
            mat = ('Dry' if j % 2 else 'DryDark') if wilted and k % 2 == 0 else ('LeafLight' if j % 2 else 'Leaf')
            leaf(c, yaw, math.radians(35 + 10 * (j % 2)) - (0.4 if wilted else 0), 0.17, 0.08, 0.9, mat, segs=5, cup=0.3, thick=0.004)


# ================= 3단계 소나무 · 꽃나무 (2026-11-10) =================
def build_pine(wilted, seed=21):
    rnd = random.Random(seed)
    trunk = curve(Vector((0, 0, -0.03)), Vector((0.02, 0.01, 1)), 2.35, 0.0, 8)
    tube(trunk, 0.085, 0.022, 'Bark', seg=10)
    needle = ('Dry', 'DryDark') if wilted else ('Needle', 'NeedleLight')
    levels = 7
    for k in range(levels):
        z = 0.45 + k * 0.27
        Lb = 1.0 * (1 - k / 7.6) + 0.1
        for b in range(6):
            yaw = b * 2 * math.pi / 6 + k * 0.5 + rnd.uniform(-0.15, 0.15)
            start = Vector((0.02 * z, 0.01 * z, z))
            d0 = Vector((math.cos(yaw), math.sin(yaw), -0.1 - (0.25 if wilted else 0)))
            pts = curve(start, d0, Lb, 0.2 if not wilted else -0.15, 4)
            tube(pts, 0.022 - 0.002 * k, 0.007, 'Bark', seg=6)
            for j in range(5):                                  # 가지를 따라 넓은 바늘잎 판 (양옆으로 겹쳐 가지 덮음)
                t = 0.15 + 0.85 * j / 4
                i0 = min(len(pts) - 2, int(t * (len(pts) - 1)))
                base = pts[i0].lerp(pts[i0 + 1], t * (len(pts) - 1) - i0)
                L = (0.3 - 0.1 * t) * (1.0 - 0.04 * k)
                for e in (-1, 1):
                    leaf(base, yaw + e * 0.75 + rnd.uniform(-0.1, 0.1), 0.18 - (0.45 if wilted else 0), L, 0.085, 0.42 + (0.5 if wilted else 0),
                         needle[(j + (e > 0)) % 2], segs=3, cup=0.2, thick=0.004)
    top = trunk[-1]
    for j in range(8):                                          # 꼭대기 잎
        leaf(top - Vector((0, 0, 0.05)), j * 2 * math.pi / 8, math.radians(58) - (0.6 if wilted else 0), 0.24, 0.06, 0.25, needle[j % 2], segs=3, cup=0.2, thick=0.004)


def flower(c, n, R, rot, mat):
    """다섯 꽃잎 꽃 한 송이 = 얇은 별꽃 판 (꽃잎 끝 R · 사이 0.5R, 가운데가 살짝 오목), n = 꽃이 보는 쪽."""
    n = Vector(n).normalized()
    u = n.cross(Vector((0, 0, 1)) if abs(n.z) < 0.9 else Vector((1, 0, 0))).normalized(); v = n.cross(u)
    bm = bmesh.new()
    top, bot = [], []
    for i in range(10):
        a = rot + math.pi * i / 5
        r = R if i % 2 == 0 else R * 0.5
        q = c + (u * math.cos(a) + v * math.sin(a)) * r + n * (0.012 * (r / R))
        top.append(bm.verts.new(q + n * 0.003)); bot.append(bm.verts.new(q - n * 0.003))
    ct = bm.verts.new(c + n * 0.006); cb = bm.verts.new(c - n * 0.002)
    for i in range(10):
        j = (i + 1) % 10
        bm.faces.new((ct, top[i], top[j])); bm.faces.new((cb, bot[j], bot[i]))
        bm.faces.new((top[i], bot[i], bot[j], top[j]))
    orient_outward(bm)
    PARTS.append((bm, mat))


def blossom_cluster(center, size, nf, nl, rnd, wilted):
    """꽃 뭉치: 다섯 꽃잎 꽃 nf송이 (밖을 봄, 연분홍 · 분홍 · 꽃술색 가운데 없이 판 둘 겹침) + 작은 잎 nl장."""
    for f in range(nf if not wilted else nf // 3):
        off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.45, 0.75)))
        c = center + off * size * 0.9
        nrm = (off + Vector((0, 0, 0.6))).normalized()
        R = rnd.uniform(0.055, 0.075)
        mat = ('Dry' if f % 2 else 'DryDark') if wilted else ('BlossomLight' if f % 3 == 0 else 'Blossom')
        flower(c, nrm, R, rnd.uniform(0, 2 * math.pi), mat)
        if not wilted and f % 2 == 0:
            flower(c + nrm * 0.008, nrm, R * 0.3, rnd.uniform(0, 2 * math.pi), 'Pistil')
    for k in range(nl):
        yaw = rnd.uniform(0, 2 * math.pi)
        off = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-0.7, 0.3))) * size
        leaf(center + off, yaw, rnd.uniform(-0.2, 0.6), size * 0.75, size * 0.32, 0.6 + (0.8 if wilted else 0),
             ('Dry' if wilted else ('LeafLight', 'Leaf')[k % 2]), segs=4, cup=0.25, thick=0.004)


def build_blossom(wilted, seed=33):
    rnd = random.Random(seed)
    trunk = curve(Vector((0, 0, -0.03)), Vector((0.08, 0.04, 1)), 1.25, 0.0, 7)
    for i, p in enumerate(trunk):
        p.x += 0.06 * math.sin(i * 0.8); p.y += 0.04 * math.cos(i * 0.6)
    tube(trunk, 0.085, 0.04, 'Bark', seg=10)
    clusters = [trunk[-1] + Vector((0, 0, 0.1))]
    for b in range(7):
        yaw = b * 2 * math.pi / 7 + 0.3 + rnd.uniform(-0.2, 0.2)
        start = trunk[3 + b % 4]
        d0 = Vector((math.cos(yaw), math.sin(yaw), 0.55 if not wilted else 0.25))
        pts = curve(start, d0, 0.7 + rnd.uniform(-0.05, 0.12), 0.3 if not wilted else -0.2, 5)
        tube(pts, 0.035, 0.012, 'Bark', seg=7)
        clusters.append(pts[-1])
        mid = pts[3]
        side = Vector((math.cos(yaw - 1.1), math.sin(yaw - 1.1), 0.45))
        twig = curve(mid, side, 0.28, 0.2, 3)
        tube(twig, 0.014, 0.007, 'Bark', seg=6)
        clusters.append(twig[-1])
    for i, c in enumerate(clusters):
        blossom_cluster(c, 0.3 if i == 0 else 0.26, 22 if i == 0 else 16, 4, rnd, wilted)


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
    bm = bmesh.new(); bm.from_mesh(me)
    open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    bm.free()
    b0 = [min(v.co[i] for v in me.vertices) for i in range(3)]; b1 = [max(v.co[i] for v in me.vertices) for i in range(3)]
    print('PLANT %s verts %d tris %d open %d nonmanifold %d bounds x %.2f~%.2f y %.2f~%.2f z %.2f~%.2f'
          % (name, len(me.vertices), len(me.polygons), open_, nonman, b0[0], b1[0], b0[1], b1[1], b0[2], b1[2]))
    return ob


objs = []
for name, fn in (('PlantPot', lambda: build_pot(False)), ('PlantPotWilted', lambda: build_pot(True)),
                 ('PlantTree', lambda: build_tree(False)), ('PlantTreeWilted', lambda: build_tree(True)),
                 ('PlantPine', lambda: build_pine(False)), ('PlantPineWilted', lambda: build_pine(True)),
                 ('PlantBlossom', lambda: build_blossom(False)), ('PlantBlossomWilted', lambda: build_blossom(True))):
    fn()
    objs.append(finish(name))
for i, ob in enumerate(objs):
    ob.location.x = i * 1.8 - 2.7
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'plants.blend'))

if OUT:
    os.makedirs(OUT, exist_ok=True)
    for ob in objs:
        loc = ob.location.copy(); ob.location = (0, 0, 0)
        for x in bpy.context.view_layer.objects: x.select_set(x is ob)
        bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, 'SM_%s.fbx' % ob.name), use_selection=True, object_types={'MESH'},
                                 axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL',
                                 mesh_smooth_type='FACE')
        ob.location = loc

# 미리보기: 화분(원기둥) 위에
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng; break
    except TypeError:
        pass
pot_mat = bpy.data.materials.new('PotPreview'); pot_mat.use_nodes = True
next(x for x in pot_mat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (0.2, 0.22, 0.25, 1)
for i in range(4):
    bm = bmesh.new()
    r = 0.38 if i < 2 else 0.9
    bmesh.ops.create_cone(bm, cap_ends=True, segments=24, radius1=r * 0.88, radius2=r, depth=0.5, matrix=Matrix.Translation((i * 1.8 - 2.7, 0, -0.25)))
    me = bpy.data.meshes.new('Pot%d' % i); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('Pot%d' % i, me); sc.collection.objects.link(o); me.materials.append(pot_mat)
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.42, 0.45, 0.5, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.2
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(40), 0, math.radians(-30))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d, scale, target in (('all', Vector((0.3, -1, 0.35)), 7.2, Vector((0, 0, 0.9))), ('pot', Vector((0.4, -1, 0.5)), 2.2, Vector((-1.8, 0, 0.35))),
                              ('tree', Vector((0.3, -1, 0.3)), 3.2, Vector((1.8, 0, 1.0))), ('s3', Vector((0.3, -1, 0.3)), 9.0, Vector((7.2, 0, 1.2)))):
    d = d.normalized(); cam.location = target + d * 6; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 900; sc.render.resolution_y = 520
    sc.render.filepath = os.path.join(pv, 'plant_%s.png' % tag); bpy.ops.render.render(write_still=True)
