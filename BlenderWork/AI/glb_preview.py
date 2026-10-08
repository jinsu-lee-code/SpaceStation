import bpy, os, math, sys
from mathutils import Vector

SRC = r"C:\Users\mbc\Desktop\Unity\SpaceStation\BlenderWork\AI\glb"
OUT = r"C:\Users\mbc\AppData\Local\Temp\claude\C--Users-mbc-Desktop-Unity\a19b4944-6261-47b9-adf4-560cc99c115f\scratchpad"

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'; sh.color_type = 'TEXTURE'; sh.show_cavity = True
w = bpy.data.worlds.new('W'); w.color = (0.8, 0.8, 0.82); sc.world = w
sc.view_settings.view_transform = 'Standard'

groups = {
    'bodyhead': [f for f in sorted(os.listdir(SRC)) if f.startswith(('body_', 'head_'))],
    'cloth_m': [f for f in sorted(os.listdir(SRC)) if f.startswith('cloth_') and '_man_' in f],
    'cloth_w': [f for f in sorted(os.listdir(SRC)) if f.startswith('cloth_') and '_woman_' in f],
}
stats = []
for gname, files in groups.items():
    for o in list(bpy.data.objects): bpy.data.objects.remove(o)
    cols = 6
    for i, fn in enumerate(files):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.gltf(filepath=os.path.join(SRC, fn))
        new = [o for o in bpy.data.objects if o not in before]
        meshes = [o for o in new if o.type == 'MESH']
        bpy.context.view_layer.update()
        pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
        lo = Vector([min(p[k] for p in pts) for k in range(3)]); hi = Vector([max(p[k] for p in pts) for k in range(3)])
        size = hi - lo
        tris = sum(len(o.data.polygons) for o in meshes)
        stats.append('%s size %.3f %.3f %.3f tris %d' % (fn, size.x, size.y, size.z, tris))
        s = 0.9 / max(size)
        root = bpy.data.objects.new('R_' + fn, None); sc.collection.objects.link(root)
        for o in new:
            if o.parent is None: o.parent = root
        c = (lo + hi) / 2
        root.scale = (s, s, s)
        root.location = Vector(((i % cols) * 1.1, 0, -(i // cols) * 1.25)) - c * s
        # 라벨
        bpy.ops.object.text_add(location=((i % cols) * 1.1 - 0.5, 0, -(i // cols) * 1.25 + 0.5))
        t = bpy.context.object; t.data.body = fn.replace('.glb', ''); t.data.size = 0.07; t.rotation_euler = (math.pi / 2, 0, 0); t.color = (0, 0, 0, 1)
    rows = math.ceil(len(files) / cols)
    for view, rot, yv in (('front', 0, -10), ('back', math.pi, 10)):
        cam_d = bpy.data.cameras.new('C'); cam_d.type = 'ORTHO'; cam_d.ortho_scale = max(cols * 1.1, rows * 1.25) + 0.3
        cam = bpy.data.objects.new('C', cam_d); sc.collection.objects.link(cam); sc.camera = cam
        cam.location = ((cols - 1) * 1.1 / 2, yv, -(rows - 1) * 1.25 / 2)
        cam.rotation_euler = (math.pi / 2, 0, rot)
        sc.render.resolution_x = 1800; sc.render.resolution_y = int(1800 * (rows * 1.25 + 0.3) / (cols * 1.1 + 0.3))
        cam_d.sensor_fit = 'HORIZONTAL'; cam_d.ortho_scale = cols * 1.1 + 0.3
        sc.render.filepath = os.path.join(OUT, 'glb_%s_%s.png' % (gname, view))
        bpy.ops.render.render(write_still=True)
        bpy.data.objects.remove(cam)
with open(os.path.join(OUT, 'glb_stats.txt'), 'w') as f: f.write('\n'.join(stats))
