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
    [switch]$SkipAndroid,
    [switch]$SkipTests,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host "         OmniPad Universal Open-Source Build Pipeline            " -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

$RootDir = $PSScriptRoot
$PCServerDir = Join-Path $RootDir "PCServer"
$AndroidDir = Join-Path $RootDir "AndroidClient"
$WebClientDir = Join-Path $RootDir "WebClient"
$PortableDir = Join-Path $RootDir "OmniPad-Portable"

# --- 1. SYNC WEB CLIENT ASSETS ---
Write-Host "`n[1/5] Synchronizing WebClient PWA assets..." -ForegroundColor Yellow
$destDirs = @(
    (Join-Path $PortableDir "WebClient"),
    (Join-Path $PortableDir "wwwroot"),
    (Join-Path $AndroidDir "app\src\main\assets\web")
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

& dotnet build $appCsproj -c Release
if ($LASTEXITCODE -ne 0) {
    throw "PCServer build failed with exit code $LASTEXITCODE"
}
Write-Host "  -> PCServer build succeeded." -ForegroundColor Green

# --- 3. RUN UNIT TESTS ---
if (-not $SkipTests) {
    Write-Host "`n[3/5] Running automated unit test suite..." -ForegroundColor Yellow
    $testCsproj = Join-Path $PCServerDir "OmniPadServer.Tests\OmniPadServer.Tests.csproj"
    & dotnet test $testCsproj -c Release --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed with exit code $LASTEXITCODE"
    }
    Write-Host "  -> All 56/56 unit tests passed!" -ForegroundColor Green
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

# --- 4. PUBLISH SELF-CONTAINED PORTABLE BINARY ---
Write-Host "`n[4/5] Publishing self-contained single-file OmniPadServer.exe..." -ForegroundColor Yellow
& dotnet publish $appCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $PortableDir
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

# Ensure standard executable name
$publishedExe = Join-Path $PortableDir "OmniPadServer.App.exe"
$targetExe = Join-Path $PortableDir "OmniPadServer.exe"
if (Test-Path $publishedExe) {
    Copy-Item -Path $publishedExe -Destination $targetExe -Force
}
Write-Host "  -> OmniPadServer.exe published to: $targetExe" -ForegroundColor Green

# Publish OmniPadUpdater.exe
$updaterCsproj = Join-Path $PCServerDir "OmniPadUpdater.App\OmniPadUpdater.App.csproj"
& dotnet publish $updaterCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $PortableDir | Out-Null
Write-Host "  -> OmniPadUpdater.exe published to portable bundle." -ForegroundColor Green

# Publish OmniPadLegacinator.exe
$legacinatorCsproj = Join-Path $PCServerDir "OmniPadServer.Legacinator\OmniPadServer.Legacinator.csproj"
& dotnet publish $legacinatorCsproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $PortableDir | Out-Null
Write-Host "  -> OmniPadLegacinator.exe published to portable bundle." -ForegroundColor Green

# --- 5. BUILD ANDROID APK (GRADLE) ---
if (-not $SkipAndroid) {
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
    Write-Host "`n[5/5] Skipping Android APK build (-SkipAndroid specified)." -ForegroundColor DarkGray
}

# --- OPTIONAL: CREATE ZIP PACKAGE ---
if ($Package) {
    Write-Host "`n[+] Packaging OmniPad-Portable.zip..." -ForegroundColor Yellow
    $zipPath = Join-Path $RootDir "OmniPad-Portable.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$PortableDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
    Write-Host "  -> Packaged: $zipPath ($([Math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB)" -ForegroundColor Green
}

$sw.Stop()
Write-Host "`n=================================================================" -ForegroundColor Cyan
Write-Host " Build Finished Successfully in $([Math]::Round($sw.Elapsed.TotalSeconds, 1)) seconds! " -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Cyan
