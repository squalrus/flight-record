# Samples

Short test clips used by the experiments. **Short clips only (10-30 seconds). Never commit full copyrighted tracks.** Audio files here are stored with Git LFS (see `.gitattributes`). Anything you do not have the right to keep in a repo goes in `samples/_local/`, which is git-ignored.

Full songs for evaluating key and tempo go in `samples/_songs/` (also git-ignored), with their published key and BPM in `samples/_songs/truth.csv` (columns `file,key,bpm,source`). Run `python experiments/02-onset-features/evaluate.py`; it writes a template CSV the first time.

Feature logs from `tools/record_features.py` go to `samples/_local/logs/` by default (git-ignored with the rest of `_local/`).

## Manifest

Add one row per clip.

| File | Source (artist / album / side) | Genre | Pressing / condition | Length | Notes |
| --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |

## Recording guidance

- Record through the real signal chain (splitter → USB interface) with `tools/record_clip.py` so clips include real surface noise.
- Include variety: loud/quiet, steady tempo/rubato, a live album, a compilation, a worn record.
- Keep a note of input level; avoid clipping.
