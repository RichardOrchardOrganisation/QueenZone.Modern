# React Native Mobile Development Environment

## Purpose

This guide defines the local toolchain for the QueenZone React Native client at
[`src/QueenZone.Mobile`](../src/QueenZone.Mobile/README.md). Use the same major
versions on Windows and macOS. Patch versions may differ when a newer compatible
patch is available.

Android and iOS are equal supported platforms ([ADR 0011](decisions/0011-mobile-project-location-and-build-tooling.md)).
The Android toolchain is installed on both development machines so the shared
client can be built on either host. iOS compilation still requires macOS and
Xcode.

## Supported Baseline

| Component | Required version | Notes |
| --- | --- | --- |
| Node.js | **24 LTS** | React Native requires Node 22.11 or newer. Use the same LTS major on both machines. |
| npm | Bundled with Node.js | Do not pin a separate global npm unless the client project later requires it. |
| JDK | **17** | Use Eclipse Temurin 17. Higher JDK majors can be incompatible with the Gradle version used by React Native. |
| Android Studio | Latest stable | Install the native build for the host architecture. |
| Android SDK Platform | **API 37** | Expo SDK 58 uses `compileSdk` 37 and `targetSdk` 36. |
| Android SDK Build-Tools | **37.0.0** | Install through Android Studio's SDK Manager. |
| Android SDK Command-line Tools | Latest | Provides `sdkmanager` and `avdmanager`. |
| Android SDK Platform-Tools | Latest | Provides `adb`. |
| Android Emulator | Latest | Keep current through Android Studio. |
| Emulator system image | **API 36, Google APIs** | Use `arm64-v8a` on Apple Silicon and `x86_64` on Windows x64. |
| Git | Latest stable | Xcode Command Line Tools Git or Homebrew Git is sufficient on macOS. |
| Watchman | Latest stable | Recommended on macOS. It is not required on Windows. |
| Expo SDK | **58** | Pinned in `src/QueenZone.Mobile/package.json`. Development builds only; Expo Go is not supported. |
| Xcode | **26.4+** | Required on macOS to compile the iOS target for Expo SDK 58. |
| CocoaPods | Homebrew `cocoapods` on the self-hosted Mac; Expo prebuild on hosted CI | Interactive `npx expo run:ios` can install pods itself. The self-hosted runner service does not load `~/.zprofile`, so both iOS workflows add Homebrew to `PATH`; TestFlight also sets `IOS_BUILD_NUMBER` and runs `pod install` explicitly. |

### Self-hosted Mac build service

The M2 Mac Mini is shared by unsigned `ios-build`, signed `ios-signing`, and
Playwright `e2e` work. Keep only one runner job active on the 16 GB machine at a
time; concurrent Xcode builds can force swapping and make CI flaky. Configure
the machine not to sleep while its launchd runner service is enabled (`sudo
pmset -a sleep 0`, or an equivalent managed `caffeinate` service), because a
sleeping runner can appear available briefly and miss the hosted fallback.

The launchd environment must expose `/opt/homebrew/bin`, and CocoaPods must be
installed there (`brew install cocoapods`). Select `/Applications/Xcode.app`
once as an administrator before starting the service; CI deliberately does not
run passworded `sudo xcode-select` on self-hosted jobs. Check Simulator runtimes,
DerivedData, and CocoaPods caches periodically on the 512 GB disk. Sustained
back-to-back builds may thermal-throttle this Mini, so compare runner choice in
the job summary before treating a slow compile as a code regression.

The established Windows reference currently uses Node 24, Temurin 17, Android
SDK Platform 37, and Build-Tools 37.0.0. Match those compatibility versions on
macOS rather than copying Windows-specific paths or emulator architecture.

## macOS Setup

These steps assume an Apple Silicon Mac and the default `zsh` shell.

### 1. Install the base tools

Install [Homebrew](https://brew.sh/) if it is not already available, then run:

```bash
brew install node@24
brew install watchman
brew install --cask temurin@17
brew install --cask android-studio
```

Install the Apple Silicon edition of Android Studio. Do not run the Intel build
through Rosetta unless the machine is an Intel Mac.

### 2. Install Android SDK components

Open **Android Studio > Settings > Languages & Frameworks > Android SDK**.

Under **SDK Platforms**, enable **Show Package Details** and install:

- Android SDK Platform 37;
- Google APIs ARM 64 v8a System Image, API 36; and
- Sources for Android 36 (recommended, but not required to build).

Under **SDK Tools**, install:

- Android SDK Build-Tools 37.0.0;
- Android SDK Command-line Tools (latest);
- Android SDK Platform-Tools; and
- Android Emulator.

Do not install the Intel `x86_64` emulator image on Apple Silicon. Use the
`arm64-v8a` image so the emulator runs natively.

### 3. Configure the shell

Add the following to `~/.zprofile`:

```bash
export JAVA_HOME=$(/usr/libexec/java_home -v 17)
export ANDROID_HOME="$HOME/Library/Android/sdk"

export PATH="/opt/homebrew/opt/node@24/bin:$PATH"
export PATH="$PATH:$ANDROID_HOME/emulator"
export PATH="$PATH:$ANDROID_HOME/platform-tools"
export PATH="$PATH:$ANDROID_HOME/cmdline-tools/latest/bin"
```

Reload the profile:

```bash
source ~/.zprofile
```

For an Intel Mac, Homebrew normally uses `/usr/local` instead of
`/opt/homebrew`. Adjust the Node path accordingly.

### 4. Create an Android virtual device

Open **Android Studio > Tools > Device Manager > Create Device** and use:

- hardware profile: Pixel 8 or a comparable current phone;
- system image: Google APIs ARM 64 v8a, API 36; and
- name: `Pixel_8_API_36`.

Use a Google Play image instead if the app feature under test specifically
requires the Play Store. The Google APIs image is the leaner default for normal
development.

### 5. Verify the installation

Run:

```bash
java -version
node --version
npm --version
git --version
adb version
emulator -version
sdkmanager --version
emulator -list-avds
```

Expected results:

- Java reports major version 17;
- Node reports major version 24;
- `adb`, `emulator`, and `sdkmanager` are found on `PATH`; and
- `Pixel_8_API_36` appears in the AVD list.

## Windows Equivalents

The Windows user environment uses:

```text
ANDROID_HOME=%LOCALAPPDATA%\Android\Sdk
JAVA_HOME=C:\Program Files\Eclipse Adoptium\<Temurin 17 directory>
```

Add these directories to the user `Path`:

```text
%LOCALAPPDATA%\Android\Sdk\platform-tools
%LOCALAPPDATA%\Android\Sdk\emulator
%LOCALAPPDATA%\Android\Sdk\cmdline-tools\latest\bin
```

Use the Google APIs Intel `x86_64` API 36 emulator image on a Windows x64 host.

## Optional Components

Do not install these as baseline dependencies:

- Android NDK;
- CMake; or
- a global React Native CLI.

Install NDK or CMake only when the client or a native dependency specifies a
version. The first `npx expo run:android` / Gradle build may download NDK
27.1.12297006 and CMake 3.22.1 through `sdkmanager` because Expo SDK 58's
generated Android project requests them; that is expected and is not a manual
baseline install. Run React Native commands through the project's local
tooling, normally with `npx`, rather than maintaining a global CLI.

Developing or signing the iOS target requires Xcode 26.4 or newer (Expo SDK 58)
and CocoaPods. Clean-checkout commands live in
[`src/QueenZone.Mobile/README.md`](../src/QueenZone.Mobile/README.md).

## References

- [ADR 0019](decisions/0019-api-versioning-convention.md) — mobile/store-lag API versioning (breaking changes cut `/api/v2` alongside v1; keep v1 while installed store builds depend on it)
- [Mobile push verification](mobile-push-testing.md) — iOS TestFlight / production APNs and Android Google Play internal testing / FCM receive + record on a real device
- [React Native: Set Up Your Environment](https://reactnative.dev/docs/next/set-up-your-environment)
- [Android Studio installation](https://developer.android.com/studio/install)
- [Android 16 SDK setup](https://developer.android.com/about/versions/16/setup-sdk)
- [Android Virtual Device management](https://developer.android.com/studio/run/managing-avds)
- [Homebrew Temurin 17 cask](https://formulae.brew.sh/cask/temurin@17)
- [Homebrew Node 24 formula](https://formulae.brew.sh/formula/node@24)
