"use strict";

function selectNextCredentialsRegion(targetMode, currentRegion) {
  if (targetMode === "open-jibo" || targetMode === "open-jibo-ai") {
    return "open-jibo";
  }

  return currentRegion || "api";
}

function rewriteCredentialsRegion(credentials, targetMode) {
  const nextRegion = selectNextCredentialsRegion(targetMode, credentials && credentials.region);
  return Object.assign({}, credentials, { region: nextRegion });
}

module.exports = {
  rewriteCredentialsRegion,
  selectNextCredentialsRegion,
};
