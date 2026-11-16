# ⑤ 트림 UV 내보내기 (2026-11-16): 지어 둔 내부 모델 (INT_{이름}_*)에 트림 UV를 붙여 FBX를 다시 내보냄 — 빌더는 다시 돌리지 않음.
#   blender -b BlenderWork/Interior_Evo.blend -P BlenderWork/Tools/trim_uv_export.py -- EvoArcology [--write]
#   .blend는 저장하지 않음 (Interior_Evo.blend가 GitHub 100 MB 한도에 가까움 — UV는 내보낼 때만 계산).
#
# 트림 UV (Unity 셰이더 SpaceStation/InteriorTrim이 읽음): 트림 재질 면마다 그 면 묶음 테두리 선 넷까지 거리 (m) + 1
#   UV1 = (선0, 선1), UV2 = (선2, 선3), UV3.x = 면 폭 (마주 보는 테두리 사이, 없으면 10) · UV0 = 원래 UV (없으면 빈 층)
#   트림 면이 아니면 (0, 0) = 값 없음 (셰이더가 예전 모습 그대로).
#   면 묶음 = 같은 재질 · 46° 안으로 이어진 면 (굽은 띠 · 원기둥은 한 덩이), 테두리 = 묶음 밖과 닿는 변 (재질이 바뀌는 곳 · 각진 모서리 · 열린 끝).
#   Blender에서 하는 까닭: 사각면이 남아 있어 띠 면 하나가 위 · 아래 테두리를 둘 다 가짐 (Unity에서는 삼각형이라 띠를 가로질러 선이 끊겼음).
#   선은 면에서 REACH 안 · 면이 그 변의 길이 범위 안에 걸칠 때만, 가까운 것부터 넷. 거의 같은 선 (옆 면의 같은 테두리)은 하나로.
#   오목한 다각형 트림 면 (계단 옆면)은 삼각형으로 나누고, 면 안쪽 점에서 연장선이 실제 변과 어긋나면 그 선은 뺌 (가짜 대각선 — AI 중추 계단 2026-11-16).
# 검사: 새 FBX와 지금 FBX를 둘 다 가져와 조각 이름 · 꼭짓점 위치 · 삼각형 수 · 재질 칸 · 꼭짓점 색 · 원래 UV가 같은지 — 같아야 --write 때 덮어씀.
import bpy, bmesh, sys, os, math, tempfile, shutil, hashlib
from collections import defaultdict
from mathutils import Vector, kdtree

ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
NAME = ARGS[0]
WRITE = "--write" in ARGS
REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
FBX = os.path.join(REPO, "SpaceStation", "Assets", "_Project", "Art", "Models", "Interior", "SM_Interior_%s.fbx" % NAME)
TMP = os.path.join(tempfile.gettempdir(), "trim_uv_export")
os.makedirs(TMP, exist_ok=True)

# Blender 재질 이름 → 트림 (Unity 재질 표 InteriorTemplateBuilder: 이 이름들이 트림 재질 여덟로 감)
TRIM = {"EvoWhite", "EvoDark", "Evo2Wall", "Evo2Trim", "Evo2Gold", "Evo3Gold", "Evo3GoldLeaf", "Evo3Pearl", "Evo3Nacre"}
EXPORT = dict(axis_forward='-Z', axis_up='Y', bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
SMOOTH_DOT = 0.69   # 46° 안 = 같은 묶음 (팔각 기둥 · 원기둥 · 굽은 띠)
REACH = 0.1         # 테두리에서 이만큼 안까지만 선을 넣음 (모서리 빛 · 홈 · 안쪽 테 모두 7.5 cm 안) — 먼 면은 값 = 없음 → 꼭짓점이 덜 쪼개짐
NOLINE = 10.0
LINES = 4


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


def face_samples(pts, cen):
    """면 안쪽 검사점: 꼭짓점 · 가운데 · 변 가운데 · 가운데와 꼭짓점 · 변 가운데 사이."""
    out = list(pts) + [cen]
    for i in range(len(pts)):
        m = (pts[i] + pts[(i + 1) % len(pts)]) * 0.5
        out += [m, (cen + pts[i]) * 0.5, (cen + m) * 0.5, cen.lerp(pts[i], 0.85), cen.lerp(m, 0.85)]
    return out


def trim_uv(o):
    """o.data에 트림 UV 층을 붙임 (제자리 — 저장하지 않으니 괜찮음). 돌려줌: (트림 면 수, 묶음 수, 선 넷 넘는 면 수)."""
    me = o.data
    tgt = {i for i, m in enumerate(me.materials) if m is not None and m.name in TRIM}
    if not tgt or o.name.endswith("_Leaves"):     # 잎 조각의 흰 · 금빛 꽃잎은 템플릿 빌드가 식물 재질로 바꿈 (InteriorFoliage)
        return (0, 0, 0)
    if len(me.uv_layers) > 1:
        raise RuntimeError("%s: UV 층이 둘 이상 — 트림 UV 자리 (UV1 · UV2)가 어긋남" % o.name)
    bm = bmesh.new()
    bm.from_mesh(me)
    # 오목한 트림 면 (계단 옆면 같은 톱니 다각형)은 삼각형으로 — 한 면 안에서 테두리 연장선이 면을 가로질러 가짜 선이 생겼음 (모양 · 위치는 그대로)
    concave = [f for f in bm.faces if f.material_index in tgt and len(f.verts) > 3 and not convex(f)]
    if concave:
        bmesh.ops.triangulate(bm, faces=concave, quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.faces.ensure_lookup_table()
    bm.edges.ensure_lookup_table()
    M = o.matrix_world
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

    def seg_dist(p, a, b):
        ab = b - a
        t = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        return (p - (a + ab * t)).length

    uv_old = bm.loops.layers.uv[0] if len(bm.loops.layers.uv) else bm.loops.layers.uv.new("UVMap")
    uva = bm.loops.layers.uv.new("TrimA")
    uvb = bm.loops.layers.uv.new("TrimB")
    uvc = bm.loops.layers.uv.new("TrimC")
    over = 0
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
            # 연장선 검사: 선 가까이 (8 cm 안)인 면 안쪽 점이 실제 변에서는 멀면 = 변 끝을 지나 면을 가로지르는 선 → 뺌
            if any(0.0 <= (q - a).dot(nk) < 0.08 and seg_dist(q, a, b) > (q - a).dot(nk) + 0.015 for q in samp):
                continue
            lines.append((dist, a, nk))
        lines.sort(key=lambda x: x[0])
        # 거의 같은 선 (옆 면의 같은 테두리 — 고리 띠는 면마다 위 · 아래 변이 따로)은 하나로: 방향 25° 안 · 면 가운데에서 거리 1 cm 안
        kept = []
        for (d, a, nk) in lines:
            s = (cen - a).dot(nk)
            if any(nk.dot(nk2) > 0.9 and abs(s - s2) < 0.01 for (_d2, _a2, nk2, s2) in kept):
                continue
            kept.append((d, a, nk, s))
        # 면 폭 = 마주 보는 테두리 선 (방향이 반대) 사이 거리 중 가장 짧은 것 — 가는 손잡이 · 테에서 모서리 빛 폭을 줄임 (없으면 NOLINE)
        width = NOLINE
        for i in range(len(kept)):
            for j in range(i + 1, len(kept)):
                if kept[i][2].dot(kept[j][2]) < -0.7:
                    width = min(width, kept[i][3] + kept[j][3])
        lines = [(d, a, nk) for (d, a, nk, _s) in kept]
        if len(lines) > LINES:
            over += 1
        lines = lines[:LINES]
        for loop in f.loops:
            p = W[loop.vert.index]
            vals = [NOLINE + 1.0] * LINES
            for i, (_d, a, nk) in enumerate(lines):
                vals[i] = max(-0.02, (p - a).dot(nk)) + 1.0     # 1 = 테두리 위 · 11 = 선 없음
            loop[uva].uv = (vals[0], vals[1])
            loop[uvb].uv = (vals[2], vals[3])
            loop[uvc].uv = (width, 0.0)
    bm.to_mesh(me)
    bm.free()
    return (len(faces), len(groups), over)


def snapshot(path):
    """FBX를 따로 가져와 조각별 서명 (이름 → (꼭짓점 위치 해시, 면 수, 재질, 꼭짓점 색 해시, 첫 UV 해시, UV 층 수)). 가져온 것은 지움."""
    col = bpy.data.collections.new("_cmp")
    bpy.context.scene.collection.children.link(col)
    lc = bpy.context.view_layer.layer_collection.children[col.name]
    bpy.context.view_layer.active_layer_collection = lc
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before]
    sig = {}
    for o in new:
        if o.type != 'MESH':
            continue
        me = o.data
        name = o.name.split(".")[0]
        M = o.matrix_world
        pos = sorted((round((M @ v.co).x, 4), round((M @ v.co).y, 4), round((M @ v.co).z, 4)) for v in me.vertices)
        h = hashlib.md5(repr(pos).encode()).hexdigest()
        cols = ""
        if me.color_attributes:
            ca = me.color_attributes[0]
            cols = hashlib.md5(repr(sorted(tuple(round(c, 3) for c in d.color) for d in ca.data)).encode()).hexdigest()
        uv0 = ""
        if me.uv_layers:
            uv0 = hashlib.md5(repr(sorted((round(d.uv.x, 4), round(d.uv.y, 4)) for d in me.uv_layers[0].data)).encode()).hexdigest()
        tris = sum(len(p.vertices) - 2 for p in me.polygons)          # 삼각형 수 (오목한 트림 면은 삼각형으로 나눠 내보내니 면 수 대신)
        sig[name] = (h, tris, tuple(m.name.split(".")[0] if m else "" for m in me.materials), cols, uv0, len(me.uv_layers))
    for o in new:
        me = o.data if o.type == 'MESH' else None
        bpy.data.objects.remove(o, do_unlink=True)
        if me is not None and me.users == 0:
            bpy.data.meshes.remove(me)
    bpy.data.collections.remove(col)
    return sig


if not os.path.exists(FBX):
    raise SystemExit("지금 FBX 없음: " + FBX)
old = snapshot(FBX)
objs = []
for n in sorted(old):
    o = bpy.data.objects.get(n)
    if o is None or o.type != 'MESH':
        raise SystemExit("blend에 조각 없음: " + n)
    objs.append(o)
stats = {}
for o in objs:
    stats[o.name] = trim_uv(o)
tf = sum(s[0] for s in stats.values())
print("TRIM", NAME, "faces", tf, "groups", sum(s[1] for s in stats.values()), "over4", sum(s[2] for s in stats.values()))
for n, s in stats.items():
    if s[0]:
        print("   ", n, s)

has_col = any(o.data.color_attributes for o in objs)
result = None
for ctype in (["LINEAR", "SRGB"] if has_col else ["SRGB"]):
    out = os.path.join(TMP, "SM_Interior_%s.fbx" % NAME)
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, colors_type=ctype, **EXPORT)
    new = snapshot(out)
    diff = []
    if set(new) != set(old):
        diff.append(("names", sorted(set(old) ^ set(new))))
    for n in sorted(set(old) & set(new)):
        a, b = old[n], new[n]
        for i, what in enumerate(("pos", "tris", "mats", "colors", "uv0")):
            if what == "uv0" and a[5] == 0:
                continue
            if a[i] != b[i]:
                diff.append((n, what))
    print("COMPARE colors", ctype, "diff", diff[:12], "uvlayers", sorted({(n, old[n][5], new[n][5]) for n in new if n in old and new[n][5] != old[n][5]})[:6])
    if not diff:
        result = out
        break
if result is None:
    print("RESULT FAIL — 지금 FBX와 모양이 달라 덮어쓰지 않음")
elif WRITE:
    shutil.copyfile(result, FBX)
    print("RESULT WROTE", FBX)
else:
    print("RESULT OK (확인만 — --write로 덮어씀)", result)
