# GenderAPI.io V2 client for C#/.NET

Official GenderAPI.io V2 client for C#/.NET. It infers gender from names, email
addresses and usernames, and validates phone numbers, through the
[GenderAPI.io V2 API](https://www.genderapi.io/api-documentation).

- NuGet package: `GenderAPI` (version 2.x targets the V2 API)
- Root namespace: `GenderApi`
- Targets: `net8.0` and `netstandard2.0` (.NET Framework 4.6.1+, .NET Core 2.0+, Mono, Unity)
- Dependencies: none on `net8.0`; `System.Text.Json` on `netstandard2.0`

Results are inferences, not facts about a person, and they can be `unknown`
(`gender: null`). They never verify anyone's identity.

> **1.x / V1:** there is no 1.x package of this client. V1 users can keep calling the V1 HTTP API
> directly ([V1 documentation](https://www.genderapi.io/api-documentation/v1)); V1 stays available,
> with no deprecation or shutdown planned. The `v1` branch is kept for layout consistency with the
> other GenderAPI.io SDKs (it contains no V1 client). New integrations should use this 2.x client
> and the V2 API.

## Install

```bash
dotnet add package GenderAPI
```

## Quick start

Keep the API key on the server. Set it in the environment (`GENDERAPI_API_KEY`) or pass it
to the constructor. Creating the client sends no request.

```csharp
using GenderApi;

using var client = new GenderApiClient(); // reads GENDERAPI_API_KEY

// Single prediction (POST /gender)
GenderResponse res = await client.NameAsync("Alice Example", country: "US");
Prediction p = res.Data!;
if (p.IsIdentified)
{
    Console.WriteLine($"{p.Gender} ({p.ConfidenceKind} {p.Confidence})");
}
else
{
    Console.WriteLine($"unknown: {p.Reason}"); // successful, and billable
}
Console.WriteLine($"charged {res.Meta!.Usage!.ChargedCredits}, remaining {res.Meta.Usage.RemainingCredits}");

// Email and username
await client.EmailAsync("alice@example.com");
await client.UsernameAsync("prenses", country: "TR", forceToGenderize: true);

// Batch (POST /gender/batch), 1-50 items; input types may be mixed
BatchResponse batch = await client.GenderBatchAsync(new[]
{
    GenderRequest.ForName("Alice", "US", id: "row-1"),
    GenderRequest.ForEmail("bob@example.com", id: "row-2"),
    GenderRequest.ForUsername("prenses", aiMode: AiMode.Fallback, forceToGenderize: true, id: "row-3"),
});
foreach (BatchItemResult item in batch.Data!)
{
    if (item.IsSuccess) Console.WriteLine($"{item.Id}: {item.Data!.Gender ?? "unknown"}");
    else Console.WriteLine($"{item.Id}: failed with {item.Error!.Code}");
}
Console.WriteLine($"failed: {batch.Meta!.Summary!.Failed}");

// Balance (GET /usage, free)
UsageResponse usage = await client.UsageAsync();
Console.WriteLine(usage.Data!.RemainingCredits);

// Phone validation (POST /phone/validate)
PhoneValidationResponse phone = await client.ValidatePhoneAsync("+1 202 555 0100", "US");
Console.WriteLine(phone.Data!.Valid);
```

Every method is `async` and accepts a `CancellationToken`.

### Public API

| Method | HTTP | Notes |
| --- | --- | --- |
| `GenderAsync(GenderRequest)` / `GenderAsync(type, value, country?, aiMode?, forceToGenderize?, id?)` | `POST /gender` | one billable prediction |
| `NameAsync(...)`, `EmailAsync(...)`, `UsernameAsync(...)` | `POST /gender` | `type` preset |
| `GenderBatchAsync(IEnumerable<GenderRequest>)` | `POST /gender/batch` | 1-50 items (10 on the IP trial; the server decides) |
| `UsageAsync()` | `GET /usage` | free |
| `ValidatePhoneAsync(number, country?)` | `POST /phone/validate` | billable |
| `CapabilitiesAsync()` | `GET /` | no key sent; returns `JsonElement` |
| `ErrorCatalogAsync()` | `GET /errors` | no key sent |

`GenderApiClient` implements `IGenderApiClient` (for dependency injection and test doubles) and
`IDisposable`. Instances are thread-safe; create one and reuse it.

## Options

```csharp
var client = new GenderApiClient(new GenderApiClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("GENDERAPI_API_KEY"), // null => read GENDERAPI_API_KEY
    Timeout = TimeSpan.FromSeconds(10),                                 // default 10 s
    HttpClient = myHttpClient,                                          // optional; not disposed by the client
    UserAgentSuffix = "my-app/1.0",                                     // optional
    RequireApiKeyAccess = true,                                         // default; see below
    // BaseUrl = "https://api.genderapi.io/api/v2",                     // default; HTTPS only
});
```

| Option | Default | Notes |
| --- | --- | --- |
| `ApiKey` | `GENDERAPI_API_KEY` | sent only as `Authorization: Bearer <key>`, never in a URL, never logged |
| `Timeout` | 10 seconds | per request; `Timeout.InfiniteTimeSpan` disables it |
| `HttpClient` | client-owned, redirects disabled | configure your handler with `AllowAutoRedirect = false`; the client rejects redirected responses anyway |
| `BaseUrl` | `https://api.genderapi.io/api/v2` | must be HTTPS; plain HTTP is accepted only for `localhost`, `127.0.0.1` and `[::1]` (local test servers) |
| `UserAgentSuffix` | none | appended to `genderapi-dotnet/2.0.0` |
| `RequireApiKeyAccess` | `true` | only effective when a key is configured: a successful response whose `meta.access.mode` is present and is not `api_key` (for example `ip_trial` because the key was not recognized) throws `GenderApiAccessModeException`. Set to `false` to return such responses normally. Never applies to `CapabilitiesAsync()` or `ErrorCatalogAsync()` |

With `IHttpClientFactory`:

```csharp
services.AddHttpClient("genderapi")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
services.AddSingleton<IGenderApiClient>(sp => new GenderApiClient(new GenderApiClientOptions
{
    HttpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("genderapi"),
}));
```

### Request fields

`GenderRequest` maps one-to-one to the wire fields; the names on the wire are exact.

| Property | Wire field | Rules (checked locally, no request is sent when invalid) |
| --- | --- | --- |
| `Type` | `type` | `GenderInputType.Name`, `Email` or `Username` |
| `Value` | `value` | 1-254 characters, not blank, no control characters |
| `Country` | `country` | optional, two uppercase letters (ISO 3166-1 alpha-2); omit when unknown |
| `AiMode` | `options.ai_mode` | optional `Off`, `Fallback` or `Always`; the server defaults to `fallback` for single requests and `off` for batch items |
| `ForceToGenderize` | `forceToGenderize` | optional; dataset first (1 credit), then nickname-aware AI (2 credits total); cannot be combined with `Off` or `Always` |
| `Id` | `id` | optional, 1-64 characters, unique within a batch |

The API performs the authoritative validation (email syntax, country list, trial batch limit) and
answers with a 422 Problem when it rejects a field.

## Response fields

Every response is the parsed V2 JSON with `Data` and `Meta`. All fields are kept: unknown fields
are preserved in `ExtensionData`, enumerated values are plain strings (so new values never break
parsing), and `RawJson` holds the body as received. `RequestId`, `StatusCode` are set from the HTTP
response.

`Prediction` (`Data` of a single request, and of each successful batch item):

| Property | Wire field | Meaning |
| --- | --- | --- |
| `Gender` | `gender` | `male`, `female` or `null` |
| `ResultStatus` | `result_status` | `identified` or `unknown` (`IsIdentified`, `IsUnknown`) |
| `Reason` | `reason` | `null` when identified; `not_found`, `no_name_candidate`, `ambiguous`, `insufficient_evidence` |
| `Confidence` | `confidence` | 0-1 score, `null` when gender is null. Not a calibrated probability, not a measured accuracy, and not a percentage |
| `ConfidenceKind` | `confidence_kind` | `observed_frequency` (dataset) or `model_reported` (AI) |
| `SampleCount` | `sample_count` | dataset sample count; `null` for AI and for no match |
| `Source` | `source` | `dataset`, `ai` or `none` |
| `Name` | `name` | returned dataset name or extracted given name; can be `null` |
| `Country`, `CountrySource` | `country`, `country_source` | `dataset`, `ai_association` or `null`; neither establishes nationality, residence or ethnicity |
| `Match` | `match` | `Name`, `Method` (`normalized`, `token`, `substring`, `model_inference`), `Scope` (`country`, `global`), `Country` |
| `Input` | `input` | the echoed input |

`Meta`:

| Property | Wire field | Meaning |
| --- | --- | --- |
| `RequestId` | `meta.request_id` | id of this HTTP attempt (also `X-Request-ID`) |
| `Access.Mode` | `meta.access.mode` | `api_key`, `ip_trial` or `unauthenticated`; `Access.Reason` explains a trial |
| `Usage.BillingStatus` | `meta.usage.billing_status` | `not_charged`, `confirmed` or `unconfirmed` |
| `Usage.ChargedCredits` | `meta.usage.charged_credits` | net charge; `null` when unconfirmed |
| `Usage.RemainingCredits` | `meta.usage.remaining_credits` | balance at completion; can be negative, `null` when unknown |
| `Usage.ResetsAt`, `Limit`, `PeriodSeconds` | `meta.usage.*` | IP-trial reset time and quota, `null` otherwise |
| `Summary` | `meta.summary` | batch only: `Total`, `Succeeded`, `Identified`, `Unknown`, `Failed` |

`BatchItemResult`: `Index`, `Id`, `ChargedCredits`, and exactly one of `Data` (a `Prediction`) or
`Error` (a `Problem`). `BatchResponse.SucceededItems`, `FailedItems` and `HasFailures` help to find
failed items.

## Errors

| Exception | When |
| --- | --- |
| `GenderApiValidationException` (an `ArgumentException`) | invalid input detected locally; **no request was sent** |
| `GenderApiException` | HTTP status >= 400 or an unexpected response |
| `GenderApiTransportException` (a `GenderApiException`) | no usable response (network failure, non-JSON or invalid JSON body); billing outcome unknown |
| `GenderApiTimeoutException` (a `GenderApiTransportException`) | the configured timeout elapsed; billing outcome unknown |
| `GenderApiRedirectException` (a `GenderApiException`) | the server answered 3xx; redirects are never followed |
| `GenderApiAccessModeException` (a `GenderApiException`) | a key is configured (and `RequireApiKeyAccess` is on) but a 2xx response reports another `meta.access.mode`, usually `ip_trial` |

Caller cancellation through the `CancellationToken` surfaces as `OperationCanceledException`.

`GenderApiAccessModeException` (`Code` = `unexpected_access_mode`) is raised after the request has
already been processed, so trial credits may have been used. It is not retried. `Result` holds the
complete typed response the method would have returned (`GenderResponse`, `BatchResponse`,
`UsageResponse` or `PhoneValidationResponse`, typed as `object`), `AccessMode` and `AccessReason`
hold `meta.access.mode` / `meta.access.reason`, and `StatusCode`, `RequestId`, `Meta`,
`BillingStatus` and `RawBody` (the raw JSON) are set as on other errors. The key is never included
in the message. Check the configured key, or set `RequireApiKeyAccess = false` to accept trial
responses.

`GenderApiException` exposes `StatusCode`, `Code` (stable machine code), `Title`, `Detail`,
`Action`, `Errors` (validation pointers), `RequestId` (from the body, `meta.request_id` or the
`X-Request-ID` header), `RetryAfter` / `RetryAfterRaw` (from the `Retry-After` header),
`BillingStatus`, `Meta`, `Problem` (the parsed body), `BatchData` and `RawBody`.
Match on `Code`, not on `Detail` text. The full list of codes is published at
`https://api.genderapi.io/api/v2/errors` (`ErrorCatalogAsync()`).

```csharp
try
{
    await client.NameAsync("Alice");
}
catch (GenderApiValidationException ex)
{
    // Fix the input; nothing was sent.
}
catch (GenderApiAccessModeException ex)
{
    // Processed under ex.AccessMode (e.g. ip_trial); the result is in ex.Result. Check the API key.
    var res = (GenderResponse)ex.Result;
}
catch (GenderApiException ex) when (ex.StatusCode == 429)
{
    // Wait at least ex.RetryAfter. A later request is a new operation and is billed normally.
}
catch (GenderApiException ex) when (ex.BillingStatus == "unconfirmed")
{
    // Do not resubmit. Contact support with ex.RequestId.
}
catch (GenderApiTransportException ex)
{
    // The request may have completed and been billed. Check UsageAsync() before resubmitting.
}
```

HTTP statuses: 400 invalid request, 401 invalid key, 403 insufficient credits or account
restriction, 422 invalid fields, 429 throttling, 502 provider failure, 503 dependency unavailable or
billing reconciliation required, 504 prediction timeout.

When **all** executed batch items fail, the API answers with an error status; the exception keeps
the item outcomes in `BatchData`. A **partial** batch success is not an exception: the method
returns normally and the failed items are in `FailedItems`.

## Billing and retries

- The client **never retries** a request, including on 429 or on a timeout. A lost response can
  still mean a completed, billed request.
- Redirects are never followed, so the key is never forwarded to another host.
- Single requests default to AI `fallback` (1 credit total). `AiMode.Always` costs 2.
  `ForceToGenderize` costs 1 on a dataset match and 2 when AI is used. Batch items default to AI `off`.
- Unknown results are successful and billable. Prediction failures are refunded unless billing
  cannot be confirmed.
- A positive starting balance is enough, so a 2-credit request can leave the balance at -1.
- On a partial batch success, retry only the failed items, and only once billing is confirmed.
  Resubmitting successful items charges them again.
- On `unconfirmed` billing or `billing_reconciliation_required`, contact support with the
  `RequestId` before retrying.
- Use `UsageAsync()` (free) to read the current balance.

## IP trial (no key)

A client without a key still works: the server applies its shared IP trial (10 credits per 24
hours) and reports it in `Meta.Access.Mode == "ip_trial"` and `Meta.Usage.ResetsAt`. The client has
no trial logic of its own; the server decides. A missing or unknown key can fall back to the trial,
while disabled, expired or restricted keys do not.

When a key **is** configured, a response reporting `ip_trial` means the key was not used; by default
the client throws `GenderApiAccessModeException` (the request was processed and may have used trial
credits; the full result is in `Result`). Set `RequireApiKeyAccess = false` to accept it instead.

## Server-side only

Use this package on servers, workers and back-end jobs. Do not ship the API key in browser (Blazor
WebAssembly), desktop or mobile applications: anyone can extract it. Call your own back end from
those clients instead. Response bodies (`RawJson`, `RawBody`) can contain the personal values you
submitted; do not log them wholesale.

## Migrating from 1.x (V1)

There is no 1.x package of this client. V1 users can keep calling the V1 HTTP API directly
([V1 documentation](https://www.genderapi.io/api-documentation/v1)); V1 stays available, with no
deprecation or shutdown planned.

The 2.0.0 package is a new client for the V2 API; changing the base URL alone does not migrate a V1
integration. The same API key and credit balance work with V2.

| V1 | V2 (this client) |
| --- | --- |
| `/api`, `/api/email`, `/api/username` | `POST /api/v2/gender` with `type` = `name`, `email` or `username` (`GenderAsync`, `NameAsync`, `EmailAsync`, `UsernameAsync`) |
| `/api/name/multi/country`, `/api/email/multi/country`, `/api/username/multi/country` | `POST /api/v2/gender/batch` with `items`; types may be mixed (`GenderBatchAsync`) |
| `/api/phone` with `number` and `address` | `POST /api/v2/phone/validate` with `number` and `country` (`ValidatePhoneAsync`) |
| `askToAI` | `options.ai_mode` (`AiMode.Fallback`; single requests already default to fallback, `AiMode.Always` forces AI) |
| `forceToGenderize` | same field, now for all three types: dataset first, then nickname-aware AI |
| flat response fields | `Data` for the result, `Meta` for request, access and billing |
| `probability` (percentage) | `Confidence` on a 0-1 scale plus `ConfidenceKind`; AI scores are not calibrated probabilities |
| `total_names` | `Data.SampleCount` (nullable) |
| `used_credits` / `remaining_credits` | `Meta.Usage.ChargedCredits` / `Meta.Usage.RemainingCredits`, plus `BillingStatus` |
| `status` / `errno` | HTTP status plus Problem Details `Code` and `Action` (`GenderApiException`) |
| retrying POST requests | each retry is a new, billed operation; the client never retries |

## Documentation

- API documentation: https://www.genderapi.io/api-documentation
- V2 guides: [authentication](https://www.genderapi.io/docs/v2/authentication),
  [request parameters](https://www.genderapi.io/docs/v2/request-parameters),
  [responses](https://www.genderapi.io/docs/v2/responses),
  [batch](https://www.genderapi.io/docs/v2/batch),
  [AI options](https://www.genderapi.io/docs/v2/ai-options),
  [credits and usage](https://www.genderapi.io/docs/v2/credits-and-usage),
  [errors and retries](https://www.genderapi.io/docs/v2/errors-and-retries),
  [phone validation](https://www.genderapi.io/docs/v2/phone-validation),
  [migration from V1](https://www.genderapi.io/docs/v2/migration)
- OpenAPI: https://api.genderapi.io/api/v2/openapi.json
- Error catalog: https://api.genderapi.io/api/v2/errors
- Issues: https://github.com/GenderAPI/genderapi-C/issues

## Development

```bash
dotnet test            # unit tests; stub HTTP handler and a local 127.0.0.1 server, no real API calls
dotnet pack -c Release # writes src/GenderApi/bin/Release/GenderAPI.2.0.0.nupkg
```

Releases are published to NuGet by `.github/workflows/publish.yml` when a `v*` tag is pushed. The
workflow needs the repository secret `NUGET_API_KEY` (a nuget.org API key scoped to push `GenderAPI`).

## License

MIT, see [LICENSE](LICENSE).
