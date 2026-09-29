<#
.SYNOPSIS
    OmniPad Universal One-Click Build Script
    Builds PCServer (.NET 8), WebClient sync, and AndroidClient (APK) with zero manual hassle.
.PARAMETER SkipAndroid
    Skip compiling the Android APK (useful if Android SDK is not installed on the build machine).
.PARAMETER SkipTests
    Skip running automated unit test suite.
.PARAMETER Package
    Compress the finished build into OmniPad-Portable.zip ready for GitHub Releases.
#>

param(
    [ValidateSet("win-x64", "linux-x64", "all")]
    [string]$Runtime = "win-x64",
    [switch]$SkipAndroid,
    [switch]$SkipTests,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "         OmniPad Universal Open-Source Build Pipeline            " -ForegroundColor Cyan
Write-Host "         Target Runtime: $Runtime                                " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$RootDir = $PSScriptRoot
$PCServerDir = Join-Path $RootDir "PCServer"
$AndroidDir = Join-Path $RootDir "AndroidClient"
$WebClientDir = Join-Path $RootDir "WebClient"
$PortableDir = Join-Path $RootDir "OmniPad-Portable"
$LinuxPublishDir = Join-Path (Join-Path $RootDir "publish") "OmniPad-Linux-x64"

# --- 1. SYNC WEB CLIENT ASSETS ---
Write-Host "`n[1/5] Synchronizing WebClient PWA assets..." -ForegroundColor Yellow
$destDirs = @(
    (Join-Path $PortableDir "WebClient"),
    (Join-Path $PortableDir "wwwroot"),
    (Join-Path $AndroidDir "app\src\main\assets\web"),
    (Join-Path (Join-Path $RootDir "iOSClient") "OmniPad\Resources\Web")
)

foreach ($dest in $destDirs) {
    if (-not (Test-Path $dest)) {
        New-Item -ItemType Directory -Path $dest -Force | Out-Null
    }
    Copy-Item -Path "$WebClientDir\*" -Destination "$dest\" -Recurse -Force
}

# Ensure app.ico is present for Updater and Portable distribution
$serverIco = Join-Path $PCServerDir "OmniPadServer.App\app.ico"
if (Test-Path $serverIco) {
    Copy-Item -Path $serverIco -Destination (Join-Path $PCServerDir "OmniPadUpdater.App\app.ico") -Force
    Copy-Item -Path $serverIco -Destination (Join-Path $PortableDir "app.ico") -Force
}
Write-Host "  -> Synced WebClient to Portable & Android assets successfully." -ForegroundColor Green

# --- 2. BUILD PC SERVER (.NET 8) ---
Write-Host "`n[2/5] Building OmniPad PC Server (.NET 8)..." -ForegroundColor Yellow
$appCsproj = Join-Path $PCServerDir "OmniPadServer.App\OmniPadServer.App.csproj"

if ($Runtime -eq "linux-x64") {
    & dotnet build $appCsproj -c Release -f net8.0
} else {
    & dotnet build $appCsproj -c Release
}
if ($LASTEXITCODE -ne 0) {
    throw "PCServer build failed with exit code $LASTEXITCODE"
}
Write-Host "  -> PCServer build succeeded." -ForegroundColor Green

# --- 3. RUN UNIT TESTS ---
if (-not $SkipTests) {
    Write-Host "`n[3/5] Running automated unit test suite..." -ForegroundColor Yellow
    $testCsproj = Join-Path $PCServerDir "OmniPadServer.Tests\OmniPadServer.Tests.csproj"
    if ($Runtime -eq "linux-x64") {
        & dotnet test $testCsproj -c Release -f net8.0 --verbosity normal
    } else {
        & dotnet test $testCsproj -c Release --verbosity normal
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed with exit code $LASTEXITCODE"
    }
    Write-Host "  -> All unit tests passed!" -ForegroundColor Green
} else {
    Write-Host "`n[3/5] Skipping unit tests (-SkipTests specified)." -ForegroundColor DarkGray
}

# Terminate any running OmniPadServer instance so files are not locked during overwrite
$runningProcesses = Get-Process -Name "OmniPadServer*", "OmniPadUpdater*" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    foreach ($proc in $runningProcesses) {
        Write-Host "  -> Stopping running process $($proc.ProcessName) (PID $($proc.Id)) to unlock files..." -ForegroundColor DarkYellow
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Milliseconds 500
}

# --- 4. PUBLISH BINARIES ---
Write-Host "`n[4/5] Publishing self-contained binaries (Runtime: $Runtime)..." -ForegroundColor Yellow

$updaterCsproj = Join-Path $PCServerDir "OmniPadUpdater.App\OmniPadUpdater.App.csproj"
$legacinatorCsproj = Join-Path $PCServerDir "OmniPadServer.Legacinator\OmniPadServer.Legacinator.csproj"
$versionJson = Join-Path $RootDir "version.json"

# (A) WINDOWS PUBLISH
if ($Runtime -in @("win-x64", "all")) {
    Write-Host "  -> Publishing Windows x64 portable bundle..." -ForegroundColor Cyan
    & dotnet publish $appCsproj -c Release -r win-x64 -f net8.0-windows --self-contained true -p:PublishSingleFile=true -o $PortableDir
    if ($LASTEXITCODE -ne 0) {
        throw "Windows publish failed with exit code $LASTEXITCODE"
    }

    $publishedExe = Join-Path $PortableDir "OmniPadServer.App.exe"
    $targetExe = Join-Path $PortableDir "OmniPadServer.exe"
    if (Test-Path $publishedExe) {
        Copy-Item -Path $publishedExe -Destination $targetExe -Force
    }

    & dotnet publish $updaterCsproj -c Release -r win-x64 -f net8.0-windows --self-contained true -p:PublishSingleFile=true -o $PortableDir | Out-Null
    & dotnet publish $legacinatorCsproj -c Release -r win-x64 -f net8.0-windows --self-contained true -p:PublishSingleFile=true -o $PortableDir | Out-Null
    if (Test-Path $versionJson) { Copy-Item -Path $versionJson -Destination $PortableDir -Force }
    Write-Host "  -> Windows x64 published to: $PortableDir" -ForegroundColor Green

    if ($Package) {
        Write-Host "  -> Packaging OmniPad-Portable.zip..." -ForegroundColor Yellow
        $zipPath = Join-Path $RootDir "OmniPad-Portable.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path "$PortableDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "  -> Packaged: $zipPath ($([Math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
    }
}

# (B) LINUX PUBLISH
if ($Runtime -in @("linux-x64", "all")) {
    Write-Host "  -> Publishing Linux x64 self-contained bundle..." -ForegroundColor Cyan
    if (-not (Test-Path $LinuxPublishDir)) {
        New-Item -ItemType Directory -Path $LinuxPublishDir -Force | Out-Null
    }

    & dotnet publish $appCsproj -c Release -r linux-x64 -f net8.0 --self-contained true -p:PublishSingleFile=true -o $LinuxPublishDir
    if ($LASTEXITCODE -ne 0) {
        throw "Linux publish failed with exit code $LASTEXITCODE"
    }

    $publishedLinuxBin = Join-Path $LinuxPublishDir "OmniPadServer.App"
    $targetLinuxBin = Join-Path $LinuxPublishDir "OmniPadServer"
    if (Test-Path $publishedLinuxBin) {
        Copy-Item -Path $publishedLinuxBin -Destination $targetLinuxBin -Force
    }

    & dotnet publish $updaterCsproj -c Release -r linux-x64 -f net8.0 --self-contained true -p:PublishSingleFile=true -o $LinuxPublishDir | Out-Null

    $linuxWeb = Join-Path $LinuxPublishDir "WebClient"
    $linuxWwwroot = Join-Path $LinuxPublishDir "wwwroot"
    if (-not (Test-Path $linuxWeb)) { New-Item -ItemType Directory -Path $linuxWeb -Force | Out-Null }
    if (-not (Test-Path $linuxWwwroot)) { New-Item -ItemType Directory -Path $linuxWwwroot -Force | Out-Null }
    Copy-Item -Path "$WebClientDir\*" -Destination "$linuxWeb\" -Recurse -Force
    Copy-Item -Path "$WebClientDir\*" -Destination "$linuxWwwroot\" -Recurse -Force
    if (Test-Path $versionJson) { Copy-Item -Path $versionJson -Destination $LinuxPublishDir -Force }

    Write-Host "  -> Linux x64 published to: $LinuxPublishDir" -ForegroundColor Green

    if ($Package) {
        Write-Host "  -> Packaging OmniPad-Linux-x64.tar.gz..." -ForegroundColor Yellow
        $tarPath = Join-Path $RootDir "OmniPad-Linux-x64.tar.gz"
        if (Test-Path $tarPath) { Remove-Item $tarPath -Force }
        $publishParent = Split-Path $LinuxPublishDir
        $publishFolder = Split-Path $LinuxPublishDir -Leaf
        & tar -czvf $tarPath -C $publishParent $publishFolder | Out-Null
        Write-Host "  -> Packaged: $tarPath ($([Math]::Round((Get-Item $tarPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
    }
}

# --- 5. BUILD ANDROID APK (GRADLE) ---
if (-not $SkipAndroid -and $Runtime -ne "linux-x64") {
    Write-Host "`n[5/5] Building Android Client APK..." -ForegroundColor Yellow
    
    # Ensure JAVA_HOME points to a Gradle 8.10 compatible JDK (17 or 21)
    function Get-JdkMajorVersion([string]$dir) {
        if (-not $dir) { return 0 }
        $relPath = Join-Path $dir "release"
        if (Test-Path $relPath) {
            $line = Get-Content $relPath | Select-String 'JAVA_VERSION="([0-9]+)'
            if ($line -and $line.Matches[0].Groups[1].Value) {
                return [int]$line.Matches[0].Groups[1].Value
            }
        }
        return 0
    }

    $currentMajor = Get-JdkMajorVersion $env:JAVA_HOME
    if ($currentMajor -ne 17 -and $currentMajor -ne 21) {
        $jbrCandidates = @(
            "C:\Program Files\Android\Android Studio\jbr",
            "$env:LOCALAPPDATA\Programs\Android Studio\jbr",
            "C:\Program Files\Microsoft\jdk-21*",
            "C:\Program Files\Microsoft\jdk-17*",
            "C:\Program Files\Eclipse Adoptium\jdk-17*",
            "C:\Program Files\Eclipse Adoptium\jdk-21*",
            "C:\Program Files\Java\jdk-17*",
            "C:\Program Files\Java\jdk-21*"
        )
        foreach ($cand in $jbrCandidates) {
            $resolved = Resolve-Path $cand -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($resolved -and (Test-Path $resolved.Path)) {
                $candMajor = Get-JdkMajorVersion $resolved.Path
                if ($candMajor -eq 17 -or $candMajor -eq 21) {
                    $env:JAVA_HOME = $resolved.Path
                    $env:PATH = "$($env:JAVA_HOME)\bin;$env:PATH"
                    Write-Host "  -> Selected compatible JDK $($candMajor): $($env:JAVA_HOME)" -ForegroundColor Cyan
                    break
                }
            }
        }
    }

    $gradlew = Join-Path $AndroidDir "gradlew.bat"
    if (Test-Path $gradlew) {
        Push-Location $AndroidDir
        try {
            & $gradlew assembleDebug --no-daemon
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Android Gradle build failed. Check JDK/Android SDK setup."
            } else {
                $apkSource = Join-Path $AndroidDir "app\build\outputs\apk\debug\app-debug.apk"
                $apkDest = Join-Path $PortableDir "apks\OmniPad.apk"
                if (Test-Path $apkSource) {
                    $apkFolder = Split-Path $apkDest
                    if (-not (Test-Path $apkFolder)) { New-Item -ItemType Directory -Path $apkFolder -Force | Out-Null }
                    Copy-Item -Path $apkSource -Destination $apkDest -Force
                    Write-Host "  -> OmniPad.apk copied to: $apkDest" -ForegroundColor Green
                }
            }
        } finally {
            Pop-Location
        }
    } else {
        Write-Warning "gradlew.bat not found at: $gradlew"
    }
} else {
    Write-Host "`n[5/5] Skipping Android APK build." -ForegroundColor DarkGray
}

$sw.Stop()
Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host " Build Finished Successfully in $([Math]::Round($sw.Elapsed.TotalSeconds, 1)) seconds! " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
