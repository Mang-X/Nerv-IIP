# Skeleton / Spinner

Loading indicators. Skeleton mirrors content shape; Spinner signals in-progress work.

## Skeleton

Use for initial data load — replaces the content area before data arrives.

```vue
<!-- Table row skeletons (match column count and expected widths) -->
<TableRow v-for="i in 5" :key="i">
  <TableCell><Skeleton class="h-5 w-32" /></TableCell>
  <TableCell><Skeleton class="h-5 w-48" /></TableCell>
  <TableCell><Skeleton class="h-5 w-40 font-mono" /></TableCell>
  <TableCell><Skeleton class="h-5 w-16" /></TableCell>
  <TableCell><Skeleton class="ml-auto h-8 w-8" /></TableCell>
</TableRow>

<!-- Card content skeleton -->
<div class="grid gap-3 p-6">
  <Skeleton class="h-6 w-48" />
  <Skeleton class="h-4 w-full" />
  <Skeleton class="h-4 w-3/4" />
</div>
```

## Spinner

Use for inline loading: button submission, background refresh, small async indicators.

```vue
<!-- Button with in-progress spinner -->
<Button type="submit" :disabled="pending">
  <Spinner v-if="pending" class="size-4" />
  <LogInIcon v-else class="size-4" aria-hidden="true" />
  Sign in
</Button>

<!-- Inline page refresh indicator -->
<div class="flex items-center gap-2 text-sm text-muted-foreground">
  <Spinner class="size-3" />
  Refreshing…
</div>
```

## Decision guide

| Situation | Use |
|---|---|
| Page or section initial load | Skeleton |
| Button action in progress | Spinner inside Button |
| Background refetch (data already visible) | Spinner alongside stale data |
| Full-page blank loading | Skeleton grid matching page layout |

## Do NOT

- Do not use Spinner for initial page loads — use Skeleton.
- Do not show loading state after less than ~200ms (consider `suspense` delays).
- Do not use Skeleton with random widths — match expected content width.
