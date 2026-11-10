"""临时诊断: 看改编出来的谱子是否调性稳定、旋律是否级进。"""
import collections
import sys

from fan_music import score as S

NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def main(path):
    sc = S.parse(path)
    print(f"bpm = {sc.bpm:g}   声部数 = {len(sc.voices)}")
    for v in sc.voices:
        ps = [p for p, _ in v.notes if p is not None]
        if not ps:
            print(f"[{v.label}] 没有音符")
            continue
        pc = collections.Counter(p % 12 for p in ps)
        tot = sum(pc.values())
        top = pc.most_common(7)
        cover = sum(c for _, c in top) / tot * 100

        iv = collections.Counter()
        for a, b in zip(ps, ps[1:]):
            step = abs(b - a) % 12
            iv[step if step <= 6 else 12 - step] += 1
        movers = sum(iv.values())
        small = sum(c for k, c in iv.items() if k <= 2) / movers * 100 if movers else 0.0

        names = " ".join(f"{NAMES[k]}:{c}" for k, c in top)
        print(f"[{v.label:<8}] 音数 {tot:>3}  主要音级 {names}")
        print(f"           7 个音级覆盖 {cover:.0f}%, 相邻音差<=2 半音占 {small:.0f}%")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "scores/bad_apple.fscore")
