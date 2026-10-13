# Shared host-only VMware CLI wrapper. Never prints credentials or full arguments.
function Invoke-VmCheck([string[]]$Arguments, [int]$TimeoutMs = 15000) {
    $info = [System.Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Vmrun
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        [void]$process.Start()
        $outputTask = $process.StandardOutput.ReadToEndAsync()
        $errorTask = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit($TimeoutMs)) {
            $process.Kill($true)
            $process.WaitForExit()
            return @{ Success = $false; Reason = 'VMware did not answer within the operation timeout.'; Output = '' }
        }
        $output = $outputTask.GetAwaiter().GetResult() + $errorTask.GetAwaiter().GetResult()
        $reason = if ($output -match '(?i)password.*(required|incorrect)|invalid.*password|decrypt') {
            'VM encryption password was missing or not accepted.'
        } elseif ($output -match '(?i)guest.*(authentication|permissions)|invalid.*(user|credential)|login|logon') {
            'Guest Windows sign-in was not accepted. Use the account password, not a PIN.'
        } elseif ($output -match '(?i)tools.*(not running|not installed)|guest operations agent') {
            'VMware Tools is not ready. Check its installation and restart the guest if needed.'
        } elseif ($output -match '(?i)not supported') {
            'VMware reports that this operation is not supported for the VM.'
        } elseif ($output -match '(?i)incorrect password|password.*invalid') {
            'VM encryption password was not accepted.'
        } elseif ($output -match '(?i)program could not run') {
            'VMware authenticated but could not launch the guest program. Sign into the guest desktop before retrying.'
        } elseif ($output -match '(?i)permission|access.*denied') {
            'Guest Windows denied permission for this operation.'
        } elseif ($output -match '(?i)unrecognized command|unknown command|invalid command|invalid parameters|usage:') {
            'VMware rejected the command syntax. The host helper needs correction; this is not evidence of a bad Windows password.'
        } else {
            # Return only a redacted native error line, never usage/arguments or process-list contents.
            $safeLine = @($output -split '[\r\n]+' | Where-Object { $_ -match '^Error:' } | Select-Object -First 1)
            $safeText = if ($safeLine.Count) { [string]$safeLine[0] } else { 'No recognized error text.' }
            for ($i = 0; $i -lt $Arguments.Count - 1; $i++) {
                if ($Arguments[$i] -in @('-vp', '-gp', '-gu') -and $Arguments[$i + 1].Length -gt 0) {
                    $safeText = $safeText.Replace($Arguments[$i + 1], '[redacted]')
                }
            }
            if ($safeText.Length -gt 300) { $safeText = $safeText.Substring(0, 300) }
            "VMware operation failed (exit code $($process.ExitCode)). $safeText"
        }
        return @{ Success = $process.ExitCode -eq 0; Reason = $reason; Output = $output }
    } finally {
        $info.ArgumentList.Clear()
        $process.Dispose()
    }
}
