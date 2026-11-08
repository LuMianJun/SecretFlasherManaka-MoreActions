param([Parameter(Mandatory=$true)][string]$GameDir)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$pluginsRoot = [IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/plugins'))
if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'))) { throw 'BepInEx IL2CPP installation not found.' }
$manifest = @(Get-Content -LiteralPath (Join-Path $packageRoot 'files.sha256.json') -Raw -Encoding UTF8 | ConvertFrom-Json | ForEach-Object { $_ })
$required = @('BepInEx/plugins/SFMMoreActions/SFM.MoreActions.dll','BepInEx/plugins/SFMVmdProbe/SFM.VmdProbe.dll','BepInEx/plugins/SFMNpcSpectators/SFM.NpcSpectators.dll')
$allowed = $required + @('BepInEx/plugins/SFMVmdProbe/README.zh-CN.md')
$allowed += 'BepInEx/plugins/SFMVmdProbe/THIRD_PARTY_NOTICES.md'
$allowed += 'BepInEx/plugins/SFMMoreActions/THIRD_PARTY_NOTICES.md'
foreach ($folder in @('SFMMoreActions','SFMVmdProbe')) {
    foreach ($name in @('动作事件说明.zh-CN.md','原版音效名称.txt')) { $allowed += "BepInEx/plugins/$folder/$name" }
}
$allowed += @('BepInEx/plugins/SFMMoreActions/README.zh-CN.md','BepInEx/plugins/SFMNpcSpectators/README.zh-CN.md')
if (@($manifest.file | Sort-Object -Unique).Count -ne $manifest.Count) { throw 'Duplicate package target.' }
foreach ($name in $required) { if ($name -notin $manifest.file) { throw "Missing plugin: $name" } }
$backupRoot = Join-Path $gameRoot ('BepInEx/unified-vmd-backups/' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss-fff'))
$items = @()
$configRoot = [IO.Path]::GetFullPath((Join-Path $gameRoot 'BepInEx/config'))
function Assert-Within([string]$root,[string]$path) {
    $base = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($base + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Migration path outside expected root.' }
    return $full
}
function Assert-NoLink([string]$path,[string]$root) {
    $current = [IO.Path]::GetFullPath($path)
    $boundary = [IO.Path]::GetFullPath($root)
    while ($current.Length -ge $boundary.Length) {
        if (Test-Path -LiteralPath $current) {
            $node = Get-Item -LiteralPath $current -Force
            if (($node.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing a linked migration/install path.' }
        }
        if ($current -eq $boundary) { break }
        $current = [IO.Path]::GetDirectoryName($current)
        if (-not $current) { break }
    }
}
foreach ($entry in $manifest) {
    if ($entry.file -notin $allowed) { throw 'Unexpected package target.' }
    $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $entry.file))
    $target = [IO.Path]::GetFullPath((Join-Path $gameRoot $entry.file))
    if (-not $source.StartsWith($packageRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        -not $target.StartsWith($pluginsRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package path outside expected roots.' }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $entry.sha256) { throw "Package hash mismatch: $($entry.file)" }
    Assert-NoLink $target $pluginsRoot
    $exists = Test-Path -LiteralPath $target
    if ($entry.preserveExisting -and $exists) { Write-Output "Preserved existing: $($entry.file)"; continue }
    $items += [pscustomobject]@{source=$source;target=$target;exists=$exists;backup=(Join-Path $backupRoot $entry.file)}
}
# Legacy names are only recognized by this migration. Assets and configuration
# are copied without overwriting SFM data; original user data remains available.
$legacy = @(
    @{old='Seleka2MoreActions';new='SFMMoreActions';dll='Seleka2.MoreActions.dll';oldCfg='local.seleka2.moreactions.cfg';newCfg='local.sfm.moreactions.cfg'},
    @{old='Seleka2VmdProbe';new='SFMVmdProbe';dll='Seleka2.VmdProbe.dll';oldCfg='local.seleka2.vmdprobe.cfg';newCfg='local.sfm.vmdprobe.cfg'},
    @{old='Seleka2NpcSpectators';new='SFMNpcSpectators';dll='Seleka2.NpcSpectators.dll';oldCfg='local.seleka2.npcspectators.cfg';newCfg='local.sfm.npcspectators.cfg'}
)
$migrations = @()
$retired = @()
foreach ($module in $legacy) {
    $oldRoot = Assert-Within $pluginsRoot (Join-Path $pluginsRoot $module.old)
    $newRoot = Assert-Within $pluginsRoot (Join-Path $pluginsRoot $module.new)
    Assert-NoLink $oldRoot $pluginsRoot
    Assert-NoLink $newRoot $pluginsRoot
    if (Test-Path -LiteralPath $oldRoot) {
        $pending = New-Object 'System.Collections.Generic.Queue[string]'
        $pending.Enqueue($oldRoot)
        while ($pending.Count -gt 0) {
            $directory = $pending.Dequeue()
            foreach ($node in @(Get-ChildItem -LiteralPath $directory -Force)) {
                if (($node.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing a linked legacy asset.' }
                if ($node.PSIsContainer) { $pending.Enqueue($node.FullName); continue }
                if ($node.Extension -in @('.dll','.exe','.pdb')) { continue }
                $relative = $node.FullName.Substring($oldRoot.Length + 1)
                $target = Assert-Within $newRoot (Join-Path $newRoot $relative)
                Assert-NoLink $target $pluginsRoot
                if (($items.target -contains $target) -or (Test-Path -LiteralPath $target)) { continue }
                $migrations += [pscustomobject]@{source=$node.FullName;target=$target}
            }
        }
    }
    foreach ($dllName in @($module.dll,($module.dll.Replace('Seleka2.','SFM.')))) {
        $oldDll = Assert-Within $oldRoot (Join-Path $oldRoot $dllName)
        if (Test-Path -LiteralPath $oldDll) {
            $backup = Assert-Within $backupRoot (Join-Path $backupRoot ('legacy/' + $module.old + '/' + $dllName))
            $retired += [pscustomobject]@{source=$oldDll;backup=$backup;moved=$false;sha256=(Get-FileHash -LiteralPath $oldDll).Hash}
        }
    }
    $oldConfig = Assert-Within $configRoot (Join-Path $configRoot $module.oldCfg)
    $newConfig = Assert-Within $configRoot (Join-Path $configRoot $module.newCfg)
    Assert-NoLink $oldConfig $configRoot
    Assert-NoLink $newConfig $configRoot
    if ((Test-Path -LiteralPath $oldConfig) -and -not (Test-Path -LiteralPath $newConfig)) {
        $migrations += [pscustomobject]@{source=$oldConfig;target=$newConfig}
    }
}
# Check every replacement before changing files. Never stop a running game.
foreach ($path in @($items | Where-Object {$_.exists} | ForEach-Object {$_.target}) + @($retired | ForEach-Object {$_.source})) {
    $handle = [IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $handle.Dispose()
}
foreach ($item in $items) {
    if ($item.exists) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($item.backup)) -Force | Out-Null
        Copy-Item -LiteralPath $item.target -Destination $item.backup
        if ((Get-FileHash -LiteralPath $item.target).Hash -ne (Get-FileHash -LiteralPath $item.backup).Hash) { throw 'Backup verification failed.' }
    }
}
$created = @()
try {
    foreach ($copy in $migrations) {
        if (Test-Path -LiteralPath $copy.target) { continue }
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($copy.target)) -Force | Out-Null
        $created += $copy.target
        Copy-Item -LiteralPath $copy.source -Destination $copy.target
        if ((Get-FileHash -LiteralPath $copy.source).Hash -ne (Get-FileHash -LiteralPath $copy.target).Hash) { throw 'Migration copy verification failed.' }
    }
    foreach ($item in $retired) {
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($item.backup)) -Force | Out-Null
        Move-Item -LiteralPath $item.source -Destination $item.backup
        $item.moved = $true
        if ((Get-FileHash -LiteralPath $item.backup).Hash -ne $item.sha256) { throw 'Legacy DLL backup mismatch.' }
    }
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
    foreach ($item in $retired) {
        if ($item.moved) { Copy-Item -LiteralPath $item.backup -Destination $item.source -Force }
    }
    foreach ($path in $created) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    throw $failure
}
Write-Output "Installed SFM More Actions 1.4.1, MMD 1.1.0, NPC Spectators 1.0.0. Backup: $backupRoot"
Write-Output ("Migrated user files: " + $created.Count + "; retired legacy DLLs: " + $retired.Count)
