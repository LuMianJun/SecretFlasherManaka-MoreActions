param(
    [Parameter(Mandatory=$true)][string]$PackageDir,
    [string]$FixtureRoot = (Join-Path ([IO.Path]::GetTempPath()) ('sfm-installer-' + [Guid]::NewGuid().ToString('N')))
)
$ErrorActionPreference='Stop'
$batch=[IO.Path]::GetFullPath($FixtureRoot)
$package=(Resolve-Path -LiteralPath $PackageDir).Path
$installer=Join-Path $package 'install-mods.ps1'
$modules=@(
 @{old='Seleka2MoreActions';new='SFMMoreActions';dll='Seleka2.MoreActions.dll';oldCfg='local.seleka2.moreactions.cfg';newCfg='local.sfm.moreactions.cfg'},
 @{old='Seleka2VmdProbe';new='SFMVmdProbe';dll='Seleka2.VmdProbe.dll';oldCfg='local.seleka2.vmdprobe.cfg';newCfg='local.sfm.vmdprobe.cfg'},
 @{old='Seleka2NpcSpectators';new='SFMNpcSpectators';dll='Seleka2.NpcSpectators.dll';oldCfg='local.seleka2.npcspectators.cfg';newCfg='local.sfm.npcspectators.cfg'}
)
function New-Fixture([string]$name,[bool]$legacy=$false) {
 $root=Join-Path $batch ('fixtures/'+$name)
 if(Test-Path -LiteralPath $root){throw 'Use fresh fixture paths.'}
 New-Item -ItemType Directory -Path (Join-Path $root 'BepInEx/core'),(Join-Path $root 'BepInEx/config') -Force|Out-Null
 [IO.File]::WriteAllText((Join-Path $root 'BepInEx/core/BepInEx.Unity.IL2CPP.dll'),'Installer boundary marker; never loaded')
 if($legacy){
  foreach($module in $modules){
   $dir=Join-Path $root ('BepInEx/plugins/'+$module.old)
   New-Item -ItemType Directory -Path $dir -Force|Out-Null
   [IO.File]::WriteAllText((Join-Path $dir $module.dll),'legacy binary fixture')
   [IO.File]::WriteAllText((Join-Path $root ('BepInEx/config/'+$module.oldCfg)),'[User]'+[Environment]::NewLine+'Value = '+$module.oldCfg)
  }
  $dir=Join-Path $root 'BepInEx/plugins/Seleka2MoreActions/Actions/UserPack'
  New-Item -ItemType Directory -Path $dir -Force|Out-Null
  [IO.File]::WriteAllText((Join-Path $dir 'pose'),'user-owned action')
  $dir=Join-Path $root 'BepInEx/plugins/Seleka2VmdProbe/Motions/UserMotion'
  New-Item -ItemType Directory -Path $dir -Force|Out-Null
  foreach($name in @('motion.vmd','BGM.wav','action.json')){[IO.File]::WriteAllText((Join-Path $dir $name),'user-owned '+$name)}
 }
 return $root
}
function Assert-Package([string]$root) {
 foreach($entry in @(Get-Content -LiteralPath (Join-Path $package 'files.sha256.json') -Raw -Encoding UTF8|ConvertFrom-Json|ForEach-Object{$_})){
  if((Get-FileHash -LiteralPath (Join-Path $root $entry.file)).Hash -ne $entry.sha256){throw 'Installed file mismatch'}
 }
}
function Assert-Legacy([string]$root) {
 foreach($module in $modules){
  if(Test-Path -LiteralPath (Join-Path $root ('BepInEx/plugins/'+$module.old+'/'+$module.dll))){throw 'Legacy DLL still discoverable'}
  $old=Join-Path $root ('BepInEx/config/'+$module.oldCfg)
  $new=Join-Path $root ('BepInEx/config/'+$module.newCfg)
  if((Get-FileHash -LiteralPath $old).Hash -ne (Get-FileHash -LiteralPath $new).Hash){throw 'Configuration migration mismatch'}
 }
 foreach($relative in @('SFMMoreActions/Actions/UserPack/pose','SFMVmdProbe/Motions/UserMotion/motion.vmd','SFMVmdProbe/Motions/UserMotion/BGM.wav','SFMVmdProbe/Motions/UserMotion/action.json')){
  if(-not(Test-Path -LiteralPath (Join-Path $root ('BepInEx/plugins/'+$relative)))){throw 'User asset missing after migration'}
 }
}
$fresh=New-Fixture 'fresh'
& $installer -GameDir $fresh
Assert-Package $fresh
& $installer -GameDir $fresh
Assert-Package $fresh
Write-Output 'PASS fresh install and repeated upgrade hashes'
$old=New-Fixture 'legacy' $true
& $installer -GameDir $old
Assert-Package $old
Assert-Legacy $old
& $installer -GameDir $old
Assert-Legacy $old
Write-Output 'PASS legacy DLL retirement, action/music/JSON/config migration and idempotency'
$collision=New-Fixture 'collision' $true
New-Item -ItemType Directory -Path (Join-Path $collision 'BepInEx/plugins/SFMMoreActions/Actions/UserPack') -Force|Out-Null
$newAction=Join-Path $collision 'BepInEx/plugins/SFMMoreActions/Actions/UserPack/pose'
$newConfig=Join-Path $collision 'BepInEx/config/local.sfm.moreactions.cfg'
[IO.File]::WriteAllText($newAction,'newer user action wins')
[IO.File]::WriteAllText($newConfig,'newer user configuration wins')
$actionHash=(Get-FileHash -LiteralPath $newAction).Hash
$configHash=(Get-FileHash -LiteralPath $newConfig).Hash
& $installer -GameDir $collision
if((Get-FileHash -LiteralPath $newAction).Hash -ne $actionHash -or (Get-FileHash -LiteralPath $newConfig).Hash -ne $configHash){throw 'SFM user data was overwritten'}
Write-Output 'PASS newer SFM data wins without deleting legacy data'
$locked=New-Fixture 'locked' $true
$lockedDll=Join-Path $locked 'BepInEx/plugins/Seleka2MoreActions/Seleka2.MoreActions.dll'
$handle=[IO.File]::Open($lockedDll,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
$rejected=$false
try{try{& $installer -GameDir $locked}catch{$rejected=$true}}finally{$handle.Dispose()}
if(-not $rejected -or -not(Test-Path -LiteralPath $lockedDll) -or (Test-Path -LiteralPath (Join-Path $locked 'BepInEx/config/local.sfm.moreactions.cfg'))){throw 'Locked DLL preflight mutated user data'}
Write-Output 'PASS locked legacy DLL rejected before migration'
$rollback=New-Fixture 'rollback' $true
$global:SfmFixtureFailCopy=$true
$global:SfmFixtureFailTarget=Join-Path $rollback 'BepInEx/plugins/SFMMoreActions/SFM.MoreActions.dll'
function Copy-Item {
 [CmdletBinding()]param([string]$LiteralPath,[string]$Destination,[switch]$Force)
 if($global:SfmFixtureFailCopy -and $Destination -eq $global:SfmFixtureFailTarget){$global:SfmFixtureFailCopy=$false;throw 'Injected installation failure'}
 Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
$rejected=$false
try{& $installer -GameDir $rollback}catch{$rejected=$true}
if(-not $rejected){throw 'Failure injection did not run'}
foreach($module in $modules){
 if(-not(Test-Path -LiteralPath (Join-Path $rollback ('BepInEx/plugins/'+$module.old+'/'+$module.dll)))){throw 'Rollback failed to restore old DLL'}
 if(Test-Path -LiteralPath (Join-Path $rollback ('BepInEx/config/'+$module.newCfg))){throw 'Rollback left copied configuration'}
}
if(Test-Path -LiteralPath (Join-Path $rollback 'BepInEx/plugins/SFMMoreActions/Actions/UserPack/pose')){throw 'Rollback left migrated copies'}
Write-Output 'PASS mid-install failure restores old binaries and removes only created copies'

Write-Output ("Fixture evidence: " + $batch)
