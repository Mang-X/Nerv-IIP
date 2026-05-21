# Card

Content grouping surface. Used for settings sections, detail panels, stat summaries, and login forms.

## Anatomy

```
Card
  CardHeader
    CardTitle
    CardDescription
    CardAction      (optional: right-aligned action button)
  CardContent
  CardFooter        (optional: form actions, links)
```

## Usage

```vue
<!-- Settings section or entity detail -->
<Card>
  <CardHeader>
    <CardTitle>User profile</CardTitle>
    <CardDescription>View and update basic identity information.</CardDescription>
  </CardHeader>
  <CardContent class="grid gap-4">
    <!-- content -->
  </CardContent>
</Card>

<!-- Card with header action -->
<Card>
  <CardHeader>
    <CardTitle>API keys</CardTitle>
    <CardAction>
      <Button variant="outline" size="sm" type="button">Add key</Button>
    </CardAction>
  </CardHeader>
  <CardContent>
    <!-- content -->
  </CardContent>
</Card>

<!-- Form card (e.g. login) -->
<Card class="w-full max-w-sm">
  <CardHeader>
    <CardTitle>Sign in</CardTitle>
  </CardHeader>
  <CardContent>
    <form class="grid gap-4" @submit.prevent="submit">
      <!-- fields -->
    </form>
  </CardContent>
  <CardFooter>
    <Button type="submit" class="w-full">Sign in</Button>
  </CardFooter>
</Card>
```

## Do NOT

- Do not wrap a `Table` inside a `Card` — use the `overflow-hidden rounded-lg border bg-background` div wrapper instead (tables have their own border).
- Do not add `p-*` padding to `Card` itself — `CardContent` already has the correct padding.
- Do not use `Card` for every grouping — flat sections with just a heading are preferable for dense admin pages.
