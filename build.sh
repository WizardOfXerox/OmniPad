#!/usr/bin/env bash
# =============================================================================
# OmniPad Universal Build Script for Linux (x64)
# Builds PCServer (.NET 8), runs unit tests, publishes self-contained single-file
# binaries, and optionally packages an OmniPad-Linux-x64.tar.gz bundle.
# =============================================================================
set -euo pipefail

SKIP_TESTS=false
PACKAGE=false
CLEAN=false

for arg in "$@"; do
    case "$arg" in
        --skip-tests)
            SKIP_TESTS=true
            ;;
        --package)
            PACKAGE=true
            ;;
        --clean)
            CLEAN=true
            ;;
        -h|--help)
            echo "Usage: ./build.sh [OPTIONS]"
            echo "Options:"
            echo "  --skip-tests    Skip automated unit tests"
            echo "  --package       Package build into OmniPad-Linux-x64.tar.gz"
            echo "  --clean         Clean previous publish outputs before building"
            echo "  -h, --help      Display this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $arg"
            echo "Run ./build.sh --help for available options."
            exit 1
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PCSERVER_DIR="$SCRIPT_DIR/PCServer"
WEBCLIENT_DIR="$SCRIPT_DIR/WebClient"
PUBLISH_DIR="$SCRIPT_DIR/publish/OmniPad-Linux-x64"

echo -e "\033[1;36m=================================================================\033[0m"
echo -e "\033[1;36m       OmniPad Linux Host Build & Publish Pipeline (.NET 8)      \033[0m"
echo -e "\033[1;36m=================================================================\033[0m"

# Ensure dotnet SDK is available
if ! command -v dotnet >/dev/null 2>&1; then
    echo -e "\033[1;31mError: .NET SDK ('dotnet') not found in PATH.\033[0m"
    echo "Please install .NET 8.0 SDK from https://dot.net or via your package manager:"
    echo "  Ubuntu/Debian: sudo apt install -y dotnet-sdk-8.0"
    echo "  Fedora:        sudo dnf install -y dotnet-sdk-8.0"
    echo "  Arch Linux:    sudo pacman -S dotnet-sdk-8.0"
    exit 1
fi

DOTNET_VER=$(dotnet --version)
echo -e "Using .NET SDK version: \033[1;32m$DOTNET_VER\033[0m"

if [ "$CLEAN" = true ] && [ -d "$PUBLISH_DIR" ]; then
    echo -e "\n\033[1;33mCleaning previous build outputs...\033[0m"
    rm -rf "$PUBLISH_DIR"
fi

# --- 1. SYNC WEB CLIENT ASSETS ---
echo -e "\n\033[1;33m[1/4] Synchronizing WebClient assets...\033[0m"
mkdir -p "$SCRIPT_DIR/AndroidClient/app/src/main/assets/web"
cp -r "$WEBCLIENT_DIR"/* "$SCRIPT_DIR/AndroidClient/app/src/main/assets/web/" 2>/dev/null || true
echo -e "  -> WebClient assets synchronized."

# --- 2. BUILD SOLUTION ---
echo -e "\n\033[1;33m[2/4] Building PCServer Solution (net8.0)...\033[0m"
dotnet build "$PCSERVER_DIR/OmniPadServer.sln" -c Release -f net8.0
echo -e "  -> PCServer build succeeded."

# --- 3. RUN UNIT TESTS ---
if [ "$SKIP_TESTS" = false ]; then
    echo -e "\n\033[1;33m[3/4] Running automated unit test suite...\033[0m"
    dotnet test "$PCSERVER_DIR/OmniPadServer.Tests/OmniPadServer.Tests.csproj" -c Release -f net8.0 --no-build --verbosity normal
    echo -e "  -> All unit tests passed!"
else
    echo -e "\n\033[1;30m[3/4] Skipping unit tests (--skip-tests specified).\033[0m"
fi

# --- 4. PUBLISH SELF-CONTAINED SINGLE-FILE BINARIES ---
echo -e "\n\033[1;33m[4/4] Publishing self-contained single-file Linux x64 binaries...\033[0m"
mkdir -p "$PUBLISH_DIR"

dotnet publish "$PCSERVER_DIR/OmniPadServer.App/OmniPadServer.App.csproj" \
    -c Release -r linux-x64 -f net8.0 --self-contained true -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR"

# Standardize executable name
if [ -f "$PUBLISH_DIR/OmniPadServer.App" ]; then
    cp -f "$PUBLISH_DIR/OmniPadServer.App" "$PUBLISH_DIR/OmniPadServer"
fi
chmod +x "$PUBLISH_DIR/OmniPadServer" 2>/dev/null || true

# Publish updater
dotnet publish "$PCSERVER_DIR/OmniPadUpdater.App/OmniPadUpdater.App.csproj" \
    -c Release -r linux-x64 -f net8.0 --self-contained true -p:PublishSingleFile=true \
    -o "$PUBLISH_DIR" >/dev/null

chmod +x "$PUBLISH_DIR/OmniPadUpdater" 2>/dev/null || true

# Copy WebClient assets to publish bundle
mkdir -p "$PUBLISH_DIR/WebClient" "$PUBLISH_DIR/wwwroot"
cp -r "$WEBCLIENT_DIR"/* "$PUBLISH_DIR/WebClient/"
cp -r "$WEBCLIENT_DIR"/* "$PUBLISH_DIR/wwwroot/"
if [ -f "$SCRIPT_DIR/version.json" ]; then
    cp -f "$SCRIPT_DIR/version.json" "$PUBLISH_DIR/"
fi

echo -e "  -> Self-contained server published to: \033[1;32m$PUBLISH_DIR\033[0m"

# Optional Packaging
if [ "$PACKAGE" = true ]; then
    echo -e "\n\033[1;33m[+] Packaging OmniPad-Linux-x64.tar.gz...\033[0m"
    TAR_FILE="$SCRIPT_DIR/OmniPad-Linux-x64.tar.gz"
    rm -f "$TAR_FILE"
    tar -czvf "$TAR_FILE" -C "$SCRIPT_DIR/publish" OmniPad-Linux-x64 >/dev/null
    SIZE=$(du -h "$TAR_FILE" | cut -f1)
    echo -e "  -> Packaged: \033[1;32m$TAR_FILE\033[0m ($SIZE)"
fi

echo -e "\n\033[1;36m=================================================================\033[0m"
echo -e "\033[1;32m Build Finished Successfully!\033[0m"
echo -e "\033[1;36m=================================================================\033[0m"
echo -e "\n\033[1;33mLinux /dev/uinput Setup Note:\033[0m"
echo "To allow OmniPadServer to emulate virtual gamepads without requiring root (sudo):"
echo "  1. Add your user to the input group:"
echo "     sudo usermod -aG input \$USER"
echo "  2. Install the udev rule for uinput:"
echo "     echo 'KERNEL==\"uinput\", MODE=\"0660\", GROUP=\"input\", OPTIONS+=\"static_node=uinput\"' | sudo tee /etc/udev/rules.d/99-omniPad-uinput.rules"
echo "  3. Reload udev rules and log out / log in:"
echo "     sudo udevadm control --reload-rules && sudo udevadm trigger"
echo -e "\nTo run OmniPad Server:"
echo "  cd \"$PUBLISH_DIR\" && ./OmniPadServer"
