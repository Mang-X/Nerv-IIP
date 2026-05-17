# Frontend Design System Planning

The console has a working third-stage skeleton, but the visual design system is not selected. Backend SDK, persistence, deployment and migration verification must not wait on UI work, and UI work must not start by accident while backend foundations are still settling.

## Current Decision

1. Do not start new frontend feature work during the release-grade persistence foundation phase.
2. Do not add new console pages for migration state, database profile state or SDK verification.
3. Do not restyle `frontend/packages/ui`, `frontend/packages/app-shell` or console layouts as part of backend foundation work.
4. Do not select shadcn-vue, UnoCSS, token naming, density, theme strategy or app shell navigation inside a backend implementation task.
5. API client generation and frontend quality gates are allowed only when backend OpenAPI changes require them.

## Required Future Spec

Before frontend implementation resumes, create a separate Superpowers design spec that decides:

1. Component library and registry strategy.
2. Design token model for color, typography, spacing, elevation, radius and state.
3. Icon policy, including when lucide icons are required.
4. Layout density for operations-heavy console screens.
5. Accessibility baseline for keyboard navigation, focus, contrast and reduced motion.
6. Theme strategy and whether tenant branding is supported in the first product slice.
7. Migration path from current local primitives to the selected system.
8. Testing strategy for visual regressions and responsive layout.

## Allowed Backend-Phase Frontend Work

The following work remains allowed because it preserves contract health without making design decisions:

1. Regenerate `frontend/packages/api-client` from Gateway OpenAPI when backend contract tests require it.
2. Run `pnpm -C frontend check`, `fmt`, `lint`, `typecheck`, `test` and `build` after mechanical generated-client changes.
3. Fix generated-client or transport failures that directly result from backend contract changes.

Any new page, component, route, visual redesign, navigation change or product workflow requires the future Design System spec first.
