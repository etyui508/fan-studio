#!/usr/bin/env python3
"""fan_studio site/API/download server.

Only the Python standard library is required. The API performs read-only
queries against the public GitHub repository and local project metadata.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import threading
import time
import urllib.request
from http import HTTPStatus
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "site"
REPO = "etyui508/fan-studio"
RELEASES = f"https://github.com/{REPO}/releases"
LATEST_API = f"https://api.github.com/repos/{REPO}/releases/latest"
APP_VERSION = "1.0.0"

_release_cache = {"at": 0.0, "data": None}
_release_lock = threading.Lock()


def github_latest():
    with _release_lock:
        if _release_cache["data"] is not None and time.time() - _release_cache["at"] < 60:
            return _release_cache["data"]
        req = urllib.request.Request(
            LATEST_API,
            headers={"Accept": "application/vnd.github+json", "User-Agent": "fan-studio-api/1.0"},
        )
        try:
            with urllib.request.urlopen(req, timeout=8) as response:
                data = json.load(response)
        except Exception:
            data = None
        _release_cache["at"] = time.time()
        _release_cache["data"] = data
        return data


def motors():
    rows = []
    path = ROOT / "studio" / "motors.txt"
    if not path.exists():
        return rows
    lines = path.read_text(encoding="utf-8-sig").splitlines()
    header = lines[0].split("|")
    for line in lines[1:]:
        values = line.split("|")
        if len(values) < len(header):
            continue
        row = dict(zip(header, values))
        rows.append(row)
    return rows


def score_list():
    result = []
    for path in sorted((ROOT / "scores").glob("*.fscore")):
        stat = path.stat()
        result.append({
            "name": path.name,
            "bytes": stat.st_size,
            "modified": int(stat.st_mtime),
            "url": f"https://raw.githubusercontent.com/{REPO}/main/scores/{path.name}",
        })
    return result


def latest_payload():
    release = github_latest()
    if not release:
        return {
            "ok": False,
            "version": APP_VERSION,
            "release_url": RELEASES,
            "download_url": RELEASES,
        }
    asset = next(
        (item for item in release.get("assets", []) if item.get("name", "").lower().endswith(".zip")),
        None,
    )
    return {
        "ok": True,
        "version": release.get("tag_name", "").lstrip("v"),
        "published_at": release.get("published_at"),
        "release_url": release.get("html_url"),
        "download_url": asset.get("browser_download_url") if asset else release.get("html_url"),
        "size": asset.get("size") if asset else None,
    }


class Handler(SimpleHTTPRequestHandler):
    role = "site"

    def log_message(self, fmt, *args):
        print(f"[{self.role}] {self.address_string()} {fmt % args}", flush=True)

    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("X-Content-Type-Options", "nosniff")
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(HTTPStatus.NO_CONTENT)
        self.end_headers()

    def do_GET(self):
        path = urlparse(self.path).path
        if self.role == "api":
            return self.api_get(path)
        if self.role == "download":
            return self.download_get(path)
        return super().do_GET()

    def api_get(self, path):
        if path == "/v1/health" or path == "/health":
            return self.json_response({
                "ok": True,
                "service": "fan-api",
                "version": APP_VERSION,
                "time": int(time.time()),
            })
        if path == "/v1/latest":
            return self.json_response(latest_payload())
        if path == "/v1/motors":
            return self.json_response({"ok": True, "count": len(motors()), "motors": motors()})
        if path == "/v1/scores":
            scores = score_list()
            return self.json_response({"ok": True, "count": len(scores), "scores": scores})
        if path == "/v1/links":
            return self.json_response({
                "ok": True,
                "site": "https://fanstudio.bony-doorframe-shortly.top",
                "download": "https://fan-download.bony-doorframe-shortly.top",
                "github": f"https://github.com/{REPO}",
            })
        if path in ("/", "/v1", "/docs"):
            return self.json_response({
                "service": "fan-api",
                "endpoints": [
                    "/v1/health", "/v1/latest", "/v1/motors", "/v1/scores", "/v1/links"
                ],
            })
        self.json_response({"ok": False, "error": "not_found"}, HTTPStatus.NOT_FOUND)

    def download_get(self, path):
        if path in ("/health", "/v1/health"):
            return self.json_response({"ok": True, "service": "fan-download"})
        target = f"https://github.com/{REPO}/releases/latest"
        if path in ("/win", "/windows", "/download"):
            latest = latest_payload()
            target = latest.get("download_url") or target
        self.send_response(HTTPStatus.FOUND)
        self.send_header("Location", target)
        self.send_header("Cache-Control", "no-store")
        self.end_headers()

    def json_response(self, data, status=HTTPStatus.OK):
        body = json.dumps(data, ensure_ascii=False, indent=2).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--role", choices=("site", "api", "download"), required=True)
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--host", default="127.0.0.1")
    args = parser.parse_args()

    class ScopedHandler(Handler):
        role = args.role

        def __init__(self, *handler_args, **handler_kwargs):
            super().__init__(*handler_args, directory=str(SITE), **handler_kwargs)

    server = ThreadingHTTPServer((args.host, args.port), ScopedHandler)
    print(f"fan_studio {args.role} listening on http://{args.host}:{args.port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
