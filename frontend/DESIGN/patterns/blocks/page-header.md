# Block: Page Header

Section heading with title and description. Implemented as `IamPageHeader` component.

## Component location

`frontend/apps/console/src/components/iam/IamPageHeader.vue`

## Usage

```vue
<IamPageHeader
  title="Users"
  description="Manage system users and their role assignments."
/>
```

## Output HTML

```html
<header class="flex flex-col gap-1">
  <h1 class="text-2xl font-semibold tracking-normal text-foreground">Users</h1>
  <p class="max-w-3xl text-sm leading-6 text-muted-foreground">…</p>
</header>
```

## Inline equivalent (for pages that don't use IamPageHeader)

```vue
<div class="flex flex-col gap-1">
  <h1 class="text-2xl font-semibold tracking-tight">Instances</h1>
  <p class="text-sm text-muted-foreground">Active application instances managed by the Gateway.</p>
</div>
```

## When to add a header action

If the primary page action is complex or needs to be separated from the toolbar:

```vue
<div class="flex items-start justify-between gap-4">
  <IamPageHeader title="Users" description="…" />
  <Button type="button">Export</Button>
</div>
```

## Do NOT

- Do not skip the description — it provides context for first-time users and screen readers.
- Do not use `<h2>` for page-level headings — use `<h1>` (only one `h1` per page).
- Do not put IamPageHeader inside a Card.
