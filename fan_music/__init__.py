"""fan_music — 用电脑风扇放音乐。

核心事实: 风扇的"音高"来自叶片通过频率 (blade passing frequency)

    f_bp = RPM / 60 * 叶片数

反解就是让风扇唱某个音需要的转速:

    RPM = f_bp * 60 / 叶片数

因为风扇转速有上下限, 单靠基频音域很窄, 所以本模块支持用第 k 次谐波发音
(代价是根音会低 k 分之一, 听起来像加了一个低音踏板)。
"""

__version__ = "0.1.0"
__all__ = ["score", "engine", "audio", "backends", "cli"]
