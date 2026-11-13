# 3단계 내부 정원 동물 (2026-11-10, 궤도 도시 — 사용자 "토끼 모양새도 이상해, 뒷다리는 어디간거야"): 바깥 모듈의 작은 덩어리 동물을 키워 쓰지 않고
# 내부 크기 (미터) 전용 모델. 귀여운 2등신 · 매끈한 몸 (토끼 · 사슴 = 메타볼로 녹인 한 덩어리, 백조 = 겹친 타원체 · 꺾인 관) · 얼굴 = 손그림 아틀라스 T_Critters_BaseColor 얼굴 칸 정면 투영 (MODELING 7-10).
#   Critter_Bunny  앉은 토끼 (큰 뒷다리 허벅지 + 땅에 붙은 긴 뒷발 · 앞발 둘 · 긴 귀 · 솜꼬리) 높이 0.5
#   Critter_Deer   사슴 (다리 넷 · 발굽 · 목 · 큰 머리 · 귀 · 짧은 꼬리) 머리 꼭대기 1.2
#   Critter_Fawn   아기 사슴 (흰 점 무늬) 0.75배
#   Critter_Swan   백조 (물에 뜬 몸 · 접은 날개 · S자 목 · 주황 부리, 원점 = 물높이)
#   Critter_Seal · SealPup · Dolphin · Puffin · Turtle · Crab  궤도 방어망 바다 동물 (2026-11-13, 아래 "바다 동물" — `--sea` = 바다 동물만 미리보기)
# 좌표: 미터, 발 · 물높이 z = 0, 앞 = +x. 재질 슬롯 하나 (M_Critters — Unity가 ModuleFxMaterials "Critters"로 바꿈).
# 사용: blender -b --factory-startup -P BlenderWork/InteriorProps/critter_builder.py -- [fbx 경로]
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

HERE = os.path.dirname(os.path.abspath(__file__))
ATLAS = os.path.join(HERE, '..', '..', 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Modules', 'Textures', 'Evo_Critters', 'T_Critters_BaseColor.png')
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SEA_ONLY = '--sea' in argv                     # 바다 동물만 짓고 미리보기 (내보내지 않음)
argv = [a for a in argv if not a.startswith('--')]
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
    # 삼각형으로 나눠 내보냄 — 색 경계를 자른 (bisect) 오각형 · 휜 사각형을 Unity가 "self-intersecting polygon"으로 버렸음 (퍼핀 6장, 체크리스트 9)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], ngon_method='BEAUTY')   # 사각형은 그대로 (나누면 휜 사각형이 작은 뒤집힌 삼각형)
    bm.to_mesh(me); bm.free()
    for poly in me.polygons: poly.use_smooth = True
    me.materials.append(MAT)
    ob = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(ob)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print('CRITTER %s raw tris %d' % (name, tris))
    if tris > 14000:   # 메타볼 몸은 촘촘함 → 1.4만 삼각형 안으로 (모양 · 칸 경계 유지)
        md = ob.modifiers.new("Dec", 'DECIMATE'); md.ratio = 14000 / tris
        bpy.context.view_layer.objects.active = ob
        bpy.ops.object.modifier_apply(modifier="Dec")
    b = bmesh.new(); b.from_mesh(me); b.normal_update()
    fold = 0                                   # 접힌 면 (이웃과 법선이 반대 — 체크리스트 80) 0이어야 함
    for f in b.faces:
        nb = [g.normal for e in f.edges for g in e.link_faces if g is not f]
        if nb and sum(f.normal.dot(m) for m in nb) / len(nb) < 0.0:
            fold += 1
            print("FOLD", name, tuple(round(v, 3) for v in f.calc_center_median()), round(f.calc_area() * 1e6, 2), [round(f.normal.dot(m), 2) for m in nb], len(f.verts))
    print('CRITTER %s tris %d open %d nonmanifold %d folded %d' % (name, sum(len(f.verts) - 2 for f in b.faces),
          sum(1 for e in b.edges if e.is_boundary), sum(1 for e in b.edges if not e.is_manifold and not e.is_boundary), fold))
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
    # 메타볼 면이 몇 장 접혀 나옴 (이웃과 법선이 반대 — 오리 5 · 새끼 6 · 사슴 1장, 부드러운 음영에서 검은 점) → 접힌 면 꼭짓점 · 둘레를 이웃 평균으로 폄
    for _ in range(6):
        bm.normal_update()
        bad = set()
        for f in bm.faces:
            nb = [g.normal for e in f.edges for g in e.link_faces if g is not f]
            if nb and sum(f.normal.dot(m) for m in nb) / len(nb) < 0.0:
                bad.update(f.verts)
        if not bad:
            break
        ring_ = set(bad)
        for v in bad:
            ring_.update(e.other_vert(v) for e in v.link_edges)
        bmesh.ops.smooth_vert(bm, verts=list(ring_), factor=1.0, use_axis_x=True, use_axis_y=True, use_axis_z=True)
    # 펴도 남는 아주 작은 뒤집힌 삼각형 (이웃 모두와 법선이 반대 — 돌고래 옆구리 0.3 mm²)은 가장 짧은 변을 접어 없앰
    bm.normal_update()
    for f in [f for f in bm.faces if all(f.normal.dot(g.normal) < -0.5 for e in f.edges for g in e.link_faces if g is not f)]:
        if f.is_valid:
            bmesh.ops.collapse(bm, edges=[min(f.edges, key=lambda e: e.calc_length())], uvs=False)
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
            elif lab in CR_SW or lab in CR_FACES:      # 색 이름 표지 (발굽 dark · 꼬리 cream · 물범 주둥이 silver …)
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


# ---------------- 오리 가족 (바이오스피어 호수 — 바깥 모듈과 같은 흰 어미 · 노란 새끼, 2026-11-11) ----------------
# 흰 집오리 사진: 물에 반쯤 잠긴 둥근 배 모양 몸 · 앞으로 불룩한 가슴 · 위로 들린 짧은 꽁지 · 짧고 굵은 목 · 둥근 머리 · 넓적한 주황 부리 /
#   새끼 = 공 같은 솜털 몸 · 목 없이 몸만 한 머리 · 조그만 날개. 몸 · 목 · 머리 = 메타볼 한 덩어리, 날개 · 부리 = 겹친 타원체, 얼굴 = 아틀라스 (부리가 입을 덮음).

def build_duck():
    W = "white"
    mell((-0.02, 0, 0.075), (0.175, 0.125, 0.105))                                 # 몸 (물높이 z 0 아래로 3 cm 잠김)
    mell((0.085, 0, 0.1), (0.1, 0.105, 0.1))                                        # 앞으로 불룩한 가슴
    mell((-0.19, 0, 0.14), (0.075, 0.055, 0.034), rot_=rot(35))                     # 위로 들린 꽁지
    mchain([(0.13, 0, 0.17), (0.15, 0, 0.23)], [0.055, 0.055])                      # 짧은 목 (가슴 위로 곧게)
    mell((0.15, 0, 0.3), (0.1, 0.095, 0.095), "head")                               # 큰 둥근 머리 (2등신)
    mneg((0.04, 0, 0.215), (0.055, 0.12, 0.04))                                    # 목덜미 오목 (등에서 머리까지 한 비탈 = 물개처럼 보였음)
    meta_part(0.011, head_c=(0.15, 0, 0.3), head_R=0.097, face="white", base=W, eye_back=12.0)
    for s in (-1, 1):
        ellipsoid((-0.045, s * 0.097, 0.11), (0.115, 0.035, 0.058), W, rot=rot(10))  # 접은 날개 (몸 옆에 붙어 끝이 꽁지 쪽으로 들림 — 크면 꽁지가 둘로 보였음)
    ellipsoid((0.262, 0, 0.27), (0.078, 0.05, 0.021), "orange", rot=rot(8))         # 넓적한 윗부리
    ellipsoid((0.25, 0, 0.255), (0.062, 0.04, 0.014), "orange", rot=rot(14))        # 아랫부리
    return finish("Critter_Duck")


def build_duckling():
    Y = "yellow"
    mell((-0.01, 0, 0.045), (0.085, 0.07, 0.065))                                  # 동글동글 솜털 몸
    mball((-0.085, 0, 0.075), 0.026)                                                # 짧은 꽁지
    mell((0.055, 0, 0.13), (0.066, 0.063, 0.063), "head")                          # 목 없이 몸만 한 머리
    meta_part(0.006, head_c=(0.055, 0, 0.13), head_R=0.064, face="yellow", base=Y, eye_back=10.0)
    for s in (-1, 1):
        ellipsoid((-0.01, s * 0.064, 0.06), (0.045, 0.02, 0.03), Y, rot=rot(15))    # 조그만 날개
    ellipsoid((0.124, 0, 0.12), (0.036, 0.028, 0.011), "orange", rot=rot(6))        # 작은 부리
    ellipsoid((0.118, 0, 0.112), (0.028, 0.022, 0.008), "orange", rot=rot(12))
    return finish("Critter_Duckling")


# ---------------- 꿀벌 (항성 발전망 내부 — 바깥 모듈 꿀벌처럼 귀여운 2등신 · 다리 · 침 없음, 2026-11-11) ----------------
# 둥근 솜털 몸 (노랑 + 짙은 띠 둘 · 짙은 꽁무니) · 몸만 한 둥근 머리 (노란 얼굴 칸) · 끝이 동그란 더듬이 둘.
# 날개 = 따로 물체 Critter_BeeWings (Unity에서 수정 재질 · 파닥임 — 원점 = 몸 가운데, 가로로 줄였다 폈다).

def build_bee():
    Y = "yellow"
    HC, HR_ = (0.105, 0, 0.06), 0.1
    mell((-0.075, 0, 0.0), (0.118, 0.106, 0.1), rot_=rot(-12))                     # 둥근 배 (뒤가 살짝 처짐 — 길면 애벌레 같았음)
    mell(HC, (0.096, 0.1, 0.094), "head")                                           # 몸만 한 둥근 머리 (몸보다 조금 위)
    mneg((0.012, 0, 0.125), (0.034, 0.14, 0.03))                                    # 머리 · 몸 사이 오목 (위 · 아래) — 머리가 몸에 묻혀 한 덩어리 캡슐 같았음
    mneg((0.012, 0, -0.07), (0.034, 0.14, 0.026))
    meta_part(0.008, head_c=HC, head_R=HR_, face="yellow", base=Y, eye_back=8.0)
    bm = PARTS[-1]
    for xc in (-0.05, -0.088, -0.128, -0.166, -0.184):                               # 띠 경계를 곧게 (면 단위로 칠하면 들쭉날쭉)
        geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=(xc, 0, 0), plane_no=(1, 0, 0))
    uvl = bm.loops.layers.uv.active
    for f in bm.faces:                                                              # 짙은 띠 둘 + 꽁무니 (머리 빼고)
        p = f.calc_center_median()
        if (p - Vector(HC)).length < HR_ * 1.05:
            continue
        if -0.088 < p.x < -0.05 or -0.166 < p.x < -0.128 or p.x < -0.184:
            for l in f.loops:
                l[uvl].uv = color_uv("dark")
        else:
            for l in f.loops:
                l[uvl].uv = color_uv(Y)
    for s in (-1, 1):
        limb([(0.11, s * 0.035, 0.14), (0.135, s * 0.055, 0.2), (0.17, s * 0.08, 0.23)], [0.008, 0.007, 0.006], "dark")
        ellipsoid((0.175, s * 0.083, 0.235), (0.019, 0.019, 0.019), "dark")
    return finish("Critter_Bee")


def build_bee_wings():
    for s in (-1, 1):
        ellipsoid((-0.035, s * 0.1, 0.135), (0.09, 0.062, 0.006), "silver", rot=rot(z_deg=-s * 25, x_deg=s * 22))   # 큰 앞날개 (뒤로 비스듬히 들림)
        ellipsoid((-0.085, s * 0.075, 0.11), (0.052, 0.036, 0.005), "silver", rot=rot(z_deg=-s * 42, x_deg=s * 15))  # 작은 뒷날개
    return finish("Critter_BeeWings")


# ---------------- 바다 동물 (궤도 방어망 "등대 곶 해안" 내부, 2026-11-13) ----------------
# 사진 참고 (위키미디어 공용 · 도감): 점박이물범 = 통통한 방추형 몸 · 목 없이 둥근 머리 · 큰 검은 눈 · 볼록한 수염 주머니 · 몸 옆 짧은 앞지느러미 ·
#   꼬리 끝 V자 뒷지느러미 (바위 위에선 머리 · 뒷지느러미를 들고 엎드림) / 하프물범 새끼 = 하얀 솜털 공 같은 몸 / 큰돌고래 = 매끈한 방추형 · 둥근 멜론 이마 ·
#   짧은 부리 · 웃는 입선 · 낫 모양 등지느러미 · 가슴지느러미 · 가로로 넓은 꼬리 (뒤로 젖힘) · 등 짙고 배 · 아래턱 밝음 / 코뿔바다오리 (퍼핀) = 선 몸 · 검은 등 ·
#   검은 머리 모자 · 흰 얼굴 · 흰 배 · 목 검은 띠 · 크고 납작한 세모 부리 (주황 끝 · 회청 뿌리) · 주황 물갈퀴 발 / 푸른바다거북 = 앞이 넓은 물방울 등딱지 ·
#   노 같은 긴 앞지느러미 · 작은 뒷지느러미 · 둥근 머리 / 게 = 옆으로 넓은 등딱지 · 들어 올린 큰 집게 둘 · 옆 다리 셋씩.
# → 귀여운 2등신 (큰 머리), 얼굴 = 아틀라스 얼굴 칸 (물범 · 돌고래 = 회색, 아기 물범 · 퍼핀 = 흰색, 거북 = 초록, 게 = 산호). 물범 점 무늬는 쓰지 않음 (환공포증 — 체크리스트 47).
# 원점: 물범 · 퍼핀 · 게 = 발 · 배 밑 (z 0), 거북 = 물높이 (등딱지 가장자리), 돌고래 = 뛰어오른 몸 가운데 (CritterFx Leap — 모델은 코를 15° 든 뛰는 모습).

def repaint(bm, rule):
    """메타볼 덩어리 칸 색 다시 칠하기: rule(면 가운데, 얼굴 그림 면인지) → 색 이름 (None = 그대로). 얼굴 그림 면 = 모서리 UV가 서로 다른 면."""
    uvl = bm.loops.layers.uv.active
    for f in bm.faces:
        tex = len({(round(l[uvl].uv[0], 5), round(l[uvl].uv[1], 5)) for l in f.loops}) > 1
        c = rule(f.calc_center_median(), tex)
        if c:
            uv = color_uv(c)
            for l in f.loops:
                l[uvl].uv = uv


def cut(bm, co, no):
    # 칸 색 경계를 곧게 (면 단위로 칠하면 들쭉날쭉 — 꿀벌 줄무늬와 같음)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no)


def side(n, no, co):
    return (Vector(co) - Vector(n)).dot(Vector(no))


def pose_all(M):
    # 지은 조각 전체를 한 번에 돌리기 · 줄이기 (돌고래 뛰는 자세 · 게 크기)
    for p in PARTS:
        bmesh.ops.transform(p, matrix=M, verts=p.verts)


def on_skin(bm, rays, off=0.0015):
    """(시작점, 방향) 광선이 메타볼 몸에 닿는 점 + 법선 × off — 입선 · 수염이 몸 면을 따라감 (떠 있거나 묻히지 않게)."""
    tree = BVHTree.FromBMesh(bm)
    out = []
    for o, d in rays:
        hit = tree.ray_cast(Vector(o), Vector(d).normalized())
        if hit[0] is not None:
            out.append(hit[0] + hit[1] * off)
    return out


def build_seal():
    G = "gray"
    HC, HR_ = (0.27, 0, 0.37), 0.14
    mell((-0.02, 0, 0.13), (0.28, 0.17, 0.13))                                     # 통통한 몸 (배 = 바위에 닿음 z 0)
    mell((0.15, 0, 0.16), (0.15, 0.155, 0.15))                                      # 가슴 (든 머리를 받침)
    mell((-0.27, 0, 0.11), (0.16, 0.12, 0.085), rot_=rot(12))                       # 엉덩이 → 꼬리 (끝이 살짝 들림)
    mell((-0.43, 0, 0.13), (0.08, 0.07, 0.05), rot_=rot(18))
    mchain([(0.17, 0, 0.22), (0.24, 0, 0.3)], [0.12, 0.115])                        # 목 없이 이어진 굵은 목
    mell(HC, (0.14, 0.145, 0.135), "head")                                          # 큰 둥근 머리 (2등신)
    # 뒤통수 → 등은 오목 없이 매끈하게 (사용자 2026-11-13 "뒤통수에 몽둥이 맞은 것마냥 움푹" — 토끼처럼 빼는 덩어리로 목덜미를 팠더니
    #  옆에선 안 보이고 위 · 뒤에서 패인 자국으로 보였음. 물범은 목이 없어 머리 · 등이 한 비탈)
    meta_part(0.016, head_c=HC, head_R=HR_, face="gray", base=G, eye_back=10.0)
    pads = []
    for s in (-1, 1):                                                               # 볼록한 수염 주머니 둘 (밝은 회색 ω — 메타볼 칸으로 칠하면 경계가 들쭉날쭉)
        pads.append(ellipsoid((0.38, s * 0.033, 0.305), (0.048, 0.045, 0.038), "silver", rot=rot(z_deg=s * 12)))
    ellipsoid((0.424, 0, 0.341), (0.016, 0.026, 0.014), "dark")                     # 작은 코 (주머니 위 가운데)
    for s, pad in zip((-1, 1), pads):
        for dz, dy in ((0.012, 0.0), (-0.012, -0.012)):                              # 수염 둘씩 (주머니 면에서 바깥 · 뒤로)
            p0 = on_skin(pad, [((0.4, s * 0.3, 0.305 + dz), (0, -s, 0))], -0.004)[0]
            limb([p0, p0 + Vector((0.01, s * (0.045 + dy), dz * 0.8)), p0 + Vector((0.005, s * (0.085 + dy), dz * 2.0))],
                 [0.0028, 0.0022, 0.0016], "white", seg=6, cap=3)
        ellipsoid((0.12, s * 0.165, 0.03), (0.085, 0.034, 0.02), G, rot=rot(z_deg=-s * 28))   # 앞지느러미 (몸 옆에 붙어 뒤 · 바깥으로)
        ellipsoid((-0.52, s * 0.05, 0.165), (0.085, 0.048, 0.014), G, rot=rot(16, -s * 14))   # 뒷지느러미 (V자로 들림)
    return finish("Critter_Seal")


def build_seal_pup():
    W = "white"
    HC, HR_ = (0.165, 0, 0.235), 0.112
    mell((-0.01, 0, 0.1), (0.19, 0.135, 0.1))                                       # 솜털 공 몸
    mell((0.09, 0, 0.12), (0.11, 0.12, 0.11))
    mell((-0.17, 0, 0.085), (0.09, 0.08, 0.065), rot_=rot(12))
    mell((-0.25, 0, 0.095), (0.05, 0.045, 0.035), rot_=rot(18))
    mchain([(0.1, 0, 0.16), (0.15, 0, 0.2)], [0.095, 0.09])
    mell(HC, (0.112, 0.118, 0.108), "head")                                         # 몸만 한 머리
    for s in (-1, 1):
        mell((0.257, s * 0.026, 0.19), (0.034, 0.033, 0.028), "head")               # 수염 주머니 (흰 털 — 얼굴 그림의 미소가 그대로 보임)
    meta_part(0.0108, head_c=HC, head_R=HR_, face="white", base=W, eye_back=8.0)
    skin = PARTS[-1]
    ellipsoid((0.285, 0, 0.216), (0.013, 0.02, 0.011), "dark")                      # 작은 코
    for s in (-1, 1):
        p0 = on_skin(skin, [((0.27, s * 0.3, 0.192), (0, -s, 0))], -0.003)[0]
        limb([p0, p0 + Vector((0.008, s * 0.035, 0.006)), p0 + Vector((0.004, s * 0.065, 0.012))], [0.0022, 0.0018, 0.0013], "silver", seg=6, cap=3)
        ellipsoid((0.07, s * 0.128, 0.024), (0.058, 0.025, 0.015), W, rot=rot(z_deg=-s * 28))
        ellipsoid((-0.3, s * 0.035, 0.115), (0.055, 0.032, 0.01), W, rot=rot(16, -s * 14))
    return finish("Critter_SealPup")


def build_dolphin():
    G = "gray"
    HC, HR_ = (0.27, 0, 0.03), 0.18
    # 몸 = 등뼈를 따라 굵기가 매끈하게 바뀌는 공 줄 (큰 몸 하나 + 꼬리 줄은 이음에서 불룩 · 등이 울퉁불퉁했음)
    mchain([(-0.63, 0, 0.042), (-0.5, 0, 0.03), (-0.36, 0, 0.016), (-0.18, 0, 0.005), (0.0, 0, 0.0), (0.14, 0, 0.008)],
           [0.034, 0.06, 0.1, 0.145, 0.165, 0.165])
    mell(HC, (0.18, 0.168, 0.175), "head")                                          # 몸보다 굵은 둥근 멜론 이마 (2등신)
    mell((0.47, 0, -0.045), (0.09, 0.062, 0.048), rot_=rot(-6))                     # 짧은 부리
    meta_part(0.0176, head_c=HC, head_R=HR_, face="gray", base=G, eye_back=10.0)
    skin = PARTS[-1]
    BELLY_P, BELLY_N = (0, 0, -0.035), (0.03, 0, 1)                                 # 밝은 배 · 아래턱 경계 (꼬리 쪽이 높음)
    cut(skin, BELLY_P, BELLY_N)
    repaint(skin, lambda p, tex: None if tex else ("silver" if side(BELLY_P, BELLY_N, p) < 0 else G))
    side_pts = [(0.415, -0.028), (0.44, -0.042), (0.475, -0.05), (0.51, -0.052), (0.54, -0.051)]
    rays = [((x, 0.4, z), (0, -1, 0)) for (x, z) in side_pts] + [((1.0, 0, -0.05), (-1, 0, 0))] + [((x, -0.4, z), (0, 1, 0)) for (x, z) in reversed(side_pts)]
    limb(on_skin(skin, rays, 0.001), [0.0045] * 11, "dark", seg=8, cap=4)          # 웃는 입선 (부리 옆면을 따라 끝이 위로)
    ellipsoid((-0.08, 0, 0.2), (0.065, 0.02, 0.13), G, rot=rot(-35))                # 낫 모양 등지느러미 (끝이 뒤로)
    for s in (-1, 1):
        ellipsoid((0.16, s * 0.15, -0.095), (0.095, 0.028, 0.036), G, rot=rot(18, -s * 30, s * 35))   # 가슴지느러미 (뒤 · 아래 · 바깥)
        ellipsoid((-0.69, s * 0.1, 0.048), (0.08, 0.13, 0.014), G, rot=rot(z_deg=s * 30))            # 꼬리지느러미 (가로로 넓게, 뒤로 젖힘)
    pose_all(Matrix.Rotation(math.radians(-15), 4, 'Y'))                           # 코를 15° 든 뛰는 모습 (Leap이 +30 → −60° 숙임을 더함)
    return finish("Critter_Dolphin")


def build_puffin():
    HC, HR_ = (0.03, 0, 0.27), 0.094
    mell((-0.005, 0, 0.14), (0.098, 0.092, 0.118), rot_=rot(-8))                   # 선 몸 (달걀)
    mell((0.025, 0, 0.18), (0.07, 0.075, 0.07))                                    # 앞으로 불룩한 가슴
    mell(HC, (0.094, 0.092, 0.09), "head")                                          # 큰 둥근 머리
    mell((-0.095, 0, 0.07), (0.045, 0.03, 0.018), rot_=rot(-35))                    # 짧은 꽁지
    meta_part(0.0078, head_c=HC, head_R=HR_, face="white", base="white", eye_back=14.0)
    skin = PARTS[-1]
    CAP_P, CAP_N = (HC[0], 0, HC[2] + 0.055), (-0.35, 0, 1)                          # 검은 머리 모자 (뒤로 갈수록 낮게)
    BACK_P, BACK_N = (-0.02, 0, 0.1), (1, 0, 0.2)                                   # 몸 = 검은 등 · 흰 가슴
    NAPE_X = HC[0] - 0.035                                                          # 머리 뒤쪽 = 검정
    COLLAR = (0.186, 0.204)                                                         # 목 검은 띠 (흰 얼굴 아래)
    for co, no in ((CAP_P, CAP_N), (BACK_P, BACK_N), ((NAPE_X, 0, 0), (1, 0, 0)), ((0, 0, COLLAR[0]), (0, 0, 1)), ((0, 0, COLLAR[1]), (0, 0, 1))):
        cut(skin, co, no)

    def rule(p, tex):
        if p.z > COLLAR[1]:                                                         # 머리
            if side(CAP_P, CAP_N, p) > 0 or p.x < NAPE_X:
                return "dark"
            return None if tex else "white"
        if p.z > COLLAR[0]:
            return "dark"
        return "dark" if side(BACK_P, BACK_N, p) < 0 else "white"
    repaint(skin, rule)
    # 크고 납작한 세모 부리 (타원 두 개는 주황 콩처럼 보였음): 뿌리 = 얼굴 속, 위 · 아래 가장자리가 끝으로 모이며 끝이 아래로 굽음,
    #  회청 뿌리 → 노란 줄 → 주황 끝
    secs, cols = [], []
    for i in range(15):
        t = i / 14
        secs.append(((0.09 + 0.088 * t, 0, 0.247 - 0.01 * t - 0.026 * t * t), 0.019 * (1 - t) ** 0.55 + 0.004, 0.056 * (1 - t) ** 0.7 + 0.009))
        cols.append("blue" if t < 0.3 else "yellow" if t < 0.37 else "orange")
    loft(secs, cols)
    for s in (-1, 1):
        ellipsoid((-0.035, s * 0.078, 0.15), (0.07, 0.024, 0.078), "dark", rot=rot(-15, 0, s * 8))     # 접은 검은 날개
        limb([(0.0, s * 0.035, 0.06), (0.012, s * 0.038, 0.016)], [0.013, 0.011], "orange", seg=8, cap=4)   # 짧은 다리
        ellipsoid((0.045, s * 0.04, 0.008), (0.048, 0.028, 0.008), "orange", rot=rot(z_deg=-s * 8))    # 물갈퀴 발
    return finish("Critter_Puffin")


def loft(secs, colors, seg=16):
    """가로 단면 타원 (y 반폭 · z 반높이)을 x 방향으로 이은 한 덩어리 (퍼핀 부리): secs = [(중심, 반폭, 반높이)], colors = 단면마다 그 뒤 띠 색. 양 끝 = 부채꼴 마개."""
    bm = bmesh.new()
    rings = []
    for (c, hw, hh) in secs:
        c = Vector(c)
        rings.append([bm.verts.new(c + Vector((0, hw * math.cos(2 * math.pi * k / seg), hh * math.sin(2 * math.pi * k / seg)))) for k in range(seg)])
    faces = []
    for i, (a, b) in enumerate(zip(rings, rings[1:])):
        for k in range(seg):
            faces.append((bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k])), colors[i]))
    c0 = bm.verts.new(Vector(secs[0][0]) - Vector((0.004, 0, 0)))
    c1 = bm.verts.new(Vector(secs[-1][0]) + Vector((0.003, 0, 0)))
    for k in range(seg):
        faces.append((bm.faces.new((rings[0][(k + 1) % seg], rings[0][k], c0)), colors[0]))
        faces.append((bm.faces.new((rings[-1][k], rings[-1][(k + 1) % seg], c1)), colors[-1]))
    uvl = bm.loops.layers.uv.new("UVMap")
    for f, col in faces:
        uv = color_uv(col)
        for l in f.loops:
            l[uvl].uv = uv
    orient_outward(bm)
    PARTS.append(bm)
    return bm


def turtle_shell(c, ra, rb, top, bot, seg=48, rings=24):
    """등딱지: 앞이 조금 넓은 물방울 돔 (위 = 아틀라스 등딱지 무늬 (큰 판 · 낮은 대비)를 위에서 투영, 아래 = 밝은 배딱지)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    uvl = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        up = f.calc_center_median().z > -0.02
        for l in f.loops:
            co = l.vert.co
            l[uvl].uv = (0.125 + co.y * 0.112, 0.125 + co.x * 0.112) if up else color_uv("cream")
    for v in bm.verts:
        x, y, z = v.co
        v.co = Vector((c[0] + x * ra, c[1] + y * rb * (1.0 + 0.1 * x), c[2] + (z * top if z > 0 else z * bot)))
    orient_outward(bm)
    PARTS.append(bm)
    return bm


def build_turtle():
    GR = "green"
    HC, HR_ = (0.36, 0, 0.055), 0.088
    mell((0.0, 0, -0.005), (0.24, 0.18, 0.04))                                     # 등딱지 속 몸 (지느러미 · 목 뿌리를 이음)
    mchain([(0.18, 0, 0.0), (0.29, 0, 0.035)], [0.06, 0.058])                       # 짧은 목
    mell(HC, (0.088, 0.085, 0.078), "head")                                         # 큰 둥근 머리 (물 위로)
    for s in (-1, 1):
        mell((0.065, s * 0.285, -0.015), (0.15, 0.048, 0.015), rot_=rot(z_deg=-s * 52))   # 노 같은 긴 앞지느러미 (뿌리 = 등딱지 앞 옆 밑, 끝 = 뒤 바깥)
        mell((-0.25, s * 0.13, -0.02), (0.075, 0.042, 0.013), rot_=rot(z_deg=s * 35))    # 작은 뒷지느러미
    mball((-0.31, 0, -0.012), 0.022)                                                # 짧은 꼬리
    meta_part(0.0118, head_c=HC, head_R=HR_, face="green", base=GR, eye_back=10.0)
    turtle_shell((0.0, 0, 0.0), 0.3, 0.235, 0.13, 0.06)
    return finish("Critter_Turtle")


def build_crab():
    C = "coral"
    HC, HR_ = (0.0, 0, 0.088), 0.115
    mell(HC, (0.095, 0.145, 0.07), "head")                                          # 옆으로 넓은 둥근 등딱지 = 얼굴 (2등신 — 몸이 곧 머리)
    meta_part(0.008, head_c=HC, head_R=HR_, face="coral", base=C)
    for s in (-1, 1):
        limb([(0.04, s * 0.11, 0.08), (0.1, s * 0.16, 0.1), (0.14, s * 0.165, 0.13)], [0.02, 0.017, 0.016], C, seg=10, cap=5)   # 팔
        ellipsoid((0.17, s * 0.165, 0.15), (0.055, 0.04, 0.045), "red", rot=rot(-20))    # 통통한 집게 (들어 올림)
        ellipsoid((0.222, s * 0.165, 0.172), (0.04, 0.02, 0.016), "red", rot=rot(-28))   # 위 집게발
        ellipsoid((0.215, s * 0.165, 0.128), (0.034, 0.018, 0.013), "red", rot=rot(5))   # 아래 집게발 (V자로 벌어짐)
        for k, x0 in enumerate((0.035, -0.015, -0.06)):                              # 옆 다리 셋 (짧고 굵게, 끝이 뾰족 — 길고 가늘면 거미처럼 보였음)
            limb([(x0, s * 0.11, 0.075), (x0 - 0.015 * k, s * 0.17, 0.1), (x0 - 0.02 - 0.025 * k, s * 0.195, 0.007)], [0.02, 0.016, 0.007], C, seg=8, cap=4)
    pose_all(Matrix.Scale(0.6, 4))                                                  # 정원 게 크기 (폭 0.3 m)
    return finish("Critter_Crab")


SEA_BUILDERS = (build_seal, build_seal_pup, build_dolphin, build_puffin, build_turtle, build_crab)


def setup_preview(ground_rgb):
    sc = bpy.context.scene
    for o_ in [o for o in bpy.data.objects if o.name.split('.')[0] in ('Ground', 'Sun', 'Cam')]:   # 앞 미리보기 바닥 · 해 · 카메라
        bpy.data.objects.remove(o_)
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            sc.render.engine = eng; break
        except TypeError:
            pass
    wd = bpy.data.worlds.new('W'); wd.use_nodes = True
    next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.5, 0.55, 0.62, 1)
    sc.world = wd
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=12)
    gm = bpy.data.meshes.new('Ground'); bm.to_mesh(gm); bm.free()
    g = bpy.data.objects.new('Ground', gm); sc.collection.objects.link(g)
    gmat = bpy.data.materials.new('G'); gmat.use_nodes = True
    next(x for x in gmat.node_tree.nodes if x.type == 'BSDF_PRINCIPLED').inputs['Base Color'].default_value = ground_rgb + (1,)
    gm.materials.append(gmat)
    ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.5
    lo = bpy.data.objects.new('Sun', ld); lo.rotation_euler = (math.radians(50), 0, math.radians(30)); sc.collection.objects.link(lo)
    cd = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cd); sc.collection.objects.link(cam); sc.camera = cam
    os.makedirs(os.path.join(HERE, 'preview'), exist_ok=True)
    return sc, cam, cd, g


def shoot(sc, cam, cd, name, loc, tgt, lens=50):
    cam.location = loc
    cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cd.lens = lens
    sc.render.filepath = os.path.join(HERE, 'preview', 'critter_%s.png' % name)
    bpy.ops.render.render(write_still=True)


def sea_preview(sea, hide=()):
    """바다 동물 모델 시트: 나란히 (옆 · 비스듬히) + 동물마다 비스듬히 앞 · 정면 · 옆 (모래 바닥)."""
    for o_ in hide:
        o_.hide_render = True
    sc, cam, cd, g = setup_preview((0.78, 0.7, 0.52))
    sc.render.resolution_x, sc.render.resolution_y = 1400, 900
    # 물범 · 아기 물범 · 돌고래 (뛰는 몸 가운데라 띄움) · 퍼핀 · 거북 (원점 = 물높이라 배딱지만큼 띄움) · 게
    lay = [((0.0, 0, 0), (0.0, 0, 0.25), 1.1), ((1.25, 0, 0), (1.25, 0, 0.17), 0.7), ((2.95, 0, 0.32), (2.95, 0, 0.35), 1.5),
           ((4.35, 0, 0), (4.35, 0, 0.18), 0.45), ((5.45, 0, 0.065), (5.45, 0, 0.1), 0.95), ((6.45, 0, 0), (6.45, 0, 0.07), 0.38)]
    for o_, (at, _, _) in zip(sea, lay):
        o_.location = at
    shoot(sc, cam, cd, "sea_side", (3.0, -5.6, 0.55), (3.0, 0, 0.2), 24)
    shoot(sc, cam, cd, "sea_front", (8.0, -4.6, 2.0), (3.4, 0, 0.15), 32)
    for o_, (_, c, s) in zip(sea, lay):
        tag = o_.name.replace("Critter_", "").lower()
        c = Vector(c)
        shoot(sc, cam, cd, tag + "_q", c + Vector((1.25, -1.05, 0.45)) * s * 1.35, c)
        shoot(sc, cam, cd, tag + "_face", c + Vector((1.6, -0.3, 0.25)) * s * 1.35, c)
        shoot(sc, cam, cd, tag + "_side", c + Vector((0.05, -1.7, 0.25)) * s * 1.35, c)
        shoot(sc, cam, cd, tag + "_back", c + Vector((-1.3, 0.9, 0.6)) * s * 1.35, c)
        shoot(sc, cam, cd, tag + "_top", c + Vector((-0.8, -0.7, 1.2)) * s * 1.35, c)   # 위 · 뒤에서 (뒤통수 · 등 이음 — 옆에선 안 보이는 패임)


if SEA_ONLY:
    sea_preview([f() for f in SEA_BUILDERS])
else:
    objs = [build_bunny(), build_deer("Critter_Deer"), build_deer("Critter_Fawn", 0.72, spots=True, head_k=1.15), build_swan(),
            build_duck(), build_duckling(), build_bee(), build_bee_wings()]
    sea = [f() for f in SEA_BUILDERS]

    if OUT:
        for x in bpy.context.view_layer.objects: x.select_set(x in objs or x in sea)
        bpy.context.view_layer.objects.active = objs[0]
        bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
        print('EXPORTED', OUT)

    # 미리보기: 나란히 놓고 옆 · 비스듬히 (EEVEE, 회색 바닥) — 바다 동물은 숨기고 정원 동물만, 그다음 바다 동물 시트
    for o_ in sea:
        o_.hide_render = True
    for i, ob in enumerate(objs[:4]):
        ob.location.x = i * 1.25 - 1.9
    objs[4].location.x = 3.1                                   # 오리 가족: 어미 뒤에 새끼 셋 (Unity 줄 간격)
    objs[5].location.x = 2.65
    dups = []
    for k, x in enumerate((2.35, 2.05)):
        dup = bpy.data.objects.new("Duckling%d" % k, objs[5].data); dup.location = (x, 0.04 * (-1) ** k, 0); bpy.context.scene.collection.objects.link(dup)
        dups.append(dup)
    for o_ in objs[6:8]:
        o_.location = (4.2, 0, 0.35)                           # 꿀벌 (날개 = 같은 원점)
    WM = bpy.data.materials.new('Wing'); WM.use_nodes = True
    _wb = next(x for x in WM.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    _wb.inputs['Base Color'].default_value = (0.85, 0.95, 1.0, 1); _wb.inputs['Alpha'].default_value = 0.45; _wb.inputs['Roughness'].default_value = 0.05
    try:
        WM.surface_render_method = 'BLENDED'
    except Exception:
        WM.blend_method = 'BLEND'
    objs[7].data.materials[0] = WM
    sc, cam, cd, ground = setup_preview((0.35, 0.55, 0.3))
    sc.render.resolution_x, sc.render.resolution_y = 1400, 700
    for tag, loc, tgt in (("side", (0.6, -9.2, 0.7), (0.6, 0, 0.45)), ("front", (5.6, -5.6, 1.8), (0.6, 0, 0.4)),
                          ("bunny", (-1.7, -1.35, 0.32), (-1.9, 0, 0.2)), ("swan", (2.25, -1.9, 0.75), (1.85, 0, 0.35)),
                          ("deer", (0.6, -1.9, 1.15), (-0.2, 0, 0.85)), ("fawn", (1.5, -1.25, 0.6), (0.95, 0, 0.55)),
                          ("duck", (3.75, -0.95, 0.45), (3.12, 0, 0.18)), ("duck_side", (3.05, -1.25, 0.2), (3.05, 0, 0.17)),
                          ("duckling", (2.95, -0.5, 0.2), (2.65, 0, 0.08)), ("family", (3.6, -1.9, 0.75), (2.65, 0, 0.1)),
                          ("bee", (4.75, -0.6, 0.55), (4.22, 0, 0.38)), ("bee_side", (4.2, -0.95, 0.42), (4.2, 0, 0.38)), ("bee_back", (3.75, 0.55, 0.62), (4.2, 0, 0.38))):
        shoot(sc, cam, cd, tag, loc, tgt)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'critters.blend'))
    for o_ in objs + dups + [ground]:
        o_.hide_render = True
    for o_ in sea:
        o_.hide_render = False
    sea_preview(sea)
