"""
음악 다듬기 (사운드 2차 개편 — 내부 음악 등, 2026-11-23). 사운드 작업 지침 = docs/sound/00-workflow.md.
VARCO 음악(mp3)을 Blender의 aud로 풀고, numpy로 마디에 딱 맞는 루프를 만든 뒤 Ogg Vorbis로 저장한다 (이 PC엔 ffmpeg 없음).
실행: "<Blender>/blender.exe" -b --factory-startup --python AudioOriginals/build_music.py
원본 = AudioOriginals/Music/*.mp3 (git), 결과 = Assets/_Project/Audio/Music/*.ogg. 몇 번 돌려도 같은 결과.

루프 만들기:
  1. 박 길이를 긴 간격 자기상관(16 · 32 · 64 · 96박)으로 정밀하게 잼 (요청 90 BPM이어도 실제는 조금 다름)
  2. 시작 = 고른 마디 머리 (music_check.py의 loop 시작), 길이 = 마디 수 × 4박
  3. 끝 자리를 ±20 ms 안에서 파형 상관으로 맞춤 (상관 0.5 넘을 때만) → 끝을 시작에 겹쳐 이음 (기본 0.15초 — 같은 박 머리끼리 겹치므로 이음새가 안 들림, 길게 끄는 패드 곡은 0.6초)
  4. 크기 = RMS −18 dBFS (내부 4곡이 같은 크기여야 방을 옮길 때 크기가 안 튐), 최고 −1 dB 넘지 않게
"""
import os
import numpy as np
import aud

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "Music")
DST = os.path.join(ROOT, "..", "SpaceStation", "Assets", "_Project", "Audio", "Music")
SR = 48000


def load(name):
    s = aud.Sound(os.path.join(SRC, name))
    if int(s.specs[0]) != SR:
        s = s.resample(SR, False)
    x = np.asarray(s.data(), dtype=np.float64)
    return x if x.ndim == 2 else np.stack([x, x], 1)


def flux_of(mono, hop=256, win=2048):
    n = 1 + (len(mono) - win) // hop
    idx = np.arange(win)[None, :] + hop * np.arange(n)[:, None]
    S = np.log1p(100 * np.abs(np.fft.rfft(mono[idx] * np.hanning(win), axis=1))[:, :400])
    f = np.maximum(0, np.diff(S, axis=0)).sum(1)
    f = np.maximum(0, f - np.convolve(f, np.ones(32) / 32, "same"))
    return f, SR / hop


def exact_beat(mono, bpm):
    """박 길이(초). 긴 간격 자기상관 봉우리를 포물선으로 다듬어 평균."""
    f, fps = flux_of(mono)
    F = np.fft.rfft(f, 2 * len(f))
    ac = np.fft.irfft(F * np.conj(F))[:len(f)]
    beat0 = fps * 60.0 / bpm; est = []
    for k in (16, 32, 64, 96):
        c = k * beat0; lo, hi = int(c * 0.985), int(c * 1.015) + 1
        if hi + 1 >= len(ac):
            continue
        i = lo + int(np.argmax(ac[lo:hi]))
        a, b, d = ac[i - 1], ac[i], ac[i + 1]
        off = 0.5 * (a - d) / (a - 2 * b + d) if (a - 2 * b + d) != 0 else 0.0
        est.append((i + off) / k / fps)
    return float(np.median(est))


def make_loop(x, start, bars, beat, xf=0.15):
    mono = x.mean(1)
    # 시작 = 고른 자리 ±50 ms 안의 가장 센 박 머리 (onset) 10 ms 앞
    f, fps = flux_of(mono)
    c = int(start * fps); w = int(0.05 * fps)
    s = int(round(((c - w + int(np.argmax(f[c - w:c + w + 1]))) / fps - 0.01) * SR))
    L = int(round(bars * 4 * beat * SR))
    # 끝 자리 맞춤: 시작 0.5초와 가장 닮은 곳 (±20 ms)
    ref = mono[s:s + SR // 2]; best = (-2.0, 0)
    for o in range(-int(0.02 * SR), int(0.02 * SR) + 1, 8):
        seg = mono[s + L + o:s + L + o + len(ref)]
        c = float(np.dot(ref, seg) / (np.linalg.norm(ref) * np.linalg.norm(seg) + 1e-12))
        best = max(best, (c, o))
    # 내용이 닮았을 때만 옮김 (32마디 뒤가 1마디와 다른 곡이면 상관이 낮아 엉뚱한 곳을 고를 수 있음 → 박 길이 그대로)
    if best[0] < 0.5:
        best = (best[0], 0)
    L += best[1]
    out = x[s:s + L].copy()
    XF = int(xf * SR)
    t = np.linspace(0, np.pi / 2, XF)[:, None]
    out[:XF] = x[s:s + XF] * np.sin(t) + x[s + L:s + L + XF] * np.cos(t)
    return out, L, best


def level(x, rms_db=-18.0, peak_db=-1.0):
    g = 10 ** (rms_db / 20) / np.sqrt((x ** 2).mean())
    g = min(g, 10 ** (peak_db / 20) / np.abs(x).max())
    return x * g


def save(name, x):
    os.makedirs(DST, exist_ok=True)
    p = os.path.join(DST, name)
    aud.Sound.buffer(x.astype(np.float32), SR).write(p, rate=SR, channels=aud.CHANNELS_STEREO, format=aud.FORMAT_S16,
                                                       container=aud.CONTAINER_OGG, codec=aud.CODEC_VORBIS, bitrate=192000)
    return p


def fit_length(x, n):
    """루프를 n 샘플로 (FFT — 루프는 한 바퀴가 이어지는 신호라 이음새가 안 깨짐). 음높이는 길이 비율만큼 바뀜 (0.5 % = 8.6센트)."""
    X = np.fft.rfft(x, axis=0); Y = np.zeros((n // 2 + 1, x.shape[1]), dtype=complex)
    k = min(len(X), len(Y)); Y[:k] = X[:k]
    return np.fft.irfft(Y, n, axis=0) * (n / len(x))


def grid_of(out, bars):
    """루프의 박 위치(샘플, 첫 박 머리)와 마디 머리 박(0 ~ 3). 박 = 길이 / (마디 × 4) — 루프가 정확히 마디 단위라서.
    박 위치 = onset을 1 ms 단위 박 격자에 맞춰 가장 센 자리, 마디 머리 = 저음 onset + 화음 바뀜(박마다 음 봉우리 분포 차)이 가장 큰 박."""
    m = out.mean(1); n = len(m); beat = n / (bars * 4.0); hop = 128
    mm = np.concatenate([m, m[:4096]])
    idx = np.arange(2048)[None, :] + hop * np.arange(1 + (len(mm) - 2048) // hop)[:, None]
    S = np.abs(np.fft.rfft(mm[idx] * np.hanning(2048), axis=1))
    L = np.log1p(100 * S)
    f = np.maximum(0, np.diff(L[:, :400], axis=0)).sum(1); f = np.maximum(0, f - np.convolve(f, np.ones(64) / 64, "same"))
    fl = np.maximum(0, np.diff(L[:, :13], axis=0)).sum(1)
    best = max((f[((ph + beat * np.arange(bars * 4)) / hop).astype(int)].sum(), ph) for ph in np.arange(0, beat, 48))
    ph = best[1]
    fq = np.fft.rfftfreq(2048, 1 / SR); sel = (fq > 110) & (fq < 5000)
    pc = np.round(12 * np.log2(fq[sel] / 440.0) + 9).astype(int) % 12
    chroma = []
    for b in range(bars * 4):
        a, z = int((ph + b * beat) / hop), int((ph + (b + 1) * beat) / hop)
        Lg = np.log(S[a:z, sel] + 1e-9); base = np.array([np.convolve(r, np.ones(31) / 31, "same") for r in Lg])
        c = np.bincount(pc, weights=np.maximum(0, Lg - base - 1.0).sum(0), minlength=12); chroma.append(c / (np.linalg.norm(c) + 1e-9))
    nov = [1 - float(np.dot(chroma[b], chroma[b - 1])) for b in range(bars * 4)]
    low = [fl[min(len(fl) - 1, int((ph + b * beat) / hop))] for b in range(bars * 4)]
    score = []
    for k in range(4):
        sl = np.mean(low[k::4]) / (np.mean(low) + 1e-9); sn = np.mean(nov[k::4]) / (np.mean(nov) + 1e-9)
        score.append(sl + sn)
    return ph, int(np.argmax(score)), beat, score


def align(out, ref, bars):
    """루프 시작을 돌려 박 · 마디 머리를 기준 곡과 같은 자리로 (한 바퀴로 이어진 신호라 이음새는 그대로)."""
    rph, rbar, beat, _ = grid_of(ref, bars)
    ph, bar, _, score = grid_of(out, bars)
    shift = int(round((ph + bar * beat) - (rph + rbar * beat)))
    print(f"  박 맞춤: 박 위치 {ph / SR * 1000:.0f} ms · 마디 머리 박 {bar} (점수 {np.round(score, 2).tolist()}) → 기준 {rph / SR * 1000:.0f} ms · 박 {rbar} 에 맞춰 {shift / SR * 1000:+.0f} ms 돌림")
    return np.roll(out, -shift, axis=0)


def build(dst, src, start, bars, bpm, ref_len=None, ref=None, xf=0.15):
    x = load(src)
    beat = exact_beat(x.mean(1), bpm)
    # 어택이 드문 차분한 곡은 박 재기가 흔들림 → 요청 빠르기 그대로 (VARCO "90 BPM" 곡은 실제 89.997 ~ 89.999)
    if abs(60 / beat - bpm) > 0.05:
        print(f"  {dst}: 잰 빠르기 {60 / beat:.3f}가 요청 {bpm}과 달라 요청 빠르기로")
        beat = 60.0 / bpm
    out, L, corr = make_loop(x, start, bars, beat, xf)
    if ref_len is not None and len(out) != ref_len:
        ratio = ref_len / len(out)
        assert abs(ratio - 1) < 0.005, f"{dst}: 기준 길이와 {abs(ratio - 1) * 100:.2f}% 차이 (0.5% 넘음 — 빠르기가 맞는 후보로)"
        out = fit_length(out, ref_len)
        print(f"  {dst}: 기준 길이에 맞춤 {L} → {ref_len} 샘플 ({1200 * np.log2(1 / ratio):+.1f}센트)")
    if ref is not None:
        out = align(out, ref, bars)
    out = level(out)
    p = save(dst, out)
    # 확인: 두 번 이어 붙였을 때 이음새 앞뒤 50 ms 크기 차
    y = np.concatenate([out, out]).mean(1); j = len(out)
    a = 20 * np.log10(np.sqrt((y[j - 2400:j] ** 2).mean()) + 1e-9); b = 20 * np.log10(np.sqrt((y[j:j + 2400] ** 2).mean()) + 1e-9)
    print(f"{dst}: BPM {60 / beat:.3f} · {bars}마디 = {L / SR:.3f}s (박 기준 {bars * 4 * beat:.3f}s) · 끝 맞춤 상관 {corr[0]:.3f} (옮김 {corr[1] / SR * 1000:+.1f} ms) · 이음새 크기 차 {b - a:+.1f} dB · {os.path.getsize(p) // 1024} KB")
    return out


# ---------------- 내부 음악 (단계별 4곡 — SOUND_LIST_NEW "내부 음악 이어 붙이기") ----------------
# 2026-11-23 둘째 판: 첫 판(마림바 · 신스 아르페지오 · 토이 피아노 · 오르골, 32마디)은 사용자 "바깥의 침착하고 차분한 음악에 비해
# 너무 톡톡 튄다" → tools/calm_check.py로 바깥 곡과 나란히 재서 (바깥 = 어택 3.2 ~ 4.3/초 · 무게중심 160 ~ 224 Hz) 차분한 곡으로 바꿈.
# 차분한 후보 46곡 (뜯는 악기 · 종 · 딸깍 빼고 패드 · 낮은 베이스 · 느린 화음) 중 고름. 조건 = 어택 · 무게중심이 바깥 곡 가까이,
# 끊김 없음, F# · E♭이 약함 (네 곡이 A · C · D · E · F · G 중심이라 겹쳐도 안 부딪힘), 빠르기 90 또는 60.
# 길이 = 96초 (90 BPM 36마디 = 60 BPM 24마디 — 두 빠르기 모두 마디에 딱 맞음). 패드 곡이라 이음새는 0.6초 겹침.
# 기본 (생존 공업 → 조금 묵직하게) — music_calm_0 b1 "엔진실의 밤 드론":
#   요청 = 낮은 패드 + 천천히 부푸는 서브 베이스 + 먹먹한 낮은 박동 ("warm quiet ambient drone for a space station engine room at night")
#   고른 이유 = 무게중심 216 Hz · 300 Hz 아래 80 % (바깥 낮 곡 224 Hz · 75 %와 가장 닮음) · 끊김 0 · 빠르기 90.0
#   뺀 것: c1 · d2 · f · e = 끊김 4 ~ 7마디 · a · b2 · c2 · f1 = A 장조 (F# 셈) · d1 · e2 · f2 = 빠르기 91 ~ 92.5
REF = build("music_interior_0.ogg", "interior_0_calm.mp3", 39.84, 36, 90.0, xf=0.6)
# 1단계 (미래 기술 — 매끈한 전자) — music_calm_1 a1 "매끈한 미래 패드":
#   요청 = 유리 같은 신스 패드가 천천히 바뀜 + 따뜻한 서브 베이스 + 멀리 아주 옅은 박동
#   고른 이유 = 어택 1.37/초 · 고음 어택 1.0 % (가장 차분) · 무게중심 299 Hz · 끊김 1 (루프 밖) · 60 BPM이라 24마디 = 96초
#   뺀 것: b1 · c1 = 어택 4.9/초 · 끊김 4 ~ 5 · b2 · d · e = 빠르기 92 ~ 115 · a2 = A 장조 · 고음 어택 6.4 %
build("music_interior_1.ogg", "interior_1_calm.mp3", 14.71, 24, 60.0, len(REF), xf=0.6)
# 2단계 (테마파크 → 여가의 저녁) — music_calm_2 d1 "궤도 공원의 저녁":
#   요청 = 따뜻한 현악 패드 + 부드러운 오르간 같은 긴 화음 + 낮은 베이스 ("relaxed calm evening ambient in a cozy orbital park")
#   고른 이유 = F 장조 0.91 · 어택 0.34/초 · 고음 어택 0.1 % · 끊김 0 (180으로 잼 = 90의 두 배)
#   뺀 것: b2 · e1 = 어택 4.1 ~ 5.3/초 · a · b1 = F# 셈 (A 장조 · E 장조) · c · d2 · e2 = E♭ 셈 (C 단조 · F 단조)
build("music_interior_2.ogg", "interior_2_calm.mp3", 74.31, 36, 90.0, len(REF), REF, xf=0.6)
# 3단계 이상 (동화) — music_calm_3 b2 "유리 정원":
#   요청 = 따뜻한 현악 합주 + 플루트 같은 패드 + 길게 끄는 낮은 베이스 ("serene enchanted glass garden ambient in space")
#   고른 이유 = 어택 0.57/초 · 고음 어택 0.2 % · 고른 구간 51마디 · 끊김 1 (루프 밖) · F# · E♭ 거의 없음
#   뺀 것: b1 = F 장조 0.93이지만 300 Hz 아래 42 % (가벼움) · a · d · e · c = 빠르기 60 ~ 135 (90 · 60 아님) 또는 E♭ 셈
build("music_interior_3.ogg", "interior_3_calm.mp3", 56.11, 36, 90.0, len(REF), REF, xf=0.6)
