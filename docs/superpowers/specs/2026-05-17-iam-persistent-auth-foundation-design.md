# IAM Persistent Auth Foundation Design

## Context

The sixth stage made AppHub and Ops the reference services for schema governance: service-owned schemas, EF migrations, metadata comments, schema catalog entries, PostgreSQL profile tests and reusable schema convention tests. IAM is the next platform capability that owns long-lived security data, so it should enter that same persistence baseline before Gateway-wide authorization, File Storage authorization, Ops approvals or console login UI are added.

IAM already has an early in-memory skeleton for login, refresh, logout, `/me`, users, roles, sessions and Connector Host credential validation. That skeleton is useful for contract exploration, but it is not a durable security foundation: passwords are hashed with a simple SHA-256 helper, access tokens are base64 session strings, refresh token state is in memory, placeholder management endpoints do not write real facts, and there is no migration, catalog or schema convention gate for IAM tables.

This phase upgrades IAM into a persistent backend foundation while intentionally keeping the scope narrower than a full product authorization rollout. It must not introduce new console pages, Gateway-wide bearer policies, OAuth/OIDC, SSO, MFA, complex ABAC, private deployment bundles or customer release scripts.

## Recommended Approach

Use a backend-only IAM persistence slice that keeps the existing IAM HTTP surface recognizable but replaces the in-memory security facts with CleanDDD aggregates, PostgreSQL persistence and explicit seed behavior. Preserve the InMemory profile for early vertical-slice scripts, and add a PostgreSQL profile that follows the AppHub/Ops conventions.

Alternatives considered:

1. Add only IAM schema and migrations. This would satisfy part of the persistence baseline, but it would not prove login, refresh token rotation, revocation and seed behavior against persistent state.
2. Build the full authorization chain now: IAM persistence, Gateway bearer policies, permission checks, Connector Host token exchange and console login. This is closer to the final product, but it combines several independent risk areas and would force frontend Design System work too early.
3. Build the IAM persistent auth foundation first. This produces a durable security backend and a reusable pattern for FileStorage and later services while keeping Gateway and frontend changes for separate specs.

The third option is the selected design.

## Scope

In scope:

1. Keep IAM as a three-project service under `backend/services/Iam`.
2. Add IAM Domain aggregates or focused aggregate roots for users, roles, memberships, user sessions and Connector Host credentials.
3. Add IAM Infrastructure PostgreSQL profile with `ApplicationDbContext`, entity configurations, repositories, migration runner and EF migrations under the `iam` schema.
4. Configure PostgreSQL `__EFMigrationsHistory` inside the `iam` schema.
5. Keep the existing InMemory profile as the default for current early vertical-slice scripts.
6. Replace the in-memory login path in PostgreSQL mode with persistent users, password hashes, user sessions and refresh token rotation.
7. Use ASP.NET Core security primitives for password hashing and JWT bearer token creation/validation through thin IAM-specific adapters.
8. Add idempotent local/dev seed behavior for the initial platform administrator, platform administrator role, seed permissions and local Connector Host credential.
9. Add IAM schema convention tests using `Nerv.IIP.Testing.EntityFramework`.
10. Add PostgreSQL profile tests proving IAM can migrate an empty database and run login, refresh, revoke and Connector Host credential validation against persisted state.
11. Update IAM architecture docs, schema catalog, implementation readiness and README state.

Out of scope:

1. Gateway-wide authentication and permission policies.
2. Console login UI, navigation, pages, design tokens or component changes.
3. OAuth2/OIDC authorization server behavior, consent pages or third-party app marketplace.
4. SSO, MFA, WebAuthn, device binding, DPoP or mTLS.
5. Complex ABAC, cross-organization delegation and temporary grants.
6. Full user/role management workflows beyond the minimal endpoints needed to prove persistent facts.
7. Customer release migration bundle, installer integration, backup/restore rehearsal or production seed operator UI.
8. GaussDB, DMDB or other database profile validation.

## Requirements Analysis

### Stakeholders

| Role | Goal/Pain | Permissions/Limitations | Notes |
| --- | --- | --- | --- |
| Platform administrator | Can sign in, refresh a session, inspect basic IAM facts and revoke sessions. | Uses seeded admin account in this phase; full role management UI is not included. | Represents the first human principal. |
| IAM service | Owns user, role, membership, session and credential security facts. | Must not rely on another service for identity truth. | Provides the persistence baseline for later authorization. |
| Connector Host | Needs a machine credential that can be validated against organization/environment scope. | Receives only connector principal validation in this phase; full token exchange is later. | Keeps current Connector Host skeleton compatible. |
| Future Gateway/Console | Needs stable IAM token and permission facts to consume later. | Does not get full enforcement in this phase. | This phase prepares the backend contract and data model. |
| Release/operator | Needs migration and seed behavior that can later be wrapped in release scripts. | Customer release scripting is later. | Local/dev seed must be explicit and diagnosable. |

### Requirement Items

| ID | Scenario | Stakeholder/Object | Business Entity | Operation Type | Constraints/Prerequisites | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| IAM-R1 | Seed initial organization, environment, admin role, admin user and local Connector Host credential. | Release/operator, IAM service | User, Role, Membership, ConnectorHostCredential | Create/seed | Seed must be idempotent and avoid logging secrets. | Local/dev defaults may exist only with clear config names. |
| IAM-R2 | Platform admin logs in with login name and password. | Platform administrator | User, UserSession | Create | Disabled or deleted users cannot log in; password hash uses ASP.NET Core hasher. | Returns JWT access token, refresh token and session id. |
| IAM-R3 | Platform admin refreshes a session. | Platform administrator | UserSession | Modify/create | Refresh token is stored only as a hash; rotation invalidates the previous refresh token. | Stale access tokens are rejected when security or permission version changes. |
| IAM-R4 | Platform admin logs out or a session is revoked. | Platform administrator, IAM service | UserSession | Modify/close | Revoked sessions cannot refresh; `/me` rejects revoked session tokens. | Revocation reason is recorded where available. |
| IAM-R5 | Platform admin queries self profile. | Platform administrator | User, Membership, Role | View | Requires a valid bearer token. | Response contains user identity and basic organization/environment context. |
| IAM-R6 | Platform admin lists users, roles and sessions. | Platform administrator | User, Role, UserSession | View | In this phase, endpoints can remain minimal and admin-oriented. | Full permission policy enforcement is later. |
| IAM-R7 | Connector Host credential is validated. | Connector Host | ConnectorHostCredential | View/validate | Secret is stored only as a hash; validity window and organization/environment scope are checked. | Full Connector Host token exchange is later. |
| IAM-R8 | IAM tables are migrated from an empty PostgreSQL database. | Release/operator | All persistent IAM entities | Async/setup | Uses EF migrations, not `EnsureCreated()`. | Tests own disposable verification database. |
| IAM-R9 | IAM schema follows database conventions. | IAM service, future agents | All persistent IAM entities | Validate | Business tables and columns have comments; string IDs have length and generation rules; history table is in `iam`. | Reuses schema convention helper. |

### Business Entity View

| Business Entity | Covers Requirements | Main Responsibilities/Rules | Key Input/Output |
| --- | --- | --- | --- |
| User | IAM-R1, IAM-R2, IAM-R3, IAM-R4, IAM-R5, IAM-R6 | Login identity, password hash, enabled/deleted state, security stamp and permission version. | Login name/password in; user identity and token claims out. |
| Role | IAM-R1, IAM-R5, IAM-R6 | Named permission set. Permission codes come from the documented seed baseline. | Role id/name and permission code list. |
| Membership | IAM-R1, IAM-R5 | User scope in organization/environment and assigned roles. | User id plus organization/environment scope. |
| UserSession | IAM-R2, IAM-R3, IAM-R4, IAM-R5, IAM-R6 | Refresh token hash, access-token session anchor, expiration, revocation and permission-version snapshot. | Session id, refresh token hash, issued/expires/revoked timestamps. |
| ConnectorHostCredential | IAM-R1, IAM-R7 | Machine credential hash, organization/environment scope, capability scope and validity window. | Connector host id/secret in; connector principal out. |
| SeedManifest | IAM-R1, IAM-R8 | Records seed name/version or equivalent idempotency evidence. | Seed execution result and diagnostic metadata. |

### Triggers And Follow-Up Actions

| Trigger | Follow-up Action/Impact | Related Stakeholders | Affected Entity | Notes |
| --- | --- | --- | --- | --- |
| Admin user is seeded | Create role, permission set, membership and initial session-independent user facts. | Release/operator, platform administrator | User, Role, Membership | Seed can be rerun without duplicating rows. |
| Login succeeds | Create user session and issue token pair. | Platform administrator | UserSession | Token issuance is not a domain event that leaves IAM in this phase. |
| Refresh succeeds | Revoke or retire old refresh state, create new refresh token state and issue a new access token. | Platform administrator | UserSession | Rotation failure must not leak which token part was wrong. |
| Session revoked | Future refresh and `/me` calls fail for that session. | Platform administrator, IAM service | UserSession | Existing JWT is rejected through session lookup. |
| User security stamp or permission version changes | Existing access tokens become stale. | IAM service, future Gateway | User, UserSession | Full permission cache invalidation is later. |
| Connector credential is revoked or expires | Connector credential validation fails. | Connector Host | ConnectorHostCredential | Connector Host token exchange remains later. |

## CleanDDD Model

### Aggregates

| Name | Responsibility Summary | Key Invariants |
| --- | --- | --- |
| User | Owns login identity, password hash, enabled state, security stamp and permission version. | Login name is unique; disabled/deleted users cannot create or refresh sessions; password hash is never empty; security stamp changes invalidate access tokens. |
| Role | Owns role name and permission codes. | Role name is unique in platform scope; permission codes must be known seed codes or explicitly added by a later migration. |
| Membership | Connects a user to organization/environment roles. | A user cannot have duplicate membership for the same organization/environment; role ids must be non-empty. |
| UserSession | Owns refresh token state and session lifecycle. | Refresh token hash is never stored as clear text; revoked sessions cannot refresh; expiration is UTC; session id is generated by IAM. |
| ConnectorHostCredential | Owns machine credential hash, validity and scope. | Secret hash is never empty; credential is valid only inside its validity window; connector host id is unique. |
| SeedManifest | Records idempotent seed execution. | Seed name and version are unique; re-running the same seed is safe. |

The implementation may group some small aggregates into focused files, but it should not keep using the current `IamFacts.cs` record-only model as the final persistent domain model.

### Commands

| Name | Aggregate | Input | Behavior/Event | Idempotency |
| --- | --- | --- | --- | --- |
| SeedIamBaselineCommand | User, Role, Membership, ConnectorHostCredential, SeedManifest | Seed config, permission list, admin login/email/password, connector host id/secret | Creates or updates baseline IAM facts. | Idempotent by seed name/version and stable business keys. |
| LoginUserCommand | User, UserSession | Login name, password, client info, ip address | Verifies password and creates session. | Not idempotent; each successful login creates a session. |
| RefreshUserSessionCommand | UserSession, User | Refresh token, client info, ip address | Validates refresh hash, revokes old refresh state and creates rotated token. | Single-use token rotation. |
| RevokeUserSessionCommand | UserSession | Session id, reason | Marks session revoked. | Idempotent by session id. |
| ValidateConnectorHostCredentialCommand | ConnectorHostCredential | Connector host id, secret | Validates secret hash and scope. | Read-like command with no mutation. |

### Queries

| Name | Aggregate | Filter/Sort/Page | Output |
| --- | --- | --- | --- |
| GetCurrentPrincipalQuery | User, Membership, Role, UserSession | Session id from bearer token. | User id, login name, email, principal type, organization/environment scopes and permission version. |
| ListUsersQuery | User | Optional search/status, page number, page size. | Minimal user list DTO. |
| ListRolesQuery | Role | Optional search, page number, page size. | Role id/name and permission codes. |
| ListSessionsQuery | UserSession | Optional user id/revoked status, page number, page size. | Session id, user id, issued/expires/revoked timestamps. |

### Domain Events

| Domain Event | Publisher | Handling Action | External Side Effect |
| --- | --- | --- | --- |
| UserLoggedInDomainEvent | UserSession | Record session-created diagnostics inside IAM if needed. | None in this phase. |
| UserSessionRefreshedDomainEvent | UserSession | Record rotation diagnostics inside IAM if needed. | None in this phase. |
| UserSessionRevokedDomainEvent | UserSession | Future cache invalidation hook. | None in this phase. |
| PermissionSetChangedDomainEvent | Role or User | Future permission cache invalidation hook. | Not wired outside IAM in this phase. |
| ConnectorHostCredentialValidatedDomainEvent | ConnectorHostCredential | Optional diagnostic event only. | None in this phase. |

### API Endpoints

| Method/Path | Command/Query | Authentication/Authorization | Consistency |
| --- | --- | --- | --- |
| `POST /api/iam/v1/auth/login` | LoginUserCommand | Anonymous. | Creates persistent session and returns token pair. |
| `POST /api/iam/v1/auth/refresh` | RefreshUserSessionCommand | Anonymous with refresh token. | Rotates refresh token atomically. |
| `POST /api/iam/v1/auth/logout` | RevokeUserSessionCommand | Bearer token preferred; session id fallback kept only if existing contract requires it. | Idempotent session revoke. |
| `GET /api/iam/v1/me` | GetCurrentPrincipalQuery | Bearer token. | Reads persisted user/session state. |
| `GET /api/iam/v1/users` | ListUsersQuery | Admin-oriented; full permission enforcement later. | Read-only. |
| `GET /api/iam/v1/roles` | ListRolesQuery | Admin-oriented; full permission enforcement later. | Read-only. |
| `GET /api/iam/v1/sessions` | ListSessionsQuery | Admin-oriented; full permission enforcement later. | Read-only. |
| `POST /api/iam/v1/sessions/{sessionId}/revoke` | RevokeUserSessionCommand | Admin-oriented; full permission enforcement later. | Idempotent. |
| `POST /api/iam/v1/connectors/credentials/validate` | ValidateConnectorHostCredentialCommand | Anonymous machine secret validation. | Returns connector principal only. |

Create/update user and role endpoints can remain placeholders or be deferred unless implementation needs them to prove the slice. If they are retained, they must not claim production management support until they persist real facts and validate inputs.

## Architecture

The target structure mirrors AppHub/Ops:

```text
backend/services/Iam/
  src/
    Nerv.IIP.Iam.Domain/
      AggregatesModel/
        UserAggregate/
        RoleAggregate/
        MembershipAggregate/
        UserSessionAggregate/
        ConnectorHostCredentialAggregate/
      DomainEvents/
    Nerv.IIP.Iam.Infrastructure/
      ApplicationDbContext.cs
      IamPersistenceServiceCollectionExtensions.cs
      IamDatabaseMigrationRunner.cs
      EntityConfigurations/
      Repositories/
      Migrations/
    Nerv.IIP.Iam.Web/
      Application/
        Commands/
        Queries/
        Auth/
        Seed/
      Endpoints/
  tests/
    Nerv.IIP.Iam.Web.Tests/
```

`Program.cs` should register FastEndpoints, caching, observability and the selected persistence profile. The default profile remains `InMemory`; PostgreSQL is selected with `Persistence:Provider=PostgreSQL` and `ConnectionStrings:IamDb`.

The PostgreSQL profile registers:

1. `ApplicationDbContext` with `UseNpgsql(..., npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "iam"))`;
2. repositories and unit of work;
3. password hashing adapter;
4. JWT token issuer/validator;
5. seed runner or seed command handler that is explicit in local/dev tests.

The InMemory profile should continue to serve the existing early skeleton. It can be refactored behind interfaces so endpoint behavior is consistent, but it should not be expanded into a second production implementation.

## Data Model

Schema: `iam`

Initial business tables:

| Table | Kind | Purpose |
| --- | --- | --- |
| `organizations` | business | Minimal platform organization facts needed by IAM seed and membership scope. |
| `environments` | business | Minimal environment facts under an organization. |
| `users` | business | Login identities, password hash, status, security stamp and permission version. |
| `roles` | business | Role metadata. |
| `role_permissions` | business | Permission codes assigned to roles. |
| `memberships` | business | User role assignments scoped by organization/environment. |
| `user_sessions` | business | Refresh token hash, session state, expiration and revocation facts. |
| `connector_host_credentials` | business | Machine credential hash, organization/environment scope, capability scope and validity. |
| `seed_manifests` | business | Idempotent seed execution records. |
| `cap_published_messages` | system | CAP published message outbox if IAM wires CAP in this phase or later. |
| `cap_received_messages` | system | CAP received message inbox if IAM wires CAP in this phase or later. |
| `cap_locks` | system | CAP distributed lock table if IAM wires CAP in this phase or later. |
| `__EFMigrationsHistory` | system | EF migration history in the `iam` schema. |

If CAP is not wired for IAM in this phase, CAP tables should not be created prematurely. The catalog should reflect only tables actually created by the migration.

Important data rules:

1. Password hash and refresh token hash are never logged and never returned by API.
2. Connector Host secret hash is never logged and never returned by API.
3. Login name and email have stable maximum lengths and uniqueness constraints.
4. String identifiers have `ValueGeneratedNever()` and explicit maximum lengths.
5. JSON/text columns are avoided in the first IAM schema unless a specific compatibility reason is documented.
6. All timestamps are UTC and comments must state that.

## Token And Security Flow

Access token:

1. Use JWT Bearer.
2. Claims include `sub`, `sessionId`, `principalType=user`, `organizationId`, `environmentId`, `securityStamp`, `permissionVersion`, `iat` and `jti`.
3. The JWT lifetime should be short enough for development safety; exact default can be configured, for example 15 minutes.
4. Service-side validation for `/me` and IAM endpoints must check the persisted session, not only JWT signature.

Refresh token:

1. Generated from cryptographically strong random bytes.
2. Returned to the caller only once.
3. Stored as hash only.
4. Rotated on refresh.
5. Old refresh token fails after successful rotation.
6. Disabled users and revoked sessions cannot refresh.

Password:

1. Use `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>` or a thin wrapper around it.
2. Store the hasher output, not SHA-256.
3. Support future rehash detection if the hasher reports a rehash-needed result.

Seed secrets:

1. Local/dev defaults may exist for repeatable tests and scripts.
2. Non-local release flows must provide admin password and connector secret through configuration or secure input.
3. Secrets must never be written to logs, schema catalog or migration files.

## Error Handling

Authentication errors return 401 with a stable problem shape and no credential detail leakage:

1. Invalid login name or password: generic unauthorized response.
2. Invalid refresh token: generic unauthorized response.
3. Revoked session: unauthorized response.
4. Expired session or access token: unauthorized response.
5. Disabled user: unauthorized response.
6. Invalid Connector Host credential: unauthorized response.

Validation errors return 400 with field-level information when the input is not a secret, for example malformed paging values. Secret-related errors should remain generic.

Persistence errors should fail loudly in tests and be logged with correlation id in runtime logs. Logs must include service name, correlation id and operation name but not tokens, password, refresh token, connector secret or full connection string.

Seed errors fail startup or the explicit seed command in local/dev verification. A seed step should report seed name, seed version, owner service, result and correlation id.

## Testing

The implementation should be test-first:

1. Add failing tests for PostgreSQL login, refresh token rotation, logout/revoke, `/me` and Connector Host credential validation using a disposable IAM database.
2. Add a failing IAM schema convention test before adding metadata.
3. Add IAM `ApplicationDbContext`, entity configurations, migration runner and migration.
4. Add idempotent seed behavior and verify it can run twice without duplicate users, roles or credentials.
5. Verify old refresh tokens fail after rotation.
6. Verify revoked sessions fail for refresh and `/me`.
7. Verify disabled users cannot log in or refresh.
8. Verify password hashes are not SHA-256 plain hashes and are never returned.
9. Run targeted IAM tests.
10. Run `dotnet test backend/Nerv.IIP.sln --no-restore`.
11. Run `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln` if Connector Host contracts or SDK auth behavior changes.
12. Run `git diff --check`.

A new script such as `scripts/verify-iam-persistent-auth-foundation.ps1` is useful if the PostgreSQL test setup needs Docker orchestration following the fifth-stage persistence script pattern. If targeted IAM PostgreSQL tests can own their disposable database through existing local infrastructure, the script can be added in the implementation plan rather than forced by this design.

Frontend gates are not required unless Gateway OpenAPI or generated frontend client inputs change. This phase should not change console pages or design assets.

## Documentation

Update these documents when implementing:

1. `README.md`: current status and next-stage summary.
2. `docs/architecture/implementation-readiness.md`: IAM persistent auth foundation status and usage.
3. `docs/architecture/iam-authentication-baseline.md`: note what is now implemented versus still future.
4. `docs/architecture/database-schema-catalog.md`: add IAM schema tables and known gaps.
5. `docs/architecture/database-schema-conventions.md`: no broad rewrite, but add IAM to the list of services covered by convention tests once implemented.
6. `docs/architecture/technology-stack-references.md`: update only if a new long-lived dependency is introduced.

No new ADR is required. ADR 0009 already covers service-owned migrations, release/seed strategy and schema catalog obligations. This phase implements that accepted decision for IAM.

## Completion Definition

The phase is ready to close when:

1. IAM has PostgreSQL `ApplicationDbContext`, entity configurations and committed EF migrations in the `iam` schema.
2. IAM configures `__EFMigrationsHistory` inside the `iam` schema.
3. IAM business tables and columns have comments that pass schema convention tests.
4. IAM schema catalog entries match the migration and entity configurations.
5. The initial admin, role, permission set, membership and local Connector Host credential can be seeded idempotently.
6. Persistent login returns a JWT access token, refresh token and session id.
7. Refresh token rotation persists only hashes and rejects the old refresh token.
8. Logout/session revoke prevents refresh and `/me`.
9. Disabled users cannot log in or refresh.
10. Connector Host credential validation works against persisted hashed credentials.
11. Targeted IAM PostgreSQL tests pass.
12. Backend solution tests pass.
13. Existing InMemory behavior remains available for early scripts.
14. No Gateway-wide authorization, console login UI or Design System work is introduced.
