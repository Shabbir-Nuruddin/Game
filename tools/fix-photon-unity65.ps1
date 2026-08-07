param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot ".."))
)

$ErrorActionPreference = "Stop"

$photonHandler = Join-Path $ProjectRoot "Assets\Photon\PhotonUnityNetworking\Code\PhotonHandler.cs"
if (-not (Test-Path -LiteralPath $photonHandler)) {
    throw "PhotonHandler.cs was not found. Import Photon PUN 2 first, then run this script again."
}

$source = [System.IO.File]::ReadAllText($photonHandler)
$original = $source

# Unity 6.5 makes GetInstanceID() a compiler error. Photon only uses the IDs here
# to determine whether this object is its singleton, so reference equality is the
# correct replacement and avoids converting Unity's new 64-bit EntityId type.
$source = [regex]::Replace(
    $source,
    'this\.GetInstanceID\(\)\s*==\s*instance\.GetInstanceID\(\)',
    'ReferenceEquals(this, instance)'
)

# The singleton does not depend on instance-ID ordering, so the faster unordered
# lookup is appropriate and removes Unity 6.5's FindFirstObjectByType warning.
$source = $source.Replace(
    'FindFirstObjectByType<PhotonHandler>()',
    'FindAnyObjectByType<PhotonHandler>()'
)

if ($source -eq $original) {
    if ($source.Contains('ReferenceEquals(this, instance)')) {
        Write-Host "PhotonHandler.cs is already compatible with Unity 6.5."
        exit 0
    }

    throw "PhotonHandler.cs did not contain the expected PUN 2 code. Update PUN 2 or patch its singleton check manually."
}

[System.IO.File]::WriteAllText($photonHandler, $source, [System.Text.UTF8Encoding]::new($false))
Write-Host "Patched PhotonHandler.cs for Unity 6.5. Return to Unity and let scripts recompile."

