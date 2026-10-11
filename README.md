# JiboExperiments

This repository is the working lab for bringing Jibo back.

The active OpenJibo work lives in [OpenJibo/README.md](OpenJibo/README.md), and the long-range story now lives in [OpenJibo/docs/roadmap.md](OpenJibo/docs/roadmap.md).

For fresh native Linux self-hosting experiments, see the
[preview downloads and testing site](https://transcendentsoftware-jd.github.io/JiboExperiments/downloads.html).
The recommended experimental package is `runtime-preview-38165346370`, with
mandatory signature verification and explicit AVX2 opt-in. It is not a stable
release, updater or robot conversion tool; license/notice and live-robot gates
remain open. [Exact package acceptance and limits](OpenJibo/docs/guarded-source-preview-publication-20261011.md).

Repo hygiene: keep generated scratch output, temp captures, and local workspace state out of Git. The `.codex/` tree is ignored by default, and only intentionally shared configuration should be promoted to tracked files.

Start here:

- [OpenJibo overview](OpenJibo/README.md)
- [Roadmap](OpenJibo/docs/roadmap.md)
- [Current cloud truth](OpenJibo/docs/development-plan.md)
- [Working feature queue](OpenJibo/docs/feature-backlog.md)
- [Official site, builds, starters and update plan](OpenJibo/docs/official-distribution-plan.md)
- [Device bootstrap path](OpenJibo/docs/device-bootstrap.md)

This repo intentionally stays a living notebook, not a polished product. That lets us keep the reverse-engineering notes, the hosted cloud, and the post-cloud ideas in one place without pretending they are all equally finished.
