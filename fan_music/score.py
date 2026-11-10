"""`.fscore` 乐谱解析 (v2: 多电机 / 多声部)。

一台风扇就是一个声部。每个声部有自己的叶片数、转速范围、惯性时间常数和摆位,
因为物理上它们本来就是不同的电机。

单电机(旧格式,仍然可用):

    bpm = 92
    blades = 7
    E4:1 E4:1 F4:1 G4:1

多电机:

    bpm = 120
    title = 四电机合奏

    [voice melody]          ; 声部名随意, 但建议 melody/bass/pad 这样
    blades = 7              ; 叶片数(决定音域)
    harmonic = 2            ; 用第几阶谐波发音, auto = 自动挑
    glide = 0.08            ; 惯性时间常数(秒), 小风扇小、大风扇大
    min_rpm = 700           ; 校准得来: 刚好能稳转的下限
    max_rpm = 3200
    start_duty = 0.18       ; 刚好能转起来的占空比
    detune = 0              ; 音分
    pan = -0.45             ; 摆位: -1 最左, 0 正中, +1 最右
    delay_ms = 4            ; 摆位延迟(毫秒), 让几只风扇不在同一个点
    gain = 1.0              ; 音量
    A4:1 C5:1 E5:2          ; 音符:拍数

    [voice bass]
    blades = 7
    ...
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field

_SEMI = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}
_NOTE_RE = re.compile(r"^([A-Ga-g])([#b]?)(-?\d)$")
_REST_TOKENS = {"r", "rest", "-", ".", "_"}
_VOICE_HEADER = re.compile(r"^\[\s*([^\[\]]+?)\s*\]$")

_VOICE_KEYS = {
    "blades": int,
    "harmonic": int,
    "glide": float,
    "min_rpm": float,
    "max_rpm": float,
    "start_duty": float,
    "detune": float,
    "pan": float,
    "delay_ms": float,
    "gain": float,
    "noise_center": float,
    "timbre": float,
    "noise_gain": float,
}
_GLOBAL_KEYS = {"bpm": float, "title": str}


def note_to_midi(name):
    """'C#4' -> 61; 'r' -> None。"""
    text = name.strip()
    if text.lower() in _REST_TOKENS:
        return None
    m = _NOTE_RE.match(text)
    if not m:
        raise ValueError(f"无法解析音符: {name!r}")
    letter, accidental, octave = m.group(1).upper(), m.group(2), int(m.group(3))
    semis = _SEMI[letter] + (1 if accidental == "#" else -1 if accidental == "b" else 0)
    return 12 * (octave + 1) + semis


def midi_to_name(midi):
    names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
    return f"{names[midi % 12]}{midi // 12 - 1}"


def midi_to_freq(midi, detune=0.0):
    """十二平均律, A4 = 440 Hz。detune 单位是音分。"""
    return 440.0 * 2.0 ** ((midi - 69) / 12.0) * 2.0 ** (detune / 1200.0)


@dataclass
class Voice:
    """一台风扇 = 一个声部。"""

    label: str = "main"
    blades: int = 7
    harmonic: int | None = None
    glide: float = 0.22
    min_rpm: float = 400.0
    max_rpm: float = 2000.0
    start_duty: float = 0.22
    detune: float = 0.0
    pan: float = 0.0
    delay_ms: float = 0.0
    gain: float = 1.0
    noise_center: float = 420.0    # 气动噪声中心频率(Hz): 小扇高、大扇低
    timbre: float = 1.15           # 谐波衰减指数: 小=更"劈"(谐波丰富), 大=更圆润
    noise_gain: float = 1.0        # 风噪相对音高的音量
    notes: list = field(default_factory=list)

    def __str__(self):
        return (
            f"<Voice {self.label}: {len(self.notes)} 音, {self.blades} 叶, "
            f"{self.min_rpm:g}-{self.max_rpm:g}rpm, k={self.harmonic or 'auto'}, "
            f"pan={self.pan:+.2f}>"
        )


@dataclass
class Score:
    bpm: float = 96.0
    title: str = ""
    source: str = ""
    voices: list = field(default_factory=list)

    @property
    def beat_seconds(self):
        return 60.0 / self.bpm

    @property
    def is_polyphonic(self):
        return len(self.voices) > 1

    # ---- 单声部兼容层: 老代码/老谱子直接当一台风扇用 ----
    @property
    def notes(self):
        return self.voices[0].notes if self.voices else []

    def _first(self, attr, fallback=None):
        return getattr(self.voices[0], attr) if self.voices else fallback

    @property
    def blades(self):
        return self._first("blades", 7)

    @property
    def harmonic(self):
        return self._first("harmonic")

    @property
    def glide(self):
        return self._first("glide", 0.22)

    @property
    def min_rpm(self):
        return self._first("min_rpm", 400.0)

    @property
    def max_rpm(self):
        return self._first("max_rpm", 2000.0)

    @property
    def start_duty(self):
        return self._first("start_duty", 0.22)

    @property
    def detune(self):
        return self._first("detune", 0.0)

    def __str__(self):
        return (
            f"<Score {self.title or self.source}: {len(self.voices)} 电机, "
            f"{self.bpm:g}bpm>"
        )


def _convert(key, value, where):
    if key == "harmonic":
        return None if value.lower() in ("auto", "0", "") else int(value)
    try:
        return _VOICE_KEYS[key](value)
    except ValueError:
        raise ValueError(f"{where}: {key} = {value!r} 不是数字") from None


def _note_token(token, where):
    if ":" in token:
        name, _, dur = token.partition(":")
    else:
        name, dur = token, "1"
    try:
        beats = float(dur)
    except ValueError:
        raise ValueError(f"{where}: 拍数 {dur!r} 不是数字") from None
    if beats <= 0:
        raise ValueError(f"{where}: 拍数必须 > 0")
    return note_to_midi(name), beats


def parse_text(text, source="<text>"):
    sc = Score(source=source)
    defaults = {}
    implicit = []
    explicit = []
    current = None

    for lineno, raw in enumerate(text.splitlines(), 1):
        where = f"{source}:{lineno}"
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        line = re.split(r";|//", line, maxsplit=1)[0].strip()
        if not line:
            continue

        m = _VOICE_HEADER.match(line)
        if m:
            label = m.group(1).strip()
            if label.lower().startswith("voice "):      # [voice fan40] 里的 voice 是关键字, 不是名字
                label = label[6:].strip()
            current = Voice(label=label or f"voice{len(explicit) + 1}", **defaults)
            explicit.append(current)
            continue

        if re.match(r"^[A-Za-z_]\w*\s*=", line):
            key, _, value = line.partition("=")
            key, value = key.strip().lower(), value.strip()
            if key in _GLOBAL_KEYS:
                try:
                    setattr(sc, key, _GLOBAL_KEYS[key](value))
                except ValueError:
                    raise ValueError(f"{where}: {key} = {value!r} 类型不对") from None
            elif key in _VOICE_KEYS:
                converted = _convert(key, value, where)
                if current is None:
                    defaults[key] = converted     # 声部段之前的设置 = 全体默认值
                else:
                    setattr(current, key, converted)
            else:
                raise ValueError(f"{where}: 未知设置项 {key!r}")
            continue

        target = current.notes if current is not None else implicit
        for token in line.replace("|", " ").split():
            target.append(_note_token(token, where))

    voices = []
    if implicit:
        voices.append(Voice(label="main", **defaults, notes=implicit))
    voices.extend(explicit)
    voices = [v for v in voices if v.notes]
    if not voices:
        raise ValueError(f"{source}: 谱子里没有音符")

    for v in voices:
        if v.max_rpm <= v.min_rpm:
            raise ValueError(f"{source}: 声部 {v.label} 的 max_rpm 必须大于 min_rpm")
        if not 0.0 <= v.start_duty <= 1.0:
            raise ValueError(f"{source}: 声部 {v.label} 的 start_duty 必须在 0~1")
        v.pan = max(-1.0, min(1.0, float(v.pan)))

    sc.voices = voices
    if not sc.title:
        sc.title = source
    return sc


def parse(path):
    with open(path, encoding="utf-8-sig") as fh:
        return parse_text(fh.read(), source=path)
