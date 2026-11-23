"""긴 실제 녹음에서 큰 소리 순간을 잘라 wav로 (최대 앞 0.2초 ~ 뒤 len초) + 앞 · 뒤 바닥 잡음 · 6 kHz 위 바닥.
실행: <ai env python> cut_events.py <원본> <출력 폴더> <이름 앞> [뒤 길이 초 = 3.5]"""
import sys, os, numpy as np, soundfile as sf, librosa
SR = 48000
src, out, pre = sys.argv[1], sys.argv[2], sys.argv[3]
L = float(sys.argv[4]) if len(sys.argv) > 4 else 3.5
os.makedirs(out, exist_ok=True)
x, sr = sf.read(src, always_2d=True); x = x.mean(1)
x = librosa.resample(x, orig_sr=sr, target_sr=SR) if sr != SR else x
e = 20 * np.log10(np.sqrt(np.convolve(x ** 2, np.ones(480) / 480, mode="same")) + 1e-9)
floor = np.percentile(e, 20)
last = -10 ** 9; k = 0
for c in np.where(e > floor + 18)[0][::240]:
    if c - last < 0.7 * SR:
        continue
    p = c + int(np.argmax(e[c:c + int(0.15 * SR)])); last = p
    a, b = p - int(0.2 * SR), p + int(L * SR)
    if a < 0 or b > len(x):
        continue
    seg = x[a:b]
    pre_db = 20 * np.log10(np.sqrt((seg[:int(0.15 * SR)] ** 2).mean()) + 1e-9)
    pk_db = 20 * np.log10(np.abs(seg).max() + 1e-9)
    sf.write(os.path.join(out, f"{pre}_{k:02d}.wav"), seg / (np.abs(seg).max() + 1e-9) * 0.7, SR, subtype="PCM_16")
    print(f"{pre}_{k:02d}  at {a / SR:7.2f}s  peak {pk_db:6.1f} dBFS  pre-roll {pre_db - pk_db:6.1f} dB")
    k += 1
