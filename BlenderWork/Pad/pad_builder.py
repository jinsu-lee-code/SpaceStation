# 11-12 휴대 패드 모델 (2026-10-10): 튼튼한 정거장용 태블릿 — 둥근 몸체 · 어두운 화면 · 주황 모서리 범퍼(정거장 강조색) · 옆 손잡이 · 위 센서 바
# 11-16 피드백 (2026-10-11): "로우폴리지만 허접해 보임" → 두 톤 몸체(밝은 뒤판 + 어두운 앞 베젤) · 화면 둘레 홈 · ㄱ자 모서리 가드(나사) ·
#   손잡이 고무 마디 · 위 가운데 홀로그램 투사기(렌즈 = Unity HoloAnchor 바로 아래) · 스피커 구멍 · 상태 LED · 위 버튼 · 뒤 배터리 판
# 사용: blender -b --factory-startup -P BlenderWork/Pad/pad_builder.py -- [fbx 출력 경로]
#   → BlenderWork/Pad/pad.blend + 미리보기 BlenderWork/Pad/preview/pad_*.png (+ fbx)
# 좌표: 미터, 가운데 원점, 화면이 +y를 봄 (Unity에서 −z = 카메라 쪽). 가로 x · 세로 z (Unity y)
# 화면 앞면: y = SCREEN_Y, 크기 SCREEN_W × SCREEN_H — Unity 패드 UI(월드 캔버스)를 이 면 바로 앞에 놓음 (InteriorPad.ScreenSize)
# 같은 평면 겹침 금지(MODELING.md 3): 조각마다 앞 · 뒤 면 높이(y)를 서로 다르게 — 아래 숫자들은 모두 0.0003 이상 엇갈림
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FBX = argv[0] if argv else None

W, H, D, R = 0.26, 0.175, 0.016, 0.022          # 몸체 가로 · 세로 · 두께 · 모서리 반지름
SCREEN_W, SCREEN_H, SCREEN_R = 0.214, 0.134, 0.008
SCREEN_Y = D / 2 + 0.0015                       # 화면 앞면 (몸체 앞면보다 1.5mm 앞)
LENS = (0.0, 0.002)                             # 투사기 렌즈 가운데 (x, y) — Unity HoloAnchor (0, 0.094, −0.002)
COLORS = {
    'PadBody': ((0.25, 0.28, 0.32), 0.35),     # 뒤판 · 옆 (밝은 회청)
    'PadBezel': ((0.07, 0.08, 0.095), 0.3),    # 앞 베젤 · 투사기 몸통 (어두운 건메탈)
    'PadScreen': ((0.015, 0.02, 0.03), 0.1),   # 무광 — 0.85면 UI가 잠깐 투명해질 때 천장 조명이 반사돼 번쩍였음 (11-13)
    'PadAccent': ((0.95, 0.55, 0.18), 0.4),
    'PadGrip': ((0.06, 0.06, 0.07), 0.12),     # 고무
    'PadMetal': ((0.6, 0.63, 0.67), 0.6),      # 나사 · 렌즈 테
    'PadLight': ((0.3, 0.85, 1.0), 0.5),       # LED · 렌즈 고리 (정거장 홀로그램 색)
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    m.diffuse_color = c + (1,)
    MATS[n] = m
ORDER = list(COLORS)
PARTS = []   # (bmesh, 재질)


def orient_outward(bm):
    """닫힌 덩어리의 부호 있는 부피가 음수면 면을 뒤집음 (MODELING.md 5)."""
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def to3(axis, u, v, w):
    """단면 좌표 (u, v) + 축 방향 w → 3D. axis 'y': (x, z) 단면 · 'z': (x, y) · 'x': (y, z)."""
    if axis == 'y': return (u, w, v)
    if axis == 'z': return (u, v, w)
    return (w, u, v)


def prism(pts, a0, a1, mat, axis='y', bevel=0.0, seg=2):
    """단면 다각형 pts를 축 방향 a0 ~ a1로 밀어낸 닫힌 덩어리 (오목 단면도 됨) + 양 끝 가장자리 둥글게(bevel)."""
    bm = bmesh.new()
    lo = [bm.verts.new(to3(axis, u, v, a0)) for u, v in pts]
    hi = [bm.verts.new(to3(axis, u, v, a1)) for u, v in pts]
    n = len(pts)
    bm.faces.new(hi)
    bm.faces.new(list(reversed(lo)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    orient_outward(bm)
    if bevel > 0:
        k = {'x': 0, 'y': 1, 'z': 2}[axis]
        cap = [e for e in bm.edges if all(abs(v.co[k] - a1) < 1e-7 for v in e.verts) or all(abs(v.co[k] - a0) < 1e-7 for v in e.verts)]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=seg, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # 베벨 뒤 감김이 섞임 (MODELING.md 2) — 작은 덩어리라 재계산 후 부피로 확인
        orient_outward(bm)
    PARTS.append((bm, mat))


def rrect(cx, cz, w, h, r, seg=6):
    """둥근 직사각형 단면 점 (가운데 cx, cz)."""
    pts = []
    r = min(r, w / 2 - 1e-5, h / 2 - 1e-5)
    for ox, oz, a0 in ((w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)))
    return pts


def circle(cx, cz, r, seg=20):
    return [(cx + r * math.cos(2 * math.pi * i / seg), cz + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def slab(cx, cz, w, h, r, y0, y1, mat, seg=6, bevel=0.0):
    """앞에서 본 둥근 직사각형 판 (두께 = y0 ~ y1)."""
    prism(rrect(cx, cz, w, h, r, seg), y0, y1, mat, 'y', bevel)


# ---------------- 몸체 ----------------
# 뒤판(밝은 회청) + 앞 베젤(어두운 건메탈)을 겹쳐 두 톤 — 베젤이 몸체보다 4mm 작아 둘레에 밝은 테두리가 보임
slab(0, 0, W, H, R, -D / 2, D / 2, 'PadBody', seg=8, bevel=0.0025)
slab(0, 0, W - 0.008, H - 0.008, R - 0.004, D / 2 - 0.0018, D / 2 + 0.0008, 'PadBezel', seg=8, bevel=0.0008)
# 화면 둘레 홈 (화면보다 2mm 큰 고무 테 — 화면이 베젤에 박힌 것처럼)
slab(0, 0, SCREEN_W + 0.004, SCREEN_H + 0.004, SCREEN_R + 0.002, D / 2 - 0.0006, D / 2 + 0.0011, 'PadGrip', seg=4, bevel=0.0004)
# 화면 (앞면 = SCREEN_Y)
slab(0, 0, SCREEN_W, SCREEN_H, SCREEN_R, D / 2 - 0.0005, SCREEN_Y, 'PadScreen', seg=4, bevel=0.0004)

# ---------------- ㄱ자 모서리 가드 (주황) + 나사 ----------------
GUARD_L, GUARD_T, GUARD_E = 0.044, 0.011, 0.0022     # 팔 길이(몸체 가장자리에서) · 폭 · 몸체 밖으로 나온 정도
cx0, cz0 = W / 2 - R, H / 2 - R
Ro = R + GUARD_E
Ri = Ro - GUARD_T
for sx in (1, -1):
    for sz in (1, -1):
        pts = [(W / 2 - GUARD_L, cz0 + Ro)]
        for i in range(15):
            a = math.radians(90 - 90 * i / 14)
            pts.append((cx0 + Ro * math.cos(a), cz0 + Ro * math.sin(a)))
        pts.append((cx0 + Ro, H / 2 - GUARD_L))
        pts.append((cx0 + Ri, H / 2 - GUARD_L))
        for i in range(15):
            a = math.radians(90 * i / 14)
            pts.append((cx0 + Ri * math.cos(a), cz0 + Ri * math.sin(a)))
        pts.append((W / 2 - GUARD_L, cz0 + Ri))
        prism([(sx * u, sz * v) for u, v in pts], -D / 2 - 0.0016, D / 2 + 0.0021, 'PadAccent', 'y', bevel=0.0012)
        # 나사: 가드 띠 가운데 45° (머리 + 십자 홈 대신 가운데 어두운 점)
        mr = (Ro + Ri) / 2
        sxp, szp = sx * (cx0 + mr * math.cos(math.radians(45))), sz * (cz0 + mr * math.sin(math.radians(45)))
        prism(circle(sxp, szp, 0.0021, 16), D / 2 + 0.0016, D / 2 + 0.0029, 'PadMetal', 'y', bevel=0.0004)
        prism(circle(sxp, szp, 0.0008, 10), D / 2 + 0.0024, D / 2 + 0.0033, 'PadGrip', 'y')

# ---------------- 옆 손잡이 (고무) + 마디 ----------------
for sx in (1, -1):
    slab(sx * (W / 2 + 0.0015), 0, 0.012, 0.085, 0.0055, -D / 2 - 0.002, D / 2 + 0.0019, 'PadGrip', seg=4, bevel=0.0015)
    for i in range(7):
        z = -0.03 + i * 0.01
        slab(sx * (W / 2 + 0.0072), z, 0.004, 0.0036, 0.0017, -D / 2 - 0.0003, D / 2 + 0.0003, 'PadGrip', seg=3, bevel=0.0006)

# ---------------- 위 가운데 홀로그램 투사기 ----------------
# 몸통: 위 가장자리에 솟은 사다리꼴 (어두운 건메탈), 렌즈 = 금속 테 → 청록 고리 → 어두운 유리 (계단식, 윗면 높이 모두 다름)
top = H / 2 + 0.0055
hous = [(-0.033, H / 2 - 0.009), (0.033, H / 2 - 0.009), (0.033, H / 2 - 0.0005), (0.024, top), (-0.024, top), (-0.033, H / 2 - 0.0005)]
prism(hous, -D / 2 - 0.0006, D / 2 + 0.0013, 'PadBezel', 'y', bevel=0.0011)
lx, ly = LENS
prism(circle(lx, ly, 0.0068, 24), top - 0.001, top + 0.0012, 'PadMetal', 'z', bevel=0.0004)
prism(circle(lx, ly, 0.0058, 24), top - 0.0005, top + 0.0016, 'PadLight', 'z', bevel=0.0003)
prism(circle(lx, ly, 0.0047, 24), top, top + 0.0021, 'PadScreen', 'z', bevel=0.0004)
# 투사기 앞면 통풍 홈 (양옆 3줄씩)
for sx in (1, -1):
    for i in range(3):
        slab(sx * (0.015 + i * 0.0045), H / 2 + 0.0005, 0.0016, 0.0062, 0.0007, D / 2 + 0.0009, D / 2 + 0.0019, 'PadGrip', seg=2)

# ---------------- 위 · 아래 베젤 띠 ----------------
# 위 센서 바 + 카메라 점
slab(0, SCREEN_H / 2 + 0.0095, 0.034, 0.0045, 0.002, D / 2 + 0.0002, D / 2 + 0.0014, 'PadScreen', seg=3, bevel=0.0003)
prism(circle(0.0105, SCREEN_H / 2 + 0.0095, 0.0011, 12), D / 2 + 0.0007, D / 2 + 0.0017, 'PadMetal', 'y')
# 아래 왼쪽: 상태 LED 3개 (청록 · 청록 · 주황)
for i, mat in enumerate(('PadLight', 'PadLight', 'PadAccent')):
    slab(-0.08 + i * 0.0065, -(SCREEN_H / 2 + 0.0098), 0.004, 0.0017, 0.0008, D / 2 + 0.0004, D / 2 + 0.0015, mat, seg=2)
# 아래 오른쪽: 스피커 구멍 (세로 홈 8줄)
for i in range(8):
    slab(0.057 + i * 0.0036, -(SCREEN_H / 2 + 0.0098), 0.0013, 0.0065, 0.00055, D / 2 + 0.0003, D / 2 + 0.0012, 'PadGrip', seg=2)
# 아래 가운데: 홈 버튼 자리 (얇은 금속 테 + 고무)
slab(0, -(SCREEN_H / 2 + 0.0098), 0.022, 0.0052, 0.0026, D / 2 + 0.0001, D / 2 + 0.0010, 'PadMetal', seg=3, bevel=0.0003)
slab(0, -(SCREEN_H / 2 + 0.0098), 0.0185, 0.0034, 0.0017, D / 2 + 0.0005, D / 2 + 0.0013, 'PadGrip', seg=3)

# ---------------- 위 가장자리 버튼 · 뒤 배터리 판 ----------------
for x, w in ((-0.062, 0.012), (-0.08, 0.008)):
    slab(x, H / 2 + 0.0008, w, 0.0042, 0.0018, -0.0035, 0.0037, 'PadGrip', seg=3, bevel=0.0005)
slab(0, -0.004, W - 0.07, H - 0.06, 0.012, -D / 2 - 0.0012, -D / 2 + 0.0007, 'PadBezel', seg=6, bevel=0.0006)
for i in range(6):
    slab(-0.04 + i * 0.016, 0.052, 0.009, 0.0024, 0.0011, -D / 2 - 0.0019, -D / 2 - 0.0004, 'PadGrip', seg=2)

me = bpy.data.meshes.new('SM_InteriorPad'); ob = bpy.data.objects.new('SM_InteriorPad', me)
bpy.context.scene.collection.objects.link(ob)
for n in ORDER: me.materials.append(MATS[n])
big = bmesh.new()
for bm, mat in PARTS:
    vm = {v: big.verts.new(v.co) for v in bm.verts}
    for f in bm.faces:
        nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = ORDER.index(mat)
    bm.free()
bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')   # 오목 단면(ㄱ자 가드)도 Unity에서 바르게
big.to_mesh(me); big.free()
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
for p in me.polygons: p.use_smooth = True
try:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
except Exception as e:
    print('PAD smooth-by-angle skipped', e)

bm = bmesh.new(); bm.from_mesh(me)
open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
# 덩어리별 부호 있는 부피 (음수 = 뒤집힘)
bm.verts.ensure_lookup_table()
seen = set(); flipped = 0; chunks = 0
for f0 in bm.faces:
    if f0.index in seen: continue
    stack = [f0]; group = []
    seen.add(f0.index)
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
print('PAD verts', len(me.vertices), 'faces', len(me.polygons), 'open', open_, 'nonmanifold', nonman, 'chunks', chunks, 'flipped', flipped,
      'screen_y %.4f screen %.3f x %.3f' % (SCREEN_Y, SCREEN_W, SCREEN_H))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'pad.blend'))

if FBX:
    os.makedirs(os.path.dirname(FBX), exist_ok=True)
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')

# 미리보기
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng
        break
    except TypeError:
        pass
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.42, 0.45, 0.5, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.0
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(-60), 0, math.radians(20))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d, scale, target in (('front', Vector((0, 1, 0.05)), 0.34, Vector((0, 0, 0))), ('q', Vector((0.6, 0.8, 0.45)), 0.34, Vector((0, 0, 0))),
                              ('back', Vector((-0.4, -1, 0.3)), 0.34, Vector((0, 0, 0))),
                              ('top', Vector((0.15, 0.7, 0.7)), 0.12, Vector((0, 0, H / 2))),
                              ('corner', Vector((0.5, 0.8, 0.35)), 0.09, Vector((W / 2 - 0.02, 0, -H / 2 + 0.02)))):
    d = d.normalized(); cam.location = target + d * 2; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = 700; sc.render.resolution_y = 520
    sc.render.filepath = os.path.join(pv, 'pad_%s.png' % tag); bpy.ops.render.render(write_still=True)
