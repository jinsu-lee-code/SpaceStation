# 실행 환경은 ref_compare.py 머리 참고 (PANNs).
"""
AI 청각 모델 (PANNs Cnn14, AudioSet 527 범주)로 "무엇으로 들리는지" 점수.
실행: <ai env python> tag.py <기대 범주 쉼표 목록> <wav 여러 개 또는 폴더>
출력: 파일마다 위 5개 범주 + 기대 범주 점수 (0 ~ 1).
"""
import sys, os, glob, numpy as np, soundfile as sf, librosa
from panns_inference import AudioTagging, labels

want = [w.strip() for w in sys.argv[1].split(",") if w.strip()]
files = []
for a in sys.argv[2:]:
    files += sorted(glob.glob(os.path.join(a, "*.wav"))) if os.path.isdir(a) else [a]
at = AudioTagging(checkpoint_path=None, device="cpu")
wi = [labels.index(w) for w in want]
print(f"{'file':40s} " + " ".join(f"{w[:12]:>12s}" for w in want) + "   top5")
for f in files:
    x, sr = sf.read(f, always_2d=True); x = x.mean(1)
    x = librosa.resample(x, orig_sr=sr, target_sr=32000) if sr != 32000 else x
    if len(x) < 32000:
        x = np.pad(x, (0, 32000 - len(x)))
    clip, _ = at.inference(x[None, :].astype(np.float32))
    s = clip[0]
    top = np.argsort(s)[::-1][:5]
    print(f"{os.path.basename(f)[:40]:40s} " + " ".join(f"{s[i]:12.3f}" for i in wi) + "   " + ", ".join(f"{labels[i]} {s[i]:.2f}" for i in top))
