# fan_studio User Guide

## Start

Run `fan_studio.exe`. The default interface language is English. Change it with
the language button in the top-right toolbar; fan_studio restarts after saving
any pending project changes.

## Library and piano roll

- Search motors by model, family, or role.
- Drag a motor into the roll or double-click a card to add a voice.
- Double-click empty roll space to create a note.
- Drag notes to move, drag the right edge to resize, and drag empty space to
  marquee-select.
- Hover a pitch row to see frequency and the current motor's required RPM.
- Click a pitch name to audition it. Out-of-range notes only show a warning.

## View and editing

- Mouse wheel: vertical scroll
- Shift+wheel: horizontal scroll
- Ctrl+wheel: zoom
- Ctrl+0 or Fit content: fit notes into view
- Ctrl+Z / Ctrl+Y: undo / redo
- Ctrl+A: select all
- Ctrl+D: duplicate
- Delete: delete selection
- Shift+Left/Right: resize selection
- Ctrl+Up/Down: transpose one octave

## MIDI import

Open **Import MIDI**, choose the voice count, duration, and BPM. The local Python
arranger replaces the current project and can be undone with Ctrl+Z.

## Safety

Software speed, duty, range, and settling values are simulation/editing values,
not motor ratings or safe limits. Real hardware can be damaged or cause injury
through overspeed, runaway, bearing or blade failure, overheating, or electrical
faults.
