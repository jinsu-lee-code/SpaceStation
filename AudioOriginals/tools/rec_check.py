"""
게임 안 녹음 검수 (작업 지침 7): ListenerTap으로 녹음한 WAV를 구간별로 나눠 소리 크기 · 튀는 정도(jump) · 지직거림(crackle) · 대역 비율을 출력.
실행: "<Blender>/5.2/python/bin/python.exe" -I AudioOriginals/tools/rec_check.py <녹음.wav> 0.5-3:이름 3-7:이름 ...
"""
import sys, wave, numpy as np
sys.argv += []
f = sys.argv[1]; segs = [tuple(map(float, s.split("-"))) + (n,) for s, n in (a.split(":") for a in sys.argv[2:])]
w = wave.open(f); sr = w.getframerate(); ch = w.getnchannels()
x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64).reshape(-1, ch).mean(1) / 32768
print(f"len={len(x)/sr:.1f}s sr={sr} peak={20*np.log10(np.abs(x).max()+1e-9):.1f}dB")
def stats(a, b, name):
    y = x[int(a*sr):int(b*sr)]
    if len(y) < 2048: return
    win = 2048; fr = np.array([y[i:i+win]*np.hanning(win) for i in range(0, len(y)-win, 512)])
    S = np.abs(np.fft.rfft(fr, axis=1))**2; fq = np.fft.rfftfreq(win, 1/sr); P = S.sum(0)
    rms = 20*np.log10(np.sqrt((y**2).mean())+1e-9); pk = 20*np.log10(np.abs(y).max()+1e-9)
    # 10 ms 소리 크기의 최대 - 중앙값 = 튀는 정도
    e = np.sqrt(np.convolve(y**2, np.ones(int(sr*0.01))/int(sr*0.01), mode="valid"))
    jump = 20*np.log10(e.max()/np.median(e)+1e-9)
    # 지지직 (짧은 튐): 1 ms 단위 고역(>3 kHz) 크기의 99.9% / 중앙값
    hp = y - np.convolve(y, np.ones(16)/16, mode="same")
    e1 = np.sqrt(np.convolve(hp**2, np.ones(int(sr*0.001))/int(sr*0.001), mode="valid"))
    crackle = 20*np.log10(np.percentile(e1, 99.9)/np.median(e1)+1e-9)
    band = lambda lo, hi: 10*np.log10(P[(fq>=lo)&(fq<hi)].sum()/P.sum()+1e-12)
    print(f"{name:14s} {a:4.1f}-{b:4.1f}s rms={rms:6.1f} peak={pk:6.1f} jump={jump:5.1f}dB crackle={crackle:5.1f}dB  <200={band(0,200):6.1f} 200-1k={band(200,1000):6.1f} 1-4k={band(1000,4000):6.1f} >4k={band(4000,24000):6.1f}")
for a, b, n in segs: stats(a, b, n)
