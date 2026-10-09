# PNG 여러 장을 높이 h로 맞춰 가로로 붙임: blender -b --factory-startup -P combine_png.py -- <출력.png> <높이> <입력.png>...
import bpy, sys
import numpy as np
a = sys.argv[sys.argv.index('--') + 1:]
out, h, ins = a[0], int(a[1]), a[2:]
arrs = []
for p in ins:
    im = bpy.data.images.load(p); w, hh = im.size
    nw = max(1, int(w * h / hh)); im.scale(nw, h)
    arrs.append(np.array(im.pixels[:]).reshape(h, nw, 4)); bpy.data.images.remove(im)
A = np.concatenate(arrs, 1)
im = bpy.data.images.new('c', A.shape[1], A.shape[0]); im.pixels = A.astype(np.float32).ravel()
im.filepath_raw = out; im.file_format = 'PNG'; im.save()
