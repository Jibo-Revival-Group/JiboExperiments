# Public Site Plan

## Goal

Stand up a small public site and web app on `openjibo.com` that makes the project understandable in a few minutes and gives the owner-facing web surface a real home.

`jiborevived.com` remains the community-maintained Jibo Revival Group hub and status space. `openjibo.com` is the Open Jibo showcase, account entry surface, and hosted-cloud entry point.

## Current Status (`2026-08-18`)

Status: `ready` for neutral site implementation.

The repository contains a static placeholder only. The first implementation should explain the platform, link the Jibo Revival Group and source repositories, show device/conversion information, compare hosting choices consistently, and provide clean contact routing. Commercial membership belongs on each provider's own clearly labeled surface.

## First Version Content

- project overview
- current status
- links to source repositories
- roadmap / long-range plan
- links to device bootstrap docs
- explanation of the hosted-cloud direction
- owner/account entry flows that can hand off into auth or onboarding
- a clear path into the managed cloud experience when applicable
- contribution/contact or waitlist path

## Hosting Choice And Onboarding Split

The public site should make the hosted-cloud path explicit instead of hiding it inside one general account page.

Recommended host split:

- `openjibo.com`: showcase, docs, account entry, and owner-facing overview
- `auth.openjibo.com`: only if a shared neutral identity authority is intentionally separated; do not assume one commercial provider owns ecosystem identity
- `cloud.openjibo.com`: Transcendent Software LLC's clearly labeled paid managed service
- other admitted provider domains: their own terms, status, support, privacy, and onboarding surfaces

Recommended onboarding flow:

1. Start from `openjibo.com` or the robot conversion/onboarding entry.
2. Compare managed OpenJibo Cloud, other community providers, owner-managed hosting, self-hosted hybrid, and self-hosted isolated choices.
3. Explicitly select a provider or self-hosted target.
4. If the target requires signup/payment or other authorization, use a short-lived signed handoff to that provider.
5. The provider returns a signed success/failure result bound to the onboarding session.
6. On success, onboarding writes and verifies the selected provider target.
7. On failure, onboarding stops safely and preserves retry, provider-switch, export, and self-hosted recovery choices.
8. On cancellation/revocation, the provider applies its policy without silently moving the robot elsewhere or deleting owner data.

## Hosting Choices Page

Compare every option on the same dimensions:

- operator and source repository
- price
- setup difficulty
- maintenance responsibility
- backups and restore behavior
- privacy/data-location summary
- support and public status
- supported device/conversion requirements
- shared identity/sync dependency
- export, provider-switch, and recovery path

The Transcendent Software service can be featured, but it must be labeled as commercial and should not hide self-hosting or future admitted providers.

## Contact

Route a minimal form by topic: platform/community, compatibility/conversion, provider application, managed-service account/sales, managed-service support, security/privacy, and partnership/press. Collect only what is necessary, disclose the recipient and retention, rate-limit abuse, and keep vulnerability reports out of the general form.

## What The Site Should Not Pretend Yet

- zero-touch recovery
- complete parity with the original cloud
- public production readiness before device validation is repeatable
- that `neohub.openjibo.com` needs a separate public web presence unless routing evidence proves it

## Initial Repo Asset

A simple static site scaffold lives in [src/OpenJibo.Site](/OpenJibo/src/OpenJibo.Site).
