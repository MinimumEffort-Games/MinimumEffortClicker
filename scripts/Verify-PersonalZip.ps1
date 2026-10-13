param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Archive))
try {
    $manifests = @($zip.Entries | Where-Object { $_.FullName -match '(^|/)build-manifest.json$' })
    if ($manifests.Count -ne 1 -or $manifests[0].Length -gt 1MB -or $zip.Entries.Count -gt 2000) { throw 'Unexpected archive structure.' }
    $manifestEntry = $manifests[0]
    $prefix = $manifestEntry.FullName.Substring(0, $manifestEntry.FullName.Length - 'build-manifest.json'.Length)
    $reader = [IO.StreamReader]::new($manifestEntry.Open())
    try { $manifest = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    [void]$names.Add($manifestEntry.FullName)
    foreach ($file in $manifest.files) {
        $relative = $file.path.Replace('\','/')
        if ($relative.StartsWith('/') -or $relative.Contains(':') -or @($relative.Split('/') | Where-Object { $_ -in @('..','.','') }).Count) { throw 'Invalid manifest path.' }
        $name = $prefix + $relative
        if (!$names.Add($name)) { throw 'Duplicate manifest file.' }
        $entries = @($zip.Entries | Where-Object { $_.FullName -ceq $name })
        if ($entries.Count -ne 1) { throw "Missing or duplicate package entry: $relative" }
        $stream = $entries[0].Open(); $hasher = [Security.Cryptography.SHA256]::Create()
        try { $actual = [Convert]::ToHexString($hasher.ComputeHash($stream)) } finally { $stream.Dispose(); $hasher.Dispose() }
        if ($actual -ne $file.sha256) { throw "Hash mismatch: $relative" }
    }
    foreach ($entry in $zip.Entries) {
        if (!$entry.FullName.EndsWith('/') -and !$names.Contains($entry.FullName)) { throw 'Unlisted archive file.' }
    }
    [pscustomobject]@{ Version = $manifest.version; Schema = $manifest.schema; VerifiedFiles = $manifest.files.Count; ArchiveSha256 = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash; Passed = $true } | ConvertTo-Json
} finally { $zip.Dispose() }
