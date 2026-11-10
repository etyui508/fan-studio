// LegalDocuments.cs —— 首次启动协议、隐私政策与内置使用说明

namespace FanStudio
{
    public static class LegalDocuments
    {
        public const string Version = "1.1";
        public const string EffectiveDate = "2026-11-10";

        public const string Guide =
@"fan_studio 使用说明

一、启动与保存
直接运行 fan_studio.exe 即可。点击“打开 .fscore”载入已有工程，点击“保存”写回 .fscore。右上角的 “?” 可随时重新打开本说明、用户协议和隐私政策。

二、四区工作台
左侧是电机库。可按型号、族系或角色搜索；把卡片拖进卷帘，或双击卡片，即可在播放头位置添加声部。

中央是钢琴卷帘。横轴是节拍，纵轴是 MIDI 音高。双击空白处创建音符；拖动音符改变拍位和音高；拖动音符右边缘改变时值；在空白处拖拽可框选。

右侧是检查器。选中音符后可精确修改音高、起始拍和长度；下方可直接编辑叶片数、谐波、转速范围、惯性、噪声、声像和音量等物理参数。

底部是同时发声数。红色虚线代表 8 台参考线。超过该线不代表程序禁止播放，但低频互相遮蔽会更明显。

三、颜色与音高
同一台电机在型号库、卷帘音域带和音符上使用同一颜色。逻辑声部名（例如 lead、bass）仍按文件中的物理参数工作。

音高尺是深色文字标尺。缩小视图时只标 C 行；放大后显示更多音名。当前声部音域内的标签会使用该电机颜色，音域外标签变暗。

鼠标停在某一行时，会显示该音高、频率，以及当前声部要达到它所需的 RPM。点击左侧音高名可按当前声部试听；超出音域时只提示，不会发声。

四、视图操作
滚轮：上下滚动。
Shift+滚轮：左右滚动。
Ctrl+滚轮：缩放时间与音高行高。
“适应内容”：把时间范围和音高范围同时调整到当前项目有内容的位置。
选中另一台电机时，视图会检查其音域；若音域不在画面内，会自动滚动到该范围。

五、快捷键
Space：播放/停止
Ctrl+S：保存；Ctrl+Shift+S：另存为
Ctrl+O：打开工程
Ctrl+N：新建项目
Ctrl+I：导入 MIDI
Ctrl+F：搜索电机库
F1：使用说明、用户协议与隐私政策
F5：渲染并播放
Esc：停止播放；未播放时清除选择
Ctrl+Z / Ctrl+Y：撤销 / 重做
Ctrl+A：全选音符
Ctrl+D：复制选中音符
Delete：删除
方向键：音高上下移动、拍位左右移动
Shift+左右键：改变选中音符时值
Ctrl+上下键：整体移动一个八度
Home / End：播放头跳到开头 / 结尾
Ctrl+左右键：播放头前后跳一小节
Ctrl+0：适应内容
Ctrl+加号 / 减号：缩放
Ctrl+1…9：切换到第 1…9 个声部

在输入框内编辑搜索词或物理参数时，空格、方向键、Ctrl+A 和 Ctrl+Z/Y 会优先交给输入框。

六、物理约束
红色边框表示音域越界，或音符短于该电机的 3.5τ 整定时间。检查器会显示具体原因。
风扇不是扬声器：转速惯性、启动死区和低频遮蔽都是真实的物理限制。程序会在底部汇总时值与音域风险，但不会擅自改写音符。
软件中的转速、占空比、音域和整定值都是仿真/编辑参数，不是电机额定值或安全值。实机可能因超速、飞车、轴承或叶片强度不足而损坏或伤人。

七、MIDI 导入
点击“导入 MIDI”，选择文件后设置声部数、最长时长和工程 BPM。导入会调用项目自带的 Python 改编器；结果替换当前项目，但可用 Ctrl+Z 撤销。

八、文件与显示
.fscore 是纯文本工程格式，可直接用文本编辑器查看。WAV 预览写入系统临时目录。日志只写入程序目录，不会上传。

协议版本：" + Version + @"，生效日期：" + EffectiveDate;

        public const string Terms =
@"fan_studio 用户协议
版本 " + Version + @" · 生效日期 " + EffectiveDate + @"

1. 接受与适用范围
本协议适用于 fan_studio 电机乐器编辑器。首次启动时，点击“同意并继续”即表示你已阅读并接受本协议与《隐私政策》。如果你不同意，请点击“退出”并停止使用软件。

2. 软件用途
本软件用于编辑 .fscore 工程、模拟多电机声学效果、试听声音、导入 MIDI 以及生成预览音频。软件不保证任何具体硬件、风扇或电机能够达到仿真效果。

3. 用户责任
你应确保对所使用的 MIDI、乐谱、录音、电机参数和其他素材拥有合法使用权。不得利用本软件制作、传播违反当地法律法规的内容。将受版权保护的歌曲改编、渲染为音频后对外传播，仍由你自行取得所需授权并承担责任。

4. 硬件与安全
涉及真实电机、风扇、电源、驱动板或主板的操作存在噪声、过热、机械损伤和电气风险。你必须自行确认供电、绝缘、限流、固定和保护措施，并在安全环境中测试。
软件中的转速、占空比、音域及 3.5τ 整定值均为声学仿真和编辑参数，不是电机额定值或安全值。实机可能因超速、飞车、轴承或叶片强度不足、过热或电气故障而损坏或伤人。不得把软件中的数值直接当作硬件驱动上限。

5. 数据与文件
你对自己的工程文件负责。保存前请确认目标路径，重要工程请建立备份。软件不承诺恢复因误删、磁盘故障、断电或第三方工具造成的数据丢失。

6. 知识产权与开源许可
本软件源代码按 MIT License 提供，完整许可文本见项目根目录 LICENSE。第三方组件继续适用各自许可证，清单见 THIRD_PARTY_NOTICES.md。
用户乐谱、MIDI 素材和用户自行生成的声音结果归相应权利人所有。本协议不改变这些内容的权利归属。

7. 免责声明
软件按“现状”提供。在法律允许的范围内，项目维护者不对因使用或无法使用本软件造成的设备损坏、数据丢失、业务中断、人身伤害或间接损失承担责任。你可以依照 MIT License 修改和验证源码，但修改后的行为由修改者负责。
在法律不允许免责的范围内，本条不免除相应责任；因故意或重大过失造成的人身伤害不因本条被排除。

8. 适用法律与联系
本协议适用中华人民共和国大陆地区法律。争议应优先通过友好协商解决；协商不成的，依法向有管辖权的人民法院提出。
项目主页：https://github.com/etyui508/fan-studio
联系维护者：https://github.com/etyui508/fan-studio/issues
维护者：etyui508

9. 协议更新
协议发生重要变化时会更新版本号。新版本生效后，软件可再次请求确认。

协议版本：" + Version + @"，生效日期：" + EffectiveDate + @"。";

        public const string Privacy =
@"fan_studio 隐私政策
版本 " + Version + @" · 生效日期 " + EffectiveDate + @"

1. 本地优先
fan_studio 默认在本机运行，不包含账号系统、云端同步、遥测、广告 SDK 或使用行为统计。软件不会主动把你的工程、MIDI、音频、设备信息或操作记录上传到服务器。

2. 软件会读取什么
只有在你主动选择“打开 .fscore”“导入 MIDI”或从命令行传入文件时，软件才会读取对应文件。它们用于解析乐谱、显示编辑器和生成声音。

3. 软件会写入什么
保存工程时，软件把你指定的 .fscore 写入所选路径。
试听和渲染时，WAV 预览写入操作系统临时目录。
调试日志、崩溃日志和测试结果写入程序目录。
首次同意协议的版本记录写入当前用户的 APPDATA\fan_studio 目录。
当前版本没有自动保存或崩溃恢复功能。如果未来加入这些功能，本政策会同步注明自动保存文件的位置。

4. 外部程序
导入 MIDI 时，软件会在本机启动 python -m fan_music import-midi，并传入你选择的 MIDI 路径、声部数和输出路径。该进程由本机 Python 运行，不上传文件。

5. 网络
本软件本身不发起网络请求。若你使用第三方命令行后端、云盘同步目录或操作系统服务，其数据处理受对应第三方规则约束。

6. 保留与删除
工程文件由你管理。临时 WAV、程序目录日志和 APPDATA 中的同意记录可直接删除。删除 APPDATA\fan_studio 下的同意记录后，下次启动会重新显示协议。

7. 儿童与敏感信息
本工具不是面向儿童的在线服务，也不要求提供姓名、手机号、精确位置、生物识别或支付信息。请勿把不必要的个人敏感信息写入工程文件或日志。

8. 联系与变更
隐私政策发生重要变化时会更新版本号并在软件内展示。若你对本地数据处理有疑问，请通过 https://github.com/etyui508/fan-studio/issues 联系维护者，并检查程序目录、临时目录和 APPDATA\fan_studio 中的实际文件。

政策版本：" + Version + @"，生效日期：" + EffectiveDate + @"。";

        public const string GuideEn =
@"fan_studio User Guide

1. Start and save
Run fan_studio.exe. Use Open .fscore to load a project and Save to write it back. The ? button reopens this guide, the terms, and the privacy policy.

2. Workspace
The motor library is on the left. Search by model, family, or role, drag a card into the piano roll, or double-click a card to add a voice at the playhead.

In the piano roll, double-click empty space to create a note. Drag a note to change time and pitch. Drag its right edge to change duration. Drag empty space to marquee-select.

The inspector edits pitch, position, duration, and all physical motor parameters. The bottom panel shows concurrent voices and the 8-motor reference line.

3. Colors and pitch
A motor keeps the same color in the library, range overlay, and notes. The text-based pitch ruler labels C rows when zoomed out and more rows when zoomed in. Labels inside the selected voice range use the motor color.

Hover a pitch row to see its frequency and the RPM required by the current voice. Click a pitch name on the left to audition it. Notes outside the range only show a warning.

4. View controls
Mouse wheel: vertical scroll.
Shift+wheel: horizontal scroll.
Ctrl+wheel: zoom time and pitch rows.
Fit content: fit the project's time and pitch ranges.

5. Shortcuts
Space: play / stop
Ctrl+S: save; Ctrl+Shift+S: save as
Ctrl+O: open; Ctrl+N: new project
Ctrl+I: import MIDI; Ctrl+F: search motor library
F1: guide, licenses, and shortcuts
F5: render and play
Esc: stop playback; clear selection when stopped
Ctrl+Z / Ctrl+Y: undo / redo
Ctrl+A: select all notes; Ctrl+D: duplicate
Delete: delete selection
Arrow keys: nudge pitch or time
Shift+Left/Right: change duration
Ctrl+Up/Down: transpose one octave
Home / End: move playhead to start / end
Ctrl+0: fit content
Ctrl+1…9: select voice 1…9

Text fields keep their normal Space, arrow, selection, and undo behavior while editing.

6. Physical limits
Red outlines mean a note is outside the motor range or shorter than its 3.5τ settling time.
Speed, duty, range, and settling values are simulation/editing parameters, not motor ratings or safe limits. Real hardware can be damaged or cause injury through overspeed, runaway, bearing or blade failure.

7. MIDI import
Choose Import MIDI, then set voice count, duration, and project BPM. The local Python arranger replaces the current project; Ctrl+Z restores it.

8. Files
.fscore projects are plain text. WAV previews go to the system temporary directory. Logs stay in the program directory and are never uploaded.

Version " + Version + @", effective " + EffectiveDate;

        public const string TermsEn =
@"fan_studio Terms of Use
Version " + Version + @" · Effective " + EffectiveDate + @"

1. Acceptance and scope
These terms apply to fan_studio. On first launch, clicking Agree and Continue confirms that you have read and accept these terms and the Privacy Policy. If you do not agree, choose Exit and stop using the software.

2. Purpose
The software edits .fscore projects, simulates multi-motor acoustics, previews sound, imports MIDI, and renders audio. No specific motor or hardware is guaranteed to reproduce the simulation.

3. User responsibility
You must have permission to use every MIDI file, score, recording, motor parameter, and other input. Do not use the software for unlawful content. If you distribute an audio rendering of a copyrighted song, you remain responsible for obtaining the required rights.

4. Hardware and safety
Real motors, fans, power supplies, drivers, and motherboards create noise, heat, mechanical, and electrical risks. You are responsible for power limits, insulation, current limiting, mounting, enclosure, and safe testing.

Speed, duty cycle, pitch range, and 3.5τ settling values in the software are simulation/editing parameters, not motor ratings or safe limits. Real hardware may be damaged or cause injury through overspeed, runaway, bearing or blade failure, overheating, or electrical faults. Never use software values directly as hardware drive limits.

5. Data and files
You are responsible for project files. Confirm save paths and keep backups. The project does not promise recovery after accidental deletion, disk failure, power loss, or third-party tools.

6. Intellectual property and license
The source code is provided under the MIT License. See LICENSE in the project root. Third-party components remain under their own licenses; see THIRD_PARTY_NOTICES.md.
User scores, MIDI inputs, and user-generated audio remain the property of their respective owners.

7. Disclaimer
The software is provided as is. To the extent permitted by law, the maintainer is not liable for equipment damage, data loss, business interruption, personal injury, or indirect loss. You may modify and verify the source under the MIT License, but you are responsible for modified behavior.
Nothing in this section excludes liability where exclusion is prohibited. Liability for intentional misconduct or gross negligence causing personal injury is not excluded.

8. Governing law and contact
These terms are governed by the laws of the mainland People's Republic of China. Disputes should first be addressed through good-faith discussion and then through a court with jurisdiction.
Project: https://github.com/etyui508/fan-studio
Contact: https://github.com/etyui508/fan-studio/issues
Maintainer: etyui508

9. Updates
Material changes update the version. The software may ask you to accept a new version.

Version " + Version + @", effective " + EffectiveDate + @".";

        public const string PrivacyEn =
@"fan_studio Privacy Policy
Version " + Version + @" · Effective " + EffectiveDate + @"

1. Local-first operation
fan_studio runs locally. There is no account system, cloud sync, telemetry, advertising SDK, or usage analytics. The software does not upload projects, MIDI, audio, device information, or activity logs.

2. Data read
The software reads files only when you choose Open .fscore or Import MIDI, or when a path is passed on the command line.

3. Data written
Save writes the .fscore path you choose.
Preview and render write WAV files to the system temporary directory.
Debug, crash, and test logs are written to the program directory.
The accepted legal version is stored under the current user's APPDATA\fan_studio directory.
This version has no autosave or crash recovery. If those features are added, this policy will state their file locations.

4. External process
MIDI import starts local Python with `python -m fan_music import-midi`. File paths and settings are passed to that local process; files are not uploaded.

5. Network
The desktop editor does not initiate network requests. Third-party command backends, cloud-synced folders, and operating-system services follow their own policies.

6. Retention and deletion
You control project files. Temporary WAV files, logs, and the acceptance record can be deleted directly. Delete the record under APPDATA\fan_studio to show the agreement again.

7. Sensitive data
The software does not request names, phone numbers, precise location, biometrics, or payment data. Avoid writing unnecessary sensitive information into projects or logs.

8. Contact and changes
Material changes update the version and are shown in the app. For privacy questions, open an issue at https://github.com/etyui508/fan-studio/issues.

Version " + Version + @", effective " + EffectiveDate + @".";
    }
}
