# 11-12 휴대 패드 모델 (2026-10-10): 튼튼한 정거장용 태블릿 — 둥근 몸체 · 어두운 화면 · 주황 모서리 범퍼(정거장 강조색) · 옆 손잡이 · 위 센서 바
# 사용: blender -b --factory-startup -P BlenderWork/Pad/pad_builder.py -- [fbx 출력 경로]
#   → BlenderWork/Pad/pad.blend + 미리보기 BlenderWork/Pad/preview/pad_*.png (+ fbx)
# 좌표: 미터, 가운데 원점, 화면이 +y를 봄 (Unity에서 −z = 카메라 쪽). 가로 x · 세로 z
# 화면 앞면: y = SCREEN_Y, 크기 SCREEN_W × SCREEN_H — Unity 패드 UI(월드 캔버스)를 이 면 바로 앞에 놓음 (InteriorPad.ScreenSize)
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FBX = argv[0] if argv else None

W, H, D, R = 0.26, 0.175, 0.016, 0.022          # 몸체 가로 · 세로 · 두께 · 모서리 반지름
SCREEN_W, SCREEN_H, SCREEN_R = 0.214, 0.134, 0.008
SCREEN_Y = D / 2 + 0.0015                       # 화면 앞면 (몸체 앞면보다 1.5mm 앞)
COLORS = {
    'PadBody': ((0.21, 0.24, 0.28), 0.35),
    'PadScreen': ((0.015, 0.02, 0.03), 0.85),
    'PadAccent': ((0.95, 0.55, 0.18), 0.4),
    'PadGrip': ((0.09, 0.09, 0.1), 0.15),
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


def rounded_slab(cx, cy, cz, w, h, d, r, mat, seg=6, bevel=0.0015):
    """둥근 직사각형(x 가로 · z 세로, 모서리 반지름 r)을 y로 두께 d만큼 + 가장자리 둥글게 (bevel)."""
    bm = bmesh.new()
    pts = []
    corners = [(w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)]
    for ox, oz, a0 in corners:
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cz + oz + r * math.sin(a)))
    back = [bm.verts.new((x, cy - d / 2, z)) for x, z in pts]
    front = [bm.verts.new((x, cy + d / 2, z)) for x, z in pts]
    n = len(pts)
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((back[i], back[j], front[j], front[i]))
    orient_outward(bm)
    if bevel > 0:
        cap = [e for e in bm.edges if all(abs(v.co.y - (cy + d / 2)) < 1e-6 for v in e.verts) or all(abs(v.co.y - (cy - d / 2)) < 1e-6 for v in e.verts)]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=2, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # 베벨 뒤 감김이 섞임 (MODELING.md 2) — 작은 볼록 덩어리라 재계산 후 부피로 확인
        orient_outward(bm)
    PARTS.append((bm, mat))


# 몸체
rounded_slab(0, 0, 0, W, H, D, R, 'PadBody', seg=8, bevel=0.0025)
# 화면 (몸체 앞면 위로 살짝 튀어나온 얇은 판)
rounded_slab(0, D / 2 + 0.0005, 0, SCREEN_W, SCREEN_H, 0.002, SCREEN_R, 'PadScreen', seg=4, bevel=0.0004)
# 모서리 범퍼 (몸체 모서리를 감싸게 조금 크게)
for sx in (1, -1):
    for sz in (1, -1):
        rounded_slab(sx * (W / 2 - 0.013), 0, sz * (H / 2 - 0.013), 0.036, 0.036, D + 0.006, 0.014, 'PadAccent', seg=5, bevel=0.002)
# 옆 손잡이 (고무)
for sx in (1, -1):
    rounded_slab(sx * (W / 2 + 0.0015), 0, 0, 0.012, 0.075, D + 0.004, 0.0055, 'PadGrip', seg=4, bevel=0.0015)
# 위 센서 바 (화면과 위 가장자리 사이)
rounded_slab(0, D / 2 + 0.0004, SCREEN_H / 2 + 0.0095, 0.034, 0.0045, 0.0018, 0.002, 'PadScreen', seg=3, bevel=0.0003)

me = bpy.data.meshes.new('SM_InteriorPad'); ob = bpy.data.objects.new('SM_InteriorPad', me)
bpy.context.scene.collection.objects.link(ob)
for n in ORDER: me.materials.append(MATS[n])
big = bmesh.new()
for bm, mat in PARTS:
    vm = {v: big.verts.new(v.co) for v in bm.verts}
    for f in bm.faces:
        nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = ORDER.index(mat)
    bm.free()
big.to_mesh(me); big.free()
bpy.context.view_layer.objects.active = ob; ob.select_set(True)
for p in me.polygons: p.use_smooth = True
try:
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
except Exception as e:
    print('PAD smooth-by-angle skipped', e)

bm = bmesh.new(); bm.from_mesh(me)
open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
bm.free()
print('PAD verts', len(me.vertices), 'faces', len(me.polygons), 'open', open_, 'nonmanifold', nonman,
      'screen_y %.4f screen %.3f x %.3f' % (SCREEN_Y, SCREEN_W, SCREEN_H))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'pad.blend'))

if FBX:
    os.makedirs(os.path.dirname(FBX), exist_ok=True)
    for x in bpy.context.view_layer.objects: x.select_set(x is ob)
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')

# 미리보기
sc = bpy.context.scene
try:
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    sc.render.engine = 'BLENDER_EEVEE'
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.42, 0.45, 0.5, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.0
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(-60), 0, math.radians(20))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for tag, d in (('front', Vector((0, 1, 0.05))), ('q', Vector((0.6, 0.8, 0.45))), ('back', Vector((-0.4, -1, 0.3)))):
    d = d.normalized(); cam.location = d * 2; cam.rotation_euler = (-d).to_track_quat('-Z', 'Z').to_euler()
    cd.ortho_scale = 0.34; sc.render.resolution_x = 700; sc.render.resolution_y = 520
    sc.render.filepath = os.path.join(pv, 'pad_%s.png' % tag); bpy.ops.render.render(write_still=True)
