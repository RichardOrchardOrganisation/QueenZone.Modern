import io
import struct
import zlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from ui_proof_bundle import PNG, passed_cases, route_matches, validate_archive, package_web

def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


IMAGE = PNG + chunk(b"IHDR", struct.pack(">IIBBBBB", 1, 1, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"\x00\x00\x00\x00")) + chunk(b"IEND", b"")

TRX = b'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions><UnitTest id="1"><TestMethod className="QueenZone.Web.E2E.SmokeTests" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="1" outcome="Passed" /></Results></TestRun>'''
JUNIT = b'<testsuites><testsuite><testcase name="flow" /></testsuite></testsuites>'


def bundle(platform="web", report=TRX, shot=IMAGE, **changes):
    entry = {"feature": "web.home.index" if platform == "web" else "mobile.home.index", "platform": platform,
             "tests": 1, "fixtures": ["SmokeTests"] if platform == "web" else [], "report": "results.xml", "screenshots": ["screen.png"]}
    manifest = {"version": 1, "headSha": "a" * 40, "testedSha": "b" * 40, "runId": "10", "attempt": "1", "job": "web-browser-tests", "entries": [entry]}
    manifest.update(changes)
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w") as archive:
        archive.writestr("manifest.json", json.dumps(manifest))
        archive.writestr("results.xml", report)
        archive.writestr("screen.png", shot)
    return stream.getvalue()


class ProofTests(unittest.TestCase):
    def test_web_and_mobile_archives(self):
        self.assertEqual(validate_archive(bundle())["entries"][0]["tests"], 1)
        self.assertEqual(validate_archive(bundle("android", JUNIT))["entries"][0]["platform"], "android")

    def test_missing_screenshot_or_bad_identity(self):
        for args in [{"shot": b""}, {"shot": PNG}, {"headSha": "old"}, {"entries": []}, {"testedSha": "unknown"}]:
            archive = bundle(**args)
            with self.assertRaises(ValueError):
                validate_archive(archive)

    def test_zero_skipped_failed_and_wrong_fixture(self):
        for report in [TRX.replace(b'Passed', b'Failed'), TRX.replace(b'Passed', b'NotExecuted'), TRX.replace(b'SmokeTests', b'OtherTests')]:
            archive = bundle(report=report)
            with self.assertRaises(ValueError):
                validate_archive(archive)
        for content in [b'<testsuites/>', b'<testsuites><testcase><skipped/></testcase></testsuites>', b'<testsuites><testcase><failure/></testcase></testsuites>']:
            self.assertEqual(passed_cases(content), 0)

    def test_unsafe_and_duplicate_archive_paths(self):
        for name in ['../outside', '/outside', 'screen.png']:
            data = io.BytesIO(bundle())
            with zipfile.ZipFile(data, 'a') as archive:
                archive.writestr(name, 'bad')
            invalid_archive = data.getvalue()
            with self.assertRaises(ValueError):
                validate_archive(invalid_archive)

    def test_xml_entities_rejected(self):
        with self.assertRaises(ValueError):
            passed_cases(b'<!DOCTYPE a [<!ENTITY x "bad">]><testsuites/>')

    def test_route_matching(self):
        self.assertTrue(route_matches('/', 'http://localhost:5099/'))
        self.assertTrue(route_matches('/news/{id}/{slug}', 'http://localhost:5099/news/1003/example'))
        self.assertFalse(route_matches('/', 'http://localhost:5099/news'))
        self.assertFalse(route_matches('/news/{id}', 'http://localhost:5099/news/1003/other'))

    def test_packaging_requires_all_fixtures_and_matching_page(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            reports = root / 'test-results/local-trx/e2e'
            snapshots = root / 'test-results/e2e/passed-proof'
            maps = root / 'docs/feature-map/web'
            for folder in [reports, snapshots, maps]:
                folder.mkdir(parents=True)
            (reports / 'run.trx').write_bytes(TRX)
            (snapshots / 'one.png').write_bytes(IMAGE)
            (snapshots / 'one.json').write_text(json.dumps({'fixture': 'SmokeTests', 'url': 'http://localhost:5099/', 'screenshot': 'one.png'}))
            feature = {'id': 'web.home.index', 'url': '/', 'specs': ['tests/SmokeTests.cs']}
            (maps / 'home.json').write_text(json.dumps({'entries': [feature]}))
            self.assertEqual(len(package_web(root, root / 'out')), 1)
            feature['specs'].append('tests/MissingTests.cs')
            (maps / 'home.json').write_text(json.dumps({'entries': [feature]}))
            self.assertEqual(package_web(root, root / 'out'), [])


if __name__ == '__main__':
    unittest.main()
