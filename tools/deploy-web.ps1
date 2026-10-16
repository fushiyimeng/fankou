<#
.SYNOPSIS
    把网页版发布并推到 N100 上（独立容器 fankou-web，8080 端口）。

.DESCRIPTION
    一条命令跑完：publish -> 打包（不带 .br，容器里没装 brotli 模块）-> scp -> 解包 -> 重启容器。
    线上地址：http://fankou.fushiyimeng.cn:8080/

    连接信息默认走 N100连接方法.txt 里那套（私钥 C:/Users/LiuHu/.ssh/astrbot_vm），
    需要时用 -TargetHost / -KeyPath 覆盖。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\deploy-web.ps1
#>

param(
    [string]$TargetHost = "fushiyimeng@192.168.1.8",
    [string]$KeyPath = "$env:USERPROFILE/.ssh/astrbot_vm",
    [string]$RemoteDir = "/opt/go-bot/fankou"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    Write-Host "==> publish (client)" -ForegroundColor Cyan
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
    dotnet publish src/FanKou.Web -c Release -m:1 -o publish/fankou-web
    if ($LASTEXITCODE -ne 0) { throw "publish client failed" }

    Write-Host "==> publish (server)" -ForegroundColor Cyan
    dotnet publish src/FanKou.Server -c Release -r linux-x64 --self-contained true -m:1 `
        -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish/fankou-server
    if ($LASTEXITCODE -ne 0) { throw "publish server failed" }

    Write-Host "==> pack" -ForegroundColor Cyan
    Remove-Item -LiteralPath "publish/fankou-web.tgz" -Force -ErrorAction SilentlyContinue
    tar -czf publish/fankou-web.tgz --exclude=*.br -C publish/fankou-web/wwwroot .
    if ($LASTEXITCODE -ne 0) { throw "tar failed" }

    Write-Host "==> upload to $TargetHost" -ForegroundColor Cyan
    $files = @(
        "publish/fankou-web.tgz",
        "publish/fankou-server/FanKou.Server",
        "tools/deploy/nginx.conf",
        "tools/deploy/docker-compose.yml",
        "tools/deploy/install-server.sh")
    scp -i $KeyPath -o StrictHostKeyChecking=no $files "${TargetHost}:/tmp/"
    if ($LASTEXITCODE -ne 0) { throw "scp failed" }

    Write-Host "==> unpack and restart container" -ForegroundColor Cyan
    $remote = "bash /tmp/install-server.sh $RemoteDir"

    ssh -i $KeyPath -o StrictHostKeyChecking=no $TargetHost $remote
    if ($LASTEXITCODE -ne 0) { throw "remote deploy failed" }

    Write-Host ""
    Write-Host "==> done: https://fankou.fushiyimeng.cn/" -ForegroundColor Green
}
finally {
    Pop-Location
}
