param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/plugins'))
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'))) { throw 'BepInEx IL2CPP installation not found.' }
$manifest = @(Get-Content -LiteralPath (Join-Path $packageRoot 'files.sha256.json') -Raw | ConvertFrom-Json)
$required = @('BepInEx/plugins/Seleka2MoreActions/Seleka2.MoreActions.dll','BepInEx/plugins/Seleka2VmdProbe/Seleka2.VmdProbe.dll','BepInEx/plugins/Seleka2NpcSpectators/Seleka2.NpcSpectators.dll')
$allowed = $required + @('BepInEx/plugins/Seleka2VmdProbe/README.zh-CN.md')
$allowed += 'BepInEx/plugins/Seleka2VmdProbe/THIRD_PARTY_NOTICES.md'
foreach ($folder in @('Seleka2MoreActions','Seleka2VmdProbe')) {
    foreach ($name in @('动作事件说明.zh-CN.md','原版音效名称.txt')) { $allowed += "BepInEx/plugins/$folder/$name" }
}
$allowed += @('BepInEx/plugins/Seleka2MoreActions/README.zh-CN.md','BepInEx/plugins/Seleka2NpcSpectators/README.zh-CN.md')
if (@($manifest.file | Sort-Object -Unique).Count -ne $manifest.Count) { throw 'Duplicate package target.' }
foreach ($name in $required) { if ($name -notin $manifest.file) { throw "Missing plugin: $name" } }
$backupRoot = Join-Path $gameRoot ('BepInEx/unified-vmd-backups/' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
$items = @()
foreach ($entry in $manifest) {
    if ($entry.file -notin $allowed) { throw 'Unexpected package target.' }
    $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.file))
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $entry.file))
    if (-not $source.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        -not $target.StartsWith($pluginsRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package path outside expected roots.' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Package hash mismatch: $($entry.file)" }
    $exists = Test-Path -LiteralPath $target
    if ($entry.preserveExisting -and $exists) { Write-Output "Preserved existing: $($entry.file)"; continue }
    $items += [pscustomobject]@{source=$source;target=$target;exists=$exists;backup=(Join-Path $backupRoot $entry.file)}
}
foreach ($item in $items) {
    if ($item.exists) {
        # Check all targets first. Never terminate the user's game.
        $handle = [IO.File]::Open($item.target,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        $handle.Dispose()
    }
}
foreach ($item in $items) {
    if ($item.exists) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($item.backup)) -Force | Out-Null
        Copy-Item -LiteralPath $item.target -Destination $item.backup
        if ((Get-FileHash -LiteralPath $item.target).Hash -ne (Get-FileHash -LiteralPath $item.backup).Hash) { throw 'Backup verification failed.' }
    }
}
try {
    foreach ($item in $items) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($item.target)) -Force | Out-Null
        Copy-Item -LiteralPath $item.source -Destination $item.target -Force
        if ((Get-FileHash -LiteralPath $item.source).Hash -ne (Get-FileHash -LiteralPath $item.target).Hash) { throw 'Installed file verification failed.' }
    }
} catch {
    $failure = $_
    foreach ($item in $items) {
        if ($item.exists) { Copy-Item -LiteralPath $item.backup -Destination $item.target -Force }
        elseif (Test-Path -LiteralPath $item.target) { Remove-Item -LiteralPath $item.target }
    }
    throw $failure
}
Write-Output "Installed More Actions 0.5.3, MMD 0.9.9, NPC Spectators 0.2.0. Backup: $backupRoot"
