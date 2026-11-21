#!/usr/bin/env bash
# Contrôle par liste d'autorisation (ADR-0009) : le bloc « on » du workflow donné n'a qu'un déclencheur,
# workflow_dispatch. Le fichier est lu par un vrai analyseur YAML, yq (YAML 1.2, comme actionlint), et non ligne à
# ligne : une clé explicite (« ? on »), une étiquette (« !!str on »), un échappement (« "\x6Fn" ») ou un leurre logé
# dans une chaîne multiligne ne le trompent pas. Les sauts de ligne qu'un analyseur YAML 1.1 lirait comme des fins
# de ligne, et pas yq (CR seul, NEL, LS, PS), sont refusés d'office. Toute forme inattendue échoue, au lieu de passer.
#
# Usage : controler-les-declencheurs.sh <workflow.yml>
# yq : la variable YQ (la CI y met yq 4.54.3, empreinte vérifiée), sinon le yq du PATH. jq et perl viennent du système.
set -euo pipefail

fichier="${1-}"
yq="${YQ:-yq}"
refuser() {
  echo "Refus : $1" >&2
  exit 1
}

[[ -f "$fichier" ]] || refuser "workflow introuvable."

# CR qui n'est pas suivi de LF, NEL (U+0085), LS (U+2028), PS (U+2029).
if LC_ALL=C perl -ne '$vu = 1 if /\r(?!\n)/ || /\xC2\x85|\xE2\x80[\xA8\xA9]/; END { exit($vu ? 0 : 1) }' "$fichier"; then
  refuser "le workflow contient un saut de ligne inattendu (CR seul, NEL, LS ou PS)."
fi

lire() { "$yq" -o=json -I=0 "$1" "$fichier" 2> /dev/null | tr -d '\r'; }

cles="$(lire 'keys')" || refuser "le workflow ne se lit pas en YAML."
[[ -n "$cles" ]] || refuser "le workflow ne se lit pas en YAML."
# Un seul document, une seule clé « on », et aucune clé qu'un autre analyseur pourrait lire comme « on » :
# un booléen, ou un mot que YAML 1.1 lit comme un booléen.
[[ "$(jq -s 'length' <<< "$cles")" == 1 ]] || refuser "le workflow doit tenir en un seul document."
[[ "$(jq '[.[] | select(. == "on")] | length' <<< "$cles")" == 1 ]] || refuser "le workflow doit avoir une seule clé « on »."
[[ "$(jq '[.[] | select(type != "string")] | length' <<< "$cles")" == 0 ]] ||
  refuser "le workflow a une clé qui n'est pas une chaîne."
douteuses='^(y|Y|yes|Yes|YES|n|N|no|No|NO|true|True|TRUE|false|False|FALSE|On|ON|off|Off|OFF)$'
[[ "$(jq --arg motif "$douteuses" '[.[] | select(test($motif))] | length' <<< "$cles")" == 0 ]] ||
  refuser "le workflow a une clé que YAML 1.1 lirait comme un booléen."
# Les seules clés de racine d'un workflow : une clé de fusion (<<), un nombre que YAML 1.1 lirait comme 1 donc
# comme « on » (0:1.), ou toute autre clé inattendue sont refusés.
permises='["name","run-name","on","permissions","env","defaults","concurrency","jobs"]'
[[ "$(jq --argjson permises "$permises" '[.[] | select(. as $cle | $permises | index($cle) | not)] | length' <<< "$cles")" == 0 ]] ||
  refuser "le workflow a une clé de racine qui n'est pas celle d'un workflow."

[[ "$(lire '.on | type')" == '"!!map"' ]] || refuser "le bloc « on » doit être un mapping."
[[ "$(lire '.on | keys')" == '["workflow_dispatch"]' ]] || refuser "le bloc « on » n'admet que workflow_dispatch."
echo "Déclencheurs conformes : workflow_dispatch seul."
