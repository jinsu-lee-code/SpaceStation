"""
음악이 "톡톡 튀는지 · 차분한지" 재기 (2026-11-23 사용자 "바깥의 침착하고 차분한 음악에 비해 내부 음악이 너무 톡톡 튄다").
실행: "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/tools/calm_check.py <wav 또는 폴더> ...
수치:
  타악 = 뜯고 두드리는 성분 비율 (HPSS: 시간 방향으로 짧게 솟는 칸 ÷ 전체, 300 Hz ~ 8 kHz). 높을수록 톡톡.
  어택/초 = 1초에 또렷한 어택(300 Hz 위 onset이 그 곡 중앙값의 3배 넘음) 수. 높을수록 바쁨.
  고음 어택 = 2 kHz 위에서 솟는 성분 비율 (딸깍 · 칼림바 · 오르골처럼 밝게 튀는 소리).
  무게중심 · 300 Hz 아래 = 음악 무게 (바깥 낮 곡 = 215 Hz · 80 %).
  지속 = 화음 성분(HPSS 하모닉)이 다음 프레임까지 남는 정도 (높을수록 길게 끄는 패드 · 현).
"""
import sys, os, glob, wave
import numpy as np

sys.stdout.reconfigure(encoding="utf-8")
SR = 48000


def load(f):
    w = wave.open(f); ch = w.getnchannels(); sr = w.getframerate()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float32) / 32768
    return x.reshape(-1, ch).mean(1), sr


def medfilt(a, k, axis):
    """축 방향 이동 중앙값 (k 홀수)."""
    pad = [(0, 0)] * a.ndim; pad[axis] = (k // 2, k // 2)
    p = np.pad(a, pad, mode="edge")
    v = np.lib.stride_tricks.sliding_window_view(p, k, axis=axis)
    return np.median(v, axis=-1)


def analyze(path):
    x, sr = load(path)
    x = x[:min(len(x), 120 * sr)]  # 앞 2분
    win, hop = 2048, 1024
    n = 1 + (len(x) - win) // hop
    idx = np.arange(win)[None, :] + hop * np.arange(n)[:, None]
    S = np.abs(np.fft.rfft(x[idx] * np.hanning(win), axis=1)).astype(np.float32)
    fq = np.fft.rfftfreq(win, 1 / sr)
    band = (fq >= 300) & (fq <= 8000)
    B = S[:, band]
    H = np.zeros_like(B); P = np.zeros_like(B)
    for a in range(0, len(B), 1200):  # 조각마다 (메모리)
        blk = B[max(0, a - 16):a + 1216]
        h = medfilt(blk, 17, 0); p = medfilt(blk, 17, 1)
        o = a - max(0, a - 16)
        H[a:a + 1200] = h[o:o + 1200]; P[a:a + 1200] = p[o:o + 1200]
    mh = H ** 2 / (H ** 2 + P ** 2 + 1e-12); mp = 1 - mh
    E = B ** 2
    perc = float((E * mp).sum() / E.sum())
    hi = fq[band] > 2000
    hiperc = float((E[:, hi] * mp[:, hi]).sum() / E.sum())
    L = np.log1p(100 * B)
    flux = np.maximum(0, np.diff(L, axis=0)).sum(1)
    thr = 3 * np.median(flux)
    pk = (flux[1:-1] > thr) & (flux[1:-1] >= flux[:-2]) & (flux[1:-1] >= flux[2:])
    rate = pk.sum() / (len(x) / sr)
    Hh = H ** 2
    sustain = float(np.mean(np.sum(np.minimum(Hh[1:], Hh[:-1]), 1) / (np.sum(Hh[:-1], 1) + 1e-12)))
    Pall = (S ** 2).mean(0)
    cent = float((Pall * fq).sum() / Pall.sum()); low = float(Pall[fq < 300].sum() / Pall.sum())
    return perc, rate, hiperc, cent, low, sustain


paths = []
for a in sys.argv[1:]:
    paths += sorted(glob.glob(os.path.join(a, "*.wav"))) if os.path.isdir(a) else [a]
print(f"{'곡':28} 타악   어택/초  고음어택  무게중심  <300Hz  지속")
for p in paths:
    perc, rate, hiperc, cent, low, sus = analyze(p)
    print(f"{os.path.basename(p)[:-4]:28} {perc*100:4.0f}%  {rate:6.2f}   {hiperc*100:5.1f}%   {cent:5.0f} Hz  {low*100:4.0f}%  {sus:.2f}")
