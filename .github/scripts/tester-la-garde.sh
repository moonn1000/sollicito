#!/usr/bin/env bash
# Éprouve la garde de la publication sur des refs admises et refusées, sans rien publier (CI de chaque pull request,
# ADR-0009). Il faut l'historique de main (actions/checkout, fetch-depth: 0).
set -euo pipefail

garde="$(dirname "$0")/garder-la-publication.sh"
sur_main="$(git rev-parse origin/main)"
# Un commit absent de main, créé ici et jamais poussé.
hors_main="$(GIT_AUTHOR_NAME=essai GIT_AUTHOR_EMAIL=essai@invalid GIT_COMMITTER_NAME=essai GIT_COMMITTER_EMAIL=essai@invalid \
  git commit-tree "$sur_main^{tree}" -p "$sur_main" -m "essai de la garde")"
saut=$'\n'

ecarts=0
essayer() {
  local attendu="$1" ref="$2" commit="$3" description="$4" obtenu
  if bash "$garde" "$ref" "$commit" > /dev/null 2>&1; then obtenu=admis; else obtenu=refus; fi
  if [[ "$obtenu" == "$attendu" ]]; then
    echo "ok    : $description ($obtenu)"
  else
    echo "ÉCART : $description ($obtenu au lieu de $attendu)"
    ecarts=$((ecarts + 1))
  fi
}

essayer admis 'refs/tags/v1.2.3' "$sur_main" "étiquette de version sur main"
essayer admis 'refs/tags/v10.20.30' "$sur_main" "étiquette à plusieurs chiffres"
essayer refus 'refs/heads/v1.2.3' "$sur_main" "branche nommée comme une version"
essayer refus 'v1.2.3' "$sur_main" "nom court, sans refs/tags/"
essayer refus 'refs/tags/v1.2' "$sur_main" "version à deux nombres"
essayer refus 'refs/tags/1.2.3' "$sur_main" "version sans v"
essayer refus 'refs/tags/v1.2.3-rc.1' "$sur_main" "pré-version"
essayer refus 'refs/tags/v1.2.3;id' "$sur_main" "point-virgule"
# shellcheck disable=SC2016 # la substitution reste littérale : c'est elle qu'on éprouve
essayer refus 'refs/tags/v1.2.3$(id)' "$sur_main" "substitution de commande"
essayer refus "refs/tags/v1.2.3${saut}" "$sur_main" "saut de ligne final"
essayer refus 'refs/tags/v1.2.3' "$hors_main" "commit absent de main"
essayer refus 'refs/tags/v1.2.3' 'abc' "commit qui n'est pas une empreinte"
essayer refus 'refs/tags/v1.2.3' '' "commit vide"

if (( ecarts > 0 )); then
  echo "$ecarts écart(s)."
  exit 1
fi
echo "Garde conforme."
