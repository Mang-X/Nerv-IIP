# Date Picker

Date controls are design-system components exported from `@nerv-iip/ui`. The current #143 baseline composes `Popover` with compact native date inputs for reliable MVP form and filter workflows. `Calendar` and `RangeCalendar` are exported as low-level Reka root primitives only; they are not yet styled, full calendar-grid components.

## Exports

- `DatePicker`
- `DateRangePicker`
- `DateRangeValue`
- `Calendar` low-level Reka root
- `RangeCalendar` low-level Reka root
- `Popover` and public popover parts

## Contract

1. `DatePicker` accepts `v-model` as a `YYYY-MM-DD` string or `null`.
2. `DateRangePicker` accepts `v-model` as `{ from?: string | null, to?: string | null }` or `null`.
3. Components keep API-bound values DateOnly-compatible; consumers convert to endpoint DTOs at the app boundary.
4. Range selection uses explicit `Apply`, `Cancel`, and `Clear` actions; each action closes the popover after updating or restoring the draft value.
5. Triggers are compact outline buttons suitable for toolbar filters and form fields.
6. Disabled and clearable states are handled by props; pages should not reimplement clear buttons.
7. App pages should use `DatePicker` and `DateRangePicker` for product UI. Direct `Calendar` and `RangeCalendar` usage is reserved for future design-system work until styled parts are added.

## Usage

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { DatePicker, DateRangePicker, type DateRangeValue } from '@nerv-iip/ui'

const dueDate = ref<string | null>(null)
const plannedWindow = ref<DateRangeValue | null>(null)
</script>

<template>
  <DatePicker v-model="dueDate" />
  <DateRangePicker v-model="plannedWindow" />
</template>
```

## Rules

Do not import calendar or popover internals from deep paths in app code. Use the `@nerv-iip/ui` barrel export.
