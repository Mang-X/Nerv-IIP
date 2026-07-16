# Date Picker (NvDatePicker / NvDateRangePicker / NvTimePicker)

Date/time controls for desktop product UI, exported from `@nerv-iip/ui`:

- `NvDatePicker` — single date. `v-model` is a `YYYY-MM-DD` string or `null`;
  props `placeholder`, `disabled`.
- `NvDateRangePicker` — range. `v-model` is `DateRange`
  (`{ start: string | null, end: string | null }`) or `null`.
- `NvTimePicker` — time selection for schedule/window inputs.

The un-prefixed `DatePicker` / `DateRangePicker` (and their `DateRangeValue`
type) plus `Calendar` / `RangeCalendar` are 原版 / low-level exports —
library-internal only; do not use them in app code.

## Contract

1. Values stay DateOnly-compatible strings; consumers convert to endpoint DTOs at the app boundary.
2. Disabled and clearable states are handled by props; pages should not reimplement clear buttons.
3. Triggers are compact and suitable for toolbar filters and form fields.

## Usage

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { NvDatePicker, NvDateRangePicker, type DateRange } from '@nerv-iip/ui'

const dueDate = ref<string | null>(null)
const plannedWindow = ref<DateRange | null>(null)
</script>

<template>
  <NvDatePicker v-model="dueDate" />
  <NvDateRangePicker v-model="plannedWindow" />
</template>
```

## Rules

- Do not import calendar or popover internals from deep paths in app code — use the `@nerv-iip/ui` barrel export.
- Do not compose Popover + native date inputs by hand — that pre-NvUI pattern is superseded by `NvDatePicker` / `NvDateRangePicker`.
