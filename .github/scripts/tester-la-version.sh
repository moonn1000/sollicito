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
# shellcheck disable=SC2016 # ${version} est le texte littéral que cherche jq
exiger "titre de la pull request qui porte la version" jq -e '."pull-request-title-pattern" | contains("${version}")' "$config"

# Une seule ligne annotée, qui porte <Version>X.Y.Z</Version>, et la même version que le manifeste.
annotees="$(grep -c 'x-release-please-version' "$props" || true)"
exiger "une seule ligne annotée dans Directory.Build.props" test "$annotees" = 1
version="$(grep 'x-release-please-version' "$props" | sed -nE 's#.*<Version>([0-9]+\.[0-9]+\.[0-9]+)</Version>.*#\1#p')"
attendue="$(jq -r '.["."] // "0.0.0"' "$manifeste")"
exiger "version de Directory.Build.props ($version) égale à celle du manifeste ($attendue)" test "$version" = "$attendue"

# Le workflow de version : workflow_dispatch seul, par liste d'autorisation.
controle="$(dirname "$0")/controler-les-declencheurs.sh"
exiger "workflow de version : workflow_dispatch seul" bash "$controle" "$workflow"
exiger "workflow de publication : workflow_dispatch seul" bash "$controle" .github/workflows/publication.yml

# Le contrôle lui-même, éprouvé sur des copies du workflow : chaque déclencheur ajouté doit être refusé.
essais="$(mktemp -d)"
trap 'rm -rf "$essais"' EXIT
refuse() {
  local description="$1" copie="$essais/version.yml"
  shift
  "$@" < "$workflow" > "$copie"
  exiger "contrôle des déclencheurs : $description refusé" bash -c "! bash '$controle' '$copie'"
}
ajouter() { awk -v ligne="$1" '{ print } /^on:[[:space:]]*$/ { print ligne }'; }
for declencheur in 'push:' '"push":' "'push':" 'push :' 'pull_request:' 'pull_request_target:' 'issue_comment:' \
  'issues:' 'workflow_call:' 'workflow_run:' 'merge_group:' 'watch:' 'check_run:' 'schedule:' 'release:' \
  'repository_dispatch:' 'create:'; do
  refuse "$declencheur ajouté" ajouter "  $declencheur"
done
refuse "on: en ligne (liste)" sed -E 's/^on:[[:space:]]*$/on: [push, workflow_dispatch]/'
refuse "on: en ligne (un seul nom)" sed -E 's/^on:[[:space:]]*$/on: workflow_dispatch/'
refuse "second bloc on:" awk '{ print } END { print "on:"; print "  push:" }'
refuse "second document" awk '{ print } END { print "---"; print "on:"; print "  push:" }'

# Formes qui trompaient une lecture ligne à ligne : le bloc « on: » remplacé par un autre texte (printf %b).
remplacer() {
  awk -v bloc="$1" '
    /^on:[[:space:]]*$/ { while ((getline ligne < bloc) > 0) print ligne; saute = 1; next }
    saute && /^[^[:space:]]/ { saute = 0 }
    !saute { print }'
}
bloc() {
  local description="$1" texte="$2"
  printf '%b' "$texte" > "$essais/bloc"
  refuse "$description" remplacer "$essais/bloc"
}
bloc "CR seul avant push" 'on:\n  workflow_dispatch:\n# c\r  push:\n'
bloc "NEL avant push" 'on:\n  workflow_dispatch: # c\xc2\x85  push:\n'
bloc "LS avant push" 'on:\n  workflow_dispatch: # c\xe2\x80\xa8  push:\n'
bloc "PS avant push" 'on:\n  workflow_dispatch: # c\xe2\x80\xa9  push:\n'
bloc "leurre en guillemets doubles, clé explicite" 'leurre: "\non:\n  workflow_dispatch:\n  "\n? on\n:\n  push:\n  workflow_dispatch:\n'
bloc "leurre en guillemets doubles, !!str on" 'leurre: "\non:\n  workflow_dispatch:\n  "\n!!str on:\n  push:\n  workflow_dispatch:\n'
bloc "leurre en guillemets doubles, échappement" 'leurre: "\non:\n  workflow_dispatch:\n  "\n"\\x6Fn":\n  push:\n  workflow_dispatch:\n'
bloc "leurre en guillemets simples, clé explicite" "leurre: '\\non:\\n  workflow_dispatch:\\n  '\\n? on\\n:\\n  push:\\n  workflow_dispatch:\\n"
bloc "« on » entre guillemets, push ajouté" '"on":\n  push:\n  workflow_dispatch:\n'
bloc "clé true:" 'true:\n  push:\n  workflow_dispatch:\n'
bloc "clé On:" 'On:\n  push:\n  workflow_dispatch:\n'
bloc "tabulation" 'on:\n\tpush:\n  workflow_dispatch:\n'
bloc "clé de fusion" 'on:\n  <<: {push: {}}\n  workflow_dispatch:\n'

# Et une forme juste, admise : le même workflow en CRLF, comme dans une copie Windows.
tr -d '\r' < "$workflow" | sed 's/$/\r/' > "$essais/crlf.yml"
exiger "contrôle des déclencheurs : copie en CRLF admise" bash "$controle" "$essais/crlf.yml"

if (( ecarts > 0 )); then
  echo "$ecarts écart(s)."
  exit 1
fi
echo "Configuration de la version conforme."
