#!/usr/bin/env python2
"""
Jibo Robot Log Streamer (Python 2.7 / Python 3 compatible, zero-dep)
Streams /var/log/messages via Server-Sent Events over LAN.
Run on-demand for debugging: python log_streamer.py [--host 192.168.1.20 --port 8766
--token <secret> --origin https://portal.example]
The default loopback bind needs no token. LAN binds should always use --token and
--origin (or JIBO_LOG_STREAM_TOKEN/JIBO_LOG_STREAM_ORIGIN).
Port defaults to 8766 so it does not collide with the credentials API on 8765.
"""

import sys
import os
import time
import argparse
try:
    from BaseHTTPServer import BaseHTTPRequestHandler
    from SocketServer import ThreadingMixIn, TCPServer
except ImportError:
    from http.server import BaseHTTPRequestHandler
    from socketserver import ThreadingMixIn, TCPServer

LOG_PATH = "/var/log/messages"
DEFAULT_PORT = 8766
DEFAULT_BIND_HOST = '127.0.0.1'

class ThreadingHTTPServer(ThreadingMixIn, TCPServer):
    daemon_threads = True
    allow_reuse_address = True

class LogStreamHandler(BaseHTTPRequestHandler):
    server_version = 'JiboLogStreamer/1.0'

    def _authorized(self):
        expected = self.server.access_token
        if not expected:
            # An unconfigured streamer is safe only for local diagnostics.
            return self.client_address[0] in ('127.0.0.1', '::1')
        supplied = self.headers.get('Authorization', '')
        if supplied.startswith('Bearer '):
            supplied = supplied[7:]
        if not supplied:
            try:
                from urllib.parse import urlparse, parse_qs
            except ImportError:
                from urlparse import urlparse, parse_qs
            supplied = parse_qs(urlparse(self.path).query).get('token', [''])[0]
        return supplied == expected

    def _cors(self):
        origin = self.server.allowed_origin
        if origin:
            self.send_header('Access-Control-Allow-Origin', origin)

    def _write(self, value):
        if not isinstance(value, bytes):
            value = value.encode('utf-8')
        self.wfile.write(value)

    def do_GET(self):
        if not self._authorized():
            self.send_response(401)
            self.send_header('Content-Type', 'application/json')
            self._cors()
            self.end_headers()
            self._write('{"error":"unauthorized"}\n')
            return
        try:
            from urllib.parse import urlparse
        except ImportError:
            from urlparse import urlparse
        route = urlparse(self.path).path
        if route == '/health' or route == '/':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self._cors()
            self.end_headers()
            self._write('{"status": "ok", "log_path": "%s"}\n' % LOG_PATH)
            return

        if route == '/stream':
            self.send_response(200)
            self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Cache-Control', 'no-cache')
            self.send_header('Connection', 'keep-alive')
            self._cors()
            self.end_headers()

            # Send initial history (last 100 lines)
            if os.path.exists(LOG_PATH):
                try:
                    f_hist = open(LOG_PATH, 'r')
                    lines = f_hist.readlines()
                    f_hist.close()
                    last_100 = lines[-100:] if len(lines) > 100 else lines
                    for line in last_100:
                        self._write('data: %s\n\n' % line.strip())
                        self.wfile.flush()
                except Exception as e:
                    try:
                        self._write('data: Error reading history: %s\n\n' % str(e))
                        self.wfile.flush()
                    except Exception:
                        pass

            # Tail new lines
            try:
                if os.path.exists(LOG_PATH):
                    f = open(LOG_PATH, 'r')
                    f.seek(0, 2)
                    while True:
                        line = f.readline()
                        if not line:
                            time.sleep(0.1)
                            continue
                        self.wfile.write('data: %s\n\n' % line.strip())
                        self.wfile.flush()
            except Exception:
                pass
            return

        self.send_response(404)
        self.end_headers()

    def log_message(self, format, *args):
        pass

def run_server(host=DEFAULT_BIND_HOST, port=DEFAULT_PORT, access_token=None, allowed_origin=None):
    server = ThreadingHTTPServer((host, port), LogStreamHandler)
    server.access_token = access_token or os.environ.get('JIBO_LOG_STREAM_TOKEN')
    server.allowed_origin = allowed_origin or os.environ.get('JIBO_LOG_STREAM_ORIGIN')
    print("Starting Jibo log streamer on %s:%d..." % (host, port))
    server.serve_forever()

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=DEFAULT_PORT)
    parser.add_argument('--host', default=DEFAULT_BIND_HOST)
    parser.add_argument('--token', default=None)
    parser.add_argument('--origin', default=None)
    args = parser.parse_args()
    run_server(args.host, args.port, args.token, args.origin)
