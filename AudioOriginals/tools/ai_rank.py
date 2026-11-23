"""
게임 소리 전체를 AI 청각 모델 (PANNs Cnn14, AudioSet 527 범주)로 "무엇으로 들리는지" 줄 세우기 (2026-11-24, 실행 환경은 ref_compare.py 머리).
한 번 소리는 2.5초 (길면 길이 + 0.5초)마다 네 번, 루프는 이어 붙여 10초로 만들어 잼 (1 ~ 3초 그대로는 점수가 흔들림).
artificial = 순음 · 삐 · 전자음 · 신시사이저 · 잡음 · 웅 · 지직 범주 점수 합 — 높을수록 실제 물건 소리보다 "기계로 만든 소리"로 들림.
실행: <ai env python> ai_rank.py <Audio 폴더> <출력 csv>
"""
import sys, os, glob, numpy as np, soundfile as sf, librosa
from panns_inference import AudioTagging, labels

ART = ["Sine wave", "Beep, bleep", "Electronic music", "Synthesizer", "Static", "White noise", "Pink noise", "Noise",
       "Hum", "Mains hum", "Buzz", "Electronic tuner", "Tone", "Chirp tone", "Theremin", "Distortion"]
ai = [labels.index(a) for a in ART if a in labels]
at = AudioTagging(checkpoint_path=None, device="cpu")
SR = 32000
rows = []
for f in sorted(glob.glob(os.path.join(sys.argv[1], "**", "*.wav"), recursive=True)):
    x, sr = sf.read(f, always_2d=True); x = x.mean(1)
    x = librosa.resample(x, orig_sr=sr, target_sr=SR) if sr != SR else x
    if len(x) < SR * 0.05:
        continue
    n = 10 * SR
    if f.endswith("_loop.wav"):
        y = np.tile(x, int(np.ceil(n / len(x))))[:n]
    else:
        y = np.zeros(n); step = max(2.5, len(x) / SR + 0.5)
        for k in range(4):
            i = int(k * step * SR)
            if i >= n:
                break
            m = min(len(x), n - i); y[i:i + m] += x[:m]
    s = at.inference(y.astype(np.float32)[None, :])[0][0]
    top = np.argsort(s)[::-1][:4]
    art = float(s[ai].sum())
    artt = ", ".join(f"{labels[i]} {s[i]:.2f}" for i in sorted(ai, key=lambda i: -s[i])[:2])
    rows.append((os.path.relpath(f, sys.argv[1]).replace("\\", "/"), art, " | ".join(f"{labels[i]} {s[i]:.2f}" for i in top), artt))
rows.sort(key=lambda r: -r[1])
with open(sys.argv[2], "w", encoding="utf-8") as o:
    o.write("file\tartificial\ttop4\tartificial_top\n")
    for r in rows:
        o.write(f"{r[0]}\t{r[1]:.2f}\t{r[2]}\t{r[3]}\n")
print(len(rows), "files")
