#!/usr/bin/env python3
"""Read-only host and Docker preflight for a separate Linux starter trial.

This reports prerequisites; it does not install, pull, start, or certify anything.
"""

import json
import os
import platform
from pathlib import Path
import shutil
import subprocess
import sys


COMMAND_TIMEOUT_SECONDS = 8
DOCKER_INFO_FORMAT = ("[{{json .OSType}},{{json .Architecture}},"
                      "{{json .ServerVersion}},{{json .MemTotal}},"
                      "{{json .OperatingSystem}}]")


def _read_text(path):
    try:
        return Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def _memory_bytes():
    values = {}
    for line in _read_text("/proc/meminfo").splitlines():
        key, separator, raw = line.partition(":")
        if separator and key in ("MemTotal", "MemAvailable"):
            parts = raw.strip().split()
            if len(parts) == 2 and parts[1] == "kB" and parts[0].isdigit():
                values[key] = int(parts[0]) * 1024
    return values.get("MemTotal"), values.get("MemAvailable")


def _is_containerized():
    return Path("/.dockerenv").exists() or Path("/run/.containerenv").exists()


def _command(args):
    try:
        result = subprocess.run(
            args, capture_output=True, text=True, timeout=COMMAND_TIMEOUT_SECONDS,
            check=False,
        )
    except FileNotFoundError:
        return None, "docker_cli_missing"
    except subprocess.TimeoutExpired:
        return None, "command_timeout"
    except OSError:
        return None, "command_unavailable"
    if result.returncode != 0:
        return None, "command_failed"
    return result.stdout.strip(), None


def collect_report():
    system = platform.system()
    release = platform.release()
    kernel = (_read_text("/proc/sys/kernel/osrelease") + " " +
              _read_text("/proc/version") + " " + release).lower()
    is_wsl = system == "Linux" and ("microsoft" in kernel or "wsl" in kernel)
    is_containerized = system == "Linux" and _is_containerized()
    total_memory, available_memory = _memory_bytes() if system == "Linux" else (None, None)
    try:
        disk = shutil.disk_usage(Path.cwd())
        disk_total, disk_free = disk.total, disk.free
    except OSError:
        disk_total, disk_free = None, None

    report = {
        "purpose": "read_only_native_linux_starter_preflight",
        "host": {
            "os": system, "architecture": platform.machine(), "kernel_release": release,
            "wsl": is_wsl, "containerized": is_containerized,
            "memory_total_bytes": total_memory,
            "memory_available_bytes": available_memory,
            "working_directory_disk_total_bytes": disk_total,
            "working_directory_disk_free_bytes": disk_free,
        },
        "docker_server": None,
        "compose_version": None,
        "preflight_passed": False,
        "issues": [],
        "scope": "Prerequisites only. A Unix Docker endpoint is best-effort locality evidence, not proof of daemon host identity. Speech suitability, installation, persistence, restore, and robot behavior remain untested.",
    }
    issues = report["issues"]
    if system != "Linux":
        issues.append("host_not_linux")
    if is_wsl:
        issues.append("host_is_wsl_not_independent_native_linux")
    if is_containerized:
        issues.append("host_is_container_not_independent_native_linux")
    if total_memory is None or available_memory is None:
        issues.append("host_memory_unavailable")
    if disk_total is None or disk_free is None:
        issues.append("working_directory_disk_unavailable")

    endpoint_output, endpoint_error = _command([
        "docker", "context", "inspect", "--format", "{{json .Endpoints.docker.Host}}",
    ])
    if endpoint_error:
        issues.append("docker_context_" + endpoint_error)
    else:
        try:
            endpoint = json.loads(endpoint_output)
            if not isinstance(endpoint, str) or not endpoint.startswith("unix:///"):
                issues.append("docker_endpoint_not_local_unix_socket")
        except (ValueError, TypeError):
            issues.append("docker_context_invalid_response")
    # Either override can select another endpoint. Never print its potentially
    # credential-bearing value, and conservatively reject non-Unix overrides.
    docker_host_override = os.environ.get("DOCKER_HOST")
    if docker_host_override and not docker_host_override.startswith("unix:///"):
        issues.append("docker_host_override_not_local_unix_socket")

    if any(issue.startswith("docker_context_") or
           issue in ("docker_endpoint_not_local_unix_socket",
                     "docker_host_override_not_local_unix_socket") for issue in issues):
        # Do not contact an unverified or remote daemon.
        return report

    docker_output, docker_error = _command(["docker", "info", "--format", DOCKER_INFO_FORMAT])
    if docker_error:
        issues.append("docker_info_" + docker_error)
    else:
        try:
            info = json.loads(docker_output)
            if not isinstance(info, list) or len(info) != 5:
                raise ValueError("Invalid Docker server field shape")
            server = {
                "os": info[0], "architecture": info[1],
                "version": info[2], "memory_total_bytes": info[3],
                "operating_system": info[4],
            }
            if any(not isinstance(server[key], str) or not server[key].strip()
                   for key in ("os", "architecture", "version", "operating_system")) or \
               type(server["memory_total_bytes"]) is not int or server["memory_total_bytes"] <= 0:
                raise ValueError("Missing Docker server fields")
            report["docker_server"] = server
            if server["os"].lower() != "linux":
                issues.append("docker_server_not_linux")
            if "docker desktop" in str(server["operating_system"]).lower():
                issues.append("docker_server_is_desktop_not_independent_linux")
        except (ValueError, TypeError):
            issues.append("docker_info_invalid_response")

    compose_output, compose_error = _command(["docker", "compose", "version", "--short"])
    if compose_error:
        issues.append("compose_version_" + compose_error)
    elif not compose_output:
        issues.append("compose_version_empty")
    else:
        report["compose_version"] = compose_output

    report["preflight_passed"] = not issues
    return report


def main():
    report = collect_report()
    print(json.dumps(report, sort_keys=True))
    return 0 if report["preflight_passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
