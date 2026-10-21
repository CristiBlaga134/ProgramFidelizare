# Produce programul de instalare: publică executabilul, apoi compilează
# scriptul Inno Setup. Rezultatul: installer\Output\ProgramFidelizare-Setup-<versiune>.exe
#
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#
# Cu -SkipPublish se folosește executabilul deja aflat în publish\.

param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Fidelizare.App\Fidelizare.App.csproj'
$publishDir = Join-Path $root 'publish'

# Compilatorul Inno Setup poate fi instalat pentru toți utilizatorii sau doar
# pentru cel curent; se caută în ambele locuri și în PATH.
$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)

$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { $iscc = $command.Source }
}

if (-not $iscc) {
    throw 'Nu am găsit Inno Setup 6 (ISCC.exe). Se descarcă de la https://jrsoftware.org/isdl.php'
}

if (-not $SkipPublish) {
    Write-Host 'Public executabilul...'
    dotnet publish $project -c Release -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw 'Publicarea a eșuat.' }
}

# Versiunea vine din proiect, ca installerul și executabilul să spună același lucru.
$version = (dotnet msbuild $project -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or -not $version) { throw 'Nu am putut citi versiunea din proiect.' }

Write-Host "Compilez installerul pentru versiunea $version..."
& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'ProgramFidelizare.iss')
if ($LASTEXITCODE -ne 0) { throw 'Compilarea installerului a eșuat.' }

Write-Host ''
Write-Host "Gata: $(Join-Path $PSScriptRoot "Output\ProgramFidelizare-Setup-$version.exe")"
