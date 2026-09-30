# Changelog

All notable changes to the `GenderAPI` NuGet package are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-09-30

### Breaking

- First published release of the C#/.NET client, built for the GenderAPI.io **V2** API
  (`https://api.genderapi.io/api/v2`). It does not speak the V1 API; see "Migrating from 1.x" in
  the README. V1 is in maintenance; the `v1` branch is kept for layout consistency.

### Added

- `GenderApiClient` / `IGenderApiClient` with async, cancellable methods:
  `GenderAsync`, `NameAsync`, `EmailAsync`, `UsernameAsync` (`POST /gender`),
  `GenderBatchAsync` (`POST /gender/batch`), `UsageAsync` (`GET /usage`),
  `ValidatePhoneAsync` (`POST /phone/validate`), `CapabilitiesAsync` (`GET /`) and
  `ErrorCatalogAsync` (`GET /errors`).
- Typed response models (`Prediction`, `Meta`, `Usage`, `Access`, `BatchItemResult`,
  `BatchSummary`, `Problem`, ...) that keep every field, preserve unknown fields in
  `ExtensionData` and keep enumerated values as strings.
- Structured errors: `GenderApiException` with status, code, title, detail, action, validation
  pointers, request id, `Retry-After`, billing status, batch data and raw body; plus
  `GenderApiTransportException`, `GenderApiTimeoutException`, `GenderApiRedirectException` and
  `GenderApiValidationException` (local validation, no request sent).
- Safety defaults: no automatic retries, no redirects, 10-second timeout, HTTPS-only base URL
  (plain HTTP only for localhost test servers), Bearer authentication only, no request at
  construction, optional injected `HttpClient`.
- Works without an API key (the server may apply its shared IP trial).
- Targets `net8.0` and `netstandard2.0`.
