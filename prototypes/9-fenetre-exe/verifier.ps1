<#
.SYNOPSIS
  Vérifie le prototype de #9 : publie l'exe autonome d'un seul fichier, le copie seul dans un dossier
  vide, le lance en autotest et lit son rapport.

.DESCRIPTION
  Tout se passe dans un dossier d'essai neuf sous %TEMP%, supprimé à la fin (sauf -Garder).
  .NET extrait les bibliothèques natives de l'exe sous %TEMP%\.net\Prototype9, dans un dossier par
  version du code publié : le premier lancement d'une version les extrait, les suivants les réutilisent.
  Pour chaque lancement, le script dit si l'extraction était neuve et si son dossier reste après la fermeture.

.PARAMETER Lancements
  Nombre de lancements successifs du même exe.

.PARAMETER Compresse
  Publie avec la compression du fichier unique (EnableCompressionInSingleFile).

.PARAMETER Preuves
  Copie dans preuves/ le rapport et la capture du premier lancement, et un résumé de la publication,
  sous des noms propres à la variante (normal ou compresse).

.PARAMETER Garder
  Garde le dossier d'essai (exe, rapports, captures) au lieu de le supprimer.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 5)][int]$Lancements = 2,
    [switch]$Compresse,
    [switch]$Preuves,
    [switch]$Garder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Masquer([string]$texte) {
    # Le compte Windows n'apparaît pas dans ce qui peut être publié.
    $texte.Replace([IO.Path]::GetTempPath().TrimEnd('\'), '%TEMP%').Replace($env:USERPROFILE, '%USERPROFILE%')
}

function Lire($objet, [string[]]$chemin) {
    # Lecture tolérante sous StrictMode : $null si une propriété manque, par exemple dans le rapport d'un lancement interrompu.
    foreach ($nom in $chemin) {
        if ($null -eq $objet -or -not $objet.PSObject.Properties[$nom]) { return $null }
        $objet = $objet.$nom
    }
    return $objet
}

$ici = $PSScriptRoot
$temp = [IO.Path]::GetTempPath().TrimEnd('\')
$baseExtraction = Join-Path $temp '.net\Prototype9'
$essai = Join-Path $temp ('sollicito-proto9-essai-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$publication = Join-Path $essai 'publication'
$lancement = Join-Path $essai 'lancement'
$echec = $false

Push-Location $ici
try {
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'dotnet introuvable' }
    $variante = if ($Compresse) { 'compresse' } else { 'normal' }

    & dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "restauration en mode verrouillé : code $LASTEXITCODE" }
    $arguments = @('publish', '-c', 'Release', '--no-restore', '-o', $publication)
    if ($Compresse) { $arguments += '-p:EnableCompressionInSingleFile=true' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "publication : code $LASTEXITCODE" }

    $fichiers = @(Get-ChildItem -LiteralPath $publication -Recurse -File -Force)
    Write-Host ''
    Write-Host ('SDK {0}, variante {1}, fichiers publiés : {2}' -f $sdk, $variante, $fichiers.Count)
    foreach ($f in $fichiers) { Write-Host ('  {0} : {1:N1} Mo ({2} octets)' -f $f.Name, ($f.Length / 1MB), $f.Length) }
    if ($fichiers.Count -ne 1) { throw 'la publication doit produire un seul fichier' }
    $exe = $fichiers[0]

    # L'exe tourne seul dans un dossier vide, loin du dépôt et de ses fichiers de compilation.
    New-Item -ItemType Directory -Path $lancement | Out-Null
    Copy-Item -LiteralPath $exe.FullName -Destination $lancement
    $exeEssai = Join-Path $lancement $exe.Name
    Write-Host ('Lancement depuis {0} : {1} fichier(s) dans le dossier' -f (Masquer $lancement), @(Get-ChildItem -LiteralPath $lancement -Force).Count)

    $resultats = @()
    for ($i = 1; $i -le $Lancements; $i++) {
        $rapport = Join-Path $essai "rapport-$i.json"
        $capture = Join-Path $essai "capture-$i.png"
        $extractionsAvant = @(if (Test-Path -LiteralPath $baseExtraction) { Get-ChildItem -LiteralPath $baseExtraction -Directory | ForEach-Object { $_.Name } })

        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $exeEssai
        $info.Arguments = "--autotest --rapport `"$rapport`" --capture `"$capture`""
        $info.WorkingDirectory = $lancement
        $info.UseShellExecute = $false
        $debut = Get-Date
        $chrono = [System.Diagnostics.Stopwatch]::StartNew()
        $processus = [System.Diagnostics.Process]::Start($info)
        # Délai entre la demande de lancement et la création du processus (analyse antivirus d'un exe neuf, notamment).
        $avantDemarrage = [long]($processus.StartTime - $debut).TotalMilliseconds
        if (-not $processus.WaitForExit(120000)) {
            $processus.Kill()
            throw "lancement $i : délai de 120 s dépassé"
        }
        $duree = $chrono.ElapsedMilliseconds
        $code = $processus.ExitCode
        if (-not (Test-Path -LiteralPath $rapport)) { throw "lancement $i : code $code, aucun rapport" }
        $contenu = Get-Content -LiteralPath $rapport -Raw -Encoding UTF8 | ConvertFrom-Json

        $pagePrete = Lire $contenu @('sections', 'durees', 'msDepuisDemarrageProcessusJusquaPagePrete')
        $dossierExtraction = Lire $contenu @('sections', 'execution', 'extraction', 'dossier')
        $extraction = $null
        if ($dossierExtraction) {
            $reel = $dossierExtraction.Replace('%TEMP%', $temp)
            $extraction = [pscustomobject]@{
                dossier = $dossierExtraction
                neuve = -not ($extractionsAvant -contains (Split-Path -Leaf $reel))
                resteApresFermeture = (Test-Path -LiteralPath $reel)
                octets = Lire $contenu @('sections', 'execution', 'extraction', 'octets')
            }
        }

        Write-Host ''
        Write-Host ('Lancement {0} : code {1}, {2} ms au total, dont {3} ms avant le démarrage du processus ; page prête {4} ms après le démarrage ; verdict {5}' -f `
            $i, $code, $duree, $avantDemarrage, $pagePrete, (Lire $contenu @('verdict')))
        if ($extraction) {
            Write-Host ('  Extraction {0} : {1}, {2} octets, dossier présent après la fermeture : {3}' -f `
                $(if ($extraction.neuve) { 'neuve' } else { 'réutilisée' }), $extraction.dossier, $extraction.octets, $extraction.resteApresFermeture)
        }
        foreach ($c in @(Lire $contenu @('controles'))) {
            if ($null -eq $c) { continue }
            $etat = if ($c.ok) { 'OK    ' } else { 'ÉCHEC ' }
            Write-Host "  $etat$($c.nom)"
        }
        foreach ($m in @(Lire $contenu @('manquants'))) { if ($m) { Write-Host "  MANQUE $m" } }
        foreach ($e in @(Lire $contenu @('erreurs'))) { if ($e) { Write-Host "  ERREUR $e" } }

        if ($code -ne 0) { $echec = $true }
        $resultats += [pscustomobject]@{
            lancement = $i
            codeSortie = $code
            verdict = Lire $contenu @('verdict')
            dureeTotaleMs = $duree
            msAvantDemarrageProcessus = $avantDemarrage
            msDuDemarrageJusquaPagePrete = $pagePrete
            extractionNeuve = if ($extraction) { $extraction.neuve } else { $null }
            dossierExtractionResteApresFermeture = if ($extraction) { $extraction.resteApresFermeture } else { $null }
            octetsExtraits = if ($extraction) { $extraction.octets } else { $null }
        }
    }

    if ($Preuves) {
        $dossierPreuves = Join-Path $ici 'preuves'
        New-Item -ItemType Directory -Force -Path $dossierPreuves | Out-Null
        Copy-Item -LiteralPath (Join-Path $essai 'rapport-1.json') -Destination (Join-Path $dossierPreuves "rapport-$variante.json") -Force
        Copy-Item -LiteralPath (Join-Path $essai 'capture-1.png') -Destination (Join-Path $dossierPreuves "fenetre-$variante.png") -Force
        $resume = [pscustomobject]@{
            sdk = $sdk
            variante = $variante
            fichiersPublies = $fichiers.Count
            exe = $exe.Name
            octets = $exe.Length
            lancements = $resultats
        } | ConvertTo-Json -Depth 4
        [IO.File]::WriteAllText((Join-Path $dossierPreuves "publication-$variante.json"), $resume, (New-Object System.Text.UTF8Encoding $false))
        Write-Host ''
        Write-Host 'Preuves copiées dans preuves/.'
    }
}
finally {
    Pop-Location
    if ($Garder) {
        Write-Host ('Dossier d''essai gardé : {0}' -f (Masquer $essai))
    }
    elseif (Test-Path -LiteralPath $essai) {
        try { Remove-Item -LiteralPath $essai -Recurse -Force }
        catch { Write-Warning ('Dossier d''essai non supprimé : {0}' -f (Masquer $essai)) }
    }
}

Write-Host ''
if ($echec) {
    Write-Host 'Verdict : échoué'
    exit 1
}
Write-Host 'Verdict : réussi'
