# Alert

Inline informational message. Persistent, non-dismissable feedback within page flow.

> NvUI status: there is no `NvAlert` yet — `Alert` / `AlertTitle` /
> `AlertDescription` are the current canonical exports from `@nerv-iip/ui`
> (原版 primitives kept as the app-facing name until a brand rebuild exists).

## Variants

| Variant       | When to use                                             |
| ------------- | ------------------------------------------------------- |
| `default`     | Neutral information, guidance                           |
| `destructive` | API errors, form submission failures, permission errors |

## Usage

```vue
<!-- API/server error above a form or table -->
<Alert v-if="error" variant="destructive">
  <AlertDescription>{{ error }}</AlertDescription>
</Alert>

<!-- Informational notice -->
<Alert>
  <AlertTitle>Read-only mode</AlertTitle>
  <AlertDescription>
    You do not have permission to modify IAM settings. Contact your administrator.
  </AlertDescription>
</Alert>
```

## Alert vs toast

| Use Alert                                       | Use toast()                                           |
| ----------------------------------------------- | ----------------------------------------------------- |
| Persistent error that blocks the current action | Transient success after a mutation                    |
| Server error from a page-level data fetch       | Transient error that the user already sees in context |
| Permission/auth warning                         | Background task completion                            |

## Do NOT

- Do not use `Alert` for success states — use `toast.success(...)`.
- Do not dismiss Alert manually — if the condition resolves, the `v-if` removes it automatically.
- Do not use `Alert` inside an `NvDialog` for validation errors — use `NvFieldError` per field instead.
- Do not stack multiple alerts — consolidate into one with a list if needed.
