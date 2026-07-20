#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
fixture_dir="$script_dir/fixtures/openjibo-conversion-region"

node - "$script_dir" "$fixture_dir/credentials-api.json" "$fixture_dir/credentials-open-jibo.json" <<'NODE'
const fs = require("fs");
const path = require("path");

const scriptDir = path.resolve(process.argv[2]);
const inputPath = path.resolve(process.argv[3]);
const expectedPath = path.resolve(process.argv[4]);
const helpers = require(path.join(scriptDir, "openjibo-conversion-region.js"));

const input = JSON.parse(fs.readFileSync(inputPath, "utf8"));
const expected = JSON.parse(fs.readFileSync(expectedPath, "utf8"));
const result = helpers.rewriteCredentialsRegion(input, "open-jibo");

if (JSON.stringify(result) !== JSON.stringify(expected)) {
  throw new Error(`Unexpected credentials rewrite.\nExpected: ${JSON.stringify(expected)}\nActual:   ${JSON.stringify(result)}`);
}

if (input.region !== "api") {
  throw new Error(`Fixture input was mutated: ${JSON.stringify(input)}`);
}

if (helpers.selectNextCredentialsRegion("open-jibo-ai", "api") !== "open-jibo") {
  throw new Error("open-jibo-ai should select open-jibo as the credentials region.");
}

console.log(JSON.stringify({
  Input: input,
  Result: result,
  Expected: expected,
}, null, 2));
NODE
