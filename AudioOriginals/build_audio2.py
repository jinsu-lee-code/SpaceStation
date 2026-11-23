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


def notch(x, f, q=8.0, passes=2):
    """좁은 울림 하나(f Hz)만 깎음 — 2026-11-20 사용자 "문 닫힐 때 찰랑거리는 소리가 어색" (A5의 2391 Hz 쇳소리 울림)."""
    w0 = 2 * np.pi * f / SR
    alpha = np.sin(w0) / (2 * q)
    b = np.array([1.0, -2 * np.cos(w0), 1.0]) / (1 + alpha)
    a = np.array([1.0, -2 * np.cos(w0) / (1 + alpha), (1 - alpha) / (1 + alpha)])
    for _ in range(passes):
        y = np.zeros_like(x)
        x1 = x2 = y1 = y2 = 0.0
        for i in range(len(x)):
            v = b[0] * x[i] + b[1] * x1 + b[2] * x2 - a[1] * y1 - a[2] * y2
            x2, x1, y2, y1 = x1, x[i], y1, v
            y[i] = v
        x = y
    return x


def circular(fn, x, pad=0.5):
    """루프용: 끝을 앞에 붙여 필터를 돌린 뒤 잘라 이음새가 그대로 이어지게."""
    n = int(pad * SR)
    return fn(np.concatenate([x[-n:], x]))[n:]


# ================= 내부 문 (SOUND_LIST_NEW 4-1 · D-6) =================
# 문은 0.25초 만에 열리고 닫힘 (InteriorDoor Speed 4) → 소리는 바로 시작, 1초 안팎. 문 자리에서 3D로 남 (방 울림은 게임이 따로 → 여기선 마른 소리).

D = "Interior/door/"
# 음량 (2026-11-20 검수 녹음: 처음 −14 dB + 날 선 "쾅" = 방 바탕 위 약 8 dB로 튀어나옴 → −21 dB는 바탕 위 3 dB로 묻힘 → 쾅은 tame으로 눌러 두고 바탕 위 4 ~ 6 dB)
OPEN, CLOSE, OPEN1, CLOSE1, LOCK = -15.0, -17.0, -18.0, -19.0, -15.0  # 기본 문 하나로 통일 뒤 녹음 검수: 열림 +0.4 ~ 2.3 dB로 묻힘 → +2 dB, 닫힘 +1 dB

# 기본 문 열림 — 철컥 + 치익 (2026-11-20 아기자기 톤: 철컥을 6 dB 누르고 5 kHz 위를 반쯤 둥글게). basic_open_5 첫 0.8초.
# 2026-11-20 사용자 "하나로 통일 — 산업 공단 느낌의 기본 모듈, 조금 묵직한 음 하나" → 후보 셋(5 · 2 · 3)을 하나(5)로, 반복감은 음높이 ±3%로만
x = load(D + "basic_open_5.wav")
save("Interior/door_open_01.wav", fade(highpass(soften(tame(trim_lead(x)[: int(0.8 * SR)], 6), 5000, 0.5)), 0.003, 0.3), OPEN)

# 기본 문 닫힘 — 처음 "쾅"(너무 강함) → 부드러운 툭 셋(A5 · A2 · B3, 성격이 제각각 + A5 찰랑) → 2026-11-20 사용자 "하나로 통일, 산업 공단 느낌으로 조금 묵직하게"
# → VARCO "산업용 미닫이 · 조금 묵직하지만 부드럽게 먹먹한 쿵 · 쇳소리 울림 · 날 선 쾅 없이" 5개 중 5: 작은 딸깍 몇 번 → 0.53초 쿵, 300 Hz 아래 87 %,
# 순간 최고 6.3 dB (가장 둥긂) · 찰랑 0 · 잡음 −52 dB. (4 = 찰랑 26, 3 = 앞 삐 + 꼬리 3.6초, 2 = 3초 뒤 또 부딪힘, 1 = 덜 묵직). 쿵을 0.25초에.
x = load(D + "close_heavy_5.wav")
save("Interior/door_close_01.wav", highpass(soften(tame(hit_at(x, hit_time(x), 0.36, 1.0, 0.35), 3), 4000, 0.4)), CLOSE)  # 둥근 쿵은 커지는 데 0.14초 → 시작이 0.2초쯤 (문 닫힘 0.25초)

# 1단계 문 열림 — 부드러운 한 덩어리 "스윽" (기본보다 작게)
x = load(D + "s1_open_4.wav")
save("Interior/door1_open_01.wav", fade(highpass(trim_lead(x)), 0.003, 0.2), OPEN1)
for i, n in ((2, 2), (3, 5)):
    x = load(D + f"s1_open_{n}.wav")
    save(f"Interior/door1_open_{i:02d}.wav", fade(highpass(trim_lead(x)[: int(1.0 * SR)]), 0.003, 0.4), OPEN1)

# 1단계 문 닫힘 — 2 = "스윽 → 딸깍" (딸깍을 0.3초로) / 5 = 짧은 스윽
x = load(D + "s1_close_2.wav")
save("Interior/door1_close_01.wav", highpass(tame(hit_at(x, hit_time(x), 0.3, 0.9, 0.3), 5)), CLOSE1)
x = load(D + "s1_close_5.wav")
save("Interior/door1_close_02.wav", fade(highpass(trim_lead(x)[: int(1.0 * SR)]), 0.003, 0.4), CLOSE1)
# s1_close_1 ("웅" 자석 소리 + 4.2 · 8 kHz 울림 — 검수 ring 28, 깎아도 8 = "찰랑") → 2026-11-20 뺌, 1단계 닫힘은 두 개로

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

# ================= 3단계 소리 풍경 — AI 중추 "수정 뇌 미로 정원" (SOUND_LIST_NEW D-7b) =================
# 음악 = 내부 음악 3단계 (차분한 현악 · 플루트 패드, 90 BPM, A · C · D · E · F · G 중심 — B · F# · E♭ 약함).
# 음이 있는 소리는 그 음들로만 (런타임에 음계 높이로 바꿔 다음 박에), 맥동하는 루프는 박 길이의 배수 (90 BPM 1박 = 0.6667초).
# VARCO는 하나씩 순서대로 (2026-11-23 지침): 바탕 1번 · 수정 공명 2번 · 드론 2번 · 새 · 띵 · 책장 각 1번 = 8번.
A = "Interior/scape_aicore/"
BEAT = 60.0 / 90.0


def chain_loop(parts, xf):
    """조각들을 xf초씩 겹쳐 이어 한 바퀴 루프로 (마지막 → 처음도 같은 겹침, equal-power)."""
    n = int(xf * SR)
    t = np.linspace(0, np.pi / 2, n)
    out = parts[0][n:].copy()
    for p in parts[1:] + [parts[0]]:
        out[-n:] = out[-n:] * np.cos(t) + p[:n] * np.sin(t)
        out = np.concatenate([out, p[n:]])
    return out[:-(len(parts[0]) - n)]


def loop_exact(x, length):
    """x의 앞 length초를 루프로 — 뒤에 남는 0.8초를 처음에 겹쳐 이음 (x는 length + 0.8초 넘게)."""
    n, k = int(length * SR), int(0.8 * SR)
    out = x[:n].copy()
    t = np.linspace(0, np.pi / 2, k)
    out[:k] = x[:k] * np.sin(t) + x[n:n + k] * np.cos(t)
    return out


def phrases(x, gap=0.4, pad=0.12, rise_db=18):
    """조용한 바닥 위로 rise_db 넘게 솟는 구간들 (gap초 안 틈은 하나로) — 새 지저귐 소절 자르기."""
    w = int(0.01 * SR)
    env = 20 * np.log10(np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "same")) + 1e-9)
    on = env > np.percentile(env, 20) + rise_db
    segs, i = [], 0
    idx = np.where(on)[0]
    for j in idx:
        if segs and j - segs[-1][1] < gap * SR:
            segs[-1][1] = j
        else:
            segs.append([j, j])
    return [(max(0, a - int(pad * SR)), min(len(x), b + int(pad * SR))) for a, b in segs if b - a > 0.15 * SR]


def pitch(x, semis):
    """음높이 바꾸기 (재생 속도째 — 길이도 바뀜). 짧은 맑은 음에만."""
    r = 2 ** (semis / 12)
    pos = np.arange(0, len(x) - 1, r)
    return np.interp(pos, np.arange(len(x)), x)


# 바탕 — 정원 바람 · 잎 (2D, 방 전체). 후보 5 중 2 (가장 고름 · 지직 3.5 · 울림 1) + 5 (잎 사각임 · 울림 0)를 2초씩 겹쳐 19초 —
# 10초 하나를 돌리면 반복이 들림. 3 = 밝고 (716 Hz) 지직 11.5 · 6.9초에 사건, 4 = 울림 5, 1 = 5.3초에 작은 사건.
bed = chain_loop([sl(load(A + "bed_a.wav"), 0.3, 9.9), sl(load(A + "bed_b.wav"), 0.3, 9.9)], 2.0)
save("Interior/scape_aicore_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

# 수정 뇌 공명 (뇌 자리 3D 루프) — VARCO 첫 5개 = 2 ~ 8초 한 번 부풀었다 사라짐 (루프 X), "긴 · 고른"으로 다시 5개 = 고르지만
# 낮은 웅웅뿐이거나 맑은 음이 음악과 안 맞음 (2 = 600 Hz D+37센트 · 3800 Hz 삐). → 몸통 = 다시 만든 4 (울림 줄 6 — 가장 적음),
# 유리 공명 = 합성 (라4 · 도5 · 미5 + 라3, 음악 4곡 어느 화음 위에서도 안 부딪힘), 2박(1.333초)마다 숨 쉬듯 맥동.
# 길이 = 8초 = 정확히 12박 (96초 음악 루프의 1/12) — 음 · 맥동 주기가 8초에 딱 떨어지게 주파수를 1/8 Hz 단위로.
L8 = 8.0
tt = np.arange(int(L8 * SR)) / SR
glass = np.zeros_like(tt)
for f0, amp in ((220.0, 0.5), (440.0, 1.0), (523.25, 0.8), (659.25, 0.6)):
    for cents in (-3, 0, 3):
        f = round(f0 * 2 ** (cents / 1200) * L8) / L8
        glass += amp / 3 * (np.sin(2 * np.pi * f * tt) + 0.22 * np.sin(2 * np.pi * 2 * f * tt + 0.7) + 0.08 * np.sin(2 * np.pi * 3 * f * tt + 1.9))
pulse = 0.55 + 0.45 * (0.5 - 0.5 * np.cos(2 * np.pi * tt / (2 * BEAT))) ** 1.5
glass = circular(lambda y: onepole_lp(y, 2500), glass * pulse)
body = loop_exact(sl(load(A + "brain_body.wav"), 0.5, 10.0), L8)
body = circular(lambda y: highpass(y, 35), body) * (0.8 + 0.2 * pulse)
g_rms, b_rms = np.sqrt((glass ** 2).mean()), np.sqrt((body ** 2).mean())
save("Interior/scape_aicore_brain_loop.wav", 0.55 * glass / g_rms + 0.45 * body / b_rms, -22)

# 드론 둥지 탑 — 작은 드론이 깨어나 뜨는 "그르릉" (가끔, 탑 자리 3D). 첫 5개 = 소리 중심 7 ~ 16 kHz 높은 "위잉" 쉿,
# "낮고 둥근 작은 로봇 숨소리"로 다시 5개 중 5 (155 Hz · 300 Hz 아래 99 % · 빠르게 떨리는 그르릉, 2.5초 한 번 부풂). 나머지 = 기계 삐 줄.
save("Interior/scape_aicore_drone_01.wav", fade(highpass(trim_lead(load(A + "drone_purr.wav"), -45), 40), 0.05, 0.4), -14)   # 2026-11-23 −24 → −14: 음악까지 켠 소리에서 귀 감도 −23 dB로 안 들렸음

# 새 (낮에만, 정원 여기저기 3D) — 5개 중 1의 두 소절 + 5의 한 소절 (바닥 잡음 −46 ~ −51 dB).
# 3 = 열 번 넘게 연달아 (바쁨), 4 = 날카로움 11.6 · 바닥 −38 dB, 2 = 한 번 터진 뒤 잡음 꼬리.
k = 0
for f in ("bird_1.wav", "bird_5.wav"):
    x = load(A + f)
    for a, b in phrases(x):
        k += 1
        save(f"Interior/scape_aicore_bird_{k:02d}.wav", fade(highpass(x[a:b], 300), 0.02, 0.1), -24)

# 시냅스 빛 "띵" (미로 길 따라 3D, 음계 높이 · 다음 박) — 5개 중 2: 한 번만 맑게 울리고 C7 +6센트 (거의 정확한 '도').
# 1 = 여러 번 침, 3 = F#6 · 4 = D5 +35센트 · 5 = D#6 +33센트 (음악 음과 어긋남). 시작 2 ms로 조금 날카로움 → 8 ms로 둥글게 · 4.5 kHz 위 반쯤.
ting = fade(soften(trim_lead(load(A + "crystal_ting.wav"), -40)[: int(1.8 * SR)], 4500, 0.5), 0.008, 0.8)
ting = pitch(ting, -0.06)  # +6센트 → 정확히 C7 (런타임 음높이 계산의 기준 = MIDI 96)
save("Interior/scape_aicore_ting_01.wav", ting, -14)   # 2026-11-23 −24 → −14: 음악까지 켠 소리에서 귀 감도 −25 dB로 안 들렸음 (바이오스피어 반딧불도 이 소리 — 음량을 그만큼 낮춤)

# 대표 선율 (들어설 때 한 번, 2D) — 같은 띵을 미6 · 솔6 · 도7로, 8분음표 (반 박 = 0.333초) 간격, 마지막 음은 길게.
motif = np.zeros(int(2.6 * SR))
for i, (semis, gain) in enumerate(((-8, 0.8), (-5, 0.85), (0, 1.0))):
    motif = place(motif, pitch(ting, semis) * gain, i * BEAT / 2)
save("Interior/scape_aicore_motif.wav", fade(motif, 0.0, 0.6), -20)

# 독서대 책장 넘김 (주민이 책 읽는 자리, 3D, 가끔) — 5개 중 3: 넘김이 하나씩 떨어져 있고 바닥 −49.6 dB (가장 깨끗).
x = load(A + "page_3.wav")
w = int(0.05 * SR)
env = np.convolve(x ** 2, np.ones(w) / w, "same")
i = int(np.argmax(env))
seg = x[max(0, i - int(0.35 * SR)): i + int(0.45 * SR)]
save("Interior/scape_aicore_page_01.wav", fade(highpass(seg, 120), 0.03, 0.15), -26)

# ================= 3단계 소리 풍경 — 바이오스피어 "물의 순환 고리" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/Biosphere.cs 값 (폭포 기둥 · 수문 폭포 셋 · 물길 셋 · 물레방아 셋 · 도는 구름 고리 다섯 · 반딧불 숲). VARCO 하나씩 순서대로 10번.
B = "Interior/scape_bio/"


def period_of(x, lo=0.5, hi=1.6):
    """반복 주기 (초) — 크기 곡선의 자기상관 봉우리 (물레방아 한 바퀴)."""
    w = int(0.01 * SR)
    env = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "same"))[::w]
    env = env - env.mean()
    ac = np.correlate(env, env, "full")[len(env) - 1:]
    a, b = int(lo / 0.01), int(hi / 0.01)
    return (a + int(np.argmax(ac[a:b]))) * 0.01


# 바탕 — 숲 잎 사각임 · 바람이 일었다 잦아듦 (2D). 후보 5 중 3 (잎 사각 · 가끔 바람) + 4 (낮고 고른 공기)를 2초씩 겹쳐 15초.
# 1 · 2 = 2초짜리, 5 = 6 ~ 8 kHz 쉿 줄이 깔림.
bed = chain_loop([sl(load(B + "bed_a.wav"), 0.3, 9.9), sl(load(B + "bed_b.wav"), 0.3, 9.9)], 2.0)
save("Interior/scape_bio_bed_loop.wav", circular(lambda y: soften(highpass(y, 40), 6000, 0.4), bed), -24)

# 가운데 폭포 기둥 (하늘 호수 → 호수, 폭 1.2 m · 10 m, 3D) — 후보 5 중 4: 가장 매끈한 쏟아짐 (지직 4.8 · 날카로움 14.3).
# 1 = 밝음 (4.7 kHz) · 날카로움 16.9, 2 · 5 = 비슷하지만 조금 더 거칢. 아늑하게 5 kHz 위 반쯤.
save("Interior/scape_bio_waterfall_loop.wav", circular(lambda y: soften(highpass(y, 50), 5000, 0.5), loop_exact(sl(load(B + "waterfall.wav"), 0.2, 10.24), 9.0)), -22)

# 능선 수문 작은 폭포 셋 (폭 0.6 m, 3D) — 같은 후보 묶음 중 3: 가장 따뜻하고 첨벙임이 있음 (1.45 kHz · 4 kHz 위 4 %).
save("Interior/scape_bio_sluice_loop.wav", circular(lambda y: highpass(y, 60), loop_exact(sl(load(B + "sluice_fall.wav"), 0.2, 10.24), 9.0)), -24)

# 물길 졸졸 (단마다 작은 폭포, 3D) — 후보 5 중 3: 날카로움 12.1 · 지직 12.1 (가장 낮음) · 바닥 −15.5 dB. 처음 · 끝 페이드는 잘라 냄.
# 5 = 울림 12, 1 · 4 = 날카로움 · 지직 조금 더 높음.
save("Interior/scape_bio_brook_loop.wav", circular(lambda y: highpass(y, 80), loop_exact(sl(load(B + "brook.wav"), 0.6, 9.4), 8.0)), -24)

# 구름 아래 비 (구름 고리 다섯을 따라 돎, 3D) — 후보 5 중 3: 가장 어두움 (6.9 kHz) · 바닥 −17.4 dB. 빗방울은 원래 높은 소리 →
# 4.5 kHz 위를 눌러 "잎 너머로 듣는" 부드러운 비. 1 · 2 · 4 · 5 = 7.5 ~ 9.9 kHz로 더 밝음.
def flatten(x, win=1.5):
    """느린 크기 변화(끝으로 갈수록 잦아듦)를 펴서 고르게 — 루프 이음새에서 크기가 안 튀게."""
    w = int(win * SR)
    env = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "same")) + 1e-9
    return x * (np.median(env) / env)


rain = flatten(sl(load(B + "rain.wav"), 0.3, 7.6))
save("Interior/scape_bio_rain_loop.wav", circular(lambda y: soften(soften(highpass(y, 120), 4500, 0.5), 8000, 0.4), loop_exact(rain, 6.4)), -26)

# 물레방아 셋 (작은 나무 바퀴, 3D) — 첫 5개 = 그냥 고른 물소리 (바퀴 리듬 없음, 5 = 울림 167), "약 1초마다 물받이 철썩 + 삐걱"으로 다시 5개 중 4:
# 같은 무늬가 일정 간격으로 반복 · 날카로움 11.7 (가장 둥긂). 한 바퀴 주기를 재서 정수 바퀴로 루프.
x = load(B + "wheel.wav")
p = period_of(sl(x, 0.5, 6.5))
n = int(5.5 // p)
save("Interior/scape_bio_wheel_loop.wav", circular(lambda y: highpass(y, 80), loop_exact(sl(x, 0.6, 7.3), n * p - 0.0)), -26)
report.append(f"  물레방아 한 바퀴 {p:.2f}초 × {n}")

# 개구리 (물가 · 숲 길, 3D) — 후보 5 중 2: 짧은 "개굴" 묶음이 간격을 두고 (바닥 −57 dB). 1 · 3 · 4 = 길게 끄는 음 (신스 같음), 5 = 빠른 떨림.
x = load(B + "frog.wav")
for k, (a, b) in enumerate(phrases(x, gap=0.3)[:4], 1):
    save(f"Interior/scape_bio_frog_{k:02d}.wav", fade(highpass(x[a:b], 150), 0.01, 0.08), -24)

# 밤 풀벌레 (밤에만, 2D 층) — 후보 5 중 1: 쉬엄쉬엄 끊겨 우는 귀뚜라미. 3 · 4 · 5 = 한 높이로 끝없이 이어지는 "삐—" 줄, 2 = 바닥 −6.5 dB.
save("Interior/scape_bio_crickets_loop.wav", circular(lambda y: soften(highpass(y, 300), 8000, 0.4), loop_exact(sl(load(B + "crickets.wav"), 0.2, 10.24), 9.0)), -30)

# 먼 천둥 (밤에 아주 가끔, 돔 위 멀리) — 후보 5 중 2: 울림 줄 4 (나머지 14 ~ 51 = 낮은 "웅") · 바닥 −42.8 dB.
save("Interior/scape_bio_thunder_01.wav", fade(highpass(trim_lead(load(B + "thunder.wav"), -45), 30), 0.2, 1.5), -22)

# 물방울 (호수 · 물길 둘레, 음계 높이 · 다음 박) + 대표 선율 — 후보 5 중 2: 한 번 "똑", 날카로움 6.0 (가장 둥긂), C6 −33센트 → 정확한 C6 (MIDI 84).
# 1 = E7 (밝음) · 3 · 4 · 5 = 여러 번 침 · G#5 · F#7 (음계 밖).
drop = fade(trim_lead(load(B + "drop.wav"), -40)[: int(1.2 * SR)], 0.004, 0.5)
drop = pitch(highpass(drop, 200), 0.33)
save("Interior/scape_bio_drop_01.wav", drop, -24)
# 대표 선율 = 물이 아래로 떨어지듯 미6 · 도6 · 라5 · 솔5 (8분음표, 마지막 길게)
motif = np.zeros(int(2.8 * SR))
for i, (semis, gain) in enumerate(((4, 0.85), (0, 0.9), (-3, 0.95), (-5, 1.0))):
    motif = place(motif, pitch(drop, semis) * gain, i * BEAT / 2)
save("Interior/scape_bio_motif.wav", fade(motif, 0.0, 0.6), -20)

# ================= 3단계 소리 풍경 — 궤도 도시 "알이 깨어난 정원 섬 도시" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/OrbitalCity.cs 값 (계단 폭포 셋 · 고리 강 · 언덕 마을 · 꼭대기 광장 · 옥상 전망대 38.7 m). VARCO 하나씩 순서대로 7번.
# 궤도열차는 플레이어가 운전할 때만 달림 → 열차 소리는 체험 소리 때 (5-2).
OC = "Interior/scape_city/"

# 바탕 — 지붕 없는 섬 정원의 바람 (2D). 후보 5 중 3 (가장 매끈 — 날카로움 8.6 · 지직 3.7) + 2 (탁 트인 바람결)를 2초씩 겹쳐.
# 4 · 5 = 날카로움 21 ~ 24 (거칢), 1 = 날카로움 15.6.
bed = chain_loop([sl(load(OC + "bed_a.wav"), 0.3, 7.8), sl(load(OC + "bed_b.wav"), 0.3, 7.8)], 2.0)
save("Interior/scape_city_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

# 언덕 계단 폭포 셋 (단마다 2.4 ~ 2.9 m → 돌 웅덩이, 3D — 높이별로 재생 높이 1.05 · 1.0 · 0.95) — 후보 5 중 1:
# 가장 어둡고 (3.4 kHz) 가장 매끈 (날카로움 10.5 · 울림 0). 2 ~ 5 = 4 ~ 6 kHz로 밝고 날카로움 14 ~ 14.5.
save("Interior/scape_city_cascade_loop.wav", circular(lambda y: soften(highpass(y, 60), 5000, 0.4), loop_exact(sl(load(OC + "cascade.wav"), 0.2, 7.3), 6.2)), -23)

# 고리 강 (섬 둘레, 3D 여섯 곳) — 후보 5 중 2: 지직 10.9 (가장 낮음) · 날카로움 11.7. 3 · 5 = 날카로움 15 ~ 17.
save("Interior/scape_city_river_loop.wav", circular(lambda y: highpass(y, 60), loop_exact(flatten(sl(load(OC + "river.wav"), 0.3, 10.24)), 9.0)), -25)

# 언덕 마을 · 꼭대기 광장 웅성 (3D, 밤엔 15 %) — 후보 5 중 1: 울림 줄 3 (목소리가 하나하나 튀지 않고 흐릿하게 섞임).
# 2 ~ 5 = 울림 10 ~ 44 (특정 목소리 높이가 도드라짐).
save("Interior/scape_city_murmur_loop.wav", circular(lambda y: soften(highpass(y, 120), 5000, 0.4), loop_exact(flatten(sl(load(OC + "murmur.wav"), 0.3, 10.24)), 9.0)), -26)

# 옥상 전망대 꼭대기 바람 (38.7 m, 3D) — 후보 5 중 1: 울림 0 · 날카로움 8.7 (가장 고름).
save("Interior/scape_city_rooftop_loop.wav", circular(lambda y: highpass(y, 30), loop_exact(sl(load(OC + "rooftop.wav"), 0.3, 10.24), 9.0)), -24)

# 밤 창 불빛 웅웅 (밤에만, 언덕 마을 3D) — 후보 5 중 4: 가장 낮고 둥긂 (165 Hz · 날카로움 7.7 · 울림 0). 다섯 모두 "윙" 험 음 높이 봉우리 없음.
save("Interior/scape_city_night_loop.wav", circular(lambda y: highpass(y, 30), loop_exact(sl(load(OC + "night_glow.wav"), 0.3, 10.24), 9.0)), -26)

# 종 (가끔 섬 위로 + 대표 선율) — 후보 5 중 5: 한 번만 맑게, G6 0센트. VARCO "마을 종"은 손종 높이 (3 kHz 대) →
# 한 옥타브 내려 G5 (MIDI 79) 큰 종탑 종의 따뜻하고 긴 울림으로. 시작 1.6 ms → 8 ms로 둥글게.
# 1 · 3 = 여러 번 침, 2 = A7 −44센트, 4 = F#7 +40센트 배음 (음악과 부딪힘).
bell = fade(soften(trim_lead(load(OC + "bell.wav"), -45)[: int(3.2 * SR)], 5000, 0.4), 0.008, 1.2)
bell = pitch(highpass(bell, 150), -12)
save("Interior/scape_city_bell_01.wav", bell, -14)   # 2026-11-23 −24 → −14: 광장에서 음악 + 바탕 (−27 dB)에 15 dB 묻혀 안 들렸음 (사용자 "꼭대기 광장에서 종소리가 안 들림")
# 대표 선율 = 종탑 네 음 솔 · 미 · 라 · 도 (한 박 간격 — 종은 천천히)
motif = np.zeros(int(6.5 * SR))
for i, (semis, gain) in enumerate(((0, 0.9), (-3, 0.85), (2, 0.9), (5, 1.0))):
    motif = place(motif, pitch(bell, semis) * gain, i * BEAT)
save("Interior/scape_city_motif.wav", fade(motif, 0.0, 1.0), -20)

# ================= 3단계 소리 풍경 — 궤도 방어망 "등대 곶 해안" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/OrbitalDefense.cs 값 (왼쪽 곶 · 등대 렌즈 9.65 m · 앞 모래 해변 물가 z ≈ −1 · 바다 · 수정 부표 포대 · 방파제 끝 · 바다 동굴 입구 ·
# 곶 뒤 폭포). VARCO 하나씩 순서대로 11번 (렌즈 · 바닷새는 2번씩). 곶 뒤 폭포 · 곶 윗면 개울 · 등대 꼭대기 바람은 궤도 도시 소리를 같이 씀.
# 별똥별 반짝임 · 포대 수정 반짝임 · 밤 오로라 울림은 음악 조 (도 오음계)에 맞춰 합성.
OD = "Interior/scape_defense/"

# 바탕 — 잔잔한 만의 바닷바람 + 먼 잔물결 (2D). 후보 5 중 1 (9.6초 내내 고름) + 4 (조금 더 어두움)를 2초씩 겹쳐 14초.
# 2 = 위쪽 가는 울림 줄 (울림 24), 3 = 7.7초에 끊김, 5 = 낮은 웅 줄.
bed = chain_loop([sl(load(OD + "bed_a.wav"), 0.3, 9.3), sl(load(OD + "bed_b.wav"), 0.3, 6.8)], 2.0)
save("Interior/scape_defense_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

# 앞 해변 물가 잔파도 (물가 넷, 3D) — 후보 5 중 1: 모래로 밀려왔다 자갈 사이로 빠지는 파도가 5.3초 간격으로 둘 →
# 두 파도를 파도 시작에서 이어 8.1초 고리 (첫 파도 5.25초 · 둘째 2.85초 — 고르지 않은 게 자연스러움).
# 2 · 3 = 큰 너울 하나뿐, 4 = 1.4초마다 찰랑 (너무 잦음), 5 = 5.9초 안에 둘 (짧음).
shore = load(OD + "shore.wav")
shore = chain_loop([sl(shore, 0.0, 6.25), sl(shore, 5.25, 9.05)], 1.0)
save("Interior/scape_defense_shore_loop.wav", circular(lambda y: soften(highpass(y, 60), 7000, 0.3), shore), -24)

# 곶 · 동굴 입구 · 방파제 끝 바위 파도 (3D 셋) — 후보 5 중 2: 4초 안팎으로 고르게 세 번 → 파도 시작 (1.0 · 8.9초) 사이 7.9초 고리.
# 해변 잔파도와 갈리게 2.5 kHz 위를 눌러 둥글고 묵직하게. 1 = 더 어둡지만 간격이 들쭉날쭉, 3 = 바닥 잡음 −22.8 dB, 4 · 5 = 큰 파도 하나.
rock = loop_exact(sl(load(OD + "rock.wav"), 0.95, 10.24), 7.9)
save("Interior/scape_defense_rock_loop.wav", circular(lambda y: soften(soften(highpass(y, 40), 2500, 0.5), 6000, 0.4), rock), -23)

# 등대 렌즈 돌아감 (렌즈 가운데 9.65 m, 3D) — 첫 5개 = 딸깍 · 밝음 (2.3 ~ 5.8 kHz, 1 · 2 = 쇳소리 울림 122 · 49),
# "딸깍 없이 낮은 회전음"으로 다시 5개 중 3: 300 Hz 아래 79 % · 톱니 결이 일정 · 울림 6. 3.9초 → 결 주기를 재서 정수 번으로 고리.
# 다시 만든 5 = 7.3초에 끊김 · 울림 17, 2 = 딸깍이 띄엄띄엄, 1 = 2.8초 험, 4 = 울림 48.
lens = sl(load(OD + "lens.wav"), 0.15, 3.9)
lp = period_of(lens, 0.2, 1.0)
lens = loop_exact(lens, lp * int((len(lens) / SR - 0.85) / lp))
save("Interior/scape_defense_lens_loop.wav", circular(lambda y: soften(highpass(y, 40), 4000, 0.4), lens), -24)
report.append(f"  렌즈 결 주기 {lp:.2f}초 · 고리 {len(lens) / SR:.2f}초")

# 바다 가운데 수정 부표 포대 (3D) — 몸통 = 후보 5 중 1 (3.4초 · 가장 고른 낮은 웅웅, 뚜렷한 음 없이 퍼짐 → 음악과 안 부딪힘)을
# 앞뒤로 이어 늘림, 반짝임 = 합성 (도5 · 솔5 · 미6 — 도 오음계, 셋이 서로 다른 느린 숨으로). 다섯 모두 4 kHz 위가 없어 "수정"이 안 들렸음.
hb = sl(load(OD + "battery_hum.wav"), 0.3, 3.2)
body = chain_loop([hb, hb[::-1], hb, hb[::-1]], 0.8)
LB = len(body) / SR
tt = np.arange(len(body)) / SR
shim = np.zeros_like(tt)
for f0, amp, k in ((523.25, 1.0, 1), (783.99, 0.7, 2), (1318.51, 0.45, 3)):
    for cents in (-4, 4):
        f = round(f0 * 2 ** (cents / 1200) * LB) / LB
        shim += amp / 2 * np.sin(2 * np.pi * f * tt + k) * (0.55 + 0.45 * np.sin(2 * np.pi * k * tt / LB + 1.3 * k))
shim = circular(lambda y: onepole_lp(y, 3000), shim)
s_rms, b_rms = np.sqrt((shim ** 2).mean()), np.sqrt((body ** 2).mean())
save("Interior/scape_defense_battery_loop.wav", circular(lambda y: highpass(y, 35), 0.65 * body / b_rms + 0.35 * shim / s_rms), -24)
report.append(f"  포대 고리 {LB:.2f}초")

# 바닷새 (낮, 바다 위 여기저기 3D) — 첫 5개 = 바다 · 바람 잡음 띠에 새가 희미함 (바닥 −4 ~ −10 dB) → "배경 없는 가까운 녹음"으로
# 다시 5개 중 2 (갈매기 "꺄옥" 넷이 깔끔히 떨어짐 · 바닥 −53 dB)의 앞 · 뒤 소절 + 3 (짧은 퍼핀 재잘, 바닥 −62 dB).
# 다시 만든 1 = 울음이 뒤엉킴, 4 = 바닥 −41 dB, 5 = 일곱 번 같은 울음.
g = load(OD + "gull.wav")
ph = phrases(g, gap=0.12, pad=0.05)
half = max(1, len(ph) // 2)
for k, (a, b) in enumerate([(ph[0][0], ph[half - 1][1]), (ph[half][0], ph[-1][1])] if len(ph) > 1 else [ph[0]]):
    save(f"Interior/scape_defense_gull_{k + 1:02d}.wav", fade(highpass(g[a:b], 300), 0.01, 0.08), -14)
report.append(f"  갈매기 소절 {len(ph)}개")
pf = load(OD + "puffin.wav")
save("Interior/scape_defense_gull_03.wav", fade(highpass(trim_lead(pf, -45), 300), 0.01, 0.08), -14)

# 물범 (낮, 방파제 끝 바위 3D) — 후보 5 모두 딸깍 · 지직이 섞임 (지직 44 ~ 57) → 3의 앞 두 번 "멍멍" (0.05 ~ 0.8초)만.
seal = sl(load(OD + "seal.wav"), 0.05, 0.8)   # 0.8초 뒤 = 작은 딸깍 꼬리 (다듬은 뒤 그림으로 확인)
save("Interior/scape_defense_seal_01.wav", fade(soften(highpass(seal, 100), 4500, 0.5), 0.01, 0.12), -14)

# 등대 종 (가끔 등대에서 + 대표 선율) — 후보 5 중 3: 한 번 맑게 · 7.5초 긴 울림 · 도 (C4 −40 · C5 −15센트) → +0.28반음 올려 도에 맞춤.
# 시작 1.2 ms → 15 ms로 둥글게 (−45 dB에서 자르면 앞 잡음 뒤에 그대로 쳐서 −30 dB에서 자름). 4 = 라 (A4, 좋지만 계획이 "낮은 으뜸음"), 1 = 시 (B4), 2 = 도6 높음, 5 = 미♭6 + 1.4초 뒤에 침.
lbell = fade(soften(trim_lead(load(OD + "bell.wav"), -30)[: int(6.0 * SR)], 5000, 0.4), 0.015, 1.5)
lbell = pitch(highpass(lbell, 80), 0.28)
save("Interior/scape_defense_bell_01.wav", lbell, -14)
# 대표 선율 = 등대 종 세 음 솔 · 라 · 도 (한 박 간격, 마지막 도는 한 옥타브 아래 도를 겹쳐 묵직하게 — "낮은 으뜸음")
motif = np.zeros(int(8.0 * SR))
for i, (semis, gain) in enumerate(((-5, 0.85), (-3, 0.85), (0, 1.0))):
    motif = place(motif, pitch(lbell, semis) * gain, i * BEAT)
motif = place(motif, pitch(lbell, -12) * 0.5, 2 * BEAT)
save("Interior/scape_defense_motif.wav", fade(motif, 0.0, 1.2), -20)

# 별똥별 (밤, 바다 위 하늘 3D · 음계) — 후보 5 중 3의 첫 "휙" (바닥 −66 dB · 가장 깨끗) + 끝에 합성 반짝임 (도7 · 솔6 · 미7 짧은 방울).
# 다섯 모두 반짝임 없는 바람 소리뿐. 1 = 바닥 −41 dB, 2 = 5.8 kHz 쉿, 4 = 긴 꼬리, 5 = 바닥 −25 dB.
star = fade(highpass(sl(load(OD + "star.wav"), 0.0, 1.0), 300), 0.02, 0.25)
star = np.concatenate([star, np.zeros(int(0.6 * SR))])
st = np.arange(int(0.45 * SR)) / SR
for at, f0, amp in ((0.55, 2093.0, 0.5), (0.66, 1567.98, 0.4), (0.78, 2637.02, 0.35)):
    tone = np.exp(-st / 0.12) * (np.sin(2 * np.pi * f0 * st) + 0.15 * np.sin(2 * np.pi * 2 * f0 * st))
    star = place(star, tone * amp * np.abs(star).max(), at)
save("Interior/scape_defense_star_01.wav", fade(soften(star, 7000, 0.4), 0.0, 0.2), -14)

# 밤 오로라 울림 (밤에만, 2D) — 합성: 도4 · 솔4 · 레5 · 미5 (도 오음계)를 ±4센트로 겹쳐 서로 다른 느린 숨, 옅은 바람결 (1 ~ 4 kHz).
# 16초 = 정확히 24박 (음악 96초의 1/6), 주파수는 1/16 Hz 단위라 고리에 딱 맞음.
LA = 16.0
ta = np.arange(int(LA * SR)) / SR
aur = np.zeros_like(ta)
for j, (f0, amp) in enumerate(((261.63, 1.0), (392.0, 0.8), (587.33, 0.55), (659.25, 0.45))):
    for cents in (-4, 4):
        f = round(f0 * 2 ** (cents / 1200) * LA) / LA
        aur += amp / 2 * np.sin(2 * np.pi * f * ta + j) * (0.5 + 0.5 * np.sin(2 * np.pi * (j % 3 + 1) * ta / LA + 2.1 * j)) ** 2
rng = np.random.default_rng(7)
air = circular(lambda y: onepole_lp(highpass(y, 1000), 4000), rng.standard_normal(len(ta)))
air *= 0.5 + 0.5 * np.sin(2 * np.pi * 2 * ta / LA)
aur = circular(lambda y: onepole_lp(y, 2500), aur)
a_rms, n_rms = np.sqrt((aur ** 2).mean()), np.sqrt((air ** 2).mean())
save("Interior/scape_defense_aurora_loop.wav", 0.85 * aur / a_rms + 0.15 * air / n_rms, -26)

# ================= 3단계 소리 풍경 — 궤도 항만 "진주 조개 항구 · 인어 왕국" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/OrbitalPort.cs 값 (왼쪽 산호 섬 궁전 · 인어 꼬리 분수, 가운데 석호 · 해마 가리비 배 넷 · 해마 회전목마 · 선착장, 뒤 산호 방파제,
# 오른쪽 진주 잔교 · 큰 진주 조개). VARCO 하나씩 순서대로 7번 (선착장 2번). 바닷새 · 방파제 바깥 파도 = 궤도 방어망 소리, 회전목마 = 등대 렌즈 회전음을 같이 씀.
# 대표 선율 · 밤 "먼 노래 조각" = 하프 (Karplus-Strong 합성, 도 오음계 — VARCO 하프는 음이 음악과 안 맞음).
OP = "Interior/scape_port/"

# 바탕 — 잔잔한 석호 항구의 찰랑임 (2D). 후보 5 중 4 (가장 잔잔 — 날카로움 8.9 · 바닥 −37.8 dB) + 1 (조금 더 또렷한 찰랑)을 2초씩 겹쳐
# + 궤도 방어망 바닷바람을 옅게 (25 %) — 다섯 모두 바람결 없이 물소리뿐. 2 = 울림 4.7 (웅), 3 = 큰 찰랑이 띄엄띄엄, 5 = 꼬리가 큼.
lap = chain_loop([sl(load(OP + "bed_a.wav"), 0.3, 9.9), sl(load(OP + "bed_b.wav"), 0.3, 9.9)], 2.0)
wind = load(OD + "bed_a.wav")
wind = np.tile(wind, int(np.ceil(len(lap) / len(wind))) + 1)[: len(lap)]
lap = circular(lambda y: soften(highpass(y, 50), 5000, 0.35), lap)
wind = circular(lambda y: highpass(y, 40), wind)
l_rms, w_rms = np.sqrt((lap ** 2).mean()), np.sqrt((wind ** 2).mean())
save("Interior/scape_port_bed_loop.wav", 0.75 * lap / l_rms + 0.25 * wind / w_rms, -24)

# 인어 꼬리 분수 (마당 가운데, 3D) — 후보 5 중 5: 5.2초 내내 보글보글 고르게 쏟아짐. 2 = 9.5 kHz 쉿, 1 · 3 · 4 = 중간에 끊김.
save("Interior/scape_port_fountain_loop.wav", circular(lambda y: soften(highpass(y, 80), 5000, 0.45), loop_exact(sl(load(OP + "fountain.wav"), 0.1, 5.15), 4.2)), -24)

# 선착장 · 진주 잔교 나무 · 밧줄 삐걱임 (3D 둘) — 첫 5개 = 낮은 웅 띠 위에 삐걱임이 희미함 → "배경 없는 가까운 녹음"으로 다시 5개 중 5:
# 바닥 −48 dB · 울림 0, 삐걱임이 9초 동안 띄엄띄엄. 2 · 3 = 6초로 짧음, 1 = 바닥 −41, 4 = 바닥 −33.
# 그대로는 115 Hz 봉우리 · 80 ~ 320 Hz가 대부분이라 귀 감도로 rms보다 20 dB 작음 (녹음 검수에서 음악 −51, 거의 안 들림) → 5반음 올려 (6.1초 루프)
# 작은 나무 삐걱 결로 · 150 Hz 아래를 걸러 · 원본을 −18로 (다른 루프와 같은 음량 범위에서 쓰게).
save("Interior/scape_port_dock_loop.wav", circular(lambda y: soften(highpass(highpass(y, 150), 150), 4000, 0.4), pitch(loop_exact(sl(load(OP + "dock_creak.wav"), 0.0, 9.1), 8.2), 5)), -18)

# 해마 가리비 배 물살 (배 넷을 따라 돎, 3D) — 후보 5 중 4: 5.8초 내내 가장 고름 · 울림 0. 5 = 가운데 한 번 크게 부풂, 3 = 2.2초에 낮은 음 줄, 1 = 밝고 튐.
save("Interior/scape_port_wake_loop.wav", circular(lambda y: soften(highpass(y, 60), 5000, 0.4), loop_exact(flatten(sl(load(OP + "wake.wav"), 0.2, 5.7)), 4.6)), -24)

# 석호 수련 사이 물방울 "퐁" (가끔, 3D) — 후보 5 중 3의 네 방울 (배경 디지털 무음 · 위로 미끄러지는 귀여운 퐁).
# 1 = 쇳소리 울림 72, 4 = 날카로운 딸깍 (5.2 kHz), 2 · 5 = 어수선. 석호에 깔리는 물소리는 바탕 · 배 물살 · 방파제 파도로.
pk = load(OP + "plink.wav")
pp = phrases(pk, gap=0.08, pad=0.03, rise_db=20)
for k, (a, b) in enumerate(pp[:4]):
    save(f"Interior/scape_port_plink_{k + 1:02d}.wav", fade(highpass(pk[a:b], 200), 0.005, 0.06), -14)
report.append(f"  수련 물방울 {len(pp)}개")

# 물고기 퐁당 (가끔, 석호 3D) — 후보 5 중 3 (둥글게 한 번 퐁당 · 1.5 kHz · 날카로움 9.4) + 4의 앞 퐁당. 1 · 2 · 5 = 밝고 튐 (2.9 ~ 4.2 kHz · 날카로움 15 ~ 19).
save("Interior/scape_port_fish_01.wav", fade(highpass(trim_lead(load(OP + "fish_a.wav"), -45), 120), 0.01, 0.3), -14)
save("Interior/scape_port_fish_02.wav", fade(highpass(sl(trim_lead(load(OP + "fish_b.wav"), -45), 0.0, 0.85), 120), 0.01, 0.25), -14)


def harp(f, dur=3.0, seed=0):
    """하프 한 음 (Karplus-Strong — 정수 지연으로 만든 뒤 재생 속도로 정확한 높이에 맞춤, 손끝으로 뜯은 듯 부드러운 첫 결)."""
    rng = np.random.default_rng(seed)
    n = int(SR / f)
    buf = onepole_lp(rng.standard_normal(n), 2500) * 0.8
    out = np.zeros(int(dur * SR * 1.05))
    for i in range(len(out)):
        j = i % n
        out[i] = buf[j]
        buf[j] = 0.4985 * (buf[j] + buf[(j + 1) % n])
    out = onepole_lp(out, 4000)
    out = pitch(out, 12 * np.log2(f / (SR / n)))[: int(dur * SR)]
    return fade(out, 0.004, 0.6)


def distant(x, mix=0.35, seed=3):
    """멀리서 들리게 — 짧은 잔향 (0.9초 지수 꼬리 잡음) + 3 kHz 위를 누름."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(0.9 * SR)) / SR
    ir = rng.standard_normal(len(t)) * np.exp(-t / 0.25)
    wet = np.convolve(x, ir)[: len(x)]
    wet *= np.sqrt((x ** 2).mean()) / (np.sqrt((wet ** 2).mean()) + 1e-12)
    return onepole_lp((1 - mix) * x + mix * wet, 3000)


C4, E4, G4, A4, C5, E5 = 261.63, 329.63, 392.0, 440.0, 523.25, 659.25
# 대표 선율 = 하프로 도 · 미 · 솔 · 라 · 도 · 미 (반의 반 박씩 — 손가락으로 쓸어 올리는 물결)
motif = np.zeros(int(5.5 * SR))
for i, f in enumerate((C4, E4, G4, A4, C5, E5)):
    motif = place(motif, harp(f, 3.5, i) * (0.8 + 0.04 * i), i * BEAT / 4)
save("Interior/scape_port_motif.wav", fade(motif, 0.0, 1.0), -20)
# 밤 "먼 노래 조각" (궁전 쪽, 가끔 3D · 박에 맞춤) — 하프 세 음 소절 셋 (솔 · 미 · 도 / 라 · 솔 · 미 / 미 · 솔 · 라), 한 박 간격, 멀리서 들리게.
for k, notes in enumerate(((G4, E4, C4), (A4, G4, E4), (E4, G4, A4))):
    ph = np.zeros(int(4.5 * SR))
    for i, f in enumerate(notes):
        ph = place(ph, harp(f, 3.0, 10 + 3 * k + i) * (0.9 if i < 2 else 1.0), i * BEAT)
    save(f"Interior/scape_port_song_{k + 1:02d}.wav", fade(distant(ph), 0.0, 1.0), -14)

# ================= 3단계 소리 풍경 — 수리 사령부 "반딧불 고목 치유소" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/RepairCommand.cs 값 (뒤 이끼 언덕 위 진주 고목 · 가지 위 나무집 관제실 (지붕 금빛 경종) · 도르래 구조 바구니 · 나무 구멍 공방 둘 ·
# 샘 → 물길 → 연못, 앞 이끼 숲 빈터 · 그루터기 앞 기억 등불 다섯). 늘 밤 숲이라 낮 · 밤 구분 없음. VARCO 하나씩 순서대로 5번 (고목 2번).
# 샘 · 물길 = 바이오스피어 물길, 개구리 = 바이오스피어, 공방 드론 = AI 중추 드론 그르릉, 줄기 · 나무집 · 도르래 삐걱 = 궤도 항만 삐걱임,
# 관측대 바람 = 궤도 도시 옥상 바람을 같이 씀. 반딧불 반짝임은 음악 조 (F 오음계 — D 단조 · F 장조 곡과 같은 음)에 맞춰 합성.
RC = "Interior/scape_repair/"

# 바탕 — 밤 숲 빈터 (2D). 후보 5 중 2: 끊겨 우는 풀벌레 (4.8 kHz · 초당 1.8번) + 옅은 숲 공기 · 바닥 −19 dB (가장 깨끗) → 4.5 kHz 위를 눌러 숲 너머로.
# 1 = 한 높이로 끝없는 "삐—" 줄, 3 = 웅 (tonal 5.3), 4 = 6.3초로 짧음 · 낮은 웅, 5 = 바닥 −4.6 dB · 울림 6.
save("Interior/scape_repair_bed_loop.wav", circular(lambda y: soften(soften(highpass(y, 150), 4500, 0.5), 8000, 0.4), loop_exact(sl(load(RC + "bed.wav"), 0.2, 10.24), 9.0)), -26)

# 고목 잎 사각임 (잎 구름 아래, 3D) — 첫 5개 ("큰 고목 삐걱")는 모두 61 ~ 129 Hz 낮은 웅뿐 (300 Hz 아래 99 ~ 100 % · 잎 소리 없음, 귀 감도로 안 들림) →
# "잎 구름 사각임 · 가끔 작은 나무 삐걱"으로 다시 5개 중 3 (가장 길고 고름) · 4 (일었다 잦아드는 바람, 바닥 −34 dB) · 5 (가장 어두움 5.6 kHz)를
# 1.5초씩 겹쳐 고리. 원래 밝은 잎 소리 (6 kHz 대)라 4.5 kHz 위를 눌러 둥글게. 1 · 2 = 바닥 −21 ~ −27 dB. 줄기 삐걱은 궤도 항만 삐걱임을 낮춰 씀.
leaves = chain_loop([sl(load(RC + "leaf_a.wav"), 0.8, 6.3), sl(load(RC + "leaf_c.wav"), 0.5, 5.9), sl(load(RC + "leaf_b.wav"), 0.6, 5.0)], 1.5)
# 이은 그대로는 0.5초 크기가 −57 ~ −30 (27 dB — 잦아들 땐 사라지고 부풀면 튐) → 느린 크기 변화를 절반만 펴서 (13 dB 안팎) 일었다 잦아드는 결은 남김.
le = circular(lambda y: np.sqrt(np.convolve(y ** 2, np.ones(int(1.0 * SR)) / int(1.0 * SR), "same")) + 1e-9, leaves)
leaves = leaves * np.sqrt(np.median(le) / le)
save("Interior/scape_repair_leaves_loop.wav", circular(lambda y: soften(soften(highpass(y, 200), 4500, 0.5), 8000, 0.4), leaves), -24)
report.append(f"  고목 잎 고리 {len(leaves) / SR:.2f}초")

# 부엉이 (가지 위 여기저기, 가끔 3D) — 후보 5 중 1: 맑은 "후—" 둘 · 배경 디지털 무음 · 700 Hz (F5 +3센트 — 음악 조 안).
# 둘째 "후"를 1.1초 뒤로 당겨 "후, 후" 한 마디 + 둘째만 한 번. 3 · 4 = 재잘거림 (지직 49 ~ 61), 5 = 음이 흔들림, 2 = 0.6초 한 번.
ow = load(RC + "owl.wav")
h1, h2 = fade(sl(ow, 0.04, 0.98), 0.01, 0.15), fade(sl(ow, 2.70, 3.45), 0.01, 0.15)
save("Interior/scape_repair_owl_01.wav", highpass(place(np.zeros(int(2.0 * SR)), h1, 0.0) + place(np.zeros(int(2.0 * SR)), h2, 1.1)[: int(2.0 * SR)], 200), -14)
save("Interior/scape_repair_owl_02.wav", highpass(h2, 200), -14)

# 금빛 경종 (대표 선율 · 체험 "경종 울리기"에 씀) — 후보 5 중 3: 한 번만 맑게 · 950 · 1900 Hz 옥타브 배음 (B♭5 +32센트).
# VARCO "작은 금빛 종"은 손종 높이 → 17.3반음 내려 정확한 F4 (349.2 Hz, 2.7배 길게 — 나무집 종각의 큰 종).
# 내리기 전에 6 kHz 위 치는 잡음을 걸러 (내리면 2 kHz 대 "칙"이 됨), 첫 울림을 눌러 긴 울림이 남게. 1 · 5 = 0.3초 뒤 한 번 더 침, 2 = 배음이 C# · A · D, 4 = 음 중심이 G와 G# 사이.
gb = trim_lead(load(RC + "bell.wav"), -30)[: int(4.0 * SR)]
gb = pitch(onepole_lp(onepole_lp(gb, 6000), 6000), 12 * np.log2(349.23 / 950.0))
gb = fade(tame(highpass(gb, 80), 8.0), 0.015, 2.5)
save("Interior/scape_repair_bell_01.wav", gb, -14)


def twinkle(f, dur=0.9, seed=0):
    """반딧불 반짝 — 맑은 사인 (기음 + 2배 · 3배 옅게) · 9 Hz 반짝 떨림 · 빨리 켜지고 천천히 꺼짐."""
    t = np.arange(int(dur * SR)) / SR
    env = (1 - np.exp(-t / 0.006)) * np.exp(-t / 0.28)
    trem = 0.75 + 0.25 * np.sin(2 * np.pi * 9.0 * t + seed)
    x = np.sin(2 * np.pi * f * t) + 0.22 * np.sin(2 * np.pi * 2 * f * t + 0.7) + 0.06 * np.sin(2 * np.pi * 3.01 * f * t + 1.9)
    return fade(x * env * trem, 0.0, 0.1)


C6 = 1046.5
# 반딧불 반짝 (기억 등불 · 잎 구름 둘레, 가끔 3D · 음계 높이 · 다음 박) — 도6 하나 + 같은 음 두 번 (두 번째 옅게).
save("Interior/scape_repair_firefly_01.wav", twinkle(C6), -14)
save("Interior/scape_repair_firefly_02.wav", place(twinkle(C6, seed=1), twinkle(C6, seed=2) * 0.55, 0.16), -14)
# 대표 선율 = 경종 F4 한 번 → 반딧불이 등불을 켜 올라감 라5 · 도6 · 레6 · 파6 (한 박 뒤부터 반 박씩) — "경종을 울리면 반딧불이 등불을 켬"
motif = np.zeros(int(9.0 * SR))
motif = place(motif, gb, 0.0)
for i, semis in enumerate((-3, 0, 2, 5)):
    motif = place(motif, twinkle(C6 * 2 ** (semis / 12), 1.2, 3 + i) * 0.45 * np.abs(gb).max(), BEAT + i * BEAT / 2)
save("Interior/scape_repair_motif.wav", fade(motif, 0.0, 1.5), -20)

# ================= 3단계 소리 풍경 — 항성 발전망 "꿀벌 정원" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/StellarGrid.cs 값 (왼쪽 꽃나무 숲 · 등나무 꽃 정자 (그네 · 수정 꽃송이 종) · 해바라기 · 꿀벌 물터 연못 · 전망대 · 꿀 샘 그루터기 (곰) ·
# 꿀 언덕 속 벌집 홀 · 라벤더 이랑, 꿀벌 = 고리 길을 나는 벌 12 · 맴도는 벌 5). VARCO 하나씩 순서대로 4번.
# 정자 등나무 · 꽃나무 숲 잎 = 수리 사령부 잎, 밤 풀벌레 = 수리 사령부 바탕, 연못 물방울 = 궤도 항만 수련 물방울을 같이 씀.
# 꿀벌 · 벌집 · 곰 소리는 음이 있어 음악 조 (D 단조 · F 장조) 안의 음으로 맞추고, 수정 꽃 종은 F 오음계로 합성.
SG = "Interior/scape_stellar/"

# 바탕 — 햇살 꽃밭 바람 (2D). 후보 5 중 5 (가장 어둡고 고름 · 지직 5.5) + 3 (키 큰 풀이 흔들리는 밝은 결 · 지직 6.8)을 2초씩 겹쳐 고리.
# 2 · 4 = 6.1 · 3.7초로 짧음, 1 = 지직 9.6.
bed = chain_loop([sl(load(SG + "bed_a.wav"), 0.3, 9.9), sl(load(SG + "bed_b.wav"), 0.3, 9.6)], 2.0)
save("Interior/scape_stellar_bed_loop.wav", circular(lambda y: soften(highpass(y, 60), 6000, 0.4), bed), -24)
report.append(f"  꽃밭 바탕 고리 {len(bed) / SR:.2f}초")

# 꿀벌 한 마리 지나감 (낮, 고리 길 · 꽃나무 숲 · 라벤더 둘레 가끔 3D) — "제자리에서 맴도는 벌" 루프를 불렀는데 다섯 모두 1.8 ~ 3.9초 "다가왔다 멀어지는" 소리 →
# 그대로 지나가는 벌로. 1 (가장 매끈 · B♭2 +22센트 — 조 안, 그대로) + 5 (F3 −45센트 → +0.45반음 정확한 F3, 지직 12.7 최저).
# 기음이 낮아 (115 ~ 175 Hz) 귀 감도로 작음 → 150 Hz 아래를 눌러 날개 결 (배음)이 들리게. 2 = 쉿 (4 kHz 위 31 %), 3 = 1.8초, 4 = 지직 21.7.
save("Interior/scape_stellar_bee_01.wav", fade(highpass(highpass(trim_lead(load(SG + "bee_a.wav"), -45), 150), 150), 0.05, 0.3), -14)
save("Interior/scape_stellar_bee_02.wav", fade(highpass(highpass(pitch(trim_lead(load(SG + "bee_b.wav"), -45), 0.45), 150), 150), 0.05, 0.3), -14)

# 벌집 홀 웅웅 (꿀 언덕 속 · 둥근 벌집 입구, 3D) — 후보 5 중 3: 가장 고르게 이어짐 (지직 9.5 최저) · 벌 떼 화음 가운데 403 Hz (G4와 G#4 사이) → −0.5반음 G4.
# 1 · 2 · 5 = 크게 부풀었다 가라앉음, 4 = 무늬가 일정하지 않음.
hive = loop_exact(pitch(sl(load(SG + "hive.wav"), 0.3, 8.56), -0.5), 7.0)
save("Interior/scape_stellar_hive_loop.wav", circular(lambda y: soften(highpass(y, 120), 5000, 0.4), hive), -24)

# 곰 주민 "음~" (낮, 꿀 샘 그루터기 가끔 3D) — 후보 5 중 1: 배경 −50 dB · 2초 매끈한 "음~" (꿀맛) + 뒤에 짧은 "음음".
# 긴 "음~" = F#4 −15센트 → −0.85반음 F4, 뒤 "음음" = G4 +4센트 (그대로). 3 = 씹는 소리, 4 = 딸깍 (지직 59), 2 = 날 선 시작 (2.9 ms), 5 = 지직 28.
br = load(SG + "bear.wav")
save("Interior/scape_stellar_bear_01.wav", fade(highpass(pitch(sl(br, 0.05, 2.3), -0.85), 120), 0.02, 0.25), -14)
save("Interior/scape_stellar_bear_02.wav", fade(highpass(sl(br, 3.92, 5.3), 120), 0.02, 0.15), -14)


def flower_bell(f, dur=2.4, seed=0):
    """수정 꽃송이 종 — 맑은 유리 종 (기음 + 2배 · 2.76배 · 5.4배 옅게, 높은 배음부터 꺼짐) · 부드럽게 치고 길게 남음."""
    t = np.arange(int(dur * SR)) / SR
    x = (np.sin(2 * np.pi * f * t) * np.exp(-t / 0.9) + 0.25 * np.sin(2 * np.pi * 2 * f * t + 0.4 + seed) * np.exp(-t / 0.6)
         + 0.3 * np.sin(2 * np.pi * 2.76 * f * t + 1.1 + seed) * np.exp(-t / 0.35) + 0.1 * np.sin(2 * np.pi * 5.4 * f * t + 2.3) * np.exp(-t / 0.15))
    return fade(onepole_lp(x * (1 - np.exp(-t / 0.003)), 6000), 0.0, 0.4)


# 수정 꽃 종 (정자 꽃송이, 가끔 3D · 음계 높이 · 다음 박 — 밤에도) — 도6 하나.
save("Interior/scape_stellar_flowerbell_01.wav", flower_bell(C6), -14)
# 대표 선율 = 꽃 그네가 굴러 오르듯 파5 · 라5 · 도6 · 레6 · 파6 (반 박씩, 마지막 길게)
motif = np.zeros(int(5.5 * SR))
for i, semis in enumerate((-7, -3, 0, 2, 5)):
    motif = place(motif, flower_bell(C6 * 2 ** (semis / 12), 3.0 if i == 4 else 2.4, i) * (0.8 + 0.05 * i), i * BEAT / 2)
save("Interior/scape_stellar_motif.wav", fade(motif, 0.0, 1.0), -20)

# ================= 4단계 소리 풍경 — 심우주 통신기지 "은하수 천문 언덕" (SOUND_LIST_NEW D-7b) =================
# 자리는 Interior/DeepSpaceComm.cs 값 + 템플릿 강 메시 (S자 은하수 강: 왼쪽 앞 별 샘 (−3.4, −2.3) → 뒤 가운데 (8, 2.0) → 오른쪽 앞 별 샘 (19.4, −2.4),
# 돔 셋 (가운데 관측실 · 옆 중계소 둘) · 뒤 전파 접시 넷 · 앞 별똥별 아치 · 별자리 광장). 늘 밤. VARCO 1번 (바탕).
# 은하수 강 = 궤도 도시 고리 강, 별 샘 = 바이오스피어 물길, 중계소 · 관측실 웅웅 = 궤도 방어망 수정 포대 (도 오음계 반짝임), 풀벌레 = 수리 사령부 바탕,
# 부엉이 = 수리 사령부 부엉이 (같은 부엉이 모델), 별똥별 = 궤도 방어망 별똥별, 강 위 유리 별 = 항성 발전망 수정 꽃 종을 한 옥타브 위로.
# 신호 "삐" · 은하수 깊은 울림 · 대표 선율 (신호 → 유리 별)은 음악 조 (F 장조)에 맞춰 합성.
DS = "Interior/scape_deep/"

# 바탕 — 별 아래 풀 언덕 밤바람 (2D). 후보 5 중 2 (고른 풀바람 · 지직 3.8) + 5 (비슷한 밝기 665 Hz)를 2초씩 겹쳐 고리.
# 1 · 3 = 아주 어두움 (277 · 312 Hz, 300 Hz 아래 84 ~ 86 % — 귀 감도로 작음), 4 = 4.7초 · 5.4초에 작은 딸깍.
bed = chain_loop([sl(load(DS + "bed_a.wav"), 0.3, 10.0), sl(load(DS + "bed_b.wav"), 0.3, 10.0)], 2.0)
save("Interior/scape_deep_bed_loop.wav", circular(lambda y: soften(highpass(y, 60), 6000, 0.4), bed), -24)
report.append(f"  통신기지 바탕 고리 {len(bed) / SR:.2f}초")

# 은하수 깊은 울림 (2D, 은하수 아래 깊은 울림) — 합성: 파2 · 도3 · 파3 · 라3 · 도4 · 파4 · 라4 (F 장조 으뜸화음)를 ±3센트로 겹쳐 서로 다른 느린 숨 +
# 옅은 별빛 라5 · 도6 (처음엔 도4까지만 — 300 Hz 아래 100 %라 귀 감도로 거의 안 들려 위 음을 더함). 16초 = 정확히 24박, 주파수는 1/16 Hz 단위라 고리에 딱 맞음 (궤도 방어망 오로라와 같은 방식, 더 낮고 깊게).
LM = 16.0
tm = np.arange(int(LM * SR)) / SR
mw = np.zeros_like(tm)
for j, (f0, amp) in enumerate(((87.31, 1.0), (130.81, 0.8), (174.61, 0.6), (220.0, 0.5), (261.63, 0.45), (349.23, 0.3), (440.0, 0.2), (880.0, 0.08), (1046.5, 0.06))):
    for cents in (-3, 3):
        f = round(f0 * 2 ** (cents / 1200) * LM) / LM
        mw += amp / 2 * np.sin(2 * np.pi * f * tm + j) * (0.55 + 0.45 * np.sin(2 * np.pi * (j % 3 + 1) * tm / LM + 1.7 * j)) ** 2
save("Interior/scape_deep_milkyway_loop.wav", circular(lambda y: onepole_lp(highpass(y, 40), 2500), mw), -26)


def signal_beep(f, seed=0):
    """전파 신호 "삐 · 빕" — 맑은 사인 (3배 옅게 — 무전기 결) 두 번 (0.13초 · 0.09초, 사이 0.07초), 부드러운 시작 · 끝 + 아주 옅은 무전 잡음."""
    def tone(d):
        t = np.arange(int(d * SR)) / SR
        return (np.sin(2 * np.pi * f * t) + 0.12 * np.sin(2 * np.pi * 3 * f * t)) * np.minimum(1, t / 0.008) * np.minimum(1, (d - t) / 0.03)
    out = place(place(np.zeros(int(0.6 * SR)), tone(0.13), 0.0), 0.8 * tone(0.09), 0.2)
    rng = np.random.default_rng(seed)
    hiss = onepole_lp(highpass(rng.standard_normal(len(out)), 1200), 4000)
    hiss *= np.exp(-np.arange(len(out)) / SR / 0.25) * 0.06 / (np.abs(hiss).max() + 1e-9)   # 소리 최대의 6 % (−24 dB)
    return fade(out + hiss * np.abs(out).max(), 0.0, 0.05)


# 전파 접시 신호 "삐 · 빕" (뒤 능선 접시 넷 둘레, 가끔 3D · 음계 높이 · 다음 박) — 도6.
save("Interior/scape_deep_signal_01.wav", signal_beep(C6), -14)
# 대표 선율 = 신호 "삐 · 빕" 레6 두 번 → 유리 별이 반짝이며 파5 · 라5 · 도6 · 레6 · 파6 (반의 반 박씩, 마지막 길게) — "먼 별에서 온 신호"
motif = np.zeros(int(6.0 * SR))
for i in range(2):
    motif = place(motif, signal_beep(C6 * 2 ** (2 / 12), 5 + i) * 0.7, i * BEAT / 2)
for i, semis in enumerate((-7, -3, 0, 2, 5)):
    motif = place(motif, flower_bell(C6 * 2 ** (semis / 12), 3.0 if i == 4 else 2.2, 7 + i) * (0.75 + 0.05 * i), BEAT + i * BEAT / 4)
save("Interior/scape_deep_motif.wav", fade(motif, 0.0, 1.0), -20)

print("\n".join(report))
