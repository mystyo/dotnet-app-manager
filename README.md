# DISCLAIMER

This app has been vibe coded, it mean to be just local tool to help run locally and debug multiple repo projects.

# .NET App Manager

A web-based dashboard for discovering, managing, and running .NET projects across multiple repositories.

## Features

- **Multi-repo scanning** — Configure multiple folder paths to scan for .NET projects
- **Smart discovery** — Automatically detects runnable projects by looking for `launchSettings.json`
- **Build & Run** — Build and run projects directly from the browser with real-time console output via Server-Sent Events
- **Run All / Stop All** — Start or stop all visible projects at once
- **Build configurations** — Switch between Debug, Release, or custom configurations from a dropdown
- **Profiles** — Group projects into named profiles and filter the dashboard to show only what you need
- **Manage Projects** — Ignore specific projects and set custom start order for Run All
- **Project dependencies** — View local project references for each service

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later

### Run

```bash
git clone https://github.com/your-username/dotnet-app-manager.git
cd dotnet-app-manager
dotnet run
```

Open `https://localhost:5001` (or the URL shown in the console).

### Configure

1. Go to **Configuration** and add one or more folder paths containing your .NET solutions
2. Optionally add custom build configurations (Debug and Release are included by default)
3. Go to **Manage Projects** to ignore libraries or set start order
4. Go to **Profiles** to create project groups


## License

MIT
