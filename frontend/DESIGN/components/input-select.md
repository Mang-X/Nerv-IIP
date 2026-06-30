# Input / Select

Text input and fixed-option selector. Always used inside a `Field` context.

## Input

```vue
<!-- Plain text -->
<Field>
  <FieldLabel for="login-name">Login name</FieldLabel>
  <Input id="login-name" v-model="form.loginName" required />
  <FieldError v-if="errors.loginName">{{ errors.loginName }}</FieldError>
</Field>

<!-- Search with icon prefix -->
<div class="relative">
  <SearchIcon
    class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
    aria-hidden="true"
  />
  <Input v-model="search" class="pl-8" type="search" placeholder="Search…" />
</div>

<!-- Password -->
<Input v-model="form.password" type="password" autocomplete="current-password" />
```

## Select

```vue
<Field>
  <FieldLabel for="role-type">Role type</FieldLabel>
  <SelectPro v-model="form.roleType">
    <SelectProTrigger id="role-type">
      <SelectProValue placeholder="Choose a type…" />
    </SelectProTrigger>
    <SelectProContent>
      <SelectProItem value="system">System</SelectProItem>
      <SelectProItem value="custom">Custom</SelectProItem>
    </SelectProContent>
  </SelectPro>
</Field>

<!-- Toolbar filter (no Field wrapper needed) -->
<SelectPro v-model="statusFilter">
  <SelectProTrigger class="w-36" aria-label="Filter by status">
    <SelectProValue placeholder="Status" />
  </SelectProTrigger>
  <SelectProContent>
    <SelectProItem value="all">All statuses</SelectProItem>
    <SelectProItem value="enabled">Enabled</SelectProItem>
    <SelectProItem value="disabled">Disabled</SelectProItem>
  </SelectProContent>
</SelectPro>
```

Use `SelectPro` for desktop product UI. Consumers may pass layout-only classes
such as width or compact height, but should not restyle trigger/content/item
colors; those states belong to the component contract.

## Input Types

| Type | Use case |
|---|---|
| `text` | Default |
| `email` | Email address (enables browser validation) |
| `password` | Credentials (always add `autocomplete`) |
| `search` | Search inputs (renders a clear button in most browsers) |
| `number` | Integer quantities |

## Do NOT

- Do not use `<input>` directly — always use `<Input>` from `@nerv-iip/ui`.
- Do not use Input for selecting from a fixed list — use `SelectPro`.
- Do not use `SelectPro` for searching large datasets — that requires a Combobox (not yet installed).
- Do not omit `for`/`id` pairing when inside a `Field`.
