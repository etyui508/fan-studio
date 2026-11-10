# fan_studio Privacy Policy

Version 1.1 · Effective 2026-11-10

fan_studio is local-first. It has no account system, cloud sync, telemetry,
advertising SDK, or usage analytics, and does not upload projects, MIDI, audio,
device information, or activity logs.

Files are read only when you open a project, import MIDI, or pass a path on the
command line.

The software writes:

- `.fscore` files to the path you choose
- WAV previews to the operating-system temporary directory
- logs and test output to the program directory
- the accepted legal version to `%APPDATA%\fan_studio`

MIDI import starts local Python with `python -m fan_music import-midi`; files are
not uploaded by that process.

The desktop editor does not initiate network requests. Third-party command
backends, synced folders, and OS services follow their own policies.

This version has no autosave or crash recovery. If those features are added,
their storage locations will be documented here.

Privacy questions: <https://github.com/etyui508/fan-studio/issues>
