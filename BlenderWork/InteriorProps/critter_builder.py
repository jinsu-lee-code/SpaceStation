# 3단계 내부 정원 동물 (2026-11-10, 궤도 도시 — 사용자 "토끼 모양새도 이상해, 뒷다리는 어디간거야"): 바깥 모듈의 작은 덩어리 동물을 키워 쓰지 않고
# 내부 크기 (미터) 전용 모델. 귀여운 2등신 · 매끈한 몸 (토끼 · 사슴 = 메타볼로 녹인 한 덩어리, 백조 = 겹친 타원체 · 꺾인 관) · 얼굴 = 손그림 아틀라스 T_Critters_BaseColor 얼굴 칸 정면 투영 (MODELING 7-10).
#   Critter_Bunny  앉은 토끼 (큰 뒷다리 허벅지 + 땅에 붙은 긴 뒷발 · 앞발 둘 · 긴 귀 · 솜꼬리) 높이 0.5
#   Critter_Deer   사슴 (다리 넷 · 발굽 · 목 · 큰 머리 · 귀 · 짧은 꼬리) 머리 꼭대기 1.2
#   Critter_Fawn   아기 사슴 (흰 점 무늬) 0.75배
#   Critter_Swan   백조 (물에 뜬 몸 · 접은 날개 · S자 목 · 주황 부리, 원점 = 물높이)
# 좌표: 미터, 발 · 물높이 z = 0, 앞 = +x. 재질 슬롯 하나 (M_Critters — Unity가 ModuleFxMaterials "Critters"로 바꿈).
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/critter_builder.py -- [fbx 경로]
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ATLAS = os.path.join(HERE, '..', '..', 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Modules', 'Textures', 'Evo_Critters', 'T_Critters_BaseColor.png')
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = argv[0] if argv else None

# ---- 아틀라스 칸 (evo_builder critter_tex와 같은 배치) ----
CR_FACES = ["white", "yellow", "green", "coral", "gray", "brown", "cream", "pink"]
CR_SW = ["orange", "dark", "shell", "shell2", "silver", "blue", "red", "leaf"]
CR_PAT = {"wing_blue": 2, "wing_orange": 3, "wing_pink": 4, "fish": 5, "seal": 6, "deer": 7}


def face_center(face):
    i = CR_FACES.index(face); col, row = i % 4, i // 4
    return (col * 0.25 + 0.125, 1.0 - (row + 0.5) / 4.0)


def color_uv(name):
    """색 칸 한 점: 몸 색 칸 가운데, 또는 얼굴 칸 모서리 (= 그 동물 바탕색)."""
    if name in CR_SW:
        j = CR_SW.index(name); return (j * 0.125 + 0.0625, 0.375)
    cu, cv = face_center(name); return (cu - 0.11, cv + 0.11)


def pat_uv(name, along, across):
    j = CR_PAT[name]
    return (j * 0.125 + 0.0625 + max(-1.0, min(1.0, across)) * 0.056, 0.125 + max(-1.0, min(1.0, along)) * 0.112)


bpy.ops.wm.read_factory_settings(use_empty=True)
MAT = bpy.data.materials.new('M_Critters'); MAT.use_nodes = True
_b = next(x for x in MAT.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
_b.inputs['Roughness'].default_value = 0.6
if os.path.exists(ATLAS):
    _t = MAT.node_tree.nodes.new('ShaderNodeTexImage'); _t.image = bpy.data.images.load(ATLAS); _t.interpolation = 'Linear'
    MAT.node_tree.links.new(_t.outputs['Color'], _b.inputs['Base Color'])

PARTS = []


def orient_outward(bm):
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def _uv_all(bm, fn):
    uvl = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for l in f.loops:
            l[uvl].uv = fn(l.vert.co)


def ellipsoid(c, r, color, seg=20, rings=13, rot=None, pattern=None):
    """늘린 공 (r = 반지름 x · y · z, rot = 기울기 행렬). pattern = 무늬 칸 (몸 옆면: 길이 = x, 높이 = z)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    uv0 = color_uv(color)
    _uv_all(bm, (lambda co: pat_uv(pattern, co.x, co.z)) if pattern else (lambda co: uv0))
    M = Matrix.Translation(Vector(c)) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((r[0], r[1], r[2], 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    orient_outward(bm); PARTS.append(bm)
    return bm


def limb(pts, rads, color, seg=14, cap=7, step=0.015):
    """꺾인 선을 따라 한 덩어리 관 + 양 끝 반구 (animal_builder.limb 틀 — 무릎을 두 관으로 겹치면 지그재그 선)."""
    pts = [Vector(p) for p in pts]
    if len(pts) > 2:   # Catmull-Rom으로 매끈하게 (꺾인 마디마다 고리 줄이 보였음 — 백조 · 사슴 목)
        ext = [pts[0] * 2 - pts[1]] + pts + [pts[-1] * 2 - pts[-2]]
        sp, sr = [], []
        for i in range(1, len(ext) - 2):
            p0, p1, p2, p3 = ext[i - 1], ext[i], ext[i + 1], ext[i + 2]
            for k in range(10):
                t = k / 10
                sp.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
                sr.append(rads[i - 1] + (rads[i] - rads[i - 1]) * t)
        sp.append(pts[-1]); sr.append(rads[-1])
        pts, rads = sp, sr
    segs = [(a, b, (b - a).length) for a, b in zip(pts, pts[1:])]
    total = sum(s[2] for s in segs); acc = [0.0]
    for s in segs: acc.append(acc[-1] + s[2])

    def at(t):
        t = min(max(t, 0.0), total)
        i = next(k for k in range(len(segs)) if t <= acc[k + 1] + 1e-9)
        a, b, L = segs[i]; f = (t - acc[i]) / L
        d = (b - a).normalized()
        for k in (i - 1, i + 1):
            if 0 <= k < len(segs):
                dist = abs(t - acc[max(i, k)])
                if dist < 0.05: d = d.lerp((segs[k][1] - segs[k][0]).normalized(), 0.5 * (1 - dist / 0.05)).normalized()
        return a + (b - a) * f, d, rads[i] + (rads[i + 1] - rads[i]) * f
    bm = bmesh.new()
    frame = [None]

    def ring(c, d, r):
        # 단면 방향을 앞 고리에서 이어받음 (기준 축을 바꾸면 관이 수직이 될 때 단면이 돌아 비틀린 줄이 보였음 — 백조 · 사슴 목)
        rf = frame[0] if frame[0] is not None else (Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0)))
        e2 = (rf - d * rf.dot(d)).normalized(); e1 = d.cross(e2)
        frame[0] = e2
        return [bm.verts.new(c + (e1 * math.cos(2 * math.pi * k / seg) + e2 * math.sin(2 * math.pi * k / seg)) * r) for k in range(seg)]
    loops = []
    c0, d0, r0 = at(0.0); loops.append([bm.verts.new(c0 - d0 * r0)])
    for i in range(1, cap): a = math.pi / 2 * (1 - i / cap); loops.append(ring(c0 - d0 * r0 * math.sin(a), d0, r0 * math.cos(a)))
    n = max(2, int(total / step))
    for i in range(n + 1): loops.append(ring(*at(total * i / n)))
    c1, d1, r1 = at(total)
    for i in range(1, cap): a = math.pi / 2 * i / cap; loops.append(ring(c1 + d1 * r1 * math.sin(a), d1, r1 * math.cos(a)))
    loops.append([bm.verts.new(c1 + d1 * r1)])
    for a, b in zip(loops, loops[1:]):
        if len(a) == 1:
            for k in range(seg): bm.faces.new((a[0], b[(k + 1) % seg], b[k]))
        elif len(b) == 1:
            for k in range(seg): bm.faces.new((a[k], a[(k + 1) % seg], b[0]))
        else:
            for k in range(seg): bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    uv0 = color_uv(color); _uv_all(bm, lambda co: uv0)
    orient_outward(bm); PARTS.append(bm)
    return bm


def head(c, R, face, sq=(1.0, 1.0, 1.0), nlat=16, nlon=24):
    """얼굴 공: 앞 (+x) 반구 = 아틀라스 얼굴 칸 정면 투영 (큰 눈 · 반짝이 · 볼 · 작은 미소), 뒤 = 바탕색. sq = 찌그러뜨림 (그림은 그대로)."""
    bm = bmesh.new(); rings = []
    for i in range(nlat + 1):
        th = math.pi * i / nlat; x = -math.cos(th); r = math.sin(th)
        if i in (0, nlat): rings.append([bm.verts.new((x, 0.0, 0.0))]); continue
        rings.append([bm.verts.new((x, r * math.cos(2 * math.pi * j / nlon), r * math.sin(2 * math.pi * j / nlon))) for j in range(nlon)])
    for a, b in zip(rings, rings[1:]):
        for i in range(nlon):
            j = (i + 1) % nlon
            if len(a) == 1: bm.faces.new((a[0], b[j], b[i]))
            elif len(b) == 1: bm.faces.new((a[i], a[j], b[0]))
            else: bm.faces.new((a[i], a[j], b[j], b[i]))
    cu, cv = face_center(face); base = color_uv(face)
    uvl = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        front = f.calc_center_median().x > -0.12
        for l in f.loops:
            co = l.vert.co   # x 앞 · y 옆 · z 위
            l[uvl].uv = (cu - co.y * (120 / 1024) * 1.02, cv + co.z * (120 / 1024) * 1.02) if front else base
    M = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((R * sq[0], R * sq[1], R * sq[2], 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    orient_outward(bm); PARTS.append(bm)
    return bm


def rot(y_deg=0.0, z_deg=0.0, x_deg=0.0):
    return (Matrix.Rotation(math.radians(z_deg), 3, 'Z') @ Matrix.Rotation(math.radians(y_deg), 3, 'Y') @ Matrix.Rotation(math.radians(x_deg), 3, 'X'))


def finish(name):
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    for p in PARTS:
        tmp = bpy.data.meshes.new('tmp'); p.to_mesh(tmp); bm.from_mesh(tmp); bpy.data.meshes.remove(tmp); p.free()
    PARTS.clear()
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = True
    me.materials.append(MAT)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris > 14000:   # 메타볼 몸은 촘촘함 → 1.4만 삼각형 안으로 (모양 · 칸 경계 유지)
        md = ob.modifiers.new("Dec", 'DECIMATE'); md.ratio = 14000 / tris
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier="Dec")
    b = bmesh.new(); b.from_mesh(me)
    print('CRITTER %s tris %d open %d nonmanifold %d' % (name, sum(len(f.verts) - 2 for f in b.faces),
          sum(1 for e in b.edges if e.is_boundary), sum(1 for e in b.edges if not e.is_manifold and not e.is_boundary)))
    b.free()
    return ob


# ---------------- 메타볼 몸 (사용자 2026-11-10 "사슴의 몸 다리 이음새랑 머리 모양 · 토끼의 몸통 수정 — 실제 사진을 보고") ----------------
# 타원체를 겹치면 이음마다 주름 · 막대 다리가 공 아래 꽂힌 모양이 됨 → 메타볼로 몸 · 허벅지 · 다리 · 목 · 머리를 녹여 한 덩어리로.
# 칸 색 (UV)은 면마다 가장 센 조각의 표지로: 'head' (얼굴 칸 정면 투영) · 'base' (바탕색) · 'dark' (발굽) · 'cream' · 'pattern' (아기 사슴 점).
# 사진 참고 (위키미디어 공용): 노루 = 곧은 원통 몸 · 몸에서 이어져 가늘어지는 넓적다리 · 어깨 · 뒤로 꺾인 뒷다리 발목 · 가슴에서 굵게 솟는 목 ·
#   뒤가 넓고 주둥이로 좁아지는 쐐기 머리 · 큰 타원 귀 V자 · 아주 짧은 꼬리 / 산토끼 (앉음) = 등에서 엉덩이까지 둥근 빵 몸 · 허벅지는 몸 실루엣 안 ·
#   땅에 붙은 긴 뒷발 · 짧은 앞다리 · 목 없이 몸 앞 위에 붙은 머리 · 긴 귀 · 흰 솜꼬리.

META = []   # (종류, 중심, 반지름 (x, y, z), 회전 행렬, 표지)


def mball(c, r, label="base"):
    META.append(("BALL", Vector(c), (r, r, r), Matrix.Identity(3), label))


def mell(c, r, label="base", rot_=None):
    META.append(("ELLIPSOID", Vector(c), tuple(r), rot_ or Matrix.Identity(3), label))


def mneg(c, r, rot_=None):
    # 빼는 조각 (음수 메타볼): 녹은 덩어리에서 오목하게 덜어냄 — 토끼 목덜미 (머리와 등이 한 둔덕으로 이어졌음)
    META.append(("NEG", Vector(c), tuple(r), rot_ or Matrix.Identity(3), "neg"))


def mchain(pts, rads, label="base", step=0.45):
    # 꺾인 선을 따라 공을 촘촘히 (가늘어지는 다리 · 목)
    pts = [Vector(p) for p in pts]
    for (a, b), (ra, rb) in zip(zip(pts, pts[1:]), zip(rads, rads[1:])):
        L = (b - a).length
        n = max(2, int(L / (min(ra, rb) * step)))
        for k in range(n + (1 if b is pts[-1] else 0)):
            t = k / n
            mball(a + (b - a) * t, ra + (rb - ra) * t, label)


_CAL = {}


def _calibrate():
    # 메타볼 한 조각이 실제로 보이는 반지름 (반지름 1 · 굳기 2 · 문턱 0.6) — 원하는 크기를 넣으면 맞게 나누려고
    for kind in ("BALL", "ELLIPSOID"):
        mb = bpy.data.metaballs.new("Cal"); mb.resolution = 0.02; mb.render_resolution = 0.02; mb.threshold = 0.6
        ob = bpy.data.objects.new("Cal", mb); bpy.context.scene.collection.objects.link(ob)
        el = mb.elements.new(type=kind); el.co = (0, 0, 0); el.radius = 1.0; el.stiffness = 2.0
        if kind == "ELLIPSOID":
            el.size_x = el.size_y = el.size_z = 1.0
        dg = bpy.context.evaluated_depsgraph_get()
        me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
        _CAL[kind] = max(abs(v.co.x) for v in me.vertices)
        bpy.data.objects.remove(ob); bpy.data.metaballs.remove(mb); bpy.data.meshes.remove(me)
    print("META CAL", _CAL)


def _field(e, p):
    kind, c, r, R, _ = e
    q = R.transposed() @ (p - c)
    d = math.sqrt((q.x / r[0]) ** 2 + (q.y / r[1]) ** 2 + (q.z / r[2]) ** 2)
    return max(0.0, 1.0 - (d / 1.4) ** 2) ** 2


def eye_warp(h, back):
    # 얼굴 그림을 머리 둘레 각으로 돌려 눈 · 볼을 뒤로 (사용자 2026-11-11 "사슴 눈망울 조금만 뒤쪽으로"):
    #  그림의 빈 바탕만 늘려 눈 · 볼을 back°만큼 뒤로 — 눈 · 입 모양은 늘어나지 않음. 빈 곳이 높이마다 달라서:
    #  눈 높이 (z > −0.12) = 두 눈 사이 (0 ~ 8°), 입 높이 (z < −0.16) = 입 (±6.5°) 밖 ~ 볼 앞 (6.5 ~ 20.5°), 그 사이는 다 빈 바탕이라 이어 붙임
    #  (처음 6 ~ 11°만 늘렸더니 눈 안쪽 끝이 그 자리에 걸려 검은 줄로 늘어났음)
    if not back:
        return h.y
    t = min(1.0, max(0.0, (h.z + 0.16) / 0.04))
    a, L = 6.5 * (1 - t), 14.0 + (8.0 - 14.0) * t
    rr = math.hypot(h.x, h.y)
    ph = math.degrees(math.atan2(abs(h.y), h.x))
    if ph <= a:
        return h.y
    pt = a + (ph - a) * L / (L + back) if ph < a + L + back else ph - back
    return math.copysign(rr * math.sin(math.radians(pt)), h.y)


def meta_part(res, head_c=None, head_R=1.0, face="white", base="white", pattern=None, pat_len=1.0, pat_h=1.0, pat_c=(0, 0, 0), eye_back=0.0):
    """META 조각들을 녹인 한 메시 → PARTS. 면 UV = 가장 센 조각 표지."""
    if not _CAL:
        _calibrate()
    mb = bpy.data.metaballs.new("Meta"); mb.resolution = res; mb.render_resolution = res; mb.threshold = 0.6
    ob = bpy.data.objects.new("Meta", mb); bpy.context.scene.collection.objects.link(ob)
    for (kind, c, r, Rm, _) in META:
        el = mb.elements.new(type="ELLIPSOID")
        el.co = c; el.radius = 1.0 / _CAL["ELLIPSOID"]; el.stiffness = 2.0
        el.size_x, el.size_y, el.size_z = r
        el.rotation = Rm.to_quaternion()
        el.use_negative = kind == "NEG"
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    bpy.data.objects.remove(ob); bpy.data.metaballs.remove(mb)
    bm = bmesh.new(); bm.from_mesh(me); bpy.data.meshes.remove(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=res * 0.05)
    uvl = bm.loops.layers.uv.new("UVMap")
    cu, cv = face_center(face) if head_c is not None else (0, 0)
    for f in bm.faces:
        p = f.calc_center_median()
        best = max((e for e in META if e[0] != "NEG"), key=lambda e: _field(e, p))
        lab = best[4]
        if lab == "head":   # 얼굴 그림 원 밖으로 나가는 면 (목 쪽)은 바탕색 — 옆 칸 색이 얼룩으로 비쳤음
            h = (p - Vector(head_c)) / head_R
            if h.y * h.y + h.z * h.z > 0.92 ** 2:
                lab = "base"
        if lab == "pattern":   # 점 무늬는 등 · 옆 위쪽만 (배 쪽은 칸 가장자리가 들쭉날쭉한 흰 띠로 보였음)
            if (p.z - pat_c[2]) / pat_h < -0.25 or abs(p.x - pat_c[0]) / pat_len > 0.85:
                lab = "base"
        for l in f.loops:
            co = l.vert.co
            if lab == "head":
                h = (co - Vector(head_c)) / head_R
                front = (p.x - head_c[0]) > -0.25 * head_R
                l[uvl].uv = (cu - eye_warp(h, eye_back) * (120 / 1024) * 1.02, cv + h.z * (120 / 1024) * 1.02) if front else color_uv(face)
            elif lab == "pattern" and pattern:
                l[uvl].uv = pat_uv(pattern, (co.x - pat_c[0]) / pat_len, (co.z - pat_c[2]) / pat_h)
            elif lab in ("dark", "cream", "pink"):
                l[uvl].uv = color_uv(lab)
            else:
                l[uvl].uv = color_uv(base)
    orient_outward(bm); PARTS.append(bm)
    META.clear()
    return bm


# ---------------- 토끼 (앉음, 산토끼 사진) ----------------

def build_bunny():
    W = "white"
    mell((-0.035, 0, 0.14), (0.175, 0.125, 0.14), rot_=rot(8))                     # 둥근 빵 몸 (엉덩이 쪽이 낮고 넓음)
    mell((0.06, 0, 0.18), (0.095, 0.1, 0.115))                                      # 가슴 (몸 앞 위로 솟음)
    for s in (-1, 1):
        mell((-0.075, s * 0.068, 0.1), (0.12, 0.068, 0.098), rot_=rot(-10))          # 허벅지 (몸 실루엣 안에서 부드럽게 불룩)
        mell((0.025, s * 0.078, 0.026), (0.115, 0.036, 0.027))                       # 땅에 붙은 긴 뒷발 (허벅지에서 앞으로 이어짐)
        mchain([(0.095, s * 0.045, 0.13), (0.12, s * 0.046, 0.06), (0.135, s * 0.046, 0.026)], [0.03, 0.026, 0.025])   # 짧은 앞다리
        mell((0.15, s * 0.046, 0.022), (0.035, 0.026, 0.02))                         # 앞발
    mell((0.115, 0, 0.3), (0.118, 0.105, 0.102), "head")                           # 머리 (목 없이 몸 앞 위에, 2등신으로 큼)
    mell((0.2, 0, 0.275), (0.065, 0.07, 0.058), "head")                            # 둥근 주둥이 · 볼
    mball((-0.2, 0, 0.11), 0.042)                                                   # 솜꼬리
    mneg((-0.025, 0, 0.345), (0.05, 0.1, 0.045))                                 # 목덜미 오목 (사용자 2026-11-11 "토끼 목 근처 등 쪽 조금만 얇게" —
                                                                                    #  애니메이션 토끼처럼 머리 뒤 · 등 사이가 살짝 들어감, 1.5 ~ 2.6 cm)
    meta_part(0.0065, head_c=(0.12, 0, 0.3), head_R=0.115, face="white", base=W)
    ellipsoid((0.26, 0, 0.29), (0.012, 0.016, 0.011), "pink")                      # 코
    for s in (-1, 1):                                                               # 긴 귀 (뒤로 살짝 · 바깥으로 조금) + 안쪽 분홍
        r = rot(-18, 0, s * -9)
        ellipsoid((0.075, s * 0.042, 0.49), (0.034, 0.022, 0.13), W, rot=r)
        ellipsoid((0.09, s * 0.042, 0.495), (0.011, 0.013, 0.1), "pink", rot=r)
    return finish("Critter_Bunny")


# ---------------- 사슴 (노루 사진) ----------------

def build_deer(name, s=1.0, spots=False, head_k=1.0):
    def P(x, y, z): return (x * s, y * s, z * s)
    def S(*r): return tuple(v * s for v in r)
    body = "pattern" if spots else "base"
    mell(P(0.0, 0, 0.66), S(0.33, 0.15, 0.165), body)                              # 몸통 (곧은 원통)
    mell(P(0.2, 0, 0.635), S(0.16, 0.14, 0.185))                                   # 깊은 가슴
    mell(P(-0.21, 0, 0.69), S(0.16, 0.14, 0.15))                                   # 엉덩이
    for sy in (-1, 1):
        mell(P(-0.2, sy * 0.075, 0.54), S(0.115, 0.06, 0.19), rot_=rot(18))          # 넓적다리 (엉덩이에서 앞 아래로 가늘어짐)
        mchain([P(-0.155, sy * 0.08, 0.4), P(-0.245, sy * 0.08, 0.25), P(-0.225, sy * 0.08, 0.07), P(-0.22, sy * 0.08, 0.03)],
               [0.05 * s, 0.034 * s, 0.027 * s, 0.03 * s])                                   # 뒷다리 (뒤로 꺾인 발목)
        mball(P(-0.218, sy * 0.08, 0.03), 0.033 * s, "dark")                          # 뒷발굽
        mell(P(0.2, sy * 0.07, 0.55), S(0.085, 0.055, 0.15), rot_=rot(-8))          # 어깨
        mchain([P(0.215, sy * 0.07, 0.42), P(0.235, sy * 0.07, 0.25), P(0.245, sy * 0.07, 0.07), P(0.25, sy * 0.07, 0.03)],
               [0.045 * s, 0.03 * s, 0.025 * s, 0.028 * s])                                  # 앞다리 (곧게)
        mball(P(0.252, sy * 0.07, 0.03), 0.031 * s, "dark")                          # 앞발굽
    mchain([P(0.28, 0, 0.74), P(0.36, 0, 0.9), P(0.43, 0, 1.03)], [0.095 * s, 0.072 * s, 0.062 * s])   # 가슴에서 굵게 솟는 목
    hk = head_k
    hc = P(0.46, 0, 1.11)
    mell(hc, S(0.14 * hk, 0.12 * hk, 0.12 * hk), "head")                            # 머리 뒤 (넓은 쪽)
    mell(P(0.46 + 0.13 * hk, 0, 1.11 - 0.045 * hk), S(0.095 * hk, 0.065 * hk, 0.062 * hk), "head", rot_=rot(14))   # 주둥이 (앞으로 좁아짐)
    mell(P(-0.37, 0, 0.75), S(0.04, 0.035, 0.055), "cream", rot_=rot(-35))          # 아주 짧은 꼬리
    meta_part(0.011 * s, head_c=hc, head_R=0.13 * s * hk, face="brown", base="brown", pattern="deer" if spots else None,
              pat_len=0.33 * s, pat_h=0.165 * s, pat_c=P(0.0, 0, 0.66), eye_back=16.0)   # 눈 = 주둥이 뿌리에서 머리 가운데 쪽으로 (애니메이션 사슴)
    nx = 0.46 + 0.13 * hk + 0.09 * hk
    ellipsoid(P(nx, 0, 1.11 - 0.06 * hk), S(0.022 * hk, 0.03 * hk, 0.022 * hk), "dark")   # 검은 코
    for sy in (-1, 1):                                                              # 큰 타원 귀 (머리 뒤 위, V자로 바깥 · 위)
        r = rot(-10, 0, sy * -32)
        ellipsoid(P(0.42, sy * 0.125 * hk, 1.11 + 0.15 * hk), S(0.028 * hk, 0.065 * hk, 0.115 * hk), "brown", rot=r)
        ellipsoid(P(0.433, sy * 0.125 * hk, 1.112 + 0.15 * hk), S(0.008 * hk, 0.045 * hk, 0.085 * hk), "cream", rot=r)
    return finish(name)


# ---------------- 백조 ----------------

def build_swan():
    W = "white"
    ellipsoid((0, 0, 0.1), (0.3, 0.17, 0.15), W)                                  # 몸 (물높이 z 0 아래로 조금 잠김)
    ellipsoid((-0.25, 0, 0.18), (0.11, 0.08, 0.06), W, rot=rot(-25))              # 들린 꽁지
    for s in (-1, 1):
        ellipsoid((-0.04, s * 0.115, 0.17), (0.25, 0.048, 0.095), W, rot=rot(-9))   # 접은 날개
    limb([(0.18, 0, 0.16), (0.29, 0, 0.38), (0.22, 0, 0.56), (0.27, 0, 0.66)], [0.055, 0.045, 0.04, 0.038], W)   # S자 목
    head((0.3, 0, 0.7), 0.075, "white")
    ellipsoid((0.395, 0, 0.685), (0.06, 0.024, 0.022), "orange", rot=rot(12))      # 주황 부리
    ellipsoid((0.355, 0, 0.7), (0.022, 0.026, 0.026), "dark")                      # 부리 뿌리 검은 혹
    return finish("Critter_Swan")


objs = [build_bunny(), build_deer("Critter_Deer"), build_deer("Critter_Fawn", 0.72, spots=True, head_k=1.15), build_swan()]

if OUT:
    for x in bpy.context.view_layer.objects: x.select_set(x in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
    print('EXPORTED', OUT)

# 미리보기: 나란히 놓고 옆 · 비스듬히 (EEVEE, 회색 바닥)
for i, ob in enumerate(objs):
    ob.location.x = i * 1.25 - 1.9
sc = bpy.context.scene
for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
    try:
        sc.render.engine = eng; break
    except TypeError:
        pass
sc.render.resolution_x, sc.render.resolution_y = 1400, 700
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.5, 0.55, 0.62, 1)
sc.world = wd
bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=6)
gm = bpy.data.meshes.new('Ground'); bm.to_mesh(gm); bm.free()
g = bpy.data.objects.new('Ground', gm); sc.collection.objects.link(g)
gmat = bpy.data.materials.new('G'); gmat.use_nodes = True
next(x for x in gmat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = (0.35, 0.55, 0.3, 1)
gm.materials.append(gmat)
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.5
lo = bpy.data.objects.new('Sun', ld); lo.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(lo)
cd = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cd); sc.collection.objects.link(cam); sc.camera = cam
os.makedirs(os.path.join(HERE, 'preview'), exist_ok=True)
for tag, loc, tgt in (("side", (0.0, -7.6, 0.7), (0.0, 0, 0.45)), ("front", (4.6, -5.2, 1.8), (0.0, 0, 0.4)),
                      ("bunny", (-1.7, -1.35, 0.32), (-1.9, 0, 0.2)), ("swan", (2.25, -1.9, 0.75), (1.85, 0, 0.35)),
                      ("deer", (0.6, -1.9, 1.15), (-0.2, 0, 0.85)), ("fawn", (1.5, -1.25, 0.6), (0.95, 0, 0.55))):
    cam.location = loc
    d = Vector(tgt) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cd.lens = 50
    sc.render.filepath = os.path.join(HERE, 'preview', 'critter_%s.png' % tag)
    bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'critters.blend'))
