# Architecture

The source is split into four assemblies distributed together in the single `Minicon.FileCleanUp` NuGet package. Consumers keep one PackageReference and the existing `Minicon.FileCleanUp` namespace.

| Project | Responsibility | Own project references |
| --- | --- | --- |
| Domain | Options, audit records, result/statistics models and pure UTC retention decisions | None |
| Core | Cleanup orchestration, validation, selection, retries and contracts | Domain |
| Infrastructure | Durable local audit journal and recovery implementation | Core, Domain |
| Minicon.FileCleanUp | Public entry point, options snapshots, configuration binding, DI and default implementation composition | Core, Domain, Infrastructure |

Core accepts `IFileSystem`, `TimeProvider`, `ILogger`, retry jitter and a journal factory. It never constructs the infrastructure implementation. The entry point provides that factory; journal creation still occurs after path validation. The existing journal ownership/disposal rules remain unchanged.

Contracts live in Core: `IFileCleanUpService`, `ICleanupAuditJournal`, `ICleanupAuditRecoveryService`, `IRetryJitter`. Domain does not depend on DI, logging, configuration binding or TestableIO. Statistics remain result data rather than a separate service.

Internal access is narrowly granted: Domain permits Core to update result counters and the entry point to report configuration-binding failures; Core exposes its internal run implementation only to the entry-point assembly. Tests use public contracts, without friend-assembly access.

Public types moved to other assemblies retain their namespace and are forwarded from the original assembly. `FileCleanUpService` stays in the entry assembly, including its constructor and logging category. Assembly-qualified reflection can observe the new owning assemblies; hosts must not depend on scanning only the original assembly for all types.

Only the entry project is packable. Its pack target includes the three layer assemblies; external package dependencies remain declared on the entry project. Internal ProjectReferences are private to packaging so no unpublished Minicon package dependencies are emitted. Source-based samples/tests explicitly reference the layer projects; installed consumers need only one PackageReference.

Validation: existing public behavior tests, real-process recovery, Windows storage acceptance and a fresh package consumer that executes a dry run with Required audit. The package test checks bundled assemblies, absence of internal package dependencies, and offline HTML build/publish/opt-out.

This structure ships in 1.1.0. Published 1.0.0 remains immutable.

## Readability conventions

One top-level public type per file, named after that type. Control flow uses explicit braces and multi-line executable blocks. Keep simple auto-properties compact; split long argument lists and compound conditions across lines. Separate methods and logical phases with blank lines. `.editorconfig` defines formatting and brace requirements, checked by CI using `dotnet format`.
