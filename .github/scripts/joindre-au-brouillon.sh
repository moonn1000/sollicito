#!/usr/bin/env bash
# Joint au brouillon d'une version l'exe préparé, son empreinte et le paquet de son attestation (ADR-0009). Avant
# l'envoi, il vérifie que le brouillon existe, puis l'empreinte et l'attestation, hors ligne avec la racine de
# confiance de Sigstore. Après, il compare le condensé que GitHub affiche à l'empreinte.
#
# Usage : joindre-au-brouillon.sh <dossier préparé> <paquet de l'attestation>
# Lit GITHUB_REF, GITHUB_SHA, GITHUB_REPOSITORY, RUNNER_TEMP et GH_TOKEN dans l'environnement.
set -euo pipefail

dossier="${1-}"
paquet="${2-}"

if ! [[ "${GITHUB_REF-}" =~ ^refs/tags/v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Refus : la publication ne part que d'une étiquette de version, refs/tags/vX.Y.Z." >&2
  exit 1
fi
export ETIQUETTE="${GITHUB_REF#refs/tags/}"
export NOM="Sollicito-${ETIQUETTE#v}-win-x64.exe"

# 1. Le brouillon de cette étiquette. Un brouillon ne se lit qu'avec l'écriture du dépôt, et pas par son étiquette.
brouillon="$(gh api --paginate "repos/$GITHUB_REPOSITORY/releases" \
  --jq '.[] | select(.tag_name == env.ETIQUETTE and .draft) | .id')"
if ! [[ "$brouillon" =~ ^[0-9]+$ ]]; then
  echo "Refus : il faut un brouillon de version, et un seul, pour $ETIQUETTE." >&2
  exit 1
fi

# 2. L'empreinte et l'attestation, vérifiées hors ligne. Il faut gh 2.102.0 ou plus : avant, --signer-workflow se
#    lisait comme un préfixe et --source-ref sans la casse (avis GHSA-wjmr-j3rp-mh2g et GHSA-4mq3-hpgx-9cx8).
read -r majeure mineure _ <<< "$(gh --version | sed -nE '1s/^gh version ([0-9]+)\.([0-9]+)\.([0-9]+).*/\1 \2 \3/p')"
if ! [[ "${majeure-}" =~ ^[0-9]+$ && "${mineure-}" =~ ^[0-9]+$ ]] || (( 10#$majeure < 2 || (10#$majeure == 2 && 10#$mineure < 102) )); then
  echo "Refus : il faut gh 2.102.0 ou plus pour vérifier l'attestation." >&2
  exit 1
fi
(cd "$dossier" && sha256sum --check --strict "$NOM.sha256")
racine="$RUNNER_TEMP/trusted_root.jsonl"
gh attestation trusted-root > "$racine"
gh attestation verify "$dossier/$NOM" \
  --bundle "$paquet" \
  --custom-trusted-root "$racine" \
  --repo "$GITHUB_REPOSITORY" \
  --signer-workflow "$GITHUB_REPOSITORY/.github/workflows/publication.yml" \
  --source-ref "$GITHUB_REF" \
  --source-digest "$GITHUB_SHA"
cp "$paquet" "$dossier/$NOM.sigstore.json"

# 3. L'envoi au brouillon. gh retrouve un brouillon par son étiquette ; --clobber laisse un rattrapage remplacer
#    les fichiers d'un envoi interrompu, identiques puisque la construction est reproductible.
gh release upload "$ETIQUETTE" --repo "$GITHUB_REPOSITORY" --clobber \
  "$dossier/$NOM" "$dossier/$NOM.sha256" "$dossier/$NOM.sigstore.json"

# 4. Le condensé que GitHub affiche pour l'exe joint.
empreinte="$(cut -d ' ' -f 1 "$dossier/$NOM.sha256")"
condense="$(gh api "repos/$GITHUB_REPOSITORY/releases/$brouillon/assets" --jq '.[] | select(.name == env.NOM) | .digest')"
if [[ "$condense" != "sha256:$empreinte" ]]; then
  echo "Refus : le condensé que GitHub affiche n'est pas l'empreinte de l'exe." >&2
  exit 1
fi
echo "Joints au brouillon $ETIQUETTE : $NOM, $NOM.sha256 et $NOM.sigstore.json, empreinte $empreinte."
