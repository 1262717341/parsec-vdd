# ============================================================
#  Parsec 虚拟显示器（中文版）一键编译 + 打包脚本
#
#  用法：
#     powershell -ExecutionPolicy Bypass -File .\build.ps1
#
#  它做四件事：
#     1. 把 version.txt 里的版本号写进 App.xaml.cs
#     2. 用 .NET SDK 编译（net48，WPF）
#     3. 用 Inno Setup 打成中文安装包，直接输出到桌面
#     4. 打包成功后自动把版本号 +1，下次编译就是新版本
# ============================================================

$ErrorActionPreference = 'Stop'

$root      = $PSScriptRoot
$proj      = Join-Path $root 'app\ParsecVDisplay.csproj'
$svcProj   = Join-Path $root 'svc\ParsecVDisplaySvc\ParsecVDisplaySvc.csproj'
$appCs     = Join-Path $root 'app\App.xaml.cs'
$iss       = Join-Path $root 'setup\setup.iss'
$versionTxt= Join-Path $root 'version.txt'
$dotnet    = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$iscc      = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$desktop   = [Environment]::GetFolderPath('Desktop')

if (-not (Test-Path -LiteralPath $dotnet)) { throw "找不到 .NET SDK：$dotnet" }
if (-not (Test-Path -LiteralPath $iscc))   { throw "找不到 Inno Setup 编译器：$iscc" }

function Get-Version {
    if (-not (Test-Path -LiteralPath $versionTxt)) { return '0.46.0' }
    return (Get-Content -LiteralPath $versionTxt -Raw).Trim()
}

function Set-Version([string]$v) {
    [IO.File]::WriteAllText($versionTxt, $v + "`r`n", (New-Object Text.UTF8Encoding $false))
}

function Bump-Version([string]$v) {
    $parts = $v.Split('.')
    while ($parts.Count -lt 3) { $parts += '0' }
    $patch = [int]$parts[2] + 1
    return "$($parts[0]).$($parts[1]).$patch"
}

$version = Get-Version
Write-Host "==> 本次版本号：$version" -ForegroundColor Cyan

# --- 0. 关掉正在运行的旧版本，避免 bin 下的 exe 被占用 -----------------
foreach ($name in @('ParsecVDisplay')) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue

    if ($procs) {
        Write-Host "==> 结束正在运行的 $name 进程（避免文件占用）"
        $procs | ForEach-Object { try { $_.Kill() } catch { } }
        Start-Sleep -Milliseconds 800
    }
}

# --- 1. 同步版本号到源代码 -----------------------------------
$content = Get-Content -LiteralPath $appCs -Raw
$content = [regex]::Replace($content, 'public const string VERSION = "[^"]*";',
                            "public const string VERSION = `"$version`";")
[IO.File]::WriteAllText($appCs, $content, (New-Object Text.UTF8Encoding $false))
Write-Host "==> 已写入 App.VERSION = $version"

# --- 2. 编译 -------------------------------------------------
Write-Host "==> 正在编译 …" -ForegroundColor Cyan
& $dotnet build $proj -c Release -o (Join-Path $root 'bin') --nologo
if ($LASTEXITCODE -ne 0) { throw "编译失败，已中止打包。" }

# 常驻服务（开机即保持虚拟显示器）
& $dotnet build $svcProj -c Release -o (Join-Path $root 'bin') --nologo
if ($LASTEXITCODE -ne 0) { throw "服务编译失败，已中止打包。" }

$exe = Join-Path $root 'bin\ParsecVDisplay.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "编译产物不存在：$exe" }

$svcExe = Join-Path $root 'bin\ParsecVDisplaySvc.exe'
if (-not (Test-Path -LiteralPath $svcExe)) { throw "服务产物不存在：$svcExe" }

Write-Host ("==> 编译完成：{0}（{1:N0} 字节）" -f $exe, (Get-Item $exe).Length)
Write-Host ("==> 服务产物：{0}（{1:N0} 字节）" -f $svcExe, (Get-Item $svcExe).Length)

# --- 3. 打安装包到桌面 ---------------------------------------
Write-Host "==> 正在打包安装程序 …" -ForegroundColor Cyan
& $iscc "/DMyAppVersion=$version" "/DOutDir=$desktop" $iss
if ($LASTEXITCODE -ne 0) { throw "打包失败（Inno Setup 返回 $LASTEXITCODE）。" }

$setup = Join-Path $desktop "ParsecVDisplay-v$version-setup.exe"
if (-not (Test-Path -LiteralPath $setup)) { throw "安装包未生成：$setup" }

# --- 4. 版本号 +1，供下次使用 --------------------------------
$next = Bump-Version $version
Set-Version $next

Write-Host ""
Write-Host "==================== 完成 ====================" -ForegroundColor Green
Write-Host "安装包：$setup"
Write-Host ("大小  ：{0:N0} 字节" -f (Get-Item $setup).Length)
Write-Host "下次编译版本号将自动变为：$next"
