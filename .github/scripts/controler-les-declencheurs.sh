#!/usr/bin/env bash
# Contrôle par liste d'autorisation (ADR-0009) : le bloc « on: » du workflow donné ne doit avoir qu'un déclencheur,
# workflow_dispatch. Tout autre déclencheur, toute forme en ligne (on: [push], on: workflow_dispatch) et toute clé
# « on » entre guillemets sont refusés. Une forme inattendue échoue, au lieu de passer.
#
# Usage : controler-les-declencheurs.sh <workflow.yml>
set -euo pipefail

fichier="${1-}"
if [[ ! -f "$fichier" ]]; then
  echo "Refus : workflow introuvable." >&2
  exit 1
fi

# Retire les CR d'une copie Windows avant la lecture.
tr -d '\r' < "$fichier" | awk '
  BEGIN { blocs = 0; dans = 0; niveau = -1; autorises = 0; autres = 0; douteux = 0 }
  /^[[:space:]]*(#.*)?$/ { next }
  {
    match($0, /^ */)
    retrait = RLENGTH
    if (retrait == 0) {
      dans = 0
      if ($0 ~ /^on:[[:space:]]*(#.*)?$/) { blocs++; dans = 1; next }
      # « on » en ligne, ou entre guillemets : une forme que ce contrôle refuse plutôt que de la deviner.
      if ($0 ~ /^["\047]?on["\047]?[[:space:]]*:/) { douteux++ }
      next
    }
    if (dans) {
      if (niveau < 0) { niveau = retrait }
      if (retrait < niveau) { douteux++ }
      else if (retrait == niveau) {
        if ($0 ~ /^ *workflow_dispatch:[[:space:]]*(#.*)?$/) { autorises++ } else { autres++ }
      }
    }
  }
  END { exit !(blocs == 1 && autorises == 1 && autres == 0 && douteux == 0) }
'
