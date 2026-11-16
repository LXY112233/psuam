param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'), [switch]$RunUiTests, [switch]$RunVisualTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.x 编译器。' }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$wpf = Join-Path (Split-Path $compiler) 'WPF'
$refs = @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll','WindowsFormsIntegration.dll') | ForEach-Object { '/reference:'+(Join-Path $wpf $_) }
$refs += '/reference:System.Xaml.dll'
$refs += '/reference:System.Net.Http.dll','/reference:System.IO.Compression.dll','/reference:System.IO.Compression.FileSystem.dll'
$frameworkTarget = Join-Path $PSScriptRoot 'FrameworkTarget.cs'
$refs += "/win32manifest:$(Join-Path $PSScriptRoot 'app.manifest')"
function Copy-RuntimeConfig([string]$target) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MoveReminder.exe.config') -Destination ($target+'.config') -Force }
$sources = 'Core.cs','Theme.cs','NavigationButton.cs','Typography.cs','CrispTextBox.cs','AppleControls.cs','Activity.cs','ActivityDashboard.cs','UpdateSchedule.cs','UpdateService.cs','UpdateInstaller.cs','UpdateForm.cs','ToastForm.cs','SettingsForm.cs','Program.cs','ReminderApp.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$exe = Join-Path $OutputDirectory '走动提醒.exe'
& $compiler @refs /nologo /target:winexe /platform:anycpu /optimize+ /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32icon:$(Join-Path $PSScriptRoot 'app.ico')" "/resource:$(Join-Path $PSScriptRoot 'app.ico'),app.ico" "/out:$exe" $sources $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译应用失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'MoveReminder.exe.config') -Destination (Join-Path $OutputDirectory '走动提醒.exe.config') -Force
$testExe = Join-Path $OutputDirectory 'CoreTests.exe'
& $compiler @refs /nologo /utf8output /reference:System.Web.Extensions.dll "/out:$testExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'Tests.cs') $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译测试失败。' }
Copy-RuntimeConfig $testExe
& $testExe
if ($LASTEXITCODE -ne 0) { throw '核心测试失败。' }
$regressionExe = Join-Path $OutputDirectory 'RegressionTests.exe'
& $compiler @refs /nologo /utf8output /reference:System.Web.Extensions.dll "/out:$regressionExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'RegressionTests.cs') $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译问题复现测试失败。' }
Copy-RuntimeConfig $regressionExe
& $regressionExe
if ($LASTEXITCODE -ne 0) { throw '问题复现测试未通过。' }
$activityExe = Join-Path $OutputDirectory 'ActivityTests.exe'
& $compiler @refs /nologo /utf8output /reference:System.Web.Extensions.dll "/out:$activityExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'Activity.cs') (Join-Path $PSScriptRoot 'ActivityTests.cs') $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译打卡记录测试失败。' }
Copy-RuntimeConfig $activityExe
& $activityExe
if ($LASTEXITCODE -ne 0) { throw '打卡记录测试未通过。' }
$scheduleExe = Join-Path $OutputDirectory 'UpdateScheduleTests.exe'
& $compiler @refs /nologo /utf8output /reference:System.Web.Extensions.dll "/out:$scheduleExe" (Join-Path $PSScriptRoot 'Core.cs') (Join-Path $PSScriptRoot 'UpdateSchedule.cs') (Join-Path $PSScriptRoot 'UpdateScheduleTests.cs') $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译更新调度测试失败。' }
Copy-RuntimeConfig $scheduleExe
& $scheduleExe
if ($LASTEXITCODE -ne 0) { throw '更新调度测试未通过。' }
$updateExe = Join-Path $OutputDirectory 'UpdateTests.exe'
& $compiler @refs /nologo /utf8output /reference:System.Web.Extensions.dll "/out:$updateExe" (Join-Path $PSScriptRoot 'UpdateService.cs') (Join-Path $PSScriptRoot 'UpdateInstaller.cs') (Join-Path $PSScriptRoot 'UpdateTests.cs') $frameworkTarget
if ($LASTEXITCODE -ne 0) { throw '编译更新安装测试失败。' }
Copy-RuntimeConfig $updateExe
& $updateExe
if ($LASTEXITCODE -ne 0) { throw '更新安装测试未通过。' }
if ($RunUiTests) {
    $uiExe = Join-Path $OutputDirectory 'UiTests.exe'
    & $compiler @refs /nologo /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$exe" "/out:$uiExe" (Join-Path $PSScriptRoot 'UiTests.cs') $frameworkTarget
    if ($LASTEXITCODE -ne 0) { throw '编译界面测试失败。' }
    Copy-RuntimeConfig $uiExe
    & $uiExe
    if ($LASTEXITCODE -ne 0) { throw '界面测试失败。' }
    $interactionExe = Join-Path $OutputDirectory 'InteractionTests.exe'
    & $compiler @refs /nologo /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$exe" "/out:$interactionExe" (Join-Path $PSScriptRoot 'InteractionTests.cs') $frameworkTarget
    if ($LASTEXITCODE -ne 0) { throw '编译交互测试失败。' }
    Copy-RuntimeConfig $interactionExe
    & $interactionExe
    if ($LASTEXITCODE -ne 0) { throw '交互测试失败。' }
    $activityUiExe = Join-Path $OutputDirectory 'ActivityIntegrationTests.exe'
    & $compiler @refs /nologo /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$exe" "/out:$activityUiExe" (Join-Path $PSScriptRoot 'ActivityIntegrationTests.cs') $frameworkTarget
    if ($LASTEXITCODE -ne 0) { throw '编译打卡集成测试失败。' }
    Copy-RuntimeConfig $activityUiExe
    & $activityUiExe (Join-Path $OutputDirectory 'activity-previews')
    if ($LASTEXITCODE -ne 0) { throw '打卡集成测试失败。' }
}
Write-Output "已生成：$exe"

if ($RunVisualTests) {
    $visualExe = Join-Path $OutputDirectory 'VisualTests.exe'
    & $compiler @refs /nologo /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$exe" "/out:$visualExe" (Join-Path $PSScriptRoot 'VisualTests.cs') $frameworkTarget
    if ($LASTEXITCODE -ne 0) { throw '编译视觉检查失败。' }
    Copy-RuntimeConfig $visualExe
    & $visualExe (Join-Path $OutputDirectory 'previews')
    if ($LASTEXITCODE -ne 0) { throw '视觉或控件检查失败。' }
    $typographyExe = Join-Path $OutputDirectory 'TypographyTests.exe'
    & $compiler @refs /nologo /utf8output /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$exe" "/out:$typographyExe" (Join-Path $PSScriptRoot 'TypographyTests.cs') $frameworkTarget
    if ($LASTEXITCODE -ne 0) { throw '编译字体检查失败。' }
    Copy-RuntimeConfig $typographyExe
    & $typographyExe (Join-Path $OutputDirectory 'previews')
    if ($LASTEXITCODE -ne 0) { throw '字体检查失败。' }
}

