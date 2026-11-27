<#
.SYNOPSIS
  Contrôle l'exe publié de Sollicito : un seul fichier, un paquet sans fichier superflu, et aucun chemin de la
  machine de construction (ADR-0009).

.DESCRIPTION
  Lit le manifeste du paquet de l'exe d'un seul fichier (format du SDK .NET, version 6 ou plus), décompresse chaque
  fichier embarqué, puis vérifie :
    - que le dossier de publication ne contient que Sollicito.exe ;
    - que le paquet embarque les assemblies de Sollicito, et aucun fichier superflu : symboles, documentation XML,
      assembly WPF de WebView2, hôte et fichiers de lancement de Sollicito.Api, module d'hébergement sous IIS ;
    - qu'aucun fichier, lu décompressé, ni l'exe lui-même, ne contient un chemin de la machine de construction
      (dossier du dépôt, profil Windows, dossier temporaire, nom du compte), en UTF-8 comme en UTF-16, sans tenir
      compte de la casse ;
    - que les chemins de symboles des assemblies de Sollicito commencent par /_/ (ContinuousIntegrationBuild). Lire
      ces chemins dans les fichiers décompressés sert aussi de témoin : le contrôle voit bien l'intérieur du paquet ;
    - qu'aucun fichier, ni l'exe, ne contient une marque de la version de test de la restauration (#28) : le fichier
      qui remplace son dialogue, et la variable de son point d'arrêt. Un témoin de la version publiée doit s'y lire.
  Le script ne lance pas l'exe. Il sort avec le code 1 au premier écart, après les avoir tous signalés.

.PARAMETER Dossier
  Le dossier de publication (sortie de dotnet publish).

.PARAMETER Interdits
  Chaînes interdites, en plus de celles que le script tire de la machine : dossier du dépôt, profil Windows, dossier
  temporaire, nom du compte, et en CI GITHUB_WORKSPACE et RUNNER_TEMP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Dossier,
    [string[]]$Interdits = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ecarts = New-Object System.Collections.Generic.List[string]
function Signaler([string]$ecart) {
    $ecarts.Add($ecart)
    Write-Host "ÉCART : $ecart"
}

# --- La recherche d'une marque, et son autotest ---
# Vrai si le texte se lit dans les octets : tels quels (UTF-8 et ASCII), ou en UTF-16 à l'un des deux alignements.
function Contient-Texte([byte[]]$contenu, [string]$texte) {
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $enOctets = $latin1.GetString([Text.Encoding]::UTF8.GetBytes($texte))
    return $latin1.GetString($contenu).IndexOf($enOctets, [StringComparison]::Ordinal) -ge 0 -or
        [Text.Encoding]::Unicode.GetString($contenu).IndexOf($texte, [StringComparison]::Ordinal) -ge 0 -or
        ($contenu.Length -gt 1 -and [Text.Encoding]::Unicode.GetString($contenu, 1, $contenu.Length - 1).IndexOf($texte, [StringComparison]::Ordinal) -ge 0)
}

# Le témoin de la version publiée, plus bas, ne prouve que la lecture qui l'a trouvé (#95, point 8) : chacune des trois
# se vérifie d'abord ici, sur un texte rangé comme elle seule le voit ; un texte absent ne se trouve pas.
$sonde = 'sonde-de-la-recherche'
$rangements = [ordered]@{
    'octets'                        = [Text.Encoding]::UTF8.GetBytes("ab$sonde")
    'UTF-16, à un alignement pair'  = [Text.Encoding]::Unicode.GetBytes("a$sonde")
    'UTF-16, à un alignement impair' = [byte[]](@([byte]0x61) + [Text.Encoding]::Unicode.GetBytes("a$sonde"))
}
foreach ($rangement in $rangements.GetEnumerator()) {
    if (-not (Contient-Texte $rangement.Value $sonde)) {
        Signaler "la recherche d'une marque ne voit pas un texte rangé en $($rangement.Key)"
    }
    if (Contient-Texte $rangement.Value 'texte-absent-de-la-sonde') {
        Signaler "la recherche d'une marque trouve un texte absent ($($rangement.Key))"
    }
}
Write-Host "Lectures de la recherche des marques vérifiées : $($rangements.Count)."

# --- Le dossier de publication ne contient que l'exe ---
$fichiersPublies = @(Get-ChildItem -LiteralPath $Dossier -Recurse -File | ForEach-Object { $_.Name })
if ($fichiersPublies.Count -ne 1 -or $fichiersPublies[0] -ne 'Sollicito.exe') {
    Signaler ("le dossier de publication doit ne contenir que Sollicito.exe ; il contient : " + ($fichiersPublies -join ', '))
}
$exe = Join-Path $Dossier 'Sollicito.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "Sollicito.exe est introuvable dans $Dossier."
}
$octets = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $exe).Path)
Write-Host ("Sollicito.exe : {0:N1} Mo ({1} octets)" -f ($octets.Length / 1e6), $octets.Length)

# Latin-1 fait correspondre chaque octet à un caractère : une recherche de texte y devient une recherche d'octets.
$latin1 = [Text.Encoding]::GetEncoding(28591)

# --- Le manifeste du paquet ---
# L'hôte de l'exe porte, juste avant cette signature (SHA-256 de « .net core bundle »), la position de l'en-tête du
# paquet. Les fichiers embarqués suivent l'hôte : la première occurrence est la sienne.
$signature = [byte[]](0x8b, 0x12, 0x02, 0xb9, 0x6a, 0x61, 0x20, 0x38, 0x72, 0x7b, 0x93, 0x02, 0x14, 0xd7, 0xa0, 0x32,
    0x13, 0xf5, 0xb9, 0xe6, 0xef, 0xae, 0x33, 0x18, 0xee, 0x3b, 0x2d, 0xce, 0x24, 0xb3, 0x6a, 0xae)
$texteDeLExe = $latin1.GetString($octets)
$position = $texteDeLExe.IndexOf($latin1.GetString($signature), [StringComparison]::Ordinal)
if ($position -lt 8) {
    throw "Sollicito.exe n'est pas un exe .NET d'un seul fichier : signature du paquet introuvable."
}
$flux = New-Object IO.MemoryStream(, $octets)
$lecteur = New-Object IO.BinaryReader($flux, [Text.Encoding]::UTF8)
$flux.Position = $position - 8
$flux.Position = $lecteur.ReadInt64()
$versionMajeure = $lecteur.ReadUInt32()
$versionMineure = $lecteur.ReadUInt32()
$nombreDeFichiers = $lecteur.ReadInt32()
[void]$lecteur.ReadString()
if ($versionMajeure -lt 6) {
    throw "Paquet en version $versionMajeure.$versionMineure : ce script lit la version 6 ou plus."
}
# Position et taille de deps.json et de runtimeconfig.json, puis les drapeaux du paquet.
[void]$lecteur.ReadBytes(40)

$entrees = New-Object System.Collections.Generic.List[object]
for ($i = 0; $i -lt $nombreDeFichiers; $i++) {
    $entrees.Add([pscustomobject]@{
            Decalage        = $lecteur.ReadInt64()
            Taille          = $lecteur.ReadInt64()
            TailleCompressee = $lecteur.ReadInt64()
            Sorte           = $lecteur.ReadByte()
            Nom             = $lecteur.ReadString()
        })
}
$compresses = @($entrees | Where-Object { $_.TailleCompressee -gt 0 }).Count
Write-Host "Paquet v$versionMajeure.$versionMineure : $nombreDeFichiers fichiers, dont $compresses compressés."

function Lire-Entree($entree) {
    if ($entree.TailleCompressee -eq 0) {
        $contenu = New-Object byte[] $entree.Taille
        [Array]::Copy($octets, $entree.Decalage, $contenu, 0, $entree.Taille)
        return , $contenu
    }
    $source = New-Object IO.MemoryStream($octets, [int]$entree.Decalage, [int]$entree.TailleCompressee, $false)
    $inflation = New-Object IO.Compression.DeflateStream($source, [IO.Compression.CompressionMode]::Decompress)
    $sortie = New-Object IO.MemoryStream([int]$entree.Taille)
    $inflation.CopyTo($sortie)
    $inflation.Dispose()
    if ($sortie.Length -ne $entree.Taille) {
        throw "$($entree.Nom) : $($sortie.Length) octets décompressés au lieu de $($entree.Taille)."
    }
    return , $sortie.ToArray()
}

# --- Ce que le paquet embarque ---
# Sorte 2 : fichier natif, extrait sous %TEMP%\.net au premier lancement.
$natifs = @($entrees | Where-Object { $_.Sorte -eq 2 } | ForEach-Object { $_.Nom })
Write-Host ("Fichiers natifs : " + ($natifs -join ', '))
$requis = 'Sollicito.dll', 'Sollicito.Api.dll', 'Sollicito.Application.dll', 'Sollicito.Domain.dll',
'Sollicito.Infrastructure.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll',
'e_sqlite3.dll', 'Sollicito.deps.json', 'Sollicito.runtimeconfig.json'
foreach ($nom in $requis) {
    if (-not ($entrees | Where-Object { $_.Nom -eq $nom })) {
        Signaler "le paquet n'embarque pas $nom"
    }
}
$superflus = 'Microsoft.Web.WebView2.Wpf.dll', 'Sollicito.Api.exe', 'Sollicito.Api.deps.json',
'Sollicito.Api.runtimeconfig.json', 'aspnetcorev2_inprocess.dll'
foreach ($entree in $entrees) {
    $nomCourt = [IO.Path]::GetFileName($entree.Nom)
    if ($entree.Nom -match '\.(pdb|xml)$' -or $superflus -contains $nomCourt) {
        Signaler "le paquet embarque un fichier superflu : $($entree.Nom)"
    }
}
if ($compresses -eq 0) {
    Signaler "aucun fichier du paquet n'est compressé (EnableCompressionInSingleFile)"
}

# --- Aucun chemin de la machine de construction ---
$depot = (& git rev-parse --show-toplevel 2>$null)
$candidats = @($Interdits) + @(
    $depot,
    (Get-Location).Path,
    $env:USERPROFILE,
    [IO.Path]::GetTempPath(),
    $env:GITHUB_WORKSPACE,
    $env:RUNNER_TEMP,
    $env:USERNAME
)
$motifs = New-Object System.Collections.Generic.List[string]
foreach ($candidat in $candidats) {
    if ([string]::IsNullOrWhiteSpace($candidat)) { continue }
    $sansFin = $candidat.TrimEnd('\', '/')
    if ($sansFin.Length -lt 4) { continue }
    foreach ($forme in @($sansFin.Replace('/', '\'), $sansFin.Replace('\', '/'))) {
        if (-not $motifs.Contains($forme)) { $motifs.Add($forme) }
    }
}
# Un nom de compte court se cherche entre séparateurs de chemin : seul, il apparaîtrait partout.
if (-not [string]::IsNullOrWhiteSpace($env:USERNAME) -and $env:USERNAME.Length -lt 4) {
    foreach ($forme in @("\$($env:USERNAME)\", "/$($env:USERNAME)/")) {
        if (-not $motifs.Contains($forme)) { $motifs.Add($forme) }
    }
}
Write-Host "Chaînes cherchées : $($motifs.Count) (dossier du dépôt, profil, dossier temporaire, nom du compte)."

function Chercher-Chemins([byte[]]$contenu, [string]$ou) {
    # Le contenu lu de trois façons : octets (UTF-8 et ASCII), et UTF-16 aux deux alignements.
    $lectures = @(
        $latin1.GetString($contenu),
        [Text.Encoding]::Unicode.GetString($contenu),
        $(if ($contenu.Length -gt 1) { [Text.Encoding]::Unicode.GetString($contenu, 1, $contenu.Length - 1) } else { '' })
    )
    foreach ($motif in $motifs) {
        $enOctets = $latin1.GetString([Text.Encoding]::UTF8.GetBytes($motif))
        if ($lectures[0].IndexOf($enOctets, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $lectures[1].IndexOf($motif, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $lectures[2].IndexOf($motif, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Signaler "$ou contient un chemin de la machine de construction : $motif"
            return
        }
    }
}

# L'exe tel quel couvre l'hôte et les fichiers non compressés ; chaque fichier compressé est lu décompressé.
Chercher-Chemins $octets 'Sollicito.exe'
$cheminsDeSymboles = 0
foreach ($entree in $entrees) {
    $nomCourt = [IO.Path]::GetFileName($entree.Nom)
    $estDeSollicito = $nomCourt -like 'Sollicito*.dll'
    if ($entree.TailleCompressee -eq 0 -and -not $estDeSollicito) { continue }
    $contenu = Lire-Entree $entree
    if ($entree.TailleCompressee -gt 0) {
        Chercher-Chemins $contenu $entree.Nom
    }
    if ($estDeSollicito) {
        # Entrée CodeView du répertoire de débogage : « RSDS », GUID (16 octets), âge (4 octets), chemin terminé par 0.
        $texte = $latin1.GetString($contenu)
        $rsds = $texte.IndexOf('RSDS', [StringComparison]::Ordinal)
        while ($rsds -ge 0) {
            $debut = $rsds + 24
            $fin = [Array]::IndexOf($contenu, [byte]0, $debut)
            if ($fin -gt $debut) {
                $chemin = [Text.Encoding]::UTF8.GetString($contenu, $debut, $fin - $debut)
                if ($chemin -match '\.pdb$') {
                    $cheminsDeSymboles++
                    if (-not $chemin.StartsWith('/_/')) {
                        Signaler "$($entree.Nom) garde un chemin de symboles qui ne commence pas par /_/ : $chemin"
                    }
                }
            }
            $rsds = $texte.IndexOf('RSDS', $rsds + 4, [StringComparison]::Ordinal)
        }
    }
}
Write-Host "Chemins de symboles lus dans les assemblies de Sollicito : $cheminsDeSymboles."
if ($cheminsDeSymboles -eq 0) {
    Signaler "aucun chemin de symboles lu dans les assemblies de Sollicito : le contrôle ne voit pas l'intérieur du paquet"
}

# --- Aucune marque de la version de test de la restauration (#28) ---
# Le remplaçant du dialogue de la restauration et son point d'arrêt ne se compilent que dans la version de test : leurs
# marques ne se lisent nulle part dans l'exe publié, ni dans ses fichiers décompressés. Les marques des autres écarts de
# la version de test attendent #91. Témoin : le type du message de la restauration, que la version publiée porte, se lit
# dans Sollicito.dll décompressé.
$marquesDeTest = @('restauration-a-confirmer.txt', 'SOLLICITO_TEST_ARRET_RESTAURATION')
$temoinPublie = 'restaurerUneSauvegarde'
$temoinLu = $false
$lectures = @([pscustomobject]@{ Nom = 'Sollicito.exe'; Contenu = $octets })
foreach ($entree in $entrees) {
    if ([IO.Path]::GetFileName($entree.Nom) -like 'Sollicito*.dll') {
        $lectures += [pscustomobject]@{ Nom = $entree.Nom; Contenu = (Lire-Entree $entree) }
    }
}
foreach ($lecture in $lectures) {
    foreach ($marque in $marquesDeTest) {
        if (Contient-Texte $lecture.Contenu $marque) {
            Signaler "$($lecture.Nom) contient une marque de la version de test : $marque"
        }
    }
    if ($lecture.Nom -eq 'Sollicito.dll' -and (Contient-Texte $lecture.Contenu $temoinPublie)) {
        $temoinLu = $true
    }
}
Write-Host "Marques de la version de test cherchées : $($marquesDeTest.Count), dans $($lectures.Count) fichiers."
if (-not $temoinLu) {
    Signaler "le témoin « $temoinPublie » ne se lit pas dans Sollicito.dll : le contrôle des marques de la version de test ne voit pas l'intérieur du paquet"
}

if ($ecarts.Count -gt 0) {
    Write-Host "$($ecarts.Count) écart(s)."
    exit 1
}
Write-Host 'Exe conforme.'
# Explicite : sinon le code de sortie serait celui de la dernière commande externe lancée (git).
exit 0
