# Inventory Aggregation Service

A small .NET 10 minimal-API service. It reads every inventory record from the (paginated, not perfectly well-behaved)
Inventory API, aggregates them by category, and exposes the result through one `GET` endpoint of its own.

The aggregation is simple. The effort went into **getting a complete and correct dataset** out of the upstream API and
into **failing honestly** when that is not possible.

## Prerequisites

* .NET 10 SDK (10.0.100 or newer). `global.json` pins the SDK band and opts `dotnet test` into the
  **Microsoft Testing Platform** runner, which xunit.v3 4.x requires.
* The Inventory API base URL and API key from your assignment email.

## Configure

| Setting | Where | Notes |
|---|---|---|
| `InventoryApi:BaseUrl` | `src/InventoryAggregation.Api/appsettings.json` | Replace the `REPLACE-WITH-INVENTORY-API-HOST` placeholder. Must be https (http only for loopback). |
| `InventoryApi:ApiKey` | **user-secrets or environment variable - never a file in the repo** | Required; the app refuses to start without it. |
| `InventoryApi:ApiKeyHeaderName` | `appsettings.json` | Defaults to `X-Api-Key`. **Adjust to whatever header your assignment email specifies.** |

Set the API key (pick one):

```bash
# user-secrets (development)
dotnet user-secrets set "InventoryApi:ApiKey" "<your key>" --project src/InventoryAggregation.Api

# environment variable (any environment); double underscore = ':'
export InventoryApi__ApiKey="<your key>"          # bash/zsh
$env:InventoryApi__ApiKey = "<your key>"          # PowerShell
```

Every other setting (page size, concurrency, retry/timeout/circuit-breaker values, cache duration) is documented in
`Configuration/InventoryApiOptions.cs` and validated at startup, so a bad value fails fast with a clear message.

## Run

```bash
dotnet run --project src/InventoryAggregation.Api
curl http://localhost:5080/api/inventory/categories
```

### Endpoint

`GET /api/inventory/categories`

```jsonc
{
  "categories": [
    {
      "category": "Electronics",
      "totalInventoryValue": 1799.88,
      "availableRecordCount": 1,
      "mostRecentlyUpdated": { "id": 1001, "name": "Thermal Receipt Printer", "lastUpdated": "2026-10-15T12:30:00Z" },
      "items": [ { "id": 1001, "name": "Thermal Receipt Printer", "price": 149.99, "quantity": 12, "lastUpdated": "2026-10-15T12:30:00Z" } ]
    }
  ],
  "summary": {                       // deliberate addition - see "Deviations"
    "generatedAt": "2026-11-20T18:00:00Z",
    "recordsRetrieved": 157, "duplicatesDiscarded": 0, "invalidRecordsExcluded": 0,
    "zeroQuantityRecordsExcluded": 12, "availableRecords": 145, "warnings": []
  }
}
```

Failures return RFC 9457 `application/problem+json` with a generic message (details stay in the logs):

| Status | Meaning |
|---|---|
| 503 | Upstream unreachable, returned 5xx/408/429 after retries, or the circuit breaker is open |
| 504 | Upstream did not answer within the time limits |
| 502 | Upstream answered with something unusable (4xx, malformed JSON, inconsistent or incomplete dataset) |
| 500 | Anything unexpected (still a problem document, never a stack trace) |

## Test

```bash
dotnet test                                   # whole solution (xunit.v3 on Microsoft Testing Platform)
dotnet run --project tests/InventoryAggregation.Tests    # alternative: the test project is itself an executable
```

### Coverage (100% gate)

```bash
pwsh ./scripts/coverage.ps1
```

Runs the suite with `Microsoft.Testing.Extensions.CodeCoverage`, measures only `InventoryAggregation.Api.dll`, prints any
uncovered lines, and exits non-zero if line or branch coverage is below 100%. (`coverlet.collector` does not work with
xunit.v3 4.x; the VSTest bridge was removed.) For an HTML report:
`dotnet tool install -g dotnet-reportgenerator-globaltool` then `reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:coverage`.

### Benchmarks

The test project contains BenchmarkDotNet benchmarks (`Benchmarks/`):

* `AggregationBenchmarks` - sanitize / aggregate cost at 1,000 and 100,000 records, with allocations.
* `PagedFetchBenchmarks` - 40 pages at a simulated 2 ms each, concurrency 1 vs 4 vs 8.

Two always-on tests execute the benchmark methods once and assert correct results, so benchmark code cannot rot. The real
measurement run is opt-in because it is slow and only meaningful in Release:

#### Bash
```bash
INVENTORY_RUN_BENCHMARKS=true dotnet test -c Release --filter-method "*RunBenchmarkSuite"
```
#### PowerShell
```PowerShell
$env:INVENTORY_RUN_BENCHMARKS="true"; dotnet test -c Release --filter-method "*RunBenchmarkSuite"
```

## Architecture note

```
HTTP GET --> InventoryEndpoints --> IInventoryReportService --> ReportCache (single-flight, TTL)
                                          |
                                          +--> IInventoryRecordSource  (PagedInventoryRecordSource: pagination + integrity checks)
                                          |        +--> IInventoryPageClient  (InventoryPageClient: the ONLY code that knows HTTP)
                                          |                 +--> HttpClient from IHttpClientFactory + standard resilience handler
                                          +--> RecordSanitizer  (validate/convert raw records)
                                          +--> IInventoryAggregator (pure grouping/totals)
```

* **`InventoryPageClient`** - one page per call; turns every HTTP/JSON outcome into a page or an `InventoryDataException`
  with a coarse failure kind. Typed client via `IHttpClientFactory`; redirects disabled so the API key cannot be forwarded.
* **Resilience pipeline** (`Microsoft.Extensions.Http.Resilience`, "standard handler", configured in `ServiceCollectionExtensions`):
  *retry* with exponential backoff + jitter for 5xx/408/429/transport errors/attempt timeouts (never for other 4xx);
  *attempt timeout* so one hung request cannot stall everything; *total timeout* bounding the whole call including retries;
  *circuit breaker* so a dead upstream fails fast instead of being hammered; a concurrency *rate limiter* guard. I use the library for
  these policies rather than hand-rolling them, but every number is configurable and covered by tests against the real pipeline.
* **`PagedInventoryRecordSource`** - walks the pages (bounded parallelism) and proves completeness and consistency; see below.
  It depends only on `IInventoryPageClient`, so it is tested with an in-memory fake.
* **`RecordSanitizer` + `InventoryAggregator`** - pure functions over plain objects; they have no idea HTTP exists.
* **`ReportCache`** - the dataset is static, so one report is cached (default 5 min) with single-flight rebuilds: concurrent
  callers share one upstream walk, and failures are never cached.
* **Split rationale:** each seam is one reason to change - transport (client), pagination/integrity (source), business rules
  (sanitizer/aggregator), orchestration/caching (service), wire contract (`Contracts`, separate from the domain model).

### How "complete and correct" is enforced

For every page: the echoed page number matches; page size is sane; `totalPages == ceil(totalRecords / pageSize)`; totals do
not drift between pages; and the page holds *exactly* the number of records its position implies (full, except the last).
Those expected counts sum to `totalRecords`, so a dataset that passes every page check is complete. A page that fails is
re-fetched up to `MaxPageAttempts` times (a short/empty 200 is often transient), then the whole retrieval fails with a 502 -
**the service never returns a silently partial result**. If the server clamps our page size, its echoed size is used.

## Assumptions and decisions

| Open question | Decision |
|---|---|
| Duplicate ids | Identical repeats are dropped and counted (`summary.duplicatesDiscarded`). Same id with *different* content fails retrieval (502): we cannot know which is right. Stricter alternative: fail on any duplicate. |
| Invalid records (missing field, negative price/quantity, blank name/category) | Excluded from all figures, counted in `summary.invalidRecordsExcluded` and described in `summary.warnings` (first 50). A structurally broken *page* (bad JSON, wrong types) fails the whole call. |
| Category matching | Trimmed and case-insensitive (`electronics` = `Electronics `); display name is the spelling used by the lowest-id record. |
| Money | `decimal` throughout; each category total rounded to 2 dp, half away from zero. |
| Most recently updated, ties | Newest `lastUpdated`; ties go to the lowest id (deterministic). |
| Timestamps | Converted to UTC and serialised with a `Z` suffix. |
| Ordering | Categories alphabetical (ignoring case), items by id. |
| Zero-quantity records | Excluded before grouping, so they affect no figure (not count, value, or "most recent") and a category with only zero-stock records is omitted. They are counted in the summary. |
| Empty upstream dataset | Valid: `{"categories": []}`. |
| Page size | Requests 25 (the maximum) to minimise calls; honours a smaller size if the server insists. |
| API key header | Not specified in the brief; configurable (`ApiKeyHeaderName`, default `X-Api-Key`). |

### Deviations from the suggested response shape

* An extra top-level `summary` object, so exclusions and discarded duplicates are visible to the caller instead of silent.
* Failures use `application/problem+json` with 502/503/504 (a 200 with an empty list would hide an outage).

## Known limitations / what I would do next

* **Packages:** versions in `Directory.Packages.props` were the newest I could confirm when authoring (xunit.v3 **4.0.3** - the 4.0.x
  line; set `4.0.0` there if you need that exact patch); run `dotnet package list --outdated` to pick up anything newer, and
  bump the xunit.v3 / Testing Platform / CodeCoverage packages **together**.
* Retry honours exponential backoff but **not the `Retry-After` header**.
* Resilience settings are read once at startup (changing them needs a restart); other settings use `IOptions`.
* The report cache is in-process and single-entry; with several instances each would walk the upstream. A distributed cache
  or conditional requests (ETag) would be next if the dataset were large or changing.
* The service's own endpoint has no authentication, rate limiting or HTTPS redirection - out of scope here, required before real exposure.
* No OpenAPI document or health endpoint; no OpenTelemetry beyond what the resilience/HTTP libraries emit by default.
* A permanently inconsistent upstream (e.g. persistently overlapping pages) yields a 502, not a best-effort partial answer, by design.
* A `lastUpdated` without a UTC offset is outside the documented contract; `System.Text.Json` behaviour applies.
* Coverage is gated at 100% line/branch for the production assembly via `scripts/coverage.ps1`; compiler-generated async state
  machine branches can occasionally make the branch figure differ slightly between tool versions - the script prints exactly what is uncovered.
