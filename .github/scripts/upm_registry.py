"""Builds the static UPM registry of com.feeder.mcp.

pack <tag> <out_dir>                 tarball of Packages/com.feeder.mcp at <tag> (package/ prefix, like npm pack)
entry <tag> <tgz> <tarball_url> <registry_dir>
                                     writes versions/<version>.json for one packed tag
notes <tag>                          prints the CHANGELOG section of that version (release notes)
index <registry_dir>               rebuilds com.feeder.mcp, -/v1/search and -/all from versions/*.json

The registry is plain files, so any static host can serve it: Unity Package Manager asks for
<registry url>/com.feeder.mcp and follows dist.tarball of the version it installs."""
import base64, hashlib, json, subprocess, sys, time
from pathlib import Path

NAME = "com.feeder.mcp"
PACKAGE_DIR = "Packages/com.feeder.mcp"


def git(*args, binary=False):
    out = subprocess.run(["git", *args], check=True, capture_output=True).stdout
    return out if binary else out.decode("utf-8")


def version_key(v):
    core, _, pre = v.partition("-")
    return tuple(int(x) for x in core.split(".")), pre == "", pre


def pack(tag, out_dir):
    manifest = json.loads(git("show", f"{tag}:{PACKAGE_DIR}/package.json"))
    version = manifest["version"]
    if tag.startswith("v") and tag[1:] != version:
        sys.exit(f"{tag}: package.json says {version}")
    out = Path(out_dir) / f"{NAME}-{version}.tgz"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(git("archive", "--format=tar.gz", "--prefix=package/", f"{tag}:{PACKAGE_DIR}", binary=True))
    print(out)


def entry(tag, tgz, tarball_url, registry_dir):
    manifest = json.loads(git("show", f"{tag}:{PACKAGE_DIR}/package.json"))
    data = Path(tgz).read_bytes()
    manifest.update({
        "_id": f"{NAME}@{manifest['version']}",
        "dist": {
            "tarball": tarball_url,
            "shasum": hashlib.sha1(data).hexdigest(),
            "integrity": "sha512-" + base64.b64encode(hashlib.sha512(data).digest()).decode(),
        },
        "_time": git("log", "-1", "--format=%cI", tag).strip(),
    })
    out = Path(registry_dir) / "versions" / f"{manifest['version']}.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(out)


def notes(tag):
    changelog = git("show", f"{tag}:{PACKAGE_DIR}/CHANGELOG.md")
    version = tag.lstrip("v")
    section = changelog.split(f"## [{version}]", 1)[-1].split("\n## [", 1)[0]
    print(section.split("\n", 1)[-1].strip() if f"## [{version}]" in changelog else f"Release {version}.")


def index(registry_dir):
    root = Path(registry_dir)
    entries = [json.loads(p.read_text(encoding="utf-8")) for p in (root / "versions").glob("*.json")]
    entries.sort(key=lambda e: version_key(e["version"]))
    stable = [e for e in entries if "-" not in e["version"]] or entries
    latest = stable[-1]
    now = time.strftime("%Y-%m-%dT%H:%M:%S.000Z", time.gmtime())
    times = {e["version"]: e.pop("_time") for e in entries}
    doc = {
        "_id": NAME,
        "name": NAME,
        "description": latest.get("description", ""),
        "dist-tags": {"latest": latest["version"]},
        "versions": {e["version"]: e for e in entries},
        "time": {"created": min(times.values()), "modified": now, **times},
        "readme": "",
    }
    (root / NAME).write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")

    summary = {k: latest[k] for k in ("name", "version", "description", "displayName", "author", "keywords") if k in latest}
    search = {"objects": [{"package": {**summary, "date": times[latest["version"]], "links": {}},
                           "score": {"final": 1, "detail": {"quality": 1, "popularity": 1, "maintenance": 1}},
                           "searchScore": 1}],
              "total": 1, "time": now}
    (root / "-" / "v1").mkdir(parents=True, exist_ok=True)
    (root / "-" / "v1" / "search").write_text(json.dumps(search, indent=2) + "\n", encoding="utf-8")
    all_doc = {"_updated": int(time.time() * 1000),
               NAME: {**summary, "dist-tags": {"latest": latest["version"]},
                      "versions": {e["version"]: "latest" if e is latest else "" for e in entries},
                      "time": {"modified": now}}}
    (root / "-" / "all").write_text(json.dumps(all_doc, indent=2) + "\n", encoding="utf-8")
    print(f"{NAME}: {', '.join(e['version'] for e in entries)} (latest {latest['version']})")


if __name__ == "__main__":
    commands = {"pack": pack, "entry": entry, "notes": notes, "index": index}
    if len(sys.argv) < 2 or sys.argv[1] not in commands:
        sys.exit(__doc__)
    commands[sys.argv[1]](*sys.argv[2:])
