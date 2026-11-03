# Notification cues

The three PCM WAV cues are project-owned audio under the repository's MIT license.
They are embedded in the desktop application and do not use Windows sound-scheme aliases.

| File | Pattern | Duration |
| --- | --- | --- |
| `waterdrop.wav` | Short descending water resonance | 500 ms |
| `soft-piano.wav` | Soft C5 and E5 piano tones with harmonic decay | 1200 ms |
| `warm-chord.wav` | Gentle C major seventh chord with a smooth attack | 1500 ms |

All files use mono, 44100 Hz, 16-bit PCM with smooth fade envelopes and a peak below -13 dBFS. Playback respects
Windows volume controls. The error option retains the Windows critical-error sound.

Run `python scripts/generate-notification-sounds.py` to reproduce the cues.
