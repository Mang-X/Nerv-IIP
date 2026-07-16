# Card (NvCard)

Content grouping surface. Used for settings sections, detail panels, stat
summaries, and login forms. App code uses the `NvCard*` family from
`@nerv-iip/ui`; the un-prefixed `Card*` parts are the shadcn 原版 primitives —
library-internal only. For KPI/stat tiles, prefer the purpose-built
`NvMetricCard` (or `NvSectionCard`/`NvSectionCards` for dashboard stat rows)
over hand-building with `NvCard`.

## Anatomy

```
NvCard
  NvCardHeader
    NvCardTitle
    NvCardDescription
    NvCardAction      (optional: right-aligned action button)
  NvCardContent
  NvCardFooter        (optional: form actions, links)
```

## Usage

```vue
<!-- Settings section or entity detail -->
<NvCard>
  <NvCardHeader>
    <NvCardTitle>User profile</NvCardTitle>
    <NvCardDescription>View and update basic identity information.</NvCardDescription>
  </NvCardHeader>
  <NvCardContent class="grid gap-4">
    <!-- content -->
  </NvCardContent>
</NvCard>

<!-- Card with header action -->
<NvCard>
  <NvCardHeader>
    <NvCardTitle>API keys</NvCardTitle>
    <NvCardAction>
      <NvButton variant="outline" size="sm" type="button">Add key</NvButton>
    </NvCardAction>
  </NvCardHeader>
  <NvCardContent>
    <!-- content -->
  </NvCardContent>
</NvCard>

<!-- Form card (e.g. login) -->
<NvCard class="w-full max-w-sm">
  <NvCardHeader>
    <NvCardTitle>Sign in</NvCardTitle>
  </NvCardHeader>
  <NvCardContent>
    <form class="grid gap-4" @submit.prevent="submit">
      <!-- fields -->
    </form>
  </NvCardContent>
  <NvCardFooter>
    <NvButton type="submit" class="w-full">Sign in</NvButton>
  </NvCardFooter>
</NvCard>
```

## Do NOT

- Do not wrap an `NvDataTable` inside an `NvCard` — the table brings its own bordered surface.
- Do not add `p-*` padding to `NvCard` itself — `NvCardContent` already has the correct padding.
- Do not use `NvCard` for every grouping — flat sections with just a heading are preferable for dense admin pages.
