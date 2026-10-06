@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo 正在打包成单文件 exe……
echo.

dotnet publish src\FanKou.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:PublishTrimmed=false ^
  -p:EnableCompressionInSingleFile=true

if errorlevel 1 (
    echo.
    echo 打包失败，看看上面的报错。
    pause
    exit /b 1
)

echo.
echo 打包完成，exe 在这里：
echo   src\FanKou.App\bin\Release\net10.0-windows\win-x64\publish\FanKou.App.exe
echo.
echo 把那个 exe 拷到任何地方双击就能玩。想换牌面样式，
echo 在它旁边建一个 skins 文件夹就行（详见 skins\说明.txt）。
echo.
pause
