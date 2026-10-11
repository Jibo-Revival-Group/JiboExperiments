# Neutral public-site domain cutover — preparation only

No DNS, GitHub custom-domain or Azure binding was changed by this document.
The approved experimental site update is published separately through the
manual GitHub Pages workflow. A runtime deployment is not part of this lane.

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

Confirm the apex/`www` cutover target and where DNS is administered. The generated
verification TXT value must come from the publishing owner's GitHub Pages settings.
An operator with DNS access must apply the narrowly scoped records; no domain
credentials, verification token or private zone export should be pasted into Git.
