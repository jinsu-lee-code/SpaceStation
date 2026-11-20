"""
사운드 검수 표 (작업 지침 7): 폴더 안 WAV 전부의 crest(50 ms 대비 순간 최고 — 14 dB 넘으면 치고 나옴 의심) · attack(ms) · crackle(고음 잡음 터짐) · tonal(좁은 울림 봉우리 — 4.5 넘으면 통통 · 웅 의심) · ring(0.8 ~ 12 kHz 좁은 울림이 이어진 구간 수 — 5 넘으면 "찰랑" 쇳소리 의심, 2026-11-20 문 닫힘) · mid1-6k(발소리 닿는 소리)를 출력.
실행: PYTHONIOENCODING=utf-8 "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/tools/audit.py <Audio 폴더>
"""
import sys, wave, glob, os, numpy as np
rows = []
for f in sorted(glob.glob(sys.argv[1] + "/**/*.wav", recursive=True)):
    w = wave.open(f); sr = w.getframerate(); ch = w.getnchannels()
    x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64).reshape(-1, ch).mean(1) / 32768
    if len(x) < sr * 0.05: continue
    pk = np.abs(x).max() + 1e-9
    n = int(sr * 0.05); st = np.sqrt(np.convolve(x ** 2, np.ones(n) / n, mode="valid"))
    crest = 20 * np.log10(pk / (st.max() + 1e-9))   # 50 ms 소리 크기 대비 순간 최고 = 치고 나옴
    # 가장 큰 곳까지 오르는 시간 (10% → 90%)
    e = np.sqrt(np.convolve(x ** 2, np.ones(int(sr * 0.002)) / int(sr * 0.002), mode="same"))
    ip = int(np.argmax(e)); lo = np.where(e[:ip] < e[ip] * 0.1)[0]; a0 = lo[-1] if len(lo) else 0
    hi = np.where(e[a0:ip + 1] >= e[ip] * 0.9)[0]; attack = (hi[0] if len(hi) else 0) / sr * 1000
    hp = x - np.convolve(x, np.ones(16) / 16, mode="same")
    e1 = np.sqrt(np.convolve(hp ** 2, np.ones(48) / 48, mode="valid"))
    crackle = 20 * np.log10(np.percentile(e1, 99.9) / (np.median(e1) + 1e-12) + 1e-9)
    win = 4096; seg = x[: min(len(x), sr * 4)]
    fr = np.array([seg[i:i + win] * np.hanning(win) for i in range(0, len(seg) - win, 2048)]) if len(seg) > win else np.array([np.pad(seg, (0, win - len(seg))) * np.hanning(win)])
    S = np.abs(np.fft.rfft(fr, axis=1)).mean(0); fq = np.fft.rfftfreq(win, 1 / sr)
    sm = np.convolve(S, np.ones(31) / 31, mode="same") + 1e-12
    tonal = np.percentile(S / sm, 99.8)
    P = S ** 2; mid = P[(fq > 1000) & (fq < 6000)].sum() / P.sum()
    # 찰랑: 높은 음역 좁은 울림이 여러 구간 이어짐
    fr2 = np.array([x[i:i + 4096] * np.hanning(4096) for i in range(0, max(1, min(len(x), sr * 4) - 4096), 1024)])
    ring = 0
    if len(fr2):
        S2 = np.abs(np.fft.rfft(fr2, axis=1)); f2 = np.fft.rfftfreq(4096, 1 / sr); hb = (f2 > 800) & (f2 < 12000)
        r2 = S2[:, hb] / (np.apply_along_axis(lambda r: np.convolve(r, np.ones(41) / 41, mode='same'), 1, S2[:, hb]) + 1e-9)
        e2 = 20 * np.log10(np.sqrt((fr2 ** 2).mean(1)) + 1e-9)
        ring = int(((r2.max(1) > 6) & (e2 > e2.max() - 30)).sum())
    flags = []
    loop = f.endswith("_loop.wav")
    if not loop and crest > 14: flags.append("치고나옴")
    if not loop and attack < 2 and crest > 10: flags.append("날카로운시작")
    if crackle > 22 and loop: flags.append("지직")
    if tonal > 4.5: flags.append("울림봉우리")
    if "step" in f and mid < 0.05: flags.append("닿는소리없음")
    if not loop and ring > 5: flags.append("찰랑")
    rows.append((os.path.relpath(f, sys.argv[1]), crest, attack, crackle, tonal, mid, ring, " ".join(flags)))
print(f"{'file':42s} crest attack crackle tonal mid1-6k ring  flags")
for r in rows:
    print(f"{r[0]:42s} {r[1]:5.1f} {r[2]:6.1f} {r[3]:7.1f} {r[4]:5.1f} {r[5]:6.2f} {r[6]:4d}  {r[7]}")
