# Bootstrap Scripts

These scripts support the first OpenJibo recovery path:

- discover which hosts the robot is trying to reach
- generate DNS override records for a controlled environment
- verify that the robot-facing domains resolve and answer as expected
- audit a mounted robot filesystem for the conversion-relevant config files before any write helper runs
- orchestrate the Linux conversion flow with audit, plan, and gated apply helpers

Windows PowerShell wrappers remain available for local staging and analysis, but the robot-facing conversion path is shell-based.

They are intentionally non-destructive.

## Harness Scaffold

Use the harness scaffold when you want to test conversion against a disposable copy of the extracted robot image.

The intended flow is:

1. scaffold a writable overlay from `jibo_full_emmc`
2. run audit/plan/apply against the overlay
3. reset the overlay from the source snapshot when you want a clean run

The scaffold normalizes the extracted image layout into robot-root paths such as `var/jibo`, `usr/local/etc`, `skills`, `etc`, and `boot` so the conversion helpers can run against the overlay without special-case path handling.

Entry points:

- `Scaffold-OpenJiboHarness.ps1`
- `Run-OpenJiboHarness.ps1`
- `Rollback-OpenJiboConversion.ps1`
- `Roundtrip-OpenJiboHarness.ps1`
- `Validate-OpenJiboHarnessRoundTrip.ps1`
- `Demo-OpenJiboHarness.ps1`
- `Recommend-OpenJiboHarnessMode.ps1`
- `Build-LinuxFilesystemFromCopies.ps1`
- `Inspect-LinuxFilesystemDemo.ps1`
- `Run-OpenJiboFilesystemDemo.ps1`

Example:

```powershell
.\Scaffold-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -Clean
.\Run-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -TargetMode open-jibo -Apply -Strict -Clean
.\Rollback-OpenJiboConversion.ps1 -RobotRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -ApplyPath C:\Projects\JiboExperiments\artifacts\harness-overlay\run-output\invoke\conversion-apply.json -Strict
.\Roundtrip-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\harness-overlay -TargetMode open-jibo -Strict -Clean
.\Validate-OpenJiboHarnessRoundTrip.ps1 -OutputDirectory C:\Projects\JiboExperiments\artifacts\harness-overlay-output
.\Demo-OpenJiboHarness.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OverlayRoot C:\Projects\JiboExperiments\artifacts\demo-overlay -Strict -Clean
.\Recommend-OpenJiboHarnessMode.ps1 -Goal demo
.\Build-LinuxFilesystemFromCopies.ps1 -SourceRoot C:\Users\JacobDubin\Downloads\jibo_full_emmc -OutputRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo -Clean
.\Inspect-LinuxFilesystemDemo.ps1 -OutputRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo5
.\Run-OpenJiboFilesystemDemo.ps1 -DemoRoot C:\Projects\JiboExperiments\artifacts\linux-fs-demo5\demo-root -Strict
```
