# Public Site Plan

## Goal

Stand up a small public site and web app on `openjibo.com` that makes the project understandable in a few minutes and gives the owner-facing web surface a real home.

`jiborevived.com` remains the community-maintained Jibo Revival Group hub and status space. `openjibo.com` is the Open Jibo showcase, account entry surface, and hosted-cloud entry point.

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

## Subscription And Onboarding Split

The public site should make the hosted-cloud path explicit instead of hiding it inside one general account page.

Recommended host split:

- `openjibo.com`: showcase, docs, account entry, and owner-facing overview
- `auth.openjibo.com`: authentication, robot/loop registration, and token issuance
- `cloud.openjibo.com` or `members.openjibo.com`: subscription management and hosted-cloud access control

Recommended onboarding flow:

1. Start onboarding from `openjibo.com` or the robot onboarding entry.
2. If hosted access is needed, hand off to the subscription surface.
3. The subscription surface completes signup, payment, or plan selection.
4. The provider returns a signed success or failure result to onboarding.
5. On success, onboarding resumes and completes robot setup.
6. On failure, onboarding stops and surfaces the reason clearly.
7. If the user later cancels a subscription, the hosted cloud should revoke access until the account is validated again through the authorized flow.

## What The Site Should Not Pretend Yet

- zero-touch recovery
- complete parity with the original cloud
- public production readiness before device validation is repeatable
- that `neohub.openjibo.com` needs a separate public web presence unless routing evidence proves it

## Initial Repo Asset

A simple static site scaffold lives in [src/OpenJibo.Site](/OpenJibo/src/OpenJibo.Site).
