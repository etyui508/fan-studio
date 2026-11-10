"""真正去驱动硬件的后端。

三种出口:
  null     只走一遍流程, 打印占空比 (无硬件也能测时序)
  serial   Arduino/ESP32, 见 arduino/fan_synth/fan_synth.ino
  command  调外部命令, 用来接 FanControl / nbfc / 你自己写的桥

注意 command 的后端每次都要起进程, 一秒钟能更新十几次就到顶了, 只能放慢速曲子。
"""

from __future__ import annotations

import ctypes
import glob
import os
import shutil
import subprocess
import sys
import time

from . import engine


class NullBackend:
    name = "null"

    def __init__(self, verbose=True):
        self.verbose = verbose
        self.sent = []

    def start(self):
        if self.verbose:
            print("[null] 空跑: 不碰任何硬件, 只按时间轴走一遍。")

    def set_duty(self, duty):
        self.sent.append(duty)

    def close(self):
        if self.verbose and self.sent:
            lo = min(self.sent) * 100.0
            hi = max(self.sent) * 100.0
            print(f"[null] 共下发 {len(self.sent)} 次, 占空比 {lo:.1f}% ~ {hi:.1f}%")


class SerialBackend:
    name = "serial"

    def __init__(self, port, baud=115200):
        self.port = port
        self.baud = baud
        self.ser = None

    def start(self):
        try:
            import serial  # pyserial
        except ImportError:
            raise SystemExit(
                "缺少 pyserial。先装: pip install pyserial\n"
                "或者用 --backend null 空跑。"
            )
        self.ser = serial.Serial(self.port, self.baud, timeout=0.2, write_timeout=0.5)
        time.sleep(1.8)  # 等 Arduino 复位完成
        print(f"[serial] 已连接 {self.port} @ {self.baud}")

    def set_duty(self, duty):
        if self.ser is None:
            return
        permille = int(round(max(0.0, min(1.0, duty)) * 1000.0))
        try:
            self.ser.write(b"d%d\n" % permille)
        except Exception as exc:  # 线松了 / 板子重启
            print(f"[serial] 写入失败: {exc}")

    def close(self):
        if self.ser is not None:
            try:
                self.ser.write(b"d0\n")
                self.ser.close()
            except Exception:
                pass
            self.ser = None


class CommandBackend:
    name = "command"

    def __init__(self, template, min_interval=0.12, verbose=True):
        if "{duty" not in template:
            raise SystemExit('命令模板里必须包含 {duty}, 例如: --cmd "nbfc set -s {duty}"')
        self.template = template
        self.min_interval = min_interval
        self.verbose = verbose
        self.last_call = 0.0
        self.last_duty = None
        self.calls = 0

    def start(self):
        print(f"[command] 模板: {self.template}")
        print("[command] 提示: 进程启动有开销, 这条路上转速更新只能到 10Hz 量级, 谱子请放慢。")

    def set_duty(self, duty):
        now = time.monotonic()
        pct = round(max(0.0, min(1.0, duty)) * 100.0, 1)
        if self.last_duty is not None and abs(pct - self.last_duty) < 0.4:
            return
        if now - self.last_call < self.min_interval:
            return
        cmd = self.template.format(duty=pct, duty_int=int(round(pct)), percent=int(round(pct)))
        try:
            subprocess.run(cmd, shell=True, capture_output=True, timeout=5)
        except Exception as exc:
            print(f"[command] 执行失败: {exc}")
        self.last_call = now
        self.last_duty = pct
        self.calls += 1

    def close(self):
        if self.verbose:
            print(f"[command] 共调用 {self.calls} 次")


def play(plan, score, backend, rate=40.0, voice_index=0):
    """按时间轴把占空比喂给其中一台风扇。

    时间表来自 engine.duty_schedule: 换速段给满/给最低(饱和), 到位后回稳态值。
    渲染器用的是同一条曲线, 所以"听到的"和"真跑出来的"是同一个东西。
    """
    dt = 1.0 / rate
    sched = engine.duty_schedule(plan, score, voice_index=voice_index)
    start_duty = float(score.voices[voice_index].start_duty)
    idx = -1
    t = 0.0
    backend.start()
    try:
        deadline = time.perf_counter()
        while t < plan.duration:
            while idx + 1 < len(sched) and sched[idx + 1][0] <= t:
                idx += 1
            duty = sched[idx][1] if idx >= 0 else start_duty
            backend.set_duty(duty)
            t += dt
            deadline += dt
            sleep = deadline - time.perf_counter()
            if sleep > 0:
                time.sleep(sleep)
    except KeyboardInterrupt:
        print("\n[play] 中断, 正在停车…")
    finally:
        backend.close()
    return 0


def _is_admin():
    try:
        return bool(ctypes.windll.shell32.IsUserAnAdmin())
    except Exception:
        return False


def doctor():
    print("== fan_music 环境检查 ==")
    print(f"Python      : {sys.version.split()[0]} ({sys.executable})")
    try:
        import numpy

        print(f"numpy       : {numpy.__version__}")
    except ImportError:
        print("numpy       : 缺失! 先 pip install numpy")
    try:
        import serial

        print(f"pyserial    : {serial.__version__}  -> --backend serial 可用")
    except ImportError:
        print("pyserial    : 未安装 (只有接单片机时才需要: pip install pyserial)")
    print(f"管理员权限  : {'是' if _is_admin() else '否'}  (读写主板 Super I/O 需要)")

    try:
        import serial.tools.list_ports as lp

        ports = list(lp.comports())
        if ports:
            for p in ports:
                print(f"串口        : {p.device}  {p.description}")
        else:
            print("串口        : 没有找到 (板子插上了吗?)")
    except Exception:
        print("串口        : 无法枚举 (pyserial 未装)")

    guesses = [
        r"C:\Program Files\LibreHardwareMonitor\LibreHardwareMonitorLib.dll",
        r"C:\Program Files (x86)\LibreHardwareMonitor\LibreHardwareMonitorLib.dll",
    ]
    found = [g for g in guesses if os.path.exists(g)]
    appdata = os.environ.get("LOCALAPPDATA", "")
    if appdata:
        found += glob.glob(os.path.join(appdata, "FanControl", "*"))
    print(f"风扇软件    : {' 找到 ' + str(len(found)) + ' 项' if found else '没找到 LibreHardwareMonitor / FanControl'}")
    for f in found[:5]:
        print(f"              {f}")
    print(f"nbfc        : {shutil.which('nbfc') or '未安装'}")
    print()
    print("下一步: python -m fan_music render scores/ode_to_joy.fscore  (先听仿真)")
    return 0
