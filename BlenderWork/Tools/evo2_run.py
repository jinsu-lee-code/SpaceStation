# Evo2 방 다시 짓기 (2026-11-18, lift_run 틀 + 원래 내보내기 dsu/ds_export.py와 같은 마무리: 몸체 밖 조각 clean · 꼭짓점 색 LINEAR): 방 하나를 다시 지어 검사 · 내보냄 — 그 방 빌더 텍스트만 바꾸고 공용 텍스트 (int_common · evo_kit · evo2_kit)는 .blend 것 그대로 (체크리스트 237).
#   blender -b BlenderWork/Interior_Evo2.blend -P BlenderWork/Tools/evo2_run.py -- <빌더 키 (ds4 · ai3 · op3 · rc3)> <방 이름 (EvoAICore …)> <빌더 파일> [--export] [--save] [--noclean]   → 뒤에 trim_uv_export.py -- <방> --write
#   출력: TRIS · CHECKBAD (열린 · 비다양체 · 뒤집힌 덩어리) · SPOTS 줄 수 · 조각 이름 — 고치기 전 빌더 (--export 없이)와 견줌
import bpy, io, contextlib, traceback, sys, hashlib

args = sys.argv[sys.argv.index("--") + 1:]
KEY, MOD, SRC = args[0], args[1], args[2]
NAME = "INT_%s_" % MOD
OUT = "C:/Users/mbc/Desktop/Unity/SpaceStation/SpaceStation/Assets/_Project/Art/Models/Interior/SM_Interior_%s.fbx" % MOD
src = open(SRC, encoding="utf-8").read()
t_ = bpy.data.texts.get(KEY + "_builder") or bpy.data.texts.new(KEY + "_builder")
t_.from_string(src)
t_.use_fake_user = True
g = {"__name__": KEY + "_builder"}
buf = io.StringIO()
try:
    with contextlib.redirect_stdout(buf):
        exec(compile(src, KEY + "_builder", "exec"), g)
except Exception:
    print(buf.getvalue()[-1500:])
    print(traceback.format_exc()[-3000:])
    raise SystemExit(1)
lines = buf.getvalue().splitlines()
print("SPOTSLINES", len([l for l in lines if l.startswith("SPOTS")]), hashlib.md5("\n".join(l for l in lines if l.startswith(("SPOTS", "PLANTS"))).encode()).hexdigest())
for l in lines:
    if l.startswith(("HATCH", "LIFT", "LAND", "WARN")):
        print(l)
objs = sorted((o for o in bpy.data.objects if o.name.startswith(NAME)), key=lambda o: o.name)
for o in objs:   # --noclean = 빌더가 스스로 정리하는 방 (Interior_Evo.blend에서 처음 지은 2단계 방 — run()이 Shell · Glass만 clean, 하늘 · 빛 조각은 그대로)
    if not o.name.endswith("_Shell") and "--noclean" not in args:
        g["clean"](o)
print("OBJS", len(objs), [o.name[len(NAME):] for o in objs])
tri = {o.name[len(NAME):]: sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs}
print("TRIS", sum(tri.values()), tri)
bad = []
for o in objs:
    if o.name.endswith(("_Sky", "_Curtain", "_Water", "_DialFace", "_Leaves", "_Fx")) or "Bolt" in o.name:   # 한쪽만 보이는 판 · 잎 · 빛 (06b)
        continue
    r = g["check"](o.name)
    if r[1] or r[2] or r[3]:
        bad.append((o.name[len(NAME):], r))
print("CHECKBAD", bad)
if "--export" in args:
    for o in bpy.context.view_layer.objects:
        o.select_set(o in objs)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', colors_type='LINEAR')
    print("EXPORTED", OUT)
if "--save" in args:
    bpy.ops.wm.save_mainfile()
    print("SAVED", bpy.data.filepath)
