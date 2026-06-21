# Bootstrap Scripts

These scripts support the first OpenJibo recovery path:

- discover which hosts the robot is trying to reach
- generate DNS override records for a controlled environment
- verify that the robot-facing domains resolve and answer as expected
- audit a mounted robot filesystem for the conversion-relevant config files before any write helper runs
- orchestrate the conversion flow with audit, plan, and gated apply helpers

They are intentionally non-destructive.
