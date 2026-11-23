# PNG 여러 장을 너비 w로 맞춰 세로로 쌓음 (위 → 아래): blender -b --factory-startup -P stack_png.py -- <출력.png> <너비> <입력.png>...
import bpy, sys
import numpy as np
a = sys.argv[sys.argv.index('--') + 1:]
out, w, ins = a[0], int(a[1]), a[2:]
arrs = []
for p in ins:
    im = bpy.data.images.load(p); ww, h = im.size
    nh = max(1, int(h * w / ww)); im.scale(w, nh)
    arrs.append(np.array(im.pixels[:]).reshape(nh, w, 4)); bpy.data.images.remove(im)
A = np.concatenate(list(reversed(arrs)), 0)          # Blender 픽셀은 아래 줄부터 → 위에 올 그림을 뒤에
im = bpy.data.images.new('s', A.shape[1], A.shape[0]); im.pixels = A.astype(np.float32).ravel()
im.filepath_raw = out; im.file_format = 'PNG'; im.save()
