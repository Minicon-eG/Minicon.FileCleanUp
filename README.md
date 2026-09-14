# Minicon.FileCleanUp

.NET 10 library for age-based file cleanup, designed for customer-owned console applications.

**Version 1.0.0 for Windows and UNC network shares.** Customer-specific deployment and acceptance remain the host operator’s responsibility. The HTML handbook describes the broader target specification. See [implementation status](https://github.com/Minicon-eG/Minicon.FileCleanUp/blob/v1.0.0/docs/IMPLEMENTATION-STATUS.md) for the exact tested scope and remaining work. File deletion is permanent.

```csharp
builder.Services.AddFileCleanUp(builder.Configuration.GetSection("FileCleanUp"));
using var host = builder.Build();
var cleanup = host.Services.GetRequiredService<IFileCleanUpService>();
var result = await cleanup.RunAsync(cancellationToken);
```

The library consumes configuration supplied by the host. Logging uses `ILogger<FileCleanUpService>`; the sample host integrates Serilog/Seq. `IFileSystem` (TestableIO) and `TimeProvider` are injectable. Dry run is enabled by default.

- Offline handbook: `docs/index.html`; the NuGet package copies it to `docs/Minicon.FileCleanUp/index.html` in build/publish output.
- Disable copying: `<MiniconFileCleanUpCopyDocumentation>false</MiniconFileCleanUpCopyDocumentation>`.
- Sample: `samples/Minicon.FileCleanUp.Console`.
- Test: `dotnet test`.
- Host/process acceptance: `python3 scripts/host-smoke.py`.
- Windows storage acceptance on a disposable administrative test machine: `./scripts/windows-storage-smoke.ps1`.
- Package boundary test: `python3 scripts/package-smoke.py`.
- Build package: `dotnet pack src/Minicon.FileCleanUp -c Release -o artifacts/packages`.

Public behavior tests were developed in individual red/green cycles, starting with safe dry run. They use in-memory IO, controlled time and targeted boundary faults. Real-process tests verify crash/restart behavior. Windows CI exercises a disposable SMB share, outage/restart, sharing locks and junctions. See the [Windows production runbook](https://github.com/Minicon-eG/Minicon.FileCleanUp/blob/v1.0.0/docs/PRODUCTION-RUNBOOK.md) for deployment, audit storage and remaining customer acceptance.

MIT license.
