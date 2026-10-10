# 11-11d 동물 주민 (2026-10-10): 두 발로 서는 2~3등신 로우폴리 동물. 몸 · 리그는 모든 종류가 공유, 머리 · 귀 · 꼬리 · 털색만 종류별
# 사용: blender -b --factory-startup -P BlenderWork/Animal/animal_builder.py -- <종류> [fbx 출력 경로]
#   → BlenderWork/Animal/animal_<종류>.blend + 미리보기 BlenderWork/Animal/preview/<종류>_*.png (+ fbx)
# 좌표: 미터, 발바닥 z = 0, 앞 = −y, 캐릭터 왼쪽 = +x. 본 이름 = Unity 사람형 자동 매핑 이름 (ResidentPoser 재사용)
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
KIND = argv[0] if argv else 'rabbit'
FBX = argv[1] if len(argv) > 1 else None
# 음영: smooth = 부드러운 음영 + 분할 1.5배 (2026-10-10 사용자 결정 — 말랑한 장난감 느낌, 실루엣은 단순하게) / flat = 면이 보이는 각진 로우폴리(비교용)
SHADE = os.environ.get('ANIMAL_SHADE', 'smooth')
SEGK = 1.5 if SHADE == 'smooth' else 1.0
TAG = KIND + ('' if SHADE == 'smooth' else '_' + SHADE)

# ---- 공유 몸 치수 ----
HIP_Z = 0.24          # 다리 시작 (0.30은 다리가 길어 인형 탈처럼 보였음)
SH = Vector((0.135, 0.0, 0.47))   # 왼어깨 관절
HEAD_C = Vector((0, 0, 0.71))     # 머리 가운데
HEAD_R = (0.20, 0.18, 0.185)      # 머리 반지름 (옆 · 앞뒤 · 위아래)

# ---- 색 (Unity에서 털색은 MaterialPropertyBlock로 바꿈 — 재질 이름이 슬롯) ----
#   Fur = 주 털(Unity에서 색 바꿈) · FurLight = 배 · 주둥이 · 귀 안쪽 밝은 털 · Pink = 코 · 볼 · Dark = 무늬 · 코(어두운) · Beak = 부리 · 물갈퀴
#   슬롯은 모든 종류가 같은 순서 (쓰지 않는 슬롯도 둠 — Unity에서 슬롯 번호로 색을 바꿀 수 있게)
SPECIES = {
    'rabbit': dict(fur=(0.93, 0.90, 0.86), light=(1.0, 0.99, 0.97), pink=(0.95, 0.62, 0.66), dark=(0.25, 0.2, 0.2)),
    'cat': dict(fur=(0.96, 0.72, 0.42), light=(1.0, 0.96, 0.9), pink=(0.95, 0.6, 0.62), dark=(0.78, 0.48, 0.25)),
    'dog': dict(fur=(0.88, 0.72, 0.5), light=(1.0, 0.97, 0.92), pink=(0.93, 0.6, 0.6), dark=(0.5, 0.33, 0.22)),
    'bear': dict(fur=(0.56, 0.39, 0.26), light=(0.88, 0.75, 0.58), pink=(0.92, 0.58, 0.55), dark=(0.16, 0.11, 0.09)),
    'fox': dict(fur=(0.95, 0.52, 0.2), light=(1.0, 0.98, 0.95), pink=(0.93, 0.58, 0.55), dark=(0.2, 0.14, 0.12)),
    'panda': dict(fur=(0.97, 0.97, 0.95), light=(1.0, 1.0, 1.0), pink=(0.95, 0.62, 0.66), dark=(0.13, 0.13, 0.14)),
    'koala': dict(fur=(0.63, 0.64, 0.68), light=(0.96, 0.95, 0.95), pink=(0.93, 0.62, 0.66), dark=(0.18, 0.18, 0.2)),
    'penguin': dict(fur=(0.17, 0.2, 0.28), light=(1.0, 1.0, 0.99), pink=(0.95, 0.62, 0.66), dark=(0.1, 0.1, 0.12), beak=(1.0, 0.64, 0.18)),
}
KINDS = list(SPECIES)
SP = SPECIES[KIND]
COLORS = {'Fur': SP['fur'], 'FurLight': SP['light'], 'Pink': SP['pink'],
          'Eye': (0.05, 0.04, 0.05), 'EyeHi': (1, 1, 1), 'Dark': SP['dark'], 'Beak': SP.get('beak', (1.0, 0.64, 0.18))}

bpy.ops.wm.read_factory_settings(use_empty=True)
MATS = {}
for n, c in COLORS.items():
    m = bpy.data.materials.new('M_Animal_' + n); m.use_nodes = True
    b = next(x for x in m.node_tree.nodes if x.type == 'BSDF_PRINCIPLED')
    b.inputs['Base Color'].default_value = c + (1,); b.inputs['Roughness'].default_value = 0.25 if n.startswith('Eye') else 0.75
    m.diffuse_color = c + (1,)
    MATS[n] = m
MAT_ORDER = list(COLORS)

PARTS = []   # (bmesh, 재질 이름, 본 가중치 함수(점 → {본: 가중치}))


def orient_outward(bm):
    """닫힌 덩어리의 부호 있는 부피가 음수면 면을 뒤집음 (recalc_face_normals를 믿지 않음 — MODELING.md 5)."""
    bm.faces.ensure_lookup_table()
    vol = 0.0
    for f in bm.faces:
        vs = [v.co for v in f.verts]
        for i in range(1, len(vs) - 1): vol += vs[0].dot(vs[i].cross(vs[i + 1])) / 6
    if vol < 0:
        for f in bm.faces: f.normal_flip()


def ellipsoid(c, r, seg=14, rings=9, rot=None, mat='Fur', w=None):
    bm = bmesh.new()
    seg = int(round(seg * SEGK)); rings = int(round(rings * SEGK))
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)
    M = Matrix.Translation(c) @ (rot.to_4x4() if rot else Matrix.Identity(4)) @ Matrix.Diagonal((r[0], r[1], r[2], 1))
    bmesh.ops.transform(bm, matrix=M, verts=bm.verts)
    orient_outward(bm)
    PARTS.append((bm, mat, w))
    return bm


def capsule(p0, p1, r0, r1, seg=10, rings=6, mat='Fur', w=None, flat=1.0):
    """p0 → p1 관 (반지름 r0 → r1) + 양 끝 반구. flat = 옆(관 기준 첫 축) 대비 두께 비."""
    ax = (p1 - p0); L = ax.length; u = ax / L
    e1 = u.orthogonal().normalized(); e2 = u.cross(e1)
    if abs(e1.z) > abs(e2.z): e1, e2 = e2, e1      # e1 ≈ 수평
    seg = int(round(seg * SEGK)); rings = int(round(rings * SEGK))
    bm = bmesh.new()
    prof = []   # (축 위치, 반지름)
    for i in range(rings + 1):                     # p0 반구
        a = math.pi / 2 * (1 - i / rings); prof.append((-r0 * math.sin(a), r0 * math.cos(a)))
    for i in range(1, 4): t = i / 4; prof.append((L * t, r0 + (r1 - r0) * t))
    for i in range(rings + 1):                     # p1 반구
        a = math.pi / 2 * i / rings; prof.append((L + r1 * math.sin(a), r1 * math.cos(a)))
    loops = []
    for s, r in prof:
        if r < 1e-5:
            loops.append([bm.verts.new(p0 + u * s)]); continue
        loops.append([bm.verts.new(p0 + u * s + (e1 * math.cos(2 * math.pi * k / seg) + e2 * math.sin(2 * math.pi * k / seg) * flat) * r) for k in range(seg)])
    for a, b in zip(loops, loops[1:]):
        if len(a) == 1:
            for k in range(seg): bm.faces.new((a[0], b[(k + 1) % seg], b[k]))
        elif len(b) == 1:
            for k in range(seg): bm.faces.new((a[k], a[(k + 1) % seg], b[0]))
        else:
            for k in range(seg): bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    orient_outward(bm)
    PARTS.append((bm, mat, w))
    return bm


def limb(pts, rads, seg=10, cap=6, step=0.012, mat='Fur', w=None, flat=1.0):
    """꺾인 선(pts, 마디마다 반지름 rads)을 따라 한 덩어리 관 + 양 끝 반구. 팔꿈치 · 무릎을 두 관으로 겹치면
    부드러운 음영에서 겹친 선이 지그재그로 보였음 (Unity 근접) → 한 관으로 이어 꺾음."""
    seg = int(round(seg * SEGK)); cap = int(round(cap * SEGK))
    segs = [(a, b, (b - a).length) for a, b in zip(pts, pts[1:])]
    total = sum(s[2] for s in segs)
    acc = [0.0]
    for s in segs: acc.append(acc[-1] + s[2])

    def at(t):
        """호 길이 t → (중심, 매끈한 진행 방향, 반지름)."""
        t = min(max(t, 0.0), total)
        i = next(k for k in range(len(segs)) if t <= acc[k + 1] + 1e-9)
        a, b, L = segs[i]; f = (t - acc[i]) / L
        c = a + (b - a) * f; r = rads[i] + (rads[i + 1] - rads[i]) * f
        d = (b - a).normalized()
        for k in (i - 1, i + 1):                     # 마디 근처는 이웃 마디 방향과 섞음
            if 0 <= k < len(segs):
                dist = abs(t - acc[max(i, k)])
                if dist < 0.04: d = d.lerp((segs[k][1] - segs[k][0]).normalized(), 0.5 * (1 - dist / 0.04)).normalized()
        return c, d, r
    ref = Vector((0, -1, 0))
    bm = bmesh.new()

    def ring(c, d, r):
        rf = ref if abs(d.dot(ref)) < 0.9 else Vector((0, 0, 1))   # 앞으로 뻗는 관(부리)은 위를 기준으로
        e2 = (rf - d * rf.dot(d)).normalized(); e1 = d.cross(e2)
        return [bm.verts.new(c + (e1 * math.cos(2 * math.pi * k / seg) + e2 * math.sin(2 * math.pi * k / seg) * flat) * r) for k in range(seg)]
    loops = []
    c0, d0, r0 = at(0.0)
    loops.append([bm.verts.new(c0 - d0 * r0)])
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
    orient_outward(bm)
    PARTS.append((bm, mat, w))
    return bm


def on_head(yaw, pitch, out=0.0):
    """머리 타원체 겉면 점 (yaw = 앞에서 옆으로(+ = 캐릭터 왼쪽), pitch = 위로) + 바깥으로 out."""
    d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    k = 1.0 / math.sqrt((d.x / HEAD_R[0]) ** 2 + (d.y / HEAD_R[1]) ** 2 + (d.z / HEAD_R[2]) ** 2)
    p = HEAD_C + d * k
    n = Vector(((p.x - HEAD_C.x) / HEAD_R[0] ** 2, (p.y - HEAD_C.y) / HEAD_R[1] ** 2, (p.z - HEAD_C.z) / HEAD_R[2] ** 2)).normalized()
    return p + n * out, n


def ss(t):
    t = min(1.0, max(0.0, t)); return t * t * (3 - 2 * t)


# ---- 가중치 함수 ----
def W_head(p): return {'Head': 1.0}


def W_torso(p):
    t1 = ss((p.z - 0.30) / 0.08); t2 = ss((p.z - 0.40) / 0.08)
    return {'Hips': 1 - t1, 'Spine': t1 * (1 - t2), 'Chest': t2}


def W_limb(b0, b1, j, sp):
    """관절 j(위치)에서 b0 → b1로 넘어감 (sp = 섞는 폭)."""
    def f(p):
        t = ss(((p - j).dot(sp[1]) + sp[0]) / (2 * sp[0]))
        return {b0: 1 - t, b1: t}
    return f


# ---- 공유 몸 ----
ellipsoid(Vector((0, 0.005, 0.37)), (0.155, 0.13, 0.17), seg=14, rings=9, w=W_torso)           # 몸통 (달걀형)
TORSO = (Vector((0, 0.005, 0.37)), (0.155, 0.13, 0.17))


def surface_patch(C, R, cx, cz, ax, az, out=0.0025, inn=0.003, nr=6, nt=28, mat='FurLight', w=None, ang=0.0, back=False):
    """타원체(C, R) 앞면 위의 타원 무늬(가운데 cx · cz, 반지름 ax · az)를 얇은 판으로 (겉 = 표면 + out, 속 = 표면 − inn, 옆띠로 닫음).
    무늬를 타원체로 겹쳐 놓으면 몸통과 얕은 각도로 만나 테두리가 톱니처럼 보였음 (Unity 근접)."""
    sgn = 1 if back else -1                            # back = 뒷면(+y)에 붙임

    def surf(x, z, off):
        u = 1 - ((x - C.x) / R[0]) ** 2 - ((z - C.z) / R[2]) ** 2
        p = Vector((x, C.y + sgn * R[1] * math.sqrt(max(0.0, u)), z))
        n = Vector(((p.x - C.x) / R[0] ** 2, (p.y - C.y) / R[1] ** 2, (p.z - C.z) / R[2] ** 2)).normalized()
        return p + n * off
    ca, sa = math.cos(ang), math.sin(ang)

    def pt(i, j, off):
        a_ = ax * (i / nr) * math.cos(2 * math.pi * j / nt); b_ = az * (i / nr) * math.sin(2 * math.pi * j / nt)
        return surf(cx + a_ * ca - b_ * sa, cz + a_ * sa + b_ * ca, off)
    bm = bmesh.new()
    nt = int(round(nt * SEGK))
    layers = []
    for off in (out, -inn):
        cen = bm.verts.new(surf(cx, cz, off))
        rings = [[bm.verts.new(pt(i, j, off)) for j in range(nt)] for i in range(1, nr + 1)]
        layers.append((cen, rings))
    for li, (cen, rings) in enumerate(layers):
        fl = li == 1                                   # 속 면은 반대로 감음
        for j in range(nt):
            a, b = rings[0][j], rings[0][(j + 1) % nt]
            bm.faces.new((cen, a, b) if not fl else (cen, b, a))
        for r0, r1 in zip(rings, rings[1:]):
            for j in range(nt):
                q = (r0[j], r0[(j + 1) % nt], r1[(j + 1) % nt], r1[j])
                bm.faces.new(q[::-1] if not fl else q)
    o_, i_ = layers[0][1][-1], layers[1][1][-1]          # 옆띠
    for j in range(nt):
        bm.faces.new((i_[j], i_[(j + 1) % nt], o_[(j + 1) % nt], o_[j]))
    orient_outward(bm)
    PARTS.append((bm, mat, w))


# ---- 종류별 설정 ----
BELLY = {'penguin': (0.0, 0.36, 0.125, 0.155), 'fox': (0.0, 0.40, 0.075, 0.09), 'panda': (0.0, 0.35, 0.0, 0.0)}.get(KIND, (0.0, 0.35, 0.10, 0.12))
LIMB_MAT = 'Dark' if KIND == 'panda' else 'Fur'     # 판다 = 팔다리 검정


def W_part(b): return lambda p: {b: 1.0}


# 11-16 생명감용 덧붙인 본 (사람형 22본 밖, Unity에서 코드로 돌림): 귀(처짐 · 쫑긋) · 꼬리(흔들기) · 눈(깜빡임 = 세로로 납작하게)
EXTRA = {}   # 본 이름 → (머리 끝, 꼬리 끝, 부모)


def W_soft(b, parent, base, axis, start=-0.01, width=0.05):
    """뿌리(base)에서 axis 방향으로 갈수록 parent → b. 뿌리는 부모에 붙어 있어 돌려도 머리 · 엉덩이와 틈이 생기지 않음."""
    def f(p):
        t = ss(((p - base).dot(axis) - start) / width)
        return {parent: 1 - t, b: t}
    return f


def rz(deg): return Matrix.Rotation(math.radians(deg), 3, 'Z')


if BELLY[2] > 0:
    surface_patch(TORSO[0], TORSO[1], BELLY[0], BELLY[1], BELLY[2], BELLY[3], w=W_torso)   # 배 무늬
ellipsoid(Vector((0, 0, 0.525)), (0.075, 0.07, 0.05), seg=10, rings=6, w=lambda p: {'Chest': 0.5, 'Neck': 0.5})   # 목 (머리 · 몸 사이 메움)
for sx, side in ((1, 'Left'), (-1, 'Right')):
    sh = Vector((SH.x * sx, SH.y, SH.z))
    el = sh + Vector((0.10 * sx, 0, -0.10))            # 팔꿈치 (A자세 45°)
    wr = el + Vector((0.07 * sx, -0.01, -0.085))        # 손목
    u = (wr - sh).normalized()
    if KIND == 'penguin':                               # 날개: 납작한 지느러미, 끝이 가늘게
        limb([sh, el, wr + u * 0.05], [0.045, 0.05, 0.012], flat=0.35, w=W_limb(side + 'UpperArm', side + 'LowerArm', el, (0.02, u)))
    else:
        limb([sh, el, wr], [0.042, 0.038, 0.036], mat=LIMB_MAT, w=W_limb(side + 'UpperArm', side + 'LowerArm', el, (0.02, u)))
        ellipsoid(wr + u * 0.03, (0.045, 0.04, 0.045), seg=10, rings=7, mat=LIMB_MAT, w=W_part(side + 'Hand'))   # 손 (뭉툭한 앞발)
    hp = Vector((0.075 * sx, 0, HIP_Z + 0.03)); kn = Vector((0.08 * sx, -0.01, 0.145)); an = Vector((0.08 * sx, 0, 0.065))
    limb([hp, kn, an], [0.068, 0.06, 0.055], mat=LIMB_MAT, w=W_limb(side + 'UpperLeg', side + 'LowerLeg', kn, (0.03, (an - hp).normalized())))
    # 발
    if KIND == 'rabbit':                                # 큰 뒷발 (앞으로 긴 타원) + 분홍 발끝
        ellipsoid(Vector((0.085 * sx, -0.045, 0.035)), (0.055, 0.105, 0.04), seg=12, rings=7, w=W_part(side + 'Foot'))
        ellipsoid(Vector((0.085 * sx, -0.10, 0.02)), (0.035, 0.04, 0.012), seg=10, rings=5, mat='Pink', w=W_part(side + 'Foot'))
    elif KIND == 'penguin':                             # 주황 물갈퀴
        ellipsoid(Vector((0.085 * sx, -0.05, 0.018)), (0.055, 0.09, 0.018), seg=12, rings=6, mat='Beak', w=W_part(side + 'Foot'))
    else:                                               # 둥근 발
        ellipsoid(Vector((0.083 * sx, -0.03, 0.038)), (0.058, 0.08, 0.04), seg=12, rings=7, mat=LIMB_MAT, w=W_part(side + 'Foot'))

# ---- 머리 (모든 종류 같은 두상) ----
ellipsoid(HEAD_C, HEAD_R, seg=16, rings=11, w=W_head)


def head_disc(yaw, pitch, r, mat, out=-0.01, lift=0.0, turn=0.0, inner=None, ear=None):
    """머리 위 · 옆에 세운 둥근 귀 (앞을 보는 납작한 원반) + 안쪽 밝은 원반. ear = 'Left' / 'Right'면 귀 본(아래쪽 뿌리 기준)."""
    p, n = on_head(yaw, pitch, out)
    p = p + Vector((0, 0, lift))
    rot = rz(turn)
    w = W_head
    if ear:
        base = p - Vector((0, 0, r[2] * 0.6)); up = Vector((0, 0, 1))
        w = W_soft(ear + 'Ear', 'Head', base, up, start=0.0, width=r[2] * 0.8)
        EXTRA[ear + 'Ear'] = (base, base + up * r[2] * 1.6, 'Head')
    ellipsoid(p, r, seg=14, rings=8, rot=rot, mat=mat, w=w)
    if inner:
        fw = rot @ Vector((0, -1, 0))
        ellipsoid(p + fw * (r[1] * 0.55), (r[0] * 0.62, r[1] * 0.5, r[2] * 0.62), seg=12, rings=7, rot=rot, mat=inner, w=w)


def pointy_ear(sx, yaw, pitch, length, width, out_deg, back_deg, inner='FurLight', tip=None):
    """세모 귀: 머리에서 끝이 뾰족한 납작한 관 + 앞쪽 안쪽. 귀 본 = 뿌리 → 끝."""
    base, n = on_head(yaw * sx, pitch, -0.03)
    tilt = Matrix.Rotation(math.radians(out_deg * sx), 3, 'Y') @ Matrix.Rotation(math.radians(-back_deg), 3, 'X')
    up = tilt @ Vector((0, 0, 1)); fw = (tilt @ Vector((0, -1, 0))).normalized()
    tipp = base + up * length
    side = 'Left' if sx > 0 else 'Right'
    w = W_soft(side + 'Ear', 'Head', base, up, start=0.02, width=0.04)   # 머리 속에 묻힌 뿌리는 머리에
    EXTRA[side + 'Ear'] = (base, tipp, 'Head')
    limb([base, tipp], [width, 0.004], cap=4, flat=0.45, w=w)
    limb([base + up * 0.025 + fw * 0.012, tipp - up * 0.03 + fw * 0.012], [width * 0.6, 0.003], cap=3, flat=0.3, mat=inner, w=w)
    if tip:
        limb([base + up * (length * 0.72), tipp + up * 0.002], [width * 0.33, 0.005], cap=3, flat=0.5, mat=tip, w=w)


def nose(p, r, mat='Dark'):
    ellipsoid(p, r, seg=10, rings=6, mat=mat, w=W_head)


for sx, side in ((1, 'Left'), (-1, 'Right')):
    # 눈: 검은 반짝이는 타원 + 흰 반사점 (모든 종류)
    ep, en = on_head(0.36 * sx, 0.02, -0.006)
    W_eye = W_part(side + 'Eye')                        # 11-16 눈 본: 세로로 납작하게 해서 깜빡임 (반사점도 함께)
    EXTRA[side + 'Eye'] = (ep, ep + en * 0.04, 'Head')
    ellipsoid(ep, (0.028, 0.012, 0.036), seg=12, rings=7, rot=en.to_track_quat('-Y', 'Z').to_matrix(), mat='Eye', w=W_eye)
    hp_, hn = on_head(0.36 * sx - 0.03 * sx, 0.10, 0.005)
    ellipsoid(hp_, (0.009, 0.004, 0.009), seg=8, rings=5, rot=hn.to_track_quat('-Y', 'Z').to_matrix(), mat='EyeHi', w=W_eye)
    # 볼터치
    cp, cn = on_head(0.62 * sx, -0.22, -0.004)
    ellipsoid(cp, (0.03, 0.006, 0.02), seg=10, rings=5, rot=cn.to_track_quat('-Y', 'Z').to_matrix(), mat='Pink', w=W_head)
    # 귀
    if KIND == 'rabbit':                                # 긴 귀 (머리 위 옆 0.50 · 바깥 15° — 0.38은 두 귀가 붙어 보였음)
        base, n = on_head(0.50 * sx, 1.10, -0.02)
        tilt = Matrix.Rotation(math.radians(15 * sx), 3, 'Y') @ Matrix.Rotation(math.radians(-10), 3, 'X')
        up = tilt @ Vector((0, 0, 1)); tip = base + up * 0.27
        W_ear = W_soft(side + 'Ear', 'Head', base, up, start=0.02, width=0.05)
        EXTRA[side + 'Ear'] = (base, tip, 'Head')
        capsule(base, tip, 0.048, 0.034, seg=10, rings=5, flat=0.42, w=W_ear)
        fw = (tilt @ Vector((0, -1, 0))).normalized()
        capsule(base + up * 0.05 + fw * 0.013, tip - up * 0.02 + fw * 0.013, 0.03, 0.02, seg=10, rings=4, flat=0.3, mat='Pink', w=W_ear)
    elif KIND == 'cat':
        pointy_ear(sx, 0.55, 0.92, 0.13, 0.066, 18, 5, inner='Pink')     # 0.11 · 0.055는 작아 보였음
    elif KIND == 'fox':
        pointy_ear(sx, 0.52, 0.95, 0.15, 0.062, 14, 5, inner='FurLight', tip='Dark')
    elif KIND == 'dog':                                 # 늘어진 귀 (머리 옆에서 아래로)
        p, n = on_head(0.80 * sx, 0.40, 0.004)
        rot = Matrix.Rotation(math.radians(-18 * sx), 3, 'Y')
        base = p + Vector((0, 0, 0.012)); down = rot @ Vector((0, 0, -1))   # 귀 본 = 위 뿌리에서 아래로 (늘어진 귀가 흔들림)
        EXTRA[side + 'Ear'] = (base, base + down * 0.15, 'Head')
        ellipsoid(p + Vector((0.012 * sx, 0, -0.06)), (0.022, 0.05, 0.085), seg=12, rings=8,
                  rot=rot, mat='Dark', w=W_soft(side + 'Ear', 'Head', base, down, start=0.0, width=0.05))
    elif KIND in ('bear', 'panda'):                     # 둥근 귀
        head_disc(0.62 * sx, 0.78, (0.055, 0.025, 0.052), 'Dark' if KIND == 'panda' else 'Fur', lift=0.015, turn=20 * sx,
                  inner=None if KIND == 'panda' else 'FurLight', ear=side)
    elif KIND == 'koala':                               # 크고 복슬한 귀 (옆으로)
        # 55°로 옆을 보게 두면 옆에서 납작한 원판처럼 보였음 → 머리 위 옆에서 앞을 보게
        head_disc(0.82 * sx, 0.62, (0.085, 0.035, 0.08), 'Fur', out=-0.02, lift=0.01, turn=28 * sx, inner='FurLight', ear=side)
    if KIND == 'panda':                                 # 눈 무늬 (바깥 아래로 기운 타원)
        surface_patch(HEAD_C, HEAD_R, 0.072 * sx, HEAD_C.z - 0.01, 0.042, 0.058, ang=math.radians(-35 * sx), mat='Dark', w=W_head)

# 주둥이 · 코 · 부리
if KIND == 'rabbit':
    mp, mn = on_head(0.0, -0.30, -0.03); ellipsoid(mp, (0.07, 0.04, 0.045), seg=12, rings=7, mat='FurLight', w=W_head)
    np_, nn = on_head(0.0, -0.17, 0.004)
    ellipsoid(np_, (0.02, 0.012, 0.014), seg=8, rings=5, rot=nn.to_track_quat('-Y', 'Z').to_matrix(), mat='Pink', w=W_head)
elif KIND == 'cat':
    mp, mn = on_head(0.0, -0.30, -0.028); ellipsoid(mp, (0.065, 0.038, 0.04), seg=12, rings=7, mat='FurLight', w=W_head)
    np_, nn = on_head(0.0, -0.18, 0.004)
    ellipsoid(np_, (0.018, 0.01, 0.012), seg=8, rings=5, rot=nn.to_track_quat('-Y', 'Z').to_matrix(), mat='Pink', w=W_head)
elif KIND in ('dog', 'bear'):
    big = KIND == 'dog'
    mp, mn = on_head(0.0, -0.28, -0.022 if big else -0.025)
    mr = (0.082, 0.06, 0.055) if big else (0.075, 0.05, 0.05)
    ellipsoid(mp, mr, seg=12, rings=8, mat='FurLight', w=W_head)
    nose(mp + Vector((0, -mr[1] + 0.006, mr[2] * 0.45)), (0.03, 0.02, 0.022))
elif KIND == 'fox':                                     # 앞으로 뾰족한 흰 주둥이 + 코끝
    mp, mn = on_head(0.0, -0.26, -0.035)
    ellipsoid(mp + Vector((0, -0.02, 0)), (0.065, 0.07, 0.045), seg=12, rings=8, mat='FurLight', w=W_head)
    nose(mp + Vector((0, -0.088, 0.012)), (0.02, 0.014, 0.016))
elif KIND == 'panda':
    np_, nn = on_head(0.0, -0.20, 0.002); nose(np_, (0.024, 0.014, 0.017))
    mp, mn = on_head(0.0, -0.32, -0.02); ellipsoid(mp, (0.055, 0.03, 0.035), seg=12, rings=7, mat='FurLight', w=W_head)
elif KIND == 'koala':                                   # 큰 검은 코
    np_, nn = on_head(0.0, -0.16, -0.012); nose(np_, (0.038, 0.03, 0.052))
elif KIND == 'penguin':                                 # 흰 얼굴 + 주황 부리
    surface_patch(HEAD_C, HEAD_R, 0.0, HEAD_C.z - 0.03, 0.15, 0.125, w=W_head)
    bp, bn = on_head(0.0, -0.16, -0.015)
    limb([bp, bp + Vector((0, -0.055, -0.012))], [0.03, 0.006], cap=4, flat=0.55, mat='Beak', w=W_head)

# 꼬리
# 11-16 꼬리 본: 뿌리 → 꼬리 방향 (긴 꼬리는 뿌리에서 갈수록 Hips → Tail, 둥근 꼬리는 통째로)
TAIL = {'rabbit': ((0, 0.10, 0.27), (0, 0.18, 0.27), False), 'cat': ((0, 0.07, 0.25), (0, 0.17, 0.2), True),
        'dog': ((0, 0.08, 0.27), (0, 0.16, 0.3), True), 'fox': ((0, 0.07, 0.25), (0, 0.17, 0.18), True),
        'penguin': ((0, 0.09, 0.22), (0, 0.16, 0.19), False)}.get(KIND, ((0, 0.10, 0.27), (0, 0.17, 0.27), False))
T0, T1 = Vector(TAIL[0]), Vector(TAIL[1])
EXTRA['Tail'] = (T0, T1, 'Hips')
W_tail = W_soft('Tail', 'Hips', T0, (T1 - T0).normalized(), start=0.0, width=0.05) if TAIL[2] else W_part('Tail')
if KIND == 'rabbit':
    ellipsoid(Vector((0, 0.13, 0.27)), (0.05, 0.045, 0.05), seg=10, rings=7, mat='FurLight', w=W_tail)
elif KIND == 'cat':
    limb([Vector((0, 0.07, 0.25)), Vector((0, 0.17, 0.2)), Vector((0, 0.25, 0.28)), Vector((0.02, 0.26, 0.42))], [0.024, 0.024, 0.022, 0.018], w=W_tail)
elif KIND == 'dog':
    limb([Vector((0, 0.08, 0.27)), Vector((0, 0.16, 0.3)), Vector((0, 0.19, 0.38))], [0.03, 0.027, 0.02], w=W_tail)
elif KIND == 'fox':
    limb([Vector((0, 0.07, 0.25)), Vector((0, 0.17, 0.18)), Vector((0, 0.28, 0.22)), Vector((0, 0.33, 0.33))], [0.03, 0.065, 0.07, 0.04], w=W_tail)
    ellipsoid(Vector((0, 0.335, 0.37)), (0.045, 0.045, 0.05), seg=10, rings=7, mat='FurLight', w=W_tail)
elif KIND in ('bear', 'panda', 'koala'):
    ellipsoid(Vector((0, 0.125, 0.27)), (0.035, 0.03, 0.035), seg=10, rings=7, w=W_tail)
elif KIND == 'penguin':
    ellipsoid(Vector((0, 0.115, 0.21)), (0.05, 0.035, 0.02), seg=10, rings=6, rot=Matrix.Rotation(math.radians(-30), 3, 'X'), w=W_tail)

# ---- 리그 ----
arm = bpy.data.armatures.new('Animal_Rig'); rig = bpy.data.objects.new('Animal_Rig', arm)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig; rig.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
EB = {}


def bone(n, h, t, parent=None):
    b = arm.edit_bones.new(n); b.head = h; b.tail = t
    if parent: b.parent = EB[parent]
    EB[n] = b


bone('Hips', (0, 0, HIP_Z), (0, 0, 0.32))
bone('Spine', (0, 0, 0.32), (0, 0, 0.40), 'Hips')
bone('Chest', (0, 0, 0.40), (0, 0, 0.50), 'Spine')
bone('Neck', (0, 0, 0.50), (0, 0, 0.54), 'Chest')
bone('Head', (0, 0, 0.54), (0, 0, 0.90), 'Neck')
for sx, side in ((1, 'Left'), (-1, 'Right')):
    sh = Vector((SH.x * sx, 0, SH.z)); el = sh + Vector((0.10 * sx, 0, -0.10)); wr = el + Vector((0.07 * sx, -0.01, -0.085))
    bone(side + 'Shoulder', (0.04 * sx, 0, SH.z), sh, 'Chest')
    bone(side + 'UpperArm', sh, el, side + 'Shoulder')
    bone(side + 'LowerArm', el, wr, side + 'UpperArm')
    bone(side + 'Hand', wr, wr + (wr - el).normalized() * 0.06, side + 'LowerArm')
    hp = Vector((0.075 * sx, 0, HIP_Z + 0.03)); kn = Vector((0.08 * sx, -0.01, 0.145)); an = Vector((0.08 * sx, 0, 0.065))
    bone(side + 'UpperLeg', hp, kn, 'Hips')
    bone(side + 'LowerLeg', kn, an, side + 'UpperLeg')
    bone(side + 'Foot', an, Vector((0.085 * sx, -0.09, 0.025)), side + 'LowerLeg')
    bone(side + 'Toes', Vector((0.085 * sx, -0.09, 0.025)), Vector((0.085 * sx, -0.14, 0.02)), side + 'Foot')
for n, (h, t, par) in EXTRA.items():                     # 11-16 귀 · 꼬리 · 눈
    bone(n, h, t, par)
bpy.ops.object.mode_set(mode='OBJECT')

# ---- 한 메시로 합치고 가중치 ----
me = bpy.data.meshes.new('ANM_' + KIND); ob = bpy.data.objects.new('ANM_' + KIND, me)
bpy.context.scene.collection.objects.link(ob)
for n in MAT_ORDER: me.materials.append(MATS[n])
big = bmesh.new()
wl = []   # 점마다 가중치
for bm, mat, w in PARTS:
    mi = MAT_ORDER.index(mat)
    vm = {}
    for v in bm.verts:
        vm[v] = big.verts.new(v.co); wl.append((w or W_head)(v.co))
    for f in bm.faces:
        nf = big.faces.new([vm[v] for v in f.verts]); nf.material_index = mi; nf.smooth = SHADE == 'smooth'
    bm.free()
big.to_mesh(me); big.free()
groups = {}
for i, ws in enumerate(wl):
    for n, x in ws.items():
        if x < 1e-3: continue
        g = groups.get(n) or ob.vertex_groups.new(name=n); groups[n] = g
        g.add([i], x, 'REPLACE')
ob.parent = rig
mod = ob.modifiers.new('Armature', 'ARMATURE'); mod.object = rig

# ---- 검사: 열린 · 비다양체 가장자리 ----
bm = bmesh.new(); bm.from_mesh(me)
open_ = sum(1 for e in bm.edges if len(e.link_faces) == 1); nonman = sum(1 for e in bm.edges if len(e.link_faces) > 2)
print('ANIMAL', KIND, 'verts', len(me.vertices), 'faces', len(me.polygons), 'open', open_, 'nonmanifold', nonman,
      'height %.3f' % max(v.co.z for v in me.vertices))
bm.free()

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'animal_%s.blend' % TAG))

if FBX:
    os.makedirs(os.path.dirname(FBX), exist_ok=True)
    for x in bpy.context.view_layer.objects: x.select_set(x in (ob, rig))
    bpy.ops.export_scene.fbx(filepath=FBX, use_selection=True, object_types={'MESH', 'ARMATURE'},
                             axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL', bake_space_transform=False,
                             add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
                             mesh_smooth_type='FACE', bake_anim=False)

# ---- 미리보기 렌더 ----
sc = bpy.context.scene
try:
    sc.render.engine = 'BLENDER_EEVEE_NEXT'
except TypeError:
    sc.render.engine = 'BLENDER_EEVEE'
wd = bpy.data.worlds.new('W'); wd.use_nodes = True
next(n for n in wd.node_tree.nodes if n.type == 'BACKGROUND').inputs['Color'].default_value = (0.42, 0.45, 0.5, 1)
sc.world = wd; sc.view_settings.view_transform = 'Standard'
ld = bpy.data.lights.new('Sun', 'SUN'); ld.energy = 3.0
lo = bpy.data.objects.new('Sun', ld); sc.collection.objects.link(lo); lo.rotation_euler = (math.radians(50), 0, math.radians(30))
cd = bpy.data.cameras.new('C'); cd.type = 'ORTHO'; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam); sc.camera = cam
pv = os.path.join(HERE, 'preview'); os.makedirs(pv, exist_ok=True)
c = Vector((0, 0, 0.55))


def shoot(tag, d):
    d = d.normalized(); cam.location = c + d * 5; cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
    cd.ortho_scale = 1.3; sc.render.resolution_x = 600; sc.render.resolution_y = 700
    sc.render.filepath = os.path.join(pv, '%s_%s.png' % (TAG, tag)); bpy.ops.render.render(write_still=True)


shoot('front', Vector((0, -1, 0.1))); shoot('q', Vector((0.7, -0.7, 0.2))); shoot('side', Vector((1, 0, 0.05))); shoot('back', Vector((-0.4, 1, 0.2)))
# 자세 확인: 팔 내림 · 다리 들기
for side, sg in (('Left', 1), ('Right', -1)):
    pb = rig.pose.bones[side + 'UpperArm']; pb.rotation_mode = 'XYZ'
    R = Matrix.Rotation(math.radians(35 * sg), 4, 'Y'); m = rig.matrix_world @ pb.bone.matrix_local
    pb.rotation_euler = (m.inverted() @ R @ m).to_euler()
pb = rig.pose.bones['LeftUpperLeg']; pb.rotation_mode = 'XYZ'
R = Matrix.Rotation(math.radians(-50), 4, 'X'); m = rig.matrix_world @ pb.bone.matrix_local
pb.rotation_euler = (m.inverted() @ R @ m).to_euler()
pb = rig.pose.bones['Head']; pb.rotation_mode = 'XYZ'
R = Matrix.Rotation(math.radians(20), 4, 'Z'); m = rig.matrix_world @ pb.bone.matrix_local
pb.rotation_euler = (m.inverted() @ R @ m).to_euler()
bpy.context.view_layer.update()
shoot('pose', Vector((0.7, -0.7, 0.2)))
