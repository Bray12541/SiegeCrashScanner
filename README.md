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
- Live physical-memory use, available RAM, pagefile allocation, and commit usage
- NVIDIA, AMD, and Windows Display driver events, correlated within two minutes of a Siege crash
- BattlEye service state and actual Service Control Manager failure events
- Currently running overlay and monitoring applications from a clearly defined common-program list

Results are ranked only when the scan finds supporting evidence. Likelihood is expressed as **Low**, **Medium**, or **High**—never as a fabricated percentage. Exception codes such as `0xc0000005`, `0xc0000374`, and `0xc0000409` are explained as application memory-corruption signals and are not treated as automatic proof of bad RAM.

The scanner recommends one next test at a time. **Export Diagnostic Report** saves the details as a text file after removing the Windows username, computer name, user-profile path components, and IPv4 addresses. Hardware serial numbers are never collected.

## Driver updates

**UPDATE DRIVERS** runs from inside the app and uses the built-in Windows Update service to find, download, and install applicable signed GPU and platform drivers for the detected Intel/AMD/NVIDIA hardware. It shows every matched update and installation result in the app, requests administrator approval before making changes, and reports when Windows requires a restart.

The feature installs the newest compatible driver that Windows Update offers to that specific PC. A hardware vendor may occasionally publish a newer optional or beta package that Windows Update does not yet offer. Driver updating never flashes the BIOS or changes firmware, overclocking, XMP, RAM timings, game files, or anti-cheat configuration.

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
