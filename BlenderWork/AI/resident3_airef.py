# 11-11c AI 생성(VARCO3D)용 기준 이미지: 지금 모델(몸 · 머리 · 옷 · 머리카락 파츠)을 정면/옆/뒤 직교로 찍음
# 결과: //AI/ref/<파츠>.png (1024², 흰 배경, 쉬는 자세 T자세) — 실루엣 · 비율 · 위치 기준 (AI가 디테일만 더함)
# Blender 텍스트 블록 `resident3_airef`와 같은 내용 (Interior_Kit.blend 안에 없으면 이 파일을 텍스트로 열어 쓴다)
# 사용: ns = {'bpy': bpy}; exec(bpy.data.texts['resident3_airef'].as_string(), ns); ns['shoot_all']()
import bpy, bmesh, os, math
from mathutils import Vector
OUT = bpy.path.abspath('//AI/ref')
CL = {'bpy': bpy}; exec(bpy.data.texts['resident2_cloth'].as_string(), CL)
HR = {'bpy': bpy}; exec(bpy.data.texts['resident2_hair'].as_string(), HR)
SCENE = 'R2_RefShots'
SKIN = (0.93, 0.8, 0.7, 1); CLOTH = (0.72, 0.74, 0.78, 1); HAIR = (0.45, 0.32, 0.22, 1)

def scene():
    sc = bpy.data.scenes.get(SCENE) or bpy.data.scenes.new(SCENE)
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.resolution_x = sc.render.resolution_y = 1024
    sh = sc.display.shading
    sh.light = 'STUDIO'; sh.color_type = 'OBJECT'; sh.show_cavity = True; sh.cavity_type = 'BOTH'
    sc.render.film_transparent = False
    w = bpy.data.worlds.get('R2_RefWorld') or bpy.data.worlds.new('R2_RefWorld')
    w.color = (1, 1, 1); sc.world = w
    sc.view_settings.view_transform = 'Standard'
    if sc.camera is None:
        cd = bpy.data.cameras.new('R2_RefCam'); cd.type = 'ORTHO'; cd.clip_end = 20
        cam = bpy.data.objects.new('R2_RefCam', cd); sc.collection.objects.link(cam); sc.camera = cam
    return sc

def clear():
    sc = bpy.data.scenes.get(SCENE)
    if not sc: return
    for o in list(sc.collection.objects):
        d = o.data; bpy.data.objects.remove(o)
        if isinstance(d, bpy.types.Mesh) and d.users == 0: bpy.data.meshes.remove(d)
        elif isinstance(d, bpy.types.Camera) and d.users == 0: bpy.data.cameras.remove(d)
    bpy.data.scenes.remove(sc)
    w = bpy.data.worlds.get('R2_RefWorld')
    if w and w.users == 0: bpy.data.worlds.remove(w)

def world_copy(sc, src, keep=None, color=CLOTH):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = src.evaluated_get(dg); me = ev.to_mesh()
    bm = bmesh.new(); bm.from_mesh(me); ev.to_mesh_clear()
    if keep:
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not keep(f.calc_center_median())], context='FACES')
    bm.transform(src.matrix_world)
    me2 = bpy.data.meshes.new(src.name + '_ref'); bm.to_mesh(me2); bm.free()
    o = bpy.data.objects.new(src.name + '_ref', me2); sc.collection.objects.link(o); o.color = color
    for p in me2.polygons: p.use_smooth = True
    return o

def hair_part(sc, pname):
    """파츠 하나(+ 정수리 캡)만의 머리 껍질 — 파츠가 없는 방향의 안쪽 공은 지움."""
    V, F = HR['combo_shell']([HR['PARTS'][pname]], HR['head_bvh']())
    me = bpy.data.meshes.new('hair_' + pname); me.from_pydata([tuple(v) for v in V], [], F)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if (v.co - HR['HC']).length < 0.094], context='VERTS')
    bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new('hair_' + pname, me); sc.collection.objects.link(o); o.color = HAIR
    for p in me.polygons: p.use_smooth = True
    return o

def view_of(pname):
    return 'front' if pname.startswith('front') else 'side' if pname.startswith('side') else 'back'

def shoot(sc, objs, name, view='front', margin=1.12):
    cam = sc.camera
    for o in sc.collection.objects:
        if o.type == 'MESH': o.hide_render = o not in objs
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)]); hi = Vector([max(p[i] for p in pts) for i in range(3)])
    c = (lo + hi) / 2; size = hi - lo
    if view == 'front':
        cam.location = (c.x, lo.y - 5, c.z); cam.rotation_euler = (math.pi / 2, 0, 0); span = max(size.x, size.z)
    elif view == 'back':
        cam.location = (c.x, hi.y + 5, c.z); cam.rotation_euler = (math.pi / 2, 0, math.pi); span = max(size.x, size.z)
    else:  # 인물 왼쪽(+x)에서
        cam.location = (hi.x + 5, c.y, c.z); cam.rotation_euler = (math.pi / 2, 0, math.pi / 2); span = max(size.y, size.z)
    cam.data.ortho_scale = span * margin
    os.makedirs(OUT, exist_ok=True)
    sc.render.filepath = os.path.join(OUT, name + '.png')
    bpy.ops.render.render(write_still=True, scene=sc.name)
    # 다시 맞추기용: 찍은 범위(월드) 기록 → 생성 메시를 이 상자에 맞춰 놓음
    return dict(name=name, view=view, lo=tuple(lo), hi=tuple(hi), scale=span * margin)

def shoot_all():
    sc = scene(); shots = []
    for pre, g in (('R2M_', 'man'), ('R2W_', 'woman')):
        I = CL['info'](pre); body = bpy.data.objects[pre + 'Body']
        shots.append(shoot(sc, [world_copy(sc, body, lambda p: CL['region'](p, I) == 'head', SKIN)], 'head_' + g))
        shots.append(shoot(sc, [world_copy(sc, body, lambda p: CL['region'](p, I) != 'head', SKIN)], 'body_' + g))
        for o in sorted((o for o in bpy.data.collections['INT_Resident2Cloth'].objects if o.name.startswith(pre)), key=lambda o: o.name):
            shots.append(shoot(sc, [world_copy(sc, o)], 'cloth_%s_%s' % (o.name[len(pre) + 2:], g)))
    for pname in HR['PARTS']:
        shots.append(shoot(sc, [hair_part(sc, pname)], 'hair_' + pname, view_of(pname)))
    import json
    with open(os.path.join(OUT, 'shots.json'), 'w') as f: json.dump(shots, f, indent=1)
    clear()
    return shots
