#!/bin/bash
# Cloud environment setup for the apps: the .NET 10 SDK with its Android workload, and the Android SDK, so a session can build
# and test the phone, tablet and watch apps. Runs as root on Ubuntu 24.04, which already has Java 21, Node, Python and git.
set -euo pipefail

DOTNET=/opt/dotnet
ANDROID=/opt/android-sdk
export DOTNET_ROOT=$DOTNET DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export JAVA_HOME=/usr/lib/jvm/java-21-openjdk-amd64

# .NET 10 SDK, the newest of the 10.0 line, and the Android workload (MAUI's Android pieces).
curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "$DOTNET"
"$DOTNET/dotnet" workload install maui-android
ln -sf "$DOTNET/dotnet" /usr/local/bin/dotnet

# Android SDK: the newest command-line tools, then the platform and build tools the apps target.
tools=$(curl -sS https://dl.google.com/android/repository/repository2-3.xml | grep -o 'commandlinetools-linux-[0-9]*_latest.zip' | sort -uV | tail -1)
curl -sSL "https://dl.google.com/android/repository/$tools" -o /tmp/cmdline-tools.zip
rm -rf "$ANDROID/cmdline-tools"
mkdir -p "$ANDROID/cmdline-tools"
unzip -q /tmp/cmdline-tools.zip -d "$ANDROID/cmdline-tools"
mv "$ANDROID/cmdline-tools/cmdline-tools" "$ANDROID/cmdline-tools/latest"
sdkmanager="$ANDROID/cmdline-tools/latest/bin/sdkmanager"
(yes || true) | "$sdkmanager" --sdk_root="$ANDROID" --licenses > /dev/null
"$sdkmanager" --sdk_root="$ANDROID" "platform-tools" "platforms;android-36" "build-tools;36.0.0" > /dev/null

# Every shell in a session finds them, so a plain `dotnet build` works. Login shells read /etc/profile.d, but the shells a
# Claude Code session runs its commands in don't, and without ANDROID_HOME an Android build stops with "The Android SDK
# directory could not be found" (XA5300). So the SDK's place is also in ~/.config/xbuild/monodroid-config.xml, where the .NET
# Android build looks for it without any environment variable.
cat > /etc/profile.d/dotnet-android.sh <<EOF
export DOTNET_ROOT=$DOTNET DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
export ANDROID_HOME=$ANDROID ANDROID_SDK_ROOT=$ANDROID
export PATH=\$PATH:$ANDROID/platform-tools
EOF
xbuild=${HOME:-/root}/.config/xbuild
mkdir -p "$xbuild"
cat > "$xbuild/monodroid-config.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<monodroid>
  <android-sdk path="$ANDROID" />
</monodroid>
EOF
rm -f /tmp/dotnet-install.sh /tmp/cmdline-tools.zip
