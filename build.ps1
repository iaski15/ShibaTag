# Builds ShibaTag into release\:
#   ShibaTag.exe                  standalone portable app (runtime bundled)
#   ShibaTag-Setup-<version>.exe  installer: per-user install + Start menu + Installed apps entry
# Version lives in Directory.Build.props.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (Test-Path release) { Remove-Item release -Recurse -Force }

dotnet publish ShibaTag.csproj -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# The setup embeds release\ShibaTag.exe, so it must build after the publish above.
dotnet build Setup\ShibaTagSetup.csproj -c Release
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
Copy-Item Setup\bin\Release\net48\ShibaTag-Setup.exe "release\ShibaTag-Setup-$version.exe"

Get-ChildItem release | Format-Table Name, @{ n = 'MB'; e = { '{0:N1}' -f ($_.Length / 1MB) } } -AutoSize
