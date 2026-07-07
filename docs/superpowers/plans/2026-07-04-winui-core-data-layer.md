# WinUI Core Data Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the WinUI 3 shell a real shared-core-backed catalog data layer with SQLite persistence and `catalog.json` export.

**Architecture:** Add a pure .NET class library for contracts, search rules, CSV parsing, snapshot export, and repository persistence. Keep WinUI as the presentation shell that maps records into page state and calls the repository for search, save, delete, and import actions.

**Tech Stack:** .NET 10, WinUI 3, Windows App SDK 2.2, xUnit, Microsoft.Data.Sqlite, System.Text.Json

## Global Constraints

- Windows is the only write source of truth.
- SQLite is the only persisted source; `catalog.json` is an exported snapshot.
- Search semantics must stay compatible with the original shared contract: exact part number first, then alias, then broader keyword and attribute matches.
- CSV import must report row-level errors without aborting the full batch.
- WinUI remains the primary shell; Android remains out of scope for this task.

---

### Task 1: Extract Shared Core Contract

**Files:**
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\ComponentCatalogCore.csproj`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Contracts\ComponentContracts.cs`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Search\CatalogSearchService.cs`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Importing\CatalogImportService.cs`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Export\CatalogSnapshotService.cs`
- Test: `G:\LegacyWorkspace\入库\ComponentCatalogCore.Tests\CatalogSearchServiceTests.cs`
- Test: `G:\LegacyWorkspace\入库\ComponentCatalogCore.Tests\CatalogImportServiceTests.cs`

**Interfaces:**
- Produces: `ComponentRecord`, `SearchQuery`, `SearchResult`, `ImportParseResult`, `ExportCatalogSnapshot`
- Produces: `CatalogSearchService.Search(...)`, `CatalogImportService.ParseCsvRows(...)`, `CatalogSnapshotService.BuildSnapshot(...)`

- [ ] Write failing tests for search ranking and CSV parsing.
- [ ] Run the focused tests and confirm they fail for the missing core services.
- [ ] Implement the minimal core contracts and pure services to satisfy those tests.
- [ ] Re-run the focused tests and confirm they pass.

### Task 2: Add SQLite Repository and Snapshot Export

**Files:**
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Persistence\CatalogRepositoryOptions.cs`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogCore\Persistence\CatalogRepository.cs`
- Modify: `G:\LegacyWorkspace\入库\ComponentCatalogCore\ComponentCatalogCore.csproj`
- Test: `G:\LegacyWorkspace\入库\ComponentCatalogCore.Tests\CatalogRepositoryTests.cs`

**Interfaces:**
- Consumes: pure services and contracts from Task 1
- Produces: `CatalogRepository.InitializeAsync()`, `ListComponentsAsync()`, `SearchAsync(...)`, `UpsertComponentAsync(...)`, `DeleteByIdAsync(...)`, `ImportCsvAsync(...)`, `ExportSnapshotAsync()`

- [ ] Write failing repository tests for SQLite persistence and `catalog.json` export.
- [ ] Run the repository tests and confirm they fail before implementation.
- [ ] Implement the SQLite schema, CRUD methods, search integration, CSV import integration, and JSON export.
- [ ] Re-run repository tests and confirm they pass.

### Task 3: Connect WinUI Shell to the Real Repository

**Files:**
- Modify: `G:\LegacyWorkspace\入库\ComponentCatalogWinUI\ComponentCatalogWinUI.csproj`
- Modify: `G:\LegacyWorkspace\入库\ComponentCatalogWinUI\Models\ComponentItem.cs`
- Modify: `G:\LegacyWorkspace\入库\ComponentCatalogWinUI\Pages\HomePage.xaml.cs`
- Modify: `G:\LegacyWorkspace\入库\ComponentCatalogWinUI\Pages\HomePage.xaml`
- Create: `G:\LegacyWorkspace\入库\ComponentCatalogWinUI\Services\CatalogDesignSeed.cs`

**Interfaces:**
- Consumes: `CatalogRepository` and core contracts from Task 2
- Produces: WinUI page behavior backed by persisted storage instead of in-memory sample state

- [ ] Seed the repository on first launch with sample catalog data.
- [ ] Replace in-memory page operations with repository-backed load, search, save, delete, and import flows.
- [ ] Keep the current Fluent layout intact while updating status text and summaries from real data.
- [ ] Build the WinUI app and run a smoke launch to verify startup and non-empty rendering.
