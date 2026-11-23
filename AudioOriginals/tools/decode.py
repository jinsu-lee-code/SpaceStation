"""
mp3 → wav 풀기 (이 PC엔 ffmpeg가 없어서 Blender의 소리 모듈 aud로 품). 48 kHz · 16비트 · 원래 채널 수.
실행: "<Blender>/blender.exe" -b --factory-startup --python AudioOriginals/tools/decode.py -- <mp3 폴더 또는 파일> <wav 폴더>
"""
import sys, os, glob, wave
import numpy as np
import aud

args = sys.argv[sys.argv.index("--") + 1:]
src, out = args[0], args[1]
os.makedirs(out, exist_ok=True)
files = [src] if os.path.isfile(src) else sorted(glob.glob(os.path.join(src, "*.mp3")))
for f in files:
    s = aud.Sound(f)
    rate, ch = s.specs
    if int(rate) != 48000:
        s = s.resample(48000, False)
    x = np.asarray(s.data(), dtype=np.float64)  # (샘플, 채널)
    if x.ndim == 1:
        x = x[:, None]
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    dst = os.path.join(out, os.path.splitext(os.path.basename(f))[0] + ".wav")
    w = wave.open(dst, "wb"); w.setnchannels(pcm.shape[1]); w.setsampwidth(2); w.setframerate(48000)
    w.writeframes(pcm.tobytes()); w.close()
    print("decoded", os.path.basename(f), "%.1f s" % (len(pcm) / 48000), "ch", pcm.shape[1], "src rate", rate)
