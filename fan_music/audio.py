"""多电机声学仿真: 把 Plan 渲染成立体声 WAV。

不是录音, 是一个物理模型:

  * 每个电机有自己的叶片数、转速上下限和一阶惯性时间常数, 转速由饱和控制律驱动
    (加速给满、减速给最低、到位回稳态), 所以每个音是准的, 音头有一段可听见的上滑;
  * 叶片通过频率给出 f_bp 的谐波列, 选中的第 k 阶被加权成主音;
  * 气流噪声是带通白噪声, 音量随转速 1.7 次方增长, 每台电机噪声互不相关;
  * 每台电机有独立摆位(pan + 微延时), 所以能听出"几只风扇在不同位置";
  * 最后过一遍共用的房间混响 —— 它们毕竟在同一个机箱里。
"""

from __future__ import annotations

import math
import wave

import numpy as np

DEFAULT_FS = 48000
HARMONICS = 7


def speed_curve(plan, score, fs, voice_index=0):
    """按饱和控制律解析地算出某台电机的转速曲线 (逐段闭式解, 不逐样本递推)。"""
    voice = score.voices[voice_index]
    n = int(round(plan.duration * fs))
    rpm = np.full(n, float(voice.min_rpm), dtype=np.float64)
    events = plan.by_voice().get(voice_index, [])
    if n <= 0 or not events:
        return rpm

    tau = max(float(voice.glide), 1e-4)
    bounds = [(ev.cmd_t0, float(ev.rpm)) for ev in events]
    v = float(voice.min_rpm)

    for i, (t_start, target) in enumerate(bounds):
        i0 = max(0, int(round(t_start * fs)))
        i1 = int(round(bounds[i + 1][0] * fs)) if i + 1 < len(bounds) else n
        i1 = min(n, max(i1, i0))
        if i1 <= i0:
            continue
        m = i1 - i0
        if abs(target - v) < 1e-6:
            rpm[i0:i1] = target
            continue
        asym = float(voice.max_rpm) if target > v else float(voice.min_rpm)
        tt = np.arange(m, dtype=np.float64) / fs
        curve = asym + (v - asym) * np.exp(-tt / tau)
        denom = asym - v
        ratio = (asym - target) / denom if abs(denom) > 1e-9 else 1.0
        if 0.0 < ratio < 1.0:
            t_hit = -tau * math.log(ratio)
            hit = min(m, max(0, int(math.ceil(t_hit * fs))))
            curve[hit:] = target
        else:
            curve[:] = target
        rpm[i0:i1] = curve
        v = float(curve[-1])

    return rpm


def _shape_noise(rng, n, fs, center=420.0):
    """带通 + 粉红倾斜的气流噪声。"""
    white = rng.standard_normal(n)
    spec = np.fft.rfft(white)
    freqs = np.fft.rfftfreq(n, 1.0 / fs)
    safe = np.maximum(freqs, 1.0)
    bump = np.exp(-0.5 * (np.log(safe / center) / 0.85) ** 2)
    tilt = 1.0 / (1.0 + (safe / 1800.0) ** 1.4)
    shape = 0.55 * bump + 0.35 * tilt + 0.03
    out = np.fft.irfft(spec * shape, n=n)
    peak = float(np.max(np.abs(out))) or 1.0
    return out / peak


def render_voice(plan, score, fs, voice_index, rng, noise=0.85):
    """渲染单台电机(单声道)。"""
    voice = score.voices[voice_index]
    n = int(round(plan.duration * fs))
    sig = np.zeros(n, dtype=np.float64)
    if n <= 0:
        return sig

    rpm = speed_curve(plan, score, fs, voice_index)
    f_bp = rpm * voice.blades / 60.0
    phase = 2.0 * np.pi * np.cumsum(f_bp) / fs
    vel = np.clip(rpm / voice.max_rpm, 0.0, 1.0) ** 1.25

    base = np.array([0.0] + [0.30 / (m ** float(voice.timbre))
                             for m in range(1, HARMONICS + 1)])
    for ev in plan.by_voice().get(voice_index, []):
        if ev.freq is None:
            continue
        i0 = max(0, int(round(ev.t0 * fs)))
        i1 = min(n, int(round(ev.t1 * fs)))
        if i1 <= i0:
            continue
        w = base.copy()
        if 1 <= ev.harmonic <= HARMONICS:
            w[ev.harmonic] += 1.0      # 目标那一阶当主音
        seg_phase = phase[i0:i1]
        acc = np.zeros(i1 - i0, dtype=np.float64)
        for m in range(1, HARMONICS + 1):
            if w[m] > 0.0:
                acc += w[m] * np.sin(m * seg_phase)
        sig[i0:i1] += vel[i0:i1] * acc

    air = _shape_noise(rng, n, fs, center=float(voice.noise_center))
    air_gain = np.clip(rpm / voice.max_rpm, 0.0, 1.0) ** 1.7
    sig += noise * 0.35 * float(voice.noise_gain) * air_gain * air
    return sig


def render(plan, fs=DEFAULT_FS, noise=0.85, reverb=0.14, seed=7):
    """返回 (立体声 float64 数组 (2, n), 采样率)。"""
    score = plan.score
    n = int(round(plan.duration * fs))
    if n <= 0:
        return np.zeros((2, 0)), fs

    mix = np.zeros((2, n), dtype=np.float64)
    rng = np.random.default_rng(seed)

    for vi, voice in enumerate(score.voices):
        sig = render_voice(plan, score, fs, vi, rng, noise=noise)
        theta = (float(voice.pan) + 1.0) * np.pi / 4.0
        delay = int(round(max(0.0, float(voice.delay_ms)) * 1e-3 * fs))
        if 0 < delay < n:
            sig = np.concatenate((np.zeros(delay), sig[: n - delay]))
        gain = float(voice.gain)
        mix[0] += gain * math.cos(theta) * sig
        mix[1] += gain * math.sin(theta) * sig

    if reverb > 0.0:
        ir_len = int(0.65 * fs)
        ir_t = np.arange(ir_len, dtype=np.float64) / fs
        ir = rng.standard_normal(ir_len) * np.exp(-ir_t / 0.18)
        ir /= float(np.max(np.abs(ir))) or 1.0
        nfft = 1 << int(math.ceil(math.log2(n + ir_len)))
        H = np.fft.rfft(ir, nfft)
        wet = np.empty_like(mix)
        for ch in range(2):
            wet[ch] = np.fft.irfft(np.fft.rfft(mix[ch], nfft) * H, nfft)[:n]
        wet_rms = float(np.sqrt(np.mean(wet ** 2))) or 1.0
        sig_rms = float(np.sqrt(np.mean(mix ** 2))) or 1.0
        mix = (1.0 - reverb) * mix + reverb * wet * (sig_rms / wet_rms)

    peak = float(np.max(np.abs(mix)))
    if peak > 0:
        mix = mix / peak * 0.89
    return mix, fs


def write_wav(path, sig, fs=DEFAULT_FS):
    """sig 可以是 (n,) 单声道, 或 (2, n) / (n, 2) 立体声。"""
    arr = np.asarray(sig, dtype=np.float64)
    if arr.ndim == 1:
        arr = arr[np.newaxis, :]
    elif arr.shape[0] > arr.shape[1]:
        arr = arr.T
    pcm = np.clip(arr, -1.0, 1.0)
    data = (pcm.T * 32767.0).astype("<i2")
    with wave.open(str(path), "wb") as w:
        w.setnchannels(arr.shape[0])
        w.setsampwidth(2)
        w.setframerate(int(fs))
        w.writeframes(data.tobytes())
    return str(path)


def verify(plan, sig, fs=DEFAULT_FS, tol_ratio=0.04, min_prominence=2.5):
    """检查每个音在混音里到底有没有出现、偏高偏低多少。

    多声部混在一起时, 全局 FFT 峰值往往是别的声部, 所以这里不做"全局找峰",
    而是在目标频率 ±tol_ratio 的窄带里找峰, 并算它的突出度(峰 / 该带中位数)。
    突出度太低说明这个音被盖住了或者根本没唱出来。

    返回 (rows, worst_cents, detected, total)。
    rows: [(声部, 音名, 目标Hz, 实测Hz, 音分, 突出度)]
    """
    if isinstance(sig, np.ndarray) and sig.ndim == 2:
        sig = sig.mean(axis=0)
    rows = []
    worst = 0.0
    detected = 0
    total = 0
    if sig.size == 0:
        return rows, worst, detected, total

    for evs in plan.by_voice().values():
        for i, ev in enumerate(evs):
            if ev.freq is None or ev.duration < 0.05:
                continue
            nxt = evs[i + 1].cmd_t0 if i + 1 < len(evs) else ev.t1
            steady_end = min(ev.t1, nxt)
            i0 = int((ev.t0 + 0.15 * ev.duration) * fs)
            i1 = int(min(ev.t1 - 0.05 * ev.duration, steady_end) * fs)
            if i1 - i0 < 256:
                continue
            i0 = max(0, min(i0, sig.size - 64))
            i1 = max(i0 + 64, min(i1, sig.size))
            total += 1

            seg = sig[i0:i1] * np.hanning(i1 - i0)
            nfft = 1 << int(math.ceil(math.log2(max((i1 - i0) * 8, 8192))))
            spec = np.abs(np.fft.rfft(seg, nfft))
            freqs = np.fft.rfftfreq(nfft, 1.0 / fs)

            lo_hz = ev.freq * (1.0 - tol_ratio)
            hi_hz = ev.freq * (1.0 + tol_ratio)
            lo = int(np.searchsorted(freqs, lo_hz))
            hi = int(np.searchsorted(freqs, hi_hz))
            if hi <= lo + 1:
                continue
            band = spec[lo:hi]
            j = int(np.argmax(band)) + lo
            prominence = float(band.max()) / (float(np.median(band)) + 1e-18)

            peak_bin = float(j)
            if 0 < j < spec.size - 1:
                y0, y1, y2 = spec[j - 1], spec[j], spec[j + 1]
                denom = y0 - 2.0 * y1 + y2
                # 只有真的是个局部极大(denom < 0)才插值, 并且修正量限制在半个 bin 内,
                # 否则在近乎平坦的谱上会插出天文数字
                if denom < -1e-12:
                    delta = 0.5 * (y0 - y2) / denom
                    peak_bin = j + max(-0.5, min(0.5, delta))
            peak_hz = peak_bin * fs / nfft
            cents = 1200.0 * math.log2(peak_hz / ev.freq) if peak_hz > 0 else 0.0

            if prominence >= min_prominence:
                detected += 1
                if abs(cents) > abs(worst):
                    worst = cents
            rows.append((ev.voice, ev.label, ev.freq, peak_hz, cents, prominence))

    return rows, worst, detected, total
