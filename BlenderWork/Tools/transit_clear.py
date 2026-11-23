# BlenderWork/Tools/transit_clear.py — 기본 방 벽 가구 하나 빼기 (위아래 이동 장치 벽 자리, 2026-11-20): int_common에 TRANSIT_CLEAR (상자 안에 통째로 든 부품은 join에서 뺌)를 더하고,
# 빌더마다 TRANSIT_CLEAR 줄을 넣어 다시 짓고 → 비다양체 검사 → FBX 내보내기 (nb_run과 같은 설정) → .blend 저장. 옛 FBX와 면 수 비교.
#   blender -b <blend> --factory-startup -P BlenderWork/Tools/transit_clear.py -- <spec.json> [--dry]
#   spec = [{"key": "medical_builder", "mod": "Medical", "fbx": "...SM_Interior_Medical.fbx", "box": [x0, x1, y0, y1, z0, z1], "what": "진료 책상 · 의자"}, ...]
import bpy, sys, io, contextlib, json, os, shutil, tempfile, traceback
args = sys.argv[sys.argv.index("--") + 1:]
SPEC = json.load(open(args[0], encoding="utf-8"))
DRY = "--dry" in args
def _show(lc):
    lc.exclude = False; lc.hide_viewport = False; lc.collection.hide_viewport = False
    for c_ in lc.children:
        _show(c_)
for c_ in bpy.context.view_layer.layer_collection.children:
    _show(c_)

MARK = "TRANSIT_CLEAR = []"
ic = bpy.data.texts["int_common"]
src = ic.as_string()
if MARK not in src:
    old = "def join(parts, name):\n    bpy.ops.object.select_all(action='DESELECT')\n"
    assert src.count(old) == 1, "join 모양이 다름"
    new = '''TRANSIT_CLEAR = []   # 위아래 이동 장치 벽 자리 (2026-11-20, 사용자 "벽 가구 하나 빼고 벽 장치"): 빌더가 Unity 상자 (x0, x1, y0, y1, z0, z1)를 넣으면 경계 상자가 그 안에 통째로 든 부품은 합치지 않음

def _cleared(o):
    if not TRANSIT_CLEAR or o.type != 'MESH' or not o.data.vertices:
        return False
    vs = [o.matrix_world @ v.co for v in o.data.vertices]
    xs = [-v.x for v in vs]; ys = [v.z for v in vs]; zs = [-v.y for v in vs]
    return any(min(xs) >= b[0] and max(xs) <= b[1] and min(ys) >= b[2] and max(ys) <= b[3] and min(zs) >= b[4] and max(zs) <= b[5] for b in TRANSIT_CLEAR)

def join(parts, name):
    flags = [_cleared(o) for o in parts]
    if any(flags):
        print("TRANSIT_CLEAR", name, [o.name.split(".")[0] for o, f in zip(parts, flags) if f])
        drop = [o for o, f in zip(parts, flags) if f]
        parts = [o for o, f in zip(parts, flags) if not f]
        for o in drop:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
'''
    ic.from_string(src.replace(old, new, 1))
    print("INT_COMMON PATCHED")
ic.use_fake_user = True

EXEC = 'exec(bpy.data.texts["int_common"].as_string(), g)\n'
for item in SPEC:
    key, mod, fbx, box, what = item["key"], item["mod"], item["fbx"], item["box"], item["what"]
    t = bpy.data.texts[key]
    s = t.as_string()
    line = "TRANSIT_CLEAR = [(%s)]   # 위아래 이동 장치 벽 자리 (2026-11-20, 사용자 \"벽 가구 하나 빼고 벽 장치\"): 이 상자 (Unity x0, x1, y0, y1, z0, z1) 안 부품 = %s — 합치지 않음\n" % (
        ", ".join("%.3f" % v for v in box), what)
    lines = s.split("\n")
    lines = [l for l in lines if not l.startswith("TRANSIT_CLEAR = ")]
    s2 = "\n".join(lines)
    assert s2.count(EXEC) == 1, key + ": int_common 줄을 못 찾음"
    s2 = s2.replace(EXEC, EXEC + line, 1)
    t.from_string(s2)
    t.use_fake_user = True
    NAME = "INT_%s_" % mod
    g = {"__name__": key}
    buf = io.StringIO()
    try:
        with contextlib.redirect_stdout(buf):
            exec(compile(s2, key, "exec"), g)
    except Exception:
        print("BUILD FAIL", key, buf.getvalue()[-800:], traceback.format_exc()[-1500:])
        continue
    removed = [l for l in buf.getvalue().splitlines() if l.startswith("TRANSIT_CLEAR")]
    print("REMOVED", key, removed)
    objs = sorted((o for o in bpy.data.objects if o.name.startswith(NAME)), key=lambda o: o.name)
    newp = {o.name: len(o.data.polygons) for o in objs}
    bad = []
    for o in objs:
        if o.name.endswith(("_Sky", "_Curtain", "_Water", "_DialFace")) or "Bolt" in o.name:
            continue
        r = g["check"](o.name) if "check" in g else (0, 0, 0, 0)
        if r[1] or r[2] or r[3]:
            bad.append((o.name[len(NAME):], r))
    print("CHECKBAD", key, bad)
    # 옛 FBX 면 수
    tmp = os.path.join(tempfile.gettempdir(), "clear_old_%s.fbx" % mod)
    shutil.copyfile(fbx, tmp)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=tmp)
    oldp = {}
    for o in set(bpy.data.objects) - before:
        if o.type == 'MESH':
            oldp[o.name.split(".")[0]] = len(o.data.polygons)
        bpy.data.objects.remove(o, do_unlink=True)
    print("POLYS", key, {k: (oldp.get(k), v) for k, v in newp.items()})
    if not removed:
        print("NOTHING REMOVED — 내보내지 않음", key)
        continue
    if bad or DRY:
        print("NOT EXPORTED", key, "dry" if DRY else bad)
        continue
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE')
    print("EXPORTED", key, fbx)
if not DRY:
    bpy.ops.wm.save_mainfile()
    print("SAVED", bpy.data.filepath)
