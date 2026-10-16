@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 正在发布网页版并推到 N100（容器 fankou-web）……
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\deploy-web.ps1"

if errorlevel 1 (
    echo.
    echo 部署失败，看看上面的报错。
    pause
    exit /b 1
)

echo.
echo 完成。线上地址：https://fankou.fushiyimeng.cn/
echo （只有 IPv6 能访问；手机 4G/5G 流量试最准）
echo.
pause
