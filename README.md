**English** | [简体中文](README.zh.md)

# fan_studio — turn fans and motors into instruments

[![Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-24292f)](#download)
[![License: MIT](https://img.shields.io/badge/license-MIT-f2a33a)](LICENSE)

fan_studio is an open-source desktop editor for making music with cooling fans,
brushless motors, steppers, servos, pumps, and other physical devices. It maps
pitch to the frequency at which a motor repeats a physical event, then computes
the RPM and duty cycle needed to produce that note.

- Official site: <https://fanstudio.bony-doorframe-shortly.top>
- Chinese site: <https://fanstudio.bony-doorframe-shortly.top/zh/>
- Public API: <https://fan-api.bony-doorframe-shortly.top/v1/health>
- Downloads: <https://fan-download.bony-doorframe-shortly.top/win>
- Releases: <https://github.com/etyui508/fan-studio/releases>
- Issues: <https://github.com/etyui508/fan-studio/issues>

## Download

The Windows package contains the editor, sample scores, the motor library,
offline documentation, and the local Python arranger used by MIDI import.

```text
https://fan-download.bony-doorframe-shortly.top/win
```

Extract the ZIP and run `fan_studio.exe`. Python is only required for MIDI
arrangement and command-line rendering.

## Why fans?

A fan does not work like a speaker. Its pitch comes from repeating pressure
pulses as blades pass through the air:

```text
blade passing frequency = RPM / 60 × blade count
RPM = target frequency × 60 / blade count
```

For example, a 7-blade fan needs `440 × 60 / 7 = 3771 RPM` to produce an
audible A4 fundamental. Motors with many poles, commutator segments, rotor
slots, or step pulses can produce higher pitch through those events instead.

The project uses the same event-frequency model across 40 motor presets in 16
physical families. It also models inertia and schedules acceleration so a note
finishes changing speed at its start time rather than arriving late.

## Desktop editor

The WinForms editor includes:

- A searchable motor library with stable per-motor colors
- A zoomable piano roll with drag, resize, marquee selection, and undo
- A text-based pitch ruler with frequency and required-RPM hover readouts
- Click-to-audition pitch labels and range warnings
- A physical inspector for blades, harmonics, RPM limits, inertia, noise, pan,
  gain, and placement delay
- Real-time concurrency and timing-risk diagnostics
- MIDI import through the existing Python arranger
- English UI by default, with a Chinese option stored locally
- First-run Terms of Use and Privacy Policy confirmation

### Build

```powershell
cd studio
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\fan_studio.exe
```

### Checks

```powershell
.\fan_studio.exe --selftest
.\fan_studio.exe --uitest
.\fan_studio.exe --scorecheck
.\fan_studio.exe --snapshot preview.png ..\scores\bad_apple_6motors_45s.fscore
```

## Command-line tools

```powershell
python -m fan_music render scores/ode_to_joy.fscore
python -m fan_music show scores/ode_to_joy.fscore
python -m fan_music play scores/bad_apple_6motors_45s.fscore --backend serial --port COM3
python -m fan_music midi song.mid
python -m fan_music import-midi song.mid --voices 4 --duration 45 --render
```

### Routes to real hardware

| Route | Hardware | Notes |
|---|---|---|
| A | Arduino/ESP32 + external 4-pin fan | Recommended; isolated and controllable |
| B | 12 V switching into a motor winding | Experimental; audible coil excitation |
| C | Existing motherboard fan controller | Often locked down by firmware and unsafe for CPU cooling |

Start with route A. Run `tools/fan_probe.ps1 -Heat 20` to check whether a
machine exposes temperature, tachometer, or fan control interfaces.

## The physics model

The renderer follows a causal chain:

1. Map pitch to a motor event frequency.
2. Convert the event frequency to target RPM.
3. Plan a bounded, inertia-aware speed transition.
4. Generate harmonics and airflow noise from instantaneous RPM.
5. Apply stereo placement, micro-delay, reverb, and normalization.
6. Run a per-note FFT pitch check against the score.

The preview is a simulation, not a recording. Real fans add bearing noise,
resonance, blade geometry, and electrical behavior that no model can fully
capture.

## The .fscore format

One voice represents one motor:

```ini
bpm = 120

[voice lead]
blades = 7
harmonic = 2
min_rpm = 900
max_rpm = 3400
glide = 0.08
start_duty = 0.18
pan = -0.45
delay_ms = 3
gain = 1.0
A4:1 C5:1 E5:2

[voice bass]
blades = 7
harmonic = 1
min_rpm = 450
max_rpm = 1800
pan = 0.45
A2:2 E3:2
```

Voice names can be arbitrary. The desktop editor also supports legacy
single-motor files with no `[voice ...]` header.

## MIDI arranging

Ordinary MIDI usually has far more notes and pitch range than a motor ensemble
can reproduce. `import-midi`:

1. Extracts melody, harmony, alto, and bass layers.
2. Moves each layer by octaves into the selected motor's range.
3. Resamples around each motor's settling time.

The result is a `.fscore` project that can be inspected and edited in the
desktop app.

## Public API

The public API is read-only and requires no key:

```text
GET /v1/health
GET /v1/latest
GET /v1/motors
GET /v1/scores
GET /v1/links
```

The service uses only the Python standard library. Deployment units are in
[`deploy/systemd`](deploy/systemd).

## Safety

Speed, duty cycle, range, and 3.5τ values in the software are simulation and
editing parameters, not motor ratings or safe limits. Real hardware can be
damaged or cause injury through overspeed, runaway, bearing or blade failure,
overheating, or electrical faults. Never use the software's values directly as
hardware drive limits.

## Changelog

See [CHANGELOG.md](CHANGELOG.md). Detailed release notes are maintained from the
next release onward.

## User documents

- [English user guide](docs/en/USER_GUIDE.md)
- [English terms](docs/en/TERMS.md)
- [English privacy policy](docs/en/PRIVACY.md)
- [Chinese user guide](studio/使用说明.md)
- [Chinese terms](studio/用户协议.md)
- [Chinese privacy policy](studio/隐私政策.md)

## License

Source code and project documentation are provided under the
[MIT License](LICENSE), copyright 2026 etyui508.
Third-party components are listed in
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Maintainer: [etyui508](https://github.com/etyui508)
