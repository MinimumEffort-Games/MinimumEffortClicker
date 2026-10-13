#!/usr/bin/env python3
"""Linux port of scripts/Update-LicenseInventory.ps1: records every restored NuGet package's licence
metadata and copies its notices. Uses local package metadata only. Restore both solutions first
(the Windows one with -p:EnableWindowsTargeting=true on Linux)."""
import base64, json, os, re, shutil, sys, xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parents[2]
entries = {}
assets_files = sorted(p for d in ("src", "tests") for p in (root / d).rglob("obj/project.assets.json"))
if not assets_files:
    sys.exit("Restore first: no obj/project.assets.json found.")
last_assets = None
for assets_path in assets_files:
    assets = json.loads(assets_path.read_text(encoding="utf-8-sig"))
    last_assets = assets
    for name, library in assets["libraries"].items():
        if library.get("type") != "package" or name in entries:
            continue
        package_dir = next((Path(r) / library["path"] for r in assets["packageFolders"] if (Path(r) / library["path"]).exists()), None)
        if package_dir is None:
            sys.exit(f"Package not found: {name}")
        nuspec = next(package_dir.glob("*.nuspec"))
        meta = ET.parse(nuspec).getroot().find("{*}metadata")
        def text(tag):
            node = meta.find("{*}" + tag)
            return node.text if node is not None and node.text else ""
        license_node = meta.find("{*}license")
        repository = meta.find("{*}repository")
        package_id, version = text("id"), text("version")
        notices = []
        for notice in sorted(p for p in package_dir.iterdir() if p.is_file() and re.match(r"^(license|licence|third.?party.?notices|notice)(\.|$)", p.name, re.I)):
            relative = f"licenses/nuget/{package_id}/{version}/{notice.name}"
            destination = root / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(notice, destination)
            notices.append(relative)
        entries[name] = {
            "package": package_id, "version": version,
            "licenseType": license_node.get("type", "") if license_node is not None else "",
            "license": license_node.text or "" if license_node is not None else "",
            "licenseUrl": text("licenseUrl"),
            "repository": repository.get("url", "") if repository is not None else "",
            "commit": repository.get("commit", "") if repository is not None else "",
            "contentHash": library.get("sha512", ""),
            "localNotices": notices,
        }
# Windows SDK projections are implicit framework references, absent from assets.libraries.
props = (root / "Directory.Build.props").read_text()
sdk_version = re.search(r"<WindowsSdkPackageVersion>(.*?)</WindowsSdkPackageVersion>", props).group(1)
sdk_root = next((Path(r) / "microsoft.windows.sdk.net.ref" / sdk_version for r in last_assets["packageFolders"]
                 if (Path(r) / "microsoft.windows.sdk.net.ref" / sdk_version).exists()), None)
if sdk_root is None:
    sys.exit(f"Pinned Windows SDK projection package {sdk_version} was not restored.")
sdk_meta = ET.parse(sdk_root / "microsoft.windows.sdk.net.ref.nuspec").getroot().find("{*}metadata")
entries[f"Microsoft.Windows.SDK.NET.Ref/{sdk_version}"] = {
    "package": "Microsoft.Windows.SDK.NET.Ref", "version": sdk_version, "licenseType": "url",
    "license": "Microsoft Windows SDK licence", "licenseUrl": sdk_meta.find("{*}licenseUrl").text,
    "contentHash": (sdk_root / f"microsoft.windows.sdk.net.ref.{sdk_version}.nupkg.sha512").read_text().strip(),
    "localNotices": [], "implicitFrameworkReference": True,
}
inventory = [entries[k] for k in sorted(entries, key=str.lower)]
(root / "licenses" / "nuget-inventory.json").write_text(json.dumps(inventory, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"Recorded {len(inventory)} package licences and copied available notices.")
