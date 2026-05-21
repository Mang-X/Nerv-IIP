# Block: Data Table

Entity list table with loading skeleton, empty state, and row actions.

## Wrapper pattern

Always wrap `<Table>` in a border container:

```vue
<div class="overflow-hidden rounded-lg border bg-background">
  <Table>
    ...
  </Table>
</div>
```

## States to handle (all three are required)

| State | Implementation |
|---|---|
| Loading | `v-if="pending"` rows of `<Skeleton>` cells |
| Empty | `<TableEmpty :colspan="N">` message |
| Data | `v-for` rows |

Use `template v-if / v-else-if / v-else` pattern to avoid ambiguity.

## Row actions

Use `DropdownMenu` with `MoreHorizontalIcon` ghost icon button.

```vue
<TableCell class="text-right">
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button
        size="icon"
        variant="ghost"
        type="button"
        :aria-label="`Open actions for ${item.name}`"
        :disabled="!canManage"
      >
        <MoreHorizontalIcon class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem @select="emit('edit', item)">Edit</DropdownMenuItem>
      <DropdownMenuItem variant="destructive" @select="emit('delete', item)">
        Delete
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</TableCell>
```

## Skeleton sizing convention

Match skeleton width/height to the expected content shape:

| Content type | Skeleton class |
|---|---|
| Short text (name) | `h-5 w-32` |
| Medium text (email) | `h-5 w-48` |
| Long text (UUID) | `h-5 w-40` |
| Icon button | `h-8 w-8 ml-auto` |
| Status badge | `h-5 w-20` |

## Do NOT

- Do not skip the loading skeleton — never show an empty state during initial load.
- Do not use a loading overlay on top of data — swap between skeleton/empty/data via `v-if`.
- Do not put the `AlertDialog` confirm inside the Table component.
- Do not use `<style scoped>` in table components — all styling is via Tailwind utilities.
