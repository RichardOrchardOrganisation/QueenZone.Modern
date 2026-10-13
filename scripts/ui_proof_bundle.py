"""Package CI evidence and validate downloaded archives without extracting or executing them."""
import argparse
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import struct
import zlib
import sys
import xml.etree.ElementTree as ET
import zipfile

PNG = b"\x89PNG\r\n\x1a\n"
LIMIT = 50 * 1024 * 1024


def valid_png(data):
    if not data.startswith(PNG):
        return False
    offset = len(PNG)
    image_data = False
    header = False
    while offset + 12 <= len(data):
        length = int.from_bytes(data[offset:offset + 4], "big")
        end = offset + 12 + length
        if end > len(data):
            return False
        kind = data[offset + 4:offset + 8]
        chunk = data[offset + 8:offset + 8 + length]
        if zlib.crc32(kind + chunk) != int.from_bytes(data[end - 4:end], "big"):
            return False
        if not header:
            if kind != b"IHDR" or length != 13 or not all(struct.unpack(">II", chunk[:8])):
                return False
            header = True
        image_data = image_data or kind == b"IDAT" and length > 0
        if kind == b"IEND":
            return header and image_data and length == 0 and end == len(data)
        offset = end
    return False


def read_xml(data):
    if b"<!DOCTYPE" in data.upper() or b"<!ENTITY" in data.upper():
        raise ValueError("XML entities are not permitted")
    return ET.fromstring(data)


def passed_cases(data, fixture=None):
    root = read_xml(data)
    if fixture is not None:
        if not root.tag.endswith("}TestRun"):
            return 0
        ns = {"t": root.tag[1:].split("}")[0]}
        definitions = {test.get("id") for test in root.findall(".//t:UnitTest", ns)
                       if test.find("t:TestMethod", ns) is not None
                       and test.find("t:TestMethod", ns).get("className", "").split(",")[0].endswith("." + fixture)}
        cases = [case for case in root.findall(".//t:UnitTestResult", ns) if case.get("testId") in definitions]
        return len(cases) if cases and all(case.get("outcome") == "Passed" for case in cases) else 0
    cases = root.findall(".//testcase")
    if root.tag == "testcase":
        cases = [root]
    return len(cases) if cases and all(not any(case.find(tag) is not None for tag in ("failure", "error", "skipped")) for case in cases) else 0


def route_matches(route, url):
    from urllib.parse import urlsplit
    pattern = re.escape(route)
    pattern = re.sub(r"\\\{.*?\\\}", "[^/]+", pattern)
    return re.fullmatch(pattern.rstrip("/") + "/?", urlsplit(url).path) is not None


def copy_file(source, output, relative):
    target = output / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    return relative


def package_web(root, output):
    reports = sorted((root / "test-results/local-trx/e2e").glob("*.trx"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not reports:
        raise ValueError("No current Playwright TRX report")
    report = reports[0]
    data = report.read_bytes()
    copy_file(report, output, "results.trx")
    snapshots = [(p, json.loads(p.read_text(encoding="utf-8-sig"))) for p in (root / "test-results/e2e/passed-proof").glob("*.json")]
    entries = []
    for mapping in (root / "docs/feature-map/web").glob("*.json"):
        for feature in json.loads(mapping.read_text(encoding="utf-8-sig"))["entries"]:
            specs = [Path(spec).stem for spec in feature.get("specs", [])]
            counts = [passed_cases(data, spec) for spec in specs]
            shots = [(p.parent / shot["screenshot"]) for p, shot in snapshots
                     if shot["fixture"] in specs and route_matches(feature["url"], shot["url"])]
            if not specs or not all(counts) or not shots:
                continue
            folder = feature["id"]
            entries.append({"feature": folder, "platform": "web", "tests": sum(counts), "fixtures": specs,
                            "report": "results.trx",
                            "screenshots": [copy_file(shot, output, folder + "/" + shot.name) for shot in shots[:1]]})
    return entries


def package_mobile(root, output, platform):
    entries = []
    for proof in (root / "artifacts/proof").glob("mobile.*/" + platform + "/*/proof.md"):
        text = proof.read_text(encoding="utf-8-sig")
        def field(name):
            match = re.search(r"^- " + name + r": (.+)$", text, re.M)
            return match[1].strip() if match else ""
        if field("Result") != "PASS" or field("SHA") != os.environ["GITHUB_SHA"]:
            continue
        report = proof.parent / "junit.xml"
        shot = proof.parent / "screenshot.png"
        count = passed_cases(report.read_bytes()) if report.exists() else 0
        if count and shot.exists() and valid_png(shot.read_bytes()):
            feature = field("Feature")
            if not re.fullmatch(r"mobile\.[a-z0-9.-]+", feature):
                raise ValueError("Invalid mobile feature")
            entries.append({"feature": feature, "platform": platform, "tests": count,
                            "report": copy_file(report, output, feature + "/junit.xml"),
                            "screenshots": [copy_file(shot, output, feature + "/screenshot.png")]})
    return entries


def validate_archive(data):
    if len(data) > LIMIT:
        raise ValueError("Proof archive too large")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        members = archive.infolist()
        names = [item.filename for item in members]
        if len(names) != len(set(names)) or len(names) > 2000 or sum(item.file_size for item in members) > LIMIT:
            raise ValueError("Invalid or oversized archive")
        if any(PurePosixPath(name).is_absolute() or ".." in PurePosixPath(name).parts or "\\" in name for name in names):
            raise ValueError("Unsafe archive path")
        manifest = json.loads(archive.read("manifest.json"))
        if manifest.get("version") != 1 or not re.fullmatch(r"[a-f0-9]{40}", manifest.get("headSha", "")):
            raise ValueError("Invalid manifest identity")
        if not re.fullmatch(r"[a-f0-9]{40}", manifest.get("testedSha", "")) or not str(manifest.get("runId", "")).isdigit() or not str(manifest.get("attempt", "")).isdigit():
            raise ValueError("Invalid run provenance")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", manifest.get("job", "")):
            raise ValueError("Missing producer job")
        entries = manifest.get("entries", [])
        if not isinstance(entries, list) or not entries:
            raise ValueError("No verified features")
        for entry in entries:
            if entry.get("platform") not in ("web", "android", "ios") or not re.fullmatch(r"(web|mobile)\.[a-z0-9.-]+", entry.get("feature", "")):
                raise ValueError("Invalid feature/platform")
            report = archive.read(entry["report"])
            counts = ([passed_cases(report, fixture) for fixture in entry.get("fixtures", [])]
                      if entry["platform"] == "web" else [passed_cases(report)])
            if not counts or not all(counts) or sum(counts) != entry.get("tests"):
                raise ValueError("Missing, skipped or failed behavioural tests")
            shots = entry.get("screenshots", [])
            if not shots or any(not valid_png(archive.read(shot)) for shot in shots):
                raise ValueError("Missing screenshot evidence")
        return manifest


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["web", "android", "ios", "validate"])
    args = parser.parse_args()
    if args.mode == "validate":
        print(json.dumps(validate_archive(sys.stdin.buffer.read(LIMIT + 1))))
        return
    root = Path(__file__).resolve().parent.parent
    output = root / "artifacts/ui-proof" / args.mode
    output.mkdir(parents=True, exist_ok=True)
    entries = package_web(root, output) if args.mode == "web" else package_mobile(root, output, args.mode)
    if not entries:
        raise ValueError("No passing feature evidence to upload")
    manifest = {"version": 1, "headSha": os.environ.get("UI_PROOF_HEAD_SHA") or os.environ["GITHUB_SHA"],
                "testedSha": os.environ["GITHUB_SHA"], "runId": os.environ["GITHUB_RUN_ID"],
                "attempt": os.environ["GITHUB_RUN_ATTEMPT"], "job": os.environ["GITHUB_JOB"], "entries": entries}
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")


if __name__ == "__main__":
    main()
