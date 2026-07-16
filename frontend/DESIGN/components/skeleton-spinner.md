# Skeleton / Spinner / NvLoader

Loading indicators. Skeleton mirrors content shape; Spinner/NvLoader signal
in-progress work.

> NvUI status: `Skeleton` and `Spinner` are the current canonical exports from
> `@nerv-iip/ui` (原版 primitives kept as the app-facing names — no brand
> rebuild yet). The branded `NvLoader` (variants `ring | dots | bars | pulse`)
> is the richer alternative for brand-colored inline loading; `NvButton` has a
> built-in `loading` prop so buttons never hand-compose a spinner.

## Skeleton

Use for initial data load — replaces the content area before data arrives.
Note: `NvDataTable` renders its own skeleton rows via `loading` +
`skeletonRows`; don't rebuild table skeletons by hand.

```vue
<!-- Card content skeleton -->
<div class="grid gap-3 p-6">
  <Skeleton class="h-6 w-48" />
  <Skeleton class="h-4 w-full" />
  <Skeleton class="h-4 w-3/4" />
</div>
```

## Spinner / NvLoader

Use for inline loading: background refresh, small async indicators.

```vue
<!-- Button submission — built into NvButton -->
<NvButton type="submit" :loading="pending">Sign in</NvButton>

<!-- Inline page refresh indicator -->
<div class="flex items-center gap-2 text-sm text-muted-foreground">
  <Spinner class="size-3" />
  Refreshing…
</div>

<!-- Brand-colored loader -->
<NvLoader variant="ring" size="sm" />
```

## Decision guide

| Situation                                 | Use                                             |
| ----------------------------------------- | ----------------------------------------------- |
| Page or section initial load              | Skeleton                                        |
| Table initial load                        | `NvDataTable :loading` (built-in skeleton rows) |
| Button action in progress                 | `NvButton :loading`                             |
| Background refetch (data already visible) | Spinner / `NvLoader` alongside stale data       |
| Full-page blank loading                   | Skeleton grid matching page layout              |

## Do NOT

- Do not use Spinner for initial page loads — use Skeleton.
- Do not hand-compose a spinner inside a button — use `NvButton :loading`.
- Do not show loading state after less than ~200ms (consider `suspense` delays).
- Do not use Skeleton with random widths — match expected content width.
