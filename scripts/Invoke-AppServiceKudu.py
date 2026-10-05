#!/usr/bin/env python3
"""Kudu helpers used by deploy.yml (publish-profile Basic auth).

This talks to SCM/Kudu only. It cannot set ARM application settings such as
WEBSITE_RUN_FROM_PACKAGE — that flag has to be an App Service app setting,
and Kudu POST /api/settings does not persist it there (see #660 deploy).
deploy.yml's configure-app-settings job owns those settings via az CLI /
azure/login instead (#666); this script is restart/cleanup only.

Supported actions:
  --delete-setting NAME   DELETE /api/settings/{NAME} (404 is success)
  --restart               POST /api/app/restart (fallback /api/restart)
  --download-wwwroot PATH Download the exact deployed wwwroot as a zip

Reads AZURE_WEBAPP_PUBLISH_PROFILE. Never prints the profile, username,
or password.
"""

from __future__ import annotations

import argparse
import base64
import os
import shutil
import sys
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET


def parse_msdeploy_profile(xml_text: str) -> tuple[str, str, str]:
    root = ET.fromstring(xml_text)
    profiles = root.findall(".//publishProfile")
    if root.tag == "publishProfile":
        profiles = [root]
    for profile in profiles:
        if profile.get("publishMethod") != "MSDeploy":
            continue
        user = profile.get("userName") or ""
        password = profile.get("userPWD") or ""
        publish_url = profile.get("publishUrl") or ""
        if user and password and publish_url:
            host = publish_url.split(":")[0]
            return user, password, host
    raise SystemExit("MSDeploy publish profile with userName/userPWD/publishUrl was not found.")


def kudu_request(user: str, password: str, host: str, method: str, path: str) -> int:
    token = base64.b64encode(f"{user}:{password}".encode("utf-8")).decode("ascii")
    url = f"https://{host}{path}"
    request = urllib.request.Request(
        url,
        method=method,
        data=b"" if method == "POST" else None,
        headers={
            "Authorization": f"Basic {token}",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            return response.status
    except urllib.error.HTTPError as exc:
        return exc.code


def download_wwwroot(user: str, password: str, host: str, destination: str) -> None:
    token = base64.b64encode(f"{user}:{password}".encode("utf-8")).decode("ascii")
    request = urllib.request.Request(
        f"https://{host}/api/zip/site/wwwroot/",
        method="GET",
        headers={"Authorization": f"Basic {token}"},
    )
    temporary = f"{destination}.partial"
    try:
        with urllib.request.urlopen(request, timeout=180) as response:
            if response.status != 200:
                raise RuntimeError(f"Kudu wwwroot download returned HTTP {response.status}.")
            with open(temporary, "wb") as output:
                shutil.copyfileobj(response, output)
        os.replace(temporary, destination)
    finally:
        if os.path.exists(temporary):
            os.remove(temporary)


def delete_settings(user: str, password: str, host: str, names: list[str]) -> int:
    for name in names:
        if not name or "/" in name or "\\" in name:
            print(f"::error::Invalid setting name '{name}'.", file=sys.stderr)
            return 1
        status = kudu_request(user, password, host, "DELETE", f"/api/settings/{name}")
        if status in (200, 204, 404):
            print(f"Kudu DELETE /api/settings/{name} → HTTP {status}")
        else:
            print(
                f"::error::Kudu DELETE /api/settings/{name} returned HTTP {status}.",
                file=sys.stderr,
            )
            return 1

    return 0


def restart_app(user: str, password: str, host: str) -> int:
    status = kudu_request(user, password, host, "POST", "/api/app/restart")
    if status in (200, 202, 204):
        print(f"Kudu POST /api/app/restart → HTTP {status}")
        return 0
    if status == 404:
        status = kudu_request(user, password, host, "POST", "/api/restart")
        if status in (200, 202, 204):
            print(f"Kudu POST /api/restart → HTTP {status}")
            return 0
    print(f"::error::Kudu restart returned HTTP {status}.", file=sys.stderr)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--delete-setting", action="append", default=[])
    parser.add_argument("--restart", action="store_true")
    parser.add_argument("--download-wwwroot")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return _self_test()

    if not args.delete_setting and not args.restart and not args.download_wwwroot:
        print(
            "::error::Specify --delete-setting, --restart, and/or --download-wwwroot.",
            file=sys.stderr,
        )
        return 2

    xml_text = os.environ.get("AZURE_WEBAPP_PUBLISH_PROFILE", "").strip()
    if not xml_text:
        print("::error::AZURE_WEBAPP_PUBLISH_PROFILE is empty.", file=sys.stderr)
        return 1

    user, password, host = parse_msdeploy_profile(xml_text)

    if args.download_wwwroot:
        download_wwwroot(user, password, host, args.download_wwwroot)
        print(f"Downloaded deployed wwwroot to {args.download_wwwroot}.")

    if delete_settings(user, password, host, args.delete_setting) != 0:
        return 1
    if args.restart:
        return restart_app(user, password, host)

    return 0


def _self_test() -> int:
    sample = """<publishData>
  <publishProfile publishMethod="FTP" userName="ftp-user" userPWD="ftp-secret" publishUrl="ftp.example.com" />
  <publishProfile publishMethod="MSDeploy" userName="$site" userPWD="s3cret" publishUrl="site.scm.azurewebsites.net:443" />
</publishData>"""
    user, password, host = parse_msdeploy_profile(sample)
    assert user == "$site", user
    assert password == "s3cret", password
    assert host == "site.scm.azurewebsites.net", host
    print("Invoke-AppServiceKudu self-test passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
