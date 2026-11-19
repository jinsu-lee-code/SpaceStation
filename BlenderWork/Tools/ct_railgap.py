# 화물 환적장 지금 FBX에 갑판 난간 틈 여섯 (빛 다리 문, 모든 면 연결 2026-11-19) — 빌더로 다시 지으면 아치 · 바닥 둘레가 지금 FBX와 달라짐 (공용 함수가 바뀜)
#   blender -b BlenderWork/Interior_Evo.blend -P BlenderWork/Tools/ct_railgap.py -- [--write]   (.blend는 저장 안 함 — 97.5 MB)
import bpy, bmesh, sys, os, shutil, math
from mathutils import Vector
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in ARGS
TOOL = r"C:\Users\mbc\Desktop\Unity\SpaceStation\BlenderWork\Tools\trim_uv_export.py"
sys.argv = [sys.argv[0], "--"]
g = {"__name__": "trim_mod", "__file__": TOOL}
exec(open(TOOL, encoding="utf-8").read(), g)
NAME = "EvoCargoTransfer"
path = g["fbx_path"](NAME)
old = g["snapshot"](path)
objs = [bpy.data.objects[n] for n in sorted(old)]   # Interior_Evo.blend의 원본 조각 (이 FBX를 트림 UV로 내보낸 것)

F = -1.3
HW, D0, D1 = 1.7, 3.66, 3.95   # 틈 반 폭 (문 패널 ±1.6 + 여유) · 문 평면에서 안쪽 0.29 ~ 0 (난간 3.7 ~ 3.9, 물길 테 3.6 앞)
CELLS = [((0.0, 0.0), (-1, 0)), ((0.0, 0.0), (0, -1)), ((8.0, 0.0), (0, -1)), ((16.0, 0.0), (1, 0)), ((0.0, 8.0), (-1, 0)), ((16.0, 8.0), (1, 0))]

def B(x, y, z):
    return Vector((-x, -z, y))

def region(c, d):
    # 난간 띠 (Unity): 문 쪽 축 범위 · 옆 범위 · 높이 — 틈 안 = 옆 ±HW
    cx, cz = c
    dx, dz = d
    if dx:
        a0, a1 = sorted((cx + dx * (D0 - 0.05), cx + dx * (D1 + 0.35)))
        return ('x', a0, a1, cz)
    a0, a1 = sorted((cz + dz * (D0 - 0.05), cz + dz * (D1 + 0.35)))
    return ('z', a0, a1, cx)

def unity(v):
    return (-v.x, v.z, -v.y)

def gap(obj, c, d):
    axis, a0, a1, lc = region(c, d)
    bm = bmesh.new(); bm.from_mesh(obj.data)
    def inside_band(v):
        ux, uy, uz = unity(v.co)
        along = ux if axis == 'x' else uz
        return a0 <= along <= a1 and F + 0.005 < uy < F + 1.75
    band = [f for f in bm.faces if all(inside_band(v) for v in f.verts)]
    for sgn in (-1, 1):
        lat = lc + sgn * HW
        # 옆 경계 평면 (Unity z = lat 또는 x = lat) → Blender: Unity z → −y, Unity x → −x
        if axis == 'x':
            co, no = Vector((0, -lat, 0)), Vector((0, 1, 0))
        else:
            co, no = Vector((-lat, 0, 0)), Vector((1, 0, 0))
        geom = list({e for f in band for e in f.edges} | set(band) | {v for f in band for v in f.verts})
        res = bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no)
        band = [f for f in bm.faces if all(inside_band(v) for v in f.verts)]
    kill = []
    for f in band:
        cc = f.calc_center_median()
        ux, uy, uz = unity(cc)
        lat_v = uz if axis == 'x' else ux
        if abs(lat_v - lc) < HW - 1e-4:
            kill.append(f)
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(obj.data); bm.free()
    return len(kill)

shell = next(o for o in objs if o.name.endswith("_Shell"))
glass = next(o for o in objs if o.name.endswith("_Glass"))
tri0 = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (shell, glass)}
killed = 0
for (c, d) in CELLS:
    for o in (shell, glass):
        killed += gap(o, c, d)
print("KILLED", killed)
# 틈 끝 기둥 (난간 금속 재질) — 잘린 난간 끝을 덮음, UV = 가장 가까운 남은 꼭짓점의 UV
from mathutils import kdtree, Matrix
trim_idx = next((i for i, m in enumerate(shell.data.materials) if m and "Evo2Trim" in m.name), 0)
bm = bmesh.new(); bm.from_mesh(shell.data)
uv_layers = list(bm.loops.layers.uv.values())
added = []
for (c, d) in CELLS:
    cx, cz = c
    dx, dz = d
    for sgn in (-1, 1):
        if dx:
            px, pz = cx + dx * 3.8, cz + sgn * (HW + 0.02)
        else:
            px, pz = cx + sgn * (HW + 0.02), cz + dz * 3.8
        ret = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=10, radius1=0.035, radius2=0.035, depth=1.12,
                                    matrix=Matrix.Translation(B(px, F + 0.56, pz)))
        added += [f for f in {f for v in ret["verts"] for f in v.link_faces}]
for f in added:
    f.material_index = trim_idx
bm.to_mesh(shell.data); bm.free()

# 둥근 웅덩이 (못) 낮은 테 (두께 0.15 · 바닥 위 0.45 — 띠 · 테 머리)가 (1,0,0) −z 문 바로 안 (문 평면 0.2 m 안)을 지나 문 구멍 자리를 막음 →
# 문 앞 x 8 ± CURB_HW만 바닥 위 1 cm에서 가로로 잘라 그 위 테를 지우고 잘린 윗면 · 양 끝을 막음 (바닥 · 물 · 테 아랫도리는 그대로)
CURB_HW, CURB_CUT = 1.0, 0.01
wall_idx = next((i for i, m in enumerate(shell.data.materials) if m and m.name == "Evo2Wall"), trim_idx)
def curb_cut(obj):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    def in_box(v, lo=-1e9, hi=1e9, floor=-1.0):
        ux, uy, uz = unity(v.co)
        return lo <= ux <= hi and -3.79 <= uz <= -2.9 and F + floor <= uy <= F + 0.5
    before = [e for e in bm.edges if e.is_boundary and all(in_box(v, 8.0 - CURB_HW - 0.01, 8.0 + CURB_HW + 0.01, -0.5) for v in e.verts)]
    print("CURB open boundary edges before", len(before))
    region = lambda: [f for f in bm.faces if all(in_box(v, 8.0 - CURB_HW - 0.5, 8.0 + CURB_HW + 0.5) for v in f.verts)]
    for sgn in (-1, 1):
        faces = region()
        geom = list({e for f in faces for e in f.edges} | set(faces) | {v for f in faces for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=Vector((-(8.0 + sgn * CURB_HW), 0, 0)), plane_no=Vector((1, 0, 0)))
    faces = [f for f in bm.faces if all(in_box(v, 8.0 - CURB_HW - 1e-4, 8.0 + CURB_HW + 1e-4) for v in f.verts)]
    geom = list({e for f in faces for e in f.edges} | set(faces) | {v for f in faces for v in f.verts})
    bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=Vector((0, 0, F + CURB_CUT)), plane_no=Vector((0, 0, 1)))
    kill = [f for f in bm.faces if all(in_box(v, 8.0 - CURB_HW - 1e-4, 8.0 + CURB_HW + 1e-4, CURB_CUT - 1e-4) for v in f.verts)]
    from collections import Counter, defaultdict
    rng = defaultdict(lambda: [9, -9, 9, -9])
    for f in kill:
        r = rng[obj.data.materials[f.material_index].name]
        for v in f.verts:
            ux, uy, uz = unity(v.co)
            r[0] = min(r[0], uz); r[1] = max(r[1], uz); r[2] = min(r[2], uy - F); r[3] = max(r[3], uy - F)
    for k, r in rng.items():
        print("CURB RANGE", k, [round(x, 3) for x in r])
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    # 잘린 윗면 (바닥 + 1 cm 경계) · 양 끝 (x = 8 ± HW 평면 경계) 막기 — 테 머리 재질
    capped = 0
    top = [e for e in bm.edges if e.is_boundary and all(abs(unity(v.co)[1] - F - CURB_CUT) < 2e-4 and in_box(v, 8.0 - CURB_HW - 1e-3, 8.0 + CURB_HW + 1e-3) for v in e.verts)]
    ends = [e for e in bm.edges if e.is_boundary and e not in top and all(abs(abs(unity(v.co)[0] - 8.0) - CURB_HW) < 2e-4 and unity(v.co)[1] >= F + CURB_CUT - 2e-4 and in_box(v) for v in e.verts)]
    print("CURB EDGES top", len(top), "ends", len(ends))
    if top or ends:
        ret = bmesh.ops.holes_fill(bm, edges=top + ends, sides=0)
        for f in ret["faces"]:
            f.normal_update()
            f.material_index = trim_idx if abs(f.normal.z) > 0.7 else wall_idx   # 윗면 = 테 머리, 끝 단면 = 테 몸 (Blender z = 위)
            capped += 1
        bmesh.ops.triangulate(bm, faces=[f for f in ret["faces"] if len(f.verts) > 4])
    # 테 머리 띠는 밑이 트인 ㄷ자 — 잘린 끝 ㄷ자를 닫는 면 (끝 평면 위 남은 경계 사슬)
    for sgn in (-1, 1):
        lat = 8.0 + sgn * CURB_HW
        chain = [e for e in bm.edges if e.is_boundary and all(abs(unity(v.co)[0] - lat) < 2e-4 and unity(v.co)[1] >= F + 0.3 and in_box(v) for v in e.verts)]
        if len(chain) >= 2:
            ret = bmesh.ops.contextual_create(bm, geom=chain)
            for f in ret["faces"]:
                f.material_index = trim_idx
                capped += 1
    open_left = [e for e in bm.edges if e.is_boundary and all(in_box(v, 8.0 - CURB_HW - 0.01, 8.0 + CURB_HW + 0.01, -0.5) for v in e.verts)]
    print("CURB open boundary edges left", len(open_left), [tuple(round(c, 2) for c in unity((e.verts[0].co + e.verts[1].co) / 2)) for e in open_left][:8])
    bm.to_mesh(obj.data); bm.free()
    return len(kill), capped

print("CURB", curb_cut(shell))
# 잘린 테 양 끝 = 낮은 기둥 (테 몸 재질 · 머리 테 재질 원판) — 끝 단면을 덮음 (테 가운데 선: x 7 → z −3.64, x 9 → z −3.05)
bm = bmesh.new(); bm.from_mesh(shell.data)
for (bx, bz) in ((8.0 - CURB_HW, -3.64), (8.0 + CURB_HW, -3.05)):
    body = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=16, radius1=0.17, radius2=0.17, depth=0.48,
                                 matrix=Matrix.Translation(B(bx, F + 0.24, bz)))
    for f in {f for v in body["verts"] for f in v.link_faces}:
        f.material_index = wall_idx
    head = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=16, radius1=0.19, radius2=0.19, depth=0.04,
                                 matrix=Matrix.Translation(B(bx, F + 0.49, bz)))
    for f in {f for v in head["verts"] for f in v.link_faces}:
        f.material_index = trim_idx
bm.to_mesh(shell.data); bm.free()
for o in objs:
    g["trim_uv"](o)
tri1 = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (shell, glass)}
print("TRIS", tri0, "->", tri1)
out = os.path.join(g["TMP"], os.path.basename(path))
os.makedirs(g["TMP"], exist_ok=True)
for o in objs:
    o.hide_set(False)
for o in bpy.context.view_layer.objects:
    o.select_set(o in objs)
bpy.context.view_layer.objects.active = objs[0]
has_col = any(o.data.color_attributes for o in objs)
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'MESH'}, colors_type="LINEAR" if has_col else "SRGB", **g["EXPORT"])
new = g["snapshot"](out)
diff = []
for n in sorted(set(old) & set(new)):
    if n.endswith(("_Shell", "_Glass")):
        continue
    a, b = old[n], new[n]
    diff += [(n, w) for w in g["geom_differ"](a[0], b[0])]
    for i, what in ((1, "tris"), (2, "mats"), (3, "colors"), (4, "uv0")):
        if a[i] != b[i]:
            diff.append((n, what))
print("OTHERS", "same" if not diff else diff[:6], "names", sorted(set(old) ^ set(new))[:4])
if WRITE and not diff:
    shutil.copyfile(out, path)
    print("WROTE", path)
