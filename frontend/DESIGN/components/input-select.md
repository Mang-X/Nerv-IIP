# Input / Select (NvInput / NvSelect)

Text input and fixed-option selector. Always used inside an `NvField` context
in forms. App code uses `NvInput` and the `NvSelect*` family from
`@nerv-iip/ui`; the un-prefixed `Input` / `Select*` are shadcn 原版 primitives
— library-internal only.

## Input

```vue
<!-- Plain text -->
<NvField>
  <NvFieldLabel for="login-name">Login name</NvFieldLabel>
  <NvInput id="login-name" v-model="form.loginName" required />
  <NvFieldError v-if="errors.loginName">{{ errors.loginName }}</NvFieldError>
</NvField>

<!-- Search with icon prefix -->
<div class="relative">
  <SearchIcon
    class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
    aria-hidden="true"
  />
  <NvInput v-model="search" class="pl-8" type="search" placeholder="Search…" />
</div>

<!-- Password -->
<NvInput v-model="form.password" type="password" autocomplete="current-password" />
```

## Select

```vue
<NvField>
  <NvFieldLabel for="role-type">Role type</NvFieldLabel>
  <NvSelect v-model="form.roleType">
    <NvSelectTrigger id="role-type">
      <NvSelectValue placeholder="Choose a type…" />
    </NvSelectTrigger>
    <NvSelectContent>
      <NvSelectItem value="system">System</NvSelectItem>
      <NvSelectItem value="custom">Custom</NvSelectItem>
    </NvSelectContent>
  </NvSelect>
</NvField>

<!-- Toolbar filter (no NvField wrapper needed) -->
<NvSelect v-model="statusFilter">
  <NvSelectTrigger class="w-36" aria-label="Filter by status">
    <NvSelectValue placeholder="Status" />
  </NvSelectTrigger>
  <NvSelectContent>
    <NvSelectItem value="all">All statuses</NvSelectItem>
    <NvSelectItem value="enabled">Enabled</NvSelectItem>
    <NvSelectItem value="disabled">Disabled</NvSelectItem>
  </NvSelectContent>
</NvSelect>
```

Use `NvSelect` for desktop product UI. Consumers may pass layout-only classes
such as width or compact height, but should not restyle trigger/content/item
colors; those states belong to the component contract. Reka runtime constraint:
`NvSelectItem` must not have an empty-string `value`.

## Input Types

| Type       | Use case                                                |
| ---------- | ------------------------------------------------------- |
| `text`     | Default                                                 |
| `email`    | Email address (enables browser validation)              |
| `password` | Credentials (always add `autocomplete`)                 |
| `search`   | Search inputs (renders a clear button in most browsers) |
| `number`   | Integer quantities                                      |

## Do NOT

- Do not use `<input>` directly — always use `<NvInput>` from `@nerv-iip/ui`.
- Do not use `NvInput` for selecting from a fixed list — use `NvSelect`.
- Do not use `NvSelect` for searching large datasets — use `NvSearchSelect` (searchable popup single-select) or `NvCombobox` (type-to-filter with free input), both in `@nerv-iip/ui`.
- Do not omit `for`/`id` pairing when inside an `NvField`.
