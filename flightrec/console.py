"""Console helpers for the command-line tools."""

from __future__ import annotations

import sys


def safe_output() -> None:
    """Replace characters the console cannot show instead of crashing.

    Windows consoles often use a legacy code page (e.g. cp1252), and text from outside
    services (Discogs titles, recognition results) can contain characters it lacks, such as
    zero-width spaces, which would otherwise raise UnicodeEncodeError mid-run.
    """
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(errors="replace")
