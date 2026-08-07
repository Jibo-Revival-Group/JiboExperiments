#!/usr/bin/env python2
"""
Jibo Robot Log Streamer (Python 2.7 / Python 3 compatible, zero-dep)
Streams /var/log/messages via Server-Sent Events over LAN.
Run on-demand for debugging: python log_streamer.py [--port 8766]
"""

import sys
import os
import time
import argparse
try:
    from BaseHTTPServer import HTTPServer, BaseHTTPRequestHandler
except ImportError:
    from http.server import HTTPServer, BaseHTTPRequestHandler

LOG_PATH = "/var/log/messages"
DEFAULT_PORT = 8766

class LogStreamHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/health' or self.path == '/':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            self.wfile.write('{"status": "ok", "log_path": "%s"}\n' % LOG_PATH)
            return

        if self.path == '/stream':
            self.send_response(200)
            self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Cache-Control', 'no-cache')
            self.send_header('Connection', 'keep-alive')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()

            # Send initial history (last 100 lines)
            if os.path.exists(LOG_PATH):
                try:
                    f_hist = open(LOG_PATH, 'r')
                    lines = f_hist.readlines()
                    f_hist.close()
                    last_100 = lines[-100:] if len(lines) > 100 else lines
                    for line in last_100:
                        self.wfile.write('data: %s\n\n' % line.strip())
                        self.wfile.flush()
                except Exception as e:
                    try:
                        self.wfile.write('data: Error reading history: %s\n\n' % str(e))
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

def run_server(host='0.0.0.0', port=DEFAULT_PORT):
    server = HTTPServer((host, port), LogStreamHandler)
    print("Starting Jibo log streamer on %s:%d..." % (host, port))
    server.serve_forever()

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=DEFAULT_PORT)
    parser.add_argument('--host', default='0.0.0.0')
    args = parser.parse_args()
    run_server(args.host, args.port)
