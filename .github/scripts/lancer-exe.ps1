<#
.SYNOPSIS
  Lancement de fumée de l'exe publié de Sollicito, sur un runner neuf de la CI (ADR-0009).

.DESCRIPTION
  Copie l'exe seul dans un dossier vide et le lance. Il doit, avant le délai d'attente, créer sa base et le profil
  de son navigateur intégré, et lancer ce navigateur (msedgewebview2.exe, processus enfant). Puis il doit vivre
  encore au bout du délai de vie. À la fin, réussite ou non, l'arbre de ses processus est arrêté.

  La version publiée range ses données dans %LOCALAPPDATA%\Sollicito, sans autre choix possible : le script refuse
  de tourner si ce dossier existe déjà, pour ne jamais toucher aux données d'un poste.

  Ce lancement ne prouve pas que la page s'affiche : la version publiée n'ouvre pas le protocole CDP. Une boîte
  d'erreur de Sollicito, elle, bloque la fenêtre avant le navigateur intégré, et le lancement échoue au délai
  d'attente.

.PARAMETER Exe
  Le chemin de Sollicito.exe publié.

.PARAMETER Vie
  Secondes pendant lesquelles l'exe doit rester lancé, depuis son démarrage.

.PARAMETER Attente
  Secondes accordées à l'exe pour créer sa base et son profil et lancer son navigateur intégré.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [ValidateRange(1, 300)][int]$Vie = 15,
    [ValidateRange(5, 600)][int]$Attente = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$donnees = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Sollicito'
if (Test-Path -LiteralPath $donnees) {
    throw "Le dossier de données de Sollicito existe déjà : ce lancement ne touche jamais aux données d'un poste. Lancez-le sur un runner neuf."
}
$base = Join-Path $donnees 'sollicito.db'
$profil = Join-Path (Join-Path $donnees 'WebView2') 'EBWebView'

$dossier = Join-Path ([IO.Path]::GetTempPath()) ('sollicito-lancement-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $dossier | Out-Null
$copie = Join-Path $dossier 'Sollicito.exe'
Copy-Item -LiteralPath $Exe -Destination $copie

$chrono = [Diagnostics.Stopwatch]::StartNew()
$processus = Start-Process -FilePath $copie -WorkingDirectory $dossier -PassThru
# Garder la poignée du processus : sans elle, son code de sortie resterait vide.
$null = $processus.Handle
Write-Host "Sollicito.exe lancé seul dans un dossier vide (processus $($processus.Id))."

function Etat() {
    $navigateur = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $($processus.Id)" |
            Where-Object { $_.Name -eq 'msedgewebview2.exe' }).Count
    [pscustomobject]@{
        Base       = Test-Path -LiteralPath $base
        Profil     = Test-Path -LiteralPath $profil
        Navigateur = $navigateur
    }
}

try {
    $pret = $false
    $etat = Etat
    while ($chrono.Elapsed.TotalSeconds -lt $Attente) {
        if ($processus.HasExited) {
            throw ("Sollicito.exe s'est arrêté au bout de {0:N1} s, avec le code {1}." -f $chrono.Elapsed.TotalSeconds, $processus.ExitCode)
        }
        $etat = Etat
        if ($etat.Base -and $etat.Profil -and $etat.Navigateur -gt 0) {
            $pret = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $pret) {
        throw ("Au bout de {0} s : base créée {1}, profil créé {2}, navigateur intégré lancé {3}." -f $Attente, $etat.Base, $etat.Profil, ($etat.Navigateur -gt 0))
    }
    Write-Host ("Prêt au bout de {0:N1} s : base et profil du navigateur intégré créés, navigateur intégré lancé." -f $chrono.Elapsed.TotalSeconds)

    $reste = $Vie - $chrono.Elapsed.TotalSeconds
    if ($reste -gt 0) {
        Start-Sleep -Milliseconds ([int][math]::Ceiling($reste * 1000))
    }
    if ($processus.HasExited) {
        throw ("Sollicito.exe s'est arrêté avant {0} s, avec le code {1}." -f $Vie, $processus.ExitCode)
    }
    Write-Host ("Toujours lancé au bout de {0:N1} s." -f $chrono.Elapsed.TotalSeconds)
}
finally {
    if (-not $processus.HasExited) {
        # L'arbre entier : Sollicito.exe et les processus du navigateur intégré.
        & taskkill.exe /PID $processus.Id /T /F | Out-Null
    }
    if (-not $processus.WaitForExit(30000)) {
        Write-Host "Sollicito.exe ne s'est pas arrêté dans les 30 s."
    }
}
Write-Host 'Lancement de fumée réussi ; arbre des processus arrêté.'
# Explicite : sinon le code de sortie serait celui de la dernière commande externe lancée (taskkill).
exit 0
