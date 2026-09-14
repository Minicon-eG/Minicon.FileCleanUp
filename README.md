# Minicon.FileCleanUp

.NET 10 library for age-based file cleanup, designed for customer-owned console applications.

**Development preview. Not a production release.** The HTML handbook describes the broader target specification. See [implementation status](docs/IMPLEMENTATION-STATUS.md) for the exact tested scope and remaining work. File deletion is permanent.

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
- Package boundary test: `python3 scripts/package-smoke.py`.
- Build package: `dotnet pack src/Minicon.FileCleanUp -c Release -o artifacts/packages`.

Public behavior tests were developed in individual red/green cycles, starting with safe dry run. They use in-memory IO, controlled time and targeted boundary faults. Real local IO tests cover journal reopening; Windows/SMB acceptance is tracked separately.

MIT license.
