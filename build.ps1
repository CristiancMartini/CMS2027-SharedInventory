param(
    [string]$GameDir = "D:\Steam\steamapps\common\Car Mechanic Simulator 2027 Demo",
    [string]$Dotnet = "dotnet"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$payload = Join-Path $root "payload"
$dist = Join-Path $root "dist"
$bepinexUrl = "https://builds.bepinex.dev/projects/bepinex_be/791/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791%2Bb782da4.zip"

if (-not (Test-Path "$GameDir\BepInEx\interop\Assembly-CSharp.dll")) {
    throw "BepInEx interop not found in '$GameDir'. Install BepInEx in the game and launch it once first."
}

New-Item -ItemType Directory -Force $payload, $dist | Out-Null

if (-not (Test-Path "$payload\BepInEx.zip")) {
    Write-Host "Downloading BepInEx..."
    Invoke-WebRequest -UseBasicParsing $bepinexUrl -OutFile "$payload\BepInEx.zip"
}

Write-Host "Building plugin..."
& $Dotnet build "$root\src\SharedInventory\SharedInventory.csproj" -c Release -p:GameDir="$GameDir" -o "$root\build\plugin"
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }
Copy-Item "$root\build\plugin\SharedInventory.dll" "$payload\SharedInventory.dll" -Force

Write-Host "Building installer..."
& $Dotnet build "$root\src\Installer\Installer.csproj" -c Release -o "$root\build\installer"
if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
Copy-Item "$root\build\installer\CMS2027-SharedInventory-Installer.exe" "$dist\" -Force
Copy-Item "$root\build\plugin\SharedInventory.dll" "$dist\" -Force

Write-Host "Done: $dist"
