# 12-3b 합성 연출 (2026-10-13 사용자 결정, 스타크래프트 2 테란 건설 캡처 참고):
#   합성 = 규격 둘레를 감싸는 기계 고리(칼라)가 용접선 높이로 아래부터 올라가며, 고리 위 받침에 앉은 기계팔들이 안쪽 규격을 용접한다.
#   재료는 아래부터 사라지고 상위 모듈이 아래부터 차오름 (경계 = 고리 윗면). 고리에는 보라 형광(신비한 느낌, 사용자 요청).
#   받침 기둥 방식은 우주에서 허공에 박힌 것처럼 보여 폐기 (사용자 피드백 2026-10-13).
# 특징(MODELING.md 7): 마디로 이어진 고리 + 보라 형광 띠 · 모서리 발광 결정체 · 안쪽 롤러(모듈 몸체를 물고 오름) ·
#   윗팔 유압 피스톤 · 관절 볼트 · 구리 끝 용접 토치 · 상태등
# 사용: blender -b --factory-startup -P BlenderWork/SynthArm/arm_builder.py -- [fbx 출력 폴더]
#   → BlenderWork/SynthArm/arm.blend + 미리보기 preview/*.png (+ fbx: SM_SynthRing.fbx · SM_SynthArm.fbx)
# 좌표: 미터(정거장 칸 = 1), Blender z = 위(Unity y). 고리 중심선 높이 = z 0.
# 고리 토막 (Unity가 규격 둘레 크기로 조립 — 아래 assemble_ring과 같은 규칙):
#   SynthRing_Straight: 규격 변 1칸. 원점 = 그 칸 변의 가운데(경계면 위), 변 방향 = x(−0.5 ~ +0.5), 바깥 = −y, 안쪽 몸체 쪽 = +y
#   SynthRing_Corner  : 규격 모서리. 원점 = 경계 모서리 점, 규격 안쪽 = x < 0 · y > 0 사분면 (오른쪽 아래 모서리 기준, 나머지는 돌려 씀)
# 팔 (계층, 각 원점 = 관절 축, 쉬는 자세 = 곧게 위로, 일하는 쪽 = +y 안쪽):
#   SynthMount(직선 토막 가운데 위, 원점 = 토막 원점과 같음) > SynthArm_Turret(위 축) > SynthArm_Upper(어깨 x 축) > SynthArm_Fore(팔꿈치)
#     > SynthArm_Head(손목) > SynthArm_Tip(빈 오브젝트 = 토치 끝)
# 같은 평면 겹침 금지(MODELING.md 3): 겹치는 조각끼리 반지름 · 끝면을 0.002 이상 엇갈림
import bpy, bmesh, sys, os, math
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
FBX_DIR = argv[0] if argv else None

# 고리 단면: 안쪽 면 y = +RI, 바깥 면 y = −RO, 높이 ±RH.
# 사용자 결정 2026-10-13 "고리가 비행하는 느낌": 모듈 겉면(경계 안쪽 0.04)과 닿지 않게 RI = 0 → 4cm 틈, 롤러 없음, 아래 추진 노즐 · 모서리 반중력 원반
RI, RO, RH = 0.0, 0.115, 0.042
NOZZLE_Y = -(RO - RI) / 2       # 노즐 줄 (고리 단면 가운데)
# 팔: 받침 위 회전대 위치(직선 토막 원점 기준), 크기 배율 (고리에 맞게 작게)
MOUNT = Vector((0.0, -0.035, RH + 0.03))
S = 0.56
TURRET_Z, SHOULDER_Z, ELBOW_Z, WRIST_Z, TIP_Z = 0.0, 0.125, 0.585, 0.965, 1.105   # 배율 전, MOUNT 기준

COLORS = {
    'ArmBody': ((0.95, 0.62, 0.16), 0.45),     # 산업용 노랑 주황 (팔)
    'ArmDark': ((0.08, 0.09, 0.105), 0.35),    # 관절 · 고리 몸통 (어두운 건메탈)
    'ArmMetal': ((0.62, 0.65, 0.69), 0.7),     # 피스톤 막대 · 볼트 · 롤러
    'ArmCopper': ((0.72, 0.38, 0.18), 0.55),   # 토치 끝
    'ArmLight': ((0.3, 0.85, 1.0), 0.4),       # 팔 상태등 (Unity에서 발광)
    'ArmGlow': ((0.62, 0.3, 1.0), 0.5),        # 고리 보라 형광 (Unity에서 강한 발광 · 맥동)
}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, (c, smooth) in COLORS.items():
    m = bpy.data.materials.new('M_' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 1 - smooth
    if n == 'ArmGlow':   # 미리보기에서도 빛나 보이게 (FBX에는 색만 감 — Unity 재질이 발광 담당)
        b.inputs['Emission Color'].default_value = c + (1,); b.inputs['Emission Strength'].default_value = 0.9
    m.diffuse_color = c + (1,)
    MATS[n] = m
ORDER = list(COLORS)
PARTS = {}   # 오브젝트 이름 → [(bmesh, 재질)]
CUR = ['']
OFF = [Vector((0, 0, 0))]


def orient_outward(bm):
    """닫힌 덩어리의 부호 있는 부피가 음수면 면을 뒤집음 (MODELING.md 5)."""
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def to3(axis, u, v, w):
    """단면 좌표 (u, v) + 축 방향 w → 3D. axis 'z': (x, y) 단면 · 'x': (y, z) · 'y': (x, z)."""
    if axis == 'y': return Vector((u, w, v))
    if axis == 'z': return Vector((u, v, w))
    return Vector((w, u, v))


def loft(sec0, sec1, a0, a1, mat, axis='z', bevel=0.0, seg=2):
    """단면 sec0(a0) → sec1(a1)을 이은 닫힌 덩어리 (두 단면 점 수 같음, 끝 가장자리 둥글게)."""
    bm = bmesh.new()
    lo = [bm.verts.new(to3(axis, u, v, a0) + OFF[0]) for u, v in sec0]
    hi = [bm.verts.new(to3(axis, u, v, a1) + OFF[0]) for u, v in sec1]
    n = len(sec0)
    bm.faces.new(hi)
    bm.faces.new(list(reversed(lo)))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    orient_outward(bm)
    if bevel > 0:
        k = {'x': 0, 'y': 1, 'z': 2}[axis]
        e0, e1 = a0 + OFF[0][k], a1 + OFF[0][k]
        cap = [e for e in bm.edges if all(abs(v.co[k] - e1) < 1e-7 for v in e.verts) or all(abs(v.co[k] - e0) < 1e-7 for v in e.verts)]
        bmesh.ops.bevel(bm, geom=cap, offset=bevel, segments=seg, profile=0.5, affect='EDGES')
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)   # 베벨 뒤 감김이 섞임 (MODELING.md 2)
        orient_outward(bm)
    PARTS.setdefault(CUR[0], []).append((bm, mat))


def prism(pts, a0, a1, mat, axis='z', bevel=0.0, seg=2):
    loft(pts, pts, a0, a1, mat, axis, bevel, seg)


def rrect(cx, cy, w, h, r, seg=4):
    pts = []
    r = min(r, w / 2 - 1e-5, h / 2 - 1e-5)
    for ox, oy, a0 in ((w / 2 - r, h / 2 - r, 0), (-(w / 2 - r), h / 2 - r, 90), (-(w / 2 - r), -(h / 2 - r), 180), (w / 2 - r, -(h / 2 - r), 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cy + oy + r * math.sin(a)))
    return pts


def circle(cx, cy, r, seg=20):
    return [(cx + r * math.cos(2 * math.pi * i / seg), cy + r * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def box(x0, x1, y0, y1, z0, z1, mat, bevel=0.0):
    prism([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z0, z1, mat, 'z', bevel)


def disc_x(y, z, r, x0, x1, mat, seg=20, bevel=0.0):
    prism(circle(y, z, r, seg), x0, x1, mat, 'x', bevel)


def bolts_x(x, y, z, r, n, mat):
    """관절 원판 바깥면(x)에 둥글게 박힌 볼트 n개 (원판 면에서 0.004 솟음, 0.002 묻힘)."""
    for i in range(n):
        a = 2 * math.pi * i / n + 0.3
        lo, hi = (x - 0.002, x + 0.004) if x > 0 else (x - 0.004, x + 0.002)
        prism(circle(y + r * math.cos(a), z + r * math.sin(a), 0.0065, 8), lo, hi, mat, 'x')


# ================= 고리 직선 토막 (1칸) =================
CUR[0] = 'SynthRing_Straight'
HALF = 0.502   # 이웃 토막과 0.004 겹침 (끝면이 같은 평면 · 맞닿는 틈 없게 — MODELING.md 3 · 4)
sec = [(u - (RO - RI) / 2, v) for u, v in rrect(0, 0, RO + RI, 2 * RH, 0.022, 4)]
prism(sec, -HALF, HALF, 'ArmDark', 'x', bevel=0.006)
# 보라 형광: 윗면 바깥쪽 띠 · 바깥 옆면 가운데 띠 (토막 끝 0.05 앞에서 끊겨 마디가 보임)
box(-0.44, 0.44, -0.075, -0.035, RH - 0.004, RH + 0.006, 'ArmGlow')
box(-0.44, 0.44, -RO - 0.006, -RO + 0.004, -0.011, 0.011, 'ArmGlow')
# 바깥 옆 볼트 띠 (토막 이음매)
for x in (-0.47, 0.47):
    box(x - 0.012, x + 0.012, -RO - 0.008, -RO + 0.01, -RH + 0.006, RH - 0.006, 'ArmMetal', bevel=0.003)
# 아랫면 추진 노즐 2개: 몸통 아래로 튀어나온 통(어두운) → 금속 테 → 안쪽 보라 빛 (Unity가 아래로 불꽃 원뿔 · 입자)
for x in (-0.25, 0.25):
    loft(circle(x, NOZZLE_Y, 0.034, 20), circle(x, NOZZLE_Y, 0.04, 20), -RH - 0.03, -RH + 0.006, 'ArmDark', bevel=0.003)
    prism(circle(x, NOZZLE_Y, 0.043, 20), -RH - 0.006, -RH + 0.004, 'ArmMetal', bevel=0.002)
    loft(circle(x, NOZZLE_Y, 0.026, 18), circle(x, NOZZLE_Y, 0.021, 18), -RH - 0.034, -RH - 0.022, 'ArmGlow')

# ================= 고리 모서리 토막 =================
CUR[0] = 'SynthRing_Corner'
# 규격 안쪽 = x < 0 · y > 0. 몸체 = 두 변 고리의 바깥 귀퉁이 (x −RI' ~ +RO, y −RO ~ +RI'), 바깥 귀 둥글게, 직선보다 조금 큼
CI, CO, CH = RI - 0.004, RO + 0.012, RH + 0.008
pts = [(-CI - 0.03, CI), (-CI - 0.03, -CO)]
for i in range(9):   # 바깥 귀퉁이 (x +, y −) 둥글게
    a = math.radians(-90 + 90 * i / 8)
    pts.append((CO - 0.05 + 0.05 * math.cos(a), -CO + 0.05 + 0.05 * math.sin(a)))
pts += [(CO, CI + 0.03), (-CI, CI + 0.03), (-CI, CI)]
prism(pts, -CH, CH, 'ArmDark', bevel=0.006)
# 모서리 발광 결정체 (위로 솟은 보라 팔각 기둥 + 받침 고리)
cx, cy = (CO - CI) / 2 - 0.004, -(CO - CI) / 2 + 0.004
prism(circle(cx, cy, 0.04, 16), CH - 0.004, CH + 0.016, 'ArmMetal', bevel=0.003)
loft(circle(cx, cy, 0.024, 8), circle(cx, cy, 0.011, 8), CH + 0.014, CH + 0.075, 'ArmGlow')
# 아래 반중력 원반: 어두운 받침 원반 + 금속 테 + 보라 빛 원판 (Unity가 아래로 빛 · 입자)
prism(circle(cx, cy, 0.052, 24), -CH - 0.022, -CH + 0.004, 'ArmDark', bevel=0.004)
prism(circle(cx, cy, 0.046, 24), -CH - 0.028, -CH - 0.016, 'ArmMetal', bevel=0.002)
prism(circle(cx, cy, 0.036, 24), -CH - 0.032, -CH - 0.024, 'ArmGlow')

# ================= 팔 받침 (직선 토막 위) =================
CUR[0] = 'SynthMount'
box(-0.085, 0.085, -0.1, -0.002, RH - 0.002, MOUNT.z - 0.012, 'ArmDark', bevel=0.005)
prism(circle(MOUNT.x, MOUNT.y, 0.062, 24), MOUNT.z - 0.016, MOUNT.z, 'ArmBody', bevel=0.004)
box(-0.07, -0.05, -0.103, -0.097, RH + 0.006, MOUNT.z - 0.02, 'ArmLight')

# ================= 팔 (MOUNT 기준으로 만든 뒤 배율 S) =================
OFF[0] = MOUNT
CUR[0] = 'SynthArm_Turret'
prism(circle(0, 0, 0.066, 24), TURRET_Z + 0.001, 0.048, 'ArmDark', bevel=0.004)
prism(circle(0, 0, 0.056, 24), 0.046, 0.056, 'ArmBody', bevel=0.003)
for sx in (1, -1):
    prism(rrect(0.0, 0.095, 0.09, 0.09, 0.03), sx * 0.04, sx * 0.058, 'ArmBody', 'x', bevel=0.004)
disc_x(0.0, SHOULDER_Z, 0.019, -0.068, 0.068, 'ArmMetal', seg=16)

CUR[0] = 'SynthArm_Upper'
disc_x(0.0, SHOULDER_Z, 0.040, -0.036, 0.036, 'ArmDark', seg=24, bevel=0.004)
for sx in (1, -1):
    bolts_x(sx * 0.036, 0.0, SHOULDER_Z, 0.027, 6, 'ArmMetal')
prism(rrect(0, 0, 0.058, 0.052, 0.013), SHOULDER_Z + 0.026, ELBOW_Z - 0.025, 'ArmBody', bevel=0.005)
prism(rrect(0, 0.0275, 0.034, 0.008, 0.003), SHOULDER_Z + 0.07, ELBOW_Z - 0.07, 'ArmDark')
prism(circle(0.0, -0.052, 0.015, 14), SHOULDER_Z + 0.04, SHOULDER_Z + 0.25, 'ArmDark', bevel=0.003)
prism(circle(0.0, -0.052, 0.0075, 12), SHOULDER_Z + 0.242, ELBOW_Z - 0.055, 'ArmMetal')
for z in (SHOULDER_Z + 0.045, ELBOW_Z - 0.058):
    prism(rrect(0.0, -0.039, 0.026, 0.03, 0.007), z - 0.011, z + 0.011, 'ArmDark', bevel=0.003)
disc_x(0.0, ELBOW_Z, 0.035, -0.031, 0.031, 'ArmDark', seg=24, bevel=0.004)

CUR[0] = 'SynthArm_Fore'
for sx in (1, -1):
    prism(rrect(0.0, ELBOW_Z + 0.026, 0.066, 0.105, 0.03), sx * 0.034, sx * 0.047, 'ArmBody', 'x', bevel=0.003)
    bolts_x(sx * 0.047, 0.0, ELBOW_Z, 0.02, 4, 'ArmMetal')
prism(rrect(0, 0, 0.048, 0.043, 0.011), ELBOW_Z + 0.066, WRIST_Z - 0.023, 'ArmBody', bevel=0.005)
prism(circle(0.0, 0.03, 0.0068, 10), ELBOW_Z + 0.055, WRIST_Z - 0.032, 'ArmDark')
disc_x(0.0, WRIST_Z, 0.028, -0.027, 0.027, 'ArmDark', seg=20, bevel=0.003)

CUR[0] = 'SynthArm_Head'
for sx in (1, -1):
    prism(rrect(0.0, WRIST_Z + 0.02, 0.05, 0.062, 0.021), sx * 0.029, sx * 0.04, 'ArmDark', 'x', bevel=0.003)
loft(circle(0, 0, 0.03, 20), circle(0, 0, 0.024, 20), WRIST_Z + 0.04, TIP_Z - 0.055, 'ArmDark', bevel=0.003)
prism(circle(0, 0, 0.032, 20), WRIST_Z + 0.053, WRIST_Z + 0.064, 'ArmBody', bevel=0.002)
loft(circle(0, 0, 0.019, 18), circle(0, 0, 0.01, 18), TIP_Z - 0.057, TIP_Z - 0.005, 'ArmCopper', bevel=0.002)
prism(circle(0, 0, 0.005, 10), TIP_Z - 0.007, TIP_Z, 'ArmMetal')
prism(rrect(0.0, -0.0275, 0.014, 0.008, 0.003), WRIST_Z + 0.072, WRIST_Z + 0.088, 'ArmLight')
OFF[0] = Vector((0, 0, 0))

# ================= 오브젝트 만들기 =================
ARM = ('SynthArm_Turret', 'SynthArm_Upper', 'SynthArm_Fore', 'SynthArm_Head')


def scaled(p):
    return MOUNT + (p - MOUNT) * S


PIVOT = {'SynthRing_Straight': Vector((0, 0, 0)), 'SynthRing_Corner': Vector((0, 0, 0)), 'SynthMount': Vector((0, 0, 0)),
         'SynthArm_Turret': scaled(MOUNT + Vector((0, 0, TURRET_Z))), 'SynthArm_Upper': scaled(MOUNT + Vector((0, 0, SHOULDER_Z))),
         'SynthArm_Fore': scaled(MOUNT + Vector((0, 0, ELBOW_Z))), 'SynthArm_Head': scaled(MOUNT + Vector((0, 0, WRIST_Z)))}
PARENT = {'SynthArm_Turret': 'SynthMount', 'SynthArm_Upper': 'SynthArm_Turret', 'SynthArm_Fore': 'SynthArm_Upper', 'SynthArm_Head': 'SynthArm_Fore'}
OBJS = {}
report = []
for name, parts in PARTS.items():
    piv = PIVOT[name]
    me = bpy.data.meshes.new(name); ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    used = [n for n in ORDER if any(m == n for _, m in parts)]
    for n in used: me.materials.append(MATS[n])
    big = bmesh.new()
    for bm, mat in parts:
        vm = {v: big.verts.new((scaled(v.co) if name in ARM else v.co) - piv) for v in bm.verts}
        for f in bm.faces:
            nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = used.index(mat)
        bm.free()
    bmesh.ops.triangulate(big, faces=big.faces[:], quad_method='BEAUTY', ngon_method='BEAUTY')
    open_ = sum(1 for e in big.edges if len(e.link_faces) == 1); nonman = sum(1 for e in big.edges if len(e.link_faces) > 2)
    big.to_mesh(me); big.free()
    for p in me.polygons: p.use_smooth = True
    OBJS[name] = ob
    report.append('%s f%d open%d nm%d' % (name, len(me.polygons), open_, nonman))

for name, ob in OBJS.items():
    if name in PARENT:
        ob.parent = OBJS[PARENT[name]]
        ob.location = PIVOT[name] - PIVOT[PARENT[name]]
    else:
        ob.location = PIVOT[name]
tip = bpy.data.objects.new('SynthArm_Tip', None)
bpy.context.scene.collection.objects.link(tip)
tip.parent = OBJS['SynthArm_Head']; tip.location = (0, 0, (TIP_Z - WRIST_Z) * S); tip.empty_display_size = 0.02

bpy.context.view_layer.update()
for ob in OBJS.values():
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.view_layer.objects: o.select_set(o is ob)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(40))
    except Exception as e:
        print('ARM smooth-by-angle skipped', e)
print('ARM', ' | '.join(report))
print('ARM pivots', {k: tuple(round(c, 4) for c in v) for k, v in PIVOT.items() if k in ARM}, 'tip len %.4f' % ((TIP_Z - WRIST_Z) * S))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'arm.blend'))


def export(path, roots):
    """계층이 있는 FBX는 bake_space_transform을 끈다 — 켜면 자식 관절 회전 · 위치가 엉켜 들어왔음 (팔 Upper 90° 등).
    축 변환은 Unity ModelImporter.bakeAxisConversion(SynthesisFxSetup)이 맡는다."""
    keep = set()
    for r in roots:
        keep.add(r)
        keep.update(r.children_recursive)
    for o in bpy.context.view_layer.objects: o.select_set(o in keep)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=False, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')


if FBX_DIR:
    os.makedirs(FBX_DIR, exist_ok=True)
    export(os.path.join(FBX_DIR, 'SM_SynthRing.fbx'), [OBJS['SynthRing_Straight'], OBJS['SynthRing_Corner']])
    export(os.path.join(FBX_DIR, 'SM_SynthArm.fbx'), [OBJS['SynthMount']])

# ================= 미리보기 =================
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng
        break
    except TypeError:
        pass
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.12, 0.13, 0.16, 1)   # 우주처럼 어둡게 (보라 형광이 보이게)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.5
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(-55), 0, math.radians(30))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
for f in os.listdir(pv):
    os.remove(os.path.join(pv, f))   # 이전 설계(기둥) 미리보기 정리


def shot(name, d, scale, target, w=600, h=600):
    d = d.normalized(); cam.location = target + d * 6; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = scale; sc.render.resolution_x = w; sc.render.resolution_y = h
    sc.render.filepath = os.path.join(pv, name + '.png'); bpy.ops.render.render(write_still=True)


L1, L2 = (ELBOW_Z - SHOULDER_Z) * S, (TIP_Z - ELBOW_Z) * S     # 윗팔 · (아래팔 + 곧은 머리)


def joints(root):
    j = {}
    for o in root.children_recursive:
        for k in ('Turret', 'Upper', 'Fore', 'Head', 'Tip'):
            if o.name.startswith('SynthArm_' + k):
                j[k] = o
    return j


def solve(mount, target):
    """2관절 IK (Unity와 같은 계산): 회전대가 목표를 보고, 어깨 · 팔꿈치로 토치 끝(머리 곧게)을 목표에 댐. 팔꿈치는 위로."""
    j = joints(mount)
    for k in ('Turret', 'Upper', 'Fore', 'Head'):
        j[k].rotation_euler = (0, 0, 0)
    bpy.context.view_layer.update()
    p = mount.matrix_world.inverted() @ Vector(target)
    j['Turret'].rotation_euler = (0, 0, math.atan2(-(p.x - MOUNT.x), p.y - MOUNT.y))
    bpy.context.view_layer.update()
    mw = j['Upper'].matrix_world
    s, fwd, up = mw.translation, (mw.to_3x3() @ Vector((0, 1, 0))).normalized(), (mw.to_3x3() @ Vector((0, 0, 1))).normalized()
    v = Vector(target) - s
    f, u = v.dot(fwd), v.dot(up)
    d = max(abs(L1 - L2) + 1e-3, min(L1 + L2 - 1e-3, math.hypot(f, u)))
    a = math.atan2(f, u)
    b = math.acos((L1 * L1 + d * d - L2 * L2) / (2 * L1 * d))
    g = math.acos((L1 * L1 + L2 * L2 - d * d) / (2 * L1 * L2))
    j['Upper'].rotation_euler = (-(a - b), 0, 0)
    j['Fore'].rotation_euler = (-(math.pi - g), 0, 0)
    bpy.context.view_layer.update()
    return (j['Tip'].matrix_world.translation - Vector(target)).length


def copy_tree(root):
    dup = {}
    for o in [root] + list(root.children_recursive):
        c = o.copy()
        if o.data: c.data = o.data
        sc.collection.objects.link(c); dup[o] = c
    for o, c in dup.items():
        if o.parent in dup: c.parent = dup[o.parent]
    return dup[root]


def place(ob, x, y, z, deg):
    ob.location = (x, y, z); ob.rotation_euler = (0, 0, math.radians(deg))


def assemble_ring(x0, x1, y0, y1, z):
    """규격 바닥 사각형 [x0, x1] × [y0, y1] (칸 경계) 둘레에 고리 토막을 놓음 — Unity 조립과 같은 규칙.
    변마다 바깥 방향 o에 대해 토막 회전 θ = atan2(o.x, −o.y) (토막 −y가 o를 봄). 모서리: 오른쪽 아래 0°, 오른쪽 위 90°, 왼쪽 위 180°, 왼쪽 아래 −90°."""
    out = []
    for i in range(int(round(x1 - x0))):
        out.append(('S', x0 + 0.5 + i, y0, 0)); out.append(('S', x0 + 0.5 + i, y1, 180))
    for i in range(int(round(y1 - y0))):
        out.append(('S', x0, y0 + 0.5 + i, -90)); out.append(('S', x1, y0 + 0.5 + i, 90))
    out += [('C', x1, y0, 0), ('C', x1, y1, 90), ('C', x0, y1, 180), ('C', x0, y0, -90)]
    made = []
    for kind, x, y, deg in out:
        src = OBJS['SynthRing_Straight' if kind == 'S' else 'SynthRing_Corner']
        c = src.copy(); c.data = src.data; c.hide_render = False; sc.collection.objects.link(c)
        place(c, x, y, z, deg); made.append(c)
    return made


def make_block(name, x0, x1, y0, y1, z0, z1, col):
    OFF[0] = Vector((0, 0, 0)); CUR[0] = name
    box(x0, x1, y0, y1, z0, z1, 'ArmDark')
    bm2, _ = PARTS[name].pop()
    me = bpy.data.meshes.new(name); bm2.to_mesh(me); bm2.free()
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob)
    m = bpy.data.materials.new('M_' + name); m.use_nodes = True
    next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = col + (1,)
    me.materials.append(m)
    return ob


# 원본 오브젝트는 원점에서 숨김 (복사본으로 장면 구성)
for o in OBJS.values():
    o.hide_render = True
tip.hide_render = True

# 1) 토막 확대: 직선 2개 + 모서리 + 받침 · 팔 (곧게)
ring = assemble_ring(-1.0, 0.0, 0.0, 1.0, 0.0)
for c in ring[:]:
    if not (c.location.y < 0.01 or c.location.x > -0.01):
        c.hide_render = True
mnt = copy_tree(OBJS['SynthMount'])
for o in [mnt] + list(mnt.children_recursive):
    o.hide_render = False
place(mnt, -0.5, 0.0, 0.0, 0)
shot('parts', Vector((0.55, -1, 0.6)), 1.0, Vector((-0.2, 0.1, 0.15)))
for o in ring + [mnt] + list(mnt.children_recursive):
    bpy.data.objects.remove(o, do_unlink=True) if o.name in bpy.data.objects else None

# 2) 장면: 1×2 규격 (x −0.5 ~ 0.5, y 0 ~ 2), 높이 1칸 — 절반 진행 (아래 = 상위 모듈 파랑 회색, 위 = 재료 회색), 팔 2개
SEAM = 0.45
make_block('Result', -0.46, 0.46, 0.04, 1.96, 0.04, SEAM + RH - 0.002, (0.32, 0.45, 0.62))
make_block('Material', -0.44, 0.44, 0.06, 1.94, SEAM + RH + 0.002, 0.96, (0.55, 0.56, 0.58))
ring = assemble_ring(-0.5, 0.5, 0.0, 2.0, SEAM)
arms = []
for x, y, deg, target in ((-0.5, 0.5, -90, (-0.44, 0.75, SEAM + RH + 0.03)), (0.5, 1.5, 90, (0.44, 1.2, SEAM + RH + 0.05))):
    m = copy_tree(OBJS['SynthMount'])
    for o in [m] + list(m.children_recursive):
        o.hide_render = False
    place(m, x, y, SEAM, deg)
    bpy.context.view_layer.update()
    arms.append((m, target))
errs = [solve(m, t) for m, t in arms]

# 미리보기 전용 (Unity 연출 흉내, 내보내지 않음): 노즐 · 반중력 원반 아래 반투명 보라 불꽃 원뿔 + 보라 점광원
plume = bpy.data.materials.new('M_PreviewPlume'); plume.use_nodes = True
pb = next(x for x in plume.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
pb.inputs['Base Color'].default_value = (0.62, 0.3, 1.0, 1); pb.inputs['Alpha'].default_value = 0.35
pb.inputs['Emission Color'].default_value = (0.62, 0.3, 1.0, 1); pb.inputs['Emission Strength'].default_value = 1.6
try:
    plume.surface_render_method = 'BLENDED'
except Exception:
    plume.blend_method = 'BLEND'
cx, cy = (CO - CI) / 2 - 0.004, -(CO - CI) / 2 + 0.004
for c in ring:
    corner = c.data.name.startswith('SynthRing_Corner')
    locs = [Vector((cx, cy, -CH - 0.032))] if corner else [Vector((x, NOZZLE_Y, -RH - 0.034)) for x in (-0.25, 0.25)]
    for lp in locs:
        wp = c.matrix_world @ lp
        r0, ln = (0.034, 0.2) if corner else (0.022, 0.16)
        OFF[0] = Vector((0, 0, 0)); CUR[0] = 'Plume'
        loft(circle(wp.x, wp.y, 0.004, 14), circle(wp.x, wp.y, r0, 14), wp.z - ln, wp.z - 0.002, 'ArmGlow')
        bm2, _ = PARTS['Plume'].pop()
        me = bpy.data.meshes.new('Plume'); bm2.to_mesh(me); bm2.free(); me.materials.append(plume)
        sc.collection.objects.link(bpy.data.objects.new('Plume', me))
        li = bpy.data.lights.new('PL', 'POINT'); li.energy = 1.5; li.color = (0.65, 0.35, 1.0); li.shadow_soft_size = 0.05
        lo2 = bpy.data.objects.new('PL', li); lo2.location = wp - Vector((0, 0, 0.06)); sc.collection.objects.link(lo2)
print('ARM ik err', ['%.4f' % e for e in errs])
shot('scene_half', Vector((0.8, -1, 0.75)), 2.6, Vector((0, 1.0, 0.5)), 900, 650)
shot('scene_low', Vector((1, -0.25, 0.22)), 2.6, Vector((0, 1.0, 0.5)), 900, 650)
shot('scene_close', Vector((-1, -0.6, 0.55)), 0.9, Vector((-0.5, 0.6, SEAM + 0.1)), 700, 600)
shot('scene_under', Vector((0.9, -0.8, -0.3)), 1.0, Vector((0.45, 0.05, SEAM - 0.06)), 700, 600)   # 모서리 아래: 모듈과의 틈 · 노즐 · 원반
