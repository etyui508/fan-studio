"""命令行入口: python -m fan_music {show,render,play,midi,doctor} ..."""

from __future__ import annotations

import argparse
import os
import sys

from . import arrange, audio, backends, engine, midi, motors, score as score_mod


def F(x):
    """数值 -> 精简字符串(整数不带小数点)。"""
    return ("%g" % float(x))


def _load(path, args):
    sc = score_mod.parse(path)
    for attr in ("harmonic", "glide", "blades", "min_rpm", "max_rpm"):
        value = getattr(args, attr, None)
        if value is None:
            continue
        for voice in sc.voices:      # 覆盖所有电机
            setattr(voice, attr, value)
    if getattr(args, "bpm", None) is not None:
        sc.bpm = args.bpm
    for voice in sc.voices:
        voice.pan = max(-1.0, min(1.0, float(voice.pan)))
    return sc


def _print_plan(p, sc, max_rows=16, show_all=False):
    print(f"{sc.title} — {sc.bpm:g}bpm, {len(sc.voices)} 台电机, "
          f"总时长 {p.duration:.2f}s, 准点率 {p.settle_ratio * 100:.0f}%")
    for v in sc.voices:
        print(f"  · {v}")
    print()

    buckets = p.by_voice()
    for vi, voice in enumerate(sc.voices):
        evs = buckets.get(vi, [])
        if not evs:
            continue
        print(f"[{voice.label}]  {len(evs)} 个音符")
        print("  #  音   拍  k   f_bp(Hz)  目标rpm  占空比  抢拍ms  备注")
        print("  " + "-" * 66)
        limit = len(evs) if show_all else min(max_rows, len(evs))
        for ev in evs[:limit]:
            print(f"{ev.index + 1:>3}  {ev.label:<4} {ev.beats:>4g} {ev.harmonic:>2} "
                  f"{ev.f_bp:>9.1f} {ev.rpm:>8.0f} {ev.duty * 100:>6.1f}% "
                  f"{ev.head_start * 1000:>6.0f}  {ev.warning}")
        if limit < len(evs):
            print(f"  … 还有 {len(evs) - limit} 个音 (加 --all 看全部)")
        print()

    if p.warnings:
        print(f"!! {len(p.warnings)} 处超出电机能力, 已夹到边界:")
        for w in p.warnings[:8]:
            print(f"   - {w}")
        if len(p.warnings) > 8:
            print(f"   … 还有 {len(p.warnings) - 8} 条")
        print()


def cmd_show(args):
    sc = _load(args.score, args)
    _print_plan(engine.plan(sc, anticipate=args.anticip), sc, show_all=args.all)
    return 0


def cmd_render(args):
    sc = _load(args.score, args)
    p = engine.plan(sc, anticipate=args.anticip)
    _print_plan(p, sc, show_all=args.all)

    out = args.out
    if not out:
        stem = os.path.splitext(os.path.basename(args.score))[0]
        out = os.path.join("out", stem + ".wav")
    os.makedirs(os.path.dirname(os.path.abspath(out)) or ".", exist_ok=True)

    sig, fs = audio.render(p, fs=args.fs, noise=args.noise, reverb=args.reverb)
    audio.write_wav(out, sig, fs)
    secs = sig.shape[-1] / fs if fs else 0
    print(f"已写出 {out}  ({secs:.2f}s, {fs}Hz, {sig.shape[0]} 声道, "
          f"{sig.shape[-1]} 采样)")

    if args.verify:
        rows, worst, detected, total = audio.verify(p, sig, fs)
        if rows:
            print()
            print("音准自检 (在目标频率窄带里找峰):")
            for voice, label, want, got, cents, prom in rows[:12]:
                flag = "  ok" if prom >= 2.5 else "  被盖住"
                print(f"  {voice:<8} {label:<4} 期望 {want:7.1f}Hz  实测 {got:7.1f}Hz  "
                      f"{cents:+7.1f} 音分  突出度 {prom:5.1f}{flag}")
            if len(rows) > 12:
                print(f"  … 共 {len(rows)} 个音")
            print(f"  检出 {detected}/{total} 个音, 最差偏差 {worst:+.1f} 音分")
    return 0


def cmd_play(args):
    sc = _load(args.score, args)
    p = engine.plan(sc, anticipate=args.anticip)
    _print_plan(p, sc, show_all=args.all)

    if args.voice >= len(sc.voices):
        raise SystemExit(f"没有第 {args.voice} 号电机, 这台机器上只有 {len(sc.voices)} 台")
    voice = sc.voices[args.voice]
    print(f"驱动 [{voice.label}] 这一台电机 ({voice.blades} 叶, "
          f"{voice.min_rpm:g}-{voice.max_rpm:g}rpm)")

    if args.backend == "serial":
        if not args.port:
            raise SystemExit("--backend serial 需要 --port COM3 这样的串口名")
        backend = backends.SerialBackend(args.port, args.baud)
    elif args.backend == "command":
        if not args.command_template:
            raise SystemExit('--backend command 需要 --cmd "...", 模板里用 {duty} 占位')
        backend = backends.CommandBackend(args.command_template)
    else:
        backend = backends.NullBackend()

    print(f"开始驱动 ({backend.name})…  Ctrl+C 停车")
    return backends.play(p, sc, backend, rate=args.rate, voice_index=args.voice)


def cmd_midi(args):
    mf = midi.read(args.path)
    print(midi.describe(mf))
    return 0


def cmd_motors(args):
    if args.csv:
        # 给 GUI(fan_studio.exe)读的纯数据格式, 单一数据源, 免得两份参数漂移
        print("key|model|family|semantics|blades|min_rpm|max_rpm|start_duty|glide|"
              "harmonic|timbre|noise_center|noise_gain|kind|role|note")
        for m in motors.MOTOR_CATALOG:
            print("|".join([
                m["key"], m["model"], m["family"], m["semantics"],
                str(m["blades"]),
                F(m["min_rpm"]), F(m["max_rpm"]), F(m["start_duty"]), F(m["glide"]),
                F(m["harmonic"] or 1), F(m["timbre"]), F(m["noise_center"]),
                F(m["noise_gain"]), m["kind"], m["role"],
                str(m.get("note", "")).replace("|", "/").replace("\n", " "),
            ]))
        return 0

    print("电机型号库 —— 每种电机是一个乐器。音高不是'转多快', 而是'每秒重复多少次某个周期事件':")
    print("  风扇=叶片扫过  电刷=换向片切换  无刷=换相  步进=步进脉冲  气泵=阀门开合")
    print()
    print(motors.table(show_all=not args.playable_only))
    print()
    print(motors.legend())
    print()
    print("  默认编制: " + " → ".join(motors.DEFAULT_ENSEMBLE))
    print("  --voices N 取前 N 台; --motors fan40,nema17,fpv,erm 可以自己点菜。")
    return 0


def cmd_import_midi(args):
    keys = args.motors.split(",") if args.motors else None
    if keys:
        voices = len(keys)          # 点名模式: 点几台就是几台
    else:
        voices = args.voices
        if voices < 1:
            raise SystemExit("--voices 至少 1 台 (1 台就是单旋律)")
    mf = midi.read(args.midi_path)
    dups = arrange.duplicate_groups(mf)
    if dups:
        n = sum(len(d) for d in dups)
        print(f"注意: {n} 条轨内容完全重复 (轨号 "
              f"{', '.join('/'.join(str(i) for i in d) for d in dups)}), 只取其中一条。")

    sc, stats = arrange.arrange(
        mf,
        start=args.start,
        duration=args.duration,
        voices=voices,
        step=args.step,
        bpm=args.tempo,
        track=args.track,
        keys=keys,
    )
    start, end = stats["window"]
    print(f"源轨: {stats['source_track']}   窗口 {start:.1f}s-{end:.1f}s   "
          f"{len(sc.voices)} 台电机")
    print()
    print("  声部      型号                  角色              移调     音符   音域          采样")
    print("  " + "-" * 88)
    for b in stats["bands"]:
        print(f"  {b['label']:<8} {b['model']:<20} {b['rule']:<14} {b['shift']:+5.0f} 八度  "
              f"{b['notes']:>5}   {b['range'][0]:>4} - {b['range'][1]:<4} {b['step'] * 1000:>4.0f}ms")
    models = {b["model"] for b in stats["bands"]}
    if len(models) < len(sc.voices):
        print(f"  注意: {len(sc.voices)} 台里只有 {len(models)} 种型号, 其余是循环复用的(名字带 -2 / -3)")
    if len(sc.voices) > 8:
        print(f"  提示: {len(sc.voices)} 台已经超出实用范围, 每台可用音域只有约 2 个八度, "
              f"声部会互相遮蔽; 对比一下 --voices 6 就知道差多少。")

    out = args.out or os.path.join(
        "scores", os.path.splitext(os.path.basename(args.midi_path))[0][:40] + ".fscore"
    )
    title = os.path.splitext(os.path.basename(args.midi_path))[0]
    text = arrange.to_fscore(
        sc,
        title=title,
        comment_lines=[
            f"由 MIDI 自动改编: {os.path.basename(args.midi_path)}",
            f"源轨 {stats['source_track']}, 窗口 {start:.1f}s-{end:.1f}s, "
            f"每层按 300ms 左右的周期重新采样",
            "每层整体移八度进入各自电机的可用音域(移八度不破坏和声)",
            "改音符直接改下面这些 音名:拍数 就行",
        ],
    )
    os.makedirs(os.path.dirname(os.path.abspath(out)) or ".", exist_ok=True)
    with open(out, "w", encoding="utf-8-sig") as fh:   # 带 BOM, 老版 PowerShell/记事本也不会乱码
        fh.write(text)
    print()
    print(f"已写出乐谱 {out}  ({len(text.splitlines())} 行)")

    if args.render:
        p = engine.plan(sc)
        wav = os.path.join("out", os.path.splitext(os.path.basename(out))[0] + ".wav")
        os.makedirs(os.path.dirname(os.path.abspath(wav)) or ".", exist_ok=True)
        sig, fs = audio.render(p, fs=args.fs, noise=args.noise, reverb=args.reverb)
        audio.write_wav(wav, sig, fs)
        rows, worst, detected, total = audio.verify(p, sig, fs)
        print(f"已写出 {wav}  ({sig.shape[-1] / fs:.2f}s, {sig.shape[0]} 声道)")
        print(f"音准自检: 检出 {detected}/{total} 个音, 最差 {worst:+.1f} 音分, "
              f"准点率 {p.settle_ratio * 100:.0f}%")
    return 0


def cmd_doctor(args):
    return backends.doctor()


def build_parser():
    p = argparse.ArgumentParser(
        prog="fan_music",
        description="用电脑风扇放音乐: 叶片通过频率 = 音高。",
    )
    sub = p.add_subparsers(dest="command", required=True)

    def common(sp):
        sp.add_argument("score", help="乐谱文件 (.fscore)")
        sp.add_argument("--harmonic", type=int, help="强制用第 k 次谐波 (默认按谱子, auto 自动挑)")
        sp.add_argument("--bpm", type=float)
        sp.add_argument("--glide", type=float, help="转速惯性时间常数(秒)")
        sp.add_argument("--blades", type=int)
        sp.add_argument("--min-rpm", dest="min_rpm", type=float)
        sp.add_argument("--max-rpm", dest="max_rpm", type=float)
        sp.add_argument("--anticip", type=float, default=engine.ANTICIPATE,
                        help="提前多少个 glide 常数开始换速(抢拍), 0 = 不抢")
        sp.add_argument("--all", action="store_true", help="打印全部音符行")

    for name, helptext in (
        ("show", "把音符翻译成转速/占空比, 不发声"),
        ("render", "渲染 WAV 预览 (多电机风扇声学仿真)"),
        ("play", "真的去驱动其中一台风扇"),
    ):
        sp = sub.add_parser(name, help=helptext)
        common(sp)
        if name == "render":
            sp.add_argument("-o", "--out", help="输出 wav 路径, 默认 out/<谱名>.wav")
            sp.add_argument("--fs", type=int, default=audio.DEFAULT_FS)
            sp.add_argument("--noise", type=float, default=0.85, help="气流噪声比例, 0 关闭")
            sp.add_argument("--reverb", type=float, default=0.14)
            sp.add_argument("--no-verify", dest="verify", action="store_false",
                            help="跳过 FFT 音准自检")
        if name == "play":
            sp.add_argument("--backend", choices=["null", "serial", "command"], default="null")
            sp.add_argument("--voice", type=int, default=0, help="驱动第几个声部的电机")
            sp.add_argument("--port", help="串口, 如 COM3")
            sp.add_argument("--baud", type=int, default=115200)
            sp.add_argument("--cmd", dest="command_template",
                            help='外部命令模板, 如 "nbfc set -s {duty}"')
            sp.add_argument("--rate", type=float, default=40.0, help="每秒下发次数")

    sp = sub.add_parser("midi", help="看一个 MIDI 文件里有什么 (轨道/音域/时长)")
    sp.add_argument("path", help="MIDI 文件路径")

    sp = sub.add_parser("import-midi", help="把 MIDI 改编成多电机风扇谱")
    sp.add_argument("midi_path", help="MIDI 文件路径")
    sp.add_argument("-o", "--out", help="输出 .fscore 路径")
    sp.add_argument("--voices", type=int, default=6,
                    help="用几台电机, 不设上限 (默认 6; 超出型号数会循环复用, 3~8 台最实用)")
    sp.add_argument("--motors", default=None,
                    help="自己指定型号, 逗号分隔, 如 fan40,nema17,fpv,erm")
    sp.add_argument("--step", type=float, default=None,
                    help="重采样周期(秒), 默认按每台电机的整定时间自动定 (不填更好)")
    sp.add_argument("--start", type=float, default=None, help="起始秒, 默认自动跳过前奏")
    sp.add_argument("--duration", type=float, default=45.0, help="取多少秒")
    sp.add_argument("--tempo", type=float, default=120.0, help="写进谱子的 BPM")
    sp.add_argument("--track", type=int, default=None, help="指定轨道号, 默认选音符最多的一条")
    sp.add_argument("--render", action="store_true", help="顺手渲染成 WAV")
    sp.add_argument("--fs", type=int, default=audio.DEFAULT_FS)
    sp.add_argument("--noise", type=float, default=0.85)
    sp.add_argument("--reverb", type=float, default=0.14)

    spm = sub.add_parser("motors", help="看电机型号库: 每种电机的音域/惯性/音色")
    spm.add_argument("--playable-only", action="store_true",
                     help="只列能直接编曲的型号")
    spm.add_argument("--csv", action="store_true",
                     help="输出竖线分隔的纯数据(给 fan_studio.exe 用)")

    sub.add_parser("doctor", help="检查环境: numpy/pyserial/串口/主板风扇工具")
    return p


def main(argv=None):
    args = build_parser().parse_args(argv)
    table = {
        "show": cmd_show,
        "render": cmd_render,
        "play": cmd_play,
        "midi": cmd_midi,
        "import-midi": cmd_import_midi,
        "motors": cmd_motors,
        "doctor": cmd_doctor,
    }
    try:
        return table[args.command](args)
    except KeyboardInterrupt:
        return 130


if __name__ == "__main__":
    sys.exit(main())
