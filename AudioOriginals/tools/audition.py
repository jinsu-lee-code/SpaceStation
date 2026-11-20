"""
사운드 후보 고르기 도구 (사운드 작업 지침, 사용자 2026-11-20 — 사용자가 듣고 고르지 않고 Claude가 그림 · 수치로 판단).
폴더 안 WAV를 이름 앞부분(마지막 _번호 앞)으로 묶어 묶음마다 스펙트로그램 판(sheet_<묶음>.png)을 만들고, 수치 표를 출력한다.
실행: "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/tools/audition.py <wav 폴더> <그림 폴더> [보여 줄 초, 기본 4]
판 읽기: 가로 = 시간 (초록 세로선 = 0.25 · 0.5 · 1초), 세로 = 50 Hz ~ 16 kHz (로그), 밝을수록 셈, 흰 점선 = 소리 크기.
수치: cent = 소리 무게중심(Hz) · low/high = 300 Hz 아래 / 4 kHz 위 비율 · floor = 잡음 바닥(dB) · tail40 = 가장 큰 곳에서 −40 dB까지(초) · flat = 잡음스러움(0 음 ~ 1 잡음) · tonal = 좁은 봉우리(인공 떨림 의심, 3 넘으면 확인)
"""
import sys, wave, glob, os, zlib, struct
import numpy as np
src, out = sys.argv[1], sys.argv[2]
SR = 48000; W, H = 900, 110; T = float(sys.argv[3]) if len(sys.argv) > 3 else 4.0
def load(f):
    w = wave.open(f); x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
    return x
def png(path, img):
    h, w, _ = img.shape
    raw = b"".join(b"\x00" + img[y].astype(np.uint8).tobytes() for y in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))
def row(x):
    n = int(T * SR); x = np.pad(x[:n], (0, max(0, n - len(x))))
    hop = n // W; win = 2048; frames = []
    for i in range(W):
        s = i * hop; seg = x[s:s + win]
        seg = np.pad(seg, (0, win - len(seg))) * np.hanning(win)
        frames.append(np.abs(np.fft.rfft(seg)))
    S = np.array(frames).T  # freq x time
    freqs = np.fft.rfftfreq(win, 1 / SR)
    # 로그 주파수 50 Hz ~ 16 kHz → H 줄
    edges = np.geomspace(50, 16000, H + 1)
    M = np.zeros((H, W))
    for k in range(H):
        m = (freqs >= edges[k]) & (freqs < edges[k + 1])
        if not m.any(): m = np.argmin(abs(freqs - edges[k])) == np.arange(len(freqs))
        M[H - 1 - k] = S[m].mean(0)
    db = 20 * np.log10(M + 1e-9); db = np.clip((db - (db.max() - 70)) / 70, 0, 1)
    img = np.stack([db ** 0.8 * 255, db ** 1.5 * 200, (1 - db) * db * 4 * 120], -1)
    # 소리 크기 선 (흰색), 0.25 s · 1 s 눈금 (초록)
    env = np.array([np.sqrt((x[i * hop:i * hop + hop] ** 2).mean() + 1e-12) for i in range(W)])
    e = np.clip((20 * np.log10(env) + 60) / 60, 0, 1)
    for i in range(W):
        y = int((1 - e[i]) * (H - 1)); img[y, i] = [255, 255, 255]
    for t in (0.25, 0.5, 1.0, 2.0, 3.0):
        (img.__setitem__((slice(None), int(t / T * W) - 1), [0, 200, 80] if t < 2 else [0, 90, 40]) if t < T else None)
    return img
groups = {}
for f in sorted(glob.glob(os.path.join(src, "*.wav"))):
    g = os.path.basename(f).rsplit("_", 1)[0]; groups.setdefault(g, []).append(f)
for g, fs in groups.items():
    rows = []
    for f in fs:
        rows.append(row(load(f))); rows.append(np.full((6, W, 3), 60))
    png(os.path.join(out, f"sheet_{g}.png"), np.concatenate(rows))
# 수치
print("file              cent  low<300 high>4k  floor  tail40  flat  tonal")
for f in sorted(glob.glob(os.path.join(src, "*.wav"))):
    x = load(f); pk = np.abs(x).max()
    win = 2048; hop = 512
    fr = [x[i:i + win] * np.hanning(win) for i in range(0, len(x) - win, hop)]
    S = np.abs(np.fft.rfft(np.array(fr), axis=1)); fq = np.fft.rfftfreq(win, 1 / SR)
    e = np.sqrt((np.array(fr) ** 2).mean(1)); loud = e > e.max() * 0.1
    P = (S[loud] ** 2).sum(0)
    cent = (fq * P).sum() / P.sum()
    low = P[fq < 300].sum() / P.sum(); high = P[fq > 4000].sum() / P.sum()
    floor = 20 * np.log10(np.percentile(e, 10) / e.max() + 1e-9)
    ip = int(np.argmax(e)); below = np.where(e[ip:] < e.max() * 0.01)[0]
    tail = (below[0] * hop / SR) if len(below) else (len(e) - ip) * hop / SR
    gm = np.exp(np.log(S[loud] + 1e-9).mean(1)); am = S[loud].mean(1) + 1e-9
    flat = (gm / am).mean()
    # 음 있는 잡음(인공 떨림): 오래 지속되는 좁은 봉우리 비율
    med = np.median(S[loud], axis=0) + 1e-9
    ton = np.percentile(S[loud].mean(0) / np.convolve(S[loud].mean(0), np.ones(25) / 25, mode="same").clip(1e-9), 99.5)
    print(f"{os.path.basename(f):17s} {cent:5.0f}  {low:5.2f}  {high:5.2f}  {floor:6.1f}  {tail:5.2f}  {flat:4.2f}  {ton:5.1f}")
