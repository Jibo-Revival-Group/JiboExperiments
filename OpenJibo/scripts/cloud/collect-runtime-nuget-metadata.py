"""Collect bounded NuGet manifests for an exact image inventory, never approve terms."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

spec = importlib.util.spec_from_file_location("nuget_policy", Path(__file__).with_name("review-runtime-sbom.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
BASE = "https://api.nuget.org/v3-flatcontainer/"
MAX_MANIFEST = 1024 * 1024
MAX_TOTAL = 16 * 1024 * 1024
MAX_PACKAGES = 200


def packages(document):
    selected, excluded = {}, []
    for artifact in document["artifacts"]:
        if artifact.get("type") != "dotnet":
            continue
        metadata = artifact.get("metadata")
        if not isinstance(metadata, dict) or metadata.get("type") != "package":
            excluded.append({"name": artifact["name"], "reason": "not_scanner_package_metadata"})
            continue
        name, version = artifact["name"], artifact.get("version")
        if (not isinstance(version, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,99}", name)
                or not re.fullmatch(r"[0-9][A-Za-z0-9.+-]{0,99}", version)
                or artifact.get("purl", "").lower() != f"pkg:nuget/{name}@{version}".lower()):
            raise ValueError("Unsafe or inconsistent NuGet identity")
        key = (name.lower(), version.lower())
        item = selected.setdefault(key, {"name": name, "version": version, "inventory_record_count": 0,
                                         "scanner_sha512_values": []})
        package_hash = metadata.get("sha512", "")
        if not isinstance(package_hash, str) or len(package_hash) > 1024:
            raise ValueError("Malformed scanner package hash")
        if package_hash and package_hash not in item["scanner_sha512_values"]:
            item["scanner_sha512_values"].append(package_hash)
            item["scanner_sha512_values"].sort()
        item["inventory_record_count"] += 1
    if not selected or len(selected) > MAX_PACKAGES:
        raise ValueError("Expected a bounded, nonempty NuGet package set")
    return [selected[key] for key in sorted(selected)], excluded


def manifest_url(package):
    # Only validated package identities from packages(); never use upstream URLs.
    name, version = package["name"].lower(), package["version"].lower()
    return f"{BASE}{name}/{version}/{name}.nuspec"


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise ValueError("NuGet redirect refused")


def download(url):
    if not url.startswith(BASE):
        raise ValueError("Unexpected feed")
    opener = urllib.request.build_opener(NoRedirect())
    request = urllib.request.Request(url, headers={"User-Agent": "OpenJibo-release-review/1", "Accept-Encoding": "identity"})
    with opener.open(request, timeout=30) as response:
        if response.geturl() != url:
            raise ValueError("Unexpected final URL")
        data = response.read(MAX_MANIFEST + 1)
    if not data or len(data) > MAX_MANIFEST:
        raise ValueError("Empty or oversized manifest")
    return data


def inspect_manifest(data, package):
    if not data or len(data) > MAX_MANIFEST:
        raise ValueError("Invalid manifest size")
    text = data.decode("utf-8-sig")
    if "<!DOCTYPE" in text.upper() or "<!ENTITY" in text.upper():
        raise ValueError("XML declarations refused")
    root = ET.fromstring(text)
    local = lambda element: element.tag.rsplit("}", 1)[-1]
    if local(root) != "package":
        raise ValueError("Not a package manifest")
    metadata = [child for child in root if local(child) == "metadata"]
    if len(metadata) != 1:
        raise ValueError("Ambiguous manifest metadata")
    fields = {}
    for child in metadata[0]:
        key = local(child)
        if key in ("id", "version", "license", "licenseUrl", "copyright", "repository"):
            if key in fields:
                raise ValueError("Repeated manifest field")
            fields[key] = {"text": child.text or "", "attributes": dict(child.attrib)}
    for key, expected in (("id", package["name"]), ("version", package["version"])):
        if fields.get(key, {}).get("text", "").strip().lower() != expected.lower():
            raise ValueError("Manifest identity mismatch")
    return fields


def collect(inventory, profile, output, fetch=False, downloader=download,
            release_version=policy.LEGACY_RELEASE):
    if fetch and (output is None or Path(output).exists()):
        raise ValueError("Select a fresh output path before fetching")
    document, inventory_hash = policy.load_inventory(inventory)
    reviewed = policy.review(document, profile, inventory_hash, release_version)
    selected, excluded = packages(document)
    report = {"schema_version": 1, "runtime_image": reviewed["runtime_image"],
              "source_commit": reviewed["source_commit"], "cpu_profile": profile,
              "release_version": release_version,
              "syft_json_sha256": inventory_hash, "package_count": len(selected),
              "excluded_dotnet_records": excluded, "packages": [],
              "license_review_approved": False, "stable_promotion_allowed": False,
              "package_archive_hashes_verified": False,
              "scope": "Downloaded manifests are supplemental unsigned metadata, not proof of package contents, complete notices, corresponding source or legal approval. External URLs and license-file references are not followed."}
    contents = {}
    for item in selected:
        entry = dict(item, manifest_url=manifest_url(item), review_status="unreviewed")
        if fetch:
            data = downloader(entry["manifest_url"])
            entry["manifest_fields"] = inspect_manifest(data, item)
            path = f"manifests/{item['name'].lower()}/{item['version'].lower()}.nuspec"
            contents[path] = data
            entry.update(manifest_path=path, manifest_sha256=hashlib.sha256(data).hexdigest())
            if sum(map(len, contents.values())) > MAX_TOTAL:
                raise ValueError("Manifest collection exceeds size limit")
        report["packages"].append(entry)
    if fetch:
        with zipfile.ZipFile(output, "x", compression=zipfile.ZIP_DEFLATED) as archive:
            for path, data in sorted(contents.items()):
                archive.writestr(path, data)
            archive.writestr("review.json", json.dumps(report, indent=2, sort_keys=True))
    return {"runtime_image": reviewed["runtime_image"], "package_count": len(selected),
            "fetched": fetch, "license_review_approved": False,
            "package_archive_hashes_verified": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True)
    parser.add_argument("--cpu-profile", required=True, choices=sorted(policy.DIGESTS))
    parser.add_argument("--output")
    parser.add_argument("--release-version", choices=[policy.LEGACY_RELEASE, policy.AUDIO_RELEASE],
                        default=policy.LEGACY_RELEASE)
    parser.add_argument("--fetch", action="store_true", help="Fetch manifests from fixed nuget.org endpoint; otherwise plan only")
    args = parser.parse_args()
    if args.fetch and not args.output:
        parser.error("--fetch requires a fresh --output ZIP path")
    if args.fetch and Path(args.output).exists():
        parser.error("Output exists; preserve it and select a fresh path")
    try:
        print(json.dumps(collect(args.inventory, args.cpu_profile, args.output, args.fetch,
                                 release_version=args.release_version), sort_keys=True))
    except (OSError, ValueError, TypeError, AttributeError, KeyError, ET.ParseError):
        parser.exit(1, "NuGet evidence collection failed; no license approval recorded. Preserve existing evidence.\n")


if __name__ == "__main__":
    main()
