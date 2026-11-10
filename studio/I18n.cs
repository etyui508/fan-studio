// I18n.cs — persistent UI language and English labels for fan_studio.

using System;
using System.Collections.Generic;
using System.IO;

namespace FanStudio
{
    public enum UiLanguage
    {
        English,
        Chinese
    }

    public static class AppSettings
    {
        public static UiLanguage Language = UiLanguage.English;

        static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ApplicationData), "fan_studio");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static void Load()
        {
            Language = UiLanguage.English;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (string line in File.ReadAllLines(SettingsPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (line.Substring(0, eq).Trim().Equals("language", StringComparison.OrdinalIgnoreCase))
                    {
                        string value = line.Substring(eq + 1).Trim();
                        Language = value.Equals("zh", StringComparison.OrdinalIgnoreCase) ||
                            value.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                            ? UiLanguage.Chinese : UiLanguage.English;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath,
                    "language=" + (Language == UiLanguage.Chinese ? "zh-CN" : "en") + Environment.NewLine);
            }
            catch { }
        }

        public static void SetLanguage(UiLanguage language)
        {
            Language = language;
            Save();
        }
    }

    public static class I18n
    {
        static readonly Dictionary<string, string> Ui = new Dictionary<string, string>
        {
            { "电机库", "Motor library" },
            { "拖入卷帘", "Drag into roll" },
            { "搜索型号 / 族系 / 角色", "Search model / family / role" },
            { "没有匹配的可定音高电机", "No matching pitched motors" },
            { "检查器", "Inspector" },
            { "上下文", "Context" },
            { "当前声部", "Current voice" },
            { "选择声部  ▼", "Select voice  ▼" },
            { "没有选中内容", "Nothing selected" },
            { "在卷帘里点一个音符，或从左边的电机库拖一台电机进来。", "Select a note in the piano roll, or drag a motor in from the library." },
            { "库型号", "Library motor" },
            { "型号", "Model" },
            { "族系", "Family" },
            { "机理", "Mechanism" },
            { "角色", "Role" },
            { "音域", "Range" },
            { "最短音符", "Minimum note" },
            { "惯性 τ", "Inertia τ" },
            { "拖入卷帘 / 双击添加到当前拍位", "Drag into the roll / double-click to add at the playhead" },
            { "选中的音符", "Selected note" },
            { "电机", "Motor" },
            { "音高", "Pitch" },
            { "起始拍", "Start beat" },
            { "长度", "Length" },
            { "谐波", "Harmonic" },
            { "叶片通过频率", "Blade-pass frequency" },
            { "目标转速", "Target RPM" },
            { "占空比", "Duty cycle" },
            { "声部", "Voice" },
            { "音符", "Notes" },
            { "拍位", "Beats" },
            { "操作", "Actions" },
            { "试听声部", "Audition voice" },
            { "删除声部", "Delete voice" },
            { "电机参数", "Motor parameters" },
            { "每转事件数", "Events / revolution" },
            { "谐波次数", "Harmonic order" },
            { "最低转速 rpm", "Min RPM" },
            { "最高转速 rpm", "Max RPM" },
            { "启动占空比", "Start duty" },
            { "惯性 τ (ms)", "Inertia τ (ms)" },
            { "谐波衰减", "Harmonic falloff" },
            { "噪声中心 Hz", "Noise center (Hz)" },
            { "噪声量", "Noise amount" },
            { "声像 -1…1", "Pan (-1…1)" },
            { "音量", "Gain" },
            { "摆位延迟 ms", "Placement delay (ms)" },
            { "参数直接改变物理模型。改动会进入撤销历史，并立即重算音域、换速时间与风险。", "These parameters drive the physical model. Changes are undoable and immediately recompute range, settling time and warnings." },
            { "快捷键", "Shortcuts" },
            { "同时发声数", "Voices" },
            { "适应内容", "Fit content" },
            { "把左侧电机拖进这里，或双击创建声部", "Drag a motor here, or double-click to create a voice" },
            { "已选 ", "Selected " },
            { "个音符", " notes" }
        };

        static readonly Dictionary<string, string> Families = new Dictionary<string, string>
        {
            { "轴流风机", "Axial fan" },
            { "离心风机", "Centrifugal fan" },
            { "电刷直流", "Brushed DC" },
            { "永磁直流", "PM DC" },
            { "励磁直流", "Field-wound DC" },
            { "无刷直流", "Brushless DC" },
            { "步进电机", "Stepper" },
            { "伺服舵机", "Servo" },
            { "偏心转子", "Eccentric rotor" },
            { "隔膜泵", "Diaphragm pump" },
            { "交流感应", "AC induction" },
            { "同步电机", "Synchronous" },
            { "磁阻电机", "Reluctance" },
            { "直驱专用", "Direct drive" },
            { "直线电磁", "Linear electromagnetic" },
            { "压电", "Piezo" },
            { "自定义声部", "Custom voice" }
        };

        static readonly Dictionary<string, string> Semantics = new Dictionary<string, string>
        {
            { "叶片通过", "Blade passing" },
            { "换向片切换", "Commutator switching" },
            { "换相", "Commutation" },
            { "步进脉冲", "Step pulses" },
            { "偏心块旋转", "Eccentric rotation" },
            { "单向阀开合", "Valve pulses" },
            { "转子槽通过", "Rotor-slot passing" },
            { "极对数换相", "Pole-pair commutation" },
            { "相开关频率", "Phase switching" },
            { "内部电机换向", "Internal commutation" },
            { "位置更新率", "Position update" },
            { "电流环/载波", "Current loop / carrier" },
            { "驱动频率", "Drive frequency" },
            { "参数化声部", "Parameterized voice" }
        };

        static readonly Dictionary<string, string> Roles = new Dictionary<string, string>
        {
            { "主旋律", "Lead" }, { "副旋律", "Harmony" }, { "和声", "Chords" },
            { "内声部", "Inner voice" }, { "低音", "Bass" }, { "超低音/踏板", "Sub bass / pedal" },
            { "强奏", "Accent" }, { "中高音", "Upper mid" }, { "高音铃", "High bell" },
            { "高音", "Treble" }, { "超高音", "Ultra high" }, { "中低音", "Low mid" },
            { "中音", "Mid" }, { "宽音域", "Wide range" }, { "高音/咔哒", "Treble / click" },
            { "中音/咔哒", "Mid / click" }, { "低音嗡嗡", "Low hum" }, { "超低音", "Sub bass" },
            { "中低音/踏板", "Low mid / pedal" }, { "自定义", "Custom" }
        };

        static readonly Dictionary<string, string> Models = new Dictionary<string, string>
        {
            { "fan40", "40 mm high-speed fan" }, { "fan60", "60 mm mid-speed fan" },
            { "fan80", "80 mm case fan" }, { "fan92", "92 mm case fan" },
            { "fan120", "120 mm quiet fan" }, { "fan140", "140 mm high-airflow fan" },
            { "fan12038", "12038 server fan" }, { "centrifugal", "Centrifugal fan (squirrel cage)" },
            { "blower", "Turbine blower" }, { "laptop", "Laptop thin-blade blower" },
            { "motor130", "130 toy motor" }, { "motor540", "540 RC motor" },
            { "coreless", "Coreless motor" }, { "pmdc", "PM DC gear motor" },
            { "shunt", "Shunt DC motor" }, { "series", "Series DC motor" },
            { "compound", "Compound DC motor" }, { "fpv", "FPV motor 12N14P" },
            { "gimbal", "Gimbal motor 12N14P" }, { "nema17", "NEMA17 stepper" },
            { "stepper28byj", "28BYJ-48 geared stepper" }, { "erm", "Eccentric rotating mass" },
            { "pump", "Diaphragm pump" }, { "cage", "Squirrel-cage induction motor" },
            { "wound_rotor", "Wound-rotor induction motor" }, { "sync", "Synchronous motor" },
            { "pmsm", "PMSM" }, { "wound_sync", "Wound-field synchronous motor" },
            { "srm", "Switched reluctance motor" }, { "synrm", "Synchronous reluctance motor" },
            { "torque_motor", "Direct-drive torque motor" }, { "servo_cont", "Continuous-rotation servo" },
            { "servo_digital", "Digital servo (333 Hz)" }, { "servo_mg996", "MG996R metal-gear servo" },
            { "servo", "SG90 position servo" }, { "servo_industrial", "Industrial servo + drive" },
            { "voicecoil", "Voice coil / electromagnet" }, { "linear_motor", "Linear motor" },
            { "piezo", "Piezo / ultrasonic motor" }, { "vfd", "AC motor + VFD" }
        };

        public static bool IsEnglish { get { return AppSettings.Language == UiLanguage.English; } }

        public static string L(string chinese, string english)
        {
            return IsEnglish ? english : chinese;
        }

        public static string UI(string chinese)
        {
            if (!IsEnglish || string.IsNullOrEmpty(chinese)) return chinese;
            string value;
            return Ui.TryGetValue(chinese, out value) ? value : chinese;
        }

        public static string MotorFamily(string value)
        {
            return Translate(Families, value);
        }

        public static string MotorSemantics(string value)
        {
            return Translate(Semantics, value);
        }

        public static string MotorRole(string value)
        {
            return Translate(Roles, value);
        }

        public static string MotorModel(FanStudio.Motor motor)
        {
            if (motor == null) return IsEnglish ? "Unassigned" : "未指定";
            string value;
            if (IsEnglish && Models.TryGetValue(motor.Key ?? "", out value)) return value;
            return motor.Model;
        }

        static string Translate(Dictionary<string, string> map, string value)
        {
            if (!IsEnglish || string.IsNullOrEmpty(value)) return value;
            string translated;
            return map.TryGetValue(value, out translated) ? translated : value;
        }
    }
}
