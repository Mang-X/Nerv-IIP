<script setup lang="ts">
import { BadgePro, messagePro, NotifierHost, StatusBadgePro, ThemeToggle } from '@nerv-iip/ui'
import type {
  ActionItem,
  MobileTabItem,
  PickerOption,
  StepItem,
  SwipeAction,
  TabItem,
} from '@nerv-iip/ui-mobile'
import {
  ActionSheet,
  AppShellMobile,
  Badge,
  BottomSheet,
  Cell,
  CellGroup,
  Collapse,
  Empty,
  Fab,
  InfiniteList,
  ListRow,
  MobileButton,
  MobileCheckbox,
  MobileDatePicker,
  MobileDialog,
  MobileGrid,
  MobileInput,
  MobileRadioGroup,
  MobileRadioItem,
  MobileSwitch,
  MobileTabs,
  MobileToast,
  NavBar,
  NoticeBar,
  Picker,
  PullRefresh,
  Result,
  ScanBar,
  SearchBar,
  Steps,
  Stepper,
  SwipeCell,
  TabBar,
  VirtualList,
} from '@nerv-iip/ui-mobile'
import type { FabAction, GridItem } from '@nerv-iip/ui-mobile'
import {
  BatteryFullIcon,
  BellIcon,
  BoxesIcon,
  ChartColumnIcon,
  ClipboardListIcon,
  PrinterIcon,
  ScanLineIcon,
  SignalHighIcon,
  SplitIcon,
  TruckIcon,
  UserIcon,
  UsersIcon,
  WifiIcon,
  WrenchIcon,
  XCircleIcon,
} from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

// 页面标题由 VitePress frontmatter 提供（原 definePage 已移除）

// 状态栏时钟（设备外观框用）
const clock = ref('9:41')
let timer: ReturnType<typeof setInterval> | undefined
function tick() {
  clock.value = new Date().toLocaleTimeString('zh-CN', {
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}
onMounted(() => {
  tick()
  timer = setInterval(tick, 10_000)
})
onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})

const search = ref('')
const qty = ref(12)
const lockMaterial = ref(true)
const inspect = ref('std')
const sheetOpen = ref(false)
const confirmOpen = ref(false)
const dangerOpen = ref(false)
const scanActive = ref(false)

// 宫格 Grid
const gridItems: GridItem[] = [
  { key: 'wo', icon: ClipboardListIcon, text: '工单', badge: 12 },
  { key: 'scan', icon: ScanLineIcon, text: '扫码' },
  { key: 'mtl', icon: BoxesIcon, text: '物料', badge: true },
  { key: 'report', icon: ChartColumnIcon, text: '报工' },
  { key: 'device', icon: WrenchIcon, text: '设备' },
  { key: 'plan', icon: TruckIcon, text: '排产' },
  { key: 'team', icon: UsersIcon, text: '班组' },
  { key: 'alert', icon: BellIcon, text: '告警', badge: 3 },
]
function onGrid(item: GridItem) {
  messagePro.info(`打开「${item.text}」`)
}

// Fab 悬浮按钮
const fabActions: FabAction[] = [
  { key: 'scan', icon: ScanLineIcon, text: '扫码入库' },
  { key: 'wo', icon: ClipboardListIcon, text: '新建工单' },
  { key: 'repair', icon: WrenchIcon, text: '设备报修' },
]
function onFabSelect(action: FabAction) {
  messagePro.success(`已触发：${action.text}`)
}

// Toast 居中提示
const toastShow = ref(false)
const toastType = ref<'text' | 'loading' | 'success' | 'error'>('text')
const toastMsg = ref('已保存')
function fireToast(type: 'text' | 'success' | 'error', msg: string) {
  toastType.value = type
  toastMsg.value = msg
  toastShow.value = true
}
const loadingToast = ref(false)
function runLoadingToast() {
  loadingToast.value = true
  window.setTimeout(() => {
    loadingToast.value = false
    fireToast('success', '提交成功')
  }, 1600)
}
const scans = ref<string[]>(['MTL-7782-0034'])

const tabDemo = ref('orders')
const demoTabs: TabItem[] = [
  { value: 'orders', label: '工单', icon: ClipboardListIcon },
  { value: 'scan', label: '扫码', icon: ScanLineIcon },
  { value: 'me', label: '我的', icon: UserIcon },
]

const searchKw = ref('')
const topTab = ref('doing')
const topTabs: MobileTabItem[] = [
  { value: 'all', label: '全部' },
  { value: 'doing', label: '进行中' },
  { value: 'done', label: '已完成' },
  { value: 'blocked', label: '阻塞' },
]
const checkA = ref(true)
const checkB = ref(false)
const procSteps: StepItem[] = [
  { label: '下料' },
  { label: '加工', note: '进行中' },
  { label: '质检' },
  { label: '入库' },
]
const actionOpen = ref(false)
const actions: ActionItem[] = [
  { label: '拆分工单', value: 'split' },
  { label: '补打标签', value: 'reprint' },
  { label: '报告异常', value: 'fault', danger: true },
]
function onAction(value: string) {
  messagePro.info(`操作：${value}`)
}

const swipeActions: SwipeAction[] = [
  { label: '完工', value: 'finish', tone: 'brand' },
  { label: '暂停', value: 'pause' },
  { label: '删除', value: 'remove', tone: 'danger' },
]
const swipeRows = [
  { code: 'WO-2406-0421', product: '齿轮箱端盖' },
  { code: 'WO-2406-0426', product: '液压阀体 V3' },
]
function onSwipe(value: string) {
  messagePro.info(`侧滑操作：${value}`)
}

const pickerOpen = ref(false)
const pickerLine = ref('line-a')
const pickerOptions: PickerOption[] = [
  { label: 'A 线 · 精密加工', value: 'line-a' },
  { label: 'B 线 · 锻压', value: 'line-b' },
  { label: 'C 线 · 总装', value: 'line-c' },
  { label: 'D 线 · 热处理', value: 'line-d' },
  { label: 'E 线 · 喷涂', value: 'line-e' },
]
const pickerLabel = computed(
  () => pickerOptions.find((o) => o.value === pickerLine.value)?.label ?? '请选择',
)

const dateOpen = ref(false)
const dateVal = ref('2026-06-18')

const refreshing = ref(false)
const refreshList = ref([
  '齿轮箱端盖 · WO-2406-0421',
  '液压阀体 V3 · WO-2406-0426',
  '电机定子叠片 · WO-2406-0430',
  '前桥壳体 A2 · WO-2406-0413',
  '转向节 L · WO-2406-0419',
])
let refreshSeq = 0
function onRefresh() {
  window.setTimeout(() => {
    refreshList.value.unshift(`新到工单 · WO-2406-05${10 + refreshSeq++}`)
    refreshing.value = false
    messagePro.success('已刷新')
  }, 1200)
}

// 加载更多
const infLoading = ref(false)
const infFinished = ref(false)
const infList = ref(Array.from({ length: 8 }, (_, i) => i + 1))
function onLoadMore() {
  window.setTimeout(() => {
    const next = infList.value.length
    for (let i = 1; i <= 8; i++) infList.value.push(next + i)
    infLoading.value = false
    if (infList.value.length >= 32) infFinished.value = true
  }, 900)
}

// 虚拟滚动：2000 条
const bigList = Array.from({ length: 2000 }, (_, i) => ({
  id: i + 1,
  code: `WO-2406-${String(i + 1).padStart(4, '0')}`,
  qty: ((i * 37) % 900) + 100,
}))

function onScan(value: string) {
  scans.value.unshift(value)
  messagePro.success(`已扫码 ${value}`)
}
function fireMessage(kind: 'success' | 'info' | 'warning' | 'error') {
  const map = {
    success: () => messagePro.success('报工成功'),
    info: () => messagePro.info('已同步网关'),
    warning: () => messagePro.warning('库存接近下限'),
    error: () => messagePro.error('网络中断'),
  }
  map[kind]()
}
</script>

<template>
  <div class="ds-phone-wrap">
    <div class="ds-phone ds-m">
      <!-- 状态栏（仅大屏预览显示；真机由系统绘制） -->
      <div class="ds-statusbar">
        <span class="font-semibold tabular-nums">{{ clock }}</span>
        <span class="ds-island" aria-hidden="true" />
        <span class="flex items-center gap-1.5">
          <SignalHighIcon class="size-4" aria-hidden="true" />
          <WifiIcon class="size-4" aria-hidden="true" />
          <BatteryFullIcon class="size-5" aria-hidden="true" />
        </span>
      </div>

      <AppShellMobile class="ds-m h-auto min-h-0 flex-1">
        <template #header>
          <NavBar title="移动控件">
            <template #right><ThemeToggle /></template>
          </NavBar>
        </template>

        <div class="space-y-7 py-4">
          <!-- 按钮 -->
          <section id="m-basic">
            <p class="ds-m-eyebrow">按钮 MobileButton</p>
            <div class="space-y-3 px-3">
              <div class="flex flex-wrap items-center gap-2">
                <MobileButton variant="primary">主操作</MobileButton>
                <MobileButton variant="default">次要</MobileButton>
                <MobileButton variant="outline">描边</MobileButton>
                <MobileButton variant="text">文字</MobileButton>
                <MobileButton variant="danger">删除</MobileButton>
              </div>
              <div class="flex flex-wrap items-center gap-2">
                <MobileButton variant="primary" size="sm">小号</MobileButton>
                <MobileButton variant="primary" size="md">中号</MobileButton>
                <MobileButton variant="primary" size="lg">大号</MobileButton>
              </div>
              <MobileButton variant="primary" size="lg" block>整宽主按钮</MobileButton>
            </div>
          </section>

          <!-- 单元格 -->
          <section>
            <p class="ds-m-eyebrow">单元格 Cell</p>
            <div class="px-3">
              <CellGroup>
                <Cell title="工单号" value="WO-2406-0413" />
                <Cell title="产品" value="前桥壳体 A2" />
                <Cell title="工艺路线" note="3 道工序" arrow @click="messagePro.info('查看工艺')" />
                <Cell title="加急插单">
                  <template #value><MobileSwitch v-model="lockMaterial" /></template>
                </Cell>
              </CellGroup>
            </div>
          </section>

          <!-- 表单 -->
          <section id="m-form">
            <p class="ds-m-eyebrow">表单控件</p>
            <div class="space-y-4 px-3">
              <MobileInput v-model="search" placeholder="搜索工单 / 物料">
                <template #leading><ScanLineIcon aria-hidden="true" /></template>
              </MobileInput>
              <div
                class="flex items-center justify-between rounded-xl border border-border bg-card px-4 py-3"
              >
                <span class="text-[15px]">报工数量</span>
                <Stepper v-model="qty" :min="1" :max="999" />
              </div>
              <div>
                <p class="mb-2 px-1 text-sm text-muted-foreground">质检策略</p>
                <MobileRadioGroup v-model="inspect">
                  <MobileRadioItem value="std">标准全检</MobileRadioItem>
                  <MobileRadioItem value="sample">抽样检验</MobileRadioItem>
                  <MobileRadioItem value="exempt">免检放行</MobileRadioItem>
                </MobileRadioGroup>
              </div>
            </div>
          </section>

          <!-- 标签与状态 -->
          <section>
            <p class="ds-m-eyebrow">标签与状态</p>
            <div class="flex flex-wrap gap-2 px-3">
              <BadgePro variant="brand">品牌</BadgePro>
              <BadgePro variant="success">已完成</BadgePro>
              <BadgePro variant="warning">待处理</BadgePro>
              <StatusBadgePro value="running" pulse />
              <StatusBadgePro value="blocked" />
            </div>
          </section>

          <!-- 通知条 -->
          <section>
            <p class="ds-m-eyebrow">通知条 NoticeBar</p>
            <div class="space-y-2">
              <NoticeBar tone="info">今日计划已重排，受影响工单 6 张</NoticeBar>
              <NoticeBar tone="warning">B 线物料不足：液压阀体 V3 缺口 452 件</NoticeBar>
              <NoticeBar tone="danger">WC-ASM-04 设备报警，请尽快处理</NoticeBar>
            </div>
          </section>

          <!-- 列表行 -->
          <section>
            <p class="ds-m-eyebrow">列表行 ListRow</p>
            <div class="overflow-hidden border-y border-border">
              <ListRow
                title="齿轮箱端盖"
                subtitle="WO-2406-0421 · 320 件"
                @select="messagePro.info('打开工单')"
              />
              <ListRow
                title="液压阀体 V3"
                subtitle="WO-2406-0426 · 640 件"
                @select="messagePro.info('打开工单')"
              />
            </div>
          </section>

          <!-- 标签栏 -->
          <section>
            <p class="ds-m-eyebrow">标签栏 TabBar</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border bg-card">
              <TabBar v-model="tabDemo" :items="demoTabs" />
            </div>
            <p class="mt-2 px-4 text-sm text-muted-foreground">当前：{{ tabDemo }}</p>
          </section>

          <!-- 反馈 -->
          <section id="m-feedback">
            <p class="ds-m-eyebrow">反馈 Message / 抽屉</p>
            <div class="space-y-3 px-3">
              <div class="flex flex-wrap gap-2">
                <MobileButton variant="default" size="sm" @click="fireMessage('success')"
                  >成功</MobileButton
                >
                <MobileButton variant="default" size="sm" @click="fireMessage('info')"
                  >信息</MobileButton
                >
                <MobileButton variant="default" size="sm" @click="fireMessage('warning')"
                  >预警</MobileButton
                >
                <MobileButton variant="default" size="sm" @click="fireMessage('error')"
                  >错误</MobileButton
                >
              </div>
              <MobileButton variant="primary" size="md" block @click="sheetOpen = true"
                >打开底部抽屉</MobileButton
              >
              <div class="flex gap-2">
                <MobileButton variant="default" size="md" class="flex-1" @click="confirmOpen = true"
                  >居中确认</MobileButton
                >
                <MobileButton variant="danger" size="md" class="flex-1" @click="dangerOpen = true"
                  >危险确认</MobileButton
                >
              </div>
            </div>
          </section>

          <!-- 宫格 -->
          <section>
            <p class="ds-m-eyebrow">宫格 MobileGrid</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border bg-card">
              <MobileGrid :items="gridItems" :columns="4" @select="onGrid" />
            </div>
          </section>

          <!-- 悬浮按钮 -->
          <section>
            <p class="ds-m-eyebrow">悬浮按钮 Fab</p>
            <div
              class="relative mx-3 h-64 overflow-hidden rounded-xl border border-border bg-background"
            >
              <div class="space-y-2 p-3">
                <div v-for="n in 4" :key="n" class="h-12 rounded-lg bg-card" />
              </div>
              <Fab :actions="fabActions" @select="onFabSelect" />
            </div>
            <p class="mt-2 px-4 text-xs text-muted-foreground">
              点击展开速拨动作；锚定容器右下角。
            </p>
          </section>

          <!-- 居中提示 -->
          <section>
            <p class="ds-m-eyebrow">居中提示 MobileToast</p>
            <div class="grid grid-cols-2 gap-2 px-3">
              <MobileButton variant="default" size="md" @click="fireToast('text', '已复制单号')"
                >文字</MobileButton
              >
              <MobileButton variant="default" size="md" @click="fireToast('success', '报工成功')"
                >成功</MobileButton
              >
              <MobileButton variant="default" size="md" @click="fireToast('error', '网络异常')"
                >失败</MobileButton
              >
              <MobileButton variant="default" size="md" @click="runLoadingToast"
                >加载（带遮罩）</MobileButton
              >
            </div>
          </section>

          <!-- 结果页 -->
          <section>
            <p class="ds-m-eyebrow">结果页 Result</p>
            <div class="mx-3 rounded-xl border border-border bg-card">
              <Result
                status="success"
                title="本班产出已同步"
                description="已完成 4,210 件，良率 99.2%。"
              />
            </div>
          </section>

          <!-- 扫码 -->
          <section>
            <p class="ds-m-eyebrow">扫码 ScanBar</p>
            <div class="space-y-3 px-3">
              <ScanBar :active="scanActive" placeholder="对准条码 / 二维码" @scan="onScan" />
              <CellGroup>
                <Cell v-for="(s, i) in scans" :key="`${s}-${i}`" :title="s" note="物料条码" />
              </CellGroup>
            </div>
          </section>

          <!-- 搜索栏 -->
          <section>
            <p class="ds-m-eyebrow">搜索栏 SearchBar</p>
            <SearchBar v-model="searchKw" cancelable placeholder="搜索工单 / 物料 / 设备" />
          </section>

          <!-- 顶部标签 -->
          <section>
            <p class="ds-m-eyebrow">顶部标签 MobileTabs</p>
            <MobileTabs v-model="topTab" :items="topTabs" />
            <p class="mt-3 px-4 text-sm text-muted-foreground">当前分类：{{ topTab }}</p>
          </section>

          <!-- 步骤条 -->
          <section>
            <p class="ds-m-eyebrow">步骤条 Steps</p>
            <div class="px-3">
              <Steps :steps="procSteps" :current="1" />
            </div>
          </section>

          <!-- 复选框 -->
          <section>
            <p class="ds-m-eyebrow">复选框 MobileCheckbox</p>
            <div class="px-4">
              <MobileCheckbox v-model="checkA">首检合格后转批量</MobileCheckbox>
              <MobileCheckbox v-model="checkB">完工自动生成入库单</MobileCheckbox>
            </div>
          </section>

          <!-- 角标 -->
          <section>
            <p class="ds-m-eyebrow">角标 Badge</p>
            <div class="flex items-center gap-7 px-5">
              <Badge :count="5">
                <BellIcon class="size-6 text-foreground" aria-hidden="true" />
              </Badge>
              <Badge :count="128" :max="99">
                <ClipboardListIcon class="size-6 text-foreground" aria-hidden="true" />
              </Badge>
              <Badge dot>
                <UserIcon class="size-6 text-foreground" aria-hidden="true" />
              </Badge>
            </div>
          </section>

          <!-- 动作面板 -->
          <section>
            <p class="ds-m-eyebrow">动作面板 ActionSheet</p>
            <div class="px-3">
              <MobileButton variant="default" size="md" block @click="actionOpen = true">
                打开动作面板
              </MobileButton>
            </div>
          </section>

          <!-- 折叠面板 -->
          <section>
            <p class="ds-m-eyebrow">折叠面板 Collapse</p>
            <div
              class="mx-3 divide-y divide-border overflow-hidden rounded-xl border border-border"
            >
              <Collapse title="工艺参数" :open="true">
                主轴转速 2400 rpm · 进给 180 mm/min · 冷却液开
              </Collapse>
              <Collapse title="物料清单"> 铝棒 6061-T6 ×1 · 密封圈 ×2 · 标准件若干 </Collapse>
              <Collapse title="质检记录"> 首检合格（08:12 张伟）· 巡检 2 次无异常 </Collapse>
            </div>
          </section>

          <!-- 侧滑操作 -->
          <section>
            <p class="ds-m-eyebrow">侧滑操作 SwipeCell</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border">
              <SwipeCell
                v-for="row in swipeRows"
                :key="row.code"
                :actions="swipeActions"
                class="border-b border-border last:border-0"
                @select="onSwipe"
              >
                <div class="flex min-h-touch items-center gap-3 px-4 py-3">
                  <div class="min-w-0 flex-1">
                    <div class="text-[15px]">{{ row.product }}</div>
                    <div class="text-sm text-muted-foreground">{{ row.code }}</div>
                  </div>
                  <span class="shrink-0 text-xs text-muted-foreground">← 左滑</span>
                </div>
              </SwipeCell>
            </div>
          </section>

          <!-- 滚轮选择 -->
          <section>
            <p class="ds-m-eyebrow">滚轮选择 Picker</p>
            <div class="px-3">
              <CellGroup>
                <Cell title="目标产线" :value="pickerLabel" arrow @click="pickerOpen = true" />
                <Cell title="计划日期" :value="dateVal" arrow @click="dateOpen = true" />
              </CellGroup>
            </div>
          </section>

          <!-- 下拉刷新 -->
          <section>
            <p class="ds-m-eyebrow">下拉刷新 PullRefresh</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border">
              <PullRefresh v-model="refreshing" class="h-56" @refresh="onRefresh">
                <Cell v-for="(item, i) in refreshList" :key="`${item}-${i}`" :title="item" />
              </PullRefresh>
            </div>
            <p class="mt-2 px-4 text-xs text-muted-foreground">
              在列表顶部下拉以刷新（追加新工单）。
            </p>
          </section>

          <!-- 加载更多 -->
          <section>
            <p class="ds-m-eyebrow">加载更多 InfiniteList</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border">
              <InfiniteList
                v-model="infLoading"
                :finished="infFinished"
                class="h-56"
                @load="onLoadMore"
              >
                <Cell
                  v-for="n in infList"
                  :key="n"
                  :title="`工单条目 #${n}`"
                  note="滚动到底自动加载"
                />
              </InfiniteList>
            </div>
          </section>

          <!-- 虚拟滚动 -->
          <section>
            <p class="ds-m-eyebrow">虚拟滚动 VirtualList</p>
            <p class="mb-2 px-4 text-xs text-muted-foreground">2000 条数据，仅渲染可视区行。</p>
            <div class="mx-3 overflow-hidden rounded-xl border border-border">
              <VirtualList :items="bigList" :item-height="56" class="h-64">
                <template #default="{ item }">
                  <div class="flex h-full items-center gap-3 border-b border-border px-4">
                    <span class="font-mono text-sm text-muted-foreground">{{ item.code }}</span>
                    <span class="ml-auto text-sm tabular-nums">{{ item.qty }} 件</span>
                  </div>
                </template>
              </VirtualList>
            </div>
          </section>

          <!-- 空状态 -->
          <section>
            <p class="ds-m-eyebrow">空状态 Empty</p>
            <div class="mx-3 rounded-xl border border-border bg-card">
              <Empty description="暂无待处理工单">
                <MobileButton variant="primary" size="sm">去接单</MobileButton>
              </Empty>
            </div>
          </section>
        </div>
      </AppShellMobile>

      <div class="ds-home-indicator" aria-hidden="true" />
    </div>
  </div>

  <BottomSheet v-model:open="sheetOpen" title="更多操作">
    <div class="space-y-2 py-1">
      <MobileButton
        variant="default"
        size="lg"
        block
        class="justify-start gap-3"
        @click="sheetOpen = false"
      >
        <SplitIcon class="size-5" aria-hidden="true" />拆分工单
      </MobileButton>
      <MobileButton
        variant="default"
        size="lg"
        block
        class="justify-start gap-3"
        @click="sheetOpen = false"
      >
        <PrinterIcon class="size-5" aria-hidden="true" />补打标签
      </MobileButton>
      <MobileButton
        variant="default"
        size="lg"
        block
        class="justify-start gap-3"
        @click="sheetOpen = false"
      >
        <WrenchIcon class="size-5" aria-hidden="true" />设备维护
      </MobileButton>
      <MobileButton
        variant="danger"
        size="lg"
        block
        class="justify-start gap-3"
        @click="sheetOpen = false"
      >
        <XCircleIcon class="size-5" aria-hidden="true" />报告异常
      </MobileButton>
    </div>
  </BottomSheet>

  <ActionSheet v-model:open="actionOpen" title="工单操作" :actions="actions" @select="onAction" />

  <Picker
    v-model:open="pickerOpen"
    v-model="pickerLine"
    :options="pickerOptions"
    title="选择产线"
  />

  <MobileDatePicker v-model:open="dateOpen" v-model="dateVal" title="计划日期" />

  <MobileDialog
    v-model:open="confirmOpen"
    title="下发到产线？"
    description="工单 WO-2406-0413 将下发至 A 线排队，物料即时锁定。"
    confirm-text="下发"
    @confirm="messagePro.success('已下发到 A 线')"
  />
  <MobileDialog
    v-model:open="dangerOpen"
    title="确认作废该工单？"
    description="作废后不可恢复，已领用物料需手动退库。"
    confirm-text="作废"
    confirm-tone="danger"
    @confirm="messagePro.error('工单已作废')"
  />

  <MobileToast v-model:show="toastShow" :type="toastType" :message="toastMsg" />
  <MobileToast v-model:show="loadingToast" type="loading" message="提交中…" overlay />

  <NotifierHost />
</template>

<style scoped>
.ds-m-eyebrow {
  position: relative;
  margin: 0 0 0.75rem 1rem;
  padding-left: 0.625rem;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--muted-foreground);
}
.ds-m-eyebrow::before {
  content: '';
  position: absolute;
  left: 0;
  top: 0.15rem;
  bottom: 0.15rem;
  width: 2px;
  border-radius: 9999px;
  background: var(--brand);
}

/* ---- Phone frame ----
   Real phone (<sm): full-bleed app, OS draws the status bar.
   Larger screens: a centered device bezel with a mock status bar so the kit
   reads as a native app preview. */
.ds-phone-wrap {
  display: flex;
  justify-content: center;
  min-height: 100dvh;
}
.ds-phone {
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100dvh;
  background: var(--background);
  color: var(--foreground);
  overflow: hidden;
}
.ds-statusbar {
  display: none;
}
.ds-home-indicator {
  display: none;
}

@media (min-width: 640px) {
  .ds-phone-wrap {
    align-items: center;
    padding: 2rem 1rem;
    background:
      radial-gradient(
        60% 50% at 50% 0%,
        color-mix(in oklch, var(--brand) 10%, transparent),
        transparent 70%
      ),
      var(--muted);
  }
  .ds-phone {
    width: 390px;
    height: 844px;
    border-radius: 3.25rem;
    border: 11px solid color-mix(in oklch, var(--foreground) 92%, black);
    box-shadow:
      0 1px 0 1px color-mix(in oklch, var(--foreground) 70%, black) inset,
      0 40px 90px -30px rgb(0 0 0 / 0.55);
  }
  .ds-statusbar {
    position: relative;
    z-index: 30;
    display: flex;
    align-items: center;
    justify-content: space-between;
    height: 52px;
    padding: 0 1.75rem 0 2rem;
    font-size: 0.875rem;
    color: var(--foreground);
  }
  .ds-island {
    position: absolute;
    left: 50%;
    top: 8px;
    transform: translateX(-50%);
    width: 84px;
    height: 26px;
    border-radius: 9999px;
    background: color-mix(in oklch, var(--foreground) 92%, black);
  }
  .ds-home-indicator {
    display: block;
    height: 22px;
    flex-shrink: 0;
  }
  .ds-home-indicator::after {
    content: '';
    display: block;
    width: 134px;
    height: 5px;
    margin: 8px auto 0;
    border-radius: 9999px;
    background: color-mix(in oklch, var(--foreground) 55%, transparent);
  }
}
</style>
