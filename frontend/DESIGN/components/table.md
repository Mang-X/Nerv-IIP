# Table

Displays tabular data for entity lists.

## Anatomy

```
Table
  TableHeader
    TableRow
      TableHead (column label)
  TableBody
    TableRow (data row, v-for)
      TableCell
    TableEmpty (zero results)
```

## Usage

```vue
<div class="overflow-hidden rounded-lg border bg-background">
  <Table>
    <TableHeader>
      <TableRow>
        <TableHead>Name</TableHead>
        <TableHead>Email</TableHead>
        <TableHead class="w-16 text-right">Actions</TableHead>
      </TableRow>
    </TableHeader>
    <TableBody>
      <!-- Loading state -->
      <template v-if="pending">
        <TableRow v-for="i in 5" :key="i">
          <TableCell><Skeleton class="h-5 w-32" /></TableCell>
          <TableCell><Skeleton class="h-5 w-48" /></TableCell>
          <TableCell><Skeleton class="ml-auto h-8 w-8" /></TableCell>
        </TableRow>
      </template>

      <!-- Empty state -->
      <TableEmpty v-else-if="items.length === 0" :colspan="3">
        No items match the current filters.
      </TableEmpty>

      <!-- Data rows -->
      <TableRow v-for="item in items" v-else :key="item.id">
        <TableCell class="font-medium">{{ item.name }}</TableCell>
        <TableCell class="text-muted-foreground">{{ item.email }}</TableCell>
        <TableCell class="text-right">
          <!-- row actions via DropdownMenu -->
        </TableCell>
      </TableRow>
    </TableBody>
  </Table>
</div>
```

## Column Conventions

| Column type | `TableHead` / `TableCell` classes |
|---|---|
| Primary identifier | `font-medium` |
| UUID / technical ID | `font-mono text-xs text-muted-foreground` |
| Status | contains `<Badge>` only |
| Actions | `class="w-16 text-right"` on head; `class="text-right"` on cell |
| Timestamps | `text-muted-foreground` |

## Do NOT

- Do not wrap Table in a `<Card>` — use `overflow-hidden rounded-lg border bg-background` div wrapper instead.
- Do not add extra `py-*` padding to `TableCell`; the default density is intentional.
- Do not put action buttons directly in cells — always use `DropdownMenu` for multiple actions.
- Do not skip `TableEmpty` — it must always be present when data can be empty.
