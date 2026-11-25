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
    """짧은 구간(50 ms) 최대 RMS를 target_db로 맞추고, 피크는 peak_db를 넘지 않게 (1차와 같은 기준).
    루프 (`_loop.wav`)는 먼저 고음 상한 검사 (`match_tilt`, 2026-11-23 배경 쉬— 정리 — 넘는 것만 낮춤, 예외는 `TILT`)."""
    if rel.endswith("_loop.wav") and TILT.get(rel, {}) is not None:
        x, note = match_tilt(x, **TILT.get(rel, {}))
    else:
        note = ""
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
    report.append(f"{rel:36s} {len(y) / SR:5.2f}s  peak {20 * np.log10(max(np.abs(y).max(), 1e-9)):6.1f} dB{note}")


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


# 2026-11-23 사용자 "배경에 잡음 느낌의 소리가 섞인다 — 깔끔하게 어울리는 게 더 중요" → 내부 음악 4곡은 1 kHz 위로 급히 어두워지는데
# (몸통 띠 기준 4k −30 · 8k −38 · 16k −38 dB) 물 · 비 · 바탕 루프 29개는 8 kHz까지 거의 평평 (−1 ~ −7) — 어두운 음악 위의 고른 쉬—가 잡음으로 들림.
# soften (원본 65 % + 로우패스)은 고음을 3 dB 남짓밖에 못 줄였음. 상한 = 몸통 띠 (250 ~ 2k 중 가장 큰 옥타브) 기준 4k −10 · 8k −22 · 16k −34 dB.
TILT_CEIL = ((4000, -10.0), (8000, -22.0), (16000, -34.0))
TILT = {
    "Interior/scape_bio_crickets_loop.wav": None,                                  # 4.8 kHz 풀벌레가 소리 자체
    "Interior/scape_repair_bed_loop.wav": dict(ceil=TILT_CEIL[1:], start=5600.0),  # 풀벌레 4.8 kHz는 두고 그 위 숲 공기만
    "Interior/scape_repair_leaves_loop.wav": dict(ceil=((4000, -6.0), (8000, -18.0), (16000, -30.0))),  # 잎 사각임 결은 남김
    "Ride/dsc_static_loop.wav": dict(ceil=((4000, -6.0), (8000, -16.0), (16000, -28.0))),               # 신호 맞추기 잡음 = 놀이 단서
    "Event/solar_storm_loop.wav": dict(ceil=((4000, -6.0), (8000, -16.0), (16000, -28.0))),             # 바깥 태양 폭풍 지직 (일부러)
}


def match_tilt(x, ceil=TILT_CEIL, start=2000.0):
    """고음 옥타브 (4k · 8k · 16k)가 몸통 띠 + 상한을 넘는 만큼만 고정 EQ로 낮춤 — 루프 전체를 한 번에 (원형 FFT라 이음새 그대로).
    낮추는 양은 start Hz = 0에서 log 주파수로 이어 높을수록 같거나 커지게 (부드러운 로우패스 모양). 넘는 띠가 없으면 그대로 돌려줌."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    P = np.abs(X) ** 2
    band = lambda b: 10 * np.log10(P[(f >= b / np.sqrt(2)) & (f < min(b * np.sqrt(2), SR / 2))].sum() + 1e-20)
    body = max(band(b) for b in (250, 500, 1000, 2000))
    cut = list(np.maximum.accumulate([0.0] + [max(0.0, band(fc) - (body + c)) for fc, c in ceil]))
    if max(cut) < 0.5:
        return x, ""
    a = np.interp(np.log2(np.maximum(f, 1.0)), np.log2([start] + [fc for fc, _ in ceil]), cut, left=0.0, right=cut[-1])
    return np.fft.irfft(X * 10 ** (-a / 20), len(x)), f"  고음 상한 ({', '.join(f'{fc // 1000}k -{v:.0f}' for (fc, _), v in zip(ceil, cut[1:]))} dB)"


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
# → 2026-11-24 방 환경음 6 (amb_room_*)은 실제 녹음으로 다시 만듦 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 막은 줄은 기록 (1차 보관본 legacy/는 그대로 둠)
lp2 = lambda fc: (lambda y: onepole_lp(onepole_lp(y, fc), fc))
# save("Interior/amb_room_electric_loop.wav", circular(lp2(1200), load(L + "amb_room_electric_loop.wav")), -24)
# save("Interior/amb_room_heat_loop.wav", circular(lp2(1200), load(L + "amb_room_heat_loop.wav")), -22)
# 기계 방: 1.75초마다 같은 "칙·틱"이 또렷하게 반복 → 고음을 눌러 멀리서 나는 소리로
# save("Interior/amb_room_machine_loop.wav", circular(lp2(2000), load(L + "amb_room_machine_loop.wav")), -24)
# 2026-11-23 배경 쉬— 정리: 생활 · 물 · 복도 방도 1차 그대로는 8 kHz −7 ~ −18 (1극 로우패스뿐) → 보관본에서 다시 만들며 save의 고음 상한을 거침.
# 바깥 태양 폭풍 지직도 8 · 16 kHz가 몸통보다 +4 dB → 지직 결은 남기고 덜 낮춤 (TILT).
# for name, db in (("amb_room_life_loop", -26), ("amb_room_water_loop", -24), ("amb_room_hall_loop", -26)):
#     save(f"Interior/{name}.wav", load(L + f"{name}.wav"), db)
save("Event/solar_storm_loop.wav", load("Event/legacy/solar_storm_loop.wav"), -14)


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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(A + "bed_a.wav"), 0.3, 9.9), sl(load(A + "bed_b.wav"), 0.3, 9.9)], 2.0)
# save("Interior/scape_aicore_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(B + "bed_a.wav"), 0.3, 9.9), sl(load(B + "bed_b.wav"), 0.3, 9.9)], 2.0)
# save("Interior/scape_bio_bed_loop.wav", circular(lambda y: soften(highpass(y, 40), 6000, 0.4), bed), -24)

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
    if k in (2, 3):   # → 2026-11-25 소절 묶음으로 바꿈 (맨 아래 "실제 녹음 C — 생물 · 날씨" 구역 — 소절 하나씩은 짧아 Fowl 0.14 · Crow 0.20으로 들림)
        continue
    save(f"Interior/scape_bio_frog_{k:02d}.wav", fade(highpass(x[a:b], 150), 0.01, 0.08), -24)

# 밤 풀벌레 (밤에만, 2D 층) — 후보 5 중 1: 쉬엄쉬엄 끊겨 우는 귀뚜라미. 3 · 4 · 5 = 한 높이로 끝없이 이어지는 "삐—" 줄, 2 = 바닥 −6.5 dB.
save("Interior/scape_bio_crickets_loop.wav", circular(lambda y: soften(highpass(y, 300), 8000, 0.4), loop_exact(sl(load(B + "crickets.wav"), 0.2, 10.24), 9.0)), -30)

# 먼 천둥 (밤에 아주 가끔, 돔 위 멀리) — 후보 5 중 2: 울림 줄 4 (나머지 14 ~ 51 = 낮은 "웅") · 바닥 −42.8 dB.
# → 2026-11-25 실제 녹음 (멀리서 굴러가는 천둥)으로 바꿈 (맨 아래 "실제 녹음 C — 생물 · 날씨" 구역 — 이것은 Heart murmur 0.17로 들림) — 아래 줄은 기록
# save("Interior/scape_bio_thunder_01.wav", fade(highpass(trim_lead(load(B + "thunder.wav"), -45), 30), 0.2, 1.5), -22)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(OC + "bed_a.wav"), 0.3, 7.8), sl(load(OC + "bed_b.wav"), 0.3, 7.8)], 2.0)
# save("Interior/scape_city_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# save("Interior/scape_city_night_loop.wav", circular(lambda y: highpass(y, 30), loop_exact(sl(load(OC + "night_glow.wav"), 0.3, 10.24), 9.0)), -26)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(OD + "bed_a.wav"), 0.3, 9.3), sl(load(OD + "bed_b.wav"), 0.3, 6.8)], 2.0)
# save("Interior/scape_defense_bed_loop.wav", circular(lambda y: highpass(y, 40), bed), -24)

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
# → 2026-11-25 실제 녹음 (갈매기 무리)으로 바꿈 (맨 아래 "실제 녹음 C — 생물 · 날씨" 구역 — 이 셋은 Owl 0.21 · 애완동물 0.48 · 거위 Honk 0.32로 들림) — 아래 줄은 기록
# g = load(OD + "gull.wav")
# ph = phrases(g, gap=0.12, pad=0.05)
# half = max(1, len(ph) // 2)
# for k, (a, b) in enumerate([(ph[0][0], ph[half - 1][1]), (ph[half][0], ph[-1][1])] if len(ph) > 1 else [ph[0]]):
#     save(f"Interior/scape_defense_gull_{k + 1:02d}.wav", fade(highpass(g[a:b], 300), 0.01, 0.08), -14)
# report.append(f"  갈매기 소절 {len(ph)}개")
# pf = load(OD + "puffin.wav")
# save("Interior/scape_defense_gull_03.wav", fade(highpass(trim_lead(pf, -45), 300), 0.01, 0.08), -14)

# 물범 (낮, 방파제 끝 바위 3D) — 후보 5 모두 딸깍 · 지직이 섞임 (지직 44 ~ 57) → 3의 앞 두 번 "멍멍" (0.05 ~ 0.8초)만.
# → 2026-11-25 실제 녹음 바탕으로 바꿈 (맨 아래 "실제 녹음 C — 생물 · 날씨" 구역 — 이것은 Oink 0.06 · Music 0.06, 무엇인지 안 들림) — 아래 줄은 기록
# seal = sl(load(OD + "seal.wav"), 0.05, 0.8)   # 0.8초 뒤 = 작은 딸깍 꼬리 (다듬은 뒤 그림으로 확인)
# save("Interior/scape_defense_seal_01.wav", fade(soften(highpass(seal, 100), 4500, 0.5), 0.01, 0.12), -14)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# lap = chain_loop([sl(load(OP + "bed_a.wav"), 0.3, 9.9), sl(load(OP + "bed_b.wav"), 0.3, 9.9)], 2.0)
# wind = load(OD + "bed_a.wav")
# wind = np.tile(wind, int(np.ceil(len(lap) / len(wind))) + 1)[: len(lap)]
# lap = circular(lambda y: soften(highpass(y, 50), 5000, 0.35), lap)
# wind = circular(lambda y: highpass(y, 40), wind)
# l_rms, w_rms = np.sqrt((lap ** 2).mean()), np.sqrt((wind ** 2).mean())
# save("Interior/scape_port_bed_loop.wav", 0.75 * lap / l_rms + 0.25 * wind / w_rms, -24)

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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(SG + "bed_a.wav"), 0.3, 9.9), sl(load(SG + "bed_b.wav"), 0.3, 9.6)], 2.0)
# save("Interior/scape_stellar_bed_loop.wav", circular(lambda y: soften(highpass(y, 60), 6000, 0.4), bed), -24)
# report.append(f"  꽃밭 바탕 고리 {len(bed) / SR:.2f}초")

# 꿀벌 한 마리 지나감 (낮, 고리 길 · 꽃나무 숲 · 라벤더 둘레 가끔 3D) — "제자리에서 맴도는 벌" 루프를 불렀는데 다섯 모두 1.8 ~ 3.9초 "다가왔다 멀어지는" 소리 →
# 그대로 지나가는 벌로. 1 (가장 매끈 · B♭2 +22센트 — 조 안, 그대로) + 5 (F3 −45센트 → +0.45반음 정확한 F3, 지직 12.7 최저).
# 기음이 낮아 (115 ~ 175 Hz) 귀 감도로 작음 → 150 Hz 아래를 눌러 날개 결 (배음)이 들리게. 2 = 쉿 (4 kHz 위 31 %), 3 = 1.8초, 4 = 지직 21.7.
# → 2026-11-25 1은 실제 녹음 (꿀벌)으로 바꿈 (맨 아래 "실제 녹음 C — 생물 · 날씨" 구역 — 1은 Music 0.50 · Didgeridoo로 들림, 2는 Insect 0.90 그대로) — 아래 줄은 기록
# save("Interior/scape_stellar_bee_01.wav", fade(highpass(highpass(trim_lead(load(SG + "bee_a.wav"), -45), 150), 150), 0.05, 0.3), -14)
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
# → 2026-11-24 실제 녹음으로 바꿈 (맨 아래 "바탕 · 방 환경음 실제 녹음" 구역) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# bed = chain_loop([sl(load(DS + "bed_a.wav"), 0.3, 10.0), sl(load(DS + "bed_b.wav"), 0.3, 10.0)], 2.0)
# save("Interior/scape_deep_bed_loop.wav", circular(lambda y: soften(highpass(y, 60), 6000, 0.4), bed), -24)
# report.append(f"  통신기지 바탕 고리 {len(bed) / SR:.2f}초")

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

# ================= 체험 · 미니게임 공용 꾸러미 (SOUND_LIST_NEW 5-1 — 창 열기 · 닫기 · 등급 상승을 빌려 쓰던 자리) =================
# Audio/Ride/. 탑승 · 내림 = VARCO 걸쇠 (2번), 나머지는 음악 조 (F 장조 · D 단조 — F 오음계)에 맞춰 합성: 하프 (궤도 항만) · 유리 종 (항성 발전망) · 반짝임 (수리 사령부).
RD = "Ride/"

# 탑승 (자리에 앉고 안전바가 "툭") — 첫 5개 ("푹신한 안전바가 잠김")는 모두 밝고 날 선 딸깍 (무게중심 5 ~ 10 kHz · crest 17 ~ 20, 여러 번 침) →
# "먹먹한 플라스틱 걸쇠 + 쿠션 툭"으로 다시 5개 중 4: 무게중심 2.7 kHz (가장 낮음) · crest 12.4 · 시작 19 ms (부드러움). 그 안 툭 여섯 중 가장 크고 긴 1.03초 것.
# 다시 만든 1 · 3 · 5 = 6 ~ 8 kHz로 밝음, 2 = 지직 224.
lt = load(RD + "latch.wav")
board = fade(soften(highpass(sl(lt, 1.0, 1.32), 80), 4000, 0.5), 0.004, 0.08)
save("Ride/ride_board.wav", board, -14)
# 내림 (안전바가 풀리고 일어남) — 바람 빠지는 "슉" (합성, 0.25초 부풀었다 잦아듦 · 0.8 ~ 3 kHz) 뒤에 같은 걸쇠의 다른 툭 (2.7초)을 2반음 높여 가볍게.
rng = np.random.default_rng(11)
t = np.arange(int(0.32 * SR)) / SR
puff = onepole_lp(highpass(rng.standard_normal(len(t)), 800), 3000) * np.sin(np.pi * np.minimum(1, t / 0.32)) ** 2
puff *= 0.35 * np.abs(board).max() / (np.abs(puff).max() + 1e-9)
leave = place(puff, pitch(soften(highpass(sl(lt, 2.67, 2.95), 80), 4000, 0.5), 2), 0.2)
save("Ride/ride_leave.wav", fade(leave, 0.01, 0.08), -14)


def wood_tick(f=698.46, dur=0.12):
    """작은 나무 "톡" — 짧은 울림 (기음 + 2.6배 옅게, 25 ms 만에 꺼짐) + 아주 짧은 두드림 잡음."""
    t = np.arange(int(dur * SR)) / SR
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.025) + 0.3 * np.sin(2 * np.pi * 2.6 * f * t) * np.exp(-t / 0.012)
    rng = np.random.default_rng(5)
    x += 0.25 * onepole_lp(rng.standard_normal(len(t)), 3000) * np.exp(-t / 0.004)
    return fade(x * (1 - np.exp(-t / 0.0015)), 0.0, 0.03)


# 남은 시간 째깍 — 작은 나무 "톡" (파5 — 조 안).
save("Ride/game_tick.wav", wood_tick(), -8)   # −18은 녹음 검수에서 음악보다 −24 (0.12초 짧은 소리라 묻힘)

# 점수 "띵" — 맑은 유리 종 도6 (짧게 0.7초). 연달아 맞히면 AudioService.TryPlayScore가 F 오음계로 한 칸씩 높임.
save("Ride/game_score.wav", fade(flower_bell(C6, 0.7, 3), 0.0, 0.25), -14)

# 틀림 · 놓침 "뿌" — 부드럽게 내려가는 둥근 음 (라4 → 파4, 0.28초 · 배음 옅게 · 3 kHz 위 없음). 거절음 (UiError)보다 가볍게.
t = np.arange(int(0.34 * SR)) / SR
fr = 440.0 * (349.23 / 440.0) ** np.minimum(1, t / 0.22)
ph = 2 * np.pi * np.cumsum(fr) / SR
boop = (np.sin(ph) + 0.3 * np.sin(2 * ph) + 0.08 * np.sin(3 * ph)) * np.minimum(1, t / 0.012) * np.exp(-np.maximum(0, t - 0.12) / 0.08)
save("Ride/game_miss.wav", fade(onepole_lp(boop, 3000), 0.0, 0.05), -14)

F4, A4_, C5_, D5, F5, A5, C6_, D6, F6, A6, C7 = 349.23, 440.0, 523.25, 587.33, 698.46, 880.0, 1046.5, 1174.66, 1396.91, 1760.0, 2093.0
# 시작 — 하프 "도 · 파" 두 음 (준비) + 유리 종 파 · 라 · 도 화음 (출발). 1.6초.
start = np.zeros(int(2.4 * SR))
start = place(start, harp(C5_, 1.5, 21) * 0.8, 0.0)
start = place(start, harp(F5, 1.5, 22) * 0.85, BEAT / 2)
for i, f in enumerate((F5, A5, C6_)):
    start = place(start, flower_bell(f, 1.6, 20 + i) * 0.45, BEAT)
save("Ride/game_start.wav", fade(start, 0.0, 0.5), -14)

# 성공 — 하프가 파 · 라 · 도 · 파로 올라가고 (반의 반 박씩) 유리 종 파 · 라 화음.
clear = np.zeros(int(3.0 * SR))
for i, f in enumerate((F4, A4_, C5_, F5)):
    clear = place(clear, harp(f, 2.2, 30 + i) * (0.75 + 0.05 * i), i * BEAT / 4)
for i, f in enumerate((F5, A5)):
    clear = place(clear, flower_bell(f, 2.0, 30 + i) * 0.4, BEAT)
save("Ride/game_clear.wav", fade(clear, 0.0, 0.8), -14)

# 아쉬움 — 하프가 라 · 파 · 레로 천천히 내려감 (반 박씩, 부드럽게).
fail = np.zeros(int(3.0 * SR))
for i, f in enumerate((A4_, F4, 293.66)):
    fail = place(fail, harp(f, 2.2, 40 + i) * (0.8 - 0.08 * i), i * BEAT / 2)
save("Ride/game_fail.wav", fade(onepole_lp(fail, 2500), 0.0, 0.8), -14)

# 새 기록 — 하프 여섯 음 파4 ~ 도6 빠르게 (1/6 박씩) → 유리 종 파 · 라 · 도 화음 + 반짝임 파6 · 라6 · 도7.
rec = np.zeros(int(4.0 * SR))
for i, f in enumerate((F4, A4_, C5_, F5, A5, C6_)):
    rec = place(rec, harp(f, 2.4, 50 + i) * (0.7 + 0.04 * i), i * BEAT / 6)
for i, f in enumerate((F5, A5, C6_)):
    rec = place(rec, flower_bell(f, 2.6, 50 + i) * 0.45, BEAT)
for i, f in enumerate((F6, A6, C7)):
    rec = place(rec, twinkle(f, 1.0, 60 + i) * 0.25, BEAT + BEAT / 4 + i * BEAT / 4)
save("Ride/game_record.wav", fade(rec, 0.0, 1.0), -14)

# ================= 체험 — 궤도 도시 "금빛 궤도열차 · 정확히 멈추기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorOrbitTrain (W 가속 · S 브레이크 · 한 바퀴 돌아 정지선). 출발 · 탑승 · 내림 · 결과는 5-1 공용. VARCO 하나씩 2번 (달림 · 브레이크), 정확히 멈춤 = 합성.
OCR = "Ride/OrbitalCity/"

# 달림 (탄 동안 2D 루프, 빠르기에 따라 크기 · 재생 높이) — 후보 5 중 5: 1초 뒤부터 끝까지 0.5초 크기가 −18 ~ −19 dB로 고름 · 울림 0 · 지직 7.8 (가장 낮음) · 낮은 전기 "웅" (82 Hz 대, 또렷한 음 없음).
# 1 = 지나가며 커졌다 작아짐 (−40 → −14 → −40, 루프 X) · 380 ~ 415 Hz 모터 "이잉" (G#4 — 조 밖), 2 · 3 = 울림 줄 23 · 33, 4 = 3초만 고름.
# 다섯 모두 레일 이음 소리가 없음 → 합성 "다-닥" (먹먹한 나무 같은 두드림, 바퀴 둘 0.26초 간격)을 2.1초마다 옅게 얹음 (재생 높이를 올리면 같이 빨라짐).
hum = highpass(sl(load(OCR + "tram_hum.wav"), 0.7, 5.74), 45)
run = chain_loop([hum, hum[::-1]], 0.8)
LR = len(run) / SR
rng = np.random.default_rng(31)
tk = np.arange(int(0.09 * SR)) / SR
clack = onepole_lp(onepole_lp(rng.standard_normal(len(tk)), 1400), 2500) * np.exp(-tk / 0.010) * 0.6 + np.sin(2 * np.pi * 170 * tk) * np.exp(-tk / 0.028)
clack = fade(clack, 0.001, 0.02)
clack *= 1.8 * np.sqrt((run ** 2).mean()) / np.sqrt((clack[: int(0.05 * SR)] ** 2).mean())
for k in range(4):
    for j, g_ in ((0, 1.0), (1, 0.8)):
        at = (0.5 + k * LR / 4 + j * 0.26) % LR
        i = int(at * SR)
        n = min(len(clack), len(run) - i)
        run[i:i + n] += clack[:n] * g_ * (0.85 + 0.15 * ((k * 7) % 3) / 2)
save("Ride/otrain_loop.wav", circular(lambda y: soften(y, 5000, 0.3), run), -18)   # −24 → −18: 낮은 "웅" (300 Hz 아래 85 %)이라 귀 감도로 rms보다 9 dB 작음 — 녹음 검수에서 음량 0.8에 음악보다 −12 (안 들림)
report.append(f"  궤도열차 달림 고리 {LR:.2f}초")

# 브레이크 (S를 누르는 순간, 빠를 때만) — 후보 5 중 2의 처음 1.1초: 둥근 시작 (44 ms) · 무게중심 4 kHz (가장 덜 밝음), "푸쉬" 부풀었다 잦아듦.
# 1 = 날 선 시작 (판에 꼭대기까지 세로줄) · 긴 쉿 꼬리, 3 · 5 = 그냥 이어지는 쉿 (무게중심 7.8 ~ 9.4 kHz), 4 = 쉿 뒤에 "툭". 끽 소리는 다섯 모두 없음.
br = sl(load(OCR + "air_brake.wav"), 0.0, 1.15)
save("Ride/otrain_brake.wav", fade(soften(soften(highpass(br, 150), 5000, 0.45), 9000, 0.4), 0.02, 0.55), -14)

# 정확히 멈춤 (판정 뒤 천천히 정지선에 맞춰 역에 섬 — 대표 순간) — 합성: 유리 종 (항성 발전망 꽃 종 틀) 파5 · 라5 · 도6 · 파6을 0.09초씩 굴려 친 화음 + 낮은 파4.
# 결과음 (성공 · 새 기록, 하프 + 종)은 3.5초 전에 끝남 (판정 → 결과 3.5초 → 맞춰 섬) — 하프 없이 종만이라 결과음과 갈림.
stop = np.zeros(int(3.2 * SR))
stop = place(stop, flower_bell(349.23, 2.8, 70) * 0.35, 0.0)
for i, f in enumerate((698.46, 880.0, 1046.5, 1396.91)):
    stop = place(stop, flower_bell(f, 2.6, 71 + i) * (0.75 - 0.08 * i), 0.09 * i)
save("Ride/otrain_stop_perfect.wav", fade(stop, 0.0, 0.8), -14)

# ================= 체험 — 바이오스피어 "물꼬 맞추기 · 반딧불이 잡기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorSluiceGates (수문 바퀴 셋 · 물꼬가 한 단씩) · InteriorFireflyCatch (유리병으로 반딧불이). VARCO 1번 (수문 바퀴), 물 흐름 = 소리 풍경 수문 폭포 · 물길 원본, 반딧불이 = 합성.
BIR = "Ride/Biosphere/"

# 수문 바퀴 한 칸 (바퀴 앞 3D, 바퀴는 0.6초에 120° 돌고 멈춤) — 후보 5 중 1의 끝 1.05초: 나무 톱니 "드르륵" 0.47초 → 0.47 · 0.64초에 "툭 · 툭" (바퀴가 서는 순간),
# 무게중심 1.7 kHz · 300 Hz 아래 44 % (따뜻한 나무) · 울림 0. 2 = 5초 내내 톱니만 (툭 없음), 3 · 4 = 불규칙한 삐걱 (4 = 울림 13), 5 = 울림 22.
# 소리 풍경 물레방아 · 삐걱임 조각은 물 철썩 · 긴 "끼익"이라 "한 칸 돌림"이 안 됨.
gw = sl(load(BIR + "gate_wheel.wav"), 1.80, 2.85)
save("Ride/bio_sluice_wheel.wav", fade(soften(highpass(gw, 80), 6000, 0.3), 0.03, 0.25), -14)

# 물 흐름 (물꼬가 열려 물이 흐르기 시작하는 순간, 그 물길 수문 3D) — 소리 풍경 수문 폭포 (sluice_fall, 첨벙임이 있는 따뜻한 물) 75 % + 물길 졸졸 (brook) 45 %를
# 0.3초에 부풀었다 1.4초에 잦아들게 (1.6초). 새로 만들지 않음 — 같은 방의 물과 같은 결.
fl = sl(load("Interior/scape_bio/sluice_fall.wav"), 3.0, 4.6)
bk = sl(load("Interior/scape_bio/brook.wav"), 2.0, 3.6)[: len(fl)]
water = highpass(0.75 * fl / np.sqrt((fl ** 2).mean()) + 0.45 * bk / np.sqrt((bk ** 2).mean()), 80)
tw = np.arange(len(water)) / SR
water *= (1 - np.exp(-tw / 0.18)) * np.exp(-np.maximum(0, tw - 0.45) / 0.38)
save("Ride/bio_water_flow.wav", fade(soften(water, 5000, 0.4), 0.0, 0.3), -14)


def pop_glide(f, dur=0.7):
    """병 "퐁" — 7반음 아래에서 미끄러져 올라와 f에 닿는 둥근 음 (0.1초 안에) + 짧은 병 몸 "폭" (한 옥타브 아래)."""
    t = np.arange(int(dur * SR)) / SR
    ph = 2 * np.pi * np.cumsum(f * 2 ** (-7 * np.exp(-t / 0.035) / 12)) / SR
    tone = (np.sin(ph) + 0.18 * np.sin(2 * ph)) * (1 - np.exp(-t / 0.004)) * np.exp(-t / 0.16)
    body = np.sin(2 * np.pi * f / 2 * t) * np.exp(-t / 0.02) * 0.4
    return tone + body


# 반딧불이 잡기 (병에 담는 순간 2D · 연달아 잡으면 음계로 오름 — TryPlayNote, 기준 = 도6) — 합성: 병 "퐁" (솔5 → 도6으로 미끄러짐) + 반짝임 (도7 · 솔7).
# 음계로 옮겨도 조가 안 깨지게 겹친 음은 으뜸 · 5도 · 옥타브만 (3도를 넣으면 레에서 파#이 됨).
catch = np.zeros(int(1.0 * SR))
catch = place(catch, pop_glide(C6), 0.0)
catch = place(catch, twinkle(2 * C6, 0.8, 81) * 0.35, 0.05)
catch = place(catch, twinkle(3 * C6, 0.8, 82) * 0.15, 0.09)
save("Ride/bio_firefly_catch.wav", fade(onepole_lp(catch, 7000), 0.0, 0.15), -14)

# ================= 체험 — 궤도 방어망 "오로라 보호막 · 등대 빛 돌리기 · 별똥별 막기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorAuroraShield (방파제 끝 레버 → 보호막 쇼) · InteriorLighthouse (등실 금빛 손잡이로 렌즈 돌리기) · InteriorStarfall (포대 자리에서 별똥별 쏘기).
# VARCO 하나씩 3번 (레버 1 · 손잡이 2 — 첫 판은 다섯 모두 밝고 날 선 삐걱 · 딸깍). 레버 "끼익" = 궤도 항만 삐걱임 원본, 보호막 = 밤 오로라 울림 조각, 나머지 음은 합성.
# 음계로 옮기는 소리 (보호막에 맞음 · 빛이 답함 · 별똥별 터짐)는 겹친 음을 으뜸 · 5도 · 옥타브만 (3도는 레 · 라에서 조 밖 음이 됨).
ODR = "Ride/OrbitalDefense/"

# 레버 당김 (방파제 끝 레버 자리 3D, 0.6초에 당겨짐) — 첫 5개 ("나무 레버 끼익 + 툭")는 모두 "툭 · 쿵" 두드림만 (삐걱 없음) →
# "끼익" = 궤도 항만 선착장 삐걱임 원본 7.25 ~ 7.9초 (가로 배음 줄이 있는 나무 삐걱 0.5초, 같은 나무 잔교 결)를 3반음 올려 작게,
# "툭" = 후보 4의 첫 두드림 (시작 10 ms · 무게중심 520 Hz · 울림 0)을 레버가 끝에 닿는 0.52초에. 1 = 낮은 "쿵" (72 % 300 Hz 아래), 2 · 5 = 여러 번 덜컥, 3 = 두드림 둘 + 1초 뒤 큰 "쿵".
creak = pitch(sl(load("Interior/scape_port/dock_creak.wav"), 7.25, 7.9), 3)
creak = fade(soften(highpass(creak, 150), 4000, 0.4), 0.01, 0.12)
th = load(ODR + "lever_thunk.wav")
h = hit_time(th)
th = fade(highpass(sl(th, max(0.0, h - 0.012), h + 0.85), 60), 0.003, 0.35)
pk = lambda y: np.sqrt(np.convolve(y ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "valid").max())
lever = np.zeros(int(1.5 * SR))
lever = place(lever, creak * 0.5 * pk(th) / pk(creak), 0.0)
lever = place(lever, th, 0.52)
save("Ride/odef_lever_pull.wav", fade(lever, 0.0, 0.2), -14)

# 보호막 펼쳐짐 (레버 0.6초 뒤 막이 레버 자리에서 퍼져 덮음 — 큰 순간, 2D) — 밤 오로라 울림 (도4 · 솔4 · 레5 · 미5 합성 루프) 4초 조각을
# 2.2초에 걸쳐 차올랐다가 잦아들게 + 반짝임이 도6 · 레6 · 미6 · 솔6 · 라6 · 도7로 올라감 (도 오음계 — 오로라와 같은 음) + 바람결 "후우" 부풂.
aur = sl(load("../SpaceStation/Assets/_Project/Audio/Interior/scape_defense_aurora_loop.wav"), 3.0, 7.2)
ta = np.arange(len(aur)) / SR
swell = np.clip(ta / 2.2, 0, 1) ** 1.6 * np.exp(-np.maximum(0, ta - 2.2) / 1.1)
aur = aur / np.sqrt((aur ** 2).mean()) * swell
rng = np.random.default_rng(41)
gust = onepole_lp(highpass(rng.standard_normal(len(ta)), 600), 2500) * swell ** 1.5
aur += 0.25 * gust / np.sqrt((gust ** 2).mean() + 1e-12)
for i, semis in enumerate((0, 2, 4, 7, 9, 12)):
    aur = place(aur, twinkle(C6 * 2 ** (semis / 12), 1.2, 90 + i) * (0.35 + 0.08 * i) * np.abs(aur).max() * 0.5, 0.35 + i * 0.3)
save("Ride/odef_shield_unfurl.wav", fade(onepole_lp(aur, 6000), 0.05, 0.8), -14)


def glass_pong(f, dur=1.1, seed=0):
    """둥근 유리 "퐁" — 살짝 위에서 내려와 f에 닿음 (25 ms), ±3센트 옅은 두 겹이 일렁임 (오로라 막 결 — 같은 크기 세 겹은 0.8초에 서로 지워짐), 2배음은 빨리 꺼짐."""
    t = np.arange(int(dur * SR)) / SR
    bend = 2 ** (0.8 * np.exp(-t / 0.025) / 12)
    x = np.zeros_like(t)
    for k, (c, a) in enumerate(((-3, 0.35), (0, 1.0), (3, 0.35))):
        ph = 2 * np.pi * np.cumsum(f * 2 ** (c / 1200) * bend) / SR + k + seed
        x += a * (np.sin(ph) + 0.3 * np.sin(2 * ph) * np.exp(-t / 0.25))
    return x / 1.7 * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.35)


# 별똥별이 보호막에 맞음 (2D, 맞을 때마다 음계로 한 칸씩 — 도 · 레 · 파 · 솔 · 라 되풀이, 기준 = 도5) — 둥근 유리 "퐁" + 반짝 (도7 · 솔7).
hit = np.zeros(int(1.2 * SR))
hit = place(hit, glass_pong(523.25, 1.1, 1), 0.0)
hit = place(hit, twinkle(4 * 523.25, 0.8, 91) * 0.22, 0.03)
hit = place(hit, twinkle(6 * 523.25, 0.6, 92) * 0.1, 0.06)
save("Ride/odef_star_hit_shield.wav", fade(onepole_lp(hit, 7000), 0.0, 0.2), -14)

# 손잡이 돌림 (손잡이를 잡은 동안 2D 루프, 돌리는 빠르기만큼 크기) — 몸통 = 두 번째 5개 ("나무 굴대가 천천히 도는 낮은 웅") 중 1:
# 0.5 ~ 4초 고름 (−17 ~ −19 dB) · crest 6.4 · 지직 26 (가장 낮음) · 116 Hz 대 낮은 나무 "웅" (기어). 2 ~ 5 = 띄엄띄엄 끊김 (울림 6 ~ 28).
# 삐걱 = 첫 5개 ("나무 손잡이 삐걱 · 기어") 중 2: 한 바퀴마다 삐걱 (0.5 ~ 0.7초 간격 — 돌리는 손 결). 다섯 모두 밝고 날 섬 (무게중심 4.4 ~ 8.4 kHz · 시작 1.2 ~ 1.8 ms ·
# 지직 45 ~ 86 — 삐걱 자체가 잔 딸깍이라 2 kHz 위를 눌러도 지직 52) → 첫머리 0.90초 → 6.26초 (9번)를 고리로 잘라 한 옥타브 내려 낮은 나무 "끄응" (1.2초마다)으로,
# 2 kHz 위를 두 번 누르고 센 곳을 8 dB 눌러 몸통에 옅게 (40 %) — 결과 지직 33 · 시작 10 ms. 몸통만이면 귀 감도로 rms보다 16 dB 작아 거의 안 들림.
ax = sl(load(ODR + "crank_axle.wav"), 0.5, 3.98)
body = circular(lambda y: highpass(y, 50), chain_loop([ax, ax[::-1], ax, ax[::-1]], 0.8))
ck = loop_exact(sl(load(ODR + "crank_creak.wav"), 0.85, 7.1), 5.36)
ck = pitch(np.concatenate([ck, ck[:200]]), -12)[: len(body)]
ck = circular(lambda y: tame(soften(soften(highpass(y, 120), 1800, 0.7), 1800, 0.7), 8.0, 0.003, 0.05), ck)
save("Ride/odef_crank_loop.wav", body / np.sqrt((body ** 2).mean()) + 0.4 * ck / np.sqrt((ck ** 2).mean()), -24)
report.append(f"  등대 손잡이 고리 {len(body) / SR:.2f}초")


def glow_bell(f, dur=1.6, seed=0):
    """빛이 닿아 답하는 유리 종 — 꽃 종보다 부드럽게 켜지고 (20 ms) 한 옥타브 위가 천천히 차올라 남음 (빛 번짐)."""
    t = np.arange(int(dur * SR)) / SR
    glow = np.sin(2 * np.pi * 2 * f * t + seed) * (1 - np.exp(-t / 0.12)) * np.exp(-t / 0.5) * 0.25
    return (flower_bell(f, dur, seed) + glow) * (1 - np.exp(-t / 0.02))


# 빛이 닿아 답함 (2D — 새 곳 = 찾은 차례대로 도 · 레 · 파 · 솔 · 라 · 도, 이미 찾은 곳 = 그 곳의 음을 작게, 기준 = 도5) — 합성 "빛 유리 종".
# → 2026-11-25 실제 녹음 (유리 꽃병 딩, 도6)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역) — 아래 줄은 기록
# save("Ride/odef_beam_react.wav", fade(glow_bell(523.25, 1.6, 93), 0.0, 0.4), -14)

# 포 쏨 (클릭마다, 0.22초마다 연타 가능 — 2D) — 합성: 공기 "퐁" (850 → 330 Hz로 20 ms 만에 내려감 + 2배음 옅게, 60 ms에 꺼짐) + 짧은 "쉿" (0.9 ~ 3.5 kHz, 50 ms).
# 0.25초로 짧고 날 선 첫머리 없이 (시작 4 ms 위) — 연달아 눌러도 거슬리지 않게. 처음엔 380 → 160 Hz (300 Hz 아래 95 %)였는데 녹음 검수에서
# 음량 0.35에 음악보다 −15 (첫 20 ms만 치고 사라져 묻힘) → 높이를 올려 귀 감도 +5 dB · 원본 −8.
t = np.arange(int(0.25 * SR)) / SR
ph = 2 * np.pi * np.cumsum(330 + 520 * np.exp(-t / 0.02)) / SR
fire = (np.sin(ph) + 0.25 * np.sin(2 * ph)) * (1 - np.exp(-t / 0.002)) * np.exp(-t / 0.06)
rng = np.random.default_rng(43)
hs = onepole_lp(highpass(rng.standard_normal(len(t)), 900), 3500) * (1 - np.exp(-t / 0.004)) * np.exp(-t / 0.05)
fire += 0.4 * hs / np.abs(hs).max()
save("Ride/odef_cannon_fire.wav", fade(fire, 0.0, 0.05), -8)

# 별똥별 터짐 (맞힌 순간 2D, 연달아 맞히면 음계로 — 공용 점수 "띵"과 하나로, 기준 = 도5) — 합성: 부드러운 "팟" (3 kHz 아래 잡음 30 ms) + 반짝임이 도5 · 도6 · 솔6 · 도7로 흩어짐.
# 궤도 방어망 소리 풍경 별똥별 반짝임 (도7 · 솔6 · 미7)은 미 (3도)가 있어 음계로 옮기면 조가 깨져서 새로 만듦.
t = np.arange(int(0.12 * SR)) / SR
rng = np.random.default_rng(44)
puff = onepole_lp(rng.standard_normal(len(t)), 3000) * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.03)
burst = np.zeros(int(1.1 * SR))
burst = place(burst, puff / np.abs(puff).max() * 0.35, 0.0)
for i, (mul, at, g_) in enumerate(((1, 0.0, 0.6), (2, 0.03, 0.45), (3, 0.07, 0.3), (4, 0.12, 0.2), (3, 0.2, 0.12), (4, 0.27, 0.1))):
    burst = place(burst, twinkle(523.25 * mul, 0.8, 95 + i) * g_, at)
save("Ride/odef_star_burst.wav", fade(onepole_lp(burst, 7000), 0.0, 0.2), -14)

# ================= 체험 — 수리 사령부 "구조 바구니 · 경종 등불 쇼 · 드론 순서 기억" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorRescueBasket (F마다 도르래 줄을 당겨 나무집 마루로) · InteriorFireflyShow (경종 → 반딧불 드론이 가지 등불 열셋을 켬) · InteriorDroneMemory (등불 다섯 순서 기억).
# VARCO 1번 (도르래 줄). 바구니 삐걱 = 궤도 항만 삐걱임 원본 + 궤도 방어망 레버 "툭", 경종 = 소리 풍경 scape_repair_bell_01 그대로 (새 파일 없음),
# 반짝임 · 등불 음은 소리 풍경 반딧불 (twinkle)과 같은 결로 합성. 음계로 옮기는 소리는 겹친 음을 으뜸 · 5도 · 옥타브만.
RCR = "Ride/RepairCommand/"

# 도르래 줄 (탄 동안 2D 루프 — 줄을 당기는 동안 · 내려가는 동안 크기 1, 멈추면 0.4초에 꺼짐) — 후보 5 중 4: 5.5초 내내 이어지는 줄 · 도르래 결
# (무게중심 606 Hz · 울림 2 · 지직 25). 1 · 2 · 3 · 5 = 1.5 ~ 2초 "쉬이익" 당김 덩어리 사이가 비어 루프 X (1 = 무게중심 4.4 kHz로 밝음).
# 1.2 ~ 5.35초에서 튀는 곳 셋 (1.1 · 2.5 · 4.8초, 10 ms가 0.1초 중앙보다 +14 ~ +22 dB)을 10 dB 눌러 없애고 0.4초 크기 흔들림을 절반 펴 앞뒤로 이음.
# 멈췄다 다시 당기면 루프가 처음부터 — 당길 때마다 같은 "스윽"으로 시작.
rp = tame(highpass(sl(load(RCR + "rope_pulley.wav"), 1.2, 5.35), 70), 10.0, 0.002, 0.04)
ev = np.sqrt(np.convolve(rp ** 2, np.ones(int(0.4 * SR)) / int(0.4 * SR), "same")) + 1e-9
rp = rp * np.sqrt(np.median(ev) / ev)
rope = chain_loop([rp, rp[::-1]], 0.6)
save("Ride/rcmd_rope_loop.wav", circular(lambda y: soften(y, 5000, 0.4), rope), -24)
report.append(f"  도르래 줄 고리 {len(rope) / SR:.2f}초")

# 바구니가 정류장 · 마루에 닿음 (바구니 자리 3D) — "삐걱" = 궤도 항만 선착장 삐걱임 원본 1.9 ~ 2.6초 (엮은 바구니 · 줄이 당겨지는 짧은 삐걱 덩어리)를
# 5반음 올려 작게 (소리 풍경 도르래 삐걱과 같은 나무 결), "툭" = 궤도 방어망 레버 "툭"을 5반음 올려 가볍게 0.3초에.
bc = pitch(sl(load("Interior/scape_port/dock_creak.wav"), 1.9, 2.6), 5)
bc = fade(soften(highpass(bc, 200), 4000, 0.4), 0.01, 0.1)
bt = load("Ride/OrbitalDefense/lever_thunk.wav")
h = hit_time(bt)
bt = pitch(fade(highpass(sl(bt, max(0.0, h - 0.012), h + 0.7), 80), 0.003, 0.3), 5)
pk = lambda y: np.sqrt(np.convolve(y ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "valid").max())
basket = np.zeros(int(1.0 * SR))
basket = place(basket, bc * 0.6 * pk(bt) / pk(bc), 0.0)
basket = place(basket, soften(bt, 3000, 0.4), 0.3)
save("Ride/rcmd_basket_creak.wav", fade(basket, 0.0, 0.15), -14)

# 등불 켜짐 반짝 (드론이 가지 등불을 켤 때마다 그 등불 3D — 켠 차례대로 음계로 올라감, 기준 = 도5 · 열셋이면 도5 → 파7) —
# 소리 풍경 반딧불 (도6 twinkle)과 같은 결을 한 옥타브 아래 몸으로 + 옥타브 · 12도 반짝임 (빛이 확 켜짐).
sh = np.zeros(int(1.3 * SR))
sh = place(sh, twinkle(523.25, 1.2, 100), 0.0)
sh = place(sh, twinkle(1046.5, 1.0, 101) * 0.45, 0.02)
sh = place(sh, twinkle(1569.75, 0.8, 102) * 0.2, 0.05)
save("Ride/rcmd_lantern_shimmer.wav", fade(onepole_lp(sh, 7000), 0.0, 0.25), -14)

# 드론 떼가 공방에서 날아오름 (등불 쇼 2단계 — 종이 멎고 떼가 고목 둘레로, 2D) — 합성: 작은 반짝임 열넷이 파5에서 파7까지 F 오음계로 흩어지며 올라감 + 옅은 바람 "후우".
rise = np.zeros(int(2.6 * SR))
rng = np.random.default_rng(51)
fsc = [349.23 * 2 ** (s / 12) for s in (12, 14, 16, 19, 21, 24, 26, 28, 31, 33, 36)]   # 파5 · 솔5 · 라5 · 도6 · 레6 · 파6 …
for i in range(14):
    k = min(len(fsc) - 1, int(i * len(fsc) / 14 + rng.integers(0, 2)))
    rise = place(rise, twinkle(fsc[k], 0.7, 110 + i) * (0.25 + 0.02 * i), 0.05 + i * 0.11 + rng.uniform(0, 0.04))
tr = np.arange(len(rise)) / SR
whoosh = onepole_lp(highpass(rng.standard_normal(len(tr)), 500), 2500) * np.sin(np.pi * np.clip(tr / 2.0, 0, 1)) ** 2
rise += 0.12 * np.abs(rise).max() * whoosh / np.abs(whoosh).max()
save("Ride/rcmd_drones_rise.wav", fade(onepole_lp(rise, 7000), 0.02, 0.5), -14)

# 쇼 피날레 (가지 등불 열셋이 다 켜지고 떼가 환히 빛나며 퍼짐 — 큰 순간, 2D) — 합성: 꽃 종 파5 · 라5 · 도6 화음 + 반짝임이 파7 → 파6로 흩어져 내림 + 낮은 하프 파3 · 파4.
# 대표 선율 (scape_repair_motif = 경종 + 반짝임 오름)은 경종이 다시 울리는 것처럼 들려 (종은 쇼 처음에만 흔들림) 쓰지 않음.
fin = np.zeros(int(4.2 * SR))
fin = place(fin, harp(174.61, 3.0, 120) * 0.35, 0.0)
fin = place(fin, harp(349.23, 3.0, 121) * 0.3, 0.02)
for i, f in enumerate((698.46, 880.0, 1046.5)):
    fin = place(fin, flower_bell(f, 3.0, 122 + i) * 0.4, 0.04 + 0.05 * i)
for i, s in enumerate((36, 33, 31, 28, 26, 24, 21, 19)):
    fin = place(fin, twinkle(349.23 * 2 ** (s / 12), 1.0, 130 + i) * (0.3 - 0.02 * i), 0.15 + i * 0.12)
save("Ride/rcmd_show_finale.wav", fade(onepole_lp(fin, 7000), 0.0, 1.0), -14)

# 기억 등불 음 (드론 순서 기억 — 드론이 보여 줄 때 · 플레이어가 고를 때 둘 다, 2D, 등불 k = 도 · 레 · 파 · 솔 · 라, 기준 = 도5) —
# 합성: 꽃 종 (맑은 한 음, 1.1초 — 보여 주는 빠르기 0.68 ~ 1.25초에 한 음씩) + 옥타브 반짝임 옅게. 등불마다 높이가 또렷해야 해서 반짝임은 작게.
ln = np.zeros(int(1.3 * SR))
ln = place(ln, flower_bell(523.25, 1.2, 140), 0.0)
ln = place(ln, twinkle(1046.5, 0.8, 141) * 0.2, 0.01)
save("Ride/rcmd_lantern_note.wav", fade(ln, 0.0, 0.3), -14)


def whoosh(dur, peak, lo, mid, hi, seed=0):
    """바람 "휙" — 잡음 두 띠 (lo ~ mid · mid ~ hi)를 시간에 따라 낮은 띠 → 높은 띠 → 낮은 띠로 넘김 (지나가며 높아졌다 낮아짐), peak초에 가장 큼."""
    rng = np.random.default_rng(seed)
    t = np.arange(int(dur * SR)) / SR
    n = rng.standard_normal(len(t))
    low = onepole_lp(onepole_lp(highpass(n, lo), mid), mid)
    high = onepole_lp(onepole_lp(highpass(n, mid), hi), hi)
    low, high = low / np.sqrt((low ** 2).mean()), high / np.sqrt((high ** 2).mean())
    k = np.exp(-((t - peak) / (0.45 * peak + 0.02)) ** 2)   # 가장 빠를 때 높은 띠
    env = np.where(t < peak, np.sin(0.5 * np.pi * t / peak) ** 2, np.exp(-(t - peak) / (0.3 * (dur - peak))))
    return fade((low * (1 - 0.6 * k) + high * 0.6 * k) * env, 0.0, 0.03)


def bee_buzz(f0, dur, prof, jit=0.008, seed=0, glide=None):
    """꿀벌 날갯소리 — 원본 벌 (scape_stellar/bee_b)의 배음 크기 (prof dB)로 다시 합성 · 기음이 느리게 흔들림 (jit) · 날개 바람 쉿 옅게.
    떨림 주기는 dur초에 정수 번 (루프 이음새 없게), 위상은 적분식으로 정확히. glide = (끝 배율, 초) — 그 시간 동안 높이가 오름 (놀란 "윙!")."""
    t = np.arange(int(dur * SR)) / SR
    rng = np.random.default_rng(seed)
    mods = [(2 / dur, jit), (7 / dur, 0.6 * jit), (13 / dur, 0.5 * jit)]
    ph = f0 * t
    for fm, a in mods:
        p0 = rng.uniform(0, 2 * np.pi)
        ph = ph + f0 * a / (2 * np.pi * fm) * (np.sin(2 * np.pi * fm * t + p0) - np.sin(p0))
    if glide is not None:
        g, gt = glide
        u = np.clip(t / gt, 0, 1)
        ph = ph + f0 * (g - 1) * np.where(t < gt, gt * (u ** 3 - 0.5 * u ** 4), t - gt + 0.5 * gt)   # 높이 배율 1 + (g−1)(3u² − 2u³)를 적분
    ph *= 2 * np.pi
    x = np.zeros_like(t)
    for k, db in enumerate(prof, 1):
        if k * f0 * (glide[0] if glide else 1.0) > 9000:
            break
        x += 10 ** (db / 20) * np.sin(k * ph + rng.uniform(0, 2 * np.pi))
    am = 1 + 0.12 * np.sin(2 * np.pi * round(6 * dur) / dur * t)   # 날개 결 (6 Hz 안팎)
    hiss = onepole_lp(highpass(rng.standard_normal(len(t)), 2000), 6000)
    hiss *= 0.04 * np.abs(x).max() / (np.abs(hiss).max() + 1e-9)
    return (x + hiss * (1 + np.sin(ph) * 0.5)) * am


# ================= 체험 — 궤도 항만 "가리비 배 뱃놀이 · 진주 조개 잡기 · 인어 분수 진주 던지기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorScallopRide (해마 가리비 배 — 석호 한 바퀴) · InteriorPearlClams (큰 조개 셋이 번갈아 열림) · InteriorFountainToss (분수 빛 고리에 진주 던지기).
# VARCO 하나씩 2번 (노 젓기 · 조개). 배 닿음 = 소리 풍경 선착장 삐걱임 원본 + 궤도 방어망 레버 "툭", 퐁당 = 소리 풍경 물고기 퐁당 (scape_port_fish_02) 그대로 (새 파일 없음),
# 진주 · 던지기는 합성. 음계로 옮기는 진주 잡기는 겹친 음을 으뜸 · 5도 · 옥타브만.
OPR = "Ride/OrbitalPort/"

# 노 젓기 (배가 나아가는 동안 2D 루프, 빠르기만큼 크기) — 후보 5 중 2 + 3의 첨벙 셋: "약 1초마다 첨벙"으로 불렀는데 다섯 모두 2 ~ 4초에 한 번 크게 첨벙
# (첨벙마다 1.5 ~ 2초 꼬리) → 고른 첨벙을 2박 (1.33초)마다 차례로 놓아 고리 (5.33초 = 4번). 2 = 첨벙 사이 디지털 무음 · 지직 15.9 (가장 깨끗),
# 3 = 물결 결이 있는 첨벙 둘 (지직 18 ~ 24). 1 = 지직 29 ~ 34, 4 = 첨벙 하나뿐 · 바닥 −23 dB, 5 = 지직 40.
# 젓는 손 결 = 소리 풍경 선착장 삐걱임 원본 조각 둘을 7반음 올려 (작은 노걸이 "끽") 첨벙마다 옅게.
pa, pb = load(OPR + "paddle_a.wav"), load(OPR + "paddle_b.wav")
dock = load("Interior/scape_port/dock_creak.wav")


def stroke(x, a, b):
    s = trim_lead(sl(x, a, b), -32)[: int(2.4 * SR)]
    return fade(highpass(s, 80), 0.01, 0.45)


strokes = [stroke(pa, 0.3, 2.6), stroke(pb, 0.05, 2.4), stroke(pa, 4.95, 6.73), stroke(pb, 5.7, 8.05)]
creaks = [fade(soften(highpass(pitch(sl(dock, a, a + 0.42), 7), 300), 4000, 0.4), 0.01, 0.12) for a in (7.25, 1.95)]
PP = 2 * BEAT
paddle = np.zeros(int(4 * PP * SR))
pk = lambda y: np.sqrt(np.convolve(y ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "valid").max())
for i, (s, g_) in enumerate(zip(strokes, (1.0, 0.85, 0.92, 0.88))):
    one = np.zeros(int(2.6 * SR))
    one = place(one, s / pk(s) * g_, 0.0)
    c = creaks[i % 2]
    one = place(one, c / pk(c) * 0.22 * g_, 0.12)
    idx = (int(i * PP * SR) + np.arange(len(one))) % len(paddle)   # 끝을 넘는 꼬리는 처음으로 (고리)
    np.add.at(paddle, idx, one)
# 첨벙만 이으면 셋째 첨벙 (후보 2의 짧은 둘째) 꼬리 뒤 3.9초에 0.1초 빈 곳 → 배 옆 잔물결 (소리 풍경 배 물살 고리 4.6초를 5.33초로 늘여 — 2.6반음 낮아짐)을
# 첨벙 크기의 25 %로 밑에 깖.
wk = load("../SpaceStation/Assets/_Project/Audio/Interior/scape_port_wake_loop.wav")
wk = np.interp(np.arange(len(paddle)) * len(wk) / len(paddle), np.arange(len(wk) + 1), np.concatenate([wk, wk[:1]]))
paddle = paddle + 0.25 * np.sqrt((paddle ** 2).mean()) * wk / np.sqrt((wk ** 2).mean())
paddle = circular(lambda y: soften(y, 5000, 0.4), paddle)
save("Ride/oport_paddle_loop.wav", paddle, -24)
report.append(f"  노 젓기 고리 {len(paddle) / SR:.2f}초 · 이음새 {abs(paddle[0] - paddle[-1]):.4f}")

# 배가 선착장에 닿음 (배 자리 3D) — 소리 풍경 선착장 삐걱임 원본 1.9 ~ 2.6초 (바구니 삐걱과 같은 조각)를 2반음만 올려 (배 = 바구니보다 큼),
# "툭" = 궤도 방어망 레버 "툭" 그대로 높이 (바구니는 5반음 올림) 0.25초에.
bc = fade(soften(highpass(pitch(sl(dock, 1.9, 2.6), 2), 150), 4000, 0.4), 0.01, 0.12)
bt = load("Ride/OrbitalDefense/lever_thunk.wav")
h = hit_time(bt)
bt = fade(highpass(sl(bt, max(0.0, h - 0.012), h + 0.8), 60), 0.003, 0.35)
bump = np.zeros(int(1.2 * SR))
bump = place(bump, bc * 0.55 * pk(bt) / pk(bc), 0.0)
bump = place(bump, soften(bt, 3500, 0.4), 0.25)
save("Ride/oport_boat_dock.wav", fade(bump, 0.0, 0.2), -14)

# 조개 열림 (그 조개 자리 3D — 뚜껑이 0.22초에 열림) — 후보 5 중 4의 첫 "스르륵" (위로 미끄러지는 결 · crest 11.6 가장 둥긂 · 시작 10 ms) +
# 2의 둘째 "딸깍" (시작 3.8 ms)을 4 kHz 위를 눌러 다 열린 0.2초에 옅게. 1 = 3.3 kHz로 밝고 4초 뒤 또 침, 3 = 5 kHz · crest 22.9 (날 섬), 5 = 둘째 소리가 날 섬 (crest 23.9).
cs = load(OPR + "clam_slide.wav")
slide = fade(highpass(trim_lead(sl(cs, 0.0, 0.6), -35), 120), 0.005, 0.15)[: int(0.45 * SR)]
cc = load(OPR + "clam_clap.wav")
click = sl(cc, 0.55, 1.1)
h = hit_time(click)
click = fade(soften(soften(highpass(sl(click, max(0.0, h - 0.006), h + 0.25), 200), 4000, 0.5), 4000, 0.5), 0.002, 0.1)
opn = np.zeros(int(0.7 * SR))
opn = place(opn, slide, 0.0)
opn = place(opn, click * 0.45 * pk(slide) / pk(click), 0.2)
save("Ride/oport_clam_open.wav", fade(opn, 0.0, 0.15), -14)

# 조개 닫힘 "탁" (뚜껑이 다 닫히는 순간 그 조개 자리 3D) — 후보 2의 첫 소리: 둥근 "툭" 하나 (834 Hz · 300 Hz 아래 32 % · 시작 8 ms · 울림 0).
# 다른 후보의 닫힘 = 1 · 4 = 밝은 딸깍 (2.4 ~ 2.7 kHz), 5 = 날 섬. 부딪힘이 파일 처음에 오게 잘라 닫히는 순간 바로 울림.
# 그 "툭"은 20 ms뿐이라 녹음 검수에서 음악보다 −22 dB (귀 감도 0.25초로는 거의 안 들림) → 속 빈 껍데기가 울리는 몸통을 합성해 밑에 깖
# (라4 440 Hz 0.12초 + 1.18 kHz 0.06초 + 2.65 kHz 옅게 — 목탁 같은 "톡"), VARCO 부딪힘은 센 곳을 10 dB 눌러 40 %로 얹음 (피크 제한에 안 걸리게).
# 30 ~ 50 ms 몸통은 +11 dB에 그쳐 (−11 예상) 0.12초로 늘이고 −9로 저장.
snap = sl(cc, 0.0, 0.55)
h = hit_time(snap)
snap = fade(soften(highpass(sl(snap, max(0.0, h - 0.008), h + 0.4), 80), 5000, 0.4), 0.003, 0.2)
snap = tame(snap, 10.0, 0.001, 0.03)
tk = np.arange(int(0.4 * SR)) / SR
knock = (np.sin(2 * np.pi * 440 * tk) * np.exp(-tk / 0.12) + 0.5 * np.sin(2 * np.pi * 1180 * tk) * np.exp(-tk / 0.06)
         + 0.25 * np.sin(2 * np.pi * 2650 * tk) * np.exp(-tk / 0.012)) * (1 - np.exp(-tk / 0.0015))
snap = place(knock / np.abs(knock).max(), snap / np.abs(snap).max() * 0.4, 0.006)
save("Ride/oport_clam_snap.wav", fade(snap, 0.0, 0.1), -9)

# 진주를 잡음 (2D, 연달아 잡으면 음계로 — 공용 점수 "띵" 대신, 기준 = 도6, 금빛 진주는 1.3배) — 합성: 둥근 유리 "또롱" (궤도 방어망 glass_pong) 도6 +
# 반짝임 도7 · 솔7 (으뜸 · 5도만 — 음계로 옮겨도 조가 안 깨짐).
pg = np.zeros(int(1.2 * SR))
pg = place(pg, glass_pong(C6, 1.1, 150), 0.0)
pg = place(pg, twinkle(2 * C6, 0.8, 151) * 0.25, 0.04)
pg = place(pg, twinkle(3 * C6, 0.6, 152) * 0.12, 0.08)
save("Ride/oport_pearl_get.wav", fade(onepole_lp(pg, 7000), 0.0, 0.25), -14)

# 진주 던짐 (F마다 2D) — 합성 가벼운 "휙" (0.3초, 0.5 ~ 1.4 kHz → 1.4 ~ 3.5 kHz로 넘어갔다 돌아옴, 0.08초에 가장 큼).
save("Ride/oport_throw.wav", whoosh(0.32, 0.08, 500, 1400, 3500, 153), -14)

# ================= 체험 — AI 중추 "수정 뇌 깨우기 · 정원 드론 날기 · 뇌 미로 빛 구슬 · 시냅스 따라 켜기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorBrainShow (책 넷을 펼쳐 수정 뇌를 깨움) · InteriorDroneRide (정원 드론 바구니) · InteriorMazeRun (미로 빛 구슬) · InteriorSynapseMemory (시냅스 여섯 순서 기억).
# VARCO 0번 — 책장 = 소리 풍경 독서대 책장 원본, 수정 공명 = 소리 풍경 수정 뇌 루프 조각, 드론 바람 = 궤도 도시 옥상 바람 원본, 시냅스 = 소리 풍경 수정 띵 원본을
# 한 옥타브 내려 (도6), 나머지 음은 합성. 음계로 옮기는 소리 (빛 구슬 · 시냅스)는 겹친 음을 으뜸 · 5도 · 옥타브만.

# 책 펼침 (그 책 받침 자리 3D — 책장이 0.7초에 펼쳐짐) — 소리 풍경 독서대 책장 원본 (page_3, 넘김 한 번)의 같은 조각 + 펼쳐진 책장이 닿는 "툭"
# (궤도 방어망 레버 "툭"을 7반음 올려 작게) 0.62초에.
x = load(A + "page_3.wav")
env = np.convolve(x ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "same")
i = int(np.argmax(env))
page = fade(highpass(x[max(0, i - int(0.35 * SR)): i + int(0.45 * SR)], 120), 0.03, 0.15)
lt = load("Ride/OrbitalDefense/lever_thunk.wav")
h = hit_time(lt)
tap = fade(soften(highpass(pitch(sl(lt, max(0.0, h - 0.01), h + 0.6), 7), 150), 3000, 0.5), 0.003, 0.25)
book = np.zeros(int(1.3 * SR))
book = place(book, page, 0.0)
book = place(book, onepole_lp(tap, 2500) * 0.3 * pk(page) / pk(tap), 0.62)   # 0.6은 "툭"이 책장보다 커서 (피크 제한) 반으로 · 더 둥글게
save("Ride/aicore_book_open.wav", tame(fade(book, 0.0, 0.2), 6.0), -14)   # 책장 넘김의 날 선 첫머리 (crest 22)에 피크 제한이 걸려 음악보다 −0.3 → 센 곳만 6 dB 눌러 같은 −14로

# 수정 뇌가 깨어나 차오름 (책 넷을 다 펼친 0.8초 뒤 쇼 시작, 2D 큰 순간) — 합성 유리 화음 라3 · 라4 · 도5 · 미5 · 라5 (소리 풍경 수정 뇌 공명과 같은 음)가
# 2.2초에 걸쳐 부풀고 (±3센트 겹침 — 일렁임) 소리 풍경 수정 뇌 루프 조각이 함께 차오름 + 반짝임이 라5 · 도6 · 미6 · 라6으로 올라감. 4초.
LS = 4.2
ts = np.arange(int(LS * SR)) / SR
sw = np.clip(ts / 2.2, 0, 1) ** 1.5 * np.exp(-np.maximum(0, ts - 2.4) / 0.9)
chord = np.zeros_like(ts)
for j, (f0, amp) in enumerate(((220.0, 0.5), (440.0, 1.0), (523.25, 0.8), (659.25, 0.6), (880.0, 0.3))):
    for c in (-3, 0, 3):
        f = f0 * 2 ** (c / 1200)
        chord += amp / 3 * (np.sin(2 * np.pi * f * ts + j + c) + 0.2 * np.sin(4 * np.pi * f * ts + 0.7))
chord = onepole_lp(chord * sw, 3000)
brain = sl(load("../SpaceStation/Assets/_Project/Audio/Interior/scape_aicore_brain_loop.wav"), 0.0, LS)
brain = brain / np.sqrt((brain ** 2).mean()) * sw
swell = chord / np.sqrt((chord ** 2).mean()) + 0.5 * brain
for i_, semis in enumerate((-3, 0, 4, 9)):
    swell = place(swell, twinkle(C6 * 2 ** (semis / 12), 1.2, 160 + i_) * 0.6 * np.abs(swell).max() * (0.6 + 0.1 * i_), 0.5 + i_ * 0.42)
save("Ride/aicore_crystal_swell.wav", fade(swell, 0.05, 0.8), -14)


def purr(f0, dur, flutter=24.0, seed=0):
    """작은 드론 날개 "그르릉" — 기음 + 배음 옅게, flutter Hz로 빠르게 떨림 (소리 풍경 드론 그르릉 결). 주파수 · 떨림은 dur초에 정수 번 (루프용)."""
    t = np.arange(int(dur * SR)) / SR
    f0 = round(f0 * dur) / dur
    fl = round(flutter * dur) / dur
    x = sum(a * np.sin(2 * np.pi * k * f0 * t + k) for k, a in ((1, 1.0), (2, 0.55), (3, 0.3), (4, 0.18), (5, 0.1), (6, 0.06)))
    return x * (0.6 + 0.4 * np.sin(2 * np.pi * fl * t) ** 2)


# 드론을 타고 나는 동안 (2D 루프, 떠오름 · 날기 · 내려앉기 빠르기만큼) — 궤도 도시 옥상 바람 원본 (rooftop, 9초 고리 — 높은 곳 바람결) +
# 작은 날개 "그르릉" 합성 (소리 풍경 드론 그르릉 155 Hz 결에 맞춰 레3 · 24 Hz 떨림, 옅게). 새로 VARCO를 부르지 않음 — 같은 방 드론 · 같은 바람.
wind = loop_exact(sl(load(OC + "rooftop.wav"), 0.3, 10.24), 9.0)
wind = circular(lambda y: soften(highpass(y, 60), 5000, 0.4), wind)
pr = circular(lambda y: highpass(onepole_lp(y, 1500), 60), purr(146.83, 9.0))
ride = wind / np.sqrt((wind ** 2).mean()) + 0.35 * pr / np.sqrt((pr ** 2).mean())
save("Ride/aicore_droneride_loop.wav", ride, -20)   # −24는 음악보다 −7.3 (바람이 낮아 귀 감도로 작음) → +4 dB
report.append(f"  드론 바람 고리 {len(ride) / SR:.2f}초 · 이음새 {abs(ride[0] - ride[-1]):.4f}")

# 드론이 착륙장에 내려앉음 (드론 자리 3D) — 궤도 방어망 레버 "툭"을 3반음 올려 둥글게 + 날개 "그르릉"이 0.7초에 걸쳐 낮아지며 멎음.
h = hit_time(lt)
lt3 = fade(soften(highpass(pitch(sl(lt, max(0.0, h - 0.01), h + 0.7), 3), 80), 3000, 0.4), 0.003, 0.3)
td = np.arange(int(0.9 * SR)) / SR
down = sum(a * np.sin(2 * np.pi * k * np.cumsum(146.83 * (1 - 0.4 * np.minimum(1, td / 0.7))) / SR) for k, a in ((1, 1.0), (2, 0.5), (3, 0.25)))
down = fade(down * (0.6 + 0.4 * np.sin(2 * np.pi * 24 * td) ** 2) * np.exp(-td / 0.3), 0.01, 0.2)
land = np.zeros(int(1.1 * SR))
land = place(land, onepole_lp(down, 1500) * 0.5 * pk(lt3) / pk(down), 0.0)
land = place(land, lt3, 0.05)
save("Ride/aicore_drone_land.wav", fade(land, 0.0, 0.2), -11)   # −14는 음악보다 +0.3 (300 Hz 아래 68 %) → +3 dB

# 빛 구슬 주움 (2D, 모은 차례대로 음계 — 공용 점수 대신, 기준 = 도6) — 합성 "띠리링": 유리 종 도6 → 반짝임 솔6 → 도7 (으뜸 · 5도 · 옥타브만, 0.07초씩).
orb = np.zeros(int(1.3 * SR))
orb = place(orb, flower_bell(C6, 1.2, 170) * 0.8, 0.0)
orb = place(orb, twinkle(1.5 * C6, 0.9, 171) * 0.45, 0.07)
orb = place(orb, twinkle(2 * C6, 0.9, 172) * 0.35, 0.14)
save("Ride/aicore_orb_collect.wav", fade(onepole_lp(orb, 7000), 0.0, 0.3), -14)

# 시냅스 음 (시냅스 여섯 — 빛 알이 보여 줄 때 · 플레이어가 고를 때 둘 다, 2D, 시냅스 k = 도 · 레 · 파 · 솔 · 라 · 위 도) — 소리 풍경 수정 띵 원본 (C7 +6센트)을
# 한 옥타브 내려 정확한 도6 (그대로 도7이면 위 도 = 도8 4.2 kHz로 날카로움). 내리면 2배 길어져 1.6초에서 자름.
st = fade(soften(trim_lead(load(A + "crystal_ting.wav"), -40)[: int(1.8 * SR)], 4500, 0.5), 0.008, 0.8)
st = pitch(st, -12.06)[: int(1.6 * SR)]
save("Ride/aicore_synapse_note.wav", fade(st, 0.0, 0.5), -14)

# ================= 체험 — 항성 발전망 "정자 꽃 그네 · 벌 몰래 꿀 먹기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorFlowerSwing (F 박자로 굴러 수정 꽃송이 종 여섯) · InteriorHoneySneak (곰이 되어 경비 꿀벌 몰래 꿀). VARCO 하나씩 2번 (꿀 후루룩 · 곰 "앗").
# 꽃 종 = 소리 풍경 수정 꽃 종 (scape_stellar_flowerbell_01, 도6) 그대로 (새 파일 없음), 경비 벌 윙윙 · 놀란 "윙!" = 소리 풍경 벌 원본의 배음으로 다시 합성, 그네 휘익 = 합성.
SGR = "Ride/StellarGrid/"

# 그네 "휘익" (흔들림마다 가장 낮은 점을 지나는 순간 — 0.15초 앞에서 울려 가장 큰 곳이 바닥에 옴, 빠를수록 크게) — 합성: 걸러진 바람 잡음
# 0.25 ~ 0.9 kHz → 0.9 ~ 2.2 kHz로 넘어갔다 돌아옴 (지나가며 높아졌다 낮아짐), 0.5초.
save("Ride/sgrid_swing_whoosh.wav", onepole_lp(whoosh(0.5, 0.15, 250, 900, 2200, 180), 3000), -14)

# 경비 꿀벌 윙윙 (미니게임 동안 경비 벌에 붙은 3D 루프, 경고 · 쏘러 갈 때 높고 크게) — "제자리에서 맴도는 벌" 루프는 VARCO가 다섯 모두 지나가는 소리로 줌
# (소리 풍경 때 겪음) → 소리 풍경 벌 원본 (bee_b, F3 170 Hz)의 가장 큰 0.3초 배음 크기를 재서 F3 (174.5 Hz — 4초에 698번, 고리에 딱 맞음)로 다시 합성,
# 기음이 느리게 흔들리고 (±0.8 %) 날개 결 6 Hz · 날개 바람 쉿 옅게. 소리 풍경 벌처럼 150 Hz 아래를 걸러 날개 결 (배음)이 들리게.
BEE = tuple(-18.0 * np.log10(k) for k in range(1, 52))
# (bee_b 실제 배음 = 0 · −23 · −29 · −32 · −31 · −34 · −33 · −38 …: 그대로면 귀 감도로 거의 안 들리는 낮은 "웅" (300 Hz 아래 90 %) →
#  배음이 옥타브마다 5.4 dB씩만 줄게 (톱니 결 — 윙윙거리는 날개) 다시 맞춤. 2 ~ 12배음을 10 dB 올린 첫 판도 300 Hz 아래 90 %였음.)
gb = bee_buzz(174.5, 4.0, BEE, 0.008, 181)
gb = circular(lambda y: soften(highpass(highpass(y, 150), 150), 5000, 0.3), gb)
save("Ride/sgrid_bee_guard_loop.wav", gb, -21)   # −24는 4 m에서 음악보다 −5.3 → +3 dB (경고 때 또렷하게)
report.append(f"  경비 벌 고리 {len(gb) / SR:.2f}초 · 이음새 {abs(gb[0] - gb[-1]):.4f}")

# 꿀 한 숟갈 (2D, 숟갈마다 0.6초 — 공용 점수 "띵"과 겹쳐 울림) — 후보 5 중 3의 "후룹" (빨아들이는 한 결, crest 9.9 가장 둥긂)을 어둡게 (6.7 kHz → 3 kHz 아래로) +
# 2의 "냠" (입 다무는 작은 소리, 790 Hz) 0.28초에, 둘 다 2반음 올려 작고 귀엽게 · 0.55초 (다음 숟갈 전에 끝남).
# 다섯 모두 끈적한 입 딸깍이 이어진 소리 (지직 35 ~ 44) — 1 = 날 섬 (crest 20), 4 = 4 kHz 위 딸깍, 5 = 울림 9.
su = load(SGR + "slurp_suck.wav")
suck = fade(onepole_lp(onepole_lp(highpass(sl(su, 5.02, 5.45), 200), 3000), 3000), 0.04, 0.12)
sm = load(SGR + "slurp_smack.wav")
smack = sl(sm, 1.95, 2.6)
h = hit_time(smack)
smack = fade(soften(highpass(sl(smack, max(0.0, h - 0.01), h + 0.22), 150), 4000, 0.4), 0.003, 0.08)
slurp = np.zeros(int(0.6 * SR))
slurp = place(slurp, pitch(suck, 2) * 0.8 * pk(smack) / pk(suck), 0.0)
slurp = place(slurp, pitch(smack, 2), 0.28)
save("Ride/sgrid_honey_slurp.wav", fade(slurp[: int(0.56 * SR)], 0.0, 0.05), -14)

# 들킴 (2D — 공용 틀림 대신) — 경비 벌이 "윙!" (같은 벌 합성이 0.25초에 F3 → B♭3으로 치솟으며 부풂, 곰 코로 날아감) + 곰 "앗"
# (VARCO 5개 중 5의 첫 "이잇" — 라5 −2센트로 조 안 · 0.4초 가장 짧음 · 배경 디지털 무음, 코를 쏘는 0.4초에). 1 = 레6 0.6초 길게, 2 · 4 = 라#5 ~ 시5 (조 밖), 3 = 솔6 −26센트.
zip_ = bee_buzz(174.5, 0.5, BEE, 0.004, 182, glide=(1.335, 0.25))
tz = np.arange(len(zip_)) / SR
zip_ = soften(highpass(highpass(zip_, 150), 150), 5000, 0.3) * np.minimum(1, tz / 0.06) * np.exp(-np.maximum(0, tz - 0.28) / 0.08)
yl = load(SGR + "yelp.wav")
yelp = fade(highpass(trim_lead(sl(yl, 0.0, 0.5), -35), 200), 0.01, 0.12)[: int(0.4 * SR)]
caught = np.zeros(int(0.95 * SR))
caught = place(caught, fade(zip_, 0.0, 0.05) / pk(zip_), 0.0)
caught = place(caught, yelp / pk(yelp) * 0.9, 0.4)
save("Ride/sgrid_caught.wav", fade(caught, 0.0, 0.1), -14)

# ================= 체험 — 심우주 통신기지 "전파 접시 · 별똥별 아치 소원 · 신호 맞추기 · 별똥별 잡기" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorDishSignal (손바퀴로 접시 넷을 맞춰 신호를 모음) · InteriorWishArch (아치 앞 별똥별 소원) · InteriorSignalTune (무전 파형 맞추기) · InteriorStarCatch (별똥별 잡기).
# VARCO 1번 (접시 모터). 신호 = 소리 풍경 접시 신호 (scape_deep_signal_01, 도6) · 대표 선율 (scape_deep_motif), 소원 별똥별 = 궤도 방어망 별똥별 (scape_defense_star_01),
# 소원 시작 = 항성 발전망 수정 꽃 종을 한 옥타브 위로 (이 방 "강 위 유리 별"과 같은 소리) — 모두 새 파일 없음. 나머지는 합성.
DSR = "Ride/DeepSpaceComm/"

# 접시가 도는 모터 (손바퀴를 돌린 접시 자리 3D, 접시가 2.6초에 천천히 돌아섬 — 느리게 시작해 가운데가 가장 빠르고 천천히 섬) — 후보 5 중 4:
# 4.4초 내내 고름 · 울림 0 · 지직 9.3 (가장 낮음) · 1.1 ~ 1.2 kHz 부드러운 "위잉" 결. 접시 빠르기 곡선 (6t(1−t))대로 재생 빠르기 0.55 → 1 → 0.55 · 크기를 바꿔 2.9초 한 번 소리로.
# 1 = 바닥 −10 dB (잡음) · 울림 22, 2 = 118 Hz 낮은 웅 (울림 154), 3 = 울림 19 (톱니 떨림), 5 = 파#4 (조 밖) · 울림 107.
mo = highpass(sl(load(DSR + "dish_motor.wav"), 0.25, 4.3), 80)
TT = 2.6
n = int((TT + 0.3) * SR)
tm = np.arange(n) / SR
u = np.clip(tm / TT, 0, 1)
spd = np.where(tm < TT, 6 * u * (1 - u) / 1.5, 0.0)            # 0 ~ 1 (가운데 1)
rate = 0.55 + 0.45 * spd
pos = np.cumsum(rate)
pos = pos / pos[-1] * min(len(mo) - 2, pos[-1])
motor = np.interp(pos, np.arange(len(mo)), mo)
amp = (0.3 + 0.7 * spd ** 0.7) * np.minimum(1, tm / 0.12) * np.where(tm < TT, 1.0, np.exp(-(tm - TT) / 0.08))
motor = soften(soften(motor * amp, 4000, 0.5), 7000, 0.4)
# → 2026-11-25 실제 녹음 (차 선루프 모터)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역 — 빠르기 곡선 0.55 → 1 → 0.55가 사이렌 0.42로 들림) — 아래 줄은 기록
# save("Ride/dsc_dish_motor.wav", fade(motor, 0.0, 0.1), -14)

# 아치 소원 — 별똥별이 강에 닿아 별빛이 번짐 (2D 큰 순간) — 합성: 반짝임 스물넷이 F 오음계 파7 → 파5로 쏟아져 내림 (2.6초, 처음 빽빽 → 띄엄띄엄) +
# 유리 종 파5 · 라5 · 도6 화음이 부풀었다 잦아듦 + 옅은 "샤라락" (2 ~ 6 kHz 잡음이 반짝임을 따라 잦아듦).
fsc = [349.23 * 2 ** (s / 12) for s in (12, 14, 16, 19, 21, 24, 26, 28, 31, 33, 36)]   # 파5 · 솔5 · 라5 · 도6 · 레6 · 파6 · 솔6 · 라6 · 도7 · 레7 · 파7
wish = np.zeros(int(3.6 * SR))
rng = np.random.default_rng(190)
for i_ in range(24):
    at = 2.6 * (i_ / 24) ** 1.4
    k = int(round((1 - i_ / 23) * (len(fsc) - 1) + rng.integers(-1, 2)))
    k = int(np.clip(k, 0, len(fsc) - 1))
    wish = place(wish, twinkle(fsc[k], 0.9, 191 + i_) * (0.35 - 0.006 * i_), at)
for i_, f in enumerate((698.46, 880.0, 1046.5)):
    wish = place(wish, flower_bell(f, 3.0, 220 + i_) * 0.3, 0.05 + 0.04 * i_)
tw = np.arange(len(wish)) / SR
shh = onepole_lp(highpass(rng.standard_normal(len(tw)), 2000), 6000) * np.exp(-tw / 0.9) * np.minimum(1, tw / 0.05)
wish += 0.05 * np.abs(wish).max() * shh / np.abs(shh).max()
save("Ride/dsc_wish_sparkle.wav", fade(onepole_lp(wish, 8000), 0.0, 0.8), -14)

# 신호 맞추기 무전 잡음 (조작대에 선 동안 2D 루프 — 크기 = 1 − 맞은 정도: 맞을수록 잡음이 걷힘) — 합성: 0.4 ~ 3.2 kHz로 걸러진 잡음이 느리게 일렁임 (0.5 · 1.25 Hz) +
# 아주 옅은 "지직" 열 개 (4초 고리에, 3 ms 짧은 톡 · 2.5 kHz 아래로 둥글게 — 잡음 최대의 15 %). 4초 = 흔들림 주기가 정수 번이라 고리에 딱 맞음.
LT = 4.0
tt = np.arange(int(LT * SR)) / SR
rng = np.random.default_rng(200)
nz = circular(lambda y: onepole_lp(onepole_lp(highpass(highpass(y, 400), 400), 3200), 3200), rng.standard_normal(len(tt)))
nz = nz / np.abs(nz).max() * (0.75 + 0.15 * np.sin(2 * np.pi * 0.5 * tt) + 0.1 * np.sin(2 * np.pi * 1.25 * tt + 1.0))
for i_ in range(10):
    at = (i_ + rng.uniform(0.1, 0.9)) * LT / 10
    tk = onepole_lp(rng.standard_normal(int(0.004 * SR)), 2500) * np.exp(-np.arange(int(0.004 * SR)) / (0.0012 * SR))
    tk = tk / np.abs(tk).max() * 0.15
    i0 = int(at * SR)
    nz[i0:i0 + len(tk)] += tk[: len(nz) - i0]
save("Ride/dsc_static_loop.wav", nz, -24)
report.append(f"  무전 잡음 고리 {len(nz) / SR:.2f}초 · 이음새 {abs(nz[0] - nz[-1]):.4f}")

# 또렷하게 맞춤 (2D — 85점 이상 = 크게 · 연달아 맞출수록 위 음, 아래 = 작게 도 — 공용 점수 · UiClick 대신, 기준 = 도6) — 합성: 신호 "삐 · 빕" (소리 풍경 접시 신호 틀) 도6 +
# 유리 종 도6 · 반짝임 도7 (으뜸 · 옥타브만 — 음계로 옮겨도 조가 안 깨짐). 잡음 루프가 걷힌 뒤 맑은 신호가 남는 느낌.
lock = np.zeros(int(1.4 * SR))
lock = place(lock, signal_beep(C6, 201) * 0.7, 0.0)
lock = place(lock, flower_bell(C6, 1.2, 202) * 0.8, 0.02)
lock = place(lock, twinkle(2 * C6, 0.8, 203) * 0.2, 0.05)
save("Ride/dsc_tune_lock.wav", fade(lock, 0.0, 0.3), -14)

# 별똥별 잡음 (2D, 연달아 잡으면 음계로 — 공용 점수 대신, 기준 = 도6, 큰 별은 1.3배) — 합성: 짧은 "슉" (잡아챔, 1 ~ 4 kHz 40 ms) + 반짝임 도6 · 솔6 · 도7 · 솔7이 한꺼번에 "챵".
sc = np.zeros(int(1.0 * SR))
sc = place(sc, whoosh(0.08, 0.03, 1000, 2500, 4500, 210) * 0.12, 0.0)
for i_, (mul, g_, at) in enumerate(((1, 0.7, 0.02), (1.5, 0.35, 0.03), (2, 0.3, 0.04), (3, 0.12, 0.06))):
    sc = place(sc, twinkle(C6 * mul, 0.85, 211 + i_) * g_, at)
save("Ride/dsc_star_catch.wav", fade(onepole_lp(sc, 7500), 0.0, 0.25), -14)

# ================= 체험 — 2단계 공통 도구 (SOUND_LIST_NEW 5-2 셋째 묶음, 2026-11-23) =================
# 2단계 톤 = 밝음 · 여가 테마파크 (아기자기, 날 선 쇳소리 X). 2단계 방은 소리 풍경 없이 방 환경음 + 내부 음악 2단계 곡 (D 단조 · F 장조, 90 BPM).
pk50 = lambda y: np.sqrt(np.convolve(y ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "valid").max())
rms = lambda y: np.sqrt((y ** 2).mean()) + 1e-12


def best_loop(x, lo, hi, win=1.0):
    """루프 길이 고르기: 앞 win초의 크기 곡선과 가장 닮은 lo ~ hi초 뒤 자리 (리듬이 있는 소리 — 칙칙폭폭 — 이음새에서 박이 안 어긋나게)."""
    w = int(0.005 * SR)
    env = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "same"))[::w]
    n = int(win / 0.005)
    a = env[:n] - env[:n].mean()
    best, at = -2.0, lo
    for k in range(int(lo / 0.005), int(hi / 0.005)):
        b = env[k:k + n] - env[k:k + n].mean()
        c = (a * b).sum() / (np.sqrt((a ** 2).sum() * (b ** 2).sum()) + 1e-12)
        if c > best:
            best, at = c, k * 0.005
    return at, best


def varlp(x, fc):
    """시간에 따라 바뀌는 1차 로우패스 (fc = 샘플마다 Hz) — 불이 꺼지듯 소리가 먹먹해짐."""
    a = np.exp(-2 * np.pi * np.asarray(fc) / SR)
    y = np.empty_like(x)
    s = 0.0
    for i in range(len(x)):
        s = (1 - a[i]) * x[i] + a[i] * s
        y[i] = s
    return y


# ================= 체험 — 대형 토러스 "은하철도 링 기차" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorTrain (고가 선로를 돌며 역 넷에 섬 — 문 닫힘 0.9초 → 6.5초 달림 → 도착). 궤도 도시 금빛 궤도열차 (전기 "웅" · 공기 브레이크)와 갈리게 증기 · 칙칙폭폭 쪽.
# VARCO 하나씩 3번 (기적 · 달림 · 증기).
GTR = "Ride/GrandTorus/"

# 출발 기적 (문이 닫히기 시작하는 순간 — 역 근처 · 탄 사람 2D) — 후보 5 중 2: 처음부터 끝까지 F5 ±10센트로 또렷 (조 안 — 옮기지 않음) · 바닥 −51 dB.
# 긴 "뿌우—" (0.1 ~ 2.1초) 하나 + 짧은 "뽀" (3.0초)를 짧은 "뽀" → 긴 "뽀오—"로 이어 장난감 기차 "뽀 뽀오—" (1.8초).
# 1 = 첫 음이 D5 +46 → E5로 올라가며 흔들림, 3 = D#5 (조 밖) · 바닥 −35 dB, 4 = F5 · C5 두 음이 좋지만 바닥 −22 dB (잡음), 5 = B4 (조 밖).
wh = load(GTR + "whistle.wav")
toot_a = fade(highpass(trim_lead(sl(wh, 2.95, 3.6), -35), 200)[: int(0.38 * SR)], 0.015, 0.12)
toot_b = fade(highpass(trim_lead(sl(wh, 0.05, 1.5), -35), 200), 0.015, 0.5)
whistle = np.zeros(int(2.0 * SR))
whistle = place(whistle, toot_a * 0.85, 0.0)
whistle = place(whistle, toot_b, 0.48)
save("Ride/torus_whistle.wav", fade(soften(whistle, 6000, 0.35), 0.0, 0.1), -14)

# 달림 (탄 동안 2D 루프, 빠르기만큼 크기 · 재생 높이) — 후보 5 중 5: 0.5초 크기가 −20 ~ −25 dB로 가장 고르고 칙칙폭폭이 가장 규칙적 (주기 0.26초 · 자기상관 0.43).
# 2 = 고르지만 6.3초로 짧고 칙칙이 흐림 (0.32), 3 = 크기 −31 ~ −20 흔들림, 1 = 밝은 딸깍 (지직 31), 4 = 끝으로 갈수록 잦아듦 (−13 → −33).
# 루프 길이는 앞 1초의 칙칙 무늬와 가장 닮은 뒤 자리 (7 ~ 8.6초)로 — 이음새에서 박이 안 어긋나게.
tr = sl(load(GTR + "train_run.wav"), 0.4, 9.6)
L_run, c_run = best_loop(tr, 7.0, 8.2, 0.9)
run = circular(lambda y: soften(highpass(y, 60), 6000, 0.3), loop_exact(tr, L_run))
save("Ride/torus_run_loop.wav", run, -22)
report.append(f"  은하철도 달림 고리 {L_run:.2f}초 (닮음 {c_run:.2f}) · 이음새 {abs(run[0] - run[-1]):.4f}")

# 도착 증기 "쉬익" (역에 서는 순간 — 역 근처 · 탄 사람 2D) — "부풀었다 잦아드는 쉬익"을 불렀는데 다섯 모두 몇 초 동안 고른 쉿 (무게중심 5.8 ~ 12.8 kHz) →
# 가장 어두운 5 (4 kHz 위 49 %)의 고른 곳 1.8초를 0.08초에 부풀었다 1.2초에 잦아들게 · 3.5 kHz 위를 두 번 눌러 "칙"이 아닌 부드러운 "쉬익"으로.
# 1 · 2 · 4 = 4 kHz 위 88 ~ 99 % (날카로운 쉿), 3 = 바닥 −11 dB.
hs = highpass(sl(load(GTR + "steam_hiss.wav"), 3.5, 5.3), 250)
th_ = np.arange(len(hs)) / SR
hs = hs * (1 - np.exp(-th_ / 0.08)) * np.exp(-np.maximum(0, th_ - 0.15) / 0.45)
save("Ride/torus_brake.wav", fade(soften(soften(hs, 3500, 0.6), 3500, 0.6), 0.005, 0.3), -14)

# ================= 체험 — 식량 공장 "유리 바퀴 관람차" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorFerrisWheel (바퀴 둘 — 곤돌라 한 칸 72°를 7초에 돌고 섬, 맨 아래에서 다리 · 문이 0.9초에 열림 · 닫힘). VARCO 0번 —
# 문 "스르륵" = 궤도 항만 조개 "스르륵" 원본, "딸깍" = 5-1 탑승 걸쇠 원본, 삐걱 = 궤도 항만 선착장 삐걱임 원본, 바람 = 궤도 도시 옥상 바람 원본.
# 곤돌라 유리문 (열릴 때 · 닫힐 때 같은 소리, 바퀴 가까이 · 탄 사람 2D) — 조개 "스르륵" (0.45초)을 3반음 내려 큰 유리문 결로 + 문이 끝에 닿는 0.8초에 걸쇠 "딸깍"을 2반음 올려 가볍게.
cs_ = load("Ride/OrbitalPort/clam_slide.wav")
slide = pitch(fade(highpass(trim_lead(sl(cs_, 0.0, 0.6), -35), 150)[: int(0.5 * SR)], 0.01, 0.2), -3)
lt_ = load(RD + "latch.wav")
click = pitch(fade(soften(highpass(sl(lt_, 1.0, 1.32), 120), 4000, 0.5), 0.004, 0.08), 2)
door = np.zeros(int(1.2 * SR))
door = place(door, soften(slide, 5000, 0.4), 0.0)
door = place(door, click * 0.7 * pk50(slide) / pk50(click), 0.8)
save("Ride/ferris_door.wav", fade(door, 0.0, 0.1), -14)

# 도는 동안 (탄 사람 2D 루프, 바퀴 빠르기만큼) — 아주 느린 삐걱 + 높은 곳 바람. 삐걱 = 선착장 삐걱임 원본 8.2초 고리를 2반음만 올려 (소리 풍경 선착장 = 5반음 — 그보다 낮고 느리게, 7.3초),
# 바람 = 궤도 도시 옥상 바람 원본을 같은 길이 고리로. 삐걱 60 % · 바람 40 %.
cr = pitch(loop_exact(sl(load("Interior/scape_port/dock_creak.wav"), 0.0, 9.1), 8.2), 2)
cr = circular(lambda y: soften(highpass(highpass(y, 150), 150), 4000, 0.4), cr)
wd = loop_exact(sl(load(OC + "rooftop.wav"), 0.3, 10.24), (len(cr) + 0.5) / SR)
wd = circular(lambda y: soften(highpass(y, 60), 5000, 0.4), wd)[: len(cr)]
creak = 0.6 * cr / rms(cr) + 0.4 * wd / rms(wd)
save("Ride/ferris_creak_loop.wav", creak, -16)   # −22는 녹음 검수에서 음악보다 −10 (낮은 삐걱 · 바람이라 귀 감도로 작음) → +6 dB
report.append(f"  관람차 삐걱 고리 {len(creak) / SR:.2f}초 · 이음새 {abs(creak[0] - creak[-1]):.4f}")

# ================= 체험 — 저장 단지 "별빛 회전목마" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorCarousel (12초 서 있다가 두 바퀴 + 120°를 가속 · 감속하며 돎). VARCO 0번 — 종 · 오르골 모두 F 장조 합성.
# 출발 종 (회전목마 가까이 · 탄 사람 2D, 멈출 때는 작게 한 번) — 유리 종 파5 두 번 + 도6 한 번 "딩 딩 딩—" (0.16초 간격, 마지막 길게).
bell = np.zeros(int(2.6 * SR))
for i, (f, g_, d) in enumerate(((698.46, 0.7, 1.0), (698.46, 0.75, 1.0), (1046.5, 1.0, 2.2))):
    bell = place(bell, flower_bell(f, d, 300 + i) * g_, i * 0.16)
bell = place(bell, flower_bell(349.23, 2.2, 303) * 0.3, 0.32)
save("Ride/carousel_bell.wav", fade(bell, 0.0, 0.5), -14)


def music_box(f, dur=1.8, seed=0):
    """오르골 빗살 한 음 — 기음 + 2배 · 3배 옅게 (빨리 꺼짐) + 아주 짧은 높은 결 (빗살이 튕기는 "틱"), 2 ms에 켜지고 길게 남음."""
    t = np.arange(int(dur * SR)) / SR
    x = (np.sin(2 * np.pi * f * t) * np.exp(-t / 0.75) + 0.28 * np.sin(2 * np.pi * 2 * f * t + 0.5) * np.exp(-t / 0.28)
         + 0.1 * np.sin(2 * np.pi * 3 * f * t + 1.3) * np.exp(-t / 0.14) + 0.06 * np.sin(2 * np.pi * 5.93 * f * t + seed) * np.exp(-t / 0.04))
    rng = np.random.default_rng(seed)
    tick = highpass(rng.standard_normal(len(t)), 2500) * np.exp(-t / 0.0015) * 0.04
    return fade(onepole_lp((x + tick) * (1 - np.exp(-t / 0.002)), 8000), 0.0, 0.3)


# 오르골 (회전목마에 붙은 3D 루프 — 도는 동안 크기 1 · 서면 0) — F 오음계 선율 12마디 (48박 = 32초, 90 BPM = BEAT 격자):
# 내부 음악 (96초 = 144박)의 정확히 1/3 길이라 음악이 한 바퀴 돌 때마다 같은 자리로 맞물림 — ObjectLoop.SyncBeat로 음악 박에 맞춰 시작.
# 선율 = 파5 ~ 파6 (오르골 높이), 반주 = 한 박마다 낮은 부서진 화음 (F · Dm · C — 3도 미 없이 도 · 솔), 모두 F 장조 음 (음악 화음 위에서 안 부딪힘).
MB = 12 * 4
mel = [  # (박, 음 MIDI, 길이 박)
    (0, 84, 1), (1, 81, .5), (1.5, 84, .5), (2, 86, 1), (3, 84, 1),
    (4, 81, 1), (5, 79, .5), (5.5, 81, .5), (6, 77, 2),
    (8, 79, 1), (9, 81, .5), (9.5, 84, .5), (10, 86, 1), (11, 89, 1),
    (12, 86, 1), (13, 84, 1), (14, 81, 2),
    (16, 84, 1), (17, 81, .5), (17.5, 84, .5), (18, 86, 1), (19, 84, 1),
    (20, 89, 1), (21, 86, .5), (21.5, 84, .5), (22, 81, 2),
    (24, 79, 1), (25, 81, .5), (25.5, 79, .5), (26, 77, 1), (27, 74, 1),
    (28, 77, 1), (29, 79, 1), (30, 81, 2),
    (32, 86, 1.5), (33.5, 84, .5), (34, 81, 1), (35, 84, 1),
    (36, 86, 1), (37, 89, 1), (38, 86, 1), (39, 84, 1),
    (40, 81, 1), (41, 84, .5), (41.5, 81, .5), (42, 79, 1), (43, 81, .5), (43.5, 79, .5),
    (44, 77, 3),
]
CH = {"F": (65, 72, 69, 72), "Dm": (62, 69, 65, 69), "C": (60, 67, 72, 67)}
bars = ("F", "F", "Dm", "F", "F", "Dm", "C", "F", "Dm", "Dm", "C", "F")
mf = lambda m: 440.0 * 2 ** ((m - 69) / 12)
LMB = MB * BEAT
box = np.zeros(int((LMB + 3.0) * SR))
for i, (b, m, d) in enumerate(mel):
    box = place(box, music_box(mf(m), 1.4 + 0.5 * d, 400 + i) * (0.8 if d < 1 else 0.9), b * BEAT)
for k, c in enumerate(bars):
    for j, m in enumerate(CH[c]):
        box = place(box, music_box(mf(m), 1.6, 500 + 4 * k + j) * (0.34 if j == 0 else 0.24), (4 * k + j) * BEAT)
n_ = int(round(LMB * SR))
tail = box[n_:]
box = box[:n_].copy()
box[: len(tail)] += tail   # 마지막 음 꼬리를 처음으로 (고리)
save("Ride/carousel_music_loop.wav", box, -24)
report.append(f"  회전목마 오르골 {len(box) / SR:.3f}초 = {len(box) / SR / BEAT:.2f}박 · 이음새 {abs(box[0] - box[-1]):.4f}")

# ================= 체험 — 화물 환적장 "은하수 후룸" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorFlumeRide (보트 셋이 물길 한 바퀴 — 체인 리프트 오르막 · 큰 낙하 · 물보라 못 · 터널 · 항구 정차장). VARCO 하나씩 2번 (체인 · 물보라).
# 내리막 · 물길 물살 = 바이오스피어 수문 폭포 루프 그대로 (새 파일 없음), 도착 = 궤도 항만 노 첨벙 원본 + 궤도 방어망 레버 "툭".
CTR = "Ride/CargoTransfer/"

# 체인 리프트 오르막 (탄 사람 2D 루프, 리프트 위에 있는 동안) — "느린 둥근 래칫 딸깍 · 1초에 네 번"으로 불러 다섯 중 3만 딸깍이 하나씩 떨어져 들림
# (나머지 = 잔 딸깍이 빽빽함 · 4 = 4.7초 · 1 = 9 kHz 쇳소리 울림 46) → 3의 가장 둥근 "딸깍" 넷 (무게중심 1.7 ~ 2.2 kHz · crest 14 ~ 17)을
# 반 박 (0.33초)이 아니라 0.5초마다 차례로 놓아 4초 고리 (느린 체인) + 바이오스피어 물길 졸졸을 30 %로 밑에 (오르막 물통에 물이 흐름).
ch_ = load(CTR + "lift_chain.wav")
clacks = []
for t0 in (0.89, 1.27, 3.19, 0.07):
    c_ = sl(ch_, max(0.0, t0 - 0.01), t0 + 0.3)
    clacks.append(fade(tame(soften(soften(highpass(c_, 120), 4000, 0.5), 7000, 0.4), 6.0), 0.003, 0.12))
LC = 4.0
chain = np.zeros(int(LC * SR))
for i in range(8):
    c_ = clacks[i % 4] / pk50(clacks[i % 4]) * (1.0 if i % 2 == 0 else 0.8)
    idx = (int(i * 0.5 * SR) + np.arange(len(c_))) % len(chain)
    np.add.at(chain, idx, c_)
bk = loop_exact(sl(load(B + "brook.wav"), 0.6, 5.5), LC)
bk = circular(lambda y: highpass(y, 120), bk)
chain = chain + 0.3 * rms(chain) / rms(bk) * bk
save("Ride/flume_chain_loop.wav", chain, -24)
report.append(f"  후룸 체인 고리 {len(chain) / SR:.2f}초 · 이음새 {abs(chain[0] - chain[-1]):.4f}")

# 큰 낙하 물보라 (탄 보트가 낙하 끝 물보라 자리를 지나는 순간 2D · 관람석에서 보면 그 자리 3D) — 후보 5 중 1: 0.3초에 바로 "촤악" → 1.7초 동안 물방울이 쏟아져 잦아듦,
# 바닥 −54 dB (가장 깨끗) · crest 7 (가장 둥긂). 3 = 1초에 걸쳐 부풂 (물보라 순간과 안 맞음), 5 = 2 ~ 3초에야 큼, 2 = 9초 내내 물소리, 4 = 시작 앞이 지저분.
sp_ = load(CTR + "big_splash.wav")
splash = fade(soften(highpass(sl(sp_, 0.2, 2.7), 80), 5000, 0.35), 0.01, 0.6)
save("Ride/flume_big_splash.wav", splash, -14)

# 정차장 도착 (탄 보트가 서는 순간 — 정차장 근처 · 탄 사람 2D, SupplyArrive 대신) — 물에 배가 닿는 "철썩" (궤도 항만 노 첨벙 원본) + 0.15초에 나무 "툭" (궤도 방어망 레버 "툭"을 2반음 올려).
pa_ = load("Ride/OrbitalPort/paddle_a.wav")
slap = fade(highpass(trim_lead(sl(pa_, 0.3, 2.6), -32), 100)[: int(0.9 * SR)], 0.01, 0.35)
lt2 = load("Ride/OrbitalDefense/lever_thunk.wav")
h2 = hit_time(lt2)
thk = pitch(fade(highpass(sl(lt2, max(0.0, h2 - 0.012), h2 + 0.7), 80), 0.003, 0.3), 2)
dock2 = np.zeros(int(1.1 * SR))
dock2 = place(dock2, slap / pk50(slap) * 0.8, 0.0)
dock2 = place(dock2, soften(thk, 3500, 0.4) / pk50(thk), 0.15)
save("Ride/flume_dock.wav", fade(dock2, 0.0, 0.15), -14)

# ================= 체험 — 연구원 "별빛 데이터 미로" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorMaze (입구 아치를 지나면 시간을 재고 가운데 연구실에 들어서면 도착). VARCO 0번 — 미로 안 웅 = 궤도 방어망 수정 포대 루프 그대로 (낮은 웅웅 + 도 오음계 반짝임, 새 파일 없음).
# 출구 (가운데 연구실 도착 2D — 결과음 성공 · 새 기록 0.5초 앞) — 유리 아르페지오 도6 · 레6 · 파6 · 라6 (0.07초씩 굴러 오름) + 반짝임 도7: "데이터 문이 열림".
ex = np.zeros(int(1.6 * SR))
for i, f in enumerate((1046.5, 1174.66, 1396.91, 1760.0)):
    ex = place(ex, flower_bell(f, 1.2, 600 + i) * (0.7 + 0.08 * i), i * 0.07)
ex = place(ex, twinkle(2093.0, 0.9, 605) * 0.3, 0.3)
save("Ride/maze_exit.wav", fade(onepole_lp(ex, 7500), 0.0, 0.4), -14)

# ================= 체험 — 드론 정비창 "드론 라이트 쇼" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorDroneShow (버튼 · 관람석 → 하늘 화면에 드론이 떠올라 대형 다섯). VARCO 0번 — 쇼 시작 = 수리 사령부 "드론 떼 날아오름" 반짝임 그대로 (새 파일 없음),
# 쇼 동안 드론 떼 웅 = 합성 (AI 중추 드론 "그르릉" 틀을 F 장조 음 여섯에 겹침). 쇼 음악은 범위 밖 (SOUND_LIST 7번).
# 드론 떼 (쇼 동안 2D 루프) — 작은 드론 열둘: 파3 · 라3 · 도4 · 파4 · 라4 · 도5 (F 장조 으뜸화음 — 음악과 안 부딪힘)마다 ±6센트 둘,
# 날개 떨림 19 ~ 31 Hz가 저마다 달라 "윙윙" 떼 결, 크기는 1 ~ 3번 느리게 오르내림 (16초 = 24박에 정수 번 — 고리에 딱 맞음) + 옅은 바람결.
LDS = 16.0
td = np.arange(int(LDS * SR)) / SR
swarm = np.zeros_like(td)
rng = np.random.default_rng(700)
for j, (f0, amp) in enumerate(((174.61, 0.7), (220.0, 0.8), (261.63, 1.0), (349.23, 0.9), (440.0, 0.6), (523.25, 0.4))):
    for c in (-6, 6):
        d_ = purr(f0 * 2 ** (c / 1200), LDS, flutter=19 + 12 * rng.random(), seed=j)
        swell = 0.6 + 0.4 * np.sin(2 * np.pi * (1 + (j + (c > 0)) % 3) * td / LDS + rng.uniform(0, 6.28))
        swarm += amp * d_ / rms(d_) * swell
air = circular(lambda y: onepole_lp(highpass(y, 800), 3000), rng.standard_normal(len(td)))
swarm = circular(lambda y: highpass(onepole_lp(y, 2500), 80), swarm)
swarm = swarm / rms(swarm) + 0.12 * air / rms(air)
# → 2026-11-25 실제 녹음 (RC 헬기 여섯 겹)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역 — 합성 떼는 Music 0.79 · Piano로 들림) — 아래 줄은 기록
# save("Ride/droneshow_swarm_loop.wav", swarm, -24)
# report.append(f"  드론 떼 고리 {len(swarm) / SR:.2f}초 · 이음새 {abs(swarm[0] - swarm[-1]):.4f}")

# ================= 체험 — 요새 포대 "폭죽놀이 직관" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorFireworkShow (쇼 레버 → 24초 동안 0.5 ~ 1초마다 쏨, 포구에서 0.8초 뒤 하늘 화면에 터짐 · 평소에도 가끔). VARCO 0번 — 모두 합성 (레버 = 궤도 방어망 레버 조각).
# 쏨 "쉬익" (포구 자리 3D, 0.8초 뒤 터짐) — 걸러진 바람이 낮은 띠 → 높은 띠로 올라감 (0.5 ~ 4.5 kHz, 0.78초) + 불티 "칙" 몇 개 옅게.
t = np.arange(int(0.82 * SR)) / SR
rng = np.random.default_rng(710)
nz = rng.standard_normal(len(t))
lo_ = onepole_lp(onepole_lp(highpass(nz, 350), 1100), 1100)
mi_ = onepole_lp(onepole_lp(highpass(nz, 1100), 2400), 2400)
hi_ = onepole_lp(onepole_lp(highpass(nz, 2400), 4500), 4500)
u = np.clip(t / 0.78, 0, 1)
launch = (lo_ / rms(lo_) * np.clip(1 - 2 * u, 0, 1) + mi_ / rms(mi_) * (1 - np.abs(2 * u - 1)) + hi_ / rms(hi_) * np.clip(2 * u - 1, 0, 1) * 0.7)
launch *= np.minimum(1, t / 0.08) * (0.6 + 0.4 * u) * np.where(t < 0.7, 1.0, np.exp(-(t - 0.7) / 0.04))
for k in range(7):
    at = rng.uniform(0.1, 0.65)
    g = onepole_lp(highpass(rng.standard_normal(int(0.004 * SR)), 1500), 5000) * np.exp(-np.arange(int(0.004 * SR)) / (0.001 * SR))
    launch = place(launch, g / np.abs(g).max() * 0.5 * np.abs(launch).max(), at)
# → 2026-11-24 이 "쉬익"만으로는 잡음 · 파도처럼 들려 아래 (터짐 다음)에서 실제 발사통 "퉁" 위에 짧고 옅게 얹음

# 터짐 (하늘 화면 불꽃 — 2D, 재생 높이 ±10 %, 넷 중 하나씩) — 2026-11-24 사용자 "효과음이 전반적으로 이상, 폭죽 소리가 대표" → 실제 녹음과 같은 눈금으로 재 보니
# 첫 판 합성 "팡 + 지지직"은 125 ~ 500 Hz 몸통이 비고 (45 Hz 순음만) 4 ~ 16 kHz가 실제보다 10 ~ 25 dB 밝고 지지직이 초당 19개 (실제 2 ~ 8) · 꼬리 1.6초 (실제는 3초 넘게 하늘 울림).
# → 실제 공중 폭죽 녹음 (archive.org "The Designer's Choice — FIREWORKS", CC0 — Ride/Fortress/SOURCE.txt) 3분 연발에서 자른 터짐 42개 중
# 앞이 조용하고 (−22 ~ −29 dB) 6 kHz 위 바닥이 낮고 (−48 ~ −56 dB) 다음 큰 터짐과 1초 넘게 떨어진 넷. 다음 터짐 직전에 자르고 하늘 울림 꼬리를 이어 붙임,
# 날 선 첫 순간만 4 dB 눌러 아기자기 톤으로 (몸통 · 울림은 그대로).
FW = "Ride/Fortress/"


def fft_conv(x, h):
    n = len(x) + len(h) - 1
    m = 1 << (n - 1).bit_length()
    return np.fft.irfft(np.fft.rfft(x, m) * np.fft.rfft(h, m), m)[:n]


for k, (name, end) in enumerate((("show_06", 2.6), ("show_09", 1.45), ("show_95", 1.5), ("show_02", 1.05))):
    x = highpass(sl(load(FW + f"real_burst_{name}.wav"), 0.19, end), 40)   # 터짐 = 0.193초 (조각마다 같음)
    x = fade(x, 0.007, min(0.35, len(x) / SR * 0.3))   # 시작 10 → 90 % 약 4 ms (실제 중앙값 — 바로 자르면 1.5 ms로 날 섬)
    # 하늘 울림 — 잘라 낸 뒤를 이어 줌 (1.8초 지수 꼬리 잡음, 2.5 kHz 위 누름, 몸통 최대의 22 %)
    rng = np.random.default_rng(730 + k)
    tt = np.arange(int(1.8 * SR)) / SR
    wet = fft_conv(x, onepole_lp(rng.standard_normal(len(tt)), 2500) * np.exp(-tt / 0.45))
    wet *= 0.22 * np.abs(x).max() / (np.abs(wet).max() + 1e-12)
    y = np.concatenate([x, np.zeros(len(wet) - len(x))]) + wet
    y, note = match_tilt(np.pad(y, (0, int(0.5 * SR))))   # 한 번 소리도 루프와 같은 고음 상한
    # 녹음 검수 (요새 안, 음악 대비 귀 감도): 넷 다 −12면 +4.6 · +4.9 · +1.0 · +4.6 → 셋째 (show_95)는 첫 순간만 뾰족해 최대 −1 dB에 막혀
    # −10으로도 안 커짐 (crest 12.3, tame은 5 ms 단위라 첫 1 ms가 빠져나감) → 1 ms 앞을 내다보는 최대치 누르기 6 dB 뒤 −10으로
    y = tame(trim_lead(y[: len(y) - int(0.5 * SR)]), 4.0)
    if name == "show_95":
        w = int(0.001 * SR)
        pk = np.lib.stride_tricks.sliding_window_view(np.pad(np.abs(y), (w, w), mode="edge"), 2 * w + 1).max(1)
        y = y * np.convolve(np.minimum(1.0, np.abs(y).max() * 10 ** (-6 / 20) / (pk + 1e-12)), np.ones(w) / w, mode="same")
    save(f"Ride/firework_burst_{k + 1:02d}.wav", fade(y, 0.0, 0.4), -10 if name == "show_95" else -12)
    report.append(f"  폭죽 터짐 {k + 1} = 실제 {name} {end - 0.19:.2f}초 + 울림{note}")

# 쏨 (포구 자리 3D, 0.8초 뒤 터짐) — 2026-11-24 실제 녹음 · AI 청각 모델 (PANNs) 비교: 첫 판 0.8초 "쉬익"은 혼자서 잡음 · 쉭 · 파도 (폭죽 0)로 들리고,
# 쇼처럼 10초 연발에 넣으면 폭죽 점수를 0.83 → 0.44 ~ 0.52로 깎음. 실제 공중 폭죽은 발사통 "퉁" (200 Hz 아래 36 ~ 53 %, 0.07초) 뒤 조용히 오름
# → 같은 녹음의 앞이 조용한 (−42 dB) "퉁" + 첫 판 쉬익을 0.35초 · 2.5 kHz 위 누르고 퉁 최대의 12 %로. 둘째 후보 show_50은 200 Hz 아래만 있어
# 녹음 검수에서 음악보다 −11.6 (첫째 +4.5) — 귀에 안 들려 뺌, 하나를 재생 높이 ±6 %로 돌려 씀.
for k, name in enumerate(("show_47",)):
    th = fade(highpass(sl(load(FW + f"real_launch_{name}.wav"), 0.17, 0.75), 35), 0.002, 0.25)
    air = onepole_lp(launch[int(0.05 * SR):int(0.40 * SR)], 2500)
    air = fade(air / (np.abs(air).max() + 1e-12), 0.06, 0.15) * 0.12 * np.abs(th).max()
    # 녹음 검수: 퉁은 거의 200 Hz 아래라 귀 감도로 터짐보다 −21 dB (−14 · 음량 0.25) → 파일 −10 · 음량 0.7로 터짐보다 약 −8 (낮은 소리라 귀 감도보다 크게 느껴짐)
    save(f"Ride/firework_launch_{k + 1:02d}.wav", place(th, air, 0.02), -10)

# 쇼 레버 (레버 자리 3D — 레버는 0.2초에 끝까지 당겨짐) — 궤도 방어망 레버와 같은 나무 결: 선착장 삐걱 0.2초를 3반음 올려 작게 + 레버 "툭"을 0.2초에.
crk = pitch(sl(load("Interior/scape_port/dock_creak.wav"), 7.25, 7.5), 3)
crk = fade(soften(highpass(crk, 150), 4000, 0.4), 0.01, 0.06)
thk2 = fade(highpass(sl(lt2, max(0.0, h2 - 0.012), h2 + 0.85), 60), 0.003, 0.35)
lever = np.zeros(int(1.2 * SR))
lever = place(lever, crk * 0.5 * pk50(thk2) / pk50(crk), 0.0)
lever = place(lever, thk2, 0.2)
save("Ride/firework_lever.wav", fade(lever, 0.0, 0.2), -14)

# ================= 체험 — 그리드 허브 "번개 정원" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorLightning (테슬라 탑 에너지 구 → 번개 나무 여섯으로 저마다 0.3 ~ 1.7초마다 내리침). VARCO 1번 (정전기).
GHR = "Ride/GridHub/"
# 번개 "찌직 · 퐁" (번개가 닿는 나무 자리 3D, 음계 칸을 골라 도 · 레 · 파 · 솔 · 라) — "작고 귀여운 정전기 찌직 + 퐁"을 불렀는데 다섯 모두 날 선 넓은 지직
# (무게중심 11.7 ~ 13.4 kHz · 4 kHz 위 90 ~ 97 % · crest 18 ~ 24) → 2의 첫 지직 0.15초 (따로 떨어진 한 덩이)를 3 kHz 위를 두 번 눌러 먹먹하게 ·
# 센 곳을 6 dB 눌러 "찌직"만 남기고, 끝에 합성 "퐁" (도6 — 한 옥타브 반 위에서 25 ms에 내려와 닿는 둥근 음)을 얹음.
# 1 · 4 = 지직이 이어져 덩이가 안 갈림, 3 · 5 = 바닥 −11 ~ −14 dB (계속 지직).
zp = sl(load(GHR + "zap.wav"), 0.05, 0.21)
zp = fade(tame(highpass(soften(soften(zp, 3000, 0.75), 3000, 0.75), 400), 6.0), 0.004, 0.05)
t = np.arange(int(0.45 * SR)) / SR
phz = 2 * np.pi * np.cumsum(C6 * (1 + 0.5 * np.exp(-t / 0.025))) / SR
pop_ = (np.sin(phz) + 0.2 * np.sin(2 * phz)) * (1 - np.exp(-t / 0.003)) * np.exp(-t / 0.09)
zap = np.zeros(int(0.6 * SR))
zap = place(zap, zp / pk50(zp) * 0.8, 0.0)
zap = place(zap, pop_ / pk50(pop_), 0.07)
save("Ride/lightning_zap.wav", fade(onepole_lp(zap, 7000), 0.0, 0.1), -14)

# 테슬라 탑 에너지 구 웅 (탑 꼭대기에 붙은 3D 루프, 가동 중) — 합성 낮은 웅 파2 · 파3 · 도4 (조 안, 8초 = 12박 고리에 딱 맞는 주파수 · 느린 숨) +
# 같은 정전기 후보 4의 고른 지직 (0.5 ~ 4.5초)을 2.5 kHz 위를 눌러 "플라스마 공" 결로 25 %.
L8_ = 8.0
tl = np.arange(int(L8_ * SR)) / SR
hum_ = np.zeros_like(tl)
for j, (f0, amp) in enumerate(((87.31, 1.0), (174.61, 0.7), (261.63, 0.35))):
    for c in (-4, 4):
        f = round(f0 * 2 ** (c / 1200) * L8_) / L8_
        hum_ += amp / 2 * np.sin(2 * np.pi * f * tl + j) * (0.7 + 0.3 * np.sin(2 * np.pi * (j + 1) * tl / L8_ + j))
ck_ = sl(load(GHR + "crackle.wav"), 0.5, 4.5)
ck_ = chain_loop([ck_, ck_[::-1]], 0.5)
ck_ = np.interp(np.arange(len(tl)) * len(ck_) / len(tl), np.arange(len(ck_) + 1), np.concatenate([ck_, ck_[:1]]))
ck_ = circular(lambda y: soften(soften(highpass(y, 300), 2500, 0.8), 2500, 0.8), ck_)
tesla = circular(lambda y: highpass(y, 40), 0.75 * hum_ / rms(hum_) + 0.25 * ck_ / rms(ck_))
# → 2026-11-25 실제 녹음 (테슬라 코일 + 아크 지직)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역) — 아래 줄은 기록
# save("Ride/lightning_hum_loop.wav", tesla, -19)   # −24는 4 m에서 음악보다 −7.8 (낮은 웅이라 귀 감도로 작음) → +5 dB
# report.append(f"  번개 탑 웅 고리 {len(tesla) / SR:.2f}초 · 이음새 {abs(tesla[0] - tesla[-1]):.4f}")

# ================= 체험 — 치안 본부 "떠 있는 순찰차" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorPatrolCar (차 둘 — 광장 위에 살짝 떠서 출렁이다 가끔 출동: 경광등 → 떠오름 → 돌아섬 → 날아가며 작아짐 → 돌아와 내려앉음). VARCO 1번 (떠 있는 웅).
SHR = "Ride/SecurityHQ/"
# 떠 있는 웅 (차에 붙은 3D 루프, 추진 세기만큼 크기 · 높이) — "작은 호버카가 떠 있는 부드러운 웅 · 10초 고른"으로 불러 다섯 중 4만 고름 (0.25 ~ 4.3초 −32 ~ −34 dB) ·
# 187 Hz F#3 +22센트 (조 밖) → −1.22반음 정확한 F3. 1 · 2 · 3 = 지나가며 부풀었다 잦아듦 (−50 → −17 → −41 dB), 5 = 81 Hz 낮은 웅 (귀 감도로 rms보다 15 dB 작음).
hv = pitch(highpass(sl(load(SHR + "hover.wav"), 0.25, 4.3), 50), -1.22)
hover = chain_loop([hv, hv[::-1]], 0.6)
hover = circular(lambda y: soften(y, 4000, 0.4), hover)
save("Ride/patrol_hover_loop.wav", hover, -15)   # −20은 4 m에서 음악보다 −7.2 (서 있을 땐 크기 0.6 → −12) → +5 dB
report.append(f"  순찰차 웅 고리 {len(hover) / SR:.2f}초 · 이음새 {abs(hover[0] - hover[-1]):.4f}")

# 경광등 "삐용" (출동 · 돌아올 때 경광등이 켜지는 순간, 차 자리 3D) — 합성 아주 작은 두 음 레6 → 라5 (조 안, 0.11초 뒤 0.04초에 미끄러져 내림).
t = np.arange(int(0.4 * SR)) / SR
fsir = np.where(t < 0.11, 1174.66, 880.0 + (1174.66 - 880.0) * np.exp(-np.maximum(0, t - 0.11) / 0.015))
phs = 2 * np.pi * np.cumsum(fsir) / SR
siren = (np.sin(phs) + 0.22 * np.sin(2 * phs) + 0.06 * np.sin(3 * phs)) * np.minimum(1, t / 0.008) * np.where(t < 0.25, 1.0, np.exp(-(t - 0.25) / 0.04))
save("Ride/patrol_siren.wav", fade(onepole_lp(siren, 6000), 0.0, 0.05), -14)

# ================= 체험 — 시민 회관 "스텔라돔 상영" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorStellaDome (쉼 20초 → 어두워짐 6초 → 별 · 은하수 12초 → 별자리 여덟 36초 → 별똥별 · 오로라 14초 → 새벽 7초). VARCO 0번 —
# 상영 동안 별 하늘 울림 = 심우주 통신기지 "은하수 깊은 울림" 루프를 크게 (F 장조 으뜸화음), 불 꺼짐 · 켜짐 = 합성.
# 불이 꺼짐 (어두워짐 시작, 돔 안 2D) — 부드러운 화음 파3 · 도4 · 파4 · 라4 · 도5가 3초에 걸쳐 먹먹해지며 (로우패스 4.5 kHz → 250 Hz) 잦아듦 +
# 같이 내려가는 바람 "슈우웅" (잡음 로우패스 3.5 kHz → 200 Hz).
LDD = 3.4
t = np.arange(int(LDD * SR)) / SR
pad = np.zeros_like(t)
for j, (f0, amp) in enumerate(((174.61, 0.8), (261.63, 0.7), (349.23, 0.6), (440.0, 0.45), (523.25, 0.35))):
    for c in (-3, 3):
        f = f0 * 2 ** (c / 1200)
        pad += amp / 2 * (np.sin(2 * np.pi * f * t + j) + 0.3 * np.sin(4 * np.pi * f * t + 0.5 * j) + 0.12 * np.sin(6 * np.pi * f * t + j))
rng = np.random.default_rng(730)
wn = rng.standard_normal(len(t))
k_ = np.clip(t / 3.0, 0, 1)
fc_dn = 4500 * (250 / 4500) ** k_
dim = varlp(varlp(pad, fc_dn), fc_dn)
wdn = varlp(varlp(highpass(wn, 150), 3500 * (200 / 3500) ** k_), 3500 * (200 / 3500) ** k_)
dim = dim / rms(dim) * np.minimum(1, t / 0.15) * np.exp(-np.maximum(0, t - 0.4) / 1.4) + 0.35 * wdn / rms(wdn) * np.sin(np.pi * np.clip(t / 2.4, 0, 1)) ** 2
save("Ride/dome_dim.wav", fade(dim, 0.0, 0.5), -14)
# 불이 켜짐 (새벽에 객석 불빛이 다시 오르는 순간, 돔 안 2D) — 같은 화음 · 바람이 거꾸로 (로우패스 250 Hz → 4.5 kHz로 밝아지며 차오름) + 끝에 옅은 반짝 파6 · 도7.
fc_up = 250 * (4500 / 250) ** k_
up = varlp(varlp(pad, fc_up), fc_up)
wup = varlp(varlp(highpass(wn, 150), 200 * (3500 / 200) ** k_), 200 * (3500 / 200) ** k_)
up = up / rms(up) * np.clip(t / 2.2, 0, 1) ** 1.5 * np.where(t < 2.4, 1.0, np.exp(-(t - 2.4) / 0.5)) + 0.35 * wup / rms(wup) * np.sin(np.pi * np.clip((t - 0.6) / 2.4, 0, 1)) ** 2
up = place(up, twinkle(1396.91, 1.0, 731) * 0.25 * np.abs(up).max(), 2.2)
up = place(up, twinkle(2093.0, 0.9, 732) * 0.18 * np.abs(up).max(), 2.35)
save("Ride/dome_brighten.wav", fade(up[: len(t)], 0.02, 0.4), -14)

# 상영 동안 별 하늘 울림 (돔 안 2D 루프) — 심우주 통신기지 "은하수 깊은 울림" 그대로는 (−26 저장, 300 Hz 아래가 대부분) 녹음 검수에서 음악보다 −19.5 dB로 안 들림 →
# 같은 소리를 −14로 따로 저장 (소리 풍경 파일은 그대로 둠).
save("Ride/dome_show_loop.wav", load("../SpaceStation/Assets/_Project/Audio/Interior/scape_deep_milkyway_loop.wav"), -14)

# ================= 체험 — 정련 콤비나트 "작은 해 용광로 · 집게 크레인" (SOUND_LIST_NEW 5-2, 2026-11-23) =================
# InteriorSunForge (해 둘레를 42초에 한 바퀴 도는 쇳물 국자 셋 — 주형 위에서 기울어 1.2초 동안 부음) · InteriorOreCrane (크레인 둘 — 들보 · 집게가 오가며 광석을 나름).
# VARCO 하나씩 2번 (용광로 · 붓기). 크레인 모터 = 심우주 통신기지 접시 모터 원본, 집게 "턱" = 5-1 걸쇠 원본.
RFR = "Ride/RefineryCombine/"
# 작은 해 용광로 (해 가운데에 붙은 3D 루프, 가동 중) — 후보 5 중 4: 5.9초 내내 −15 ~ −17 dB로 고른 따뜻한 불 "웅" · 지직 17.6 (가장 낮음).
# 1 = 3.7 · 8.1초에 튀는 소리, 3 · 5 = 지직 31 ~ 35 (잔 탁탁), 2 = 115 Hz (300 Hz 아래 99 % — 귀 감도로 16 dB 작음). 앞뒤로 이어 늘림 (불 소리라 거꾸로도 같은 결).
fu = highpass(sl(load(RFR + "furnace.wav"), 0.3, 5.6), 40)
furnace = chain_loop([fu, fu[::-1]], 0.8)
furnace = circular(lambda y: soften(y, 4000, 0.3), furnace)
save("Ride/refinery_furnace_loop.wav", furnace, -14)   # 300 Hz 아래 89 % · 귀 감도로 rms보다 11 dB 작음 → −18로 저장한 첫 판이 4 m에서 음악보다 −8.5 → −14
report.append(f"  용광로 고리 {len(furnace) / SR:.2f}초 · 이음새 {abs(furnace[0] - furnace[-1]):.4f}")

# 쇳물 붓기 (국자가 기울어 줄기가 나오는 순간, 그 주형 자리 3D) — "주르륵" = 후보 2의 걸쭉한 부글부글 붓기 (0.1 ~ 1.4초, 바닥 −49 dB) +
# "치익" = 후보 5의 붓고 난 뒤 지글거림 (1.1 ~ 2.8초, 바닥 −51 dB)을 0.9초부터 이어 잦아들게 (줄기 1.2초 → 식으며 치익).
# 4 = 붓기 · 쉿이 섞여 밝음 (3.6 kHz), 1 = 9 kHz 줄 (울림 21), 3 = 3초 사이 빔.
po = fade(highpass(sl(load(RFR + "pour.wav"), 0.1, 1.45), 80), 0.06, 0.35)
sz = load(RFR + "sizzle.wav")
sz = sl(sz, 1.1, 2.9)
tz = np.arange(len(sz)) / SR
sz = soften(soften(highpass(sz, 300), 5000, 0.5), 5000, 0.5) * np.minimum(1, tz / 0.25) * np.exp(-np.maximum(0, tz - 0.3) / 0.6)
pour = np.zeros(int(2.8 * SR))
pour = place(pour, po / pk50(po), 0.0)
pour = place(pour, sz / pk50(sz) * 0.55, 0.9)
# → 2026-11-25 실제 녹음으로 바꿈 (맨 아래 "실제 녹음 D" 구역 — AI Water · 변기 물 내림으로 들렸음) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# save("Ride/refinery_ladle_pour.wav", fade(pour, 0.0, 0.3), -14)

# 크레인 움직임 (들보 · 집게에 붙은 3D 루프, 움직이는 동안만 — 들보 = 1, 줄 오르내림 = 0.9 높이) — 심우주 통신기지 접시 모터 원본 (부드러운 "위잉", 울림 0 · 지직 9.3)을 앞뒤로 이음.
mo2 = highpass(sl(load("Ride/DeepSpaceComm/dish_motor.wav"), 0.25, 4.3), 80)
crane = chain_loop([mo2, mo2[::-1]], 0.6)
crane = circular(lambda y: soften(soften(y, 4000, 0.5), 7000, 0.4), crane)
# → 2026-11-25 실제 녹음 (책상 리프트 기어 모터)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역) — 아래 줄은 기록
# save("Ride/refinery_crane_move_loop.wav", crane, -24)
# report.append(f"  크레인 모터 고리 {len(crane) / SR:.2f}초 · 이음새 {abs(crane[0] - crane[-1]):.4f}")

# 집게가 광석을 쥠 "턱" (집게가 다 닫히는 순간 집게 자리 3D) — 5-1 탑승 걸쇠 원본의 다른 툭 (2.67초)을 4반음 내려 묵직하게 + 3 kHz 위를 눌러 둥글게.
grab = pitch(fade(soften(highpass(sl(lt_, 2.62, 2.98), 80), 3000, 0.5), 0.004, 0.1), -4)
save("Ride/refinery_crane_grab.wav", fade(grab, 0.0, 0.08), -14)

# ================= 체험 — 1단계 공통 도구 (SOUND_LIST_NEW 5-2 넷째 묶음, 2026-11-23) =================
# 1단계 톤 = 매끄러운 전자 · 발달한 미래 기술 (아기자기, 날 선 쇳소리 X). 1단계 방은 소리 풍경 없이 방 환경음 + 내부 음악 1단계 곡 (D 단조 · F 장조, 90 BPM).


def vrate(x, rate):
    """재생 빠르기 곡선 (샘플마다 배율)대로 다시 읽음 — 모터가 돌기 시작하면 높아지고 멈출 때 낮아짐 (길이 = len(rate))."""
    pos = np.concatenate([[0.0], np.cumsum(rate)[:-1]])
    return np.interp(np.minimum(pos, len(x) - 2), np.arange(len(x)), x)


# ================= 체험 — 중력 거주구 "전망실 망원경" (SOUND_LIST_NEW 5-2 · 4-3, 2026-11-23) =================
# InteriorTelescope (머리를 보고 F = 들여다봄 → 마우스로 둘러보기 · 휠로 배율 → F · ESC = 뗌). VARCO 1번 — 들여다봄 · 뗌 · 확대 모두 같은 렌즈 "위잉"을
# 재생 빠르기 곡선으로 올리고 내려 만듦 (한 물건 = 소리 하나).
GVR = "Ride/GravityHabitat/"
# 렌즈 위잉 — "들여다볼 때 오르는 위잉 → 1초 뒤 내려가는 위잉"으로 불렀는데 다섯 모두 둘이 갈리지 않음 → 그중 4: 1.5 kHz 매끈한 전자 "위잉" · 4 kHz 위 1 % · 울림 0,
# 봉우리 650 · 880 · 1050 Hz (미5 · 라5 · 도6 — 조 안). 0.8초부터 고른 1.25 ~ 3.35초를 몸통으로. 1 = 4.6 kHz 밝은 세 덩이 (지직 78), 2 = 바닥 −5 dB 웅 · 울림 9,
# 3 = 0.45초 날 선 쉿 (9.9 kHz), 5 = 고르지만 울림 4 · 무게중심 4 kHz (밝음).
tw = highpass(sl(load(GVR + "telescope_whirr.wav"), 1.25, 3.35), 150)
tw = onepole_lp(onepole_lp(soften(tw, 5000, 0.4), 4500), 4500)   # VARCO 위잉에 붙은 고른 쉿 (6 ~ 16 kHz가 전체보다 −21 dB — 사용자 "배경에 쉬— 치찰")을 4.5 kHz 위 두 번 눌러 걷어 냄
PAD = np.zeros(int(0.06 * SR))   # 한 번 소리 끝을 고요하게 (뒤 꼬리 바닥 −60 dBFS 아래)

# 들여다봄 (눈을 댐 — 2D, 0.9초): 렌즈가 깨어나며 빠르기 0.55 → 1로 오르는 "위잉" + 옅은 "스르륵" (렌즈 경통이 미끄러지는 바람결, 0.18초에 가장 큼).
t = np.arange(int(0.9 * SR)) / SR
rin = 0.55 + 0.45 * (1 - np.exp(-t / 0.12))
look_in = vrate(tw, rin) * (1 - np.exp(-t / 0.04)) * np.where(t < 0.42, 1.0, np.exp(-(t - 0.42) / 0.12))
sw = onepole_lp(onepole_lp(whoosh(0.55, 0.18, 400, 1500, 3500, 830), 4000), 4000)   # 바람결의 6 kHz 위 쉿도 눌러 둠 (그대로면 6 ~ 16 kHz −21 dB)
look_in = place(look_in, sw / rms(sw) * 0.45 * rms(look_in[: int(0.4 * SR)]), 0.0)[: len(t)]
save("Ride/t1_telescope_in.wav", np.concatenate([fade(look_in, 0.0, 0.15), PAD]), -14)

# 뗌 (그만 보기 — 2D, 0.75초): 같은 위잉이 빠르기 1 → 0.5로 내려가며 잦아듦 + 더 옅은 "스르륵".
t = np.arange(int(0.75 * SR)) / SR
rout = 0.5 + 0.5 * np.exp(-t / 0.22)
look_out = vrate(tw, rout) * (1 - np.exp(-t / 0.015)) * np.exp(-t / 0.2)
sw = onepole_lp(onepole_lp(whoosh(0.4, 0.08, 300, 1200, 3000, 831), 4000), 4000)
look_out = place(look_out, sw / rms(sw) * 0.3 * rms(look_out[: int(0.3 * SR)]), 0.0)[: len(t)]
save("Ride/t1_telescope_out.wav", np.concatenate([fade(look_out, 0.0, 0.15), PAD]), -14)

# 배율 (휠 한 칸마다 2D, 0.3초): 짧게 빨라졌다 서는 "윙" — 당김 (확대) = 그대로 (미 · 라 · 도), 밂 (축소) = 재생 높이 0.891 (레 · 솔 · 시♭ — 둘 다 조 안).
t = np.arange(int(0.3 * SR)) / SR
rz = 0.8 + 0.25 * (1 - np.exp(-t / 0.05))
zoom = vrate(tw[int(0.5 * SR):], rz) * np.sin(np.pi * t / 0.3) ** 1.5
save("Ride/t1_telescope_zoom.wav", np.concatenate([fade(zoom, 0.005, 0.06), PAD]), -14)

# ================= 체험 — 보안청 "천장 포탑 받침" (SOUND_LIST_NEW 5-2 · 4-4, 2026-11-23) =================
# InteriorTurretAim (평소 = 몇 초마다 다른 쪽으로 천천히 돌아 경계 · 유성우 예보 = 오는 쪽으로 빠르게). VARCO 1번 (서보).
# 서보 "위잉" (포탑 받침에 붙은 3D 루프 — 도는 빠르기만큼 크기 · 재생 높이) — "고른 서보 루프"로 불렀는데 다섯 모두 1.5 ~ 8초 한 번 돌고 서는 소리 · 4 kHz 위 24 ~ 91 % (밝은 이잉).
# 그중 2 (돌기 시작 → 1.2 ~ 2초 고름 → 멈춤, 울림 1)의 고른 곳 기음 = 391.5 Hz (솔4 −2센트, 조 안)와 배음 크기를 재서 정확한 솔4로 다시 합성 —
# 원본 배음은 높은 데까지 −20 dB로 남아 밝으니 일곱째부터 3 dB씩 더 줄이고, 한 옥타브 아래 솔3을 옅게 (천장 포탑의 무게).
# 2초 = 솔4 784번 · 떨림 · 숨이 고리에 정수 번이라 이음새 없음. 1 = 9.7 kHz 쉿, 3 = 기음 730 Hz (파#5 — 조 밖) · 울림 85, 4 = 들쭉날쭉 · 울림 73, 5 = 1.6초뿐.
SBR = "Ride/SecurityBureau/"
LS_ = 2.0
ts_ = np.arange(int(LS_ * SR)) / SR
f0 = 392.0
prof = (0, -9, -18, -19, -27, -26, -26, -29, -32, -35, -38, -41, -44, -47)   # 1 ~ 6 = 원본 그대로, 7 ~ = 3 dB씩 더 줄임
rng = np.random.default_rng(810)
ph = f0 * ts_
for fm, a_, p0 in ((3 / LS_, 0.002, 1.0), (11 / LS_, 0.0012, 2.1), (23 / LS_, 0.0008, 0.4)):   # ±0.2 % 느린 떨림 + 잔 흔들림 (고리에 정수 번)
    ph = ph + f0 * a_ / (2 * np.pi * fm) * (np.sin(2 * np.pi * fm * ts_ + p0) - np.sin(p0))
servo = sum(10 ** (db / 20) * np.sin(2 * np.pi * k * ph + rng.uniform(0, 2 * np.pi)) for k, db in enumerate(prof, 1))
servo = servo + 10 ** (-8 / 20) * np.sin(2 * np.pi * 0.5 * ph + 0.3)
servo = servo * (1 + 0.06 * np.sin(2 * np.pi * 60 / LS_ * ts_) + 0.04 * np.sin(2 * np.pi * 3 / LS_ * ts_ + 0.5))   # 모터 결 30 Hz · 느린 숨
air = circular(lambda y: onepole_lp(onepole_lp(highpass(y, 300), 2500), 2500), rng.standard_normal(len(ts_)))
servo = servo / rms(servo) + 0.22 * air / rms(air)
# → 2026-11-25 실제 녹음 (차 전동 창문 모터)으로 바꿈 (맨 아래 "실제 녹음 B — 기계음" 구역) — 아래 줄은 기록
# save("Ride/t1_turret_servo_loop.wav", servo, -20)   # 솔4 · 솔3이 대부분이라 귀 감도로 rms보다 4 dB 작음 → 다른 물체 루프 (−24)보다 4 dB 크게
# report.append(f"  포탑 서보 고리 {len(servo) / SR:.2f}초 · 이음새 {abs(servo[0] - servo[-1]):.4f}")

# ================= 체험 — 정비 공창 "수리 패드 용접" · 조선소 "용접 갠트리" (SOUND_LIST_NEW 5-2 · 4-4, 2026-11-23) =================
# InteriorWelder (회전판 위 용접 팔 토치 — 한 번에 하나씩 2 ~ 4초) · InteriorShipyard (갠트리가 판 이음선으로 옮겨 가 양옆 토치로 2 ~ 3초씩). VARCO 1번 (용접).
# 조선소 갠트리 모터 = 정련 콤비나트 크레인 모터 (refinery_crane_move_loop) 그대로 (새 파일 없음).
MWR = "Ride/MaintenanceWorks/"
# 용접 "쉬이 · 치직" (용접하는 토치에 붙은 3D 루프) — "부드러운 용접 쉿 + 옅은 지글"로 불렀는데 다섯 모두 무게중심 13 ~ 15 kHz의 고른 쉿뿐 (지글 없음, 지직 6 ~ 16 · 울림 0) →
# 가장 고른 3의 뒤 구간 (3.15 ~ 6.85초, 0.5초 크기 −21 ~ −23 dB)을 앞뒤로 이어 3 kHz 위를 네 번 걸러 부드러운 "쉬이"로 (두 번은 무게중심 6.5 kHz로 아직 밝았음) +
# 아크 흔들림 (30 Hz 아래 느린 잡음으로 크기가 ±35 % 일렁임) + 둥근 "치직" 알갱이 (초당 약 14개 · 2 ~ 4 ms · 1 ~ 3.5 kHz · 0.4 ms에 켜짐 — 바로 켜면 판 꼭대기까지 세로줄)를 합성해 얹음.
# 1 = 끝으로 갈수록 커졌다 작아짐, 2 · 5 = 바닥 −6 ~ −11 dB · 끝이 잦아듦, 4 = 0.5 ~ 1.5초마다 끊김.
wd = flatten(highpass(sl(load(MWR + "weld_hiss.wav"), 3.15, 6.85), 300), 0.5)
weld = chain_loop([wd, wd[::-1]], 0.5)
lp6 = lambda y: onepole_lp(onepole_lp(onepole_lp(onepole_lp(onepole_lp(onepole_lp(y, 2500), 2500), 2500), 2500), 2500), 2500)
weld = circular(lambda y: soften(lp6(y), 2000, 0.3), weld)   # 원본 쉿이 13 ~ 15 kHz라 3 kHz 네 번으로는 6 ~ 16 kHz가 전체보다 −15 dB (치찰로 들림) → 2.5 kHz 여섯 번
n_ = len(weld)
rng = np.random.default_rng(820)
am = circular(lambda y: onepole_lp(onepole_lp(y, 30), 30), rng.standard_normal(n_))
weld = weld * (1 + 0.35 * am / np.abs(am).max())
crk = np.zeros(n_)
for k in range(int(n_ / SR * 14)):
    ng = int(rng.uniform(0.002, 0.004) * SR)
    tg = np.arange(ng) / SR
    g = onepole_lp(onepole_lp(onepole_lp(highpass(rng.standard_normal(ng), 1000), 3000), 3000), 3000) * (1 - np.exp(-tg / 0.0004)) * np.exp(-tg / 0.0012)
    idx = (int(rng.uniform(0, n_)) + np.arange(ng)) % n_
    np.add.at(crk, idx, g / np.abs(g).max() * rng.uniform(0.3, 1.0))
weld = weld / rms(weld) + 0.35 * crk / rms(crk)
# → 2026-11-25 실제 녹음으로 바꿈 (맨 아래 "실제 녹음 D" 구역 — AI Fireworks · Thunder로 들렸음) — 아래 줄은 기록 (VARCO 원본은 그대로 둠)
# save("Ride/t1_weld_loop.wav", weld, -24)
report.append(f"  용접 고리 {len(weld) / SR:.2f}초 · 이음새 {abs(weld[0] - weld[-1]):.4f}")

# ================= 남은 빌린 소리 정리 (SOUND_LIST_NEW 5-2 넷째 묶음 B, 2026-11-23) =================
# 별똥별 다가옴 (궤도 방어망 오로라 보호막 · 별똥별 막기 — 바깥 운석 경보 MeteorIncoming 대신, 2D) — 소리 풍경 별똥별 원본 (후보 3)의 첫 "휙" (0.45초에 가장 큼, 2 kHz)을
# 0.55초까지 더 커지게 (다가옴) · 빠르기 1.04 → 0.94로 살짝 내려가게 (지나감) · 5 kHz 위를 눌러 부드럽게. 반짝임은 맞음 · 터짐 소리에 있어 뺌.
si = highpass(sl(load(OD + "star.wav"), 0.05, 1.05), 300)
t = np.arange(int(0.85 * SR)) / SR
inc = vrate(si, 1.04 - 0.1 * np.clip(t / 0.8, 0, 1))
inc = inc * np.where(t < 0.55, (0.35 + 0.65 * t / 0.55) ** 1.5, np.exp(-(t - 0.55) / 0.12))
save("Ride/odef_star_incoming.wav", fade(soften(soften(inc, 5000, 0.4), 8000, 0.4), 0.01, 0.08), -14)

# 빈 구조 바구니 부르기 (UiOpen 대신 — 바구니 자리 3D): 멈춤쇠가 풀리는 "딸깍" (5-1 탑승 걸쇠 조각을 3반음 높여 가볍게) + 줄이 움직이기 시작하는 "스윽" (도르래 줄 원본 0.4초).
# 그 뒤 바구니가 혼자 오르내리는 동안은 도르래 줄 루프를 바구니에 붙여 3D로 (코드).
lt4 = load(RD + "latch.wav")
clk = pitch(fade(soften(highpass(sl(lt4, 1.0, 1.32), 120), 4000, 0.5), 0.004, 0.08), 3)
rp4 = fade(tame(soften(soften(highpass(sl(load(RCR + "rope_pulley.wav"), 1.45, 1.95), 70), 3500, 0.5), 3500, 0.5), 8.0, 0.002, 0.04), 0.12, 0.25)   # 줄 조각의 잔 튐을 눌러 (그대로면 지직 43)
call = np.zeros(int(0.75 * SR))
call = place(call, clk, 0.0)
call = place(call, rp4 / pk50(rp4) * 0.9 * pk50(clk), 0.1)
# 걸쇠 딸깍의 6 ~ 16 kHz (전체보다 −14.5 dB)를 눌러 먹먹하게 — 누른 만큼 귀 감도가 줄어 4 m에서 +5.1 → +0.4 ~ +1.8.
# 딸깍 순간 피크가 피크 제한 (−1 dB)에 걸려 저장 크기를 올려도 안 커져서 줄 "스윽"을 0.55 → 0.9로 키우고 센 곳을 10 dB 눌러 −12로.
save("Ride/rcmd_basket_call.wav", tame(fade(onepole_lp(onepole_lp(call, 5000), 5000), 0.0, 0.1), 10.0, 0.002, 0.04), -12)

# 수정 뇌 쇼 끝 (빛이 잦아드는 3.5초 — UiClose 대신, 2D) — 쇼 시작 "차오름"과 같은 유리 화음 라3 · 라4 · 도5 · 미5 · 라5가 먹먹해지며 (로우패스 3 kHz → 400 Hz)
# 잦아듦 + 소리 풍경 수정 뇌 루프 조각이 함께 + 반짝임이 라6 · 미6 · 도6 · 라5로 내려감.
LF_ = 3.6
tf = np.arange(int(LF_ * SR)) / SR
chord = np.zeros_like(tf)
for j, (f0_, amp) in enumerate(((220.0, 0.5), (440.0, 1.0), (523.25, 0.8), (659.25, 0.6), (880.0, 0.3))):
    for c in (-3, 0, 3):
        f = f0_ * 2 ** (c / 1200)
        chord += amp / 3 * (np.sin(2 * np.pi * f * tf + j + c) + 0.2 * np.sin(4 * np.pi * f * tf + 0.7))
env_f = np.minimum(1, tf / 0.12) * np.exp(-tf / 1.1)
fcf = 3000 * (400 / 3000) ** np.clip(tf / 3.0, 0, 1)
chord = varlp(varlp(chord * env_f, fcf), fcf)
brain = sl(load("../SpaceStation/Assets/_Project/Audio/Interior/scape_aicore_brain_loop.wav"), 0.0, LF_)
brain = varlp(brain / rms(brain) * env_f, fcf)
fade_ = chord / rms(chord) + 0.5 * brain
for i_, semis in enumerate((9, 4, 0, -3)):
    fade_ = place(fade_, twinkle(C6 * 2 ** (semis / 12), 1.0, 840 + i_) * 0.45 * np.abs(fade_).max() * (0.9 - 0.15 * i_), 0.1 + i_ * 0.4)
save("Ride/aicore_crystal_fade.wav", fade(fade_[: len(tf)], 0.03, 0.6), -14)

# 바이오스피어 밭이 핌 (물이 닿아 목마른 밭이 피는 순간 — 공용 점수 "띵" 대신, 2D · 연달아 피면 음계로, 기준 = 도6) — 꽃잎이 펴지는 "사르륵" (수리 사령부 고목 잎 원본 0.3초) +
# 부드럽게 켜지는 유리 종 도6 (20 ms) + 반짝 솔6 · 도7이 굴러 오름 (으뜸 · 5도 · 옥타브만 — 음계로 옮겨도 조가 안 깨짐).
lf = load(RC + "leaf_a.wav")
env_l = np.convolve(lf ** 2, np.ones(int(0.05 * SR)) / int(0.05 * SR), "same")
i0 = int(np.argmax(env_l[int(1.0 * SR): int(5.5 * SR)])) + int(1.0 * SR)
rustle = fade(onepole_lp(onepole_lp(highpass(lf[i0 - int(0.12 * SR): i0 + int(0.2 * SR)], 1500), 5000), 5000), 0.06, 0.15)   # 잎 원본의 6 kHz 위 사각임은 눌러 둥글게
tb = np.arange(int(1.1 * SR)) / SR
bell_b = flower_bell(C6, 1.1, 850) * (1 - np.exp(-tb / 0.02))
bloom = np.zeros(int(1.3 * SR))
bloom = place(bloom, rustle / pk50(rustle) * 0.35 * pk50(bell_b), 0.0)
bloom = place(bloom, bell_b, 0.06)
bloom = place(bloom, twinkle(1.5 * C6, 0.8, 851) * 0.35, 0.13)
bloom = place(bloom, twinkle(2 * C6, 0.8, 852) * 0.25, 0.2)
save("Ride/bio_field_bloom.wav", fade(onepole_lp(bloom, 7500), 0.0, 0.25), -14)

# ================= 바탕 · 방 환경음 실제 녹음 (SOUND_LIST_NEW 10번 A, 2026-11-24) =================
# 사용자 "배경에 잡음 느낌" → AI 청각 모델 (PANNs) 줄 세우기 (docs/sound/ai-rank-2026-11-24.tsv)에서 기본 · 1단계 방 환경음 6과 3단계 바탕이 "분홍 잡음 · 흰 잡음 · 사이렌"으로 들림
# (생활 0.52 · 복도 0.59 · 물 0.45 · 기계 흰 잡음 0.22 · 전기 사이렌 0.43 · 항성 발전망 0.41 · 궤도 도시 0.19 + 0.12 · 심우주 0.13 + 0.11 · AI 중추 흰 잡음 0.15).
# → 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 녹음마다 AudioOriginals/Ambience/<녹음>/SOURCE.txt)으로 다시 만듦. 파일 이름은 그대로 (칸 연결 그대로).
# 후보 = 38개 (mp3 56 MB로 훑고 고른 13개만 flac 76 MB로) — 그 공간 소리로 들리는지 (AI 위 범주) · 분홍 · 흰 잡음 · 사이렌 · 말소리 < 0.1 · 0.5초 크기 폭 · 튐 · 음 봉우리가 음악 조 안인지로 고름.
# 루프 = 같은 녹음의 고른 조각 둘 ~ 넷을 이어 (chain_loop, 조각마다 rms를 맞춤) 10 ~ 17초, 겹치는 층은 모두 같은 길이로 만들어 섞음 (짧은 층을 반복해 붙이면 이음새에서 그 층이 끊김).
# 음 봉우리: 녹음 속 모터 · 날개 · 전기 웅은 재생 높이째 옮겨 음악 조 (D 단조 · F 장조) 안으로 (repitch_loop — 원형 보간이라 이음새 그대로), 안 맞는 좁은 줄은 notch_lines (원형 FFT).
# 고음은 save의 자동 상한 (match_tilt)이 맡음. 음량 = 게임 안 바탕만 혼자 울린 귀 감도 크기를 고치기 전과 같게 (SoundLibrary Room* · SS_* Bed 음량).
AMB = "Ambience/"


def notch_lines(x, freqs, depth_db=18.0, bw=2.0):
    """좁은 줄 여럿 (freqs Hz, 폭 bw Hz)만 깎음 — 루프 전체를 원형 FFT로 (이음새 그대로)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = np.zeros_like(f)
    for fk in freqs:
        g = np.maximum(g, np.exp(-0.5 * ((f - fk) / bw) ** 2))
    return np.fft.irfft(X * 10 ** (-depth_db * g / 20), len(x))


def repitch_loop(x, cents):
    """루프째 재생 높이 바꾸기 (길이도 바뀜) — 끝 → 처음을 이어 보간해 이음새 그대로."""
    r = 2 ** (cents / 1200)
    n = int(round(len(x) / r))
    return np.interp(np.arange(n) * (len(x) / n), np.arange(len(x) + 1), np.concatenate([x, x[:1]]))


def half_flat(x, win=1.0):
    """느린 크기 변화를 절반만 펴서 (√(중앙/크기)) 바람 · 잎이 일었다 잦아드는 결은 남기고 튀지 않게 — 원형 이동 평균 (누적 합)."""
    n = int(win * SR)
    y = np.concatenate([x[-n:], x, x[:n]]) ** 2
    c = np.concatenate([[0.0], np.cumsum(y)])
    i = np.arange(len(x)) + n - n // 2
    e = np.sqrt((c[i + n] - c[i]) / n) + 1e-9
    return x * np.sqrt(np.median(e) / e)


def low_shelf(x, db, lo=120.0, hi=320.0):
    """lo Hz 아래를 db만큼 (lo ~ hi 사이는 log 주파수로 이어) — 원형 FFT. 모터 · 밤 공기 웅 (90 ~ 180 Hz)이 몸통보다 커서
    귀 감도 (A)로 맞추면 낮은 웅만 커지는 것을 막음 (고치기 전 분홍 잡음은 A − rms −1 dB, 실제 녹음은 −8 ~ −10 dB)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = np.interp(np.log2(np.maximum(f, 1.0)), [np.log2(lo), np.log2(hi)], [db, 0.0])
    return np.fft.irfft(X * 10 ** (g / 20), len(x))


def piece(folder, pre, start, length):
    return load(f"{AMB}{folder}/{pre}_{start:06.2f}.wav")[: int(round(length * SR))]


def rchain(folder, pre, starts, seg, xf):
    """같은 녹음의 조각들 (시작초 · 길이 seg)을 rms를 맞춰 xf초씩 겹쳐 한 바퀴 루프로 — 길이 = 조각 수 × (seg − xf)."""
    parts = [piece(folder, pre, s, seg) for s in starts]
    parts = [p / rms(p) * rms(parts[0]) for p in parts]
    return chain_loop(parts, xf)


def layer(a, b, db):
    """같은 길이의 두 루프를 rms 기준 b를 db만큼 작게 섞음."""
    assert len(a) == len(b), (len(a), len(b))
    return a / rms(a) + b / rms(b) * 10 ** (db / 20)


def hpc(x, fc, times=1):
    for _ in range(times):
        x = circular(lambda y: highpass(y, fc), x)
    return x


lpc = lambda x, fc: circular(lambda y: onepole_lp(onepole_lp(y, fc), fc), x)
BB = 116.54                                                                    # Bb2 — 안정기 웅을 옮긴 높이
BB_OFF = [BB * h for h in (7, 11, 13, 14, 17, 19, 21, 22, 25, 26, 27, 28, 29, 31, 33, 34, 35)]   # Bb 배음 중 조 밖 (Ab · E−49 · F#+40 · B · C# · Eb …)


def trees(starts, seg, xf):
    """나무 사이 바람 · 잎 사각임 (trees_rustle) — 뒤에 깔린 먼 엔진 웅 (66 · 79 · 133 · 141 · 146 Hz 줄, 125 Hz 띠가 몸통보다 +10 dB)을 깎고 300 Hz 아래를 세 번 걸러 잎 · 바람만."""
    x = notch_lines(rchain("trees_rustle", "trees", starts, seg, xf), [66, 79, 133, 141, 146, 266, 282], 24, 3.0)
    return half_flat(hpc(x, 300, 3))


def bush(starts, seg, xf):
    """덤불 사각임 (bush_rustle) — 가까이서 흔든 잎 · 바람결. 94 Hz 배음 줄 (F#2+23 · F#3+16 · G4−50)을 깎고 150 Hz 아래 (손 · 바람 덜컹)를 두 번 거름."""
    return half_flat(hpc(notch_lines(rchain("bush_rustle", "bush", starts, seg, xf), [94, 187, 281, 381], 20, 2.0), 150, 2))


def river(starts, seg, xf, fc):
    """작은 폭포 강 (river_falls)을 fc Hz 위를 눌러 멀리서 들리는 물로 — 펌프 줄 (80 Hz Eb2+45와 배음)은 깎음."""
    return lpc(hpc(notch_lines(rchain("river_falls", "river", starts, seg, xf), [80, 159, 239, 318], 20, 2.0), 60), fc)


def crickets(starts, seg, xf):
    """밤 풀벌레 (night_crickets) — 전기 웅 줄 (60 · 120 · 180 · 240 Hz)과 72 · 226 · 452 Hz 줄을 깎고 100 Hz 아래 거름."""
    return hpc(notch_lines(rchain("night_crickets", "crickets", starts, seg, xf), [60, 72, 120, 180, 226, 240, 452], 20, 2.0), 100)


def air(starts, seg, xf):
    """조용한 밤 바깥 공기 (night_air) — 전기 웅 60 · 120 Hz와 72 Hz 줄을 깎고 60 Hz 아래 거름."""
    return hpc(notch_lines(rchain("night_air", "air", starts, seg, xf), [60, 72, 120], 20, 2.0), 60)


# --- 기본 · 1단계 방 환경음 6 (2D, 늘 깔림 — 2단계 · 1단계 방도 이것) ---
# 생활 (거주 · 의료 · 휴게) = 조용한 방 공기 + 먼 환풍기 (room_tone, 77초 중 0.5초 크기 폭 1.1 ~ 1.5 dB인 세 조각, 17초).
# 날개 · 모터 줄이 조 밖 (G#3+45 · F#4−25 · B1−27 · C#4−20) → −50센트 옮기면 F2 · F4 · C4 +22 ~ +28 (조 안), 남는 G#3 · Bb1+31 줄만 깎음.
# 후보 넷 중 하나: 큰 선풍기 (MCU_Large, 줄이 Bb · F로 조 안이지만 "선풍기"라 기계 방과 겹침) · 작은 냉장고 (Vehicle 0.39 · Boat) · 방 공기 + 냉장고 섞음 (Vehicle 0.33).
# AI: 분홍 잡음 0.52 → 0.00 · 위 범주 Vehicle 0.24 · Microwave oven 0.18 · Air conditioning 0.14 · Mechanical fan 0.13 · Inside, small room 0.06.
x = notch_lines(repitch_loop(rchain("room_tone", "tone", [16.0, 2.75, 43.0], 7.0, 1.5), -50), [207, 414, 59, 118], 20, 2.0)
save("Interior/amb_room_life_loop.wav", hpc(x, 40), -26)

# 물 (농장 · 물 재활용 · 산소) = 물이 흐르는 펌프 (water_motor — 모터 줄 F2 · D3 · F3 · Bb1, 처음부터 조 안) + 수영장 물 졸졸 (pool_trickle) −6 dB.
# 펌프만으로는 Boat · Vehicle 0.18 · Stream 0.17 (물 반 · 배 반) → 졸졸을 얹어 Stream 0.29 · Pour 0.23 · Trickle 0.17 · Water 0.14.
# 뺀 후보: 파충류 물그릇 자동 급수 (Water 0.64로 물은 또렷하지만 8 · 16 kHz가 몸통보다 +9 dB — 물방울 치찰, 0.5초 크기 폭 8.5 dB · 튐 42) ·
# 거품 (7초 하나뿐, 반복) · 물받이 홈통 · 작은 폭포 강 (물 방에는 너무 바깥 소리). AI: 분홍 잡음 0.45 → 0.01.
x = layer(rchain("water_motor", "flow", [22.0, 35.0, 11.0], 7.0, 1.5), rchain("pool_trickle", "trickle", [18.0, 25.25, 8.0], 7.0, 1.5), -6)
save("Interior/amb_room_water_loop.wav", low_shelf(hpc(x, 40), -6), -21)   # 낮은 웅 −6 dB: A − rms −8.5 → −6.3 · 귀 감도로 맞추면 음량이 1을 넘어 (AudioSource는 1에서 잘림) 원본을 −21로

# 기계 (창고 · 정비 · 화물 · 채굴 · 연구) = 쉬지 않고 도는 작은 펌프 (pump, 26초 중 0.5초 크기 폭 0.6 dB 두 조각 11초).
# 배음 줄 F1−18 · F2−32 · C3−25 → +25센트 옮겨 F · C 거의 정확히, 남는 901 Hz (A5+41) 줄과 두 배를 깎음.
# AI: 흰 잡음 0.22 → 0.02 · 위 범주 Vehicle 0.24 · Engine 0.15 · Pump (liquid) 0.10 · Power tool 0.09 · Tools · Idling.
# 뺀 후보: 큰 선풍기 (Vehicle 0.36 · Microwave — 기계보다 공조) · 공업 선풍기 Manitowec (B2−6 · C#5 날개 줄 34 dB — 음이 또렷 · 조 밖, C로 옮기면 Mains hum 0.93) ·
# 빨래 건조기 (Vehicle 0.73) · 에스컬레이터 (6초, Train 0.79) · 펌프 + 선풍기 섞음 (Vehicle 0.39 · Motorcycle).
f0 = 901 * 2 ** (25 / 1200)
x = notch_lines(repitch_loop(rchain("pump", "pump", [13.25, 2.0], 7.0, 1.5), 25), [f0, 2 * f0], 20, 2.0)
save("Interior/amb_room_machine_loop.wav", hpc(x, 40), -24)

# 고열 (제련 · 핵융합 · 연료전지) = 열풍기 (heater — 송풍 · 불 웅, 74초 중 0.5초 크기 폭 0.9 ~ 1.0 dB 세 조각).
# 모터 줄 112.8 Hz (A2+43)와 배음 226 · 451 Hz, 45 Hz → −43센트 옮겨 A2 · A3 · A4 · F1 (조 안).
# 게임 안 녹음 (slot3 제련소): 송풍 그대로 (1.2 kHz 위 살림)는 음악과 섞이면 Waves, surf 0.39 · Ocean 0.31 (넓은 쉬—로 들림) → 1.2 kHz 위를 눌러 먹먹한 화로 송풍으로
# (같은 크기로 섞은 모의 녹음: Waves 0.49 → 없음, Music 0.85 · Vehicle 0.18 — 고치기 전과 같은 꼴). 파일 AI: 위 Vehicle 0.42 · Car · Aircraft 0.09 · Engine (지금 것도 Vehicle 0.48 —
# AudioSet에 보일러 · 화로 범주가 없음), 흰 잡음 0.06 → 0.09.
# 뺀 후보: 열풍기 + 가스 불꽃 (5초 하나뿐 · 흰 잡음 0.10) · 열풍기 + 솥 끓는 거품 (Pour · Water로 들려 물 방과 겹침 — 끓음은 D-5 과열 상태 층 몫) · 용암 거품 (만든 소리 · Music 0.39).
x = repitch_loop(rchain("heater", "heater", [62.75, 55.0, 45.5], 7.0, 1.5), -43)
save("Interior/amb_room_heat_loop.wav", lpc(low_shelf(hpc(x, 40), -6), 1200), -22)   # 낮은 웅 −6 dB (A − rms −7.5 → −5.5) · 1.2 kHz 위 누름

# 전기 (배터리 · 태양광 · 실드 · 포탑) = 형광등 안정기 웅 (ballast_hum, 21초 중 0.5초 크기 폭 1.1 dB 두 조각) + 생활 방 공기 −12 dB.
# 지금 것 = 사이렌 0.43 · 민방위 사이렌 0.40 (120 Hz 웅이 오르내림) → 오르내리지 않는 실제 웅. 120 Hz (Bb2와 B2 사이)를 −51센트 옮겨 Bb2 (D 단조 · F 장조 안,
# 1 · 2 · 3 · 4 · 5 · 6 · 8 · 9 · 10 · 12배음 = Bb · F · D · C), 조 밖 배음 (7 · 11 · 13 · 14 · 17 …배)은 깎음. C3 (+149센트)로 옮긴 것은 Siren 0.09 · Speech 0.14로 더 나빠 뺌,
# 1 kHz 아래만 남기면 Hum 0.17로 흐려져 웅을 다 남김. AI: 사이렌 0.43 → 0.01 · 위 범주 Mains hum 0.42 · Hum 0.41 · Vehicle 0.34, 흰 잡음 0.06.
x = layer(hpc(rchain("ballast_hum", "hum", [6.0, 13.0], 7.0, 1.5), 40), hpc(rchain("room_tone", "tone", [16.0, 2.75], 7.0, 1.5), 120), -12)
x = notch_lines(repitch_loop(x, -51), BB_OFF + [207, 414, 59, 118], 24, 2.0)
save("Interior/amb_room_electric_loop.wav", x, -24)

# 복도 · 홀 (코어 · 회전 링) = 조용한 밤 바깥 공기 (night_air — 5분 중 0.5초 크기 폭 1.5 ~ 2.2 dB 세 조각): 낮은 웅 + 먼 공기, 넓은 곳의 빈 공기.
# AI: 분홍 잡음 0.59 → 0.01 · 위 범주 Vehicle 0.16 · White noise 0.07 · Eruption 0.05 (밤 공기의 낮은 웅). 뺀 후보: 큰 선풍기를 800 Hz 아래로 (Vehicle 0.42 · Microwave) · 밤 공기 + 선풍기 (Vehicle 0.59 · Aircraft).
save("Interior/amb_room_hall_loop.wav", low_shelf(air([285.25, 43.0, 189.0], 7.0, 1.5), -6), -26)   # A − rms −9.3 → −6.4

# --- 3단계 바탕 (RoomSoundscape Bed, 2D) — D-7b 바탕 설명대로, 실내 돔 정원이라 바람은 세지 않게 ---
# 이 모음에는 바람 · 잎 녹음이 적어 (나무 사이 바람 35초 · 덤불 32초 · 밤 공기 5분 · 밤 풀벌레 · 작은 폭포 강) 정원 넷은 같은 녹음을 다른 조각 · 다른 섞음으로.
# 나무 사이 바람만 쓰면 분홍 잡음 0.11 ~ 0.32 (높은 잎 소리만 남아 고른 쉬—) → 덤불 바람결 · 밤 공기를 섞어야 0.02 아래. AI는 잎 · 바람을 Waves · Ocean · Wind로 듣는 일이 많음.
# 정원 바탕에 새 · 풀벌레가 또렷한 녹음 (교외 새 · 카디널 · 여름 낮 · 새 + 산들바람)은 10 ~ 17초 루프에서 같은 지저귐이 되풀이되고 밤에도 남아 뺌 (새는 가끔 소리가 따로 있음).
# AI 중추 "정원 바람 · 잎" = 밤 공기 (몸통) + 덤불 바람결 −3 dB + 나무 잎 −8 dB, 16.5초. 흰 잡음 0.15 → 0.01 · 위 Waves 0.35 · Ocean 0.32 · Vehicle 0.31 · Wind 0.26.
L3g = air([88.5, 20.0, 180.75], 7.0, 1.5)
x = layer(layer(L3g, bush([0.75, 10.2], 9.45, 1.2), -3), trees([6.0, 26.75, 13.25], 6.7, 1.2), -8)
save("Interior/scape_aicore_bed_loop.wav", x, -24)   # 낮은 웅 셸프 (−6 dB)를 걸면 Train 0.23 · Rail transport로 들려 그대로

# 바이오스피어 "숲 잎 사각임 · 바람" (물의 순환 고리) = 덤불 바람결 + 먼 작은 폭포 강 (2.5 kHz 위 누름) −3 dB + 나무 잎 −8 dB, 11.4초.
# 위 Vehicle 0.33 · Boat 0.31 · Waves 0.28 · Ocean 0.26 · Wind 0.23 (지금 것 Vehicle 0.27 · Eruption 0.13 · Train 0.12), 분홍 잡음 0.01.
# 뺀 섞음: 강 + 나무 잎 + 밤 공기 (분홍 잡음 0.23), 나무 잎 + 덤불 흔들기 (16초 덤불 흔들기는 Rumble · Thunder).
x = layer(layer(bush([0.75, 13.5, 8.5], 5.0, 1.2), river([21.5, 32.75, 13.0], 5.0, 1.2, 2500), -3), trees([0.75, 13.25, 21.0], 5.0, 1.2), -8)
save("Interior/scape_bio_bed_loop.wav", x, -24)

# 궤도 도시 "지붕 없는 섬 정원 바람" = 덤불 바람결 + 나무 잎 −4 dB + 밤 공기 −6 dB, 11.4초. 분홍 0.19 · 흰 0.12 → 0.02 · 0.01, 위 Waves 0.43 · Ocean 0.38 · Vehicle 0.26 · Wind 0.24.
x = layer(layer(bush([0.75, 8.5, 13.5], 5.0, 1.2), trees([21.0, 6.0, 0.75], 5.0, 1.2), -4), air([88.5, 20.0, 180.75], 5.0, 1.2), -6)
save("Interior/scape_city_bed_loop.wav", x, -24)

# 항성 발전망 "햇살 꽃밭 바람" = 덤불 바람결 + 밤 공기 −4 + 나무 잎 −8 + 풀벌레 아주 옅게 −16 dB (여름 풀밭), 11.4초. 분홍 0.41 → 0.00, 위 Waves 0.45 · Ocean 0.42 · Boat 0.40 · Wind 0.35.
# 뺀 섞음: 밤 공기 몸통 + 덤불 + 풀벌레 −12 (Vehicle 0.51), 나무 잎 + 밤 공기 + 풀벌레 (분홍 0.23).
x = layer(layer(layer(bush([13.5, 8.5, 0.75], 5.0, 1.2), air([180.75, 88.5, 20.0], 5.0, 1.2), -4),
                trees([13.25, 21.0, 26.75], 5.0, 1.2), -8), crickets([7.25, 110.5, 121.25], 5.0, 1.2), -16)
save("Interior/scape_stellar_bed_loop.wav", x, -24)

# 심우주 통신기지 "별 아래 풀 언덕 밤바람" (늘 밤) = 밤 바깥 공기 + 밤 풀벌레 −12 dB, 16.5초. 분홍 0.13 · 흰 0.11 → 0.00 · 0.01, 위 Vehicle 0.21 · Aircraft 0.09 · Outside, rural or natural 0.08.
# 풀벌레는 따로 2D 층 (수리 사령부 바탕)도 있어 바탕에는 옅게. 뺀 섞음: 밤 공기 + 나무 잎 (Waves 0.28 · Aircraft), 셋 다 (Vehicle 0.53 · Aircraft 0.31).
x = layer(air([6.0, 63.75, 159.75], 7.0, 1.5), crickets([7.25, 110.5, 121.25], 7.0, 1.5), -12)
save("Interior/scape_deep_bed_loop.wav", low_shelf(x, -6), -24)   # A − rms −9.7 → −6.2

# 궤도 방어망 "잔잔한 만의 바닷바람 + 먼 잔물결" = 나무 잎 + 덤불 바람결 (같은 크기) + 먼 작은 폭포 강 (1.5 kHz 위 누름) −6 dB, 11.4초.
# AI가 이 섞음을 바다로 들음: 위 Waves, surf 0.46 · Ocean 0.40 · Vehicle 0.26 · Wind 0.21 (지금 것 Vehicle 0.16 · 흰 잡음 0.07), 분홍 0.04.
# 15.2초로 늘이려고 덤불 8.8초 조각 둘을 쓰면 13.5 ~ 18.5초의 덤불 털림이 들어가 Vehicle 0.38 · Waves 0.13으로 흐려져 11.4초로. 갈매기 + 먼 파도 녹음은 갈매기가 끊이지 않아 (0.5초 크기 폭 11 ~ 15 dB) 뺌.
sea = layer(layer(trees([6.0, 0.75, 26.75], 5.0, 1.2), bush([0.75, 8.5, 13.5], 5.0, 1.2), 0), river([13.0, 44.25, 21.5], 5.0, 1.2, 1500), -6)
save("Interior/scape_defense_bed_loop.wav", sea, -24)

# 궤도 항만 "석호 찰랑임" = 수영장 물 졸졸 + 통 속 물 휘돌림 (water_swirl) −3 dB, 전기 웅 줄을 깎고 + 궤도 방어망 바탕 (바로 위 sea) −9.5 dB (전처럼 바람결 25 %), 11.4초.
# 위 Stream 0.32 · Boat 0.14 · Trickle 0.13 · Water 0.12 (지금 것 Vehicle 0.07 · Boat 0.07 · Stream 0.04). 뺀 후보: 작은 폭포 강 (Stream 0.32지만 Vehicle 0.27 — 흐르는 강물).
lap = hpc(notch_lines(layer(rchain("pool_trickle", "trickle", [21.5, 4.0, 27.25], 5.0, 1.2), rchain("water_swirl", "swirl", [140.5, 173.25, 183.0], 5.0, 1.2), -3),
                      [60, 120, 180, 240, 360, 480], 18, 1.5), 60)
save("Interior/scape_port_bed_loop.wav", layer(lap, sea, -9.5), -24)

# 궤도 도시 밤 창 불빛 웅웅 (밤에만, 언덕 마을 3D) = 안정기 웅 (2.5 kHz 아래 — 창 너머로 조금 먹먹하게) + 작은 냉장고 −3 dB → 전기 방과 같은 Bb2로.
# 냉장고 줄 (E2 · E3−31 · E4−35 · 1611 · 2014 Hz)은 옮기기 전에 깎음. 흰 잡음 0.11 → 0.02 · 위 Hum 0.21 · Vehicle 0.19 · Mains hum 0.17.
# 웅을 700 · 1200 Hz 아래로 더 먹먹하게 하면 Hum 0.07 ~ 0.08로 흐려져 (Vehicle만 남음) 2.5 kHz. 뺀 섞음: 웅 + 밤 공기 (Speech 0.12 · Gong), 냉장고만 (Vehicle 0.42 · Boat).
x = layer(lpc(hpc(rchain("ballast_hum", "hum", [6.0, 13.0], 7.0, 1.5), 40), 2500),
          hpc(notch_lines(rchain("mini_fridge", "fridge", [30.75, 20.5], 7.0, 1.5), [81, 162, 323, 1611, 2014], 18, 1.5), 60), -3)
save("Interior/scape_city_night_loop.wav", notch_lines(repitch_loop(x, -51), BB_OFF, 24, 2.0), -26)

# ================= 실제 녹음 D — 쇳물 붓기 · 산소 누출 (작은 / 큰) · 용접 (SOUND_LIST_NEW 10번 D, 2026-11-25) =================
# AI 청각 모델 (PANNs) 줄 세우기에서 국자 쇳물 붓기 = Water 0.28 · 변기 물 내림 0.19, 산소 누출 = Static 0.33 · Noise 0.21 · White noise 0.20,
# 용접 = Fireworks 0.19 · Thunder 0.12 · Rain 0.10 → 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 녹음마다 AudioOriginals/Misc/<녹음>/SOURCE.txt)으로.
# 파일 이름 · 길이 · 쓰는 곳은 그대로 (칸 연결 그대로), 결만 바꿈. 후보 = 25개 (mp3 16 MB로 훑고 고른 4개만 flac 17.5 MB) — 범주 FIRE · GEOTHERMAL · AIR · ELECTRICITY ·
# WATER · MACHINES · OBJECTS · ICE. 이 모음엔 용접 · 쇳물 · 튀김 녹음이 없어서, 스팀 청소기가 바닥에 닿는 "지글" 구간 (10초 창 AI Sizzle 0.29 ~ 0.34 · Frying 0.24 ~ 0.33)을 찾아 바탕으로 씀.
# AI 점수는 ai_rank.py와 같은 꼴 (한 번 소리 = 4번 반복 10초, 루프 = 10초로 이어 붙임).
MSC = "Misc/"


def misc_piece(folder, pre, start, a, b):
    """Misc/<녹음>/<앞>_<조각 시작초>.wav에서 원본 시각 a ~ b초 (샘플 수 = 반올림 — 층 길이가 1 샘플도 안 어긋나게)."""
    x = load(f"{MSC}{folder}/{pre}_{start:06.2f}.wav")
    i = int(round((a - start) * SR))
    return x[i:i + int(round((b - a) * SR))].copy()


def misc_seg(folder, pre, start, a, b, fc):
    return highpass(highpass(misc_piece(folder, pre, start, a, b), fc), fc)


def d_swell(n, att, hold, tau):
    """att초에 차올라 hold초까지 버티고 tau초로 잦아드는 크기 곡선."""
    t = np.arange(n) / SR
    return np.minimum(1, t / att) * np.where(t < hold, 1.0, np.exp(-(t - hold) / tau))


lp2d = lambda x, fc: onepole_lp(onepole_lp(x, fc), fc)

# --- 국자 쇳물 붓기 (정련 콤비나트 — 국자가 기울어 1.2초 줄기가 나오는 순간 그 주형 3D, 2.8초 그대로) ---
# 걸쭉한 흐름 = 용암 거품 녹음 (GEOLava, 80 ~ 82.8초)의 낮은 "꿀렁" (700 Hz 아래, 0 ~ 1초 버팀) + 불 탁탁 = 모닥불 녹음 (7 ~ 9.8초, 1.5 kHz 위 — 뒤에 깔린 말소리 · 동물은 걸러짐)
# + 식으며 치익 = 스팀 청소기가 바닥에 닿는 지글 (17 ~ 19.8초, 0.3초에 차올라 1.4초부터 잦아듦) → 8 kHz 위를 두 번 누름.
# AI: Water 0.28 · Toilet flush 0.19 · Liquid 0.11 → Sizzle 0.39 · Frying (food) 0.36 · Stir 0.28, Water 0.02 · Toilet flush 0.00.
# 뺀 것: 용암 거품만 (Toilet flush 0.23 · Music — 만든 소리) · 솥 거품 셋 (Water 0.11 ~ 0.16) · 콘크리트에 쏟은 물 (Drip · Water) · 가스 불꽃 (Vehicle · Jet engine) ·
# 지글만 (Sizzle 0.18 · Vehicle 0.16 — 꿀렁 · 탁탁이 없으면 흐르는 느낌이 없음) · 지글을 6 kHz 위까지 누름 (Sizzle 0.06 — 지글 결이 사라짐) · 탁탁을 얼음 갈라짐으로 (Sizzle 0.23).
# 용암 꿀렁의 60 · 154 Hz 줄 (전기 웅 · Eb3 — 조 밖)은 깎음.
NPD = int(2.8 * SR)
d_rum = notch_lines(lp2d(highpass(misc_piece("lava_bubbles", "lava", 79.5, 80.0, 82.8), 40), 700), [60, 154], 18, 2.0)
d_siz = highpass(misc_piece("steam_cleaner", "steam", 16.8, 17.0, 19.8), 150)
d_crk = highpass(misc_piece("campfire", "fire", 6.5, 7.0, 9.8), 1500)
d_pour = np.zeros(NPD)
for part, gain, env in ((d_rum, 0.8, d_swell(NPD, 0.08, 1.0, 0.35)), (d_siz, 1.0, d_swell(NPD, 0.3, 1.4, 0.6)), (d_crk, 0.5, d_swell(NPD, 0.05, 1.1, 0.4))):
    d_pour += np.pad(part, (0, max(0, NPD - len(part))))[:NPD] / rms(part) * gain * env
save("Ride/refinery_ladle_pour.wav", fade(lp2d(d_pour, 8000), 0.0, 0.3), -14)

# --- 산소 누출 (바깥 사건 OxygenLeakEventData — 나쁜 사건 알림 0.6초 뒤 2D) — 작은 / 큰 두 크기 (사용자 2026-11-24 "추천대로", CONNECT_1121 A5 이웃에 따라 규모) ---
# 같은 녹음 · 같은 구간 = 한 가족: 스팀 청소기를 켠 첫 "쉬익" (0.3 ~ 3.3초 — 0.1초 크기 −29 ~ −34 dB로 고름, 바닥을 문지르는 "탁"이 없음).
# 큰 = 지금 칸 (2.6초, 지금 2.56초와 같은 길이): 2반음 내려 굵게, 12 ms에 바로 터져 1.4초 버티고 0.45초로 잦아듦.
# 작은 = 파일만 (1.4초, 예정 — 좋은 이웃 옆 작은 누출): 1반음 올려 가늘게, 0.45초 버티고 잦아듦.
# 9 kHz 위를 두 번 누름 (지금 것 무게중심 11 kHz · 6 ~ 16 kHz −2 dB → 4.2 kHz · −5.5 dB, 6 kHz까지 누르면 Hiss · Steam이 0.1 아래로 떨어져 Vehicle만 남음).
# 김 속 1230 Hz 삐 (Eb6 — 조 밖)와 60 · 67 Hz 웅은 깎음.
# AI: 큰 = Static 0.33 · Noise 0.21 · White noise 0.20 → Steam 0.43 · Hiss 0.43 · Vehicle 0.38, Static 0.00 · White noise 0.02 /
#     작은 = Hiss 0.23 · Steam 0.12 · Vehicle 0.12 · Door 0.08 (1.4초 한 번 소리는 AI가 미닫이문 "칙"으로도 들음 — 문지르는 "탁"이 든 3.2초 뒤 구간은 Door 0.2 ~ 0.6), Static 0.02.
# 뺀 후보: 김 빼기 밸브 (Acme Windmaster — Vehicle 0.12 · 흰 잡음 0.06) · 스프레이 캔 (Spray 0.51 · Hiss 0.32이나 무게중심 10 kHz — 6 kHz로 누르면 Breathing · Sigh) ·
# 스팀 청소기 김만 (Steam 0.24 — 다른 녹음, 더 밝음) · 타이어 바람 넣기 (Vehicle 0.35 — 낮은 웅) · 휘핑크림 (Vehicle 0.42) · 혈압계 바람 빼기 (반복 펌프) · 에어로졸 (1.4초뿐).
d_lk = notch_lines(highpass(misc_piece("steam_cleaner", "steam", 0.0, 0.3, 3.3), 120), [60, 67, 1230], 18, 3.0)
d_big = lp2d(pitch(d_lk, -2), 9000)[: int(2.6 * SR)]
save("Event/oxygen_leak.wav", fade(d_big * d_swell(len(d_big), 0.012, 1.4, 0.45), 0.0, 0.25), -14)
d_small = lp2d(pitch(d_lk, 1), 9000)[: int(1.4 * SR)]
save("Event/oxygen_leak_small.wav", fade(d_small * d_swell(len(d_small), 0.008, 0.45, 0.45), 0.0, 0.3), -14)

# --- 용접 루프 (정비 공창 수리 패드 용접 팔 · 조선소 갠트리 토치 — 불꽃이 튀는 토치에 붙은 3D 루프, 코드 그대로) ---
# 지글 = 스팀 청소기가 바닥에 닿는 지글 세 조각 (17 ~ 19.9 · 4.95 ~ 6.1 · 6.85 ~ 8초 — 6.2 · 6.7초의 "탁"은 피함, 300 Hz 아래 청소기 모터 웅을 거름) +
# 아크 탁탁 = 전기 아크 · 불꽃 녹음 (1 ~ 3.55 · 53 ~ 55.55초 — 고르게 튀는 구간, 1.5 kHz 위)를 같은 크기로, 두 층 모두 4.3초 (이음새에서 한 층만 끊기지 않게).
# 4 kHz 위를 두 번 눌러 부드럽게 (날 선 지직 X — 6 ~ 16 kHz 전체 대비 −25 dB) + save 자동 고음 상한. 0.5초 크기 폭 3.5 dB · 음 봉우리 없음.
# AI: Fireworks 0.19 · Thunder 0.12 · Rain 0.10 → Sizzle 0.23 · Stir 0.20 · Vehicle 0.19 · Frying (food) 0.16, Fireworks · Thunder 0.00 · Rain 0.01.
# 뺀 것: 지글만 (자동 고음 상한 뒤 Vehicle 0.22 — 탁탁이 있어야 지글로 들림) · 테슬라 코일 둘 (Mechanisms · Ratchet / Jackhammer — 날 선 붕붕 줄) ·
# 이발기 · 면도기 (Vehicle · Engine — 음이 또렷) · 가스 불꽃 (Vehicle 0.51) · 모닥불 탁탁 (Vehicle 0.30 — 불로 들려 용접과 멂) · 고르지 않은 아크 구간 (40 ~ 42.7초 — 0.1초 크기 −64 ~ −31 dB).
d_wz = [misc_seg("steam_cleaner", "steam", 16.8, 17.0, 19.9, 300), misc_seg("steam_cleaner", "steam", 0.0, 4.95, 6.1, 300), misc_seg("steam_cleaner", "steam", 0.0, 6.85, 8.0, 300)]
d_wz = chain_loop([p / rms(p) * rms(d_wz[0]) for p in d_wz], 0.3)
d_wa = [misc_seg("electric_arcs", "arcs", 0.5, 1.0, 3.55, 1500), misc_seg("electric_arcs", "arcs", 52.5, 53.0, 55.55, 1500)]
d_wa = chain_loop([p / rms(p) * rms(d_wa[0]) for p in d_wa], 0.4)
assert len(d_wz) == len(d_wa), (len(d_wz), len(d_wa))
d_weld = circular(lambda y: lp2d(y, 4000), d_wz / rms(d_wz) + d_wa / rms(d_wa))
save("Ride/t1_weld_loop.wav", d_weld, -24)
report.append(f"  용접 고리 (실제 녹음) {len(d_weld) / SR:.2f}초 · 이음새 차 / 이웃 샘플 차 중앙값 {abs(d_weld[0] - d_weld[-1]) / np.median(np.abs(np.diff(d_weld))):.1f}")

# ================= 실제 녹음 B — 기계음 (SOUND_LIST_NEW 10번 B, 2026-11-25) =================
# AI 청각 모델 줄 세우기 (docs/sound/ai-rank-2026-11-24.tsv)에서 기계가 순음 · 사이렌 · 신시사이저로 들린 것 (포탑 서보 · 번개 탑 웅 · 빛 반응 = Sine wave,
# 접시 모터 = Siren, 크레인 = Synthesizer · Noise, 바깥 정거장 웅 = Silence · Sine wave, 드론 떼 = Music · Piano)
# → 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 녹음마다 AudioOriginals/Machines/<녹음>/SOURCE.txt)으로 다시 만듦. 파일 이름 · 칸 그대로.
# 후보 38개 (mp3 27 MB로 훑고 고른 10개만 flac 34 MB) — 10초판 (루프 = 이어 붙임 · 한 번 소리 = 2.5초마다 4번)의 AI 위 범주 · Siren · Sine wave · Music · Synthesizer,
# 소음원 (포탑 · 크레인 — 나중에 옆방으로 먹먹하게 새어 들 자리, SOUND_LIST D-4)은 2 kHz 로우패스판도 무엇인지 남는지, 음 봉우리가 음악 조 (D 단조 · F 장조) 안인지로 고름
# (빠르기로 Pitch가 바뀌는 루프는 Pitch 1에서). AudioSet은 고른 모터 웅을 대개 Vehicle · Engine으로 들음 (서보 · 크레인 · 테슬라 범주는 없음).
# 음량 = 게임 안 4 m 앞 혼자 울림 귀 감도를 고치기 전과 같게 (루프 = 0.25초 창 평균, 한 번 소리 = 최대) — 칸 음량은 그대로 두고 원본 저장 크기로 맞춤.
# 옛 합성 · VARCO 저장 줄 (각 체험 구역)은 주석 기록으로 둠.
MCH = "Machines/"


def mpiece(folder, pre, start, length):
    return load(f"{MCH}{folder}/{pre}_{start:06.2f}.wav")[: int(round(length * SR))]


def mrchain(folder, pre, starts, seg, xf):
    """같은 녹음의 조각들 (시작초 · 길이 seg)을 rms를 맞춰 xf초씩 겹쳐 한 바퀴 루프로 (A의 rchain과 같음 — 폴더만 Machines)."""
    parts = [mpiece(folder, pre, s, seg) for s in starts]
    parts = [p / rms(p) * rms(parts[0]) for p in parts]
    return chain_loop(parts, xf)


def cents_b(f_from, f_to):
    return 1200 * np.log2(f_to / f_from)


def f0_track(x, lo, hi, ks=range(3, 9), win=0.1, hop=0.02):
    """모터 기음 따라가기: 창마다 lo ~ hi Hz 후보 중 ks배음 크기 합이 가장 큰 것, 5칸 중앙값으로 튀는 값 정리. 돌려줌 = (창 가운데 초, Hz)."""
    N, H, M = int(win * SR), int(hop * SR), 1 << 16
    f = np.fft.rfftfreq(M, 1 / SR)
    cand = np.arange(lo, hi, 0.05)
    t, f0 = [], []
    for i in range(0, len(x) - N, H):
        X = np.abs(np.fft.rfft(x[i:i + N] * np.hanning(N), M))
        t.append((i + N / 2) / SR)
        f0.append(cand[int(np.argmax(sum(np.interp(k * cand, f, X) for k in ks)))])
    f0 = np.array(f0)
    return np.array(t), np.array([np.median(f0[max(0, j - 2):j + 3]) for j in range(len(f0))])


def flat_pitch(x, t, f0, target):
    """기음이 target Hz (수 하나 또는 창마다)가 되게 재생 빠르기를 샘플마다 바꿔 다시 읽음 (길이도 그만큼 바뀜)."""
    tau = np.arange(len(x)) / SR
    T = np.cumsum(np.interp(tau, t, f0) / np.interp(tau, t, np.broadcast_to(target, f0.shape))) / SR
    pos = np.interp(np.arange(0, T[-1], 1 / SR), T, tau) * SR
    return np.interp(pos, np.arange(len(x)), x)


# 보안청 천장 포탑 서보 (포탑 받침 3D 루프, 도는 빠르기만큼 크기 · Pitch 0.84 ~ 1) = 차 전동 창문 올림 (window_motor — 창 모터 = 작은 기어 모터, 서보와 같은 결).
# 창이 오르며 짐이 바뀌어 모터 기음이 91 ~ 99 Hz (±70센트)로 오르내림 — 그대로 이으면 느린 높이 흔들림 → 배음 합으로 기음을 따라가 재생 빠르기를 바꿔
# 가장 오래 머문 91.7 Hz를 정확히 F2 (87.31 Hz — 배음 F · C · A, 경계 빠르기 Pitch 0.84면 D · A)로, 흔들림은 35 %만 남김 (±25센트 — 모터 결).
# 다 펴면 (0 %) 배음이 너무 고르게 남아 2 kHz 로우패스판이 Gong 0.45 · Music 0.23, 조 밖 배음 (7배 Eb−31 …)까지 깎으면 더 화음처럼 (Gong · Music) — 둘 다 뺌.
# 뒤 2.4 ~ 2.7초는 짐이 흐트러져 따라가기가 틀려 원본 0.6 ~ 2.35초만 쓰고, 앞뒤로 이어 느린 크기 변화를 절반 폄.
# AI: Siren 0.11 · Sine wave 0.04 → 0.00 · 0.00, 위 Vehicle 0.82 · Aircraft · Propeller (고른 모터 웅), 2 kHz 로우패스판도 Vehicle 0.23 (Music 0.13 — 벽 너머로도 모터).
# 뺀 후보: 로봇 서보 모터 (진짜 서보지만 4 ~ 8 kHz 밝은 "지잉" 1 ~ 2초 덩어리 — Dental drill 0.72, 로우패스판 Screaming) · RC 차 모터 (Engine 0.38이지만 로우패스판 Noise) ·
# 책상 리프트 (로우패스판 Gong — 크레인에 씀) · 침대 리프트 (Siren 0.40 — 높이가 오르내림) · 미세 공구 (로우패스판 Gong 0.84) · 무선 드릴 (Speech 0.23 · 끽).
wmB = hpc(mpiece("window_motor", "window", 0.40, 3.0)[int(0.2 * SR):int(1.95 * SR)], 60)
tB, fB = f0_track(wmB, 84.0, 102.0)
fmB = np.bincount(np.round(fB).astype(int)).argmax()
fmB = fB[np.abs(fB - fmB) < 1].mean()
wmB = flat_pitch(wmB, tB, fB, 87.31 * (fB / fmB) ** 0.35)
servoB = half_flat(chain_loop([wmB, wmB[::-1]], 0.4), 0.8)
save("Ride/t1_turret_servo_loop.wav", servoB, -15.6)
report.append(f"  포탑 서보 (창 모터) 고리 {len(servoB) / SR:.2f}초 · 이음새 {abs(servoB[0] - servoB[-1]):.4f} · 머문 기음 {fmB:.1f} Hz → F2")

# 그리드 허브 번개 정원 테슬라 탑 웅 (탑 꼭대기 3D 루프, 가동 중) = 실제 테슬라 코일 (tesla_coil — 24.8 Hz 펄스의 촘촘한 배음 "부르르") + 전기 아크의 고른 지직 (arc_sizzle) 15 %.
# 방 바탕이 이미 전기 방 (형광등 안정기 웅 Bb2)이라 안정기 웅을 또 섞지 않고 코일 결로 (섞어 보면 로우패스판 Synthesizer 0.78 ~ 0.87).
# 펄스를 F0 (21.83 Hz, −221센트)로 옮겨 배음 F · C · A, 조 밖 배음 (7 · 11 · 13 · 14 · 17 …배 — 가장 또렷하던 240 Hz B−49 등)은 깎고 1.8 kHz 위를 눌러 둥글게 (날 선 버즈 X),
# 지직은 아크 녹음의 1 ~ 8초 고른 "치지직"만 (뒤는 덩어리로 튐) 3 kHz 위를 눌러 아주 옅게.
# AI: Music 0.18 · Sine wave 0.06 → 0.02 · 0.00, 위 Engine 0.27 · Idling (AudioSet에 테슬라 · 변압기 범주가 없어 낮은 펄스 웅을 엔진으로 들음 — 원본도 Jackhammer · Sewing machine),
# 2 kHz 로우패스판 Engine 0.20 (조 밖 배음을 깎기 전 Synthesizer 0.21). 뺀 후보: 테슬라 코일 방전 터짐 (1 ~ 2초마다 끊김) · 장난감 팽이 웅 (Music 0.39 · Synthesizer).
# 옛 웅은 지직 덩어리가 순간 최대를 만들어 (0.25초 최대가 평균보다 6.4 dB 큼) 평균으로 맞춤 — 새 것은 고름 (최대 − 평균 2 dB).
tslB = repitch_loop(hpc(mrchain("tesla_coil", "tesla", [8.0, 22.0, 41.0], 5.0, 0.8), 60), cents_b(24.8, 21.83))
tslB = lpc(notch_lines(tslB, [21.83 * k for k in (7, 11, 13, 14, 17, 19, 22, 23, 25, 26, 28, 29, 31, 33, 34, 35)], 20, 3.0), 1800)
szB = lpc(repitch_loop(hpc(mrchain("arc_sizzle", "arc", [1.0, 2.0, 3.2], 5.0, 0.8), 400), cents_b(24.8, 21.83)), 3000)
teslaB = tslB / rms(tslB) + 0.15 * szB / rms(szB)
save("Ride/lightning_hum_loop.wav", teslaB, -20.8)
report.append(f"  번개 탑 웅 (테슬라 코일) 고리 {len(teslaB) / SR:.2f}초 · 이음새 {abs(teslaB[0] - teslaB[-1]):.4f}")

# 궤도 방어망 등대 빛이 닿아 답함 (2D, TryPlayNote — 찾은 차례대로 도 · 레 · 파 · 솔 · 라 · 도) = 실제 유리 꽃병 "딩" (glass_vase — 60번 친 녹음 중 36.07초):
# 기음 1530.8 Hz를 정확한 도6 (−658.4센트)으로 — 기준 음이 도5 → 도6 (유리 결이 삶, 도5까지 내리면 쇠 Clang 0.7 쪽). 조 밖 1.38배 · 2배 봉우리 −40 dB, 앞 바닥 −61 dB.
# AI (2.5초마다 4번): Sine wave 0.19 · Dial tone 0.08 → 0.00, 위 Ding 0.69 · Clang 0.18 · Chink, clink. 뺀 후보: 같은 꽃병의 다른 딩 (1.38배 봉우리 −7 ~ −35 dB · 꼬리 짧음 ·
# Clang 0.5 ~ 0.7) · 명상 차임 (쇠 막대, 2.7배 봉우리 −5 dB) · 소리굽쇠 · 유리 두드림 (바닥 −20 ~ −35 dB) · 도자기 종 · 유리 종 (흔들어 계속 울림).
vaseB = pitch(mpiece("glass_vase", "vase", 36.07, 1.62), -6.584)
save("Ride/odef_beam_react.wav", fade(highpass(trim_lead(vaseB, -40), 150), 0.002, 0.45)[: int(2.0 * SR)], -13.2)

# 심우주 통신기지 전파 접시 모터 (손바퀴를 돌린 접시 자리 3D, 2.6초에 돌아섬) = 실제 차 선루프 열림 (sunroof_motor) — 모터가 "툭" 걸려 도는 첫 3.45초에서
# 1.65 ~ 1.95초 딸깍을 건너뛰어 잇고 (0.15초 겹침) 위잉 557 Hz (C#5)를 D5로 (+92센트). 빠르기 곡선으로 높이를 오르내리지 않음 (지금 것 = 재생 빠르기 0.55 → 1 → 0.55 = 사이렌).
# 몸통의 60 ~ 85 %가 200 Hz 아래 차체 울림이라 100 Hz 아래를 두 번 거르고, 1 ms 뾰족함 (시작 딸깍)만 12 dB 눌러 (폭죽 터짐과 같은 식) 피크 제한 안에서 크게.
# "툭"을 더 누르거나 250 Hz까지 거르면 위잉은 커지지만 Power windows 0.82 → 0.02 (걸리는 "툭"이 전동 모터라는 단서) — 최대는 옛 것과 같게, 몸통 평균 −2 dB로 타협.
# AI (4번): Siren 0.42 → 0.00, 위 Vehicle 0.58 · Power windows, electric windows 0.35 · Car. 뺀 후보: 선루프 닫힘 (끝 닫힘 "쿵"이 커야 Power windows) · 창문 올림 (포탑에 씀) ·
# 창문 내림 (로우패스판 Siren 0.16) · 책상 리프트.
saB = hpc(mpiece("sunroof_motor", "sunroof", 0.00, 1.65), 60)
sbB = hpc(mpiece("sunroof_motor", "sunroof", 1.95, 1.5), 60)
kB = int(0.15 * SR)
txB = np.linspace(0, np.pi / 2, kB)
dishB = np.concatenate([saB[:-kB], saB[-kB:] * np.cos(txB) + sbB[:kB] * np.sin(txB), sbB[kB:]])
dishB = tame(highpass(highpass(pitch(dishB, 0.92), 100), 100), 4.0, 0.002, 0.05)
w1B = int(0.001 * SR)
pkB = np.lib.stride_tricks.sliding_window_view(np.pad(np.abs(dishB), (w1B, w1B), mode="edge"), 2 * w1B + 1).max(1)
dishB = dishB * np.convolve(np.minimum(1.0, np.abs(dishB).max() * 10 ** (-12 / 20) / (pkB + 1e-12)), np.ones(w1B) / w1B, mode="same")
save("Ride/dsc_dish_motor.wav", fade(dishB, 0.005, 0.35), -4.8)   # 피크 −1 dB 바로 아래 (더 키우면 피크 제한에 걸림)

# 정련 콤비나트 크레인 (들보 · 집게 3D 루프, Pitch 0.88 ~ 1.12) · 조선소 갠트리 (ShipyardGantryLoop — 같은 파일, Pitch 0.85 ~ 1.15) = 실제 책상 리프트 올림 (desk_lift —
# 전동 리프트 기어 모터, 호이스트와 같은 결) 3 ~ 12초. 60 Hz 웅 (B1−48)을 Bb1로 (−51센트 — 위잉 719 Hz도 F5로), 조 밖 줄 (40 · 48 · 80 · 139 Hz와 옮긴 뒤 조 밖으로 간
# 105 · 141 · 252 Hz)은 깎고 남은 웅 줄 (60 · 100 · 120 · 239 Hz)도 16 dB 눌러 모터 결만, 낮은 쪽 −6 dB 셸프 (300 Hz 아래 93 % — 귀 감도로 작음).
# AI: Music 0.28 · Synthesizer 0.21 · Noise 0.23 → 0.01 · 0.00 · 0.00, 위 Vehicle 0.67 · Train · Aircraft, 2 kHz 로우패스판도 Vehicle 0.53 (벽 너머로도 모터).
# 뺀 후보: 세차장 기계 (Vehicle이지만 물 · 솔 소리) · 에스컬레이터 (Train 0.77 — 칙칙) · 엘리베이터 (문 소리 · Music 0.47) · 책상 리프트 내림 (로우패스판 Gong 0.43 · Vehicle 0.15).
rlB = 2 ** (-51 / 1200)
liftB = repitch_loop(hpc(mrchain("desk_lift", "lift", [3.0, 7.5], 4.5, 0.5), 40), -51)
liftB = notch_lines(notch_lines(liftB, [f * rlB for f in (40, 48, 80, 139, 1081)], 20, 2.0), [f * rlB for f in (60, 100, 120, 239)], 16, 3.0)
craneB = low_shelf(notch_lines(liftB, [51, 105, 141, 252], 15, 2.5), -6, 150, 400)
save("Ride/refinery_crane_move_loop.wav", craneB, -17.6)
report.append(f"  크레인 모터 (책상 리프트) 고리 {len(craneB) / SR:.2f}초 · 이음새 {abs(craneB[0] - craneB[-1]):.4f}")

# 바깥 정거장 웅 (바깥 화면 2D 루프 — 메뉴 0.5배 · 내부 0.25배로도 깔림) = 실제 건조기 드럼 웅 (dryer, 600 Hz 아래) + 큰 환기 팬 (big_fan) 40 %.
# 바깥 음악은 낮 D 장조 · 메뉴 C# 단조 · 밤 흐릿 · 내부 D 단조 — 넷 모두에 맞는 음은 E · A뿐 → 건조기의 줄 (120 · 360 Hz …)은 모두 깎아 음 없는 웅으로,
# 팬 줄 (Bb1 · Bb2 · F3 · F4)은 −100센트 옮겨 A · E로, 남은 흐릿한 줄 (75 · 108 · 151 Hz)도 12 dB 누름.
# AI: Silence 0.15 · Sine wave 0.12 → 0.00 · 0.00, 위 Vehicle 0.41 · Field recording · Aircraft, 분홍 · 흰 잡음 0.00 · 0.04.
# 뺀 후보: 팬만 (2 kHz 로우패스판 Gong 0.91 — 음이 또렷) · PC 팬 (Gong 0.90) · 세차장 (Eruption · Explosion) · 펌프 (박동 · Synthesizer) · 방 공기 팬 (Music 0.21).
drB = hpc(mrchain("dryer", "dryer", [2.0, 9.0], 7.0, 1.2), 30)
drB = notch_lines(drB, [50, 62, 100, 120, 129, 240, 246, 332, 360, 389, 480, 573, 639, 732], 20, 2.5)
fnB = np.tile(repitch_loop(hpc(mrchain("big_fan", "fan", [20.0, 45.0, 80.0], 7.0, 1.2), 30), -100), 2)
nB, kB = len(drB), int(0.8 * SR)   # 팬 층을 건조기 층과 같은 길이의 고리로 (loop_exact와 같은 식 — 길이를 샘플로 정확히)
txB = np.linspace(0, np.pi / 2, kB)
fnB_ = fnB[:nB].copy()
fnB_[:kB] = fnB[:kB] * np.sin(txB) + fnB[nB:nB + kB] * np.cos(txB)
stationB = lpc(notch_lines(drB / rms(drB) + 0.4 * fnB_ / rms(fnB_), [75, 108, 151], 12, 2.5), 600)
save("Ambient/amb_station_loop.wav", stationB, -25.6)
report.append(f"  바깥 정거장 웅 (건조기 + 팬) 고리 {len(stationB) / SR:.2f}초 · 이음새 {abs(stationB[0] - stationB[-1]):.4f}")

# 드론 정비창 라이트 쇼 드론 떼 (쇼 동안 2D 루프, 16초 = 24박) = 실제 RC 헬기 떠 있음 (rc_heli_hover) 셋 · 날기 (rc_heli_fly) 셋을 저마다 다른 자리 · 높이로 여섯 겹.
# 떠 있는 날개 음 E3+23 → F3 근처 (+62 ~ +92센트 — 한 음이 아니라 떼 결), 날기 D4−26 → D4 근처 (+12 ~ +40센트), 86 Hz 꼬리 날개 줄 (옮기면 F#2 — 조 밖)은 깎음.
# 겹마다 16초 고리로 (loop_exact) 만들어 더함. AI: Music 0.79 · Piano 0.31 · Synthesizer 0.14 → 0.01 · 0.00 · 0.00, 위 Vehicle 0.69 · Aircraft. 음 봉우리 F3 · D5 (조 안).
# 뺀 후보: 작은 드론 날기 (Music 0.56 — 높은 날개 음이 선율처럼). AI 중추 둥지 드론 깨어남 · 착륙 (scape_aicore_drone_01 · aicore_drone_land)은
# 실제 RC 헬기 시작 · 멈춤이 Drill · Power tool (드릴)로 들려 지금 것 (artificial 0.13)을 그대로 둠.
rngB = np.random.default_rng(910)
swarmB = np.zeros(int(16.0 * SR))
for fdB, prB, stB, cB, ntB, gB in (("rc_heli_hover", "hover", 5.75, 77, (86,), 1.0), ("rc_heli_hover", "hover", 10.0, 62, (86,), 0.8), ("rc_heli_hover", "hover", 5.75, 92, (86,), 0.6),
                                   ("rc_heli_fly", "fly", 30.25, 26, (), 0.9), ("rc_heli_fly", "fly", 19.5, 12, (), 0.7), ("rc_heli_fly", "fly", 6.25, 40, (), 0.6)):
    pB = hpc(mpiece(fdB, prB, stB, 4.0), 80)
    yB = chain_loop([pB, pB[::-1]], 0.5)
    if ntB:
        yB = notch_lines(yB, list(ntB), 18, 3.0)
    yB = repitch_loop(yB, cB)
    yB = np.tile(yB, int(np.ceil((len(swarmB) + SR) / len(yB))) + 1)
    offB = int(rngB.uniform(0, len(yB) - len(swarmB) - SR))
    yB = loop_exact(yB[offB:], 16.0)
    swarmB += gB * yB / rms(yB)
save("Ride/droneshow_swarm_loop.wav", swarmB, -24.9)
report.append(f"  드론 떼 (RC 헬기 여섯 겹) 고리 {len(swarmB) / SR:.2f}초 · 이음새 {abs(swarmB[0] - swarmB[-1]):.4f}")

# ================= 실제 녹음 C — 생물 · 날씨 (SOUND_LIST_NEW 10번 C, 2026-11-25) =================
# AI 청각 모델 줄 세우기 (docs/sound/ai-rank-2026-11-24.tsv)에서 생물 · 날씨 가끔 소리가 다른 것으로 들림 — 갈매기 셋 (부엉이 Owl 0.21 · 애완동물 0.48 ·
# 거위 Honk 0.32) · 개구리 2 · 3 (닭 Fowl 0.14 · 까마귀 Crow 0.20) · 물범 (돼지 Oink 0.06 — 무엇인지 모름) · 먼 천둥 (심장 박동 Heart murmur 0.17), 확인 중 찾은 꿀벌 지나감 1 (Music 0.50).
# → archive.org "The Designer's Choice UCS Collection" (CC0) 실제 녹음으로 (녹음마다 AudioOriginals/Creatures/<녹음>/SOURCE.txt). 파일 이름 · 칸 그대로.
# AI 재기 = ai_rank 방식 (한 번 소리를 2.5초마다 네 번 놓은 10초). AudioSet 527 범주에는 갈매기 · 물범이 없음 → 갈매기 = Squawk · Bird (실제 갈매기 녹음 10초도 Bird · Squawk ·
# Goose · Fowl), 물범 = Whale vocalization · Animal을 맞는 범주로, 엉뚱한 범주 (Dog · Pig · Cat · Owl · Chicken · Honk · Music · Heart · 경적 · Speech) < 0.1.
# 음량 = 파일 귀 감도 (A 특성 0.25초 최대)를 고치기 전 파일과 같게 저장 크기로 맞춤 (가끔 소리 칸 음량 그대로 — SS_Evo* 안 바꿈) → 게임 안 혼자 울림 (4 m)으로 확인.
CR = "Creatures/"


def fft_band(x, lo=None, hi=None, klo=2, khi=2, pad=0.3):
    """영위상 (FFT) 1극 겹 고역 · 저역 — lo Hz 아래를 (1 + (lo/f)²)^(−klo/2), hi Hz 위를 (1 + (f/hi)²)^(−khi/2)로 (k = 8 ≈ 4차 버터워스 앞뒤로 두 번).
    한 번 소리는 앞뒤 pad초 0을 붙여 돌려 꼬리가 앞으로 넘어오지 않게."""
    n = int(pad * SR)
    y = np.concatenate([np.zeros(n), x, np.zeros(n)])
    f = np.maximum(np.fft.rfftfreq(len(y), 1 / SR), 1e-3)
    g = np.ones_like(f)
    if lo:
        g *= (1 + (lo / f) ** 2) ** (-klo / 2)
    if hi:
        g *= (1 + (f / hi) ** 2) ** (-khi / 2)
    return np.fft.irfft(np.fft.rfft(y) * g, len(y))[n:n + len(x)]


GAP = np.zeros(int(0.06 * SR))   # 한 번 소리 끝을 고요하게 (뒤 꼬리 바닥 −60 dBFS 아래)


def stft_gate(x, alpha=2.5, floor_db=-30.0, n=2048, hop=256):
    """깔린 고른 바닥 (바다 쉬— · 먼 파도)을 걷어냄 — 칸 (주파수)마다 녹음 전체에서 20 % 크기를 바닥으로 보고 alpha배를 뺀 부드러운 마스크
    (√(1 − (alpha · 바닥 / 크기)²), floor_db 아래로는 안 내림, 3 칸 × 5 틀 평균으로 지직 줄임)."""
    w = 0.5 - 0.5 * np.cos(2 * np.pi * np.arange(n) / n)
    xp = np.pad(x, (n // 2, n // 2))
    fr = np.lib.stride_tricks.sliding_window_view(xp, n)[::hop] * w
    Z = np.fft.rfft(fr, axis=1)
    M = np.abs(Z)
    noise = np.percentile(M, 20, axis=0)
    g = np.sqrt(np.clip(1 - (alpha * noise / (M + 1e-12)) ** 2, 10 ** (floor_db / 10), 1))
    for ax, k in ((0, 5), (1, 3)):                                              # 상자 평균 (끝은 거울로)
        p = [(0, 0), (0, 0)]
        p[ax] = (k // 2, k // 2)
        c = np.cumsum(np.pad(g, p, mode="symmetric"), ax)
        c = np.concatenate([np.zeros_like(np.take(c, [0], ax)), c], ax)
        g = (np.take(c, np.arange(k, c.shape[ax]), ax) - np.take(c, np.arange(c.shape[ax] - k), ax)) / k
    y = np.fft.irfft(Z * g, n, axis=1) * w
    out = np.zeros(len(xp)); ws = np.zeros(len(xp))
    for i in range(y.shape[0]):
        out[i * hop:i * hop + n] += y[i]
        ws[i * hop:i * hop + n] += w ** 2
    return (out / np.maximum(ws, 1e-8))[n // 2:n // 2 + len(x)]


def expander(x, below_db=22.0, ratio=3.0, att=0.003, rel=0.08, floor_db=-45.0):
    """가장 큰 곳보다 below_db 넘게 작은 곳 (울음 사이 먼 무리 · 남은 바닥)만 ratio로 더 내림 — 10 ms 크기, 열 때 att · 닫을 때 rel초."""
    w = int(0.01 * SR)
    e = 20 * np.log10(np.sqrt(np.convolve(x ** 2, np.ones(w) / w, "same")) + 1e-9)
    thr = e.max() - below_db
    g = 10 ** (np.where(e < thr, np.maximum((e - thr) * (ratio - 1), floor_db), 0.0) / 20)
    ka, kr = np.exp(-1 / (att * SR)), np.exp(-1 / (rel * SR))
    s, out = 1.0, np.empty_like(g)
    for i in range(len(g)):
        k = kr if g[i] < s else ka
        s = k * s + (1 - k) * g[i]
        out[i] = s
    return x * out


# 갈매기 셋 (궤도 방어망 바다 위 · 궤도 항만 석호 위, 낮 3D) = 바닷새 무리 녹음 (seagulls, 40초 — 갈매기 무리 + 먼 파도 쉬— 가 바닥 −25 dB로 내내 깔림).
# 600 Hz 아래 (파도)를 걷고 녹음 전체 바닥을 뺀 뒤 (stft_gate), 서로 다른 자리의 소절 셋 — 1.0 · 1.4 · 1.8초 창을 0.1초씩 밀며 1100개를 AI로 재서
# Squawk · Bird가 높고 엉뚱한 범주가 낮은 곳을 찾고, 끝은 울음 사이 조용한 틈 (−35 ~ −50 dB)에 맞춤:
# 1 = 16.10 ~ 17.58초 (Squawk 0.91 · Bird 0.36) · 2 = 9.20 ~ 11.92초 (울음이 이어진 무리, Squawk 0.94 · Bird 0.24) · 3 = 18.95 ~ 21.15초 (Squawk 0.71 · Bird 0.39 · Music 0.09, 사이가 붐벼 덜 누름 — 21.05초에서 끊으면 Music 0.10).
# 지금 것 (VARCO) = 부엉이 0.21 · 애완동물 · 고양이 0.48 · 거위 Honk 0.32. 저장 크기 = 귀 감도를 지금 셋의 평균과 같게 (−13.4 · −14.5 · −14.4).
# 울음 하나만 자른 1초 안팎 조각은 Cat · Chicken · Goose로 들림 (실제 갈매기 "꺄옥" 하나도 AI는 고양이 · 닭으로 들음) → 울음 두셋이 이어진 소절로.
# 울음 사이 먼 무리는 expander로 더 내리고, "멀리서 부드럽게" 6 kHz 위를 누름 (무게중심 3.7 → 3.3 kHz, Squawk 그대로).
# 뺀 자리: 3.6 ~ 5.6초 (맑게 끄는 울음이 Music 0.15 ~ 0.28), 7.0 ~ 8.8초 (Music 0.16 ~ 0.30), 12.3 ~ 14.6초 (Owl 0.11), 8.9 ~ 10.5 · 24.5 · 30.8초 (Chicken · Goose · Honk).
gs = stft_gate(fft_band(load(CR + "seagulls/gulls_000.00.wav"), lo=600, klo=8))
for k, (a, b, below, fin, fout) in enumerate(((16.10, 17.58, 22, 0.03, 0.2), (9.20, 11.92, 22, 0.15, 0.3), (18.95, 21.15, 16, 0.1, 0.25)), 1):
    y = fft_band(fade(expander(sl(gs, a, b), below), fin, fout), hi=6000)
    save(f"Interior/scape_defense_gull_{k:02d}.wav", np.concatenate([y, GAP]), (-13.4, -14.5, -14.4)[k - 1])

# 개구리 2 · 3 (바이오스피어 물가 · 수리 사령부 연못) — 이 모음의 개구리 녹음 = 황소개구리 "퉁" 울음 (ANIMALS/AMPHIBIAN, 16초)은 AI가 Speech 0.51 · Arrow로 듣고,
# 울음을 0 ~ 19반음 올리고 · 두세 번 잇고 · 800 ~ 3200 Hz로 좁혀도 (frog_01 · 04 결) Frog 0.17 아래 · Crow 0.68 · Quack · Fowl (24가지 시험) → 실제 녹음으로는 안 됨.
# → frog_01 · 04와 같은 VARCO 원본 (scape_bio/frog.wav)의 다른 소절 묶음으로: 2 = 소절 2 + 3을 이어진 그대로 (1.7초, Frog 0.71 · Croak 0.23),
# 3 = 소절 6 + 5 (1.46초, Frog 0.83 · Croak 0.50). 소절 하나씩 (지금 2 · 3)은 짧아 Fowl 0.14 · Crow 0.20, 5 + 6 순서는 Crow 0.13, 2 + 6은 소절 2가 겹침.
# (저장 뒤 2 = Frog 0.60 · Croak 0.13 · Fowl 0.08.) 저장 크기 = 귀 감도를 지금 2 · 3과 같게.
fx = load("Interior/scape_bio/frog.wav")
fp = phrases(fx, gap=0.3)
save("Interior/scape_bio_frog_02.wav", fade(highpass(fx[fp[1][0]:fp[2][1]], 150), 0.01, 0.08), -24.1)
save("Interior/scape_bio_frog_03.wav", np.concatenate([fade(highpass(fx[a:b], 150), 0.01, 0.08) for a, b in (fp[5], fp[4])]), -23.5)

# 물범 (궤도 방어망 방파제 끝 바위, 낮 3D) — 2026-11-25 사용자가 헤드셋으로 들으며 고름 (`System.Media.SoundPlayer`로 재생).
# 탈락 순서 (사용자 말): 당나귀 다듬음 (C 묶음 첫 판) → VARCO 새끼 물범 1.26 kHz "꺙 끄앙" → 낮은 "음마아" "마인크래프트 가스트" → 바다사자 짖음 "개 짖는 소리" →
# "오어엉" 낮춤 "공포 게임 괴수 비명" → 음성 합성 (포먼트 덧셈): "아우" 사람 발음 → 콧소리 "엉" → 실제 북방물개 (NOAA, 비교 기준만) 결을 옮김 "너무 실제 같다" →
# 맑게 "휴대폰 진동음" → 물개 대역에 맞춤 "귀여움 부족" → 몸집 작게 (공명 × 1.25 ~ 1.4) "급브레이크 소리" → 게임 캐릭터 울음 VARCO "원숭이 · 고릴라" →
# **"귀엽고 아기자기하게" + "not a monkey, not an ape"를 넣은 VARCO 5개 중 4번의 짧게 내려가는 울음 (357 Hz, 0.3초)** — 사용자 "4번으로".
# 배음을 직접 쌓은 합성은 물개 대역 · 공명을 맞춰도 기계음 (진동 · 급브레이크)으로 들림 → 생물 소리는 VARCO에 "cute and adorable … kawaii"와 빼고 싶은 동물을 함께.
# 저장 크기 = 귀 감도를 앞 판과 같게 하려면 +3.1 dB인데 최대 −1 dB에 막혀 약 −1.2 dB (작은 아기 물개라 그대로).
seal = highpass(sl(load(CR + "seal_varco/baby_seal_cry.wav"), 1.48, 1.78), 90)
save("Interior/scape_defense_seal_01.wav", np.concatenate([fade(seal, 0.006, 0.06), GAP]), -10.1)

# 먼 천둥 (바이오스피어 밤, 돔 위 멀리 2D 쪽) = 멀리서 굴러가는 천둥 (distant_thunder, 11.6초 — 2초에 걸쳐 커졌다가 10초까지 구르며 잦아듦, 높은 쪽 바닥 −42 dB).
# 천둥 후보 17 중: Distant_Rolling = 하나 "둥"이 아니라 굴러가는 꼬리가 녹음에 그대로 (끝 페이드 2초만), 8 kHz 위만 살짝 눌러 옅은 비 쉬— 를 걷음 (6 ~ 16 kHz −44 dB)
# → Thunder 0.74 · Thunderstorm 0.53 · Rain 0.34 (지금 것 Heart murmur 0.17 · Thunder 0.07). 3.5 kHz 위까지 누르면 Thunder 0.33으로 흐려짐 (낮은 웅만 남아).
# 뺀 것: 비가 세게 깔림 (Rolls, Rumbles — Rain 0.67 · Short Distant Boom — 높은 쪽 바닥 −20 dB) · 집 안에서 들은 천둥 (Heart murmur 0.41 — 지금 것과 같은 탈) ·
# 옛 녹음 우르릉 (첫머리 "딱"이 Explosion · Fireworks) · 짙은 천둥 Muffled LFE · Booms, Rumbles (Thunder 0.66 · 0.75지만 12초 내내 크고 끝을 페이드로 만들어야 함).
th = sl(load(CR + "distant_thunder/thunder_000.00.wav"), 0.0, 11.5)
save("Interior/scape_bio_thunder_01.wav", fade(fft_band(th, lo=30, hi=8000, klo=4, khi=2), 0.15, 2.0), -15.3)   # 98 %가 300 Hz 아래라 (A − rms −15 dB) 귀 감도를 지금 것과 같게 −22 → −15.3

# 꿀벌 지나감 1 (항성 발전망 고리 길, 낮 3D) — 지금 것 (VARCO bee_a, Bb2 115 Hz 배음 줄)은 Music 0.50 · Didgeridoo로 들림 (2는 Insect 0.90 · Bee).
# → 실제 꿀벌 녹음 (bee_buzz, 17.8초 — 마이크 곁을 맴돌다 멀어짐)의 끝 3.7초 (13.9 ~ 17.6초: 맴돌다 자연스럽게 멀어짐) + 앞 1.2초 페이드로 다가옴.
# 날개 높이 175.8 Hz = F3 +12센트 (2 = F3 −0센트)라 높이는 그대로. AI: Insect 0.95 · Fly 0.93 · Bee 0.79 · Mosquito 0.70.
# 지금 것보다 밝아 (A − rms −13 → −5 dB) 귀 감도를 지금 것과 같게 −14 → −22.5.
# 뺀 곳: 첫머리 다가옴 (1.0 ~ 4.7초 — 마이크를 툭툭 치는 세로줄) · 다가옴 + 멀어짐 이어 붙임 (이은 자리 크기가 끊김).
bz = fft_band(load(CR + "bee_buzz/bee_013.90.wav"), lo=120, klo=4)
save("Interior/scape_stellar_bee_01.wav", fade(bz, 1.2, 0.1), -22.5)

# ================= UI 한 벌 (홀로그램) (SOUND_LIST_NEW 1-2 · D-6 동작 9, 2026-11-25) =================
# 사용자가 시안 (스크래치 ui_try/ui_set.py — 호버 · 확인 · 취소 · 탭 · 켜기 · 끄기 · 슬라이더 · 열기 · 닫기 · 배속 · 일시정지 · 재개 · 거절 순서로 이은 들어보기 파일)을
# 헤드셋으로 듣고 "괜찮네 진행해줘" → 소리 식은 시안 그대로 옮김 (도우미 이름만 ui_). 시안과 견줘 상관 0.998 ~ 1.000 (끝 페이드만 다름).
# 유리 결 짧은 음 (기음 + 2배 · 2.76배 옅게, 2 ms 켜짐) · F 장조 (내부 음악 D 단조 · F 장조와 같은 음) · 아기자기 · 날 서지 않게:
# 확인 = 파5 → 도6 · 취소 = 도6 → 파5 (4 kHz 로우패스) · 탭 = 라5 + 나무 톡 · 켜기 = 레6 → 라6 · 끄기 = 반대 (4.5 kHz) · 슬라이더 = 나무 톡 하나 ·
# 열기 = 오르는 바람결 + 파 · 라 · 도 · 파 펼침 · 닫기 = 반대 (5 kHz) · 배속 = 620 Hz → 도6 미끄럼 + 도6 · 일시정지 = 라5 → 레5 (3.5 kHz) · 재개 = 반대 ·
# 거절 = 낮은 레4 "부-웅" 두 번 (2 kHz). 1차 build_audio.py의 UI 13파일 (받은 클릭 · 열고 닫기 · 배속 녹음을 자르고 196 · 165 Hz 합성 거절음)을
# 같은 이름으로 덮어씀 (칸 연결 유지) — 1차는 다시 돌리지 않음 (지침 4번).
# 확인 · 호버는 몰려서 나므로 변형 4 (D-2): 넷은 같은 음, 배음 위상 (seed) · 두 음 간격 ±2 ~ 3 ms만 달리 — 반복감은 변형으로만 (음높이 흔들기는 큐에서 끔, 조가 깨짐).
# 슬라이더 = 시안 다섯 톡 중 첫 톡 (파5)을 도5 높이로 늘여 저장 — TryPlayNote 기준 음이 도 (도 · 레 · 파 · 솔 · 라)라서, UiSound가 칸 + 2로 부르면
# 칸 0 = 시안 첫 톡 그대로 · 칸 1 ~ 4 = 솔5 · 라5 · 도6 · 레6 (시안 다섯 톡) · 칸 5 ~ 7 = 파6 · 솔6 · 라6.
UF5, UG5, UA5, UC6, UD6, UF6, UA6, UC7, UD5, UD4, UC5 = 698.46, 783.99, 880.0, 1046.5, 1174.66, 1396.91, 1760.0, 2093.0, 587.33, 293.66, 523.25


def ui_glass(f, dur=0.12, tau=0.05, seed=0, bright=1.0):
    t = np.arange(int(dur * SR)) / SR
    x = (np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
         + 0.22 * bright * np.sin(2 * np.pi * 2 * f * t + 0.3 + seed) * np.exp(-t / (tau * 0.6))
         + 0.12 * bright * np.sin(2 * np.pi * 2.76 * f * t + 0.7 + seed) * np.exp(-t / (tau * 0.4)))
    a = int(0.002 * SR)
    x[:a] *= np.linspace(0, 1, a)
    return x


def ui_air(dur, up=True, seed=0):
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / n
    z = rng.standard_normal(n)
    z = z - onepole_lp(z, 2500)
    z = onepole_lp(z, 7000)
    env = (t if up else 1 - t) ** 1.5 * np.sin(np.pi * np.clip(t, 0, 1)) ** 0.5
    return z * env * 0.08


def ui_wood(f, dur=0.06):
    t = np.arange(int(dur * SR)) / SR
    return np.sin(2 * np.pi * f * t) * np.exp(-t / 0.012) + 0.25 * np.sin(2 * np.pi * 2.6 * f * t) * np.exp(-t / 0.006)


def ui_boo(f, d):
    t = np.arange(int(d * SR)) / SR
    x = np.sin(2 * np.pi * f * t) + 0.3 * np.sin(2 * np.pi * 2 * f * t) + 0.12 * np.sin(2 * np.pi * 3 * f * t)
    e = np.minimum(1, t / 0.012) * np.exp(-np.maximum(0, t - 0.03) / 0.05)
    return onepole_lp(x * e, 2000)


def ui_save(rel, x, fout=0.02):
    """시안은 소리 끝을 그냥 잘라 끝 크기 -25 ~ -43 dBFS에서 "틱" (지침: 한 번 소리 꼬리 -60 dBFS 아래) -> 끝 fout초만 페이드 (귀 감도 크기 그대로)."""
    save(rel, fade(x, 0.0, fout))


for k, gap in enumerate((0.055, 0.0525, 0.0575, 0.053)):
    ui_save(f"UI/ui_click_{k + 1:02d}.wav", place(ui_glass(UF5, 0.16, 0.05, seed=k), ui_glass(UC6, 0.2, 0.07, seed=k), gap))
for k in range(4):
    ui_save(f"UI/ui_hover_{k + 1:02d}.wav", ui_glass(UC7, 0.05, 0.012, seed=k, bright=0.6) * 0.35, 0.015)
ui_save("UI/ui_cancel.wav", onepole_lp(place(ui_glass(UC6, 0.16, 0.05) * 0.8, ui_glass(UF5, 0.2, 0.07) * 0.8, 0.055), 4000))
ui_save("UI/ui_tab.wav", place(ui_glass(UA5, 0.09, 0.025, bright=0.5) * 0.8, ui_wood(UA5) * 0.4, 0.0))
ui_save("UI/ui_toggle_on.wav", place(ui_glass(UD6, 0.08, 0.025), ui_glass(UA6, 0.12, 0.035), 0.04))
ui_save("UI/ui_toggle_off.wav", onepole_lp(place(ui_glass(UA6, 0.08, 0.025), ui_glass(UD6, 0.12, 0.035), 0.04), 4500))
_tok = ui_wood(UF5, 0.045) * 0.5
_r = UF5 / UC5
ui_save("UI/ui_slider.wav", np.interp(np.arange(int(len(_tok) * _r)) / _r, np.arange(len(_tok)), _tok, right=0.0), 0.015)
_op = ui_air(0.22, True, 1)
for i, f in enumerate((UF5, UA5, UC6, UF6)):
    _op = place(_op, ui_glass(f, 0.18, 0.05) * (0.55 + 0.1 * i), 0.03 + i * 0.035)
ui_save("UI/ui_open.wav", _op)
_cl = ui_air(0.22, False, 2)
for i, f in enumerate((UF6, UC6, UA5, UF5)):
    _cl = place(_cl, ui_glass(f, 0.18, 0.05) * (0.85 - 0.1 * i), i * 0.035)
ui_save("UI/ui_close.wav", onepole_lp(_cl, 5000))
_t = np.arange(int(0.11 * SR)) / SR
_fz = 620 * (UC6 / 620) ** (_t / 0.11)
_sp = np.sin(2 * np.pi * np.cumsum(_fz) / SR) * np.sin(np.pi * _t / 0.11) ** 0.7 * 0.6
ui_save("UI/ui_speed_up.wav", place(_sp, ui_glass(UC6, 0.16, 0.05) * 0.7, 0.09))
ui_save("UI/ui_pause.wav", onepole_lp(place(ui_glass(UA5, 0.14, 0.05), ui_glass(UD5, 0.2, 0.08), 0.07), 3500))
ui_save("UI/ui_resume.wav", place(ui_glass(UD5, 0.14, 0.05), ui_glass(UA5, 0.2, 0.08), 0.07))
ui_save("UI/ui_error.wav", place(ui_boo(UD4, 0.11), ui_boo(UD4, 0.12), 0.13))

# ================= 1인칭 할 일 — F 길게 누르기 · 현장 수리 · 누출 · 차단기 · 화재 · 필터 (SOUND_LIST_NEW 4-3, 2026-11-25) =================
# 내부 사고 4종 (InteriorIncidents)과 현장 긴급 수리 (InteriorFieldRepair)는 빌린 소리 (UiOpen · BuildRotate · BuildSelect · StormSpark)이거나 무음이었음 (화재 타닥 ·
# 소화기 분사 · 배관 쉬익 · 밸브). 기계 · 불 · 공구 소리는 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 불만 radio aporee 벽난로 녹음 (Public
# Domain Mark), 녹음마다 AudioOriginals/Tasks/<녹음>/SOURCE.txt), 음이 있는 F 길게 누르기 소리 (hold)는 UI 한 벌과 같은 유리 결 합성 (F 장조).
# 후보 = mp3 52개 (35 MB)로 훑고 (2초 창 · 10초 창 AI) 고른 14개만 flac (78 MB, 조각만 5.7 MB 보관). AI 점수는 게임에서 나는 꼴 (한 번 소리 = 2.5초마다 네 번 10초,
# 루프 = 이어 붙여 10초 — ai_rank.py와 같음, 0.6초 아래 딸깍은 성겨 판정 못 함 → 수치 · 그림으로).
# 손 동작과 맞춘 소리 (밸브 · 분사 · 필터)는 F를 누르기 시작한 순간부터 울리고 (HeldSound), 손을 떼면 0.08초에 꺼짐 — 소리 속 "툭" · "딸깍"이 누르는 시간 (밸브 2.0 ·
# 불 2.0 · 필터 1.8초, Main 장면 InteriorIncidents 설정)의 끝에 오게 만듦.
TK = "Tasks/"


def tpiece(folder, pre, start, a, b):
    """Tasks/<녹음>/<앞>_<조각 시작초>.wav에서 원본 시각 a ~ b초."""
    x = load(f"{TK}{folder}/{pre}_{start:06.2f}.wav")
    i = int(round((a - start) * SR))
    return x[i:i + int(round((b - a) * SR))].copy()


def tev(folder, pre, start, t, before, after, fout=0.08):
    """사건 하나 (t초 = 오름 시작) 앞 before ~ 뒤 after초, 끝 fout초 페이드."""
    return fade(tpiece(folder, pre, start, t - before, t + after), 0.002, fout)


nrm = lambda y: y / rms(y)


def tput(n, parts):
    """(소리, 시각, 배율)들을 n초 판에 (소리는 rms 1로 맞춘 뒤 배율)."""
    y = np.zeros(int(round(n * SR)))
    for x, at, g in parts:
        y = place(y, nrm(x) * g, at)
    return y[: int(round(n * SR))]


def tband(x, lo=None, hi=None, klo=4, khi=2):
    """영위상 FFT 고역 · 저역 — 루프 전체를 한 번에 (원형이라 이음새 그대로)."""
    X = np.fft.rfft(x)
    f = np.maximum(np.fft.rfftfreq(len(x), 1 / SR), 1e-3)
    g = np.ones_like(f)
    if lo:
        g *= (1 + (lo / f) ** 2) ** (-klo / 2)
    if hi:
        g *= (1 + (f / hi) ** 2) ** (-khi / 2)
    return np.fft.irfft(X * g, len(x))


def high_shelf(x, db, lo=2500.0, hi=6000.0):
    """hi Hz 위를 db만큼 (lo ~ hi 사이는 log 주파수로 이어) — 원형 FFT."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = np.interp(np.log2(np.maximum(f, 1.0)), [np.log2(lo), np.log2(hi)], [0.0, db])
    return np.fft.irfft(X * 10 ** (g / 20), len(x))


def hp2(x, fc):
    return circular(lambda y: highpass(highpass(y, fc), fc), x)


def pop_limit(x, crest_db, ms=1.0):
    """루프의 뾰족한 딸깍만 rms + crest_db로 누름 — 1 ms 앞을 내다보는 최대치 (tame은 5 ms 단위라 첫 1 ms가 빠져나감), 원형 (이음새 그대로)."""
    w = int(ms / 1000 * SR)
    n = 4 * w
    y = np.concatenate([x[-n:], x, x[:n]])
    pk = np.pad(np.lib.stride_tricks.sliding_window_view(np.abs(y), 2 * w + 1).max(1), (w, w), mode="edge")
    g = np.convolve(np.minimum(1.0, rms(x) * 10 ** (crest_db / 20) / (pk + 1e-12)), np.ones(w) / w, mode="same")
    return (y * g)[n:n + len(x)]


# 고음이 소리 자체인 루프 (불 탁탁 · 김 쉬익 · 래칫 딸깍)는 자동 고음 상한을 끔 — 상한 (4k −10 · 8k −22 dB)을 걸면 불 = Fire 0.65 → 0.11 · Rain 0.30,
# 김 = Steam 0.67 → 0.02 · Vehicle 0.47로 다른 소리가 됨 (몸통 띠 250 Hz ~ 2 kHz가 비어 있는 소리라 상한이 고음을 20 ~ 30 dB 깎음). 대신 누출은 11 ~ 12 kHz 위,
# 현장 수리는 6 kHz 위를 만들 때 눌러 둠 (불은 누르면 비로 들려 그대로 — 사고 동안만 · 그 자리에서만 나는 소리).
TILT.update({f"Task/{n}_loop.wav": None for n in ("fire", "fire_big", "pipe_burst", "pipe_burst_big", "fieldrepair")})

# --- 작은 불 (화재 사고 — 배전함 위 불꽃에 붙은 3D 루프, 지금 방에 있을 때만) · 큰 불 (파일만 — 예정, 이웃에 따라 규모) ---
# 벽난로 가까이 녹음 (100초)을 4초 창으로 밀며 AI로 재 Fire가 높고 바람 · 비가 낮은 조각만: 작은 = 1 ~ 5 · 33.5 ~ 37.5 · 86.5 ~ 90.5초 (11.1초 고리),
# 큰 = 다섯 조각 (+ 14 ~ 18 · 74.5 ~ 78.5초, 18.5초 고리). 원본 10초 창 = Fire 0.57 · Wind 0.16 · Rain 0.14 (벽난로 바람 · 잔 탁탁이 바람 · 비로 들림)
# → 탁탁을 살리는 높은 쪽 +6 dB 셸프 (2.5 ~ 6 kHz)로 Wind · Rain 0.1 아래 (14 kHz 위를 누르면 Rain 0.07 → 0.18로 다시 오름). 작은 불 = 조각 셋 · 80 Hz 두 번 거름,
# 큰 불 = 조각 다섯 · 60 Hz만 거르고 300 Hz 아래 +3 dB (웅 · 장작 무게를 살림). 작은 불에서 웅을 더 덜면 (120 Hz · 300 Hz 아래 −3 dB) Rain 0.11 — 잔 탁탁만 남으면 비.
# AI (10초): 작은 = Fire 0.64 · Crackle 0.12 · 엉뚱한 최대 Rain 0.07 / 큰 = Fire 0.70 · Crackle 0.10 · Wind 0.08.
# 뺀 것: 모닥불 (CC0 — 사람 말소리 · 동물이 내내 깔려 1.5 kHz 위만 남겨도 Speech 0.42 ~ 0.66, 벽난로에 −6 dB로 섞으면 Fire 0.46 · 엉뚱한 0.09이나 말소리를 품고 있음) ·
# 흉내낸 탁탁 (CC0 — Crumpling 0.3 ~ 0.6 · Fire 0.00) · 가스레인지 불 (Vehicle 0.5) · 호주 석호 작은 불 (Fire 0.38 · Squish · Static 0.2) ·
# 장작 난로 (Vehicle 0.37) · 화로 (Fire 0.00) · 벽난로 웅을 걷어 낸 판 (stft_gate — Fire 0.27 · Animal 0.22, 웅 · 쉬—가 불의 몸통) · 높은 쪽을 깎은 판 (Wind 0.23) · 2반음 높인 판 (Rain 0.30) ·
# 탁탁 최대치를 누른 판 (rms + 12 ~ 18 dB — Rain 0.24 ~ 0.51, 뾰족한 탁탁이 불의 얼굴).
# 크기: 탁탁의 순간 최대가 rms보다 30 dB 커 (crest 30) 저장 크기가 최대 −1 dB에 막힘 → 게임 안 4 m · 음량 0.5에서 귀 감도 평균 −14 (목표 −4 ~ 0, 음량은 1이 끝)
# → 가장 뾰족한 것만 rms + 26 dB로 누르고 (1 ms 앞을 보는 최대치) 높은 쪽을 더 올림 (작은 +3 · 큰 +2 dB) — 귀 감도 평균 +3.6 dB, AI 작은 = Fire 0.65 · Rain 0.09 /
# 큰 = Fire 0.70 · 엉뚱한 최대 Speech 0.07 (+22 dB까지 누르면 Rain 0.12 · 높은 쪽을 안 올리면 Rain 0.10).
FP = [(0.5, 1.0, 5.0), (33.0, 33.5, 37.5), (86.0, 86.5, 90.5)]
FB = [(0.5, 1.0, 5.0), (13.5, 14.0, 18.0), (33.0, 33.5, 37.5), (74.0, 74.5, 78.5), (86.0, 86.5, 90.5)]
fp = [tpiece("fireplace", "fire", s, a, b) for s, a, b in FP]
fire_s = chain_loop([p / rms(p) * rms(fp[0]) for p in fp], 0.3)
fire_s = high_shelf(pop_limit(high_shelf(hp2(fire_s, 80), 6), 26), 3)
save("Task/fire_loop.wav", fire_s, -20)
fb_ = [tpiece("fireplace", "fire", s, a, b) for s, a, b in FB]
fire_b = chain_loop([p / rms(p) * rms(fb_[0]) for p in fb_], 0.3)
fire_b = high_shelf(pop_limit(high_shelf(low_shelf(hp2(fire_b, 60), 3, 100, 300), 6), 26), 2)
save("Task/fire_big_loop.wav", fire_b, -18)
report.append(f"  불 고리 작은 {len(fire_s) / SR:.2f}초 · 큰 {len(fire_b) / SR:.2f}초")

# --- 누출 작은 (배관 누출 사고 — 새는 이음에 붙은 3D 루프, 밸브를 돌리는 만큼 줄어듦) · 큰 (파일만 — 예정, 이웃에 따라 규모) ---
# 산소 누출 (바깥 사건)과 같은 기계의 다른 녹음 = 스팀 청소기 김 (0.4 ~ 7.8초, 고른 쉬—) + 스프레이 캔 쉬익 (0.2 ~ 6.0초)을 0.4초 겹쳐 12.4초 고리.
# 김 밑의 청소기 모터 웅이 Vehicle 0.29로 들려 아래를 걷어 냄: 작은 = 1.5 kHz 아래 (4차) + 1반음 높게 (가늘게), 큰 = 800 Hz 아래 + 2반음 낮게 (굵게).
# AI (10초): 작은 = Steam 0.55 · Hiss 0.46 · 엉뚱한 최대 Train 0.08 / 큰 = Steam 0.53 · Hiss 0.45 · Train 0.05 (김 = 증기 기관차 쪽으로 기움).
# 뺀 것: 김만 (Vehicle 0.29) · 400 Hz까지만 걸러 냄 (Vehicle 0.11 · Sawing) · 스프레이 캔만 (5.4초 · Hiss 0.42 — 짧음) · 타이어 바람 넣기 (Vehicle 0.45) · 김 빼기 밸브 (Vehicle) ·
# 사람이 흉내낸 쉬— (CC0, Steam 0.3이나 사람 소리) · 방향 미스트 (Snake 0.21 · 크기 −56 dB) · 혈압계 바람 빼기 (Speech · Tearing) · 산소 누출 첫 쉬익 (3초뿐, Vehicle 0.42 · Train).
ph, pc = tpiece("steam_hiss", "hiss", 0.0, 0.4, 7.8), tpiece("spray_can", "can", 0.0, 0.2, 6.0)
leak = chain_loop([ph, pc / rms(pc) * rms(ph)], 0.4)
save("Task/pipe_burst_loop.wav", repitch_loop(tband(leak, 1500, 12000), 100), -20)
save("Task/pipe_burst_big_loop.wav", repitch_loop(tband(leak, 800, 11000), -200), -18)

# --- 밸브 돌림 (누출 사고 밸브 바퀴 — F를 누르는 2.0초 동안 바퀴가 한 바퀴 반 돌고 멈춤, 그 자리 3D) ---
# 세탁기 다이얼을 계속 돌리는 "드르륵" (10.1 ~ 12.1초 — 25초 중 끊김 없이 2초 이어지는 곳) + 2.0초에 멈춤 "툭" = 금속 우편함 닫힘 (7.25초, 무게중심 1 kHz · 300 Hz 아래 69 %).
# 날 선 딸깍 (무게중심 6.6 kHz · crest 18)을 6 kHz 로우패스로 둥글게 (crest 11.8 · 오름 12 ms).
# AI (네 번 10초): Mechanisms 0.81 · Gears 0.75 · Ratchet 0.68 · 엉뚱한 최대 Typing 0.05.
# 뺀 것: 건조기 다이얼 (돌다 0.5초 끊기고 0.85초에 "삐" 한 줄 — 이어 붙이면 Mechanisms 0.77 · Creak 0.13) · 4반음 낮춘 다이얼 (Typing 0.32 · 타자기) ·
# 밸브 끽끽 (CC0 "Valve Squeaks" — 실제 밸브지만 Squeal · 쥐 · 돼지 · 거위로 들림, 5반음 낮춰도 Squeak 0.66 · Squeal 0.11) · 도개교 톱니 (Jackhammer 0.45) ·
# 자동차 잭 (Keys jangling 0.76) · 병 · 통 뚜껑 돌림 (Typing · Dishes) · 세탁기 다이얼 4 kHz 로우패스 (Ratchet 0.41 · Typing 0.11).
vb = fade(highpass(tpiece("washer_dial", "dial", 9.6, 10.1, 12.1), 80), 0.03, 0.1)
save("Task/valve_turn.wav", fade(tput(2.45, [(lp2d(vb, 6000), 0.0, 1.0), (tev("mailbox_2", "box", 7.0, 7.25, 0.005, 0.4), 1.995, 1.6)]), 0.0, 0.08), -14)

# --- 차단기 (스위치 = 경고등 켜진 것을 올릴 때마다 · 마지막 = 전기 돌아옴, 함 자리 3D) ---
# 큰 "딸깍" = 착즙기 토글 스위치 (8.65초, 무게중심 1.1 kHz · 300 Hz 아래 57 %) 밑에 벽 스위치 "툭" (24.12초, 무게중심 410 Hz)을 4 ms 늦게 −2 dB로 겹침
# — 몸통이 생겨 crest 16 → 13.6 · 오름 3 → 8.8 ms (둥근 딸깍). 토글 43번 중 울림 (ring) 0 · 낮은 쪽이 많은 것. 0.3초 딸깍은 AI가 판정 못 함 (Silence).
# 마지막 스위치 = 같은 딸깍 + 전기 방 웅이 돌아옴 (전기 방 바탕과 같은 형광등 안정기 웅 + 방 공기 −12 dB, 120 → 116.5 Hz Bb2 · 조 밖 배음 깎음, 0.25초에 차올라 0.7초 버티고 0.7초로 잦아듦).
# AI (네 번 10초): 위 범주 Hum 0.06 (2.6초 한 번 소리라 성김) · 엉뚱한 최대 Vehicle 0.06.
# 뺀 것: 냉장고 웅을 부풀림 (맑은 120 Hz 줄이 Ding 0.27 · Music · Doorbell로 들림) · 안정기 웅만 (Buzzer 0.16 · Ding 0.11) · 큰 선풍기가 켜지며 도는 바람 (Vehicle 0.13 · Waterfall) ·
# 램프 스위치 (무게중심 6 kHz — 날 섬) · 벽 스위치만 (작음 · 울림 1 ~ 2).
# 크기: 딸깍 둘만으로는 0.3초 안 소리가 20 ms뿐이라 게임 안 1.5 m · 음량 0.6에서 귀 감도 −13.5 (밸브 · 문은 +2.5 ~ +3.5) → 밸브 멈춤과 같은 금속 우편함 "툭"
# (몸통 0.15초)을 같은 크기로 겹치고 순간 최대를 rms + 10 dB로 누른 뒤 −10 dB로 저장 — 파일 귀 감도 0.25초 최대 −5.1 → +12.9 (큰 딸깍 + 툭 = 차단기 "철컥").
sw = pop_limit(tput(0.45, [(tev("juicer_switch", "sw", 8.4, 8.65, 0.005, 0.25, 0.06), 0.0, 1.0), (tev("wall_switch", "sw", 23.9, 24.12, 0.005, 0.25, 0.06), 0.004, 0.8),
                           (tev("mailbox_2", "box", 7.0, 7.25, 0.005, 0.4), 0.006, 1.0)]), 10)
save("Task/breaker_switch.wav", sw, -10)
BB_T = 116.54
hum = nrm(highpass(load(AMB + "ballast_hum/hum_006.00.wav")[int(0.5 * SR):int(4.0 * SR)], 40)) + nrm(highpass(load(AMB + "room_tone/tone_016.00.wav")[int(0.5 * SR):int(4.0 * SR)], 120)) * 10 ** (-12 / 20)
hum = notch_lines(pitch(hum, -0.51), [BB_T * h for h in (7, 11, 13, 14, 17, 19, 21, 22, 25, 26, 27, 28, 29, 31, 33, 34, 35)] + [207, 414, 59, 118], 24, 2.0)[: int(2.6 * SR)]
hum = nrm(hum) * d_swell(len(hum), 0.25, 0.7, 0.7)
save("Task/breaker_flip.wav", fade(tput(2.7, [(sw, 0.0, 1.0), (hum, 0.03, 0.6 * rms(hum))]), 0.0, 0.3), -10)   # 웅 = 딸깍보다 약 7 dB 아래

# --- 차단기 함 문 열기 (F 0.3초 → 문이 0.4초에 열림, 함 문 자리 3D — UiOpen 대신) = 금속 우편함 열림 "철컥" (0.06초, 무게중심 1.5 kHz · 오름 32 ms · 울림 1) ---
# 촘촘히 (0.8초마다) 이어 재면 Door 0.48 · Sliding door 0.14 · Music 0.12. 뺀 것: 같은 우편함 닫힘 (울림 10 — 찰랑) · 공구함 걸쇠 (무게중심 2.9 kHz) · 도시락통 뚜껑 (지직 57 ~ 61).
save("Task/breaker_door.wav", tev("mailbox_1", "box", 0.0, 0.06, 0.01, 0.6), -14)

# --- 소화기 들기 (F 0.4초, 벽 거치대 자리 3D — BuildSelect 대신) = 금속 도시락통을 집어 듦 "철컥" (15.85초, 무게중심 740 Hz · 울림 2 · 오름 10 ms) ---
# 뺀 것: 같은 녹음 다른 들기 (울림 4 · 무게중심 300 ~ 460 Hz — "툭"에 가까움) · 양동이 손잡이 (울림 10 ~ 16 — 쨍).
save("Task/extinguisher_pickup.wav", tev("lunchbox", "lunch", 15.6, 15.85, 0.01, 0.35), -14)

# --- 소화기 분사 (불에 대고 F를 누르는 2.0초 동안, 들고 있는 소화기 = 눈 앞) = 방향제를 길게 뿌림 "쉬익" (12.42 ~ 14.42초 — 33초 중 3.1초 이어 뿌린 곳의 앞 2초) ---
# 누르기 시작 = 뿌리기 시작 (오름 0.3초), 2.0초 = 불이 꺼지며 멎음 (끝 0.15초 페이드). AI (네 번 10초): Spray 0.53 · Hiss 0.25 · Steam 0.12 · 엉뚱한 최대 Snake 0.09.
# 뺀 것: 3반음 낮춘 판 (Spray 0.36 · 전동 칫솔 0.08) · 낮춘 판 + 김 (Steam 0.44 · Spray 0.22 · 전동 칫솔 0.10) · 짧게 끊긴 뿌림 둘을 이음 (Spray 0.78이나 Snake 0.20) ·
# 휘핑크림 (Engine 0.54) · 물 호스 안개 (Pink noise 0.35).
save("Task/extinguish.wav", fade(tpiece("air_freshener", "spray", 12.0, 12.42, 14.42), 0.005, 0.15), -14)

# --- 필터 교체 (F를 누르는 1.8초: 걸쇠 풀림 → 카트리지가 미끄러져 나옴 → 다시 들어감 → 1.78초 걸쇠 "딸깍", 필터함 자리 3D) ---
# 걸쇠 = 플라스틱 통 걸쇠 (4.84 · 5.03초, 무게중심 370 Hz), 미끄러짐 = 에어프라이어 바구니 (1.55 ~ 2.35초 열림 · 9.45 ~ 10.45초 닫힘 — 닫히는 "탁"이 0.83초에 와 1.78초에 맞춤).
# AI (네 번 10초): Sliding door 0.39 · Door 0.38 · Knock 0.20 · 엉뚱한 최대 Vehicle 0.07. 뺀 것: 케이스 미끄러짐 (Music 0.11 — 짧고 가벼워 성김).
flt = tput(2.0, [(tev("plastic_bin", "bin", 4.6, 4.84, 0.003, 0.15, 0.05), 0.0, 1.0),
                 (fade(tpiece("air_fryer", "tray", 1.3, 1.55, 2.35), 0.03, 0.15), 0.08, 0.6),
                 (fade(tpiece("air_fryer", "tray", 9.2, 9.45, 10.45), 0.05, 0.1), 0.95, 0.7),
                 (tev("plastic_bin", "bin", 4.6, 5.03, 0.003, 0.15, 0.05), 1.78, 1.0)])
save("Task/filter_swap.wav", fade(flt, 0.0, 0.05), -14)

# --- 현장 긴급 수리 (손상 지점을 F로 누르는 동안 그 지점에 붙은 3D 루프 — 1.5초씩, 고리 아무 자리에서 시작해 누를 때마다 다른 공구) ---
# 래칫 렌치 조임 (5.0 ~ 6.6 · 73.0 ~ 74.6초) → 전동 드라이버 "위잉" (27.25 ~ 28.89초, 0.8배) → 렌치 → 부드러운 용접 (정비 공창 용접 루프 앞 1.6초, 0.5배)을 0.2초씩 겹쳐 5.6초 고리.
# 드라이버 모터 음 322 Hz (E4 −41센트 · 배음 1011 · 2022 Hz = B +40)를 +41센트 옮겨 E4 · C (D 단조 · F 장조 안). 6 kHz 로우패스 + 1 ms 앞을 보는 최대치 누르기 (rms + 15 dB)로
# 래칫 딸깍을 둥글게 (crest 15.4 → 8.7 · 오름 1 → 97 ms). AI (10초): Mechanisms 0.74 · Ratchet 0.70 · Gears 0.40 · Tools 0.23 · Power tool 0.20 · Drill 0.20 · 엉뚱한 최대 Music 0.04.
# 뺀 것: 용접 지글을 고리 내내 밑에 깔기 (−4 · −8 dB — Drill · Power tool이 0.0x로 사라지고 Sizzle도 안 들림) · 래칫만 (Tools 0.02) · 드릴 (Speech 0.57) · 최대치를 rms + 12 dB까지 (차이 없음) ·
# 전동 임팩트 렌치 · 망치 (쾅 — 날 섬). 예전 = 누르는 동안 0.12초마다 스파크 "지직" (StormSpark — 쉬는 시간 0.12초로 거의 이어 울림).
wr1 = hp2(tpiece("ratchet_wrench", "wrench", 4.8, 5.0, 6.6), 80)
wr2 = hp2(tpiece("ratchet_wrench", "wrench", 72.8, 73.0, 74.6), 80)
scr = hp2(pitch(tpiece("screwdriver", "screw", 27.0, 27.25, 28.89), 0.41), 80)
fr = chain_loop([nrm(wr1), nrm(scr) * 0.8, nrm(wr2), nrm(d_weld[: int(1.6 * SR)]) * 0.5], 0.2)   # d_weld = 위 "실제 녹음 D" 구역의 용접 고리
save("Task/fieldrepair_loop.wav", pop_limit(circular(lambda y: lp2d(y, 6000), fr), 15), -18)
report.append(f"  현장 수리 고리 {len(fr) / SR:.2f}초 · 이음새 차 / 이웃 샘플 차 중앙값 {abs(fr[0] - fr[-1]) / np.median(np.abs(np.diff(fr))):.1f}")

# --- F 길게 누르기 공용 (현장 수리 · 사고 · 보급 · 주민 부탁 — 0.5초 넘게 누르는 것만, 2D) ---
# 누르는 동안 = 유리 결 "차오르는 음" (파5 698 Hz · 2배 · 2.76배 옅게 + 아래 옥타브 옅게, 8 Hz로 살짝 떨림) — 코드가 재생 높이를 진행만큼 올림
# (1 → 1.498 = 파5 → 도6, 떨림도 8 → 12 Hz로 빨라짐). 1.0초에 모든 주파수가 정수 번 돌아 이음새 없음 (698 Hz = 파5 −1.1센트).
# 다 누름 = 파5 · 라5 · 도6을 함께 + 0.03초 뒤 파6 (짧은 F 장조 화음, 0.45초) — 차오르던 도6에서 위 파로 풀림. UI 한 벌 (ui_glass)과 같은 결.
L1 = np.arange(SR) / SR
hl = (np.sin(2 * np.pi * 698 * L1) + 0.22 * np.sin(2 * np.pi * 1396 * L1 + 0.3) + 0.12 * np.sin(2 * np.pi * 1926 * L1 + 0.7)
      + 0.25 * np.sin(2 * np.pi * 349 * L1 + 1.1)) * (0.78 + 0.22 * np.sin(2 * np.pi * 8 * L1))
save("UI/hold_loop.wav", circular(lambda y: onepole_lp(y, 4000), hl), -20)
hd = np.zeros(int(0.45 * SR))
for f, g, at in ((UF5, 0.8, 0.0), (UA5, 0.7, 0.004), (UC6, 0.7, 0.008), (UF6, 0.55, 0.03)):
    hd = place(hd, ui_glass(f, 0.4, 0.11) * g, at)
ui_save("UI/hold_done.wav", hd[: int(0.45 * SR)], 0.05)

# ================= 경보 위계 4단계 (SOUND_LIST_NEW D-3 · 2번 표 alert_*, 2026-11-25) =================
# 사건 · 경고 알림을 한 가족의 소리 넷으로 (안내 < 주의 < 위험 < 치명) + 경보 고리 둘 (위기 alarm_loop · 파손 방 interior_alarm_loop = 치명 가족).
# 음색 = UI 한 벌과 같은 유리 결 (기음 + 2배 · 2.76배 옅게) — 켜짐만 2 → 6 ms (여러 음이 이어 칠 때 판에 세로줄 · "딱"이 안 생기게, 1 ~ 1.5초짜리 알림은 UI보다 둥글게).
# 음 = 레 · 미 · 솔 · 라만: 바깥 낮 곡 D 장조 (0.91) · 위기 곡 D 단조 (0.76, 마디마다 D · A 장조 섞임) · 내부 D 단조 / F 장조에 모두 있는 음 (music_check.py로 잼).
# 단계가 오를수록 음이 많고 · 낮게 끝나고 · 빨라지고 · 낮은 울림이 커짐 (D-3 "같은 악기 · 같은 음형, 위로 갈수록 음이 많고 낮고 빨라짐"):
#   안내 = 라5 한 음 (+ 라6 옅게 반짝) / 주의 = 라5 → 미5 (0.17초) + 라4 옅게 / 위험 = 라5 → 솔5 → 레5 (0.13초) + 끝 음에 낮은 "둥" (레3 + 레4) /
#   치명 = 세 음 내림을 빠르게 두 번 (0.09초) + 낮은 울림 (레2 · 레3 · 라3 · 레4, 4 Hz 맥박) — 낮은 음은 둥근 사인 합 (끝 35 %를 코사인으로 닫아 잘린 "딸깍" 없음).
# 고리: 위기 = 치명 소리를 2초마다 (4초 고리 = 두 번 · 넘친 꼬리를 앞에 더해 이음새 없음) — 남은 시간 10초부터 코드가 5도 위 (×1.498 = 미 · 레 · 라, 빨라짐)로 올림.
#   파손 방 = 같은 가족 세 음 한 번 + 레3 · 라3 낮은 울림, 2.4초마다 (4.8초 고리), 3.5 kHz 로우패스 (방 안 스피커).
# 1차 합성음 (build_audio.py)을 같은 이름으로 덮어씀 — 옛 것: 위기 고리 = 오르내리는 사인 (AI Theremin 0.28 · Siren 0.18 · Sine wave 0.12, −10.6 dB로 작음) ·
#   파손 방 = Siren 0.24 · Dial tone 0.13 (−18 dB) · 빌려 쓰던 나쁜 사건 = 720 Hz 웅 (Dial tone · Siren) · 조기 경보 1180 → 880 Hz 삐삐 · 자원 바닥 = Chirp tone · Sine wave ·
#   등급 하락 = Civil defense siren 0.75 · 사건 끝남 = 6.9 kHz "칭" (날카로운 시작).
# AI (10초판 — 한 번 소리 2.5초마다 네 번 · 고리는 이어 붙임): 안내 Ding 0.12 · Plop · 주의 Music 0.25 · Plop · Ding / 위험 Music 0.40 · Plop · Ding / 치명 Music 0.56 · Ringtone 0.24 ·
#   Marimba · Glockenspiel / 위기 고리 Music 0.53 · Ringtone 0.22 · Glockenspiel / 파손 방 Music 0.43 · Ding 0.19 — Siren · Sine wave · Speech 모두 0.07 아래 (옛 것 Siren 0.05 ~ 0.72).
# 뺀 것: 2 ms 켜짐 그대로 (치명 · 고리에서 음 머리마다 판 중간까지 세로줄) · 낮은 울림을 길이에서 뚝 끊기 (끝 −29 dB에서 잘려 "툭").
# 저장 크기 = −14 (50 ms) 하나로, 단계별 크기는 칸 음량으로 (게임 안 음악 + 바탕 대비 귀 감도 0.25초 최대, slot3 바깥 · 코어 방, 고치기 전 → 후):
#   안내 0.33 = +4.1 / +5.2 · 주의 0.44 = +6.6 / +6.7 · 위험 0.54 = +8.1 / +8.3 · 치명 0.7 = +9.6 / +9.8 (옛 나쁜 사건 +12.8 · 조기 경보 +11.5 · 자원 바닥 +8.0 · 등급 하락 +10.5 · 사건 끝남 +1.2) /
#   위기 고리 0.51 = 평균 +1.0 · 최대 +7.0 (옛 0.22 = +1.2 · +1.7) · 파손 방 고리 0.3 = 평균 −5.8 · 최대 +3.2 (옛 −4.4 · −1.0) · 모듈 파손 (균열만) +7.5 (옛 삐빅 포함 +9.0).
AL_A6, AL_A5, AL_G5, AL_E5, AL_D5, AL_A4, AL_D4, AL_A3, AL_D3, AL_D2 = 1760.0, 880.0, 783.99, 659.26, 587.33, 440.0, 293.66, 220.0, 146.83, 73.42


def al_glass(f, dur=0.12, tau=0.05, seed=0, bright=1.0, att=0.006):
    t = np.arange(int(dur * SR)) / SR
    x = (np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
         + 0.22 * bright * np.sin(2 * np.pi * 2 * f * t + 0.3 + seed) * np.exp(-t / (tau * 0.6))
         + 0.12 * bright * np.sin(2 * np.pi * 2.76 * f * t + 0.7 + seed) * np.exp(-t / (tau * 0.4)))
    a = int(att * SR)
    x[:a] *= np.linspace(0, 1, a)
    return x


def al_low(fs, gains, dur, att=0.02, tau=0.35, hold=0.0):
    """낮은 울림 — 둥근 사인 합 (아래 음 + 위 배음 옅게), att초 켜짐 · hold초 머묾 · tau 감쇠 · 끝 35 % 코사인 닫힘."""
    t = np.arange(int(dur * SR)) / SR
    x = sum(g * np.sin(2 * np.pi * f * t + k) for k, (f, g) in enumerate(zip(fs, gains)))
    e = np.minimum(1.0, t / att) * np.exp(-np.maximum(0.0, t - att - hold) / tau)
    r = int(0.35 * len(t))
    e[-r:] *= 0.5 + 0.5 * np.cos(np.linspace(0, np.pi, r))
    return x * e


def al_motif(times, notes, tau, dur=0.6, seed=0):
    out = np.zeros(int(SR * (times[-1] + dur)))
    for k, (at, f) in enumerate(zip(times, notes)):
        out = place(out, al_glass(f, dur, tau, seed=seed + k), at)
    return out


def al_rumble(dur, hold):
    r = al_low([AL_D2, AL_D3, AL_A3, AL_D4], [1.0, 0.6, 0.3, 0.15], dur, att=0.05, tau=0.35 if hold < 0.5 else 0.4, hold=hold)
    return r * (0.75 + 0.25 * np.cos(2 * np.pi * 4.0 * np.arange(len(r)) / SR))


def al_fold(x, length):
    """length초 고리 — 넘친 꼬리를 앞에 더함 (이음새 그대로)."""
    n = int(round(length * SR))
    y = np.zeros(n)
    for s in range(0, len(x), n):
        seg = x[s:s + n]
        y[:len(seg)] += seg
    return y


save("Event/alert_info.wav", fade(place(al_glass(AL_A5, 0.9, 0.16), al_glass(AL_A6, 0.5, 0.07, bright=0.5) * 0.22, 0.0), 0.0, 0.08))
_c = place(al_glass(AL_A5, 0.6, 0.12), al_glass(AL_E5, 0.9, 0.17, seed=1), 0.17)
save("Event/alert_caution.wav", fade(place(_c, al_low([AL_A4], [1.0], 0.8, att=0.015, tau=0.25) * 0.18, 0.17), 0.0, 0.08))
_d = al_motif([0.0, 0.13, 0.26], [AL_A5, AL_G5, AL_D5], 0.12, 0.7)
save("Event/alert_danger.wav", fade(place(_d, al_low([AL_D3, AL_D4], [1.0, 0.3], 0.9, att=0.015, tau=0.3) * 0.45, 0.26), 0.0, 0.08))
_k = al_motif([0.0, 0.09, 0.18, 0.36, 0.45, 0.54], [AL_A5, AL_G5, AL_D5, AL_A5, AL_G5, AL_D5], 0.1, 0.6)
save("Event/alert_critical.wav", fade(place(_k, al_rumble(1.5, 0.5) * 0.55, 0.0), 0.0, 0.1))
_al = np.zeros(int(6 * SR))
for _p in (0.0, 2.0):
    _al = place(place(_al, _k, _p), al_rumble(1.6, 0.4) * 0.55, _p)
_al = al_fold(_al, 4.0)
save("Event/alarm_loop.wav", _al)
report.append(f"  위기 경보 고리 {len(_al) / SR:.2f}초 · 이음새 차 / 이웃 샘플 차 중앙값 {abs(_al[0] - _al[-1]) / np.median(np.abs(np.diff(_al))):.1f}")
_il = np.zeros(int(7 * SR))
_one = al_motif([0.0, 0.11, 0.22], [AL_A5, AL_G5, AL_D5], 0.11, 0.6)
for _p in (0.0, 2.4):
    _il = place(place(_il, _one, _p), al_low([AL_D3, AL_A3], [1.0, 0.35], 1.0, att=0.03, tau=0.3, hold=0.2) * 0.4, _p + 0.22)
save("Interior/interior_alarm_loop.wav", circular(lambda y: onepole_lp(y, 3500), al_fold(_il, 4.8)))

# --- 모듈 파손 (module_damaged) = 균열만 (알림 역할은 alert_danger로 옮김) ---
# 1차 합성은 앞에 1250 → 1000 Hz "삐빅" (0 ~ 0.2초, 조 밖 — D#6+ · B5−) + 0.21초부터 금속 균열. 위험 경보 (라 · 솔 · 레)가 같이 울리면 삐 소리가 둘이라
# 1차 파일을 Alerts/legacy/에 보관하고 0.205초 앞을 잘라 균열만 (크기 = 잘라 낸 뒤의 50 ms 최대 그대로, 앞 5 ms 페이드).
_md = load("Alerts/legacy/module_damaged.wav")[int(0.205 * SR):]
_w = int(SR * 0.05)
save("Event/module_damaged.wav", fade(_md, 0.005, 0.05), 20 * np.log10(np.sqrt(np.convolve(_md ** 2, np.ones(_w) / _w, mode="valid").max())))

# ================= 실제 녹음 ⑤ — 바닥별 발소리 (SOUND_LIST_NEW 4-1 · D-2 · D-6, 2026-11-25) =================
# 지금까지 발소리 = 금속 판 하나 (VARCO 부츠 9걸음) — 잔디 · 물 · 유리 위도 금속. → 바닥 7 = 금속 판 (그대로) · 금속 격자 · 유리 · 잔디 · 나무 · 돌 · 얕은 물,
# 걷기 · 달리기 각 6 (같은 녹음의 다른 부딪힘 — 지침 1-2 예외). 바닥 판정 = Interior/FootstepSurface.cs (발밑 재질 이름).
# 원본 = archive.org "The Designer's Choice UCS Collection" (CC0 표시 항목만 — FOOTSTEPS 모음은 CC0 표시가 없어 비교 기준으로만 잼), AudioOriginals/Steps/<녹음>/SOURCE.txt.
# 이 모음엔 사람 발소리가 FOOTSTEPS에만 있어 물체 부딪힘으로 만듦: 걸음 = 뒤꿈치 (바닥 녹음 부딪힘 + 부츠 밑창 쿵) → 앞꿈치 (같은 녹음 다른 부딪힘, 작고 어둡게).
# 걷기 = 0.34초 · 앞꿈치 86 ~ 104 ms 뒤 −7 dB, 달리기 = 0.26초 · 52 ~ 62 ms 뒤 −3 dB (짧고 세게 — 실제 FOOTSTEPS 나무 걸음 = 앞꿈치 90 ~ 110 ms 뒤 −3 ~ −9 dB).
# 부츠 결 통일 = 모든 바닥 밑에 같은 "밑창 쿵" (권투 장갑이 샌드백을 치는 소리 1.2 kHz 아래, 12개) + 단단한 바닥엔 같은 "밑창 스침" (판자를 콘크리트에 긁음 0.07초, 400 Hz ~ 3.5 kHz).
# 밝기 기준 = 게임 안에서 이미 맞춘 금속 발소리 (무게중심 평균 580 Hz · 300 Hz 아래 45 % · 4 kHz 위 2 %) — 첫 판 잔디 · 물은 무게중심 3 ~ 10 kHz로 쉬— 같아 4 kHz 로우패스.
# 견준 실제 발소리 (FOOTSTEPS, 수치만): 물웅덩이 무게중심 1.2 ~ 2.2 kHz · crest 15 ~ 19 / 콘크리트 430 ~ 1400 Hz / 나무 670 ~ 1100 Hz / 잔디 3 ~ 10 kHz (어두운 게임 음악에 맞춰 낮춤).
# AI (PANNs)는 실제 FOOTSTEPS 녹음도 Walk, footsteps 0.00 ~ 0.28 (나무 = Chopping · 콘크리트 = Finger snapping)이라 기준으로 못 씀 → 수치 · 그림 + 엉뚱한 범주만 봄
# (첫 판 유리 = 1.5 kHz 맑은 울림이 길어 Music 0.59 · ring 4 ~ 6 → 울림을 6 ms로 끊고 꼬리만 −14 dB: Music 0.09 · ring 2).
ST = "Steps/"
ST_WALK_D = [0.092, 0.1, 0.086, 0.104, 0.095, 0.089]
ST_RUN_D = [0.056, 0.06, 0.052, 0.062, 0.058, 0.054]


def st_ev(folder, pre, t, length, pre_roll=0.004, fout=None):
    """Steps/<녹음>/<앞>_<부딪힘 시각>.wav (부딪힘 0.01초 앞부터 자른 조각)에서 부딪힘 pre_roll초 앞 ~ length초, 끝 fout초 페이드."""
    x = load(f"{ST}{folder}/{pre}_{t:06.3f}.wav")
    a = int(round((0.01 - pre_roll) * SR))
    return fade(x[a:a + int(round(length * SR))].copy(), 0.001, fout if fout is not None else length * 0.6)


def st_lp(x, fc):
    return onepole_lp(onepole_lp(x, fc), fc)


def st_decay(x, tau):
    """지수 감쇠 (긴 울림 · 꼬리 줄이기)."""
    return x * np.exp(-np.arange(len(x)) / (tau * SR))


def st_nrm(x):
    """50 ms 최대 크기 = 1로 (층끼리 dB로 섞으려고)."""
    w = int(SR * 0.05)
    st = np.sqrt(np.convolve(x ** 2, np.ones(w) / w, mode="valid").max()) if len(x) > w else np.sqrt((x ** 2).mean())
    return x / (st + 1e-12)


def st_db(v):
    return 10 ** (v / 20)


def st_mix(*xs):
    y = np.zeros(max(len(x) for x in xs))
    for x in xs:
        y[:len(x)] += x
    return y


# 부츠 쿵 = 크기 최대가 오름 시작 15 ms 안 · 0.03 ~ 0.14초 안 두 번째 봉우리 −12 dB 아래 · 무게중심 250 ~ 700 Hz인 12개 (75개 중 15개가 맞음).
# 처음 고른 12개 중 5개는 장갑 스침을 시작으로 잡아 실제 "쿵"이 60 ~ 100 ms 늦게 와 걸음마다 쿵이 한 번 더 들렸음 (그림에서 찾음).
ST_BOOT = [0.175, 1.223, 4.791, 6.572, 7.994, 23.856, 26.162, 32.299, 34.914, 36.2, 40.004, 46.116]


def st_boot(i):
    """부츠 밑창 쿵 = 권투 장갑 샌드백 (boxing_glove) 1.2 kHz 아래 — 모든 바닥에 같은 결."""
    return st_nrm(st_lp(highpass(st_ev("boxing_glove", "glove", ST_BOOT[i % 12], 0.12, fout=0.084), 50), 1200))


ST_SC = [0.32, 0.52, 0.78, 1.12, 1.4, 1.62, 2.62, 2.85, 3.12, 3.52, 3.82, 4.2]


def st_scuff(k):
    """밑창 스침 = 판자를 콘크리트에 긁음 (board_scrape) 0.07초, 400 Hz ~ 3.5 kHz, 12 ms에 차올라 30 ms로 잦아듦."""
    seg = st_lp(highpass(st_ev("board_scrape", "scrape", ST_SC[k % 12], 0.07, pre_roll=0.0, fout=0.0), 400), 3500)
    n = np.arange(len(seg))
    return st_nrm(seg * np.minimum(1.0, n / (0.012 * SR)) * np.exp(-n / (0.03 * SR)))


# --- 바닥 처방 (k = 0 ~ 5 걷기, 6 ~ 11 달리기 — 같은 녹음의 다른 부딪힘) ---
# 나무: 뒤꿈치 = 판자로 판자 바닥을 침 (board_hits — 속 빈 나무 "탁", 150 Hz 아래 = 폰 녹음 전기 웅 줄 걸러 냄) + 나무 문 주먹 쿵 (door_pounds) −5 dB + 부츠 −8 dB,
#   앞꿈치 = 다른 판자 침 3 kHz 로우패스. 뺀 것: 문 쿵만 (300 Hz 아래 55 % · 1 ~ 6 kHz 1 % — "탁" 없이 노크 · Knock 0.75), 나무망치 (gavel — 0 dBFS에서 잘린 녹음), 영화 슬레이트 딱 (5 kHz 대 짝 — 3.5 kHz로 눌러도 묻힘).
ST_WB = [1.335, 4.035, 5.925, 0.025, 3.365, 9.805, 2.51, 5.54, 7.655, 8.385, 9.15, 10.425]
ST_WP = [0.235, 0.76, 2.49, 2.69, 4.39, 5.885, 6.34, 8.51, 8.965, 10.405, 12.435, 13.155]


def st_wood(run, k):
    hl = 0.11 if run else 0.15
    heel = st_mix(st_nrm(highpass(st_decay(st_ev("board_hits", "board", ST_WB[k], hl), 0.04 if run else 0.05), 150)),
                  st_nrm(st_decay(st_ev("door_pounds", "door", ST_WP[k], hl), 0.05)) * st_db(-5), st_boot(k) * st_db(-8))
    toe = st_nrm(st_lp(highpass(st_decay(st_ev("board_hits", "board", ST_WB[(k + 3) % 12], 0.1), 0.03), 150), 3000))
    return heel, toe


# 돌: 뒤꿈치 = 부츠 −2 dB + 벽돌이 콘크리트에 떨어지는 첫 "턱" (brick_drop — 18 ~ 22 ms로 빨리 끊음, 길게 두면 벽돌 울림이 "퐁") + 작은 돌 차기 알갱이 (small_stones) −12 dB,
#   앞꿈치 = 다른 벽돌 "턱" 4 kHz 로우패스. 뺀 것: 벽돌 울림을 50 ms 남긴 판 (AI Plop 0.85).
ST_SB = [0.235, 3.565, 5.605, 7.895, 9.64, 11.565, 13.41, 15.27, 15.44, 0.235, 5.605, 9.64]
ST_SG = [0.295, 0.615, 2.71, 4.98, 7.21, 7.5]


def st_stone(run, k):
    grit = st_nrm(st_lp(highpass(st_decay(st_ev("small_stones", "stones", ST_SG[k % 6], 0.06, pre_roll=0.004 - 0.01 * (k // 6)), 0.02), 800), 5000))
    heel = st_mix(st_boot(k) * st_db(-2), st_nrm(st_lp(st_decay(st_ev("brick_drop", "brick", ST_SB[k], 0.1), 0.018 if run else 0.022), 7000)), grit * st_db(-12))
    toe = st_nrm(st_lp(st_decay(st_ev("brick_drop", "brick", ST_SB[(k + 4) % 12], 0.07), 0.016), 4000))
    return heel, toe


# 유리: 뒤꿈치 = 부츠 + 유리 톡 (glass_taps — 1.52 kHz 울림을 솔6 1568 Hz로 +0.6반음, 첫 6 ms만 세게 + 울림 꼬리 −14 dB) −6 dB, 앞꿈치 = 다른 톡 −3 dB + 부츠 −6 dB.
#   뺀 것: 울림을 22 ~ 35 ms 남긴 판 (Music 0.59 · ring 4 ~ 6 — 찰랑), 샷잔 떨어뜨림 (여러 번 튐 · ring 4 ~ 12).
ST_GT = [0.95, 1.79, 2.6, 5.055, 7.925, 8.61, 9.295, 10.255, 10.57, 11.745, 0.275, 6.465]


def st_tick(t, tau):
    x = pitch(st_ev("glass_taps", "tap", t, 0.12, fout=0.04), 0.6)
    return st_nrm(st_mix(st_decay(x, 0.006), st_decay(x, tau) * st_db(-14)))


def st_glass(run, k):
    heel = st_mix(st_boot(k), st_tick(ST_GT[k], 0.018 if run else 0.022) * st_db(-6))
    toe = st_mix(st_tick(ST_GT[(k + 5) % 12], 0.015) * st_db(-3), st_boot(k + 3) * st_db(-6))
    return heel, toe


# 금속 격자: 뒤꿈치 = 큰 금속 들통 쿵 (metal_pail — 속 빈 금속) + 철망 우리 문 덜컹 (cage_door, 1 ~ 5 kHz 떨림 0.09초 — 조각마다 다른 자리) −1 dB + 부츠 −6 dB,
#   앞꿈치 = 다른 들통 쿵 2.5 kHz 로우패스 + 철망 −8 dB. 뺀 것: 들통 울림 9 ~ 10인 부딪힘 (3.235 · 8.98은 앞꿈치로만), 철망 −7 dB (떨림이 안 들림 — 1 ~ 6 kHz 3 %).
ST_GP = [0.01, 1.68, 6.275, 6.97, 7.885, 3.235, 5.415, 8.98, 0.01, 1.68, 6.275, 7.885]
ST_GR = [0.035, 0.05, 0.065, 0.08, 0.045, 0.06, 0.04, 0.055, 0.07, 0.085, 0.075, 0.09]


def st_grate(run, k):
    hl = 0.11 if run else 0.13
    mesh = load(f"{ST}cage_door/cage_00.010.wav")
    a = int(round((ST_GR[k] - 0.0) * SR))
    rat = st_nrm(fade(st_lp(highpass(mesh[a:a + int(round(0.09 * SR))], 1000), 5000), 0.001, 0.05))
    heel = st_mix(st_nrm(st_decay(st_ev("metal_pail", "pail", ST_GP[k], hl), 0.04 if run else 0.05)), rat * st_db(-1), st_boot(k) * st_db(-6))
    toe = st_mix(st_nrm(st_lp(st_decay(st_ev("metal_pail", "pail", ST_GP[(k + 5) % 12], 0.08), 0.03), 2500)), rat * st_db(-8))
    return heel, toe


# 잔디: 뒤꿈치 = 부츠 + 덤불 잎 사각 (bush_rustle — 1 kHz 위가 갑자기 커지는 순간 24개, 0.14초, 250 Hz ~ 4 kHz) −5 dB, 앞꿈치 = 다른 사각 −3 dB + 부츠 500 Hz 아래 −6 dB.
#   모래 해변 · 흙 · 화단도 이것 (부드러운 땅). 뺀 것: 4 kHz로 안 누른 판 (무게중심 3 ~ 10 kHz — 실제 잔디 녹음만큼 밝지만 어두운 음악 위에 쉬—), 사각 −2 dB 판 (무게중심 3.4 kHz), 풀밭에 넘어짐 (2번뿐).
ST_GB = [0.35, 0.955, 3.49, 5.315, 6.465, 7.415, 8.855, 10.43, 11.02, 11.37, 11.805, 12.255, 12.89, 14.975, 17.265, 19.07, 20.825, 23.59, 23.965, 28.565, 28.98, 29.49, 29.895, 30.355]
ST_GB_ORDER = [12, 5, 10, 3, 18, 13, 20, 16, 4, 9, 7, 23, 22, 15, 1, 14, 2, 21, 8, 17, 6, 11, 0, 19]   # 센 순서 (사각이 큰 것부터)


def st_crunch(t, length, tau):
    return st_nrm(st_lp(st_decay(highpass(st_ev("bush_rustle", "bush", t, length, pre_roll=0.002, fout=length * 0.5), 250), tau), 4000))


def st_grass(run, k):
    heel = st_mix(st_boot(k), st_crunch(ST_GB[ST_GB_ORDER[k]], 0.14, 0.045 if run else 0.06) * st_db(-5))
    toe = st_mix(st_crunch(ST_GB[ST_GB_ORDER[k + 12]], 0.12, 0.05) * st_db(-3), st_nrm(st_lp(st_boot(k + 4), 500)) * st_db(-6))
    return heel, toe


# 얕은 물: 뒤꿈치 = 부츠 + 물에 뛰어드는 첨벙의 첫 0.14초 (water_jump · water_splash_small — 200 Hz ~ 4 kHz, 30 ~ 40 ms로 잦아듦 = "철벅") −3 dB, 앞꿈치 = 다른 첨벙 −2 dB + 부츠 500 Hz 아래 −8 dB.
#   무게중심 1.7 ~ 2.1 kHz (실제 물웅덩이 1.2 ~ 2.2 kHz). 뺀 것: 걸레 통 물 (mop bucket — 무게중심 120 ~ 7000 Hz로 들쭉날쭉, 출렁임만), 부츠 −3 dB 판 (무게중심 3 kHz).
ST_AS = [("water_jump", t) for t in (2.165, 4.28, 5.965, 6.385, 8.45, 10.26, 12.23, 14.05, 15.81)] + [("water_splash_small", t) for t in (0.015, 0.385)]


def st_splash(i, tau):
    folder, t = ST_AS[i % len(ST_AS)]
    return st_nrm(st_lp(st_decay(highpass(st_ev(folder, "splash", t, 0.14, pre_roll=0.003, fout=0.06), 200), tau), 4000))


def st_water(run, k):
    heel = st_mix(st_boot(k), st_splash(k, 0.03 if run else 0.04) * st_db(-3))
    toe = st_mix(st_splash(k + 5, 0.025) * st_db(-2), st_nrm(st_lp(st_boot(k + 4), 500)) * st_db(-8))
    return heel, toe


def st_step(heel, toe, run, i):
    """뒤꿈치 → 앞꿈치를 한 걸음으로 (걷기 0.34초 · 달리기 0.26초), 60 Hz 아래 정리."""
    d, gt, L, fout = (ST_RUN_D[i], -3.0, 0.26, 0.05) if run else (ST_WALK_D[i], -7.0, 0.34, 0.07)
    y = np.zeros(int(L * SR))
    y[:min(len(heel), len(y))] += heel[:len(y)]
    a = int(d * SR)
    n = min(len(toe), len(y) - a)
    y[a:a + n] += toe[:n] * st_db(gt)
    return highpass(fade(y, 0.0005, fout), 60)


# 크기: 바닥마다 50 ms 최대 저장 크기를 달리해 귀 감도 (A 특성 0.25초 최대)를 금속 판 걷기 평균 (−27.3 dBFS)에 맞춤 (걷기 −27.3 ~ −27.5, 달리기는 앞꿈치가 겹쳐 +1 ~ +1.6)
# — 칸 음량은 모두 금속과 같은 0.42 (달리기 1 · 걷기 0.75배는 코드).
ST_DBS = dict(wood=-17.9, stone=-17.9, glass=-14.6, grate=-17.2, grass=-15.1, water=-15.7)
for st_name, st_fn in (("wood", st_wood), ("stone", st_stone), ("glass", st_glass), ("grate", st_grate), ("grass", st_grass), ("water", st_water)):
    for st_run in (False, True):
        for i in range(6):
            k = i + (6 if st_run else 0)
            heel, toe = st_fn(st_run, k)
            if st_name not in ("grass", "water"):   # 단단한 바닥 = 뒤꿈치 15 ms 뒤 밑창 스침 (걷기 −12 · 달리기 −9 dB)
                sc = st_scuff(k) * st_db(-9 if st_run else -12)
                heel = st_mix(heel, np.concatenate([np.zeros(int(0.015 * SR)), sc]))
            save(f"Interior/step_{st_name}_{'run' if st_run else 'walk'}_{i + 1:02d}.wav", st_step(heel, toe, st_run, i), ST_DBS[st_name])

# --- 금속 판 달리기 (걷기 step_metal_01 ~ 09는 그대로) — 같은 VARCO 부츠 원본 (Interior/step/)의 걸음 중 앞꿈치까지 짧은 여섯을 0.2초로 (꼬리 · 울림을 잘라 짧고 세게) ---
st_m = [("metal_steps_4.wav", t) for t in (2.11, 2.72, 3.28, 3.77, 4.33)] + [("metal_steps_5.wav", 1.65)]
for i, (f, t) in enumerate(st_m, 1):
    x = load("Interior/step/" + f)
    seg = trim_lead(sl(x, max(0.0, t - 0.03), t + 0.25), -30)[: int(0.2 * SR)]
    save(f"Interior/step_metal_run_{i:02d}.wav", fade(highpass(seg, 60), 0.002, 0.06), -17)

# ================= 바깥 정거장 험 3단계 (SOUND_LIST_NEW 4-4 ext_hum_1 ~ 3 · D-5 · D-12, 2026-11-25) =================
# 바깥 화면의 정거장 웅 (amb_station_loop — 실제 녹음 B: 건조기 드럼 + 큰 환기 팬, 600 Hz 아래) 위에 정거장 크기에 따라 겹치는 층 셋.
# GameAudio가 모듈 수 · 등급으로 두께 (0 ~ 3)를 정해 이웃 단계끼리 교차 (ExteriorHum — 1단계 = 작은 정거장 "가는 험" → 3단계 = 큰 정거장 "여러 겹 기계").
# 교차해도 결이 이어지게 단계마다 아래 단계 층을 그대로 품고 새 층을 더함: 1 = 팬 whirr · 큰 팬 웅 · 공기 / 2 = + 펌프 / 3 = + 깊은 웅 + 느린 기계 움직임.
# 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 녹음마다 AudioOriginals/Exterior/<녹음>/SOURCE.txt). 후보 12개 (mp3 17 MB로 훑고 고른 6개만 flac 22 MB, 조각 11.6 MB 보관).
# 음: 바깥 낮 D 장조 · 메뉴 C# 단조 · 밤 · 내부 D 단조 / F 장조 모두에 맞는 음은 E · A뿐 (B 구역과 같음) — 큰 팬 줄 (B2 123 Hz 배음)은 −200센트 옮겨 A · E로,
# 그 밖 줄 (안정기 120 Hz · 펌프 42.9 Hz 배음 = A3−43 · D5+39 · A5+41 · 화장실 팬 G#4−22 · 히터 F1+40 …)은 모두 깎고, 남긴 A · E 줄도 14 ~ 18 dB 눌러 "징"으로 안 들리게.
# 뺀 후보: 안정기 웅 (120 Hz 배음 줄 — 2 kHz 로우패스판 Hum 0.73 · Mains hum 0.63) · 방 공기 팬 · 작은 냉장고 (로우패스판 Gong 0.82 · 0.47) · 환기 휘파람 (바람 크기 폭 13 dB) ·
# 조용한 밤 공기 (−46 dB — 잡음 바닥) · 전자 실험실 (SF 삐 · 크기 폭 7.8 dB).
# 섞는 비율: 1단계를 팬 whirr 1.0 + 큰 팬 0.45 + 공기 0.6으로 하니 AI Music 0.14 · 2 kHz 로우패스판 Gong 0.74 (팬 whirr가 음처럼) → 팬을 줄이고 공기를 늘림 (팬만이면 Gong 0.91).
# AI (10초판): 1 = Vehicle 0.34 · Aircraft 0.23 (Music 0.08) / 2 = Vehicle 0.34 · Aircraft 0.25 / 3 = Vehicle 0.44 · Aircraft 0.41 · Aircraft engine 0.10,
# 세 단계 모두 Siren · Sine wave 0.00 · 분홍 잡음 0.00 · 흰 잡음 0.05 아래 · 1 kHz 아래 음 줄 없음 (A · E 줄은 +10 dB 아래로). 바탕 (amb_station_loop) = Vehicle 0.54.
# 크기: 파일은 같은 크기 (−24)로 두고 칸 음량으로 단계 크기를 정함 (바탕 혼자보다 바탕 + 단계가 귀 감도로 +2 · +4 · +6 dB — SoundLibrary ExtHum1 ~ 3).
EXT = "Exterior/"


def ext_chain(folder, pre, starts, seg, xf):
    """같은 녹음의 조각들 (시작초 · 길이 seg)을 rms를 맞춰 xf초씩 겹쳐 한 바퀴 루프로."""
    parts = [load(f"{EXT}{folder}/{pre}_{s:06.2f}.wav")[: int(round(seg * SR))] for s in starts]
    parts = [p / rms(p) * rms(parts[0]) for p in parts]
    return chain_loop(parts, xf)


def ext_fit(x, n):
    """한 바퀴 루프 x를 정확히 n 샘플 고리로 (이어 붙여 늘린 뒤 뒤 0.8초를 처음에 겹침) — 모든 층을 같은 길이로 (짧은 층을 반복해 붙이면 이음새에서 그 층만 끊김)."""
    k = int(0.8 * SR)
    y = np.tile(x, int(np.ceil((n + k) / len(x))) + 1)
    out = y[:n].copy()
    t = np.linspace(0, np.pi / 2, k)
    out[:k] = y[:k] * np.sin(t) + y[n:n + k] * np.cos(t)
    return out


def ext_band(x, lo=None, hi=None, order=2):
    """원형 FFT 띠 거르기 (버터워스 크기 모양) — 루프 전체를 한 번에 (이음새 그대로)."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    g = np.ones_like(f)
    if lo:
        g = g / np.sqrt(1 + (lo / np.maximum(f, 1e-3)) ** (2 * order))
    if hi:
        g = g / np.sqrt(1 + (f / hi) ** (2 * order))
    return np.fft.irfft(X * g, len(x))


nE = int(20.0 * SR)   # 세 단계 모두 20초 고리
rE = 2 ** (-200 / 1200)
# 화장실 팬 whirr: 120 · 240 · 360 Hz (안정기) · 410 Hz (G#4−22) · 820 Hz 깎고, E1 · E4 줄은 14 dB만 눌러 옅게, 150 ~ 1200 Hz (가는 결)
fanE = ext_band(notch_lines(ext_chain("bathroom_fan", "fan", [2.0, 12.0, 22.0], 8.0, 1.0), [120, 240, 360, 410, 820], 22, 2.5), 150, 1200)
fanE = ext_fit(notch_lines(fanE, [41.0, 325.2, 328.1], 14, 3.0), nE)
# 큰 팬 (B2 123 Hz 배음) → −200센트 = A2 110 Hz: 배음 A · E는 18 dB 눌러 남기고, 4.5배 · 9배 (C# → B) · 2.5배 (D#4 → C#4) · 44.7 Hz (F1+40) · 145.8 Hz (D3) · 1150 Hz 줄은 깎음, 80 ~ 1200 Hz
bigE = repitch_loop(ext_chain("large_fan", "bigfan", [3.0, 27.0], 11.0, 1.0), -200)
bigE = notch_lines(bigE, [553.7 * rE, 1107.4 * rE, 1168.9 * rE, 120 * rE, 120, 145.8, 1150.6, 44.7, 89.4, 273.9, 547.8], 22, 3.0)
bigE = ext_fit(ext_band(notch_lines(bigE, [109.9, 111.3, 219.0, 328.9, 438.7], 18, 3.0), 80, 1200), nE)
# 히터 공기 웅: 44.7 Hz (F1+40) 배음 · 120 Hz 줄 깎고 60 ~ 700 Hz
heatE = ext_band(notch_lines(ext_chain("heater", "heater", [5.0, 35.0], 11.5, 1.5), [44.7, 89.4, 134.1, 120, 240], 26, 3.0), 60, 700)
heatE = ext_fit(heatE, nE)
# 펌프 (AI Pump (liquid) 0.36): 42.9 Hz 배음 (A3−43 · E5−41 · D5+39 · A5+41 …) 40개와 471.7 Hz를 모두 깎아 음 없는 펌프 결만, 50 ~ 500 Hz
pumpE = ext_band(notch_lines(ext_chain("pump", "pump", [1.0, 9.0, 16.0], 7.5, 1.0), [42.9 * h for h in range(1, 41)] + [471.7], 22, 2.5), 50, 500)
pumpE = ext_fit(pumpE, nE)
# 트럭 실내 깊은 웅 (줄 없음, AI Vehicle · Rumble): 30 ~ 220 Hz만 — 큰 기관실 바닥 울림
deepE = ext_fit(ext_band(ext_chain("truck_interior", "truck", [5.0, 45.0], 11.5, 1.5), 30, 220), nE)
# 에스컬레이터 (6초 녹음 두 조각): 느린 기계 움직임, 465.8 · 931.6 Hz (A#) 깎고 80 ~ 900 Hz — 크면 기차 (Train · Clickety-clack)로 들려 옅게만
escE = ext_fit(ext_band(notch_lines(ext_chain("escalator", "escalator", [0.0, 2.8], 3.2, 0.5), [931.6, 465.8], 20, 3.0), 80, 900), nE)
uE = lambda v: v / rms(v)
humE = {
    1: uE(fanE) * 0.6 + uE(bigE) * 0.3 + uE(heatE) * 1.0,
    2: uE(fanE) * 0.5 + uE(bigE) * 0.35 + uE(heatE) * 1.0 + uE(pumpE) * 0.5,
    3: uE(fanE) * 0.45 + uE(bigE) * 0.35 + uE(heatE) * 0.9 + uE(pumpE) * 0.6 + uE(deepE) * 1.2 + uE(escE) * 0.35,
}
for kE, vE in humE.items():
    vE = notch_lines(vE, [931.6], 20, 3.0)   # 히터 · 펌프 고리에 남은 A#5 줄
    save(f"Ambient/ext_hum_{kE}_loop.wav", vE, -24.0)
    report.append(f"  바깥 정거장 험 {kE}단계 고리 {len(vE) / SR:.2f}초 · 이음새 {abs(vE[0] - vE[-1]):.4f} (이웃 샘플 차 {np.abs(np.diff(vE)).mean():.4f})")

# ================= 합성 · 이민선 · 위아래 이동 장치 · 유리 엘리베이터 · 방 드나들기 (SOUND_LIST_NEW 2번 · 4-1, 2026-11-25) =================
# 합성 (비행 고리 · 기계팔 연출) · 이민선 · 위아래 이동 장치 · 유리 엘리베이터는 무음, 내부 들어가기 · 나가기 · 장치 목적지 판은 창 열기 · 닫기를 빌려 썼음.
# 기계 · 물체 · 공기 = 실제 녹음 (archive.org "The Designer's Choice UCS Collection", CC0 — 녹음마다 AudioOriginals/Systems/<녹음>/SOURCE.txt),
# 음 있는 신호 (종 · 반짝임 · 패드) = 합성 F 장조 (UI 한 벌 · 체험 소리와 같은 유리 결 — ui_glass · flower_bell · twinkle · music_box).
# 후보 = mp3 33개 (17 MB)로 훑고 (2초 창 AI · 판 · 음 높이) 고른 14개만 flac (20 MB, 조각만 8.7 MB 보관). AI 점수는 게임에서 나는 꼴 (한 번 소리 = 2.5초마다 네 번 10초,
# 루프 = 이어 붙여 10초 — ai_rank.py와 같음). 0.5초 아래 소리 (버튼 · 도착 "툭")는 성겨 Silence로만 나와 수치 · 그림으로 봄.
# 용접 지글 (합성 진행 중)은 위 "실제 녹음 D" 구역의 d_weld (정비 공창 용접 고리)를 조각으로 다시 씀. 잎 (합성 완료 3) · 덤불 (꽃바람)은 "바탕 · 방 환경음" 구역의 trees · bush.
SYS = "Systems/"


def sx_piece(folder, pre, start, a, b):
    """Systems/<녹음>/<앞>_<조각 시작초>.wav에서 원본 시각 a ~ b초."""
    x = load(f"{SYS}{folder}/{pre}_{start:06.2f}.wav")
    i = int(round((a - start) * SR))
    return x[i:i + int(round((b - a) * SR))].copy()


sx_hz = lambda m: 440.0 * 2 ** ((m - 69) / 12)   # MIDI 번호 → Hz (F4 65 · C5 72 · F5 77 · A5 81 · C6 84 · F6 89 · A6 93 · C7 96)


def sx_env(n, pts):
    """(초, 배율) 점들을 이은 크기 곡선 (n 샘플)."""
    a, v = zip(*pts)
    return np.interp(np.arange(n) / SR, a, v)


def sx_pad(notes, dur, att, rel, det=2.0, seed=0, bright=0.15):
    """부드러운 패드 — 음마다 ±det센트 두 겹 사인 (느린 일렁임) + 2배음 옅게, att초에 차올라 rel초로 잦아듦."""
    t = np.arange(int(dur * SR)) / SR
    x = np.zeros_like(t)
    for k, m in enumerate(notes):
        f = sx_hz(m)
        for c in (-det, det):
            ph = 2 * np.pi * f * 2 ** (c / 1200) * t + seed + k
            x += np.sin(ph) + bright * np.sin(2 * ph)
    return x / (2 * len(notes)) * np.minimum(1, t / att) * np.exp(-np.maximum(0, t - att) / rel)


def sx_spark(notes, t0, step, dur, gain=0.3, seed=0, kind="twinkle"):
    """반짝임 여럿 (notes = MIDI, t0초부터 step초 간격) — 한 판으로 (kind = twinkle 반딧불 결 · pong 둥근 유리)."""
    y = np.zeros(int(round(dur * SR)))
    for k, m in enumerate(notes):
        tone = twinkle(sx_hz(m), 0.9, seed + k) if kind == "twinkle" else glass_pong(sx_hz(m), 1.1, seed + k)
        y = place(y, tone * gain, t0 + k * step)
    return y[: int(round(dur * SR))]


# --- 합성 시작 (바깥 2D · 화면 좌우 — 비행 고리가 규격 밑 0.5칸에서 1.4초에 솟아 앉고 (처음이 빠르고 끝이 느림), 0.84초부터 팔이 깨어나 1.2초쯤 일을 시작) ---
# 추진 "슈우" = 헤어드라이어가 켜지는 소리 (0.07 ~ 1.45초, Hair dryer 0.81 — 모터 삐 줄 729 · 803 · 961 Hz를 12 dB 깎고 5반음 낮춰 고리 크기로) → 1.25초 앉음 "철컥" =
# 자동 잠금 장치 (모터 "위잉" 뒤 1.315초 "철컥", Power windows 0.78) → 1.35초 공구 시동 = 로봇 서보 "위잉" (13.49초부터, Drill 0.71).
# AI: Tools 0.16 · Power tool 0.14 · Drill 0.12 · Wood 0.13 (엉뚱한 범주 없음). 서보를 한 옥타브 낮추면 Vehicle · Music, 5반음 낮추면 Wood 0.76 · Rub (나무 문지름)이라 원래 높이 + 5 kHz 로우패스.
# 뺀 후보: 장난감 임팩트 드릴 (Dental drill 0.75 — 치과 드릴로 들림) · 핸드 드라이어 (Vacuum cleaner) · 트레일러 걸쇠 (말소리가 섞임) · 차 트렁크 자동 닫힘 (끝에 Gong · Music).
sx_dry = notch_lines(sx_piece("hair_dryer", "dryer", 0.0, 0.07, 1.45), [729.5, 802.7, 960.9, 1916.0, 1933.6, 2903.3], 12, 4.0)
sx_dry = lp2d(pitch(sx_dry, -5), 3500)
sx_dry = sx_dry * sx_env(len(sx_dry), [(0, 0.0), (0.03, 0.8), (0.3, 1.0), (1.0, 0.6), (1.6, 0.25), (1.84, 0.0)])
sx_lock = tame(lp2d(sx_piece("auto_lock", "lock", 0.0, 0.9, 1.7), 5000), 4)          # 1.315초 "철컥" = 조각 0.415초
sx_srv = lp2d(sx_piece("servo_motors", "servo", 13.2, 13.42, 14.3), 5000)
sx_srv = sx_srv * sx_env(len(sx_srv), [(0, 0.0), (0.04, 1.0), (0.6, 0.8), (0.88, 0.0)])
sx_x = tput(2.8, [(sx_dry, 0.0, 1.0), (sx_lock, 1.25 - 0.415, 0.9), (sx_srv, 1.35, 0.6)])
save("Event/synth_start.wav", fade(match_tilt(sx_x)[0], 0.0, 0.25), -14)

# --- 합성 진행 중 (바깥 2D 루프 — 진행 중인 합성 중 카메라에 가장 가까운 것 하나, 거리 · 좌우 · 일시정지로 크기, 내부에선 끔) ---
# 11.5초 고리: 비행 고리 추진 웅 = 호버 공 팬 (Vehicle 0.33 — 134.8 Hz C#3 −48센트를 −52센트 옮겨 C3, 팬 삐 1362 · 2045 Hz 깎음, 2.2 kHz 로우패스, −8 dB) +
# 기계팔이 옮겨 가는 "위잉" = 로봇 서보 짧은 위잉 여섯 (0.27 ~ 0.42초 → 한 옥타브 낮춰 0.5 ~ 0.8초, 0.4 · 2.3 · 4.9 · 6.4 · 8.6 · 10.2초) +
# 멈춰 용접 = 정비 공창 용접 지글 (d_weld) 조각 셋 (1.3 · 5.6 · 9.1초, 1.2 ~ 1.6초). 합성은 60초 넘게 이어져 작고 고르게 (한 번 소리보다 −8 dB 저장).
# AI: Rub 0.15 · Wood 0.13 · Tools 0.11 · Power tool 0.08 · Drill 0.07, Music 0.06. 서보를 5 · 7반음만 낮추면 Rub 0.8 · Sanding (사포질), 용접 −7 dB면 공구 범주가 약함.
SXL = 11.5
sx_hv = notch_lines(sx_piece("hover_ball", "hover", 3.0, 3.0, 15.0), [1362.3, 2044.9, 4910.2, 5320.3, 7365.2], 18, 4.0)
sx_hv = loop_exact(lp2d(repitch_loop(sx_hv, -52), 2200), SXL)
sx_ld = np.zeros(int(SXL * SR))
for sx_k, ((sx_a, sx_b), sx_t) in enumerate(zip([(26.96, 27.37), (30.27, 30.64), (32.80, 33.22), (29.10, 29.42), (31.42, 31.69), (39.68, 39.91)],
                                               (0.4, 2.3, 4.9, 6.4, 8.6, 10.2))):
    sx_w = lp2d(pitch(fade(sx_piece("servo_motors", "servo", 26.5 if sx_a < 38 else 38.2, sx_a - 0.03, sx_b + 0.06), 0.02, 0.06), -12), 5000)
    sx_ld = place(sx_ld, sx_w / rms(sx_w) * (0.5 if sx_k % 2 else 0.4), sx_t)
sx_ld = sx_ld[: int(SXL * SR)] + np.concatenate([sx_ld[int(SXL * SR):], np.zeros(int(SXL * SR))])[: int(SXL * SR)]   # 고리 끝을 넘은 꼬리는 앞으로
sx_wd = np.zeros(int(SXL * SR))
for sx_a, sx_t, sx_n in ((0.0, 1.3, 1.5), (1.6, 5.6, 1.2), (2.7, 9.1, 1.6)):
    sx_w = d_weld[int(sx_a * SR): int((sx_a + sx_n) * SR)]
    sx_wd = place(sx_wd, sx_w * sx_env(len(sx_w), [(0, 0.0), (0.12, 1.0), (sx_n - 0.2, 1.0), (sx_n, 0.0)]), sx_t)
sx_wd = sx_wd[: int(SXL * SR)]
sx_x = sx_hv / rms(sx_hv) * 10 ** (-8 / 20) + sx_ld / rms(sx_ld) * 10 ** (-2 / 20) + sx_wd / rms(sx_wd) * 10 ** (-2 / 20)
save("Event/synth_loop.wav", circular(lambda y: lp2d(y, 6500), sx_x), -22)

# --- 합성 완료 1 · 2 · 3단계 (레시피 단계, 4단계 = 3 — 바깥 2D · 섬광 · 불꽃 터짐 → 고리가 위로 날아올라 1.1초에 사라지거나 제자리에서 흩어짐) ---
# 셋 다 "풀려나 날아오름" = 큰 휙 (Designed "Swoosh, Huge, Windy", Whoosh 0.59)의 가장 센 0.42초부터 + 단계가 오를수록 화려한 F 장조 합성 (아기자기 · 날 서지 않게).
# 1단계 (발달한 미래 기술) = 매끈한 전자: 유리 아르페지오 파5 · 라5 · 도6 · 파6 (60 ms 간격) + 사인 패드 F4 · C5 · F5 — AI Music 0.63 · Ding (짧은 징글 — 의도).
# 2단계 (여가 테마파크) = 밝음: 꽃 종 파5 ~ 도7 빠른 굴림 + 0.42초 끝 화음 파6 · 라6 · 도7 + 높은 반짝임 — AI Ding 0.45 · Clang 0.44 · Music 0.23. 종 5.4배음 (7.5 · 9.5 · 11.3 kHz 삐)은 깎음.
# 3단계 (진주 · 금 동화) = 유리 · 자연: 낮은 빛 종 도5 · 파5 · 라5 · 도6을 천천히 (90 ms) + 풍경 같은 둥근 유리 "퐁" 일곱 + 잎 사각임 (trees — 바탕 구역과 같은 녹음)이 일었다 잦아듦
# — AI Music 0.36 · Glockenspiel 0.23 · Tubular bells 0.20 · Bell 0.15.
sx_wh = sx_piece("whoosh_huge", "whoosh", 0.0, 0.0, 2.44)
sx_rel = lp2d(fade(sx_wh[int(0.42 * SR):], 0.03, 0.4), 6000)
sx_d1 = np.zeros(int(2.8 * SR))
for sx_k, sx_m in enumerate((77, 81, 84, 89)):
    sx_d1 = place(sx_d1, ui_glass(sx_hz(sx_m), 0.9, 0.22, seed=sx_k, bright=0.6) * (0.7 + 0.1 * sx_k), 0.02 + 0.06 * sx_k)
sx_d1 = sx_d1 + sx_pad((65, 72, 77), 2.8, 0.08, 0.7, bright=0.1) * 0.5
save("Event/synth_done_1.wav", fade(tput(2.8, [(sx_rel, 0.0, 0.7), (sx_d1, 0.0, 1.0)]), 0.0, 0.3), -14)
sx_d2 = np.zeros(int(2.8 * SR))
for sx_k, sx_m in enumerate((77, 81, 84, 89, 93, 96)):
    sx_d2 = place(sx_d2, flower_bell(sx_hz(sx_m), 1.4, sx_k) * 0.6, 0.02 + 0.045 * sx_k)
for sx_k, sx_m in enumerate((89, 93, 96)):
    sx_d2 = place(sx_d2, flower_bell(sx_hz(sx_m), 1.8, 7 + sx_k) * 0.55, 0.42 + 0.01 * sx_k)
sx_d2 = sx_d2[: int(2.8 * SR)] + sx_spark((96, 98, 101, 93, 100, 96), 0.3, 0.17, 2.8, 0.25, 3)
sx_x = lp2d(tput(2.8, [(sx_rel, 0.0, 0.6), (sx_d2, 0.0, 1.0)]), 7000)
save("Event/synth_done_2.wav", fade(notch_lines(sx_x, [sx_hz(m) * 5.4 for m in (89, 93, 96)], 24, 6.0), 0.0, 0.3), -14)
sx_d3 = np.zeros(int(3.0 * SR))
for sx_k, sx_m in enumerate((72, 77, 81, 84)):
    sx_d3 = place(sx_d3, glow_bell(sx_hz(sx_m), 2.4, sx_k) * 0.6, 0.02 + 0.09 * sx_k)
sx_d3 = sx_d3[: int(3.0 * SR)] + sx_spark((89, 86, 93, 91, 96, 89, 93), 0.45, 0.24, 3.0, 0.3, 5, kind="pong")
sx_leaf = trees([0.75, 13.25], 2.6, 0.4)[: int(2.6 * SR)]
sx_leaf = sx_leaf * sx_env(len(sx_leaf), [(0, 0.0), (0.35, 1.0), (1.2, 0.8), (2.6, 0.0)])
save("Event/synth_done_3.wav", fade(tput(3.0, [(sx_rel, 0.0, 0.5), (sx_d3, 0.0, 1.0), (sx_leaf, 0.1, 0.35)]), 0.0, 0.3), -14)

# --- 이민선 도착 (바깥 2D · 여객 도킹 포트 쪽 — 이민 시계가 배를 부를 때, 내부에선 그 포트 방에 있을 때만) ---
# 큰 배가 다가옴 = 화물기 지나감 녹음의 앞 5.2초 (Vehicle · Aircraft · Propeller)를 5반음 낮춰 크게 · 1.9 kHz 로우패스로 멀리, 3.3초에 가장 크고 4.6초에 엔진이 멎음
# (120 Hz 아래 −12 dB 셸프 — 낮은 웅만 커서 귀 감도로 안 들렸음) → 3.95초 도킹 집게 = 자동 잠금 "위잉 · 철컥" + 4.0초 산업용 냉동고 문 "쿵" (먹먹하게)
# → 4.55초 에어록 = 공중전화 부스 유압 "치익" → 1.32초 뒤 닫힘. AI: Vehicle 0.43 · Aircraft 0.12 · Music 0.24 · Speech 0.08.
# 뺀 후보: 늦은 귀 감도 판 (낮은 쪽 −7 dB — Vehicle 0.52 · Car · Rumble이나 귀 감도 5.4로 음량이 1을 넘어야 함) · 먼 제트기 (Explosion · Artillery) · 차 접근 (Car).
sx_pl = low_shelf(lp2d(pitch(sx_piece("cargo_plane", "plane", 0.0, 0.0, 5.2), -5), 1900), -12, 120, 320)[: int(4.6 * SR)]
sx_pl = sx_pl * sx_env(len(sx_pl), [(0, 0.0), (1.5, 0.35), (3.3, 1.0), (3.9, 0.7), (4.6, 0.0)])
sx_clamp = tame(lp2d(sx_piece("auto_lock", "lock", 0.0, 0.9, 1.9), 4000), 3)
sx_thud = tame(lp2d(sx_piece("freezer_door", "freezer", 2.9, 3.3, 3.89), 2500), 3)   # 3.385초 "쿵" = 조각 0.085초
sx_air = lp2d(sx_piece("phone_booth", "booth", 0.0, 0.05, 1.94), 6000)
sx_x = tput(6.6, [(sx_pl, 0.0, 1.0), (sx_clamp, 3.95 - 0.415, 0.7), (sx_thud, 4.0 - 0.085, 0.6), (sx_air, 4.55, 0.5)])
save("Event/ship_immigrant_arrive.wav", fade(sx_x, 0.0, 0.3), -9)

# --- 위아래 이동 장치 켜짐 (목적지 판이 열릴 때 2D — 창 열기 0.5 대신) ---
# 장치가 깨어남 = 낮은 웅 F2 · F3 · C4 (0.18초에 차오름) + 짧은 서보 "윙" (39.68초 위잉을 한 옥타브 낮춤) + 유리 두 음 도6 → 파6. AI Ding 0.47 · Clang 0.40 · Music 0.36.
sx_ohum = sx_pad((41, 53, 60), 0.9, 0.18, 0.25, det=3.0, bright=0.3)
sx_osrv = lp2d(pitch(fade(sx_piece("servo_motors", "servo", 38.2, 39.65, 39.95), 0.01, 0.05), -12), 4000)
sx_ogl = place(ui_glass(sx_hz(84), 0.5, 0.12, 1, 0.6), ui_glass(sx_hz(89), 0.6, 0.15, 2, 0.6), 0.09)
save("Interior/transit_open.wav", fade(tput(0.9, [(sx_ohum, 0.0, 0.6), (sx_osrv, 0.0, 0.35), (sx_ogl, 0.05, 0.45)]), 0.0, 0.15), -14)

# --- 위아래 이동 장치 타기 0 ~ 4 (떠나는 장치의 단계 장치 — 2D, 떠오르기 · 가라앉기 1.3초 (점점 빨라짐 · 끝에 어두워짐) → 고른 방 → 도착 1.1초 (빠르게 와서 느려짐)) ---
# 2.7초: 1.25초까지 차오르고 어두운 동안 버티다 도착하며 잦아듦 (tr_up). 음 있는 층은 아주 옅게 — 조금만 커도 AI가 바로 Music (0.5 ~ 0.8)으로 들음 (움직임 소리가 먼저).
TRD = 2.7
tr_up = lambda n: sx_env(n, [(0, 0.0), (0.15, 0.45), (1.25, 1.0), (1.6, 0.9), (2.3, 0.4), (TRD, 0.0)])
# 0 = 기본 화물 승강기 칸 (작업등 · 증기): 차고 문 체인 모터 (1.5 ~ 4.5초, 120 Hz 전기 웅 = A#2 +52센트 → −52센트 Bb2) + 출발 · 도착 유압 "치익" — AI Vehicle 0.74 · Car · Truck · Air brake 0.10.
sx_r0 = lp2d(repitch_loop(sx_piece("garage_door", "garage", 0.5, 1.5, 4.5), -52)[: int(TRD * SR)], 3000) * tr_up(int(TRD * SR))
sx_r0s = lp2d(fade(sx_piece("phone_booth", "booth", 0.0, 0.1, 1.1), 0.02, 0.3), 5000)
save("Interior/transit_ride_0.wav", fade(tput(TRD, [(sx_r0, 0.0, 1.0), (sx_r0s, 0.0, 0.45), (sx_r0s, 2.0, 0.3)]), 0.0, 0.2), -14)
# 1 = 흰 세라믹 반중력 원판 (자기장 고리): 낮은 휙 바람결 (Designed "Slow, Low Whoosh") + 매끈한 웅 파3 · 도4 · 파4 아주 옅게 (음은 그대로, 밝기만 오름) — AI Vehicle 0.10 · Echo · Music 0.04.
sx_r1 = lp2d(sx_piece("whoosh_low", "whoosh", 0.0, 0.0, TRD), 3000) * tr_up(int(TRD * SR))
sx_r1h = sx_pad((53, 60, 65), TRD, 1.2, 0.8, det=4.0, bright=0.45) * sx_env(int(TRD * SR), [(0, 0.2), (1.3, 1.0), (TRD, 0.6)])
save("Interior/transit_ride_1.wav", fade(tput(TRD, [(sx_r1, 0.0, 1.0), (lp2d(sx_r1h, 1500), 0.0, 0.08)]), 0.0, 0.2), -14)
# 2 = 혼천의 (놀이기구): 도는 휙 (큰 휙을 초당 3번 일렁이게) + 오르골 도6 (출발) · 파6 (도착) 한 음씩 — AI Music 0.49 · Whoosh 0.10 (오르골 여덟 음 = Music 0.72 · Ice cream truck → 둘로 줄임).
sx_r2 = lp2d(np.pad(sx_wh, (0, max(0, int(TRD * SR) - len(sx_wh))))[: int(TRD * SR)], 5000)
sx_r2 = sx_r2 * (0.65 + 0.35 * np.sin(2 * np.pi * 3.0 * np.arange(len(sx_r2)) / SR)) * tr_up(len(sx_r2))
sx_r2m = place(np.zeros(int(TRD * SR)), music_box(sx_hz(84), 1.2, 0) * 0.5, 0.15)
sx_r2m = place(sx_r2m, music_box(sx_hz(89), 1.0, 9) * 0.4, 1.8)[: int(TRD * SR)]
save("Interior/transit_ride_2.wav", fade(tput(TRD, [(sx_r2, 0.0, 1.0), (sx_r2m, 0.0, 0.1)]), 0.0, 0.2), -14)
# 3 = 수정 꽃 연꽃 (꽃바람): 덤불 바람결 (bush) + 큰 휙 돌풍 (2반음 낮춤) + 유리 반짝임 셋 아주 옅게, 루프와 같은 고음 상한 (잎이 밝아 6 kHz 위 51 %였음)
# — AI Ocean 0.27 · Boat · Waves · Wind 0.20 · Wind noise 0.19 (잎 · 바람을 AI가 바다로 들음 — 바탕 구역과 같음), Music 0.14. 뺀 것: 둥근 유리 "퐁" (꺾이는 음이 Animal · Goat).
sx_r3 = lp2d(bush([0.75, 8.5], TRD + 0.5, 0.5)[: int(TRD * SR)], 4500) * tr_up(int(TRD * SR))
sx_r3w = lp2d(pitch(sx_wh, -2), 3000)[: int(TRD * SR)]
sx_r3w = np.pad(sx_r3w, (0, int(TRD * SR) - len(sx_r3w))) * tr_up(int(TRD * SR))
sx_x = tput(TRD, [(sx_r3, 0.0, 1.0), (sx_r3w, 0.0, 0.7), (sx_spark((84, 89, 93), 0.3, 0.6, TRD, 0.3, 11), 0.0, 0.08)])
save("Interior/transit_ride_3.wav", fade(match_tilt(sx_x)[0], 0.0, 0.2), -14)
# 4 = 성운 은 원판 (성운): 큰 휙을 7반음 낮춰 먹먹하게 + 반짝이는 패드 파4 · 라4 · 도5 · 솔5 (±6센트 일렁임) + 별 셋 — AI Whoosh 0.46 · Music 0.41.
sx_r4 = lp2d(pitch(sx_wh, -7), 2000)
sx_r4 = np.pad(sx_r4, (0, max(0, int(TRD * SR) - len(sx_r4))))[: int(TRD * SR)] * tr_up(int(TRD * SR))
sx_r4p = lp2d(sx_pad((65, 69, 72, 79), TRD, 0.9, 1.2, det=6.0, bright=0.1), 2500)
save("Interior/transit_ride_4.wav", fade(tput(TRD, [(sx_r4, 0.0, 1.0), (sx_r4p, 0.0, 0.07), (sx_spark((89, 96, 93), 0.5, 0.5, TRD, 0.2, 13), 0.0, 0.05)]), 0.0, 0.2), -14)

# --- 위아래 이동 장치 도착 (2D — 발판에 내려앉아 연출이 끝날 때, 연출을 끈 설정에서도) ---
# 냉동고 문 "쿵"을 1.5 kHz 로우패스로 아주 먹먹한 "툭" + 짧은 유압 숨 — 0.7초 (300 Hz 아래 89 %, crest 11.5).
sx_at1 = tame(lp2d(sx_piece("freezer_door", "freezer", 2.9, 3.3, 3.75), 1500), 4)
sx_at2 = lp2d(fade(sx_piece("phone_booth", "booth", 0.0, 0.6, 1.0), 0.01, 0.25), 4000)
save("Interior/transit_arrive.wav", fade(tput(0.7, [(sx_at1, 0.0, 1.0), (sx_at2, 0.05, 0.35)]), 0.0, 0.15), -14)

# --- 유리 엘리베이터 (진화 방 10곳 — 1단계 = 깨끗한 자기 부상, 2단계 이상 = 밝은 종 · 유리 울림, 모두 3D) ---
# 버튼 = 사각 버튼 딸깍 (20.225초 — 43번 중 몸통 있는 것: 무게중심 877 Hz · 300 Hz 아래 64 % · crest 18) + 1단계 = 유리 "삑" 도6 / 2단계 = 라5 → 도6 밝은 두 음.
sx_lbc = lp2d(fade(sx_piece("button_press", "button", 20.0, 20.2, 20.5), 0.002, 0.1), 6000)
save("Interior/lift_button_1.wav", fade(tput(0.4, [(sx_lbc, 0.0, 1.0), (ui_glass(sx_hz(84), 0.25, 0.06, 0, 0.3), 0.03, 0.6)]), 0.0, 0.08), -14)
sx_lb2 = place(ui_glass(sx_hz(81), 0.35, 0.09, 1, 1.0), ui_glass(sx_hz(84), 0.4, 0.11, 2, 1.0), 0.05)
save("Interior/lift_button_2.wav", fade(tput(0.5, [(sx_lbc, 0.0, 1.0), (sx_lb2, 0.03, 0.7)]), 0.0, 0.1), -14)
# 움직이는 동안 (캡슐에 붙은 3D 루프, 5초) = 러닝머신 모터가 고르게 도는 구간 (10 ~ 22초, Vehicle 0.65 — 줄 140.6 · 237.3 Hz 등 14 dB · 15.3 ~ 15.7 kHz 전자 삐 깎음, 2.5 kHz 로우패스)
# + 자기 부상 웅 파3 · 도4 (−22 dB, 5초에 정수 번 돌아 이음새 없음), 2단계 = 라4 더하고 유리 반짝임 파6 · 도6 · 라6 · 레6 (−18 dB).
# AI: 1 = Vehicle 0.62 · Car · Engine (Music 0.00), 2 = Vehicle 0.30 · Music 0.18 · Train.
# 뺀 후보: 진짜 엘리베이터 운행 구간 (모터 음 Bb2 · F4 · Bb4 · F5 + 낮은 쿵 박자 — 줄을 깎고 거르고 걸러도 Music 0.34 ~ 0.63 · Timpani) · 에스컬레이터 (Train 0.7) ·
# 욕실 환풍기 (Microwave oven 0.5) · 안마기 모터 (Music · Sewing machine).
LFL = 5.0
sx_lf = loop_exact(lp2d(notch_lines(sx_piece("treadmill", "tread", 10.0, 10.0, 22.0), [140.6, 237.3, 383.8, 281.2, 474.6], 14, 3.0), 2500), LFL)
sx_lf = notch_lines(sx_lf, [15457.0, 15650.0], 30, 20.0)
sx_t = np.arange(int(LFL * SR)) / SR
sx_h1 = (np.sin(2 * np.pi * 174.6 * sx_t) + 0.5 * np.sin(2 * np.pi * 261.6 * sx_t)) * (0.85 + 0.15 * np.sin(2 * np.pi * 0.4 * sx_t))
sx_h2 = sx_h1 + 0.35 * np.sin(2 * np.pi * 440.0 * sx_t) * (0.7 + 0.3 * np.sin(2 * np.pi * 0.2 * sx_t + 1))
sx_g = np.zeros(int(LFL * SR))
for sx_a, sx_m in ((0.3, 89), (1.6, 84), (2.9, 93), (4.1, 86)):
    sx_g = place(sx_g, twinkle(sx_hz(sx_m), 0.9, int(sx_a * 10)) * 0.5, sx_a)
sx_g = sx_g[: int(LFL * SR)] + np.concatenate([sx_g[int(LFL * SR):], np.zeros(int(LFL * SR))])[: int(LFL * SR)]
save("Interior/lift_move_1_loop.wav", sx_lf / rms(sx_lf) + sx_h1 / rms(sx_h1) * 10 ** (-22 / 20), -22)
save("Interior/lift_move_2_loop.wav", sx_lf / rms(sx_lf) + sx_h2 / rms(sx_h2) * 10 ** (-22 / 20) + sx_g / rms(sx_g) * 10 ** (-18 / 20), -22)
# 도착 (캡슐 자리 3D — 서자마자 그 층 문이 0.63초에 열림) = 1단계 꽃 종 "띵" 도6 / 2단계 "띵 — 동" 라6 → 파6 + 반짝 둘 (종 5.4배음 깎음)
# + 0.15초 자판기 미닫이 문 "스르륵" (0.12 ~ 1.0초, 3 kHz 로우패스). AI: 1 = Ding 0.30 · Clang · Whoosh 0.08, 2 = Music 0.37 · Ding 0.16 (두 음 = 초인종 결).
sx_lad = lp2d(fade(sx_piece("vending_door", "door", 0.0, 0.12, 1.0), 0.03, 0.3), 3000)
save("Interior/lift_arrive_1.wav", fade(tput(1.5, [(flower_bell(sx_hz(84), 1.4, 0), 0.0, 1.0), (sx_lad, 0.15, 0.4)]), 0.0, 0.3), -14)
sx_la2 = place(flower_bell(sx_hz(93), 1.2, 1), flower_bell(sx_hz(89), 1.5, 2), 0.2)
sx_x = lp2d(tput(1.7, [(sx_la2, 0.0, 1.0), (sx_spark((96, 89), 0.25, 0.12, 1.7, 1.0, 21), 0.0, 0.25), (sx_lad, 0.15, 0.4)]), 6000)
save("Interior/lift_arrive_2.wav", fade(notch_lines(sx_x, [sx_hz(m) * 5.4 for m in (89, 93)], 24, 6.0), 0.0, 0.3), -14)

# --- 방 드나들기 (2D — 들어가기 = 페이드 0.25 + 화면 0.25초 → 짓기 → 최소 0.8초 → "해치 여는 중" / 나가기 = 페이드 0.15초 → 바깥 0.25초, 창 열기 · 닫기 대신) ---
# 들어가기 "우웅—" = 큰 휙 전체 (0.7초에 차오름)를 3반음 낮춰 4 kHz 로우패스 + 0.7초 해치 유압 "치익" (공중전화 부스) + 아주 옅은 웅 F2 · C3 · F3 — AI Whoosh 0.30 · Music 0.22.
# 나가기 "우웅↘" = 큰 휙의 가장 센 곳부터 잦아듦을 5반음 낮춤 + 1.0초 해치 닫힘 "쿵" 옅게 — AI Whoosh 0.53 · Sound effect 0.16.
sx_iw = lp2d(pitch(sx_wh, -3), 4000)
sx_ih = sx_pad((41, 48, 53), len(sx_iw) / SR, 0.6, 0.8, det=3.0, bright=0.2)
sx_ia = lp2d(fade(sx_piece("phone_booth", "booth", 0.0, 0.1, 1.25), 0.05, 0.4), 5000)
save("Interior/interior_enter.wav", fade(tput(2.6, [(sx_iw, 0.0, 1.0), (sx_ih, 0.0, 0.06), (sx_ia, 0.7, 0.3)]), 0.0, 0.3), -14)
sx_xw = lp2d(pitch(fade(sx_wh[int(0.45 * SR):], 0.03, 0.3), -5), 3000)
sx_xt = lp2d(fade(sx_piece("phone_booth", "booth", 0.0, 1.25, 1.7), 0.01, 0.2), 2500)
save("Interior/interior_exit.wav", fade(tput(2.0, [(sx_xw, 0.0, 1.0), (sx_xt, 1.0, 0.25)]), 0.0, 0.3), -14)

# ================= 실제 녹음 ⑥ — 방 환경음 1단계 판 · 가끔 소리 꾸러미 · 상태 층 (SOUND_LIST_NEW 4-4 · D-5, 2026-11-25) =================
# 원본 = archive.org "The Designer's Choice UCS Collection" (CC0) 조각 — AudioOriginals/RoomAmb/<녹음>/ (SOURCE.txt). 후보는 mp3로 훑고 고른 것만 flac.
RA = "RoomAmb/"


def rpc(folder, pre, start, length=None):
    x = load(f"{RA}{folder}/{pre}_{start:06.2f}.wav")
    return x if length is None else x[: int(round(length * SR))]


def rrc(folder, pre, starts, seg, xf):
    """RoomAmb 조각들을 rms를 맞춰 xf초씩 겹쳐 한 바퀴 루프로 (길이 = 조각 수 × (seg − xf))."""
    parts = [rpc(folder, pre, s, seg) for s in starts]
    parts = [p / rms(p) * rms(parts[0]) for p in parts]
    return chain_loop(parts, xf)


# --- 1단계 방 환경음 6 (amb_{성격}_1_loop, 2D 늘 깔림 — 1단계 방 · 소리 풍경이 없는 2단계 방) ---
# 기본 판 (amb_room_*, 10번 A)과 같은 성격을 "발달한 미래 기술" 쪽으로: 날개 · 모터 줄 (윙)을 더 깎고 고음을 더 눌러 매끈하게, 게임 안 크기는 기본 판보다 1.5 dB 작게 (음량으로).
# 음 봉우리는 모두 음악 조 (D 단조 · F 장조) 안 또는 흐릿하게 — 조 밖 줄은 notch_lines, 녹음째 옮길 땐 repitch_loop.
# 생활 1단계 (중력 거주구 · 거주 타워 · 문화 · 복지 · 보안청) = 욕실 환풍기 whirr (bath_fan — 0.5초 크기 폭 1.2 dB)를 900 Hz 아래 = 벽 너머 조용한 환기.
# 날개 · 전기 줄 (41 · 82 · 120 · 240 · 248 (B3) · 360 · 410 (G#4) · 496 · 820 Hz)과 남는 웅 줄 (131 · 164 · 328 · 656 Hz)을 깎음, 1.2 kHz 아래. 생활 기본 판 (방 공기 + 먼 환풍기 윙)과 달리 윙 없이 공기만.
# 첫 판 (900 Hz 아래, 웅 줄 남김)은 게임 안 바탕만 AI Music 0.19 · Scary music 0.09 (모의 섞음 Timpani 0.09) → 줄을 더 깎아 Music 0.04 · Vehicle 0.34 (기본 판 Microwave · Vehicle과 같은 꼴).
# 뺀 시안: 방 공기 (room_tone)를 1.2 kHz 아래로 (Vehicle 0.37 · Microwave 0.21 — 기본 판과 같은 녹음이라 구분 안 됨) · 안정기 웅 −14 · −18 dB를 얹음 (Music 0.06 — 차이 없음, 웅은 전기 방 몫) ·
# 환풍기 + 방 공기 −4 · −8 dB (섞으면 Ocean 0.05 ~ 0.06 · White noise 0.09).
fan1 = notch_lines(rrc("bath_fan", "fan", [3.0, 12.0, 24.0], 6.0, 1.5), [41, 82, 120, 240, 248, 360, 410, 496, 820], 30, 3.0)
fan1 = lpc(hpc(notch_lines(fan1, [131, 164, 328, 656], 24, 3.0), 60), 1200)
save("Interior/amb_life_1_loop.wav", fan1, -24)

# 물 1단계 (수직 농장 · 생태 · 수소) = 쇼핑몰 실내 분수 (fountain, Stream 0.46 · Waterfall 0.39) 1.2 kHz 아래 + 수영장 졸졸 (Ambience/pool_trickle, 3.5 kHz 아래) −6 dB = 수경 재배 물길, 10초.
# 펌프 웅 (물 기본 판)은 뺌. 첫 판 (분수만 2.5 kHz 아래)은 혼자 Stream 0.30이지만 게임 안 음악과 섞으면 Waves, surf 0.58 · Ocean 0.55 (분수 물보라 쉬— 가 파도로 들림, A 겪은 일 ⑦과 같음)
# → 분수를 1.2 kHz 아래로 누르고 졸졸을 얹어 모의 섞음 Ocean 0.08 · Stream 0.16 (파일 Stream 0.45 · Pour 0.32 · Rain 0.13).
# 뺀 시안: 분수 1.0 ~ 1.5 kHz만 (Stream 0.09 ~ 0.15 — 물이 흐려짐) · 분수 + 졸졸 −4 dB (2.5 kHz — Rain 0.28 · Raindrop) · 다른 분수 (사람 말소리 Speech 0.47).
foun1 = notch_lines(rrc("fountain", "foun", [0.2, 2.6, 4.9, 1.4], 3.4, 0.9), [59, 77, 118, 154], 20, 2.0)
tr1 = chain_loop([load(f"Ambience/pool_trickle/trickle_{s:06.2f}.wav")[: int(3.4 * SR)] for s in (4.0, 21.5, 27.25, 8.0)], 0.9)
tr1 = hpc(notch_lines(tr1, [60, 120, 180, 240], 18, 1.5), 60)
save("Interior/amb_water_1_loop.wav", layer(lpc(hpc(foun1, 60), 1200), lpc(tr1, 3500), -6), -24)

# 기계 1단계 (물류 허브 · 연구 단지 · 조선소 · 대형 창고 · 정비 공창) = 컴퓨터 본체 팬 (pc_fan) = 조용한 서버 · 자동화 웅, 15.2초.
# 조 밖 줄 (53 · 59 · 106 · 118 Hz 전기 웅, 134 · 141 · 157 · 747 (F#5) · 1081 ~ 1091 (C#6) · 1494 Hz 팬 줄)을 깎고 느린 크기 흔들림 (0.1초 5 ~ 6 dB)을 반만 폄.
# 뺀 시안: 팬 + 에스컬레이터 −6 dB (Train 0.32 · Speech 0.13).
pc1 = notch_lines(rrc("pc_fan", "pc", [8.0, 16.0, 24.0, 32.0], 5.0, 1.2), [53, 59, 106, 118, 134, 141, 149, 157, 168, 177, 236, 298, 747, 1081, 1086, 1091, 1494], 24, 2.5)
save("Interior/amb_machine_1_loop.wav", half_flat(hpc(pc1, 50)), -24)

# 고열 1단계 (융합 발전소 · 광물 처리 · 자재 재생) = 에어프라이어 열풍 팬 (fryer_fan — 0.5초 크기 폭 0.8 dB, 34 ~ 57초 삑 없는 곳) = 따뜻하고 매끈한 송풍, 15.6초.
# 줄 120 · 480 · 840 Hz (B2−48 · Bb4+50 · G#5+20)를 −50센트 옮겨 Bb2 · Bb4 · G5−30 (조 안), 남는 193 · 206 · 412 · 724 · 1448 Hz 줄은 깎음, 2 kHz 아래 (기본 판 열풍기보다 고르고 윙 없음).
fry1 = notch_lines(repitch_loop(rrc("fryer_fan", "fryer", [40.0, 46.5, 52.0, 34.0], 5.0, 1.2), -50), [193, 206, 412, 724, 1448], 24, 2.5)
save("Interior/amb_heat_1_loop.wav", lpc(hpc(fry1, 50), 2000), -24)

# 전기 1단계 (축전 뱅크 · 실드 매트릭스 · 방어 포대 · 태양광 · 자동화 · 비상 대응 · 장갑) = 작은 냉장고 압축기 웅 (Ambience/mini_fridge, 줄 E3−23 → +119센트 F3 · F4 · C5)
# 1.5 kHz 아래 + 기본 판과 같은 안정기 웅 (Bb2, 3 kHz 아래) −14 dB = 지잉 없는 매끈한 전기 웅, 10.3초. 두 층을 따로 옮겨 길이를 맞춤 (웅 조각 겹침 2.01초 → 옮긴 뒤 같은 길이).
# AI: Speech 0.06 · Music 0.09 · Gong 0.02 · 위 Vehicle 0.33 · Boat (기본 판 Vehicle 0.40 · Hum 0.33 · Mains hum 0.32).
# 뺀 시안: 안정기 웅을 600 ~ 4000 Hz 아래로 (낮게 거를수록 모음처럼 들려 Speech 0.11 ~ 0.30, 600 Hz = Gong 0.16 · Singing bowl) · 웅 + 욕실 환풍기 공기 (Speech 0.18) ·
# 냉장고 + 웅 −6 · −10 dB (Speech 0.22 · 0.18) · 냉장고만 (Music 0.16 · Ambient music).
fz1 = repitch_loop(hpc(notch_lines(rchain("mini_fridge", "fridge", [20.5, 30.75], 7.0, 1.5), [60, 120, 180, 240], 18, 1.5), 60), 119)
hz1 = notch_lines(repitch_loop(hpc(rchain("ballast_hum", "hum", [6.0, 13.0], 7.0, 2.01), 40), -51), BB_OFF, 24, 2.0)
hz1 = np.interp(np.arange(len(fz1)) * (len(hz1) / len(fz1)), np.arange(len(hz1) + 1), np.concatenate([hz1, hz1[:1]]))   # 10.278 → 10.269초 (0.09 % — 높이 차 1.5센트)
save("Interior/amb_electric_1_loop.wav", layer(lpc(fz1, 1500), lpc(hz1, 3000), -14), -24)

# 넓은 공간 1단계 (지금은 2단계 궤도 미러뿐 — 1단계 방에는 넓은 공간 성격이 없음) = 조용한 밤 바깥 공기의 다른 조각 (기본 판과 같은 녹음 · 다른 자리) 낮은 웅 −6 dB.
# 149 · 164 Hz 줄 (D3+30 · E3−8)까지 깎음. 뺀 시안: 같은 녹음 10.5 · 150 · 200초 조각 (212 Hz G#3 줄 · Gong 0.15).
q1 = notch_lines(rrc("night_air", "air", [40.0, 95.0, 250.0], 6.0, 1.5), [60, 72, 120, 126, 149, 164], 20, 2.0)
save("Interior/amb_hall_1_loop.wav", low_shelf(hpc(q1, 60), -6), -26)

# --- 가끔 소리 꾸러미 (Interior/Spot/spot_{성격}_{단계}_NN — 닫힌 방 바탕 12마다, D-5 ②) ---
# 꾸러미 = 무리 셋 × 소리 둘 (RoomSoundscape SS_Spot_*의 Scatters: 무리마다 25 ~ 70초, 꾸러미 전체로 8 ~ 30초에 하나 꼴 · 방 아무 칸 3D). 1단계 꾸러미는 기본 판 무리 하나를 같이 씀.
# 실제 녹음 (물건 · 물 · 기계 · 불 · 전기)은 녹음마다 50 ms 크기 · 바닥 · 울림 (ring)을 재서 깨끗한 순간만 (후보 735 순간), 날 선 딸깍은 tame · soften으로 둥글게,
# 넓은 공간은 distant (짧은 잔향 + 3 kHz 위 누름)로 먼 소리. 음 있는 신호 (1단계 알림 · 데이터 · 충전 · 먼 종 · 퐁, 물방울)는 F 장조 합성 또는 녹음 높이를 재서 음계로 (Tonal — 다음 박).
# 파일 크기 = 귀 감도 (A 0.25초 최대) −20 dBFS로 맞춤 (낮은 "툭"이 rms로 맞추면 8 ~ 9 dB 작게 들리는 것을 막음 — 겪은 일 11-23) · 큐 음량은 게임 안 녹음으로.
SP_ = "Interior/Spot/"


def a_weight(x):
    X = np.fft.rfft(x)
    f2 = np.fft.rfftfreq(len(x), 1 / SR) ** 2
    ra = (12194 ** 2 * f2 ** 2) / ((f2 + 20.6 ** 2) * np.sqrt((f2 + 107.7 ** 2) * (f2 + 737.9 ** 2)) * (f2 + 12194 ** 2) + 1e-30)
    return np.fft.irfft(X * ra / 0.7943, len(x))   # 1 kHz = 0 dB


def win_max(x, w):
    n = int(w * SR)
    return np.sqrt(np.convolve(x ** 2, np.ones(n) / n, mode="valid").max()) if len(x) > n else np.sqrt((x ** 2).mean())


def peak_soft(x, reduce_db, ms=1.0):
    """센 순간만 reduce_db까지 눌러 둥글게 — 1 ms 앞을 내다보는 최대치 + 2 ms 고르기 (tame은 5 ms 단위라 첫 1 ms가 빠져나감 — 겪은 일 11-24 ④)."""
    w = max(1, int(ms / 1000 * SR))
    y = np.pad(np.abs(x), (w, w), mode="edge")
    pk = np.lib.stride_tricks.sliding_window_view(y, 2 * w + 1).max(1)
    thr = np.abs(x).max() * 10 ** (-reduce_db / 20)
    g = np.convolve(np.minimum(1.0, thr / (pk + 1e-12)), np.ones(2 * w) / (2 * w), mode="same")
    return x * g


def save_spot(rel, x, a_db=-20.0):
    """한 번 소리 저장 — 귀 감도 (A 0.25초 최대)가 a_db가 되게 save의 50 ms 목표를 정함. 피크 −1 dB에 걸리면 센 순간을 그만큼 (8 dB까지) 먼저 누름 (보고 줄에 A 값)."""
    over = 20 * np.log10(np.abs(x).max() * 10 ** ((a_db - 20 * np.log10(win_max(a_weight(x), 0.25) + 1e-12)) / 20) + 1e-12) + 1.0
    if over > 0.3:
        x = peak_soft(x, min(8.0, over + 0.5))
    w50 = 20 * np.log10(win_max(x, 0.05) + 1e-12)
    a = 20 * np.log10(win_max(a_weight(x), 0.25) + 1e-12)
    save(rel, x, w50 + (a_db - a))
    y = x * 10 ** ((a_db - a) / 20)
    g = min(1.0, 10 ** (-1 / 20) / (np.abs(y).max() + 1e-12))
    report[-1] += f"  A {a_db + 20 * np.log10(g):.1f}"


MAINS = [60, 120, 180, 240, 300, 360, 480]   # 녹음에 깔린 전기 웅 (120 Hz = B2−48 · 조 밖)


def shot(x, hp=70, lp=None, tame_db=0.0, soft=None, fin=0.003, fout=0.15, lead=True, lines=()):
    """한 번 소리 다듬기: 앞 빈 곳 자르기 → 전기 웅 · 조 밖 줄 깎기 → 낮은 쪽 정리 → 센 곳 누르기 → 고음 누르기 → 페이드."""
    if lead:
        x = trim_lead(x, -40)
    x = notch_lines(x, MAINS + list(lines), 20, 2.0)
    x = highpass(x, hp)
    if tame_db > 0:
        x = tame(x, tame_db)
    if soft:
        x = soften(x, soft, 0.6)
    if lp:
        x = onepole_lp(onepole_lp(x, lp), lp)
    return fade(x, fin, min(fout, len(x) / SR * 0.5))


def peak_hz(x, lo, hi):
    n = 1 << 17
    P = np.abs(np.fft.rfft(x * np.hanning(len(x)), n)) ** 2
    f = np.fft.rfftfreq(n, 1 / SR)
    m = (f >= lo) & (f <= hi)
    return f[m][np.argmax(P[m])]


def tones(n_sec, parts):
    """합성 신호: (시작초, 소리) 목록을 길이 n_sec 판에 얹음."""
    x = np.zeros(int(n_sec * SR))
    for at, s in parts:
        i = int(at * SR)
        x[i:i + len(s)] += s[: len(x) - i]
    return x


F4, C5, F5, G5, A5, C6, D6, F6, G6 = 349.23, 523.25, 698.46, 783.99, 880.0, 1046.5, 1174.66, 1396.91, 1567.98

# 생활 기본 — 그릇 내려놓음 둘 (도자기 그릇, 몸통 540 ~ 710 Hz · crest 8 · ring 0 — 30 순간 중 바닥 −47 dB로 가장 깨끗한 둘, 4 kHz 위 반쯤 누름)
save_spot(SP_ + "spot_life_0_01.wav", shot(rpc("bowl", "bowl", 64.45), tame_db=3, soft=4000))
save_spot(SP_ + "spot_life_0_02.wav", shot(rpc("bowl", "bowl", 9.45), tame_db=3, soft=4000))
# 나무 서랍 닫힘 (236 Hz 툭 · 1.2초 · 바닥 −37 dB) · 욕실 수납장 닫힘 (169 Hz · 바닥 −62 dB) — 둘 다 300 Hz 아래 90 %, 날 선 끝 없음
save_spot(SP_ + "spot_life_0_03.wav", shot(rpc("drawer_wood", "drawer", 81.13), hp=60, tame_db=3, fout=0.3))
save_spot(SP_ + "spot_life_0_04.wav", shot(rpc("cabinet", "cab", 31.35), hp=60, tame_db=3, fout=0.3))
# 철제 캐비닛 서랍 (277 Hz · 1.5초 · 바닥 −60 dB, 4 kHz 아래) · 먼 미닫이문 (비닐 미닫이 슥 → 1.3초 툭, distant로 벽 너머)
save_spot(SP_ + "spot_life_0_05.wav", shot(rpc("file_cabinet", "file", 34.05), hp=60, tame_db=3, lp=4000, fout=0.35))
save_spot(SP_ + "spot_life_0_06.wav", shot(distant(rpc("patio_door", "patio", 0.0), 0.4, 11), hp=80, lp=2500, fout=0.4, lead=False))
# 생활 1단계 — 냉장고 서랍 (플라스틱 부드러운 슥 · 258 Hz) · 자동 종이 타월 디스펜서 서보 위잉 (2.1초)
save_spot(SP_ + "spot_life_1_01.wav", shot(rpc("fridge_drawer", "fdrw", 38.58), hp=60, tame_db=2, fout=0.25))
save_spot(SP_ + "spot_life_1_02.wav", shot(rpc("towel_servo", "towel", 0.0), hp=90, lp=4500, fin=0.08, fout=0.3, lead=False, lines=(278, 556)))   # 모터 줄 C#4 깎음
# 알림 "띵-동" 합성 (UI 한 벌과 같은 유리 결, 으뜸 · 5도 · 옥타브만 — 음계로 옮겨도 조 안): 파5 → 도6 / 파5 → 파6 (기준음 파5 = 77)
save_spot(SP_ + "spot_life_1_03.wav", fade(tones(0.7, [(0.0, ui_glass(F5, 0.3, 0.09)), (0.13, 0.8 * ui_glass(C6, 0.5, 0.13))]), 0.002, 0.2), -22)
save_spot(SP_ + "spot_life_1_04.wav", fade(tones(0.75, [(0.0, ui_glass(F5, 0.3, 0.09, 1)), (0.15, 0.7 * ui_glass(F6, 0.55, 0.12, 1, 0.6))]), 0.002, 0.2), -22)

# 물 기본 — 물방울 둘 (욕조에 떨어지는 물방울, 바닥 −49 dB · 30 순간 중 높이가 파6 (1389 · 1422 Hz)인 둘 → 높이를 재서 정확히 파6으로 · 짧은 잔향으로 0.3초)
for k, st in enumerate((1.55, 7.98)):
    x = rpc("tub_drip", "drip", st)
    x = pitch(x, 12 * np.log2(F6 / peak_hz(x, 1200, 1700)))
    save_spot(SP_ + f"spot_water_0_0{k + 1}.wav", shot(distant(x, 0.3, 21 + k), hp=150, fout=0.12))
# 배관 꿀렁 둘 (물 꼴깍 거품, 322 · 355 Hz · 바닥 −43 dB)
save_spot(SP_ + "spot_water_0_03.wav", shot(rpc("glug", "glug", 0.0), hp=60, lp=5000, fout=0.2))
save_spot(SP_ + "spot_water_0_04.wav", shot(rpc("glug", "glug", 7.38), hp=60, lp=5000, fout=0.25))
# 작은 펌프 켜짐 (위잉 오름 1.6초) · 꺼짐 (위잉 내림 2.8초 — 바닥 −85 dB까지 잦아듦)
save_spot(SP_ + "spot_water_0_05.wav", shot(rpc("pump_small", "pump", 0.0), hp=50, lp=4000, fin=0.05, fout=0.35, lead=False))
save_spot(SP_ + "spot_water_0_06.wav", shot(rpc("pump_small", "pump", 27.1), hp=50, lp=2500, fin=0.05, fout=0.3, lead=False, lines=(305, 610)))   # 내려가는 위잉 Eb4 줄 깎음
# 물 1단계 — 분무 칙 (호스 안개 모드 시작 1.0초 · 끝 0.4초 페이드, 3.5 kHz 아래 — 5 kHz면 AI Pink noise 0.10) · 분무기 칙 (0.5초, 6.7 kHz 밝음 → 반 누름)
save_spot(SP_ + "spot_water_1_01.wav", shot(rpc("mist", "mist", 0.0)[: int(1.0 * SR)], hp=120, lp=3500, fin=0.03, fout=0.4, lead=False, lines=(211, 422)))
save_spot(SP_ + "spot_water_1_02.wav", shot(rpc("spray_bottle", "spray", 0.0), hp=150, soft=4000, lp=6000, fout=0.15))
# 기포 "퐁" 합성 (바이오스피어 반딧불 병 퐁과 같은 틀 — 미끄러져 올라와 파5에 닿음, 기준음 파5 = 77)
save_spot(SP_ + "spot_water_1_03.wav", fade(pop_glide(F5, 0.6), 0.002, 0.2), -22)
save_spot(SP_ + "spot_water_1_04.wav", fade(onepole_lp(pop_glide(F5, 0.45) * np.exp(-np.arange(int(0.45 * SR)) / SR / 0.09), 3000), 0.002, 0.15), -22)

# 기계 기본 — 먹먹한 금속 툭 둘 (큰 금속 들통, 290 ~ 330 Hz · ring 0 · 바닥 −37 dB, 2 kHz 아래 — 파이프 떨어뜨림 · 금속 봉 녹음은 ring 39 ~ 80 쇳소리 울림이라 뺌)
save_spot(SP_ + "spot_machine_0_01.wav", shot(rpc("metal_pail", "pail", 19.98), hp=60, tame_db=3, lp=2000, fout=0.3))
save_spot(SP_ + "spot_machine_0_02.wav", shot(rpc("metal_pail", "pail", 17.68), hp=60, tame_db=3, lp=2000, fout=0.3))
# 증기 칙 (김 빼는 밸브 1.7초 · 2 kHz 몸통) · 김 빠짐 끝 (스팀 청소기 꺼질 때 1.5초)
save_spot(SP_ + "spot_machine_0_03.wav", shot(rpc("steam_release", "steam", 0.0), hp=150, lp=3500, fout=0.6, lead=False))   # 6 kHz 아래면 AI Static 0.14 → 3.5 kHz
save_spot(SP_ + "spot_machine_0_04.wav", shot(rpc("steam_cleaner", "steamc", 7.35), hp=150, lp=6000, fin=0.1, fout=0.3, lead=False))
# 래칫 드르륵 둘 (렌치, 50 순간 중 crest 12 ~ 14 · 시작 38 ~ 51 ms로 가장 둥근 둘 — 6 kHz 밝은 딸깍 덩어리라 3.5 kHz 아래 + 4 dB 누름)
save_spot(SP_ + "spot_machine_0_05.wav", shot(rpc("ratchet", "ratchet", 33.8), hp=100, tame_db=4, lp=3500, fout=0.15))
save_spot(SP_ + "spot_machine_0_06.wav", shot(rpc("ratchet", "ratchet", 76.5), hp=100, tame_db=4, lp=3500, fout=0.15))
# 기계 1단계 — 서보 위잉 켜짐 · 멈춤 (책상 높이 조절 모터)
save_spot(SP_ + "spot_machine_1_01.wav", shot(rpc("desk_servo", "desk", 0.4), hp=80, lp=4000, fin=0.12, fout=0.4, lead=False, lines=(138, 276)))
save_spot(SP_ + "spot_machine_1_02.wav", shot(rpc("desk_servo", "desk", 13.3), hp=80, lp=4000, fin=0.1, fout=0.3, lead=False, lines=(246, 492, 721, 1442)))
# 레이저 프린터 종이 슥 (1.5초) · 디스크 드라이브 문 닫힘 + 모터 (0.9초)
save_spot(SP_ + "spot_machine_1_03.wav", shot(rpc("printer", "print", 3.1), hp=100, lp=5000, fin=0.05, fout=0.4, lead=False, lines=(138, 276)))
save_spot(SP_ + "spot_machine_1_04.wav", shot(rpc("disc_drive", "cd", 0.0), hp=80, tame_db=4, lp=5000, fout=0.2))
# 데이터 "삐빅" 합성 (도6 두 번 / 도6 → 솔6 — 으뜸 · 5도, 기준음 도6 = 84)
save_spot(SP_ + "spot_machine_1_05.wav", fade(tones(0.4, [(0.0, ui_glass(C6, 0.1, 0.025, 2, 0.5)), (0.085, ui_glass(C6, 0.1, 0.025, 2, 0.5))]), 0.002, 0.15), -24)
save_spot(SP_ + "spot_machine_1_06.wav", fade(tones(0.45, [(0.0, ui_glass(C6, 0.1, 0.025, 3, 0.5)), (0.09, 0.8 * ui_glass(G6, 0.14, 0.035, 3, 0.5))]), 0.002, 0.15), -24)

# 고열 기본 — 가스 불 붙음 (점화 딸깍 8.5 kHz를 6 dB 누르고 → 181 Hz "후웅") · 김 쉬익 (스팀 청소기 켜질 때 1.4초)
save_spot(SP_ + "spot_heat_0_01.wav", shot(rpc("gas_ignite", "gas", 0.0), hp=60, tame_db=6, soft=4000, fout=0.4, lines=(105, 126)))
save_spot(SP_ + "spot_heat_0_02.wav", shot(rpc("steam_cleaner", "steamc", 0.0), hp=150, lp=5000, fout=0.5, lead=False))
# 불꽃 탁 둘 (모닥불, 크게 튀는 순간)
save_spot(SP_ + "spot_heat_0_03.wav", shot(rpc("campfire", "camp", 28.5), hp=80, tame_db=3, lp=6000, fout=0.15))
save_spot(SP_ + "spot_heat_0_04.wav", shot(rpc("campfire", "camp", 46.6), hp=80, tame_db=3, lp=6000, fout=0.2))
# 솥뚜껑 달그락 둘 (큰 냄비 뚜껑, 424 ~ 600 Hz · crest 7 ~ 7.7 · ring 0 ~ 3 — 32 순간 중 가장 둥근 둘, 4 kHz 아래)
save_spot(SP_ + "spot_heat_0_05.wav", shot(rpc("pot_lid", "lid", 49.08), hp=80, tame_db=3, lp=4000, fout=0.2))
save_spot(SP_ + "spot_heat_0_06.wav", shot(rpc("pot_lid", "lid", 32.0), hp=80, tame_db=3, lp=4000, fout=0.3))
# 고열 1단계 — 열풍 팬 켜짐 (오븐 대류 팬이 도는 3.4초 — 첫 삑은 뺀 자리부터) · 열풍 한 번 일렁임 (에어프라이어 팬 1.8초에 부풀었다 잦아드는 곡선, 바탕과 같이 −50센트)
save_spot(SP_ + "spot_heat_1_01.wav", shot(rpc("oven_fan", "oven", 0.6), hp=60, lp=3000, fin=0.3, fout=0.8, lead=False))
x = rpc("fryer_fan", "fryer", 46.5)[: int(1.8 * SR)]
save_spot(SP_ + "spot_heat_1_02.wav", shot(repitch_loop(x * np.sin(np.pi * np.arange(len(x)) / len(x)) ** 1.5, -50), hp=60, lp=2000, fout=0.1, lead=False, lines=(193, 202, 206, 404, 412, 724)))
# 보글 둘 (큰 솥 끓는 거품 한 덩이, 440 ~ 530 Hz)
save_spot(SP_ + "spot_heat_1_03.wav", shot(rpc("cauldron", "cauld", 31.88), hp=80, lp=4000, fin=0.01, fout=0.2))
save_spot(SP_ + "spot_heat_1_04.wav", shot(rpc("cauldron", "cauld", 40.18), hp=80, lp=4000, fin=0.01, fout=0.2))
# 온기 신호 합성 (UI 유리 결 파5 · 도6 5도 화음이 20 ms에 차오름 + 따뜻한 바람결 — 기준음 파5 = 77). 낮은 유리 퐁 (파4, 2 kHz 아래)은 AI Sine wave 0.71이라 뺌.
warm = lambda f, d, s: ui_glass(f, d, 0.16, s, 0.8) * np.minimum(1.0, np.arange(int(d * SR)) / (0.02 * SR))
save_spot(SP_ + "spot_heat_1_05.wav", fade(tones(0.8, [(0.0, ui_air(0.35, True, 6) * 3), (0.12, warm(F5, 0.6, 6) + 0.7 * warm(C6, 0.6, 7))]), 0.01, 0.25), -24)
save_spot(SP_ + "spot_heat_1_06.wav", fade(tones(0.8, [(0.0, warm(C6, 0.5, 8) * 0.8), (0.16, warm(F5, 0.6, 9)), (0.1, ui_air(0.3, False, 7) * 3)]), 0.01, 0.25), -24)

# 전기 기본 — 벽 스위치 딸깍 (786 Hz · 바닥 −44 dB, 61 순간 중 몸통이 가장 낮은 것) · 토글 스위치 (1.6 kHz · 300 Hz 아래 58 %)
save_spot(SP_ + "spot_electric_0_01.wav", shot(rpc("wall_switch", "switch", 9.88), hp=80, tame_db=4, soft=4000, fout=0.2))
save_spot(SP_ + "spot_electric_0_02.wav", shot(rpc("toggle_switch", "toggle", 16.92), hp=80, tame_db=5, soft=4000, fout=0.15))
# 작은 지직 둘 (전기 아크 — 1.6 · 3.5 kHz, 0.35 ~ 0.6초, 4 kHz 아래 + 4 dB 누름 — 테슬라 코일은 crest 19 · 5 kHz로 날 서서 뺌)
save_spot(SP_ + "spot_electric_0_03.wav", shot(rpc("electric_arcs", "arc", 14.0), hp=150, tame_db=4, lp=4000, fout=0.12))
save_spot(SP_ + "spot_electric_0_04.wav", shot(rpc("electric_arcs", "arc", 39.4), hp=150, tame_db=4, lp=4000, fout=0.15))
# 버튼 딸깍 둘 (네모 버튼, 1.4 · 1.5 kHz · 바닥 −40 dB)
save_spot(SP_ + "spot_electric_0_05.wav", shot(rpc("push_button", "button", 34.88), hp=80, tame_db=4, soft=4000, fout=0.15))
save_spot(SP_ + "spot_electric_0_06.wav", shot(rpc("push_button", "button", 19.05), hp=80, tame_db=4, soft=4000, fout=0.15))
# 전기 1단계 — 충전 다 됨 합성 (유리 결 파5 → 도6 → 파6 빠른 오름) · 짧은 삐 둘 (파5 · 도6) — 기준음 파5 = 77.
# 파5 → 도6로 미끄러져 오르는 결은 AI Siren 0.40 · Police car라 뺌 (높이가 오르내리는 소리 = 사이렌).
save_spot(SP_ + "spot_electric_1_01.wav", fade(tones(0.8, [(0.0, 0.7 * ui_glass(F5, 0.2, 0.05, 4, 0.6)), (0.07, 0.75 * ui_glass(C6, 0.2, 0.05, 4, 0.6)), (0.14, 0.8 * ui_glass(F6, 0.6, 0.14, 4, 0.6))]), 0.002, 0.25), -24)
save_spot(SP_ + "spot_electric_1_02.wav", fade(tones(0.5, [(0.0, ui_glass(F5, 0.12, 0.03, 5, 0.5)), (0.12, 0.8 * ui_glass(C6, 0.16, 0.04, 5, 0.5))]), 0.002, 0.15), -24)

# 넓은 공간 기본 — 먼 금속 쿵 둘 (큰 통 두드림 265 ~ 276 Hz · crest 6.6 ~ 7.7 · ring 3, 잔향 45 % + 1.8 kHz 아래 — 큰 홀 저편)
save_spot(SP_ + "spot_hall_0_01.wav", shot(distant(rpc("giant_tub", "tub", 5.65), 0.45, 31), hp=60, lp=1800, fout=0.4))
save_spot(SP_ + "spot_hall_0_02.wav", shot(distant(rpc("giant_tub", "tub", 11.35), 0.45, 32), hp=60, lp=1800, fout=0.4))
# 먼 큰 문 닫힘 (업소용 냉동고 문 320 Hz 툭) · 먼 셔터 (멀리서 녹음한 차고 문 움직임 2.6초)
save_spot(SP_ + "spot_hall_0_03.wav", shot(distant(rpc("freezer_door", "freezer", 3.2), 0.45, 33), hp=60, lp=2000, fout=0.35))
save_spot(SP_ + "spot_hall_0_04.wav", shot(distant(rpc("garage_door", "garage", 1.0), 0.3, 34), hp=60, lp=2000, fin=0.3, fout=0.6, lead=False))
# 공기 휙 둘 (느리고 낮은 휙 — 환기 흐름, 녹음 속 좁은 울림 507 · 838 · 1014 · 1983 Hz (B4 · G#5 · B5 · B6 — 조 밖)를 깎음)
save_spot(SP_ + "spot_hall_0_05.wav", shot(rpc("low_whoosh", "whoosh", 0.0), hp=80, lp=3000, fin=0.05, fout=0.6, lead=False, lines=(507, 838, 1014, 1983)))
save_spot(SP_ + "spot_hall_0_06.wav", shot(rpc("low_whoosh", "whoosh", 2.95), hp=80, lp=3000, fin=0.05, fout=0.5, lead=False, lines=(507, 838, 1014, 1983)))
# 넓은 공간 1단계 — 먼 자동문 열림 (슥 2.7초) · 닫힘 (3초). 먼 엘리베이터 웅은 AI Music 0.27 · Timpani (낮은 울림 음)라 뺌.
save_spot(SP_ + "spot_hall_1_01.wav", shot(distant(rpc("auto_door", "autod", 0.0), 0.4, 35), hp=80, lp=2500, fin=0.05, fout=0.5, lead=False))
save_spot(SP_ + "spot_hall_1_02.wav", shot(distant(rpc("auto_door", "autod", 6.9), 0.4, 36), hp=80, lp=2500, fin=0.05, fout=0.5, lead=False))
# 먼 안내 종 합성 (둥근 유리 퐁 도5 → 솔5 → 도6, 으뜸 · 5도 · 옥타브만, 잔향으로 멀리 — 기준음 도5 = 72)
save_spot(SP_ + "spot_hall_1_03.wav", fade(distant(tones(2.0, [(0.0, glass_pong(C5, 1.2, 0)), (0.22, 0.8 * glass_pong(G5, 1.2, 2)), (0.44, 0.6 * glass_pong(C6, 1.2, 4))]), 0.5, 37), 0.003, 0.5), -24)
save_spot(SP_ + "spot_hall_1_04.wav", fade(distant(tones(1.6, [(0.0, 0.8 * glass_pong(C6, 1.2, 5)), (0.24, glass_pong(C5, 1.2, 7))]), 0.5, 38), 0.003, 0.5), -24)

# --- 상태 층 (Interior/State/, D-5 ③ — 지금 있는 방 상태를 읽어 깔림) ---
ST_ = "Interior/State/"
# 파손 = 선체 삐걱 넷 (배 삐걱 녹음의 짧은 덩어리 0.7 ~ 0.9초 — 594 Hz 마찰 울림 (D5+22, 조 안)이 든 "끼익", 3반음 낮춰 큰 선체 · 2.5 kHz 아래)
# 뺀 후보: 욕실 문 삐걱 (2.7 ~ 3.9 kHz 끽 · ring 39 ~ 90 — 날 선 쇳소리), 긴 덩어리 (1.5초 넘으면 AI Machine gun · Hammer — 마찰 결이 따다닥으로 들림), 마루 삐걱 · 서보 침대 삐걱 (Music 0.2 · Tuning fork).
for k, st in enumerate((8.95, 45.95, 60.55, 25.55)):
    save_spot(ST_ + f"state_creak_0{k + 1}.wav", shot(pitch(rpc("ship_creak", "creak", st), -3), hp=80, tame_db=3, lp=2500, fin=0.02, fout=0.25, lead=False))
# 파손 = 전선 지직 루프 (6초): 전기 아크 지직 셋 (1.1 · 3.4 · 4.9초 자리, 4 kHz 아래 · 누름) + 아주 옅은 안정기 웅 (Bb2, 700 Hz 아래 −14 dB — 빈 사이를 이음)
hz = notch_lines(repitch_loop(hpc(rchain("ballast_hum", "hum", [6.0, 13.0], 7.0, 1.5), 40), -51), BB_OFF, 24, 2.0)
bed = lpc(loop_exact(np.concatenate([hz, hz])[: int(6.9 * SR)], 6.0), 700)
spk = np.zeros(len(bed))
for at, (st, ln) in zip((1.1, 3.4, 4.9), ((14.0, 0.45), (39.4, 0.7), (53.0, 0.6))):
    s = shot(rpc("electric_arcs", "arc", st)[: int(ln * SR)], hp=200, tame_db=5, lp=4000, fout=0.1)
    i = int(at * SR)
    spk[i:i + len(s)] += (s / (np.abs(s).max() + 1e-12))[: len(spk) - i]
save(ST_ + "state_spark_loop.wav", spk / rms(spk) * rms(bed) * 10 ** (14 / 20) + bed, -20)
# 전력 부족 · 비활성 = 웅이 낮아지며 꺼짐 (4.4초): 벽 스위치 딸깍 (앞 0.25초) → 컴퓨터 본체가 꺼지며 팬 · 디스크가 서는 소리 (pc_fan 44초 — AI Rumble 0.68).
# 뺀 시안: 안정기 웅을 재생 빠르기로 한 옥타브 내림 (AI Music 0.23 · Boing — 만든 소리로 들림), 청소기 · 핸드 드라이어 멈춤 (Hair dryer · Vacuum cleaner).
clk = shot(rpc("wall_switch", "switch", 9.88)[: int(0.25 * SR)], hp=80, tame_db=5, soft=4000, fout=0.08)
off = shot(rpc("pc_fan", "pc", 44.0), hp=50, lp=3000, fin=0.02, fout=0.3, lead=False, lines=(53, 106, 138, 159))
save_spot(ST_ + "state_powerdown.wav", fade(tones(len(off) / SR + 0.1, [(0.0, clk / (np.abs(clk).max() + 1e-12) * 0.35), (0.1, off / (np.abs(off).max() + 1e-12))]), 0.002, 0.2))
# 산소 부족 = 환기 팬 멈춤 (큰 선풍기를 끌 때 2.8초 — 날개가 서며 웅이 잦아듦)
save_spot(ST_ + "state_fan_stop.wav", shot(rpc("fan_large", "fan", 119.0), hp=50, lp=3000, fin=0.05, fout=0.3, lead=False))
# 과열 = 끓는 거품 루프 (커피 퍼컬레이터가 끓는 4.4초 조각 셋을 이음 — AI Water 0.29 · Liquid · Boiling 0.16, 큰 솥 녹음은 Water · 수도꼭지 쪽이라 뺌)
save(ST_ + "state_boil_loop.wav", lpc(hpc(notch_lines(rrc("percolator", "perc", [1.0, 10.0, 18.0], 4.4, 1.0), [53, 67, 106], 20, 2.0), 80), 4000), -24)

print("\n".join(report))
