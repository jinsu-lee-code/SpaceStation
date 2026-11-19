# 진화 단계별 연결 통로 (2026-11-17, 사용자 "진화 단계별 연결 통로" — 범위 = 바깥 + 내부 튜브, 단계가 다르면 높은 단계 모양).
# 바깥 통로 (Unity StationConnectors): 수평 = 통로 (길이 방향 = Z, 단위 길이 1 — 늘려도 단면 그대로) + 양끝 칼라 + 불빛 띠,
#   수직 = 기둥 (단위 길이 1) + 양끝 받침판 + 불빛 띠, 새로 = 고리 (정해진 간격마다 코드가 놓음 — 늘리면 찌그러지므로 통로에 굽지 않음).
# 단계 (모듈 테마, 메모 module-stage-themes · evolved-module-look):
#   0 기본 = 지금 팔각 여압 통로 (SM_Connectors.fbx 그대로)
#   1 세라믹 캡슐 = 매끈한 흰 원통 + 옆 짙은 유리 띠 + 둥근 칼라 빛 이음매 / 흰 기둥 + 유리 빛 고리
#   2 미드나잇 놀이 궤도 = 남색 아래 몸 + 유리 윗덮개 (안 불빛 레일) + 따뜻한 빛 고리 / 남색 기둥 + 나선 빛 띠
#   3 진주 · 금 유리 회랑 = 진주 바닥 · 금 난간 + 유리 둥근 지붕 + 금 아치 갈비 / 진주 기둥 + 금 머리 · 받침
#   4 성운 별빛 관 = 남보라 유리관 (드문 별빛) + 은 겹고리 / 은 망원경 칼라 / 남보라 유리 기둥 + 은 테
# 좌표 (부품): 미터 아닌 칸 (1 = 칸 하나), 로컬 x 오른쪽 · y 위 · z 길이 (Unity와 같은 뜻 — 내보낼 때 축 바꿈).
# 사용: blender -b --factory-startup -P BlenderWork/Connectors/connector_builder.py -- --sheet   (바깥 통로 시안 시트)
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, '..', '..'))
FONT = os.path.join(ROOT, 'SpaceStation', 'Assets', '_Project', 'Art', 'Fonts', 'Maplestory-Bold.ttf')
OLD_FBX = os.path.join(ROOT, 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Modules', 'SM_Connectors.fbx')
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
SHEET = '--sheet' in argv
PREVIEW = os.path.join(HERE, 'preview')

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene

# ---------------- 재질 ----------------
MATS = {}


def mat(name, rgb, rough=0.4, metal=0.0, emit=None, emit_k=0.0, glass=False, alpha=1.0):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = rgb + (1,)
    b.inputs['Roughness'].default_value = rough
    b.inputs['Metallic'].default_value = metal
    if emit is not None:
        b.inputs['Emission Color'].default_value = emit + (1,)
        b.inputs['Emission Strength'].default_value = emit_k
    if glass:
        b.inputs['Transmission Weight'].default_value = 0.9
        b.inputs['IOR'].default_value = 1.2
    if alpha < 1.0:
        b.inputs['Alpha'].default_value = alpha
        try:
            m.surface_render_method = 'BLENDED'
        except AttributeError:
            m.blend_method = 'BLEND'
    MATS[name] = m
    return m


def hexrgb(h):
    h = h.lstrip('#')
    c = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    return tuple(x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


# ---------------- 도형 ----------------
def new_obj(name, bm, mats):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for m in mats:
        me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me); SC.collection.objects.link(ob)
    return ob


def lathe(name, prof, m, seg=40, closed=False, a0=0.0, a1=2 * math.pi, mi=None):
    """(반지름, z) 단면을 z축으로 돌림. closed = 단면이 고리 (마지막 → 처음). mi = 단면 변마다 재질 번호."""
    bm = bmesh.new()
    full = abs(a1 - a0 - 2 * math.pi) < 1e-6
    n = seg if full else seg + 1
    rings = []
    for i in range(n):
        a = a0 + (a1 - a0) * i / seg
        ca, sa = math.cos(a), math.sin(a)
        rings.append([bm.verts.new((r * ca, r * sa, z)) for r, z in prof])
    k = len(prof)
    for i in range(seg):
        A, B = rings[i], rings[(i + 1) % n]
        for j in range(k if closed else k - 1):
            j2 = (j + 1) % k
            try:
                f = bm.faces.new((A[j], B[j], B[j2], A[j2]))
                f.material_index = (mi[j] if mi else 0)
            except ValueError:
                pass
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    mats = m if isinstance(m, list) else [m]
    return new_obj(name, bm, mats)


def sweep(name, sec, m, z0=-0.5, z1=0.5, closed=True, mi=None):
    """(x, y) 단면을 z0 → z1로 밂 (단면 일정 — 길이로 늘려도 모양 그대로)."""
    bm = bmesh.new()
    A = [bm.verts.new((x, y, z0)) for x, y in sec]
    B = [bm.verts.new((x, y, z1)) for x, y in sec]
    k = len(sec)
    for j in range(k if closed else k - 1):
        j2 = (j + 1) % k
        f = bm.faces.new((A[j], A[j2], B[j2], B[j]))
        f.material_index = (mi[j] if mi else 0)
    mats = m if isinstance(m, list) else [m]
    return new_obj(name, bm, mats)


def circle(r, n=32, a0=0.0, a1=2 * math.pi, cx=0.0, cy=0.0):
    full = abs(a1 - a0 - 2 * math.pi) < 1e-6
    cnt = n if full else n + 1
    return [(cx + r * math.cos(a0 + (a1 - a0) * i / n), cy + r * math.sin(a0 + (a1 - a0) * i / n)) for i in range(cnt)]


def box(name, size, m, at=(0, 0, 0), bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0] + at[0], v.co.y * size[1] + at[1], v.co.z * size[2] + at[2]))
    ob = new_obj(name, bm, [m] if not isinstance(m, list) else m)
    if bevel > 0:
        md = ob.modifiers.new('B', 'BEVEL'); md.width = bevel; md.segments = 3
    return ob


def rounded_rect(w, h, r, n=6):
    """가운데 원점 둥근 사각형 단면 (x, y)."""
    pts = []
    for cx, cy, a in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for i in range(n + 1):
            t = math.radians(a + 90 * i / n)
            pts.append((cx + r * math.cos(t), cy + r * math.sin(t)))
    return pts


# ---------------- 단계별 바깥 부품 (로컬: x 오른쪽 · y 위 · z 길이) ----------------
# 반환: {"tube": [obj..], "collar": [..], "ring": [..] 또는 None, "strip": [..] (단위 길이), "strut": [..], "plate": [..], "vring": [..] 또는 None}
# 칼라 · 받침판 = z 0 이 모듈 표면, +z 쪽 (통로 안쪽)으로 자람. 고리 = z 0 가운데.
RING_STEP = {1: 0.30, 2: 0.24, 3: 0.22, 4: 0.26}


def parts_stage1():
    W = mat('S1_Ceramic', hexrgb('#D3E1F8'), 0.55)
    G = mat('S1_Glass', hexrgb('#2A4656'), 0.06, 0.4)
    L = mat('S1_Light', hexrgb('#BFF6FF'), 0.3, emit=hexrgb('#7FE8FF'), emit_k=6.0)
    R = 0.088
    tube = [sweep('S1_Tube', circle(R, 32), W)]
    # 옆 짙은 유리 띠 (양옆, 바깥 창 — 좁게: 흰 몸이 주인공) — 표면 바로 위 얇은 판
    for s in (1, -1):
        a = 0 if s > 0 else math.pi
        arc = circle(R + 0.0012, 6, a - 0.2, a + 0.2) + list(reversed(circle(R - 0.004, 6, a - 0.2, a + 0.2)))
        tube.append(sweep('S1_Band', arc, G))
    strip = [sweep('S1_Strip', [(-0.006, R + 0.001), (0.006, R + 0.001), (0.006, R + 0.005), (-0.006, R + 0.005)], L)]
    # 둥근 칼라 (모듈 쪽이 넓고 통로 쪽으로 매끈하게 줄어듦) + 빛 이음매 고리
    prof = [(R - 0.002, 0.055), (R + 0.004, 0.05), (0.104, 0.038), (0.118, 0.02), (0.124, 0.0), (0.124, -0.02), (R - 0.01, -0.02)]
    collar = [lathe('S1_Collar', prof, W, 40, closed=True),
              lathe('S1_Seam', [(0.1255, 0.004), (0.1255, 0.012), (0.1225, 0.012), (0.1225, 0.004)], L, 40, closed=True)]
    ring = [lathe('S1_Ring', [(R, -0.009), (R + 0.006, -0.006), (R + 0.006, 0.006), (R, 0.009)], W, 40)]
    # 수직: 흰 기둥 + 가운데 유리 빛 고리, 둥근 받침판
    RS = 0.062
    strut = [sweep('S1_Strut', circle(RS, 28), W)]
    vring = [lathe('S1_VRing', [(RS + 0.004, -0.018), (RS + 0.004, 0.018)], G, 28),
             lathe('S1_VRingL', [(RS + 0.005, -0.004), (RS + 0.005, 0.004)], L, 28)]
    plate = [lathe('S1_Plate', [(RS - 0.01, 0.045), (RS + 0.004, 0.04), (0.1, 0.022), (0.118, 0.008), (0.12, -0.012), (RS - 0.01, -0.012)], W, 40, closed=True)]
    vstrip = []
    return dict(tube=tube, collar=collar, ring=ring, strip=strip, strut=strut, plate=plate, vring=vring, vstrip=vstrip)



def parts_stage2():
    """2단계 에어록 현관 포드: 남색 캡슐 (조금 굵게) + 정원 쪽 에어록 고리 (빛 고리 · 상태등) + 옆 둥근 창 (크게 · 띄엄띄엄 — 고리 자리)."""
    N = mat('S2_Navy', hexrgb('#2D3778'), 0.5, 0.15)
    T = mat('S2_Trim', hexrgb('#9EA6D8'), 0.3, 0.5)
    Gw = mat('S2_Port', hexrgb('#FFE7B8'), 0.1, emit=hexrgb('#FFC46A'), emit_k=2.5)
    W = mat('S2_Warm', hexrgb('#FFE2A8'), 0.3, emit=hexrgb('#FFC46A'), emit_k=7.0)
    R = 0.1
    tube = [sweep('S2_Hull', circle(R, 32), N)]
    for s in (1, -1):
        tube.append(sweep('S2_Trim', circle(0.0045, 8, cx=s * (R + 0.001), cy=-0.03), T))
    strip = [sweep('S2_Strip', [(-0.006, R + 0.001), (0.006, R + 0.001), (0.006, R + 0.005), (-0.006, R + 0.005)], W)]
    # 칼라 = 에어록 문 고리: 두꺼운 남색 테 + 연보라 테 + 따뜻한 빛 고리, 위에 작은 상태등 상자
    collar = [lathe('S2_Lock', [(R - 0.004, 0.07), (0.112, 0.068), (0.128, 0.05), (0.14, 0.02), (0.142, -0.02), (R - 0.01, -0.02)], N, 40, closed=True),
              lathe('S2_LockT', [(0.143, -0.012), (0.143, 0.004), (0.139, 0.004), (0.139, -0.012)], T, 40, closed=True),
              lathe('S2_LockL', [(0.126, 0.052), (0.13, 0.056), (0.116, 0.07), (0.112, 0.066)], W, 40, closed=True),
              box('S2_LockLamp', (0.05, 0.016, 0.02), W, at=(0, 0.15, 0.025))]
    # 고리 자리 = 옆 둥근 창 한 쌍 (현관 안 불빛)
    ring = []
    for s in (1, -1):
        ring.append(lathe('S2_PortRim', [(0.026, -0.004), (0.034, -0.004), (0.034, 0.006), (0.026, 0.006)], T, 24, closed=True))
        ring.append(lathe('S2_PortGlass', [(0.0, 0.0), (0.027, 0.0)], Gw, 24))
    # 둥근 창 둘을 몸 양옆 (x = ±R)으로 — 창 축이 x를 보게
    rot = Matrix.Rotation(math.radians(90), 4, 'Y')
    for i, ob in enumerate(ring):
        s = 1 if i < 2 else -1
        ob.data.transform(Matrix.Translation((s * (R - 0.001), 0, 0)) @ (rot if s > 0 else Matrix.Rotation(math.radians(-90), 4, 'Y')))
    RS = 0.06
    strut = [sweep('S2_Strut', circle(RS, 28), N)]
    vring = []
    bm = bmesh.new(); prev = None
    for i in range(49):
        t = i / 48; a = t * 2 * math.pi; z = (t - 0.5) * 0.09
        c, s_ = math.cos(a), math.sin(a)
        v0 = bm.verts.new(((RS + 0.003) * c, (RS + 0.003) * s_, z - 0.006)); v1 = bm.verts.new(((RS + 0.003) * c, (RS + 0.003) * s_, z + 0.006))
        if prev:
            bm.faces.new((prev[0], v0, v1, prev[1]))
        prev = (v0, v1)
    vring.append(new_obj('S2_Spiral', bm, [W]))
    plate = [lathe('S2_Plate', [(RS - 0.008, 0.04), (0.105, 0.03), (0.125, 0.01), (0.126, -0.012), (RS - 0.008, -0.012)], N, 40, closed=True),
             lathe('S2_PlateT', [(0.1265, -0.006), (0.1265, 0.006), (0.123, 0.006), (0.123, -0.006)], T, 40, closed=True)]
    return dict(tube=tube, collar=collar, ring=ring, strip=strip, strut=strut, plate=plate, vring=vring, vstrip=[])


DECK_Y = -0.16          # 바깥 칸 중심 아래 = 내부 바닥 높이 (F = 중심 − 1.3 m ÷ 8)
DECK_W = 0.36           # 내부 걷는 폭 ~2.9 m ÷ 8


def parts_deck(st):
    """3 · 4단계 다리 데크 (통로 없음 — 받침끼리 이어진 다리): 데크 판 + 옆 테 + 난간 (늘림 부분), 기둥 · 정원등 (간격마다), 받침에 붙는 넓은 착지판 (칼라 자리)."""
    if st == 3:
        D = mat('S3_Pearl', hexrgb('#E2E7F3'), 0.45, 0.1)
        E = mat('S3_Gold', hexrgb('#D9AE4E'), 0.25, 1.0)
        L = mat('S3_Light', hexrgb('#FFF3D6'), 0.3, emit=hexrgb('#FFE7B0'), emit_k=5.0)
    else:
        D = mat('S4_Deck', hexrgb('#4B3FA0'), 0.3, 0.2)
        E = mat('S4_Silver', hexrgb('#CDD3DE'), 0.2, 1.0)
        L = mat('S4_Light', hexrgb('#E8E2FF'), 0.3, emit=hexrgb('#C9B8FF'), emit_k=6.0)
    y0, hw = DECK_Y, DECK_W / 2
    rail_h = 0.13                                           # 1.05 m ÷ 8
    tube = [sweep('D%d_Slab' % st, [(-hw, y0 - 0.03), (hw, y0 - 0.03), (hw, y0), (-hw, y0)], D),
            sweep('D%d_Under' % st, [(-hw * 0.6, y0 - 0.055), (hw * 0.6, y0 - 0.055), (hw * 0.8, y0 - 0.03), (-hw * 0.8, y0 - 0.03)], D)]
    for s in (1, -1):
        tube.append(sweep('D%d_Edge' % st, [(s * hw, y0 - 0.032), (s * (hw + 0.008), y0 - 0.032), (s * (hw + 0.008), y0 + 0.006), (s * hw, y0 + 0.006)], E))
        tube.append(sweep('D%d_Rail' % st, circle(0.0045, 10, cx=s * (hw - 0.006), cy=y0 + rail_h), E))
    strip = [sweep('D%d_Glow' % st, [(-0.012, y0 + 0.001), (0.012, y0 + 0.001), (0.012, y0 + 0.003), (-0.012, y0 + 0.003)], L)]
    # 착지판 = 받침 옆면에 붙는 넓은 반원 판 (데크보다 넓게 퍼짐)
    land = []
    bm = bmesh.new()
    prof =[(-(hw + 0.07), 0.0), (-(hw + 0.07), 0.03)] + [((hw + 0.07) * math.cos(math.pi - math.pi * i / 16), 0.03 + 0.06 * math.sin(math.pi * i / 16)) for i in range(17)] + [((hw + 0.07), 0.0)]
    top = [bm.verts.new((x, y0 + 0.004, z - 0.02)) for x, z in prof]
    bot = [bm.verts.new((x, y0 - 0.034, z - 0.02)) for x, z in prof]
    bm.faces.new(list(reversed(top))); bm.faces.new(bot)
    k = len(prof)
    for j in range(k):
        bm.faces.new((top[j], top[(j + 1) % k], bot[(j + 1) % k], bot[j]))
    land.append(new_obj('D%d_Land' % st, bm, [D]))
    land.append(sweep('D%d_LandEdge' % st, [(-(hw + 0.07), y0 - 0.034), (hw + 0.07, y0 - 0.034), (hw + 0.07, y0 - 0.026), (-(hw + 0.07), y0 - 0.026)], E, -0.02, 0.0))
    # 간격마다: 난간 기둥 둘 + 한쪽 정원등 (3단계 = 금 등 · 4단계 = 은 별 등)
    ring = []
    for s in (1, -1):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.004, radius2=0.004, depth=rail_h)
        for v in bm.verts:
            v.co = Vector((v.co.x + s * (hw - 0.006), v.co.z + y0 + rail_h / 2, v.co.y))
        ring.append(new_obj('D%d_Post' % st, bm, [E]))
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.006, radius2=0.005, depth=0.11)
    for v in bm.verts:
        v.co = Vector((v.co.x + hw * 0.55, v.co.z + y0 + 0.055, v.co.y))
    ring.append(new_obj('D%d_LampPost' % st, bm, [E]))
    bm = bmesh.new()
    if st == 3:
        bmesh.ops.create_uvsphere(bm, u_segments=14, v_segments=8, radius=0.014)
    else:
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.016)
    for v in bm.verts:
        v.co += Vector((hw * 0.55, y0 + 0.12, 0))
    ring.append(new_obj('D%d_Lamp' % st, bm, [L]))
    # 수직 (데크 아님 — 위아래는 기둥 그대로)
    RS = 0.058
    if st == 3:
        strut = [sweep('S3_Strut', circle(RS, 28), D)]
        vring = [lathe('S3_VBand', [(RS + 0.001, -0.007), (RS + 0.006, -0.004), (RS + 0.006, 0.004), (RS + 0.001, 0.007)], E, 28)]
        plate = [lathe('S3_Plate', [(RS - 0.006, 0.05), (RS + 0.004, 0.045), (0.074, 0.03), (0.09, 0.022), (0.098, 0.012), (0.128, 0.004), (0.13, -0.014), (RS - 0.006, -0.014)], E, 40, closed=True)]
    else:
        I = mat('S4_Nebula', hexrgb('#5A48B8'), 0.08, 0.2, emit=hexrgb('#6B4FD0'), emit_k=0.6, glass=True, alpha=0.8)
        Dk = mat('S4_Dark', hexrgb('#2A2352'), 0.4, 0.3)
        strut = [sweep('S4_Strut', circle(RS, 28), I), sweep('S4_SCore', circle(RS * 0.5, 16), Dk)]
        vring = [lathe('S4_VRing', [(RS, -0.012), (RS + 0.007, -0.01), (RS + 0.007, 0.01), (RS, 0.012)], E, 28)]
        bm = bmesh.new(); pts = []
        for i in range(8):
            a = i * math.pi / 4; r = 0.135 if i % 2 == 0 else 0.085
            pts.append((r * math.cos(a), r * math.sin(a)))
        top = [bm.verts.new((x, y, 0.012)) for x, y in pts]; bot = [bm.verts.new((x, y, -0.012)) for x, y in pts]
        bm.faces.new(top); bm.faces.new(list(reversed(bot)))
        for j in range(8):
            bm.faces.new((bot[j], bot[(j + 1) % 8], top[(j + 1) % 8], top[j]))
        plate = [new_obj('S4_Plate', bm, [E])]
    return dict(tube=tube, collar=land, ring=ring, strip=strip, strut=strut, plate=plate, vring=vring, vstrip=[])


def parts_stage3():
    return parts_deck(3)


def parts_stage4():
    return parts_deck(4)


def parts_stage0():
    """지금 기본 통로: SM_Connectors.fbx를 그대로 가져와 칸 단위 · 로컬 축으로 맞춤 (없으면 비슷한 팔각)."""
    H = mat('S0_Hull', hexrgb('#B8B8BF'), 0.45, 0.4)
    L = mat('S0_Light', hexrgb('#BFF6FF'), 0.3, emit=hexrgb('#7FE8FF'), emit_k=5.0)
    got = {}
    if os.path.exists(OLD_FBX):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=OLD_FBX)
        for ob in set(bpy.data.objects) - before:
            if ob.type != 'MESH':
                bpy.data.objects.remove(ob); continue
            ob.parent = None
            # 가져온 축 → 로컬 (x 오른쪽 · y 위 · z 길이): 메시에 굽고 길이 축을 찾아 z로
            me = ob.data; me.transform(ob.matrix_world); ob.matrix_world = Matrix.Identity(4)
            ext = [max(v.co[i] for v in me.vertices) - min(v.co[i] for v in me.vertices) for i in range(3)]
            name = ob.name.split('.')[0]
            long_axis = ext.index(max(ext)) if name in ('CN_Tube', 'CN_Strut') else ext.index(min(ext))
            if long_axis == 1:
                me.transform(Matrix.Rotation(math.radians(90), 4, 'X'))
            elif long_axis == 0:
                me.transform(Matrix.Rotation(math.radians(-90), 4, 'Y'))
            me.materials.clear(); me.materials.append(H)
            got[name] = ob
    R = 0.095
    tube = [got.get('CN_Tube') or sweep('S0_Tube', circle(R, 8, math.pi / 8, 2 * math.pi + math.pi / 8)[:8], H)]
    collar = [got.get('CN_Collar') or lathe('S0_Collar', [(R, 0.05), (0.12, 0.05), (0.12, -0.03), (R, -0.03)], H, 8, closed=True)]
    strut = [got.get('CN_Strut') or sweep('S0_Strut', circle(0.075, 12), H)]
    plate = [got.get('CN_Plate') or lathe('S0_Plate', [(0.075, 0.04), (0.12, 0.02), (0.12, -0.03), (0.075, -0.03)], H, 16, closed=True)]
    strip = [box('S0_Strip', (0.022, 0.008, 1.0), L, at=(0, 0.088, 0))]
    vstrip = [box('S0_VStrip', (0.008, 0.03, 1.0), L, at=(s * 0.081, 0, 0)) for s in (-1, 1)]
    return dict(tube=tube, collar=collar, ring=None, strip=strip, strut=strut, plate=plate, vring=None, vstrip=vstrip)


# ---------------- 조립 (Unity StationConnectors.Build와 같은 규칙) ----------------
def copy_into(parts, coll, matrix):
    out = []
    for p in parts:
        ob = p.copy(); ob.data = p.data
        coll.objects.link(ob)
        ob.matrix_world = matrix @ p.matrix_world
        out.append(ob)
    return out


def assemble(P, length, frame, coll, vertical=False, step=None):
    """frame = 로컬 (x · y · z길이) → 월드 행렬. 통로 = 길이만큼 z 늘림, 칼라 = ±length/2 (B는 뒤집음), 고리 = step 간격."""
    half = length * 0.5
    body = P['strut'] if vertical else P['tube']
    ends = P['plate'] if vertical else P['collar']
    rings = P['vring'] if vertical else P['ring']
    strips = P['vstrip'] if vertical else P['strip']
    S = Matrix.Diagonal((1, 1, length, 1))
    copy_into(body, coll, frame @ S)
    copy_into(strips, coll, frame @ S)
    copy_into(ends, coll, frame @ Matrix.Translation((0, 0, -half)))
    copy_into(ends, coll, frame @ Matrix.Translation((0, 0, half)) @ Matrix.Rotation(math.pi, 4, 'Y'))
    if rings:
        if vertical:
            copy_into(rings, coll, frame)                     # 짧은 기둥 = 가운데 한 벌
        elif step:
            free = length - 2 * 0.07                          # 칼라 안쪽 남은 길이
            n = int(free / step)
            for i in range(n):
                z = -free / 2 + (i + 0.5) * free / n
                copy_into(rings, coll, frame @ Matrix.Translation((0, 0, z)))


def stub(name, size, at, body, band, coll):
    """모듈 몸 대역 (그 단계 몸 색 상자 + 모듈 색 띠)."""
    b = box(name, size, body, at=at, bevel=0.04)
    for c in list(b.users_collection):
        c.objects.unlink(b)
    coll.objects.link(b)
    t = box(name + '_Band', (size[0] * 1.004, size[1] * 1.004, 0.05), band, at=(at[0], at[1], at[2] + size[2] * 0.22))
    for c in list(t.users_collection):
        c.objects.unlink(t)
    coll.objects.link(t)


STAGES = [
    (0, '기본 · 팔각 여압 통로', parts_stage0, 0.20, '#9DA2AE', '#6E7686'),
    (1, '1단계 · 세라믹 캡슐', parts_stage1, 0.50, '#F0F2F6', '#58C9E8'),
    (2, '2단계 · 에어록 현관 포드', parts_stage2, 0.54, '#22284F', '#FFC46A'),
    (3, '3단계 · 받침을 잇는 다리 데크', parts_stage3, 0.86, '#F2ECE1', '#D9AE4E'),
    (4, '4단계 · 성운 다리 데크', parts_stage4, 0.74, '#2C255E', '#CDD3DE'),
]


def label(text, at, size, coll, rgb=(0.95, 0.95, 0.97)):
    cu = bpy.data.curves.new('T', 'FONT'); cu.body = text; cu.size = size; cu.align_x = 'CENTER'
    if os.path.exists(FONT):
        cu.font = bpy.data.fonts.load(FONT, check_existing=True)
    ob = bpy.data.objects.new('Label', cu); coll.objects.link(ob)
    ob.location = at; ob.rotation_euler = (math.radians(90), 0, 0)
    ob.data.materials.append(mat('LabelMat', rgb, 0.5, emit=rgb, emit_k=1.5))
    ob.visible_shadow = False
    return ob


def setup_render():
    for eng in ('BLENDER_EEVEE_NEXT', 'BLENDER_EEVEE'):
        try:
            SC.render.engine = eng; break
        except TypeError:
            pass
    wd = bpy.data.worlds.new('W'); wd.use_nodes = True
    next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.05, 0.06, 0.1, 1)
    next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Strength'].default_value = 0.6
    SC.world = wd
    for nm, rot, e in (('Key', (40, 0, 30), 3.0), ('Rim', (60, 0, 200), 1.5)):
        ld = bpy.data.lights.new(nm, 'SUN'); ld.energy = e
        lo = bpy.data.objects.new(nm, ld); lo.rotation_euler = tuple(math.radians(a) for a in rot); SC.collection.objects.link(lo)
    # 카메라 옆 채움 빛 (모듈 사이 그늘 속 수직 기둥도 보이게)
    fd = bpy.data.lights.new('CamFill', 'AREA'); fd.energy = 60; fd.size = 1.5
    fo = bpy.data.objects.new('CamFill', fd); SC.collection.objects.link(fo)
    try:
        SC.eevee.use_bloom = True
    except AttributeError:
        pass
    SC.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in SC.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
    cd = bpy.data.cameras.new('Cam'); cam = bpy.data.objects.new('Cam', cd); SC.collection.objects.link(cam); SC.camera = cam
    os.makedirs(PREVIEW, exist_ok=True)
    return cam, cd


def shoot(cam, cd, name, loc, tgt, lens=50):
    cam.location = loc
    cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    cd.lens = lens
    fo = bpy.data.objects['CamFill']
    fo.location = Vector(loc) + Vector((-0.4, 0, 0.5))
    fo.rotation_euler = (Vector(tgt) - fo.location).to_track_quat('-Z', 'Y').to_euler()
    SC.render.filepath = os.path.join(PREVIEW, name + '.png')
    bpy.ops.render.render(write_still=True)


def sheet():
    """단계마다 한 장: 위 = 수평 통로 (그 단계 흔한 길이), 아래 = 수직 기둥 (0.16), 모듈 몸 대역 사이. 이름표."""
    cam, cd = setup_render()
    SC.render.resolution_x, SC.render.resolution_y = 800, 620
    shots = []
    src = []
    for st, name, fn, length, body_hex, band_hex in STAGES:
        coll = bpy.data.collections.new('Stage%d' % st); SC.collection.children.link(coll)
        P = fn()
        for k, v in P.items():
            for ob in (v or []):
                if ob.name in SC.collection.objects:
                    SC.collection.objects.unlink(ob)
                src.append(ob)
        body = mat('Body%d' % st, hexrgb(body_hex), 0.35, 0.1)
        band = mat('Band%d' % st, hexrgb(band_hex), 0.3, 0.6 if st >= 3 else 0.1, emit=hexrgb(band_hex) if st in (1, 2) else None, emit_k=1.2)
        X = st * 6.0
        # 수평: 로컬 z → 월드 x, y → 월드 z, x → 월드 y
        Fh = Matrix(((0, 0, 1, X), (1, 0, 0, 0), (0, 1, 0, 0.55), (0, 0, 0, 1)))
        assemble(P, length, Fh, coll, False, RING_STEP.get(st))
        stub('A%d' % st, (0.7, 0.8, 0.7), (X - length / 2 - 0.35, 0, 0.55), body, band, coll)
        stub('B%d' % st, (0.7, 0.8, 0.7), (X + length / 2 + 0.35, 0, 0.55), body, band, coll)
        # 수직: 로컬 z → 월드 z, y → 월드 x
        vl = 0.16
        Fv = Matrix(((0, 1, 0, X), (-1, 0, 0, 0), (0, 0, 1, -0.55), (0, 0, 0, 1)))
        assemble(P, vl, Fv, coll, True)
        stub('C%d' % st, (0.8, 0.8, 0.5), (X, 0, -0.55 + vl / 2 + 0.25), body, band, coll)
        stub('D%d' % st, (0.8, 0.8, 0.5), (X, 0, -0.55 - vl / 2 - 0.25), body, band, coll)
        for c in bpy.data.collections:
            if c.name.startswith('Stage'):
                c.hide_render = c is not coll
        # 수평 통로: 비스듬히 앞 위
        shoot(cam, cd, 'cn_h%d' % st, (X + 0.3, -1.05, 0.86), (X + 0.02, 0, 0.6), 42)
        # 수직 기둥: 앞 조금 아래에서 틈을 봄
        shoot(cam, cd, 'cn_v%d' % st, (X + 0.32, -0.92, -0.47), (X, 0, -0.55), 45)
        shots.append(st)
    # 섞임 예: 3단계 다리 → 1단계 모듈 (다리가 닿는 면 = 에어록 문 — 기존 문 대신, 사용자 2026-11-17)
    coll = bpy.data.collections.new('Stage5'); SC.collection.children.link(coll)
    P = parts_deck(3)
    for v in P.values():
        for ob in (v or []):
            if ob.name in SC.collection.objects:
                SC.collection.objects.unlink(ob)
    X, length = 30.0, 0.6
    Fh = Matrix(((0, 0, 1, X), (1, 0, 0, 0), (0, 1, 0, 0.55), (0, 0, 0, 1)))
    assemble(P, length, Fh, coll, False, RING_STEP[3])
    stub('A5', (0.7, 0.8, 0.7), (X - length / 2 - 0.35, 0, 0.55), MATS['Body3'], MATS['Band3'], coll)
    stub('B5', (0.7, 0.8, 0.7), (X + length / 2 + 0.35, 0, 0.55), MATS['Body1'], MATS['Band1'], coll)
    lock_c = (X + length / 2 - 0.004, 0, 0.55 + DECK_Y + 0.15)
    for nm, prof, m in (('Lock5', [(0.085, -0.012), (0.115, -0.012), (0.115, 0.012), (0.085, 0.012)], MATS['S1_Ceramic']),
                        ('LockL5', [(0.08, -0.004), (0.086, -0.004), (0.086, 0.004), (0.08, 0.004)], MATS['S1_Light']),
                        ('LockLeaf5', [(0.0, -0.002), (0.082, -0.002), (0.082, 0.002), (0.0, 0.002)], mat('S1_Leaf', hexrgb('#C9D6EE'), 0.35, 0.3))):
        ob = lathe(nm, prof, m, 40, closed=True)
        for c in list(ob.users_collection):
            c.objects.unlink(ob)
        coll.objects.link(ob)
        ob.matrix_world = Matrix.Translation(lock_c) @ Matrix.Rotation(math.radians(90), 4, 'Y')
    for c in bpy.data.collections:
        if c.name.startswith('Stage'):
            c.hide_render = c is not coll
    shoot(cam, cd, 'cn_h5', (X + 0.05, -1.05, 0.8), (X + 0.08, 0, 0.5), 42)
    # 이름표 띠 (글자만, 단계마다 한 장 — 시트에서 위에 붙임)
    for c in bpy.data.collections:
        if c.name.startswith('Stage'):
            c.hide_render = True
    lc = bpy.data.collections.new('Labels'); SC.collection.children.link(lc)
    SC.render.resolution_x, SC.render.resolution_y = 800, 90
    cd.type = 'ORTHO'; cd.ortho_scale = 4.0
    for st, name, *_ in STAGES + [(5, '섞임 · 3단계 다리 → 1단계 에어록 문')]:
        lab = label(name, (st * 6.0, 30.0, -0.06), 0.22, lc)
        cam.location = (st * 6.0, 29.0, 0.0); cam.rotation_euler = (math.radians(90), 0, 0)
        SC.render.filepath = os.path.join(PREVIEW, 'cn_l%d.png' % st)
        bpy.ops.render.render(write_still=True)
        lab.hide_render = True
    print('SHEET', shots)


# ---------------- 내부 시안 (미터, 바닥 z 0, 길이 = 월드 +y) ----------------
SHEET2 = '--sheet2' in argv
TUBE_L = 6.0
TUBE_FRAME = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # 로컬 z(길이) → 월드 y, 로컬 y(위) → 월드 z


def star_world():
    """어두운 남색 하늘 + 드문 별 (크게 · 띄엄띄엄)."""
    wd = bpy.data.worlds.new('Sky'); wd.use_nodes = True
    nt = wd.node_tree; bg = next(n for n in nt.nodes if n.type == 'BACKGROUND')
    vor = nt.nodes.new('ShaderNodeTexVoronoi'); vor.inputs['Scale'].default_value = 90
    ramp = nt.nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position = 0.0; ramp.color_ramp.elements[0].color = (1, 1, 1, 1)
    ramp.color_ramp.elements[1].position = 0.035; ramp.color_ramp.elements[1].color = (0, 0, 0, 1)
    mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'
    mix.inputs['A'].default_value = (0.012, 0.014, 0.035, 1); mix.inputs['B'].default_value = (2.5, 2.5, 3.0, 1)
    nt.links.new(vor.outputs['Distance'], ramp.inputs['Fac'])
    nt.links.new(ramp.outputs['Color'], mix.inputs['Factor'])
    nt.links.new(mix.outputs['Result'], bg.inputs['Color'])
    SC.world = wd


def rr_path(w, h, r, ymin, n=8):
    """둥근 사각 단면의 열린 길 (오른쪽 아래 ymin → 위 → 왼쪽 아래 ymin), 바닥 변 없음."""
    pts = [(w / 2, ymin)]
    for i in range(n + 1):
        t = math.radians(90 * i / n)
        pts.append((w / 2 - r + r * math.cos(t), h - r + r * math.sin(t)))
    for i in range(n + 1):
        t = math.radians(90 + 90 * i / n)
        pts.append((-w / 2 + r + r * math.cos(t), h - r + r * math.sin(t)))
    pts.append((-w / 2, ymin))
    return pts


def open_band(name, a, b, m, z, w):
    """열린 띠 (갈비 · 문틀): 바깥 a · 안 b 같은 점 수, 끝은 열림."""
    bm = bmesh.new(); k = len(a)
    r0 = ([bm.verts.new((x, y, z - w / 2)) for x, y in a], [bm.verts.new((x, y, z - w / 2)) for x, y in b])
    r1 = ([bm.verts.new((x, y, z + w / 2)) for x, y in a], [bm.verts.new((x, y, z + w / 2)) for x, y in b])
    for j in range(k - 1):
        bm.faces.new((r0[0][j], r0[0][j + 1], r1[0][j + 1], r1[0][j]))
        bm.faces.new((r0[1][j], r1[1][j], r1[1][j + 1], r0[1][j + 1]))
        bm.faces.new((r0[0][j], r0[1][j], r0[1][j + 1], r0[0][j + 1]))
        bm.faces.new((r1[0][j], r1[0][j + 1], r1[1][j + 1], r1[1][j]))
    return new_obj(name, bm, [m])


def framed(objs, prefixes):
    for ob in objs:
        if ob.name.startswith(prefixes):
            ob.matrix_world = TUBE_FRAME
    return objs


def airlock_door(name, y, facing, at_x=0.0, frame_m=None, light_m=None, leaf_m=None):
    """에어록 문 (걷는 틀 반폭 1.25 · 높이 2.8 안 문간 1.4 × 2.4): 두꺼운 둥근 사각 테 + 빛 테 + 가운데 갈라지는 문짝 둘 + 위 상태 띠."""
    objs = []
    o = rr_path(2.3, 3.0, 0.7, 0.0); i = rr_path(1.5, 2.5, 0.45, 0.0)
    fr = open_band(name + '_Frame', o, i, frame_m, 0.0, 0.3)
    li = open_band(name + '_Glow', rr_path(1.5, 2.5, 0.45, 0.0), rr_path(1.44, 2.47, 0.42, 0.0), light_m, -0.16, 0.03)
    objs += [fr, li]
    for s in (1, -1):
        leaf = box(name + '_Leaf', (0.7, 0.08, 2.4), leaf_m, at=(s * 0.36 + at_x, y, 1.2))
        objs.append(leaf)
    lamp = box(name + '_Lamp', (0.9, 0.05, 0.08), light_m, at=(at_x, y - 0.17 * facing, 2.72))
    objs.append(lamp)
    # 테 · 빛 테는 로컬 → 월드 (z → y, facing 쪽으로 앞)
    M = Matrix.Translation((at_x, y, 0)) @ TUBE_FRAME @ (Matrix.Rotation(math.pi, 4, 'Y') if facing < 0 else Matrix.Identity(4))
    fr.matrix_world = M; li.matrix_world = M
    return objs


def interior_stage1():
    """1단계 세라믹 통로 (닫힌 관): 흰 둥근 사각 (x 1.25 에서 천장 2.99) + 양옆 파노라마 창 띠 + 흰 갈비 + 바닥 빛줄."""
    W = mat('I1_White', hexrgb('#EEF1F5'), 0.25)
    F = mat('I1_Floor', hexrgb('#C9CED6'), 0.5)
    G = mat('I1_Window', hexrgb('#0B1626'), 0.04, 0.3)
    L = mat('I1_Light', hexrgb('#BFF6FF'), 0.3, emit=hexrgb('#7FE8FF'), emit_k=4.0)
    w, h, r = 3.0, 3.1, 0.6
    objs = [sweep('I1_Shell', list(reversed(rr_path(w, h, r, 0.0))), W, 0, TUBE_L, closed=False)]
    for s in (1, -1):
        objs.append(sweep('I1_Win', [(s * (w / 2 - 0.004), 1.05), (s * (w / 2 - 0.004), 1.95)], G, 0.4, TUBE_L - 0.4, closed=False))
        objs.append(sweep('I1_WinRim', [(s * (w / 2 - 0.01), 1.0), (s * (w / 2 - 0.01), 1.05), (s * (w / 2 - 0.016), 1.05), (s * (w / 2 - 0.016), 1.0)], L, 0.4, TUBE_L - 0.4))
    objs.append(box('I1_Floor', (2.9, TUBE_L, 0.04), F, at=(0, TUBE_L / 2, 0.02)))
    for s in (1, -1):
        objs.append(box('I1_FloorLine', (0.05, TUBE_L - 0.6, 0.012), L, at=(s * 1.1, TUBE_L / 2, 0.046)))
    for z in (1.5, 3.0, 4.5):
        objs.append(open_band('I1_Rib', rr_path(w - 0.01, h - 0.005, r, 0.06), rr_path(w - 0.2, h - 0.1, r - 0.1, 0.06), W, z, 0.12))
    pl = bpy.data.lights.new('IntLamp', 'POINT'); pl.energy = 120
    po = bpy.data.objects.new('IntLamp', pl); po.location = (0, 3.0, 2.4); SC.collection.objects.link(po); objs.append(po)
    return framed(objs, ('I1_Shell', 'I1_Win', 'I1_Rib'))


def interior_vestibule():
    """2단계 우주복 현관 (짧은 에어록 방, 길이 4 m): 닫힌 쪽 문 (뒤) → 왼쪽 우주복 보관함 셋 · 오른쪽 벤치 · 거울 → 정원 쪽 에어록 문 (앞)."""
    N = mat('V_Navy', hexrgb('#232A55'), 0.4, 0.2)
    F = mat('V_Floor', hexrgb('#171C3A'), 0.5)
    T = mat('V_Trim', hexrgb('#A9B0E2'), 0.3, 0.5)
    Wm = mat('V_Warm', hexrgb('#FFE2A8'), 0.3, emit=hexrgb('#FFC46A'), emit_k=5.0)
    Suit = mat('V_Suit', hexrgb('#E9ECF2'), 0.45)
    Visor = mat('V_Visor', hexrgb('#E0A04A'), 0.08, 0.8)
    Mir = mat('V_Mirror', hexrgb('#C8D2E8'), 0.03, 1.0)
    Gl = mat('V_Glass', hexrgb('#9EB0FF'), 0.03, glass=True, alpha=0.15)
    Lk = mat('V_Leaf', hexrgb('#3A4270'), 0.35, 0.4)
    Lc = mat('V_Cyan', hexrgb('#BFF6FF'), 0.3, emit=hexrgb('#7FE8FF'), emit_k=5.0)
    Lg = 4.0
    w, h, r = 3.2, 3.2, 0.7
    objs = [sweep('V_Shell', list(reversed(rr_path(w, h, r, 0.0))), N, 0, Lg, closed=False)]
    objs.append(box('V_Floor', (3.1, Lg, 0.04), F, at=(0, Lg / 2, 0.02)))
    objs.append(box('V_Runner', (0.08, Lg - 0.6, 0.012), Wm, at=(0, Lg / 2, 0.046)))
    objs.append(box('V_Ceil', (1.2, Lg - 0.6, 0.03), Wm, at=(0, Lg / 2, 3.17)))
    # 왼쪽: 우주복 보관함 셋 (유리 앞판 · 안에 흰 우주복 + 금빛 투구 창)
    for k in range(3):
        y = 0.9 + k * 1.1
        # 앞이 트인 칸 (뒤판 · 옆판 · 위아래 판) — 안의 우주복이 보이게
        objs.append(box('V_LockerBack', (0.04, 0.9, 2.3), N, at=(-1.56, y, 1.2)))
        for dy in (-0.45, 0.45):
            objs.append(box('V_LockerSide', (0.5, 0.04, 2.3), N, at=(-1.33, y + dy, 1.2)))
        for zz in (0.06, 2.34):
            objs.append(box('V_LockerShelf', (0.5, 0.9, 0.04), N, at=(-1.33, y, zz)))
        objs.append(box('V_LockerRim', (0.03, 0.94, 0.04), T, at=(-1.07, y, 2.36)))
        objs.append(box('V_LockerGlass', (0.01, 0.86, 2.24), Gl, at=(-1.07, y, 1.2)))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.17)
        for v in bm.verts:
            v.co = Vector((v.co.x * 1.6 - 1.32, v.co.y * 1.2 + y, v.co.z * 2.6 + 1.25))
        objs.append(new_obj('V_SuitBody', bm, [Suit]))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.16)
        for v in bm.verts:
            v.co = Vector((v.co.x - 1.3, v.co.y + y, v.co.z + 1.85))
        objs.append(new_obj('V_Helmet', bm, [Suit]))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.1)
        for v in bm.verts:
            v.co = Vector((v.co.x * 0.4 - 1.17, v.co.y * 1.0 + y, v.co.z * 0.8 + 1.85))
        objs.append(new_obj('V_Visor', bm, [Visor]))
        objs.append(box('V_LockerLamp', (0.03, 0.6, 0.03), Wm, at=(-1.06, y, 2.4)))
    # 오른쪽: 벤치 + 거울 + 빛 테
    objs.append(box('V_Bench', (0.45, 2.2, 0.08), T, at=(1.3, 2.0, 0.48)))
    for yy in (1.1, 2.9):
        objs.append(box('V_BenchLeg', (0.35, 0.08, 0.44), N, at=(1.3, yy, 0.24)))
    objs.append(box('V_Mirror', (0.02, 1.6, 1.4), Mir, at=(1.58, 2.0, 1.55)))
    objs.append(box('V_MirrorRim', (0.03, 1.7, 0.04), Wm, at=(1.57, 2.0, 2.28)))
    # 앞 = 정원 쪽 에어록 문 (cyan 빛 테 · 위 상태 띠), 뒤 = 닫힌 쪽 문
    objs += airlock_door('V_Lock', Lg, 1, frame_m=T, light_m=Lc, leaf_m=Lk)
    pl = bpy.data.lights.new('VLamp', 'POINT'); pl.energy = 90; pl.color = (1.0, 0.85, 0.65)
    po = bpy.data.objects.new('VLamp', pl); po.location = (0, 2.0, 2.6); SC.collection.objects.link(po); objs.append(po)
    return framed(objs, ('V_Shell',))


def garden_ground(name, x0, x1, y0, y1, m):
    return box(name, (x1 - x0, y1 - y0, 0.2), m, at=((x0 + x1) / 2, (y0 + y1) / 2, -0.1))


def tree(name, at, leaf_m, trunk_m, k=1.0):
    objs = []
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.12 * k, radius2=0.08 * k, depth=1.6 * k)
    for v in bm.verts:
        v.co += Vector((at[0], at[1], 0.8 * k))
    objs.append(new_obj(name + '_Trunk', bm, [trunk_m]))
    for dx, dy, dz, rr in ((0, 0, 2.0, 0.9), (0.5, 0.2, 1.7, 0.6), (-0.45, -0.25, 1.8, 0.6)):
        bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=rr * k)
        for v in bm.verts:
            v.co += Vector((at[0] + dx * k, at[1] + dy * k, dz * k))
        objs.append(new_obj(name + '_Leaf', bm, [leaf_m]))
    return objs


def bridge_scene(mixed=False):
    """3단계 바닥 연결: 정원 A (앞) 끝에서 진주 · 금 다리가 칸 경계를 건너 정원 B로. mixed = 다리 끝이 1단계 흰 벽의 에어록 문."""
    P = mat('B_Pearl', hexrgb('#F4EFE6'), 0.2, 0.15)
    Au = mat('B_Gold', hexrgb('#D9AE4E'), 0.25, 1.0)
    Gr = mat('B_Grass', hexrgb('#5E9A55'), 0.8)
    Gr2 = mat('B_Grass2', hexrgb('#6FAE62'), 0.8)
    Tr = mat('B_Trunk', hexrgb('#8A6A4A'), 0.7)
    L = mat('B_Light', hexrgb('#FFF3D6'), 0.3, emit=hexrgb('#FFE7B0'), emit_k=5.0)
    objs = [garden_ground('B_GroundA', -4, 4, -3, 2.0, Gr)]
    # 정원 끝 (받침 가장자리 = 금 테) → 다리 (폭 2.9, 길이 4) → 다음 받침
    objs.append(box('B_EdgeA', (8, 0.12, 0.25), Au, at=(0, 2.06, -0.06)))
    objs.append(box('B_Deck', (2.9, 4.0, 0.12), P, at=(0, 4.0, -0.06)))
    for s in (1, -1):
        objs.append(box('B_DeckEdge', (0.06, 4.0, 0.16), Au, at=(s * 1.48, 4.0, -0.04)))
        objs.append(sweep('B_Rail', circle(0.035, 10), Au, 2.0, 6.0))
        objs[-1].matrix_world = Matrix.Translation((s * 1.42, 0, 1.05)) @ Matrix.Rotation(math.radians(-90), 4, 'X')
        for k in range(5):
            objs.append(box('B_Post', (0.05, 0.05, 1.05), Au, at=(s * 1.42, 2.2 + k * 0.9, 0.52)))
    for k in range(2):
        objs.append(box('B_LampPost', (0.07, 0.07, 1.6), Au, at=(1.25, 2.6 + k * 2.6, 0.8)))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.13)
        for v in bm.verts:
            v.co += Vector((1.25, 2.6 + k * 2.6, 1.68))
        objs.append(new_obj('B_Lamp', bm, [L]))
    objs.append(box('B_DeckGlow', (0.06, 3.8, 0.012), L, at=(0, 4.0, 0.006)))
    if not mixed:
        objs.append(box('B_EdgeB', (8, 0.12, 0.25), Au, at=(0, 5.94, -0.06)))
        objs.append(garden_ground('B_GroundB', -4, 4, 6.0, 14.0, Gr2))
        objs += tree('B_TreeB', (-1.6, 9.0), Gr2, Tr, 1.2) + tree('B_TreeB2', (2.0, 11.0), Gr, Tr, 1.0)
        objs += tree('B_TreeA', (-2.8, -0.5), Gr2, Tr, 1.0)
    else:
        # 낮은 단계 (1단계) 흰 벽 + 에어록 문 (기존 문 대신)
        Wh = mat('M_White', hexrgb('#EEF1F5'), 0.25)
        Lc = mat('M_Cyan', hexrgb('#BFF6FF'), 0.3, emit=hexrgb('#7FE8FF'), emit_k=5.0)
        Lk = mat('M_Leaf', hexrgb('#D9DEE6'), 0.35, 0.3)
        for s in (1, -1):
            objs.append(box('M_Wall', (3.0, 0.3, 5.0), Wh, at=(s * 2.6, 6.15, 2.5)))
        objs.append(box('M_WallTop', (2.2, 0.3, 2.0), Wh, at=(0, 6.15, 4.0)))
        objs += airlock_door('M_Lock', 6.0, 1, frame_m=Wh, light_m=Lc, leaf_m=Lk)
        objs += tree('B_TreeA', (-2.8, -0.5), Gr2, Tr, 1.0)
    return objs


def up_option(kind):
    """트인 방에서 위 모듈로: 바닥 = 풀 (z 0), 위 칸 바닥 (해치) = z 5.3."""
    P = mat('U_Pearl', hexrgb('#F4EFE6'), 0.2, 0.15)
    Au = mat('U_Gold', hexrgb('#D9AE4E'), 0.25, 1.0)
    Gr = mat('U_Grass', hexrgb('#5E9A55'), 0.8)
    Beam = mat('U_Beam', hexrgb('#FFF3D6'), 0.3, emit=hexrgb('#FFE7B0'), emit_k=1.2, alpha=0.18)
    Gl = mat('U_Glass', hexrgb('#E6F4FF'), 0.03, glass=True, alpha=0.15)
    L = mat('U_Light', hexrgb('#FFF3D6'), 0.3, emit=hexrgb('#FFE7B0'), emit_k=5.0)
    objs = [box('U_Ground', (9, 9, 0.1), Gr, at=(0, 0, -0.05))]
    H = 5.3
    if kind == 'A':
        objs.append(lathe('U_Pad', [(0.0, 0.06), (0.85, 0.06), (0.95, 0.0), (0.0, 0.0)], P, 48, closed=True))
        objs.append(lathe('U_PadRing', [(0.86, 0.065), (0.93, 0.065), (0.93, 0.08), (0.86, 0.08)], L, 48, closed=True))
        objs.append(lathe('U_Beam', [(0.75, 0.1), (0.75, H - 0.25)], Beam, 48))
        objs.append(lathe('U_Portal', circle(0.09, 12, cx=1.05, cy=H - 0.15), Au, 48, closed=True))
        objs.append(lathe('U_PortalPearl', circle(0.05, 10, cx=0.92, cy=H - 0.15), P, 48, closed=True))
        objs.append(lathe('U_Lid', [(0.0, H - 0.1), (0.7, H - 0.1), (0.7, H - 0.16), (0.0, H - 0.16)], P, 48, closed=True))
    elif kind == 'B':
        for sx in (-0.7, 0.7):
            for sy in (-0.7, 0.7):
                objs.append(box('U_Post', (0.1, 0.1, H), Au, at=(sx, sy, H / 2)))
        for z in (1.8, 3.6):
            objs.append(box('U_Landing', (1.6, 1.6, 0.08), P, at=(0, 0, z)))
        objs.append(box('U_Top', (1.9, 1.9, 0.12), P, at=(0, 0, H - 0.06)))
        for i in range(17):
            objs.append(box('U_Rung', (0.6, 0.04, 0.04), Au, at=(0, -0.78, 0.3 + i * 0.3)))
        for sx in (-0.3, 0.3):
            objs.append(box('U_Rail', (0.04, 0.04, H), Au, at=(sx, -0.78, H / 2)))
    else:
        objs.append(lathe('U_Shaft', [(0.95, 0.0), (0.95, H)], Gl, 48))
        for z in (0.06, H - 0.06):
            objs.append(lathe('U_ShaftRing', [(0.93, z - 0.06), (1.03, z - 0.06), (1.03, z + 0.06), (0.93, z + 0.06)], Au, 48, closed=True))
        objs.append(lathe('U_Cab', [(0.0, 0.12), (0.8, 0.12), (0.8, 0.2), (0.0, 0.2)], P, 48, closed=True))
        objs.append(lathe('U_CabRoof', [(0.0, 2.55), (0.8, 2.55), (0.8, 2.63), (0.0, 2.63)], P, 48, closed=True))
        for a in range(4):
            t = a * math.pi / 2 + math.pi / 4
            objs.append(box('U_CabPost', (0.05, 0.05, 2.45), Au, at=(0.76 * math.cos(t), 0.76 * math.sin(t), 1.37)))
    return objs


def sheet2():
    cam, cd = setup_render()
    star_world()
    SC.render.resolution_x, SC.render.resolution_y = 800, 620
    scenes = [
        ('it_1', '1단계 · 세라믹 통로 (닫힌 관)', interior_stage1, ((0.35, 0.25, 1.6), (-0.1, TUBE_L, 1.75), 16)),
        ('it_2', '2단계 · 우주복 현관 (에어록 방)', interior_vestibule, ((0.6, 0.15, 1.65), (-0.35, 4.0, 1.4), 16)),
        ('it_3', '3단계 · 바닥이 이어진 다리', lambda: bridge_scene(False), ((0.4, -2.2, 1.65), (0.0, 6.0, 1.2), 20)),
        ('it_m', '3단계 ↔ 낮은 단계 · 에어록 문', lambda: bridge_scene(True), ((0.4, -1.6, 1.65), (0.0, 6.0, 1.5), 20)),
    ]
    for key, text, fn, (loc, tgt, lens) in scenes:
        coll = bpy.data.collections.new(key); SC.collection.children.link(coll)
        for ob in fn():
            for c in list(ob.users_collection):
                c.objects.unlink(ob)
            coll.objects.link(ob)
        for c in bpy.data.collections:
            c.hide_render = c is not coll
        shoot(cam, cd, key, loc, tgt, lens)
    for kind in ('A', 'B', 'C'):
        coll = bpy.data.collections.new('Up' + kind); SC.collection.children.link(coll)
        for ob in up_option(kind):
            for c in list(ob.users_collection):
                c.objects.unlink(ob)
            coll.objects.link(ob)
        for c in bpy.data.collections:
            c.hide_render = c is not coll
        shoot(cam, cd, 'up_%s' % kind, (3.4, -4.6, 1.6), (0, 0, 2.6), 24)
    for c in bpy.data.collections:
        c.hide_render = True
    lc = bpy.data.collections.new('Labels2'); SC.collection.children.link(lc)
    SC.render.resolution_x, SC.render.resolution_y = 800, 90
    cd.type = 'ORTHO'; cd.ortho_scale = 4.0
    labels = [(k, t) for k, t, *_ in scenes] + [('up_A', '위로 A · 빛 기둥 승강판 (추천)'), ('up_B', '위로 B · 하늘 사다리 탑'), ('up_C', '위로 C · 유리 승강 통')]
    for i, (key, text) in enumerate(labels):
        lab = label(text, (i * 10.0, 30.0, -0.06), 0.2, lc)
        cam.location = (i * 10.0, 29.0, 0.0); cam.rotation_euler = (math.radians(90), 0, 0)
        SC.render.filepath = os.path.join(PREVIEW, key + '_l.png')
        bpy.ops.render.render(write_still=True)
        lab.hide_render = True
    print('SHEET2 done')


# ---------------- 내보내기 (Unity SM_ConnectorsEvo.fbx + 색표 텍스처) ----------------
# 메시 이름: CN{단계}_{Tube · Collar · Ring · Strip · Strut · Plate · VRing}, CN_Lock (3단계+ 다리가 낮은 단계 모듈에 닿는 면의 에어록 문 고리).
# 재질: Strip = "Flow" (Unity ConnectorFlow), 나머지 = "M_ConnectorsEvo" 하나 — 색은 색표 칸 UV (비활성 = MPB 어둡게가 그대로 먹게).
# 축: Unity 로컬 (x, y 위, z 길이) = Blender (−x, z, −y) (FBX −Z 앞 · Y 위 → Unity x 뒤집힘).
EXPORT = '--export' in argv
SWATCH = ['S1_Ceramic', 'S1_Glass', 'S1_Light', 'S2_Navy', 'S2_Trim', 'S2_Warm', 'S2_Port', 'S3_Pearl',
          'S3_Gold', 'S3_Light', 'S4_Deck', 'S4_Silver', 'S4_Light', 'S4_Dark', 'S4_Star', 'S4_Nebula', 'S1_Leaf']
SW_PX = 8                                                  # 칸 하나 = 8 × 8 픽셀 (Unity 점 필터)
TEX_DIR = os.path.join(ROOT, 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Modules', 'Textures', 'ConnectorsEvo')
FBX_OUT = os.path.join(ROOT, 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Modules', 'SM_ConnectorsEvo.fbx')
TO_BLENDER = Matrix(((-1, 0, 0, 0), (0, 0, -1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def swatch_uv(name):
    i = SWATCH.index(name)
    return ((i + 0.5) / len(SWATCH), 0.5)


def write_palette():
    """색표 세 장: BaseColor (sRGB) · 금속 R + 매끈함 A (선형) · Emission (sRGB)."""
    os.makedirs(TEX_DIR, exist_ok=True)
    w, h = len(SWATCH) * SW_PX, SW_PX
    base, mg, em = [0.0] * (w * h * 4), [0.0] * (w * h * 4), [0.0] * (w * h * 4)
    for i, nm in enumerate(SWATCH):
        m = MATS.get(nm)
        b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED') if m else None
        col = tuple(b.inputs['Base Color'].default_value)[:3] if b else (1, 1, 1)
        metal = b.inputs['Metallic'].default_value if b else 0
        smooth = 1.0 - (b.inputs['Roughness'].default_value if b else 0.5)
        e = tuple(b.inputs['Emission Color'].default_value)[:3] if b and b.inputs['Emission Strength'].default_value > 0 else (0, 0, 0)
        to_s = lambda c: (c * 12.92) if c <= 0.0031308 else (1.055 * c ** (1 / 2.4) - 0.055)
        for y in range(h):
            for x in range(i * SW_PX, (i + 1) * SW_PX):
                k = (y * w + x) * 4
                base[k:k + 4] = [to_s(col[0]), to_s(col[1]), to_s(col[2]), 1.0]
                mg[k:k + 4] = [metal, metal, metal, smooth]
                em[k:k + 4] = [to_s(e[0]), to_s(e[1]), to_s(e[2]), 1.0]
    for nm, px in (('T_ConnectorsEvo_BaseColor', base), ('T_ConnectorsEvo_MetallicGloss', mg), ('T_ConnectorsEvo_Emission', em)):
        im = bpy.data.images.new(nm, w, h, alpha=True)
        im.pixels = px
        im.filepath_raw = os.path.join(TEX_DIR, nm + '.png'); im.file_format = 'PNG'; im.save()


def bake_role(name, objs, flow=False):
    """부품 여럿 → 메시 하나 (Unity 축으로 돌림, 색표 UV, 재질 슬롯 하나)."""
    bm = bmesh.new()
    for ob in objs:
        me = ob.data.copy()
        me.transform(ob.matrix_world)
        tmp = bmesh.new(); tmp.from_mesh(me)
        uv = tmp.loops.layers.uv.verify()
        for f in tmp.faces:
            mname = me.materials[f.material_index].name if me.materials else 'S1_Ceramic'
            u = swatch_uv(mname) if (not flow and mname in SWATCH) else (0.5, 0.5)
            for l in f.loops:
                l[uv].uv = u
        tmp.to_mesh(me); tmp.free()
        bm.from_mesh(me)
        bpy.data.meshes.remove(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.transform(TO_BLENDER)
    slot = 'Flow' if flow else 'M_ConnectorsEvo'
    me.materials.append(bpy.data.materials.get(slot) or bpy.data.materials.new(slot))
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new(name, me); SC.collection.objects.link(ob)
    return ob


def lock_parts():
    W = MATS['S1_Ceramic']; L = MATS['S1_Light']; Lf = mat('S1_Leaf', hexrgb('#C9D6EE'), 0.35, 0.3)
    # 에어록 문 고리: 모듈 면 (z 0)에 붙는 둥근 문 — 축 = z (밖 = +z), 데크 높이 위 문 가운데
    c = DECK_Y + 0.15
    out = []
    for nm, prof, m in (('Lock', [(0.085, -0.006), (0.115, -0.006), (0.115, 0.018), (0.085, 0.018)], W),
                        ('LockL', [(0.08, 0.0), (0.086, 0.0), (0.086, 0.012), (0.08, 0.012)], L),
                        ('LockLeaf', [(0.0, 0.0), (0.082, 0.0), (0.082, 0.008), (0.0, 0.008)], Lf)):
        ob = lathe(nm, prof, m, 40, closed=True)
        ob.matrix_world = Matrix.Translation((0, c, 0))
        out.append(ob)
    return out


def export():
    out = []
    for st, fn in ((1, parts_stage1), (2, parts_stage2), (3, lambda: parts_deck(3)), (4, lambda: parts_deck(4))):
        P = fn()
        for role, key in (('Tube', 'tube'), ('Collar', 'collar'), ('Ring', 'ring'), ('Strut', 'strut'), ('Plate', 'plate'), ('VRing', 'vring')):
            if P.get(key):
                out.append(bake_role('CN%d_%s' % (st, role), P[key]))
        if P.get('strip'):
            out.append(bake_role('CN%d_Strip' % st, P['strip'], flow=True))
    out.append(bake_role('CN_Lock', lock_parts()))
    write_palette()
    for ob in list(SC.collection.objects):
        ob.select_set(ob in out)
    bpy.ops.export_scene.fbx(filepath=FBX_OUT, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=True)
    for ob in out:
        print('EXPORT', ob.name, len(ob.data.polygons), [round(v, 3) for v in ob.dimensions])
    print('EXPORTED', FBX_OUT)


if SHEET:
    sheet()
if SHEET2:
    sheet2()
if EXPORT:
    export()


# ---------------- 내부 통로 키트 (Unity SM_InteriorKit_Tubes.fbx) ----------------
# 미터 · 로컬 (x 오른쪽 · y 위 · 바닥 y 0 · z 길이), 늘리는 조각 (Tube) = z −0.5 ~ 0.5 단면 일정, 끝 (Collar · Airlock) = z ±0.15 (벽 속으로 0.153 묻음),
# 간격 조각 (Ring) · 가구 (Vestibule) = z 0 가운데. 재질 칸 이름 = Unity 프로젝트 재질 이름 (설정 메뉴가 이름으로 이음).
# 걷는 틀 (InteriorGeometry 반폭 1.25 · 높이 2.8 · 충돌은 코드)을 모든 조각이 비움. 문 구멍 1.4 × 2.4 (바닥부터).
#   Evo1 세라믹 튜브 · Evo2 우주복 현관 (보관함 · 벤치 · 거울은 Vestibule) · Evo3 · Evo4 다리 (데크 · 난간 · 하늘 껍질), Airlock 문틀 · 문짝.
EXPORT_KIT = '--export-kit' in argv
KIT_FBX = os.path.join(ROOT, 'SpaceStation', 'Assets', '_Project', 'Art', 'Models', 'Interior', 'SM_InteriorKit_Tubes.fbx')
KMAT = {}


def kmat(name, rgb, rough=0.5, metal=0.0, emit=None, alpha=1.0):
    """미리보기 색만 (Unity는 이름으로 프로젝트 재질을 씀)."""
    if name not in KMAT:
        KMAT[name] = mat(name, hexrgb(rgb), rough, metal, emit=hexrgb(emit) if emit else None, emit_k=3.0 if emit else 0.0, alpha=alpha)
    return KMAT[name]


def orient(ob, inward, cx=0.0, cy=1.5):
    """열린 껍질 면 방향: inward = 단면 가운데 (cx, cy) 쪽을 보게 (안에서 보는 튜브 · 하늘), 아니면 바깥."""
    bm = bmesh.new(); bm.from_mesh(ob.data)
    for f in bm.faces:
        c = f.calc_center_median()
        to_c = Vector((cx - c.x, cy - c.y, 0.0))
        d = f.normal.dot(to_c)
        if (d < 0) == inward:
            f.normal_flip()
    bm.to_mesh(ob.data); bm.free()
    return ob


def solid(ob):
    bm = bmesh.new(); bm.from_mesh(ob.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(ob.data); bm.free()
    return ob


def kit_tube_evo1():
    W = kmat('M_InteriorEvoWhite', '#EEF1F5', 0.3); F = kmat('M_InteriorEvoFloor', '#C9CED6'); Wn = kmat('M_InteriorWindow', '#0B1626', 0.05)
    A = kmat('M_Accent_Core', '#7FE8FF', 0.3, emit='#7FE8FF'); Dk = kmat('M_InteriorEvoDark', '#2A3038', 0.4)
    w, h, r = 3.0, 3.1, 0.6                                   # x 1.25 에서 천장 2.99 (걷는 틀 2.8 위)
    tube = [orient(sweep('T1_Shell', rr_path(w, h, r, 0.0), W), True)]
    for s in (1, -1):
        tube.append(orient(sweep('T1_Win', [(s * (w / 2 - 0.004), 1.05), (s * (w / 2 - 0.004), 1.95)], Wn, closed=False), True))
        tube.append(solid(sweep('T1_WinRim', [(s * (w / 2 - 0.006), 0.99), (s * (w / 2 - 0.006), 1.05), (s * (w / 2 - 0.03), 1.05), (s * (w / 2 - 0.03), 0.99)], A)))
        tube.append(solid(sweep('T1_WinTop', [(s * (w / 2 - 0.006), 1.95), (s * (w / 2 - 0.006), 2.0), (s * (w / 2 - 0.03), 2.0), (s * (w / 2 - 0.03), 1.95)], Dk)))
        tube.append(solid(sweep('T1_FloorLine', [(s * 1.08, 0.003), (s * 1.13, 0.003), (s * 1.13, 0.009), (s * 1.08, 0.009)], A)))
    tube.append(solid(sweep('T1_Floor', [(-w / 2, -0.06), (w / 2, -0.06), (w / 2, 0.003), (-w / 2, 0.003)], F)))
    # 끝 고리: 둥근 사각 테 (바깥 3.6 × 3.55, 안 = 껍질), 두께 ±0.15, 방 쪽 면에 모듈 색 이음 빛줄
    outer = rr_path(3.6, 3.55, 0.85, -0.06); inner = rr_path(w - 0.01, h - 0.005, r, -0.06)
    collar = [solid(open_band('T1_Collar', outer, inner, W, 0.0, 0.3)),
              open_band('T1_CollarLine', rr_path(w + 0.12, h + 0.06, r + 0.06, 0.0), rr_path(w + 0.04, h + 0.02, r + 0.02, 0.0), A, -0.152, 0.004)]
    ring = [solid(open_band('T1_Rib', rr_path(w - 0.01, h - 0.005, r, 0.06), rr_path(w - 0.16, h - 0.08, r - 0.08, 0.06), W, 0.0, 0.1))]
    return tube, collar, ring, []


def suit(objs, x, z, face):
    """보관함 속 우주복 한 벌 (흰 몸 · 투구 · 금빛 창 · 팔 둘) — face = 방 쪽 (+1 / −1 x)."""
    Wt = kmat('M_InteriorEvoWhite', '#EEF1F5', 0.3); Au = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0)
    for nm, (rx, ry, rz), (cx, cy, cz), m in (('S_Body', (0.17, 0.42, 0.16), (0, 1.15, 0), Wt), ('S_Helmet', (0.15, 0.15, 0.15), (0, 1.78, 0), Wt),
                                               ('S_Visor', (0.05, 0.08, 0.11), (0.1 * face, 1.79, 0), Au), ('S_Pack', (0.1, 0.25, 0.15), (-0.13 * face, 1.3, 0), Wt)):
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=1.0)
        for v in bm.verts:
            v.co = Vector((v.co.x * rx + x + cx, v.co.y * rz + z + cz, v.co.z * ry + cy))
        objs.append(new_obj(nm, bm, [m]))
    for s in (1, -1):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.055, radius2=0.06, depth=0.62)
        for v in bm.verts:
            v.co = Vector((v.co.x + x, v.co.y + z + s * 0.2, v.co.z + 1.1))
        objs.append(new_obj('S_Arm', bm, [Wt]))
    for s in (1, -1):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.07, radius2=0.075, depth=0.72)
        for v in bm.verts:
            v.co = Vector((v.co.x + x, v.co.y + z + s * 0.09, v.co.z + 0.4))
        objs.append(new_obj('S_Leg', bm, [Wt]))


def kit_tube_evo2():
    N = kmat('M_InteriorEvo2Wall', '#232A55', 0.45); D = kmat('M_InteriorEvo2Deck', '#171C3A'); T = kmat('M_InteriorEvo2Trim', '#A9B0E2', 0.3, 0.5)
    Au = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0); Lt = kmat('M_InteriorLight', '#FFE2A8', 0.3, emit='#FFC46A'); A = kmat('M_Accent_Core', '#FFC46A', emit='#FFC46A')
    Dk = kmat('M_InteriorEvoDark', '#2A3038', 0.1, 0.6)
    w, h, r = 3.2, 3.2, 0.7
    tube = [orient(sweep('T2_Shell', rr_path(w, h, r, 0.0), N), True)]
    tube.append(solid(sweep('T2_Floor', [(-w / 2, -0.06), (w / 2, -0.06), (w / 2, 0.003), (-w / 2, 0.003)], D)))
    tube.append(solid(sweep('T2_Runner', [(-0.05, 0.003), (0.05, 0.003), (0.05, 0.009), (-0.05, 0.009)], A)))
    tube.append(solid(sweep('T2_Ceil', [(-0.55, h - 0.03), (0.55, h - 0.03), (0.55, h - 0.005), (-0.55, h - 0.005)], Lt)))
    for s in (1, -1):
        tube.append(solid(sweep('T2_Edge', [(s * 1.52, 0.003), (s * 1.58, 0.003), (s * 1.58, 0.07), (s * 1.52, 0.07)], T)))
    # 끝 테: 얇게 벽에 붙임 (z −0.15 ~ −0.05 → 벽 속으로 0.153 묻으면 벽면 + 0.003 ~ 0.103) — 트인 방 사이 틈이 0.85 m뿐이라 두꺼운 테 둘이 겹침
    outer = rr_path(3.8, 3.7, 0.95, -0.06); inner = rr_path(w - 0.01, h - 0.005, r, -0.06)
    collar = [solid(open_band('T2_Collar', outer, inner, T, -0.1, 0.1)),
              open_band('T2_CollarGold', rr_path(w + 0.14, h + 0.07, r + 0.07, 0.0), rr_path(w + 0.04, h + 0.02, r + 0.02, 0.0), Au, -0.048, 0.004)]
    # 빛 아치 (간격): 연보라 테 + 안쪽 따뜻한 빛 띠
    ring = [open_band('T2_Arch', rr_path(w - 0.01, h - 0.005, r, 0.0), rr_path(w - 0.12, h - 0.06, r - 0.06, 0.0), T, 0.0, 0.14),
            open_band('T2_ArchL', rr_path(w - 0.12, h - 0.06, r - 0.06, 0.25), rr_path(w - 0.13, h - 0.065, r - 0.065, 0.25), Lt, 0.0, 0.05)]
    # 얇은 현관 (사용자 2026-11-17 "얇은 현관" — 2단계 ↔ 1단계 이하 틈 0.85 m): 양옆 벽에 우주복 보관함 한 칸씩 (폭 0.6, 걷는 틀 밖 0.33 깊이), 벤치 · 거울 없음
    ves = []
    for side in (-1, 1):
        x0 = side * (w / 2 - 0.02)                        # 벽 안쪽 면
        inward = -side
        ves.append(box('V_Back', (0.04, 0.6, 2.3), N, at=(x0 + inward * 0.02, 0, 1.2)))
        for dz in (-0.3, 0.3):
            ves.append(box('V_Side', (0.33, 0.04, 2.3), T, at=(x0 + inward * 0.165, dz, 1.2)))
        for yy in (0.06, 2.33):
            ves.append(box('V_Shelf', (0.33, 0.6, 0.04), T, at=(x0 + inward * 0.165, 0, yy)))
        ves.append(box('V_Lamp', (0.03, 0.46, 0.03), Lt, at=(x0 + inward * 0.33, 0, 2.28)))
        ves.append(box('V_Rim', (0.03, 0.64, 0.04), Au, at=(x0 + inward * 0.335, 0, 2.36)))
        suit(ves, x0 + inward * 0.17, 0.0, inward)
    for ob in ves:
        if ob.name.startswith(('V_Back', 'V_Side', 'V_Shelf', 'V_Lamp', 'V_Rim', 'S_')):
            # box()는 (x, y, z)=(가로, 깊이, 높이) 월드 → 키트 로컬 (x, 위, 길이)로: y ↔ z 바꿈
            ob.data.transform(Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    return tube, collar, ring, ves


def kit_bridge(st):
    if st == 3:
        D = kmat('M_InteriorEvo3Pearl', '#F4EFE6', 0.3, 0.1); E = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0); P = D
        Fl = kmat('M_InteriorEvo2Path', '#E8E0D0', 0.6)
    else:
        D = kmat('M_InteriorEvo4Wall', '#2E2766', 0.35, 0.2); E = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0); P = D
        Fl = kmat('M_InteriorEvo4Deck', '#1B1740', 0.45)
    Lt = kmat('M_InteriorLight', '#FFE2A8', 0.3, emit='#FFC46A'); Sky = kmat('M_InteriorEvo2Sky', '#101830', 0.5)
    A = kmat('M_Accent_Core', '#FFE7B0', emit='#FFE7B0')
    hw = 1.5
    tube = [solid(sweep('B%d_Deck' % st, [(-hw, -0.28), (hw, -0.28), (hw, 0.003), (-hw, 0.003)], Fl)),
            solid(sweep('B%d_Under' % st, [(-hw * 0.6, -0.5), (hw * 0.6, -0.5), (hw * 0.85, -0.28), (-hw * 0.85, -0.28)], D)),
            solid(sweep('B%d_Glow' % st, [(-0.04, 0.003), (0.04, 0.003), (0.04, 0.008), (-0.04, 0.008)], A))]
    for s in (1, -1):
        tube.append(solid(sweep('B%d_Edge' % st, [(s * hw, -0.3), (s * (hw + 0.08), -0.3), (s * (hw + 0.08), 0.06), (s * hw, 0.06)], E)))
        tube.append(solid(sweep('B%d_Rail' % st, circle(0.035, 10, cx=s * (hw - 0.02), cy=1.05), E)))
        tube.append(solid(sweep('B%d_Rail2' % st, circle(0.022, 10, cx=s * (hw - 0.02), cy=0.55), E)))
    # 하늘 껍질 (바깥 하늘 화면 — 트인 방 하늘과 같은 재질): 데크 둘레 원통 (반지름 3.4, 가운데 y 1.0)
    tube.append(orient(sweep('B%d_Sky' % st, circle(3.4, 40, cx=0.0, cy=1.0), Sky), True, 0.0, 1.0))
    # 끝 = 문턱판 (데크와 방 바닥 이음매를 덮음) + 양옆 낮은 기둥
    collar = [solid(sweep('B%d_Sill' % st, [(-hw - 0.08, -0.3), (hw + 0.08, -0.3), (hw + 0.08, 0.012), (-hw - 0.08, 0.012)], E, -0.15, -0.05))]
    for s in (1, -1):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.09, radius2=0.07, depth=1.2)
        for v in bm.verts:
            v.co = Vector((v.co.x + s * (hw - 0.02), v.co.z + 0.6, v.co.y - 0.1))
        collar.append(new_obj('B%d_Pillar' % st, bm, [P]))
    # 간격: 난간 기둥 둘 + 한쪽 정원등 (기둥 + 빛 구)
    ring = []
    for s in (1, -1):
        bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=0.03, radius2=0.03, depth=1.05)
        for v in bm.verts:
            v.co = Vector((v.co.x + s * (hw - 0.02), v.co.z + 0.525, v.co.y))
        ring.append(new_obj('B%d_Post' % st, bm, [E]))
    bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.05, radius2=0.04, depth=1.7)
    for v in bm.verts:
        v.co = Vector((v.co.x + hw - 0.18, v.co.z + 0.85, v.co.y))
    ring.append(new_obj('B%d_LampPost' % st, bm, [P]))
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=0.14) if st == 3 else bmesh.ops.create_icosphere(bm, subdivisions=1, radius=0.15)
    for v in bm.verts:
        v.co += Vector((hw - 0.18, 1.82, 0))
    ring.append(new_obj('B%d_Lamp' % st, bm, [Lt]))
    return tube, collar, ring, []


def kit_airlock():
    """에어록 문틀 (튜브 끝, 문 구멍 1.4 × 2.4 둘레 둥근 사각 두꺼운 테 + 모듈 색 빛 테 + 위 상태 띠) · 문짝 (0.72 × 2.4 × 0.09, 둥근 창 · 빛줄)."""
    W = kmat('M_InteriorEvoWhite', '#EEF1F5', 0.3); Dk = kmat('M_InteriorEvoDark', '#2A3038', 0.2, 0.4)
    A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    frame = [open_band('L_Frame', rr_path(2.5, 3.2, 0.6, -0.06), rr_path(1.46, 2.46, 0.3, -0.06), W, -0.09, 0.12),
             open_band('L_Glow', rr_path(1.62, 2.56, 0.36, 0.0), rr_path(1.5, 2.49, 0.32, 0.0), A, -0.028, 0.004),
             solid(box('L_Lamp', (0.9, 0.04, 0.08), A, at=(0, -0.02, 2.82)))]
    frame[-1].data.transform(Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1))))
    # 문짝 (가운데 원점): 흰 판 + 짙은 둥근 창 + 세로 빛줄 (맞닿는 쪽)
    leaf = [solid(box('L_Leaf', (0.72, 2.4, 0.08), W)),
            lathe('L_Port', [(0.0, 0.042), (0.15, 0.042)], Dk, 24),
            lathe('L_PortRim', [(0.15, 0.04), (0.19, 0.04), (0.19, 0.046), (0.15, 0.046)], W, 24, closed=True),
            lathe('L_PortB', [(0.15, -0.042), (0.0, -0.042)], Dk, 24),                       # 뒤쪽 창 (양면 문짝)
            lathe('L_PortRimB', [(0.15, -0.046), (0.19, -0.046), (0.19, -0.04), (0.15, -0.04)], W, 24, closed=True),
            solid(box('L_Stripe', (0.04, 2.0, 0.09), A, at=(-0.32, 0, 0)))]
    for ob in leaf[1:5]:
        ob.data.transform(Matrix.Translation((0.05, 0.45, 0)))
    for ob in frame[:2]:
        solid(ob)
    return frame, leaf


def bake_kit(name, objs):
    """조각 여럿 → 메시 하나 (재질 칸 = 이름 합집합, Unity 축으로 돌림)."""
    bm = bmesh.new(); names = []
    for ob in objs:
        me = ob.data.copy(); me.transform(ob.matrix_world)
        for p in me.polygons:
            mn = me.materials[p.material_index].name if me.materials else 'M_InteriorEvoWhite'
            if mn not in names:
                names.append(mn)
            p.material_index = names.index(mn)
        bm.from_mesh(me); bpy.data.meshes.remove(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.transform(TO_BLENDER)
    for mn in names:
        short = SLOT_SHORT.get(mn, mn)                     # Unity InteriorSetup.CreateKit 슬롯 이름 (EvoWhite · Accent · Window …)
        me.materials.append(bpy.data.materials.get(short) or bpy.data.materials.new(short))
    for p in me.polygons:
        p.use_smooth = False
    ob = bpy.data.objects.new(name, me); SC.collection.objects.link(ob)
    return ob


SLOT_SHORT = {'M_InteriorEvoWhite': 'EvoWhite', 'M_InteriorEvoFloor': 'EvoFloor', 'M_InteriorEvoDark': 'EvoDark', 'M_InteriorWindow': 'Window',
              'M_Accent_Core': 'Accent', 'M_InteriorLight': 'Light', 'M_InteriorEvo2Wall': 'Evo2Wall', 'M_InteriorEvo2Deck': 'Evo2Deck',
              'M_InteriorEvo2Trim': 'Evo2Trim', 'M_InteriorEvo2Gold': 'Evo2Gold', 'M_InteriorEvo2Sky': 'Evo2Sky', 'M_InteriorEvo3Pearl': 'Evo3Pearl',
              'M_InteriorEvo3Gold': 'Evo3Gold', 'M_InteriorEvo2Path': 'Evo3Path', 'M_InteriorEvo4Wall': 'Evo4Wall', 'M_InteriorEvo4Trim': 'Evo4Trim',
              'M_InteriorEvo4Deck': 'Evo4Deck', 'M_InteriorHull': 'Hull', 'M_InteriorHullDark': 'HullDark', 'M_InteriorHazard': 'Hazard',
              'M_InteriorEvoDark': 'EvoDark', 'M_InteriorEvo2Star': 'Evo4Star', 'M_InteriorEvo3Prism': 'Evo3Prism', 'M_InteriorLeaf': 'Leaf'}


def kit_short(st):
    """짧은 틈 (0.7 m 아래 — 트인 방끼리 · 2 ↔ 2 · 3단계 ↔ 2단계): 난간 · 하늘 껍질 · 끝 테 없이 두 방 바닥을 잇는 문턱판만 (늘려 씀)."""
    if st == 2:
        Fl = kmat('M_InteriorEvo2Deck', '#171C3A'); E = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0)
    elif st == 3:
        Fl = kmat('M_InteriorEvo2Path', '#E8E0D0', 0.6); E = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0)
    else:
        Fl = kmat('M_InteriorEvo4Deck', '#1B1740', 0.45); E = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0)
    hw = 0.95                                                   # 문간 (3단계 0.95 · 문 구멍 1.4) 폭만큼
    objs = [solid(sweep('S%d_Sill' % st, [(-hw, -0.12), (hw, -0.12), (hw, 0.004), (-hw, 0.004)], Fl))]
    for s in (1, -1):
        objs.append(solid(sweep('S%d_Edge' % st, [(s * hw, -0.12), (s * (hw + 0.06), -0.12), (s * (hw + 0.06), 0.03), (s * hw, 0.03)], E)))
    return objs


UP_SWAP = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # Blender z 위로 만든 것 → 키트 로컬 (x, 위 y, 길이 z)


def upright(ob):
    """z축으로 돌려 만든 조각을 키트 로컬 (y = 위)로 세우고 바깥 법선으로."""
    ob.data.transform(UP_SWAP)
    return solid(ob)


def kit_skylift(st):
    """빛 기둥 승강판 (진화 단계별 통로 ③, 사용자 2026-11-17 "A로 하는데, 색깔을 모듈의 색 테마에 맞춰서"):
    바닥 판 = 반지름 0.75 · 윗면 y 0.05 · 옆 치마 y −0.25까지 (언덕 위 판은 치마가 땅에 묻혀 떠 보이지 않음) + 바깥 테 (단계 금속) + 빛 고리 · 가운데 빛 렌즈 (강조 슬롯 = 방 모듈 색).
    하늘 고리 = 반지름 0.85 (하늘 껍질 바로 아래, 피벗 = 고리 가운데): 금속 고리 + 아래를 보는 빛 띠 + 단계 몸 색 바깥 테. 빛 기둥은 Unity가 그림 (SpaceStation/LightShaft).
    큰 동심원만 (작은 구멍 · 점 무늬 없음)."""
    if st == 2:
        B = kmat('M_InteriorEvo2Deck', '#171C3A'); T = kmat('M_InteriorEvo2Trim', '#A9B0E2', 0.3, 0.5); G = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0)
    elif st == 3:
        B = kmat('M_InteriorEvo3Pearl', '#F4EFE6', 0.3, 0.1); G = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0); T = G
    else:
        B = kmat('M_InteriorEvo4Deck', '#1B1740', 0.45); G = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0); T = G
    A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    R = 0.75
    pad = [upright(lathe('P_Body', [(0.0, 0.05), (R - 0.09, 0.05), (R - 0.09, 0.045), (R - 0.03, 0.03), (R - 0.03, -0.25), (0.0, -0.25)], B, 48)),
           upright(lathe('P_Rim', [(R - 0.1, 0.045), (R, 0.045), (R, -0.25), (R - 0.03, -0.25), (R - 0.03, 0.02), (R - 0.1, 0.02)], T, 48, closed=True)),
           upright(lathe('P_RimTop', [(R - 0.1, 0.045), (R - 0.1, 0.07), (R - 0.005, 0.07), (R, 0.045)], T, 48, closed=True)),
           upright(lathe('P_Glow', [(0.46, 0.049), (0.56, 0.049), (0.56, 0.056), (0.46, 0.056)], A, 48, closed=True)),
           upright(lathe('P_LensRim', [(0.17, 0.049), (0.24, 0.049), (0.24, 0.064), (0.17, 0.064)], T, 32, closed=True)),
           upright(lathe('P_Lens', [(0.0, 0.058), (0.17, 0.058), (0.17, 0.049), (0.0, 0.049)], A, 32, closed=True))]
    Rr = 0.85
    ring = [upright(lathe('R_Metal', circle(0.05, 12, cx=Rr, cy=0.0), G, 48, closed=True)),
            upright(lathe('R_Band', [(Rr + 0.05, 0.02), (Rr + 0.2, 0.02), (Rr + 0.2, -0.02), (Rr + 0.05, -0.02)], B, 48, closed=True)),
            upright(lathe('R_Edge', [(Rr + 0.2, 0.025), (Rr + 0.24, 0.025), (Rr + 0.24, -0.025), (Rr + 0.2, -0.025)], T, 48, closed=True)),
            upright(lathe('R_Glow', [(Rr - 0.12, -0.03), (Rr - 0.04, -0.03), (Rr - 0.04, -0.012), (Rr - 0.12, -0.012)], A, 48, closed=True))]
    return pad, ring


DS_HALF, DS_LAND, DS_RUN, DS_RISE, DS_STEPS = 1.1, 1.0, 0.3, 0.78, 3   # Unity InteriorGeometry.Steps* 와 같음 (층계참 1.0 = 문 벽 0.2 + 난간 사이 0.72 — 0.85면 0.56이라 플레이어 (지름 0.6)가 옆으로 못 지나감)


def kit_door_steps(st):
    """문 계단 (모든 면 연결 ②, 2026-11-19 — 바닥이 문턱보다 0.78 m 낮은 정원 방 (AI 중추 · 수리 사령부)의 옆문 앞, 문이 열릴 때만 Unity가 놓음):
    피벗 = 문 평면 · 문턱 가운데 (y 0 = 문턱, 방 쪽 = −z). 문턱 높이 층계참 (x ±1.1 · z −1.0 ~ 0) + +x 쪽으로 세 단 (너비 0.3 · 높이 0.195, 땅 = y −0.78).
    단계 몸 색 받침 (땅 2 cm 밑부터) 위 판석 판 (5 cm, 받침보다 1 cm 넓게) · 내려가는 모서리 금속 테, 층계참 방 쪽 · 끝 (−x)을 따라 난간
    (AI 중추 첫 층계참과 같은 꼴 — 세 단은 정원 계단처럼 난간 없이). 반대쪽은 Unity가 x −1로, 다른 높이는 y로 늘려 씀."""
    if st == 2:
        B = kmat('M_InteriorEvo2Wall', '#232A55', 0.45); Fl = kmat('M_InteriorEvo2Deck', '#171C3A'); E = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0)
        R = kmat('M_InteriorEvo2Trim', '#A9B0E2', 0.3, 0.5)
    elif st == 3:
        B = kmat('M_InteriorEvo3Pearl', '#F4EFE6', 0.3, 0.1); Fl = kmat('M_InteriorEvo2Path', '#E8E0D0', 0.6); E = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0)
        R = E
    else:
        B = kmat('M_InteriorEvo4Wall', '#2E2766', 0.35, 0.2); Fl = kmat('M_InteriorEvo4Deck', '#1B1740', 0.45); E = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0)
        R = E
    rise = DS_RISE / (DS_STEPS + 1)
    z0, z1, ground = -DS_LAND, 0.0, -DS_RISE
    rects = [(-DS_HALF, DS_HALF, 0.0)] + [(DS_HALF + (k - 1) * DS_RUN, DS_HALF + k * DS_RUN, -k * rise) for k in range(1, DS_STEPS + 1)]
    objs = []
    for (a0, a1, top) in rects:
        cx, cz, sx, sz = (a0 + a1) / 2, (z0 + z1) / 2, a1 - a0, z1 - z0
        objs.append(solid(box('DS_Body', (sx - 0.02, top - 0.05 - (ground - 0.02), sz - 0.02), B, at=(cx, (ground - 0.02 + top - 0.05) / 2, cz))))
        objs.append(solid(box('DS_Tread', (sx, 0.05, sz), Fl, at=(cx, top - 0.025, cz))))
        objs.append(solid(box('DS_Nose', (0.04, 0.015, sz - 0.04), E, at=(a1 - 0.022, top + 0.0045, cz))))
    # 난간: 층계참 방 쪽 (z −0.8) · 끝 (x −1.05) — 손잡이 (1.05) · 가운데 줄 (0.5) · 기둥 셋 (몸 색 + 금속 깃)
    xa, xb, za, zb, H = -DS_HALF + 0.05, DS_HALF - 0.05, z0 + 0.05, -0.06, 1.05
    for (r, y) in ((0.032, H), (0.014, 0.5)):
        run = sweep('DS_RailX', circle(r, 10, cx=0.0, cy=y), R, xa, xb)          # z 방향으로 민 뒤 y축으로 돌려 x 방향 (방 쪽)
        run.data.transform(Matrix.Translation((0, 0, za)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
        objs.append(solid(run))
        objs.append(solid(sweep('DS_RailZ', circle(r, 10, cx=xa, cy=y), R, za, zb)))
    for (px, pz) in ((xa, za), (xb, za), (xa, zb)):
        for (nm, r0, r1, y0, y1, m) in (('DS_Post', 0.03, 0.03, -0.02, H, B), ('DS_Collar', 0.052, 0.044, -0.02, 0.06, R)):
            bm = bmesh.new(); bmesh.ops.create_cone(bm, cap_ends=True, segments=10, radius1=r0, radius2=r1, depth=y1 - y0)
            for v in bm.verts:
                v.co = Vector((v.co.x + px, v.co.z + (y0 + y1) / 2, v.co.y + pz))
            objs.append(solid(new_obj(nm, bm, [m])))
        bm = bmesh.new(); bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.045)
        for v in bm.verts:
            v.co += Vector((px, H, pz))
        objs.append(new_obj('DS_Knob', bm, [R]))
    return objs


# ---------------- 위아래 이동 장치 (2026-11-19) ----------------
# 사용자 "단계별로 각 단계 테마에 맞는 상하이동장치" — 시안 (scratchpad up9/lift_concepts3.py) "이대로 진행".
# Blender z 위로 짓고 UP_SWAP으로 키트 로컬 (y 위, 앞 = −z)로 세움. 피벗 = 발판 밑 땅 가운데, 발판 윗면 = TR_PAD (Unity InteriorTransit.PadHeight).
# 빛 기둥은 Unity (InteriorSkyLift, 방 강조색)가 그림 — 1 ~ 4단계만. 모두 3.25 m 밑 (낮은 천장 방).
TR_PAD = 0.14


def _tr(ob):
    ob.data.transform(UP_SWAP)
    return solid(ob)


def tr_cyl(name, x, y, r, z0, z1, m, segs=40, r1=None, caps=True):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, segments=segs, radius1=r, radius2=r if r1 is None else r1, depth=z1 - z0,
                          matrix=Matrix.Translation((x, y, (z0 + z1) / 2)))
    return _tr(new_obj(name, bm, [m]))


def tr_box(name, x, y, z, sx, sy, sz, m, rz=0.0, rx=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((x, y, z)) @ Matrix.Rotation(rz, 4, 'Z') @ Matrix.Rotation(rx, 4, 'X') @ Matrix.Diagonal((sx, sy, sz, 1)))
    return _tr(new_obj(name, bm, [m]))


def tr_torus(name, x, y, z, R, r, m, segs=48, tsegs=10, rx=0.0, ry=0.0, rz=0.0, arc=1.0, a0=0.0):
    bm = bmesh.new()
    n = segs if arc >= 1.0 else segs + 1
    rows = []
    for i in range(n):
        a = a0 + 2 * math.pi * arc * i / segs
        c = Vector((R * math.cos(a), R * math.sin(a), 0)); d = c.normalized()
        rows.append([bm.verts.new(c + d * r * math.cos(2 * math.pi * j / tsegs) + Vector((0, 0, r * math.sin(2 * math.pi * j / tsegs)))) for j in range(tsegs)])
    for i in range(n if arc >= 1.0 else n - 1):
        A, B = rows[i], rows[(i + 1) % n]
        for j in range(tsegs):
            bm.faces.new((A[j], A[(j + 1) % tsegs], B[(j + 1) % tsegs], B[j]))
    if arc < 1.0:
        bm.faces.new(list(reversed(rows[0]))); bm.faces.new(rows[-1])
    bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(rz, 3, 'Z') @ Matrix.Rotation(ry, 3, 'Y') @ Matrix.Rotation(rx, 3, 'X'))
    bmesh.ops.translate(bm, vec=(x, y, z), verts=bm.verts)
    return _tr(new_obj(name, bm, [m]))


def tr_tube(name, pts, r, m, segs=10):
    bm = bmesh.new()
    P = [Vector(p) for p in pts]
    rings = []
    for i, c in enumerate(P):
        t = (P[min(i + 1, len(P) - 1)] - P[max(i - 1, 0)]).normalized()
        ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
        a1 = t.cross(ref).normalized(); a2 = t.cross(a1)
        rings.append([bm.verts.new(c + (a1 * math.cos(2 * math.pi * j / segs) + a2 * math.sin(2 * math.pi * j / segs)) * r) for j in range(segs)])
    for i in range(len(rings) - 1):
        for j in range(segs):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    return _tr(new_obj(name, bm, [m]))


def tr_sphere(name, x, y, z, r, m, segs=16):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=segs // 2, radius=r)
    bmesh.ops.translate(bm, vec=(x, y, z), verts=bm.verts)
    return _tr(new_obj(name, bm, [m]))


def tr_star(name, x, y, z, ro, ri, n, t, m, rz=0.0, upright_star=False):
    bm = bmesh.new()
    pts = [((ro if k % 2 == 0 else ri) * math.cos(rz + math.pi * k / n + math.pi / 2), (ro if k % 2 == 0 else ri) * math.sin(rz + math.pi * k / n + math.pi / 2)) for k in range(2 * n)]
    lo = [bm.verts.new((px, py, -t / 2)) for px, py in pts]
    hi = [bm.verts.new((px, py, t / 2)) for px, py in pts]
    bm.faces.new(lo[::-1]); bm.faces.new(hi)
    for k in range(len(pts)):
        j = (k + 1) % len(pts)
        bm.faces.new((lo[k], lo[j], hi[j], hi[k]))
    if upright_star:
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'))
    bmesh.ops.translate(bm, vec=(x, y, z), verts=bm.verts)
    return _tr(new_obj(name, bm, [m]))


def tr_gem(name, x, y, z, r, up, dn, m, sides=6):
    bm = bmesh.new()
    top = bm.verts.new((x, y, z + up)); bot = bm.verts.new((x, y, z - dn))
    rg = [bm.verts.new((x + r * math.cos(2 * math.pi * k / sides), y + r * math.sin(2 * math.pi * k / sides), z)) for k in range(sides)]
    for k in range(sides):
        j = (k + 1) % sides
        bm.faces.new((top, rg[k], rg[j])); bm.faces.new((bot, rg[j], rg[k]))
    return _tr(new_obj(name, bm, [m]))


def tr_blade(name, base, d, length, width, lift, curl, m, thick=0.02, n=8):
    """둥근 잎 · 꽃잎: base에서 d 방향, 위로 lift만큼 기울고 끝이 curl만큼 휨 (끝이 둥긂)."""
    d = Vector(d).normalized(); s = Vector((-d.y, d.x, 0)).normalized() if abs(d.z) < 0.99 else Vector((1, 0, 0))
    up = s.cross(d).normalized()
    if up.z < 0:
        up = -up
    bm = bmesh.new()
    top, bot = [], []
    for i in range(n + 1):
        t = i / n
        w = width * 0.5 * (0.35 + 0.65 * math.sin(math.pi * min(t * 0.92 + 0.08, 1.0)) ** 0.5) * (1.0 if t < 0.85 else math.sqrt(max(0.0, 1 - ((t - 0.85) / 0.15) ** 2)))
        c = Vector(base) + d * (length * t) + up * (lift * t + curl * t * t)
        top.append((bm.verts.new(c + s * w), bm.verts.new(c - s * w)))
        bot.append((bm.verts.new(c + s * w - up * thick), bm.verts.new(c - s * w - up * thick)))
    for i in range(n):
        bm.faces.new((top[i][0], top[i + 1][0], top[i + 1][1], top[i][1]))
        bm.faces.new((bot[i][1], bot[i + 1][1], bot[i + 1][0], bot[i][0]))
        bm.faces.new((top[i][0], bot[i][0], bot[i + 1][0], top[i + 1][0]))
        bm.faces.new((top[i + 1][1], bot[i + 1][1], bot[i][1], top[i][1]))
    bm.faces.new((top[0][1], bot[0][1], bot[0][0], top[0][0]))
    bm.faces.new((top[n][0], bot[n][0], bot[n][1], top[n][1]))
    return _tr(new_obj(name, bm, [m]))


def tr_tri(name, pts, m, thick=0.01):
    bm = bmesh.new()
    lo = [bm.verts.new(p) for p in pts]
    hi = [bm.verts.new((p[0], p[1] - thick, p[2])) for p in pts]
    bm.faces.new(lo); bm.faces.new(hi[::-1])
    for k in range(3):
        j = (k + 1) % 3
        bm.faces.new((lo[k], hi[k], hi[j], lo[j]))
    return _tr(new_obj(name, bm, [m]))


def transit_basic():
    """기본 (생존 공업): 앞 (−y)이 트인 화물 승강기 칸 — 철판 바닥 · 홈 줄 · 문턱 경고 띠 · 네 모서리 기둥 · 옆 철망 + 노란 가로대 · 뒤판 · 뒤 안내 레일 둘
    · 지붕 + 모터 상자 · 감는 북 · 지붕 앞 경고 띠 · 오르내림 화살표 등 (모양으로 구분) · 호출 단추 기둥 (오른쪽 앞)."""
    S = kmat('M_InteriorHull', '#8A9099', 0.45, 0.7); D = kmat('M_InteriorHullDark', '#2B2F35', 0.55, 0.6)
    Y = kmat('M_InteriorHazard', '#F2B81A', 0.45, 0.1); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_box('T_Base', 0, 0, TR_PAD / 2, 1.9, 1.9, TR_PAD, D),
         tr_box('T_Floor', 0, 0, TR_PAD - 0.005, 1.72, 1.72, 0.012, S)]
    for k in range(-5, 6):
        o.append(tr_box('T_Tread', k * 0.15, 0.05, TR_PAD + 0.004, 0.025, 1.5, 0.01, D))
    for i in range(10):
        o.append(tr_box('T_Stripe', -0.855 + 0.19 * i + 0.095, -0.9, TR_PAD + 0.002, 0.19, 0.1, 0.012, Y if i % 2 == 0 else D))
    for sx in (-0.9, 0.9):
        for sy in (-0.9, 0.9):
            o.append(tr_box('T_Post', sx, sy, (TR_PAD + 2.8) / 2, 0.1, 0.1, 2.8 - TR_PAD, D))
        o.append(tr_box('T_SideMesh', sx, 0.05, 1.2, 0.02, 1.7, 1.6, D))
        for z in (0.45, 1.25, 2.05):
            o.append(tr_box('T_SideBar', sx, 0.0, z, 0.05, 1.8, 0.06, Y))
    o += [tr_box('T_Back', 0, 0.9, 1.45, 1.7, 0.03, 2.5, S),
          tr_box('T_Roof', 0, 0, 2.86, 2.0, 2.0, 0.12, D),
          tr_box('T_Motor', 0, 0.25, 3.05, 1.0, 0.8, 0.28, S)]
    for sx in (-0.5, 0.5):
        o.append(tr_box('T_Rail', sx, 1.02, 1.6, 0.08, 0.1, 3.0, S))
    drum = tr_cyl('T_Drum', 0, 0, 0.16, -0.45, 0.45, D, 16)
    drum.data.transform(Matrix.Translation((0, 3.05, -0.32)) @ Matrix.Rotation(math.pi / 2, 4, 'Z'))
    o.append(drum)
    for i in range(10):
        o.append(tr_box('T_RoofStripe', -0.95 + 0.19 * i + 0.095, -1.0, 2.86, 0.19, 0.02, 0.12, Y if i % 2 == 0 else D))
    for sx, up in ((-0.35, True), (0.35, False)):
        s = 1 if up else -1
        o.append(tr_tri('T_Arrow', [(sx - 0.13, -1.015, 2.86 - 0.07 * s), (sx, -1.015, 2.86 + 0.08 * s), (sx + 0.13, -1.015, 2.86 - 0.07 * s)][::s], A))
    o += [tr_box('T_CallPost', 1.25, -0.95, 0.6, 0.22, 0.18, 1.2, Y),
          tr_box('T_CallPanel', 1.25, -1.045, 1.0, 0.16, 0.012, 0.36, D)]
    for z in (1.1, 0.92):
        b = tr_cyl('T_Button', 0, 0, 0.04, -0.012, 0.012, A, 12)
        b.data.transform(Matrix.Translation((1.25, z, -1.055)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
        o.append(b)
    return o


def transit_stage1():
    """1단계 (발달한 미래 기술): 흰 세라믹 반중력 원판 — 판 안 빛 고리 둘 (강조색) · 휜 흰 기둥 셋 (기둥마다 빛 고리) · 위 후광 고리 + 아래 빛 띠 · 홀로그램 목적지 판 (오른쪽 앞)."""
    W = kmat('M_InteriorEvoWhite', '#ECEFF3', 0.25); D = kmat('M_InteriorEvoFloor', '#9EA3AD', 0.4); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_cyl('T_Pad', 0, 0, 0.95, 0.0, TR_PAD, W, 48, r1=0.9),
         tr_cyl('T_PadInner', 0, 0, 0.7, TR_PAD - 0.004, TR_PAD + 0.006, D, 48),
         tr_torus('T_PadGlow', 0, 0, TR_PAD + 0.006, 0.74, 0.02, A),
         tr_torus('T_PadGlow2', 0, 0, TR_PAD + 0.006, 0.45, 0.012, A, 40, 6)]
    for k in range(3):
        a = math.radians(90 + 120 * k)
        pts = []
        for t in range(11):
            u = t / 10
            rr = 0.9 - 0.16 * math.sin(math.pi * u)
            pts.append((rr * math.cos(a), rr * math.sin(a), 0.1 + 2.8 * u))
        o.append(tr_tube('T_Pylon', pts, 0.055, W, 12))
        ring = tr_torus('T_PylonLight', 0, 0, 0, 0.065, 0.012, A, 16, 6, rx=math.pi / 2)
        ring.data.transform(Matrix.Translation((0.745 * math.cos(a), 1.5, 0.745 * math.sin(a))) @ Matrix.Rotation(-a + math.pi / 2, 4, 'Y'))
        o.append(ring)
    o += [tr_torus('T_Halo', 0, 0, 2.95, 0.88, 0.075, W, 48, 12),
          tr_torus('T_HaloGlow', 0, 0, 2.865, 0.82, 0.016, A),
          tr_box('T_HoloBase', 1.25, -0.75, 0.04, 0.3, 0.3, 0.08, W),
          tr_box('T_HoloStem', 1.25, -0.75, 0.55, 0.07, 0.07, 1.0, W),
          tr_box('T_HoloPanel', 1.25, -0.75, 1.3, 0.46, 0.012, 0.3, A, rz=math.radians(30), rx=math.radians(-15))]
    return o


def transit_stage2():
    """2단계 (여가 · 밤하늘 천문 기구): 혼천의 승강대 — 미드나잇 원판 · 금 테 · 별자리 상감 (별 여섯 + 금 선) · 판 둘레 금 별 넷 · 금 지지대 셋이 받친 혼천의 (금 고리 셋 + 별빛 구)
    · 꼭대기 별 · 망원경 모양 안내 기둥 (오른쪽 앞)."""
    M = kmat('M_InteriorEvo2Deck', '#171C3A'); G = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0); T = kmat('M_InteriorEvo2Trim', '#A9B0E2', 0.3, 0.5)
    St = kmat('M_InteriorEvo2Star', '#DDF0FF', emit='#DDF0FF'); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_cyl('T_Pad', 0, 0, 0.98, 0.0, TR_PAD, M, 48, r1=0.92),
         tr_torus('T_Rim', 0, 0, TR_PAD, 0.92, 0.035, G),
         tr_torus('T_Inner', 0, 0, TR_PAD + 0.004, 0.62, 0.014, A, 40, 6)]
    cons = [(0.2, 0.25), (0.42, 0.05), (0.3, -0.3), (-0.05, -0.45), (-0.38, -0.2), (-0.3, 0.3)]
    for i, (cx, cy) in enumerate(cons):
        o.append(tr_star('T_Star', cx, cy, TR_PAD + 0.006, 0.07 if i % 2 == 0 else 0.05, 0.025, 4, 0.01, St))
    for i in range(len(cons) - 1):
        (ax, ay), (bx, by) = cons[i], cons[i + 1]
        o.append(tr_tube('T_Con', [(ax, ay, TR_PAD + 0.003), (bx, by, TR_PAD + 0.003)], 0.006, G, 6))
    for k in range(4):
        a = math.radians(45 + 90 * k)
        o.append(tr_star('T_RimStar', 0.95 * math.cos(a), 0.95 * math.sin(a), TR_PAD + 0.01, 0.08, 0.03, 4, 0.02, G))
    H = 2.55
    for k in range(3):
        a = math.radians(30 + 120 * k)
        o.append(tr_tube('T_Strut', [(0.9 * math.cos(a), 0.9 * math.sin(a), TR_PAD), (0.84 * math.cos(a), 0.84 * math.sin(a), 1.5), (0.6 * math.cos(a), 0.6 * math.sin(a), H - 0.08)], 0.022, G, 8))
    o += [tr_torus('T_ArmA', 0, 0, H, 0.55, 0.022, G, rx=math.radians(90)),
          tr_torus('T_ArmB', 0, 0, H, 0.55, 0.022, G, rx=math.radians(90), rz=math.radians(90)),
          tr_torus('T_ArmC', 0, 0, H, 0.62, 0.026, G, rx=math.radians(23)),
          tr_torus('T_Band', 0, 0, H, 0.6, 0.012, T, rx=math.radians(-18), rz=math.radians(40)),
          tr_sphere('T_Core', 0, 0, H, 0.11, St),
          tr_star('T_TopStar', 0, 0, H + 0.68, 0.14, 0.055, 5, 0.05, St, upright_star=True),
          tr_box('T_KioskBase', 1.3, -0.7, 0.05, 0.36, 0.36, 0.1, M),
          tr_cyl('T_KioskStem', 1.3, -0.7, 0.06, 0.1, 1.0, G, 12)]
    scope = tr_cyl('T_Scope', 0, 0, 0.1, -0.33, 0.33, M, 20, r1=0.07)
    scope.data.transform(Matrix.Translation((1.3, 1.15, -0.7)) @ Matrix.Rotation(math.radians(30), 4, 'Y') @ Matrix.Rotation(math.radians(-35), 4, 'X'))
    o += [scope, tr_box('T_Screen', 1.3, -0.81, 0.92, 0.32, 0.012, 0.2, A, rx=math.radians(-25))]
    return o


def transit_stage3():
    """3단계 (행성 정원 + 빛과 수정): 수정 꽃 승강대 — 진주 원판 · 금 테 · 둥근 꽃잎 두 겹 (연꽃) · 금 덩굴 셋이 감아 오르며 위에 뜬 수정을 감쌈 (둥근 잎) · 수정 금 고리."""
    P = kmat('M_InteriorEvo3Pearl', '#F4EFE6', 0.3, 0.1); G = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0)
    C = kmat('M_InteriorEvo3Prism', '#CFF4FF', 0.05, 0.0, emit='#BFEFFF', alpha=0.6); L = kmat('M_InteriorLeaf', '#4E9A4A', 0.5)
    A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_cyl('T_Pad', 0, 0, 0.82, 0.0, TR_PAD, P, 48, r1=0.78),
         tr_cyl('T_PadInner', 0, 0, 0.6, TR_PAD - 0.004, TR_PAD + 0.006, A, 40),
         tr_torus('T_Rim', 0, 0, TR_PAD + 0.006, 0.62, 0.02, G)]
    for k in range(12):
        a = 2 * math.pi * (k + 0.5) / 12
        o.append(tr_blade('T_PetalOut', (0.66 * math.cos(a), 0.66 * math.sin(a), 0.07), (math.cos(a), math.sin(a), 0), 0.4, 0.34, 0.1, 0.14, P, 0.025))
    for k in range(12):
        a = 2 * math.pi * k / 12
        o.append(tr_blade('T_PetalIn', (0.6 * math.cos(a), 0.6 * math.sin(a), 0.12), (math.cos(a), math.sin(a), 0), 0.3, 0.27, 0.2, 0.16, P, 0.02))
    HC = 2.75
    for k in range(3):
        a = math.radians(90 + 120 * k)
        pts = []
        for t in range(21):
            u = t / 20
            rr = 0.82 * (1 - u) ** 0.9 + 0.2 * u + 0.08 * math.sin(math.pi * u * 2)
            aa = a + 0.6 * u
            pts.append((rr * math.cos(aa), rr * math.sin(aa), 0.1 + (HC - 0.45) * (u ** 0.9)))
        o.append(tr_tube('T_Vine', pts, 0.035, G, 8))
        for t in (4, 8, 12, 15):
            p = Vector(pts[t]); out = Vector((p.x, p.y, 0)).normalized()
            o.append(tr_blade('T_Leaf', p, out + Vector((0, 0, 0.9)), 0.24, 0.17, 0.05, 0.03, L, 0.012, 6))
    o += [tr_gem('T_Prism', 0, 0, HC, 0.24, 0.45, 0.4, C, 6),
          tr_torus('T_PrismRing', 0, 0, HC - 0.05, 0.3, 0.018, G)]
    return o


def transit_stage4():
    """4단계 (우주 · 별빛, 성운 남보라 · 은): 성운 승강대 — 은 원판 속 성운 판 · 둘레 네 별 · 위에 뜬 은 초승달 고리 둘이 엇갈림 (고리 위 별) · 가운데 별 · 별빛 안내 기둥 (오른쪽 앞)."""
    D = kmat('M_InteriorEvo4Deck', '#1B1740', 0.45); S = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0); N = kmat('M_InteriorEvo4Wall', '#2E2766', 0.35, 0.2)
    St = kmat('M_InteriorEvo2Star', '#DDF0FF', emit='#DDF0FF'); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_cyl('T_Pad', 0, 0, 0.96, 0.0, TR_PAD, S, 48, r1=0.9),
         tr_cyl('T_Neb', 0, 0, 0.74, TR_PAD - 0.004, TR_PAD + 0.008, N, 48),
         tr_torus('T_NebRim', 0, 0, TR_PAD + 0.008, 0.74, 0.015, S),
         tr_torus('T_NebGlow', 0, 0, TR_PAD + 0.01, 0.4, 0.01, A, 40, 6)]
    for k in range(4):
        a = math.radians(45 + 90 * k)
        o.append(tr_star('T_Star', 0.84 * math.cos(a), 0.84 * math.sin(a), TR_PAD + 0.012, 0.1, 0.03, 4, 0.025, St))
    H = 2.9
    o += [tr_torus('T_CrescentA', 0, 0, H, 0.8, 0.045, S, arc=0.62, a0=math.radians(10), rx=math.radians(12)),
          tr_torus('T_CrescentB', 0, 0, H + 0.12, 0.7, 0.038, S, arc=0.62, a0=math.radians(200), rx=math.radians(-12))]
    for k in range(5):
        a = math.radians(10 + 0.62 * 360 * k / 4)
        o.append(tr_star('T_CrStar', 0.8 * math.cos(a), 0.8 * math.sin(a), H + 0.8 * math.sin(a) * math.sin(math.radians(12)), 0.06, 0.02, 4, 0.02, St, upright_star=True))
    o += [tr_star('T_TopStar', 0, 0, H + 0.12, 0.13, 0.04, 4, 0.04, St, upright_star=True),
          tr_box('T_KioskBase', 1.3, -0.7, 0.05, 0.32, 0.32, 0.1, D),
          tr_cyl('T_KioskStem', 1.3, -0.7, 0.05, 0.1, 1.05, S, 12),
          tr_box('T_Screen', 1.3, -0.7, 1.22, 0.4, 0.012, 0.28, A, rz=math.radians(30), rx=math.radians(-15))]
    return o


TRANSITS = ((0, transit_basic), (1, transit_stage1), (2, transit_stage2), (3, transit_stage3), (4, transit_stage4))


def transit_preview(out_dir):
    """다섯 장치를 한 줄로 · 하나씩 근접 렌더 (사진 검토용, --transit-preview <폴더>) — 굽기 전 조각 (kmat 색) 그대로, z 위로 되돌려 놓음."""
    for o in list(SC.collection.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    xs = [0.0, 4.4, 8.8, 13.2, 17.6]
    tris = {}
    for (st, fn), x in zip(TRANSITS, xs):
        n = 0
        for ob in fn():
            ob.data.transform(Matrix.Translation((x, 0, 0)) @ UP_SWAP)
            solid(ob)
            n += sum(len(p.vertices) - 2 for p in ob.data.polygons)
        tris[st] = n
    print('TRIS', tris)
    box('Floor', (30, 10, 0.1), kmat('PreviewFloor', '#2A2D33', 0.8), at=(8.8, 0, -0.05))
    box('Wall', (30, 0.1, 6), kmat('PreviewWall', '#14161C', 0.9), at=(8.8, 3.2, 3))
    for x in xs:   # 사람 키 1.75 m 비교 기둥
        b = bmesh.new(); bmesh.ops.create_cone(b, cap_ends=True, segments=16, radius1=0.2, radius2=0.16, depth=1.42, matrix=Matrix.Translation((x - 1.45, 0.35, 0.71)))
        new_obj('Ref', b, [kmat('PreviewRef', '#80858F', 0.7)])
        b = bmesh.new(); bmesh.ops.create_uvsphere(b, u_segments=16, v_segments=8, radius=0.15); bmesh.ops.translate(b, vec=(x - 1.45, 0.35, 1.58), verts=b.verts)
        new_obj('RefHead', b, [kmat('PreviewRef', '#80858F', 0.7)])
    world = bpy.data.worlds.new('W'); SC.world = world; world.use_nodes = True
    bg = next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND')
    bg.inputs[0].default_value = (0.25, 0.27, 0.32, 1)
    for (lx, ly, lz, e) in ((3, -7, 6, 700), (14, -6, 5, 600), (8.8, -3, 8, 300)):
        ld = bpy.data.lights.new('L', 'AREA'); ld.energy = e; ld.size = 6
        lo = bpy.data.objects.new('L', ld); SC.collection.objects.link(lo); lo.location = (lx, ly, lz)
        lo.rotation_euler = (Vector((8.8, 0, 1.2)) - Vector((lx, ly, lz))).to_track_quat('-Z', 'Y').to_euler()
    try:
        SC.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        try:
            SC.render.engine = 'BLENDER_EEVEE'
        except TypeError:
            pass
    cam_d = bpy.data.cameras.new('C'); cam = bpy.data.objects.new('C', cam_d); SC.collection.objects.link(cam); SC.camera = cam
    def shot(name, eye, at, lens, rx, ry):
        SC.render.resolution_x, SC.render.resolution_y = rx, ry
        cam_d.lens = lens; cam.location = eye
        cam.rotation_euler = (Vector(at) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
        SC.render.filepath = os.path.join(out_dir, name + '.png')
        bpy.ops.render.render(write_still=True)
    shot('transit_all', (8.8, -19.0, 4.4), (8.8, 0, 1.55), 30, 2000, 760)
    for i, x in enumerate(xs):
        shot('transit_s%d' % i, (x + 2.0, -4.4, 2.3), (x + 0.15, 0, 1.55), 30, 900, 1000)
    print('PREVIEW DONE')


# ---------------- 벽 위아래 이동 장치 (2026-11-19, 사용자 "길을 막는다면 벽면에 배치" · 시안 "이대로 진행") ----------------
# 피벗 = 벽 면 · 바닥 (키트 로컬 z 0 = 벽 면, 방 쪽 = −z), 발판 = 반원 (반지름 약 0.95, 윗면 TR_PAD) — 서는 자리 = z −0.5. 빛 기둥 없음.
TRW_STAND = 0.5   # Unity InteriorTransit.WallStand 와 같음


def tr_halfdisk(name, r, z0, z1, m, segs=40):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segs, radius1=r, radius2=r, depth=z1 - z0, matrix=Matrix.Translation((0, 0, (z0 + z1) / 2)))
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, 0), plane_no=(0, 1, 0), clear_outer=True)
    bmesh.ops.holes_fill(bm, edges=bm.edges[:], sides=0)
    return _tr(new_obj(name, bm, [m]))


def tr_arc(name, cx, cz, R, r, m, a0, a1, y=-0.06, segs=28):
    return tr_tube(name, [(cx + R * math.cos(a0 + (a1 - a0) * i / segs), y, cz + R * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs + 1)], r, m, 8)


def wall_basic():
    """기본: 벽에 붙은 납작한 화물 승강기 — 벽판 · 경고 띠 테 · 노란 안내 레일 둘 · 1.4 × 1.0 발판 (홈 · 앞 경고 띠) · 옆 손잡이 · 위 모터 · 경광등 · 옆 호출판 ▲▼."""
    S = kmat('M_InteriorHull', '#8A9099', 0.45, 0.7); D = kmat('M_InteriorHullDark', '#2B2F35', 0.55, 0.6)
    Y = kmat('M_InteriorHazard', '#F2B81A', 0.45, 0.1); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_box('W_Back', 0, -0.02, 1.55, 1.6, 0.04, 2.9, S)]
    for i in range(15):
        z = 0.12 + i * 0.19
        for sx in (-0.86, 0.86):
            o.append(tr_box('W_FrameV', sx, -0.03, z + 0.095, 0.08, 0.04, 0.19, Y if i % 2 == 0 else D))
    for i in range(9):
        o.append(tr_box('W_FrameT', -0.86 + i * 0.215 + 0.1, -0.03, 3.0, 0.215, 0.04, 0.08, Y if i % 2 == 0 else D))
    for sx in (-0.62, 0.62):
        o.append(tr_box('W_Rail', sx, -0.1, 1.5, 0.08, 0.1, 2.9, Y))
        for z in (0.5, 1.5, 2.5):
            o.append(tr_box('W_Bracket', sx, -0.05, z, 0.16, 0.06, 0.08, D))
    o += [tr_box('W_Pad', 0, -0.55, TR_PAD / 2, 1.4, 1.0, TR_PAD, D), tr_box('W_PadTop', 0, -0.55, TR_PAD + 0.002, 1.3, 0.9, 0.006, S)]
    for k in range(-4, 5):
        o.append(tr_box('W_Tread', k * 0.14, -0.55, TR_PAD + 0.007, 0.022, 0.8, 0.008, D))
    for i in range(7):
        o.append(tr_box('W_Edge', -0.6 + i * 0.2 + 0.1, -1.03, TR_PAD + 0.003, 0.2, 0.06, 0.012, Y if i % 2 == 0 else D))
    for sx in (-0.68, 0.68):
        o.append(tr_tube('W_SideBar', [(sx, -0.12, 1.05), (sx, -0.95, 1.05), (sx, -0.95, TR_PAD)], 0.03, Y))
        o.append(tr_tube('W_SideMid', [(sx, -0.12, 0.6), (sx, -0.95, 0.6)], 0.022, Y))
    o += [tr_box('W_Motor', 0, -0.22, 2.95, 1.1, 0.4, 0.3, S), tr_box('W_MotorFace', 0, -0.43, 2.95, 0.9, 0.02, 0.18, D),
          tr_box('W_Beacon', 0.45, -0.3, 3.15, 0.12, 0.12, 0.1, A),
          tr_box('W_CallPlate', 1.15, -0.03, 1.15, 0.26, 0.04, 0.5, Y), tr_box('W_CallFace', 1.15, -0.055, 1.15, 0.18, 0.01, 0.38, D)]
    for z, up in ((1.25, True), (1.05, False)):
        s = 1 if up else -1
        o.append(tr_tri('W_Arrow', [(1.15 - 0.06, -0.062, z - 0.04 * s), (1.15, -0.062, z + 0.05 * s), (1.15 + 0.06, -0.062, z - 0.04 * s)][::s], A))
    return o


def wall_stage1():
    """1단계: 흰 세라믹 — 둥근 윗머리 벽판 · 빛 테 (강조색) · 흰 레일 · 반원 발판 + 빛 고리 · 홀로그램 판."""
    W = kmat('M_InteriorEvoWhite', '#ECEFF3', 0.25); F = kmat('M_InteriorEvoFloor', '#9EA3AD', 0.4); A = kmat('M_Accent_Core', '#7FE8FF', emit='#7FE8FF')
    o = [tr_box('W_Plate', 0, -0.02, 1.35, 1.5, 0.04, 2.7, W), tr_arc('W_PlateTop', 0, 2.7, 0.75, 0.06, W, 0, math.pi, y=-0.04)]
    for sx in (-0.78, 0.78):
        o += [tr_box('W_Glow', sx, -0.05, 1.35, 0.03, 0.02, 2.7, A), tr_box('W_Rail', sx * 0.82, -0.1, 1.45, 0.07, 0.08, 2.9, W)]
    o += [tr_arc('W_GlowTop', 0, 2.7, 0.78, 0.015, A, 0, math.pi),
          tr_halfdisk('W_Pad', 0.95, 0.0, TR_PAD, W), tr_halfdisk('W_PadIn', 0.7, TR_PAD - 0.004, TR_PAD + 0.006, F),
          tr_arc('W_PadGlow', 0, 0, 0.74, 0.018, A, math.pi, 2 * math.pi, y=0)]
    o[-1].data.transform(Matrix.Translation((0, TR_PAD + 0.008, 0)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    o.append(tr_box('W_Holo', 1.0, -0.06, 1.25, 0.32, 0.02, 0.24, A))
    return o


def wall_stage2():
    """2단계: 미드나잇 · 금 — 금 테 벽판 · 별자리 · 반원 발판 + 금 테 · 위 혼천의 반쪽 + 별."""
    M = kmat('M_InteriorEvo2Deck', '#171C3A'); G = kmat('M_InteriorEvo2Gold', '#D9AE4E', 0.25, 1.0); St = kmat('M_InteriorEvo2Star', '#DDF0FF', emit='#DDF0FF')
    o = [tr_box('W_Plate', 0, -0.02, 1.45, 1.5, 0.04, 2.7, M), tr_arc('W_Arch', 0, 2.8, 0.75, 0.05, G, 0, math.pi)]
    for sx in (-0.76, 0.76):
        o.append(tr_box('W_Frame', sx, -0.05, 1.45, 0.05, 0.03, 2.7, G))
    for (sx, sz) in ((-0.3, 2.3), (0.1, 2.5), (0.35, 2.1), (-0.15, 1.9), (0.25, 1.6)):
        o.append(tr_star('W_Star', sx, -0.05, sz, 0.06, 0.022, 4, 0.01, St, upright_star=True))
    o += [tr_halfdisk('W_Pad', 0.98, 0.0, TR_PAD, M), tr_arc('W_Rim', 0, 0, 0.92, 0.035, G, math.pi, 2 * math.pi, y=0)]
    o[-1].data.transform(Matrix.Translation((0, TR_PAD, 0)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    for a in (0.0, 0.5):
        o.append(tr_arc('W_Arm', 0, 3.05, 0.42, 0.02, G, a, math.pi - a, y=-0.1))
    o.append(tr_star('W_TopStar', 0, -0.1, 3.55, 0.13, 0.05, 5, 0.04, St, upright_star=True))
    return o


def wall_stage3():
    """3단계: 진주 · 금 · 수정 — 진주 아치 벽판 · 금 덩굴 테 + 잎 · 반원 연꽃 발판 · 위 수정."""
    P = kmat('M_InteriorEvo3Pearl', '#F4EFE6', 0.3, 0.1); G = kmat('M_InteriorEvo3Gold', '#D9AE4E', 0.25, 1.0)
    C = kmat('M_InteriorEvo3Prism', '#CFF4FF', 0.05, 0.0, emit='#BFEFFF', alpha=0.6); L = kmat('M_InteriorLeaf', '#4E9A4A', 0.5)
    o = [tr_box('W_Plate', 0, -0.02, 1.35, 1.4, 0.04, 2.5, P), tr_arc('W_Arch', 0, 2.6, 0.7, 0.05, P, 0, math.pi, y=-0.04)]
    for sx in (-1, 1):
        pts = [(sx * (0.74 + 0.05 * math.sin(t * 0.9)), -0.07, 0.1 + t * 0.12) for t in range(22)]
        o.append(tr_tube('W_Vine', pts, 0.03, G, 8))
        for t in range(3, 21, 4):
            p = Vector(pts[t])
            o.append(tr_blade('W_Leaf', p, Vector((sx, -0.6, 0.5)), 0.22, 0.15, 0.04, 0.03, L, 0.012, 6))
    o.append(tr_halfdisk('W_Pad', 0.85, 0.0, TR_PAD, P))
    for k in range(7):
        a = math.pi + math.pi * (k + 0.5) / 7
        o.append(tr_blade('W_Petal', (0.62 * math.cos(a), 0.62 * math.sin(a), 0.07), (math.cos(a), math.sin(a), 0), 0.38, 0.32, 0.1, 0.14, P, 0.025))
    o += [tr_gem('W_Crystal', 0, -0.2, 3.1, 0.18, 0.32, 0.3, C, 6), tr_arc('W_CrystalRing', 0, 0, 0.24, 0.015, G, 0, 2 * math.pi, y=0)]
    o[-1].data.transform(Matrix.Translation((0, 3.05, -0.2)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    return o


def wall_stage4():
    """4단계: 성운 · 은 — 은 테 성운 벽판 · 별 · 반원 은 발판 + 성운 판 · 위 은 초승달 + 별."""
    Sv = kmat('M_InteriorEvo4Trim', '#CDD3DE', 0.2, 1.0); N = kmat('M_InteriorEvo4Wall', '#2E2766', 0.35, 0.2); St = kmat('M_InteriorEvo2Star', '#DDF0FF', emit='#DDF0FF')
    o = [tr_box('W_Plate', 0, -0.02, 1.45, 1.5, 0.04, 2.7, N), tr_box('W_FrameTop', 0, -0.05, 2.8, 1.58, 0.04, 0.06, Sv)]
    for sx in (-0.76, 0.76):
        o.append(tr_box('W_Frame', sx, -0.05, 1.45, 0.06, 0.04, 2.7, Sv))
    for (sx, sz) in ((-0.35, 2.4), (0.3, 2.2), (0.0, 1.8), (-0.25, 1.4), (0.35, 1.2)):
        o.append(tr_star('W_Star', sx, -0.05, sz, 0.07, 0.02, 4, 0.01, St, upright_star=True))
    o += [tr_halfdisk('W_Pad', 0.96, 0.0, TR_PAD, Sv), tr_halfdisk('W_Neb', 0.74, TR_PAD - 0.004, TR_PAD + 0.008, N),
          tr_arc('W_Crescent', 0, 3.05, 0.55, 0.04, Sv, 0.3, math.pi - 0.3, y=-0.1),
          tr_star('W_TopStar', 0, -0.1, 3.3, 0.12, 0.035, 4, 0.03, St, upright_star=True)]
    return o


TRANSIT_WALLS = ((0, wall_basic), (1, wall_stage1), (2, wall_stage2), (3, wall_stage3), (4, wall_stage4))


def wall_preview(out_dir):
    """벽 장치 다섯 미리보기 (--wall-preview <폴더>)."""
    for o in list(SC.collection.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    xs = [0.0, 3.2, 6.4, 9.6, 12.8]
    for (st, fn), x in zip(TRANSIT_WALLS, xs):
        for ob in fn():
            ob.data.transform(Matrix.Translation((x, 0, 0)) @ UP_SWAP)
            solid(ob)
    box('Wall', (17, 0.2, 3.6), kmat('PreviewWall', '#8C9096', 0.7), at=(6.4, 0.1, 1.8))
    box('Floor', (17, 4.2, 0.1), kmat('PreviewFloor', '#6B6F76', 0.8), at=(6.4, -2, -0.05))
    world = bpy.data.worlds.new('W'); SC.world = world; world.use_nodes = True
    next(n for n in world.node_tree.nodes if n.type == 'BACKGROUND').inputs[0].default_value = (0.25, 0.27, 0.32, 1)
    for (lx, ly, lz, e) in ((0, -6, 5, 700), (8, -6, 5, 700), (14, -5, 4, 500)):
        ld = bpy.data.lights.new('L', 'AREA'); ld.energy = e; ld.size = 5
        lo = bpy.data.objects.new('L', ld); SC.collection.objects.link(lo); lo.location = (lx, ly, lz)
        lo.rotation_euler = (Vector((lx, 0, 1.4)) - Vector((lx, ly, lz))).to_track_quat('-Z', 'Y').to_euler()
    try:
        SC.render.engine = 'BLENDER_EEVEE_NEXT'
    except TypeError:
        try:
            SC.render.engine = 'BLENDER_EEVEE'
        except TypeError:
            pass
    cd = bpy.data.cameras.new('C'); cam = bpy.data.objects.new('C', cd); SC.collection.objects.link(cam); SC.camera = cam
    SC.render.resolution_x, SC.render.resolution_y = 2000, 700
    cd.lens = 30; eye = (6.4, -13.5, 2.6); cam.location = eye; cam.rotation_euler = (Vector((6.4, 0, 1.6)) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    SC.render.filepath = os.path.join(out_dir, 'wallkit_all.png'); bpy.ops.render.render(write_still=True)
    eye = (2.0, -6.5, 3.0); cam.location = eye; cam.rotation_euler = (Vector((3.2, 0, 1.2)) - Vector(eye)).to_track_quat('-Z', 'Y').to_euler()
    SC.render.resolution_x, SC.render.resolution_y = 1400, 800
    SC.render.filepath = os.path.join(out_dir, 'wallkit_side.png'); bpy.ops.render.render(write_still=True)
    print('WALL PREVIEW DONE')


def export_kit():
    out = []
    for st, fn in ((1, kit_tube_evo1), (2, kit_tube_evo2), (3, lambda: kit_bridge(3)), (4, lambda: kit_bridge(4))):
        tube, collar, ring, ves = fn()
        out.append(bake_kit('KIT_Tube_Evo%d' % st, tube))
        out.append(bake_kit('KIT_TubeCollar_Evo%d' % st, collar))
        if ring:
            out.append(bake_kit('KIT_TubeRing_Evo%d' % st, ring))
        if ves:
            out.append(bake_kit('KIT_Vestibule_Evo%d' % st, ves))
        if st >= 2:
            out.append(bake_kit('KIT_TubeShort_Evo%d' % st, kit_short(st)))
            pad, ring = kit_skylift(st)
            out.append(bake_kit('KIT_SkyLiftPad_Evo%d' % st, pad))
            out.append(bake_kit('KIT_SkyLiftRing_Evo%d' % st, ring))
            out.append(bake_kit('KIT_DoorSteps_Evo%d' % st, kit_door_steps(st)))
    for st, fn in TRANSITS:   # 위아래 이동 장치 (2026-11-19)
        out.append(bake_kit('KIT_Transit_Evo%d' % st, fn()))
    for st, fn in TRANSIT_WALLS:   # 벽 위아래 이동 장치
        out.append(bake_kit('KIT_TransitWall_Evo%d' % st, fn()))
    frame, leaf = kit_airlock()
    out.append(bake_kit('KIT_AirlockFrame', frame))
    out.append(bake_kit('KIT_AirlockLeaf', leaf))
    for ob in list(SC.collection.objects):
        ob.select_set(ob in out)
    bpy.ops.export_scene.fbx(filepath=KIT_FBX, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
    for ob in out:
        print('KIT', ob.name, len(ob.data.polygons), [m.name for m in ob.data.materials], [round(v, 3) for v in ob.dimensions])
    print('EXPORTED', KIT_FBX)


if EXPORT_KIT:
    export_kit()
if '--transit-preview' in argv:
    transit_preview(argv[argv.index('--transit-preview') + 1])
if '--wall-preview' in argv:
    wall_preview(argv[argv.index('--wall-preview') + 1])
