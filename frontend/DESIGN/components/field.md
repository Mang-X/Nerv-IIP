# Field

Form field wrapper with label, description, and error messaging.

## Anatomy

```
Field (or FieldSet for groups)
  FieldLabel
  FieldGroup (optional: icon prefix/suffix wrapper)
    Input / Select / Checkbox
  FieldDescription (optional: help text)
  FieldError (conditional: validation error)
```

## Usage

```vue
<!-- Simple field -->
<Field>
  <FieldLabel for="email">Email address</FieldLabel>
  <Input id="email" v-model="form.email" type="email" required />
  <FieldDescription>Used for login and notifications.</FieldDescription>
  <FieldError v-if="errors.email">{{ errors.email }}</FieldError>
</Field>

<!-- Field with icon prefix -->
<Field>
  <FieldLabel for="search">Search</FieldLabel>
  <FieldGroup>
    <div class="relative">
      <SearchIcon class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
      <Input id="search" v-model="search" class="pl-8" type="search" />
    </div>
  </FieldGroup>
</Field>

<!-- Checkbox inside FieldSet -->
<FieldSet>
  <FieldLegend>Permissions</FieldLegend>
  <FieldGroup class="gap-2">
    <Field v-for="p in permissions" :key="p.code">
      <Checkbox :id="p.code" v-model:checked="selected[p.code]" />
      <FieldLabel :for="p.code">{{ p.code }}</FieldLabel>
    </Field>
  </FieldGroup>
</FieldSet>
```

## Stack Layout

Form fields should stack with `gap-4` in a `FieldGroup` or `<form class="grid gap-4">`.

## Do NOT

- Do not use raw `<label>` elements — always use `FieldLabel`.
- Do not use raw `<p>` for error text — always use `FieldError`.
- Do not place `FieldError` outside a `Field` context.
- Do not skip `for` / `id` pairing between `FieldLabel` and the input.
