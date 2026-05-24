# Date Picker

Date controls are design-system components exported from `@nerv-iip/ui`. They compose `Popover`, `Calendar`, and `RangeCalendar` so business pages do not create page-local calendar styling.

## Exports

- `DatePicker`
- `DateRangePicker`
- `DateRangeValue`
- `Calendar` and public calendar parts
- `RangeCalendar` and public range-calendar parts
- `Popover` and public popover parts

## Contract

1. `DatePicker` accepts `v-model` as a `YYYY-MM-DD` string or `null`.
2. `DateRangePicker` accepts `v-model` as `{ from?: string | null, to?: string | null }` or `null`.
3. Components keep API-bound values DateOnly-compatible; consumers convert to endpoint DTOs at the app boundary.
4. Range selection uses explicit `Apply`, `Cancel`, and `Clear` actions.
5. Triggers are compact outline buttons suitable for toolbar filters and form fields.
6. Disabled and clearable states are handled by props; pages should not reimplement clear buttons.

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
