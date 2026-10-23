param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Invoke-Dotnet { & dotnet @args; if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE)" } }
Invoke-Dotnet restore DiscproducerDiagnostics.sln
Invoke-Dotnet build DiscproducerDiagnostics.sln -c $Configuration --no-restore
if (-not $SkipTests) { Invoke-Dotnet test tests/DiscproducerDiagnostics.Tests -c $Configuration --no-build --logger 'trx;LogFileName=tests.trx' }
Invoke-Dotnet publish src/DiscproducerDiagnostics.App -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' '-p:DebugType=None' -o artifacts/win-x64
Invoke-Dotnet publish src/DiscproducerDiagnostics.Cli -c $Configuration -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:PublishTrimmed=false' '-p:DebugType=None' -o artifacts/cli-win-x64
Copy-Item artifacts/cli-win-x64/discdiag-cli.exe artifacts/win-x64/discdiag-cli.exe
Copy-Item README.md artifacts/win-x64/README.md
Copy-Item docs artifacts/win-x64/docs -Recurse -Force
Compress-Archive -Path artifacts/win-x64/* -DestinationPath artifacts/DiscproducerDiagnostics-win-x64.zip -Force
Get-FileHash artifacts/DiscproducerDiagnostics-win-x64.zip -Algorithm SHA256 | Format-List
