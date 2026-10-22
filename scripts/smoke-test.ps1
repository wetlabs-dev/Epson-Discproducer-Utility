param([string]$Executable = "$PSScriptRoot/../artifacts/win-x64/DiscproducerDiagnostics.App.exe")
$ErrorActionPreference = 'Stop'
# Launches the real WPF window; no scan or hardware query is triggered.
$stderrPath = Join-Path ([System.IO.Path]::GetTempPath()) ('discproducer-smoke-' + [guid]::NewGuid() + '.log')
$app = Start-Process -FilePath (Resolve-Path $Executable) -PassThru -RedirectStandardError $stderrPath
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 250
        $app.Refresh()
        if ($app.HasExited) {
            if (Test-Path $stderrPath) { Get-Content $stderrPath | Write-Output }
            Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-2)} -ErrorAction SilentlyContinue |
                Where-Object { $_.Message -like '*DiscproducerDiagnostics*' } | ForEach-Object { Write-Output $_.Message }
            throw "Application exited before showing a window: $($app.ExitCode)"
        }
    } while ($app.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($app.MainWindowHandle -eq 0) { throw 'WPF main window did not open' }
    if ($app.MainWindowTitle -ne 'Discproducer Diagnostics') { throw 'Unexpected window title' }
    Write-Output 'WPF window launch passed (no physical device tested).'
    $app.CloseMainWindow() | Out-Null
    if (-not $app.WaitForExit(10000)) { throw 'Application did not close cleanly' }
    if ($app.ExitCode -ne 0) { throw "Application exited with $($app.ExitCode)" }
} finally {
    if (-not $app.HasExited) { $app.Kill() }
    $app.Dispose()
}

# Verify the packaged WinExe's redirected JSON worker path and actual native enumeration.
$start = [System.Diagnostics.ProcessStartInfo]::new()
$start.FileName = (Resolve-Path $Executable).Path
$start.Arguments = '--worker'
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardInput = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$worker = [System.Diagnostics.Process]::Start($start)
try {
    $output = $worker.StandardOutput.ReadToEndAsync()
    $errors = $worker.StandardError.ReadToEndAsync()
    $worker.StandardInput.WriteLine('{"Operation":"scan"}')
    $worker.StandardInput.Close()
    if (-not $worker.WaitForExit(30000)) { throw 'Native discovery worker timed out' }
    if ($worker.ExitCode -ne 0) { throw "Discovery worker failed: $($errors.Result)" }
    $reply = $output.Result | ConvertFrom-Json
    if ($null -ne $reply.Error -or $null -eq $reply.Discovery) { throw "Native discovery failed: $($output.Result)" }
    Write-Output "Packaged worker/native enumeration passed: $($reply.Discovery.Devices.Count) Epson nodes. No device query was sent."
} finally {
    if (-not $worker.HasExited) { $worker.Kill() }
    $worker.Dispose()
}
