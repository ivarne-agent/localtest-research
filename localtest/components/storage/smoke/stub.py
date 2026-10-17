"""Smoke-test stub on one port: a PDP that permits every decision."""
import json
import sys
from http.server import BaseHTTPRequestHandler, HTTPServer


class Handler(BaseHTTPRequestHandler):
    def _json(self, status, body):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        request = json.loads(self.rfile.read(length) or b"{}")
        if self.path.rstrip("/").endswith("authorization/api/v1/decision"):
            n = max(1, len(((request.get("Request") or {}).get("MultiRequests") or {}).get("RequestReference") or []))
            self._json(200, {"Response": [{"Decision": "Permit"} for _ in range(n)]})
        else:
            self._json(404, {})

    def do_GET(self):
        self._json(404, {})

    def log_message(self, fmt, *args):
        sys.stderr.write("stub: " + (fmt % args) + "\n")


HTTPServer(("127.0.0.1", int(sys.argv[1])), Handler).serve_forever()
