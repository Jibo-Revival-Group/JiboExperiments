# Robot Image Script Map

This maps the extracted robot images to the current Open Jibo scripts and test harnesses.

## Sources

### Update flash bundle

Path:

- `C:\Users\JacobDubin\Downloads\jibo-pvt-flash-build-dev-531`

Use it as:

- a factory/reset reference
- a flash bundle reference for the Tegra recovery path
- a source for kernel, partition, and flash-layout artifacts

Relevant contents:

- `flash_jibo/flash-jibo.sh`
- `flash_jibo/flash-dfu.sh`
- `flash_jibo/output/images/rootfs.ext4`
- `flash_jibo/output/images/services.ext4`
- `flash_jibo/output/images/skills.ext4`
- `flash_jibo/output/images/var.ext4`
- `flash_jibo/output/images/zImage`
- `flash_jibo/output/images/meerkat_rev02.bct`
- `flash_jibo/output/images/u-boot-flasher.bin`

### Full EMMC dump

Path:

- `C:\Users\JacobDubin\Downloads\jibo_full_emmc`

Use it as:

- the main filesystem and config reference for scripts
- the source for mount-path expectations
- the baseline for audit/plan/apply/rollback harnesses

Relevant partitions:

- `0.rootfsA`
- `1.rootfsB`
- `3.services`
- `4.var`
- `5.skills`

## Script Mapping

### Audit helpers

The audit scripts should read, not write:

- `4.var/jibo/credentials.json`
- `4.var/jibo/identity.json`
- `4.var/jibo/mode.json`
- `3.services/etc/jibo-jetstream-service.json`
- `3.services/etc/jibo-system-manager.json`
- `3.services/etc/jibo-ssm/jibo-ssm-normal.json`
- `3.services/etc/jibo-ssm/jibo-ssm-oobe.json`
- `3.services/etc/jibo-ssm/jibo-ssm-developer.json`
- `3.services/etc/jibo-ssm/jibo-ssm-int-developer.json`
- `5.skills/jibo/Jibo/Skills/oobe-config/config.json`
- `5.skills/jibo/Jibo/Skills/oobe-config/oobe-config.js`
- `0.rootfsA/boot/extlinux/extlinux.conf`
- `0.rootfsA/etc/fstab`
- `0.rootfsA/etc/inittab`

### Plan helpers

The plan scripts should derive proposed writes from the same files above, plus the identity material that may later support clone detection:

- `4.var/jibo/keys/keypair.json`
- `4.var/jibo/keys/symmetric-*.json`
- `4.var/jibo/identity/deepid/*`
- `4.var/jibo/identity/faces/*`
- `4.var/jibo/asr/*`

Plan output should describe:

- backup targets
- staged mode/region changes
- first-boot/OOBE pending state
- rollback metadata

### Apply helpers

The apply scripts should write only the minimum staged conversion state for now:

- `3.services/etc/jibo-jetstream-service.json`
- `5.skills/jibo/Jibo/Skills/oobe-config/config.json`
- `4.var/jibo/identity/openjibo-conversion.json`

The current conversion design intentionally keeps:

- `4.var/jibo/credentials.json` on the proven `api` region until onboarding completes
- stock identity files untouched unless the owner explicitly requests a repair step

### Verify helpers

Verification should confirm:

- the edited JSON files still parse
- backups exist under the apply output directory
- the robot root only changed where expected
- the staged Open Jibo marker is present
- the boot/mode files still match the baseline structure

## File Roles

### Identity and state

- `4.var/jibo/credentials.json`
  - current live region and access data
  - baseline reference for conversion
- `4.var/jibo/identity.json`
  - legacy robot identity claim
- `4.var/jibo/mode.json`
  - current boot mode
- `4.var/jibo/identity/openjibo-conversion.json`
  - staged conversion marker written by the apply helper

### Network and runtime

- `3.services/etc/jibo-jetstream-service.json`
  - region routing and hub/entrypoint definitions
- `3.services/etc/jibo-system-manager.json`
  - service launch order and mode layout
- `3.services/etc/jibo-ssm/*.json`
  - mode-specific SSM definitions

### OOBE and skill state

- `5.skills/jibo/Jibo/Skills/oobe-config/config.json`
  - OOBE server region and OTA filter
- `5.skills/jibo/Jibo/Skills/oobe-config/oobe-config.js`
  - OOBE app behavior and screen flow
- `5.skills/jibo/Jibo/Skills/@be/be/config/be-*.json`
  - behavior-engine mode profiles

### Boot and recovery

- `0.rootfsA/boot/extlinux/extlinux.conf`
  - Tegra boot entry and rootfs selection
- `0.rootfsA/etc/fstab`
  - mount layout
- `0.rootfsA/etc/inittab`
  - init flow and service startup

## VM Recommendation

Use the EMMC dump for a filesystem-backed harness.

Do not treat the update flash bundle as a VM image. It is a flash package and device boot reference, not a faithful runtime target.

That means:

- use the EMMC dump for config diffs, reset baselines, and conversion script smoke tests
- use the update bundle for flash/recovery references
- keep real turn/audio regression on the robot

For the next implementation step, see [robot-image-test-harness-plan.md](robot-image-test-harness-plan.md).
