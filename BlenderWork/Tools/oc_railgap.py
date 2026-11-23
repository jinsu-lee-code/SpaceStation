# 궤도 도시 지금 FBX에 1층 둘레 난간벽 틈 스물 (모든 면 연결 2026-11-19, 사용자 "1층은 어떤 칸이든 이어지게") — oc_builder 텍스트가
#   마지막 내보내기 뒤에 바뀌어 다시 지으면 잎 · 몸체 위치 · UV0가 지금 FBX와 달라짐 → .blend 모델 (FBX와 같음)의 난간만 자름
#   blender -b BlenderWork/Interior_Evo.blend -P BlenderWork/Tools/oc_railgap.py -- [--write]   (.blend는 저장 안 함)
# 난간 단면 (곧은 가장자리, 칸 중심에서 문 쪽 거리 d · 바닥 위 y): 테두리 턱 (진주) d 3.32 ~ 3.61 · 윗면 0.12, 그 위 벽 (진주) d 3.41 ~ 3.52 · 0.1 ~ 0.97,
#   금 띠 d 3.39 ~ 3.55 · 0.97 ~ 1.05 → 문 앞 ±HW만 벽 · 금 띠를 지우고 잘린 끝을 막음 (턱 윗면 0.12는 그대로 = 문턱). 둥근 모서리 칸은 난간이 d 2.6부터 휨.
# 가운데 네 면 문 자리 유리 원뿔 등 (금 받침 + 유리 원뿔, 높이 2.9)은 문 양옆 ±LAMP_S로 하나씩 (문기둥 등 한 쌍).
import bpy, bmesh, sys, os, shutil
from mathutils import Vector
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
WRITE = "--write" in ARGS
TOOL = r"C:\Users\mbc\Desktop\Unity\SpaceStation\BlenderWork\Tools\trim_uv_export.py"
sys.argv = [sys.argv[0], "--"]
g = {"__name__": "trim_mod", "__file__": TOOL}
exec(open(TOOL, encoding="utf-8").read(), g)
NAME = "EvoOrbitalCity"
path = g["fbx_path"](NAME)
old = g["snapshot"](path)
objs = [bpy.data.objects[n] for n in sorted(old)]
for o in objs:
    assert o.matrix_world.is_identity, o.name

F = -1.3
HW = 1.7            # 틈 반 폭 (문 패널 ±1.6 + 여유)
LAMP_S = 2.15       # 가운데 면 등 옆 비낌
CS = 8.0
FACES = "(0,0,0)-x (0,0,0)-z (1,0,0)-z (2,0,0)-z (3,0,0)-z (4,0,0)+x (4,0,0)-z (0,0,1)-x (4,0,1)+x (0,0,2)-x (4,0,2)+x (0,0,3)-x (4,0,3)+x " \
        "(0,0,4)-x (0,0,4)+z (1,0,4)+z (2,0,4)+z (3,0,4)+z (4,0,4)+x (4,0,4)+z".split()
CORNER = {(0, 0), (4, 0), (0, 4), (4, 4)}
MIDDLE = {(2, 0), (0, 2), (4, 2), (2, 4)}

def unity(v):
    return (-v.x, v.z, -v.y)

def parse(f):
    c = eval(f[:-2])
    sgn = 1 if f[-2] == "+" else -1
    d = (sgn, 0) if f[-1] == "x" else (0, sgn)
    return (c[0], c[2]), d

shell = next(o for o in objs if o.name.endswith("_Shell"))
glass = next(o for o in objs if o.name.endswith("_Glass"))
mats = [m.name if m else "" for m in shell.data.materials]
PEARL, GOLD = mats.index("Evo3Pearl"), mats.index("Evo3Gold")
tri0 = {o.name: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in (shell, glass)}

def frame(cell, d):
    cx, cz = cell[0] * CS, cell[1] * CS
    dx, dz = d
    def da(v):
        ux, uy, uz = unity(v.co)
        return (ux - cx) * dx + (uz - cz) * dz, (uz - cz) if dx else (ux - cx), uy - F
    return cx, cz, da

# 가운데 면 등: 금 받침 (Shell) · 유리 원뿔 (Glass) 덩어리 → +LAMP_S로 옮기고 −LAMP_S에 하나 더
def islands(bm):
    seen = set(); out = []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack = [f]; comp = []; seen.add(f.index)
        while stack:
            h = stack.pop(); comp.append(h)
            for e in h.edges:
                for k in e.link_faces:
                    if k.index not in seen:
                        seen.add(k.index); stack.append(k)
        out.append(comp)
    return out

moved = 0
for o in (shell, glass):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    comps = islands(bm)
    for f in FACES:
        cell, d = parse(f)
        if cell not in MIDDLE:
            continue
        cx, cz = cell[0] * CS + d[0] * 3.05, cell[1] * CS + d[1] * 3.05
        lamp = []
        for comp in comps:
            us = [unity(v.co) for v in {v for x in comp for v in x.verts}]
            lo = [min(u[i] for u in us) for i in range(3)]; hi = [max(u[i] for u in us) for i in range(3)]
            if abs((lo[0] + hi[0]) / 2 - cx) < 0.3 and abs((lo[2] + hi[2]) / 2 - cz) < 0.3 and hi[0] - lo[0] < 1.0 and hi[2] - lo[2] < 1.0:
                lamp += comp
        assert lamp, (o.name, f)
        side = Vector((0, -1, 0)) if d[0] else Vector((-1, 0, 0))   # Unity 옆 축 (x 면 = z, z 면 = x) → Blender
        verts = list({v for x in lamp for v in x.verts})
        dup = bmesh.ops.duplicate(bm, geom=lamp + list({e for x in lamp for e in x.edges}) + verts)
        bmesh.ops.translate(bm, verts=[v for v in dup["geom"] if isinstance(v, bmesh.types.BMVert)], vec=side * (-LAMP_S))
        bmesh.ops.translate(bm, verts=verts, vec=side * LAMP_S)
        moved += 1
    bm.to_mesh(o.data); bm.free()
print("LAMPS", moved)

bm = bmesh.new(); bm.from_mesh(shell.data)
killed = capped = 0
for f in FACES:
    cell, d = parse(f)
    cx, cz, da = frame(cell, d)
    d0 = 2.0 if cell in CORNER else 3.3   # 모서리 칸 난간은 d 2.4 앞에서부터 휨
    def rail(face, lim):
        if face.material_index not in (PEARL, GOLD):
            return False
        ys, ss = [], []
        for v in face.verts:
            dd, s, y = da(v)
            if not (d0 <= dd <= 3.58 and 0.095 <= y <= 1.06):
                return False
            ys.append(y); ss.append(s)
        if all(abs(y - 0.12) < 0.003 for y in ys):
            return False   # 테두리 턱 윗면 (문턱으로 남김)
        if min(ys) >= 0.2 and max(ys) <= 0.31:
            return False   # 등 금 받침 윗면 (0.3 — 모서리 등이 d 2.1까지)
        return max(ss) >= -lim and min(ss) <= lim
    def opens():
        return [e for e in bm.edges if e.is_boundary and all(d0 - 0.05 <= da(v)[0] <= 3.6 and 0.09 <= da(v)[2] <= 1.07 and abs(da(v)[1]) <= HW + 0.05 for v in e.verts)]
    open0 = len(opens())
    band = [x for x in bm.faces if rail(x, HW + 0.01)]
    for sgn in (-1, 1):
        lat = sgn * HW
        if d[0]:
            co, no = Vector((0, -(cz + lat), 0)), Vector((0, 1, 0))
        else:
            co, no = Vector((-(cx + lat), 0, 0)), Vector((1, 0, 0))
        geom = list({e for x in band for e in x.edges} | set(band) | {v for x in band for v in x.verts})
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=co, plane_no=no)
        band = [x for x in bm.faces if rail(x, HW + 0.01)]
    kill = [x for x in band if all(abs(da(v)[1]) <= HW + 1e-4 for v in x.verts)]
    assert kill, f
    bmesh.ops.delete(bm, geom=kill, context='FACES')
    killed += len(kill)
    # 잘린 끝 막기 (벽 · 금 띠 따로 — 끝 평면 위 경계 고리)
    for sgn in (-1, 1):
        ends = [e for e in bm.edges if e.is_boundary and all(abs(da(v)[1] - sgn * HW) < 2e-4 and d0 - 0.01 <= da(v)[0] <= 3.6 and 0.09 <= da(v)[2] <= 1.07 for v in e.verts)]
        if not ends:
            continue
        ret = bmesh.ops.holes_fill(bm, edges=ends, sides=0)
        print("   END", sgn, "edges", len(ends), "caps", len(ret["faces"]))
        for x in ret["faces"]:
            x.material_index = GOLD if sum(da(v)[2] for v in x.verts) / len(x.verts) > 0.975 else PEARL
            capped += 1
        bmesh.ops.triangulate(bm, faces=[x for x in ret["faces"] if len(x.verts) > 4])
    rest = opens()
    print("FACE", f, "kill", len(kill), "open before", open0, "after", len(rest), [tuple(round(c, 2) for c in da(e.verts[0])) for e in rest][:4])
left = [e for e in bm.edges if e.is_boundary and 0.09 <= unity(e.verts[0].co)[1] - F <= 1.07 and e.link_faces and e.link_faces[0].material_index in (PEARL, GOLD)]
print("KILLED", killed, "CAPPED", capped, "open rail edges left", len(left), [tuple(round(c, 2) for c in unity((e.verts[0].co + e.verts[1].co) / 2)) for e in left][:6])
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
