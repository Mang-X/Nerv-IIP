# Block: App Shell

The application chrome, from `@nerv-iip/app-shell`. **`AppShellT`** is the long-term
T-shaped shell (FE-3); **`AppShell`** below is the legacy two-level sidebar kept for
migration compatibility.

## AppShellT — T-shaped shell (top domains + domain-local side nav)

Built on FE-2's `AppShellInset` (dashboard-01 `variant="inset"`). Top bar = permission-
filtered capability domains (overflow → "更多"); left = the current domain's side nav;
top-right = command search (⌘/Ctrl+K, placeholder until FE-13), `#header-actions` slot
(theme controls) and the user menu.

```ts
// Exported from @nerv-iip/app-shell
interface NavDomain { id: string, title: string, icon?: Component, to?: RouteLocationRaw, requiredPermissions?: string[] }
interface NavLink { title: string, to: RouteLocationRaw, icon?: Component, requiredPermissions?: string[] }
interface NavGroup { label?: string, items: NavLink[] }
type SideNav = NavGroup[]

// AppShellT props
{ title, topDomains: NavDomain[], currentDomainId?, sideNav?: SideNav,
  maxVisibleDomains? = 7, user?: ShellUser, signOutLabel?, searchLabel?, recent?, starred? }
// emits: signOut, openSearch   // slots: #header-actions, #default
```

The consuming app owns the nav model + RBAC filtering (e.g. business-console
`src/navigation.ts`) and route→domain resolution; the shell does **no** enforcement.
See `docs/architecture/frontend-navigation-map.md` for the Business Console menu division.

## Legacy: AppShell (collapsible sidebar + inset content)

The legacy chrome combining **sidebar-07** (collapsible sidebar with icon-only mode) and **sidebar-01** (header with `border-b` separator and `p-4` content spacing). Kept for compatibility; new layouts use `AppShellT`.

## Component location

| File | Role |
|---|---|
| `packages/app-shell/src/AppShell.vue` | Root layout: SidebarProvider → Sidebar + SidebarInset |
| `packages/app-shell/src/NavMain.vue` | Collapsible nav groups + simple link items |
| `packages/app-shell/src/NavUser.vue` | User dropdown in sidebar footer |

## Interface

```ts
// Exported from @nerv-iip/app-shell
interface NavSubItem {
  title: string
  to: RouteLocationRaw
}

interface NavItem {
  title: string
  to?: RouteLocationRaw   // leaf item (direct link)
  icon?: Component         // lucide-vue-next icon component
  isActive?: boolean       // default-open for collapsible groups
  items?: NavSubItem[]     // group with sub-items (makes it collapsible)
}

// Props
props: {
  title: string           // brand name shown in sidebar header
  navItems: NavItem[]
  user?: {
    name: string
    email?: string
  }
}

// Emits
signOut: []

// Slots
#header   // content after separator in topbar (breadcrumb — see DefaultLayout)
#default  // page content
```

## Usage in DefaultLayout

```vue
<AppShell title="Nerv-IIP" :nav-items="navItems" :user="shellUser" @sign-out="signOut">
  <template #header>
    <Breadcrumb>
      <BreadcrumbList>
        <BreadcrumbItem class="hidden md:block">
          <span class="text-muted-foreground">IAM</span>
        </BreadcrumbItem>
        <BreadcrumbSeparator class="hidden md:block" />
        <BreadcrumbItem>
          <BreadcrumbPage>Users</BreadcrumbPage>
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  </template>
  <slot />
</AppShell>
```

## Nav item conventions

```ts
const navItems: NavItem[] = [
  { title: 'Instances', icon: LayersIcon, to: { name: '/' } },
  {
    title: 'IAM',
    icon: ShieldIcon,
    isActive: true,
    items: [
      { title: 'Users', to: { path: '/iam/users' } },
      { title: 'Roles', to: { path: '/iam/roles' } },
      { title: 'Sessions', to: { path: '/iam/sessions' } },
    ],
  },
]
```

- Leaf items have `to` and no `items`.
- Group items have `items` and no `to`. Set `isActive: true` to default the collapsible open.
- `to` accepts any `RouteLocationRaw` (named routes, path objects).

## Layout

- **Sidebar**: sidebar-07 — collapsible (16rem expanded, 3rem icon-only), state persisted to `localStorage` key `sidebar_state`.
- **Topbar**: sidebar-01 style — `h-16`, `border-b`, `px-4` directly on `<header>` (no inner div). Shrinks to `h-12` when sidebar is in icon mode.
- **Content area**: sidebar-01 style — `p-4` on all sides (1rem gap between topbar border and content).
- **Mobile** (< 768px): Sidebar hidden, toggle via SidebarTrigger button.

## Component hierarchy

```
SidebarProvider          (state management + TooltipProvider)
├── Sidebar              (collapsible="icon")
│   ├── SidebarHeader    (brand logo "N" + title)
│   ├── SidebarContent
│   │   └── NavMain      (SidebarGroup → Collapsible menu items)
│   ├── SidebarFooter
│   │   └── NavUser      (avatar + user dropdown with sign-out)
│   └── SidebarRail      (drag handle for collapse/expand)
└── SidebarInset
    ├── header           (border-b, px-4: SidebarTrigger + Separator + #header slot)
    └── div.p-4          (page content via #default slot)
```

## Adding a new nav section

1. Add a `NavItem` to the `navItems` array in `DefaultLayout.vue`.
2. Leaf items must have `to` (a valid `RouteLocationRaw`).
3. Group items must have `items` and no `to`.
4. Icons are optional but recommended for top-level items.
5. Set `isActive: true` on groups that should be expanded by default.

## Do NOT

- Do not put business logic inside `AppShell` — it is purely structural.
- Do not modify `AppShell.vue` for per-page layout tweaks — use the `#header` slot or a second layout.
- Do not add a second sidebar — all navigation lives in this single shell.
- Do not wrap `SidebarProvider` in a `TooltipProvider` — `SidebarProvider` already includes one.
