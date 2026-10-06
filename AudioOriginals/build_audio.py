"""
SpaceStation 5-8 사운드 가공·합성 스크립트.
원본(AudioOriginals/)을 읽어 Assets/_Project/Audio/ 아래에 게임용 WAV를 만든다. 원본은 건드리지 않는다.
실행: blender -b --factory-startup --python build_audio.py   (Blender 내장 파이썬의 aud·numpy 사용)
- 가공: 여러 소리가 든 파일을 낱개로 분리, 길이 다듬기(페이드), 앞 무음 제거, 루프 이음새 크로스페이드, 음량 정규화
- 합성: 목록에 없던 소리(배치·철거·운석 충돌·레이저·실드·파손·경보 등)를 수치 합성
"""
import aud, numpy as np, os, wave

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = HERE
DST = os.path.join(HERE, "..", "SpaceStation", "Assets", "_Project", "Audio")
SR = 48000
rng = np.random.default_rng(7)
report = []


# ---------------- 입출력 ----------------

def load(rel):
    s = aud.Sound(os.path.join(SRC, rel))
    rate = int(s.specs[0])
    d = np.asarray(s.data(), dtype=np.float64)
    if d.ndim > 1:
        d = d.mean(axis=1)
    if rate != SR:
        d = resample(d, SR / rate)
    return d


def save(rel, x, target_db=-10.0, peak_db=-1.0):
    """짧은 구간(50ms) 최대 RMS를 target_db로 맞추고, 피크는 peak_db를 넘지 않게."""
    x = np.asarray(x, dtype=np.float64)
    w = int(SR * 0.05)
    if len(x) > w:
        sq = np.convolve(x ** 2, np.ones(w) / w, mode="valid")
        st = np.sqrt(sq.max())
    else:
        st = np.sqrt((x ** 2).mean())
    g = 10 ** (target_db / 20) / max(st, 1e-9)
    pk = np.abs(x).max() * g
    lim = 10 ** (peak_db / 20)
    if pk > lim:
        g *= lim / pk
    y = np.clip(x * g, -1, 1)
    path = os.path.join(DST, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with wave.open(path, "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes((y * 32767).astype("<i2").tobytes())
    report.append(f"{rel:34s} {len(y)/SR:5.2f}s  peak {20*np.log10(max(np.abs(y).max(),1e-9)):6.1f} dB")


# ---------------- 도구 ----------------

def t_axis(dur):
    return np.arange(int(SR * dur)) / SR


def sl(x, a, b):
    return x[int(a * SR):int(b * SR)].copy()


def fade(x, fin=0.003, fout=0.03):
    x = x.copy()
    ni, no = int(fin * SR), int(fout * SR)
    if ni > 0:
        x[:ni] *= np.linspace(0, 1, ni) ** 2
    if no > 0:
        x[-no:] *= np.linspace(1, 0, no) ** 2
    return x


def trim_lead(x, thresh_db=-45):
    thr = np.abs(x).max() * 10 ** (thresh_db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    start = max(0, idx[0] - int(0.002 * SR)) if len(idx) else 0
    return x[start:]


def resample(x, ratio):
    """ratio > 1: 길어짐(낮아짐)."""
    n = int(len(x) * ratio)
    return np.interp(np.arange(n) / ratio, np.arange(len(x)), x)


def varispeed(x, r0, r1):
    """재생 속도를 r0→r1로 바꾸며 읽기 (테이프 속도 변화)."""
    pos, out = 0.0, []
    n = len(x)
    # 대략 길이 추정 후 선형 보간
    est = int(n / max(0.05, (r0 + r1) / 2))
    rates = np.linspace(r0, r1, est)
    p = np.cumsum(rates)
    p = p[p < n - 1]
    return np.interp(p, np.arange(n), x)


def onepole_lp(x, fc):
    a = np.exp(-2 * np.pi * fc / SR)
    y = np.empty_like(x)
    s = 0.0
    for i in range(len(x)):
        s = (1 - a) * x[i] + a * s
        y[i] = s
    return y


def onepole_hp(x, fc):
    return x - onepole_lp(x, fc)


def lp_sweep(x, f0, f1):
    """차단 주파수가 f0→f1(지수)로 움직이는 1차 로우패스."""
    fc = f0 * (f1 / f0) ** np.linspace(0, 1, len(x))
    a = np.exp(-2 * np.pi * fc / SR)
    y = np.empty_like(x)
    s = 0.0
    for i in range(len(x)):
        s = (1 - a[i]) * x[i] + a[i] * s
        y[i] = s
    return y


def tilt_soft(x, fc=6000, amount=0.5):
    """고음 날카로움 완화: 원본과 로우패스를 섞음."""
    return (1 - amount) * x + amount * onepole_lp(onepole_lp(x, fc), fc)


def reverb(x, wet=0.2, decay=0.35, length=0.9, tone=5000):
    n = int(length * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-t / decay)
    ir = onepole_lp(ir, tone)
    ir[: int(0.012 * SR)] = 0  # 짧은 프리딜레이
    ir /= np.sqrt((ir ** 2).sum())
    size = 1 << int(np.ceil(np.log2(len(x) + n)))
    y = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[: len(x) + n]
    dry = np.concatenate([x, np.zeros(n)])
    out = dry + wet * y * (np.abs(x).max() / max(np.abs(y).max(), 1e-9))
    # 꼬리 끝 정리
    return fade(trim_tail(out), 0, 0.05)


def trim_tail(x, thresh_db=-60):
    thr = np.abs(x).max() * 10 ** (thresh_db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    return x[: idx[-1] + 1] if len(idx) else x


def place(dst, src, at):
    i = int(at * SR)
    end = min(len(dst), i + len(src))
    dst[i:end] += src[: end - i]
    return dst


def env_exp(dur, tau, attack=0.002):
    t = t_axis(dur)
    e = np.exp(-t / tau)
    na = int(attack * SR)
    if na > 0:
        e[:na] *= np.linspace(0, 1, na)
    return e


def modal(dur, freqs, decays, amps, attack=0.0008):
    t = t_axis(dur)
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays, amps):
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / d)
    na = int(attack * SR)
    out[:na] *= np.linspace(0, 1, na)
    return out


def sweep_sine(dur, f0, f1, tau):
    """주파수가 f0→f1로 지수 수렴하는 사인."""
    t = t_axis(dur)
    f = f1 + (f0 - f1) * np.exp(-t / tau)
    return np.sin(2 * np.pi * np.cumsum(f) / SR)


def noise(dur):
    return rng.standard_normal(int(dur * SR))


def bandnoise(dur, lo, hi):
    return onepole_lp(onepole_hp(noise(dur), lo), hi)


def loop_xfade(x, xf):
    """끝 xf초를 처음과 등전력 크로스페이드 → 이음새 없는 루프."""
    c = int(xf * SR)
    y = x[: len(x) - c].copy()
    k = np.linspace(0, 1, c)
    y[:c] = x[:c] * np.sin(k * np.pi / 2) + x[len(x) - c:] * np.cos(k * np.pi / 2)
    return y


def segment_at(x, start, length, fout=0.04):
    return fade(sl(x, start, start + length), 0.002, fout)


# ================= UI =================

click = load("UI/ui_click.wav")
for i, s in enumerate([0.07, 1.22, 2.10, 2.98]):
    seg = segment_at(click, s - 0.004, 0.11, 0.03)
    save(f"UI/ui_click_{i+1:02d}.wav", tilt_soft(trim_lead(seg), 5000, 0.45), -12)

hover = load("UI/ui_hover.wav")
for i, s in enumerate([0.07, 1.87]):
    seg = segment_at(hover, s - 0.004, 0.06, 0.02)
    save(f"UI/ui_hover_{i+1:02d}.wav", tilt_soft(trim_lead(seg), 4000, 0.6), -14)

tab = load("UI/ui_tab.wav")
seg = trim_lead(sl(tab, 0.95, 1.40))
seg = varispeed(seg, 0.85, 1.35)  # 살짝 올라가는 "슉"
save("UI/ui_tab.wav", fade(seg[: int(0.28 * SR)], 0.004, 0.12), -12)

oc = load("UI/ui_open&close.wav")
save("UI/ui_open.wav", fade(trim_lead(sl(oc, 0.0, 0.62)), 0.003, 0.25), -12)
save("UI/ui_close.wav", fade(trim_lead(sl(oc, 2.15, 2.75)), 0.003, 0.25), -12)

speed = load("UI/ui_speed.wav")
base = trim_lead(sl(speed, 0.05, 0.75))
save("UI/ui_speed_up.wav", fade(varispeed(base, 0.7, 1.6), 0.004, 0.08), -13)
save("UI/ui_pause.wav", fade(varispeed(base, 1.0, 0.22)[: int(0.5 * SR)], 0.004, 0.12), -13)
save("UI/ui_resume.wav", fade(varispeed(base, 0.3, 1.1)[: int(0.45 * SR)], 0.004, 0.08), -13)

# 거절음 (합성): 낮은 "부-웅" 두 번
def buzz(dur, f):
    t = t_axis(dur)
    ph = 2 * np.pi * f * t
    s = np.sin(ph) + 0.35 * np.sin(3 * ph) + 0.18 * np.sin(5 * ph)
    return fade(s, 0.004, 0.035)
err = np.zeros(int(0.32 * SR))
place(err, buzz(0.11, 196), 0.0)
place(err, buzz(0.13, 165), 0.15)
save("UI/ui_error.wav", onepole_lp(err, 2500), -14)


# ================= 건설 =================

sel = load("Build/build_select.wav")
save("Build/build_select.wav", fade(trim_lead(sel), 0.002, 0.05), -13)

rot = load("Build/build_rotate.wav")
for i, s in enumerate([0.17, 1.30, 3.08, 3.80]):
    seg = trim_lead(segment_at(rot, s - 0.01, 0.26, 0.06))
    save(f"Build/build_rotate_{i+1:02d}.wav", tilt_soft(seg, 5000, 0.4), -14)

rs = load("Build/repair_start.wav")
save("Build/repair_start.wav", fade(trim_lead(sl(rs, 0.0, 1.2)), 0.002, 0.45), -12)
rd = load("Build/repair_done.wav")
save("Build/repair_done.wav", fade(trim_lead(sl(rd, 0.0, 1.0)), 0.002, 0.45), -11)

# 정비: 래칫 두 번 (렌치) — 회전음 재활용, 살짝 낮게
mt = np.zeros(int(0.75 * SR))
place(mt, resample(trim_lead(segment_at(rot, 1.29, 0.3, 0.05)), 1.18), 0.0)
place(mt, resample(trim_lead(segment_at(rot, 3.79, 0.3, 0.05)), 1.25), 0.33)
save("Build/maintain.wav", tilt_soft(mt, 4500, 0.5), -13)

# 배치 (합성): 도킹 "철컹" + 압력 "칙"
d = 1.0
x = np.zeros(int(d * SR))
place(x, 0.9 * sweep_sine(0.35, 140, 52, 0.06) * env_exp(0.35, 0.11), 0.0)
place(x, 0.55 * modal(0.6, [138, 287, 523, 829, 1262, 1903], [0.32, 0.22, 0.16, 0.1, 0.07, 0.05], [1, .7, .5, .35, .22, .12]), 0.0)
place(x, 0.4 * modal(0.3, [612, 1180, 2050], [0.07, 0.05, 0.03], [1, .6, .3]), 0.075)
hs = bandnoise(0.45, 1800, 7000) * env_exp(0.45, 0.13, attack=0.012)
place(x, 0.32 * hs, 0.17)
save("Build/build_place.wav", reverb(x, 0.18, 0.3), -9)

# 철거 (합성): 분리 "철컥" + 감압 "슈우"
x = np.zeros(int(1.0 * SR))
place(x, 0.45 * modal(0.25, [890, 1630, 2480], [0.05, 0.035, 0.02], [1, .5, .3]), 0.0)
place(x, 0.6 * modal(0.5, [176, 341, 610, 977, 1420], [0.22, 0.15, 0.1, 0.07, 0.05], [1, .7, .45, .3, .15]), 0.06)
place(x, 0.5 * sweep_sine(0.25, 110, 60, 0.05) * env_exp(0.25, 0.07), 0.06)
w = lp_sweep(noise(0.6), 6000, 700) * env_exp(0.6, 0.2, attack=0.03)
place(x, 0.38 * w, 0.13)
save("Build/build_remove.wav", reverb(x, 0.18, 0.3), -9)

# 통로 연결 (합성): 에어록 "치익-툭"
x = np.zeros(int(0.5 * SR))
h = bandnoise(0.24, 2500, 8000)
h *= np.sin(np.linspace(0, np.pi, len(h))) ** 0.7
place(x, 0.35 * h, 0.0)
place(x, 0.8 * sweep_sine(0.18, 120, 80, 0.04) * env_exp(0.18, 0.05), 0.25)
place(x, 0.35 * modal(0.15, [640, 1170], [0.05, 0.03], [1, .5]), 0.25)
save("Build/build_connect.wav", reverb(x, 0.15, 0.25), -14)


# ================= 이벤트 =================

ep = load("Event/event-positive.wav")
save("Event/event_positive.wav", fade(trim_lead(sl(ep, 0.0, 2.1)), 0.002, 0.9), -11)

en = load("Event/event_negative.wav")
save("Event/event_negative.wav", fade(trim_lead(sl(en, 0.0, 1.5)), 0.002, 0.5), -12)

ee = load("Event/event_end.wav")
save("Event/event_end.wav", fade(trim_lead(sl(ee, 0.03, 0.72)), 0.002, 0.25), -13)

inc = load("Event/event_incoming.wav")
inc = fade(trim_lead(sl(inc, 0.15, 2.6), -40), 0.05, 0.9)
w = int(0.05 * SR)
peak_t = np.convolve(inc ** 2, np.ones(w), "valid").argmax() / SR
report.append(f"  meteor_incoming 최고점 {peak_t:.2f}s")
save("Event/meteor_incoming.wav", inc, -11)

# 운석 충돌 (합성): 저음 쿵 + 금속 공명 + 파편
x = np.zeros(int(1.6 * SR))
place(x, 1.0 * sweep_sine(0.6, 125, 36, 0.09) * env_exp(0.6, 0.22), 0.0)
place(x, 0.75 * onepole_lp(noise(0.5), 1800) * env_exp(0.5, 0.1), 0.0)
fr = np.array([173, 317, 461, 689, 1033, 1490, 2210]) * rng.uniform(0.97, 1.03, 7)
place(x, 0.4 * modal(1.2, fr, [0.45, 0.35, 0.28, 0.2, 0.13, 0.09, 0.06], [1, .8, .6, .45, .3, .2, .12]), 0.005)
for _ in range(26):
    at = 0.05 + rng.exponential(0.22)
    if at > 1.1:
        continue
    c = bandnoise(0.012, 1500, 6000) * env_exp(0.012, 0.003)
    place(x, 0.3 * np.exp(-at / 0.5) * rng.uniform(0.4, 1) * c, at)
save("Event/meteor_impact.wav", reverb(x, 0.25, 0.45), -7)

# 공중 폭발 (합성): 가벼운 "퍽"
x = np.zeros(int(1.0 * SR))
place(x, 0.9 * lp_sweep(noise(0.7), 5000, 250) * env_exp(0.7, 0.14), 0.0)
place(x, 0.7 * sweep_sine(0.4, 100, 40, 0.06) * env_exp(0.4, 0.12), 0.0)
for _ in range(14):
    at = 0.03 + rng.exponential(0.12)
    if at < 0.6:
        place(x, 0.2 * bandnoise(0.008, 2500, 8000) * env_exp(0.008, 0.002) * np.exp(-at / 0.3), at)
save("Event/meteor_explode.wav", reverb(x, 0.3, 0.4), -9)

# 포탑 레이저 (합성): 짧은 "삐융"
d = 0.36
t = t_axis(d)
f = 360 + 2100 * np.exp(-t / 0.055)
ph = 2 * np.pi * np.cumsum(f) / SR
x = (np.sin(ph) + 0.3 * np.sin(2 * ph + 0.5) + 0.12 * np.sin(3 * ph)) * env_exp(d, 0.09, attack=0.0015)
x += 0.25 * bandnoise(d, 3000, 9000) * env_exp(d, 0.012)
x *= 1 + 0.15 * np.sin(2 * np.pi * 38 * t)
save("Event/turret_laser.wav", reverb(onepole_lp(x, 7000), 0.2, 0.25), -11)

# 실드 빗겨냄 (합성): 에너지 장막 "우웅-팅"
d = 1.1
t = t_axis(d)
hum_f = 118 - 18 * (t / d)
hp = 2 * np.pi * np.cumsum(hum_f) / SR
hum = (np.sin(hp) + 0.5 * np.sin(1.5 * hp) + 0.25 * np.sin(3 * hp)) * (1 + 0.35 * np.sin(2 * np.pi * 17 * t))
henv = np.minimum(1, t / 0.06) * np.exp(-np.maximum(0, t - 0.06) / 0.3)
x = 0.55 * hum * henv
x += 0.5 * modal(d, np.array([1, 2.32, 4.25, 6.63]) * 760, [0.42, 0.25, 0.13, 0.07], [1, .5, .28, .14])
x += 0.12 * bandnoise(d, 3000, 9000) * env_exp(d, 0.05)
save("Event/shield_deflect.wav", reverb(x, 0.3, 0.45, tone=6000), -10)

# 모듈 파손 (합성): 경고 "삐빅" + 금속 균열
x = np.zeros(int(0.9 * SR))
for at, f in ((0.0, 1250), (0.11, 990)):
    tt = t_axis(0.075)
    b = (np.sin(2 * np.pi * f * tt) + 0.25 * np.sin(2 * np.pi * 3 * f * tt)) * 0.45
    place(x, fade(b, 0.003, 0.02), at)
crack = onepole_hp(noise(0.06), 1200) * env_exp(0.06, 0.012)
place(x, 0.7 * crack, 0.22)
place(x, 0.45 * modal(0.6, [410, 980, 1660, 2390], [0.18, 0.12, 0.07, 0.04], [1, .7, .4, .2]), 0.22)
creak = lp_sweep(noise(0.35), 900, 300) * (1 + np.sin(2 * np.pi * 31 * t_axis(0.35))) * env_exp(0.35, 0.12, attack=0.02)
place(x, 0.3 * creak, 0.26)
save("Event/module_damaged.wav", reverb(x, 0.15, 0.3), -9)

md = load("Event/module_destroyed.wav")
save("Event/module_destroyed.wav", fade(trim_lead(sl(md, 0.0, 3.2)), 0.002, 1.3), -8)

# 태양 폭풍 루프: 끝 페이드 제거 후 크로스페이드
st = load("Event/solar_storm_loop.wav")
st = sl(st, 0.0, 8.15)
save("Event/solar_storm_loop.wav", loop_xfade(st, 1.5), -14)

# 폭풍 스파크 (합성) 3종: 짧은 전기 "파직"
for v in range(3):
    d = 0.3
    x = np.zeros(int(d * SR))
    at = 0.0
    for _ in range(rng.integers(3, 7)):
        c = onepole_hp(noise(0.03), 1500) * env_exp(0.03, rng.uniform(0.003, 0.01))
        place(x, rng.uniform(0.4, 1.0) * c, at)
        at += rng.uniform(0.01, 0.05)
        if at > 0.2:
            break
    tt = t_axis(0.07)
    bz = np.sign(np.sin(2 * np.pi * 120 * tt)) * env_exp(0.07, 0.03)
    place(x, 0.12 * onepole_lp(bz, 3000), 0.0)
    save(f"Event/storm_spark_{v+1:02d}.wav", fade(trim_tail(tilt_soft(x, 7000, 0.4)), 0.0, 0.02), -14)

# 산소 누출: 고음 위주 쉿 소리에 낮은 대역을 더해 덜 날카롭게
ol = load("Event/oxygen_leak.wav")
ol = sl(ol, 0.0, 2.6)
follow = onepole_lp(np.abs(ol), 20)
body = bandnoise(len(ol) / SR, 900, 4000)
body *= follow / max(follow.max(), 1e-9) * np.abs(ol).max() / max(np.abs(body).max(), 1e-9)
mix = tilt_soft(ol, 8000, 0.5) * 0.7 + body * 0.45
save("Event/oxygen_leak.wav", fade(trim_lead(mix), 0.08, 0.9), -14)

sa = load("Event/supply_arrive.wav")
save("Event/supply_arrive.wav", fade(trim_lead(sl(sa, 0.2, 7.7)), 0.3, 1.2), -12)

gu = load("Event/grade_up.wav")
gu = fade(trim_lead(sl(gu, 0.0, 3.8)), 0.002, 1.2)
save("Event/grade_up.wav", gu, -9)
# 등급 하락: 같은 음을 아래로 미끄러지게
save("Event/grade_down.wav", fade(varispeed(sl(gu, 0, 2.6), 0.9, 0.62), 0.002, 0.8), -11)
# 정거장 완성: 등급 상승 + 5도 위 겹침
vic = np.concatenate([gu, np.zeros(int(0.6 * SR))])
place(vic, 0.5 * resample(gu, 1 / 1.5), 0.28)
save("Event/victory.wav", fade(vic, 0.002, 1.0), -8)

go = load("Event/game_over.wav")
save("Event/game_over.wav", fade(trim_lead(sl(go, 0.0, 2.1)), 0.002, 0.4), -8)

rdp = load("Event/resource_depleted.wav")
save("Event/resource_depleted.wav", fade(trim_lead(sl(rdp, 0.0, 1.7)), 0.002, 0.5), -13)

# 경보 루프 (합성): 1.6초에 한 번 오르내리는 사이렌, 위상이 정확히 이어지도록 주기 정수배
T = 1.6
t = t_axis(T)
f = 620 + 230 * (0.5 - 0.5 * np.cos(2 * np.pi * t / T))
ph = 2 * np.pi * np.cumsum(f) / SR
ph *= round(ph[-1] / (2 * np.pi)) * 2 * np.pi / ph[-1]  # 마지막 위상을 2π 정수배로
x = np.sin(ph) + 0.3 * np.sin(2 * ph) + 0.12 * np.sin(3 * ph) + 0.4 * np.sin(0.5 * ph)
save("Event/alarm_loop.wav", onepole_lp(x, 3500), -15)

# 조기 경보 (합성, Phase 6 방어 연구): 레이더 핑 두 번 + 낮은 확인음
def pip(dur, f):
    t = t_axis(dur)
    tone = np.sin(2 * np.pi * f * t) + 0.25 * np.sin(4 * np.pi * f * t)
    return fade(tone * env_exp(dur, dur * 0.6, 0.003), 0.003, 0.02)

ew = np.zeros(int(0.9 * SR))
place(ew, pip(0.1, 1180), 0.0)
place(ew, pip(0.1, 1180), 0.16)
place(ew, 0.9 * pip(0.26, 880), 0.32)
save("Event/early_warning.wav", trim_tail(reverb(onepole_lp(ew, 5000), 0.18, 0.3, 0.5)), -12)


# ================= 배경 =================

amb = load("Ambient/amb_station_loop.wav")
amb = sl(amb, 0.03, 6.2)
save("Ambient/amb_station_loop.wav", loop_xfade(amb, 1.2), -16)

# ================= 11-9 내부 걸음·방 환경음 (합성) =================

rng = np.random.default_rng(7)  # 섹션별 난수 — 앞 섹션 소비량과 무관하게 이 섹션만 돌려도 같은 결과

# 금속 바닥 발소리 6: 뒤꿈치 "쿵"(낮은 몸통) + 바닥판 울림(짧은 금속 공진) + 신발 마찰 "슥". 변형마다 높이·울림을 조금씩
for k in range(6):
    d = 0.42
    pitch = 1.0 + (k - 2.5) * 0.035
    body = sweep_sine(d, 150 * pitch, 72 * pitch, 0.02) * env_exp(d, 0.045, attack=0.0015)
    ring = modal(d, [612 * pitch, 1290 * pitch, 2240 * pitch, 3410 * pitch], [0.06, 0.04, 0.025, 0.015],
                 [0.35, 0.22, 0.12, 0.06])
    scuff = bandnoise(d, 900, 5200) * env_exp(d, 0.018, attack=0.004)
    x = 1.0 * body + (0.55 + 0.1 * (k % 3)) * ring + 0.32 * scuff
    x = onepole_lp(x, 7000)
    save("Interior/step_metal_%02d.wav" % (k + 1), fade(trim_tail(reverb(x, 0.12, 0.12, 0.25, 4000), -50), 0.001, 0.03), -16)


def slow_mod(dur, rate, depth, phase=0.0):
    """천천히 출렁이는 0..1 근처 계수 (rate Hz, 루프 길이에 맞춰 정수 주기면 이음새가 자연스러움)."""
    t = t_axis(dur)
    return 1.0 - depth + depth * (0.5 + 0.5 * np.sin(2 * np.pi * rate * t + phase))


def hum(dur, f0, harmonics, jitter=0.0):
    t = t_axis(dur)
    out = np.zeros_like(t)
    for i, a in enumerate(harmonics):
        f = f0 * (i + 1)
        out += a * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28) + jitter * np.sin(2 * np.pi * 0.17 * t))
    return out


AMB = 14.0   # 생성 길이 (루프 = 14 − 크로스페이드 2 = 12초)
air = lambda lo, hi: bandnoise(AMB, lo, hi)

# 생활(거주·의료·휴게): 조용한 환기 바람 + 아주 옅은 전원 험
x = 0.8 * air(120, 1100) * slow_mod(AMB, 1 / 6, 0.25) + 0.05 * hum(AMB, 60, [1, 0.4, 0.15])
save("Interior/amb_room_life_loop.wav", loop_xfade(onepole_lp(x, 2500), 2.0), -26)

# 물·식물(농장·물 재활용·산소): 환기 + 흐르는 물 + 가끔 올라오는 기포 "뽁"
x = 0.5 * air(150, 900) + 0.35 * air(500, 3200) * slow_mod(AMB, 1 / 3.5, 0.5)
for i in range(46):
    at = rng.uniform(0.0, AMB - 0.2)
    dd = rng.uniform(0.04, 0.09)
    b = sweep_sine(dd, rng.uniform(280, 420), rng.uniform(700, 1100), dd * 0.6) * env_exp(dd, dd * 0.35, attack=0.004)
    place(x, rng.uniform(0.08, 0.2) * b, at)
save("Interior/amb_room_water_loop.wav", loop_xfade(onepole_lp(x, 4500), 2.0), -24)

# 기계(창고·정비·화물·채굴·연구): 모터 험 + 장비 팬 + 일정한 간격의 서보 "칙·틱"
x = 0.18 * hum(AMB, 92, [1, 0.6, 0.35, 0.2, 0.1], jitter=0.3) + 0.5 * air(200, 2400) * slow_mod(AMB, 1 / 7, 0.2)
for i in range(int(AMB / 1.75)):
    at = 0.3 + i * 1.75 + rng.uniform(-0.05, 0.05)
    c = modal(0.12, [1800, 2900, 4100], [0.02, 0.012, 0.008], [0.4, 0.25, 0.12]) + 0.3 * bandnoise(0.12, 1500, 6000) * env_exp(0.12, 0.01)
    place(x, 0.22 * c, at)
save("Interior/amb_room_machine_loop.wav", loop_xfade(onepole_lp(x, 6000), 2.0), -24)

# 고열·고출력(제련·핵융합·연료전지): 깊은 우웅 + 부풀었다 가라앉는 화염 굉음 + 드문 탁탁 튀는 소리
x = 1.0 * air(28, 90) * slow_mod(AMB, 1 / 7, 0.35) + 0.45 * air(70, 380) * slow_mod(AMB, 1 / 3.5, 0.55, 1.3) + 0.08 * hum(AMB, 48, [1, 0.5])
for i in range(30):
    at = rng.uniform(0.0, AMB - 0.05)
    place(x, rng.uniform(0.05, 0.14) * bandnoise(0.02, 1500, 6000) * env_exp(0.02, 0.004), at)
save("Interior/amb_room_heat_loop.wav", loop_xfade(onepole_lp(x, 3500), 2.0), -22)

# 전기·방어(배터리·태양광·실드·포탑·장갑 격벽·손상 통제): 변압기 120Hz 웅 + 살짝 맥놀이 + 옅은 고음 + 드문 지직
x = 0.3 * hum(AMB, 120, [1, 0.55, 0.3, 0.18, 0.1, 0.06]) + 0.12 * hum(AMB, 120.6, [1, 0.4]) + 0.3 * air(150, 1200)
x += 0.02 * np.sin(2 * np.pi * 3150 * t_axis(AMB)) * slow_mod(AMB, 1 / 4.7, 0.6)
for i in range(9):
    at = rng.uniform(0.0, AMB - 0.2)
    zap = bandnoise(0.09, 2000, 8000) * (rng.random(int(0.09 * SR)) > 0.7) * env_exp(0.09, 0.03)
    place(x, 0.1 * zap, at)
save("Interior/amb_room_electric_loop.wav", loop_xfade(onepole_lp(x, 7000), 2.0), -24)

# 넓은 공간(코어·회전 링·연결 튜브): 낮게 흐르는 공기 + 멀리서 울리는 금속 삐걱 (울림 길게)
x = 0.9 * air(50, 420) * slow_mod(AMB, 1 / 7, 0.4)
for i in range(4):
    at = 1.0 + i * 3.3 + rng.uniform(-0.4, 0.4)
    cr = modal(1.6, [rng.uniform(140, 210), rng.uniform(330, 470), rng.uniform(700, 900)], [0.6, 0.4, 0.25], [0.5, 0.3, 0.15], attack=0.05)
    place(x, 0.06 * cr, at)
x = reverb(x, 0.25, 0.9, 1.6, 2500)[: int(AMB * SR)]
save("Interior/amb_room_hall_loop.wav", loop_xfade(onepole_lp(x, 3000), 2.0), -26)

# ================= 11-10 내부 상태 연출 (합성) =================

rng = np.random.default_rng(11)  # 섹션별 난수 (11-9와 같은 이유)

# 파손 방 경보음: 두 음 번갈아 울리는 클랙슨 "뿌-빠-" (640 / 480 Hz, 0.6초씩). 주기 1.2초 × 2 + 크로스페이드 0.3초 → 2.4초 루프
PERIOD, TONE, XF = 1.2, 0.6, 0.3
n = int((PERIOD * 2 + XF) * SR)
t = np.arange(n) / SR
phase_in = np.mod(t, PERIOD)
f = np.where(phase_in < TONE, 640.0, 480.0)
ph = 2 * np.pi * np.cumsum(f) / SR
tone = np.sin(ph) + 0.35 * np.sin(3 * ph) + 0.15 * np.sin(5 * ph)
local = np.mod(t, TONE)
env = np.minimum(1.0, local / 0.02) * np.minimum(1.0, (TONE - local) / 0.03)  # 음마다 짧은 상승·하강 (딸깍 방지)
x = onepole_lp(tone * env, 3500)
x = reverb(x, 0.18, 0.25, 0.6, 3000)[:n]
save("Interior/interior_alarm_loop.wav", loop_xfade(x, XF), -20)

print("\n".join(report))
open(os.path.join(HERE, "build_report.txt"), "w", encoding="utf-8").write("\n".join(report))
