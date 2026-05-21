# Block: Toolbar

Search input + optional filters + primary action. Implemented as the reusable `IamListToolbar` component.

## Component location

`frontend/apps/console/src/components/iam/IamListToolbar.vue`

## Props

| Prop | Type | Default | Purpose |
|---|---|---|---|
| `actionLabel` | `string` | required | Primary button label |
| `actionDisabled` | `boolean` | `false` | Gates action on permission |
| `searchLabel` | `string` | `'Search'` | Accessible label for input |
| `searchPlaceholder` | `string` | `'Search'` | Input placeholder |
| `showStatusFilter` | `boolean` | `false` | Show status Select |
| `statusOptions` | `{label,value}[]` | enabled/disabled | Dropdown options |

## Models

- `v-model:search` — string
- `v-model:status` — string (empty = "all")

## Usage

```vue
<IamListToolbar
  v-model:search="search"
  v-model:status="statusFilter"
  action-label="Create User"
  search-placeholder="Search by login name or email"
  show-status-filter
  :action-disabled="!canManage"
  @action="createOpen = true"
/>
```

## Layout

Responsive: stacks vertically on mobile, horizontal on `sm:`. Search input grows to fill available space. Action button is always right-aligned.

## Do NOT

- Do not add more than one primary action to this toolbar.
- Do not place the toolbar inside the Table component.
- Do not pass filter logic into the toolbar — it emits models, the parent filters.
