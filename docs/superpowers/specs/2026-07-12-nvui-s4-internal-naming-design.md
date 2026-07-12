# NvUI S4 Internal Naming Closure Design

## Goal

Complete issue #896 by making NvUI's source layout and internal identifiers match
its canonical `Nv*` public component names. Remove the transitional `pro`
vocabulary, close the remaining `.ds-*` and `.sb-*` class names, and turn the
naming contracts into permanent closure guards.

## Source layout

`frontend/packages/ui/src/components/pro/` moves to
`frontend/packages/ui/src/components/pc/`. The directory describes the PC
surface; `pro` is not retained as a compatibility path. The existing `blocks/`,
`layout/`, `screen/`, `touch/`, and `ui/` directories retain their responsibilities.
The shadcn source under `components/ui/**` remains byte-for-byte unchanged.

Every component implementation filename in `pc/` uses its canonical `Nv*` name.
Internal imports use those names as well. The package continues to expose brand
components from the bare `@nerv-iip/ui` entry point; no new package subpath is
introduced.

## Pagination correction

The current `DataTablePaginationPro.vue` is used as a general PC pagination
component even though its name makes it appear table-specific. It moves to
`components/pc/pagination/NvPagination.vue` and is exported as `NvPagination`.
`NvDataTable` composes `NvPagination`. The misleading `NvDataTablePagination`
export is removed and all consumers move to `NvPagination`.

The shadcn primitives in `components/ui/pagination/**` remain unchanged and keep
their unprefixed exports for NvUI composition only.

## Internal selector closure

- NvUI PC classes change from `.ds-*` to `.nv-*`.
- Screen classes change from `.sb-*` to `.nv-scr-*`.
- NvUI `data-slot` values change from `*-pro` or other transitional names to
  their `nv-*` canonical form.
- Legacy `--sb-*` variables and local transitional variants are forbidden.
- Component behavior, props, emits, and visual token values remain unchanged.

## Governance and tests

The naming contract is changed first and must fail against the transitional tree.
The final contract scans source paths, filenames, exports, selectors, slots, and
tokens. It also asserts that `components/ui/**` contains none of the NvUI brand
identifiers. ADR 0020 and existing migration/design documentation are corrected
to describe the `pc/` layer and `NvPagination` rather than preserving stale
transition terminology.

## Verification

Run focused NvUI contract and component tests first, then the full frontend
`typecheck`, `test`, and `build` commands plus the design-system documentation
build. A final repository scan must find no live `components/pro`, `*Pro.vue`,
`.ds-*`, `.sb-*`, `--sb-*`, or `NvDataTablePagination` references outside explicit
historical documentation allowlists.

