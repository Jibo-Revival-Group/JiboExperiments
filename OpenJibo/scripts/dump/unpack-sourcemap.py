#!/usr/bin/env python3
"""Unpacks the original TypeScript sources embedded in a robot .js.map file.

The stock Jibo services are browserify bundles shipped with their source maps,
and those maps still carry `sourcesContent` - i.e. the complete pre-compilation
TypeScript for services like jibo-ssm (SyncManager, KB clients, the notification
plumbing). Reading that is dramatically easier than reading the bundle.

Usage:
  unpack-sourcemap.py <file.js.map> [dest-dir]

Defaults to writing next to the map in a <bundle-name>.src/ directory. Paths are
sanitized so nothing can escape the destination.
"""

from __future__ import annotations

import json
import sys
from pathlib import Path


def safe_relative_path(source: str) -> Path:
    """Maps a source-map entry to a path that cannot escape the destination."""
    cleaned = source.replace("\\", "/").lstrip("/")
    parts = [part for part in cleaned.split("/") if part not in ("", ".", "..")]
    return Path(*parts) if parts else Path("unnamed")


def main(argv: list[str]) -> int:
    if len(argv) < 2:
        print(__doc__.strip(), file=sys.stderr)
        return 2

    map_path = Path(argv[1])
    if not map_path.is_file():
        print(f"error: no such source map: {map_path}", file=sys.stderr)
        return 1

    dest = Path(argv[2]) if len(argv) > 2 else map_path.with_suffix("").with_suffix(".src")

    data = json.loads(map_path.read_text(encoding="utf-8", errors="replace"))
    sources = data.get("sources") or []
    contents = data.get("sourcesContent") or []
    if not contents:
        print(f"error: {map_path} has no sourcesContent", file=sys.stderr)
        return 1

    written = 0
    for source, content in zip(sources, contents):
        if content is None:
            continue
        target = dest / safe_relative_path(source)
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        written += 1

    print(f"{written} sources -> {dest}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
