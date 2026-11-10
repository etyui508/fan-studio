"""电机型号库: 每种电机是一个"乐器"。

核心问题: 一个电机为什么会发出某个音高?

答案不是"它转多快", 而是**它每秒钟重复多少次某个周期事件**:

| 电机种类 | 周期事件 | 音高公式 | 典型数字 |
|---|---|---|---|
| 轴流风扇 | 叶片扫过 | rpm/60 × 叶片数 | 7 叶 1500rpm = 175Hz |
| 电刷直流 | 换向片切换 | rpm/60 × 换向片数 | 3 片 6000rpm = 300Hz |
| 无刷直流 | 换相 | rpm/60 × 极对数 | 12N14P 10000rpm = 1167Hz |
| 步进电机 | 步进脉冲 | rpm/60 × 每转步数 | NEMA17 200步/转 300rpm = 1000Hz |
| 偏心转子 | 偏心块转一圈 | rpm/60 | 振动马达 12000rpm = 200Hz |
| 隔膜气泵 | 单向阀开合 | rpm/60 × 每转行程 | 3000rpm = 50Hz |
| 伺服舵机 | 内部小电机换向 | 内部 rpm/60 × 换向片数 | 3 片 12000rpm = 600Hz |
| 交流异步 | 转子槽通过 | rpm/60 × 转子槽数 | 28 槽 1400rpm = 653Hz |
| 开关磁阻 | 相开关 | rpm/60 × 转子凸极数 | 8/6 结构 3000rpm = 300Hz |

伺服舵机要特别说明: 它的输出轴被减速箱压到几十转, 但**听到的音高来自减速箱前面那个小电机**,
所以转速范围填的是内部电机的转速。而且它的"占空比"其实是 50Hz 位置信号的脉宽偏移,
命令更新率只有 50Hz(数字舵机 300Hz+), 所以音符不能太短 —— 这是它和风扇最大的区别:

所以本库里 `blades` 这一列的真实含义是"**每转的周期事件数**", 不是字面的叶片数。
风扇填叶片数, 步进填每转步数, 无刷填极对数, 电刷填换向片数。公式完全一样。

另外三列决定音色, 也都是物理量:
* `glide` 转动惯量 (秒): 空心杯 15ms, 140mm 大扇 220ms, 带减速箱的步进 250ms;
* `noise_center` 宽频噪声中心: 小扇 900Hz 是"嘶", 大扇 200Hz 是"呼", 步进几乎没有;
* `timbre` 谐波衰减指数: 越小越"劈"(步进电机是咔哒声, timbre 0.5), 越大越圆润(大扇 1.5)。

注意 `kind` 一列:
* `revolve` —— 音高由转速推出, 本引擎可以直接编曲;
* `direct`  —— 音高由驱动频率直接给(音圈/压电/带电抗器的变频器), 需要另一种后端;
* `unpitched` —— 无法定音高(舵机/超声电机/低频云台电机), 只能做节奏或音效。
"""

from __future__ import annotations

import math

# ─────────────────────────────────────────────────────────────────────────────
# 型号库。`blades` = 每转的周期事件数, 详见模块开头的表。
# ─────────────────────────────────────────────────────────────────────────────
MOTOR_CATALOG = [
    # ═══ 轴流风机 ═══
    dict(key="fan40", model="40mm 高速小扇", family="轴流风机", semantics="叶片通过",
         blades=7, min_rpm=3000, max_rpm=13000, start_duty=0.25, glide=0.05,
         harmonic=1, noise_center=900.0, timbre=0.85, noise_gain=1.25,
         kind="revolve", role="主旋律", drive="25kHz PWM + 12V",
         note="转速极高、惯性极小, 能唱最快的句子; 尖锐带谐波"),
    dict(key="fan60", model="60mm 中速扇", family="轴流风机", semantics="叶片通过",
         blades=9, min_rpm=2000, max_rpm=7000, start_duty=0.22, glide=0.07,
         harmonic=1, noise_center=700.0, timbre=0.95, noise_gain=1.10,
         kind="revolve", role="副旋律", drive="25kHz PWM + 12V",
         note="9 叶, 同属中高音区但比 40mm 柔和"),
    dict(key="fan80", model="80mm 机箱扇", family="轴流风机", semantics="叶片通过",
         blades=7, min_rpm=1500, max_rpm=4200, start_duty=0.20, glide=0.09,
         harmonic=1, noise_center=520.0, timbre=1.05, noise_gain=1.00,
         kind="revolve", role="和声", drive="25kHz PWM + 12V",
         note="中音区, 换速比小扇慢一档"),
    dict(key="fan92", model="92mm 机箱扇", family="轴流风机", semantics="叶片通过",
         blades=9, min_rpm=1200, max_rpm=3200, start_duty=0.20, glide=0.11,
         harmonic=1, noise_center=430.0, timbre=1.10, noise_gain=1.00,
         kind="revolve", role="内声部", drive="25kHz PWM + 12V",
         note="9 叶中低音, 与 80mm 音域接近但谐波结构不同"),
    dict(key="fan120", model="120mm 静音扇", family="轴流风机", semantics="叶片通过",
         blades=7, min_rpm=500, max_rpm=1500, start_duty=0.18, glide=0.18,
         harmonic=1, noise_center=260.0, timbre=1.35, noise_gain=0.90,
         kind="revolve", role="低音", drive="25kHz PWM + 12V",
         note="低转速大扇, 惯性明显, 只适合长音"),
    dict(key="fan140", model="140mm 大风量", family="轴流风机", semantics="叶片通过",
         blades=9, min_rpm=450, max_rpm=1800, start_duty=0.16, glide=0.22,
         harmonic=1, noise_center=200.0, timbre=1.50, noise_gain=0.85,
         kind="revolve", role="超低音/踏板", drive="25kHz PWM + 12V",
         note="惯性最大, 几乎是个低频振荡器"),
    dict(key="fan12038", model="12038 服务器暴力扇", family="轴流风机", semantics="叶片通过",
         blades=7, min_rpm=1500, max_rpm=5200, start_duty=0.22, glide=0.13,
         harmonic=2, noise_center=380.0, timbre=1.00, noise_gain=1.30,
         kind="revolve", role="强奏", drive="25kHz PWM + 12V, 2.5A",
         note="高静压, 噪音大; 用第 2 阶谐波可唱中高音"),

    # ═══ 离心/涡轮 ═══
    dict(key="centrifugal", model="离心风机(鼠笼)", family="离心风机", semantics="叶片通过",
         blades=32, min_rpm=800, max_rpm=3000, start_duty=0.20, glide=0.15,
         harmonic=1, noise_center=400.0, timbre=1.15, noise_gain=1.10,
         kind="revolve", role="中高音", drive="25kHz PWM + 12V",
         note="32 片前向叶片, 基频天生偏高, 风量大但吵"),
    dict(key="blower", model="涡轮鼓风机", family="离心风机", semantics="叶片通过",
         blades=41, min_rpm=1200, max_rpm=4000, start_duty=0.20, glide=0.10,
         harmonic=1, noise_center=1200.0, timbre=0.90, noise_gain=1.15,
         kind="revolve", role="高音铃", drive="25kHz PWM + 12V",
         note="41 片密集叶片, 基频就高, 像口哨"),
    dict(key="laptop", model="笔电薄叶涡轮", family="离心风机", semantics="叶片通过",
         blades=43, min_rpm=1500, max_rpm=5000, start_duty=0.15, glide=0.06,
         harmonic=1, noise_center=800.0, timbre=1.00, noise_gain=1.05,
         kind="revolve", role="高音", drive="EC 专用, 极难驱动",
         note="5V 轻薄叶, 转速高惯性小; 但接口是 EC 私有协议"),

    # ═══ 电刷直流 ═══
    dict(key="motor130", model="130 玩具电机", family="电刷直流", semantics="换向片切换",
         blades=3, min_rpm=3000, max_rpm=18000, start_duty=0.06, glide=0.04,
         harmonic=1, noise_center=1400.0, timbre=0.80, noise_gain=0.60,
         kind="revolve", role="中高音", drive="PWM 直驱 3-6V",
         note="3 片换向器, 音高是换向火花频率; 电刷噪声很尖"),
    dict(key="motor540", model="540 车模电机", family="电刷直流", semantics="换向片切换",
         blades=3, min_rpm=5000, max_rpm=25000, start_duty=0.08, glide=0.06,
         harmonic=1, noise_center=1600.0, timbre=0.75, noise_gain=0.70,
         kind="revolve", role="高音", drive="PWM + MOSFET, 大电流",
         note="7.2V 大扭矩, 换向频率可到 1.2kHz"),
    dict(key="coreless", model="空心杯电机", family="电刷直流", semantics="换向片切换",
         blades=5, min_rpm=8000, max_rpm=30000, start_duty=0.05, glide=0.015,
         harmonic=1, noise_center=2000.0, timbre=0.85, noise_gain=0.55,
         kind="revolve", role="超高音", drive="PWM 直驱",
         note="5 片换向器 + 无铁芯, 惯性极小, 响应最快"),

    # ═══ 永磁直流 ═══
    dict(key="pmdc", model="永磁直流减速电机", family="永磁直流", semantics="换向片切换",
         blades=12, min_rpm=300, max_rpm=3000, start_duty=0.08, glide=0.15,
         harmonic=1, noise_center=500.0, timbre=1.10, noise_gain=0.70,
         kind="revolve", role="中低音", drive="PWM + H 桥",
         note="永磁体励磁 + 12 片换向器: 60-600Hz; 行星减速箱带来一层低频齿噪"),

    # ═══ 励磁直流 ═══
    dict(key="shunt", model="并励直流电机", family="励磁直流", semantics="换向片切换",
         blades=24, min_rpm=1200, max_rpm=1800, start_duty=0.10, glide=0.25,
         harmonic=1, noise_center=700.0, timbre=1.15, noise_gain=0.80,
         kind="revolve", role="中音", drive="调压调速",
         note="并励特性硬: 转速几乎不随负载变 -> 音高极稳, 但可变范围只有 1.5 倍, 唱不了宽音域"),
    dict(key="series", model="串励直流电机", family="励磁直流", semantics="换向片切换",
         blades=24, min_rpm=800, max_rpm=12000, start_duty=0.06, glide=0.10,
         harmonic=1, noise_center=900.0, timbre=0.85, noise_gain=0.95,
         kind="revolve", role="宽音域", drive="调压调速, 轻载会飞车",
         note="串励特性软: 起动转矩大、轻载转速极高 -> 音域宽达 320-4800Hz, "
              "是直流族里最能唱的一个; 但轻载飞车很危险"),
    dict(key="compound", model="复励直流电机", family="励磁直流", semantics="换向片切换",
         blades=24, min_rpm=900, max_rpm=4000, start_duty=0.08, glide=0.18,
         harmonic=1, noise_center=800.0, timbre=1.00, noise_gain=0.85,
         kind="revolve", role="中音", drive="调压调速",
         note="并励 + 串励的折中: 起动转矩大又不会飞车, 音域 360-1600Hz"),

    # ═══ 无刷直流 ═══
    dict(key="fpv", model="穿越机电机 12N14P", family="无刷直流", semantics="换相",
         blades=7, min_rpm=3000, max_rpm=30000, start_duty=0.10, glide=0.012,
         harmonic=1, noise_center=2000.0, timbre=0.85, noise_gain=0.90,
         kind="revolve", role="高音", drive="ESC 三相驱动, 需转速闭环",
         note="7 对极: 换相频率 = rpm/60×7, 这就是穿越机的电机啸叫"),
    dict(key="gimbal", model="云台电机 12N14P", family="无刷直流", semantics="换相",
         blades=7, min_rpm=5, max_rpm=300, start_duty=0.05, glide=0.30,
         harmonic=1, noise_center=120.0, timbre=1.60, noise_gain=0.40,
         kind="unpitched", role="—", drive="FOC 驱动, 低 KV 大扭矩",
         note="换相频率只有 0.6-35Hz, 听不见音高, 只能当颤音/低频震感"),

    # ═══ 步进电机 ═══
    dict(key="nema17", model="NEMA17 步进", family="步进电机", semantics="步进脉冲",
         blades=200, min_rpm=30, max_rpm=600, start_duty=0.05, glide=0.03,
         harmonic=1, noise_center=3000.0, timbre=0.55, noise_gain=0.12,
         kind="revolve", role="高音/咔哒", drive="A4988/DRV8825 细分驱动",
         note="200 步/转: 300rpm 就是 1kHz 的咔哒, 经典软驱音乐的来源"),
    dict(key="stepper28byj", model="28BYJ-48 减速步进", family="步进电机", semantics="步进脉冲",
         blades=4096, min_rpm=1, max_rpm=15, start_duty=0.05, glide=0.25,
         harmonic=1, noise_center=1200.0, timbre=0.50, noise_gain=0.15,
         kind="revolve", role="中音/咔哒", drive="ULN2003 单极驱动",
         note="1:64 减速箱, 输出 4096 步/转; 慢而机械的齿轮撞击率"),

    # ═══ 其他回转类 ═══
    dict(key="erm", model="转子振动马达", family="偏心转子", semantics="偏心块旋转",
         blades=1, min_rpm=8000, max_rpm=15000, start_duty=0.10, glide=0.05,
         harmonic=1, noise_center=300.0, timbre=1.80, noise_gain=1.40,
         kind="revolve", role="低音嗡嗡", drive="PWM 直驱 3V",
         note="偏心块每转一圈晃一次 = 133-250Hz, 几乎全是震动噪声"),
    dict(key="pump", model="隔膜气泵", family="隔膜泵", semantics="单向阀开合",
         blades=1, min_rpm=3000, max_rpm=3600, start_duty=0.30, glide=0.30,
         harmonic=1, noise_center=700.0, timbre=0.60, noise_gain=1.50,
         kind="revolve", role="低频嗡嗡", drive="交流直驱或 PWM",
         note="50/60Hz 嗡嗡声的来源; 转速几乎不可调, 只能当持续音"),

    # ═══ 交流感应电机 ═══
    dict(key="cage", model="鼠笼异步电机", family="交流感应", semantics="转子槽通过",
         blades=28, min_rpm=280, max_rpm=1450, start_duty=0.10, glide=0.45,
         harmonic=1, noise_center=600.0, timbre=1.20, noise_gain=1.10,
         kind="revolve", role="低音", drive="变频器 VFD",
         note="28 转子槽: 槽通过频率 130-677Hz 是主要电磁噪声; 有滑差, 实际转速略低于同步速"),
    dict(key="wound_rotor", model="绕线式异步电机", family="交流感应", semantics="转子槽/滑环",
         blades=36, min_rpm=300, max_rpm=1400, start_duty=0.10, glide=0.40,
         harmonic=1, noise_center=800.0, timbre=1.05, noise_gain=1.05,
         kind="revolve", role="中低音", drive="VFD + 外接起动电阻",
         note="36 槽 + 滑环电刷: 比鼠笼多一层电刷嘶声; 外接电阻还能改善起动性能"),

    # ═══ 同步电机 ═══
    dict(key="sync", model="同步电机", family="同步电机", semantics="转子槽通过",
         blades=24, min_rpm=750, max_rpm=1500, start_duty=0.10, glide=0.35,
         harmonic=1, noise_center=700.0, timbre=1.15, noise_gain=1.00,
         kind="revolve", role="中低音", drive="VFD / 并网",
         note="转速严格锁在 2×50Hz/极对数, 音高是几档量化的值而不是连续的 —— 唱不了滑音"),
    dict(key="pmsm", model="永磁同步电机 PMSM", family="同步电机", semantics="极对数换相",
         blades=4, min_rpm=100, max_rpm=3000, start_duty=0.08, glide=0.08,
         harmonic=1, noise_center=1000.0, timbre=1.00, noise_gain=0.85,
         kind="revolve", role="中音", drive="FOC 驱动",
         note="4 对极: 换相频率只有 6.7-200Hz, 偏低; 真正好听的是电流环载波, 那得走 direct 后端"),
    dict(key="wound_sync", model="绕线励磁同步电机", family="同步电机", semantics="转子槽通过",
         blades=30, min_rpm=600, max_rpm=1500, start_duty=0.10, glide=0.40,
         harmonic=1, noise_center=650.0, timbre=1.25, noise_gain=1.05,
         kind="revolve", role="低音", drive="VFD + 直流励磁",
         note="多一套励磁绕组: 调励磁能改噪声和功率因数, 等于多一个音色旋钮"),

    # ═══ 磁阻电机 ═══
    dict(key="srm", model="开关磁阻电机 SRM", family="磁阻电机", semantics="相开关频率",
         blades=6, min_rpm=300, max_rpm=6000, start_duty=0.10, glide=0.06,
         harmonic=1, noise_center=1500.0, timbre=0.60, noise_gain=1.30,
         kind="revolve", role="高音/嗡", drive="不对称半桥 + 位置检测",
         note="8/6 结构: 6 个转子凸极, 相开关 30-600Hz。转矩脉动大是它的标志, "
              "谐波极丰富 -> 本库里最'脏'也最有个性的音色"),
    dict(key="synrm", model="同步磁阻电机 SynRM", family="磁阻电机", semantics="转子槽通过",
         blades=4, min_rpm=300, max_rpm=3000, start_duty=0.10, glide=0.10,
         harmonic=1, noise_center=1200.0, timbre=1.30, noise_gain=0.95,
         kind="revolve", role="中高音", drive="VFD + 磁障转子",
         note="4 极 + 多层磁障: 电磁噪声比 SRM 干净得多, 效率高但功率因数偏低"),

    # ═══ 直驱/专用 ═══
    dict(key="torque_motor", model="力矩电机(直驱转台)", family="直驱专用", semantics="极对数换相",
         blades=22, min_rpm=10, max_rpm=200, start_duty=0.08, glide=0.35,
         harmonic=1, noise_center=200.0, timbre=1.45, noise_gain=0.80,
         kind="revolve", role="超低音", drive="直驱伺服驱动器",
         note="22 对极直驱: 换相频率只有 3.7-73Hz -> 听感是次低频震动; "
              "但齿槽转矩频率会跑到 100-300Hz, 那才是它的音"),

    # ═══ 伺服舵机 ═══
    dict(key="servo_cont", model="连续旋转舵机", family="伺服舵机", semantics="内部电机换向",
         blades=3, min_rpm=800, max_rpm=20000, start_duty=0.10, glide=0.06,
         harmonic=1, noise_center=1500.0, timbre=0.85, noise_gain=0.70,
         kind="revolve", role="中高音", drive="50Hz 位置信号, 脉宽当占空比",
         note="去掉限位就是双向直流电机, 可正反转; 音高来自内部小电机换向频率, "
              "不是输出轴转速。命令更新率仅 50Hz, 音符不能短于 0.1 秒"),
    dict(key="servo_digital", model="数字舵机 333Hz", family="伺服舵机", semantics="内部电机换向",
         blades=3, min_rpm=600, max_rpm=16000, start_duty=0.10, glide=0.035,
         harmonic=1, noise_center=1800.0, timbre=0.80, noise_gain=0.75,
         kind="revolve", role="高音", drive="333Hz 位置信号",
         note="控制环跑到 300Hz 以上, 能唱更快的句子; 代价是多一个 333Hz 的控制嗡声"),
    dict(key="servo_mg996", model="MG996R 金属齿舵机", family="伺服舵机", semantics="内部电机换向",
         blades=3, min_rpm=600, max_rpm=12000, start_duty=0.10, glide=0.12,
         harmonic=1, noise_center=1100.0, timbre=1.05, noise_gain=1.10,
         kind="revolve", role="中音", drive="50Hz 位置信号, 大扭矩",
         note="金属齿轮惯性大、齿隙撞击明显, 换速慢一倍; 音色比 SG90 更'咬'"),
    dict(key="servo", model="SG90 位置舵机", family="伺服舵机", semantics="位置更新率",
         blades=1, min_rpm=0, max_rpm=600, start_duty=0.05, glide=0.02,
         harmonic=1, noise_center=1000.0, timbre=1.00, noise_gain=0.80,
         kind="unpitched", role="—", drive="50Hz 位置信号, 有限位",
         note="有限位, 只能在 180° 内来回; 每次'移动'是一声齿轮嗒, 音高不由转速定, "
              "只能做节奏/打击声部"),
    dict(key="servo_industrial", model="工业伺服 + 驱动器", family="伺服舵机", semantics="电流环/载波",
         blades=4, min_rpm=0, max_rpm=3000, start_duty=0.10, glide=0.05,
         harmonic=1, noise_center=6000.0, timbre=1.20, noise_gain=1.10,
         kind="unpitched", role="—", drive="伺服驱动器, 电流环 1-4kHz",
         note="PMSM + 编码器闭环, 转速与音高脱钩; 可听的是电流环/载波啸叫(1-4kHz), "
              "属于'伺服啸叫'而不是乐音"),
    # ═══ 不可定音高 / 特殊驱动 ═══
    dict(key="voicecoil", model="音圈/电磁铁", family="直线电磁", semantics="驱动频率",
         blades=1, min_rpm=0, max_rpm=0, start_duty=0.05, glide=0.005,
         harmonic=1, noise_center=600.0, timbre=0.90, noise_gain=0.20,
         kind="direct", role="—", drive="直接给音频频率方波",
         note="音高直接等于驱动频率 20-2000Hz, 打击感最强; 需要另一种后端"),
    dict(key="linear_motor", model="直线电机", family="直线电磁", semantics="驱动电频率",
         blades=1, min_rpm=0, max_rpm=0, start_duty=0.08, glide=0.02,
         harmonic=1, noise_center=800.0, timbre=0.95, noise_gain=0.60,
         kind="direct", role="—", drive="三相直线驱动, 音高 = 电频率",
         note="旋转电机沿径向剖开展平: 没有转速, 音高由驱动电频率直接决定(20Hz-1kHz); "
              "有端部效应和发热问题"),
    dict(key="piezo", model="压电/超声电机", family="压电", semantics="谐振频率",
         blades=1, min_rpm=0, max_rpm=0, start_duty=0.05, glide=0.005,
         harmonic=1, noise_center=40000.0, timbre=1.00, noise_gain=0.10,
         kind="unpitched", role="—", drive="40kHz 谐振 + 幅度调制",
         note="工作在 40kHz 超声波段, 只能靠调制产生可听边带"),
    dict(key="vfd", model="异步电机 + 变频器", family="交流感应", semantics="载波+槽谐波",
         blades=4, min_rpm=0, max_rpm=3000, start_duty=0.10, glide=0.50,
         harmonic=1, noise_center=4000.0, timbre=1.20, noise_gain=1.00,
         kind="unpitched", role="—", drive="VFD, 载波 1-16kHz 可设",
         note="转速和音高脱钩; 听到的其实是变频器载波(设成音频就是'唱歌的变频器')"),
]

# 默认编制顺序: 先 6 台轴流风扇, 再逐步加入更有意思的电机。
DEFAULT_ENSEMBLE = [
    "fan40", "fan60", "fan80", "fan92", "fan120", "fan140",
    "blower", "centrifugal", "laptop", "fan12038",
    "coreless", "motor130", "motor540", "series", "compound", "shunt",
    "servo_cont", "servo_digital", "fpv",
    "wound_sync", "sync", "wound_rotor", "cage", "nema17", "pmdc",
    "stepper28byj", "srm", "synrm", "pmsm", "erm", "torque_motor", "pump",
]

_BY_KEY = {m["key"]: m for m in MOTOR_CATALOG}

# 声部角色: (取哪一层的音, 取该层最高还是最低, 采样周期)
ROLE_TABLE = {
    1: [dict(pick="top", stratum=(0.00, 1.00), step=0.28)],
    2: [dict(pick="top", stratum=(0.00, 1.00), step=0.30),
        dict(pick="bottom", stratum=(0.00, 1.00), step=0.70)],
    3: [dict(pick="top", stratum=(0.00, 1.00), step=0.30),
        dict(pick="top", stratum=(0.35, 0.95), step=0.45),
        dict(pick="bottom", stratum=(0.00, 1.00), step=0.65)],
    4: [dict(pick="top", stratum=(0.00, 1.00), step=0.28),
        dict(pick="top", stratum=(0.30, 0.90), step=0.40),
        dict(pick="bottom", stratum=(0.10, 0.70), step=0.50),
        dict(pick="bottom", stratum=(0.00, 1.00), step=0.65)],
    5: [dict(pick="top", stratum=(0.00, 1.00), step=0.26),
        dict(pick="top", stratum=(0.35, 0.95), step=0.36),
        dict(pick="top", stratum=(0.45, 0.80), step=0.46),
        dict(pick="bottom", stratum=(0.15, 0.70), step=0.55),
        dict(pick="bottom", stratum=(0.00, 1.00), step=0.70)],
    6: [dict(pick="top", stratum=(0.00, 1.00), step=0.25),
        dict(pick="top", stratum=(0.40, 0.98), step=0.32),
        dict(pick="top", stratum=(0.45, 0.85), step=0.40),
        dict(pick="bottom", stratum=(0.30, 0.75), step=0.50),
        dict(pick="bottom", stratum=(0.05, 0.55), step=0.65),
        dict(pick="bottom", stratum=(0.00, 0.30), step=1.10)],
    7: [dict(pick="top", stratum=(0.00, 1.00), step=0.25),
        dict(pick="top", stratum=(0.45, 0.98), step=0.31),
        dict(pick="top", stratum=(0.50, 0.88), step=0.38),
        dict(pick="bottom", stratum=(0.40, 0.80), step=0.46),
        dict(pick="top", stratum=(0.10, 0.55), step=0.55),
        dict(pick="bottom", stratum=(0.02, 0.45), step=0.70),
        dict(pick="bottom", stratum=(0.00, 0.25), step=1.20)],
    8: [dict(pick="top", stratum=(0.00, 1.00), step=0.24),
        dict(pick="top", stratum=(0.50, 0.98), step=0.30),
        dict(pick="top", stratum=(0.55, 0.90), step=0.36),
        dict(pick="bottom", stratum=(0.45, 0.82), step=0.44),
        dict(pick="top", stratum=(0.15, 0.60), step=0.52),
        dict(pick="bottom", stratum=(0.05, 0.50), step=0.66),
        dict(pick="bottom", stratum=(0.00, 0.28), step=1.10),
        dict(pick="bottom", stratum=(0.00, 0.15), step=1.50)],
}


def _procedural_roles(count):
    """声部多于 8 个时按位置生成角色: 第 0 个永远是旋律, 其余往下摊开。"""
    out = [dict(pick="top", stratum=(0.00, 1.00), step=0.25)]
    for i in range(1, count):
        pos = (i - 1) / max(1, count - 2)
        center = 0.85 - 0.80 * pos
        half = 0.28
        out.append(dict(
            pick="top" if i % 2 == 0 else "bottom",
            stratum=(max(0.0, center - half), min(1.0, center + half)),
            step=0.32 + 0.60 * pos,
        ))
    return out


def roles(count):
    if count in ROLE_TABLE:
        return list(ROLE_TABLE[count])
    if count < min(ROLE_TABLE):
        return list(ROLE_TABLE[1])
    return _procedural_roles(count)


def by_key(key):
    return _BY_KEY.get(key)


def playable_hz(motor):
    """这个型号能唱出的基音频率区间 (Hz)。"""
    k = motor.get("harmonic") or 1
    lo = motor["min_rpm"] * motor["blades"] / 60.0 * k
    hi = motor["max_rpm"] * motor["blades"] / 60.0 * k
    return lo, hi


def note_name_of(hz):
    if hz <= 0:
        return "—"
    midi = 69 + 12 * math.log2(hz / 440.0)
    names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"]
    m = int(round(midi))
    return f"{names[m % 12]}{m // 12 - 1}"


def arrangable():
    """所有能直接编曲的型号, 按默认编制顺序。"""
    out = []
    for key in DEFAULT_ENSEMBLE:
        m = _BY_KEY.get(key)
        if m and m["kind"] == "revolve":
            out.append(m)
    return out


def ensemble(count, keys=None):
    """取 count 台电机, 配上前 count 个声部角色。

    数量不设上限: 超出可编曲型号数时从头循环复用(现实中你确实可以买两台一模一样的风扇),
    复用的型号会在名字后面加 -2 / -3 以示区分。

    注意 3~8 台最实用 —— 每台可用音域只有约 2 个八度, 再多就会叠在同一片频谱里互相遮蔽。
    这里不拦你, 但听得出来。
    """
    if keys:
        pool = [_BY_KEY[k] for k in keys if k in _BY_KEY and _BY_KEY[k]["kind"] == "revolve"]
    else:
        pool = arrangable()
    if not pool:
        raise ValueError("没有可编曲的电机")

    count = max(1, int(count))
    chosen = [pool[i % len(pool)] for i in range(count)]
    role_list = roles(count)

    used = {}
    out = []
    for i, (motor, role) in enumerate(zip(chosen, role_list)):
        pan = 0.0 if count == 1 else -0.6 + 1.2 * i / (count - 1)
        # 摆位延时按比例摊开(3~25ms), 免得声部一多就散架
        delay_ms = 3.0 + 22.0 * (i / max(1, count - 1))
        gain = round(max(0.45, 1.0 - 0.03 * i), 2)
        # 硬约束: 音符不能短于这台电机的整定时间。
        # 一阶惯性 τ 要 3.5τ 才到位(96%), 比这短的音符根本唱不到目标音高 ——
        # 实测 MG996R(τ=120ms)用 0.28s 的音符, 检出率只有 19%; 检出的音偏差却只有 0.1 音分,
        # 说明不是音高算错, 是它来不及换速。
        step = max(role["step"], 3.5 * motor["glide"])
        used[motor["key"]] = used.get(motor["key"], 0) + 1
        nth = used[motor["key"]]
        label = motor["key"] if nth == 1 else f"{motor['key']}-{nth}"
        out.append(dict(motor, label=label, pan=pan, delay_ms=delay_ms, gain=gain,
                        step=step, pick=role["pick"], stratum=role["stratum"]))
    return out


def table(show_all=True):
    lines = []
    last_family = None
    for i, m in enumerate(MOTOR_CATALOG, 1):
        if not show_all and m["kind"] != "revolve":
            continue
        if m["family"] != last_family:
            last_family = m["family"]
            lines.append(f"  ── {last_family} ──")
        lo, hi = playable_hz(m)
        if m["kind"] == "revolve":
            rng = f"{note_name_of(lo):>3}-{note_name_of(hi):<3} {lo:>5.0f}-{hi:<5.0f}Hz"
        else:
            rng = "      (不定音高)      "
        flag = {"revolve": "可编曲", "direct": "需专用后端", "unpitched": "只能做音效"}[m["kind"]]
        lines.append(
            f"  {i:>2} {m['model']:<21}{m['blades']:>5} "
            f"{m['min_rpm']:>5}-{m['max_rpm']:<5} {rng}  "
            f"{m['glide'] * 1000:>4.0f}ms {m['timbre']:>5.2f} {flag:<11}{m['semantics']}"
        )
    return "\n".join(lines)


def legend():
    return (
        "  列: 事件/转 = 每转的周期事件数(风扇填叶片数, 步进填每转步数, 无刷填极对数);\n"
        "      转速范围; 基音区间 = 能唱出的音; 惯性 = glide 时间常数;\n"
        "      timbre = 谐波衰减指数(小 = 咔哒/劈, 大 = 圆润)。"
    )
