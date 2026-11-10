"""把乐谱翻译成每台风扇能执行的动作: 音 -> (谐波 k, 叶片通过频率, 转速, 占空比)。

一台风扇 = 一个声部 = 一个电机。每个电机的参数(叶片数、转速范围、惯性)各自独立,
时间轴共享同一条拍子线, 于是它们能合奏。

两个物理事实决定了算法:

1. 风扇是惯性负载。转速对占空比是一阶惯性环节, 时间常数 glide (0.08~0.6s)。
2. 占空比有上下界, 所以换速存在**时间最优**解: 往高转先把占空比给满冲到目标转速,
   再回到稳态值; 往低转就给到刚好不停转的最小值。饱和控制比干等惯性逼近快一倍。
   换速耗时可用 transition_time 解析算出, 于是我们把换速安排成"刚好在起拍时结束"。
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

from .score import midi_to_freq, midi_to_name

LEAD_IN = 0.25      # 每个声部开唱前的空转时间(秒)
TAIL = 0.9          # 结束后留给余音/降速的时间
ANTICIPATE = 1.0    # 提前量系数: 1.0 = 换速刚好在起拍结束, 0 = 不提前


def blade_freq(rpm, blades):
    """叶片通过频率 (Hz): f_bp = RPM/60 * 叶片数。"""
    return rpm * blades / 60.0


def rpm_for_blade(f_bp, blades):
    return f_bp * 60.0 / blades


def transition_time(prev_rpm, target_rpm, voice):
    """饱和控制下从 prev_rpm 冲到 target_rpm 需要多久(秒)。

    加速时占空比给满 -> 转速指数逼近 max_rpm; 减速时给到最小 -> 逼近 min_rpm。
    解 asym + (v - asym)·e^(-t/τ) = target 即可。
    """
    if abs(target_rpm - prev_rpm) < 1e-6:
        return 0.0
    tau = max(float(voice.glide), 1e-4)
    asym = float(voice.max_rpm) if target_rpm > prev_rpm else float(voice.min_rpm)
    denom = asym - prev_rpm
    if abs(denom) < 1e-9:
        return 0.0
    ratio = (asym - target_rpm) / denom
    if not 0.0 < ratio < 1.0:
        return 0.0
    return -tau * math.log(ratio)


@dataclass
class NoteEvent:
    voice: str
    voice_index: int
    index: int
    label: str
    midi: int | None
    beats: float
    t0: float
    t1: float
    freq: float | None
    harmonic: int
    f_bp: float
    rpm: float
    duty: float
    warning: str = ""
    detail: str = ""
    cmd_t0: float = 0.0      # 换速指令时刻(早于 t0, 好让风扇准点到位)

    @property
    def duration(self):
        return self.t1 - self.t0

    @property
    def head_start(self):
        """这个音抢到的提前量(秒)。"""
        return max(0.0, self.t0 - self.cmd_t0)


@dataclass
class Plan:
    score: object
    events: list = field(default_factory=list)
    warnings: list = field(default_factory=list)
    lead_in: float = LEAD_IN

    @property
    def duration(self):
        if not self.events:
            return self.lead_in
        return max(ev.t1 for ev in self.events) + TAIL

    def by_voice(self):
        buckets = {}
        for ev in self.events:
            buckets.setdefault(ev.voice_index, []).append(ev)
        return buckets

    @property
    def settle_ratio(self):
        """真正落在正确音高上的时间占全部音符时长的比例。"""
        total = 0.0
        good = 0.0
        for evs in self.by_voice().values():
            for i, ev in enumerate(evs):
                total += ev.duration
                nxt = evs[i + 1].cmd_t0 if i + 1 < len(evs) else ev.t1
                good += max(0.0, min(ev.t1, nxt) - ev.t0)
        return good / total if total else 0.0


def fit_harmonic(freq, voice, kmax=8):
    """给目标音高挑谐波次数 k, 使 f_bp = freq/k 落在电机的转速区间里。

    auto 模式取最小的 k (基频最强), 所以越高的音会自动滑向越高的谐波。
    """
    fmin = blade_freq(voice.min_rpm, voice.blades)
    fmax = blade_freq(voice.max_rpm, voice.blades)

    if voice.harmonic:
        k = int(voice.harmonic)
        f_bp = freq / k
        if f_bp > fmax:
            return k, fmax, f"需要 {f_bp:.1f}Hz 超过电机上限, 已压到 {fmax:.1f}Hz (偏低)"
        if f_bp < fmin:
            return k, fmin, f"需要 {f_bp:.1f}Hz 低于电机下限, 已抬到 {fmin:.1f}Hz (偏高)"
        return k, f_bp, ""

    k = max(1, math.ceil(freq / fmax - 1e-9))
    f_bp = freq / k
    if f_bp < fmin:
        return k, fmin, f"{freq:.0f}Hz 基频低于电机下限, 已抬到 {fmin:.1f}Hz (偏高)"
    if k > kmax:
        return k, fmax, f"{freq:.0f}Hz 太高, 已压到 {fmax:.1f}Hz"
    return k, f_bp, ""


def duty_for(rpm, voice):
    """转速 -> 稳态 PWM 占空比 (0-1)。两点标定: start_duty->min_rpm, 1.0->max_rpm。"""
    span = max(voice.max_rpm - voice.min_rpm, 1e-9)
    frac = min(1.0, max(0.0, (rpm - voice.min_rpm) / span))
    return voice.start_duty + frac * (1.0 - voice.start_duty)


def plan(score, anticipate=ANTICIPATE):
    factor = max(0.0, float(anticipate))
    events = []
    warnings = []

    for vi, voice in enumerate(score.voices):
        beat = score.beat_seconds
        t = LEAD_IN
        voice_events = []

        for i, (midi, beats) in enumerate(voice.notes):
            dur = beats * beat
            if midi is None:
                ev = NoteEvent(
                    voice=voice.label,
                    voice_index=vi,
                    index=i,
                    label="r",
                    midi=None,
                    beats=beats,
                    t0=t,
                    t1=t + dur,
                    freq=None,
                    harmonic=0,
                    f_bp=blade_freq(voice.min_rpm, voice.blades),
                    rpm=float(voice.min_rpm),
                    duty=float(voice.start_duty),
                )
            else:
                freq = midi_to_freq(midi, voice.detune)
                k, f_bp, warning = fit_harmonic(freq, voice)
                rpm = min(voice.max_rpm, max(voice.min_rpm, rpm_for_blade(f_bp, voice.blades)))
                ev = NoteEvent(
                    voice=voice.label,
                    voice_index=vi,
                    index=i,
                    label=midi_to_name(midi),
                    midi=midi,
                    beats=beats,
                    t0=t,
                    t1=t + dur,
                    freq=freq,
                    harmonic=k,
                    f_bp=f_bp,
                    rpm=rpm,
                    duty=duty_for(rpm, voice),
                    warning=warning,
                    detail=f"根音 {midi_to_name(midi - round(12 * math.log2(k)))}",
                )
                if warning:
                    warnings.append(f"[{voice.label}] 第 {i + 1} 音 {ev.label}: {warning}")
            voice_events.append(ev)
            t += dur

        # 抢拍: 让换速刚好在起拍时结束
        prev_rpm = float(voice.min_rpm)
        for i, ev in enumerate(voice_events):
            lead = factor * transition_time(prev_rpm, ev.rpm, voice)
            floor = voice_events[i - 1].t0 + 0.05 if i else 0.0
            ev.cmd_t0 = max(floor, min(ev.t0, ev.t0 - lead))
            prev_rpm = ev.rpm

        events.extend(voice_events)

    events.sort(key=lambda e: (e.t0, e.voice_index))
    return Plan(score=score, events=events, warnings=warnings)


def duty_schedule(plan, score, voice_index=0):
    """某一台真实风扇用的时间表: [(时刻, 占空比)]。

    换速段直接给满(或给到最低), 到位后回稳态占空比 —— 和渲染器的模型一致,
    所以"听到的"和"真跑出来的"是同一个东西。
    """
    voice = score.voices[voice_index]
    events = plan.by_voice().get(voice_index, [])
    sched = []
    prev = float(voice.min_rpm)
    for ev in events:
        tgt = float(ev.rpm)
        if abs(tgt - prev) > 1e-6:
            sat = 1.0 if tgt > prev else float(voice.start_duty)
            sched.append((ev.cmd_t0, sat))
            hit = ev.cmd_t0 + transition_time(prev, tgt, voice)
            sched.append((min(hit, ev.t1), float(ev.duty)))
        else:
            sched.append((ev.cmd_t0, float(ev.duty)))
        prev = tgt
    return sched


def summary_rows(p):
    rows = []
    for ev in p.events:
        rows.append(
            (
                ev.voice,
                ev.index + 1,
                ev.label,
                ev.beats,
                ev.harmonic,
                ev.f_bp,
                ev.rpm,
                ev.duty * 100.0,
                ev.head_start * 1000.0,
                ev.warning,
            )
        )
    return rows
