# Claude Instructions

Read and follow [AGENTS.md](./AGENTS.md).

## Before You Start

**Always read `docs/architecture/implementation-readiness.md`** before making changes.
It records the current phase, delivered services, database schemas, and environment prerequisites.
Do NOT assume a service, schema, or port is ready based on prior knowledge — verify there.

## Frontend: NvUI components

Business/app code uses the `Nv*` brand components only (`NvButton`, `NvDataTable`,
`NvPageHeader`, `NvMobileBadge`, `NvOeeHero`, …), imported from the bare
`@nerv-iip/ui` / `@nerv-iip/ui-mobile` boundary. A name without the `Nv` prefix is
a shadcn 原版 primitive or a `@deprecated` old name — never use it in app code, and
never deep-import `components/ui/`, `reka-ui`, or `shadcn-vue`. Full spec + the
four-surface map: AGENTS.md "NvUI Component Library"; the frozen component map is
`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md` Appendix A.

For the full instructions (commands, principles, constraints, common mistakes, done
definition), read [AGENTS.md](./AGENTS.md).
