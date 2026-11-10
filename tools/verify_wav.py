"""对已经渲染好的 WAV 复验音准, 省得为了自检再渲一遍。

用法:
    python tools/verify_wav.py scores/bad_apple_6motors.fscore out/bad_apple_6motors.wav
"""

import sys
import wave

import numpy as np

from fan_music import audio, engine, score as S


def load(path):
    with wave.open(path) as w:
        n, fs, ch = w.getnframes(), w.getframerate(), w.getnchannels()
        raw = np.frombuffer(w.readframes(n), dtype="<i2").astype(np.float64) / 32768.0
    if ch > 1:
        raw = raw.reshape(-1, ch).mean(axis=1)
    return raw, fs


def main(score_path, wav_path):
    sc = S.parse(score_path)
    p = engine.plan(sc)
    sig, fs = load(wav_path)
    rows, worst, detected, total = audio.verify(p, sig, fs)

    print(f"谱子: {score_path}   {len(sc.voices)} 台电机")
    print(f"WAV : {wav_path}   {sig.size / fs:.2f}s @ {fs}Hz")
    print(f"准点率 {p.settle_ratio * 100:.0f}%   检出 {detected}/{total} 个音")
    if rows:
        print(f"检出的音里最差偏差 {worst:+.1f} 音分")
        off = [r for r in rows if r[-1] >= 2.5 and abs(r[-2]) > 60]
        print(f"偏差超过 60 音分的: {len(off)} 个")
        for r in off[:5]:
            print(f"   {r[0]:<8} {r[1]:<4} 期望 {r[2]:.1f}Hz 实测 {r[3]:.1f}Hz {r[4]:+.1f}音分")
    print()
    print("  按声部分组的检出率:")
    for vi, voice in enumerate(sc.voices):
        mine = [r for r in rows if r[0] == voice.label]
        good = [r for r in mine if r[-1] >= 2.5]
        if not mine:
            continue
        print(f"   {voice.label:<8} {voice.blades}叶 {voice.min_rpm:>5.0f}-{voice.max_rpm:<5.0f}rpm  "
              f"{len(good):>4}/{len(mine):<4} 检出 ({len(good) / len(mine) * 100:>3.0f}%)")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        raise SystemExit(1)
    main(sys.argv[1], sys.argv[2])
