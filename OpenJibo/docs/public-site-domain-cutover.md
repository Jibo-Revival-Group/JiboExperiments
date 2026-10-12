# Neutral public-site domain cutover

This runbook was prepared before the owner-approved domain cutover. A runtime
deployment is not part of this lane; the later actions are recorded below.

## Executed progress, 2026-10-11

- The site update at `73dceb3a730209ecde5656801468dd9ce7d3ab33` passed CI and
  [Pages publication 38182930424](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38182930424).
- The owner verified `openjibo.com`; the repository's Pages custom domain was
  then set to that exact apex. The API reports `protected_domain_state: verified`.
- The owner changed only the web-routing records. Both authoritative nameservers
  and Google's public resolver returned all four proposed Pages IPv4 addresses;
  `www` points directly to `transcendentsoftware-jd.github.io`.
- HTTP homepage, downloads and stylesheet returned 200. The current experimental
  version, both ZIP checksums and all four ZIP/signature links were verified on
  the published site. API CNAME and Microsoft 365 MX answers remain unchanged.
- HTTPS is **not complete**: Pages still reports no certificate and enforcement
  disabled. Its domain health response still reports a non-Pages apex address,
  contrary to the current authoritative/public DNS answers. Do not claim TLS
  acceptance or toggle/delete DNS repeatedly to work around this cached check.

Once the certificate becomes available, enable HTTPS enforcement and verify
apex/`www` redirects, downloads and CSS without bypassing certificate validation.
No source `CNAME` file, runtime deployment or Azure binding was added.

Read-only managed-cloud inventory confirmed that `rg-openjibo-prod` contains the
robot runtime `openjibo-cloud`, not a separate `openjibo-managed-api` customer app.
The managed app remains in staging at revision `openjibo-managed-api--39e5cd41f081`;
its GitHub repository has only the staging protected environment. Do not point
`cloud.openjibo.com` to that staging app or the robot runtime. Production bootstrap
needs a reviewed resource/configuration/cost plan and separate owner approval.

## Observed public configuration, 2026-10-11

Read-only queries to authoritative server `ns31.domaincontrol.com` returned:

| Record | Current answer | Treatment |
| --- | --- | --- |
| `openjibo.com` A | `13.248.243.5`, `76.223.105.230` | Snapshot; replacement needs owner approval |
| `www.openjibo.com` CNAME | `openjibo.com` | Snapshot; replacement needs owner approval |
| `cloud.openjibo.com` A lookup | DNS name does not exist | Separate managed-service DNS/TLS lane |
| `api.openjibo.com` CNAME | `openjibo-cloud.salmoncliff-b5e9ebc7.centralus.azurecontainerapps.io` | Preserve |
| `openjibo.com` MX | `openjibo-com.mail.protection.outlook.com`, preference 0 | Preserve |
| Authoritative NS | `ns31.domaincontrol.com`, `ns32.domaincontrol.com` | Preserve; not a nameserver migration |

GitHub Pages reports workflow publication, HTTPS enforced, no custom domain,
and `https://transcendentsoftware-jd.github.io/JiboExperiments/` as the current URL.
The DNS snapshot is not a complete zone export. IPv6, CAA, TXT, DKIM, verification,
SRV, socket/NeoHub and other records must be inventoried before any cutover.
The workstation's DNS command did not support CAA; do not infer absence from that.
The cloud lookup does not prove the managed application itself is unavailable;
it only establishes that this public hostname is not provisioned in the queried
zone. The site now describes it as planned rather than offering a broken link.

## Proposed owner-approved sequence

The GitHub instructions recommend domain verification first, adding the custom
domain to Pages before DNS routing, and a direct `www` CNAME without the repository
path. The current repository belongs to `transcendentsoftware-jd`; account versus
organization verification must match the chosen publishing ownership. Do not
invent the verification TXT token or configure a different owner's account.
[Domain verification](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/verifying-your-custom-domain-for-github-pages).

For the current repository, the proposed routing is:

| DNS type | Host | Proposed value |
| --- | --- | --- |
| A | `@` | `185.199.108.153` |
| A | `@` | `185.199.109.153` |
| A | `@` | `185.199.110.153` |
| A | `@` | `185.199.111.153` |
| CNAME | `www` | `transcendentsoftware-jd.github.io` |

These are GitHub's documented values, not changes already made.
[Custom-domain configuration](https://docs.github.com/en/pages/configuring-a-custom-domain-for-your-github-pages-site/managing-a-custom-domain-for-your-github-pages-site).

1. Owner confirms replacement of the current apex/`www` website and publishing
   ownership. Export the complete zone and record current website/redirect behavior.
2. Owner adds the exact GitHub-generated verification TXT and verifies ownership.
3. With separate authorization, configure Pages custom domain `openjibo.com`.
4. Owner/operator changes only the approved web-routing records. Preserve email,
   API/socket/NeoHub, cloud-service and other unrelated records. Review existing
   apex IPv6/CAA records for compatibility; do not delete unknown records wholesale.
5. Check authoritative and public resolvers, certificate issuance, HTTPS enforcement,
   apex/`www` redirects, downloads, CSS, keyboard navigation and all unaffected services.
6. Roll back only the changed web records to their saved values if validation fails;
   coordinate Pages settings and TLS, not just DNS. Do not leave dangling mappings.

With Actions publication, a source `CNAME` file is not required. Domain association
is a repository setting; no file is added to the site's allowlisted payload.

## Managed-cloud hostname is separate

Do not point `cloud.openjibo.com` at this neutral Pages site or the robot runtime.
First confirm the managed control-plane production target, Azure custom-domain
verification/TLS, authentication redirect/cookie settings and its owner approval.
Then provision its DNS and verify sign-in independently. Paid-provider accounts
must not become prerequisites for reading or downloading self-hosted packages.

## Owner input needed

The apex/`www` target is approved and its DNS changes are complete. Preserve the
verification TXT record. HTTPS is now enforced and no further web DNS input is
needed. Managed-cloud production creation and its later domain/Entra activation
remain separate approval steps. No domain credentials, verification token or private
zone export should be pasted into Git.

## Publication recovery — 2026-10-12

Unpublishing and republishing changed Pages to legacy branch publication from
`main` at `/`. That generated a Jekyll page from the repository README, not the
intended `OpenJibo/src/OpenJibo.Site` artifact. `/site.css` and `/downloads.html`
returned 404. The site source was already merged to `main`; this was not a
development-branch or CSS-source issue.

Restored **Settings → Pages → Build and deployment → Source → GitHub Actions**,
preserving `openjibo.com` and HTTPS enforcement, then manually ran
`openjibo-public-site.yml` on `main`. [Run 38251443557](https://github.com/transcendentsoftware-jd/JiboExperiments/actions/runs/38251443557)
passed all seven site contracts and deployed source
`a042f0065682d7bc395b117b820222c6289b6376`. Live HTTPS checks confirmed the intended
homepage, `/site.css` (`text/css`) and `/downloads.html` all returned 200.

For future republishing, keep the source set to GitHub Actions and dispatch that
workflow on `main`; do not select branch-root publication or relocate/duplicate
the site into the repository root. A hard refresh can clear a previously cached
README page. No robot/customer application, Azure resource or DNS record was
changed by this recovery.
