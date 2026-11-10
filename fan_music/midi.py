"""极简 Standard MIDI File 解析器 (无第三方依赖)。

只做本项目需要的事: 把 MIDI 里的音符、速度、轨道名读出来, 换算成秒。
不处理 SMPTE 时间基准, 不处理 SysEx 内容, 不处理弯音/CC —— 用不上。
"""

from __future__ import annotations

import bisect
import struct
from dataclasses import dataclass, field

_NAMES = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]


def pitch_name(pitch):
    return f"{_NAMES[pitch % 12]}{pitch // 12 - 1}"


def pitch_freq(pitch):
    return 440.0 * 2.0 ** ((pitch - 69) / 12.0)


@dataclass
class Note:
    track: int
    channel: int
    pitch: int
    velocity: int
    start_tick: int
    end_tick: int
    start: float = 0.0
    end: float = 0.0

    @property
    def duration(self):
        return self.end - self.start


@dataclass
class Track:
    index: int
    name: str = ""
    notes: list = field(default_factory=list)
    program: int = 0
    channels: set = field(default_factory=set)

    @property
    def is_drum(self):
        return self.channels == {9} or (9 in self.channels and len(self.channels) == 1)

    @property
    def lowest(self):
        return min((n.pitch for n in self.notes), default=None)

    @property
    def highest(self):
        return max((n.pitch for n in self.notes), default=None)


@dataclass
class MidiFile:
    fmt: int = 1
    division: int = 480
    tracks: list = field(default_factory=list)
    tempo_map: list = field(default_factory=list)   # [(tick, us_per_quarter)]
    length: float = 0.0

    def seconds(self, tick):
        """tick -> 秒 (按 tempo map 积分)。"""
        if not self.tempo_map:
            return tick / self.division * 0.5
        idx = bisect.bisect_right(self.tempo_map, (tick, float("inf"))) - 1
        idx = max(0, idx)
        base_tick, us = self.tempo_map[idx]
        total = 0.0
        for i in range(idx):
            t0, u0 = self.tempo_map[i]
            t1 = self.tempo_map[i + 1][0]
            total += (t1 - t0) / self.division * (u0 / 1e6)
        total += (tick - base_tick) / self.division * (us / 1e6)
        return total

    @property
    def bpm(self):
        us = self.tempo_map[0][1] if self.tempo_map else 500000
        return 60_000_000.0 / us


def _read_vlq(data, i):
    value = 0
    while True:
        if i >= len(data):
            raise ValueError("VLQ 越界, 文件可能被截断")
        b = data[i]
        i += 1
        value = (value << 7) | (b & 0x7F)
        if not b & 0x80:
            return value, i


def read(path):
    with open(path, "rb") as fh:
        data = fh.read()

    if data[:4] != b"MThd":
        raise ValueError("不是 MIDI 文件 (缺少 MThd)")
    header_len = struct.unpack(">I", data[4:8])[0]
    fmt, ntrks, division = struct.unpack(">HHH", data[8:14])
    if division & 0x8000:
        raise ValueError("SMPTE 时间基准的 MIDI 暂不支持")

    mf = MidiFile(fmt=fmt, division=division)
    pos = 8 + header_len
    tempos = []

    for track_index in range(ntrks):
        if pos + 8 > len(data):
            break
        chunk = data[pos:pos + 4]
        length = struct.unpack(">I", data[pos + 4:pos + 8])[0]
        body = data[pos + 8: pos + 8 + length]
        pos += 8 + length
        if chunk != b"MTrk":
            continue

        track = Track(index=track_index)
        ticks = 0
        i = 0
        status = 0
        open_notes = {}

        while i < len(body):
            delta, i = _read_vlq(body, i)
            ticks += delta
            if i >= len(body):
                break
            byte = body[i]

            if byte == 0xFF:                     # meta
                i += 1
                if i >= len(body):
                    break
                meta_type = body[i]
                i += 1
                mlen, i = _read_vlq(body, i)
                payload = body[i:i + mlen]
                i += mlen
                if meta_type == 0x51 and len(payload) >= 3:
                    us = (payload[0] << 16) | (payload[1] << 8) | payload[2]
                    tempos.append((ticks, us))
                elif meta_type == 0x03:
                    track.name = payload.decode("latin-1", "replace").strip()
                elif meta_type == 0x2F:
                    break
                continue

            if byte in (0xF0, 0xF7):             # sysex: 跳过
                i += 1
                slen, i = _read_vlq(body, i)
                i += slen
                continue

            if byte & 0x80:                      # 新的 status byte
                status = byte
                i += 1
            else:
                if not status:
                    i += 1
                    continue

            kind = status & 0xF0
            channel = status & 0x0F

            if kind in (0x80, 0x90):             # note off / on
                if i + 1 >= len(body):
                    break
                pitch, velocity = body[i], body[i + 1]
                i += 2
                key = (channel, pitch)
                if kind == 0x90 and velocity > 0:
                    open_notes[key] = (ticks, velocity)
                else:
                    started = open_notes.pop(key, None)
                    if started is not None:
                        start_tick, vel = started
                        track.notes.append(
                            Note(track_index, channel, pitch, vel, start_tick, ticks)
                        )
                track.channels.add(channel)
            elif kind in (0xA0, 0xB0, 0xE0):
                i += 2
            elif kind == 0xC0:
                track.program = body[i] if i < len(body) else 0
                i += 1
                track.channels.add(channel)
            elif kind == 0xD0:
                i += 1
            else:
                i += 1

        # 没收尾的音按文件末尾处理
        for (channel, pitch), (start_tick, vel) in open_notes.items():
            track.notes.append(Note(track_index, channel, pitch, vel, start_tick, ticks))

        track.notes.sort(key=lambda n: (n.start_tick, n.pitch))
        mf.tracks.append(track)

    tempos.sort(key=lambda t: t[0])
    if not tempos:
        tempos = [(0, 500000)]
    if tempos[0][0] != 0:
        tempos.insert(0, (0, tempos[0][1]))
    mf.tempo_map = tempos

    for track in mf.tracks:
        for n in track.notes:
            n.start = mf.seconds(n.start_tick)
            n.end = mf.seconds(n.end_tick)
    last = max((n.end for t in mf.tracks for n in t.notes), default=0.0)
    mf.length = last
    return mf


def monophonic(notes, keep="top"):
    """把可能重叠的音符压成单声部: 同一时刻只留一个 (风扇一次只能唱一个音)。

    keep="top" 取最高音(旋律), "bottom" 取最低音(低音)。
    """
    ordered = sorted(notes, key=lambda n: (n.start, n.pitch))
    if keep == "top":
        ordered = sorted(notes, key=lambda n: (n.start, -n.pitch))
    out = []
    for n in ordered:
        if out and n.start < out[-1].end - 1e-9:
            # 与上一个音重叠: 截断上一个音
            prev = out[-1]
            if n.start <= prev.start + 1e-9:
                continue
            out[-1] = Note(prev.track, prev.channel, prev.pitch, prev.velocity,
                           prev.start_tick, n.start_tick, prev.start, n.start)
        out.append(Note(n.track, n.channel, n.pitch, n.velocity,
                        n.start_tick, n.end_tick, n.start, n.end))
    out.sort(key=lambda n: n.start)
    return out


def quantize(notes, grid):
    """把起止时间对齐到 grid(秒) 网格上。"""
    if grid <= 0:
        return notes
    out = []
    for n in notes:
        start = round(n.start / grid) * grid
        end = round(n.end / grid) * grid
        if end - start < grid * 0.5:
            end = start + grid
        out.append(Note(n.track, n.channel, n.pitch, n.velocity,
                        n.start_tick, n.end_tick, start, end))
    return out


def describe(mf):
    rows = []
    rows.append(f"格式 {mf.fmt}, {len(mf.tracks)} 轨, 每拍 {mf.division} tick, "
                f"起始 {mf.bpm:.1f} BPM, 总长 {mf.length:.1f}s")
    rows.append("")
    rows.append("  轨  名称                       音符  音域             通道   鼓?")
    rows.append("  " + "-" * 68)
    for t in mf.tracks:
        if not t.notes:
            rows.append(f"{t.index:>4}  {(t.name or '(空)')[:24]:<24} {0:>6}  {'-':<16} "
                        f"{'-':<6} {'':>3}")
            continue
        rng = f"{pitch_name(t.lowest)} - {pitch_name(t.highest)}"
        chans = ",".join(str(c) for c in sorted(t.channels))
        rows.append(f"{t.index:>4}  {(t.name or '(无名)')[:24]:<24} {len(t.notes):>6}  "
                    f"{rng:<16} {chans:<6} {'是' if t.is_drum else '':>3}")
    return "\n".join(rows)
