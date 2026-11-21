<#
.SYNOPSIS
  Lancement de fumée de l'exe publié de Sollicito, sur un runner neuf de la CI (ADR-0009).

.DESCRIPTION
  Copie l'exe seul dans un dossier vide et le lance. Avant le délai d'attente, il doit :
    - créer sa base et le profil de son navigateur intégré ;
    - lancer ce navigateur (msedgewebview2.exe, processus enfant) ;
    - afficher sa fenêtre « Sollicito », sans aucune boîte de dialogue.
  Il doit encore être lancé au bout du délai de vie, toujours sans boîte de dialogue. À la fin, réussite ou non,
  l'arbre de ses processus est arrêté.

  Une boîte de dialogue (classe #32770) serait une erreur de Sollicito (Signalement) ou le dialogue de choix d'un
  dossier, qui ne s'ouvre qu'à la demande de l'utilisateur (#26). Dans les deux cas, le lancement échoue, au lieu de
  réussir pour une mauvaise raison.

  La version publiée range ses données dans %LOCALAPPDATA%\Sollicito, sans autre choix possible : le script refuse
  de tourner si ce dossier existe déjà, pour ne jamais toucher aux données d'un poste.

  Ce lancement ne prouve pas que la page s'affiche : la version publiée n'ouvre pas le protocole CDP.

.PARAMETER Exe
  Le chemin de Sollicito.exe publié.

.PARAMETER Vie
  Secondes pendant lesquelles l'exe doit rester lancé, depuis son démarrage.

.PARAMETER Attente
  Secondes accordées à l'exe pour être prêt.
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

# Les fenêtres visibles de premier niveau d'un processus : classe et titre.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class FenetresDUnProcessus
{
    private delegate bool Rappel(IntPtr fenetre, IntPtr parametre);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(Rappel rappel, IntPtr parametre);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr fenetre, out uint processus);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr fenetre, StringBuilder classe, int taille);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr fenetre, StringBuilder titre, int taille);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr fenetre);

    public static List<string[]> Visibles(uint processus)
    {
        var fenetres = new List<string[]>();
        EnumWindows(delegate (IntPtr fenetre, IntPtr parametre)
        {
            uint proprietaire;
            GetWindowThreadProcessId(fenetre, out proprietaire);
            if (proprietaire == processus && IsWindowVisible(fenetre))
            {
                var classe = new StringBuilder(256);
                var titre = new StringBuilder(256);
                GetClassName(fenetre, classe, classe.Capacity);
                GetWindowText(fenetre, titre, titre.Capacity);
                fenetres.Add(new[] { classe.ToString(), titre.ToString() });
            }
            return true;
        }, IntPtr.Zero);
        return fenetres;
    }
}
'@

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
    $fenetres = @([FenetresDUnProcessus]::Visibles([uint32]$processus.Id))
    [pscustomobject]@{
        Base       = Test-Path -LiteralPath $base
        Profil     = Test-Path -LiteralPath $profil
        Navigateur = $navigateur -gt 0
        Fenetre    = @($fenetres | Where-Object { $_[0] -like 'WindowsForms10.*' -and $_[1] -eq 'Sollicito' }).Count -gt 0
        Dialogues  = @($fenetres | Where-Object { $_[0] -eq '#32770' } | ForEach-Object { $_[1] })
    }
}

function Exiger-SansDialogue($etat) {
    if ($etat.Dialogues.Count -gt 0) {
        throw ("Une boîte de dialogue est ouverte au bout de {0:N1} s : « {1} »." -f $chrono.Elapsed.TotalSeconds, ($etat.Dialogues -join ' », « '))
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
        Exiger-SansDialogue $etat
        if ($etat.Base -and $etat.Profil -and $etat.Navigateur -and $etat.Fenetre) {
            $pret = $true
            break
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $pret) {
        throw ("Au bout de {0} s : base créée {1}, profil créé {2}, navigateur intégré lancé {3}, fenêtre affichée {4}." -f $Attente, $etat.Base, $etat.Profil, $etat.Navigateur, $etat.Fenetre)
    }
    Write-Host ("Prêt au bout de {0:N1} s : base et profil créés, navigateur intégré lancé, fenêtre « Sollicito » affichée, sans boîte de dialogue." -f $chrono.Elapsed.TotalSeconds)

    $reste = $Vie - $chrono.Elapsed.TotalSeconds
    if ($reste -gt 0) {
        Start-Sleep -Milliseconds ([int][math]::Ceiling($reste * 1000))
    }
    if ($processus.HasExited) {
        throw ("Sollicito.exe s'est arrêté avant {0} s, avec le code {1}." -f $Vie, $processus.ExitCode)
    }
    $etat = Etat
    Exiger-SansDialogue $etat
    if (-not $etat.Fenetre) {
        throw ("La fenêtre « Sollicito » n'est plus affichée au bout de {0:N1} s." -f $chrono.Elapsed.TotalSeconds)
    }
    Write-Host ("Toujours lancé au bout de {0:N1} s, fenêtre affichée, sans boîte de dialogue." -f $chrono.Elapsed.TotalSeconds)
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
