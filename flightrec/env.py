"""Load API keys from untracked ``.env.local`` / ``.env`` files (no dependency).

Format: ``KEY=value`` per line; blank lines and ``#`` comments are ignored; values may be
wrapped in single or double quotes; empty values are skipped. Precedence: variables
already in the environment (a shell export), then ``.env.local``, then ``.env``. Both
files are git-ignored. Never print or log the values.
"""

from __future__ import annotations

import os
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_FILES = (".env.local", ".env")


def load_env(path: str | Path | None = None) -> list[str]:
    """Load ``path``, or by default the repo's ``.env.local`` then ``.env``; return the
    names that were set."""
    if path is None:
        return [name for f in DEFAULT_FILES for name in load_env(REPO_ROOT / f)]
    path = Path(path)
    if not path.exists():
        return []
    loaded = []
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key = key.strip().removeprefix("export ").strip()
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
            value = value[1:-1]
        elif " #" in value:
            value = value.split(" #", 1)[0].rstrip()
        if key and value and key not in os.environ:
            os.environ[key] = value
            loaded.append(key)
    return loaded
