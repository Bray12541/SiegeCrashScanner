# Siege Crash Scanner

Siege Crash Scanner is a lightweight Windows 11 x64 desktop diagnostic for common Rainbow Six Siege crash signals. It is written in C# with .NET 8 and WPF.

The normal **SCAN MY PC** action is read-only. It does not inject into Siege, alter game files, change BIOS/XMP/RAM settings, disable or bypass BattlEye, uninstall drivers, delete files, overclock hardware, or change the registry.

## Run the ready-to-use app

Open:

```text
release\SiegeCrashScanner.exe
```

The published executable is self-contained for Windows x64, so the target PC does not need a separate .NET installation. Windows may show its normal reputation warning for an unsigned locally built executable.

## What the scanner checks

- CPU, GPU, GPU driver version/date, RAM capacity/speed, motherboard, BIOS, and Windows version
- Steam and Ubisoft Connect Siege installations and free space on the game drive
- Application Event Log entries for recent Siege crashes, including faulting application/module, exception code, and offset
- Windows Memory Diagnostic results
- WHEA hardware events, including CPU, memory-controller, PCIe, and corrected events
- CPER decoding for known processor, memory, PCIe, and SoC/firmware sections, including fatal/corrected severity and the persisted `PreviousError` flag
- WHEA fingerprinting that collapses replayed copies into unique records while preserving the raw occurrence count
- Live physical-memory use, available RAM, pagefile allocation, and commit usage
- NVIDIA, AMD, and Windows Display driver events, correlated within two minutes of a Siege crash
- WHEA-to-Siege correlation within five minutes, recent disk/controller/filesystem errors, unexpected shutdown events, system uptime, BIOS date, and physical-drive status
- BattlEye service state and actual Service Control Manager failure events
- Currently running overlay and monitoring applications from a clearly defined common-program list
- A current-session comparison after **SCAN AGAIN**, showing only evidence newer than the previous scan

Results are ranked only when the scan finds supporting evidence. Likelihood is expressed as **Low**, **Medium**, or **High**—never as a fabricated percentage. Exception codes such as `0xc0000005`, `0xc0000374`, and `0xc0000409` are explained as application memory-corruption signals and are not treated as automatic proof of bad RAM.

The scanner recommends one next test at a time. **Export Diagnostic Report** saves the details as a text file after removing the Windows username, computer name, user-profile path components, and IPv4 addresses. Hardware serial numbers are never collected.

## Driver Center

The app provides three separate update paths so a successful Windows Update check is not mistaken for a vendor-current driver check:

- **WINDOWS DRIVERS** searches Windows Update for applicable signed display and platform drivers and reports every offered update and installation result inside the app.
- **NVIDIA GAME READY** opens an installed NVIDIA App, or downloads its current installer from NVIDIA's official site. The downloaded installer must have a valid NVIDIA Authenticode signature before it can run.
- **INTEL PLATFORM** opens Intel Driver & Support Assistant, or downloads its installer from Intel's official site. The downloaded installer must have a valid Intel Authenticode signature before it can run.

NVIDIA App and Intel Driver & Support Assistant perform the final device-compatibility check and show the vendor's license/install approval. The user must approve that vendor step; Siege Crash Scanner never accepts a third-party license silently. Intel DSA may not offer motherboard-manufacturer-specific packages such as some chipset INF or Management Engine releases, so those may still need to come from the PC or motherboard support page.

Driver updating never flashes the BIOS or changes firmware, overclocking, XMP, RAM timings, game files, or anti-cheat configuration.

## Automatic app updates

The app checks the latest stable release from `Bray12541/SiegeCrashScanner` on GitHub when it starts. If a newer version is available, an update banner appears. Installation happens only after the user clicks **INSTALL UPDATE** and approves the Windows administrator prompt.

Every release must include both `SiegeCrashScanner.exe` and `SiegeCrashScanner.exe.sha256`. The updater downloads both files and refuses to install the executable unless its SHA-256 digest matches the published checksum. It then closes the current app, replaces the executable, and reopens the updated version. Offline update checks fail silently and never block scanning.

## Windows integrity tools

**Run SFC Scan** runs:

```text
sfc.exe /scannow
```

**Run DISM Repair** runs:

```text
dism.exe /Online /Cleanup-Image /RestoreHealth
```

**Run CHKDSK Online Scan** runs:

```text
chkdsk.exe /scan
```

The CHKDSK action performs an online diagnostic scan and does not schedule an offline repair. The app can also open Windows Memory Diagnostic and Windows Update, but Windows leaves the final test/update choice to the user.

These repairs run only after a separate button click, trigger the standard Windows administrator prompt, and return their output to the app. Canceling the prompt makes no change.

## Build with Visual Studio 2022

Requirements:

- Windows 11 x64
- Visual Studio 2022 with the **.NET desktop development** workload
- .NET 8 SDK or newer with the .NET 8 targeting pack

Steps:

1. Open `SiegeCrashScanner.sln` in Visual Studio (`SiegeCrashScanner.slnx` is also included for newer Visual Studio releases).
2. Select **Release** and **x64**.
3. Choose **Build > Build Solution**.

The framework-dependent build is written under:

```text
SiegeCrashScanner\bin\Release\net8.0-windows\win-x64\
```

## Build or publish from the command line

From this repository's root:

```powershell
dotnet build SiegeCrashScanner.sln -c Release
dotnet publish SiegeCrashScanner\SiegeCrashScanner.csproj -c Release -r win-x64 --self-contained true -o release
```

The project file enables single-file publishing and includes native libraries in the bundle.

## Implementation notes

- The scan window is the most recent 30 days, with newest crashes displayed first.
- System hardware is queried locally through Windows CIM. Memory/commit data comes from the Windows memory-status API.
- Event Log access can be restricted by enterprise policy. The app keeps scanning other sources and records an availability note rather than inventing a result.
- A stopped BattlEye service is not automatically an error; the service may start on demand with the game. Only recognized Service Control Manager failure events are counted as failures.
- The presence of Discord, Afterburner, RivaTuner, or another listed program is a testable clue, not proof of causation.
