export { cn } from './lib/utils'

export {
  ACCENT_PRESETS,
  ACCENT_STORAGE_KEY,
  COLOR_MODE_STORAGE_KEY,
  DEFAULT_ACCENT,
  initTheme,
  useColorMode,
  useThemeAccent,
} from './composables/useTheme'
export type { ColorMode } from './composables/useTheme'

// FE-2 custom block component library (distinct from原版 primitives above).
export * from './components/blocks'

// FE-2 Pro — copy-rebuilt premium components (restrained Vercel/Linear craft).
export * from './components/pro'

// Touch — large touch-optimized components for tablet boards / workshop kiosks.
export * from './components/touch'

// Layout — page scaffolding primitives (container, page+asides, grid, columns, sections).
export * from './components/layout'

export {
  Breadcrumb,
  BreadcrumbEllipsis,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from './components/ui/breadcrumb'
export {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupAction,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInput,
  SidebarInset,
  SidebarMenu,
  SidebarMenuAction,
  SidebarMenuBadge,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSkeleton,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarProvider,
  SidebarRail,
  SidebarSeparator,
  SidebarTrigger,
  useSidebar,
} from './components/ui/sidebar'

export {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogMedia,
  AlertDialogTitle,
  AlertDialogTrigger,
} from './components/ui/alert-dialog'
export { Alert, AlertDescription, AlertTitle } from './components/ui/alert'
export { Avatar, AvatarFallback, AvatarImage } from './components/ui/avatar'
export { Badge } from './components/ui/badge'
export { Button, buttonVariants } from './components/ui/button'
export { Calendar } from './components/ui/calendar'
export {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from './components/ui/card'
export {
  ChartContainer,
  ChartLegendContent,
  ChartTooltipContent,
  type ChartConfig,
} from './components/ui/chart'
export { Checkbox } from './components/ui/checkbox'
export { DatePicker, DateRangePicker, type DateRangeValue } from './components/ui/date-picker'
export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogOverlay,
  DialogScrollContent,
  DialogTitle,
  DialogTrigger,
} from './components/ui/dialog'
export {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from './components/ui/dropdown-menu'
export {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from './components/ui/empty'
export {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSeparator,
  FieldSet,
  FieldTitle,
} from './components/ui/field'
export { Input } from './components/ui/input'
export { Label } from './components/ui/label'
export {
  Pagination,
  PaginationContent,
  PaginationEllipsis,
  PaginationFirst,
  PaginationItem,
  PaginationLast,
  PaginationLink,
  PaginationNext,
  PaginationPrevious,
} from './components/ui/pagination'
export {
  Popover,
  PopoverAnchor,
  PopoverContent,
  PopoverDescription,
  PopoverHeader,
  PopoverTitle,
  PopoverTrigger,
} from './components/ui/popover'
export { Separator } from './components/ui/separator'
export {
  Sheet,
  SheetClose,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from './components/ui/sheet'
export {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectItemText,
  SelectLabel,
  SelectScrollDownButton,
  SelectScrollUpButton,
  SelectSeparator,
  SelectTrigger,
  SelectValue,
} from './components/ui/select'
export { Progress } from './components/ui/progress'
export { RangeCalendar } from './components/ui/range-calendar'
export { ScrollArea, ScrollBar } from './components/ui/scroll-area'
export { Skeleton } from './components/ui/skeleton'
export { Spinner } from './components/ui/spinner'
export { Tabs, TabsContent, TabsList, TabsTrigger } from './components/ui/tabs'
export { Toaster } from './components/ui/sonner'
export { toast } from 'vue-sonner'
export { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from './components/ui/tooltip'
export {
  Table,
  TableBody,
  TableCaption,
  TableCell,
  TableEmpty,
  TableFooter,
  TableHead,
  TableHeader,
  TableRow,
} from './components/ui/table'
export {
  FileUpload,
  uploadWithNativeFileStorageTransport,
  useFileUpload,
  type FileUploadCompletedFile,
  type FileUploadCompleteSessionRequest,
  type FileUploadCreateSessionRequest,
  type FileUploadExpose,
  type FileUploadMode,
  type FileUploadProvider,
  type FileUploadRejectedFile,
  type FileUploadRow,
  type FileUploadSession,
  type FileUploadTransport,
  type FileUploadTransportContext,
} from './components/ui/file-upload'
