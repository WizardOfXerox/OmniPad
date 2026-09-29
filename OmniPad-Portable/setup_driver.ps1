<#
.SYNOPSIS
    OmniPad Windows Environment & Driver Setup Script
    Checks/installs .NET 8 Runtime, ViGEmBus driver, and configures Windows Firewall.
#>

param(
    [switch]$InstallDriver,
    [switch]$InstallDotNet
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "       OmniPad Windows Environment Setup          " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Check & Install Microsoft .NET 8 Desktop Runtime
Write-Host "`n[1/3] Checking Microsoft .NET 8 Desktop Runtime..." -ForegroundColor Yellow
$hasDotNet8 = $false
try {
    $runtimes = & dotnet --list-runtimes 2>$null
    if ($runtimes -match "Microsoft.WindowsDesktop.App 8\.") {
        $hasDotNet8 = $true
    }
} catch {}

if (-not $hasDotNet8 -and (Test-Path "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App\8.*")) {
    $hasDotNet8 = $true
}

if ($hasDotNet8) {
    Write-Host "[OK] Microsoft .NET 8 Desktop Runtime is installed and ready." -ForegroundColor Green
} else {
    Write-Host "[INFO] Microsoft .NET 8 Desktop Runtime was NOT found on this PC." -ForegroundColor Yellow
    Write-Host "       (Note: OmniPadServer.exe will still run standalone without .NET installed)" -ForegroundColor DarkGray

    $dotnetInstallerUrl = "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"
    $dotnetInstallerPath = "$PSScriptRoot\windowsdesktop-runtime-8.0-win-x64.exe"

    if ($InstallDotNet) {
        Write-Host "Downloading official Microsoft .NET 8 Desktop Runtime installer..." -ForegroundColor Cyan
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $dotnetInstallerUrl -OutFile $dotnetInstallerPath
        Write-Host "Launching Microsoft installer (requires approval)..." -ForegroundColor Cyan
        Start-Process -FilePath $dotnetInstallerPath -Wait
        Write-Host "[OK] .NET 8 installation completed." -ForegroundColor Green
    } else {
        Write-Host "To install .NET 8 automatically, run: .\install_dotnet.bat" -ForegroundColor Cyan
        Write-Host "Or re-run this script with: -InstallDotNet" -ForegroundColor Cyan
    }
}

# 2. Windows Firewall Rule Check & Setup
Write-Host "`n[2/3] Checking Windows Firewall rules for OmniPad..." -ForegroundColor Yellow
$firewallRule = Get-NetFirewallRule -DisplayName "OmniPad Gamepad Server" -ErrorAction SilentlyContinue

if ($null -eq $firewallRule) {
    Write-Host "Adding Windows Firewall exception for UDP ports 27500, 27501 and TCP 27502..." -ForegroundColor Cyan
    try {
        New-NetFirewallRule -DisplayName "OmniPad Gamepad Server" `
                            -Direction Inbound `
                            -Protocol UDP `
                            -LocalPort 27500, 27501 `
                            -Action Allow `
                            -Profile Any -ErrorAction Stop | Out-Null

        New-NetFirewallRule -DisplayName "OmniPad Web PWA Server" `
                            -Direction Inbound `
                            -Protocol TCP `
                            -LocalPort 27502 `
                            -Action Allow `
                            -Profile Any -ErrorAction Stop | Out-Null

        Write-Host "[OK] Firewall rules successfully created." -ForegroundColor Green
    }
    catch {
        Write-Host "[WARNING] Could not add firewall rules automatically (Requires Run as Administrator)." -ForegroundColor Red
        Write-Host "If connecting over Wi-Fi fails, allow ports 27500, 27501, 27502 in Windows Defender Firewall."
    }
}
else {
    Write-Host "[OK] Windows Firewall rules are already configured." -ForegroundColor Green
}

# 3. Check ViGEmBus Driver
Write-Host "`n[3/3] Checking ViGEmBus Kernel Driver..." -ForegroundColor Yellow
$vigemService = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue

if ($null -ne $vigemService -and $vigemService.Status -eq 'Running') {
    Write-Host "[OK] ViGEmBus driver is installed and RUNNING! Native Xbox 360 controller emulation is active." -ForegroundColor Green
}
else {
    Write-Host "[INFO] ViGEmBus kernel driver is NOT currently installed." -ForegroundColor Yellow
    Write-Host "       (OmniPad will automatically use zero-driver Keyboard/Mouse fallback mode)"

    $installerUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe"
    $installerPath = "$PSScriptRoot\ViGEmBus_Setup.exe"

    if ($InstallDriver) {
        if (-not (Test-Path $installerPath)) {
            Write-Host "Downloading ViGEmBus installer..." -ForegroundColor Cyan
            Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath
        }
        Write-Host "Launching ViGEmBus installer (requires admin approval)..." -ForegroundColor Cyan
        Start-Process -FilePath $installerPath -Wait
        Write-Host "[OK] ViGEmBus installation completed. Please re-run this script to verify." -ForegroundColor Green
    }
    else {
        Write-Host "`nTo install ViGEmBus automatically, run: .\install_vigem.bat" -ForegroundColor Cyan
        Write-Host "Or re-run this script with: -InstallDriver" -ForegroundColor Cyan
    }
}

Write-Host "`nSetup check finished!" -ForegroundColor Cyan
