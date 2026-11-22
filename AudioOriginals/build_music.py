"""
음악 다듬기 (사운드 2차 개편 — 내부 음악 등, 2026-11-23). 사운드 작업 지침 = docs/sound/00-workflow.md.
VARCO 음악(mp3)을 Blender의 aud로 풀고, numpy로 마디에 딱 맞는 루프를 만든 뒤 Ogg Vorbis로 저장한다 (이 PC엔 ffmpeg 없음).
실행: "<Blender>/blender.exe" -b --factory-startup --python AudioOriginals/build_music.py
원본 = AudioOriginals/Music/*.mp3 (git), 결과 = Assets/_Project/Audio/Music/*.ogg. 몇 번 돌려도 같은 결과.

루프 만들기:
  1. 박 길이를 긴 간격 자기상관(16 · 32 · 64 · 96박)으로 정밀하게 잼 (요청 90 BPM이어도 실제는 조금 다름)
  2. 시작 = 고른 마디 머리 (music_check.py의 loop 시작), 길이 = 마디 수 × 4박
  3. 끝 자리를 ±20 ms 안에서 파형 상관으로 맞춤 (상관 0.5 넘을 때만) → 끝을 시작에 0.15초 겹쳐 이음 (같은 박 머리끼리 겹치므로 이음새가 안 들림)
  4. 크기 = RMS −18 dBFS (내부 4곡이 같은 크기여야 방을 옮길 때 크기가 안 튐), 최고 −1 dB 넘지 않게
"""
import os
import numpy as np
import aud

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "Music")
DST = os.path.join(ROOT, "..", "SpaceStation", "Assets", "_Project", "Audio", "Music")
SR = 48000
XF = int(0.15 * SR)


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


def make_loop(x, start, bars, beat):
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


def build(dst, src, start, bars, bpm, ref_len=None, ref=None):
    x = load(src)
    beat = exact_beat(x.mean(1), bpm)
    out, L, corr = make_loop(x, start, bars, beat)
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


# ---------------- 내부 음악 (단계별 4곡, 같은 조 · 빠르기 · 마디 수 — SOUND_LIST_NEW "내부 음악 이어 붙이기") ----------------
# 기준 = 기본 모듈 곡: D 단조 · 90 BPM · 32마디 (85.3초). 나머지 셋은 이 조 · 빠르기 · 마디 수에 맞춰 고름.
# 기본 (생존 공업 → 조금 묵직하지만 아늑하게) — 후보 18곡(프롬프트 9가지) 중 b1:
#   요청 = 마림바 · 칼림바 반복 + 따뜻한 패드 + 작은 기계 딸깍 타악 + 둥근 베이스, "조금 묵직한 공업, 그래도 다정하게"
#   고른 이유 = 빠르기 90.0 정확 · D 단조가 가장 또렷 (0.89) · 고른 구간 44마디 · 선율 몫 53% (90짜리 중 가장 낮음 — 배경에 깔림) · 이음새 0.444 (가장 매끈)
#   뺀 것: c1 · d1 = 꼭대기까지 뻗는 잔 고음 타악이 빽빽 (딸깍 지직) · a1 · a2 · b2 · c2 · e · i = 빠르기 92 ~ 103 (기준 90에서 벗어남)
#          f = A 장조 · h = 끊김 마디 6개 · g = 고른 구간 16 ~ 22마디 · VARCO는 "major"를 넣어도 18곡 중 16곡이 단조로 나옴
REF = build("music_interior_0.ogg", "interior_0_basic.mp3", 70.41, 32, 90.0)
_, rbar, _, rscore = grid_of(REF, 32)
print(f"  기준 곡 마디 머리 박 {rbar} (점수 {np.round(rscore, 2).tolist()})")
# 나머지 셋 = 4곡이 늘 같은 자리를 돌도록 기준 곡 샘플 수에 맞추고 (ref_len), 박 · 마디 머리를 기준 곡에 맞춰 돌림 (ref) —
# 방을 옮겨 두 곡이 2.5초 겹칠 때 박이 엇갈리지 않게 (1단계 곡은 처음에 박 사이 한가운데서 잘렸었음 — 379 ms 어긋남).
# 1단계 (미래 기술 — 매끈한 전자) — 후보 10곡(프롬프트 5가지) 중 c2:
#   요청 = 부드러운 신스 아르페지오 + 전기 피아노 화음 + 매끈한 서브 베이스 + 옅은 전자 하이햇 ("calm synthwave-lite")
#   고른 이유 = 10곡 중 유일하게 빠르기 90.0 · D 단조 0.85 · 고른 구간 33마디. 루프 끝이 프레이즈 끝(길게 끄는 화음)이라 처음으로 자연스럽게 돌아감
#   뺀 것: 나머지 9곡 = 빠르기 90.5 ~ 93 (요청 90이어도 92 ~ 93이 많음)
build("music_interior_1.ogg", "interior_1_tech.mp3", 31.41, 32, 90.0, len(REF), REF)
# 2단계 (테마파크 — 밝음) — 후보 18곡(프롬프트 9가지) 중 f2:
#   요청 = 토이 피아노 화음 + 우쿨렐레 + 둥근 베이스 + 옅은 탬버린, "carefree and warm" (F 장조 = D 단조와 같은 음)
#   고른 이유 = 빠르기 90.0 · F 장조 0.86 / D 단조 0.82 (같은 음계) · 고른 구간 37마디 · 이음새 0.347 (가장 매끈)
#   뺀 것: 90짜리 b1 · b2 · g1 = A 단조 · C 장조 (D 단조와 B음이 부딪힘) · F 장조 a1 · c1 · e1 · e2 = 빠르기 64 ~ 100
build("music_interior_2.ogg", "interior_2_park.mp3", 32.76, 32, 90.0, len(REF), REF)
# 3단계 이상 (동화) — 후보 18곡(프롬프트 9가지) 중 e1:
#   요청 = 첼레스타 · 오르골 반복 + 따뜻한 현악 + 플루트 패드 + 피치카토 베이스 ("enchanted storybook orbital city")
#   고른 이유 = 빠르기 90.0 · F 장조 0.89 (밝은 동화) · 고른 구간 38마디 · 끊김 2마디(루프 밖)
#   뺀 것: c1 = D 단조 0.90이지만 고른 구간 31마디 · 어두움 · b · d = B 단조 · E 단조 (D 장조 · G 장조 음계) · i1 = 91.5 · a = C 장조
build("music_interior_3.ogg", "interior_3_tale.mp3", 8.28, 32, 90.0, len(REF), REF)
