#!/usr/bin/env bash
# Contrôle, sans rien publier, la configuration de release-please et le workflow de version (ADR-0009) :
# versions en brouillon avec leur étiquette, version de l'exe annotée et citée dans extra-files, et un workflow de
# version qui ne se lance qu'à la main. CI de chaque pull request.
set -euo pipefail

config=release-please-config.json
manifeste=.release-please-manifest.json
props=Directory.Build.props
workflow=.github/workflows/version.yml

ecarts=0
exiger() {
  local description="$1"
  shift
  if "$@" > /dev/null 2>&1; then
    echo "ok    : $description"
  else
    echo "ÉCART : $description"
    ecarts=$((ecarts + 1))
  fi
}

exiger "type simple, première version 1.0.0" jq -e '."release-type" == "simple" and ."initial-version" == "1.0.0"' "$config"
exiger "version en brouillon, étiquette créée aussitôt" jq -e '.draft == true and ."force-tag-creation" == true' "$config"
exiger "étiquette vX.Y.Z, sans composant" jq -e '."include-v-in-tag" == true and ."include-component-in-tag" == false' "$config"
exiger "Directory.Build.props dans extra-files" jq -e '.packages["."]."extra-files" | index("Directory.Build.props") != null' "$config"
exiger "textes réglables présents" jq -e '[."pull-request-title-pattern", ."pull-request-header", ."pull-request-footer", .label, ."release-label"] | all(type == "string" and length > 0)' "$config"
exiger "titre de la pull request qui porte la version" jq -e '."pull-request-title-pattern" | contains("${version}")' "$config"

# Une seule ligne annotée, qui porte <Version>X.Y.Z</Version>, et la même version que le manifeste.
annotees="$(grep -c 'x-release-please-version' "$props" || true)"
exiger "une seule ligne annotée dans Directory.Build.props" test "$annotees" = 1
version="$(grep 'x-release-please-version' "$props" | sed -nE 's#.*<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>.*#\1#p')"
attendue="$(jq -r '.["."] // "0.0.0"' "$manifeste")"
exiger "version de Directory.Build.props ($version) égale à celle du manifeste ($attendue)" test "$version" = "$attendue"

# Le workflow de version : workflow_dispatch seul, jamais à chaque push.
exiger "workflow de version lancé à la main seulement" grep -qE '^  workflow_dispatch:' "$workflow"
exiger "aucun autre déclencheur du workflow de version" bash -c "! grep -qE '^  (push|pull_request|pull_request_target|schedule|workflow_run|release|create|repository_dispatch):' '$workflow'"

if (( ecarts > 0 )); then
  echo "$ecarts écart(s)."
  exit 1
fi
echo "Configuration de la version conforme."
