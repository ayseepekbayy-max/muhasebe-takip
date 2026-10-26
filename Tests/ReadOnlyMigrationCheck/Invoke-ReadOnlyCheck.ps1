param([Parameter(Mandatory=$true)][Security.SecureString]$Connection)
$ErrorActionPreference = 'Stop'
$ptr = [IntPtr]::Zero
$plain = $null
$process = $null
try {
    $dll = Join-Path $PSScriptRoot 'bin\Release\net10.0\ReadOnlyMigrationCheck.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw 'Build missing' }
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
    $start.Arguments = '"' + $dll + '"'
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Prevent inherited runtime tracing, profiling and startup injection in this child.
    foreach ($key in @($start.EnvironmentVariables.Keys)) {
        if ($key -match '^(DOTNET_|CORECLR_|COR_|COMPlus_|PG|NPGSQL_|OTEL_)') {
            $start.EnvironmentVariables.Remove($key)
        }
    }
    $start.EnvironmentVariables['DOTNET_EnableDiagnostics'] = '0'
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Connection)
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $process.StandardInput.Write($plain)
    $process.StandardInput.Close()
    $plain = $null
    if (-not $process.WaitForExit(120000)) {
        $process.Kill()
        throw 'Timeout'
    }
    $result = $stdout.GetAwaiter().GetResult()
    [void]$stderr.GetAwaiter().GetResult()
    if ($process.ExitCode -ne 0) { throw 'Check failed' }
    Write-Output $result
}
catch {
    Write-Output 'KONTROL DURDU: Baglanti veya kontrol hatasi; ayrintilar gizlendi.'
}
finally {
    $plain = $null
    if ($ptr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    if ($null -ne $process) {
        try { if (-not $process.HasExited) { $process.Kill() } } catch { }
        $process.Dispose()
    }
}
