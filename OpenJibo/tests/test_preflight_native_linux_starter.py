"""Offline tests for the read-only native Linux starter preflight."""

import importlib.util
from pathlib import Path
import subprocess
from types import SimpleNamespace
import unittest
from unittest import mock


SCRIPT = Path(__file__).resolve().parents[1] / "scripts/cloud/preflight-native-linux-starter.py"
SPEC = importlib.util.spec_from_file_location("native_linux_preflight", SCRIPT)
preflight = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(preflight)


class NativeLinuxPreflightTests(unittest.TestCase):
    def setUp(self):
        self.patches = [
            mock.patch.object(preflight.platform, "system", return_value="Linux"),
            mock.patch.object(preflight.platform, "machine", return_value="x86_64"),
            mock.patch.object(preflight.platform, "release", return_value="6.8.0-generic"),
            mock.patch.object(preflight, "_read_text", side_effect=lambda path: {
                "/proc/meminfo": "MemTotal: 8388608 kB\nMemAvailable: 4194304 kB\n",
                "/proc/sys/kernel/osrelease": "6.8.0-generic",
                "/proc/version": "Linux version 6.8.0",
            }.get(path, "")),
            mock.patch.object(preflight.shutil, "disk_usage", return_value=SimpleNamespace(
                total=100000000000, free=50000000000)),
            mock.patch.object(preflight, "_is_containerized", return_value=False),
            mock.patch.dict(preflight.os.environ, {}, clear=True),
        ]
        for patch in self.patches:
            patch.start()
            self.addCleanup(patch.stop)

    @staticmethod
    def command(args):
        if args[1:3] == ["context", "inspect"]:
            return '"unix:///var/run/docker.sock"', None
        if args[1] == "info":
            return '["linux","x86_64","28.1.0",8000000000,"Ubuntu 24.04"]', None
        if args[1:3] == ["compose", "version"]:
            return "2.39.0", None
        raise AssertionError(args)

    def test_native_linux_reports_facts_without_certifying_install(self):
        with mock.patch.object(preflight, "_command", side_effect=self.command):
            result = preflight.collect_report()
        self.assertTrue(result["preflight_passed"])
        self.assertEqual(result["host"]["memory_total_bytes"], 8589934592)
        self.assertEqual(result["docker_server"]["version"], "28.1.0")
        self.assertEqual(result["compose_version"], "2.39.0")
        self.assertIn("remain untested", result["scope"])

    def test_wsl_and_docker_desktop_are_not_native_acceptance(self):
        def read_wsl(path):
            if path == "/proc/meminfo":
                return "MemTotal: 8388608 kB\nMemAvailable: 4194304 kB\n"
            return "5.15.0-microsoft-standard-WSL2"

        def command(args):
            value, error = self.command(args)
            if args[1] == "info":
                value = value.replace("Ubuntu 24.04", "Docker Desktop")
            return value, error

        with mock.patch.object(preflight, "_read_text", side_effect=read_wsl), \
             mock.patch.object(preflight, "_command", side_effect=command):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("host_is_wsl_not_independent_native_linux", result["issues"])
        self.assertIn("docker_server_is_desktop_not_independent_linux", result["issues"])

    def test_unavailable_docker_and_compose_fail_clearly(self):
        with mock.patch.object(preflight, "_command", return_value=(None, "docker_cli_missing")):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_context_docker_cli_missing", result["issues"])
        self.assertIsNone(result["docker_server"])

    def test_docker_timeout_fails_clearly(self):
        def command(args):
            if args[1] == "info":
                return None, "command_timeout"
            return self.command(args)

        with mock.patch.object(preflight, "_command", side_effect=command):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_info_command_timeout", result["issues"])

    def test_remote_endpoint_is_rejected_without_echoing_it(self):
        def command(args):
            if args[1] == "context":
                return '"ssh://user:secret@example.invalid"', None
            return self.command(args)

        with mock.patch.object(preflight, "_command", side_effect=command) as run:
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_endpoint_not_local_unix_socket", result["issues"])
        self.assertNotIn("secret", str(result))
        self.assertEqual(run.call_count, 1)

    def test_remote_docker_host_override_is_rejected_without_echo(self):
        with mock.patch.object(preflight, "_command", side_effect=self.command), \
             mock.patch.dict(preflight.os.environ, {"DOCKER_HOST": "tcp://secret@example.invalid"}):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_host_override_not_local_unix_socket", result["issues"])
        self.assertNotIn("secret", str(result))

    def test_subprocess_timeout_is_bounded_and_has_no_shell(self):
        args = ["docker", "info", "--format", preflight.DOCKER_INFO_FORMAT]
        with mock.patch.object(preflight.subprocess, "run", side_effect=subprocess.TimeoutExpired(args, 8)) as run:
            output, error = preflight._command(args)
        self.assertIsNone(output)
        self.assertEqual(error, "command_timeout")
        run.assert_called_once_with(args, capture_output=True, text=True, timeout=8, check=False)

    def test_nonzero_stderr_is_not_exposed(self):
        args = ["docker", "compose", "version", "--short"]
        failed = subprocess.CompletedProcess(args, 1, "", "credential=secret")
        with mock.patch.object(preflight.subprocess, "run", return_value=failed):
            output, error = preflight._command(args)
        self.assertIsNone(output)
        self.assertEqual(error, "command_failed")
        self.assertNotIn("secret", str((output, error)))

    def test_container_host_is_not_independent_native_linux(self):
        with mock.patch.object(preflight, "_is_containerized", return_value=True), \
             mock.patch.object(preflight, "_command", side_effect=self.command):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("host_is_container_not_independent_native_linux", result["issues"])

    def test_malformed_docker_info_fails_closed(self):
        def command(args):
            if args[1] == "info":
                return '{"unexpected":"object"}', None
            return self.command(args)

        with mock.patch.object(preflight, "_command", side_effect=command):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_info_invalid_response", result["issues"])

    def test_missing_docker_operating_system_fails_closed(self):
        def command(args):
            if args[1] == "info":
                return '["linux","x86_64","28.1.0",8000000000,null]', None
            return self.command(args)

        with mock.patch.object(preflight, "_command", side_effect=command):
            result = preflight.collect_report()
        self.assertFalse(result["preflight_passed"])
        self.assertIn("docker_info_invalid_response", result["issues"])


if __name__ == "__main__":
    unittest.main()
