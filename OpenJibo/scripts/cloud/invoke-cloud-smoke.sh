#!/usr/bin/env bash
set -euo pipefail

base_url="${BASE_URL:-${BASEURL:-${BASE_URL_OLD:-http://localhost:5000}}}"
test_email="${TEST_EMAIL:-openjibo-smoke@example.com}"
test_password="${TEST_PASSWORD:-OpenJiboSmokePass!42}"
test_first_name="${TEST_FIRST_NAME:-Open}"
test_last_name="${TEST_LAST_NAME:-Jibo}"
test_robot_id="${TEST_ROBOT_ID:-open-jibo-smoke-robot}"
target_mode="${OPENJIBO_SMOKE_TARGET_MODE:-open-jibo}"
target_host="${OPENJIBO_SMOKE_TARGET_HOST:-}"
reported_connection_host="${OPENJIBO_SMOKE_REPORTED_CONNECTION_HOST:-}"
if [[ -z "$target_host" ]]; then
  if [[ "$target_mode" == "open-jibo-self-hosted" ]]; then
    target_host="$(python3 - "$base_url" <<'PYHOST'
from urllib.parse import urlparse
import sys
parsed = urlparse(sys.argv[1])
print(parsed.netloc or parsed.path)
PYHOST
)"
  else
    target_host="api.openjibo.com"
  fi
fi
if [[ -z "$reported_connection_host" ]]; then
  reported_connection_host="$target_host"
fi
base_host="$(python3 - "$base_url" <<'PY'
from urllib.parse import urlparse
import sys

parsed = urlparse(sys.argv[1])
print(parsed.netloc or parsed.path)
PY
)"

python3 - "$base_url" "$base_host" "$test_email" "$test_password" "$test_first_name" "$test_last_name" "$test_robot_id" "$target_mode" "$target_host" "$reported_connection_host" <<'PY'
import json
import sys
from dataclasses import dataclass
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from typing import Any, Dict, List, Optional

base_url, base_host, test_email, test_password, test_first_name, test_last_name, test_robot_id, target_mode, target_host, reported_connection_host = sys.argv[1:11]


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


def request_json_with_retry(
    name: str,
    method: str,
    url: str,
    headers: Optional[Dict[str, str]] = None,
    body: Optional[Dict[str, Any]] = None,
    attempts: int = 4,
    retry_status_codes: tuple[int, ...] = (500, 502, 503, 504),
) -> Result:
    last_result: Result | None = None
    for attempt in range(1, attempts + 1):
        result = request_json(name, method, url, headers, body)
        last_result = result
        if result.success:
            return result
        if result.status_code not in retry_status_codes or attempt == attempts:
            return result
        print(
            f"{name} attempt {attempt} failed with {result.status_code}; retrying after a short delay...",
            file=sys.stderr,
        )
        import time

        time.sleep(min(5, attempt * 2))

    assert last_result is not None
    return last_result


results: List[Result] = []

def add_result(result: Result) -> Result:
    results.append(result)
    return result


add_result(request_json("Health", "GET", f"{base_url.rstrip('/')}/health", {}))

account = request_json_with_retry(
    "AccountCreate",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "Account_20151111.Create",
        "Host": base_host,
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
    raise SystemExit(
        f"Account create failed with status code {account.status_code}: {account.error}. "
        f"Body: {account.body_text or account.body}"
    )

if not account.success:
    login = request_json(
        "AccountLogin",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Account_20151111.Login",
            "Host": base_host,
        },
        {
            "email": test_email,
            "password": test_password,
        },
    )
    add_result(login)
    if not login.success:
        raise SystemExit(
            f"Account login failed with status code {login.status_code}: {login.error}. "
            f"Body: {login.body_text or login.body}"
        )
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
        "Host": base_host,
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
        "Host": base_host,
    },
    {"loopId": loop_id},
)
add_result(members)
if not members.success:
    raise SystemExit(f"Loop members failed with status code {members.status_code}: {members.error}")

identity_member_id = None
if isinstance(members.body, list) and members.body and isinstance(members.body[0], dict):
    identity_member_id = str(members.body[0].get("id") or "")

if not identity_member_id:
    invite_member = request_json(
        "LoopInviteMember",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Loop_20160324.InviteMember",
            "Host": base_host,
        },
        {
            "loopId": loop_id,
            "email": "openjibo-loop-member@example.com",
            "firstName": "Loop",
            "lastName": "Member",
        },
    )
    add_result(invite_member)
    if not invite_member.success:
        raise SystemExit(f"Loop invite member failed with status code {invite_member.status_code}: {invite_member.error}")
    if isinstance(invite_member.body, dict):
        loop_members = invite_member.body.get("members")
        if isinstance(loop_members, list) and loop_members and isinstance(loop_members[-1], dict):
            identity_member_id = str(loop_members[-1].get("id") or "")

if identity_member_id:
    enrollment = request_json(
        "LoopSetEnrollment",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Loop_20160324.SetEnrollment",
            "Host": base_host,
        },
        {"loopId": loop_id, "id": identity_member_id, "face": True, "voice": True},
    )
    add_result(enrollment)
    if not enrollment.success:
        print(
            f"Loop set enrollment returned {enrollment.status_code}; continuing with recognition smoke because enrollment is best-effort.",
            file=sys.stderr,
        )

    recognition = request_json(
        "LoopRecordRecognitionObservation",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Loop_20160324.RecordRecognitionObservation",
            "Host": base_host,
        },
        {
            "loopId": loop_id,
            "memberId": identity_member_id,
            "modality": "face",
            "outcome": "recognized",
            "confidence": 0.97,
            "source": "conversion-smoke",
        },
    )
    add_result(recognition)
    if not recognition.success:
        raise SystemExit(
            f"Loop recognition observation failed with status code {recognition.status_code}: {recognition.error}"
        )

    recognition_list = request_json(
        "LoopListRecognitionObservations",
        "POST",
        f"{base_url.rstrip('/')}/",
        {
            "X-Amz-Target": "Loop_20160324.ListRecognitionObservations",
            "Host": base_host,
        },
        {"loopId": loop_id},
    )
    add_result(recognition_list)
    if not recognition_list.success:
        raise SystemExit(
            f"Loop recognition observation list failed with status code {recognition_list.status_code}: "
            f"{recognition_list.error}"
        )
    if not (
        isinstance(recognition_list.body, list)
        and any(
            isinstance(observation, dict)
            and str(observation.get("memberId") or "") == identity_member_id
            and str(observation.get("source") or "") == "conversion-smoke"
            for observation in recognition_list.body
        )
    ):
        raise SystemExit("Loop recognition observation list did not include the conversion-smoke evidence.")

prepare_body = {"loopId": loop_id, "rollbackSnapshotId": f"smoke-rollback-{test_robot_id}", "targetMode": target_mode, "targetHost": target_host}
if account_id:
    prepare_body["accountId"] = account_id

plan_conversion = request_json(
    "PlanConversion",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.PlanConversion",
        "Host": base_host,
    },
    prepare_body,
)
add_result(plan_conversion)
if not plan_conversion.success:
    raise SystemExit(
        f"PlanConversion failed with status code {plan_conversion.status_code}: {plan_conversion.error}"
    )
plan_body = plan_conversion.body if isinstance(plan_conversion.body, dict) else {}
plan_readiness = plan_body.get("conversionReadiness") if isinstance(plan_body.get("conversionReadiness"), dict) else {}
if plan_body.get("willWriteRobot"):
    raise SystemExit("PlanConversion unexpectedly reported that it would write the robot.")
if not plan_body.get("canPrepareRobot") or not plan_readiness.get("canWriteRobot"):
    raise SystemExit("PlanConversion did not report a write-safe prepared conversion path.")
if plan_body.get("targetMode") != target_mode:
    raise SystemExit("PlanConversion returned an unexpected Open Jibo target mode.")
if plan_body.get("targetHost") != target_host:
    raise SystemExit("PlanConversion returned an unexpected Open Jibo target host.")

prepare = request_json(
    "PrepareRobot",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.PrepareRobot",
        "Host": base_host,
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
        "Host": base_host,
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
        "Host": base_host,
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
        "Host": base_host,
    },
    {"token": token},
)
add_result(status_after)
if not status_after.success:
    raise SystemExit(f"GetStatus after setup failed with status code {status_after.status_code}: {status_after.error}")

reported_host_mappings = {
    "api.jibo.com": target_host,
    "api-socket.jibo.com": target_host,
    "neo-hub.jibo.com": target_host,
}

connection_proof = request_json(
    "VerifyConnection",
    "POST",
    f"{base_url.rstrip('/')}/",
    {
        "X-Amz-Target": "OOBE_20161026.VerifyConnection",
        "Host": base_host,
    },
    {
        "token": token,
        "reportedConnectionHost": reported_connection_host,
        "reportedHostMappings": reported_host_mappings,
    },
)
add_result(connection_proof)
if not connection_proof.success:
    raise SystemExit(
        f"VerifyConnection failed with status code {connection_proof.status_code}: {connection_proof.error}"
    )

proof_body = connection_proof.body if isinstance(connection_proof.body, dict) else {}
if not proof_body.get("connected"):
    raise SystemExit("VerifyConnection did not report the prepared robot as connected.")
if not proof_body.get("complete"):
    raise SystemExit("VerifyConnection did not report the prepared robot setup as complete.")
if proof_body.get("robotId") != f"robot-{test_robot_id}":
    raise SystemExit("VerifyConnection returned an unexpected robot identity.")
if proof_body.get("targetMode") != target_mode:
    raise SystemExit("VerifyConnection returned an unexpected Open Jibo target mode.")
if proof_body.get("targetHost") != target_host:
    raise SystemExit("VerifyConnection returned an unexpected Open Jibo target host.")
if proof_body.get("reportedConnectionHost") != reported_connection_host:
    raise SystemExit("VerifyConnection did not echo the normalized reported connection host.")
if not proof_body.get("reportedConnectionHostMatches"):
    raise SystemExit("VerifyConnection did not confirm the reported connection host matched the target host.")
if not proof_body.get("reportedHostMappingsMatch"):
    raise SystemExit("VerifyConnection did not confirm the robot-reported legacy host mappings matched the target host.")

reported_host_mappings_body = (
    proof_body.get("reportedHostMappings") if isinstance(proof_body.get("reportedHostMappings"), dict) else {}
)
for legacy_host, expected_host in reported_host_mappings.items():
    if reported_host_mappings_body.get(legacy_host) != expected_host:
        raise SystemExit(f"VerifyConnection did not echo the robot-reported {legacy_host} mapping to {expected_host}.")

host_mappings = proof_body.get("hostMappings") if isinstance(proof_body.get("hostMappings"), dict) else {}
for legacy_host in ("api.jibo.com", "api-socket.jibo.com", "neo-hub.jibo.com"):
    if host_mappings.get(legacy_host) != target_host:
        raise SystemExit(f"VerifyConnection did not map {legacy_host} to {target_host}.")

readiness = proof_body.get("conversionReadiness") if isinstance(proof_body.get("conversionReadiness"), dict) else {}
if not readiness.get("canWriteRobot"):
    raise SystemExit("VerifyConnection readiness is not write-safe after setup.")

print(json.dumps([result.__dict__ for result in results], indent=2))
PY
