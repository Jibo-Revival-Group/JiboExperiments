#!/usr/bin/env python3
"""Inspect websocket captures for likely robot-side identity/recognition fields.

The live robot appears to send speech turns through CLIENT_ASR/CLIENT_NLU websocket
messages. This helper scans captured telemetry and exported fixtures for face,
voice, speaker, person, member, and enrollment-like JSON keys so we can decide
whether a capture contains enough source data to call RecordRecognitionObservation.
"""
from __future__ import annotations

import argparse
import json
import os
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any, Iterable

KEY_TERMS = (
    "face", "voice", "speaker", "person", "people", "member", "identity", "recogn",
    "enroll", "user", "profile", "confidence", "score",
)
PII_KEYS = {"firstname", "lastname", "email", "phoneticname", "birthday", "birthdate"}
TEXT_TYPES = {"CLIENT_ASR", "CLIENT_NLU", "TRIGGER", "LISTEN"}


def iter_json_files(root: Path) -> Iterable[Path]:
    if root.is_file():
        yield root
        return
    for pattern in ("*.events.ndjson", "*.flow.json", "*.json"):
        yield from root.rglob(pattern)


def load_records(path: Path) -> Iterable[Any]:
    if path.name.endswith(".ndjson"):
        with path.open("r", encoding="utf-8", errors="replace") as handle:
            for line in handle:
                line = line.strip()
                if line:
                    yield json.loads(line)
    else:
        with path.open("r", encoding="utf-8", errors="replace") as handle:
            yield json.load(handle)


def walk(value: Any, prefix: str = "") -> Iterable[tuple[str, Any]]:
    if isinstance(value, dict):
        for key, child in value.items():
            path = f"{prefix}.{key}" if prefix else str(key)
            yield path, child
            yield from walk(child, path)
    elif isinstance(value, list):
        for index, child in enumerate(value):
            yield from walk(child, f"{prefix}[{index}]")


def embedded_payload(record: dict[str, Any]) -> Any | None:
    text = record.get("Text")
    if not isinstance(text, str) or not text.strip().startswith(("{", "[")):
        return None
    try:
        return json.loads(text)
    except json.JSONDecodeError:
        return None


def redact(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: ("<redacted>" if key.lower() in PII_KEYS else redact(child)) for key, child in value.items()}
    if isinstance(value, list):
        return [redact(child) for child in value]
    return value


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture_path", nargs="?", default="captures/websocket")
    parser.add_argument("--max-examples", type=int, default=20)
    args = parser.parse_args()

    root = Path(args.capture_path)
    if not root.exists():
        print(f"No websocket capture path found at {root}")
        return 0

    event_types: Counter[str] = Counter()
    message_types: Counter[str] = Counter()
    candidate_keys: Counter[str] = Counter()
    examples: dict[str, list[str]] = defaultdict(list)
    files = 0
    records = 0

    for path in sorted(iter_json_files(root)):
        files += 1
        for record in load_records(path):
            records += 1
            payloads = [record]
            if isinstance(record, dict):
                event_types[str(record.get("EventType", "fixture"))] += 1
                embedded = embedded_payload(record)
                if embedded is not None:
                    payloads.append(embedded)
                    if isinstance(embedded, dict):
                        message_types[str(embedded.get("type", "unknown"))] += 1
            for payload in payloads:
                for key_path, value in walk(payload):
                    key_leaf = key_path.rsplit(".", 1)[-1].lower()
                    if any(term in key_leaf for term in KEY_TERMS):
                        candidate_keys[key_path] += 1
                        if len(examples[key_path]) < args.max_examples:
                            preview = json.dumps(redact(value), ensure_ascii=False, sort_keys=True)
                            if len(preview) > 180:
                                preview = preview[:177] + "..."
                            examples[key_path].append(f"{path.relative_to(root) if path.is_relative_to(root) else path}: {preview}")

    print(f"Scanned files: {files}")
    print(f"Scanned records: {records}")
    print("\nMessage types:")
    for key, count in sorted(message_types.items()):
        marker = " *" if key in TEXT_TYPES else ""
        print(f" - {key}: {count}{marker}")
    print("\nRecognition candidate keys:")
    if not candidate_keys:
        print(" - none found")
    for key, count in candidate_keys.most_common():
        print(f" - {key}: {count}")
        for example in examples[key][: min(3, args.max_examples)]:
            print(f"   example: {example}")
    print("\nMapping note:")
    print(" - A stable data.runtime.perception.speaker value that matches an entry in data.runtime.loop.users is enough to map a voice/presence observation to a loop member for demo smoke wiring.")
    print(" - Face recognition should remain manual/demo-seeded until a capture exposes face/person identity metadata, peoplePresent user ids, or a robot-local source path outside the websocket stream is identified.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
