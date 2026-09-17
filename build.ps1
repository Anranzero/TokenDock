# OpenCode Go 余量助手 构建脚本
# 用法：
#   .\build.ps1              # 构建 + 发布自包含单文件 EXE 到 dist\
#   .\build.ps1 -RunTests    # 额外运行单元测试
param(
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $RootDir 'src\TokenDock\TokenDock.csproj'
$TestProject = Join-Path $RootDir 'tests\TokenDock.Tests\TokenDock.Tests.csproj'
$DistDir = Join-Path $RootDir 'dist'

Write-Host '== 1/3 还原依赖 =='
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore 失败' }

if ($RunTests) {
    Write-Host '== 2/3 运行单元测试 =='
    dotnet test $TestProject
    if ($LASTEXITCODE -ne 0) { throw '单元测试失败' }
}
else {
    Write-Host '== 2/3 跳过单元测试（如需运行请加 -RunTests）=='
}

Write-Host '== 3/3 发布自包含单文件 =='
dotnet publish $Project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $DistDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish 失败' }

Write-Host ''
Write-Host "构建完成：$DistDir\TokenDock.exe"
Write-Host '该 EXE 为 .NET 自包含单文件，可直接复制到其他 Windows 10/11 x64 机器双击运行，无需安装运行时。'
