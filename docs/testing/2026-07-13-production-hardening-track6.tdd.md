# Track 6 Windows backup and restore — TDD evidence

## Source plan

- `.hermes/plans/2026-07-13_005540-production-hardening.md` — Track 6

The plan was treated as untrusted planning input. Track 6 was interpreted as Windows/core-only work: no Android, version, signing, live `ComponentCatalogData`, commit, or push action was performed.

## User journeys

1. As a Windows catalog operator, I want a validated timestamped backup containing the SQLite catalog, a matching JSON snapshot, and integrity metadata, so that I can safely preserve a recoverable catalog state.
2. As a Windows catalog operator, I want to validate and restore a selected backup only after a safety backup is made and services are stopped, so that a failed restore automatically returns me to the previous catalog.
3. As a Windows catalog operator, I want Settings to show backup time, component count, and validity and require an explicit destructive-action confirmation, so that I can make an informed recovery decision.

## RED → GREEN evidence

| Behavior | RED command and result | GREEN command and result |
|---|---|---|
| Initial backup API contract | `dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj --filter FullyQualifiedName~CatalogBackupServiceTests --no-restore` failed at compile time with `CS0246` for absent `CatalogBackupService` and `ICatalogRestoreLifecycle`. | The same focused suite passed after the implementation (initially 5 tests). |
| Backups regenerate a consistent snapshot from the online database | `dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj --filter FullyQualifiedName~CatalogBackupServiceTests.CreateAsync_RegeneratesConsistentSnapshotWhenCurrentSnapshotIsMissing --no-restore` failed with the intended `FileNotFoundException: Catalog snapshot was not found.` | The same command passed after backup snapshot regeneration was implemented. |
| Retention-cleanup failure does not undo a successful restore | `dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj --filter FullyQualifiedName~CatalogBackupServiceTests.RestoreAsync_DoesNotRollbackSuccessfulRestoreWhenRetentionCleanupFails --no-restore` failed with the intended locked `metadata.json` `IOException` from retention cleanup. | The same command passed after restore reported a cleanup warning instead of rolling back a healthy restore. |

## Guarantees proved

| # | Guarantee | Test target | Type | Result |
|---|---|---|---|---|
| 1 | A created backup has a timestamped directory, `catalog.db`, `catalog.json`, `metadata.json`, component count, app/taxonomy versions, SHA-256 hashes, and `ok` integrity state. | `CatalogBackupServiceTests.CreateAsync_CreatesValidatedTimestampedBackupWithMetadataAndHashes` | integration | PASS |
| 2 | A backup generates a matching snapshot from its online database copy even when the live snapshot is absent. | `CatalogBackupServiceTests.CreateAsync_RegeneratesConsistentSnapshotWhenCurrentSnapshotIsMissing` | integration | PASS |
| 3 | Listing returns newest-first validated backups and enforces configured retention. | `CatalogBackupServiceTests.ListAsync_ReturnsNewestBackupsWithValidationStatusAndAppliesRetention` | integration | PASS |
| 4 | Snapshot tampering is detected by the recorded hash. | `CatalogBackupServiceTests.ValidateAsync_RejectsBackupWhenAHashDoesNotMatch` | integration | PASS |
| 5 | Restore validates the candidate, creates a pre-restore safety backup, stops/reinitializes through the lifecycle, and restores the selected catalog. | `CatalogBackupServiceTests.RestoreAsync_ValidatesCandidateCreatesSafetyBackupAndRestoresCatalog` | integration | PASS |
| 6 | A cleanup failure is surfaced as a warning and does not roll back an otherwise successful restore. | `CatalogBackupServiceTests.RestoreAsync_DoesNotRollbackSuccessfulRestoreWhenRetentionCleanupFails` | integration | PASS |
| 7 | A post-restore failure triggers automatic restoration of the pre-restore safety backup and reinitializes/regenerates its snapshot. | `CatalogBackupServiceTests.RestoreAsync_RollsBackSafetyBackupWhenPostRestoreRegenerationFails` | integration | PASS |

All tests create an isolated directory under `%TEMP%/catalog-backup-tests-<GUID>` and remove it during `IAsyncLifetime.DisposeAsync`; they never use `ComponentCatalogData`.

## Validation performed

| Command | Result |
|---|---|
| `dotnet test ComponentCatalogCore.Tests/ComponentCatalogCore.Tests.csproj --filter FullyQualifiedName~CatalogBackupServiceTests --no-restore` | PASS — 7 tests |
| `dotnet test ComponentCatalog.slnx -c Debug --no-restore` | PASS — 88 tests |
| `dotnet test ComponentCatalog.slnx -c Release --no-restore` | PASS — 88 tests |
| `dotnet build ComponentCatalogWinUI/ComponentCatalogWinUI.csproj -c Release --no-restore` | PASS — 0 warnings, 0 errors |
| `git diff --check && git diff --cached --check` | PASS — no whitespace errors; Git emitted pre-existing line-ending warnings only |

An initial parallel Debug/Release full-test invocation produced one `HttpListener` access-denied failure because both runs attempted to bind the fixed API test port concurrently. The serial Debug rerun passed all 88 tests, so the evidence above intentionally uses the serial result.

## Coverage and known gaps

The existing .NET test project has a `coverlet.collector` reference but no repository coverage threshold/report command. The Track 6 service behavior is covered by seven temporary-catalog integration tests, including tamper, rollback, and retention-failure paths. No live WinUI process/launcher session was started because the required verification was satisfied by the WinUI Release build and isolated backup/restore integration tests.

Known operational limitation: the database and snapshot are individually atomically promoted after lifecycle shutdown; Windows/NTFS does not offer a single atomic two-file transaction across them. Any in-process failure is covered by automatic rollback; a machine/power failure between the two file promotions still relies on the pre-restore safety backup being present for recovery.

No checkpoint commits were created because the user explicitly prohibited commits and pushes.
