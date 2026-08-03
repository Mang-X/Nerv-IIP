# MAN-662 Task 5 implementation report

## Status

Implemented the Ops IAM client connection/request budgets and deterministic network outcome coverage from `task-5-brief.md`. Scope stayed within the four brief-listed code/test files plus this report. No endpoint, database, OpenAPI, generated client, script, frontend, push, PR, or Linear change was made.

## Delivered behavior

- `OpsIamClientOptions` binds from `Ops:IamClient` with exact defaults:
  - `ConnectTimeout = 250ms`
  - `RequestTimeout = 500ms`
- Startup validates both values are positive.
- The typed IAM validator client sets both `SocketsHttpHandler.ConnectTimeout` and `HttpClient.Timeout`.
- DNS, connection-refused, client-owned request timeout, HTTP 503, HTTP 401, malformed success, and caller cancellation use scripted `HttpMessageHandler` tests only.
- Caller cancellation is identified from `cancellationToken.IsCancellationRequested` and rethrown with `throw;`; a client-owned timeout remains fail-closed as `iam-unavailable`.
- Public reasons remain stable:
  - HTTP 401: `iam-rejected`
  - malformed success: `iam-invalid-response`
  - DNS/refused/client timeout/HTTP 503: `iam-unavailable`
- Warning logs contain only structured `FailureKind` and `StatusCode`; exception messages, request secrets, headers, and response bodies are not logged.
- The Production local-secret test now directly composes `OpsConnectorCredentialValidator` with a Production `IWebHostEnvironment`, configured local secret, and scripted IAM 401. It no longer starts a PostgreSQL/RabbitMQ profile or calls `127.0.0.1:1`/port 1.

## Diagnostic classification evidence

| Scripted outcome | Product result | `NetworkFailureClassifier` / log classification |
| --- | --- | --- |
| `HttpRequestError.NameResolutionError` | `iam-unavailable` | `Dns` / `dns` |
| `HttpRequestError.ConnectionError` + `SocketError.ConnectionRefused` | `iam-unavailable` | `ConnectionRefused` / `connection-refused` |
| Handler blocked until `HttpClient.Timeout` | `iam-unavailable` within the 500ms client budget | `RequestTimeout` / `request-timeout` |
| HTTP 503 with secret response body | `iam-unavailable` | `BusinessError`, status 503 / `business-response` |
| HTTP 401 | `iam-rejected` | rejection branch |
| Caller token cancellation | `OperationCanceledException` propagates | no failure result and no warning log |

The scripted timeout and caller-cancellation cases both surface cancellation exceptions from real `HttpClient` behavior. They are deliberately distinguished using the caller token state, not the exception subtype.

## TDD evidence

### RED

Command:

```bash
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --configuration Release --filter 'FullyQualifiedName~OpsConnectorCredentialValidationTests|FullyQualifiedName~Production_does_not_accept_development_fake_connector_credential'
```

The first test-only run exited 1 because the replaced composition test was missing its Auth namespace import. After correcting that test assembly error without changing production, the repeat exited 1 with `9 failed, 2 passed, total 11`. Failures directly demonstrated:

- named client still used the 100-second default request timeout;
- zero budgets did not fail startup;
- caller cancellation was swallowed into a business result;
- DNS/refused/timeout/503 logs had no classification fields;
- malformed HTTP 200 JSON escaped as `JsonException`;
- client-owned timeout did occur at the scripted 500ms boundary but lacked the required classification.

### GREEN focused

The same focused command exited 0:

```text
Passed: 11, Failed: 0, Skipped: 0, Total: 11
Duration: 859 ms
```

### GREEN complete Ops project

Command:

```bash
dotnet test backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/Nerv.IIP.Ops.Web.Tests.csproj --configuration Release
```

Result exited 0:

```text
Passed: 65, Failed: 0, Skipped: 0, Total: 65
Duration: 3 s
```

`git diff --check` also exited 0.

## Files

- `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Auth/OpsConnectorCredentialValidation.cs`
- `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsConnectorCredentialValidationTests.cs`
- `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

## Commit

This report ships in the Task 5 commit `test: bound and classify ops iam failures`.

## Concerns

None. The complete Ops test project is deterministic and makes no unreachable-port call for this behavior.
