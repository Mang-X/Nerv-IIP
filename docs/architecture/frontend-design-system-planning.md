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

## Future Spec Triggers

Create a separate Superpowers design spec before changing any of these decisions:

1. Component library or registry strategy beyond shadcn-vue.
2. Design token model for color, typography, spacing, elevation, radius or state.
3. Layout density for operations-heavy console screens.
4. Theme strategy, tenant branding, or dark-mode product commitment.
5. Accessibility baseline beyond the current keyboard, focus, contrast and responsive checks.
6. Visual regression testing strategy.
