"""Offline contract tests: all HTTP and filesystem deployment actions are mocked."""
import contextlib
import importlib.util
import io
import os
from pathlib import Path
import unittest
from unittest.mock import call, patch

spec = importlib.util.spec_from_file_location("kudu", Path(__file__).with_name("Invoke-AppServiceKudu.py"))
kudu = importlib.util.module_from_spec(spec)
spec.loader.exec_module(kudu)
PROFILE = '<publishProfile publishMethod="MSDeploy" userName="fixture" userPWD="fixture" publishUrl="example.invalid:443" />'


class KuduActionTests(unittest.TestCase):
    def setUp(self):
        self.output = contextlib.ExitStack()
        self.addCleanup(self.output.close)
        self.output.enter_context(contextlib.redirect_stdout(io.StringIO()))
        self.output.enter_context(contextlib.redirect_stderr(io.StringIO()))

    def test_delete_success_statuses_preserve_order(self):
        with patch.object(kudu, "kudu_request", side_effect=[200, 204, 404]) as request:
            self.assertEqual(kudu.delete_settings("u", "p", "h", ["a", "b", "c"]), 0)
            self.assertEqual(request.call_args_list, [call("u", "p", "h", "DELETE", f"/api/settings/{name}") for name in ["a", "b", "c"]])

    def test_delete_failure_stops_before_later_setting(self):
        with patch.object(kudu, "kudu_request", return_value=500) as request:
            self.assertEqual(kudu.delete_settings("u", "p", "h", ["a", "b"]), 1)
            request.assert_called_once_with("u", "p", "h", "DELETE", "/api/settings/a")

    def test_invalid_setting_never_reaches_http(self):
        for name in ["", "path/name", "path\\name"]:
            with self.subTest(name=name), patch.object(kudu, "kudu_request") as request:
                self.assertEqual(kudu.delete_settings("u", "p", "h", [name]), 1)
                request.assert_not_called()

    def test_restart_success_does_not_fall_back(self):
        for status in [200, 202, 204]:
            with self.subTest(status=status), patch.object(kudu, "kudu_request", return_value=status) as request:
                self.assertEqual(kudu.restart_app("u", "p", "h"), 0)
                request.assert_called_once_with("u", "p", "h", "POST", "/api/app/restart")

    def test_restart_falls_back_only_for_404(self):
        with patch.object(kudu, "kudu_request", side_effect=[404, 202]) as request:
            self.assertEqual(kudu.restart_app("u", "p", "h"), 0)
            self.assertEqual(request.call_args_list, [call("u", "p", "h", "POST", "/api/app/restart"), call("u", "p", "h", "POST", "/api/restart")])
        with patch.object(kudu, "kudu_request", return_value=403) as request:
            self.assertEqual(kudu.restart_app("u", "p", "h"), 1)
            self.assertEqual(request.call_count, 1)
        with patch.object(kudu, "kudu_request", side_effect=[404, 500]):
            self.assertEqual(kudu.restart_app("u", "p", "h"), 1)

    def test_main_keeps_download_delete_restart_order(self):
        with patch.dict(os.environ, {"AZURE_WEBAPP_PUBLISH_PROFILE": PROFILE}), patch.object(kudu.sys, "argv", ["kudu", "--download-wwwroot", "fixture.zip", "--delete-setting", "a", "--restart"]), patch.object(kudu, "download_wwwroot") as download, patch.object(kudu, "kudu_request", side_effect=[204, 202]) as request:
            actions = unittest.mock.Mock()
            actions.attach_mock(download, "download")
            actions.attach_mock(request, "request")
            self.assertEqual(kudu.main(), 0)
            self.assertEqual(actions.mock_calls, [call.download("fixture", "fixture", "example.invalid", "fixture.zip"), call.request("fixture", "fixture", "example.invalid", "DELETE", "/api/settings/a"), call.request("fixture", "fixture", "example.invalid", "POST", "/api/app/restart")])

    def test_main_skips_restart_after_delete_failure(self):
        with patch.dict(os.environ, {"AZURE_WEBAPP_PUBLISH_PROFILE": PROFILE}), patch.object(kudu.sys, "argv", ["kudu", "--delete-setting", "a", "--restart"]), patch.object(kudu, "kudu_request", return_value=500) as request:
            self.assertEqual(kudu.main(), 1)
            self.assertEqual(request.call_count, 1)


if __name__ == "__main__":
    unittest.main()
