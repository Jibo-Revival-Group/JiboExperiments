#!/usr/bin/env python3
"""Read-only inspector for the outer stock robot OTA tar envelope."""

from __future__ import annotations

import argparse
import bz2
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
MAX_NESTED_COMPRESSED_BYTES = 512 * 1024 * 1024
MAX_NESTED_DECOMPRESSED_BYTES = 4 * 1024 * 1024 * 1024
MAX_NESTED_MEMBER_BYTES = 4 * 1024 * 1024 * 1024
MAX_NESTED_TOTAL_DECLARED_BYTES = 4 * 1024 * 1024 * 1024
MAX_NESTED_ENTRIES = 250_000
MAX_ELF_HEADER_VARIANTS = 32
ALLOWED_FILES = {"filesystem.tar.bz2", "preinstall", "postinstall"}
REGULAR_TYPES = {tarfile.REGTYPE, tarfile.AREGTYPE}


class InspectionError(Exception):
    pass


class _BoundedBz2Reader:
    """Incremental bzip2 reader with compressed and expanded byte ceilings."""

    def __init__(self, source: Any, compressed_limit: int, expanded_limit: int) -> None:
        self.source = source
        self.compressed_limit = compressed_limit
        self.expanded_limit = expanded_limit
        self.decoder = bz2.BZ2Decompressor()
        self.compressed_bytes = 0
        self.decompressed_bytes = 0
        self.buffer = bytearray()
        self.finished = False

    def read(self, requested: int = -1) -> bytes:
        if requested <= 0:
            requested = READ_CHUNK_BYTES
        while len(self.buffer) < requested and not self.finished:
            compressed = b""
            if self.decoder.needs_input:
                remaining = self.compressed_limit - self.compressed_bytes
                compressed = self.source.read(min(READ_CHUNK_BYTES, remaining + 1))
                if not compressed:
                    if not self.decoder.eof:
                        raise InspectionError("truncated-bzip2-stream")
                    self.finished = True
                    break
                self.compressed_bytes += len(compressed)
                if self.compressed_bytes > self.compressed_limit:
                    raise InspectionError("nested-compressed-byte-limit-exceeded")

            output_limit = min(requested - len(self.buffer), READ_CHUNK_BYTES,
                               self.expanded_limit - self.decompressed_bytes + 1)
            try:
                output = self.decoder.decompress(compressed, max_length=output_limit)
            except (OSError, EOFError) as error:
                raise InspectionError("malformed-bzip2-stream") from error
            self.decompressed_bytes += len(output)
            if self.decompressed_bytes > self.expanded_limit:
                raise InspectionError("nested-decompressed-byte-limit-exceeded")
            self.buffer.extend(output)

            if self.decoder.eof:
                if self.decoder.unused_data or self.source.read(1):
                    raise InspectionError("trailing-data-after-bzip2-stream")
                self.finished = True

        result = bytes(self.buffer[:requested])
        del self.buffer[:len(result)]
        return result


def _read_exact(stream: Any, size: int) -> bytes:
    chunks = bytearray()
    while len(chunks) < size:
        chunk = stream.read(size - len(chunks))
        if not chunk:
            break
        chunks.extend(chunk)
    return bytes(chunks)


def _normalize_nested_path(name: str, is_directory: bool) -> str:
    if not name or "\x00" in name or "\\" in name:
        raise InspectionError("unsafe-nested-path")
    if name.startswith("/") or re.match(r"^[A-Za-z]:", name):
        raise InspectionError("absolute-nested-path")
    if any(part == ".." for part in name.split("/")):
        raise InspectionError("traversal-nested-path")
    if name.endswith("/") and not is_directory:
        raise InspectionError("file-nested-path-has-directory-suffix")
    normalized = posixpath.normpath(name)
    if normalized in ("", "."):
        if name not in (".", "./") or not is_directory:
            raise InspectionError("unsupported-nested-root-path")
        return "."
    return normalized


def _skip_exact(stream: _BoundedBz2Reader, size: int) -> None:
    remaining = size
    while remaining:
        chunk = stream.read(min(READ_CHUNK_BYTES, remaining))
        if not chunk:
            raise InspectionError("truncated-nested-member-content")
        remaining -= len(chunk)


def _elf_header_identity(prefix: bytes) -> tuple[int, str, int, int, int] | None:
    """Inventory at most 64 bytes; this does not validate an ELF program or ABI.

    Layout: https://gabi.xinuos.com/elf/02-eheader.html
    """
    if not prefix.startswith(b"\x7fELF"):
        return None
    if len(prefix) < 16 or prefix[4] not in (1, 2) or prefix[5] not in (1, 2):
        raise InspectionError("unsupported-or-truncated-elf-identification")
    header_size = 52 if prefix[4] == 1 else 64
    if len(prefix) < header_size:
        raise InspectionError("truncated-elf-header")
    order = "little" if prefix[5] == 1 else "big"
    size_offset = 40 if prefix[4] == 1 else 52
    if (prefix[6] != 1 or int.from_bytes(prefix[20:24], order) != 1
            or int.from_bytes(prefix[size_offset:size_offset + 2], order) != header_size):
        raise InspectionError("unsupported-elf-header-version-or-size")
    return (32 if prefix[4] == 1 else 64, order,
            int.from_bytes(prefix[18:20], order), prefix[7], prefix[8])


def _inspect_nested_filesystem(archive: tarfile.TarFile,
                               filesystem_member: tarfile.TarInfo) -> dict[str, Any]:
    if filesystem_member.size > MAX_NESTED_COMPRESSED_BYTES:
        raise InspectionError("nested-compressed-byte-limit-exceeded")
    compressed = archive.extractfile(filesystem_member)
    if compressed is None:
        raise InspectionError("nested-filesystem-content-unreadable")

    reader = _BoundedBz2Reader(compressed, MAX_NESTED_COMPRESSED_BYTES,
                               MAX_NESTED_DECOMPRESSED_BYTES)
    seen: dict[str, bool] = {}
    nested_directories: set[str] = set()
    files = directories = executable_files = 0
    total_declared = 0
    elf_headers: dict[tuple[int, str, int, int, int], int] = {}
    try:
        while True:
            header = _read_exact(reader, 512)
            if len(header) != 512:
                raise InspectionError("malformed-or-truncated-nested-tar")
            if header == b"\0" * 512:
                if _read_exact(reader, 512) != b"\0" * 512:
                    raise InspectionError("malformed-or-truncated-nested-tar")
                while chunk := reader.read(READ_CHUNK_BYTES):
                    if any(chunk):
                        raise InspectionError("nonzero-data-after-nested-tar-end")
                break

            if len(seen) >= MAX_NESTED_ENTRIES:
                raise InspectionError("nested-entry-count-limit-exceeded")
            try:
                member = tarfile.TarInfo.frombuf(header, encoding="utf-8", errors="surrogateescape")
            except tarfile.TarError as error:
                raise InspectionError("malformed-nested-tar-header") from error
            is_directory = member.type == tarfile.DIRTYPE
            if member.type not in REGULAR_TYPES | {tarfile.DIRTYPE}:
                raise InspectionError("nested-links-special-and-extension-members-unsupported")
            normalized = _normalize_nested_path(member.name, is_directory)
            if normalized in seen:
                raise InspectionError("duplicate-normalized-nested-path")
            parent = posixpath.dirname(normalized)
            if not is_directory and normalized in nested_directories:
                raise InspectionError("file-directory-path-collision")
            while parent and parent != ".":
                if parent in seen and not seen[parent]:
                    raise InspectionError("file-directory-path-collision")
                nested_directories.add(parent)
                parent = posixpath.dirname(parent)
            seen[normalized] = is_directory
            if member.mode & 0o6000:
                raise InspectionError("setuid-or-setgid-mode-unsupported")
            if member.size < 0 or member.size > MAX_NESTED_MEMBER_BYTES:
                raise InspectionError("nested-member-byte-limit-exceeded")
            if is_directory:
                if member.size != 0:
                    raise InspectionError("directory-with-content-unsupported")
                directories += 1
                continue

            total_declared += member.size
            if total_declared > MAX_NESTED_TOTAL_DECLARED_BYTES:
                raise InspectionError("nested-total-declared-byte-limit-exceeded")
            files += 1
            if member.mode & 0o111:
                executable_files += 1
            prefix = _read_exact(reader, min(64, member.size))
            if len(prefix) != min(64, member.size):
                raise InspectionError("truncated-nested-member-content")
            identity = _elf_header_identity(prefix)
            if identity is not None:
                if identity not in elf_headers and len(elf_headers) >= MAX_ELF_HEADER_VARIANTS:
                    raise InspectionError("elf-header-variant-limit-exceeded")
                elf_headers[identity] = elf_headers.get(identity, 0) + 1
            _skip_exact(reader, member.size - len(prefix))
            padding = (-member.size) % 512
            if padding and _read_exact(reader, padding) != b"\0" * padding:
                raise InspectionError("nonzero-nested-member-padding")
    finally:
        compressed.close()

    if files == 0:
        raise InspectionError("empty-or-noop-nested-tar")
    return {
        "Performed": True,
        "CompressedBytes": filesystem_member.size,
        "DecompressedTarBytes": reader.decompressed_bytes,
        "EntryCount": len(seen),
        "RegularFileCount": files,
        "DirectoryCount": directories,
        "DeclaredRegularFileBytes": total_declared,
        "ExecutableFileCount": executable_files,
        "ElfHeaderInventory": [
            {"ClassBits": key[0], "ByteOrder": key[1], "Machine": key[2],
             "OsAbi": key[3], "AbiVersion": key[4], "FileCount": count}
            for key, count in sorted(elf_headers.items())
        ],
        "ElfHeaderScope": "first 64 bytes only; no loader, code, dependency or ABI validation",
        "CompatibilityCertification": False,
        "LimitPolicy": "conservative; filesystem links and special entries are rejected",
    }


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


def inspect_package(package_path: str, include_sha1_compat: bool = False,
                    inspect_filesystem: bool = False) -> dict[str, Any]:
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
        nested_filesystem: dict[str, Any] | None = None

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
                    elif normalized == "filesystem.tar.bz2" and inspect_filesystem:
                        nested_filesystem = _inspect_nested_filesystem(archive, member)

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
        "nested-filesystem-archive-not-inspected" if not inspect_filesystem
        else "filesystem-content-abi-ownership-hardware-not-validated",
        "publisher-trust-and-signature-not-verified",
        "installation-and-recovery-not-verified",
    ]
    if inspect_filesystem and nested_filesystem is None:
        blockers.append("nested-filesystem-archive-not-present")
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
        "NestedFilesystemInspection": nested_filesystem,
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
    parser.add_argument("--inspect-filesystem", action="store_true",
                        help="stream-inspect nested filesystem.tar.bz2 without disk extraction")
    args = parser.parse_args(argv)
    try:
        result = inspect_package(args.package, args.sha1_compat, args.inspect_filesystem)
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
