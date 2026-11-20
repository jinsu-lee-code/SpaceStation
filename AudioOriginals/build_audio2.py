"""
SpaceStation 사운드 개편 (2026-11-20, SOUND_LIST_NEW.md) 다듬기 스크립트.
VARCO에서 만들어 고른 원본(AudioOriginals/<묶음>/)을 잘라 · 이어 · 음량을 맞춰 Assets/_Project/Audio/ 아래 게임용 WAV로 만든다.
1차 build_audio.py는 다시 돌리면 1차 소리 66개를 전부 새로 만들므로, 2차 소리는 이 스크립트에만 더한다.
실행: "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/build_audio2.py   (numpy만 씀)

고른 이유 · 수치는 SOUND_LIST_NEW.md "고르는 기준"과 각 묶음 주석에.
"""
import os, sys, wave
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
DST = os.path.join(HERE, "..", "SpaceStation", "Assets", "_Project", "Audio")
SR = 48000
report = []


# ---------------- 입출력 ----------------

def load(rel):
    with wave.open(os.path.join(HERE, rel)) as w:
        if w.getframerate() != SR or w.getsampwidth() != 2:
            raise ValueError(f"{rel}: 48 kHz 16 bit만 ({w.getframerate()} Hz, {w.getsampwidth() * 8} bit)")
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768
        if w.getnchannels() > 1:
            x = x.reshape(-1, w.getnchannels()).mean(1)
    return x


def save(rel, x, target_db=-14.0, peak_db=-1.0):
    """짧은 구간(50 ms) 최대 RMS를 target_db로 맞추고, 피크는 peak_db를 넘지 않게 (1차와 같은 기준)."""
    w = int(SR * 0.05)
    st = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, mode="valid").max()) if len(x) > w else np.sqrt((x ** 2).mean())
    g = 10 ** (target_db / 20) / max(st, 1e-9)
    lim = 10 ** (peak_db / 20)
    if np.abs(x).max() * g > lim:
        g = lim / np.abs(x).max()
    y = np.clip(x * g, -1, 1)
    path = os.path.join(DST, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes((y * 32767).astype("<i2").tobytes())
    report.append(f"{rel:36s} {len(y) / SR:5.2f}s  peak {20 * np.log10(max(np.abs(y).max(), 1e-9)):6.1f} dB")


# ---------------- 도구 ----------------

def sl(x, a, b):
    return x[int(a * SR):int(b * SR)].copy()


def fade(x, fin=0.004, fout=0.05):
    x = x.copy()
    ni, no = int(fin * SR), int(fout * SR)
    if ni > 0:
        x[:ni] *= np.linspace(0, 1, ni) ** 2
    if no > 0:
        x[-no:] *= np.linspace(1, 0, no) ** 2
    return x


def trim_lead(x, thresh_db=-40):
    thr = np.abs(x).max() * 10 ** (thresh_db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    return x[max(0, idx[0] - int(0.003 * SR)):] if len(idx) else x


def onepole_lp(x, fc):
    a = np.exp(-2 * np.pi * fc / SR)
    y = np.empty_like(x)
    s = 0.0
    for i in range(len(x)):
        s = (1 - a) * x[i] + a * s
        y[i] = s
    return y


def highpass(x, fc=50):
    """발밑 울림 · 직류 제거 (2차)."""
    return x - onepole_lp(onepole_lp(x, fc), fc)


def soften(x, fc=7000, amount=0.35):
    """고음 "치익"이 너무 날카로울 때 원본과 로우패스를 섞음."""
    return (1 - amount) * x + amount * onepole_lp(onepole_lp(x, fc), fc)


def hit_time(x, after=0.0):
    """가장 큰 부딪힘 시각: after초 뒤에서 가장 큰 곳을 찾고, 그 앞 0.3초 안에서 소리 크기가 가장 가파르게 커진 순간.
    (가장 크게 뛴 곳만 보면 큰 "쾅" 뒤의 작은 부딪힘을 잡기도 함 — 2026-11-20 기본 문 닫힘 2번)"""
    w = int(SR * 0.01)
    env = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, mode="same"))[::w]
    start = int(after * SR / w)
    ip = start + int(np.argmax(env[start:]))
    lo = max(start, ip - 30)
    rise = np.diff(env[lo:ip + 1])
    return (lo + 1 + int(np.argmax(rise))) * w / SR if len(rise) else ip * w / SR


def place(dst, src, at):
    i = int(at * SR)
    if i + len(src) > len(dst):
        dst = np.concatenate([dst, np.zeros(i + len(src) - len(dst))])
    dst[i:i + len(src)] += src
    return dst


def hit_at(x, hit, at=0.25, length=0.85, fout=0.25):
    """부딪힘(hit 초)이 at초에 오게 앞을 자르고 length초로 (문이 닫히는 0.25초에 "쾅")."""
    a = max(0.0, hit - at)
    return fade(sl(x, a, a + length), 0.03, fout)


def tame(x, reduce_db=6.0, attack=0.004, release=0.08):
    """가장 센 부분(쾅)만 reduce_db까지 눌러 덜 치고 나오게 (나머지 미끄러짐 · 치익은 그대로).
    2026-11-20 사용자 "기본 문 닫히는 쾅이 순간적으로 너무 치고 나옴"."""
    w = int(SR * 0.005)
    env = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, mode="same")) + 1e-9
    thr = env.max() * 10 ** (-reduce_db / 20)
    target = np.minimum(1.0, (thr / env) ** 0.8)
    g = np.empty_like(target)
    s = 1.0
    ka, kr = np.exp(-1 / (attack * SR)), np.exp(-1 / (release * SR))
    for i in range(len(target)):
        k = ka if target[i] < s else kr
        s = k * s + (1 - k) * target[i]
        g[i] = s
    return x * g


def circular(fn, x, pad=0.5):
    """루프용: 끝을 앞에 붙여 필터를 돌린 뒤 잘라 이음새가 그대로 이어지게."""
    n = int(pad * SR)
    return fn(np.concatenate([x[-n:], x]))[n:]


# ================= 내부 문 (SOUND_LIST_NEW 4-1 · D-6) =================
# 문은 0.25초 만에 열리고 닫힘 (InteriorDoor Speed 4) → 소리는 바로 시작, 1초 안팎. 문 자리에서 3D로 남 (방 울림은 게임이 따로 → 여기선 마른 소리).

D = "Interior/door/"
# 음량 (2026-11-20 검수 녹음: 처음 −14 dB + 날 선 "쾅" = 방 바탕 위 약 8 dB로 튀어나옴 → −21 dB는 바탕 위 3 dB로 묻힘 → 쾅은 tame으로 눌러 두고 바탕 위 4 ~ 6 dB)
OPEN, CLOSE, OPEN1, CLOSE1, LOCK = -17.0, -18.0, -18.0, -19.0, -15.0

# 기본 문 열림 — 철컥 + 치익. 5 = 첫 0.6초에 둘 다 있음 / 2 = 앞 걸쇠 + 짧은 치익 + 2.7초의 멈춤 소리를 0.3초로 당김 / 3 = 첫 0.6초
x = load(D + "basic_open_5.wav")
save("Interior/door_open_01.wav", fade(highpass(soften(tame(trim_lead(x)[: int(0.8 * SR)], 4))), 0.003, 0.3), OPEN)
x = load(D + "basic_open_2.wav")
head = fade(sl(x, 0.0, 0.5), 0.003, 0.2)
stop = fade(sl(x, hit_time(x, after=2.0) - 0.01, hit_time(x, after=2.0) + 0.4), 0.005, 0.2) * 0.5
save("Interior/door_open_02.wav", highpass(soften(tame(place(head, stop, 0.3), 4))), OPEN)
x = load(D + "basic_open_3.wav")
save("Interior/door_open_03.wav", fade(highpass(soften(tame(trim_lead(x)[: int(0.7 * SR)], 4))), 0.003, 0.28), OPEN)

# 기본 문 닫힘 — 미끄러짐 → 쾅. 셋 다 쾅이 0.25초에 오게 앞을 자름. 쾅은 7 dB 누르고 날 끝(3 kHz 위)을 반쯤 깎아 "퍽"보다 "툭"으로
for i, n in enumerate((3, 4, 5), 1):
    x = load(D + f"basic_close_{n}.wav")
    save(f"Interior/door_close_{i:02d}.wav", highpass(soften(tame(hit_at(x, hit_time(x), 0.25, 0.9, 0.3), 7), 3000, 0.5)), CLOSE)

# 1단계 문 열림 — 부드러운 한 덩어리 "스윽" (기본보다 작게)
x = load(D + "s1_open_4.wav")
save("Interior/door1_open_01.wav", fade(highpass(trim_lead(x)), 0.003, 0.2), OPEN1)
for i, n in ((2, 2), (3, 5)):
    x = load(D + f"s1_open_{n}.wav")
    save(f"Interior/door1_open_{i:02d}.wav", fade(highpass(trim_lead(x)[: int(1.0 * SR)]), 0.003, 0.4), OPEN1)

# 1단계 문 닫힘 — 2 = "스윽 → 딸깍" (딸깍을 0.3초로) / 5 · 1 = 짧은 스윽
x = load(D + "s1_close_2.wav")
save("Interior/door1_close_01.wav", highpass(tame(hit_at(x, hit_time(x), 0.3, 0.9, 0.3), 5)), CLOSE1)
x = load(D + "s1_close_5.wav")
save("Interior/door1_close_02.wav", fade(highpass(trim_lead(x)[: int(1.0 * SR)]), 0.003, 0.4), CLOSE1)
x = load(D + "s1_close_1.wav")
save("Interior/door1_close_03.wav", fade(highpass(trim_lead(x), 80), 0.003, 0.25), CLOSE1)

# 에어록 — 열림 = 압력이 확 빠지는 치익 (4) · 철컥 여러 번 + 치익 (2) / 닫힘 = 철컥 → 치익 (5) · 쿵 + 낮은 웅 (1). 기본 문보다 조금 크고 길게
x = load(D + "airlock_open_4.wav")
save("Interior/airlock_open_01.wav", fade(highpass(tame(trim_lead(x)[: int(1.4 * SR)], 4)), 0.003, 0.5), LOCK)
x = load(D + "airlock_open_2.wav")
save("Interior/airlock_open_02.wav", fade(highpass(tame(trim_lead(x)[: int(1.3 * SR)], 4)), 0.003, 0.5), LOCK)
x = load(D + "airlock_close_5.wav")
save("Interior/airlock_close_01.wav", fade(highpass(tame(trim_lead(x)[: int(1.6 * SR)], 4)), 0.003, 0.6), LOCK)
x = load(D + "airlock_close_1.wav")
save("Interior/airlock_close_02.wav", fade(highpass(tame(trim_lead(x)[: int(1.4 * SR)], 4), 70), 0.003, 0.6), LOCK)


# ================= 1차 방 환경음 손질 (모듈별 소리 풍경 D-5 · D-7로 바꾸기 전까지) =================
# 2026-11-20 사용자 "배경으로 깔리는 치지지지직" → 1차 합성 때 전기 방에 일부러 넣은 "지직"(2 ~ 8 kHz 잡음 터짐 9번/12초) + 3150 Hz 삐,
# 고열 방의 "탁탁"(1.5 ~ 6 kHz 30번)이 원인. 1차 결과물을 Interior/legacy/에 보관해 두고 거기서 고음만 걸러 다시 만듦 (몇 번 돌려도 같은 결과).
L = "Interior/legacy/"
lp2 = lambda fc: (lambda y: onepole_lp(onepole_lp(y, fc), fc))
save("Interior/amb_room_electric_loop.wav", circular(lp2(1200), load(L + "amb_room_electric_loop.wav")), -24)
save("Interior/amb_room_heat_loop.wav", circular(lp2(1200), load(L + "amb_room_heat_loop.wav")), -22)
# 기계 방: 1.75초마다 같은 "칙·틱"이 또렷하게 반복 → 고음을 눌러 멀리서 나는 소리로
save("Interior/amb_room_machine_loop.wav", circular(lp2(2000), load(L + "amb_room_machine_loop.wav")), -24)


# ================= 발소리 (SOUND_LIST_NEW 4-1 · D-2) =================
# 2026-11-20 사용자 "걸을 때 재질에 닿는 느낌이 아니라 통통 튀는 소리" → 1차 합성 발소리는 300 Hz 아래 90% · 울림 봉우리 6 ~ 7.7 (북처럼 "통").
# VARCO 금속 바닥 부츠 걸음 후보 5개 중 4 · 5 (걸음마다 뒤꿈치 · 앞꿈치 "탁"이 높은 소리까지 뻗음, 울림 봉우리 3 안팎, 꼬리 짧음)에서 9걸음.
# 2는 걸음마다 울림 꼬리가 길어 탈락, 1은 걸음 수가 적고 울림이 섞임, 3은 긁는 소리 · 겹친 걸음이 섞임.
S = "Interior/step/"
steps = [("metal_steps_4.wav", t) for t in (2.11, 2.72, 3.28, 3.77, 4.33)] + [("metal_steps_5.wav", t) for t in (1.65, 2.19, 2.71, 3.40)]
cache = {}
for k, (f, t) in enumerate(steps, 1):
    x = cache.setdefault(f, load(S + f))
    seg = trim_lead(sl(x, max(0.0, t - 0.03), t + 0.32), -30)[: int(0.3 * SR)]
    save(f"Interior/step_metal_{k:02d}.wav", fade(highpass(seg, 60), 0.002, 0.08), -17)

print("\n".join(report))
