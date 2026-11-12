"""List audio devices so you can pick one for FR_AUDIO_DEVICE or --device.

Usage: python tools/list_devices.py [--all]
"""

from __future__ import annotations

import argparse


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--all", action="store_true", help="include output-only devices")
    args = parser.parse_args(argv)

    import sounddevice as sd

    devices = sd.query_devices()
    hostapis = sd.query_hostapis()
    default_in = sd.default.device[0]

    print(f"{'':1} {'idx':>3}  {'in':>2}  {'out':>3}  {'rate':>6}  {'host API':<18} name")
    for index, dev in enumerate(devices):
        if not args.all and dev["max_input_channels"] == 0:
            continue
        mark = "*" if index == default_in else " "
        print(
            f"{mark} {index:>3}  {dev['max_input_channels']:>2}  {dev['max_output_channels']:>3}"
            f"  {int(dev['default_samplerate']):>6}  {hostapis[dev['hostapi']]['name']:<18}"
            f" {dev['name']}"
        )
    print("\n* = default input. Set FR_AUDIO_DEVICE to an index or a unique part of a name.")
    print("On Windows the same device is listed once per host API; try WASAPI first.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
