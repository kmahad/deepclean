# DeepClean

A modern, lightweight Windows system optimization and cleaning utility built with **.NET 10** and **WPF**. DeepClean is designed with a "safety-first" philosophy, ensuring that your system remains stable while freeing up space and monitoring performance.

## 🚀 Features

### ✅ Completed

**Phase 0–1 — Foundation & Monitoring**

- **Fluent Dashboard**: Real-time monitoring of CPU, GPU, and RAM usage with high-performance sparklines.
- **System Tray Integration**: Quick access to all features and real-time resource monitoring from the Windows taskbar.
- **Modern UI**: Dark-themed, Fluent design language using [WPF-UI](https://github.com/lepoco/wpfui).

**Phase 2 — Deep Clean Scanner**

- **Deep Clean Scanner**: Safely identifies and removes temporary files, browser caches, update logs, and system leftovers.
- **Safety Guard**: A multi-tier protection system that prevents the accidental deletion of critical Windows folders.

**Phase 3 — Duplicate File Finder**

- **Duplicate File Finder**: A three-pass, hash-based engine that finds identical files. It prioritizes safety by forcing personal folder copies to the Recycle Bin.
- **Nothing Pre-Selected**: Copies are never ticked for you — the oldest copy in each set is only suggested as the one to keep, and only the copies you explicitly review and select are removed.

### 🛠️ In Progress (Phase 4)

- **Performance Modes**: The Performance page switches to Windows high-performance or power-saver plans and restores the plan that was active before DeepClean changed it. Gaming mode only sends graceful close requests to apps the user checked; it never force-terminates processes. The original plan is also restored when DeepClean exits.

#### Phase 4 progress

- [x] Added a Performance page and navigation entry for choosing Normal, Gaming, and Power saving modes.
- [x] Added Windows power-plan switching, with the pre-change plan retained so Normal mode and app shutdown can restore it.
- [x] Added a list of open desktop apps with explicit, persisted Gaming mode close approvals.
- [x] Gaming mode sends graceful close requests only; it does not force-terminate processes.
- [ ] Validate mode switching and restoration on Windows devices with different available power plans.

## 🛡️ Safety First

- **Always Scan First**: DeepClean never deletes anything without showing you exactly what it found.
- **Recycle Bin Default**: Most removals go to the Recycle Bin so you can undo changes if needed.
- **Hard Protection**: Critical system paths (System32, Program Files, etc.) are strictly off-limits to the cleaner.
- **User Control**: Gaming Mode will only close background applications that you have explicitly approved.

## 💻 Tech Stack

- **Framework**: .NET 10.0
- **UI**: WPF + [WPF-UI](https://github.com/lepoco/wpfui)
- **Architecture**: MVVM ([CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet-communitytoolkit))
- **Logging**: Serilog with daily rolling files
- **Testing**: xUnit with a comprehensive suite of safety and logic tests

## 🏃 Getting Started

### Requirements

- Windows 10 (1809) or higher
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Running the App

```powershell
# Clone the repository
git clone https://github.com/kmahad/deepclean.git
cd DeepClean

# Run the application
dotnet run --project src/DeepClean.App
```

### Running Tests

```powershell
dotnet test
```

## 📜 License

This project is for educational purposes. All rights reserved.
