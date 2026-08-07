#!/usr/bin/env python
"""Push robot log lines to the authenticated OpenJibo diagnostic beacon endpoint."""

from __future__ import print_function

import argparse
import json
import os
import time

try:
    from urllib.request import Request, urlopen
except ImportError:  # Python 2 robots
    from urllib2 import Request, urlopen

LOG_PATH = "/var/log/messages"


def post_lines(url, robot_id, token, lines):
    body = json.dumps({"lines": lines}).encode("utf-8")
    request = Request(url, data=body)
    request.add_header("Content-Type", "application/json")
    request.add_header("X-Jibo-RobotId", robot_id)
    request.add_header("X-Jibo-Diagnostic-Token", token)
    response = urlopen(request, timeout=15)
    response.read()
    response.close()


def run(log_path, push_url, robot_id, token, interval):
    with open(log_path, "r") as log_file:
        history = log_file.readlines()[-100:]
        pending = [line.rstrip("\r\n") for line in history if line.strip()]
        last_push = 0
        while True:
            line = log_file.readline()
            if line:
                value = line.rstrip("\r\n")
                if value:
                    pending.append(value)

            now = time.time()
            if pending and (len(pending) >= 25 or now - last_push >= interval):
                try:
                    post_lines(push_url, robot_id, token, pending[:100])
                    del pending[:100]
                    last_push = now
                except Exception as error:
                    print("Beacon publish failed: %s" % error)
                    time.sleep(min(interval, 5))
            if not line:
                time.sleep(0.2)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--log-path", default=LOG_PATH)
    parser.add_argument("--push-url", required=True)
    parser.add_argument("--robot-id", required=True)
    parser.add_argument("--token", default=os.environ.get("JIBO_DIAGNOSTIC_BEACON_TOKEN"))
    parser.add_argument("--interval", type=float, default=1.0)
    args = parser.parse_args()
    if not args.token:
        parser.error("--token or JIBO_DIAGNOSTIC_BEACON_TOKEN is required")
    run(args.log_path, args.push_url, args.robot_id, args.token, args.interval)
