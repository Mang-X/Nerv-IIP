# Field (NvField)

Form field wrapper with label, description, and error messaging. App code uses
the `NvField*` family from `@nerv-iip/ui`; the un-prefixed `Field*` parts are
the shadcn 原版 primitives — library-internal only.

## Anatomy

```
NvField (or NvFieldSet for groups)
  NvFieldLabel
  NvFieldGroup (optional: grouping wrapper)
    NvInput / NvSelect / NvCheckbox
  NvFieldDescription (optional: help text)
  NvFieldError (conditional: validation error)
```

## Usage

```vue
<!-- Simple field -->
<NvField>
  <NvFieldLabel for="email">Email address</NvFieldLabel>
  <NvInput id="email" v-model="form.email" type="email" required />
  <NvFieldDescription>Used for login and notifications.</NvFieldDescription>
  <NvFieldError v-if="errors.email">{{ errors.email }}</NvFieldError>
</NvField>

<!-- Field with icon prefix -->
<NvField>
  <NvFieldLabel for="search">Search</NvFieldLabel>
  <div class="relative">
    <SearchIcon class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" aria-hidden="true" />
    <NvInput id="search" v-model="search" class="pl-8" type="search" />
  </div>
</NvField>

<!-- Checkbox inside NvFieldSet -->
<NvFieldSet>
  <NvFieldLegend>Permissions</NvFieldLegend>
  <NvFieldGroup class="gap-2">
    <NvField v-for="p in permissions" :key="p.code" orientation="horizontal">
      <NvCheckbox :id="p.code" v-model="selected[p.code]" />
      <NvFieldLabel :for="p.code">{{ p.code }}</NvFieldLabel>
    </NvField>
  </NvFieldGroup>
</NvFieldSet>
```

## Stack Layout

Form fields should stack with `gap-4` in an `NvFieldGroup` or
`<form class="grid gap-4">`. `NvField` supports
`orientation="vertical | horizontal | responsive"` (vertical is the default).

## Do NOT

- Do not use raw `<label>` elements — always use `NvFieldLabel`.
- Do not use raw `<p>` for error text — always use `NvFieldError`.
- Do not place `NvFieldError` outside an `NvField` context.
- Do not skip `for` / `id` pairing between `NvFieldLabel` and the input.
