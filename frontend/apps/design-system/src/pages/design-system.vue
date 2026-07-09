<script setup lang="ts">
import type {
  CommandGroup,
  CommandItem,
  NvDataTableColumn,
  DescriptionItem,
  TimelineItem,
} from '@nerv-iip/ui'
import {
  NvAreaChart,
  NvBadge,
  NvBarChart,
  NvButton,
  NvCard,
  NvCheckbox,
  NvCommand,
  NvDataTablePagination,
  NvDataTable,
  NvDataTableToolbar,
  NvDatePicker,
  NvDescriptions,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvDonutChart,
  NvInput,
  Label,
  NvRadioGroup,
  NvRadioGroupItem,
  NvSwitch,
  NvLineChart,
  NvLoader,
  messagePro,
  NvMetricCard,
  notificationPro,
  NvNotifierHost,
  NvPopconfirm,
  Progress,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Separator,
  Skeleton,
  NvStatusBadge,
  NvStatusDot,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  NvThemePicker,
  NvThemeToggle,
  NvTimePicker,
  NvTimeline,
  NvTooltip,
  NvTooltipContent,
  NvTooltipProvider,
  NvTooltipTrigger,
  useThemeAccent,
} from '@nerv-iip/ui'
import {
  ActivityIcon,
  ArrowRightIcon,
  FilePlus2Icon,
  FlagIcon,
  GaugeIcon,
  LayersIcon,
  ListFilterIcon,
  PackageCheckIcon,
  PlayIcon,
  PlusIcon,
  RocketIcon,
  SearchIcon,
  SparklesIcon,
  TriangleAlertIcon,
} from 'lucide-vue-next'
import { computed, onMounted, ref } from 'vue'

definePage({ meta: { title: '桌面设计系统' } })

const { accent, setAccent, presets } = useThemeAccent()
const accentEntries = Object.entries(presets) as [string, string][]
const accentNames: Record<string, string> = {
  blue: '锐蓝',
  indigo: '靛蓝',
  violet: '品紫',
  magenta: '洋红',
  rose: '绛红',
  red: '朱红',
  orange: '橙',
  amber: '琥珀',
  lime: '青柠',
  green: '翠绿',
  teal: '青碧',
  cyan: '湖蓝',
}

// ---- Foundations data ----
const tokenGroups = [
  {
    title: '表面与文字',
    desc: '画布低于卡片一档，形成内嵌悬浮的层次。',
    items: [
      { name: '--background', cls: 'bg-background', ring: true, role: '页面画布' },
      { name: '--card', cls: 'bg-card', ring: true, role: '卡片 / 面板' },
      { name: '--muted', cls: 'bg-muted', ring: true, role: '次级表面' },
      { name: '--foreground', cls: 'bg-foreground', role: '正文文字' },
      { name: '--muted-foreground', cls: 'bg-muted-foreground', role: '次要文字' },
      { name: '--border', cls: 'bg-border', ring: true, role: '描边' },
    ],
  },
  {
    title: '语义状态',
    desc: '状态只走语义令牌，去饱和处理，专业不刺眼。',
    items: [
      { name: '--primary', cls: 'bg-primary', role: '主操作' },
      { name: '--nv-brand', cls: 'bg-brand', role: '动态强调' },
      { name: '--nv-success', cls: 'bg-success', role: '健康 / 启用' },
      { name: '--nv-warning', cls: 'bg-warning', role: '预警' },
      { name: '--destructive', cls: 'bg-destructive', role: '危险 / 错误' },
      { name: '--ring', cls: 'bg-ring', role: '聚焦环' },
    ],
  },
]

const radiusScale = [
  { name: 'sm', cls: 'rounded-sm', token: '--radius-sm' },
  { name: 'md', cls: 'rounded-md', token: '--radius-md' },
  { name: 'lg', cls: 'rounded-lg', token: '--radius-lg' },
  { name: 'xl', cls: 'rounded-xl', token: '--radius-xl' },
  { name: 'full', cls: 'rounded-full', token: '9999px' },
]
const shadowScale = [
  { name: 'xs', cls: 'shadow-xs' },
  { name: 'sm', cls: 'shadow-sm' },
  { name: 'md', cls: 'shadow-md' },
  { name: 'lg', cls: 'shadow-lg' },
]
const typeScale = [
  { label: '页面标题', cls: 'text-2xl font-semibold tracking-tight', sample: '数字工厂控制平面' },
  { label: '区块标题', cls: 'text-lg font-semibold', sample: '在制工单总览' },
  { label: '正文', cls: 'text-sm', sample: '默认表格与表单内容，密度优先于装饰。' },
  {
    label: '辅助 / 弱化',
    cls: 'text-sm text-muted-foreground',
    sample: '编号、时间戳、提示语走弱化色。',
  },
  {
    label: '等宽',
    cls: 'font-mono text-xs text-muted-foreground',
    sample: 'WO-2406-0413 · 8f3a-21c0',
  },
]

// ---- Foundations: border + spacing ----
const borderScale = [
  { name: 'hairline', cls: 'border', label: '1px · 默认描边' },
  { name: 'medium', cls: 'border-2', label: '2px · 强调 / 选中' },
  { name: 'focus', cls: 'border-2 border-brand', label: '品牌 · 聚焦' },
]
const spacingScale = [
  { step: '1', px: '4px' },
  { step: '2', px: '8px' },
  { step: '3', px: '12px' },
  { step: '4', px: '16px' },
  { step: '6', px: '24px' },
  { step: '8', px: '32px' },
]

// ---- Components data ----
const agreed = ref(true)
const lineValue = ref('line-a')
const searchValue = ref('')
const switchOn = ref(true)
const radioValue = ref('std')
const planTime = ref('08:30')
const planDate = ref('2026-06-18')
const dialogOpen = ref(false)

const proLoading = ref(false)
function runProAction() {
  proLoading.value = true
  window.setTimeout(() => {
    proLoading.value = false
    notificationPro.success('工单已派发', {
      description: 'WO-2406-0431 已进入 A 线排队队列，物料已锁定。',
    })
  }, 1500)
}

const proMetrics = [
  {
    label: '产能利用率',
    value: '87.4%',
    trend: { value: '+4.2%', direction: 'up' as const },
    hint: '近 7 日均值',
    series: [72, 74, 73, 78, 80, 79, 83, 85, 87],
  },
  {
    label: 'OEE 综合效率',
    value: '78.6%',
    trend: { value: '-1.1%', direction: 'down' as const },
    hint: '目标 82%',
    series: [82, 81, 83, 80, 79, 80, 78, 79, 78],
  },
  {
    label: '当班产出',
    value: '4,210',
    trend: { value: '+312', direction: 'up' as const },
    hint: '件 · 截至 16:00',
    series: [2100, 2400, 2800, 3000, 3300, 3600, 3900, 4050, 4210],
  },
  {
    label: '在线良率',
    value: '99.2%',
    trend: { value: '0.0%', direction: 'flat' as const },
    hint: '质检实时判定',
    series: [99.0, 99.3, 99.1, 99.2, 99.0, 99.4, 99.2, 99.1, 99.2],
  },
]

const loaderVariants = [
  { v: 'ring' as const, name: '环形' },
  { v: 'dots' as const, name: '点阵' },
  { v: 'bars' as const, name: '柱条' },
  { v: 'pulse' as const, name: '脉冲' },
]

// ---- Feedback ----
function fireMessage(kind: 'info' | 'success' | 'warning' | 'error') {
  const map = {
    info: () => messagePro.info('已同步 MES 网关'),
    success: () => messagePro.success('保存成功'),
    warning: () => messagePro.warning('库存接近下限'),
    error: () => messagePro.error('网络连接中断'),
  }
  map[kind]()
}
function fireLongMessage() {
  messagePro.info(
    'WO-2406-0413 前桥壳体 A2 已下发至 A 线排队队列，物料已锁定，预计 14:20 开工，请关注节拍与首检结果。',
  )
}
function fireBurst() {
  messagePro.success('已保存草稿')
  window.setTimeout(() => messagePro.info('已同步 MES 网关'), 140)
  window.setTimeout(() => messagePro.warning('库存接近下限'), 280)
}
function fireNotification(kind: 'info' | 'success' | 'warning' | 'error') {
  const map = {
    info: () =>
      notificationPro.info('排产已更新', { description: '今日计划重排，受影响工单 6 张。' }),
    success: () =>
      notificationPro.success('保养工单已创建', { description: 'CNC-07 · 今晚 22:00 执行。' }),
    warning: () =>
      notificationPro.warning('B 线物料不足', {
        description: '液压阀体 V3 缺口 452 件，已转采购申请。',
      }),
    error: () =>
      notificationPro.error('派工失败', { description: '工作中心 WC-ASM-04 处于阻塞状态。' }),
  }
  map[kind]()
}

// ---- ⌘K command palette ----
const cmdOpen = ref(false)
const cmdGroups: CommandGroup[] = [
  {
    label: '导航',
    items: [
      { id: 'instances', label: '实例总览', hint: 'G I', icon: LayersIcon },
      { id: 'orders', label: '工单工作台', hint: 'G O', icon: GaugeIcon },
      { id: 'iam', label: '用户与权限', icon: ActivityIcon, keywords: 'user role permission' },
    ],
  },
  {
    label: '快捷操作',
    items: [
      {
        id: 'new-wo',
        label: '新建工单',
        hint: '⌘N',
        icon: PlusIcon,
        keywords: 'create work order',
      },
      { id: 'dispatch', label: '派发到产线', icon: RocketIcon, keywords: 'dispatch line' },
      { id: 'search-material', label: '搜索物料编码', icon: SearchIcon, keywords: 'material' },
    ],
  },
]
function onCommandSelect(item: CommandItem) {
  messagePro.success(`执行：${item.label}`)
}

// ---- Charts ----
const outputSeries = [
  { label: '08:00', value: 420 },
  { label: '10:00', value: 680 },
  { label: '12:00', value: 1240 },
  { label: '14:00', value: 1880 },
  { label: '16:00', value: 2610 },
  { label: '18:00', value: 3180 },
  { label: '20:00', value: 4210 },
]
const planActual = [
  { day: '周一', plan: 900, actual: 860 },
  { day: '周二', plan: 950, actual: 980 },
  { day: '周三', plan: 1000, actual: 940 },
  { day: '周四', plan: 1000, actual: 1060 },
  { day: '周五', plan: 1100, actual: 1020 },
  { day: '周六', plan: 700, actual: 720 },
]
const planActualSeries = [
  { key: 'plan', label: '计划' },
  { key: 'actual', label: '实际' },
]
const outputByCenter = [
  { center: 'CNC-07', good: 412, scrap: 18 },
  { center: 'FORGE-02', good: 1200, scrap: 24 },
  { center: 'CNC-11', good: 96, scrap: 6 },
  { center: 'ASM-04', good: 188, scrap: 12 },
  { center: 'STAMP-01', good: 2740, scrap: 60 },
]
const outputByCenterSeries = [
  { key: 'good', label: '良品' },
  { key: 'scrap', label: '废品' },
]
const statusMix = [
  { label: '执行中', value: 38 },
  { label: '已完成', value: 52 },
  { label: '待处理', value: 22 },
  { label: '阻塞', value: 6 },
]

// ---- Workorders table ----
const workOrders = [
  {
    code: 'WO-2406-0413',
    product: '前桥壳体 A2',
    center: 'WC-CNC-07',
    qty: 480,
    done: 412,
    status: 'running',
  },
  {
    code: 'WO-2406-0419',
    product: '转向节 L',
    center: 'WC-FORGE-02',
    qty: 1200,
    done: 1200,
    status: 'completed',
  },
  {
    code: 'WO-2406-0421',
    product: '齿轮箱端盖',
    center: 'WC-CNC-11',
    qty: 320,
    done: 96,
    status: 'ready',
  },
  {
    code: 'WO-2406-0426',
    product: '液压阀体 V3',
    center: 'WC-ASM-04',
    qty: 640,
    done: 188,
    status: 'blocked',
  },
  {
    code: 'WO-2406-0430',
    product: '电机定子叠片',
    center: 'WC-STAMP-01',
    qty: 5000,
    done: 2740,
    status: 'pending',
  },
]

// ---- NvDataTable (toolbar · filter · column settings · paginate) ----
interface WoRow {
  code: string
  product: string
  center: string
  owner: string
  qty: number
  status: string
}
const WO_PRODUCTS = [
  '前桥壳体 A2',
  '转向节 L',
  '齿轮箱端盖',
  '液压阀体 V3',
  '电机定子叠片',
  '制动卡钳',
  '减速器壳体',
  '半轴齿轮',
  '油泵端盖',
  '冷却水套',
  '法兰盘 D80',
  '同步带轮',
]
const WO_CENTERS = ['WC-CNC-07', 'WC-FORGE-02', 'WC-CNC-11', 'WC-ASM-04', 'WC-STAMP-01']
const WO_OWNERS = ['张伟', '李娜', '王强', '刘洋', '陈静']
const WO_STATUS = ['running', 'completed', 'ready', 'blocked', 'pending']
const WO_QTYS = [480, 1200, 320, 640, 5000, 260, 180, 900, 420, 760, 1500, 340]
// Deterministic seed (no random) so the showcase renders identically every time.
const tableAll: WoRow[] = Array.from({ length: 48 }, (_, i) => ({
  code: `WO-2406-${String(401 + i * 3).padStart(4, '0')}`,
  product: WO_PRODUCTS[i % WO_PRODUCTS.length],
  center: WO_CENTERS[i % WO_CENTERS.length],
  owner: WO_OWNERS[i % WO_OWNERS.length],
  qty: WO_QTYS[(i * 5) % WO_QTYS.length],
  status: WO_STATUS[(i * 2) % WO_STATUS.length],
}))
const tableColumns: NvDataTableColumn<WoRow>[] = [
  {
    key: 'code',
    header: '工单号',
    sortable: true,
    filter: 'text',
    cellClass: 'font-mono text-xs',
    hideable: false,
  },
  { key: 'product', header: '产品', sortable: true, filter: 'text', cellClass: 'font-medium' },
  {
    key: 'center',
    header: '工作中心',
    filter: 'enum',
    cellClass: 'font-mono text-xs text-muted-foreground',
  },
  { key: 'owner', header: '负责人', filter: 'enum' },
  { key: 'qty', header: '数量', align: 'end', sortable: true, cellClass: 'tabular-nums' },
  {
    key: 'status',
    header: '状态',
    filter: 'enum',
    filterOptions: [
      { label: '执行中', value: 'running' },
      { label: '已完成', value: 'completed' },
      { label: '可开工', value: 'ready' },
      { label: '阻塞', value: 'blocked' },
      { label: '待处理', value: 'pending' },
    ],
  },
]
const tableSelected = ref<(string | number)[]>(['WO-2406-0401'])
const tableTabs = [
  { label: '全部', value: '' },
  { label: '执行中', value: 'running' },
  { label: '待处理', value: 'pending' },
  { label: '已完成', value: 'completed' },
  { label: '阻塞', value: 'blocked' },
]
function onTableRefresh() {
  messagePro.success('已刷新工单列表')
}

// ---- 独立操作栏 Toolbar 演示 ----
const tbSearch = ref('')
const tbTab = ref('running')
const tbDensity = ref<'comfortable' | 'compact'>('comfortable')
const tbTabs = [
  { label: '全部', value: 'all', count: 48 },
  { label: '执行中', value: 'running', count: 12 },
  { label: '待处理', value: 'pending', count: 9 },
  { label: '已完成', value: 'completed', count: 18 },
  { label: '已归档', value: 'archived', count: 9 },
]

// ---- 独立分页 Pagination 演示 ----
const pgManyPage = ref(8)
const pgManySize = ref(10)
const pgFewPage = ref(1)
const pgFewSize = ref(10)

// ---- 描述列表 Descriptions 演示（工单详情）----
const woDescItems: DescriptionItem[] = [
  { key: 'code', label: '工单号', value: 'WO-2406-0413' },
  { label: '产品', value: '前桥壳体 A2' },
  { label: '工作中心', value: 'WC-CNC-07' },
  { label: '负责人', value: '张伟' },
  { label: '计划数量', value: '480 件' },
  { label: '已完成', value: '312 件' },
  { key: 'status', label: '状态', value: 'running' },
  { label: '优先级', value: '高' },
  { label: '交期', value: '2026-06-22' },
  { label: '备注', value: '首件需三坐标全尺寸检测，合格后方可批量。', span: 3 },
]
const descClampItems: DescriptionItem[] = [
  { label: '首件检验工艺要求', value: '三坐标全尺寸' },
  { label: '关联物料批次号', value: 'MTL-7782-0034' },
  { label: '客户与交付地点', value: '一汽解放 · 长春' },
  { label: '首检报告存储路径', value: 'first-article-v3.pdf' },
]

// ---- 时间线 Timeline 演示（工序流转日志）----
const woTimeline: TimelineItem[] = [
  {
    key: 'created',
    title: '工单创建',
    label: '06-17 08:12',
    description: '由 MES 排产自动下发，物料已预占。',
    tone: 'neutral',
    icon: FilePlus2Icon,
  },
  {
    key: 'kitted',
    title: '物料齐套',
    label: '06-17 08:40',
    description: '5 项物料从 WH-A 出库并送达线边仓。',
    tone: 'success',
    icon: PackageCheckIcon,
  },
  {
    key: 'started',
    title: '开工',
    label: '06-17 09:05',
    description: '张伟在 WC-CNC-07 扫码开工，节拍 42s/件。',
    tone: 'brand',
    icon: PlayIcon,
  },
  {
    key: 'warn',
    title: '首检预警',
    label: '06-17 09:21',
    description: '孔径 Φ12.02 接近上公差，已通知工艺复核。',
    tone: 'warning',
    icon: TriangleAlertIcon,
  },
]

const stats = [
  { k: '语义令牌', v: '32+' },
  { k: '动态色板', v: '12 色' },
  { k: 'Pro 组件', v: '18' },
  { k: '图表类型', v: '4' },
]

// ---- Reveal directive ----
const reduceMotion = ref(false)
onMounted(() => {
  reduceMotion.value =
    typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)').matches
})
const vReveal = {
  mounted(el: HTMLElement, binding: { value?: number }) {
    if (
      typeof window === 'undefined' ||
      window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ||
      !('IntersectionObserver' in window)
    ) {
      return
    }
    const delay = (Number(binding.value) || 0) * 45
    el.style.setProperty('--reveal-delay', `${delay}ms`)
    el.classList.add('reveal-init')
    const io = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            el.classList.add('reveal-in')
            io.unobserve(el)
          }
        }
      },
      { threshold: 0.1, rootMargin: '0px 0px -8% 0px' },
    )
    io.observe(el)
  },
}

const progress = computed(
  () => (workOrder: { done: number; qty: number }) =>
    Math.round((workOrder.done / workOrder.qty) * 100),
)
</script>

<template>
  <NvTooltipProvider :delay-duration="200">
    <div class="ds min-h-screen bg-background text-foreground">
      <!-- 顶栏 -->
      <header
        class="sticky top-0 z-30 border-b border-border/70 bg-background/80 backdrop-blur-xl supports-[backdrop-filter]:bg-background/65"
      >
        <div class="mx-auto flex h-14 max-w-6xl items-center gap-3 px-6">
          <div
            class="flex size-7 items-center justify-center rounded-md bg-primary text-primary-foreground"
          >
            <SparklesIcon class="size-4" aria-hidden="true" />
          </div>
          <div class="flex items-baseline gap-2 whitespace-nowrap">
            <span class="text-sm font-semibold tracking-tight">Nerv-IIP</span>
            <span class="hidden text-sm text-muted-foreground sm:inline">设计系统 v2</span>
          </div>
          <div class="ml-auto flex items-center gap-1">
            <NvButton
              variant="ghost"
              size="sm"
              class="hidden sm:inline-flex"
              @click="cmdOpen = true"
            >
              <template #leading><SearchIcon aria-hidden="true" /></template>
              命令面板
              <template #trailing>
                <kbd
                  class="rounded border border-border bg-muted px-1 font-mono text-[10px] text-muted-foreground"
                  >⌘K</kbd
                >
              </template>
            </NvButton>
            <NvThemePicker />
            <NvThemeToggle />
          </div>
        </div>
      </header>

      <!-- Hero -->
      <section class="relative overflow-hidden border-b border-border/70">
        <div class="ds-hero-glow" aria-hidden="true" />
        <div class="relative mx-auto max-w-6xl px-6 py-20">
          <p class="flex items-center gap-2 text-sm font-medium text-muted-foreground">
            <NvStatusDot tone="info" pulse /> 工业智能平台 · 控制平面组件库
          </p>
          <h1 class="mt-5 max-w-2xl text-4xl font-semibold tracking-tight text-balance sm:text-5xl">
            克制、精确、可信赖的工厂界面语言
          </h1>
          <p class="mt-5 max-w-xl text-base text-muted-foreground text-pretty">
            近黑主色，去饱和语义色，亮暗双主题，12
            色动态品牌色运行时可切换。组件、图表、反馈与基础参数自成一套完整系统。
          </p>
          <div class="mt-8 flex flex-wrap items-center gap-3">
            <NvButton as="a" href="#components" variant="brand">
              浏览组件
              <template #trailing><ArrowRightIcon class="size-4" aria-hidden="true" /></template>
            </NvButton>
            <NvButton as="a" href="#foundations" variant="outline">设计基础</NvButton>
          </div>
          <dl
            class="mt-12 grid max-w-2xl grid-cols-2 gap-px overflow-hidden rounded-xl border border-border bg-border sm:grid-cols-4"
          >
            <div v-for="stat in stats" :key="stat.k" class="bg-card px-4 py-3.5">
              <dt class="text-sm text-muted-foreground">{{ stat.k }}</dt>
              <dd class="mt-0.5 text-lg font-semibold tabular-nums tracking-tight">{{ stat.v }}</dd>
            </div>
          </dl>
        </div>
      </section>

      <main class="mx-auto max-w-6xl space-y-20 px-6 py-20">
        <!-- ============ 基础 ============ -->
        <section id="foundations" v-reveal class="scroll-mt-20">
          <div class="ds-eyebrow">
            <h2 class="text-xl font-semibold tracking-tight">设计基础</h2>
            <p class="mt-1.5 text-sm text-muted-foreground">
              颜色、品牌色板、圆角、阴影、排印 —— 全部来自单一 OKLCH 令牌源。
            </p>
          </div>

          <!-- 语义令牌 -->
          <div class="mt-6 grid gap-6 lg:grid-cols-2">
            <div
              v-for="group in tokenGroups"
              :key="group.title"
              class="rounded-xl border border-border bg-card p-5 shadow-xs"
            >
              <h3 class="text-sm font-semibold">{{ group.title }}</h3>
              <p class="mt-1 text-sm text-muted-foreground">{{ group.desc }}</p>
              <ul class="mt-4 grid grid-cols-3 gap-3">
                <li v-for="t in group.items" :key="t.name" class="min-w-0">
                  <div
                    class="h-12 w-full rounded-lg"
                    :class="[t.cls, t.ring ? 'ring-1 ring-inset ring-border' : '']"
                  />
                  <p class="mt-2 truncate font-mono text-xs text-foreground">{{ t.name }}</p>
                  <p class="truncate text-xs text-muted-foreground">{{ t.role }}</p>
                </li>
              </ul>
            </div>
          </div>

          <!-- 动态品牌色板（12 色） -->
          <div class="mt-6 rounded-xl border border-border bg-card p-5 shadow-xs">
            <div class="flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <h3 class="text-sm font-semibold">动态品牌色板</h3>
                <p class="mt-1 text-sm text-muted-foreground">
                  点击任意色号，整页
                  <code class="font-mono text-xs">--nv-brand</code> 即时切换并持久化。
                </p>
              </div>
              <span class="text-xs text-muted-foreground">12 色 · OKLCH 同明度</span>
            </div>
            <div class="mt-4 grid grid-cols-6 gap-3 sm:grid-cols-12">
              <button
                v-for="[name, value] in accentEntries"
                :key="name"
                type="button"
                class="group/sw flex flex-col items-center gap-1.5 outline-none"
                :aria-label="`品牌色 ${accentNames[name] ?? name}`"
                :aria-pressed="accent === value"
                @click="setAccent(value)"
              >
                <span
                  class="size-9 rounded-lg ring-offset-2 ring-offset-card transition-transform duration-150 ease-out-quart group-hover/sw:scale-110 group-focus-visible/sw:ring-2 group-focus-visible/sw:ring-ring"
                  :class="accent === value ? 'ring-2 ring-foreground' : 'ring-1 ring-border'"
                  :style="{ backgroundColor: value }"
                />
                <span class="text-[11px] text-muted-foreground">{{
                  accentNames[name] ?? name
                }}</span>
              </button>
            </div>
          </div>

          <!-- 圆角 + 阴影 -->
          <div class="mt-6 grid gap-6 lg:grid-cols-2">
            <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
              <h3 class="text-sm font-semibold">圆角</h3>
              <div class="mt-4 flex flex-wrap items-end gap-5">
                <div
                  v-for="r in radiusScale"
                  :key="r.name"
                  class="flex flex-col items-center gap-2"
                >
                  <div class="size-14 border border-border bg-muted" :class="r.cls" />
                  <span class="font-mono text-xs text-muted-foreground">{{ r.name }}</span>
                </div>
              </div>
            </div>
            <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
              <h3 class="text-sm font-semibold">阴影 · 高度</h3>
              <div class="mt-4 flex flex-wrap items-center gap-6 px-1 py-2">
                <div
                  v-for="s in shadowScale"
                  :key="s.name"
                  class="flex flex-col items-center gap-2"
                >
                  <div class="size-14 rounded-lg bg-card" :class="s.cls" />
                  <span class="font-mono text-xs text-muted-foreground">{{ s.name }}</span>
                </div>
              </div>
            </div>
          </div>

          <!-- 边框 + 间距 -->
          <div class="mt-6 grid gap-6 lg:grid-cols-2">
            <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
              <h3 class="text-sm font-semibold">边框</h3>
              <div class="mt-4 flex flex-wrap items-end gap-5">
                <div
                  v-for="b in borderScale"
                  :key="b.name"
                  class="flex flex-col items-center gap-2"
                >
                  <div class="size-14 rounded-lg border-border bg-muted" :class="b.cls" />
                  <span class="font-mono text-xs text-muted-foreground">{{ b.name }}</span>
                </div>
              </div>
              <p class="mt-3 text-xs text-muted-foreground">
                发丝级 1px 描边为默认；2px 仅用于强调与聚焦。
              </p>
            </div>
            <div class="rounded-xl border border-border bg-card p-5 shadow-xs">
              <h3 class="text-sm font-semibold">间距 · 4px 基准</h3>
              <ul class="mt-4 space-y-2">
                <li v-for="s in spacingScale" :key="s.step" class="flex items-center gap-3">
                  <span class="w-8 font-mono text-xs text-muted-foreground">{{ s.step }}</span>
                  <span class="h-3 rounded-sm bg-brand/70" :style="{ width: s.px }" />
                  <span class="font-mono text-xs text-muted-foreground">{{ s.px }}</span>
                </li>
              </ul>
            </div>
          </div>

          <!-- 排印 -->
          <div
            class="mt-6 divide-y divide-border overflow-hidden rounded-xl border border-border bg-card"
          >
            <div
              v-for="row in typeScale"
              :key="row.label"
              class="flex flex-col gap-1 px-5 py-4 sm:flex-row sm:items-baseline sm:gap-6"
            >
              <span class="w-28 shrink-0 text-xs text-muted-foreground">{{ row.label }}</span>
              <span :class="row.cls">{{ row.sample }}</span>
            </div>
          </div>
        </section>

        <!-- ============ 组件 ============ -->
        <section id="components" v-reveal class="scroll-mt-20">
          <div class="ds-eyebrow">
            <div class="flex flex-wrap items-center gap-2">
              <h2 class="text-xl font-semibold tracking-tight">组件</h2>
              <NvBadge variant="brand">Pro</NvBadge>
            </div>
            <p class="mt-1.5 text-sm text-muted-foreground">
              复制重建于 shadcn 之上：分层表面、品牌聚焦、内建加载与状态反馈。
            </p>
          </div>

          <!-- 按钮 -->
          <div class="mt-6 space-y-5 rounded-xl border border-border bg-card p-6 shadow-xs">
            <div class="flex flex-wrap items-center gap-3">
              <NvButton variant="brand">主操作</NvButton>
              <NvButton variant="default">默认</NvButton>
              <NvButton variant="secondary">次要</NvButton>
              <NvButton variant="outline">描边</NvButton>
              <NvButton variant="ghost">幽灵</NvButton>
              <NvButton variant="destructive">危险</NvButton>
              <NvButton variant="link">链接</NvButton>
            </div>
            <Separator />
            <div class="flex flex-wrap items-center gap-3">
              <NvButton size="sm">小号</NvButton>
              <NvButton>默认</NvButton>
              <NvButton size="lg">大号</NvButton>
              <NvButton size="icon" aria-label="新建"><PlusIcon aria-hidden="true" /></NvButton>
              <NvButton disabled>禁用</NvButton>
              <NvButton
                variant="brand"
                :loading="proLoading"
                class="min-w-28"
                @click="runProAction"
              >
                {{ proLoading ? '派发中' : '确认派工' }}
              </NvButton>
            </div>
          </div>

          <!-- 徽章 + 状态 + 表单 -->
          <div class="mt-4 grid gap-4 lg:grid-cols-2">
            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">徽章与状态</h3>
              <div class="mt-4 flex flex-wrap gap-2">
                <NvBadge variant="brand">品牌</NvBadge>
                <NvBadge variant="success">已完成</NvBadge>
                <NvBadge variant="warning">待处理</NvBadge>
                <NvBadge variant="danger">阻塞</NvBadge>
                <NvBadge variant="solid">主要</NvBadge>
                <NvBadge>中性</NvBadge>
              </div>
              <div class="mt-4 flex flex-wrap gap-2">
                <NvStatusBadge value="running" pulse />
                <NvStatusBadge value="ready" />
                <NvStatusBadge value="completed" />
                <NvStatusBadge value="pending" />
                <NvStatusBadge value="blocked" />
              </div>
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">表单控件</h3>
              <div class="mt-4 grid gap-4">
                <div class="grid gap-2">
                  <Label for="ds-search">搜索</Label>
                  <NvInput id="ds-search" v-model="searchValue" placeholder="搜索工单号 / 产品">
                    <template #leading><SearchIcon aria-hidden="true" /></template>
                    <template #trailing>
                      <kbd
                        class="rounded border border-border bg-muted px-1.5 py-0.5 font-mono text-[10px] text-muted-foreground"
                        >⌘K</kbd
                      >
                    </template>
                  </NvInput>
                </div>
                <div class="grid gap-2">
                  <Label for="ds-line">目标产线</Label>
                  <NvSelect v-model="lineValue">
                    <NvSelectTrigger id="ds-line"
                      ><NvSelectValue placeholder="选择产线"
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem value="line-a">A 线 · 精密加工</NvSelectItem>
                      <NvSelectItem value="line-b">B 线 · 锻压</NvSelectItem>
                      <NvSelectItem value="line-c">C 线 · 总装</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </div>
                <div class="grid gap-2">
                  <Label>计划开工日期</Label>
                  <NvDatePicker v-model="planDate" placeholder="选择日期" />
                </div>
                <div class="grid gap-2">
                  <Label>计划开工时间</Label>
                  <NvTimePicker v-model="planTime" :minute-step="5" placeholder="选择时间" />
                </div>
                <label class="flex items-center gap-2.5 text-sm">
                  <NvCheckbox v-model="agreed" />
                  派发后立即锁定物料并生成领料单
                </label>
                <div class="flex items-center justify-between">
                  <span class="text-sm">加急插单</span>
                  <NvSwitch v-model="switchOn" />
                </div>
                <div class="grid gap-2">
                  <span class="text-sm font-medium">质检策略</span>
                  <NvRadioGroup v-model="radioValue">
                    <NvRadioGroupItem value="std">标准全检</NvRadioGroupItem>
                    <NvRadioGroupItem value="sample">抽样检验</NvRadioGroupItem>
                    <NvRadioGroupItem value="exempt">免检放行</NvRadioGroupItem>
                  </NvRadioGroup>
                </div>
              </div>
            </NvCard>
          </div>

          <!-- 标签页 + 加载态 -->
          <div class="mt-4 grid gap-4 lg:grid-cols-2">
            <NvCard class="p-6">
              <h3 class="mb-4 text-sm font-semibold">标签页</h3>
              <NvTabs default-value="overview">
                <NvTabsList>
                  <NvTabsTrigger value="overview">概览</NvTabsTrigger>
                  <NvTabsTrigger value="quality">质量</NvTabsTrigger>
                  <NvTabsTrigger value="maint">维护</NvTabsTrigger>
                </NvTabsList>
                <NvTabsContent value="overview" class="pt-4">
                  <p class="text-sm text-muted-foreground">当班共 12 条产线运行，2 条待料。</p>
                  <div class="mt-4 space-y-2">
                    <div class="flex items-center justify-between text-sm">
                      <span>计划达成</span
                      ><span class="tabular-nums text-muted-foreground">68%</span>
                    </div>
                    <Progress :model-value="68" />
                  </div>
                </NvTabsContent>
                <NvTabsContent value="quality" class="pt-4">
                  <p class="text-sm text-muted-foreground">在线判定良率 99.2%，无重大不合格。</p>
                </NvTabsContent>
                <NvTabsContent value="maint" class="pt-4">
                  <p class="text-sm text-muted-foreground">CNC-07 计划保养窗口：今晚 22:00。</p>
                </NvTabsContent>
              </NvTabs>
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">加载态</h3>
              <p class="mt-1 text-sm text-muted-foreground">
                四种克制形态，品牌着色，减弱动效下降级为静态。
              </p>
              <div class="mt-5 flex flex-wrap gap-8">
                <div
                  v-for="l in loaderVariants"
                  :key="l.v"
                  class="flex flex-col items-center gap-2"
                >
                  <NvLoader :variant="l.v" size="lg" />
                  <span class="font-mono text-xs text-muted-foreground">{{ l.name }}</span>
                </div>
              </div>
              <div class="mt-5 flex items-center gap-3">
                <Skeleton class="size-10 rounded-full" />
                <div class="flex-1 space-y-2">
                  <Skeleton class="h-3 w-2/5" />
                  <Skeleton class="h-3 w-3/5" />
                </div>
              </div>
            </NvCard>
          </div>

          <!-- 反馈：Message / Notification / Tooltip / ⌘K -->
          <div class="mt-4 grid gap-4 lg:grid-cols-2">
            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">轻提示 Message</h3>
              <p class="mt-1 text-sm text-muted-foreground">
                顶部居中、单行、短暂自动消失。超长文本省略，hover 看全文。对标传统组件库的 message。
              </p>
              <div class="mt-4 flex flex-wrap gap-2">
                <NvButton size="sm" variant="outline" @click="fireMessage('success')"
                  >成功</NvButton
                >
                <NvButton size="sm" variant="outline" @click="fireMessage('info')">信息</NvButton>
                <NvButton size="sm" variant="outline" @click="fireMessage('warning')"
                  >预警</NvButton
                >
                <NvButton size="sm" variant="outline" @click="fireMessage('error')">错误</NvButton>
                <NvButton size="sm" variant="outline" @click="fireLongMessage">长文本</NvButton>
                <NvButton size="sm" variant="brand" @click="fireBurst">连发 3 条</NvButton>
              </div>
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">通知 Notification</h3>
              <p class="mt-1 text-sm text-muted-foreground">
                右上角卡片，含标题、描述与关闭。对标 notification。
              </p>
              <div class="mt-4 flex flex-wrap gap-2">
                <NvButton size="sm" variant="outline" @click="fireNotification('success')"
                  >成功</NvButton
                >
                <NvButton size="sm" variant="outline" @click="fireNotification('info')"
                  >信息</NvButton
                >
                <NvButton size="sm" variant="outline" @click="fireNotification('warning')"
                  >预警</NvButton
                >
                <NvButton size="sm" variant="outline" @click="fireNotification('error')"
                  >错误</NvButton
                >
              </div>
              <div class="mt-4 flex items-center gap-3">
                <NvTooltip>
                  <NvTooltipTrigger as-child>
                    <NvButton variant="ghost" size="sm">
                      <template #leading><ActivityIcon aria-hidden="true" /></template>
                      悬停提示
                    </NvButton>
                  </NvTooltipTrigger>
                  <NvTooltipContent>实时采集自 MES 网关</NvTooltipContent>
                </NvTooltip>
                <NvButton variant="ghost" size="sm" @click="cmdOpen = true">
                  <template #leading><SearchIcon aria-hidden="true" /></template>
                  命令面板 ⌘K
                </NvButton>
                <NvDialog v-model:open="dialogOpen">
                  <NvDialogTrigger as-child>
                    <NvButton variant="ghost" size="sm">
                      <template #leading><GaugeIcon aria-hidden="true" /></template>
                      打开对话框
                    </NvButton>
                  </NvDialogTrigger>
                  <NvDialogContent>
                    <NvDialogHeader>
                      <NvDialogTitle>确认派发工单</NvDialogTitle>
                      <NvDialogDescription>
                        WO-2406-0431 将派发到 A 线，锁定物料并生成领料单。
                      </NvDialogDescription>
                    </NvDialogHeader>
                    <div class="rounded-lg border border-border bg-muted/40 p-4 text-sm">
                      <div class="flex justify-between">
                        <span class="text-muted-foreground">数量</span
                        ><span class="tabular-nums">480</span>
                      </div>
                      <div class="mt-2 flex justify-between">
                        <span class="text-muted-foreground">预计工时</span
                        ><span class="tabular-nums">14.5 h</span>
                      </div>
                    </div>
                    <NvDialogFooter>
                      <NvDialogClose as-child
                        ><NvButton variant="ghost">取消</NvButton></NvDialogClose
                      >
                      <NvButton variant="brand" @click="dialogOpen = false">确认派发</NvButton>
                    </NvDialogFooter>
                  </NvDialogContent>
                </NvDialog>
              </div>
            </NvCard>
          </div>
        </section>

        <!-- ============ 数据可视化 ============ -->
        <section id="charts" v-reveal class="scroll-mt-20">
          <div class="ds-eyebrow">
            <h2 class="text-xl font-semibold tracking-tight">数据可视化</h2>
            <p class="mt-1.5 text-sm text-muted-foreground">
              基于 unovis：面积、折线、柱状、环形覆盖常见工厂场景；颜色走令牌，随品牌色重着色。
            </p>
          </div>

          <div class="mt-6 grid gap-4 lg:grid-cols-2">
            <NvCard class="p-6">
              <div class="flex items-center justify-between">
                <div>
                  <h3 class="text-sm font-semibold">当班累计产出</h3>
                  <p class="mt-0.5 text-xs text-muted-foreground">面积图 · 每 2 小时采样</p>
                </div>
                <NvStatusBadge value="running" pulse />
              </div>
              <NvAreaChart class="mt-4" :data="outputSeries" :height="220" value-suffix=" 件" />
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">计划 vs 实际产量</h3>
              <p class="mt-0.5 text-xs text-muted-foreground">多系列折线 · 本周</p>
              <NvLineChart
                class="mt-4"
                :data="planActual"
                x-key="day"
                :series="planActualSeries"
                :height="220"
                value-suffix=" 件"
              />
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">各工作中心产出</h3>
              <p class="mt-0.5 text-xs text-muted-foreground">分组柱状 · 良品 / 废品</p>
              <NvBarChart
                class="mt-4"
                :data="outputByCenter"
                x-key="center"
                :series="outputByCenterSeries"
                :height="220"
              />
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">工单状态构成</h3>
              <p class="mt-0.5 mb-4 text-xs text-muted-foreground">环形占比 · 实时</p>
              <NvDonutChart
                :data="statusMix"
                :height="180"
                central-label="118"
                central-sub-label="工单"
              />
            </NvCard>
          </div>

          <!-- 指标卡 -->
          <div class="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
            <NvMetricCard
              v-for="(m, i) in proMetrics"
              :key="m.label"
              v-reveal="i"
              :label="m.label"
              :value="m.value"
              :trend="m.trend"
              :hint="m.hint"
              :series="m.series"
            />
          </div>

          <!-- 数据表 -->
          <div class="mt-4 overflow-hidden rounded-xl border border-border bg-card shadow-xs">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>工单号</TableHead>
                  <TableHead>产品</TableHead>
                  <TableHead>工作中心</TableHead>
                  <TableHead class="text-right">进度</TableHead>
                  <TableHead>状态</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="wo in workOrders" :key="wo.code">
                  <TableCell class="font-mono text-xs">{{ wo.code }}</TableCell>
                  <TableCell class="font-medium">{{ wo.product }}</TableCell>
                  <TableCell class="font-mono text-xs text-muted-foreground">{{
                    wo.center
                  }}</TableCell>
                  <TableCell>
                    <div class="flex items-center justify-end gap-2">
                      <Progress :model-value="progress(wo)" class="h-1.5 w-24" />
                      <span
                        class="w-16 text-right font-mono text-xs text-muted-foreground tabular-nums"
                      >
                        {{ wo.done }}/{{ wo.qty }}
                      </span>
                    </div>
                  </TableCell>
                  <TableCell><NvStatusBadge :value="wo.status" /></TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>

          <!-- NvDataTable：完整数据表（工具栏 + 字段筛选 + 列设置 + 密度 + 行选择 + 页码分页） -->
          <div class="mt-8 mb-3">
            <h3 class="text-sm font-semibold">数据表格 NvDataTable</h3>
            <p class="mt-1 text-xs text-muted-foreground">
              工具栏 + 快捷筛选标签 + 字段筛选 + 列显隐 + 密度 + 行选择 + 可点击页码 —— 客户端处理
              {{ tableAll.length }} 条工单。
            </p>
          </div>
          <NvDataTable
            :columns="tableColumns"
            :rows="tableAll"
            row-key="code"
            title="工单列表"
            description="近 30 天投放产线的全部工单"
            :tabs="tableTabs"
            tab-key="status"
            selectable
            refreshable
            search-placeholder="搜索工单号 / 产品 / 工作中心…"
            :page-size="10"
            v-model:selected="tableSelected"
            @refresh="onTableRefresh"
          >
            <template #cell-status="{ value }">
              <NvStatusBadge :value="String(value)" :pulse="value === 'running'" />
            </template>
            <template #bulk-actions>
              <NvButton variant="outline" size="sm">导出所选</NvButton>
              <NvPopconfirm
                :title="`确认作废选中的 ${tableSelected.length} 张工单？`"
                description="作废后不可恢复，已领用物料需手动退库。"
                confirm-text="作废"
                @confirm="messagePro.error('已作废所选工单')"
              >
                <NvButton variant="outline" size="sm">作废</NvButton>
              </NvPopconfirm>
              <NvButton variant="brand" size="sm">下发排产</NvButton>
            </template>
            <template #actions>
              <NvButton variant="brand" size="sm">
                <template #leading><PlusIcon /></template>
                新建工单
              </NvButton>
            </template>
          </NvDataTable>

          <!-- 操作栏 Toolbar（独立）-->
          <div class="mt-8 mb-3">
            <h3 class="text-sm font-semibold">操作栏 Toolbar</h3>
            <p class="mt-1 text-xs text-muted-foreground">
              可独立使用：标题 +
              实时计数、快捷筛选分段、搜索、字段筛选槽、密度、刷新、更多菜单（导出 /
              打印）与主操作。
            </p>
          </div>
          <NvDataTableToolbar
            v-model:search="tbSearch"
            v-model:tab="tbTab"
            v-model:density="tbDensity"
            title="工单列表"
            :count="48"
            :tabs="tbTabs"
            searchable
            search-placeholder="搜索工单…"
            show-density
            refreshable
            show-more
            @refresh="messagePro.info('正在刷新…')"
            @export="messagePro.success('已导出 CSV')"
            @print="messagePro.info('已发送到打印机')"
          >
            <template #filters>
              <NvButton variant="outline" size="sm">
                <template #leading><ListFilterIcon /></template>
                筛选
              </NvButton>
            </template>
            <template #actions>
              <NvButton variant="brand" size="sm">
                <template #leading><PlusIcon /></template>
                新建工单
              </NvButton>
            </template>
          </NvDataTableToolbar>

          <!-- 分页 Pagination（独立）-->
          <div class="mt-8 mb-3">
            <h3 class="text-sm font-semibold">分页 Pagination</h3>
            <p class="mt-1 text-xs text-muted-foreground">
              可独立使用：可点击页码、首末 / 上下页、多页自动省略号（… 悬停跳 5
              页）、每页条数与跳页。
            </p>
          </div>
          <div class="space-y-4">
            <div class="rounded-xl border bg-card px-3 py-3 shadow-sm sm:px-4">
              <p class="mb-2.5 text-xs text-muted-foreground">
                多页（528 条 · 53 页，含省略号 + 跳页）
              </p>
              <NvDataTablePagination
                :page="pgManyPage"
                :page-size="pgManySize"
                :total-items="528"
                show-jump
                @update:page="pgManyPage = $event"
                @update:page-size="pgManySize = $event"
              />
            </div>
            <div class="rounded-xl border bg-card px-3 py-3 shadow-sm sm:px-4">
              <p class="mb-2.5 text-xs text-muted-foreground">少页（36 条 · 4 页，无省略号）</p>
              <NvDataTablePagination
                :page="pgFewPage"
                :page-size="pgFewSize"
                :total-items="36"
                :page-size-options="[10, 20, 50]"
                @update:page="pgFewPage = $event"
                @update:page-size="pgFewSize = $event"
              />
            </div>
          </div>
        </section>

        <!-- ============ 详情展示 ============ -->
        <section id="detail" v-reveal class="scroll-mt-20">
          <div class="ds-eyebrow">
            <h2 class="text-xl font-semibold tracking-tight">详情展示</h2>
            <p class="mt-1.5 text-sm text-muted-foreground">
              键值详情（Descriptions）与工序流转（Timeline）—— 工单 / 设备详情页核心。
            </p>
          </div>

          <div class="mt-6 grid gap-4 lg:grid-cols-3">
            <NvCard class="p-6 lg:col-span-2">
              <h3 class="text-sm font-semibold">工单详情 · Descriptions</h3>
              <p class="mt-0.5 mb-4 text-xs text-muted-foreground">键值详情列表 · 3 列网格</p>
              <NvDescriptions :items="woDescItems" :columns="3">
                <template #code="{ item }">
                  <span class="font-mono text-xs">{{ item.value }}</span>
                </template>
                <template #status="{ item }">
                  <NvStatusBadge :value="String(item.value)" :pulse="item.value === 'running'" />
                </template>
              </NvDescriptions>
            </NvCard>

            <NvCard class="p-6">
              <h3 class="text-sm font-semibold">工序日志 · Timeline</h3>
              <p class="mt-0.5 mb-4 text-xs text-muted-foreground">工序流转 · 进行中脉冲</p>
              <NvTimeline :items="woTimeline" pending pending-text="精加工中…" />
            </NvCard>
          </div>

          <!-- 带边框变体 -->
          <div class="mt-4">
            <h3 class="mb-3 text-sm font-semibold">描述列表 · 带边框记录</h3>
            <NvDescriptions :items="woDescItems" :columns="3" bordered>
              <template #code="{ item }">
                <span class="font-mono text-xs">{{ item.value }}</span>
              </template>
              <template #status="{ item }">
                <NvStatusBadge :value="String(item.value)" :pulse="item.value === 'running'" />
              </template>
            </NvDescriptions>
          </div>

          <!-- 标题超长省略 + TooltipPro -->
          <div class="mt-4">
            <h3 class="mb-1 text-sm font-semibold">描述列表 · 标题超长省略</h3>
            <p class="mb-3 text-xs text-muted-foreground">
              <code class="font-mono">ellipsis</code> + <code class="font-mono">label-width</code>
              ：标题单行截断，悬停弹 TooltipPro 看全文（内容由你用插槽自定）。
            </p>
            <NvDescriptions
              :items="descClampItems"
              :columns="2"
              bordered
              ellipsis
              label-width="6rem"
            />
          </div>

          <!-- 气泡确认 Popconfirm -->
          <div class="mt-4">
            <h3 class="mb-3 text-sm font-semibold">气泡确认 · Popconfirm</h3>
            <NvCard class="flex flex-wrap items-center gap-3 p-5">
              <NvPopconfirm
                title="确认删除该工单？"
                description="删除后不可恢复。"
                confirm-text="删除"
                @confirm="messagePro.error('工单已删除')"
              >
                <NvButton variant="outline" size="sm">删除工单</NvButton>
              </NvPopconfirm>
              <NvPopconfirm
                title="确认下发到产线？"
                confirm-text="下发"
                confirm-tone="brand"
                @confirm="messagePro.success('已下发排产')"
              >
                <NvButton variant="brand" size="sm">下发排产</NvButton>
              </NvPopconfirm>
              <span class="text-xs text-muted-foreground">行内危险操作 / 主操作二次确认</span>
            </NvCard>
          </div>
        </section>

        <!-- ============ 动效 ============ -->
        <section id="motion" v-reveal class="scroll-mt-20">
          <div class="ds-eyebrow">
            <h2 class="text-xl font-semibold tracking-tight">动效</h2>
            <p class="mt-1.5 text-sm text-muted-foreground">
              指数级 ease-out 缓动，150–320ms 区间，列表入场错峰；reduced-motion 下全部降级。
            </p>
          </div>
          <div class="mt-6 grid gap-4 sm:grid-cols-3">
            <NvCard
              v-for="(curve, i) in [
                { name: 'ease-out-quart', cls: 'ease-out-quart', use: '微交互 · 悬停聚焦' },
                { name: 'ease-out-expo', cls: 'ease-out-expo', use: '入场揭示 · 覆盖层' },
                { name: 'ease-in-out-quart', cls: 'ease-in-out-quart', use: '状态切换 · 位移' },
              ]"
              :key="curve.name"
              v-reveal="i"
              class="ds-motion-card group p-5"
            >
              <div class="flex items-center gap-2">
                <ActivityIcon class="size-4 text-brand" aria-hidden="true" />
                <span class="font-mono text-xs">{{ curve.name }}</span>
              </div>
              <div class="mt-4 h-1.5 w-full overflow-hidden rounded-full bg-muted">
                <span
                  class="ds-motion-bar block h-full w-1/3 rounded-full bg-brand"
                  :class="curve.cls"
                />
              </div>
              <p class="mt-3 text-sm text-muted-foreground">{{ curve.use }}</p>
            </NvCard>
          </div>
          <p class="mt-4 flex items-center gap-2 text-sm text-muted-foreground">
            <LayersIcon class="size-4" aria-hidden="true" />
            悬停卡片查看缓动；当前
            <span class="font-medium text-foreground">{{
              reduceMotion ? '已开启减弱动效' : '动效启用中'
            }}</span
            >。
          </p>
        </section>
      </main>

      <footer class="border-t border-border/70">
        <div class="mx-auto flex max-w-6xl flex-col gap-1 px-6 py-10">
          <p class="text-sm font-medium">Nerv-IIP 设计系统 v2</p>
          <p class="text-sm text-muted-foreground">
            令牌单一来源于 <code class="font-mono text-xs">@nerv-iip/ui</code>，原版 shadcn
            零改动，定制走复制重建。
          </p>
        </div>
      </footer>

      <NvCommand v-model:open="cmdOpen" :groups="cmdGroups" @select="onCommandSelect" />
      <NvNotifierHost />
    </div>
  </NvTooltipProvider>
</template>

<style scoped>
@layer app {
  .ds-hero-glow {
    position: absolute;
    inset: 0;
    background:
      radial-gradient(
        60% 70% at 15% 0%,
        color-mix(in oklch, var(--nv-brand) 14%, transparent),
        transparent 70%
      ),
      radial-gradient(
        50% 60% at 100% 10%,
        color-mix(in oklch, var(--nv-brand) 9%, transparent),
        transparent 65%
      );
    pointer-events: none;
  }

  .ds-eyebrow {
    position: relative;
    padding-left: 0.875rem;
  }
  .ds-eyebrow::before {
    content: '';
    position: absolute;
    left: 0;
    top: 0.25rem;
    bottom: 0.25rem;
    width: 2px;
    border-radius: 9999px;
    background: var(--nv-brand);
  }

  .reveal-init {
    opacity: 0;
    transform: translateY(14px);
    will-change: opacity, transform;
  }
  .reveal-in {
    opacity: 1;
    transform: none;
    transition:
      opacity 0.55s var(--nv-ease-out-expo),
      transform 0.55s var(--nv-ease-out-expo);
    transition-delay: var(--reveal-delay, 0ms);
  }

  .ds-motion-bar {
    transform: translateX(0);
    transition: transform 1.1s var(--nv-ease-out-quart);
  }
  .ds-motion-card:hover .ds-motion-bar {
    transform: translateX(200%);
  }
  .ds-motion-card:hover .ds-motion-bar.ease-out-expo {
    transition-timing-function: var(--nv-ease-out-expo);
  }
  .ds-motion-card:hover .ds-motion-bar.ease-in-out-quart {
    transition-timing-function: var(--nv-ease-in-out-quart);
  }

  @media (prefers-reduced-motion: reduce) {
    .reveal-init {
      opacity: 1;
      transform: none;
    }
    .ds-motion-bar {
      transition: none;
    }
  }
}
</style>
