param([string]$Scale='1')
$ErrorActionPreference='Stop'
$previewRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$previewCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$previewExe=Join-Path $previewRoot 'logs\app-preview.exe'
& $previewCompiler /nologo /utf8output /codepage:65001 /target:exe /main:UiPreview ('/out:'+$previewExe) /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.Core.dll ('/win32manifest:'+(Join-Path $PSScriptRoot 'app.manifest')) ('/win32icon:'+(Join-Path $PSScriptRoot 'app.ico')) ('/resource:'+(Join-Path $PSScriptRoot 'app-icon.png')+',A350Isis.Logo') (Join-Path $PSScriptRoot 'A350IsisApp.cs') (Join-Path $PSScriptRoot 'UiPreview.cs')
if($LASTEXITCODE -ne 0){throw '预览编译失败'}
& $previewExe $previewRoot (Join-Path $previewRoot ('assets\desktop-app-preview-'+$Scale+'.png')) $Scale
if($LASTEXITCODE -ne 0){throw '布局检查失败'}
