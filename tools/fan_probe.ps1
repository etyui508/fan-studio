#requires -Version 5.1
<#
.SYNOPSIS
    fan_probe.ps1 — 给你的机器做风扇体检: 能不能读温度、能不能读转速、能不能控转速。

.DESCRIPTION
    "用风扇放音乐"这类项目的第一步不是写代码, 而是搞清楚这台机器到底把风扇藏多深。
    绝大多数笔记本(包括 Lenovo 小新这类消费级机型)在用户态是完全封闭的: 没有转速、
    没有真实温度、也没有厂商控制接口, 唯一能在内核驱动层碰它的工具(WinRing0 系)
    又普遍被 Windows 11 的内存完整性拉黑。

    这个脚本只做只读查询, 不需要管理员, 不改任何设置。

.PARAMETER Heat
    跑多少秒满载测试(默认 0 = 不跑)。大于 0 时会烧 CPU 让你听风扇, 用这个来验证
    温度读数是不是真的、以及能听到风扇换挡。

.EXAMPLE
    .\tools\fan_probe.ps1
    .\tools\fan_probe.ps1 -Heat 20
#>
[CmdletBinding()]
param(
    [ValidateRange(0, 300)]
    [int]$Heat = 0
)

$ErrorActionPreference = 'SilentlyContinue'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch {}

function Read-Temp {
    $s = Get-Counter '\Thermal Zone Information(*)\Temperature' -ErrorAction SilentlyContinue
    if (-not $s -or -not $s.CounterSamples) { return $null }
    $valid = $s.CounterSamples | Where-Object { $_.CookedValue -gt 200 }
    if (-not $valid) { return $null }
    return ($valid | Measure-Object CookedValue -Average).Average - 273.15
}

function Say($text) { Write-Host $text }
function Head($text) { Write-Host ""; Write-Host "== $text ==" -ForegroundColor Cyan }

$verdict = [ordered]@{ temp = 'no data'; rpm = 'no data'; control = 'no data' }
$notes = New-Object System.Collections.Generic.List[string]

# ---------------------------------------------------------------- 1. 机器
Head '机器'
$cs = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
$bb = Get-CimInstance Win32_BaseBoard
$se = Get-CimInstance Win32_SystemEnclosure
$isLaptop = ($se.ChassisTypes -contains 10) -or ($se.ChassisTypes -contains 9) -or ($cs.PCSystemType -eq 2)
Say ("  厂商/型号 : {0} {1}" -f $cs.Manufacturer, $cs.Model)
Say ("  主板      : {0} / {1}" -f $bb.Manufacturer, $bb.Product)
Say ("  形态      : {0}" -f $(if ($isLaptop) { '笔记本/移动设备' } else { '台式机' }))
Say ("  CPU       : {0}" -f (Get-CimInstance Win32_Processor).Name)
Say ("  系统      : {0} build {1}" -f $os.Caption, $os.BuildNumber)
$isAdmin = $false
try {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
} catch {}
Say ("  管理员    : {0}" -f $isAdmin)

# ---------------------------------------------------------- 2. 内核驱动闸门
Head '内核驱动闸门 (VBS / 内存完整性)'
$hvci = $false
$dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard
if ($dg) {
    $running = @($dg.SecurityServicesRunning)
    $hvci = $running -contains 2
    Say ("  VBS 状态        : {0}   (2 = 运行中)" -f $dg.VirtualizationBasedSecurityStatus)
    Say ("  已运行的服务    : {0}   (1 = Credential Guard, 2 = HVCI/内存完整性)" -f ($running -join ','))
    if ($hvci) {
        Say "  => 内存完整性开着: WinRing0 / inpoutx64 这类驱动会被拒绝加载,"
        Say "     LibreHardwareMonitor / OpenHardwareMonitor / FanControl 的读转速和控转速都走这条路。"
        $notes.Add('HVCI 开启, WinRing0 系驱动无法加载(读转速/控转速的常规路子被封)')
    }
} else {
    Say "  读不到 DeviceGuard 信息"
}

# ---------------------------------------------------------- 3. 系统风扇接口
Head '系统自带的风扇接口'
$fans = @(Get-CimInstance Win32_Fan)
if ($fans.Count -eq 0) {
    Say "  Win32_Fan: 空 —— 固件没有暴露 SMBIOS Type 29 风扇设备。"
} else {
    foreach ($f in $fans) {
        Say ("  Win32_Fan: Name={0}  DeviceID={1}  ActiveCooling={2}  VariableSpeed=[{3}]  DesiredSpeed=[{4}]" -f `
                $f.Name, $f.DeviceID, $f.ActiveCooling, $f.VariableSpeed, $f.DesiredSpeed)
    }
    $hasSpeed = @($fans | Where-Object { $_.DesiredSpeed -or $_.VariableSpeed }).Count -gt 0
    if ($hasSpeed) { $verdict.rpm = 'maybe' } else { $notes.Add('Win32_Fan 只有占位设备, 没有转速字段') }
}

# ---------------------------------------------------------------- 4. 温度
Head '温度读数'
$tz = Get-Counter '\Thermal Zone Information(*)\Temperature'
if ($tz -and $tz.CounterSamples) {
    foreach ($s in $tz.CounterSamples) {
        Say ("  {0} = {1} C" -f $s.InstanceName, [math]::Round($s.CookedValue - 273.15, 1))
    }
    $t0 = Read-Temp
    if ($null -ne $t0) { $verdict.temp = 'present' }
} else {
    Say "  没有 Thermal Zone 计数器"
}
$acpi = @(Get-CimInstance -Namespace root\wmi -ClassName MSAcpi_ThermalZoneTemperature)
Say ("  MSAcpi_ThermalZoneTemperature 实例数 = {0}" -f $acpi.Count)

# ------------------------------------------------------ 5. 厂商/OEM 接口
Head '厂商控制接口'
$vendorHits = @()
$nsList = @('root/wmi', 'root/cimv2')
try {
    $nsList += ((Get-CimInstance -Namespace root -ClassName __Namespace).Name | ForEach-Object { "root/$_" })
} catch {}
foreach ($ns in $nsList) {
    Get-CimClass -Namespace $ns -ErrorAction SilentlyContinue |
        Where-Object { $_.CimClassName -match 'GAMEZONE|LENOVO|ASUS|DELL|HP_|MSI_|ACER|EC_|FAN' } |
        ForEach-Object { $vendorHits += "$ns :: $($_.CimClassName)" }
}
$vendorHits = $vendorHits | Sort-Object -Unique
if (-not $vendorHits) {
    Say "  没找到任何 OEM 风扇控制类。"
} else {
    $vendorHits | ForEach-Object { Say "  $_" }
    if ($vendorHits -match 'GAMEZONE') {
        Say "  => 有 LENOVO_GAMEZONE_DATA(拯救者系列): 可能可以读风扇转速甚至直接设满速。"
        $verdict.rpm = 'maybe'
        $verdict.control = 'maybe'
    }
}

# ------------------------------------------------------ 6. 已装工具/驱动
Head '已安装的工具与驱动'
$paths = @(
    "$env:ProgramFiles\LibreHardwareMonitor", "${env:ProgramFiles(x86)}\LibreHardwareMonitor",
    "$env:ProgramFiles\OpenHardwareMonitor", "$env:ProgramFiles\HWiNFO64",
    "$env:ProgramFiles\MSI Afterburner", "$env:ProgramFiles\ArmouryCrate",
    "$env:LOCALAPPDATA\FanControl"
)
$foundTool = $false
foreach ($p in $paths) { if (Test-Path $p) { Say "  工具: $p"; $foundTool = $true } }
if (-not $foundTool) { Say "  没有任何第三方风扇工具。" }
foreach ($d in @(
        "$env:SystemRoot\System32\drivers\WinRing0x64.sys",
        "$env:SystemRoot\System32\inpoutx64.dll",
        "$env:SystemRoot\System32\AsusWinIO64.dll")) {
    if (Test-Path $d) { Say "  驱动: $d" }
}
$nbfc = (Get-Command nbfc.exe -ErrorAction SilentlyContinue).Source
Say ("  nbfc      : {0}" -f $(if ($nbfc) { $nbfc } else { '未安装' }))
$smi = (Get-Command nvidia-smi.exe -ErrorAction SilentlyContinue).Source
Say ("  nvidia-smi: {0}" -f $(if ($smi) { $smi } else { '无(独显风扇也走不了这条路)' }))

# ---------------------------------------------------------- 7. 满载测试
if ($Heat -gt 0) {
    Head "满载测试 ${Heat}s (现在听风扇)"
    $before = Read-Temp
    $jobs = 1..([Environment]::ProcessorCount) | ForEach-Object {
        Start-Job -ArgumentList $Heat -ScriptBlock {
            param($secs)
            $end = (Get-Date).AddSeconds($secs)
            $x = 1.0
            while ((Get-Date) -lt $end) { for ($k = 0; $k -lt 20000; $k++) { $x = $x * 1.0000001 + 1e-9 } }
        }
    }
    $peak = if ($null -ne $before) { $before } else { $null }
    for ($i = 0; $i -lt [math]::Ceiling($Heat / 2); $i++) {
        Start-Sleep -Seconds 2
        $t = Read-Temp
        if ($null -ne $t) {
            if ($null -eq $peak -or $t -gt $peak) { $peak = $t }
            Say ("  +{0,2}s  温度 {1,5:N1} C" -f (($i + 1) * 2), $t)
        }
    }
    $jobs | Wait-Job -Timeout ($Heat + 10) | Out-Null
    $jobs | Remove-Job -Force
    Start-Sleep -Seconds 2
    $after = Read-Temp
    if ($null -ne $before -and $null -ne $after) {
        $delta = $after - $before
        Say ("  起始 {0:N1} C -> 峰值 {1:N1} C -> 结束 {2:N1} C (变化 {3:+0.0;-0.0} C)" -f $before, $peak, $after, $delta)
        if ([math]::Abs($delta) -lt 0.5) {
            Say "  => 温度几乎没动。这个热区基本可以判定是假值(dummy zone), 读不到真实温度。"
            $verdict.temp = 'fake'
            $notes.Add('ACPI 热区是 dummy 区, 满载不变, 没有真实温度')
        } else {
            $verdict.temp = 'live'
            $notes.Add('温度读数是真的(满载有响应)')
        }
    }
} else {
    Say ""
    Say "  想验证温度真假、顺便听风扇换挡: .\tools\fan_probe.ps1 -Heat 20"
}

# ---------------------------------------------------------------- 8. 结论
Head '结论'
function Mark($v) {
    switch ($v) {
        'live' { 'OK  可用' }
        'present' { '??  有读数, 未验证真假' }
        'fake' { 'X   假值' }
        'maybe' { '??  有接口, 需实测' }
        'yes' { 'OK  可用' }
        default { 'X   不可用' }
    }
}
Say ("  读真实温度 : {0}" -f (Mark $verdict.temp))
Say ("  读风扇转速 : {0}" -f (Mark $verdict.rpm))
Say ("  控风扇转速 : {0}" -f (Mark $verdict.control))
if ($isLaptop) {
    Say ""
    Say "  这是笔记本: 风扇由 EC(嵌入式控制器) 独占管理, 固件里没有公开的寄存器表。"
    Say "  除了厂商自己的服务(联想电脑管家 / Fn+Q 换挡), 用户态基本碰不到它。"
}
if ($notes.Count -gt 0) {
    Say ""
    Say "  依据:"
    $notes | ForEach-Object { Say "    - $_" }
}
Say ""
Say "  能听到风扇换挡的零成本办法: 按 Fn+Q 切散热模式(智能/性能/安静), 音量立刻变。"
Say "  想真正「用风扇放音乐」, 走外置 4-pin 风扇 + 单片机那条路: 见 README 第 5 节。"
