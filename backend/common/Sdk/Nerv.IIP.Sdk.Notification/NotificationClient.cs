// BLOCKED: NotificationClient requires Nerv.IIP.Sdk.Core to expose a request
// context type for organization/environment/correlation/idempotency headers and
// a PlatformApiClient.CreateRequest helper. The current Sdk.Core project only
// exposes PlatformApiOptions, PlatformApiError, and PlatformApiResult<T>, so this
// SDK intentionally does not invent a local Core API in Task 1.
