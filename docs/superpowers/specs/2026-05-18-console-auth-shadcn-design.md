# Console Auth And Shadcn-Vue Design

## Purpose

This spec restarts frontend product work after the IAM persistent auth foundation and Gateway-wide permission enforcement stages. It adds the smallest production-shaped Console authentication loop and freezes the first shadcn-vue design-system decision so future UI work has one component source of truth.

The first user-facing outcome is simple: a platform administrator can open the Console, sign in through the Gateway, land on the existing instance workspace, keep API calls authorized with bearer tokens, refresh a saved session on app startup, and sign out.

## Current Context

1. IAM already owns persisted user/session facts, seed admin credentials, JWT access tokens, refresh token rotation, logout/session revoke, `/api/iam/v1/me`, and permission checks.
2. PlatformGateway already protects existing Console APIs by forwarding bearer tokens and permission context to IAM.
3. The Console frontend currently has a Vite/Vue app, Pinia, Pinia Colada, generated Gateway api-client, a minimal app shell, and local `UiButton`, `UiPanel`, and `UiBadge` primitives.
4. `docs/architecture/frontend-design-system-planning.md` blocks new frontend product work until an explicit design-system spec selects registry strategy, token model, icon policy, density, accessibility, theme, migration, and tests.
5. Current `shadcn-vue` project inspection reports Vite + TypeScript, but no `components.json`, Tailwind CSS file, Tailwind config, or initialized shadcn-vue config.

## Decisions

### Architecture

Use a PlatformGateway Console Auth facade.

The frontend calls only PlatformGateway:

```text
POST /api/console/v1/auth/login
POST /api/console/v1/auth/refresh
POST /api/console/v1/auth/logout
GET  /api/console/v1/auth/me
```

Gateway forwards those calls to IAM and does not store or reinterpret identity facts. IAM remains the source of truth for users, sessions, security stamps, permission versions, organization scope, environment scope, and permission codes.

This keeps the Console deployment model simple: one generated OpenAPI contract, one base URL, and one browser-facing API surface.

### Design System

Initialize shadcn-vue directly in the existing frontend workspace.

Use:

1. Official shadcn-vue registry.
2. `nova` preset.
3. Vite template.
4. Reka base components.
5. shadcn-vue semantic tokens and Tailwind integration.
6. `lucide-vue-next` as the icon library unless the CLI-generated project context selects a different icon library during initialization.

The registry source files belong under `frontend/packages/ui`. Console app pages and feature components consume UI through stable package exports instead of scattering registry imports across the app.

### Package Boundaries

Implement this phase without creating `frontend/packages/auth`.

Auth is currently used by one app, so the first implementation stays in `frontend/apps/console/src`:

```text
frontend/apps/console/src/
  api/auth.ts
  components/auth/LoginForm.vue
  composables/useAuthSession.ts
  router/guards/auth.ts
  stores/auth.ts
  pages/login.vue
```

If a second app or package needs the same auth behavior later, extract the stable pieces into `frontend/packages/auth`. That future package should own auth DTO mapping, storage strategy, token refresh orchestration, and app-agnostic route helpers. It should not be created in this phase.

### Old UI Primitives

The local `UiButton`, `UiPanel`, and `UiBadge` primitives are migration scaffolding, not a parallel design system. Once shadcn-vue components are installed and current consumers are migrated, delete unused old primitive files and exports.

## Scope

### In Scope

1. Gateway Console Auth facade for login, refresh, logout, and me.
2. Generated OpenAPI/api-client updates for the new Gateway auth endpoints.
3. shadcn-vue initialization and first component set in `frontend/packages/ui`.
4. Login route and login form.
5. Pinia auth store with session persistence, principal state, pending/error state, and logout cleanup.
6. API transport bearer injection for generated Gateway requests.
7. App startup session restoration using the saved refresh token and `/me` confirmation.
8. Router guards for authenticated and guest-only routes.
9. Existing Console instance and operation pages gated behind authentication.
10. App shell user menu with principal display and sign-out command.
11. Frontend and backend tests for the authentication loop.
12. Documentation updates for frontend design-system status and current implementation readiness.
13. Cleanup of stale documentation discovered during status audit: README worktree wording and schema catalog Gateway permission status.

### Out Of Scope

1. OAuth2/OIDC, SSO, MFA, WebAuthn, enterprise IdP federation, consent pages, and third-party app marketplace.
2. User, role, session, and permission management UI.
3. ABAC rule authoring or runtime policy editor.
4. High-risk Ops approval flows or notification integration.
5. Cookie-based browser auth, CSRF, DPoP, token binding, or mTLS.
6. Multi-tenant branding.
7. Creating `frontend/packages/auth`.
8. Reworking existing instance and operation workflows beyond auth gating and shadcn-vue component migration needed for consistency.

## Backend Design

### Gateway Facade

Add a Gateway auth client that calls IAM:

```text
Console browser -> PlatformGateway /api/console/v1/auth/* -> IAM /api/iam/v1/*
```

The Gateway facade forwards request and response payloads with stable Console operation IDs:

1. `loginConsoleUser`
2. `refreshConsoleSession`
3. `logoutConsoleSession`
4. `getConsolePrincipal`

The facade maps IAM status codes without hiding important auth semantics:

1. Invalid login or refresh token returns `401`.
2. Revoked or expired session returns `401`.
3. IAM unavailable returns `503` from Gateway with a small problem response.
4. Unexpected IAM error returns `502`.
5. Logout returns success if IAM revokes the session; frontend still clears local state if the logout request fails.

Gateway does not reference IAM Domain or Infrastructure. It uses HTTP and shared public DTOs only.

### Contract Shape

Console auth responses expose only what the SPA needs:

```text
accessToken
refreshToken
sessionId
expiresAtUtc
principal
```

The principal contains:

```text
principalId
principalType
loginName
organizationId
environmentId
permissionVersion
```

The contract can map from existing IAM responses; it does not require a new IAM persistence model.

## Frontend Design

### shadcn-vue Initialization

Initialize shadcn-vue from `frontend` with `pnpm dlx shadcn-vue@latest`.

The implementation plan must inspect the generated `components.json` and record:

1. `aliases`
2. `tailwindVersion`
3. `tailwindCssFile`
4. `style`
5. `base`
6. `iconLibrary`
7. `resolvedPaths`

Initial components:

1. `button`
2. `card`
3. `field`
4. `input`
5. `alert`
6. `badge`
7. `separator`
8. `skeleton`
9. `dropdown-menu`
10. `avatar`
11. `sonner`
12. `spinner`
13. `sidebar` if the `AppShell` migration uses the shadcn-vue sidebar primitive; otherwise keep the first app shell as a focused local composition over shadcn tokens.

Use shadcn-vue rules:

1. Forms use `FieldGroup` and `Field`.
2. Card layouts use `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, and `CardFooter`.
3. Loading buttons compose `Spinner` with `disabled`; no fake `isLoading` prop.
4. Status chips use `Badge`; no custom status spans.
5. Alerts use `Alert`; no custom callout markup.
6. Icons in buttons use `data-icon`; no manual icon size classes inside shadcn components.
7. Layout uses `gap-*`; no `space-x-*` or `space-y-*`.
8. Component styling uses semantic tokens and variants, not raw Tailwind color overrides.

### Visual Direction

The Console is an operations-heavy platform surface, not a marketing page. The visual direction is restrained industrial clarity:

1. Dense but readable workspace.
2. Neutral surfaces with high-contrast action states.
3. Small radius, predictable spacing, and clear focus rings.
4. Minimal motion, honoring reduced-motion preferences.
5. Login screen uses the actual product identity and operational context, but avoids oversized hero marketing composition.

The login page should feel like an operator entry point into a control plane: direct, calm, and trustworthy. It should not introduce decorative illustrations, gradient blobs, or large brand storytelling.

### Auth Store

`stores/auth.ts` owns client auth state:

1. `accessToken`
2. `refreshToken`
3. `sessionId`
4. `expiresAtUtc`
5. `principal`
6. `restoreStatus`
7. `authError`

Derived state:

1. `isAuthenticated`
2. `isRestoring`
3. `displayName`

Actions:

1. `login(loginName, password)`
2. `restoreSession()`
3. `refreshSession()`
4. `loadPrincipal()`
5. `logout()`
6. `clearSession(reason)`

The store is the only source of truth for bearer token state. Components do not read local storage directly.

### Storage Strategy

Use local browser storage for `refreshToken`, `sessionId`, and the latest principal snapshot so a browser refresh can restore the SPA. Keep `accessToken` in Pinia state and refresh it during startup.

This is an explicit SPA bearer-token tradeoff. A future cookie-based auth design must cover CSRF, same-site settings, refresh token rotation semantics, and deployment topology separately.

### API Transport

`frontend/packages/api-client/src/transport/client-config.ts` should accept a dynamic auth token provider instead of static headers only.

Generated Gateway requests attach:

```text
Authorization: Bearer <accessToken>
```

when the auth store has an access token. Requests without an access token stay anonymous so login and health endpoints remain usable.

On `401` from protected Console APIs, the frontend clears local auth state and redirects to `/login?redirect=<current path>`. This phase handles startup restore and request-time unauthorized cleanup, not background silent refresh timers.

### Router

Routes use meta:

```text
requiresAuth: true
guestOnly: true
title: string
```

Rules:

1. `/login` is guest-only.
2. Existing instance list and operation detail routes require auth.
3. Unknown route can remain public or use the app shell only after auth; the implementation plan should pick one behavior and test it.
4. If a user opens a protected route while unauthenticated, redirect to login with the intended path.
5. If an authenticated user opens login, redirect to the saved redirect target or `/`.

### Components

`pages/login.vue` remains a thin route composition surface.

`components/auth/LoginForm.vue` owns form presentation:

1. login name input
2. password input
3. submit button
4. inline error alert
5. pending state
6. accessible labels
7. disabled state while submitting

`DefaultLayout.vue` and `AppShell.vue` show authenticated context:

1. brand
2. navigation
3. principal display
4. sign-out menu

The route page coordinates navigation after successful login. The form emits typed events and receives state through props.

## Error Handling

1. Invalid credentials show an inline form error.
2. Missing credentials use client validation and `aria-invalid`.
3. Gateway/IAM unavailable shows a connection error inside the form.
4. Session restore failure clears local session and leaves the user on login.
5. Logout failure still clears local session and shows a toast.
6. Protected API `401` clears local session and redirects to login.
7. Protected API `403` remains a permission error in the current page and does not clear auth state.

## Accessibility

1. Login form fields have explicit labels.
2. Invalid fields use `data-invalid` on `Field` and `aria-invalid` on controls.
3. Submit button is keyboard reachable and disabled while pending.
4. Focus moves predictably after login failure.
5. Navigation and user menu have accessible names.
6. Toasts are supplementary; critical errors remain visible inline.
7. Color contrast follows shadcn-vue semantic token defaults and is verified in screenshots.
8. Motion is minimal and respects reduced-motion preferences.

## Testing Strategy

### Backend

Add Gateway tests for:

1. login forwards to IAM and returns auth payload.
2. invalid login returns `401`.
3. refresh forwards refresh token and returns rotated tokens.
4. logout forwards bearer/session and returns no content.
5. me forwards bearer and returns principal.
6. IAM unavailable maps to `503`.
7. OpenAPI exposes stable operation IDs.

### API Client

Add or update tests for:

1. generated auth operations are exported through stable package entry points.
2. client transport injects bearer token from the configured provider.
3. anonymous requests do not include stale auth headers after logout.

### Frontend Unit And Component

Add tests for:

1. auth store login success.
2. auth store login failure.
3. session restore with valid refresh token.
4. session restore failure clears storage.
5. router guard redirects unauthenticated users.
6. router guard redirects authenticated users away from login.
7. LoginForm disabled/pending/error states.
8. AppShell sign-out command calls logout.

### Frontend Quality Gate

Run:

```powershell
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

### Visual Verification

Start the Console dev server and verify with a browser:

1. desktop login page
2. mobile login page
3. authenticated app shell
4. sign-out menu
5. protected route redirect

Screenshots must show no overlapping text, no truncated button labels, visible focus states, and nonblank shadcn-vue styles.

## Documentation Updates

Update:

1. `docs/architecture/frontend-design-system-planning.md` to record shadcn-vue official registry + `nova` preset as the selected baseline for this phase.
2. `docs/architecture/frontend-structure.md` to document auth store, guards, and shadcn-vue UI package ownership.
3. `docs/architecture/iam-authentication-baseline.md` to note that Console login uses Gateway facade over IAM.
4. `docs/architecture/implementation-readiness.md` to mark Console login UI as completed only after verification passes.
5. `README.md` to remove stale current-worktree wording.
6. `docs/architecture/database-schema-catalog.md` to remove the stale statement that Gateway-wide permission enforcement is not connected.

## Rollout And Migration

1. Implement Gateway facade first so the generated OpenAPI is the single frontend contract.
2. Initialize shadcn-vue and migrate UI package exports before building LoginForm.
3. Add transport bearer injection before protecting routes so existing pages continue to load after login.
4. Add route guards after store restoration works.
5. Migrate current visible local primitives to shadcn-vue components.
6. Delete old UI primitive files and exports once `rg "UiButton|UiPanel|UiBadge"` shows no consumers.
7. Keep `packages/auth` as a future extraction note only.

## Acceptance Criteria

1. A seeded admin can log in through the Console UI.
2. Browser refresh restores the session and keeps protected pages accessible.
3. Existing instance list, instance detail, restart action, and operation detail requests include bearer tokens.
4. Missing or invalid auth redirects to login.
5. Logout clears local session and returns to login.
6. shadcn-vue is initialized and used for the login form and migrated visible UI components.
7. Old local UI primitives are deleted if unused.
8. Backend and frontend tests pass.
9. Frontend quality gate passes.
10. Browser screenshots verify desktop and mobile login/app shell states.

## Future `packages/auth` Extraction Note

Create `frontend/packages/auth` only when auth behavior needs to be consumed by more than one frontend app or package. That package should own reusable auth client adapters, storage abstractions, token lifecycle helpers, and app-agnostic route contracts. App-specific pages, layouts, and navigation decisions should remain in the consuming app.

## Self Review

Placeholder scan: no placeholder sections remain.

Internal consistency: Gateway is the only browser-facing API surface; IAM remains the auth fact owner; shadcn-vue is the selected UI baseline; `packages/auth` is explicitly future-only.

Scope check: this is one implementation plan because it delivers one user workflow: authenticated Console entry. OAuth, admin management UI, high-risk Ops approval, notifications, and FileStorage remain out of scope.

Ambiguity check: storage, route guards, component ownership, cleanup behavior, and verification gates are explicitly defined.
