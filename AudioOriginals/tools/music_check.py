"""
음악 후보 판정 도구 (사운드 작업 지침 3번의 음악판 — 내부 4곡 이어 붙이기용, 2026-11-23).
mp3는 먼저 decode.py로 wav로 푼다. 폴더 안 wav마다 수치를 출력하고 전체 스펙트로그램 판(sheet_music.png)을 만든다.
실행: "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/tools/music_check.py <wav 폴더> <그림 폴더> [마디 수 = 32] [고정 BPM]
수치:
  bpm = 박 빠르기 후보 3개 (onset 자기상관) · key = 조 후보 3개 (Krumhansl 프로필 상관, 높을수록 확실) · keyseg = 20초 조각마다 조 (흔들리면 전조)
  rms = 전체 크기 (dBFS) · steady = 3초 창 크기의 표준편차 (dB, 작을수록 고름) · intro/outro = 처음 · 마지막 8초가 중앙값보다 몇 dB
  cent · low<300 · high>4k = 무게중심 · 대역 비율 · hfjump = 6 kHz 위가 갑자기 10 dB 넘게 튀는 순간 수 (날 선 타악 · 지직)
  bars = 마디마다 크기 (5 = 중앙값, 1칸 = 2 dB, "." = 끊김 — 중앙값보다 8 dB 넘게 작음) · 가장 긴 고른 구간 = 중앙값 ±3 dB 마디가 이어진 수
  선율 몫 = 음 봉우리 중 700 Hz 위 비율 (높을수록 앞에 나서는 선율 · 리드가 많음 → 배경 곡은 낮은 쪽)
  loop = 고른 마디 수로 자를 때 가장 잘 이어지는 시작 (초) · 이음새 거리 (작을수록 매끈, 끊김 마디가 들면 벌점)
판 읽기: 가로 = 곡 전체, 세로 = 50 Hz ~ 16 kHz, 흰 점선 = 크기, 초록 세로선 = 4마디마다 (BPM 1순위 기준), 노란 선 = 루프 구간.
"""
import sys, wave, glob, os, zlib, struct
import numpy as np
sys.stdout.reconfigure(encoding="utf-8")

src, out = sys.argv[1], sys.argv[2]
BARS = int(sys.argv[3]) if len(sys.argv) > 3 else 32
FIX_BPM = float(sys.argv[4]) if len(sys.argv) > 4 else None
SR = 48000
NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
MAJ = np.array([6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88])
MIN = np.array([6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17])


def load(f):
    w = wave.open(f); ch = w.getnchannels()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).astype(np.float64) / 32768
    return x.reshape(-1, ch).mean(1)


def stft(x, win, hop):
    n = 1 + (len(x) - win) // hop
    idx = np.arange(win)[None, :] + hop * np.arange(n)[:, None]
    return np.abs(np.fft.rfft(x[idx] * np.hanning(win), axis=1))


def key_of(S, fq):
    # 음 봉우리만 셈: 주파수 방향 이동 평균(±15칸 ≈ ±88 Hz)보다 9 dB 넘게 솟은 칸만 (기계음 · 타악 · 잡음은 평평해서 빠짐)
    m = (fq > 110) & (fq < 5000)
    pc = np.round(12 * np.log2(fq[m] / 440.0) + 9).astype(int) % 12
    L = np.log(S[:, m] + 1e-9)
    k = np.ones(31) / 31
    base = np.array([np.convolve(r, k, "same") for r in L])
    e = np.maximum(0, L - base - 1.0).sum(0)
    chroma = np.bincount(pc, weights=e, minlength=12)
    res = []
    for r in range(12):
        res.append((np.corrcoef(np.roll(MAJ, r), chroma)[0, 1], NAMES[r] + " major"))
        res.append((np.corrcoef(np.roll(MIN, r), chroma)[0, 1], NAMES[r] + " minor"))
    return sorted(res, reverse=True), chroma


def png(path, img):
    h, w, _ = img.shape
    raw = b"".join(b"\x00" + img[y].astype(np.uint8).tobytes() for y in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    open(path, "wb").write(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b""))


def sheet_row(x, bpm, loop, W=1400, H=120):
    T = len(x) / SR; hop = len(x) // W; win = 4096
    fr = []
    for i in range(W):
        seg = x[i * hop:i * hop + win]; seg = np.pad(seg, (0, win - len(seg))) * np.hanning(win)
        fr.append(np.abs(np.fft.rfft(seg)))
    S = np.array(fr).T; freqs = np.fft.rfftfreq(win, 1 / SR)
    edges = np.geomspace(50, 16000, H + 1); M = np.zeros((H, W))
    for k in range(H):
        m = (freqs >= edges[k]) & (freqs < edges[k + 1])
        if not m.any(): m = np.argmin(abs(freqs - edges[k])) == np.arange(len(freqs))
        M[H - 1 - k] = S[m].mean(0)
    db = 20 * np.log10(M + 1e-9); db = np.clip((db - (db.max() - 70)) / 70, 0, 1)
    img = np.stack([db ** 0.8 * 255, db ** 1.5 * 200, (1 - db) * db * 4 * 120], -1)
    env = np.array([np.sqrt((x[i * hop:i * hop + hop] ** 2).mean() + 1e-12) for i in range(W)])
    e = np.clip((20 * np.log10(env) + 50) / 50, 0, 1)
    for i in range(W):
        img[int((1 - e[i]) * (H - 1)), i] = [255, 255, 255]
    if bpm:
        bar = 240.0 / bpm; t = loop[0] if loop else 0.0
        while t - 4 * bar > 0: t -= 4 * bar
        while t < T:
            c = int(t / T * W)
            if 0 <= c < W: img[::2, c] = [0, 160, 70]
            t += 4 * bar
    if loop:
        for t in loop:
            c = min(W - 1, int(t / T * W)); img[:, c] = [255, 220, 0]
    return img


def bpm_of(x):
    win, hop = 2048, 512; fps = SR / hop
    S = stft(x, win, hop); L = np.log1p(100 * S[:, :400])
    flux = np.maximum(0, np.diff(L, axis=0)).sum(1)
    flux = flux - np.convolve(flux, np.ones(16) / 16, "same")
    flux = np.maximum(flux, 0)
    ac = np.correlate(flux, flux, "full")[len(flux) - 1:]
    cands = []
    for b in np.arange(60, 181, 0.5):
        lag = fps * 60 / b
        v = sum(np.interp(lag * k, np.arange(len(ac)), ac) for k in (1, 2, 4)) / 3
        cands.append((v, b))
    cands.sort(reverse=True)
    picked = []
    for v, b in cands:
        if all(abs(b - p) > 3 for _, p in picked): picked.append((v, b))
        if len(picked) == 3: break
    top = picked[0][0]
    return [(b, v / top) for v, b in picked], flux, fps


def downbeat(flux, fps, bpm, xlow):
    beat = fps * 60 / bpm; n = len(flux)
    best = (-1, 0)
    for ph in np.arange(0, beat, 0.5):
        idx = (ph + beat * np.arange(int((n - ph) / beat))).astype(int)
        best = max(best, (flux[idx].sum(), ph))
    ph = best[1]
    # 마디 머리 = 네 박 중 저음 onset이 가장 센 박
    scores = []
    for k in range(4):
        idx = (ph + beat * (k + 4 * np.arange(int((n - ph) / (4 * beat)) - 1))).astype(int)
        scores.append(xlow[np.clip(idx, 0, len(xlow) - 1)].sum())
    return (ph + beat * int(np.argmax(scores))) / fps


def bar_levels(x, bpm, t0):
    """마디마다 크기 (dB). 첫 마디 머리 앞은 버림."""
    bar = 240.0 / bpm; t = t0; out = []
    while t + bar <= len(x) / SR:
        seg = x[int(t * SR):int((t + bar) * SR)]; out.append(20 * np.log10(np.sqrt((seg ** 2).mean()) + 1e-9)); t += bar
    return np.array(out)


def loop_point(x, bpm, t0, bars, gapbar=None):
    bar = 240.0 / bpm; L = bars * bar; T = len(x) / SR
    win = 8192; best = None
    t = t0
    while t + L + 0.5 <= T:
        a = x[int(t * SR):int(t * SR) + win]; b = x[int((t + L) * SR):int((t + L) * SR) + win]
        pa = np.log1p(np.abs(np.fft.rfft(a * np.hanning(win)))[:1500]); pb = np.log1p(np.abs(np.fft.rfft(b * np.hanning(win)))[:1500])
        # 끝 직전 · 시작 직전 크기도 비슷해야 (이음새 크기 튐)
        ea = np.sqrt((x[int((t - 0.2) * SR):int(t * SR)] ** 2).mean() + 1e-12) if t > 0.2 else 1e-6
        eb = np.sqrt((x[int((t + L - 0.2) * SR):int((t + L) * SR)] ** 2).mean() + 1e-12)
        d = np.abs(pa - pb).mean() + 0.3 * abs(20 * np.log10(ea / eb))
        if gapbar is not None:  # 구간 안에 끊김 마디가 있으면 하나당 벌점
            i0 = int(round((t - t0) / bar)); d += 0.5 * gapbar[i0:i0 + bars].sum()
        if best is None or d < best[0]: best = (d, t)
        t += bar
    return best


os.makedirs(out, exist_ok=True)
rows = []
for f in sorted(glob.glob(os.path.join(src, "*.wav"))):
    x = load(f); T = len(x) / SR; name = os.path.basename(f)[:-4]
    bpms, flux, fps = bpm_of(x)
    bpm = FIX_BPM or bpms[0][0]
    S = stft(x, 8192, 4096); fq = np.fft.rfftfreq(8192, 1 / SR)
    keys, chroma = key_of(S, fq)
    seg = []
    n20 = int(20 * SR / 4096)
    for i in range(0, len(S) - n20 // 2, n20):
        seg.append(key_of(S[i:i + n20], fq)[0][0][1].replace(" major", "").replace(" minor", "m"))
    rmsw = [20 * np.log10(np.sqrt((x[i:i + 3 * SR] ** 2).mean()) + 1e-9) for i in range(0, len(x) - 3 * SR, SR)]
    med = np.median(rmsw)
    intro = 20 * np.log10(np.sqrt((x[:8 * SR] ** 2).mean()) + 1e-9) - med
    outro = 20 * np.log10(np.sqrt((x[-8 * SR:] ** 2).mean()) + 1e-9) - med
    P = (S ** 2).mean(0)
    cent = (P * fq).sum() / P.sum(); low = P[fq < 300].sum() / P.sum(); high = P[fq > 4000].sum() / P.sum()
    hf = 10 * np.log10(S[:, fq > 6000].sum(1) ** 2 + 1e-12)
    hfjump = int((np.diff(hf) > 10).sum())
    Sl = stft(x, 2048, 512)
    xlow = np.maximum(0, np.diff(np.log1p(100 * Sl[:, :13]), axis=0)).sum(1)
    t0 = downbeat(flux, fps, bpm, xlow)
    bl = bar_levels(x, bpm, t0); bmed = np.median(bl)
    gap = bl < bmed - 8
    prof = "".join("." if g else str(int(np.clip(round((v - bmed) / 2) + 5, 0, 9))) for v, g in zip(bl, gap))
    run = best_run = 0
    for v in bl:
        run = run + 1 if abs(v - bmed) <= 3 else 0; best_run = max(best_run, run)
    # 선율 몫: 음 봉우리 중 700 Hz 위 비율 (높으면 앞에 나서는 선율 · 리드가 많음)
    mm = (fq > 110) & (fq < 5000); Lg = np.log(S[:, mm] + 1e-9)
    pk = np.maximum(0, Lg - np.array([np.convolve(r, np.ones(31) / 31, "same") for r in Lg]) - 1.0)
    lead = pk[:, fq[mm] > 700].sum() / (pk.sum() + 1e-9)
    lp = loop_point(x, bpm, t0, BARS, gap)
    loop = (lp[1], lp[1] + BARS * 240 / bpm) if lp else None
    print(f"== {name}  {T:.1f}s")
    print("  bpm  " + "  ".join(f"{b:.1f}({s:.2f})" for b, s in bpms) + (f"  [고정 {FIX_BPM}]" if FIX_BPM else ""))
    print("  key  " + "  ".join(f"{k}({c:.2f})" for c, k in keys[:3]))
    print("  keyseg " + " ".join(seg))
    cn = chroma / chroma.max()
    print("  chroma " + " ".join(f"{NAMES[i]}{cn[i]:.2f}" for i in range(12)))
    print(f"  rms {20*np.log10(np.sqrt((x**2).mean())):.1f} dB  peak {20*np.log10(np.abs(x).max()):.1f}  steady {np.std(rmsw):.1f} dB (min {min(rmsw)-med:+.1f} / max {max(rmsw)-med:+.1f})  intro {intro:+.1f}  outro {outro:+.1f}")
    print(f"  cent {cent:.0f} Hz  low<300 {low*100:.0f}%  high>4k {high*100:.1f}%  hfjump {hfjump}")
    print(f"  bars {prof}")
    print(f"  끊김 마디 {int(gap.sum())}개 · 가장 긴 고른 구간 {best_run}마디 (중앙값 ±3 dB) · 선율 몫 {lead*100:.0f}%")
    print(f"  downbeat {t0:.2f}s  loop {BARS}마디 = {BARS*240/bpm:.1f}s  " + (f"시작 {loop[0]:.2f}s ~ {loop[1]:.2f}s  이음새 {lp[0]:.3f}" if lp else "곡이 짧아 못 자름"))
    rows.append(sheet_row(x, bpm, loop)); rows.append(np.full((6, 1400, 3), 60))
if rows:
    png(os.path.join(out, "sheet_music.png"), np.concatenate(rows))
