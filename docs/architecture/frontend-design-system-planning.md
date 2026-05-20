# Frontend Design System Planning

The fifth-stage backend foundation paused frontend feature work until the console visual system could be selected deliberately. Console Auth + shadcn-vue Baseline now records that selection for the first product slice.

## Selected Baseline

Console Auth + shadcn-vue Baseline selects the official shadcn-vue registry, `reka-nova` style, Vite template, Reka base components, Tailwind CSS v4 and semantic token model. Component source lives in `frontend/packages/ui`, and console apps consume it only through stable `@nerv-iip/ui` exports.

The previous local `UiButton`, `UiPanel` and `UiBadge` primitives were migrated to shadcn-vue components and deleted. They are no longer maintained as a parallel design system.

## Current Decision

1. New console UI work must use the selected shadcn-vue baseline, semantic tokens and `@nerv-iip/ui` export boundary.
2. Do not introduce a second UI registry, competing token system, unrelated CSS framework or page-specific component skin without a new design-system spec.
3. Do not add large product workflows as incidental backend work; create a focused frontend/product spec when the workflow changes information architecture, navigation, authorization, or visual density.
4. API client generation and frontend quality gates remain allowed when backend OpenAPI changes require them.

## Phase 8 Current Baseline

Phase 8 establishes the IAM admin console baseline as a blue Calm Control Plane: restrained surfaces, blue primary actions, quiet neutral structure and high-density operational affordances. The baseline remains Tailwind CSS v4, shadcn-vue `reka-nova`, Reka primitives, lucide icons and source-owned components in `frontend/packages/ui`.

The shared UI package now owns the table, dialog, alert-dialog, checkbox, select, pagination and empty state primitives needed for IAM administration screens. Console app code should treat these as product infrastructure rather than page-local snippets.

## Token Contract

The console CSS token contract lives in `frontend/apps/console/src/assets/main.css`. Phase 8 pins semantic shadcn tokens to blue control-plane values for `--primary`, `--ring`, `--accent`, sidebar active states and chart accents while preserving the legacy token block used by existing console screens.

Tailwind v4 `@theme inline` remains required so semantic utilities such as `bg-primary`, `text-muted-foreground`, `border-border` and `ring-ring` resolve from the same contract. Token changes should update the Vitest contract in `frontend/packages/ui/src/design-system.contract.test.ts` before changing the CSS.

## Component Governance

shadcn-vue components are managed through the CLI and reviewed after generation. Generated files may be adjusted for this workspace's package-local import paths, but teams should not hand-roll parallel versions of registry components or fork visual variants inside console pages.

The `@nerv-iip/ui` barrel is the public boundary for console applications. New shadcn primitives should be exported there before application code consumes them, keeping registry churn and import path changes inside the UI package.

## IAM Admin Patterns

IAM administration screens should compose dense, task-focused views from the shared primitives: tables for users, roles and permissions; dialogs for create/edit flows; alert dialogs for destructive confirmations; selects and checkboxes for scoped choices; pagination for server-backed lists; and empty states for filtered or first-run conditions.

Console app code must import these controls from `@nerv-iip/ui`, not from `frontend/packages/ui/src/components` or direct shadcn paths. Page-specific styling should use semantic tokens and layout classes only, leaving component color, typography, radius and focus behavior governed by the shared baseline.

## Future Spec Triggers

Create a separate Superpowers design spec before changing any of these decisions:

1. Component library or registry strategy beyond shadcn-vue.
2. Design token model for color, typography, spacing, elevation, radius or state.
3. Layout density for operations-heavy console screens.
4. Theme strategy, tenant branding, or dark-mode product commitment.
5. Accessibility baseline beyond the current keyboard, focus, contrast and responsive checks.
6. Visual regression testing strategy.
