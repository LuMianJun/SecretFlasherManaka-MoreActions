param(
    [Parameter(Mandatory=$true)][string]$GameDir,
    [string]$OutputDir = (Join-Path $PSScriptRoot 'releases/SFM-MoreActions-v1.6.1')
)
$ErrorActionPreference = 'Stop'
$gameRoot = (Resolve-Path -LiteralPath $GameDir).Path
$destination = [IO.Path]::GetFullPath($OutputDir)
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath ($destination + '.zip'))) { throw 'Use a new output directory to preserve prior packages.' }
foreach ($project in @('mmd/Plugin/VmdProbe.csproj','npc-spectators/Plugin/SFM.NpcSpectators.csproj')) {
    & dotnet build (Join-Path $PSScriptRoot $project) -c Release "-p:GameDir=$gameRoot"
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $project" }
}
$files = @(
    @{source='more-actions/Plugin/bin/Release/net6.0/SFM.MoreActions.dll';target='BepInEx/plugins/SFMMoreActions/SFM.MoreActions.dll'},
    @{source='mmd/Plugin/bin/Release/net6.0/SFM.VmdProbe.dll';target='BepInEx/plugins/SFMVmdProbe/SFM.VmdProbe.dll'},
    @{source='npc-spectators/Plugin/bin/Release/net6.0/SFM.NpcSpectators.dll';target='BepInEx/plugins/SFMNpcSpectators/SFM.NpcSpectators.dll'},
    @{source='THIRD_PARTY_NOTICES.md';target='BepInEx/plugins/SFMVmdProbe/THIRD_PARTY_NOTICES.md'},
    @{source='THIRD_PARTY_NOTICES.md';target='BepInEx/plugins/SFMMoreActions/THIRD_PARTY_NOTICES.md'},
    @{source='mmd/README.zh-CN.md';target='BepInEx/plugins/SFMVmdProbe/README.zh-CN.md'},
    @{source='more-actions/README.zh-CN.md';target='BepInEx/plugins/SFMMoreActions/README.zh-CN.md'},
    @{source='npc-spectators/README.md';target='BepInEx/plugins/SFMNpcSpectators/README.zh-CN.md'},
    @{source='mmd/README.en.md';target='BepInEx/plugins/SFMVmdProbe/README.en.md'},
    @{source='more-actions/README.en.md';target='BepInEx/plugins/SFMMoreActions/README.en.md'},
    @{source='npc-spectators/README.en.md';target='BepInEx/plugins/SFMNpcSpectators/README.en.md'}
)
foreach ($folder in @('SFMMoreActions','SFMVmdProbe')) {
    foreach ($name in @('动作事件说明.zh-CN.md','原版音效名称.txt')) {
        $files += @{source="shared/$name";target="BepInEx/plugins/$folder/$name"}
    }
}
foreach ($file in $files) {
    $target = Join-Path $destination $file.target
    New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($target)) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file.source) -Destination $target
    if ($file.target.EndsWith('/README.zh-CN.md') -or $file.target.EndsWith('/README.en.md')) {
        $content = [IO.File]::ReadAllText($target).Replace('../shared/','').Replace('../more-actions/','../SFMMoreActions/').Replace('(../README.md','(../../../README.md').Replace('(../README.en.md','(../../../README.en.md').Replace('(../mmd/README.zh-CN.md','(../SFMVmdProbe/README.zh-CN.md').Replace('(../mmd/README.en.md','(../SFMVmdProbe/README.en.md').Replace('(../THIRD_PARTY_NOTICES.md)','(THIRD_PARTY_NOTICES.md)')
        if ($file.target -eq 'BepInEx/plugins/SFMNpcSpectators/README.en.md') {
            $content = $content.Replace('(README.md)','(README.zh-CN.md)')
        }
        [IO.File]::WriteAllText($target,$content)
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'README.en.md'),(Join-Path $PSScriptRoot 'install-mods.ps1') -Destination $destination
foreach ($name in @('README.md','README.en.md')) {
    $readmePath = Join-Path $destination $name
    $readme = [IO.File]::ReadAllText($readmePath).Replace('(mmd/README.zh-CN.md','(BepInEx/plugins/SFMVmdProbe/README.zh-CN.md').Replace('(mmd/README.en.md','(BepInEx/plugins/SFMVmdProbe/README.en.md').Replace('(more-actions/README.zh-CN.md','(BepInEx/plugins/SFMMoreActions/README.zh-CN.md').Replace('(more-actions/README.en.md','(BepInEx/plugins/SFMMoreActions/README.en.md').Replace('(npc-spectators/README.md','(BepInEx/plugins/SFMNpcSpectators/README.zh-CN.md').Replace('(npc-spectators/README.en.md','(BepInEx/plugins/SFMNpcSpectators/README.en.md').Replace('(shared/动作事件说明.zh-CN.md)','(BepInEx/plugins/SFMMoreActions/动作事件说明.zh-CN.md)').Replace('(THIRD_PARTY_NOTICES.md)','(BepInEx/plugins/SFMVmdProbe/THIRD_PARTY_NOTICES.md)').Replace('(VALIDATION.md)','(https://github.com/LuMianJun/SecretFlasherManaka-MoreActions/blob/main/VALIDATION.md)')
    [IO.File]::WriteAllText($readmePath,$readme)
}
$files | ForEach-Object {
    $path = Join-Path $destination $_.target
    [pscustomobject]@{file=$_.target;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;bytes=(Get-Item -LiteralPath $path).Length;preserveExisting=[bool]$_.preserveExisting}
} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $destination 'files.sha256.json') -Encoding utf8
Compress-Archive -LiteralPath (Join-Path $destination 'BepInEx'),(Join-Path $destination 'README.md'),(Join-Path $destination 'README.en.md'),(Join-Path $destination 'install-mods.ps1'),(Join-Path $destination 'files.sha256.json') -DestinationPath ($destination + '.zip')
Write-Output "Generated: $destination.zip"
