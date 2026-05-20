# Phase 8 IAM Admin Console And Design System Baseline Design

## Purpose

Phase 8 turns the existing Console Auth + shadcn-vue baseline into a usable administration surface. It does this in two linked slices:

1. **Phase 8.0 Console Design System Baseline**: define the current-stage design system contract for operations-heavy Console pages, with a blue primary theme, shadcn-vue source components, semantic tokens, density rules, state patterns, documentation and governance.
2. **Phase 8.1 IAM Admin Console & Role Permission Completion**: complete the IAM admin workflow that is still partial after the 2026-05-19 fixes: role creation, role permission editing, user management refinement, session review and session revoke from the Console.

The user-facing outcome is simple: a platform administrator can sign in, open IAM administration pages, manage users, create roles, edit role permissions, inspect sessions and revoke a session through a coherent Console UI that follows one design system.

## Current Context

1. The repository has completed the first seven implementation stages plus the Console Auth + shadcn-vue baseline.
2. The 2026-05-19 commits moved several items forward: persisted IAM user CRUD, case-insensitive unique indexes, IAM provider branch cleanup, Gateway standard auth/authorization pipeline, response envelopes, Ops claim/lease, real Docker restart, CI, E2E coverage and domain tests.
3. Current IAM user endpoints create, patch and disable users. Session listing and revoke exist. Role listing exists, but PostgreSQL role creation and role permission patch still return not implemented.
4. Current Console frontend has shadcn-vue initialized with `reka-nova`, Tailwind CSS v4, `lucide`, and stable `@nerv-iip/ui` exports.
5. Current UI exports cover Button, Card, Field, Input, Alert, Badge, Separator, Skeleton, DropdownMenu, Avatar, Toaster and Spinner.
6. Existing Console pages still contain some legacy local tokens such as `--legacy-color-*`; these remain compatibility tokens, not the future product API.
7. The Console frontend should continue to consume only PlatformGateway OpenAPI. It should not call IAM Web directly.

## Recommended Approach

Build Phase 8 as a design-system-first IAM admin slice.

Phase 8.0 defines the Console visual language and component usage rules before adding administration pages. Phase 8.1 then implements IAM admin pages using only that system.

This avoids two failure modes:

1. Adding IAM management pages with ad hoc table, dialog, form, empty-state and error-state markup.
2. Turning the design system work into a broad brand-system project before the product has enough surfaces to justify it.

The selected design direction is **Calm Control Plane**.

The Console should feel like a serious control surface: calm, precise, information-dense, low-noise and audit-friendly. Blue is the primary action and information anchor. Neutral surfaces carry most of the layout. Success, warning and danger colors are reserved for state semantics and are not replaced by blue.

## Alternatives Considered

1. **Backend hardening only**: finish IAM role mutation and API tests, defer Console pages. This is lower risk, but it misses the opportunity created by the Console Auth + shadcn-vue baseline and does not produce a product-visible administration workflow.
2. **Full IAM platform expansion**: include organization/environment switching, memberships, external clients, OAuth/OIDC, MFA and ABAC. This is too large for one phase and would mix several independent security and product decisions.
3. **Design-system-first IAM admin slice**: freeze the current Console design contract, then deliver IAM users, roles and sessions inside it. This is the selected path because it matches the real post-fix gap and keeps Phase 8 shippable.

## Scope

### In Scope

1. Blue primary theme and semantic token decisions for the current Console stage.
2. shadcn-vue component selection and `@nerv-iip/ui` export governance for IAM admin pages.
3. Console patterns for page headers, toolbars, filters, tables, dialogs, destructive confirmations, permission chips, empty states, loading states, error states and permission-denied states.
4. Gateway Console IAM Admin facade over IAM management endpoints.
5. IAM PostgreSQL role creation and role permission update.
6. IAM permission catalog endpoint for the role editor.
7. Admin reset-password endpoint for users.
8. Console pages for users, roles and sessions.
9. OpenAPI snapshot and generated api-client updates.
10. Backend, api-client, Vue component/unit and E2E coverage for the admin workflow.
11. Documentation updates for design-system status, frontend structure, IAM auth baseline, authorization matrix and implementation readiness.

### Out Of Scope

1. OAuth/OIDC, SSO, MFA, WebAuthn, consent pages and third-party app marketplace.
2. Full ABAC rule authoring or policy editor.
3. Connector Host bearer-token migration. Header-secret compatibility remains separate from this phase.
4. High-risk Ops approvals, notification integration and persistent Ops outbox.
5. FileStorage upload/download UI.
6. Multi-tenant branding, dark-mode product commitment or theme switching UI.
7. Full visual regression test infrastructure beyond targeted screenshots for Phase 8 pages.
8. Extracting `frontend/packages/auth`; Console remains the only frontend app in this phase.
9. Deleting all legacy page styles across the Console. New IAM pages must use the new system; existing instance pages can be migrated only where touched by shared shell or obvious low-risk cleanup.

## Phase 8.0 Console Design System Baseline

### Design System Mode

This is a **Create** mode design-system effort with a deliberately narrow output: a current-stage Console design-system blueprint and starter backlog.

The design system is treated as a product surface connecting:

1. Code implementation in `frontend/packages/ui`, `frontend/packages/app-shell` and `frontend/apps/console`.
2. Documentation in architecture and future Superpowers plans.
3. Accessibility behavior, keyboard support and testability.
4. Governance for adding shadcn-vue components and exposing them through `@nerv-iip/ui`.

There is no Figma library in the repository today. Code and documentation are the source of truth for this phase.

### Product Surfaces

The baseline covers these Console surfaces:

1. Authenticated app shell.
2. Instance overview and operation detail pages.
3. IAM users page.
4. IAM roles and permission editor page.
5. IAM sessions page.
6. Shared dialogs, alerts, menus, tables and form controls needed by those pages.

It does not cover marketing pages, public documentation sites or customer-facing tenant branding.

### Users And Teams

Primary users:

1. Platform administrator managing users, roles and sessions.
2. Operator inspecting managed application instances and low-risk operations.
3. Developer or AI coding agent extending Console pages.

The system must optimize for repeated scanning, safe action, traceability and fast implementation without local one-off component skins.

### System Principles

1. **Calm over flashy**: the Console is a workbench, not a landing page.
2. **Blue for action and orientation**: blue marks primary actions, selected navigation, focus and information hierarchy; it does not replace state colors.
3. **Dense but legible**: tables and forms should hold operational data without feeling cramped.
4. **State is explicit**: loading, empty, partial failure, permission denial and destructive confirmation all have first-class patterns.
5. **shadcn-vue first**: add source components from shadcn-vue before building custom UI.
6. **Stable exports only**: Console app code consumes UI through `@nerv-iip/ui`, not deep component paths.
7. **Accessible by default**: labels, focus, keyboard order, target sizes and contrast are part of the component contract.
8. **Small governance loop**: every new UI primitive must have a reason, export path and test or usage example.

### Visual Direction

Name: **Calm Control Plane**

Tone:

1. Enterprise blue, neutral workspace, precise borders.
2. Minimal motion.
3. Compact navigation.
4. Crisp table and form hierarchy.
5. No decorative gradient blobs, bokeh, or marketing hero treatment.

The visual identity should make the product feel like a reliable operations console for managed AI application infrastructure.

### Token Architecture

Use the design-system-steward layering model:

1. Primitive values may exist in CSS, but application code should consume semantic shadcn/Tailwind tokens.
2. Semantic tokens are the product contract.
3. Component tokens are added only when a component needs a stable local override.

Current delivery format is CSS custom properties in `frontend/apps/console/src/assets/main.css`, exposed to Tailwind v4 through `@theme inline`.

#### Blue Theme Token Direction

Phase 8 should set the shadcn semantic tokens to a blue primary palette:

| Token | Intent | Usage |
| --- | --- | --- |
| `--primary` | Control blue primary action | Primary buttons, selected nav, active tab, primary link emphasis. |
| `--primary-foreground` | Foreground on primary blue | Text and icons on primary actions. |
| `--ring` | Focus blue | Focus-visible outlines and interactive emphasis. |
| `--accent` | Subtle blue-tinted surface | Selected table row, soft information emphasis, active nav background when appropriate. |
| `--accent-foreground` | Foreground on subtle accent | Text on accent surfaces. |
| `--sidebar-primary` | Sidebar selected marker | Brand mark and current section anchor. |
| `--chart-1` | Primary metric blue | Future charts and sparkline primary series. |

Recommended OKLCH direction for implementation:

```css
:root {
  --primary: oklch(0.49 0.17 255);
  --primary-foreground: oklch(0.985 0 0);
  --ring: oklch(0.62 0.15 255);
  --accent: oklch(0.96 0.03 255);
  --accent-foreground: oklch(0.28 0.11 255);
  --sidebar-primary: var(--primary);
  --sidebar-primary-foreground: var(--primary-foreground);
  --chart-1: oklch(0.58 0.16 255);
}
```

Exact values can be tuned in implementation after browser screenshots, but the role mapping is fixed by this spec.

#### State Tokens

State colors keep separate semantics:

| State | Token Source | Meaning |
| --- | --- | --- |
| Success | semantic green via Badge variant or future token | Enabled, healthy, completed. |
| Warning | semantic amber via Badge variant or future token | Pending, expiring, degraded. |
| Danger | `--destructive` and destructive variants | Disabled, revoked, failed, destructive action. |
| Info | primary/accent blue | Informational guidance and selected state. |
| Neutral | background, card, muted, border | Default workspace, passive metadata and table structure. |

Do not use blue for destructive or success states.

#### Radius, Spacing And Density

Phase 8 should keep controls and cards at a restrained radius:

1. `--radius` should resolve to 0.5rem for the current Console unless shadcn-vue upstream component behavior requires a different base.
2. Table rows use compact vertical padding, but controls remain at accessible target size.
3. Page sections are unframed layouts, not cards inside cards.
4. Cards are reserved for individual bounded modules, forms, dialogs and repeated items.
5. Admin list pages use constrained content width only when forms need it; tables may use the full workspace width.

#### Typography

Use the configured shadcn-vue `geist-sans` direction if available through local package setup. Do not add external font loading in this phase. If local font setup is absent, keep a system sans stack.

Typography rules:

1. Page titles are compact, not hero-scale.
2. Table text prioritizes scanability.
3. Labels are sentence case.
4. Button labels are short verbs.
5. Error messages explain what happened and the next safe action.

#### Motion

Motion is functional:

1. Use shadcn/reka component transitions where they already exist.
2. Do not add page-load choreography.
3. Honor reduced-motion preferences.
4. Loading states use Skeleton or Spinner, not animated decorative effects.

### Component Roadmap

| Component or Pattern | Priority | Source | Rationale | Dependencies |
| --- | --- | --- | --- | --- |
| Table | P0 | shadcn-vue | IAM users, roles and sessions need dense scanning. | `@nerv-iip/ui` export and table usage docs. |
| Dialog | P0 | shadcn-vue | Create user, create role, edit user and reset password forms. | Accessible title, focus trap, form pattern. |
| AlertDialog | P0 | shadcn-vue | Disable user and revoke session confirmations. | Destructive action pattern. |
| Checkbox | P0 | shadcn-vue | Permission selection in role editor. | Field and list grouping pattern. |
| Select | P1 | shadcn-vue | Filters such as enabled/revoked state. | List toolbar pattern. |
| Pagination | P1 | shadcn-vue or local composition | Paged IAM lists. | API page model. |
| Tooltip | P2 | shadcn-vue | Permission code descriptions if labels become dense. | Icon/help pattern. |
| Tabs | P2 | shadcn-vue | Only if IAM admin pages share one route; not required for separate routes. | Navigation decision. |

The implementation plan must run shadcn-vue docs commands before adding new components and review generated files before exporting them.

### Core Patterns

#### Page Header

Use an unframed header with:

1. Title.
2. Short description.
3. Optional primary action on the right.
4. Optional compact metadata below when needed.

Do not wrap page headers in cards.

#### Toolbar

List pages use a toolbar above the table:

1. Search input.
2. Status filter when useful.
3. Primary action.
4. No more than one row on desktop unless filters overflow.
5. On mobile, stack controls with `gap-*`.

#### Data Table

Admin tables use:

1. Stable columns.
2. Clear empty state.
3. Skeleton rows while loading.
4. Inline Badge states.
5. Row actions in a DropdownMenu when there are more than two actions.
6. Destructive actions only after confirmation.
7. Horizontal overflow only as a last resort on mobile.

#### Forms

Forms use:

1. `FieldGroup` and `Field`.
2. `FieldLabel`, `FieldDescription` and `FieldError`.
3. `data-invalid` on Field and `aria-invalid` on controls.
4. Dialog footer actions with primary and secondary buttons.
5. Password fields never echo generated or submitted secret values after submit.

#### Permission Editor

Role permission editing uses:

1. Permission groups by domain prefix: `iam`, `apphub`, `ops`, `connectors`, `files`.
2. Checkbox rows with code and description.
3. Search/filter for permission codes.
4. A selected-count summary.
5. A warning when removing permissions from the administrator role.

Do not use free-text permission editing.

#### Empty, Error And Permission States

Use first-class states:

1. Empty list: neutral Card or unframed Empty-style composition with a clear next action.
2. Permission denied: Alert with permission code and safe explanation.
3. Load failure: Alert with retry action.
4. Partial failure: keep loaded data visible and show a non-blocking Alert.
5. Destructive confirmation: AlertDialog with explicit object name.

### Accessibility Baseline

Phase 8 must cover:

1. Keyboard navigation through nav, toolbar, table actions and dialogs.
2. Dialog title and description for every dialog.
3. AlertDialog title for destructive confirmations.
4. Accessible labels for all search and filter controls.
5. Focus visible on blue ring.
6. No color-only status communication.
7. Table actions have accessible names.
8. Error messages remain visible inline; toast is supplementary.
9. Buttons keep adequate hit area on mobile.
10. Screen reader output avoids leaking passwords or generated secrets.

### Documentation Model

Update `docs/architecture/frontend-design-system-planning.md` with:

1. Calm Control Plane direction.
2. Blue primary theme decision.
3. Token role mapping.
4. shadcn-vue component addition rules.
5. Admin list/form/dialog/table patterns.
6. Legacy token deprecation note.
7. Review gates for new UI surfaces.

The spec itself remains the design artifact for Phase 8. Storybook is not introduced in this phase.

### Governance

Owners:

1. Frontend implementation owns `frontend/packages/ui` and Console app usage.
2. Architecture docs own design-system decisions and future migration notes.
3. Accessibility checks are part of verification, not a separate optional review.

Contribution rules:

1. New shadcn-vue components must be added through the CLI.
2. New components must be exported through `@nerv-iip/ui` before app usage.
3. App code must not import from `packages/ui/src/components/ui/*` deep paths.
4. New raw CSS variables must be semantic and documented.
5. Legacy `--legacy-color-*` tokens must not be used in new IAM admin pages.
6. UI diffs require component tests or E2E coverage for core states.

## Phase 8.1 IAM Admin Console

### Backend Design

#### IAM Service Completion

Complete the currently partial IAM admin backend:

1. Persisted role creation in PostgreSQL mode.
2. Persisted role permission patch in PostgreSQL mode.
3. InMemory role mutation behavior aligned with PostgreSQL behavior, not hard-coded role IDs.
4. Permission catalog query based on `NervIipSeedPermissions.All` and `docs/architecture/authorization-matrix.md` descriptions.
5. Admin reset-password command and endpoint.

Current user create/update/disable and session revoke behavior should be preserved and hardened, not rewritten.

#### IAM API Shape

IAM endpoints should expose:

```text
GET  /api/iam/v1/users
POST /api/iam/v1/users
PATCH /api/iam/v1/users/{userId}
POST /api/iam/v1/users/{userId}/disable
POST /api/iam/v1/users/{userId}/reset-password

GET  /api/iam/v1/roles
POST /api/iam/v1/roles
PATCH /api/iam/v1/roles/{roleId}/permissions
GET  /api/iam/v1/permissions

GET  /api/iam/v1/sessions
POST /api/iam/v1/sessions/{sessionId}/revoke
```

Write endpoints require existing IAM permissions:

1. `iam.users.manage` for user create, update, disable and reset password.
2. `iam.roles.manage` for role create and permission patch.
3. `iam.sessions.revoke` for session revoke.

Read endpoints require:

1. `iam.users.read`
2. `iam.roles.read`
3. `iam.sessions.read`

Permission catalog read requires `iam.roles.read` because it is used to inspect assignable role permissions.

#### Request And Response Decisions

User reset password:

```text
request:  { newPassword: string }
response: 204 No Content
```

Role create:

```text
request:  { roleName: string, permissionCodes: string[] }
response: RoleResponse
```

Role permission patch:

```text
request:  { permissionCodes: string[] }
response: RoleResponse
```

Permission catalog:

```text
response: {
  items: [
    {
      code: string,
      domain: string,
      description: string,
      seeded: true
    }
  ]
}
```

The permission catalog should not invent unseeded permissions in Phase 8. Future service permissions remain documented but not assignable until seeded.

#### Domain And Persistence Rules

1. Role names are required, trimmed and unique case-insensitively within the IAM service scope.
2. Permission codes must be in `NervIipSeedPermissions.All`.
3. Role permission patch replaces the role permission set atomically.
4. Administrator role changes are allowed, but tests must cover that removing `iam.roles.manage` from the only platform admin can lock out future role edits. Phase 8 should warn in UI but does not need a complex break-glass model.
5. Reset password updates password hash and security stamp, increments permission or security version as needed, and revokes active sessions for that user.
6. Disabled users cannot login or refresh.
7. User update uniqueness remains case-insensitive.

#### Gateway Console IAM Admin Facade

The Console frontend continues to call only PlatformGateway.

Add Gateway endpoints:

```text
GET  /api/console/v1/iam/users
POST /api/console/v1/iam/users
PATCH /api/console/v1/iam/users/{userId}
POST /api/console/v1/iam/users/{userId}/disable
POST /api/console/v1/iam/users/{userId}/reset-password

GET  /api/console/v1/iam/roles
POST /api/console/v1/iam/roles
PATCH /api/console/v1/iam/roles/{roleId}/permissions
GET  /api/console/v1/iam/permissions

GET  /api/console/v1/iam/sessions
POST /api/console/v1/iam/sessions/{sessionId}/revoke
```

Gateway responsibilities:

1. Require authenticated Console bearer token.
2. Use the existing IAM-backed authorization check before forwarding.
3. Forward the original bearer token to IAM.
4. Preserve response envelope and status codes.
5. Map IAM unavailable to `503`, unexpected IAM failure to `502`.
6. Avoid referencing IAM Domain or Infrastructure.

Stable operation IDs:

```text
listConsoleIamUsers
createConsoleIamUser
updateConsoleIamUser
disableConsoleIamUser
resetConsoleIamUserPassword
listConsoleIamRoles
createConsoleIamRole
updateConsoleIamRolePermissions
listConsoleIamPermissions
listConsoleIamSessions
revokeConsoleIamSession
```

### Frontend Information Architecture

Navigation expands from one item to an admin group:

```text
Instances
IAM
  Users
  Roles
  Sessions
```

Routes:

```text
frontend/apps/console/src/pages/iam/users/index.vue
frontend/apps/console/src/pages/iam/roles/index.vue
frontend/apps/console/src/pages/iam/sessions/index.vue
```

All IAM admin routes require auth.

Phase 8 does not implement organization or environment switchers. The current principal context remains the active organization/environment.

### Frontend Data Flow

1. Gateway OpenAPI is exported after backend endpoints exist.
2. `frontend/packages/api-client` regenerates types, SDK and Pinia Colada helpers.
3. `frontend/apps/console/src/api/iam.ts` may wrap generated operations only for parameter shaping.
4. `frontend/apps/console/src/composables/useIamAdmin.ts` owns query/mutation composition, invalidation and common error mapping.
5. Pages stay thin and compose feature components.
6. Components receive data and emit typed events; they do not call generated SDK functions directly.

No page or component handwrites fetch URLs.

### Frontend Components

Create focused IAM components:

```text
frontend/apps/console/src/components/iam/IamPageHeader.vue
frontend/apps/console/src/components/iam/IamListToolbar.vue
frontend/apps/console/src/components/iam/UsersTable.vue
frontend/apps/console/src/components/iam/UserCreateDialog.vue
frontend/apps/console/src/components/iam/UserEditDialog.vue
frontend/apps/console/src/components/iam/UserResetPasswordDialog.vue
frontend/apps/console/src/components/iam/RolesTable.vue
frontend/apps/console/src/components/iam/RoleCreateDialog.vue
frontend/apps/console/src/components/iam/RolePermissionEditor.vue
frontend/apps/console/src/components/iam/SessionsTable.vue
frontend/apps/console/src/components/iam/RevokeSessionDialog.vue
frontend/apps/console/src/components/iam/PermissionCodeBadge.vue
```

If implementation finds a component is only three lines of glue, keep it local to the page instead of creating a file. The important boundary is that tables, dialogs and permission editor stay focused and testable.

### Users Page

Capabilities:

1. List users with paging.
2. Search by login name, email or user id.
3. Filter enabled/disabled.
4. Create user.
5. Edit login name, email and enabled state.
6. Disable user.
7. Reset password.

States:

1. Loading skeleton.
2. Empty result.
3. Validation error.
4. Permission denied.
5. Load failure with retry.
6. Successful mutation toast.

The table should show:

1. Login name.
2. Email.
3. User id.
4. Status badge.
5. Actions.

### Roles Page

Capabilities:

1. List roles with paging.
2. Search by role name, role id or permission code.
3. Create role with selected permissions.
4. Edit permissions for an existing role.
5. Inspect permission code groups.

The table should show:

1. Role name.
2. Role id.
3. Permission count.
4. Key permission badges.
5. Actions.

The permission editor should show grouped permissions, search and selected count. It must prevent unknown permission codes.

### Sessions Page

Capabilities:

1. List sessions with paging.
2. Search by session id or user id.
3. Filter active/revoked.
4. Revoke active session.

The table should show:

1. Session id.
2. User id.
3. Issued at.
4. Expires at.
5. Revoked at or active state.
6. Permission version.
7. Actions.

Revoking the current user's current session should show a warning that the user may be signed out.

### Error Handling

Backend:

1. Validation errors return 400 through the existing response envelope/problem shape.
2. Unauthorized returns 401.
3. Permission denied returns 403.
4. Unknown user, role or session returns 404.
5. Duplicate role/user conflicts return 409.
6. IAM unavailable from Gateway returns 503.
7. Unexpected downstream failure returns 502 from Gateway.

Frontend:

1. `401` clears auth and redirects to login.
2. `403` renders permission-denied state without clearing auth.
3. `409` renders field-level or dialog-level conflict message.
4. `404` invalidates list query and shows a mutation-specific message.
5. Network failure keeps stale data visible when available and shows retry.
6. Destructive action failure keeps the dialog open when the user can retry.

### Security And Privacy

1. Password values are never logged.
2. Reset password dialog clears local state after close or submit.
3. Generated passwords are not introduced in this phase.
4. The role editor does not expose unseeded future permissions.
5. Gateway does not bypass IAM authorization checks for admin endpoints.
6. User and role mutations should create audit-friendly logs with correlation id, action and target id, but no secrets.

### Testing Strategy

Backend IAM tests:

1. PostgreSQL role creation persists role and permissions.
2. PostgreSQL role permission patch replaces permissions atomically.
3. Unknown permission code is rejected.
4. Duplicate role name is rejected case-insensitively.
5. Reset password changes password, revokes old sessions and allows login with the new password.
6. User CRUD and session revoke tests continue to pass.

Gateway tests:

1. Each Console IAM endpoint requires bearer auth.
2. Each endpoint maps to the correct IAM permission code.
3. Denied authorization avoids downstream IAM calls.
4. Gateway preserves response envelopes and status codes.
5. Gateway OpenAPI exposes stable operation IDs.

API client tests:

1. New generated operations are exported through stable package entry points.
2. Bearer injection works for IAM admin operations.
3. Error responses remain consumable by the app wrapper.

Vue unit/component tests:

1. Users page renders loading, empty, data, error and permission denied states.
2. User dialogs validate required fields and emit typed submit events.
3. Roles page loads permission catalog and shows grouped permission checkboxes.
4. Role permission editor filters permissions and tracks selected count.
5. Sessions page revokes a session after confirmation.
6. Nav includes IAM admin routes for authenticated users.

E2E:

1. Seeded admin logs in.
2. Admin opens Users, creates a user, edits it and disables it.
3. Admin opens Roles, creates a role and updates permissions.
4. Admin opens Sessions and revokes a non-current session or verifies revoke affordance when no revocable session exists.
5. Permission-denied fixture shows a safe 403 state.

Visual/browser verification:

1. Desktop IAM users page.
2. Desktop role permission editor.
3. Desktop sessions page.
4. Mobile users page.
5. Dialog focus and destructive confirmation.
6. Blue theme appears as primary action/focus/selection, not as one-note full-page color.

### Verification Commands

Expected gates for implementation:

```powershell
dotnet test backend/Nerv.IIP.sln --no-restore
dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln --no-restore
pwsh scripts/verify-iam-persistent-auth-foundation.ps1
pwsh scripts/verify-third-slice-console.ps1
pnpm -C frontend check
pnpm -C frontend lint
pnpm -C frontend fmt
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

If implementation changes scripts, run:

```powershell
pwsh scripts/check-script-governance.ps1
```

## Documentation Updates

Update:

1. `docs/architecture/frontend-design-system-planning.md`: selected blue Calm Control Plane baseline, token role mapping, shadcn component governance and current patterns.
2. `docs/architecture/frontend-structure.md`: IAM admin routes, composable boundaries, generated-client usage and design-system consumption rules.
3. `docs/architecture/iam-authentication-baseline.md`: role mutation, user reset password and admin Console status.
4. `docs/architecture/authorization-matrix.md`: permission catalog status and IAM admin endpoint enforcement status.
5. `docs/architecture/api-contract-and-codegen.md`: Console IAM Admin facade operation IDs.
6. `docs/architecture/implementation-readiness.md`: Phase 8 completion state after verification passes.
7. `README.md`: next-stage status once Phase 8 implementation is complete.

## Rollout Order

1. Update design-system planning docs and token decisions.
2. Add required shadcn-vue components and export them through `@nerv-iip/ui`.
3. Complete IAM role mutation and permission catalog.
4. Add user reset-password endpoint.
5. Add Gateway Console IAM Admin facade and OpenAPI tests.
6. Export OpenAPI and regenerate api-client.
7. Build `useIamAdmin` composable and IAM admin pages.
8. Add E2E and browser verification.
9. Update readiness docs only after verification passes.

## Acceptance Criteria

1. Console has a documented blue Calm Control Plane design-system baseline.
2. New IAM admin UI uses shadcn-vue components through `@nerv-iip/ui`.
3. No new IAM admin page uses `--legacy-color-*` tokens.
4. PostgreSQL role creation and role permission patch are implemented.
5. Permission catalog exposes only seeded permissions.
6. Admin can create, edit, disable and reset password for users through the Console.
7. Admin can create roles and edit permissions through the Console.
8. Admin can view sessions and revoke a session through the Console.
9. Gateway enforces IAM permissions before forwarding admin facade calls.
10. OpenAPI snapshot and generated api-client include stable IAM admin operations.
11. Unit, integration, frontend and E2E tests cover the main workflow.
12. Browser verification confirms desktop/mobile layout, dialog focus and no text overlap.

## Future Work

1. Organization and environment switching.
2. Membership management.
3. External client and AuthorizationGrant management.
4. OAuth/OIDC and SSO.
5. MFA and WebAuthn.
6. Connector Host bearer-token migration.
7. Notification and high-risk Ops approval integration.
8. Visual regression testing and Storybook or equivalent component docs.
9. Dark mode or tenant branding.

## Self Review

Completeness scan: no incomplete sections remain.

Internal consistency: the Console continues to call Gateway only; IAM remains the identity and permission fact owner; shadcn-vue remains the component source; blue primary tokens are semantic rather than raw styling instructions.

Scope check: this is one phase because Phase 8.0 is the design-system prerequisite for Phase 8.1 and both deliver one coherent product surface: IAM administration in the Console. OAuth, ABAC, Connector Host bearer migration, FileStorage, Notification and release installers are explicitly out of scope.

Ambiguity check: the spec defines token roles, component governance, backend endpoints, Gateway facade operation IDs, frontend routes, error handling, testing and acceptance criteria.
