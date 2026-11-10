param([string]$OutputPath)
$ErrorActionPreference='Stop'
$appProjectRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$appSource=Join-Path $PSScriptRoot 'A350IsisApp.cs'
$radarSource=Join-Path $PSScriptRoot 'RadarWindow.cs'
$uiSource=Join-Path $PSScriptRoot 'UiKit.cs'
$appTests=Join-Path $PSScriptRoot 'IdentityTests.cs'
$appManifest=Join-Path $PSScriptRoot 'app.manifest'
$appIcon=Join-Path $PSScriptRoot 'app.ico'
$appOutput=if($OutputPath){$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)}else{Join-Path $appProjectRoot 'A350 ISIS 助手.exe'}
$appTestOutput=Join-Path $appProjectRoot 'logs\app-identity-tests.exe'
New-Item -ItemType Directory -Force (Join-Path $appProjectRoot 'logs') | Out-Null
$appArtOutput=Join-Path $appProjectRoot 'logs\app-icon-builder.exe'
& $appCompiler /nologo /target:exe ('/out:'+$appArtOutput) /r:System.Drawing.dll (Join-Path $PSScriptRoot 'IconArtwork.cs')
if($LASTEXITCODE -ne 0){throw '图标生成器编译失败'}
& $appArtOutput $PSScriptRoot
if($LASTEXITCODE -ne 0){throw '图标生成失败'}
$appReferences=@('/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Management.dll','/r:System.Web.Extensions.dll','/r:System.Core.dll','/r:System.IO.Compression.dll')
& $appCompiler /nologo /utf8output /codepage:65001 /optimize+ /platform:x64 /target:exe /main:IdentityTests ('/out:'+$appTestOutput) @appReferences $appSource $radarSource $uiSource $appTests
if($LASTEXITCODE -ne 0){throw '应用测试程序编译失败'}
& $appTestOutput
if($LASTEXITCODE -ne 0){throw '进程身份测试失败'}
& $appCompiler /nologo /utf8output /codepage:65001 /optimize+ /platform:x64 /target:winexe ('/win32icon:'+$appIcon) ('/resource:'+(Join-Path $PSScriptRoot 'app-icon.png')+',A350Isis.Logo') ('/win32manifest:'+$appManifest) ('/out:'+$appOutput) @appReferences $appSource $radarSource $uiSource
if($LASTEXITCODE -ne 0){throw '应用编译失败'}
Get-Item -LiteralPath $appOutput | Select-Object FullName,Length
