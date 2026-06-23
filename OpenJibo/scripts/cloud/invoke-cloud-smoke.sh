#!/usr/bin/env bash
set -euo pipefail

base_url="${BASE_URL:-${BASEURL:-${BASE_URL_OLD:-http://localhost:5000}}}"
test_email="${TEST_EMAIL:-openjibo-smoke@example.com}"
test_password="${TEST_PASSWORD:-OpenJiboSmokePass!42}"
test_first_name="${TEST_FIRST_NAME:-Open}"
test_last_name="${TEST_LAST_NAME:-Jibo}"
test_robot_id="${TEST_ROBOT_ID:-open-jibo-smoke-robot}"

python3 - "$base_url" "$test_email" "$test_password" "$test_first_name" "$test_last_name" "$test_robot_id" <<'PY'
import json
import sys
from dataclasses import dataclass
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from typing import Any, Dict, List, Optional

base_url, test_email, test_password, test_first_name, test_last_name, test_robot_id = sys.argv[1:7]


@dataclass
class Result:
    name: str
    success: bool
    status_code: Optional[int]
    body: Optional[Any]
    body_text: Optional[str]
    error: Optional[str] = None


def request_json(name: str, method: str, url: str, headers: Optional[Dict[str, str]] = None, body: Optional[Dict[str, Any]] = None) -> Result:
    request_headers = dict(headers or {})
    data = None
    if body is not None:
        data = json.dumps(body).encode("utf-8")
        request_headers.setdefault("Content-Type", "application/json")

    req = Request(url, data=data, headers=request_headers, method=method)

    try:
        with urlopen(req) as response:
            status_code = getattr(response, "status", None)
            body_text = response.read().decode("utf-8")
            parsed = None
            if body_text.strip():
                try:
                    parsed = json.loads(body_text)
                except Exception:
                    parsed = body_text
            return Result(name, True, status_code, parsed, body_text)
    except HTTPError as exc:
        body_text = None
        try:
            body_text = exc.read().decode("utf-8")
        except Exception:
            body_text = None
        return Result(name, False, exc.code, body_text, body_text, str(exc))
    except URLError as exc:
        return Result(name, False, None, None, None, str(exc))


results: List[Result] = []

def add_result(result: Result) -> Result:
    results.append(result)
    return result


add_result(request_json("Health", "GET", f"{base_url.rstrip('/')}/health", {}))

account = request_json(
    "AccountCreate",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "Account_20151111.Create",
        "Host": "api.jibo.com",
    },
    {
        "email": test_email,
        "password": test_password,
        "firstName": test_first_name,
        "lastName": test_last_name,
    },
)
add_result(account)

if not account.success and account.status_code != 409:
    raise SystemExit(f"Account create failed with status code {account.status_code}: {account.error}")

if not account.success:
    login = request_json(
        "AccountLogin",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Account_20151111.Login",
            "Host": "api.jibo.com",
        },
        {
            "email": test_email,
            "password": test_password,
        },
    )
    add_result(login)
    if not login.success:
        raise SystemExit(f"Account login failed with status code {login.status_code}: {login.error}")
    account = login

account_id = None
if isinstance(account.body, dict) and "id" in account.body:
    account_id = str(account.body["id"])

loops = request_json(
    "LoopList",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "Loop_20160324.ListLoops",
        "Host": "api.jibo.com",
    },
    {},
)
add_result(loops)
if not loops.success:
    raise SystemExit(f"Loop list failed with status code {loops.status_code}: {loops.error}")

loop_id = "openjibo-default-loop"
if isinstance(loops.body, list) and loops.body and isinstance(loops.body[0], dict) and "id" in loops.body[0]:
    loop_id = str(loops.body[0]["id"])

members = request_json(
    "LoopListMembers",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "Loop_20160324.ListMembers",
        "Host": "api.jibo.com",
    },
    {"loopId": loop_id},
)
add_result(members)
if not members.success:
    raise SystemExit(f"Loop members failed with status code {members.status_code}: {members.error}")

prepare_body = {"loopId": loop_id}
if account_id:
    prepare_body["accountId"] = account_id

prepare = request_json(
    "PrepareRobot",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.PrepareRobot",
        "Host": "api.jibo.com",
    },
    prepare_body,
)
add_result(prepare)
if not prepare.success:
    raise SystemExit(f"PrepareRobot failed with status code {prepare.status_code}: {prepare.error}")

token = None
if isinstance(prepare.body, dict) and "token" in prepare.body:
    token = str(prepare.body["token"])
if not token:
    raise SystemExit("PrepareRobot did not return a token.")

status_before = request_json(
    "GetStatusBeforeSetup",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.GetStatus",
        "Host": "api.jibo.com",
    },
    {"token": token},
)
add_result(status_before)
if not status_before.success:
    raise SystemExit(f"GetStatus before setup failed with status code {status_before.status_code}: {status_before.error}")

setup = request_json(
    "SetupRobot",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.SetupRobot",
        "Host": "api.jibo.com",
    },
    {"token": token, "id": test_robot_id},
)
add_result(setup)
if not setup.success:
    raise SystemExit(f"SetupRobot failed with status code {setup.status_code}: {setup.error}")

status_after = request_json(
    "GetStatusAfterSetup",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.GetStatus",
        "Host": "api.jibo.com",
    },
    {"token": token},
)
add_result(status_after)
if not status_after.success:
    raise SystemExit(f"GetStatus after setup failed with status code {status_after.status_code}: {status_after.error}")

print(json.dumps([result.__dict__ for result in results], indent=2))
PY
