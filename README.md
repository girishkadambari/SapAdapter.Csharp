# SAP Copilot Adapter (C# Edition)

A .NET 8 WebSocket adapter for SAP GUI Scripting — drop-in replacement for the TypeScript adapter.

## Why C#?
- **Native COM interop** — no `winax`, Python, or `node-gyp` build tools needed
- **Structured logging** via Serilog (console + rolling file)
- **Visual Studio debugging** — breakpoints, watch, call stack
- **Configurable** via `appsettings.json`

## Prerequisites
- Windows with SAP GUI installed + scripting enabled
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Quick Start
```powershell
dotnet build
dotnet run
# or with custom port:
dotnet run -- --port 9090
```

## Architecture
```
SapAdapter.Csharp/
├── Models/         # Wire-compatible DTOs (snapshot, protocol, errors)
├── Com/            # Native COM interop with SAP GUI
├── Sessions/       # Session registry + auto-discovery
├── Snapshot/       # Budget-constrained screen capture pipeline
│   ├── Extractors/ # Field, shell, status bar extraction
│   └── Packs/      # Transaction-specific enrichment (MIRO, ME23N)
├── Commands/       # 25+ command handlers (Grid, Table, Tree, Menu, OTC)
│   └── Handlers/   # Handler implementations
├── Server/         # WebSocket server + event broadcaster
├── Program.cs      # Entry point
└── appsettings.json # Configuration
```

## Configuration
Edit `appsettings.json`:
| Key | Default | Description |
|---|---|---|
| `Adapter.Port` | 8787 | WebSocket server port |
| `Sap.WaitTimeoutMs` | 20000 | Session idle wait timeout |
| `Sap.MaxCellReads` | 500 | Grid cell read budget per snapshot |
| `Recorder.Enabled` | true | Save replay snapshots to disk |

## Wire Protocol
Same JSON WebSocket protocol as the TypeScript adapter — no UI changes needed.
