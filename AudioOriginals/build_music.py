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


def build(dst, src, start, bars, bpm):
    x = load(src)
    beat = exact_beat(x.mean(1), bpm)
    out, L, corr = make_loop(x, start, bars, beat)
    out = level(out)
    p = save(dst, out)
    # 확인: 두 번 이어 붙였을 때 이음새 앞뒤 50 ms 크기 차
    y = np.concatenate([out, out]).mean(1); j = len(out)
    a = 20 * np.log10(np.sqrt((y[j - 2400:j] ** 2).mean()) + 1e-9); b = 20 * np.log10(np.sqrt((y[j:j + 2400] ** 2).mean()) + 1e-9)
    print(f"{dst}: BPM {60 / beat:.3f} · {bars}마디 = {L / SR:.3f}s (박 기준 {bars * 4 * beat:.3f}s) · 끝 맞춤 상관 {corr[0]:.3f} (옮김 {corr[1] / SR * 1000:+.1f} ms) · 이음새 크기 차 {b - a:+.1f} dB · {os.path.getsize(p) // 1024} KB")


# ---------------- 내부 음악 (단계별 4곡, 같은 조 · 빠르기 · 마디 수 — SOUND_LIST_NEW "내부 음악 이어 붙이기") ----------------
# 기준 = 기본 모듈 곡: D 단조 · 90 BPM · 32마디 (85.3초). 나머지 셋은 이 조 · 빠르기 · 마디 수에 맞춰 고름.
# 기본 (생존 공업 → 조금 묵직하지만 아늑하게) — 후보 18곡(프롬프트 9가지) 중 b1:
#   요청 = 마림바 · 칼림바 반복 + 따뜻한 패드 + 작은 기계 딸깍 타악 + 둥근 베이스, "조금 묵직한 공업, 그래도 다정하게"
#   고른 이유 = 빠르기 90.0 정확 · D 단조가 가장 또렷 (0.89) · 고른 구간 44마디 · 선율 몫 53% (90짜리 중 가장 낮음 — 배경에 깔림) · 이음새 0.444 (가장 매끈)
#   뺀 것: c1 · d1 = 꼭대기까지 뻗는 잔 고음 타악이 빽빽 (딸깍 지직) · a1 · a2 · b2 · c2 · e · i = 빠르기 92 ~ 103 (기준 90에서 벗어남)
#          f = A 장조 · h = 끊김 마디 6개 · g = 고른 구간 16 ~ 22마디 · VARCO는 "major"를 넣어도 18곡 중 16곡이 단조로 나옴
build("music_interior_0.ogg", "interior_0_basic.mp3", 70.41, 32, 90.0)
