param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$OutputDir = (Join-Path $PSScriptRoot 'releases/cancel-policy-0.7.2')
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$destination = [IO.Path]::GetFullPath($OutputDir)
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath ($destination + '.zip'))) { throw 'Use a new output directory to preserve prior packages.' }
foreach ($project in @('vmd-validation/Plugin/VmdProbe.csproj','npc-spectators/Plugin/Seleka2.NpcSpectators.csproj')) {
    & dotnet build (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$gameRoot"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $project" }
}
$files = @(
    @{source='more-actions/Plugin/bin/Release/net6.0/Seleka2.MoreActions.dll';target='BepInEx/plugins/Seleka2MoreActions/Seleka2.MoreActions.dll'},
    @{source='vmd-validation/Plugin/bin/Release/net6.0/Seleka2.VmdProbe.dll';target='BepInEx/plugins/Seleka2VmdProbe/Seleka2.VmdProbe.dll'},
    @{source='npc-spectators/Plugin/bin/Release/net6.0/Seleka2.NpcSpectators.dll';target='BepInEx/plugins/Seleka2NpcSpectators/Seleka2.NpcSpectators.dll'},
    @{source='THIRD_PARTY_NOTICES.md';target='BepInEx/plugins/Seleka2VmdProbe/THIRD_PARTY_NOTICES.md'},
    @{source='vmd-validation/README.zh-CN.md';target='BepInEx/plugins/Seleka2VmdProbe/README.zh-CN.md'},
    @{source='more-actions/README.zh-CN.md';target='BepInEx/plugins/Seleka2MoreActions/README.zh-CN.md'},
    @{source='npc-spectators/README.md';target='BepInEx/plugins/Seleka2NpcSpectators/README.zh-CN.md'}
)
foreach ($folder in @('Seleka2MoreActions','Seleka2VmdProbe')) {
    foreach ($name in @('动作事件说明.zh-CN.md','原版音效名称.txt')) {
        $files += @{source="shared/$name";target="BepInEx/plugins/$folder/$name"}
    }
}
foreach ($file in $files) {
    $target = Join-Path $destination $file.target
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file.source) -Destination $target
    if ($file.target.EndsWith('/README.zh-CN.md')) {
        $content = [IO.File]::ReadAllText($target).Replace('../shared/','')
        [IO.File]::WriteAllText($target,$content)
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'install-mods.ps1') -Destination $destination
$readmePath = Join-Path $destination 'README.md'
$readme = [IO.File]::ReadAllText($readmePath).Replace('(vmd-validation/README.zh-CN.md)','(BepInEx/plugins/Seleka2VmdProbe/README.zh-CN.md)').Replace('(more-actions/README.zh-CN.md)','(BepInEx/plugins/Seleka2MoreActions/README.zh-CN.md)').Replace('(npc-spectators/README.md)','(BepInEx/plugins/Seleka2NpcSpectators/README.zh-CN.md)').Replace('(shared/动作事件说明.zh-CN.md)','(BepInEx/plugins/Seleka2MoreActions/动作事件说明.zh-CN.md)').Replace('(THIRD_PARTY_NOTICES.md)','(BepInEx/plugins/Seleka2VmdProbe/THIRD_PARTY_NOTICES.md)')
[IO.File]::WriteAllText($readmePath,$readme)
$files | ForEach-Object {
    $path = Join-Path $destination $_.target
    [pscustomobject]@{file=$_.target;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;bytes=(Get-Item -LiteralPath $path).Length;preserveExisting=[bool]$_.preserveExisting}
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $destination 'files.sha256.json') -Encoding utf8
Compress-Archive -LiteralPath (Join-Path $destination 'BepInEx'),(Join-Path $destination 'README.md'),(Join-Path $destination 'install-mods.ps1'),(Join-Path $destination 'files.sha256.json') -DestinationPath ($destination + '.zip')
Write-Output "Generated: $destination.zip"
