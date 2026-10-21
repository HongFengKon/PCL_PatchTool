@echo off
chcp 65001 >nul
setlocal

set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :nocsc

if not exist "%ROOT%dist" mkdir "%ROOT%dist"

echo 编译器: %CSC%
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32manifest:"%ROOT%app.manifest" /out:"%ROOT%dist\PCL下载修复工具.exe" /r:"%ROOT%lib\Mono.Cecil.dll" /resource:"%ROOT%lib\Mono.Cecil.dll",Mono.Cecil.dll "%ROOT%src\Program.cs"
if errorlevel 1 goto :buildfail

echo.
echo [成功] 产物: %ROOT%dist\PCL下载修复工具.exe
goto :eof

:nocsc
echo [错误] 找不到 .NET Framework 编译器 csc.exe
echo        请安装 .NET Framework 4.x，或用 Visual Studio 打开 PCLDownloadFixer.csproj 构建。
exit /b 1

:buildfail
echo [错误] 构建失败
exit /b 1
