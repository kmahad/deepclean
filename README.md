# DeepClean

A modern, lightweight Windows system optimization and cleaning utility built with **.NET 10** and **WPF**. DeepClean is designed with a "safety-first" philosophy, ensuring that your system remains stable while freeing up space and monitoring performance.

## 🚀 Features

### ✅ Completed

- **Fluent Dashboard**: Real-time monitoring of CPU, GPU, and RAM usage with high-performance sparklines.
- **Deep Clean Scanner**: Safely identifies and removes temporary files, browser caches, update logs, and system leftovers.
- **Duplicate File Finder**: A three-pass, hash-based engine that finds identical files. It prioritizes safety by forcing personal folder copies to the Recycle Bin.
- **System Tray Integration**: Quick access to all features and real-time resource monitoring from the Windows taskbar.
- **Safety Guard**: A multi-tier protection system that prevents the accidental deletion of critical Windows folders.
- **Modern UI**: Dark-themed, Fluent design language using [WPF-UI](https://github.com/lepoco/wpfui).

### 🛠️ In Progress (Phase 4)

- **Performance Modes**:
  - **Gaming Mode**: Switches to high-performance power plans and gracefully closes user-approved background apps.
  - **Power Saving Mode**: Optimizes settings for battery life.
  - **Normal Mode**: Restores your system to its original state.

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
git clone https://github.com/your-username/DeepClean.git
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
