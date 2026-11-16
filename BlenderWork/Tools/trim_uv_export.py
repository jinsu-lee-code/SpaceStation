# ⑤ 트림 UV 내보내기 (2026-11-16): 지어 둔 내부 모델 (INT_{이름}_*)에 트림 UV를 붙여 FBX를 다시 내보냄 — 빌더는 다시 돌리지 않음.
#   blender -b BlenderWork/Interior_Evo.blend -P BlenderWork/Tools/trim_uv_export.py -- EvoArcology EvoGarden SM_InteriorKit_Evo1 [--write]
#   .blend에 모델이 없는 방 (지금 FBX 위에 붙임): blender -b --factory-startup -P BlenderWork/Tools/trim_uv_export.py -- EvoShieldDome --from-fbx [--write]
#   .blend는 저장하지 않음 (Interior_Evo.blend가 GitHub 100 MB 한도에 가까움 — UV는 내보낼 때만 계산).
#
# 트림 UV (Unity 셰이더 SpaceStation/InteriorTrim이 읽음): 트림 재질 면마다 그 면 묶음 테두리 선 넷까지 거리 (m) + 1
#   UV1 = (선0, 선1), UV2 = (선2, 선3), UV3.x = 면 폭 (마주 보는 테두리 사이, 없으면 10) · UV0 = 원래 UV (없으면 빈 층)
#   트림 면이 아니면 (0, 0) = 값 없음 (셰이더가 예전 모습 그대로).
#   면 묶음 = 같은 재질 · 46° 안으로 이어진 면 (굽은 띠 · 원기둥은 한 덩이), 테두리 = 묶음 밖과 닿는 변 (재질이 바뀌는 곳 · 각진 모서리 · 열린 끝).
#   Blender에서 하는 까닭: 사각면이 남아 있어 띠 면 하나가 위 · 아래 테두리를 둘 다 가짐 (Unity에서는 삼각형이라 띠를 가로질러 선이 끊겼음).
#   선은 면에서 REACH 안 · 면이 그 변의 길이 범위 안에 걸칠 때만. 거의 같은 선 (옆 면의 같은 테두리)은 하나로, 넷 넘으면 검사점 오차를 가장 줄이는 넷.
#   오목한 다각형 트림 면 (계단 옆면)은 삼각형으로 나누고, 면 안쪽 점에서 선 거리가 실제 테두리 거리보다 짧으면 그 선은 뺌 (가짜 대각선 — AI 중추 계단 2026-11-16).
#   선 넷으로 모자란 큰 다각형 (원판 둘레)은 삼각형으로 나눠 다시 계산 (삼각형 수 · 위치 그대로).
# 검사: 새 FBX와 지금 FBX를 둘 다 가져와 조각 이름 · 꼭짓점 위치 · 조각 자리 · 법선 · 삼각형 수 · 재질 칸 · 꼭짓점 색 · 원래 UV가 같은지 — 같아야 --write 때 덮어씀.
#   품질: 트림 면 검사점마다 넣은 선 거리와 실제 테두리 거리가 1 cm 넘게 다른 면 = "bad" (방마다 % · 가장 어긋난 곳 Unity 좌표).
import bpy, bmesh, sys, os, math, tempfile, shutil, hashlib
from collections import defaultdict
from mathutils import Vector, kdtree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NAMES = [a for a in ARGS if not a.startswith("--")]
WRITE = "--write" in ARGS
FROM_FBX = "--from-fbx" in ARGS     # .blend에 모델이 없는 방 (지금 FBX를 가져와 그 위에 붙임) — 빈 Blender (--factory-startup)에서 돌릴 것 (재질 이름 .001 방지)
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
FOLDER = os.path.join(REPO, "SpaceStation", "Assets", "_Project", "Art", "Models", "Interior")
TMP = os.path.join(tempfile.gettempdir(), "trim_uv_export")
os.makedirs(TMP, exist_ok=True)


def fbx_path(name):
    return os.path.join(FOLDER, (name if name.startswith("SM_") else "SM_Interior_" + name) + ".fbx")

# Blender 재질 이름 → 트림 (Unity 재질 표 InteriorTemplateBuilder: 이 이름들이 트림 재질 여덟로 감)
TRIM = {"EvoWhite", "EvoDark", "Evo2Wall", "Evo2Trim", "Evo2Gold", "Evo3Gold", "Evo3GoldLeaf", "Evo3Pearl", "Evo3Nacre"}
EXPORT = dict(axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
SMOOTH_DOT = 0.69   # 46° 안 = 같은 묶음 (팔각 기둥 · 원기둥 · 굽은 띠)
REACH = 0.1         # 테두리에서 이만큼 안까지만 선을 넣음 (모서리 빛 · 홈 · 안쪽 테 모두 7.5 cm 안) — 먼 면은 값 = 없음 → 꼭짓점이 덜 쪼개짐
NOLINE = 10.0
LINES = 4
CAP = 0.08          # 검사 · 고르기에서 보는 테두리 거리 끝 (장식이 모두 이 안)
TOL = 0.01          # 선 거리가 실제 테두리 거리와 이만큼 넘게 다르면 어긋남


def convex(f):
    """면이 볼록한가 (면 법선 기준 꺾이는 방향이 모두 같음)."""
    vs = [v.co for v in f.verts]
    n = f.normal
    sign = 0
    for i in range(len(vs)):
        s = (vs[i] - vs[i - 1]).cross(vs[(i + 1) % len(vs)] - vs[i]).dot(n)
        if abs(s) < 1e-10:
            continue
        if sign == 0:
            sign = 1 if s > 0 else -1
        elif (s > 0) != (sign > 0):
            return False
    return True


def planar(f):
    """면이 평평한가 (꼭짓점이 면 평면에서 0.2 mm 안) — 휜 다각형을 삼각형으로 나누면 법선이 바뀜 (치안 본부 겉껍질 · 3단계 문 벽)."""
    c = f.calc_center_median()
    return all(abs((v.co - c).dot(f.normal)) < 2e-4 for v in f.verts)


def face_samples(pts, cen):
    """면 안쪽 검사점: 꼭짓점 · 가운데 · 변 가운데 · 가운데와 꼭짓점 · 변 가운데 사이."""
    out = list(pts) + [cen]
    for i in range(len(pts)):
        m = (pts[i] + pts[(i + 1) % len(pts)]) * 0.5
        out += [m, (cen + pts[i]) * 0.5, (cen + m) * 0.5, cen.lerp(pts[i], 0.85), cen.lerp(m, 0.85)]
    return out


def seg_dist(p, a, b):
    ab = b - a
    t = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def solve(bm, M, tgt):
    """트림 면마다 고른 선 (변 위 점, 안쪽 방향) 넷까지 · 면 폭. 돌려줌: (면 번호 → (선들, 폭), 통계, 어긋난 다각형 면)."""
    bm.verts.index_update()
    bm.faces.index_update()
    bm.faces.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    NM = M.to_3x3().inverted().transposed()
    W = [M @ v.co for v in bm.verts]
    faces = [f for f in bm.faces if f.material_index in tgt]
    fn = {}
    for f in faces:
        n = NM @ f.normal
        fn[f.index] = n.normalized() if n.length > 1e-9 else Vector((0, 0, 1))
    parent = {f.index: f.index for f in faces}

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x
    for e in bm.edges:
        lf = [f for f in e.link_faces if f.index in parent]
        for i in range(len(lf)):
            for j in range(i + 1, len(lf)):
                a, b = lf[i], lf[j]
                if a.material_index == b.material_index and fn[a.index].dot(fn[b.index]) > SMOOTH_DOT:
                    ra, rb = find(a.index), find(b.index)
                    if ra != rb:
                        parent[ra] = rb
    # 묶음 테두리 변
    border = {}                                   # (묶음, 변 번호) → (A, B)
    for f in faces:
        g = find(f.index)
        for e in f.edges:
            if (g, e.index) in border:
                continue
            if not any(of.index != f.index and of.index in parent and find(of.index) == g for of in e.link_faces):
                border[(g, e.index)] = (W[e.verts[0].index], W[e.verts[1].index])
    keys = list(border.keys())
    samples = []
    for k, (a, b) in enumerate(border.values()):
        L = (b - a).length
        n = max(1, int(math.ceil(L / 0.2)))
        for s in range(n + 1):
            samples.append((a.lerp(b, s / n), k))
    kd = kdtree.KDTree(max(1, len(samples)))
    for i, (p, _k) in enumerate(samples):
        kd.insert(p, i)
    kd.balance()
    segs = list(border.values())
    seg_group = [g for (g, _e) in keys]
    out = {}
    over = 0
    bad = 0
    bad_at = []
    bad_ngons = []
    groups = set()
    for f in faces:
        g = find(f.index)
        groups.add(g)
        pts = [W[v.index] for v in f.verts]
        cen = sum(pts, Vector()) / len(pts)
        rad = max((p - cen).length for p in pts)
        cand = set()
        for (_co, idx, _d) in kd.find_range(cen, rad + REACH + 0.12):
            k = samples[idx][1]
            if seg_group[k] == g:
                cand.add(k)
        nrm = fn[f.index]
        samp = face_samples(pts, cen)
        # 검사점마다 실제 테두리 거리 (묶음 테두리 변 전부 중 가장 가까운 것, CAP에서 자름)
        et = [min([seg_dist(q, segs[k][0], segs[k][1]) for k in cand] + [CAP]) for q in samp]
        lines = []
        for k in cand:
            a, b = segs[k]
            ab = b - a
            L = ab.length
            if L < 1e-6:
                continue
            d = ab / L
            # 면이 변 길이 범위 (± REACH) 안에 걸쳐야 함 — 아니면 연장선 (오목한 모서리의 가짜 선)
            ts = [(p - a).dot(d) for p in pts]
            if max(ts) < -REACH or min(ts) > L + REACH:
                continue
            dist = min(seg_dist(p, a, b) for p in pts + [cen])
            if dist > REACH:
                continue
            nk = nrm.cross(d)
            if nk.length < 1e-6:
                continue
            nk.normalize()
            if (cen - a).dot(nk) < 0:
                nk = -nk
            # 가짜 선 검사: 셰이더는 선 거리 중 가장 짧은 것을 테두리 거리로 봄 → 어느 검사점에서든 선 거리가 실제 테두리 거리보다
            #   TOL 넘게 짧으면 (변 끝을 지나 면을 가로지르는 연장선 · 선 뒤쪽 점) 그 선은 뺌. 그 변 하나가 아니라 묶음 테두리 전체와 비교
            #   (둔각 삼각형은 안쪽 점이 그 변 끝을 지나 옆 변에 가까움 — 변 하나와 비교하면 멀쩡한 선이 빠졌음, 2026-11-16)
            ld = [(q - a).dot(nk) for q in samp]
            if any(l < CAP and max(0.0, l) + TOL < e for l, e in zip(ld, et)):
                continue
            lines.append((dist, a, nk, [min(max(0.0, l), CAP) for l in ld]))
        lines.sort(key=lambda x: x[0])
        # 같은 선 (옆 면의 같은 테두리 — 고리 띠는 면마다 위 · 아래 변이 따로)은 하나로: 검사점 거리가 모두 2 mm 안
        #   (예전 기준 "방향 25° · 가운데 거리 1 cm"는 굽은 테두리의 이웃 변까지 합쳐 2 ~ 4 cm 빗나갔음 — 고르기가 겹친 선은 알아서 거름)
        kept = []
        for (d, a, nk, lc) in lines:
            if any(max(abs(x - y) for x, y in zip(lc, k2[4])) < 0.002 for k2 in kept):
                continue
            kept.append((d, a, nk, (cen - a).dot(nk), lc))
        # 면 폭 = 마주 보는 테두리 선 (방향이 반대) 사이 거리 중 가장 짧은 것 — 가는 손잡이 · 테에서 모서리 빛 폭을 줄임 (없으면 NOLINE)
        width = NOLINE
        for i in range(len(kept)):
            for j in range(i + 1, len(kept)):
                if kept[i][2].dot(kept[j][2]) < -0.7:
                    width = min(width, kept[i][3] + kept[j][3])
        # 넷 넘으면 고르기: 검사점마다 (선 거리 − 실제 거리) 합을 가장 많이 줄이는 선부터 (가까운 순이면 원판 둘레 같은 데서 한쪽만 남았음)
        cur = [CAP] * len(samp)
        if len(kept) <= LINES:
            chosen = kept
            for c in kept:
                cur = [min(x, l) for x, l in zip(cur, c[4])]
        else:
            over += 1
            chosen = []
            rest = list(kept)
            while rest and len(chosen) < LINES:
                gain = [sum(max(0.0, x - e) - max(0.0, min(x, l) - e) for x, l, e in zip(cur, c[4], et)) for c in rest]
                i = max(range(len(rest)), key=lambda j: gain[j])
                if gain[i] <= 1e-6:
                    break
                c = rest.pop(i)
                chosen.append(c)
                cur = [min(x, l) for x, l in zip(cur, c[4])]
        # 품질: 검사점마다 셰이더가 볼 테두리 거리 vs 실제 — TOL 넘게 다르면 어긋난 면 (선이 빠짐 = 모서리 빛이 끊김 · 가짜 선)
        worst = max(abs(x - e) for x, e in zip(cur, et))
        if worst > TOL:
            bad += 1
            bad_at.append((worst, cen))
            if len(f.verts) > 4 and planar(f):
                bad_ngons.append(f)
        out[f.index] = ([(c[1], c[2]) for c in chosen], width)
    bad_at.sort(key=lambda x: -x[0])
    return out, (len(faces), len(groups), over, bad, bad_at[:3]), bad_ngons


def trim_uv(o):
    """o.data에 트림 UV 층을 붙임 (제자리 — 저장하지 않으니 괜찮음). 돌려줌: (트림 면 수, 묶음 수, 선 넷 넘는 면 수, 어긋난 면 수, 가장 어긋난 셋)."""
    me = o.data
    tgt = {i for i, m in enumerate(me.materials) if m is not None and m.name.split(".")[0] in TRIM}
    if not tgt or o.name.endswith("_Leaves"):     # 잎 조각의 흰 · 금빛 꽃잎은 템플릿 빌드가 식물 재질로 바꿈 (InteriorFoliage)
        return (0, 0, 0, 0, [])
    if len(me.uv_layers) > 1:
        raise RuntimeError("%s: UV 층이 둘 이상 — 트림 UV 자리 (UV1 · UV2)가 어긋남" % o.name)
    # 면을 삼각형으로 나누면 법선이 바뀜 (매끈 음영은 꼭짓점 법선이 모서리 각으로 가중 — 오목한 꼭짓점 각이 달라짐,
    #   FBX에서 가져온 사용자 법선은 면 구조 기준으로 저장돼 틀어짐 — 운송 환적장 터널 · 배 방석) → 나누기 전 모서리 법선을 원래 면 번호로 기억해 그대로 다시 넣음
    corner = {}
    for poly in me.polygons:
        for li in range(poly.loop_start, poly.loop_start + poly.loop_total):
            corner[(poly.index, me.loops[li].vertex_index)] = me.corner_normals[li].vector.copy()
    bm = bmesh.new()
    bm.from_mesh(me)
    orig = bm.faces.layers.int.new("_trim_orig")
    for f in bm.faces:
        f[orig] = f.index
    split = False
    # 오목한 트림 면 (계단 옆면 같은 톱니 다각형)은 삼각형으로 — 한 면 안에서 테두리 연장선이 면을 가로질러 가짜 선이 생겼음 (모양 · 위치는 그대로)
    concave = [f for f in bm.faces if f.material_index in tgt and len(f.verts) > 3 and not convex(f) and planar(f)]
    if concave:
        bmesh.ops.triangulate(bm, faces=concave, quad_method='BEAUTY', ngon_method='BEAUTY')
        split = True
    M = o.matrix_world
    out, st, bad_ngons = solve(bm, M, tgt)
    # 선 넷으로 모자란 큰 다각형 (원판 · 둘레가 많은 면)은 삼각형으로 나눠 다시 — 삼각형마다 가까운 둘레 변만 맡음 (삼각형 수 · 위치 그대로)
    if bad_ngons:
        bmesh.ops.triangulate(bm, faces=bad_ngons, quad_method='BEAUTY', ngon_method='BEAUTY')
        split = True
        out, st, _ = solve(bm, M, tgt)
    keep = [corner[(f[orig], loop.vert.index)] for f in bm.faces for loop in f.loops] if split else None
    W = [M @ v.co for v in bm.verts]
    if not len(bm.loops.layers.uv):
        bm.loops.layers.uv.new("UVMap")
    uva = bm.loops.layers.uv.new("TrimA")
    uvb = bm.loops.layers.uv.new("TrimB")
    uvc = bm.loops.layers.uv.new("TrimC")
    for f in bm.faces:
        if f.index not in out:
            continue
        lines, width = out[f.index]
        for loop in f.loops:
            p = W[loop.vert.index]
            vals = [NOLINE + 1.0] * LINES
            for i, (a, nk) in enumerate(lines):
                vals[i] = max(-0.02, (p - a).dot(nk)) + 1.0     # 1 = 테두리 위 · 11 = 선 없음
            loop[uva].uv = (vals[0], vals[1])
            loop[uvb].uv = (vals[2], vals[3])
            loop[uvc].uv = (width, 0.0)
    bm.faces.layers.int.remove(orig)
    bm.to_mesh(me)
    bm.free()
    if keep is not None:
        me.normals_split_custom_set(keep)        # 메시 모서리 순서 = bmesh 면 · 모서리 순서
    return st[:4] + ([(w, o.name, c) for (w, c) in st[4]],)


def snapshot(path):
    """FBX를 따로 가져와 조각별 서명 (이름 → ((꼭짓점 위치, 조각 자리, 꼭짓점별 법선), 삼각형 수, 재질, 꼭짓점 색 해시, 첫 UV 해시, UV 층 수)). 가져온 것은 지움."""
    col = bpy.data.collections.new("_cmp")
    bpy.context.scene.collection.children.link(col)
    lc = bpy.context.view_layer.layer_collection.children[col.name]
    bpy.context.view_layer.active_layer_collection = lc
    before = set(bpy.data.objects)
    mats_before = set(bpy.data.materials)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    sig = {}
    for o in new:
        if o.type != 'MESH':
            continue
        me = o.data
        name = o.name.split(".")[0]
        M = o.matrix_world
        # 꼭짓점 위치 · 조각 자리 (움직이는 부품 축) · 꼭짓점마다 법선 모음 — 해시 말고 허용 오차로 비교 (geom_differ):
        #   반올림 해시는 1/32 m처럼 딱 떨어지는 좌표가 1e-16 흔들림 (FBX에서 가져온 방의 회전 녹이기)에 뒤집혔고,
        #   다각형을 삼각형으로 나누면 법선이 1e-4쯤 흔들림
        #   법선 = (1 cm² 넘는 면의 법선, 모든 면의 법선): 한쪽의 큰 면 법선이 다른 쪽 어느 면 법선에든 있어야 함.
        #   FBX에서 가져온 방은 넓이 0 · 7 mm 삼각형의 법선이 가져오기 · 내보내기만으로도 바뀜 (3단계 문 벽 · 치안 본부 — 트림과 상관없음),
        #   큰 다각형을 삼각형으로 나누면 한쪽만 작은 면이 되니 "큰 면끼리"로 견주면 안 됨
        wv = [M @ v.co for v in me.vertices]
        cn = me.corner_normals
        big = defaultdict(set)
        every = defaultdict(set)
        for poly in me.polygons:
            for li in range(poly.loop_start, poly.loop_start + poly.loop_total):
                vi = me.loops[li].vertex_index
                n = tuple(cn[li].vector)
                every[vi].add(n)
                if poly.area >= 1e-4:
                    big[vi].add(n)
        nrm = (big, every)
        h = (wv, tuple(x for row in o.matrix_local for x in row), nrm)
        cols = ""
        if me.color_attributes:
            ca = me.color_attributes[0]
            cols = hashlib.md5(repr(sorted(set(tuple(round(c, 3) for c in d.color) for d in ca.data))).encode()).hexdigest()
        uv0 = ""
        if me.uv_layers:
            uv0 = hashlib.md5(repr(sorted(set((round(d.uv.x, 4), round(d.uv.y, 4)) for d in me.uv_layers[0].data))).encode()).hexdigest()
        tris = sum(len(p.vertices) - 2 for p in me.polygons)          # 삼각형 수 (오목한 트림 면은 삼각형으로 나눠 내보내니 면 수 대신)
        sig[name] = (h, tris, tuple(m.name.split(".")[0] if m else "" for m in me.materials), cols, uv0, len(me.uv_layers))
    for o in new:
        me = o.data if o.type == 'MESH' else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    for m in list(bpy.data.materials):
        if m not in mats_before and m.users == 0:
            bpy.data.materials.remove(m)          # 다음 가져오기 재질이 "Evo2Gold.001"이 되지 않게
    bpy.data.collections.remove(col)
    return sig


def geom_differ(a, b, ptol=1e-4, ntol=0.01):
    """(위치들, 조각 자리, 꼭짓점별 법선) 둘을 견줘 다른 것 이름들: 꼭짓점이 0.1 mm 안에 짝이 없으면 pos,
    조각 자리 칸이 1e-4 넘게 다르면 pivot, 같은 자리 꼭짓점들의 법선끼리 0.01 넘게 다르면 normals."""
    out = []
    if len(a[1]) != len(b[1]) or max(abs(x - y) for x, y in zip(a[1], b[1])) > 1e-4:
        out.append("pivot")
    if len(a[0]) != len(b[0]):
        return out + ["pos"]
    trees = []
    for s in (a, b):
        t = kdtree.KDTree(max(1, len(s[0])))
        for i, p in enumerate(s[0]):
            t.insert(p, i)
        t.balance()
        trees.append(t)
    for (s, t, tree_t) in ((a, b, trees[1]), (b, a, trees[0])):
        # s의 꼭짓점마다 t (다른 쪽) 0.1 mm 안 꼭짓점들의 법선 — 짝이 없으면 pos, 법선이 다 멀면 normals
        bad_p = bad_n = False
        for i, p in enumerate(s[0]):
            near = tree_t.find_range(p, ptol)
            if not near:
                bad_p = True
                break
            tn = [n for (_c, j, _d) in near for n in t[2][1].get(j, ())]
            for n in s[2][0].get(i, ()):
                if not tn or min(sum((x - y) ** 2 for x, y in zip(n, m)) for m in tn) > ntol * ntol:
                    bad_n = True
                    break
            if bad_n:
                break
        if bad_p and "pos" not in out:
            out.append("pos")
        if bad_n and "normals" not in out:
            out.append("normals")
    return out


def unity(p):
    return (-p.x, p.z, -p.y)      # 내보내기 축 (앞 = -Z, 위 = Y) — Unity 템플릿 안 좌표로 찾아보기 쉽게


def room(name):
    path = fbx_path(name)
    if not os.path.exists(path):
        raise RuntimeError("지금 FBX 없음: " + path)
    old = snapshot(path)
    imported = []
    mats_before = set(bpy.data.materials)
    if FROM_FBX:
        bpy.context.view_layer.active_layer_collection = bpy.context.view_layer.layer_collection
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        imported = [o for o in bpy.data.objects if o not in before]
        objs = sorted((o for o in imported if o.type == 'MESH'), key=lambda o: o.name)
        if {o.name for o in objs} != set(old):
            raise RuntimeError("가져온 조각 이름이 다름 (이름 겹침?): %s" % sorted({o.name for o in objs} ^ set(old))[:6])
        # 가져온 조각엔 축 변환 (X 90°)이 붙음 → 그대로 내보내면 조각 회전이 180°로 바뀌어 법선 · 자리 비교에서 걸렸음.
        #   회전을 메시에 녹여 .blend에서 내보낼 때와 같은 상태 (회전 없음)로 — 다시 내보내 가져오면 처음 가져온 모습과 같음.
        #   transform_apply는 작은 면 (2 cm²)의 사용자 법선을 13°까지 바꿨음 (치안 본부) → 메시를 직접 돌리고 원래 법선을 돌려 그대로 넣음
        for o in objs:
            if o.data.users > 1:
                o.data = o.data.copy()
            _loc, rot, _scale = o.matrix_basis.decompose()
            R = rot.to_matrix()
            keep = [(R @ c.vector).normalized() for c in o.data.corner_normals]
            o.data.transform(R.to_4x4())
            o.data.normals_split_custom_set(keep)
            o.rotation_mode = 'XYZ'
            o.rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()           # matrix_world 갱신 (트림 계산 · 출력 좌표)
    else:
        objs = []
        for n in sorted(old):
            o = bpy.data.objects.get(n)
            if o is None or o.type != 'MESH':
                raise RuntimeError("blend에 조각 없음: " + n)
            objs.append(o)
    try:
        stats = {o.name: trim_uv(o) for o in objs}
        tf = sum(s[0] for s in stats.values())
        bad = sum(s[3] for s in stats.values())
        print("TRIM %s faces %d groups %d over4 %d bad %d (%.1f%%)" % (name, tf, sum(s[1] for s in stats.values()),
              sum(s[2] for s in stats.values()), bad, 100.0 * bad / max(1, tf)))
        for (w, on, c) in sorted((x for s in stats.values() for x in s[4]), key=lambda x: -x[0])[:3]:
            print("    worst %.3f %s unity(%.2f, %.2f, %.2f)" % ((w, on) + unity(c)))
        has_col = any(o.data.color_attributes for o in objs)
        result = None
        for ctype in (["LINEAR", "SRGB"] if has_col else ["SRGB"]):
            out = os.path.join(TMP, os.path.basename(path))
            for o in objs:
                o.hide_set(False)                 # 눈을 꺼 둔 조각 (농장 · 물류 등)은 선택이 안 돼 내보내기에서 빠졌음 — .blend는 저장하지 않음
            for o in bpy.context.view_layer.objects:
                o.select_set(o in objs)
            bpy.context.view_layer.objects.active = objs[0]
            bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, colors_type=ctype, **EXPORT)
            new = snapshot(out)
            diff = []
            if set(new) != set(old):
                diff.append(("names", sorted(set(old) ^ set(new))[:6]))
            for n in sorted(set(old) & set(new)):
                a, b = old[n], new[n]
                diff += [(n, what) for what in geom_differ(a[0], b[0])]
                for i, what in ((1, "tris"), (2, "mats"), (3, "colors"), (4, "uv0")):
                    if what == "uv0" and a[5] == 0:
                        continue
                    if a[i] != b[i]:
                        diff.append((n, what))
            print("COMPARE %s colors %s diff %d %s" % (name, ctype, len(diff), diff[:6]))
            if not diff:
                result = out
                break
        if result is None:
            print("RESULT %s FAIL — 지금 FBX와 모양이 달라 덮어쓰지 않음" % name)
            return (name, "FAIL", tf, bad)
        if WRITE:
            shutil.copyfile(result, path)
            print("RESULT %s WROTE" % name)
            return (name, "WROTE", tf, bad)
        print("RESULT %s OK (확인만 — --write로 덮어씀)" % name)
        return (name, "OK", tf, bad)
    finally:
        if FROM_FBX:
            for o in imported:
                me = o.data if o.type == 'MESH' else None
                bpy.data.objects.remove(o, do_unlink=True)
                if me is not None and me.users == 0:
                    bpy.data.meshes.remove(me)
            for m in list(bpy.data.materials):
                if m not in mats_before and m.users == 0:
                    bpy.data.materials.remove(m)


summary = []
for name in NAMES:
    try:
        summary.append(room(name))
    except Exception as ex:          # 한 방이 막혀도 나머지는 계속 (요약에 ERROR)
        print("RESULT %s ERROR %s" % (name, ex))
        summary.append((name, "ERROR", 0, 0))
print("SUMMARY", " ".join("%s=%s/%d/%.1f%%" % (n, r, f, 100.0 * b / max(1, f)) for (n, r, f, b) in summary))
