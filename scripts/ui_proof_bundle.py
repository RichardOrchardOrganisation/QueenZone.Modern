"""Package CI evidence and validate downloaded archives without extracting or executing them."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import sys

from ui_proof_archive import PNG, LIMIT, valid_png, passed_cases, validate_archive


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
