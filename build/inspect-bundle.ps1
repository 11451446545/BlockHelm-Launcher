param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$ExtractTo,
    [string[]]$Files = @('Launcher.Infrastructure.dll', 'BlockHelm_Launcher_x64.runtimeconfig.json')
)
$ErrorActionPreference = 'Stop'
$bytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Executable))
$signature = [Convert]::FromHexString('8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE')
$offset = -1
# Apphost's PE resources (including the original icon) can place the marker
# several megabytes into the executable. Search all bytes, not just the first MiB.
$binaryText = [Text.Encoding]::Latin1.GetString($bytes)
$markerText = [Text.Encoding]::Latin1.GetString($signature)
$searchStart = 8
while ($searchStart -lt $bytes.Length) {
    $index = $binaryText.IndexOf($markerText, $searchStart, [StringComparison]::Ordinal)
    if ($index -lt 0) { break }
    $candidate = [BitConverter]::ToInt64($bytes, $index - 8)
    if ($candidate -gt 0 -and $candidate -lt ($bytes.Length - 12)) {
        $version = [BitConverter]::ToUInt32($bytes, [int]$candidate)
        if ($version -ge 1 -and $version -le 6) { $offset = $candidate; break }
    }
    $searchStart = $index + $signature.Length
}
if ($offset -le 0 -or $offset -ge $bytes.Length) { throw 'No valid .NET single-file bundle header found.' }
$stream = [IO.MemoryStream]::new($bytes, $false)
$reader = [IO.BinaryReader]::new($stream)
try {
    $stream.Position = $offset
    $major = $reader.ReadUInt32()
    $minor = $reader.ReadUInt32()
    $count = $reader.ReadInt32()
    $bundleId = $reader.ReadString()
    if ($major -ge 2) { $stream.Position += 40 }
    $entries = for ($entryIndex = 0; $entryIndex -lt $count; $entryIndex++) {
        $position = $reader.ReadInt64()
        $size = $reader.ReadInt64()
        $compressedSize = if ($major -ge 6) { $reader.ReadInt64() } else { 0 }
        $type = $reader.ReadByte()
        $path = $reader.ReadString()
        [pscustomobject]@{Path=$path;Offset=$position;Size=$size;CompressedSize=$compressedSize;Type=$type}
    }
    if ($ExtractTo) {
        $destinationRoot = [IO.Path]::GetFullPath($ExtractTo)
        [IO.Directory]::CreateDirectory($destinationRoot) | Out-Null
        foreach ($entry in $entries | Where-Object Path -In $Files) {
            $target = [IO.Path]::GetFullPath((Join-Path $destinationRoot $entry.Path))
            if (-not $target.StartsWith($destinationRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe bundle entry path.' }
            if ($entry.Offset -lt 0 -or $entry.Size -lt 0 -or $entry.Size -gt 1073741824) { throw 'Invalid bundle entry.' }
            [IO.Directory]::CreateDirectory((Split-Path -Parent $target)) | Out-Null
            $stream.Position = $entry.Offset
            $length = if ($entry.CompressedSize -gt 0) { $entry.CompressedSize } else { $entry.Size }
            $payload = $reader.ReadBytes([int]$length)
            if ($payload.Length -ne $length) { throw 'Truncated bundle entry.' }
            if ($entry.CompressedSize -gt 0) {
                $compressed = [IO.MemoryStream]::new($payload, $false)
                $inflater = [IO.Compression.DeflateStream]::new($compressed, [IO.Compression.CompressionMode]::Decompress)
                $output = [IO.MemoryStream]::new()
                try { $inflater.CopyTo($output); $payload = $output.ToArray() }
                finally { $output.Dispose(); $inflater.Dispose(); $compressed.Dispose() }
            }
            if ($payload.Length -ne $entry.Size) { throw 'Bundle size validation failed.' }
            [IO.File]::WriteAllBytes($target, $payload)
        }
    }
    $entries | Select-Object Path,Size,CompressedSize
}
finally { $reader.Dispose(); $stream.Dispose() }
