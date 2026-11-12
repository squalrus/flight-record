# Tools

Shared helpers used by the experiments. Planned (see backlog item 0.2):

| Script | Purpose |
| --- | --- |
| `list_devices.py` | List audio input devices with channels and sample rates |
| `record_clip.py` | Record N seconds from a chosen device to WAV |
| `replay.py` | Play a WAV through the same code path as live capture, so any experiment runs without hardware |

Keep these dependency-light and cross-platform (development happens on Windows; the Pi is Linux).
