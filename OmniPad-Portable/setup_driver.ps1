<#
.SYNOPSIS
    OmniPad Windows Environment & Driver Setup Script
    Configures Windows Firewall and checks/installs ViGEmBus driver.
#>

param(
    [switch]$InstallDriver
)

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "       OmniPad Windows Environment Setup          " -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan

# 1. Windows Firewall Rule Check & Setup
Write-Host "`n[1/2] Checking Windows Firewall rules for OmniPad..." -ForegroundColor Yellow
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

# 2. Check ViGEmBus Driver
Write-Host "`n[2/2] Checking ViGEmBus Kernel Driver..." -ForegroundColor Yellow
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
        Write-Host "Downloading ViGEmBus installer..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $installerUrl -OutFile $installerPath
        Write-Host "Launching installer (requires admin approval)..." -ForegroundColor Cyan
        Start-Process -FilePath $installerPath -Wait
        Write-Host "Installation completed. Please re-run this script to verify." -ForegroundColor Green
    }
    else {
        Write-Host "`nTo install ViGEmBus automatically, run:" -ForegroundColor Cyan
        Write-Host "   powershell -ExecutionPolicy Bypass -File .\tools\setup_driver.ps1 -InstallDriver" -ForegroundColor White
        Write-Host "Or download manually from: $installerUrl" -ForegroundColor Cyan
    }
}

Write-Host "`nSetup check finished!" -ForegroundColor Cyan
