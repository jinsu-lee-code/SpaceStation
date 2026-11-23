# 실제 녹음 비교 (2026-11-24): AI 환경 = uv venv <폴더>/env --python 3.11 -> uv pip install torch (--index-url https://download.pytorch.org/whl/cpu) panns-inference librosa soundfile,
# 모델 = ~/panns_data/ 에 class_labels_indices.csv (storage.googleapis.com/us_audioset/youtube_corpus/v1/csv/) · Cnn14_mAP=0.431.pth (zenodo.org/record/3987831) 를 curl로 (Windows엔 wget 없음).
"""
실제 녹음 대 우리 소리 — 한 번 소리 (터짐 · 부딪힘) 비교 (2026-11-23 사용자 "효과음이 전반적으로 이상 — 폭죽 소리가 대표").
실제 녹음에서 큰 소리 순간을 하나씩 찾아 잘라 (최대 앞 0.15초 ~ 뒤 3초) 같은 눈금으로 잼:
  attack   = 최대의 10 % -> 90 %까지 (ms)
  t20/t40  = 최대 뒤 50 ms 크기가 -20 · -40 dB까지 떨어지는 시간 (초, 바닥에 먼저 닿으면 '>')
  low      = 처음 0.3초 에너지 중 200 Hz 아래 비율 (%) — "쿵" 몸통
  cent     = 처음 0.1초 무게중심 (Hz)
  oct      = 처음 0.3초 옥타브 띠 (63 ~ 16k, 가장 큰 띠 = 0 dB)
  crk      = 0.3 ~ 2초의 잔 알갱이 (2 kHz 위 짧은 터짐) 초당 수
실행: <ai env python> ref_compare.py <실제 녹음 폴더> <우리 wav> [우리 wav ...]
"""
import sys, os, glob, numpy as np, soundfile as sf, librosa

SR = 48000
OCT = [63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]


def load(p):
    x, sr = sf.read(p, always_2d=True)
    x = x.mean(1)
    return librosa.resample(x, orig_sr=sr, target_sr=SR) if sr != SR else x


def env(x, win):
    n = int(win * SR)
    return np.sqrt(np.convolve(x ** 2, np.ones(n) / n, mode="same") + 1e-14)


def events(x, min_gap=0.7, rise_db=18):
    e = 20 * np.log10(env(x, 0.01))
    floor = np.percentile(e, 20)
    idx = []
    i = 0
    hop = int(0.005 * SR)
    cand = np.where(e > floor + rise_db)[0]
    last = -10 ** 9
    for c in cand[::hop // 1 if hop > 0 else 1]:
        if c - last < min_gap * SR:
            continue
        w = e[c:c + int(0.15 * SR)]
        p = c + int(np.argmax(w))
        idx.append(p); last = p
    return idx


def measure(x, p):
    a = max(0, p - int(0.15 * SR)); seg = x[a:p + int(3.0 * SR)]; p0 = p - a
    e = env(seg, 0.002); pk = e[p0 - int(0.01 * SR):p0 + int(0.03 * SR)].max(); ip = int(np.argmax(e[:p0 + int(0.03 * SR)]))
    lo = np.where(e[:ip] < pk * 0.1)[0]; s10 = lo[-1] if len(lo) else 0
    hi = np.where(e[s10:ip + 1] >= pk * 0.9)[0]
    attack = (hi[0] if len(hi) else 0) / SR * 1000
    e50 = 20 * np.log10(env(seg, 0.05)); m = e50[ip:ip + int(0.1 * SR)].max(); after = e50[ip:]
    floor = np.percentile(e50, 5)

    def drop(db):
        k = np.where(after < m - db)[0]
        if len(k):
            return f"{k[0] / SR:5.2f}"
        return f">{len(after) / SR:4.1f}" if floor > m - db else f">{len(after) / SR:4.1f}"
    head = seg[ip:ip + int(0.3 * SR)]
    P = np.abs(np.fft.rfft(head * np.hanning(len(head)))) ** 2; f = np.fft.rfftfreq(len(head), 1 / SR)
    low = 100 * P[f < 200].sum() / (P.sum() + 1e-20)
    h1 = seg[ip:ip + int(0.1 * SR)]; P1 = np.abs(np.fft.rfft(h1)) ** 2; f1 = np.fft.rfftfreq(len(h1), 1 / SR)
    cent = (P1 * f1).sum() / (P1.sum() + 1e-20)
    ob = [10 * np.log10(P[(f >= c / np.sqrt(2)) & (f < c * np.sqrt(2))].sum() + 1e-20) for c in OCT]
    ob = np.array(ob) - max(ob)
    tail = seg[ip + int(0.3 * SR):ip + int(2.0 * SR)]
    crk = 0.0
    if len(tail) > SR * 0.5:
        hp = tail - np.convolve(tail, np.ones(12) / 12, mode="same")
        eh = env(hp, 0.001); thr = np.median(eh) * 4
        pk_ = np.where((eh[1:-1] > thr) & (eh[1:-1] >= eh[:-2]) & (eh[1:-1] >= eh[2:]))[0]
        if len(pk_):
            pk_ = pk_[np.insert(np.diff(pk_) > int(0.004 * SR), 0, True)]
        crk = len(pk_) / (len(tail) / SR)
    return dict(attack=attack, t20=drop(20), t40=drop(40), low=low, cent=cent, oct=ob, crk=crk)


def row(name, r):
    return f"{name[:34]:34s} {r['attack']:6.1f} {r['t20']:>6s} {r['t40']:>6s} {r['low']:5.1f} {r['cent']:6.0f} {r['crk']:5.0f}  " + " ".join(f"{v:5.0f}" for v in r["oct"])


print(f"{'sound':34s} {'att ms':>6s} {'t-20':>6s} {'t-40':>6s} {'low%':>5s} {'cent':>6s} {'crk/s':>5s}  " + " ".join(f"{(str(c) if c < 1000 else str(c // 1000) + 'k'):>5s}" for c in OCT))
rows = []
for p in sorted(glob.glob(os.path.join(sys.argv[1], "*.wav")) + glob.glob(os.path.join(sys.argv[1], "*.flac"))):
    x = load(p)
    ev = events(x)
    for k, i in enumerate(ev[:12]):
        if i + int(0.5 * SR) > len(x):
            continue
        r = measure(x, i); rows.append(r)
        print(row(f"{os.path.basename(p)[:26]}#{k}", r))
print("-- 실제 녹음 중앙값 (%d개)" % len(rows))
med = dict(attack=np.median([r["attack"] for r in rows]), low=np.median([r["low"] for r in rows]), cent=np.median([r["cent"] for r in rows]),
           crk=np.median([r["crk"] for r in rows]), oct=np.median([r["oct"] for r in rows], axis=0),
           t20=f"{np.median([float(r['t20'].strip('>')) for r in rows]):5.2f}", t40=f"{np.median([float(r['t40'].strip('>')) for r in rows]):5.2f}")
print(row("REAL median", med))
print("-- 우리 소리")
for p in sys.argv[2:]:
    x = load(p); x = np.concatenate([np.zeros(int(0.2 * SR)), x, np.zeros(int(3.2 * SR))])
    i = int(np.argmax(env(x, 0.01)))
    print(row(os.path.basename(p), measure(x, i)))
