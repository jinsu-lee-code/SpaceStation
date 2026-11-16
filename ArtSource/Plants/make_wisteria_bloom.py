# 꽃송이 단 등나무 벽 덩굴 (2026-11-16 AI 중추 — 사용자 "생울타리 위 이파리는 없애고 덩굴과 꽃으로 입체감")
#  = 샘플 벽 덩굴 (WisteriaVineWall, 잎 · 가지 판 72 삼각형) + 등나무 꽃송이 판 (샘플 땅 덩굴의 빽빽한 꽃송이 UV 칸) 일곱 송이 (엇갈린 판 두 장씩)
#  보라 = WisteriaVineWallBloom_Mesh (꽃송이도 잎 재질 이름 → 잎 · 꽃 칸), 금빛 = WisteriaVineWallBloomGold_Mesh (꽃송이 재질 WisteriaGold_Mat →
#  아틀라스 '우리 그림' 칸의 금빛 꽃송이 — PlantAtlas.GoldRaceme, AI 중추 뒤 반구 길 찾기 색). 벽 덩굴만으로는 짙은 잎이 생울타리 잎과 같아 묻혔음 (플레이 확인).
# 실행: blender -b -P ArtSource/Plants/make_wisteria_bloom.py  → Assets/_Project/Art/Plants/Meshes 에 FBX 둘 (거리별 단순화 _LOD0 ~ 2)
import bpy, bmesh, os, math, random
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
MESH = os.path.normpath(os.path.join(HERE, "../../SpaceStation/Assets/_Project/Art/Plants/Meshes"))
RACEMES = (7, 7, 4)                              # LOD별 꽃송이 수


def unity(p):
    # Blender 세계 좌표 → Unity 좌표 (FBX -Z 앞 · Y 위)
    return Vector((-p.x, p.z, -p.y))


def blender(p):
    return Vector((-p[0], -p[2], p[1]))


def load(name):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(MESH, name + ".fbx"))
    out = {}
    for o in bpy.data.objects:
        if o in before or o.type != 'MESH':
            continue
        for k in range(3):
            if o.name.startswith("_LOD%d" % k):
                out[k] = o
    return out


def read(o):
    # 면 목록: (Unity 꼭짓점 셋 이상, UV, Unity 고리 법선, 재질 이름)
    me = o.data
    mw = o.matrix_world
    nm = mw.to_3x3()
    uv = me.uv_layers.active.data
    ln = [Vector(c.vector) for c in me.corner_normals]                         # Blender 4.1+ 고리 법선
    faces = []
    for p in me.polygons:
        vs = [unity(mw @ me.vertices[me.loops[li].vertex_index].co) for li in p.loop_indices]
        uvs = [tuple(uv[li].uv) for li in p.loop_indices]
        ns = [unity((nm @ ln[li]).normalized()) for li in p.loop_indices]
        faces.append((vs, uvs, ns, me.materials[p.material_index].name if me.materials else ""))
    return faces


def raceme_rect(faces):
    # 빽빽한 꽃송이 카드 (잎 · 꽃 칸 u 0.60 ~ 0.67 · v 0.23 ~ 0.32)
    us = [u for (_, uvs, _, m) in faces if "bark" not in m.lower() for u in uvs]
    sel = [u for u in us if 0.60 <= u[0] <= 0.67 and 0.23 <= u[1] <= 0.32]
    a = np.array(sel)
    return a[:, 0].min(), a[:, 0].max(), a[:, 1].min(), a[:, 1].max()


def anchors(faces, k, rr):
    # 꽃송이를 매달 자리: 잎 판 꼭짓점 중 높이 −1.5 ~ −0.35 · 앞쪽 (z 작음) — 서로 먼 점부터 (x · y)
    pts = [v for (vs, _, _, _) in faces for v in vs if -1.5 < v.y < -0.35]
    pts.sort(key=lambda v: v.z)
    pts = pts[:max(k * 4, len(pts) // 2)]
    pick = [max(pts, key=lambda v: -v.y)]
    while len(pick) < k:
        pick.append(max(pts, key=lambda v: min((v.x - q.x) ** 2 + (v.y - q.y) ** 2 for q in pick)))
    return [Vector((v.x, v.y, v.z - 0.03)) for v in pick]


def racemes(at, rect, rr, cross):
    # 꽃송이 = 위 끝에 매달려 늘어진 판 (엇갈린 두 장 ±40° · 앞 위를 보는 법선): 높이 0.36 ~ 0.48 (덩굴 크기 1 기준), 폭 = 높이 / 1.45
    u0, u1, v0, v1 = rect
    out = []
    n = Vector((0.0, 0.45, -1.0)).normalized()
    for a in at:
        h = rr.uniform(0.36, 0.48)
        w = h / 1.45
        tilt = rr.uniform(-0.12, 0.12)
        d = Vector((tilt, -1.0, -0.12)).normalized()
        yaw0 = rr.uniform(-0.3, 0.3)
        for s in ((-1.0, 1.0) if cross else (0.0,)):
            ang = yaw0 + s * math.radians(40)
            r = Vector((math.cos(ang), 0.0, math.sin(ang)))
            q = [a - r * w / 2, a + r * w / 2, a + r * w / 2 + d * h, a - r * w / 2 + d * h]
            out.append((q, [(u0, v1), (u1, v1), (u1, v0), (u0, v0)], [n] * 4))
    return out


def build(name, faces, extra, extra_mat):
    mats = []
    verts, polys, uvs, norms, mids = [], [], [], [], []
    def mat_index(m):
        if m not in mats:
            mats.append(m)
        return mats.index(m)
    for (vs, uv, ns, m) in faces + [(q, u, n, extra_mat) for (q, u, n) in extra]:
        k = len(verts)
        verts.extend(blender(v) for v in vs)
        polys.append(list(range(k, k + len(vs))))
        uvs.extend(uv)
        norms.extend(blender(n_) for n_ in ns)
        mids.append(mat_index(m))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], polys)
    lay = me.uv_layers.new(name="UVMap")
    for i, u in enumerate(uvs):
        lay.data[i].uv = u
    for m in mats:
        mt = bpy.data.materials.get(m)
        if mt is None:                                                            # 꽃송이 재질 = 잎 재질 복사 (빈 새 재질은 Unity 가져오기 결과가 매번 달랐음 — inconsistent result 경고)
            mt = bpy.data.materials[mats[0]].copy()
            mt.name = m
        me.materials.append(mt)
    for p, mi in zip(me.polygons, mids):
        p.material_index = mi
    me.normals_split_custom_set([tuple(n_) for n_ in norms])
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    return o


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    wall = {k: read(o) for k, o in load("WisteriaVineWall_Mesh").items()}
    ground = read(load("WisteriaVineGround_Mesh")[0])
    rect = raceme_rect(ground)
    leaf_mat = wall[0][0][3]
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for (suffix, rmat) in (("", leaf_mat), ("Gold", "WisteriaGold_Mat")):
        objs = []
        for k in range(3):
            rr = random.Random(5 + k)
            at = anchors(wall[0], RACEMES[k], random.Random(5))
            extra = racemes(at, rect, random.Random(9), cross=k < 2)
            objs.append(build("_LOD%d" % k, wall[k], extra, rmat))
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        path = os.path.join(MESH, "WisteriaVineWallBloom%s_Mesh.fbx" % suffix)
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y',
                                 bake_space_transform=True, apply_scale_options='FBX_SCALE_ALL', mesh_smooth_type='FACE', use_mesh_modifiers=False)
        print("BLOOM", path, [sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objs], "raceme uv", [round(x, 3) for x in rect])
        for o in objs:
            bpy.data.objects.remove(o, do_unlink=True)


main()
