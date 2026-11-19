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
