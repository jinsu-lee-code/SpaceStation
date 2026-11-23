# FBX에서 조각 빼기 (2026-11-20, 위아래 이동 장치 — 사용자 "원형으로 상하 이동장치 배치위치가 남아있는데 … 지우고 상하장치 하나로 통일"):
# 게임에 들어간 FBX를 그대로 불러 상자 · 원기둥 안에 통째로 든 조각 (이어진 면 덩어리)만 지우고 같은 설정으로 다시 내보냄.
# 빌더를 다시 짓지 않는 까닭: 진화 빌더 글 (Interior_Evo · Evo2.blend)은 내보낸 뒤 고친 단계 (착지 원판 · 트림 UV 등)가 빠져 있어 지금 FBX와 면 수가 다름.
# 다시 내보낸 FBX를 한 번 더 불러 고치지 않은 물체가 처음과 같은지 (이름 · 면 · 정점 위치 · UV · 재질) 확인한 뒤에만 덮어씀.
#
# 쓰기: blender -b --factory-startup -P BlenderWork/Tools/fbx_trim.py -- <spec.json> [--dry | --debug (걸친 조각까지 보임)]
# spec = [{"fbx": 경로, "what": 설명, "cuts": [{"cyl": [x, z, r], "y": [y0, y1]} 또는 {"box": [x0, x1, y0, y1, z0, z1]}, ...],
#          "mat": [재질 이름 일부, ...] (선택 — 조각 면 재질이 하나라도 맞아야), "maxsize": 조각 가로 최대 (선택, 기본 3.0), "maxh": 조각 높이 최대 (선택 — 바닥 표시만)}]
# 좌표 = Unity 모듈 로컬 (x, y, z). Blender 월드 (bx, by, bz) = (−x, −z, y).
import bpy, bmesh, sys, json, os, shutil, tempfile

args = sys.argv[sys.argv.index("--") + 1:]
SPEC = json.load(open(args[0], encoding="utf-8"))
DRY = "--dry" in args or "--debug" in args
DEBUG = "--debug" in args
EXPORT = dict(object_types={'MESH'}, axis_forward='-Z', axis_up='Y', bake_space_transform=True,
              apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')


def clear():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.textures):   # 재질이 쌓이면 이름에 .001이 붙어 Unity 재질 연결이 끊김
        for d in list(coll):
            coll.remove(d)


def load(path):
    clear()
    tmp = os.path.join(tempfile.gettempdir(), "fbx_trim_in.fbx")
    shutil.copyfile(path, tmp)
    bpy.ops.import_scene.fbx(filepath=tmp, bake_space_transform=True)   # 내보낼 때처럼 축 변환을 메시에 구움 — 안 그러면 물체마다 90° 회전이 남아 Unity 프리팹 (회전 없음)에서 방이 눕혀짐
    return {o.name: o for o in bpy.data.objects if o.type == 'MESH'}


def unity(o, co):
    w = o.matrix_world @ co
    return (-w.x, w.z, -w.y)


def signature(o):
    me = o.data
    pos = [unity(o, v.co) for v in me.vertices]
    uvs = [[(d.uv.x, d.uv.y) for d in uv.data] for uv in me.uv_layers]
    mats = [m.name if m else "" for m in me.materials]
    tr = tuple(round(x, 4) for row in o.matrix_basis for x in row)   # 물체 변환 (Unity 노드 자리 · 회전) — 월드 정점만 보면 회전이 메시로 옮겨 간 것을 못 봄
    head = (len(me.vertices), len(me.polygons), len(me.uv_layers), tuple(mats), o.parent.name if o.parent else "", me.name, tr)
    return head, pos, uvs


def same(a, b, tol=2e-4):
    # 정점 · UV 순서가 그대로인지 + 허용 오차 (FBX 축 · 단위 변환을 두 번 거친 작은 수치 차이)
    if a[0] != b[0]:
        return False
    if any(abs(p - q) > tol for u, v in zip(a[1], b[1]) for p, q in zip(u, v)):
        return False
    return all(abs(p - q) <= tol for la, lb in zip(a[2], b[2]) for u, v in zip(la, lb) for p, q in zip(u, v))


def inside(cut, pts):
    test = any if cut.get("partial") else all   # partial = 조금이라도 걸친 조각 (--debug)
    if "box" in cut:
        x0, x1, y0, y1, z0, z1 = cut["box"]
        return test(x0 <= p[0] <= x1 and y0 <= p[1] <= y1 and z0 <= p[2] <= z1 for p in pts)
    cx, cz, r = cut["cyl"]
    y0, y1 = cut["y"]
    return test((p[0] - cx) ** 2 + (p[2] - cz) ** 2 <= r * r and y0 <= p[1] <= y1 for p in pts)


def trim(o, item):
    me = o.data
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    seen, kill, report = set(), [], []
    mats = [m.name if m else "" for m in me.materials]
    for f0 in bm.faces:
        if f0.index in seen:
            continue
        part, stack = [], [f0]
        seen.add(f0.index)
        while stack:
            f = stack.pop()
            part.append(f)
            for v in f.verts:
                for g in v.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        pts = [unity(o, v.co) for v in {v for f in part for v in f.verts}]
        xs, ys, zs = [p[0] for p in pts], [p[1] for p in pts], [p[2] for p in pts]
        if max(max(xs) - min(xs), max(zs) - min(zs)) > item.get("maxsize", 3.0) or max(ys) - min(ys) > item.get("maxh", 99.0):
            continue
        pm = {mats[f.material_index] if f.material_index < len(mats) else "" for f in part}
        if item.get("mat") and not any(k in m for k in item["mat"] for m in pm):
            continue
        if DEBUG and any(inside(dict(c, partial=True), pts) for c in item["cuts"]):
            print("   near", o.name, "%.2f..%.2f %.2f..%.2f %.2f..%.2f" % (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)), len(part), sorted(pm))
        if any(inside(c, pts) for c in item["cuts"]):
            kill.extend(part)
            report.append(("%.2f..%.2f" % (min(xs), max(xs)), "%.2f..%.2f" % (min(ys), max(ys)), "%.2f..%.2f" % (min(zs), max(zs)), len(part), sorted(pm)))
    if kill:
        bmesh.ops.delete(bm, geom=list(set(kill)), context='FACES')
        bm.to_mesh(me)
        me.update()
    bm.free()
    return report


for item in SPEC:
    path = item["fbx"]
    objs = load(path)
    before = {n: signature(o) for n, o in objs.items()}
    removed = {}
    for n, o in objs.items():
        r = trim(o, item)
        if r:
            removed[n] = r
    total = sum(len(r) for r in removed.values())
    print("TRIM", os.path.basename(path), item.get("what", ""), "parts", total)
    for n, r in removed.items():
        for row in r:
            print("   ", n, row)
    if DRY or not total:
        print("NOT EXPORTED", "dry" if DRY else "nothing")
        continue
    out = os.path.join(tempfile.gettempdir(), "fbx_trim_out.fbx")
    for o in bpy.context.view_layer.objects:
        o.select_set(o.type == 'MESH')
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, **EXPORT)
    after = {n: signature(o) for n, o in load(out).items()}
    bad = [n for n in before if n not in removed and (n not in after or not same(before[n], after[n]))]
    miss = [n for n in before if n not in after]
    if bad or miss:
        print("CHECK FAIL", "changed", bad[:5], "missing", miss[:5])
        for n in bad[:3]:
            print("   ", n, before[n][0] == after[n][0], max((abs(p - q) for u, v in zip(before[n][1], after[n][1]) for p, q in zip(u, v)), default=0))
        continue
    shutil.copyfile(out, path)
    print("EXPORTED", path)
