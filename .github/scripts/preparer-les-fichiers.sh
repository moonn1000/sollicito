#!/usr/bin/env bash
# Prépare les fichiers à joindre à une version (ADR-0009) : Sollicito-<version>-win-x64.exe et son empreinte, au
# format de sha256sum, depuis le dossier de l'artefact du job « Exe » (Sollicito.exe et Sollicito.exe.sha256).
# La publication l'appelle avant l'envoi ; la CI de chaque pull request l'éprouve sur l'exe construit.
#
# Usage : preparer-les-fichiers.sh <version X.Y.Z> <dossier de l'artefact> <empreinte attendue> <dossier de sortie>
set -euo pipefail

version="${1-}"
source="${2-}"
attendue="${3-}"
sortie="${4-}"

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Refus : la version n'a pas la forme X.Y.Z." >&2
  exit 1
fi
if ! [[ "$attendue" =~ ^[0-9a-f]{64}$ ]]; then
  echo "Refus : l'empreinte attendue n'est pas un SHA-256." >&2
  exit 1
fi

(cd "$source" && sha256sum --check --strict Sollicito.exe.sha256)
nom="Sollicito-${version}-win-x64.exe"
mkdir -p "$sortie"
cp "$source/Sollicito.exe" "$sortie/$nom"
empreinte="$(sha256sum "$sortie/$nom" | cut -d ' ' -f 1)"
if [[ "$empreinte" != "$attendue" ]]; then
  echo "Refus : l'exe n'a pas l'empreinte que le job « Exe » a calculée." >&2
  exit 1
fi
printf '%s  %s\n' "$empreinte" "$nom" > "$sortie/$nom.sha256"
(cd "$sortie" && sha256sum --check --strict "$nom.sha256")
echo "Prêts : $nom et $nom.sha256, empreinte $empreinte."
