#!/usr/bin/env bash
# Garde de la publication (ADR-0009) : elle n'admet qu'une étiquette de version, refs/tags/vX.Y.Z en ref complète,
# posée sur un commit de main. publication.yml l'appelle en tête ; la CI de chaque pull request l'éprouve
# (tester-la-garde.sh). Les messages de refus ne citent pas la ref, qui peut contenir n'importe quel caractère.
#
# Usage : garder-la-publication.sh <ref complète> <commit> [<branche de référence, origin/main par défaut>]
set -euo pipefail

ref="${1-}"
commit="${2-}"
reference="${3:-origin/main}"

if ! [[ "$ref" =~ ^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Refus : la publication ne part que d'une étiquette de version, refs/tags/vX.Y.Z." >&2
  exit 1
fi
if ! [[ "$commit" =~ ^[0-9a-f]{40}$ ]]; then
  echo "Refus : le commit n'est pas une empreinte complète." >&2
  exit 1
fi
if ! git merge-base --is-ancestor "$commit" "$reference" 2>/dev/null; then
  echo "Refus : le commit de l'étiquette n'est pas sur main." >&2
  exit 1
fi
echo "Garde passée : ${ref#refs/tags/}, sur main."
