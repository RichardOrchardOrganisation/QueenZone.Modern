"""Package CI evidence and validate downloaded archives without extracting or executing them."""
import argparse
import io
import json
from pathlib import Path, PurePosixPath
import re
import struct
import zlib
import sys
import xml.etree.ElementTree as ET
import zipfile

PNG = b"\x89PNG\r\n\x1a\n"
LIMIT = 50 * 1024 * 1024


def valid_png_header(kind, chunk):
    return kind == b"IHDR" and len(chunk) == 13 and all(struct.unpack(">II", chunk[:8]))


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
        if not header and not valid_png_header(kind, chunk):
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
        validate_manifest_identity(manifest)
        entries = manifest.get("entries", [])
        if not isinstance(entries, list) or not entries:
            raise ValueError("No verified features")
        for entry in entries:
            validate_entry(archive, entry)
        return manifest


def validate_manifest_identity(manifest):
    if manifest.get("version") != 1 or not re.fullmatch(r"[a-f0-9]{40}", manifest.get("headSha", "")):
        raise ValueError("Invalid manifest identity")
    if not re.fullmatch(r"[a-f0-9]{40}", manifest.get("testedSha", "")) or not str(manifest.get("runId", "")).isdigit() or not str(manifest.get("attempt", "")).isdigit():
        raise ValueError("Invalid run provenance")
    if not re.fullmatch(r"[A-Za-z0-9_-]+", manifest.get("job", "")):
        raise ValueError("Missing producer job")


def validate_entry(archive, entry):
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", entry.get("platform", "")) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", entry.get("feature", "")):
        raise ValueError("Invalid feature/platform")
    report = archive.read(entry["report"])
    counts = ([passed_cases(report, fixture) for fixture in entry.get("fixtures", [])]
              if entry.get("fixtures") else [passed_cases(report)])
    if not counts or not all(counts) or sum(counts) != entry.get("tests"):
        raise ValueError("Missing, skipped or failed behavioural tests")
    shots = entry.get("screenshots", [])
    if not shots or any(not valid_png(archive.read(shot)) for shot in shots):
        raise ValueError("Missing screenshot evidence")



def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["validate", "pack"])
    parser.add_argument("--source", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    if args.mode == "validate":
        print(json.dumps(validate_archive(sys.stdin.buffer.read(LIMIT + 1))))
        return
    if args.source is None or args.output is None:
        parser.error("pack requires --source (manifest and evidence directory) and --output (ZIP)")
    source = args.source.resolve(strict=True)
    if args.output.resolve().is_relative_to(source):
        parser.error("output ZIP must be outside the evidence directory")
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", zipfile.ZIP_DEFLATED) as archive:
        for file in source.rglob("*"):
            if file.is_symlink():
                raise ValueError("Evidence symlinks are not permitted")
            if file.is_file():
                archive.write(file, file.relative_to(source).as_posix())
    data = stream.getvalue()
    validate_archive(data)
    args.output.write_bytes(data)


if __name__ == "__main__":
    main()
