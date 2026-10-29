param([string]$InstallRoot = 'C:\Nexo', [switch]$SkipStrings)
$ErrorActionPreference = 'Stop'
# Static inspection only: never starts an emulator or constructs NexoService.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
public static class NexoBinaryStrings {
 public static string[] Find(string path, string pattern) {
  byte[] bytes=File.ReadAllBytes(path); var output=new List<string>();
  var match=new Regex(pattern,RegexOptions.IgnoreCase); int start=0;
  for(int i=0;i<=bytes.Length;i++) {
   if(i<bytes.Length && ((bytes[i]>=32 && bytes[i]<=126)||bytes[i]==10||bytes[i]==13||bytes[i]==9)) continue;
   if(i-start>=10) {
    string s=Encoding.ASCII.GetString(bytes,start,i-start);
    if(match.IsMatch(s)) output.Add(s.Length>6000?s.Substring(0,6000):s);
   }
   start=i+1;
  } return output.ToArray();
 }
}
'@
$binaries = [ordered]@{
    PS2 = 'PS2\PCSX2\pcsx2-qt.exe'
    PS3 = 'PS3\RPCS3\rpcs3.exe'
    PSP = 'PSP\PPSSPP\PPSSPPWindows64.exe'
    'GameCube-Wii' = 'GameCube-Wii\Dolphin\Dolphin-x64\Dolphin.exe'
    PS4 = 'PS4\shadPS4\shadPS4.exe'
    Retro = 'Retro\RetroArch\RetroArch-Win64\retroarch.exe'
}
$assemblyPath = Join-Path $PSScriptRoot '..\src\Nexo.Core\bin\Release\net48\Nexo.Core.dll'
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($assemblyPath))
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$getProfile = $assembly.GetType('Nexo.Core.Catalog').GetMethod('Get', $flags)
$detect = $assembly.GetType('Nexo.Core.NexoService').GetMethod('DetectExecutable', $flags)
if (-not $detect) { throw 'Compila primero Nexo.Core en Release.' }
foreach ($entry in $binaries.GetEnumerator()) {
    $path = Join-Path (Join-Path $InstallRoot 'Emuladores') $entry.Value
    $profile = $getProfile.Invoke($null, @($entry.Key))
    $folder = Join-Path (Join-Path $InstallRoot 'Emuladores') $entry.Key
    $detected = $detect.Invoke($null, @($profile, [string]$folder))
    if ($detected -ine $path) { throw "Detección incorrecta para $($entry.Key): $detected" }
    Write-Output ('OK detección: ' + $entry.Key)
    Write-Output ($entry.Key + ': ' + $path)
    Write-Output ('SHA256: ' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash)
    if (-not $SkipStrings) {
        [NexoBinaryStrings]::Find($path, '^Usage:|^usage:|^\s*-(h,|L,|fullscreen:|batch:|nogui:)|no-gui|--exec|--batch|^Game path or ID|^-g,--game|^This is a CLI|^Boot filename found in args:|^Run RPCS3 without') | Select-Object -Unique -First 20
    }
}
$retro = $assembly.GetType('Nexo.Core.RetroCores')
$map = $retro.GetField('Map', $flags).GetValue($null)
$coreFolder = Join-Path (Split-Path (Join-Path (Join-Path $InstallRoot 'Emuladores') $binaries.Retro)) 'cores'
$names = @($map.Values | ForEach-Object { $_ } | Sort-Object -Unique)
foreach ($name in $names) {
    if (-not (Test-Path -LiteralPath (Join-Path $coreFolder ($name + '_libretro.dll')) -PathType Leaf)) { throw "Falta el núcleo del mapa: $name" }
}
Write-Output ('OK núcleos del mapa: ' + $names.Count + '/' + $names.Count)
