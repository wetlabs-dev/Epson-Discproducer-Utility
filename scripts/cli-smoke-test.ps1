$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$cli = Join-Path $PWD 'artifacts/win-x64/discdiag-cli.exe'
$work = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory $work | Out-Null
try {
  & $cli errors 0x01 --json | Out-File (Join-Path $work 'codes.json')
  if ($LASTEXITCODE -ne 0) { throw 'CLI code reference failed' }
  $codes = Get-Content (Join-Path $work 'codes.json') -Raw | ConvertFrom-Json
  if ($codes[0].HexCode -ne '0x00000001') { throw 'Wrong error reference' }
  & $cli analyze tests/DiscproducerDiagnostics.Tests/Fixtures/usb_u3v_sample.pcapng --json | Out-File (Join-Path $work 'analysis.json')
  if ($LASTEXITCODE -ne 5) { throw 'CLI must identify incomplete capture' }
  $analysis = Get-Content (Join-Path $work 'analysis.json') -Raw | ConvertFrom-Json
  if ($analysis.ParserVersion -ne 'usbpcap-1.3.0') { throw 'Missing parser provenance' }
  & $cli compare tests/DiscproducerDiagnostics.Tests/Fixtures/usb_u3v_sample.pcapng tests/DiscproducerDiagnostics.Tests/Fixtures/usb_u3v_sample.pcapng --json | Out-File (Join-Path $work 'comparison.json')
  if ($LASTEXITCODE -ne 5) { throw 'CLI comparison failed' }
  $source = Join-Path $work 'source'
  New-Item -ItemType Directory (Join-Path $source 'device') -Force | Out-Null
  New-Item -ItemType Directory (Join-Path $source 'communications') -Force | Out-Null
  '{"ApplicationVersion":"1.3.0","ExportSchemaVersion":3,"SessionId":"00000000-0000-0000-0000-000000000001","Mode":"TechnicalDiagnostics"}' | Set-Content (Join-Path $source 'manifest.json')
  '{}' | Set-Content (Join-Path $source 'evidence-sha256.json')
  '{}' | Set-Content (Join-Path $source 'device/identity.json')
  '{}' | Set-Content (Join-Path $source 'device/windows-device-properties.json')
  '[]' | Set-Content (Join-Path $source 'communications/transactions.json')
  $bundle = Join-Path $work 'bundle.zip'
  Compress-Archive -Path "$source/*" -DestinationPath $bundle
  foreach ($verb in @('inspect','validate')) {
    & $cli $verb $bundle --json | Out-File (Join-Path $work "$verb.json")
    if ($LASTEXITCODE -ne 0) { throw "CLI $verb failed" }
  }
  & $cli report $bundle --output (Join-Path $work 'report.html') --json
  if ($LASTEXITCODE -ne 0) { throw 'CLI report failed' }
  if (!(Test-Path (Join-Path $work 'report.html'))) { throw 'No report' }
  & $cli scan 2>$null
  if ($LASTEXITCODE -ne 2) { throw 'CLI accepted a hardware operation' }
  Write-Host 'Packaged offline CLI inspect/analyze/compare/report/validate/errors passed.'
} finally { Remove-Item $work -Recurse -Force }
