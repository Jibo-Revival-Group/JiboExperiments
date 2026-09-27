#!/usr/bin/env python3
"""Read-only inspector for the outer stock robot OTA tar envelope."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import posixpath
import re
import stat
import sys
import tarfile
from typing import Any

MAX_ARCHIVE_BYTES = 2 * 1024 * 1024 * 1024
MAX_MEMBER_BYTES = 2 * 1024 * 1024 * 1024
MAX_TOTAL_DECLARED_BYTES = 2 * 1024 * 1024 * 1024
MAX_MEMBERS = 128
READ_CHUNK_BYTES = 1024 * 1024
ALLOWED_FILES = {"filesystem.tar.bz2", "preinstall", "postinstall"}
REGULAR_TYPES = {tarfile.REGTYPE, tarfile.AREGTYPE}


class InspectionError(Exception):
    pass


def _normalize_member_name(name: str) -> str:
    if not name or "\x00" in name or "\\" in name:
        raise InspectionError("unsafe-member-path")
    if name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        raise InspectionError("absolute-member-path")
    if any(part == ".." for part in name.split("/")):
        raise InspectionError("traversal-member-path")

    normalized = posixpath.normpath(name)
    if normalized in ("", "."):
        if name not in (".", "./"):
            raise InspectionError("unsupported-root-directory-path")
        return "."
    if name.endswith("/"):
        raise InspectionError("file-member-has-directory-suffix")
    if normalized not in ALLOWED_FILES:
        raise InspectionError("unsupported-outer-member")
    return normalized


def _hash_member(archive: tarfile.TarFile, member: tarfile.TarInfo,
                 include_sha1: bool) -> dict[str, Any]:
    stream = archive.extractfile(member)
    if stream is None:
        raise InspectionError("member-content-unreadable")

    sha256 = hashlib.sha256()
    sha1 = hashlib.sha1() if include_sha1 else None
    remaining = member.size
    with stream:
        while remaining:
            chunk = stream.read(min(READ_CHUNK_BYTES, remaining))
            if not chunk:
                raise InspectionError("truncated-member-content")
            sha256.update(chunk)
            if sha1 is not None:
                sha1.update(chunk)
            remaining -= len(chunk)

    result: dict[str, Any] = {
        "sizeBytes": member.size,
        "memberSha256": sha256.hexdigest(),
        "executableBitPresent": bool(member.mode & 0o111),
    }
    if sha1 is not None:
        result["memberSha1CompatibilityOnly"] = sha1.hexdigest()
    return result


def _preflight_raw_tar(source: Any, file_size: int) -> None:
    """Bound raw headers before tarfile can process GNU/PAX extension metadata."""
    source.seek(0)
    count = 0
    total_declared = 0
    while True:
        header = source.read(512)
        if len(header) != 512:
            raise InspectionError("malformed-or-truncated-outer-tar")
        if header == b"\0" * 512:
            second_end_block = source.read(512)
            if second_end_block != b"\0" * 512:
                raise InspectionError("malformed-or-truncated-outer-tar")
            while chunk := source.read(READ_CHUNK_BYTES):
                if any(chunk):
                    raise InspectionError("nonzero-data-after-tar-end")
            return

        count += 1
        if count > MAX_MEMBERS:
            raise InspectionError("member-count-limit-exceeded")
        try:
            raw_info = tarfile.TarInfo.frombuf(header, encoding="utf-8", errors="surrogateescape")
        except tarfile.TarError as error:
            raise InspectionError("malformed-outer-tar-header") from error
        if raw_info.type not in REGULAR_TYPES | {tarfile.DIRTYPE}:
            raise InspectionError("links-special-and-extension-members-unsupported")
        if header[124] & 0x80:
            raise InspectionError("unsupported-member-size-encoding")
        declared_size = raw_info.size
        if declared_size < 0 or declared_size > MAX_MEMBER_BYTES:
            raise InspectionError("member-byte-limit-exceeded")
        total_declared += declared_size
        if total_declared > MAX_TOTAL_DECLARED_BYTES:
            raise InspectionError("total-declared-byte-limit-exceeded")

        padded_size = ((declared_size + 511) // 512) * 512
        if source.tell() + padded_size > file_size - 1024:
            raise InspectionError("truncated-member-content")
        source.seek(padded_size, os.SEEK_CUR)


def inspect_package(package_path: str, include_sha1_compat: bool = False) -> dict[str, Any]:
    # Avoid blocking on an ordinary FIFO input; fstat below still validates the
    # opened object. Callers must supply a stable local analysis copy.
    if not stat.S_ISREG(os.stat(package_path).st_mode):
        raise InspectionError("input-is-not-a-regular-file")
    with open(package_path, "rb") as source:
        package_stat = os.fstat(source.fileno())
        if not stat.S_ISREG(package_stat.st_mode):
            raise InspectionError("input-is-not-a-regular-file")
        file_size = package_stat.st_size
        if file_size == 0:
            raise InspectionError("empty-archive")
        if file_size > MAX_ARCHIVE_BYTES:
            raise InspectionError("archive-byte-limit-exceeded")
        signature = source.read(6)
        if signature.startswith((b"\x1f\x8b", b"BZh", b"\xfd7zXZ\x00", b"\x28\xb5\x2f\xfd")):
            raise InspectionError("compressed-outer-tar-unsupported")
        if file_size % 512 != 0:
            raise InspectionError("malformed-or-truncated-outer-tar")
        source.seek(file_size - 1024)
        if source.read(1024) != b"\0" * 1024:
            raise InspectionError("malformed-or-truncated-outer-tar")
        source.seek(0)
        _preflight_raw_tar(source, file_size)
        source.seek(0)

        entries: dict[str, dict[str, Any]] = {}
        seen_paths: set[str] = set()
        total_declared = 0
        hook_names: list[str] = []

        try:
            with tarfile.open(fileobj=source, mode="r:") as archive:
                for count, member in enumerate(archive, start=1):
                    if count > MAX_MEMBERS:
                        raise InspectionError("member-count-limit-exceeded")
                    normalized = _normalize_member_name(member.name)
                    if normalized in seen_paths:
                        raise InspectionError("duplicate-normalized-member-path")
                    seen_paths.add(normalized)

                    if normalized == ".":
                        if not member.isdir() or member.size != 0:
                            raise InspectionError("invalid-root-directory-entry")
                        continue

                    if member.type not in REGULAR_TYPES:
                        raise InspectionError("links-and-special-members-unsupported")
                    if member.size < 0 or member.size > MAX_MEMBER_BYTES:
                        raise InspectionError("member-byte-limit-exceeded")
                    total_declared += member.size
                    if total_declared > MAX_TOTAL_DECLARED_BYTES:
                        raise InspectionError("total-declared-byte-limit-exceeded")

                    entries[normalized] = _hash_member(archive, member, include_sha1_compat)
                    if normalized in ("preinstall", "postinstall"):
                        hook_names.append(normalized)

                trailing_start = archive.fileobj.tell()
                archive.fileobj.seek(trailing_start)
                while chunk := archive.fileobj.read(READ_CHUNK_BYTES):
                    if any(chunk):
                        raise InspectionError("nonzero-data-after-tar-end")
            if not entries:
                raise InspectionError("empty-or-noop-archive")
            archive_digests = _hash_archive(source, include_sha1_compat)
        except InspectionError:
            raise
        except (tarfile.TarError, EOFError, OSError, ValueError) as error:
            raise InspectionError("malformed-or-truncated-outer-tar") from error

    blockers = [
        "nested-filesystem-archive-not-inspected",
        "publisher-trust-and-signature-not-verified",
        "installation-and-recovery-not-verified",
    ]
    if hook_names:
        blockers.append("executable-hooks-require-manual-review")
    return {
        "OuterEnvelopeAccepted": True,
        "CanOfferUpdates": False,
        "ReadOnlyInspection": True,
        "FilesystemExtractionPerformed": False,
        "HooksExecuted": False,
        "Scope": "outer uncompressed tar envelope only; not an installation or trust assessment",
        "ArchiveSizeBytes": file_size,
        "ArchiveDigests": archive_digests,
        "MemberCount": len(seen_paths),
        "Members": entries,
        "HooksRequiringManualReview": hook_names,
        "Sha1Purpose": "optional legacy compatibility checksum only; not publisher identity",
        "Blockers": blockers,
    }


def _hash_archive(source: Any, include_sha1_compat: bool) -> dict[str, str]:
    sha256 = hashlib.sha256()
    sha1 = hashlib.sha1() if include_sha1_compat else None
    source.seek(0)
    while chunk := source.read(READ_CHUNK_BYTES):
        sha256.update(chunk)
        if sha1 is not None:
            sha1.update(chunk)
    result = {"sha256": sha256.hexdigest()}
    if sha1 is not None:
        result["sha1CompatibilityOnly"] = sha1.hexdigest()
    return result


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", help="path to the outer OTA tar package")
    parser.add_argument("--sha1-compat", action="store_true",
                        help="include optional SHA-1 compatibility checksums; not publisher identity")
    args = parser.parse_args(argv)
    try:
        result = inspect_package(args.package, args.sha1_compat)
    except (InspectionError, OSError) as error:
        print(json.dumps({
            "OuterEnvelopeAccepted": False,
            "CanOfferUpdates": False,
            "ReadOnlyInspection": True,
            "FilesystemExtractionPerformed": False,
            "HooksExecuted": False,
            "Errors": [str(error)],
        }, indent=2))
        return 2
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
