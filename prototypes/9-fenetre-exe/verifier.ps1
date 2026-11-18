<#
.SYNOPSIS
  Vérifie le prototype de #9 : publie l'exe autonome d'un seul fichier, le copie seul dans un dossier
  temporaire vide, le lance en autotest et lit son rapport.

.PARAMETER Lancements
  Nombre de lancements successifs. Le premier extrait les bibliothèques natives, les suivants les réutilisent.

.PARAMETER Compresse
  Publie avec la compression du fichier unique (EnableCompressionInSingleFile).

.PARAMETER Preuves
  Copie dans preuves/ le rapport et la capture du premier lancement, et un résumé de la publication.
#>
[CmdletBinding()]
param(
    [ValidateRange(1, 5)][int]$Lancements = 2,
    [switch]$Compresse,
    [switch]$Preuves
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Masquer([string]$texte) {
    # Le compte Windows n'apparaît pas dans ce qui peut être publié.
    $texte.Replace([IO.Path]::GetTempPath().TrimEnd('\'), '%TEMP%').Replace($env:USERPROFILE, '%USERPROFILE%')
}

$ici = $PSScriptRoot
Push-Location $ici
try {
    $sdk = (& dotnet --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'dotnet introuvable' }
    $variante = if ($Compresse) { 'compresse' } else { 'normal' }
    $sortie = Join-Path $ici ('publish\{0}-{1}' -f $variante, (Get-Date -Format 'yyyyMMdd-HHmmss'))

    & dotnet restore --locked-mode
    if ($LASTEXITCODE -ne 0) { throw "restauration en mode verrouillé : code $LASTEXITCODE" }
    $arguments = @('publish', '-c', 'Release', '--no-restore', '-o', $sortie)
    if ($Compresse) { $arguments += '-p:EnableCompressionInSingleFile=true' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "publication : code $LASTEXITCODE" }

    $fichiers = @(Get-ChildItem -LiteralPath $sortie -Recurse -File -Force)
    Write-Host ''
    Write-Host ("SDK {0}, variante {1}, fichiers publiés : {2}" -f $sdk, $variante, $fichiers.Count)
    foreach ($f in $fichiers) { Write-Host ('  {0} : {1:N1} Mo ({2} octets)' -f $f.Name, ($f.Length / 1MB), $f.Length) }
    if ($fichiers.Count -ne 1) { throw 'la publication doit produire un seul fichier' }
    $exe = $fichiers[0]

    # L'exe tourne seul, loin du dépôt et de ses fichiers de compilation.
    $essai = Join-Path ([IO.Path]::GetTempPath()) ('sollicito-proto9-essai-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Path $essai | Out-Null
    Copy-Item -LiteralPath $exe.FullName -Destination $essai
    $exeEssai = Join-Path $essai $exe.Name
    Write-Host ('Essai dans {0} : {1} fichier(s)' -f (Masquer $essai), @(Get-ChildItem -LiteralPath $essai -Force).Count)

    $resultats = @()
    for ($i = 1; $i -le $Lancements; $i++) {
        $rapport = Join-Path $essai "rapport-$i.json"
        $capture = Join-Path $essai "capture-$i.png"
        $info = New-Object System.Diagnostics.ProcessStartInfo
        $info.FileName = $exeEssai
        $info.Arguments = "--autotest --rapport `"$rapport`" --capture `"$capture`""
        $info.WorkingDirectory = $essai
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
        Write-Host ''
        Write-Host ('Lancement {0} : code {1}, {2} ms au total, dont {3} ms avant le démarrage du processus, page prête {4} ms après ; verdict {5}' -f `
            $i, $code, $duree, $avantDemarrage, $contenu.sections.durees.msDepuisDemarrageProcessusJusquaPagePrete, $contenu.verdict)
        foreach ($c in $contenu.controles) {
            $etat = if ($c.ok) { 'OK    ' } else { 'ÉCHEC ' }
            Write-Host "  $etat$($c.nom)"
        }
        foreach ($m in $contenu.manquants) { Write-Host "  MANQUE $m" }
        foreach ($e in $contenu.erreurs) { Write-Host "  ERREUR $e" }
        $resultats += [pscustomobject]@{
            lancement = $i
            codeSortie = $code
            dureeTotaleMs = $duree
            msAvantDemarrageProcessus = $avantDemarrage
            msDuDemarrageJusquaPagePrete = $contenu.sections.durees.msDepuisDemarrageProcessusJusquaPagePrete
            verdict = $contenu.verdict
        }
    }

    if ($Preuves) {
        $dossierPreuves = Join-Path $ici 'preuves'
        New-Item -ItemType Directory -Force -Path $dossierPreuves | Out-Null
        Copy-Item -LiteralPath (Join-Path $essai 'rapport-1.json') -Destination (Join-Path $dossierPreuves 'rapport-autotest.json') -Force
        Copy-Item -LiteralPath (Join-Path $essai 'capture-1.png') -Destination (Join-Path $dossierPreuves 'fenetre.png') -Force
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

    $echecs = @($resultats | Where-Object { $_.codeSortie -ne 0 })
    Write-Host ''
    if ($echecs.Count -gt 0) {
        Write-Host 'Verdict : échoué'
        exit 1
    }
    Write-Host 'Verdict : réussi'
}
finally {
    Pop-Location
}
