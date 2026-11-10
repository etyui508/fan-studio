"""把 MIDI 改编成若干台风扇能唱的谱子。

为什么需要改编, 而不是直接照搬:

* 原曲一秒十几到二十个音(16 分音符以上), 而一台风扇换速要 0.05~0.15 秒,
  一秒最多唱 3~4 个音。照搬只会得到一片糊。
* 原曲的音域有 6 个八度, 而一台风扇的可用音域只有 2 个八度左右。

做法:

1. **分层抽取**。同一时刻可能有十几个音, 每台风扇只能唱一个, 于是:
   melody 取"全体最高音"(旋律通常在最上面), harmony 取"中高层的最高音"(第二声部),
   alto 取"中低层的最低音", bass 取"全体最低音"。
   每层的音高范围互相重叠, 但选择规则不同, 所以四个声部不会撞车。
2. **整体移八度**进入各自电机的可用音域。移八度不破坏和声。
3. **重新采样**。以 step 秒为周期取一个音, 相邻同音合并成长音。
   本质是把原曲当成 1/step Hz 采样率的采样器来采, 得到连续的音高轮廓。
   风扇天生连奏, 这样反而好听。
"""

from __future__ import annotations

import math

from . import motors
from .score import Score, Voice, midi_to_name


def playable_range(preset):
    """这个电机预设能唱出的 MIDI 音高区间 (含端点, 浮点)。"""
    k = preset.get("harmonic") or 1
    lo_hz = preset["min_rpm"] * preset["blades"] / 60.0 * k
    hi_hz = preset["max_rpm"] * preset["blades"] / 60.0 * k
    return (69 + 12 * math.log2(lo_hz / 440.0), 69 + 12 * math.log2(hi_hz / 440.0))


def pick_track(mf):
    """挑出承载旋律的那条轨(非鼓、音符最多)。"""
    best = None
    for t in mf.tracks:
        if t.is_drum or not t.notes:
            continue
        if best is None or len(t.notes) > len(best.notes):
            best = t
    if best is None:
        raise ValueError("这个 MIDI 里没有非鼓的音符")
    return best


def duplicate_groups(mf):
    """找出内容完全一样的轨(常见于把整份谱子复制到多个音色)。"""
    sigs = {}
    for t in mf.tracks:
        if not t.notes:
            continue
        sig = tuple((n.pitch, n.start_tick, n.end_tick) for n in t.notes[:200])
        sigs.setdefault(sig, []).append(t.index)
    return [v for v in sigs.values() if len(v) > 1]


def quantile(values, q):
    if not values:
        return 0
    ordered = sorted(values)
    idx = min(len(ordered) - 1, max(0, int(round(q * (len(ordered) - 1)))))
    return ordered[idx]


def stratum_notes(notes, lo_q, hi_q):
    """取音高落在 [lo_q, hi_q] 分位区间内的音符。"""
    pitches = [n.pitch for n in notes]
    lo, hi = quantile(pitches, lo_q), quantile(pitches, hi_q)
    return [n for n in notes if lo <= n.pitch <= hi]


def extract_contour(notes, start, end, step, pick="top"):
    """以 step 秒为周期采样, 每个采样点取当时该层里最高/最低的那个音。

    返回 [(时刻, 音高或 None, 时长)], 相邻同音已合并。
    """
    if not notes or step <= 0:
        return []
    choose = max if pick == "top" else min
    slots = []
    t = start
    while t < end - 1e-9:
        s0, s1 = t, min(t + step, end)
        starts = [n for n in notes if s0 - 1e-6 <= n.start < s1]
        if not starts:
            starts = [n for n in notes if n.start < s1 and n.end > s0]
        if not starts:
            slots.append((t, None, s1 - t))
        else:
            slots.append((t, choose(starts, key=lambda n: n.pitch).pitch, s1 - t))
        t = s1

    merged = []
    for t, pitch, dur in slots:
        if merged and merged[-1][1] == pitch and abs(merged[-1][0] + merged[-1][2] - t) < 1e-9:
            prev = merged[-1]
            merged[-1] = (prev[0], prev[1], prev[2] + dur)
        else:
            merged.append((t, pitch, dur))
    return merged


def fold_contour(contour, lo_target, hi_target):
    """整体移若干个八度, 让轮廓落进电机能唱的区间。返回 (新轮廓, 移调半音数)。"""
    pitches = [p for _, p, _ in contour if p is not None]
    if not pitches:
        return contour, 0
    lo, hi = min(pitches), max(pitches)
    center = (lo + hi) / 2.0
    target_center = (lo_target + hi_target) / 2.0
    width = hi_target - lo_target

    shift_min = math.ceil(lo_target - lo)
    shift_max = math.floor(hi_target - hi)
    if shift_min <= shift_max:
        ideal = target_center - center
        options = [s for s in range(shift_min, shift_max + 1) if s % 12 == 0]
        shift = min(options, key=lambda s: abs(s - ideal)) if options else int(round(ideal / 12.0)) * 12
    else:
        # 轮廓比电机音域还宽: 让中心对齐, 边缘靠 clamp 兜住
        shift = int(round((target_center - center) / 12.0)) * 12
        del width
    return [(t, None if p is None else p + shift, dur) for t, p, dur in contour], shift


def arrange(mf, *, start=None, duration=None, voices=4, step=None, bpm=120.0, track=None,
            keys=None):
    """把 MIDI 改编成 Score。返回 (Score, 统计信息)。"""
    source_track = mf.tracks[track] if track is not None else pick_track(mf)
    all_notes = source_track.notes
    if not all_notes:
        raise ValueError("选中的轨道里没有音符")

    if duration is None:
        duration = min(45.0, mf.length)
    if start is None:
        # 跳过前奏: 从第一个"密度达到峰值 35%"的位置开始
        probe = 2.0
        counts = []
        t = 0.0
        while t < mf.length:
            counts.append((t, sum(1 for n in all_notes if t <= n.start < t + probe)))
            t += probe
        start = 0.0
        if counts:
            peak = max(c for _, c in counts) or 1
            for t, c in counts:
                if c >= peak * 0.35:
                    start = t
                    break
    end = min(start + duration, mf.length)
    window = [n for n in all_notes if start <= n.start < end]
    if not window:
        raise ValueError(f"{start:.1f}s-{end:.1f}s 这一段没有音符")

    presets = motors.ensemble(voices, keys=keys)
    beat = 60.0 / bpm
    score_voices = []
    stats = {"source_track": source_track.name or f"track {source_track.index}",
             "window": (start, end), "bands": []}

    for preset in presets:
        lo_target, hi_target = playable_range(preset)
        layer = stratum_notes(window, *preset["stratum"])
        contour = extract_contour(layer, start, end,
                                  step or preset["step"], pick=preset["pick"])
        contour, shift = fold_contour(contour, lo_target, hi_target)

        lo_i = int(round(lo_target))
        hi_i = int(round(hi_target))
        notes = []
        for _, pitch, dur in contour:
            beats = max(round(dur / beat / 0.05) * 0.05, 0.05)
            if pitch is None:
                notes.append((None, beats))
            else:
                notes.append((min(max(pitch, lo_i), hi_i), beats))
        if not any(p is not None for p, _ in notes):
            continue

        score_voices.append(Voice(
            label=preset["label"],
            blades=preset["blades"],
            harmonic=preset["harmonic"],
            glide=preset["glide"],
            min_rpm=preset["min_rpm"],
            max_rpm=preset["max_rpm"],
            start_duty=preset["start_duty"],
            pan=preset["pan"],
            delay_ms=preset["delay_ms"],
            gain=preset["gain"],
            noise_center=preset["noise_center"],
            timbre=preset["timbre"],
            noise_gain=preset["noise_gain"],
            notes=notes,
        ))
        sounded = [p for p, _ in notes if p is not None]
        stats["bands"].append({
            "label": preset["label"],
            "model": preset["model"],
            "rule": preset["role"],
            "shift": shift / 12.0,
            "notes": len(sounded),
            "range": (midi_to_name(min(sounded)), midi_to_name(max(sounded))),
            "step": step or preset["step"],
        })

    return Score(bpm=bpm, voices=score_voices), stats


def to_fscore(score, title="", comment_lines=()):
    """把 Score 序列化成 .fscore 文本。"""
    lines = []
    if title:
        lines.append(f"# {title}")
    for c in comment_lines:
        lines.append(f"# {c}")
    lines.append("")
    lines.append(f"bpm = {score.bpm:g}")
    lines.append("")
    for v in score.voices:
        lines.append(f"[voice {v.label}]")
        lines.append(f"blades = {v.blades}")
        lines.append(f"harmonic = {'auto' if v.harmonic is None else v.harmonic}")
        lines.append(f"glide = {v.glide:g}")
        lines.append(f"min_rpm = {v.min_rpm:g}")
        lines.append(f"max_rpm = {v.max_rpm:g}")
        lines.append(f"start_duty = {v.start_duty:g}")
        lines.append(f"pan = {v.pan:g}")
        lines.append(f"delay_ms = {v.delay_ms:g}")
        lines.append(f"gain = {v.gain:g}")
        lines.append(f"noise_center = {v.noise_center:g}")
        lines.append(f"timbre = {v.timbre:g}")
        lines.append(f"noise_gain = {v.noise_gain:g}")
        lines.append("")
        tokens = []
        for pitch, beats in v.notes:
            name = "r" if pitch is None else midi_to_name(pitch)
            tokens.append(f"{name}:{beats:g}")
        for i in range(0, len(tokens), 8):
            lines.append(" ".join(tokens[i:i + 8]))
        lines.append("")
    return "\n".join(lines) + "\n"
