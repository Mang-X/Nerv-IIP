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
  <Select v-model="form.roleType">
    <SelectTrigger id="role-type">
      <SelectValue placeholder="Choose a type…" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem value="system">System</SelectItem>
      <SelectItem value="custom">Custom</SelectItem>
    </SelectContent>
  </Select>
</Field>

<!-- Toolbar filter (no Field wrapper needed) -->
<Select v-model="statusFilter">
  <SelectTrigger class="w-36" aria-label="Filter by status">
    <SelectValue placeholder="Status" />
  </SelectTrigger>
  <SelectContent>
    <SelectItem value="all">All statuses</SelectItem>
    <SelectItem value="enabled">Enabled</SelectItem>
    <SelectItem value="disabled">Disabled</SelectItem>
  </SelectContent>
</Select>
```

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
- Do not use Input for selecting from a fixed list — use Select.
- Do not use Select for searching large datasets — that requires a Combobox (not yet installed).
- Do not omit `for`/`id` pairing when inside a `Field`.
