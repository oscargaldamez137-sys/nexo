[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_NOLOGO = '1'
$dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
$tests = Join-Path $root 'tests\Nexo.Tests\Nexo.Tests.csproj'
$app = Join-Path $root 'src\Nexo\Nexo.csproj'
$dist = Join-Path $root 'dist'
$package = Join-Path $dist 'package'
# Compiled program: internal input of the installer only (Nexo is distributed solely as Instalar Nexo.exe).
$payload = Join-Path $package 'app'
if (-not (Test-Path -LiteralPath $app)) { throw 'Falta src\Nexo\Nexo.csproj. Termina la interfaz WPF antes de empaquetar.' }
# Single source of the version: <Version> in Nexo.csproj (assembly, installer and GitHub tag vX.Y.Z).
$version = ([xml](Get-Content -LiteralPath $app -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Nexo.csproj debe declarar <Version>X.Y.Z</Version>.' }

function Run-Dotnet([string[]]$Arguments) {
    & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet falló ($LASTEXITCODE)." }
}

foreach ($project in @($tests, $app)) {
    # The project directory is the ONLY package source: no network restore or audit.
    Run-Dotnet -Arguments @('restore', $project, '--source', $root, '-p:NuGetAudit=false', '-p:AutomaticallyUseReferenceAssemblyPackages=false')
}
Run-Dotnet -Arguments @('build', $tests, '-c', 'Release', '--no-restore')
# The payload folder is pure build output: emptied every time so no stale file is packaged.
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Recurse -Force }
Run-Dotnet -Arguments @('build', $app, '-c', 'Release', '--no-restore', '-o', $payload, '-p:AutomaticallyUseReferenceAssemblyPackages=false')
Copy-Item -LiteralPath (Join-Path $root 'LEEME.md') -Destination (Join-Path $payload 'LEEME.md') -Force
[void][IO.Directory]::CreateDirectory($package)
# Build payload from compiled files only. User libraries and nexo.json are never packaged.
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$zipPath = Join-Path $package 'Nexo.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $pending = New-Object 'Collections.Generic.Queue[string]'
    $pending.Enqueue($payload)
    while ($pending.Count -gt 0) {
        foreach ($file in (Get-ChildItem -LiteralPath $pending.Dequeue() -Force)) {
            $relative = $file.FullName.Substring($payload.Length + 1)
            if ($relative -match '^(Emuladores|Juegos|\.nexo-install-[^\\]+)(\\|$)' -or $relative -imatch '^nexo\.json($|\.)' -or $file.Extension -ieq '.pdb') { continue }
            if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'No se empaquetan enlaces.' }
            if ($file.PSIsContainer) { $pending.Enqueue($file.FullName); continue }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative.Replace('\', '/'))
        }
    }
} finally { $zip.Dispose() }
foreach ($name in @('instalar.ps1', 'desinstalar.ps1', 'Desinstalar Nexo.vbs')) {
    Copy-Item -LiteralPath (Join-Path $root ('packaging\' + $name)) -Destination (Join-Path $package $name) -Force
}
Copy-Item -LiteralPath (Join-Path $root 'LEEME.md') -Destination (Join-Path $dist 'LEEME.md') -Force
# Instalar Nexo.exe is the only Windows deliverable (published on GitHub as Instalar-Nexo.exe).
$iexpress = Join-Path $env:SystemRoot 'System32\iexpress.exe'
$installer = Join-Path $dist 'Instalar Nexo.exe'
if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force }
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
VersionInfo=VersionSection
[VersionSection]
FromFile=
CompanyName=Oscar Galdamez
FileDescription=Instalador de Nexo
FileVersion=$version.0
ProductVersion=$version
ProductName=Nexo
InternalName=Instalar-Nexo
OriginalFilename=Instalar-Nexo.exe
LegalCopyright=(c) 2026 Oscar Galdamez. Licencia MIT.
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$installer
FriendlyName=Nexo
AppLaunched=powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File instalar.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="instalar.ps1"
FILE1="Nexo.zip"
FILE2="desinstalar.ps1"
FILE3="Desinstalar Nexo.vbs"
[SourceFiles]
SourceFiles0=$package\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
"@
$sedPath = Join-Path $package 'Nexo.sed'
$sed = $sed.Replace("`r`n", "`n").Replace("`n", "`r`n") + "`r`n"
[IO.File]::WriteAllText($sedPath, $sed, [Text.Encoding]::Default)
# IExpress's legacy parser fails here with a quoted absolute SED path.
# A fixed relative name also works when the project directory contains spaces.
$process = Start-Process -FilePath $iexpress -ArgumentList '/N /Q Nexo.sed' -WorkingDirectory $package -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $installer)) { throw "IExpress no generó el instalador (código $($process.ExitCode)). Revisa dist\package\Nexo.sed." }
# Tests run last; a failing suite makes the complete build fail.
& (Join-Path $root 'tests\Nexo.Tests\bin\Release\net48\Nexo.Tests.exe') $root
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de Core.' }
& (Join-Path $root 'tests\Test-Installer.ps1') -PackageDirectory $package -Workspace $root
if ((Get-Item -LiteralPath $installer).VersionInfo.ProductVersion -ne $version) { throw 'El instalador no lleva la versión de Nexo.csproj.' }
Write-Host "Listo. Nexo $version. Instalador: $installer (publícalo en GitHub como Instalar-Nexo.exe, versión v$version)"
